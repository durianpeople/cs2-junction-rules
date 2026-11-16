using System.Runtime.CompilerServices;

namespace JunctionRules.Core
{
    /// <summary>
    /// The game's own car motion formulas, as the arrival-time projection steps them. The job side wraps the real game
    /// functions (Stage2/JRWholeJunction.cs: VehicleUtils.CalculateSpeedRange / GetMaxBrakingSpeed on the car's CarData),
    /// the tests use <see cref="StockCarMotion"/>, a line-by-line mirror of the decompile. Burst: used only as a generic
    /// struct constraint, never boxed.
    /// </summary>
    public interface ICarMotion
    {
        /// <summary>VehicleUtils.CalculateSpeedRange(car, speed, timeStep).max: the fastest the car can be one step later.</summary>
        float MaxNextSpeed(float speed, float timeStep);

        /// <summary>VehicleUtils.CalculateSpeedRange(car, speed, timeStep).min: the slowest (stock braking rate, never harder).</summary>
        float MinNextSpeed(float speed, float timeStep);

        /// <summary>VehicleUtils.GetMaxBrakingSpeed(car, distance, maxResultSpeed, timeStep): the fastest speed from which the car still gets down to maxResultSpeed within distance.</summary>
        float BrakingSpeed(float distance, float maxResultSpeed, float timeStep);
    }

    /// <summary>
    /// Mirror of the decompiled CarData formulas (Game.Vehicles.VehicleUtils, Game.dll 1.6.4f1; Colossal.Mathematics
    /// MathUtils.InverseSmoothStep). Used by the tests and the managed microbench; the mod itself calls the game's own
    /// functions. A repo test checks the decompile still has these formulas.
    /// </summary>
    public struct StockCarMotion : ICarMotion
    {
        public float MaxSpeed;
        public float Acceleration;
        public float Braking;

        public static StockCarMotion Of(float maxSpeed, float acceleration, float braking)
        {
            StockCarMotion m = default;
            m.MaxSpeed = maxSpeed;
            m.Acceleration = acceleration;
            m.Braking = braking;
            return m;
        }

        /// <summary>MathUtils.InverseSmoothStep(a, b, x) = 0.5 - sin(asin(1 - 2 * saturate((x - a) / (b - a))) / 3).</summary>
        public static float InverseSmoothStep(float a, float b, float x)
        {
            float t = (x - a) / (b - a);
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return 0.5f - (float)System.Math.Sin(System.Math.Asin(1f - 2f * t) * (1.0 / 3.0));
        }

        public float MaxNextSpeed(float speed, float timeStep)
        {
            float y = InverseSmoothStep(MaxSpeed, 0f, speed) * Acceleration;
            float lo = MinNextSpeed(speed, timeStep);
            float hi = speed + y * timeStep;
            float cap = lo > MaxSpeed ? lo : MaxSpeed;
            return hi < cap ? hi : cap;
        }

        public float MinNextSpeed(float speed, float timeStep)
        {
            float lo = speed - Braking * timeStep;
            return lo > 0f ? lo : 0f;
        }

        public float BrakingSpeed(float distance, float maxResultSpeed, float timeStep)
        {
            float num = timeStep * Braking;
            float r = num * num + 2f * Braking * distance + maxResultSpeed * maxResultSpeed;
            float v = (float)System.Math.Sqrt(r > 0f ? r : 0f) - num;
            return v > 0f ? v : 0f;
        }

        /// <summary>VehicleUtils.GetBrakingDistance(car, speed, timeStep).</summary>
        public float BrakingDistance(float speed, float timeStep) => 0.5f * speed * speed / Braking + speed * timeStep;
    }

