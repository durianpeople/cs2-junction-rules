using System;
using System.Collections.Generic;
using System.Diagnostics;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using JunctionRules.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace JunctionRules.Paint
{
    /// <summary>
    /// 0.4.4 (JR-T-011 part B, owner 16 Nov 2026): hides the road's own markings under the yellow boxes, render-side only.
    /// Research: docs/MARKINGS-UNDER-BOX-RESEARCH.md (lane markings are secondary net lanes drawn as tiled decal meshes
    /// along the lane curve; the "40" and arrows are decal-only secondary objects of the road).
    /// - Marking lanes: a Game.Net.CutRange buffer (the game's own per-lane "do not draw this curve range" list, which the
    ///   renderer honours: BatchInstanceSystem.cs 787-812, BatchDataSystem.cs 1137-1170; stock writes it only on crosswalk
    ///   pedestrian lanes, LaneSystem.cs 8694-8712) with the ranges inside the box (Core/MarkingMask.InsideRanges), plus
    ///   BatchesUpdated so the batches are rebuilt (PreCullingSystem.cs 2428-2436).
    /// - Marking objects: Game.Tools.Hidden (CullingData.isHidden, BatchDataSystem.cs 605/1588), plus BatchesUpdated.
    /// JR-T-013e (G2): every write runs from <see cref="Run"/>, which MarkingRenderSystem calls in PreCulling BEFORE the
    /// game's PreCullingSystem: that system reads BatchesUpdated (PreCullingSystem.cs 2428-2436 query, 652, 1895) and
    /// PrepareCleanUpSystem / CleanUpSystem take the tag off at the end of the same frame (PrepareCleanUpSystem.cs 29-40,
    /// CleanUpSystem.cs 24-31, 53), so a tag added after PreCullingSystem (where the paint system runs) was never seen. The
    /// paint system only posts requests (<see cref="RequestApply"/>, <see cref="RequestClear"/>). The bookkeeping (strip /
    /// restore / clear order) is Core/MarkingLedger.cs, tested offline.
    /// Never persisted: CutRange is a saved type, so <see cref="StripForSave"/> (run by MarkingSaveGuardSystem ahead of the
    /// game's SerializerSystem) takes ours off before every save (retry, then empty in place; anything left is logged
    /// CRITICAL) and <see cref="Run"/> puts them back the next frame; Hidden is not a saved type (no ISerializable) but is
    /// taken off for the save as well. Only lanes / objects that had no CutRange / Hidden of their own are touched
    /// ("foreign" ones are left alone), and everything this class added is removed again when a box goes, the setting
    /// goes off, painting stops, the paint fails or the mod is disposed. Driving never reads any of this.
    /// Input: groups of world-space straight-sided polygons with the road entities (nodes / edges) whose markings they may
    /// hide; overlapping polygons are fine (ranges merge). Part A feeds merged group polygons through the same call.
    /// </summary>
    internal sealed class MarkingHider
    {
        /// <summary>One box group: its polygon(s) and the road nodes / edges whose sub-lanes and sub-objects it may hide.</summary>
        internal sealed class Group
        {
            public readonly List<List<V3>> Rings = new List<List<V3>>();
            public readonly HashSet<Entity> Sources = new HashSet<Entity>();
        }

        private enum Pending { None, Apply, Clear }

        private readonly MarkingLedger<Entity> m_Ledger = new MarkingLedger<Entity>();
        private Pending m_Pending;
        private IList<Group> m_PendingGroups;

        public int LaneCount => m_Ledger.LaneCount;
        public int ObjectCount => m_Ledger.ObjectCount;
        public int ForeignLanes { get; private set; }
        public int ForeignObjects { get; private set; }

        /// <summary>True when <see cref="Run"/> has something to do (a request, or cuts / hides of ours to keep).</summary>
        public bool HasWork => m_Pending != Pending.None || !m_Ledger.IsEmpty;

        /// <summary>Paint system: hide under these groups on the next <see cref="Run"/> (replaces an earlier request).</summary>
        public void RequestApply(IList<Group> groups) { m_Pending = Pending.Apply; m_PendingGroups = groups; }

        /// <summary>Paint system: show every marking of ours again on the next <see cref="Run"/> (replaces an earlier request).</summary>
        public void RequestClear() { m_Pending = Pending.Clear; m_PendingGroups = null; }

        /// <summary>
        /// MarkingRenderSystem, once per frame before PreCullingSystem: does the request, else keeps ours in place (after
        /// a save puts our cuts / hides back; re-hides an object whose Hidden a tool took off, ToolClearSystem.cs 93-107 /
        /// Apply*System clear Hidden on the originals of a preview). Throws on an Apply / Tick write failure (the caller
        /// fails the paint, which requests a Clear).
        /// </summary>
        public void Run(EntityManager em)
        {
            Pending p = m_Pending;
            IList<Group> groups = m_PendingGroups;
            m_Pending = Pending.None;
            m_PendingGroups = null;
            if (p == Pending.Clear) { ClearNow(em); return; }
            var w = new EntityWorld(em);
            if (p == Pending.Apply)
            {
                long t0 = Stopwatch.GetTimestamp();
                Apply(em, w, groups ?? new List<Group>());
                double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                Mod.log.Info($"yellow boxes: road markings under the boxes hidden: {LaneCount} marking lane(s) cut, {ObjectCount} marking object(s) hidden"
                    + (ForeignLanes + ForeignObjects > 0 ? $"; left alone (already cut / hidden by the game or a tool): {ForeignLanes} lane(s), {ForeignObjects} object(s)" : "")
                    + $"; {ms:0.000} ms");
                return;
            }
            if (!m_Ledger.IsEmpty) m_Ledger.Tick(w);
        }

        /// <summary>Removes everything this class added right now and queues the re-render (also after a strip). Never throws.</summary>
        public void ClearNow(EntityManager em)
        {
            m_Pending = Pending.None;
            m_PendingGroups = null;
            bool any = !m_Ledger.IsEmpty;
            int failed = 0;
            Exception first = null;
            try { failed = m_Ledger.Clear(new EntityWorld(em), out first); }
            catch (Exception e) { failed++; first = e; m_Ledger.Forget(); }
            ForeignLanes = ForeignObjects = 0;
            try
            {
                if (failed > 0) Mod.log.Error(first, $"yellow boxes: putting the road markings back failed on {failed} entit(ies)");
                else if (any) Mod.log.Info("yellow boxes: road markings under the boxes shown again");
            }
            catch { }
        }

        /// <summary>A new city was loaded: the old entities are gone with the old world; just forget them (and any request).</summary>
        public void Forget()
        {
            m_Ledger.Forget();
            m_Pending = Pending.None;
            m_PendingGroups = null;
        }

        /// <summary>
        /// Right before the game serializes the city: our CutRange buffers and Hidden tags come off (no batch rebuild, so
        /// nothing flickers). Never throws; never silent: anything that may still be cut in the save is logged CRITICAL.
        /// </summary>
        public void StripForSave(EntityManager em)
        {
            MarkingStripResult r;
            try { r = m_Ledger.StripForSave(new EntityWorld(em)); }
            catch (Exception e)
            {
                try { Mod.log.Critical(e, "yellow boxes: taking the marking cuts off before the save failed outright; " + SaveWarning); } catch { }
                return;
            }
            if (r.Skipped || r.Retried == 0) return;
            try
            {
                string counts = $"{r.Lanes} lane(s), {r.Objects} object(s); {r.Retried} needed a retry, {r.Emptied} lane cut list(s) emptied in place, {r.StillCut} lane(s) still cut, {r.HiddenLeft} object(s) still hidden";
                if (r.StillCut > 0)
                    Mod.log.Critical(r.First, "yellow boxes: marking cuts could not be taken off before the save: " + counts + "; " + SaveWarning);
                else if (r.HiddenLeft > 0)
                    Mod.log.Error(r.First, "yellow boxes: marking hide tags could not be taken off before the save (Hidden is not a saved type, the save is clean): " + counts);
                else
                    Mod.log.Warn("yellow boxes: taking the marking cuts off before the save needed a fallback, the save is clean: " + counts + "; first error " + r.First?.GetType().Name + ": " + r.First?.Message);
            }
            catch { }
        }

        private const string SaveWarning = "THIS SAVE MAY CONTAIN HIDDEN ROAD MARKINGS: switch 'Hide road markings under yellow boxes' off and save again";

        private void Apply(EntityManager em, EntityWorld w, IList<Group> groups)
        {
            m_Ledger.Tick(w);   // a stripped state goes back first, so the foreign checks below see our own buffers as ours
            var laneRings = new Dictionary<Entity, List<IList<V2>>>();
            var objRings = new Dictionary<Entity, List<IList<V2>>>();
            foreach (Group g in groups)
            {
                var rings = new List<IList<V2>>();
                foreach (List<V3> r in g.Rings)
                {
                    if (r == null || r.Count < 3) continue;
                    var p = new List<V2>(r.Count);
                    foreach (V3 v in r) p.Add(v.XZ);
                    rings.Add(p);
                }
                if (rings.Count == 0) continue;
                foreach (Entity src in g.Sources)
                {
                    if (!em.Exists(src) || em.HasComponent<Deleted>(src)) continue;
                    if (em.HasBuffer<Game.Net.SubLane>(src))
                    {
                        DynamicBuffer<Game.Net.SubLane> sl = em.GetBuffer<Game.Net.SubLane>(src, true);
                        for (int i = 0; i < sl.Length; i++) Accumulate(laneRings, sl[i].m_SubLane, rings);
                    }
                    if (em.HasBuffer<Game.Objects.SubObject>(src))
                    {
                        DynamicBuffer<Game.Objects.SubObject> so = em.GetBuffer<Game.Objects.SubObject>(src, true);
                        for (int i = 0; i < so.Length; i++) Accumulate(objRings, so[i].m_SubObject, rings);
                    }
                }
            }

            // lanes
            var wantLanes = new Dictionary<Entity, List<MarkingMask.Range>>();
            int foreignLanes = 0;
            foreach (var kv in laneRings)
            {
                Entity lane = kv.Key;
                if (!IsMarkingLane(em, lane)) continue;
                Bezier4x3 bz = em.GetComponentData<Curve>(lane).m_Bezier;
                var curve = new MarkingMask.Curve(V(bz.a), V(bz.b), V(bz.c), V(bz.d));
                List<MarkingMask.Range> ranges = MarkingMask.InsideRanges(curve, kv.Value);
                if (ranges.Count == 0) continue;
                if (!m_Ledger.TracksLane(lane) && em.HasBuffer<CutRange>(lane)) { foreignLanes++; continue; }
                wantLanes[lane] = ranges;
            }

            // objects
            var wantObjects = new HashSet<Entity>();
            int foreignObjects = 0;
            foreach (var kv in objRings)
            {
                Entity o = kv.Key;
                if (!IsMarkingObject(em, o, out List<V2> footprint, out V2 centre)) continue;
                if (!MarkingMask.HideObject(centre, footprint, kv.Value)) continue;
                if (!m_Ledger.TracksObject(o) && em.HasComponent<Hidden>(o)) { foreignObjects++; continue; }
                wantObjects.Add(o);
            }
            m_Ledger.Sync(w, wantLanes, wantObjects);
            ForeignLanes = foreignLanes;
            ForeignObjects = foreignObjects;
        }

        // ------------------------------------------------------------------ filters (read-only)

        private static void Accumulate(Dictionary<Entity, List<IList<V2>>> map, Entity e, List<IList<V2>> rings)
        {
            if (!map.TryGetValue(e, out var list)) map[e] = list = new List<IList<V2>>();
            list.AddRange(rings);
        }

        /// <summary>
        /// A road marking lane: a lane with a curve whose prefab draws only decal meshes, and that is no car, pedestrian
        /// (zebras), track, parking, connection or utility lane. Temp / deleted lanes are skipped.
        /// </summary>
        private static bool IsMarkingLane(EntityManager em, Entity lane)
        {
            if (!em.Exists(lane) || em.HasComponent<Deleted>(lane) || em.HasComponent<Temp>(lane)) return false;
            if (!em.HasComponent<Curve>(lane) || !em.HasComponent<PrefabRef>(lane)) return false;
            if (em.HasComponent<Game.Net.CarLane>(lane) || em.HasComponent<Game.Net.PedestrianLane>(lane) || em.HasComponent<Game.Net.TrackLane>(lane)
                || em.HasComponent<Game.Net.ParkingLane>(lane) || em.HasComponent<Game.Net.ConnectionLane>(lane) || em.HasComponent<Game.Net.UtilityLane>(lane)) return false;
            return DecalOnly(em, em.GetComponentData<PrefabRef>(lane).m_Prefab);
        }

        /// <summary>A road marking object: decal meshes only (arrows, painted numbers), with a transform and geometry bounds.</summary>
        private static bool IsMarkingObject(EntityManager em, Entity o, out List<V2> footprint, out V2 centre)
        {
            footprint = null;
            centre = default;
            if (!em.Exists(o) || em.HasComponent<Deleted>(o) || em.HasComponent<Temp>(o)) return false;
            if (!em.HasComponent<Game.Objects.Transform>(o) || !em.HasComponent<PrefabRef>(o)) return false;
            Entity prefab = em.GetComponentData<PrefabRef>(o).m_Prefab;
            if (!DecalOnly(em, prefab) || !em.HasComponent<ObjectGeometryData>(prefab)) return false;
            Game.Objects.Transform t = em.GetComponentData<Game.Objects.Transform>(o);
            Bounds3 b = em.GetComponentData<ObjectGeometryData>(prefab).m_Bounds;
            float3 r = math.rotate(t.m_Rotation, new float3(1, 0, 0));
            float3 f = math.rotate(t.m_Rotation, new float3(0, 0, 1));
            centre = new V2(t.m_Position.x, t.m_Position.z);
            footprint = MarkingMask.Footprint(centre, new V2(r.x, r.z), new V2(f.x, f.z), b.min.x, b.max.x, b.min.z, b.max.z);
            V2 c = default;
            foreach (V2 p in footprint) c = c + p * 0.25;
            centre = c;   // the decal's own centre (bounds may be off the pivot)
            return true;
        }

        /// <summary>True when the prefab has sub-meshes and every one of them is a decal mesh.</summary>
        private static bool DecalOnly(EntityManager em, Entity prefab)
        {
            if (!em.HasBuffer<SubMesh>(prefab)) return false;
            DynamicBuffer<SubMesh> meshes = em.GetBuffer<SubMesh>(prefab, true);
            if (meshes.Length == 0) return false;
            for (int i = 0; i < meshes.Length; i++)
            {
                Entity m = meshes[i].m_SubMesh;
                if (!em.HasComponent<MeshData>(m) || (em.GetComponentData<MeshData>(m).m_State & MeshFlags.Decal) == 0) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ writes (our own buffer / tag only)

        /// <summary>The ledger's view of the EntityManager (main thread only).</summary>
        private sealed class EntityWorld : IMarkingWorld<Entity>
        {
            private readonly EntityManager m_Em;
            public EntityWorld(EntityManager em) { m_Em = em; }

            public bool Exists(Entity e) => m_Em.Exists(e);
            public bool Alive(Entity e) => m_Em.Exists(e) && !m_Em.HasComponent<Deleted>(e);
            public bool IsTemp(Entity e) => m_Em.HasComponent<Temp>(e);
            public bool HasCut(Entity lane) => m_Em.HasBuffer<CutRange>(lane);

            public void WriteCut(Entity lane, IList<MarkingMask.Range> ranges)
            {
                DynamicBuffer<CutRange> buf = m_Em.HasBuffer<CutRange>(lane) ? m_Em.GetBuffer<CutRange>(lane) : m_Em.AddBuffer<CutRange>(lane);
                buf.Clear();
                foreach (MarkingMask.Range r in ranges) buf.Add(new CutRange { m_CurveDelta = new Bounds1((float)r.Min, (float)r.Max) });
                m_Em.AddComponent<BatchesUpdated>(lane);
            }

            public void RemoveCut(Entity lane) => m_Em.RemoveComponent<CutRange>(lane);

            /// <summary>Save fallback: an empty CutRange list draws the whole lane (BatchInstanceSystem.cs 787-812: no range = 0..1).</summary>
            public void EmptyCut(Entity lane) { if (m_Em.HasBuffer<CutRange>(lane)) m_Em.GetBuffer<CutRange>(lane).Clear(); }

            public bool HasHidden(Entity o) => m_Em.HasComponent<Hidden>(o);

            public void AddHidden(Entity o)
            {
                m_Em.AddComponent<Hidden>(o);
                m_Em.AddComponent<BatchesUpdated>(o);
            }

            public void RemoveHidden(Entity o) => m_Em.RemoveComponent<Hidden>(o);

            public void Rerender(Entity e) => m_Em.AddComponent<BatchesUpdated>(e);
        }

        private static V3 V(float3 p) => new V3(p.x, p.y, p.z);
    }
}
