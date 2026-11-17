using System.Runtime.CompilerServices;

namespace JunctionRules.Core
{
    /// <summary>
    /// 0.4.4-probe (JR-T-012), owner rule 16 Nov 2026 (binding wording): "Cars coming out of ANY building must give way,
    /// and when that building exit is a yellow box, must ALSO obey the yellow box rules."
    ///
    /// Setting "Vehicles leaving buildings give way" (default on; off = the 0.4.3 driving exactly). A vehicle leaving a
    /// building waits at the exit until the road lanes it crosses or merges into are clear. Clear is the Whole junction
    /// gap judgement (Core/ArrivalTime.cs ArrivalProjection): every road vehicle approaching the conflict on that lane or on
    /// the lanes feeding it counts when its stock reach (braking distance + the stock 2 s look-ahead) plus its speed times
    /// the leaving vehicle's own arrival time at the conflict reaches the conflict; red skips, amber skips when the
    /// approaching vehicle can stop (Core FeedingSignal, the 0.2.4 rulings). The gap check runs at EVERY building exit,
    /// boxed or not; at a boxed exit the 0.4.3 box rule (room past the band) applies as well: both must pass
    /// (<see cref="Decide"/>). On-duty emergency vehicles (stock priority 108) keep the stock behaviour. Road vehicles are
    /// never held for a leaving vehicle (apart from the 0.4.2/0.4.3 box bands). No starvation safeguard (owner): a vehicle
    /// at an exit onto a road that never clears waits for ever (PROBE-STAGE4-BOXTOOL.md section 15, deadlock notes).
    ///
    /// Where a vehicle leaves a building in the game (decompile ~/Code/decomp/yb-2026-11-13/full; Core/BuildingExit.cs):
    /// <list type="bullet">
    /// <item>Driveway (a building sub-net joined to the road's middle): Game.Net/LaneSystem.cs CreateEdgeConnectionLane
    /// (3646+) lays a node lane owned by the ROAD edge from the drive's node into each road lane; the leaving lanes
    /// (isSource) get CarLaneFlags Unsafe | SideConnection | Yield (3864-3876), the entering ones no Yield. Stock
    /// CarLaneSpeedIterator.CheckOverlappingLanes gives way there only to reservations (road vehicles never reserve
    /// their current lane: CarNavigationSystem.ReserveNavigationLanes reserves navigation lanes only) and to vehicles
    /// already ON the crossed road lane whose stock look-ahead (2 s) reaches the conflict; a vehicle in the junction
    /// or on the road before it is not seen, a slow truck pulls out on a 2 s gap. Hooked in the copy's
    /// CheckOverlappingLanes for a SideConnection + Yield lane (block S2-29, Stage2/JRExitGiveWay.cs).</item>
    /// <item>Spawn location / car park gate (a building ConnectionLane): the vehicle drives its Connection lane at
    /// 277 m/s unspawned (CarNavigationSystem 1492-1497), the road lane is never iterated
    /// ("if Connection goto IL_09bf", 2022-2024) and it joins the road lane (ResetSpeed) as soon as it is within
    /// 1 m + pivot of the lane end at &lt;= 3 m/s (1873-1886): no give-way at all. Hooked in the copy's navigation
    /// (S2-30 decides and holds, S2-31 keeps a held vehicle off the road lane).</item>
    /// <item>Lot exit (0.4.5, the usual one: industry, depots, surface car parks): the vehicle drives the lot's AREA lanes
    /// (ConnectionLane with ConnectionLaneFlags.Area becomes CarLaneFlags.Area, not Connection: InitializeSystem 288,
    /// CarNavigationSystem 672 / 1037) and the area branch never reaches the lane speed iterator (1596), so the road lane it
    /// joins is never looked at. Hooked in the copy's navigation (S2-32 decides and holds, S2-33 keeps it on its area lane).</item>
    /// <item>On-duty emergency: VehicleUtils.GetPriority(Car) = 108 with CarFlags.Emergency (654-657); stock lets it
    /// ignore non-physical signals and reserves the other lanes of its group at 102. Kept: no exit hold at all.</item>
    /// </list>
    /// Game-independent arithmetic, Burst-compatible (static methods on plain values), unit-tested (tests ExitGiveWayTests).
    /// </summary>
    public static class ExitGiveWay
    {
        /// <summary>Game.Net.CarLaneFlags values (Game.Net/CarLaneFlags.cs).</summary>
        public const uint Unsafe = 0x1u, SideConnection = 0x8u, TurnLeft = 0x10u, TurnRight = 0x20u, Yield = 0x400u;

