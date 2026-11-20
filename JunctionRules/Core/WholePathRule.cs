using System.Runtime.CompilerServices;

namespace JunctionRules.Core
{
    /// <summary>The "Junction gap rule" setting (stage 2).</summary>
    public enum JunctionGapRule
    {
        /// <summary>The game's own rule: every conflict on the path through a junction is judged on its own (default).</summary>
        Stock = 0,
        /// <summary>One decision for the whole path, taken at the waiting spot (owner rulings 14 Nov 2026).</summary>
        WholeJunction = 1,
    }

    /// <summary>
    /// Where a car that must wait under the whole-junction rule stands (owner ruling 14 Nov 2026: the default is just
    /// before the first path crossing; a "wait at the line" variant is allowed but must not be the default).
    /// </summary>
    public enum JunctionWaitingSpot
    {
        /// <summary>Just before the first point where the path crosses another live lane's path (default).</summary>
        BeforeFirstCrossing = 0,
        /// <summary>At the stop line (the start of the junction lane), for every junction.</summary>
        StopLine = 1,
    }

    /// <summary>
    /// Bits of the per-step stage 2 mode handed into the navigation job. 0 = stage 2 code does nothing (the hot path
    /// only tests this int).
    /// </summary>
    public static class Stage2Mode
    {
        /// <summary>The whole-junction rule changes driving.</summary>
        public const int Rule = 1;
        /// <summary>Count debug events for the timing log (no driving change on its own).</summary>
        public const int Count = 2;
        /// <summary>With <see cref="Rule"/>: wait at the stop line instead of before the first crossing.</summary>
        public const int AtLine = 4;
        /// <summary>0.2.4, with <see cref="Rule"/> only: turning across oncoming traffic gives way (Core/TurnAcross.cs).</summary>
        public const int TurnGivesWay = 8;
        /// <summary>0.2.4, with <see cref="TurnGivesWay"/> only: the city drives on the left (CityConfigurationSystem.leftHandTraffic).</summary>
        public const int LeftHandTraffic = 16;
        /// <summary>
        /// 0.3.2 (JR-T-004), independent of the rule: cars never push through people on crossings (Core/CrossingPush.cs).
        /// The job side strips this bit from the stage 2 mode (JRWholeJunction.Create), so every "m_Mode == 0" early exit of
        /// the 0.2.x code stays as it was; only the 0.3.2 lines in blocks S2-08 / S2-08b / S2-11 / S2-11b read it.
        /// </summary>
        public const int NoPushPeople = 32;
        /// <summary>
        /// 0.4.1 (JR-T-009), independent of the rule: cars never stop on a zebra (Core/ZebraSections.cs). Stripped from the
        /// job's stage 2 mode like <see cref="NoPushPeople"/>, together with the room margin in <see cref="ZebraMarginMask"/>.
        /// </summary>
        public const int ZebraClear = 64;
        /// <summary>0.4.1: bits 8-15 = the room margin past a zebra in decimetres (0-25.5 m; the setting allows 0-10 m).</summary>
        public const int ZebraMarginShift = 8;
        public const int ZebraMarginMask = 0xFF << ZebraMarginShift;
        /// <summary>
        /// 0.4.2 (JR-T-010): setting "Cars obey yellow boxes" (Core/BoxDriving.cs), independent of the gap rule; stripped
        /// from the job's stage 2 mode like <see cref="ZebraClear"/>. The room margin (bits 8-15) is shared with the zebra
        /// rule and carried when either setting is on.
        /// </summary>
        public const int BoxObey = 1 << 16;
        // Bits 17 / 18 retired in 1.5.0 (JR-T-038): BoxTurnWait / BoxLeftHand, the UK right-turn spot in a box (removed).
        /// <summary>0.4.2, with <see cref="BoxObey"/> only: at least one junction box exists (else the job skips the junction test).</summary>
        public const int BoxJunctions = 1 << 19;
        /// <summary>0.4.2, with <see cref="BoxObey"/> only: at least one entrance box exists (else the job skips the entrance test).</summary>
        public const int BoxEntrances = 1 << 20;
        /// <summary>0.4.2: bits 21-29 = the entrance band length in decimetres (the painted length, BoxStore.EntranceLength; 0-51.1 m).</summary>
        public const int BoxEntranceShift = 21;
        public const int BoxEntranceMask = 0x1FF << BoxEntranceShift;
        /// <summary>
        /// 0.4.4 (JR-T-012): setting "Vehicles leaving buildings give way" (Core/ExitGiveWay.cs), independent of the gap
        /// rule; stripped from the job's stage 2 mode like <see cref="BoxObey"/>.
        /// </summary>
        public const int ExitGiveWay = 1 << 30;
        // JR-T-014d (G11) BEGIN
        /// <summary>
        /// JR-T-014d (G11, owner rule 17 Nov 2026): setting "People give way at unmarked crossings" (Core/UnmarkedCrossing.cs),
        /// independent of the gap rule; stripped from the job's stage 2 mode like <see cref="ExitGiveWay"/>. Bit 7 (the last free
        /// bit below the zebra margin).
        /// </summary>
        public const int UnmarkedGiveWay = 1 << 7;
        // JR-T-014d (G11) END
        /// <summary>
        /// JR-T-022, with <see cref="BoxObey"/> only: setting "Look past the car in front at a box" (RoomScan.LookPast, default
        /// on). Bit 31, the last free bit (the int's sign bit; the handed-over mode is only ever tested bit by bit).
        /// </summary>
        public const int BoxLookPast = 1 << 31;
        /// <summary>Every bit the job side strips from the stage 2 mode (settings that are not part of the 0.2.x rule).</summary>
        public const int Stripped = NoPushPeople | ZebraClear | ZebraMarginMask | BoxObey | BoxJunctions | BoxEntrances | BoxEntranceMask
            | ExitGiveWay | UnmarkedGiveWay | BoxLookPast;

