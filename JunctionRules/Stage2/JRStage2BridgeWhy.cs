using JunctionRules.Core;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace JunctionRules.Stage2
{
    /// <summary>
    /// JR-T-025 (1.1.1), main thread of "why is this car slowing" (Core/WhySlowed.cs, job side Stage2/JRWhy.cs). Each copy
    /// step, before the job is scheduled: pick the car to track (the game's selected vehicle wins, else the API's car,
    /// WhyHandoff), hand it to the job (S2-23), read the slot the job wrote for it (seqlock) and feed the book (history,
    /// log lines on a tracking change and on each reason change, rate-limited). Nothing tracked: one selection read per step.
    /// </summary>
    public static partial class JRStage2Bridge
    {
        private static NativeArray<long> s_WhySlot;
        private static NativeArray<WhyTrack> s_WhyScratch;
        private static Game.UI.InGame.SelectedInfoUISystem s_Selected;
        private static long s_WhySeenSeq;
        private static int s_WhyGeneration;

        /// <summary>The car the job records (Entity.Null = none; S2-23 hands it to the job).</summary>
        public static Entity WhyEntity { get; private set; }

        /// <summary>The one shared slot (always created, so the job struct never holds an unallocated container).</summary>
        public static NativeArray<long> WhySlot => s_WhySlot;

        /// <summary>The tracked car's step record (job scratch, one element; only that car's worker touches it).</summary>
        public static NativeArray<WhyTrack> WhyScratch => s_WhyScratch;

        private unsafe struct ReadStore : IWhySlotStore
        {
            public long* p;
            public long Read(int i) => System.Threading.Interlocked.Read(ref p[i]);
            public void Write(int i, long v) { }
            public void WriteSeq(long v) { }
        }

        private static unsafe void UpdateWhy()
        {
            if (!s_WhySlot.IsCreated) s_WhySlot = new NativeArray<long>(Core.WhySlot.Length, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            if (!s_WhyScratch.IsCreated) s_WhyScratch = new NativeArray<WhyTrack>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            if (s_WhyGeneration != WhyHandoff.Generation)
            {
                // JR-T-025b (M1): a city load reset the readout; the slot's last sample is of the old city (its car key may be
                // reused by a new car), so mark it seen. The job of the previous step has completed (load / before scheduling).
                s_WhyGeneration = WhyHandoff.Generation;
                s_WhySeenSeq = SlotSeqSeen();
            }
            WhyHandoff.Steps++;
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) { WhyEntity = Entity.Null; return; }
            EntityManager em = world.EntityManager;
            long selected = 0;
            try
            {
                if (s_Selected == null) s_Selected = world.GetExistingSystemManaged<Game.UI.InGame.SelectedInfoUISystem>();
                Entity sel = s_Selected != null ? s_Selected.selectedEntity : Entity.Null;
                if (sel != Entity.Null && em.Exists(sel) && em.HasComponent<Game.Vehicles.CarNavigation>(sel)) selected = JRWhy.Key(sel);
            }
            catch (System.Exception) { selected = 0; }
            long car = WhyHandoff.Choose(selected, WhyHandoff.ApiCar, out string source);
            WhyBook book = WhyHandoff.Book;
            string line = book.Track(car, source);
            if (line != null) Mod.log.Info(line);
            WhyEntity = car == 0 ? Entity.Null : new Entity { Index = JunctionWatch.KeyIndex(car), Version = JunctionWatch.KeyVersion(car) };
            WhyHandoff.TrackedExists = car != 0 && em.Exists(WhyEntity) && em.HasComponent<Game.Vehicles.CarNavigation>(WhyEntity);
            if (car == 0) return;

            ReadStore st = default;
            st.p = (long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(s_WhySlot);
            if (!Core.WhySlot.TryRead(ref st, out WhySample s) || s.Seq == s_WhySeenSeq || s.Car != car) return;
            s_WhySeenSeq = s.Seq;
            Describe(em, ref s);
            double now = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
            string why = book.Observe(s, now);
            if (why != null) Mod.log.Info(why);
        }

        /// <summary>What the blocker is and where (main thread, read-only).</summary>
        private static void Describe(EntityManager em, ref WhySample s)
        {
            s.BlockerKind = WhyKind.None;
            s.BlockerX = s.BlockerZ = float.NaN;
            if (s.Blocker == 0) return;
            try
            {
                var e = new Entity { Index = JunctionWatch.KeyIndex(s.Blocker), Version = JunctionWatch.KeyVersion(s.Blocker) };
                if (!em.Exists(e)) { s.BlockerKind = WhyKind.Gone; return; }
                if (em.HasComponent<Game.Vehicles.Bicycle>(e)) s.BlockerKind = WhyKind.Cyclist;
                else if (em.HasComponent<Game.Creatures.Creature>(e)) s.BlockerKind = WhyKind.Person;
                else if (em.HasComponent<Game.Vehicles.Vehicle>(e)) s.BlockerKind = WhyKind.Vehicle;
                else s.BlockerKind = WhyKind.Other;
                if (em.HasComponent<Game.Objects.Transform>(e))
                {
                    var p = em.GetComponentData<Game.Objects.Transform>(e).m_Position;
                    s.BlockerX = p.x;
                    s.BlockerZ = p.z;
                }
            }
            catch (System.Exception) { s.BlockerKind = WhyKind.Other; }
        }

        /// <summary>The slot's sequence as "seen": the last complete sample (an odd = in-progress write counts as the one it completes).</summary>
        private static unsafe long SlotSeqSeen()
        {
            long* p = (long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(s_WhySlot);
            long seq = System.Threading.Interlocked.Read(ref p[Core.WhySlot.Seq]);
            return (seq & 1) != 0 ? seq + 1 : seq;
        }

        private static void ReleaseWhy()
        {
            if (s_WhySlot.IsCreated) s_WhySlot.Dispose();
            s_WhySlot = default;
            if (s_WhyScratch.IsCreated) s_WhyScratch.Dispose();
            s_WhyScratch = default;
            s_Selected = null;
            s_WhySeenSeq = 0;
            WhyEntity = Entity.Null;
            WhyHandoff.Reset();   // JR-T-025b (M1): nothing of this city survives the copy's release
            s_WhyGeneration = WhyHandoff.Generation;
        }
    }
}
