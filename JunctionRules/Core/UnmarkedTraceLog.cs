using System.Collections.Generic;
using System.Globalization;

namespace JunctionRules.Core
{
    /// <summary>
    /// JR-T-014d (G11) diagnostics, ON BY DEFAULT (owner rule for 0.4.8: evidence must not depend on switching a setting on).
    /// One fixed buffer of longs shared by the car job (copy), the person job (Systems/UnmarkedCrossingSystem.cs) and the
    /// main-thread drain: always-on counters (Interlocked adds) and a fixed ring of notable events / decision samples (one
    /// Interlocked cursor increment + plain writes + a stamp written last). Nothing allocates or waits on the job side.
    /// The main thread drains the ring with line caps and writes one summary line per interval (60 s of game time).
    /// This file: the layout, the record kinds and the drain / formatting (pure, tested offline).
    /// </summary>
    public static class UnmarkedTraceLayout
    {
        // ---- counters (always on; running totals) ----
        /// <summary>Car-steps in which a car did not give way to a person at / approaching an unmarked crossing although the game's check would have held it.</summary>
        public const int CarStepsNotHeld = 0;
        /// <summary>Car-steps in which a person already on the carriageway of an unmarked crossing made a car brake (safety floor; expected rare).</summary>
        public const int FloorStops = 1;
        /// <summary>People held at the kerb (episodes: one per person and wait).</summary>
        public const int PeopleHeld = 2;
        /// <summary>Waits at the kerb longer than <see cref="LongWaitSeconds"/> (one per episode).</summary>
        public const int LongWaits = 3;
        /// <summary>Person updates whose car scan hit the cap (counted as busy: wait).</summary>
        public const int PersonCapHits = 4;
        /// <summary>Person updates held at the kerb (the episodes' length in 4/15 s updates).</summary>
        public const int PersonUpdatesHeld = 5;
        public const int CounterCount = 8;

        // ---- ring ----
        public const int Cursor = 8;
        /// <summary>The simulation frame, written by the main thread each update (the car job stamps records with it).</summary>
        public const int FrameSlot = 9;
        public const int RingStart = 16;
        public const int RingSlots = 256;
        /// <summary>Per slot: [0] stamp (sequence + 1, written last), [1] kind, [2..7] payload.</summary>
        public const int SlotLongs = 8;
        public const int TotalLongs = RingStart + RingSlots * SlotLongs;

        /// <summary>Notable: a car braked for a person already on the carriageway of an unmarked crossing. Payload: frame, car, person, car speed cm/s, limit cm/s, person position (PackXZ).</summary>
        public const int KindFloor = 1;
        /// <summary>Notable: a person waited at the kerb longer than <see cref="LongWaitSeconds"/>. Payload: frame, person, blocking car, waited frames, lane, person position (PackXZ).</summary>
        public const int KindLongWait = 2;
        /// <summary>Sample: a person's wait at the kerb started. Payload: frame, person, car, gap cm, car speed cm/s, own crossing time ms.</summary>
        public const int KindHoldStart = 3;

        public const float LongWaitSeconds = 60f;
        /// <summary>Summary interval: 60 s of game time (60 simulation frames a second, as Core/PeoplePass.cs).</summary>
        public const uint IntervalFrames = 3600u;
        /// <summary>Most notable lines per interval (the rest are counted as suppressed).</summary>
        public const int NotableCap = 20;
        /// <summary>Most sample lines per interval.</summary>
        public const int SampleCap = 5;

        public static int SlotBase(long sequence) => RingStart + (int)(sequence % RingSlots) * SlotLongs;

        /// <summary>A world position (x, z) packed into one long at decimetre resolution (±214 km).</summary>
        public static long PackXZ(float x, float z)
        {
            int ix = (int)System.Math.Round(x * 10f), iz = (int)System.Math.Round(z * 10f);
            return ((long)ix << 32) | (uint)iz;
        }

