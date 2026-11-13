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
    /// </summary>
    public class CarDrivingSwitchSystem : GameSystemBase
    {
        private CarNavigationSystem m_GameNavigation;
        private CarNavigationSystem.Actions m_GameActions;
        /// <summary>Null when the copy is not registered (version gate closed or registration failed).</summary>
        private CopySystems m_Copy;
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
            // Until the first step decides, the copy and the probes stay off (the game's pair keeps its default: on).
            if (Mod.CopyRegistered)
            {
                try
                {
                    m_Copy = CopyPair.Create(World);
                }
                catch (System.Exception e)
                {
                    m_Copy = null;
                    Mod.log.Error(e, "getting the Junction Rules copy systems failed; car driving stays with the game's own system");
                    // Registered systems exist; with m_Copy null this switch never touches them again, so make sure
                    // none of them is left on.
                    Mod.DisableCopyLeftovers(World, "after the failed copy creation");
                }
            }
        }

        [Preserve]
        protected override void OnUpdate()
        {
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
