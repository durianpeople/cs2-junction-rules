using JunctionRules.Core;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace JunctionRules.Stage2
{
    /// <summary>
    /// Stage 2, main-thread side, used only by the copy's navigation system (JR-STAGE2 blocks) and the copy's timing
    /// probe. Like the copy types it is never named by Mod or CarDrivingSwitchSystem (CopyIsolationTests).
    /// </summary>
    public static class JRStage2Bridge
    {
        private static NativeArray<long> s_Counters;
        private static NativeArray<long> s_SafetyEvents;
        private static NativeArray<long> s_TraceRing;
        private static readonly ExitTraceReader s_TraceReader = new ExitTraceReader();
        private static readonly LineRateLimiter s_TraceLimiter = new LineRateLimiter(ExitTrace.MaxLinesPerSecond);
        private static double s_TraceLastSummary;
        private static bool s_TraceLogged;

        /// <summary>0.2.3: slots of the safety-stop event table (Core/ArrivalTime.cs SafetyStopEvents), 64 KiB.</summary>
        public const int SafetyEventSlots = 8192;

        /// <summary>The mode the copy's last navigation step ran with (main thread).</summary>
        public static int AppliedMode { get; private set; }

        /// <summary>Number of copy navigation steps so far (lets the probe see which steps ran with which rule).</summary>
        public static long Steps { get; private set; }

        /// <summary>
        /// Called in the copy's OnUpdate right before the job is scheduled: takes the mode published this step by the
        /// switch system and makes sure the counter array exists (it is always passed to the job, created, so the job
        /// struct never holds an unallocated container).
        /// </summary>
        public static int BeginStep()
        {
            if (!s_Counters.IsCreated) s_Counters = new NativeArray<long>(Stage2Counters.Count, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            if (!s_SafetyEvents.IsCreated) s_SafetyEvents = new NativeArray<long>(SafetyEventSlots, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            if (!s_TraceRing.IsCreated) s_TraceRing = new NativeArray<long>(ExitTrace.RingLength, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            AppliedMode = Stage2Handoff.Requested;
            bool trace = ExitTraceHandoff.Requested;
            if (!s_TraceLogged || trace != TraceOn)
            {
                if (trace) Mod.log.Info("exit trace (log) on: one line per vehicle leaving a building or parking space (first wait, hand-over to the road), at most "
                    + ExitTrace.MaxLinesPerSecond + " lines a second" + ((AppliedMode & Stage2Mode.ExitGiveWay) == 0 ? "; needs Vehicles leaving buildings give way (off now: no lines)" : ""));
                else if (s_TraceLogged) Mod.log.Info("exit trace (log) off");
                s_TraceLogged = true;
            }
            TraceOn = trace;
            DrainTrace();
            DrainEntrance();
            Steps++;
            UpdateWatch();
            return AppliedMode;
        }

        /// <summary>0.4.7: setting "Exit trace (log)" for this step (main thread; S2-23 hands it to the job).</summary>
        public static bool TraceOn { get; private set; }

        /// <summary>0.4.7: the exit trace ring (Core ExitTrace layout; written by the job with Interlocked, read here).</summary>
        public static NativeArray<long> TraceRing => s_TraceRing;

        /// <summary>0.4.7: logs the finished trace records (main thread, never waits for the job), rate-limited.</summary>
        private static void DrainTrace()
        {
            if (!s_TraceRing.IsCreated) return;
            long head = ReadTrace(0);
            // Review F5: nothing new and nothing to sum up (the usual case, also with the trace off): no clock read, no
            // delegate; the drain itself uses cached delegates (no allocation per step).
            bool fresh = ExitTraceReader.HasNew(head, s_TraceReader.Next);
            if (!fresh && s_TraceLimiter.Dropped == 0 && s_TraceReader.Lost == 0) return;
            double now = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
            s_TraceNow = now;
            if (fresh) s_TraceReader.Drain(head, s_ReadTrace, s_EmitTrace);
            if (now - s_TraceLastSummary >= 1.0)
            {
                long dropped = s_TraceLimiter.TakeDropped(), lost = s_TraceReader.TakeLost();
                if (dropped > 0 || lost > 0) Mod.log.Info("exit trace: " + dropped + " lines dropped (rate limit), " + lost + " records lost (ring overrun)");
                s_TraceLastSummary = now;
            }
        }

        private static double s_TraceNow;
        private static readonly System.Func<int, long> s_ReadTrace = ReadTrace;
        private static readonly System.Action<long, long, long, long> s_EmitTrace = EmitTrace;

        private static void EmitTrace(long v, long b, long f, long fr)
        {
            if (s_TraceLimiter.Allow(s_TraceNow)) Mod.log.Info(ExitTrace.Format(v, b, f, fr));
        }

        private static unsafe long ReadTrace(int i)
        {
            long* p = (long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(s_TraceRing);
            return System.Threading.Interlocked.Read(ref p[i]);
        }

        // ---- JR-T-013b (G4): watch one junction (Core/JunctionWatch.cs) ----
        private static NativeArray<long> s_Watch;
        private static int s_WatchVersion;
        private static long s_WatchRead;
        private static readonly WatchLimiter s_WatchLimiter = new WatchLimiter();
        private static readonly System.Collections.Generic.List<(long Car, long Blocker, long Lane, long Packed)> s_WatchEvents =
            new System.Collections.Generic.List<(long Car, long Blocker, long Lane, long Packed)>();

        private static readonly System.Func<int, long> s_ReadWatch = ReadWatch;

        private static unsafe long ReadWatch(int i) =>
            System.Threading.Interlocked.Read(ref ((long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(s_Watch))[i]);

        /// <summary>The shared watch array (always created, so the job struct never holds an unallocated container).</summary>
        public static NativeArray<long> Watch => s_Watch;

        /// <summary>
        /// Main thread, each copy step before the job is scheduled: logs the events the jobs wrote since the last step
        /// (rate-limited), then hands the watched node (Core WatchHandoff) to slot 0. A changed node resets the ring.
        /// A job of the previous step may still be writing: an event without its stamp is read at the next step.
        /// </summary>
        private static unsafe void UpdateWatch()
        {
            if (!s_Watch.IsCreated) s_Watch = new NativeArray<long>(JunctionWatch.Length, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            long* p = (long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(s_Watch);
            uint frame = (uint)Steps;
            if (p[0] != 0)
            {
                s_WatchEvents.Clear();
                long cursor = System.Threading.Interlocked.Read(ref p[1]);
                JunctionWatch.Drain(s_ReadWatch, ref s_WatchRead, cursor, s_WatchEvents, out int dropped);
                string summary = s_WatchLimiter.Tick(frame);
                if (summary != null) Mod.log.Info("watch junction " + JunctionWatch.EntityText(p[0]) + ": " + summary);
                if (dropped > 0) Mod.log.Info("watch junction " + JunctionWatch.EntityText(p[0]) + ": " + dropped + " hold event(s) overwritten before they were read");
                foreach (var e in s_WatchEvents)
                {
                    JunctionWatch.Unpack(e.Packed, out int rule, out _, out _, out _);
                    if (s_WatchLimiter.Allow(e.Car, rule, e.Blocker, frame))
                        Mod.log.Info("watch junction " + JunctionWatch.EntityText(p[0]) + ": " + JunctionWatch.Line(e.Car, e.Blocker, e.Lane, e.Packed));
                }
            }
            if (s_WatchVersion != WatchHandoff.Version)
            {
                s_WatchVersion = WatchHandoff.Version;
                // The jobs of the last step finished before this OnUpdate schedules a new one only if the game completed
                // them; a late write of the old node lands in the cleared ring and is read as one more line: harmless.
                p[0] = WatchHandoff.Node;
                s_WatchRead = System.Threading.Interlocked.Read(ref p[1]);
                s_WatchLimiter.Reset();
            }
        }

        public static NativeArray<long> Counters => s_Counters;

        // ---- JR-T-014c (G9): always-on entrance box log (Core/EntranceLog.cs); no setting ----
        private static NativeArray<long> s_EntranceRing;
        private static EntranceLogReader s_EntranceReader = new EntranceLogReader();
        private static readonly EntranceLogBook s_EntranceBook = new EntranceLogBook(line => Mod.log.Info(line));
        private static readonly System.Func<int, long> s_ReadEntrance = ReadEntrance;
        private static readonly System.Action<long, long, long, long> s_EmitEntrance = EmitEntrance;
        private static double s_EntranceNow;
        private static uint s_EntranceFrame;
        private static bool s_EntranceLogged;

        private static Game.Simulation.SimulationSystem s_Simulation;

        /// <summary>The game's simulation frame (main thread); 0 before the simulation system exists.</summary>
        private static uint SimulationFrame()
        {
            if (s_Simulation == null) s_Simulation = Unity.Entities.World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<Game.Simulation.SimulationSystem>();
            return s_Simulation != null ? s_Simulation.frameIndex : 0u;
        }

        private static NativeArray<long> EntranceRing()
        {
            if (!s_EntranceRing.IsCreated)
            {
                s_EntranceRing = new NativeArray<long>(EntranceLog.RingLength, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                s_EntranceReader = new EntranceLogReader();   // a new ring starts at sequence 1
            }
            return s_EntranceRing;
        }

        private static unsafe long ReadEntrance(int i) =>
            System.Threading.Interlocked.Read(ref ((long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(s_EntranceRing))[i]);

        private static void EmitEntrance(long vehicle, long road, long facts, long frame)
        {
            s_EntranceFrame = (uint)frame;
            s_EntranceBook.Record(vehicle, road, facts, (uint)frame, s_EntranceNow);
        }

        /// <summary>
        /// Main thread, each copy step: reads the finished entrance records (at most EntranceLog.Slots, never waits for the
        /// job) into the episode book; the book writes at most MaxNotable + MaxSamples + 1 lines per interval. Nothing new
        /// (the usual case): one read of the head and return.
        /// </summary>
        private static void DrainEntrance()
        {
            EntranceRing();
            if (!s_EntranceLogged)
            {
                s_EntranceLogged = true;
                Mod.log.Info("entrance box log (always on): notable vehicles turning into boxed buildings (stood in the box without room, held or stood over 30 s), at most "
                    + EntranceLogBook.MaxNotable + " notable + " + EntranceLogBook.MaxSamples + " sample lines and one summary per " + EntranceLogBook.IntervalSeconds + " s");
            }
            long head = ReadEntrance(0);
            bool fresh = head >= s_EntranceReader.Next;
            if (!fresh && s_EntranceBook.Tracked == 0) return;
            s_EntranceNow = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
            if (fresh) s_EntranceReader.Drain(head, s_ReadEntrance, s_EmitEntrance);
            s_EntranceBook.AddLost(s_EntranceReader.TakeLost());
            s_EntranceBook.Tick(s_EntranceNow, s_EntranceFrame);
        }

        private static JRBoxEdges s_BoxEdges;
        private static int s_BoxEdgeVersion = -1;

        /// <summary>
        /// 0.4.2 (JR-T-010-r; 0.4.3: one band per boxed car exit): the boxed exits (main thread, S2-23 takes it by value), rebuilt from
        /// Core BoxEdgeHandoff when CarDrivingSwitchSystem published new keys.
        /// </summary>
        public static JRBoxEdges BoxEdges
        {
            get
            {
                if (s_BoxEdgeVersion != BoxEdgeHandoff.Version)
                {
                    s_BoxEdges = JRBoxEdges.FromBands(BoxEdgeHandoff.Keys, BoxEdgeHandoff.T, BoxEdgeHandoff.Masks);
                    s_BoxEdgeVersion = BoxEdgeHandoff.Version;
                }
                s_BoxEdges.m_EntranceLog = EntranceRing();   // JR-T-014c (G9): always on
                s_BoxEdges.m_Frame = SimulationFrame();
                return s_BoxEdges;
            }
        }

        /// <summary>0.2.3: the safety-stop event table (written by the job only when counting; atomic compare-exchange).</summary>
        public static NativeArray<long> SafetyEvents => s_SafetyEvents;

        /// <summary>
        /// Copies the running totals (main thread, never waits). A job of the current step may still be adding; a car-step
        /// that lands a moment late is counted in the next window, which is fine for a debug count.
        /// </summary>
        public static unsafe bool Read(long[] totals)
        {
            if (!s_Counters.IsCreated || totals == null || totals.Length < Stage2Counters.Count) return false;
            long* p = (long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(s_Counters);
            for (int i = 0; i < Stage2Counters.Count; i++) totals[i] = System.Threading.Interlocked.Read(ref p[i]);
            return true;
        }

        /// <summary>From the copy's OnDestroy, after its job handle completed.</summary>
        public static void Release()
        {
            if (s_Counters.IsCreated) s_Counters.Dispose();
            s_Counters = default;
            if (s_SafetyEvents.IsCreated) s_SafetyEvents.Dispose();
            s_SafetyEvents = default;
            if (s_TraceRing.IsCreated) s_TraceRing.Dispose();
            s_TraceRing = default;
            if (s_EntranceRing.IsCreated) s_EntranceRing.Dispose();   // JR-T-014c (G9)
            s_EntranceRing = default;
            s_Simulation = null;
            if (s_Watch.IsCreated) s_Watch.Dispose();
            s_Watch = default;
            s_WatchVersion = -1;
        }
    }
}
