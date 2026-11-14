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
        private bool m_ProbesOn;
        private bool m_ProbesApplied;

        /// <summary>What actually runs now (for the settings status line).</summary>
        public static CarDrivingMode Running { get; private set; }

        /// <summary>Whether the switch holds the copy (for the settings status line).</summary>
        public static CopyState CopyState { get; private set; } = CopyState.NotCheckedYet;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_GameNavigation = World.GetOrCreateSystemManaged<CarNavigationSystem>();
            m_GameActions = World.GetOrCreateSystemManaged<CarNavigationSystem.Actions>();
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
                Mod.Settings != null ? Mod.Settings.WaitingSpot : JunctionWaitingSpot.BeforeFirstCrossing);
            Stage2Handoff.Publish(stage2Mode);
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

        [Preserve]
        public CarDrivingSwitchSystem() { }
    }
}
