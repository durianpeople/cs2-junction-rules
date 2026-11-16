using System;
using System.Collections.Generic;

namespace JunctionRules.Core
{
    /// <summary>
    /// 0.4.4 (JR-T-011 part B, owner 16 Nov 2026): which of the road's own markings lie inside a yellow box, so they can be
    /// hidden under the hatch. Game-free maths only; Paint/MarkingHider.cs applies the result.
    /// - Marking LANES (lane dashes, centre lines, edge lines, node markings: the road's secondary net lanes, decal meshes
    ///   drawn along the lane curve) are cut by curve-delta ranges: the parts of the lane's centre curve inside any box
    ///   polygon (in plan). The game's renderer skips those ranges (Game.Net.CutRange, BatchInstanceSystem.cs 787-812,
    ///   BatchDataSystem.cs 1137-1170).
    /// - Marking OBJECTS (arrows, painted speed numbers such as the "40": decal-only secondary objects of the road) are hidden
    ///   whole when their footprint lies enough inside a box (<see cref="ObjectHideFraction"/>) or their centre is inside.
    /// Polygons are straight-sided rings in plan (X, Z); they may overlap (separate boxes of one group); ranges merge.
    /// </summary>
    public static class MarkingMask
    {
        /// <summary>Plan step between curve samples (m); crossings are then refined by bisection.</summary>
        public const double SampleStep = 0.25;

        /// <summary>Upper bound on samples per curve (long lanes are sampled coarser, crossings still refined).</summary>
        public const int MaxSamples = 1024;

        /// <summary>A cut shorter than this (m along the curve) is ignored (a lane that only grazes a box corner).</summary>
        public const double MinCutLength = 0.05;

        /// <summary>A marking object is hidden when at least this share of its footprint is inside the box(es).</summary>
        public const double ObjectHideFraction = 0.25;

        /// <summary>A cubic Bezier in 3D (the game's Bezier4x3 control points).</summary>
        public struct Curve
        {
            public V3 A, B, C, D;
            public Curve(V3 a, V3 b, V3 c, V3 d) { A = a; B = b; C = c; D = d; }

            public V2 PositionXZ(double t)
            {
                double u = 1 - t;
                double w0 = u * u * u, w1 = 3 * u * u * t, w2 = 3 * u * t * t, w3 = t * t * t;
                return new V2(w0 * A.X + w1 * B.X + w2 * C.X + w3 * D.X, w0 * A.Z + w1 * B.Z + w2 * C.Z + w3 * D.Z);
            }

            /// <summary>Plan length of the control polygon (an upper bound of the curve's plan length).</summary>
            public double HullLength => V2.Distance(A.XZ, B.XZ) + V2.Distance(B.XZ, C.XZ) + V2.Distance(C.XZ, D.XZ);
        }

        /// <summary>A curve-delta range [Min, Max] (0 = curve start, 1 = curve end).</summary>
        public struct Range : IEquatable<Range>
        {
            public double Min, Max;
            public Range(double min, double max) { Min = min; Max = max; }
            public bool Equals(Range o) => Math.Abs(Min - o.Min) < 1e-4 && Math.Abs(Max - o.Max) < 1e-4;
            public override bool Equals(object obj) => obj is Range r && Equals(r);
            public override int GetHashCode() => 0;
            public override string ToString() => $"[{Min:0.####}, {Max:0.####}]";
        }

