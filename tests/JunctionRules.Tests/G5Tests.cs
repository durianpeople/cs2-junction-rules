using System;
using System.Collections.Generic;
using JunctionRules.Core;
using G = JunctionRules.Core.BoxPaintGeometry;
using CW = JunctionRules.Core.CarriagewayOutline;

namespace JunctionRules.Tests
{
    /// <summary>
    /// JR-T-013c (gap G5): Crescent Street x Eastgate Lane (node 551713:1 in the 16 Nov session; Carto export 14 Nov puts it
    /// at about (-518, 546)): a 16 m one-way street (Crescent, westbound, 2 lanes + kerbside parking) crossed by an 8 m
    /// Alley (Eastgate Lane, two-way). The 0.4.6 solver refused it: "corner arm 3 - arm 1 still crosses the kerb or a
    /// marking margin: no allowed shift clears it".
    /// </summary>
    internal static partial class Program
    {
        internal sealed class G5Arm
        {
            public double Deg, Half;   // direction from the centre; carriageway half width (kerb to kerb / 2)
            public bool Zebra, Yielding;
            public string Name;
        }

        /// <summary>
        /// Kerb lines of a junction whose arms have their own widths: arm k along Deg with carriageway half width Half;
        /// neighbouring kerbs joined by a fillet of radius R in the block (16 arc points per half), each arm's slice
        /// starting 'zebraRoom' beyond the farther of its two fillet tangent points (the mouth = index 0). Insets 0
        /// (the outer curves ARE the kerbs). Zebras (2.5 m painted, centre 1.5 m in from the mouth) where asked.
        /// </summary>
        internal static List<ArmInput> G5Kerbs(IList<G5Arm> spec, double R, double zebraRoom, out List<CrosswalkStrip> zebras, out double[] mouthR)
        {
            int n = spec.Count;
            var u = new V2[n];
            for (int k = 0; k < n; k++) { double a = spec[k].Deg * Math.PI / 180; u[k] = new V2(Math.Cos(a), Math.Sin(a)); }
            var leftArc = new List<V2>[n];
            var rightArc = new List<V2>[n];
            var leftT = new double[n];
            var rightT = new double[n];
            for (int k = 0; k < n; k++)
            {
                int j = (k + 1) % n;
                V2 nk = u[k].Left, nj = u[j].Left;
                double hk = spec[k].Half, hj = spec[j].Half;
                V2 p1 = nk * (hk + R), p2 = nj * -(hj + R);
                double t = V2.Cross(p2 - p1, u[j]) / V2.Cross(u[k], u[j]);
                V2 c = p1 + u[k] * t;
                V2 t1 = c - nk * R, t2 = c + nj * R;
                V2 q1 = nk * hk, q2 = nj * -hj;
                double tq = V2.Cross(q2 - q1, u[j]) / V2.Cross(u[k], u[j]);
                V2 corner = q1 + u[k] * tq;
                V2 mid = c + (corner - c).Normalized * R;
                double a1 = Math.Atan2((t1 - c).Z, (t1 - c).X), am = Math.Atan2((mid - c).Z, (mid - c).X), a2 = Math.Atan2((t2 - c).Z, (t2 - c).X);
                double d1 = Wrap(am - a1), d2 = Wrap(am - a2);
                var la = new List<V2> { t1 };
                var ra = new List<V2> { t2 };
                for (int i = 1; i <= 16; i++)
                {
                    double f = i / 16.0;
                    la.Add(c + new V2(Math.Cos(a1 + d1 * f), Math.Sin(a1 + d1 * f)) * R);
                    ra.Add(c + new V2(Math.Cos(a2 + d2 * f), Math.Sin(a2 + d2 * f)) * R);
                }
                leftArc[k] = la; rightArc[j] = ra;
                leftT[k] = V2.Dot(t1, u[k]);
                rightT[j] = V2.Dot(t2, u[j]);
            }
            var arms = new List<ArmInput>();
            zebras = new List<CrosswalkStrip>();
            mouthR = new double[n];
            for (int k = 0; k < n; k++)
            {
                double r = Math.Max(leftT[k], rightT[k]) + zebraRoom;
                mouthR[k] = r;
                V2 nk = u[k].Left;
                var a = new ArmInput();
                a.OuterA.Add(V3.FromXZ(u[k] * r + nk * spec[k].Half, 0));
                foreach (var p in leftArc[k]) a.OuterA.Add(V3.FromXZ(p, 0));
                a.OuterB.Add(V3.FromXZ(u[k] * r - nk * spec[k].Half, 0));
                foreach (var p in rightArc[k]) a.OuterB.Add(V3.FromXZ(p, 0));
                a.Yielding = spec[k].Yielding;
                arms.Add(a);
                if (spec[k].Zebra)
                {
                    double zc = r - 1.5;
                    zebras.Add(new CrosswalkStrip(u[k] * zc + nk * spec[k].Half, u[k] * zc - nk * spec[k].Half, 2.5));
                }
            }
            return arms;
        }

