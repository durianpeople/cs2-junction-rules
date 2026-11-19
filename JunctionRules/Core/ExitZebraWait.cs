using System.Runtime.CompilerServices;

namespace JunctionRules.Core
{
    /// <summary>
    /// JR-T-024 (owner ruling 19 Nov 2026, BINDING): waiting in a junction box just before its EXIT zebra while people cross.
    /// A car about to cross a boxed junction whose exit has a zebra:
    /// - people MOVING across that zebra = a short wait: the car may enter the box and wait just before the zebra (the game's
    ///   own pedestrian stop: front a stop margin before it, never on it), if the road beyond has room for its whole length
    ///   (trailers included) plus the room margin, free now, and the vehicle fits between the entry zebra (or the line) and that
    ///   stop point (<see cref="FitsBeforeZebra"/>: never a tail on another zebra);
    /// - people STATIONARY on the zebra (standing, queueing, not clearing) = blocked: the car waits at the box line;
    /// - no room free now beyond (vehicles blocking the exit): it waits at the box line.
    /// One general rule, behind the setting "Look past the car in front at a box" (off = 1.0.1 driving bit for bit).
    ///
    /// "Moving" is the game's own walking test on each object on the zebra's pedestrian lane (the one CheckPedestrian uses,
    /// <see cref="CrossingPush.Pedestrian"/>: a Moving component with speed^2 &gt;= 0.01). One standing person is enough for
    /// "stationary". A person standing in the waiting area at a lane end (G23: waiting at the kerb, not crossing) is not on the
    /// zebra for this rule when "Cars never push through people" is on (that setting already never holds a car for them).
    /// JR-T-024b (review O1): a STANDING person counts only on the car's way over the zebra or beside it (<see cref="OnCarsWay"/>:
    /// the overlap's range on the pedestrian lane, the game's CheckPedestrian / G23 PersonInPath rule, widened by the game's
    /// beside allowance); one standing on the far half of a wide zebra holds nobody. Walking people count anywhere on it
    /// (conservative: they may walk into the car's way). JR-T-024b (review O2): only creatures are people, as in the game's own
    /// check (CarLaneSpeedIterator calls CheckPedestrian only for objects with a Creature component; a cyclist on the crossing
    /// is a vehicle there, braked for by UpdateMaxSpeed): a cyclist on the exit zebra is not a person for this rule.
    ///
    /// The exit zebra of a boxed junction lane is its last zebra in driving order, when it starts in the lane's second half
    /// (the entry zebra lies at the lane start, the exit zebra at its end, whatever the junction's size).
    ///
    /// Pure arithmetic on plain values (Burst-compatible, no allocation).
    /// </summary>
    public static class ExitZebraWait
    {
        /// <summary>People on the exit zebra, worst first wins (<see cref="Worse"/>): nobody (or only people waiting at the kerb).</summary>
        public const int None = 0;
        /// <summary>Everyone on the exit zebra is walking.</summary>
        public const int Moving = 1;
        /// <summary>At least one person stands on the exit zebra.</summary>
        public const int Stationary = 2;

        /// <summary>The decision at the box line for a car that has room past the box (the box rule lets it in).</summary>
        public enum Verdict
        {
            /// <summary>Setting off or nobody on the exit zebra: exactly today's driving.</summary>
            Today = 0,
            /// <summary>Wait at the box line (people standing on the exit zebra, or the road beyond not free now).</summary>
            Line = 1,
            /// <summary>Enter; the game's own pedestrian check stops the car just before the exit zebra while people cross.</summary>
            WaitBeforeZebra = 2,
        }

        /// <summary>The exit zebra starts in this share of the lane or later (driving coordinates): the lane's second half.</summary>
        public const float ExitFrom = 0.5f;

        /// <summary>
        /// A standing car counts as waiting at the exit zebra (counted apart from "standing inside a box") when its front bumper
        /// is at most this far (m) before the zebra beyond the game's stop margin.
        /// </summary>
        public const float AtZebraMetres = 2f;

        /// <summary>
        /// JR-T-024b (review O1): metres past the car's range on the pedestrian lane (beyond G23's
        /// <see cref="CrossingPush.InPathToleranceMetres"/>) within which a standing person is still beside the car's way. The
        /// game's CheckPedestrian counts a person outside the range when their distance to the car's overlap line, less up to
        /// 1 m for facing it, is under half the car's width + half the person's size; the range is the car LANE's footprint on
        /// the crossing (at least the car's half width either side of that line), so the beside test reaches at most about 1 m
        /// past it (a bus: 1.25 + 0.25 + 1 - lane half width 1.5).
        /// </summary>
        public const float BesideMetres = 1f;

