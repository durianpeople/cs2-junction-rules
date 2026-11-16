using System;
using System.Collections.Generic;

namespace JunctionRules.Core
{
    // 0.4.4-probe (JR-T-011 part A, owner 16 Nov 2026): boxes that overlap or touch are drawn as ONE box: one outer
    // border, one hatch anchored once for the group, no internal borders. Storage is unchanged (each box is its own saved
    // record); this is draw-time geometry only, recomputed when a box changes. Game-independent, unit-tested
    // (tests BoxMergeTests).
    //
    // Pipeline (Merge):
    // 1. Snap: a vertex within SnapTolerance of a vertex / side of an earlier box moves onto it, so boxes built
    //    separately that "touch" (a few cm apart or overlapping by float noise) share their sides exactly.
    // 2. Bridge (junction box + exit box only): a side of the exit box facing a side of the junction box, anti-parallel
    //    within BridgeAngleDeg and at most JoinGap apart along at least BridgeMinOverlap, gets the strip between them
    //    added (a quad). This joins an exit box that stops 'marking margin' short of a priority mouth to the junction box
    //    there; a stop line or zebra between them leaves a gap of at least 2 x margin + its painted width, which is wider
    //    than JoinGap (= margin + 0.1 m), so a marking is never painted over.
    // 3. Group: union-find over pairs that touch (sides intersect or touch, or one contains a vertex of the other) or are
    //    bridged. Never across a median: two exit boxes on the same road edge whose carriageway sets are disjoint
    //    (Core/MedianSplit) never join, whatever the distance.
    // 4. Union per group (straight-sided polygons in, straight-sided out): every side is split at every crossing with
    //    another member's side and at every member vertex lying on it; a piece stays on the union boundary when the
    //    point just to its right (outside its own box) is outside every member; coincident pieces are kept once. The
    //    pieces are chained into rings (at a vertex shared by two loops the sharpest left turn is taken, so two boxes
    //    touching at one corner give two rings of the same group). Heights: original vertices keep theirs, split points
    //    interpolate along their side.
    // 5. Checks: every ring simple and counter-clockwise, no holes, union area between the largest member and the sum of
    //    the members (+ bridges). A failed check draws that group's boxes separately as before (logged note).
    // The union of boxes that are each on the carriageway (kerb, zebra, marking-margin rules) stays on the carriageway;
    // only a bridge adds area, and only the strip between two facing box sides.
    // Hatch anchoring: one rectangle for the whole group (MinAreaRect over every ring point): every ring of the group is
    // hatched with that rectangle's origin, directions and spacing (BoxPaintGeometry.BuildPieces anchor overload).

    public enum BoxMergeKind { Junction = 0, Exit = 1 }

    /// <summary>One box as drawn alone: its outline (CCW, world metres) and who it is.</summary>
    public sealed class BoxMergeInput
    {
        /// <summary>Opaque identity (entity key) and sub-identity (exit key; 0 for a junction).</summary>
        public long Key, Sub;
        public BoxMergeKind Kind;
        public List<V3> Outline;
        /// <summary>Exit boxes: the road edge key (0 = unknown).</summary>
        public long Edge;
        /// <summary>Exit boxes: carriageways covered (MedianSplit.AllCarriageways when undivided).</summary>
        public uint Carriageways = MedianSplit.AllCarriageways;

        public BoxMergeInput() { }
        public BoxMergeInput(long key, long sub, BoxMergeKind kind, List<V3> outline, long edge = 0, uint carriageways = MedianSplit.AllCarriageways)
        {
            Key = key; Sub = sub; Kind = kind; Outline = outline; Edge = edge; Carriageways = carriageways;
        }

        public bool SameBox(long key, long sub) => Key == key && Sub == sub;

        /// <summary>Entity key (index &lt;&lt; 32 | version), as SortedKeys.KeyOf.</summary>
        public static long EntityKey(int index, int version) => SortedKeys.KeyOf(index, version);

