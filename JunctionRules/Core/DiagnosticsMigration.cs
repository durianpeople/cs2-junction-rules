namespace JunctionRules.Core
{
    /// <summary>
    /// 1.0.0 (first public release): one-time settings migration for the two diagnostics logs ("Log navigation timing",
    /// "Exit trace (log)"). Both are developer options from 1.0.0: off by default and shown in Options only when the game
    /// runs with the -developerMode launch option.
    ///
    /// Schema (the hidden DiagnosticsVersion setting):
    ///   0 = never migrated: a fresh install, or settings saved by 0.4.7-probe or older. Nothing is switched: the loaded
    ///       values are kept (off by default). In particular nothing is ever switched ON for a player.
    ///   1 = saved by 0.4.8-0.4.12-probe, whose first load switched both logs on for the probe's evidence. 1.0.0 switches
    ///       both off once, because players can no longer see them in Options.
    ///   2 = this schema; nothing more is done.
    /// </summary>
    public static class DiagnosticsMigration
    {
        /// <summary>Diagnostics schema version this build writes.</summary>
        public const int Current = 2;

        /// <summary>The schema written by the 0.4.8-0.4.12 probes (their one-time switch-on).</summary>
        public const int ProbeSwitchedOn = 1;

        /// <summary>
        /// Returns true when a migration step is due (the caller then stores <see cref="Current"/> and saves). The two
        /// flags come back unchanged except for the probe schema (1), which turns both off; 'changed' says whether
        /// either flag moved.
        /// </summary>
        public static bool Migrate(int storedVersion, ref bool timing, ref bool exitTrace, out bool changed)
        {
            changed = false;
            if (storedVersion >= Current) return false;
            if (storedVersion == ProbeSwitchedOn)
            {
                changed = timing || exitTrace;
                timing = false;
                exitTrace = false;
            }
            return true;
        }
    }
}
