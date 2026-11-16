using System;
using System.Collections.Generic;

namespace JunctionRules.Core
{
    // 0.4.2-probe (JR-T-007b, owner rulings 16 Nov 2026): the yellow box is a polygon of STRAIGHT sides only, for
    // junctions and building entrances alike (one shape code).
    //
    // - Each arm gives one straight side across its mouth (kerb to kerb), already set back from its stop / give-way
    //   line and zebra by the marking margin (CarriagewayOutline.StopDistances).
    // - Adjacent mouth sides' end points are joined by one straight edge.
    // - Where that edge would cross the kerb (leave the carriageway by more than KerbTolerance) or come nearer than the
    //   margin to a painted marking, a mouth SLIDES FORWARD along its arm toward the junction centre, parallel to
    //   itself; its end points are where the slid line meets the carriageway edge (the kerb chain).
    // - WHICH mouth slides: (1) priority-road rule: only the mouths of YIELDING arms (STOP / Give Way on the approach)
    //   move; the priority road's mouths stay exactly where they are. (2) Fallback when rule 1 does not decide (all arms
    //   equal: no signs, all-way STOP or signals; or moving only the yielding arms cannot clear every edge): every arm
    //   may move, and the search takes the smallest total shift. Hard constraint in both: every vehicle conflict point
    //   (crossing of two car paths through the node) stays inside the box. If no shift clears an edge without pushing a
    //   conflict point out, that edge is left as it is and logged.
    //
    // The search (Solve): every arm's shift is taken on a grid (GridStep) from 0 to its cap (the mouth line may not
    // pass the junction centre); a corner's verdict depends only on the shifts of its two arms, so the ring of arms is
    // an exact cyclic dynamic programme over the grid (fix arm 0's shift, chain the others, close the ring): cost =
    // 1e9 per conflict point pushed out + 1e6 per edge that does not clear + the sum of shifts. That finds the global
    // optimum on the grid in n * G^3 steps (n arms, G grid values; G <= MaxGrid), no iteration, so it always
    // terminates. Each moved arm is then refined by bisection between the grid value below and the found one, holding
    // the others, to the smallest shift that keeps its own corners as good (RefineSteps). Minimal by construction: an
    // arm whose edges already clear keeps shift 0 (any shift only adds cost).
    //
    // Game-independent; unit-tested in StraightBoxTests.

    /// <summary>One mouth of the box: its two end points at shift 0 (Entry, then Exit, in CCW ring order) and the unit normal into the box.</summary>
    public sealed class BoxMouth
    {
        public V3 Entry, Exit;
        public V2 Inward;
        /// <summary>Largest forward shift allowed (metres): the mouth line may not pass the junction centre.</summary>
        public double MaxShift;
        /// <summary>STOP / Give Way on this approach (the game's CarLaneFlags.Stop / Yield on the node lanes from this arm).</summary>
        public bool Yielding;
        /// <summary>Label for notes (e.g. "arm 2").</summary>
        public string Name;
    }

    /// <summary>One car path through a node (a node car lane): its plan polyline and keys of the lanes it starts from / ends in.</summary>
    public struct LanePath
    {
        public List<V2> Points;
        public long From, To;
        public LanePath(List<V2> points, long from, long to) { Points = points; From = from; To = to; }
    }

    public enum BoxShapeRule
    {
        /// <summary>Only yielding arms moved (or nothing needed to move with a priority road present).</summary>
        PriorityRoad,
        /// <summary>All arms equal (no signs, all-way STOP, signals): smallest total shift over every arm.</summary>
        FallbackEqual,
        /// <summary>Moving only the yielding arms could not clear every edge: smallest total shift over every arm.</summary>
        FallbackCannotClear,
    }

