using System;
using System.Collections.Generic;
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
    /// Never persisted: CutRange is a saved type, so <see cref="StripForSave"/> (run by MarkingSaveGuardSystem ahead of the
    /// game's SerializerSystem) takes ours off before every save and <see cref="Tick"/> puts them back the next frame;
    /// Hidden is not a saved type (no ISerializable) but is taken off for the save as well. Only lanes / objects that had no CutRange / Hidden
    /// of their own are touched ("foreign" ones are left alone), and everything this class added is removed again when a
    /// box goes, the setting goes off, painting stops or the paint fails. Driving never reads any of this.
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

        private readonly Dictionary<Entity, List<MarkingMask.Range>> m_Lanes = new Dictionary<Entity, List<MarkingMask.Range>>();
        private readonly HashSet<Entity> m_Objects = new HashSet<Entity>();
        private bool m_Stripped;

        public int LaneCount => m_Lanes.Count;
        public int ObjectCount => m_Objects.Count;
        public int ForeignLanes { get; private set; }
        public int ForeignObjects { get; private set; }

        /// <summary>Puts the cuts / hides for these groups in place; anything of ours no longer wanted is undone.</summary>
        public void Apply(EntityManager em, IList<Group> groups)
        {
            RestoreIfStripped(em);
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
                if (!m_Lanes.ContainsKey(lane) && em.HasBuffer<CutRange>(lane)) { foreignLanes++; continue; }
                wantLanes[lane] = ranges;
            }
            foreach (var kv in new List<KeyValuePair<Entity, List<MarkingMask.Range>>>(m_Lanes))
            {
                if (!wantLanes.ContainsKey(kv.Key)) { UncutLane(em, kv.Key); m_Lanes.Remove(kv.Key); }
            }
            foreach (var kv in wantLanes)
            {
                if (m_Lanes.TryGetValue(kv.Key, out var have) && MarkingMask.SameRanges(have, kv.Value) && em.HasBuffer<CutRange>(kv.Key)) continue;
                m_Lanes[kv.Key] = kv.Value;   // tracked BEFORE the write: a failure half way still leaves it in Clear / StripForSave's reach
                CutLane(em, kv.Key, kv.Value);
            }

            // objects
            var wantObjects = new HashSet<Entity>();
            int foreignObjects = 0;
            foreach (var kv in objRings)
            {
                Entity o = kv.Key;
                if (!IsMarkingObject(em, o, out List<V2> footprint, out V2 centre)) continue;
                if (!MarkingMask.HideObject(centre, footprint, kv.Value)) continue;
                if (!m_Objects.Contains(o) && em.HasComponent<Hidden>(o)) { foreignObjects++; continue; }
                wantObjects.Add(o);
            }
            foreach (Entity o in new List<Entity>(m_Objects))
            {
                if (!wantObjects.Contains(o)) { Unhide(em, o); m_Objects.Remove(o); }
            }
            foreach (Entity o in wantObjects)
            {
                m_Objects.Add(o);   // tracked before the write (see lanes)
                if (!em.HasComponent<Hidden>(o)) Hide(em, o);
            }
            ForeignLanes = foreignLanes;
            ForeignObjects = foreignObjects;
        }

        /// <summary>
        /// Once per frame (cheap: our own few entities): after a save puts our cuts / hides back; re-hides an object whose
        /// Hidden a tool took off (ToolClearSystem.cs 93-107 / Apply*System clear Hidden on the originals of a preview).
        /// </summary>
        public void Tick(EntityManager em)
        {
            RestoreIfStripped(em);
            if (m_Objects.Count == 0) return;
            List<Entity> gone = null;
            foreach (Entity o in m_Objects)
            {
                if (!em.Exists(o) || em.HasComponent<Deleted>(o)) { (gone ??= new List<Entity>()).Add(o); continue; }
                if (!em.HasComponent<Hidden>(o) && !em.HasComponent<Temp>(o)) Hide(em, o);
            }
            if (gone != null) foreach (Entity o in gone) m_Objects.Remove(o);
        }

        /// <summary>Removes everything this class added (setting off, painting off, failure). Never throws.</summary>
        public void Clear(EntityManager em)
        {
            try
            {
                if (!m_Stripped)
                {
                    foreach (Entity l in m_Lanes.Keys) UncutLane(em, l);
                    foreach (Entity o in m_Objects) Unhide(em, o);
                }
            }
            catch (Exception e)
            {
                try { Mod.log.Error(e, "yellow boxes: putting the road markings back failed"); } catch { }
            }
            m_Lanes.Clear();
            m_Objects.Clear();
            m_Stripped = false;
            ForeignLanes = ForeignObjects = 0;
        }

        /// <summary>A new city was loaded: the old entities are gone with the old world; just forget them.</summary>
        public void Forget()
        {
            m_Lanes.Clear();
            m_Objects.Clear();
            m_Stripped = false;
        }

        /// <summary>Right before the game serializes the city: our CutRange buffers and Hidden tags come off (no batch rebuild, so nothing flickers).</summary>
        public void StripForSave(EntityManager em)
        {
            if (m_Stripped || (m_Lanes.Count == 0 && m_Objects.Count == 0)) return;
            m_Stripped = true;   // set first: whatever happens below, the next frame puts back what is still ours
            int failed = 0;
            Exception first = null;
            // one entity at a time: a failure on one never leaves the others cut in the save
            foreach (Entity l in m_Lanes.Keys)
            {
                try { if (em.Exists(l) && em.HasBuffer<CutRange>(l)) em.RemoveComponent<CutRange>(l); }
                catch (Exception e) { failed++; first = first ?? e; }
            }
            foreach (Entity o in m_Objects)
            {
                try { if (em.Exists(o) && em.HasComponent<Hidden>(o)) em.RemoveComponent<Hidden>(o); }
                catch (Exception e) { failed++; first = first ?? e; }
            }
            if (failed > 0) throw new InvalidOperationException($"{failed} marking cut(s) could not be taken off before the save", first);
        }

        private void RestoreIfStripped(EntityManager em)
        {
            if (!m_Stripped) return;
            m_Stripped = false;
            foreach (var kv in new List<KeyValuePair<Entity, List<MarkingMask.Range>>>(m_Lanes))
            {
                if (!em.Exists(kv.Key) || em.HasComponent<Deleted>(kv.Key)) { m_Lanes.Remove(kv.Key); continue; }
                if (em.HasBuffer<CutRange>(kv.Key)) { m_Lanes.Remove(kv.Key); continue; }   // someone else's now
                WriteCut(em, kv.Key, kv.Value, rebuild: true);   // re-instanced meanwhile (LOD / camera)? rebuild to be sure
            }
            foreach (Entity o in new List<Entity>(m_Objects))
            {
                if (!em.Exists(o) || em.HasComponent<Deleted>(o)) { m_Objects.Remove(o); continue; }
                if (!em.HasComponent<Hidden>(o)) Hide(em, o);
            }
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

        private static void CutLane(EntityManager em, Entity lane, List<MarkingMask.Range> ranges) => WriteCut(em, lane, ranges, rebuild: true);

        private static void WriteCut(EntityManager em, Entity lane, List<MarkingMask.Range> ranges, bool rebuild)
        {
            DynamicBuffer<CutRange> buf = em.HasBuffer<CutRange>(lane) ? em.GetBuffer<CutRange>(lane) : em.AddBuffer<CutRange>(lane);
            buf.Clear();
            foreach (MarkingMask.Range r in ranges) buf.Add(new CutRange { m_CurveDelta = new Bounds1((float)r.Min, (float)r.Max) });
            if (rebuild) em.AddComponent<BatchesUpdated>(lane);
        }

        private static void UncutLane(EntityManager em, Entity lane)
        {
            if (!em.Exists(lane) || em.HasComponent<Deleted>(lane)) return;
            if (em.HasBuffer<CutRange>(lane)) em.RemoveComponent<CutRange>(lane);
            em.AddComponent<BatchesUpdated>(lane);
        }

        private static void Hide(EntityManager em, Entity o)
        {
            em.AddComponent<Hidden>(o);
            em.AddComponent<BatchesUpdated>(o);
        }

        private static void Unhide(EntityManager em, Entity o)
        {
            if (!em.Exists(o) || em.HasComponent<Deleted>(o)) return;
            if (em.HasComponent<Hidden>(o)) em.RemoveComponent<Hidden>(o);
            em.AddComponent<BatchesUpdated>(o);
        }

        private static V3 V(float3 p) => new V3(p.x, p.y, p.z);
    }
}
