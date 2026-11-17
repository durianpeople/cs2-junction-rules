using System;
using System.Collections.Generic;
using JunctionRules.Core;
using CW = JunctionRules.Core.CarriagewayOutline;
using G = JunctionRules.Core.BoxPaintGeometry;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.11 (JR-T-017, gap G24, owner live 17 Nov: "marking over zebra"). Capper Street junction node 1205548 at (488.085,
    /// 603.923, 536.799): 4 arms of 16 m (arms 1 / 2 Capper Street, priority; arm 3 Lilac Street, arm 4 Lilac Street
    /// pedestrian street, give way), footways 3 | 3, 3 | 3, 3 | 3, 0.5 | 0.5, 4 zebras, margin 0.5 m. Log 19:32:38: "arm 1:
    /// marking (+ margin) ends 2.8 m in, past the kerb lines; box starts at the mouth" (arm 2 the same), shape rule
    /// priority-road, mouth shifts [0.00, 0.00, 1.23, 1.11] m: the box hatch / border lay over both Capper Street zebras.
    /// Owner rule: the box never covers a zebra or any road marking (0.5 m margin); a priority arm's mouth moves back to clear it.
    /// </summary>
    internal static partial class Program
    {
        private static void G24Tests()
        {
            V2 o = new V2(0, 0);
            // As the log shows the node's arm slices: short outer edges (2 m from each mouth) whose kerbs do not meet ("joined
            // end to end"), so a zebra ending 2.8 m in lies past the kerb lines. 16 m roads, mouths 10 m from the centre.
            const double H = 8, r = 10, stub = 2;
            var deg = new double[] { 0, 90, 180, 270 };   // 0 / 180 Capper Street (priority), 90 Lilac Street, 270 Lilac Street pedestrian street
            var arms = new List<ArmInput>();
            for (int i = 0; i < 4; i++)
            {
                double ang = deg[i] * Math.PI / 180;
                var u = new V2(Math.Cos(ang), Math.Sin(ang));
                var arm = new ArmInput();
                arm.OuterA.Add(V3.FromXZ(u * r + u.Left * H, 0)); arm.OuterA.Add(V3.FromXZ(u * (r - stub) + u.Left * H, 0));
                arm.OuterB.Add(V3.FromXZ(u * r - u.Left * H, 0)); arm.OuterB.Add(V3.FromXZ(u * (r - stub) - u.Left * H, 0));
                arm.InsetA = arm.InsetB = i == 3 ? 0.5 : 3;
                arm.Yielding = i == 1 || i == 3;
                arms.Add(arm);
            }
            const double R = 0;
            // Zebras 3 m wide across each arm; their junction-side edge 2.3 m in from the mouth (+ 0.5 m margin = 2.8 m, as logged).
            var zebras = new List<CrosswalkStrip>();
            for (int i = 0; i < 4; i++)
            {
                double a = deg[i] * Math.PI / 180;
                var u = new V2(Math.Cos(a), Math.Sin(a));
                V2 c = u * (r - 2.3 + 1.5);
                zebras.Add(new CrosswalkStrip(c + u.Left * (H - 0.5), c - u.Left * (H - 0.5), 3.0));
            }
            var cps = new List<V2> { new V2(2, 2), new V2(-2, 2), new V2(2, -2), new V2(-2, -2) };
            var res = CW.BuildStraight(arms, o, zebras, 0.5, cps, false);
            string notes = string.Join("; ", res.Notes);
            Check(notes.Contains("arm 1: marking (+ margin) ends 2.8 m in, past the kerb lines") || notes.Contains("arm 2: marking (+ margin) ends 2.8 m in, past the kerb lines")
                  || notes.Contains("arm 3: marking (+ margin) ends 2.8 m in, past the kerb lines"), "Capper geometry reproduced: priority-arm zebra ends 2.8 m in, past the kerb lines (" + notes + ")");
            Check(notes.Contains("would cover a marking past the kerb lines"), "the 0.4.10 shape (priority mouths at the mouth) is recognised as covering the zebra and not taken");
            Check(res.Outline != null, "a box is built clear of the zebras (" + res.Failure + ")");
            if (res.Outline == null) return;
            var ring = G.ToXZ(res.Outline);
            double worst = double.MaxValue;
            for (int k = 0; k < ring.Count; k++)
            {
                V2 p = ring[k], q = ring[(k + 1) % ring.Count];
                foreach (var z in zebras) worst = Math.Min(worst, StraightBox.SegmentToStrip(p, q, z));
            }
            bool stripInside = false;
            foreach (var z in zebras) stripInside |= G.Contains(ring, z.A, 0) || G.Contains(ring, z.B, 0) || G.Contains(ring, (z.A + z.B) * 0.5, 0);
            Check(worst >= 0.5 - 0.02 && !stripInside, $"G24: every box edge keeps the 0.5 m margin from every zebra (closest {worst:0.00} m), no zebra inside the box");
            var sb = res.Straight;
            Check(sb != null && sb.Shift[0] >= 2.8 - 1e-6 && sb.Shift[2] >= 2.8 - 1e-6, $"both Capper Street (priority) mouths moved back at least 2.8 m ({sb?.Shift[0]:0.00}, {sb?.Shift[2]:0.00})");
            bool cpsIn = true;
            foreach (var c in cps) cpsIn &= G.Contains(ring, c, 1e-3);
            Check(cpsIn, "conflict points in the middle stay inside");
            Check(res.Outline.Count <= 8, $"straight-sided box ({res.Outline.Count} vertices)");
            // A box with no marking past its kerb lines keeps its shape exactly (the 0.4.7 / 0.4.8 shapes stay cm for cm).
            var plain = CW.BuildStraight(Synth(deg, H, 20, 6, 3, new[] { false, true, false, true }), o, null, 0.5, null, false);
            Check(plain.Outline != null && !string.Join("; ", plain.Notes).Contains("held clear"), "no marking past the kerb lines: built the 0.4.7 way, unchanged");
        }
    }
}
