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

    /// <summary>Logs a change of the requested rule once (the switch system calls it each step).</summary>
    public sealed class GapRuleSwitch
    {
        private bool m_Has;
        private int m_Mode;

        /// <summary>Returns a log line when the requested mode changed (or on the first call), else null.</summary>
        public string Step(JunctionGapRule rule, bool counting, out int mode, JunctionWaitingSpot spot = JunctionWaitingSpot.BeforeFirstCrossing,
            bool turnGivesWay = false, bool leftHandTraffic = false, bool noPushPeople = false, bool zebraClear = false,
            float zebraMargin = RoomScan.DefaultMargin, bool boxObey = false, bool boxTurnWait = true, bool anyJunctionBox = false,
            bool anyEntranceBox = false, float entranceLength = 8f, bool exitGiveWay = false)
        {
            // 0.4.2 (JR-T-010): the yellow box bits (traffic side from the same leftHandTraffic the turn rule reads).
            int boxBits = Stage2Mode.BoxBits(boxObey, boxTurnWait, leftHandTraffic, anyJunctionBox, anyEntranceBox, entranceLength);
            mode = Stage2Mode.For(rule, counting, spot, turnGivesWay, leftHandTraffic, noPushPeople, zebraClear, zebraMargin, boxBits, exitGiveWay);
            if (m_Has && mode == m_Mode) return null;
            bool first = !m_Has;
            bool ruleChanged = !first && ((mode ^ m_Mode) & (Stage2Mode.Rule | Stage2Mode.AtLine | Stage2Mode.TurnGivesWay | Stage2Mode.LeftHandTraffic | Stage2Mode.NoPushPeople | Stage2Mode.ZebraClear | Stage2Mode.ZebraMarginMask
                | Stage2Mode.BoxObey | Stage2Mode.BoxTurnWait | Stage2Mode.BoxLeftHand | Stage2Mode.BoxEntranceMask | Stage2Mode.ExitGiveWay)) != 0;
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
        /// <summary>0.4.4 (JR-T-012): car-steps in which a vehicle leaving a building stood at its exit waiting for a gap (setting "Vehicles leaving buildings give way").</summary>
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
        /// <summary>0.4.4: exit scans stopped by the work cap (vehicles not read do not count).</summary>
        public const int ExitCapHits = FeedingScans + 30;
        public const int Count = FeedingScans + 31;

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
                         ", leaving driveway " + deltas[EntranceHeldLeaving].ToString(ci)
                       : "") +
                   (exitMask >= 2
                       ? "; building exits: waits " + deltas[ExitHeld].ToString(ci) +
                         " (turning across " + deltas[ExitHeldAcross].ToString(ci) +
                         ", spawn/gate " + deltas[ExitHeldSpawn].ToString(ci) + ")" +
                         ", go decisions " + deltas[ExitGo].ToString(ci) +
                         ", emergency passthroughs " + deltas[ExitEmergencyPass].ToString(ci) +
                         ", boxed spawn exit no room " + deltas[ExitSpawnBoxHeld].ToString(ci) +
                         ", cap hits " + deltas[ExitCapHits].ToString(ci)
                       : "") +
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

        /// <summary>Bumped on every publish that changes the bands.</summary>
        public static int Version { get; private set; }

        /// <summary>Edge keys, ascending (repeats allowed).</summary>
        public static long[] Keys => s_Keys;

        /// <summary>Curve position on the edge, parallel to <see cref="Keys"/>.</summary>
        public static float[] T => s_T;

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
                for (int i = 0; i < n.Length && same; i++) same = n[i].EdgeKey == s_Keys[i] && n[i].T == s_T[i];
                if (same) return;
            }
            var keys = new long[n.Length];
            var t = new float[n.Length];
            for (int i = 0; i < n.Length; i++) { keys[i] = n[i].EdgeKey; t[i] = n[i].T; }
            s_Keys = keys;
            s_T = t;
            Version++;
        }
    }
}
