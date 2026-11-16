using System;

namespace JunctionRules.Core
{
    /// <summary>
    /// What the hover outline was solved for (JR-T-007b-r F5): target and the shape parameters. 0.4.3 review F5: the box
    /// state is not part of the key (it only picks the colour, at draw time); an exit target also carries its exit key
    /// (Sub) and where the exit sits now (road edge + curve position quantised to 1/4096), so a drive re-wired to another
    /// road or moved along it is solved again instead of keeping a stale shape / failure for the whole hover.
    /// </summary>
    public struct HoverKey : IEquatable<HoverKey>
    {
        /// <summary>Target entity as index &lt;&lt; 32 | version.</summary>
        public long Entity;
        public int Kind;
        public double KerbInset, EntranceLength, MarkingMargin;
        /// <summary>0.4.3: sub-target within the entity (a building's exit, from its saved key; 0 for a junction).</summary>
        public long Sub;
        /// <summary>0.4.3: the exit's road edge (index &lt;&lt; 32 | version; 0 for a junction).</summary>
        public long AccessEdge;
        /// <summary>0.4.3: the exit's curve position on that edge, quantised (QuantiseT).</summary>
        public int AccessT;

        public HoverKey(long entity, int kind, double kerbInset, double entranceLength, double markingMargin, long sub = 0, long accessEdge = 0, int accessT = 0)
        {
            Entity = entity; Kind = kind; KerbInset = kerbInset; EntranceLength = entranceLength; MarkingMargin = markingMargin; Sub = sub;
            AccessEdge = accessEdge; AccessT = accessT;
        }

        /// <summary>Curve position to the key's grid (1/4096 of the edge: millimetres to a few cm on real roads).</summary>
        public static int QuantiseT(double t) => double.IsNaN(t) ? -1 : (int)Math.Round(Math.Max(0, Math.Min(1, t)) * 4096.0);

        public bool Equals(HoverKey o) => Entity == o.Entity && Sub == o.Sub && Kind == o.Kind && AccessEdge == o.AccessEdge && AccessT == o.AccessT
            && KerbInset.Equals(o.KerbInset) && EntranceLength.Equals(o.EntranceLength) && MarkingMargin.Equals(o.MarkingMargin);

        public override bool Equals(object obj) => obj is HoverKey k && Equals(k);

        public override int GetHashCode() => Entity.GetHashCode() ^ Sub.GetHashCode() ^ (Kind * 397) ^ AccessEdge.GetHashCode() ^ (AccessT * 31);
    }

    /// <summary>
    /// JR-T-007b-r (F5): the tool's hover outline is solved once per hovered target (0.4.2 re-solved it every 30 frames,
    /// 4-12 ms on the main thread). Solve again only when the key changes (another target, its box added / removed, kerb
    /// inset / entrance length / marking margin changed) or the caller saw one of the shape's source entities change
    /// (Updated / gone). Reset when the tool stops.
    /// </summary>
    public sealed class HoverCache<T> where T : class
    {
        private HoverKey m_Key;
        private bool m_Has;
        public T Value { get; private set; }
        /// <summary>Solves so far (diagnostics, tests).</summary>
        public int Solves { get; private set; }

        public bool Stale(HoverKey key, bool sourcesChanged) => !m_Has || sourcesChanged || !key.Equals(m_Key);

        /// <summary>The cached value, solving it with 'solve' when stale.</summary>
        public T Get(HoverKey key, bool sourcesChanged, Func<T> solve)
        {
            if (!Stale(key, sourcesChanged)) return Value;
            Value = solve();
            m_Key = key;
            m_Has = true;
            Solves++;
            return Value;
        }

        public void Reset() { m_Has = false; Value = null; }
    }
}