        /// <summary>The stock priority of an on-duty emergency vehicle (VehicleUtils.GetPriority: CarFlags.Emergency = 108).</summary>
        public const int EmergencyPriority = ArrivalProjection.MaxCarPriority;

        /// <summary>Most conflicting road lanes judged per exit (a driveway across a 2+2 road has 4).</summary>
        public const int MaxConflictLanes = 8;

        /// <summary>Most lanes read upstream of one exit (conflict lanes, the junction lanes feeding them, the lanes feeding those).
        /// 0.4.7: 48 (was 24: an exit near a junction, two levels of feeders, ran out; a lane cap hit still errs to go,
        /// Core ExitPaths.CapBusy).</summary>
        public const int MaxScanLanes = 48;

        /// <summary>Most vehicles read per exit decision. 0.4.7: only vehicles before the conflict and within
        /// <see cref="MaxScanMetres"/> count (ExitPaths.CountsAgainstBudget), and a hit is dense traffic: busy (wait;
        /// ExitPaths.CapBusy). 0.4.4-0.4.6 counted every vehicle on a read lane and erred to go.</summary>
        public const int MaxVehiclesPerScan = 96;

        /// <summary>Lanes upstream of a conflict lane read at most: 1 = the junction lanes feeding it, 2 = also the road
        /// lanes feeding those (an exit close to a junction sees the cars about to come through it).</summary>
        public const int MaxFeedDepth = 2;

        /// <summary>
        /// No lane is read upstream once the conflict is this far behind its end (m): no road vehicle reaches further
        /// (36 m/s with the 2 s look-ahead + a 3 s arrival time + its braking distance stays under it).
        /// </summary>
        public const float MaxScanMetres = 250f;

        /// <summary>Lateral tolerance (m) for "the lane the vehicle joins" in the geometric crossing test.</summary>
        public const float JoinTolerance = 0.5f;

        /// <summary>The lane is a leaving lane of a building drive onto a road (SideConnection + Yield; entering lanes have no Yield).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDrivewayExitLane(uint carLaneFlags) => (carLaneFlags & (SideConnection | Yield)) == (SideConnection | Yield);

        /// <summary>On-duty emergency vehicle (stock priority 108): stock behaviour, never held at an exit.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsEmergency(int priority) => priority >= EmergencyPriority;

        /// <summary>The give-way applies: setting on and not an on-duty emergency vehicle.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Applies(bool settingOn, int priority) => settingOn && !IsEmergency(priority);

        /// <summary>
        /// The vehicle's turn crosses oncoming traffic, read with the game's own traffic side
        /// (CityConfigurationSystem.leftHandTraffic, the navigation job's m_LeftHandTraffic): left-hand traffic = a right
        /// turn out of the exit crosses the near lane; right-hand traffic = a left turn. turnLeft / turnRight = the drive
        /// lane's CarLaneFlags TurnLeft / TurnRight (LaneSystem sets them from the lane's own bend). Only labels the
        /// counters: which lanes are crossed comes from the game's lane overlaps / geometry, so it is right on both sides.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TurnsAcross(bool turnLeft, bool turnRight, bool leftHandTraffic) => leftHandTraffic ? turnRight && !turnLeft : turnLeft && !turnRight;

        public enum ConflictRole : byte
        {
            /// <summary>Not on the vehicle's way (beyond the lane it joins).</summary>
            None = 0,
            /// <summary>A lane between the exit and the lane it joins: crossed.</summary>
            Cross = 1,
            /// <summary>The lane it joins: merged into.</summary>
            Merge = 2,
        }

