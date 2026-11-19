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
