using System.Collections.Generic;

namespace JunctionRules.Core
{
    // JR-T-007b-r (F4): which arms of a junction count as YIELDING for the box shape (StraightBox priority-road rule).
    // 0.4.2 counted an arm as yielding when ANY car lane from it carried STOP / Give Way. In Bevy Valley single turn lanes
    // on main-road arms carry Give Way (Code AM-17: Give Way for single-conflict moves), so a priority arm was misclassed
    // and its mouth slid. Now:
    // - an arm with a straight-through movement (CarLaneFlags.Forward on a node lane from it, LaneSystem.cs 7033-7055)
    //   yields only when its through lane(s) carry STOP / Give Way (all of them: one Give Way slip lane beside a
    //   priority through lane does not make the arm yielding);
    // - an arm with no through movement (a T stem, a one-way arm that only turns) yields when MOST of its lanes do
    //   (more than half).
    // Game-independent; unit-tested in StraightBoxTests (ArmYieldTests).

    /// <summary>Per-arm counts of the node car lanes that start at this arm.</summary>
    public sealed class ArmLaneCounts
    {
        public int Lanes, YieldingLanes, Through, ThroughYielding;

        public void Add(bool yielding, bool through)
        {
            Lanes++;
            if (yielding) YieldingLanes++;
            if (through) { Through++; if (yielding) ThroughYielding++; }
        }

        /// <summary>The rule above.</summary>
        public bool Yielding => Through > 0 ? ThroughYielding == Through : Lanes > 0 && YieldingLanes * 2 > Lanes;

        /// <summary>For the "arms ..." note: lanes / yielding lanes / through yielding of through.</summary>
        public string Describe() => $"{Lanes} lane(s), {YieldingLanes} yielding, through {ThroughYielding}/{Through} yielding";
    }

    public static class ArmYield
    {
        /// <summary>Classifies every arm; the returned flags are in the same order as the counts.</summary>
        public static bool[] Classify(IList<ArmLaneCounts> arms)
        {
            var r = new bool[arms?.Count ?? 0];
            for (int i = 0; i < r.Length; i++) r[i] = arms[i] != null && arms[i].Yielding;
            return r;
        }
    }
}
