using System.Collections.Generic;
using System.Diagnostics;
using Game;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;
using JunctionRules.Core;
using JunctionRules.Stage2;
using JunctionRules.Vanilla;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.Scripting;

namespace JunctionRules.Systems
{
    /// <summary>
    /// Debug-only timing of one car-navigation job, the same way for the game's and the copy's. Each probe runs right
    /// after its navigation system and before that system's Actions (registered "before Actions"), only in steps where
    /// the navigation system ran. It takes the main-thread time just after the job was scheduled and schedules a tiny
    /// marker job on the navigation job's handle (Actions.m_Dependency, public on both pairs) that stamps the time the
    /// navigation job finished. The difference ("schedule-to-done") includes waiting for upstream jobs, which is the
    /// same for both pairs because both sit in the same slot with the same dependencies. Nothing waits on the marker
    /// job: results are read once it has finished, in a later step. When the debug setting is off the switch system
    /// keeps these systems disabled, so they cost nothing.
    /// </summary>
    public abstract class NavTimingProbeSystem : GameSystemBase
    {
        public const uint kWindowFrames = 1024;
        private const int kSlots = 16;

        private struct StampJob : IJob
        {
            [NativeDisableContainerSafetyRestriction] public NativeArray<long> m_Stamps;
            public int m_Index;

            public void Execute() => m_Stamps[m_Index] = Stopwatch.GetTimestamp();
        }

        private struct Pending
        {
            public int Slot;
            public long ScheduledTicks;
            public JobHandle Handle;
        }

        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_CarQuery;
        private NativeArray<long> m_Stamps;
        private readonly bool[] m_Busy = new bool[kSlots];
        private readonly Queue<Pending> m_Pending = new Queue<Pending>(kSlots);
        private TimingWindow m_Window;
        private uint m_LastNavigationVersion;

        protected abstract string Label { get; }
        protected abstract ComponentSystemBase Navigation { get; }
        protected abstract JobHandle NavigationJob { get; }

        /// <summary>Stage 2: a navigation run of this pair was seen this step (main thread).</summary>
        protected virtual void OnNavigationRun() { }

        /// <summary>Stage 2: a window starts (also when the probe starts running).</summary>
        protected virtual void OpenWindow() { }

        /// <summary>Stage 2: the window closes; returns the suffix of its timing line and starts the next window.</summary>
        protected virtual string CloseWindow() => null;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_CarQuery = GetEntityQuery(ComponentType.ReadOnly<Car>(), ComponentType.ReadOnly<CarCurrentLane>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<ParkedCar>());
            m_Stamps = new NativeArray<long>(kSlots, Allocator.Persistent);
            m_Window = new TimingWindow(Label, kWindowFrames);
        }

