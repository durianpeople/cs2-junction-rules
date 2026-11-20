using System.Runtime.CompilerServices;

namespace JunctionRules.Core
{
    /// <summary>
    /// JR-T-033 (1.4.0, owner rule 20 Nov 2026, BINDING): a car whose only reason to hold is people on a zebra of its path
    /// waits RIGHT BEFORE that zebra (the game's own pedestrian stop: front a stop margin before it), not back at the
    /// waiting spot of its zebra section before the junction.
    ///
    /// Where it applies: the Whole junction gap rule with "Cars never stop on a zebra" (the zebra sections). With the Stock
    /// gap rule there is no safeguard (no whole-junction gap check over the window), so the car waits at its spot as in
    /// 1.3.0 (<see cref="Reason.StockGapRule"/>, JR-T-035). A zebra whose
    /// section starts at the zebra itself is already judged right before it (nothing changes). A zebra that was merged into
    /// an earlier section (its spot lies before it, typically the entry spot W before the first conflict) because the whole
    /// vehicle does not fit between the last VEHICLE point behind it and the zebra: that zebra's people held the car at the
    /// earlier spot until 1.3.0. Owner: the car may wait with its rear still over a conflict lane it has already crossed
    /// (real turner behaviour; a bus turning across oncoming traffic), so it drives up to the zebra, unless:
    /// <list type="number">
    /// <item>exit blocked: no room for the whole vehicle plus the room margin past the zebra's section (the room past
    ///   the exit, later zebras included), free now (no credit for a moving queue: the car will stand before the zebra,
    ///   so the room must already be there; the same rule as the box exit zebra wait, <see cref="ExitZebraWait"/>):
    ///   <see cref="Reason.ExitBlocked"/>;</item>
    /// <item>something before the zebra is not clear: the whole-junction gap check fails, a red light or a give-way /
    ///   STOP not cleared. Those holds are the car's own (they stop it at its spot or line whatever the people do), so
    ///   the drive-up only changes the people's distance; SAFEGUARD: while a drive-up is pending, the gap check of every
    ///   vehicle point of the zebra's section is judged over the time the car will spend there (<see cref="Window"/>:
    ///   driving up, the people's estimated wait <see cref="WaitSeconds"/>, and the rear clearing the points after it),
    ///   not only the drive-through time. A conflicting vehicle that can reach those points within that window holds the
    ///   car at its spot (<see cref="Reason.Traffic"/>). Vehicles that arrive after it has driven in wait behind it (the
    ///   game's own conflict checks: the car is physically there);</item>
    /// <item>JR-T-036 (1.4.1, owner ruling 20 Nov 2026, BINDING): a person STANDING STILL on the target zebra (or a later zebra
    ///   of its section, which the car would wait for there too) counts as exit blocked, as in the yellow box rule: the car
    ///   waits at its spot before the junction (<see cref="Reason.PeopleStanding"/>). Only people MOVING across make the car
    ///   drive up. The test is the box exit zebra wait's own (<see cref="ExitZebraWait.Person"/>: the game's walking test,
    ///   a standing person only on or beside the car's way, the G23 waiting area excluded); one standing person is enough
    ///   (mixed standing + walking = the spot);</item>
    /// <item>the junction is a painted yellow box (the stop point before the zebra lies inside the box): the existing box
    ///   rules decide, unchanged (JR-T-024 exit zebra wait): <see cref="Reason.Box"/>.</item>
    /// </list>
    /// Not exceptions: the rear over an already-crossed conflict lane (bus length). Still kept: never a tail on another
    /// zebra (the owner's "Cars never stop on a zebra": the whole vehicle must fit between the end of the zebras behind
    /// the target and its stop point, <see cref="FitsClearOfZebras"/>, else <see cref="Reason.TailOnZebra"/>); the car
    /// must be able to stop before the zebra in normal braking (the game's can-stop test, else
    /// <see cref="Reason.CannotStop"/>). Two zebras: the car waits before the FIRST zebra with people (later zebras then
    /// get the game's own distance too: they lie beyond, so the first one binds); later zebras only matter for the room
    /// past the exit. Soft zebras (signalised, people not on green) are not zebras here (unchanged). People standing in
    /// the waiting area at a lane end never hold a car (G23, as before). Never pushing through people stays.
    ///
    /// Pure arithmetic on plain values (Burst-compatible, no allocation, no managed types).
    /// </summary>
    public static class ZebraDriveUp
    {
        /// <summary>
        /// Why people on a zebra hold a car at its spot rather than right before the zebra (the extra of the why-slowed id
        /// <c>jr_people_wait_spot</c>; values stable). None = the drive-up applies (<c>jr_people_wait_zebra</c>).
        /// </summary>
        public enum Reason : byte
        {
            /// <summary>No reason: the car drives up and waits right before the zebra.</summary>
            None = 0,
            /// <summary>Something before the zebra is not clear: a conflicting vehicle can reach the car's conflict points within the wait window.</summary>
            Traffic = 1,
            /// <summary>No room for the whole vehicle (plus the margin) past the zebra's section, free now.</summary>
            ExitBlocked = 2,
            /// <summary>A yellow box junction: the box rules decide (JR-T-024).</summary>
            Box = 3,
            /// <summary>Waiting before the zebra would leave a tail on another zebra behind it.</summary>
            TailOnZebra = 4,
            /// <summary>The car cannot stop before the zebra in normal braking any more.</summary>
            CannotStop = 5,
            /// <summary>The crossing is not one of the zebra sections (not a zebra, a soft zebra, or "Cars never stop on a zebra" off): the 0.3.3 crossing rule.</summary>
            NotZebraSection = 6,
            /// <summary>JR-T-035 (review F1): the Stock junction gap rule. The drive-up needs the Whole junction rule (its safeguard
            /// is the whole-junction gap check over the <see cref="Window"/>); with the Stock rule the car waits at its spot as in 1.3.0.</summary>
            StockGapRule = 7,
            /// <summary>JR-T-036 (1.4.1, owner 20 Nov 2026): somebody stands still on the target zebra (or a later zebra of its section):
            /// exit blocked, as the yellow box rule (<see cref="ExitZebraWait.Stationary"/>); the car waits at its spot.</summary>
            PeopleStanding = 8,
        }

