using System;
using System.Collections.Generic;

namespace JunctionRules.Core
{
    // 0.4.3-probe (JR-T-007c): this is the box at a building's CAR EXIT ('access' below = the exit point on the road,
    // Core/BuildingExit.cs; 0.4.0-0.4.2 used the lot-front middle, now only the fallback). Class name kept.
    // 0.4.0-probe (JR-T-007): the yellow box in front of a building's access (UK style: the carriageway in front of the
    // driveway mouth kept clear, kerb to kerb). Shape: a band across the whole carriageway, from the kerb line on one
    // side to the kerb line on the other, 'length' metres along the road, centred on the building's access point
    // (Building.m_RoadEdge / m_CurvePosition, see BoxSelection) and clipped to the edge's own part (never into a
    // junction's node area; 0.4.2: from the nearer corner of each mouth, MarkingMargin clear of the mouths and of any
    // zebra stripes / stop or give-way lines on the road). It follows the road's curve. Kerb lines use the junction box's inset logic: the edge
    // composition's footway each side (CarriagewayOutline.FootwayWidths, with the stop-bay rule StopBayFootway), plus
    // the paint list's kerbInset, offset from the road's outer edges.

    /// <summary>0.4.2: a stretch along an edge's centre curve, metres from its start.</summary>
    public struct AlongSpan
    {
        public double From, To;
        public AlongSpan(double from, double to) { From = from; To = to; }
    }

    public static class EntranceBox
    {
        /// <summary>
        /// Where along the edge's centre curve the box runs: [from, to] in metres from the curve start, centred on
        /// 'access' (metres), 'length' long, clipped to [min, max] (the edge's own part, between its node slices).
        /// False when less than 1 m is left.
        /// </summary>
        public static bool Window(double access, double length, double min, double max, out double from, out double to)
        {
            from = Math.Max(min, access - length * 0.5);
            to = Math.Min(max, access + length * 0.5);
            return to - from >= 1.0;
        }

        /// <summary>
        /// 0.4.2 (JR-T-007b): the edge's own part along its centre curve, from its four mouth corners (metres along the
        /// curve of EdgeGeometry m_Start.m_Left.a / m_Start.m_Right.a and m_End.m_Left.d / m_End.m_Right.d). 0.4.0 took the
        /// MIDDLE of each mouth, so on a skewed mouth the band's kerb-side corner on the far side reached past that
        /// side's mouth into the node by half the skew; the band's ends are square to the curve, so the part has to end
        /// at the nearer corner of each mouth.
        /// </summary>
        public static void EdgePart(double startA, double startB, double endA, double endB, out double min, out double max)
        {
            min = Math.Max(startA, startB);
            max = Math.Min(endA, endB);
        }

        /// <summary>
        /// 0.4.2 (JR-T-007b, owner 16 Nov 2026): the window with clearances. The band keeps 'margin' metres clear of both
        /// mouths ([min, max] = the edge's own part) and of every painted marking on the road (zebra stripes at the end
        /// nodes, stop / give-way lines on the edge), each given as its painted extent [From, To] along the curve. A
        /// marking ahead of the access cuts the band's far end, one behind it the near end; the band is never moved, only
        /// shortened. False (with 'why') when the access itself is within 'margin' of a marking or mouth, or less than
        /// 1 m is left.
        /// </summary>
        public static bool Window(double access, double length, double min, double max, double margin, IList<AlongSpan> markings, out double from, out double to, out string why)
        {
            why = null;
            margin = Math.Max(0, margin);
            double lo = min + margin, hi = max - margin;
            from = Math.Max(lo, access - length * 0.5);
            to = Math.Min(hi, access + length * 0.5);
            if (access < lo - 1e-9 || access > hi + 1e-9) { why = $"exit {access:0.0} m is within {margin:0.0} m of a junction mouth (edge part {min:0.0}..{max:0.0} m)"; return false; }
            if (markings != null)
            {
                foreach (AlongSpan m in markings)
                {
                    if (!CarriagewayOutline.IsFinite(m.From) || !CarriagewayOutline.IsFinite(m.To)) continue;
                    double a = Math.Min(m.From, m.To) - margin, b = Math.Max(m.From, m.To) + margin;
                    if (b <= from || a >= to) continue;
                    if (access >= a && access <= b) { why = $"exit {access:0.0} m is on or within {margin:0.0} m of a road marking ({m.From:0.0}..{m.To:0.0} m)"; return false; }
                    if ((m.From + m.To) * 0.5 >= access) to = Math.Min(to, a); else from = Math.Max(from, b);
                }
            }
            if (to - from < 1.0) { why = $"less than 1 m of road left ({from:0.0}..{to:0.0} m) after keeping {margin:0.0} m clear of the junction mouths and road markings"; return false; }
            return true;
        }

