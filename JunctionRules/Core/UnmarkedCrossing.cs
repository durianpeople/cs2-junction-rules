using System.Runtime.CompilerServices;

namespace JunctionRules.Core
{
    /// <summary>
    /// JR-T-014d, owner rule G11 (17 Nov 2026): pedestrians crossing a road NOT on a zebra have no right of way. Cars do not
    /// give way to people waiting at or approaching an unmarked crossing; the person waits at the kerb until the approaching
    /// cars leave a safe gap and only then steps out. Safety floor: a car still never drives into a person who is ALREADY on
    /// the carriageway of the crossing (it may slow or stop as a last resort). Zebras (people have priority, cars never push
    /// through, the 2 s people pass) and signal crossings (the signal decides) are unchanged.
    ///
    /// Stock (decompile 2026-11-13):
    /// - An unmarked crossing is the game's crosswalk lane at a node whose composition has no Crosswalk marking:
    ///   Game.Net/LaneSystem.cs 8572-8582 sets PedestrianLaneFlags.Crosswalk, then Unsafe when
    ///   (CompositionFlags.General.Crosswalk) is missing, and drops its signals (hasSignals = false). Pathfinding prices it
    ///   as the "unsafe crosswalk" (Game.Pathfind/PathUtils.cs 1301-1302: (Unsafe | Crosswalk) == both). People RUN on it
    ///   (Game.Simulation/HumanNavigationSystem.cs GetTargetSpeed 687-689). Removing a zebra leaves exactly this lane.
    /// - Priority: LaneOverlapSystem 1361-1369 gives the road lane priority delta +1 (road yields) only against a pedestrian
    ///   lane that is NOT Unsafe; against an Unsafe one the delta is 0, and the overlap carries OverlapFlags.Unsafe
    ///   (1240-1247). But the car never reads that delta for people: CarLaneSpeedIterator.CheckOverlappingLanes sends every
    ///   creature on any crossed pedestrian lane to CheckPedestrian (copy: Vanilla/JRCarLaneSpeedIterator.cs, creature
    ///   branch before S2-08), which brakes for a person whose target lies in the overlap or who stands near it, Unsafe or
    ///   not. So in stock, cars give way to people on unmarked crossings exactly as on zebras.
    /// - The person side: CreatureTargetIterator.IterateLane / CheckOverlapLane (Game.Simulation/CreatureTargetIterator.cs
    ///   36-103, called from HumanNavigationSystem.MoveLaneTarget 1503-1531) clamps the person's target at the near edge of
    ///   a road / track overlap only while (a) the crossed car lane's LaneReservation reaches past the overlap start or has
    ///   priority &gt;= 108 (cars reserve their next lanes over their braking distance only:
    ///   CarNavigationSystem.ReserveNavigationLanes 2251-2316), or (b) a Moving lane object covers the overlap (a car on
    ///   it; a car's LaneObject range is its position to position + braking distance, ReserveNavigationLanes 2262 and
    ///   Game.Vehicles/ReferencesSystem.cs 345). So stock people step out in front of any car that can still stop, and the
    ///   car stops. There is no gap or time judgement on the person side.
    ///
    /// This class: the classification, the person's gap judgement and the car-side decision. Pure arithmetic,
    /// Burst-compatible (static methods on plain values), tested offline (tests/UnmarkedCrossingTests.cs).
    /// </summary>
    public static class UnmarkedCrossing
    {
        /// <summary>Game.Net.PedestrianLaneFlags values (mirrored, as ZebraSections).</summary>
        public const int PedestrianUnsafe = 1;
        public const int PedestrianCrosswalk = 2;

        /// <summary>Game.Net.CarLaneFlags.SideConnection (a driveway / building connection lane).</summary>
        public const uint CarSideConnection = 0x8u;

        /// <summary>Which kind of pedestrian lane a car lane crosses.</summary>
        public enum Kind
        {
            /// <summary>Not a crosswalk: a sidewalk / footway along or beside the road.</summary>
            Sidewalk = 0,
            /// <summary>A footway crossed by a driveway / building connection lane (the owner's pavement rule: people have priority).</summary>
            Driveway = 1,
            /// <summary>A marked crosswalk without a signal (people have priority).</summary>
            Zebra = 2,
            /// <summary>A crosswalk with a signal (the signal decides).</summary>
            Signal = 3,
            /// <summary>A crosswalk without the marking (Unsafe): G11, people give way.</summary>
            Unmarked = 4,
        }