        /// <summary>A car path from (arm a, lateral offset oa, entering) to (arm b, lateral offset ob, leaving) as a cubic sampled at 17 points.</summary>
        internal static LanePath G5Path(IList<G5Arm> spec, double[] mouthR, int a, double oa, int b, double ob, long from, long to)
        {
            V2 ua = Dir(spec[a].Deg), ub = Dir(spec[b].Deg);
            V2 p0 = ua * mouthR[a] + ua.Left * oa, p3 = ub * mouthR[b] + ub.Left * ob;
            double L = V2.Distance(p0, p3) / 3;
            V2 p1 = p0 - ua * L, p2 = p3 - ub * L;
            var pts = new List<V2>();
            for (int k = 0; k <= 16; k++)
            {
                double t = k / 16.0, s = 1 - t;
                pts.Add(p0 * (s * s * s) + p1 * (3 * s * s * t) + p2 * (3 * s * t * t) + p3 * (t * t * t));
            }
            return new LanePath(pts, from, to);
        }

        private static V2 Dir(double deg) { double a = deg * Math.PI / 180; return new V2(Math.Cos(a), Math.Sin(a)); }

        /// <summary>
        /// Crescent Street x Eastgate Lane as the Carto export and the owner's shot give it. Left-hand traffic. Arm order
        /// (input order = ConnectedEdge order, not known for the refused node): Crescent E 0 deg, Eastgate N 90+skew, Crescent W
        /// 180, Eastgate S 270+skewS. Crescent one-way westbound: two lanes at lateral +-1.5 m (plus kerbside parking inside
        /// the carriageway, half width 5 m); Eastgate two-way, one lane each way at +-1.5 m, half width alleyHalf.
        /// </summary>
        internal static void G5Crescent(double skewN, double skewS, double alleyHalf, double R, out List<ArmInput> arms, out List<CrosswalkStrip> zebras, out List<V2> conflicts, out List<G5Arm> spec)
        {
            spec = new List<G5Arm>
            {
                new G5Arm { Name = "Crescent Street E", Deg = 0, Half = 5, Zebra = true },
                new G5Arm { Name = "Eastgate Lane N", Deg = 90 + skewN, Half = alleyHalf, Zebra = true, Yielding = true },
                new G5Arm { Name = "Crescent Street W", Deg = 180, Half = 5, Zebra = false },
                new G5Arm { Name = "Eastgate Lane S", Deg = 270 + skewS, Half = alleyHalf, Zebra = true, Yielding = true },
            };
            arms = G5Kerbs(spec, R, 3.5, out zebras, out double[] mr);
            // Lanes: lateral offset along the arm's u.Left (u points out of the junction). Left-hand traffic: a lane
            // ENTERING the junction (driving along -u) keeps to its own left = -u.Left (offset -1.5), a LEAVING lane
            // (along +u) to +u.Left (offset +1.5). Crescent is one-way westbound, so both its E lanes enter and both W lanes leave.
            var p = new List<LanePath>();
            long L(int arm, int lane) => arm * 10 + lane;
            // Crescent E -> W through, both lanes (Crescent W u = -x, Left = -z: z = +1.5 is offset -1.5)
            p.Add(G5Path(spec, mr, 0, 1.5, 2, -1.5, L(0, 1), L(2, 1)));
            p.Add(G5Path(spec, mr, 0, -1.5, 2, 1.5, L(0, 2), L(2, 2)));
            // Crescent E -> N (right turn in LHT, offside lane z = +1.5) into Eastgate N leaving lane (northbound, LHT: west side)
            V2 uN = Dir(spec[1].Deg);
            double nOut = V2.Dot(new V2(-1.5, 0), uN.Left) >= 0 ? 1.5 : -1.5;   // the west-side lateral of arm N
            p.Add(G5Path(spec, mr, 0, 1.5, 1, nOut, L(0, 1), L(1, 2)));
            // Crescent E -> S (left turn, nearside z = -1.5) into Eastgate S leaving lane (southbound, LHT: east side)
            V2 uS = Dir(spec[3].Deg);
            double sOut = V2.Dot(new V2(1.5, 0), uS.Left) >= 0 ? 1.5 : -1.5;
            p.Add(G5Path(spec, mr, 0, -1.5, 3, sOut, L(0, 2), L(3, 2)));
            // Eastgate N entering (southbound, east side) -> W (right turn), -> S (through)
            p.Add(G5Path(spec, mr, 1, -nOut, 2, 1.5, L(1, 1), L(2, 2)));
            p.Add(G5Path(spec, mr, 1, -nOut, 3, sOut, L(1, 1), L(3, 2)));
            // Eastgate S entering (northbound, west side) -> W (left turn), -> N (through)
            p.Add(G5Path(spec, mr, 3, -sOut, 2, -1.5, L(3, 1), L(2, 1)));
            p.Add(G5Path(spec, mr, 3, -sOut, 1, nOut, L(3, 1), L(1, 2)));
            conflicts = StraightBox.ConflictPoints(p);
        }