        /// <summary>Even-odd point in polygon test in plan (points on an edge may go either way).</summary>
        public static bool Inside(V2 p, IList<V2> ring)
        {
            bool inside = false;
            int n = ring.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                V2 a = ring[i], b = ring[j];
                if ((a.Z > p.Z) != (b.Z > p.Z))
                {
                    double x = a.X + (p.Z - a.Z) * (b.X - a.X) / (b.Z - a.Z);
                    if (p.X < x) inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>True when p is inside any of the polygons.</summary>
        public static bool InsideAny(V2 p, IList<IList<V2>> rings)
        {
            for (int i = 0; i < rings.Count; i++) if (rings[i] != null && rings[i].Count >= 3 && Inside(p, rings[i])) return true;
            return false;
        }

        /// <summary>Plan bounds of a set of rings (false when there is no ring with 3 or more points).</summary>
        public static bool Bounds(IList<IList<V2>> rings, out V2 min, out V2 max)
        {
            min = new V2(double.MaxValue, double.MaxValue);
            max = new V2(double.MinValue, double.MinValue);
            bool any = false;
            foreach (IList<V2> r in rings)
            {
                if (r == null || r.Count < 3) continue;
                foreach (V2 p in r)
                {
                    any = true;
                    min = new V2(Math.Min(min.X, p.X), Math.Min(min.Z, p.Z));
                    max = new V2(Math.Max(max.X, p.X), Math.Max(max.Z, p.Z));
                }
            }
            return any;
        }

        /// <summary>
        /// The curve-delta ranges of <paramref name="curve"/> whose plan position lies inside any ring, sorted, merged,
        /// each at least <see cref="MinCutLength"/> long; empty when the curve stays outside. A range touching an end of
        /// the curve is snapped to exactly 0 / 1.
        /// </summary>
        public static List<Range> InsideRanges(Curve curve, IList<IList<V2>> rings)
        {
            var result = new List<Range>();
            if (rings == null || rings.Count == 0) return result;
            if (!Bounds(rings, out V2 bmin, out V2 bmax)) return result;
            // quick reject: the curve's control hull lies inside its bounds
            double cminX = Math.Min(Math.Min(curve.A.X, curve.B.X), Math.Min(curve.C.X, curve.D.X));
            double cmaxX = Math.Max(Math.Max(curve.A.X, curve.B.X), Math.Max(curve.C.X, curve.D.X));
            double cminZ = Math.Min(Math.Min(curve.A.Z, curve.B.Z), Math.Min(curve.C.Z, curve.D.Z));
            double cmaxZ = Math.Max(Math.Max(curve.A.Z, curve.B.Z), Math.Max(curve.C.Z, curve.D.Z));
            if (cmaxX < bmin.X || cminX > bmax.X || cmaxZ < bmin.Z || cminZ > bmax.Z) return result;

            double hull = curve.HullLength;
            int n = (int)Math.Ceiling(hull / SampleStep);
            n = Math.Max(8, Math.Min(MaxSamples, n));
            bool prevIn = InsideAny(curve.PositionXZ(0), rings);
            double start = prevIn ? 0.0 : -1.0;
            double prevT = 0.0;
            for (int i = 1; i <= n; i++)
            {
                double t = (double)i / n;
                bool inNow = InsideAny(curve.PositionXZ(t), rings);
                if (inNow != prevIn)
                {
                    double edge = Refine(curve, rings, prevT, t, prevIn);
                    if (inNow) start = edge;
                    else { Add(result, curve, start, edge); start = -1.0; }
                    prevIn = inNow;
                }
                prevT = t;
            }
            if (prevIn) Add(result, curve, start, 1.0);
            return result;
        }

        /// <summary>Bisection between t0 (state s0) and t1 (the other state): the crossing parameter.</summary>
        private static double Refine(Curve curve, IList<IList<V2>> rings, double t0, double t1, bool s0)
        {
            for (int k = 0; k < 20; k++)
            {
                double m = 0.5 * (t0 + t1);
                if (InsideAny(curve.PositionXZ(m), rings) == s0) t0 = m; else t1 = m;
            }
            return 0.5 * (t0 + t1);
        }

        private static void Add(List<Range> list, Curve curve, double a, double b)
        {
            if (a < 1e-6) a = 0.0;
            if (b > 1 - 1e-6) b = 1.0;
            if (b <= a) return;
            if (PlanLength(curve, a, b) < MinCutLength) return;
            if (list.Count > 0 && a - list[list.Count - 1].Max < 1e-9) { Range last = list[list.Count - 1]; last.Max = b; list[list.Count - 1] = last; return; }
            list.Add(new Range(a, b));
        }

        /// <summary>Plan length of the curve between two parameters (16 chords).</summary>
        public static double PlanLength(Curve curve, double a, double b)
        {
            double len = 0;
            V2 p = curve.PositionXZ(a);
            for (int i = 1; i <= 16; i++)
            {
                V2 q = curve.PositionXZ(a + (b - a) * i / 16.0);
                len += V2.Distance(p, q);
                p = q;
            }
            return len;
        }

        /// <summary>True when the two range lists are the same (within 1e-4 curve delta).</summary>
        public static bool SameRanges(IList<Range> a, IList<Range> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!a[i].Equals(b[i])) return false;
            return true;
        }

        /// <summary>
        /// The plan footprint of an object's local bounds (min/max X and Z in object space) placed at position, with the
        /// plan directions of the object's local +X (right) and +Z (forward) axes; counter-clockwise ring.
        /// </summary>
        public static List<V2> Footprint(V2 position, V2 right, V2 forward, double minX, double maxX, double minZ, double maxZ)
        {
            V2 P(double x, double z) => position + right * x + forward * z;
            var ring = new List<V2> { P(minX, minZ), P(maxX, minZ), P(maxX, maxZ), P(minX, maxZ) };
            if (SignedArea(ring) < 0) ring.Reverse();
            return ring;
        }

        /// <summary>Signed plan area (positive counter-clockwise in X/Z).</summary>
        public static double SignedArea(IList<V2> ring)
        {
            double s = 0;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++) s += V2.Cross(ring[j], ring[i]);
            return 0.5 * s;
        }

