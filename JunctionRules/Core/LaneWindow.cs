using System.Runtime.CompilerServices;

namespace JunctionRules.Core
{
    /// <summary>
    /// 0.4.10-probe (JR-T-016, G18 / G19, owner 17 Nov 2026: "cars still enter yellow boxes they cannot clear"): where a
    /// painted exit / entrance box lies on one road lane, for the driving rule.
    ///
    /// 0.4.3-0.4.9 placed an exit band on a lane by mapping the exit's EDGE curve position t linearly through the lane's
    /// EdgeLane.m_EdgeDelta ((t - delta.x) / (delta.y - delta.x)) and laying the exit box length centred on it. That is not
    /// where the lane is: the game builds edge lanes on the edge's CUT geometry (Game.Net.LaneSystem.CreateEdgeLanes 2652 /
    /// 2661-2662: the combined segment, or EdgeGeometry.m_Start with delta (0, 0.5) and m_End with (0.5, 1); CreateEdgeLane
    /// 4101-4105: Curve = MathUtils.Lerp(segment.m_Left, segment.m_Right, ...)), which starts and ends at the node mouths, not
    /// at the node positions where the edge Curve (and t) starts and ends. Live (Alderbrook Street, edge 984373, 192.1 m
    /// curve, lanes on its part 7.2..175.1 m): the 843726 exit box painted at 160.6..168.6 m was driven 12-13 m further
    /// north (about 148..157 m, so z ~300..309 instead of z 288..296: G19's southbound truck / car and the northbound sedan
    /// stood in the painted box with no box under them in the driving rule), and the 193433 + 843726 joint box (110.0..127.3
    /// m) was driven 3-8 m off (G18's hatchback stopped inside the painted box). 0.4.10: the band is the painted window
    /// itself: its two end points on the edge's centre curve (world x / z, the main thread's Boxes/BoxShapes.Entrance window,
    /// joint windows included, Systems/CarDrivingSwitchSystem.cs) are projected onto each lane's own curve. Burst-compatible
    /// (plain floats, no allocation), shared by the job (Stage2/JRYellowBox.cs) and the tests.
    /// </summary>
    public static class LaneWindow
    {
        /// <summary>Coarse samples of the closest-point search (then refined by ternary search on the best interval).</summary>
        public const int Samples = 32;

        /// <summary>Ternary refinement steps (interval 2 / Samples shrinks by (2/3)^steps: ~1e-6 of the lane).</summary>
        public const int RefineSteps = 28;

        /// <summary>Point of a cubic Bezier (x / z only) at t.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Point(float ax, float az, float bx, float bz, float cx, float cz, float dx, float dz, float t, out float x, out float z)
        {
            float u = 1f - t;
            float w0 = u * u * u, w1 = 3f * u * u * t, w2 = 3f * u * t * t, w3 = t * t * t;
            x = w0 * ax + w1 * bx + w2 * cx + w3 * dx;
            z = w0 * az + w1 * bz + w2 * cz + w3 * dz;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Dist2(float ax, float az, float bx, float bz, float cx, float cz, float dx, float dz, float t, float px, float pz)
        {
            Point(ax, az, bx, bz, cx, cz, dx, dz, t, out float x, out float z);
            float ex = x - px, ez = z - pz;
            return ex * ex + ez * ez;
        }

        /// <summary>
        /// The curve parameter (0..1, the lane's own game offset) of the point of the lane curve closest to (px, pz) in the
        /// x / z plane: a point beyond a lane end projects onto that end (0 or 1). Sampled, then refined.
        /// </summary>
        public static float Project(float ax, float az, float bx, float bz, float cx, float cz, float dx, float dz, float px, float pz)
        {
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i <= Samples; i++)
            {
                float d = Dist2(ax, az, bx, bz, cx, cz, dx, dz, (float)i / Samples, px, pz);
                if (d < bestD) { bestD = d; best = i; }
            }
            float lo = (best > 0 ? best - 1 : 0) / (float)Samples, hi = (best < Samples ? best + 1 : Samples) / (float)Samples;
            for (int k = 0; k < RefineSteps; k++)
            {
                float m1 = lo + (hi - lo) / 3f, m2 = hi - (hi - lo) / 3f;
                if (Dist2(ax, az, bx, bz, cx, cz, dx, dz, m1, px, pz) <= Dist2(ax, az, bx, bz, cx, cz, dx, dz, m2, px, pz)) hi = m2;
                else lo = m1;
            }
            float t = (lo + hi) * 0.5f;
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            // a point beyond a lane end projects exactly onto that end (the refinement only approaches it)
            float dt = Dist2(ax, az, bx, bz, cx, cz, dx, dz, t, px, pz);
            if (Dist2(ax, az, bx, bz, cx, cz, dx, dz, 0f, px, pz) <= dt) return 0f;
            if (Dist2(ax, az, bx, bz, cx, cz, dx, dz, 1f, px, pz) <= dt) return 1f;
            return t;
        }

        /// <summary>
        /// The painted window [(x0, z0) .. (x1, z1)] (its end points on the edge's centre curve, either order) on one lane:
        /// driving coordinates (0 = where the car enters the lane) [b0, b1]. False when the window misses the lane (both ends
        /// project onto the same lane end, e.g. the other half lane of a two-piece edge) or covers less than
        /// <see cref="MinMetres"/> of it.
        /// </summary>
        public static bool OnLane(float ax, float az, float bx, float bz, float cx, float cz, float dx, float dz, float laneLength, bool inverse,
            float x0, float z0, float x1, float z1, out float b0, out float b1)
        {
            b0 = b1 = 0f;
            if (!(laneLength > 0f)) return false;
            float u0 = Project(ax, az, bx, bz, cx, cz, dx, dz, x0, z0);
            float u1 = Project(ax, az, bx, bz, cx, cz, dx, dz, x1, z1);
            return FromOffsets(u0, u1, laneLength, inverse, out b0, out b1);
        }

        /// <summary>Shortest piece of a window on a lane that is a band (as BoxDriving.Band: 0.5 m).</summary>
        public const float MinMetres = 0.5f;

        /// <summary>Lane offsets of the window's ends (any order) to driving coordinates; false below <see cref="MinMetres"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool FromOffsets(float u0, float u1, float laneLength, bool inverse, out float b0, out float b1)
        {
            float lo = u0 < u1 ? u0 : u1, hi = u0 < u1 ? u1 : u0;
            if (lo < 0f) lo = 0f;
            if (hi > 1f) hi = 1f;
            b0 = b1 = 0f;
            if ((hi - lo) * laneLength < MinMetres) return false;
            b0 = inverse ? 1f - hi : lo;
            b1 = inverse ? 1f - lo : hi;
            return true;
        }
    }
}
