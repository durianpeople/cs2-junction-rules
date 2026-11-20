using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;
using JunctionRules.Boxes;
using JunctionRules.Core;
using JunctionRules.Paint;
using JunctionRules.Systems;

namespace JunctionRules.Settings
{
    [FileLocation(nameof(JunctionRules))]
    [SettingsUIGroupOrder(kDrivingGroup, kBoxesGroup, kPaintGroup, kDebugGroup)]
    [SettingsUIShowGroupName(kDrivingGroup, kBoxesGroup, kPaintGroup, kDebugGroup)]
    [SettingsUIKeyboardAction(kToggleBoxToolAction, ActionType.Button, usages: new[] { "DefaultTool", "Editor", "Tool" })]
    public class JunctionRulesSettings : ModSetting
    {
        public const string kSection = "Main";
        public const string kDrivingGroup = "Driving";
        public const string kDebugGroup = "Debug";
        public const string kPaintGroup = "PaintTest";
        public const string kBoxesGroup = "YellowBoxes";
        public const string kToggleBoxToolAction = "ToggleYellowBoxTool";

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

        /// <summary>
        /// Stage 2, whole junction only (0.2.4, owner ruling 14 Nov 2026): between lanes with equal signs, a turn across
        /// the oncoming flow gives way to the oncoming straight (Core/TurnAcross.cs). Default on; next simulation step.
        /// </summary>
        [SettingsUISection(kSection, kDrivingGroup)]
        public bool TurnAcrossGivesWay { get; set; }

        /// <summary>
        /// 0.3.2 (JR-T-004, owner ask 15 Nov 2026): a car held by people on a crossing of its path (zebra, junction
        /// crosswalk, footway across a driveway) or by a cyclist riding the crossing stays stopped until they are clear:
        /// no creep through them after the game's 5 s timer, none through a person standing still (Core/CrossingPush.cs).
        /// Car against car stays stock. Default on; acts while the copy drives (with either gap rule); next simulation step.
        /// </summary>
        [SettingsUISection(kSection, kDrivingGroup)]
        public bool NoPushThroughPeople { get; set; }

        /// <summary>
        /// 0.3.6 (JR-T-008, second owner ruling 16 Nov 2026): master switch for the pass, default on. Off = exactly the
        /// game's behaviour (the people-pass system does nothing, its counter stays 0), for A/B tests.
        /// </summary>
        [SettingsUISection(kSection, kDrivingGroup)]
        public bool PeoplePassOn { get; set; }

        /// <summary>The wait slider is greyed out while the switch is off (SettingsUIDisableByCondition).</summary>
        public bool PeoplePassSliderDisabled() => !PeoplePassOn;

        /// <summary>The wait the pass uses now: 0 (off) when the switch is off, else the slider value (PeoplePass.Effective).</summary>
        public float PeoplePassEffectiveSeconds() => PeoplePass.Effective(PeoplePassOn, PeoplePassSeconds);

        /// <summary>
        /// 0.3.6 (JR-T-008, owner ask and ruling 16 Nov 2026): a person (or a cyclist) held at a crossing by a vehicle
        /// standing on it walks (rides) on through it after this many seconds of game time. Default 2 s; used only while PeoplePassOn;
        /// 0 also switches the pass off. Works with either Car driving mode
        /// (Systems/PeoplePassSystem.cs, Core/PeoplePass.cs); next simulation step.
        /// </summary>
        [SettingsUIDisableByCondition(typeof(JunctionRulesSettings), nameof(PeoplePassSliderDisabled))]
        [SettingsUISlider(min = PeoplePass.MinSeconds, max = PeoplePass.MaxSeconds, step = PeoplePass.StepSeconds, unit = "floatSingleFraction")]
        [SettingsUISection(kSection, kDrivingGroup)]
        public float PeoplePassSeconds { get; set; }

        /// <summary>
        /// 0.4.1 (JR-T-009, owner rule 16 Nov 2026): a car never stops on a zebra. It enters a zebra only when the zebra is
        /// clear of people, (Whole junction) every vehicle conflict point after it up to the next zebra or the end of the
        /// junction is clear, and there is room for the whole vehicle past it. Every zebra, also mid-block ones; with
        /// either gap rule (the stock rule gets the room check only). Default on; off = 0.4.0 behaviour
        /// (Core/ZebraSections.cs). Acts while the copy drives; next simulation step.
        /// </summary>
        [SettingsUISection(kSection, kDrivingGroup)]
        public bool ZebraNonBlocking { get; set; }

        /// <summary>The margin slider is greyed out while both rules that use it are off (0.4.2: shared with the yellow boxes).</summary>
        public bool ZebraMarginDisabled() => !ZebraNonBlocking && !BoxObey;

        /// <summary>
        /// 0.4.1: extra room (m) past a zebra on top of the car's whole length before it may enter (RoomScan; default 1 m,
        /// the game's own spacing per queued car).
        /// </summary>
        [SettingsUIDisableByCondition(typeof(JunctionRulesSettings), nameof(ZebraMarginDisabled))]
        [SettingsUISlider(min = RoomScan.MinMargin, max = RoomScan.MaxMargin, step = RoomScan.StepMargin, unit = "floatSingleFraction")]
        [SettingsUISection(kSection, kDrivingGroup)]
        public float ZebraRoomMargin { get; set; }