    public sealed class StraightBoxResult
    {
        public List<V3> Outline;
        public string Failure;
        /// <summary>Per mouth (input order): forward shift in metres.</summary>
        public double[] Shift;
        public BoxShapeRule Rule;
        public List<string> Notes = new List<string>();
        /// <summary>Edges (corner index i = mouth i to mouth i+1) left crossing the kerb / marking: no shift cleared them.</summary>
        public List<int> FailingCorners = new List<int>();
        public string RuleText => Rule == BoxShapeRule.PriorityRoad ? "priority-road" : Rule == BoxShapeRule.FallbackEqual ? "fallback-equal" : "fallback-cannot-clear";
    }

    public static class StraightBox
    {
        /// <summary>How far (metres) a joining edge may run over the carriageway edge and still count as clear (sampling noise of the kerb curve).</summary>
        public const double KerbTolerance = 0.05;
        /// <summary>Largest offset (metres) of a vertex from its neighbours' line that Snap removes (outward vertices only).</summary>
        public const double SnapTolerance = 0.25;
        public const double GridStep = 0.25;
        public const int MaxGrid = 120;
        public const int RefineSteps = 14;
        private const double PenaltyConflict = 1e9, PenaltyEdge = 1e6;

        /// <summary>A kerb chain with cumulative lengths.</summary>
        private sealed class Chain
        {
            public List<V3> P;
            public double[] S;
            public Chain(List<V3> p)
            {
                P = p;
                S = new double[p.Count];
                for (int i = 1; i < p.Count; i++) S[i] = S[i - 1] + V2.Distance(p[i - 1].XZ, p[i].XZ);
            }
            public double Length => S[S.Length - 1];
            public V3 At(double s)
            {
                if (s <= 0) return P[0];
                if (s >= Length) return P[P.Count - 1];
                int i = 1;
                while (i < S.Length - 1 && S[i] < s) i++;
                double seg = S[i] - S[i - 1];
                double t = seg < 1e-12 ? 0 : (s - S[i - 1]) / seg;
                V2 xz = P[i - 1].XZ + (P[i].XZ - P[i - 1].XZ) * t;
                return V3.FromXZ(xz, P[i - 1].Y + (P[i].Y - P[i - 1].Y) * t);
            }
        }

        private sealed class Ctx
        {
            public IList<BoxMouth> M;
            public Chain[] C;
            public IList<CrosswalkStrip> Markings;
            public double Margin;
            public IList<V2> Conflicts;
            public V2[] Mid, Dir;
            /// <summary>0.4.3 (JR-T-007c, review F1): shift cost = sum of squares (a slide splits between mouths) instead of the plain sum.</summary>
            public bool Squared;
        }