        /// <summary>
        /// Classifies one crossing of a car lane with a pedestrian lane. pedestrianFlags = Game.Net.PedestrianLaneFlags,
        /// pedestrianSignal = the pedestrian lane has a LaneSignal, carLaneFlags = the CROSSING car lane's Game.Net.CarLaneFlags.
        /// A driveway (SideConnection car lane) is never an unmarked crossing, whatever the footway's flags.
        /// </summary>
        public static Kind Classify(int pedestrianFlags, bool pedestrianSignal, uint carLaneFlags)
        {
            if ((carLaneFlags & CarSideConnection) != 0) return Kind.Driveway;
            if ((pedestrianFlags & PedestrianCrosswalk) == 0) return Kind.Sidewalk;
            if (pedestrianSignal) return Kind.Signal;
            return (pedestrianFlags & PedestrianUnsafe) != 0 ? Kind.Unmarked : Kind.Zebra;
        }

        /// <summary>The pedestrian lane itself is an unmarked crossing (person side: no car lane at hand).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsUnmarkedLane(int pedestrianFlags, bool pedestrianSignal) =>
            !pedestrianSignal && (pedestrianFlags & (PedestrianUnsafe | PedestrianCrosswalk)) == (PedestrianUnsafe | PedestrianCrosswalk);

        // ---- car side ----

        /// <summary>The car's action for one person on a crossed pedestrian lane.</summary>
        public const int Stock = 0;
        /// <summary>Unmarked crossing, person not on the carriageway: the car does not give way (the game's check is skipped).</summary>
        public const int Skip = 1;
        /// <summary>Unmarked crossing, person already on the carriageway: safety floor, the game's own check at the crossing.</summary>
        public const int Floor = 2;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CarAction(bool ruleOn, Kind kind, bool personOnCarriageway) =>
            !ruleOn || kind != Kind.Unmarked ? Stock : personOnCarriageway ? Floor : Skip;

        /// <summary>
        /// Metres a person may stand past the kerb edge of the carriageway (the near edge of the first road overlap in the
        /// direction of walking) and still count as waiting at the kerb. The game clamps a waiting person's target exactly at
        /// that edge (CheckOverlapLane), and a person overshoots it a little while stopping.
        /// </summary>
        public const float KerbTolerance = 0.5f;

        /// <summary>
        /// Whether a person is on the carriageway of a crossing. p = the person's position along the pedestrian lane (m from
        /// its start), lo / hi = the carriageway (first road overlap start, last road overlap end, m), direction = +1 walking
        /// towards the lane end, -1 towards its start, 0 standing (then within the tolerance of either kerb = at the kerb).
        /// A person past the far kerb is off it.
        /// </summary>
        public static bool OnCarriageway(float p, float lo, float hi, int direction, float tolerance = KerbTolerance)
        {
            if (!(hi > lo)) return false;
            if (direction > 0) return p > lo + tolerance && p < hi;
            if (direction < 0) return p < hi - tolerance && p > lo;
            return p > lo + tolerance && p < hi - tolerance;
        }

        /// <summary>Speed (m/s, along the lane) above which a person counts as walking in a direction.</summary>
        public const float WalkingSpeed = 0.3f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Direction(float speedAlongLane) => speedAlongLane > WalkingSpeed ? 1 : speedAlongLane < -WalkingSpeed ? -1 : 0;

        // ---- person side ----

        /// <summary>
        /// Seconds added to the person's own crossing time: stepping off the kerb, and a margin so the car does not have to
        /// brake for them (UK Green Cross Code: cross when there is plenty of time). The judgement already counts the car's
        /// braking distance on top (as the whole-junction gap does).
        /// </summary>
        public const float MarginSeconds = 1f;

        /// <summary>Slowest speed (m/s) a person is assumed to cross at (guards a zero prefab speed).</summary>
        public const float MinPersonSpeed = 0.5f;

        /// <summary>A car slower than this (m/s) counts as standing: it leaves the gap (the safety floor stops it if it moves off).</summary>
        public const float StandingSpeed = 0.5f;

        /// <summary>Most car lanes (crossed lanes plus the lanes feeding them) one person reads per update (more: not read).</summary>
        public const int MaxLanes = 24;