        public static void UnpackXZ(long v, out float x, out float z)
        {
            x = (int)(v >> 32) * 0.1f;
            z = (int)(uint)(v & 0xFFFFFFFFL) * 0.1f;
        }
    }

    /// <summary>Read access to the shared buffer (the system wraps the native pointer; tests an array).</summary>
    public interface ITraceBuffer
    {
        long Read(int index);
    }

    /// <summary>
    /// Main-thread drain of the ring and the per-interval summary (Systems/UnmarkedCrossingSystem.cs calls
    /// <see cref="Drain"/> every update; cheap when nothing is new). A record is read only when its stamp equals its
    /// sequence + 1 before and after the payload is read (a writer lapping the ring meanwhile = skipped, counted as overrun).
    /// </summary>
    public sealed class UnmarkedTraceDrain
    {
        private long m_Read;
        private bool m_Started;
        private uint m_IntervalStart;
        private readonly long[] m_CountersAtStart = new long[UnmarkedTraceLayout.CounterCount];
        private int m_Notable, m_NotableSuppressed, m_Samples, m_SamplesSuppressed;
        private readonly long[] m_Words = new long[6];   // final review F5: no allocation per drain
        private long m_Overruns;

        /// <summary>Lines produced by the last call (reused list).</summary>
        public readonly List<string> Lines = new List<string>();

        /// <summary>
        /// Reads new ring records and, at the end of an interval, the summary. frame = the simulation frame now; ruleOn = the
        /// setting (shown in the summary). Returns the lines to log (also in <see cref="Lines"/>).
        /// </summary>
        public List<string> Drain<T>(ref T buffer, uint frame, bool ruleOn) where T : struct, ITraceBuffer
        {
            Lines.Clear();
            if (!m_Started)
            {
                m_Started = true;
                m_Read = buffer.Read(UnmarkedTraceLayout.Cursor);
                m_IntervalStart = frame;
                for (int i = 0; i < UnmarkedTraceLayout.CounterCount; i++) m_CountersAtStart[i] = buffer.Read(i);
                return Lines;
            }
            long cursor = buffer.Read(UnmarkedTraceLayout.Cursor);
            if (cursor - m_Read > UnmarkedTraceLayout.RingSlots)
            {
                m_Overruns += cursor - m_Read - UnmarkedTraceLayout.RingSlots;
                m_Read = cursor - UnmarkedTraceLayout.RingSlots;
            }
            long[] p = m_Words;
            for (; m_Read < cursor; m_Read++)
            {
                int b = UnmarkedTraceLayout.SlotBase(m_Read);
                long stamp = buffer.Read(b);
                if (stamp < m_Read + 1) break;                          // final review F5: still being written: next drain
                if (stamp != m_Read + 1) { m_Overruns++; continue; }   // overwritten by a newer record
                int kind = (int)buffer.Read(b + 1);
                for (int k = 0; k < 6; k++) p[k] = buffer.Read(b + 2 + k);
                if (buffer.Read(b) != m_Read + 1) { m_Overruns++; continue; }
                bool notable = kind != UnmarkedTraceLayout.KindHoldStart;
                if (notable)
                {
                    if (m_Notable >= UnmarkedTraceLayout.NotableCap) { m_NotableSuppressed++; continue; }
                    m_Notable++;
                }
                else
                {
                    if (m_Samples >= UnmarkedTraceLayout.SampleCap) { m_SamplesSuppressed++; continue; }
                    m_Samples++;
                }
                Lines.Add(FormatRecord(kind, p));
            }
            if (unchecked(frame - m_IntervalStart) >= UnmarkedTraceLayout.IntervalFrames)
            {
                var d = new long[UnmarkedTraceLayout.CounterCount];
                for (int i = 0; i < d.Length; i++)
                {
                    long now = buffer.Read(i);
                    d[i] = now - m_CountersAtStart[i];
                    m_CountersAtStart[i] = now;
                }
                Lines.Add(FormatSummary(ruleOn, unchecked(frame - m_IntervalStart), d, m_Notable, m_NotableSuppressed, m_Samples, m_SamplesSuppressed, m_Overruns));
                m_IntervalStart = frame;
                m_Notable = m_NotableSuppressed = m_Samples = m_SamplesSuppressed = 0;
                m_Overruns = 0;
            }
            return Lines;
        }

        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        public static string FormatRecord(int kind, long[] p)
        {
            switch (kind)
            {
                case UnmarkedTraceLayout.KindFloor:
                {
                    UnmarkedTraceLayout.UnpackXZ(p[5], out float x, out float z);
                    return "G11 unmarked crossing: car " + p[1].ToString(Ci) + " braked for person " + p[2].ToString(Ci) + " already on the carriageway (car "
                        + (p[3] / 100.0).ToString("0.0", Ci) + " m/s limited to " + (p[4] / 100.0).ToString("0.0", Ci) + " m/s) at ("
                        + x.ToString("0.0", Ci) + ", " + z.ToString("0.0", Ci) + "), frame " + p[0].ToString(Ci);
                }
                case UnmarkedTraceLayout.KindLongWait:
                {
                    UnmarkedTraceLayout.UnpackXZ(p[5], out float x, out float z);
                    return "G11 unmarked crossing: person " + p[1].ToString(Ci) + " held at the kerb over " + (p[3] / 60.0).ToString("0", Ci)
                        + " s (last blocking car " + p[2].ToString(Ci) + ", lane " + p[4].ToString(Ci) + ") at (" + x.ToString("0.0", Ci) + ", "
                        + z.ToString("0.0", Ci) + "), frame " + p[0].ToString(Ci);
                }
                case UnmarkedTraceLayout.KindHoldStart:
                    return "G11 unmarked crossing sample: person " + p[1].ToString(Ci) + " waits at the kerb for car " + p[2].ToString(Ci) + ": gap "
                        + (p[3] / 100.0).ToString("0.0", Ci) + " m, car " + (p[4] / 100.0).ToString("0.0", Ci) + " m/s, own crossing "
                        + (p[5] / 1000.0).ToString("0.0", Ci) + " s + " + UnmarkedCrossing.MarginSeconds.ToString("0.0", Ci) + " s margin, frame " + p[0].ToString(Ci);
                default:
                    return "G11 unmarked crossing: unknown record kind " + kind.ToString(Ci);
            }
        }