        /// <summary>
        /// The box for mouths in CCW ring order and the kerb chains between them (chain i runs from mouth i's Exit along
        /// the carriageway edge to mouth i+1's Entry). extraYielding: a yielding arm with no mouth of its own (a building
        /// driveway): the mouths given then all count as priority road. signals: all arms equal.
        /// </summary>
        public static StraightBoxResult Solve(IList<BoxMouth> mouths, IList<List<V3>> chains, IList<CrosswalkStrip> markings, double margin,
            IList<V2> conflictPoints, bool signals, bool extraYielding = false, bool squaredShiftCost = false)
        {
            var res = new StraightBoxResult();
            int n = mouths?.Count ?? 0;
            if (n < 2 || chains == null || chains.Count != n) { res.Failure = "fewer than 2 mouths or chain count mismatch"; return res; }
            var ctx = new Ctx { M = mouths, C = new Chain[n], Markings = markings ?? new List<CrosswalkStrip>(), Margin = Math.Max(0, margin), Mid = new V2[n], Dir = new V2[n], Squared = squaredShiftCost };
            for (int i = 0; i < n; i++)
            {
                var ch = chains[i];
                if (ch == null || ch.Count < 2) { res.Failure = $"kerb chain {i + 1} has fewer than 2 points"; return res; }
                ctx.C[i] = new Chain(ch);
                ctx.Mid[i] = (mouths[i].Entry.XZ + mouths[i].Exit.XZ) * 0.5;
                ctx.Dir[i] = (mouths[i].Exit.XZ - mouths[i].Entry.XZ).Normalized;
                if (ctx.Dir[i].Length < 0.5) ctx.Dir[i] = mouths[i].Inward.Left * -1;
            }
            // Conflict points: only those inside every base mouth line count (a point already behind a stop line is not the box's business).
            var cps = new List<V2>();
            int skipped = 0;
            if (conflictPoints != null)
                foreach (var c in conflictPoints)
                {
                    bool ok = true;
                    for (int i = 0; i < n; i++) if (V2.Dot(c - ctx.Mid[i], mouths[i].Inward) < -1e-6) ok = false;
                    if (ok) cps.Add(c); else skipped++;
                }
            ctx.Conflicts = cps;
            if (skipped > 0) res.Notes.Add($"{skipped} conflict point(s) behind a mouth line, not held inside");

            int yielding = 0;
            foreach (var m in mouths) if (m.Yielding) yielding++;
            bool equal = signals || (!extraYielding && (yielding == 0 || yielding == n));
            double[] shift;
            double cost;
            if (Trace != null)
                for (int i = 0; i < n; i++)
                    for (double sv = 0; sv <= Cap(ctx, i) + 1e-9; sv += 0.5)
                    {
                        int p = (i + n - 1) % n;
                        Trace($"{Name(mouths, i)} s={sv:0.0} cap={Cap(ctx, i):0.00} prevCorner={CornerPenalty(ctx, p, 0, sv)} nextCorner={CornerPenalty(ctx, i, sv, 0)} exit={ctx.C[i].At(ExitParam(ctx, i, sv)).XZ} entry={ctx.C[p].At(EntryParam(ctx, i, sv)).XZ}");
                    }
            if (!equal)
            {
                var movable = new bool[n];
                for (int i = 0; i < n; i++) movable[i] = mouths[i].Yielding;
                shift = Search(ctx, movable, out cost);
                if (cost < PenaltyEdge) res.Rule = BoxShapeRule.PriorityRoad;
                else
                {
                    var all = new bool[n];
                    for (int i = 0; i < n; i++) all[i] = true;
                    shift = Search(ctx, all, out cost);
                    res.Rule = BoxShapeRule.FallbackCannotClear;
                }
            }
            else
            {
                var all = new bool[n];
                for (int i = 0; i < n; i++) all[i] = true;
                shift = Search(ctx, all, out cost);
                res.Rule = BoxShapeRule.FallbackEqual;
            }
            res.Shift = shift;
            for (int i = 0; i < n; i++)
            {
                double p = CornerPenalty(ctx, i, shift[i], shift[(i + 1) % n]);
                if (p >= PenaltyConflict) res.Notes.Add($"edge {Name(mouths, i)} - {Name(mouths, (i + 1) % n)}: no shift keeps every conflict point inside; edge left as it is");
                else if (p >= PenaltyEdge) { res.FailingCorners.Add(i); res.Notes.Add($"edge {Name(mouths, i)} - {Name(mouths, (i + 1) % n)} still crosses the kerb or a marking margin: no allowed shift clears it (conflict points kept inside); edge left as it is"); }
            }
            for (int i = 0; i < n; i++) if (MouthPenalty(ctx, i, shift[i]) > 0) res.Notes.Add($"{Name(mouths, i)}: a conflict point lies outside its mouth line");

            // Polygon: Entry_i, Exit_i, ... ; a closed corner (the two ends met or passed on the chain) gives one vertex.
            var ring = new List<V3>();
            for (int i = 0; i < n; i++)
            {
                int prev = (i + n - 1) % n;
                double ta = ExitParam(ctx, prev, shift[prev]), tb = EntryParam(ctx, i, shift[i]);
                if (ta >= tb - 1e-6)
                {
                    ring.Add(ctx.C[prev].At(0.5 * (ta + tb)));
                    res.Notes.Add($"corner {Name(mouths, prev)} - {Name(mouths, i)} closed: the slid mouths meet on the kerb");
                }
                else ring.Add(ctx.C[prev].At(tb));
                double te = ExitParam(ctx, i, shift[i]), tn = EntryParam(ctx, (i + 1) % n, shift[(i + 1) % n]);
                if (te < tn - 1e-6) ring.Add(ctx.C[i].At(te));
            }
            ring = Clean(ring);
            if (ring.Count < 3) { res.Failure = "box collapsed to fewer than 3 points (mouths met)"; return res; }
            if (BoxPaintGeometry.SignedArea(ring) < 0) ring.Reverse();
            if (!BoxPaintGeometry.IsSimple(BoxPaintGeometry.ToXZ(ring))) { res.Failure = "box crosses itself (slid mouths passed each other)"; return res; }
            if (BoxPaintGeometry.SignedArea(ring) < 0.5) { res.Failure = "box area under 0.5 m2"; return res; }
            ring = Snap(ring, cps, res.Notes);
            res.Outline = ring;
            var xz = BoxPaintGeometry.ToXZ(ring);
            int outside = 0;
            foreach (var c in cps) if (!BoxPaintGeometry.Contains(xz, c, 1e-3)) outside++;
            if (outside > 0) res.Notes.Add($"{outside} conflict point(s) outside the box");
            return res;
        }

