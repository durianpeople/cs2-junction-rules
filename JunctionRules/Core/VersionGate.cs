using System;

namespace JunctionRules.Core
{
    /// <summary>What the copy in Vanilla/ was made from. Update together with tools/vanilla_rules.json after a port.</summary>
    public static class MadeFrom
    {
        /// <summary>Colossal.Version.version of Game.Version.current: short version + (build farm number.changeset).</summary>
        public const string GameVersion = "1.6.4f1 (1097.158a)";

        /// <summary>
        /// <see cref="IlHasher"/> hash of Game.Simulation.CarNavigationSystem (with nested types) and
        /// Game.Simulation.CarLaneSpeedIterator in that Game.dll, as printed by tools/IlHash.
        /// </summary>
        public const string VanillaCodeHash = "5a15f3b9a5f9b2db5795ae1fc4626ddabf9eb432ac19f3fd71f2b54ab5577d32";
    }

    public enum GateVerdict
    {
        /// <summary>Version and code hash match: the copy may run.</summary>
        Pass,
        /// <summary>Version matches, code hash differs, but the player chose to skip the hash check: the copy may run.</summary>
        PassHashSkipped,
        /// <summary>Different game version: the copy must not run (struct layouts baked into the Burst code may differ).</summary>
        VersionMismatch,
        /// <summary>Same version string but the vanilla code differs (hot-fix build or hashing difference).</summary>
        CodeMismatch,
        /// <summary>The check itself failed (exception): fail safe.</summary>
        Error,
    }

    public readonly struct GateResult
    {
        public readonly GateVerdict Verdict;
        public readonly string Message;

        public GateResult(GateVerdict verdict, string message)
        {
            Verdict = verdict;
            Message = message;
        }

        public bool CopyAllowed => Verdict == GateVerdict.Pass || Verdict == GateVerdict.PassHashSkipped;
    }

    /// <summary>
    /// Load-time check that the running game is the one the copy was made from. The version must always match: the
    /// Burst code bakes in the field layout of every game struct it touches, so a copy built against another Game.dll
    /// can corrupt memory even when the car code itself did not change. The code hash is a second check on top.
    /// </summary>
    public static class VersionGate
    {
        public static GateResult Evaluate(string expectedVersion, string actualVersion, string expectedHash, string actualHash, bool skipHashCheck)
        {
            if (string.IsNullOrEmpty(actualVersion) || actualVersion != expectedVersion)
            {
                return new GateResult(GateVerdict.VersionMismatch,
                    $"game version '{actualVersion}' is not '{expectedVersion}' (the version the copy was made from). " +
                    "Car driving stays with the game's own system. The copy must be re-made for this game version (tools/vanilla_copy.py).");
            }
            if (!string.IsNullOrEmpty(actualHash) && actualHash == expectedHash)
            {
                return new GateResult(GateVerdict.Pass, $"game version '{actualVersion}' and vanilla code hash {Short(actualHash)} match");
            }
            if (skipHashCheck)
            {
                return new GateResult(GateVerdict.PassHashSkipped,
                    $"game version '{actualVersion}' matches; vanilla code hash {Short(actualHash)} differs from {Short(expectedHash)} " +
                    "but the code hash check is switched off in the settings");
            }
            return new GateResult(GateVerdict.CodeMismatch,
                $"game version '{actualVersion}' matches but the vanilla code hash {Short(actualHash)} differs from {Short(expectedHash)}. " +
                "Car driving stays with the game's own system.");
        }

        /// <summary>
        /// The load-time check in its safe order: read the version first; on a version mismatch stop there (the code
        /// hash is never computed and the copy never runs, whatever <paramref name="skipHashCheck"/> says). Only after a
        /// version match is the hash computed, in its own try/catch: a hashing exception counts as a hash mismatch, so
        /// "Skip code hash check" can still let the copy run. An exception reading the version fails safe (Error).
        /// </summary>
        public static GateResult Check(string expectedVersion, Func<string> readVersion, string expectedHash, Func<string> computeHash, bool skipHashCheck)
        {
            string version;
            try
            {
                version = readVersion();
            }
            catch (Exception e)
            {
                return Failed("reading the game version: " + e.GetType().Name + ": " + e.Message);
            }
            if (string.IsNullOrEmpty(version) || version != expectedVersion)
            {
                return Evaluate(expectedVersion, version, expectedHash, null, skipHashCheck);
            }

            string hash;
            string hashError = null;
            try
            {
                hash = computeHash();
            }
            catch (Exception e)
            {
                hash = null;
                hashError = e.GetType().Name + ": " + e.Message;
            }
            GateResult result = Evaluate(expectedVersion, version, expectedHash, hash, skipHashCheck);
            return hashError == null ? result : new GateResult(result.Verdict, result.Message + " (the code hash could not be computed: " + hashError + ")");
        }

        public static GateResult Failed(string what) =>
            new GateResult(GateVerdict.Error, "version check failed (" + what + "); car driving stays with the game's own system");

        private static string Short(string h) => string.IsNullOrEmpty(h) ? "(none)" : (h.Length > 16 ? h.Substring(0, 16) : h);
    }
}
