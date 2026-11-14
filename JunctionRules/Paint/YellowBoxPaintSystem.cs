using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using JunctionRules.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Scripting;

namespace JunctionRules.Paint
{
    /// <summary>
    /// Stage 3 probe (0.3.0-probe, JR-T-003b): draws UK yellow boxes on the junctions of a local test list with the
    /// game's own surface (area) shader, option E of docs/STAGE3-PAINT-RESEARCH.md. Draw-only and opt-in:
    /// - OFF (default): this system is disabled (Enabled = false, OnUpdate never runs) and its draw callback is not
    ///   subscribed, so it does no work at all.
    /// - Reads node / edge geometry only (EntityManager Get* calls); writes nothing to any entity, creates no entity,
    ///   no area, no net, no CreationDefinition. Its only outputs are its own GPU buffers, its own material instance, the
    ///   log, and (only when the owner presses "add junction at camera centre") its own list file in ModsData.
    /// - Any failure: logged once with what failed, painting is switched off for the session (PaintFailLatch), nothing
    ///   else of the mod is touched.
    /// Driving (stage 1/2, the copy) never reads anything from here.
    /// </summary>
    public class YellowBoxPaintSystem : GameSystemBase
    {
        private static YellowBoxPaintSystem s_Instance;
        private static readonly PaintFailLatch s_Latch = new PaintFailLatch();
        private static string s_Status = "off";

        public static string StatusText => s_Latch.Failed ? "failed, off until the game restarts: " + s_Latch.What : s_Status;

        private sealed class Box
        {
            public PaintTestJunction Entry;
            public Entity Node = Entity.Null;
            public V3 LastPos;
            public readonly List<Entity> Edges = new List<Entity>();
            public AreaTriangleData[] Tris = Array.Empty<AreaTriangleData>();
            public Bounds3 Bounds;
            public string Label;
        }

        private readonly List<Box> m_Boxes = new List<Box>();
        private readonly HashSet<Entity> m_Tracked = new HashSet<Entity>();
        private readonly PaintCostMeter m_Meter = new PaintCostMeter();
        private PaintTestConfig m_Config = new PaintTestConfig();

        private EntityQuery m_NodeQuery;
        private EntityQuery m_ChangedNodeQuery;
        private EntityQuery m_ChangedEdgeQuery;
        private EntityQuery m_AreasConfigQuery;
        private PrefabSystem m_PrefabSystem;

        private bool m_InCity;
        private bool m_NeedReload = true;
        private bool m_NeedResolve;
        private bool m_NeedRebuildAll;
        private bool m_NeedMaterial;
        private bool m_AddCameraRequested;
        private bool m_LogCostRequested;
        private bool m_Active;
        private bool m_Subscribed;
        private float m_LastCostLog;
        private int m_TriangleCount;
        private BoxPaintMode m_Mode;
        private BoxPaintLayers m_Layers;

        private PaintGpu m_Gpu;

        // ------------------------------------------------------------------ control (settings call these; never throw)

        /// <summary>Settings changed (toggle, mode, layers): turns the system on or off and marks what to redo.</summary>
        internal static void ApplySettings()
        {
            try
            {
                YellowBoxPaintSystem s = s_Instance;
                if (s == null) return;
                bool want = Mod.Settings != null && Mod.Settings.YellowBoxPaintTest && !s_Latch.Failed;
                if (want && !GameVersionOk())
                {
                    // Same version gate as the copy: the paint code reads game types whose layout may differ on another game version.
                    want = false;
                    s_Status = "off: this game version differs from the one this build was made for (" + Mod.Gate.Verdict + "); the paint test is not started";
                    if (!s_VersionNoted)
                    {
                        s_VersionNoted = true;
                        Mod.log.Warn("yellow box paint test not started: version check says " + Mod.Gate.Verdict + " (" + Mod.Gate.Message + ")");
                    }
                }
                if (want && !s.m_Active) s.Activate();
                else if (!want && s.m_Active) s.Deactivate("switched off");
                if (s.m_Active && Mod.Settings != null)
                {
                    if (Mod.Settings.PaintMode != s.m_Mode) { s.m_Mode = Mod.Settings.PaintMode; s.m_NeedRebuildAll = true; }
                    if (Mod.Settings.PaintLayers != s.m_Layers) { s.m_Layers = Mod.Settings.PaintLayers; s.m_NeedMaterial = true; }
                }
            }
            catch (Exception e)
            {
                Fail("applying the paint test settings", e);
            }
        }

