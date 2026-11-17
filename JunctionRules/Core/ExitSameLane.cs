using System.Runtime.CompilerServices;

namespace JunctionRules.Core
{
    /// <summary>
    /// 0.4.12 (JR-T-018, gap G28), OWNER RULE 17 Nov 2026 (extends G22 to buildings): "entrances and exits of buildings are
    /// that yielded exit right" - a building EXIT is a yielding (lowest-priority) approach, so the G22 same-exit-lane
    /// exception applies to vehicles leaving buildings at exit boxes: a vehicle leaving a building may enter the exit box
    /// although its target lane past the box is full IF its whole-body waiting footprint overlaps ONLY paths into that same
    /// target lane; it then waits in the box at the back of the queue. A footprint that crosses any other lane / path (a
    /// far-side turn across the near lane, the other carriageway, an entrance path into a building) keeps the normal rule
    /// (wait before the pavement). Turning INTO a building is not an approach of the box in this sense (G21 unchanged).
    ///
    /// Reading (drive-lane exits: a drive's leaving lane, `SideConnection | Yield`, which crosses the pavement and joins a road
    /// lane; car-park gates / lots / building parking keep the 0.4.11 rule):
    /// <list type="bullet">
    /// <item>Boxed: the join point lies in a box band (paint = driving windows).</item>
    /// <item>Merge only (<see cref="MergeOnly"/>): every car-lane overlap still ahead on the leaving lane is the lane it joins
    /// (a near-side merge). Any crossed car lane (the near lane for a far-side turn, the other carriageway, another drive's
    /// way in / out) = no.</item>
    /// <item>Footprint (<see cref="Footprint"/>): front at the rear of the nearest vehicle STANDING ahead on the joined lane (or
    /// that lane's end) minus the margin; body = its whole length (trailers incl.) + margin behind the front, first on the
    /// joined lane back to the join point, the rest on the leaving lane's end. 0.4.12 OWNER DECISION REVISED: it may lie on the pavement / any crossing (the job passes walkEndM -1); the front must
    /// reach past the join point.</item>
    /// <item>Joined-lane paths (<see cref="TouchesOther"/>): no other overlap of the joined lane (another building's way in or
    /// out, a turn-in path) within the footprint's stretch of it: the exiter standing there never blocks an entrance path
    /// or a turn-in (G15 / G21 deadlocks).</item>
    /// </list>
    /// Then: the room-past-the-box test is replaced (exit judgement and the road-lane box hold, role Leaving), and on the
    /// joined lane a vehicle STANDING before the merge (not on it) is the queue it joins at the back, not present traffic;
    /// moving vehicles and one standing on the merge point are judged as before (entering for room never skips giving way).
    /// Counter shared with G22 ("entered a box under the same-exit-lane exception"). Game-independent, Burst-compatible.
    /// </summary>
    public static class ExitSameLane
    {
        /// <summary>
        /// The waiting footprint. joinM = the join point (metres along the joined lane, driving direction); joinLength = that
        /// lane's length; rearAhead = rear of the nearest vehicle standing ahead of the join point (&gt;= joinLength when none);
        /// wayOutLength = the leaving lane's length; walkEndM = end of its last pedestrian overlap (metres, -1 when none).
        /// joinFrom / joinTo = the footprint on the joined lane, wayFrom = where it starts on the leaving lane (= wayOutLength
        /// when the whole body is on the joined lane). False = no admissible footprint.
        /// </summary>
        public static bool Footprint(float joinM, float joinLength, float rearAhead, float ownLength, float margin, float wayOutLength, float walkEndM,
            out float joinFrom, out float joinTo, out float wayFrom)
        {
            float front = (rearAhead < joinLength ? rearAhead : joinLength) - margin;
            float body = (ownLength > 0f ? ownLength : 0f) + margin;
            joinTo = front;
            joinFrom = front - body > joinM ? front - body : joinM;
            float onJoin = front - joinM;
            float rest = body - onJoin;
            wayFrom = rest > 0f ? wayOutLength - rest : wayOutLength;
            if (!(onJoin > 0f)) return false;                       // the front cannot even get onto the lane
            if (wayFrom < 0f) return false;                         // the body would reach back past the leaving lane
            return !(walkEndM >= 0f && wayFrom < walkEndM);         // no part on the pavement
        }

        /// <summary>Every car overlap still ahead on the leaving lane is the joined lane (counts: car overlaps ahead, of them the joined lane).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool MergeOnly(int carOverlapsAhead, int joinedOverlaps) => carOverlapsAhead == joinedOverlaps;

        /// <summary>An overlap [s, e] of the joined lane (metres) touches the footprint [from, to].</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TouchesOther(float s, float e, float from, float to) => e > from && s < to;

        /// <summary>A vehicle on the joined lane is the queue the exiter joins at the back (not present): standing, not on the merge, before it.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        /// Review F2: standing = at most <see cref="ExitGiveWay.EnteringStandSpeed"/> (0.2 m/s, as G15): a queue car starting to move counts again.
        public static bool QueueNotPresent(bool g28, bool occupies, float gap, float speed) => g28 && !occupies && gap > 0f && speed <= ExitGiveWay.EnteringStandSpeed;
    }
}
