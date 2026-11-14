using System;
using System.IO;
using JunctionRules.Core;
using A = JunctionRules.Core.WholePathRule.YieldAction;

namespace JunctionRules.Tests
{
    /// <summary>
    /// Stage 2 (JR-T-002): the whole-junction rule as pure arithmetic (Core/WholePathRule.cs), the setting handoff and
    /// the debug counters. The job-side caller (Stage2/JRWholeJunction.cs) only feeds these functions the game's numbers.
    /// Offsets are curve positions 0..1 along the car's junction (node) lane; overlap ends are the game's bytes (x/255).
    /// </summary>
    internal static partial class Program
    {
        private const int Road = WholePathRule.Road;
        private const int Track = WholePathRule.Track;

        private static bool Near(float a, float b, float eps = 1e-4f) => Math.Abs(a - b) <= eps;

        /// <summary>The waiting-spot search exactly as JRWholeJunction.BeginLane runs it.</summary>
        private static float Spot(bool inverse, params (int Flags, int Start, int End, bool Held)[] overlaps)
        {
            float spot = WholePathRule.NoSpot(inverse);
            foreach (var o in overlaps) spot = WholePathRule.AddConflict(spot, o.Flags, o.Start, o.End, inverse, o.Held);
            return spot;
        }

        private static void WholePathRuleTests()
        {
            // Which overlaps get the game's yield test (vanilla: (flags & MergeStart|MergeMiddleStart) == 0, End flags inverted).
            Check(!WholePathRule.IsYieldChecked(WholePathRule.MergeStart, false), "diverging sibling (MergeStart) is not yield-checked");
            Check(WholePathRule.IsYieldChecked(WholePathRule.MergeStart, true), "MergeStart is yield-checked on an inverted lane");
            Check(!WholePathRule.IsYieldChecked(WholePathRule.MergeMiddleEnd, true), "MergeMiddleEnd is not yield-checked on an inverted lane");
            Check(WholePathRule.IsYieldChecked(WholePathRule.MergeEnd | Road, false), "merge into the same exit (MergeEnd) is yield-checked");

            // Which conflicts place the waiting spot.
            Check(WholePathRule.DefinesWaitingSpot(Road, false, false), "road crossing places the spot");
            Check(!WholePathRule.DefinesWaitingSpot(Road, false, true), "road lane held by a red signal does not place the spot");
            // Amber is live (owner ruling 14 Nov 2026): only red (LaneSignalType.Stop) holds a lane.
            Check(WholePathRule.IsHeldByRed(WholePathRule.SignalStop), "red holds the other lane");
            Check(!WholePathRule.IsHeldByRed(WholePathRule.SignalSafeStop), "amber does not hold the other lane (live)");
            Check(!WholePathRule.IsHeldByRed(0) && !WholePathRule.IsHeldByRed(3) && !WholePathRule.IsHeldByRed(4), "none / yield / go are live");
            Check(WholePathRule.DefinesWaitingSpot(Road, false, WholePathRule.IsHeldByRed(WholePathRule.SignalSafeStop)), "amber lane places the spot");
            Check(WholePathRule.DefinesWaitingSpot(Track, false, true) && WholePathRule.DefinesWaitingSpot(Track | Road, false, true), "track always places the spot");
            Check(!WholePathRule.DefinesWaitingSpot(0, false, false), "pedestrian crossing (no Road/Track flag) never places the spot");
            Check(!WholePathRule.DefinesWaitingSpot(Road | WholePathRule.Water, false, false), "water never places the spot");
            Check(!WholePathRule.DefinesWaitingSpot(Road | WholePathRule.MergeStart, false, false), "diverging sibling does not place the spot");

            // Waiting spot = last point before the first overlap (owner ruling: the start of the first path crossing).
            float s = Spot(false, (Road, 120, 150, false), (Road, 64, 100, false), (Road | WholePathRule.MergeStart, 0, 255, false));
            Check(Near(s, 64 / 255f), "waiting spot = start of the first conflict (not the sibling, not a later one)");
            Check(WholePathRule.HasSpot(s, false) && !WholePathRule.HasSpot(Spot(false, (0, 10, 20, false)), false), "no spot when only pedestrians conflict");
            float si = Spot(true, (Road, 100, 180, false), (Road, 40, 90, false));
            Check(Near(si, 180 / 255f), "inverted lane: spot is the upper end of the nearest conflict");

            // Node-edge conflict -> the spot is the stop line: distance to it equals the game's distance to the lane start.
            float e = Spot(false, (Road, 0, 30, false), (Road, 100, 130, false));
            Check(e == 0f, "conflict starting at the node edge puts the spot at offset 0");
            float origDistance = 7.3f, length = 22f;
            float stockNum5ForEdgeOverlap = origDistance + length * (0f - 0f); // game's num5 for an overlap starting at 0
            Check(Near(WholePathRule.WaitDistance(origDistance, length, e, 0f, false), stockNum5ForEdgeOverlap), "node-edge spot: wait distance = the game's stop distance at the line");

            // Wait distance uses the game's num5 formula.
            Check(Near(WholePathRule.WaitDistance(10f, 20f, 0.25f, 0f, false), 15f), "wait distance forward");
            Check(Near(WholePathRule.WaitDistance(10f, 20f, 0.75f, 1f, true), 15f), "wait distance inverted");
            Check(Near(WholePathRule.WaitDistance(-2f, 20f, 0.25f, 0.5f, false), -7f), "wait distance behind the car is negative");

            // Commitment.
            // Commitment (owner ruling 14 Nov 2026): only a front past the spot commits; speed and reservations do not.
            Check(!WholePathRule.IsCommitted(10f, 0.9f), "before the spot: not committed");
            Check(!WholePathRule.IsCommitted(2f, 0.9f), "fast car that could not stop before the spot: still not committed (no braking-distance commit)");
            Check(!WholePathRule.IsCommitted(0f, 0.9f) && !WholePathRule.IsCommitted(-0.9f, 0.9f), "car standing at the spot (game's stop point) is not committed");
            Check(WholePathRule.IsCommitted(-1f, 0.9f), "front past the spot = committed");
            Check(WholePathRule.FrontPastSpot(-1f, 0.9f) && !WholePathRule.FrontPastSpot(-0.5f, 0.9f), "front past spot uses the game's margin");

            // Scope.
            Check(WholePathRule.AtOrAfterSpot(0.25f, 0.25f, false) && WholePathRule.AtOrAfterSpot(0.5f, 0.25f, false) && !WholePathRule.AtOrAfterSpot(0.2f, 0.25f, false), "at/after spot forward");
            Check(WholePathRule.AtOrAfterSpot(0.2f, 0.25f, true) && !WholePathRule.AtOrAfterSpot(0.3f, 0.25f, true), "at/after spot inverted");
            Check(!WholePathRule.AfterSpot(0.25f, 0.25f, false) && WholePathRule.AfterSpot(0.3f, 0.25f, false), "after spot excludes the first conflict");

            // Actions: rule off = always the game's own.
            Check(WholePathRule.ForYield(false, true, false) == A.Stock && WholePathRule.ForYield(false, true, true) == A.Stock, "rule off: yields stock");
            Check(WholePathRule.ForVehicle(false, true, false, true, false) == A.Stock && WholePathRule.ForPedestrian(false, true, false) == A.Stock, "rule off: vehicles and pedestrians stock");
            Check(!WholePathRule.UndoProjection(false, true, true, false), "rule off: look-ahead kept");
            Check(WholePathRule.ForYield(true, false, false) == A.Stock && WholePathRule.ForVehicle(true, false, true, true, false) == A.Stock, "out of scope: stock");

            // Before commit: every give-way of the path stops the car at the spot.
            Check(WholePathRule.ForYield(true, true, false) == A.StopAtSpot, "before commit: reservation yield stops at the spot");
            Check(WholePathRule.ForVehicle(true, true, false, true, false) == A.StopAtSpot, "before commit: approaching priority car stops at the spot");
            Check(WholePathRule.ForVehicle(true, true, false, false, false) == A.StopAtSpot, "before commit: car on a crossing stops at the spot");
            Check(WholePathRule.ForVehicle(true, true, false, false, true) == A.Stock, "before commit: car ahead on the merge is followed as the game does");
            Check(WholePathRule.ForPedestrian(true, true, false) == A.StopAtSpot, "before commit: pedestrian stops at the spot");
            Check(Near(WholePathRule.Distance(A.StopAtSpot, 30f, 4f), 4f) && Near(WholePathRule.Distance(A.Stock, 30f, 4f), 30f) && Near(WholePathRule.Distance(A.Suppress, 30f, 4f), 30f), "distance per action");

            // No second give-way stop after commit (collision braking stays).
            Check(WholePathRule.ForYield(true, true, true) == A.Suppress, "after commit: reservation yield dropped");
            Check(WholePathRule.ForVehicle(true, true, true, true, false) == A.Suppress, "after commit: look-ahead give-way dropped");
            Check(WholePathRule.ForVehicle(true, true, true, false, false) == A.Stock && WholePathRule.ForVehicle(true, true, true, false, true) == A.Stock, "after commit: physical cars still braked for (collision)");
            Check(WholePathRule.ForPedestrian(true, true, true) == A.Stock, "after commit: pedestrian braking stays");
            Check(WholePathRule.UndoProjection(true, true, true, false) && !WholePathRule.UndoProjection(true, true, true, true) && !WholePathRule.UndoProjection(true, true, false, false), "after commit only, and not for a car already past the overlap start");
        }

