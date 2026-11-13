using System;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using Game.Simulation;
using JunctionRules.Core;
using JunctionRules.Settings;
using JunctionRules.Systems;
using JunctionRules.Vanilla;

namespace JunctionRules
{
    /// <summary>
    /// Junction Rules, stage 1: run an unchanged, Burst-compiled copy of the game's car navigation
    /// (JunctionRules/Vanilla/) in the game's own slot, switchable against the game's own system.
    /// </summary>
    public class Mod : IMod
    {
        public static readonly ILog log = LogManager.GetLogger($"{nameof(JunctionRules)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        public static JunctionRulesSettings Settings { get; private set; }

        /// <summary>Version gate result, decided once at load. Fails safe (game's own) until decided.</summary>
        public static GateResult Gate { get; private set; } = VersionGate.Failed("not evaluated yet");

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info($"{nameof(OnLoad)} version {typeof(Mod).Assembly.GetName().Version}, copy made from game {MadeFrom.GameVersion}");

            Settings = new JunctionRulesSettings(this);
            Settings.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));
            AssetDatabase.global.LoadSettings(nameof(JunctionRules), Settings, new JunctionRulesSettings(this));

            Gate = EvaluateGate(Settings.SkipCodeHashCheck);
            if (Gate.CopyAllowed) log.Info("version check: " + Gate.Verdict + ": " + Gate.Message);
            else log.Warn("version check: " + Gate.Verdict + ": " + Gate.Message);
            log.Info($"settings: car driving {Settings.CarDriving}, timing log {(Settings.DebugTiming ? "on" : "off")}, skip code hash check {(Settings.SkipCodeHashCheck ? "on" : "off")}");

            // The copy pair sits right after the game's Actions system, i.e. in the game's own slot: after every system a
            // mod placed "before CarNavigationSystem" (ClearTheWay's ClearTheWaySystem), before every system placed "after
            // CarNavigationSystem" (those sort after the game's Actions, which the game registers first), before
            // TrainNavigationSystem. Same in LoadSimulation, where the game also runs car navigation.
            foreach (SystemUpdatePhase phase in new[] { SystemUpdatePhase.GameSimulation, SystemUpdatePhase.LoadSimulation })
            {
                updateSystem.UpdateBefore<CarDrivingSwitchSystem, CarNavigationSystem>(phase);
                updateSystem.UpdateAfter<JRCarNavigationSystem, CarNavigationSystem.Actions>(phase);
                updateSystem.UpdateAfter<JRCarNavigationSystem.Actions, JRCarNavigationSystem>(phase);
            }
            // Debug timing probes: between each navigation system and its Actions system (GameSimulation only).
            updateSystem.UpdateBefore<GameNavTimingProbeSystem, CarNavigationSystem.Actions>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<CopyNavTimingProbeSystem, JRCarNavigationSystem.Actions>(SystemUpdatePhase.GameSimulation);
            // Creating the switch system now turns the copy and the probes off before any step can run them.
            updateSystem.World.GetOrCreateSystemManaged<CarDrivingSwitchSystem>();
        }

        private static GateResult EvaluateGate(bool skipHashCheck)
        {
            try
            {
                string version = Game.Version.current.version;
                string hash = IlHasher.Hash(typeof(CarNavigationSystem), typeof(CarLaneSpeedIterator));
                GateResult result = VersionGate.Evaluate(MadeFrom.GameVersion, version, MadeFrom.VanillaCodeHash, hash, skipHashCheck);
                if (result.Verdict != GateVerdict.Pass)
                {
                    foreach (var kv in IlHasher.HashEach(typeof(CarNavigationSystem), typeof(CarLaneSpeedIterator)))
                    {
                        log.Info($"vanilla code hash {kv.Key}: {kv.Value}");
                    }
                    log.Info($"vanilla code hash (all): {hash}; game full version {Game.Version.current.fullVersion}");
                }
                return result;
            }
            catch (Exception e)
            {
                log.Error(e, "version check failed");
                return VersionGate.Failed(e.GetType().Name + ": " + e.Message);
            }
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }
        }
    }
}
