using System;
using System.Collections.Generic;
using System.Globalization;

namespace JunctionRules.Core
{
    /// <summary>
    /// JR-T-014c (G9, owner rule for 0.4.8 diagnostics, 17 Nov 2026): the entrance box log. ALWAYS ON (no setting, not tied to
    /// the timing log) and compact. The job (Stage2/JRYellowBox.cs EnteringHold) writes one fixed-size record per car-step of a
    /// vehicle turning INTO a boxed building near the box (held before it, let in, or already in it) into a fixed ring
    /// (Stage2/JRStage2Bridge.cs, handed to the job inside JRBoxEdges); nothing else on the job side. The main thread drains
    /// it each copy step and <see cref="EntranceLogBook"/> folds the records into per-vehicle episodes, then logs:
    /// notable events (a vehicle that went into the box without room and then stood there, a vehicle standing in the box a
    /// long time, a vehicle held before the box a long time), at most <see cref="EntranceLogBook.MaxNotable"/> lines per
    /// interval; at most <see cref="EntranceLogBook.MaxSamples"/> sampled normal decisions per interval; one summary line
    /// per interval with activity. Ring layout (longs): [0] head sequence, then <see cref="Slots"/> records of
    /// <see cref="Words"/>: [0] sequence stamp (written last), [1] vehicle (index | version &lt;&lt; 32), [2] road edge
    /// (index | version &lt;&lt; 32), [3] facts (<see cref="Pack"/>), [4] simulation frame.
    /// </summary>
    public static class EntranceLog
    {
        public const int Words = 5;
        public const int Slots = 512;
        public const int RingLength = 1 + Slots * Words;

        public enum Event : byte
        {
            /// <summary>Before the box, able to stop, way in full: held (on the road).</summary>
            Held = 0,
            /// <summary>Before the box, able to stop, way in has room: let in.</summary>
            Go = 1,
            /// <summary>In the box (or past the point where it can stop): carries on; room = the way in had room this step.</summary>
            InBox = 2,
            /// <summary>0.4.11 (G21): before the box, way in has room, but held for a gap in the traffic its turn crosses (on the road).</summary>
            HeldGap = 3,
        }

        public static int SlotBase(long seq) => 1 + (int)((seq - 1) % Slots) * Words;

        /// <summary>facts = event | room &lt;&lt; 8 | speed in dm/s &lt;&lt; 16 (16 bits) | band start on the lane (0..65535) &lt;&lt; 32.</summary>
        public static long Pack(Event e, bool room, float speed, float bandStart)
        {
            long dm = (long)Math.Max(0, Math.Min(65535, Math.Round(speed * 10.0)));
            long t = (long)Math.Max(0, Math.Min(65535, Math.Round(bandStart * 65535.0)));
            return (long)(byte)e | (room ? 1L << 8 : 0L) | (dm << 16) | (t << 32);
        }

        public static void Unpack(long v, out Event e, out bool room, out float speed, out float bandStart)
        {
            e = (Event)(byte)v;
            room = ((v >> 8) & 1) != 0;
            speed = ((v >> 16) & 0xFFFF) / 10f;
            bandStart = ((v >> 32) & 0xFFFF) / 65535f;
        }

        public static long PackEntity(int index, int version) => (uint)index | ((long)version << 32);

        public static string Ent(long packed) =>
            ((int)(uint)packed).ToString(CultureInfo.InvariantCulture) + ":" + ((int)(packed >> 32)).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Reader of the entrance ring (main thread, never waits for the job); same protocol as the 0.4.7 exit trace.</summary>
    public sealed class EntranceLogReader
    {
        private long m_Next = 1;
        public long Lost { get; private set; }
        public long Next => m_Next;

        /// <summary>emit(vehicle, road, facts, frame). Stops at a record still being written (read again next step).</summary>
        public int Drain(long head, Func<int, long> read, Action<long, long, long, long> emit)
        {
            int n = 0;
            if (head - (m_Next - 1) > EntranceLog.Slots)
            {
                long skip = head - EntranceLog.Slots + 1 - m_Next;
                Lost += skip;
                m_Next += skip;
            }
            while (m_Next <= head)
            {
                int b = EntranceLog.SlotBase(m_Next);
                long stamp = read(b);
                if (stamp < m_Next) break;
                if (stamp > m_Next) { Lost++; m_Next++; continue; }
                long v = read(b + 1), r = read(b + 2), f = read(b + 3), fr = read(b + 4);
                if (read(b) != m_Next) { Lost++; m_Next++; continue; }
                emit(v, r, f, fr);
                n++;
                m_Next++;
            }
            return n;
        }

        public long TakeLost() { long l = Lost; Lost = 0; return l; }
    }