        /// <summary>
        /// Geometric role of a road lane for a vehicle leaving at a connection (spawn location) exit, from distances
        /// across the road measured from the exit's start point (the building side): the lane it joins merges; every
        /// lane nearer to the building than that crosses; the rest are not on its way. No traffic side is assumed, so
        /// left- and right-hand traffic come out right by themselves.
        /// </summary>
        public static ConflictRole Role(bool isJoinLane, float laneDistance, float joinDistance)
        {
            if (isJoinLane) return ConflictRole.Merge;
            return laneDistance < joinDistance - JoinTolerance ? ConflictRole.Cross : ConflictRole.None;
        }

        /// <summary>
        /// One road vehicle against one conflict, as the Whole junction projection judges a car on a conflicting lane
        /// (S2-10: stock y + speed * (2 s + own arrival time)), the same for a vehicle still on a lane feeding it so its
        /// reach does not jump at a lane boundary. The caller leaves out a vehicle whose rear is past the conflict end.
        /// gap = metres from the vehicle's front to the conflict start (&lt;= 0 = its front is on the conflict);
        /// occupies = some part of it is on the conflict; braking = its stock braking distance; delta = the leaving
        /// vehicle's own arrival time at the conflict (s). A vehicle standing before the conflict (a queue that stopped
        /// short of the exit) does not block; one standing on it does.
        /// </summary>
        public static bool VehicleBlocks(float gap, bool occupies, float speed, float braking, float delta)
        {
            if (occupies || !(gap > 0f)) return true;
            // 0.4.8 (G7, owner: the exit gives way to the westbound truck right next to it): a road vehicle standing or
            // creeping with its front within PresentMetres of the conflict is AT the exit (often held before an entrance
            // band, waiting for room past it): it has priority, the exit waits. Further back a standing queue still does not
            // block (0.4.7 review F2: a jam that stopped short of the exit).
            if (gap <= PresentMetres) return true;
            if (!(speed > 0f)) return false;
            float reach = (braking > 0f ? braking : 0f) + speed * (ArrivalProjection.StockLookAhead + (delta > 0f ? delta : 0f));
            return reach >= gap;
        }

        /// <summary>
        /// 0.4.9 (G15, Alderwood deadlock 17 Nov 08:5x): as <see cref="VehicleBlocks(float,bool,float,float,float)"/>, but a road
        /// vehicle that is WAITING TO TURN INTO A BUILDING (held by the G9 entrance rule: its way in is full) and stands
        /// before the conflict is not "present at the exit": it does not want to pass the exit, it waits for room inside a
        /// building, and that room comes from vehicles leaving. Live loop: van 767785 stood on the westbound lane before
        /// No. 40's entrance (lot full) within <see cref="PresentMetres"/> of No. 40's own exit, the exit waited for the van
        /// (PresentMetres), the lot never emptied, the van never got in. Standing on the conflict (occupies / gap &lt;= 0) or
        /// moving towards it still blocks exactly as before.
        /// </summary>
        public static bool VehicleBlocks(float gap, bool occupies, float speed, float braking, float delta, bool waitingToEnter)
        {
            if (!waitingToEnter || occupies || !(gap > 0f) || speed > EnteringStandSpeed) return VehicleBlocks(gap, occupies, speed, braking, delta);
            if (!(speed > 0f)) return false;
            float reach = (braking > 0f ? braking : 0f) + speed * (ArrivalProjection.StockLookAhead + (delta > 0f ? delta : 0f));
            return reach >= gap;
        }

        /// <summary>0.4.9 (G15): a vehicle held for room into a building counts as standing up to this speed (m/s; review F2: 0.5 -> 0.2, one that starts to move is judged as moving).</summary>
        public const float EnteringStandSpeed = 0.2f;

