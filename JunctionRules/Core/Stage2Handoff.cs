using System.Globalization;
using System.Threading;

namespace JunctionRules.Core
{
    /// <summary>
    /// Main-thread handoff of the stage 2 setting into the copy's navigation job, the same pattern as the stage 1
    /// switch: CarDrivingSwitchSystem (registered before the game's navigation slot) publishes the requested mode once
    /// per simulation step; the copy's OnUpdate (main thread, later in the same step) reads it once and copies it by
    /// value into the job struct, so a running job never sees a change. A change therefore applies at the next step in
    /// which the copy drives. Volatile access keeps it correct even if a UI thread wrote the setting.
    /// </summary>
    public static class Stage2Handoff
    {
        private static int s_Requested;

        public static int Requested => Volatile.Read(ref s_Requested);

        public static void Publish(int mode) => Volatile.Write(ref s_Requested, mode);
    }

    /// <summary>Logs a change of the requested rule once (the switch system calls it each step).</summary>
    public sealed class GapRuleSwitch
    {
        private bool m_Has;
        private int m_Mode;

        /// <summary>Returns a log line when the requested mode changed (or on the first call), else null.</summary>
        public string Step(JunctionGapRule rule, bool counting, out int mode, JunctionWaitingSpot spot = JunctionWaitingSpot.BeforeFirstCrossing)
        {
            mode = Stage2Mode.For(rule, counting, spot);
            if (m_Has && mode == m_Mode) return null;
            bool first = !m_Has;
            bool ruleChanged = !first && ((mode ^ m_Mode) & (Stage2Mode.Rule | Stage2Mode.AtLine)) != 0;
            m_Has = true;
            int old = m_Mode;
            m_Mode = mode;
            if (first) return $"junction gap rule starts as {Stage2Mode.RuleName(mode)} (acts only while the Junction Rules copy drives), counters {((mode & Stage2Mode.Count) != 0 ? "on" : "off")}";
            if (ruleChanged) return $"junction gap rule switched to {Stage2Mode.RuleName(mode)} (applies at the next step in which the Junction Rules copy drives)";
            return (mode & Stage2Mode.Count) != 0 && (old & Stage2Mode.Count) == 0 ? "junction gap counters on" : "junction gap counters off";
        }
    }

    /// <summary>Indices and the log text of the debug counters (counted in car navigation updates, "car-steps").</summary>
    public static class Stage2Counters
    {
        /// <summary>Car-steps in which a car still before its entry spot took the decision for its whole path (rule on only).</summary>
        public const int Decisions = 0;
        /// <summary>Car-steps in which a car stood at its entry spot because a later conflict (not the first point) was busy (rule on only).</summary>
        public const int WaitedForLater = 1;
        /// <summary>Car-steps in which a car stood inside the junction (front past its waiting spot) held by a give-way (both rules).</summary>
        public const int StoppedInsideYield = 2;
        /// <summary>Car-steps in which a car stood inside the junction behind a car crossing its path (collision braking, both rules).</summary>
        public const int StoppedInsideCrossing = 3;
        /// <summary>Car-steps in which a safety stop inside the junction limited the car (braking for it or standing at it; rule on only, 0.2.1).</summary>
        public const int SafetyStops = 4;
        /// <summary>0.2.3: safety-stop EVENTS, a car's transition into a safety stop counted once (Core/ArrivalTime.cs SafetyStopEvents).</summary>
        public const int SafetyStopEvents = 5;
        /// <summary>0.2.3: car-steps in which a car on a conflicting junction lane counted as busy only because of the arrival-time projection (stock 2 s look-ahead alone: clear).</summary>
        public const int ProjectedLaneHits = 6;
        /// <summary>0.2.3: car-steps in which a car on a lane feeding a conflicting junction lane blocked a point as a virtual reservation.</summary>
        public const int FeedingLaneHits = 7;
        /// <summary>0.2.3: conflict points sampled at the entry spot (car standing there, rule on): one per point per car-step.</summary>
        public const int PointSamples = 8;
        /// <summary>0.2.3: sum of the sampled points' distance from W, in centimetres.</summary>
        public const int PointDistanceSumCm = 9;
        /// <summary>0.2.3: sum of the sampled points' projected own arrival time, in milliseconds.</summary>
        public const int ArrivalSumMs = 10;
        /// <summary>0.2.3: first of PointBuckets.Count histogram slots for the distance from W.</summary>
        public const int DistanceHistogram = 11;
        /// <summary>0.2.3: first of PointBuckets.Count histogram slots for the projected own arrival time.</summary>
        public const int ArrivalHistogram = DistanceHistogram + PointBuckets.Count;
        public const int Count = ArrivalHistogram + PointBuckets.Count;

