namespace JunctionRules.Core
{
    /// <summary>Whether the switch holds the copy pair and the probes.</summary>
    public enum CopyState
    {
        /// <summary>No simulation step has run yet (no city loaded since game start).</summary>
        NotCheckedYet,
        /// <summary>The copy was not registered at load (version gate closed, or registration failed).</summary>
        NotRegistered,
        /// <summary>The switch holds the registered copy systems: the copy can run.</summary>
        Available,
        /// <summary>The copy was registered but getting its systems failed; it is never tried again this session.</summary>
        CreationFailed,
    }

    /// <summary>
    /// Lazy, attempt-once acquisition of the copy systems by the switch. The switch cannot get them in its OnCreate:
    /// UpdateSystem.UpdateBefore creates (and so OnCreates) the switch during registration, which may run before the
    /// copy is registered. So the switch asks in every update, before it writes the Enabled flags: the first update in
    /// which the copy is registered attempts once; success or failure is final for the session.
    /// </summary>
    public sealed class CopyAcquisition
    {
        public CopyState State { get; private set; } = CopyState.NotCheckedYet;

        public bool Available => State == CopyState.Available;

        /// <summary>
        /// Call at the start of every switch update with Mod.CopyRegistered. True means "attempt now" (exactly once per
        /// session, and only while registered); the caller then reports <see cref="Succeeded"/> or <see cref="Failed"/>
        /// in the same update, before it writes any Enabled flag.
        /// </summary>
        public bool ShouldAttempt(bool copyRegistered)
        {
            if (State == CopyState.Available || State == CopyState.CreationFailed) return false;
            if (!copyRegistered)
            {
                State = CopyState.NotRegistered;
                return false;
            }
            return true;
        }

        public void Succeeded()
        {
            if (State == CopyState.CreationFailed) return;
            State = CopyState.Available;
        }

        public void Failed() => State = CopyState.CreationFailed;

        /// <summary>
        /// Status line for the options page. Never says the copy can run when it cannot: a passing version check with an
        /// unavailable copy says so.
        /// </summary>
        public static string StatusText(CarDrivingMode running, GateResult gate, bool copyRegistered, CopyState state)
        {
            string run = running == CarDrivingMode.JunctionRulesCopy ? "Junction Rules copy" : "game's own";
            string copy;
            if (!copyRegistered) copy = "not registered, cannot run";
            else if (state == CopyState.Available) copy = "available";
            else if (state == CopyState.CreationFailed) copy = "creation failed, cannot run";
            else copy = "registered, set up at the first simulation step";
            return $"Running: {run}. Version check: {gate.Verdict}. Copy: {copy}.";
        }
    }
}