        [Preserve]
        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_LastNavigationVersion = Navigation.LastSystemVersion;
            m_Window.Restart(m_SimulationSystem.frameIndex, Stopwatch.GetTimestamp());
            OpenWindow();
            m_PassHasStart = PeoplePassSystem.Read(m_PassStart);
        }

        [Preserve]
        protected override void OnStopRunning()
        {
            DrainAll();
            base.OnStopRunning();
        }

        [Preserve]
        protected override void OnDestroy()
        {
            DrainAll();
            if (m_Stamps.IsCreated) m_Stamps.Dispose();
            base.OnDestroy();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            Collect();

            uint version = Navigation.LastSystemVersion;
            if (version != m_LastNavigationVersion)
            {
                m_LastNavigationVersion = version;
                OnNavigationRun();
                int slot = FreeSlot();
                if (slot < 0)
                {
                    m_Window.Skip();
                }
                else
                {
                    long scheduled = Stopwatch.GetTimestamp();
                    var job = new StampJob { m_Stamps = m_Stamps, m_Index = slot };
                    m_Busy[slot] = true;
                    m_Pending.Enqueue(new Pending { Slot = slot, ScheduledTicks = scheduled, Handle = job.Schedule(NavigationJob) });
                }
            }

            uint frame = m_SimulationSystem.frameIndex;
            if (m_Window.Due(frame))
            {
                int cars = m_Window.Count > 0 ? m_CarQuery.CalculateEntityCountWithoutFiltering() : 0;
                string line = m_Window.Tick(frame, Stopwatch.GetTimestamp(), Stopwatch.Frequency, cars, CloseWindow() + ClosePassWindow());
                if (line != null) Mod.log.Info(line);
            }
        }

        // 0.3.6 (JR-T-008): people / cyclists who passed a vehicle standing on their crossing, deltas over the window.
        private readonly long[] m_PassStart = new long[PeoplePassSystem.CountSlots];
        private readonly long[] m_PassNow = new long[PeoplePassSystem.CountSlots];
        private bool m_PassHasStart;

        private string ClosePassWindow()
        {
            string part = null;
            if (m_PassHasStart && PeoplePassSystem.Read(m_PassNow))
            {
                part = PeoplePassCounts.Format(m_PassNow[PeoplePassSystem.CountPeople] - m_PassStart[PeoplePassSystem.CountPeople],
                    m_PassNow[PeoplePassSystem.CountCyclists] - m_PassStart[PeoplePassSystem.CountCyclists]);
            }
            m_PassHasStart = PeoplePassSystem.Read(m_PassStart);
            return part ?? PeoplePassCounts.Format(-1, -1);
        }

        private int FreeSlot()
        {
            for (int i = 0; i < kSlots; i++)
            {
                if (!m_Busy[i]) return i;
            }
            return -1;
        }

        /// <summary>Reads finished markers (in order) without waiting for unfinished ones.</summary>
        private void Collect()
        {
            while (m_Pending.Count > 0 && m_Pending.Peek().Handle.IsCompleted)
            {
                Pending p = m_Pending.Dequeue();
                p.Handle.Complete();
                m_Window.Add((m_Stamps[p.Slot] - p.ScheduledTicks) * 1000.0 / Stopwatch.Frequency);
                m_Busy[p.Slot] = false;
            }
        }

        /// <summary>On disable/destroy only: waits for outstanding markers so the stamp array can be reused or freed.</summary>
        private void DrainAll()
        {
            while (m_Pending.Count > 0)
            {
                Pending p = m_Pending.Dequeue();
                p.Handle.Complete();
                m_Busy[p.Slot] = false;
            }
        }
    }

    /// <summary>Times the game's own navigation job (runs only while the game's pair is enabled).</summary>
    public class GameNavTimingProbeSystem : NavTimingProbeSystem
    {
        private CarNavigationSystem m_Navigation;
        private CarNavigationSystem.Actions m_Actions;

        protected override string Label => "game's own CarNavigationSystem";
        protected override ComponentSystemBase Navigation => m_Navigation ??= World.GetOrCreateSystemManaged<CarNavigationSystem>();
        protected override JobHandle NavigationJob => (m_Actions ??= World.GetOrCreateSystemManaged<CarNavigationSystem.Actions>()).m_Dependency;

        protected override string CloseWindow() => "; junction gap rule game's own (stock)";

        [Preserve]
        public GameNavTimingProbeSystem() { }
    }

    /// <summary>Times the copy's navigation job (runs only while the copy's pair is enabled).</summary>
    public class CopyNavTimingProbeSystem : NavTimingProbeSystem
    {
        private JRCarNavigationSystem m_Navigation;
        private JRCarNavigationSystem.Actions m_Actions;

        protected override string Label => "Junction Rules copy";
        protected override ComponentSystemBase Navigation => m_Navigation ??= World.GetOrCreateSystemManaged<JRCarNavigationSystem>();
        protected override JobHandle NavigationJob => (m_Actions ??= World.GetOrCreateSystemManaged<JRCarNavigationSystem.Actions>()).m_Dependency;

        // Stage 2: which rule the copy's steps in this window ran with, and the counter deltas over the window.
        private int m_RuleMask;
        private int m_PushMask;
        private int m_ZebraMask;
        private int m_BoxMask;
        private int m_ExitMask;
        private int m_LookPastMask;   // JR-T-022c: "Look past the car in front at a box" (same encoding)
        private bool m_CountedWhole;
        private readonly long[] m_Totals = new long[Stage2Counters.Count];
        private readonly long[] m_Start = new long[Stage2Counters.Count];
        private bool m_HasStart;
        // JR-T-014d (G11) BEGIN: unmarked-crossing counters (always on, Systems/UnmarkedTrace.cs) at the window start.
        private long m_UStartNotHeld, m_UStartPeople, m_UStartFloor;
        private bool m_UHasStart;
        // JR-T-014d (G11) END

        protected override void OpenWindow()
        {
            // JR-T-014d (G11) BEGIN
            m_UHasStart = UnmarkedTrace.Ready;
            if (m_UHasStart)
            {
                m_UStartNotHeld = UnmarkedTrace.Read(UnmarkedTraceLayout.CarStepsNotHeld);
                m_UStartPeople = UnmarkedTrace.Read(UnmarkedTraceLayout.PeopleHeld);
                m_UStartFloor = UnmarkedTrace.Read(UnmarkedTraceLayout.FloorStops);
            }
            // JR-T-014d (G11) END
            m_RuleMask = 0;
            m_PushMask = 0;
            m_ZebraMask = 0;
            m_BoxMask = 0;
            m_ExitMask = 0;
            m_LookPastMask = 0;
            m_CountedWhole = true;
            m_HasStart = JRStage2Bridge.Read(m_Start);
        }

        protected override void OnNavigationRun()
        {
            int mode = JRStage2Bridge.AppliedMode;
            m_RuleMask |= (mode & Stage2Mode.Rule) != 0 ? 2 : 1;
            m_PushMask |= (mode & Stage2Mode.NoPushPeople) != 0 ? 2 : 1;
            m_ZebraMask |= (mode & Stage2Mode.ZebraClear) != 0 ? 2 : 1;
            m_BoxMask |= (mode & Stage2Mode.BoxObey) != 0 ? 2 : 1;
            m_ExitMask |= (mode & Stage2Mode.ExitGiveWay) != 0 ? 2 : 1;
            m_LookPastMask |= (mode & Stage2Mode.BoxLookPast) != 0 ? 2 : 1;
            if ((mode & Stage2Mode.Count) == 0) m_CountedWhole = false;
        }

        protected override string CloseWindow()
        {
            long[] deltas = null;
            if (m_HasStart && m_CountedWhole && JRStage2Bridge.Read(m_Totals))
            {
                deltas = new long[Stage2Counters.Count];
                for (int i = 0; i < deltas.Length; i++) deltas[i] = m_Totals[i] - m_Start[i];
            }
            string suffix = Stage2Counters.Format(m_RuleMask, deltas, m_PushMask, m_ZebraMask, m_BoxMask, m_ExitMask, m_LookPastMask);
            // JR-T-014d (G11) BEGIN
            bool jrU = m_UHasStart && UnmarkedTrace.Ready;
            suffix += UnmarkedCrossingCounts.Format(jrU ? UnmarkedTrace.Read(UnmarkedTraceLayout.CarStepsNotHeld) - m_UStartNotHeld : -1,
                jrU ? UnmarkedTrace.Read(UnmarkedTraceLayout.PeopleHeld) - m_UStartPeople : -1,
                jrU ? UnmarkedTrace.Read(UnmarkedTraceLayout.FloorStops) - m_UStartFloor : -1);
            // JR-T-014d (G11) END
            OpenWindow();
            return suffix;
        }

        [Preserve]
        public CopyNavTimingProbeSystem() { }
    }
}
