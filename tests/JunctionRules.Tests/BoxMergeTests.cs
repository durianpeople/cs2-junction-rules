using System;
using System.Collections.Generic;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.4-probe (JR-T-011 part A, owner 16 Nov 2026): boxes that overlap or touch are drawn as ONE box (one border, one
    /// hatch anchored once per group); storage stays per box. Plus the median rule: an exit box on a divided road stops at
    /// the median (live fixture: Ironplace Avenue, Medium Road Divided, read-only 16 Nov 2026).
    /// </summary>
    internal static partial class Program
    {
        private static List<V3> Rect(double x0, double z0, double x1, double z1, double y = 0)
            => new List<V3> { new V3(x0, y, z0), new V3(x1, y, z0), new V3(x1, y, z1), new V3(x0, y, z1) };

        private static BoxMergeInput Ex(long key, long sub, List<V3> o, long edge = 0, uint cw = MedianSplit.AllCarriageways)
            => new BoxMergeInput(key, sub, BoxMergeKind.Exit, o, edge, cw);

        private static BoxMergeInput Jn(long key, List<V3> o) => new BoxMergeInput(key, 0, BoxMergeKind.Junction, o);

        private static double RingsArea(List<List<V3>> rings)
        {
            double a = 0;
            foreach (var r in rings) a += BoxPaintGeometry.SignedArea(r);
            return a;
        }

        /// <summary>Every ring vertex lies on (or within tol of) the boundary of the input union: no side leaves the boxes.</summary>
        private static bool InsideUnion(List<List<V3>> rings, IList<List<V3>> boxes, double tol = 1e-4)
        {
            foreach (var r in rings)
                for (int i = 0; i < r.Count; i++)
                {
                    V2 a = r[i].XZ, b = r[(i + 1) % r.Count].XZ;
                    V2 m = new V2((a.X + b.X) * 0.5, (a.Z + b.Z) * 0.5);
                    bool ok = false;
                    foreach (var bx in boxes)
                    {
                        var xz = BoxPaintGeometry.ToXZ(bx);
                        if (BoxPaintGeometry.Contains(xz, m, tol)) { ok = true; break; }
                    }
                    if (!ok) return false;
                }
            return true;
        }

        private static void BoxMergeTests()
        {
            var prm = BoxMergeParams.ForMargin(0.5);
            Check(Math.Abs(prm.JoinGap - 0.6) < 1e-9, "join gap = marking margin + 0.1 m");

            // ---- M1: two exits facing each other across an undivided road (the owner's screenshot): overlapping boxes, one drawn box
            {
                var a = Rect(0, -5, 8, 3);      // exit on the south kerb, box kerb to kerb (-5..5) but stop-bay inset -> overlap
                var b = Rect(1, -3, 9, 5);      // facing exit on the north kerb, staggered by 1 m
                var g = BoxMerge.Merge(new[] { Ex(1, 11, a, 100), Ex(2, 12, b, 100) }, prm);
                Check(g.Count == 1, $"M1 facing exits: one drawn group (got {g.Count})");
                Check(g.Count == 1 && g[0].Merged && g[0].Members.Count == 2, "M1 the group holds both boxes and is merged");
                Check(g.Count == 1 && g[0].Rings.Count == 1, "M1 one outer border (one ring, no internal borders)");
                if (g.Count == 1 && g[0].Rings.Count == 1)
                {
                    double ua = RingsArea(g[0].Rings);
                    Check(Math.Abs(ua - (64 + 64 - 7 * 6)) < 1e-6, $"M1 union area = A + B - overlap ({ua:0.###} m2)");
                    Check(g[0].Rings[0].Count == 8, $"M1 straight-sided L-step outline has 8 corners (got {g[0].Rings[0].Count})");
                    Check(InsideUnion(g[0].Rings, new[] { a, b }), "M1 every side lies on the boxes (stays on the carriageway)");
                    Check(BoxPaintGeometry.IsSimple(BoxPaintGeometry.ToXZ(g[0].Rings[0])), "M1 merged ring is simple");
                }
            }

            // ---- M2: same boxes exactly on top of each other (two exits at the same spot): one box, area of one
            {
                var a = Rect(0, -5, 8, 5);
                var g = BoxMerge.Merge(new[] { Ex(1, 11, a), Ex(2, 12, Rect(0, -5, 8, 5)) }, prm);
                Check(g.Count == 1 && g[0].Rings.Count == 1 && Math.Abs(RingsArea(g[0].Rings) - 80) < 1e-6, "M2 coincident boxes draw once (area 80)");
                Check(g.Count == 1 && g[0].Rings[0].Count == 4, "M2 coincident boxes keep 4 corners");
            }

            // ---- M3: staggered exits on the same kerb that overlap along the road
            {
                var a = Rect(0, -5, 8, 5);
                var b = Rect(5, -5, 13, 5);
                var g = BoxMerge.Merge(new[] { Ex(1, 11, a), Ex(1, 12, b) }, prm);
                Check(g.Count == 1 && g[0].Rings.Count == 1, "M3 staggered overlapping exits: one box");
                Check(g.Count == 1 && Math.Abs(RingsArea(g[0].Rings) - 130) < 1e-6, "M3 union area 130 (13 x 10)");
                Check(g.Count == 1 && g[0].Rings[0].Count == 4, $"M3 collinear sides collapse: a plain rectangle (got {(g.Count == 1 ? g[0].Rings[0].Count : -1)} corners)");
            }

            // ---- M4: touching end-to-end (shared mouth line), with float noise inside the snap tolerance
            {
                var a = Rect(0, -5, 8, 5);
                var b = Rect(8.03, -5, 16, 5.0);     // 3 cm gap: snapped
                var c = Rect(7.98, -5.02, 16, 5);    // 2 cm overlap and 2 cm kerb noise
                var g1 = BoxMerge.Merge(new[] { Ex(1, 11, a), Ex(2, 12, b) }, prm);
                Check(g1.Count == 1 && g1[0].Rings.Count == 1 && g1[0].Rings[0].Count == 4, "M4 end-to-end, 3 cm gap: one rectangle");
                var g2 = BoxMerge.Merge(new[] { Ex(1, 11, a), Ex(2, 12, c) }, prm);
                Check(g2.Count == 1 && g2[0].Rings.Count == 1, "M4 end-to-end, 2 cm overlap: one box");
                Check(g2.Count == 1 && g2[0].Rings[0].Count <= 5 && Math.Abs(RingsArea(g2[0].Rings) - 160.08) < 0.01, $"M4 sub-tolerance kerb noise leaves no notch (at most the 2 cm kerb slant) ({(g2.Count == 1 ? g2[0].Rings[0].Count : -1)} corners)");
                // a real gap (not touching) stays two boxes for two exits
                var d = Rect(8.5, -5, 16, 5);
                var g3 = BoxMerge.Merge(new[] { Ex(1, 11, a), Ex(2, 12, d) }, prm);
                Check(g3.Count == 2, "M4 exits 0.5 m apart are not joined (only junction-exit gaps are bridged)");
            }

            // ---- M5: exit box next to a junction box (gap = marking margin, the exit window keeps margin from the mouth)
            {
                var j = Rect(-12, -5, 0, 5);
                var e = Rect(0.5, -5, 8.5, 5);
                var g = BoxMerge.Merge(new[] { Jn(7, j), Ex(1, 11, e) }, prm);
                Check(g.Count == 1 && g[0].Bridges == 1, "M5 exit 0.5 m from a junction box: bridged into one group");
                Check(g.Count == 1 && g[0].Rings.Count == 1 && g[0].Rings[0].Count == 4, "M5 bridged result is one rectangle");
                Check(g.Count == 1 && Math.Abs(RingsArea(g[0].Rings) - 205) < 1e-6, "M5 area = junction + exit + bridge strip (120 + 80 + 5)");
                // touching (no gap)
                var g2 = BoxMerge.Merge(new[] { Jn(7, j), Ex(1, 11, Rect(0, -5, 8, 5)) }, prm);
                Check(g2.Count == 1 && g2[0].Bridges == 0 && g2[0].Rings.Count == 1, "M5 exit touching a junction box: one box, no bridge needed");
                // a zebra between them (gap 2 x margin + 3 m strip) is never painted over
                var g3 = BoxMerge.Merge(new[] { Jn(7, j), Ex(1, 11, Rect(4, -5, 12, 5)) }, prm);
                Check(g3.Count == 2, "M5 marking between junction box and exit: not bridged, two boxes");
                // partial overlap along the side: the bridge only spans the shared length
                var g4 = BoxMerge.Merge(new[] { Jn(7, j), Ex(1, 11, Rect(0.5, -5, 8.5, 2)) }, prm);
                Check(g4.Count == 1 && Math.Abs(RingsArea(g4[0].Rings) - (120 + 56 + 3.5)) < 1e-6, "M5 bridge spans only the shared 7 m");
                Check(g4.Count == 1 && InsideUnion(g4[0].Rings, new[] { j, Rect(0.5, -5, 8.5, 2), Rect(0, -5, 0.5, 2) }), "M5 bridged outline stays on the boxes and the strip");
                // skewed sides (10 deg) are not bridged
                var sk = new List<V3> { new V3(0.5, 0, -5), new V3(8.5, 0, -5), new V3(8.5, 0, 5), new V3(0.5 + 10 * Math.Tan(10 * Math.PI / 180), 0, 5) };
                var g5 = BoxMerge.Merge(new[] { Jn(7, j), Ex(1, 11, sk) }, prm);
                Check(g5.Count == 2, "M5 exit side 10 deg off the junction side: no bridge");
                // two junction boxes 0.5 m apart are not bridged (bridges are junction-exit only)
                var g6 = BoxMerge.Merge(new[] { Jn(7, j), Jn(8, Rect(0.5, -5, 12, 5)) }, prm);
                Check(g6.Count == 2, "M5 two junction boxes with a gap: no bridge");
            }

            // ---- M6: one removed leaves the other (storage per box; groups are recomputed from what is left)
            {
                var a = Rect(0, -5, 8, 3);
                var b = Rect(1, -3, 9, 5);
                var both = BoxMerge.Merge(new[] { Ex(1, 11, a), Ex(2, 12, b) }, prm);
                var left = BoxMerge.Merge(new[] { Ex(2, 12, b) }, prm);
                Check(both.Count == 1 && left.Count == 1, "M6 group of two -> one group after a removal");
                Check(left.Count == 1 && !left[0].Merged && left[0].Rings.Count == 1 && Math.Abs(RingsArea(left[0].Rings) - 64) < 1e-6, "M6 the remaining exit is drawn alone, its own outline");
                Check(left.Count == 1 && left[0].Rings[0].Count == 4, "M6 the remaining box keeps its own 4 corners");
                // group membership is by (key, sub)
                var inputs = new[] { Ex(1, 11, a), Ex(2, 12, b), Ex(3, 13, Rect(40, -5, 48, 5)) };
                var g = BoxMerge.Merge(inputs, prm);
                Check(g.Count == 2, "M6 a far box is its own group");
                Check(BoxMerge.GroupOf(g, inputs, 1, 11) == BoxMerge.GroupOf(g, inputs, 2, 12), "M6 GroupOf: both merged exits in one group");
                Check(BoxMerge.GroupOf(g, inputs, 3, 13) != BoxMerge.GroupOf(g, inputs, 1, 11), "M6 GroupOf: the far box elsewhere");
                Check(BoxMerge.GroupOf(g, inputs, 9, 99) == -1, "M6 GroupOf: unknown box -1");
            }

            // ---- M7: hatch anchored once per group (shared rect over the whole union)
            {
                var a = Rect(0, -5, 8, 5);
                var b = Rect(5, -5, 13, 5);
                var g = BoxMerge.Merge(new[] { Ex(1, 11, a), Ex(1, 12, b) }, prm);
                if (g.Count == 1)
                {
                    BoxRect r = g[0].Anchor;
                    double lu = Math.Max(r.LenU, r.LenV), lv = Math.Min(r.LenU, r.LenV);
                    Check(Math.Abs(lu - 13) < 1e-6 && Math.Abs(lv - 10) < 1e-6, $"M7 anchor spans the whole group ({lu:0.##} x {lv:0.##})");
                    var pp = new BoxPaintParams();
                    var p0 = BoxPaintGeometry.BuildPieces(g[0].Rings[0], pp, BoxPaintMode.Hatch, g[0].Anchor);
                    Check(p0.Ok, "M7 group paints with the shared anchor " + p0.Failure);
                    Check(p0.Ok && V2.Distance(p0.Rect.Origin, r.Origin) < 1e-9, "M7 BuildPieces uses the given anchor");
                    var p1 = BoxPaintGeometry.BuildPieces(a, pp, BoxPaintMode.Hatch);
                    Check(p1.Ok, "M7 old 3-arg overload still paints " + p1.Failure);
                }
                else Check(false, "M7 group built");
                // L-shaped union: one anchor for all rings
                var l = BoxMerge.Merge(new[] { Ex(1, 11, Rect(0, -5, 8, 3)), Ex(2, 12, Rect(1, -3, 9, 5)) }, prm);
                Check(l.Count == 1 && l[0].Anchor.LenU * l[0].Anchor.LenV >= 9 * 10 - 1e-6, "M7 L-shaped group anchor covers the whole outline");
            }

            // ---- M8: union robustness: three boxes in a chain, a box inside another, rotated boxes
            {
                var g = BoxMerge.Merge(new[] { Ex(1, 1, Rect(0, 0, 4, 4)), Ex(1, 2, Rect(3, 0, 7, 4)), Ex(1, 3, Rect(6, 0, 10, 4)) }, prm);
                Check(g.Count == 1 && g[0].Rings.Count == 1 && Math.Abs(RingsArea(g[0].Rings) - 40) < 1e-6, "M8 chain of three: one 10 x 4 box");
                var g2 = BoxMerge.Merge(new[] { Ex(1, 1, Rect(0, 0, 10, 10)), Ex(1, 2, Rect(2, 2, 5, 5)) }, prm);
                Check(g2.Count == 1 && g2[0].Rings.Count == 1 && Math.Abs(RingsArea(g2[0].Rings) - 100) < 1e-6, "M8 box inside a box: the outer one");
                double c = Math.Cos(0.4), s = Math.Sin(0.4);
                Func<List<V3>, List<V3>> rot = r => { var o = new List<V3>(); foreach (V3 p in r) o.Add(new V3(p.X * c - p.Z * s + 500, 3, p.X * s + p.Z * c - 200)); return o; };
                var ra = rot(Rect(0, -5, 8, 3)); var rb = rot(Rect(1, -3, 9, 5));
                var g3 = BoxMerge.Merge(new[] { Ex(1, 1, ra), Ex(2, 2, rb) }, prm);
                Check(g3.Count == 1 && g3[0].Rings.Count == 1 && Math.Abs(RingsArea(g3[0].Rings) - 86) < 1e-4, "M8 rotated, offset facing exits merge the same (area 86)");
                Check(g3.Count == 1 && g3[0].Rings.Count == 1 && Math.Abs(g3[0].Rings[0][0].Y - 3) < 1e-9, "M8 heights carried through the union");
                var none = BoxMerge.Merge(new BoxMergeInput[] { Ex(1, 1, null), Ex(1, 2, new List<V3> { new V3(0, 0, 0), new V3(1, 0, 0) }) }, prm);
                Check(none.Count == 0, "M8 inputs without a usable outline are left out");
                var u = BoxMerge.UnionRings(new List<List<V3>> { Rect(0, 0, 2, 2), Rect(4, 0, 6, 2) }, out string why);
                Check(u == null || u.Count == 2, "M8 UnionRings on disjoint boxes: no single ring (" + why + ")");
            }

            // ---- M9: snapshot + hover rings
            {
                var a = Rect(0, -5, 8, 3);
                var b = Rect(1, -3, 9, 5);
                var inputs = new List<BoxMergeInput> { Ex(1, 11, a), Ex(2, 12, b) };
                int v0 = MergedBoxes.Version;
                MergedBoxes.Publish(inputs, BoxMerge.Merge(inputs, prm), prm);
                Check(MergedBoxes.Version == v0 + 1 && MergedBoxes.Groups.Count == 1, "M9 snapshot published (version bumps)");
                var h = MergedBoxes.HoverRings(Ex(2, 12, b));
                Check(h.Count == 1 && Math.Abs(RingsArea(h) - 86) < 1e-6, "M9 hovering a merged exit shows the group's outline");
                var h2 = MergedBoxes.HoverRings(Ex(5, 15, Rect(4, -5, 12, -1)));
                Check(h2.Count == 1 && RingsArea(h2) > 86, "M9 hovering a new exit that would join shows the group it would join");
                var h3 = MergedBoxes.HoverRings(Ex(6, 16, Rect(60, -5, 68, 5)));
                Check(h3.Count == 1 && Math.Abs(RingsArea(h3) - 80) < 1e-6, "M9 hovering a lone target shows its own outline");
                MergedBoxes.Clear();
                Check(MergedBoxes.Groups.Count == 0 && MergedBoxes.Inputs.Count == 0, "M9 Clear empties the snapshot");
            }
        }

        /// <summary>Ironplace Avenue (Medium Road Divided), read-only 16 Nov 2026, segment near (-2013.9, 588.7): composition lanes.</summary>
        private static List<CompositionLaneInfo> IronplaceLanes()
        {
            const int road = MedianSplit.LaneRoad, inv = MedianSplit.LaneInvert, ped = MedianSplit.LanePedestrian, park = 0x20 | MedianSplit.LaneRoad;
            return new List<CompositionLaneInfo>
            {
                new CompositionLaneInfo(-10.5, 3, 0, ped | inv),
                new CompositionLaneInfo(-8, 2, 0, park | inv),
                new CompositionLaneInfo(-5.5, 3, 0, road | inv),
                new CompositionLaneInfo(-2.5, 3, 0, road | inv),
                new CompositionLaneInfo(2.5, 3, 1, road),
                new CompositionLaneInfo(5.5, 3, 1, road),
                new CompositionLaneInfo(8, 2, 1, park),
                new CompositionLaneInfo(10.5, 3, 1, ped),
            };
        }

        private static void MedianSplitTests()
        {
            var lanes = IronplaceLanes();
            bool parkOn = new CompositionLaneInfo(8, 2, 1, 0x20 | MedianSplit.LaneRoad).OnCarriageway;

            // ---- D1: divided road, right side: kerb (9 or 7 without parking) to the median kerb (1)
            Check(MedianSplit.SideSpan(lanes, false, out SideSpanResult r), "D1 span found");
            Check(r.Divided, "D1 Ironplace Avenue: median found");
            Check(Math.Abs(r.Lo - 1) < 1e-9, $"D1 right side box stops at the median kerb x = 1 (got {r.Lo})");
            Check(Math.Abs(r.Hi - (parkOn ? 9 : 7)) < 1e-9, $"D1 right side box reaches the kerb (got {r.Hi})");
            Check(r.CarriagewayMask == MedianSplit.Bit(1), "D1 right side mask = carriageway 1 only");
            Check(MedianSplit.SideSpan(lanes, true, out SideSpanResult l) && l.Divided && Math.Abs(l.Hi + 1) < 1e-9 && l.CarriagewayMask == MedianSplit.Bit(0), "D1 left side: kerb to -1, carriageway 0");

            // ---- D2: the kerb insets: box from the kerb to the median only
            MedianSplit.Insets(12, 3, 3, r, false, out double iL, out double iR);
            Check(Math.Abs(iL - 13) < 1e-9 && Math.Abs(iR - 3) < 1e-9, $"D2 insets right side: far side at the median (13 | 3, got {iL} | {iR})");
            MedianSplit.Insets(12, 3, 3, l, true, out iL, out iR);
            Check(Math.Abs(iL - 3) < 1e-9 && Math.Abs(iR - 13) < 1e-9, "D2 insets left side mirror");
            var c = new List<V3>(); for (int i = 0; i <= 8; i++) c.Add(new V3(i * 2, 0, 0));
            MedianSplit.Insets(12, 3, 3, r, false, out iL, out iR);
            var box = EntranceBox.BuildStraight(c, 12, iL, iR, null, 0.5, new V2(8, -5));
            Check(box.Outline != null, "D2 divided exit box builds " + box.Failure);
            if (box.Outline != null)
            {
                double zmin = double.MaxValue, zmax = double.MinValue;
                foreach (V3 p in box.Outline) { zmin = Math.Min(zmin, p.Z); zmax = Math.Max(zmax, p.Z); }
                Check(Math.Abs(zmax - zmin - 8) < 1e-6 && Math.Abs(Math.Min(Math.Abs(zmin), Math.Abs(zmax)) - 1) < 1e-6, $"D2 box is 8 m wide, kerb to median only (z {zmin:0.##}..{zmax:0.##})");
                Check(zmin >= -1e-9 || zmax <= 1e-9, "D2 box never crosses the centre line (the median)");
            }

            // ---- D3: undivided road keeps the full kerb-to-kerb span
            var und = new List<CompositionLaneInfo>
            {
                new CompositionLaneInfo(-6, 2, 0, 0x20 | MedianSplit.LaneRoad | MedianSplit.LaneInvert),
                new CompositionLaneInfo(-3.5, 3, 0, MedianSplit.LaneRoad | MedianSplit.LaneInvert),
                new CompositionLaneInfo(-0.5, 3, 0, MedianSplit.LaneRoad | MedianSplit.LaneInvert),
                new CompositionLaneInfo(0.5, 3, 0, MedianSplit.LaneRoad),
                new CompositionLaneInfo(3.5, 3, 0, MedianSplit.LaneRoad),
                new CompositionLaneInfo(9, 3, 0, MedianSplit.LanePedestrian),
            };
            Check(MedianSplit.SideSpan(und, false, out SideSpanResult u) && !u.Divided && u.CarriagewayMask == MedianSplit.AllCarriageways, "D3 undivided: no median, all carriageways");
            Check(Math.Abs(u.Lo - (parkOn ? -7 : -5)) < 1e-9 && Math.Abs(u.Hi - 5) < 1e-9, $"D3 undivided: full kerb-to-kerb span ({u.Lo}..{u.Hi})");
            MedianSplit.Insets(10, 3, 5, u, false, out iL, out iR);
            Check(iL == 3 && iR == 5, "D3 undivided insets unchanged");
            // a centre turn lane (two-way) in its own carriageway group never cuts
            var turn = new List<CompositionLaneInfo>(und.GetRange(0, 3)) { new CompositionLaneInfo(1.5, 3, 1, MedianSplit.LaneRoad | MedianSplit.LaneTwoway), new CompositionLaneInfo(4.5, 3, 2, MedianSplit.LaneRoad) };
            Check(MedianSplit.SideSpan(turn, false, out SideSpanResult t) && !t.Divided, "D3 two-way centre lane: no cut");
            // a one-way divided pair (both carriageways the same way) is no median for the box
            var oneway = new List<CompositionLaneInfo> { new CompositionLaneInfo(-2.5, 3, 0, MedianSplit.LaneRoad), new CompositionLaneInfo(2.5, 3, 1, MedianSplit.LaneRoad) };
            Check(MedianSplit.SideSpan(oneway, false, out SideSpanResult ow) && !ow.Divided, "D3 same-direction carriageways: no cut");
            // protected bike track behind a divider never stops the box
            var bike = new List<CompositionLaneInfo>(IronplaceLanes()) { new CompositionLaneInfo(11.5, 2, 2, MedianSplit.LaneRoad | MedianSplit.LaneBicyclesOnly) };
            Check(MedianSplit.SideSpan(bike, false, out SideSpanResult bk) && bk.Divided && Math.Abs(bk.Lo - 1) < 1e-9 && (bk.CarriagewayMask & MedianSplit.Bit(1)) != 0, "D3 bike track carriageway does not stop the box before the median");
            Check(!MedianSplit.SideSpan(new List<CompositionLaneInfo>(), false, out SideSpanResult e) && e.CarriagewayMask == MedianSplit.AllCarriageways, "D3 no lanes: no span, all carriageways");

            // ---- D4: facing exits across a median are never merged
            {
                var prm = BoxMergeParams.ForMargin(0.5);
                var north = Rect(0, 1, 8, 9);
                var south = Rect(0, -9, 8, -1);
                var big = Rect(0, -9, 8, 9);
                var g = BoxMerge.Merge(new[] { Ex(1, 11, north, 500, MedianSplit.Bit(1)), Ex(2, 12, south, 500, MedianSplit.Bit(0)) }, prm);
                Check(g.Count == 2, "D4 facing exits across the median: two boxes");
                // even if they overlapped (bad data), disjoint carriageways on one edge keep them apart
                var g2 = BoxMerge.Merge(new[] { Ex(1, 11, big, 500, MedianSplit.Bit(1)), Ex(2, 12, Rect(2, -9, 10, 9), 500, MedianSplit.Bit(0)) }, prm);
                Check(g2.Count == 2, "D4 disjoint carriageways never join, even overlapping");
                Check(!BoxMerge.MayJoin(Ex(1, 11, north, 500, MedianSplit.Bit(1)), Ex(2, 12, south, 500, MedianSplit.Bit(0))), "D4 MayJoin false across a median");
                Check(BoxMerge.MayJoin(Ex(1, 11, north, 500, MedianSplit.Bit(1)), Ex(2, 12, Rect(5, 1, 13, 9), 500, MedianSplit.Bit(1))), "D4 same side may join");
                var g3 = BoxMerge.Merge(new[] { Ex(1, 11, north, 500, MedianSplit.Bit(1)), Ex(2, 12, Rect(5, 1, 13, 9), 500, MedianSplit.Bit(1)) }, prm);
                Check(g3.Count == 1, "D4 staggered exits on the same side of a divided road merge");
            }

            // ---- D5: band lanes on one side only
            {
                int seg = 7 << 8;
                Check(MedianSplit.InMask(MedianSplit.Bit(1), seg | 1), "D5 lane of carriageway 1 is in a side-1 band");
                Check(!MedianSplit.InMask(MedianSplit.Bit(1), seg | 0), "D5 lane of carriageway 0 is not in a side-1 band");
                Check(MedianSplit.InMask(MedianSplit.AllCarriageways, seg | 0) && MedianSplit.InMask(MedianSplit.AllCarriageways, seg | 3), "D5 undivided band holds every lane");
                var bands = new List<ExitBand> { new ExitBand(42, 0.5f, MedianSplit.Bit(1)), new ExitBand(42, 0.5f, MedianSplit.Bit(0)), new ExitBand(42, 0.5f, MedianSplit.Bit(1)) };
                var n = ExitBands.Normalise(bands, 64, out int dropped);
                Check(n.Length == 2 && dropped == 0, $"D5 Normalise dedupes same (edge, t, mask) but keeps the other side ({n.Length})");
                Check(new ExitBand(42, 0.5f).Carriageways == MedianSplit.AllCarriageways, "D5 2-arg ExitBand = all carriageways");
            }

            // ---- D6: band touch rule
            Check(ExitBands.Touch(0.1f, 0.3f, 0.3f, 0.5f, 0f), "D6 end-to-end bands touch");
            Check(ExitBands.Touch(0.1f, 0.3f, 0.32f, 0.5f, 0.05f), "D6 within tolerance touch");
            Check(!ExitBands.Touch(0.1f, 0.3f, 0.4f, 0.5f, 0.05f), "D6 apart bands do not touch");
        }
            /// <summary>0.4.4 wiring: paint draws groups, hover shows the group, driving merges bands and reads the side mask.</summary>
        private static void BoxMergeRepoTests()
        {
            string src = Path.Combine(RepoRoot(), "JunctionRules");
            string paint = File.ReadAllText(Path.Combine(src, "Paint", "YellowBoxPaintSystem.cs"));
            Check(paint.Contains("BoxMerge.Merge(inputs, prm)") && paint.Contains("MergedBoxes.Publish(inputs, groups, prm)"), "paint groups the boxes with BoxMerge.Merge and publishes the snapshot");
            Check(paint.Contains("g.Anchor"), "paint anchors each group's hatch once (group anchor)");
            Check(paint.Contains("MergedBoxes.Clear()"), "paint clears the snapshot on load / deactivate");
            string tool = File.ReadAllText(Path.Combine(src, "Boxes", "YellowBoxToolSystem.cs"));
            Check(tool.Contains("MergedBoxes.HoverRings(self)") && tool.Contains("key.GroupsVersion = MergedBoxes.Version"), "hover draws the merged group outline, cache keyed on the groups version");
            string yb = File.ReadAllText(Path.Combine(src, "Stage2", "JRYellowBox.cs"));
            Check(yb.Contains("MedianSplit.InMask(bands->Mask(i), carriageway)"), "driving: bands apply only to lanes of the exit's side");
            Check(yb.Contains("ExitBands.JunctionJoinMetres") && yb.Contains("NextBoxedJunction(lane)"), "driving: a band near a boxed junction needs room past the junction box too");
            string bridge = File.ReadAllText(Path.Combine(src, "Stage2", "JRStage2Bridge.cs"));
            Check(bridge.Contains("JRBoxEdges.FromBands(BoxEdgeHandoff.Keys, BoxEdgeHandoff.T, BoxEdgeHandoff.Masks, BoxEdgeHandoff.Window)"), "bridge hands the carriageway masks to the job");
            string shapes = File.ReadAllText(Path.Combine(src, "Boxes", "BoxShapes.cs"));
            Check(shapes.Contains("MedianSplit.Insets(") && shapes.Contains("span.Divided"), "exit box stops at the median on divided roads");
        }
    }
}
