using System.Runtime.CompilerServices;

namespace JunctionRules.Core
{
    /// <summary>
    /// 0.4.11 (JR-T-017, gap G22), owner rule 17 Nov 2026, verbatim: "Cars from a yielding (or if everyone is yielding, then
    /// the one getting STOP is 'prioritised' vs YIELD exits) approach: may enter a box whose exit is blocked iff the spot where
    /// the car would wait (its whole body) only crosses paths into the same exit lane it is queued for."
    ///
    /// Reading (junction boxes only; exit / entrance boxes keep G9 / G21):
    /// <list type="bullet">
    /// <item>Eligible approach (<see cref="Eligible"/>): the car's junction lane yields (Stop or Yield sign status, the game's
    /// own flags on the node lane). With a priority approach at the junction (a lane with neither: plain or RightOfWay), every
    /// yielding approach is eligible. If every approach yields: the STOP approaches only when STOP and YIELD are mixed (STOP is
    /// the lowest priority); all-STOP or all-YIELD: every approach. A signalled junction (any TrafficLights / LevelCrossing
    /// lane), a roundabout lane or a lane with no sign status of its own (Unsafe only): never (conservative).</item>
    /// <item>Waiting footprint (<see cref="Footprint"/>): where it would stand while its exit lane is full: front at the end of
    /// its junction lane, or before an exit zebra (a pedestrian overlap in the far half of the lane, the zebra rule never lets
    /// it stand on one), or behind the rear of a vehicle already standing ahead on its own junction lane; body = its whole
    /// length (trailers incl.) + the stop margin behind the front. The part before the lane start is on its approach lane,
    /// outside the box.</item>
    /// <item>Same exit (<see cref="Admits"/>): every overlap of its junction lane that the footprint touches must be a car lane
    /// that ENDS in the same exit lane (Lane.m_EndNode equal: the same target lane of the same exit edge). Any other car lane
    /// (a priority through path, a path to another exit lane), any pedestrian crossing, any track: the normal box rule (wait
    /// before the box).</item>
    /// </list>
    /// Combination: the 0.4.10 nose rule and the "free now" room test stay for everybody else; the exception replaces only the
    /// room-past test of an eligible car whose footprint admits it (at the stop line, BoxHold, and at the UK right-turn spot,
    /// BoxTurnRoomHold, where a turner's re-check would otherwise stop it on another footprint). Box chains: a vehicle standing
    /// ahead on its own lane moves the footprint back (so it is judged where it would really stand). The gap rule (whole
    /// junction / turn-across / UK right-turn spot) is unchanged: entering for room never skips giving way. Deadlocks: the car
    /// blocks only paths into the exit lane it waits for; anyone it blocks needs that same full lane, so no wait cycle runs
    /// through it that would not exist without it (tests G22Tests). Game-independent, Burst-compatible.
    /// </summary>
    public static class SameExitException
    {
        public const uint Unsafe = 0x1u, LevelCrossing = 0x40u, Yield = 0x400u, Stop = 0x800u, Approach = 0x200000u, Roundabout = 0x400000u,
            RightOfWay = 0x4000000u, TrafficLights = 0x8000000u, SideConnection = 0x8u;

        /// <summary>Sign rank of a junction lane: 0 = STOP, 1 = YIELD, 2 = priority (no sign or RightOfWay). Stock comparison
        /// (LaneOverlapSystem 1374-1385): Stop below Yield below none; Approach lanes have their signs cleared (TurnAcross.SignStatus).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Rank(uint flags)
        {
            uint s = TurnAcross.SignStatus(flags);
            if ((s & Stop) != 0) return 0;
            if ((s & Yield) != 0) return 1;
            return 2;
        }

        /// <summary>A junction lane that takes part in the ranking (a real approach path: not a drive's side connection, not an
        /// Unsafe lane such as a U-turn, whose missing sign would otherwise look like a priority approach).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Ranked(uint flags) => (flags & (SideConnection | Unsafe)) == 0;

        /// <summary>A junction where the exception never applies: signals / level crossing / roundabout.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Excludes(uint flags) => (flags & (TrafficLights | LevelCrossing | Roundabout)) != 0;

        /// <summary>
        /// The car's approach is eligible: ownRank = Rank of its junction lane; minRank / maxRank over every ranked car lane of the
        /// junction; excluded = a signalled / level-crossing / roundabout junction.
        /// </summary>
        public static bool Eligible(int ownRank, int minRank, int maxRank, bool excluded)
        {
            if (excluded || ownRank >= 2) return false;
            if (maxRank >= 2) return true;          // a priority approach exists: every yielding approach
            return ownRank == minRank;              // all yield: STOP before YIELD (all-STOP / all-YIELD: everyone)
        }

