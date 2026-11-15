using System;
using System.Runtime.CompilerServices;

namespace JunctionRules.Core
{
    /// <summary>
    /// 0.2.4 (JR-T-002h addition, owner 14 Nov 2026): keeps the feeding-lane (approach-lane) scan of S2-12 cheap.
    /// Who scans, a per-car-update memo of finished scans, and the work cap. Pure logic, Burst-compatible (no
    /// allocation, no arrays: fixed fields), tested offline (tests/SignalTests.cs FeedingScanTests).
    ///
    /// Note on the decision itself: since JR-T-002g a feeding car counts only when GetPriority(car) &gt; num6, and then
    /// the stock reservation test <c>offset &gt; num10 || priority &gt; num6</c> is always true through its second half,
    /// so a feeding car counts exactly when it wins priority, is not stopped by its signal (0.2.4) and its projected
    /// reach passes the end of its lane (VirtualOffset &gt;= 0). The memo below relies on that.
    /// </summary>
    public static class FeedingScan
    {
        /// <summary>
        /// Most cars read per car-update over all its feeding scans. Normal traffic: a feeding lane holds a handful of
        /// moving cars within reach; several approaches x lanes stay well under this. When hit, the scan stops and the
        /// cars not read are NOT counted (the stock behaviour: stock never counts feeding cars; the stock reservation,
        /// look-ahead, safety re-check and collision braking still apply). Erring "busy" instead could hold a car at a
        /// busy main road for ever. Cap hits are counted in the diagnostics.
        /// </summary>
        public const int MaxCarsPerUpdate = 96;

        /// <summary>Most feeding lanes looked at per scan (a source edge's lanes ending at the junction lane's start).</summary>
        public const int MaxLanesPerScan = 16;

        /// <summary>
        /// Who scans. Only the car that decides: deciding at its entry spot W (not a follower: no car ahead of it on
        /// its own approach lane before W) or re-checking while crossing (Safety, the car itself). Never when the
        /// junction-lane checks already hold it at least as hard as a feeding hit would (hold &gt;= currentMax): that
        /// also leaves out every car further from W than its braking distance (GetMaxBrakingSpeed(d) &lt; v exactly when
        /// d &lt; GetBrakingDistance(v)), and never when the conflicting lane's own reservation already makes stock hold
        /// it the same way (reservationBlocks).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ShouldScan(PointPhase phase, bool hasCarAhead, float hold, float currentMax, bool reservationBlocks)
        {
            if (phase == PointPhase.Deciding) { if (hasCarAhead) return false; }
            else if (phase != PointPhase.Safety) return false;
            if (!(hold < currentMax)) return false;
            return !reservationBlocks;
        }

        /// <summary>
        /// 0.3.3 (JR-T-005, owner 15 Nov 2026: "should not happen"): no feeding scan for a conflicting junction lane the
        /// game marks Unsafe (Game.Net.CarLaneFlags.Unsafe: the unsafe U-turns and other last-resort connections the
        /// pathfinder penalises). Every car on the lane feeding it would otherwise count as if it were about to take it
        /// (the scan does not read routes), e.g. at Moonflower Street x Anchor Street a car on Anchor westbound turning
        /// right into Moonflower counted as a U-turner on the unsafe U-turn lane that merges into the left-turner's
        /// exit lane, so the left-turner from Moonflower was held for a car whose path never meets its own. Stock alone
        /// never sees such a car (no reservation, no car on that lane). A car really taking the unsafe lane is still
        /// judged by stock: its reservation (S2-06) and its presence on the lane (look-ahead, collision braking).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SkipsConflictingLane(uint carLaneFlags) => (carLaneFlags & TurnAcross.Unsafe) != 0;

        /// <summary>
        /// The upper bound a finished scan of one feeding source leaves in the memo: no car there can count against
        /// num6 / own arrival time delta when maxPriority &lt;= num6 or maxReachBase + maxSpeed * delta &lt;= 0
        /// (reach - remaining = (brakingDistance - 0.01) + speed * delta - remaining, bounded by the maxima).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool NothingCanCount(int maxPriority, float maxReachBase, float maxSpeed, int num6, float delta)
        {
            if (maxPriority - num6 <= 0) return true;
            float d = delta > 0f ? delta : 0f;
            return !(maxReachBase + maxSpeed * d > 0f);
        }
    }

    /// <summary>
    /// Per-car-update memo of finished feeding scans, keyed by the feeding source (the junction lane's start PathNode
    /// on the job side). Four fixed slots, round robin; a car-update rarely meets more distinct sources before it is
    /// held. Stores, over the candidate cars (moving towards the lane end, not the deciding car), the highest priority,
    /// the highest (brakingDistance - 0.01 - remaining) and the highest speed. Only a scan that ran to the end without
    /// a hit or a cap is stored. Lives in the iterator's JRWholeJunction (a local of the job, one per car-update).
    /// </summary>
    public struct FeedScanMemo<TKey> where TKey : struct, IEquatable<TKey>
    {
        private TKey m_K0, m_K1, m_K2, m_K3;
        private int m_P0, m_P1, m_P2, m_P3;
        private float m_A0, m_A1, m_A2, m_A3;
        private float m_V0, m_V1, m_V2, m_V3;
        private int m_Count;
        private int m_Next;

        public const int Slots = 4;

        public int Count => m_Count;

        public bool TryGet(TKey key, out int maxPriority, out float maxReachBase, out float maxSpeed)
        {
            if (m_Count > 0 && m_K0.Equals(key)) { maxPriority = m_P0; maxReachBase = m_A0; maxSpeed = m_V0; return true; }
            if (m_Count > 1 && m_K1.Equals(key)) { maxPriority = m_P1; maxReachBase = m_A1; maxSpeed = m_V1; return true; }
            if (m_Count > 2 && m_K2.Equals(key)) { maxPriority = m_P2; maxReachBase = m_A2; maxSpeed = m_V2; return true; }
            if (m_Count > 3 && m_K3.Equals(key)) { maxPriority = m_P3; maxReachBase = m_A3; maxSpeed = m_V3; return true; }
            maxPriority = -1; maxReachBase = float.NegativeInfinity; maxSpeed = 0f;
            return false;
        }

        public void Put(TKey key, int maxPriority, float maxReachBase, float maxSpeed)
        {
            int slot = m_Next;
            m_Next = (m_Next + 1) & (Slots - 1);
            if (m_Count < Slots) m_Count++;
            switch (slot)
            {
                case 0: m_K0 = key; m_P0 = maxPriority; m_A0 = maxReachBase; m_V0 = maxSpeed; break;
                case 1: m_K1 = key; m_P1 = maxPriority; m_A1 = maxReachBase; m_V1 = maxSpeed; break;
                case 2: m_K2 = key; m_P2 = maxPriority; m_A2 = maxReachBase; m_V2 = maxSpeed; break;
                default: m_K3 = key; m_P3 = maxPriority; m_A3 = maxReachBase; m_V3 = maxSpeed; break;
            }
        }
    }
}
