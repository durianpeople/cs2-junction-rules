using Game;
using Game.Simulation;
using JunctionRules.Core;
using UnityEngine.Scripting;

namespace JunctionRules.Systems
{
    /// <summary>
    /// Chooses which car-driving pair runs: the game's CarNavigationSystem + Actions or the copy
    /// JRCarNavigationSystem + Actions. Registered "before CarNavigationSystem" in GameSimulation and LoadSimulation,
    /// so it runs ahead of both pairs in every simulation step and writes all four Enabled flags at once: a step runs
    /// one whole pair, never both, never neither, and never a navigation system without its own Actions system.
    /// Each pair creates its queues in its navigation update and its Actions system consumes and disposes them in the
    /// same step, so nothing is left over across a switch; component data dependencies are tracked by the ECS for both.
    /// Also switches the timing probes on or off with the debug setting (off = the probes do not run at all).
    /// When the version gate did not allow the copy, the copy pair and the probes are not registered at all: this system
    /// then only keeps the game's pair on. It never names a copy type itself (fields are ComponentSystemBase, creation
    /// is in CopyPair.Create), so it loads even when the copy's types cannot.
    /// The copy systems are got lazily, in the first update that sees Mod.CopyRegistered (attempted once, see
    /// CopyAcquisition), not in OnCreate: UpdateSystem.UpdateBefore creates this system, and so runs OnCreate, at
    /// registration time, which must not depend on the order of the registrations in Mod.OnLoad.
    /// </summary>
    public class CarDrivingSwitchSystem : GameSystemBase
    {
        private CarNavigationSystem m_GameNavigation;
        private CarNavigationSystem.Actions m_GameActions;
        /// <summary>Null unless <see cref="m_Acquisition"/> is Available.</summary>
        private CopySystems m_Copy;
        private readonly CopyAcquisition m_Acquisition = new CopyAcquisition();
        private readonly DrivingSwitch m_Switch = new DrivingSwitch();
        private readonly GapRuleSwitch m_GapRule = new GapRuleSwitch();
        private Unity.Entities.EntityQuery m_JunctionBoxes;
        private Unity.Entities.EntityQuery m_EntranceBoxes;
        private Unity.Entities.EntityQuery m_EntranceBuildings;
        private int m_BoxEdgeCount = -1;
        private int m_BoxEdgeAge;
        private int m_BoxStoreVersion = -1;
        private int m_LastDropped;
        private bool m_ProbesOn;
        private bool m_ProbesApplied;

        /// <summary>What actually runs now (for the settings status line).</summary>
        public static CarDrivingMode Running { get; private set; }

        /// <summary>Whether the switch holds the copy (for the settings status line).</summary>
        public static CopyState CopyState { get; private set; } = CopyState.NotCheckedYet;

