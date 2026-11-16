using System;

namespace JunctionRules.Core
{
    /// <summary>What the hover outline was solved for (JR-T-007b-r F5): target, its box state and the shape parameters.</summary>
    public struct HoverKey : IEquatable<HoverKey>
    {
        /// <summary>Target entity as index &lt;&lt; 32 | version.</summary>
        public long Entity;
        public int Kind;
        public bool HasBox;
        public double KerbInset, EntranceLength, MarkingMargin;
        /// <summary>0.4.3: sub-target within the entity (a building's exit, from its saved key; 0 for a junction).</summary>
        public long Sub;

        public HoverKey(long entity, int kind, bool hasBox, double kerbInset, double entranceLength, double markingMargin, long sub = 0)
        {
            Entity = entity; Kind = kind; HasBox = hasBox; KerbInset = kerbInset; EntranceLength = entranceLength; MarkingMargin = markingMargin; Sub = sub;
        }

        public bool Equals(HoverKey o) => Entity == o.Entity && Sub == o.Sub && Kind == o.Kind && HasBox == o.HasBox
            && KerbInset.Equals(o.KerbInset) && EntranceLength.Equals(o.EntranceLength) && MarkingMargin.Equals(o.MarkingMargin);

        public override bool Equals(object obj) => obj is HoverKey k && Equals(k);

        public override int GetHashCode() => Entity.GetHashCode() ^ Sub.GetHashCode() ^ (Kind * 397) ^ (HasBox ? 1 : 0);
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