        private static void WholePathScenarioTests()
        {
            const float margin = 0.9f;
            // Right turn across oncoming on a green (left-hand traffic). Node lane length 20 m. Side-road lanes (red)
            // cross at 0.10-0.20; two oncoming lanes cross at 0.40-0.50 and 0.55-0.65; exit merge at 0.95.
            var turn = new (int, int, int, bool)[]
            {
                (Road, 25, 51, true), (Road, 102, 127, false), (Road, 140, 166, false), (Road | WholePathRule.MergeEnd, 242, 255, false),
            };
            float spot = Spot(false, turn);
            Check(Near(spot, 102 / 255f), "green turner: spot before the first oncoming lane (red side-road lanes ignored)");
            float len = 20f;
            // Car standing at the stop line (front at offset 0, origDistance after the game's margin = -0.9).
            float wait = WholePathRule.WaitDistance(-margin + 0f, len, spot, 0f, false);
            bool committed = WholePathRule.IsCommitted(wait, margin);
            Check(!committed && wait > 7f, "green turner at the line may pull forward ~8 m to the spot");
            // Both oncoming lanes are judged from the spot: the far lane busy keeps it at the spot (one decision).
            Check(WholePathRule.AtOrAfterSpot(140 / 255f, spot, false) && WholePathRule.ForYield(true, true, committed) == A.StopAtSpot, "far oncoming lane busy: wait at the spot, not between the lanes");
            Check(WholePathRule.AfterSpot(140 / 255f, spot, false), "far oncoming lane counts as a later conflict (counter)");
            // Car standing at the spot: still not committed; once moving it cannot stop before the spot -> committed.
            float atSpot = WholePathRule.WaitDistance(-margin + len * spot, len, spot, spot, false);
            Check(!WholePathRule.IsCommitted(atSpot, margin), "standing at the spot: decision still open");
            float inside = WholePathRule.WaitDistance(-margin, len, spot, spot + 0.05f, false);
            Check(WholePathRule.IsCommitted(inside, margin), "moved off the spot: committed");
            Check(WholePathRule.ForYield(true, true, true) == A.Suppress && WholePathRule.ForVehicle(true, true, true, true, false) == A.Suppress, "after the go: no give-way stop for the far oncoming lane");
            // Signal flips while the turner stands in the side road's path: the side-road lanes now place the spot,
            // behind the car's front -> committed, the car clears the junction.
            var flipped = new (int, int, int, bool)[] { (Road, 25, 51, false), (Road, 102, 127, true), (Road, 140, 166, true) };
            float spot2 = Spot(false, flipped);
            float w2 = WholePathRule.WaitDistance(-margin, len, spot2, spot, false);
            Check(Near(spot2, 25 / 255f) && WholePathRule.IsCommitted(w2, margin), "signal flip: car inside a now-green path is committed and clears");

            // STOP sign: the game's own full stop at the line happens before the junction lane (IterateNextLane Stop
            // flag); after it, with the spot inside the junction, the car may roll up to the spot.
            var stopArm = new (int, int, int, bool)[] { (Road, 38, 64, false), (Road, 128, 153, false) };
            float sp = Spot(false, stopArm);
            float fromLine = WholePathRule.WaitDistance(-margin, len, sp, 0f, false);
            Check(!WholePathRule.IsCommitted(fromLine, margin) && fromLine > 1f, "STOP: after the stop at the line, roll-up to the spot is allowed");
            Check(Near(WholePathRule.Distance(WholePathRule.ForYield(true, true, false), -margin, fromLine), fromLine), "STOP: a busy later conflict holds the car at the spot, not at the line");
            // STOP where the first conflict starts at the node edge: the spot is the line itself.
            float spEdge = Spot(false, (Road, 0, 26, false), (Road, 128, 153, false));
            Check(Near(WholePathRule.WaitDistance(-margin, len, spEdge, 0f, false), -margin), "node-edge conflict: car stays at the stop line");

            // "Wait at the stop line" variant: the found spot moves to the lane start (also for the green turner);
            // no spot (only pedestrians) stays no spot.
            Check(WholePathRule.ApplyVariant(spot, false, true) == 0f && WholePathRule.ApplyVariant(0.7f, true, true) == 1f, "variant: spot at the stop line");
            Check(WholePathRule.ApplyVariant(spot, false, false) == spot, "default: spot unchanged");
            Check(!WholePathRule.HasSpot(WholePathRule.ApplyVariant(WholePathRule.NoSpot(false), false, true), false), "variant keeps 'no spot'");

            // Rule off: everything stock whatever the geometry.
            Check(WholePathRule.ForYield(false, true, false) == A.Stock && WholePathRule.ForVehicle(false, true, true, true, false) == A.Stock, "stock rule leaves every decision to the game");
        }

