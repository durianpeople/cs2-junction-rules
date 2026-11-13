using System.Runtime.CompilerServices;
using Game;
using Game.Simulation;
using JunctionRules.Vanilla;
using Unity.Entities;

namespace JunctionRules.Systems
{
    /// <summary>The copy pair and the timing probes once created; typed as the ECS base class on purpose (see CopyPair).</summary>
    public sealed class CopySystems
    {
        public ComponentSystemBase Navigation;
        public ComponentSystemBase Actions;
        public ComponentSystemBase GameProbe;
        public ComponentSystemBase CopyProbe;
    }

    /// <summary>
    /// The only place outside Vanilla/ and the probe definitions that names the copy's types. Called only when the
    /// version gate allowed the copy. On another game version a component the copy uses may be renamed or gone, and then
    /// loading the copy's types throws TypeLoadException. The runtime resolves the types a method mentions when it
    /// compiles that method, so the mentions live in these NoInlining methods: a caller that skips the call never
    /// compiles them, and nothing else (Mod, CarDrivingSwitchSystem, their fields and signatures) names a copy type.
    /// Checked by the CopyIsolationTests group.
    /// </summary>
    public static class CopyPair
    {
        /// <summary>
        /// The copy pair sits right after the game's Actions system, i.e. in the game's own slot: after every system a
        /// mod placed "before CarNavigationSystem" (ClearTheWay's ClearTheWaySystem), before every system placed "after
        /// CarNavigationSystem" (those sort after the game's Actions, which the game registers first), before
        /// TrainNavigationSystem. Same in LoadSimulation, where the game also runs car navigation. The debug timing
        /// probes sit between each navigation system and its Actions system (GameSimulation only).
        /// </summary>
        /// <remarks>
        /// UpdateSystem.UpdateAfter/UpdateBefore create the system in the world at registration time
        /// (World.GetOrCreateSystemManaged, Game/UpdateSystem.cs), and a fresh system is Enabled. So whatever this
        /// registered, all of it or only part of it before an exception, is switched off again on the way out (finally).
        /// Only CarDrivingSwitchSystem turns any of them on, and only when Mod.CopyRegistered is true.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Register(UpdateSystem updateSystem)
        {
            try
            {
                foreach (SystemUpdatePhase phase in new[] { SystemUpdatePhase.GameSimulation, SystemUpdatePhase.LoadSimulation })
                {
                    updateSystem.UpdateAfter<JRCarNavigationSystem, CarNavigationSystem.Actions>(phase);
                    updateSystem.UpdateAfter<JRCarNavigationSystem.Actions, JRCarNavigationSystem>(phase);
                }
                updateSystem.UpdateBefore<GameNavTimingProbeSystem, CarNavigationSystem.Actions>(SystemUpdatePhase.GameSimulation);
                updateSystem.UpdateBefore<CopyNavTimingProbeSystem, JRCarNavigationSystem.Actions>(SystemUpdatePhase.GameSimulation);
            }
            finally
            {
                DisableAnyExisting(updateSystem.World);
            }
        }

        /// <summary>
        /// Switches off every copy or probe system that exists in the world (registered fully, partly, or created
        /// before a failure). Never throws: each system is handled in its own NoInlining helper inside its own try/catch,
        /// so one type that cannot load (TypeLoadException on another game version) or a system whose creation failed
        /// does not stop the others. Returns how many existing systems it switched off; failures go to the log.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int DisableAnyExisting(World world)
        {
            int disabled = 0;
            if (world == null) return 0;
            disabled += TryDisable("copy navigation", () => DisableNavigation(world));
            disabled += TryDisable("copy actions", () => DisableActions(world));
            disabled += TryDisable("game timing probe", () => DisableGameProbe(world));
            disabled += TryDisable("copy timing probe", () => DisableCopyProbe(world));
            return disabled;
        }

        private static int TryDisable(string what, System.Func<bool> disable)
        {
            try
            {
                return disable() ? 1 : 0;
            }
            catch (System.Exception e)
            {
                JunctionRules.Mod.log.Warn("switching off the " + what + " system failed: " + e.GetType().Name + ": " + e.Message);
                return 0;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool DisableNavigation(World world) => Disable(world.GetExistingSystemManaged<JRCarNavigationSystem>());

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool DisableActions(World world) => Disable(world.GetExistingSystemManaged<JRCarNavigationSystem.Actions>());

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool DisableGameProbe(World world) => Disable(world.GetExistingSystemManaged<GameNavTimingProbeSystem>());

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool DisableCopyProbe(World world) => Disable(world.GetExistingSystemManaged<CopyNavTimingProbeSystem>());

        private static bool Disable(ComponentSystemBase system)
        {
            if (system == null) return false;
            system.Enabled = false;
            return true;
        }

        /// <summary>Gets the registered copy pair and probes from the world, all switched off.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static CopySystems Create(World world)
        {
            var systems = new CopySystems
            {
                Navigation = world.GetOrCreateSystemManaged<JRCarNavigationSystem>(),
                Actions = world.GetOrCreateSystemManaged<JRCarNavigationSystem.Actions>(),
                GameProbe = world.GetOrCreateSystemManaged<GameNavTimingProbeSystem>(),
                CopyProbe = world.GetOrCreateSystemManaged<CopyNavTimingProbeSystem>(),
            };
            systems.Navigation.Enabled = false;
            systems.Actions.Enabled = false;
            systems.GameProbe.Enabled = false;
            systems.CopyProbe.Enabled = false;
            return systems;
        }
    }
}