        /// <summary>
        /// Share (0..1) of a CONVEX footprint's area inside the rings: each ring is clipped by the footprint
        /// (Sutherland-Hodgman, the footprint as the convex clipper) and the areas added; capped at 1 (rings of one group
        /// may overlap).
        /// </summary>
        public static double InsideFraction(IList<V2> convexFootprint, IList<IList<V2>> rings)
        {
            double fa = Math.Abs(SignedArea(convexFootprint));
            if (fa < 1e-9) return 0;
            var clip = new List<V2>(convexFootprint);
            if (SignedArea(clip) < 0) clip.Reverse();
            double sum = 0;
            foreach (IList<V2> r in rings)
            {
                if (r == null || r.Count < 3) continue;
                List<V2> poly = new List<V2>(r);
                for (int e = 0; e < clip.Count && poly.Count > 0; e++)
                {
                    V2 a = clip[e], b = clip[(e + 1) % clip.Count];
                    poly = ClipHalfPlane(poly, a, b);
                }
                if (poly.Count >= 3) sum += Math.Abs(SignedArea(poly));
            }
            return Math.Min(1.0, sum / fa);
        }

        private static List<V2> ClipHalfPlane(List<V2> poly, V2 a, V2 b)
        {
            var output = new List<V2>(poly.Count + 2);
            V2 d = b - a;
            for (int i = 0; i < poly.Count; i++)
            {
                V2 p = poly[i], q = poly[(i + 1) % poly.Count];
                double sp = V2.Cross(d, p - a), sq = V2.Cross(d, q - a);
                bool pin = sp >= 0, qin = sq >= 0;
                if (pin) output.Add(p);
                if (pin != qin)
                {
                    double t = sp / (sp - sq);
                    output.Add(p + (q - p) * t);
                }
            }
            return output;
        }

        /// <summary>The hide rule for a marking object: centre inside a ring, or <see cref="ObjectHideFraction"/> of its footprint inside.</summary>
        public static bool HideObject(V2 centre, IList<V2> convexFootprint, IList<IList<V2>> rings)
        {
            if (InsideAny(centre, rings)) return true;
            return InsideFraction(convexFootprint, rings) >= ObjectHideFraction;
        }
    }
}
