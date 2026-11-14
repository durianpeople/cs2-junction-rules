using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>0.3.0-probe (JR-T-003b): source checks that the yellow box paint test is draw-only, off by default and isolated from driving.</summary>
    internal static partial class Program
    {
        private static void PaintRepoTests()
        {
            string root = RepoRoot();
            Check(root != null, "repo root found");
            if (root == null) return;
            string src = Path.Combine(root, "JunctionRules");
            string paintDir = Path.Combine(src, "Paint");
            Check(Directory.Exists(paintDir), "Paint folder exists");
            if (!Directory.Exists(paintDir)) return;

            // Draw-only: no structural change or component write anywhere in the paint code.
            var writes = new Regex(@"\b(AddComponent|AddComponentData|SetComponentData|SetComponent|RemoveComponent|CreateEntity|DestroyEntity|Instantiate|AddBuffer|SetBuffer<|SetSharedComponent|EntityCommandBuffer|CommandBuffer|CreationDefinition|GetComponentLookup\(false|GetBufferLookup\(false|GetRefRW|ToolSystem|activeTool|Serialize)\b");
            var offenders = new List<string>();
            foreach (string f in Directory.GetFiles(paintDir, "*.cs"))
            {
                string code = StripComments(File.ReadAllText(f));
                foreach (Match m in writes.Matches(code)) offenders.Add(Path.GetFileName(f) + ": " + m.Value);
                if (Regex.IsMatch(code, @"GetBuffer<[^>]+>\(\s*[\w.]+\s*\)")) offenders.Add(Path.GetFileName(f) + ": GetBuffer without isReadOnly");
            }
            Check(offenders.Count == 0, "paint code is draw-only (no entity writes, no tools): " + string.Join("; ", offenders));

            string sys = StripComments(File.ReadAllText(Path.Combine(paintDir, "YellowBoxPaintSystem.cs")));
            Check(Regex.Matches(sys, @"beginContextRendering \+=").Count == 1 && Regex.Matches(sys, @"beginContextRendering -=").Count == 1, "draw callback subscribed in one place and removed in one place");
            Check(sys.Contains("Enabled = false;") && Regex.IsMatch(sys, @"OnCreate\(\)[\s\S]*?Enabled = false;[\s\S]*?ApplySettings\(\);"), "paint system starts disabled; only the setting turns it on");
            Check(Regex.IsMatch(sys, @"private void Deactivate\(string why\)[\s\S]*?Enabled = false;[\s\S]*?Unsubscribe\(\);[\s\S]*?Dispose\(\)"), "switching off disables the system, unsubscribes the draw and releases the GPU objects");
            Check(Regex.Matches(sys, @"catch \(Exception e\)").Count >= 4, "paint update, draw and controls are wrapped");

            // JR-T-003b-r: version gate, road-node-only picks, no overwrite of an unreadable list.
            Check(Regex.IsMatch(sys, @"want && !GameVersionOk\(\)") && sys.Contains("GateVerdict.VersionMismatch") == false && sys.Contains("v == GateVerdict.Pass"), "paint test starts only when the version gate does not say VersionMismatch/Error");
            Check(Regex.IsMatch(sys, @"All = new\[\] \{ ComponentType\.ReadOnly<Game\.Net\.Node>\(\), ComponentType\.ReadOnly<ConnectedEdge>\(\), ComponentType\.ReadOnly<Game\.Net\.Road>\(\) \}"), "node query is road nodes only");
            Check(sys.Contains("RoadEdgeCount(ents[n]) >= 3") && sys.Contains("not overwriting it"), "camera pick uses junction nodes (3+ road edges); an unreadable list file is never overwritten");

            // Isolation: only Mod.cs (registration, in try/catch) and the settings (through PaintControl) name the paint code.
            var named = new List<string>();
            foreach (string f in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(src, f).Replace(Path.DirectorySeparatorChar, '/');
                if (rel.StartsWith("obj/") || rel.StartsWith("bin/") || rel.StartsWith("Paint/")) continue;
                string code = StripComments(File.ReadAllText(f));
                if (code.Contains("YellowBoxPaintSystem") || code.Contains("PaintGpu")) named.Add(rel);
                if ((code.Contains("PaintRegistration") || code.Contains("PaintControl")) && rel != "Mod.cs" && rel != "Settings/JunctionRulesSettings.cs") named.Add(rel);
            }
            Check(named.Count == 0, "paint types named only through PaintRegistration (Mod.cs) / PaintControl (settings): " + string.Join(", ", named));
            string mod = StripComments(File.ReadAllText(Path.Combine(src, "Mod.cs")));
            Check(Regex.IsMatch(mod, @"try\s*\{\s*PaintRegistration\.Register\(updateSystem\);\s*\}\s*catch"), "Mod.cs registers the paint test inside its own try/catch");
            Check(mod.IndexOf("PaintRegistration.Register", StringComparison.Ordinal) > mod.IndexOf("UpdateBefore<CarDrivingSwitchSystem", StringComparison.Ordinal), "paint test registered after the driving systems");
            foreach (string f in Directory.GetFiles(Path.Combine(src, "Vanilla"), "*.cs").Concat(Directory.GetFiles(Path.Combine(src, "Stage2"), "*.cs")))
                Check(!File.ReadAllText(f).Contains("Paint"), Path.GetFileName(f) + " does not reference the paint test");

            string settings = StripComments(File.ReadAllText(Path.Combine(src, "Settings", "JunctionRulesSettings.cs")));
            Check(Regex.IsMatch(settings, @"SetDefaults\(\)[\s\S]*?YellowBoxPaintTest = false;"), "Yellow box paint test defaults to off");
            Check(settings.Contains("PaintLayers = BoxPaintLayers.Roads;") && settings.Contains("PaintMode = BoxPaintMode.Hatch;"), "paint defaults: hatch, Roads layer");
            Check((int)BoxPaintLayers.Roads == 2 && (int)BoxPaintLayers.RoadsAndOther == 34 && (int)BoxPaintLayers.AllButVehicles == 39, "decal layer values match Game.Rendering.DecalLayers");

            string csproj = File.ReadAllText(Path.Combine(src, "JunctionRules.csproj"));
            Check(csproj.Contains("<Version>0.3.0</Version>") && csproj.Contains("<InformationalVersion>0.3.0-probe</InformationalVersion>"), "version 0.3.0-probe");

            string kit = Path.Combine(RepoRoot(), "PROBE-STAGE3-PAINT.md");
            Check(File.Exists(kit), "probe kit PROBE-STAGE3-PAINT.md exists");
            if (File.Exists(kit))
            {
                string k = File.ReadAllText(kit);
                foreach (string step in new[] { "| S6 |", "| S7 |", "| S8 |", "| S9 |", "| S10 |", "| S11 |" })
                    Check(k.Contains(step), "probe kit has step " + step.Trim('|', ' '));
                Check(k.Contains("paint-test.json") && k.Contains("FAILED while") && k.Contains("paint cost over"), "probe kit names the list file, the failure line and the cost line");
                Check(k.Contains("0000000000000000000000000000000000000000000000000000000000000000  JunctionRules.dll") || !File.Exists(Path.Combine(RepoRoot(), ".staged", "0.3.0-probe", "STALE.md")), "probe kit install sums stay placeholders while the staged build is stale");
            }
        }
    }
}
