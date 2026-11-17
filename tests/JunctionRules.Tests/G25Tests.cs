using System;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.11 (JR-T-017, gap G25, owner live 17 Nov): at an UNBOXED T near (-1765, 272) right-turners waited at their entry
    /// spot with nothing crossing their path while the side-street lane they join held a slow lorry queue. Owner rule: an
    /// exit-room hold outside a junction is yellow-box behaviour only. Finding: the hold is the GAME's own (CarLaneSpeedIterator
    /// IterateNextLane -> CheckSpace, decompile 2026-11-13 lines ~430-480 / 642-...): entering a junction lane with turn /
    /// forward flags from an approach lane, a car stops at the entry when the junction lane has a true crossing overlap and
    /// the next lane has no space for its length. Junction Rules adds no room hold at an unboxed junction; these tests pin that.
    /// </summary>
    internal static partial class Program
    {
        private struct G25Elements : IPathElements
        {
            public int[] K; public float[] S, E;
            public int Count => K.Length;
            public void Get(int i, out int kind, out float start, out float end) { kind = K[i]; start = S[i]; end = E[i]; }
        }

        private static void G25Tests()
        {
            // The right turn's path through the T: one crossing (the northbound lane), a merge at its end; no zebra on any arm.
            var path = new G25Elements { K = new[] { ZebraSections.SpotPoint, ZebraSections.SpotPoint }, S = new[] { 0.3f, 0.9f }, E = new[] { 0.5f, 1f } };
            Check(!ZebraSections.Build(ref path, true, false, 18f, 0.9f, 18.5f, 2f, 1f, out _), "no zebra on the turner's path: the zebra room rule does not apply (no layout, no hold)");
            string root = RepoRoot();
            string box = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRYellowBox.cs"));
            Check(box.Contains("if (!m_BoxJunctions || !m_OwnerData.TryGetComponent(lane, out Owner owner) || !m_BoxJunctionData.HasComponent(owner.m_Owner)) return float.MaxValue;"),
                "the junction room hold (BoxHold) returns at once for a junction without a yellow box");
            string copy = File.ReadAllText(Path.Combine(root, "JunctionRules", "Vanilla", "JRCarLaneSpeedIterator.cs"));
            int inBlock = 0, calls = 0;
            bool open = false;
            foreach (string line in copy.Split('\n'))
            {
                if (line.Contains("JR-STAGE2 BEGIN")) open = true;
                if (line.Contains("!CheckSpace(lane, curveOffset, nextLanes, out blocker)")) { calls++; if (open) inBlock++; }
                if (line.Contains("JR-STAGE2 END")) open = false;
            }
            Check(calls == 3 && inBlock == 0, $"the exit-room stop the owner saw is the game's own CheckSpace ({calls} stock calls, {inBlock} inside Junction Rules blocks)");
        }
    }
}