        /// <summary>
        /// 0.4.4 (JR-T-012, owner rule 16 Nov 2026): "Cars coming out of ANY building must give way, and when that building
        /// exit is a yellow box, must ALSO obey the yellow box rules." A vehicle leaving a building (drive, car park or
        /// depot gate) waits at the exit until the road lanes it crosses or merges into are clear (the Whole junction gap
        /// judgement, Core/ExitGiveWay.cs); at a boxed exit it also needs room past the band. On-duty emergency vehicles:
        /// stock. Default on; off = the 0.4.3 behaviour. Acts while the copy drives; next simulation step.
        /// </summary>
        [SettingsUISection(kSection, kDrivingGroup)]
        public bool ExitGiveWay { get; set; }

        // JR-T-014d (G11) BEGIN
        /// <summary>
        /// 0.4.8 (JR-T-014d, owner rule G11 17 Nov 2026): people crossing where there is no zebra (unmarked crossings) have
        /// no right of way. They wait at the kerb until approaching cars leave a safe gap (Systems/UnmarkedCrossingSystem.cs);
        /// cars do not hold for them, but never drive into a person already on the carriageway. Default on; off = 0.4.7.
        /// </summary>
        [SettingsUISection(kSection, kDrivingGroup)]
        public bool PeopleGiveWayUnmarked { get; set; }
        // JR-T-014d (G11) END

        /// <summary>Read-only: what runs now and the version gate result.</summary>
        [SettingsUISection(kSection, kDrivingGroup)]
        public string Status => StatusText();

        // ---- 0.4.0-probe (JR-T-007): yellow box selection tool; boxes saved in the city ----

        /// <summary>Keyboard shortcut that opens / closes the yellow box tool (rebindable; default Ctrl+Y).</summary>
        [SettingsUISection(kSection, kBoxesGroup)]
        [SettingsUIKeyboardBinding(BindingKeyboard.Y, kToggleBoxToolAction, ctrl: true)]
        public ProxyBinding ToggleBoxToolBinding { get; set; }

        /// <summary>Opens / closes the yellow box tool (same as the toolbar button and the shortcut).</summary>
        [SettingsUIButton]
        [SettingsUISection(kSection, kBoxesGroup)]
        public bool OpenBoxTool { set { BoxToolControl.Toggle(); } }

        private float m_YellowBoxMarkingMargin = MarkingMargin.Default;

        /// <summary>
        /// 0.4.2-probe: how far (m) every yellow box edge keeps clear of painted road markings (zebra stripes, stop /
        /// give-way lines), measured from the marking's painted edge. The paint list's "markingMargin" overrides it.
        /// </summary>
        [SettingsUISlider(min = MarkingMargin.Min, max = MarkingMargin.Max, step = MarkingMargin.Step, unit = "floatSingleFraction")]
        [SettingsUISection(kSection, kBoxesGroup)]
        public float YellowBoxMarkingMargin
        {
            get => m_YellowBoxMarkingMargin;
            set
            {
                m_YellowBoxMarkingMargin = value;
                // JR-T-007b-r F6: a paint-test.json markingMargin keeps overriding the slider (as in the paint).
                JunctionRules.Boxes.BoxStore.SetMarginSetting(value);
                PaintControl.ApplySettings();
            }
        }

        private bool m_HideMarkingsUnderBoxes = false;

        /// <summary>
        /// 0.4.4 (JR-T-011 B, owner 16 Nov 2026): the road's own markings inside a painted yellow box (lane dashes, centre
        /// lines, arrows, painted speed numbers) are not drawn, so only the hatch shows. Render-side only, never saved
        /// (Paint/MarkingHider.cs). JR-T-013e (G2): default OFF until the owner's live gates K1 (save with it on, load without
        /// the mod: markings intact) and K2 (add / remove a box, toggle: markings go / come back at once) pass; off = the 0.4.3
        /// look (markings show through the box).
        /// </summary>
        [SettingsUISection(kSection, kBoxesGroup)]
        public bool HideMarkingsUnderBoxes
        {
            get => m_HideMarkingsUnderBoxes;
            set { m_HideMarkingsUnderBoxes = value; PaintControl.ApplySettings(); }
        }

        /// <summary>
        /// 0.4.2 (JR-T-010, owner rule 16 Nov 2026, UK yellow box): a car does not enter a box the owner placed (junction or
        /// entrance) unless there is room for its whole length past it; default on; off = 0.4.1 driving (Core/BoxDriving.cs).
        /// Acts while the copy drives; next simulation step.
        /// </summary>
        [SettingsUISection(kSection, kBoxesGroup)]
        public bool BoxObey { get; set; }

