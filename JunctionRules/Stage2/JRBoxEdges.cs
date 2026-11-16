using JunctionRules.Core;
using Unity.Collections;
using Unity.Entities;

namespace JunctionRules.Stage2
{
    /// <summary>
    /// 0.4.2 (JR-T-010-r, review item 1): the road edges with a boxed building access (Building.m_RoadEdge of every
    /// building with YellowBoxEntrance), sorted. Built on the main thread: Systems/CarDrivingSwitchSystem.cs publishes the
    /// keys (Core BoxEdgeHandoff) when the entrance boxes change (and every 64 steps, for an access re-wired to a rebuilt road), handed
    /// to the copy's job BY VALUE each step (block S2-23 / S2-26): a fixed-size struct, so there is no container to dispose
    /// and no job dependency to track. Up to 15 edges; more = <see cref="m_Overflow"/>: every edge "may have" a box and
    /// the band test falls back to the 0.4.2 building loop (correct, only slower).
    /// </summary>
    public struct JRBoxEdges : IKeyList
    {
        public FixedList128Bytes<long> m_Keys;
        public bool m_Overflow;

        public int Count => m_Keys.Length;
        public long Key(int i) => m_Keys[i];

        public static long KeyOf(Entity e) => SortedKeys.KeyOf(e.Index, e.Version);

        /// <summary>The set from the published keys (Core BoxEdgeHandoff, sorted).</summary>
        public static JRBoxEdges FromKeys(long[] keys)
        {
            JRBoxEdges set = default;
            for (int i = 0; i < keys.Length; i++)
            {
                if (set.m_Keys.Length >= set.m_Keys.Capacity) { set.m_Overflow = true; break; }
                set.m_Keys.Add(keys[i]);
            }
            return set;
        }

        /// <summary>Adds a road edge (sorted, no duplicates); a full set turns into "all edges".</summary>
        public void Add(Entity edge)
        {
            if (edge == Entity.Null || m_Overflow) return;
            long key = KeyOf(edge);
            int at = SortedKeys.InsertAt(ref this, key);
            if (at < 0) return;
            if (m_Keys.Length >= m_Keys.Capacity)
            {
                m_Overflow = true;
                return;
            }
            m_Keys.Insert(at, key);
        }

        /// <summary>The edge may carry a boxed access (always true after an overflow).</summary>
        public bool MayHave(Entity edge) => m_Overflow || SortedKeys.Contains(ref this, KeyOf(edge));
    }
}
