using System;
using System.Collections.Generic;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.10 (JR-T-016): the 0.4.9 live gaps G18 / G19 (17 Nov 2026 09:50+, Alderbrook Street, edge 984373, log
    /// JunctionRules.Mod.log 09:50:00, read-only cs2 reads, main's tracks hatch.txt / sedan.txt).
    ///
    /// Geometry from the live log and lanes: the edge's centre curve is straight, 192.07 m (t 0.102 = 19.6 m ... t 0.857 =
    /// 164.6 m along), from about (-1900.96, 456.58) southwards; its lanes run on the CUT part 7.2..175.3 m (the
    /// northbound lane 984373/6 starts at the south mouth (-1898.21, 281.27), cs2_junction_lanes; 1.5 m west of the centre).
    /// Painted windows (log): 193433 exit 110.0..118.0 m, 843726 derived entrance 119.3..127.3 m, built as ONE box
    /// 110.0..127.3 m; 843726 exit 160.6..168.6 m (z 296.0..288.0).
    /// </summary>
    internal static partial class Program
    {
        private const float AbEdgeLength = 192.07f;
        private const float AbCutStart = 7.2f, AbCutEnd = 175.3f;

        /// <summary>A point on Alderbrook's centre curve 'along' metres from its start (x drifts +0.0243 m per m).</summary>
        private static void AbCentre(float along, out float x, out float z)
        {
            x = -1900.96f + 0.02424f * along;
            z = 456.58f - along * 0.99971f;
        }

        /// <summary>The northbound lane (drawn south to north, the cut part, 1.5 m west of the centre) as straight Bezier points.</summary>
        private static void AbNorthbound(out float ax, out float az, out float bx, out float bz, out float cx, out float cz, out float dx, out float dz)
        {
            AbCentre(AbCutEnd, out ax, out az);
            AbCentre(AbCutStart, out dx, out dz);
            ax -= 1.5f; dx -= 1.5f;
            bx = ax + (dx - ax) / 3f; bz = az + (dz - az) / 3f;
            cx = ax + 2f * (dx - ax) / 3f; cz = az + 2f * (dz - az) / 3f;
        }

        /// <summary>z on the northbound lane at driving coordinate u (0 = south mouth).</summary>
        private static float AbNbZ(float u)
        {
            AbNorthbound(out _, out float az, out _, out _, out _, out _, out _, out float dz);
            return az + (dz - az) * u;
        }

        /// <summary>
        /// 0.4.3-0.4.9's band on a lane: the edge t mapped through EdgeLane.m_EdgeDelta (here the combined lane drawn against
        /// the edge, delta (1, 0)), the box length centred on it (BoxDriving.Band).
        /// </summary>
        private static bool OldBand(float t, float laneLength, out float b0, out float b1)
        {
            float access = (t - 1f) / (0f - 1f);
            return BoxDriving.Band(access, laneLength, 8f, false, out b0, out b1);
        }

        private static bool NewBand(float fromM, float toM, out float b0, out float b1)
        {
            AbNorthbound(out float ax, out float az, out float bx, out float bz, out float cx, out float cz, out float dx, out float dz);
            AbCentre(fromM, out float x0, out float z0);
            AbCentre(toM, out float x1, out float z1);
            float len = AbCutEnd - AbCutStart;
            return LaneWindow.OnLane(ax, az, bx, bz, cx, cz, dx, dz, len, false, x0, z0, x1, z1, out b0, out b1);
        }

        /// <summary>
        /// 0.4.10 (G20, owner shot ae3ba73f, Alderwood edge 557050, Scandic Baron No. 40 = building 839552): the derived
        /// entrance box (window 34.7..42.7 m) and the saved exit box (45.5..53.5 m) are 2.8 m apart and were drawn as two
        /// boxes with a strip of road between (0.4.9 merged only below 2 m).
        /// </summary>
        private static void G20MergeGapTests()
        {
            uint all = MedianSplit.AllCarriageways;
            var w = new List<ExitWindow> { new ExitWindow(557050, all, 45.5, 53.5), new ExitWindow(557050, all, 34.7, 42.7) };
            Check(ExitBoxWindows.Chains(w, 2.0).Count == 0, "G20 root cause: with 0.4.9's 2 m gap the No. 40 boxes (2.8 m apart) stay two boxes");
            var chains = ExitBoxWindows.Chains(w, ExitBands.MergeGapMetres);
            Check(chains.Count == 1 && chains[0].Members.Count == 2 && Math.Abs(chains[0].From - 34.7) < 1e-9 && Math.Abs(chains[0].To - 53.5) < 1e-9,
                "G20: 0.4.10 builds them as ONE box 34.7..53.5 m (paint and driving use the same chains)");
            Check(EntranceBox.WindowFits(34.7, 53.5, 0, 120, 0.5, null, out _), "the joint window fits when no marking lies in the gap");
            // full 0.4.9 log (JunctionRules.Mod.2026-11-17_049.log lines 13-14): edge 557050's part 8.9..127.3 m, 1 marking on
            // the road; stop / give-way lines lie at the road's ends (BoxShapes.StopLines, zebras at the end nodes), e.g. 126.5 m
            Check(EntranceBox.WindowFits(34.7, 53.5, 8.9, 127.3, 0.5, new[] { new AlongSpan(126.3, 126.6) }, out string fitWhy), "No. 40: the joint window 34.7..53.5 m fits its road (part 8.9..127.3 m, marking at the end): " + fitWhy);
            var pairs = ExitBoxWindows.Neighbours(w, ExitBoxWindows.LogPairsWithin);
            Check(pairs.Count == 1 && pairs[0].A == 1 && pairs[0].B == 0 && Math.Abs(pairs[0].Gap - 2.8) < 1e-9, "one neighbour pair logged, gap 2.8 m");
            string merged = ExitBoxWindows.PairDecision(2.8, ExitBands.MergeGapMetres, true, null, 34.7, 53.5);
            Check(merged.StartsWith("MERGED: gap 2.8 m < 6.0 m") && merged.Contains("34.7..53.5"), "log: " + merged);
            string far = ExitBoxWindows.PairDecision(6.5, ExitBands.MergeGapMetres, false, null, 0, 0);
            Check(far.StartsWith("NOT merged: gap 6.5 m >= 6.0 m"), "log: " + far);
            string marked = ExitBoxWindows.PairDecision(2.8, ExitBands.MergeGapMetres, false, "joint window crosses a road marking", 0, 0);
            Check(marked.StartsWith("NOT merged: gap 2.8 m < 6.0 m but the joint box is refused: joint window crosses a road marking"), "log: " + marked);
            Check(!EntranceBox.WindowFits(34.7, 53.5, 0, 120, 0.5, new[] { new AlongSpan(43.5, 43.8) }, out string why) && why.Contains("road marking"), "a marking in the gap keeps them apart: " + why);
            // why 6 m: a gap below the shortest vehicle (~4.2-4.5 m) + the room margin (1 m) holds no vehicle
            Check(ExitBands.MergeGapMetres >= 4.5f + RoomScan.DefaultMargin && ExitBands.MergeGapMetres <= 6.5f, "merge gap = shortest vehicle + room margin, rounded up (6 m)");
            Check(ExitBands.HoldTouchMetres(4.2f, 1f) == ExitBands.MergeGapMetres && ExitBands.HoldTouchMetres(16.5f, 1f) == 17.5f,
                "driving per vehicle: at least the paint's gap (a car), its own length + margin when longer (an artic)");
            var n3 = ExitBoxWindows.Neighbours(new List<ExitWindow> { new ExitWindow(1, all, 0, 8), new ExitWindow(1, all, 30, 38), new ExitWindow(1, 0x1, 9, 17) }, ExitBoxWindows.LogPairsWithin);
            Check(n3.Count == 0, "pairs only on one road and carriageway set, below 15 m");
        }

        private static void G18G19Tests()
        {
            float laneLength = AbCutEnd - AbCutStart;
            AbNorthbound(out float ax, out float az, out float bx, out float bz, out float cx, out float cz, out float dx, out float dz);
            Check(Math.Abs(ax - -1898.21f) < 0.1f && Math.Abs(az - 281.27f) < 0.1f, "live lane: northbound 984373/6 starts at the south mouth (-1898.21, 281.27)");

            // ---- G19 placement: 843726's exit box, painted z 288.0..296.0 (164.6 m along, t 0.857)
            Check(OldBand(164.6f / AbEdgeLength, laneLength, out float o0, out float o1), "0.4.9 band exists");
            float oz0 = AbNbZ(o0), oz1 = AbNbZ(o1);
            Check(oz0 > 300f && oz1 > 308f, $"G19 root cause: 0.4.9 drove 843726's exit box at z {oz0:0.0}..{oz1:0.0}, 12-13 m north of the paint (z 288..296)");
            Check(NewBand(160.6f, 168.6f, out float n0, out float n1), "0.4.10 band exists");
            float nz0 = AbNbZ(n0), nz1 = AbNbZ(n1);
            Check(Math.Abs(nz0 - 288.0f) < 0.1f && Math.Abs(nz1 - 296.0f) < 0.1f, $"0.4.10: the band is the painted box, z {nz0:0.00}..{nz1:0.00}");
            // frame 36628751: sedan 909747 stood at z 294.4 (front ~296.6, car ahead 910969 at 298.9): in the paint, outside
            // the 0.4.9 band (so nothing held it at the box line); 0.4.10: z 294.4 is inside the band
            Check(294.4f > nz0 && 294.4f < nz1 && 294.4f < oz0, "G19 sedan: stood in the painted box, which 0.4.9 did not drive (band further north); 0.4.10 drives it");
            // sedan.txt: the car ahead 910969 crept to z 298.9 (front ~301.1) and stood there 36628579..36629298: at the 0.4.9
            // DRIVEN box line (z ~301.3), i.e. held for room past the misplaced box; the sedan queued behind it in the paint
            Check(Math.Abs(298.9f + 2.2f - oz0) < 1.0f, $"G19: 910969 stood with its front at the 0.4.9 driven line (z {oz0:0.0})");
            // frame 36627998: truck 380975 (z 295.8) and car 910952 (z 288.2) southbound stood in the paint queued from the junction:
            // same cause (the southbound lane maps the same t to the same metres along: the band was at z ~300..309 too)
            Check(295.8f < oz0 && 288.2f < oz0, "G19 southbound truck / car: in the painted box, south of the 0.4.9 band: same cause (misplaced band), not a moving-queue entry");

            // ---- G18: 193433 exit + 843726 derived entrance built as ONE box 110.0..127.3 m (z 346.6..329.3)
            var w = new List<ExitWindow> { new ExitWindow(984373, MedianSplit.AllCarriageways, 110.0, 118.0), new ExitWindow(984373, MedianSplit.AllCarriageways, 119.3, 127.3) };
            var chains = ExitBoxWindows.Chains(w, ExitBands.MergeGapMetres);
            Check(chains.Count == 1 && chains[0].From == 110.0 && chains[0].To == 127.3, "the paint's chain: one box 110.0..127.3 m");
            Check(NewBand(110.0f, 127.3f, out float j0, out float j1), "0.4.10 joint band exists");
            float jz0 = AbNbZ(j0), jz1 = AbNbZ(j1);
            Check(Math.Abs(jz0 - 329.3f) < 0.1f && Math.Abs(jz1 - 346.6f) < 0.1f, $"G18: 0.4.10 drives the joint painted box z {jz0:0.00}..{jz1:0.00} (entry for northbound traffic z 329.3)");
            // 0.4.9: each member mapped through t, merged per vehicle (hatchback 4.2 m + 1 m margin)
            OldBand(114.0f / AbEdgeLength, laneLength, out float e0, out float e1);
            OldBand(123.3f / AbEdgeLength, laneLength, out float i0, out float i1);
            float touch = ExitBands.HoldTouchMetres(4.2f, 1f) / laneLength;
            var old = ExitBands.Chain(new List<(float, float)> { (i0, i1), (e0, e1) }, touch);
            float oldEntryZ = AbNbZ(old[0].b0);
            Check(oldEntryZ > 333f, $"G18 root cause: 0.4.9's entry for the joint box was z {oldEntryZ:0.0}, inside the paint (starts z 329.3): the hatchback held there stood in the box");
            // hatchback 909920 track: z 330.2 at 5.82 m/s, stopped at z 333.6 (front ~335.7), car past the box 911176 stopped at z 349.5
            float hatchFront = 333.6f + 2.1f;
            Check(hatchFront > jz0 && hatchFront < oldEntryZ + 2f, "the live stop (front ~335.7) lies inside the paint and at the 0.4.9 entry");
            // 0.4.10: the car past the box (911176, rear ~347.3) leaves 0.7 m past the box end: no room for 4.2 + 1 m: held at z 329.3
            RoomScan rs = RoomScan.Begin(4.2f, 1f);
            rs.FreeNow = true;
            rs.Add(347.3f - jz1, 4.4f, 0f, 911176, 1);
            Check(!rs.HasRoom && rs.BlockerIndex == 911176, "0.4.10: no room past the painted box: the hatchback is held before z 329.3, naming 911176");

            // ---- LaneWindow details
            Check(!LaneWindow.FromOffsets(1f, 1f, 80f, false, out _, out _), "a window beyond the lane's end (the other half lane of a two-piece edge) is no band");
            Check(LaneWindow.FromOffsets(0.9f, 1f, 80f, false, out float h0, out float h1) && h0 == 0.9f && h1 == 1f, "a window over a half lane's end is clipped to it");
            Check(LaneWindow.FromOffsets(0.2f, 0.3f, 100f, true, out float r0, out float r1) && Math.Abs(r0 - 0.7f) < 1e-6f && Math.Abs(r1 - 0.8f) < 1e-6f, "a lane driven against its curve: driving coordinates flipped");
            Check(LaneWindow.FromOffsets(0.3f, 0.2f, 100f, false, out float s0, out float s1) && s0 == 0.2f && s1 == 0.3f, "window ends in either order");
            Check(!LaneWindow.FromOffsets(0.5f, 0.504f, 100f, false, out _, out _), "less than 0.5 m on the lane: no band");
            // a curved lane (quarter-circle-like, radius ~50 m): the window ends project perpendicular onto the lane
            float u = LaneWindow.Project(0f, 0f, 27.6f, 0f, 50f, 22.4f, 50f, 50f, 35.36f + 2f, 14.64f - 2f);
            LaneWindow.Point(0f, 0f, 27.6f, 0f, 50f, 22.4f, 50f, 50f, u, out float px, out float pz);
            Check(u > 0.4f && u < 0.6f && Math.Abs(Math.Sqrt((px - 37.36) * (px - 37.36) + (pz - 12.64) * (pz - 12.64)) - Math.Sqrt(8.0)) < 0.2, "curved lane: the closest point of the curve (perpendicular foot)");
            Check(LaneWindow.Project(0f, 0f, 1f, 0f, 2f, 0f, 3f, 0f, -5f, 1f) == 0f && LaneWindow.Project(0f, 0f, 1f, 0f, 2f, 0f, 3f, 0f, 9f, -1f) == 1f, "points beyond the ends project onto the ends");

            // ---- hand-over: a joint box's members share one window: one band
            var bands = ExitBands.Normalise(new[]
            {
                new ExitBand(5L, 0.6f, 1u, 1f, 2f, 3f, 4f),
                new ExitBand(5L, 0.6f, 1u, 1f, 2f, 3f, 4f),
                new ExitBand(5L, 0.6f, 1u, 1f, 2f, 3f, 5f),
            }, ExitBands.Capacity, out _);
            Check(bands.Length == 2, "the same window at the same t is kept once; another window stays");
            int v0 = BoxEdgeHandoff.Version;
            BoxEdgeHandoff.Publish(bands);
            int v1 = BoxEdgeHandoff.Version;
            Check(v1 == v0 + 1 && BoxEdgeHandoff.Window.Length == 8 && BoxEdgeHandoff.Window[0] == 1f && BoxEdgeHandoff.Window[4] == 1f && BoxEdgeHandoff.Window[3] + BoxEdgeHandoff.Window[7] == 9f, "handoff carries x0, z0, x1, z1 per band");
            BoxEdgeHandoff.Publish(bands);
            Check(BoxEdgeHandoff.Version == v1, "same bands and windows: no new version");
            BoxEdgeHandoff.Publish(new[] { new ExitBand(5L, 0.6f, 1u, 1f, 2f, 3f, 6f) });
            Check(BoxEdgeHandoff.Version == v1 + 1, "a window moved: new version");
            BoxEdgeHandoff.Publish(new[] { new ExitBand(5L, 0.6f, 1u) });
            Check(BoxEdgeHandoff.Version == v1 + 2 && float.IsNaN(BoxEdgeHandoff.Window[0]), "a band without a window is handed over as NaN (the job does not drive it)");
            BoxEdgeHandoff.Publish(new ExitBand[0]);

            // ---- G19 rule: room FREE NOW (UK: do not enter unless your exit is clear)
            // stop-and-go: the car ahead creeps at 4.66 m/s (910969's speed in sedan.txt) with its rear 3.0 m past the box end;
            // the sedan (4.4 m + 1 m margin) needs 5.4 m. 0.4.9 counted the creeping car's braking distance as room.
            RoomScan creditScan = RoomScan.Begin(4.4f, 1f);
            creditScan.Add(3.0f, 4.4f, 2.6f, 910969, 1);
            RoomScan freeScan = RoomScan.Begin(4.4f, 1f);
            freeScan.FreeNow = true;
            freeScan.Add(3.0f, 4.4f, 2.6f, 910969, 1);
            Check(creditScan.HasRoom && !freeScan.HasRoom && freeScan.HeldForMovingQueue && freeScan.BlockerIndex == 910969,
                "G19: a creeping queue 3 m past the box: 0.4.9 let the car in (credit), 0.4.10 holds it (free now), counted as a free-now hold, naming the car ahead");
            RoomScan flow = RoomScan.Begin(4.4f, 1f);
            flow.FreeNow = true;
            flow.Add(12f, 4.4f, 6f, 1, 1);
            Check(flow.HasRoom && !flow.HeldForMovingQueue, "G13: flowing traffic with its rear 12 m past the box: room free now, no hold");
            RoomScan empty = RoomScan.Begin(16.5f, 1f);
            empty.FreeNow = true;
            Check(empty.HasRoom, "nothing past the box: room (the unknown rest counts as free, as before)");
            RoomScan queued = RoomScan.Begin(4.4f, 1f);
            queued.FreeNow = true;
            queued.AddQueued(4.4f);
            Check(queued.HasRoom, "a car ahead still in the box with nothing past it: room (unchanged)");
            // No new deadlock (G15): a deadlock is a standing state; for standing vehicles the credit is 0, so free-now and the
            // 0.4.9 rule give the same verdict on every standing scene: no hold exists that 0.4.9 would not also make there.
            var rnd = new Random(1710);
            bool same = true;
            for (int n = 0; n < 5000 && same; n++)
            {
                float len = 3.5f + (float)rnd.NextDouble() * 15f, margin = (float)rnd.NextDouble() * 2f;
                RoomScan a = RoomScan.Begin(len, margin), b = RoomScan.Begin(len, margin);
                b.FreeNow = true;
                int k = rnd.Next(0, 5);
                float rear = (float)rnd.NextDouble() * 10f - 2f;
                for (int i = 0; i < k; i++)
                {
                    if (rnd.Next(3) == 0) { float q = 4f + (float)rnd.NextDouble() * 8f; a.AddQueued(q); b.AddQueued(q); continue; }
                    float l = 4f + (float)rnd.NextDouble() * 14f;
                    a.Add(rear, l, 0f, i + 1, 1);
                    b.Add(rear, l, 0f, i + 1, 1);
                    rear += l + (float)rnd.NextDouble() * 12f;
                }
                same = a.HasRoom == b.HasRoom && a.BlockerIndex == b.BlockerIndex && !b.HeldForMovingQueue;
            }
            Check(same, "G15 check: standing scenes (no credit): free-now = the 0.4.9 verdict and blocker (5000 random scenes): no new deadlock state");

            // ---- wiring (source checks)
            string root = RepoRoot();
            string yb = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRYellowBox.cs"));
            string ex = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRExitGiveWay.cs"));
            string wj = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            int freeNowCalls = 0, at = 0;
            while ((at = yb.IndexOf("freeNow: true", at, StringComparison.Ordinal)) >= 0) { freeNowCalls++; at++; }
            Check(freeNowCalls == 3 && ex.Contains("ref pedestrianLanes, freeNow: true,\n                    frontToStart: (b1 - t) * length);"), "free now: junction box line, exit / entrance band (through and leaving), right-turner's turn-spot re-check, and a vehicle leaving into a box");
            Check(wj.Contains("scan.FreeNow = freeNow;") && wj.Contains("bool sideObstacles = false, bool freeNow = false,\n            float frontToStart = -1f, bool lookPast = true)"), "HasRoomPast: free now only on request (zebras keep the moving-queue credit)");
            int zebra = wj.IndexOf("if (HasRoomPast(lane, layout.SectionEnd(k), inverse, length, -1f, Entity.Null, self, safeTimeStep,", StringComparison.Ordinal);
            int zebraEnd = zebra < 0 ? -1 : wj.IndexOf(")", wj.IndexOf("ref pedestrianLanes", zebra, StringComparison.Ordinal), StringComparison.Ordinal);
            Check(zebra >= 0 && zebraEnd > zebra && !wj.Substring(zebra, zebraEnd - zebra).Contains("freeNow"), "the zebra room test is unchanged");
            Check(!yb.Contains("BoxDriving.Band(") && !ex.Contains("BoxDriving.Band(") && !ex.Contains("m_EdgeDelta.x"), "no band is laid from t + the box length any more");
            string sw = File.ReadAllText(Path.Combine(root, "JunctionRules", "Systems", "CarDrivingSwitchSystem.cs"));
            Check(sw.Contains("merged == m_MergedVersion"), "driving bands rebuilt when the paint regroups");
            string db = File.ReadAllText(Path.Combine(root, "JunctionRules", "Boxes", "DrivingBands.cs"));
            Check(db.Contains("BoxShapes.Entrance(em, src.Access, p.KerbInset, p.EntranceLength, p.MarkingMargin, src.BuildingPos)")
                && db.Contains("ExitBoxWindows.Assemble(windows, ExitBands.MergeGapMetres, Fits)")
                && db.Contains("BoxShapes.Entrance(em, first.Access, p.KerbInset, p.EntranceLength, p.MarkingMargin, first.BuildingPos, from, to)"),
                "driving windows are made by the paint's own shape code (box window, joint windows as JointBoxes)");
        }
    }
}