        /// <summary>
        /// 0.4.9 (G14): a joint window of several boxes on one edge (Core/ExitBoxWindows) may be used when it keeps 'margin'
        /// clear of both mouths ([min, max] = the edge's own part) and of every painted marking, exactly as each box's own
        /// window does (<see cref="Window(double,double,double,double,double,IList{AlongSpan},out double,out double,out string)"/>):
        /// a marking in the gap between two boxes keeps them apart.
        /// </summary>
        public static bool WindowFits(double from, double to, double min, double max, double margin, IList<AlongSpan> markings, out string why)
        {
            why = null;
            margin = Math.Max(0, margin);
            const double eps = 1e-6;
            if (!(to - from >= 1.0)) { why = "joint window shorter than 1 m"; return false; }
            if (from < min + margin - eps || to > max - margin + eps) { why = $"joint window {from:0.0}..{to:0.0} m reaches within {margin:0.0} m of a junction mouth (edge part {min:0.0}..{max:0.0} m)"; return false; }
            if (markings != null)
            {
                foreach (AlongSpan m in markings)
                {
                    if (!CarriagewayOutline.IsFinite(m.From) || !CarriagewayOutline.IsFinite(m.To)) continue;
                    double a = Math.Min(m.From, m.To) - margin, b = Math.Max(m.From, m.To) + margin;
                    if (b <= from + eps || a >= to - eps) continue;
                    why = $"a road marking ({m.From:0.0}..{m.To:0.0} m) lies in the joint window {from:0.0}..{to:0.0} m";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 0.4.2: where a painted marking lies along the road: its four painted corners (centre line A-B, +- painted
        /// width / 2 square to it) projected onto the centre polyline (XZ points with their metres along the curve).
        /// False when the marking's middle lies more than 'maxLateral' metres from the centre line (not on this road).
        /// </summary>
        public static bool StripAlong(IList<V2> centre, IList<double> along, CrosswalkStrip s, double maxLateral, out AlongSpan span)
        {
            span = default;
            if (centre == null || along == null || centre.Count < 2 || along.Count != centre.Count) return false;
            if (!CarriagewayOutline.IsFinite(s.A) || !CarriagewayOutline.IsFinite(s.B) || !CarriagewayOutline.IsFinite(s.Width)) return false;
            Project(centre, along, (s.A + s.B) * 0.5, out _, out double lateral);
            if (lateral > maxLateral) return false;
            V2 n = (s.B - s.A).Normalized.Left * (Math.Max(0, s.Width) * 0.5);
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (V2 c in new[] { s.A + n, s.A - n, s.B + n, s.B - n })
            {
                Project(centre, along, c, out double m, out _);
                lo = Math.Min(lo, m); hi = Math.Max(hi, m);
            }
            span = new AlongSpan(lo, hi);
            return true;
        }

        /// <summary>
        /// 0.4.2-probe (owner 16 Nov 2026: "a building entrance IS a junction whose driveway is a yielding arm"): the
        /// entrance box through the same straight-sided solver as junction boxes (StraightBox.Solve). The road's two
        /// directions are the two priority mouths: straight lines across the carriageway (kerb to kerb, square to the
        /// road) at the window's ends ('centre' runs from 'from' to 'to'); the kerb chains are the two kerb lines between
        /// them. The driveway is the yielding arm, but the game gives it no geometry of its own (the building's access
        /// is a point on the edge curve, Building.m_RoadEdge / m_CurvePosition), so it has no mouth: the box side along
        /// the driveway is the kerb. The road mouths stay where the window puts them unless a joining edge crosses the
        /// kerb (inner side of a bend): then the fallback slides them toward each other (smallest total shift).
        /// </summary>
        public static StraightBoxResult BuildStraight(IList<V3> centre, double halfWidth, double footLeft, double footRight, IList<CrosswalkStrip> markings, double margin)
            => BuildStraight(centre, halfWidth, footLeft, footRight, markings, margin, null);

        /// <summary>
        /// 0.4.3 (JR-T-007c; review F1 of 0.4.2): with the exit point (on the road centre curve, x / z) held inside the box
        /// as a hard conflict point, and a sum-of-squares shift cost, so on a bend the fallback slides BOTH road mouths a
        /// little instead of one a lot (0.4.2: R 30 m, 8 m window: one mouth slid 3.58 m, the box shrank to ~4.4 m and the
        /// access could end up outside it). Null exit = 0.4.2 behaviour except for the squared cost.
        /// </summary>
        public static StraightBoxResult BuildStraight(IList<V3> centre, double halfWidth, double footLeft, double footRight, IList<CrosswalkStrip> markings, double margin, V2? exitPoint)
        {
            var fail = new StraightBoxResult();
            if (centre == null || centre.Count < 2) { fail.Failure = "fewer than 2 centre points"; return fail; }
            foreach (var p in centre) if (!CarriagewayOutline.IsFinite(p)) { fail.Failure = "non-finite centre point"; return fail; }
            double dl = halfWidth - footLeft, dr = halfWidth - footRight;
            if (!CarriagewayOutline.IsFinite(dl) || !CarriagewayOutline.IsFinite(dr) || dl + dr < 0.5) { fail.Failure = $"carriageway under 0.5 m wide (half width {halfWidth:0.00}, footways {footLeft:0.00}|{footRight:0.00})"; return fail; }
            V2 d0 = (centre[1].XZ - centre[0].XZ).Normalized;
            V2 d1 = (centre[centre.Count - 1].XZ - centre[centre.Count - 2].XZ).Normalized;
            var left = BoxPaintGeometry.OffsetToward(centre, dl, centre[0].XZ + d0.Left);
            var right = BoxPaintGeometry.OffsetToward(centre, dr, centre[0].XZ - d0.Left);
            double length = 0;
            for (int i = 1; i < centre.Count; i++) length += V2.Distance(centre[i - 1].XZ, centre[i].XZ);
            // CCW ring: right kerb forward, then left kerb back (the left of the travel direction is +Left, inside).
            var revLeft = new List<V3>(left);
            revLeft.Reverse();
            var revRight = new List<V3>(right);
            revRight.Reverse();
            int last = centre.Count - 1;
            var mouths = new List<BoxMouth>
            {
                new BoxMouth { Entry = left[0], Exit = right[0], Inward = d0, MaxShift = length * 0.5, Name = "road mouth (start side)" },
                new BoxMouth { Entry = right[last], Exit = left[last], Inward = d1 * -1, MaxShift = length * 0.5, Name = "road mouth (end side)" },
            };
            var chains = new List<List<V3>> { right, revLeft };
            // orientation: right kerb forward then left kerb back is CCW when the left kerb is on +Left (game xz); otherwise swap the sides
            var probe = new List<V3>(right);
            probe.AddRange(revLeft);
            if (BoxPaintGeometry.SignedArea(probe) < 0)
            {
                mouths[0].Entry = right[0]; mouths[0].Exit = left[0];
                mouths[1].Entry = left[last]; mouths[1].Exit = right[last];
                chains = new List<List<V3>> { left, revRight };
            }
            var hold = exitPoint.HasValue && CarriagewayOutline.IsFinite(exitPoint.Value) ? new List<V2> { exitPoint.Value } : null;
            return StraightBox.Solve(mouths, chains, markings, margin, hold, false, extraYielding: true, squaredShiftCost: true);
        }

        /// <summary>Nearest point of the polyline to p: metres along (from 'along') and the distance off the line.</summary>
        public static void Project(IList<V2> centre, IList<double> along, V2 p, out double metres, out double distance)
        {
            metres = along[0];
            distance = double.MaxValue;
            for (int i = 1; i < centre.Count; i++)
            {
                V2 a = centre[i - 1], d = centre[i] - a;
                double len2 = V2.Dot(d, d);
                double t = len2 > 1e-12 ? Math.Max(0, Math.Min(1, V2.Dot(p - a, d) / len2)) : 0;
                double dist = V2.Distance(p, a + d * t);
                if (dist < distance) { distance = dist; metres = along[i - 1] + (along[i] - along[i - 1]) * t; }
            }
        }

        /// <summary>
        /// The outline from the centre curve samples over the window (in road direction): the left kerb line is the curve
        /// moved left by halfWidth - footLeft, the right one moved right by halfWidth - footRight (left / right of the
        /// curve direction = the composition's x &lt; 0 / x &gt; 0 sides, LaneSystem.cs 3648). Returns a CCW ring, or
        /// null with 'why'.
        /// </summary>
        public static List<V3> Build(IList<V3> centre, double halfWidth, double footLeft, double footRight, out string why)
        {
            why = null;
            if (centre == null || centre.Count < 2) { why = "fewer than 2 centre points"; return null; }
            foreach (var p in centre) if (!CarriagewayOutline.IsFinite(p)) { why = "non-finite centre point"; return null; }
            double dl = halfWidth - footLeft, dr = halfWidth - footRight;
            if (!CarriagewayOutline.IsFinite(dl) || !CarriagewayOutline.IsFinite(dr) || dl + dr < 0.5) { why = $"carriageway under 0.5 m wide (half width {halfWidth:0.00}, footways {footLeft:0.00}|{footRight:0.00})"; return null; }
            V2 d0 = (centre[1].XZ - centre[0].XZ).Normalized;
            V2 leftPoint = centre[0].XZ + d0.Left;
            V2 rightPoint = centre[0].XZ - d0.Left;
            var left = BoxPaintGeometry.OffsetToward(centre, dl, leftPoint);
            var right = BoxPaintGeometry.OffsetToward(centre, dr, rightPoint);
            var ring = new List<V3>(left.Count + right.Count);
            ring.AddRange(right);
            for (int i = left.Count - 1; i >= 0; i--) ring.Add(left[i]);
            if (BoxPaintGeometry.SignedArea(ring) < 0) ring.Reverse();
            if (!BoxPaintGeometry.IsSimple(BoxPaintGeometry.ToXZ(ring))) { why = "outline crosses itself (road bends too sharply for the box)"; return null; }
            return ring;
        }
    }
}