        /// <summary>
        /// 0.4.9 (G12, owner 17 Nov: "the sidewalk is a zebra; decide before it, then go in one move"): what the vehicle's
        /// front passing its sidewalk spot (or, with no sidewalk, its first road lane spot) means. A Go that still stands
        /// (Core GoStands: no Wait after it) = judged; no Go but a decision at the first road lane in the same update (it
        /// could no longer stop before the sidewalk, but before the road) = late; neither = unjudged (must be 0).
        /// 0.4.8 counted at the PIVOT hand-over onto the drive lane instead: a lorry held before the sidewalk whose pivot
        /// reached the drive lane start was counted "crosses the sidewalk ... NOT judged" while it still stood before the
        /// sidewalk (live: vehicle 844033 'waits' and 'NOT judged' in the same frame 36642880), and every normal exit that
        /// had passed the sidewalk spot after its Go was re-judged at the road lane and counted "judged only on the
        /// sidewalk" (24 of 28 in the 08:54 window).
        /// </summary>
        public enum CrossOutcome : byte { Judged = 0, Late = 1, Unjudged = 2 }

        public static CrossOutcome Crossing(bool goStands, bool lateDecision) => goStands ? CrossOutcome.Judged : lateDecision ? CrossOutcome.Late : CrossOutcome.Unjudged;

        /// <summary>
        /// 0.4.9 (G12): a vehicle whose front is past its sidewalk spot with a Go that still stands is COMMITTED: it is not
        /// judged again at the first road lane (0.4.8 did, so a Go'd lorry could be stopped ON the pavement, front at the
        /// kerb, by traffic that appeared while it crossed; live: 380901 at No. 40, pivot at the drive start, front at the
        /// kerb). The game's own physical checks (vehicles on its lanes, reservations) still apply. Only while it is moving
        /// (<see cref="CommitMovingSpeed"/>): stopped on the pavement, it is judged again at the first road lane.
        /// </summary>
        public static bool Committed(bool pastSidewalkSpot, bool goStands, bool moving) => pastSidewalkSpot && goStands && moving;

        /// <summary>0.4.9 (G12): a committed vehicle must still be moving (m/s): one that came to a stop on the pavement (people on it, the
        /// vehicle ahead) is judged again at the road lane before it moves on (its Go is up to 16 s old by then).</summary>
        public const float CommitMovingSpeed = 0.5f;

        /// <summary>
        /// 0.4.8 (G7): a road vehicle whose front is this close (m) before a conflict blocks the exit whatever its speed: it
        /// is present at the exit (the blue westbound truck of the 17 Nov shot stood ~2-6 m before the exit, held before the
        /// entrance band, and 0.4.7 let the leaving truck out in front of it because it stood still). 10 m = an 8 m entrance
        /// band's half plus the game's stop margins and a car's nose.
        /// </summary>
        public const float PresentMetres = 10f;

        /// <summary>
        /// 0.4.8 (G7): a driveway exit vehicle within this distance (m) of its spot is judged on every update. 0.4.7 judged it
        /// only while its own exit hold was the binding limit (hold &lt; currentMax), so one creeping under a lower limit (the car
        /// ahead, a curve) reached the spot without a decision and crossed. 12 m &gt; what a vehicle covers between two
        /// navigation updates (16 frames) at 30 m/s.
        /// </summary>
        public const float JudgeNearMetres = 12f;

        /// <summary>0.4.8 (G7): half a sidewalk past its pedestrian lane (m): the outer line a lot exit stops before.</summary>
        public const float SidewalkHalfWidth = 1.5f;

        /// <summary>
        /// 0.4.8 (G7): metres before the join point where a lot exit's straight way meets the sidewalk's outer line.
        /// lateral = the outermost building-side pedestrian lane's distance across the road from the join point (m);
        /// crossing = |cos| of the way's angle to the road's normal (1 = straight across). A shallow way is capped at
        /// 1 / 0.3 (about 73 degrees) so a near-parallel way never pushes the stop point far back.
        /// </summary>
        public static float SidewalkBack(float lateral, float crossing)
        {
            if (!(lateral > 0f)) return 0f;
            float c = crossing > 0.3f ? (crossing < 1f ? crossing : 1f) : 0.3f;
            return (lateral + SidewalkHalfWidth) / c;
        }

        /// <summary>0.4.8 (G7): the exit scan runs when the exit hold binds (as 0.4.7) or the vehicle is within JudgeNearMetres of its spot.</summary>
        public static bool MustJudge(float hold, float currentMax, float waitDistance) => hold < currentMax || waitDistance <= JudgeNearMetres;

