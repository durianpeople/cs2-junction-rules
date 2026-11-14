using System.Runtime.CompilerServices;

namespace JunctionRules.Core
{
    /// <summary>
    /// 0.2.4 (JR-T-002h second addition, owner ruling 14 Nov 2026: "the turning-right car should not have right of
    /// way"): setting "Turning across oncoming traffic gives way" (default on, whole junction only). Between two junction
    /// lanes with EQUAL sign status (no Stop / Yield / RightOfWay / Unsafe difference, no signal or yield override for
    /// this overlap) where one is a turn across the oncoming flow and the other is that oncoming straight, the turner
    /// has the lower priority (overlap priority delta +1 for the turner, -1 for the straight), whatever the game's
    /// geometric tie-break (LaneOverlapSystem) said; and the straight car does not give way to the turner's lane
    /// reservation alone (a turner that has to give way does not hold the straight car by reserving; a turner physically
    /// in the way still does, stock collision braking).
    ///
    /// Turn across oncoming: left-hand traffic TurnRight / GentleTurnRight / UTurnRight, right-hand traffic the left
    /// flags (the game's own CityConfigurationSystem.leftHandTraffic, as LaneOverlapSystem's tie-break uses).
    /// Oncoming straight (from stock data, no angles): a Forward lane whose END edge is the turner's START edge (a node
    /// lane runs from PathNode(source edge, ...) to PathNode(target edge, ...); PathNode.OwnerEquals compares the edge).
    /// Not classified (stock decides): two turners, oncoming turns (in LHT an oncoming left turn shares the turner's
    /// target: a merge), lanes from the same or a third edge, chained lanes inside a node (start or end owned by the node).
    /// Pure logic, Burst-compatible.
    /// </summary>
    public static class TurnAcross
    {
        // Game.Net.CarLaneFlags (uint), mirrored; checked against the decompile in TurnAcrossRepoTests.
        public const uint Unsafe = 0x1u, UTurnLeft = 0x2u, TurnLeft = 0x10u, TurnRight = 0x20u, Yield = 0x400u, Stop = 0x800u,
            UTurnRight = 0x20000u, GentleTurnLeft = 0x40000u, GentleTurnRight = 0x80000u, Forward = 0x100000u,
            Approach = 0x200000u, RightOfWay = 0x4000000u;

        /// <summary>The flags the stock priority delta looks at before its geometric tie-break (LaneOverlapSystem).</summary>
        public const uint SignFlags = Stop | Yield | RightOfWay | Unsafe;

        /// <summary>Turn flags that cross the oncoming flow for the traffic side.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint AcrossFlags(bool leftHandTraffic) =>
            leftHandTraffic ? TurnRight | GentleTurnRight | UTurnRight : TurnLeft | GentleTurnLeft | UTurnLeft;

        /// <summary>Stock: a lane with Approach has its Stop / Yield / RightOfWay cleared before the comparison.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint SignStatus(uint flags) => (flags & Approach) != 0 ? flags & Unsafe : flags & SignFlags;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsTurnAcross(uint flags, bool leftHandTraffic) => (flags & AcrossFlags(leftHandTraffic)) != 0;

        /// <summary>
        /// The overlap priority delta this rule sets for the OWN lane (+1 = own car gives way, -1 = other gives way), or
        /// 0 when the rule does not classify the pair (stock delta kept). turnerStartEqualsStraightEnd is evaluated for
        /// the pair in the right order by the caller: ownStartIsOtherEnd = own start edge == other end edge;
        /// otherStartIsOwnEnd = other start edge == own end edge. signalOverride = the stock num8 (sign / signal
        /// override for this overlap): any override keeps stock.
        /// </summary>
        public static int Delta(uint ownFlags, uint otherFlags, bool ownStartIsOtherEnd, bool otherStartIsOwnEnd, int signalOverride, bool leftHandTraffic)
        {
            if (signalOverride != 0) return 0;
            if (SignStatus(ownFlags) != SignStatus(otherFlags)) return 0;
            bool ownTurn = IsTurnAcross(ownFlags, leftHandTraffic), otherTurn = IsTurnAcross(otherFlags, leftHandTraffic);
            if (ownTurn == otherTurn) return 0;
            if (ownTurn) return (otherFlags & Forward) != 0 && ownStartIsOtherEnd ? 1 : 0;
            return (ownFlags & Forward) != 0 && otherStartIsOwnEnd ? -1 : 0;
        }

        /// <summary>
        /// num6 after the rule: stock did num6 -= num9 (num9 = overlap delta, zeroed for a space request when positive);
        /// the rule replaces num9 by its own delta with the same space-request zeroing. delta 0 = unchanged.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ApplyNum6(int num6, int num9, int delta, bool requestSpace)
        {
            if (delta == 0) return num6;
            int d = requestSpace && delta > 0 ? 0 : delta;
            return num6 + num9 - d;
        }

        /// <summary>
        /// The straight car (delta -1) does not give way to the turner's lane reservation alone: the stock test is
        /// offset &gt; num10 || priority2 &gt; num6; the reservation is discounted when its priority does not beat num6
        /// (an emergency turner, 108, still holds it).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool DiscountReservation(int delta, int reservationPriority, int num6) => delta < 0 && reservationPriority <= num6;
    }
}