        /// <summary>Speed (m/s) under which a car counts as standing.</summary>
        public const float StandingSpeed = 0.5f;

        /// <summary>Tolerance when deciding that a recorded braking speed is the one that limits the car.</summary>
        public const float BindingTolerance = 0.01f;

        /// <summary>The recorded limit is the car's binding limit and the car stands.</summary>
        public static bool IsStandingOn(bool recorded, float recordedSpeed, float finalMaxSpeed, float currentSpeed) =>
            recorded && recordedSpeed <= finalMaxSpeed + BindingTolerance && currentSpeed < StandingSpeed;

        /// <summary>The recorded limit is the car's binding limit (moving or standing).</summary>
        public static bool IsBinding(bool recorded, float recordedSpeed, float finalMaxSpeed) =>
            recorded && recordedSpeed <= finalMaxSpeed + BindingTolerance;

        /// <summary>Index of the bucket holding the median sample of a histogram (-1 when empty).</summary>
        public static int MedianBucket(long[] values, int first, int count)
        {
            long total = 0;
            for (int i = 0; i < count; i++) total += values[first + i];
            if (total <= 0) return -1;
            long half = (total + 1) / 2, run = 0;
            for (int i = 0; i < count; i++)
            {
                run += values[first + i];
                if (run >= half) return i;
            }
            return count - 1;
        }

        private static string Histogram(long[] d, int first, bool distance, CultureInfo ci)
        {
            var sb = new System.Text.StringBuilder();
            float lo = 0f;
            for (int i = 0; i < PointBuckets.Count; i++)
            {
                float hi = distance ? PointBuckets.DistanceEdge(i) : PointBuckets.ArrivalEdge(i);
                if (i > 0) sb.Append(' ');
                sb.Append(lo.ToString(ci)).Append(float.IsPositiveInfinity(hi) ? "+" : "-" + hi.ToString(ci)).Append(':').Append(d[first + i].ToString(ci));
                lo = hi;
            }
            int m = MedianBucket(d, first, PointBuckets.Count);
            if (m >= 0)
            {
                float mlo = m == 0 ? 0f : distance ? PointBuckets.DistanceEdge(m - 1) : PointBuckets.ArrivalEdge(m - 1);
                float mhi = distance ? PointBuckets.DistanceEdge(m) : PointBuckets.ArrivalEdge(m);
                sb.Append(" (median in ").Append(mlo.ToString(ci)).Append(float.IsPositiveInfinity(mhi) ? "+" : "-" + mhi.ToString(ci)).Append(')');
            }
            return sb.ToString();
        }

        /// <summary>
        /// The suffix of the copy's timing line. ruleMask: bit 0 = a step ran with stock, bit 1 = with whole junction
        /// (both = the rule was switched inside the window). deltas: null when counters were not on for the whole window.
        /// </summary>
        public static string Format(int ruleMask, long[] deltas)
        {
            string rule = ruleMask == 1 ? "stock" : ruleMask == 2 ? "whole junction" : ruleMask == 3 ? "mixed (switched in this window)" : "none";
            if (deltas == null || deltas.Length < Count) return "; junction gap rule " + rule;
            var ci = CultureInfo.InvariantCulture;
            long n = deltas[PointSamples];
            string points = n <= 0 ? "points at W 0"
                : "points at W " + n.ToString(ci) +
                  ", distance from W mean " + (deltas[PointDistanceSumCm] / 100.0 / n).ToString("0.0", ci) + " m [" + Histogram(deltas, DistanceHistogram, true, ci) + "]" +
                  ", own arrival mean " + (deltas[ArrivalSumMs] / 1000.0 / n).ToString("0.00", ci) + " s [" + Histogram(deltas, ArrivalHistogram, false, ci) + "]";
            return "; junction gap rule " + rule +
                   "; car-steps: whole-path decisions " + deltas[Decisions].ToString(ci) +
                   ", waited at entry spot for a later conflict " + deltas[WaitedForLater].ToString(ci) +
                   ", safety stops inside " + deltas[SafetyStops].ToString(ci) +
                   " (events " + deltas[SafetyStopEvents].ToString(ci) + ")" +
                   ", stopped inside for a give-way " + deltas[StoppedInsideYield].ToString(ci) +
                   ", stopped inside behind a crossing car " + deltas[StoppedInsideCrossing].ToString(ci) +
                   "; arrival projection: busy only by projection, junction-lane car " + deltas[ProjectedLaneHits].ToString(ci) +
                   ", feeding-lane car " + deltas[FeedingLaneHits].ToString(ci) +
                   "; " + points;
        }
    }
}
