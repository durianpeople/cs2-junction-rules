using System.Runtime.CompilerServices;
using System.Threading;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Pathfind;
using Game.Prefabs;
using Game.Vehicles;
using JunctionRules.Core;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace JunctionRules.Stage2
{
    /// <summary>
    /// Stage 2, job side: the whole-junction rule as the copy's lane-speed iterator (Vanilla/JRCarLaneSpeedIterator.cs)
    /// calls it from its JR-STAGE2 blocks. One value lives in each iterator (one per car and step, a local of the
    /// navigation job), so all state here is per car; nothing is shared between threads except the debug counters and
    /// the safety-event table (Interlocked, debug only). Burst-compatible: blittable fields, no managed allocations.
    ///
    /// Per junction lane (BeginLane, once per CheckOverlappingLanes call): find the entry spot W (0.2.3: the only spot;
    /// the whole path is one decision), where the car is and the first uncrossed point. Per overlap (InScope): classify
    /// it (Core/StretchRule.cs: Deciding / Safety / Crossing / Stock) and, when Deciding or Safety, the car's own arrival
    /// time at it (0.2.3, Core/ArrivalTime.cs, the game's own motion). Per stock yield demand: leave the game's value
    /// (Stock), stop at W or before the first uncrossed point (StopAtSpot) or drop the give-way (Suppress). 0.2.3 also
    /// projects the other cars to that arrival time: the stock 2 s look-ahead becomes 2 s + arrival time
    /// (AfterProjection) and cars on the lanes feeding a conflicting lane count as virtual reservations
    /// (FeedingLaneYield). With m_Mode == 0 every entry point returns at its first test.
    /// </summary>
    public struct JRWholeJunction
    {
        /// <summary>The stock reservation time step (CarNavigationSystem.ReserveNavigationLanes: timeStep = 4f / 15f).</summary>
        public const float ReservationTimeStep = 4f / 15f;

        public ComponentLookup<NodeLane> m_NodeLaneData;
        // 0.2.3: read-only stock lookups for the feeding lanes of a conflicting junction lane (the job's own read-only
        // m_OwnerData / m_LaneData / m_Lanes, plus the S2-20 ConnectedEdge lookup).
        public ComponentLookup<Owner> m_OwnerData;
        public ComponentLookup<Lane> m_LaneData;
        public BufferLookup<Game.Net.SubLane> m_SubLanes;
        public BufferLookup<ConnectedEdge> m_ConnectedEdges;
        /// <summary>Debug counters (created only when counting; the job's persistent array).</summary>
        public NativeArray<long> m_Counters;
        /// <summary>Stage2Mode bits; 0 = off.</summary>
        public int m_Mode;

        /// <summary>Set by IterateFirstLane (S2-02): the car's current lane(s); feeding lanes equal to them are its own approach.</summary>
        public Entity m_CurrentLane;
        public Entity m_CurrentLane2;

        // ---- state of the junction lane being checked (BeginLane) ----
        public bool m_LaneActive;
        /// <summary>The car's front is past the entry spot W (0.2.0 "committed").</summary>
        public bool m_Committed;
        public bool m_Inverse;
        /// <summary>Entry spot W in the game's lane offsets (as 0.2.0).</summary>
        public float m_Spot;
        /// <summary>Spot, stretch and the car's place in it (Core/StretchRule.cs; one stretch since 0.2.3).</summary>
        public StretchState m_Stretch;
        /// <summary>Set by IterateFirstLane, cleared by IterateNextLane: the lane being checked is the car's current lane.</summary>
        public bool m_FirstLane;
        /// <summary>The car's front is past the entry spot of its current lane (it is inside the junction's paths).</summary>
        public bool m_Inside;
        private Entity m_LaneEntity;

        // ---- 0.2.3: the car's own motion for the arrival time (BeginLane) ----
        private CarData m_Car;
        private float m_Speed;
        private float m_TimeStep;
        /// <summary>The junction lane's stock drive speed (GetMaxDriveSpeed: speed limit and turn cap, lane wear).</summary>
        private float m_DriveSpeed;
        /// <summary>Metres from the car's front to the start of the junction lane (0 when on it).</summary>
        private float m_DriveFrom;
        private float m_DeltaU;
        private float m_DeltaValue;
        private bool m_OwnStartRead;
        private bool m_HasOwnStart;
        private PathNode m_OwnStart;
        /// <summary>Diagnostics: the car stands at its entry spot (points are sampled for the timing log).</summary>
        private bool m_AtSpot;

        // ---- the overlap being checked (InScope, once per overlap before the game's yield and object checks) ----
        public PointPhase m_OvPhase;
        public float m_OvStopWait;
        public float m_OvStart;
        /// <summary>0.2.3: the car's own arrival time at this overlap (Deciding / Safety), else 0.</summary>
        public float m_OvDelta;

        // ---- per-car results for the debug counters ----
        public bool m_Decision;
        public bool m_HasLater;
        public float m_LaterSpeed;
        public bool m_HasInsideYield;
        public float m_InsideYieldSpeed;
        public bool m_HasInsideCrossing;
        public float m_InsideCrossingSpeed;
        public bool m_HasSafety;
        public float m_SafetySpeed;
        public bool m_ProjectedLaneHit;
        public bool m_FeedingLaneHit;

        public bool RuleOn
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (m_Mode & Stage2Mode.Rule) != 0;
        }

        public bool Counting
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (m_Mode & Stage2Mode.Count) != 0;
        }

        /// <summary>The overlap being checked gets the arrival-time projection (rule on, Deciding or Safety).</summary>
        public bool Projecting
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => RuleOn && (m_OvPhase == PointPhase.Deciding || m_OvPhase == PointPhase.Safety);
        }

        public static JRWholeJunction Create(int mode, ComponentLookup<NodeLane> nodeLaneData, ComponentLookup<Owner> ownerData,
            ComponentLookup<Lane> laneData, BufferLookup<Game.Net.SubLane> subLanes, BufferLookup<ConnectedEdge> connectedEdges,
            NativeArray<long> counters)
        {
            JRWholeJunction j = default;
            j.m_Mode = mode;
            if (mode != 0)
            {
                j.m_NodeLaneData = nodeLaneData;
                j.m_OwnerData = ownerData;
                j.m_LaneData = laneData;
                j.m_SubLanes = subLanes;
                j.m_ConnectedEdges = connectedEdges;
                if ((mode & Stage2Mode.Count) != 0) j.m_Counters = counters;
            }
            return j;
        }

        /// <summary>The lane's overlaps as conflict points in driving coordinates (red-held road lanes are not live).</summary>
        private struct OverlapConflicts : IConflictList
        {
            public DynamicBuffer<LaneOverlap> m_Overlaps;
            public bool m_Inverse;
            /// <summary>Bit i = overlap i is held by red (first 64 overlaps; the rest are looked up).</summary>
            public ulong m_Held;
            public ComponentLookup<LaneSignal> m_Signals;

            public int Count => m_Overlaps.Length;

            public bool Conflict(int i, out float start, out float end)
            {
                LaneOverlap o = m_Overlaps[i];
                bool held = i < 64 ? (m_Held & (1UL << i)) != 0 : HeldByRed(o, ref m_Signals);
                return StretchGeometry.ToConflict((int)o.m_Flags, o.m_ThisStart, o.m_ThisEnd, m_Inverse, held, out start, out end);
            }
        }

        /// <summary>The game's own motion functions on the car's prefab data (Core/ArrivalTime.cs ICarMotion).</summary>
        private struct GameMotion : ICarMotion
        {
            public CarData m_Car;

            public float MaxNextSpeed(float speed, float timeStep) => VehicleUtils.CalculateSpeedRange(m_Car, speed, timeStep).max;

            public float MinNextSpeed(float speed, float timeStep) => VehicleUtils.CalculateSpeedRange(m_Car, speed, timeStep).min;

            public float BrakingSpeed(float distance, float maxResultSpeed, float timeStep) =>
                VehicleUtils.GetMaxBrakingSpeed(m_Car, distance, maxResultSpeed, timeStep);
        }

        private static bool HeldByRed(LaneOverlap o, ref ComponentLookup<LaneSignal> signals)
        {
            int flags = (int)o.m_Flags;
            // Only red holds a lane; amber (SafeStop) is live (owner ruling 14 Nov 2026). Track lanes are always live.
            return (flags & WholePathRule.Road) != 0 && (flags & WholePathRule.Track) == 0
                && signals.TryGetComponent(o.m_Other, out LaneSignal s) && WholePathRule.IsHeldByRed((int)s.m_Signal);
        }

        /// <summary>
        /// Start of CheckOverlappingLanes for one lane: decide whether the rule covers this lane (a node lane with at
        /// least one conflict point), find its entry spot W, where the car is and the first uncrossed point of its path.
        /// origDistance is the game's value after its own "- margin". The car, its speed, the time step and the lane's
        /// speed limit (the value the game passes to CheckOverlappingLanes, speed-limit factor applied) feed the game's
        /// can-stop test and, 0.2.3, the arrival time: drive speed = VehicleUtils.GetMaxDriveSpeed(car, lane) with that
        /// limit, reduced by lane wear (ModifyDriveSpeed) exactly as IterateFirstLane / IterateNextLane do.
        /// </summary>
        public void BeginLane(Entity lane, DynamicBuffer<LaneOverlap> overlaps, ref ComponentLookup<LaneSignal> signals,
            ref ComponentLookup<Game.Net.CarLane> carLanes, ref ComponentLookup<LaneCondition> laneConditions,
            float curveX, float length, float origDistance, float margin, bool inverse, CarData car, float currentSpeed,
            float timeStep, float speedLimit)
        {
            m_LaneActive = false;
            m_Committed = false;
            m_Inside = false;
            if (!m_NodeLaneData.HasComponent(lane)) return;
            OverlapConflicts list = default;
            list.m_Overlaps = overlaps;
            list.m_Inverse = inverse;
            list.m_Signals = signals;
            int n = overlaps.Length < 64 ? overlaps.Length : 64;
            for (int i = 0; i < n; i++)
            {
                if (HeldByRed(overlaps[i], ref signals)) list.m_Held |= 1UL << i;
            }
            float braking = VehicleUtils.GetBrakingDistance(car, currentSpeed, timeStep);
            if (!m_Stretch.Begin(ref list, origDistance, length, StretchGeometry.ToDriving(curveX, inverse), margin, braking,
                    (m_Mode & Stage2Mode.AtLine) != 0))
            {
                return;
            }
            m_Inverse = inverse;
            m_Spot = StretchGeometry.ToDriving(m_Stretch.Spots[0], inverse);
            m_Committed = m_Stretch.PastEntry;
            m_Inside = m_FirstLane && m_Committed;
            m_LaneActive = true;
            m_LaneEntity = lane;
            m_OwnStartRead = false;
            if (RuleOn)
            {
                if (m_Stretch.Next < m_Stretch.Spots.Count) m_Decision = true;
                m_Car = car;
                m_Speed = currentSpeed;
                m_TimeStep = timeStep;
                float drive = speedLimit;
                if (carLanes.TryGetComponent(lane, out Game.Net.CarLane carLane))
                {
                    carLane.m_SpeedLimit = speedLimit;
                    drive = VehicleUtils.GetMaxDriveSpeed(car, carLane);
                    if (laneConditions.TryGetComponent(lane, out LaneCondition condition)) VehicleUtils.ModifyDriveSpeed(ref drive, condition);
                }
                m_DriveSpeed = drive;
                float toStart = m_Stretch.Wait(0f) + margin;
                m_DriveFrom = toStart > 0f ? toStart : 0f;
                m_DeltaU = -1f;
                m_AtSpot = Counting && !m_Stretch.PastEntry && currentSpeed < Stage2Counters.StandingSpeed
                    && m_Stretch.Wait(m_Stretch.Spots[0]) <= margin;
            }
        }

        /// <summary>
        /// The overlap starting at <paramref name="start"/> (game lane offset) is part of the rule (yield-checked, at or
        /// after the entry spot). Also classifies it for the checks that follow and, 0.2.3, computes the car's own
        /// arrival time at it when it is being decided or re-checked.
        /// </summary>
        public bool InScope(float start, bool yieldChecked)
        {
            m_OvDelta = 0f;
            if (m_Mode == 0 || !m_LaneActive || !yieldChecked || !WholePathRule.AtOrAfterSpot(start, m_Spot, m_Inverse))
            {
                m_OvPhase = PointPhase.Stock;
                return false;
            }
            float u = StretchGeometry.ToDriving(start, m_Inverse);
            m_OvStart = u;
            m_OvPhase = m_Stretch.Classify(RuleOn, u, out m_OvStopWait);
            if (Projecting)
            {
                m_OvDelta = Delta(u);
                if (m_AtSpot && m_OvPhase == PointPhase.Deciding) Sample(u, m_OvDelta);
            }
            return true;
        }

        /// <summary>The car's own arrival time at the point u (driving coordinates), cached for repeated starts.</summary>
        private float Delta(float u)
        {
            if (u == m_DeltaU) return m_DeltaValue;
            GameMotion motion = default;
            motion.m_Car = m_Car;
            float distance = m_Stretch.Wait(u) + m_Stretch.Margin;
            m_DeltaValue = ArrivalTime.Compute(ref motion, distance, m_Speed, m_DriveSpeed, m_DriveFrom, m_TimeStep);
            m_DeltaU = u;
            return m_DeltaValue;
        }

        /// <summary>
        /// A stock give-way demand from a blocking reservation (a prediction): stockSpeed is the game's
        /// GetMaxBrakingSpeed(car, distance to this conflict, safe time step). Returns the speed the game then uses.
        /// </summary>
        public float Yield(bool inScope, float stockSpeed, float start, CarData car, float safeTimeStep, Bounds1 speedRange)
        {
            if (!inScope) return stockSpeed;
            return Apply(StretchState.ForPrediction(m_OvPhase), stockSpeed, car, safeTimeStep, speedRange);
        }

        /// <summary>A car asking for space (reservation priority 96): held at the spot before it, dropped while crossing.</summary>
        public float SpaceYield(bool inScope, float stockSpeed, float start, CarData car, float safeTimeStep, Bounds1 speedRange)
        {
            if (!inScope) return stockSpeed;
            return Apply(StretchState.ForSpaceRequest(m_OvPhase), stockSpeed, car, safeTimeStep, speedRange);
        }

        private float Apply(WholePathRule.YieldAction a, float stockSpeed, CarData car, float safeTimeStep, Bounds1 speedRange)
        {
            float result = a == WholePathRule.YieldAction.StopAtSpot ? VehicleUtils.GetMaxBrakingSpeed(car, m_OvStopWait, safeTimeStep)
                : a == WholePathRule.YieldAction.Suppress ? float.MaxValue : stockSpeed;
            if (Counting && a != WholePathRule.YieldAction.Suppress)
            {
                Record(MathUtils.Clamp(result, speedRange), a, false);
            }
            return result;
        }

        /// <summary>Distance for the game's pedestrian check on a crossing of the path.</summary>
        public float PedestrianDistance(bool inScope, float stockDistance)
        {
            if (!inScope) return stockDistance;
            return WholePathRule.Distance(StretchState.ForPedestrian(m_OvPhase), stockDistance, m_OvStopWait);
        }

        /// <summary>
        /// After the game's priority look-ahead: returns the curve position the game goes on with. Crossing: undone
        /// (0.2.0 committed semantics). 0.2.3, Deciding / Safety: the stock y + v * 2 / len becomes y + v * (2 + arrival
        /// time) / len (only where stock applied its own). threshold = the stock skip limit (otherStart + num3, or
        /// otherEnd - num3 when the other car drives its lane inverted, flag2), for the diagnostics only.
        /// </summary>
        public float AfterProjection(bool inScope, float originalY, float projectedY, bool objectPastStart, bool flag2, float threshold)
        {
            if (!inScope || projectedY == originalY) return projectedY;
            if (StretchState.UndoProjection(m_OvPhase, objectPastStart)) return originalY;
            if (!Projecting) return projectedY;
            float extended = ArrivalProjection.ExtendLookAhead(originalY, projectedY, m_OvDelta);
            if (Counting)
            {
                bool stockCounts = flag2 ? projectedY < threshold : projectedY > threshold;
                bool extendedCounts = flag2 ? extended < threshold : extended > threshold;
                if (!stockCounts && extendedCounts) m_ProjectedLaneHit = true;
            }
            return extended;
        }

        /// <summary>
        /// 0.2.3 (owner ruling 14 Nov 2026): cars on the lanes feeding the conflicting junction lane <paramref name="other"/>,
        /// as virtual reservations on it, through the unchanged stock reservation test. Every car on a feeding lane counts
        /// (its route is not read: owner choice, the stricter option). num10 / num6 = the stock test's values for this
        /// overlap. Returns the speed limit this adds (already clamped to the speed range), or float.MaxValue.
        ///
        /// Feeding lanes, from stock data only: a node lane starts at PathNode(source edge, lane index, segment) of the
        /// edge lane that ends there (Game.Net.LaneSystem node-lane creation; edge lanes end at PathNode(edge, ...)), so
        /// the source is the connected edge of the lane's owner node whose index the start node carries
        /// (PathNode.OwnerEquals), or the node itself for a lane chained inside the node; its sub-lanes whose
        /// Lane.m_EndNode equals the junction lane's m_StartNode are the feeding lanes (the same test as
        /// Game.Simulation.TrafficLightSystem.CheckNextLane). One level upstream only, as stock reservations.
        /// </summary>
        public float FeedingLaneYield(Entity other, float otherLength, float num10, int num6, Entity self,
            ref BufferLookup<LaneObject> laneObjects, ref ComponentLookup<Curve> curves, ref ComponentLookup<Moving> moving,
            ref ComponentLookup<Car> cars, ref ComponentLookup<Controller> controllers, ref ComponentLookup<PrefabRef> prefabRefs,
            ref ComponentLookup<CarData> prefabCars, CarData car, float safeTimeStep, Bounds1 speedRange, float currentMax)
        {
            if (!Projecting) return float.MaxValue;
            // Nothing to add when the car is already held at least as hard as this hold would.
            float hold = MathUtils.Clamp(VehicleUtils.GetMaxBrakingSpeed(car, m_OvStopWait, safeTimeStep), speedRange);
            if (!(hold < currentMax)) return float.MaxValue;
            if (!m_LaneData.TryGetComponent(other, out Lane otherLane) || !m_OwnerData.TryGetComponent(other, out Owner owner)) return float.MaxValue;
            PathNode start = otherLane.m_StartNode;
            if (!m_OwnStartRead)
            {
                m_OwnStartRead = true;
                m_HasOwnStart = m_LaneData.TryGetComponent(m_LaneEntity, out Lane ownLane);
                m_OwnStart = ownLane.m_StartNode;
            }
            // Fed from the car's own approach (a sibling lane): cars there are behind it (stock queueing).
            if (m_HasOwnStart && start.Equals(m_OwnStart)) return float.MaxValue;
            Entity source = Entity.Null;
            if (m_ConnectedEdges.TryGetBuffer(owner.m_Owner, out DynamicBuffer<ConnectedEdge> edges))
            {
                for (int i = 0; i < edges.Length; i++)
                {
                    Entity e = edges[i].m_Edge;
                    if (new PathNode(e, 0).OwnerEquals(start)) { source = e; break; }
                }
            }
            if (source == Entity.Null && new PathNode(owner.m_Owner, 0).OwnerEquals(start)) source = owner.m_Owner;
            if (source == Entity.Null || !m_SubLanes.TryGetBuffer(source, out DynamicBuffer<Game.Net.SubLane> subLanes)) return float.MaxValue;
            for (int i = 0; i < subLanes.Length; i++)
            {
                Entity feed = subLanes[i].m_SubLane;
                if (feed == other || feed == m_CurrentLane || feed == m_CurrentLane2) continue;
                if (!m_LaneData.TryGetComponent(feed, out Lane feedLane) || !feedLane.m_EndNode.Equals(start)) continue;
                if (!laneObjects.TryGetBuffer(feed, out DynamicBuffer<LaneObject> objects) || objects.Length == 0 || !curves.TryGetComponent(feed, out Curve feedCurve)) continue;
                for (int j = 0; j < objects.Length; j++)
                {
                    LaneObject o = objects[j];
                    Entity e = o.m_LaneObject;
                    if (e == self) continue;
                    // A trailer (or anything driven by another controller, incl. the car's own trailers): its controller is counted.
                    if (controllers.TryGetComponent(e, out Controller c) && c.m_Controller != Entity.Null && c.m_Controller != e) continue;
                    if (!cars.TryGetComponent(e, out Car otherCar)) continue;
                    float x = o.m_CurvePosition.x;
                    if (o.m_CurvePosition.y < x) continue;   // driving this lane inverted (two-way lane): not towards its end
                    if (!moving.TryGetComponent(e, out Moving m)) continue;
                    float speed = math.dot(m.m_Velocity, math.normalizesafe(MathUtils.Tangent(feedCurve.m_Bezier, x)));
                    if (!(speed > 0f)) continue;
                    if (!prefabRefs.TryGetComponent(e, out PrefabRef pr) || !prefabCars.TryGetComponent(pr.m_Prefab, out CarData otherData)) continue;
                    float bd = VehicleUtils.GetBrakingDistance(otherData, speed, ReservationTimeStep);
                    float offset = ArrivalProjection.VirtualOffset((1f - x) * feedCurve.m_Length, bd, speed, m_OvDelta, otherLength);
                    if (offset < 0f || !ArrivalProjection.ReservationBlocks(offset, num10, VehicleUtils.GetPriority(otherCar), num6)) continue;
                    if (Counting) m_FeedingLaneHit = true;
                    return MathUtils.Clamp(Apply(StretchState.ForPrediction(m_OvPhase), float.MaxValue, car, safeTimeStep, speedRange), speedRange);
                }
            }
            return float.MaxValue;
        }

        /// <summary>Distance for the game's vehicle braking (UpdateMaxSpeed) on a conflicting lane.</summary>
        public float VehicleDistance(bool inScope, float stockDistance, bool projected, bool merge)
        {
            if (!inScope) return stockDistance;
            return WholePathRule.Distance(StretchState.ForVehicle(m_OvPhase, projected, merge), stockDistance, m_OvStopWait);
        }

        /// <summary>Counters only: after the game's vehicle braking, note what limited the car.</summary>
        public void AfterVehicle(bool inScope, float maxSpeedBefore, float maxSpeedAfter, bool projected, bool merge, float start)
        {
            if (!inScope || !Counting || !(maxSpeedAfter < maxSpeedBefore)) return;
            if (merge && !projected) return;
            Record(maxSpeedAfter, StretchState.ForVehicle(m_OvPhase, projected, merge), !projected);
        }

        private void Record(float speed, WholePathRule.YieldAction a, bool physical)
        {
            if (a == WholePathRule.YieldAction.StopAtSpot)
            {
                if (m_OvPhase == PointPhase.Safety) Min(ref m_HasSafety, ref m_SafetySpeed, speed);
                else if (m_Stretch.Spots.Count > 0 && m_OvStart > m_Stretch.Spots[0] + WholePathRule.SpotEpsilon) Min(ref m_HasLater, ref m_LaterSpeed, speed);
            }
            else if (a == WholePathRule.YieldAction.Stock && m_Inside)
            {
                if (physical) Min(ref m_HasInsideCrossing, ref m_InsideCrossingSpeed, speed);
                else Min(ref m_HasInsideYield, ref m_InsideYieldSpeed, speed);
            }
        }

        private static void Min(ref bool has, ref float value, float speed)
        {
            value = has ? (speed < value ? speed : value) : speed;
            has = true;
        }

        /// <summary>Diagnostics (b1): one conflict point seen from the car standing at W: its distance from W and the projected own arrival time.</summary>
        private void Sample(float u, float delta)
        {
            if (!m_Counters.IsCreated) return;
            float d = m_Stretch.Length * (u - m_Stretch.Spots[0]);
            if (d < 0f) d = 0f;
            Add(m_Counters, Stage2Counters.PointSamples, 1);
            Add(m_Counters, Stage2Counters.PointDistanceSumCm, (long)(d * 100f));
            Add(m_Counters, Stage2Counters.ArrivalSumMs, (long)(delta * 1000f));
            Add(m_Counters, Stage2Counters.DistanceHistogram + PointBuckets.Distance(d), 1);
            Add(m_Counters, Stage2Counters.ArrivalHistogram + PointBuckets.Arrival(delta), 1);
        }

        /// <summary>The safety-event table (a persistent NativeArray&lt;long&gt;) as atomic slots.</summary>
        private unsafe struct TableSlots : IAtomicSlots
        {
            public long* m_P;
            public int m_N;

            public int Length => m_N;

            public long Read(int i) => Interlocked.Read(ref m_P[i]);

            public bool CompareExchange(int i, long value, long comparand) => Interlocked.CompareExchange(ref m_P[i], value, comparand) == comparand;
        }

        /// <summary>
        /// Called once per car and step by the navigation job (JR-STAGE2 block S2-22) when counting: adds this car's
        /// events to the shared counters. finalMaxSpeed is the iterator's result, currentSpeed the car's speed;
        /// eventTable / entity / frame: 0.2.3 safety-stop events (a car's transition into a safety stop counted once).
        /// </summary>
        public unsafe void AddCounts(NativeArray<long> counters, NativeArray<long> eventTable, Entity entity, uint frame, float finalMaxSpeed, float currentSpeed)
        {
            if (!Counting || !counters.IsCreated) return;
            if (m_Decision) Add(counters, Stage2Counters.Decisions, 1);
            if (Stage2Counters.IsStandingOn(m_HasLater, m_LaterSpeed, finalMaxSpeed, currentSpeed)) Add(counters, Stage2Counters.WaitedForLater, 1);
            if (Stage2Counters.IsStandingOn(m_HasInsideYield, m_InsideYieldSpeed, finalMaxSpeed, currentSpeed)) Add(counters, Stage2Counters.StoppedInsideYield, 1);
            if (Stage2Counters.IsStandingOn(m_HasInsideCrossing, m_InsideCrossingSpeed, finalMaxSpeed, currentSpeed)) Add(counters, Stage2Counters.StoppedInsideCrossing, 1);
            if (m_ProjectedLaneHit) Add(counters, Stage2Counters.ProjectedLaneHits, 1);
            if (m_FeedingLaneHit) Add(counters, Stage2Counters.FeedingLaneHits, 1);
            if (Stage2Counters.IsBinding(m_HasSafety, m_SafetySpeed, finalMaxSpeed))
            {
                Add(counters, Stage2Counters.SafetyStops, 1);
                bool isNew = true;
                if (eventTable.IsCreated)
                {
                    TableSlots slots = default;
                    slots.m_P = (long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(eventTable);
                    slots.m_N = eventTable.Length;
                    isNew = SafetyStopEvents.Record(ref slots, entity.Index, frame);
                }
                if (isNew) Add(counters, Stage2Counters.SafetyStopEvents, 1);
            }
        }

        private static unsafe void Add(NativeArray<long> counters, int index, long value)
        {
            long* p = (long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(counters);
            Interlocked.Add(ref p[index], value);
        }
    }
}
