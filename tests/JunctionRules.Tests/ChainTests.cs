using System;
using System.Collections.Generic;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.10 (owner 17 Nov 2026, chains of boxes): "per vehicle, walk the boxes along its path; a gap between two boxes counts
    /// as a place it may stop ONLY if the gap fits its whole length (incl. trailers) plus margin. If it does not fit the gap,
    /// treat the boxes as one: the vehicle may not enter the FIRST box unless it has free room for its whole length past the
    /// LAST box of the chain (or in a gap that fits it). Per vehicle length, regardless of whether the paint merged them."
    ///
    /// The job's pipeline on one lane (metres along the lane), with its own Core pieces: per vehicle the boxes closer than
    /// its length + margin are one (ExitBands.Chain with HoldTouchMetres; JRYellowBox.LaneBands), the hold is at the first
    /// box of that chain, room is needed past its end; a further box starting inside the reach is chained in too
    /// (BoxDriving.BandInWindow + RoomScan.ExtendPast: HasRoomPast same-lane block / FurtherBoxes on later lanes); vehicles
    /// standing in the window block, moving ones ahead go first (free now).
    /// </summary>
    internal static partial class Program
    {
        private const float CarLen = 4.2f, ArticLen = 16.5f, ChainMargin = 1f;

        /// <summary>The job's decision for a vehicle before the first box: (its chain's entry, where room is needed from, the room verdict).</summary>
        private static (float entry, float roomFrom, float needEnd, bool room) JobChain(List<(float a, float b)> painted, float length, List<(float rear, float len, bool standing)> ahead)
        {
            painted.Sort((x, y) => x.a.CompareTo(y.a));
            var bands = ExitBands.Chain(painted, ExitBands.HoldTouchMetres(length, ChainMargin));
            var first = bands[0];
            RoomScan scan = RoomScan.Begin(length, ChainMargin);
            scan.FreeNow = true;
            // vehicles inside the chain ahead (queued: standing ones block, moving ones go first)
            var past = new List<(float rear, float len, bool standing)>();
            foreach (var v in ahead)
            {
                if (v.rear < first.b1) { if (v.standing) scan.BlockBy(1, 1); else scan.AddQueued(v.len); }
                else past.Add(v);
            }
            // further boxes on the lane (the job's same-lane block, bands in order, each against the reach at that moment)
            for (int i = 1; i < bands.Count; i++)
                if (BoxDriving.BandInWindow(bands[i].b0, bands[i].b1, first.b1, 1e9f, 1f, 0f, scan.Reach, out float pastEnd))
                    scan.ExtendPast(pastEnd);
            past.Sort((x, y) => x.rear.CompareTo(y.rear));
            foreach (var v in past) scan.Add(v.rear - first.b1, v.len, 0f, 2, 1);
            return (first.b0, first.b1, first.b1 + scan.Needed, scan.HasRoom);
        }

        private static List<(float a, float b)> TwoBoxes(float gap) => new List<(float a, float b)> { (0f, 8f), (8f + gap, 16f + gap) };

        private static void ChainTests()
        {
            var none = new List<(float rear, float len, bool standing)>();
            // ---- two boxes, gaps 2.8 / 6 / 12 / 25 m: car (4.2 m: needs 5.2 m) vs artic (16.5 m with its trailer: needs 17.5 m)
            foreach (float gap in new[] { 2.8f, 6f, 12f, 25f })
            {
                foreach (float len in new[] { CarLen, ArticLen })
                {
                    bool fits = gap >= len + ChainMargin;
                    var r = JobChain(TwoBoxes(gap), len, none);
                    string who = len == CarLen ? "car" : "artic";
                    float lastEnd = 16f + gap;
                    if (fits)
                        Check(r.entry == 0f && Math.Abs(r.roomFrom - 8f) < 1e-4f && Math.Abs(r.needEnd - (8f + len + ChainMargin)) < 1e-4f && r.room,
                            $"chain: {who}, gap {gap} m fits it ({len + ChainMargin:0.0} m): it may stop in the gap: room needed past the FIRST box only (8..{8f + len + ChainMargin:0.0} m)");
                    else
                        Check(r.entry == 0f && Math.Abs(r.roomFrom - lastEnd) < 1e-4f && Math.Abs(r.needEnd - (lastEnd + len + ChainMargin)) < 1e-4f && r.room,
                            $"chain: {who}, gap {gap} m does not fit it: one box 0..{lastEnd} m for it: held before the first box, room needed past the LAST box");
                }
            }
            Check(JobChain(TwoBoxes(17.51f), ArticLen, none).roomFrom == 8f && JobChain(TwoBoxes(17.49f), ArticLen, none).roomFrom > 8f
                && !ExitBands.Joined(8f, 13.5f, 5.5f - 1e-3f) && ExitBands.Joined(8f, 13.49f, 5.5f),
                "boundary (artic): a gap of its length + margin (17.5 m) fits it, 1 cm less does not; Joined is strict");
            Check(JobChain(TwoBoxes(6.0f), CarLen, none).roomFrom == 8f && JobChain(TwoBoxes(5.9f), CarLen, none).roomFrom > 8f,
                "boundary (car): at least the paint's 6 m: a gap under 6 m is painted yellow (one box), so no vehicle stops in it");
            // the gap that fits must also be FREE now: a vehicle standing in it keeps the car out; one far enough on leaves room
            var blocker = new List<(float rear, float len, bool standing)> { (8f + 3f, 4.2f, true) };
            Check(!JobChain(TwoBoxes(12f), CarLen, blocker).room, "car, 12 m gap: a car standing 3 m into the gap: no room (it would stop with its tail in the first box)");
            Check(JobChain(TwoBoxes(12f), CarLen, new List<(float, float, bool)> { (8f + 6f, 4.2f, true) }).room, "car, 12 m gap: a car standing 6 m into the gap leaves 6 m: room");
            Check(!JobChain(TwoBoxes(12f), ArticLen, new List<(float, float, bool)> { (8f + 6f, 4.2f, true) }).room, "artic, same scene: the chain holds a standing car: no room (free now)");
            Check(!JobChain(TwoBoxes(12f), ArticLen, new List<(float, float, bool)> { (8f + 6f, 4.2f, false), (40f, 4.2f, true) }).room,
                "artic: a moving car in the chain goes first and the next standing car 40 m on leaves 40 - 28 - 5.2 < 17.5 m: no room");
            Check(JobChain(TwoBoxes(12f), ArticLen, new List<(float, float, bool)> { (8f + 6f, 4.2f, false), (60f, 4.2f, true) }).room,
                "artic: the same with the standing car 60 m on: room for both past the last box");

            // ---- three boxes in a row: gaps 3 m and 10 m
            var three = new List<(float a, float b)> { (0f, 8f), (11f, 19f), (29f, 37f) };
            var c3 = JobChain(new List<(float, float)>(three), CarLen, none);
            Check(Math.Abs(c3.roomFrom - 19f) < 1e-4f && Math.Abs(c3.needEnd - 24.2f) < 1e-4f, "three boxes, car: boxes 1 + 2 are one for it (3 m gap), it may stop in the 10 m gap: room past box 2");
            var a3 = JobChain(new List<(float, float)>(three), ArticLen, none);
            Check(Math.Abs(a3.roomFrom - 37f) < 1e-4f && Math.Abs(a3.needEnd - 54.5f) < 1e-4f, "three boxes, artic: all three are one for it: room past box 3");
            Check(!JobChain(new List<(float, float)>(three), CarLen, new List<(float, float, bool)> { (21f, 4.2f, true) }).room, "three boxes, car: a vehicle standing in the 10 m gap 2 m in: no room");

            // ---- a merged group (the paint joins 0..8 + 10..18, 2 m apart) + a separate box 8 m after it
            var w = new List<ExitWindow> { new ExitWindow(1, MedianSplit.AllCarriageways, 0, 8), new ExitWindow(1, MedianSplit.AllCarriageways, 10, 18), new ExitWindow(1, MedianSplit.AllCarriageways, 26, 34) };
            var painted = new List<(float a, float b)>();
            foreach (var box in ExitBoxWindows.Assemble(w, ExitBands.MergeGapMetres, (m, f, t) => true)) painted.Add(((float)box.From, (float)box.To));
            Check(painted.Count == 2, "paint: 0..18 as one box (2 m gap) and 26..34 apart (8 m >= 6 m)");
            var cm = JobChain(new List<(float, float)>(painted), CarLen, none);
            Check(Math.Abs(cm.roomFrom - 18f) < 1e-4f, "merged group + box, car: the 8 m gap fits it: room past the merged group");
            var am = JobChain(new List<(float, float)>(painted), ArticLen, none);
            Check(Math.Abs(am.roomFrom - 34f) < 1e-4f && Math.Abs(am.needEnd - 51.5f) < 1e-4f, "merged group + box, artic: the 8 m gap does not fit it: room past the separate box too");

            // ---- across lanes (the job's FurtherBoxes): a box ends 2 m before its lane end; the next lane has a box from 2..10 m
            // (gap 4 m) or from 10..18 m (gap 12 m): path lane read = 2 m to the next lane's start
            foreach (float len in new[] { CarLen, ArticLen })
            foreach (float start in new[] { 2f, 10f })
            {
                RoomScan s = RoomScan.Begin(len, ChainMargin);
                s.FreeNow = true;
                bool ext = BoxDriving.BandInWindow(start, start + 8f, 0f, 50f, 1f, 2f, s.Reach, out float pe);
                if (ext) s.ExtendPast(pe);
                float gap = 2f + start;
                bool fits = gap >= len + ChainMargin;
                Check(ext == !fits && (!ext || Math.Abs(s.Needed - (2f + start + 8f + len + ChainMargin)) < 1e-4f),
                    $"across lanes: {(len == CarLen ? "car" : "artic")}, gap {gap} m: {(fits ? "fits: may stop in the gap" : "does not fit: room needed past the next lane's box")}");
            }
            // the job's band merge per vehicle is the same function the tests use (no separate code path)
            string yb = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "JunctionRules", "Stage2", "JRYellowBox.cs"));
            Check(yb.Contains("float touch = ExitBands.HoldTouchMetres(m_VehicleLength, m_RoomMargin) / laneLength;") && yb.Contains("ExitBands.Touch(merged[last].x, merged[last].y, b0, b1, touch)"),
                "the job merges a lane's boxes per vehicle (whole length incl. trailers + margin) with ExitBands.Touch, whatever the paint did");
        }
    }
}
