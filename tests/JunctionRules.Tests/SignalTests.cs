using System;
using System.IO;
using System.Text.RegularExpressions;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.2.4 (JR-T-002h, owner rulings 14 Nov 2026): approach (feeding) lane cars whose own junction lane faces red are
    /// skipped; amber skips those the stock amber test lets stop and counts those that cannot stop; everything else as
    /// 0.2.3 / JR-T-002g. The decision order mirrors Stage2/JRWholeJunction.FeedingLaneYield: priority gate, signal,
    /// virtual reservation, stock reservation test (checked against the job text in SignalRepoTests).
    /// </summary>
    internal static partial class Program
    {
        /// <summary>One feeding car as FeedingLaneYield decides it (same order, same Core calls).</summary>
        private static bool FeedCounted(int signal, int signalFlags, bool applies, int priority, int num6,
            float speed, float toStop, float remainingOnFeed, float delta, float junctionLength, float num10)
        {
            var car = Typical;
            if (!ArrivalProjection.WinsPriority(ArrivalProjection.MaxCarPriority, num6)) return false;
            if (!ArrivalProjection.WinsPriority(priority, num6)) return false;
            if (applies && FeedingSignal.Obeys(priority, signalFlags)
                && FeedingSignal.Skips(true, signal, signalFlags, priority, car.BrakingDistance(speed, 0f), toStop)) return false;
            float offset = ArrivalProjection.VirtualOffset(remainingOnFeed, car.BrakingDistance(speed, 4f / 15f), speed, delta, junctionLength);
            return offset >= 0f && ArrivalProjection.FeedingCarBlocks(offset, num10, priority, num6);
        }

        private static void FeedingSignalTests()
        {
            var car = Typical;   // braking 8 m/s^2
            const int minorNum6 = 99, mainNum6 = 101;
            // A main-road car 12 m/s, 20 m before the junction lane; deciding car on a minor arm, 1.5 s from the point.
            float speed = 12f, rest = 20f, delta = 1.5f, len = 30f, num10 = 0.05f;
            float stopDist = car.BrakingDistance(speed, 0f);   // 9 m: can stop within 20 m
            Check(Near(stopDist, 0.5f * 144f / 8f), "stock amber braking distance: GetBrakingDistance(car, v, 0f) = v^2 / 2b");

            bool green = FeedCounted(FeedingSignal.Go, 0, true, 100, minorNum6, speed, rest, rest, delta, len, num10);
            bool none = FeedCounted(FeedingSignal.None, 0, false, 100, minorNum6, speed, rest, rest, delta, len, num10);
            bool yield = FeedCounted(FeedingSignal.Yield, 0, true, 100, minorNum6, speed, rest, rest, delta, len, num10);
            Check(green && none && yield, "green / no signal / signal Yield: counted as 0.2.3 (JR-T-002g gate passed)");

            // Owner ruling 1: red -> skipped.
            Check(!FeedCounted(FeedingSignal.Stop, 0, true, 100, minorNum6, speed, rest, rest, delta, len, num10), "red: approach-lane car skipped");
            Check(!FeedCounted(FeedingSignal.Stop, 0, true, 100, minorNum6, 20f, 1f, 1f, delta, len, num10), "red: skipped even when it could not stop (owner ruling 1, literal)");
            Check(FeedingSignal.Skips(true, FeedingSignal.Stop, 0, 100, 1000f, 0f), "Skips(red) independent of braking");

            // Owner ruling 2: amber.
            Check(!FeedCounted(FeedingSignal.SafeStop, 0, true, 100, minorNum6, speed, rest, rest, delta, len, num10), "amber + can stop (9 m <= 20 m): skipped");
            Check(!FeedCounted(FeedingSignal.SafeStop, 0, true, 100, minorNum6, speed, stopDist, stopDist, delta, len, num10), "amber, braking distance == distance: skipped (stock <=)");
            float shortRest = 5f;   // 9 m needed, 5 m left: goes through
            Check(FeedCounted(FeedingSignal.SafeStop, 0, true, 100, minorNum6, speed, shortRest, shortRest, delta, len, num10), "amber + cannot stop: counted (wins priority)");
            Check(!FeedCounted(FeedingSignal.SafeStop, 0, true, 100, mainNum6, speed, shortRest, shortRest, delta, len, num10), "amber + cannot stop but loses priority (main-road deciding car): not counted (JR-T-002g gate stays)");
            Check(!FeedCounted(FeedingSignal.Go, 0, true, 100, mainNum6, speed, rest, rest, delta, len, num10) && !FeedCounted(FeedingSignal.Go, 0, true, 100, 100, speed, rest, rest, delta, len, num10),
                "green: priority gate unchanged (main road / equal priority not counted)");
            // Front offset: distance to the stop = rest of lane minus the car's front (ObjectGeometryData bounds max z).
            float front = 2.3f;
            Check(FeedingSignal.Skips(true, FeedingSignal.SafeStop, 0, 100, stopDist, stopDist + front - front) && !FeedingSignal.Skips(true, FeedingSignal.SafeStop, 0, 100, stopDist, stopDist + front - front - 0.01f),
                "amber boundary at front-to-lane-start distance");

            // Emergency vehicles: stock ignores a non-physical red / amber (m_Priority < 108 || Physical).
            Check(FeedCounted(FeedingSignal.Stop, 0, true, 108, mainNum6, speed, rest, rest, delta, len, num10), "emergency on red (non-physical signal): counted, as stock lets it through");
            Check(FeedCounted(FeedingSignal.SafeStop, 0, true, 108, mainNum6, speed, rest, rest, delta, len, num10), "emergency on amber that could stop: counted (ignores the signal)");
            Check(!FeedCounted(FeedingSignal.Stop, FeedingSignal.PhysicalFlag, true, 108, mainNum6, speed, rest, rest, delta, len, num10), "emergency on a PHYSICAL red: skipped (stock stops it)");
            Check(FeedingSignal.Obeys(100, 0) && FeedingSignal.Obeys(107, 0) && !FeedingSignal.Obeys(108, 0) && FeedingSignal.Obeys(108, FeedingSignal.PhysicalFlag) && !FeedingSignal.Obeys(108, 1),
                "Obeys = priority < 108 || Physical (CanExtend alone does not)");

            // Signal applies only where stock keeps the junction lane's signal flags for this car.
            Check(FeedingSignal.Applies(true, false, true) && FeedingSignal.Applies(true, true, false) && !FeedingSignal.Applies(true, false, false) && !FeedingSignal.Applies(false, true, true),
                "applies: TrafficLights/LevelCrossing lane and (junction or feeding lane Approach)");
            Check(FeedCounted(FeedingSignal.Stop, 0, false, 100, minorNum6, speed, rest, rest, delta, len, num10), "red that does not apply to the car (no Approach / unsignalled lane): counted as 0.2.3");
            Check(!FeedingSignal.Skips(false, FeedingSignal.Stop, 0, 100, 0f, 100f), "Skips false when the signal does not apply");

            // Remaining lane as stock measures it.
            Check(Near(FeedingSignal.RestOfLane(30f, 30f, 0f), 30f) && Near(FeedingSignal.RestOfLane(9.5f, 10f, 0.5f), 9.5f + (5f - 9.5f) * 0.5f)
                && Near(FeedingSignal.RestOfLane(0f, 30f, 1f), 0.001f), "RestOfLane = max(0.001, lerp(chord, len * (1 - x), 1 - x))");
            Check(FeedingSignal.CanSkip(FeedingSignal.Stop) && FeedingSignal.CanSkip(FeedingSignal.SafeStop) && !FeedingSignal.CanSkip(FeedingSignal.Go)
                && !FeedingSignal.CanSkip(FeedingSignal.Yield) && !FeedingSignal.CanSkip(FeedingSignal.None), "only red and amber can skip");
        }

        private static void SignalRepoTests()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string dec = Path.Combine(home, "Code", "decomp", "yb-2026-11-13");
            string enumFile = Path.Combine(dec, "full", "Game.Net", "LaneSignalType.cs");
            string flagsFile = Path.Combine(dec, "full", "Game.Net", "LaneSignalFlags.cs");
            string it = Path.Combine(dec, "CarLaneSpeedIterator.cs");
            string nav = Path.Combine(dec, "CarNavigationSystem.cs");
            string vu = Path.Combine(dec, "full", "Game.Vehicles", "VehicleUtils.cs");
            if (File.Exists(enumFile) && File.Exists(flagsFile) && File.Exists(it) && File.Exists(nav) && File.Exists(vu))
            {
                string e = Regex.Replace(File.ReadAllText(enumFile), @"\s+", " ");
                Check(e.Contains("public enum LaneSignalType : byte { None, Stop, SafeStop, Yield, Go }")
                    && FeedingSignal.None == 0 && FeedingSignal.Stop == 1 && FeedingSignal.SafeStop == 2 && FeedingSignal.Yield == 3 && FeedingSignal.Go == 4,
                    "LaneSignalType {None, Stop, SafeStop, Yield, Go} = FeedingSignal 0..4");
                string f = Regex.Replace(File.ReadAllText(flagsFile), @"\s+", " ");
                Check(f.Contains("public enum LaneSignalFlags : byte { CanExtend = 1, Physical }") && FeedingSignal.PhysicalFlag == 2, "LaneSignalFlags.Physical = 2");
                string i = File.ReadAllText(it);
                Check(i.Contains("float brakingDistance = VehicleUtils.GetBrakingDistance(m_PrefabCar, m_CurrentSpeed, 0f);"), "stock signal braking distance: current speed, time step 0");
                Check(Regex.IsMatch(i, @"case LaneSignalType\.SafeStop:\s*if \(\(m_Priority < 108 \|\| \(componentData3\.m_Flags & LaneSignalFlags\.Physical\) != 0\) && brakingDistance <= num2\)\s*\{\s*driveSpeed = 0f;"),
                    "stock amber decision: (m_Priority < 108 || Physical) && brakingDistance <= num2 -> stop (FeedingSignal.Skips)");
                Check(Regex.IsMatch(i, @"case LaneSignalType\.Stop:\s*if \(\(m_Priority < 108 \|\| \(componentData3\.m_Flags & LaneSignalFlags\.Physical\) != 0\) && brakingDistance <= num2 \+ 1f\)"),
                    "stock red decision: emergency ignores a non-physical red (FeedingSignal.Obeys)");
                Check(i.Contains("float num = 0f - m_PrefabObjectGeometry.m_Bounds.max.z;") && i.Contains("float num2 = m_Distance + num;"), "stock stop distance = distance to lane start minus front (bounds max z)");
                Check(i.Contains("if ((componentData2.m_Flags & (Game.Net.CarLaneFlags.LevelCrossing | Game.Net.CarLaneFlags.TrafficLights)) != 0 && m_LaneSignalData.TryGetComponent(lane, out var componentData3))")
                    && i.Contains("if ((carLaneFlags & Game.Net.CarLaneFlags.Approach) == 0)") && i.Contains("componentData2.m_Flags &= ~(Game.Net.CarLaneFlags.LevelCrossing | Game.Net.CarLaneFlags.Stop | Game.Net.CarLaneFlags.TrafficLights);"),
                    "stock: signal read only on a TrafficLights / LevelCrossing lane, flags cleared without Approach (FeedingSignal.Applies)");
                Check(i.Contains("float num8 = math.max(0.001f, math.lerp(math.distance(@float, float3), curve.m_Length * num7, num7));"), "stock rest of the current lane (FeedingSignal.RestOfLane)");
                Check(Regex.IsMatch(i, @"m_Lane = laneOverlap\.m_Other;[\s\S]{0,4000}?if \(m_LaneSignalData\.TryGetComponent\(m_Lane, out var componentData2\)\)\s*\{\s*switch \(componentData2\.m_Signal\)\s*\{\s*case LaneSignalType\.Stop:\s*num8\+\+;"),
                    "CheckOverlappingLanes reads the CONFLICTING lane's signal into componentData2 (S2-12 reuses it)");
                string n = File.ReadAllText(nav);
                Check(n.Contains("float num2 = math.length(moving.m_Velocity);") && n.Contains("num2 = math.min(num2, prefabCarData.m_MaxSpeed);") && n.Contains("carLaneSpeedIterator.m_CurrentSpeed = num2;"),
                    "stock current speed = min(|velocity|, CarData max speed)");
                Check(n.Contains("carLaneSpeedIterator.m_Priority = priority;"), "iterator m_Priority = VehicleUtils.GetPriority(car)");
                string v = File.ReadAllText(vu);
                Check(v.Contains("return 0.5f * speed * speed / prefabCarData.m_Braking + speed * timeStep;"), "GetBrakingDistance(CarData) as mirrored by StockCarMotion.BrakingDistance");
            }
            else
            {
                Console.WriteLine("note: decompile not found, signal pins not checked");
            }
            string root = RepoRoot();
            if (root == null) { Check(false, "repo root found"); return; }
            string iter = File.ReadAllText(Path.Combine(root, "JunctionRules", "Vanilla", "JRCarLaneSpeedIterator.cs"));
            Check(iter.Contains("num6, componentData2, m_Entity, ref m_LaneObjectData,") && iter.Contains("ref m_CarLaneData, ref m_PrefabObjectGeometryData, ref m_LaneReservationData, m_PrefabCar, m_SafeTimeStep"),
                "S2-12 passes the stock-read conflicting-lane signal and the read-only CarLane / geometry lookups");
            string job = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            int gate = job.IndexOf("!ArrivalProjection.WinsPriority(otherPriority, num6)) continue;", StringComparison.Ordinal);
            int sig = job.IndexOf("FeedingSignal.Skips(true, signal, signalFlags, otherPriority, VehicleUtils.GetBrakingDistance(otherData, stockSpeed, 0f), toStop)) continue;", StringComparison.Ordinal);
            int res = job.IndexOf("!ArrivalProjection.FeedingCarBlocks(offset, num10, otherPriority, num6)) continue;", StringComparison.Ordinal);
            Check(gate > 0 && sig > gate && res > sig, "job order: priority gate, signal skip, reservation test (as FeedCounted)");
            Check(job.Contains("float stockSpeed = math.min(math.length(m.m_Velocity), otherData.m_MaxSpeed);") && job.Contains("geometry.m_Bounds.max.z"), "job amber test uses stock speed and front offset");
        }

        /// <summary>
        /// 0.2.4 scan cost: who scans (Core FeedingScan.ShouldScan), the per-car-update memo and the cap, in a queue
        /// scenario: 10 cars queued on a minor arm (standing, 7 m apart, the first at W), each car-update checking 3
        /// overlaps whose conflicting lanes share one feeding source (through + turn lanes of the same main-road edge).
        /// </summary>
        private static void FeedingScanTests()
        {
            var car = Typical;
            int scans = 0, followerSkips = 0, memoSkips = 0;
            const int queue = 10, overlaps = 3, minorNum6 = 99;
            for (int k = 0; k < queue; k++)
            {
                float toW = 0.05f + 7f * k;                      // stop wait (m_OvStopWait): first car standing at its stop point
                float v = 0f;
                float currentMax = car.MaxNextSpeed(v, Dt);      // speed range max
                if (k > 0) currentMax = Math.Min(currentMax, car.BrakingSpeed(1.5f, 0f, Dt));   // following limit (car ahead 1.5 m)
                float hold = car.BrakingSpeed(toW, 0f, Dt);
                bool ahead = k > 0;
                var memo = new FeedScanMemo<int>();
                for (int ov = 0; ov < overlaps; ov++)
                {
                    if (!FeedingScan.ShouldScan(PointPhase.Deciding, ahead, hold, currentMax, false)) { if (ahead) followerSkips++; continue; }
                    if (memo.TryGet(42, out int mp, out float ma, out float mv) && FeedingScan.NothingCanCount(mp, ma, mv, minorNum6, 1.2f)) { memoSkips++; continue; }
                    scans++;
                    // the scan found only standing / far cars: no car can reach its lane end (reach - remaining < 0)
                    memo.Put(42, 100, -15f, 8f);
                }
            }
            Console.WriteLine($"queue scenario: {queue} cars x {overlaps} overlaps -> {scans} feeding scans, follower skips {followerSkips}, memo skips {memoSkips}");
            Check(scans == 1, "10 queued cars: only the car at W scans, once per source per car-update");
            Check(followerSkips == (queue - 1) * overlaps && memoSkips == overlaps - 1, "followers never scan; repeat overlaps of the same source use the memo");

            // Who scans.
            Check(!FeedingScan.ShouldScan(PointPhase.Deciding, false, 5f, 5f, false), "already held as hard: no scan");
            Check(!FeedingScan.ShouldScan(PointPhase.Deciding, false, 1f, 5f, true), "conflicting lane's own reservation already holds it: no scan");
            Check(FeedingScan.ShouldScan(PointPhase.Safety, true, 1f, 5f, false), "safety re-check (crossing car itself) scans; the follower test is for deciding only");
            Check(!FeedingScan.ShouldScan(PointPhase.Crossing, false, 1f, 5f, false) && !FeedingScan.ShouldScan(PointPhase.Stock, false, 1f, 5f, false), "crossing / stock phases never scan");
            // Far from W: hold >= speed range max exactly when the distance is at least the braking distance of that speed.
            float vmax = 12f, bdist = car.BrakingDistance(vmax, Dt);
            Check(car.BrakingSpeed(bdist + 0.01f, 0f, Dt) >= vmax && car.BrakingSpeed(bdist - 0.01f, 0f, Dt) < vmax, "hold < currentMax <=> within the braking distance of W (far cars skip by the hold test)");

            // Memo bound.
            Check(FeedingScan.NothingCanCount(100, 5f, 10f, 101, 2f), "memo: nobody wins priority (main-road deciding car): skip");
            Check(FeedingScan.NothingCanCount(100, -30f, 10f, 99, 2f) && !FeedingScan.NothingCanCount(100, -30f, 10f, 99, 3.1f), "memo: reach bound vs own arrival time");
            Check(FeedingScan.NothingCanCount(-1, float.NegativeInfinity, 0f, 0, 5f), "memo: empty source: skip");
            var m4 = new FeedScanMemo<int>();
            for (int i = 0; i < 6; i++) m4.Put(i, i, i, i);
            Check(m4.Count == 4 && !m4.TryGet(0, out _, out _, out _) && !m4.TryGet(1, out _, out _, out _) && m4.TryGet(5, out int p5, out _, out _) && p5 == 5, "memo: 4 slots, round robin");
            Check(FeedingScan.MaxCarsPerUpdate >= 64 && FeedingScan.MaxLanesPerScan >= 8, "cap high enough not to bind in normal traffic");

            // Microbench (managed): the per-overlap gate + memo lookup that replaces a repeat scan.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int n = 2_000_000, sink = 0;
            var mb = new FeedScanMemo<int>();
            mb.Put(7, 100, -10f, 9f);
            for (int i = 0; i < n; i++)
            {
                if (FeedingScan.ShouldScan(PointPhase.Deciding, (i & 7) != 0, 0.1f, 1.3f, false)
                    && mb.TryGet(7, out int a1, out float a2, out float a3) && FeedingScan.NothingCanCount(a1, a2, a3, 99, (i & 3) * 0.5f)) sink++;
            }
            double ns = sw.Elapsed.TotalMilliseconds * 1e6 / n;
            Console.WriteLine($"microbench: feeding gate + memo {ns:F1} ns/call (managed, {n} calls, sink {sink})");
            Check(ns < 2000, "microbench sanity (< 2 us per gate)");
        }
    }
}
