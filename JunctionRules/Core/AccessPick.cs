using System;
using System.Collections.Generic;

namespace JunctionRules.Core
{
    // 1.0.3 (JR-T-023, owner 19 Nov 2026, live on 1.0.2: "the yellow box target is too small. I should be able to make a
    // valid entrance/exit light up when I hover on either the road or somewhere between the road and the exit."):
    // the hover area of one building access (exit, entrance or two-way; not a junction-kind access, which is its node).
    // 1.0.2 picked an access from a road hit only within EntranceLength / 2 + 2 m of its point on the road; a cursor on
    // the pavement / driveway / verge picked nothing. One rule now, the same for the Ctrl+Y tool and the API's x,z pick
    // (Boxes/BoxTargeting.PickAt):
    // (a) the box area on the road: within (length / 2 + margin) of the access point along the road and within (the road's
    //     half width + margin) across it (the whole road incl. its footways, in the road's direction at the access);
    // (b) the strip from the access point on the road to the building's access point (sub-net node / spawn location):
    //     (length / 2 + margin) either side of that line, from the road to 'margin' past the building's access point.
    // Several areas hold the cursor: the access whose line (road point -> building point) is nearest wins.
    // Pure geometry on the ground plane (x, z); no allocations.

    /// <summary>One access's hover area (ground plane, game x / z).</summary>
    public struct AccessPickArea
    {
        /// <summary>The access point on the road's centre curve.</summary>
        public V2 Point;
        /// <summary>The road's direction at the access (any length; zero = the box area is a circle of the half length).</summary>
        public V2 Along;
        /// <summary>Half the road's width (composition width / 2: kerb line to kerb line including footways).</summary>
        public double HalfWidth;
        /// <summary>The building's access point (sub-net node, spawn location); HasInner false = box area only.</summary>
        public V2 Inner;
        public bool HasInner;
    }

    public static class AccessPick
    {
        /// <summary>Metres added round the box area and the strip (1.0.2's road reach was length / 2 + 2 m: kept along the road).</summary>
        public const double Margin = 2.0;

        /// <summary>
        /// How far from the cursor the game side looks for roads whose accesses may hold it (metres): the strip of an
        /// access whose building point lies further than this from its road is reached from the building itself (a
        /// building hit picks its nearest access, as before).
        /// </summary>
        public const double SearchRadius = 48.0;

        /// <summary>(a) the box area on the road, plus 'margin'.</summary>
        public static bool InBox(in AccessPickArea a, V2 p, double length, double margin = Margin)
        {
            if (!Finite(p) || !Finite(a.Point)) return false;
            double halfLen = Math.Max(0, length) * 0.5 + Math.Max(0, margin);
            V2 d = p - a.Point;
            V2 u = a.Along.Normalized;
            if (u.X == 0 && u.Z == 0) return d.Length <= halfLen;
            double s = V2.Dot(d, u), l = V2.Dot(d, u.Left);
            double halfWide = Math.Max(0, Finite(a.HalfWidth) ? a.HalfWidth : 0) + Math.Max(0, margin);
            return Math.Abs(s) <= halfLen && Math.Abs(l) <= halfWide;
        }

        /// <summary>(b) the strip from the access point on the road to the building's access point, plus 'margin'.</summary>
        public static bool InStrip(in AccessPickArea a, V2 p, double length, double margin = Margin)
        {
            if (!a.HasInner || !Finite(p) || !Finite(a.Point) || !Finite(a.Inner)) return false;
            V2 axis = a.Inner - a.Point;
            double run = axis.Length;
            if (run < 0.1) return false;
            V2 u = axis * (1.0 / run);
            V2 d = p - a.Point;
            double s = V2.Dot(d, u), l = V2.Dot(d, u.Left);
            double half = Math.Max(0, length) * 0.5 + Math.Max(0, margin);
            return s >= 0 && s <= run + Math.Max(0, margin) && Math.Abs(l) <= half;
        }

        /// <summary>The cursor is on this access's hover area (box area or strip).</summary>
        public static bool Contains(in AccessPickArea a, V2 p, double length, double margin = Margin)
            => InBox(a, p, length, margin) || InStrip(a, p, length, margin);

        /// <summary>The overlap tie-break: distance from the cursor to the access's line (road point -> building point; the road point alone without one).</summary>
        public static double Distance(in AccessPickArea a, V2 p)
        {
            if (!a.HasInner || !Finite(a.Inner)) return V2.Distance(p, a.Point);
            V2 d = a.Inner - a.Point;
            double len2 = V2.Dot(d, d);
            double t = len2 > 1e-12 ? Math.Max(0, Math.Min(1, V2.Dot(p - a.Point, d) / len2)) : 0;
            return V2.Distance(p, a.Point + d * t);
        }

        /// <summary>The index of the access whose hover area holds the cursor (nearest line wins on overlaps); -1 = none.</summary>
        public static int Pick(IList<AccessPickArea> areas, V2 p, double length, double margin = Margin)
        {
            if (areas == null || !Finite(p)) return -1;
            int best = -1;
            double bestD = double.MaxValue;
            for (int i = 0; i < areas.Count; i++)
            {
                AccessPickArea a = areas[i];
                if (!Contains(a, p, length, margin)) continue;
                double d = Distance(a, p);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private static bool Finite(V2 v) => Finite(v.X) && Finite(v.Z);

        private static bool Finite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);
    }
}
