using System.Runtime.CompilerServices;

namespace JunctionRules.Core
{
    /// <summary>
    /// JR-T-033 item B1 (1.4.0, owner 20 Nov 2026: "The pavement, I don't think it should count like a zebra anymore in a sense
    /// that cars yield to pedestrians. Cars should have the right of way."): where a car's lane is a building access (a driveway
    /// or way in: <c>CarLaneFlags.SideConnection</c>, either direction), the pavement it crosses (unmarked: no Crosswalk flag;
    /// a marked zebra over an access stays a zebra, JR-T-035) is NOT a zebra:
    /// <list type="bullet">
    /// <item>no zebra section (0.4.8 G7 made a leaving driveway's pavement a hard zebra Z: people on it held the car before
    ///   it, and its room-past hold; the exit give-way's own hold before the pavement for ROAD traffic and its room on the
    ///   joined lane (S2-29) stay);</item>
    /// <item>no "wait at the spot" for people there (the whole-junction crossing rule leaves an access's pavement to the game's
    ///   own check at the crossing);</item>
    /// <item>the 0.3.2 "never push through people" full stop (no timeout) applies only to a person physically IN the car's way
    ///   (<see cref="PersonInWay"/>: inside the car's range on the pavement, shrunk by <see cref="InWayShrinkMetres"/>); a person
    ///   standing at its edge or beside it gets the game's own creep past (3 m/s), so the car moves off and the game's own
    ///   rule holds the person at the edge (people wait for a moving car whose reach covers the access). Before, G23's
    ///   0.5 m widening counted a person held at the edge as in the way: car and person waited for each other until the
    ///   people pass (2 s).</item>
    /// </list>
    /// Safety floor: the game's own pedestrian check still runs for everyone (it brakes for a walking person and creeps past a
    /// standing one), and a person in the car's way still gets the full stop. A look-past no-stop zone was never set on a
    /// pavement (only crosswalks count there), so nothing changes there. Unmarked side-road crossings (G11) are not touched.
    /// Pure functions (Burst-compatible).
    /// </summary>
    public static class AccessCrossing
    {
        /// <summary>CarLaneFlags.SideConnection (as ExitGiveWay / EntryGiveWay read it).</summary>
        public const uint SideConnection = ExitGiveWay.SideConnection;

        /// <summary>The car lane is a building access (driveway out or way in).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAccessLane(uint carLaneFlags) => (carLaneFlags & SideConnection) != 0;

        /// <summary>
        /// Metres taken off each side of the car's range on the pavement for "in the car's way": the game's own rule holds a
        /// person at the near edge of that range (CreatureTargetIterator.CheckOverlapLane); a person's body is about 0.3-0.5 m,
        /// and the range is the access lane's width (wider than the car's body), so a person held at the edge stands clear of
        /// the car's body.
        /// </summary>
        public const float InWayShrinkMetres = 0.25f;

        /// <summary>
        /// A person at <paramref name="personPos"/> (game offset on the pavement lane) is in the car's way: inside the car's range
        /// on it (rangeA / rangeB, game offsets, either order) shrunk by <see cref="InWayShrinkMetres"/> each side (a range
        /// narrower than twice that: its middle point).
        /// </summary>
        public static bool PersonInWay(float personPos, float rangeA, float rangeB, float laneLength)
        {
            float lo = (rangeA < rangeB ? rangeA : rangeB) * laneLength + InWayShrinkMetres;
            float hi = (rangeA < rangeB ? rangeB : rangeA) * laneLength - InWayShrinkMetres;
            if (hi < lo)
            {
                float mid = (lo + hi) * 0.5f;
                lo = mid;
                hi = mid;
            }
            float p = personPos * laneLength;
            return p >= lo && p <= hi;
        }

        /// <summary>
        /// The 0.3.2 full stop applies to this person: on an access only when in the car's way (<see cref="PersonInWay"/>);
        /// elsewhere G23's widened test (<see cref="CrossingPush.PersonInPath"/>), unchanged.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool FullStopApplies(bool accessLane, bool inPathG23, bool inWay) => accessLane ? inWay : inPathG23;

        /// <summary>
        /// JR-T-035 (review F3): the pedestrian overlap of an access lane is the unmarked pavement B1 exempts: no
        /// <c>PedestrianLaneFlags.Crosswalk</c> flag. A marked crosswalk an access lane crosses (side-connection lanes are also
        /// made at nodes, beside a junction mouth) stays a zebra: cars yield there as at any zebra.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AccessPavement(bool accessLane, int pedestrianLaneFlags) =>
            accessLane && (pedestrianLaneFlags & ZebraSections.PedestrianCrosswalk) == 0;

        /// <summary>
        /// The zebra-section kind of a pedestrian overlap of the car's lane: the unmarked pavement of an access is never a zebra
        /// (B1; G7 made a leaving driveway's pavement a hard zebra), anything else (a marked zebra over an access included) the
        /// given kind.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SectionKind(bool accessLane, int pedestrianLaneFlags, int kind) => AccessPavement(accessLane, pedestrianLaneFlags) ? 0 : kind;

        /// <summary>A crossing of the car's path is part of the whole-junction decision (its spot): never the unmarked pavement over an access.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CrossingInRule(bool accessLane, int pedestrianLaneFlags) => !AccessPavement(accessLane, pedestrianLaneFlags);
    }
}
