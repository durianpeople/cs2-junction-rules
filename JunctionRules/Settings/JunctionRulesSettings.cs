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

        /// <summary>
        /// 0.4.2 (JR-T-010, owner rule 16 Nov 2026, UK yellow box): a car does not enter a box the owner placed (junction or
        /// entrance) unless there is room for its whole length past it; default on; off = 0.4.1 driving (Core/BoxDriving.cs).
        /// Acts while the copy drives; next simulation step.
        /// </summary>
        [SettingsUISection(kSection, kBoxesGroup)]
        public bool BoxObey { get; set; }

        /// <summary>The right-turn exception is greyed out while the box rule is off.</summary>
        public bool BoxTurnWaitDisabled() => !BoxObey;

        /// <summary>0.4.2: UK exception: a car turning across oncoming traffic may enter and wait in the box (default on).</summary>
        [SettingsUIDisableByCondition(typeof(JunctionRulesSettings), nameof(BoxTurnWaitDisabled))]
        [SettingsUISection(kSection, kBoxesGroup)]
        public bool BoxTurnWait { get; set; }

        // ---- stage 3 probe (0.3.0-probe): yellow box paint test, draw-only, default off ----

        private bool m_YellowBoxPaintTest;
        private BoxPaintMode m_PaintMode;
        private BoxPaintLayers m_PaintLayers = PaintSettingsMigration.Default;

        /// <summary>
        /// Stage 3 probe: paints UK yellow boxes on the junctions listed in ModsData/JunctionRules/paint-test.json with
        /// the game's surface shader. Draw-only (nothing saved, no entity touched, no driving change). Default off; off =
        /// the paint system does not run at all.
        /// </summary>
        [SettingsUISection(kSection, kPaintGroup)]
        public bool YellowBoxPaintTest
        {
            get => m_YellowBoxPaintTest;
            set { m_YellowBoxPaintTest = value; PaintControl.ApplySettings(); }
        }

        /// <summary>What the paint test draws: hatch (border + diagonals), plain fill, or outline only.</summary>
        [SettingsUISection(kSection, kPaintGroup)]
        public BoxPaintMode PaintMode
        {
            get => m_PaintMode;
            set { m_PaintMode = value; PaintControl.ApplySettings(); }
        }

        /// <summary>Decal layers the paint goes onto: Roads (2, default) or Roads + Other (34), or 39 like G87 (debug).</summary>
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

        /// <summary>Re-reads the test list file and rebuilds every box.</summary>
        [SettingsUIButton]
        [SettingsUISection(kSection, kPaintGroup)]
        public bool PaintRefresh { set { PaintControl.Refresh(); } }

        /// <summary>Adds the junction node at the camera centre to the test list file (read-only pick; writes only the list file).</summary>
        [SettingsUIButton]
        [SettingsUISection(kSection, kPaintGroup)]
        public bool PaintAddCameraJunction { set { PaintControl.AddCameraJunction(); } }

        /// <summary>Writes the paint cost line to the log now.</summary>
        [SettingsUIButton]
        [SettingsUISection(kSection, kPaintGroup)]
        public bool PaintLogCost { set { PaintControl.LogCost(); } }

        /// <summary>Read-only: what the paint test does now.</summary>
        [SettingsUISection(kSection, kPaintGroup)]
        public string PaintStatus => PaintControl.Status();

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
            TurnAcrossGivesWay = true;
            NoPushThroughPeople = true;
            PeoplePassOn = true;
            PeoplePassSeconds = PeoplePass.DefaultSeconds;
            ZebraNonBlocking = true;
            ZebraRoomMargin = RoomScan.DefaultMargin;
            BoxObey = true;
            BoxTurnWait = true;
            YellowBoxPaintTest = false;
            PaintMode = BoxPaintMode.Hatch;
            PaintLayers = PaintSettingsMigration.Default;
            DebugTiming = false;
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

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.TurnAcrossGivesWay)), "Whole junction: turning across oncoming traffic gives way" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.TurnAcrossGivesWay)),
                    "Only with the Whole junction rule. On (default): where neither direction has a sign or signal priority over the other, a car turning across the oncoming traffic (right turns where cars drive on the left, left turns where they drive on the right) gives way to the oncoming cars going straight on, and an oncoming car going straight on does not wait just because the turner has reserved its way through; it still brakes for a turner that is physically in its path. Off: the game's own priority. Changes take effect at the next simulation step." },

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.NoPushThroughPeople)), "Cars never push through people on crossings" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.NoPushThroughPeople)),
                    "On (default): a car waiting for people on a crossing of its path (a zebra, a crossing at a junction, a footway across a driveway), or for a cyclist riding across it, keeps waiting until they are clear, however long that takes. Off: the game's own behaviour, where after about 5 seconds the car creeps on at walking pace through the people (and creeps at once past a person standing still). Cars waiting for other cars are not changed. Works with either junction gap rule, only while Car driving is the Junction Rules copy. Changes take effect at the next simulation step." },

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.PeoplePassOn)), "People pass a car that blocks them" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.PeoplePassOn)),
                    "On (default): a person or cyclist held on a crossing by a vehicle standing across it goes on through the vehicle after the wait set below. Off: exactly the game's behaviour (they wait as long as the vehicle stands), for comparison. Works with either Car driving setting. Changes take effect at the next simulation step." },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.PeoplePassSeconds)), "People pass a car that blocks a crossing after (s)" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.PeoplePassSeconds)),
                    "A person walking a crossing (a zebra, a crossing at a junction, a footway across a driveway) who is held by a car or other vehicle standing on it walks on through the vehicle after this many seconds; a cyclist riding the crossing likewise rides on through it at walking pace. Default 2 seconds. Used only while People pass a car that blocks them is on; 0 also switches the pass off. Cars still never push through people (setting above). Works with either Car driving setting. Changes take effect at the next simulation step." },

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.ZebraNonBlocking)), "Cars never stop on a zebra" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.ZebraNonBlocking)),
                    "On (default): a car drives onto a zebra only when it can also leave it. The zebra must be clear of people, there must be room for the whole vehicle (trailers included) past it in the queue ahead, and with the Whole junction rule every crossing point with other lanes after the zebra up to the next zebra (or the end of the junction) must be clear too; it waits before the zebra until then. Where the whole vehicle cannot wait between two zebras (or between a zebra and the crossing points before it), it waits before the earlier one until everything up to the later one is clear. Applies to every zebra, also mid-block ones, with either junction gap rule (the Stock rule gets the room check only). Off: the 0.4.0 behaviour. Only while Car driving is the Junction Rules copy. Changes take effect at the next simulation step." },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.ZebraRoomMargin)), "Room margin past a zebra or yellow box (m)" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.ZebraRoomMargin)),
                    "Extra space, on top of the vehicle's own length, that must be free past a zebra (setting Cars never stop on a zebra) or past a yellow box (setting Cars obey yellow boxes) before a car drives onto it. A moving queue counts the distance the car ahead needs to stop (the game's own measure), so a queue that keeps moving lets cars in. Default 1 m. Larger: fewer tails left on a zebra, more waiting. Changes take effect at the next simulation step." },

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.Status)), "Status" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.Status)), "What runs now, the result of the version check made at game start, and whether the copy can run (set up at the first simulation step). Reopen the options page to refresh." },

                { m_Setting.GetOptionGroupLocaleID(JunctionRulesSettings.kBoxesGroup), "Yellow boxes" },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.ToggleBoxToolBinding)), "Yellow box tool shortcut" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.ToggleBoxToolBinding)), "Keyboard shortcut that opens and closes the yellow box tool (default Ctrl+Y)." },
                { m_Setting.GetBindingKeyLocaleID(JunctionRulesSettings.kToggleBoxToolAction), "Yellow box tool" },
                { m_Setting.GetBindingMapLocaleID(), "Junction Rules" },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.OpenBoxTool)), "Open / close the yellow box tool" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.OpenBoxTool)), "Same as the yellow box button at the top left of the screen and the shortcut. In the tool, point at a road junction or at the road in front of a building's access: what can take a box glows (yellow: click adds a box; orange: click removes it). Right click or Esc leaves the tool. Boxes are saved with the city and painted at once. Junctions: at-grade road junctions with 3 or more road arms (not highways, bridges, tunnels, elevated roads, roundabouts or level crossings). Entrances: a building's road access on an at-grade, non-highway road, away from junctions. How cars drive at the boxes: see Cars obey yellow boxes below." },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.BoxObey)), "Cars obey yellow boxes" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.BoxObey)),
                    "On (default): at a junction or building entrance you gave a yellow box, a car does not drive into the box unless there is room for its whole length (trailers included) plus the room margin on its exit beyond the box. A queue that keeps moving counts as room (the same measure as Cars never stop on a zebra); a queue that stands does not. Cars already in the box drive on. With the Whole junction gap rule, cars also take their give-way decision at the box's stop line instead of inside the box. At an entrance box, cars on the main road do not stop inside the band in front of the access, and cars leaving the driveway wait until there is room past the band on their lane; cars turning into the driveway are not held. Only the boxes you placed count. Off: cars drive as in 0.4.1 (the boxes are paint only). Changes take effect at the next simulation step." },
                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.BoxTurnWait)), "Yellow boxes: turning across oncoming traffic may wait in the box" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.BoxTurnWait)),
                    "On (default, as UK law): a car turning across oncoming traffic (right in a left-hand traffic city, left in a right-hand traffic city) may drive into the box and wait there for a gap when its exit has room and only oncoming traffic stops it (with the Whole junction gap rule it waits just before the oncoming lanes; never on a zebra). Off: it waits at the stop line like everyone else. With the Stock gap rule the game's own give-ways decide where cars wait, so this switch changes nothing there." },
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

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.DebugTiming)), "Log navigation timing" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.DebugTiming)), "Every 1024 simulation frames, write the timing of the running car-navigation job (game's own or copy, measured the same way) and the simulation rate to the mod log; the copy's line also names the junction gap rule that ran and its stage 2 counters. Off: nothing is measured. Only available when the version check passed at game start." },

                { m_Setting.GetOptionLabelLocaleID(nameof(JunctionRulesSettings.SkipCodeHashCheck)), "Skip code hash check (probe aid)" },
                { m_Setting.GetOptionDescLocaleID(nameof(JunctionRulesSettings.SkipCodeHashCheck)), "Only if the mod log says the game version matches but the code hash does not: lets the copy run anyway. The game version must still match. Read at game start: restart the game after changing it." },
            };
        }

        public void Unload() { }
    }
}
