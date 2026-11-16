using Game;
using Game.Common;
using Game.Creatures;
using Game.Net;
using Game.Objects;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;
using JunctionRules.Core;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace JunctionRules.Systems
{
    /// <summary>
    /// 0.3.6 (JR-T-008): people and cyclists pass a car that stands on their crossing after the setting's wait
    /// (Core/PeoplePass.cs has the stock analysis with cites). Runs right after the game's HumanNavigationSystem on the
    /// same UpdateFrame slice (frameIndex % 16), so it sees each person just after the game decided their target, and
    /// each bicycle of that slice (the car navigation of the slice runs in the same frame; the IgnoreBlocker flag set here
    /// is read by the bicycle's next navigation update, game's or copy's). A hold is timed per person / bicycle and per
    /// car: it starts when the standing car first shows as the Crossing blocker and is dropped when the blocker changes
    /// or the car moves. No game code is copied; nothing changes for anyone not held by a standing vehicle.
    /// Works with either Car driving mode. Switch "People pass a car that blocks them" off (or wait 0) = the system does nothing and counts nothing (the game's behaviour).
    /// </summary>
    public class PeoplePassSystem : GameSystemBase
    {
        public const int CountPeople = 0;
        public const int CountCyclists = 1;
        public const int CountSlots = 2;

        /// <summary>Holds older than this are dropped by the sweep (entity gone or no longer in the slice).</summary>
        private const uint kStaleFrames = 600u * 60u;
        private const uint kSweepEvery = 4096u;

        private struct Hold
        {
            public Entity Car;
            public uint Start;
        }

        private static NativeArray<long> s_Counts;

        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_HumanQuery;
        private EntityQuery m_BicycleQuery;
        private NativeParallelHashMap<Entity, Hold> m_Holds;

        /// <summary>Running totals (main thread, never waits): people passed, cyclists passed.</summary>
        public static unsafe bool Read(long[] totals)
        {
            if (!s_Counts.IsCreated || totals == null || totals.Length < CountSlots) return false;
            long* p = (long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(s_Counts);
            for (int i = 0; i < CountSlots; i++) totals[i] = System.Threading.Interlocked.Read(ref p[i]);
            return true;
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_HumanQuery = GetEntityQuery(ComponentType.ReadOnly<Human>(), ComponentType.ReadOnly<UpdateFrame>(),
                ComponentType.ReadWrite<HumanCurrentLane>(), ComponentType.ReadOnly<Blocker>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            m_BicycleQuery = GetEntityQuery(ComponentType.ReadOnly<Bicycle>(), ComponentType.ReadOnly<Car>(), ComponentType.ReadOnly<UpdateFrame>(),
                ComponentType.ReadWrite<CarCurrentLane>(), ComponentType.ReadOnly<Blocker>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<ParkedCar>());
            m_Holds = new NativeParallelHashMap<Entity, Hold>(256, Allocator.Persistent);
            if (!s_Counts.IsCreated) s_Counts = new NativeArray<long>(CountSlots, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        }

        [Preserve]
        protected override void OnDestroy()
        {
            Dependency.Complete();
            if (m_Holds.IsCreated) m_Holds.Dispose();
            if (s_Counts.IsCreated) s_Counts.Dispose();
            s_Counts = default;
            base.OnDestroy();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            float seconds = Mod.Settings != null ? Mod.Settings.PeoplePassEffectiveSeconds() : PeoplePass.DefaultSeconds;
            if (seconds <= 0f)
            {
                if (!m_Holds.IsEmpty)
                {
                    Dependency.Complete();
                    m_Holds.Clear();
                }
                return;
            }
            uint frame = m_SimulationSystem.frameIndex;
            var slice = new UpdateFrame(frame % 16);
            m_HumanQuery.ResetFilter();
            m_HumanQuery.SetSharedComponentFilter(slice);
            m_BicycleQuery.ResetFilter();
            m_BicycleQuery.SetSharedComponentFilter(slice);

            var humans = new HumanJob
            {
                m_EntityType = GetEntityTypeHandle(),
                m_BlockerType = GetComponentTypeHandle<Blocker>(true),
                m_CurrentLaneType = GetComponentTypeHandle<HumanCurrentLane>(false),
                m_VehicleData = GetComponentLookup<Vehicle>(true),
                m_MovingData = GetComponentLookup<Moving>(true),
                m_LaneOverlaps = GetBufferLookup<LaneOverlap>(true),
                m_LaneObjects = GetBufferLookup<LaneObject>(true),
                m_Holds = m_Holds,
                m_Counts = s_Counts,
                m_Frame = frame,
                m_Seconds = seconds,
            };
            JobHandle h = humans.Schedule(m_HumanQuery, Dependency);
            var bikes = new BicycleJob
            {
                m_EntityType = GetEntityTypeHandle(),
                m_BlockerType = GetComponentTypeHandle<Blocker>(true),
                m_CurrentLaneType = GetComponentTypeHandle<CarCurrentLane>(false),
                m_VehicleData = GetComponentLookup<Vehicle>(true),
                m_BicycleData = GetComponentLookup<Bicycle>(true),
                m_MovingData = GetComponentLookup<Moving>(true),
                m_PedestrianLaneData = GetComponentLookup<PedestrianLane>(true),
                m_Holds = m_Holds,
                m_Counts = s_Counts,
                m_Frame = frame,
                m_Seconds = seconds,
            };
            h = bikes.Schedule(m_BicycleQuery, h);
            if (frame % kSweepEvery == 0)
            {
                h = new SweepJob { m_Holds = m_Holds, m_Frame = frame }.Schedule(h);
            }
            Dependency = h;
        }

        [BurstCompile]
        private struct HumanJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle m_EntityType;
            [ReadOnly] public ComponentTypeHandle<Blocker> m_BlockerType;
            public ComponentTypeHandle<HumanCurrentLane> m_CurrentLaneType;
            [ReadOnly] public ComponentLookup<Vehicle> m_VehicleData;
            [ReadOnly] public ComponentLookup<Moving> m_MovingData;
            [ReadOnly] public BufferLookup<LaneOverlap> m_LaneOverlaps;
            [ReadOnly] public BufferLookup<LaneObject> m_LaneObjects;
            public NativeParallelHashMap<Entity, Hold> m_Holds;
            [NativeDisableContainerSafetyRestriction] public NativeArray<long> m_Counts;
            public uint m_Frame;
            public float m_Seconds;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(m_EntityType);
                NativeArray<Blocker> blockers = chunk.GetNativeArray(ref m_BlockerType);
                NativeArray<HumanCurrentLane> lanes = chunk.GetNativeArray(ref m_CurrentLaneType);
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity e = entities[i];
                    Blocker blocker = blockers[i];
                    Entity car = blocker.m_Blocker;
                    HumanCurrentLane lane = lanes[i];
                    bool held = blocker.m_Type == BlockerType.Crossing && car != Entity.Null && m_VehicleData.HasComponent(car)
                        && (lane.m_Flags & (CreatureLaneFlags.WaitSignal | CreatureLaneFlags.Connection)) == 0
                        && PeoplePass.Standing(m_MovingData.TryGetComponent(car, out Moving mv), math.lengthsq(mv.m_Velocity));
                    if (!held)
                    {
                        m_Holds.Remove(e);
                        continue;
                    }
                    if (!m_Holds.TryGetValue(e, out Hold hold) || hold.Car != car)
                    {
                        m_Holds[e] = new Hold { Car = car, Start = m_Frame };
                        continue;
                    }
                    if (!PeoplePass.Due(hold.Start, m_Frame, m_Seconds)) continue;
                    if (TryPass(ref lane, car))
                    {
                        lanes[i] = lane;
                        m_Counts[CountPeople] = m_Counts[CountPeople] + 1;
                        m_Holds.Remove(e);
                    }
                }
            }

            private bool TryPass(ref HumanCurrentLane lane, Entity car)
            {
                if (!m_LaneOverlaps.TryGetBuffer(lane.m_Lane, out DynamicBuffer<LaneOverlap> overlaps)) return false;
                float x = lane.m_CurvePosition.x;
                float y = lane.m_CurvePosition.y;
                bool passed = false;
                // The car may cover two adjacent road lanes: pass every overlap it holds that starts where the last ended.
                for (int pass = 0; pass < 4; pass++)
                {
                    bool found = false;
                    for (int j = 0; j < overlaps.Length; j++)
                    {
                        LaneOverlap o = overlaps[j];
                        bool roadOrTrack = (o.m_Flags & (OverlapFlags.Road | OverlapFlags.Track)) != 0;
                        bool merge = (o.m_Flags & (OverlapFlags.MergeStart | OverlapFlags.MergeEnd | OverlapFlags.MergeMiddleStart | OverlapFlags.MergeMiddleEnd)) != 0;
                        if (!PeoplePass.CountsAsCrossing(roadOrTrack, merge)) continue;
                        if (!PeoplePass.PastOverlap(x, y, o.m_ThisStart * (1f / 255f), o.m_ThisEnd * (1f / 255f), out float nx)) continue;
                        if (!CarOn(o.m_Other, car, o.m_OtherStart * (1f / 255f), o.m_OtherEnd * (1f / 255f))) continue;
                        x = nx;
                        found = true;
                        break;
                    }
                    if (!found) break;
                    passed = true;
                }
                if (passed) lane.m_CurvePosition.x = x;
                return passed;
            }

            private bool CarOn(Entity otherLane, Entity car, float otherStart, float otherEnd)
            {
                if (!m_LaneObjects.TryGetBuffer(otherLane, out DynamicBuffer<LaneObject> objects)) return false;
                for (int k = 0; k < objects.Length; k++)
                {
                    LaneObject lo = objects[k];
                    if (lo.m_LaneObject == car && PeoplePass.Covers(lo.m_CurvePosition.x, lo.m_CurvePosition.y, otherStart, otherEnd)) return true;
                }
                return false;
            }
        }

        [BurstCompile]
        private struct BicycleJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle m_EntityType;
            [ReadOnly] public ComponentTypeHandle<Blocker> m_BlockerType;
            public ComponentTypeHandle<CarCurrentLane> m_CurrentLaneType;
            [ReadOnly] public ComponentLookup<Vehicle> m_VehicleData;
            [ReadOnly] public ComponentLookup<Bicycle> m_BicycleData;
            [ReadOnly] public ComponentLookup<Moving> m_MovingData;
            [ReadOnly] public ComponentLookup<PedestrianLane> m_PedestrianLaneData;
            public NativeParallelHashMap<Entity, Hold> m_Holds;
            [NativeDisableContainerSafetyRestriction] public NativeArray<long> m_Counts;
            public uint m_Frame;
            public float m_Seconds;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(m_EntityType);
                NativeArray<Blocker> blockers = chunk.GetNativeArray(ref m_BlockerType);
                NativeArray<CarCurrentLane> lanes = chunk.GetNativeArray(ref m_CurrentLaneType);
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity e = entities[i];
                    Blocker blocker = blockers[i];
                    Entity car = blocker.m_Blocker;
                    CarCurrentLane lane = lanes[i];
                    bool isVehicle = car != Entity.Null && m_VehicleData.HasComponent(car);
                    bool standing = isVehicle && PeoplePass.Standing(m_MovingData.TryGetComponent(car, out Moving mv), math.lengthsq(mv.m_Velocity));
                    bool held = PeoplePass.CyclistHeld(m_PedestrianLaneData.HasComponent(lane.m_Lane), isVehicle,
                        isVehicle && m_BicycleData.HasComponent(car), blocker.m_Type == BlockerType.Crossing, standing);
                    if (!held)
                    {
                        m_Holds.Remove(e);
                        continue;
                    }
                    if ((lane.m_LaneFlags & Game.Vehicles.CarLaneFlags.IgnoreBlocker) != 0) continue;
                    if (!m_Holds.TryGetValue(e, out Hold hold) || hold.Car != car)
                    {
                        m_Holds[e] = new Hold { Car = car, Start = m_Frame };
                        continue;
                    }
                    if (!PeoplePass.Due(hold.Start, m_Frame, m_Seconds)) continue;
                    lane.m_LaneFlags |= Game.Vehicles.CarLaneFlags.IgnoreBlocker;
                    lanes[i] = lane;
                    m_Counts[CountCyclists] = m_Counts[CountCyclists] + 1;
                    m_Holds.Remove(e);
                }
            }
        }

        [BurstCompile]
        private struct SweepJob : IJob
        {
            public NativeParallelHashMap<Entity, Hold> m_Holds;
            public uint m_Frame;

            public void Execute()
            {
                NativeArray<Entity> keys = m_Holds.GetKeyArray(Allocator.Temp);
                for (int i = 0; i < keys.Length; i++)
                {
                    if (m_Holds.TryGetValue(keys[i], out Hold hold) && unchecked(m_Frame - hold.Start) > kStaleFrames) m_Holds.Remove(keys[i]);
                }
                keys.Dispose();
            }
        }

        [Preserve]
        public PeoplePassSystem() { }
    }
}