        /// <summary>
        /// 0.4.8 (G7): metres the leaving vehicle's front must travel until its WHOLE length (trailers incl.) is past a crossed
        /// conflict (from its front to the conflict start, plus the conflict's length, plus its own length). Its own arrival
        /// time for a CROSSED lane is the time to clear it, not to reach it: a slow 18 m lorry blocks the near lane for
        /// seconds after its nose has passed (0.4.7 used the time to reach the conflict start).
        /// </summary>
        public static float ClearMetres(float toConflictStart, float conflictLength, float ownLength) =>
            (toConflictStart > 0f ? toConflictStart : 0f) + (conflictLength > 0f ? conflictLength : 0f) + (ownLength > 0f ? ownLength : 0f);

        /// <summary>
        /// 0.4.8 (G7, owner rule 17 Nov: the sidewalk a building exit crosses is a ZEBRA): the exit's waiting spot is the
        /// start of the FIRST element on its way: the sidewalk (any pedestrian lane the drive lane overlaps, marked crossing
        /// or not) or, if it comes first, the first road lane. Driving coordinates of the drive lane; 2 = nothing ahead.
        /// </summary>
        public static float ExitSpot(float firstRoad, float firstSidewalk) => firstSidewalk < firstRoad ? firstSidewalk : firstRoad;

        /// <summary>
        /// Where the leaving vehicle waits: W = metres from its front to the first conflict start, minus the game's stop
        /// margin (CheckOverlappingLanes: 0.9 m, 0.4 m bicycle; origDistance already has it). Null hold when it is
        /// already past W (committed) or cannot stop before it (the game's can-stop test GetBrakingDistance &lt;= distance
        /// + margin); then the game's own checks decide, as for every other committed car.
        /// </summary>
        public static bool CanHold(float waitDistance, float margin, float brakingDistance) =>
            waitDistance >= -WholePathRule.SpotEpsilon && brakingDistance <= waitDistance + margin;

        /// <summary>Slack (m) of an area exit's hold: a vehicle that crept this far past its stop point is still held, one that cannot stop within the point plus this is released (stock).</summary>
        public const float AreaHoldSlack = 1.5f;

        /// <summary>
        /// Area exit (0.4.5, S2-32): can the vehicle be held short of the join point? waitDistance = metres its front may still
        /// travel to the stop point (one margin before the join point), brakingDistance = its stock braking distance. A
        /// vehicle already clearly past the stop point (committed, on its way onto the road) or one that cannot stop in time is
        /// left to the game; a stopped vehicle that crept a little past the point is still held (no release by drift).
        /// </summary>
        public static bool CanHoldArea(float waitDistance, float brakingDistance) =>
            waitDistance >= -AreaHoldSlack && brakingDistance <= (waitDistance > 0f ? waitDistance : 0f) + AreaHoldSlack;

        /// <summary>
        /// 0.4.6: distance (m) before the join point from which a lot exit is judged and held. The stock area branch hands a vehicle
        /// over to the road lane as soon as the end of its area path is within max(look-ahead, braking distance at 11.1 m/s + 1 m +
        /// pivot) of it (CarNavigationSystem 1767-1772, MoveAreaTarget 2529), up to ~20 m before the join point; 0.4.5 only judged
        /// a vehicle whose braking speed was already below its current limit (the "hold &lt; currentMax" filter) and measured the
        /// distance with half the WHOLE train (trailers) instead of the tractor's front, so a truck was handed over to the road
        /// lane before it was ever judged. The scan must therefore start before that hand-over: the braking distance at the lane
        /// speed plus the vehicle's front offset plus <see cref="AreaScanExtra"/> (the join point lies up to a lane or two beyond
        /// the end of the area path), never less than <see cref="AreaScanMin"/>.
        /// </summary>
        public static float AreaScanMetres(float brakingAtLaneSpeed, float frontOffset)
        {
            float v = (brakingAtLaneSpeed > 0f ? brakingAtLaneSpeed : 0f) + (frontOffset > 0f ? frontOffset : 0f) + AreaScanExtra;
            return v > AreaScanMin ? v : AreaScanMin;
        }

