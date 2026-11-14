using System.Runtime.CompilerServices;

namespace JunctionRules.Core
{
    /// <summary>
    /// A list of the conflicts of one junction (node) lane, as the stretch rule reads it. The job side wraps the game's
    /// LaneOverlap buffer (Stage2/JRWholeJunction.cs), the tests wrap an array. Burst: used only as a generic struct
    /// constraint, never boxed.
    /// </summary>
    public interface IConflictList
    {
        int Count { get; }

        /// <summary>
        /// Conflict i in driving coordinates (0 = where the car enters the lane, 1 = where it leaves it). Returns false
        /// when the overlap is not a conflict point (does not place a waiting spot: see WholePathRule.DefinesWaitingSpot).
        /// </summary>
        bool Conflict(int i, out float start, out float end);
    }

    /// <summary>
    /// The waiting spots of one junction lane, in driving coordinates, nearest first. Fixed capacity (no allocation,
    /// Burst-compatible): a lane with more spots keeps the first <see cref="Capacity"/> (the rest fold into the last
    /// stretch, i.e. fewer stops inside, never more).
    /// </summary>
    public struct WaitingSpots
    {
        public const int Capacity = 8;
        private float m_S0, m_S1, m_S2, m_S3, m_S4, m_S5, m_S6, m_S7;
        public int Count;

        public float this[int i]
        {
            readonly get
            {
                switch (i)
                {
                    case 0: return m_S0;
                    case 1: return m_S1;
                    case 2: return m_S2;
                    case 3: return m_S3;
                    case 4: return m_S4;
                    case 5: return m_S5;
                    case 6: return m_S6;
                    default: return m_S7;
                }
            }
            set
            {
                switch (i)
                {
                    case 0: m_S0 = value; break;
                    case 1: m_S1 = value; break;
                    case 2: m_S2 = value; break;
                    case 3: m_S3 = value; break;
                    case 4: m_S4 = value; break;
                    case 5: m_S5 = value; break;
                    case 6: m_S6 = value; break;
                    default: m_S7 = value; break;
                }
            }
        }

        public bool Add(float spot)
        {
            if (Count >= Capacity) return false;
            this[Count++] = spot;
            return true;
        }

        /// <summary>Index of the stretch a conflict starting at <paramref name="start"/> belongs to (the last spot at or before it).</summary>
        public readonly int StretchOf(float start)
        {
            int k = 0;
            for (int i = 1; i < Count; i++)
            {
                if (this[i] <= start + WholePathRule.SpotEpsilon) k = i;
            }
            return k;
        }

        /// <summary>End of stretch k (the next spot), or 2 for the last stretch (beyond every offset).</summary>
        public readonly float StretchEnd(int k) => k + 1 < Count ? this[k + 1] : 2f;
    }

    /// <summary>
    /// How the rule treats one stock give-way demand (owner rulings 14 Nov 2026, JR-T-002c "stretches").
    /// </summary>
    public enum PointPhase
    {
        /// <summary>The game's own handling (rule off, out of scope, or the 0.2.1 decision fix: the car cannot stop at its spot).</summary>
        Stock = 0,
        /// <summary>The car is before the waiting spot of this point's stretch and can stop there: the decision for the stretch, taken at the spot.</summary>
        Deciding = 1,
        /// <summary>The car is crossing this point's stretch, the point is still ahead, and the car can stop comfortably before the first uncrossed point: safety stop there.</summary>
        Safety = 2,
        /// <summary>The car is crossing this point's stretch and cannot stop comfortably before it (or is already in it): no give-way, stock collision braking only.</summary>
        Crossing = 3,
    }

    /// <summary>
    /// The 0.2.1 rule (owner wording 14 Nov 2026, binding): a car's path through the node has ordered conflict points.
    /// A waiting spot is any position right before a conflict point where the car is not inside any other lane's path:
    /// the entry spot W (the 0.2.0 spot) and every gap between conflict points that the car's own body fits into (its
    /// front stops at the game's stop margin before the next point, its rear clear of the previous points). The conflict
    /// points from one spot to the next form a stretch. At each spot the car makes one decision for its stretch (stock
    /// judgement on every point of it). While crossing a stretch it keeps checking every uncrossed point of that stretch;
    /// if one is no longer clear by the stock gap judgement and it can stop comfortably, it stops before the first
    /// uncrossed point and waits there until all remaining points of the stretch are clear; if it cannot, only stock
    /// collision braking. With no mid spots this is exactly the 0.2.0 single decision at W plus the safety net.
    ///
    /// "Comfortably" = the game's own can-stop test for an overlap, CarLaneSpeedIterator.CheckOverlappingLanes
    /// <c>GetBrakingDistance(car, currentSpeed, timeStep) &lt;= num5 + num</c>: stopping before the point within the car's
    /// prefab braking (the only braking rate the game has; CalculateSpeedRange never brakes harder). No new margin.
    ///
    /// Pure arithmetic on plain values (Burst-compatible, no allocation); distances use the game's num5 formula.
    /// </summary>
    public struct StretchState
    {
        public WaitingSpots Spots;
        /// <summary>The game's origDistance (after its stop margin) for this lane: distance from the car's front to the lane start, minus the margin.</summary>
        public float OrigDistance;
        public float Length;
        /// <summary>The car's curve offset on this lane in driving coordinates.</summary>
        public float CurveX;
        /// <summary>The game's stop margin (0.9 m, bicycles 0.4 m).</summary>
        public float Margin;
        /// <summary>The game's braking distance at the car's current speed (VehicleUtils.GetBrakingDistance(car, speed, timeStep)).</summary>
        public float Braking;
        /// <summary>Index of the next spot ahead of the car's front (= Spots.Count when it has passed them all).</summary>
        public int Next;
        /// <summary>First conflict point of the stretch being crossed that the front has not reached (2 = none).</summary>
        public float FirstUncrossed;

