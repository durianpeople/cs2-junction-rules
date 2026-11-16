using System;
using System.Collections.Generic;
using JunctionRules.Core;
using G = JunctionRules.Core.BoxPaintGeometry;
using CW = JunctionRules.Core.CarriagewayOutline;

namespace JunctionRules.Tests
{
    internal sealed class LiveArm
    {
        public string Name;
        public double[] OuterA, InnerA, OuterB, InnerB;
        public double HalfA, HalfB, InsetA, InsetB;
        public bool Yielding;
        public LiveArm(string name, double[] oa, double[] ia, double[] ob, double[] ib, double halfA, double halfB, double insetA, double insetB, bool yielding)
        {
            Name = name; OuterA = oa; InnerA = ia; OuterB = ob; InnerB = ib; HalfA = halfA; HalfB = halfB; InsetA = insetA; InsetB = insetB; Yielding = yielding;
        }
    }

    internal sealed class LivePath
    {
        public string From, To;
        public double[] Ctrl;
        public LivePath(string from, string to, double[] ctrl) { From = from; To = to; Ctrl = ctrl; }
    }

    /// <summary>0.4.2-probe (JR-T-007b): straight-sided box, slide-forward mouths, priority-road rule, conflict points.</summary>
    internal static partial class Program
    {
        private static List<V3> Pts(double[] a)
        {
            var r = new List<V3>();
            for (int i = 0; i + 2 < a.Length; i += 3) r.Add(new V3(a[i], a[i + 1], a[i + 2]));
            return r;
        }

        private static List<ArmInput> LiveInputs(LiveArm[] arms, bool withInner)
        {
            var r = new List<ArmInput>();
            foreach (var a in arms)
            {
                var x = new ArmInput { OuterA = Pts(a.OuterA), OuterB = Pts(a.OuterB), InsetA = a.InsetA, InsetB = a.InsetB, Yielding = a.Yielding };
                if (withInner) { x.InnerA = Pts(a.InnerA); x.InnerB = Pts(a.InnerB); x.HalfA = a.HalfA; x.HalfB = a.HalfB; }
                r.Add(x);
            }
            return r;
        }

        private static List<V2> LiveConflicts(LivePath[] paths)
        {
            var lp = new List<LanePath>();
            foreach (var p in paths)
            {
                var c = Pts(p.Ctrl);
                var pts = new List<V2>();
                for (int k = 0; k <= 32; k++)
                {
                    double t = k / 32.0, u = 1 - t;
                    double x = u * u * u * c[0].X + 3 * u * u * t * c[1].X + 3 * u * t * t * c[2].X + t * t * t * c[3].X;
                    double z = u * u * u * c[0].Z + 3 * u * u * t * c[1].Z + 3 * u * t * t * c[2].Z + t * t * t * c[3].Z;
                    pts.Add(new V2(x, z));
                }
                lp.Add(new LanePath(pts, p.From.GetHashCode(), p.To.GetHashCode()));
            }
            return StraightBox.ConflictPoints(lp);
        }

        /// <summary>The full carriageway (mouth lines + kerbs following the corner curves), from the lerp kerbs: the reference the box must stay inside.</summary>
        private static List<V2> Carriageway(LiveArm[] arms, V2 centre)
        {
            var res = CW.BuildStraight(LiveInputs(arms, true), centre, null, 0, null, false);
            var ring = G.BuildOutline(res.Kerbs, centre, 0, out string why);
            Check(ring != null, "reference carriageway ring builds (" + why + ")");
            return ring == null ? new List<V2>() : G.ToXZ(ring);
        }

