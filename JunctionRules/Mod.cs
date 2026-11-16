using System;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using Game.Simulation;
using JunctionRules.Core;
using JunctionRules.Boxes;
using JunctionRules.Paint;
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
            // 0.4.0 (JR-T-007-r): a key binding problem must not take the driving rules down with it (the tool then has no shortcut).
            try { Settings.RegisterKeyBindings(); }
            catch (Exception e) { log.Warn("yellow box tool shortcut not registered: " + e.GetType().Name + ": " + e.Message); }
            Settings.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));
            AssetDatabase.global.LoadSettings(nameof(JunctionRules), Settings, new JunctionRulesSettings(this));
            try
            {
                string migrated = Settings.MigrateAfterLoad();
                if (migrated != null) log.Info(migrated);
            }
            catch (Exception e)
            {
                log.Warn("settings migration failed (settings kept as loaded): " + e.GetType().Name + ": " + e.Message);
            }

            Gate = EvaluateGate(Settings.SkipCodeHashCheck);
            if (Gate.CopyAllowed) log.Info("version check: " + Gate.Verdict + ": " + Gate.Message);
            else log.Warn("version check: " + Gate.Verdict + ": " + Gate.Message);
            log.Info($"settings: car driving {Settings.CarDriving}, junction gap rule {Settings.GapRule}, waiting spot {Settings.WaitingSpot}, turning across oncoming gives way {(Settings.TurnAcrossGivesWay ? "on" : "off")}, cars never push through people on crossings {(Settings.NoPushThroughPeople ? "on" : "off")}, people pass a car that blocks a crossing after {(PeoplePass.On(Settings.PeoplePassEffectiveSeconds()) ? Settings.PeoplePassEffectiveSeconds().ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s" : "off")}, cars never stop on a zebra {(Settings.ZebraNonBlocking ? "on (room margin " + Settings.ZebraRoomMargin.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m)" : "off")}, timing log {(Settings.DebugTiming ? "on" : "off")}, yellow box gap to road markings {Settings.YellowBoxMarkingMargin.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} m, yellow box paint test {(Settings.YellowBoxPaintTest ? "on" : "off")} (paint onto {Settings.PaintLayers}), skip code hash check {(Settings.SkipCodeHashCheck ? "on" : "off")}");

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
                    // UpdateAfter/UpdateBefore create systems at registration, Enabled. Register already switches off what
                    // it made (finally); this repeats it in case Register failed before its own try (e.g. at JIT time).
                    DisableCopyLeftovers(updateSystem.World, "after the failed registration");
                }
            }
            else
            {
                log.Info("Junction Rules copy and timing probes not registered (version check did not allow the copy)");
            }
            // 0.3.6 (JR-T-008): people and cyclists pass a vehicle standing on their crossing. Own system after the
            // game's HumanNavigationSystem; no game code copied. Registered only when the version gate allowed the copy
            // (it relies on the 1.6.4f1 meaning of the blocker and the crossing clamp); a failure leaves the rest running.
            if (Gate.CopyAllowed)
            {
                try
                {
                    updateSystem.UpdateAfter<PeoplePassSystem, HumanNavigationSystem>(SystemUpdatePhase.GameSimulation);
                }
                catch (Exception e)
                {
                    log.Error(e, "registering the people-pass system failed; people wait for standing cars as in the game");
                }
            }
            // The switch runs ahead of the game's car navigation in every step, with or without the copy. Registered
            // last, after CopyRegistered is final: UpdateBefore creates the switch (runs its OnCreate) right here. The
            // switch does not depend on that order (it gets the copy lazily in its first update, see
            // CarDrivingSwitchSystem), but registering it last keeps the two in step. The copy and the probes are
            // already off at this point (CopyPair.Register's finally); the switch's first update, ahead of both
            // navigation slots, writes all four Enabled flags of the step.
            foreach (SystemUpdatePhase phase in new[] { SystemUpdatePhase.GameSimulation, SystemUpdatePhase.LoadSimulation })
            {
                updateSystem.UpdateBefore<CarDrivingSwitchSystem, CarNavigationSystem>(phase);
            }

            // Stage 3 probe (0.3.0-probe): yellow box paint test, draw-only, default off. Registered last and on its own:
            // if it fails, the copy, stage 2 and the timing probes above are already in place and unaffected.
            try
            {
                PaintRegistration.Register(updateSystem);
            }
            catch (Exception e)
            {
                log.Error(e, "registering the yellow box paint test failed; painting is unavailable, the rest of Junction Rules is not affected");
                PaintRegistration.DisableLeftover(updateSystem.World);
            }

            // 0.4.0-probe (JR-T-007): yellow box selection tool + its toggle (toolbar button, shortcut, Options button).
            // Idle until the owner opens it. On its own try/catch: a failure leaves everything above in place.
            try
            {
                Boxes.BoxRegistration.Register(updateSystem);
            }
            catch (Exception e)
            {
                log.Error(e, "registering the yellow box tool failed; the tool is unavailable, the rest of Junction Rules is not affected");
            }
        }

        /// <summary>
        /// Switches off any copy or probe system that exists in the world, so nothing of the copy runs next to the game's
        /// own system while the switch leaves it alone. Never throws. Used when registration or creation failed.
        /// </summary>
        internal static void DisableCopyLeftovers(Unity.Entities.World world, string when)
        {
            try
            {
                int n = CopyPair.DisableAnyExisting(world);
                log.Warn($"switched off {n} existing Junction Rules copy/probe system(s) {when}");
            }
            catch (Exception e)
            {
                log.Error(e, $"switching off the Junction Rules copy/probe systems {when} failed");
            }
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