        /// <summary>Extra metres of the lot exit scan beyond the stock hand-over distance (join point beyond the area path end).</summary>
        public const float AreaScanExtra = 15f;

        /// <summary>Least lot exit scan distance (m).</summary>
        public const float AreaScanMin = 25f;

        /// <summary>Counter episodes (0.4.6): a vehicle's consecutive exit decisions (at most <see cref="Core.SafetyStopEvents.ContinueFrames"/> apart) are one event, per kind.</summary>
        public const int EventKinds = 21;

        /// <summary>0.4.11 (G21): event kind of "held on the road before turning into a building: waits for a gap in the traffic it
        /// crosses" (written every held update; read by other entrants' gap scans: a standing entrant is not present).</summary>
        public const int EntryGapHeldKind = 20;

        /// <summary>0.4.9 (G15): event kind of "held before a box: its way INTO a building is full" (written every held update, read by exits nearby).</summary>
        public const int EnteringHeldKind = 17;

        /// <summary>0.4.9 (G12): event kind of "front passed its sidewalk spot" (one counted crossing per episode).</summary>
        public const int DriveCrossKind = 18;

        /// <summary>0.4.9 (G13): event kind of "held at a driveway exit this update" (read by the same vehicle's lane reservation step).</summary>
        public const int DriveHeldKind = 19;

        /// <summary>0.4.9 (G15): an entering hold counts for exits nearby when it is at most this many frames old (two navigation updates; review F2: was 48).</summary>
        public const uint EnteringHeldWindow = 32;

        /// <summary>0.4.8 (trace): event kind of "the last Go judgement was at a BOXED exit" (so the sidewalk-crossing line says boxed yes even when judged in an earlier update).</summary>
        public const int DriveBoxedKind = 16;

        /// <summary>0.4.8 review (G7 F2): event kind of an exit's last WAIT decision (a Go older than the last Wait is no judgement).</summary>
        public const int ExitWaitKind = 14;

        /// <summary>0.4.8 review (G7 F4): event kind of "judged only on the sidewalk" (could not stop before it; counter episode).</summary>
        public const int LateJudgeKind = 15;

        /// <summary>0.4.8 review (G7 F3): metres of slack past the sidewalk spot a stopped vehicle may have crept and still be held there.</summary>
        public const float SpotSlack = 0.3f;

        /// <summary>
        /// 0.4.8 (G7, owner 17 Nov: an exit straight ACROSS the road into an opposite access): the vehicle uses the lane it
        /// "joins" for less than its own length (+ 2 m) before leaving it again (into the opposite driveway / a junction lane):
        /// it crosses that lane rather than merging, so the lane is busy for the time to CLEAR it with the whole vehicle.
        /// spanOnJoin = metres of its path on the joined lane (-1 = unknown: it stays on the lane, a merge).
        /// </summary>
        public static bool CrossesThrough(float spanOnJoin, float ownLength) => spanOnJoin >= 0f && spanOnJoin < ownLength + 2f;

        /// <summary>Review F2: a recorded Go stands unless a Wait was recorded after it (frames, uint wrap safe).</summary>
        public static bool GoStands(bool waitFound, uint lastWait, uint lastGo) => !waitFound || (int)(lastGo - lastWait) >= 0;

        /// <summary>0.4.8 (diagnostics): event kind of the trace's "held long" line (one per lane stay of 30 s, the game resets its lane timer).</summary>
        public const int LongHoldKind = 12;

        /// <summary>0.4.8 (diagnostics): event kind of "stands on the road after leaving" (counter + trace, one per episode).</summary>
        public const int StuckKind = 13;

        /// <summary>0.4.8 (G7): event kind of "a full Go judgement before the sidewalk" (the frame is read back at the drive entry).</summary>
        public const int DriveGoKind = 8;

        /// <summary>0.4.8 (G7): event kind of "held before the sidewalk" (counter episode).</summary>
        public const int SidewalkHoldKind = 9;