        /// <summary>Sub-identity of a building's exit box (its saved key packed in 40 bits, never 0); 0 = junction box.</summary>
        public static long ExitSub(ExitKey k) => 1L + ((long)k.Kind << 32 | (long)(ushort)k.X10 << 16 | (ushort)k.Z10);
    }

    public sealed class BoxMergeParams
    {
        /// <summary>Vertices / sides closer than this are one (metres).</summary>
        public double SnapTolerance = 0.05;
        /// <summary>Largest gap a junction-exit bridge spans (metres); the paint uses marking margin + 0.1 m.</summary>
        public double JoinGap = 0.6;
        public double BridgeAngleDeg = 3.0;
        public double BridgeMinOverlap = 1.0;

        public static BoxMergeParams ForMargin(double markingMargin) => new BoxMergeParams { JoinGap = Math.Max(0, markingMargin) + 0.1 };
    }

    /// <summary>
    /// One drawn box: one or more members. Rings = the world-space outlines to paint (CCW, closed implicitly), already
    /// median-clipped (the exit boxes are) and merged. Anchor = the hatch rectangle shared by every ring.
    /// </summary>
    public sealed class MergedBoxGroup
    {
        /// <summary>Indices into the Merge input list.</summary>
        public List<int> Members = new List<int>();
        /// <summary>True when the rings are a union of 2+ boxes (false: one box, or a failed union drawn separately).</summary>
        public bool Merged;
        public List<List<V3>> Rings = new List<List<V3>>();
        public BoxRect Anchor;
        public int Bridges;
        public List<string> Notes = new List<string>();

        public bool Contains(IList<BoxMergeInput> inputs, long key, long sub)
        {
            foreach (int m in Members) if (inputs[m].SameBox(key, sub)) return true;
            return false;
        }
    }

    public static class BoxMerge
    {
        private const double Eps = 1e-9;
        private const double OnSegTol = 1e-5;
        private const double SideProbe = 1e-3;

