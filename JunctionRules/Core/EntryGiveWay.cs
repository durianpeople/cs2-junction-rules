using System.Runtime.CompilerServices;

namespace JunctionRules.Core
{
    /// <summary>
    /// 0.4.11 (JR-T-017, gap G21, owner rule 17 Nov 2026): a vehicle turning INTO a building across traffic gives way.
    /// Owner: road traffic going straight has priority; a vehicle turning into a building across oncoming traffic (left-hand
    /// traffic: a right turn into the entrance) gives way with the same gap judgement as the whole-junction rule and the exits
    /// (speed and time of the approaching vehicles, its own WHOLE length to clear), waits in its own lane BEFORE any box and
    /// never stops across the oncoming lane; it still needs free room in the entrance (G9 / 0.4.10). A turn in that only
    /// leaves its own lane (left in, left-hand traffic) crosses no road lane: no wait.
    ///
    /// Stock (decompile ~/Code/decomp/yb-2026-11-13/full):
    /// <list type="bullet">
    /// <item>The way in is a node lane OWNED BY THE ROAD EDGE (Game.Net/LaneSystem.cs CreateEdgeConnectionLane 3646+): it starts
    /// on the road lane the vehicle drives (StartNode = PathNode(edge, lane index, b, t2), 3858; its curve starts at
    /// MathUtils.Position(road lane curve, t2), 3794-3803) and runs across the other road lanes to the drive. Flags
    /// Unsafe | SideConnection (3868), TurnLeft / TurnRight by its bend (3873-3880), and NO Yield (only the leaving lane,
    /// isSource, gets Yield, 3869-3872).</item>
    /// <item>Its overlaps with the road lanes it crosses (Game.Net/LaneOverlapSystem.cs CheckOverlaps: computed in an edge only
    /// when one lane is a node lane, 1160-1163) get the Unsafe tie-break: the way in has the lower priority (1386-1389). In
    /// the vehicle's lane speed iterator (CheckOverlappingLanes) that only adds the stock 2 s look-ahead for a road vehicle
    /// already ON the crossed lane (2026-11-13 copy: `curvePosition.y += objectSpeed * 2f / length` when the other's priority
    /// beats num6) and gives way to reservations; a vehicle on the junction lane before the road is not seen at all.</item>
    /// <item>The vehicle also asks for space on it (CarNavigationSystem 920-923: Unsafe and owned by an edge = RequestSpace;
    /// ReserveNavigationLanes 2277-2287 reserves it at priority 96), which only stops road traffic that would block the
    /// drive in a queue (CheckOverlapSpace), never a moving oncoming vehicle.</item>
    /// </list>
    /// Why Junction Rules missed it (0.4.10): the turn-across rule (Core/TurnAcross.cs, S2-13) classifies only lanes of a
    /// JUNCTION: oncoming straight = a Forward lane whose end edge is the turner's start edge; the way in is an edge-owned
    /// Unsafe lane and the crossed road lane has no Forward flag, so its sign status differs (Unsafe) and the rule returns
    /// stock. The whole-path rule (S2-04, BeginLane on any NodeLane) does decide the way in at its first crossing, but with
    /// the time to REACH the conflict, not to CLEAR it with the whole vehicle (the exits' 0.4.8 G7 measure): a van starting
    /// from rest needs ~3 s to clear a 3.5 m lane plus its own 6 m, a car 25 m away at 10 m/s arrives in 2.5 s. Its waiting
    /// spot is that first crossing, INSIDE the box (the 0.4.8 G9 doc: "enters and waits for the oncoming gap inside the box
    /// at the game's own give-way"), with the nose at the oncoming lane. The exit rule's driveway hold (S2-29) acts only on
    /// a leaving lane (SideConnection + Yield). Live 17 Nov 18:59-19:00: van 380934 (to No. 40 Alderwood) logged "stands in
    /// the box 30 s (way in has room: waiting to turn)", then turned across the westbound lane in front of oncoming traffic.
    ///
    /// Now (job: Stage2/JRExitGiveWay.cs EntryRoadHold, reached from the S2-29 call with the ROAD lane, no new copy block):
    /// the vehicle is judged on the road lane it turns from, before its way in: spot 1 = the box line (band entry) when the
    /// way in leaves the road inside a painted exit / entrance box; spot 2 = where the way in leaves its own lane (the drive
    /// start: the vehicle stands wholly in its own lane), used when there is no box or the vehicle could no longer stop
    /// before the box line. Every road car lane the way in crosses (not the lane it leaves, not another drive lane) is judged
    /// like an exit's crossed lane: road vehicles on it and on the lanes feeding it (2 levels), stock reach + speed x own time
    /// to CLEAR the lane with its whole length (Core ExitGiveWay.VehicleBlocks / ClearMetres / ArrivalTime); a vehicle
    /// standing within 10 m of the conflict blocks (the exits' G7 present rule), unless it is itself waiting to turn into a
    /// building (room or gap: no facing-entrances deadlock, the G15 rule). Past both spots (or unable to stop): committed,
    /// the game's own checks finish the turn. On-duty emergency vehicles: stock. Setting: "Vehicles leaving buildings give
    /// way" (0.4.11: also turning in). Game-independent, Burst-compatible, unit-tested (tests G21Tests).
    /// </summary>
    public static class EntryGiveWay
    {
        /// <summary>Game.Net.CarLaneFlags values (Game.Net/CarLaneFlags.cs).</summary>
        public const uint SideConnection = ExitGiveWay.SideConnection, Yield = ExitGiveWay.Yield;

