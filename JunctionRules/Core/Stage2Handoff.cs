using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace JunctionRules.Core
{
    /// <summary>
    /// Main-thread handoff of the stage 2 setting into the copy's navigation job, the same pattern as the stage 1
    /// switch: CarDrivingSwitchSystem (registered before the game's navigation slot) publishes the requested mode once
    /// per simulation step; the copy's OnUpdate (main thread, later in the same step) reads it once and copies it by
    /// value into the job struct, so a running job never sees a change. A change therefore applies at the next step in
    /// which the copy drives. Volatile access keeps it correct even if a UI thread wrote the setting.
    /// </summary>
    public static class Stage2Handoff
    {
        private static int s_Requested;

        public static int Requested => Volatile.Read(ref s_Requested);

        public static void Publish(int mode) => Volatile.Write(ref s_Requested, mode);
    }

    /// <summary>
    /// 0.4.7 (G1): setting "Exit trace (log)", published by Systems/CarDrivingSwitchSystem.cs each step and taken by
    /// Stage2/JRStage2Bridge.cs in the copy's OnUpdate (S2-23), the same path as <see cref="Stage2Handoff"/>.
    /// </summary>
    public static class ExitTraceHandoff
    {
        private static int s_On;

        public static bool Requested => Volatile.Read(ref s_On) != 0;

        public static void Publish(bool on) => Volatile.Write(ref s_On, on ? 1 : 0);
    }

    /// <summary>Logs a change of the requested rule once (the switch system calls it each step).</summary>
    public sealed class GapRuleSwitch
    {
        private bool m_Has;
        private int m_Mode;

        /// <summary>Returns a log line when the requested mode changed (or on the first call), else null.</summary>
        public string Step(JunctionGapRule rule, bool counting, out int mode, JunctionWaitingSpot spot = JunctionWaitingSpot.BeforeFirstCrossing,
            bool turnGivesWay = false, bool leftHandTraffic = false, bool noPushPeople = false, bool zebraClear = false,
            float zebraMargin = RoomScan.DefaultMargin, bool boxObey = false, bool boxTurnWait = true, bool anyJunctionBox = false,
            bool anyEntranceBox = false, float entranceLength = 8f, bool exitGiveWay = false, bool unmarkedGiveWay = false)
        {
            // 0.4.2 (JR-T-010): the yellow box bits (traffic side from the same leftHandTraffic the turn rule reads).
            int boxBits = Stage2Mode.BoxBits(boxObey, boxTurnWait, leftHandTraffic, anyJunctionBox, anyEntranceBox, entranceLength);
            mode = Stage2Mode.For(rule, counting, spot, turnGivesWay, leftHandTraffic, noPushPeople, zebraClear, zebraMargin, boxBits, exitGiveWay, unmarkedGiveWay);
            if (m_Has && mode == m_Mode) return null;
            bool first = !m_Has;
            bool ruleChanged = !first && ((mode ^ m_Mode) & (Stage2Mode.Rule | Stage2Mode.AtLine | Stage2Mode.TurnGivesWay | Stage2Mode.LeftHandTraffic | Stage2Mode.NoPushPeople | Stage2Mode.ZebraClear | Stage2Mode.ZebraMarginMask
                | Stage2Mode.BoxObey | Stage2Mode.BoxTurnWait | Stage2Mode.BoxLeftHand | Stage2Mode.BoxEntranceMask | Stage2Mode.ExitGiveWay
                | Stage2Mode.UnmarkedGiveWay)) != 0;
            bool boxesChanged = !first && ((mode ^ m_Mode) & (Stage2Mode.BoxJunctions | Stage2Mode.BoxEntrances)) != 0;
            m_Has = true;
            int old = m_Mode;
            m_Mode = mode;
            if (first) return $"junction gap rule starts as {Stage2Mode.RuleName(mode)} (acts only while the Junction Rules copy drives), counters {((mode & Stage2Mode.Count) != 0 ? "on" : "off")}{BoxesPresent(mode)}";
            if (ruleChanged) return $"junction gap rule switched to {Stage2Mode.RuleName(mode)} (applies at the next step in which the Junction Rules copy drives){BoxesPresent(mode)}";
            if (boxesChanged && ((mode ^ old) & Stage2Mode.Count) == 0) return "yellow boxes in the city changed" + BoxesPresent(mode);
            return (mode & Stage2Mode.Count) != 0 && (old & Stage2Mode.Count) == 0 ? "junction gap counters on" : "junction gap counters off";
        }

        /// <summary>0.4.2: which box kinds the driving rule sees (only with "Cars obey yellow boxes" on).</summary>
        private static string BoxesPresent(int mode) =>
            (mode & Stage2Mode.BoxObey) == 0 ? ""
            : "; yellow boxes present: junctions " + ((mode & Stage2Mode.BoxJunctions) != 0 ? "yes" : "no") + ", entrances " + ((mode & Stage2Mode.BoxEntrances) != 0 ? "yes" : "no");
    }

    /// <summary>Indices and the log text of the debug counters (counted in car navigation updates, "car-steps").</summary>
    public static class Stage2Counters
    {
        /// <summary>Car-steps in which a car still before its entry spot took the decision for its whole path (rule on only).</summary>
        public const int Decisions = 0;
        /// <summary>Car-steps in which a car stood at its entry spot because a later conflict (not the first point) was busy (rule on only).</summary>
        public const int WaitedForLater = 1;
        /// <summary>Car-steps in which a car stood inside the junction (front past its waiting spot) held by a give-way (both rules).</summary>
        public const int StoppedInsideYield = 2;
        /// <summary>Car-steps in which a car stood inside the junction behind a car crossing its path (collision braking, both rules).</summary>
        public const int StoppedInsideCrossing = 3;
        /// <summary>Car-steps in which a safety stop inside the junction limited the car (braking for it or standing at it; rule on only, 0.2.1).</summary>
        public const int SafetyStops = 4;
        /// <summary>0.2.3: safety-stop EVENTS, a car's transition into a safety stop counted once (Core/ArrivalTime.cs SafetyStopEvents).</summary>
        public const int SafetyStopEvents = 5;
        /// <summary>0.2.3: car-steps in which a car on a conflicting junction lane counted as busy only because of the arrival-time projection (stock 2 s look-ahead alone: clear).</summary>
        public const int ProjectedLaneHits = 6;
        /// <summary>0.2.3: car-steps in which a car on a lane feeding a conflicting junction lane blocked a point as a virtual reservation.</summary>
        public const int FeedingLaneHits = 7;
        /// <summary>0.2.3: conflict points sampled at the entry spot (car standing there, rule on): one per point per car-step.</summary>
        public const int PointSamples = 8;
        /// <summary>0.2.3: sum of the sampled points' distance from W, in centimetres.</summary>
        public const int PointDistanceSumCm = 9;
        /// <summary>0.2.3: sum of the sampled points' projected own arrival time, in milliseconds.</summary>
        public const int ArrivalSumMs = 10;
        /// <summary>0.2.3: first of PointBuckets.Count histogram slots for the distance from W.</summary>
        public const int DistanceHistogram = 11;
        /// <summary>0.2.3: first of PointBuckets.Count histogram slots for the projected own arrival time.</summary>
        public const int ArrivalHistogram = DistanceHistogram + PointBuckets.Count;
        /// <summary>0.2.4: feeding-lane scans run (a source's lanes read), summed over car-steps.</summary>
        public const int FeedingScans = ArrivalHistogram + PointBuckets.Count;
        /// <summary>0.2.4: feeding-lane scans skipped because the car was a follower (a car ahead of it before W).</summary>
        public const int FeedingFollowerSkips = FeedingScans + 1;
        /// <summary>0.2.4: feeding-lane scans skipped by the per-car-update memo (same source already read, nothing there can count).</summary>
        public const int FeedingMemoSkips = FeedingScans + 2;
        /// <summary>0.2.4: feeding-lane scans stopped by the work cap (cars not read are not counted, as stock).</summary>
        public const int FeedingCapHits = FeedingScans + 3;
        /// <summary>0.2.4: overlaps where "turning across oncoming gives way" changed the stock priority (num6), car-steps x overlaps.</summary>
        public const int TurnOverrides = FeedingScans + 4;
        /// <summary>0.2.4: give-ways a straight car did NOT make for a turner's reservation alone (the turner gives way).</summary>
        public const int TurnReservationDiscounts = FeedingScans + 5;
        /// <summary>0.3.2: car-steps in which a creep through people or a cyclist on a crossing was refused (any reason below; setting "Cars never push through people on crossings").</summary>
        public const int CrossingRefused = FeedingScans + 6;
        /// <summary>0.3.2: ... because of the game's 5 s PushBlockers timer (the owner's "waits a few seconds, then drives through").</summary>
        public const int CrossingRefusedTimer = FeedingScans + 7;
        /// <summary>0.3.2: ... because the person stood still (no Moving component or slower than 0.1 m/s; the game creeps at once).</summary>
        public const int CrossingRefusedStanding = FeedingScans + 8;
        /// <summary>0.3.2: ... because the car's ignored blocker (IgnoreBlocker creep) was a cyclist on a crossing.</summary>
        public const int CrossingRefusedCyclist = FeedingScans + 9;
        /// <summary>0.3.3 (JR-T-005): feeding scans not made because the conflicting lane is Unsafe (car-steps with at least one).</summary>
        public const int FeedingUnsafeSkips = FeedingScans + 10;
        /// <summary>0.3.3 (JR-T-006): car-steps where a crossing after W was left to the game's own check at the crossing (car fits).</summary>
        public const int CrossingsAtCrossing = FeedingScans + 11;
        /// <summary>0.3.3 (JR-T-006): car-steps where a crossing after W stayed in the decision at W (the vehicle does not fit before it).</summary>
        public const int CrossingsNoFit = FeedingScans + 12;
        /// <summary>0.4.1 (JR-T-009): car-steps in which a car stood before a zebra held by people on it (setting "Cars never stop on a zebra").</summary>
        public const int ZebraHeldPeople = FeedingScans + 13;
        /// <summary>0.4.1: car-steps in which a car stood before a zebra because a vehicle point after it in its section was not clear (Whole junction).</summary>
        public const int ZebraHeldCs = FeedingScans + 14;
        /// <summary>0.4.1: car-steps in which a car stood before a zebra because there was no room for it past the section.</summary>
        public const int ZebraHeldRoom = FeedingScans + 15;
        /// <summary>0.4.1: car-steps in which a zebra lane had a zebra merged into the section before it (the vehicle does not fit before it).</summary>
        public const int ZebraMerged = FeedingScans + 16;
        /// <summary>0.4.1 (JR-T-009-r): car-steps in which a zebra section was checked early at the previous spot (people + room), the car cannot stop before the zebra after that spot.</summary>
        public const int ZebraEarly = FeedingScans + 17;
        /// <summary>0.4.1 (JR-T-009-r): car-steps in which the room scan met a further zebra (another node, e.g. mid-block) inside its window: room needed past it too, people on it = no room.</summary>
        public const int ZebraFurther = FeedingScans + 18;
        /// <summary>0.4.1 (JR-T-009-r, probe): car-steps in which the room scan read a lane whose LaneObject buffer was not in curve-position order (expected 0).</summary>
        public const int ZebraScanUnsorted = FeedingScans + 19;
        /// <summary>0.4.2 (JR-T-010): car-steps in which a car stood before a boxed junction because there was no room for it past the box (setting "Cars obey yellow boxes").</summary>
        public const int BoxHeldRoom = FeedingScans + 20;
        /// <summary>0.4.2: car-steps in which a car turning across oncoming traffic stood inside a box at its turn spot (the UK right-turn exception; Whole junction).</summary>
        public const int BoxTurnWaits = FeedingScans + 21;
        /// <summary>0.4.2: car-steps in which a main-road car stood before an entrance band because there was no room for it past the band.</summary>
        public const int EntranceHeldMain = FeedingScans + 22;
        /// <summary>0.4.2: car-steps in which a car leaving a driveway stood at the road because there was no room for it past the band on its target lane.</summary>
        public const int EntranceHeldLeaving = FeedingScans + 23;
        /// <summary>0.4.4 (JR-T-012): exit events (0.4.6: one per vehicle episode, not per car-step) in which a vehicle leaving a building stood at its exit waiting for a gap (setting "Vehicles leaving buildings give way").</summary>
        public const int ExitHeld = FeedingScans + 24;
        /// <summary>0.4.4: ... of those, a driveway turn across the near traffic (left-hand traffic: right turn; right-hand: left turn; the game's flag).</summary>
        public const int ExitHeldAcross = FeedingScans + 25;
        /// <summary>0.4.4: ... of those, at a spawn location / car park gate (the vehicle waits inside the building, unseen).</summary>
        public const int ExitHeldSpawn = FeedingScans + 26;
        /// <summary>0.4.4: car-steps in which a vehicle at its exit found every crossed / merged lane clear (go decisions).</summary>
        public const int ExitGo = FeedingScans + 27;
        /// <summary>0.4.4: car-steps in which an on-duty emergency vehicle left a building without the give-way (stock).</summary>
        public const int ExitEmergencyPass = FeedingScans + 28;
        /// <summary>0.4.4: car-steps in which a vehicle at a boxed spawn exit had its gap but no room past the band (0.4.3 box rule).</summary>
        public const int ExitSpawnBoxHeld = FeedingScans + 29;
        /// <summary>0.4.4: exit scans stopped by the work cap (0.4.7: lane or vehicle cap; split in ExitCapLanes / ExitCapVehicles).</summary>
        public const int ExitCapHits = FeedingScans + 30;
        /// <summary>0.4.6 (probe): lane hand-overs of a vehicle from a lot's AREA lane to a public road lane (one per hand-over; each is judged by the exit rule just before it).</summary>
        public const int ExitJoinArea = FeedingScans + 31;
        /// <summary>0.4.6 (probe): ... from a building Connection lane (spawn location / car park gate) to a public road lane.</summary>
        public const int ExitJoinConnection = FeedingScans + 32;
        /// <summary>0.4.6 (probe): ... from any other building-owned lane to a public road lane (expected 0: a driveway goes through its SideConnection lane; a nonzero count is an ungated way out of a building).</summary>
        public const int ExitJoinOther = FeedingScans + 33;
        /// <summary>0.4.7 (G1): hand-overs from a building's spawn location object (TransformTarget; the 0.4.6 'other', judged by S2-35 since 0.4.7).</summary>
        public const int ExitJoinSpawnPoint = FeedingScans + 34;
        /// <summary>0.4.7: ... from a building-owned parking lane (ParkingSpace; S2-35).</summary>
        public const int ExitJoinParking = FeedingScans + 35;
        /// <summary>0.4.7: ... from a kerbside parking lane of the road (ParkingSpace, not building-owned; S2-35, "give way when moving off").</summary>
        public const int ExitJoinKerbParking = FeedingScans + 36;
        /// <summary>0.4.7: ... from a car lane of a building's own road into a public junction lane (S2-36).</summary>
        public const int ExitJoinBuildingLane = FeedingScans + 37;
        /// <summary>0.4.7: ... from a driveway's SideConnection lane (judged by S2-29 before the hand-over; counted only).</summary>
        public const int ExitJoinDrive = FeedingScans + 38;
        /// <summary>0.4.7: hand-overs of a path the rule judges (lot, gate, spawn point, parking, building road) with no exit decision in that update (expected 0; on-duty emergency vehicles not included).</summary>
        public const int ExitJoinUnjudged = FeedingScans + 39;
        /// <summary>0.4.7: exit decisions that hit the lane cap (far feeding lanes not read; errs to go).</summary>
        public const int ExitCapLanes = FeedingScans + 40;
        /// <summary>0.4.7: exit decisions that hit the vehicle cap (dense traffic; counts as busy: wait).</summary>
        public const int ExitCapVehicles = FeedingScans + 41;
        /// <summary>JR-T-013b (G3): car-steps in which a car turning across oncoming traffic stood at its turn spot inside a box because its exit had no room (room test re-run at the turn spot).</summary>
        public const int BoxTurnRoomWaits = FeedingScans + 42;
        /// <summary>
        /// 0.4.7 review (G1 F1): vehicles that appear directly in a public road lane, not through a building lane / spawn object
        /// / parking space (unspawned, current lane a public road car lane, no Connection / ResetSpeed flag): a path that starts
        /// on the road lane itself (PathfindTargetSeeker cargo spawn branch 610-613 / 660-668, the front-connection fallback
        /// AddEdgeTargets(Building.m_RoadEdge)). Not judged by the exit rule (counted only); one per vehicle and episode.
        /// </summary>
        public const int ExitRoadStart = FeedingScans + 43;
        /// <summary>0.4.8 (G7): exit vehicles standing at their spot before the sidewalk (every hold of the exit rule: gap, box, room; events per vehicle).</summary>
        public const int ExitHeldBeforeSidewalk = FeedingScans + 44;
        /// <summary>0.4.8 (G7): of those, unboxed exits held because the lane they join has no room for the whole vehicle (events).</summary>
        public const int ExitHeldRoom = FeedingScans + 45;
        /// <summary>0.4.8 (G7): driveway exits crossing the sidewalk. 0.4.9 (G12): counted where the FRONT passes the sidewalk spot (0.4.8: at the pivot hand-over onto the drive lane, often while the vehicle still waited before the sidewalk).</summary>
        public const int ExitDriveEntries = FeedingScans + 46;
        /// <summary>0.4.8 (G7): of those, crossed with no Go standing and no decision at the road lane either. Must be 0.</summary>
        public const int ExitDriveUnjudged = FeedingScans + 47;
        /// <summary>0.4.8 (G7): vehicles standing 5 s+ on a driveway's leaving lane with the front past the sidewalk spot (0.4.9: only those; 0.4.8 also counted vehicles queued on a long drive lane behind a held one). Expected ~0.</summary>
        public const int ExitStuckOnRoad = FeedingScans + 48;
        /// <summary>JR-T-014c (G9): car-steps in which a vehicle turning INTO a building stood before the box (entrance / two-way drive) because there was no room for its whole length past the box on its way in.</summary>
        public const int EntranceHeldEntering = FeedingScans + 49;
        /// <summary>0.4.8 review (G7 F4): driveway exits judged only at the first road lane (could not stop before the sidewalk; episodes). 0.4.9 (G12): only crossings with no Go standing (0.4.8 also counted every Go'd exit re-judged after it passed the spot).</summary>
        public const int ExitDriveLate = FeedingScans + 50;
        /// <summary>0.4.9 (G15): car-steps in which an exit did not wait for a vehicle standing next to it that waits for room INTO a building.</summary>
        public const int ExitEntrantIgnored = FeedingScans + 51;
        /// <summary>0.4.9 (G13): car-steps in which a vehicle held at a driveway exit did not ask road traffic for space (the game's keep-clear request withdrawn).</summary>
        public const int ExitKeepClearWithdrawn = FeedingScans + 52;
        /// <summary>
        /// 0.4.10 (G19, probe): car-steps in which a yellow box hold (junction box, exit / entrance band, the right-turner's
        /// turn-spot re-check, a vehicle leaving into a box) bound only because room must now be FREE NOW: the vehicle that
        /// blocked had room with its moving braking-distance credit (0.4.9 would have let the car in). Shows what the
        /// free-now rule costs through traffic. An upper bound (review F6): only the first blocker's credit is tested.
        /// </summary>
        public const int BoxHeldFreeNow = FeedingScans + 53;
        /// <summary>
        /// 0.4.10 (owner: "if the car can't clear the whole thing, the car should not enter the box"): car-steps in which a
        /// standing car has its NOSE in a box: front bumper past the painted box line, rear still before it
        /// (BoxDriving.NoseIn; junction / exit / entrance boxes; not a right-turner at its spot). MUST stay 0.
        /// </summary>
        public const int BoxNosePast = FeedingScans + 54;
        /// <summary>
        /// 0.4.10 (probe): car-steps in which a STANDING car has a part of its body inside a box (junction box: on its lanes;
        /// exit / entrance box: front past the near edge, rear not past the far edge), not counting a right-turner at its
        /// turn spot (the UK exception). What the owner sees as "a car stopped in the box"; expected ~0 (a light that turned
        /// amber / red after the car entered on green, a vehicle that cut into the room past the box).
        /// </summary>
        public const int BoxStoodInside = FeedingScans + 55;
        public const int Count = FeedingScans + 56;

        /// <summary>Speed (m/s) under which a car counts as standing.</summary>
        public const float StandingSpeed = 0.5f;

        /// <summary>Tolerance when deciding that a recorded braking speed is the one that limits the car.</summary>
        public const float BindingTolerance = 0.01f;

        /// <summary>The recorded limit is the car's binding limit and the car stands.</summary>
        public static bool IsStandingOn(bool recorded, float recordedSpeed, float finalMaxSpeed, float currentSpeed) =>
            recorded && recordedSpeed <= finalMaxSpeed + BindingTolerance && currentSpeed < StandingSpeed;

        /// <summary>The recorded limit is the car's binding limit (moving or standing).</summary>
        public static bool IsBinding(bool recorded, float recordedSpeed, float finalMaxSpeed) =>
            recorded && recordedSpeed <= finalMaxSpeed + BindingTolerance;

        /// <summary>Index of the bucket holding the median sample of a histogram (-1 when empty).</summary>
        public static int MedianBucket(long[] values, int first, int count)
        {
            long total = 0;
            for (int i = 0; i < count; i++) total += values[first + i];
            if (total <= 0) return -1;
            long half = (total + 1) / 2, run = 0;
            for (int i = 0; i < count; i++)
            {
                run += values[first + i];
                if (run >= half) return i;
            }
            return count - 1;
        }

        private static string Histogram(long[] d, int first, bool distance, CultureInfo ci)
        {
            var sb = new System.Text.StringBuilder();
            float lo = 0f;
            for (int i = 0; i < PointBuckets.Count; i++)
            {
                float hi = distance ? PointBuckets.DistanceEdge(i) : PointBuckets.ArrivalEdge(i);
                if (i > 0) sb.Append(' ');
                sb.Append(lo.ToString(ci)).Append(float.IsPositiveInfinity(hi) ? "+" : "-" + hi.ToString(ci)).Append(':').Append(d[first + i].ToString(ci));
                lo = hi;
            }
            int m = MedianBucket(d, first, PointBuckets.Count);
            if (m >= 0)
            {
                float mlo = m == 0 ? 0f : distance ? PointBuckets.DistanceEdge(m - 1) : PointBuckets.ArrivalEdge(m - 1);
                float mhi = distance ? PointBuckets.DistanceEdge(m) : PointBuckets.ArrivalEdge(m);
                sb.Append(" (median in ").Append(mlo.ToString(ci)).Append(float.IsPositiveInfinity(mhi) ? "+" : "-" + mhi.ToString(ci)).Append(')');
            }
            return sb.ToString();
        }

        /// <summary>
        /// The suffix of the copy's timing line. ruleMask: bit 0 = a step ran with stock, bit 1 = with whole junction
        /// (both = the rule was switched inside the window). deltas: null when counters were not on for the whole window.
        /// pushMask (0.3.2), same encoding for the setting "Cars never push through people on crossings" (bit 0 = a step ran
        /// with it off, bit 1 = on); 0 = not reported.
        /// </summary>
        public static string Format(int ruleMask, long[] deltas, int pushMask = 0, int zebraMask = 0, int boxMask = 0, int exitMask = 0)
        {
            string rule = ruleMask == 1 ? "stock" : ruleMask == 2 ? "whole junction" : ruleMask == 3 ? "mixed (switched in this window)" : "none";
            string push = pushMask == 1 ? "off" : pushMask == 2 ? "on" : pushMask == 3 ? "switched in this window" : null;
            string pushPart = push == null ? "" : "; never push through people on crossings " + push;
            // 0.4.1 (JR-T-009): setting "Cars never stop on a zebra", same encoding.
            string zebra = zebraMask == 1 ? "off" : zebraMask == 2 ? "on" : zebraMask == 3 ? "switched in this window" : null;
            if (zebra != null) pushPart += "; never stop on a zebra " + zebra;
            // 0.4.2 (JR-T-010): setting "Cars obey yellow boxes", same encoding.
            string box = boxMask == 1 ? "off" : boxMask == 2 ? "on" : boxMask == 3 ? "switched in this window" : null;
            if (box != null) pushPart += "; obey yellow boxes " + box;
            // 0.4.4 (JR-T-012): setting "Vehicles leaving buildings give way", same encoding.
            string exit = exitMask == 1 ? "off" : exitMask == 2 ? "on" : exitMask == 3 ? "switched in this window" : null;
            if (exit != null) pushPart += "; vehicles leaving buildings give way " + exit;
            if (deltas == null || deltas.Length < Count) return "; junction gap rule " + rule + pushPart;
            var ci = CultureInfo.InvariantCulture;
            long n = deltas[PointSamples];
            string points = n <= 0 ? "points at W 0"
                : "points at W " + n.ToString(ci) +
                  ", distance from W mean " + (deltas[PointDistanceSumCm] / 100.0 / n).ToString("0.0", ci) + " m [" + Histogram(deltas, DistanceHistogram, true, ci) + "]" +
                  ", own arrival mean " + (deltas[ArrivalSumMs] / 1000.0 / n).ToString("0.00", ci) + " s [" + Histogram(deltas, ArrivalHistogram, false, ci) + "]";
            return "; junction gap rule " + rule +
                   "; car-steps: whole-path decisions " + deltas[Decisions].ToString(ci) +
                   ", waited at entry spot for a later conflict " + deltas[WaitedForLater].ToString(ci) +
                   ", safety stops inside " + deltas[SafetyStops].ToString(ci) +
                   " (events " + deltas[SafetyStopEvents].ToString(ci) + ")" +
                   ", stopped inside for a give-way " + deltas[StoppedInsideYield].ToString(ci) +
                   ", stopped inside behind a crossing car " + deltas[StoppedInsideCrossing].ToString(ci) +
                   "; arrival projection: busy only by projection, junction-lane car " + deltas[ProjectedLaneHits].ToString(ci) +
                   ", feeding-lane car " + deltas[FeedingLaneHits].ToString(ci) +
                   "; feeding scans " + deltas[FeedingScans].ToString(ci) +
                   " (skipped: follower " + deltas[FeedingFollowerSkips].ToString(ci) +
                   ", memo " + deltas[FeedingMemoSkips].ToString(ci) +
                   "; cap hits " + deltas[FeedingCapHits].ToString(ci) +
                   "; unsafe lane " + deltas[FeedingUnsafeSkips].ToString(ci) + ")" +
                   "; turning across oncoming: priority changed " + deltas[TurnOverrides].ToString(ci) +
                   ", turner reservation not waited for " + deltas[TurnReservationDiscounts].ToString(ci) +
                   pushPart +
                   "; crossing give-ups refused " + deltas[CrossingRefused].ToString(ci) +
                   " (5 s push " + deltas[CrossingRefusedTimer].ToString(ci) +
                   ", standing person " + deltas[CrossingRefusedStanding].ToString(ci) +
                   ", cyclist " + deltas[CrossingRefusedCyclist].ToString(ci) + ")" +
                   "; crossings after W judged at the crossing " + deltas[CrossingsAtCrossing].ToString(ci) +
                   ", kept at W (vehicle does not fit) " + deltas[CrossingsNoFit].ToString(ci) +
                   (zebraMask >= 2
                       ? "; zebra entries held: people " + deltas[ZebraHeldPeople].ToString(ci) +
                         ", Cs after " + deltas[ZebraHeldCs].ToString(ci) +
                         ", no room past " + deltas[ZebraHeldRoom].ToString(ci) +
                         " (zebra sections merged, vehicle does not fit between: " + deltas[ZebraMerged].ToString(ci) +
                         "; room/people checked early at previous spot (cannot stop before zebra): " + deltas[ZebraEarly].ToString(ci) + ")" +
                         "; room scan: further zebra in reach " + deltas[ZebraFurther].ToString(ci) +
                         ", lane objects out of curve order " + deltas[ZebraScanUnsorted].ToString(ci)
                       : "") +
                   (boxMask >= 2
                       ? "; box entries held " + deltas[BoxHeldRoom].ToString(ci) +
                         ", right-turn waits " + deltas[BoxTurnWaits].ToString(ci) +
                         "; entrance band holds: main road " + deltas[EntranceHeldMain].ToString(ci) +
                         ", leaving driveway " + deltas[EntranceHeldLeaving].ToString(ci) +
                         "; entrance box entries held (entrance full) " + deltas[EntranceHeldEntering].ToString(ci)
                       : "") +
                   (exitMask >= 2
                       ? "; building exits: waits " + deltas[ExitHeld].ToString(ci) +
                         " (turning across " + deltas[ExitHeldAcross].ToString(ci) +
                         ", spawn/gate " + deltas[ExitHeldSpawn].ToString(ci) + ")" +
                         ", go decisions " + deltas[ExitGo].ToString(ci) +
                         ", emergency passthroughs " + deltas[ExitEmergencyPass].ToString(ci) +
                         ", boxed spawn exit no room " + deltas[ExitSpawnBoxHeld].ToString(ci) +
                         ", cap hits " + deltas[ExitCapHits].ToString(ci) +
                         ", hand-overs to the road from: lot " + deltas[ExitJoinArea].ToString(ci) +
                         ", gate " + deltas[ExitJoinConnection].ToString(ci) +
                         ", other " + deltas[ExitJoinOther].ToString(ci) +
                         ", spawn point " + deltas[ExitJoinSpawnPoint].ToString(ci) +
                         ", building parking " + deltas[ExitJoinParking].ToString(ci) +
                         ", kerbside parking " + deltas[ExitJoinKerbParking].ToString(ci) +
                         ", building road " + deltas[ExitJoinBuildingLane].ToString(ci) +
                         ", drive " + deltas[ExitJoinDrive].ToString(ci) +
                         "; hand-overs not judged " + deltas[ExitJoinUnjudged].ToString(ci) +
                         "; cap hits by kind: lanes " + deltas[ExitCapLanes].ToString(ci) +
                         " (go), vehicles " + deltas[ExitCapVehicles].ToString(ci) + " (wait)"
                       : "") +
                   // JR-T-013b (G3): newest counter, at the end of the text (before the points).
                   (boxMask >= 2 ? "; right-turn waits for exit room " + deltas[BoxTurnRoomWaits].ToString(ci) : "") +
                   // 0.4.7 review (G1 F1): newest counter, after the G3 one.
                   (exitMask >= 2 ? "; placed straight onto a road lane (not judged) " + deltas[ExitRoadStart].ToString(ci) : "") +
                   // 0.4.8 (G7): the sidewalk rule's proof counters, newest at the end.
                   (exitMask >= 2
                       ? "; exits held before the sidewalk " + deltas[ExitHeldBeforeSidewalk].ToString(ci) +
                         " (no room on the lane it joins " + deltas[ExitHeldRoom].ToString(ci) + ")" +
                         "; driveway exits crossing the sidewalk " + deltas[ExitDriveEntries].ToString(ci) +
                         ", of them without a full judgement " + deltas[ExitDriveUnjudged].ToString(ci) + " (must be 0)" +
                         "; standing on the road after leaving a driveway (5 s+) " + deltas[ExitStuckOnRoad].ToString(ci) +
                         "; judged only on the sidewalk (could not stop before it) " + deltas[ExitDriveLate].ToString(ci)
                       : "") +
                   // 0.4.9 (G15 / G13): newest, at the end.
                   (exitMask >= 2
                       ? "; exits not waiting for a vehicle that waits to turn in " + deltas[ExitEntrantIgnored].ToString(ci) +
                         "; held exits' keep-clear requests withdrawn " + deltas[ExitKeepClearWithdrawn].ToString(ci)
                       : "") +
                   // 0.4.10 (G19): newest, at the end.
                   (boxMask >= 2 ? "; box holds only because the queue past the box was moving (free-now rule) " + deltas[BoxHeldFreeNow].ToString(ci)
                       + "; standing with the front bumper past a box line (nose in the box) " + deltas[BoxNosePast].ToString(ci) + " (must be 0)"
                       + "; standing inside a box (not at the right-turn spot) " + deltas[BoxStoodInside].ToString(ci) : "") +
                   "; " + points;
        }
    }

    /// <summary>
    /// 0.4.2 (JR-T-010-r): the boxed exits for the driving rule, published on the main thread by
    /// Systems/CarDrivingSwitchSystem.cs (which may not name copy types) and taken by Stage2/JRStage2Bridge.cs in the
    /// copy's OnUpdate (S2-23), the same hand-over path as <see cref="Stage2Handoff"/>. Main thread only.
    /// 0.4.3: one entry per boxed CAR EXIT (Core/ExitBands.cs: edge key + curve position), sorted by edge then t; an
    /// edge may repeat (several exits on one road). 0.4.2 published one edge key per boxed building.
    /// </summary>
    public static class BoxEdgeHandoff
    {
        private static long[] s_Keys = new long[0];
        private static float[] s_T = new float[0];
        private static uint[] s_Masks = new uint[0];
        private static float[] s_Window = new float[0];

        /// <summary>Bumped on every publish that changes the bands.</summary>
        public static int Version { get; private set; }

        /// <summary>Edge keys, ascending (repeats allowed).</summary>
        public static long[] Keys => s_Keys;

        /// <summary>Curve position on the edge, parallel to <see cref="Keys"/>.</summary>
        public static float[] T => s_T;

        /// <summary>0.4.4: carriageway mask per band (ExitBand.Carriageways), parallel to <see cref="Keys"/>.</summary>
        public static uint[] Masks => s_Masks;

        /// <summary>
        /// 0.4.10 (G18): the painted window per band, 4 floats each (x0, z0, x1, z1: its end points on the edge's centre
        /// curve), parallel to <see cref="Keys"/>; NaN x0 = no window (not driven).
        /// </summary>
        public static float[] Window => s_Window;

        /// <summary>Exits not handed over at the last publish (more than ExitBands.Capacity).</summary>
        public static int Dropped { get; private set; }

        /// <summary>Publishes the bands (any order, duplicates allowed); normalised here. No change = no new version.</summary>
        public static void Publish(IEnumerable<ExitBand> bands)
        {
            ExitBand[] n = ExitBands.Normalise(bands, ExitBands.Capacity, out int dropped);
            Dropped = dropped;
            if (n.Length == s_Keys.Length)
            {
                bool same = true;
                for (int i = 0; i < n.Length && same; i++)
                    same = n[i].EdgeKey == s_Keys[i] && n[i].T == s_T[i] && n[i].Carriageways == s_Masks[i] && SameWindow(n[i], s_Window, i);
                if (same) return;
            }
            var keys = new long[n.Length];
            var t = new float[n.Length];
            var masks = new uint[n.Length];
            var window = new float[n.Length * 4];
            for (int i = 0; i < n.Length; i++)
            {
                keys[i] = n[i].EdgeKey; t[i] = n[i].T; masks[i] = n[i].Carriageways;
                window[4 * i] = n[i].HasWindow ? n[i].X0 : float.NaN;
                window[4 * i + 1] = n[i].Z0;
                window[4 * i + 2] = n[i].X1;
                window[4 * i + 3] = n[i].Z1;
            }
            s_Keys = keys;
            s_T = t;
            s_Masks = masks;
            s_Window = window;
            Version++;
        }

        private static bool SameWindow(ExitBand b, float[] w, int i)
        {
            if (w == null || w.Length < 4 * i + 4) return false;
            if (!b.HasWindow) return float.IsNaN(w[4 * i]);
            return w[4 * i] == b.X0 && w[4 * i + 1] == b.Z0 && w[4 * i + 2] == b.X1 && w[4 * i + 3] == b.Z1;
        }
    }
}
