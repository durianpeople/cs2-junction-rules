using System;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.2 (JR-T-010): cars obey yellow boxes (Core/BoxDriving.cs, Core RoomScan shared with #14, Stage2/JRYellowBox.cs).
    /// Box at a T and a 4-way, exit full / moving, the UK right-turn exception, the entrance band (main road and leaving
    /// the driveway), bus length, setting off = 0.4.1 behaviour.
    /// </summary>
    internal static partial class Program
    {
        private const float BCar = 4.5f;
        private const float BBus = 12f;
        private const float BArtic = 18f;   // bus + trailer / articulated: whole length incl. trailers

        /// <summary>Junction box room test as JRYellowBox.BoxHold runs it: in-box queue first, then the exit vehicles.</summary>
        private static bool BoxRoom(float length, float margin, int queuedInBox, params (float Rear, float Len, float Credit)[] exit)
        {
            var r = RoomScan.Begin(length, margin);
            for (int i = 0; i < queuedInBox; i++) r.AddQueued(BCar);
            foreach (var v in exit) r.Add(v.Rear, v.Len, v.Credit);
            return r.HasRoom;
        }

        private static void BoxJunctionTests()
        {
            // Classify: before the line and able to stop -> the room test decides; inside / cannot stop -> carry on.
            Check(BoxDriving.Classify(false, 10f, M, BrakingDistance(5f)) == BoxDriving.Approach.Check, "before the line, can stop: check");
            Check(BoxDriving.Classify(true, 10f, M, 0f) == BoxDriving.Approach.Inside, "current lane on the boxed node: already in the box, carry on");
            Check(BoxDriving.Classify(false, -1f, M, 0f) == BoxDriving.Approach.Inside, "front past the line: carry on");
            Check(BoxDriving.Classify(false, 1f, M, BrakingDistance(15f)) == BoxDriving.Approach.CannotStop, "too fast to stop at the line: carry on (game's can-stop test)");
            Check(!BoxDriving.Holds(BoxDriving.Approach.Inside, false) && !BoxDriving.Holds(BoxDriving.Approach.CannotStop, false), "in the box / cannot stop: never held");
            Check(BoxDriving.Holds(BoxDriving.Approach.Check, false) && !BoxDriving.Holds(BoxDriving.Approach.Check, true), "at the line: held only without room");

            // T junction (no oncoming straight for the side-road turner): one spot, the stop line.
            Check(BoxDriving.JunctionSpots(true, 2f, out WaitingSpots t) && t.Count == 1 && t[0] == 0f, "box at a T: one decision at the stop line");
            Check(!BoxDriving.JunctionSpots(false, 0.5f, out _), "lane without conflict points: no whole-junction spots (stock), room test only");
            // 4-way: the turner across oncoming gets its turn spot as the second spot.
            Check(BoxDriving.JunctionSpots(true, 0.55f, out WaitingSpots x) && x.Count == 2 && x[0] == 0f && Near(x[1], 0.55f), "box at a 4-way, turner: stop line + turn spot");
            Check(BoxDriving.JunctionSpots(true, 0f, out WaitingSpots x0) && x0.Count == 1, "turn spot at the line itself: one spot");

            // Exit full / exit moving (the exit lane beyond the box, distances from the box exit to the rear of each vehicle).
            Check(BoxRoom(BCar, 1f, 0), "empty exit: enter");
            Check(!BoxRoom(BCar, 1f, 0, (2f, BCar, 0f)), "exit full: a standing car 2 m past the box holds the car at the line");
            Check(BoxRoom(BCar, 1f, 0, (2f, BCar, BrakingDistance(8f))), "exit moving: the car ahead's braking distance counts (no crawl)");
            Check(!BoxRoom(BCar, 1f, 0, (2f, BCar, BrakingDistance(8f)), (9f, BCar, 0f)), "moving car close behind a standing one: held");
            // Vehicles already in the box ahead on the own path take the exit room first.
            Check(BoxRoom(BCar, 1f, 0, (10f, BCar, 0f)) && !BoxRoom(BCar, 1f, 1, (10f, BCar, 0f)), "a car in the box ahead (same exit) takes 5.5 m of exit room first");
            var q = RoomScan.Begin(BCar, 1f); q.AddQueued(BCar);
            Check(q.HasRoom && q.Obstacles == 1 && !q.Done(10.9f) && q.Done(11f), "AddQueued: no block test, occupies length + 1 m");
            var qb = RoomScan.Begin(BCar, 1f); qb.Add(0f, BCar, 0f); qb.AddQueued(BCar);
            Check(qb.Blocked && qb.Obstacles == 1, "AddQueued after a block: ignored");

            // Bus length: same exit, a car fits, a bus does not; trailers count (whole length).
            Check(BoxRoom(BCar, 1f, 0, (8f, BCar, 0f)) && !BoxRoom(BBus, 1f, 0, (8f, BCar, 0f)), "bus length: 8 m free lets a car in, holds a 12 m bus");
            Check(BoxRoom(BBus, 1f, 0, (13f, BCar, 0f)) && !BoxRoom(BArtic, 1f, 0, (13f, BCar, 0f)), "trailers: 13 m free lets a bus in, holds an 18 m vehicle with trailer");
        }

        private static void BoxTurnTests()
        {
            uint lhtRight = TurnAcross.TurnRight, lhtLeft = TurnAcross.TurnLeft;
            Check(BoxDriving.TurnerMayWait(true, lhtRight, true) && !BoxDriving.TurnerMayWait(true, lhtLeft, true), "LHT: the right turn crosses oncoming and may wait");
            Check(BoxDriving.TurnerMayWait(true, lhtLeft, false) && !BoxDriving.TurnerMayWait(true, lhtRight, false), "RHT: the left turn may wait");
            Check(!BoxDriving.TurnerMayWait(false, lhtRight, true), "exception off: no turner waits in the box");
            Check(!BoxDriving.TurnerMayWait(true, TurnAcross.Forward, true), "straight ahead: no exception");
            Check(BoxDriving.TurnerMayWait(true, TurnAcross.GentleTurnRight, true) && BoxDriving.TurnerMayWait(true, TurnAcross.UTurnRight, true), "gentle right and U-turn (LHT) cross oncoming too (TurnAcross set)");
            Check(BoxDriving.IsOncomingStraight(TurnAcross.Forward, true), "oncoming straight: Forward lane whose end edge is the turner's start edge");
            Check(!BoxDriving.IsOncomingStraight(TurnAcross.Forward, false), "crossing straight from a side arm: not oncoming (decided at the line)");
            Check(!BoxDriving.IsOncomingStraight(TurnAcross.TurnLeft, true), "oncoming turner: not a straight");

            // The turn spot goes into the zebra layout only where the zebra rule lets a car stand.
            // Lane 30 m: zebra on the entry [0, 0.1] (3 m), the oncoming straight crossed at 0.5 (15 m).
            var items = new ZElems { Items = new[] { Zb(0f, 0.1f), Cv(0.5f, 0.6f) } };
            Check(ZebraSections.Build(ref items, true, true, ZLane, M, BCar, 0.5f, out ZebraLayout ok) && ok.ExtraIndex == 1 && ok.Spots.Count == 2
                && ok.Spots[0] == 0f && Near(ok.Spots[1], 0.5f) && ok.HasZebra(0) && !ok.HasZebra(1), "turn spot inserted after the entry zebra (car fits 12 m clear of it)");
            Check(ZebraSections.Build(ref items, true, true, ZLane, M, BCar, 0.05f, out ZebraLayout onZ) && onZ.ExtraIndex == -1 && onZ.Spots.Count == 1, "turn spot on a zebra: refused (zebra rule wins)");
            Check(ZebraSections.Build(ref items, true, true, ZLane, M, BCar, 0.2f, out ZebraLayout tight) && tight.ExtraIndex == -1, "turn spot 3 m past the zebra: car does not fit clear of it, refused");
            Check(ZebraSections.Build(ref items, true, true, ZLane, M, BBus, 0.5f, out ZebraLayout bus) && bus.ExtraIndex == -1, "bus (12 m) at the same turn spot: does not fit clear of the zebra, decides at the line");
            Check(ZebraSections.Build(ref items, true, true, ZLane, M, BCar, 2f, out ZebraLayout none) && none.ExtraIndex == -1 && none.Spots.Count == 1, "no turn spot (2 = none): the 0.4.1 layout");
            Check(ZebraSections.Build(ref items, true, true, ZLane, M, BCar, out ZebraLayout legacy) && legacy.Spots.Count == none.Spots.Count && legacy.LastEnd == none.LastEnd, "old overload = extra spot none");
            // Zebra ahead of the turn spot keeps its own section index after the shift.
            var two = new ZElems { Items = new[] { Zb(0f, 0.1f), Cv(0.5f, 0.6f), Zb(0.8f, 0.9f) } };
            Check(ZebraSections.Build(ref two, true, true, ZLane, M, BCar, 0.5f, out ZebraLayout sh) && sh.ExtraIndex == 1 && sh.Spots.Count == 3 && sh.HasZebra(2) && Near(sh.FirstZebra(2), 0.8f),
                "zebra after the turn spot: its section moves up one (zebra bits shifted)");
            Check(ZebraSections.OnZebra(ref two, 0.85f) && !ZebraSections.OnZebra(ref two, 0.5f), "OnZebra");

            // Spots without zebras: the turn spot sits as the 2nd waiting spot (JunctionSpots) and StretchState counts the
            // turner as deciding at the line for points before it and at the turn spot for the oncoming points.
            var pts = new ZConflicts { Items = new[] { Cv(0.3f, 0.35f), Cv(0.55f, 0.6f) } };
            BoxDriving.JunctionSpots(true, 0.55f, out WaitingSpots sp);
            Check(sp.StretchOf(0.3f) == 0 && sp.StretchOf(0.55f) == 1, "points before the turn spot decided at the line, the oncoming ones at the turn spot");
            Check(pts.Count == 2, "conflict list");
        }

        private static void BoxEntranceTests()
        {
            const float lane = 60f, band = 8f;
            // Access at the middle: band 26-34 m (0.4333..0.5667).
            Check(BoxDriving.Band(0.5f, lane, band, false, out float b0, out float b1) && Near(b0, 26f / 60f) && Near(b1, 34f / 60f), "band centred on the access, painted length");
            Check(BoxDriving.Band(0.5f, lane, band, true, out float i0, out float i1) && Near(i0, 26f / 60f) && Near(i1, 34f / 60f), "inverse lane: same band in driving coordinates (centred)");
            Check(BoxDriving.Band(0.1f, lane, band, true, out float j0, out float j1) && Near(j0, 1f - 10f / 60f) && Near(j1, 1f - 2f / 60f), "inverse lane: band mirrored");
            Check(BoxDriving.Band(0.02f, lane, band, false, out float c0, out float c1) && c0 == 0f && Near(c1, 5.2f / 60f), "band clipped at the lane start");
            Check(!BoxDriving.Band(1.2f, lane, band, false, out _, out _) && !BoxDriving.Band(0.5f, 0f, band, false, out _, out _), "band off the lane / zero length lane: none");

            // Main road: through the whole band, entry = band start.
            Check(BoxDriving.Role(0f, 1f, b0, b1, out float e) == BoxDriving.EntranceRole.Main && Near(e, b0), "main road through the band: entry at the band start");
            Check(BoxDriving.Role(0f, 0.3f, b0, b1, out _) == BoxDriving.EntranceRole.None, "path ends before the band: none");
            Check(BoxDriving.Role(0.7f, 1f, b0, b1, out _) == BoxDriving.EntranceRole.None, "path starts after the band: none");
            // Leaving the driveway: joins the lane inside the band, entry = the join point.
            Check(BoxDriving.Role(0.5f, 1f, b0, b1, out float lj) == BoxDriving.EntranceRole.Leaving && Near(lj, 0.5f), "leaving the driveway: joins inside the band, entry = join point");
            // Entering the driveway: path ends inside the band: exempt.
            Check(BoxDriving.Role(0f, 0.5f, b0, b1, out _) == BoxDriving.EntranceRole.Entering, "turning into the driveway: exempt");
            Check(BoxDriving.Role(0f, 1f, 0.9f, 1f, out _) == BoxDriving.EntranceRole.Main, "band at the lane end: through traffic still Main (path runs on)");

            // The room past the band: the same RoomScan (main road queue standing at the band end holds the main road car).
            Check(!BoxRoom(BCar, 1f, 0, (1f, BCar, 0f)), "main road: queue standing 1 m past the band: held before the band");
            Check(BoxRoom(BCar, 1f, 0, (1f, BCar, BrakingDistance(8f))), "main road: queue moving: enter");
            // Leaving: vehicles already between the join point and the band end on the target lane count as queued.
            Check(BoxRoom(BCar, 1f, 0, (7f, BCar, 0f)) && !BoxRoom(BCar, 1f, 1, (7f, BCar, 0f)), "leaving the driveway: a car in the band ahead takes room first; no room past the band on the target lane: held");
            Check(!BoxRoom(BBus, 1f, 0, (7f, BCar, 0f)), "bus leaving a depot: needs its whole length past the band");
        }

        private static void BoxSettingTests()
        {
            // Setting off = the 0.4.1 mode, bit for bit.
            Check(Stage2Mode.BoxBits(false, true, true, true, true, 8f) == 0, "setting off: no box bits");
            foreach (var rule in new[] { JunctionGapRule.Stock, JunctionGapRule.WholeJunction })
            {
                foreach (bool zebra in new[] { false, true })
                {
                    int legacy = Stage2Mode.For(rule, true, JunctionWaitingSpot.BeforeFirstCrossing, true, true, true, zebra, 2f);
                    int off = Stage2Mode.For(rule, true, JunctionWaitingSpot.BeforeFirstCrossing, true, true, true, zebra, 2f, Stage2Mode.BoxBits(false, true, true, true, true, 8f));
                    Check(off == legacy, "setting off: same mode as 0.4.1 (" + rule + ", zebra " + zebra + ")");
                    int on = Stage2Mode.For(rule, true, JunctionWaitingSpot.BeforeFirstCrossing, true, true, true, zebra, 2f, Stage2Mode.BoxBits(true, true, true, true, false, 8f));
                    Check((on & ~Stage2Mode.Stripped) == (legacy & ~Stage2Mode.Stripped), "setting on: 0.2.x rule bits unchanged (stripped on the job side)");
                    Check((on & Stage2Mode.BoxObey) != 0 && Near(Stage2Mode.ZebraMargin(on), 2f) && Near(Stage2Mode.BoxEntranceLength(on), 8f), "box on: margin and band length travel in the mode");
                    Check(((on & Stage2Mode.ZebraClear) != 0) == zebra, "box on does not switch the zebra rule");
                }
            }
            int bits = Stage2Mode.BoxBits(true, false, false, false, true, 12.5f);
            Check((bits & Stage2Mode.BoxTurnWait) == 0 && (bits & Stage2Mode.BoxLeftHand) == 0 && (bits & Stage2Mode.BoxJunctions) == 0 && (bits & Stage2Mode.BoxEntrances) != 0 && Near(Stage2Mode.BoxEntranceLength(bits), 12.5f), "box bits encode each setting");
            Check(Near(Stage2Mode.BoxEntranceLength(Stage2Mode.BoxBits(true, true, true, true, true, 999f)), 51.1f), "band length clamped to the 9-bit field");
            Check((Stage2Mode.Stripped & (Stage2Mode.BoxObey | Stage2Mode.BoxTurnWait | Stage2Mode.BoxLeftHand | Stage2Mode.BoxJunctions | Stage2Mode.BoxEntrances | Stage2Mode.BoxEntranceMask)) ==
                (Stage2Mode.BoxObey | Stage2Mode.BoxTurnWait | Stage2Mode.BoxLeftHand | Stage2Mode.BoxJunctions | Stage2Mode.BoxEntrances | Stage2Mode.BoxEntranceMask), "Create strips every box bit");
            Check((Stage2Mode.BoxEntranceMask & (Stage2Mode.Rule | Stage2Mode.Count | Stage2Mode.AtLine | Stage2Mode.TurnGivesWay | Stage2Mode.LeftHandTraffic | Stage2Mode.NoPushPeople | Stage2Mode.ZebraClear | Stage2Mode.ZebraMarginMask | Stage2Mode.BoxEntrances)) == 0, "box bits do not overlap other bits");
            Check(Stage2Mode.BoxEntranceMask > 0, "box bits fit a positive int");
            string name = Stage2Mode.RuleName(Stage2Mode.For(JunctionGapRule.WholeJunction, false, boxBits: Stage2Mode.BoxBits(true, true, true, true, true, 8f)));
            Check(name.EndsWith("; cars obey yellow boxes (room margin 1.0 m, exit boxes driven over their painted windows (box length setting 8.0 m); turning across oncoming may wait in the box, left-hand traffic)"), "rule name with the setting: " + name);
            Check(!Stage2Mode.RuleName(Stage2Mode.For(JunctionGapRule.WholeJunction, false)).Contains("yellow"), "rule name without the setting: unchanged");
            Check(Stage2Mode.RuleName(Stage2Mode.For(JunctionGapRule.Stock, false, boxBits: Stage2Mode.BoxBits(true, false, false, true, true, 8f))).Contains("no waiting in the box"), "rule name, exception off");

            // Switch messages.
            var sw = new GapRuleSwitch();
            string s1 = sw.Step(JunctionGapRule.Stock, false, out _, boxObey: true, anyJunctionBox: true);
            Check(s1 != null && s1.Contains("cars obey yellow boxes") && s1.EndsWith("; yellow boxes present: junctions yes, entrances no"), "first step names the box rule and boxes present: " + s1);
            string s2 = sw.Step(JunctionGapRule.Stock, false, out _, boxObey: true, anyJunctionBox: true, anyEntranceBox: true);
            Check(s2 == "yellow boxes in the city changed; yellow boxes present: junctions yes, entrances yes", "box placed: message " + s2);
            string s3 = sw.Step(JunctionGapRule.Stock, false, out _, boxObey: false, anyJunctionBox: true, anyEntranceBox: true);
            Check(s3 != null && s3.StartsWith("junction gap rule switched to stock") && !s3.Contains("yellow"), "setting off: switch message " + s3);
            Check(sw.Step(JunctionGapRule.Stock, false, out _, boxObey: false, anyJunctionBox: false) == null, "setting off: box presence changes nothing (no message)");

            // Counters.
            Check(Stage2Counters.BoxHeldRoom == Stage2Counters.ZebraScanUnsorted + 1 && Stage2Counters.EntranceHeldLeaving == Stage2Counters.ExitHeld - 1, "box counters after the zebra ones");
            var dl = new long[Stage2Counters.Count];
            dl[Stage2Counters.BoxHeldRoom] = 7; dl[Stage2Counters.BoxTurnWaits] = 3; dl[Stage2Counters.EntranceHeldMain] = 2; dl[Stage2Counters.EntranceHeldLeaving] = 1;
            string on2 = Stage2Counters.Format(2, dl, 2, 2, 2);
            Check(on2.Contains("; obey yellow boxes on") && on2.Contains("; box entries held 7, right-turn waits 3; entrance band holds: main road 2, leaving driveway 1"), "timing log: box counters " + on2);
            string off2 = Stage2Counters.Format(2, dl, 2, 2, 1);
            Check(off2.Contains("obey yellow boxes off") && !off2.Contains("box entries held"), "timing log, setting off: no box counts");
            Check(!Stage2Counters.Format(2, dl, 2, 2).Contains("yellow"), "timing log without the box mask: the 0.4.1 text");
        }

        private static void BoxRepoTests()
        {
            string root = RepoRoot();
            string src = Path.Combine(root, "JunctionRules");
            string it = File.ReadAllText(Path.Combine(src, "Vanilla", "JRCarLaneSpeedIterator.cs"));
            int b28 = it.IndexOf("// JR-STAGE2 BEGIN S2-28:", StringComparison.Ordinal), b28b = it.IndexOf("// JR-STAGE2 BEGIN S2-28b:", StringComparison.Ordinal);
            Check(b28 > 0 && b28b > b28, "S2-28 and S2-28b present");
            foreach (int b in new[] { b28, b28b })
            {
                int sig = it.LastIndexOf("private void CheckCurrentLane(", b, StringComparison.Ordinal);
                int brace = it.IndexOf('{', sig);
                Check(sig > 0 && it.Substring(brace + 1, b - brace - 1).Trim().Length == 0, "box block is the first statement of CheckCurrentLane");
                int end = it.IndexOf("JR-STAGE2 END", b, StringComparison.Ordinal);
                string block = it.Substring(b, end - b);
                Check(block.Contains("if (m_JR.m_BoxRule && !JunctionRules.Core.ExitGiveWay.IsEmergency(m_Priority))") && block.Contains("m_JR.BoxHold(") && block.Contains("m_BlockerType = BlockerType.Continuing;"), "box block gated by the setting, sets no blocker");
            }
            string nav = File.ReadAllText(Path.Combine(src, "Vanilla", "JRCarNavigationSystem.cs"));
            int s26 = nav.IndexOf("BEGIN S2-26", StringComparison.Ordinal);
            int s26e = nav.IndexOf("JR-STAGE2 END", s26, StringComparison.Ordinal);
            string b26 = nav.Substring(s26, s26e - s26);
            Check(b26.Contains("if (carLaneSpeedIterator.m_JR.m_BoxRule)") || b26.Contains("m_JR.m_BoxRule"), "S2-26 hands the box lookups only with the setting on");
            Check(b26.Contains("m_JR.SetBoxes(m_JRBoxJunctionData, m_JREdgeLaneData, ref m_JRBoxEdges, m_LaneSignalData)"), "S2-26 SetBoxes (0.4.3 review F1: the job's one copy by ref, no building lookups)");
            Check(!nav.Contains("m_JRBoxEntranceData") && !nav.Contains("m_JRBuildingData") && !nav.Contains("m_JRConnectedBuildings"), "0.4.3 review F1: the unused building lookups are gone from the job");
            Check(nav.Contains("GetComponentLookup<JunctionRules.Boxes.YellowBoxJunction>(isReadOnly: true)") || nav.Contains("YellowBoxJunction>(true)"), "box lookups read-only");
            string job = File.ReadAllText(Path.Combine(src, "Stage2", "JRWholeJunction.cs"));
            Check(job.Contains("j.m_BoxRule = (mode & Stage2Mode.BoxObey) != 0;") && job.Contains("mode &= ~Stage2Mode.Stripped;"), "Create reads and strips the box bits");
            string yb = File.ReadAllText(Path.Combine(src, "Stage2", "JRYellowBox.cs"));
            string code = System.Text.RegularExpressions.Regex.Replace(yb + File.ReadAllText(Path.Combine(src, "Core", "BoxDriving.cs")), @"//.*", "");
            Check(!System.Text.RegularExpressions.Regex.IsMatch(code, @"\bnew\s+\w"), "box code has no 'new' (Burst, no allocation)");
            Check(yb.Contains("HasRoomPast(") && !yb.Contains("RoomScan.Begin"), "job side shares the zebra room scan (HasRoomPast), no second scanner");
            string settings = File.ReadAllText(Path.Combine(src, "Settings", "JunctionRulesSettings.cs"));
            Check(settings.Contains("BoxObey = true;") && settings.Contains("BoxTurnWait = true;"), "settings default on (obey, UK right-turn exception)");
            Check(settings.Contains("\"Cars obey yellow boxes\"") && settings.Contains("nameof(BoxTurnWaitDisabled)"), "setting label; exception greyed while off");
            string sw = File.ReadAllText(Path.Combine(src, "Systems", "CarDrivingSwitchSystem.cs"));
            Check(sw.Contains("Mod.Settings.BoxObey") && sw.Contains("Mod.Settings.BoxTurnWait") && sw.Contains("BoxStore.EntranceLength"), "switch system hands the box settings over every step");
            string dev = File.ReadAllText(Path.Combine(root, "DEVIATIONS.md"));
            Check(dev.Contains("S2-28") && dev.Contains("0.4.2"), "DEVIATIONS lists the box blocks");
        }

        /// <summary>Keys stored in a given order (float.MaxValue = skipped entry).</summary>
        private struct TKeys : IOrderKeys
        {
            public float[] K;
            public int Count => K.Length;
            public float Key(int i) => K[i];
        }

        /// <summary>Feeds (rear, length) vehicles into a RoomScan in key order (ScanOrder) or in storage order.</summary>
        private static bool FeedRoom(bool ordered, float need, params (float Rear, float Len)[] v)
        {
            var r = RoomScan.Begin(need, 1f);
            var keys = new TKeys { K = new float[v.Length] };
            for (int i = 0; i < v.Length; i++) keys.K[i] = v[i].Rear;
            if (!ordered)
            {
                foreach (var x in v) r.Add(x.Rear, x.Len, 0f);
                return r.HasRoom;
            }
            float last = float.MinValue; int li = -1;
            for (int g = 0; g < v.Length; g++)
            {
                int j = ScanOrder.Next(ref keys, last, li);
                if (j < 0) break;
                last = keys.Key(j); li = j;
                r.Add(v[j].Rear, v[j].Len, 0f);
            }
            return r.HasRoom;
        }

        private static void ScanOrderTests()
        {
            var k = new TKeys { K = new[] { 0.5f, 0.2f, 0.9f, float.MaxValue, 0.2f } };
            int a = ScanOrder.Next(ref k, float.MinValue, -1);
            int b = ScanOrder.Next(ref k, k.Key(a), a);
            int c = ScanOrder.Next(ref k, k.Key(b), b);
            int d = ScanOrder.Next(ref k, k.Key(c), c);
            int e = ScanOrder.Next(ref k, k.Key(d), d);
            Check(a == 1 && b == 4 && c == 0 && d == 2 && e == -1, "ScanOrder: ascending key, ties by index, skipped entries left out (" + a + b + c + d + e + ")");
            Check(!ScanOrder.InOrder(ref k), "buffer out of order detected (probe counter)");
            var sorted = new TKeys { K = new[] { 0.1f, float.MaxValue, 0.1f, 0.4f } };
            Check(ScanOrder.InOrder(ref sorted), "sorted buffer (skips ignored)");
            // reviewer: an inverse lane reads the (ascending-by-position) buffer from its end: keys fall along the buffer.
            var inv = new TKeys { K = new[] { 0.9f, 0.5f, float.MaxValue, 0.2f } };
            Check(!ScanOrder.InOrder(ref inv) && ScanOrder.InOrder(ref inv, true), "inverse lane: a sorted buffer is not reported as out of order");
            var empty = new TKeys { K = new float[0] };
            Check(ScanOrder.Next(ref empty, float.MinValue, -1) == -1 && ScanOrder.InOrder(ref empty), "empty buffer");
            // The live 0.4.1 case: the game sorts LaneObject by .y (the reservation look-ahead); a faster car behind a slower one sorts after it.
            // Buffer [car at 14 m, car at 8 m]: read in buffer order the near car looks 'behind' the far one (false hold).
            Check(!FeedRoom(false, BCar, (14f, BCar), (8f, BCar)), "buffer order: false 'no room' (the 0.4.1 risk)");
            Check(FeedRoom(true, BCar, (14f, BCar), (8f, BCar)), "curve order: room (8 m and 14 - 5.5 m both fit 5.5 m)");
            Check(!FeedRoom(true, BCar, (14f, BCar), (3f, BCar)), "curve order still holds for a near standing car");
            string job = File.ReadAllText(Path.Combine(RepoRoot(), "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            Check(job.Contains("ScanOrder.Next(ref keys, lastKey, lastIndex)") && !job.Contains("objects[inverse ? objects.Length - 1 - jj : jj]"), "ScanLane reads lane objects in curve order, not buffer order");
        }

        private static ZebraLayout GBuild(float vehicle, float gapMargin, params (int, float, float)[] items)
        {
            var list = new ZElems { Items = items };
            Check(ZebraSections.Build(ref list, true, false, ZLane, M, vehicle, 2f, gapMargin, out ZebraLayout l), "layout built");
            return l;
        }

        private static void ZebraGapTests()
        {
            // Lane 30 m, car 4.5 m, stop margin 0.9 m, room margin 1 m: a gap needs 6.4 m between the zebra and the C.
            var zocc = GBuild(BCar, 1f, Zb(0f, 0.1f), Cv(0.4f, 0.45f), Cv(0.5f, 0.55f));
            Check(zocc.Spots.Count == 2 && zocc.Spots[0] == 0f && Near(zocc.Spots[1], 0.4f) && zocc.HasZebra(0) && !zocc.HasZebra(1) && zocc.Gaps == 1,
                "ZOCC: waiting spot O at the first C after the zebra");
            Check(Near(zocc.SectionEnd(0), 0.1f) && zocc.Spots.StretchOf(0.4f) == 1 && zocc.Spots.StretchOf(0.5f) == 1, "ZOCC: Z's section is the zebra alone (room past Z = O free); the Cs are decided at O");
            var zcc = GBuild(BCar, 1f, Zb(0f, 0.1f), Cv(0.15f, 0.2f), Cv(0.5f, 0.55f));
            var zcc0 = GBuild(BCar, -1f, Zb(0f, 0.1f), Cv(0.15f, 0.2f), Cv(0.5f, 0.55f));
            Check(zcc.Spots.Count == 1 && zcc.Gaps == 0 && Near(zcc.LastEnd, zcc0.LastEnd) && zcc.Spots.StretchOf(0.5f) == 0, "ZCC (no gap): unchanged 0.4.1 layout, the Cs decided before Z");
            var off = GBuild(BCar, -1f, Zb(0f, 0.1f), Cv(0.4f, 0.45f), Cv(0.5f, 0.55f));
            Check(off.Spots.Count == 1 && off.Gaps == 0, "gap margin off (< 0): the 0.4.1 layout");
            var bus = GBuild(BBus, 1f, Zb(0f, 0.1f), Cv(0.4f, 0.45f), Cv(0.5f, 0.55f));
            Check(bus.Spots.Count == 1 && bus.Gaps == 0, "bus: 9 m gap does not take 12 m + margins, decided before Z");
            var m3 = GBuild(BCar, 3f, Zb(0f, 0.1f), Cv(0.3f, 0.35f));
            Check(m3.Gaps == 0 && GBuild(BCar, 0f, Zb(0f, 0.1f), Cv(0.3f, 0.35f)).Gaps == 1, "the room margin counts in the gap (6 m: margin 0 fits, 3 m does not)");
            // ZOCZ: the later zebra fits after the C: its own section; it does not: merged into O (decided at O).
            var zocz = GBuild(BCar, 1f, Zb(0f, 0.1f), Cv(0.4f, 0.45f), Zb(0.8f, 0.9f));
            Check(zocz.Spots.Count == 3 && Near(zocz.Spots[1], 0.4f) && !zocz.HasZebra(1) && zocz.HasZebra(2) && Near(zocz.Spots[2], 0.8f), "ZOCZ: Z | O C | Z");
            var zoczTight = GBuild(BCar, 1f, Zb(0f, 0.1f), Cv(0.4f, 0.45f), Zb(0.5f, 0.6f));
            Check(zoczTight.Spots.Count == 2 && zoczTight.HasZebra(1) && Near(zoczTight.FirstZebra(1), 0.5f) && zoczTight.Merged == 1, "ZOCZ with the second zebra close: decided at O (C + Z2 + room past Z2)");
            var zz = GBuild(BCar, 1f, Zb(0f, 0.1f), Zb(0.5f, 0.6f));
            Check(zz.Gaps == 0 && zz.Spots.Count == 2, "Z Z without a C: no gap spot (zebra spots only)");
            // O occupied by another car: no room past Z, the car waits before Z.
            var free = RoomScan.Begin(BCar, 1f);
            Check(free.HasRoom && free.Done((0.4f - 0.1f) * ZLane), "O free: room found inside the gap (scan ends there)");
            var occ = RoomScan.Begin(BCar, 1f); occ.Add(2f, BCar, 0f);
            Check(!occ.HasRoom, "O occupied (a car standing 2 m past Z): no room, wait before Z");
            // Can no longer stop at O: O merges into Z (the Cs decided before Z), as JR-T-009-r.
            var early = zocc;
            Check(ZebraSections.MergeUnstoppable(ref early, 0, 15f, ZLane) == 1 && early.Spots.Count == 1 && early.Spots.StretchOf(0.4f) == 0, "car too fast to stop at O: Cs decided before Z");
            var slow = zocc;
            Check(ZebraSections.MergeUnstoppable(ref slow, 0, 5f, ZLane) == 0 && slow.Spots.Count == 2, "slow car keeps O");
            // Room section: before Z it is Z's (room past Z = O free); at O no zebra follows: none (0.2.3: no exit-room check).
            string job = File.ReadAllText(Path.Combine(RepoRoot(), "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            Check(job.Contains("m_BoxLane ? -1f : m_RoomMargin"), "job: gaps on, except inside a box (the box decides at the line)");
        }

        private struct TLongs : IKeyList
        {
            public System.Collections.Generic.List<long> L;
            public int Count => L.Count;
            public long Key(int i) => L[i];
        }

        private static void BoxEdgeSetTests()
        {
            // The sorted set the entrance band asks before any building is read (JRBoxEdges.Add uses InsertAt + Insert).
            var set = new TLongs { L = new System.Collections.Generic.List<long>() };
            foreach (long k in new long[] { (7L << 32) | 1, (3L << 32) | 2, (7L << 32) | 1, (12L << 32) | 1, (3L << 32) | 1 })
            {
                int at = SortedKeys.InsertAt(ref set, k);
                if (at >= 0) set.L.Insert(at, k);
            }
            Check(set.L.Count == 4 && set.L[0] == ((3L << 32) | 1) && set.L[3] == ((12L << 32) | 1), "edge set: sorted, duplicates dropped");
            Check(SortedKeys.Contains(ref set, (3L << 32) | 2) && SortedKeys.Contains(ref set, (12L << 32) | 1) && !SortedKeys.Contains(ref set, (3L << 32) | 3)
                && !SortedKeys.Contains(ref set, 0L), "edge set: binary search (version counts: a rebuilt edge with a reused index is not in the set)");
            int v0 = BoxEdgeHandoff.Version;
            BoxEdgeHandoff.Publish(new[] { new ExitBand(9L, 0.5f), new ExitBand(2L, 0.25f), new ExitBand(9L, 0.5f) });
            int v1 = BoxEdgeHandoff.Version;
            BoxEdgeHandoff.Publish(new[] { new ExitBand(2L, 0.25f), new ExitBand(9L, 0.5f) });
            Check(v1 == v0 + 1 && BoxEdgeHandoff.Version == v1 && BoxEdgeHandoff.Keys.Length == 2 && BoxEdgeHandoff.Keys[0] == 2L && BoxEdgeHandoff.T[1] == 0.5f, "handoff: sorted, deduplicated, same bands = no new version");
            BoxEdgeHandoff.Publish(new[] { new ExitBand(2L, 0.25f), new ExitBand(9L, 0.75f) });
            Check(BoxEdgeHandoff.Version == v1 + 1, "handoff: an exit moved along its road = new version");
            BoxEdgeHandoff.Publish(new ExitBand[0]);
            Check(BoxEdgeHandoff.Version == v1 + 2 && BoxEdgeHandoff.Keys.Length == 0 && BoxEdgeHandoff.T.Length == 0, "handoff: cleared");
            Check(SortedKeys.KeyOf(7, 1) == ((7L << 32) | 1), "entity key = index, version");
            var none = new TLongs { L = new System.Collections.Generic.List<long>() };
            Check(!SortedKeys.Contains(ref none, 5L) && SortedKeys.InsertAt(ref none, 5L) == 0, "empty set");

            string src = Path.Combine(RepoRoot(), "JunctionRules");
            string yb = File.ReadAllText(Path.Combine(src, "Stage2", "JRYellowBox.cs"));
            // 0.4.3: one band per boxed car exit (edge + t), no building read in the job (no one-box-per-building format)
            Check(yb.Contains("int first = bands->First(edge);") && yb.Contains("bands->Key(i) == edgeKey") && yb.Contains("LaneBands(lane, edgeOwner.m_Owner, laneLength, inverse,")
                && yb.Contains("LaneWindow.OnLane(") && !yb.Contains("m_EdgeDelta.x"),
                "BoxHold: one binary search to the edge's first exit band, then every painted window on that edge projected onto the lane (0.4.10: no EdgeDelta mapping)");
            string ybCode = System.Text.RegularExpressions.Regex.Replace(yb, @"//.*|///.*", "");
            Check(!ybCode.Contains("m_ConnectedBuildings.TryGetBuffer") && !ybCode.Contains("m_RoadEdge") && !ybCode.Contains("m_CurvePosition") && !ybCode.Contains("m_BoxEntranceData.HasComponent"),
                "BoxHold no longer reads Building.m_RoadEdge / m_CurvePosition or the building's box component (0.4.0-0.4.2 format)");
            string whole = File.ReadAllText(Path.Combine(src, "Stage2", "JRWholeJunction.cs"));
            Check(whole.Contains("private unsafe JRBoxEdges* m_BoxEdges;") && !System.Text.RegularExpressions.Regex.IsMatch(whole, @"\bJRBoxEdges\s+m_BoxEdges\b")
                && !whole.Contains("m_BoxEntranceData") && !whole.Contains("m_BuildingData") && !whole.Contains("m_ConnectedBuildings")
                && yb.Contains("m_BoxEdges = (JRBoxEdges*)UnsafeUtility.AddressOf(ref boxEdges);"),
                "0.4.3 review F1: each car's iterator holds a pointer to the job's one band set (no ~1 KB copy per car), no unused lookups");
            string edges = File.ReadAllText(Path.Combine(src, "Stage2", "JRBoxEdges.cs"));
            Check(edges.Contains("FixedList512Bytes<long> m_Keys") && edges.Contains("FixedList512Bytes<float> m_T") && edges.Contains("SortedKeys.First(ref this, KeyOf(edge))"), "exit bands by value (keys + t), first band by binary search");
            string nav = File.ReadAllText(Path.Combine(src, "Vanilla", "JRCarNavigationSystem.cs"));
            Check(nav.Contains("jobData.m_JRBoxEdges = JunctionRules.Stage2.JRStage2Bridge.BoxEdges;") && nav.Contains("m_JREdgeLaneData, ref m_JRBoxEdges, m_LaneSignalData);"), "S2-23 takes the set by value (one copy per job), S2-26 hands the iterator a pointer to it");
            string sw = File.ReadAllText(Path.Combine(src, "Systems", "CarDrivingSwitchSystem.cs"));
            Check(sw.Contains("UpdateBoxEdges((stage2Mode & Stage2Mode.BoxEntrances) != 0);") && sw.Contains("BoxFacts.BoxedExitBands(EntityManager, buildings[i], sources)") && sw.Contains("DrivingBands.Build(EntityManager, sources, out notPainted, out joint)")
                && sw.Contains("BoxEdgeHandoff.Publish(bands);") && sw.Contains("version == m_BoxStoreVersion") && !sw.Contains("JunctionRules.Stage2") && !sw.Contains("m_RoadEdge"),
                "switch system rebuilds the exit bands on the main thread when boxes change (count or BoxStore.Version) and periodically, from the saved exit keys");
            Check(sw.Contains("if (dropped != m_LastDropped)") && sw.IndexOf("if (dropped != m_LastDropped)", StringComparison.Ordinal) > sw.IndexOf("BoxEdgeHandoff.Publish(bands);", StringComparison.Ordinal)
                && System.Text.RegularExpressions.Regex.IsMatch(sw, @"if \(dropped != m_LastDropped\)\s*\{\s*if \(dropped > 0\) Mod\.log\.Warn")
                && sw.Contains("m_LastDropped = 0;"),
                "0.4.3 review F2: the over-limit Warn follows the Dropped count, not the hand-over Version");
            string facts = File.ReadAllText(Path.Combine(src, "Boxes", "BoxFacts.cs"));
            Check(facts.Contains("ExitBands.Boxed(keys, c.Legacy, old, cands, out int unmatched)") && facts.Contains("AddSource(em, building, exits[i], bpos, into)")
                && facts.Contains("BoxSelection.ExitOk(ExitFacts(em, building, x, out Access a), out _)"), "BoxedExitBands: saved keys matched to current exits, each a source for its painted window (0.4.10)");
            string store = File.ReadAllText(Path.Combine(src, "Boxes", "BoxStore.cs"));
            Check(System.Text.RegularExpressions.Regex.Matches(store, @"Version\+\+;").Count == 6, "BoxStore bumps its version in all six writers (Add, AddExit, RemoveExit, SetExits, Remove, JR-T-021 MigrateEntrances)");
            string job = File.ReadAllText(Path.Combine(src, "Stage2", "JRWholeJunction.cs"));
            Check(job.Contains("bool ordered = ScanOrder.InOrder(ref keys, inverse);") && job.Contains("float lastKey = -MaxRearReach / math.max(laneLength, 0.001f);"), "ScanLane: straight read when in order, selection starts at the rear reach");
        }

        /// <summary>0.4.3: the driving rule reads one band per boxed car exit (Core/ExitBands.cs).</summary>
        private static void BoxExitBandTests()
        {
            // A building with a driveway on road 1, a spawn location (depot gate) on road 2 and its own access road junction.
            var exits = new System.Collections.Generic.List<ExitCandidate>
            {
                new ExitCandidate { Kind = ExitKind.Driveway, Key = new ExitKey(ExitKind.Driveway, -9.4, 23.7), RoadId = 1, World = new V2(100, 50) },
                new ExitCandidate { Kind = ExitKind.SpawnLocation, Key = new ExitKey(ExitKind.SpawnLocation, 20, 10), RoadId = 2, World = new V2(130, 40) },
                new ExitCandidate { Kind = ExitKind.Junction, Key = new ExitKey(ExitKind.Junction, 0, -30), RoadId = 77, World = new V2(110, 0) },
            };
            var keys = new System.Collections.Generic.List<ExitKey> { new ExitKey(ExitKind.SpawnLocation, 20.4, 9.8) };
            var r = ExitBands.Boxed(keys, false, default, exits, out int un);
            Check(r.Count == 1 && r[0] == 1 && un == 0, "one boxed exit of three: only that exit drives as a band (not the building's other exits)");
            keys.Add(new ExitKey(ExitKind.Driveway, -9.4, 23.7));
            r = ExitBands.Boxed(keys, false, default, exits, out un);
            Check(r.Count == 2 && r[0] == 0 && r[1] == 1 && un == 0, "two boxed exits of one building: two bands");
            keys.Add(new ExitKey(ExitKind.Driveway, 50, 50));
            r = ExitBands.Boxed(keys, false, default, exits, out un);
            Check(r.Count == 2 && un == 1, "a saved key with no exit now: not a band, counted");
            r = ExitBands.Boxed(new System.Collections.Generic.List<ExitKey> { new ExitKey(ExitKind.Driveway, -9.4, 23.7), new ExitKey(ExitKind.Driveway, -9.5, 23.6) }, false, default, exits, out un);
            Check(r.Count == 1 && un == 0, "two keys on one exit: one band");
            r = ExitBands.Boxed(null, true, new V2(128, 41), exits, out un);
            Check(r.Count == 1 && r[0] == 1 && un == 0, "legacy 0.4.0-0.4.2 box (no keys): the exit nearest its old access point, as the paint's load migration");
            r = ExitBands.Boxed(null, true, new V2(110, 2), exits, out un);
            Check(r.Count == 0 && un == 0, "legacy box nearest the building's own road junction: no band (it becomes a junction box on the node)");
            r = ExitBands.Boxed(null, true, new V2(0, 0), new System.Collections.Generic.List<ExitCandidate>(), out un);
            Check(r.Count == 0 && un == 1, "legacy box on a building without exits: nothing");
            r = ExitBands.Boxed(new System.Collections.Generic.List<ExitKey> { new ExitKey(ExitKind.Fallback, -9.0, 23.0) }, false, default, exits, out un);
            Check(r.Count == 1 && r[0] == 0, "a fallback key once the game shows a real drive: the drive within 2 m (ExitRules.Match second pass)");

            // Hand-over: sorted by edge then t, near-equal merged, capped.
            var n = ExitBands.Normalise(new[] { new ExitBand(5, 0.8f), new ExitBand(3, 0.4f), new ExitBand(5, 0.2f), new ExitBand(5, 0.20001f), new ExitBand(4, float.NaN) }, 63, out int dropped);
            Check(n.Length == 3 && n[0].EdgeKey == 3 && n[1].EdgeKey == 5 && n[1].T == 0.2f && n[2].T == 0.8f && dropped == 0, "bands: by edge, then t; duplicates and NaN dropped");
            var many = new System.Collections.Generic.List<ExitBand>();
            for (int i = 0; i < 70; i++) many.Add(new ExitBand(i, 0.5f));
            n = ExitBands.Normalise(many, ExitBands.Capacity, out dropped);
            Check(n.Length == 63 && dropped == 7 && ExitBands.Capacity == 63, "over the job's 63 bands: the rest counted as dropped (Warn in the log)");
            // 0.4.3 review F2: 63 exits, then a 64th that sorts last: the kept bands (and so the Version) do not change,
            // but Dropped goes 0 -> 1, which the switch system logs on its own.
            var full = new System.Collections.Generic.List<ExitBand>();
            for (int i = 0; i < 63; i++) full.Add(new ExitBand(100 + i, 0.5f));
            BoxEdgeHandoff.Publish(full);
            int vFull = BoxEdgeHandoff.Version;
            Check(BoxEdgeHandoff.Dropped == 0 && BoxEdgeHandoff.Keys.Length == 63, "F2: 63 exits: all kept, none dropped");
            full.Add(new ExitBand(1000, 0.5f));
            BoxEdgeHandoff.Publish(full);
            Check(BoxEdgeHandoff.Dropped == 1 && BoxEdgeHandoff.Version == vFull && BoxEdgeHandoff.Keys.Length == 63 && BoxEdgeHandoff.Keys[62] == 162,
                "F2: a 64th exit sorting last: Dropped = 1 with the same kept bands and Version (warned on the Dropped change)");
            full.RemoveAt(63);
            BoxEdgeHandoff.Publish(full);
            Check(BoxEdgeHandoff.Dropped == 0 && BoxEdgeHandoff.Version == vFull, "F2: back to 63: Dropped = 0");
            BoxEdgeHandoff.Publish(new ExitBand[0]);

            // Job loop as JRYellowBox.BoxHold runs it: first band of the edge by binary search, then each band on it.
            var set = new TLongs { L = new System.Collections.Generic.List<long> { 3, 5, 5, 5, 9 } };
            Check(SortedKeys.First(ref set, 5) == 1 && SortedKeys.First(ref set, 3) == 0 && SortedKeys.First(ref set, 9) == 4 && SortedKeys.First(ref set, 4) == -1 && SortedKeys.First(ref set, 10) == -1,
                "First: the edge's first band among repeats; -1 for an edge without boxed exits");
            var empty = new TLongs { L = new System.Collections.Generic.List<long>() };
            Check(SortedKeys.First(ref empty, 1) == -1, "First on no bands");

            // Two boxed exits on one 60 m road lane (t 0.25 and 0.75), band 8 m: a main-road car passing both meets each
            // band at its own place; a car leaving the second drive joins inside the second band only.
            float lane = 60f, band = 8f;
            float[] ts = { 0.25f, 0.75f };
            int mainBands = 0, leavingBands = 0;
            float firstEntry = 2f;
            foreach (float t in ts)
            {
                if (!BoxDriving.Band(t, lane, band, false, out float b0, out float b1)) continue;
                var role = BoxDriving.Role(0f, 1f, b0, b1, out float entry);
                if (role == BoxDriving.EntranceRole.Main) { mainBands++; if (entry < firstEntry) firstEntry = entry; }
                if (BoxDriving.Role(0.75f, 1f, b0, b1, out _) == BoxDriving.EntranceRole.Leaving) leavingBands++;
            }
            Check(mainBands == 2 && Near(firstEntry, 11f / 60f), "main road: each boxed exit is its own band (first entry 4 m before the first exit)");
            Check(leavingBands == 1, "leaving one drive: only that exit's band applies (the building's other boxed exit does not hold it)");
        }
    }
}
