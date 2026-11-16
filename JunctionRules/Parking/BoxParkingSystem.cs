using System;
using System.Collections.Generic;
using System.Diagnostics;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Serialization;
using Game.Tools;
using Game.Vehicles;
using JunctionRules.Core;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace JunctionRules.Parking
{
    /// <summary>
    /// 0.4.7 (JR-T-013d, gap G6, owner rule 17 Nov 2026): no parking inside a yellow box. Research and cites:
    /// Core/ParkingMask.cs (how stock blocks slots) and DEVIATIONS.md section G6.
    ///
    /// Mechanism (runtime only): for every parking lane of a box's road entities (node + edges of a junction box; the road
    /// edge and its end nodes of an exit box), the curve ranges whose strip overlaps the box polygon go into the lane's own
    /// Game.Net.LaneOverlap buffer as one extra entry per range, marked as ours by pointing at the lane itself (m_Other ==
    /// lane, no flags; the game never makes a lane overlap itself). The game's parking code then treats those ranges like a
    /// driveway: the slot search skips them (VehicleUtils.FindFreeParkingSpace, used by ValidateParkingSpace for every car
    /// heading to a space, so a car arriving picks another space or re-paths), the pathfinder's free-space value leaves them
    /// out (ParkingLaneDataSystem.CalculateFreeSpace, recomputed because we add PathfindUpdated), and parked cars are never
    /// moved: FixParkingLocationSystem runs only for Updated lanes (FixParkingLocationSystem.cs 1776-1795) and in
    /// Modification5, before this system.
    ///
    /// Order: ModificationEnd, before the game's ParkingLaneDataSystem. LaneOverlapSystem (Modification4B) rebuilds the
    /// buffer of an Updated lane without our entries (road upgrade, Traffic / RoadBuilder lane rewrite, load); this system
    /// sees the Updated parking lane of a box road the same frame and puts them back before the free space is recomputed.
    ///
    /// Save safety: LaneOverlap is IEmptySerializable (its entries are never saved). The only saved value our entries
    /// influence is ParkingLane.m_FreeSpace; right before every save (Serialize phase, BoxParkingSaveGuardSystem) it is set
    /// back to the game's own value computed without our entries (ParkingFreeSpaceCopy, the game's code), and after the
    /// save the lanes get PathfindUpdated so the game recomputes it with them. ParkingLane flags are never written.
    /// Box removed / setting off / failure: our entries come off and the game recomputes the lane (PathfindUpdated).
    /// Input: the paint system publishes one group per painted saved box (polygon + its road entities); merged boxes may
    /// publish per-box or union polygons, the ranges of one lane are merged here either way.
    /// </summary>
    public class BoxParkingSystem : GameSystemBase
    {
        /// <summary>One box (or merged box group): its outline polygon(s), world space, and the road entities whose lanes it may cover.</summary>
        internal sealed class Group
        {
            public readonly List<List<V3>> Rings = new List<List<V3>>();
            public readonly HashSet<Entity> Sources = new HashSet<Entity>();
        }

        private static BoxParkingSystem s_Instance;
        private static List<Group> s_Input = new List<Group>();
        private static int s_InputVersion;
        private static bool s_Failed;
        private static string s_Status = "no boxes";

        /// <summary>Options status text (also used in the probe note).</summary>
        internal static string StatusText => s_Failed ? "failed, off until the game restarts" : s_Status;

        private readonly Dictionary<Entity, List<(byte Start, byte End)>> m_Applied = new Dictionary<Entity, List<(byte, byte)>>();
        private readonly HashSet<Entity> m_Sources = new HashSet<Entity>();
        private readonly HashSet<Entity> m_AfterSave = new HashSet<Entity>();
        private int m_SeenVersion = -1;
        private bool m_SettingOn;
        private bool m_SettingKnown;
        private EntityQuery m_ChangedParkingQuery;
        /// <summary>Review F1: road nodes / edges with lanes updated this frame (an edit next to a box: see OnUpdate).</summary>
        private EntityQuery m_UpdatedNetQuery;
        /// <summary>Review F3: lanes whose buffer the current Apply wrote, until m_Applied holds them (Fail opens them too).</summary>
        private readonly HashSet<Entity> m_Writing = new HashSet<Entity>();
        private SaveGameSystem m_SaveSystem;

        /// <summary>The paint system's boxes changed (or went): main thread. Never throws.</summary>
        internal static void Publish(List<Group> groups)
        {
            s_Input = groups ?? new List<Group>();
            s_InputVersion++;
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_ChangedParkingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Net.ParkingLane>() },
                Any = new[] { ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            });
            m_UpdatedNetQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Net.SubLane>(), ComponentType.ReadOnly<Updated>() },
                Any = new[] { ComponentType.ReadOnly<Game.Net.Node>(), ComponentType.ReadOnly<Game.Net.Edge>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            m_SaveSystem = World.GetOrCreateSystemManaged<SaveGameSystem>();
            s_Instance = this;
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
            base.OnDestroy();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            // the old city's lanes went with it (and LaneOverlapSystem rebuilds every buffer after a load)
            m_Applied.Clear();
            m_Writing.Clear();
            m_Sources.Clear();
            m_AfterSave.Clear();
            m_SeenVersion = -1;
            s_Status = "no boxes";
        }

        private bool SaveRunning => m_SaveSystem != null && m_SaveSystem.Enabled;

        [Preserve]
        protected override void OnUpdate()
        {
            if (s_Failed) { Enabled = false; return; }
            string step = "starting";
            try
            {
                // a save is being written: change nothing until it is done (the free space of our lanes is the game's own)
                if (SaveRunning) return;
                EntityManager em = EntityManager;
                if (m_AfterSave.Count > 0)
                {
                    step = "putting the box ranges back after the save";
                    int n = AddPathfindUpdated(em, m_AfterSave);
                    m_AfterSave.Clear();
                    Mod.log.Info($"yellow boxes: no parking in boxes: after the save, {n} parking lane(s) recomputed with the boxes again");
                }
                bool on = Mod.Settings == null || Mod.Settings.NoParkingInBoxes;
                bool need = s_InputVersion != m_SeenVersion || !m_SettingKnown || on != m_SettingOn;
                if (!need && (m_Applied.Count > 0 || m_Sources.Count > 0) && !m_ChangedParkingQuery.IsEmptyIgnoreFilter)
                {
                    step = "checking updated parking lanes";
                    need = TouchesOurs(em);
                }
                // Review F1: the game's LaneOverlapSystem also rebuilds the overlap buffers of every edge connected to an
                // Updated node, without tagging those lanes Updated (LaneOverlapSystem.cs 26-56 AddNonUpdatedEdgesJob, buffer
                // cleared in UpdateLaneOverlapsJob 1077). A box does not list the far-end node of its edges, so an edit at the
                // next junction (a sign, a lane connection) wiped our entries unseen. On any road update: re-check our lanes.
                if (!need && m_Applied.Count > 0 && !m_UpdatedNetQuery.IsEmptyIgnoreFilter)
                {
                    step = "checking the box parking lanes after a road update";
                    need = LostOurs(em);
                    if (need) Mod.log.Info("yellow boxes: no parking in boxes: a road update next to a box removed the block from a parking lane; putting it back");
                }
                if (!need) return;
                bool settingChanged = m_SettingKnown && on != m_SettingOn;
                m_SeenVersion = s_InputVersion;
                m_SettingOn = on;
                m_SettingKnown = true;
                step = "blocking parking in the boxes";
                Apply(em, on ? s_Input : new List<Group>(), settingChanged);
            }
            catch (Exception e)
            {
                Fail(step, e);
            }
        }

        /// <summary>True when an Updated / Deleted parking lane is one of ours or belongs to a box road.</summary>
        private bool TouchesOurs(EntityManager em)
        {
            NativeArray<Entity> lanes = m_ChangedParkingQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < lanes.Length; i++)
                {
                    Entity l = lanes[i];
                    if (m_Applied.ContainsKey(l)) return true;
                    if (em.HasComponent<Owner>(l) && m_Sources.Contains(em.GetComponentData<Owner>(l).m_Owner)) return true;
                }
                return false;
            }
            finally
            {
                lanes.Dispose();
            }
        }

        /// <summary>Review F1: true when a lane we block no longer holds exactly our entries (the game rebuilt its buffer).</summary>
        private bool LostOurs(EntityManager em)
        {
            foreach (var kv in m_Applied)
            {
                Entity lane = kv.Key;
                if (!em.Exists(lane) || em.HasComponent<Deleted>(lane) || !em.HasBuffer<LaneOverlap>(lane)) continue;
                DynamicBuffer<LaneOverlap> buf = em.GetBuffer<LaneOverlap>(lane, true);
                var current = new List<OverlapEntry>(buf.Length);
                for (int i = 0; i < buf.Length; i++) current.Add(new OverlapEntry(buf[i].m_ThisStart, buf[i].m_ThisEnd, IsOurs(buf[i], lane), i));
                if (!ParkingMask.HasExactly(current, kv.Value)) return true;
            }
            return false;
        }

        private void Apply(EntityManager em, List<Group> groups, bool settingChanged)
        {
            m_Writing.Clear();
            long t0 = Stopwatch.GetTimestamp();
            var want = new Dictionary<Entity, List<TRange>>();
            var info = new Dictionary<Entity, LaneInfo>();
            m_Sources.Clear();
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
                    if (src == Entity.Null) continue;
                    m_Sources.Add(src);
                    if (!em.Exists(src) || em.HasComponent<Deleted>(src) || !em.HasBuffer<Game.Net.SubLane>(src)) continue;
                    DynamicBuffer<Game.Net.SubLane> subLanes = em.GetBuffer<Game.Net.SubLane>(src, true);
                    var lanes = new List<Entity>(subLanes.Length);
                    for (int i = 0; i < subLanes.Length; i++) lanes.Add(subLanes[i].m_SubLane);
                    foreach (Entity lane in lanes)
                    {
                        if (!info.TryGetValue(lane, out LaneInfo li))
                        {
                            li = ReadLane(em, lane);
                            info[lane] = li;
                        }
                        if (!li.Ok) continue;
                        List<TRange> ranges = ParkingMask.InsideRanges(li.A, li.B, li.C, li.D, li.HalfWidth, rings);
                        if (ranges.Count == 0) continue;
                        if (!want.TryGetValue(lane, out List<TRange> have)) want[lane] = have = new List<TRange>();
                        have.AddRange(ranges);
                    }
                }
            }
            var wantBytes = new Dictionary<Entity, List<(byte Start, byte End)>>();
            foreach (var kv in want) wantBytes[kv.Key] = ParkingMask.ToBytes(kv.Value);

            var touched = new List<Entity>();
            int freed = 0;
            foreach (Entity lane in m_Applied.Keys)
            {
                if (wantBytes.ContainsKey(lane)) continue;
                m_Writing.Add(lane);
                if (WriteOurs(em, lane, null)) { touched.Add(lane); freed++; }
            }
            int ranges2 = 0, slots = 0, slotted = 0;
            foreach (var kv in wantBytes)
            {
                ranges2 += kv.Value.Count;
                m_Writing.Add(kv.Key);   // review F3: before the write, so a throw inside it is opened by Fail too
                if (WriteOurs(em, kv.Key, kv.Value)) touched.Add(kv.Key);
                if (info.TryGetValue(kv.Key, out LaneInfo li) && li.SlotCount > 0)
                {
                    slotted++;
                    slots += ParkingMask.BlockedSlots(ParkingMask.SlotRanges(li.A, li.B, li.C, li.D, li.Length, li.SlotCount, li.SlotInterval, li.Starting, li.Ending), kv.Value);
                }
            }
            m_Applied.Clear();
            foreach (var kv in wantBytes) m_Applied[kv.Key] = kv.Value;
            m_Writing.Clear();
            int updated = AddPathfindUpdated(em, touched);
            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            s_Status = m_SettingOn
                ? $"{wantBytes.Count} parking lane(s) blocked in {groups.Count} box(es) ({slots} marked space(s))"
                : "off";
            if (touched.Count > 0 || settingChanged)
                Mod.log.Info($"yellow boxes: no parking in boxes {(m_SettingOn ? "ON" : "OFF")}: {groups.Count} box(es), {wantBytes.Count} parking lane(s) with {ranges2} blocked range(s)"
                    + $" ({slots} marked space(s) on {slotted} slotted lane(s)); {freed} lane(s) opened again; {updated} lane(s) recomputed; {ms:0.000} ms");
        }

        private struct LaneInfo
        {
            public bool Ok;
            public V3 A, B, C, D;
            public double HalfWidth, Length;
            public int SlotCount;
            public double SlotInterval;
            public bool Starting, Ending;
        }

        /// <summary>A parking lane of a road (not a taxi / virtual stand), with its curve, width and (for the log) slots. Read-only.</summary>
        private static LaneInfo ReadLane(EntityManager em, Entity lane)
        {
            var li = new LaneInfo();
            if (!em.Exists(lane) || em.HasComponent<Deleted>(lane) || em.HasComponent<Temp>(lane)) return li;
            if (!em.HasComponent<Game.Net.ParkingLane>(lane) || !em.HasComponent<Curve>(lane) || !em.HasComponent<PrefabRef>(lane) || !em.HasBuffer<LaneOverlap>(lane)) return li;
            Game.Net.ParkingLane pl = em.GetComponentData<Game.Net.ParkingLane>(lane);
            if ((pl.m_Flags & ParkingLaneFlags.VirtualLane) != 0) return li;
            Curve curve = em.GetComponentData<Curve>(lane);
            Entity prefab = em.GetComponentData<PrefabRef>(lane).m_Prefab;
            double width = em.HasComponent<NetLaneData>(prefab) ? em.GetComponentData<NetLaneData>(prefab).m_Width : 0;
            if (width <= 0.1) width = 2.0;
            Bezier4x3 b = curve.m_Bezier;
            li.A = new V3(b.a.x, b.a.y, b.a.z);
            li.B = new V3(b.b.x, b.b.y, b.b.z);
            li.C = new V3(b.c.x, b.c.y, b.c.z);
            li.D = new V3(b.d.x, b.d.y, b.d.z);
            li.HalfWidth = width * 0.5;
            li.Length = curve.m_Length;
            if (em.HasComponent<ParkingLaneData>(prefab))
            {
                ParkingLaneData pld = em.GetComponentData<ParkingLaneData>(prefab);
                if (pld.m_SlotInterval != 0f)
                {
                    li.SlotCount = NetUtils.GetParkingSlotCount(curve, pl, pld);
                    li.SlotInterval = NetUtils.GetParkingSlotInterval(curve, pl, pld, li.SlotCount);
                }
            }
            li.Starting = (pl.m_Flags & ParkingLaneFlags.StartingLane) != 0;
            li.Ending = (pl.m_Flags & ParkingLaneFlags.EndingLane) != 0;
            li.Ok = true;
            return li;
        }

        /// <summary>The mod's own entry: the lane overlapping itself with no flags (the game never makes one).</summary>
        internal static bool IsOurs(LaneOverlap o, Entity lane)
            => o.m_Other == lane && o.m_Flags == 0 && o.m_ThisStart == o.m_OtherStart && o.m_ThisEnd == o.m_OtherEnd && o.m_Parallelism == 0 && o.m_PriorityDelta == 0;

        private static LaneOverlap Ours(Entity lane, byte start, byte end)
            => new LaneOverlap { m_Other = lane, m_Flags = 0, m_ThisStart = start, m_ThisEnd = end, m_OtherStart = start, m_OtherEnd = end, m_Parallelism = 0, m_PriorityDelta = 0 };

        /// <summary>
        /// Replaces our entries in the lane's overlap buffer by <paramref name="wanted"/> (null / empty = none of ours left),
        /// stock entries kept, ordered by the game's key (Core/ParkingMask.Plan). Returns true when the buffer changed.
        /// </summary>
        private static bool WriteOurs(EntityManager em, Entity lane, List<(byte Start, byte End)> wanted)
        {
            if (!em.Exists(lane) || em.HasComponent<Deleted>(lane) || !em.HasBuffer<LaneOverlap>(lane)) return false;
            DynamicBuffer<LaneOverlap> buf = em.GetBuffer<LaneOverlap>(lane);
            var current = new List<OverlapEntry>(buf.Length);
            var stock = new List<LaneOverlap>(buf.Length);
            for (int i = 0; i < buf.Length; i++)
            {
                LaneOverlap o = buf[i];
                bool ours = IsOurs(o, lane);
                current.Add(new OverlapEntry(o.m_ThisStart, o.m_ThisEnd, ours, i));
                if (!ours) stock.Add(o);
            }
            if (ParkingMask.HasExactly(current, wanted)) return false;
            List<OverlapEntry> plan = ParkingMask.Plan(current, wanted);
            // Plan numbers the stock entries 0..k-1 in their order, then ours
            buf.Clear();
            foreach (OverlapEntry e in plan)
            {
                if (e.Ours) buf.Add(Ours(lane, e.Start, e.End));
                else buf.Add(stock[e.Index]);
            }
            return true;
        }

        private static int AddPathfindUpdated(EntityManager em, IEnumerable<Entity> lanes)
        {
            var alive = new List<Entity>();
            foreach (Entity l in lanes) if (em.Exists(l) && !em.HasComponent<Deleted>(l) && !em.HasComponent<PathfindUpdated>(l)) alive.Add(l);
            if (alive.Count == 0) return 0;
            var arr = new NativeArray<Entity>(alive.ToArray(), Allocator.Temp);
            try { em.AddComponent<PathfindUpdated>(arr); }
            finally { arr.Dispose(); }
            return alive.Count;
        }

        private void Fail(string step, Exception e)
        {
            s_Failed = true;
            try { Mod.log.Error(e, $"yellow boxes: no parking in boxes failed while {step}; switched off until the game restarts, parking in the boxes opened again"); } catch { }
            try
            {
                EntityManager em = EntityManager;
                var touched = new List<Entity>();
                // review F3: also the lanes the failed Apply wrote before it threw (not yet in m_Applied)
                var open = new HashSet<Entity>(m_Applied.Keys);
                open.UnionWith(m_Writing);
                foreach (Entity l in open) { try { if (WriteOurs(em, l, null)) touched.Add(l); } catch { } }
                AddPathfindUpdated(em, touched);
            }
            catch (Exception e2)
            {
                try { Mod.log.Error(e2, "yellow boxes: opening the parking in the boxes again failed"); } catch { }
            }
            m_Applied.Clear();
            m_Writing.Clear();
            m_Sources.Clear();
            Enabled = false;
        }

        // ------------------------------------------------------------------ save

        /// <summary>
        /// Serialize phase, ahead of the game's serializer (BoxParkingSaveGuardSystem): every parking lane we block gets the
        /// free-space value the game computes without our entries (Parking/ParkingFreeSpaceCopy.cs, the game's own code), so the
        /// save holds exactly what the game would have saved without the mod; the entries themselves are never saved
        /// (LaneOverlap is IEmptySerializable). After the save, OnUpdate gives these lanes PathfindUpdated. Never throws; a
        /// lane whose value cannot be computed gets the larger of its value and a whole car's length (the game re-checks a
        /// space when a car arrives) so no save ever shows such a lane as full because of a box.
        /// </summary>
        internal static void PrepareForSave(EntityManager em)
        {
            BoxParkingSystem s = s_Instance;
            if (s == null || s.m_Applied.Count == 0) return;
            int lanes = 0, changed = 0, failed = 0;
            try
            {
                em.CompleteAllTrackedJobs();
                var copy = new ParkingFreeSpaceCopy
                {
                    m_LaneData = s.GetComponentLookup<Lane>(true),
                    m_CarLaneData = s.GetComponentLookup<Game.Net.CarLane>(true),
                    m_ParkedCarData = s.GetComponentLookup<ParkedCar>(true),
                    m_UnspawnedData = s.GetComponentLookup<Unspawned>(true),
                    m_PrefabRefData = s.GetComponentLookup<PrefabRef>(true),
                    m_ObjectGeometryData = s.GetComponentLookup<ObjectGeometryData>(true),
                    m_SubLanes = s.GetBufferLookup<Game.Net.SubLane>(true),
                };
                foreach (Entity lane in s.m_Applied.Keys)
                {
                    if (!em.Exists(lane) || em.HasComponent<Deleted>(lane) || !em.HasComponent<Game.Net.ParkingLane>(lane)) continue;
                    lanes++;
                    s.m_AfterSave.Add(lane);
                    if (StockFreeSpace(em, ref copy, lane, out float stock))
                    {
                        Game.Net.ParkingLane pl = em.GetComponentData<Game.Net.ParkingLane>(lane);
                        if (pl.m_FreeSpace != stock)
                        {
                            pl.m_FreeSpace = stock;
                            em.SetComponentData(lane, pl);
                            changed++;
                        }
                    }
                    else
                    {
                        failed++;
                        Optimistic(em, lane);
                    }
                }
                Mod.log.Info($"yellow boxes: no parking in boxes: before the save, {lanes} parking lane(s) carry the game's own free space ({changed} changed"
                    + (failed > 0 ? $", {failed} could not be computed and were marked free" : "") + ")");
            }
            catch (Exception e)
            {
                try { Mod.log.Error(e, "yellow boxes: preparing the parking lanes for the save failed; marking them free"); } catch { }
                foreach (Entity lane in s.m_Applied.Keys) { try { Optimistic(em, lane); s.m_AfterSave.Add(lane); } catch { } }
            }
        }

        private static bool StockFreeSpace(EntityManager em, ref ParkingFreeSpaceCopy copy, Entity lane, out float stock)
        {
            stock = 0f;
            NativeArray<LaneObject> objects = default;
            NativeArray<LaneOverlap> overlaps = default;
            try
            {
                Game.Net.ParkingLane pl = em.GetComponentData<Game.Net.ParkingLane>(lane);
                Curve curve = em.GetComponentData<Curve>(lane);
                Entity prefab = em.GetComponentData<PrefabRef>(lane).m_Prefab;
                if (!em.HasComponent<ParkingLaneData>(prefab)) return false;
                ParkingLaneData pld = em.GetComponentData<ParkingLaneData>(prefab);
                Owner owner = em.HasComponent<Owner>(lane) ? em.GetComponentData<Owner>(lane) : default;
                Lane laneData = em.GetComponentData<Lane>(lane);
                DynamicBuffer<LaneObject> lo = em.GetBuffer<LaneObject>(lane, true);
                objects = new NativeArray<LaneObject>(lo.Length, Allocator.Temp);
                for (int i = 0; i < lo.Length; i++) objects[i] = lo[i];
                objects.Sort();   // as the game's job does first (ParkingLaneDataSystem.cs 226)
                DynamicBuffer<LaneOverlap> ob = em.GetBuffer<LaneOverlap>(lane, true);
                int n = 0;
                for (int i = 0; i < ob.Length; i++) if (!IsOurs(ob[i], lane)) n++;
                overlaps = new NativeArray<LaneOverlap>(n, Allocator.Temp);
                n = 0;
                for (int i = 0; i < ob.Length; i++) if (!IsOurs(ob[i], lane)) overlaps[n++] = ob[i];
                Bounds1 blocked = copy.GetBlockedRange(owner, laneData);
                stock = copy.CalculateFreeSpace(curve, pl, pld, objects, overlaps, blocked);
                return true;
            }
            catch (Exception e)
            {
                try { Mod.log.Warn($"yellow boxes: free space of parking lane {lane} not computed for the save: {e.GetType().Name}: {e.Message}"); } catch { }
                return false;
            }
            finally
            {
                if (objects.IsCreated) objects.Dispose();
                if (overlaps.IsCreated) overlaps.Dispose();
            }
        }

        private static void Optimistic(EntityManager em, Entity lane)
        {
            if (!em.Exists(lane) || !em.HasComponent<Game.Net.ParkingLane>(lane)) return;
            Game.Net.ParkingLane pl = em.GetComponentData<Game.Net.ParkingLane>(lane);
            if ((pl.m_Flags & ParkingLaneFlags.VirtualLane) != 0) return;
            float whole = em.GetComponentData<Curve>(lane).m_Length;
            Entity prefab = em.GetComponentData<PrefabRef>(lane).m_Prefab;
            if (em.HasComponent<ParkingLaneData>(prefab))
            {
                float max = em.GetComponentData<ParkingLaneData>(prefab).m_MaxCarLength;
                if (max != 0f) whole = Math.Min(whole, max);
            }
            if (pl.m_FreeSpace < whole)
            {
                pl.m_FreeSpace = whole;
                em.SetComponentData(lane, pl);
            }
        }
    }

    /// <summary>
    /// 0.4.7 (JR-T-013d): runs in SystemUpdatePhase.Serialize ahead of the game's SerializerSystem (registered with
    /// UpdateBefore like the game's own TrimPathsSystem, SystemOrder.cs 731-735; the phase runs only while a save is written,
    /// SaveGameSystem.cs 82, and every save, manual, auto or on quit, goes through SaveGameSystem: GameManager.cs 945).
    /// Puts the game's own free-space value on the parking lanes the yellow boxes block (BoxParkingSystem.PrepareForSave).
    /// Does nothing when no lane is blocked. Never throws.
    /// </summary>
    public class BoxParkingSaveGuardSystem : GameSystemBase
    {
        [Preserve]
        protected override void OnUpdate()
        {
            try { BoxParkingSystem.PrepareForSave(EntityManager); }
            catch (Exception e) { try { Mod.log.Error(e, "yellow boxes: parking save guard failed"); } catch { } }
        }

        [Preserve]
        public BoxParkingSaveGuardSystem()
        {
        }
    }
}
