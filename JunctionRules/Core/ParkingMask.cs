using System;
using System.Collections.Generic;

namespace JunctionRules.Core
{
    /// <summary>A curve-parameter range [Min, Max] of a lane (0 = start, 1 = end).</summary>
    public struct TRange
    {
        public double Min, Max;
        public TRange(double min, double max) { Min = min; Max = max; }
        public override string ToString() => $"[{Min:0.####}, {Max:0.####}]";
    }

    /// <summary>One LaneOverlap of a parking lane as the parking code sees it: byte range along the lane (/255) and whether it is ours.</summary>
    public struct OverlapEntry
    {
        public byte Start, End;
        public bool Ours;
        /// <summary>Position in the buffer it came from (stock entries keep their order among equal keys).</summary>
        public int Index;
        public OverlapEntry(byte start, byte end, bool ours, int index) { Start = start; End = end; Ours = ours; Index = index; }
        /// <summary>The game's sort key (Game.Net.LaneOverlap.CompareTo: (m_ThisStart &lt;&lt; 8) | m_ThisEnd).</summary>
        public int Key => (Start << 8) | End;
        public override string ToString() => $"{(Ours ? "ours" : "stock")} {Start}-{End}";
    }

    /// <summary>
    /// 0.4.7 (JR-T-013d, gap G6, owner rule 17 Nov 2026: no parking inside a yellow box, whether or not the road has
    /// kerbside parking there). Game-independent part: which part of a parking lane lies under a box polygon, as the
    /// curve ranges the game's parking code blocks.
    ///
    /// How stock blocks parking slots (decompile 1.6.4f1): a parking lane's LaneOverlap buffer lists the curve ranges where
    /// other lanes (driveways, crosswalks) cross it (m_ThisStart / m_ThisEnd, curve position x 255). The slot search
    /// (VehicleUtils.FindFreeParkingSpace 1477-1600: a slot [x, y] is skipped when an overlap starts before y and was not
    /// passed before x), the free-space value the pathfinder reads (ParkingLaneDataSystem.CalculateFreeSpace 547-730, into
    /// PathUtils.GetParkingSpaceSpecification 1189-1232) and the parked-car fix-up (FixParkingLocationSystem 566-760) all
    /// skip those ranges. The buffer is IEmptySerializable (Game.Net/LaneOverlap.cs 9: never written into a save, only
    /// its presence) and rebuilt by LaneOverlapSystem for updated lanes and for every lane after a load (1899-1919).
    /// The mod adds one range of its own per box part of a lane; this class works out those ranges.
    /// </summary>
    public static class ParkingMask
    {
        /// <summary>A lane strip that only grazes a box edge (closer than this, metres, sideways) does not count.</summary>
        public const double SideTolerance = 0.10;

        /// <summary>Sampling step along the lane, metres (refined by bisection at each range end).</summary>
        public const double Step = 0.25;

        // ------------------------------------------------------------------ bezier

        public static V3 Point(V3 a, V3 b, V3 c, V3 d, double t)
        {
            double u = 1 - t;
            double w0 = u * u * u, w1 = 3 * u * u * t, w2 = 3 * u * t * t, w3 = t * t * t;
            return new V3(a.X * w0 + b.X * w1 + c.X * w2 + d.X * w3, a.Y * w0 + b.Y * w1 + c.Y * w2 + d.Y * w3, a.Z * w0 + b.Z * w1 + c.Z * w2 + d.Z * w3);
        }

        public static V2 Tangent(V3 a, V3 b, V3 c, V3 d, double t)
        {
            double u = 1 - t;
            V2 p0 = a.XZ, p1 = b.XZ, p2 = c.XZ, p3 = d.XZ;
            V2 tan = 3 * u * u * (p1 - p0) + 6 * u * t * (p2 - p1) + 3 * t * t * (p3 - p2);
            if (tan.Length < 1e-9) tan = p3 - p0;
            return tan.Normalized;
        }

        public static double Length(V3 a, V3 b, V3 c, V3 d, int n = 64)
        {
            double len = 0;
            V2 prev = a.XZ;
            for (int i = 1; i <= n; i++)
            {
                V2 p = Point(a, b, c, d, (double)i / n).XZ;
                len += V2.Distance(prev, p);
                prev = p;
            }
            return len;
        }

        // ------------------------------------------------------------------ strip under the polygons

