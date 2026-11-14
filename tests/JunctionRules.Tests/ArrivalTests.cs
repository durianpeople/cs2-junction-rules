using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using JunctionRules.Core;
using A = JunctionRules.Core.WholePathRule.YieldAction;
using P = JunctionRules.Core.PointPhase;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.2.3 (JR-T-002f): the car's own arrival time at a conflict point (Core/ArrivalTime.cs), the projection of the
    /// other cars to that time (junction lanes: longer look-ahead; feeding lanes: virtual reservation), the safety-stop
    /// event counter and the point histograms. The job side (Stage2/JRWholeJunction.cs) wraps the game's own
    /// VehicleUtils functions in ICarMotion; StockCarMotion mirrors them line by line (checked against the decompile
    /// in ArrivalRepoTests).
    /// </summary>
    internal static partial class Program
    {
        // A typical car prefab (CarData): max speed 40 m/s, acceleration 5 m/s^2, braking 8 m/s^2.
        private static StockCarMotion Typical => StockCarMotion.Of(40f, 5f, Braking);

        private struct ArraySlots : IAtomicSlots
        {
            public long[] Values;
            public int Length => Values.Length;
            public long Read(int i) => Values[i];
            public bool CompareExchange(int i, long value, long comparand)
            {
                if (Values[i] != comparand) return false;
                Values[i] = value;
                return true;
            }
        }

        /// <summary>A plain re-derivation of the stepping (no shared code with ArrivalTime.Compute), car on the junction lane.</summary>
        private static float ManualArrival(StockCarMotion m, float distance, float speed, float cap)
        {
            double s = 0, t = 0, v = speed;
            for (int i = 0; i < 1000; i++)
            {
                double acc = (0.5 - Math.Sin(Math.Asin(1 - 2 * Math.Clamp((v - m.MaxSpeed) / (0 - m.MaxSpeed), 0, 1)) / 3)) * m.Acceleration;
                double next = Math.Min(Math.Min(v + acc * Dt, Math.Max(Math.Max(0, v - m.Braking * Dt), m.MaxSpeed)), cap);
                next = Math.Max(next, Math.Max(0, v - m.Braking * Dt));
                double ds = 0.5 * (v + next) * Dt;
                if (s + ds >= distance) return (float)(t + Dt * (distance - s) / ds);
                s += ds; t += Dt; v = next;
            }
            return float.NaN;
        }

        private static void ArrivalTimeTests()
        {
            var car = Typical;
            // Stock acceleration curve: from standstill the first step gains InverseSmoothStep(max, 0, 0) * accel * dt = accel * dt.
            Check(Near(StockCarMotion.InverseSmoothStep(40f, 0f, 0f), 1f, 1e-5f) && Near(StockCarMotion.InverseSmoothStep(40f, 0f, 40f), 0f, 1e-5f), "InverseSmoothStep ends: full accel at 0, none at max speed");
            Check(Near(car.MaxNextSpeed(0f, Dt), 5f * Dt, 1e-5f), "standstill: one step gains accel * dt");
            Check(Near(car.MinNextSpeed(3f, Dt), Math.Max(0f, 3f - 8f * Dt)) && car.MinNextSpeed(1f, Dt) == 0f, "slowest next speed = stock braking, never below 0");
            // Standstill acceleration to a point 10 m ahead, no binding cap.
            float t10 = ArrivalTime.Compute(ref car, 10f, 0f, 30f, 0f, Dt);
            Check(Near(t10, ManualArrival(car, 10f, 0f, 30f), 1e-3f), "standstill: arrival time matches a manual step-through");
            Check(t10 > 1.8f && t10 < 2.3f, $"standstill: 10 m takes about sqrt(2*10/5) = 2 s ({t10:F3} s)");
            // Turn speed cap: a tight turn capped at 4 m/s reaches a 20 m point later than an uncapped one.
            float uncapped = ArrivalTime.Compute(ref car, 20f, 0f, 30f, 0f, Dt);
            float capped = ArrivalTime.Compute(ref car, 20f, 0f, 4f, 0f, Dt);
            Check(capped > uncapped + 1f && Near(capped, ManualArrival(car, 20f, 0f, 4f), 1e-3f), $"turn speed cap binds ({uncapped:F2} s -> {capped:F2} s)");
            Check(capped > 20f / 4f && capped < 20f / 4f + 1f, "capped: about distance / cap plus the acceleration phase");
            // A car already above the turn cap brakes toward it at the stock rate (never slower than MinNextSpeed).
            float fastIn = ArrivalTime.Compute(ref car, 20f, 12f, 4f, 0f, Dt);
            Check(fastIn < capped && fastIn > 20f / 12f, "faster car on the junction lane: braking to the cap at the stock rate, earlier than from standstill");
            // Near vs far point.
            float nearT = ArrivalTime.Compute(ref car, 3f, 0f, 10f, 0f, Dt), farT = ArrivalTime.Compute(ref car, 25f, 0f, 10f, 0f, Dt);
            Check(nearT < farT && nearT < 1.2f && farT > 3f, $"near point earlier than far point ({nearT:F2} s vs {farT:F2} s)");
            // Approach: a fast car before the junction lane must brake down to the drive speed at the lane start.
            float approach = ArrivalTime.Compute(ref car, 30f, 14f, 5f, 15f, Dt);
            float noBrake = 30f / 14f;
            Check(approach > noBrake, $"approach at 14 m/s to a 5 m/s turn 15 m ahead: brakes in time, later than at constant speed ({approach:F2} > {noBrake:F2})");
            Check(ArrivalTime.Compute(ref car, 0f, 5f, 5f, 0f, Dt) == 0f && ArrivalTime.Compute(ref car, -2f, 5f, 5f, 0f, Dt) == 0f, "point reached or behind: 0");
            // Horizon: a point the car never reaches (drive speed 0) gets the horizon, never more.
            float h = ArrivalTime.Compute(ref car, 50f, 0f, 0f, 0f, Dt);
            Check(Near(h, ArrivalTime.Horizon(Dt)) && Near(ArrivalTime.Horizon(Dt), 64f * Dt), "unreachable point: the horizon (17 s, bounded loop)");
            // Monotone in distance (more distance never gives an earlier arrival).
            bool mono = true; float last = 0f;
            for (float d = 0.5f; d < 60f; d += 0.5f) { float x = ArrivalTime.Compute(ref car, d, 2f, 8f, 4f, Dt); if (x < last - 1e-5f) mono = false; last = x; }
            Check(mono, "arrival time never decreases with distance");

            // Microbench (managed, not Burst): per call cost of the projection.
            int n = 200000; float sink = 0f;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; i++) sink += ArrivalTime.Compute(ref car, 4f + (i & 31), (i & 7) * 0.5f, 8f, 2f, Dt);
            sw.Stop();
            double ns = sw.Elapsed.TotalMilliseconds * 1e6 / n;
            Console.WriteLine($"microbench: ArrivalTime.Compute {ns:F0} ns/call (managed, {n} calls, sink {sink:F0})");
            Check(ns < 20000, "microbench sanity (< 20 us per call)");
        }

        private static void ArrivalProjectionTests()
        {
            var car = Typical;
            // Junction-lane car: the look-ahead y + v*2/len becomes y + v*(2+delta)/len.
            Check(ArrivalProjection.ExtendLookAhead(0.2f, 0.4f, 0f) == 0.4f, "delta 0: stock look-ahead exactly");
            Check(ArrivalProjection.ExtendLookAhead(0.2f, 0.2f, 3f) == 0.2f, "no stock look-ahead (stock gave none): nothing added");
            Check(Near(ArrivalProjection.ExtendLookAhead(0.2f, 0.4f, 2f), 0.6f), "delta 2: look-ahead doubles (4 s)");

            // Far carriageway: W at 0.05 on a 40 m left turn across a divided road; far point 0.55 = 20 m from W.
            // A car on the far conflicting lane (30 m long, 10 m/s) sits 25 m before the overlap start (0.2 of its lane, overlap at 0.2 + 25/30).
            float len = 40f, otherLen = 60f, otherY = 0.2f, otherV = 10f, overlapStart = 0.2f + 25f / otherLen;
            float stockY = otherY + otherV * ArrivalProjection.StockLookAhead / otherLen;
            float delta = ArrivalTime.Compute(ref car, 20f, 0f, 8f, 0f, Dt);
            float projY = ArrivalProjection.ExtendLookAhead(otherY, stockY, delta);
            Check(stockY < overlapStart, "far-carriageway car outside the stock 2 s window (stock: go)");
            Check(projY > overlapStart, $"... inside the projected window (2 s + own arrival {delta:F2} s): busy");
            var divided = new[] { C(0.05f, 0.15f), C(0.15f, 0.25f), C(0.45f, 0.55f), C(0.55f, 0.65f) };
            var atW = Car(len, B(13) - M / len, 0f, divided);
            P phase = atW.Classify(true, B(140), out float stop);
            Check(phase == P.Deciding && StretchState.ForPrediction(phase) == A.StopAtSpot && Near(stop, atW.Wait(B(13))), "... so the car waits at W (whole-path decision)");
            // First point at W: the job computes the distance as Wait + the game's stop margin (JRWholeJunction.Delta), so a car
            // standing at W still needs its margin (0.9 m) to reach the point: about 0.6 s from standstill, not 0 (JR-T-002fr).
            float nearDelta = ArrivalTime.Compute(ref car, atW.Wait(B(13)) + M, 0f, 8f, 0f, Dt);
            Check(nearDelta > 0.4f && nearDelta < 0.9f && Near(ArrivalProjection.ExtendLookAhead(otherY, stockY, nearDelta) - stockY, otherV * nearDelta / otherLen, 1e-4f), $"first point at W: only the stop margin to reach ({nearDelta:F2} s), look-ahead 2 s + that");
            Check(ArrivalProjection.ExtendLookAhead(otherY, stockY, 0f) == stockY, "near point at the car: exactly stock");

            // Feeding lane: virtual reservation on the junction lane.
            float bd = car.BrakingDistance(12f, 4f / 15f);
            float off = ArrivalProjection.VirtualOffset(20f, bd, 12f, 1.5f, 30f);
            float expected = (bd - 0.01f + 12f * 1.5f - 20f) / 30f;
            Check(Near(off, expected), "approach-lane car: reservation = braking distance + speed * delta beyond its lane end");
            Check(ArrivalProjection.VirtualOffset(20f, bd, 12f, 0f, 30f) < 0f, "same car with delta 0: stock reach stays on its lane (no reservation, as stock)");
            Check(ArrivalProjection.VirtualOffset(5f, 0f, 0f, 3f, 30f) < 0f, "standing car on the feeding lane: no virtual reservation");
            Check(ArrivalProjection.VirtualOffset(0f, 100f, 20f, 5f, 30f) == 1f, "reach past the junction lane: offset 1");
            // The stock reservation test decides (offset > num10 || priority > num6).
            Check(ArrivalProjection.ReservationBlocks(off, off - 0.01f, 100, 102), "reservation past the overlap start blocks");
            Check(!ArrivalProjection.ReservationBlocks(off, off + 0.01f, 100, 102), "reservation short of the overlap start does not (same priority)");
            Check(ArrivalProjection.ReservationBlocks(0.01f, 0.5f, 100, 99), "STOP arm (num6 99): any reserving car blocks, as stock");

            // JR-T-002g (owner ruling 14 Nov 2026): a feeding-lane car counts ONLY when it would win the stock priority test
            // (num12 - num6 > 0, the S2-10 condition). num6 = deciding car's GetPriority (100) minus the overlap's priority
            // delta / sign override (stock num6 -= num9): main road -> num6 > 100, minor arm / STOP / YIELD -> num6 < 100.
            int mainNum6 = 100 - (-1), minorNum6 = 100 - 1, equalNum6 = 100;
            Check(ArrivalProjection.ReservationBlocks(off, off - 0.01f, 100, mainNum6) && !ArrivalProjection.FeedingCarBlocks(off, off - 0.01f, 100, mainNum6),
                "main-road deciding car (num6 101) is NOT held by a minor-arm feeding car (100), even with its reach past the overlap start");
            Check(!ArrivalProjection.FeedingCarBlocks(1f, 0f, 100, mainNum6), "... not even with its reach across the whole junction lane");
            Check(ArrivalProjection.FeedingCarBlocks(off, off + 0.01f, 100, minorNum6) && ArrivalProjection.FeedingCarBlocks(0.01f, 0.5f, 100, minorNum6),
                "minor-arm deciding car (num6 99) IS held by a main-road feeding car (100), as by a stock reservation of that priority");
            Check(!ArrivalProjection.FeedingCarBlocks(off, off - 0.01f, 100, equalNum6) && !ArrivalProjection.WinsPriority(100, equalNum6),
                "equal priority (num6 100 vs 100): not counted, like S2-10 (stock look-ahead only when num12 - num6 > 0)");
            for (int n6 = 95; n6 <= 112; n6++)
            {
                foreach (int pr in new[] { 100, 108 })
                {
                    if (ArrivalProjection.WinsPriority(pr, n6) != (pr - n6 > 0)) Check(false, $"WinsPriority = stock num12 - num6 > 0 (pr {pr}, num6 {n6})");
                    if (ArrivalProjection.FeedingCarBlocks(0.3f, 0.2f, pr, n6) != (pr - n6 > 0)) Check(false, $"FeedingCarBlocks = priority test then reservation test (pr {pr}, num6 {n6})");
                }
            }
            Check(ArrivalProjection.FeedingCarBlocks(off, off + 0.01f, 108, mainNum6), "emergency feeding car (108) still counts against a main-road car (num6 101)");
            Check(!ArrivalProjection.WinsPriority(ArrivalProjection.MaxCarPriority, 108), "num6 >= 108: no stock car priority wins, feeding scan skipped");

            // The safety re-check uses the same projection: Safety (like Deciding) gives StopAtSpot on a prediction.
            var crossingNear = Car(len, 0.10f, 3f, divided);
            P safety = crossingNear.Classify(true, B(140), out float sStop);
            float remaining = crossingNear.Wait(B(140)) + M;
            float d2 = ArrivalTime.Compute(ref car, remaining, 3f, 8f, 0f, Dt);
            Check(safety == P.Safety && StretchState.ForPrediction(safety) == A.StopAtSpot && Near(sStop, crossingNear.Wait(B(38))), "re-check: projected far car stops the crossing car before the first uncrossed point");
            Check(d2 > 0f && ArrivalProjection.ExtendLookAhead(otherY, stockY, d2) > stockY, "re-check: projection from the current state to the remaining point");
            // Can stop vs cannot stop.
            var fast = Car(len, B(115) - 2f / len, 10f, divided);
            Check(fast.Classify(true, B(140), out _) == P.Crossing && StretchState.ForPrediction(P.Crossing) == A.Suppress && StretchState.ForVehicle(P.Crossing, false, false) == A.Stock,
                "cannot stop: no projected wait, stock collision braking for a physical car");
            // Rule off: Stock, no projection.
            Check(atW.Classify(false, B(140), out _) == P.Stock && StretchState.ForPrediction(P.Stock) == A.Stock, "rule off: stock give-way, no projection");
        }

        private static void SafetyEventTests()
        {
            var slots = new ArraySlots { Values = new long[64] };
            int events = 0;
            for (uint f = 1000; f < 1000 + 16 * 10; f += 16) if (SafetyStopEvents.Record(ref slots, 7, f)) events++;
            Check(events == 1, "ten consecutive safety-stop car-steps (every 16 frames) = one event");
            Check(SafetyStopEvents.Record(ref slots, 7, 1000 + 16 * 9 + 48) == false, "one missed update (48 frames) still continues");
            Check(SafetyStopEvents.Record(ref slots, 7, 1000 + 16 * 9 + 48 + 49), "a gap over 48 frames starts a new event");
            Check(SafetyStopEvents.Record(ref slots, 8, 1300) && !SafetyStopEvents.Record(ref slots, 8, 1316), "another car: its own event");
            // Entity index 0 is a valid key (key = index + 1).
            Check(SafetyStopEvents.Record(ref slots, 0, 5000) && !SafetyStopEvents.Record(ref slots, 0, 5016), "entity index 0 counted");
            // Expired slots are reused; a full table over-counts (never hides an event).
            var tiny = new ArraySlots { Values = new long[2] };
            SafetyStopEvents.Record(ref tiny, 1, 100); SafetyStopEvents.Record(ref tiny, 2, 100);
            Check(SafetyStopEvents.Record(ref tiny, 3, 110) && SafetyStopEvents.Record(ref tiny, 3, 126), "full table: counts as new (over-count)");
            Check(SafetyStopEvents.Record(ref tiny, 3, 400) && !SafetyStopEvents.Record(ref tiny, 3, 416), "expired slots reused");
            var empty = new ArraySlots { Values = Array.Empty<long>() };
            Check(SafetyStopEvents.Record(ref empty, 1, 1), "no table: every step counts");

            // Histogram buckets and median.
            Check(PointBuckets.Distance(0f) == 0 && PointBuckets.Distance(2f) == 1 && PointBuckets.Distance(31.9f) == 6 && PointBuckets.Distance(500f) == 7, "distance buckets");
            Check(PointBuckets.Arrival(0.49f) == 0 && PointBuckets.Arrival(2f) == 4 && PointBuckets.Arrival(9f) == 7, "arrival buckets");
            var h = new long[3 + PointBuckets.Count];
            Check(Stage2Counters.MedianBucket(h, 3, PointBuckets.Count) == -1, "empty histogram: no median");
            h[3 + 1] = 2; h[3 + 4] = 3;
            Check(Stage2Counters.MedianBucket(h, 3, PointBuckets.Count) == 4, "median bucket");
        }

        private static void ArrivalRepoTests()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string vu = Path.Combine(home, "Code", "decomp", "yb-2026-11-13", "full", "Game.Vehicles", "VehicleUtils.cs");
            string it = Path.Combine(home, "Code", "decomp", "yb-2026-11-13", "CarLaneSpeedIterator.cs");
            string nav = Path.Combine(home, "Code", "decomp", "yb-2026-11-13", "CarNavigationSystem.cs");
            if (File.Exists(vu) && File.Exists(it) && File.Exists(nav))
            {
                string t = File.ReadAllText(vu);
                Check(t.Contains("float y = MathUtils.InverseSmoothStep(prefabCarData.m_MaxSpeed, 0f, currentSpeed) * prefabCarData.m_Acceleration;")
                    && t.Contains("@float.y = math.min(@float.y, math.max(@float.x, prefabCarData.m_MaxSpeed));"), "CalculateSpeedRange as mirrored by StockCarMotion");
                Check(t.Contains("num * num + 2f * prefabCarData.m_Braking * distance + maxResultSpeed * maxResultSpeed"), "GetMaxBrakingSpeed(maxResultSpeed) as mirrored");
                string i = File.ReadAllText(it);
                Check(i.Contains("curvePosition.y += objectSpeed * 2f / math.max(1f, m_Curve.m_Length);"), "stock 2 s look-ahead (ExtendLookAhead base)");
                Check(i.Contains("if (offset > num10 || priority2 > num6)"), "stock reservation test (ReservationBlocks)");
                Check(i.Contains("if (num12 - num6 > 0)"), "stock look-ahead priority test (WinsPriority)");
                Check(t.Contains("return math.select(100, 108, (carData.m_Flags & CarFlags.Emergency) != 0);"), "stock car priorities 100 / 108 (MaxCarPriority)");
                string n = File.ReadAllText(nav);
                Check(n.Contains("float num = math.max(0f, VehicleUtils.GetBrakingDistance(prefabCarData, math.abs(navigationData.m_MaxSpeed), timeStep) - 0.01f);"), "stock reservation reach (VirtualOffset)");
                Check(n.Contains("int priority = VehicleUtils.GetPriority(car);"), "stock reservation priority = VehicleUtils.GetPriority(car) (feeding car priority source)");
            }
            else
            {
                Console.WriteLine("note: decompile not found, arrival formulas not cross-checked");
            }
            string root = RepoRoot();
            if (root == null) { Check(false, "repo root found"); return; }
            string iter = File.ReadAllText(Path.Combine(root, "JunctionRules", "Vanilla", "JRCarLaneSpeedIterator.cs"));
            Check(iter.Contains("float jrFeedSpeed = m_JR.FeedingLaneYield(m_Lane, m_Curve.m_Length, math.select(math.max(num3 + @float.z, m_CurveOffset.x), 0f, inverse), num6,"), "S2-12 feeds the stock num10/num6 into the feeding-lane test");
            Check(iter.Contains("curvePosition.y = m_JR.AfterProjection(jrInScope, jrY, curvePosition.y,"), "S2-10 extends or undoes the look-ahead");
            string job = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            Check(Regex.IsMatch(job, @"Projecting\s*\{[^}]*get\s*=>\s*RuleOn\s*&&\s*\(m_OvPhase == PointPhase\.Deciding \|\| m_OvPhase == PointPhase\.Safety\)"), "projection runs for the decision AND the safety re-check (same projection)");
            Check(job.Contains("int otherPriority = VehicleUtils.GetPriority(otherCar);") && job.Contains("!ArrivalProjection.WinsPriority(otherPriority, num6)) continue;")
                && job.Contains("!ArrivalProjection.FeedingCarBlocks(offset, num10, otherPriority, num6)) continue;"), "S2-12 feeding cars count only when they win the stock priority test (JR-T-002g)");
            Check(job.Contains("VehicleUtils.CalculateSpeedRange(") && job.Contains("VehicleUtils.GetMaxBrakingSpeed(") && job.Contains("VehicleUtils.GetMaxDriveSpeed("), "job side uses the game's own motion functions");
            string core = File.ReadAllText(Path.Combine(root, "JunctionRules", "Core", "ArrivalTime.cs"));
            string code = Regex.Replace(core, @"//.*", "");
            Check(!Regex.IsMatch(code, @"\bnew\s+\w"), "arrival code has no 'new' (no managed allocation)");
        }
    }
}
