using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    internal static partial class Program
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
            ("VersionGateCheckTests", VersionGateCheckTests),
            ("DrivingSwitchTests", DrivingSwitchTests),
            ("CopyAcquisitionTests", CopyAcquisitionTests),
            ("CopyIsolationTests", CopyIsolationTests),
            ("TimingWindowTests", TimingWindowTests),
            ("IlHasherTests", IlHasherTests),
            ("VanillaHeaderTests", VanillaHeaderTests),
            ("DiffScriptSelfTest", DiffScriptSelfTest),
            ("WholePathRuleTests", WholePathRuleTests),
            ("WholePathScenarioTests", WholePathScenarioTests),
            ("Stage2RulingTests", Stage2RulingTests),
            ("Stage2SwitchTests", Stage2SwitchTests),
            ("Stage2CountersTests", Stage2CountersTests),
            ("Stage2RepoTests", Stage2RepoTests),
            ("StretchSpotTests", StretchSpotTests),
            ("StretchDecisionTests", StretchDecisionTests),
            ("StretchRootCauseTests", StretchRootCauseTests),
            ("StretchWholePathTests", StretchWholePathTests),
            ("StretchDeadlockTests", StretchDeadlockTests),
            ("StretchRepoTests", StretchRepoTests),
            ("VehicleLengthTests", VehicleLengthTests),
            ("ArrivalTimeTests", ArrivalTimeTests),
            ("ArrivalProjectionTests", ArrivalProjectionTests),
            ("SafetyEventTests", SafetyEventTests),
            ("ArrivalRepoTests", ArrivalRepoTests),
            ("FeedingSignalTests", FeedingSignalTests),
            ("SignalRepoTests", SignalRepoTests),
            ("FeedingScanTests", FeedingScanTests),
            ("TurnAcrossTests", TurnAcrossTests),
            ("TurnAcrossRepoTests", TurnAcrossRepoTests),
            ("CrossingPushTests", CrossingPushTests),
            ("CrossingRepoTests", CrossingRepoTests),
            ("MoonflowerTests", MoonflowerTests),
            ("CrossingPointTests", CrossingPointTests),
            ("ClearPointTests", ClearPointTests),
            ("Tech9RepoTests", Tech9RepoTests),
            ("BoxOutlineTests", BoxOutlineTests),
            ("StraightBoxLiveTests", StraightBoxLiveTests),
            ("ExitTests", ExitTests),
            ("StraightBoxRuleTests", StraightBoxRuleTests),
            ("G5Tests", G5Tests),
            ("G5MergeTests", G5MergeTests),
            ("MarginHoverTests", MarginHoverTests),
            ("ReviewFixTests", ReviewFixTests),
            ("BoxInsetTests", BoxInsetTests),
            ("BoxHatchTests", BoxHatchTests),
            ("BoxRectHeightTests", BoxRectHeightTests),
            ("BoxCarriagewayTests", BoxCarriagewayTests),
            ("BoxThroughSideTests", BoxThroughSideTests),
            ("BoxSelectionTests", BoxSelectionTests),
            ("BoxRecordTests", BoxRecordTests),
            ("EntranceBoxTests", EntranceBoxTests),
            ("PaintMigrationTests", PaintMigrationTests),
            ("PaintConfigTests", PaintConfigTests),
            ("PaintRepoTests", PaintRepoTests),
            ("PeoplePassTests", PeoplePassTests),
            ("PeoplePassRepoTests", PeoplePassRepoTests),
            ("ZebraSectionTests", ZebraSectionTests),
            ("ZebraRoomTests", ZebraRoomTests),
            ("ZebraEarlyTests", ZebraEarlyTests),
            ("ZebraFurtherTests", ZebraFurtherTests),
            ("ZebraSettingTests", ZebraSettingTests),
            ("ZebraRepoTests", ZebraRepoTests),
            ("BoxJunctionTests", BoxJunctionTests),
            ("BoxTurnTests", BoxTurnTests),
            ("BoxEntranceTests", BoxEntranceTests),
            ("BoxSettingTests", BoxSettingTests),
            ("BoxRepoTests", BoxRepoTests),
            ("G3TurnSpotRoomTests", G3TurnSpotRoomTests),
            ("G3CounterTests", G3CounterTests),
            ("G3RepoTests", G3RepoTests),
            ("WatchTests", WatchTests),
            ("WatchRepoTests", WatchRepoTests),
            ("ScanOrderTests", ScanOrderTests),
            ("ZebraGapTests", ZebraGapTests),
            ("BoxEdgeSetTests", BoxEdgeSetTests),
            ("BoxExitBandTests", BoxExitBandTests),
            ("BendExitTests", BendExitTests),
            ("MarkingMaskTests", MarkingMaskTests),
            ("MarkingMergeTests", MarkingMergeTests),
            ("MarkingRepoTests", MarkingRepoTests),
            ("MarkingLedgerTests", MarkingLedgerTests),
            ("BoxMergeTests", BoxMergeTests),
            ("MedianSplitTests", MedianSplitTests),
            ("BoxMergeRepoTests", BoxMergeRepoTests),
            ("ExitLayoutTests", ExitLayoutTests),
            ("ExitGapTests", ExitGapTests),
            ("ExitDecisionTests", ExitDecisionTests),
            ("ExitAreaTests", ExitAreaTests),
            ("ExitEventTests", ExitEventTests),
            ("ExitPriorityTests", ExitPriorityTests),
            ("ExitModeTests", ExitModeTests),
            ("ExitRepoTests", ExitRepoTests),
            ("ExitPathTests", ExitPathTests),
            ("ExitCapTests", ExitCapTests),
            ("ExitTraceTests", ExitTraceTests),
            ("ExitG1RepoTests", ExitG1RepoTests),
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

        // ---- version gate: check order (version first, hash in its own try/catch) ----

        private static void VersionGateCheckTests()
        {
            int hashCalls = 0;
            Func<string> goodHash = () => { hashCalls++; return H; };
            Func<string> otherHash = () => { hashCalls++; return "00" + H.Substring(2); };
            Func<string> throwingHash = () => { hashCalls++; throw new InvalidOperationException("hash boom"); };

            GateResult r = VersionGate.Check(V, () => V, H, goodHash, false);
            Check(r.Verdict == GateVerdict.Pass && hashCalls == 1, "check: same version + same hash passes");
            r = VersionGate.Check(V, () => V, H, otherHash, false);
            Check(r.Verdict == GateVerdict.CodeMismatch && !r.CopyAllowed, "check: hash mismatch blocks");

            // Version mismatch: never hashes, never allows, skip or not.
            hashCalls = 0;
            r = VersionGate.Check(V, () => "1.6.5f1 (1100.1aa)", H, goodHash, false);
            Check(r.Verdict == GateVerdict.VersionMismatch && !r.CopyAllowed, "check: other version blocks");
            r = VersionGate.Check(V, () => "1.6.5f1 (1100.1aa)", H, goodHash, true);
            Check(r.Verdict == GateVerdict.VersionMismatch && !r.CopyAllowed, "check: skip-hash never skips the version check");
            r = VersionGate.Check(V, () => "1.6.5f1 (1100.1aa)", H, throwingHash, true);
            Check(r.Verdict == GateVerdict.VersionMismatch && !r.CopyAllowed, "check: version mismatch with a throwing hash still VersionMismatch");
            Check(!VersionGate.Check(V, () => null, H, goodHash, true).CopyAllowed, "check: null version blocks");
            Check(hashCalls == 0, $"check: version mismatch never computes the hash (calls {hashCalls})");

            // Version read throws: fail safe.
            r = VersionGate.Check(V, () => throw new TypeLoadException("version boom"), H, goodHash, true);
            Check(r.Verdict == GateVerdict.Error && !r.CopyAllowed && r.Message.Contains("version boom"), "check: version read exception fails safe");

            // Hash throws after a version match: counts as a hash mismatch.
            r = VersionGate.Check(V, () => V, H, throwingHash, false);
            Check(r.Verdict == GateVerdict.CodeMismatch && !r.CopyAllowed, "check: hash exception after version match = code mismatch");
            Check(r.Message.Contains("could not be computed") && r.Message.Contains("hash boom"), "check: hash exception named in the message");
            r = VersionGate.Check(V, () => V, H, throwingHash, true);
            Check(r.Verdict == GateVerdict.PassHashSkipped && r.CopyAllowed, "check: hash exception + skip hash check allows the copy");
            r = VersionGate.Check(V, () => V, H, () => throw new TypeLoadException("x"), true);
            Check(r.Verdict == GateVerdict.PassHashSkipped, "check: TypeLoadException while hashing is caught too");
        }

        // ---- copy isolation (source check): nothing outside CopyPair names a copy type ----

        private static string StripComments(string text)
        {
            var sb = new System.Text.StringBuilder(text.Length);
            foreach (string line in text.Split('\n'))
            {
                int c = line.IndexOf("//", StringComparison.Ordinal);
                sb.Append(c < 0 ? line : line.Substring(0, c)).Append('\n');
            }
            return sb.ToString();
        }

        private static void CopyIsolationTests()
        {
            string root = RepoRoot();
            Check(root != null, "repo root found");
            if (root == null) return;
            string src = Path.Combine(root, "JunctionRules");
            string[] copyTypeNames = { "JRCarNavigationSystem", "JRCarLaneSpeedIterator", "CopyNavTimingProbeSystem", "GameNavTimingProbeSystem", "JunctionRules.Vanilla",
                "JRWholeJunction", "JRStage2Bridge", "JunctionRules.Stage2" };
            // Files allowed to name copy types: the copy itself, the probe definitions, and CopyPair.
            var offenders = new List<string>();
            foreach (string f in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(src, f).Replace(Path.DirectorySeparatorChar, '/');
                if (rel.StartsWith("obj/") || rel.StartsWith("bin/") || rel.StartsWith("Vanilla/") || rel.StartsWith("Stage2/")) continue;
                if (rel == "Systems/CopyPair.cs" || rel == "Systems/NavTimingProbeSystems.cs") continue;
                string code = StripComments(File.ReadAllText(f));
                foreach (string n in copyTypeNames)
                {
                    if (code.Contains(n)) offenders.Add(rel + " names " + n);
                }
            }
            Check(offenders.Count == 0, "only CopyPair names copy types: " + string.Join("; ", offenders));

            string copyPair = StripComments(File.ReadAllText(Path.Combine(src, "Systems", "CopyPair.cs")));
            int publicStatic = System.Text.RegularExpressions.Regex.Matches(copyPair, @"public static \w+ \w+\(").Count;
            int noInline = System.Text.RegularExpressions.Regex.Matches(copyPair, @"\[MethodImpl\(MethodImplOptions\.NoInlining\)\]\s*public static").Count;
            Check(publicStatic >= 2 && noInline == publicStatic, $"every CopyPair method is NoInlining ({noInline} of {publicStatic})");
            // CopySystems holds the copy only as the ECS base class (no copy type in a field).
            Check(System.Text.RegularExpressions.Regex.Matches(copyPair, @"public ComponentSystemBase \w+;").Count == 4, "CopySystems fields are ComponentSystemBase");

            // Registration and creation happen only behind the gate.
            string mod = StripComments(File.ReadAllText(Path.Combine(src, "Mod.cs")));
            int reg = mod.IndexOf("CopyPair.Register(", StringComparison.Ordinal);
            int gateIf = mod.LastIndexOf("if (Gate.CopyAllowed)", reg < 0 ? 0 : reg, StringComparison.Ordinal);
            Check(reg > 0 && gateIf > 0 && mod.IndexOf("CopyPair.Register(", reg + 1, StringComparison.Ordinal) < 0, "Mod registers the copy once, inside if (Gate.CopyAllowed)");
            string sw = StripComments(File.ReadAllText(Path.Combine(src, "Systems", "CarDrivingSwitchSystem.cs")));
            int create = sw.IndexOf("CopyPair.Create(", StringComparison.Ordinal);
            int regIf = sw.LastIndexOf("if (!m_Acquisition.ShouldAttempt(Mod.CopyRegistered))", create < 0 ? 0 : create, StringComparison.Ordinal);
            Check(create > 0 && regIf > 0 && sw.IndexOf("CopyPair.Create(", create + 1, StringComparison.Ordinal) < 0,
                "switch creates the copy once, only after ShouldAttempt(Mod.CopyRegistered)");

            // JR-T-001d: UpdateBefore creates the switch (runs OnCreate) at registration, so (1) the switch must not get
            // the copy in OnCreate, (2) it gets it in OnUpdate before writing any flag, (3) Mod registers it only after
            // CopyRegistered is final, and never via a separate GetOrCreateSystemManaged.
            int onCreate = sw.IndexOf("protected override void OnCreate()", StringComparison.Ordinal);
            int onUpdate = sw.IndexOf("protected override void OnUpdate()", StringComparison.Ordinal);
            int onCreateEnd = sw.IndexOf("\n        }", onCreate + 1, StringComparison.Ordinal); // end of the method body
            Check(onCreate > 0 && onCreateEnd > onCreate && !sw.Substring(onCreate, onCreateEnd - onCreate).Contains("CopyPair") &&
                  !sw.Substring(onCreate, onCreateEnd - onCreate).Contains("CopyRegistered") && !sw.Substring(onCreate, onCreateEnd - onCreate).Contains("AcquireCopyOnce"),
                "switch OnCreate does not touch the copy");
            int acquireCall = sw.IndexOf("AcquireCopyOnce();", onUpdate < 0 ? 0 : onUpdate, StringComparison.Ordinal);
            int stepCall = sw.IndexOf("m_Switch.Step(", onUpdate < 0 ? 0 : onUpdate, StringComparison.Ordinal);
            int firstEnabled = sw.IndexOf(".Enabled =", onUpdate < 0 ? 0 : onUpdate, StringComparison.Ordinal);
            Check(onUpdate > 0 && acquireCall > onUpdate && stepCall > acquireCall && firstEnabled > stepCall, "switch OnUpdate acquires the copy before the step and before any Enabled write");
            int setRegistered = mod.IndexOf("CopyRegistered = true", StringComparison.Ordinal);
            int switchReg = mod.IndexOf("UpdateBefore<CarDrivingSwitchSystem", StringComparison.Ordinal);
            Check(setRegistered > 0 && switchReg > setRegistered && reg > 0 && switchReg > reg, "Mod registers the switch after CopyRegistered = true and after CopyPair.Register");
            int elseBranch = mod.IndexOf("copy and timing probes not registered", StringComparison.Ordinal);
            Check(elseBranch > 0 && switchReg > elseBranch, "Mod registers the switch after the whole copy block");
            Check(!mod.Contains("GetOrCreateSystemManaged<CarDrivingSwitchSystem>"), "Mod does not create the switch on its own");
            Check(sw.Contains("DrivingSwitch.GateFor(Mod.Gate, m_Copy != null)"), "switch closes the gate when the copy is absent");

            // Leftover copy systems are switched off on every failure path (UpdateAfter/UpdateBefore create systems
            // at registration, and a fresh system is Enabled).
            int regBody = copyPair.IndexOf("public static void Register(", StringComparison.Ordinal);
            int fin = copyPair.IndexOf("finally", regBody < 0 ? 0 : regBody, StringComparison.Ordinal);
            int nextMethod = copyPair.IndexOf("public static", regBody + 1, StringComparison.Ordinal);
            Check(regBody > 0 && fin > regBody && fin < nextMethod && copyPair.IndexOf("DisableAnyExisting(updateSystem.World)", fin, StringComparison.Ordinal) is int d && d > fin && d < nextMethod,
                "Register switches off what it created in a finally");
            foreach (string t in new[] { "JRCarNavigationSystem>", "JRCarNavigationSystem.Actions>", "GameNavTimingProbeSystem>", "CopyNavTimingProbeSystem>" })
            {
                int n = System.Text.RegularExpressions.Regex.Matches(copyPair, @"\[MethodImpl\(MethodImplOptions\.NoInlining\)\]\s*private static bool \w+\(World world\) => Disable\(world\.GetExistingSystemManaged<" + System.Text.RegularExpressions.Regex.Escape(t)).Count;
                Check(n == 1, "DisableAnyExisting has its own NoInlining helper for " + t.TrimEnd('>'));
            }
            Check(System.Text.RegularExpressions.Regex.Matches(copyPair, @"TryDisable\(""[^""]+"", \(\) => Disable\w+\(world\)\)").Count == 4, "DisableAnyExisting wraps each of the 4 helpers in TryDisable");
            int modCatch = mod.IndexOf("catch (Exception e)", reg < 0 ? 0 : reg, StringComparison.Ordinal);
            int modLeft = mod.IndexOf("DisableCopyLeftovers(updateSystem.World", modCatch < 0 ? 0 : modCatch, StringComparison.Ordinal);
            int modElse = mod.IndexOf("else", modCatch < 0 ? 0 : modCatch, StringComparison.Ordinal);
            Check(modCatch > reg && modLeft > modCatch && modLeft < modElse, "Mod switches off leftovers when registration fails");
            int swCatch = sw.IndexOf("catch (System.Exception e)", create < 0 ? 0 : create, StringComparison.Ordinal);
            Check(swCatch > create && sw.IndexOf("Mod.DisableCopyLeftovers(World", swCatch, StringComparison.Ordinal) > swCatch, "switch switches off leftovers when creating the copy fails");
            Check(sw.IndexOf("m_Acquisition.Failed()", swCatch < 0 ? 0 : swCatch, StringComparison.Ordinal) > swCatch, "switch records the failed creation (never retried)");
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

            // Copy not registered (gate closed at load, or registration failed): the switch must keep the game's own.
            Check(!DrivingSwitch.GateFor(s_Closed, false).CopyAllowed, "gate closed, copy absent: closed");
            Check(DrivingSwitch.GateFor(s_Open, true).Verdict == GateVerdict.Pass, "gate open, copy registered: unchanged");
            GateResult absent = DrivingSwitch.GateFor(s_Open, false);
            Check(!absent.CopyAllowed && absent.Verdict == GateVerdict.Error && absent.Message.Contains("not registered"), "gate open but copy absent: closed, says why");
            var a = new DrivingSwitch();
            Check(a.Step(CarDrivingMode.JunctionRulesCopy, absent, out flags, out note) && flags.GameNavigation && !flags.CopyNavigation && flags.IsValid, "copy absent: copy request runs game own");
            Check(note != null && note.Contains("blocked") && note.Contains("not registered"), "copy absent: blocked warning says why");
        }

        // ---- lazy copy acquisition ----

        private static void CopyAcquisitionTests()
        {
            // Normal: not registered yet at OnCreate time is irrelevant; first update with registered attempts once.
            var a = new CopyAcquisition();
            Check(a.State == CopyState.NotCheckedYet && !a.Available, "starts not checked");
            Check(a.ShouldAttempt(true), "first update with copy registered attempts");
            a.Succeeded();
            Check(a.Available && a.State == CopyState.Available, "success: available");
            Check(!a.ShouldAttempt(true) && a.Available, "after success: no second attempt");

            // Failure: attempted once, never again, stays unavailable.
            var f = new CopyAcquisition();
            Check(f.ShouldAttempt(true), "failure case: attempts");
            f.Failed();
            Check(f.State == CopyState.CreationFailed && !f.Available, "failure: creation failed");
            bool again = false;
            for (int i = 0; i < 100; i++) again |= f.ShouldAttempt(true);
            Check(!again && f.State == CopyState.CreationFailed, "failure: never retried");
            f.Succeeded();
            Check(f.State == CopyState.CreationFailed, "failure is final");

            // Not registered (gate closed / registration failed): never attempts, says so.
            var n = new CopyAcquisition();
            Check(!n.ShouldAttempt(false) && n.State == CopyState.NotRegistered && !n.Available, "not registered: no attempt");
            Check(!n.ShouldAttempt(false), "not registered: still no attempt");

            // The bug JR-T-001d fixed: OnCreate saw CopyRegistered=false. Acquisition no longer happens at OnCreate; the
            // first update sees the final value (true) and acquires.
            var late = new CopyAcquisition();
            Check(late.ShouldAttempt(true), "registered by the first update: acquired then (OnCreate order no longer matters)");

            // Switch + acquisition together, first step: flags always one whole pair, copy only when available.
            foreach (bool registered in new[] { false, true })
            foreach (bool createOk in new[] { false, true })
            foreach (CarDrivingMode want in Enum.GetValues(typeof(CarDrivingMode)))
            {
                var acq = new CopyAcquisition();
                bool held = false;
                if (acq.ShouldAttempt(registered)) { if (createOk) { acq.Succeeded(); held = true; } else acq.Failed(); }
                var sw = new DrivingSwitch();
                bool applied = sw.Step(want, DrivingSwitch.GateFor(s_Open, held), out PairFlags fl, out _);
                bool copyRuns = registered && createOk && want == CarDrivingMode.JunctionRulesCopy;
                Check(applied && fl.IsValid && fl.CopyNavigation == copyRuns && held == acq.Available,
                    $"first step: registered {registered}, create ok {createOk}, want {want}: one whole pair, copy runs {copyRuns}");
            }

            // Status text never reads as runnable when the copy cannot run.
            string s1 = CopyAcquisition.StatusText(CarDrivingMode.GameOwn, s_Open, true, CopyState.CreationFailed);
            Check(s1.Contains("Pass") && s1.Contains("creation failed, cannot run"), "status: pass but creation failed says cannot run");
            string s2 = CopyAcquisition.StatusText(CarDrivingMode.GameOwn, s_Open, false, CopyState.NotRegistered);
            Check(s2.Contains("not registered, cannot run"), "status: not registered says cannot run");
            string s3 = CopyAcquisition.StatusText(CarDrivingMode.JunctionRulesCopy, s_Open, true, CopyState.Available);
            Check(s3.Contains("Running: Junction Rules copy") && s3.Contains("Copy: available"), "status: available");
            string s4 = CopyAcquisition.StatusText(CarDrivingMode.GameOwn, s_Open, true, CopyState.NotCheckedYet);
            Check(s4.Contains("first simulation step"), "status: registered, not set up yet");
            string s5 = CopyAcquisition.StatusText(CarDrivingMode.GameOwn, s_Closed, false, CopyState.NotCheckedYet);
            Check(s5.Contains("VersionMismatch") && s5.Contains("cannot run"), "status: gate closed says cannot run");
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
