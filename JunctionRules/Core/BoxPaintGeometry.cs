using System;
using System.Collections.Generic;

namespace JunctionRules.Core
{
    // Stage 3 probe (0.3.0-probe, JR-T-003b): game-independent geometry of a UK yellow box junction marking
    // (TSRGD 2016 diagram 1043; Traffic Signs Manual chapter 5 (2018), section 8.4 and figures 8-1 / 8-6).
    // Draw-only: the mod feeds the pieces made here to the game's surface shader (option E of
    // docs/STAGE3-PAINT-RESEARCH.md). Nothing here touches the game; unit-tested in BoxPaintTests.
    //
    // Conventions: plan coordinates are (X, Z) like the game's xz; Y is height. Rings are closed implicitly (last point
    // joins the first) and counter-clockwise in (X, Z) with the standard shoelace sign (the game's
    // Game.Areas.GeometrySystem.Area() > 0, i.e. AreaFlags.CounterClockwise). Left(v) = (-v.Z, v.X) like
    // Colossal.Mathematics.MathUtils.Left, so the inside of a CCW ring is on the left of each side.

    /// <summary>What the paint test draws (setting "Yellow box paint test: what to draw").</summary>
    public enum BoxPaintMode
    {
        /// <summary>Border strip plus criss-cross diagonals clipped to the outline (the real marking).</summary>
        Hatch = 0,
        /// <summary>Whole outline filled with plain colour (isolates texture/UV questions from geometry).</summary>
        PlainFill = 1,
        /// <summary>Border strip only (checks the outline against the kerbs and mouths).</summary>
        OutlineOnly = 2,
    }

    /// <summary>Decal layers the paint goes onto (game's Game.Rendering.DecalLayers: Terrain 1, Roads 2, Buildings 4, Vehicles 8, Creatures 16, Other 32).</summary>
    public enum BoxPaintLayers
    {
        Roads = 2,
        RoadsAndOther = 34,
        /// <summary>Terrain + Roads + Buildings + Other (39, what G87 UK Junction Box uses). Debug comparison only.</summary>
        AllButVehicles = 39,
    }

    public struct V2
    {
        public double X, Z;
        public V2(double x, double z) { X = x; Z = z; }
        public static V2 operator +(V2 a, V2 b) => new V2(a.X + b.X, a.Z + b.Z);
        public static V2 operator -(V2 a, V2 b) => new V2(a.X - b.X, a.Z - b.Z);
        public static V2 operator *(V2 a, double s) => new V2(a.X * s, a.Z * s);
        public static V2 operator *(double s, V2 a) => new V2(a.X * s, a.Z * s);
        public static double Dot(V2 a, V2 b) => a.X * b.X + a.Z * b.Z;
        /// <summary>z-component of a x b in the (X, Z) plane; &gt; 0 when b is counter-clockwise from a.</summary>
        public static double Cross(V2 a, V2 b) => a.X * b.Z - a.Z * b.X;
        public double Length => Math.Sqrt(X * X + Z * Z);
        public V2 Normalized { get { double l = Length; return l > 1e-12 ? new V2(X / l, Z / l) : new V2(0, 0); } }
        public V2 Left => new V2(-Z, X);
        public static double Distance(V2 a, V2 b) => (a - b).Length;
        public override string ToString() => $"({X:0.###}, {Z:0.###})";
    }

    public struct V3
    {
        public double X, Y, Z;
        public V3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public V2 XZ => new V2(X, Z);
        public static V3 FromXZ(V2 p, double y) => new V3(p.X, y, p.Z);
        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
    }

    /// <summary>
    /// One connected edge's slice of the node, reduced to its two carriageway kerb lines (already inset from the outer
    /// edge of the footway). Both lines start at the arm mouth (index 0, the .a end of the game's EdgeNodeGeometry
    /// curves) and run toward the node centre.
    /// </summary>
    public sealed class ArmSlice
    {
        public List<V3> LeftKerb = new List<V3>();
        public List<V3> RightKerb = new List<V3>();
        public ArmSlice() { }
        public ArmSlice(List<V3> left, List<V3> right) { LeftKerb = left; RightKerb = right; }
    }