        /// <summary>
        /// The footprint on the junction lane, in metres along it (driving direction). laneLength = its length; exitZebraStart =
        /// start of the first pedestrian overlap in the far half (or a value &gt;= laneLength when none); rearAhead = rear of the
        /// nearest vehicle standing ahead of the car on this lane (&gt;= laneLength when none); ownLength incl. trailers.
        /// </summary>
        public static void Footprint(float laneLength, float exitZebraStart, float rearAhead, float ownLength, float margin, out float from, out float to)
        {
            float front = laneLength;
            if (exitZebraStart < front) front = exitZebraStart;
            if (rearAhead < front) front = rearAhead;
            to = front - margin;
            from = to - (ownLength > 0f ? ownLength : 0f) - margin;
        }

        /// <summary>Review 3 F2: the whole footprint lies on the junction lane (none of the body back on the approach).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool WithinLane(float from) => from >= 0f;

        public enum Path : byte
        {
            /// <summary>A car lane into the same exit lane (ok).</summary>
            SameExit = 0,
            /// <summary>A car lane to another exit lane / a through path (blocks someone who could otherwise move).</summary>
            OtherExit = 1,
            /// <summary>A pedestrian crossing / track / anything else (conservative: no).</summary>
            Other = 2,
            /// <summary>
            /// 0.4.12 (owner decision 17 Nov: "for this exception, it is allowed to block non-zebra and pavement pedestrian
            /// paths"): a pedestrian path that is NOT a zebra (<see cref="BlocksAsZebra"/> false): an unmarked crossing or a
            /// pavement / footway path. May lie under the footprint.
            /// </summary>
            Pavement = 3,
        }

        public const int PedCrosswalk = ZebraSections.PedestrianCrosswalk, PedUnsafe = ZebraSections.PedestrianUnsafe;   // Game.Net.PedestrianLaneFlags

        /// <summary>
        /// 0.4.12 (owner decision 17 Nov): a pedestrian path the same-exit-lane exception must never block = a ZEBRA: the game's
        /// marked crosswalk (PedestrianLaneFlags.Crosswalk without Unsafe, LaneSystem.cs 8571-8580), the same definition as the
        /// kept "cars never stop on a zebra" rule (ZebraSections.IsZebra), so the two rules always agree on what a zebra is.
        /// Unmarked crossings (Unsafe) and footway / pavement lanes (no Crosswalk flag, incl. a drive's sidewalk band) may be
        /// under the footprint. Open owner question (PROBE 22.9): a pedestrian-street (tiled) arm's crossing carries the marked
        /// flag although no stripes are drawn; it counts as a zebra here AND in the no-stop rule until the owner rules otherwise.
        /// </summary>
        public static bool BlocksAsZebra(int pedestrianFlags) => ZebraSections.IsZebra(pedestrianFlags);

        /// <summary>
        /// 0.4.12 OWNER DECISION REVISED (17 Nov, supersedes the zebra limit above for this exception): "it can block ANY pedestrian
        /// paths for the yielding exit at the box". Every pedestrian path (zebra, unmarked crossing, pavement) may lie under the
        /// footprint; only vehicle paths into other lanes disqualify. <see cref="BlocksAsZebra"/> now only feeds the counter
        /// "admitted under the exception with a tail on a zebra".
        /// </summary>
        public static Path PedestrianKind(int pedestrianFlags) => Path.Pavement;

        /// <summary>The footprint [from, to] admits the car: every overlap range [s, e] (metres) it touches is a SameExit path.</summary>
        public static bool Admits<T>(float from, float to, ref T paths) where T : struct, IFootprintPaths
        {
            for (int i = 0; i < paths.Count; i++)
            {
                if (!paths.Get(i, out float s, out float e, out Path kind)) continue;
                if (e > from && s < to && kind != Path.SameExit && kind != Path.Pavement) return false;
            }
            return true;
        }
    }

    /// <summary>The overlaps of the car's junction lane in metres along it with their kind (job: LaneOverlap buffer; tests: arrays).</summary>
    public interface IFootprintPaths
    {
        int Count { get; }
        /// <summary>false = not an overlap to judge (water).</summary>
        bool Get(int i, out float start, out float end, out SameExitException.Path kind);
    }
}