        /// <summary>Largest distance (m) any point of the ring's sides (sampled every 0.1 m) lies outside the reference carriageway.</summary>
        private static double WorstOutside(IList<V3> ring, List<V2> carriageway)
        {
            double worst = 0;
            for (int i = 0; i < ring.Count; i++)
            {
                V2 a = ring[i].XZ, b = ring[(i + 1) % ring.Count].XZ;
                int steps = Math.Max(1, (int)(V2.Distance(a, b) / 0.1));
                for (int k = 0; k <= steps; k++)
                {
                    V2 p = a + (b - a) * (k / (double)steps);
                    if (!G.Contains(carriageway, p)) worst = Math.Max(worst, G.DistanceToBoundary(carriageway, p));
                }
            }
            return worst;
        }

        private static double LineDistance(V2 p, V2 a, V2 b) => Math.Abs(V2.Cross(b - a, p - a)) / (b - a).Length;

        private static bool HasVertex(IList<V3> ring, V3 p, double tol)
        {
            foreach (var v in ring) if (V2.Distance(v.XZ, p.XZ) <= tol) return true;
            return false;
        }

        private static void StraightBoxLiveTests()
        {
            // ---- Moonflower St x Anchor St (T; Moonflower has STOP; owner drawing 1-2-3-4 / 5-6 -> 7-8)
            {
                V2 centre = new V2(LiveFixtures.MoonflowerCentre[0], LiveFixtures.MoonflowerCentre[1]);
                var arms = LiveInputs(LiveFixtures.MoonflowerArms, true);
                var cps = LiveConflicts(LiveFixtures.MoonflowerPaths);
                Check(cps.Count >= 3, $"Moonflower x Anchor: car path crossings found ({cps.Count})");
                var res = CW.BuildStraight(arms, centre, null, MarkingMargin.Default, cps, false);
                Check(res.Outline != null, "Moonflower x Anchor: straight box builds (" + res.Failure + ")");
                if (res.Outline != null)
                {
                    var sb = res.Straight;
                    Check(sb.Rule == BoxShapeRule.PriorityRoad, "Moonflower x Anchor: priority-road rule decides (" + sb.RuleText + ")");
                    Check(sb.Shift[0] == 0 && sb.Shift[1] == 0, $"Moonflower x Anchor: Anchor mouths do not move (shifts {sb.Shift[0]}, {sb.Shift[1]})");
                    Check(sb.Shift[2] > 1.0, $"Moonflower x Anchor: Moonflower mouth slides forward ({sb.Shift[2]:0.00} m)");
                    // unshifted Anchor mouth points (kerb lines at the mouth) are exact vertices of the box
                    var k0 = res.Kerbs[0]; var k1 = res.Kerbs[1];
                    bool exact = HasVertex(res.Outline, k0.LeftKerb[0], 1e-9) && HasVertex(res.Outline, k0.RightKerb[0], 1e-9)
                        && HasVertex(res.Outline, k1.LeftKerb[0], 1e-9) && HasVertex(res.Outline, k1.RightKerb[0], 1e-9);
                    Check(exact, "Moonflower x Anchor: the 4 Anchor mouth points (1, 2, 3, 4) are box vertices, unchanged");
                    // points 1 and 4 = the Anchor mouth ends on the north (Moonflower) side: larger Z
                    V3 p1 = k0.LeftKerb[0].Z > k0.RightKerb[0].Z ? k0.LeftKerb[0] : k0.RightKerb[0];
                    V3 p4 = k1.LeftKerb[0].Z > k1.RightKerb[0].Z ? k1.LeftKerb[0] : k1.RightKerb[0];
                    double worstOff = 0;
                    foreach (var v in res.Outline) if (v.Z > centre.Z) worstOff = Math.Max(worstOff, LineDistance(v.XZ, p1.XZ, p4.XZ));
                    Check(worstOff <= 0.1, $"Moonflower x Anchor: Moonflower mouth (7-8) collinear with 1-4 within 0.1 m ({worstOff:0.000} m); box = quad 1-2-3-4");
                    Check(res.Outline.Count == 4, $"Moonflower x Anchor: straight-sided box, {res.Outline.Count} vertices");
                    var cw = Carriageway(LiveFixtures.MoonflowerArms, centre);
                    double outside = WorstOutside(res.Outline, cw);
                    Check(outside <= StraightBox.KerbTolerance + 1e-6, $"Moonflower x Anchor: box stays on the carriageway (worst {outside:0.000} m)");
                    var xz = G.ToXZ(res.Outline);
                    bool allIn = true;
                    foreach (var c in cps) allIn &= G.Contains(xz, c, 1e-3);
                    Check(allIn, "Moonflower x Anchor: every car path crossing inside the box");
                    Console.WriteLine($"    Moonflower x Anchor: rule {sb.RuleText}, shifts {string.Join(", ", Array.ConvertAll(sb.Shift, s => s.ToString("0.00")))} m, {res.Outline.Count} vertices, worst off 1-4 {worstOff:0.000} m");
                }
            }
            // ---- Lindbergh St x Anchor St (skewed 4-way; Anchor arms STOP, Lindbergh priority)
            {
                V2 centre = new V2(LiveFixtures.LindberghCentre[0], LiveFixtures.LindberghCentre[1]);
                var cw = Carriageway(LiveFixtures.LindberghArms, centre);
                // 0.4.0 path (outer curves offset sideways, chords where offset stubs do not meet): leaves the carriageway
                var old = CW.Build(LiveInputs(LiveFixtures.LindberghArms, false), centre, null, 0.05);
                Check(old.Outline != null, "Lindbergh x Anchor: 0.4.0 outline builds");
                double oldOut = old.Outline == null ? 0 : WorstOutside(old.Outline, cw);
                Check(oldOut > 0.5, $"Lindbergh x Anchor: 0.4.0 outline runs over the kerb (worst {oldOut:0.00} m outside the carriageway)");
                var cps = LiveConflicts(LiveFixtures.LindberghPaths);
                var res = CW.BuildStraight(LiveInputs(LiveFixtures.LindberghArms, true), centre, null, MarkingMargin.Default, cps, false);
                Check(res.Outline != null, "Lindbergh x Anchor: straight box builds (" + res.Failure + ")");
                if (res.Outline != null)
                {
                    var sb = res.Straight;
                    double outside = WorstOutside(res.Outline, cw);
                    Check(outside <= StraightBox.KerbTolerance + 1e-6, $"Lindbergh x Anchor: box stays on the carriageway (worst {outside:0.000} m)");
                    Check(sb.Shift[0] == 0 && sb.Shift[3] == 0 || sb.Rule != BoxShapeRule.PriorityRoad, "Lindbergh x Anchor: Lindbergh (priority) mouths stay when the priority-road rule decides");
                    var xz = G.ToXZ(res.Outline);
                    bool allIn = true;
                    foreach (var c in cps) allIn &= G.Contains(xz, c, 1e-3);
                    Check(allIn, "Lindbergh x Anchor: every car path crossing inside the box");
                    Console.WriteLine($"    Lindbergh x Anchor: rule {sb.RuleText}, shifts {string.Join(", ", Array.ConvertAll(sb.Shift, s => s.ToString("0.00")))} m, {res.Outline.Count} vertices, 0.4.0 worst over kerb {oldOut:0.00} m");
                }
            }
        }

