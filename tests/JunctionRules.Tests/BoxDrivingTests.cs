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
            Check(name.EndsWith("; cars obey yellow boxes (room margin 1.0 m, entrance band 8.0 m; turning across oncoming may wait in the box, left-hand traffic)"), "rule name with the setting: " + name);
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
            Check(Stage2Counters.BoxHeldRoom == Stage2Counters.ZebraScanUnsorted + 1 && Stage2Counters.EntranceHeldLeaving == Stage2Counters.Count - 1, "box counters after the zebra ones");
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
                Check(block.Contains("if (m_JR.m_BoxRule)") && block.Contains("m_JR.BoxHold(") && block.Contains("m_BlockerType = BlockerType.Continuing;"), "box block gated by the setting, sets no blocker");
            }
            string nav = File.ReadAllText(Path.Combine(src, "Vanilla", "JRCarNavigationSystem.cs"));
            int s26 = nav.IndexOf("BEGIN S2-26", StringComparison.Ordinal);
            int s26e = nav.IndexOf("JR-STAGE2 END", s26, StringComparison.Ordinal);
            string b26 = nav.Substring(s26, s26e - s26);
            Check(b26.Contains("if (carLaneSpeedIterator.m_JR.m_BoxRule)") || b26.Contains("m_JR.m_BoxRule"), "S2-26 hands the box lookups only with the setting on");
            Check(b26.Contains("m_JR.SetBoxes(m_JRBoxJunctionData, m_JRBoxEntranceData, m_JRBuildingData, m_JRConnectedBuildings, m_JREdgeLaneData)"), "S2-26 SetBoxes");
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
    }
}