    /// <summary>
    /// Main-thread episode book of the entrance log (game-independent, tested offline). Fixed caps: at most
    /// <see cref="MaxVehicles"/> tracked vehicles, <see cref="MaxNotable"/> notable lines and <see cref="MaxSamples"/>
    /// sample lines per interval, one summary line per interval with activity.
    /// </summary>
    public sealed class EntranceLogBook
    {
        public const double IntervalSeconds = 60.0;
        public const int MaxNotable = 10;
        public const int MaxSamples = 5;
        public const int MaxVehicles = 1024;
        /// <summary>Simulation frames per second at normal speed (CS2 runs 60 simulation frames a game second at 1x).</summary>
        public const int FramesPerSecond = 60;
        /// <summary>No record for this many frames ends the vehicle's episode (it turned in, re-routed or despawned).</summary>
        public const uint EpisodeGapFrames = 5 * FramesPerSecond;
        /// <summary>Held before the box at least this long = notable ("held a long time").</summary>
        public const uint LongHoldFrames = 30 * FramesPerSecond;
        /// <summary>Standing in the box (speed under StandSpeed) this long after entering WITHOUT room = notable.</summary>
        public const uint StandNoRoomFrames = 2 * FramesPerSecond;
        /// <summary>Standing in the box this long even with room (the UK turner waiting for oncoming) = notable.</summary>
        public const uint LongStandFrames = 30 * FramesPerSecond;
        public const float StandSpeed = 0.5f;

        private sealed class Episode
        {
            public long Road;
            public uint First, Last, HeldFirst, StandFirst;
            public bool Held, WentIn, NoRoom, Standing, ReportedHold, ReportedStand, Counted, SampledGo, HeldGap, ReportedHoldGap;
            public uint HeldGapFirst;
            public float BandStart;
        }

        private readonly Dictionary<long, Episode> m_Vehicles = new Dictionary<long, Episode>();
        private readonly List<long> m_Stale = new List<long>();
        private readonly Action<string> m_Log;
        private double m_IntervalStart = double.NaN;
        private int m_Notable, m_Samples;
        private long m_NotableDropped, m_SampleSkipped, m_Lost;
        private long m_Turning, m_Held, m_Go, m_NoRoom, m_StoodNoRoom, m_LongStand, m_LongHold, m_Full, m_HeldGap, m_LongHoldGap;

        public EntranceLogBook(Action<string> log) { m_Log = log; }

        /// <summary>Tracked vehicles now (bounded by MaxVehicles).</summary>
        public int Tracked => m_Vehicles.Count;

        public void AddLost(long lost) { m_Lost += lost; }

        /// <summary>One ring record (main thread).</summary>
        public void Record(long vehicle, long road, long facts, uint frame, double now)
        {
            if (double.IsNaN(m_IntervalStart)) m_IntervalStart = now;
            EntranceLog.Unpack(facts, out EntranceLog.Event e, out bool room, out float speed, out float bandStart);
            if (m_Vehicles.TryGetValue(vehicle, out Episode ep) && (Since(ep.Last, frame) > EpisodeGapFrames || ep.Road != road)) { m_Vehicles.Remove(vehicle); ep = null; }
            if (ep == null)
            {
                if (m_Vehicles.Count >= MaxVehicles) { m_Full++; return; }
                ep = new Episode { Road = road, First = frame, Last = frame, BandStart = bandStart };
                m_Vehicles[vehicle] = ep;
            }
            if (frame > ep.Last) ep.Last = frame;
            if (!ep.Counted) { ep.Counted = true; m_Turning++; }
            switch (e)
            {
                case EntranceLog.Event.Held:
                    if (!ep.Held)
                    {
                        ep.Held = true;
                        ep.HeldFirst = frame;
                        m_Held++;
                        Sample("held before the box (way in full, waits on the road)", vehicle, road, bandStart, speed);
                    }
                    if (!ep.ReportedHold && Since(ep.HeldFirst, frame) >= LongHoldFrames)
                    {
                        ep.ReportedHold = true;
                        m_LongHold++;
                        Notable("held before the box " + Seconds(Since(ep.HeldFirst, frame)) + " (way in still full)", vehicle, road, bandStart, speed);
                    }
                    break;
                case EntranceLog.Event.HeldGap:
                    // 0.4.11 (G21): written after the room test of the same step (its Go record came first): held for the gap.
                    if (!ep.HeldGap)
                    {
                        ep.HeldGap = true;
                        ep.HeldGapFirst = frame;
                        m_HeldGap++;
                        Sample("held before the box (way in has room; waits for a gap in the traffic its turn crosses)", vehicle, road, bandStart, speed);
                    }
                    if (!ep.ReportedHoldGap && Since(ep.HeldGapFirst, frame) >= LongHoldFrames)
                    {
                        ep.ReportedHoldGap = true;
                        m_LongHoldGap++;
                        Notable("held before the box " + Seconds(Since(ep.HeldGapFirst, frame)) + " waiting for a gap in the traffic its turn crosses", vehicle, road, bandStart, speed);
                    }
                    break;
                case EntranceLog.Event.Go:
                    // 0.4.11: the room test passed; a gap hold of the same step may follow (HeldGap), so the sample says only that.
                    if (!ep.WentIn && !ep.Held && !ep.HeldGap && !ep.SampledGo)
                    {
                        ep.SampledGo = true;
                        Sample("way in has room", vehicle, road, bandStart, speed);
                    }
                    break;
                case EntranceLog.Event.InBox:
                    if (!ep.WentIn)
                    {
                        ep.WentIn = true;
                        if (room) m_Go++;
                        else { ep.NoRoom = true; m_NoRoom++; Sample("went into the box WITHOUT room (could not stop before it)", vehicle, road, bandStart, speed); }
                    }
                    if (speed < StandSpeed)
                    {
                        if (!ep.Standing) { ep.Standing = true; ep.StandFirst = frame; }
                        uint stood = Since(ep.StandFirst, frame);
                        if (!ep.ReportedStand && ep.NoRoom && !room && stood >= StandNoRoomFrames)
                        {
                            ep.ReportedStand = true;
                            m_StoodNoRoom++;
                            Notable("entered the box without room and stands in it " + Seconds(stood) + " (way in full)", vehicle, road, bandStart, speed);
                        }
                        else if (!ep.ReportedStand && stood >= LongStandFrames)
                        {
                            ep.ReportedStand = true;
                            m_LongStand++;
                            Notable("stands in the box " + Seconds(stood) + " (way in " + (room ? "has room: waiting to turn" : "full") + ")", vehicle, road, bandStart, speed);
                        }
                    }
                    else ep.Standing = false;
                    break;
            }
        }

