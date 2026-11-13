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

        /// <summary>True only when the copy pair and the probes were registered (gate allowed it and registration worked).</summary>
        public static bool CopyRegistered { get; private set; }

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

            // The switch runs ahead of the game's car navigation in every step, with or without the copy.
            foreach (SystemUpdatePhase phase in new[] { SystemUpdatePhase.GameSimulation, SystemUpdatePhase.LoadSimulation })
            {
                updateSystem.UpdateBefore<CarDrivingSwitchSystem, CarNavigationSystem>(phase);
            }
            // The copy pair and the probes are registered only when the gate allows the copy. Their types are named only
            // inside CopyPair (NoInlining), so on another game version, where loading them could throw TypeLoadException,
            // nothing here touches them and the game's own system keeps running.
            CopyRegistered = false;
            if (Gate.CopyAllowed)
            {
                try
                {
                    CopyPair.Register(updateSystem);
                    CopyRegistered = true;
                }
                catch (Exception e)
                {
                    log.Error(e, "registering the Junction Rules copy failed; car driving stays with the game's own system");
                    Gate = VersionGate.Failed("registering the copy: " + e.GetType().Name + ": " + e.Message);
                }
            }
            else
            {
                log.Info("Junction Rules copy and timing probes not registered (version check did not allow the copy)");
            }
            // Creating the switch system now turns the copy and the probes off before any step can run them.
            updateSystem.World.GetOrCreateSystemManaged<CarDrivingSwitchSystem>();
        }

        /// <summary>
        /// Version first (a mismatch always keeps the game's own system and never hashes), then the code hash in its own
        /// try/catch inside VersionGate.Check, so a hashing exception after a version match counts as a hash mismatch.
        /// </summary>
        private static GateResult EvaluateGate(bool skipHashCheck)
        {
            GateResult result = VersionGate.Check(MadeFrom.GameVersion, ReadGameVersion, MadeFrom.VanillaCodeHash, HashVanillaCode, skipHashCheck);
            if (result.Verdict == GateVerdict.CodeMismatch || result.Verdict == GateVerdict.PassHashSkipped)
            {
                try
                {
                    foreach (var kv in IlHasher.HashEach(typeof(CarNavigationSystem), typeof(CarLaneSpeedIterator)))
                    {
                        log.Info($"vanilla code hash {kv.Key}: {kv.Value}");
                    }
                    log.Info($"vanilla code hash (all): {HashVanillaCode()}; game full version {Game.Version.current.fullVersion}");
                }
                catch (Exception e)
                {
                    log.Warn("vanilla code hash diagnostics failed: " + e.GetType().Name + ": " + e.Message);
                }
            }
            return result;
        }

        private static string ReadGameVersion() => Game.Version.current.version;

        private static string HashVanillaCode() => IlHasher.Hash(typeof(CarNavigationSystem), typeof(CarLaneSpeedIterator));

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