        /// <summary>The box sub-settings are greyed out while the box rule is off. (1.5.0, JR-T-038: the UK right-turn setting
        /// "turning across oncoming traffic may wait in the box" is retired; past the line every car drives as at any junction.)</summary>
        public bool BoxSubSettingsDisabled() => !BoxObey;

        /// <summary>
        /// JR-T-022 (owner 19 Nov 2026): a car about to enter a box judges the whole stretch ahead (every vehicle before, in
        /// and past the box, their speeds, the room beyond them), not only the car right in front: when the vehicles past the
        /// box are clearly moving away, each is expected to keep going and to stop only behind a queue or vehicle it can see
        /// standing (or at a red light), and that leaves the room, it does not pause at the line (Core RoomScan.LookPast; 1.2.0). JR-T-024: with it, people walking across a box's exit zebra let the car in to wait just
        /// before the zebra when the road past has room free now; someone standing there on its way holds it at the line (Core
        /// ExitZebraWait; JR-T-024b: cyclists are not people, as in the game's own check). Default on; off = 1.0.1 driving. Greyed out while the box rule is off.
        /// </summary>
        [SettingsUIDisableByCondition(typeof(JunctionRulesSettings), nameof(BoxSubSettingsDisabled))]
        [SettingsUISection(kSection, kBoxesGroup)]
        public bool BoxLookPast { get; set; }

        /// <summary>
        /// 0.4.7 (JR-T-013d, gap G6, owner rule 17 Nov 2026): no car parks inside a yellow box, whether or not the road has
        /// kerbside parking there. Every parking slot whose space overlaps a box (junction or exit box) is blocked the way
        /// the game blocks slots at driveways (Parking/BoxParkingSystem.cs); cars already parked there leave normally.
        /// Runtime only: nothing extra is saved with the city. Default on; off = 0.4.6 (cars may park in boxes).
        /// </summary>
        [SettingsUISection(kSection, kBoxesGroup)]
        public bool NoParkingInBoxes { get; set; }

        // ---- stage 3 probe (0.3.0-probe): yellow box paint test, draw-only, default off; 1.0.0: developer options ----

        private bool m_YellowBoxPaintTest;
        private BoxPaintMode m_PaintMode;
        private BoxPaintLayers m_PaintLayers = PaintSettingsMigration.Default;

        /// <summary>
        /// Stage 3 probe: paints UK yellow boxes on the junctions listed in ModsData/JunctionRules/paint-test.json with
        /// the game's surface shader. Draw-only (nothing saved, no entity touched, no driving change). Default off; off =
        /// the paint system does not run at all.
        /// </summary>
        [SettingsUIDeveloper] // 1.0.0: developer option, shown only with the -developerMode launch option
        [SettingsUISection(kSection, kPaintGroup)]
        public bool YellowBoxPaintTest
        {
            get => m_YellowBoxPaintTest;
            set { m_YellowBoxPaintTest = value; PaintControl.ApplySettings(); }
        }

        /// <summary>What the paint test draws: hatch (border + diagonals), plain fill, or outline only.</summary>
        [SettingsUIDeveloper] // 1.0.0: developer option, shown only with the -developerMode launch option
        [SettingsUISection(kSection, kPaintGroup)]
        public BoxPaintMode PaintMode
        {
            get => m_PaintMode;
            set { m_PaintMode = value; PaintControl.ApplySettings(); }
        }

        /// <summary>Decal layers the paint goes onto: Roads (2, default) or Roads + Other (34), or 39 like G87 (debug).</summary>
        [SettingsUIDeveloper] // 1.0.0: developer option, shown only with the -developerMode launch option
        [SettingsUISection(kSection, kPaintGroup)]
        public BoxPaintLayers PaintLayers
        {
            get => m_PaintLayers;
            set { m_PaintLayers = value; PaintControl.ApplySettings(); }
        }

        /// <summary>
        /// Hidden: settings schema version (0.3.5, PaintSettingsMigration). 0 = saved by 0.3.4-probe or older; the load
        /// in Mod.OnLoad migrates once (a saved Roads + Other from probe step S6 goes back to Roads) and writes 1.
        /// Deliberately NOT reset in SetDefaults: after "Reset to defaults" a later Roads + Other choice must survive the
        /// next load (a reset version would migrate it back to Roads).
        /// </summary>
        [SettingsUIHidden]
        public int PaintSettingsVersion { get; set; }

        /// <summary>
        /// Hidden: diagnostics schema (Core/DiagnosticsMigration). 0.4.8-0.4.12-probe wrote 1 after switching "Log navigation
        /// timing" and "Exit trace (log)" on once for the probe's evidence. 1.0.0 (first public release) never switches them
        /// on: a fresh install keeps them off, and a probe's saved 1 switches both off once (they are developer options now).
        /// Not reset in SetDefaults.
        /// </summary>
        [SettingsUIHidden]
        public int DiagnosticsVersion { get; set; }

        public const int DiagnosticsCurrent = DiagnosticsMigration.Current;

