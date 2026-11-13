using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    internal static class Program
    {
        private static int s_Failed;
        private static int s_Passed;

        private static void Check(bool condition, string name)
        {
            if (condition) { s_Passed++; }
            else { s_Failed++; Console.WriteLine("FAIL: " + name); }
        }

        // Test groups in run order. Args filter by case-insensitive substring of the name; --list prints names.
        private static readonly (string Name, Action Run)[] s_Groups =
        {
            ("VersionGateTests", VersionGateTests),
            ("DrivingSwitchTests", DrivingSwitchTests),
            ("TimingWindowTests", TimingWindowTests),
            ("IlHasherTests", IlHasherTests),
            ("VanillaHeaderTests", VanillaHeaderTests),
            ("DiffScriptSelfTest", DiffScriptSelfTest),
        };

        private static int Main(string[] args)
        {
            var filters = new List<string>();
            foreach (string a in args)
            {
                if (a == "--list")
                {
                    foreach (var g in s_Groups) { Console.WriteLine(g.Name); }
                    return 0;
                }
                filters.Add(a);
            }
            var selected = new List<(string Name, Action Run)>();
            foreach (var g in s_Groups)
            {
                if (filters.Count == 0 || filters.Exists(f => g.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)) { selected.Add(g); }
            }
            foreach (string f in filters)
            {
                if (!Array.Exists(s_Groups, g => g.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    Console.WriteLine($"no test group matches '{f}' (use --list)");
                    return 2;
                }
            }
            foreach (var g in selected) { g.Run(); }
            var ran = new List<string>();
            foreach (var g in selected) { ran.Add(g.Name); }
            Console.WriteLine($"groups ({selected.Count}/{s_Groups.Length}): {string.Join(", ", ran)}");
            Console.WriteLine($"{s_Passed} passed, {s_Failed} failed");
            return s_Failed == 0 ? 0 : 1;
        }

        // ---- version gate ----

        private const string V = "1.6.4f1 (1097.158a)";
        private const string H = "5a15f3b9a5f9b2db5795ae1fc4626ddabf9eb432ac19f3fd71f2b54ab5577d32";

        private static void VersionGateTests()
        {
            GateResult r = VersionGate.Evaluate(V, V, H, H, false);
            Check(r.Verdict == GateVerdict.Pass && r.CopyAllowed, "same version + same hash passes");

            r = VersionGate.Evaluate(V, "1.6.5f1 (1100.1aa)", H, H, false);
            Check(r.Verdict == GateVerdict.VersionMismatch && !r.CopyAllowed, "other version blocks");
            r = VersionGate.Evaluate(V, "1.6.5f1 (1100.1aa)", H, H, true);
            Check(r.Verdict == GateVerdict.VersionMismatch && !r.CopyAllowed, "skip-hash never skips the version check");
            r = VersionGate.Evaluate(V, "1.6.4f1 (1097.159b)", H, H, false);
            Check(!r.CopyAllowed, "same short version, other changeset blocks");
            Check(!VersionGate.Evaluate(V, null, H, H, true).CopyAllowed, "null version blocks");
            Check(!VersionGate.Evaluate(V, "", H, H, true).CopyAllowed, "empty version blocks");

            r = VersionGate.Evaluate(V, V, H, "00" + H.Substring(2), false);
            Check(r.Verdict == GateVerdict.CodeMismatch && !r.CopyAllowed, "hash mismatch blocks");
            r = VersionGate.Evaluate(V, V, H, "00" + H.Substring(2), true);
            Check(r.Verdict == GateVerdict.PassHashSkipped && r.CopyAllowed, "hash mismatch with skip allows");
            Check(!VersionGate.Evaluate(V, V, H, null, false).CopyAllowed, "missing hash blocks");
            Check(!VersionGate.Evaluate(V, V, "PENDING", H, false).CopyAllowed, "placeholder expected hash blocks");

            r = VersionGate.Failed("boom");
            Check(r.Verdict == GateVerdict.Error && !r.CopyAllowed && r.Message.Contains("boom"), "failed check blocks and says why");

            Check(MadeFrom.GameVersion == V, "MadeFrom version is the one the copy was made from");
            Check(MadeFrom.VanillaCodeHash.Length == 64, "MadeFrom hash is a sha256 hex string");
        }

        // ---- driving switch ----

        private static readonly GateResult s_Open = VersionGate.Evaluate(V, V, H, H, false);
        private static readonly GateResult s_Closed = VersionGate.Evaluate(V, "9.9.9", H, H, false);

        private static void DrivingSwitchTests()
        {
            foreach (CarDrivingMode m in Enum.GetValues(typeof(CarDrivingMode)))
            {
                Check(PairFlags.For(m).IsValid, "flags valid for " + m);
            }
            Check(PairFlags.For(CarDrivingMode.GameOwn).GameNavigation && PairFlags.For(CarDrivingMode.GameOwn).GameActions, "game own runs the game pair");
            Check(PairFlags.For(CarDrivingMode.JunctionRulesCopy).CopyNavigation && !PairFlags.For(CarDrivingMode.JunctionRulesCopy).GameNavigation, "copy runs only the copy pair");
            // Every invalid shape is rejected (both, neither, nav without its actions).
            int valid = 0;
            for (int i = 0; i < 16; i++)
            {
                var f = new PairFlags((i & 1) != 0, (i & 2) != 0, (i & 4) != 0, (i & 8) != 0);
                if (f.IsValid) valid++;
            }
            Check(valid == 2, "exactly two of 16 flag shapes are valid");

            Check(DrivingSwitch.Effective(CarDrivingMode.JunctionRulesCopy, false) == CarDrivingMode.GameOwn, "gate closed forces game own");
            Check(DrivingSwitch.Effective(CarDrivingMode.JunctionRulesCopy, true) == CarDrivingMode.JunctionRulesCopy, "gate open allows copy");
            Check(DrivingSwitch.Effective(CarDrivingMode.GameOwn, true) == CarDrivingMode.GameOwn, "game own stays game own");

            var s = new DrivingSwitch();
            Check(s.Step(CarDrivingMode.GameOwn, s_Open, out PairFlags flags, out string note), "first step always applies");
            Check(flags.GameNavigation && note != null && note.Contains("starts"), "first step logs start");
            Check(!s.Step(CarDrivingMode.GameOwn, s_Open, out flags, out note) && note == null, "no change: nothing to do, no log");
            Check(s.Step(CarDrivingMode.JunctionRulesCopy, s_Open, out flags, out note) && flags.CopyNavigation && flags.IsValid, "switch to copy applies");
            Check(note.Contains("switched to Junction Rules copy") && s.Switches == 1, "switch logged and counted");
            Check(s.Applied == CarDrivingMode.JunctionRulesCopy, "applied mode tracked");
            Check(!s.Step(CarDrivingMode.JunctionRulesCopy, s_Open, out flags, out note), "copy steady: nothing to do");
            Check(s.Step(CarDrivingMode.GameOwn, s_Open, out flags, out note) && flags.GameNavigation && s.Switches == 2, "switch back applies");

            // Many alternating steps: every step's flags are valid and match the request.
            var t = new DrivingSwitch();
            bool allValid = true;
            int applied = 0;
            for (int i = 0; i < 1000; i++)
            {
                CarDrivingMode want = (i / 7) % 2 == 0 ? CarDrivingMode.GameOwn : CarDrivingMode.JunctionRulesCopy;
                if (t.Step(want, s_Open, out flags, out note)) applied++;
                allValid &= flags.IsValid && (flags.CopyNavigation == (want == CarDrivingMode.JunctionRulesCopy));
            }
            Check(allValid, "alternating requests: flags always valid and as requested");
            Check(applied == 143 && t.Switches == 142, $"alternating requests: applied only on change (applied {applied}, switches {t.Switches})");

            // Gate closed: copy requested -> game own, warned once.
            var g = new DrivingSwitch();
            Check(g.Step(CarDrivingMode.JunctionRulesCopy, s_Closed, out flags, out note) && flags.GameNavigation && !flags.CopyNavigation, "blocked copy runs game own");
            Check(note != null && note.Contains("blocked"), "blocked copy warns");
            Check(!g.Step(CarDrivingMode.JunctionRulesCopy, s_Closed, out flags, out note) && note == null, "blocked warning only once");
            Check(g.Applied == CarDrivingMode.GameOwn && g.Switches == 0, "blocked: no switch counted");

            // Reset (new world) re-applies on the next step.
            s.Reset();
            Check(!s.HasApplied && s.Step(CarDrivingMode.GameOwn, s_Open, out flags, out note) && note.Contains("starts"), "reset re-applies");
        }

        // ---- timing window ----

        private static void TimingWindowTests()
        {
            var w = new TimingWindow("test", 100);
            const long f = 1000; // ticks per second
            Check(w.Due(5), "first tick is due (starts the window)");
            Check(w.Tick(5, 0, f, 10) == null, "first tick starts the window");
            Check(!w.Due(50) && w.Tick(50, 500, f, 10) == null, "mid-window: not due, no line");
            Check(w.Due(105) && w.Tick(105, 1000, f, 10) == null, "empty window is quiet");
            for (int i = 1; i <= 20; i++) w.Add(i);
            w.Skip();
            Check(!w.Due(204), "not due one frame early");
            string line = w.Tick(205, 3000, f, 1234);
            Check(line != null, "full window gives a line");
            Check(line != null && line.Contains("frames 105-205") && line.Contains("100 sim frames in 2.00 s = 50.0 frames/s"), "line has frame span and rate: " + line);
            Check(line != null && line.Contains("cars 1234") && line.Contains("over 20 runs") && line.Contains("mean 10.500"), "line has cars, runs, mean");
            Check(line != null && line.Contains("p50 10.000") && line.Contains("p95 19.000") && line.Contains("max 20.000") && line.Contains("(skipped 1)"), "line has percentiles and skips");
            Check(w.Count == 0 && w.Skipped == 0, "window restarts after a line");

            w.Skip();
            string onlySkips = w.Tick(400, 4000, f, 0);
            Check(onlySkips != null && onlySkips.Contains("no navigation runs sampled (skipped 1)"), "skips-only window is reported");

            // Frame counter going backwards (new game loaded): restart, no line.
            w.Add(1);
            Check(w.Due(10) && w.Tick(10, 5000, f, 0) == null && w.Count == 0, "frame going back restarts the window");

            var sorted = new List<double> { 1, 2, 3, 4 };
            Check(TimingWindow.Percentile(sorted, 0.5) == 2 && TimingWindow.Percentile(sorted, 0.95) == 4 && TimingWindow.Percentile(sorted, 0) == 1, "nearest-rank percentiles");
            Check(TimingWindow.Percentile(new List<double>(), 0.5) == 0, "empty percentile is 0");
            Check(new TimingWindow("x", 0).Due(0), "zero window size clamps to 1");
        }

        // ---- IL hasher ----

        private static void IlHasherTests()
        {
            string a1 = IlHasher.Hash(typeof(FixtureA));
            string a2 = IlHasher.Hash(typeof(FixtureA));
            Check(a1 == a2 && a1.Length == 64, "hash is deterministic sha256 hex");
            Check(a1 != IlHasher.Hash(typeof(FixtureB)), "different constant: different hash");
            Check(a1 != IlHasher.Hash(typeof(FixtureC)), "different call: different hash");
            Check(a1 != IlHasher.Hash(typeof(FixtureA), typeof(FixtureB)), "two types hash differs from one");
            Check(IlHasher.Hash(typeof(FixtureA), typeof(FixtureB)) == IlHasher.Hash(typeof(FixtureA), typeof(FixtureB)), "multi-type deterministic");
            Check(a1 != IlHasher.Hash(typeof(FixtureD)), "nested type change shows in outer hash");
            string d = IlHasher.Describe(typeof(FixtureD));
            Check(d.Contains("Inner") && d.Contains("Twice"), "description covers nested types and members");
            Check(d.Contains("System.Math"), "operands resolved to names, not tokens");
            var each = IlHasher.HashEach(typeof(FixtureA), typeof(FixtureB));
            Check(each.Count == 2 && each[0].Value == a1, "per-type hashes listed");
        }

        // ---- repo checks ----

        private static string RepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "tools", "vanilla_rules.json"))) dir = Path.GetDirectoryName(dir);
            return dir;
        }

        private static void VanillaHeaderTests()
        {
            string root = RepoRoot();
            Check(root != null, "repo root found");
            if (root == null) return;
            string[] files = Directory.GetFiles(Path.Combine(root, "JunctionRules", "Vanilla"), "*.cs");
            Check(files.Length == 2, "two copied files in Vanilla/");
            foreach (string f in files)
            {
                string text = File.ReadAllText(f);
                string n = Path.GetFileName(f);
                Check(text.StartsWith("// ==== COPIED GAME CODE: Junction Rules"), n + ": header first");
                Check(text.Contains("Source: Game.dll 1.6.4f1 (1097.158a)"), n + ": header names the game version");
                Check(text.Contains("// Original type: Game.Simulation."), n + ": header names the original type");
                Check(text.Contains("namespace JunctionRules.Vanilla;"), n + ": moved namespace");
                Check(!text.Contains("namespace Game.Simulation"), n + ": no game namespace left");
            }
            // Copied game code lives only in Vanilla/: no other source file declares a "COPIED GAME CODE" header.
            bool stray = false;
            foreach (string f in Directory.GetFiles(Path.Combine(root, "JunctionRules"), "*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) || f.Contains(Path.DirectorySeparatorChar + "Vanilla" + Path.DirectorySeparatorChar)) continue;
                if (File.ReadAllText(f).Contains("COPIED GAME CODE")) stray = true;
            }
            Check(!stray, "no copied game code outside Vanilla/");
        }

        private static void DiffScriptSelfTest()
        {
            string root = RepoRoot();
            if (root == null) { Check(false, "repo root found"); return; }
            var psi = new ProcessStartInfo("python3", "tools/vanilla_copy.py selftest")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using Process p = Process.Start(psi);
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            Check(p.ExitCode == 0, "vanilla_copy.py selftest exits 0: " + output.Trim());
        }
    }

    // Fixtures for the IL hasher.
    internal static class FixtureA { public static int F(int x) => x * 3 + 1; }
    internal static class FixtureB { public static int F(int x) => x * 3 + 2; }
    internal static class FixtureC { public static int F(int x) => Math.Abs(x) * 3 + 1; }
    internal static class FixtureD
    {
        public static int F(int x) => x * 3 + 1;
        internal static class Inner { public static int Twice(int x) => Math.Max(x, 0) * 2; }
    }
}
