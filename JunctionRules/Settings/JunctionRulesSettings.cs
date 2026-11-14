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

        /// <summary>
        /// Stage 2: how a car judges the conflicts on its path through a junction. Stock (default): the game's own,
        /// conflict by conflict. Whole junction: wait just before the first path crossing (0.2.3: the only waiting
        /// spot, no mid-junction spots), one decision there for the whole path (stock judgement on each point, other cars
        /// projected to the car's own arrival time), plus a safety stop before the next uncrossed point if a missed car
        /// shows up and the car can still stop. Acts only while the copy drives; applies at the next simulation step.
        /// </summary>
        [SettingsUISection(kSection, kDrivingGroup)]
        public JunctionGapRule GapRule { get; set; }

        /// <summary>
        /// Stage 2, whole junction only: where a waiting car stands. Before the first crossing (default, owner ruling
        /// 14 Nov 2026) or at the stop line (the allowed variant).
        /// </summary>
        [SettingsUISection(kSection, kDrivingGroup)]
        public JunctionWaitingSpot WaitingSpot { get; set; }

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
            GapRule = JunctionGapRule.Stock;
            WaitingSpot = JunctionWaitingSpot.BeforeFirstCrossing;
            DebugTiming = false;
            SkipCodeHashCheck = false;
        }

        public static string StatusText()
        {
            return CopyAcquisition.StatusText(CarDrivingSwitchSystem.Running, Mod.Gate, Mod.CopyRegistered, CarDrivingSwitchSystem.CopyState);
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

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.GapRule)), "Junction gap rule" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.GapRule)),
                    "How cars judge the conflicts on their path through a junction. Stock (default): the game's own, conflict by conflict. Whole junction (stage 2 test): a car that has to wait stops just before the first point where its path crosses another lane's path (at the stop line if that is at the junction edge; STOP signs still stop at the line first), then goes only when the game's own judgement finds every conflict on its whole path clear, with the other cars projected to the time this car needs to reach each conflict (cars on the approach lanes count too). There is no waiting spot inside the junction, also not on a central reservation. While crossing it keeps checking the conflicts it has not reached yet in the same way: if one is no longer clear and it can still stop with normal braking, it stops just before it (safety stop) and waits until the rest is clear; otherwise it brakes only to avoid hitting anything. Acts only while Car driving is the Junction Rules copy. Changes take effect at the next simulation step." },
                { m_Setting.GetEnumValueLocaleID(JunctionGapRule.Stock), "Stock (conflict by conflict)" },
                { m_Setting.GetEnumValueLocaleID(JunctionGapRule.WholeJunction), "Whole junction" },

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.WaitingSpot)), "Whole junction: waiting spot" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.WaitingSpot)),
                    "Only with the Whole junction rule. Before the first crossing (default): a car that has to wait moves into the junction up to the point just before its path first crosses another lane's path (for example a turner across oncoming traffic on a green waits before the first oncoming lane). At the stop line: every car waits at the line instead. Changes take effect at the next simulation step." },
                { m_Setting.GetEnumValueLocaleID(JunctionWaitingSpot.BeforeFirstCrossing), "Before the first crossing" },
                { m_Setting.GetEnumValueLocaleID(JunctionWaitingSpot.StopLine), "At the stop line" },

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.Status)), "Status" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.Status)), "What runs now, the result of the version check made at game start, and whether the copy can run (set up at the first simulation step). Reopen the options page to refresh." },

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.DebugTiming)), "Log navigation timing" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.DebugTiming)), "Every 1024 simulation frames, write the timing of the running car-navigation job (game's own or copy, measured the same way) and the simulation rate to the mod log; the copy's line also names the junction gap rule that ran and its stage 2 counters. Off: nothing is measured. Only available when the version check passed at game start." },

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.SkipCodeHashCheck)), "Skip code hash check (probe aid)" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.SkipCodeHashCheck)), "Only if the mod log says the game version matches but the code hash does not: lets the copy run anyway. The game version must still match. Read at game start: restart the game after changing it." },
            };
        }

        public void Unload() { }
    }
}