        /// <summary>Owner rulings of 14 Nov 2026 (JR-T-002b): amber live, decide only at the spot, exit queues stock, roundabouts per node.</summary>
        private static void Stage2RulingTests()
        {
            const float margin = 0.9f, len = 20f;
            // Right turn across two oncoming lanes (left-hand traffic), on a node lane of 20 m: oncoming lanes cross at
            // 0.40-0.50 and 0.55-0.65, exit merge 0.95-1.0. Spot W = 0.40 (8 m in).
            var turn = new (int, int, int, bool)[] { (Road, 102, 127, false), (Road, 140, 166, false), (Road | WholePathRule.MergeEnd, 242, 255, false) };
            float w = Spot(false, turn);
            Check(Near(w, 102 / 255f), "turners: W before the first oncoming lane");
            float Wd(float carX) => WholePathRule.WaitDistance(-margin, len, w, carX, false);

            // A stands at W (front at the game's stop point): decides there.
            float aAtW = Wd(w - margin / len);
            Check(Near(aAtW, 0f) && !WholePathRule.IsCommitted(aAtW, margin), "A at W: not committed, decision open");
            Check(WholePathRule.ForVehicle(true, true, false, true, false) == A.StopAtSpot && Near(WholePathRule.Distance(A.StopAtSpot, 12f, aAtW), aAtW), "A at W, far oncoming lane busy: waits at W");
            // A goes: front past W -> committed, no give-way inside.
            float aGone = Wd(w + 0.1f);
            Check(WholePathRule.IsCommitted(aGone, margin) && WholePathRule.ForYield(true, true, true) == A.Suppress, "A past W: committed, crosses both oncoming lanes in one go");

            // B queued behind A on the same node lane, 2 m in, standing or moving at any speed: never committed behind W.
            float bBehind = Wd(0.1f);
            Check(bBehind > 4f && !WholePathRule.IsCommitted(bBehind, margin), "B behind W: not committed (no braking-distance, reservation or leader commit)");
            // B on the approach (look-ahead lane): the wait distance is positive, so never committed to that lane.
            float bApproach = WholePathRule.WaitDistance(15f, len, w, 0f, false);
            Check(!WholePathRule.IsCommitted(bApproach, margin), "B still on the approach: not committed to the junction lane");
            // A's gap is not B's: a busy conflict while B follows A stops B at W (not at the conflict, not through).
            Check(WholePathRule.ForVehicle(true, true, false, true, false) == A.StopAtSpot && Near(WholePathRule.Distance(A.StopAtSpot, 14f, bBehind), bBehind), "B following A, oncoming car within the stock gap: B stops at W, no tailgating through");
            Check(WholePathRule.ForYield(true, true, false) == A.StopAtSpot, "B following A, reservation on an oncoming lane: B stops at W");
            Check(!WholePathRule.UndoProjection(true, true, false, false), "B behind W keeps the stock 2 s look-ahead of oncoming cars");
            // B rolls up to W and decides on its own there.
            float bAtW = Wd(w - margin / len);
            Check(!WholePathRule.IsCommitted(bAtW, margin), "B at W: decision open");
            Check(WholePathRule.ForVehicle(true, true, false, true, false) == A.StopAtSpot, "B at W, gap busy: waits at W");
            Check(WholePathRule.IsCommitted(Wd(w + 0.05f), margin) && WholePathRule.ForVehicle(true, true, true, true, false) == A.Suppress, "B at W, gap clear: goes; past W it is committed");

            // Exit queue: stock car-following, before and after commit (no room check, yellow boxes are stage 3).
            Check(WholePathRule.ForVehicle(true, true, true, false, true) == A.Stock, "committed car behind a queue on the exit merge: stock following");
            Check(WholePathRule.ForVehicle(true, true, false, false, true) == A.Stock, "car before W behind a queue on the exit merge: stock following");
            Check(WholePathRule.ForVehicle(true, true, true, false, false) == A.Stock, "committed car with a car physically in its path: stock collision braking");

            // Amber lanes are live and place W; red does not.
            var amber = new (int, int, int, bool)[] { (Road, 25, 51, WholePathRule.IsHeldByRed(WholePathRule.SignalSafeStop)), (Road, 102, 127, false) };
            var red = new (int, int, int, bool)[] { (Road, 25, 51, WholePathRule.IsHeldByRed(WholePathRule.SignalStop)), (Road, 102, 127, false) };
            Check(Near(Spot(false, amber), 25 / 255f), "amber side-road lane: W before it");
            Check(Near(Spot(false, red), 102 / 255f), "red side-road lane: W after it (before the first live lane)");
            // Signal flip, new W ahead of the car (advisory a): the car rolls up to it and decides again.
            float carX = 10 / 255f;
            Check(!WholePathRule.IsCommitted(WholePathRule.WaitDistance(-margin, len, Spot(false, amber), carX, false), margin), "signal flip with the new W ahead: not committed, rolls up and decides again");

            // Node-edge / no-crossing paths: no road or track conflict -> no spot -> stock.
            Check(!WholePathRule.HasSpot(Spot(false), false), "lane without overlaps: no spot");
            Check(!WholePathRule.HasSpot(Spot(false, (Road | WholePathRule.MergeStart, 0, 255, false)), false), "only a diverging sibling: no spot (stock)");
            Check(!WholePathRule.HasSpot(Spot(false, (0, 30, 60, false)), false), "only a pedestrian crossing: no spot (stock)");
            Check(!WholePathRule.HasSpot(Spot(false, (Road, 30, 60, true)), false), "only red-held conflicts: no spot (stock)");

            // Roundabout: one decision per roundabout node lane, from that lane's own overlaps (no ring look-ahead).
            var ring = new (int, int, int, bool)[] { (Road | WholePathRule.MergeStart, 0, 60, false), (Road | WholePathRule.MergeEnd, 120, 255, false) };
            Check(Near(Spot(false, ring), 120 / 255f), "ring lane at a roundabout node: W before the entry merge of that node");
            var entry = new (int, int, int, bool)[] { (Road | WholePathRule.MergeEnd, 90, 255, false) };
            Check(Near(Spot(false, entry), 90 / 255f), "entry lane: W before the ring lane it joins");
        }