        /// <summary>Main thread, each step after the drain: closes the interval (summary line) and forgets old episodes.</summary>
        public void Tick(double now, uint frame)
        {
            if (double.IsNaN(m_IntervalStart)) { m_IntervalStart = now; return; }
            if (now - m_IntervalStart < IntervalSeconds) return;
            bool any = m_Turning + m_Held + m_HeldGap + m_NoRoom + m_Lost + m_Full + m_NotableDropped > 0;
            if (any)
            {
                var ci = CultureInfo.InvariantCulture;
                m_Log("entrance boxes (last " + ((int)Math.Round(now - m_IntervalStart)).ToString(ci) + " s): " + m_Turning + " vehicle(s) turning in at a box, "
                      + m_Held + " held before the box (way in full), " + m_Go + " went in with room, " + m_NoRoom + " went in without room (could not stop), "
                      + m_StoodNoRoom + " stood in the box without room, " + m_LongStand + " stood in the box over 30 s, " + m_LongHold + " held over 30 s"
                      + "; " + m_HeldGap + " held before the box for a gap in the traffic their turn crosses (" + m_LongHoldGap + " over 30 s); lines: "
                      + m_Notable + " notable (" + m_NotableDropped + " over the cap), " + m_Samples + " samples (" + m_SampleSkipped + " skipped); "
                      + m_Lost + " records lost (ring overrun), " + m_Full + " not tracked (book full)");
            }
            m_IntervalStart = now;
            m_Notable = m_Samples = 0;
            m_NotableDropped = m_SampleSkipped = m_Lost = m_Turning = m_Held = m_Go = m_NoRoom = m_StoodNoRoom = m_LongStand = m_LongHold = m_Full = m_HeldGap = m_LongHoldGap = 0;
            m_Stale.Clear();
            foreach (var kv in m_Vehicles) if (Since(kv.Value.Last, frame) > EpisodeGapFrames) m_Stale.Add(kv.Key);
            foreach (long k in m_Stale) m_Vehicles.Remove(k);
        }

        private void Notable(string what, long vehicle, long road, float bandStart, float speed)
        {
            if (m_Notable >= MaxNotable) { m_NotableDropped++; return; }
            m_Notable++;
            m_Log("entrance box: vehicle " + EntranceLog.Ent(vehicle) + " " + what + "; road " + EntranceLog.Ent(road) + " box from t "
                  + bandStart.ToString("0.000", CultureInfo.InvariantCulture) + ", speed " + speed.ToString("0.0", CultureInfo.InvariantCulture) + " m/s");
        }

        private void Sample(string what, long vehicle, long road, float bandStart, float speed)
        {
            if (m_Samples >= MaxSamples) { m_SampleSkipped++; return; }
            m_Samples++;
            m_Log("entrance box sample: vehicle " + EntranceLog.Ent(vehicle) + " " + what + "; road " + EntranceLog.Ent(road) + " box from t "
                  + bandStart.ToString("0.000", CultureInfo.InvariantCulture) + ", speed " + speed.ToString("0.0", CultureInfo.InvariantCulture) + " m/s");
        }

        /// <summary>Frames from 'from' to 'to'; 0 when 'to' is earlier (records of one step may arrive out of order).</summary>
        private static uint Since(uint from, uint to) => to >= from ? to - from : 0u;

        private static string Seconds(uint frames) =>
            (frames / (double)FramesPerSecond).ToString("0", CultureInfo.InvariantCulture) + " s";
    }
}