        /// <summary>Distance from the car to its stop point before <paramref name="u"/> (the game's num5 formula, margin included).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly float Wait(float u) => OrigDistance + Length * (u - CurveX);

        /// <summary>The car's front is past <paramref name="u"/> (WholePathRule.FrontPastSpot).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool FrontPast(float u) => WholePathRule.FrontPastSpot(Wait(u), Margin);

        /// <summary>The game's own can-stop test (num5 + num): the braking distance fits before the point.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool CanStopBefore(float u) => Braking <= Wait(u) + Margin;

        /// <summary>The car has passed the entry spot W (0.2.0 "committed").</summary>
        public readonly bool PastEntry => Next > 0;

        /// <summary>
        /// Builds the spots of a lane (false: no conflict point, the rule does not cover this lane). carLength = the car's
        /// own length (prefab bounds, front to rear); a gap between conflict points is a waiting spot when the car fits in
        /// it: front at the stop margin before the next point, rear clear of the previous ones (gap * length &gt;=
        /// carLength + margin). The entry spot needs no room (the rear is on the approach). atLine = "wait at the stop
        /// line" variant: the entry spot moves to the lane start; mid spots stay.
        /// </summary>
        public bool Begin<T>(ref T list, float origDistance, float length, float curveX, float margin, float braking, float carLength, bool atLine)
            where T : struct, IConflictList
        {
            Spots = default;
            OrigDistance = origDistance;
            Length = length;
            CurveX = curveX;
            Margin = margin;
            Braking = braking;
            Next = 0;
            FirstUncrossed = 2f;
            FindSpots(ref list, length, carLength + margin, ref Spots);
            if (Spots.Count == 0) return false;
            if (atLine) Spots[0] = 0f;
            while (Next < Spots.Count && FrontPast(Spots[Next])) Next++;
            if (Next > 0)
            {
                int k = Next - 1;
                float lo = Spots[k] - WholePathRule.SpotEpsilon, hi = Spots.StretchEnd(k) - WholePathRule.SpotEpsilon;
                for (int i = 0; i < list.Count; i++)
                {
                    if (!list.Conflict(i, out float s, out _)) continue;
                    if (s >= lo && s < hi && s < FirstUncrossed && !FrontPast(s)) FirstUncrossed = s;
                }
            }
            return true;
        }

        /// <summary>
        /// The spots of a lane: the entry spot (start of the first conflict) and every gap of at least
        /// <paramref name="room"/> metres between the end of a group of overlapping conflicts and the next conflict.
        /// Conflicts that touch or overlap (within SpotEpsilon) form one group: no spot inside another lane's path.
        /// </summary>
        public static void FindSpots<T>(ref T list, float length, float room, ref WaitingSpots spots) where T : struct, IConflictList
        {
            spots = default;
            int n = list.Count;
            float first = 2f;
            for (int i = 0; i < n; i++)
            {
                if (list.Conflict(i, out float s, out _) && s < first) first = s;
            }
            if (first > 1f) return;
            spots.Add(first);
            float groupEnd = first;
            while (true)
            {
                // Extend the group: every conflict starting at or before its end belongs to it.
                bool grown = true;
                while (grown)
                {
                    grown = false;
                    for (int i = 0; i < n; i++)
                    {
                        if (list.Conflict(i, out float s, out float e) && s <= groupEnd + WholePathRule.SpotEpsilon && e > groupEnd)
                        {
                            groupEnd = e;
                            grown = true;
                        }
                    }
                }
                float next = 2f;
                for (int i = 0; i < n; i++)
                {
                    if (list.Conflict(i, out float s, out _) && s > groupEnd + WholePathRule.SpotEpsilon && s < next) next = s;
                }
                if (next > 1f) return;
                if ((next - groupEnd) * length >= room && !spots.Add(next)) return;
                groupEnd = next;
            }
        }

