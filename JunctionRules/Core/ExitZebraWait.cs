using System.Runtime.CompilerServices;

namespace JunctionRules.Core
{
    /// <summary>
    /// JR-T-024 (owner ruling 19 Nov 2026, BINDING): waiting in a junction box just before its EXIT zebra while people cross.
    /// A car about to cross a boxed junction whose exit has a zebra:
    /// - people MOVING across that zebra = a short wait: the car may enter the box and wait just before the zebra (the game's
    ///   own pedestrian stop: front a stop margin before it, never on it), if the road beyond has room for its whole length
    ///   (trailers included) plus the room margin, free now;
    /// - people STATIONARY on the zebra (standing, queueing, not clearing) = blocked: the car waits at the box line;
    /// - no room free now beyond (vehicles blocking the exit): it waits at the box line.
    /// One general rule, behind the setting "Look past the car in front at a box" (off = 1.0.1 driving bit for bit).
    ///
    /// "Moving" is the game's own walking test on each object on the zebra's pedestrian lane (the one CheckPedestrian uses,
    /// <see cref="CrossingPush.Pedestrian"/>: a Moving component with speed^2 &gt;= 0.01). One standing person is enough for
    /// "stationary". A person standing in the waiting area at a lane end (G23: waiting at the kerb, not crossing) is not on the
    /// zebra for this rule when "Cars never push through people" is on (that setting already never holds a car for them).
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

        /// <summary>One object on the exit zebra's pedestrian lane: <see cref="None"/>, <see cref="Moving"/> or <see cref="Stationary"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Person(bool hasMoving, float speedSq, bool noPushPeople, bool inWaitZone)
        {
            bool standing = CrossingPush.Pedestrian(false, hasMoving, speedSq) == CrossingPush.PushReason.Standing;
            if (noPushPeople && CrossingPush.NotCrossing(standing, inWaitZone, false)) return None;
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
        /// The distance the game's pedestrian check uses for a person on a zebra of the path: with
        /// <see cref="Verdict.WaitBeforeZebra"/> for this lane and the overlap being its exit zebra, the game's own distance to
        /// that zebra (the car stops just before it, inside the box); otherwise today's distance (the section's spot).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float PedestrianDistance(bool waitBeforeZebra, bool isExitZebra, float stockDistance, float todayDistance) =>
            waitBeforeZebra && isExitZebra ? stockDistance : todayDistance;

        /// <summary>
        /// Counting only: a standing car in the box waits at the exit zebra (front bumper <paramref name="frontToZebra"/> m before
        /// its start: not on it, and within the stop margin plus <see cref="AtZebraMetres"/>) while somebody is on it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool WaitingAtZebra(float frontToZebra, float margin, int people) =>
            people != None && frontToZebra >= -BoxDriving.NoseTolerance && frontToZebra <= margin + AtZebraMetres;
    }
}
