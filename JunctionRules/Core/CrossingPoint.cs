using System.Runtime.CompilerServices;

namespace JunctionRules.Core
{
    /// <summary>
    /// A lane's overlaps with their raw flags, in driving coordinates (0 = where the car enters the lane). The job side
    /// wraps the game's LaneOverlap buffer (Stage2/JRWholeJunction.cs), the tests an array. Burst: generic constraint only.
    /// </summary>
    public interface IOverlapRanges
    {
        int Count { get; }

        /// <summary>Overlap i: its Game.Net.OverlapFlags bits and its range in driving coordinates.</summary>
        void Get(int i, out int flags, out float start, out float end);
    }

    /// <summary>
    /// 0.3.3 (JR-T-006, owner 15 Nov 2026): pedestrian (and cyclist) crossings are not part of the whole-junction gap.
    /// "It's fine that it checks the speed and time of all cars going to the conflict points, but it should not take
    /// into account the pedestrians. Cars should stop right before where the pedestrian conflict point crosses." Owner
    /// clarification the same day: that stop is NOT mandatory: the car waits before the crossing only when the crossing
    /// is not clear (people on or approaching it, as stock judges); with a clear crossing it drives straight through.
    ///
    /// So a crossing of the path that starts at or after the entry spot W is judged by the game's own pedestrian /
    /// cyclist check at the crossing itself (stock distance = just before the crossing; 0.3.2 "never push through
    /// people" still applies there), NOT in the decision at W and not in the S2-08 hold at W, PROVIDED the whole
    /// vehicle (trailers included, JR-T-002d length) fits between the end of the last vehicle point before the crossing
    /// and the crossing's stop point. If it does not fit, the crossing stays in the decision at W (the 0.3.2 behaviour):
    /// standing before the crossing would leave the car's tail in a vehicle point behind it. That fallback is the
    /// existing rule, not a new safeguard; the owner decides whether to keep it (PROBE note).
    ///
    /// Vehicle point ("clearance point") for the fit: a road or track overlap the game yield-checks (not water, not a
    /// diverging sibling from the same start) that does not run into the car's own end (no end-merge flag in driving
    /// direction): standing inside a merge into the same exit lane is ordinary queueing in that lane (stock car
    /// following), not a blocked crossing path, so merges do not count. Red-held lanes DO count (the tail would block
    /// them at the next green). Pure arithmetic, Burst-compatible.
    /// </summary>
    public static class CrossingPoint
    {
        /// <summary>The overlap is a vehicle point a waiting car must clear (see the class summary).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsClearancePoint(int flags, bool inverse)
        {
            if ((flags & WholePathRule.Water) != 0 || (flags & (WholePathRule.Road | WholePathRule.Track)) == 0) return false;
            if (!WholePathRule.IsYieldChecked(flags, inverse)) return false;
            int endMerge = inverse ? (WholePathRule.MergeStart | WholePathRule.MergeMiddleStart) : (WholePathRule.MergeEnd | WholePathRule.MergeMiddleEnd);
            return (flags & endMerge) == 0;
        }

        /// <summary>
        /// End (driving coordinates) of the last vehicle point that starts before the crossing at
        /// <paramref name="crossingStart"/>; 0 (the lane start) when there is none. A vehicle point that starts before the
        /// crossing and reaches over it returns its end (&gt; crossingStart): then nothing fits.
        /// </summary>
        public static float PreviousEnd<T>(ref T list, float crossingStart, bool inverse) where T : struct, IOverlapRanges
        {
            float prev = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                list.Get(i, out int flags, out float s, out float e);
                if (!IsClearancePoint(flags, inverse)) continue;
                if (s < crossingStart - WholePathRule.SpotEpsilon && e > prev) prev = e;
            }
            return prev;
        }

        /// <summary>
        /// The whole vehicle fits between the previous vehicle point's end and the stop point before the crossing: the
        /// game stops the front <paramref name="margin"/> before the crossing start (CheckOverlappingLanes num5 =
        /// origDistance - margin + ...), so the room is laneLength * (crossingStart - previousEnd) - margin.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Fits(float laneLength, float crossingStart, float previousEnd, float margin, float vehicleLength) =>
            laneLength * (crossingStart - previousEnd) - margin >= vehicleLength;

        /// <summary>
        /// The crossing is judged at the crossing (the game's own check, stock distance) rather than in the decision at W:
        /// rule on, the overlap is a crossing (the other lane is a pedestrian lane) and the car fits before it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool JudgedAtCrossing(bool ruleOn, bool isCrossing, bool fits) => ruleOn && isCrossing && fits;
    }
}
