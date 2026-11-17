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

        /// <summary>
        /// 0.4.10 (G20): every pair of NEIGHBOURING boxes on one road (same edge and carriageways, in order along it) whose gap
        /// is below <paramref name="within"/> metres, for the per-pair merge log: (A, B) = indices into 'windows' (A the
        /// earlier one, or the box reaching furthest so far), Gap = B.From - furthest end so far (negative = overlap).
        /// A pair is merged by <see cref="Chains"/> exactly when Gap &lt; the merge gap (if the joint window fits).
        /// </summary>
        public static List<(int A, int B, double Gap)> Neighbours(IList<ExitWindow> windows, double within)
        {
            var result = new List<(int A, int B, double Gap)>();
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
            int far = -1;
            foreach (int i in order)
            {
                ExitWindow w = windows[i];
                if (far >= 0 && windows[far].Edge == w.Edge && windows[far].Carriageways == w.Carriageways)
                {
                    double gap = w.From - windows[far].To;
                    if (gap < within) result.Add((far, i, gap));
                    if (w.To > windows[far].To) far = i;
                    continue;
                }
                far = i;
            }
            return result;
        }

        /// <summary>
        /// 0.4.10 (review F5): the boxes as drawn, one entry per drawn box: each chain (<see cref="Chains"/>) whose joint window
        /// the paint accepts (<paramref name="jointFits"/>(first member, from, to): BoxShapes.Entrance with the joint window
        /// gives an outline) is ONE box over the joint window; every other window is its own box. Window order of the
        /// result: joint boxes first (chain order), then the single ones in input order. Shared by the driving list
        /// (Boxes/DrivingBands.cs); the paint's JointBoxes applies the same chains to its drawn boxes.
        /// </summary>
        public static List<(List<int> Members, double From, double To)> Assemble(IList<ExitWindow> windows, double gap, Func<int, double, double, bool> jointFits)
        {
            var result = new List<(List<int> Members, double From, double To)>();
            if (windows == null) return result;
            var done = new bool[windows.Count];
            foreach (var chain in Chains(windows, gap))
            {
                if (jointFits != null && !jointFits(chain.Members[0], chain.From, chain.To)) continue;
                foreach (int m in chain.Members) done[m] = true;
                result.Add((chain.Members, chain.From, chain.To));
            }
            for (int i = 0; i < windows.Count; i++)
                if (!done[i] && IsFinite(windows[i].From) && IsFinite(windows[i].To) && windows[i].To > windows[i].From)
                    result.Add((new List<int> { i }, windows[i].From, windows[i].To));
            return result;
        }

        /// <summary>Gap below this (m) between neighbouring boxes is logged with its merge decision (farther pairs are plainly separate).</summary>
        public const double LogPairsWithin = 15.0;

        /// <summary>
        /// 0.4.10 (G20): the log text of one neighbour pair's merge decision. 'jointFits' / 'jointWhy' = the joint window's
        /// check for the chain holding both (only read when the gap is below the merge gap).
        /// </summary>
        public static string PairDecision(double gap, double mergeGap, bool jointFits, string jointWhy, double from, double to)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string g = gap < 0 ? $"overlap {(-gap).ToString("0.0", ci)} m" : $"gap {gap.ToString("0.0", ci)} m";
            if (gap >= mergeGap) return $"NOT merged: {g} >= {mergeGap.ToString("0.0", ci)} m (a vehicle can wait between them)";
            if (!jointFits) return $"NOT merged: {g} < {mergeGap.ToString("0.0", ci)} m but the joint box is refused: {jointWhy}";
            return $"MERGED: {g} < {mergeGap.ToString("0.0", ci)} m (no vehicle can wait between them): one box, window {from.ToString("0.0", ci)}..{to.ToString("0.0", ci)} m";
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
