using System;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.1 (JR-T-009): cars never stop on a zebra (Core/ZebraSections.cs, Core RoomScan, Stage2/JRWholeJunction.cs
    /// BeginZebras). Owner examples ZCCCC, CCCZ, CCZCC; detail 2 (bus does not fit: merge); the binding ZCZ rule (a waiting
    /// spot is no waiting spot if the whole vehicle does not fit there clear of the zebra behind it; chains merge);
    /// detail 1 (room past: queue full / moving, owner margin); mid-block zebra; setting off = 0.4.0 behaviour.
    /// </summary>
    internal static partial class Program
    {
        private const float ZLane = 30f;      // junction lane length (m)
        private const float ZCar = 4.5f;      // a car, whole length (m)
        private const float ZBus = 12f;       // a bus (m)

        /// <summary>Path elements in driving coordinates (kind bits, start, end).</summary>
        private struct ZElems : IPathElements
        {
            public (int Kind, float Start, float End)[] Items;
            public int Count => Items.Length;

            public void Get(int i, out int kind, out float start, out float end)
            {
                kind = Items[i].Kind;
                start = Items[i].Start;
                end = Items[i].End;
            }
        }

        /// <summary>The vehicle points of a ZElems list as conflicts (for StretchState.BeginWithSpots).</summary>
        private struct ZConflicts : IConflictList
        {
            public (int Kind, float Start, float End)[] Items;
            public int Count => Items.Length;

            public bool Conflict(int i, out float start, out float end)
            {
                start = Items[i].Start;
                end = Items[i].End;
                return (Items[i].Kind & ZebraSections.SpotPoint) != 0;
            }
        }

        private static (int, float, float) Zb(float s, float e) => (ZebraSections.Zebra, s, e);
        private static (int, float, float) Cv(float s, float e) => (ZebraSections.SpotPoint | ZebraSections.Clearance, s, e);

        private static ZebraLayout ZBuild(bool sections, float vehicle, params (int, float, float)[] items)
        {
            var list = new ZElems { Items = items };
            bool any = ZebraSections.Build(ref list, sections, false, ZLane, M, vehicle, out ZebraLayout layout);
            Check(any, "layout built (lane has a zebra)");
            return layout;
        }

        /// <summary>A car whose front is at <paramref name="front"/> (driving coordinates) at speed 0, on the lane's spots.</summary>
        private static StretchState ZCarAt(ZebraLayout layout, float front, params (int, float, float)[] items)
        {
            var list = new ZConflicts { Items = items };
            var s = default(StretchState);
            s.BeginWithSpots(ref list, layout.Spots, -M, ZLane, front, M, BrakingDistance(0f));
            return s;
        }

        /// <summary>Front a margin before u (where the game stops a car for a point at u).</summary>
        private static float ZStop(float u) => u - (M + 0.05f) / ZLane;

        private static void ZebraSectionTests()
        {
            // ---- ZCCCC: stop before Z; go only when Z and all four Cs are clear (one decision at Z) ----
            var zcccc = new[] { Zb(0.10f, 0.20f), Cv(0.30f, 0.33f), Cv(0.40f, 0.43f), Cv(0.50f, 0.53f), Cv(0.60f, 0.63f) };
            var l1 = ZBuild(true, ZCar, zcccc);
            Check(l1.Spots.Count == 1 && Near(l1.Spots[0], 0.10f) && l1.HasZebra(0), "ZCCCC: one section, its spot is the zebra (no W before it)");
            foreach (var c in zcccc)
            {
                Check(l1.Spots.StretchOf(c.Item2) == 0, "ZCCCC: every element in the zebra's section");
            }
            Check(l1.IsAfterZebra(0, 0.30f) && l1.IsAfterZebra(0, 0.60f), "ZCCCC: the Cs count as 'Cs after' the zebra");
            var car1 = ZCarAt(l1, ZStop(0.10f), zcccc);
            Check(car1.Classify(true, 0.60f, out float w1) == PointPhase.Deciding && Near(w1, car1.Wait(0.10f)), "ZCCCC: the last C holds the car before Z");
            Check(car1.Classify(true, 0.10f, out _) == PointPhase.Deciding, "ZCCCC: people on Z hold it before Z");
            Check(ZebraSections.RoomCheckDue(l1, car1.Next, car1.CanStopBefore(l1.Spots[0])) && Near(l1.SectionEnd(0), 0.63f), "ZCCCC: room is checked past the last C (section end)");

            // ---- CCCZ: wait before C1 for the three Cs; then before Z only if needed ----
            var cccz = new[] { Cv(0.10f, 0.15f), Cv(0.20f, 0.25f), Cv(0.30f, 0.35f), Zb(0.60f, 0.70f) };
            var l2 = ZBuild(true, ZCar, cccz);
            Check(l2.Spots.Count == 2 && Near(l2.Spots[0], 0.10f) && Near(l2.Spots[1], 0.60f), "CCCZ: W and the zebra are spots");
            Check(!l2.HasZebra(0) && l2.HasZebra(1) && l2.Merged == 0, "CCCZ: section 0 is the Cs, section 1 the zebra");
            var car2 = ZCarAt(l2, ZStop(0.10f), cccz);
            Check(car2.Classify(true, 0.30f, out float w2) == PointPhase.Deciding && Near(w2, car2.Wait(0.10f)), "CCCZ: the Cs are decided at W");
            Check(!ZebraSections.RoomCheckDue(l2, car2.Next, true), "CCCZ: no room check at W (section without zebra: stock)");
            var car2b = ZCarAt(l2, 0.40f, cccz);
            Check(car2b.Next == 1 && car2b.Classify(true, 0.60f, out float w2b) == PointPhase.Deciding && Near(w2b, car2b.Wait(0.60f)), "CCCZ: past the Cs, people on Z hold it before Z");
            Check(ZebraSections.RoomCheckDue(l2, car2b.Next, car2b.CanStopBefore(0.60f)), "CCCZ: room past Z is checked before Z");

            // ---- CCZCC: wait before C1 for C1+C2; then before Z for Z + the last two Cs ----
            var cczcc = new[] { Cv(0.10f, 0.15f), Cv(0.20f, 0.25f), Zb(0.50f, 0.60f), Cv(0.70f, 0.75f), Cv(0.80f, 0.85f) };
            var l3 = ZBuild(true, ZCar, cczcc);
            Check(l3.Spots.Count == 2 && Near(l3.Spots[1], 0.50f) && l3.HasZebra(1), "CCZCC: two sections, the second starts at Z");
            Check(l3.Spots.StretchOf(0.20f) == 0 && l3.Spots.StretchOf(0.70f) == 1 && l3.Spots.StretchOf(0.80f) == 1, "CCZCC: C1,C2 in section 0; C3,C4 in section 1");
            var car3 = ZCarAt(l3, ZStop(0.10f), cczcc);
            Check(car3.Classify(true, 0.20f, out _) == PointPhase.Deciding && car3.Classify(true, 0.80f, out float w3) == PointPhase.Deciding
                && Near(w3, car3.Wait(0.50f)), "CCZCC: at W the car decides C1,C2; C4 is decided later, at Z");
            var car3b = ZCarAt(l3, 0.35f, cczcc);
            Check(car3b.Classify(true, 0.80f, out float w3b) == PointPhase.Deciding && Near(w3b, car3b.Wait(0.50f)) && l3.IsAfterZebra(1, 0.80f), "CCZCC: before Z: Z plus C3,C4 hold it before Z");
            Check(Near(l3.SectionEnd(1), 0.85f) && Near(l3.LastEnd, 0.85f), "CCZCC: room is measured past C4");

            // ---- detail 2: a bus does not fit between the Cs and the zebra: one decision before C1 ----
            var ccz = new[] { Cv(0.10f, 0.15f), Cv(0.20f, 0.35f), Zb(0.60f, 0.70f) };   // gap 30 * 0.25 - 0.9 = 6.6 m
            Check(ZBuild(true, ZCar, ccz).Spots.Count == 2, "bus fit: a car (4.5 m) waits between the Cs and Z");
            var lb = ZBuild(true, ZBus, ccz);
            Check(lb.Spots.Count == 1 && lb.Merged == 1 && lb.HasZebra(0) && Near(lb.FirstZebra(0), 0.60f), "bus fit: a bus (12 m) merges Z into the first section");
            var bus = ZCarAt(lb, ZStop(0.10f), ccz);
            Check(bus.Classify(true, 0.60f, out float wb) == PointPhase.Deciding && Near(wb, bus.Wait(0.10f)), "bus fit: people on Z hold the bus before C1");
            Check(ZebraSections.RoomCheckDue(lb, bus.Next, true) && Near(lb.SectionEnd(0), 0.70f), "bus fit: room past Z is checked before C1");

            // ---- ZCZ (owner case 2, binding): short gap: one decision before Z1 ----
            var zczShort = new[] { Zb(0.10f, 0.20f), Cv(0.25f, 0.30f), Zb(0.35f, 0.45f) };     // 30 * 0.05 - 0.9 = 0.6 m
            var l4 = ZBuild(true, ZCar, zczShort);
            Check(l4.Spots.Count == 1 && l4.Merged == 1, "ZCZ short: Z2 merged into Z1's section");
            var car4 = ZCarAt(l4, ZStop(0.10f), zczShort);
            Check(car4.Classify(true, 0.35f, out float w4) == PointPhase.Deciding && Near(w4, car4.Wait(0.10f)), "ZCZ short: people on Z2 hold the car before Z1");
            Check(car4.Classify(true, 0.25f, out _) == PointPhase.Deciding, "ZCZ short: C holds it before Z1");
            Check(ZebraSections.RoomCheckDue(l4, car4.Next, true) && Near(l4.SectionEnd(0), 0.45f), "ZCZ short: room is needed past Z2");
            var l4s = ZBuild(false, ZCar, zczShort);
            Check(l4s.Spots.Count == 1 && l4s.Merged == 1 && Near(l4s.SectionEnd(0), 0.45f), "ZCZ short, stock gap rule: zebras merge the same way (Z1 to Z2: 30 * 0.15 - 0.9 = 3.6 m < 4.5 m)");

            // ---- ZCZ long gap: the car waits between the zebras ----
            var zczLong = new[] { Zb(0.10f, 0.20f), Cv(0.25f, 0.30f), Zb(0.60f, 0.70f) };      // 30 * 0.30 - 0.9 = 8.1 m
            var l5 = ZBuild(true, ZCar, zczLong);
            Check(l5.Spots.Count == 2 && l5.Merged == 0 && Near(l5.Spots[1], 0.60f), "ZCZ long: two sections");
            var car5 = ZCarAt(l5, ZStop(0.10f), zczLong);
            Check(car5.Classify(true, 0.60f, out float w5) == PointPhase.Deciding && Near(w5, car5.Wait(0.60f)), "ZCZ long: people on Z2 do not hold the car before Z1");
            Check(ZebraSections.RoomCheckDue(l5, 0, true) && Near(l5.SectionEnd(0), 0.30f), "ZCZ long: entering Z1 needs room past the C (between the zebras)");
            var car5b = ZCarAt(l5, 0.45f, zczLong);
            Check(car5b.Next == 1 && car5b.Classify(true, 0.60f, out float w5b) == PointPhase.Deciding && Near(w5b, car5b.Wait(0.60f)), "ZCZ long: between the zebras it waits before Z2");
            float between = ZLane * (0.60f - 0.30f) - M;
            Check(between >= ZCar, "ZCZ long: the whole car fits between C and Z2 clear of both");

            // ---- ZCZ: a bus does not fit between the zebras, a car does ----
            var zczMid = new[] { Zb(0.10f, 0.20f), Cv(0.25f, 0.30f), Zb(0.50f, 0.60f) };       // 30 * 0.20 - 0.9 = 5.1 m
            Check(ZBuild(true, ZCar, zczMid).Spots.Count == 2, "ZCZ mid: a car waits between the zebras");
            var l6 = ZBuild(true, ZBus, zczMid);
            Check(l6.Spots.Count == 1 && l6.Merged == 1, "ZCZ mid: a bus is one decision before Z1");
            var bus6 = ZCarAt(l6, ZStop(0.10f), zczMid);
            Check(bus6.Classify(true, 0.50f, out float w6) == PointPhase.Deciding && Near(w6, bus6.Wait(0.10f)), "ZCZ mid: people on Z2 hold the bus before Z1");

            // ---- chain of three zebras with short gaps: one decision; a far third zebra keeps its own spot ----
            var chain = new[] { Zb(0.10f, 0.15f), Cv(0.20f, 0.22f), Zb(0.27f, 0.32f), Cv(0.37f, 0.40f), Zb(0.45f, 0.50f) };
            var l7 = ZBuild(true, ZCar, chain);
            Check(l7.Spots.Count == 1 && l7.Merged == 2 && Near(l7.SectionEnd(0), 0.50f), "chain of 3: Z C Z C Z with short gaps is one decision before Z1");
            var car7 = ZCarAt(l7, ZStop(0.10f), chain);
            Check(car7.Classify(true, 0.45f, out float w7) == PointPhase.Deciding && Near(w7, car7.Wait(0.10f)), "chain of 3: people on Z3 hold the car before Z1");
            var chainFar = new[] { Zb(0.10f, 0.15f), Cv(0.20f, 0.22f), Zb(0.27f, 0.32f), Cv(0.37f, 0.40f), Zb(0.80f, 0.85f) };
            var l7b = ZBuild(true, ZCar, chainFar);
            Check(l7b.Spots.Count == 2 && l7b.Merged == 1 && Near(l7b.Spots[1], 0.80f), "chain: Z1+Z2 merged, a far Z3 keeps its own spot");
            var l7s = ZBuild(false, ZCar, Zb(0.10f, 0.15f), Zb(0.20f, 0.25f), Zb(0.30f, 0.35f));
            Check(l7s.Spots.Count == 1 && l7s.Merged == 2, "chain, stock gap rule: three close zebras are one decision");

            // ---- mid-block zebra: a crossing node, the lane's only element is the zebra ----
            var mid = new[] { Zb(0.40f, 0.60f) };
            foreach (bool sections in new[] { true, false })
            {
                var lm = ZBuild(sections, ZCar, mid);
                Check(lm.Spots.Count == 1 && Near(lm.Spots[0], 0.40f) && lm.HasZebra(0) && Near(lm.LastEnd, 0.60f), "mid-block: one section at the zebra (" + (sections ? "whole junction" : "stock") + ")");
                Check(ZebraSections.RoomCheckDue(lm, 0, true), "mid-block: room past the zebra is checked");
                Check(!ZebraSections.RoomCheckDue(lm, 0, false), "mid-block: a car that cannot stop any more is not held (stock)");
                Check(!ZebraSections.RoomCheckDue(lm, 1, true), "mid-block: past the zebra nothing is held");
            }

            // ---- no zebra / stop line / zebra flags ----
            var none = new ZElems { Items = new[] { Cv(0.1f, 0.2f), (ZebraSections.Clearance, 0.3f, 0.4f) } };
            Check(!ZebraSections.Build(ref none, true, false, ZLane, M, ZCar, out _), "lane without a zebra: nothing changes (0.4.0 state)");
            var atl = new ZElems { Items = new[] { Cv(0.2f, 0.25f), Zb(0.6f, 0.7f) } };
            ZebraSections.Build(ref atl, true, true, ZLane, M, ZCar, out ZebraLayout la);
            Check(la.Spots.Count == 2 && la.Spots[0] == 0f && Near(la.Spots[1], 0.6f), "wait at the stop line: W moves to the lane start, zebra spots stay");
            Check(ZebraSections.IsZebra(ZebraSections.PedestrianCrosswalk), "a crosswalk lane is a zebra");
            Check(!ZebraSections.IsZebra(ZebraSections.PedestrianCrosswalk | ZebraSections.PedestrianUnsafe), "a removed zebra (Unsafe crosswalk lane) is not");
            Check(!ZebraSections.IsZebra(0) && !ZebraSections.IsZebra(ZebraSections.PedestrianUnsafe), "pavements / jaywalk lanes are not zebras");
        }

        private static void ZebraRoomTests()
        {
            // Needed = whole length + margin (default 1 m, the game's per-car spacing).
            var r0 = RoomScan.Begin(ZCar, RoomScan.DefaultMargin);
            Check(Near(r0.Needed, 5.5f) && r0.HasRoom && r0.Done(5.5f) && !r0.Done(5.4f), "empty exit: room; the scan ends once the read path covers length + margin");

            // Exit queue full: a standing car 3 m past the zebra.
            var full = RoomScan.Begin(ZCar, 1f);
            full.Add(3f, 4.5f, 0f);
            Check(!full.HasRoom && full.Done(0f), "exit queue full: standing car 3 m past the zebra holds the car");

            // Exit queue moving: the same car moving with a 4 m braking distance (the game's credit) lets it in.
            var moving = RoomScan.Begin(ZCar, 1f);
            moving.Add(3f, 4.5f, BrakingDistance(5f));
            Check(BrakingDistance(5f) > 2.5f && moving.HasRoom, "exit queue moving: the car ahead's braking distance counts as room (no crawl)");
            Check(!moving.Done(10f) && moving.Done(11f), "moving queue: the scan reads on past the first car (length + 1 m occupied)");
            moving.Add(9f, 4.5f, 0f);
            Check(!moving.HasRoom, "moving car close behind a standing one: the standing one decides (room 9 - 5.5 = 3.5 m)");
            var movingFar = RoomScan.Begin(ZCar, 1f);
            movingFar.Add(3f, 4.5f, BrakingDistance(5f));
            movingFar.Add(20f, 4.5f, 0f);
            Check(movingFar.HasRoom, "moving queue with space behind its tail: room");

            // Owner margin is tunable.
            var m0 = RoomScan.Begin(ZCar, 0f); m0.Add(5f, 4.5f, 0f);
            var m3 = RoomScan.Begin(ZCar, 3f); m3.Add(5f, 4.5f, 0f);
            Check(m0.HasRoom && !m3.HasRoom, "margin 0 m lets a car in with 5 m free, margin 3 m holds it");
            var bus = RoomScan.Begin(ZBus, 1f); bus.Add(10f, 4.5f, 0f);
            Check(!bus.HasRoom, "a bus needs its whole 12 m (+ margin) past the zebra");
            var onZebra = RoomScan.Begin(ZCar, 1f); onZebra.Add(-1f, 4.5f, 10f);
            Check(onZebra.HasRoom, "a fast car still leaving the zebra ahead: its braking distance promises the room");
            var stuckOn = RoomScan.Begin(ZCar, 1f); stuckOn.Add(-1f, 4.5f, 0f);
            Check(!stuckOn.HasRoom, "a standing car on the section end: no room");
            Check(RoomScan.MinMargin == 0f && RoomScan.MaxMargin == 10f && RoomScan.StepMargin == 0.5f && RoomScan.DefaultMargin == 1f, "margin slider 0-10 m, step 0.5, default 1 m");
        }

        private static void ZebraSettingTests()
        {
            // Setting off = the 0.4.0 mode, bit for bit.
            foreach (var rule in new[] { JunctionGapRule.Stock, JunctionGapRule.WholeJunction })
            {
                foreach (bool counting in new[] { false, true })
                {
                    int off = Stage2Mode.For(rule, counting, JunctionWaitingSpot.BeforeFirstCrossing, true, true, true, false, 3f);
                    int legacy = Stage2Mode.For(rule, counting, JunctionWaitingSpot.BeforeFirstCrossing, true, true, true);
                    Check(off == legacy && (off & Stage2Mode.Stripped & ~Stage2Mode.NoPushPeople) == 0, "setting off: same mode as 0.4.0 (" + rule + ", counting " + counting + ")");
                    int on = Stage2Mode.For(rule, counting, JunctionWaitingSpot.BeforeFirstCrossing, true, true, true, true, 2.5f);
                    Check((on & ~Stage2Mode.Stripped) == (legacy & ~Stage2Mode.Stripped), "setting on: the 0.2.x rule bits are unchanged (stripped on the job side)");
                    Check((on & Stage2Mode.ZebraClear) != 0 && Near(Stage2Mode.ZebraMargin(on), 2.5f), "setting on: bit and margin travel in the mode");
                }
            }
            Check(Stage2Mode.ZebraBits(false, 5f) == 0, "off: no zebra bits at all");
            foreach (float m in new[] { 0f, 0.5f, 1f, 7.5f, 10f })
            {
                Check(Near(Stage2Mode.ZebraMargin(Stage2Mode.ZebraBits(true, m)), m), "margin round trip " + m + " m");
            }
            Check(Near(Stage2Mode.ZebraMargin(Stage2Mode.ZebraBits(true, 99f)), 25.5f) && Stage2Mode.ZebraMargin(Stage2Mode.ZebraBits(true, -2f)) == 0f, "margin clamped to the 8-bit field");
            Check((Stage2Mode.Stripped & (Stage2Mode.Rule | Stage2Mode.Count | Stage2Mode.AtLine | Stage2Mode.TurnGivesWay | Stage2Mode.LeftHandTraffic)) == 0, "stripped bits never touch the 0.2.x rule bits");
            Check(!Stage2Mode.RuleName(Stage2Mode.For(JunctionGapRule.WholeJunction, false)).Contains("zebra"), "rule name without the setting: unchanged");
            Check(Stage2Mode.RuleName(Stage2Mode.For(JunctionGapRule.WholeJunction, false, zebraClear: true, zebraMargin: 1f)).EndsWith("; cars never stop on a zebra (room margin 1.0 m)"), "rule name with the setting");

            // Counters.
            Check(Stage2Counters.ZebraHeldPeople == Stage2Counters.CrossingsNoFit + 1 && Stage2Counters.ZebraMerged == Stage2Counters.BoxHeldRoom - 1, "zebra counters after the 0.3.3 ones (0.4.2 box counters follow)");
            var dl = new long[Stage2Counters.Count];
            dl[Stage2Counters.ZebraHeldPeople] = 4; dl[Stage2Counters.ZebraHeldCs] = 3; dl[Stage2Counters.ZebraHeldRoom] = 2; dl[Stage2Counters.ZebraMerged] = 1;
            string on2 = Stage2Counters.Format(2, dl, 2, 2);
            Check(on2.Contains("; never push through people on crossings on; never stop on a zebra on;") && on2.Contains("; zebra entries held: people 4, Cs after 3, no room past 2 (zebra sections merged, vehicle does not fit between: 1); points at W 0"), "timing log: zebra counters " + on2);
            string off2 = Stage2Counters.Format(2, dl, 2, 1);
            Check(off2.Contains("never stop on a zebra off") && !off2.Contains("zebra entries held"), "timing log, setting off: no zebra counts");
            Check(!Stage2Counters.Format(2, dl, 2).Contains("zebra"), "timing log without the zebra mask: the 0.4.0 text");
        }

        private static void ZebraRepoTests()
        {
            string root = RepoRoot();
            string src = Path.Combine(root, "JunctionRules");
            string decomp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Code", "decomp", "yb-2026-11-13", "full");
            string flags = Path.Combine(decomp, "Game.Net", "PedestrianLaneFlags.cs");
            if (File.Exists(flags))
            {
                string f = File.ReadAllText(flags);
                Check(f.Contains("Unsafe = 1,") && f.Contains("Crosswalk = 2,"), "decompile: PedestrianLaneFlags Unsafe = 1, Crosswalk = 2 (as mirrored)");
                string ls = File.ReadAllText(Path.Combine(decomp, "Game.Net", "LaneSystem.cs"));
                Check(ls.Contains("PedestrianLaneFlags.Crosswalk") && ls.Contains("CreateNodePedestrianLanes"), "decompile: crosswalks are node pedestrian lanes (also mid-block)");
            }
            string it = File.ReadAllText(Path.Combine(src, "Vanilla", "JRCarLaneSpeedIterator.cs"));
            int s04 = it.IndexOf("BEGIN S2-04", StringComparison.Ordinal), s25 = it.IndexOf("BEGIN S2-25", StringComparison.Ordinal), s05 = it.IndexOf("BEGIN S2-05", StringComparison.Ordinal);
            Check(s04 > 0 && s04 < s25 && s25 < s05, "S2-25 sits after S2-04, before the overlap loop");
            Check(it.Contains("if (m_JR.m_ZebraRule)\n\t\t{\n\t\t\tfloat jrZebraHold") || it.Contains("if (m_JR.m_ZebraRule)\r\n"), "S2-25 does nothing with the setting off");
            Check(it.Contains("bool jrInScope = (m_JR.m_Mode != 0 || m_JR.m_ZebraActive) && m_JR.InScope("), "S2-05: zebras in scope also with the stock gap rule");
            Check(it.Contains("m_Blocker = Entity.Null;\n\t\t\t\tm_BlockerType = BlockerType.Continuing;") || it.Contains("m_Blocker = Entity.Null;\r\n"), "room hold sets no blocker (no IgnoreBlocker creep through a held car)");
            string nav = File.ReadAllText(Path.Combine(src, "Vanilla", "JRCarNavigationSystem.cs"));
            Check(nav.IndexOf("BEGIN S2-26", StringComparison.Ordinal) > nav.IndexOf("BEGIN S2-21", StringComparison.Ordinal) && nav.Contains("m_JR.SetPath(navigationLanes)"), "S2-26 hands over the navigation lanes after S2-21");
            string job = File.ReadAllText(Path.Combine(src, "Stage2", "JRWholeJunction.cs"));
            Check(job.Contains("j.m_ZebraRule = (mode & Stage2Mode.ZebraClear) != 0;") && job.Contains("mode &= ~Stage2Mode.Stripped;"), "Create strips the zebra bits (0.2.x m_Mode == 0 exits unchanged)");
            Check(job.Contains("if (m_OvZebra && !RuleOn)") && job.Contains("if (isCrossing && RuleOn && !m_OvZebra)"), "zebras skip the 0.3.3 judged-at-crossing branch; other crossings keep it");
            string settings = File.ReadAllText(Path.Combine(src, "Settings", "JunctionRulesSettings.cs"));
            Check(settings.Contains("ZebraNonBlocking = true;") && settings.Contains("ZebraRoomMargin = RoomScan.DefaultMargin;"), "setting default on, margin 1 m");
            Check(settings.Contains("\"Cars never stop on a zebra\"") && settings.Contains("nameof(ZebraMarginDisabled)"), "setting label; slider greyed while off");
            string sw = File.ReadAllText(Path.Combine(src, "Systems", "CarDrivingSwitchSystem.cs"));
            Check(sw.Contains("Mod.Settings.ZebraNonBlocking") && sw.Contains("Mod.Settings.ZebraRoomMargin"), "switch system hands the setting over every step");
            string core = File.ReadAllText(Path.Combine(src, "Core", "ZebraSections.cs"));
            string code = System.Text.RegularExpressions.Regex.Replace(core, @"//.*", "");
            Check(!System.Text.RegularExpressions.Regex.IsMatch(code, @"\bnew\s+\w"), "zebra sections code has no 'new' (Burst, no allocation)");
        }
    }
}
