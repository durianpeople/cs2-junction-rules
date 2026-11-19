using System.Threading;
using Game.Vehicles;
using JunctionRules.Core;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace JunctionRules.Stage2
{
    /// <summary>
    /// JR-T-025 (1.1.1), job side of "why is this car slowing" (Core/WhySlowed.cs). Only the ONE tracked car (the game's
    /// selected vehicle, else the API's car; Entity.Null = none) records anything: the copy's blocks test the per-car flag
    /// (S2-21: a local from one entity compare per car; the iterator's m_WhyOn). No driving effect: nothing here changes a speed, blocker
    /// or lane. Burst-safe (value types, a pointer store for the shared slot).
    /// </summary>
    public static class JRWhy
    {
        public static long Key(Entity e) => JunctionWatch.Key(e.Index, e.Version);

        /// <summary>An untagged lowering of the iterator's limit since the last tagged point: classified from the game's blocker record.</summary>
        public static void Settle(ref WhyTrack t, float maxSpeed, Entity blocker, BlockerType type, Entity lane) =>
            t.Settle(maxSpeed, (int)type, Key(blocker), Key(lane));

        /// <summary>The shared slot through its pointer: plain writes, the sequence with Interlocked (a full barrier).</summary>
        private unsafe struct PtrStore : IWhySlotStore
        {
            public long* p;
            public long Read(int i) => p[i];
            public void Write(int i, long v) => p[i] = v;
            public void WriteSeq(long v) => Interlocked.Exchange(ref p[WhySlot.Seq], v);
        }

        /// <summary>
        /// The tracked car's step record (the copy's per-car scratch, one element). Only the tracked car's worker touches it
        /// (the job runs each entity once a step), so the parallel job never writes a shared job field per car.
        /// </summary>
        public static unsafe ref WhyTrack Ref(NativeArray<WhyTrack> scratch) =>
            ref UnsafeUtility.ArrayElementAsRef<WhyTrack>(NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(scratch), 0);

        /// <summary>
        /// End of the tracked car's step (called right after UpdateNavigationTarget for that car only): resolve the record
        /// against the final limit and write the slot (seqlock). The job runs one car per entity, so one writer.
        /// </summary>
        public static unsafe void Write(NativeArray<long> slot, NativeArray<WhyTrack> scratch, float finalLimit, uint frame, Entity car,
            float speed, float x, float z, Entity gameBlocker, BlockerType gameType)
        {
            if (!slot.IsCreated || slot.Length < WhySlot.Length || !scratch.IsCreated || scratch.Length < 1) return;
            ref WhyTrack t = ref Ref(scratch);
            t.Finish(finalLimit, t.BeforeSteer);
            PtrStore s = default;
            s.p = (long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(slot);
            WhySlot.Write(ref s, frame, Key(car), in t.Bound, speed, x, z, finalLimit, Key(gameBlocker), (int)gameType);
        }
    }

    /// <summary>
    /// JR-T-025: the Junction Rules hooks name the rule of the hold they return (read by the copy's hold blocks S2-25,
    /// S2-28, S2-28b, S2-29, S2-37 and the exit holds S2-30, S2-32, S2-35, S2-36). Written only for the tracked car
    /// (m_WhyOn); cleared with the G17 hold blocker at the start of every hook (BeginHoldBlocker).
    /// </summary>
    public partial struct JRWholeJunction
    {
        /// <summary>S2-21: this car is the tracked one (the only car that records why it slows).</summary>
        public bool m_WhyOn;
        private byte m_WhyNote;
        private float m_WhyNoteDist, m_WhyNoteExtra;
        /// <summary>The last failed room scan of this hook: room short by (m) and the look-past refusal code.</summary>
        private float m_WhyRoomShort;
        private byte m_WhyRefusal;

        /// <summary>The rule the hook named for its hold, else <paramref name="fallback"/> (the hook's own family).</summary>
        public WhyRule WhyRuleOr(WhyRule fallback) => WhyRules.Hook(m_WhyNote, fallback);
        public float WhyDistance => m_WhyNote != 0 ? m_WhyNoteDist : float.NaN;
        public float WhyExtra => m_WhyNote != 0 ? m_WhyNoteExtra : (m_WhyRoomShort > 0f ? m_WhyRoomShort : float.NaN);

        private void WhyReset()
        {
            m_WhyNote = 0;
            m_WhyNoteDist = float.NaN;
            m_WhyNoteExtra = float.NaN;
            m_WhyRoomShort = float.NaN;
            m_WhyRefusal = 0;
        }

        private void WhyNote(WhyRule rule, float distance, float extra)
        {
            if (!m_WhyOn) return;
            m_WhyNote = (byte)rule;
            m_WhyNoteDist = distance;
            m_WhyNoteExtra = extra;
        }

        /// <summary>A room hold: the look-past refusal (extra = its code) when the look-past refused, else <paramref name="room"/> (extra = room short by, m).</summary>
        private void WhyNoteRoom(WhyRule room, float distance)
        {
            if (!m_WhyOn) return;
            if (m_WhyRefusal != 0) WhyNote(WhyRule.JrBoxLookPastRefused, distance, m_WhyRefusal);
            else WhyNote(room, distance, m_WhyRoomShort);
        }

        /// <summary>NoteRoomBlocker (a scan without room): keep its shortfall and look-past refusal.</summary>
        private void WhyNoteScan(in RoomScan scan)
        {
            if (!m_WhyOn) return;
            float min = scan.MinRoomNow;
            m_WhyRoomShort = min < float.MaxValue && scan.Needed > min ? scan.Needed - min : float.NaN;
            m_WhyRefusal = scan.Refusal;
        }

        /// <summary>An exit / drive hold: gives way (gap) or no room on the lane it joins / past its box.</summary>
        private void WhyNoteExit(ExitGiveWay.Decision d)
        {
            if (d == ExitGiveWay.Decision.WaitRoom) WhyNote(WhyRule.JrExitBoxRoom, float.NaN, m_WhyRoomShort);
            else WhyNote(WhyRule.JrExitGiveWay, float.NaN, float.NaN);
        }

        /// <summary>The safety re-check phase of the current overlap (S2-06 / S2-07 / S2-11b: gap vs re-check).</summary>
        public bool WhySafetyPhase => m_OvPhase == PointPhase.Safety;
    }
}