    /// <summary>
    /// 0.2.3 (JR-T-002f, owner ruling 14 Nov 2026, fix (a)): the car's OWN arrival time at a conflict point, from its
    /// actual state, with the game's own motion: per step of the navigation time step the speed may rise to
    /// CalculateSpeedRange(...).max (the stock acceleration curve), never above the junction lane's drive speed
    /// (VehicleUtils.GetMaxDriveSpeed: speed limit and the speed-in-turns cap) and, before the junction lane, never above
    /// what still lets it brake down to that drive speed at the lane start (GetMaxBrakingSpeed, the stock braking); it
    /// never slows faster than the stock braking (CalculateSpeedRange(...).min). Distance per step is trapezoidal. No
    /// extra time, no margin: the result is the time the front needs to reach the point when the car goes now.
    /// Pure arithmetic, bounded loop, Burst-compatible (generic struct constraint, no allocation).
    /// </summary>
    public static class ArrivalTime
    {
        /// <summary>Loop bound. With the navigation step (4/15 s) this is a 17 s horizon; a point further out in time gets the horizon (more projection = stricter, never bolder).</summary>
        public const int MaxSteps = 64;

        /// <summary>
        /// Seconds until the car's front reaches a point <paramref name="distance"/> metres ahead (0 if it is there or
        /// past it). speed = current speed; driveSpeed = the junction lane's stock drive speed, binding from
        /// <paramref name="driveFrom"/> metres ahead (0 when the car is on that lane); before that point the car keeps
        /// at most max(speed, driveSpeed) and brakes in time for driveSpeed. timeStep = the navigation step.
        /// </summary>
        public static float Compute<T>(ref T car, float distance, float speed, float driveSpeed, float driveFrom, float timeStep)
            where T : struct, ICarMotion
        {
            if (!(distance > 0f)) return 0f;
            float approachCap = speed > driveSpeed ? speed : driveSpeed;
            float s = 0f, t = 0f, v = speed < 0f ? 0f : speed;
            for (int i = 0; i < MaxSteps; i++)
            {
                float limit;
                if (s >= driveFrom)
                {
                    limit = driveSpeed;
                }
                else
                {
                    float brake = car.BrakingSpeed(driveFrom - s, driveSpeed, timeStep);
                    limit = approachCap < brake ? approachCap : brake;
                }
                float next = car.MaxNextSpeed(v, timeStep);
                if (next > limit) next = limit;
                float floor = car.MinNextSpeed(v, timeStep);
                if (next < floor) next = floor;
                float ds = 0.5f * (v + next) * timeStep;
                if (s + ds >= distance)
                {
                    return t + timeStep * ((distance - s) / ds);
                }
                s += ds;
                t += timeStep;
                v = next;
            }
            return t;
        }

        /// <summary>The horizon <see cref="Compute"/> never exceeds.</summary>
        public static float Horizon(float timeStep) => MaxSteps * timeStep;
    }

    /// <summary>
    /// 0.2.3 projection of the OTHER cars to the deciding car's arrival time (owner ruling 14 Nov 2026), both at constant
    /// speed as the stock 2 s look-ahead assumes.
    /// </summary>
    public static class ArrivalProjection
    {
        /// <summary>The stock look-ahead time for a higher-priority car on a conflicting lane (CarLaneSpeedIterator.CheckOverlappingLanes: objectSpeed * 2f).</summary>
        public const float StockLookAhead = 2f;

        /// <summary>
        /// A car on a conflicting junction lane: the stock look-ahead y + v * 2 / len becomes y + v * (2 + delta) / len,
        /// applied only where stock applied its own (stockY != originalY). delta = 0 returns stock exactly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ExtendLookAhead(float originalY, float stockY, float delta)
        {
            if (stockY == originalY || !(delta > 0f)) return stockY;
            return originalY + (stockY - originalY) * ((StockLookAhead + delta) / StockLookAhead);
        }

        /// <summary>
        /// A car on a lane feeding the conflicting junction lane, as a virtual reservation on that junction lane: its
        /// stock reservation reach (braking distance, CarNavigationSystem.ReserveNavigationLanes:
        /// max(0, GetBrakingDistance(car, speed, 4/15) - 0.01)) plus speed * delta, beyond the metres it still has on its
        /// lane. Returns the reserved offset on the junction lane (0..1, as LaneReservation.GetOffset), or -1 when the
        /// projected reach stays on the feeding lane (no reservation, as stock).
        /// </summary>
        public static float VirtualOffset(float remainingOnFeed, float brakingDistance, float speed, float delta, float junctionLength)
        {
            if (!(speed > 0f)) return -1f;
            float reach = (brakingDistance - 0.01f > 0f ? brakingDistance - 0.01f : 0f) + speed * (delta > 0f ? delta : 0f);
            float r = reach - remainingOnFeed;
            if (!(r > 0f)) return -1f;
            float o = r / (junctionLength > 1e-6f ? junctionLength : 1e-6f);
            return o < 1f ? o : 1f;
        }

