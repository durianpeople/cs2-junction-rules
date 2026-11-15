using System;
using System.Collections.Generic;
using System.IO;
using JunctionRules.Core;
using G = JunctionRules.Core.BoxPaintGeometry;
using CW = JunctionRules.Core.CarriagewayOutline;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.3.5-probe (JR-T-003e): yellow box on the carriageway only (kerb to kerb, corner curves, stop lines, zebras),
    /// footway widths from composition pieces, side assignment, and the "paint onto" default / migration.
    /// </summary>
    internal static partial class Program
    {
        /// <summary>
        /// Synthetic junction node shaped like the game's slices: arm k along angle deg[k] with OUTER half-width H (back of
        /// footway), mouth at r. Each arm's two outer edges run straight from the mouth, then round the block corner on a
        /// fillet of radius R (outer edge), to the fillet middle, where the neighbour arm's edge ends too (two edges of a
        /// corner meet end to end, as the game's slices do). Between arms 180 deg apart the edges run straight to the foot
        /// point. Returns the arms (ArmInput, insets not set) and, per corner, the fillet centre (or null when straight).
        /// </summary>
        private static List<ArmInput> GameArms(double[] deg, double H, double r, double R, out List<V2?> filletCentres, int arcPts = 16)
        {
            int n = deg.Length;
            var u = new V2[n];
            for (int k = 0; k < n; k++) { double a = deg[k] * Math.PI / 180; u[k] = new V2(Math.Cos(a), Math.Sin(a)); }
            var left = new List<V2>[n];    // edge on the CCW side of arm k (faces arm k+1)
            var right = new List<V2>[n];   // edge on the CW side of arm k (faces arm k-1)
            filletCentres = new List<V2?>();
            for (int k = 0; k < n; k++)
            {
                int j = (k + 1) % n;
                V2 nk = u[k].Left, nj = u[j].Left;
                var lk = new List<V2> { u[k] * r + nk * H };
                var rj = new List<V2> { u[j] * r - nj * H };
                double cross = V2.Cross(u[k], u[j]);
                if (Math.Abs(cross) < 1e-9)
                {
                    // straight side (arms 180 deg apart): both edges run to the foot point of the centre
                    V2 foot = nk * H;
                    lk.Add(foot); rj.Add(foot);
                    filletCentres.Add(null);
                }
                else
                {
                    // fillet centre: crossing of the two edge lines moved R further out into the block
                    V2 p1 = nk * (H + R), p2 = nj * -(H + R);
                    double t = V2.Cross(p2 - p1, u[j]) / V2.Cross(u[k], u[j]);
                    V2 c = p1 + u[k] * t;
                    V2 t1 = c - nk * R, t2 = c + nj * R;
                    // corner point of the two outer lines, the arc middle points at it
                    V2 q1 = nk * H, q2 = nj * -H;
                    double tq = V2.Cross(q2 - q1, u[j]) / V2.Cross(u[k], u[j]);
                    V2 corner = q1 + u[k] * tq;
                    V2 mid = c + (corner - c).Normalized * R;
                    lk.Add(t1); rj.Add(t2);
                    double a1 = Math.Atan2((t1 - c).Z, (t1 - c).X), am = Math.Atan2((mid - c).Z, (mid - c).X), a2 = Math.Atan2((t2 - c).Z, (t2 - c).X);
                    double d1 = Wrap(am - a1), d2 = Wrap(am - a2);
                    for (int i = 1; i <= arcPts; i++)
                    {
                        double f = i / (double)arcPts;
                        lk.Add(c + new V2(Math.Cos(a1 + d1 * f), Math.Sin(a1 + d1 * f)) * R);
                        rj.Add(c + new V2(Math.Cos(a2 + d2 * f), Math.Sin(a2 + d2 * f)) * R);
                    }
                    filletCentres.Add(c);
                }
                left[k] = lk; right[j] = rj;
            }
            var arms = new List<ArmInput>();
            for (int k = 0; k < n; k++)
            {
                var a = new ArmInput();
                foreach (var p in left[k]) a.OuterA.Add(V3.FromXZ(p, 0));
                foreach (var p in right[k]) a.OuterB.Add(V3.FromXZ(p, 0));
                arms.Add(a);
            }
            return arms;
        }

        private static double Wrap(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI; return a; }

        private static void SetInsets(List<ArmInput> arms, double w) { foreach (var a in arms) { a.InsetA = w; a.InsetB = w; } }

        /// <summary>Smallest distance from p to any arm's outer edge polyline.</summary>
        private static double DistToOuter(List<ArmInput> arms, V2 p)
        {
            double best = double.MaxValue;
            foreach (var a in arms)
                foreach (var line in new[] { a.OuterA, a.OuterB })
                    for (int i = 0; i + 1 < line.Count; i++) best = Math.Min(best, G.DistanceToSegment(p, line[i].XZ, line[i + 1].XZ));
            return best;
        }

        private static void BoxCarriagewayGuardTests()
        {
            // ---- non-finite input: clean failure, no box
            var arms = GameArms(new double[] { 0, 90, 180, 270 }, 7, 16, 3, out _);
            foreach (var a in arms) { a.InsetA = 3; a.InsetB = 3; }
            var good = CW.Build(arms, new V2(0, 0), null, 0.05);
            Check(good.Outline != null, "guard: the clean 4-arm junction builds (baseline)");
            var bad = GameArms(new double[] { 0, 90, 180, 270 }, 7, 16, 3, out _);
            foreach (var a in bad) { a.InsetA = 3; a.InsetB = 3; }
            bad[2].OuterA[5] = new V3(double.NaN, 0, 1);
            var r = CW.Build(bad, new V2(0, 0), null, 0.05);
            Check(r.Outline == null && r.Failure != null && r.Failure.StartsWith("non-finite") && r.Kerbs.Count == 0, "guard: a NaN edge point fails cleanly with no outline and no kerbs (" + r.Failure + ")");
            foreach (var a in bad) { a.InsetA = 3; a.InsetB = 3; }
            bad[2].OuterA[5] = new V3(1, 0, 1);
            bad[1].InsetB = double.PositiveInfinity;
            r = CW.Build(bad, new V2(0, 0), null, 0.05);
            Check(r.Outline == null && r.Failure != null && r.Failure.StartsWith("non-finite"), "guard: an infinite inset fails cleanly (" + r.Failure + ")");
            r = CW.Build(arms, new V2(double.NaN, 0), null, 0.05);
            Check(r.Outline == null && r.Failure != null && r.Failure.StartsWith("non-finite"), "guard: a NaN centre fails cleanly");
            var nanCross = new List<CrosswalkStrip> { new CrosswalkStrip(new V2(double.NaN, 0), new V2(18, 4), 3) };
            r = CW.Build(arms, new V2(0, 0), nanCross, 0.05);
            Check(r.Outline != null && r.Notes.Exists(n => n.Contains("non-finite geometry ignored")), "guard: a NaN crossing is dropped with a note, the box still builds");
            Check(G.BuildPieces(null, new BoxPaintParams(), BoxPaintMode.Hatch).Failure == "no outline", "guard: no outline -> no box (BuildPieces refuses)");

            // ---- Douglas-Peucker stays within tolerance along a long curve (a 400-point, 120 m radius, 150 deg arc)
            var arc = new List<V3>();
            const double R = 120, span = 150 * Math.PI / 180;
            for (int i = 0; i < 400; i++) { double t = span * i / 399; arc.Add(new V3(R * Math.Cos(t), 0, R * Math.Sin(t))); }
            foreach (double tol in new[] { 0.01, 0.05, 0.2 })
            {
                var st = G.Straighten(arc, tol);
                double worst = 0;
                foreach (var p in arc)
                {
                    double best = double.MaxValue;
                    for (int i = 0; i + 1 < st.Count; i++) best = Math.Min(best, G.DistanceToSegment(p.XZ, st[i].XZ, st[i + 1].XZ));
                    worst = Math.Max(worst, best);
                }
                bool ends = st.Count >= 2 && st[0].X == arc[0].X && st[0].Z == arc[0].Z && st[st.Count - 1].X == arc[399].X && st[st.Count - 1].Z == arc[399].Z;
                Check(worst <= tol + 1e-9 && ends && st.Count < arc.Count, $"Douglas-Peucker: long arc kept within {tol} m (worst {worst:0.0000} m, {st.Count} of {arc.Count} points, ends kept)");
            }
        }

        private static void BoxCarriagewayTests()
        {
            // ---- footway widths from composition LANES (outer edges of road / bicycle / parking lanes), not pieces
            const int Road = CW.LaneFlagRoad, Ped = CW.LaneFlagPedestrian, Park = CW.LaneFlagParking, Bike = CW.LaneFlagRoad | 0x2000000;
            Check(CW.IsCarriagewayLane(Road) && CW.IsCarriagewayLane(Park) && CW.IsCarriagewayLane(Bike) && CW.IsCarriagewayLane(Park | 0x100000), "lane class: road, parking, bicycle (Road|BicyclesOnly) are carriageway");
            Check(!CW.IsCarriagewayLane(Ped) && !CW.IsCarriagewayLane(0x800) && !CW.IsCarriagewayLane(0) && !CW.IsCarriagewayLane(Ped | Road), "lane class: pedestrian, utility, none are not");
            // Real Small Road (W 16): road pieces -3..3 (car lanes at +-1.5, 3 m), "Sidewalk With Parking Piece 5" x 3..8 each
            // side holding the parking lane (x +-4, 2 m) AND the pavement (pedestrian lane at +-6.5). Visible kerb at +-5.
            var small = new List<CompositionLaneSpan>
            {
                new CompositionLaneSpan(-6.5, 2, CW.IsCarriagewayLane(Ped)), new CompositionLaneSpan(-4, 2, CW.IsCarriagewayLane(Park)),
                new CompositionLaneSpan(-1.5, 3, CW.IsCarriagewayLane(Road)), new CompositionLaneSpan(1.5, 3, CW.IsCarriagewayLane(Road)),
                new CompositionLaneSpan(4, 2, CW.IsCarriagewayLane(Park)), new CompositionLaneSpan(6.5, 2, CW.IsCarriagewayLane(Ped)),
            };
            Check(CW.FootwayWidths(small, 16, out double fl, out double fr, out string why) && Math.Abs(fl - 3) < 1e-9 && Math.Abs(fr - 3) < 1e-9, $"footways: Small Road (parking lane inside the 5 m sidewalk piece) gives 3 | 3 m, kerb at +-5 ({fl}, {fr}, {why})");
            // Bay variant ("Sidewalk With Parking Piece 5 - Bay", a piece that carries HasRoadLanes itself): the bay is a
            // parking lane flagged Road too; the pieces would say carriageway 8 m per side, the lanes still say kerb at +-5.
            var bay = new List<CompositionLaneSpan>
            {
                new CompositionLaneSpan(-6.5, 2, CW.IsCarriagewayLane(Ped)), new CompositionLaneSpan(-4, 2, CW.IsCarriagewayLane(Park | Road)),
                new CompositionLaneSpan(-1.5, 3, CW.IsCarriagewayLane(Road)), new CompositionLaneSpan(1.5, 3, CW.IsCarriagewayLane(Road)),
                new CompositionLaneSpan(4, 2, CW.IsCarriagewayLane(Park | Road)), new CompositionLaneSpan(6.5, 2, CW.IsCarriagewayLane(Ped)),
            };
            Check(CW.FootwayWidths(bay, 16, out fl, out fr, out why) && Math.Abs(fl - 3) < 1e-9 && Math.Abs(fr - 3) < 1e-9, $"footways: Small Road Bay variant gives 3 | 3 m ({fl}, {fr}, {why})");
            var asym = new List<CompositionLaneSpan>
            {
                new CompositionLaneSpan(-6, 2, false), new CompositionLaneSpan(-4, 2, CW.IsCarriagewayLane(Park)), new CompositionLaneSpan(-1.5, 3, CW.IsCarriagewayLane(Road)),
                new CompositionLaneSpan(1.5, 3, CW.IsCarriagewayLane(Road)), new CompositionLaneSpan(5, 4, false),
            };
            Check(CW.FootwayWidths(asym, 14, out fl, out fr, out why) && Math.Abs(fl - 2) < 1e-9 && Math.Abs(fr - 4) < 1e-9, $"footways: asymmetric 2 | parking 2 + road 6 | 4 m gives 2 and 4 m ({fl}, {fr})");
            var noFoot = new List<CompositionLaneSpan> { new CompositionLaneSpan(-3.5, 7, true), new CompositionLaneSpan(3.5, 7, true) };
            Check(CW.FootwayWidths(noFoot, 14, out fl, out fr, out _) && fl == 0 && fr == 0, "footways: lanes to the edge give a real (logged) 0 | 0");
            var none = new List<CompositionLaneSpan> { new CompositionLaneSpan(0, 3, CW.IsCarriagewayLane(Ped)) };
            Check(!CW.FootwayWidths(none, 14, out _, out _, out why) && why == "no road, bicycle or parking lanes", "footways: no carriageway lane is reported, not guessed (" + why + ")");
            Check(!CW.FootwayWidths(new List<CompositionLaneSpan>(), 14, out _, out _, out why) && why == "no composition lanes", "footways: empty lane list reported (" + why + ")");
            Check(!CW.FootwayWidths(small, 0, out _, out _, out _), "footways: zero width refused");
            var odd = new List<CompositionLaneSpan> { new CompositionLaneSpan(0, 20, true) };
            Check(!CW.FootwayWidths(odd, 14, out _, out _, out why) && why.StartsWith("odd carriageway span"), "footways: carriageway wider than the road refused (" + why + ")");
            var nanLane = new List<CompositionLaneSpan>(small) { new CompositionLaneSpan(double.NaN, 3, true) };
            Check(CW.FootwayWidths(nanLane, 16, out fl, out fr, out _) && Math.Abs(fl - 3) < 1e-9 && Math.Abs(fr - 3) < 1e-9, "footways: a NaN lane is ignored");

            BoxCarriagewayGuardTests();

            // ---- side assignment: the outer curve touching the edge's left kerb end gets the left footway
            CW.AssignSides(new V2(10, 7), new V2(10, -7), new V2(10, 7), 2, 4, out double ia, out double ib);
            Check(ia == 2 && ib == 4, "sides: outer A at the edge's left end takes the left footway");
            CW.AssignSides(new V2(10, 7), new V2(10, -7), new V2(10, -7), 2, 4, out ia, out ib);
            Check(ia == 4 && ib == 2, "sides: mirrored (end node / flipped slice) swaps them");

            // ---- 4-arm cross: 14 m road (3 m footways, 8 m carriageway), mouths 16 m out, outer fillet R 3 m
            const double H = 7, w = 3, h = H - w, r = 16, R = 3;
            var x = GameArms(new[] { 0.0, 90, 180, 270 }, H, r, R, out var centresX);
            SetInsets(x, w);
            var res = CW.Build(x, new V2(0, 0), null, 0.05);
            Check(res.Outline != null, "4-arm: carriageway outline merges (" + res.Failure + ")");
            if (res.Outline != null)
            {
                var xz = G.ToXZ(res.Outline);
                double Rk = R + w;   // kerb radius: concentric with the outer fillet, footway w wider
                // each rounded kerb corner ADDS the fillet area (corner square minus quarter circle) to the sharp plus shape
                double expected = (2 * h) * (2 * h) + 4 * (2 * h * (r - h)) + 4 * Rk * Rk * (1 - Math.PI / 4);
                double area = G.SignedArea(xz);
                Check(Math.Abs(area - expected) < 0.5, $"4-arm: area {area:0.00} m2 = kerb-to-kerb plus shape with {Rk} m kerb radii {expected:0.00} m2 (no pavement; chords within the 0.05 m straighten tolerance)");
                Check(G.IsSimple(xz) && area > 0, "4-arm: simple, counter-clockwise");
                double minFoot = double.MaxValue;
                foreach (var p in xz) minFoot = Math.Min(minFoot, DistToOuter(x, p));
                Check(minFoot > w - 0.02, $"4-arm: every outline point is at least the footway width in from the back of the footway (min {minFoot:0.000} m)");
                // follows the kerb corner curves: points on the kerb arc (radius R + w round the fillet centre) are on the outline
                double worst = 0;
                V2 c0 = centresX[0].Value;
                for (int i = 0; i <= 8; i++)
                {
                    double a = Math.PI + Math.PI / 2 * i / 8;   // corner between arm 0 and arm 90: arc faces the junction (towards -x, -z from c0)
                    V2 kp = c0 + new V2(Math.Cos(a), Math.Sin(a)) * Rk;
                    worst = Math.Max(worst, G.DistanceToBoundary(xz, kp));
                }
                Check(worst < 0.06, $"4-arm: outline follows the kerb corner curve (worst {worst:0.000} m off)");
                Check(!G.Contains(xz, c0 + new V2(-1, -1).Normalized * (R + w * 0.5)), "4-arm: the corner pavement is not painted");
                Check(!G.Contains(xz, new V2(r - 1, h + w * 0.5)), "4-arm: the footway along an arm is not painted");
                Check(G.Contains(xz, new V2(r - 0.1, h - 0.1)) && G.Contains(xz, new V2(0, 0)), "4-arm: carriageway up to the mouth and the centre are painted");
                Check(res.StopDistance != null && Array.TrueForAll(res.StopDistance, d => d == 0), "4-arm: no crossings, every arm starts at its mouth (stop line)");
                var pr = G.BuildPieces(res.Outline, new BoxPaintParams(), BoxPaintMode.Hatch);
                Check(pr.Ok && pr.Pieces.Count > 0, "4-arm: hatch pieces build on the carriageway outline (" + pr.Failure + ")");
            }

            // inset 0 (old 0.3.4 behaviour): the outline is the whole node incl. pavements, strictly larger
            SetInsets(x, 0);
            var whole = CW.Build(x, new V2(0, 0), null, 0.05);
            SetInsets(x, w);
            Check(whole.Outline != null && res.Outline != null && G.SignedArea(whole.Outline) > G.SignedArea(res.Outline) + 4 * 2 * w * (r - H), "4-arm: footway inset takes the pavements out (whole node is much larger)");

            // ---- 4-arm with a zebra on arm 0: strip across the arm 2 m inside the mouth, 3 m wide (stripes 0.5..3.5 m in)
            var zebra = new List<CrosswalkStrip> { new CrosswalkStrip(new V2(r - 2, -H), new V2(r - 2, H), 3) };
            var resZ = CW.Build(x, new V2(0, 0), zebra, 0.05);
            Check(resZ.Outline != null && Math.Abs(resZ.StopDistance[0] - 3.5) < 1e-9 && resZ.StopDistance[1] == 0 && resZ.StopDistance[2] == 0, $"zebra: arm 0 box starts 3.5 m in (far edge of the stripes), others at the mouth ({(resZ.StopDistance == null ? "-" : string.Join(", ", resZ.StopDistance))})");
            if (resZ.Outline != null)
            {
                var xz = G.ToXZ(resZ.Outline);
                double maxX = double.MinValue;
                foreach (var p in xz) maxX = Math.Max(maxX, p.X);
                Check(Math.Abs(maxX - (r - 3.5)) < 1e-6, $"zebra: box edge on arm 0 at x = {maxX:0.###} (= {r - 3.5}), not over the stripes");
                Check(!G.Contains(xz, new V2(r - 2, 0)) && G.Contains(xz, new V2(r - 3.6, 0)), "zebra: the zebra is clear, the carriageway just inside it is painted");
                Check(G.IsSimple(xz), "zebra: outline still simple");
                var other = G.ToXZ(res.Outline);
                double maxY = double.MinValue; foreach (var p in xz) maxY = Math.Max(maxY, p.Z);
                Check(Math.Abs(maxY - r) < 1e-6, "zebra: the other arms keep their mouths");
            }

            // unmarked / far crossings: a strip 30 m away belongs to no arm and is ignored with a note
            var far = new List<CrosswalkStrip> { new CrosswalkStrip(new V2(40, -4), new V2(40, 4), 3) };
            var resF = CW.Build(x, new V2(0, 0), far, 0.05);
            Check(resF.Outline != null && Array.TrueForAll(resF.StopDistance, d => d == 0) && resF.Notes.Exists(s => s.Contains("ignored")), "zebra: a crossing away from every mouth is ignored and noted");
            // a crossing deeper than the arm's kerbs: arm left untrimmed, noted
            var deep = new List<CrosswalkStrip> { new CrosswalkStrip(new V2(r - 11.5, -4), new V2(r - 11.5, 4), 2) };
            var resD = CW.Build(GameArms(new[] { 0.0, 90, 180, 270 }, H, r, R, out _).ConvertAll(a => { a.InsetA = w; a.InsetB = w; return a; }), new V2(0, 0), deep, 0.05);
            Check(resD.Outline != null && resD.StopDistance[0] == 0 && resD.Notes.Exists(s => s.Contains("past the kerb lines")), "zebra: a crossing past the kerb lines leaves the arm at its mouth, noted");

            // ---- 3-arm T (Moonflower x Anchor shape): through road 0/180, stem 90; zebra on the stem; footways 3 m
            var t = GameArms(new[] { 0.0, 90, 180 }, H, r, R, out var centresT);
            SetInsets(t, w);
            var stemZebra = new List<CrosswalkStrip> { new CrosswalkStrip(new V2(-H, r - 1.5), new V2(H, r - 1.5), 3) };
            var resT = CW.Build(t, new V2(0, 0), stemZebra, 0.05);
            Check(resT.Outline != null, "T: outline merges (" + resT.Failure + ")");
            if (resT.Outline != null)
            {
                var xz = G.ToXZ(resT.Outline);
                Check(G.IsSimple(xz), "T: simple");
                Check(Math.Abs(resT.StopDistance[1] - 3.0) < 1e-9, $"T: stem box starts 3.0 m in (zebra 1.5 m in, 3 m wide) ({resT.StopDistance[1]})");
                // straight side (south, z = -h): kerb line straight, pavement below it clean, no notch
                double minZ = double.MaxValue; int onKerb = 0;
                foreach (var p in xz) { minZ = Math.Min(minZ, p.Z); if (Math.Abs(p.Z + h) < 1e-6) onKerb++; }
                Check(Math.Abs(minZ + h) < 1e-6, $"T: the straight side stops at the kerb z = {-h} (min {minZ:0.###}), pavement clean");
                Check(onKerb == 2, $"T: the straight kerb side is one straight edge mouth to mouth ({onKerb} points on it): no notch");
                Check(!G.Contains(xz, new V2(-r + 0.5, -h - 0.2)) && G.Contains(xz, new V2(-r + 0.2, -h + 0.2)), "T: west mouth corner sits on the kerb, footway outside");
                double rk = R + w, worst = 0;
                V2 c0 = centresT[0].Value;
                for (int i = 0; i <= 8; i++)
                {
                    double a = Math.PI + Math.PI / 2 * i / 8;
                    worst = Math.Max(worst, G.DistanceToBoundary(xz, c0 + new V2(Math.Cos(a), Math.Sin(a)) * rk));
                }
                Check(worst < 0.06, $"T: follows the kerb corner curve by the stem (worst {worst:0.000} m)");
                double minFoot = double.MaxValue;
                foreach (var p in xz) minFoot = Math.Min(minFoot, DistToOuter(t, p));
                Check(minFoot > w - 0.02, $"T: nothing on a pavement (min {minFoot:0.000} m from the back of footway)");
                double area = G.SignedArea(xz);
                double expected = 2 * r * 2 * h /* through road */ + 2 * h * (r - h - 3.0) /* stem up to the zebra */ + 2 * rk * rk * (1 - Math.PI / 4);
                Check(Math.Abs(area - expected) < 0.5, $"T: area {area:0.00} = through carriageway + stem to the zebra + two kerb fillets {expected:0.00}");
            }

            // ---- skewed 4-arm (60 deg) with asymmetric footways per side still merges simple and keeps off the pavement
            var sk = GameArms(new[] { 0.0, 60, 180, 240 }, H, 18, 2, out _);
            foreach (var a in sk) { a.InsetA = 2; a.InsetB = 3.5; }
            var resS = CW.Build(sk, new V2(0, 0), null, 0.05);
            Check(resS.Outline != null && G.IsSimple(G.ToXZ(resS.Outline)), "skewed: outline merges and is simple (" + resS.Failure + ")");
            if (resS.Outline != null)
            {
                double minFoot = double.MaxValue;
                foreach (var p in G.ToXZ(resS.Outline)) minFoot = Math.Min(minFoot, DistToOuter(sk, p));
                Check(minFoot > 2 - 0.02, $"skewed: no point nearer the back of footway than the narrower footway ({minFoot:0.000} m)");
            }

            // ---- trim helper: cut where both kerbs cross the line; heights interpolated
            var arm = new ArmSlice(new List<V3> { new V3(10, 0, 4), new V3(0, 2, 4) }, new List<V3> { new V3(10, 0, -4), new V3(0, 2, -4) });
            Check(CW.TrimToStopLine(arm, new V2(0, 0), 2.5, out ArmSlice cut) && Math.Abs(cut.LeftKerb[0].X - 7.5) < 1e-9 && Math.Abs(cut.LeftKerb[0].Y - 0.5) < 1e-9 && Math.Abs(cut.RightKerb[0].X - 7.5) < 1e-9, "trim: both kerbs cut 2.5 m in, height interpolated");
            Check(!CW.TrimToStopLine(arm, new V2(0, 0), 11, out ArmSlice same) && ReferenceEquals(same, arm), "trim: a cut past the kerb ends is refused, arm unchanged");
        }

        private static void PaintMigrationTests()
        {
            Check(PaintSettingsMigration.Default == BoxPaintLayers.Roads && (int)PaintSettingsMigration.Default == 2, "paint onto default = Roads (mask 2)");
            Check(PaintSettingsMigration.OldProbeValue == BoxPaintLayers.RoadsAndOther && (int)PaintSettingsMigration.OldProbeValue == 34, "old probe value = Roads + Other (34)");
            Check(PaintSettingsMigration.Migrate(0, BoxPaintLayers.RoadsAndOther, out bool ch) == BoxPaintLayers.Roads && ch, "migration: saved 34 from the old probe step -> 2");
            Check(PaintSettingsMigration.Migrate(0, BoxPaintLayers.Roads, out ch) == BoxPaintLayers.Roads && !ch, "migration: Roads stays, no change");
            Check(PaintSettingsMigration.Migrate(0, BoxPaintLayers.AllButVehicles, out ch) == BoxPaintLayers.AllButVehicles && !ch, "migration: any other choice (39) is kept");
            Check(PaintSettingsMigration.Migrate(PaintSettingsMigration.Current, BoxPaintLayers.RoadsAndOther, out ch) == BoxPaintLayers.RoadsAndOther && !ch, "migration: runs once; Roads + Other chosen after it is kept");

            string root = RepoRoot();
            if (root == null) { Check(false, "repo root found"); return; }
            string src = Path.Combine(root, "JunctionRules");
            string settings = File.ReadAllText(Path.Combine(src, "Settings", "JunctionRulesSettings.cs"));
            string mod = File.ReadAllText(Path.Combine(src, "Mod.cs"));
            string sys = StripComments(File.ReadAllText(Path.Combine(src, "Paint", "YellowBoxPaintSystem.cs")));
            Check(settings.Contains("[SettingsUIHidden]") && settings.Contains("public int PaintSettingsVersion { get; set; }") && !settings.Contains("PaintSettingsVersion = 0"), "settings: hidden schema version (int default 0 detects an old file), never reset by SetDefaults");
            {
                int a = settings.IndexOf("public override void SetDefaults()"), z = a < 0 ? -1 : settings.IndexOf("public string MigrateAfterLoad()", a);
                Check(a >= 0 && z > a && !settings.Substring(a, z - a).Contains("PaintSettingsVersion"), "settings: SetDefaults leaves the schema version alone (Reset to defaults, then Roads + Other, survives the next load)");
            }
            Check(settings.Contains("PaintSettingsMigration.Migrate(PaintSettingsVersion, before, out bool changed)") && settings.Contains("PaintSettingsVersion = PaintSettingsMigration.Current;") && settings.Contains("ApplyAndSave();"), "settings: migration writes the new schema version and saves");
            int load = mod.IndexOf("AssetDatabase.global.LoadSettings(", StringComparison.Ordinal), mig = mod.IndexOf("Settings.MigrateAfterLoad()", StringComparison.Ordinal);
            Check(load >= 0 && mig > load, "Mod.OnLoad migrates right after loading the saved settings");
            // 0.3.5 outline source: edge composition + carriageway builder, no node-composition road-lane test any more
            Check(!sys.Contains("no road-lane pieces") && sys.Contains("comp.m_Edge") && sys.Contains("CarriagewayOutline.Build(arms, centre, crossings,"), "paint system: footways from the edge composition, outline from CarriagewayOutline.Build");
            Check(sys.Contains("NetCompositionLane") && sys.Contains("NetLaneData ld = em.GetComponentData<NetLaneData>(l.m_Lane)") && sys.Contains("CarriagewayOutline.IsCarriagewayLane(") && !sys.Contains("NetPieceFlags.HasRoadLanes"), "paint system: carriageway from lane extents (position +- NetLaneData width / 2), not piece spans");
            Check(sys.Contains("footway unknown") && sys.Contains("kerbNotes.Add(\"unknown\")") && !sys.Contains("kerbNotes.Add(\"0|0\")"), "paint system: unknown footway is logged, never a silent 0|0");
            Check(sys.Contains("!em.HasComponent<Game.Net.Road>(e)") && sys.Contains("non-road edge(s)"), "paint system: non-road edges (paths) are skipped as arms, with a note");
            Check(sys.Contains("PedestrianLaneFlags.Crosswalk") && sys.Contains("PedestrianLaneFlags.Unsafe"), "paint system: marked crossings = crosswalk lanes without Unsafe");
        }
    }
}