        /// <summary>Test / diagnostics hook (null in the game).</summary>
        public static Action<string> Trace;

        private static string Name(IList<BoxMouth> m, int i) => string.IsNullOrEmpty(m[i].Name) ? $"mouth {i + 1}" : m[i].Name;

        private static double Cap(Ctx ctx, int i) => Math.Max(0, ctx.M[i].MaxShift);

        /// <summary>Exact cyclic DP over the shift grid, then per-arm bisection refinement. Returns shifts; cost = total penalty + shifts.</summary>
        private static double[] Search(Ctx ctx, bool[] movable, out double cost)
        {
            int n = ctx.M.Count;
            var grid = new double[n][];
            for (int i = 0; i < n; i++)
            {
                if (!movable[i] || Cap(ctx, i) <= 1e-9) { grid[i] = new[] { 0.0 }; continue; }
                double step = Math.Max(GridStep, Cap(ctx, i) / (MaxGrid - 1));
                var g = new List<double>();
                for (double s = 0; s < Cap(ctx, i) - 1e-9; s += step) g.Add(s);
                g.Add(Cap(ctx, i));
                grid[i] = g.ToArray();
            }
            // node cost (shift + mouth containment) and corner tables
            var node = new double[n][];
            for (int i = 0; i < n; i++)
            {
                node[i] = new double[grid[i].Length];
                for (int a = 0; a < grid[i].Length; a++) node[i][a] = (ctx.Squared ? grid[i][a] * grid[i][a] : grid[i][a]) + MouthPenalty(ctx, i, grid[i][a]);
            }
            var corner = new double[n][,];
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                corner[i] = new double[grid[i].Length, grid[j].Length];
                for (int a = 0; a < grid[i].Length; a++)
                    for (int b = 0; b < grid[j].Length; b++) corner[i][a, b] = CornerPenalty(ctx, i, grid[i][a], grid[j][b]);
            }
            double best = double.MaxValue;
            int[] bestPick = null;
            for (int a0 = 0; a0 < grid[0].Length; a0++)
            {
                // dp over arms 1..n-1 with back pointers
                var dp = new double[n][];
                var from = new int[n][];
                dp[0] = new double[grid[0].Length];
                for (int a = 0; a < dp[0].Length; a++) dp[0][a] = a == a0 ? node[0][a0] : double.MaxValue;
                for (int i = 1; i < n; i++)
                {
                    dp[i] = new double[grid[i].Length];
                    from[i] = new int[grid[i].Length];
                    for (int b = 0; b < grid[i].Length; b++)
                    {
                        double m = double.MaxValue;
                        int arg = -1;
                        for (int a = 0; a < grid[i - 1].Length; a++)
                        {
                            if (dp[i - 1][a] == double.MaxValue) continue;
                            double v = dp[i - 1][a] + corner[i - 1][a, b];
                            if (v < m) { m = v; arg = a; }
                        }
                        dp[i][b] = m == double.MaxValue ? m : m + node[i][b];
                        from[i][b] = arg;
                    }
                }
                for (int b = 0; b < grid[n - 1].Length; b++)
                {
                    if (dp[n - 1][b] == double.MaxValue) continue;
                    double total = dp[n - 1][b] + corner[n - 1][b, a0];
                    if (total < best - 1e-12)
                    {
                        best = total;
                        bestPick = new int[n];
                        bestPick[n - 1] = b;
                        for (int i = n - 1; i >= 1; i--) bestPick[i - 1] = i - 1 == 0 ? a0 : from[i][bestPick[i]];
                    }
                }
            }
            var shift = new double[n];
            for (int i = 0; i < n; i++) shift[i] = grid[i][bestPick[i]];
            // refinement: each moved arm down to the smallest shift that keeps its two corners and mouth as good
            for (int i = 0; i < n; i++)
            {
                if (shift[i] <= 1e-9) continue;
                int p = (i + n - 1) % n, q = (i + 1) % n;
                double Pen(double s) => MouthPenalty(ctx, i, s) + CornerPenalty(ctx, p, shift[p], s) + CornerPenalty(ctx, i, s, shift[q]);
                double target = Pen(shift[i]);
                int k = Array.IndexOf(grid[i], shift[i]);
                double lo = k > 0 ? grid[i][k - 1] : 0, hi = shift[i];
                if (Pen(lo) <= target) { shift[i] = lo; continue; }
                for (int it = 0; it < RefineSteps; it++)
                {
                    double mid = 0.5 * (lo + hi);
                    if (Pen(mid) <= target) hi = mid; else lo = mid;
                }
                shift[i] = hi;
            }
            cost = 0;
            for (int i = 0; i < n; i++) cost += MouthPenalty(ctx, i, shift[i]) + CornerPenalty(ctx, i, shift[i], shift[(i + 1) % n]);
            return shift;
        }