        private static bool s_VersionNoted;

        /// <summary>The paint test starts only when the game version is the one this build was made for (any gate verdict except a version mismatch or a failed check).</summary>
        private static bool GameVersionOk()
        {
            GateVerdict v = Mod.Gate.Verdict;
            return v == GateVerdict.Pass || v == GateVerdict.PassHashSkipped || v == GateVerdict.CodeMismatch;
        }

        internal static void RequestReload() { Request(s => s.m_NeedReload = true, "reload the test list"); }
        internal static void RequestAddCameraJunction() { Request(s => s.m_AddCameraRequested = true, "add the junction at the camera centre"); }
        internal static void RequestCostLog() { Request(s => s.m_LogCostRequested = true, "log the paint cost"); }

        private static void Request(Action<YellowBoxPaintSystem> a, string what)
        {
            try
            {
                YellowBoxPaintSystem s = s_Instance;
                if (s == null || !s.m_Active) { Mod.log.Info($"yellow box paint test: cannot {what}: the paint test is off" + (s_Latch.Failed ? " (failed earlier: " + s_Latch.What + ")" : "")); return; }
                a(s);
            }
            catch (Exception e) { Fail(what, e); }
        }

        /// <summary>Logs the first failure (what + exception) and switches painting off for the session. Never throws.</summary>
        internal static void Fail(string what, Exception e)
        {
            try
            {
                if (s_Latch.Trip(what + ": " + (e == null ? "" : e.GetType().Name + ": " + e.Message)))
                {
                    if (e != null) Mod.log.Error(e, "yellow box paint test FAILED while " + what + "; painting is off for this session (the rest of Junction Rules is not affected)");
                    else Mod.log.Error("yellow box paint test FAILED: " + what + "; painting is off for this session (the rest of Junction Rules is not affected)");
                }
                s_Instance?.Deactivate(null);
            }
            catch
            {
                // last resort: never let the paint test take anything else down
            }
        }