        /// <summary>0.4.2: the room margin bits alone (shared by the zebra and the yellow box rules).</summary>
        public static int MarginBits(float marginMetres)
        {
            int dm = (int)System.Math.Round((marginMetres > 0f ? marginMetres : 0f) * 10f);
            if (dm > 255) dm = 255;
            return dm << ZebraMarginShift;
        }

        /// <summary>
        /// 0.4.2: the yellow box bits for the settings (0 when "Cars obey yellow boxes" is off). anyJunction / anyEntrance =
        /// boxes of that kind exist in the city; entranceLength = the band length in metres.
        /// </summary>
        public static int BoxBits(bool obey, bool anyJunction, bool anyEntrance, float entranceLength, bool lookPast = false)
        {
            if (!obey) return 0;
            int dm = (int)System.Math.Round((entranceLength > 0f ? entranceLength : 0f) * 10f);
            if (dm > 0x1FF) dm = 0x1FF;
            return BoxObey | (anyJunction ? BoxJunctions : 0)
                | (anyEntrance ? BoxEntrances : 0) | (dm << BoxEntranceShift)
                // JR-T-022: look past the car in front at a box
                | (lookPast ? BoxLookPast : 0);
        }

        /// <summary>0.4.2: the entrance band length (m) carried in a mode.</summary>
        public static float BoxEntranceLength(int mode) => ((mode & BoxEntranceMask) >> BoxEntranceShift) * 0.1f;

        /// <summary>0.4.1: the zebra bits for the setting (margin rounded to 0.1 m, clamped to 0-25.5 m).</summary>
        public static int ZebraBits(bool on, float marginMetres) => on ? ZebraClear | MarginBits(marginMetres) : 0;

        /// <summary>0.4.1: the room margin (m) carried in a mode.</summary>
        public static float ZebraMargin(int mode) => ((mode & ZebraMarginMask) >> ZebraMarginShift) * 0.1f;

