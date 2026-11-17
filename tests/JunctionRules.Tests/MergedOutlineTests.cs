using System;
using System.Collections.Generic;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.10 (owner 17 Nov 2026, "merge = one real box"): boxes that merge are built as ONE bigger box: a single outline from
    /// the start of the first to the end of the last, kerb to kerb, one continuous hatch over the gaps (no strip of plain road,
    /// no inner borders, no two hatches side by side); driving uses exactly that outline. The paint builds it with the same
    /// straight-box code as any exit box over the JOINT window (Paint/YellowBoxPaintSystem.JointBoxes -> Boxes/BoxShapes
    /// .Entrance(joint window) -> Core EntranceBox.BuildStraight), every member gets that one outline and BoxMerge keeps
    /// identical rings once; driving takes the same joint window (Boxes/DrivingBands, ExitBoxWindows.Assemble). Here: the
    /// Core geometry for the two live pairs, with the live road (Small Road 16 m, footways 3 m: carriageway half 5 m).
    /// </summary>
    internal static partial class Program
    {
        /// <summary>Centre points every metre over [from, to] metres along a straight road from 'origin' in direction 'dir'.</summary>
        private static List<V3> RoadCentre(V2 origin, V2 dir, double from, double to)
        {
            var c = new List<V3>();
            int n = Math.Max(2, (int)Math.Ceiling(to - from) + 1);
            for (int i = 0; i < n; i++)
            {
                double m = from + (to - from) * i / (n - 1);
                c.Add(new V3(origin.X + dir.X * m, 0, origin.Z + dir.Z * m));
            }
            return c;
        }

        private static void MergedOutlineCase(string name, V2 origin, V2 dir, double a0, double a1, double b0, double b1)
        {
            dir = dir.Normalized;
            V2 left = dir.Left;
            V2 At(double along, double lateral) => origin + dir * along + left * lateral;
            // the live windows chain into one joint window (paint and driving: ExitBoxWindows, MergeGapMetres)
            var w = new List<ExitWindow> { new ExitWindow(7, MedianSplit.AllCarriageways, a0, a1), new ExitWindow(7, MedianSplit.AllCarriageways, b0, b1) };
            var boxes = ExitBoxWindows.Assemble(w, ExitBands.MergeGapMetres, (m, f, t) => EntranceBox.WindowFits(f, t, 0, 300, 0.5, null, out _));
            Check(boxes.Count == 1 && Math.Abs(boxes[0].From - a0) < 1e-9 && Math.Abs(boxes[0].To - b1) < 1e-9, $"{name}: one joint window {a0}..{b1} m");
            // the ONE outline (BoxShapes.Entrance over the joint window: EntranceBox.BuildStraight, kerb to kerb, margin 0.5 m)
            V2 exit = At((a0 + a1) / 2, 4.0);   // the first box's exit point, held inside
            var joint = EntranceBox.BuildStraight(RoadCentre(origin, dir, a0, b1), 8, 3, 3, null, 0.5, exit);
            Check(joint.Outline != null && joint.Failure == null, $"{name}: one outline built " + joint.Failure);
            if (joint.Outline == null) return;
            var ring = BoxPaintGeometry.ToXZ(joint.Outline);
            Check(ring.Count == 4, $"{name}: a single straight-sided polygon (4 corners, got {ring.Count})");
            // straight sides: every corner lies on a kerb line (lateral +-5 m) and on an end line (a0 / b1)
            bool corners = true;
            foreach (V2 p in ring)
            {
                V2 d = p - origin;
                double along = V2.Dot(d, dir), lat = V2.Dot(d, left);
                corners &= Math.Abs(Math.Abs(lat) - 5.0) < 0.05 && (Math.Abs(along - a0) < 0.05 || Math.Abs(along - b1) < 0.05);
            }
            Check(corners, $"{name}: corners at the kerbs (5 m each side) and at {a0} / {b1} m: kerb to kerb, first start to last end");
            // covers the gap (the strip of plain road the owner saw) right across, and both boxes
            double gm = (a1 + b0) / 2;
            bool gap = true;
            foreach (double lat in new[] { -4.9, -2.5, 0, 2.5, 4.9 }) gap &= BoxPaintGeometry.Contains(ring, At(gm, lat), 1e-6);
            Check(gap, $"{name}: the gap at {gm:0.00} m is inside the box kerb to kerb (no strip of plain road)");
            Check(BoxPaintGeometry.Contains(ring, At(a0 + 1, 0), 1e-6) && BoxPaintGeometry.Contains(ring, At(b1 - 1, 0), 1e-6)
                && !BoxPaintGeometry.Contains(ring, At(a0 - 0.3, 0), 1e-6) && !BoxPaintGeometry.Contains(ring, At(b1 + 0.3, 0), 1e-6), $"{name}: from the first box's start to the last box's end, nothing more");
            Check(Math.Abs(Math.Abs(BoxPaintGeometry.SignedArea(joint.Outline)) - (b1 - a0) * 10) < 0.5, $"{name}: area = joint length x 10 m carriageway");
            // 0.4.9 before G20 (2 m rule at No. 40) drew the two boxes on their own: the gap was plain road
            var first = EntranceBox.BuildStraight(RoadCentre(origin, dir, a0, a1), 8, 3, 3, null, 0.5, exit);
            var second = EntranceBox.BuildStraight(RoadCentre(origin, dir, b0, b1), 8, 3, 3, null, 0.5, At((b0 + b1) / 2, 4.0));
            Check(first.Outline != null && second.Outline != null && !BoxPaintGeometry.Contains(BoxPaintGeometry.ToXZ(first.Outline), At(gm, 0), 1e-6)
                && !BoxPaintGeometry.Contains(BoxPaintGeometry.ToXZ(second.Outline), At(gm, 0), 1e-6), $"{name}: two separate boxes leave the gap uncovered (what the shot showed)");
            // the paint: every member carries the joint outline; BoxMerge keeps ONE ring (one border, one hatch)
            var inputs = new List<BoxMergeInput>
            {
                new BoxMergeInput(1, 11, BoxMergeKind.Exit, new List<V3>(joint.Outline), 7, MedianSplit.AllCarriageways),
                new BoxMergeInput(2, 12, BoxMergeKind.Exit, new List<V3>(joint.Outline), 7, MedianSplit.AllCarriageways),
            };
            var groups = BoxMerge.Merge(inputs, BoxMergeParams.ForMargin(0.5));
            Check(groups.Count == 1 && groups[0].Rings.Count == 1 && groups[0].Members.Count == 2, $"{name}: drawn as one group with ONE ring (no inner border, one hatch)");
            // a marking in the gap still keeps them apart (the marking-margin rule on the merged outline)
            Check(!EntranceBox.WindowFits(a0, b1, 0, 300, 0.5, new[] { new AlongSpan(gm - 0.1, gm + 0.1) }, out string why) && why.Contains("road marking"),
                $"{name}: a road marking in the gap keeps the boxes apart: " + why);
        }

        private static void MergedOutlineTests()
        {
            // Alderwood No. 40 (edge 557050): entrance 34.7..42.7 m + exit 45.5..53.5 m -> one box 34.7..53.5 m
            MergedOutlineCase("No. 40 Alderwood", new V2(-1950, 270), new V2(-1, 0), 34.7, 42.7, 45.5, 53.5);
            // Alderbrook (edge 984373): 193433 exit 110.0..118.0 m + 843726 entrance 119.3..127.3 m -> one box 110.0..127.3 m
            MergedOutlineCase("Alderbrook 193433 + 843726", new V2(-1900.96, 456.58), new V2(0.02424, -0.99971), 110.0, 118.0, 119.3, 127.3);
            // driving uses that one window: the job projects its two end points onto the lane (Core LaneWindow): the band is the box
            Check(LaneWindow.OnLane(0f, 0f, 0f, 10f, 0f, 20f, 0f, 30f, 30f, false, 0f, 3.47f, 0f, 5.35f, out float d0, out float d1)
                && Math.Abs(d0 * 30f - 3.47f) < 0.01f && Math.Abs(d1 * 30f - 5.35f) < 0.01f, "driving: the band is the joint window's two ends on the lane (the gap road is box)");
        }
    }
}