        /// <summary>
        /// JR-T-024b (review O1): the person at <paramref name="personPos"/> (game offset on the zebra's pedestrian lane) is on the
        /// car's way over it or beside it: inside the overlap's range on that lane (rangeA / rangeB, game offsets, either order)
        /// widened by G23's tolerance plus <see cref="BesideMetres"/> (laneLength in metres).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool OnCarsWay(float personPos, float rangeA, float rangeB, float laneLength)
        {
            float lo = (rangeA < rangeB ? rangeA : rangeB) * laneLength - CrossingPush.InPathToleranceMetres - BesideMetres;
            float hi = (rangeA < rangeB ? rangeB : rangeA) * laneLength + CrossingPush.InPathToleranceMetres + BesideMetres;
            float p = personPos * laneLength;
            return p >= lo && p <= hi;
        }

        /// <summary>
        /// One object on the exit zebra's pedestrian lane: <see cref="None"/>, <see cref="Moving"/> or <see cref="Stationary"/>.
        /// isCreature = the game's own people test (JR-T-024b, O2: a cyclist is not one); onCarsWay = <see cref="OnCarsWay"/>
        /// (JR-T-024b, O1: only read for a standing person; walking people count anywhere on the zebra).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Person(bool isCreature, bool hasMoving, float speedSq, bool noPushPeople, bool inWaitZone, bool onCarsWay)
        {
            if (!isCreature) return None;
            bool standing = CrossingPush.Pedestrian(false, hasMoving, speedSq) == CrossingPush.PushReason.Standing;
            if (noPushPeople && CrossingPush.NotCrossing(standing, inWaitZone, false)) return None;
            if (standing && !onCarsWay) return None;
            return standing ? Stationary : Moving;
        }

        /// <summary>The worse of two people classes (stationary over moving over none).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Worse(int a, int b) => a > b ? a : b;

        /// <summary>The zebra starting at <paramref name="start"/> (driving coordinates) is the last on the lane and in its second half.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsExitZebra(float start, float lastZebraStart) =>
            start >= ExitFrom && start >= lastZebraStart - WholePathRule.SpotEpsilon;

        /// <summary>
        /// The decision for a car before the box line whose box room test passed. lookPast = the setting; people = the class
        /// of the exit zebra; roomFreeNow = room for the whole vehicle plus the margin past the box, free now (no look-past
        /// credit: the car will stand before the zebra, so the room must already be there).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Verdict Decide(bool lookPast, int people, bool roomFreeNow)
        {
            if (!lookPast || people == None) return Verdict.Today;
            return people == Stationary || !roomFreeNow ? Verdict.Line : Verdict.WaitBeforeZebra;
        }

        /// <summary>
        /// Review fix (JR-T-024): the whole vehicle fits between the end of the zebra before the exit zebra (the entry zebra; 0 =
        /// the box line when there is none) and the stop point a stop margin before the exit zebra: waiting there never leaves a
        /// tail on another zebra or past the line ("Cars never stop on a zebra"; the zebra sections merge a vehicle that does not
        /// fit into the entry decision for the same reason). Same arithmetic as <see cref="CrossingPoint.Fits"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool FitsBeforeZebra(float laneLength, float zebraStart, float previousEnd, float margin, float vehicleLength) =>
            CrossingPoint.Fits(laneLength, zebraStart, previousEnd, margin, vehicleLength);

        /// <summary>
        /// The distance the game's pedestrian check uses for a person on a zebra of the path: with
        /// <see cref="Verdict.WaitBeforeZebra"/> for this lane and the overlap being its exit zebra, the game's own distance to
        /// that zebra (the car stops just before it, inside the box); otherwise today's distance (the section's spot).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float PedestrianDistance(bool waitBeforeZebra, bool isExitZebra, float stockDistance, float todayDistance) =>
            waitBeforeZebra && isExitZebra ? stockDistance : todayDistance;

        /// <summary>
        /// Counting only: a standing car in the box waits at the exit zebra (front bumper <paramref name="frontToZebra"/> m before
        /// its start: not on it, and within the stop margin plus <see cref="AtZebraMetres"/>) BECAUSE of a person on it.
        /// JR-T-024b (review O3): heldByZebraPerson = the car's own blocker (the game's Blocker of its last step, which
        /// CheckPedestrian sets to the person it stops for) is a person on this exit zebra. Somebody merely being on the zebra
        /// while the car stands there for another reason (a vehicle ahead, the exit blocked, a light) is not a zebra wait: that
        /// car is counted as standing inside the box, as before JR-T-024.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool WaitingAtZebra(float frontToZebra, float margin, bool heldByZebraPerson) =>
            heldByZebraPerson && frontToZebra >= -BoxDriving.NoseTolerance && frontToZebra <= margin + AtZebraMetres;
    }
}
