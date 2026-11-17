using System;
using System.Collections.Generic;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.7 (JR-T-013a, gap G1): every way out of a building / parking space is classified and judged (Core/ExitPaths.cs);
    /// cap policy; the exit trace (record layout, ring reader, rate limit); placement of the new copy blocks S2-35 / S2-36.
    /// </summary>
    internal static partial class Program
    {
        private const uint NavTT = ExitPaths.NavTransformTarget, NavPS = ExitPaths.NavParkingSpace, NavConn = ExitPaths.NavConnection, NavArea = ExitPaths.NavArea;

        /// <summary>The 0.4.6 probe's classification (S2-34 before 0.4.7), to show where the spawn point landed.</summary>
        private static string Probe046(uint flags, bool fromBuilding)
        {
            if ((flags & NavArea) != 0) return "lot";
            if ((flags & NavConn) != 0) return fromBuilding ? "gate" : null;
            return fromBuilding ? "other" : null;
        }

        private static void ExitPathTests()
        {
            // The G1 root cause: a building's spawn location object (TransformTarget) was the 0.4.6 'other' and never judged.
            Check(Probe046(NavTT, true) == "other" && ExitPaths.Classify(NavTT, true, false, false) == ExitPaths.Path.SpawnPoint
                && ExitPaths.JudgedAtHandOver(ExitPaths.Path.SpawnPoint), "spawn point: the 0.4.6 'other', now its own path and judged (S2-35)");
            Check(ExitPaths.Classify(NavArea, false, false, false) == ExitPaths.Path.Lot && ExitPaths.Classify(NavArea, true, false, false) == ExitPaths.Path.Lot, "area lane: lot (with or without a building owner, as 0.4.5)");
            Check(ExitPaths.Classify(NavConn, true, false, false) == ExitPaths.Path.Gate, "building connection lane: gate");
            Check(ExitPaths.Classify(NavConn, false, false, false) == ExitPaths.Path.None, "connection lane of no building (outside connection): not a way out of a building");
            Check(ExitPaths.Classify(NavTT, false, false, false) == ExitPaths.Path.None, "a transform target of no building: stock, not counted");
            Check(ExitPaths.Classify(NavPS, true, false, false) == ExitPaths.Path.Parking, "building parking lane: building parking");
            Check(ExitPaths.Classify(NavPS, false, false, false) == ExitPaths.Path.KerbParking && ExitPaths.JudgedAtHandOver(ExitPaths.Path.KerbParking),
                "kerbside parking of the road: judged (owner rule: a building exit is a yielding arm; give way when moving off)");
            Check(ExitPaths.Classify(NavConn | NavPS, true, false, false) == ExitPaths.Path.Parking, "garage (Connection | ParkingSpace): the stock parking branch, judged as parking");
            Check(ExitPaths.Classify(0u, true, true, false) == ExitPaths.Path.BuildingLane && ExitPaths.JudgedAtHandOver(ExitPaths.Path.BuildingLane), "car lane of a building's own road: building road, judged (S2-36)");
            Check(ExitPaths.Classify(0u, false, true, false) == ExitPaths.Path.None, "road lane to road lane: not counted");
            Check(ExitPaths.Classify(0u, false, true, true) == ExitPaths.Path.Drive && !ExitPaths.JudgedAtHandOver(ExitPaths.Path.Drive), "drive (SideConnection lane, owned by the road): counted, judged earlier by S2-29");
            Check(ExitPaths.Classify(0u, true, false, false) == ExitPaths.Path.Other && !ExitPaths.JudgedAtHandOver(ExitPaths.Path.Other), "a building-owned lane with no car lane data: other (expected 0)");
            // Lane kinds (the trace's lane type) and counters.
            Check(ExitPaths.Kind(NavTT, false, false) == ExitPaths.LaneKind.SpawnObject && ExitPaths.Kind(NavConn | NavPS, false, false) == ExitPaths.LaneKind.ParkingLane
                && ExitPaths.Kind(0u, true, true) == ExitPaths.LaneKind.DriveLane && ExitPaths.Kind(0u, false, false) == ExitPaths.LaneKind.NoCarLane, "lane kinds");
            var seen = new HashSet<int>();
            bool distinct = true;
            for (int i = 1; i < ExitPaths.PathCount; i++)
            {
                int c = ExitPaths.Counter((ExitPaths.Path)i);
                if (c < Stage2Counters.ExitJoinArea || c >= Stage2Counters.Count || !seen.Add(c)) distinct = false;
            }
            Check(distinct && ExitPaths.Counter(ExitPaths.Path.None) == -1, "every path has its own hand-over counter; none for road to road");
            Check(ExitPaths.Counter(ExitPaths.Path.Lot) == Stage2Counters.ExitJoinArea && ExitPaths.Counter(ExitPaths.Path.Gate) == Stage2Counters.ExitJoinConnection
                && ExitPaths.Counter(ExitPaths.Path.Other) == Stage2Counters.ExitJoinOther, "lot / gate / other keep their 0.4.6 counters");
            // Decisions: every judged path takes the same decision (gap, then box).
            foreach (ExitPaths.Path p in new[] { ExitPaths.Path.SpawnPoint, ExitPaths.Path.KerbParking, ExitPaths.Path.BuildingLane })
            {
                Check(ExitGiveWay.Decide(true, ExCarPriority, false, true, false, true) == ExitGiveWay.Decision.WaitGap
                    && ExitGiveWay.Decide(true, ExCarPriority, true, true, true, false) == ExitGiveWay.Decision.WaitBox
                    && ExitGiveWay.Decide(true, 108, true, true, true, false) == ExitGiveWay.Decision.WaitBox
                    && ExitGiveWay.Decide(true, 108, false, true, false, true) == ExitGiveWay.Decision.Go, ExitPaths.Name(p) + ": gap wait, box wait (room past), emergency only box rule");
            }
            // Flag values against the decompile.
            string decomp = Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", "Code", "decomp", "yb-2026-11-13", "full", "Game.Vehicles", "CarLaneFlags.cs");
            if (File.Exists(decomp))
            {
                string f = File.ReadAllText(decomp);
                Check(f.Contains("TransformTarget = 8u,") && f.Contains("ParkingSpace = 0x10u,") && f.Contains("Connection = 0x1000000u,") && f.Contains("Area = 0x4000000u,"),
                    "decompile: Game.Vehicles.CarLaneFlags values mirrored in ExitPaths");
            }
        }

        private static void ExitCapTests()
        {
            Check(!ExitPaths.CapBusy(true, false), "lane cap: errs to go (a geometry-fixed cap must not hold an exit for ever)");
            Check(ExitPaths.CapBusy(false, true) && ExitPaths.CapBusy(true, true), "vehicle cap: dense traffic, busy (wait)");
            Check(ExitGiveWay.MaxScanLanes >= 48, "lane budget raised (24 ran out near junctions with two feeder levels)");
            Check(!ExitPaths.CountsAgainstBudget(30f, 20f, -25f), "a vehicle past the conflict does not use the vehicle budget");
            Check(!ExitPaths.CountsAgainstBudget(-400f, 20f, ExitGiveWay.MaxScanMetres + 1f), "a vehicle beyond the scan distance does not use the vehicle budget");
            Check(ExitPaths.CountsAgainstBudget(-40f, 20f, 35f), "an approaching vehicle in reach does");
            // A long busy lane: 150 vehicles past the exit, 40 approaching within reach. 0.4.6 counted all 190 (cap hit at 96,
            // erred to go: the exit cut out); 0.4.7 counts the 40.
            int counted = 0;
            for (int i = 0; i < 150; i++) if (ExitPaths.CountsAgainstBudget(25f + 6f * i, 20f, 20f - (29f + 6f * i))) counted++;
            for (int i = 0; i < 40; i++) if (ExitPaths.CountsAgainstBudget(-10f - 6f * i, 20f, 14f + 6f * i)) counted++;
            Check(counted == 40 && counted <= ExitGiveWay.MaxVehiclesPerScan, "busy road: only the 40 approaching vehicles count, no cap hit");
            Check(190 > ExitGiveWay.MaxVehiclesPerScan, "the same lane hit the 0.4.6 cap");
            // Review F2: a standing jam of 100 vehicles before the conflict (stopped short of the exit). None of them blocks
            // (VehicleBlocks), so none may use the budget: 0.4.7 before the fix counted them, hit the cap and waited for ever.
            // 100 vehicles on four queued lanes (the join lane and its feeders), 25 each, 6 m apart, all within reach.
            int standing = 0, before = 0;
            bool anyBlocks = false;
            for (int i = 0; i < 100; i++)
            {
                float gap = 2f + 6f * (i % 25), front = 20f - gap, rear = front - 4.5f;
                if (ExitPaths.CountsAgainstBudget(rear, 24f, gap)) before++;
                if (ExitPaths.CountsAgainstBudget(rear, 24f, gap, 0f)) standing++;
                if (ExitGiveWay.VehicleBlocks(gap, false, 0f, 0f, 1f)) anyBlocks = true;
            }
            Check(before == 100 && before > ExitGiveWay.MaxVehiclesPerScan, "the jam hit the vehicle cap before the review fix (busy: wait for ever)");
            // 0.4.8 (G7): the queue heads within PresentMetres of the exit (2 m and 8 m on each of the four lanes) are AT the exit and block
            // (owner: the exit gives way to the vehicle right next to it); the rest of the jam still uses no budget.
            Check(standing == 8 && anyBlocks, "standing jam of 100 before the exit: only the 8 queue heads within 10 m count and block, the rest no budget");
            int farStanding = 0; bool farBlocks = false;
            for (int i = 0; i < 100; i++)
            {
                float gap = 12f + 6f * (i % 25), front = 20f - gap, rear = front - 4.5f;
                if (ExitPaths.CountsAgainstBudget(rear, 24f, gap, 0f)) farStanding++;
                if (ExitGiveWay.VehicleBlocks(gap, false, 0f, 0f, 1f)) farBlocks = true;
            }
            Check(farStanding == 0 && !farBlocks, "a jam that stopped 12 m or more short of the exit: no budget used, nobody blocks: go (0.4.7 review F2 kept)");
            Check(ExitPaths.CountsAgainstBudget(15f, 24f, 0f, 0f) && ExitPaths.CountsAgainstBudget(15f, 24f, -2f, 0f), "a standing vehicle on the conflict still counts (it blocks)");
            Check(ExitPaths.CountsAgainstBudget(-40f, 20f, 35f, 0.1f), "a moving approaching vehicle in reach still counts");
        }

        private static void ExitTraceTests()
        {
            // Record facts round trip.
            bool ok = true;
            for (int p = 0; p < ExitPaths.PathCount; p++)
                for (int k = 0; k < 8; k++)
                    for (int d = 0; d < 3; d++)
                    {
                        long v = ExitTrace.Pack(ExitTrace.Event.HandOver, (ExitPaths.Path)p, (ExitPaths.LaneKind)k, (ExitGiveWay.Decision)d, 0x1F, (p & 1) == 0);
                        ExitTrace.Unpack(v, out ExitTrace.Event e, out ExitPaths.Path pp, out ExitPaths.LaneKind kk, out ExitGiveWay.Decision dd, out int r, out bool b);
                        if (e != ExitTrace.Event.HandOver || (int)pp != p || (int)kk != k || (int)dd != d || r != 0x1F || b != ((p & 1) == 0)) ok = false;
                    }
            Check(ok, "trace record facts pack / unpack for every path, lane kind, decision, reasons and boxed");
            long veh = ExitTrace.PackEntity(123456, 3), bld = ExitTrace.PackEntity(789, 2);
            string w = ExitTrace.Format(veh, bld, ExitTrace.Pack(ExitTrace.Event.Wait, ExitPaths.Path.SpawnPoint, ExitPaths.LaneKind.SpawnObject, ExitGiveWay.Decision.WaitGap, ExitTrace.ReasonDecided, true), 4096L | (55L << 32));
            Check(w.Contains("exit trace: waits vehicle 123456:3 building 789:2 path spawn point (spawn location object)") && w.Contains("road owner 55") && w.Contains("decision wait (gap)") && w.Contains("boxed yes") && w.Contains("frame 4096"),
                "trace line: vehicle, building, path + lane type, decision + reason, boxed: " + w);
            string box = ExitTrace.Format(veh, 0, ExitTrace.Pack(ExitTrace.Event.Wait, ExitPaths.Path.KerbParking, ExitPaths.LaneKind.ParkingLane, ExitGiveWay.Decision.WaitBox, ExitTrace.ReasonDecided, true), 1);
            Check(box.Contains("building none") && box.Contains("kerbside parking (parking lane)") && box.Contains("wait (box: no room past)"), "trace line: kerbside parking, box wait");
            string go = ExitTrace.Format(veh, bld, ExitTrace.Pack(ExitTrace.Event.HandOver, ExitPaths.Path.Lot, ExitPaths.LaneKind.AreaLane, ExitGiveWay.Decision.Go, ExitTrace.ReasonDecided | ExitTrace.ReasonLaneCap | ExitTrace.ReasonNodeJoin, false), 1);
            Check(go.Contains("joins road") && go.Contains("(junction)") && go.Contains("go (gap clear; lane cap hit") && go.Contains("boxed no"), "trace line: hand-over, junction join, lane cap");
            string vc = ExitTrace.Format(veh, bld, ExitTrace.Pack(ExitTrace.Event.Wait, ExitPaths.Path.Gate, ExitPaths.LaneKind.ConnectionLane, ExitGiveWay.Decision.WaitGap, ExitTrace.ReasonDecided | ExitTrace.ReasonVehicleCap, false), 1);
            Check(vc.Contains("wait (dense traffic: vehicle cap)"), "trace line: vehicle cap wait");
            string sig = ExitTrace.Format(veh, bld, ExitTrace.Pack(ExitTrace.Event.HandOver, ExitPaths.Path.BuildingLane, ExitPaths.LaneKind.CarLane, ExitGiveWay.Decision.Go, ExitTrace.ReasonDecided | ExitTrace.ReasonSignal | ExitTrace.ReasonNodeJoin, false), 1);
            Check(sig.Contains("decision go (signalled arm)") && !sig.Contains("gap clear"), "trace line: building road at a signalled arm says so (review F6): " + sig);
            Check(ExitTrace.ReasonSignal < 0x100 && (ExitTrace.ReasonSignal & (ExitTrace.ReasonDecided | ExitTrace.ReasonLaneCap | ExitTrace.ReasonVehicleCap | ExitTrace.ReasonEmergency | ExitTrace.ReasonNodeJoin)) == 0, "signal reason: its own bit, fits the packed byte");
            string un = ExitTrace.Format(veh, bld, ExitTrace.Pack(ExitTrace.Event.HandOver, ExitPaths.Path.Other, ExitPaths.LaneKind.NoCarLane, ExitGiveWay.Decision.Go, 0, false), 1);
            string dr = ExitTrace.Format(veh, 0, ExitTrace.Pack(ExitTrace.Event.HandOver, ExitPaths.Path.Drive, ExitPaths.LaneKind.DriveLane, ExitGiveWay.Decision.Go, 0, false), 1);
            string em = ExitTrace.Format(veh, bld, ExitTrace.Pack(ExitTrace.Event.HandOver, ExitPaths.Path.Gate, ExitPaths.LaneKind.ConnectionLane, ExitGiveWay.Decision.Go, ExitTrace.ReasonEmergency, false), 1);
            Check(un.Contains("not judged") && dr.Contains("judged before the hand-over") && em.Contains("emergency"), "trace line: not judged / drive / emergency");

            // Ring protocol: the job's writer (TraceWrite) and the main-thread reader.
            long[] ring = new long[ExitTrace.RingLength];
            void Write(long vehicle)
            {
                long seq = ++ring[0];
                int b = ExitTrace.SlotBase(seq);
                ring[b] = 0;
                ring[b + 1] = vehicle; ring[b + 2] = 0; ring[b + 3] = 0; ring[b + 4] = seq;
                ring[b] = seq;
            }
            var reader = new ExitTraceReader();
            var got = new List<long>();
            for (int i = 1; i <= 10; i++) Write(i);
            int n = reader.Drain(ring[0], i => ring[i], (v, bl, f, fr) => got.Add(v));
            Check(n == 10 && got.Count == 10 && got[0] == 1 && got[9] == 10, "reader: records in order");
            got.Clear();
            for (int i = 11; i <= 11 + ExitTrace.Slots + 9; i++) Write(i);   // Slots + 10 records: 10 overwritten before reading
            reader.Drain(ring[0], i => ring[i], (v, bl, f, fr) => got.Add(v));
            Check(got.Count == ExitTrace.Slots && reader.TakeLost() == 10 && got[0] == 21, "reader: ring overrun counted as lost, the newest Slots records read");
            got.Clear();
            long next = ring[0] + 1;
            ring[0] = next;   // a writer took a sequence number but has not finished (stamp still the old record's)
            Check(reader.Drain(ring[0], i => ring[i], (v, bl, f, fr) => got.Add(v)) == 0 && got.Count == 0, "reader: a record being written is left for the next step");
            int sb = ExitTrace.SlotBase(next);
            ring[sb + 1] = 999; ring[sb] = next;
            Check(reader.Drain(ring[0], i => ring[i], (v, bl, f, fr) => got.Add(v)) == 1 && got[0] == 999, "reader: ... and read once finished");
            Check(reader.Next == ring[0] + 1 && !ExitTraceReader.HasNew(ring[0], reader.Next), "reader: all read, nothing new (the bridge skips the drain: review F5)");
            Write(1000);
            Check(ExitTraceReader.HasNew(ring[0], reader.Next), "reader: a new record is seen");
            var fresh = new ExitTraceReader();
            Check(!ExitTraceReader.HasNew(0, fresh.Next) && fresh.Next == 1, "reader: an empty ring has nothing new");

            // Rate limit.
            var lim = new LineRateLimiter(ExitTrace.MaxLinesPerSecond);
            int allowed = 0;
            for (int i = 0; i < 25; i++) if (lim.Allow(100.0 + i * 0.01)) allowed++;
            Check(allowed == ExitTrace.MaxLinesPerSecond && lim.TakeDropped() == 25 - ExitTrace.MaxLinesPerSecond, "rate limit: at most 20 lines a second, the rest counted as dropped");
            Check(lim.Allow(101.5) && lim.TakeDropped() == 0, "rate limit: the next second logs again");
            Check(ExitGiveWay.TraceWaitKind < ExitGiveWay.EventKinds && ExitGiveWay.TraceWaitKind > 5, "trace wait episodes use their own event kind");
        }

        private static void ExitG1RepoTests()
        {
            string root = RepoRoot();
            Check(root != null, "repo root found");
            if (root == null) return;
            string nav = File.ReadAllText(Path.Combine(root, "JunctionRules", "Vanilla", "JRCarNavigationSystem.cs"));
            string glue = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRExitGiveWay.cs"));
            string settings = File.ReadAllText(Path.Combine(root, "JunctionRules", "Settings", "JunctionRulesSettings.cs"));
            string dev = File.ReadAllText(Path.Combine(root, "DEVIATIONS.md"));
            string Block(string id)
            {
                int s = nav.IndexOf("// JR-STAGE2 BEGIN " + id + ":", StringComparison.Ordinal);
                return s < 0 ? "" : nav.Substring(s, nav.IndexOf("// JR-STAGE2 END", s, StringComparison.Ordinal) - s);
            }
            int s35 = nav.IndexOf("// JR-STAGE2 BEGIN S2-35", StringComparison.Ordinal);
            int parking = nav.IndexOf("laneIterator.IterateParkingTarget(currentLane.m_Lane", StringComparison.Ordinal);
            int areaElse = nav.IndexOf("if ((currentLane.m_LaneFlags & Game.Vehicles.CarLaneFlags.Area) == 0)", StringComparison.Ordinal);
            Check(s35 > parking && parking > 0 && s35 < areaElse, "S2-35 in the stock TransformTarget / ParkingSpace branch, after its own iteration");
            string b35 = Block("S2-35");
            Check(b35.Contains("ExitTargetHold(entity, priority, currentLane.m_Lane, currentLane.m_LaneFlags, navigationLanes[0]") && b35.Contains("AddCounts(") && b35.Contains("TraceWait("),
                "S2-35: the vehicle's own priority, its current lane and flags, counters (the branch skips S2-22), trace");
            int s30 = nav.IndexOf("// JR-STAGE2 BEGIN S2-30", StringComparison.Ordinal);
            int s36 = nav.IndexOf("// JR-STAGE2 BEGIN S2-36", StringComparison.Ordinal);
            Check(s36 > s30 && s36 < nav.IndexOf("navigation.m_MaxSpeed = laneIterator.m_MaxSpeed;", s30, StringComparison.Ordinal), "S2-36 after S2-30, before the max speed is taken");
            Check(Block("S2-36").Contains("ExitLaneHold(entity, laneIterator.m_Priority,"), "S2-36: the vehicle's own priority");
            string b33 = Block("S2-33");
            int s33 = nav.IndexOf("// JR-STAGE2 BEGIN S2-33", StringComparison.Ordinal);
            Check(b33.Contains("if (laneIterator.m_JR.ExitWaiting)") && !b33.Contains("CarLaneFlags.Area) != 0 &&") && nav.IndexOf("ApplySideEffects(ref currentLane", s33, StringComparison.Ordinal) > s33,
                "S2-33 keeps EVERY held exit off the road lane, before the lane switch");
            Check(nav.Contains("S2-34, S2-35, S2-36, S2-51, S2-51b; see DEVIATIONS.md") && dev.Contains("S2-35") && dev.Contains("S2-36"), "new blocks listed in the copy header and DEVIATIONS.md");
            Check(Block("S2-23").Contains("JRStage2Bridge.TraceOn") && Block("S2-23").Contains("JRStage2Bridge.TraceRing") && Block("S2-26").Contains("SetTrace(m_JRExitTrace)"), "trace setting and ring handed to the job and the vehicle");
            int vb = glue.IndexOf("private bool VehiclesBlock", StringComparison.Ordinal);
            string vblock = vb < 0 ? "" : glue.Substring(vb);
            Check(vblock.IndexOf("CountsAgainstBudget(rear, cEnd, gap, speed)", StringComparison.Ordinal) > vblock.IndexOf("float speed = 0f;", StringComparison.Ordinal) && vblock.IndexOf("float speed = 0f;", StringComparison.Ordinal) > 0
                && vblock.IndexOf("CountsAgainstBudget(", StringComparison.Ordinal) < vblock.IndexOf("++budget.m_Vehicles", StringComparison.Ordinal)
                && vblock.Contains("return ExitPaths.CapBusy(false, true);"), "vehicle budget counts only relevant vehicles; a vehicle cap hit is busy");
            Check(glue.Contains("ExitJoinLane(from, priority, next, !parking,") && glue.Contains("ExitJoinLane(lane, priority, next, true,"), "spawn point and building road need a building owner, a parking space does not");
            Check(glue.Contains("WholePathRule.Water | WholePathRule.MergeStart") && glue.Contains("m_ExitNodeJoin = nodeJoin;"), "junction joins judged from the joined lane's overlaps (not the edge-lane lateral rule)");
            // 0.4.7 review fixes (G1).
            Check(Block("S2-22").Contains("CountRoadStart(m_JRCounters, m_JRSafetyEvents, entity, m_SimulationFrame, currentLane.m_Lane, currentLane.m_LaneFlags, m_UnspawnedData.HasComponent(entity)")
                && glue.Contains("(flags & (NotRoadLane | Game.Vehicles.CarLaneFlags.ResetSpeed)) != 0) return;"), "F1: vehicles placed straight onto a road lane counted in S2-22 (gate exits with ResetSpeed not)");
            Check(glue.Contains("feed == reads.m_OwnLane") && glue.Contains("reads.m_OwnLane = ownLane;") && glue.Contains("reads.m_OwnLane = lane;")
                && glue.Contains("ExitApproachHold(self, lane, false, priority,"), "F3: the leaving vehicle's own lane is never a feeding lane");
            Check(glue.Contains("m_ExitSignalArm = true;") && glue.Contains("ExitTrace.ReasonSignal"), "F6: signalled arm reason set and traced");
            string bridge = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRStage2Bridge.cs"));
            int dt = bridge.IndexOf("private static void DrainTrace()", StringComparison.Ordinal);
            string dtb = dt < 0 ? "" : bridge.Substring(dt, bridge.IndexOf("private static unsafe long ReadTrace", dt, StringComparison.Ordinal) - dt);
            Check(dtb.IndexOf("return;", dtb.IndexOf("HasNew(", StringComparison.Ordinal), StringComparison.Ordinal) < dtb.IndexOf("Stopwatch.GetTimestamp", StringComparison.Ordinal)
                && dtb.Contains("Drain(head, s_ReadTrace, s_EmitTrace)") && !dtb.Contains("=>"), "F5: DrainTrace returns early with nothing to read and uses cached delegates (no closure)");
            Check(settings.Contains("public bool ExitTrace { get; set; }") && settings.Contains("ExitTrace = true;") && settings.Contains("\"Exit trace (log)\"")
                && settings.Contains("GetOptionDescLocaleID(nameof(JunctionRulesSettings.ExitTrace))"), "setting Exit trace (log): default on (0.4.8 probe, compact), label and description");
        }
    }
}