        /// <summary>The stock reservation test (CheckOverlappingLanes: offset &gt; num10 || priority2 &gt; num6), unchanged.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ReservationBlocks(float offset, float num10, int priority, int num6) => offset > num10 || priority > num6;

        /// <summary>
        /// The stock priority test for a car on a conflicting junction lane (CheckOverlappingLanes: num12 - num6 &gt; 0,
        /// where num12 = VehicleUtils.GetPriority(car) and num6 = the deciding car's priority minus the overlap's
        /// priority delta / sign override; stock applies its look-ahead only then, S2-10 extends only then).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool WinsPriority(int otherPriority, int num6) => otherPriority - num6 > 0;

        /// <summary>The highest stock car priority (VehicleUtils.GetPriority(Car): 100, emergency 108).</summary>
        public const int MaxCarPriority = 108;

        /// <summary>
        /// JR-T-002g (owner ruling 14 Nov 2026, binding): a car on a lane feeding the conflicting junction lane counts
        /// ONLY when it would win the stock priority test against the deciding car, the same condition S2-10 uses for a
        /// car on the conflicting junction lane (<see cref="WinsPriority"/>); then its virtual reservation goes through
        /// the unchanged stock reservation test. A main-road car never brakes for a minor-arm car heading to its
        /// STOP / YIELD line; equal priority is not counted (as S2-10: no look-ahead at equal priority).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool FeedingCarBlocks(float offset, float num10, int priority, int num6) =>
            WinsPriority(priority, num6) && ReservationBlocks(offset, num10, priority, num6);
    }

    /// <summary>
    /// 0.2.4 (JR-T-002h, owner rulings 14 Nov 2026, binding): a car on a lane feeding the conflicting junction lane is
    /// skipped when that junction lane's own signal stops it, decided as the car itself decides it in stock
    /// (CarLaneSpeedIterator.IterateNextLane, the car lane branch for a lane with TrafficLights / LevelCrossing):
    /// <list type="bullet">
    /// <item>the signal applies only when stock keeps the junction lane's TrafficLights / LevelCrossing flags for that
    /// car (the junction lane or the car's own lane has the Approach flag, else stock clears them);</item>
    /// <item>a car obeys red / amber only when <c>m_Priority &lt; 108 || (signal.m_Flags &amp; LaneSignalFlags.Physical) != 0</c>
    /// (stock: emergency vehicles ignore a non-physical signal, so they are NOT skipped);</item>
    /// <item>RED (LaneSignalType.Stop): skipped (owner ruling 1, regardless of whether it could stop);</item>
    /// <item>AMBER (LaneSignalType.SafeStop): skipped when the stock amber test says it stops,
    /// <c>VehicleUtils.GetBrakingDistance(car, currentSpeed, 0f) &lt;= num2</c> with num2 = distance from its front to the
    /// start of the junction lane (owner ruling 2); counted when it cannot stop (it goes through);</item>
    /// <item>None / Yield / Go: never skipped (0.2.3 unchanged).</item>
    /// </list>
    /// Pure arithmetic, Burst-compatible.
    /// </summary>
    public static class FeedingSignal
    {
        /// <summary>Game.Net.LaneSignalType values (byte enum: None, Stop, SafeStop, Yield, Go).</summary>
        public const int None = 0, Stop = 1, SafeStop = 2, Yield = 3, Go = 4;

        /// <summary>Game.Net.LaneSignalFlags.Physical (CanExtend = 1, Physical = 2).</summary>
        public const int PhysicalFlag = 2;

