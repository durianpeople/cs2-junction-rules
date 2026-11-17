using System;
using System.Collections.Generic;

namespace JunctionRules.Core
{
    // 0.4.9-probe (JR-T-015, G14, owner 17 Nov 2026: "should be merged"): exit / entrance boxes on the same road stretch whose
    // windows overlap or are less than ExitBands.MergeGapMetres apart along the road are built as ONE box over the joint
    // window, not drawn as two outlines and unioned. Live (Alderbrook Street, shot a4d15d38): two exit boxes ~0.5-1 m apart
    // stayed two boxes (BoxMerge joins exit boxes only when they touch within 0.05 m), and an exit box and an entrance box a
    // little over 0.5 m apart were unioned with nearly collinear kerb sides, the union failed ("union boundary does not
    // close", log 08:49:39) and both outlines were drawn on top of each other. Game-independent; the paint
    // (Paint/YellowBoxPaintSystem.cs Regroup) rebuilds the chain's box with Boxes/BoxShapes.Entrance over the joint window
    // (EntranceBox.WindowFits: never over a road marking or a junction mouth; otherwise the boxes stay as they were).

    /// <summary>One exit / entrance box's window along its road edge's centre curve (metres), for chaining.</summary>
    public struct ExitWindow
    {
        public long Edge;
        public uint Carriageways;
        public double From, To;

        public ExitWindow(long edge, uint carriageways, double from, double to) { Edge = edge; Carriageways = carriageways; From = from; To = to; }
    }

    public static class ExitBoxWindows
    {
        /// <summary>
        /// Chains of box indices to build as one box: same edge, same carriageway set (never across a median), sorted along
        /// the road, each next window starting less than 'gap' metres after the furthest end so far (overlap included).
        /// Only chains of two or more are returned; joint window = [min From, max To].
        /// </summary>
        public static List<(List<int> Members, double From, double To)> Chains(IList<ExitWindow> windows, double gap)
        {
            var result = new List<(List<int> Members, double From, double To)>();
            if (windows == null || windows.Count < 2) return result;
            var order = new List<int>();
            for (int i = 0; i < windows.Count; i++)
                if (IsFinite(windows[i].From) && IsFinite(windows[i].To) && windows[i].To > windows[i].From) order.Add(i);
            order.Sort((a, b) =>
            {
                ExitWindow x = windows[a], y = windows[b];
                if (x.Edge != y.Edge) return x.Edge.CompareTo(y.Edge);
                if (x.Carriageways != y.Carriageways) return x.Carriageways.CompareTo(y.Carriageways);
                int c = x.From.CompareTo(y.From);
                return c != 0 ? c : a.CompareTo(b);
            });
            List<int> cur = null;
            double from = 0, to = 0;
            foreach (int i in order)
            {
                ExitWindow w = windows[i];
                if (cur != null && windows[cur[0]].Edge == w.Edge && windows[cur[0]].Carriageways == w.Carriageways && w.From - to < gap)
                {
                    cur.Add(i);
                    if (w.To > to) to = w.To;
                    continue;
                }
                if (cur != null && cur.Count > 1) result.Add((cur, from, to));
                cur = new List<int> { i };
                from = w.From;
                to = w.To;
            }
            if (cur != null && cur.Count > 1) result.Add((cur, from, to));
            return result;
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
