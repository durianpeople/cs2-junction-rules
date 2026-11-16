using System;
using System.Collections.Generic;

namespace JunctionRules.Core
{
    // 0.4.4-probe (JR-T-011, owner 16 Nov 2026, Ironplace Avenue, a Medium Road Divided): an exit box covers only the
    // carriageway on the building's side, from its kerb up to the median (raised median, barrier, divider of a dual
    // carriageway), never across it. Undivided roads keep the full kerb-to-kerb box. Two separate one-way edges need
    // nothing here: each edge is its own road and the box already stops at its own kerbs.
    //
    // How the game marks a median (decompile ~/Code/decomp/yb-2026-11-13/full):
    // - A median / barrier / divider piece carries a NetDividerPiece component; m_BlockTraffic = true gives the piece
    //   NetPieceFlags.BlockTraffic (Game.Prefabs/NetInitializeSystem.cs 1276-1286; NetDividerPiece.cs 10-14). Nothing
    //   else sets BlockTraffic (only NetInitializeSystem writes it).
    // - NetCompositionHelpers.AddCompositionLanes walks the composition pieces left to right and starts a NEW
    //   carriageway index (num2++) at every BlockTraffic piece that follows lanes (NetCompositionHelpers.cs 1207-1212);
    //   every composition lane gets that index: NetCompositionLane.m_Carriageway = TempLaneGroup.m_CarriagewayIndex
    //   (lines 1357, 1444). So on an undivided road every road / parking lane has the same m_Carriageway, and lanes on
    //   either side of a raised median or barrier have different ones. A painted (crossable) centre is no divider.
    // - Each edge car lane carries it on: CarLane.m_CarriagewayGroup = (segmentIndex << 8) | m_Carriageway
    //   (Game.Net/LaneSystem.cs 4153), so the driving rule can tell the lanes of the exit's side by its low byte.
    // - Lane positions: NetCompositionLane.m_Position.x = piece offset + piece lane position (NetCompositionHelpers.cs
    //   1443), composition x < 0 = the edge's left (LaneSystem.cs 3648); a lane's direction: LaneFlags.Invert (0x1,
    //   set when the piece is inverted xor its lanes flipped, NetCompositionHelpers.cs 1437-1440), Twoway (0x80) =
    //   both directions (centre turn lane).
    //
    // Rule (SideSpan): carriageways ordered from the building's kerb inward; the box takes every carriageway up to (not
    // including) the first one whose CAR lanes all run against the building side's own car lanes (that one lies past a
    // median). Carriageways with no car lane (a protected bike track, a parking strip behind a divider) never stop it, a
    // carriageway holding both directions (undivided) is taken whole. The box's far side is the inner edge of the
    // last carriageway taken (the median's kerb); when nothing is cut the box is the full kerb-to-kerb span as before.

    /// <summary>One lane of an edge composition for the median rule (composition metres, x &lt; 0 = the edge's left).</summary>
    public struct CompositionLaneInfo
    {
        public double Position, Width;
        /// <summary>NetCompositionLane.m_Carriageway.</summary>
        public int Carriageway;
        /// <summary>Game LaneFlags of the lane (composition lane flags | lane prefab flags).</summary>
        public int Flags;

        public CompositionLaneInfo(double position, double width, int carriageway, int flags)
        {
            Position = position; Width = width; Carriageway = carriageway; Flags = flags;
        }

        public bool OnCarriageway => CarriagewayOutline.IsCarriagewayLane(Flags);
        public bool IsCar => (Flags & MedianSplit.LaneRoad) != 0 && (Flags & MedianSplit.LaneBicyclesOnly) == 0 && (Flags & MedianSplit.LanePedestrian) == 0;
    }

    /// <summary>The building side's part of the carriageway (composition metres) and the carriageways it covers.</summary>
    public struct SideSpanResult
    {
        /// <summary>Composition x range of the box across the road (kerb to median, or kerb to kerb).</summary>
        public double Lo, Hi;
        /// <summary>A median was found and the box stops at it.</summary>
        public bool Divided;
        /// <summary>Bit c set = carriageway index c is inside the box (all bits = undivided / unknown).</summary>
        public uint CarriagewayMask;
    }

    public static class MedianSplit
    {
        private sealed class Group
        {
            public int Index;
            public double Lo, Hi;
            public bool Fwd, Back;
        }