        /// <summary>The stock lowest priority that ignores a non-physical red / amber (emergency, VehicleUtils.GetPriority).</summary>
        public const int IgnoresSignalPriority = 108;

        /// <summary>Stock: the car obeys the signal's red / amber (<c>m_Priority &lt; 108 || Physical</c>).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Obeys(int priority, int signalFlags) => priority < IgnoresSignalPriority || (signalFlags & PhysicalFlag) != 0;

        /// <summary>Red or amber: the only signals that can skip a feeding car (a quick pre-test before reading the car).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CanSkip(int signal) => signal == Stop || signal == SafeStop;

        /// <summary>
        /// True = the feeding car stops at its signal and is NOT counted. applies = the signal governs this car's entry
        /// (stock flag test, see the class); brakingDistance = GetBrakingDistance(car, min(|velocity|, max speed), 0f);
        /// distanceToStop = metres from its front to the start of the junction lane.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Skips(bool applies, int signal, int signalFlags, int priority, float brakingDistance, float distanceToStop)
        {
            if (!applies || !CanSkip(signal) || !Obeys(priority, signalFlags)) return false;
            return signal == Stop || brakingDistance <= distanceToStop;
        }

        /// <summary>
        /// Stock: the junction lane's signal flags survive for a car coming from its lane only when the junction lane or
        /// the car's lane has the Approach flag (otherwise IterateNextLane clears LevelCrossing / Stop / TrafficLights),
        /// and the signal is read only for a lane with TrafficLights or LevelCrossing.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Applies(bool junctionSignalled, bool junctionApproach, bool feedApproach) =>
            junctionSignalled && (junctionApproach || feedApproach);

        /// <summary>
        /// Remaining metres on the feeding lane from curve position x as stock measures a lane's rest
        /// (IterateFirstLane: max(0.001, lerp(chord, length * (1 - x), 1 - x))); the stock lateral term to the car's
        /// position is left out (shorter = more cars "cannot stop" = counted: never bolder).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float RestOfLane(float chord, float length, float x)
        {
            float t = 1f - x;
            if (t < 0f) t = 0f;
            float d = chord + (length * t - chord) * t;
            return d > 0.001f ? d : 0.001f;
        }
    }

    /// <summary>
    /// Atomic slots for <see cref="SafetyStopEvents"/>: the job wraps a persistent NativeArray&lt;long&gt; (Interlocked),
    /// the tests a long[].
    /// </summary>
    public interface IAtomicSlots
    {
        int Length { get; }
        long Read(int i);
        /// <summary>Sets slot i to value if it holds comparand; returns true on success.</summary>
        bool CompareExchange(int i, long value, long comparand);
    }

    /// <summary>
    /// 0.2.3 diagnostics (b1): count a car's transition into a safety stop once, not per car-step. A fixed-size table
    /// (no allocation in the job) of (car key, simulation frame of its last safety-stop car-step); a car whose last
    /// safety-stop step is at most <see cref="ContinueFrames"/> frames old is continuing (each car is updated every 16
    /// frames), otherwise this step starts a new event. Lock-free (compare-exchange), bounded probing; a full probe range
    /// counts as a new event (over-counts, never hides one). Debug only.
    /// </summary>
    public static class SafetyStopEvents
    {
        public const int Probes = 8;
        /// <summary>Three navigation updates of one car (16 frames each): one missed update still continues the event.</summary>
        public const uint ContinueFrames = 48;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Home(int key, int length) => (int)(((uint)key * 2654435761u) % (uint)length);