        /// <summary>
        /// Upper bound (s) of the estimated wait for the people on the zebra. A person standing on the car's way has no
        /// predictable end: the bound (JR-T-036: a standing person no longer allows a drive-up, so in the drive-up window the
        /// bound only caps slow or long walks). 10 s covers a walker crossing a two-lane zebra end to end (about 10-12 m at the
        /// game's walking pace of about 1.3-1.5 m/s: 7-9 s) with margin, and stays inside the arrival-time horizon
        /// (<see cref="ArrivalTime.Horizon"/>: 17 s at the navigation step).
        /// </summary>
        public const float MaxWaitSeconds = 10f;

        /// <summary>A walker's speed along the zebra is taken as at least this (m/s), so a person walking slowly or across the zebra's line still gives a bounded time.</summary>
        public const float MinWalkSpeed = 0.5f;

        /// <summary>
        /// The car's way over the zebra on the pedestrian lane, widened (m) each side: G23's in-path tolerance plus the box
        /// exit zebra's beside allowance (<see cref="ExitZebraWait.BesideMetres"/>): the game's CheckPedestrian still counts a
        /// person that close to the car's way.
        /// </summary>
        public static float WayMarginMetres => CrossingPush.InPathToleranceMetres + ExitZebraWait.BesideMetres;

        /// <summary>
        /// Seconds until one person on the zebra has left the car's way (rangeA / rangeB: the car's way on the pedestrian
        /// lane in its offsets; personPos: the person's offset; laneLength in m; alongSpeed: the person's speed along the
        /// lane in m/s, signed: + = towards offset 1). Standing on the car's way (or beside it): <see cref="MaxWaitSeconds"/>.
        /// Walking: the distance to the far edge of the widened way in the walking direction over the speed (at least
        /// <see cref="MinWalkSpeed"/>), at most <see cref="MaxWaitSeconds"/>; 0 when already past it.
        /// </summary>
        public static float PersonSeconds(bool standing, float personPos, float rangeA, float rangeB, float laneLength, float alongSpeed)
        {
            if (standing) return MaxWaitSeconds;
            float lo = (rangeA < rangeB ? rangeA : rangeB) * laneLength - WayMarginMetres;
            float hi = (rangeA < rangeB ? rangeB : rangeA) * laneLength + WayMarginMetres;
            float p = personPos * laneLength;
            float metres = alongSpeed >= 0f ? hi - p : p - lo;
            if (!(metres > 0f)) return 0f;
            float v = alongSpeed >= 0f ? alongSpeed : -alongSpeed;
            if (v < MinWalkSpeed) v = MinWalkSpeed;
            float t = metres / v;
            return t < MaxWaitSeconds ? t : MaxWaitSeconds;
        }