        private static void Stage2SwitchTests()
        {
            Check(Stage2Mode.For(JunctionGapRule.Stock, false) == 0, "stock, no log: mode 0 (stage 2 code idle)");
            Check(Stage2Mode.For(JunctionGapRule.WholeJunction, false) == Stage2Mode.Rule, "whole junction: rule bit");
            Check(Stage2Mode.For(JunctionGapRule.Stock, true) == Stage2Mode.Count, "stock with log: count bit only");
            Check(Stage2Mode.For(JunctionGapRule.WholeJunction, true) == (Stage2Mode.Rule | Stage2Mode.Count), "both bits");
            Check(Stage2Mode.RuleName(1) == "whole junction" && Stage2Mode.RuleName(2) == "stock", "rule names");
            Check((int)JunctionGapRule.Stock == 0, "default enum value is stock");
            Check((int)JunctionWaitingSpot.BeforeFirstCrossing == 0, "default waiting spot is before the first crossing");
            Check(Stage2Mode.For(JunctionGapRule.WholeJunction, false, JunctionWaitingSpot.StopLine) == (Stage2Mode.Rule | Stage2Mode.AtLine), "at-line variant bit");
            Check(Stage2Mode.For(JunctionGapRule.Stock, false, JunctionWaitingSpot.StopLine) == 0, "at-line variant does nothing with the stock rule");
            Check(Stage2Mode.RuleName(Stage2Mode.Rule | Stage2Mode.AtLine) == "whole junction (wait at the stop line)", "variant name");

            var sw = new GapRuleSwitch();
            string first = sw.Step(JunctionGapRule.Stock, false, out int m0);
            Check(m0 == 0 && first != null && first.Contains("starts as stock"), "first step logs the starting rule");
            Check(sw.Step(JunctionGapRule.Stock, false, out _) == null, "no change: no log line");
            string ch = sw.Step(JunctionGapRule.WholeJunction, false, out int m1);
            Check(m1 == Stage2Mode.Rule && ch != null && ch.Contains("switched to whole junction"), "switch logs once");
            Check(sw.Step(JunctionGapRule.WholeJunction, true, out int m2) == "junction gap counters on" && m2 == 3, "counters on");
            Check(sw.Step(JunctionGapRule.WholeJunction, false, out _) == "junction gap counters off", "counters off");
            string v = sw.Step(JunctionGapRule.WholeJunction, false, out int m3, JunctionWaitingSpot.StopLine);
            Check(m3 == 5 && v != null && v.Contains("wait at the stop line"), "variant switch logs");

            Stage2Handoff.Publish(3);
            Check(Stage2Handoff.Requested == 3, "handoff carries the mode");
            Stage2Handoff.Publish(0);
            Check(Stage2Handoff.Requested == 0, "handoff back to 0");
        }