        public static string FormatSummary(bool ruleOn, uint frames, long[] d, int notable, int notableSuppressed, int samples, int samplesSuppressed, long overruns)
        {
            return "G11 unmarked crossings, last " + (frames / 60.0).ToString("0", Ci) + " s game time (People give way at unmarked crossings "
                + (ruleOn ? "on" : "off") + "): people held at the kerb " + d[UnmarkedTraceLayout.PeopleHeld].ToString(Ci)
                + " (person-updates " + d[UnmarkedTraceLayout.PersonUpdatesHeld].ToString(Ci) + ", waits over "
                + UnmarkedTraceLayout.LongWaitSeconds.ToString("0", Ci) + " s " + d[UnmarkedTraceLayout.LongWaits].ToString(Ci)
                + ", scan cap hits " + d[UnmarkedTraceLayout.PersonCapHits].ToString(Ci) + "), car-steps not held for people "
                + d[UnmarkedTraceLayout.CarStepsNotHeld].ToString(Ci) + ", stopped for a person already crossing unmarked "
                + d[UnmarkedTraceLayout.FloorStops].ToString(Ci) + "; lines: notable " + notable.ToString(Ci) + " (suppressed "
                + notableSuppressed.ToString(Ci) + "), samples " + samples.ToString(Ci) + " (suppressed " + samplesSuppressed.ToString(Ci)
                + "), ring overruns " + overruns.ToString(Ci);
        }
    }
}
