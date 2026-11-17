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

            // --- owner decision option (c): skip the stock room hold at UNBOXED junctions when every crossed lane is clear ---
            const uint turnRight = 0x20u, lights = 0x8000000u, roundabout = 0x400000u;
            Check(StockRoomSkip.Applies(true, true, turnRight, true, false), "unboxed, unsignalled junction lane, whole-junction rule on: the skip may apply");
            Check(!StockRoomSkip.Applies(true, true, turnRight, true, true), "boxed junction: box rules decide (stock hold kept)");
            Check(!StockRoomSkip.Applies(true, true, turnRight | lights, true, false) && !StockRoomSkip.Applies(true, true, turnRight | roundabout, true, false), "signalled / roundabout lanes: stock");
            Check(!StockRoomSkip.Applies(false, true, turnRight, true, false) && !StockRoomSkip.Applies(true, false, turnRight, true, false), "needs the whole-junction rule and the scan lookups");
            Check(StockRoomSkip.IsCrossedLane(0x80 | 0x4 | 0x8, true) && !StockRoomSkip.IsCrossedLane(0x80 | 0x2, true) && !StockRoomSkip.IsCrossedLane(0x80 | 0x40, true), "crossed = the overlaps CheckSpace counts as crossings (no merge / unsafe)");
            // The live T near (-1765, 272): right turn (LHT) crosses the northbound lane, joins the side street where a slow lorry
            // queue stands; northbound lane empty -> the turner enters (waits inside for room).
            var van = StockCarMotion.Of(30f, 3f, 6f);
            float clear = ArrivalTime.Compute(ref van, ExitGiveWay.ClearMetres(0.9f + 3f, 4f, 6.5f), 0f, 8f, 0f, 4f / 15f);
            bool emptyBusy = false;   // no vehicle on the northbound lane or its feeders
            Check(StockRoomSkip.Skip(true, 1, emptyBusy), "unboxed T, slow exit queue, no crossing traffic: the turner enters (stock room hold skipped)");
            // A northbound car 30 m away at 10 m/s: busy for the van's whole-length clear time -> the stock hold stays (waits outside).
            bool approaching = ExitGiveWay.VehicleBlocks(30f, false, 10f, 10f * 10f / 16f, clear, false);
            Check(approaching && !StockRoomSkip.Skip(true, 1, approaching), $"crossing traffic approaching (30 m, 10 m/s, van clears in {clear:0.0} s): waits outside as the game does");
            Check(!ExitGiveWay.VehicleBlocks(120f, false, 10f, 6.25f, clear, false) && StockRoomSkip.Skip(true, 1, false), "the same car 120 m away: clear, the turner enters");
            Check(!StockRoomSkip.Skip(true, 0, false), "no crossing at all: the stock hold had another reason, kept");
            Check(ExitGiveWay.VehicleBlocks(5f, false, 0f, 0f, clear, false), "a vehicle standing 5 m before the crossing (present): busy, waits outside");
            int s54 = copy.IndexOf("JR-STAGE2 BEGIN S2-54", StringComparison.Ordinal);
            int lastCheck = copy.LastIndexOf("!CheckSpace(lane, curveOffset, nextLanes, out blocker)", StringComparison.Ordinal);
            int dist = copy.IndexOf("num2 += m_DistanceOffset;", s54 > 0 ? s54 : 0, StringComparison.Ordinal);
            Check(s54 > lastCheck && lastCheck > 0 && dist > s54 && dist - s54 < 1600, "S2-54 sits right after the stock CheckSpace chain, before num2 += m_DistanceOffset");
            Check(copy.Contains("if (driveSpeed == 0f && blockerType == BlockerType.Continuing && m_JR.SkipStockRoomHold("), "only a stock room hold (Continuing) is dropped, never a STOP-line or signal stop");
            Check(Stage2Counters.Format(2, new long[Stage2Counters.Count], 0, 0, 0, 2).Contains("stock room hold skipped (unboxed, crossings clear) 0"), "counter on the timing line");
        }
    }
}