        private static void Stage2CountersTests()
        {
            Check(Stage2Counters.IsStandingOn(true, 0f, 0f, 0.1f), "standing car held by the recorded limit");
            Check(!Stage2Counters.IsStandingOn(true, 0f, 0f, 2f), "moving car is not counted");
            Check(!Stage2Counters.IsStandingOn(true, 3f, 0f, 0.1f), "a looser recorded limit is not the reason it stands");
            Check(!Stage2Counters.IsStandingOn(false, 0f, 0f, 0f), "nothing recorded: not counted");

            Check(Stage2Counters.Format(1, null) == "; junction gap rule stock", "suffix without counters");
            Check(Stage2Counters.Format(3, null).Contains("mixed"), "suffix for a switched window");
            var dl = new long[Stage2Counters.Count];
            dl[Stage2Counters.Decisions] = 10; dl[Stage2Counters.WaitedForLater] = 2; dl[Stage2Counters.StoppedInsideCrossing] = 1;
            dl[Stage2Counters.SafetyStops] = 4; dl[Stage2Counters.SafetyStopEvents] = 1; dl[Stage2Counters.ProjectedLaneHits] = 3; dl[Stage2Counters.FeedingLaneHits] = 5;
            dl[Stage2Counters.FeedingScans] = 7; dl[Stage2Counters.FeedingFollowerSkips] = 9; dl[Stage2Counters.FeedingMemoSkips] = 2;
            string f0 = Stage2Counters.Format(2, dl);
            Check(f0 == "; junction gap rule whole junction; car-steps: whole-path decisions 10, waited at entry spot for a later conflict 2, safety stops inside 4 (events 1), stopped inside for a give-way 0, stopped inside behind a crossing car 1; arrival projection: busy only by projection, junction-lane car 3, feeding-lane car 5; feeding scans 7 (skipped: follower 9, memo 2; cap hits 0); turning across oncoming: priority changed 0, turner reservation not waited for 0; points at W 0", "suffix with counters: " + f0);
            dl[Stage2Counters.PointSamples] = 4; dl[Stage2Counters.PointDistanceSumCm] = 4000; dl[Stage2Counters.ArrivalSumMs] = 8000;
            dl[Stage2Counters.DistanceHistogram + 2] = 1; dl[Stage2Counters.DistanceHistogram + 3] = 3;
            dl[Stage2Counters.ArrivalHistogram + 3] = 2; dl[Stage2Counters.ArrivalHistogram + 4] = 2;
            string f = Stage2Counters.Format(2, dl);
            Check(f.EndsWith("; points at W 4, distance from W mean 10.0 m [0-2:0 2-4:0 4-8:1 8-12:3 12-16:0 16-24:0 24-32:0 32+:0 (median in 8-12)], own arrival mean 2.00 s [0-0.5:0 0.5-1:0 1-1.5:0 1.5-2:2 2-2.5:2 2.5-3:0 3-4:0 4+:0 (median in 1.5-2)]"), "suffix with point histograms: " + f);
            Check(!f.Contains("mid-junction"), "0.2.3: no mid-junction counter");
            Check(Stage2Counters.Format(2, new long[] { 1, 2, 3, 4 }) == "; junction gap rule whole junction", "too few counters: rule only");

            var w = new TimingWindow("Junction Rules copy", 4);
            w.Tick(0, 0, 1000, 5);
            w.Add(0.5);
            string line = w.Tick(4, 1000, 1000, 5, "; junction gap rule stock");
            Check(line != null && line.StartsWith("timing Junction Rules copy: frames 0-4") && line.EndsWith("(skipped 0); junction gap rule stock"), "timing line keeps the stage 1 format and appends the rule: " + line);
            var w2 = new TimingWindow("x", 4);
            w2.Tick(0, 0, 1000, 5);
            w2.Add(0.5);
            Check(w2.Tick(4, 1000, 1000, 5).EndsWith("(skipped 0)"), "no suffix: the stage 1 line unchanged");
        }

