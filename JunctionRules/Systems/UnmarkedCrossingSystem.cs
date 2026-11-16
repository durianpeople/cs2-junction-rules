using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Creatures;
using Game.Net;
using Game.Objects;
using Game.Pathfind;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;
using JunctionRules.Core;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace JunctionRules.Systems
{
    /// <summary>
    /// JR-T-014d (G11, owner rule 17 Nov 2026), person side of "People give way at unmarked crossings" (Core/UnmarkedCrossing.cs
    /// has the stock analysis with cites). Runs right after the game's HumanNavigationSystem on the same UpdateFrame slice
    /// (frameIndex % 16), like PeoplePassSystem, so it sees each person just after the game set their target. A person on an
    /// unmarked crossing (crosswalk lane with Unsafe, no signal) who has not yet stepped past the kerb edge of the
    /// carriageway is held there while any approaching vehicle (on a crossed road lane before the crossing, or on a lane
    /// feeding it whose route takes that lane or is not known yet) could reach the crossing before the person is past it plus
    /// a 1 s margin (stock braking distance + speed x time, as the whole-junction gap). Held = the target pulled back to the
    /// kerb edge, exactly where the game's own clamp (CreatureTargetIterator.CheckOverlapLane) would put it, the walking
    /// speed capped to the game's braking speed for that distance, blocker = the car (type Crossing). Once a person is on the
    /// carriageway nothing here touches them (the game's own clamp and the car-side safety floor apply). No game code is
    /// copied; nothing changes for zebras, signal crossings, sidewalks or driveways, nor with the setting off.
    ///
    /// The people pass (PeoplePassSystem) stays for every crossing, unmarked included: it only acts on a vehicle STANDING
    /// on the crossing (a queue across it, not a car giving way); this system never counts standing vehicles, so a person
    /// held by a queued car still goes through it after 2 s, and the car-side floor stops that car if it moves off meanwhile.
    ///
    /// Diagnostics (always on): counters and records in Systems/UnmarkedTrace.cs, drained here on the main thread with line
    /// caps (Core/UnmarkedTraceLog.cs).
    /// </summary>
    public class UnmarkedCrossingSystem : GameSystemBase
    {
        private struct Wait
        {
            public uint Start;
            public uint Last;
            public byte LongReported;
        }

        private const uint kSweepEvery = 4096u;
        private const uint kStaleFrames = 64u;

        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_HumanQuery;
        private NativeParallelHashMap<Entity, Wait> m_Waits;
        private readonly UnmarkedTraceDrain m_Drain = new UnmarkedTraceDrain();

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            UnmarkedTrace.Ensure();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_HumanQuery = GetEntityQuery(ComponentType.ReadOnly<Human>(), ComponentType.ReadOnly<UpdateFrame>(),
                ComponentType.ReadWrite<HumanCurrentLane>(), ComponentType.ReadWrite<HumanNavigation>(), ComponentType.ReadWrite<Blocker>(),
                ComponentType.ReadOnly<Transform>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            m_Waits = new NativeParallelHashMap<Entity, Wait>(256, Allocator.Persistent);
        }

        [Preserve]
        protected override void OnDestroy()
        {
            Dependency.Complete();
            if (m_Waits.IsCreated) m_Waits.Dispose();
            base.OnDestroy();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            bool on = Mod.Settings == null || Mod.Settings.PeopleGiveWayUnmarked;
            uint frame = m_SimulationSystem.frameIndex;
            UnmarkedTrace.SetFrame(frame);
            DrainLog(frame, on);
            if (!on)
            {
                if (!m_Waits.IsEmpty)
                {
                    Dependency.Complete();
                    m_Waits.Clear();
                }
                return;
            }
            var slice = new UpdateFrame(frame % 16);
            m_HumanQuery.ResetFilter();
            m_HumanQuery.SetSharedComponentFilter(slice);
            var job = new HoldJob
            {
                m_EntityType = GetEntityTypeHandle(),
                m_CurrentLaneType = GetComponentTypeHandle<HumanCurrentLane>(false),
                m_NavigationType = GetComponentTypeHandle<HumanNavigation>(false),
                m_BlockerType = GetComponentTypeHandle<Blocker>(false),
                m_TransformType = GetComponentTypeHandle<Transform>(true),
                m_PrefabRefType = GetComponentTypeHandle<PrefabRef>(true),
                m_PedestrianLaneData = GetComponentLookup<Game.Net.PedestrianLane>(true),
                m_LaneSignalData = GetComponentLookup<LaneSignal>(true),
                m_CurveData = GetComponentLookup<Curve>(true),
                m_LaneData = GetComponentLookup<Lane>(true),
                m_OwnerData = GetComponentLookup<Owner>(true),
                m_NodeLaneData = GetComponentLookup<NodeLane>(true),
                m_PrefabRefData = GetComponentLookup<PrefabRef>(true),
                m_NetLaneData = GetComponentLookup<NetLaneData>(true),
                m_HumanData = GetComponentLookup<HumanData>(true),
                m_GeometryData = GetComponentLookup<ObjectGeometryData>(true),
                m_CarData = GetComponentLookup<Car>(true),
                m_PrefabCarData = GetComponentLookup<CarData>(true),
                m_ControllerData = GetComponentLookup<Controller>(true),
                m_MovingData = GetComponentLookup<Moving>(true),
                m_LaneOverlaps = GetBufferLookup<LaneOverlap>(true),
                m_LaneObjects = GetBufferLookup<LaneObject>(true),
                m_SubLanes = GetBufferLookup<Game.Net.SubLane>(true),
                m_ConnectedEdges = GetBufferLookup<ConnectedEdge>(true),
                m_NavigationLanes = GetBufferLookup<CarNavigationLane>(true),
                m_Waits = m_Waits,
                m_Frame = frame,
            };
            JobHandle h = job.Schedule(m_HumanQuery, Dependency);
            if (frame % kSweepEvery == 0) h = new SweepJob { m_Waits = m_Waits, m_Frame = frame }.Schedule(h);
            Dependency = h;
        }

        /// <summary>Main thread, every update: drain the ring (line caps) and the per-interval summary.</summary>
        private void DrainLog(uint frame, bool on)
        {
            if (!UnmarkedTrace.Ready) return;
            var reader = new UnmarkedTrace.Reader();
            var lines = m_Drain.Drain(ref reader, frame, on);
            for (int i = 0; i < lines.Count; i++) Mod.log.Info(lines[i]);
        }

        [BurstCompile]
        private struct HoldJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle m_EntityType;
            public ComponentTypeHandle<HumanCurrentLane> m_CurrentLaneType;
            public ComponentTypeHandle<HumanNavigation> m_NavigationType;
            public ComponentTypeHandle<Blocker> m_BlockerType;
            [ReadOnly] public ComponentTypeHandle<Transform> m_TransformType;
            [ReadOnly] public ComponentTypeHandle<PrefabRef> m_PrefabRefType;
            [ReadOnly] public ComponentLookup<Game.Net.PedestrianLane> m_PedestrianLaneData;
            [ReadOnly] public ComponentLookup<LaneSignal> m_LaneSignalData;
            [ReadOnly] public ComponentLookup<Curve> m_CurveData;
            [ReadOnly] public ComponentLookup<Lane> m_LaneData;
            [ReadOnly] public ComponentLookup<Owner> m_OwnerData;
            [ReadOnly] public ComponentLookup<NodeLane> m_NodeLaneData;
            [ReadOnly] public ComponentLookup<PrefabRef> m_PrefabRefData;
            [ReadOnly] public ComponentLookup<NetLaneData> m_NetLaneData;
            [ReadOnly] public ComponentLookup<HumanData> m_HumanData;
            [ReadOnly] public ComponentLookup<ObjectGeometryData> m_GeometryData;
            [ReadOnly] public ComponentLookup<Car> m_CarData;
            [ReadOnly] public ComponentLookup<CarData> m_PrefabCarData;
            [ReadOnly] public ComponentLookup<Controller> m_ControllerData;
            [ReadOnly] public ComponentLookup<Moving> m_MovingData;
            [ReadOnly] public BufferLookup<LaneOverlap> m_LaneOverlaps;
            [ReadOnly] public BufferLookup<LaneObject> m_LaneObjects;
            [ReadOnly] public BufferLookup<Game.Net.SubLane> m_SubLanes;
            [ReadOnly] public BufferLookup<ConnectedEdge> m_ConnectedEdges;
            [ReadOnly] public BufferLookup<CarNavigationLane> m_NavigationLanes;
            public NativeParallelHashMap<Entity, Wait> m_Waits;
            public uint m_Frame;

            private int m_LanesRead;
            private int m_CarsRead;

            /// <summary>The blocking car found by the last scan and its numbers (for the sample record).</summary>
            private Entity m_HitCar;
            private float m_HitGap, m_HitSpeed, m_HitClear;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(m_EntityType);
                NativeArray<HumanCurrentLane> lanes = chunk.GetNativeArray(ref m_CurrentLaneType);
                NativeArray<HumanNavigation> navs = chunk.GetNativeArray(ref m_NavigationType);
                NativeArray<Blocker> blockers = chunk.GetNativeArray(ref m_BlockerType);
                NativeArray<Transform> transforms = chunk.GetNativeArray(ref m_TransformType);
                NativeArray<PrefabRef> prefabs = chunk.GetNativeArray(ref m_PrefabRefType);
                for (int i = 0; i < entities.Length; i++)
                {
                    HumanCurrentLane lane = lanes[i];
                    if (!Judge(lane, transforms[i], prefabs[i], out float limit, out float personSpeed))
                    {
                        EndWait(entities[i]);
                        continue;
                    }
                    if (!Busy(lane.m_Lane, transforms[i], lane, personSpeed))
                    {
                        EndWait(entities[i]);
                        continue;
                    }
                    Hold(entities[i], ref lane, navs, blockers, i, transforms[i], prefabs[i], limit);
                    lanes[i] = lane;
                }
            }

            /// <summary>
            /// The person is on an unmarked crossing, walking towards its carriageway, still before the kerb edge, and the
            /// game's target already lies past that edge. limit = the kerb edge (curve delta), personSpeed = run speed.
            /// </summary>
            private bool Judge(HumanCurrentLane lane, Transform transform, PrefabRef prefab, out float limit, out float personSpeed)
            {
                limit = 0f;
                personSpeed = 0f;
                if (lane.m_Lane == Entity.Null) return false;
                if ((lane.m_Flags & (CreatureLaneFlags.EndReached | CreatureLaneFlags.WaitSignal | CreatureLaneFlags.Connection
                    | CreatureLaneFlags.TransformTarget | CreatureLaneFlags.Area)) != 0) return false;
                if (!m_PedestrianLaneData.TryGetComponent(lane.m_Lane, out Game.Net.PedestrianLane ped)) return false;
                if (!UnmarkedCrossing.IsUnmarkedLane((int)ped.m_Flags, m_LaneSignalData.HasComponent(lane.m_Lane))) return false;
                float x = lane.m_CurvePosition.x, end = lane.m_CurvePosition.y;
                if (x == end) return false;
                bool forward = end > x;
                if (!Carriageway(lane.m_Lane, out float lo, out float hi)) return false;
                if (!UnmarkedCrossing.TargetPastKerb(x, lo, hi, forward)) return false;
                if (!m_CurveData.TryGetComponent(lane.m_Lane, out Curve curve)) return false;
                MathUtils.Distance(curve.m_Bezier.xz, transform.m_Position.xz, out float tp);
                if (!UnmarkedCrossing.BeforeKerb(tp * curve.m_Length, lo * curve.m_Length, hi * curve.m_Length, forward)) return false;
                limit = forward ? lo : hi;
                m_HumanData.TryGetComponent(prefab.m_Prefab, out HumanData hd);
                personSpeed = UnmarkedCrossing.PersonSpeed(hd.m_RunSpeed, hd.m_WalkSpeed);
                return true;
            }

            /// <summary>First road overlap start and last road overlap end of the crossing (curve deltas).</summary>
            private bool Carriageway(Entity pedLane, out float lo, out float hi)
            {
                lo = 2f;
                hi = -1f;
                if (!m_LaneOverlaps.TryGetBuffer(pedLane, out DynamicBuffer<LaneOverlap> ov)) return false;
                for (int i = 0; i < ov.Length; i++)
                {
                    LaneOverlap o = ov[i];
                    if (!PeoplePass.CountsAsCrossing((o.m_Flags & (OverlapFlags.Road | OverlapFlags.Track)) != 0,
                        (o.m_Flags & (OverlapFlags.MergeStart | OverlapFlags.MergeEnd | OverlapFlags.MergeMiddleStart | OverlapFlags.MergeMiddleEnd)) != 0)) continue;
                    lo = math.min(lo, math.min((float)o.m_ThisStart, o.m_ThisEnd) * (1f / 255f));
                    hi = math.max(hi, math.max((float)o.m_ThisStart, o.m_ThisEnd) * (1f / 255f));
                }
                return hi > lo;
            }

            /// <summary>Any approaching vehicle that does not leave the person a safe gap (Core UnmarkedCrossing.CarBlocks).</summary>
            private bool Busy(Entity pedLane, Transform transform, HumanCurrentLane lane, float personSpeed)
            {
                m_LanesRead = 0;
                m_CarsRead = 0;
                m_HitCar = Entity.Null;
                Curve curve = m_CurveData[pedLane];
                MathUtils.Distance(curve.m_Bezier.xz, transform.m_Position.xz, out float tp);
                float offLane = math.distance(transform.m_Position.xz, MathUtils.Position(curve.m_Bezier, tp).xz);
                bool forward = lane.m_CurvePosition.y > lane.m_CurvePosition.x;
                DynamicBuffer<LaneOverlap> ov = m_LaneOverlaps[pedLane];
                for (int i = 0; i < ov.Length; i++)
                {
                    LaneOverlap o = ov[i];
                    if (!PeoplePass.CountsAsCrossing((o.m_Flags & (OverlapFlags.Road | OverlapFlags.Track)) != 0,
                        (o.m_Flags & (OverlapFlags.MergeStart | OverlapFlags.MergeEnd | OverlapFlags.MergeMiddleStart | OverlapFlags.MergeMiddleEnd)) != 0)) continue;
                    float s = math.min((float)o.m_ThisStart, o.m_ThisEnd) * (1f / 255f), e = math.max((float)o.m_ThisStart, o.m_ThisEnd) * (1f / 255f);
                    float far = forward ? (e - tp) * curve.m_Length : (tp - s) * curve.m_Length;
                    if (far <= 0f) continue;
                    float clear = UnmarkedCrossing.ClearTime(far + offLane, personSpeed);
                    float os = math.min((float)o.m_OtherStart, o.m_OtherEnd) * (1f / 255f);
                    if (LaneBusy(o.m_Other, os, clear)) return true;
                    if (m_CarsRead > UnmarkedCrossing.MaxCars)
                    {
                        UnmarkedTrace.Add(UnmarkedTraceLayout.PersonCapHits, 1);
                        return true;
                    }
                }
                return false;
            }

            /// <summary>The crossed lane itself (cars before the crossing) and the lanes feeding it.</summary>
            private bool LaneBusy(Entity carLane, float conflictStart, float clear)
            {
                if (!m_CurveData.TryGetComponent(carLane, out Curve laneCurve)) return false;
                if (CarsOn(carLane, laneCurve, conflictStart * laneCurve.m_Length, clear)) return true;
                if (!m_LaneData.TryGetComponent(carLane, out Lane l) || !m_OwnerData.TryGetComponent(carLane, out Owner owner)) return false;
                PathNode start = l.m_StartNode;
                Entity source = Entity.Null;
                if (m_ConnectedEdges.TryGetBuffer(owner.m_Owner, out DynamicBuffer<ConnectedEdge> edges))
                {
                    for (int i = 0; i < edges.Length; i++)
                    {
                        if (new PathNode(edges[i].m_Edge, 0).OwnerEquals(start)) { source = edges[i].m_Edge; break; }
                    }
                }
                if (source == Entity.Null && new PathNode(owner.m_Owner, 0).OwnerEquals(start)) source = owner.m_Owner;
                if (source == Entity.Null || !m_SubLanes.TryGetBuffer(source, out DynamicBuffer<Game.Net.SubLane> subLanes)) return false;
                float into = conflictStart * laneCurve.m_Length;
                m_LaneSignalData.TryGetComponent(carLane, out LaneSignal signal);
                bool signalled = m_LaneSignalData.HasComponent(carLane);
                for (int i = 0; i < subLanes.Length; i++)
                {
                    Entity feed = subLanes[i].m_SubLane;
                    if (feed == carLane || !m_LaneData.TryGetComponent(feed, out Lane fl) || !fl.m_EndNode.Equals(start)) continue;
                    if (++m_LanesRead > UnmarkedCrossing.MaxLanes) return false;
                    if (!m_CurveData.TryGetComponent(feed, out Curve feedCurve)) continue;
                    if (CarsOnFeed(feed, feedCurve, into, clear, carLane, owner.m_Owner, signalled, signal)) return true;
                }
                return false;
            }

            /// <summary>Cars on the crossed lane whose front is still before the crossing (driving towards it).</summary>
            private bool CarsOn(Entity carLane, Curve laneCurve, float conflictAt, float clear)
            {
                if (!m_LaneObjects.TryGetBuffer(carLane, out DynamicBuffer<LaneObject> objects)) return false;
                for (int j = 0; j < objects.Length; j++)
                {
                    LaneObject o = objects[j];
                    if (!Candidate(o, out CarData data, out float front)) continue;
                    if (o.m_CurvePosition.y < o.m_CurvePosition.x) continue;
                    if (++m_CarsRead > UnmarkedCrossing.MaxCars) return false;
                    float speed = Speed(o.m_LaneObject, laneCurve, o.m_CurvePosition.x);
                    float gap = conflictAt - o.m_CurvePosition.x * laneCurve.m_Length - front;
                    if (Blocks(o.m_LaneObject, data, gap, speed, clear)) return true;
                }
                return false;
            }

            private bool CarsOnFeed(Entity feed, Curve feedCurve, float into, float clear, Entity crossed, Entity junction, bool signalled, LaneSignal signal)
            {
                if (!m_LaneObjects.TryGetBuffer(feed, out DynamicBuffer<LaneObject> objects)) return false;
                for (int j = 0; j < objects.Length; j++)
                {
                    LaneObject o = objects[j];
                    if (!Candidate(o, out CarData data, out float front)) continue;
                    if (o.m_CurvePosition.y < o.m_CurvePosition.x) continue;
                    if (++m_CarsRead > UnmarkedCrossing.MaxCars) return false;
                    float speed = Speed(o.m_LaneObject, feedCurve, o.m_CurvePosition.x);
                    if (!(speed >= UnmarkedCrossing.StandingSpeed)) continue;
                    float remaining = (1f - o.m_CurvePosition.x) * feedCurve.m_Length;
                    if (signalled && m_CarData.TryGetComponent(o.m_LaneObject, out Car car))
                    {
                        float braking = VehicleUtils.GetBrakingDistance(data, speed, 0f);
                        if (FeedingSignal.Skips(true, (int)signal.m_Signal, (int)signal.m_Flags, VehicleUtils.GetPriority(car), braking, remaining - front)) continue;
                    }
                    if (!UnmarkedCrossing.RouteCounts(RouteOf(o.m_LaneObject, crossed, junction))) continue;
                    float gap = remaining + into - front;
                    if (Blocks(o.m_LaneObject, data, gap, speed, clear)) return true;
                }
                return false;
            }

            /// <summary>A car (its controller; trailers skipped) with its prefab data and front offset.</summary>
            private bool Candidate(LaneObject o, out CarData data, out float front)
            {
                data = default;
                front = 0f;
                Entity e = o.m_LaneObject;
                if (m_ControllerData.TryGetComponent(e, out Controller c) && c.m_Controller != Entity.Null && c.m_Controller != e) return false;
                if (!m_CarData.HasComponent(e)) return false;
                if (!m_PrefabRefData.TryGetComponent(e, out PrefabRef pr) || !m_PrefabCarData.TryGetComponent(pr.m_Prefab, out data)) return false;
                if (m_GeometryData.TryGetComponent(pr.m_Prefab, out ObjectGeometryData g)) front = g.m_Bounds.max.z;
                return true;
            }

            private float Speed(Entity car, Curve curve, float x)
            {
                if (!m_MovingData.TryGetComponent(car, out Moving m)) return 0f;
                return math.dot(m.m_Velocity, math.normalizesafe(MathUtils.Tangent(curve.m_Bezier, x)));
            }

            private bool Blocks(Entity car, CarData data, float gap, float speed, float clear)
            {
                float braking = VehicleUtils.GetBrakingDistance(data, math.max(0f, speed), 4f / 15f);
                if (!UnmarkedCrossing.CarBlocks(gap, speed, braking, clear)) return false;
                m_HitCar = car;
                m_HitGap = gap;
                m_HitSpeed = speed;
                m_HitClear = clear;
                return true;
            }

            /// <summary>Whether a car on a feeding lane takes the crossed lane (its navigation lanes; unknown counts).</summary>
            private UnmarkedCrossing.Route RouteOf(Entity car, Entity crossed, Entity junction)
            {
                if (!m_NavigationLanes.TryGetBuffer(car, out DynamicBuffer<CarNavigationLane> nav)) return UnmarkedCrossing.Route.Unknown;
                int n = math.min(nav.Length, 8);
                for (int k = 0; k < n; k++)
                {
                    Entity l = nav[k].m_Lane;
                    if (l == crossed) return UnmarkedCrossing.Route.Takes;
                    if (m_OwnerData.TryGetComponent(l, out Owner ow) && ow.m_Owner == junction) return UnmarkedCrossing.Route.Other;
                }
                return UnmarkedCrossing.Route.Unknown;
            }

            /// <summary>Pull the target back to the kerb edge (the game's clamp point), brake, blocker = the car; diagnostics.</summary>
            private void Hold(Entity person, ref HumanCurrentLane lane, NativeArray<HumanNavigation> navs, NativeArray<Blocker> blockers, int i,
                Transform transform, PrefabRef prefab, float limit)
            {
                Curve curve = m_CurveData[lane.m_Lane];
                m_GeometryData.TryGetComponent(prefab.m_Prefab, out ObjectGeometryData geometry);
                m_HumanData.TryGetComponent(prefab.m_Prefab, out HumanData hd);
                float laneOffset = 0f;
                if (m_PrefabRefData.TryGetComponent(lane.m_Lane, out PrefabRef lanePrefab) && m_NetLaneData.TryGetComponent(lanePrefab.m_Prefab, out NetLaneData netLane))
                {
                    m_NodeLaneData.TryGetComponent(lane.m_Lane, out NodeLane nodeLane);
                    laneOffset = CreatureUtils.GetLaneOffset(geometry, netLane, nodeLane, limit, lane.m_LanePosition);
                }
                bool forward = lane.m_CurvePosition.y > lane.m_CurvePosition.x;
                float3 kerb = CreatureUtils.GetLanePosition(curve.m_Bezier, limit, laneOffset);
                lane.m_CurvePosition.x = limit;
                lane.m_QueueEntity = lane.m_Lane;
                lane.m_QueueArea = CreatureUtils.GetQueueArea(geometry, kerb, forward ? curve.m_Bezier.d : curve.m_Bezier.a);
                HumanNavigation nav = navs[i];
                nav.m_TargetPosition = kerb;
                nav.m_TargetDirection = default;
                float radius = math.max(geometry.m_Bounds.max.z, (geometry.m_Bounds.max.x - geometry.m_Bounds.min.x) * 0.5f);
                float dist = math.max(0f, math.distance(transform.m_Position, kerb) - radius);
                float brake = hd.m_Acceleration > 0f ? CreatureUtils.GetMaxBrakingSpeed(hd, dist, 4f / 15f) : 0f;
                nav.m_MaxSpeed = math.min(nav.m_MaxSpeed, brake);
                if (nav.m_MaxSpeed < 0.1f) nav.m_MaxSpeed = 0f;
                navs[i] = nav;
                Blocker b = blockers[i];
                b.m_Blocker = m_HitCar;
                b.m_Type = BlockerType.Crossing;
                blockers[i] = b;

                UnmarkedTrace.Add(UnmarkedTraceLayout.PersonUpdatesHeld, 1);
                if (!m_Waits.TryGetValue(person, out Wait w) || unchecked(m_Frame - w.Last) > kStaleFrames)
                {
                    w = new Wait { Start = m_Frame, Last = m_Frame };
                    UnmarkedTrace.Add(UnmarkedTraceLayout.PeopleHeld, 1);
                    UnmarkedTrace.Record(UnmarkedTraceLayout.KindHoldStart, m_Frame, person.Index, m_HitCar.Index,
                        (long)(m_HitGap * 100f), (long)(m_HitSpeed * 100f), (long)(m_HitClear * 1000f));
                }
                w.Last = m_Frame;
                uint waited = unchecked(m_Frame - w.Start);
                if (w.LongReported == 0 && waited >= UnmarkedTraceLayout.LongWaitSeconds * 60f)
                {
                    w.LongReported = 1;
                    UnmarkedTrace.Add(UnmarkedTraceLayout.LongWaits, 1);
                    UnmarkedTrace.Record(UnmarkedTraceLayout.KindLongWait, m_Frame, person.Index, m_HitCar.Index, waited, lane.m_Lane.Index,
                        UnmarkedTraceLayout.PackXZ(transform.m_Position.x, transform.m_Position.z));
                }
                m_Waits[person] = w;
            }

            private void EndWait(Entity person)
            {
                if (!m_Waits.IsEmpty) m_Waits.Remove(person);
            }
        }

        [BurstCompile]
        private struct SweepJob : IJob
        {
            public NativeParallelHashMap<Entity, Wait> m_Waits;
            public uint m_Frame;

            public void Execute()
            {
                NativeArray<Entity> keys = m_Waits.GetKeyArray(Allocator.Temp);
                for (int i = 0; i < keys.Length; i++)
                {
                    if (m_Waits.TryGetValue(keys[i], out Wait w) && unchecked(m_Frame - w.Last) > kSweepEvery) m_Waits.Remove(keys[i]);
                }
                keys.Dispose();
            }
        }

        [Preserve]
        public UnmarkedCrossingSystem() { }
    }
}