        /// <summary>
        /// THE grouping API (paint, hover and part B's marking mask read it): boxes in, drawn groups out. Inputs with no
        /// usable outline are left out. Each group's Rings are the final world-space polygons.
        /// </summary>
        public static List<MergedBoxGroup> Merge(IList<BoxMergeInput> boxes, BoxMergeParams prm)
        {
            prm = prm ?? new BoxMergeParams();
            var groups = new List<MergedBoxGroup>();
            if (boxes == null) return groups;
            int n = boxes.Count;
            var rings = new List<V3>[n];
            var bounds = new double[n][];
            for (int i = 0; i < n; i++)
            {
                List<V3> r = Clean(boxes[i]?.Outline);
                if (r == null) continue;
                rings[i] = r;
            }
            // 1. snap (later boxes onto earlier ones)
            for (int i = 0; i < n; i++)
            {
                if (rings[i] == null) continue;
                for (int j = 0; j < i; j++)
                {
                    if (rings[j] == null || !MayJoin(boxes[i], boxes[j])) continue;
                    if (!BoundsNear(rings[i], rings[j], prm.SnapTolerance + 1e-6)) continue;
                    List<V3> s = Snap(rings[i], rings[j], prm.SnapTolerance);
                    if (s != null) rings[i] = s;
                }
            }
            for (int i = 0; i < n; i++) if (rings[i] != null) bounds[i] = Bounds(rings[i]);
            // 2-3. touch / bridge, union-find
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            var bridges = new List<(int a, int b, List<V3> quad)>();
            for (int i = 0; i < n; i++)
            {
                if (rings[i] == null) continue;
                for (int j = i + 1; j < n; j++)
                {
                    if (rings[j] == null || !MayJoin(boxes[i], boxes[j])) continue;
                    double reach = Math.Max(prm.SnapTolerance, (boxes[i].Kind != boxes[j].Kind ? prm.JoinGap : 0)) + 1e-6;
                    if (!BoxesNear(bounds[i], bounds[j], reach)) continue;
                    if (Touch(rings[i], rings[j])) { Union(parent, i, j); continue; }
                    if (boxes[i].Kind != boxes[j].Kind)
                    {
                        int ji = boxes[i].Kind == BoxMergeKind.Junction ? i : j, ei = ji == i ? j : i;
                        List<V3> quad = Bridge(rings[ji], rings[ei], prm);
                        if (quad != null) { bridges.Add((ji, ei, quad)); Union(parent, i, j); }
                    }
                }
            }
            var byRoot = new Dictionary<int, MergedBoxGroup>();
            for (int i = 0; i < n; i++)
            {
                if (rings[i] == null) continue;
                int root = Find(parent, i);
                if (!byRoot.TryGetValue(root, out MergedBoxGroup g)) { g = new MergedBoxGroup(); byRoot[root] = g; groups.Add(g); }
                g.Members.Add(i);
            }
            var result = new List<MergedBoxGroup>(groups.Count);
            foreach (MergedBoxGroup g in groups)
            {
                if (g.Members.Count == 1)
                {
                    g.Rings.Add(new List<V3>(rings[g.Members[0]]));
                    g.Anchor = AnchorOf(g.Rings);
                    result.Add(g);
                    continue;
                }
                var polys = new List<List<V3>>();
                double maxArea = 0, sum = 0;
                foreach (int m in g.Members) { polys.Add(rings[m]); double a = BoxPaintGeometry.SignedArea(rings[m]); maxArea = Math.Max(maxArea, a); sum += a; }
                foreach (var b in bridges)
                {
                    if (Find(parent, b.a) != Find(parent, g.Members[0])) continue;
                    polys.Add(b.quad);
                    sum += BoxPaintGeometry.SignedArea(b.quad);
                    g.Bridges++;
                }
                List<List<V3>> u = UnionRings(polys, out string why);
                if (u != null)
                {
                    double ua = 0;
                    foreach (var r in u) ua += BoxPaintGeometry.SignedArea(r);
                    if (ua < maxArea - 1e-3 - 1e-6 * maxArea || ua > sum + 1e-3 + 1e-6 * sum) { why = $"union area {ua:0.###} m2 outside [{maxArea:0.###}, {sum:0.###}]"; u = null; }
                }
                if (u == null)
                {
                    // drawn separately, as 0.4.3
                    foreach (int m in g.Members)
                    {
                        var single = new MergedBoxGroup();
                        single.Members.Add(m);
                        single.Rings.Add(rings[m]);
                        single.Anchor = AnchorOf(single.Rings);
                        single.Notes.Add($"merge with {g.Members.Count - 1} touching box(es) failed ({why}); drawn separately");
                        result.Add(single);
                    }
                    continue;
                }
                g.Merged = true;
                g.Rings = u;
                g.Anchor = AnchorOf(u);
                if (g.Bridges > 0) g.Notes.Add($"{g.Bridges} junction-exit gap(s) bridged");
                result.Add(g);
            }
            return result;
        }

        /// <summary>The group (index into 'groups') holding input (key, sub); -1 when none.</summary>
        public static int GroupOf(IList<MergedBoxGroup> groups, IList<BoxMergeInput> inputs, long key, long sub)
        {
            for (int g = 0; g < groups.Count; g++) if (groups[g].Contains(inputs, key, sub)) return g;
            return -1;
        }

        /// <summary>Two boxes may join: never two exit boxes on the same road edge with disjoint carriageways (a median between them).</summary>
        public static bool MayJoin(BoxMergeInput a, BoxMergeInput b)
        {
            if (a == null || b == null) return false;
            if (a.Kind == BoxMergeKind.Exit && b.Kind == BoxMergeKind.Exit && a.Edge != 0 && a.Edge == b.Edge && (a.Carriageways & b.Carriageways) == 0) return false;
            return true;
        }

        public static BoxRect AnchorOf(List<List<V3>> rings)
        {
            var pts = new List<V2>();
            foreach (var r in rings) foreach (var p in r) pts.Add(p.XZ);
            return BoxPaintGeometry.MinAreaRect(pts);
        }

        // ------------------------------------------------------------------ helpers