        /// <summary>The estimated wait: the slowest person (worst first); call with 0 for the first person.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float WaitSeconds(float soFar, float person) => person > soFar ? person : soFar;

        /// <summary>
        /// The arrival time the gap check of one vehicle point of the zebra's section is judged with while a drive-up is
        /// pending (replaces the drive-through arrival time <paramref name="deltaPoint"/>): the car reaches the point, or
        /// the zebra's stop point if that is later (<paramref name="deltaZebra"/>), then waits <paramref name="wait"/> s for
        /// the people and needs <paramref name="clear"/> s from standstill to move its whole length past what it stands
        /// over. A conflicting vehicle that can reach the point within this time holds the car at its spot.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Window(float deltaPoint, float deltaZebra, float wait, float clear) =>
            (deltaPoint > deltaZebra ? deltaPoint : deltaZebra) + (wait > 0f ? wait : 0f) + (clear > 0f ? clear : 0f);

        /// <summary>
        /// The zebra at <paramref name="zebraStart"/> (driving coordinates) is decided at an earlier spot of its section
        /// (<paramref name="sectionSpot"/>): a drive-up candidate. A zebra that starts its own section is already judged
        /// right before it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool MergedZebra(float zebraStart, float sectionSpot) => zebraStart > sectionSpot + WholePathRule.SpotEpsilon;

        /// <summary>
        /// The whole vehicle fits between the end of the zebras behind the target (<paramref name="previousZebraEnd"/>,
        /// driving coordinates; <paramref name="hasZebraBehind"/> false = none on this lane: the rear may reach back over the
        /// line onto the car's own approach) and the game's stop point a margin before the zebra (CrossingPoint.Fits
        /// arithmetic). Vehicle points behind it do not count (owner: rear over a crossed lane is allowed).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool FitsClearOfZebras(bool hasZebraBehind, float laneLength, float zebraStart, float previousZebraEnd, float margin, float vehicleLength) =>
            !hasZebraBehind || CrossingPoint.Fits(laneLength, zebraStart, previousZebraEnd, margin, vehicleLength);

        /// <summary>
        /// The drive-up decision for the first zebra with people of a section the car still decides at its spot, in the
        /// order the exceptions are named: the gap rule (JR-T-035: only the Whole junction rule has the safeguard, else
        /// <see cref="Reason.StockGapRule"/>), box, somebody standing on the zebra (JR-T-036, <see cref="Reason.PeopleStanding"/>),
        /// then the geometry (tail on a zebra, cannot stop), then the exit room.
        /// The traffic exception is not part of this verdict: it is the car's own gap check over the <see cref="Window"/>
        /// (it holds the car at the spot by itself, whatever the people distance is).
        /// </summary>
        public static Reason Eligible(bool wholeJunction, bool boxLane, bool peopleStanding, bool fitsClearOfZebras, bool canStopBefore, bool roomPastFreeNow)
        {
            if (!wholeJunction) return Reason.StockGapRule;
            if (boxLane) return Reason.Box;
            if (peopleStanding) return Reason.PeopleStanding;
            if (!fitsClearOfZebras) return Reason.TailOnZebra;
            if (!canStopBefore) return Reason.CannotStop;
            if (!roomPastFreeNow) return Reason.ExitBlocked;
            return Reason.None;
        }

        /// <summary><see cref="Eligible(bool, bool, bool, bool, bool, bool)"/> with nobody standing on the zebra (walkers only).</summary>
        public static Reason Eligible(bool wholeJunction, bool boxLane, bool fitsClearOfZebras, bool canStopBefore, bool roomPastFreeNow) =>
            Eligible(wholeJunction, boxLane, false, fitsClearOfZebras, canStopBefore, roomPastFreeNow);

        /// <summary>
        /// The full rule for a simulation or a test: <see cref="Eligible(bool, bool, bool, bool, bool, bool)"/>, then the safeguard (a
        /// conflicting vehicle able to reach the car's conflict points within the <see cref="Window"/>) = <see cref="Reason.Traffic"/>.
        /// </summary>
        public static Reason Decide(bool wholeJunction, bool boxLane, bool peopleStanding, bool fitsClearOfZebras, bool canStopBefore, bool roomPastFreeNow,
            bool conflictWithinWindow)
        {
            Reason r = Eligible(wholeJunction, boxLane, peopleStanding, fitsClearOfZebras, canStopBefore, roomPastFreeNow);
            if (r != Reason.None) return r;
            return conflictWithinWindow ? Reason.Traffic : Reason.None;
        }