        /// <summary>1.0.0: the one-time diagnostics migration (Mod.OnLoad, after MigrateAfterLoad); returns a log line or null.</summary>
        public string MigrateDiagnostics()
        {
            bool timing = DebugTiming, trace = ExitTrace;
            if (!DiagnosticsMigration.Migrate(DiagnosticsVersion, ref timing, ref trace, out bool changed)) return null;
            int from = DiagnosticsVersion;
            DebugTiming = timing;
            ExitTrace = trace;
            DiagnosticsVersion = DiagnosticsCurrent;
            ApplyAndSave();
            return changed
                ? $"settings: diagnostics schema {from} -> {DiagnosticsCurrent}: the probe's timing log and exit trace switched off (developer options; -developerMode shows them)"
                : $"settings: diagnostics schema {from} -> {DiagnosticsCurrent}: timing log {(timing ? "on" : "off")}, exit trace {(trace ? "on" : "off")} kept";
        }

        /// <summary>Re-reads the test list file and rebuilds every box.</summary>
        [SettingsUIButton]
        [SettingsUIDeveloper] // 1.0.0: developer option, shown only with the -developerMode launch option
        [SettingsUISection(kSection, kPaintGroup)]
        public bool PaintRefresh { set { PaintControl.Refresh(); } }

        /// <summary>Adds the junction node at the camera centre to the test list file (read-only pick; writes only the list file).</summary>
        [SettingsUIButton]
        [SettingsUIDeveloper] // 1.0.0: developer option, shown only with the -developerMode launch option
        [SettingsUISection(kSection, kPaintGroup)]
        public bool PaintAddCameraJunction { set { PaintControl.AddCameraJunction(); } }

        /// <summary>Writes the paint cost line to the log now.</summary>
        [SettingsUIButton]
        [SettingsUIDeveloper] // 1.0.0: developer option, shown only with the -developerMode launch option
        [SettingsUISection(kSection, kPaintGroup)]
        public bool PaintLogCost { set { PaintControl.LogCost(); } }

        /// <summary>Read-only: what the paint test does now.</summary>
        [SettingsUIDeveloper] // 1.0.0: developer option, shown only with the -developerMode launch option
        [SettingsUISection(kSection, kPaintGroup)]
        public string PaintStatus => PaintControl.Status();

        /// <summary>Logs per-window timing of the active navigation job (game's or copy's). Off: no cost.</summary>
        [SettingsUIDeveloper] // 1.0.0: developer option, shown only with the -developerMode launch option
        [SettingsUISection(kSection, kDebugGroup)]
        public bool DebugTiming { get; set; }

        /// <summary>
        /// 0.4.7 (JR-T-013a, G1): one log line per vehicle leaving a building or a parking space (its first wait at the
        /// exit, its hand-over to the road lane): vehicle, building, path, decision, boxed. At most Core
        /// ExitTrace.MaxLinesPerSecond lines a second. Needs "Vehicles leaving buildings give way". Default off.
        /// </summary>
        [SettingsUIDeveloper] // 1.0.0: developer option, shown only with the -developerMode launch option
        [SettingsUISection(kSection, kDebugGroup)]
        public bool ExitTrace { get; set; }

        /// <summary>
        /// Probe aid: let the copy run when the game version matches but the vanilla code hash does not (only a
        /// hashing difference between runtimes can cause that on an unchanged game). Never skips the version check.
        /// Read at game start (restart the game after changing it).
        /// </summary>
        [SettingsUIDeveloper] // 1.0.0: developer option, shown only with the -developerMode launch option
        [SettingsUISection(kSection, kDebugGroup)]
        public bool SkipCodeHashCheck { get; set; }

        /// <summary>
        /// JR-T-013b (G4): starts the watch-one-junction log at the junction the camera looks at (any junction, boxed or
        /// not). Default off: nothing is watched until this is pressed; not saved (a load clears it).
        /// </summary>
        [SettingsUIButton]
        [SettingsUIDeveloper] // 1.0.0: developer option, shown only with the -developerMode launch option
        [SettingsUISection(kSection, kDebugGroup)]
        public bool WatchCameraJunction { set { WatchHandoff.PickRequested = true; } }

        /// <summary>JR-T-013b (G4): stops the watch-one-junction log.</summary>
        [SettingsUIButton]
        [SettingsUIDeveloper] // 1.0.0: developer option, shown only with the -developerMode launch option
        [SettingsUISection(kSection, kDebugGroup)]
        public bool WatchStop { set { WatchHandoff.StopRequested = true; } }

        public override void SetDefaults()
        {
            CarDriving = CarDrivingMode.GameOwn;
            GapRule = JunctionGapRule.Stock;
            WaitingSpot = JunctionWaitingSpot.BeforeFirstCrossing;
            TurnAcrossGivesWay = true;
            NoPushThroughPeople = true;
            PeoplePassOn = true;
            PeoplePassSeconds = PeoplePass.DefaultSeconds;
            YellowBoxMarkingMargin = MarkingMargin.Default;
            ZebraNonBlocking = true;
            ZebraRoomMargin = RoomScan.DefaultMargin;
            ExitGiveWay = true;
            PeopleGiveWayUnmarked = true; // JR-T-014d (G11)
            BoxObey = true;
            HideMarkingsUnderBoxes = false;
            BoxLookPast = true;   // JR-T-022
            YellowBoxPaintTest = false;
            PaintMode = BoxPaintMode.Hatch;
            PaintLayers = PaintSettingsMigration.Default;
            NoParkingInBoxes = true;
            DebugTiming = false;   // 1.0.0: developer option, off for players
            ExitTrace = false;     // 1.0.0: developer option, off for players
            SkipCodeHashCheck = false;
        }