        private static int Find(int[] p, int i) { while (p[i] != i) { p[i] = p[p[i]]; i = p[i]; } return i; }
        private static void Union(int[] p, int a, int b) { a = Find(p, a); b = Find(p, b); if (a != b) p[Math.Max(a, b)] = Math.Min(a, b); }

        /// <summary>CCW copy without repeated / non-finite points; null when fewer than 3 points or no area.</summary>
        private static List<V3> Clean(List<V3> ring)
        {
            if (ring == null || ring.Count < 3) return null;
            var r = new List<V3>(ring.Count);
            foreach (V3 p in ring)
            {
                if (!CarriagewayOutline.IsFinite(p)) return null;
                if (r.Count > 0 && V2.Distance(r[r.Count - 1].XZ, p.XZ) < 1e-7) continue;
                r.Add(p);
            }
            while (r.Count > 1 && V2.Distance(r[0].XZ, r[r.Count - 1].XZ) < 1e-7) r.RemoveAt(r.Count - 1);
            if (r.Count < 3) return null;
            double a = BoxPaintGeometry.SignedArea(r);
            if (Math.Abs(a) < 1e-6) return null;
            if (a < 0) r.Reverse();
            return r;
        }

        private static double[] Bounds(List<V3> r)
        {
            double x0 = double.MaxValue, x1 = double.MinValue, z0 = double.MaxValue, z1 = double.MinValue;
            foreach (V3 p in r) { x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X); z0 = Math.Min(z0, p.Z); z1 = Math.Max(z1, p.Z); }
            return new[] { x0, x1, z0, z1 };
        }

        private static bool BoxesNear(double[] a, double[] b, double d) => !(a[0] > b[1] + d || b[0] > a[1] + d || a[2] > b[3] + d || b[2] > a[3] + d);

        private static bool BoundsNear(List<V3> a, List<V3> b, double d) => BoxesNear(Bounds(a), Bounds(b), d);

        /// <summary>Ring a with its vertices snapped onto b's vertices, then b's sides (within tol); null when nothing moved or the result is not simple.</summary>
        private static List<V3> Snap(List<V3> a, List<V3> b, double tol)
        {
            var r = new List<V3>(a);
            bool moved = false;
            for (int i = 0; i < r.Count; i++)
            {
                V2 p = r[i].XZ;
                double best = tol;
                int bi = -1;
                for (int k = 0; k < b.Count; k++)
                {
                    double d = V2.Distance(p, b[k].XZ);
                    if (d <= best && d > 0) { best = d; bi = k; }
                }
                if (bi >= 0) { r[i] = new V3(b[bi].X, r[i].Y, b[bi].Z); moved = true; continue; }
                for (int k = 0; k < b.Count; k++)
                {
                    V2 s0 = b[k].XZ, s1 = b[(k + 1) % b.Count].XZ;
                    V2 d = s1 - s0;
                    double len2 = V2.Dot(d, d);
                    if (len2 < 1e-12) continue;
                    double t = V2.Dot(p - s0, d) / len2;
                    if (t <= 0 || t >= 1) continue;
                    V2 q = s0 + d * t;
                    double dist = V2.Distance(p, q);
                    if (dist > 0 && dist <= tol) { r[i] = new V3(q.X, r[i].Y, q.Z); moved = true; break; }
                }
            }
            if (!moved) return null;
            List<V3> c = Clean(r);
            if (c == null || !BoxPaintGeometry.IsSimple(BoxPaintGeometry.ToXZ(c))) return null;
            return c;
        }

        /// <summary>The two rings' sides cross or touch, or one holds a vertex of the other.</summary>
        public static bool Touch(List<V3> a, List<V3> b)
        {
            for (int i = 0; i < a.Count; i++)
            {
                V2 p1 = a[i].XZ, p2 = a[(i + 1) % a.Count].XZ;
                for (int k = 0; k < b.Count; k++)
                {
                    V2 q1 = b[k].XZ, q2 = b[(k + 1) % b.Count].XZ;
                    if (BoxPaintGeometry.SegmentIntersect(p1, p2, q1, q2, out _, out _)) return true;
                    if (BoxPaintGeometry.DistanceToSegment(q1, p1, p2) <= OnSegTol || BoxPaintGeometry.DistanceToSegment(p1, q1, q2) <= OnSegTol) return true;
                }
            }
            var ax = BoxPaintGeometry.ToXZ(a);
            var bx = BoxPaintGeometry.ToXZ(b);
            return BoxPaintGeometry.Contains(ax, bx[0]) || BoxPaintGeometry.Contains(bx, ax[0]);
        }

