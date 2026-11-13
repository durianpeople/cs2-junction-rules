namespace JunctionRules.Core
{
    /// <summary>The "Car driving" setting.</summary>
    public enum CarDrivingMode
    {
        /// <summary>The game's own CarNavigationSystem (default).</summary>
        GameOwn = 0,
        /// <summary>The unchanged copy in Vanilla/ (JRCarNavigationSystem).</summary>
        JunctionRulesCopy = 1,
    }

    /// <summary>Enabled flags of the four systems. Only two shapes are valid: one whole pair on, the other off.</summary>
    public readonly struct PairFlags
    {
        public readonly bool GameNavigation;
        public readonly bool GameActions;
        public readonly bool CopyNavigation;
        public readonly bool CopyActions;

        public PairFlags(bool gameNavigation, bool gameActions, bool copyNavigation, bool copyActions)
        {
            GameNavigation = gameNavigation;
            GameActions = gameActions;
            CopyNavigation = copyNavigation;
            CopyActions = copyActions;
        }

        public static PairFlags For(CarDrivingMode mode) => mode == CarDrivingMode.JunctionRulesCopy
            ? new PairFlags(false, false, true, true)
            : new PairFlags(true, true, false, false);

        /// <summary>Exactly one navigation system runs, and each Actions system runs exactly when its own navigation does.</summary>
        public bool IsValid => GameNavigation != CopyNavigation && GameActions == GameNavigation && CopyActions == CopyNavigation;

        public override string ToString() => $"game nav {On(GameNavigation)}/actions {On(GameActions)}, copy nav {On(CopyNavigation)}/actions {On(CopyActions)}";

        private static string On(bool b) => b ? "on" : "off";
    }

    /// <summary>
    /// Decides which pair runs. Called once per simulation step by the switch system, which runs before the game's
    /// navigation slot, so a change applies between two steps: the step either runs the whole game pair or the
    /// whole copy pair, never both and never neither.
    /// </summary>
    public sealed class DrivingSwitch
    {
        private bool m_HasApplied;
        private CarDrivingMode m_Applied;
        private bool m_GateWarned;

        public CarDrivingMode Applied => m_Applied;
        public bool HasApplied => m_HasApplied;
        public int Switches { get; private set; }

        public static CarDrivingMode Effective(CarDrivingMode requested, bool copyAllowed) =>
            copyAllowed && requested == CarDrivingMode.JunctionRulesCopy ? CarDrivingMode.JunctionRulesCopy : CarDrivingMode.GameOwn;

        /// <summary>
        /// Returns true when the flags must be (re)written now, with the flags in <paramref name="flags"/> and a log line
        /// in <paramref name="note"/> (null when nothing is worth logging). The first call always applies.
        /// </summary>
        public bool Step(CarDrivingMode requested, GateResult gate, out PairFlags flags, out string note)
        {
            note = null;
            CarDrivingMode effective = Effective(requested, gate.CopyAllowed);
            if (requested == CarDrivingMode.JunctionRulesCopy && !gate.CopyAllowed && !m_GateWarned)
            {
                m_GateWarned = true;
                note = "Junction Rules copy requested but blocked: " + gate.Message;
            }
            flags = PairFlags.For(effective);
            if (m_HasApplied && effective == m_Applied) return false;

            string what = effective == CarDrivingMode.JunctionRulesCopy ? "Junction Rules copy" : "game's own";
            string line = m_HasApplied ? $"car driving switched to {what} ({flags})" : $"car driving starts with {what} ({flags})";
            note = note == null ? line : note + "; " + line;
            if (m_HasApplied) Switches++;
            m_HasApplied = true;
            m_Applied = effective;
            return true;
        }

        /// <summary>Forget the applied state (new World / game load) so the next step re-applies the flags.</summary>
        public void Reset()
        {
            m_HasApplied = false;
        }
    }
}
