using System;
using System.Collections.Generic;

namespace JunctionRules.Core
{
    // 0.4.2-probe (JR-T-007b, owner 16 Nov 2026: "I want the Ctrl+Y highlight to look like [Traffic's Priorities Tool
    // hover], not the yellow blob"): the yellow box tool's hover is a thin outline of the box itself (the same outline the
    // paint uses: junction carriageway shape or entrance band), drawn with the game's overlay lines, like Traffic.
    //
    // What Traffic 0.2.13 does (decompile of the installed Traffic.dll, pdx_mods/80095_29, ilspycmd 8.2):
    // - Traffic.Rendering/OverlayRenderingHelpers.cs 12-179 DrawNodeOutline: for each connected edge, a straight
    //   OverlayRenderSystem.Buffer.DrawLine across the edge's mouth (line 168: EdgeGeometry segment m_Left.a/d to
    //   m_Right.a/d) and DrawCurve along the node slice's two outer curves (lines 171-172: EdgeNodeGeometry
    //   m_Left.m_Left and m_Right.m_Right; plus the inner pair on roundabouts, 173-177). Every call: outline colour =
    //   fill colour = the colour, outlineWidth 0, StyleFlags 0, the given lineWidth, roundness float2(1).
    // - Traffic.Rendering/ToolOverlaySystem.cs 912 (HighlightIntersectionJob): colour = Color.red when the action is
    //   blocked (ToolActionBlocked), Color.white for the hovered node (Temp entity: PriorityToolSystem.cs 211-221 makes a
    //   Temp EditIntersection under the cursor), else new Color(0, 0.83, 1) (cyan) for the selected node; offsetLength 0
    //   in the Priorities tool (EditPriorities), 2 m in the lane connector.
    // - ToolOverlaySystem.cs 1646: lineWidth = feedbackLinesWidth * 0.75; Traffic/ModSettings.cs 303: FeedbackOutlineWidth
    //   default 0.3 -> 0.225 m.
    // - Orange new Color(1, 0.65, 0) (ToolOverlaySystem.cs 833, 843): Traffic's warning outline when a click would reset
    //   something (forbidden turn / priorities / roundabout resets). Traffic has no "remove" colour; we use this one for
    //   "click removes the box" (a click that takes something away), white for "click adds".
    // Ours: the box outline (not the whole node area: the box is what the click adds), every ring segment one DrawLine
    // with Traffic's arguments.

    public static class BoxHover
    {
        /// <summary>Traffic's default hover line width: FeedbackOutlineWidth 0.3 x 0.75 (metres).</summary>
        public const float LineWidth = 0.225f;

        /// <summary>RGBA: white = click adds a box (Traffic's hover colour).</summary>
        public static readonly float[] AddColour = { 1f, 1f, 1f, 1f };

        /// <summary>RGBA: orange = the target has a box, click removes it (Traffic's warning outline colour).</summary>
        public static readonly float[] RemoveColour = { 1f, 0.65f, 0f, 1f };

        public static float[] Colour(bool hasBox) => hasBox ? RemoveColour : AddColour;

        /// <summary>
        /// The closed ring as line segments (last point back to the first), skipping segments shorter than minLength and
        /// non-finite points. Empty for fewer than 2 usable points.
        /// </summary>
        public static List<(V3 A, V3 B)> Segments(IList<V3> ring, double minLength = 0.01)
        {
            var r = new List<(V3, V3)>();
            if (ring == null) return r;
            var pts = new List<V3>(ring.Count);
            foreach (V3 p in ring) if (CarriagewayOutline.IsFinite(p)) pts.Add(p);
            if (pts.Count < 2) return r;
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                V3 a = pts[i], b = pts[(i + 1) % n];
                if (n == 2 && i == 1) break;   // two points: one segment, not there and back
                if (V2.Distance(a.XZ, b.XZ) < minLength) continue;
                r.Add((a, b));
            }
            return r;
        }
    }
}
