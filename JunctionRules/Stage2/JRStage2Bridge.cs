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
            Steps++;
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
            double now = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
            s_TraceReader.Drain(head, ReadTrace, (v, b, f, fr) =>
            {
                if (s_TraceLimiter.Allow(now)) Mod.log.Info(ExitTrace.Format(v, b, f, fr));
            });
            if (now - s_TraceLastSummary >= 1.0)
            {
                long dropped = s_TraceLimiter.TakeDropped(), lost = s_TraceReader.TakeLost();
                if (dropped > 0 || lost > 0) Mod.log.Info("exit trace: " + dropped + " lines dropped (rate limit), " + lost + " records lost (ring overrun)");
                s_TraceLastSummary = now;
            }
        }

        private static unsafe long ReadTrace(int i)
        {
            long* p = (long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(s_TraceRing);
            return System.Threading.Interlocked.Read(ref p[i]);
        }

        public static NativeArray<long> Counters => s_Counters;

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
                    s_BoxEdges = JRBoxEdges.FromBands(BoxEdgeHandoff.Keys, BoxEdgeHandoff.T);
                    s_BoxEdgeVersion = BoxEdgeHandoff.Version;
                }
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
        }
    }
}
