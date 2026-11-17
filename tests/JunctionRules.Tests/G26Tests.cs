using System;
using System.Collections.Generic;
using System.IO;
using JunctionRules.Core;
using CW = JunctionRules.Core.CarriagewayOutline;
using G = JunctionRules.Core.BoxPaintGeometry;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.12 (JR-T-018, gap G26, owner live 17 Nov 20:2x): the exit box of building 77236 (Driveway@(25.4, 21.3)) on Sunset
    /// Street edge 1155147 at (83.9, 605.25, 432.3) was painted kerb to kerb ACROSS a raised centre median ("8.0 m x kerb to
    /// kerb", footways 3.75 | 3.75, no median note). Read-only cs2_road_outline 17 Nov: RoadBuilder prefab
    /// re9b1ba39-07e6-4799-a6b5-931265703461-76561198022823782, 16 m: Sidewalk 4 (x -6.25) | Car Drive 3 (x -2.5) | Median
    /// Piece 2 (x 0, BlockTraffic | BlockCrosswalk) | Car Drive 3 (x 2.5) | Sidewalk 4 (x 6.25); both car pieces Invert +
    /// FlipLanes = the SAME direction (one-way, node 621408: 2 lanes in, 0 out). 0.4.4-0.4.11 cut only at a median whose
    /// far lanes ran the other way. Owner: the box is bound by ANY median and never goes over it; junction boxes never
    /// paint over a median nose or island; driving = paint.
    /// </summary>
    internal static partial class Program
    {
        private const int G26Road = MedianSplit.LaneRoad, G26Inv = MedianSplit.LaneInvert, G26Ped = MedianSplit.LanePedestrian;
        private const int G26Track = 0x4;   // LaneFlags.Track (not a car lane)

        /// <summary>Sunset Street 1155147's edge composition lanes (car lanes 3 m at -2.5 / 2.5, carriageways 0 / 1, same direction).</summary>
        private static List<CompositionLaneInfo> SunsetLanes() => new List<CompositionLaneInfo>
        {
            new CompositionLaneInfo(-6.25, 4, 0, G26Ped),
            new CompositionLaneInfo(-2.5, 3, 0, G26Road),
            new CompositionLaneInfo(2.5, 3, 1, G26Road),
            new CompositionLaneInfo(6.25, 4, 1, G26Ped),
        };

        private static readonly List<DividerInfo> SunsetMedian = new List<DividerInfo> { new DividerInfo(-1, 1) };

        private static void G26ExitTests()
        {
            const double half = 8, foot = 3.75, margin = 0.5;
            // ---- E1: the live failure reproduced with the 0.4.4 rule (no pieces, no margin) is now a cut
            Check(MedianSplit.SideSpan(SunsetLanes(), true, out SideSpanResult old) && old.Divided, "E1 Sunset Street (one-way + median): a median now even with the 0.4.4 entry point (lane gap, no margin)");
            // ---- E2: the real pieces + margin, both sides of the road
            foreach (bool left in new[] { true, false })
            {
                string side = left ? "left" : "right";
                Check(MedianSplit.SideSpan(SunsetLanes(), SunsetMedian, left, margin, out SideSpanResult s), $"E2 {side}: span found");
                Check(s.Divided && s.FarCut && !s.NearCut && s.FromPieces, $"E2 {side}: far side cut at the median piece (divided {s.Divided}, far {s.FarCut}, near {s.NearCut}, pieces {s.FromPieces})");
                Check(s.CarriagewayMask == MedianSplit.Bit(left ? 0 : 1), $"E2 {side}: only the exit's carriageway is boxed / driven (mask 0x{s.CarriagewayMask:X})");
                double far = left ? s.Hi : s.Lo, kerb = left ? s.Lo : s.Hi;
                Check(Math.Abs(far - (left ? -1.5 : 1.5)) < 1e-9, $"E2 {side}: box stops 0.5 m before the median's edge at x {(left ? -1 : 1)} (got {far})");
                Check(Math.Abs(kerb - (left ? -4 : 4)) < 1e-9, $"E2 {side}: kerb side as before (got {kerb})");
                MedianSplit.Insets(half, foot, foot, s, left, out double iL, out double iR);
                double expectFar = half + 1.5;   // 9.5 m in from the far composition edge (the median edge at -1 / 1, + 0.5 m)
                Check(left ? (Math.Abs(iL - foot) < 1e-9 && Math.Abs(iR - expectFar) < 1e-9) : (Math.Abs(iR - foot) < 1e-9 && Math.Abs(iL - expectFar) < 1e-9),
                    $"E2 {side}: insets keep the building's footway {foot} and move the far side to the median - 0.5 m ({iL} | {iR})");
                // the box itself (straight 40 m edge along x; composition left = +z here as in BoxMergeTests D2)
                var c = new List<V3>(); for (int i = 0; i <= 20; i++) c.Add(new V3(i * 2, 605.25, 0));
                var box = EntranceBox.BuildStraight(c, half, iL, iR, null, margin, new V2(20, left ? 2.5 : -2.5));
                Check(box.Outline != null, $"E2 {side}: exit box builds ({box.Failure})");
                if (box.Outline == null) continue;
                double zmin = double.MaxValue, zmax = double.MinValue;
                foreach (V3 p in box.Outline) { zmin = Math.Min(zmin, p.Z); zmax = Math.Max(zmax, p.Z); }
                double nearMedian = Math.Min(Math.Abs(zmin), Math.Abs(zmax));
                Check(zmin >= 0 || zmax <= 0, $"E2 {side}: the box never crosses the road centre (z {zmin:0.00}..{zmax:0.00})");
                Check(nearMedian >= 1.5 - 1e-6, $"E2 {side}: the box keeps >= 0.5 m off the median kerb at |z| = 1 (inner edge |z| = {nearMedian:0.00})");
                Check(Math.Abs((zmax - zmin) - (half - foot - 1.5)) < 1e-6, $"E2 {side}: 2.75 m across = kerb (4.25) to median - 0.5 (1.5) ({zmax - zmin:0.00})");
            }

            // ---- E3: Ironplace Avenue (two-way divided, already cut in 0.4.4): now 0.5 m off the median as well
            {
                var lanes = IronplaceLanesG26();
                Check(MedianSplit.SideSpan(lanes, new List<DividerInfo> { new DividerInfo(-1, 1) }, false, margin, out SideSpanResult r) && r.Divided && Math.Abs(r.Lo - 1.5) < 1e-9 && r.CarriagewayMask == MedianSplit.Bit(1),
                    $"E3 Ironplace right side: median edge 1 + 0.5 = 1.5 (was 1.0 in 0.4.11; got {r.Lo})");
            }

            // ---- E4: undivided roads from the live re-check keep kerb to kerb (no BlockTraffic piece: one carriageway index)
            {
                var alderbrook = new List<CompositionLaneInfo> { new CompositionLaneInfo(-1.5, 3, 0, G26Road | G26Inv), new CompositionLaneInfo(1.5, 3, 0, G26Road) };
                Check(MedianSplit.SideSpan(alderbrook, new List<DividerInfo>(), true, margin, out SideSpanResult a) && !a.Divided && a.CarriagewayMask == MedianSplit.AllCarriageways && a.Lo == -3 && a.Hi == 3,
                    "E4 Alderbrook Street 73431 (Small Road): undivided, full span, all carriageways");
                var victoria = new List<CompositionLaneInfo> { new CompositionLaneInfo(-3, 3, 0, G26Road), new CompositionLaneInfo(0, 3, 0, G26Road), new CompositionLaneInfo(3, 3, 0, G26Road) };
                Check(MedianSplit.SideSpan(victoria, null, false, margin, out SideSpanResult v) && !v.Divided, "E4 Victoria Street 988860 (3-lane one-way): no median");
                // a two-way centre turn lane is painted: the game keeps it in the SAME carriageway (no BlockTraffic piece)
                var turn = new List<CompositionLaneInfo> { new CompositionLaneInfo(-3, 3, 0, G26Road | G26Inv), new CompositionLaneInfo(0, 3, 0, G26Road | MedianSplit.LaneTwoway), new CompositionLaneInfo(3, 3, 0, G26Road) };
                Check(MedianSplit.SideSpan(turn, null, true, margin, out SideSpanResult t) && !t.Divided, "E4 painted centre turn lane: no median, kerb to kerb");
            }

            // ---- E5: a divider between the kerb and the exit's lanes (protected bike track): the box starts past it
            {
                var lanes = new List<CompositionLaneInfo>
                {
                    new CompositionLaneInfo(-8, 2, 0, G26Road | MedianSplit.LaneBicyclesOnly),
                    new CompositionLaneInfo(-4.5, 3, 1, G26Road | G26Inv),
                    new CompositionLaneInfo(-1.5, 3, 1, G26Road | G26Inv),
                    new CompositionLaneInfo(1.5, 3, 1, G26Road),
                    new CompositionLaneInfo(4.5, 3, 1, G26Road),
                };
                var dividers = new List<DividerInfo> { new DividerInfo(-7, -6) };
                Check(MedianSplit.SideSpan(lanes, dividers, true, margin, out SideSpanResult b) && b.Divided && b.NearCut && !b.FarCut, "E5 bike track behind a divider: near-side cut only");
                Check(Math.Abs(b.Lo - (-5.5)) < 1e-9 && Math.Abs(b.Hi - 6) < 1e-9 && b.CarriagewayMask == MedianSplit.Bit(1), $"E5 box from the divider + 0.5 (-5.5) to the far kerb (6) ({b.Lo}..{b.Hi})");
                MedianSplit.Insets(9, 2, 2, b, true, out double iL, out double iR);
                Check(Math.Abs(iL - 3.5) < 1e-9 && Math.Abs(iR - 2) < 1e-9, $"E5 insets: building side moves in past the divider (3.5 | 2, got {iL} | {iR})");
            }

            // ---- E6: tram reservation between two dividers (centre tracks with kerbs both sides): the far side of the
            // building's carriageway stops at the first divider; tracks and the other carriageway get no box
            {
                var lanes = new List<CompositionLaneInfo>
                {
                    new CompositionLaneInfo(-7.5, 3, 0, G26Road | G26Inv),
                    new CompositionLaneInfo(-4.5, 3, 0, G26Road | G26Inv),
                    new CompositionLaneInfo(-1.5, 3, 1, G26Track),
                    new CompositionLaneInfo(1.5, 3, 1, G26Track | G26Inv),
                    new CompositionLaneInfo(4.5, 3, 2, G26Road),
                    new CompositionLaneInfo(7.5, 3, 2, G26Road),
                };
                var dividers = new List<DividerInfo> { new DividerInfo(-3, -3), new DividerInfo(3, 3) };   // zero-width kerbs
                bool trackOn = lanes[2].OnCarriageway;
                Check(MedianSplit.SideSpan(lanes, dividers, true, margin, out SideSpanResult tr) && tr.FarCut && tr.CarriagewayMask == MedianSplit.Bit(0),
                    $"E6 tram reservation (tracks on carriageway: {trackOn}): exit's carriageway only");
                Check(Math.Abs(tr.Hi - (-3.5)) < 1e-9, $"E6 stops 0.5 m before the reservation's kerb at -3 (got {tr.Hi})");
                Check(MedianSplit.SideSpan(lanes, dividers, false, margin, out SideSpanResult tr2) && tr2.CarriagewayMask == MedianSplit.Bit(2) && Math.Abs(tr2.Lo - 3.5) < 1e-9, "E6 other side mirrors (from 3.5)");
            }

            // ---- E7: margin wider than the carriageway: zero-width span, never reversed (the box builder then refuses it)
            {
                var narrow = new List<CompositionLaneInfo> { new CompositionLaneInfo(-1.5, 1, 0, G26Road), new CompositionLaneInfo(1.5, 1, 1, G26Road) };
                Check(MedianSplit.SideSpan(narrow, new List<DividerInfo> { new DividerInfo(-1, 1) }, true, 2.0, out SideSpanResult n) && n.Hi >= n.Lo && n.Hi - n.Lo < 1e-9, $"E7 too narrow: zero width, not reversed ({n.Lo}..{n.Hi})");
            }

            // ---- E8: divider pieces outside the gap between the lane groups (a kerb-side barrier) are ignored; the lane gap is
            // the fallback when no piece lies in it
            {
                var dividers = new List<DividerInfo> { new DividerInfo(-8.5, -8) };
                Check(MedianSplit.SideSpan(SunsetLanes(), dividers, true, margin, out SideSpanResult f) && f.FarCut && !f.FromPieces && Math.Abs(f.Hi - (-1.5)) < 1e-9,
                    $"E8 no piece in the gap: median edges from the lane gap (-1 - 0.5 = {f.Hi})");
            }
        }

        private static List<CompositionLaneInfo> IronplaceLanesG26() => new List<CompositionLaneInfo>
        {
            new CompositionLaneInfo(-10.5, 3, 0, G26Ped | G26Inv),
            new CompositionLaneInfo(-5.5, 3, 0, G26Road | G26Inv),
            new CompositionLaneInfo(-2.5, 3, 0, G26Road | G26Inv),
            new CompositionLaneInfo(2.5, 3, 1, G26Road),
            new CompositionLaneInfo(5.5, 3, 1, G26Road),
            new CompositionLaneInfo(10.5, 3, 1, G26Ped),
        };

        /// <summary>Node 621408 (live fixture): median noses at the two Sunset Street mouths; the junction box keeps 0.5 m off them.</summary>
        private static void G26JunctionTests()
        {
            var la = LiveFixtures621408.Arms;
            var centre = new V2(LiveFixtures621408.Centre[0], LiveFixtures621408.Centre[1]);
            var arms = LiveInputs(la, true);
            var noses = new List<CrosswalkStrip>();
            int added = 0;
            for (int k = 0; k < la.Length; k++)
            {
                var oa = Pts(la[k].OuterA); var ia = Pts(la[k].InnerA); var ob = Pts(la[k].OuterB); var ib = Pts(la[k].InnerB);
                var node = LiveFixtures621408.Nodes[k];
                var mouth = new NodeMouth(oa[0].XZ, ia[0].XZ, ib[0].XZ, ob[0].XZ, node.Width, node.MiddleOffset);
                V2 inward = ((ia[1].XZ - ia[0].XZ) + (ib[1].XZ - ib[0].XZ));
                var pieces = new List<MedianPiece>();
                foreach (var d in node.Dividers) pieces.Add(new MedianPiece(d.Offset, d.Width, 0.6, true, d.PreserveShape));
                added += MedianNose.AddStrips(mouth, inward, pieces, noses, out string through);
                Check(through == null, $"J1 arm {k + 1}: no median runs through ({through})");
            }
            Check(added == 2 && noses.Count == 2, $"J1 two noses (the two Sunset Street mouths), got {added}");
            // the nose on 1155147 sits at the edge's end centre (91.01, 413.54), 0.3 m in, 2 m across
            var n4 = noses[noses.Count - 1];
            V2 mid = (n4.A + n4.B) * 0.5;
            Check(V2.Distance(mid, new V2(91.01, 413.54)) < 0.45 && Math.Abs(V2.Distance(n4.A, n4.B) - 2) < 0.05 && Math.Abs(n4.Width - 0.6) < 1e-9 && n4.Median && !n4.StopLine,
                $"J1 1155147 nose: middle {mid} ~0.3 m in from (91.01, 413.54), {V2.Distance(n4.A, n4.B):0.00} m across, 0.6 m long");
            // the box with and without the noses
            var none = CW.BuildStraight(LiveInputs(la, true), centre, new List<CrosswalkStrip>(), 0.5, new List<V2>(), false);
            var res = CW.BuildStraight(arms, centre, noses, 0.5, new List<V2>(), false);
            Check(res.Outline != null, $"J2 node 621408 builds a box clear of the noses ({res.Failure}; {string.Join("; ", res.Notes)})");
            if (res.Outline == null) return;
            var ring = G.ToXZ(res.Outline);
            double worst = double.MaxValue;
            for (int k = 0; k < ring.Count; k++) foreach (var z in noses) worst = Math.Min(worst, StraightBox.SegmentToStrip(ring[k], ring[(k + 1) % ring.Count], z));
            bool inside = false;
            foreach (var z in noses) inside |= G.Contains(ring, z.A, 0) || G.Contains(ring, z.B, 0) || G.Contains(ring, (z.A + z.B) * 0.5, 0);
            Check(worst >= 0.5 - 0.02 && !inside, $"J2 every box edge keeps the 0.5 m margin from both noses (closest {worst:0.00} m), no nose inside the box");
            if (none.Outline != null)
            {
                var r0 = G.ToXZ(none.Outline);
                double w0 = double.MaxValue;
                for (int k = 0; k < r0.Count; k++) foreach (var z in noses) w0 = Math.Min(w0, StraightBox.SegmentToStrip(r0[k], r0[(k + 1) % r0.Count], z));
                bool in0 = false;
                foreach (var z in noses) in0 |= G.Contains(r0, z.A, 0) || G.Contains(r0, z.B, 0) || G.Contains(r0, (z.A + z.B) * 0.5, 0);
                Console.WriteLine($"  G26 node 621408 without nose input (no zebras / stop lines in the fixture): closest edge to a nose {w0:0.00} m, nose inside {in0}; with: {worst:0.00} m");
            }

            // ---- J3: a median running on through the junction (BlockTraffic without PreserveShape): no box
            {
                var mouth = new NodeMouth(new V2(-8, 0), new V2(0, 0), new V2(0, 0), new V2(8, 0), 16, 0);
                var into = new List<CrosswalkStrip>();
                int n = MedianNose.AddStrips(mouth, new V2(0, 1), new List<MedianPiece> { new MedianPiece(0, 2, 64, true, false) }, into, out string through);
                Check(n == 0 && into.Count == 0 && through != null && through.Contains("runs on through"), "J3 median through the junction: named, no strip, caller builds no box");
            }
            // ---- J4: inward fallback / sign, zero length, non-divider pieces ignored
            {
                var mouth = new NodeMouth(new V2(-8, 0), new V2(0, 0), new V2(0, 0), new V2(8, 0), 16, 0);
                var into = new List<CrosswalkStrip>();
                var pcs = new List<MedianPiece> { new MedianPiece(-2.5, 3, 0, false, false), new MedianPiece(0, 5, 0, true, true) };
                int n = MedianNose.AddStrips(mouth, new V2(0, -1), pcs, into, out string through);
                // outer left (-8,0) -> outer right (8,0): left normal (0, 1) points into the node; a reversed tangent is flipped
                Check(n == 1 && through == null && Math.Abs(into[0].Width - MedianNose.FallbackLength) < 1e-9 && into[0].A.Z > 0 && Math.Abs(into[0].A.Z - MedianNose.FallbackLength / 2) < 1e-9 && Math.Abs(into[0].A.X + 2.5) < 1e-9 && Math.Abs(into[0].B.X - 2.5) < 1e-9,
                    "J4 5 m nose with no length: fallback length, into the node, 5 m across; car piece ignored");
                var off = new NodeMouth(new V2(-8, 0), new V2(-2, 0), new V2(-2, 0), new V2(8, 0), 16, -2);
                Check(V2.Distance(off.At(-2), new V2(-2, 0)) < 1e-9 && V2.Distance(off.At(3), new V2(3, 0)) < 1e-9 && V2.Distance(off.At(-5), new V2(-5, 0)) < 1e-9, "J4 middle offset: halves mapped as the node mesh");
            }
        }

        private static void G26RepoTests()
        {
            string root = RepoRoot();
            string shapes = File.ReadAllText(Path.Combine(root, "JunctionRules", "Boxes", "BoxShapes.cs"));
            Check(shapes.Contains("MedianSide(em, e, a.T, side.Value, margin, out SideSpanResult span, out bool buildingCompLeft)"), "repo: exit / entrance boxes pass the marking margin to the median rule");
            Check(shapes.Contains("MedianSplit.SideSpan(info, Dividers(em, ce), compLeft, margin, out span)"), "repo: the median rule reads the composition's divider pieces");
            Check(shapes.Contains("(p.m_PieceFlags & NetPieceFlags.BlockTraffic) == 0") && shapes.Contains("NetSectionFlags.Underground | NetSectionFlags.Overhead"), "repo: dividers = BlockTraffic pieces above ground");
            Check(shapes.Contains("noses += MedianNoses(em, armEdges[k], node, markings, out string through);"), "repo: junction boxes add every arm's median nose as a never-cover strip");
            Check(shapes.Contains("s.Failure = \"roundabout: the box would cover the centre island"), "repo: a saved roundabout box is refused (island)");
            string cw = File.ReadAllText(Path.Combine(root, "JunctionRules", "Core", "CarriagewayOutline.cs"));
            Check(cw.Contains("ok.FindAll(c => !c.StopLine && !c.Median)"), "repo: median noses are not counted as zebras");
            string bands = File.ReadAllText(Path.Combine(root, "JunctionRules", "Boxes", "DrivingBands.cs"));
            Check(bands.Contains("BoxShapes.Entrance(em, src.Access, p.KerbInset, p.EntranceLength, p.MarkingMargin, src.BuildingPos);") && bands.Contains("shape.Carriageways"),
                "repo: driving bands come from the same Entrance shape (window + carriageway mask): paint = driving");
        }
    }
}

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.12 (JR-T-018, gap G27, owner 17 Nov): SUV 602885 leaving building 77236 at (72.4, 427.1) waited correctly (the lane
    /// past its box held a stopped queue, first car 838449 ~3 m past the box end) but the trace said "NOTABLE held 25 s+ ...
    /// decision wait (gap) boxed no" at 20:22:42 although the exit is boxed (edge 1155147). Cause: the box test ran only when
    /// the gap scan was clear, and a standing vehicle within the present distance makes the gap scan busy. Fix (labels only):
    /// boxed from the band set also when held by the gap scan; a standing blocker is logged as "no room".
    /// </summary>
    internal static partial class Program
    {
        private static void G27TraceTests()
        {
            long f = ExitTrace.Pack(ExitTrace.Event.LongHold, ExitPaths.Path.Drive, ExitPaths.LaneKind.DriveLane, ExitGiveWay.Decision.WaitGap,
                ExitTrace.ReasonDecided | ExitTrace.ReasonBlockerStanding, true);
            ExitTrace.Unpack(f, out var e, out var path, out var kind, out var d, out int reasons, out bool boxed);
            Check(e == ExitTrace.Event.LongHold && path == ExitPaths.Path.Drive && d == ExitGiveWay.Decision.WaitGap && boxed
                  && (reasons & ExitTrace.ReasonBlockerStanding) != 0 && (reasons & ExitTrace.ReasonDecided) != 0, "G27 pack / unpack keeps the standing-blocker bit and boxed");
            string line = ExitTrace.Format(ExitTrace.PackEntity(602885, 1), ExitTrace.PackEntity(77236, 1), f, ((long)1155147 << 32) | 36843900u, ExitTrace.PackPoint(72.4f, 427.1f));
            Check(line.Contains("NOTABLE held") && line.Contains("decision wait (no room: a vehicle stands in its way out, at / past the box)") && line.Contains("boxed yes"),
                "G27 the owner's SUV line now reads no room + boxed yes: " + line);
            long g = ExitTrace.Pack(ExitTrace.Event.Wait, ExitPaths.Path.Drive, ExitPaths.LaneKind.DriveLane, ExitGiveWay.Decision.WaitGap, ExitTrace.ReasonDecided, false);
            string gl = ExitTrace.Format(1, 0, g, 0);
            Check(gl.Contains("decision wait (gap)") && gl.Contains("boxed no"), "G27 a moving blocker at an unboxed exit still reads wait (gap): " + gl);
            ExitTrace.Unpack(ExitTrace.Pack(ExitTrace.Event.Wait, ExitPaths.Path.Drive, ExitPaths.LaneKind.DriveLane, ExitGiveWay.Decision.Go, 0xFF, false), out _, out _, out _, out _, out int r2, out bool b2);
            Check(r2 == 0xFF && !b2, "G27 the old eight reason bits and boxed are unchanged by the new bit");
            Check(ExitTrace.Standing(0f) && !ExitTrace.Standing(2f), "G27 standing below 0.5 m/s");
            string src = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "JunctionRules", "Stage2", "JRExitGiveWay.cs"));
            Check(src.Contains("if (busy && m_BoxRule && hasJoin) boxed = ExitInBand(joinNav.m_Lane, joinNav.m_CurvePosition.x, ref carLanes, ref curves);")
                  && src.Contains("if (busy && m_BoxRule) boxed = ExitInBand(road, t, ref carLanes, ref curves);"),
                "G27 repo: both exit judgements know 'boxed' also when the gap scan holds the exit (label only, decision unchanged)");
            Check(src.Contains("ExitTrace.ReasonDecided | DriveReasons(drive), drive ? m_DriveBoxed : m_ExitBoxed)"), "G27 repo: the drive-lane long-hold line uses the drive judgement's own flags");
        }
    }
}