        /// <summary>
        /// The curve ranges of a lane (centre curve a..d, strip halfWidth to each side) whose cross-section touches any of
        /// the rings (XZ polygons, either winding) by more than <see cref="SideTolerance"/>. Sorted, merged.
        /// </summary>
        public static List<TRange> InsideRanges(V3 a, V3 b, V3 c, V3 d, double halfWidth, IList<IList<V2>> rings)
        {
            var result = new List<TRange>();
            if (rings == null || rings.Count == 0) return result;
            double hw = Math.Max(0.0, halfWidth - SideTolerance);
            double len = Length(a, b, c, d);
            int n = Math.Max(16, (int)Math.Ceiling(len / Step));
            // quick reject: lane bounds vs ring bounds
            var useRings = new List<IList<V2>>();
            Bounds(a, b, c, d, hw + 0.5, out V2 lmin, out V2 lmax);
            foreach (IList<V2> r in rings)
            {
                if (r == null || r.Count < 3) continue;
                RingBounds(r, out V2 rmin, out V2 rmax);
                if (rmin.X > lmax.X || rmax.X < lmin.X || rmin.Z > lmax.Z || rmax.Z < lmin.Z) continue;
                useRings.Add(r);
            }
            if (useRings.Count == 0) return result;
            bool prevHit = false;
            double start = 0;
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n;
                bool hit = Hits(a, b, c, d, t, hw, useRings);
                if (hit && !prevHit) start = i == 0 ? 0.0 : Refine(a, b, c, d, hw, useRings, (double)(i - 1) / n, t);
                if (!hit && prevHit) result.Add(new TRange(start, Refine(a, b, c, d, hw, useRings, t, (double)(i - 1) / n)));
                prevHit = hit;
            }
            if (prevHit) result.Add(new TRange(start, 1.0));
            return Merge(result);
        }

        /// <summary>Bisection between a miss (tOut) and a hit (tIn): returns a t that hits, within 1e-4 of the boundary.</summary>
        private static double Refine(V3 a, V3 b, V3 c, V3 d, double hw, List<IList<V2>> rings, double tOut, double tIn)
        {
            for (int k = 0; k < 14; k++)
            {
                double m = 0.5 * (tOut + tIn);
                if (Hits(a, b, c, d, m, hw, rings)) tIn = m; else tOut = m;
            }
            return tIn;
        }

        private static bool Hits(V3 a, V3 b, V3 c, V3 d, double t, double hw, List<IList<V2>> rings)
        {
            V2 p = Point(a, b, c, d, t).XZ;
            V2 side = Tangent(a, b, c, d, t).Left;
            V2 p0 = p - side * hw, p1 = p + side * hw;
            foreach (IList<V2> r in rings) if (SegmentTouchesRing(p0, p1, r)) return true;
            return false;
        }

