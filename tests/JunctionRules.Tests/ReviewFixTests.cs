using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using JunctionRules.Core;
using G = JunctionRules.Core.BoxPaintGeometry;
using CW = JunctionRules.Core.CarriagewayOutline;

namespace JunctionRules.Tests
{
    /// <summary>JR-T-007b-r: review fixes F2-F10 of the 0.4.2 straight-sided boxes and hover outline.</summary>
    internal static partial class Program
    {
        /// <summary>F7: the version, read from the csproj (single source).</summary>
        private static bool ProjectVersion(out string version, out string informational)
        {
            string proj = File.ReadAllText(Path.Combine(RepoRoot(), "JunctionRules", "JunctionRules.csproj"));
            Match v = Regex.Match(proj, @"<Version>(\d+\.\d+\.\d+)</Version>");
            Match i = Regex.Match(proj, @"<InformationalVersion>([^<]+)</InformationalVersion>");
            version = v.Success ? v.Groups[1].Value : null;
            informational = i.Success ? i.Groups[1].Value : null;
            return v.Success && i.Success;
        }

        private static void ReviewFixTests()
        {
            V2 o = new V2(0, 0);
            string src = Path.Combine(RepoRoot(), "JunctionRules");
            string Code(params string[] p) => StripComments(File.ReadAllText(Path.Combine(src, Path.Combine(p))));

            // ---- F2: a corner that cannot clear, or a conflict point outside, = no box (never a box over the pavement).
            {
                // Cross, every arm priority (equal): a conflict point in the pavement of the NE corner (beyond the kerb arc)
                // can only be held inside by an edge over the pavement.
                var arms = Synth(new double[] { 0, 90, 180, 270 }, 8, 20, 6, 3, null);
                var cps = new List<V2> { new V2(9.5, 9.5) };
                var r = CW.BuildStraight(arms, o, null, 0.5, cps, false);
                Check(r.Outline == null && r.Failure != null && r.Failure.StartsWith("no box"), "F2: conflict point only reachable over the pavement: no box (" + r.Failure + ")");
                Check(r.Failure != null && (r.Failure.Contains("corner") || r.Failure.Contains("conflict point")), "F2: failure names the corner / conflict point");
                var ok = CW.BuildStraight(Synth(new double[] { 0, 90, 180, 270 }, 8, 20, 6, 3, null), o, null, 0.5, new List<V2> { new V2(1, 1) }, false);
                Check(ok.Outline != null && ok.Straight.FailingCorners.Count == 0, "F2: a conflict point on the carriageway still builds (" + ok.Failure + ")");
                string shapes = Code("Paint", "YellowBoxPaintSystem.cs");
                Check(shapes.Contains("Mod.log.Warn($\"yellow box paint test: {b.Label}: node {b.Node}: no box: straight-sided outline failed"), "F2: junction shape failure logged at Warn with the node");
                string sb = Code("Core", "StraightBox.cs");
                Check(!sb.Contains("edge left as it is"), "F2: no 'edge left as it is' path any more");
            }

            // ---- F3: no outline and no box = not selectable (Warn once per entity); a boxed target stays removable.
            {
                Check(BoxSelection.Toggle(false, false) == ToggleAction.None && BoxSelection.Toggle(true, false) == ToggleAction.Remove, "F3: invalid without a box = nothing; with a box = remove");
                string tool = Code("Boxes", "YellowBoxToolSystem.cs");
                Check(Regex.IsMatch(tool, @"h\.Segments\.Count == 0 && !m_Hover\.HasBox\)[\s\S]{0,200}m_Hover\.Valid = false;"), "F3: a target with no outline and no box is made invalid (no invisible box saved)");
                Check(tool.Contains("m_NoShapeWarned.Add(m_Hover.Entity)") && tool.Contains("Mod.log.Warn($\"yellow box tool:"), "F3: shape failure warned once per target entity");
                Check(Code("Paint", "YellowBoxPaintSystem.cs").Contains("Mod.log.Warn($\"yellow boxes: {b.Label}: building {b.Building}: not painted"), "F3: entrance rebuild failure logged at Warn");
            }

            // ---- F4: arm yield classification.
            {
                var main = new ArmLaneCounts();
                main.Add(false, true); main.Add(false, true); main.Add(true, false);   // two priority through lanes + one Give Way turn lane
                Check(!main.Yielding, "F4: main-road arm with one yielding turn lane stays priority (" + main.Describe() + ")");
                var minor = new ArmLaneCounts();
                minor.Add(true, true); minor.Add(true, false);
                Check(minor.Yielding, "F4: arm whose through lane yields = yielding");
                var partThrough = new ArmLaneCounts();
                partThrough.Add(true, true); partThrough.Add(false, true);
                Check(!partThrough.Yielding, "F4: only some through lanes yield = priority");
                var stem = new ArmLaneCounts();
                stem.Add(true, false); stem.Add(true, false);
                Check(stem.Yielding, "F4: T stem (no through) with all lanes yielding = yielding");
                var stemHalf = new ArmLaneCounts();
                stemHalf.Add(true, false); stemHalf.Add(false, false);
                Check(!stemHalf.Yielding, "F4: T stem with half its lanes yielding = priority (most = more than half)");
                var stemMost = new ArmLaneCounts();
                stemMost.Add(true, false); stemMost.Add(true, false); stemMost.Add(false, false);
                Check(stemMost.Yielding, "F4: T stem with 2 of 3 yielding = yielding");
                Check(!new ArmLaneCounts().Yielding, "F4: arm with no lanes = priority");
                bool[] y = ArmYield.Classify(new List<ArmLaneCounts> { main, stem, main });
                Check(!y[0] && y[1] && !y[2], "F4: classify a T (main / stem / main)");
                Check(main.Describe() == "3 lane(s), 1 yielding, through 0/2 yielding", "F4: per-arm counts text: " + main.Describe());
                // A T whose main arms each carry one yielding turn lane: still priority-road (only the stem slides), as in 0.4.2 without the turn lanes.
                var t = Synth(new double[] { 0, 90, 180 }, 8, 20, 6, 3, ArmYield.Classify(new List<ArmLaneCounts> { main, stem, main }));
                var tr = CW.BuildStraight(t, o, null, 0.5, null, false);
                Check(tr.Outline != null && tr.Straight.Rule == BoxShapeRule.PriorityRoad && tr.Straight.Shift[0] == 0 && tr.Straight.Shift[2] == 0, "F4: T with Give Way turn lanes on the main arms: priority-road, main mouths stay");
                string shapes = Code("Boxes", "BoxShapes.cs");
                Check(shapes.Contains("CarLaneFlags.Forward") && shapes.Contains("ArmYield.Classify(counts)") && shapes.Contains("counts[k].Describe()"), "F4: BoxShapes counts through lanes, classifies with ArmYield, logs per-arm counts in the arms note");
                Check(!Regex.IsMatch(shapes, @"arms\[best\]\.Yielding = true"), "F4: no 'any yielding lane' rule left");
            }

            // ---- F5: hover solved once per target; re-solved on target / box state / params / source change.
            {
                var c = new HoverCache<string>();
                int n = 0;
                Func<string> solve = () => (++n).ToString();
                var k = new HoverKey(42, 1, false, 0, 8, 0.5);
                c.Get(k, false, solve);
                for (int i = 0; i < 100; i++) c.Get(k, false, solve);
                Check(n == 1, $"F5: same target for 100 frames: solved once ({n})");
                c.Get(new HoverKey(42, 1, true, 0, 8, 0.5), false, solve);
                Check(n == 2, "F5: box added: re-solved");
                c.Get(new HoverKey(42, 1, true, 0, 8, 0.7), false, solve);
                Check(n == 3, "F5: margin changed: re-solved");
                c.Get(new HoverKey(42, 1, true, 0, 10, 0.7), false, solve);
                c.Get(new HoverKey(42, 1, true, 0.2, 10, 0.7), false, solve);
                Check(n == 5, "F5: entrance length / kerb inset changed: re-solved");
                c.Get(new HoverKey(43, 1, true, 0.2, 10, 0.7), false, solve);
                Check(n == 6, "F5: another target: re-solved");
                c.Get(new HoverKey(43, 1, true, 0.2, 10, 0.7), true, solve);
                Check(n == 7, "F5: a source Updated: re-solved");
                c.Reset();
                c.Get(new HoverKey(43, 1, true, 0.2, 10, 0.7), false, solve);
                Check(n == 8 && c.Solves == 8, "F5: reset (tool stopped): re-solved");
                string tool = Code("Boxes", "YellowBoxToolSystem.cs");
                Check(!tool.Contains("kRefreshFrames") && !tool.Contains("m_CacheAge"), "F5: no periodic re-solve");
                Check(Regex.IsMatch(tool, @"OnStopRunning\(\)[\s\S]{0,120}m_Cache\.Reset\(\)") && Regex.IsMatch(tool, @"OnStartRunning\(\)[\s\S]{0,200}m_Cache\.Reset\(\)"), "F5: cache reset when the tool starts and stops");
                Check(tool.Contains("HasComponent<Updated>(e)"), "F5: a source entity Updated forces a re-solve");
            }

            // ---- F6: shape parameters published at mod load / tool start; Options setter keeps a list override.
            {
                var none = BoxShapeParams.From(null, 0.8);
                Check(none.MarkingMargin == 0.8 && none.KerbInset == 0 && none.EntranceLength == 8 && none.MarginSource == "options", "F6: no list file: Options margin, default inset / length");
                var list = PaintTestConfig.Parse("{\"paint\": {\"markingMargin\": 1.2, \"kerbInset\": 0.3, \"entranceLength\": 10}}");
                var p = BoxShapeParams.From(list, 0.5);
                Check(p.MarkingMargin == 1.2 && p.KerbInset == 0.3 && p.EntranceLength == 10, "F6: list file: margin override, inset, length");
                p.ApplySetting(0.9);
                Check(p.MarkingMargin == 1.2 && p.MarginSource.Contains("markingMargin"), "F6: Options slider moved: the list override still wins");
                var noOverride = BoxShapeParams.From(PaintTestConfig.Parse("{\"paint\": {}}"), 0.5);
                noOverride.ApplySetting(0.9);
                Check(noOverride.MarkingMargin == 0.9, "F6: no list override: the slider applies");
                string settings = Code("Settings", "JunctionRulesSettings.cs");
                Check(settings.Contains("BoxStore.SetMarginSetting(value)") && !settings.Contains("Effective(value, double.NaN"), "F6: Options setter goes through BoxStore (keeps the list override)");
                Check(Code("Mod.cs").Contains("BoxStore.LoadParams(\"mod load\")"), "F6: parameters published at mod load");
                Check(Code("Boxes", "YellowBoxToolSystem.cs").Contains("BoxStore.LoadParams(\"tool start\")"), "F6: parameters published at tool start");
                string paint = Code("Paint", "YellowBoxPaintSystem.cs");
                Check(paint.Contains("BoxStore.PublishParams(p)") && paint.Contains("BoxShapeParams.From(m_Config, setting)"), "F6: the paint system publishes its list read");
                Check(Code("Boxes", "BoxStore.cs").Contains("if (PaintPublished) return;"), "F6: tool start keeps the paint system's read once it published");
            }

            // ---- F8: rule wording.
            {
                var r = new StraightBoxResult { Rule = BoxShapeRule.FallbackCannotClear };
                Check(r.RuleCode == "priority-moved" && r.RuleText.StartsWith("priority-moved (priority arms moved: yielding arms alone cannot clear)"), "F8: rule code + wording: " + r.RuleText);
                Check(new StraightBoxResult { Rule = BoxShapeRule.PriorityRoad }.RuleCode == "priority-road" && new StraightBoxResult { Rule = BoxShapeRule.FallbackEqual }.RuleCode == "fallback-equal", "F8: other codes unchanged");
                Check(!Code("Core", "StraightBox.cs").Contains("fallback-cannot-clear"), "F8: old label gone");
            }

            // ---- F9: full 64-bit lane end keys (0.4.2 folded PathNode to 32 bits).
            {
                // ulong.GetHashCode = low ^ high: owner 1 / lane 0 and owner 0 / lane 1 collide there.
                Check(((ulong)1 << 32).GetHashCode() == ((ulong)1).GetHashCode(), "F9: (the 0.4.2 fold collides for these two)");
                Check(LanePath.Key(1, 0, 0, false) != LanePath.Key(0, 1, 0, false), "F9: full keys differ");
                bool exact = true;
                foreach (int q in new[] { 0, 1, 16383, 16384, 32766, 32767 })
                {
                    float cp = q * 3.051851E-05f;   // PathNode.GetCurvePos
                    long want = (long)(((ulong)7 << 32) | ((ulong)q << 16) | 0x0203);
                    if (LanePath.Key(7, 0x0203, cp, false) != want) exact = false;
                }
                Check(exact, "F9: key rebuilds the game's PathNode layout (owner, curve position, segment, lane)");
                Check(LanePath.Key(7, 3, 0.5f, true) != LanePath.Key(7, 3, 0.5f, false), "F9: secondary bit kept");
                Check(Code("Boxes", "BoxShapes.cs").Contains("Key(lane.m_StartNode), Key(lane.m_EndNode)") && !Code("Boxes", "BoxShapes.cs").Contains("m_StartNode.GetHashCode()"), "F9: BoxShapes uses the full key");
            }

            // ---- F10: junction stop lines only from this node's half of each arm edge.
            {
                string shapes = Code("Boxes", "BoxShapes.cs");
                Check(shapes.Contains("StopLines(em, e, markings, em.GetComponentData<Edge>(e).m_Start == node)") && shapes.Contains("nearStart.Value ? t > 0.5f : t < 0.5f"), "F10: junction reads stop lines on its own half of each arm");
            }
        }
    }
}
