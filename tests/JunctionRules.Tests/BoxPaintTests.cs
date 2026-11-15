using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using JunctionRules.Core;
using G = JunctionRules.Core.BoxPaintGeometry;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.3.0-probe (JR-T-003b): pure geometry of the yellow box paint test (outline merge, inset, hatch clipping),
    /// the test list file, and source checks that the paint code is draw-only and isolated.
    /// </summary>
    internal static partial class Program
    {
        // ---- synthetic junctions ----

        /// <summary>
        /// A junction of straight arms round (0,0): arm k points along angle deg[k], carriageway half-width h[k], mouth at
        /// distance r. Each kerb runs from the mouth inward for 'inward' metres past the centre line (long enough to cross
        /// the next arm's kerb = an overlapping slice, as the game's slices overlap) or stops short ('gap').
        /// </summary>
        private static List<ArmSlice> Arms(double[] deg, double[] h, double r, double inward, double y = 0, Func<V2, double> yOf = null, int pts = 6)
        {
            var arms = new List<ArmSlice>();
            for (int k = 0; k < deg.Length; k++)
            {
                double a = deg[k] * Math.PI / 180;
                var dir = new V2(Math.Cos(a), Math.Sin(a));
                V2 n = dir.Left;
                var left = new List<V3>();
                var right = new List<V3>();
                for (int i = 0; i <= pts; i++)
                {
                    double t = r - (r + inward) * i / pts;   // from the mouth (t = r) inward to t = -inward
                    V2 pl = dir * t + n * h[k], pr = dir * t - n * h[k];
                    left.Add(V3.FromXZ(pl, yOf != null ? yOf(pl) : y));
                    right.Add(V3.FromXZ(pr, yOf != null ? yOf(pr) : y));
                }
                arms.Add(new ArmSlice(left, right));
            }
            return arms;
        }

        private static double[] Same(int n, double v) { var a = new double[n]; for (int i = 0; i < n; i++) a[i] = v; return a; }

        private static void BoxOutlineTests()
        {
            // Plain X: two 8 m roads crossing at right angles, mouths 12 m out. Outline = plus shape (concave).
            var x = Arms(new[] { 0.0, 90, 180, 270 }, Same(4, 4), 12, 12);
            var ring = G.BuildOutline(x, new V2(0, 0), 0.05, out string why);
            Check(ring != null, "X: outline merges (" + why + ")");
            if (ring == null) return;
            double area = G.SignedArea(ring);
            double expected = 8 * 8 + 4 * (8 * (12 - 4));
            Check(Math.Abs(area - expected) < 1e-6, $"X: outline area {area:0.###} = plus shape {expected}");
            Check(ring.Count == 12, $"X: 12 corners (4 mouths x 2 + 4 kerb corners), got {ring.Count}");
            Check(G.IsSimple(G.ToXZ(ring)) && area > 0, "X: simple and counter-clockwise");

            // Arm order and left/right naming do not matter.
            var shuffled = new List<ArmSlice> { x[2], new ArmSlice(x[0].RightKerb, x[0].LeftKerb), x[3], x[1] };
            var ring2 = G.BuildOutline(shuffled, new V2(0, 0), 0.05, out _);
            Check(ring2 != null && Math.Abs(G.SignedArea(ring2) - expected) < 1e-6, "X: same outline for shuffled arms and swapped kerb names");

            // Gap: kerbs stop 1 m short of the corner -> joined straight across the corner (still simple, slightly smaller).
            var gap = Arms(new[] { 0.0, 90, 180, 270 }, Same(4, 4), 12, -5);
            var ringG = G.BuildOutline(gap, new V2(0, 0), 0.05, out why);
            Check(ringG != null && G.IsSimple(G.ToXZ(ringG)), "gap: kerbs that do not meet are joined straight (" + why + ")");
            if (ringG != null) Check(Math.Abs(G.SignedArea(ringG) - (expected + 4 * 0.5)) < 1e-6, $"gap: area {G.SignedArea(ringG):0.##} = plus shape + four half-metre-square corner chords (the straight join cuts across each inner corner)");

            // Skewed X (roads at 60 deg).
            var skew = Arms(new[] { 0.0, 60, 180, 240 }, Same(4, 4), 14, 20);
            var ringS = G.BuildOutline(skew, new V2(0, 0), 0.05, out why);
            Check(ringS != null, "skewed X: outline merges (" + why + ")");
            if (ringS != null)
            {
                // central rhombus (8 x 8 / sin 60) + 4 arms; mouth to corner along each kerb differs, so check by containment
                Check(G.Contains(G.ToXZ(ringS), new V2(0, 0)), "skewed X: contains the centre");
                Check(!G.Contains(G.ToXZ(ringS), new V2(10, -10)), "skewed X: corner outside the roads is outside");
                Check(G.Contains(G.ToXZ(ringS), new V2(13.5, 0)) && G.Contains(G.ToXZ(ringS), new V2(14 * Math.Cos(Math.PI / 3) - 0.1, 14 * Math.Sin(Math.PI / 3) - 0.1)), "skewed X: arm mouths inside");
            }

            // T at an angle (side road at 70 deg), Y, 5 arms, divided road (wide arms), each simple.
            var cases = new (string name, List<ArmSlice> arms)[]
            {
                ("T at an angle", Arms(new[] { 0.0, 70, 180 }, new[] { 4.0, 3, 4 }, 12, 12)),
                ("Y", Arms(new[] { 0.0, 150, 210 }, Same(3, 3.5), 15, 15)),
                ("5 arms", Arms(new[] { 0.0, 72, 144, 216, 288 }, Same(5, 4), 15, 15)),
                ("divided road", Arms(new[] { 0.0, 90, 180, 270 }, new[] { 9.5, 4, 9.5, 4 }, 16, 16)),
            };
            foreach (var c in cases)
            {
                var rc = G.BuildOutline(c.arms, new V2(0, 0), 0.05, out why);
                Check(rc != null && G.IsSimple(G.ToXZ(rc)) && G.SignedArea(rc) > 0, $"{c.name}: outline merges, simple, CCW ({why})");
                if (rc == null) continue;
                foreach (var a in c.arms)
                    Check(G.DistanceToBoundary(G.ToXZ(rc), a.LeftKerb[0].XZ) < 1e-6 && G.DistanceToBoundary(G.ToXZ(rc), a.RightKerb[0].XZ) < 1e-6, $"{c.name}: every mouth point is on the outline");
                Check(G.Contains(G.ToXZ(rc), new V2(0, 0)), $"{c.name}: contains the centre");
            }

            // Failures: one arm; a kerb with one point.
            Check(G.BuildOutline(Arms(new[] { 0.0 }, new[] { 4.0 }, 10, 10), new V2(0, 0), 0.05, out why) == null && why.Contains("fewer than 2"), "one arm: no outline, says why");
            var bad = Arms(new[] { 0.0, 90 }, Same(2, 4), 10, 10);
            bad[0].LeftKerb = new List<V3> { bad[0].LeftKerb[0] };
            Check(G.BuildOutline(bad, new V2(0, 0), 0.05, out why) == null && why.Contains("fewer than 2 points"), "kerb with one point: no outline, says why");
            var hull = G.HullOutline(x);
            Check(hull.Count >= 3 && G.SignedArea(hull) > expected, "hull fallback is convex and covers the plus shape");
        }

        private static void BoxInsetTests()
        {
            // Straight kerb along +X, carriageway on the +Z side: inset 2 m moves it to z = 2.
            var kerb = new List<V3> { new V3(0, 5, 0), new V3(5, 5, 0), new V3(10, 5, 0) };
            var o = G.OffsetToward(kerb, 2, new V2(0, 4));
            bool ok = true;
            foreach (var p in o) ok &= Math.Abs(p.Z - 2) < 1e-9 && Math.Abs(p.Y - 5) < 1e-12;
            Check(ok, "inset: straight kerb moves 2 m toward the carriageway, height kept");
            var o2 = G.OffsetToward(kerb, 2, new V2(0, -4));
            Check(Math.Abs(o2[1].Z + 2) < 1e-9, "inset: side follows the other kerb");
            // A kerb that turns 90 deg round a corner keeps its side: the mitred corner point sits 2 m from both legs.
            var bend = new List<V3> { new V3(10, 0, 0), new V3(0, 0, 0), new V3(0, 0, 10) };
            var ob = G.OffsetToward(bend, 2, new V2(10, -4));   // carriageway below the first leg (z < 0)
            Check(Math.Abs(ob[0].Z + 2) < 1e-9 && Math.Abs(ob[1].X + 2) < 1e-9 && Math.Abs(ob[1].Z + 2) < 1e-9 && Math.Abs(ob[2].X + 2) < 1e-9, $"inset: bend keeps its side and mitres the corner ({ob[1]})");
            Check(G.OffsetToward(kerb, 0, new V2(0, 4))[1].Z == 0, "inset 0 leaves the kerb");

            // Inset applied per arm before the merge shrinks the X by the footway on every side.
            var arms = Arms(new[] { 0.0, 90, 180, 270 }, Same(4, 6), 12, 12);
            var inset = new List<ArmSlice>();
            foreach (var a in arms) inset.Add(new ArmSlice(G.OffsetToward(a.LeftKerb, 2, a.RightKerb[0].XZ), G.OffsetToward(a.RightKerb, 2, a.LeftKerb[0].XZ)));
            var ring = G.BuildOutline(inset, new V2(0, 0), 0.05, out string why);
            double expected = 8 * 8 + 4 * (8 * (12 - 4));
            Check(ring != null && Math.Abs(G.SignedArea(ring) - expected) < 1e-6, $"inset arms (12 m road, 2 m footways) merge to the 8 m carriageway plus shape ({why})");

            // Straighten drops sampling points on a straight run, keeps a real bend.
            var s = G.Straighten(new List<V3> { new V3(0, 0, 0), new V3(1, 0, 0.001), new V3(2, 0, 0), new V3(2, 0, 2) }, 0.05);
            Check(s.Count == 3, "straighten: collinear point dropped, bend kept");
        }

        /// <summary>Generic checks for one built box; returns false when a check failed.</summary>
        private static void CheckPieces(string name, BoxPaintResult res, BoxPaintParams prm, BoxPaintMode mode)
        {
            Check(res.Ok, $"{name}: built ({res.Failure})");
            if (!res.Ok) return;
            var xz = G.ToXZ(res.Outline);
            bool inside = true, convex = true, ccw = true, fans = true;
            double hatchArea = 0, borderArea = 0;
            foreach (var p in res.Pieces)
            {
                var r = G.ToXZ(p.Ring);
                foreach (var v in r) inside &= G.Contains(xz, v, 1e-6);
                double a = G.SignedArea(r);
                ccw &= a > 0;
                if (p.Kind != PaintPieceKind.Fill)
                {
                    for (int i = 0; i < r.Count; i++) convex &= V2.Cross(r[(i + 1) % r.Count] - r[i], r[(i + 2) % r.Count] - r[(i + 1) % r.Count]) >= -1e-9;
                }
                fans &= p.Triangles.Count % 3 == 0 && Math.Abs(G.TrianglesArea(r, p.Triangles) - a) < 1e-6 * Math.Max(1, a);
                if (p.Kind == PaintPieceKind.Hatch) hatchArea += a;
                if (p.Kind == PaintPieceKind.Border) borderArea += a;
            }
            Check(inside, $"{name}: every piece lies inside the outline");
            Check(ccw, $"{name}: every piece counter-clockwise with area");
            Check(convex, $"{name}: border and hatch pieces convex");
            Check(fans, $"{name}: piece triangles cover each piece exactly");

            if (mode == BoxPaintMode.Hatch)
            {
                // Independent check: strip-by-strip area of (outline AND strip) via Sutherland-Hodgman on the outline
                // (exact in area even where the result has several parts).
                double expectedHatch = 0;
                foreach (double sign in new[] { 1.0, -1.0 })
                {
                    double ang = sign * prm.AngleDeg * Math.PI / 180;
                    V2 d = res.Rect.U * Math.Cos(ang) + res.Rect.V * Math.Sin(ang);
                    V2 n = d.Left;
                    double mn = double.MaxValue, mx = double.MinValue;
                    foreach (var p in xz) { double pn = V2.Dot(p - res.Rect.Origin, n); mn = Math.Min(mn, pn); mx = Math.Max(mx, pn); }
                    for (int k = (int)Math.Ceiling((mn - prm.LineWidth / 2) / res.SpacingUsed); k <= (int)Math.Floor((mx + prm.LineWidth / 2) / res.SpacingUsed); k++)
                        expectedHatch += G.SignedArea(G.ClipConvex(xz, G.StripQuad(xz, res.Rect.Origin, d, k * res.SpacingUsed, prm.LineWidth)));
                }
                Check(Math.Abs(hatchArea - expectedHatch) < 1e-6 * Math.Max(1, expectedHatch), $"{name}: hatch area {hatchArea:0.####} = outline-clipped strips {expectedHatch:0.####}");
                Check(hatchArea > 0, $"{name}: has diagonals");
            }
            else Check(hatchArea == 0, $"{name}: no diagonals in mode {mode}");

            if (mode != BoxPaintMode.PlainFill)
            {
                bool near = true;
                foreach (var p in res.Pieces)
                    if (p.Kind == PaintPieceKind.Border) foreach (var v in p.Ring) near &= G.DistanceToBoundary(xz, v.XZ) <= prm.BorderWidth * prm.MitreLimit + 1e-6;
                Check(near && borderArea > 0, $"{name}: border strip hugs the outline");
            }
        }

        private static void BoxHatchTests()
        {
            var prm = new BoxPaintParams();
            // Square 20 x 20: exact border area, lines at right angles, TSM spacing 2.5 m (shortest side >= 9 m).
            var sq = new List<V3> { new V3(0, 0, 0), new V3(20, 0, 0), new V3(20, 0, 20), new V3(0, 0, 20) };
            var res = G.BuildPieces(sq, prm, BoxPaintMode.Hatch);
            CheckPieces("square", res, prm, BoxPaintMode.Hatch);
            double border = 0;
            foreach (var p in res.Pieces) if (p.Kind == PaintPieceKind.Border) border += G.SignedArea(p.Ring);
            Check(Math.Abs(border - (4 * 20 * 0.2 - 4 * 0.2 * 0.2)) < 1e-6, $"square: border area {border:0.######} = 4 x 20 m x 0.2 m less the corner overlaps");
            Check(res.SpacingUsed == 2.5, "TSM 8.4.1: 2.5 m spacing when the shortest side is 9 m or more");
            var small = G.BuildPieces(new List<V3> { new V3(0, 0, 0), new V3(8, 0, 0), new V3(8, 0, 12), new V3(0, 0, 12) }, prm, BoxPaintMode.Hatch);
            Check(small.SpacingUsed == 2.0, "TSM 8.4.1: 2 m spacing when the shortest side is under 9 m");
            var fixedSp = new BoxPaintParams { Spacing = 3 };
            Check(G.BuildPieces(sq, fixedSp, BoxPaintMode.Hatch).SpacingUsed == 3, "spacing parameter overrides the TSM rule");

            // Coverage: a point on a diagonal is painted; a point midway between diagonals of both families is not.
            V2 o = res.Rect.Origin, u = res.Rect.U, v = res.Rect.V;
            V2 d1 = (u + v).Normalized, d2 = (u - v).Normalized;
            V2 onLine = o + d1 * 14.0;    // the main diagonal through the corner (k = 0)
            Check(Covered(res, onLine), "square: a point on the main diagonal is painted");
            V2 n1 = d1.Left, n2 = d2.Left;   // unit normals of the two families
            V2 between = new V2(double.NaN, double.NaN);
            for (double bx = 2; bx <= 18 && double.IsNaN(between.X); bx += 0.05)
                for (double bz = 2; bz <= 18; bz += 0.05)
                {
                    var q = new V2(bx, bz);
                    double ra = ((V2.Dot(q - o, n1) % 2.5) + 2.5) % 2.5, rb = ((V2.Dot(q - o, n2) % 2.5) + 2.5) % 2.5;
                    if (Math.Abs(ra - 1.25) < 0.05 && Math.Abs(rb - 1.25) < 0.05) { between = q; break; }
                }
            Check(!double.IsNaN(between.X) && !Covered(res, between), $"square: half-way between diagonals of both families is not painted ({between})");
            // The two families are at right angles (8.2.4) for angle 45.
            Check(Math.Abs(V2.Dot(d1, d2)) < 1e-12, "angle 45: the two families cross at 90 deg");

            // Arbitrary outlines from the synthetic junctions, every mode.
            var shapes = new (string name, List<ArmSlice> arms)[]
            {
                ("X", Arms(new[] { 0.0, 90, 180, 270 }, Same(4, 4), 12, 12)),
                ("skewed X", Arms(new[] { 0.0, 60, 180, 240 }, Same(4, 4), 14, 20)),
                ("T at an angle", Arms(new[] { 0.0, 70, 180 }, new[] { 4.0, 3, 4 }, 12, 12)),
                ("Y", Arms(new[] { 0.0, 150, 210 }, Same(3, 3.5), 15, 15)),
                ("5 arms", Arms(new[] { 0.0, 72, 144, 216, 288 }, Same(5, 4), 15, 15)),
                ("divided road", Arms(new[] { 0.0, 90, 180, 270 }, new[] { 9.5, 4, 9.5, 4 }, 16, 16)),
            };
            foreach (var s in shapes)
            {
                var ring = G.BuildOutline(s.arms, new V2(0, 0), 0.05, out _);
                if (ring == null) { Check(false, s.name + ": outline"); continue; }
                foreach (BoxPaintMode m in new[] { BoxPaintMode.Hatch, BoxPaintMode.OutlineOnly, BoxPaintMode.PlainFill })
                    CheckPieces($"{s.name} [{m}]", G.BuildPieces(new List<V3>(ring), prm, m), prm, m);
            }

            // Rebuild cost of one box (5 arms, hatch): pure geometry, managed, this machine.
            {
                var five = Arms(new[] { 0.0, 72, 144, 216, 288 }, Same(5, 4), 15, 15, 0, null, 8);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int runs = 20, pieces = 0;
                for (int k = 0; k < runs; k++)
                {
                    var ring5 = G.BuildOutline(five, new V2(0, 0), 0.05, out _);
                    var r5 = G.BuildPieces(ring5, prm, BoxPaintMode.Hatch);
                    pieces = r5.TriangleCount;
                }
                double ms = sw.Elapsed.TotalMilliseconds / runs;
                Console.WriteLine($"box rebuild bench: 5-arm hatch box, {pieces} triangles, {ms:0.00} ms per build (managed)");
                Check(ms < 100, $"one box rebuild well under 100 ms ({ms:0.00} ms)");
            }

            // Concave L (not from arms): hatch split into several pieces per line where the notch cuts it.
            var L = new List<V3> { new V3(0, 0, 0), new V3(20, 0, 0), new V3(20, 0, 6), new V3(6, 0, 6), new V3(6, 0, 20), new V3(0, 0, 20) };
            var rl = G.BuildPieces(L, prm, BoxPaintMode.Hatch);
            CheckPieces("concave L", rl, prm, BoxPaintMode.Hatch);
            Check(!Covered(rl, new V2(15, 15)), "concave L: the notch stays clean");
            // Other angles and widths stay inside.
            var p2 = new BoxPaintParams { AngleDeg = 30, LineWidth = 0.3, BorderWidth = 0.5, Spacing = 1.7 };
            CheckPieces("concave L, 30 deg, wide lines", G.BuildPieces(L, p2, BoxPaintMode.Hatch), p2, BoxPaintMode.Hatch);

            // Outline only: the middle stays clean.
            var ro = G.BuildPieces(sq, prm, BoxPaintMode.OutlineOnly);
            Check(!Covered(ro, new V2(10, 10)) && Covered(ro, new V2(0.1, 10)), "outline only: border painted, middle clean");
            // Plain fill: one piece = the outline.
            var rf = G.BuildPieces(sq, prm, BoxPaintMode.PlainFill);
            Check(rf.Pieces.Count == 1 && Math.Abs(G.SignedArea(rf.Pieces[0].Ring) - 400) < 1e-9 && rf.Pieces[0].Triangles.Count == 6, "plain fill: one piece, the whole outline, two triangles");

            // Line cap.
            var capped = new BoxPaintParams { Spacing = 0.5, MaxLinesPerFamily = 5 };
            var rc = G.BuildPieces(sq, capped, BoxPaintMode.Hatch);
            Check(rc.Ok && rc.Warnings.Count == 1 && rc.Warnings[0].Contains("capped"), "line cap: extra diagonals dropped with a warning");

            // Clockwise input is turned round; a degenerate outline fails with a reason.
            var cw = new List<V3>(sq); cw.Reverse();
            Check(G.BuildPieces(cw, prm, BoxPaintMode.Hatch).Ok, "clockwise outline accepted (reversed)");
            var deg = G.BuildPieces(new List<V3> { new V3(0, 0, 0), new V3(1, 0, 0), new V3(2, 0, 0) }, prm, BoxPaintMode.Hatch);
            Check(!deg.Ok && deg.Failure != null, "degenerate outline: no box, failure named");
        }

        private static bool Covered(BoxPaintResult res, V2 p)
        {
            foreach (var piece in res.Pieces) if (G.Contains(G.ToXZ(piece.Ring), p, 0)) return true;
            return false;
        }

        private static void BoxRectHeightTests()
        {
            // Smallest rectangle of a rotated 30 x 10 rectangle: recovered, U = the long side.
            double a = 25 * Math.PI / 180;
            var u = new V2(Math.Cos(a), Math.Sin(a));
            var v = u.Left;
            var pts = new List<V2> { new V2(5, 5), new V2(5, 5) + u * 30, new V2(5, 5) + u * 30 + v * 10, new V2(5, 5) + v * 10 };
            var r = G.MinAreaRect(pts);
            Check(Math.Abs(r.LenU - 30) < 1e-6 && Math.Abs(r.LenV - 10) < 1e-6, $"min rect: 30 x 10 recovered ({r.LenU:0.###} x {r.LenV:0.###})");
            Check(Math.Abs(Math.Abs(V2.Dot(r.U, u)) - 1) < 1e-9, "min rect: U along the long side");
            var tall = G.MinAreaRect(new List<V2> { new V2(0, 0), new V2(4, 0), new V2(4, 12), new V2(0, 12) });
            Check(Math.Abs(tall.LenU - 12) < 1e-9 && Math.Abs(tall.LenV - 4) < 1e-9 && Math.Abs(V2.Cross(tall.U, tall.V) - 1) < 1e-12, "min rect: long side first, V = Left(U)");
            bool corners = true;
            foreach (var p in new List<V2> { new V2(0, 0), new V2(4, 0), new V2(4, 12), new V2(0, 12) })
            {
                double pu = V2.Dot(p - tall.Origin, tall.U), pv = V2.Dot(p - tall.Origin, tall.V);
                corners &= pu >= -1e-9 && pu <= tall.LenU + 1e-9 && pv >= -1e-9 && pv <= tall.LenV + 1e-9;
            }
            Check(corners, "min rect: origin is the (min U, min V) corner");

            // Elevated junction: every painted vertex at deck height.
            var deck = Arms(new[] { 0.0, 90, 180, 270 }, Same(4, 4), 12, 12, 23.5);
            var ring = G.BuildOutline(deck, new V2(0, 0), 0.05, out _);
            var res = G.BuildPieces(ring, new BoxPaintParams(), BoxPaintMode.Hatch);
            bool level = res.Ok;
            foreach (var p in res.Pieces) foreach (var q in p.Ring) level &= Math.Abs(q.Y - 23.5) < 1e-9;
            Check(level, "elevated: all paint at the deck height (23.5 m)");
            // Sloped junction (plane y = 0.05 x + 0.02 z + 7): heights interpolated exactly.
            Func<V2, double> plane = p => 0.05 * p.X + 0.02 * p.Z + 7;
            var slope = Arms(new[] { 0.0, 70, 180 }, new[] { 4.0, 3, 4 }, 12, 12, 0, plane);
            var rs = G.BuildPieces(G.BuildOutline(slope, new V2(0, 0), 0.05, out _), new BoxPaintParams(), BoxPaintMode.Hatch);
            bool onPlane = rs.Ok;
            foreach (var p in rs.Pieces) foreach (var q in p.Ring) onPlane &= Math.Abs(q.Y - plane(q.XZ)) < 1e-6;
            Check(onPlane, "sloped: paint follows the junction surface");

            // Shader neighbours: CCW ring handed over clockwise (prev = i+1, next = i-1), swap flips it.
            G.Neighbours(4, 0, false, out int pr, out int nx);
            Check(pr == 1 && nx == 3, "neighbours: prev = i+1, next = i-1 (game's clockwise view of a CCW area)");
            G.Neighbours(4, 0, true, out pr, out nx);
            Check(pr == 3 && nx == 1, "neighbours: swap flips");
        }

        private static void PaintConfigTests()
        {
            string json = @"{
  // test list
  ""junctions"": [
    { ""x"": 1092.7, ""z"": -81.5, ""name"": ""Valley x Carlisle"" },
    { ""x"": 10, ""y"": 23.5, ""z"": 20 },
    { ""x"": ""bad"" }
  ],
  ""paint"": { ""borderWidth"": 0.25, ""lineWidth"": 9, ""angle"": 40, ""color"": [1, 0.9, 0], ""swapPrevNext"": true, ""volumeAbove"": 0.5, ""edgeFade"": [0.5, 0.1] },
  ""future"": 1
}";
            var c = PaintTestConfig.Parse(json);
            Check(c.Junctions.Count == 2 && c.Junctions[0].Name == "Valley x Carlisle" && !c.Junctions[0].HasY && c.Junctions[1].HasY && c.Junctions[1].Y == 23.5, "config: junctions read, y optional");
            Check(c.Problems.Exists(p => p.Contains("junction 3")), "config: bad junction reported, others kept");
            Check(c.Geometry.BorderWidth == 0.25 && c.Geometry.AngleDeg == 40 && c.Geometry.LineWidth == 2.0 && c.Problems.Exists(p => p.Contains("LineWidth")), "config: values read; out-of-range clamped and reported");
            Check(c.Render.Color.Length == 4 && c.Render.Color[3] == 1 && c.Render.SwapPrevNext && c.Render.VolumeAbove == 0.5 && c.Render.EdgeFadeX == 0.5, "config: render values read, rgb gets alpha 1");
            Check(c.Geometry.Spacing == 0 && c.Render.VolumeBelow == 1.0, "config: missing values keep defaults");

            var d = PaintTestConfig.Parse("");
            Check(d.Junctions.Count == 0 && d.Problems.Count == 0 && d.Geometry.BorderWidth == 0.2 && d.Geometry.LineWidth == 0.15, "config: empty file = empty list, TSM defaults (200 mm border, 150 mm lines)");
            var e = PaintTestConfig.Parse("{ \"junctions\": [ ");
            Check(e.Junctions.Count == 0 && e.Problems.Count == 1 && e.Problems[0].StartsWith("not valid JSON"), "config: broken JSON reported, nothing painted");
            var nan = PaintTestConfig.Parse("{\"paint\": {\"spacing\": \"x\"}}");
            Check(nan.Geometry.Spacing == 0 && nan.Problems.Count == 1, "config: wrong type falls back to the default and is reported");

            // Round trip.
            var back = PaintTestConfig.Parse(c.Format());
            Check(back.Junctions.Count == 2 && back.Junctions[1].Y == 23.5 && back.Junctions[0].Name == "Valley x Carlisle" && back.Geometry.AngleDeg == 40 && back.Render.SwapPrevNext && back.Problems.Count == 0, "config: Format round-trips");
            Check(PaintTestConfig.Parse(new PaintTestConfig().Format()).Problems.Count == 0, "config: defaults format cleanly");

            // 0.3.1 diag switches: defaults = normal draw; values read, bad ones reported; kept on write-back.
            var dd = new PaintTestConfig().Diag;
            Check(dd.DrawPath == "decal" && dd.DrawDecal && !dd.DrawOverlay && dd.MaterialFrom == "" && dd.DecalLayerMask == -1 && dd.ColorBuffer[2] == 0 && dd.LodFactor == 0
                && !dd.FlipWinding && dd.OffsetDir == null && dd.TestSquare == 0 && dd.SetBuffersEachFrame && dd.RendererPriority == 0 && dd.RenderQueue == -1 && dd.Cameras == "nonPreview" && dd.FormatMembers().Count == 0,
                "diag: defaults are the normal draw and write nothing back");
            Check(!new PaintTestConfig().Format().Contains("diag"), "diag: default list file has no diag object");
            var dg = PaintTestConfig.Parse(@"{ ""diag"": { ""drawPath"": ""both"", ""materialFrom"": ""Concrete Surface 01"", ""decalLayerMask"": 255, ""colorBuffer"": [0, 0, 1], ""lodFactor"": 1e6,
                ""flipWinding"": true, ""offsetDir"": [1, 2, 0, 1], ""testSquare"": 20, ""setBuffersEachFrame"": false, ""rendererPriority"": 5, ""renderQueue"": 2000, ""cameras"": ""game"", ""later"": 1 },
                ""paint"": { ""volumeBelow"": 50, ""volumeAbove"": 50 } }");
            var di = dg.Diag;
            Check(dg.Problems.Count == 0 && di.DrawDecal && di.DrawOverlay && di.MaterialFrom == "Concrete Surface 01" && di.DecalLayerMask == 255 && di.ColorBuffer[2] == 1 && di.LodFactor == 1e6
                && di.FlipWinding && di.OffsetDir[1] == 2 && di.TestSquare == 20 && !di.SetBuffersEachFrame && di.RendererPriority == 5 && di.RenderQueue == 2000 && di.Cameras == "game",
                "diag: every switch read (" + string.Join("; ", dg.Problems) + ")");
            Check(dg.Render.VolumeBelow == 50 && dg.Render.VolumeAbove == 50, "diag: huge volume allowed (up to 200 m)");
            string ds = di.Describe();
            Check(ds.Contains("drawPath both") && ds.Contains("decalLayerMask 255") && ds.Contains("testSquare 20 m") && ds.Contains("cameras game"), "diag: effective line " + ds);
            var dback = PaintTestConfig.Parse(dg.Format()).Diag;
            Check(dback.Describe() == ds && dback.GpuKey == di.GpuKey, "diag: switches survive the list write-back");
            var dbad = PaintTestConfig.Parse(@"{ ""diag"": { ""drawPath"": ""laser"", ""decalLayerMask"": 300, ""colorBuffer"": [1], ""flipWinding"": 1, ""offsetDir"": [1, 2], ""testSquare"": -3, ""renderQueue"": 1.5 } }");
            Check(dbad.Diag.DrawPath == "decal" && dbad.Diag.DecalLayerMask == -1 && dbad.Diag.ColorBuffer.Length == 3 && !dbad.Diag.FlipWinding && dbad.Diag.OffsetDir == null && dbad.Diag.TestSquare == 0 && dbad.Diag.RenderQueue == -1
                && dbad.Problems.Count >= 7, "diag: bad values fall back to defaults and are reported (" + dbad.Problems.Count + ")");
            Check(PaintTestConfig.Parse(@"{ ""diag"": 3 }").Problems.Exists(p => p.Contains("diag is not an object")), "diag: non-object reported");
            Check(PaintTestConfig.Parse(@"{ ""paint"": { ""volumeAbove"": 500 } }").Render.VolumeAbove == 200, "diag: volume clamp at 200 m");
            var sq = PaintDiagParams.Square(new V3(10, 5, -4), 20);
            Check(sq.Ring.Count == 4 && sq.Triangles.Count == 6 && sq.Ring.TrueForAll(q => q.Y == 5)
                && PaintDiagParams.ShaderCross(sq.Ring[0], sq.Ring[1], sq.Ring[2]) > 0 && PaintDiagParams.ShaderCross(sq.Ring[0], sq.Ring[2], sq.Ring[3]) > 0
                && Math.Abs(G.TrianglesArea(G.ToXZ(sq.Ring), sq.Triangles) - 400) < 1e-9, "diag: test square is 20 x 20 m at node height with shader-positive winding");
            // The shader's winding test agrees with the geometry's CCW rings (pieces from BuildPieces).
            var pr = G.BuildPieces(new List<V3> { new V3(0, 0, 0), new V3(20, 0, 0), new V3(20, 0, 12), new V3(0, 0, 12) }, new BoxPaintParams(), BoxPaintMode.Hatch);
            bool allPos = true;
            foreach (var pc in pr.Pieces) for (int t = 0; t + 2 < pc.Triangles.Count; t += 3) if (PaintDiagParams.ShaderCross(pc.Ring[pc.Triangles[t]], pc.Ring[pc.Triangles[t + 1]], pc.Ring[pc.Triangles[t + 2]]) <= 0) allPos = false;
            Check(pr.Ok && allPos, "diag: every box triangle has the winding the area shader keeps");

            // Node picking.
            var nodes = new List<V3> { new V3(0, 0, 0), new V3(0.3, 12, 0.2), new V3(4, 0, 0), new V3(50, 0, 50) };
            var j = new PaintTestJunction { X = 0.1, Z = 0.1 };
            int i = PaintTestConfig.PickNode(j, nodes, 6, out int stacked);
            Check(i == 0 && stacked == 1, "pick: nearest in plan; stacked deck node counted");
            j.Y = 11;
            Check(PaintTestConfig.PickNode(j, nodes, 6, out _) == 1, "pick: y chooses the deck node");
            Check(PaintTestConfig.PickNode(new PaintTestJunction { X = 30, Z = 30 }, nodes, 6, out _) == -1, "pick: nothing within the radius");
            // Camera ray straight down from above (0,100,0): deck (y 12) is nearer than the ground node.
            Check(PaintTestConfig.PickAlongRay(new V3(0, 100, 0), new V3(0, -1, 0), nodes, 8) == 1, "camera pick: deck wins over the ground below");
            Check(PaintTestConfig.PickAlongRay(new V3(0, 100, 0), new V3(0, 1, 0), nodes, 8) == -1, "camera pick: nothing behind the camera");

            // Fail-once latch and cost meter.
            var latch = new PaintFailLatch();
            Check(latch.Trip("a") && !latch.Trip("b") && latch.Failed && latch.What == "a" && latch.Suppressed == 1, "latch: first failure logged once, later ones counted");
            var m = new PaintCostMeter();
            m.AddDraw(10); m.AddDraw(30); m.AddUpdate(5); m.AddRebuild(2000);
            string line = m.Line(1e6, 30, 2, 400);
            Check(line.Contains("boxes 2") && line.Contains("triangles 400") && line.Contains("draw 20.0 us/frame avg, 30.0 max (2 frames)") && line.Contains("rebuilds 1, 2.000 ms avg"), "cost line: " + line);
        }
    }
}
