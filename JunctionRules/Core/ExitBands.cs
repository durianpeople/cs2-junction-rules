using System;
using System.Collections.Generic;

namespace JunctionRules.Core
{
    /// <summary>
    /// 0.4.3-probe (JR-T-007c driving): one boxed building car exit as the driving rule sees it: the public road edge
    /// (entity key, SortedKeys.KeyOf) and the exit's curve position on that edge (the edge curve's own t, as
    /// ConnectedNode.m_CurvePosition / Building.m_CurvePosition). Exits through the building's own access road are
    /// junction boxes on the node (YellowBoxJunction) and never bands.
    /// </summary>
    public struct ExitBand
    {
        public long EdgeKey;
        public float T;

        public ExitBand(long edgeKey, float t) { EdgeKey = edgeKey; T = t; }
    }

    /// <summary>
    /// 0.4.3-probe: which saved exit boxes of a building become driving bands, and the hand-over list (sorted by edge,
    /// then t). Replaces 0.4.2's one band per building at Building.m_RoadEdge + m_CurvePosition (the lot-front middle).
    /// Game-independent; Boxes/BoxFacts.BoxedExitBands reads the game data, Systems/CarDrivingSwitchSystem.cs publishes
    /// (BoxEdgeHandoff), Stage2/JRBoxEdges.cs carries the list into the job by value.
    /// </summary>
    public static class ExitBands
    {
        /// <summary>Most bands the job carries (Stage2/JRBoxEdges: FixedList512Bytes&lt;long&gt; keys + FixedList512Bytes&lt;float&gt; t).</summary>
        public const int Capacity = 63;

        /// <summary>Two bands on the same edge closer than this (curve t) are one band.</summary>
        public const float SameT = 1e-4f;

        /// <summary>
        /// Indices into 'exits' of the exits that carry a box and drive as a band: each saved key matched as the paint
        /// matches it (ExitRules.Match: same kind within KeyMatch, else any non-junction kind); a legacy (key-less
        /// 0.4.0-0.4.2) box takes the exit nearest its old access point (ExitRules.Migrate), as the paint's load migration
        /// does. Junction exits and keys without an exit are left out ('unmatched' counts the keys without an exit).
        /// Distinct, in exit order.
        /// </summary>
        public static List<int> Boxed(IList<ExitKey> keys, bool legacy, V2 legacyAccess, IList<ExitCandidate> exits, out int unmatched)
        {
            unmatched = 0;
            var r = new List<int>();
            if (exits == null || exits.Count == 0)
            {
                unmatched = legacy ? 1 : keys?.Count ?? 0;
                return r;
            }
            var hit = new bool[exits.Count];
            if (legacy)
            {
                int i = ExitRules.Migrate(legacyAccess, exits, out _);
                if (i < 0) unmatched++;
                else hit[i] = true;
            }
            else if (keys != null)
            {
                foreach (ExitKey k in keys)
                {
                    int i = ExitRules.Match(k, exits);
                    if (i < 0) unmatched++;
                    else hit[i] = true;
                }
            }
            for (int i = 0; i < exits.Count; i++) if (hit[i] && exits[i].Kind != ExitKind.Junction) r.Add(i);
            return r;
        }

        /// <summary>
        /// The hand-over order: by edge key, then t; near-equal t on one edge merged. At most 'capacity' kept (the rest
        /// counted in 'dropped': those exits are not obeyed, logged by the caller).
        /// </summary>
        public static ExitBand[] Normalise(IEnumerable<ExitBand> bands, int capacity, out int dropped)
        {
            var all = new List<ExitBand>();
            if (bands != null) all.AddRange(bands);
            all.Sort((a, b) => a.EdgeKey != b.EdgeKey ? a.EdgeKey.CompareTo(b.EdgeKey) : a.T.CompareTo(b.T));
            var r = new List<ExitBand>(all.Count);
            foreach (ExitBand b in all)
            {
                if (float.IsNaN(b.T)) continue;
                if (r.Count > 0 && r[r.Count - 1].EdgeKey == b.EdgeKey && Math.Abs(r[r.Count - 1].T - b.T) < SameT) continue;
                r.Add(b);
            }
            dropped = Math.Max(0, r.Count - capacity);
            if (dropped > 0) r.RemoveRange(capacity, dropped);
            return r.ToArray();
        }
    }
}