        /// <summary>
        /// 0.4.8 review (G7 F3): how long a slot stays reserved for its key (frames since its last write). Exit Go / Wait
        /// records are read back up to ExitGiveWay.DriveGoWindow later (at the hand-over), so another key may not take
        /// their slot before that; every other kind keeps the 0.2.3 ContinueFrames.
        /// </summary>
        public static uint Keep(int key)
        {
            int kind = (key - 1) / ExitGiveWay.EventKindStride;
            return kind == ExitGiveWay.DriveGoKind || kind == ExitGiveWay.ExitWaitKind || kind == ExitGiveWay.DriveBoxedKind ? ExitGiveWay.DriveGoWindow : ContinueFrames;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long Pack(int key, uint frame) => ((long)key << 32) | frame;

        /// <summary>
        /// Records a safety-stop car-step of car <paramref name="entityIndex"/> at <paramref name="frame"/>; returns true
        /// when it starts a new event.
        /// </summary>
        public static bool Record<T>(ref T slots, int entityIndex, uint frame) where T : struct, IAtomicSlots
        {
            int n = slots.Length;
            if (n <= 0) return true;
            int key = entityIndex + 1;
            int home = Home(key, n);
            int probes = Probes < n ? Probes : n;
            for (int i = 0; i < probes; i++)
            {
                int slot = (home + i) % n;
                long v = slots.Read(slot);
                if ((int)(v >> 32) != key) continue;
                uint last = (uint)v;
                bool continuing = frame - last <= ContinueFrames;
                slots.CompareExchange(slot, Pack(key, frame), v);
                return !continuing;
            }
            for (int i = 0; i < probes; i++)
            {
                int slot = (home + i) % n;
                long v = slots.Read(slot);
                int k = (int)(v >> 32);
                if (k != 0 && frame - (uint)v <= Keep(k)) continue;
                if (slots.CompareExchange(slot, Pack(key, frame), v)) return true;
            }
            return true;
        }
    }

    /// <summary>0.4.8 (G7): read side of the event table (no write).</summary>
    public static class EventLookup
    {
        /// <summary>The frame of the last recorded step of key <paramref name="entityIndex"/> (the same probing as
        /// <see cref="SafetyStopEvents.Record"/>); false when the key is not in its probe range.</summary>
        public static bool Last<T>(ref T slots, int entityIndex, out uint frame) where T : struct, IAtomicSlots
        {
            frame = 0;
            int n = slots.Length;
            if (n <= 0) return false;
            int key = entityIndex + 1;
            int home = (int)(((uint)key * 2654435761u) % (uint)n);
            int probes = SafetyStopEvents.Probes < n ? SafetyStopEvents.Probes : n;
            for (int i = 0; i < probes; i++)
            {
                long v = slots.Read((home + i) % n);
                if ((int)(v >> 32) != key) continue;
                frame = (uint)v;
                return true;
            }
            return false;
        }

        /// <summary>A recorded frame is fresh: at most <paramref name="window"/> frames before <paramref name="now"/> (uint wrap safe).</summary>
        public static bool Fresh(bool found, uint last, uint now, uint window) => found && now - last <= window;
    }

    /// <summary>0.2.3 diagnostics (b1): fixed histogram buckets for the per-point distance from W and the projected own arrival time.</summary>
    public static class PointBuckets
    {
        public const int Count = 8;

        /// <summary>Upper bucket edges in metres (last bucket open): 0-2, 2-4, 4-8, 8-12, 12-16, 16-24, 24-32, 32+.</summary>
        public static float DistanceEdge(int i) => i == 0 ? 2f : i == 1 ? 4f : i == 2 ? 8f : i == 3 ? 12f : i == 4 ? 16f : i == 5 ? 24f : i == 6 ? 32f : float.PositiveInfinity;

        /// <summary>Upper bucket edges in seconds (last bucket open): 0-0.5, 0.5-1, 1-1.5, 1.5-2, 2-2.5, 2.5-3, 3-4, 4+.</summary>
        public static float ArrivalEdge(int i) => i == 0 ? 0.5f : i == 1 ? 1f : i == 2 ? 1.5f : i == 3 ? 2f : i == 4 ? 2.5f : i == 5 ? 3f : i == 6 ? 4f : float.PositiveInfinity;

        public static int Distance(float metres)
        {
            for (int i = 0; i < Count - 1; i++) if (metres < DistanceEdge(i)) return i;
            return Count - 1;
        }

        public static int Arrival(float seconds)
        {
            for (int i = 0; i < Count - 1; i++) if (seconds < ArrivalEdge(i)) return i;
            return Count - 1;
        }
    }
}