        private static double MouthPenalty(Ctx ctx, int i, double s)
        {
            double pen = 0;
            foreach (var c in ctx.Conflicts) if (V2.Dot(c - ctx.Mid[i], ctx.M[i].Inward) < s - 1e-6) pen += PenaltyConflict;
            return pen;
        }

        /// <summary>Where mouth i's slid line meets chain i (from its start); the chain length when it never does.</summary>
        private static double ExitParam(Ctx ctx, int i, double s) => FirstHit(ctx.C[i], ctx.Mid[i] + ctx.M[i].Inward * s, ctx.Dir[i], false);

        /// <summary>Where mouth i's slid line meets chain i-1 (searched from that chain's end); 0 when it never does.</summary>
        private static double EntryParam(Ctx ctx, int i, double s)
        {
            int n = ctx.M.Count;
            return FirstHit(ctx.C[(i + n - 1) % n], ctx.Mid[i] + ctx.M[i].Inward * s, ctx.Dir[i], true);
        }

        private static double FirstHit(Chain c, V2 o, V2 d, bool fromEnd)
        {
            int m = c.P.Count;
            double Side(int k) => V2.Cross(d, c.P[k].XZ - o);
            if (!fromEnd)
            {
                double s0 = Side(0);
                if (Math.Abs(s0) < 1e-9) return 0;
                for (int k = 1; k < m; k++)
                {
                    double s1 = Side(k);
                    if (Math.Abs(s1) < 1e-9 || (s0 < 0) != (s1 < 0))
                    {
                        double t = Math.Abs(s1) < 1e-9 ? 1 : s0 / (s0 - s1);
                        return c.S[k - 1] + (c.S[k] - c.S[k - 1]) * t;
                    }
                    s0 = s1;
                }
                return c.Length;
            }
            else
            {
                double s0 = Side(m - 1);
                if (Math.Abs(s0) < 1e-9) return c.Length;
                for (int k = m - 2; k >= 0; k--)
                {
                    double s1 = Side(k);
                    if (Math.Abs(s1) < 1e-9 || (s0 < 0) != (s1 < 0))
                    {
                        double t = Math.Abs(s1) < 1e-9 ? 1 : s0 / (s0 - s1);
                        return c.S[k + 1] - (c.S[k + 1] - c.S[k]) * t;
                    }
                    s0 = s1;
                }
                return 0;
            }
        }