        private static void Stage2RepoTests()
        {
            // Mirrored flag values equal the game's OverlapFlags (decompile of the game the copy was made from).
            string decomp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Code", "decomp", "yb-2026-11-13", "full", "Game.Net", "OverlapFlags.cs");
            if (File.Exists(decomp))
            {
                string t = File.ReadAllText(decomp);
                Check(t.Contains("MergeStart = 1,") && t.Contains("MergeEnd = 2,") && t.Contains("MergeMiddleStart = 0x10,") && t.Contains("MergeMiddleEnd = 0x20,")
                      && t.Contains("Road = 0x80,") && t.Contains("Track = 0x100,") && t.Contains("Water = 0x800"), "OverlapFlags values match the decompile");
            }
            else
            {
                Console.WriteLine("note: decompile not found, OverlapFlags values not cross-checked");
            }
            Check(WholePathRule.MergeStart == 1 && WholePathRule.MergeEnd == 2 && WholePathRule.MergeMiddleStart == 0x10 && WholePathRule.MergeMiddleEnd == 0x20
                  && WholePathRule.Road == 0x80 && WholePathRule.Track == 0x100 && WholePathRule.Water == 0x800, "mirrored flag constants");
            Check(WholePathRule.ByteToOffset == 0.003921569f, "byte factor is the game's literal");
            string sigDecomp = Path.Combine(Path.GetDirectoryName(decomp), "LaneSignalType.cs");
            if (File.Exists(sigDecomp))
            {
                string[] names = { "None", "Stop", "SafeStop", "Yield", "Go" };
                string body = File.ReadAllText(sigDecomp);
                int ix = 0; bool order = true;
                foreach (string n in names) { int k = body.IndexOf("\t" + n, ix, StringComparison.Ordinal); order &= k > 0; ix = k < 0 ? ix : k; }
                Check(order && WholePathRule.SignalStop == 1 && WholePathRule.SignalSafeStop == 2, "LaneSignalType mirror: Stop = 1, SafeStop = 2 (decompile order)");
            }

            string root = RepoRoot();
            if (root == null) { Check(false, "repo root found"); return; }
            string src = Path.Combine(root, "JunctionRules");
            // Every JR-STAGE2 block in the copies is insert-only: no game identifier rebinding, nothing outside blocks
            // (vanilla_copy.py check proves that part); here: the blocks call only the Stage2 entry points.
            foreach (string f in Directory.GetFiles(Path.Combine(src, "Vanilla"), "*.cs"))
            {
                string[] lines = File.ReadAllLines(f);
                bool inBlock = false, ok = true;
                int blocks = 0;
                string bad = "";
                foreach (string ln in lines)
                {
                    string l = ln.Trim();
                    if (l.StartsWith("// JR-STAGE2 BEGIN")) { ok &= !inBlock; inBlock = true; blocks++; continue; }
                    if (l == "// JR-STAGE2 END") { ok &= inBlock; inBlock = false; continue; }
                    if (!inBlock && !l.StartsWith("//") && (l.Contains("m_JR") || l.Contains("jrInScope") || l.Contains("JRStage2Bridge") || l.Contains("JRWholeJunction"))) { ok = false; bad = l; }
                }
                Check(ok && !inBlock && blocks > 0, Path.GetFileName(f) + ": blocks balanced, not nested, stage 2 names only inside them " + bad);
            }
            string iter = File.ReadAllText(Path.Combine(src, "Vanilla", "JRCarLaneSpeedIterator.cs"));
            int ccl = iter.IndexOf("private void CheckCurrentLane(", StringComparison.Ordinal);
            int cpl = iter.IndexOf("private void CheckOverlappingLanes(", StringComparison.Ordinal);
            Check(ccl > 0 && cpl > ccl && iter.IndexOf("JR-STAGE2", ccl, cpl - ccl, StringComparison.Ordinal) < 0, "car-following (CheckCurrentLane) has no stage 2 block: exit queues stay stock");
            Check(iter.Contains("x = (@float.x = math.min(x, @float.x));"), "roundabout clamp of the game kept");
            bool rbInBlock = false;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(iter, @"// JR-STAGE2 BEGIN[\s\S]*?// JR-STAGE2 END"))
                rbInBlock |= m.Value.Contains("isRoundabout") || m.Value.Contains("m_TempBuffer");
            Check(!rbInBlock, "no stage 2 block touches the roundabout handling");
            Check(typeof(WholePathRule).GetMethod("IsCommitted").GetParameters().Length == 2, "commitment depends only on the wait distance (no speed, reservation or leader input)");
            string sw = File.ReadAllText(Path.Combine(src, "Systems", "CarDrivingSwitchSystem.cs"));
            Check(sw.Contains("Stage2Handoff.Publish(") && sw.Contains("m_GapRule.Step("), "switch system publishes the rule each step");
            string nav = File.ReadAllText(Path.Combine(src, "Vanilla", "JRCarNavigationSystem.cs"));
            int begin = nav.IndexOf("JRStage2Bridge.BeginStep()", StringComparison.Ordinal);
            int sched = nav.IndexOf("JobChunkExtensions.ScheduleParallel(jobData", StringComparison.Ordinal);
            Check(begin > 0 && sched > begin, "copy takes the mode on the main thread before scheduling its job");
            string settings = File.ReadAllText(Path.Combine(src, "Settings", "JunctionRulesSettings.cs"));
            Check(settings.Contains("GapRule = JunctionGapRule.Stock;") && settings.Contains("WaitingSpot = JunctionWaitingSpot.BeforeFirstCrossing;"), "settings default to stock / before the first crossing");
            string bridge = File.ReadAllText(Path.Combine(src, "Stage2", "JRWholeJunction.cs"));
            string bridgeCode = System.Text.RegularExpressions.Regex.Replace(bridge, @"//.*", "");
            Check(!System.Text.RegularExpressions.Regex.IsMatch(bridgeCode, @"\bnew\s+(?!PathNode\()\w"), "job-side struct has no 'new' except the PathNode value type (no managed allocation)");
            string proj = File.ReadAllText(Path.Combine(src, "JunctionRules.csproj"));
            Check(proj.Contains("<Version>0.2.4</Version>"), "version 0.2.4");
        }
    }
}
