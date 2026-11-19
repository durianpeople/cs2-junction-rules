using System;
using System.Collections.Generic;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// JR-T-027 (owner rule 20 Nov 2026, 1.2.0): the box look-past predicts each vehicle ahead from what is visibly ahead: a
    /// vehicle moving away carries on and stops only at the back of a visible standing queue / standing vehicle or a red stop
    /// line. The scenario set and the look-past scenes, with the models' stop point SEEN while closed (as the job sees a red
    /// light), off vs on.
    /// </summary>
    internal static partial class Program
    {
        private struct VisibleStopTotals
        {
            public int SetViolOff, SetViolOn, SetNoseOn, PausesOff, PausesOn, NeedOff, NeedOn, StoodOff, StoodOn, StoodLook, StoodLookDead;
            public bool Drained;
        }

        private static VisibleStopTotals VisibleStopRuns(bool print)
        {
            var t = new VisibleStopTotals { Drained = true };
            s_SimStopVisible = true;
            try
            {
                for (int seed = 1; seed <= 20; seed++)
                    foreach (int which in new[] { 0, 1, 2 })
                    {
                        SimLane Make() => which == 0 ? Alderwood(ExitBands.MergeGapMetres) : which == 1 ? Alderbrook(ExitBands.MergeGapMetres) : Chained(ExitBands.MergeGapMetres);
                        var on = SimRun(Make(), seed, true, 2400, 2400, true, false, out _, true);
                        var off = SimRun(Make(), seed, true, 2400, 2400, false, false, out _, true);
                        t.SetViolOff += off.violations; t.SetViolOn += on.violations; t.SetNoseOn += on.nose;
                        t.Drained &= on.drained && off.drained;
                    }
                foreach (LpScene sc in LpScenes())
                {
                    int pOff = 0, pOn = 0, nOff = 0, nOn = 0, sOff = 0, sOn = 0, sLook = 0;
                    for (int seed = 1; seed <= 20; seed++)
                    {
                        LpResult off = LpRun(sc, seed, false);
                        LpResult on = LpRun(sc, seed, true);
                        pOff += off.Pauses; nOff += off.Needless; sOff += off.StoodSteps;
                        pOn += on.Pauses; nOn += on.Needless; sOn += on.StoodSteps; sLook += on.StoodLook;
                        t.Drained &= on.Drained && off.Drained;
                    }
                    if (print) Console.WriteLine($"  seen stop, scene '{sc.Name}': pauses {pOff} -> {pOn}, needless {nOff} -> {nOn}, stood in box {sOff} -> {sOn} (let in by the look-past {sLook})");
                    t.PausesOff += pOff; t.PausesOn += pOn; t.NeedOff += nOff; t.NeedOn += nOn; t.StoodOff += sOff; t.StoodOn += sOn;
                    if (sc.HardStop) t.StoodLookDead += sLook; else t.StoodLook += sLook;
                }
            }
            finally { s_SimStopVisible = false; s_SimStopAt = float.NaN; }
            if (print)
            {
                Console.WriteLine($"  seen stop, box scenario set (60 runs): standing in a box not caused by a light off {t.SetViolOff} -> on {t.SetViolOn}, nose {t.SetNoseOn}, all drained {t.Drained}");
                Console.WriteLine($"  seen stop, look-past scenes total: pauses {t.PausesOff} -> {t.PausesOn}, needless {t.NeedOff} -> {t.NeedOn}, stood in box {t.StoodOff} -> {t.StoodOn}, let in by the look-past {t.StoodLook} (+ {t.StoodLookDead} dead stop)");
            }
            return t;
        }

        private static void LookPastPredictTests()
        {
            VisibleStopTotals t = VisibleStopRuns(true);
            Check(t.Drained, "seen stop: everything drains on and off (no deadlock)");
            Check(t.SetNoseOn == 0, "seen stop: no nose in a box on the scenario set");
            Check(t.NeedOn < t.NeedOff && t.PausesOn < t.PausesOff, $"seen stop: fewer needless pauses ({t.NeedOff} -> {t.NeedOn}) and pauses ({t.PausesOff} -> {t.PausesOn})");
            // measured 1.2.0 levels (1.1.0 on the same runs: scenario set 0, scenes let in 8 + 0), guarded against growth
            // JR-T-028 (LookPastMinSpeed 5 m/s, was 3: 579 / 282 / 335, pauses 12233, needless 1790): measured 57 / 247 / 288,
            // pauses 12802, needless 1907; small headroom so growth is caught.
            Check(t.SetViolOn <= 62 && t.StoodLook <= 260 && t.StoodLookDead <= 305,
                $"seen stop: standing in a box by cars let in when the stop point closed after they committed stays at the measured level (set {t.SetViolOn} <= 62, scenes {t.StoodLook} <= 260, dead {t.StoodLookDead} <= 305)");
            Check(t.PausesOn <= 13050 && t.NeedOn <= 1960, $"seen stop: pauses and needless pauses stay at the measured level ({t.PausesOn} <= 13050, {t.NeedOn} <= 1960)");
            Check(RoomScan.LookPastMinSpeed == 5f, "JR-T-028: clearly moving = 5 m/s (a crawling, decelerating queue tail counts as standing)");

            // JR-T-028 (review E): the why-slowed readout uses the predicted room only with the look-past on (off = 1.0.1 MinRoomNow)
            string why = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "JunctionRules", "Stage2", "JRWhy.cs"));
            Check(why.Contains("float min = scan.LookPastOn ? scan.MinRoomPredicted : scan.MinRoomNow;")
                && System.Text.RegularExpressions.Regex.Matches(why, @"MinRoomPredicted").Count == 1,
                "JRWhy: MinRoomPredicted is read once, only when scan.LookPastOn (else MinRoomNow)");

            // ---- the rule on plain values (Core RoomScan)
            RoomScan Scan(float sight = 50f) { RoomScan r = RoomScan.Begin(4.2f, 1f); r.FreeNow = true; r.LookPast = true; r.SightLeft = sight; return r; }
            // Fernbrook 878675 (JR-T-026): leader in the box (queued), the next car's rear 1.4 m short of the exit at 17.7 m/s
            RoomScan fern = Scan(21f);
            fern.AddQueued(4.5f, 16.1f, 1, 1);
            fern.Add(-1.4f, 4.5f, 15f, 2, 1, 17.7f, 5.3f);
            fern.FinishLookPast();
            Check(fern.HasRoom && fern.AdmittedByLookPast && fern.LookNeedEnd == 5.2f + 5.5f + 5.5f,
                "Fernbrook 878675: a car moving through and one just past the box with nothing seen to stop them carry on: room (1.1.0: held, worst-case travel)");
            // they carry on to the back of a standing car: room = its rear - everything queued before it
            RoomScan stand = Scan();
            stand.AddQueued(4.5f, 16f, 1, 1);
            stand.Add(-1.4f, 4.5f, 15f, 2, 1, 17.7f, 5.3f);
            stand.Add(14f, 4.5f, 0f, 3, 1, 0f, 0f);   // 14 - 11 = 3 < 5.2
            stand.FinishLookPast();
            Check(!stand.HasRoom && stand.BlockerIndex == 3 && Math.Abs(stand.MinRoomPredicted - 3f) < 1e-4f,
                "a visible standing car: the cars ahead queue at its back; no room for X: held, naming the standing car (predicted room 3 m)");
            RoomScan standOk = Scan();
            standOk.AddQueued(4.5f, 16f, 1, 1);
            standOk.Add(-1.4f, 4.5f, 15f, 2, 1, 17.7f, 5.3f);
            standOk.Add(17f, 4.5f, 0f, 3, 1, 0f, 0f);   // 17 - 11 = 6 >= 5.2
            standOk.FinishLookPast();
            Check(standOk.HasRoom && standOk.AdmittedByLookPast, "a visible standing car far enough on: the queue at its back still leaves X its room: X goes");
            // a red light: its stop line is the stopping point (the old call, no speed)
            RoomScan red = Scan();
            red.Add(-1.4f, 4.5f, 15f, 2, 1, 17.7f, 5.3f);
            red.Add(9f, 0f, 0f, 0, 0);   // 9 - 5.5 = 3.5 < 5.2
            red.FinishLookPast();
            Check(!red.HasRoom && red.BlockerIndex == 0, "a red stop line: the cars ahead stop at it; no room for X: held");
            // crawling (below LookPastMinSpeed) counts where it is
            RoomScan crawl = Scan();
            crawl.Add(2f, 4.5f, 1f, 7, 1, RoomScan.LookPastMinSpeed - 0.5f, 0f);
            crawl.FinishLookPast();
            Check(!crawl.HasRoom && crawl.BlockerIndex == 7, "a crawling car counts where it is (a standing queue)");
            // the road to the carrying-on car's predicted front must be seen
            RoomScan sight = Scan(10f);
            sight.Add(2f, 4.5f, 10f, 7, 1, 8f, 4f);   // needs 5.2 + 5.5 = 10.7 > 10
            sight.FinishLookPast();
            Check(!sight.HasRoom && sight.Refusal == (byte)LookPastRefusal.BeyondSight, "sight: the road up to the carrying-on car's predicted front lies beyond sight: today's rule (held, room_beyond_sight)");
            // a large vehicle past the box hides what lies beyond it: it counts only on its own guaranteed travel less the margin (1.1.0)
            RoomScan big = Scan();
            big.Add(2f, 16.5f, 20f, 3, 1, 10f, 7f, true);   // pred 2 + (7 - 5) = 4 < 5.2
            big.FinishLookPast();
            RoomScan bigFast = Scan();
            bigFast.Add(2f, 16.5f, 20f, 3, 1, 10f, 12f, true);   // pred 2 + 7 = 9 >= 5.2
            bigFast.FinishLookPast();
            Check(!big.HasRoom && big.BlockerIndex == 3 && bigFast.HasRoom, "a view-blocking vehicle past the box: X sees nothing beyond it, so it counts only on its guaranteed travel less the large margin (as 1.1.0)");
        }
    }
}
