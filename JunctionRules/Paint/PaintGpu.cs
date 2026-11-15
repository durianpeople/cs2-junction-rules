using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Colossal.Mathematics;
using Game.Prefabs;
using Game.Rendering;
using JunctionRules.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace JunctionRules.Paint
{
    /// <summary>
    /// The paint test's own GPU objects: a material instance made from the game's rendered-area material, the
    /// "Area triangle volume" prism mesh, and the three buffers the surface shader reads. Mirrors
    /// Game.Rendering.AreaBatchSystem (material set-up, lines 2083-2120) and AreaRenderSystem (draw, 182-211; mesh,
    /// 221-262) of the 13 Nov 2026 decompile; nothing of the game's own batches is read or changed.
    /// 0.3.1-probe (JR-T-003c): diagnostic switches (PaintDiagParams) and a second, non-decal draw path: the area tool
    /// overlay (AreaTypePrefab Surface material, shader AreaShader, buffer colossal_AreaTriangleBuffer; mirrors
    /// AreaBufferSystem 300-338 / 793-802 and AreaRenderSystem 150-178).
    /// </summary>
    internal sealed class PaintGpu : IDisposable
    {
        private static readonly int s_AreaBatchBuffer = Shader.PropertyToID("colossal_AreaBatchBuffer");
        private static readonly int s_AreaBatchColors = Shader.PropertyToID("colossal_AreaBatchColors");
        private static readonly int s_VisibleIndices = Shader.PropertyToID("colossal_VisibleIndices");
        private static readonly int s_AreaTriangleBuffer = Shader.PropertyToID("colossal_AreaTriangleBuffer");
        private static readonly int s_BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int s_BaseColorMap = Shader.PropertyToID("_BaseColorMap");
        private static readonly int s_NormalMap = Shader.PropertyToID("_NormalMap");
        private static readonly int s_MaskMap = Shader.PropertyToID("_MaskMap");
        private static readonly int s_Metallic = Shader.PropertyToID("_Metallic");
        private static readonly int s_Smoothness = Shader.PropertyToID("_Smoothness");
        private static readonly int s_NormalOpacity = Shader.PropertyToID("_NormalOpacity");
        private static readonly int s_UVScale = Shader.PropertyToID("colossal_UVScale");
        private static readonly int s_EdgeNormal = Shader.PropertyToID("colossal_EdgeNormal");
        private static readonly int s_EdgeFadeRange = Shader.PropertyToID("colossal_EdgeFadeRange");
        private static readonly int s_EdgeNoise = Shader.PropertyToID("colossal_EdgeNoise");
        private static readonly int s_AreaParameters = Shader.PropertyToID("colossal_AreaParameters");
        private static readonly int s_DecalLayerMask = Shader.PropertyToID("colossal_DecalLayerMask");

        /// <summary>Layout of AreaBufferSystem's private AreaTriangleData (the overlay shader's buffer), stride 124.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct OverlayTriangle
        {
            public float3 m_APos, m_BPos, m_CPos;
            public float2 m_APrevXZ, m_BPrevXZ, m_CPrevXZ, m_ANextXZ, m_BNextXZ, m_CNextXZ, m_YMinMax;
            public float4 m_FillColor, m_EdgeColor;
        }

        private Material m_Material;          // decal path (null when not drawn)
        private Material m_Overlay;           // overlay path (null when not drawn)
        private Mesh m_Mesh;
        private Texture2D m_White, m_Normal, m_Mask;
        private ComputeBuffer m_Triangles, m_Colors, m_OverlayTris;
        private GraphicsBuffer m_Indices, m_Args;
        private Bounds m_Bounds;
        private int m_Count;
        private bool m_OptionalReported;
        private bool m_DrawReported;
        private PaintDiagParams m_Diag;

        private PaintGpu() { }

        public string GpuKey => m_Diag.GpuKey;

        /// <summary>Makes the material(s) and mesh. Returns null with a clear reason when something the shader needs is missing.</summary>
        public static PaintGpu Create(PrefabSystem prefabSystem, EntityManager em, EntityQuery areasConfigQuery, PaintDiagParams diag, out string failure)
        {
            failure = null;
            var gpu = new PaintGpu { m_Diag = diag };
            try
            {
                if (diag.DrawDecal)
                {
                    Material baseMat = DecalSource(prefabSystem, em, areasConfigQuery, diag.MaterialFrom, out failure);
                    if (baseMat == null) { gpu.Dispose(); return null; }
                    gpu.m_Material = new Material(baseMat) { name = "Junction Rules yellow box paint test" };
                    gpu.m_Material.renderQueue = diag.RenderQueue >= 0 ? diag.RenderQueue : baseMat.shader.renderQueue;
                    foreach (var (id, name) in new[] { (s_DecalLayerMask, "colossal_DecalLayerMask"), (s_AreaParameters, "colossal_AreaParameters"), (s_BaseColor, "_BaseColor") })
                    {
                        if (!gpu.m_Material.HasProperty(id)) { failure = $"shader '{baseMat.shader.name}' has no property {name}"; gpu.Dispose(); return null; }
                    }
                    bool meshPassWasOn = gpu.m_Material.GetShaderPassEnabled("DBufferMesh");
                    if (!meshPassWasOn) gpu.m_Material.SetShaderPassEnabled("DBufferMesh", true);
                    Mod.log.Info($"yellow box paint test: decal material from '{baseMat.name}' (shader '{baseMat.shader.name}', render queue {gpu.m_Material.renderQueue}, shader queue {baseMat.shader.renderQueue}); {PassReport(gpu.m_Material)}; DBufferMesh pass {(meshPassWasOn ? "enabled" : "was DISABLED on the material, enabled now")}");
                }
                if (diag.DrawOverlay)
                {
                    Material ov = OverlaySource(prefabSystem, em, out failure);
                    if (ov == null) { gpu.Dispose(); return null; }
                    gpu.m_Overlay = new Material(ov) { name = "Junction Rules yellow box paint test (overlay)" };
                    if (diag.RenderQueue >= 0) gpu.m_Overlay.renderQueue = diag.RenderQueue;
                    const float mnd = 0.75f;   // AreaUtils.GetMinNodeDistance(AreaType.Surface)
                    gpu.m_Overlay.SetVector(s_AreaParameters, new Vector4(mnd * (1f / 32f), mnd * 0.25f, mnd * 2f, 0f));
                    Mod.log.Info($"yellow box paint test: overlay material from '{ov.name}' (shader '{ov.shader.name}', render queue {gpu.m_Overlay.renderQueue}); {PassReport(gpu.m_Overlay)}");
                }
                gpu.m_Mesh = CreateMesh();
                gpu.m_Args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
                return gpu;
            }
            catch
            {
                gpu.Dispose();
                throw;
            }
        }

        private static Material DecalSource(PrefabSystem prefabSystem, EntityManager em, EntityQuery areasConfigQuery, string from, out string failure)
        {
            failure = null;
            if (!string.IsNullOrEmpty(from))
            {
                var names = new List<string>();
                using (EntityQuery q = em.CreateEntityQuery(ComponentType.ReadOnly<RenderedAreaData>(), ComponentType.ReadOnly<PrefabData>()))
                using (NativeArray<Entity> ents = q.ToEntityArray(Allocator.Temp))
                {
                    foreach (Entity e in ents)
                    {
                        if (!prefabSystem.TryGetPrefab(e, out AreaPrefab ap) || ap == null) continue;
                        names.Add(ap.name);
                        if (!string.Equals(ap.name, from, StringComparison.OrdinalIgnoreCase)) continue;
                        RenderedArea ra = ap.GetComponent<RenderedArea>();
                        if (ra != null && ra.m_Material != null && ra.m_Material.shader != null) return ra.m_Material;
                        Mod.log.Info($"yellow box paint test: area prefab '{ap.name}' has no own material (it uses the areas configuration material)");
                        break;
                    }
                }
                if (!names.Exists(n => string.Equals(n, from, StringComparison.OrdinalIgnoreCase)))
                {
                    names.Sort(StringComparer.Ordinal);
                    Mod.log.Warn($"yellow box paint test: diag.materialFrom '{from}' is no rendered area prefab; using the areas configuration material. Rendered area prefabs: " + string.Join(", ", names));
                }
            }
            if (areasConfigQuery.IsEmptyIgnoreFilter) { failure = "no AreasConfigurationData singleton (areas configuration prefab not loaded)"; return null; }
            AreasConfigurationPrefab cfg = prefabSystem.GetPrefab<AreasConfigurationPrefab>(areasConfigQuery.GetSingletonEntity());
            if (cfg == null) { failure = "AreasConfigurationPrefab not found"; return null; }
            Material baseMat = cfg.m_RenderedAreaMaterial;
            if (baseMat == null) { failure = "AreasConfigurationPrefab.m_RenderedAreaMaterial is null"; return null; }
            if (baseMat.shader == null) { failure = "rendered-area material has no shader"; return null; }
            return baseMat;
        }

        private static Material OverlaySource(PrefabSystem prefabSystem, EntityManager em, out string failure)
        {
            failure = null;
            using (EntityQuery q = em.CreateEntityQuery(ComponentType.ReadOnly<AreaTypeData>(), ComponentType.ReadOnly<PrefabData>()))
            using (NativeArray<Entity> ents = q.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity e in ents)
                {
                    if (!prefabSystem.TryGetPrefab(e, out AreaTypePrefab tp) || tp == null) continue;
                    if (tp.m_Type == Game.Areas.AreaType.Surface && tp.m_Material != null && tp.m_Material.shader != null) return tp.m_Material;
                }
            }
            failure = "no Surface AreaTypePrefab with a material (overlay draw path)";
            return null;
        }

        private static string PassReport(Material m)
        {
            var passes = new List<string>();
            for (int i = 0; i < m.passCount; i++)
            {
                string n = m.GetPassName(i);
                passes.Add(n + (m.GetShaderPassEnabled(n) ? "" : " (off)"));
            }
            return "passes [" + string.Join(", ", passes) + "], keywords [" + string.Join(", ", m.shaderKeywords) + "]";
        }

        /// <summary>Material values, the game's own calls (AreaBatchSystem 2094-2119) with our values.</summary>
        public void ApplyMaterial(PaintRenderParams rp, BoxPaintLayers layers)
        {
            Material m = m_Material;
            if (m == null) return;
            var missing = new List<string>();
            void SetF(int id, string name, float v) { if (m.HasProperty(id)) m.SetFloat(id, v); else missing.Add(name); }
            void SetV(int id, string name, Vector4 v) { if (m.HasProperty(id)) m.SetVector(id, v); else missing.Add(name); }
            void SetT(int id, string name, Texture t) { if (m.HasProperty(id)) m.SetTexture(id, t); else missing.Add(name); }

            int mask = m_Diag.DecalLayerMask >= 0 ? m_Diag.DecalLayerMask : (int)layers;
            m.SetVector(s_AreaParameters, new Vector4((float)rp.Expand, (float)(0.5 * (rp.VolumeBelow + rp.VolumeAbove)), 0f, 0f));
            m.SetFloat(s_DecalLayerMask, math.asfloat(mask));
            m.SetColor(s_BaseColor, new Color((float)rp.Color[0], (float)rp.Color[1], (float)rp.Color[2], (float)rp.Color[3]));
            SetF(s_Metallic, "_Metallic", (float)rp.Metallic);
            SetF(s_Smoothness, "_Smoothness", (float)rp.Smoothness);
            SetF(s_NormalOpacity, "_NormalOpacity", (float)rp.NormalOpacity);
            SetF(s_UVScale, "colossal_UVScale", (float)rp.UVScale);
            SetF(s_EdgeNormal, "colossal_EdgeNormal", (float)rp.EdgeNormal);
            SetV(s_EdgeFadeRange, "colossal_EdgeFadeRange", new Vector4((float)rp.EdgeFadeX, (float)rp.EdgeFadeY, 0f, 0f));
            SetV(s_EdgeNoise, "colossal_EdgeNoise", new Vector4((float)rp.EdgeNoiseX, (float)rp.EdgeNoiseY, 0f, 0f));
            if (rp.PlainTextures)
            {
                if (m_White == null) m_White = Plain(new Color(1f, 1f, 1f, 1f), false);
                if (m_Normal == null) m_Normal = Plain(new Color(0.5f, 0.5f, 1f, 1f), true);
                if (m_Mask == null) m_Mask = Plain(new Color(0f, 1f, 1f, 1f), true);
                SetT(s_BaseColorMap, "_BaseColorMap", m_White);
                SetT(s_NormalMap, "_NormalMap", m_Normal);
                SetT(s_MaskMap, "_MaskMap", m_Mask);
            }
            if (missing.Count > 0 && !m_OptionalReported)
            {
                m_OptionalReported = true;
                Mod.log.Warn("yellow box paint test: shader lacks optional properties (left at the material's values): " + string.Join(", ", missing));
            }
        }

        private static Texture2D Plain(Color c, bool linear)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false, linear) { name = "Junction Rules paint test plain", hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply(false, true);
            return t;
        }

        /// <summary>
        /// Uploads all triangles (one draw for every box). Colour buffer = diag.colorBuffer for every triangle (default
        /// zeros, as for a plain surface); visible indices = all. The overlay buffer (when drawn) gets the same triangles
        /// with the paint colour as fill and edge colour.
        /// </summary>
        public unsafe void Upload(AreaTriangleData[] tris, Bounds3 bounds, PaintRenderParams rp)
        {
            m_Count = tris.Length;
            m_DrawReported = false;   // report the first draw of every new upload
            if (m_Count == 0) return;
            if (m_Triangles == null || m_Triangles.count < m_Count)
            {
                m_Triangles?.Release();
                m_Colors?.Release();
                m_Indices?.Release();
                m_OverlayTris?.Release(); m_OverlayTris = null;
                int cap = Math.Max(64, m_Count * 2);
                m_Triangles = new ComputeBuffer(cap, sizeof(AreaTriangleData));
                m_Colors = new ComputeBuffer(cap, sizeof(Game.Rendering.AreaColorData));
                m_Indices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, 4);
                var idx = new int[cap];
                for (int i = 0; i < cap; i++) idx[i] = i;
                m_Indices.SetData(idx);
                if (m_Overlay != null) m_OverlayTris = new ComputeBuffer(cap, sizeof(OverlayTriangle));
            }
            var col = new Game.Rendering.AreaColorData[m_Triangles.count];
            var cv = new float3((float)m_Diag.ColorBuffer[0], (float)m_Diag.ColorBuffer[1], (float)m_Diag.ColorBuffer[2]);
            for (int i = 0; i < col.Length; i++) col[i].m_Color = cv;
            m_Colors.SetData(col);
            m_Triangles.SetData(tris, 0, 0, m_Count);
            if (m_Overlay != null)
            {
                Color lin = new Color((float)rp.Color[0], (float)rp.Color[1], (float)rp.Color[2], (float)rp.Color[3]).linear;
                var fill = new float4(lin.r, lin.g, lin.b, lin.a);
                var ov = new OverlayTriangle[m_Count];
                for (int i = 0; i < m_Count; i++)
                {
                    AreaTriangleData t = tris[i];
                    ov[i] = new OverlayTriangle
                    {
                        m_APos = t.m_APos, m_BPos = t.m_BPos, m_CPos = t.m_CPos,
                        m_APrevXZ = t.m_APrevXZ, m_BPrevXZ = t.m_BPrevXZ, m_CPrevXZ = t.m_CPrevXZ,
                        m_ANextXZ = t.m_ANextXZ, m_BNextXZ = t.m_BNextXZ, m_CNextXZ = t.m_CNextXZ,
                        m_YMinMax = t.m_YMinMax, m_FillColor = fill, m_EdgeColor = fill,
                    };
                }
                m_OverlayTris.SetData(ov, 0, 0, m_Count);
                m_Overlay.SetBuffer(s_AreaTriangleBuffer, m_OverlayTris);
            }
            BindDecalBuffers();
            var args = new GraphicsBuffer.IndirectDrawIndexedArgs
            {
                indexCountPerInstance = m_Mesh.GetIndexCount(0),
                instanceCount = (uint)m_Count,
                startIndex = m_Mesh.GetIndexStart(0),
                baseVertexIndex = m_Mesh.GetBaseVertex(0),
            };
            m_Args.SetData(new[] { args });
            m_Bounds = RenderingUtils.ToBounds(bounds);
        }

        private void BindDecalBuffers()
        {
            if (m_Material == null || m_Triangles == null) return;
            m_Material.SetBuffer(s_AreaBatchBuffer, m_Triangles);
            m_Material.SetBuffer(s_AreaBatchColors, m_Colors);
            m_Material.SetBuffer(s_VisibleIndices, m_Indices);
        }

        /// <summary>One indirect draw per camera and path (decal: all but preview cameras, as the game does for area batches; overlay: Game and SceneView, as the tool overlay).</summary>
        public void Draw(List<Camera> cameras)
        {
            if (m_Count == 0) return;
            if (m_Diag.SetBuffersEachFrame)
            {
                BindDecalBuffers();
                if (m_Overlay != null && m_OverlayTris != null) m_Overlay.SetBuffer(s_AreaTriangleBuffer, m_OverlayTris);
            }
            bool gameOnly = m_Diag.Cameras == "game";
            int decalDraws = 0, overlayDraws = 0;
            List<string> seen = m_DrawReported ? null : new List<string>();
            foreach (Camera cam in cameras)
            {
                if (cam == null) continue;
                bool isGame = cam.cameraType == CameraType.Game || cam.cameraType == CameraType.SceneView;
                seen?.Add($"'{cam.name}' {cam.cameraType}{(cam.enabled ? "" : " disabled")}");
                if (m_Material != null && cam.cameraType != CameraType.Preview && (!gameOnly || isGame))
                {
                    var rp = new RenderParams(m_Material) { worldBounds = m_Bounds, camera = cam, rendererPriority = m_Diag.RendererPriority };
                    Graphics.RenderMeshIndirect(in rp, m_Mesh, m_Args, 1, 0);
                    decalDraws++;
                }
                if (m_Overlay != null && isGame)
                {
                    var rp = new RenderParams(m_Overlay) { worldBounds = m_Bounds, camera = cam, rendererPriority = m_Diag.RendererPriority };
                    Graphics.RenderMeshIndirect(in rp, m_Mesh, m_Args, 1, 0);
                    overlayDraws++;
                }
            }
            if (seen != null && seen.Count > 0)
            {
                m_DrawReported = true;
                Mod.log.Info($"yellow box paint test: first draw: {m_Count} triangles, bounds centre {m_Bounds.center} size {m_Bounds.size}; cameras [{string.Join(", ", seen)}]; decal draws {decalDraws}, overlay draws {overlayDraws}");
            }
        }

        public void Dispose()
        {
            m_Count = 0;
            m_Triangles?.Release(); m_Triangles = null;
            m_Colors?.Release(); m_Colors = null;
            m_Indices?.Release(); m_Indices = null;
            m_OverlayTris?.Release(); m_OverlayTris = null;
            m_Args?.Release(); m_Args = null;
            if (m_Material != null) { UnityEngine.Object.Destroy(m_Material); m_Material = null; }
            if (m_Overlay != null) { UnityEngine.Object.Destroy(m_Overlay); m_Overlay = null; }
            if (m_Mesh != null) { UnityEngine.Object.Destroy(m_Mesh); m_Mesh = null; }
            if (m_White != null) { UnityEngine.Object.Destroy(m_White); m_White = null; }
            if (m_Normal != null) { UnityEngine.Object.Destroy(m_Normal); m_Normal = null; }
            if (m_Mask != null) { UnityEngine.Object.Destroy(m_Mask); m_Mask = null; }
        }

        /// <summary>The 6-vertex prism the game draws each area triangle with (same layout as AreaRenderSystem.CreateMesh).</summary>
        private static Mesh CreateMesh()
        {
            var v = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, 1f), new Vector3(1f, 1f, 0f),
            };
            var t = new[] { 0, 2, 1, 3, 4, 5, 3, 0, 4, 4, 0, 1, 4, 1, 5, 5, 1, 2, 5, 2, 3, 3, 2, 0 };
            return new Mesh { name = "Junction Rules paint test triangle volume", vertices = v, triangles = t };
        }
    }
}
