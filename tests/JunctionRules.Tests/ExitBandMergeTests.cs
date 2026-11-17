using System;
using System.Collections.Generic;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.7 (JR-T-013g, final review F1): the leaving-vehicle band rule (Stage2/JRExitGiveWay.cs ExitBandBlocks, used by
    /// S2-30 / S2-32 / S2-35 / S2-36) reads the same band set as S2-28 BoxHold: only bands of the join lane's carriageway,
    /// touching bands merged into one, and a band reaching the lane end joins the next boxed junction. The job code is
    /// Burst / ECS; this models its loop with the same Core primitives (MedianSplit.InMask, BoxDriving.Band,
    /// ExitBands.Touch) and checks the job source uses them.
    /// </summary>
    internal static partial class Program
    {
        /// <summary>The ExitBandBlocks band loop on one lane (inverse = false): merged bands in edge t order.</summary>
        private static List<(float b0, float b1)> LeavingBands(IList<(float t, uint mask)> exits, int carriageway, float laneLength, float bandLength)
        {
            var merged = new List<(float b0, float b1)>();
            float touch = ExitBands.TouchMetres / laneLength;
            foreach (var (t, mask) in exits)
            {
                if (!MedianSplit.InMask(mask, carriageway)) continue;
                if (!BoxDriving.Band(t, laneLength, bandLength, false, out float nb0, out float nb1)) continue;
                int last = merged.Count - 1;
                if (last >= 0 && ExitBands.Touch(merged[last].b0, merged[last].b1, nb0, nb1, touch))
                {
                    merged[last] = (Math.Min(merged[last].b0, nb0), Math.Max(merged[last].b1, nb1));
                    continue;
                }
                merged.Add((nb0, nb1));
            }
            return merged;
        }

        /// <summary>The band the join point t lies in (its end = where the room test starts), or null: not boxed.</summary>
        private static (float b0, float b1)? JoinBand(List<(float b0, float b1)> bands, float t)
        {
            foreach (var b in bands)
                if (t >= b.b0 - WholePathRule.SpotEpsilon && t <= b.b1 + WholePathRule.SpotEpsilon) return b;
            return null;
        }

        private static void ExitBandMergeTests()
        {
            const float L = 100f, band = 10f;
            int seg = 5 << 8;   // CarriagewayGroup: segment index in the high bits, carriageway in the low byte

            // Divided road: the lane is on carriageway 0; an exit box on the far side (carriageway 1) is not a band here.
            {
                var far = LeavingBands(new[] { (0.5f, MedianSplit.Bit(1)) }, seg | 0, L, band);
                Check(far.Count == 0 && JoinBand(far, 0.5f) == null, "F1 divided road: the opposite side's exit band is ignored for a leaving car");
                var near = LeavingBands(new[] { (0.5f, MedianSplit.Bit(0)) }, seg | 0, L, band);
                Check(JoinBand(near, 0.5f) != null, "F1 divided road: the own side's exit band boxes the join point");
                var undivided = LeavingBands(new[] { (0.5f, MedianSplit.AllCarriageways) }, seg | 0, L, band);
                Check(JoinBand(undivided, 0.5f) != null, "F1 undivided road: the band holds every lane");
            }

            // Touching bands (exits 10 m apart, 10 m bands): one band; joining in the first needs room past the merged end.
            {
                var m = LeavingBands(new[] { (0.40f, MedianSplit.AllCarriageways), (0.50f, MedianSplit.AllCarriageways) }, seg, L, band);
                Check(m.Count == 1, $"F1 touching bands merge into one ({m.Count})");
                var j = JoinBand(m, 0.40f);
                Check(j != null && Math.Abs(j.Value.b1 - 0.55f) < 1e-4f, $"F1 joining in the first band: room past the merged end 0.55 ({j?.b1})");
                Check(Math.Abs(j.Value.b0 - 0.35f) < 1e-4f, "F1 merged band starts at the first band's start");
                var apart = LeavingBands(new[] { (0.30f, MedianSplit.AllCarriageways), (0.60f, MedianSplit.AllCarriageways) }, seg, L, band);
                Check(apart.Count == 2 && Math.Abs(JoinBand(apart, 0.30f).Value.b1 - 0.35f) < 1e-4f, "F1 bands apart stay two: room past the first band's own end");
                var within = LeavingBands(new[] { (0.40f, MedianSplit.AllCarriageways), (0.5009f, MedianSplit.AllCarriageways) }, seg, L, band);
                Check(within.Count == 1, "F1 bands within the 0.1 m touch tolerance merge");
                var mixed = LeavingBands(new[] { (0.40f, MedianSplit.Bit(0)), (0.50f, MedianSplit.Bit(1)) }, seg | 0, L, band);
                Check(mixed.Count == 1 && Math.Abs(mixed[0].b1 - 0.45f) < 1e-4f, "F1 a touching band on the far side does not extend the own-side band");
            }

            // Junction join: a merged band ending within JunctionJoinMetres of the lane end joins the boxed junction.
            {
                var m = LeavingBands(new[] { (0.88f, MedianSplit.AllCarriageways), (0.96f, MedianSplit.AllCarriageways) }, seg, L, band);
                var j = JoinBand(m, 0.88f).Value;
                Check(m.Count == 1 && (1f - j.b1) * L <= ExitBands.JunctionJoinMetres, "F1 a merged band reaching the lane end joins the next boxed junction");
                var mid = JoinBand(LeavingBands(new[] { (0.5f, MedianSplit.AllCarriageways) }, seg, L, band), 0.5f).Value;
                Check((1f - mid.b1) * L > ExitBands.JunctionJoinMetres, "F1 a mid-lane band does not join a junction");
            }

            // The job source uses the same band set as BoxHold.
            string src = File.ReadAllText(Path.Combine(RepoRoot(), "JunctionRules", "Stage2", "JRExitGiveWay.cs"));
            int at = src.IndexOf("private unsafe bool ExitBandBlocks(", StringComparison.Ordinal);
            int end = at < 0 ? -1 : src.IndexOf("private bool ConflictBusy(", at, StringComparison.Ordinal);
            string body = at < 0 || end < 0 ? "" : src.Substring(at, end - at);
            // 0.4.10 (G18): the band set is one shared function (JRYellowBox.LaneBands) for BoxHold and ExitBandBlocks.
            string yb = File.ReadAllText(Path.Combine(RepoRoot(), "JunctionRules", "Stage2", "JRYellowBox.cs"));
            int lb = yb.IndexOf("private unsafe bool LaneBands(", StringComparison.Ordinal);
            int lbEnd = lb < 0 ? -1 : yb.IndexOf("private float EnteringHold(", lb, StringComparison.Ordinal);
            string lbody = lb < 0 || lbEnd < 0 ? "" : yb.Substring(lb, lbEnd - lb);
            Check(body.Contains("LaneBands(road, edgeOwner.m_Owner, length, false, carriageway, ref curves, out FixedList128Bytes<float2> merged)")
                && lbody.Contains("MedianSplit.InMask(bands->Mask(i), carriageway)"), "F1 job: ExitBandBlocks filters bands by the join lane's carriageway");
            Check(lbody.Contains("ExitBands.Touch(") && lbody.Contains("ExitBands.HoldTouchMetres(m_VehicleLength, m_RoomMargin) / laneLength"), "F1 job: ExitBandBlocks merges touching bands (0.4.9: the vehicle's own length + margin, at least 2 m)");
            Check(body.Contains("ExitBands.JunctionJoinMetres") && body.Contains("NextBoxedJunction(road)"), "F1 job: ExitBandBlocks joins a boxed junction at the lane end");
            Check(body.Contains("HasRoomPast(road, b1, false, length, t, joined,"), "F1 job: room past the merged band (and joined junction box)");
            Check(src.Contains("m_ExitNodeJoin = nodeJoin;\n            m_ExitSignalArm = false;"), "F2 job: the signalled-arm trace reason resets with the node-join flag");
        }
    }
}
