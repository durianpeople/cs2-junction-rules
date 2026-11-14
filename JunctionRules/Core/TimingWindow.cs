using System;
using System.Collections.Generic;
using System.Globalization;

namespace JunctionRules.Core
{
    /// <summary>
    /// Collects navigation job timings over a window of simulation frames and formats one log line per window.
    /// Game-independent; the probe systems feed it. Times are in milliseconds.
    /// </summary>
    public sealed class TimingWindow
    {
        private readonly List<double> m_Samples = new List<double>(256);
        private readonly string m_Label;
        private readonly uint m_WindowFrames;
        private bool m_Started;
        private uint m_StartFrame;
        private long m_StartTicks;
        private int m_Skipped;

        public TimingWindow(string label, uint windowFrames)
        {
            m_Label = label;
            m_WindowFrames = Math.Max(1u, windowFrames);
        }

        public int Count => m_Samples.Count;
        public int Skipped => m_Skipped;

        public void Add(double ms) => m_Samples.Add(ms);

        /// <summary>A sample could not be taken (all marker slots busy).</summary>
        public void Skip() => m_Skipped++;

        /// <summary>True when the next <see cref="Tick"/> would close the window (or start the first one).</summary>
        public bool Due(uint frame) => !m_Started || frame < m_StartFrame || frame - m_StartFrame >= m_WindowFrames;

        /// <summary>
        /// Call once per probe update with the simulation frame and a wall clock (ticks at <paramref name="ticksPerSecond"/>).
        /// Returns a log line when a window is complete (and starts the next), else null.
        /// </summary>
        public string Tick(uint frame, long ticks, long ticksPerSecond, int cars, string suffix = null)
        {
            if (!m_Started || frame < m_StartFrame)
            {
                Restart(frame, ticks);
                return null;
            }
            uint frames = frame - m_StartFrame;
            if (frames < m_WindowFrames) return null;
            double seconds = ticksPerSecond > 0 ? (ticks - m_StartTicks) / (double)ticksPerSecond : 0;
            // A window in which this navigation system never ran (the other pair was active) is not logged.
            string line = m_Samples.Count == 0 && m_Skipped == 0 ? null : Format(m_StartFrame, frame, seconds, cars, suffix);
            Restart(frame, ticks);
            return line;
        }

        public void Restart(uint frame, long ticks)
        {
            m_Started = true;
            m_StartFrame = frame;
            m_StartTicks = ticks;
            m_Samples.Clear();
            m_Skipped = 0;
        }

        /// <summary>suffix (stage 2): appended as is, e.g. "; junction gap rule stock" (null = the stage 1 line).</summary>
        public string Format(uint fromFrame, uint toFrame, double seconds, int cars, string suffix = null)
        {
            var ci = CultureInfo.InvariantCulture;
            uint frames = toFrame - fromFrame;
            string rate = seconds > 0 ? (frames / seconds).ToString("0.0", ci) : "n/a";
            if (m_Samples.Count == 0)
            {
                return $"timing {m_Label}: frames {fromFrame}-{toFrame} ({frames} sim frames in {seconds.ToString("0.00", ci)} s = {rate} frames/s), cars {cars}, no navigation runs sampled (skipped {m_Skipped}){suffix}";
            }
            var sorted = new List<double>(m_Samples);
            sorted.Sort();
            double sum = 0;
            foreach (double s in sorted) sum += s;
            double mean = sum / sorted.Count;
            return $"timing {m_Label}: frames {fromFrame}-{toFrame} ({frames} sim frames in {seconds.ToString("0.00", ci)} s = {rate} frames/s), cars {cars}, " +
                   $"navigation job schedule-to-done ms over {sorted.Count} runs: mean {mean.ToString("0.000", ci)} p50 {Percentile(sorted, 0.50).ToString("0.000", ci)} " +
                   $"p95 {Percentile(sorted, 0.95).ToString("0.000", ci)} max {sorted[sorted.Count - 1].ToString("0.000", ci)} (skipped {m_Skipped}){suffix}";
        }

        /// <summary>Nearest-rank percentile of an ascending list.</summary>
        public static double Percentile(List<double> sorted, double p)
        {
            if (sorted.Count == 0) return 0;
            int rank = (int)Math.Ceiling(p * sorted.Count);
            return sorted[Math.Min(sorted.Count - 1, Math.Max(0, rank - 1))];
        }
    }
}