    /// <summary>Paint dimensions. Defaults: TSM chapter 5 (2018) section 8.4.1 and figure 8-1 (see PROBE-STAGE3-PAINT.md).</summary>
    public sealed class BoxPaintParams
    {
        /// <summary>Boundary line width, metres (figure 8-1: 200 mm).</summary>
        public double BorderWidth = 0.20;
        /// <summary>Diagonal line width, metres (8.4.1: 150 mm).</summary>
        public double LineWidth = 0.15;
        /// <summary>Spacing between parallel diagonals, metres, measured square to the lines. 0 = TSM rule: about 2 m when the shortest side of the box is under 9 m, else 2.5 m (8.4.1).</summary>
        public double Spacing = 0;
        /// <summary>Angle of the diagonals to the box axis (long side of the smallest rectangle round the box), degrees. The two families are at +angle and -angle (45 = 90 deg between them, the aim in 8.2.4).</summary>
        public double AngleDeg = 45;
        /// <summary>Extra inset from the kerb line into the carriageway, metres (on top of the footway inset).</summary>
        public double KerbInset = 0;
        /// <summary>Mitre limit for border corners, in border widths.</summary>
        public double MitreLimit = 4;
        /// <summary>Points closer than this along a straight run are dropped from the outline (metres).</summary>
        public double StraightenTolerance = 0.05;
        /// <summary>Hard cap on diagonals per family (keeps a mistaken huge box cheap).</summary>
        public int MaxLinesPerFamily = 200;
        /// <summary>0.4.0: length along the road of an entrance box (in front of a building's access), metres.</summary>
        public double EntranceLength = 8.0;
        /// <summary>
        /// 0.4.2: paint list "markingMargin" (metres clear of road markings and, for entrance boxes, of junction mouths).
        /// NaN = not in the file: the Options value applies (MarkingMargin.Effective).
        /// </summary>
        public double MarkingMargin = double.NaN;

        public double EffectiveSpacing(double shortestSide)
        {
            if (Spacing > 0) return Spacing;
            return shortestSide < 9.0 ? 2.0 : 2.5;
        }

        /// <summary>Clamps every value into a safe range; returns the names of values that were changed.</summary>
        public List<string> Clamp()
        {
            var changed = new List<string>();
            BorderWidth = ClampOne(BorderWidth, 0.02, 2.0, 0.20, nameof(BorderWidth), changed);
            LineWidth = ClampOne(LineWidth, 0.02, 2.0, 0.15, nameof(LineWidth), changed);
            if (Spacing != 0) Spacing = ClampOne(Spacing, 0.5, 20.0, 0, nameof(Spacing), changed);
            AngleDeg = ClampOne(AngleDeg, 5.0, 85.0, 45, nameof(AngleDeg), changed);
            KerbInset = ClampOne(KerbInset, -3.0, 10.0, 0, nameof(KerbInset), changed);
            MitreLimit = ClampOne(MitreLimit, 1.0, 20.0, 4, nameof(MitreLimit), changed);
            StraightenTolerance = ClampOne(StraightenTolerance, 0.0, 1.0, 0.05, nameof(StraightenTolerance), changed);
            EntranceLength = ClampOne(EntranceLength, 2.0, 40.0, 8.0, nameof(EntranceLength), changed);
            if (!double.IsNaN(MarkingMargin)) MarkingMargin = ClampOne(MarkingMargin, Core.MarkingMargin.Min, Core.MarkingMargin.Max, Core.MarkingMargin.Default, nameof(MarkingMargin), changed);
            if (MaxLinesPerFamily < 1 || MaxLinesPerFamily > 2000) { MaxLinesPerFamily = 200; changed.Add(nameof(MaxLinesPerFamily)); }
            return changed;
        }