        /// <summary>
        /// 0.4.2 (JR-T-010-r, review item 1) / 0.4.3: rebuilds the boxed car exits for the driving rule (Stage2/JRBoxEdges.cs)
        /// on the main thread when the boxes change (count of boxed buildings or Boxes/BoxStore.Version: an exit added to a
        /// building that already had one), and every BoxEdgeRefreshSteps steps (a drive re-wired to a rebuilt road keeps
        /// its box but gets a new road edge). Every saved exit key becomes its own band at that exit (Boxes/BoxFacts
        /// .BoxedExitBands); no one-box-per-building assumption. Tiny: a few buildings.
        /// </summary>
        private void UpdateBoxEdges(bool any)
        {
            if (!any)
            {
                if (m_BoxEdgeCount != 0) BoxEdgeHandoff.Publish(new ExitBand[0]);
                m_BoxEdgeCount = 0;
                m_LastDropped = 0;
                return;
            }
            int count = m_EntranceBuildings.CalculateEntityCount();
            int version = JunctionRules.Boxes.BoxStore.Version;
            if (count == m_BoxEdgeCount && version == m_BoxStoreVersion && ++m_BoxEdgeAge < BoxEdgeRefreshSteps) return;
            m_BoxEdgeCount = count;
            m_BoxStoreVersion = version;
            m_BoxEdgeAge = 0;
            var bands = new System.Collections.Generic.List<ExitBand>();
            int unmatched = 0;
            using (var buildings = m_EntranceBuildings.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < buildings.Length; i++) unmatched += JunctionRules.Boxes.BoxFacts.BoxedExitBands(EntityManager, buildings[i], bands);
            }
            int before = BoxEdgeHandoff.Version;
            BoxEdgeHandoff.Publish(bands);
            int dropped = BoxEdgeHandoff.Dropped;
            if (BoxEdgeHandoff.Version != before)
            {
                Mod.log.Info($"yellow boxes (driving): {BoxEdgeHandoff.Keys.Length} boxed car exit(s) on {count} building(s) handed to the box rule"
                    + (unmatched > 0 ? $"; {unmatched} saved exit key(s) without an exit now (not obeyed until the paint re-keys or drops them)" : ""));
            }
            // 0.4.3 review F2: the kept bands can stay the same (no new version) while the number over the limit changes
            // (a 64th exit sorting last), so the over-limit count is logged on its own change.
            if (dropped != m_LastDropped)
            {
                if (dropped > 0) Mod.log.Warn($"yellow boxes (driving): {dropped} boxed car exit(s) over the limit of {ExitBands.Capacity} are NOT obeyed ({bands.Count} found)");
                else if (m_LastDropped > 0) Mod.log.Info($"yellow boxes (driving): back within the limit of {ExitBands.Capacity} boxed car exits; all obeyed");
                m_LastDropped = dropped;
            }
        }

