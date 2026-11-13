using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using JunctionRules.Core;
using JunctionRules.Systems;

namespace JunctionRules.Settings
{
    [FileLocation(nameof(JunctionRules))]
    [SettingsUIGroupOrder(kDrivingGroup, kDebugGroup)]
    [SettingsUIShowGroupName(kDrivingGroup, kDebugGroup)]
    public class JunctionRulesSettings : ModSetting
    {
        public const string kSection = "Main";
        public const string kDrivingGroup = "Driving";
        public const string kDebugGroup = "Debug";

        public JunctionRulesSettings(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        /// <summary>
        /// Which car-driving system runs. Takes effect at the next simulation step (switchable in game, also while a
        /// city is running). Game's own (default) or the Junction Rules copy (blocked by the version gate if the game
        /// is not the version the copy was made from).
        /// </summary>
        [SettingsUISection(kSection, kDrivingGroup)]
        public CarDrivingMode CarDriving { get; set; }

        /// <summary>Read-only: what runs now and the version gate result.</summary>
        [SettingsUISection(kSection, kDrivingGroup)]
        public string Status => StatusText();

        /// <summary>Logs per-window timing of the active navigation job (game's or copy's). Off: no cost.</summary>
        [SettingsUISection(kSection, kDebugGroup)]
        public bool DebugTiming { get; set; }

        /// <summary>
        /// Probe aid: let the copy run when the game version matches but the vanilla code hash does not (only a
        /// hashing difference between runtimes can cause that on an unchanged game). Never skips the version check.
        /// Read at game start (restart the game after changing it).
        /// </summary>
        [SettingsUISection(kSection, kDebugGroup)]
        public bool SkipCodeHashCheck { get; set; }

        public override void SetDefaults()
        {
            CarDriving = CarDrivingMode.GameOwn;
            DebugTiming = false;
            SkipCodeHashCheck = false;
        }

        public static string StatusText()
        {
            string running = CarDrivingSwitchSystem.Running == CarDrivingMode.JunctionRulesCopy ? "Junction Rules copy" : "game's own";
            return $"Running: {running}. Version check: {Mod.Gate.Verdict}.";
        }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly JunctionRulesSettings m_Setting;

        public LocaleEN(JunctionRulesSettings setting) { m_Setting = setting; }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Junction Rules" },
                { m_Setting.GetOptionTabLocaleID(JunctionRulesSettings.kSection), "Main" },
                { m_Setting.GetOptionGroupLocaleID(JunctionRulesSettings.kDrivingGroup), "Car driving" },
                { m_Setting.GetOptionGroupLocaleID(JunctionRulesSettings.kDebugGroup), "Debug" },

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.CarDriving)), "Car driving" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.CarDriving)),
                    "Which car-driving system runs. Game's own (default): the game's CarNavigationSystem. Junction Rules copy (stage 1 test): an unchanged copy of it, built with Burst, in the same place in the simulation. Changes take effect at the next simulation step, also while a city is running. The copy only runs on the game version it was made from; otherwise the game's own keeps running and the mod log says why." },
                { m_Setting.GetEnumValueLocaleID(CarDrivingMode.GameOwn), "Game's own" },
                { m_Setting.GetEnumValueLocaleID(CarDrivingMode.JunctionRulesCopy), "Junction Rules copy" },

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.Status)), "Status" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.Status)), "What runs now and the result of the version check made at game start. Reopen the options page to refresh." },

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.DebugTiming)), "Log navigation timing" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.DebugTiming)), "Every 1024 simulation frames, write the timing of the running car-navigation job (game's own or copy, measured the same way) and the simulation rate to the mod log. Off: nothing is measured." },

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.SkipCodeHashCheck)), "Skip code hash check (probe aid)" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.SkipCodeHashCheck)), "Only if the mod log says the game version matches but the code hash does not: lets the copy run anyway. The game version must still match. Read at game start: restart the game after changing it." },
            };
        }

        public void Unload() { }
    }
}
