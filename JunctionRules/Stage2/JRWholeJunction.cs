using System.Runtime.CompilerServices;
using System.Threading;
using Colossal.Mathematics;
using Game.Net;
using Game.Prefabs;
using Game.Vehicles;
using JunctionRules.Core;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace JunctionRules.Stage2
{
    /// <summary>
    /// Stage 2, job side: the whole-junction rule as the copy's lane-speed iterator (Vanilla/JRCarLaneSpeedIterator.cs)
    /// calls it from its JR-STAGE2 blocks. One value lives in each iterator (one per car and step), so all state here is
    /// per car; nothing is shared between threads except the counters (Interlocked adds, debug only).
    /// Burst-compatible: blittable fields, no managed allocations.
    ///
    /// Per junction lane (BeginLane, once per CheckOverlappingLanes call): find the waiting spots (entry spot W and the
    /// 0.2.1 mid spots), where the car is among them and the first uncrossed point of the stretch it is crossing.
    /// Per overlap (InScope): classify it (Core/StretchRule.cs: Deciding / Safety / Crossing / Stock). Per stock yield
    /// demand (Yield / SpaceYield / Vehicle / Pedestrian): leave the game's value (Stock), stop at the stretch's spot or
    /// before the first uncrossed point (StopAtSpot) or drop the give-way (Suppress). With m_Mode == 0 every entry point
    /// returns at its first test.
    /// </summary>
    public struct JRWholeJunction
    {
        public ComponentLookup<NodeLane> m_NodeLaneData;
        /// <summary>Stage2Mode bits; 0 = off.</summary>
        public int m_Mode;
        /// <summary>
        /// 0.2.2: the whole vehicle's length, trailers included (Stage2/JRVehicleLength.cs, computed once per car and
        /// update by the navigation job, S2-25). 0 = not given: the controller's prefab bounds are used (0.2.1).
        /// </summary>
        public float m_VehicleLength;

        // ---- state of the junction lane being checked (BeginLane) ----
        public bool m_LaneActive;
        /// <summary>The car's front is past the entry spot W (0.2.0 "committed").</summary>
        public bool m_Committed;
        public bool m_Inverse;
        /// <summary>Entry spot W in the game's lane offsets (as 0.2.0).</summary>
        public float m_Spot;
        /// <summary>Spots, stretches and the car's place in them (0.2.1, Core/StretchRule.cs).</summary>
        public StretchState m_Stretch;
        /// <summary>Set by IterateFirstLane, cleared by IterateNextLane: the lane being checked is the car's current lane.</summary>
        public bool m_FirstLane;
        /// <summary>The car's front is past the entry spot of its current lane (it is inside the junction's paths).</summary>
        public bool m_Inside;

        // ---- the overlap being checked (InScope, once per overlap before the game's yield and object checks) ----
        public PointPhase m_OvPhase;
        public float m_OvStopWait;
        public bool m_OvMid;
        public float m_OvStart;

        // ---- per-car results for the debug counters ----
        public bool m_Decision;
        public bool m_HasLater;
        public float m_LaterSpeed;
        public bool m_HasInsideYield;
        public float m_InsideYieldSpeed;
        public bool m_HasInsideCrossing;
        public float m_InsideCrossingSpeed;
        public bool m_HasMid;
        public float m_MidSpeed;
        public bool m_HasSafety;
        public float m_SafetySpeed;

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

        public static JRWholeJunction Create(int mode, ComponentLookup<NodeLane> nodeLaneData, float vehicleLength)
        {
            JRWholeJunction j = default;
            j.m_Mode = mode;
            if (mode != 0)
            {
                j.m_NodeLaneData = nodeLaneData;
                j.m_VehicleLength = vehicleLength;
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

        private static bool HeldByRed(LaneOverlap o, ref ComponentLookup<LaneSignal> signals)
        {
            int flags = (int)o.m_Flags;
            // Only red holds a lane; amber (SafeStop) is live (owner ruling 14 Nov 2026). Track lanes are always live.
            return (flags & WholePathRule.Road) != 0 && (flags & WholePathRule.Track) == 0
                && signals.TryGetComponent(o.m_Other, out LaneSignal s) && WholePathRule.IsHeldByRed((int)s.m_Signal);
        }

        /// <summary>
        /// Start of CheckOverlappingLanes for one lane: decide whether the rule covers this lane (a node lane with at
        /// least one conflict point), find its waiting spots (entry spot W and mid spots), where the car is among them
        /// and the first uncrossed point of the stretch it is crossing. origDistance is the game's value after its own
        /// "- margin". braking = the game's braking distance at the car's current speed (its can-stop test). The room a
        /// mid spot needs is m_VehicleLength (trailers included, 0.2.2) or, if not given, the geometry's bounds length.
        /// </summary>
        public void BeginLane(Entity lane, DynamicBuffer<LaneOverlap> overlaps, ref ComponentLookup<LaneSignal> signals,
            float curveX, float length, float origDistance, float margin, bool inverse, CarData car, float currentSpeed,
            float timeStep, ObjectGeometryData geometry)
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
            // 0.2.2: the room a mid spot needs is the whole vehicle, trailers included (owner ruling 14 Nov 2026).
            float carLength = m_VehicleLength > 0f ? m_VehicleLength : geometry.m_Bounds.max.z - geometry.m_Bounds.min.z;
            if (!m_Stretch.Begin(ref list, origDistance, length, StretchGeometry.ToDriving(curveX, inverse), margin, braking, carLength,
                    (m_Mode & Stage2Mode.AtLine) != 0))
            {
                return;
            }
            m_Inverse = inverse;
            m_Spot = StretchGeometry.ToDriving(m_Stretch.Spots[0], inverse);
            m_Committed = m_Stretch.PastEntry;
            m_Inside = m_FirstLane && m_Committed;
            m_LaneActive = true;
            if (RuleOn && m_Stretch.Next < m_Stretch.Spots.Count) m_Decision = true;
        }

        /// <summary>
        /// The overlap starting at <paramref name="start"/> (game lane offset) is part of the rule (yield-checked, at or
        /// after the entry spot). Also classifies it for the checks that follow (Core/StretchRule.cs).
        /// </summary>
        public bool InScope(float start, bool yieldChecked)
        {
            if (m_Mode == 0 || !m_LaneActive || !yieldChecked || !WholePathRule.AtOrAfterSpot(start, m_Spot, m_Inverse))
            {
                m_OvPhase = PointPhase.Stock;
                return false;
            }
            float u = StretchGeometry.ToDriving(start, m_Inverse);
            m_OvStart = u;
            m_OvPhase = m_Stretch.Classify(RuleOn, u, out m_OvStopWait, out m_OvMid);
            return true;
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

        /// <summary>After the game's priority look-ahead: returns the curve position the game goes on with.</summary>
        public float AfterProjection(bool inScope, float originalY, float projectedY, bool objectPastStart)
        {
            if (!inScope || projectedY == originalY) return projectedY;
            return StretchState.UndoProjection(m_OvPhase, objectPastStart) ? originalY : projectedY;
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
                else if (m_OvMid) Min(ref m_HasMid, ref m_MidSpeed, speed);
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

        /// <summary>
        /// Called once per car and step by the navigation job (JR-STAGE2 block) when counting: adds this car's events
        /// to the shared counters. finalMaxSpeed is the iterator's result, currentSpeed the car's speed.
        /// </summary>
        public void AddCounts(NativeArray<long> counters, float finalMaxSpeed, float currentSpeed)
        {
            if (!Counting || !counters.IsCreated) return;
            if (m_Decision) Add(counters, Stage2Counters.Decisions);
            if (Stage2Counters.IsStandingOn(m_HasLater, m_LaterSpeed, finalMaxSpeed, currentSpeed)) Add(counters, Stage2Counters.WaitedForLater);
            if (Stage2Counters.IsStandingOn(m_HasInsideYield, m_InsideYieldSpeed, finalMaxSpeed, currentSpeed)) Add(counters, Stage2Counters.StoppedInsideYield);
            if (Stage2Counters.IsStandingOn(m_HasInsideCrossing, m_InsideCrossingSpeed, finalMaxSpeed, currentSpeed)) Add(counters, Stage2Counters.StoppedInsideCrossing);
            if (Stage2Counters.IsStandingOn(m_HasMid, m_MidSpeed, finalMaxSpeed, currentSpeed)) Add(counters, Stage2Counters.WaitedAtMidSpot);
            if (Stage2Counters.IsBinding(m_HasSafety, m_SafetySpeed, finalMaxSpeed)) Add(counters, Stage2Counters.SafetyStops);
        }

        private static unsafe void Add(NativeArray<long> counters, int index)
        {
            long* p = (long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(counters);
            Interlocked.Increment(ref p[index]);
        }
    }
}