        /// <summary>Penalty of the joining edge of corner i (mouth i at shift si, mouth i+1 at sj).</summary>
        private static double CornerPenalty(Ctx ctx, int i, double si, double sj)
        {
            int n = ctx.M.Count, j = (i + 1) % n;
            var c = ctx.C[i];
            double ta = ExitParam(ctx, i, si), tb = EntryParam(ctx, j, sj);
            if (ta >= tb - 1e-6) return 0;   // closed corner: no joining edge
            V2 p = c.At(ta).XZ, q = c.At(tb).XZ;
            double pen = 0;
            if (!EdgeClear(ctx, c, ta, tb, p, q)) pen += PenaltyEdge;
            V2 e = q - p;
            double len = e.Length;
            if (len > 1e-9)
                foreach (var cp in ctx.Conflicts) if (V2.Cross(e, cp - p) / len < -1e-6) pen += PenaltyConflict;
            return pen;
        }

        /// <summary>
        /// The clearance test: no kerb chain point between the two end points lies more than KerbTolerance on the box
        /// side of the edge (the edge stays on the carriageway), and the edge keeps 'margin' from every marking's painted
        /// rectangle (centre line +- painted width / 2).
        /// </summary>
        public static bool EdgeClearOfKerb(IList<V3> chain, V2 p, V2 q, double tol = KerbTolerance)
        {
            V2 e = q - p;
            double len = e.Length;
            if (len < 1e-9) return true;
            foreach (var v in chain) if (V2.Cross(e, v.XZ - p) / len > tol) return false;
            return true;
        }

        private static bool EdgeClear(Ctx ctx, Chain c, double ta, double tb, V2 p, V2 q)
        {
            var between = new List<V3>();
            for (int k = 0; k < c.P.Count; k++) if (c.S[k] > ta + 1e-6 && c.S[k] < tb - 1e-6) between.Add(c.P[k]);
            if (!EdgeClearOfKerb(between, p, q)) return false;
            foreach (var m in ctx.Markings)
                if (SegmentToStrip(p, q, m) < ctx.Margin - 1e-3) return false;
            return true;
        }

