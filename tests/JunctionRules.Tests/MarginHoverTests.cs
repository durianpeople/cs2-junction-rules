using System;
using System.Collections.Generic;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.2 (JR-T-007b): marking margin (stop lines, zebras, entrance window), margin setting and paint-test.json key,
    /// entrance boxes through the straight-sided solver, and the tool's hover outline (BoxHover).
    /// </summary>
    internal static partial class Program
    {
        private static void MarginHoverTests()
        {
            // --- Margin from the painted edge (stop line / zebra). Arm along +z, mouth at z = 20, kerbs x = +-5, centre at the origin.
            var arm = new ArmSlice(
                new List<V3> { new V3(-5, 0, 20), new V3(-5, 0, 30) },
                new List<V3> { new V3(5, 0, 20), new V3(5, 0, 30) });
            var arms = new List<ArmSlice> { arm };
            V2 o = new V2(0, 0);
            {
                // Stop line 0.4 m outside the mouth, painted 0.3 m wide: its junction-side edge is 0.25 m outside -> with 0.5 m margin the box starts 0.25 m in.
                var stop = new List<CrosswalkStrip> { new CrosswalkStrip(new V2(-4, 20.4), new V2(4, 20.4), 0.3, true) };
                double d5 = CarriagewayOutline.StopDistances(arms, o, stop, null, 0.5)[0];
                double d0 = CarriagewayOutline.StopDistances(arms, o, stop, null, 0.0)[0];
                Check(Math.Abs(d5 - 0.25) < 1e-9, $"margin: stop line, box edge 0.5 m clear of its painted edge ({d5:0.000} m in from the mouth)");
                Check(d0 == 0, "margin: stop line outside the mouth with margin 0 leaves the mouth where it is (0.4.0 behaviour)");
                double d1 = CarriagewayOutline.StopDistances(arms, o, stop, null, 1.0)[0];
                Check(Math.Abs(d1 - 0.75) < 1e-9 && d1 > d5, $"margin change: 1.0 m moves the edge 0.5 m further in ({d1:0.000})");
            }
            {
                // Zebra inside the node: centre line 2 m in, 3 m wide -> painted edge 3.5 m in -> box edge at 4.0 m with 0.5 m margin (0.4.0: flush at 3.5).
                var zebra = new List<CrosswalkStrip> { new CrosswalkStrip(new V2(-4, 18), new V2(4, 18), 3.0) };
                double d5 = CarriagewayOutline.StopDistances(arms, o, zebra, null, MarkingMargin.Default)[0];
                double d0 = CarriagewayOutline.StopDistances(arms, o, zebra, null, 0)[0];
                Check(Math.Abs(d5 - 4.0) < 1e-9 && Math.Abs(d0 - 3.5) < 1e-9, $"margin: zebra, 0.5 m beyond its painted edge ({d5:0.00}; flush {d0:0.00} = 0.4.0)");
                var notes = new List<string>();
                var far = new List<CrosswalkStrip> { new CrosswalkStrip(new V2(40, 0), new V2(48, 0), 3.0) };
                Check(CarriagewayOutline.StopDistances(arms, o, far, notes, 0.5)[0] == 0 && notes.Exists(n => n.Contains("ignored")), "margin: a marking next to no mouth is ignored, with a note");
            }

            // --- Margin setting: Options value, paint-test.json override, clamping.
            {
                Check(MarkingMargin.Default == 0.5f && MarkingMargin.Min == 0f && MarkingMargin.Max == 2f, "margin setting: default 0.5 m, range 0..2 m");
                Check(MarkingMargin.Effective(0.8, double.NaN, out string s1) == 0.8 && s1 == "options", "margin setting: Options value when the list has none");
                Check(MarkingMargin.Effective(0.8, 1.2, out string s2) == 1.2 && s2.Contains("markingMargin"), "margin setting: paint-test.json markingMargin overrides");
                Check(MarkingMargin.Effective(5, double.NaN, out _) == 2 && MarkingMargin.Effective(-1, double.NaN, out _) == 0, "margin setting: clamped to 0..2");
                Check(MarkingMargin.Effective(double.NaN, double.NaN, out string s3) == MarkingMargin.Default && s3.Contains("invalid"), "margin setting: invalid value -> default, reported");
                var c = PaintTestConfig.Parse("{\"paint\": {\"markingMargin\": 0.7}}");
                Check(c.Geometry.MarkingMargin == 0.7 && c.Problems.Count == 0, "paint-test.json: markingMargin read");
                var back = PaintTestConfig.Parse(c.Format());
                Check(back.Geometry.MarkingMargin == 0.7 && back.Problems.Count == 0, "paint-test.json: markingMargin round-trips");
                var none = PaintTestConfig.Parse("{\"paint\": {}}");
                Check(double.IsNaN(none.Geometry.MarkingMargin) && !none.Format().Contains("markingMargin"), "paint-test.json: absent markingMargin stays absent (Options value applies, never pinned)");
                var bad = PaintTestConfig.Parse("{\"paint\": {\"markingMargin\": \"x\"}}");
                Check(double.IsNaN(bad.Geometry.MarkingMargin) && bad.Problems.Exists(p => p.Contains("markingMargin")), "paint-test.json: bad markingMargin ignored and reported");
                var big = PaintTestConfig.Parse("{\"paint\": {\"markingMargin\": 9}}");
                Check(big.Geometry.MarkingMargin == MarkingMargin.Max && big.Problems.Exists(p => p.Contains("MarkingMargin")), "paint-test.json: out-of-range markingMargin clamped and reported");
            }

            // --- Entrance window: zebra and mouth clearances, edge part.
            {
                EntranceBox.EdgePart(1, 2, 40, 39, out double mn, out double mx);
                Check(mn == 2 && mx == 39, "entrance: edge part ends at the nearer mouth corner each end");
                var zebra = new List<AlongSpan> { new AlongSpan(22.5, 25.5) };
                Check(EntranceBox.Window(20, 8, 0, 50, 0.5, zebra, out double f, out double t, out _) && f == 16 && Math.Abs(t - 22.0) < 1e-9, $"entrance near a zebra: far end 0.5 m short of the stripes ({f:0.0}..{t:0.0})");
                Check(EntranceBox.Window(20, 8, 0, 50, 0.0, zebra, out _, out double t0, out _) && Math.Abs(t0 - 22.5) < 1e-9, "entrance near a zebra: margin 0 = flush on the stripes");
                Check(!EntranceBox.Window(24, 8, 0, 50, 0.5, zebra, out _, out _, out string why) && why.Contains("road marking"), "entrance on a zebra: no box, reason given");
                Check(EntranceBox.Window(3, 8, 2, 50, 0.5, null, out double f2, out _, out _) && f2 == 2.5, "entrance near a mouth: band kept 0.5 m clear of the mouth");
                Check(!EntranceBox.Window(2.2, 8, 2, 50, 0.5, null, out _, out _, out string why2) && why2.Contains("mouth"), "entrance access within the margin of a mouth: no box");
            }

            // --- Entrance boxes through the straight-sided solver.
            {
                // Straight road 8 m long, half width 8, 3 m footways (= live Capper Street, Small Road edge: sidewalks 5 m incl. 2 m parking,
                // clear 3 m; 0.4.1 drew its hover band over both pavements): priority mouths stay, quad on the kerb lines z = +-5, no pavement.
                var c = new List<V3>();
                for (int i = 0; i <= 8; i++) c.Add(new V3(i, 0, 0));
                var r = EntranceBox.BuildStraight(c, 8, 3, 3, null, 0.5);
                Check(r.Outline != null && r.Rule == BoxShapeRule.PriorityRoad && r.Shift[0] == 0 && r.Shift[1] == 0, "entrance straight: priority-road, no mouth moves (" + r.Failure + ")");
                if (r.Outline != null)
                {
                    bool onKerbs = true;
                    foreach (var v in r.Outline) onKerbs &= Math.Abs(Math.Abs(v.Z) - 5) < 1e-6 && v.X > -1e-6 && v.X < 8 + 1e-6;
                    Check(r.Outline.Count == 4 && onKerbs, $"entrance straight (Capper Street Small Road): 4 corners on the kerb lines, nothing on the pavements ({r.Outline.Count})");
                    Check(BoxPaintGeometry.SignedArea(r.Outline) > 0, "entrance straight: CCW ring");
                }
            }
            {
                // Curve radius 60 (inner kerb 55), 8 m long: the inner chord would cut 0.145 m over the kerb -> both road mouths slide in.
                double R = 60;
                var c = new List<V3>();
                for (int i = 0; i <= 16; i++) { double a = (i / 16.0) * (8 / R); c.Add(new V3(R * Math.Sin(a), 0, R - R * Math.Cos(a))); }
                var r = EntranceBox.BuildStraight(c, 8, 3, 3, null, 0.5);
                Check(r.Outline != null && r.Rule == BoxShapeRule.FallbackCannotClear && r.Shift[0] + r.Shift[1] > 0, $"entrance on a bend: fallback slides the road mouths in (smallest total) ({r.RuleText}, {(r.Shift == null ? "-" : string.Join(", ", Array.ConvertAll(r.Shift, s => s.ToString("0.00"))))}) " + r.Failure);
                if (r.Outline != null)
                {
                    var ctr = new V2(0, R);
                    double worst = 0;
                    var segs = BoxHover.Segments(r.Outline);
                    foreach (var sg in segs)
                        for (int k = 0; k <= 20; k++)
                        {
                            V2 p = sg.A.XZ + (sg.B.XZ - sg.A.XZ) * (k / 20.0);
                            double rad = V2.Distance(p, ctr);
                            worst = Math.Max(worst, Math.Max(55 - rad, rad - 65));
                        }
                    Check(worst <= StraightBox.KerbTolerance + 0.005, $"entrance on a bend: every side on the carriageway (worst {worst:0.000} m over)");
                }
            }

            // --- Hover outline (Traffic Priorities look): thin closed line, white = adds, orange = removes.
            {
                Check(Math.Abs(BoxHover.LineWidth - 0.225f) < 1e-6, "hover: line width 0.225 m (Traffic feedbackLinesWidth 0.3 x 0.75)");
                Check(BoxHover.Colour(false) == BoxHover.AddColour && BoxHover.AddColour[0] == 1 && BoxHover.AddColour[1] == 1 && BoxHover.AddColour[2] == 1, "hover: white when a click adds a box");
                Check(BoxHover.Colour(true) == BoxHover.RemoveColour && BoxHover.RemoveColour[1] < 1 && BoxHover.RemoveColour[2] == 0, "hover: orange when a click removes the box");
                var sq = new List<V3> { new V3(0, 0, 0), new V3(4, 0, 0), new V3(4, 0, 4), new V3(4, 0, 4.001), new V3(0, 0, 4) };
                var segs = BoxHover.Segments(sq);
                Check(segs.Count == 4 && segs[segs.Count - 1].B.X == 0 && segs[segs.Count - 1].B.Z == 0, $"hover: closed ring, tiny segments skipped ({segs.Count})");
                Check(BoxHover.Segments(null).Count == 0 && BoxHover.Segments(new List<V3> { new V3(0, 0, 0) }).Count == 0, "hover: nothing for no outline");
                Check(BoxHover.Segments(new List<V3> { new V3(0, 0, 0), new V3(double.NaN, 0, 0), new V3(2, 0, 0) }).Count == 1, "hover: non-finite points dropped");
            }
        }
    }
}