        /// <summary>
        /// The strip between a junction-box side and an exit-box side facing it (anti-parallel, at most JoinGap apart, the
        /// exit side outside the junction box), over the length both share; null when no side pair qualifies.
        /// </summary>
        public static List<V3> Bridge(List<V3> junction, List<V3> exit, BoxMergeParams prm)
        {
            double cosMax = Math.Cos(prm.BridgeAngleDeg * Math.PI / 180.0);
            List<V3> best = null;
            double bestLen = 0;
            for (int i = 0; i < junction.Count; i++)
            {
                V3 j0 = junction[i], j1 = junction[(i + 1) % junction.Count];
                V2 dj = j1.XZ - j0.XZ;
                double lj = dj.Length;
                if (lj < prm.BridgeMinOverlap) continue;
                V2 uj = dj * (1.0 / lj), nj = uj.Left;   // nj points into the junction box
                for (int k = 0; k < exit.Count; k++)
                {
                    V3 e0 = exit[k], e1 = exit[(k + 1) % exit.Count];
                    V2 de = e1.XZ - e0.XZ;
                    double le = de.Length;
                    if (le < prm.BridgeMinOverlap) continue;
                    if (V2.Dot(uj, de * (1.0 / le)) > -cosMax) continue;
                    // the exit side lies outside, within JoinGap of the junction side line (both ends)
                    double g0 = -V2.Dot(e0.XZ - j0.XZ, nj), g1 = -V2.Dot(e1.XZ - j0.XZ, nj);
                    if (g0 < -1e-6 || g1 < -1e-6 || g0 > prm.JoinGap + 1e-6 || g1 > prm.JoinGap + 1e-6) continue;
                    // overlap along the junction side
                    double s0 = V2.Dot(e1.XZ - j0.XZ, uj), s1 = V2.Dot(e0.XZ - j0.XZ, uj);   // exit side runs the other way
                    double lo = Math.Max(0, s0), hi = Math.Min(lj, s1);
                    if (hi - lo < prm.BridgeMinOverlap || hi - lo <= bestLen) continue;
                    // the four corners: on the junction side at lo / hi, and their feet on the exit side
                    V3 a = Lerp3(j0, j1, lo / lj), b = Lerp3(j0, j1, hi / lj);
                    double te = 0;
                    V3 c = FootOn(e0, e1, b.XZ, ref te), d = FootOn(e0, e1, a.XZ, ref te);
                    var quad = new List<V3> { a, d, c, b };
                    double area = BoxPaintGeometry.SignedArea(quad);
                    if (Math.Abs(area) < 1e-6) continue;
                    if (area < 0) quad.Reverse();
                    best = quad;
                    bestLen = hi - lo;
                }
            }
            return best;
        }

        private static V3 Lerp3(V3 a, V3 b, double t) => new V3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

        private static V3 FootOn(V3 s0, V3 s1, V2 p, ref double t)
        {
            V2 d = s1.XZ - s0.XZ;
            double len2 = V2.Dot(d, d);
            t = len2 > 1e-12 ? Math.Max(0, Math.Min(1, V2.Dot(p - s0.XZ, d) / len2)) : 0;
            return Lerp3(s0, s1, t);
        }

        // ------------------------------------------------------------------ union

        private struct Piece { public V3 A, B; }

