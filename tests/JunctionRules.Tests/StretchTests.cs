using System;
using System.IO;
using JunctionRules.Core;
using A = JunctionRules.Core.WholePathRule.YieldAction;
using P = JunctionRules.Core.PointPhase;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.2.1 (JR-T-002c): waiting spots, stretches, the safety re-check while crossing and the decision fix
    /// (Core/StretchRule.cs). The job side (Stage2/JRWholeJunction.cs) only adapts the game's overlap buffer to
    /// IConflictList and calls StretchState.Begin / Classify and the For* mappings tested here.
    /// Car model: curveX = the car's front offset in driving coordinates, origDistance = -margin, so Wait(u) is the
    /// distance from the front to u minus the game's stop margin (the game's num5).
    /// </summary>
    internal static partial class Program
    {
        private const float M = 0.9f;          // the game's stop margin for cars
        private const float Braking = 8f;      // m/s^2, a typical car prefab braking
        private const float Dt = 4f / 15f;     // the navigation job's time step

        /// <summary>VehicleUtils.GetBrakingDistance (cross-checked against the decompile in StretchRepoTests).</summary>
        private static float BrakingDistance(float speed) => 0.5f * speed * speed / Braking + speed * Dt;

        private struct ArrayConflicts : IConflictList
        {
            public (int Flags, int Start, int End, bool Held)[] Items;
            public bool Inverse;
            public int Count => Items.Length;

            public bool Conflict(int i, out float start, out float end)
            {
                var o = Items[i];
                return StretchGeometry.ToConflict(o.Flags, o.Start, o.End, Inverse, o.Held, out start, out end);
            }
        }

        private static (int, int, int, bool) C(float start, float end, int flags = WholePathRule.Road, bool held = false) =>
            (flags, (int)Math.Round(start * 255f, MidpointRounding.AwayFromZero), (int)Math.Round(end * 255f, MidpointRounding.AwayFromZero), held);

        /// <summary>The stretch state of a car whose front is at <paramref name="front"/> (driving coordinates).</summary>
        private static StretchState Car(float len, float front, float speed, params (int, int, int, bool)[] items) =>
            CarOn(len, front, speed, false, false, items);

        private static StretchState CarOn(float len, float front, float speed, bool inverse, bool atLine, params (int, int, int, bool)[] items)
        {
            var list = new ArrayConflicts { Items = items, Inverse = inverse };
            var s = default(StretchState);
            bool active = s.Begin(ref list, -M, len, front, M, BrakingDistance(speed), atLine);
            s.Next = active ? s.Next : -1;
            return s;
        }

        private static float B(int b) => b * WholePathRule.ByteToOffset;

        private static void StretchSpotTests()
        {
            // Byte -> driving coordinates.
            StretchGeometry.ToConflict(WholePathRule.Road, 51, 102, true, false, out float si, out float ei);
            Check(Near(si, 1f - B(102)) && Near(ei, 1f - B(51)), "inverted lane: conflict in driving coordinates");

            // No conflict point: the rule does not cover the lane.
            var none = CarOn(20f, 0f, 0f, false, false, C(0.2f, 0.4f, 0));
            Check(none.Next == -1, "only a pedestrian crossing: no spot, lane stays stock");

            // One group of touching/overlapping conflicts: one spot (W) = the 0.2.0 spot.
            var g = new[] { C(0.30f, 0.45f), C(0.45f, 0.60f), C(0.10f, 0.32f) };
            var s1 = Car(20f, 0f, 0f, g);
            Check(s1.Spots.Count == 1 && Near(s1.Spots[0], B(26)), "touching / overlapping conflicts: one spot = start of the first");
            Check(Near(s1.Spots[0], Spot(false, g)), "entry spot equals the 0.2.0 waiting spot");

            // 0.2.3 (owner clarification 14 Nov 2026): no mid-junction spots, central reservations are not special.
            // Divided road: two carriageways, central reservation 8 m long on a 40 m node lane -> still ONE spot (W).
            var divided = new[] { C(0.05f, 0.15f), C(0.15f, 0.25f), C(0.45f, 0.55f), C(0.55f, 0.65f) };
            var d = Car(40f, 0f, 0f, divided);
            Check(d.Spots.Count == 1 && Near(d.Spots[0], B(13)), "divided road: only the entry spot, no central-reservation spot (0.2.3)");
            var wide = Car(40f, 0f, 0f, C(0.05f, 0.10f), C(0.65f, 0.75f));
            Check(wide.Spots.Count == 1, "even a 22 m gap between conflict points is no waiting spot (0.2.3)");
            Check(d.Spots.StretchOf(B(140)) == 0 && d.Spots.StretchEnd(0) == 2f, "every point belongs to the one stretch");

            // Red-held lanes are not live: they do not place the spot.
            var red = Car(40f, 0f, 0f, C(0.05f, 0.25f, held: true), C(0.45f, 0.55f));
            Check(red.Spots.Count == 1 && Near(red.Spots[0], B(115)), "red-held first lane: the spot is before the first live lane");
            // Track lanes are always live.
            var track = Car(40f, 0f, 0f, C(0.05f, 0.25f, WholePathRule.Track, true), C(0.45f, 0.55f));
            Check(track.Spots.Count == 1 && Near(track.Spots[0], B(13)), "track lane places the spot even under red");
            // Diverging siblings and water do not count.
            var sib = Car(40f, 0f, 0f, C(0f, 1f, WholePathRule.Road | WholePathRule.MergeStart), C(0.05f, 0.25f));
            Check(sib.Spots.Count == 1 && Near(sib.Spots[0], B(13)), "diverging sibling (whole lane) does not place the spot");

            // Inverted lane: the same geometry driven from offset 1.
            var inv = CarOn(40f, 0f, 0f, true, false, C(1f - 0.15f, 1f - 0.05f), C(1f - 0.25f, 1f - 0.15f), C(1f - 0.55f, 1f - 0.45f), C(1f - 0.65f, 1f - 0.55f));
            Check(inv.Spots.Count == 1 && Near(inv.Spots[0], 1f - B(242), 2e-3f), "inverted lane: one spot in driving order");

            // Variant "wait at the stop line": the entry spot moves to the lane start.
            var line = CarOn(40f, 0f, 0f, false, true, divided);
            Check(line.Spots.Count == 1 && line.Spots[0] == 0f, "stop-line variant: entry at the line, nothing else");

            // Many conflict points: still one spot.
            var many = new (int, int, int, bool)[12];
            for (int i = 0; i < many.Length; i++) many[i] = C(i * 0.08f, i * 0.08f + 0.02f);
            var mc = Car(400f, 0f, 0f, many);
            Check(mc.Spots.Count == 1 && mc.Spots.StretchOf(0.95f) == 0, "12 separated points: one spot, one stretch");
        }

        private static void StretchDecisionTests()
        {
            float y, len = 30f;
            // Three conflict points on a 30 m node lane: one stretch.
            var three = new[] { C(0.10f, 0.25f), C(0.30f, 0.45f), C(0.50f, 0.65f) };
            float w = B(26), p2 = B(77), p3 = B(128);
            var atW = Car(len, w - M / len, 0f, three);
            Check(atW.Spots.Count == 1 && atW.Next == 0, "three points, no mid spot: one stretch, car at W decides");

            // Primary W decision unchanged: before W every point holds the car at W (0.2.0 StopAtSpot at the 0.2.0 distance).
            Check(atW.Classify(true, p3, out y) == P.Deciding && Near(y, WholePathRule.WaitDistance(-M, len, w, w - M / len, false)),
                "at W: a busy third point holds the car at W (same distance as 0.2.0)");
            Check(StretchState.ForPrediction(P.Deciding) == WholePathRule.ForYield(true, true, false), "Deciding = 0.2.0 before-commit yield");
            Check(StretchState.ForVehicle(P.Deciding, true, false) == WholePathRule.ForVehicle(true, true, false, true, false)
                  && StretchState.ForVehicle(P.Deciding, false, false) == WholePathRule.ForVehicle(true, true, false, false, false)
                  && StretchState.ForVehicle(P.Deciding, false, true) == WholePathRule.ForVehicle(true, true, false, false, true),
                "Deciding = 0.2.0 before-commit vehicle actions");
            Check(StretchState.ForPedestrian(P.Deciding) == WholePathRule.ForPedestrian(true, true, false), "Deciding = 0.2.0 before-commit pedestrian");
            Check(StretchState.ForPrediction(P.Crossing) == WholePathRule.ForYield(true, true, true)
                  && StretchState.ForVehicle(P.Crossing, true, false) == WholePathRule.ForVehicle(true, true, true, true, false)
                  && StretchState.ForVehicle(P.Crossing, false, false) == WholePathRule.ForVehicle(true, true, true, false, false)
                  && StretchState.UndoProjection(P.Crossing, false) == WholePathRule.UndoProjection(true, true, true, false)
                  && StretchState.UndoProjection(P.Crossing, true) == WholePathRule.UndoProjection(true, true, true, true),
                "Crossing = 0.2.0 after-commit actions (collision braking only)");

            // Point 1 crossed (front at 0.27, inside point 1), 5 m/s: a fast car appears at point 2 -> safety stop
            // before point 2 if comfortable. Braking distance at 5 m/s = 2.9 m.
            float bd5 = BrakingDistance(5f);
            var c1 = Car(len, 0.20f, 5f, three);
            Check(c1.Next == 1 && Near(c1.FirstUncrossed, p2), "front inside point 1: committed to the stretch, first uncrossed = point 2");
            Check(c1.Classify(true, w, out _) == P.Crossing, "point 1 (being crossed) no longer counts: no give-way");
            Check(bd5 <= c1.Wait(p2) + M && c1.Classify(true, p2, out y) == P.Safety && Near(y, c1.Wait(p2)), "fast car at point 2, can stop comfortably: safety stop before point 2");
            Check(StretchState.ForPrediction(P.Safety) == A.StopAtSpot && StretchState.ForVehicle(P.Safety, true, false) == A.StopAtSpot && !StretchState.UndoProjection(P.Safety, false),
                "safety stop keeps the stock look-ahead and stops the car");
            // A fast car at point 3 -> the stop is before the FIRST uncrossed point (point 2), not before point 3.
            Check(c1.Classify(true, p3, out y) == P.Safety && Near(y, c1.Wait(p2)), "fast car at point 3: safety stop before point 2 (first uncrossed)");
            // Cannot stop comfortably (12 m/s, braking distance 12.2 m > 2.1 m): stock collision braking only.
            var fast = Car(len, 0.20f, 12f, three);
            Check(BrakingDistance(12f) > fast.Wait(p2) + M && fast.Classify(true, p3, out _) == P.Crossing, "cannot stop in time: no safety stop (stock collision braking)");
            Check(StretchState.ForVehicle(P.Crossing, false, false) == A.Stock, "...physical cars are still braked for");
            // Comfort is the game's own can-stop test: exactly at the limit stops, a hair beyond does not.
            var edge = c1;
            edge.Braking = edge.Wait(p2) + M;
            Check(edge.Classify(true, p2, out _) == P.Safety, "braking distance == distance to the point: can stop (game's <=)");
            edge.Braking += 1e-3f;
            Check(edge.Classify(true, p2, out _) == P.Crossing, "braking distance > distance: cannot");

            // Waiting after a safety stop: standing before point 2, every remaining point of the stretch holds it.
            var stood = Car(len, p2 - M / len, 0f, three);
            Check(stood.Classify(true, p2, out y) == P.Safety && Near(y, 0f, 1e-3f), "standing at the safety stop: point 2 busy keeps it there");
            Check(stood.Classify(true, p3, out y) == P.Safety && Near(y, 0f, 1e-3f), "standing at the safety stop: point 3 busy keeps it there too (all remaining points)");
            // All clear -> no demand at all -> it continues (nothing to test beyond 'no other hold'); point 1 never holds it again.
            Check(stood.Classify(true, w, out _) == P.Crossing, "the crossed point does not hold the waiting car");
            // Past point 2: only point 3 remains.
            var past2 = Car(len, 0.36f, 3f, three);
            Check(Near(past2.FirstUncrossed, p3) && past2.Classify(true, p2, out _) == P.Crossing && past2.Classify(true, p3, out _) == P.Safety,
                "past point 2: point 2 no longer counts, point 3 still re-checked");

            // Physical car in a later point while crossing: collision braking at THAT point (stock), never a wait further
            // back (deadlock rule); a car asking for space is dropped as in 0.2.0.
            Check(StretchState.ForVehicle(P.Safety, false, false) == A.Stock && StretchState.ForVehicle(P.Safety, false, true) == A.Stock, "physical car while crossing: stock braking at its own point");
            Check(StretchState.ForSpaceRequest(P.Safety) == A.Suppress && StretchState.ForSpaceRequest(P.Crossing) == A.Suppress && StretchState.ForSpaceRequest(P.Deciding) == A.StopAtSpot,
                "space request: held at the spot before it, dropped while crossing (0.2.0)");
            Check(StretchState.ForPedestrian(P.Safety) == A.Stock, "pedestrians while crossing: the game's own pedestrian braking");

            // Rule off: everything stock.
            Check(c1.Classify(false, p2, out _) == P.Stock && atW.Classify(false, p3, out _) == P.Stock, "rule off: stock");
            Check(StretchState.ForPrediction(P.Stock) == A.Stock && StretchState.ForVehicle(P.Stock, true, false) == A.Stock
                  && StretchState.ForPedestrian(P.Stock) == A.Stock && StretchState.ForSpaceRequest(P.Stock) == A.Stock && !StretchState.UndoProjection(P.Stock, false),
                "Stock phase leaves every value to the game");
        }

        private static void StretchRootCauseTests()
        {
            float y, len = 30f;
            var three = new[] { C(0.10f, 0.25f), C(0.30f, 0.45f), C(0.50f, 0.65f) };
            float w = B(26), p3 = B(128);
            // Root cause 1 (0.2.0): after the front passed W every give-way of the path was dropped, although the car's
            // own braking reservation had not yet claimed the later points; the stock test that protects a later point
            // is a continuous one. 0.2.1: those points are re-checked (Safety) until the front reaches them.
            var c = Car(len, 0.20f, 5f, three);
            Check(c.PastEntry && c.Classify(true, p3, out _) == P.Safety, "RC1: committed car still re-checks a later uncrossed point (0.2.0 dropped it)");
            Check(WholePathRule.ForYield(true, true, true) == A.Suppress, "RC1 reference: 0.2.0 after-commit yield was Suppress");

            // Root cause 2, the decision fix: a car that cannot stop at its spot any more (fast, surprised just before W)
            // keeps the game's own give-way for the busy point instead of braking for the unreachable spot, overrunning
            // it and then dropping the give-way (0.2.0).
            float front = w - 3f / len;                     // 3 m before W
            var surprised = Car(len, front, 13f, three);
            Check(!surprised.PastEntry && BrakingDistance(13f) > surprised.Wait(w) + M, "fast car 3 m before W cannot stop at W");
            Check(surprised.Classify(true, p3, out _) == P.Stock, "decision fix: stock give-way for the busy point (not StopAtSpot at an unreachable W)");
            var slow = Car(len, front, 3f, three);
            Check(slow.Classify(true, p3, out y) == P.Deciding && Near(y, slow.Wait(w)), "a car that can stop at W still decides there (primary rule unchanged)");
            // Standing at W the decision is always taken at W.
            var standing = Car(len, w - M / len, 0f, three);
            Check(standing.Classify(true, p3, out _) == P.Deciding, "standing at W: decision at W");
        }

        private static void StretchWholePathTests()
        {
            float y, len = 40f;
            // 0.2.3 (owner clarification 14 Nov 2026): divided road (e.g. crossing Carlisle), near carriageway 0.05-0.25,
            // central reservation 8 m, far carriageway 0.45-0.65: ONE decision at W for the whole path.
            var divided = new[] { C(0.05f, 0.15f), C(0.15f, 0.25f), C(0.45f, 0.55f), C(0.55f, 0.65f) };
            float w = B(13), near2 = B(38), far1 = B(115), far2 = B(140);
            var atW = Car(len, w - M / len, 0f, divided);
            Check(atW.Classify(true, near2, out y) == P.Deciding && Near(y, atW.Wait(w)), "near carriageway busy: wait at W");
            Check(atW.Classify(true, far2, out y) == P.Deciding && Near(y, atW.Wait(w)), "far carriageway busy, near clear: wait at W too (no central-reservation wait)");
            // Crossing the near half, a busy far carriageway is a safety re-check (stop before the first uncrossed point) if it can stop.
            var crossingNear = Car(len, 0.10f, 3f, divided);
            Check(crossingNear.PastEntry && Near(crossingNear.FirstUncrossed, B(38)), "front inside the first near lane: first uncrossed = next near lane");
            Check(crossingNear.Classify(true, far2, out y) == P.Safety && Near(y, crossingNear.Wait(B(38))), "far busy while crossing the near half: safety stop before the FIRST uncrossed point");
            // On the reservation (past the near half), first uncrossed = far1; it waits there until all remaining points are clear.
            var onRes = Car(len, far1 - M / len, 0f, divided);
            Check(Near(onRes.FirstUncrossed, far1) && onRes.Classify(true, far1, out y) == P.Safety && Near(y, 0f, 1e-3f), "standing before the far carriageway after a safety stop: far1 busy holds it");
            Check(onRes.Classify(true, far2, out y) == P.Safety && Near(y, 0f, 1e-3f), "...far2 busy holds it there too (all remaining points)");
            Check(onRes.Classify(true, near2, out _) == P.Crossing, "crossed near points never hold it again");
            // Cannot stop before the first uncrossed point: stock collision braking only.
            var fastRes = Car(len, far1 - 2f / len, 10f, divided);
            Check(fastRes.Classify(true, far2, out _) == P.Crossing, "too fast to stop before the far carriageway: no safety stop");
            // A green turner across two oncoming lanes 1 m apart: one spot, both lanes in one decision.
            var turn = new[] { C(B(102), B(127)), C(B(140), B(166)) };
            var t = Car(20f, B(102) - M / 20f, 0f, turn);
            Check(t.Spots.Count == 1 && t.Classify(true, B(140), out y) == P.Deciding && Near(y, 0f, 1e-3f), "green turner: one spot, both oncoming lanes in one decision");
        }

        private static void StretchDeadlockTests()
        {
            // Two committed cars, each safety-stopped, facing each other's points. A safety stop waits only on
            // predictions (a blocking reservation or the 2 s look-ahead); a physical car in a point gives stock
            // collision braking at that point (as stock and 0.2.0); a space request is dropped while crossing. A standing
            // car makes no prediction: its look-ahead adds speed * 2 s = 0, and its forward reservation is its braking
            // distance at speed 0 (nothing past its own position; its current lane is never reserved, only its curve
            // position). So no safety-stopped car can be the reason another one waits except physically, which is the
            // game's own collision braking. Spot waiters stand outside every path (entry spot before the first point,
            // and since 0.2.3 no mid spots), so they cannot hold anyone either.
            Check(StretchState.ForVehicle(P.Safety, false, false) == A.Stock, "physical blocker while crossing: stock collision braking only");
            Check(StretchState.ForSpaceRequest(P.Safety) == A.Suppress, "space request from a standing car never holds a crossing car");
            Check(BrakingDistance(0f) == 0f && 0f * 2f == 0f, "standing car: no forward reservation, no look-ahead");
            // 0.2.3: never a mid spot; the one spot is the 0.2.0 entry spot.
            var rnd = new Random(7);
            bool fitOk = true;
            for (int k = 0; k < 400; k++)
            {
                int n = rnd.Next(1, 7);
                var items = new (int, int, int, bool)[n];
                for (int i = 0; i < n; i++) { int a = rnd.Next(0, 240); items[i] = (WholePathRule.Road, a, Math.Min(255, a + rnd.Next(5, 60)), false); }
                float len = 10f + (float)rnd.NextDouble() * 50f;
                var s = Car(len, 0f, 0f, items);
                if (s.Spots.Count != 1 || !Near(s.Spots[0], Spot(false, items))) fitOk = false;
            }
            Check(fitOk, "random lanes: exactly one spot, the 0.2.0 entry spot (no mid spots, 0.2.3)");
        }

        private static void StretchRepoTests()
        {
            string decomp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Code", "decomp", "yb-2026-11-13", "full", "Game.Vehicles", "VehicleUtils.cs");
            if (File.Exists(decomp))
            {
                string t = File.ReadAllText(decomp);
                Check(t.Contains("return 0.5f * speed * speed / prefabCarData.m_Braking + speed * timeStep;"), "GetBrakingDistance formula as mirrored in the tests");
                Check(t.Contains("currentSpeed + new float2(0f - prefabCarData.m_Braking, y) * timeStep"), "CalculateSpeedRange brakes at most at m_Braking (one braking rate: 'comfortable' = stock braking)");
            }
            else
            {
                Console.WriteLine("note: decompile not found, braking formula not cross-checked");
            }
            string iterDecomp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Code", "decomp", "yb-2026-11-13", "CarLaneSpeedIterator.cs");
            if (File.Exists(iterDecomp))
            {
                Check(File.ReadAllText(iterDecomp).Contains("bool flag = VehicleUtils.GetBrakingDistance(m_PrefabCar, m_CurrentSpeed, m_TimeStep) <= num5 + num;"),
                    "the game's own can-stop test per overlap (the 'comfortable' definition)");
            }
            string root = RepoRoot();
            if (root == null) { Check(false, "repo root found"); return; }
            string iter = File.ReadAllText(Path.Combine(root, "JunctionRules", "Vanilla", "JRCarLaneSpeedIterator.cs"));
            Check(iter.Contains("m_JR.BeginLane(lane, bufferData, ref m_LaneSignalData, ref m_CarLaneData, ref m_LaneConditionData, curveOffset.x, length, origDistance, num, inverse, m_PrefabCar, m_CurrentSpeed, m_TimeStep, speedLimit);"),
                "S2-04 passes the game's speed, time step and the lane's speed limit (comfort test, arrival time)");
            Check(iter.Contains("maxBrakingSpeed3 = m_JR.SpaceYield("), "S2-07 uses the space-request mapping");
            string job = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            Check(job.Contains("VehicleUtils.GetBrakingDistance(car, currentSpeed, timeStep)"), "job side uses the game's braking distance");
            string core = File.ReadAllText(Path.Combine(root, "JunctionRules", "Core", "StretchRule.cs"));
            string code = System.Text.RegularExpressions.Regex.Replace(core, @"//.*", "");
            Check(!System.Text.RegularExpressions.Regex.IsMatch(code, @"\bnew\s+\w"), "stretch rule code has no 'new' (no managed allocation)");
            Check(Stage2Counters.SafetyStops == 4 && Stage2Counters.SafetyStopEvents == 5 && Stage2Counters.Count == 24 + 2 * PointBuckets.Count, "0.2.4 counters (0.2.3 set + four feeding-scan + two turn counters) + 0.3.2 four crossing counters + 0.3.3 three");
            Check(Stage2Counters.IsBinding(true, 1f, 1f) && !Stage2Counters.IsBinding(true, 2f, 1f) && !Stage2Counters.IsBinding(false, 0f, 1f), "safety counter counts braking or standing on the stop");
        }
    }
}