        /// <summary>Steps between rebuilds of the boxed edge set while the box count is unchanged.</summary>
        private const int BoxEdgeRefreshSteps = 64;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_GameNavigation = World.GetOrCreateSystemManaged<CarNavigationSystem>();
            m_GameActions = World.GetOrCreateSystemManaged<CarNavigationSystem.Actions>();
            // 0.4.2: the saved yellow boxes (0.4.0 components), only to tell the copy which kinds exist.
            m_JunctionBoxes = GetEntityQuery(Unity.Entities.ComponentType.ReadOnly<JunctionRules.Boxes.YellowBoxJunction>());
            m_EntranceBoxes = GetEntityQuery(Unity.Entities.ComponentType.ReadOnly<JunctionRules.Boxes.YellowBoxEntrance>());
            m_EntranceBuildings = GetEntityQuery(Unity.Entities.ComponentType.ReadOnly<JunctionRules.Boxes.YellowBoxEntrance>(),
                Unity.Entities.ComponentType.ReadOnly<Game.Buildings.Building>());
            // Nothing of the copy here: it may not be registered yet (see the class summary). Until the first update,
            // the game's pair keeps its default (on) and the copy and the probes stay off (CopyPair.Register's finally).
        }

        /// <summary>
        /// Gets the copy systems once, in the first update that sees the copy registered. On failure everything of the
        /// copy that exists is switched off and the switch stays on the game's own pair for the session.
        /// </summary>
        private void AcquireCopyOnce()
        {
            if (!m_Acquisition.ShouldAttempt(Mod.CopyRegistered))
            {
                CopyState = m_Acquisition.State;
                return;
            }
            try
            {
                m_Copy = CopyPair.Create(World);
                m_Acquisition.Succeeded();
            }
            catch (System.Exception e)
            {
                m_Copy = null;
                m_Acquisition.Failed();
                Mod.log.Error(e, "getting the Junction Rules copy systems failed; car driving stays with the game's own system");
                // Registered systems exist; with m_Copy null this switch never touches them again, so make sure
                // none of them is left on.
                Mod.DisableCopyLeftovers(World, "after the failed copy creation");
            }
            CopyState = m_Acquisition.State;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            // First, in the same update and before any Enabled flag is written: the copy is either held (all four of
            // its systems known, off) or absent (closed gate below, never enabled).
            AcquireCopyOnce();
            CarDrivingMode requested = Mod.Settings != null ? Mod.Settings.CarDriving : CarDrivingMode.GameOwn;
            GateResult gate = DrivingSwitch.GateFor(Mod.Gate, m_Copy != null);
            if (m_Switch.Step(requested, gate, out PairFlags flags, out string note))
            {
                // Order does not matter inside this update (none of the four is running now); all four are written
                // before either navigation slot is reached in this step.
                m_GameNavigation.Enabled = flags.GameNavigation;
                m_GameActions.Enabled = flags.GameActions;
                if (m_Copy != null)
                {
                    m_Copy.Navigation.Enabled = flags.CopyNavigation;
                    m_Copy.Actions.Enabled = flags.CopyActions;
                }
                Running = m_Switch.Applied;
            }
            if (note != null) Mod.log.Info(note);

            if (m_Copy == null) return;
            bool probes = Mod.Settings != null && Mod.Settings.DebugTiming;
            // Stage 2: publish the junction gap rule for this step (the copy's OnUpdate, later in this step, takes it by
            // value into its job). Counters run only with the timing log on.
            string ruleNote = m_GapRule.Step(Mod.Settings != null ? Mod.Settings.GapRule : JunctionGapRule.Stock, probes, out int stage2Mode,
                Mod.Settings != null ? Mod.Settings.WaitingSpot : JunctionWaitingSpot.BeforeFirstCrossing,
                Mod.Settings == null || Mod.Settings.TurnAcrossGivesWay, LeftHandTraffic(),
                Mod.Settings == null || Mod.Settings.NoPushThroughPeople,
                Mod.Settings == null || Mod.Settings.ZebraNonBlocking,
                Mod.Settings != null ? Mod.Settings.ZebraRoomMargin : RoomScan.DefaultMargin,
                // 0.4.2 (JR-T-010): cars obey yellow boxes; which box kinds exist (the job skips a kind with none).
                Mod.Settings == null || Mod.Settings.BoxObey,
                Mod.Settings == null || Mod.Settings.BoxTurnWait,
                !m_JunctionBoxes.IsEmptyIgnoreFilter,
                !m_EntranceBoxes.IsEmptyIgnoreFilter,
                (float)JunctionRules.Boxes.BoxStore.EntranceLength,
                // 0.4.4 (JR-T-012): vehicles leaving buildings give way.
                Mod.Settings == null || Mod.Settings.ExitGiveWay);
            Stage2Handoff.Publish(stage2Mode);
            // 0.4.7 (G1): the per-vehicle exit trace (logged by the copy side, rate-limited).
            ExitTraceHandoff.Publish(Mod.Settings != null && Mod.Settings.ExitTrace);
            UpdateBoxEdges((stage2Mode & Stage2Mode.BoxEntrances) != 0);
            if (ruleNote != null) Mod.log.Info(ruleNote);
            if (!m_ProbesApplied || probes != m_ProbesOn)
            {
                m_ProbesApplied = true;
                m_ProbesOn = probes;
                m_Copy.GameProbe.Enabled = probes;
                m_Copy.CopyProbe.Enabled = probes;
                Mod.log.Info($"navigation timing log {(probes ? "on" : "off")}");
            }
        }

        /// <summary>0.2.4: the game's own traffic side (CityConfigurationSystem.leftHandTraffic, as LaneOverlapSystem reads it).</summary>
        private bool LeftHandTraffic()
        {
            if (m_CityConfiguration == null) m_CityConfiguration = World.GetOrCreateSystemManaged<Game.City.CityConfigurationSystem>();
            return m_CityConfiguration.leftHandTraffic;
        }

        private Game.City.CityConfigurationSystem m_CityConfiguration;

        [Preserve]
        public CarDrivingSwitchSystem() { }
    }
}