        /// <summary>Union of simple CCW rings (see the header, step 4). Null (with why) when the result has a hole or a ring is not simple.</summary>
        public static List<List<V3>> UnionRings(IList<List<V3>> polys, out string why)
        {
            why = null;
            var xz = new List<List<V2>>(polys.Count);
            foreach (var p in polys) xz.Add(BoxPaintGeometry.ToXZ(p));
            var pieces = new List<Piece>();
            for (int pi = 0; pi < polys.Count; pi++)
            {
                List<V3> P = polys[pi];
                for (int i = 0; i < P.Count; i++)
                {
                    V3 a = P[i], b = P[(i + 1) % P.Count];
                    V2 ax = a.XZ, bx = b.XZ;
                    double len = V2.Distance(ax, bx);
                    if (len < 1e-9) continue;
                    var ts = new List<double> { 0, 1 };
                    for (int qi = 0; qi < polys.Count; qi++)
                    {
                        if (qi == pi) continue;
                        List<V2> Q = xz[qi];
                        for (int k = 0; k < Q.Count; k++)
                        {
                            V2 q1 = Q[k], q2 = Q[(k + 1) % Q.Count];
                            if (BoxPaintGeometry.SegmentIntersect(ax, bx, q1, q2, out double t, out _)) ts.Add(t);
                            AddOnSeg(ts, ax, bx, len, q1);
                        }
                    }
                    ts.Sort();
                    for (int k = 0; k + 1 < ts.Count; k++)
                    {
                        double t0 = Math.Max(0, Math.Min(1, ts[k])), t1 = Math.Max(0, Math.Min(1, ts[k + 1]));
                        if ((t1 - t0) * len < 1e-6) continue;
                        V3 p0 = Lerp3(a, b, t0), p1 = Lerp3(a, b, t1);
                        V2 m = (p0.XZ + p1.XZ) * 0.5;
                        V2 right = (p1.XZ - p0.XZ).Normalized.Left * -1;
                        if (InsideAny(xz, m + right * SideProbe)) continue;   // the outside of this side is covered: interior
                        if (!InsideAny(xz, m - right * SideProbe)) continue;  // degenerate sliver
                        bool dup = false;
                        foreach (Piece q in pieces) if (Near(q.A, p0) && Near(q.B, p1)) { dup = true; break; }
                        if (!dup) pieces.Add(new Piece { A = p0, B = p1 });
                    }
                }
            }
            if (pieces.Count < 3) { why = "no union boundary"; return null; }
            // chain
            var used = new bool[pieces.Count];
            var rings = new List<List<V3>>();
            for (int s = 0; s < pieces.Count; s++)
            {
                if (used[s]) continue;
                var ring = new List<V3>();
                int cur = s;
                int guard = 0;
                while (true)
                {
                    used[cur] = true;
                    ring.Add(pieces[cur].A);
                    V3 end = pieces[cur].B;
                    if (Near(end, pieces[s].A) && ring.Count >= 2)
                    {
                        // closed, unless another unused piece also leaves here and the loop is not done (handled by left-most turn)
                        break;
                    }
                    V2 din = (pieces[cur].B.XZ - pieces[cur].A.XZ).Normalized;
                    int next = -1;
                    double bestTurn = double.MinValue;
                    for (int k = 0; k < pieces.Count; k++)
                    {
                        if (used[k] || !Near(pieces[k].A, end)) continue;
                        V2 dout = (pieces[k].B.XZ - pieces[k].A.XZ).Normalized;
                        double turn = Math.Atan2(V2.Cross(din, dout), V2.Dot(din, dout));
                        if (turn > bestTurn) { bestTurn = turn; next = k; }
                    }
                    if (next < 0) { why = "union boundary does not close"; return null; }
                    cur = next;
                    if (++guard > pieces.Count + 1) { why = "union boundary loops"; return null; }
                }
                List<V3> c = Simplify(ring);
                if (c == null) continue;
                rings.Add(c);
            }
            foreach (var r in rings)
            {
                double a = BoxPaintGeometry.SignedArea(r);
                if (a < 0) { why = "boxes enclose a hole"; return null; }
                if (!BoxPaintGeometry.IsSimple(BoxPaintGeometry.ToXZ(r))) { why = "merged outline is not simple"; return null; }
            }
            if (rings.Count == 0) { why = "no ring"; return null; }
            return rings;
        }

