using Game;
using Game.Simulation;
using JunctionRules.Core;
using JunctionRules.Vanilla;
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
    /// </summary>
    public class CarDrivingSwitchSystem : GameSystemBase
    {
        private CarNavigationSystem m_GameNavigation;
        private CarNavigationSystem.Actions m_GameActions;
        private JRCarNavigationSystem m_CopyNavigation;
        private JRCarNavigationSystem.Actions m_CopyActions;
        private GameNavTimingProbeSystem m_GameProbe;
        private CopyNavTimingProbeSystem m_CopyProbe;
        private readonly DrivingSwitch m_Switch = new DrivingSwitch();
        private bool m_ProbesOn;
        private bool m_ProbesApplied;

        /// <summary>What actually runs now (for the settings status line).</summary>
        public static CarDrivingMode Running { get; private set; }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_GameNavigation = World.GetOrCreateSystemManaged<CarNavigationSystem>();
            m_GameActions = World.GetOrCreateSystemManaged<CarNavigationSystem.Actions>();
            m_CopyNavigation = World.GetOrCreateSystemManaged<JRCarNavigationSystem>();
            m_CopyActions = World.GetOrCreateSystemManaged<JRCarNavigationSystem.Actions>();
            m_GameProbe = World.GetOrCreateSystemManaged<GameNavTimingProbeSystem>();
            m_CopyProbe = World.GetOrCreateSystemManaged<CopyNavTimingProbeSystem>();
            // Until the first step decides, the copy and the probes stay off (the game's pair keeps its default: on).
            m_CopyNavigation.Enabled = false;
            m_CopyActions.Enabled = false;
            m_GameProbe.Enabled = false;
            m_CopyProbe.Enabled = false;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            CarDrivingMode requested = Mod.Settings != null ? Mod.Settings.CarDriving : CarDrivingMode.GameOwn;
            if (m_Switch.Step(requested, Mod.Gate, out PairFlags flags, out string note))
            {
                // Order does not matter inside this update (none of the four is running now); all four are written
                // before either navigation slot is reached in this step.
                m_GameNavigation.Enabled = flags.GameNavigation;
                m_GameActions.Enabled = flags.GameActions;
                m_CopyNavigation.Enabled = flags.CopyNavigation;
                m_CopyActions.Enabled = flags.CopyActions;
                Running = m_Switch.Applied;
            }
            if (note != null) Mod.log.Info(note);

            bool probes = Mod.Settings != null && Mod.Settings.DebugTiming;
            if (!m_ProbesApplied || probes != m_ProbesOn)
            {
                m_ProbesApplied = true;
                m_ProbesOn = probes;
                m_GameProbe.Enabled = probes;
                m_CopyProbe.Enabled = probes;
                Mod.log.Info($"navigation timing log {(probes ? "on" : "off")}");
            }
        }

        [Preserve]
        public CarDrivingSwitchSystem() { }
    }
}