        /// <summary>
        /// The mode bits. The 0.2.4 turn bits are set only with the rule on, so the rule-off mode (and with it every
        /// "m_Mode == 0" early exit of the job) is unchanged. 0.3.2: <see cref="NoPushPeople"/> with either rule.
        /// </summary>
        public static int For(JunctionGapRule rule, bool counting, JunctionWaitingSpot spot = JunctionWaitingSpot.BeforeFirstCrossing,
            bool turnGivesWay = false, bool leftHandTraffic = false, bool noPushPeople = false, bool zebraClear = false,
            float zebraMargin = RoomScan.DefaultMargin, int boxBits = 0, bool exitGiveWay = false, bool unmarkedGiveWay = false) =>
            (rule == JunctionGapRule.WholeJunction
                ? Rule | (spot == JunctionWaitingSpot.StopLine ? AtLine : 0) | (turnGivesWay ? TurnGivesWay | (leftHandTraffic ? LeftHandTraffic : 0) : 0)
                : 0) | (counting ? Count : 0) | (noPushPeople ? NoPushPeople : 0) | ZebraBits(zebraClear, zebraMargin)
            // 0.4.2: the yellow box bits (BoxBits); the shared room margin also when only the box rule is on.
            | ((boxBits & BoxObey) != 0 ? boxBits | MarginBits(zebraMargin) : 0)
            // 0.4.4: vehicles leaving buildings give way.
            | (exitGiveWay ? ExitGiveWay : 0)
            // JR-T-014d (G11): people give way at unmarked crossings.
            | (unmarkedGiveWay ? UnmarkedGiveWay : 0);

