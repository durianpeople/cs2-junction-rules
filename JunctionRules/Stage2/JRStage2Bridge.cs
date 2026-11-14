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
            AppliedMode = Stage2Handoff.Requested;
            Steps++;
            return AppliedMode;
        }

        public static NativeArray<long> Counters => s_Counters;

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
        }
    }
}
