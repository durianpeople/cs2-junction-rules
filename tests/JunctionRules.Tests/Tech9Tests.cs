using System;
using System.IO;
using JunctionRules.Core;
using A = JunctionRules.Core.WholePathRule.YieldAction;
using P = JunctionRules.Core.PointPhase;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.3.3-probe. JR-T-005: false give-way at Moonflower Street x Anchor Street (unsafe U-turn lane fed by the
    /// right-turner's approach lane). JR-T-006: pedestrian / cyclist crossings out of the whole-junction gap, judged at
    /// the crossing when the whole vehicle fits before it (Core/CrossingPoint.cs); a clear crossing or a clear vehicle
    /// point gives no hold and no speed cap (owner clarification 15 Nov 2026).
    /// </summary>
    internal static partial class Program
    {
        // ---- live geometry, Moonflower Street x Anchor Street (read-only cs2_junction_lanes curves, 15 Nov 2026) ----
        // xz of the node lanes' Bezier control points (world metres). Width 3 m each (Car Drive Lane 3).
        private static readonly double[] s_Yellow = { -437.71, 33.66, -437.16, 29.78, -434.13, 26.50, -430.21, 26.62 };   // Moonflower S-bound left -> Anchor E-bound
        private static readonly double[] s_Red = { -430.10, 23.62, -435.65, 23.46, -439.95, 28.06, -440.72, 33.56 };      // Anchor W-bound right -> Moonflower N-bound
        private static readonly double[] s_UTurn = { -430.10, 23.62, -432.10, 23.56, -432.21, 26.56, -430.21, 26.62 };    // Anchor W-bound unsafe U-turn -> Anchor E-bound
        private static readonly double[] s_WStraight = { -430.10, 23.62, -435.56, 23.46, -441.03, 23.28, -446.49, 23.08 }; // Anchor W-bound straight

        private static (double, double) Bez(double[] c, double t)
        {
            double u = 1 - t;
            double a = u * u * u, b = 3 * u * u * t, cc = 3 * u * t * t, d = t * t * t;
            return (a * c[0] + b * c[2] + cc * c[4] + d * c[6], a * c[1] + b * c[3] + cc * c[5] + d * c[7]);
        }

        private static double MinDistance(double[] p, double[] q)
        {
            double best = double.MaxValue;
            for (int i = 0; i <= 400; i++)
            {
                var a = Bez(p, i / 400.0);
                for (int j = 0; j <= 400; j++)
                {
                    var b = Bez(q, j / 400.0);
                    double d = Math.Sqrt((a.Item1 - b.Item1) * (a.Item1 - b.Item1) + (a.Item2 - b.Item2) * (a.Item2 - b.Item2));
                    if (d < best) best = d;
                }
            }
            return best;
        }

        private static void MoonflowerTests()
        {
            // The game finds an overlap only where the lanes come within radius1 + radius2 = (width * 0.4) * 2 = 2.4 m
            // (LaneOverlapSystem.CheckOverlaps: radius = (m_Width + m_WidthOffset) * 0.4f; FindOverlapRange).
            const double reach = 3.0 * 0.4 * 2;
            double redYellow = MinDistance(s_Yellow, s_Red);
            Check(redYellow > reach + 0.4, $"Moonflower: red and yellow paths never come within the game's overlap reach ({redYellow:0.00} m > {reach} m): no LaneOverlap between them, for stock or JR");
            Check(MinDistance(s_Yellow, s_WStraight) > reach + 0.4, "Moonflower: yellow does not overlap the Anchor westbound straight either");
            Check(MinDistance(s_Yellow, s_UTurn) < 0.01, "Moonflower: yellow and the east unsafe U-turn end at the same exit lane (MergeEnd overlap, yield-checked for yellow)");
            Check(s_UTurn[0] == s_Red[0] && s_UTurn[1] == s_Red[1], "Moonflower: the unsafe U-turn starts where red starts: both are fed by Anchor westbound (lane 57336/5)");

            // Root cause: S2-12 counts every car on the lane feeding a conflicting junction lane (routes not read). The only
            // conflicting lane of yellow fed by red's approach is the U-turn, which the game flags Unsafe.
            const uint unsafeUturn = TurnAcross.UTurnRight | TurnAcross.Unsafe;
            Check(FeedingScan.SkipsConflictingLane(unsafeUturn), "JR-T-005: no feeding scan for an Unsafe conflicting lane (the Moonflower U-turn)");
            Check(!FeedingScan.SkipsConflictingLane(TurnAcross.Forward), "JR-T-005: the eastbound straight yellow merges into is still scanned (real through traffic)");
            Check(!FeedingScan.SkipsConflictingLane(TurnAcross.TurnRight) && !FeedingScan.SkipsConflictingLane(TurnAcross.TurnLeft | TurnAcross.Stop)
                && !FeedingScan.SkipsConflictingLane(TurnAcross.UTurnRight) && !FeedingScan.SkipsConflictingLane(TurnAcross.RightOfWay),
                "JR-T-005: only the Unsafe flag skips (a safe U-turn lane, turns and signs are scanned as before)");
            // Stock priority for yellow (STOP) against the U-turn (Unsafe, no sign): Stop decides first, yellow gives way
            // (LaneOverlapSystem num4: Stop differs -> +1 for the Stop lane), so a same-priority feeding car wins.
            Check(TurnAcross.SignStatus(TurnAcross.TurnLeft | TurnAcross.Stop) != TurnAcross.SignStatus(unsafeUturn),
                "Moonflower: yellow and the U-turn differ in sign status, so the turn-across rule does not touch the pair (stock delta)");

            // Lindbergh x Anchor (owner check: red gave way to yellow already in the junction) is a physical / reservation
            // hold: stock + S2-06 / S2-11, untouched by the unsafe skip (only S2-12 virtual feeding cars).
            Check(StretchState.ForVehicle(P.Deciding, false, false) == A.StopAtSpot && StretchState.ForPrediction(P.Deciding) == A.StopAtSpot,
                "Lindbergh: a car in the junction (physical) or reserving it still holds the deciding car at its spot");
        }

        private struct RangeList : IOverlapRanges
        {
            public (int Flags, float Start, float End)[] Items;
            public int Count => Items.Length;

            public void Get(int i, out int flags, out float start, out float end)
            {
                flags = Items[i].Flags; start = Items[i].Start; end = Items[i].End;
            }
        }

        private static void CrossingPointTests()
        {
            const int R = WholePathRule.Road;
            Check(CrossingPoint.IsClearancePoint(R, false), "a crossing road lane is a vehicle point for the fit");
            Check(CrossingPoint.IsClearancePoint(WholePathRule.Track, false), "a track is a vehicle point");
            Check(!CrossingPoint.IsClearancePoint(R | WholePathRule.MergeEnd, false) && !CrossingPoint.IsClearancePoint(R | WholePathRule.MergeMiddleEnd, false),
                "a merge into the same exit is queueing, not a vehicle point for the fit");
            Check(!CrossingPoint.IsClearancePoint(R | WholePathRule.MergeStart, false), "a diverging sibling (not yield-checked) does not count");
            Check(!CrossingPoint.IsClearancePoint(0, false), "a pedestrian lane (no Road / Track flag) is not a vehicle point");
            Check(!CrossingPoint.IsClearancePoint(R | WholePathRule.Water, false), "water does not count");
            Check(!CrossingPoint.IsClearancePoint(R | WholePathRule.MergeStart, true) && CrossingPoint.IsClearancePoint(R | WholePathRule.MergeEnd, false) == false
                && CrossingPoint.IsClearancePoint(R | WholePathRule.MergeEnd, true) == false,
                "inverted lane: the end-merge flags are the Start ones (and End ones are not yield-checked)");

            // Right turn, lane 20 m: oncoming lanes 0.30-0.40 and 0.45-0.60, merge into the exit 0.70-1.00, exit crosswalk 0.85-0.95.
            var list = new RangeList { Items = new[] { (R, 0.30f, 0.40f), (R, 0.45f, 0.60f), (R | WholePathRule.MergeEnd, 0.70f, 1.0f), (0, 0.85f, 0.95f) } };
            float prev = CrossingPoint.PreviousEnd(ref list, 0.85f, false);
            Check(Near(prev, 0.60f), "previous vehicle point before the exit crosswalk = end of the far oncoming lane (merge ignored)");
            // Room = 20 * (0.85 - 0.60) - 0.9 = 4.1 m.
            Check(CrossingPoint.Fits(20f, 0.85f, prev, 0.9f, 4.0f), "a 4.0 m car fits in 4.1 m: crosswalk judged at the crosswalk");
            Check(!CrossingPoint.Fits(20f, 0.85f, prev, 0.9f, 4.5f), "a 4.5 m car does not fit: crosswalk stays in the decision at W (0.3.2 behaviour)");
            Check(!CrossingPoint.Fits(20f, 0.85f, prev, 0.9f, 16.5f), "an articulated truck (trailer counted) does not fit");
            // A vehicle point reaching over the crosswalk: nothing fits.
            var over = new RangeList { Items = new[] { (R, 0.50f, 0.90f), (0, 0.85f, 0.95f) } };
            Check(!CrossingPoint.Fits(20f, 0.85f, CrossingPoint.PreviousEnd(ref over, 0.85f, false), 0.9f, 0.1f), "a vehicle point over the crosswalk: never fits");
            // Straight across, no vehicle point before the far crosswalk except W's own: lane start counts as the previous end.
            var none = new RangeList { Items = new[] { (0, 0.80f, 0.90f) } };
            Check(Near(CrossingPoint.PreviousEnd(ref none, 0.8f, false), 0f), "no vehicle point before: room measured from the lane start");
            Check(!CrossingPoint.JudgedAtCrossing(false, true, true), "rule off: nothing changes (stock)");
            Check(!CrossingPoint.JudgedAtCrossing(true, false, true), "a road lane is never taken out of the decision");
            Check(CrossingPoint.JudgedAtCrossing(true, true, true), "rule on, crossing, fits: judged at the crossing");

            // Owner (15 Nov): A, B vehicle points, C crosswalk. At W the car decides on A and B only (C out of scope);
            // inside, Safety re-checks the next uncrossed VEHICLE point; C has its own stock check at C.
            var abc = new (int, int, int, bool)[] { C(0.30f, 0.40f), C(0.45f, 0.60f) };
            StretchState atW = Car(20f, 0.30f - 0.9f / 20f, 0f, abc);
            Check(atW.Classify(true, 0.45f, out float wB) == P.Deciding && Near(wB, atW.Wait(atW.Spots[0])), "at W: B is decided at W with A");
            StretchState pastA = Car(20f, 0.40f, 1f, abc);
            Check(pastA.Classify(true, 0.45f, out float wB2) == P.Safety && Near(wB2, pastA.Wait(0.45f)), "after A, before B: B re-checked, stop just before B if not safe");
        }

        /// <summary>
        /// Owner clarification 15 Nov 2026: no forced stop or slow-down at a clear point. A minimal model of how the
        /// iterator turns demands into a speed: the car's limit is the minimum of GetMaxBrakingSpeed(distance) over the
        /// demands the stock checks raise (a busy lane, a person at the crossing); the rule only moves the distance of a
        /// demand (WholePathRule.Distance) and never adds one. No demand = no cap, at a crossing or a vehicle point.
        /// </summary>
        private static void ClearPointTests()
        {
            const float vmax = 13.9f, dt = 4f / 15f;
            float Brake(float d) => d <= 0f ? 0f : (float)Math.Min(vmax, -Braking * dt + Math.Sqrt(Braking * Braking * dt * dt + 2 * Braking * d));
            float Limit((bool demand, A action, float stock, float wait)[] demands)
            {
                float v = vmax;
                foreach (var d in demands)
                {
                    if (!d.demand) continue;
                    v = Math.Min(v, Brake(WholePathRule.Distance(d.action, d.stock, d.wait)));
                }
                return v;
            }
            // Car 8 m before W; vehicle points A, B and crosswalk C 20 m ahead (out of scope: action Stock).
            float wait = 8f, toC = 20f;
            Check(Limit(new[] { (false, A.StopAtSpot, 9f, wait), (false, A.StopAtSpot, 14f, wait), (false, A.Stock, toC, wait) }) == vmax,
                "all clear: no hold and no speed cap (no forced stop at W, at B or at C)");
            Check(Limit(new[] { (false, A.StopAtSpot, 9f, wait), (false, A.StopAtSpot, 14f, wait), (true, A.Stock, toC, wait) }) == Brake(toC),
                "people at C: the car brakes for C at C (stock distance), not at W");
            Check(Brake(toC) > Brake(wait), "the crosswalk demand is softer than a stop at W");
            Check(Limit(new[] { (true, A.StopAtSpot, 14f, wait), (false, A.Stock, toC, wait) }) == Brake(wait), "B busy: wait at W (one decision for A and B)");
            // Safety inside: B clear -> no cap.
            Check(Limit(new[] { (false, A.StopAtSpot, 3f, 2f) }) == vmax, "inside, B clear: drives through without slowing");
        }

        private static void Tech9RepoTests()
        {
            string root = RepoRoot();
            if (root == null) { Check(false, "repo root found"); return; }
            string jr = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            string it = File.ReadAllText(Path.Combine(root, "JunctionRules", "Vanilla", "JRCarLaneSpeedIterator.cs"));
            int feed = jr.IndexOf("public float FeedingLaneYield", StringComparison.Ordinal);
            int skip = jr.IndexOf("FeedingScan.SkipsConflictingLane", StringComparison.Ordinal);
            int scan = jr.IndexOf("FeedingScan.ShouldScan", StringComparison.Ordinal);
            Check(feed > 0 && skip > feed && skip < scan, "S2-12: the unsafe-lane skip comes before any feeding scan work");
            Check(it.Contains("m_JR.InScope(jrStart, (laneOverlap.m_Flags & overlapFlags) == 0, m_PedestrianLaneData.HasComponent(m_Lane))"),
                "S2-05 tells InScope whether the other lane is a pedestrian lane (a crossing)");
            int inScope = jr.IndexOf("public bool InScope", StringComparison.Ordinal);
            int classify = jr.IndexOf("m_Stretch.Classify(RuleOn, u, out m_OvStopWait)", StringComparison.Ordinal);
            int judged = jr.IndexOf("CrossingPoint.JudgedAtCrossing", StringComparison.Ordinal);
            Check(inScope > 0 && judged > inScope && judged < classify, "a crossing judged at the crossing leaves InScope before it is classified (all its checks stay stock)");
            // The no-push setting (0.3.2) is not gated by the scope, so it still applies at the crossing.
            Check(it.Contains("if (m_JR.m_NoPushPeople) m_MaxSpeed = float.MaxValue;"), "0.3.2 no-push still runs for every crossing (independent of the scope)");
            // The Yield path (reservations, Lindbergh) does not consult the unsafe skip.
            int yield = jr.IndexOf("public float Yield(", StringComparison.Ordinal);
            int yieldEnd = jr.IndexOf("public float SpaceYield(", StringComparison.Ordinal);
            Check(yield > 0 && !jr.Substring(yield, yieldEnd - yield).Contains("SkipsConflictingLane"), "reservations (S2-06) keep holding as before (Lindbergh)");
        }
    }
}
