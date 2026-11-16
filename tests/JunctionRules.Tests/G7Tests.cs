using System;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.8 (JR-T-014, gap G7): vehicles leaving buildings still pulled out and blocked Alderwood Street (17 Nov shot, left-hand
    /// traffic, south lots: near lane westbound, far lane eastbound). Owner rules: the exit gives way to road traffic (also a
    /// vehicle standing right next to it); a vehicle crossing to the far lane never ends with its nose in a lane it cannot
    /// join; the sidewalk is a zebra Z directly followed by the road lanes, so the whole decision is taken BEFORE the sidewalk.
    /// </summary>
    internal static partial class Program
    {
        private const float G7Dt = 4f / 15f;
        // A lorry with a trailer (max 22 m/s, accel 1.6 m/s^2, braking 4 m/s^2) and a car (as ArrivalTests' typical car).
        private static StockCarMotion G7Lorry => StockCarMotion.Of(22f, 1.6f, 4f);
        private static StockCarMotion G7Car => StockCarMotion.Of(40f, 5f, 8f);
        private static float G7Braking(float v) => v * v / (2f * 8f);

        private struct G7Slots : IAtomicSlots
        {
            public long[] Values;
            public int Length => Values.Length;
            public long Read(int i) => Values[i];
            public bool CompareExchange(int i, long value, long comparand)
            {
                if (Values[i] != comparand) return false;
                Values[i] = value;
                return true;
            }
        }

        private static void G7ExitSidewalkTests()
        {
            // --- the waiting spot: the sidewalk comes before the first road lane ---
            Check(ExitGiveWay.ExitSpot(0.55f, 0.10f) == 0.10f, "sidewalk overlap before the near lane: the spot is the sidewalk (stop before the pavement)");
            Check(ExitGiveWay.ExitSpot(0.30f, 2f) == 0.30f, "no sidewalk on the drive lane: the spot stays the first road lane (0.4.7)");
            Check(ExitGiveWay.ExitSpot(0.20f, 0.60f) == 0.20f, "a pedestrian lane after the road lane does not move the spot back");

            // --- G7 vehicle 1 (middle box, right-out across the near lane): the westbound truck standing next to the exit ---
            Check(ExitGiveWay.VehicleBlocks(3f, false, 0f, 0f, 2f), "westbound truck standing 3 m before the near-lane conflict (held before the band): the exit waits");
            Check(ExitGiveWay.VehicleBlocks(ExitGiveWay.PresentMetres, false, 0.2f, 0f, 1f), "a creeping vehicle at the present distance blocks");
            Check(!ExitGiveWay.VehicleBlocks(30f, false, 0f, 0f, 2f), "a queue that stopped 30 m short of the exit does not block (0.4.7 review F2 kept)");
            Check(ExitPaths.CountsAgainstBudget(-10f, 24f, 3f, 0f) && !ExitPaths.CountsAgainstBudget(-40f, 24f, 30f, 0f), "the present vehicle is read (budget), the far standing one is skipped");

            // --- far lane: stopped queue (no room) vs moving queue ---
            float lorryLen = 18.5f;
            RoomScan stopped = RoomScan.Begin(lorryLen, RoomScan.DefaultMargin);
            stopped.Add(6f, 4.5f, 0f);   // eastbound car standing 6 m past the join point
            Check(stopped.Blocked, "far lane stopped 6 m past the join point: no room for an 18.5 m lorry");
            Check(ExitGiveWay.Decide(true, 100, true, true, false, true, false) == ExitGiveWay.Decision.WaitRoom, "unboxed exit, gap clear, far lane full: wait (no room), before the sidewalk");
            RoomScan moving = RoomScan.Begin(lorryLen, RoomScan.DefaultMargin);
            moving.Add(6f, 4.5f, G7Braking(15f));   // the same car moving at 15 m/s: its braking distance counts as room
            Check(!moving.Blocked, "far lane moving: room (moving queue counts, the zebra rule)");
            Check(ExitGiveWay.Decide(true, 100, true, true, false, true, true) == ExitGiveWay.Decision.Go, "gap clear and room on the lane it joins: go");
            RoomScan carRoom = RoomScan.Begin(4.5f, RoomScan.DefaultMargin);
            carRoom.Add(6f, 4.5f, 0f);
            Check(!carRoom.Blocked, "the same 6 m space fits a car (whole length + margin): a car may go where the lorry may not");

            // --- boxed exit: room past the band decides; the join-point room is implied ---
            Check(ExitGiveWay.Decide(true, 100, true, true, true, false, true) == ExitGiveWay.Decision.WaitBox, "boxed exit, no room past the band: wait (box)");
            Check(ExitGiveWay.Decide(true, 100, true, true, true, true, false) == ExitGiveWay.Decision.Go, "boxed exit with room past the band: go (band room implies join room)");
            Check(ExitGiveWay.Decide(true, 100, true, false, true, true, false) == ExitGiveWay.Decision.WaitRoom, "box rule off at a boxed exit: the plain room rule still holds it");
            Check(ExitGiveWay.Decide(true, 100, false, true, true, true, true) == ExitGiveWay.Decision.WaitGap, "gap busy first");
            Check(ExitGiveWay.Decide(true, 108, true, true, false, true, false) == ExitGiveWay.Decision.Go, "on-duty emergency: stock, no room hold");
            Check(ExitGiveWay.Decide(false, 100, true, true, false, true, false) == ExitGiveWay.Decision.Go, "setting off: 0.4.3 behaviour (no room hold unboxed)");

            // --- left-out (merge into the near lane only) ---
            Check(ExitGiveWay.Role(true, ExNear, ExNear) == ExitGiveWay.ConflictRole.Merge && ExitGiveWay.Role(false, ExFar, ExNear) == ExitGiveWay.ConflictRole.None,
                "LHT left-out: merges into the near lane, the far lane is not on its way");
            Check(ExitGiveWay.VehicleBlocks(4f, false, 0f, 0f, 1f), "left-out: a near-lane car standing 4 m before the exit has priority (exit waits)");

            // --- trailer truck: crossed lanes use the time to CLEAR them with the whole vehicle ---
            var lorry = G7Lorry;
            var car = G7Car;
            float toNear = 3f, nearLen = 3.5f;
            float lorryReach = ArrivalTime.Compute(ref lorry, toNear, 0f, 14f, 0f, G7Dt);
            float lorryClear = ArrivalTime.Compute(ref lorry, ExitGiveWay.ClearMetres(toNear, nearLen, lorryLen), 0f, 14f, 0f, G7Dt);
            float carClear = ArrivalTime.Compute(ref car, ExitGiveWay.ClearMetres(toNear, nearLen, 4.5f), 0f, 14f, 0f, G7Dt);
            Check(lorryClear > lorryReach + 2f && lorryClear > carClear + 2f, "a lorry needs seconds longer to clear the near lane than to reach it, and longer than a car");
            float v = 13.9f, gap = 90f;   // a westbound car at 50 km/h, 90 m away
            Check(!ExitGiveWay.VehicleBlocks(gap, false, v, G7Braking(v), lorryReach) && ExitGiveWay.VehicleBlocks(gap, false, v, G7Braking(v), lorryClear),
                "50 km/h car 90 m away: clear by the 0.4.7 reach time, busy by the lorry's clear time (it would sit across the lane)");
            Check(!ExitGiveWay.VehicleBlocks(gap, false, v, G7Braking(v), carClear), "the same car is no reason for a car to wait");
            Check(ExitGiveWay.ClearMetres(-2f, 3.5f, 4f) == 7.5f && ExitGiveWay.ClearMetres(1f, -1f, -1f) == 1f, "ClearMetres: negative parts count as 0");

            // --- straight across into an opposite access (owner 17 Nov, Ordinateur van): the far lane is crossed, not merged ---
            Check(ExitGiveWay.CrossesThrough(3.5f, 5.5f) && !ExitGiveWay.CrossesThrough(40f, 5.5f) && !ExitGiveWay.CrossesThrough(-1f, 5.5f),
                "a few metres on the far lane before the opposite driveway = a crossing; staying on it = a merge; unknown = merge");
            Check(ExitGiveWay.VehicleBlocks(0f, true, 0f, 0f, 1f) && ExitGiveWay.VehicleBlocks(4f, false, 0f, 0f, 1f),
                "a stationary far-lane queue car standing on the crossing point (or right before it) blocks: the van waits in the lot");
            RoomScan into = RoomScan.Begin(5.5f, RoomScan.DefaultMargin);
            into.Add(3.5f, 4.5f, 0f);   // a car standing 3.5 m into the opposite driveway past the far lane
            Check(into.Blocked, "no room in the opposite driveway for the whole van: wait in the lot (room scan reads the path past the far lane)");

            // --- live 17 Nov 07:36 (main's track, van 380864 leaving 40 Alderwood St by its sub-net driveway, edge 557049):
            // standing in the lot (front ~267.4, sidewalk 266.25-269.25, westbound lane from 269.25), then 1.87 -> 4.64 m/s
            // across the westbound lane in front of a westbound truck at 11.2 m/s, no trace line. Stock starts every update with
            // m_MaxSpeed = speedRange.max = v + accel * dt (CarNavigationSystem 1503/1543, VehicleUtils.CalculateSpeedRange);
            // 0.4.7 S2-29 scanned only when its hold (braking speed to the spot) was below that: a van pulling away from rest
            // never satisfied it before it could no longer stop. Kinematics: accel 3, braking 8 m/s^2, dt 4/15 s, spot 0.95 m ahead.
            {
                float sv = 0f, w = 0.95f, a = 3f, br = 8f, dt = 4f / 15f;
                int oldScans = 0, newScans = 0, holdable = 0;
                for (int i = 0; i < 20 && w > -2f; i++)
                {
                    float braking = sv * sv / (2f * br);
                    bool canHold = ExitGiveWay.CanHold(w, 0.9f, braking);
                    float hold = (float)Math.Sqrt(2f * br * Math.Max(0f, w));
                    float currentMax = sv + a * dt;
                    if (canHold)
                    {
                        holdable++;
                        if (hold < currentMax) oldScans++;
                        if (ExitGiveWay.MustJudge(hold, currentMax, w)) newScans++;
                    }
                    float nv = Math.Min(currentMax, 13.9f);
                    w -= 0.5f * (sv + nv) * dt;
                    sv = nv;
                }
                Check(holdable > 0 && oldScans == 0, $"0.4.7 gate: the van pulling away from rest was never judged while it could still stop ({holdable} holdable updates, {oldScans} scans) - the Alderwood van");
                Check(newScans == holdable, $"0.4.8: judged on every update while it can still stop ({newScans}/{holdable})");
            }

            // --- judged on every update near the spot (0.4.7 hold < currentMax hole) ---
            Check(ExitGiveWay.MustJudge(3f, 1f, 5f), "creeping under a lower limit 5 m before the spot: judged");
            Check(ExitGiveWay.MustJudge(1f, 3f, 40f), "exit hold binding: judged (0.4.7)");
            Check(!ExitGiveWay.MustJudge(3f, 1f, 40f), "40 m back and held harder by the car ahead: not yet (cheap)");

            // --- lot exits stop before the sidewalk line ---
            Check(Math.Abs(ExitGiveWay.SidewalkBack(5f, 1f) - 6.5f) < 1e-4f, "straight across: 5 m to the sidewalk lane + half a sidewalk");
            Check(Math.Abs(ExitGiveWay.SidewalkBack(5f, 0.5f) - 13f) < 1e-4f && Math.Abs(ExitGiveWay.SidewalkBack(5f, 0.05f) - 6.5f / 0.3f) < 1e-3f, "an angled way is longer; a near-parallel way is capped");
            Check(ExitGiveWay.SidewalkBack(-1f, 1f) == 0f, "no sidewalk on the building side: no extra stop distance");

            // --- proof counter: the Go judgement is read back at the drive entry ---
            var slots = new G7Slots { Values = new long[64] };
            int key = ExitGiveWay.EventKey(1234, ExitGiveWay.DriveGoKind);
            SafetyStopEvents.Record(ref slots, key, 100u);
            SafetyStopEvents.Record(ref slots, key, 116u);
            bool found = EventLookup.Last(ref slots, key, out uint last);
            Check(found && last == 116u, "the last Go frame is read back");
            Check(EventLookup.Fresh(found, last, 116u + ExitGiveWay.DriveGoWindow, ExitGiveWay.DriveGoWindow) && !EventLookup.Fresh(found, last, 117u + ExitGiveWay.DriveGoWindow, ExitGiveWay.DriveGoWindow),
                "a Go counts for DriveGoWindow frames");
            Check(!EventLookup.Last(ref slots, ExitGiveWay.EventKey(1235, ExitGiveWay.DriveGoKind), out _), "no Go recorded: not judged");
            Check(!EventLookup.Last(ref slots, ExitGiveWay.EventKey(1234, ExitGiveWay.SidewalkHoldKind), out _), "kinds are separate keys");
            Check(ExitGiveWay.EventKey(1 << 25, ExitGiveWay.EventKinds - 1) > 0, "the largest event key stays a positive int");
            // Review F2: a Wait after the Go cancels it; review F3: Go / Wait slots are kept for the whole window.
            Check(ExitGiveWay.GoStands(false, 0u, 100u) && ExitGiveWay.GoStands(true, 90u, 100u) && !ExitGiveWay.GoStands(true, 116u, 100u), "a Go stands unless a Wait came after it");
            Check(SafetyStopEvents.Keep(ExitGiveWay.EventKey(7, ExitGiveWay.DriveGoKind) + 1) == ExitGiveWay.DriveGoWindow
                && SafetyStopEvents.Keep(ExitGiveWay.EventKey(7, ExitGiveWay.ExitWaitKind) + 1) == ExitGiveWay.DriveGoWindow
                && SafetyStopEvents.Keep(ExitGiveWay.EventKey(7, ExitGiveWay.SidewalkHoldKind) + 1) == SafetyStopEvents.ContinueFrames
                && SafetyStopEvents.Keep(8) == SafetyStopEvents.ContinueFrames, "slot keep time: exit Go / Wait for the window, every other kind as before");
            var crowded = new G7Slots { Values = new long[8] };
            int goKey = ExitGiveWay.EventKey(42, ExitGiveWay.DriveGoKind);
            SafetyStopEvents.Record(ref crowded, goKey, 1000u);
            for (int i = 0; i < 14; i++) SafetyStopEvents.Record(ref crowded, ExitGiveWay.EventKey(500 + i, ExitGiveWay.SidewalkHoldKind), 1100u + (uint)i * 60u);   // up to frame 1880, inside the window
            Check(EventLookup.Last(ref crowded, goKey, out uint kept) && kept == 1000u, "a full 8-slot table never evicts a Go younger than the window");

            // --- trace text ---
            long judgedFacts = ExitTrace.Pack(ExitTrace.Event.EnterDrive, ExitPaths.Path.Drive, ExitPaths.LaneKind.DriveLane, ExitGiveWay.Decision.Go, ExitTrace.ReasonDecided, true);
            long unjudgedFacts = ExitTrace.Pack(ExitTrace.Event.EnterDrive, ExitPaths.Path.Drive, ExitPaths.LaneKind.DriveLane, ExitGiveWay.Decision.Go, 0, false);
            string tj = ExitTrace.Format(ExitTrace.PackEntity(5, 1), ExitTrace.PackEntity(9, 1), judgedFacts, 7L);
            string tu = ExitTrace.Format(ExitTrace.PackEntity(5, 1), 0, unjudgedFacts, 7L);
            Check(tj.Contains("crosses the sidewalk") && tj.Contains("go (full judgement before the sidewalk)") && tj.Contains("boxed yes"), "trace: a judged sidewalk crossing");
            Check(tu.Contains("NOT judged before the sidewalk"), "trace: an unjudged one names itself");
            long roomWait = ExitTrace.Pack(ExitTrace.Event.Wait, ExitPaths.Path.Drive, ExitPaths.LaneKind.DriveLane, ExitGiveWay.Decision.WaitRoom, ExitTrace.ReasonDecided, false);
            Check(ExitTrace.Format(1, 0, roomWait, 0).Contains("wait (no room on the lane it joins)"), "trace: the room wait");
            Check(ExitTrace.Format(1, 0, stuck0(), 0, ExitTrace.PackPoint(-1983.2f, 266.4f)).Contains(" at (-1983.2, 266.4)") && ExitTrace.PointText(0) == "", "trace: the exit position is named (0 = none)");

            // --- compact trace (0.4.8, on by default) ---
            Check(ExitTrace.Notable(unjudgedFacts) && !ExitTrace.Notable(judgedFacts) && !ExitTrace.Notable(roomWait), "notable: a sidewalk crossing without a judgement; a judged one and a normal wait are only sampled");
            long stuck = ExitTrace.Pack(ExitTrace.Event.StuckOnRoad, ExitPaths.Path.Drive, ExitPaths.LaneKind.DriveLane, ExitGiveWay.Decision.Go, ExitTrace.ReasonDecided, false);
            long longHold = ExitTrace.Pack(ExitTrace.Event.LongHold, ExitPaths.Path.Drive, ExitPaths.LaneKind.DriveLane, ExitGiveWay.Decision.WaitRoom, ExitTrace.ReasonDecided, false);
            long handUnjudged = ExitTrace.Pack(ExitTrace.Event.HandOver, ExitPaths.Path.BuildingLane, ExitPaths.LaneKind.CarLane, ExitGiveWay.Decision.Go, 0, false);
            long handEmergency = ExitTrace.Pack(ExitTrace.Event.HandOver, ExitPaths.Path.BuildingLane, ExitPaths.LaneKind.CarLane, ExitGiveWay.Decision.Go, ExitTrace.ReasonEmergency, false);
            Check(ExitTrace.Notable(stuck) && ExitTrace.Notable(longHold) && ExitTrace.Notable(handUnjudged) && !ExitTrace.Notable(handEmergency), "notable: stuck on the road, long hold, unjudged hand-over; not an on-duty emergency");
            Check(ExitTrace.Format(1, 0, stuck, 0).Contains("NOTABLE stands on the road after leaving") && ExitTrace.Format(1, 0, longHold, 0).Contains("NOTABLE held 25 s+ at the exit")
                && ExitTrace.Format(1, 0, longHold, 0).Contains("wait (no room on the lane it joins)") && ExitTrace.Format(1, 0, handUnjudged, 0).Contains("NOTABLE joins road"), "notable lines say so");
            var sampler = new TraceSampler();
            int logged = 0;
            for (int i = 0; i < 200; i++) if (sampler.Allow(i % 50 == 0, 10.0 + i * 0.1)) logged++;
            Check(logged == TraceSampler.SampleMax + 4, "200 events in 20 s: the 4 notable ones and 6 normal samples logged");
            Check(sampler.TakeSummary(40.0) == null, "no summary before the minute is over");
            string sum = sampler.TakeSummary(70.5);
            Check(sum != null && sum.Contains("200 exit events, notable 4 (4 logged, cap 20 a minute), normal sample 6 of 196"), "one summary line a minute");
            var flood = new TraceSampler();
            int floodLogged = 0;
            for (int i = 0; i < 100; i++) if (flood.Allow(true, 5.0 + i * 0.1)) floodLogged++;
            string floodSum = flood.TakeSummary(70.0);
            Check(floodLogged == TraceSampler.NotableMax && floodSum.Contains("notable 100 (20 logged, cap 20 a minute)"), "final review F3: notable lines capped at 20 a minute, the rest counted");
            Check(sampler.TakeSummary(200.0) == null, "no summary for an empty minute");
            Check(sampler.Allow(false, 300.0), "a new window samples again");

            // --- counters text ---
            var deltas = new long[Stage2Counters.Count];
            deltas[Stage2Counters.ExitHeldBeforeSidewalk] = 7;
            deltas[Stage2Counters.ExitHeldRoom] = 3;
            deltas[Stage2Counters.ExitDriveEntries] = 12;
            deltas[Stage2Counters.ExitDriveUnjudged] = 0;
            string line = Stage2Counters.Format(2, deltas, 2, 2, 2, 2);
            Check(line.Contains("exits held before the sidewalk 7 (no room on the lane it joins 3)") && line.Contains("driveway exits crossing the sidewalk 12, of them without a full judgement 0 (must be 0); standing on the road after leaving a driveway (5 s+) 0; judged only on the sidewalk (could not stop before it) 0"),
                "timing line: the sidewalk proof counters");
        }

        private static long stuck0() => ExitTrace.Pack(ExitTrace.Event.StuckOnRoad, ExitPaths.Path.Drive, ExitPaths.LaneKind.DriveLane, ExitGiveWay.Decision.Go, ExitTrace.ReasonDecided, false);

        private static void G7RepoTests()
        {
            string root = RepoRoot();
            if (root == null) return;
            string glue = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRExitGiveWay.cs"));
            string wj = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            string nav = File.ReadAllText(Path.Combine(root, "JunctionRules", "Vanilla", "JRCarNavigationSystem.cs"));
            string it = File.ReadAllText(Path.Combine(root, "JunctionRules", "Vanilla", "JRCarLaneSpeedIterator.cs"));
            int dh = glue.IndexOf("public float ExitDrivewayHold(", StringComparison.Ordinal);
            int dhEnd = glue.IndexOf("private static bool ExitSidewalk(", StringComparison.Ordinal);
            string body = dh > 0 && dhEnd > dh ? glue.Substring(dh, dhEnd - dh) : "";
            Check(body.Contains("ExitGiveWay.ExitSpot(firstRoad, firstWalk)") && body.Contains("ExitSidewalk(overlaps[i]"), "S2-29 waits before the sidewalk");
            Check(body.Contains("ExitGiveWay.MustJudge(hold, currentMax, wait)") && !body.Contains("if (!(hold < currentMax)) return float.MaxValue;"), "S2-29 no longer skips a vehicle creeping under a lower limit");
            Check(body.Contains("ExitGiveWay.ClearMetres(") && body.Contains("o.m_Other == joinNav.m_Lane && !across") && body.Contains("ExitGiveWay.CrossesThrough("), "crossed lanes use the clear time, the joined lane the reach time unless the path only crosses it (opposite access)");
            Check(body.IndexOf("ExitGiveWay.CanHold(", StringComparison.Ordinal) < body.IndexOf("ConflictBusy(", StringComparison.Ordinal), "the decision is taken only while it can still stop before the spot");
            Check(body.Contains("wait + ExitGiveWay.SpotSlack") && body.Contains("wait = origDistance + length * (firstRoad - start);") && body.Contains("m_DriveJudged = hasJoin;"),
                "review F4/F1: slack at the spot, fallback judgement at the first road lane, no full judgement without the joined lane");
            Check(nav.Contains("m_ZebraRule || carLaneSpeedIterator.m_JR.m_BoxRule || carLaneSpeedIterator.m_JR.m_ExitRule)"), "review F1: the path and vehicle length are handed in with only the exit rule on");
            Check(glue.Contains("deltaClear > delta ? deltaClear : delta"), "review F5: lot / spawn / parking / building-road exits use the clear time on crossed lanes");
            Check(wj.Contains("elements.m_SidewalkZebra = m_ExitRule") && wj.Contains("(m_SidewalkZebra || ZebraSections.IsZebra("), "zebra sections: a driveway's sidewalk is a Z (people, ZC merge, room past)");
            Check(it.Contains("ref m_LaneOverlapData, ref m_PedestrianLaneData);\n\t\t\tif (jrExitHold < m_MaxSpeed)"), "S2-29 hands the overlap and pedestrian lookups");
            Check(nav.Contains("ExitHandOver(m_JRCounters, m_JRSafetyEvents, entity") && nav.Contains("m_JR.NoteExit(m_JRCounters, m_JRSafetyEvents, entity, m_SimulationFrame, currentLane.m_Lane, num2, currentLane.m_Duration"),
                "S2-34 checks the drive entry against the recorded Go; S2-22 records it");
            int s22 = nav.IndexOf("// JR-STAGE2 BEGIN S2-22", StringComparison.Ordinal);
            int s34 = nav.IndexOf("// JR-STAGE2 BEGIN S2-34", StringComparison.Ordinal);
            Check(s22 > 0 && s34 > s22 && nav.IndexOf("NoteExit(", s22, StringComparison.Ordinal) < s34, "the Go is recorded before the same update's hand-over");
            Check(glue.Contains("roomJoin = HasRoomPast(road, t, false"), "spawn point / gate / parking / lot / building road exits need room on the lane they join");
            Check(glue.Contains("float back = lot ? SidewalkBack("), "lot exits stop before the sidewalk line");
            string settings = File.ReadAllText(Path.Combine(root, "JunctionRules", "Settings", "JunctionRulesSettings.cs"));
            string mod = File.ReadAllText(Path.Combine(root, "JunctionRules", "Mod.cs"));
            Check(settings.Contains("DebugTiming = true;") && settings.Contains("ExitTrace = true;") && settings.Contains("public string MigrateDiagnostics()") && mod.Contains("Settings.MigrateDiagnostics()"),
                "0.4.8 probe: timing log and compact exit trace on by default, switched on once over an older saved off");
            Check(glue.Contains("Add(counters, Stage2Counters.ExitStuckOnRoad, 1)"), "standing on the road after leaving is counted");
            Check(glue.Contains("if (unjudged && FreshGo(eventTable, self, frame)) unjudged = false;"), "a hand-over after a recent Go is judged (0.4.7 building-road 'not judged' artefact)");
        }
    }
}