        private static List<ArmInput> Synth(double[] deg, double H, double r, double R, double inset, bool[] yielding)
        {
            var arms = GameArms(deg, H, r, R, out _);
            for (int i = 0; i < arms.Count; i++) { arms[i].InsetA = arms[i].InsetB = inset; arms[i].Yielding = yielding != null && yielding[i]; }
            return arms;
        }

        private static void StraightBoxRuleTests()
        {
            V2 o = new V2(0, 0);
            // T: through road 0/180 priority, stem 90 with STOP. Through side straight; stem mouth slides to the through kerb line.
            {
                var arms = Synth(new double[] { 0, 90, 180 }, 8, 20, 6, 3, new[] { false, true, false });
                var res = CW.BuildStraight(arms, o, null, 0.5, null, false);
                Check(res.Outline != null, "T priority: builds (" + res.Failure + ")");
                if (res.Outline != null)
                {
                    var sb = res.Straight;
                    Check(sb.Rule == BoxShapeRule.PriorityRoad && sb.Shift[0] == 0 && sb.Shift[2] == 0 && sb.Shift[1] > 0, $"T priority: only the stem slides ({sb.RuleText}, {sb.Shift[1]:0.00} m)");
                    double maxZ = double.MinValue, minZ = double.MaxValue;
                    foreach (var v in res.Outline) { maxZ = Math.Max(maxZ, v.Z); minZ = Math.Min(minZ, v.Z); }
                    Check(Math.Abs(minZ - -5) < 1e-6, $"T priority: through side straight on the kerb line z = -5 ({minZ:0.000})");
                    Check(maxZ <= 5 + StraightBox.KerbTolerance + 1e-6, $"T priority: stem mouth slid to the far kerb line z = 5 ({maxZ:0.000})");
                    Check(res.Outline.Count <= 6, $"T priority: straight quad ({res.Outline.Count} vertices)");
                }
            }
            // All-way equal 4-way skew (no signs): fallback-equal, smallest total shift, stays on the carriageway.
            {
                var deg = new double[] { 0, 70, 180, 250 };
                var arms = Synth(deg, 8, 20, 6, 3, null);
                var res = CW.BuildStraight(arms, o, null, 0.5, null, false);
                Check(res.Outline != null && res.Straight.Rule == BoxShapeRule.FallbackEqual, "4-way skew, no signs: fallback-equal (" + res.Failure + ")");
                if (res.Outline != null)
                {
                    var reference = G.ToXZ(G.BuildOutline(CW.BuildStraight(Synth(deg, 8, 20, 6, 3, null), o, null, 0, null, false).Kerbs, o, 0, out _));
                    Check(WorstOutside(res.Outline, reference) <= StraightBox.KerbTolerance + 1e-6, "4-way skew: box on the carriageway");
                    double sum = 0; foreach (var s in res.Straight.Shift) sum += s;
                    Check(sum > 0, $"4-way skew: some mouths slide (total {sum:0.00} m)");
                }
                // all-way STOP counts as equal too
                var allStop = Synth(deg, 8, 20, 6, 3, new[] { true, true, true, true });
                Check(CW.BuildStraight(allStop, o, null, 0.5, null, false).Straight.Rule == BoxShapeRule.FallbackEqual, "4-way all-way STOP: fallback-equal");
                // signals: equal even with yield flags
                var sig = Synth(deg, 8, 20, 6, 3, new[] { false, true, false, true });
                Check(CW.BuildStraight(sig, o, null, 0.5, null, true).Straight.Rule == BoxShapeRule.FallbackEqual, "4-way with signals: fallback-equal");
            }
            // Yielding-only cannot clear: a cross where only one arm yields; the corners between two priority arms fail.
            {
                var arms = Synth(new double[] { 0, 90, 180, 270 }, 8, 20, 6, 3, new[] { false, false, false, true });
                var res = CW.BuildStraight(arms, o, null, 0.5, null, false);
                Check(res.Outline != null && res.Straight.Rule == BoxShapeRule.FallbackCannotClear, $"cross, one STOP arm: fallback-cannot-clear ({res.Straight?.RuleText}, {res.Failure})");
                if (res.Outline != null) Check(res.Straight.FailingCorners.Count == 0, "cross: every edge clears after the fallback");
            }
            // Conflict-point containment: a conflict point near the stem mouth stops the stem sliding past it.
            {
                var arms = Synth(new double[] { 0, 90, 180 }, 8, 20, 6, 3, new[] { false, true, false });
                var free = CW.BuildStraight(arms, o, null, 0.5, null, false);
                var cps = new List<V2> { new V2(0, 9), new V2(12, 0), new V2(-12, 0) };
                var held = CW.BuildStraight(Synth(new double[] { 0, 90, 180 }, 8, 20, 6, 3, new[] { false, true, false }), o, null, 0.5, cps, false);
                // JR-T-007b-r F2: the stem may not slide past the conflict point, so its corner edges cannot clear by sliding.
                // 0.4.6: no box at all. 0.4.7 (G5): the corners are cut by straight chamfers instead (on the carriageway, the
                // point inside); with chamfers off (the 0.4.6 behaviour) it is still the refusal naming the corner.
                Check(free.Outline != null, "containment: free stem builds");
                StraightBox.ChamferCorners = false;
                var held046 = CW.BuildStraight(Synth(new double[] { 0, 90, 180 }, 8, 20, 6, 3, new[] { false, true, false }), o, null, 0.5, cps, false);
                StraightBox.ChamferCorners = true;
                Check(held046.Outline == null && held046.Failure != null && held046.Failure.Contains("corner") && held046.Failure.Contains("still crosses the kerb"), "containment (0.4.6, no chamfers): an edge that cannot clear = no box, failure names the corner (" + held046.Failure + ")");
                Check(held046.Straight.FailingCorners.Count > 0, "containment (0.4.6): failing corner listed");
                Check(held.Outline != null && held.Straight.Chamfers.Count > 0 && held.Straight.FailingCorners.Count == 0, "containment (0.4.7): chamfered box instead of a refusal (" + held.Failure + ")");
                if (held.Outline != null)
                {
                    var hx = G.ToXZ(held.Outline);
                    bool inside = true;
                    foreach (var c in cps) inside &= G.Contains(hx, c, 1e-3);
                    Check(inside, "containment (0.4.7): every conflict point inside");
                    var reference = G.ToXZ(G.BuildOutline(CW.BuildStraight(Synth(new double[] { 0, 90, 180 }, 8, 20, 6, 3, null), o, null, 0, null, false).Kerbs, o, 0, out _));
                    Check(WorstOutside(held.Outline, reference) <= StraightBox.KerbTolerance + 1e-6, "containment (0.4.7): chamfered box on the carriageway");
                }
                if (free.Outline != null && held.Outline != null) Check(held.Straight.Shift[1] < free.Straight.Shift[1], $"containment: the stem slides less ({held.Straight.Shift[1]:0.00} < {free.Straight.Shift[1]:0.00} m)");
            }
            // Margin vs a zebra on a neighbouring arm: a joining edge keeps the margin from the painted strip.
            {
                double d = StraightBox.SegmentToStrip(new V2(0, 0), new V2(10, 0), new CrosswalkStrip(new V2(5, 1), new V2(5, 6), 1.0));
                Check(Math.Abs(d - 1.0) < 1e-9, $"strip distance: edge 1 m from a painted zebra strip end ({d:0.000})");
                double c = StraightBox.SegmentToStrip(new V2(0, 0), new V2(10, 0), new CrosswalkStrip(new V2(5, -1), new V2(5, 6), 1.0));
                Check(c == 0, "strip distance: crossing edge = 0");
            }
            // Conflict points: crossings only, merges and diverges left out.
            {
                var paths = new List<LanePath>
                {
                    new LanePath(new List<V2> { new V2(-5, 0), new V2(5, 0) }, 1, 2),
                    new LanePath(new List<V2> { new V2(0, -5), new V2(0, 5) }, 3, 4),
                    new LanePath(new List<V2> { new V2(0, -5), new V2(5, 0) }, 3, 2),
                };
                var cp = StraightBox.ConflictPoints(paths);
                Check(cp.Count == 1 && V2.Distance(cp[0], new V2(0, 0)) < 1e-9, $"conflict points: one crossing, the merge and diverge ignored ({cp.Count})");
            }
        }
    }
}
