using System;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.12 (JR-T-018, gap G29, owner live 0.4.11: "this cop car is yielded but doesn't go in"). Police car 1224495 (patrolling,
    /// not on a call: normal priority) at (81.8, 403.6) heading 73 on Mistwood Lane (alley, give way) 11 m west of node 621408,
    /// turning right into Sunset Street 953631's near carriageway whose queue stands just past the box. Mistwood's zebra lies
    /// 0-2.3 m into the node (box starts +2.8 m on arm 1 in the log); the turn is ~11 m long, so the G22 footprint (front 1 m
    /// before the lane end, 4.7 m car + 1 m margin) lies ~4.3-10 m in: clear of the zebra, over through paths into the same lane
    /// only. Yet 0.4.11 logged "entered a box under the same-exit-lane exception 0" all session: the GAME's own junction room hold
    /// (CarLaneSpeedIterator IterateNextLane -> CheckSpace: a junction lane with a true crossing and no room on the next lane)
    /// still ran at boxed junctions (G25's skip, S2-54, was unboxed only) and kept every G22 car out before BoxHold's exception
    /// could matter. Fix: S2-54 also skips it at a boxed junction when G22 admits the car (crossed lanes still judged clear).
    /// </summary>
    internal static partial class Program
    {
        private static void G29Tests()
        {
            const uint none = 0u;
            Check(StockRoomSkip.Applies(true, true, none, true, false), "G29 unboxed: as G25 (0.4.11)");
            Check(!StockRoomSkip.Applies(true, true, none, true, true), "G29 boxed, old overload: stock hold kept (0.4.11 behaviour of the 5-arg call)");
            Check(!StockRoomSkip.Applies(true, true, none, true, true, false), "G29 boxed without the G22 exception: the box rule's room test and the stock hold stay");
            Check(StockRoomSkip.Applies(true, true, none, true, true, true), "G29 boxed and admitted by G22 (the cop): the stock room hold may be dropped");
            Check(!StockRoomSkip.Applies(true, true, StockRoomSkip.TrafficLights, true, true, true) && !StockRoomSkip.Applies(true, true, StockRoomSkip.Roundabout, true, true, true),
                "G29 signals / roundabouts: stock, as G22 itself");
            Check(!StockRoomSkip.Skip(StockRoomSkip.Applies(true, true, none, true, true, true), 1, true), "G29 a busy crossed lane keeps the hold (entering for room never skips giving way)");
            Check(StockRoomSkip.Skip(StockRoomSkip.Applies(true, true, none, true, true, true), 2, false), "G29 crossed lanes clear: dropped");
            // the cop's G22 footprint at 621408 (lane ~11 m, Mistwood zebra 0..2.3 m, no exit zebra on 953631, nothing standing ahead)
            SameExitException.Footprint(11f, float.MaxValue, float.MaxValue, 4.7f, 1f, out float from, out float to);
            Check(SameExitException.WithinLane(from) && from > 2.3f && Math.Abs(to - 10f) < 1e-4, $"G29 the cop's waiting footprint {from:0.0}..{to:0.0} m lies past its own zebra (0..2.3 m) and inside the turn");
            // nose trace line
            long f = ExitTrace.Pack(ExitTrace.Event.BoxNose, ExitPaths.Path.Drive, ExitPaths.LaneKind.DriveLane, ExitGiveWay.Decision.Go, ExitTrace.ReasonSameExitEligible, true);
            string line = ExitTrace.Format(ExitTrace.PackEntity(1224499, 1), ExitTrace.PackEntity(838449, 1), f, ((long)51645 << 32) | 123u, ExitTrace.PackPoint(1.6f, 0f));
            Check(ExitTrace.Notable(f) && line.Contains("NOTABLE nose in a box: vehicle 1224499:1 stands with its front 1.6 m past the line of the box at 51645") && line.Contains("same-exit-lane exception admits it") && line.Contains("waits for 838449:1"),
                "G29 nose trace names the car, the box, how far past, G22 and its blocker: " + line);
            string free = ExitTrace.Format(1, 0, ExitTrace.Pack(ExitTrace.Event.BoxNose, ExitPaths.Path.Drive, ExitPaths.LaneKind.DriveLane, ExitGiveWay.Decision.Go, 0, true), ((long)7 << 32) | 1u, ExitTrace.PackPoint(0.4f, 0f));
            Check(free.Contains("waits for nothing named") && !free.Contains("exception"), "G29 nose trace without a named blocker / G22");
            Check(ExitGiveWay.BoxNoseKind < ExitGiveWay.EventKinds && (long)ExitGiveWay.EventKinds * ExitGiveWay.EventKindStride < int.MaxValue, "G29 nose episodes: own event kind, keys fit");
            Check(Stage2Counters.BoxNoseAtLight == Stage2Counters.Count - 7 && Stage2Counters.StockRoomHoldSkippedBoxed == Stage2Counters.Count - 8, "G29 two new counters before the owner-decision one");
            // review F1: a pedestrian overlap without merge / unsafe / water flags is a crossing for stock CheckSpace too
            Check(StockRoomSkip.IsCrossedLane(0, true) && !StockRoomSkip.IsCrossedLane(0x1, true), "G29 review F1: a plain zebra overlap is a true crossing (merge-flagged ones are not)");
            string root = RepoRoot();
            string sr = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRStockRoom.cs"));
            string yb = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRYellowBox.cs"));
            string copy = File.ReadAllText(Path.Combine(root, "JunctionRules", "Vanilla", "JRCarLaneSpeedIterator.cs"));
            Check(sr.Contains("if (sameExit && StockRoomSkip.IsCrossedLane((int)o.m_Flags, true) && !carLanes.HasComponent(o.m_Other)) { zebraCrossings++; continue; }")
                  && sr.Contains("if (crossings + zebraCrossings == 0) return false;"), "repo (review F1): a G22-admitted boxed lane whose only crossing is a zebra still gets the skip (people: the zebra rule)");
            Check(sr.Contains("&& SameExitAdmits(lane, boxOwner.m_Owner, laneLength, inverse, 0f, self,") && sr.Contains("StockRoomSkip.Applies(RuleOn, m_ExitLookups, (uint)laneData.m_Flags, m_NodeLaneData.HasComponent(lane), boxed, sameExit)"),
                "repo: S2-54's job side asks G22 at a boxed junction");
            Check(copy.Contains("m_JR.SkipStockRoomHold(lane, componentData2, componentData.m_Length, num2, flag2, m_PrefabCar, m_CurrentSpeed, m_TimeStep, m_Entity, ref m_LaneConditionData,"),
                "repo: the copied S2-54 call line is unchanged (no copy change)");
            Check(yb.Contains("m_BoxPedestrians = pedestrianLanes;") && yb.Contains("BoxDriving.LightStopsInWindow((int)noseSig.m_Signal)) m_BoxNoseLight = true;"),
                "repo: BoxHold keeps the pedestrian lookup; a nose at the lane's own red / amber is counted apart");
        }
    }
}
