using System;
using System.Collections.Generic;

namespace JunctionRules.Core
{
    // 0.4.0-probe (JR-T-007): the yellow box in front of a building's access (UK style: the carriageway in front of the
    // driveway mouth kept clear, kerb to kerb). Shape: a band across the whole carriageway, from the kerb line on one
    // side to the kerb line on the other, 'length' metres along the road, centred on the building's access point
    // (Building.m_RoadEdge / m_CurvePosition, see BoxSelection) and clipped to the edge's own part (never into a
    // junction's node area). It follows the road's curve. Kerb lines use the junction box's inset logic: the edge
    // composition's footway each side (CarriagewayOutline.FootwayWidths, with the stop-bay rule StopBayFootway), plus
    // the paint list's kerbInset, offset from the road's outer edges.

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
