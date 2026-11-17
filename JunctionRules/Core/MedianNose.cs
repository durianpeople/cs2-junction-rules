using System;
using System.Collections.Generic;

namespace JunctionRules.Core
{
    // 0.4.12-probe (JR-T-018, gap G26, owner 17 Nov 2026: "the box must be bound by ANY median and never go over it";
    // junction boxes never paint over a median nose or island). Pure geometry for BoxShapes.MedianNoses: an arm's median
    // nose at a junction is its node composition's BlockTraffic | PreserveShape piece ("Median End Piece 2": 2 m wide,
    // 0.6 m long; "Median End Piece 5": 5 x 1.6), at the arm's mouth. It becomes a strip (CrosswalkStrip.Median) across
    // the median, from the mouth inward over the piece's length, so CarriagewayOutline.StopDistances keeps the box 'margin'
    // clear of it exactly as of a zebra, and a nose past the kerb lines holds the mouth (G24 machinery). A BlockTraffic
    // piece without PreserveShape on a node end is the median itself running on through the junction (no median break):
    // no box can avoid it there, so the junction gets none ('through').

    /// <summary>The four mouth points of a node end and its composition's width / middle offset (node composition frame).</summary>
    public struct NodeMouth
    {
        /// <summary>m_Left.m_Left.a, m_Left.m_Right.a, m_Right.m_Left.a, m_Right.m_Right.a (x / z).</summary>
        public V2 OuterLeft, MiddleLeft, MiddleRight, OuterRight;
        public double Width, MiddleOffset;

        public NodeMouth(V2 outerLeft, V2 middleLeft, V2 middleRight, V2 outerRight, double width, double middleOffset)
        {
            OuterLeft = outerLeft; MiddleLeft = middleLeft; MiddleRight = middleRight; OuterRight = outerRight;
            Width = width; MiddleOffset = middleOffset;
        }

        /// <summary>
        /// The mouth point at composition x: x &lt;= MiddleOffset on the left half (outer left at -Width / 2 to the middle),
        /// else on the right half (middle to outer right at +Width / 2), linear across each half as the node mesh maps it.
        /// </summary>
        public V2 At(double x)
        {
            double half = Width * 0.5, mo = MiddleOffset;
            if (x <= mo)
            {
                double w = mo + half;
                double f = w > 1e-9 ? (x + half) / w : 1;
                return OuterLeft + (MiddleLeft - OuterLeft) * Clamp01(f);
            }
            else
            {
                double w = half - mo;
                double f = w > 1e-9 ? (x - mo) / w : 0;
                return MiddleRight + (OuterRight - MiddleRight) * Clamp01(f);
            }
        }

        private static double Clamp01(double f) => f < 0 ? 0 : f > 1 ? 1 : f;
    }

    /// <summary>One node-composition piece for the nose rule (composition metres).</summary>
    public struct MedianPiece
    {
        public double Offset, Width, Length;
        public bool BlockTraffic, PreserveShape;

        public MedianPiece(double offset, double width, double length, bool blockTraffic, bool preserveShape)
        {
            Offset = offset; Width = width; Length = length; BlockTraffic = blockTraffic; PreserveShape = preserveShape;
        }
    }

    public static class MedianNose
    {
        /// <summary>Length used when a nose piece reports none (NetPieceData.m_Length 0): the 2 m median's 0.6 m, doubled.</summary>
        public const double FallbackLength = 1.2;

        /// <summary>
        /// Adds a strip per nose piece to 'into' (Median = true): across the piece's width at the mouth, centred half its
        /// length in from the mouth, painted width = its length. 'inward' = into the node (the node-end curves' start
        /// tangent; when unusable, the left normal of the mouth line outer left -&gt; outer right, which points into the node:
        /// live node 621408 arm 1155147). A median running on through (BlockTraffic without PreserveShape) sets 'through'
        /// and adds nothing. Returns the number of strips added.
        /// </summary>
        public static int AddStrips(NodeMouth mouth, V2 inward, IList<MedianPiece> pieces, List<CrosswalkStrip> into, out string through)
        {
            through = null;
            if (pieces == null || into == null) return 0;
            V2 across = mouth.OuterRight - mouth.OuterLeft;
            V2 normal = across.Normalized.Left;
            V2 n = inward.Normalized;
            if (n.Length < 0.5 || !CarriagewayOutline.IsFinite(n)) n = normal;
            else if (normal.Length > 0.5 && V2.Dot(n, normal) < 0) n = n * -1;
            if (n.Length < 0.5) return 0;
            int added = 0;
            foreach (MedianPiece p in pieces)
            {
                if (!p.BlockTraffic) continue;
                if (!CarriagewayOutline.IsFinite(p.Offset) || !CarriagewayOutline.IsFinite(p.Width)) continue;
                if (!p.PreserveShape)
                {
                    through = $"a median runs on through the junction (divider piece {p.Width:0.#} m at x {p.Offset:0.##} on the node end, no median break): the box would cover it";
                    return added;
                }
                double len = p.Length > 1e-3 && CarriagewayOutline.IsFinite(p.Length) ? p.Length : FallbackLength;
                double w = Math.Max(0, p.Width);
                V2 a = mouth.At(p.Offset - w * 0.5), b = mouth.At(p.Offset + w * 0.5);
                V2 shift = n * (len * 0.5);
                into.Add(new CrosswalkStrip(a + shift, b + shift, len, false, true));
                added++;
            }
            return added;
        }
    }
}