        public static string RuleName(int mode) =>
            ((mode & Rule) == 0 ? "stock"
            : ((mode & AtLine) != 0 ? "whole junction (wait at the stop line)" : "whole junction")
              + ((mode & TurnGivesWay) != 0 ? ", turning across oncoming gives way (" + ((mode & LeftHandTraffic) != 0 ? "left" : "right") + "-hand traffic)" : ""))
            + ((mode & NoPushPeople) != 0 ? "; cars never push through people on crossings" : "")
            + ((mode & ZebraClear) != 0 ? "; cars never stop on a zebra (room margin " + ZebraMargin(mode).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m)" : "")
            + ((mode & BoxObey) != 0 ? "; cars obey yellow boxes (room margin " + ZebraMargin(mode).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                + " m, exit boxes driven over their painted windows (box length setting " + BoxEntranceLength(mode).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m); "
                + "past the line as any junction"
                + ((mode & BoxLookPast) != 0 ? ", looking past the car in front" : "")
                + ")" : "")
            + ((mode & ExitGiveWay) != 0 ? "; vehicles leaving buildings give way" : "")
            + ((mode & UnmarkedGiveWay) != 0 ? "; people give way at unmarked crossings" : "");
    }

    /// <summary>
    /// The whole-junction rule as plain arithmetic, game-independent so it can be unit-tested and called from Burst code
    /// (static methods on plain values only). The job-side caller (Stage2/JRWholeJunction.cs) feeds it the game's own
    /// numbers: the lane overlap ranges, the iterator's distances and the game's own braking distance.
    ///
    /// Terms (DEVIATIONS.md S2, docs/STAGE2-ANALYSIS.md):
    /// - conflict: a lane overlap of a junction (node) lane that the game itself gives the yield test, i.e. not a
    ///   diverging sibling from the same start (vanilla skips those: (flags &amp; MergeStart|MergeMiddleStart) == 0, or the
    ///   End flags when driving the lane inverted) and not water.
    /// - waiting spot: the last point of the path that is not inside any other lane's path = the start of the first
    ///   conflict with a road or track lane. A road lane held by a red signal right now is not a live path and does not
    ///   place the waiting spot (owner ruling for turns across oncoming on green); a lane on amber (SafeStop) IS live
    ///   (owner ruling 14 Nov 2026: only red does not count); a track lane always does.
    ///   Pedestrian crossings are judged in the decision but never place the waiting spot.
    /// - committed: the car's own front is past its waiting spot (by the game's stop margin), i.e. it has left the spot
    ///   and is inside the junction's paths. Owner ruling 14 Nov 2026 (decide only at the spot): nothing else commits a
    ///   car: not the braking-distance test, not a lane reservation, not a look-ahead lane, not a leader's go. Until its
    ///   front reaches the spot every give-way of the path stops it at the spot, so the decision that lets it pass is the
    ///   one judged at the spot. A follower queued behind a leader is behind the spot, so it cannot inherit the leader's
    ///   decision; it rolls up to the spot and decides for itself.
    /// </summary>
    public static class WholePathRule
    {
        // Game.Net.OverlapFlags values (mirrored so this file stays game-independent; checked by a test against the
        // decompile).
        public const int MergeStart = 0x1;
        public const int MergeEnd = 0x2;
        public const int MergeMiddleStart = 0x10;
        public const int MergeMiddleEnd = 0x20;
        public const int Road = 0x80;
        public const int Track = 0x100;
        public const int Water = 0x800;

        // Game.Net.LaneSignalType values (mirrored, checked by a test against the decompile).
        public const int SignalStop = 1;
        public const int SignalSafeStop = 2;

        /// <summary>
        /// The other lane is held by red right now: only LaneSignalType.Stop. Amber (SafeStop) counts as live, both for
        /// the waiting spot and in the decision (owner ruling 14 Nov 2026).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsHeldByRed(int signal) => signal == SignalStop;

        /// <summary>LaneOverlap bytes to curve offsets, the game's own factor (1/255 as a float literal).</summary>
        public const float ByteToOffset = 0.003921569f;

        /// <summary>Conflicts (and pedestrian crossings) at or after the spot are judged together within this offset tolerance.</summary>
        public const float SpotEpsilon = 1e-4f;

        /// <summary>
        /// True when the game gives this overlap its yield test (reservations, priority look-ahead of approaching
        /// cars): vanilla CheckOverlappingLanes' "(laneOverlap.m_Flags &amp; overlapFlags) == 0".
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsYieldChecked(int flags, bool inverse) =>
            (flags & (inverse ? (MergeEnd | MergeMiddleEnd) : (MergeStart | MergeMiddleStart))) == 0;

        /// <summary>Whether this overlap places the waiting spot (see the class summary).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool DefinesWaitingSpot(int flags, bool inverse, bool otherHeldByRed)
        {
            if ((flags & Water) != 0 || !IsYieldChecked(flags, inverse)) return false;
            if ((flags & Track) != 0) return true;
            return (flags & Road) != 0 && !otherHeldByRed;
        }

        /// <summary>Start of an overlap as seen by the car: the lower end, or the upper end on an inverted lane.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float StartFromCar(float thisStart, float thisEnd, bool inverse) => inverse ? thisEnd : thisStart;

        /// <summary>The nearer of two starts, for building the waiting spot over all conflicts of a lane.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Nearer(float a, float b, bool inverse) => inverse ? (a > b ? a : b) : (a < b ? a : b);

        /// <summary>Initial value for <see cref="Nearer"/> (beyond every real offset in driving direction).</summary>
        public static float NoSpot(bool inverse) => inverse ? -1f : 2f;

        public static bool HasSpot(float spot, bool inverse) => inverse ? spot >= 0f : spot <= 1f;

        /// <summary>The "wait at the stop line" variant moves a found spot to the start of the junction lane.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ApplyVariant(float spot, bool inverse, bool atLine) =>
            atLine && HasSpot(spot, inverse) ? (inverse ? 1f : 0f) : spot;

        /// <summary>
        /// One step of the waiting-spot search over a lane's overlaps (LaneOverlap bytes as stored by the game):
        /// returns the spot after taking this overlap into account. Start from <see cref="NoSpot"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float AddConflict(float spot, int flags, int thisStart, int thisEnd, bool inverse, bool otherHeldByRed)
        {
            if (!DefinesWaitingSpot(flags, inverse, otherHeldByRed)) return spot;
            return Nearer(spot, StartFromCar(thisStart * ByteToOffset, thisEnd * ByteToOffset, inverse), inverse);
        }

        /// <summary>
        /// Distance from the car to the waiting spot, with the same formula the game uses for the distance to an
        /// overlap (CheckOverlappingLanes: num5 = origDistance + length * (start - curveOffset.x)).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float WaitDistance(float origDistance, float length, float spot, float curveX, bool inverse) =>
            origDistance + length * (inverse ? curveX - spot : spot - curveX);

        /// <summary>
        /// The car has committed to the junction: its front is past the waiting spot (owner ruling 14 Nov 2026: the
        /// decision is taken only at the spot). A car standing at the spot (front at the game's stop point, a margin
        /// before it) or anywhere behind it is not committed, whatever its speed, reservation or leader. On a look-ahead
        /// lane the wait distance is always positive, so a car is never committed to a lane it has not reached.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCommitted(float waitDistance, float margin) => FrontPastSpot(waitDistance, margin);

        /// <summary>The car's front is past the waiting spot by more than the game's margin (it is inside a path).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool FrontPastSpot(float waitDistance, float margin) => waitDistance + margin < 0f;

        /// <summary>The overlap starts at or after the waiting spot (it is part of the whole-path decision).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AtOrAfterSpot(float start, float spot, bool inverse) =>
            inverse ? start <= spot + SpotEpsilon : start >= spot - SpotEpsilon;

        /// <summary>The overlap starts strictly after the waiting spot (a later conflict, not the first).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AfterSpot(float start, float spot, bool inverse) =>
            inverse ? start < spot - SpotEpsilon : start > spot + SpotEpsilon;

        /// <summary>What the rule does with one stock yield (a braking demand from a conflict).</summary>
        public enum YieldAction
        {
            /// <summary>Leave the game's value (rule off, not a junction lane, or the conflict is before the spot).</summary>
            Stock = 0,
            /// <summary>Not committed: stop at the waiting spot instead of at this conflict.</summary>
            StopAtSpot = 1,
            /// <summary>Committed: no more give-way stops inside the junction (collision braking stays).</summary>
            Suppress = 2,
        }

        /// <summary>
        /// A give-way demand of the stock test (a reservation that blocks, a car asking for space, an approaching car
        /// that counts only thanks to the game's priority look-ahead).
        /// </summary>
        public static YieldAction ForYield(bool ruleOn, bool inScope, bool committed)
        {
            if (!ruleOn || !inScope) return YieldAction.Stock;
            return committed ? YieldAction.Suppress : YieldAction.StopAtSpot;
        }

        /// <summary>
        /// A car (or bicycle) on a conflicting lane that the game brakes for. Projected = it counts only because the
        /// game projected a higher-priority approaching car two seconds ahead (a give-way). Physical = it is in or past
        /// the conflict area. Merge = the overlap is where both lanes run into the same exit (following, not crossing).
        /// Before committing: a projected car, or a physical car on a crossing, keeps the car at the waiting spot; a
        /// physical car on the merge is followed as the game does (an exit queue is the stage 3 yellow-box question).
        /// After committing: projected cars are ignored (see <see cref="UndoProjection"/>), physical cars stay braked for.
        /// </summary>
        public static YieldAction ForVehicle(bool ruleOn, bool inScope, bool committed, bool projected, bool merge)
        {
            if (!ruleOn || !inScope) return YieldAction.Stock;
            if (committed) return projected ? YieldAction.Suppress : YieldAction.Stock;
            return projected || !merge ? YieldAction.StopAtSpot : YieldAction.Stock;
        }

        /// <summary>
        /// Pedestrians on a crossing of the path: before committing, wait at the spot; after committing the game's own
        /// pedestrian braking stays (it brakes only for people at the crossing line).
        /// </summary>
        public static YieldAction ForPedestrian(bool ruleOn, bool inScope, bool committed)
        {
            if (!ruleOn || !inScope || committed) return YieldAction.Stock;
            return YieldAction.StopAtSpot;
        }

        /// <summary>
        /// After commit the game's priority look-ahead (+2 s for a higher-priority approaching car) is undone unless the
        /// car is already past the start of the overlap on its own lane: exactly what the game itself does for an
        /// overlap it considers committed.
        /// </summary>
        public static bool UndoProjection(bool ruleOn, bool inScope, bool committed, bool objectPastStart) =>
            ruleOn && inScope && committed && !objectPastStart;

        /// <summary>The distance the stock braking is computed for under <paramref name="action"/>.</summary>
        public static float Distance(YieldAction action, float stockDistance, float waitDistance) =>
            action == YieldAction.StopAtSpot ? waitDistance : stockDistance;
    }
}
