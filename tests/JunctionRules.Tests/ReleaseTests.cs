using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    internal static partial class Program
    {
        /// <summary>
        /// 1.0.0 (first public release): diagnostics migration, developer-only settings, and the Paradox Mods publish
        /// configuration (template schema, version in step with the csproj, images present and the right shape).
        /// </summary>
        private static void ReleaseTests()
        {
            // ---- diagnostics migration: never switches a log on ----
            bool t = false, x = false;
            Check(DiagnosticsMigration.Migrate(0, ref t, ref x, out bool ch) && !t && !x && !ch, "fresh install (schema 0): due, both logs stay off, nothing changed");
            t = true; x = false;
            Check(DiagnosticsMigration.Migrate(0, ref t, ref x, out ch) && t && !x && !ch, "schema 0 with a saved on (pre-0.4.8 own choice): kept");
            t = true; x = true;
            Check(DiagnosticsMigration.Migrate(1, ref t, ref x, out ch) && !t && !x && ch, "0.4.8-0.4.12 probe (schema 1, switched on): both off once");
            t = false; x = false;
            Check(DiagnosticsMigration.Migrate(1, ref t, ref x, out ch) && !t && !x && !ch, "probe schema with both already off: due, nothing changed");
            t = true; x = true;
            Check(!DiagnosticsMigration.Migrate(2, ref t, ref x, out ch) && t && x && !ch, "current schema: not due, a developer's own on is kept");
            Check(!DiagnosticsMigration.Migrate(7, ref t, ref x, out ch) && t && x, "newer schema: not due");
            for (int v = 0; v <= 3; v++)
                for (int i = 0; i < 4; i++)
                {
                    bool a = (i & 1) != 0, b = (i & 2) != 0, a0 = a, b0 = b;
                    DiagnosticsMigration.Migrate(v, ref a, ref b, out _);
                    if ((a && !a0) || (b && !b0)) Check(false, $"schema {v}: a log was switched on");
                }

            string root = RepoRoot();
            string src = Path.Combine(root, "JunctionRules");
            string settings = File.ReadAllText(Path.Combine(src, "Settings", "JunctionRulesSettings.cs"));
            string mod = File.ReadAllText(Path.Combine(src, "Mod.cs"));

            // ---- developer-only settings: every paint-test and debug member carries [SettingsUIDeveloper] ----
            var members = Regex.Matches(settings, @"((?:[ \t]*\[[^\]\r\n]*\][^\r\n]*\r?\n)+)[ \t]*public [^\r\n]*?(\w+)\s*(?:\{|=>)");
            int dev = 0;
            var seen = new HashSet<string>();
            foreach (Match m in members)
            {
                string attrs = m.Groups[1].Value, name = m.Groups[2].Value;
                if (m.Value.Contains(" class ")) continue;
                bool devGroup = attrs.Contains("kPaintGroup)]") || attrs.Contains("kDebugGroup)]");
                if (devGroup) { dev++; seen.Add(name); }
                if (devGroup && !attrs.Contains("[SettingsUIDeveloper]")) Check(false, $"developer setting {name} is visible to players");
                if (!devGroup && attrs.Contains("[SettingsUIDeveloper]")) Check(false, $"player setting {name} is hidden");
            }
            foreach (string n in new[] { "YellowBoxPaintTest", "PaintMode", "PaintLayers", "PaintRefresh", "PaintAddCameraJunction", "PaintLogCost", "PaintStatus",
                                         "DebugTiming", "ExitTrace", "SkipCodeHashCheck", "WatchCameraJunction", "WatchStop" })
                Check(seen.Contains(n), $"developer-only: {n}");
            Check(dev == 12, $"12 developer-only members ({dev})");
            Check(settings.Contains("DebugTiming = false;") && settings.Contains("ExitTrace = false;") && settings.Contains("SkipCodeHashCheck = false;") && settings.Contains("YellowBoxPaintTest = false;"),
                "developer options default off");
            Check(!Regex.IsMatch(settings, @"DebugTiming\s*=\s*true|ExitTrace\s*=\s*true"), "nothing switches a diagnostics log on");
            Check(settings.Contains("[SettingsUIHidden]\n        public int DiagnosticsVersion") || settings.Contains("[SettingsUIHidden]\r\n        public int DiagnosticsVersion"), "diagnostics schema stays a hidden saved setting (probe settings files still load)");
            Check(settings.Contains("public const int DiagnosticsCurrent = DiagnosticsMigration.Current;") && mod.Contains("Settings.MigrateDiagnostics()"), "the migration runs at load");
            // player-visible text carries no probe / stage / version-history wording
            int locStart = settings.IndexOf("public class LocaleEN", StringComparison.Ordinal);
            string loc = settings.Substring(locStart);
            int idx = 0, playerKeys = 0;
            var loclines = loc.Split('\n');
            for (int i = 0; i < loclines.Length; i++)
            {
                string l = loclines[i];
                bool devLine = Regex.IsMatch(l, @"\b(YellowBoxPaintTest|PaintMode|PaintLayers|PaintRefresh|PaintAddCameraJunction|PaintLogCost|PaintStatus|DebugTiming|ExitTrace|SkipCodeHashCheck|WatchCameraJunction|WatchStop|kPaintGroup|kDebugGroup|BoxPaintMode|BoxPaintLayers)\b");
                // a description on the next line belongs to the key on this one
                if (devLine && i + 1 < loclines.Length && loclines[i + 1].TrimStart().StartsWith("\"")) { i++; continue; }
                if (devLine) continue;
                if (l.Contains("GetOption")) playerKeys++;
                if (Regex.IsMatch(l, @"""[^""]*(probe|stage \d|\b0\.\d+\.\d+\b|JR-T-|G\d\d?\b)[^""]*""", RegexOptions.IgnoreCase))
                {
                    idx++;
                    Check(false, "player-visible text has probe/stage/version wording: " + l.Trim().Substring(0, Math.Min(140, l.Trim().Length)));
                }
            }
            Check(idx == 0 && playerKeys > 20, "player-visible option texts are plain (no probe / stage / version history)");

            // ---- publish configuration (template schema) ----
            string props = Path.Combine(src, "Properties");
            XDocument cfg = XDocument.Load(Path.Combine(props, "PublishConfiguration.xml"));
            XElement r = cfg.Root;
            Check(r.Name.LocalName == "Publish", "publish config root <Publish>");
            string[] templateOrder = { "ModId", "DisplayName", "ShortDescription", "LongDescription", "Thumbnail", "Screenshot", "Tag", "ForumLink", "ModVersion", "GameVersion", "Dependency", "RequiredDLC", "ChangeLog", "ExternalLink" };
            var names = r.Elements().Select(e => e.Name.LocalName).Distinct().ToList();
            Check(names.SequenceEqual(templateOrder), "publish config: the template's elements in the template's order: " + string.Join(",", names));
            string Val(string n) => r.Element(n)?.Attribute("Value")?.Value;
            Check(Val("ModId") == "" || Regex.IsMatch(Val("ModId") ?? "", @"^\d+$"), "ModId empty (first upload) or the number Paradox gave");
            Check(Val("DisplayName") == "Junction Rules", "DisplayName");
            Check(!string.IsNullOrWhiteSpace(Val("ShortDescription")) && !Val("ShortDescription").Contains("\n"), "ShortDescription: one line");
            string longDesc = r.Element("LongDescription")?.Value ?? "";
            Check(longDesc.Contains("Getting started") && longDesc.Contains("Yellow boxes") && longDesc.Contains("https://github.com/durianpeople/cs2-junction-rules"), "LongDescription from the README, with getting started and the source link");
            Check(!Regex.IsMatch(longDesc, @"probe|JR-T-|\bG\d\d?\b", RegexOptions.IgnoreCase), "LongDescription has no probe wording");
            Check(r.Elements("Tag").Any(e => e.Attribute("Value")?.Value == "Code Mod"), "Tag Code Mod");
            Check(Val("ForumLink") == "https://github.com/durianpeople/cs2-junction-rules", "ForumLink = GitHub");
            Check(r.Elements("ExternalLink").Any(e => e.Attribute("Type")?.Value == "github" && e.Attribute("Url")?.Value == "https://github.com/durianpeople/cs2-junction-rules"), "ExternalLink github");
            Check(ProjectVersion(out string ver, out string info) && Val("ModVersion") == ver && info == ver, $"ModVersion {Val("ModVersion")} = csproj Version {ver} = InformationalVersion {info} (release: no suffix)");
            Check(Val("GameVersion") == "1.6.*", "GameVersion 1.6.* (game uploader format; the copy was made from 1.6.4)");
            Check(r.Elements("Dependency").All(e => string.IsNullOrEmpty(e.Attribute("Id")?.Value)) && r.Elements("RequiredDLC").All(e => string.IsNullOrEmpty(e.Attribute("Value")?.Value)), "no dependencies, no DLC");
            Check(!string.IsNullOrWhiteSpace(Val("ChangeLog")), "ChangeLog set (NewVersion needs it)");

            // ---- images ----
            bool Png(string rel, out int w, out int h)
            {
                w = h = 0;
                string p = Path.Combine(src, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(p)) return false;
                byte[] b = File.ReadAllBytes(p);
                if (b.Length < 24 || b[0] != 0x89 || b[1] != 0x50 || b[2] != 0x4E || b[3] != 0x47) return false;
                w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
                h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
                return true;
            }
            Check(Png(Val("Thumbnail"), out int tw, out int th) && tw == th && tw >= 256, $"thumbnail: square PNG ({tw}x{th})");
            var shots = r.Elements("Screenshot").Select(e => e.Attribute("Value")?.Value).Where(v => !string.IsNullOrEmpty(v)).ToList();
            Check(shots.Count >= 3 && shots.Count <= 5, $"3-5 screenshots ({shots.Count})");
            foreach (string s in shots) Check(Png(s, out int sw, out int sh) && sw >= 1000 && sw > sh, $"screenshot {s}: landscape PNG ({sw}x{sh})");

            // ---- publish profiles (template schema + Burst) and the csproj targets ----
            foreach (var (file, cmd) in new[] { ("PublishNewMod", "Publish"), ("PublishNewVersion", "NewVersion"), ("UpdatePublishedConfiguration", "Update") })
            {
                string p = Path.Combine(props, "PublishProfiles", file + ".pubxml");
                XDocument d = File.Exists(p) ? XDocument.Load(p) : null;
                XElement g = d?.Root?.Element("PropertyGroup");
                string P(string n) => g?.Element(n)?.Value;
                Check(g != null && P("ModPublisherCommand") == cmd && P("Configuration") == "Release" && P("PublishProtocol") == "FileSystem" && P("_TargetId") == "Folder"
                      && P("TargetFramework") == "net472" && P("Burst") == "true" && P("PublishDir") != null, $"profile {file}: ModPublisherCommand {cmd}, Release, net472, Burst");
            }
            string proj = File.ReadAllText(Path.Combine(src, "JunctionRules.csproj"));
            Check(proj.Contains("Name=\"JRPublishToParadoxMods\" AfterTargets=\"Publish\"") && proj.Contains("$(ModPublisherCommand) \"$(JRPublishConfigFullPath)\" -c \"$(JRPublishContentDir)\" -v"),
                "publish target runs ModPublisher <command> <config> -c <content> -v (as Mod.targets)");
            Check(proj.Contains("--sandbox") && proj.Contains("Not supported in release mode"), "dry run: sandbox flag stops ModPublisher before login");
            Check(!proj.Contains("Mod.targets\"") && !Regex.IsMatch(proj, @"<Import[^>]*Mod\.(props|targets)"), "the toolchain's Mod.props / Mod.targets are not imported (they would redeploy the live Mods folder)");
            Check(proj.Contains("_win_x86_64.dll") && Regex.IsMatch(proj, @"<ModPostProcessorPath Condition=""'\$\(ModPostProcessorPath\)' == ''"">\$\(CSII_MODPOSTPROCESSORPATH\)"),
                "Burst content uploaded; post-processor path visible when the profile sets Burst");
        }
    }
}
