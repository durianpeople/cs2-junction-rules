using System.Runtime.CompilerServices;

namespace JunctionRules.Core
{
    /// <summary>
    /// 0.4.11 (JR-T-017, G25, owner decision option (c)): when the game's own junction room hold (IterateNextLane ->
    /// CheckSpace) may be dropped. Stock cites (decompile 2026-11-13, CarLaneSpeedIterator): IterateNextLane ~429 / 473 /
    /// 480 stops the car at the entry of a junction lane (driveSpeed = 0, blocker type Continuing) when CheckSpace fails;
    /// CheckSpace (~642+) holds only when the junction lane has a true crossing overlap (no merge / unsafe / water flag) and
    /// the next lane lacks room for the car's length. Game-independent, unit-tested (G25Tests).
    /// </summary>
    public static class StockRoomSkip
    {
        public const uint TrafficLights = 0x8000000u, LevelCrossing = 0x40u, Roundabout = 0x400000u;
        public const int MergeFlags = 0x1 | 0x2 | 0x10 | 0x20, Unsafe = 0x40, Water = 0x800;

        /// <summary>
        /// The skip may apply: Whole junction rule on, the exit lookups present (the gap scan reads them), a junction lane,
        /// not a boxed junction (the box rules decide there), not a signalled / level-crossing / roundabout lane (stock).
        /// </summary>
        public static bool Applies(bool wholeJunctionRule, bool scanLookups, uint junctionLaneFlags, bool isJunctionLane, bool boxed) =>
            Applies(wholeJunctionRule, scanLookups, junctionLaneFlags, isJunctionLane, boxed, false);

        /// <summary>
        /// 0.4.12 (JR-T-018, G29, owner live 17 Nov: "this cop car is yielded but doesn't go in"): at a BOXED junction the stock
        /// hold is the same room test the box rule already makes, but without the owner's G22 same-exit-lane exception, so G22
        /// could never let anyone in (0.4.11 live: "entered a box under the same-exit-lane exception 0" all session). The skip
        /// now also applies at a boxed junction when the box rule admits this car under G22 (sameExitAdmits); the crossed lanes
        /// must still be clear (Skip). Signals / level crossings / roundabouts: stock, as G22 itself.
        /// </summary>
        public static bool Applies(bool wholeJunctionRule, bool scanLookups, uint junctionLaneFlags, bool isJunctionLane, bool boxed, bool sameExitAdmits) =>
            wholeJunctionRule && scanLookups && isJunctionLane && (!boxed || sameExitAdmits) && (junctionLaneFlags & (TrafficLights | LevelCrossing | Roundabout)) == 0;

        /// <summary>A lane the turner CROSSES: the overlaps CheckSpace counts as crossings (no merge / unsafe / water flag), a car lane.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCrossedLane(int overlapFlags, bool otherIsCarLane) =>
            otherIsCarLane && (overlapFlags & (MergeFlags | Unsafe | Water)) == 0;

        /// <summary>Review F2: a true crossing (no merge / unsafe / water) of a TRACK lane (OverlapFlags.Track 0x100): not judged by the gap scan, stock hold kept.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CrossesTrack(int overlapFlags) => (overlapFlags & (MergeFlags | Unsafe | Water)) == 0 && (overlapFlags & Track) != 0;

        public const int Track = 0x100;

        /// <summary>The whole decision: every crossed lane clear (and at least one crossing: else the stock hold had another reason).</summary>
        public static bool Skip(bool applies, int crossedLanes, bool anyBusy) => applies && crossedLanes > 0 && !anyBusy;
    }
}
