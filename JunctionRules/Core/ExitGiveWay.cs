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

        /// <summary>Most lanes read upstream of one exit (conflict lanes, the junction lanes feeding them, the lanes feeding those).</summary>
        public const int MaxScanLanes = 24;

        /// <summary>Most vehicles read per exit decision. When hit, vehicles not read do not count (as FeedingScan: erring
        /// "busy" could hold the exit for ever); cap hits are counted.</summary>
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
            if (!(speed > 0f)) return false;
            float reach = (braking > 0f ? braking : 0f) + speed * (ArrivalProjection.StockLookAhead + (delta > 0f ? delta : 0f));
            return reach >= gap;
        }

        /// <summary>
        /// Where the leaving vehicle waits: W = metres from its front to the first conflict start, minus the game's stop
        /// margin (CheckOverlappingLanes: 0.9 m, 0.4 m bicycle; origDistance already has it). Null hold when it is
        /// already past W (committed) or cannot stop before it (the game's can-stop test GetBrakingDistance &lt;= distance
        /// + margin); then the game's own checks decide, as for every other committed car.
        /// </summary>
        public static bool CanHold(float waitDistance, float margin, float brakingDistance) =>
            waitDistance >= -WholePathRule.SpotEpsilon && brakingDistance <= waitDistance + margin;

        public enum Decision : byte
        {
            /// <summary>May leave.</summary>
            Go = 0,
            /// <summary>Waits for a gap on the lanes it crosses / merges into.</summary>
            WaitGap = 1,
            /// <summary>Gap clear (or not checked), but a yellow box at the exit has no room past it (0.4.3 rule).</summary>
            WaitBox = 2,
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
