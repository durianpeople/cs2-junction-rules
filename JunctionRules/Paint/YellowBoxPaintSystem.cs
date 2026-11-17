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
using JunctionRules.Boxes;
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
    /// Stage 3 probe (0.3.0-probe, JR-T-003b; 0.3.1-probe JR-T-003c adds the diag switches): draws UK yellow boxes on the junctions of a local test list with the
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

        private enum BoxKind { ListJunction, SavedJunction, SavedEntrance }

        private sealed class Box
        {
            public BoxKind Kind;
            public PaintTestJunction Entry;
            public Entity Node = Entity.Null;
            public Entity Building = Entity.Null;
            /// <summary>0.4.3: SavedEntrance = one boxed car exit of Building (Core/BuildingExit.cs ExitKey).</summary>
            public ExitKey Key;
            public bool Dropped;
            public V3 LastPos;
            public readonly List<Entity> Edges = new List<Entity>();
            public AreaTriangleData[] Tris = Array.Empty<AreaTriangleData>();
            public Bounds3 Bounds;
            public string Label;
            /// <summary>0.4.4 (JR-T-011): the box outline as drawn alone (null = not painted); merged with its touching neighbours at draw time (Core/BoxMerge.cs).</summary>
            public List<V3> Outline;
            /// <summary>0.4.4: exit box: its road edge key and the carriageways it covers (divided road: the building's side).</summary>
            public long EdgeKey;
            public uint Carriageways = MedianSplit.AllCarriageways;
            /// <summary>JR-T-014c (G9): a box at a car ENTRANCE of a boxed building, derived at resolve time (Key kind Entrance, never saved).</summary>
            public bool Derived;
            /// <summary>0.4.9 (G14): exit / entrance box built: its window along the edge (m), access and building side, for the joint-box pass (Regroup).</summary>
            public bool HasWindow;
            public double WinFrom, WinTo;
            public BoxFacts.Access Access;
            public V2 BuildingPos;
        }

        /// <summary>0.4.4 (JR-T-011): one drawn group (one or more boxes merged): its triangles, cached by geometry signature.</summary>
        private sealed class DrawnGroup
        {
            public AreaTriangleData[] Tris = Array.Empty<AreaTriangleData>();
            public Bounds3 Bounds;
        }

        private List<DrawnGroup> m_Groups = new List<DrawnGroup>();
        private Dictionary<string, DrawnGroup> m_GroupCache = new Dictionary<string, DrawnGroup>();

        private readonly List<Box> m_Boxes = new List<Box>();
        private readonly HashSet<Entity> m_Tracked = new HashSet<Entity>();
        private readonly PaintCostMeter m_Meter = new PaintCostMeter();
        private PaintTestConfig m_Config = new PaintTestConfig();

        private EntityQuery m_NodeQuery;
        private EntityQuery m_ChangedNodeQuery;
        private EntityQuery m_ChangedEdgeQuery;
        private EntityQuery m_AreasConfigQuery;
        private EntityQuery m_SavedJunctionQuery;
        private EntityQuery m_SavedEntranceQuery;
        private EntityQuery m_ChangedBuildingQuery;

        /// <summary>0.4.0: saved boxes (tool) exist in the loaded city: the system runs for them even with the paint test off.</summary>
        private static bool s_SavedPresent;
        private bool m_TestOn;
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

        /// <summary>0.4.4 (JR-T-011 B): hides the road's own markings under the painted boxes (Paint/MarkingHider.cs).</summary>
        private readonly MarkingHider m_Hider = new MarkingHider();
        private bool m_NeedMarkings;
        private bool m_HideMarkings;
        /// <summary>The marking-hide groups of the last regroup: one per merged box group (Core MergedBoxes).</summary>
        private List<MarkingHider.Group> m_MarkGroups = new List<MarkingHider.Group>();
        private int m_MarkVersion = -1;

        // ------------------------------------------------------------------ control (settings call these; never throw)

        /// <summary>Settings changed (toggle, mode, layers): turns the system on or off and marks what to redo.</summary>
        internal static void ApplySettings()
        {
            try
            {
                YellowBoxPaintSystem s = s_Instance;
                if (s == null) return;
                bool testOn = Mod.Settings != null && Mod.Settings.YellowBoxPaintTest;
                bool want = (testOn || s_SavedPresent) && !s_Latch.Failed;
                if (want && !GameVersionOk())
                {
                    // Same version gate as the copy: the paint code reads game types whose layout may differ on another game version.
                    want = false;
                    s_Status = "off: this game version differs from the one this build was made for (" + Mod.Gate.Verdict + "); no boxes are painted (saved boxes stay in the city)";
                    if (!s_VersionNoted)
                    {
                        s_VersionNoted = true;
                        Mod.log.Warn("yellow box paint test not started: version check says " + Mod.Gate.Verdict + " (" + Mod.Gate.Message + ")");
                    }
                }
                if (want && !s.m_Active) { s.m_TestOn = testOn; s.Activate(); }
                else if (!want && s.m_Active) s.Deactivate(testOn ? "switched off" : "paint test off and no saved boxes");
                else if (s.m_Active && s.m_TestOn != testOn) { s.m_TestOn = testOn; s.m_NeedResolve = true; }
                if (s.m_Active && Mod.Settings != null)
                {
                    if (Mod.Settings.PaintMode != s.m_Mode) { s.m_Mode = Mod.Settings.PaintMode; s.m_NeedRebuildAll = true; }
                    if (Mod.Settings.PaintLayers != s.m_Layers) { s.m_Layers = Mod.Settings.PaintLayers; s.m_NeedMaterial = true; }
                    if (s.UpdateShapeParams()) s.m_NeedRebuildAll = true;
                    if (Mod.Settings.HideMarkingsUnderBoxes != s.m_HideMarkings) { s.m_HideMarkings = Mod.Settings.HideMarkingsUnderBoxes; s.m_NeedMarkings = true; }
                }
            }
            catch (Exception e)
            {
                Fail("applying the paint test settings", e);
            }
        }

        private static bool s_VersionNoted;

        private double m_Margin = MarkingMargin.Default;

        /// <summary>0.4.2: the marking margin in force (Options slider, or the list file's markingMargin when present).</summary>
        private double CurrentMargin => m_Margin;

        /// <summary>
        /// 0.4.2: refreshes the shape parameters the tool shares (BoxStore: entrance length, kerb inset, margin); true
        /// (and a log line) when the margin changed, so every box is rebuilt.
        /// </summary>
        private bool UpdateShapeParams()
        {
            double setting = Mod.Settings != null ? Mod.Settings.YellowBoxMarkingMargin : MarkingMargin.Default;
            BoxShapeParams p = BoxShapeParams.From(m_Config, setting);
            BoxStore.PublishParams(p);
            double m = p.MarkingMargin;
            if (Math.Abs(m - m_Margin) < 1e-9) return false;
            m_Margin = m;
            Mod.log.Info($"yellow boxes: marking margin {m:0.0} m ({p.MarginSource}); boxes rebuilt");
            return true;
        }

        /// <summary>The paint test starts only when the game version is the one this build was made for (any gate verdict except a version mismatch or a failed check).</summary>
        private static bool GameVersionOk()
        {
            GateVerdict v = Mod.Gate.Verdict;
            return v == GateVerdict.Pass || v == GateVerdict.PassHashSkipped || v == GateVerdict.CodeMismatch;
        }

        /// <summary>0.4.0: the yellow box tool added or removed a saved box: paint it / clear it at once (starts the system when needed).</summary>
        internal static void NotifyBoxesChanged()
        {
            try
            {
                s_SavedPresent = true;   // re-counted by the resolve that follows
                YellowBoxPaintSystem s = s_Instance;
                if (s == null) return;
                if (s.m_Active) s.m_NeedResolve = true;
                else ApplySettings();
            }
            catch (Exception e) { Fail("taking the tool's change", e); }
        }

        /// <summary>0.4.4: MarkingSaveGuardSystem, right before the city is serialized. Never throws.</summary>
        internal static void StripMarkingsForSave(EntityManager em)
        {
            try
            {
                YellowBoxPaintSystem s = s_Instance;
                if (s == null) return;
                s.m_Hider.StripForSave(em);   // retries, falls back, logs CRITICAL if a cut may stay in the save
            }
            catch (Exception e)
            {
                try { Mod.log.Critical(e, "yellow boxes: taking the marking cuts off before the save failed; THIS SAVE MAY CONTAIN HIDDEN ROAD MARKINGS: switch 'Hide road markings under yellow boxes' off and save again"); } catch { }
            }
        }

        /// <summary>
        /// JR-T-013e (G2): MarkingRenderSystem, every frame in PreCulling before the game's PreCullingSystem: does the
        /// marking request of the paint system (apply / clear) or keeps ours in place. Runs also while painting is off, so a
        /// clear requested by Deactivate is done. Never throws: a failure fails the paint (which requests the clear).
        /// </summary>
        internal static void RunMarkings(EntityManager em)
        {
            YellowBoxPaintSystem s = s_Instance;
            if (s == null || !s.m_Hider.HasWork) return;
            try { s.m_Hider.Run(em); }
            catch (Exception e) { Fail("hiding the road markings under the boxes", e); }
        }

        /// <summary>JR-T-013e (G2 F4): Mod.OnDispose: painting off and the road's markings back at once. Never throws.</summary>
        internal static void Shutdown()
        {
            try
            {
                YellowBoxPaintSystem s = s_Instance;
                if (s == null) return;
                s.Deactivate("the mod is unloading");
                if (s.World != null && s.World.IsCreated) s.m_Hider.ClearNow(s.EntityManager);
            }
            catch (Exception e)
            {
                try { Mod.log.Error(e, "yellow boxes: switching off on mod unload failed"); } catch { }
            }
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
            m_SavedJunctionQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<YellowBoxJunction>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            m_SavedEntranceQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<YellowBoxEntrance>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            m_ChangedBuildingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Buildings.Building>() },
                Any = new[] { ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            });
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            BoxStore.Changed += NotifyBoxesChanged;   // 0.4.0: the tool's add / remove (Boxes/BoxStore.cs)
            Enabled = false;
            s_Instance = this;
            ApplySettings();
        }

        [Preserve]
        protected override void OnDestroy()
        {
            try { Deactivate(null); } catch { }
            try { m_Hider.ClearNow(EntityManager); } catch { }   // G2: the render system does not run again
            BoxStore.Changed -= NotifyBoxesChanged;
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
            m_Groups = new List<DrawnGroup>();
            m_MarkGroups = new List<MarkingHider.Group>();
            m_GroupCache.Clear();
            MergedBoxes.Clear();
            m_Tracked.Clear();
            m_Hider.Forget();   // 0.4.4: the old city's lanes / objects went with it
            PublishParking();
            // 0.4.0: saved boxes in this city? (two empty-query checks, once per load)
            int j = m_InCity ? m_SavedJunctionQuery.CalculateEntityCount() : 0, en = m_InCity ? m_SavedEntranceQuery.CalculateEntityCount() : 0;
            s_SavedPresent = j + en > 0;
            if (m_InCity) Mod.log.Info($"yellow boxes: {j} junction box(es) and {en} building(s) with exit boxes saved in this city");
            ApplySettings();
        }

        private void Activate()
        {
            m_Active = true;
            m_NeedReload = true;
            m_Mode = Mod.Settings.PaintMode;
            m_Layers = Mod.Settings.PaintLayers;
            m_HideMarkings = Mod.Settings.HideMarkingsUnderBoxes;
            m_NeedMarkings = true;
            m_Meter.Reset();
            m_LastCostLog = UnityEngine.Time.realtimeSinceStartup;
            s_Status = "on, starting";
            Enabled = true;
            Mod.log.Info($"yellow box painting ON ({(m_TestOn ? "paint test list + " : "")}saved boxes; mode {m_Mode}, decal layers {m_Layers} = {(int)m_Layers}); list file {ListPath()}");
        }

        /// <summary>Stops everything: no update, no draw callback, GPU objects released, boxes forgotten.</summary>
        private void Deactivate(string why)
        {
            bool wasActive = m_Active;
            m_Active = false;
            Enabled = false;
            Unsubscribe();
            if (m_Gpu != null) { m_Gpu.Dispose(); m_Gpu = null; }
            ClearMarkings();   // 0.4.4: the road's markings come back with the boxes gone
            m_Boxes.Clear();
            m_Groups = new List<DrawnGroup>();
            m_MarkGroups = new List<MarkingHider.Group>();
            m_GroupCache.Clear();
            MergedBoxes.Clear();
            m_Tracked.Clear();
            m_TriangleCount = 0;
            PublishParking();
            s_Status = "off";
            if (wasActive && why != null) Mod.log.Info("yellow box painting OFF (" + why + "); nothing drawn, nothing left behind");
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
                if (m_NeedReload)
                {
                    // 0.3.1: the list (with its diag switches) is read before the GPU objects are made, so the switches
                    // that choose the material / draw path apply at once; a change of those on Refresh remakes them.
                    step = "reading the test list";
                    m_NeedReload = false;
                    LoadList();
                    m_NeedMaterial = true;
                    m_NeedResolve = true;
                    if (m_Gpu != null && m_Gpu.GpuKey != m_Config.Diag.GpuKey)
                    {
                        Mod.log.Info("yellow box paint test: draw path / material switches changed, remaking the GPU objects");
                        m_Gpu.Dispose();
                        m_Gpu = null;
                    }
                    else if (m_Gpu != null) m_Gpu.SetDiag(m_Config.Diag);   // review fix: the kept GPU objects read the new switch values
                }
                if (m_Gpu == null)
                {
                    step = "setting up the material and buffers";
                    m_Gpu = PaintGpu.Create(m_PrefabSystem, EntityManager, m_AreasConfigQuery, m_Config.Diag, out string gpuFailure);
                    if (m_Gpu == null) { Fail(step + ": " + gpuFailure, null); return; }
                    m_NeedMaterial = true;
                    m_NeedRebuildAll = true;   // new buffers: upload again
                    Subscribe();
                }
                if (m_NeedMaterial)
                {
                    step = "setting material values";
                    m_NeedMaterial = false;
                    m_Gpu.ApplyMaterial(m_Config.Render, m_Layers);
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
                    if (!m_TestOn && !s_SavedPresent) { Deactivate("paint test off and no saved boxes"); return; }
                }
                bool changed = false;
                if (m_NeedRebuildAll)
                {
                    step = "building the boxes";
                    m_NeedRebuildAll = false;
                    foreach (Box b in m_Boxes) RebuildBox(b);
                    if (m_Boxes.RemoveAll(x => x.Dropped) > 0)
                    {
                        bool saved = false;
                        foreach (Box b in m_Boxes) saved |= b.Kind != BoxKind.ListJunction;
                        s_SavedPresent = saved;
                    }
                    changed = true;
                }
                else if (m_Tracked.Count != 0)
                {
                    step = "checking for road edits";
                    changed = CheckEdits();
                }
                if (changed)
                {
                    step = "merging touching boxes";
                    Regroup();
                    step = "uploading the boxes";
                    Upload();
                    m_NeedMarkings = true;
                    PublishParking();
                    if (!m_TestOn && !s_SavedPresent) { Deactivate("paint test off and no saved boxes left"); return; }
                }
                if (MergedBoxes.Version != m_MarkVersion) m_NeedMarkings = true;   // the merged groups changed
                if (m_NeedMarkings)
                {
                    step = "hiding the road markings under the boxes";
                    m_NeedMarkings = false;
                    ApplyMarkings();
                }
                // G2: keeping ours in place (after a save, after a tool) is MarkingRenderSystem's job (before PreCullingSystem)
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

        /// <summary>The paint list file (JR-T-007b-r: path kept in Boxes/BoxStore.cs, which also reads it for the tool).</summary>
        internal static string ListPath() => BoxStore.ListPath();

        private void LoadList()
        {
            m_GroupCache.Clear();   // review: cached group triangles hold the old list's geometry / render / diag values
            string path = ListPath();
            if (!File.Exists(path))
            {
                m_Config = new PaintTestConfig();
                UpdateShapeParams();
                if (m_TestOn) Mod.log.Info($"yellow box paint test: no list file at {path}: nothing to paint (add junctions with the button or write the file, then press Refresh)");
                return;
            }
            m_Config = PaintTestConfig.Parse(File.ReadAllText(path));
            UpdateShapeParams();
            Mod.log.Info($"yellow box paint test: list {path}: {m_Config.Junctions.Count} junction(s); border {m_Config.Geometry.BorderWidth} m, lines {m_Config.Geometry.LineWidth} m, spacing {(m_Config.Geometry.Spacing > 0 ? m_Config.Geometry.Spacing + " m" : "auto (TSM 2 m / 2.5 m)")}, angle {m_Config.Geometry.AngleDeg} deg, kerb inset +{m_Config.Geometry.KerbInset} m");
            foreach (string p in m_Config.Problems) Mod.log.Warn("yellow box paint test: list file: " + p);
            Mod.log.Info("yellow box paint test: diag switches (effective): " + m_Config.Diag.Describe()
                + $"; render: volume -{m_Config.Render.VolumeBelow}/+{m_Config.Render.VolumeAbove} m, expand {m_Config.Render.Expand}, edgeFade [{m_Config.Render.EdgeFadeX}, {m_Config.Render.EdgeFadeY}], edgeNoise [{m_Config.Render.EdgeNoiseX}, {m_Config.Render.EdgeNoiseY}], swapPrevNext {m_Config.Render.SwapPrevNext}, plainTextures {m_Config.Render.PlainTextures}, colour [{string.Join(", ", m_Config.Render.Color)}]");
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
            ResolveSaved();
            if (!m_TestOn) return;
            if (m_Config.Junctions.Count == 0) { s_Status = "on, test list empty"; return; }
            ReadNodes(out NativeArray<Entity> ents, out List<V3> pos);
            try
            {
                foreach (PaintTestJunction j in m_Config.Junctions)
                {
                    var b = new Box { Kind = BoxKind.ListJunction, Entry = j, Label = string.IsNullOrEmpty(j.Name) ? $"({j.X:0.0}, {j.Z:0.0})" : $"'{j.Name}'" };
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

        /// <summary>
        /// 0.4.0: the saved boxes (tool): every node with YellowBoxJunction; 0.4.3: one box per boxed exit of every building
        /// with YellowBoxEntrance. A 0.4.0-0.4.2 building box (no exit keys) is migrated first (MigrateLegacy).
        /// </summary>
        private void ResolveSaved()
        {
            NativeArray<Entity> e = m_SavedEntranceQuery.ToEntityArray(Allocator.Temp);
            try
            {
                EntityManager em = EntityManager;
                for (int i = 0; i < e.Length; i++)
                {
                    if (!em.Exists(e[i]) || !em.HasComponent<YellowBoxEntrance>(e[i])) continue;
                    if (em.GetComponentData<YellowBoxEntrance>(e[i]).Legacy) MigrateLegacy(e[i]);
                    if (!em.HasComponent<YellowBoxEntrance>(e[i])) continue;
                    YellowBoxEntrance c = em.GetComponentData<YellowBoxEntrance>(e[i]);
                    for (int k = 0; k < c.m_Exits.Length; k++)
                        m_Boxes.Add(new Box { Kind = BoxKind.SavedEntrance, Building = e[i], Key = c.m_Exits[k], Label = $"saved exit box (building {e[i]}, exit {c.m_Exits[k]})" });
                    AddEntranceBoxes(e[i], c);
                }
            }
            finally
            {
                e.Dispose();
            }
            // junctions after the migration: a legacy box on a building whose exit is its own access road moved onto that node
            NativeArray<Entity> j = m_SavedJunctionQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < j.Length; i++) m_Boxes.Add(new Box { Kind = BoxKind.SavedJunction, Node = j[i], Label = "saved junction box (node " + j[i] + ")" });
            }
            finally
            {
                j.Dispose();
            }
            bool any = false;
            foreach (Box b in m_Boxes) any |= b.Kind != BoxKind.ListJunction;
            s_SavedPresent = any;
        }

        /// <summary>
        /// JR-T-014c (G9, owner 17 Nov 2026): a boxed building gets a box at every car ENTRANCE too (an entry-only sub-net node
        /// on a public road's middle, BoxFacts.Accesses), derived here from the game data on every resolve: nothing is written
        /// to the save, so boxes saved by 0.4.3-0.4.7 pick up their entrances on load. An entrance at the same place as a
        /// boxed exit adds nothing (Core/BuildingAccess.EntranceBoxes); an entry-only access ROAD junction is not derived
        /// (it would need a junction box on the node: the tool offers it). Logged once per box when built (RebuildEntrance).
        /// </summary>
        private void AddEntranceBoxes(Entity building, YellowBoxEntrance c)
        {
            EntityManager em = EntityManager;
            if (!AccessRules.EntrancesBoxed(c.m_Exits.Length, c.Legacy)) return;
            List<BoxFacts.Exit> exits = BoxFacts.Exits(em, building, null);
            List<BoxFacts.Exit> entrances = BoxFacts.Entrances(em, building, null);
            if (entrances.Count == 0) return;
            var exitCands = new List<ExitCandidate>(exits.Count);
            foreach (var x in exits) exitCands.Add(x.C);
            var boxed = new List<ExitCandidate>();
            for (int k = 0; k < c.m_Exits.Length; k++)
            {
                int i = ExitRules.Match(c.m_Exits[k], exitCands);
                if (i >= 0) boxed.Add(exitCands[i]);
            }
            var inCands = new List<ExitCandidate>(entrances.Count);
            foreach (var x in entrances) inCands.Add(x.C);
            foreach (int i in AccessRules.EntranceBoxes(inCands, boxed))
                m_Boxes.Add(new Box { Kind = BoxKind.SavedEntrance, Derived = true, Building = building, Key = entrances[i].C.Key, Label = $"entrance box (building {building}, entrance {entrances[i].C.Key}, derived)" });
        }

        /// <summary>
        /// 0.4.3 (owner ruling 2): a 0.4.0-0.4.2 building box (one box at the lot-front middle, no exit key) moves to the
        /// building's exit nearest the old box centre (Building.m_RoadEdge at m_CurvePosition; Core/BuildingExit.cs
        /// ExitRules.Migrate): a driveway / spawn-location / fallback exit gets the key; an exit through the building's own
        /// access road becomes a junction box on that node (the old box component goes). No exit and no road access at
        /// all: the old box is dropped. Every case logs one line. Only our own components are written.
        /// </summary>
        private void MigrateLegacy(Entity building)
        {
            EntityManager em = EntityManager;
            var notes = new List<string>();
            BoxFacts.Accesses(em, building, notes, out List<BoxFacts.Exit> exits, out List<BoxFacts.Exit> entrances, out BoxFacts.Exit fallback, out bool hasFallback);
            V2 old = default;
            Game.Buildings.Building bd = em.GetComponentData<Game.Buildings.Building>(building);
            BoxFacts.Access a = em.Exists(bd.m_RoadEdge) ? BoxFacts.AccessOf(em, bd.m_RoadEdge, bd.m_CurvePosition) : default;
            if (a.Valid) old = new V2(a.Point.x, a.Point.z);
            else { BoxFacts.GetFrame(em, building, out old, out _); }
            var cands = new List<ExitCandidate>(exits.Count);
            foreach (var x in exits) cands.Add(x.C);
            int i = ExitRules.Migrate(old, cands, out string why);
            if (i < 0 && hasFallback && entrances.Count > 0)
            {
                // JR-T-014c (G9 / G10): no real exit but car entrance(s): the old box becomes the building's marker key (entrances boxed)
                BoxStore.SetExits(em, building, fallback.C.Key);
                Mod.log.Info($"yellow boxes: 0.4.0-0.4.2 box on building {building} migrated to its {entrances.Count} car entrance(s) (marker key {fallback.C.Key}, not drawn)");
                return;
            }
            if (i < 0)
            {
                BoxStore.Remove(em, building, false, notify: false);
                Mod.log.Info($"yellow boxes: 0.4.0-0.4.2 box on building {building} DROPPED on load: {why}");
                return;
            }
            BoxFacts.Exit ex = exits[i];
            if (ex.IsJunction)
            {
                BoxStore.Remove(em, building, false, notify: false);
                BoxStore.Add(em, ex.Node, notify: false);
                Mod.log.Info($"yellow boxes: 0.4.0-0.4.2 box on building {building} migrated to a JUNCTION box on node {ex.Node} ({why})");
                return;
            }
            BoxStore.SetExits(em, building, ex.C.Key);
            Mod.log.Info($"yellow boxes: 0.4.0-0.4.2 box on building {building} migrated to exit {ex.C.Key} ({why})" + (notes.Count > 0 ? "; notes: " + string.Join("; ", notes) : ""));
        }

        /// <summary>
        /// 0.4.0: a saved box whose node / building is no longer a valid target loses its component (no orphan box left
        /// in the save); logged. Only our own component is touched.
        /// </summary>
        private void DropSaved(Box b, string why)
        {
            b.Dropped = true;
            b.Tris = Array.Empty<AreaTriangleData>();
            b.Outline = null;
            EntityManager em = EntityManager;
            Entity e = b.Kind == BoxKind.SavedJunction ? b.Node : b.Building;
            if (e != Entity.Null && em.Exists(e) && !b.Derived)   // JR-T-014c: a derived entrance box has nothing saved to drop
            {
                // the one writer (Boxes/BoxStore.cs); 0.4.3: a building loses only this exit's box
                if (b.Kind == BoxKind.SavedJunction) BoxStore.Remove(em, e, true, notify: false);
                else BoxStore.RemoveExit(em, e, b.Key, notify: false);
            }
            Mod.log.Info($"yellow boxes: {b.Label}: box dropped ({why})");
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
            bool nodes = !m_ChangedNodeQuery.IsEmptyIgnoreFilter, edges = !m_ChangedEdgeQuery.IsEmptyIgnoreFilter, buildings = !m_ChangedBuildingQuery.IsEmptyIgnoreFilter;
            if (!nodes && !edges && !buildings) return false;
            var hit = new HashSet<Entity>();
            if (nodes) Collect(m_ChangedNodeQuery, hit);
            if (edges) Collect(m_ChangedEdgeQuery, hit);
            if (buildings) Collect(m_ChangedBuildingQuery, hit);
            if (hit.Count == 0) return false;
            bool any = false;
            foreach (Box b in m_Boxes)
            {
                bool touched = hit.Contains(b.Node) || hit.Contains(b.Building);
                foreach (Entity e in b.Edges) touched |= hit.Contains(e);
                if (!touched) continue;
                any = true;
                if (b.Kind == BoxKind.ListJunction)
                {
                    if (!EntityManager.Exists(b.Node) || EntityManager.HasComponent<Deleted>(b.Node)) Reresolve(b);
                }
                else
                {
                    // A saved box lives on its entity: a deleted node / building takes its box with it (bulldozed, or
                    // replaced by an edit that makes a new entity; ApplyNetSystem.cs 383-393). Never re-attached elsewhere.
                    Entity e = b.Kind == BoxKind.SavedJunction ? b.Node : b.Building;
                    if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e))
                    {
                        b.Dropped = true;
                        b.Tris = Array.Empty<AreaTriangleData>();
                        Mod.log.Info($"yellow boxes: {b.Label}: its {(b.Kind == BoxKind.SavedJunction ? "node" : "building")} was deleted, the box went with it");
                        continue;
                    }
                }
                RebuildBox(b);
            }
            if (any)
            {
                m_Boxes.RemoveAll(x => x.Dropped);
                bool saved = false;
                foreach (Box b in m_Boxes) saved |= b.Kind != BoxKind.ListJunction;
                s_SavedPresent = saved;
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
            b.Outline = null;
            b.Edges.Clear();
            try
            {
                if (b.Kind == BoxKind.SavedEntrance) { RebuildEntrance(b); return; }
                if (b.Node == Entity.Null) return;
                EntityManager em = EntityManager;
                if (b.Kind == BoxKind.SavedJunction)
                {
                    m_Tracked.Add(b.Node);
                    if (!BoxSelection.JunctionOk(BoxFacts.Junction(em, b.Node), out string why))
                    {
                        // an edit kept the node entity but it is no longer a junction that can take a box
                        if (em.Exists(b.Node) && em.HasBuffer<ConnectedEdge>(b.Node))
                        {
                            DynamicBuffer<ConnectedEdge> ce = em.GetBuffer<ConnectedEdge>(b.Node, true);
                            for (int i = 0; i < ce.Length; i++) m_Tracked.Add(ce[i].m_Edge);
                        }
                        DropSaved(b, "node no longer valid: " + why);
                        return;
                    }
                }
                // 0.4.2: the shape comes from Boxes/BoxShapes.cs (shared with the tool's hover outline), read-only.
                BoxShapes.Shape shape = BoxShapes.Junction(em, b.Node, m_Config.Geometry.KerbInset, CurrentMargin);
                foreach (Entity e in shape.Sources) { m_Tracked.Add(e); if (e != b.Node) b.Edges.Add(e); }
                m_Tracked.Add(b.Node);
                if (shape.Failure == "node gone") { Mod.log.Info($"yellow box paint test: {b.Label}: node gone; skipped"); return; }
                b.LastPos = shape.Position;
                var notes = new List<string>(shape.Notes);
                if (shape.Arms < 2) { Mod.log.Info($"yellow box paint test: {b.Label}: {shape.Arms} arm(s), no box"); return; }
                if (shape.Outline == null)
                {
                    // 0.4.2: no hull fallback any more (a hull is not the straight-sided box and can cover pavement)
                    Mod.log.Warn($"yellow box paint test: {b.Label}: node {b.Node}: no box: straight-sided outline failed ({shape.Failure})" + (notes.Count > 0 ? "; notes: " + string.Join("; ", notes) : ""));
                    return;
                }
                if (m_Config.Diag.TestSquare > 0)
                {
                    // diag.testSquare: one plain square on the node instead of the box (rules out the outline/hatch geometry); never merged.
                    var sq = new BoxPaintResult { Ok = true, Rect = new BoxRect { LenU = m_Config.Diag.TestSquare, LenV = m_Config.Diag.TestSquare } };
                    sq.Pieces.Add(PaintDiagParams.Square(b.LastPos, m_Config.Diag.TestSquare));
                    b.Tris = ToTriangles(sq, m_Config.Render, m_Config.Diag, out b.Bounds);
                    notes.Add($"diag.testSquare: drawing a {m_Config.Diag.TestSquare} m square on the node instead of the box");
                }
                b.Outline = shape.Outline;
                long ticks = Stopwatch.GetTimestamp() - t0;
                Mod.log.Info($"yellow box paint test: {b.Label}: node {b.Node} at {b.LastPos}, {shape.Arms} arms, outline {shape.Outline.Count} pts / {BoxPaintGeometry.SignedArea(shape.Outline):0.0} m2, solved in {ticks * 1000.0 / Stopwatch.Frequency:0.000} ms" + (notes.Count > 0 ? "; notes: " + string.Join("; ", notes) : ""));
            }
            finally
            {
                m_Meter.AddRebuild(Stopwatch.GetTimestamp() - t0);
            }
        }

        /// <summary>
        /// Exit box (0.4.3, JR-T-007c: at the building's CAR EXIT, BoxFacts.Exits; the saved key finds its exit again,
        /// Core/BuildingExit.cs ExitRules.Match). Shape: Boxes/BoxShapes.Entrance -> Core/EntranceBox.BuildStraight, the
        /// junction solver with the road's two directions as priority mouths EntranceLength apart, centred on the exit (the
        /// yielding arm's mouth centre). The exit that is gone (drive removed, building replaced by another prefab) drops
        /// this key only, logged.
        /// </summary>
        /// <summary>0.4.8 review (G9 F2): buildings already warned about an undrawn saved lot-front box (once per session).</summary>
        private readonly HashSet<Entity> m_FallbackWarned = new HashSet<Entity>();

        private void RebuildEntrance(Box b)
        {
            EntityManager em = EntityManager;
            b.HasWindow = false;
            m_Tracked.Add(b.Building);
            var exitNotes = new List<string>();
            BoxFacts.Accesses(em, b.Building, exitNotes, out List<BoxFacts.Exit> exits, out List<BoxFacts.Exit> entrances, out _, out _);
            BoxFacts.Exit exit;
            if (b.Derived)
            {
                // 0.4.8 review (G9 F1): the building lost its saved box component (its saved key was dropped): no phantom entrance box.
                if (!BoxStore.Has(em, b.Building, false))
                {
                    b.Dropped = true;
                    Mod.log.Info($"yellow boxes: {b.Label}: the building has no saved box any more, entrance box not drawn");
                    return;
                }
                // JR-T-014c (G9): the entrance this derived box stands for (same kind within ExitRules.KeyMatch)
                if (!BoxFacts.FindExit(entrances, b.Key, out exit) || !exit.IsEntrance)
                {
                    b.Dropped = true;
                    Mod.log.Info($"yellow boxes: {b.Label}: entrance gone, box not drawn (nothing saved for it)");
                    return;
                }
            }
            else if (b.Key.ExitKind == ExitKind.Fallback && !BoxFacts.FindExit(exits, b.Key, out _))
            {
                // JR-T-014c (G10): a saved Fallback key is never drawn; kept untouched in the save (no write)
                SavedFallbackFate fate = AccessRules.Fallback(false, exits.Count, entrances.Count);
                // 0.4.8 review (G9 F2): once per building and session a WARN, so a box the owner saw before and no longer sees is explained.
                if (m_FallbackWarned.Add(b.Building))
                    Mod.log.Warn($"yellow boxes: building {b.Building}: its saved lot-front box is no longer drawn (0.4.8: boxes only at real car accesses)"
                        + (fate == SavedFallbackFate.Marker ? $"; its {entrances.Count} entrance box(es) are drawn instead" : "; it has no real car access: remove the box with the tool"));
                Mod.log.Info($"yellow boxes: {b.Label}: lot-front fallback not drawn (boxes only at real car accesses); key kept as the building's box marker: "
                    + (fate == SavedFallbackFate.Marker ? $"{exits.Count} real exit(s) not boxed by it, {entrances.Count} entrance(s) boxed through it"
                       : "the building has no real car exit or entrance: nothing drawn or obeyed (remove it with the tool on the building)")
                    + (exitNotes.Count > 0 ? "; notes: " + string.Join("; ", exitNotes) : ""));
                return;
            }
            else if (!BoxFacts.FindExit(exits, b.Key, out exit))
            {
                var have = new List<string>();
                foreach (var x in exits) have.Add(x.C.Key.ToString());
                DropSaved(b, $"exit {b.Key} not found any more (building exits now: {(have.Count == 0 ? "none" : string.Join(", ", have))})");
                return;
            }
            if (!b.Derived && !exit.C.Key.Equals(b.Key))
            {
                // same exit, read under another kind / a few cm off (e.g. a fallback key once the drive shows): re-key it
                BoxStore.RemoveExit(em, b.Building, b.Key, notify: false);
                BoxStore.AddExit(em, b.Building, exit.C.Key, notify: false);
                Mod.log.Info($"yellow boxes: {b.Label}: re-keyed to {exit.C.Key}");
                b.Key = exit.C.Key;
            }
            if (exit.Node != Entity.Null) { b.Edges.Add(exit.Node); m_Tracked.Add(exit.Node); }
            EntranceFacts f = BoxFacts.ExitFacts(em, b.Building, exit, out BoxFacts.Access a);
            if (a.Edge != Entity.Null) { b.Edges.Add(a.Edge); m_Tracked.Add(a.Edge); }
            if (a.Edge != Entity.Null && em.Exists(a.Edge) && em.HasComponent<Edge>(a.Edge))
            {
                // the mouths move when a node at either end is rebuilt
                Edge ed = em.GetComponentData<Edge>(a.Edge);
                b.Edges.Add(ed.m_Start); b.Edges.Add(ed.m_End);
                m_Tracked.Add(ed.m_Start); m_Tracked.Add(ed.m_End);
            }
            if (!BoxSelection.ExitOk(f, out string why)) { DropSaved(b, "building exit no longer valid: " + why); return; }
            BoxFacts.GetFrame(em, b.Building, out V2 buildingPos, out _);
            BoxShapes.Shape shape = BoxShapes.Entrance(em, a, m_Config.Geometry.KerbInset, m_Config.Geometry.EntranceLength, CurrentMargin, buildingPos);   // 0.4.4: building side of a median
            b.LastPos = shape.Position;
            if (shape.Outline == null) { Mod.log.Warn($"yellow boxes: {b.Label}: building {b.Building}: not painted: {shape.Failure}" + (shape.Notes.Count > 0 ? "; notes: " + string.Join("; ", shape.Notes) : "")); return; }
            var notes = new List<string>(shape.Notes);
            notes.AddRange(exitNotes);
            b.Outline = shape.Outline;
            b.EdgeKey = SortedKeys.KeyOf(a.Edge.Index, a.Edge.Version);
            b.Carriageways = shape.Carriageways;
            b.HasWindow = true;
            b.WinFrom = shape.From;
            b.WinTo = shape.To;
            b.Access = a;
            b.BuildingPos = buildingPos;
            Mod.log.Info($"yellow boxes: {b.Label}: exit source {exit.C.Source}; box on edge {a.Edge} at {b.LastPos}, {shape.To - shape.From:0.0} m x {(shape.Divided ? "kerb to median" : "kerb to kerb")}, outline {shape.Outline.Count} pts" + (notes.Count > 0 ? "; notes: " + string.Join("; ", notes) : ""));
        }

        // ------------------------------------------------------------------ 0.4.4: merged drawing (JR-T-011)

        /// <summary>
        /// Groups the boxes' outlines (Core/BoxMerge.Merge: touching / overlapping boxes, and a junction box with an exit
        /// box at its mouth, become one box; never across a median) and builds each group's paint once, the hatch anchored
        /// on the group (BoxPaintGeometry.BuildPieces anchor). Runs only when a box changed (rebuild / edit / tool); a
        /// group whose rings, mode and parameters are unchanged reuses its triangles. Publishes the groups
        /// (Core MergedBoxes) for the tool's hover.
        /// </summary>
        private void Regroup()
        {
            long t0 = Stopwatch.GetTimestamp();
            var inputs = new List<BoxMergeInput>();
            var inputBoxes = new List<Box>();   // inputBoxes[i] made inputs[i] (MergedBoxGroup.Members index both)
            var drawn = new List<DrawnGroup>();
            Dictionary<Box, List<V3>> joint = JointBoxes();   // 0.4.9 (G14)
            foreach (Box b in m_Boxes)
            {
                if (b.Tris.Length > 0) { drawn.Add(new DrawnGroup { Tris = b.Tris, Bounds = b.Bounds }); continue; }   // diag test squares
                if (b.Outline == null) continue;
                inputBoxes.Add(b);
                bool exitBox = b.Kind == BoxKind.SavedEntrance;
                Entity e = exitBox ? b.Building : b.Node;
                long sub = exitBox ? BoxMergeInput.ExitSub(b.Key) : (b.Kind == BoxKind.ListJunction ? -1 : 0);
                List<V3> outline = joint.TryGetValue(b, out List<V3> jo) ? jo : b.Outline;
                inputs.Add(new BoxMergeInput(BoxMergeInput.EntityKey(e.Index, e.Version), sub, exitBox ? BoxMergeKind.Exit : BoxMergeKind.Junction, outline,
                    exitBox ? b.EdgeKey : 0, exitBox ? b.Carriageways : MedianSplit.AllCarriageways));
            }
            BoxMergeParams prm = BoxMergeParams.ForMargin(CurrentMargin);
            List<MergedBoxGroup> groups = BoxMerge.Merge(inputs, prm);
            var cache = new Dictionary<string, DrawnGroup>();
            int merged = 0, reused = 0;
            foreach (MergedBoxGroup g in groups)
            {
                string sig = Signature(g);
                if (!m_GroupCache.TryGetValue(sig, out DrawnGroup d))
                {
                    d = BuildGroup(g, inputs);
                }
                else reused++;
                cache[sig] = d;
                if (d.Tris.Length > 0) drawn.Add(d);
                if (g.Merged)
                {
                    merged++;
                    if (!m_GroupCache.ContainsKey(sig)) Mod.log.Info($"yellow boxes: {g.Members.Count} boxes drawn as one ({g.Rings.Count} ring(s), {d.Tris.Length} triangles)" + (g.Notes.Count > 0 ? "; " + string.Join("; ", g.Notes) : ""));
                }
                else if (g.Notes.Count > 0 && !m_GroupCache.ContainsKey(sig)) Mod.log.Warn("yellow boxes: " + string.Join("; ", g.Notes));
            }
            m_GroupCache = cache;
            m_Groups = drawn;
            m_MarkGroups = MarkingGroups(groups, inputBoxes);
            MergedBoxes.Publish(inputs, groups, prm);
            long ticks = Stopwatch.GetTimestamp() - t0;
            m_Meter.AddRebuild(ticks);
            if (merged > 0 || groups.Count != inputs.Count)
                Mod.log.Info($"yellow boxes: {inputs.Count} box(es) drawn as {groups.Count} ({merged} merged group(s), {reused} reused) in {ticks * 1000.0 / Stopwatch.Frequency:0.000} ms");
        }

        /// <summary>
        /// 0.4.9 (G14, owner 17 Nov 2026: near-touching boxes must be one box): exit / entrance boxes on the same road edge and
        /// carriageways whose windows overlap or are less than ExitBands.MergeGapMetres apart (Core/ExitBoxWindows) get ONE
        /// box built over their joint window (Boxes/BoxShapes.Entrance with the joint window, from the first box's access):
        /// every member gets that same outline, so BoxMerge groups them as identical rings (one box, one outline, one hatch;
        /// hover and the tool still find every member). A joint window over a road marking or into a mouth margin is refused
        /// (Core EntranceBox.WindowFits) and those boxes stay as they were (logged once). Draw-time only: nothing saved.
        /// </summary>
        private Dictionary<Box, List<V3>> JointBoxes()
        {
            var result = new Dictionary<Box, List<V3>>();
            var boxes = new List<Box>();
            var windows = new List<ExitWindow>();
            foreach (Box b in m_Boxes)
            {
                if (b.Kind != BoxKind.SavedEntrance || b.Dropped || b.Outline == null || !b.HasWindow || b.Tris.Length > 0) continue;
                boxes.Add(b);
                windows.Add(new ExitWindow(b.EdgeKey, b.Carriageways, b.WinFrom, b.WinTo));
            }
            if (boxes.Count < 2) return result;
            EntityManager em = EntityManager;
            foreach (var chain in ExitBoxWindows.Chains(windows, ExitBands.MergeGapMetres))
            {
                Box first = boxes[chain.Members[0]];
                BoxShapes.Shape shape = BoxShapes.Entrance(em, first.Access, m_Config.Geometry.KerbInset, m_Config.Geometry.EntranceLength, CurrentMargin, first.BuildingPos,
                    chain.From, chain.To);
                var labels = new List<string>();
                foreach (int m in chain.Members) labels.Add(boxes[m].Label);
                string sig = first.EdgeKey + "|" + chain.From.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "|" + chain.To.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "|" + (shape.Outline != null);
                bool log = m_JointLogged.Add(sig);
                if (shape.Outline == null)
                {
                    if (log) Mod.log.Info($"yellow boxes: {chain.Members.Count} boxes within {ExitBands.MergeGapMetres:0.0} m on edge {first.Access.Edge} kept apart: {shape.Failure} ({string.Join("; ", labels)})");
                    continue;
                }
                foreach (int m in chain.Members) result[boxes[m]] = new List<V3>(shape.Outline);
                if (log) Mod.log.Info($"yellow boxes: {chain.Members.Count} boxes within {ExitBands.MergeGapMetres:0.0} m on edge {first.Access.Edge} built as ONE box, window {chain.From:0.0}..{chain.To:0.0} m ({string.Join("; ", labels)})");
            }
            return result;
        }

        /// <summary>0.4.9 (G14): joint boxes already logged (signature), so a re-group does not repeat the line.</summary>
        private readonly HashSet<string> m_JointLogged = new HashSet<string>();

        private string Signature(MergedBoxGroup g)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append((int)m_Mode).Append('|');
            foreach (List<V3> r in g.Rings)
            {
                foreach (V3 p in r) sb.Append(Math.Round(p.X, 3)).Append(',').Append(Math.Round(p.Y, 3)).Append(',').Append(Math.Round(p.Z, 3)).Append(';');
                sb.Append('/');
            }
            return sb.ToString();
        }

        private DrawnGroup BuildGroup(MergedBoxGroup g, List<BoxMergeInput> inputs)
        {
            var d = new DrawnGroup();
            var all = new BoxPaintResult { Ok = true, Rect = g.Anchor };
            foreach (List<V3> ring in g.Rings)
            {
                BoxPaintResult res = BoxPaintGeometry.BuildPieces(new List<V3>(ring), m_Config.Geometry, m_Mode, g.Anchor);
                if (!res.Ok) { Mod.log.Warn($"yellow boxes: group of {g.Members.Count} box(es): one ring not painted: {res.Failure}"); continue; }
                all.Pieces.AddRange(res.Pieces);
                foreach (string w in res.Warnings) Mod.log.Info("yellow boxes: " + w);
            }
            if (all.Pieces.Count > 0) d.Tris = ToTriangles(all, m_Config.Render, m_Config.Diag, out d.Bounds);
            return d;
        }

        /// <summary>Pieces -> the game's AreaTriangleData layout (Game.Rendering.AreaTriangleData), one per triangle.</summary>
        private static AreaTriangleData[] ToTriangles(BoxPaintResult res, PaintRenderParams rp, PaintDiagParams diag, out Bounds3 bounds)
        {
            var list = new List<AreaTriangleData>(res.TriangleCount);
            bounds = new Bounds3(new float3(float.MaxValue), new float3(float.MinValue));
            float size = (float)Math.Max(1.0, Math.Max(res.Rect.LenU, res.Rect.LenV));
            float lodFactor = diag.LodFactor > 0 ? (float)diag.LodFactor
                : RenderingUtils.CalculateDistanceFactor(RenderingUtils.CalculateLodLimit(RenderingUtils.GetRenderingSize(new float3(size, 1f, size))));
            V2 left = res.Rect.U.Left;
            var offsetDir = diag.OffsetDir != null
                ? new float4((float)diag.OffsetDir[0], (float)diag.OffsetDir[1], (float)diag.OffsetDir[2], (float)diag.OffsetDir[3])
                : new float4((float)res.Rect.Origin.X, (float)res.Rect.Origin.Z, (float)left.X, (float)left.Z);
            foreach (PaintPiece piece in res.Pieces)
            {
                int n = piece.Ring.Count;
                for (int t = 0; t + 2 < piece.Triangles.Count; t += 3)
                {
                    int ia = piece.Triangles[t], ib = piece.Triangles[t + 1], ic = piece.Triangles[t + 2];
                    if (diag.FlipWinding) { int sw = ib; ib = ic; ic = sw; }   // negative control: clockwise
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

        // ------------------------------------------------------------------ 0.4.4: road markings under the boxes

        /// <summary>
        /// The marking-hide input: one group per merged box group (Core/BoxMerge.Merge), with the group's final rings
        /// (the union, already stopped at a median: markings on the far carriageway stay) and the union of its members'
        /// sources (road node and edges). A failed union arrives as single-box groups, so those hide per box.
        /// </summary>
        private static List<MarkingHider.Group> MarkingGroups(List<MergedBoxGroup> groups, List<Box> inputBoxes)
        {
            var result = new List<MarkingHider.Group>();
            foreach (MergedBoxGroup mg in groups)
            {
                var g = new MarkingHider.Group();
                foreach (List<V3> r in mg.Rings) if (r != null && r.Count >= 3) g.Rings.Add(r);
                if (g.Rings.Count == 0) continue;
                foreach (int i in mg.Members)
                {
                    if (i < 0 || i >= inputBoxes.Count) continue;
                    Box b = inputBoxes[i];
                    if (b.Node != Entity.Null) g.Sources.Add(b.Node);
                    foreach (Entity e in b.Edges) if (e != Entity.Null) g.Sources.Add(e);
                }
                result.Add(g);
            }
            return result;
        }

        private void ApplyMarkings()
        {
            m_MarkVersion = MergedBoxes.Version;
            if (!m_HideMarkings) { ClearMarkings(); return; }
            m_Hider.RequestApply(m_MarkGroups);   // G2: done (and logged) by MarkingRenderSystem before the next PreCullingSystem
        }

        /// <summary>G2: the clear is done (and logged) by MarkingRenderSystem before the next PreCullingSystem (also while painting is off).</summary>
        private void ClearMarkings() => m_Hider.RequestClear();

        // ------------------------------------------------------------------ 0.4.7: no parking in the boxes (G6)

        /// <summary>
        /// The no-parking input (Parking/BoxParkingSystem.cs): one group per painted saved box (its outline and the road node /
        /// edges it was built from). Paint-test list boxes are left out (they are not boxes in the city). Fed the per-box
        /// outlines (not #17's merged rings): the parking system merges the ranges of a lane, so a merged group blocks the same.
        /// </summary>
        internal static List<JunctionRules.Parking.BoxParkingSystem.Group> ParkingGroups(IEnumerable<(List<V3> Outline, Entity Node, IEnumerable<Entity> Edges)> boxes)
        {
            var groups = new List<JunctionRules.Parking.BoxParkingSystem.Group>();
            foreach (var b in boxes)
            {
                if (b.Outline == null || b.Outline.Count < 3) continue;
                var g = new JunctionRules.Parking.BoxParkingSystem.Group();
                g.Rings.Add(b.Outline);
                if (b.Node != Entity.Null) g.Sources.Add(b.Node);
                foreach (Entity e in b.Edges) if (e != Entity.Null) g.Sources.Add(e);
                groups.Add(g);
            }
            return groups;
        }

        /// <summary>Hands the current saved boxes to the parking system (empty when painting stops). Never throws.</summary>
        private void PublishParking()
        {
            try
            {
                var input = new List<(List<V3>, Entity, IEnumerable<Entity>)>();
                foreach (Box b in m_Boxes) if (b.Kind != BoxKind.ListJunction && b.Outline != null && b.Tris.Length == 0) input.Add((b.Outline, b.Node, b.Edges));   // JR-T-013m: per-box outlines (#17 draws merged groups; the parking system unions a lane's ranges, so merged groups come out the same)
                JunctionRules.Parking.BoxParkingSystem.Publish(ParkingGroups(input));
            }
            catch (Exception e)
            {
                try { Mod.log.Error(e, "yellow boxes: handing the boxes to the no-parking system failed"); } catch { }
            }
        }

        private void Upload()
        {
            var all = new List<AreaTriangleData>();
            var bounds = new Bounds3(new float3(float.MaxValue), new float3(float.MinValue));
            foreach (DrawnGroup g in m_Groups)
            {
                if (g.Tris.Length == 0) continue;
                all.AddRange(g.Tris);
                bounds |= g.Bounds;
            }
            m_TriangleCount = all.Count;
            m_Gpu.Upload(all.ToArray(), bounds, m_Config.Render);
            LogSanity(all, bounds);
            int painted = 0;
            foreach (Box b in m_Boxes) if (b.Outline != null || b.Tris.Length > 0) painted++;
            int saved = 0, listed = 0;
            foreach (Box b in m_Boxes) { if (b.Kind == BoxKind.ListJunction) listed++; else saved++; }
            s_Status = $"on, {painted} box(es) painted as {m_Groups.Count} ({saved} saved, {listed} from the test list), {m_TriangleCount} triangles";
        }

        /// <summary>0.3.1: one line per upload with what the shader gets (winding, heights, bounds, first triangle).</summary>
        private void LogSanity(List<AreaTriangleData> all, Bounds3 bounds)
        {
            if (all.Count == 0) { Mod.log.Info("yellow box paint test: upload: no triangles"); return; }
            int bad = 0;
            float lodMin = float.MaxValue, lodMax = float.MinValue;
            foreach (AreaTriangleData t in all)
            {
                if (PaintDiagParams.ShaderCross(V(t.m_APos), V(t.m_BPos), V(t.m_CPos)) <= 0) bad++;
                lodMin = math.min(lodMin, t.m_LodDistanceFactor);
                lodMax = math.max(lodMax, t.m_LodDistanceFactor);
            }
            AreaTriangleData f = all[0];
            Mod.log.Info($"yellow box paint test: upload: {all.Count} triangles, {bad} with non-positive shader winding; bounds {bounds.min} .. {bounds.max}; lod factor {lodMin:0.###}..{lodMax:0.###}; first: A {f.m_APos} B {f.m_BPos} C {f.m_CPos}, A prev/next {f.m_APrevXZ}/{f.m_ANextXZ}, y {f.m_YMinMax}, offsetDir {f.m_OffsetDir}");
        }

        private static V3 V(float3 p) => new V3(p.x, p.y, p.z);

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
