using System;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    internal static partial class Program
    {
        // The game's VehicleUtils braking formulas (Game.Vehicles.VehicleUtils, decompile 1.6.4f1), for the "full stop is
        // never above the creep" property the no-push rule relies on.
        private static float GameMaxBrakingSpeed(float braking, float distance, float timeStep)
        {
            float num = timeStep * braking;
            return MathF.Max(0f, MathF.Sqrt(MathF.Max(0f, num * num + 2f * braking * distance)) - num);
        }

        private static float GameMaxBrakingSpeed(float braking, float distance, float maxResultSpeed, float timeStep)
        {
            float num = timeStep * braking;
            return MathF.Max(0f, MathF.Sqrt(MathF.Max(0f, num * num + 2f * braking * distance + maxResultSpeed * maxResultSpeed)) - num);
        }

        private static void CrossingPushTests()
        {
            // The game's own branch choice in CheckPedestrian.
            Check(CrossingPush.Pedestrian(true, true, 2f) == CrossingPush.PushReason.Timer, "5 s timer: creep even past a walking person");
            Check(CrossingPush.Pedestrian(true, false, 0f) == CrossingPush.PushReason.Timer, "timer wins over standing (one reason per person)");
            Check(CrossingPush.Pedestrian(false, false, 0f) == CrossingPush.PushReason.Standing, "no Moving component (collapsed accident victim): creep at once");
            Check(CrossingPush.Pedestrian(false, true, 0.009f) == CrossingPush.PushReason.Standing, "slower than 0.1 m/s: standing");
            Check(CrossingPush.Pedestrian(false, true, 0.01f) == CrossingPush.PushReason.None, "0.1 m/s exactly: walking (the game's >= 0.01)");
            Check(CrossingPush.Pedestrian(false, true, float.NaN) == CrossingPush.PushReason.Standing, "NaN speed stays on the creep side, as the game's negated test");
            Check(CrossingPush.Pedestrian(false, true, 1.5f) == CrossingPush.PushReason.None, "walking person, no timer: the game stops anyway");

            // The speed after the rule.
            Check(CrossingPush.PedestrianSpeed(false, CrossingPush.PushReason.Timer, 3f, 0f) == 3f, "setting off: the game's creep stays");
            Check(CrossingPush.PedestrianSpeed(true, CrossingPush.PushReason.None, 2f, 0f) == 2f, "no push reason: the game's value stays");
            Check(CrossingPush.PedestrianSpeed(true, CrossingPush.PushReason.Timer, 3f, 0f) == 0f, "timer: full stop instead of 3 m/s");
            Check(CrossingPush.PedestrianSpeed(true, CrossingPush.PushReason.Standing, 3.5f, 1.2f) == 1.2f, "standing: braking to stop instead of the creep");
            Check(CrossingPush.PedestrianSpeed(true, CrossingPush.PushReason.Timer, 1f, 2f) == 1f, "never raises the game's limit");

            // Full stop <= creep for every distance, braking and time step the game uses (so the rule only lowers).
            bool lower = true;
            foreach (float b in new[] { 2f, 4f, 8f, 12f })
                foreach (float t in new[] { 4f / 15f, 4f / 15f + 0.5f, 4f / 15f + 1.5f })
                    for (float d = -2f; d < 60f; d += 0.37f)
                    {
                        float full = GameMaxBrakingSpeed(b, d, t);
                        float creep = MathF.Max(3f, GameMaxBrakingSpeed(b, d, 3f, t));
                        if (full > creep) lower = false;
                    }
            Check(lower, "game formulas: full-stop speed never above the creep speed");
            Check(GameMaxBrakingSpeed(8f, 0f, 4f / 15f) == 0f && MathF.Max(3f, GameMaxBrakingSpeed(8f, 0f, 3f, 4f / 15f)) >= 3f,
                "at the stop point: stop 0 m/s, the game's creep at least 3 m/s (the push-through)");

            // Cyclists on a crossing: only the car's ignored blocker, only a bicycle, only on a pedestrian lane.
            Check(CrossingPush.RefuseIgnore(true, true, true, true), "ignored cyclist on a crossing: creep refused");
            Check(!CrossingPush.RefuseIgnore(false, true, true, true), "setting off: stock");
            Check(!CrossingPush.RefuseIgnore(true, false, true, true), "not the ignored blocker: nothing to refuse");
            Check(!CrossingPush.RefuseIgnore(true, true, false, true), "car on the lane (not a bicycle): car against car stays stock");
            Check(!CrossingPush.RefuseIgnore(true, true, true, false), "bicycle on a road lane: stock");

            // Mode bit: independent of the rule, never part of the stage 2 mode the 0.2.x code tests.
            Check(Stage2Mode.NoPushPeople == 32 && (Stage2Mode.NoPushPeople & (Stage2Mode.Rule | Stage2Mode.Count | Stage2Mode.AtLine | Stage2Mode.TurnGivesWay | Stage2Mode.LeftHandTraffic)) == 0,
                "own bit, no overlap with the stage 2 bits");
            Check(Stage2Mode.For(JunctionGapRule.Stock, false, noPushPeople: true) == Stage2Mode.NoPushPeople, "stock rule + setting: only the no-push bit");
            Check((Stage2Mode.For(JunctionGapRule.Stock, false, noPushPeople: true) & ~Stage2Mode.NoPushPeople) == 0, "stock rule + setting: stage 2 mode stays 0 after stripping");
            Check(Stage2Mode.For(JunctionGapRule.WholeJunction, true, JunctionWaitingSpot.BeforeFirstCrossing, true, true, true) == (1 | 2 | 8 | 16 | 32), "all bits");
            Check(Stage2Mode.For(JunctionGapRule.Stock, false) == 0, "setting not passed: unchanged (0)");
            Check(Stage2Mode.RuleName(Stage2Mode.NoPushPeople) == "stock; cars never push through people on crossings", "rule name with the setting");
            Check(Stage2Mode.RuleName(Stage2Mode.Rule) == "whole junction", "rule name without the setting unchanged");

            var sw = new GapRuleSwitch();
            string first = sw.Step(JunctionGapRule.Stock, false, out int m0, noPushPeople: true);
            Check(m0 == Stage2Mode.NoPushPeople && first != null && first.Contains("never push through people"), "first step logs the setting");
            Check(sw.Step(JunctionGapRule.Stock, false, out _, noPushPeople: true) == null, "no change: no log");
            string off = sw.Step(JunctionGapRule.Stock, false, out int m1, noPushPeople: false);
            Check(m1 == 0 && off != null && off.Contains("switched to stock") && !off.Contains("never push"), "switching the setting off logs once");

            // Timing line part.
            var dl = new long[Stage2Counters.Count];
            dl[Stage2Counters.CrossingRefused] = 7; dl[Stage2Counters.CrossingRefusedTimer] = 5; dl[Stage2Counters.CrossingRefusedStanding] = 2; dl[Stage2Counters.CrossingRefusedCyclist] = 1;
            string f = Stage2Counters.Format(1, dl, 2);
            Check(f.Contains("; never push through people on crossings on; crossing give-ups refused 7 (5 s push 5, standing person 2, cyclist 1); points at W 0"), "counter text: " + f);
            Check(Stage2Counters.Format(1, null, 3) == "; junction gap rule stock; never push through people on crossings switched in this window", "setting reported without counters");
            Check(Stage2Counters.Format(1, null) == "; junction gap rule stock", "no push mask: line unchanged");
            Check(Stage2Counters.CrossingRefusedCyclist == Stage2Counters.Count - 1, "new counters at the end (earlier indices unchanged)");
        }

        private static void CrossingRepoTests()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string dec = Path.Combine(home, "Code", "decomp", "yb-2026-11-13", "full");
            string itPath = Path.Combine(dec, "Game.Simulation", "CarLaneSpeedIterator.cs");
            string navPath = Path.Combine(dec, "Game.Simulation", "CarNavigationSystem.cs");
            string colPath = Path.Combine(dec, "Game.Simulation", "CreatureCollisionIterator.cs");
            string humPath = Path.Combine(dec, "Game.Simulation", "HumanNavigationSystem.cs");
            if (File.Exists(itPath) && File.Exists(navPath) && File.Exists(colPath) && File.Exists(humPath))
            {
                string it = File.ReadAllText(itPath);
                Check(it.Contains("float position = ((m_PushBlockers || !m_MovingData.TryGetComponent(obj, out var componentData2) || !(math.lengthsq(componentData2.m_Velocity) >= 0.01f)) ? math.max(3f, VehicleUtils.GetMaxBrakingSpeed(m_PrefabCar, distanceOffset, 3f, m_SafeTimeStep)) : VehicleUtils.GetMaxBrakingSpeed(m_PrefabCar, distanceOffset, m_SafeTimeStep));"),
                    "stock CheckPedestrian creep formula (CrossingPush.Pedestrian mirrors it)");
                Check(CountOf(it, "m_PushBlockers") == 2, "m_PushBlockers: the field and its one read in CheckPedestrian");
                Check(CountOf(it, "BlockerType.Temporary") == 1, "blocker type Temporary only from CheckPedestrian (people)");
                Check(CountOf(it, "CheckPedestrian(") == 2, "CheckPedestrian: one call (overlapping lanes) and its definition");
                Check(it.Contains("maxBrakingSpeed = math.select(maxBrakingSpeed, 3f, ignore && maxBrakingSpeed < 3f);"), "stock ignore-blocker creep in UpdateMaxSpeed");
                string nav = File.ReadAllText(navPath);
                Check(nav.Contains("if (currentLane.m_Duration >= 5f)\n\t\t\t\t{\n\t\t\t\t\tcurrentLane.m_LaneFlags |= Game.Vehicles.CarLaneFlags.PushBlockers;"), "stock 5 s PushBlockers timer");
                Check(nav.Contains("if (num11 != flag2 || currentLane.m_Duration >= 30f)"), "lane duration restarts when the Temporary state changes (ApplySideEffects)");
                Check(nav.Contains("currentLane.m_LaneFlags |= Game.Vehicles.CarLaneFlags.IgnoreBlocker;") && nav.Contains("if (!m_MovingData.HasComponent(laneIterator.m_Blocker))"),
                    "stock IgnoreBlocker only after IsBlocked, which needs a blocker without Moving");
                string col = File.ReadAllText(colPath);
                Check(!col.Contains("CarData") && !col.Contains("CarLane"), "people do not look at cars (CreatureCollisionIterator reads no car data)");
                string hum = File.ReadAllText(humPath);
                Check(CountOf(hum, "currentLane.m_Flags |= CreatureLaneFlags.WaitSignal;") == 1, "people stop for signals only (one WaitSignal site, from LaneSignal)");
            }
            else
            {
                Console.WriteLine("note: decompile not found, crossing pins not checked");
            }
            string root = RepoRoot();
            if (root == null) { Check(false, "repo root found"); return; }
            string iter = File.ReadAllText(Path.Combine(root, "JunctionRules", "Vanilla", "JRCarLaneSpeedIterator.cs"));
            int open = iter.IndexOf("if (m_JR.m_NoPushPeople) m_MaxSpeed = float.MaxValue;", StringComparison.Ordinal);
            int call = iter.IndexOf("CheckPedestrian(overlapLine, laneObject.m_LaneObject, laneObject.m_CurvePosition.y, num5, giveSpace, inverse);", StringComparison.Ordinal);
            int fix = iter.IndexOf("if (m_JR.m_NoPushPeople) m_JR.NoPushPedestrian(ref m_MaxSpeed, ref m_Blocker, ref m_BlockerType, jrPedMax, jrPedBlocker, jrPedType, laneObject.m_LaneObject, num5, m_PushBlockers,", StringComparison.Ordinal);
            int restore = iter.IndexOf("num5 = jrPedNum5;", StringComparison.Ordinal);
            Check(open > 0 && call > open && fix > call && restore > fix, "S2-08 opens the limit, the game's check, S2-08b refuses the creep with the same distance, then restores num5");
            int save = iter.IndexOf("Entity jrIgnore = m_Ignore;", StringComparison.Ordinal);
            int upd = iter.IndexOf("UpdateMaxSpeed(laneObject.m_LaneObject, blockerType, objectSpeed, curvePosition.x, distanceFactor, num5, num4,", StringComparison.Ordinal);
            int back = iter.IndexOf("m_Ignore = jrIgnore;", StringComparison.Ordinal);
            Check(save > 0 && upd > save && back > upd, "S2-11 hides a crossing cyclist from m_Ignore for the one UpdateMaxSpeed call, S2-11b restores it");
            string job = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            Check(job.Contains("j.m_NoPushPeople = (mode & Stage2Mode.NoPushPeople) != 0;") && job.Contains("mode &= ~Stage2Mode.NoPushPeople;"), "Create strips the setting bit from the stage 2 mode");
            string set = File.ReadAllText(Path.Combine(root, "JunctionRules", "Settings", "JunctionRulesSettings.cs"));
            Check(set.Contains("NoPushThroughPeople = true;") && set.Contains("\"Cars never push through people on crossings\""), "setting defaults on, owner's label");
            string sys = File.ReadAllText(Path.Combine(root, "JunctionRules", "Systems", "CarDrivingSwitchSystem.cs"));
            Check(sys.Contains("Mod.Settings == null || Mod.Settings.NoPushThroughPeople"), "switch system hands the setting over (default on)");
        }

        private static int CountOf(string text, string what)
        {
            int n = 0, i = 0;
            while ((i = text.IndexOf(what, i, StringComparison.Ordinal)) >= 0) { n++; i += what.Length; }
            return n;
        }
    }
}
