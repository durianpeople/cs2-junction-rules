using System;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.12 (JR-T-018, gap G28), owner rule 17 Nov: a building EXIT is a yielding approach, so G22's same-exit-lane box
    /// exception applies to vehicles leaving buildings at exit boxes (Core/ExitSameLane.cs). Live case: SUV 602885 leaving
    /// building 77236 at (72.4, 427.1) onto Sunset Street 1155147 (one-way, left-hand traffic): the exit is on the driver's
    /// RIGHT (lateral -12.6 m from the centre line), joining the right lane (centre -2.5 m) with nothing crossed: a near-side
    /// merge. Box window 15.1..23.1 m along the edge, join point ~19.0 m; queue car 838449 at 27.3 m (lateral -2.46 m: the same
    /// lane), rear ~25.1 m. Pavement x -8.25..-4.25 (Sidewalk Piece 4 at -6.25), lane edge -4.
    /// </summary>
    internal static partial class Program
    {
        private static void G28Tests()
        {
            const float margin = 1f, suv = 4.8f;
            // ---- live geometry, leaving lane from the lot boundary (lateral 12.6) to the lane centre (2.5): ~10.1 m; pavement end
            // at lateral 4.25 -> 8.35 m along it
            float wayOut = 10.1f, walkEnd = 8.35f;
            bool ok = ExitSameLane.Footprint(19.0f, 47f, 25.1f, suv, margin, wayOut, walkEnd, out float jf, out float jt, out float wf);
            Check(ok && Math.Abs(jt - 24.1f) < 1e-4 && Math.Abs(jf - 19.0f) < 1e-4 && wf >= walkEnd, $"G28 live SUV 602885: admitted, front 24.1 m (1 m behind 838449's rear), tail {wayOut - wf:0.0} m back on the leaving lane, off the pavement (from {wf:0.00} >= {walkEnd})");
            // a lorry does not fit: its tail would stand on the pavement -> normal rule (waits before the pavement)
            Check(!ExitSameLane.Footprint(19.0f, 47f, 25.1f, 12f, margin, wayOut, walkEnd, out _, out _, out _), "G28 a 12 m lorry would stand on the pavement: not admitted");
            // queue rear at the join point: the front cannot get onto the lane
            Check(!ExitSameLane.Footprint(19.0f, 47f, 19.5f, suv, margin, wayOut, walkEnd, out _, out _, out _), "G28 queue reaching back to the join point: not admitted (no room for the front)");
            // no queue on the lane: footprint at the lane end (room past the box is missing further on), whole body on the lane
            Check(ExitSameLane.Footprint(19.0f, 47f, float.MaxValue, suv, margin, wayOut, walkEnd, out float jf2, out float jt2, out float wf2) && jt2 == 46f && Math.Abs(jf2 - 40.2f) < 1e-4 && wf2 == wayOut,
                "G28 no standing queue on the joined lane: footprint at the lane end, wholly on the lane");
            // ---- merge only: a far-side turn crosses the near lane (two car overlaps ahead, one is the joined lane)
            Check(ExitSameLane.MergeOnly(1, 1) && !ExitSameLane.MergeOnly(2, 1) && !ExitSameLane.MergeOnly(1, 0), "G28 near-side merge only; a far-side turn across the near lane / another drive's way: normal rule");
            // ---- joined-lane paths under the footprint (another building's way in, a turn-in) keep the normal rule (G15 / G21 deadlocks)
            Check(ExitSameLane.TouchesOther(22f, 23f, jf, jt) && !ExitSameLane.TouchesOther(30f, 31f, jf, jt) && !ExitSameLane.TouchesOther(15f, 19f, jf, jt),
                "G28 an entrance / turn-in path under the footprint blocks the exception; one past it or behind the join point does not");
            // ---- the queue it joins: standing, before the merge, not on it
            Check(ExitSameLane.QueueNotPresent(true, false, 3f, 0f), "G28 a car standing 3 m before the merge (queue at the box line) is not present");
            Check(!ExitSameLane.QueueNotPresent(true, true, -1f, 0f), "G28 a car standing ON the merge point still blocks");
            Check(!ExitSameLane.QueueNotPresent(true, false, 8f, 6f), "G28 a moving car is judged as before (gap)");
            Check(!ExitSameLane.QueueNotPresent(false, false, 3f, 0f), "G28 without the exception standing cars stay present (0.4.8 rule)");
            // ---- deadlock argument (G15): the exiter in the box blocks only the joined lane's own traffic behind it, which waits
            // for the same full lane; entrance paths / turn-ins under it are excluded above; the far carriageway is never touched
            // (merge only + median, G26). So no wait cycle runs through it that did not exist without it.
            string root = RepoRoot();
            string ex = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRExitGiveWay.cs"));
            string yb = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRYellowBox.cs"));
            Check(ex.Contains("if (m_BoxRule) G28Check(lane, overlaps, start, inverse, length, self,") && ex.IndexOf("G28Check(lane") < ex.IndexOf("if (firstRoad > 1f)"),
                "repo: G28Check runs before the drive judgement's early returns (committed vehicles too)");
            Check(ex.Contains("if (boxed && !roomBox && m_G28JoinLane != Entity.Null && m_G28JoinLane == joinNav.m_Lane) { roomBox = true; m_G28Used = true; }"), "repo: the exception replaces only the room past the box");
            Check(ex.Contains("if (ExitSameLane.QueueNotPresent(m_G28Scan, occupies, gap, speed)) continue;"), "repo: standing queue on the joined lane not present only in the G28 scan");
            Check(yb.Contains("if (role == BoxDriving.EntranceRole.Leaving && m_G28JoinLane != Entity.Null && m_G28JoinLane == lane)") && yb.Contains("m_BoxSameExitEntry = true;"),
                "repo: the road-lane box hold (role Leaving) honours the exception; counter shared with G22");
            int eh = ex.IndexOf("private float EntryRoadHold"), ehEnd = eh < 0 ? -1 : ex.IndexOf("\n        private ", eh + 10);
            Check(eh >= 0 && ehEnd > eh && !ex.Substring(eh, ehEnd - eh).Contains("G28"), "repo: turning INTO a building (G21 EntryRoadHold) is untouched");
        }
    }
}