        /// <summary>0.4.8 (G7): event kind of "held: no room on the lane it joins" (counter episode).</summary>
        public const int RoomHoldKind = 10;

        /// <summary>0.4.8 (G7): event kind of the drive exit's trace "waits" line (episode).</summary>
        public const int DriveTraceWaitKind = 11;

        /// <summary>
        /// 0.4.8 (G7): a Go judgement counts for the drive entry when it is at most this many frames old. The vehicle is
        /// re-judged every navigation update (16 frames) while it can still stop before the sidewalk; from its last Go its
        /// front passes the spot and its pivot reaches the drive lane within a few metres. 960 frames = 16 s at 60 frames/s
        /// (a lorry creeping out at 0.5 m/s).
        /// </summary>
        public const uint DriveGoWindow = 960;

        /// <summary>0.4.7: event kind of the exit trace's "starts waiting" line (one per wait episode).</summary>
        public const int TraceWaitKind = 6;

        /// <summary>0.4.7 review (G1 F1): event kind of "placed straight onto a public road lane" (one count per vehicle and episode).</summary>
        public const int RoadStartKind = 7;

        /// <summary>Key of an exit-counter event: the entity index shifted into a per-kind range of the shared event table (kind 0 stays the plain index = the safety stops).</summary>
        public static int EventKey(int entityIndex, int kind) => entityIndex + kind * EventKindStride;

        /// <summary>Entity indices stay below this (entity ids of a city never reach 64 million).</summary>
        public const int EventKindStride = 1 << 26;

        public enum Decision : byte
        {
            /// <summary>May leave.</summary>
            Go = 0,
            /// <summary>Waits for a gap on the lanes it crosses / merges into.</summary>
            WaitGap = 1,
            /// <summary>Gap clear (or not checked), but a yellow box at the exit has no room past it (0.4.3 rule).</summary>
            WaitBox = 2,
            /// <summary>0.4.8 (G7): unboxed exit, gap clear, but no room for the whole vehicle on the lane it joins (a stopped
            /// far-lane queue): it would end up across the lanes it crossed, nose in a lane it cannot join.</summary>
            WaitRoom = 3,
        }

        /// <summary>
        /// 0.4.8 (G7): the whole decision, taken at the spot before the sidewalk. As <see cref="Decide(bool,int,bool,bool,bool,bool)"/>,
        /// plus: with the setting on, an UNBOXED exit (or a boxed one with the box rule off) also needs room for its whole
        /// length on the lane it joins, past the join point (the zebra / box room-past scan). A boxed exit's room past the
        /// band already implies it. On-duty emergency vehicles: only the box rule (stock otherwise).
        /// </summary>
        public static Decision Decide(bool settingOn, int priority, bool gapClear, bool boxRuleOn, bool boxed, bool roomPastBox, bool roomOnJoin)
        {
            Decision d = Decide(settingOn, priority, gapClear, boxRuleOn, boxed, roomPastBox);
            if (d != Decision.Go) return d;
            if (boxRuleOn && boxed) return Decision.Go;
            return Applies(settingOn, priority) && !roomOnJoin ? Decision.WaitRoom : Decision.Go;
        }

        /// <summary>
        /// The owner rule as one decision. The gap check is NOT gated on a box: with the setting on every exit needs a
        /// clear gap; a boxed exit (box rule on) also needs room past the band, as in 0.4.3. Setting off or an on-duty
        /// emergency vehicle: the 0.4.3 behaviour (only the box rule). In the copy the two checks sit where the game
        /// looks at each lane (gap: the exit lane, S2-29 / S2-30; box: the road lane, 0.4.3 S2-28 / S2-30) and the
        /// car's speed limit is the lower of the two holds, which is exactly this decision.
        /// </summary>
        public static Decision Decide(bool settingOn, int priority, bool gapClear, bool boxRuleOn, bool boxed, bool roomPastBox)
        {
            if (Applies(settingOn, priority) && !gapClear) return Decision.WaitGap;
            if (boxRuleOn && boxed && !roomPastBox) return Decision.WaitBox;
            return Decision.Go;
        }
    }
}