        /// <summary>
        /// One-time migration after loading the saved settings (0.3.5): a "paint onto" of Roads + Other saved by the
        /// 0.3.0-0.3.4 probe step goes back to the Roads default; any other value is kept. Returns a log line, or null
        /// when nothing was due.
        /// </summary>
        public string MigrateAfterLoad()
        {
            if (PaintSettingsVersion >= PaintSettingsMigration.Current) return null;
            BoxPaintLayers before = PaintLayers;
            BoxPaintLayers after = PaintSettingsMigration.Migrate(PaintSettingsVersion, before, out bool changed);
            if (changed) PaintLayers = after;
            int from = PaintSettingsVersion;
            PaintSettingsVersion = PaintSettingsMigration.Current;
            ApplyAndSave();
            return changed
                ? $"settings migrated (schema {from} -> {PaintSettingsMigration.Current}): yellow box paint onto {before} -> {after} (the new default; {before} stays selectable)"
                : $"settings schema {from} -> {PaintSettingsMigration.Current}: yellow box paint onto {before} kept";
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
                    "Which car-driving system runs. Game's own (default): the game's own car driving; the driving rules below do nothing. Junction Rules: the game's car driving with Junction Rules' changes (a Burst-compiled copy that runs in the same place in the simulation); switch to it to use the driving rules below and the yellow box rules. Changes take effect at the next simulation step, also while a city is running. Junction Rules only runs on the game version it was made for; otherwise the game's own keeps running and Status says why." },
                { m_Setting.GetEnumValueLocaleID(CarDrivingMode.GameOwn), "Game's own" },
                { m_Setting.GetEnumValueLocaleID(CarDrivingMode.JunctionRulesCopy), "Junction Rules" },

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.GapRule)), "Junction gap rule" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.GapRule)),
                    "How cars judge the conflicts on their path through a junction. Stock (default): the game's own, conflict by conflict. Whole junction: a car that has to wait stops just before the first point where its path crosses another lane's path (at the stop line if that is at the junction edge; STOP signs still stop at the line first), then goes only when the game's own judgement finds every conflict on its whole path clear, with the other cars projected to the time this car needs to reach each conflict (cars on the approach lanes count too). There is no waiting spot inside the junction, also not on a central reservation. While crossing it keeps checking the conflicts it has not reached yet in the same way: if one is no longer clear and it can still stop with normal braking, it stops just before it (safety stop) and waits until the rest is clear; otherwise it brakes only to avoid hitting anything. Acts only while Car driving is Junction Rules. Changes take effect at the next simulation step." },
                { m_Setting.GetEnumValueLocaleID(JunctionGapRule.Stock), "Stock (conflict by conflict)" },
                { m_Setting.GetEnumValueLocaleID(JunctionGapRule.WholeJunction), "Whole junction" },

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.WaitingSpot)), "Whole junction: waiting spot" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.WaitingSpot)),
                    "Only with the Whole junction rule. Before the first crossing (default): a car that has to wait moves into the junction up to the point just before its path first crosses another lane's path (for example a turner across oncoming traffic on a green waits before the first oncoming lane). At the stop line: every car waits at the line instead. Changes take effect at the next simulation step." },
                { m_Setting.GetEnumValueLocaleID(JunctionWaitingSpot.BeforeFirstCrossing), "Before the first crossing" },
                { m_Setting.GetEnumValueLocaleID(JunctionWaitingSpot.StopLine), "At the stop line" },

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.TurnAcrossGivesWay)), "Whole junction: turning across oncoming traffic gives way" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.TurnAcrossGivesWay)),
                    "Only with the Whole junction rule. On (default): where neither direction has a sign or signal priority over the other, a car turning across the oncoming traffic (right turns where cars drive on the left, left turns where they drive on the right) gives way to the oncoming cars going straight on, and an oncoming car going straight on does not wait just because the turner has reserved its way through; it still brakes for a turner that is physically in its path. Off: the game's own priority. Changes take effect at the next simulation step." },

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.NoPushThroughPeople)), "Cars never push through people on crossings" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.NoPushThroughPeople)),
                    "On (default): a car waiting for people on a crossing of its path (a zebra or a crossing at a junction), or for a cyclist riding across it, keeps waiting until they are clear, however long that takes. At a building access the pavement is not a crossing: the car stops this way only for a person actually in its way there, and a person standing beside its way does not hold it. Off: the game's own behaviour, where after about 5 seconds the car creeps on at walking pace through the people (and creeps at once past a person standing still). Cars waiting for other cars are not changed. Works with either junction gap rule, only while Car driving is Junction Rules. Changes take effect at the next simulation step." },

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.PeoplePassOn)), "People pass a car that blocks them" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.PeoplePassOn)),
                    "On (default): a person or cyclist held on a crossing by a vehicle standing across it goes on through the vehicle after the wait set below. Off: exactly the game's behaviour (they wait as long as the vehicle stands), for comparison. Works with either Car driving setting. Changes take effect at the next simulation step." },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.PeoplePassSeconds)), "People pass a car that blocks a crossing after (s)" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.PeoplePassSeconds)),
                    "A person walking a crossing (a zebra or a crossing at a junction) or the pavement across a building access who is held by a car or other vehicle standing on it walks on through the vehicle after this many seconds; a cyclist riding the crossing likewise rides on through it at walking pace. Default 2 seconds. Used only while People pass a car that blocks them is on; 0 also switches the pass off. Cars still never push through people (setting above). Works with either Car driving setting. Changes take effect at the next simulation step." },

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.ZebraNonBlocking)), "Cars never stop on a zebra" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.ZebraNonBlocking)),
                    "On (default): a car drives onto a zebra only when it can also leave it. The zebra must be clear of people, there must be room for the whole vehicle (trailers included) past it in the queue ahead, and with the Whole junction rule every crossing point with other lanes after the zebra up to the next zebra (or the end of the junction) must be clear too; it waits before the zebra until then. Where the whole vehicle cannot wait between two zebras (or between a zebra and the crossing points before it), it decides at the earlier waiting spot and never waits with its tail on a zebra. When people walking across the later zebra are then the only reason to wait, it drives up and waits right before that zebra (its rear may still stand over a lane it has already crossed), unless there is no room past the zebra, traffic could reach it while it waits there, or the junction is a yellow box; someone standing still on that zebra counts as no room (as at a yellow box), so the car waits at the earlier spot (Whole junction rule only; with the Stock rule it waits at the earlier spot). Applies to every zebra, also mid-block ones, with either junction gap rule (the Stock rule gets the room check only). At a zebra with a pedestrian light that is red or ending for the people, a car may stand on the zebra while it gives way (e.g. turning across oncoming traffic), but it still does not drive onto it while there is no room past it. Off: as in the game. Only while Car driving is Junction Rules. Changes take effect at the next simulation step." },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.ZebraRoomMargin)), "Room margin past a zebra or yellow box (m)" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.ZebraRoomMargin)),
                    "Extra space, on top of the vehicle's own length, that must be free past a zebra (setting Cars never stop on a zebra) or past a yellow box (setting Cars obey yellow boxes) before a car drives onto it. A moving queue counts the distance the car ahead needs to stop (the game's own measure), so a queue that keeps moving lets cars in. Default 1 m. Larger: fewer tails left on a zebra, more waiting. Changes take effect at the next simulation step." },

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.ExitGiveWay)), "Vehicles leaving buildings give way" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.ExitGiveWay)),
                    "On (default): a car, van, truck or bus leaving any building (a drive, a car park or a depot gate) waits at the exit until every road lane it crosses or merges into is clear: no vehicle on that lane, in the junction before it or on the road before that junction would reach the exit before it is out of the way, judged from the approaching vehicles' speed as with the Whole junction gap rule (a vehicle stopped by a red light, or by amber when it can stop, does not count). This works at every exit, boxed or not. At an exit you gave a yellow box, the car must also have room past the box (Cars obey yellow boxes): both must hold. Vehicles on the road are not held for it (apart from the yellow box rules). On-duty emergency vehicles leave as in the game. There is no time limit: an exit onto a road that never clears waits until it does. A vehicle turning into a building across traffic (a right turn in where cars drive on the left) also gives way, judged the same way with its whole length: it waits in its own lane before the yellow box (or, without a box, where it would leave its lane), never across the oncoming lane; a turn in that crosses no other lane does not wait. Off: vehicles leave and turn in as in the game. Works with either junction gap rule, only while Car driving is Junction Rules. Changes take effect at the next simulation step." },

                // JR-T-014d (G11) BEGIN
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.PeopleGiveWayUnmarked)), "People give way at unmarked crossings" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.PeopleGiveWayUnmarked)),
                    "On (default): people crossing a road where there is no zebra and no signal (an unmarked crossing) have no right of way. They wait at the kerb until the approaching cars leave a safe gap: no car on the lanes they cross, or about to turn into them, would reach them before they are across plus one second, judged from the cars' speed and braking distance. Cars do not stop for people waiting at or walking up to an unmarked crossing, but a car always stops for a person already on the road. Zebras and signal crossings work as before. Off: as in the game (cars give way to people at unmarked crossings as at zebras). Only while Car driving is Junction Rules. Changes take effect at the next simulation step." },
                // JR-T-014d (G11) END

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.Status)), "Status" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.Status)), "What runs now, the result of the version check made at game start, and whether the copy can run (set up at the first simulation step). Reopen the options page to refresh." },

                { m_Setting.GetOptionGroupLocaleID(JunctionRulesSettings.kBoxesGroup), "Yellow boxes" },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.ToggleBoxToolBinding)), "Yellow box tool shortcut" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.ToggleBoxToolBinding)), "Keyboard shortcut that opens and closes the yellow box tool (default Ctrl+Y)." },
                { m_Setting.GetBindingKeyLocaleID(JunctionRulesSettings.kToggleBoxToolAction), "Yellow box tool" },
                { m_Setting.GetBindingMapLocaleID(), "Junction Rules" },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.OpenBoxTool)), "Open / close the yellow box tool" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.OpenBoxTool)), "Same as the yellow box button at the top left of the screen and the shortcut. In the tool, point at a road junction, at a building or at the road in front of a building's car exit: what can take a box shows a thin outline of the box it would get (white: click adds the box; orange: click removes it). Right click or Esc leaves the tool. Boxes are saved with the city and painted at once. Junctions: at-grade road junctions with 3 or more road arms (not highways, bridges, tunnels, elevated roads, roundabouts or level crossings). Exits: each car exit of a building (its drive, car park or depot gate) on an at-grade, non-highway road, away from junctions: one box per exit. Where a service building's own road meets a public road, that is a junction. How cars drive at the boxes: see Cars obey yellow boxes below." },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.YellowBoxMarkingMargin)), "Yellow box: gap to road markings (m)" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.YellowBoxMarkingMargin)), "How far every yellow box edge stays clear of zebra stripes and stop / give-way lines, measured from the painted edge of the marking (default 0.5 m). Exit boxes keep the same gap from junction mouths. Boxes are redrawn at once." },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.HideMarkingsUnderBoxes)), "Hide road markings under yellow boxes" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.HideMarkingsUnderBoxes)), "On: the road's own markings inside a yellow box (lane lines, centre lines, arrows, painted speed numbers) are not drawn, so only the box hatch shows. Markings outside the box stay; on a divided road the box stops at the median, so the far side keeps its markings. Removing a box brings its markings back. Only the drawing changes: nothing is saved with the city and driving is not affected. Off (default): markings show through the box as before." },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.BoxObey)), "Cars obey yellow boxes" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.BoxObey)),
                    "On (default): at a junction or building car exit you gave a yellow box, a car does not drive into the box unless there is room for its whole length (trailers included) plus the room margin on its exit beyond the box. A queue that keeps moving counts as room (the same measure as Cars never stop on a zebra); a queue that stands does not. Cars already in the box drive on. Once a car may enter (its exit is not blocked), it drives as at any other junction: with the Whole junction gap rule it drives up to just before its first crossing with live traffic (lanes held by a red light do not count) and waits there, inside the box if that is where the crossing is; if its exit fills up while it waits there, it stays at that spot until there is room again. Exit blocked: it waits at the stop line (unless the same-exit-lane exception lets it in). At an exit box, cars on the main road do not stop inside the band in front of that exit, and cars leaving the building there wait until there is room past the band on their lane; cars turning in are not held. A building with several boxed exits: each exit's band counts on its own. Only the boxes you placed count. Off: the boxes are paint only. Changes take effect at the next simulation step." },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.BoxLookPast)), "Look past the car in front at a box" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.BoxLookPast)),
                    "On (default): a car about to drive into a yellow box looks at the whole stretch ahead, not only at the car right in front: every vehicle before, in and past the box, how fast they move and the free road beyond them. It expects each vehicle ahead, in and past the box, to keep going and to stop only behind a queue or vehicle it can see standing (crawling slower than about 18 km/h counts as standing), or at a red light, packed nose to tail and never standing in another yellow box, on a zebra (except a signal zebra whose people have red) or in another junction; when that leaves room for its whole length (trailers included) plus the room margin past the box, it drives on instead of pausing at the line for the car in front to clear the box first. It judges only what a driver could see: within 100 m around it (nothing stopped or slow within sight: it goes), in a straight line not blocked by a building, and nothing behind a lorry or bus (that vehicle itself counts with its own speed, with a little room to spare; with a lorry or bus ahead of it in or before the box it judges as with this setting off: it goes when the room past the box is free now); ordinary cars do not block the view. When that is not clear (a standing queue or a red light that leaves no room), it waits at the line as before; it still never stops inside the box for traffic. People on the box's exit zebra: when they are all walking across and the road past the box has room for the car's whole length now and the car fits between the entry zebra and the exit zebra, the car may drive in and wait just before the zebra (never on it); when someone stands on the zebra in the car's way, or there is no room, it waits at the line. Off: a car enters only when the room past the box is free now, and people on the exit zebra are handled as in 1.0.1." },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.NoParkingInBoxes)), "No parking in yellow boxes" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.NoParkingInBoxes)),
                    "On (default): no car parks inside a yellow box you placed (junction or building exit box), even where the road has kerbside parking. Every parking space that overlaps a box is closed the way the game closes spaces in front of a driveway: new cars do not choose it and a car arriving there looks for another space. Cars already parked in the box drive off normally when they leave. Removing a box, or turning this off, opens the spaces again. Nothing extra is saved with the city: a save loaded without the mod has all its parking. Off: cars may park in boxes as before." },
                { m_Setting.GetOptionGroupLocaleID(JunctionRulesSettings.kPaintGroup), "Yellow box paint test (stage 3 probe)" },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.YellowBoxPaintTest)), "Yellow box paint test" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.YellowBoxPaintTest)),
                    "Off (default): nothing runs. On: paints UK yellow boxes on the junctions listed in the test list file (ModsData/JunctionRules/paint-test.json) with the game's own surface paint. Purely visual: nothing is saved in the city, no road or node is changed, cars drive exactly as before. Switch this off and the boxes are gone at once; after deleting the list file press Refresh to clear them while the test is on. If anything fails, the mod log says what and painting stays off until the game restarts. See PROBE-STAGE3-PAINT.md." },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.PaintMode)), "Yellow box paint test: what to draw" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.PaintMode)), "Box (default): border line plus criss-cross lines. Plain fill: the whole box area in plain colour (to see the shape without the lines). Outline only: just the border line (to check it against the kerbs and the arm mouths)." },
                { m_Setting.GetEnumValueLocaleID(BoxPaintMode.Hatch), "Box (border + criss-cross)" },
                { m_Setting.GetEnumValueLocaleID(BoxPaintMode.PlainFill), "Plain fill" },
                { m_Setting.GetEnumValueLocaleID(BoxPaintMode.OutlineOnly), "Outline only" },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.PaintLayers)), "Yellow box paint test: paint onto" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.PaintLayers)), "Which surfaces take the paint. Roads (default): the road surface only. Roads + Other: also poles and signs (seen live 15 Nov 2026); try it only if the box does not show on some junctions or decks. Everything but vehicles: what the G87 junction box surface uses; it can also paint the ground (debug comparison only)." },
                { m_Setting.GetEnumValueLocaleID(BoxPaintLayers.Roads), "Roads" },
                { m_Setting.GetEnumValueLocaleID(BoxPaintLayers.RoadsAndOther), "Roads + Other" },
                { m_Setting.GetEnumValueLocaleID(BoxPaintLayers.AllButVehicles), "Everything but vehicles (G87 value)" },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.PaintRefresh)), "Refresh test list" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.PaintRefresh)), "Read the test list file again and redraw every box (after you edit the file)." },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.PaintAddCameraJunction)), "Add junction at camera centre" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.PaintAddCameraJunction)), "Adds the junction the camera looks at (the nearest junction node, one with 3 or more road arms, within 8 m of the screen centre line, nearest to the camera, so a deck wins over the ground below) to the test list file and paints it. Only writes the list file; nothing in the city changes. The paint test must be on." },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.PaintLogCost)), "Log paint cost now" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.PaintLogCost)), "Writes the paint cost line (draw time per frame, edit check time, rebuilds) to the mod log now. It is also written every 30 seconds while boxes are painted." },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.PaintStatus)), "Paint test status" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.PaintStatus)), "Off, on (how many junctions are painted), or failed (what failed; restart the game to try again). Reopen the options page to refresh." },

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.ExitTrace)), "Exit trace (log)" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.ExitTrace)), "Writes one line to the mod log for each vehicle leaving a building or a parking space: when it starts waiting at the exit and when it joins the road. Each line names the vehicle, the building, the way out (lot, gate, spawn point, building or kerbside parking, building road, drive), the decision (wait or go, and why) and whether the exit has a yellow box. 0.4.8 compact: every notable event (let out without a full judgement, held 25 s+, standing on the road after leaving), a sample of 6 normal events a minute and one summary line a minute; at most 20 lines a second. Needs Vehicles leaving buildings give way and the Junction Rules copy. Default off (developer option). Changes take effect at the next simulation step." },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.DebugTiming)), "Log navigation timing" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.DebugTiming)), "Every 1024 simulation frames, write the timing of the running car-navigation job (game's own or copy, measured the same way) and the simulation rate to the mod log; the copy's line also names the junction gap rule that ran and its stage 2 counters. Off: nothing is measured. Only available when the version check passed at game start." },

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.SkipCodeHashCheck)), "Skip code hash check (probe aid)" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.SkipCodeHashCheck)), "Only if the mod log says the game version matches but the code hash does not: lets the copy run anyway. The game version must still match. Read at game start: restart the game after changing it." },

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.WatchCameraJunction)), "Watch the junction at camera centre (log)" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.WatchCameraJunction)), "Debug: picks the junction the camera looks at (the nearest junction node, one with 3 or more road arms, within 8 m of the screen centre line, nearest to the camera; boxed or not) and writes every car standing there to the mod log: the car, what the game says it waits for (the vehicle or the kind of wait), the lane it is on and which rule holds it (a Junction Rules rule or the game's own). At most one line per car and reason every 512 steps and 20 lines per 256 steps; the rest is counted. Only while the Junction Rules copy drives. Nothing in the city changes. Off until pressed; a load stops it." },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.WatchStop)), "Stop watching the junction" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.WatchStop)), "Stops the junction watch log." },
            };
        }

        public void Unload() { }
    }
}