        /// <summary>Plan distance from segment pq to a marking's painted rectangle (0 when they touch or overlap).</summary>
        public static double SegmentToStrip(V2 p, V2 q, CrosswalkStrip m)
        {
            V2 along = (m.B - m.A).Normalized;
            V2 off = along.Left * (Math.Max(0, m.Width) * 0.5);
            var rect = new List<V2> { m.A - off, m.B - off, m.B + off, m.A + off };
            if (BoxPaintGeometry.Contains(rect, p) || BoxPaintGeometry.Contains(rect, q)) return 0;
            double best = double.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                V2 a = rect[k], b = rect[(k + 1) % 4];
                if (BoxPaintGeometry.SegmentIntersect(p, q, a, b, out _, out _)) return 0;
                best = Math.Min(best, BoxPaintGeometry.DistanceToSegment(a, p, q));
                best = Math.Min(best, BoxPaintGeometry.DistanceToSegment(p, a, b));
                best = Math.Min(best, BoxPaintGeometry.DistanceToSegment(q, a, b));
            }
            return best;
        }

        /// <summary>
        /// The vehicle conflict points of a node: every proper crossing of two car paths (node car lane curves, sampled)
        /// that start from different lanes AND end in different lanes. Merges into one lane (same To) and diverges from
        /// one lane (same From) are left out: those paths meet at the lane end / start, i.e. ON a mouth line, and holding
        /// them inside would pin every exit arm at its mouth (at Moonflower x Anchor the merge into Moonflower's exit
        /// lane lies on its mouth, 2.8 m outside the owner's quad 1-2-3-4).
        /// </summary>
        public static List<V2> ConflictPoints(IList<LanePath> paths)
        {
            var r = new List<V2>();
            if (paths == null) return r;
            for (int a = 0; a < paths.Count; a++)
                for (int b = a + 1; b < paths.Count; b++)
                {
                    var p = paths[a];
                    var q = paths[b];
                    if (p.Points == null || q.Points == null || p.From == q.From || p.To == q.To) continue;
                    for (int i = 0; i < p.Points.Count - 1; i++)
                        for (int j = 0; j < q.Points.Count - 1; j++)
                            if (BoxPaintGeometry.SegmentIntersect(p.Points[i], p.Points[i + 1], q.Points[j], q.Points[j + 1], out double t, out _))
                            {
                                V2 x = p.Points[i] + (p.Points[i + 1] - p.Points[i]) * t;
                                bool dup = false;
                                foreach (var e in r) if (V2.Distance(e, x) < 0.05) { dup = true; break; }
                                if (!dup) r.Add(x);
                            }
                }
            return r;
        }

        /// <summary>
        /// Drops near-collinear OUTWARD vertices (within SnapTolerance of the line through their neighbours): a slid mouth
        /// stops as soon as its joining edges are within KerbTolerance of the kerb, which can leave its end points a
        /// few centimetres to decimetres proud of the neighbours' line (at Moonflower x Anchor 0.20 m above 1-4). Removing an
        /// outward vertex only shrinks the box (it stays on the carriageway and clear of markings); a removal that
        /// would push a conflict point out or make the ring not simple is skipped. CCW ring in, CCW ring out.
        /// </summary>
        public static List<V3> Snap(List<V3> ring, IList<V2> conflicts, List<string> notes)
        {
            var r = new List<V3>(ring);
            int removed = 0;
            while (r.Count > 3)
            {
                int best = -1;
                double bestD = SnapTolerance;
                for (int i = 0; i < r.Count; i++)
                {
                    V2 u = r[(i + r.Count - 1) % r.Count].XZ, v = r[i].XZ, w = r[(i + 1) % r.Count].XZ;
                    V2 e = w - u;
                    double len = e.Length;
                    if (len < 1e-6) continue;
                    double side = V2.Cross(e, v - u) / len;   // < 0: v right of u->w = outside a CCW ring
                    if (side > 1e-9) continue;
                    double d = -side;
                    if (d > bestD) continue;
                    var t = new List<V3>(r);
                    t.RemoveAt(i);
                    var xz = BoxPaintGeometry.ToXZ(t);
                    if (!BoxPaintGeometry.IsSimple(xz)) continue;
                    bool ok = true;
                    if (conflicts != null) foreach (var c in conflicts) if (!BoxPaintGeometry.Contains(xz, c, 1e-3)) { ok = false; break; }
                    if (!ok) continue;
                    best = i;
                    bestD = d;
                }
                if (best < 0) break;
                r.RemoveAt(best);
                removed++;
            }
            if (removed > 0) notes?.Add($"{removed} near-collinear corner(s) (within {SnapTolerance:0.00} m) straightened");
            return r;
        }

        private static List<V3> Clean(List<V3> ring)
        {
            var r = new List<V3>(ring.Count);
            foreach (var p in ring) if (r.Count == 0 || V2.Distance(r[r.Count - 1].XZ, p.XZ) > 1e-4) r.Add(p);
            while (r.Count > 1 && V2.Distance(r[0].XZ, r[r.Count - 1].XZ) <= 1e-4) r.RemoveAt(r.Count - 1);
            return r;
        }
    }
}
