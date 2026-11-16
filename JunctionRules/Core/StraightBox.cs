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
    //   (crossing of two car paths through the node) stays inside the box. JR-T-007b-r (owner rule: the box never goes
    //   onto the pavement or over a zebra / stop line): if after the fallback an edge still does not clear, or a
    //   conflict point ends up outside, there is NO box (Failure names the corner / mouth); never a box left as it is.
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
    // 0.4.7 (JR-T-013c, gap G5, owner rules: box on the carriageway only, straight-sided, 0.5 m marking margin, all
    // conflict points inside): CHAMFERED CORNERS. A joining edge is a chord of the kerb's corner curve, and a chord of
    // a fillet always runs over the pavement (by the arc's sagitta), so before 0.4.7 a corner cleared only when the
    // two mouths slid forward until their lines met ON the kerb. Where a conflict point pins a mouth (a car path that
    // crosses another inside a narrow side road's mouth, beyond the main road's kerb line: Crescent Street x Eastgate
    // Lane, an 8 m Alley across a 16 m street, refused in 0.4.6 with "corner ... still crosses the kerb ... no allowed
    // shift clears it"), the corner could never close and the whole junction got no box although a valid shape
    // exists. Now such a corner may instead be cut by ONE straight chamfer edge that touches the kerb curve but never
    // crosses it (a supporting line of the kerb between the two mouth ends: the line of one kerb segment, or the chord
    // moved parallel into the box past the deepest kerb point); its ends are where it meets the two mouth lines. Valid
    // only when each end stays within the half of its mouth side next to the corner (two chamfers of one narrow mouth
    // never overlap), it keeps the marking margin and every conflict point stays on the box side; of the valid lines
    // the one cutting the least carriageway off wins (TryChamfer). A chamfered
    // corner costs PenaltyChamfer (+ the area it cuts off): the search still closes a corner by sliding whenever it can, so every
    // junction that had a box in 0.4.6 keeps exactly the same shape (sliding costs only metres, far under the
    // penalty); the chamfer only turns a 0.4.6 refusal into a box.
    //
    // Game-independent; unit-tested in StraightBoxTests and G5Tests.

    /// <summary>One mouth of the box: its two end points at shift 0 (Entry, then Exit, in CCW ring order) and the unit normal into the box.</summary>
    public sealed class BoxMouth
    {
        public V3 Entry, Exit;
        public V2 Inward;
        /// <summary>Largest forward shift allowed (metres): the mouth line may not pass the junction centre.</summary>
        public double MaxShift;
        /// <summary>
        /// 0.4.8 (JR-T-014b, G5): smallest shift (metres): the arm's marking (+ margin) lies past its kerb lines, so the
        /// box may not start at the mouth (it would lie on the zebra); 0 = none. Holds for priority arms too (it is
        /// where the box starts, not a slide). Set only by the 0.4.8 retry of CarriagewayOutline.BuildStraight.
        /// </summary>
        public double MinShift;
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

        /// <summary>
        /// JR-T-007b-r (F9): the full 64-bit key of a game PathNode rebuilt from its public parts, in the game's own
        /// layout (Game.Pathfind/PathNode.cs: owner entity index &lt;&lt; 32 | curve position * 32767 &lt;&lt; 16 | secondary bit 31 |
        /// segment &lt;&lt; 8 | lane = GetLaneIndex()). 0.4.2 used PathNode.GetHashCode() (ulong hash = high XOR low 32 bits),
        /// so two different lane ends could share a key.
        /// </summary>
        public static long Key(int ownerIndex, ushort laneIndex, float curvePos, bool secondary)
        {
            ulong q = (ulong)System.Math.Max(0, System.Math.Min(32767, (int)System.Math.Round(curvePos * 32767f)));
            ulong k = ((ulong)(uint)ownerIndex << 32) | (q << 16) | laneIndex | (secondary ? 0x80000000UL : 0UL);
            return unchecked((long)k);
        }
    }

    public enum BoxShapeRule
    {
        /// <summary>Only yielding arms moved (or nothing needed to move with a priority road present).</summary>
        PriorityRoad,
        /// <summary>All arms equal (no signs, all-way STOP, signals): smallest total shift over every arm.</summary>
        FallbackEqual,
        /// <summary>Moving only the yielding arms could not clear every edge: smallest total shift over every arm (code "priority-moved").</summary>
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
        /// <summary>Edges (corner index i = mouth i to mouth i+1) that no allowed shift clears of the kerb / a marking, or that would leave a conflict point outside; any entry = Failure, no box.</summary>
        public List<int> FailingCorners = new List<int>();
        /// <summary>0.4.7 (G5): corners (i = mouth i to mouth i+1) cut by a straight chamfer instead of a kerb-to-kerb edge, with the chamfer depth (m).</summary>
        public List<(int Corner, double Depth)> Chamfers = new List<(int, double)>();
        /// <summary>Stable short code of the rule, for logs and tests.</summary>
        public string RuleCode => Rule == BoxShapeRule.PriorityRoad ? "priority-road" : Rule == BoxShapeRule.FallbackEqual ? "fallback-equal" : "priority-moved";
        /// <summary>The rule for logs: short code plus what it means (JR-T-007b-r: no wording that reads as an error).</summary>
        public string RuleText => Rule == BoxShapeRule.PriorityRoad ? "priority-road (only give-way / STOP arms moved)"
            : Rule == BoxShapeRule.FallbackEqual ? "fallback-equal (all arms equal: smallest total move)"
            : "priority-moved (priority arms moved: yielding arms alone cannot clear)";
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
        /// <summary>0.4.7 (G5): cost of a chamfered corner (plus the area it cuts off, m2): above any sum of shifts, far below an edge that does not clear.</summary>
        public const double PenaltyChamfer = 1e4;

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
            /// <summary>0.4.7 (G5): chamfered corners allowed in this search pass.</summary>
            public bool Chamfer;
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
            // 0.4.8 (JR-T-014b): an arm held clear of a marking past its kerb lines (MinShift) has its base line there,
            // exactly as an arm trimmed to its zebra + margin has (a point between the mouth and that line is skipped too).
            var cps = new List<V2>();
            int skipped = 0;
            if (conflictPoints != null)
                foreach (var c in conflictPoints)
                {
                    bool ok = true;
                    for (int i = 0; i < n; i++) if (V2.Dot(c - ctx.Mid[i], mouths[i].Inward) < Math.Max(0, mouths[i].MinShift) - 1e-6) ok = false;
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
            // 0.4.7 (G5): first exactly the 0.4.6 search (no chamfers: same shapes, same cost); only when that leaves an
            // edge that cannot clear is the search run again with chamfered corners allowed.
            ctx.Chamfer = false;
            shift = Shifts(ctx, equal, out cost, out BoxShapeRule rule);
            if (cost >= PenaltyEdge && ChamferCorners)
            {
                ctx.Chamfer = true;
                double[] s2 = Shifts(ctx, equal, out double c2, out BoxShapeRule r2);
                if (c2 <= cost) { shift = s2; cost = c2; rule = r2; }   // also on a tie: the failure then says why no cut fits
                else ctx.Chamfer = false;
            }
            res.Rule = rule;
            res.Shift = shift;
            // JR-T-007b-r F2: a corner that still fails after the fallback, or a conflict point pushed out, means no box.
            var fails = new List<string>();
            for (int i = 0; i < n; i++)
            {
                double p = CornerPenalty(ctx, i, shift[i], shift[(i + 1) % n]);
                string corner = $"corner {Name(mouths, i)} - {Name(mouths, (i + 1) % n)}";
                if (p >= PenaltyConflict) { res.FailingCorners.Add(i); fails.Add($"{corner}: no shift keeps every conflict point inside"); }
                else if (p >= PenaltyEdge)
                {
                    res.FailingCorners.Add(i);
                    var cs = Corner(ctx, i, shift[i], shift[(i + 1) % n]);
                    fails.Add($"{corner} still crosses the kerb or a marking margin: no allowed shift clears it" + (cs.ChamferWhy != null ? $" and no straight cut fits ({cs.ChamferWhy})" : ""));
                }
            }
            for (int i = 0; i < n; i++) if (MouthPenalty(ctx, i, shift[i]) > 0) fails.Add($"{Name(mouths, i)}: a conflict point lies outside its mouth line");
            if (fails.Count > 0)
            {
                res.Notes.AddRange(fails);
                res.Failure = "no box (would cover pavement / a marking or leave a conflict point outside): " + string.Join("; ", fails);
                return res;
            }

            // Polygon: Entry_i, Exit_i, ... ; a closed corner (the two ends met or passed on the chain) gives one vertex.
            var ring = new List<V3>();
            var corners = new CornerShape[n];
            for (int i = 0; i < n; i++)
            {
                corners[i] = Corner(ctx, i, shift[i], shift[(i + 1) % n]);
                if (corners[i].Chamfered)
                {
                    res.Chamfers.Add((i, corners[i].Depth));
                    res.Notes.Add($"corner {Name(mouths, i)} - {Name(mouths, (i + 1) % n)} cut straight {corners[i].Depth:0.00} m inside the kerb curve (chamfer: the mouths cannot slide to meet on the kerb without leaving a conflict point outside)");
                }
            }
            for (int i = 0; i < n; i++)
            {
                int prev = (i + n - 1) % n;
                var cp = corners[prev];
                if (cp.Closed)
                {
                    V3 v = ctx.C[prev].At(0.5 * (cp.Ta + cp.Tb));
                    // 0.4.8 (JR-T-014b): a mouth held clear of a marking past its kerb lines can lie beyond the whole kerb
                    // chain (Crescent W: zebra 2.3 m in, kerb lines 1.8 m long); the corner is then where the two mouth
                    // lines meet, on the asphalt, not a point of the kerb behind them. Only for such mouths (MinShift > 0):
                    // every other closed corner keeps its 0.4.7 vertex.
                    if ((Lo(ctx, prev) > 0 || Lo(ctx, i) > 0) && LineHit(ctx.Mid[prev] + mouths[prev].Inward * shift[prev], ctx.Dir[prev], ctx.Mid[i] + mouths[i].Inward * shift[i], ctx.Dir[i], out V2 x)
                        && NoKerbInside(ctx.C[prev], ctx.Mid[prev] + mouths[prev].Inward * shift[prev], mouths[prev].Inward, ctx.Mid[i] + mouths[i].Inward * shift[i], mouths[i].Inward))
                    {
                        ring.Add(V3.FromXZ(x, v.Y));
                        res.Notes.Add($"corner {Name(mouths, prev)} - {Name(mouths, i)} closed: the mouth lines meet on the asphalt");
                    }
                    else
                    {
                        ring.Add(v);
                        res.Notes.Add($"corner {Name(mouths, prev)} - {Name(mouths, i)} closed: the slid mouths meet on the kerb");
                    }
                }
                else ring.Add(cp.Chamfered ? cp.Qc : ctx.C[prev].At(cp.Tb));
                var ci = corners[i];
                if (!ci.Closed) ring.Add(ci.Chamfered ? ci.Pc : ctx.C[i].At(ci.Ta));
            }
            ring = Clean(ring);
            if (ring.Count < 3) { res.Failure = "box collapsed to fewer than 3 points (mouths met)"; return res; }
            if (BoxPaintGeometry.SignedArea(ring) < 0) ring.Reverse();
            if (!BoxPaintGeometry.IsSimple(BoxPaintGeometry.ToXZ(ring))) { res.Failure = "box crosses itself (slid mouths passed each other)"; return res; }
            if (BoxPaintGeometry.SignedArea(ring) < 0.5) { res.Failure = "box area under 0.5 m2"; return res; }
            ring = Snap(ring, cps, res.Notes);
            var xz = BoxPaintGeometry.ToXZ(ring);
            int outside = 0;
            foreach (var c in cps) if (!BoxPaintGeometry.Contains(xz, c, 1e-3)) outside++;
            if (outside > 0) { res.Failure = $"no box: {outside} conflict point(s) outside the box"; return res; }
            res.Outline = ring;
            return res;
        }

        /// <summary>
        /// The mouth shifts by the rule (priority road: only yielding arms move; fallback: every arm). 0.4.7 (G5): with
        /// chamfers allowed (ctx.Chamfer), "clears" = no chamfer needed (cost under PenaltyChamfer); when the yielding arms
        /// alone need a chamfer every arm may move and a chamfer-free all-arm shape wins; when both need one, the
        /// priority-road shape is kept.
        /// </summary>
        private static double[] Shifts(Ctx ctx, bool equal, out double cost, out BoxShapeRule rule)
        {
            int n = ctx.M.Count;
            var all = new bool[n];
            for (int i = 0; i < n; i++) all[i] = true;
            if (equal)
            {
                rule = BoxShapeRule.FallbackEqual;
                return Search(ctx, all, out cost);
            }
            var movable = new bool[n];
            for (int i = 0; i < n; i++) movable[i] = ctx.M[i].Yielding;
            double[] shift = Search(ctx, movable, out cost);
            double clears = ctx.Chamfer ? PenaltyChamfer : PenaltyEdge;
            if (cost < clears) { rule = BoxShapeRule.PriorityRoad; return shift; }
            double[] shiftAll = Search(ctx, all, out double costAll);
            if (ctx.Chamfer && costAll >= PenaltyChamfer && cost < PenaltyEdge) { rule = BoxShapeRule.PriorityRoad; return shift; }
            rule = BoxShapeRule.FallbackCannotClear;
            cost = costAll;
            return shiftAll;
        }

        /// <summary>Test / diagnostics hook (null in the game).</summary>
        public static Action<string> Trace;

        /// <summary>0.4.7 (G5): chamfered corners on (the game always). Tests switch it off to reproduce the 0.4.6 refusals.</summary>
        public static bool ChamferCorners = true;

        private static string Name(IList<BoxMouth> m, int i) => string.IsNullOrEmpty(m[i].Name) ? $"mouth {i + 1}" : m[i].Name;

        private static double Cap(Ctx ctx, int i) => Math.Max(0, ctx.M[i].MaxShift);

        /// <summary>0.4.8 (JR-T-014b): the smallest shift of mouth i (BoxMouth.MinShift, 0 unless a marking lies past its kerb lines).</summary>
        private static double Lo(Ctx ctx, int i) => Math.Max(0, ctx.M[i].MinShift);

        /// <summary>Exact cyclic DP over the shift grid, then per-arm bisection refinement. Returns shifts; cost = total penalty + shifts.</summary>
        private static double[] Search(Ctx ctx, bool[] movable, out double cost)
        {
            int n = ctx.M.Count;
            var grid = new double[n][];
            for (int i = 0; i < n; i++)
            {
                double lo0 = Lo(ctx, i);
                if (!movable[i] || Cap(ctx, i) <= lo0 + 1e-9) { grid[i] = new[] { lo0 }; continue; }
                double step = Math.Max(GridStep, (Cap(ctx, i) - lo0) / (MaxGrid - 1));
                var g = new List<double>();
                for (double s = lo0; s < Cap(ctx, i) - 1e-9; s += step) g.Add(s);
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
                if (shift[i] <= Lo(ctx, i) + 1e-9) continue;
                int p = (i + n - 1) % n, q = (i + 1) % n;
                double Pen(double s) => MouthPenalty(ctx, i, s) + CornerPenalty(ctx, p, shift[p], s) + CornerPenalty(ctx, i, s, shift[q]);
                double target = Pen(shift[i]);
                int k = Array.IndexOf(grid[i], shift[i]);
                double lo = k > 0 ? grid[i][k - 1] : Lo(ctx, i), hi = shift[i];
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
        private static double CornerPenalty(Ctx ctx, int i, double si, double sj) => Corner(ctx, i, si, sj).Penalty;

        /// <summary>0.4.7 (G5): how corner i is drawn for given shifts: closed, a kerb-to-kerb edge, or a chamfer.</summary>
        private struct CornerShape
        {
            public bool Closed, Chamfered;
            public double Ta, Tb, Depth, Penalty;
            /// <summary>Chamfer ends: on mouth i's line (Pc) and on mouth i+1's line (Qc).</summary>
            public V3 Pc, Qc;
            /// <summary>Why no chamfer fits (null when one does or none was needed).</summary>
            public string ChamferWhy;
        }

        private static CornerShape Corner(Ctx ctx, int i, double si, double sj)
        {
            int n = ctx.M.Count, j = (i + 1) % n;
            var c = ctx.C[i];
            var r = new CornerShape { Ta = ExitParam(ctx, i, si), Tb = EntryParam(ctx, j, sj) };
            if (r.Ta >= r.Tb - 1e-6) { r.Closed = true; return r; }   // closed corner: no joining edge
            V3 p3 = c.At(r.Ta), q3 = c.At(r.Tb);
            V2 p = p3.XZ, q = q3.XZ;
            V2 e = q - p;
            double len = e.Length;
            double conflictPen = 0;
            if (len > 1e-9)
                foreach (var cp in ctx.Conflicts) if (V2.Cross(e, cp - p) / len < -1e-6) conflictPen += PenaltyConflict;
            if (EdgeClear(ctx, c, r.Ta, r.Tb, p, q)) { r.Penalty = conflictPen; return r; }
            string why = null;
            if (ChamferCorners && ctx.Chamfer && TryChamfer(ctx, i, si, sj, r.Ta, r.Tb, p3, q3, out V3 pc, out V3 qc, out double depth, out double area, out why))
            {
                r.Chamfered = true;
                r.Pc = pc; r.Qc = qc; r.Depth = depth;
                r.Penalty = PenaltyChamfer + area;
                return r;
            }
            r.ChamferWhy = why;
            r.Penalty = PenaltyEdge + conflictPen;
            return r;
        }

        /// <summary>
        /// 0.4.7 (G5): the straight chamfer of corner i (mouth i at shift si, mouth i+1 at sj; p / q = their ends on chain
        /// i at ta / tb). Candidates are lines that touch the kerb between p and q without crossing it: the line of each
        /// kerb segment there (a supporting line of the corner curve) and the chord p-q moved parallel into the box by the
        /// deepest kerb point. A candidate is valid when every kerb point between p and q lies on or behind it (within
        /// KerbTolerance), it meets mouth i's line no further from p than half of mouth i's side and mouth i+1's line no
        /// further from q than half of that side (so the two chamfers of one mouth never overlap), it keeps the marking
        /// margin, and every conflict point stays on the box side. Of the valid ones the one cutting the least area off
        /// the carriageway wins. False with 'why' (the reason of the least bad candidate) when none is valid.
        /// </summary>
        private static bool TryChamfer(Ctx ctx, int i, double si, double sj, double ta, double tb, V3 p3, V3 q3, out V3 pc, out V3 qc, out double depth, out double area, out string why)
        {
            int n = ctx.M.Count, j = (i + 1) % n;
            pc = qc = default;
            depth = 0;
            area = 0;
            why = null;
            var c = ctx.C[i];
            V2 p = p3.XZ, q = q3.XZ, e = q - p;
            double len = e.Length;
            if (len < 1e-9) { why = "corner ends coincide"; return false; }
            // the kerb between p and q (p, inner chain points, q)
            var kerb = new List<V2> { p };
            for (int k = 0; k < c.P.Count; k++) if (c.S[k] > ta + 1e-6 && c.S[k] < tb - 1e-6) kerb.Add(c.P[k].XZ);
            kerb.Add(q);
            V2 ei = ctx.C[(i + n - 1) % n].At(EntryParam(ctx, i, si)).XZ, xj = ctx.C[j].At(ExitParam(ctx, j, sj)).XZ;
            V2 oi = ctx.Mid[i] + ctx.M[i].Inward * si, oj = ctx.Mid[j] + ctx.M[j].Inward * sj;
            double halfI = 0.5 * V2.Distance(ei, p), halfJ = 0.5 * V2.Distance(q, xj);
            // candidate lines (point, unit direction from the p side to the q side)
            var cand = new List<(V2 O, V2 D)>();
            double chordDepth = 0;
            foreach (var v in kerb) chordDepth = Math.Max(chordDepth, V2.Cross(e, v - p) / len);
            V2 eu = e * (1.0 / len);
            cand.Add((p + new V2(-eu.Z, eu.X) * chordDepth, eu));
            for (int k = 0; k + 1 < kerb.Count; k++)
            {
                V2 d = kerb[k + 1] - kerb[k];
                if (d.Length < 1e-6) continue;
                cand.Add((kerb[k], d.Normalized));
            }
            double bestArea = double.MaxValue;
            string bestWhy = null;
            int bestRank = -1;   // how far the best failing candidate got (for 'why')
            foreach (var (o, d) in cand)
            {
                // kerb on or behind the line
                double deep = 0;
                bool crosses = false;
                foreach (var v in kerb)
                {
                    double side = V2.Cross(d, v - o);
                    if (side > KerbTolerance) { crosses = true; break; }
                    deep = Math.Max(deep, -side);
                }
                if (crosses) continue;
                if (!LineHit(oi, ctx.Dir[i], o, d, out V2 a) || !LineHit(oj, ctx.Dir[j], o, d, out V2 b)) continue;
                double fromP = V2.Dot(p - a, ctx.Dir[i]), fromQ = V2.Dot(b - q, ctx.Dir[j]);
                if (fromP < -1e-6 || fromQ < -1e-6 || fromP > halfI + 1e-6 || fromQ > halfJ + 1e-6)
                {
                    if (bestRank < 1) { bestRank = 1; bestWhy = "a cut clear of the kerb would take more than half of a mouth side"; }
                    continue;
                }
                bool markOk = true;
                foreach (var m in ctx.Markings) if (SegmentToStrip(a, b, m) < ctx.Margin - 1e-3) { markOk = false; break; }
                if (!markOk)
                {
                    if (bestRank < 2) { bestRank = 2; bestWhy = "a cut clear of the kerb comes within the marking margin"; }
                    continue;
                }
                int outside = 0;
                foreach (var cp in ctx.Conflicts) if (V2.Cross(d, cp - o) < -1e-6) outside++;
                if (outside > 0)
                {
                    if (bestRank < 3) { bestRank = 3; bestWhy = $"a cut clear of the kerb would leave {outside} conflict point(s) outside"; }
                    continue;
                }
                // area cut off: a, p, kerb..., q, b
                var poly = new List<V2> { a };
                poly.AddRange(kerb);
                poly.Add(b);
                double cut = Math.Abs(BoxPaintGeometry.SignedArea(poly));
                if (cut < bestArea)
                {
                    bestArea = cut;
                    depth = deep;
                    pc = V3.FromXZ(a, p3.Y);
                    qc = V3.FromXZ(b, q3.Y);
                }
            }
            if (bestArea < double.MaxValue) { area = bestArea; return true; }
            why = bestWhy ?? "no straight cut meets both mouth lines";
            return false;
        }

        /// <summary>0.4.8 (JR-T-014b): no point of kerb chain c lies more than KerbTolerance inside both mouth lines
        /// (origins o1/o2, inward normals n1/n2), so the box corner where the lines meet is on the asphalt.</summary>
        private static bool NoKerbInside(Chain c, V2 o1, V2 n1, V2 o2, V2 n2)
        {
            foreach (var p in c.P) if (V2.Dot(p.XZ - o1, n1) > KerbTolerance && V2.Dot(p.XZ - o2, n2) > KerbTolerance) return false;
            return true;
        }

        /// <summary>Where the line o1 + t d1 meets the line o2 + u d2 (false when parallel).</summary>
        private static bool LineHit(V2 o1, V2 d1, V2 o2, V2 d2, out V2 x)
        {
            double den = V2.Cross(d1, d2);
            x = o1;
            if (Math.Abs(den) < 1e-9) return false;
            double t = V2.Cross(o2 - o1, d2) / den;
            x = o1 + d1 * t;
            return true;
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
