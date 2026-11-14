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
    /// Per junction lane (BeginLane, once per CheckOverlappingLanes call): find the waiting spot and whether the car has
    /// committed. Per stock yield demand (Yield / Vehicle / Pedestrian): either leave the game's value (Stock), move the
    /// stop to the waiting spot (StopAtSpot) or drop the give-way (Suppress). With m_Mode == 0 every entry point returns
    /// at its first test.
    /// </summary>
    public struct JRWholeJunction
    {
        public ComponentLookup<NodeLane> m_NodeLaneData;
        /// <summary>Stage2Mode bits; 0 = off.</summary>
        public int m_Mode;

        // ---- state of the junction lane being checked (BeginLane) ----
        public bool m_LaneActive;
        public bool m_Committed;
        public bool m_Inverse;
        public float m_Spot;
        public float m_WaitDistance;
        /// <summary>Set by IterateFirstLane, cleared by IterateNextLane: the lane being checked is the car's current lane.</summary>
        public bool m_FirstLane;
        /// <summary>The car's front is past the waiting spot of its current lane (it is inside the junction's paths).</summary>
        public bool m_Inside;

        // ---- per-car results for the debug counters ----
        public bool m_Decision;
        public bool m_HasLater;
        public float m_LaterSpeed;
        public bool m_HasInsideYield;
        public float m_InsideYieldSpeed;
        public bool m_HasInsideCrossing;
        public float m_InsideCrossingSpeed;

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

        public static JRWholeJunction Create(int mode, ComponentLookup<NodeLane> nodeLaneData)
        {
            JRWholeJunction j = default;
            j.m_Mode = mode;
            if (mode != 0) j.m_NodeLaneData = nodeLaneData;
            return j;
        }

        /// <summary>
        /// Start of CheckOverlappingLanes for one lane: decide whether the rule covers this lane (a node lane with at
        /// least one conflict that places a waiting spot), where the spot is, and whether the car has committed.
        /// origDistance is the game's value after its own "- margin"; brakingDistance is the game's
        /// GetBrakingDistance(car, current speed, time step), the same value as the game's "can brake" flag.
        /// </summary>
        public void BeginLane(Entity lane, DynamicBuffer<LaneOverlap> overlaps, ref ComponentLookup<LaneSignal> signals,
            float curveX, float length, float origDistance, float origMinOffset, float margin, float brakingDistance, bool inverse)
        {
            m_LaneActive = false;
            m_Committed = false;
            m_Inside = false;
            if (!m_NodeLaneData.HasComponent(lane)) return;
            float spot = WholePathRule.NoSpot(inverse);
            for (int i = 0; i < overlaps.Length; i++)
            {
                LaneOverlap o = overlaps[i];
                int flags = (int)o.m_Flags;
                bool held = false;
                if ((flags & WholePathRule.Road) != 0 && (flags & WholePathRule.Track) == 0 && signals.TryGetComponent(o.m_Other, out LaneSignal s))
                {
                    held = s.m_Signal == LaneSignalType.Stop || s.m_Signal == LaneSignalType.SafeStop;
                }
                spot = WholePathRule.AddConflict(spot, flags, o.m_ThisStart, o.m_ThisEnd, inverse, held);
            }
            if (!WholePathRule.HasSpot(spot, inverse)) return;
            spot = WholePathRule.ApplyVariant(spot, inverse, (m_Mode & Stage2Mode.AtLine) != 0);
            m_Inverse = inverse;
            m_Spot = spot;
            m_WaitDistance = WholePathRule.WaitDistance(origDistance, length, spot, curveX, inverse);
            m_Committed = WholePathRule.IsCommitted(brakingDistance, m_WaitDistance, margin, spot, origMinOffset, inverse);
            m_Inside = m_FirstLane && WholePathRule.FrontPastSpot(m_WaitDistance, margin);
            m_LaneActive = true;
            if (RuleOn && !m_Committed) m_Decision = true;
        }

        /// <summary>The overlap starting at <paramref name="start"/> is part of this lane's whole-path decision.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool InScope(float start, bool yieldChecked)
        {
            return m_Mode != 0 && m_LaneActive && yieldChecked && WholePathRule.AtOrAfterSpot(start, m_Spot, m_Inverse);
        }

        /// <summary>
        /// A stock give-way demand from a blocking reservation or a car asking for space: stockSpeed is the game's
        /// GetMaxBrakingSpeed(car, distance to this conflict, safe time step). Returns the speed the game then uses.
        /// </summary>
        public float Yield(bool inScope, float stockSpeed, float start, CarData car, float safeTimeStep, Bounds1 speedRange)
        {
            if (!inScope) return stockSpeed;
            WholePathRule.YieldAction a = WholePathRule.ForYield(RuleOn, true, m_Committed);
            float result = a == WholePathRule.YieldAction.StopAtSpot ? VehicleUtils.GetMaxBrakingSpeed(car, m_WaitDistance, safeTimeStep)
                : a == WholePathRule.YieldAction.Suppress ? float.MaxValue : stockSpeed;
            if (Counting && a != WholePathRule.YieldAction.Suppress)
            {
                RecordYield(MathUtils.Clamp(result, speedRange), start, a == WholePathRule.YieldAction.StopAtSpot);
            }
            return result;
        }

        /// <summary>Distance for the game's pedestrian check on a crossing of the path.</summary>
        public float PedestrianDistance(bool inScope, float stockDistance)
        {
            if (!inScope) return stockDistance;
            return WholePathRule.Distance(WholePathRule.ForPedestrian(RuleOn, true, m_Committed), stockDistance, m_WaitDistance);
        }

        /// <summary>After the game's priority look-ahead: returns the curve position the game goes on with.</summary>
        public float AfterProjection(bool inScope, float originalY, float projectedY, bool objectPastStart)
        {
            if (!inScope || projectedY == originalY) return projectedY;
            return WholePathRule.UndoProjection(RuleOn, true, m_Committed, objectPastStart) ? originalY : projectedY;
        }

        /// <summary>Distance for the game's vehicle braking (UpdateMaxSpeed) on a conflicting lane.</summary>
        public float VehicleDistance(bool inScope, float stockDistance, bool projected, bool merge)
        {
            if (!inScope) return stockDistance;
            return WholePathRule.Distance(WholePathRule.ForVehicle(RuleOn, true, m_Committed, projected, merge), stockDistance, m_WaitDistance);
        }

        /// <summary>Counters only: after the game's vehicle braking, note what limited the car.</summary>
        public void AfterVehicle(bool inScope, float maxSpeedBefore, float maxSpeedAfter, bool projected, bool merge, float start)
        {
            if (!inScope || !Counting || !(maxSpeedAfter < maxSpeedBefore)) return;
            WholePathRule.YieldAction a = WholePathRule.ForVehicle(RuleOn, true, m_Committed, projected, merge);
            if (projected)
            {
                RecordYield(maxSpeedAfter, start, a == WholePathRule.YieldAction.StopAtSpot);
            }
            else if (!merge)
            {
                if (a == WholePathRule.YieldAction.StopAtSpot) RecordLater(maxSpeedAfter, start);
                else if (m_Inside) Min(ref m_HasInsideCrossing, ref m_InsideCrossingSpeed, maxSpeedAfter);
            }
        }

        private void RecordYield(float speed, float start, bool stoppedAtSpot)
        {
            if (stoppedAtSpot) RecordLater(speed, start);
            else if (m_Inside) Min(ref m_HasInsideYield, ref m_InsideYieldSpeed, speed);
        }

        private void RecordLater(float speed, float start)
        {
            if (WholePathRule.AfterSpot(start, m_Spot, m_Inverse)) Min(ref m_HasLater, ref m_LaterSpeed, speed);
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
        }

        private static unsafe void Add(NativeArray<long> counters, int index)
        {
            long* p = (long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(counters);
            Interlocked.Increment(ref p[index]);
        }
    }
}