        /// <summary>LaneOverlap flags (Game.Net.OverlapFlags) the conflict test reads.</summary>
        public const int Road = WholePathRule.Road, Water = WholePathRule.Water;

        /// <summary>The lane is a drive's way IN from a road (SideConnection without Yield; the way out has Yield).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDrivewayEntryLane(uint carLaneFlags) => (carLaneFlags & (SideConnection | Yield)) == SideConnection;

        /// <summary>
        /// An overlap of the way in that the give-way judges: a road car lane it CROSSES. Not the road lane it leaves (that
        /// overlap is a merge at its start: OverlapFlags.MergeStart / MergeMiddleStart, WholePathRule.IsYieldChecked false; the
        /// lane is also excluded by entity, isOwnRoadLane), not another drive's way in / out (SideConnection), not water.
        /// </summary>
        public static bool IsCrossedRoad(int overlapFlags, bool otherIsCarLane, bool otherIsSideConnection, bool isOwnRoadLane)
        {
            if ((overlapFlags & Water) != 0 || !otherIsCarLane || otherIsSideConnection || isOwnRoadLane) return false;
            return WholePathRule.IsYieldChecked(overlapFlags, false);
        }

        /// <summary>
        /// 0.4.11 (owner: every building access): a road lane lies between the vehicle's own lane and the building-side end of a
        /// gate / lot / parking way in, by lateral offset across the road at the leave point (metres, positive towards the
        /// building side): crossed when it is clearly beside its own lane (more than ExitGiveWay.JoinTolerance) and nearer
        /// than the building-side end.
        /// </summary>
        public static bool CrossedLateral(float laneLateral, float buildingSideLateral) =>
            laneLateral > ExitGiveWay.JoinTolerance && laneLateral < buildingSideLateral;

        public enum Spot : byte
        {
            /// <summary>Committed: past the drive start or unable to stop before it (the game's own checks).</summary>
            None = 0,
            /// <summary>The painted box line (band entry) on the road lane: waits before the box.</summary>
            BoxLine = 1,
            /// <summary>Where the way in leaves its own lane (no box, or too late for the box line): waits in its own lane.</summary>
            DriveStart = 2,
        }

        /// <summary>
        /// Where the vehicle is judged. boxLineWait / driveStartWait = the game's stop distance (num5 form: metres from its front
        /// minus the stop margin) to the box line and to the drive start; boxed = the way in leaves inside a box on this lane;
        /// inBox = the vehicle already drives inside that band (BoxHold's in-band test); braking = its stock braking distance.
        /// The box line first; the drive start when the box line cannot be held (or there is no box); none = committed.
        /// </summary>
        public static Spot Choose(bool boxed, bool inBox, float boxLineWait, float driveStartWait, float margin, float braking)
        {
            if (boxed && !inBox && ExitGiveWay.CanHold(boxLineWait + ExitGiveWay.SpotSlack, margin, braking)) return Spot.BoxLine;
            if (ExitGiveWay.CanHold(driveStartWait + ExitGiveWay.SpotSlack, margin, braking)) return Spot.DriveStart;
            return Spot.None;
        }

        /// <summary>
        /// One road vehicle against one crossed lane of the way in: the exit judgement (Core ExitGiveWay.VehicleBlocks with the
        /// G15 entrant exception). waitingToTurnIn = it stands before the conflict while waiting to turn into a building (held
        /// for room or, 0.4.11, for a gap): not present, the entrant may go.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool VehicleBlocks(float gap, bool occupies, float speed, float braking, float ownClearTime, bool waitingToTurnIn) =>
            ExitGiveWay.VehicleBlocks(gap, occupies, speed, braking, ownClearTime, waitingToTurnIn);

        /// <summary>
        /// Metres the turning vehicle's front travels until its whole length (trailers incl.) is past a crossed lane: from its
        /// front to the drive start, along the way in to the overlap start, the overlap, its own length (Core ExitGiveWay.ClearMetres).
        /// </summary>
        public static float ClearMetres(float frontToDriveStart, float driveToConflict, float conflictLength, float ownLength) =>
            ExitGiveWay.ClearMetres((frontToDriveStart > 0f ? frontToDriveStart : 0f) + (driveToConflict > 0f ? driveToConflict : 0f), conflictLength, ownLength);

        /// <summary>The setting applies to this vehicle (on, not an on-duty emergency vehicle: stock).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Applies(bool settingOn, int priority) => ExitGiveWay.Applies(settingOn, priority);

        /// <summary>The whole decision: wait for the gap, else go (room into the building is the G9 box hold's own test).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Holds(bool applies, Spot spot, bool gapClear) => applies && spot != Spot.None && !gapClear;
    }
}