        /// <summary>Ring sides keep the margin from every marking (painted strip).</summary>
        private static double G5MarkingGap(IList<V3> ring, IList<CrosswalkStrip> marks)
        {
            double worst = double.MaxValue;
            for (int i = 0; i < ring.Count; i++)
                foreach (var m in marks) worst = Math.Min(worst, StraightBox.SegmentToStrip(ring[i].XZ, ring[(i + 1) % ring.Count].XZ, m));
            return worst;
        }

        private static bool SameRing(IList<V3> a, IList<V3> b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (V2.Distance(a[i].XZ, b[i].XZ) > 1e-9) return false;
            return true;
        }

        private static CarriagewayResult G5Solve(List<ArmInput> arms, List<CrosswalkStrip> zebras, List<V2> cps, bool chamfer)
        {
            bool was = StraightBox.ChamferCorners;
            StraightBox.ChamferCorners = chamfer;
            try { return CW.BuildStraight(arms, new V2(0, 0), zebras, 0.5, cps, false); }
            finally { StraightBox.ChamferCorners = was; }
        }

        private static void G5Tests()
        {
            V2 o = new V2(0, 0);
            int cases = 0, built = 0;
            foreach (double skew in new[] { 0.0, -15, 15 })
                foreach (double half in new[] { 3.0, 4.0 })
                    foreach (double R in new[] { 3.0, 5.0, 8.0 })
                    {
                        cases++;
                        string tag = $"Crescent x Eastgate (N arm skew {skew}, alley half {half} m, kerb radius {R} m)";
                        G5Crescent(skew, 0, half, R, out var arms, out var zebras, out var cps, out var spec);
                        // 0.4.6 behaviour (no chamfers): refused, a corner next to the alley that cannot clear
                        var old = G5Solve(arms, zebras, cps, false);
                        Check(old.Outline == null && old.Failure != null && old.Failure.Contains("still crosses the kerb or a marking margin: no allowed shift clears it"), $"{tag}: 0.4.6 refusal reproduced ({old.Failure})");
                        // without the conflict points it always built: the refusal comes from the turning paths pinning the mouths
                        Check(G5Solve(arms, zebras, null, false).Outline != null, $"{tag}: no conflict points = a box even in 0.4.6");
                        var res = G5Solve(arms, zebras, cps, true);
                        Check(res.Outline != null, $"{tag}: 0.4.7 builds a box ({res.Failure})");
                        if (res.Outline == null) continue;
                        built++;
                        var reference = G.ToXZ(G.BuildOutline(CW.BuildStraight(arms, o, null, 0, null, false).Kerbs, o, 0, out _));
                        double worst = WorstOutside(res.Outline, reference);
                        Check(worst <= StraightBox.KerbTolerance + 1e-6, $"{tag}: box on the carriageway (worst {worst:0.000} m over the kerb)");
                        var xz = G.ToXZ(res.Outline);
                        bool allIn = true;
                        foreach (var c in cps) allIn &= G.Contains(xz, c, 1e-3);
                        Check(allIn, $"{tag}: every conflict point inside");
                        Check(G.IsSimple(xz), $"{tag}: ring simple");
                        double gap = G5MarkingGap(res.Outline, zebras);
                        Check(gap >= 0.5 - 1e-3, $"{tag}: every side keeps 0.5 m from the zebras ({gap:0.000} m)");
                        Check(res.Straight.Chamfers.Count >= 1 && res.Straight.Chamfers.Count <= 4, $"{tag}: chamfered corner(s) listed ({res.Straight.Chamfers.Count})");
                        Check(res.Outline.Count <= 12, $"{tag}: straight-sided polygon, few vertices ({res.Outline.Count})");
                        bool noted = false;
                        foreach (var nt in res.Straight.Notes) noted |= nt.Contains("cut straight");
                        Check(noted, $"{tag}: chamfer named in the notes");
                        if (skew == 0 && half == 3 && R == 5)
                        {
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            for (int k = 0; k < 5; k++) G5Solve(arms, zebras, cps, true);
                            Console.WriteLine($"    {tag}: {res.Outline.Count} vertices, chamfers {string.Join(", ", res.Straight.Chamfers.ConvertAll(c => $"corner {c.Corner + 1} {c.Depth:0.00} m"))}, shifts [{string.Join(", ", Array.ConvertAll(res.Straight.Shift, v => v.ToString("0.00")))}], solve {sw.Elapsed.TotalMilliseconds / 5:0.0} ms");
                            Console.WriteLine("      outline " + string.Join(" ", res.Outline.ConvertAll(v => $"({v.X:0.00},{v.Z:0.00})")));
                        }
                    }
            Check(built == cases, $"Crescent x Eastgate: every variant builds ({built}/{cases})");

            // Unchanged where 0.4.6 had a box: the live fixtures and the synthetic rule cases give the same outline with chamfers on.
            {
                var mc = new V2(LiveFixtures.MoonflowerCentre[0], LiveFixtures.MoonflowerCentre[1]);
                var lc = new V2(LiveFixtures.LindberghCentre[0], LiveFixtures.LindberghCentre[1]);
                foreach (var (name, arms, centre, paths) in new[] { ("Moonflower x Anchor", LiveFixtures.MoonflowerArms, mc, LiveFixtures.MoonflowerPaths), ("Lindbergh x Anchor", LiveFixtures.LindberghArms, lc, LiveFixtures.LindberghPaths) })
                {
                    var cps = LiveConflicts(paths);
                    bool was = StraightBox.ChamferCorners;
                    StraightBox.ChamferCorners = false;
                    var off = CW.BuildStraight(LiveInputs(arms, true), centre, null, 0.5, cps, false);
                    StraightBox.ChamferCorners = true;
                    var on = CW.BuildStraight(LiveInputs(arms, true), centre, null, 0.5, cps, false);
                    StraightBox.ChamferCorners = was;
                    Check(off.Outline != null && SameRing(off.Outline, on.Outline) && on.Straight.Chamfers.Count == 0, $"{name}: 0.4.6 box unchanged with chamfers on");
                }
                var synth = new (double[] deg, bool[] y, bool sig)[]
                {
                    (new double[] { 0, 90, 180 }, new[] { false, true, false }, false),
                    (new double[] { 0, 70, 180, 250 }, null, false),
                    (new double[] { 0, 70, 180, 250 }, new[] { false, true, false, true }, true),
                    (new double[] { 0, 90, 180, 270 }, new[] { false, false, false, true }, false),
                    (new double[] { 0, 60, 140, 220, 300 }, null, false),
                };
                foreach (var (deg, y, sig) in synth)
                {
                    StraightBox.ChamferCorners = false;
                    var off = CW.BuildStraight(Synth(deg, 8, 20, 6, 3, y), o, null, 0.5, null, sig);
                    StraightBox.ChamferCorners = true;
                    var on = CW.BuildStraight(Synth(deg, 8, 20, 6, 3, y), o, null, 0.5, null, sig);
                    Check(SameRing(off.Outline, on.Outline), $"synthetic {deg.Length}-arm ({string.Join("/", deg)}): same outline with chamfers on");
                }
            }

            // Still a refusal when no straight cut fits: two conflict points 0.1 m off the kerb curve, 50 deg apart round it
            // (no single straight line can touch the kerb and keep both on the box side), plus one in the stem mouth so the
            // stem cannot slide to close the corner; the failure says why no cut fits.
            {
                var arms = Synth(new double[] { 0, 90, 180 }, 8, 20, 6, 3, new[] { false, true, false });
                // fillet between the east arm (0) and the stem (90): outer-edge centre (14, 14) radius 6, kerb (inset 3) radius 9
                V2 fc = new V2(14, 14);
                V2 At(double deg) { double a = deg * Math.PI / 180; return fc + new V2(Math.Cos(a), Math.Sin(a)) * 9.1; }
                var cps = new List<V2> { new V2(0, 9), At(-110), At(-160) };
                var res = CW.BuildStraight(arms, o, null, 0.5, cps, false);
                Check(res.Outline == null && res.Failure != null && res.Failure.Contains("no straight cut fits"), "no cut fits: refused, failure says why (" + res.Failure + ")");
            }

            // Refusal log text (YellowBoxToolSystem): node position and arm names / widths.
            {
                string s = BoxSelection.Describe(new V3(-517.98, 596.1, 546.0), new List<ArmLabel>
                {
                    new ArmLabel("Crescent Street", "Two-Lane One-Way Road", 16f),
                    new ArmLabel("Eastgate Lane", "Alley", 8f),
                    new ArmLabel(null, "S-XX-|S 16m 50kph", 16f),
                });
                Check(s == "at (-517.98, 546.00), arms: 1 Crescent Street (Two-Lane One-Way Road, 16.0 m), 2 Eastgate Lane (Alley, 8.0 m), 3 unnamed (S-XX-|S 16m 50kph, 16.0 m)", "refusal text: position and arms (" + s + ")");
                Check(BoxSelection.Describe(new V3(1, 2, 3), null) == "at (1.00, 3.00), arms: none", "refusal text: no arms");
            }
        }
    }
}
