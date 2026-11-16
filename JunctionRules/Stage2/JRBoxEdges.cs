using JunctionRules.Core;
using Unity.Collections;
using Unity.Entities;

namespace JunctionRules.Stage2
{
    /// <summary>
    /// The boxed building CAR EXITS for the driving rule (0.4.3-probe; 0.4.2 JR-T-010-r carried one road edge per boxed
    /// building and read Building.m_RoadEdge / m_CurvePosition in the job). Built on the main thread:
    /// Systems/CarDrivingSwitchSystem.cs resolves every saved exit key to its road edge and curve position
    /// (Boxes/BoxFacts.BoxedExitBands, Core/ExitBands.cs) and publishes them (Core BoxEdgeHandoff) when the boxes change
    /// (and every 64 steps, for a drive re-wired to a rebuilt road); handed to the copy's job BY VALUE each step (block
    /// S2-23 / S2-26): fixed-size lists, so there is no container to dispose and no job dependency to track. Up to
    /// ExitBands.Capacity (63) exits, sorted by edge key then t (an edge repeats when it has several boxed exits); more are
    /// not handed over (the switch system logs a Warn with the count).
    /// </summary>
    public struct JRBoxEdges : IKeyList
    {
        public FixedList512Bytes<long> m_Keys;
        public FixedList512Bytes<float> m_T;
        /// <summary>0.4.4: carriageway mask per band (Core ExitBand.Carriageways).</summary>
        public FixedList512Bytes<uint> m_Mask;

        public int Count => m_Keys.Length;
        public long Key(int i) => m_Keys[i];
        /// <summary>Curve position on the edge of band i.</summary>
        public float T(int i) => m_T[i];
        /// <summary>0.4.4: carriageway mask of band i.</summary>
        public uint Mask(int i) => m_Mask[i];

        public static long KeyOf(Entity e) => SortedKeys.KeyOf(e.Index, e.Version);

        /// <summary>The set from the published bands (Core BoxEdgeHandoff: sorted, parallel arrays).</summary>
        public static JRBoxEdges FromBands(long[] keys, float[] t, uint[] masks)
        {
            JRBoxEdges set = default;
            int n = keys.Length < t.Length ? keys.Length : t.Length;
            for (int i = 0; i < n; i++)
            {
                if (set.m_Keys.Length >= set.m_Keys.Capacity || set.m_T.Length >= set.m_T.Capacity || set.m_Mask.Length >= set.m_Mask.Capacity) break;
                set.m_Keys.Add(keys[i]);
                set.m_T.Add(t[i]);
                set.m_Mask.Add(masks != null && i < masks.Length ? masks[i] : MedianSplit.AllCarriageways);
            }
            return set;
        }

        /// <summary>Index of the edge's first band; -1 = no boxed exit on this edge (one binary search).</summary>
        public int First(Entity edge) => SortedKeys.First(ref this, KeyOf(edge));
    }
}