        /// <summary>Most cars one person reads per update; when hit the person waits (dense traffic counts as busy).</summary>
        public const int MaxCars = 64;

        /// <summary>
        /// The person's crossing speed: the game makes people RUN on an Unsafe crosswalk (GetTargetSpeed 687-689), so the run
        /// speed, else the walk speed, never under <see cref="MinPersonSpeed"/>.
        /// </summary>
        public static float PersonSpeed(float runSpeed, float walkSpeed)
        {
            float s = runSpeed > 0f ? runSpeed : walkSpeed;
            return s > MinPersonSpeed ? s : MinPersonSpeed;
        }

        /// <summary>Seconds until the person is past the far edge of one crossed lane: distance (m, &lt;= 0 = past) / speed.</summary>
        public static float ClearTime(float distanceToFarEdge, float personSpeed)
        {
            float s = personSpeed > MinPersonSpeed ? personSpeed : MinPersonSpeed;
            return distanceToFarEdge > 0f ? distanceToFarEdge / s : 0f;
        }

        /// <summary>
        /// One approaching car against one crossed lane, judged like the whole-junction / exit gap
        /// (ExitGiveWay.VehicleBlocks: stock reach + speed x own time): gap = metres from the car's front to the start of the
        /// crossing on its lane (&lt;= 0: the car is at or on the crossing: the game's own clamp decides, a Moving car on the
        /// overlap holds the person, a standing one is passed by the people pass); speed = its speed towards the crossing;
        /// braking = its stock braking distance; clearTime = the person's time to clear this lane. Blocks when the car could
        /// reach the crossing before the person is past it plus <see cref="MarginSeconds"/>. A standing car never blocks.
        /// </summary>
        public static bool CarBlocks(float gap, float speed, float braking, float clearTime)
        {
            if (!(gap > 0f)) return false;
            if (!(speed >= StandingSpeed)) return false;
            float t = (clearTime > 0f ? clearTime : 0f) + MarginSeconds;
            float reach = (braking > 0f ? braking : 0f) + speed * t;
            return reach >= gap;
        }

        /// <summary>
        /// Whether the person is still before the carriageway (may be held at the kerb): p, lo, hi in metres as
        /// <see cref="OnCarriageway"/>; forward = walking towards the lane end. A person already on it, or past it, is not held.
        /// </summary>
        public static bool BeforeKerb(float p, float lo, float hi, bool forward, float tolerance = KerbTolerance)
        {
            if (!(hi > lo)) return false;
            return forward ? p <= lo + tolerance : p >= hi - tolerance;
        }

        /// <summary>
        /// The person's target (curve delta on the lane) must be pulled back to the kerb: it lies past the kerb edge
        /// (forward: beyond lo; backward: before hi). Then the target becomes the kerb edge, as the game's own clamp.
        /// </summary>
        public static bool TargetPastKerb(float target, float lo, float hi, bool forward) => forward ? target > lo : target < hi;

        /// <summary>Route of a car on a lane feeding the crossed lane.</summary>
        public enum Route
        {
            /// <summary>Its navigation lanes contain the crossed lane.</summary>
            Takes = 0,
            /// <summary>Its navigation lanes go through another lane of the same junction: not over this crossing.</summary>
            Other = 1,
            /// <summary>Not known yet (its navigation lanes end before the junction): counted, erring towards waiting.</summary>
            Unknown = 2,
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RouteCounts(Route r) => r != Route.Other;
    }

    /// <summary>JR-T-014d (G11): the timing-log part for the unmarked crossings (copy probe line, appended).</summary>
    public static class UnmarkedCrossingCounts
    {
        /// <summary>
        /// "; unmarked crossings: car-steps not held for people N, people held at the kerb N, stopped for a person already
        /// crossing unmarked N"; a negative value = not counted in this window ("n/a").
        /// </summary>
        public static string Format(long carStepsNotHeld, long peopleHeld, long stoppedForCrossing)
        {
            return "; unmarked crossings: car-steps not held for people " + N(carStepsNotHeld) + ", people held at the kerb " + N(peopleHeld)
                + ", stopped for a person already crossing unmarked " + N(stoppedForCrossing);
        }

        private static string N(long v) => v < 0 ? "n/a" : v.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
