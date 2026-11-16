using System;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>JR-T-014d (G11, owner 17 Nov 2026): people give way at unmarked crossings; always-on diagnostics.</summary>
    internal static partial class Program
    {
        private const int PedUnsafe = 1, PedCrosswalk = 2;   // Game.Net.PedestrianLaneFlags (decompile 2026-11-13)
        private const uint CarSide = 8u;                     // Game.Net.CarLaneFlags.SideConnection

        private static void UnmarkedCrossingTests()
        {
            // Flag constants match the game's enums.
            Check(UnmarkedCrossing.PedestrianUnsafe == PedUnsafe && UnmarkedCrossing.PedestrianCrosswalk == PedCrosswalk
                && UnmarkedCrossing.CarSideConnection == CarSide, "flag values = Game.Net enums");

            // Classification.
            Check(UnmarkedCrossing.Classify(PedCrosswalk | PedUnsafe, false, 0u) == UnmarkedCrossing.Kind.Unmarked, "crosswalk + Unsafe, no signal = unmarked");
            Check(UnmarkedCrossing.Classify(PedCrosswalk, false, 0u) == UnmarkedCrossing.Kind.Zebra, "crosswalk without Unsafe = zebra");
            Check(UnmarkedCrossing.Classify(PedCrosswalk, true, 0u) == UnmarkedCrossing.Kind.Signal, "signalled zebra = signal");
            Check(UnmarkedCrossing.Classify(PedCrosswalk | PedUnsafe, true, 0u) == UnmarkedCrossing.Kind.Signal, "signalled unmarked = signal (kept as is)");
            Check(UnmarkedCrossing.Classify(0, false, CarSide) == UnmarkedCrossing.Kind.Driveway, "sidewalk over a driveway = driveway (jr-t-014's zebra)");
            Check(UnmarkedCrossing.Classify(PedCrosswalk | PedUnsafe, false, CarSide) == UnmarkedCrossing.Kind.Driveway, "driveway lane wins over any footway flags");
            Check(UnmarkedCrossing.Classify(0, false, 0u) == UnmarkedCrossing.Kind.Sidewalk, "plain sidewalk = sidewalk");
            Check(UnmarkedCrossing.Classify(PedUnsafe, false, 0u) == UnmarkedCrossing.Kind.Sidewalk, "Unsafe without Crosswalk = not a crossing (PathUtils needs both)");
            Check(UnmarkedCrossing.IsUnmarkedLane(PedCrosswalk | PedUnsafe, false), "person side: unmarked lane");
            Check(!UnmarkedCrossing.IsUnmarkedLane(PedCrosswalk | PedUnsafe, true), "person side: signal lane is not");
            Check(!UnmarkedCrossing.IsUnmarkedLane(PedCrosswalk, false) && !UnmarkedCrossing.IsUnmarkedLane(PedUnsafe, false)
                && !UnmarkedCrossing.IsUnmarkedLane(0, false), "person side: zebra / sidewalk are not");

            // Car action.
            Check(UnmarkedCrossing.CarAction(false, UnmarkedCrossing.Kind.Unmarked, false) == UnmarkedCrossing.Stock, "setting off: stock (0.4.7)");
            Check(UnmarkedCrossing.CarAction(true, UnmarkedCrossing.Kind.Unmarked, false) == UnmarkedCrossing.Skip, "unmarked, person at / approaching the kerb: car does not hold");
            Check(UnmarkedCrossing.CarAction(true, UnmarkedCrossing.Kind.Unmarked, true) == UnmarkedCrossing.Floor, "unmarked, person on the carriageway: safety floor");
            foreach (var k in new[] { UnmarkedCrossing.Kind.Zebra, UnmarkedCrossing.Kind.Signal, UnmarkedCrossing.Kind.Driveway, UnmarkedCrossing.Kind.Sidewalk })
            {
                Check(UnmarkedCrossing.CarAction(true, k, false) == UnmarkedCrossing.Stock && UnmarkedCrossing.CarAction(true, k, true) == UnmarkedCrossing.Stock,
                    "rule on, " + k + ": stock");
            }

            // Safety floor: on the carriageway? Carriageway 2..9 m along a 12 m crossing.
            Check(!UnmarkedCrossing.OnCarriageway(1f, 2f, 9f, 1), "walking forward, before the kerb: off");
            Check(!UnmarkedCrossing.OnCarriageway(2.4f, 2f, 9f, 1), "walking forward, 0.4 m past the kerb edge (overshoot): off");
            Check(UnmarkedCrossing.OnCarriageway(2.6f, 2f, 9f, 1), "walking forward, 0.6 m in: on");
            Check(UnmarkedCrossing.OnCarriageway(8.9f, 2f, 9f, 1), "walking forward near the far kerb: on");
            Check(!UnmarkedCrossing.OnCarriageway(9.5f, 2f, 9f, 1), "past the far kerb: off");
            Check(UnmarkedCrossing.OnCarriageway(5f, 2f, 9f, -1) && !UnmarkedCrossing.OnCarriageway(8.8f, 2f, 9f, -1), "walking backward: mirrored");
            Check(UnmarkedCrossing.OnCarriageway(5f, 2f, 9f, 0) && !UnmarkedCrossing.OnCarriageway(2.3f, 2f, 9f, 0) && !UnmarkedCrossing.OnCarriageway(8.7f, 2f, 9f, 0),
                "standing: mid-road on, at either kerb off");
            Check(!UnmarkedCrossing.OnCarriageway(5f, 9f, 2f, 1), "no carriageway: off");
            Check(UnmarkedCrossing.Direction(1.2f) == 1 && UnmarkedCrossing.Direction(-1.2f) == -1 && UnmarkedCrossing.Direction(0.1f) == 0, "direction from speed along the lane");

            // Person side: before the kerb, target past it.
            Check(UnmarkedCrossing.BeforeKerb(1.5f, 2f, 9f, true) && UnmarkedCrossing.BeforeKerb(2.4f, 2f, 9f, true), "forward: before / at the kerb (tolerance) = may hold");
            Check(!UnmarkedCrossing.BeforeKerb(3f, 2f, 9f, true), "forward: on the road = never held (floor instead)");
            Check(UnmarkedCrossing.BeforeKerb(9.5f, 2f, 9f, false) && !UnmarkedCrossing.BeforeKerb(7f, 2f, 9f, false), "backward mirrored");
            Check(UnmarkedCrossing.TargetPastKerb(0.5f, 0.2f, 0.8f, true) && !UnmarkedCrossing.TargetPastKerb(0.2f, 0.2f, 0.8f, true), "forward target past the kerb edge");
            Check(UnmarkedCrossing.TargetPastKerb(0.5f, 0.2f, 0.8f, false) && !UnmarkedCrossing.TargetPastKerb(0.9f, 0.2f, 0.8f, false), "backward target past the kerb edge");

            // Gap judgement. A person crossing 7 m at 1.4 m/s: 5 s + 1 s margin.
            float clear = UnmarkedCrossing.ClearTime(7f, 1.4f);
            Check(Math.Abs(clear - 5f) < 1e-4f, "own crossing time = distance / speed");
            Check(UnmarkedCrossing.ClearTime(7f, 0f) == 7f / UnmarkedCrossing.MinPersonSpeed, "unknown speed: floor speed");
            Check(UnmarkedCrossing.CarBlocks(80f, 13.9f, 20f, clear), "fast car (50 km/h) 80 m away: 20 + 13.9 x 6 = 103 m reach: person waits");
            Check(!UnmarkedCrossing.CarBlocks(150f, 13.9f, 20f, clear), "fast car 150 m away: person goes");
            Check(!UnmarkedCrossing.CarBlocks(40f, 3f, 2f, clear), "slow car (3 m/s) 40 m away: reach 20 m: person goes");
            Check(UnmarkedCrossing.CarBlocks(15f, 3f, 2f, clear), "slow car 15 m away: person waits");
            Check(!UnmarkedCrossing.CarBlocks(300f, 22f, 40f, clear), "far car (80 km/h, 300 m): person goes");
            Check(!UnmarkedCrossing.CarBlocks(5f, 0f, 0f, clear) && !UnmarkedCrossing.CarBlocks(5f, 0.3f, 0f, clear), "standing car (queue) never holds the person");
            Check(!UnmarkedCrossing.CarBlocks(0f, 10f, 10f, clear) && !UnmarkedCrossing.CarBlocks(-2f, 10f, 10f, clear), "car at / on the crossing: game's own clamp decides");
            Check(!UnmarkedCrossing.CarBlocks(80f, -10f, 0f, clear), "car driving away: no");
            Check(!UnmarkedCrossing.CarBlocks(float.NaN, 10f, 10f, clear) && !UnmarkedCrossing.CarBlocks(50f, float.NaN, 10f, clear), "NaN: no hold");
            Check(UnmarkedCrossing.PersonSpeed(3f, 1.4f) > 0f, "person speed known");

            // Routes of feeding cars.
            Check(UnmarkedCrossing.RouteCounts(UnmarkedCrossing.Route.Takes) && UnmarkedCrossing.RouteCounts(UnmarkedCrossing.Route.Unknown)
                && !UnmarkedCrossing.RouteCounts(UnmarkedCrossing.Route.Other), "feeding car counts unless it goes another way");

            // Mode bit.
            Check(Stage2Mode.UnmarkedGiveWay == 1 << 7, "mode bit 7");
            Check((Stage2Mode.For(JunctionGapRule.Stock, false, unmarkedGiveWay: true) & Stage2Mode.UnmarkedGiveWay) != 0, "bit set with the stock rule too");
            Check((Stage2Mode.For(JunctionGapRule.WholeJunction, true) & Stage2Mode.UnmarkedGiveWay) == 0, "off by default in For");
            Check((Stage2Mode.Stripped & Stage2Mode.UnmarkedGiveWay) != 0, "stripped from the job's mode like the other driving bits");
            Check(Stage2Mode.RuleName(Stage2Mode.For(JunctionGapRule.Stock, false, unmarkedGiveWay: true)).Contains("people give way at unmarked crossings"), "rule name");
            var sw = new GapRuleSwitch();
            sw.Step(JunctionGapRule.Stock, false, out int m1);
            string msg = sw.Step(JunctionGapRule.Stock, false, out int m2, unmarkedGiveWay: true);
            Check(msg != null && msg.Contains("people give way at unmarked crossings") && (m2 & Stage2Mode.UnmarkedGiveWay) != 0, "switch logs the change");

            // Timing line text.
            Check(UnmarkedCrossingCounts.Format(3, 2, 1) == "; unmarked crossings: car-steps not held for people 3, people held at the kerb 2, stopped for a person already crossing unmarked 1",
                "timing line text");
            Check(UnmarkedCrossingCounts.Format(-1, -1, -1).Contains("not held for people n/a, people held at the kerb n/a"), "not counted = n/a");
        }

        private struct ArrayTrace : ITraceBuffer
        {
            public long[] A;
            public long Read(int index) => A[index];
        }

        private static void Put(long[] a, int kind, params long[] p)
        {
            long seq = a[UnmarkedTraceLayout.Cursor]++;
            int s = UnmarkedTraceLayout.SlotBase(seq);
            a[s + 1] = kind;
            for (int i = 0; i < 6; i++) a[s + 2 + i] = i < p.Length ? p[i] : 0;
            a[s] = seq + 1;
        }

        private static void UnmarkedTraceTests()
        {
            var buf = new ArrayTrace { A = new long[UnmarkedTraceLayout.TotalLongs] };
            var d = new UnmarkedTraceDrain();
            Check(d.Drain(ref buf, 100, true).Count == 0, "first drain only starts");

            // Samples capped at 5 per interval, notable capped at 20.
            for (int i = 0; i < 9; i++) Put(buf.A, UnmarkedTraceLayout.KindHoldStart, 200, i, 77, 1500, 1390, 6000);
            var lines = d.Drain(ref buf, 200, true);
            Check(lines.Count == 5 && lines[0].Contains("waits at the kerb for car 77") && lines[0].Contains("gap 15.0 m"), "samples capped at 5");
            for (int i = 0; i < 25; i++) Put(buf.A, UnmarkedTraceLayout.KindFloor, 300, 5, 6, 1000, 0, UnmarkedTraceLayout.PackXZ(1092.7f, -81.5f));
            lines = d.Drain(ref buf, 300, true);
            Check(lines.Count == 20 && lines[0].Contains("already on the carriageway") && lines[0].Contains("(1092.7, -81.5)"), "notable capped at 20, position unpacked");
            Put(buf.A, UnmarkedTraceLayout.KindLongWait, 400, 9, 10, 3700, 42, UnmarkedTraceLayout.PackXZ(-5f, 7f));
            Check(d.Drain(ref buf, 400, true).Count == 0, "notable over the cap: suppressed");

            // Final review F5: a record still being written (stamp 0) is left for the next drain, then read (never lost).
            Put(buf.A, UnmarkedTraceLayout.KindHoldStart, 500);
            int tornSlot = UnmarkedTraceLayout.SlotBase(buf.A[UnmarkedTraceLayout.Cursor] - 1);
            long tornStamp = buf.A[tornSlot];
            buf.A[tornSlot] = 0;
            Check(d.Drain(ref buf, 500, true).Count == 0, "record being written: left for the next drain");
            buf.A[tornSlot] = tornStamp;
            Check(d.Drain(ref buf, 501, true).Count == 0, "then read (a sample over the cap: suppressed, not lost)");

            // Summary at the interval with counter deltas.
            buf.A[UnmarkedTraceLayout.PeopleHeld] = 4;
            buf.A[UnmarkedTraceLayout.CarStepsNotHeld] = 12;
            buf.A[UnmarkedTraceLayout.FloorStops] = 25;
            lines = d.Drain(ref buf, 100 + UnmarkedTraceLayout.IntervalFrames, true);
            Check(lines.Count == 1, "one summary line");
            string sum = lines[0];
            Check(sum.Contains("last 60 s") && sum.Contains("people held at the kerb 4") && sum.Contains("car-steps not held for people 12")
                && sum.Contains("stopped for a person already crossing unmarked 25") && sum.Contains("notable 20 (suppressed 6)")
                && sum.Contains("samples 5 (suppressed 5)") && sum.Contains("ring overruns 0") && sum.Contains("crossings on"), "summary text: " + sum);
            buf.A[UnmarkedTraceLayout.PeopleHeld] = 5;
            lines = d.Drain(ref buf, 100 + 2 * UnmarkedTraceLayout.IntervalFrames, false);
            Check(lines.Count == 1 && lines[0].Contains("people held at the kerb 1") && lines[0].Contains("notable 0 (suppressed 0)") && lines[0].Contains("crossings off"),
                "next interval: deltas and caps reset");

            // Ring lapped: overruns counted, the newest 256 kept.
            for (int i = 0; i < UnmarkedTraceLayout.RingSlots + 10; i++) Put(buf.A, UnmarkedTraceLayout.KindHoldStart, 1);
            lines = d.Drain(ref buf, 100 + 2 * UnmarkedTraceLayout.IntervalFrames + 1, true);
            Check(lines.Count == UnmarkedTraceLayout.SampleCap, "lapped ring still capped");

            // Pack round trip; formatting kinds.
            UnmarkedTraceLayout.UnpackXZ(UnmarkedTraceLayout.PackXZ(-12345.6f, 789.1f), out float x, out float z);
            Check(Math.Abs(x + 12345.6f) < 0.06f && Math.Abs(z - 789.1f) < 0.06f, "PackXZ round trip");
            Check(UnmarkedTraceDrain.FormatRecord(UnmarkedTraceLayout.KindLongWait, new long[] { 1, 2, 3, 3600, 5, 0 }).Contains("over 60 s"), "long wait text");

            // Expected volume: per 10 min at most 10 x (20 + 5 + 1) lines.
            Check(600 / (UnmarkedTraceLayout.IntervalFrames / 60) * (UnmarkedTraceLayout.NotableCap + UnmarkedTraceLayout.SampleCap + 1) == 260, "hard cap 260 lines / 10 min");
        }

        private static void UnmarkedRepoTests()
        {
            string root = RepoRoot();
            string iter = File.ReadAllText(Path.Combine(root, "JunctionRules", "Vanilla", "JRCarLaneSpeedIterator.cs"));
            Check(iter.Contains("JR-STAGE2 BEGIN S2-50:") && iter.Contains("JR-STAGE2 BEGIN S2-50b:"), "S2-50 / S2-50b blocks in the copy");
            Check(File.ReadAllText(Path.Combine(root, "DEVIATIONS.md")).Contains("S2-50"), "documented in DEVIATIONS.md");
            string settings = File.ReadAllText(Path.Combine(root, "JunctionRules", "Settings", "JunctionRulesSettings.cs"));
            Check(settings.Contains("PeopleGiveWayUnmarked = true;") && settings.Contains("\"People give way at unmarked crossings\""), "setting default on + label");
            string mod = File.ReadAllText(Path.Combine(root, "JunctionRules", "Mod.cs"));
            Check(mod.Contains("UpdateAfter<UnmarkedCrossingSystem, HumanNavigationSystem>"), "person system registered after HumanNavigationSystem");
            Check(File.ReadAllText(Path.Combine(root, "JunctionRules", "Systems", "CarDrivingSwitchSystem.cs")).Contains("Mod.Settings.PeopleGiveWayUnmarked"), "setting plumbed into the mode");
            Check(File.ReadAllText(Path.Combine(root, "JunctionRules", "Systems", "NavTimingProbeSystems.cs")).Contains("UnmarkedCrossingCounts.Format"), "counters on the timing line");
            string sys = File.ReadAllText(Path.Combine(root, "JunctionRules", "Systems", "UnmarkedCrossingSystem.cs"));
            Check(!sys.Contains("ScheduleParallel") && sys.Contains("[BurstCompile]"), "person job: Burst, single job (hash map writes)");
            Check(ZebraSections.IsZebra(PedCrosswalk) && !ZebraSections.IsZebra(PedCrosswalk | PedUnsafe), "ZebraSections.IsZebra excludes unmarked (never a zebra Z)");
        }
    }
}