        /// <summary><see cref="Decide(bool, bool, bool, bool, bool, bool, bool)"/> with nobody standing on the zebra.</summary>
        public static Reason Decide(bool wholeJunction, bool boxLane, bool fitsClearOfZebras, bool canStopBefore, bool roomPastFreeNow, bool conflictWithinWindow) =>
            Decide(wholeJunction, boxLane, false, fitsClearOfZebras, canStopBefore, roomPastFreeNow, conflictWithinWindow);

        /// <summary><see cref="Decide(bool, bool, bool, bool, bool, bool)"/> with the Whole junction gap rule.</summary>
        public static Reason Decide(bool boxLane, bool fitsClearOfZebras, bool canStopBefore, bool roomPastFreeNow, bool conflictWithinWindow) =>
            Decide(true, boxLane, fitsClearOfZebras, canStopBefore, roomPastFreeNow, conflictWithinWindow);

        /// <summary>
        /// JR-T-036 (1.4.1): the worst people class on the target zebra and the later zebras of its section
        /// (<see cref="ExitZebraWait.Person"/> / <see cref="ExitZebraWait.Worse"/>) has somebody standing: exit blocked, the spot.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool PeopleStanding(int worstPeople) => worstPeople == ExitZebraWait.Stationary;

        /// <summary>
        /// JR-T-035 (review F4), simplified by JR-T-036: the walkers on the target zebra can hold the car at all: an estimated
        /// wait above 0 (somebody still to cross the car's way). Walkers that are all past the car's way (wait 0) cannot hold it
        /// (the game's pedestrian check does not), so no drive-up: today's distance and the normal gap time. Standing people
        /// never get here (<see cref="Reason.PeopleStanding"/> keeps the car at its spot first).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool PeopleCanHold(float wait) => wait > 0f;

        /// <summary>
        /// The distance the game's pedestrian check uses for a person on zebra <paramref name="zebraStart"/> of the
        /// target's lane: with a drive-up pending (<paramref name="driveUp"/>) and the zebra at or after the target zebra
        /// in the same section, the distance to the stop point right before the TARGET zebra (<paramref name="targetDistance"/>:
        /// the first zebra with people binds; a later zebra of the same section does not draw the car over the target, where its
        /// tail could stand on the target); otherwise today's (the section's spot).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float PedestrianDistance(bool driveUp, bool sameSection, float zebraStart, float targetStart, float targetDistance, float todayDistance) =>
            driveUp && sameSection && zebraStart >= targetStart - WholePathRule.SpotEpsilon ? targetDistance : todayDistance;

        /// <summary>
        /// Crossing a section (front past its spot): a person on a later zebra of it that the car would not fit clear of the
        /// zebras before (<paramref name="noFit"/>) holds the car before the first zebra still ahead
        /// (<paramref name="firstAheadDistance"/>), never nearer than today's distance: "Cars never stop on a zebra".
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CrossingDistance(bool noFit, float firstAheadDistance, float todayDistance) =>
            noFit && firstAheadDistance < todayDistance ? firstAheadDistance : todayDistance;

        /// <summary>
        /// After a drive-up (the car's front past its section's spot, the zebra still ahead and the car able to stop before
        /// it): the room past the section is checked again before the car moves onto the zebra, while people are on it or
        /// while the car stands at it (front within the stop margin plus <see cref="ExitZebraWait.AtZebraMetres"/>), so a car
        /// that waited never steps onto the zebra with its exit blocked ("Cars never stop on a zebra").
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RoomAgainAtZebra(bool peopleOnZebra, bool standing, float frontToZebra, float margin) =>
            peopleOnZebra || (standing && frontToZebra <= margin + ExitZebraWait.AtZebraMetres);

        /// <summary>Stable text of a <see cref="Reason"/> (why-slowed extra).</summary>
        public static string ReasonId(Reason r)
        {
            switch (r)
            {
                case Reason.None: return "drives_up";
                case Reason.Traffic: return "traffic_before_zebra";
                case Reason.ExitBlocked: return "exit_blocked";
                case Reason.Box: return "yellow_box";
                case Reason.TailOnZebra: return "tail_on_zebra";
                case Reason.CannotStop: return "cannot_stop_before_zebra";
                case Reason.NotZebraSection: return "not_a_zebra_section";
                case Reason.StockGapRule: return "stock_gap_rule";
                case Reason.PeopleStanding: return "standing_on_zebra";
                default: return "reason_" + ((int)r).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
}