        // ------------------------------------------------------------------ system lifecycle

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            // Creating queries is free while the system is disabled; nothing is read until it runs.
            m_NodeQuery = GetEntityQuery(new EntityQueryDesc
            {
                // Road nodes only (RoadPrefab puts Road on its node archetype): path, rail, power and pipe nodes near a
                // junction can never be picked instead of the road node.
                All = new[] { ComponentType.ReadOnly<Game.Net.Node>(), ComponentType.ReadOnly<ConnectedEdge>(), ComponentType.ReadOnly<Game.Net.Road>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            m_ChangedNodeQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Net.Node>() },
                Any = new[] { ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            });
            m_ChangedEdgeQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>() },
                Any = new[] { ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            });
            m_AreasConfigQuery = GetEntityQuery(ComponentType.ReadOnly<AreasConfigurationData>());
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            Enabled = false;
            s_Instance = this;
            ApplySettings();
        }

        [Preserve]
        protected override void OnDestroy()
        {
            try { Deactivate(null); } catch { }
            if (s_Instance == this) s_Instance = null;
            base.OnDestroy();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_InCity = mode.IsGameOrEditor();
            m_NeedReload = true;   // only acted on while the paint test is on
            // Old boxes belong to the previous city's entities: stop drawing them at once (the draw checks the count).
            m_TriangleCount = 0;
            m_Boxes.Clear();
            m_Tracked.Clear();
        }

        private void Activate()
        {
            m_Active = true;
            m_NeedReload = true;
            m_Mode = Mod.Settings.PaintMode;
            m_Layers = Mod.Settings.PaintLayers;
            m_Meter.Reset();
            m_LastCostLog = UnityEngine.Time.realtimeSinceStartup;
            s_Status = "on, starting";
            Enabled = true;
            Mod.log.Info($"yellow box paint test ON (draw-only probe; mode {m_Mode}, decal layers {m_Layers} = {(int)m_Layers}); list file {ListPath()}");
        }

        /// <summary>Stops everything: no update, no draw callback, GPU objects released, boxes forgotten.</summary>
        private void Deactivate(string why)
        {
            bool wasActive = m_Active;
            m_Active = false;
            Enabled = false;
            Unsubscribe();
            if (m_Gpu != null) { m_Gpu.Dispose(); m_Gpu = null; }
            m_Boxes.Clear();
            m_Tracked.Clear();
            m_TriangleCount = 0;
            s_Status = "off";
            if (wasActive && why != null) Mod.log.Info("yellow box paint test OFF (" + why + "); nothing drawn, nothing left behind");
        }

        private void Subscribe()
        {
            if (m_Subscribed) return;
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            m_Subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!m_Subscribed) return;
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            m_Subscribed = false;
        }

        // ------------------------------------------------------------------ per-frame update (only while on)

        [Preserve]
        protected override void OnUpdate()
        {
            if (!m_Active) { Enabled = false; return; }
            long t0 = Stopwatch.GetTimestamp();
            string step = "starting";
            try
            {
                if (!m_InCity) { s_Status = "on, waiting for a city"; return; }
                if (m_Gpu == null)
                {
                    step = "setting up the material and buffers";
                    m_Gpu = PaintGpu.Create(m_PrefabSystem, m_AreasConfigQuery, out string gpuFailure);
                    if (m_Gpu == null) { Fail(step + ": " + gpuFailure, null); return; }
                    m_NeedMaterial = true;
                    Subscribe();
                }
                if (m_NeedMaterial)
                {
                    step = "setting material values";
                    m_NeedMaterial = false;
                    m_Gpu.ApplyMaterial(m_Config.Render, m_Layers);
                }
                if (m_NeedReload)
                {
                    step = "reading the test list";
                    m_NeedReload = false;
                    LoadList();
                    m_Gpu.ApplyMaterial(m_Config.Render, m_Layers);
                    m_NeedResolve = true;
                }
                if (m_AddCameraRequested)
                {
                    step = "adding the junction at the camera centre";
                    m_AddCameraRequested = false;
                    AddCameraJunction();
                }
                if (m_NeedResolve)
                {
                    step = "finding the test list nodes";
                    m_NeedResolve = false;
                    ResolveAll();
                    m_NeedRebuildAll = true;
                }
                bool changed = false;
                if (m_NeedRebuildAll)
                {
                    step = "building the boxes";
                    m_NeedRebuildAll = false;
                    foreach (Box b in m_Boxes) RebuildBox(b);
                    changed = true;
                }
                else if (m_Tracked.Count != 0)
                {
                    step = "checking for road edits";
                    changed = CheckEdits();
                }
                if (changed)
                {
                    step = "uploading the boxes";
                    Upload();
                }
                if (m_LogCostRequested || (m_Boxes.Count > 0 && UnityEngine.Time.realtimeSinceStartup - m_LastCostLog >= m_Config.Render.LogIntervalSeconds))
                {
                    m_LogCostRequested = false;
                    float now = UnityEngine.Time.realtimeSinceStartup;
                    Mod.log.Info("yellow box " + m_Meter.Line(Stopwatch.Frequency, now - m_LastCostLog, m_Boxes.Count, m_TriangleCount));
                    m_Meter.Reset();
                    m_LastCostLog = now;
                }
            }
            catch (Exception e)
            {
                Fail(step, e);
                return;
            }
            finally
            {
                m_Meter.AddUpdate(Stopwatch.GetTimestamp() - t0);
            }
        }

        // ------------------------------------------------------------------ list file

        internal static string ListPath()
        {
            string root;
            try { root = Colossal.PSI.Environment.EnvPath.kUserDataPath; }
            catch { root = Application.persistentDataPath; }
            return Path.Combine(Path.Combine(Path.Combine(root, "ModsData"), "JunctionRules"), PaintTestConfig.FileName);
        }

        private void LoadList()
        {
            string path = ListPath();
            if (!File.Exists(path))
            {
                m_Config = new PaintTestConfig();
                Mod.log.Info($"yellow box paint test: no list file at {path}: nothing to paint (add junctions with the button or write the file, then press Refresh)");
                return;
            }
            m_Config = PaintTestConfig.Parse(File.ReadAllText(path));
            Mod.log.Info($"yellow box paint test: list {path}: {m_Config.Junctions.Count} junction(s); border {m_Config.Geometry.BorderWidth} m, lines {m_Config.Geometry.LineWidth} m, spacing {(m_Config.Geometry.Spacing > 0 ? m_Config.Geometry.Spacing + " m" : "auto (TSM 2 m / 2.5 m)")}, angle {m_Config.Geometry.AngleDeg} deg, kerb inset +{m_Config.Geometry.KerbInset} m");
            foreach (string p in m_Config.Problems) Mod.log.Warn("yellow box paint test: list file: " + p);
        }

        private void AddCameraJunction()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                foreach (Camera c in Camera.allCameras) if (c.cameraType == CameraType.Game) { cam = c; break; }
            }
            if (cam == null) { Mod.log.Warn("yellow box paint test: no game camera found; nothing added"); return; }
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            ReadNodes(out NativeArray<Entity> ents, out List<V3> allPos);
            try
            {
                // Junction nodes only: 3 or more road edges. A mid-road node, a dead end or the node where a footpath joins the
                // road (2 road edges) nearer the camera must not win over the junction the camera is looking at.
                var pos = new List<V3>();
                for (int n = 0; n < ents.Length; n++) if (RoadEdgeCount(ents[n]) >= 3) pos.Add(allPos[n]);
                int i = PaintTestConfig.PickAlongRay(new V3(ray.origin.x, ray.origin.y, ray.origin.z), new V3(ray.direction.x, ray.direction.y, ray.direction.z), pos, 8.0);
                if (i < 0) { Mod.log.Info("yellow box paint test: no junction node (3 or more road arms) within 8 m of the camera centre line; nothing added"); return; }
                V3 p = pos[i];
                foreach (var j in m_Config.Junctions)
                {
                    if (V2.Distance(new V2(j.X, j.Z), p.XZ) < 1.0 && (!j.HasY || Math.Abs(j.Y - p.Y) < 1.0)) { Mod.log.Info($"yellow box paint test: node at {p} is already on the list"); return; }
                }
                string listPath = ListPath();
                if (File.Exists(listPath) && m_Config.Problems.Exists(q => q.StartsWith("not valid JSON") || q.StartsWith("top level")))
                {
                    // The loaded list is empty because the file could not be read: writing would replace the owner's file.
                    Mod.log.Warn($"yellow box paint test: {listPath} could not be read (see the earlier 'list file' line); not overwriting it; fix or delete the file, press Refresh, then add again");
                    return;
                }
                m_Config.Junctions.Add(new PaintTestJunction { X = Math.Round(p.X, 2), Y = Math.Round(p.Y, 2), Z = Math.Round(p.Z, 2), Name = "added at camera centre " + DateTime.Now.ToString("HH:mm:ss") });
                string path = ListPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, m_Config.Format());
                Mod.log.Info($"yellow box paint test: added node at {p} to {path} ({m_Config.Junctions.Count} junction(s))");
                m_NeedResolve = true;
            }
            finally
            {
                ents.Dispose();
            }
        }

        // ------------------------------------------------------------------ nodes (read-only)

        private void ReadNodes(out NativeArray<Entity> ents, out List<V3> pos)
        {
            ents = m_NodeQuery.ToEntityArray(Allocator.Temp);
            NativeArray<Game.Net.Node> nodes = m_NodeQuery.ToComponentDataArray<Game.Net.Node>(Allocator.Temp);
            pos = new List<V3>(nodes.Length);
            for (int i = 0; i < nodes.Length; i++) { float3 p = nodes[i].m_Position; pos.Add(new V3(p.x, p.y, p.z)); }
            nodes.Dispose();
        }

        /// <summary>Number of Road edges connected to a node (read-only).</summary>
        private int RoadEdgeCount(Entity node)
        {
            EntityManager em = EntityManager;
            if (!em.Exists(node) || !em.HasBuffer<ConnectedEdge>(node)) return 0;
            DynamicBuffer<ConnectedEdge> connected = em.GetBuffer<ConnectedEdge>(node, true);
            int n = 0;
            for (int i = 0; i < connected.Length; i++)
            {
                Entity e = connected[i].m_Edge;
                if (em.Exists(e) && em.HasComponent<Game.Net.Road>(e)) n++;
            }
            return n;
        }

        private void ResolveAll()
        {
            m_Boxes.Clear();
            m_Tracked.Clear();
            if (m_Config.Junctions.Count == 0) { s_Status = "on, test list empty"; m_Tracked.Clear(); return; }
            ReadNodes(out NativeArray<Entity> ents, out List<V3> pos);
            try
            {
                foreach (PaintTestJunction j in m_Config.Junctions)
                {
                    var b = new Box { Entry = j, Label = string.IsNullOrEmpty(j.Name) ? $"({j.X:0.0}, {j.Z:0.0})" : $"'{j.Name}'" };
                    int i = PaintTestConfig.PickNode(j, pos, m_Config.Render.SnapRadius, out int stacked);
                    if (i < 0) { Mod.log.Warn($"yellow box paint test: {b.Label}: no junction node within {m_Config.Render.SnapRadius} m; skipped"); continue; }
                    b.Node = ents[i];
                    b.LastPos = pos[i];
                    if (stacked > 0 && !j.HasY) Mod.log.Warn($"yellow box paint test: {b.Label}: {stacked} other node(s) stacked within 1 m in plan; picked the one at {pos[i]}; add \"y\" to choose");
                    m_Boxes.Add(b);
                }
            }
            finally
            {
                ents.Dispose();
            }
        }

        /// <summary>Re-finds a box's node from its last position (after the node was deleted or replaced by an edit).</summary>
        private void Reresolve(Box b)
        {
            ReadNodes(out NativeArray<Entity> ents, out List<V3> pos);
            try
            {
                var probe = new PaintTestJunction { X = b.LastPos.X, Y = b.LastPos.Y, Z = b.LastPos.Z };
                int i = PaintTestConfig.PickNode(probe, pos, Math.Min(2.0, m_Config.Render.SnapRadius), out _);
                if (i < 0) { b.Node = Entity.Null; Mod.log.Info($"yellow box paint test: {b.Label}: node removed and no node within 2 m of {b.LastPos}; box dropped until Refresh"); }
                else { b.Node = ents[i]; b.LastPos = pos[i]; }
            }
            finally
            {
                ents.Dispose();
            }
        }

        /// <summary>Read-only change detection: true when a boxed node or one of its edges is Updated or Deleted this frame.</summary>
        private bool CheckEdits()
        {
            bool nodes = !m_ChangedNodeQuery.IsEmptyIgnoreFilter, edges = !m_ChangedEdgeQuery.IsEmptyIgnoreFilter;
            if (!nodes && !edges) return false;
            var hit = new HashSet<Entity>();
            if (nodes) Collect(m_ChangedNodeQuery, hit);
            if (edges) Collect(m_ChangedEdgeQuery, hit);
            if (hit.Count == 0) return false;
            bool any = false;
            foreach (Box b in m_Boxes)
            {
                bool touched = hit.Contains(b.Node);
                foreach (Entity e in b.Edges) touched |= hit.Contains(e);
                if (!touched) continue;
                any = true;
                if (!EntityManager.Exists(b.Node) || EntityManager.HasComponent<Deleted>(b.Node)) Reresolve(b);
                RebuildBox(b);
            }
            return any;
        }

        private void Collect(EntityQuery q, HashSet<Entity> hit)
        {
            NativeArray<Entity> a = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < a.Length; i++) if (m_Tracked.Contains(a[i])) hit.Add(a[i]);
            a.Dispose();
        }

        // ------------------------------------------------------------------ box building (read-only reads)

        private void RebuildBox(Box b)
        {
            long t0 = Stopwatch.GetTimestamp();
            b.Tris = Array.Empty<AreaTriangleData>();
            b.Edges.Clear();
            try
            {
                if (b.Node == Entity.Null) return;
                EntityManager em = EntityManager;
                if (!em.Exists(b.Node) || em.HasComponent<Deleted>(b.Node) || !em.HasComponent<Game.Net.Node>(b.Node) || !em.HasBuffer<ConnectedEdge>(b.Node))
                {
                    Mod.log.Info($"yellow box paint test: {b.Label}: node gone; skipped");
                    return;
                }
                float3 np = em.GetComponentData<Game.Net.Node>(b.Node).m_Position;
                b.LastPos = new V3(np.x, np.y, np.z);
                var arms = new List<ArmSlice>();
                var notes = new List<string>();
                bool roundabout = false;
                DynamicBuffer<ConnectedEdge> connected = em.GetBuffer<ConnectedEdge>(b.Node, true);
                var edges = new List<Entity>(connected.Length);
                for (int i = 0; i < connected.Length; i++) edges.Add(connected[i].m_Edge);
                foreach (Entity e in edges)
                {
                    if (!em.Exists(e) || !em.HasComponent<Edge>(e)) continue;
                    b.Edges.Add(e);
                    Edge edge = em.GetComponentData<Edge>(e);
                    bool isStart = edge.m_Start == b.Node, isEnd = edge.m_End == b.Node;
                    if (!isStart && !isEnd) continue;   // connected in its middle: not an arm of this node
                    EdgeNodeGeometry g;
                    if (isStart) { if (!em.HasComponent<StartNodeGeometry>(e)) continue; g = em.GetComponentData<StartNodeGeometry>(e).m_Geometry; }
                    else { if (!em.HasComponent<EndNodeGeometry>(e)) continue; g = em.GetComponentData<EndNodeGeometry>(e).m_Geometry; }
                    if (g.m_MiddleRadius > 0f) roundabout = true;
                    KerbInsets(em, e, isStart, out double insetLeft, out double insetRight, notes);
                    List<V3> left = Sample(g.m_Left.m_Left), right = Sample(g.m_Right.m_Right);
                    double extra = m_Config.Geometry.KerbInset;
                    var l2 = BoxPaintGeometry.OffsetToward(left, insetLeft + extra, right[0].XZ);
                    var r2 = BoxPaintGeometry.OffsetToward(right, insetRight + extra, left[0].XZ);
                    arms.Add(new ArmSlice(l2, r2));
                }
                foreach (Entity e in b.Edges) m_Tracked.Add(e);
                m_Tracked.Add(b.Node);
                if (arms.Count < 2) { Mod.log.Info($"yellow box paint test: {b.Label}: {arms.Count} arm(s), no box"); return; }
                var centre = new V2(np.x, np.z);
                List<V3> outline = BoxPaintGeometry.BuildOutline(arms, centre, m_Config.Geometry.StraightenTolerance, out string why);
                if (outline == null)
                {
                    notes.Add("outline merge failed (" + why + "), using the convex hull of the kerbs");
                    outline = BoxPaintGeometry.HullOutline(arms);
                }
                BoxPaintResult res = BoxPaintGeometry.BuildPieces(outline, m_Config.Geometry, m_Mode);
                if (!res.Ok) { Mod.log.Warn($"yellow box paint test: {b.Label}: no box: {res.Failure}"); return; }
                notes.AddRange(res.Warnings);
                if (roundabout) notes.Add("roundabout node: whole node boxed from the outer kerbs, island not cut out (probe S6 looks at it)");
                b.Tris = ToTriangles(res, m_Config.Render, out b.Bounds);
                long ticks = Stopwatch.GetTimestamp() - t0;
                Mod.log.Info($"yellow box paint test: {b.Label}: node {b.Node} at {b.LastPos}, {arms.Count} arms, outline {res.Outline.Count} pts / {BoxPaintGeometry.SignedArea(res.Outline):0.0} m2, box {res.Rect.LenU:0.0} x {res.Rect.LenV:0.0} m, spacing {res.SpacingUsed} m, pieces {res.Pieces.Count}, triangles {b.Tris.Length}, built in {ticks * 1000.0 / Stopwatch.Frequency:0.000} ms" + (notes.Count > 0 ? "; notes: " + string.Join("; ", notes) : ""));
            }
            finally
            {
                m_Meter.AddRebuild(Stopwatch.GetTimestamp() - t0);
            }
        }

        private static List<V3> Sample(Bezier4x3 c)
        {
            const int n = 8;
            var r = new List<V3>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                float3 p = MathUtils.Position(c, i / (float)n);
                r.Add(new V3(p.x, p.y, p.z));
            }
            return r;
        }

        /// <summary>
        /// Footway width each side from the node-side composition: the span of the pieces with road lanes is the
        /// carriageway; what is left of the full width each side is footway (research Q5 "Carriageway only"; U7).
        /// Start node: left = composition left; end node: mirrored (the end-node slice faces the other way). Unproven
        /// assumption, checked by eye in probe S7-S10.
        /// </summary>
        private static void KerbInsets(EntityManager em, Entity edge, bool isStart, out double left, out double right, List<string> notes)
        {
            left = right = 0;
            if (!em.HasComponent<Composition>(edge)) { AddOnce(notes, "no composition: kerb inset 0"); return; }
            Composition comp = em.GetComponentData<Composition>(edge);
            Entity c = isStart ? comp.m_StartNode : comp.m_EndNode;
            if (c == Entity.Null || !em.HasComponent<NetCompositionData>(c) || !em.HasBuffer<NetCompositionPiece>(c)) { AddOnce(notes, "no node composition: kerb inset 0"); return; }
            float width = em.GetComponentData<NetCompositionData>(c).m_Width;
            DynamicBuffer<NetCompositionPiece> pieces = em.GetBuffer<NetCompositionPiece>(c, true);
            double cmin = double.MaxValue, cmax = double.MinValue;
            for (int i = 0; i < pieces.Length; i++)
            {
                NetCompositionPiece p = pieces[i];
                if ((p.m_PieceFlags & NetPieceFlags.HasRoadLanes) == 0) continue;
                cmin = Math.Min(cmin, p.m_Offset.x - p.m_Size.x * 0.5);
                cmax = Math.Max(cmax, p.m_Offset.x + p.m_Size.x * 0.5);
            }
            if (cmin > cmax) { AddOnce(notes, "no road-lane pieces: kerb inset 0"); return; }
            double l = cmin + width * 0.5, r = width * 0.5 - cmax;
            if (l < 0 || r < 0 || l > width * 0.5 || r > width * 0.5) { AddOnce(notes, $"odd carriageway span {cmin:0.00}..{cmax:0.00} of width {width:0.00}: kerb inset 0"); return; }
            if (isStart) { left = l; right = r; } else { left = r; right = l; }
        }

        private static void AddOnce(List<string> notes, string s) { if (!notes.Contains(s)) notes.Add(s); }

        /// <summary>Pieces -> the game's AreaTriangleData layout (Game.Rendering.AreaTriangleData), one per triangle.</summary>
        private static AreaTriangleData[] ToTriangles(BoxPaintResult res, PaintRenderParams rp, out Bounds3 bounds)
        {
            var list = new List<AreaTriangleData>(res.TriangleCount);
            bounds = new Bounds3(new float3(float.MaxValue), new float3(float.MinValue));
            float size = (float)Math.Max(1.0, Math.Max(res.Rect.LenU, res.Rect.LenV));
            float lodFactor = RenderingUtils.CalculateDistanceFactor(RenderingUtils.CalculateLodLimit(RenderingUtils.GetRenderingSize(new float3(size, 1f, size))));
            V2 left = res.Rect.U.Left;
            var offsetDir = new float4((float)res.Rect.Origin.X, (float)res.Rect.Origin.Z, (float)left.X, (float)left.Z);
            foreach (PaintPiece piece in res.Pieces)
            {
                int n = piece.Ring.Count;
                for (int t = 0; t + 2 < piece.Triangles.Count; t += 3)
                {
                    int ia = piece.Triangles[t], ib = piece.Triangles[t + 1], ic = piece.Triangles[t + 2];
                    var d = new AreaTriangleData
                    {
                        m_APos = F3(piece.Ring[ia]),
                        m_BPos = F3(piece.Ring[ib]),
                        m_CPos = F3(piece.Ring[ic]),
                        m_OffsetDir = offsetDir,
                        m_LodDistanceFactor = lodFactor,
                    };
                    BoxPaintGeometry.Neighbours(n, ia, rp.SwapPrevNext, out int pa, out int na);
                    BoxPaintGeometry.Neighbours(n, ib, rp.SwapPrevNext, out int pb, out int nb);
                    BoxPaintGeometry.Neighbours(n, ic, rp.SwapPrevNext, out int pc, out int nc);
                    d.m_APrevXZ = F2(piece.Ring[pa]); d.m_ANextXZ = F2(piece.Ring[na]);
                    d.m_BPrevXZ = F2(piece.Ring[pb]); d.m_BNextXZ = F2(piece.Ring[nb]);
                    d.m_CPrevXZ = F2(piece.Ring[pc]); d.m_CNextXZ = F2(piece.Ring[nc]);
                    float ymin = math.min(d.m_APos.y, math.min(d.m_BPos.y, d.m_CPos.y));
                    float ymax = math.max(d.m_APos.y, math.max(d.m_BPos.y, d.m_CPos.y));
                    d.m_YMinMax = new float2(ymin - (float)rp.VolumeBelow, ymax + (float)rp.VolumeAbove);
                    Bounds3 tb = MathUtils.Bounds(new Triangle3(d.m_APos, d.m_BPos, d.m_CPos));
                    tb.min.y = d.m_YMinMax.x;
                    tb.max.y = d.m_YMinMax.y;
                    bounds |= tb;
                    list.Add(d);
                }
            }
            return list.ToArray();
        }

        private static float3 F3(V3 p) => new float3((float)p.X, (float)p.Y, (float)p.Z);
        private static float2 F2(V3 p) => new float2((float)p.X, (float)p.Z);

        private void Upload()
        {
            var all = new List<AreaTriangleData>();
            var bounds = new Bounds3(new float3(float.MaxValue), new float3(float.MinValue));
            foreach (Box b in m_Boxes)
            {
                if (b.Tris.Length == 0) continue;
                all.AddRange(b.Tris);
                bounds |= b.Bounds;
            }
            m_TriangleCount = all.Count;
            m_Gpu.Upload(all.ToArray(), bounds);
            int painted = 0;
            foreach (Box b in m_Boxes) if (b.Tris.Length > 0) painted++;
            s_Status = $"on, {painted} of {m_Config.Junctions.Count} listed junction(s) painted, {m_TriangleCount} triangles";
        }

        // ------------------------------------------------------------------ draw (per camera, only while on)

        private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (!m_Active || m_Gpu == null || m_TriangleCount == 0) return;
            long t0 = Stopwatch.GetTimestamp();
            try
            {
                m_Gpu.Draw(cameras);
            }
            catch (Exception e)
            {
                Fail("drawing the boxes", e);
                return;
            }
            m_Meter.AddDraw(Stopwatch.GetTimestamp() - t0);
        }
    }
}
