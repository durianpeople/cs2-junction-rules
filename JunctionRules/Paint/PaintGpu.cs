using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Prefabs;
using Game.Rendering;
using JunctionRules.Core;
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
    /// </summary>
    internal sealed class PaintGpu : IDisposable
    {
        private static readonly int s_AreaBatchBuffer = Shader.PropertyToID("colossal_AreaBatchBuffer");
        private static readonly int s_AreaBatchColors = Shader.PropertyToID("colossal_AreaBatchColors");
        private static readonly int s_VisibleIndices = Shader.PropertyToID("colossal_VisibleIndices");
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

        private Material m_Material;
        private Mesh m_Mesh;
        private Texture2D m_White, m_Normal, m_Mask;
        private ComputeBuffer m_Triangles, m_Colors;
        private GraphicsBuffer m_Indices, m_Args;
        private Bounds m_Bounds;
        private int m_Count;
        private bool m_OptionalReported;

        private PaintGpu() { }

        /// <summary>Makes the material and mesh. Returns null with a clear reason when something the shader needs is missing.</summary>
        public static PaintGpu Create(PrefabSystem prefabSystem, EntityQuery areasConfigQuery, out string failure)
        {
            failure = null;
            if (areasConfigQuery.IsEmptyIgnoreFilter) { failure = "no AreasConfigurationData singleton (areas configuration prefab not loaded)"; return null; }
            Entity cfgEntity = areasConfigQuery.GetSingletonEntity();
            AreasConfigurationPrefab cfg = prefabSystem.GetPrefab<AreasConfigurationPrefab>(cfgEntity);
            if (cfg == null) { failure = "AreasConfigurationPrefab not found"; return null; }
            Material baseMat = cfg.m_RenderedAreaMaterial;
            if (baseMat == null) { failure = "AreasConfigurationPrefab.m_RenderedAreaMaterial is null"; return null; }
            if (baseMat.shader == null) { failure = "rendered-area material has no shader"; return null; }
            var gpu = new PaintGpu();
            try
            {
                gpu.m_Material = new Material(baseMat) { name = "Junction Rules yellow box paint test" };
                gpu.m_Material.renderQueue = baseMat.shader.renderQueue;
                foreach (var (id, name) in new[] { (s_DecalLayerMask, "colossal_DecalLayerMask"), (s_AreaParameters, "colossal_AreaParameters"), (s_BaseColor, "_BaseColor") })
                {
                    if (!gpu.m_Material.HasProperty(id)) { failure = $"shader '{baseMat.shader.name}' has no property {name}"; gpu.Dispose(); return null; }
                }
                gpu.m_Mesh = CreateMesh();
                gpu.m_Args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
                Mod.log.Info($"yellow box paint test: material from '{baseMat.name}' (shader '{baseMat.shader.name}', render queue {gpu.m_Material.renderQueue})");
                return gpu;
            }
            catch
            {
                gpu.Dispose();
                throw;
            }
        }

        /// <summary>Material values, the game's own calls (AreaBatchSystem 2094-2119) with our values.</summary>
        public void ApplyMaterial(PaintRenderParams rp, BoxPaintLayers layers)
        {
            Material m = m_Material;
            var missing = new List<string>();
            void SetF(int id, string name, float v) { if (m.HasProperty(id)) m.SetFloat(id, v); else missing.Add(name); }
            void SetV(int id, string name, Vector4 v) { if (m.HasProperty(id)) m.SetVector(id, v); else missing.Add(name); }
            void SetT(int id, string name, Texture t) { if (m.HasProperty(id)) m.SetTexture(id, t); else missing.Add(name); }

            m.SetVector(s_AreaParameters, new Vector4((float)rp.Expand, (float)(0.5 * (rp.VolumeBelow + rp.VolumeAbove)), 0f, 0f));
            m.SetFloat(s_DecalLayerMask, math.asfloat((int)layers));
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

        /// <summary>Uploads all triangles (one draw for every box). Colour buffer = zeros (no per-area colour, as for a plain surface); visible indices = all.</summary>
        public unsafe void Upload(AreaTriangleData[] tris, Bounds3 bounds)
        {
            m_Count = tris.Length;
            if (m_Count == 0) return;
            if (m_Triangles == null || m_Triangles.count < m_Count)
            {
                m_Triangles?.Release();
                m_Colors?.Release();
                m_Indices?.Release();
                int cap = Math.Max(64, m_Count * 2);
                m_Triangles = new ComputeBuffer(cap, sizeof(AreaTriangleData));
                m_Colors = new ComputeBuffer(cap, sizeof(Game.Rendering.AreaColorData));
                m_Indices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, 4);
                m_Colors.SetData(new Game.Rendering.AreaColorData[cap]);
                var idx = new int[cap];
                for (int i = 0; i < cap; i++) idx[i] = i;
                m_Indices.SetData(idx);
            }
            m_Triangles.SetData(tris, 0, 0, m_Count);
            m_Material.SetBuffer(s_AreaBatchBuffer, m_Triangles);
            m_Material.SetBuffer(s_AreaBatchColors, m_Colors);
            m_Material.SetBuffer(s_VisibleIndices, m_Indices);
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

        /// <summary>One indirect draw per camera (all but preview cameras, as the game does for area batches).</summary>
        public void Draw(List<Camera> cameras)
        {
            if (m_Count == 0) return;
            foreach (Camera cam in cameras)
            {
                if (cam == null || cam.cameraType == CameraType.Preview) continue;
                var rp = new RenderParams(m_Material) { worldBounds = m_Bounds, camera = cam };
                Graphics.RenderMeshIndirect(in rp, m_Mesh, m_Args, 1, 0);
            }
        }

        public void Dispose()
        {
            m_Count = 0;
            m_Triangles?.Release(); m_Triangles = null;
            m_Colors?.Release(); m_Colors = null;
            m_Indices?.Release(); m_Indices = null;
            m_Args?.Release(); m_Args = null;
            if (m_Material != null) { UnityEngine.Object.Destroy(m_Material); m_Material = null; }
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