        /// <summary>True when the segment p0-p1 has a point inside the ring (an end inside, or it crosses an edge).</summary>
        public static bool SegmentTouchesRing(V2 p0, V2 p1, IList<V2> ring)
        {
            if (Inside(ring, p0) || Inside(ring, p1)) return true;
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                if (SegmentsCross(p0, p1, ring[i], ring[(i + 1) % n])) return true;
            }
            return false;
        }

        /// <summary>Even-odd point in polygon (either winding).</summary>
        public static bool Inside(IList<V2> ring, V2 p)
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

        private static bool SegmentsCross(V2 a, V2 b, V2 c, V2 d)
        {
            V2 r = b - a, s = d - c;
            double den = V2.Cross(r, s);
            if (Math.Abs(den) < 1e-12) return false;   // parallel: an overlap along the edge also has an end inside or crossing elsewhere
            V2 q = c - a;
            double t = V2.Cross(q, s) / den, u = V2.Cross(q, r) / den;
            return t >= 0 && t <= 1 && u >= 0 && u <= 1;
        }

        private static void Bounds(V3 a, V3 b, V3 c, V3 d, double pad, out V2 min, out V2 max)
        {
            // the control polygon holds the curve
            min = new V2(Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) - pad, Math.Min(Math.Min(a.Z, b.Z), Math.Min(c.Z, d.Z)) - pad);
            max = new V2(Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) + pad, Math.Max(Math.Max(a.Z, b.Z), Math.Max(c.Z, d.Z)) + pad);
        }

        private static void RingBounds(IList<V2> r, out V2 min, out V2 max)
        {
            min = new V2(double.MaxValue, double.MaxValue);
            max = new V2(double.MinValue, double.MinValue);
            foreach (V2 p in r)
            {
                min = new V2(Math.Min(min.X, p.X), Math.Min(min.Z, p.Z));
                max = new V2(Math.Max(max.X, p.X), Math.Max(max.Z, p.Z));
            }
        }

        /// <summary>Sorted, overlapping or touching ranges joined.</summary>
        public static List<TRange> Merge(List<TRange> ranges)
        {
            var sorted = new List<TRange>(ranges);
            sorted.Sort((x, y) => x.Min.CompareTo(y.Min));
            var outList = new List<TRange>();
            foreach (TRange r in sorted)
            {
                if (outList.Count > 0 && r.Min <= outList[outList.Count - 1].Max + 1e-9)
                {
                    TRange last = outList[outList.Count - 1];
                    last.Max = Math.Max(last.Max, r.Max);
                    outList[outList.Count - 1] = last;
                }
                else outList.Add(r);
            }
            return outList;
        }

        /// <summary>
        /// The ranges as LaneOverlap bytes (curve position x 255), widened outward (floor / ceil) so the whole box part is
        /// covered, and merged again after the rounding.
        /// </summary>
        public static List<(byte Start, byte End)> ToBytes(List<TRange> ranges)
        {
            var list = new List<(byte, byte)>();
            foreach (TRange r in Merge(ranges))
            {
                int s = (int)Math.Floor(Math.Max(0, Math.Min(1, r.Min)) * 255.0 + 1e-9);
                int e = (int)Math.Ceiling(Math.Max(0, Math.Min(1, r.Max)) * 255.0 - 1e-9);
                if (e <= s) e = Math.Min(255, s + 1);
                if (list.Count > 0 && s <= list[list.Count - 1].Item2)
                {
                    var last = list[list.Count - 1];
                    list[list.Count - 1] = (last.Item1, (byte)Math.Max(last.Item2, e));
                }
                else list.Add(((byte)s, (byte)e));
            }
            return list;
        }

        // ------------------------------------------------------------------ the game's parking slots (for tests and the log)

        /// <summary>
        /// The slot ranges of a slotted parking lane, walked like the game (VehicleUtils.FindFreeParkingSpace 1503-1586,
        /// ParkingLaneDataSystem.CalculateFreeSpace 555-634): 16 chords along the curve, the first slot after an offset
        /// (StartingLane only: length - slots x interval; EndingLane only: 0; else half of it). slotCount / interval come
        /// from the game (NetUtils.GetParkingSlotCount / GetParkingSlotInterval).
        /// </summary>
        public static List<TRange> SlotRanges(V3 a, V3 b, V3 c, V3 d, double length, int slotCount, double interval, bool startingLane, bool endingLane)
        {
            var slots = new List<TRange>();
            if (slotCount <= 0 || interval <= 0) return slots;
            V2 x = a.XZ;
            double fx = 0, fy = 0;
            double num = 0;
            double num2;
            if (startingLane && !endingLane) num2 = length - slotCount * interval;
            else if (endingLane && !startingLane) num2 = 0;
            else num2 = (length - slotCount * interval) * 0.5;
            num2 = Math.Max(num2, 0);
            int i = -1;
            for (int j = 1; j <= 16; j++)
            {
                double t = j * 0.0625;
                V2 p = Point(a, b, c, d, t).XZ;
                for (num += V2.Distance(x, p); num >= num2 || (j == 16 && i < slotCount); i++)
                {
                    fy = num2 < num ? fx + (t - fx) * (num2 / num) : t;
                    if (i >= 0 && i < slotCount) slots.Add(new TRange(fx, fy));
                    num -= num2;
                    fx = fy;
                    num2 = interval;
                }
                x = p;
            }
            return slots;
        }

        /// <summary>The game's test for one slot: some overlap range intersects the slot (FindFreeParkingSpace 1548-1568).</summary>
        public static bool SlotBlocked(TRange slot, IList<(byte Start, byte End)> overlaps)
        {
            foreach (var o in overlaps)
            {
                double s = o.Start * (1.0 / 255.0), e = o.End * (1.0 / 255.0);
                if (s < slot.Max && e > slot.Min) return true;
            }
            return false;
        }

        public static int BlockedSlots(List<TRange> slots, IList<(byte Start, byte End)> overlaps)
        {
            int n = 0;
            foreach (TRange s in slots) if (SlotBlocked(s, overlaps)) n++;
            return n;
        }

        // ------------------------------------------------------------------ the buffer

        /// <summary>
        /// The new overlap list of a parking lane: every stock entry kept (in its own order), ours replaced by the wanted
        /// ranges, the whole list ordered by the game's key (stable, as the game's own sort would leave it consistent for the
        /// in-order scans of FindFreeParkingSpace / CalculateFreeSpace). Wanted empty = the stock list only (box removed,
        /// setting off).
        /// </summary>
        public static List<OverlapEntry> Plan(IList<OverlapEntry> current, IList<(byte Start, byte End)> wanted)
        {
            var list = new List<OverlapEntry>();
            int idx = 0;
            foreach (OverlapEntry e in current) if (!e.Ours) list.Add(new OverlapEntry(e.Start, e.End, false, idx++));
            if (wanted != null) foreach (var w in wanted) list.Add(new OverlapEntry(w.Start, w.End, true, idx++));
            // stable sort by key
            var keyed = new List<(int Key, int Order, OverlapEntry E)>();
            for (int i = 0; i < list.Count; i++) keyed.Add((list[i].Key, i, list[i]));
            keyed.Sort((p, q) => p.Key != q.Key ? p.Key.CompareTo(q.Key) : p.Order.CompareTo(q.Order));
            var result = new List<OverlapEntry>(keyed.Count);
            foreach (var k in keyed) result.Add(k.E);
            return result;
        }

        /// <summary>True when the lane's current list already holds exactly these ranges of ours (nothing to write).</summary>
        public static bool HasExactly(IList<OverlapEntry> current, IList<(byte Start, byte End)> wanted)
        {
            var ours = new List<(byte, byte)>();
            foreach (OverlapEntry e in current) if (e.Ours) ours.Add((e.Start, e.End));
            int n = wanted?.Count ?? 0;
            if (ours.Count != n) return false;
            ours.Sort();
            var w = new List<(byte, byte)>();
            if (wanted != null) foreach (var x in wanted) w.Add((x.Start, x.End));
            w.Sort();
            for (int i = 0; i < n; i++) if (ours[i] != w[i]) return false;
            return true;
        }
    }
}
