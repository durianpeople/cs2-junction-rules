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
    /// Burst-compatible). Since 0.2.3 only the entry spot is ever added (no mid-junction spots, owner clarification
    /// 14 Nov 2026), so Count is 0 or 1; the type keeps its 0.2.1 shape to keep the change small.
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
        /// <summary>The game's own handling (rule off, out of scope, or the 0.2.1 decision fix: the car cannot stop at its spot but can still stop before the point).</summary>
        Stock = 0,
        /// <summary>The car is before the waiting spot of this point's stretch and can stop there: the decision for the stretch, taken at the spot.</summary>
        Deciding = 1,
        /// <summary>The car is crossing this point's stretch, the point is still ahead, and the car can stop comfortably before the first uncrossed point: safety stop there.</summary>
        Safety = 2,
        /// <summary>The car is crossing this point's stretch and cannot stop comfortably before it (or is already in it): no give-way, stock collision braking only.</summary>
        Crossing = 3,
    }

    /// <summary>
    /// The rule (0.2.1 safety re-check; 0.2.3 owner clarification 14 Nov 2026, binding): a car's path through the node
    /// has ordered conflict points. The car waits just before its FIRST conflict point (entry spot W, the 0.2.0 spot)
    /// and makes ONE decision there for its whole path (stock judgement on every point, with the 0.2.3 arrival-time
    /// projection on the job side). No mid-junction spots are planned (0.2.1 had them; superseded): the whole path is
    /// one stretch. While crossing it keeps checking every uncrossed point; if one is no longer clear by the stock gap
    /// judgement (same projection) and it can stop comfortably, it stops before the first uncrossed point and waits
    /// there until all remaining points are clear; if it cannot, only stock collision braking.
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
        /// <summary>1.5.0 (JR-T-038 item B): first conflict point of the next stretch (spot still ahead) that the front has not reached (2 = none).</summary>
        public float FirstAhead;

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
        /// Builds the spot of a lane (false: no conflict point, the rule does not cover this lane). 0.2.3 (owner
        /// clarification 14 Nov 2026): the only waiting spot is the entry spot W, just before the FIRST conflict point;
        /// no mid-junction spots are ever planned, so the whole path through the node is one stretch and one decision.
        /// atLine = "wait at the stop line" variant: the entry spot moves to the lane start.
        /// </summary>
        public bool Begin<T>(ref T list, float origDistance, float length, float curveX, float margin, float braking, bool atLine)
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
            FirstAhead = 2f;
            FindSpots(ref list, ref Spots);
            if (Spots.Count == 0) return false;
            if (atLine) Spots[0] = 0f;
            Locate(ref list);
            return true;
        }

        /// <summary>
        /// 0.4.1 (JR-T-009): as <see cref="Begin"/> with the spots given (Core/ZebraSections.cs: section starts, zebras
        /// included, nearest first, the variant already applied). list = the lane's vehicle points (first uncrossed point).
        /// </summary>
        public void BeginWithSpots<T>(ref T list, WaitingSpots spots, float origDistance, float length, float curveX, float margin, float braking)
            where T : struct, IConflictList
        {
            Spots = spots;
            OrigDistance = origDistance;
            Length = length;
            CurveX = curveX;
            Margin = margin;
            Braking = braking;
            Next = 0;
            FirstUncrossed = 2f;
            FirstAhead = 2f;
            Locate(ref list);
        }

        /// <summary>Where the car is: the next spot ahead and, inside a stretch, its first uncrossed vehicle point.</summary>
        private void Locate<T>(ref T list) where T : struct, IConflictList
        {
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
            if (Next < Spots.Count)
            {
                float lo = Spots[Next] - WholePathRule.SpotEpsilon, hi = Spots.StretchEnd(Next) - WholePathRule.SpotEpsilon;
                for (int i = 0; i < list.Count; i++)
                {
                    if (!list.Conflict(i, out float s, out _)) continue;
                    if (s >= lo && s < hi && s < FirstAhead && !FrontPast(s)) FirstAhead = s;
                }
            }
        }

        /// <summary>
        /// The spot of a lane: the start of its first conflict point (the entry spot W), or none. 0.2.3: no mid spots
        /// (owner clarification 14 Nov 2026: central reservations are not special).
        /// </summary>
        public static void FindSpots<T>(ref T list, ref WaitingSpots spots) where T : struct, IConflictList
        {
            spots = default;
            float first = 2f;
            for (int i = 0; i < list.Count; i++)
            {
                if (list.Conflict(i, out float s, out _) && s < first) first = s;
            }
            if (first <= 1f) spots.Add(first);
        }

        /// <summary>
        /// Classifies one stock give-way demand from a conflict point starting at <paramref name="u"/> (driving
        /// coordinates, at or after the entry spot). stopWait = the distance the car stops at under Deciding (the
        /// entry spot) or Safety (the first uncrossed point of the path).
        /// </summary>
        public readonly PointPhase Classify(bool ruleOn, float u, out float stopWait)
        {
            int k = Spots.StretchOf(u);
            stopWait = 0f;
            if (!ruleOn) return PointPhase.Stock;
            if (k >= Next)
            {
                // The spot of this point's stretch is still ahead: the decision for the stretch is taken there.
                float spot = Spots[k];
                stopWait = Wait(spot);
                if (CanStopBefore(spot)) return PointPhase.Deciding;
                // 0.2.1 decision fix: a car that cannot stop at the spot any more keeps the game's own give-way for
                // this point (as 0.2.0 it would brake at full rate for the spot, overrun it and then drop the give-way).
                // 1.5.0 (JR-T-038 item B, never wait inside the lane you give way to): the game's give-way brakes as hard
                // as it can even when the overlap start is already inside the braking distance (GetMaxBrakingSpeed of a
                // distance <= 0), so the car stood with its front inside the lane. Now: if it can still stop before an
                // earlier point of the stretch, it stops there (Safety, as while crossing); if it can stop before this
                // point, the game's give-way (its stop is before the overlap start); if it cannot, it commits and clears
                // (Crossing: no give-way, the 2 s look-ahead undone, collision braking for cars physically there stays).
                float earliest = k == Next && FirstAhead < u ? FirstAhead : u;
                if (earliest < u && CanStopBefore(earliest))
                {
                    stopWait = Wait(earliest);
                    return PointPhase.Safety;
                }
                stopWait = 0f;
                return CanStopBefore(u) ? PointPhase.Stock : PointPhase.Crossing;
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