        /// <summary>
        /// Classifies one stock give-way demand from a conflict point starting at <paramref name="u"/> (driving
        /// coordinates, at or after the entry spot). stopWait = the distance the car stops at under Deciding (its
        /// stretch's spot) or Safety (the first uncrossed point of the stretch); mid = the stretch's spot is not the entry
        /// spot.
        /// </summary>
        public readonly PointPhase Classify(bool ruleOn, float u, out float stopWait, out bool mid)
        {
            int k = Spots.StretchOf(u);
            mid = k > 0;
            stopWait = 0f;
            if (!ruleOn) return PointPhase.Stock;
            if (k >= Next)
            {
                // The spot of this point's stretch is still ahead: the decision for the stretch is taken there.
                float spot = Spots[k];
                stopWait = Wait(spot);
                // 0.2.1 decision fix: a car that cannot stop at the spot any more keeps the game's own give-way for
                // this point (as 0.2.0 it would brake at full rate for the spot, overrun it and then drop the give-way).
                return CanStopBefore(spot) ? PointPhase.Deciding : PointPhase.Stock;
            }
            // Crossing this stretch (front past its spot). A point the front has reached is being crossed.
            if (FrontPast(u)) return PointPhase.Crossing;
            float first = FirstUncrossed < u ? FirstUncrossed : u;
            stopWait = Wait(first);
            return CanStopBefore(first) ? PointPhase.Safety : PointPhase.Crossing;
        }

        /// <summary>A blocking reservation or the 2 s priority look-ahead (the stock gap judgement: "a car will be there").</summary>
        public static WholePathRule.YieldAction ForPrediction(PointPhase phase) =>
            phase == PointPhase.Deciding || phase == PointPhase.Safety ? WholePathRule.YieldAction.StopAtSpot
            : phase == PointPhase.Crossing ? WholePathRule.YieldAction.Suppress : WholePathRule.YieldAction.Stock;

        /// <summary>
        /// A car asking for space (reservation priority 96): before the stretch's spot it holds the car at the spot (as
        /// 0.2.0); while crossing it is dropped as in 0.2.0 (the asking car stands; waiting for it inside could be a
        /// mutual wait, see STAGE2-ANALYSIS "0.2.1 deadlock").
        /// </summary>
        public static WholePathRule.YieldAction ForSpaceRequest(PointPhase phase) =>
            phase == PointPhase.Deciding ? WholePathRule.YieldAction.StopAtSpot
            : phase == PointPhase.Safety || phase == PointPhase.Crossing ? WholePathRule.YieldAction.Suppress : WholePathRule.YieldAction.Stock;

        /// <summary>
        /// A car on the conflicting lane. Projected (counts only through the 2 s look-ahead) = a prediction. Physical (in
        /// or reserving the overlap): before the spot a crossing car holds the car at the spot (0.2.0); while crossing it
        /// is stock collision braking at that point (never a wait further back: see the deadlock note). Merge = stock
        /// car-following always.
        /// </summary>
        public static WholePathRule.YieldAction ForVehicle(PointPhase phase, bool projected, bool merge)
        {
            if (projected) return ForPrediction(phase);
            if (phase == PointPhase.Deciding && !merge) return WholePathRule.YieldAction.StopAtSpot;
            return WholePathRule.YieldAction.Stock;
        }

        /// <summary>Pedestrians: before the spot they hold the car at the spot (0.2.0); otherwise the game's own pedestrian braking.</summary>
        public static WholePathRule.YieldAction ForPedestrian(PointPhase phase) =>
            phase == PointPhase.Deciding ? WholePathRule.YieldAction.StopAtSpot : WholePathRule.YieldAction.Stock;

        /// <summary>The 2 s look-ahead is undone only where the give-way is dropped (Crossing) and the other car is not already past the overlap start.</summary>
        public static bool UndoProjection(PointPhase phase, bool objectPastStart) => phase == PointPhase.Crossing && !objectPastStart;
    }

    /// <summary>Overlap bytes to driving coordinates (shared by the job side and the tests).</summary>
    public static class StretchGeometry
    {
        /// <summary>
        /// A LaneOverlap of the car's lane as a conflict point in driving coordinates: start/end measured from where the
        /// car enters the lane (an inverted lane is driven from offset 1 to 0). Returns WholePathRule.DefinesWaitingSpot.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ToConflict(int flags, int thisStart, int thisEnd, bool inverse, bool otherHeldByRed, out float start, out float end)
        {
            float a = thisStart * WholePathRule.ByteToOffset, b = thisEnd * WholePathRule.ByteToOffset;
            start = inverse ? 1f - b : a;
            end = inverse ? 1f - a : b;
            return WholePathRule.DefinesWaitingSpot(flags, inverse, otherHeldByRed);
        }

        /// <summary>A lane offset (game coordinates) in driving coordinates.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToDriving(float offset, bool inverse) => inverse ? 1f - offset : offset;
    }
}