        private static void AddOnSeg(List<double> ts, V2 a, V2 b, double len, V2 q)
        {
            V2 d = b - a;
            double t = V2.Dot(q - a, d) / (len * len);
            if (t <= 0 || t >= 1) return;
            if (V2.Distance(a + d * t, q) <= OnSegTol) ts.Add(t);
        }

        private static bool Near(V3 a, V3 b) => Math.Abs(a.X - b.X) < 1e-5 && Math.Abs(a.Z - b.Z) < 1e-5;

        private static bool InsideAny(List<List<V2>> polys, V2 p)
        {
            foreach (var q in polys) if (BoxPaintGeometry.Contains(q, p, 1e-9)) return true;
            return false;
        }

        /// <summary>Drops points that do not turn (collinear runs) and near-duplicates; null when less than 3 points / no area remain.</summary>
        private static List<V3> Simplify(List<V3> ring)
        {
            var r = new List<V3>(ring);
            bool changed = true;
            while (changed && r.Count >= 3)
            {
                changed = false;
                for (int i = 0; i < r.Count && r.Count >= 3; i++)
                {
                    V2 a = r[(i + r.Count - 1) % r.Count].XZ, b = r[i].XZ, c = r[(i + 1) % r.Count].XZ;
                    V2 d1 = b - a, d2 = c - b;
                    if (d1.Length < 1e-6 || d2.Length < 1e-6) { r.RemoveAt(i); changed = true; break; }
                    double cross = V2.Cross(d1.Normalized, d2.Normalized);
                    if (Math.Abs(cross) < 1e-7 && V2.Dot(d1, d2) > 0) { r.RemoveAt(i); changed = true; break; }
                }
            }
            if (r.Count < 3 || Math.Abs(BoxPaintGeometry.SignedArea(r)) < 1e-4) return null;
            return r;
        }
    }

    /// <summary>
    /// 0.4.4: the boxes as last drawn (main thread only): the paint system publishes its inputs and groups after every
    /// regroup (only when boxes change); the tool's hover merges against them, and other draw-time users (JR-T-011b's
    /// marking mask) read Groups[i].Rings. Version bumps on every publish.
    /// </summary>
    public static class MergedBoxes
    {
        private static List<BoxMergeInput> s_Inputs = new List<BoxMergeInput>();
        private static List<MergedBoxGroup> s_Groups = new List<MergedBoxGroup>();

        public static int Version { get; private set; }
        public static IReadOnlyList<BoxMergeInput> Inputs => s_Inputs;
        public static IReadOnlyList<MergedBoxGroup> Groups => s_Groups;
        /// <summary>The merge parameters the groups were made with.</summary>
        public static BoxMergeParams Params { get; private set; } = new BoxMergeParams();

        public static void Publish(List<BoxMergeInput> inputs, List<MergedBoxGroup> groups, BoxMergeParams prm)
        {
            s_Inputs = inputs ?? new List<BoxMergeInput>();
            s_Groups = groups ?? new List<MergedBoxGroup>();
            Params = prm ?? new BoxMergeParams();
            Version++;
        }

        public static void Clear() => Publish(null, null, Params);

        /// <summary>
        /// The hover outline of a target: the rings of the group it belongs to (it has a box) or would join (adding one):
        /// the drawn boxes without the target, plus the target's own outline, merged. Just the target's outline when it
        /// touches nothing.
        /// </summary>
        public static List<List<V3>> HoverRings(BoxMergeInput target)
        {
            var r = new List<List<V3>>();
            if (target?.Outline == null) return r;
            var inputs = new List<BoxMergeInput>();
            foreach (BoxMergeInput b in s_Inputs) if (!b.SameBox(target.Key, target.Sub)) inputs.Add(b);
            inputs.Add(target);
            List<MergedBoxGroup> groups = BoxMerge.Merge(inputs, Params);
            int g = BoxMerge.GroupOf(groups, inputs, target.Key, target.Sub);
            if (g < 0) { r.Add(target.Outline); return r; }
            return groups[g].Rings;
        }
    }
}