        // Game LaneFlags values (decompile Game.Prefabs/LaneFlags.cs: Invert 1, Road 8, Pedestrian 0x10, Twoway 0x80, BicyclesOnly 0x2000000).
        public const int LaneInvert = 0x1, LaneRoad = 0x8, LanePedestrian = 0x10, LaneTwoway = 0x80, LaneBicyclesOnly = 0x2000000;

        /// <summary>All carriageways (undivided road, or the rule could not tell).</summary>
        public const uint AllCarriageways = 0xFFFFFFFFu;

        /// <summary>Bit for a carriageway index (indices past 31 share bit 31).</summary>
        public static uint Bit(int carriageway) => 1u << Math.Max(0, Math.Min(31, carriageway));

        /// <summary>A car lane with this CarLane.m_CarriagewayGroup lies inside a band of this mask (low byte = composition carriageway).</summary>
        public static bool InMask(uint mask, int carriagewayGroup) => (mask & Bit(carriagewayGroup & 0xFF)) != 0;

        /// <summary>
        /// The building side's span. leftSide = the building is on the composition's left (x &lt; 0). False (full span,
        /// all carriageways) when there are no carriageway lanes.
        /// </summary>
        public static bool SideSpan(IList<CompositionLaneInfo> lanes, bool leftSide, out SideSpanResult r)
        {
            r = new SideSpanResult { CarriagewayMask = AllCarriageways };
            if (lanes == null) return false;
            var groups = new SortedDictionary<int, Group>();
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (CompositionLaneInfo l in lanes)
            {
                if (!l.OnCarriageway || !CarriagewayOutline.IsFinite(l.Position) || !CarriagewayOutline.IsFinite(l.Width) || l.Width < 0) continue;
                double a = l.Position - l.Width * 0.5, b = l.Position + l.Width * 0.5;
                lo = Math.Min(lo, a); hi = Math.Max(hi, b);
                if (!groups.TryGetValue(l.Carriageway, out Group g)) { g = new Group { Index = l.Carriageway, Lo = a, Hi = b }; groups[l.Carriageway] = g; }
                g.Lo = Math.Min(g.Lo, a); g.Hi = Math.Max(g.Hi, b);
                if (l.IsCar)
                {
                    if ((l.Flags & LaneTwoway) != 0) { g.Fwd = true; g.Back = true; }
                    else if ((l.Flags & LaneInvert) != 0) g.Back = true;
                    else g.Fwd = true;
                }
            }
            if (lo > hi) return false;
            r.Lo = lo; r.Hi = hi;
            if (groups.Count < 2) return true;
            // from the building's kerb inward
            var order = new List<Group>(groups.Values);
            order.Sort((x, y) => leftSide ? x.Lo.CompareTo(y.Lo) : y.Hi.CompareTo(x.Hi));
            bool? sideFwd = null;
            uint mask = 0;
            double edge = 0;
            bool cut = false;
            foreach (Group g in order)
            {
                bool cars = g.Fwd || g.Back;
                if (cars && g.Fwd && g.Back) break;                       // both directions together: undivided from here on, no cut
                if (cars && sideFwd.HasValue && g.Fwd != sideFwd.Value) { cut = true; break; }   // past the median
                if (cars && !sideFwd.HasValue) sideFwd = g.Fwd;
                mask |= Bit(g.Index);
                edge = leftSide ? g.Hi : g.Lo;
            }
            if (!cut) return true;
            r.Divided = true;
            r.CarriagewayMask = mask;
            if (leftSide) r.Hi = edge; else r.Lo = edge;
            return true;
        }
    
        /// <summary>
        /// The exit box's kerb insets from the composition edges (composition left / right): undivided = the footways as
        /// before (stop-bay heuristic kept); divided = the building side keeps its footway, the far side moves to the median
        /// kerb (half + Lo on the left, half - Hi on the right).
        /// </summary>
        public static void Insets(double half, double footLeft, double footRight, SideSpanResult span, bool leftSide, out double insetLeft, out double insetRight)
        {
            insetLeft = footLeft;
            insetRight = footRight;
            if (!span.Divided) return;
            if (leftSide) insetRight = Math.Max(footRight, half - span.Hi);
            else insetLeft = Math.Max(footLeft, half + span.Lo);
        }
    }
}
