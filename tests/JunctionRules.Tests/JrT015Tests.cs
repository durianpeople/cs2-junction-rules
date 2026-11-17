using System;
using System.Collections.Generic;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.9 (JR-T-015): the 0.4.8 live gaps G12-G15 (Bevy Valley, 17 Nov 2026 08:47+, left-hand traffic).
    /// G15 regression from the live read (frame 36670652 + 36692240): No. 40 Alderwood Street (warehouse 839552, front at
    /// z 266.7) has its EXIT drive at x -1983.4 and its ENTRANCE at x -1994.0 (entrance box log: box at (-1994.04, 274.79));
    /// the westbound (near) lane runs at z ~271.6, traffic from the east (x decreasing). A van for No. 40 stood on the
    /// westbound lane at x -1986.9 (centre) waiting for room in the full lot; No. 40's exit queue (380832 / 380901 at the
    /// drive start, 380949, 380806 behind) waited for the van; the lot never emptied.
    /// </summary>
    internal static partial class Program
    {
        // Westbound lane modelled from x = -1960 (lane start, east) to x = -2030 (end, west): driving coordinate u = (-1960 - x) / 70.
        private const float G15LaneStartX = -1960f, G15LaneLength = 70f, G15BandLength = 8f;
        private static float G15U(float x) => (G15LaneStartX - x) / G15LaneLength;
        private static float G15X(float u) => G15LaneStartX - u * G15LaneLength;

        private static List<(float b0, float b1)> G15Bands(float tolMetres)
        {
            var raw = new List<(float b0, float b1)>();
            foreach (float x in new[] { -1983.4f, -1994.04f })   // exit drive, entrance (upstream first in driving order)
            {
                BoxDriving.Band(G15U(x), G15LaneLength, G15BandLength, false, out float b0, out float b1);
                raw.Add((b0, b1));
            }
            return ExitBands.Chain(raw, tolMetres / G15LaneLength);
        }

        private static void G15LoopTests()
        {
            const float van = 6.5f, margin = 1f;   // a delivery van and the default room margin
            // --- 0.4.8: two bands (touch 0.1 m), the van held at the ENTRANCE band start stands in the EXIT band ---
            var old = G15Bands(0.1f);
            Check(old.Count == 2, "0.4.8: No. 40's exit band and entrance band (2.5 m apart) were two bands");
            float oldFront = G15X(old[1].b0), oldRear = oldFront + van;          // x: the van faces west, rear to the east
            float exitBandWest = G15X(old[0].b1), exitBandEast = G15X(old[0].b0);
            Check(oldRear > exitBandWest && oldFront < exitBandEast, $"0.4.8: the held van ({oldFront:0.0}..{oldRear:0.0}) stood in No. 40's exit band ({exitBandWest:0.0}..{exitBandEast:0.0}) (the live van centre was at -1986.9)");
            // --- 0.4.9: for this van the bands are one (gap < its length + margin): it waits before the EXIT band ---
            float tol = ExitBands.HoldTouchMetres(van, margin);
            Check(tol == van + margin && ExitBands.HoldTouchMetres(2f, 0f) == ExitBands.MergeGapMetres, "hold tolerance = own length + margin, at least the 2 m merge gap");
            var now = G15Bands(tol);
            Check(now.Count == 1, "0.4.9: one band for the van's hold");
            // the van's path on the lane ends inside the entrance part (it turns in): Entering, held before the joint band
            float pathEnd = G15U(-1994.04f);
            BoxDriving.EntranceRole role = BoxDriving.Role(0f, pathEnd, now[0].b0, now[0].b1, out _);
            Check(role == BoxDriving.EntranceRole.Entering, "the van turning into No. 40 is an entering vehicle of the joint band");
            float holdFront = G15X(BoxDriving.EnteringEntry(0f, now[0].b0));
            Check(holdFront >= exitBandEast - 1e-3f, $"0.4.9: the van waits with its front at {holdFront:0.0}, east of No. 40's exit band ({exitBandEast:0.0}): the exit mouth is clear");
            // --- the exit's judgement of the waiting van: 0.4.8 PresentMetres held the exit; 0.4.9 the van waits to enter: not present ---
            float conflictStartX = -1984.5f;                 // No. 40's left-out joins the westbound lane just west of the drive
            float gap = holdFront - conflictStartX;           // metres from the van's front to the conflict (it drives west)
            Check(gap > 0f && gap <= ExitGiveWay.PresentMetres, $"the waiting van stands {gap:0.0} m before the exit's conflict, within the 10 m 'present' distance");
            Check(ExitGiveWay.VehicleBlocks(gap, false, 0f, 0f, 1.5f), "0.4.8 rule: a standing vehicle that close blocks the exit (the lock)");
            Check(!ExitGiveWay.VehicleBlocks(gap, false, 0f, 0f, 1.5f, waitingToEnter: true), "0.4.9: a vehicle waiting for room INTO a building does not block the exit");
            // safety floors kept
            Check(ExitGiveWay.VehicleBlocks(-1f, true, 0f, 0f, 1.5f, waitingToEnter: true), "an entrant standing ON the conflict still blocks");
            Check(ExitGiveWay.VehicleBlocks(gap, false, 6f, 2.3f, 1.5f, waitingToEnter: true), "an entrant that moves off towards the conflict still blocks (reach)");
            Check(ExitGiveWay.VehicleBlocks(3f, false, 0f, 0f, 1.5f, waitingToEnter: false), "a through vehicle standing next to the exit (G7 westbound truck) still blocks");
            Check(ExitGiveWay.VehicleBlocks(3f, false, ExitGiveWay.EnteringStandSpeed + 0.5f, 0.2f, 1.5f, waitingToEnter: true), "an entrant creeping faster than the stand speed is judged as moving");

            // --- the dependency loop of the live read, rebuilt with the rules (who waits for whom) ---
            // nodes: 0 van (wants into No. 40), 1 lot (No. 40's yard, full of vans), 2 exit head (380832 / 380901 at the drive)
            bool LoopUnder(bool rule049)
            {
                var waits = new List<(int from, int to)>
                {
                    (0, 1),   // the van waits for room in the lot (G9 entrance hold: lot / driveway full)
                    (1, 2),   // the lot empties only when the exit head leaves (internal queue: 380949, 380806, the 'Crossing' van)
                };
                float vanFront = rule049 ? holdFront : oldFront;
                float vanRear = vanFront + van;
                bool occupies = vanRear > conflictStartX - 6f && vanFront < conflictStartX;   // the left-out's conflict ~6 m long, west of the drive
                float g = vanFront - conflictStartX;
                if (ExitGiveWay.VehicleBlocks(g, occupies, 0f, 0f, 1.5f, rule049)) waits.Add((2, 0));   // the exit waits for the van
                // a cycle 0 -> 1 -> 2 -> 0 ?
                bool e01 = waits.Contains((0, 1)), e12 = waits.Contains((1, 2)), e20 = waits.Contains((2, 0));
                return e01 && e12 && e20;
            }
            Check(LoopUnder(false), "0.4.8: van -> lot -> exit -> van is a closed loop (the G15 deadlock)");
            Check(!LoopUnder(true), "0.4.9: the exit no longer waits for the van: the loop is open (the exit leaves, the lot gets room, the van goes in)");
        }

        private static void G12CrossingTests()
        {
            Check(ExitGiveWay.Crossing(true, false) == ExitGiveWay.CrossOutcome.Judged && ExitGiveWay.Crossing(true, true) == ExitGiveWay.CrossOutcome.Judged,
                "a Go standing when the front passes the sidewalk spot = judged");
            Check(ExitGiveWay.Crossing(false, true) == ExitGiveWay.CrossOutcome.Late, "no Go, decided at the road lane in the same update = judged only on the sidewalk");
            Check(ExitGiveWay.Crossing(false, false) == ExitGiveWay.CrossOutcome.Unjudged, "neither = without a full judgement (must be 0)");
            Check(ExitGiveWay.Committed(true, true, true) && !ExitGiveWay.Committed(true, false, true) && !ExitGiveWay.Committed(false, true, true),
                "only a Go'd vehicle past the spot is committed (one move); a vehicle still before the spot is judged every update");
            Check(!ExitGiveWay.Committed(true, true, false), "a Go'd vehicle that stopped on the pavement is judged again at the road lane");
            // live 0.4.8 artefact: 844033 'waits' and 'NOT judged' in the same frame = hand-over while waiting, no crossing yet
            Check(ExitGiveWay.GoStands(true, 36642880u, 36642000u) == false, "a Wait after the Go: the Go no longer stands (844033 was waiting)");
            // trace: a late crossing is not a NOTABLE 'NOT judged' line
            long late = ExitTrace.Pack(ExitTrace.Event.EnterDrive, ExitPaths.Path.Drive, ExitPaths.LaneKind.DriveLane, ExitGiveWay.Decision.Go, ExitTrace.ReasonLate, false);
            long none = ExitTrace.Pack(ExitTrace.Event.EnterDrive, ExitPaths.Path.Drive, ExitPaths.LaneKind.DriveLane, ExitGiveWay.Decision.Go, 0, false);
            long good = ExitTrace.Pack(ExitTrace.Event.EnterDrive, ExitPaths.Path.Drive, ExitPaths.LaneKind.DriveLane, ExitGiveWay.Decision.Go, ExitTrace.ReasonDecided, false);
            string lateLine = ExitTrace.Format(ExitTrace.PackEntity(1, 1), 0, late, ((long)7 << 32) | 5u);
            string noneLine = ExitTrace.Format(ExitTrace.PackEntity(1, 1), 0, none, ((long)7 << 32) | 5u);
            Check(!ExitTrace.Notable(late) && lateLine.Contains("judged only at the road lane") && !lateLine.Contains("NOTABLE"), "late crossing: sampled line 'judged only at the road lane': " + lateLine);
            Check(ExitTrace.Notable(none) && noneLine.Contains("NOTABLE crosses the sidewalk") && noneLine.Contains("NOT judged before the sidewalk"), "unjudged crossing: NOTABLE");
            Check(!ExitTrace.Notable(good), "judged crossing: not notable");
            Check(ExitGiveWay.EventKinds > ExitGiveWay.DriveHeldKind && ExitGiveWay.DriveHeldKind > ExitGiveWay.DriveCrossKind && ExitGiveWay.DriveCrossKind > ExitGiveWay.EnteringHeldKind
                && ExitGiveWay.EnteringHeldKind > ExitGiveWay.DriveBoxedKind && (long)ExitGiveWay.EventKindStride * ExitGiveWay.EventKinds < int.MaxValue,
                "0.4.9 event kinds 17-19 fit the shared table's key range");
            // the timing line names the two new counters at the end
            var d = new long[Stage2Counters.Count];
            d[Stage2Counters.ExitEntrantIgnored] = 4;
            d[Stage2Counters.ExitKeepClearWithdrawn] = 9;
            string f = Stage2Counters.Format(2, d, 2, 2, 2, 2);
            Check(f.Contains("; exits not waiting for a vehicle that waits to turn in 4; held exits' keep-clear requests withdrawn 9")
                && f.IndexOf("judged only on the sidewalk", StringComparison.Ordinal) < f.IndexOf("exits not waiting", StringComparison.Ordinal), "timing line: 0.4.9 counters after the 0.4.8 ones: " + f);
        }

        private static void G14JointBoxTests()
        {
            Check(ExitBands.TouchMetres == ExitBands.MergeGapMetres && ExitBands.MergeGapMetres == 6f, "driving merges bands within the same gap the paint closes (0.4.10 G20: 6 m)");
            uint all = MedianSplit.AllCarriageways;
            // Alderbrook Street (shot a4d15d38): two exit boxes ~0.6 m apart, plus an entrance box overlapping the left one
            var w = new List<ExitWindow>
            {
                new ExitWindow(42, all, 30.0, 38.0),    // right exit box
                new ExitWindow(42, all, 21.4, 29.4),    // left exit box, 0.6 m gap
                new ExitWindow(42, all, 20.9, 28.9),    // entrance box 0.5 m off the left exit box (doubled outline in the shot)
                new ExitWindow(42, all, 60.0, 68.0),    // a box further along: its own
                new ExitWindow(77, all, 38.5, 46.5),    // another road
            };
            var chains = ExitBoxWindows.Chains(w, ExitBands.MergeGapMetres);
            Check(chains.Count == 1 && chains[0].Members.Count == 3 && Math.Abs(chains[0].From - 20.9) < 1e-9 && Math.Abs(chains[0].To - 38.0) < 1e-9,
                $"three boxes within 2 m on one road = one joint box 20.9..38.0 m (got {chains.Count} chain(s))");
            Check(ExitBoxWindows.Chains(new List<ExitWindow> { new ExitWindow(1, all, 0, 8), new ExitWindow(1, all, 10.5, 18.5) }, 2.0).Count == 0, "2.5 m apart: two boxes");
            Check(ExitBoxWindows.Chains(new List<ExitWindow> { new ExitWindow(1, 0x1, 0, 8), new ExitWindow(1, 0x2, 8.5, 16.5) }, 2.0).Count == 0, "never across a median (different carriageways)");
            Check(ExitBoxWindows.Chains(new List<ExitWindow> { new ExitWindow(1, all, 0, 8), new ExitWindow(1, all, 9.5, 17.5), new ExitWindow(1, all, 19.0, 27.0) }, 2.0)[0].Members.Count == 3,
                "a chain of three (each gap 1.5 m)");
            // the joint window keeps the margins
            Check(EntranceBox.WindowFits(20.9, 38.0, 0, 80, 0.5, null, out _), "joint window inside the edge part: fits");
            Check(!EntranceBox.WindowFits(20.9, 38.0, 0, 80, 0.5, new[] { new AlongSpan(29.5, 29.8) }, out string why) && why.Contains("road marking"), "a stop line in the gap keeps the boxes apart: " + why);
            Check(!EntranceBox.WindowFits(0.2, 8.0, 0, 80, 0.5, null, out _), "a joint window into a mouth margin is refused");
            // identical rings (members of one joint box) are one drawn box
            var ring = new List<V3> { new V3(0, 0, 0), new V3(17, 0, 0), new V3(17, 0, 9), new V3(0, 0, 9) };
            var shifted = new List<V3> { new V3(17, 0, 9), new V3(0, 0, 9), new V3(0, 0, 0), new V3(17, 0, 0) };
            var inputs = new List<BoxMergeInput>
            {
                new BoxMergeInput(1, 11, BoxMergeKind.Exit, new List<V3>(ring), 42, all),
                new BoxMergeInput(2, 12, BoxMergeKind.Exit, new List<V3>(shifted), 42, all),
                new BoxMergeInput(3, 13, BoxMergeKind.Exit, new List<V3>(ring), 42, all),
            };
            var groups = BoxMerge.Merge(inputs, new BoxMergeParams());
            Check(groups.Count == 1 && groups[0].Members.Count == 3 && groups[0].Rings.Count == 1 && groups[0].Merged && groups[0].Notes.Count == 0,
                $"three members of one joint box: one group, one ring, no failure note (groups {groups.Count}, rings {(groups.Count > 0 ? groups[0].Rings.Count : 0)})");
            Check(groups.Count == 1 && Math.Abs(BoxPaintGeometry.SignedArea(groups[0].Rings[0]) - 153.0) < 1e-6, "the joint ring keeps the box area (17 x 9 m)");
            Check(ExitGiveWay.EnteringStandSpeed <= 0.2f && ExitGiveWay.EnteringHeldWindow <= 32u, "review F2: tighter entrant stand speed and record window");
            Check(BoxMerge.SameRing(ring, shifted) && !BoxMerge.SameRing(ring, new List<V3> { new V3(0, 0, 0), new V3(17.5, 0, 0), new V3(17, 0, 9), new V3(0, 0, 9) }), "SameRing: any start vertex, 1 mm tolerance");
        }

        private static void JrT015RepoTests()
        {
            string root = RepoRoot();
            string exit = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRExitGiveWay.cs"));
            string nav = File.ReadAllText(Path.Combine(root, "JunctionRules", "Vanilla", "JRCarNavigationSystem.cs"));
            string box = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRYellowBox.cs"));
            string paint = File.ReadAllText(Path.Combine(root, "JunctionRules", "Paint", "YellowBoxPaintSystem.cs"));
            int de = exit.IndexOf("private void DriveEntry(", StringComparison.Ordinal);
            int deEnd = de < 0 ? -1 : exit.IndexOf("}", exit.IndexOf("{", de, StringComparison.Ordinal), StringComparison.Ordinal);
            string driveEntry = de < 0 || deEnd < 0 ? "" : exit.Substring(de, deEnd - de);
            Check(driveEntry.Length > 0 && !driveEntry.Contains("ExitDriveEntries") && !driveEntry.Contains("ExitDriveUnjudged"), "G12: the pivot hand-over no longer counts crossings");
            Check(exit.Contains("m_DriveCrossing && ExitEvent(eventTable, self, frame, ExitGiveWay.DriveCrossKind)") && exit.Contains("ExitGiveWay.Crossing(m_DriveGoStood, m_DriveJudged && m_DriveLate)"),
                "G12: NoteExit counts the crossing once per episode where the front passes the spot");
            Check(exit.Contains("if (ExitGiveWay.Committed(true, goStands, currentSpeed >= ExitGiveWay.CommitMovingSpeed))") && exit.Contains("m_DriveCommitted = true;"), "G12: a standing Go commits (no re-judgement on the pavement)");
            Check(exit.Contains("&& !m_ExitWaiting && m_DrivePast"), "G12: 'standing on the road' only with the front past the sidewalk spot");
            Check(nav.Contains("// 0.4.9 (JR-T-015): the event table for driving") && nav.Contains("carLaneSpeedIterator.m_JR.SetEvents(m_JRSafetyEvents, m_SimulationFrame);"), "S2-26 hands the event table in for driving");
            Check(nav.Contains("if (laneIterator.m_JR.m_ExitRule)\n\t\t\t{\n\t\t\t\tlaneIterator.m_JR.NoteExit("), "S2-22: NoteExit (Go records) runs whenever the exit rule is on");
            int s51 = nav.IndexOf("JR-STAGE2 BEGIN S2-51:", StringComparison.Ordinal), s51b = nav.IndexOf("JR-STAGE2 BEGIN S2-51b:", StringComparison.Ordinal);
            int rq = nav.IndexOf("m_LaneReservations.Enqueue(new CarNavigationHelpers.LaneReservation(currentLaneData.m_Lane, 0f, 96));", StringComparison.Ordinal);
            int rq2 = nav.IndexOf("m_LaneReservations.Enqueue(new CarNavigationHelpers.LaneReservation(carNavigationLane.m_Lane, 0f, 96));", StringComparison.Ordinal);
            Check(s51 > 0 && rq > s51 && rq2 > rq && s51b > rq2, "G13: S2-51 before both keep-clear reservations, S2-51b after them");
            Check(nav.Contains("currentLaneData.m_LaneFlags = jrCurrentFlags;") && nav.Contains("jrNav.m_Flags = jrNextFlags;"), "G13: S2-51b restores both flags");
            Check(exit.Contains("ExitGiveWay.IsDrivewayExitLane((uint)cur.m_Flags)") && exit.Contains("ExitGiveWay.IsDrivewayExitLane((uint)next.m_Flags)"), "G13: only a driveway leaving lane's request is withdrawn");
            Check(box.Contains("ExitBands.HoldTouchMetres(m_VehicleLength, m_RoomMargin) / laneLength") && box.Contains("NoteEnteringHeld(self);"), "G14/G15: per-vehicle band merge in BoxHold; entering holds recorded");
            int eh = box.IndexOf("private float EnteringHold(", StringComparison.Ordinal);
            int early = eh < 0 ? -1 : box.IndexOf("if (!(hold < currentMax))", eh, StringComparison.Ordinal);
            Check(early > 0 && box.IndexOf("NoteEnteringHeld(self);", early, StringComparison.Ordinal) < box.IndexOf("return float.MaxValue;", early, StringComparison.Ordinal),
                "review F1: a standing entrant held harder by the one ahead is recorded too (queue of vans for a full yard)");
            Check(exit.Contains("if (m_DriveCommitted) ExitEvent(eventTable, self, frame, ExitGiveWay.DriveGoKind);"), "review F3: a committed crossing keeps its Go fresh");
            string merge = File.ReadAllText(Path.Combine(root, "JunctionRules", "Core", "BoxMerge.cs"));
            Check(merge.Contains("if (same != null) { same.Members.Add(m); continue; }"), "review F6: a failed union draws a joint box's identical rings once, every member kept in that group");
            Check(exit.Contains("bool entering = !occupies && gap > 0f && gap <= ExitGiveWay.PresentMetres && (EnteringHeld(e) || (m_EntryScan && EntryGapHeld(e)));") && exit.Contains("ExitGiveWay.VehicleBlocks(gap, occupies, speed, braking, delta, entering)"),
                "G15: the exit scan asks whether a standing vehicle waits to turn in");
            Check(paint.Contains("Dictionary<Box, List<V3>> joint = JointBoxes();") && paint.Contains("ExitBoxWindows.Chains(windows, ExitBands.MergeGapMetres)"), "G14: the paint builds near boxes on one road as one");
        }
    }
}