        private static double ClampOne(double v, double lo, double hi, double fallback, string name, List<string> changed)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) { changed.Add(name); return fallback; }
            if (v < lo) { changed.Add(name); return lo; }
            if (v > hi) { changed.Add(name); return hi; }
            return v;
        }

        public BoxPaintParams Clone() => (BoxPaintParams)MemberwiseClone();
    }

    /// <summary>One painted piece: a simple polygon (CCW) with its own triangulation (triples of ring indices).</summary>
    public sealed class PaintPiece
    {
        public List<V3> Ring = new List<V3>();
        public List<int> Triangles = new List<int>();
        public PaintPieceKind Kind;
    }

    public enum PaintPieceKind { Border, Hatch, Fill }

    /// <summary>Smallest-area rectangle round a ring: Origin is the corner at (min U, min V); U is the long side.</summary>
    public struct BoxRect
    {
        public V2 Origin, U, V;
        public double LenU, LenV;
        public V2 Corner(double u, double v) => Origin + U * u + V * v;
    }

    public sealed class BoxPaintResult
    {
        public bool Ok;
        public string Failure;
        public List<V3> Outline;
        public List<int> OutlineTriangles;
        public BoxRect Rect;
        public double SpacingUsed;
        public List<PaintPiece> Pieces = new List<PaintPiece>();
        public List<string> Warnings = new List<string>();
        public int TriangleCount { get { int n = 0; foreach (var p in Pieces) n += p.Triangles.Count / 3; return n; } }
    }

    public static class BoxPaintGeometry
    {
        private const double Eps = 1e-9;

        // ------------------------------------------------------------------ polygon basics

        public static double SignedArea(IList<V2> ring)
        {
            double a = 0;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++) a += V2.Cross(ring[j], ring[i]);
            return a * 0.5;
        }

        public static double SignedArea(IList<V3> ring)
        {
            double a = 0;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++) a += V2.Cross(ring[j].XZ, ring[i].XZ);
            return a * 0.5;
        }

        public static List<V2> ToXZ(IList<V3> ring)
        {
            var r = new List<V2>(ring.Count);
            foreach (var p in ring) r.Add(p.XZ);
            return r;
        }

        /// <summary>Point in polygon (even-odd); points within tol of the boundary count as inside.</summary>
        public static bool Contains(IList<V2> ring, V2 p, double tol = 1e-6)
        {
            if (DistanceToBoundary(ring, p) <= tol) return true;
            bool inside = false;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                V2 a = ring[i], b = ring[j];
                if ((a.Z > p.Z) != (b.Z > p.Z))
                {
                    double x = (b.X - a.X) * (p.Z - a.Z) / (b.Z - a.Z) + a.X;
                    if (p.X < x) inside = !inside;
                }
            }
            return inside;
        }

        public static double DistanceToBoundary(IList<V2> ring, V2 p)
        {
            double best = double.MaxValue;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++) best = Math.Min(best, DistanceToSegment(p, ring[j], ring[i]));
            return best;
        }

        public static double DistanceToSegment(V2 p, V2 a, V2 b)
        {
            V2 ab = b - a;
            double l2 = V2.Dot(ab, ab);
            double t = l2 < Eps ? 0 : Math.Max(0, Math.Min(1, V2.Dot(p - a, ab) / l2));
            return V2.Distance(p, a + ab * t);
        }

        /// <summary>Proper or touching intersection of segments p1p2 and q1q2; t along p, u along q.</summary>
        public static bool SegmentIntersect(V2 p1, V2 p2, V2 q1, V2 q2, out double t, out double u)
        {
            V2 r = p2 - p1, s = q2 - q1;
            double d = V2.Cross(r, s);
            t = u = 0;
            if (Math.Abs(d) < 1e-12) return false;
            V2 qp = q1 - p1;
            t = V2.Cross(qp, s) / d;
            u = V2.Cross(qp, r) / d;
            return t >= -1e-9 && t <= 1 + 1e-9 && u >= -1e-9 && u <= 1 + 1e-9;
        }

        /// <summary>True when no two non-adjacent sides cross or touch.</summary>
        public static bool IsSimple(IList<V2> ring)
        {
            int n = ring.Count;
            if (n < 3) return false;
            for (int i = 0; i < n; i++)
            {
                V2 a1 = ring[i], a2 = ring[(i + 1) % n];
                for (int j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1) continue;
                    if (SegmentIntersect(a1, a2, ring[j], ring[(j + 1) % n], out _, out _)) return false;
                }
            }
            return true;
        }

        /// <summary>Andrew's monotone chain; CCW, no collinear points.</summary>
        public static List<V2> ConvexHull(IList<V2> pts)
        {
            var p = new List<V2>(pts);
            p.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z));
            if (p.Count < 3) return p;
            var h = new List<V2>(2 * p.Count);
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                for (int k = 0; k < p.Count; k++)
                {
                    V2 q = pass == 0 ? p[k] : p[p.Count - 1 - k];
                    while (h.Count >= start + 2 && V2.Cross(h[h.Count - 1] - h[h.Count - 2], q - h[h.Count - 2]) <= 1e-12) h.RemoveAt(h.Count - 1);
                    h.Add(q);
                }
                h.RemoveAt(h.Count - 1);
            }
            return h;
        }

        /// <summary>Sutherland-Hodgman: subject (any simple polygon) clipped by a CONVEX CCW clip polygon.</summary>
        public static List<V2> ClipConvex(IList<V2> subject, IList<V2> clip)
        {
            var output = new List<V2>(subject);
            for (int i = 0; i < clip.Count && output.Count > 0; i++)
            {
                V2 a = clip[i], b = clip[(i + 1) % clip.Count];
                V2 e = b - a;
                var input = output;
                output = new List<V2>(input.Count + 2);
                for (int k = 0; k < input.Count; k++)
                {
                    V2 cur = input[k], prev = input[(k + input.Count - 1) % input.Count];
                    double dc = V2.Cross(e, cur - a), dp = V2.Cross(e, prev - a);
                    bool cin = dc >= -1e-12, pin = dp >= -1e-12;
                    if (cin)
                    {
                        if (!pin) output.Add(prev + (cur - prev) * (dp / (dp - dc)));
                        output.Add(cur);
                    }
                    else if (pin)
                    {
                        output.Add(prev + (cur - prev) * (dp / (dp - dc)));
                    }
                }
            }
            return RemoveDuplicates(output, 1e-9);
        }

        public static List<V2> RemoveDuplicates(List<V2> ring, double tol)
        {
            var r = new List<V2>(ring.Count);
            foreach (var p in ring) if (r.Count == 0 || V2.Distance(r[r.Count - 1], p) > tol) r.Add(p);
            while (r.Count > 1 && V2.Distance(r[0], r[r.Count - 1]) <= tol) r.RemoveAt(r.Count - 1);
            return r;
        }

        /// <summary>Ear clipping of a simple CCW ring; returns triples of indices into the ring.</summary>
        public static List<int> Triangulate(IList<V2> ring)
        {
            var tris = new List<int>();
            int n = ring.Count;
            if (n < 3) return tris;
            var idx = new List<int>(n);
            for (int i = 0; i < n; i++) idx.Add(i);
            int guard = 0;
            while (idx.Count > 3 && guard < n * n + 10)
            {
                guard++;
                bool clipped = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int ia = idx[(k + idx.Count - 1) % idx.Count], ib = idx[k], ic = idx[(k + 1) % idx.Count];
                    V2 a = ring[ia], b = ring[ib], c = ring[ic];
                    double cr = V2.Cross(b - a, c - a);
                    if (cr <= 1e-12)
                    {
                        // reflex or collinear: drop exactly collinear (zero-area) points, keep reflex
                        if (Math.Abs(cr) <= 1e-12 && V2.Dot(b - a, c - b) >= 0) { idx.RemoveAt(k); clipped = true; break; }
                        continue;
                    }
                    bool empty = true;
                    for (int m = 0; m < idx.Count && empty; m++)
                    {
                        int ip = idx[m];
                        if (ip == ia || ip == ib || ip == ic) continue;
                        V2 p = ring[ip];
                        if (V2.Distance(p, a) < 1e-9 || V2.Distance(p, b) < 1e-9 || V2.Distance(p, c) < 1e-9) continue;
                        if (V2.Cross(b - a, p - a) >= -1e-12 && V2.Cross(c - b, p - b) >= -1e-12 && V2.Cross(a - c, p - c) >= -1e-12) empty = false;
                    }
                    if (!empty) continue;
                    tris.Add(ia); tris.Add(ib); tris.Add(ic);
                    idx.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped) break;   // not simple / numerically stuck: caller checks the area
            }
            if (idx.Count == 3 && V2.Cross(ring[idx[1]] - ring[idx[0]], ring[idx[2]] - ring[idx[0]]) > 1e-12)
            {
                tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]);
            }
            return tris;
        }

        public static double TrianglesArea(IList<V2> ring, IList<int> tris)
        {
            double a = 0;
            for (int i = 0; i + 2 < tris.Count; i += 3) a += 0.5 * V2.Cross(ring[tris[i + 1]] - ring[tris[i]], ring[tris[i + 2]] - ring[tris[i]]);
            return a;
        }

        // ------------------------------------------------------------------ kerb lines

        /// <summary>
        /// Offsets a polyline sideways by dist (metres) toward the side where 'toward' lies, judged at the first point
        /// (the arm mouth); the side stays fixed relative to the line's direction along its length, so a kerb that bends
        /// round a corner is offset into the carriageway all the way. Vertices use the mitred normal (limited).
        /// </summary>
        public static List<V3> OffsetToward(IList<V3> line, double dist, V2 toward)
        {
            var r = new List<V3>(line.Count);
            if (line.Count < 2 || Math.Abs(dist) < 1e-12) { r.AddRange(line); return r; }
            V2 d0 = (line[1].XZ - line[0].XZ).Normalized;
            double side = V2.Cross(d0, toward - line[0].XZ) >= 0 ? 1 : -1;   // +1: toward is on the left
            for (int i = 0; i < line.Count; i++)
            {
                V2 dPrev = i > 0 ? (line[i].XZ - line[i - 1].XZ).Normalized : (line[1].XZ - line[0].XZ).Normalized;
                V2 dNext = i < line.Count - 1 ? (line[i + 1].XZ - line[i].XZ).Normalized : dPrev;
                V2 nPrev = dPrev.Left, nNext = dNext.Left;
                V2 n = (nPrev + nNext).Normalized;
                double c = V2.Dot(n, nNext);
                double scale = c > 0.25 ? 1.0 / c : 4.0;
                V2 p = line[i].XZ + n * (dist * side * scale);
                r.Add(V3.FromXZ(p, line[i].Y));
            }
            return r;
        }

        /// <summary>
        /// Drops points of an open polyline while every original point stays within tol of the simplified line
        /// (Douglas-Peucker). 0.3.5: before, each point was tested only against its current neighbours, so on a long kerb
        /// curve the removals added up (about 0.1 m off a 6 m kerb radius with tol 0.05, chords cutting onto the pavement).
        /// </summary>
        public static List<V3> Straighten(IList<V3> line, double tol)
        {
            var r = new List<V3>();
            foreach (var p in line) if (r.Count == 0 || V2.Distance(r[r.Count - 1].XZ, p.XZ) > 1e-6) r.Add(p);
            if (tol <= 0 || r.Count < 3) return r;
            var keep = new bool[r.Count];
            keep[0] = keep[r.Count - 1] = true;
            var stack = new Stack<(int a, int b)>();
            stack.Push((0, r.Count - 1));
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                double worst = -1;
                int at = -1;
                for (int i = a + 1; i < b; i++)
                {
                    double d = DistanceToSegment(r[i].XZ, r[a].XZ, r[b].XZ);
                    if (d > worst) { worst = d; at = i; }
                }
                if (at >= 0 && worst > tol)
                {
                    keep[at] = true;
                    stack.Push((a, at));
                    stack.Push((at, b));
                }
            }
            var o = new List<V3>();
            for (int i = 0; i < r.Count; i++) if (keep[i]) o.Add(r[i]);
            return o;
        }

        // ------------------------------------------------------------------ outline from arm slices

        /// <summary>
        /// Merges the per-edge node slices into one outline: arms sorted round the centre; for each arm the mouth line
        /// (one kerb's mouth point to the other's), then the kerb toward the next arm, trimmed where it meets the next
        /// arm's kerb (the corner); if they do not meet, the two inner ends are joined straight. Returns a simple CCW
        /// ring, or null with a reason.
        /// </summary>
        public static List<V3> BuildOutline(IList<ArmSlice> arms, V2 centre, double straightenTol, out string failure)
        {
            failure = null;
            if (arms == null || arms.Count < 2) { failure = "fewer than 2 arms"; return null; }
            var sorted = new List<(double angle, List<V3> entry, List<V3> exit)>();
            foreach (ArmSlice a in arms)
            {
                if (a.LeftKerb == null || a.RightKerb == null || a.LeftKerb.Count < 2 || a.RightKerb.Count < 2) { failure = "an arm has a kerb line with fewer than 2 points"; return null; }
                V2 l0 = a.LeftKerb[0].XZ - centre, r0 = a.RightKerb[0].XZ - centre;
                V2 m = (l0 + r0) * 0.5;
                // The kerb whose mouth point lies counter-clockwise of the other's leads on to the next arm.
                bool leftIsExit = V2.Cross(r0, l0) > 0;
                var exit = Straighten(leftIsExit ? a.LeftKerb : a.RightKerb, straightenTol);
                var entry = Straighten(leftIsExit ? a.RightKerb : a.LeftKerb, straightenTol);
                sorted.Add((Math.Atan2(m.Z, m.X), entry, exit));
            }
            sorted.Sort((x, y) => x.angle.CompareTo(y.angle));
            int n = sorted.Count;
            // For each corner (arm i exit -> arm i+1 entry), find where to cut both lines.
            var exitCut = new (int seg, V3 point, bool hit)[n];
            var entryCut = new (int seg, V3 point, bool hit)[n];   // indexed by the arm that owns the entry line
            for (int i = 0; i < n; i++)
            {
                var ex = sorted[i].exit;
                var en = sorted[(i + 1) % n].entry;
                bool found = false;
                for (int s = 0; s < ex.Count - 1 && !found; s++)
                {
                    for (int q = 0; q < en.Count - 1 && !found; q++)
                    {
                        if (SegmentIntersect(ex[s].XZ, ex[s + 1].XZ, en[q].XZ, en[q + 1].XZ, out double t, out double u))
                        {
                            V2 p = ex[s].XZ + (ex[s + 1].XZ - ex[s].XZ) * t;
                            double y = 0.5 * (Lerp(ex[s].Y, ex[s + 1].Y, t) + Lerp(en[q].Y, en[q + 1].Y, u));
                            V3 pt = V3.FromXZ(p, y);
                            exitCut[i] = (s, pt, true);
                            entryCut[(i + 1) % n] = (q, pt, true);
                            found = true;
                        }
                    }
                }
                if (!found)
                {
                    exitCut[i] = (ex.Count - 2, ex[ex.Count - 1], false);
                    entryCut[(i + 1) % n] = (en.Count - 2, en[en.Count - 1], false);
                }
            }
            var ring = new List<V3>();
            for (int i = 0; i < n; i++)
            {
                var en = sorted[i].entry;
                var ex = sorted[i].exit;
                // entry line from the corner back out to the mouth
                var ec = entryCut[i];
                ring.Add(ec.point);
                for (int k = ec.seg; k >= 0; k--) ring.Add(en[k]);
                // mouth: entry mouth point -> exit mouth point; then the exit line in to the corner
                var xc = exitCut[i];
                for (int k = 0; k <= xc.seg; k++) ring.Add(ex[k]);
                if (!xc.hit) ring.Add(xc.point);   // the hit point is added as the next arm's entry start
            }
            ring = CleanRing(ring, 1e-4);
            if (ring.Count < 3) { failure = "outline collapsed to fewer than 3 points"; return null; }
            if (SignedArea(ring) < 0) ring.Reverse();
            var xz = ToXZ(ring);
            if (!IsSimple(xz)) { failure = "merged outline crosses itself"; return null; }
            if (SignedArea(ring) < 0.5) { failure = "outline area under 0.5 m2"; return null; }
            return ring;
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        private static List<V3> CleanRing(List<V3> ring, double tol)
        {
            var r = new List<V3>(ring.Count);
            foreach (var p in ring) if (r.Count == 0 || V2.Distance(r[r.Count - 1].XZ, p.XZ) > tol) r.Add(p);
            while (r.Count > 1 && V2.Distance(r[0].XZ, r[r.Count - 1].XZ) <= tol) r.RemoveAt(r.Count - 1);
            // drop exact spikes / collinear points
            bool changed = true;
            while (changed && r.Count > 3)
            {
                changed = false;
                for (int i = 0; i < r.Count; i++)
                {
                    V2 a = r[(i + r.Count - 1) % r.Count].XZ, b = r[i].XZ, c = r[(i + 1) % r.Count].XZ;
                    if (Math.Abs(V2.Cross(b - a, c - a)) < 1e-9) { r.RemoveAt(i); changed = true; break; }
                }
            }
            return r;
        }

        /// <summary>Fallback outline when the merge fails: convex hull of every kerb point (logged as a warning by the caller).</summary>
        public static List<V3> HullOutline(IList<ArmSlice> arms)
        {
            var pts = new List<V3>();
            foreach (var a in arms) { pts.AddRange(a.LeftKerb); pts.AddRange(a.RightKerb); }
            var hull = ConvexHull(ToXZ(pts));
            var ring = new List<V3>(hull.Count);
            foreach (var h in hull)
            {
                double best = double.MaxValue, y = 0;
                foreach (var p in pts) { double d = V2.Distance(p.XZ, h); if (d < best) { best = d; y = p.Y; } }
                ring.Add(V3.FromXZ(h, y));
            }
            return ring;
        }

        // ------------------------------------------------------------------ box rectangle

        /// <summary>Smallest-area rectangle round the points (rotating sides of the convex hull); U = long side.</summary>
        public static BoxRect MinAreaRect(IList<V2> pts)
        {
            var hull = ConvexHull(pts);
            if (hull.Count == 0) return new BoxRect { U = new V2(1, 0), V = new V2(0, 1) };
            BoxRect best = RectFor(hull, new V2(1, 0));
            double bestArea = double.MaxValue;
            for (int i = 0; i < hull.Count; i++)
            {
                V2 u = (hull[(i + 1) % hull.Count] - hull[i]).Normalized;
                if (u.Length < 0.5) continue;
                BoxRect r = RectFor(hull, u);
                double area = r.LenU * r.LenV;
                if (area < bestArea - 1e-9) { bestArea = area; best = r; }
            }
            if (best.LenV > best.LenU + 1e-9) best = RectFor(hull, best.V);   // U = the long side
            return best;
        }

        /// <summary>Rectangle round the points with sides along u and Left(u); Origin = the (min U, min V) corner.</summary>
        public static BoxRect RectFor(IList<V2> pts, V2 u)
        {
            u = u.Normalized;
            V2 v = u.Left;
            double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
            foreach (var p in pts)
            {
                double pu = V2.Dot(p, u), pv = V2.Dot(p, v);
                minU = Math.Min(minU, pu); maxU = Math.Max(maxU, pu); minV = Math.Min(minV, pv); maxV = Math.Max(maxV, pv);
            }
            return new BoxRect { Origin = u * minU + v * minV, U = u, V = v, LenU = maxU - minU, LenV = maxV - minV };
        }

        // ------------------------------------------------------------------ pieces

        /// <summary>
        /// Builds the paint pieces for one outline (CCW, 3D). Hatch: border + diagonals; PlainFill: the outline itself;
        /// OutlineOnly: border only. Every piece lies inside the outline; heights are taken from the outline
        /// (interpolated over its triangulation).
        /// </summary>
        public static BoxPaintResult BuildPieces(List<V3> outline, BoxPaintParams prm, BoxPaintMode mode) => BuildPieces(outline, prm, mode, null);

        /// <summary>
        /// 0.4.4 (JR-T-011): with 'anchor' the hatch (origin, directions, spacing) comes from that rectangle instead of the
        /// outline's own, so every ring of a merged group (Core/BoxMerge.cs) carries one continuous pattern.
        /// </summary>
        public static BoxPaintResult BuildPieces(List<V3> outline, BoxPaintParams prm, BoxPaintMode mode, BoxRect? anchor)
        {
            var res = new BoxPaintResult { Outline = outline };
            if (outline == null || outline.Count < 3) { res.Failure = "no outline"; return res; }
            if (SignedArea(outline) < 0) outline.Reverse();
            var xz = ToXZ(outline);
            var tris = Triangulate(xz);
            double area = SignedArea(xz);
            double triArea = TrianglesArea(xz, tris);
            if (tris.Count == 0 || Math.Abs(triArea - area) > 1e-6 * Math.Max(1, area) + 1e-6)
            {
                res.Failure = $"outline triangulation failed (area {area:0.###} vs triangles {triArea:0.###})";
                return res;
            }
            res.OutlineTriangles = tris;
            res.Rect = anchor ?? MinAreaRect(xz);
            res.SpacingUsed = prm.EffectiveSpacing(Math.Min(res.Rect.LenU, res.Rect.LenV));

            if (mode == BoxPaintMode.PlainFill)
            {
                var fill = new PaintPiece { Kind = PaintPieceKind.Fill, Ring = new List<V3>(outline), Triangles = new List<int>(tris) };
                res.Pieces.Add(fill);
                res.Ok = true;
                return res;
            }

            var outlineTris = new List<V2[]>();
            for (int i = 0; i + 2 < tris.Count; i += 3) outlineTris.Add(new[] { xz[tris[i]], xz[tris[i + 1]], xz[tris[i + 2]] });

            foreach (var poly in BorderPieces(xz, outlineTris, prm.BorderWidth, prm.MitreLimit))
                res.Pieces.Add(MakePiece(poly, PaintPieceKind.Border, outline, xz, tris));

            if (mode == BoxPaintMode.Hatch)
            {
                int capped = 0;
                foreach (double sign in new[] { 1.0, -1.0 })
                {
                    double a = sign * prm.AngleDeg * Math.PI / 180.0;
                    V2 d = res.Rect.U * Math.Cos(a) + res.Rect.V * Math.Sin(a);
                    foreach (var poly in HatchFamily(xz, outlineTris, res.Rect.Origin, d, res.SpacingUsed, prm.LineWidth, prm.MaxLinesPerFamily, ref capped))
                        res.Pieces.Add(MakePiece(poly, PaintPieceKind.Hatch, outline, xz, tris));
                }
                if (capped > 0) res.Warnings.Add($"diagonals capped at {prm.MaxLinesPerFamily} per family ({capped} dropped)");
            }
            res.Ok = true;
            return res;
        }

        private static PaintPiece MakePiece(List<V2> poly, PaintPieceKind kind, List<V3> outline, List<V2> xz, List<int> tris)
        {
            var piece = new PaintPiece { Kind = kind };
            foreach (var p in poly) piece.Ring.Add(V3.FromXZ(p, HeightAt(outline, xz, tris, p)));
            for (int i = 1; i + 1 < poly.Count; i++) { piece.Triangles.Add(0); piece.Triangles.Add(i); piece.Triangles.Add(i + 1); }
            return piece;
        }

        /// <summary>Height of the outline surface at p: barycentric over the outline triangles, else the nearest outline point.</summary>
        public static double HeightAt(List<V3> outline, List<V2> xz, List<int> tris, V2 p)
        {
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                V2 a = xz[tris[i]], b = xz[tris[i + 1]], c = xz[tris[i + 2]];
                double d = V2.Cross(b - a, c - a);
                if (Math.Abs(d) < 1e-12) continue;
                double wb = V2.Cross(p - a, c - a) / d, wc = V2.Cross(b - a, p - a) / d, wa = 1 - wb - wc;
                if (wa >= -1e-6 && wb >= -1e-6 && wc >= -1e-6)
                    return wa * outline[tris[i]].Y + wb * outline[tris[i + 1]].Y + wc * outline[tris[i + 2]].Y;
            }
            double best = double.MaxValue, y = outline.Count > 0 ? outline[0].Y : 0;
            for (int i = 0; i < xz.Count; i++) { double dd = V2.Distance(xz[i], p); if (dd < best) { best = dd; y = outline[i].Y; } }
            return y;
        }

        /// <summary>Clips a convex polygon to the outline (given as its CCW triangles) and merges the bits back into as few convex pieces as possible.</summary>
        public static List<List<V2>> ClipToOutline(List<V2> convex, List<V2[]> outlineTris)
        {
            var bits = new List<List<V2>>();
            foreach (var t in outlineTris)
            {
                var c = ClipConvex(convex, t);
                if (c.Count >= 3 && SignedArea(c) > 1e-7) bits.Add(c);
            }
            return MergeConvex(bits);
        }

        /// <summary>Greedy merge: two convex pieces merge when their convex hull has exactly their summed area (they touch along a side and the union is convex).</summary>
        public static List<List<V2>> MergeConvex(List<List<V2>> pieces)
        {
            var list = new List<(List<V2> poly, double area)>();
            foreach (var p in pieces) list.Add((p, SignedArea(p)));
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < list.Count && !merged; i++)
                {
                    for (int j = i + 1; j < list.Count && !merged; j++)
                    {
                        if (!BoundsTouch(list[i].poly, list[j].poly)) continue;
                        var all = new List<V2>(list[i].poly); all.AddRange(list[j].poly);
                        var hull = ConvexHull(all);
                        double ha = SignedArea(hull), sum = list[i].area + list[j].area;
                        if (Math.Abs(ha - sum) <= 1e-7 + 1e-7 * ha)
                        {
                            list[i] = (hull, ha);
                            list.RemoveAt(j);
                            merged = true;
                        }
                    }
                }
            }
            var r = new List<List<V2>>(list.Count);
            foreach (var e in list) r.Add(e.poly);
            return r;
        }

        private static bool BoundsTouch(List<V2> a, List<V2> b)
        {
            double ax0 = double.MaxValue, ax1 = double.MinValue, az0 = double.MaxValue, az1 = double.MinValue;
            foreach (var p in a) { ax0 = Math.Min(ax0, p.X); ax1 = Math.Max(ax1, p.X); az0 = Math.Min(az0, p.Z); az1 = Math.Max(az1, p.Z); }
            foreach (var p in b) if (p.X >= ax0 - 1e-6 && p.X <= ax1 + 1e-6 && p.Z >= az0 - 1e-6 && p.Z <= az1 + 1e-6) return true;
            double bx0 = double.MaxValue, bx1 = double.MinValue, bz0 = double.MaxValue, bz1 = double.MinValue;
            foreach (var p in b) { bx0 = Math.Min(bx0, p.X); bx1 = Math.Max(bx1, p.X); bz0 = Math.Min(bz0, p.Z); bz1 = Math.Max(bz1, p.Z); }
            return !(bx0 > ax1 + 1e-6 || bx1 < ax0 - 1e-6 || bz0 > az1 + 1e-6 || bz1 < az0 - 1e-6);
        }

        /// <summary>The border strip: one band per outline side, inset by width with mitred (limited) corners, clipped to the outline.</summary>
        public static List<List<V2>> BorderPieces(List<V2> ring, List<V2[]> outlineTris, double width, double mitreLimit)
        {
            int n = ring.Count;
            var inner = new V2[n];
            for (int i = 0; i < n; i++)
            {
                V2 p = ring[i];
                V2 ePrev = (p - ring[(i + n - 1) % n]).Normalized, eNext = (ring[(i + 1) % n] - p).Normalized;
                V2 nPrev = ePrev.Left, nNext = eNext.Left;
                V2 q;
                // intersection of the two inset lines p + nPrev*w + ePrev*s and p + nNext*w + eNext*t
                double den = V2.Cross(ePrev, eNext);
                if (Math.Abs(den) < 1e-9) q = p + nNext * width;
                else
                {
                    V2 a = p + nPrev * width, b = p + nNext * width;
                    double s = V2.Cross(b - a, eNext) / den;
                    q = a + ePrev * s;
                }
                if (V2.Distance(q, p) > mitreLimit * width) q = p + (q - p).Normalized * (mitreLimit * width);
                inner[i] = q;
            }
            var result = new List<List<V2>>();
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var bits = new List<List<V2>>();
                foreach (var tri in new[] { new[] { ring[i], ring[j], inner[j] }, new[] { ring[i], inner[j], inner[i] } })
                {
                    var t = new List<V2>(tri);
                    double a = SignedArea(t);
                    if (Math.Abs(a) < 1e-9) continue;
                    if (a < 0) t.Reverse();
                    bits.AddRange(ClipToOutline(t, outlineTris));
                }
                result.AddRange(MergeConvex(bits));
            }
            return result;
        }

        /// <summary>One family of parallel diagonals: lines through origin + k*spacing (square to direction d), each a strip of the given width clipped to the outline.</summary>
        public static List<List<V2>> HatchFamily(List<V2> ring, List<V2[]> outlineTris, V2 origin, V2 d, double spacing, double width, int maxLines, ref int capped)
        {
            var result = new List<List<V2>>();
            d = d.Normalized;
            V2 nrm = d.Left;
            double minN = double.MaxValue, maxN = double.MinValue, minD = double.MaxValue, maxD = double.MinValue;
            foreach (var p in ring)
            {
                double pn = V2.Dot(p - origin, nrm), pd = V2.Dot(p - origin, d);
                minN = Math.Min(minN, pn); maxN = Math.Max(maxN, pn); minD = Math.Min(minD, pd); maxD = Math.Max(maxD, pd);
            }
            int k0 = (int)Math.Ceiling((minN - width / 2) / spacing), k1 = (int)Math.Floor((maxN + width / 2) / spacing);
            int count = 0;
            for (int k = k0; k <= k1; k++)
            {
                if (count >= maxLines) { capped++; continue; }
                count++;
                double c = k * spacing;
                var strip = new List<V2>
                {
                    origin + d * (minD - 1) + nrm * (c - width / 2),
                    origin + d * (maxD + 1) + nrm * (c - width / 2),
                    origin + d * (maxD + 1) + nrm * (c + width / 2),
                    origin + d * (minD - 1) + nrm * (c + width / 2),
                };
                result.AddRange(ClipToOutline(strip, outlineTris));
            }
            return result;
        }

        /// <summary>Line k's strip of a family, for tests: same construction as HatchFamily.</summary>
        public static List<V2> StripQuad(List<V2> ring, V2 origin, V2 d, double c, double width)
        {
            d = d.Normalized;
            V2 nrm = d.Left;
            double minD = double.MaxValue, maxD = double.MinValue;
            foreach (var p in ring) { double pd = V2.Dot(p - origin, d); minD = Math.Min(minD, pd); maxD = Math.Max(maxD, pd); }
            return new List<V2>
            {
                origin + d * (minD - 1) + nrm * (c - width / 2),
                origin + d * (maxD + 1) + nrm * (c - width / 2),
                origin + d * (maxD + 1) + nrm * (c + width / 2),
                origin + d * (minD - 1) + nrm * (c + width / 2),
            };
        }

        // ------------------------------------------------------------------ shader-side per-vertex neighbours

        /// <summary>
        /// Previous / next ring neighbours of vertex i as the game's AreaBatchSystem hands them to the surface shader.
        /// The game reverses the border direction for counter-clockwise areas (AddBorders), so the shader always sees
        /// prev -> vertex -> next walking clockwise. Our rings are CCW, so prev = ring[i+1], next = ring[i-1]
        /// (swap = true flips it, a probe aid for U9).
        /// </summary>
        public static void Neighbours(int count, int i, bool swap, out int prev, out int next)
        {
            int a = (i + 1) % count, b = (i + count - 1) % count;
            if (swap) { prev = b; next = a; }
            else { prev = a; next = b; }
        }
    }
}
