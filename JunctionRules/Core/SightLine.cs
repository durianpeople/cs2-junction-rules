namespace JunctionRules.Core
{
    /// <summary>
    /// JR-T-022b (owner sight rule 19 Nov 2026): the driver's line of sight at a box is a radius around the driver
    /// (<see cref="RoomScan.LookPastSightMetres"/>, straight line, horizontal x / z), not a length along the path. On a turn
    /// with a free corner the driver sees the part of the road turned into that lies within the radius. This finds, on one
    /// lane of the car's path (a cubic Bezier, the game's Curve.m_Bezier), where the path FIRST leaves that radius, so the
    /// room scan can cut its sight there (<see cref="RoomScan.LimitSight"/>; path metres = |t - t0| x lane length, the scan's
    /// own convention). Pure arithmetic: Burst-safe, no allocations.
    ///
    /// Cost: one disc test of the four control points (a Bezier lies inside the hull of its control points, and a disc is
    /// convex: all four inside = the whole lane is seen), else at most <see cref="Samples"/> point evaluations and
    /// <see cref="Bisections"/> bisection steps per lane, for at most 1 + RoomScan's lane cap lanes per box room test, and only
    /// when the look-past is on.
    /// </summary>
    public static class SightLine
    {
        /// <summary>Points sampled along a lane span to find the first one outside the radius.</summary>
        public const int Samples = 8;
        /// <summary>Bisection steps between the last point inside and the first outside (span / 8 / 2^10: centimetres).</summary>
        public const int Bisections = 10;

        /// <summary>
        /// The first curve parameter <paramref name="t"/> between <paramref name="t0"/> and <paramref name="t1"/> (either
        /// direction) where the Bezier a-b-c-d (x / z) lies more than <paramref name="radius"/> from the eye (ex, ez). False =
        /// the whole span stays within the radius (t = t1). The start itself outside = t0.
        /// </summary>
        public static bool FirstExit(float ax, float az, float bx, float bz, float cx, float cz, float dx, float dz,
            float ex, float ez, float radius, float t0, float t1, out float t)
        {
            float r2 = radius * radius;
            if (Dist2(ax, az, bx, bz, cx, cz, dx, dz, ex, ez, t0) > r2) { t = t0; return true; }
            if (In(ax, az, ex, ez, r2) && In(bx, bz, ex, ez, r2) && In(cx, cz, ex, ez, r2) && In(dx, dz, ex, ez, r2)) { t = t1; return false; }
            float prev = t0;
            for (int i = 1; i <= Samples; i++)
            {
                float ti = t0 + (t1 - t0) * i / Samples;
                if (Dist2(ax, az, bx, bz, cx, cz, dx, dz, ex, ez, ti) > r2)
                {
                    float lo = prev, hi = ti;
                    for (int k = 0; k < Bisections; k++)
                    {
                        float mid = 0.5f * (lo + hi);
                        if (Dist2(ax, az, bx, bz, cx, cz, dx, dz, ex, ez, mid) > r2) hi = mid;
                        else lo = mid;
                    }
                    t = hi;
                    return true;
                }
                prev = ti;
            }
            t = t1;
            return false;
        }

        /// <summary>
        /// JR-T-022d (owner ruling 2, 19 Nov 2026: "the building is a box and higher than the car"; "just calculate the
        /// straight-line geometry"): does the 2D segment from (ax, az) to (bx, bz) cross a building footprint? The footprint is
        /// the object's box: local bounds [minX, maxX] x [minZ, maxZ] (the prefab's ObjectGeometryData bounds), placed at
        /// (cx, cz) with its local +z axis along (fx, fz) (the object's Transform rotation; normalised here; a zero axis = no
        /// rotation). Height is not looked at: every building blocks. Touching the box edge counts as crossing. Slab test in
        /// the box's own frame: Burst-safe float math, no allocations.
        /// </summary>
        public static bool SegmentHitsBox(float ax, float az, float bx, float bz, float cx, float cz, float fx, float fz,
            float minX, float maxX, float minZ, float maxZ)
        {
            float n = fx * fx + fz * fz;
            if (n > 1e-12f) { n = 1f / (float)System.Math.Sqrt(n); fx *= n; fz *= n; }
            else { fx = 0f; fz = 1f; }
            // local +x (right) of a yaw rotation in the game's left-handed frame: (fz, -fx)
            float rx = fz, rz = -fx;
            float pax = ax - cx, paz = az - cz, pbx = bx - cx, pbz = bz - cz;
            float lax = pax * rx + paz * rz, laz = pax * fx + paz * fz;
            float lbx = pbx * rx + pbz * rz, lbz = pbx * fx + pbz * fz;
            float t0 = 0f, t1 = 1f;
            return Slab(lax, lbx - lax, minX, maxX, ref t0, ref t1) && Slab(laz, lbz - laz, minZ, maxZ, ref t0, ref t1);
        }

        /// <summary>
        /// JR-T-022e (review F2): is the point (px, pz) inside the footprint (same box arguments as <see cref="SegmentHitsBox"/>;
        /// the edge counts as inside)?
        /// </summary>
        public static bool PointInBox(float px, float pz, float cx, float cz, float fx, float fz, float minX, float maxX, float minZ, float maxZ)
        {
            float n = fx * fx + fz * fz;
            if (n > 1e-12f) { n = 1f / (float)System.Math.Sqrt(n); fx *= n; fz *= n; }
            else { fx = 0f; fz = 1f; }
            float dx = px - cx, dz = pz - cz;
            float lx = dx * fz - dz * fx, lz = dx * fx + dz * fz;
            return lx >= minX && lx <= maxX && lz >= minZ && lz <= maxZ;
        }

        /// <summary>
        /// JR-T-022e (review F2, general rule of ruling 2): does the footprint block the sight line from the eye (ax, az) to the
        /// target (bx, bz)? It blocks when the line crosses it (<see cref="SegmentHitsBox"/>), except a footprint that contains
        /// the eye: the driver cannot be looking through the box he stands in. (For ordinary buildings the footprint is the LOT
        /// box, so a car leaving its own building, or in a car park lot, stands inside one.)
        /// </summary>
        public static bool SightBlocked(float ax, float az, float bx, float bz, float cx, float cz, float fx, float fz,
            float minX, float maxX, float minZ, float maxZ)
            => SegmentHitsBox(ax, az, bx, bz, cx, cz, fx, fz, minX, maxX, minZ, maxZ)
               && !PointInBox(ax, az, cx, cz, fx, fz, minX, maxX, minZ, maxZ);

        /// <summary>
        /// JR-T-022e (review F5): does the 2D segment cross the axis-aligned box [minX, maxX] x [minZ, maxZ] (edge counts)? The
        /// search tree walk prunes cells and items with it, so a diagonal line visits only what lies along it, not every object
        /// in its bounding box. Same slab test as <see cref="SegmentHitsBox"/>, unrotated.
        /// </summary>
        public static bool SegmentHitsAabb(float ax, float az, float bx, float bz, float minX, float maxX, float minZ, float maxZ)
        {
            float t0 = 0f, t1 = 1f;
            return Slab(ax, bx - ax, minX, maxX, ref t0, ref t1) && Slab(az, bz - az, minZ, maxZ, ref t0, ref t1);
        }

        /// <summary>JR-T-022d: clips [t0, t1] of p + t d to [lo, hi] on one axis; false = empty.</summary>
        private static bool Slab(float p, float d, float lo, float hi, ref float t0, ref float t1)
        {
            if (d > -1e-9f && d < 1e-9f) return p >= lo && p <= hi;
            float u = (lo - p) / d, w = (hi - p) / d;
            if (u > w) { float x = u; u = w; w = x; }
            if (u > t0) t0 = u;
            if (w < t1) t1 = w;
            return t0 <= t1;
        }

        /// <summary>JR-T-022d: the Bezier a-b-c-d point (x / z) at t.</summary>
        public static void PointAt(float ax, float az, float bx, float bz, float cx, float cz, float dx, float dz, float t, out float x, out float z)
        {
            float u = 1f - t;
            float w0 = u * u * u, w1 = 3f * u * u * t, w2 = 3f * u * t * t, w3 = t * t * t;
            x = w0 * ax + w1 * bx + w2 * cx + w3 * dx;
            z = w0 * az + w1 * bz + w2 * cz + w3 * dz;
        }

        private static bool In(float x, float z, float ex, float ez, float r2)
        {
            float dx = x - ex, dz = z - ez;
            return dx * dx + dz * dz <= r2;
        }

        /// <summary>Squared horizontal distance from the eye to the Bezier point at t.</summary>
        public static float Dist2(float ax, float az, float bx, float bz, float cx, float cz, float dx, float dz, float ex, float ez, float t)
        {
            float u = 1f - t;
            float w0 = u * u * u, w1 = 3f * u * u * t, w2 = 3f * u * t * t, w3 = t * t * t;
            float x = w0 * ax + w1 * bx + w2 * cx + w3 * dx - ex;
            float z = w0 * az + w1 * bz + w2 * cz + w3 * dz - ez;
            return x * x + z * z;
        }
    }
}
