using System;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.4 (JR-T-012): vehicles leaving buildings give way (Core/ExitGiveWay.cs, Stage2/JRExitGiveWay.cs).
    /// Owner rule: "Cars coming out of ANY building must give way, and when that building exit is a yellow box, must ALSO
    /// obey the yellow box rules." Layouts: a 1+1 road, lane centres measured across the road from the building side
    /// (near lane 1.75 m, far lane 5.25 m). Left-hand traffic: the near lane runs to the driver's left, a left turn out
    /// merges into it, a right turn crosses it and merges into the far lane; right-hand traffic is the mirror.
    /// </summary>
    internal static partial class Program
    {
        private const float ExNear = 1.75f, ExFar = 5.25f;
        private const int ExCarPriority = 50;   // an ordinary car (stock priorities below 108)

        /// <summary>Role of the near / far lane for an exit joining lane joinFar ? far : near (Core Role, as S2-30 measures it).</summary>
        private static (ExitGiveWay.ConflictRole Near, ExitGiveWay.ConflictRole Far) ExRoles(bool joinFar)
        {
            float join = joinFar ? ExFar : ExNear;
            return (ExitGiveWay.Role(!joinFar, ExNear, join), ExitGiveWay.Role(joinFar, ExFar, join));
        }

        private static void ExitLayoutTests()
        {
            // LHT left-out: nearside turn, merges into the near lane, crosses nothing.
            var r = ExRoles(false);
            Check(r.Near == ExitGiveWay.ConflictRole.Merge && r.Far == ExitGiveWay.ConflictRole.None, "LHT left-out: merges into the near lane only, the far lane is not on its way");
            Check(!ExitGiveWay.TurnsAcross(true, false, true), "LHT left-out: not a turn across traffic");
            // LHT right-out: crosses the near lane, merges into the far lane.
            r = ExRoles(true);
            Check(r.Near == ExitGiveWay.ConflictRole.Cross && r.Far == ExitGiveWay.ConflictRole.Merge, "LHT right-out: crosses the near lane and merges into the far lane");
            Check(ExitGiveWay.TurnsAcross(false, true, true), "LHT right-out: turn across traffic (counter label from the game's left-hand flag)");
            // RHT mirror: right-out merges into the near lane; left-out crosses the near lane and merges into the far one.
            r = ExRoles(false);
            Check(r.Near == ExitGiveWay.ConflictRole.Merge && r.Far == ExitGiveWay.ConflictRole.None, "RHT right-out: merges into the near lane only");
            Check(!ExitGiveWay.TurnsAcross(false, true, false), "RHT right-out: not a turn across traffic");
            r = ExRoles(true);
            Check(r.Near == ExitGiveWay.ConflictRole.Cross && r.Far == ExitGiveWay.ConflictRole.Merge, "RHT left-out: crosses the near lane and merges into the far lane");
            Check(ExitGiveWay.TurnsAcross(true, false, false), "RHT left-out: turn across traffic");
            // The geometry needs no traffic side: the same distances give the same roles in either city.
            Check(!ExitGiveWay.TurnsAcross(true, true, true) && !ExitGiveWay.TurnsAcross(false, false, false), "straight / both flags: not across");
            // 2+2 road, joining the far nearside lane (3rd lane out): the two near lanes are crossed, the 4th is not.
            Check(ExitGiveWay.Role(false, 1.75f, 8.75f) == ExitGiveWay.ConflictRole.Cross && ExitGiveWay.Role(false, 5.25f, 8.75f) == ExitGiveWay.ConflictRole.Cross
                && ExitGiveWay.Role(false, 12.25f, 8.75f) == ExitGiveWay.ConflictRole.None, "2+2 road: every lane nearer the building than the join lane is crossed, beyond it none");
            Check(ExitGiveWay.Role(false, 8.6f, 8.75f) == ExitGiveWay.ConflictRole.None, "a lane within the join tolerance of the join lane is not crossed");
        }

        private static void ExitGapTests()
        {
            const float delta = 2f;   // the leaving vehicle's own arrival time at the conflict (s), a truck pulling out
            // Clear road.
            Check(!ExitGiveWay.VehicleBlocks(ExitGiveWay.MaxScanMetres + 1f, false, 14f, BrakingDistance(14f), delta), "clear road (nothing in reach): go");
            // An approaching car 60 m away at various speeds: stock reach (braking + 2 s) + speed x own arrival time.
            Check(!ExitGiveWay.VehicleBlocks(60f, false, 0f, 0f, delta), "car standing 60 m before the exit (queue stopped short): go");
            Check(!ExitGiveWay.VehicleBlocks(60f, false, 5f, BrakingDistance(5f), delta), "car at 5 m/s, 60 m away: go");
            Check(ExitGiveWay.VehicleBlocks(60f, false, 12f, BrakingDistance(12f), delta), "car at 12 m/s (43 km/h), 60 m away: wait");
            Check(ExitGiveWay.VehicleBlocks(60f, false, 17f, BrakingDistance(17f), delta), "car at 17 m/s (60 km/h), 60 m away: wait");
            Check(!ExitGiveWay.VehicleBlocks(120f, false, 12f, BrakingDistance(12f), delta), "car at 12 m/s, 120 m away: go");
            // The stock 2 s gap alone would have let the truck go where the projection holds it.
            float gap = 40f, v = 11f;
            bool stockOnly = BrakingDistance(v) + v * ArrivalProjection.StockLookAhead >= gap;
            Check(!stockOnly && ExitGiveWay.VehicleBlocks(gap, false, v, BrakingDistance(v), delta), "stock 2 s look-ahead says clear, the arrival-time projection says wait (owner screenshot: truck pulls out)");
            // A slower leaving vehicle (longer own arrival time) needs a longer gap.
            Check(!ExitGiveWay.VehicleBlocks(60f, false, 10f, BrakingDistance(10f), 1f) && ExitGiveWay.VehicleBlocks(60f, false, 10f, BrakingDistance(10f), 4f), "longer own arrival time: same approaching car now blocks");
            // On the conflict.
            Check(ExitGiveWay.VehicleBlocks(-1f, true, 0f, 0f, delta), "vehicle standing on the conflict: wait");
            Check(ExitGiveWay.VehicleBlocks(0f, false, 0f, 0f, delta), "front at the conflict start: wait");
            Check(ExitGiveWay.VehicleBlocks(30f, true, 0f, 0f, delta), "occupies (part of it on the conflict): wait whatever the gap");
            Check(ExitGiveWay.VehicleBlocks(float.NaN, false, 10f, 5f, delta), "NaN gap: treated as on the conflict (never bolder)");
            // Where it waits.
            Check(ExitGiveWay.CanHold(3f, M, BrakingDistance(3f)), "slow at 3 m before the road: held there");
            Check(!ExitGiveWay.CanHold(-0.5f, M, 0f), "front past the wait point: committed, the game's own checks decide");
            Check(!ExitGiveWay.CanHold(2f, M, BrakingDistance(10f)), "cannot stop before the road: committed");
        }

        private static void ExitDecisionTests()
        {
            // Coordinator test 1: an UNBOXED exit is held for a gap (the gap check is not gated on a box).
            Check(ExitGiveWay.Decide(true, ExCarPriority, false, true, false, true) == ExitGiveWay.Decision.WaitGap, "unboxed exit, box rule on, gap busy: held");
            Check(ExitGiveWay.Decide(true, ExCarPriority, false, false, false, true) == ExitGiveWay.Decision.WaitGap, "unboxed exit, box rule off, gap busy: held");
            Check(ExitGiveWay.Decide(true, ExCarPriority, true, true, false, false) == ExitGiveWay.Decision.Go, "unboxed exit, gap clear: go (no box, no room test)");
            // Coordinator test 2: a BOXED exit needs both a clear gap AND room past the box.
            Check(ExitGiveWay.Decide(true, ExCarPriority, true, true, true, true) == ExitGiveWay.Decision.Go, "boxed exit: gap clear and room past: go");
            Check(ExitGiveWay.Decide(true, ExCarPriority, false, true, true, true) == ExitGiveWay.Decision.WaitGap, "boxed exit: room past but gap busy: held (gap)");
            Check(ExitGiveWay.Decide(true, ExCarPriority, true, true, true, false) == ExitGiveWay.Decision.WaitBox, "boxed exit with no room: held (box) although the gap is clear");
            Check(ExitGiveWay.Decide(true, ExCarPriority, false, true, true, false) != ExitGiveWay.Decision.Go, "boxed exit: neither: held");
            // Setting off = 0.4.3: only the box rule.
            Check(ExitGiveWay.Decide(false, ExCarPriority, false, true, false, true) == ExitGiveWay.Decision.Go, "setting off, unboxed, gap busy: no hold (0.4.3)");
            Check(ExitGiveWay.Decide(false, ExCarPriority, false, false, true, false) == ExitGiveWay.Decision.Go, "setting off and box rule off: no hold at all");
            Check(ExitGiveWay.Decide(false, ExCarPriority, false, true, true, false) == ExitGiveWay.Decision.WaitBox, "setting off, boxed exit with no room: the 0.4.3 box hold only");
            Check(!ExitGiveWay.Applies(false, ExCarPriority), "setting off: the give-way does not apply");
            // On-duty emergency: stock.
            Check(ExitGiveWay.IsEmergency(108) && !ExitGiveWay.IsEmergency(107) && ExitGiveWay.EmergencyPriority == 108, "on-duty emergency = stock priority 108");
            Check(ExitGiveWay.Decide(true, 108, false, true, false, true) == ExitGiveWay.Decision.Go, "on-duty emergency: not held for a gap");
            Check(!ExitGiveWay.Applies(true, 108) && ExitGiveWay.Applies(true, ExCarPriority), "give-way applies to every vehicle but an on-duty emergency one");
            // Road vehicles are not held: only a drive's LEAVING lane (SideConnection + Yield) takes the S2-29 hold.
            const uint unsafeSide = ExitGiveWay.Unsafe | ExitGiveWay.SideConnection;
            Check(ExitGiveWay.IsDrivewayExitLane(unsafeSide | ExitGiveWay.Yield), "drive leaving lane (Unsafe | SideConnection | Yield): exit give-way");
            Check(!ExitGiveWay.IsDrivewayExitLane(unsafeSide), "drive entering lane (no Yield): a road car turning in is not held");
            Check(!ExitGiveWay.IsDrivewayExitLane(0u) && !ExitGiveWay.IsDrivewayExitLane(ExitGiveWay.Yield) && !ExitGiveWay.IsDrivewayExitLane(ExitGiveWay.TurnLeft | ExitGiveWay.TurnRight),
                "road lanes (plain, give-way sign, turning): not an exit, road cars not held");
        }

        private static void ExitAreaTests()
        {
            // Area exit (0.4.5): a hold short of the join point; committed / unstoppable vehicles are left to the game.
            Check(ExitGiveWay.CanHoldArea(8f, BrakingDistance(5f)), "area exit: 8 m to the stop point at 5 m/s: can hold");
            Check(ExitGiveWay.CanHoldArea(0f, 0f), "area exit: stopped at the stop point: held");
            Check(ExitGiveWay.CanHoldArea(-1f, 0f), "area exit: a stopped vehicle that crept 1 m past the stop point is still held (no release by drift)");
            Check(!ExitGiveWay.CanHoldArea(-3f, 0f), "area exit: clearly past the stop point (on its way onto the road): not held");
            Check(!ExitGiveWay.CanHoldArea(0.5f, BrakingDistance(14f)), "area exit: a fast vehicle that cannot stop in time is left to the game");
            Check(ExitGiveWay.CanHoldArea(30f, BrakingDistance(14f)), "area exit: the same vehicle 30 m before the stop point: held");
            Check(ExitGiveWay.Decide(true, ExCarPriority, false, true, true, true) == ExitGiveWay.Decision.WaitGap
                && ExitGiveWay.Decide(true, ExCarPriority, true, true, true, false) == ExitGiveWay.Decision.WaitBox
                && ExitGiveWay.Decide(true, ExCarPriority, true, true, true, true) == ExitGiveWay.Decision.Go, "lot exit: the same gap AND box decision as every exit");
        }

        private static void ExitModeTests()
        {
            int on = Stage2Mode.For(JunctionGapRule.Stock, false, exitGiveWay: true);
            int off = Stage2Mode.For(JunctionGapRule.Stock, false);
            Check((on & Stage2Mode.ExitGiveWay) != 0 && (off & Stage2Mode.ExitGiveWay) == 0, "the exit bit travels only with the setting on");
            Check((Stage2Mode.Stripped & Stage2Mode.ExitGiveWay) != 0, "exit bit stripped from the 0.2.x mode (every m_Mode == 0 exit unchanged)");
            Check((on & ~Stage2Mode.Stripped) == 0, "setting on alone leaves the stripped stage 2 mode at 0");
            Check((Stage2Mode.ExitGiveWay & (Stage2Mode.BoxEntranceMask | Stage2Mode.BoxObey | Stage2Mode.ZebraMarginMask | 0xFF)) == 0, "exit bit does not overlap any older bit");
            int all = Stage2Mode.For(JunctionGapRule.WholeJunction, true, JunctionWaitingSpot.StopLine, true, true, true, true, 25.5f, Stage2Mode.BoxBits(true, true, true, true, true, 51.1f), true);
            Check((all & Stage2Mode.ExitGiveWay) != 0 && Stage2Mode.BoxEntranceLength(all) > 51f, "every other bit set: exit bit and the entrance length both intact");
            Check(Stage2Mode.RuleName(on).Contains("vehicles leaving buildings give way") && !Stage2Mode.RuleName(off).Contains("leaving buildings"), "rule name says the exit setting");
            var sw = new GapRuleSwitch();
            sw.Step(JunctionGapRule.Stock, false, out _, exitGiveWay: true);
            string note = sw.Step(JunctionGapRule.Stock, false, out _, exitGiveWay: false);
            Check(note != null && note.Contains("switched"), "switching the exit setting is logged as a rule switch");
            // Timing-log counters.
            long[] d = new long[Stage2Counters.Count];
            d[Stage2Counters.ExitHeld] = 7; d[Stage2Counters.ExitHeldAcross] = 3; d[Stage2Counters.ExitHeldSpawn] = 2;
            d[Stage2Counters.ExitGo] = 11; d[Stage2Counters.ExitEmergencyPass] = 1; d[Stage2Counters.ExitSpawnBoxHeld] = 4; d[Stage2Counters.ExitCapHits] = 5;
            string f = Stage2Counters.Format(1, d, 0, 0, 0, 2);
            Check(f.Contains("vehicles leaving buildings give way on") && f.Contains("building exits: waits 7 (turning across 3, spawn/gate 2), go decisions 11, emergency passthroughs 1, boxed spawn exit no room 4, cap hits 5"),
                "timing line: exit waits, go decisions, emergency passthroughs");
            Check(!Stage2Counters.Format(1, d, 0, 0, 0, 1).Contains("building exits:") && Stage2Counters.Format(1, d, 0, 0, 0, 1).Contains("give way off"), "setting off: no exit counters, says off");
            Check(!Stage2Counters.Format(1, d).Contains("leaving buildings"), "0.4.3 callers (no exit mask): unchanged text");
            Check(Stage2Counters.ExitCapHits == Stage2Counters.Count - 1 && Stage2Counters.ExitHeld == Stage2Counters.EntranceHeldLeaving + 1, "exit counters appended after the 0.4.3 ones");
        }

        private static void ExitRepoTests()
        {
            string root = RepoRoot();
            Check(root != null, "repo root found");
            if (root == null) return;
            string it = File.ReadAllText(Path.Combine(root, "JunctionRules", "Vanilla", "JRCarLaneSpeedIterator.cs"));
            string nav = File.ReadAllText(Path.Combine(root, "JunctionRules", "Vanilla", "JRCarNavigationSystem.cs"));
            string glue = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRExitGiveWay.cs"));
            string settings = File.ReadAllText(Path.Combine(root, "JunctionRules", "Settings", "JunctionRulesSettings.cs"));
            int s29 = it.IndexOf("// JR-STAGE2 BEGIN S2-29", StringComparison.Ordinal);
            int s25 = it.IndexOf("// JR-STAGE2 BEGIN S2-25", StringComparison.Ordinal);
            int loop = s29 < 0 ? -1 : it.IndexOf("LaneOverlap laneOverlap = bufferData[i];", s29, StringComparison.Ordinal);
            Check(s29 > s25 && s29 < loop, "S2-29 in CheckOverlappingLanes after S2-25, before the overlap loop");
            string b29 = s29 < 0 ? "" : it.Substring(s29, it.IndexOf("// JR-STAGE2 END", s29, StringComparison.Ordinal) - s29);
            Check(b29.Contains("if (m_JR.m_ExitRule)") && !b29.Contains("m_BoxRule"), "S2-29 gap check gated on the exit setting only, never on a box");
            int s30 = nav.IndexOf("// JR-STAGE2 BEGIN S2-30", StringComparison.Ordinal);
            Check(s30 > 0 && nav.LastIndexOf("IL_09cd:", s30, StringComparison.Ordinal) > s30 - 40 && nav.IndexOf("navigation.m_MaxSpeed = laneIterator.m_MaxSpeed;", s30, StringComparison.Ordinal) > s30,
                "S2-30 right after IL_09cd, before the max speed is taken");
            string b30 = s30 < 0 ? "" : nav.Substring(s30, nav.IndexOf("// JR-STAGE2 END", s30, StringComparison.Ordinal) - s30);
            Check(b30.Contains("m_JR.m_ExitRule") && !b30.Contains("m_BoxRule"), "S2-30 gated on the exit setting only");
            int s31 = nav.IndexOf("// JR-STAGE2 BEGIN S2-31", StringComparison.Ordinal);
            Check(s31 > s30 && nav.IndexOf("ExitWaiting", s31, StringComparison.Ordinal) > s31, "S2-31 keeps a held vehicle on its Connection lane");
            // 0.4.5: lot (Area lane) exits. 0.4.4 gated only a Connection current lane (S2-30) and the usual building exit is an Area lane.
            int s32 = nav.IndexOf("// JR-STAGE2 BEGIN S2-32", StringComparison.Ordinal);
            int tailIter = nav.LastIndexOf("laneIterator.IterateTarget(navigation.m_TargetPosition, num3);", s32 < 0 ? 0 : s32, StringComparison.Ordinal);
            int tailEnd = s32 < 0 ? -1 : nav.IndexOf("goto IL_0a0a;", s32, StringComparison.Ordinal);
            Check(s32 > 0 && s32 < s30 && tailIter > 0 && tailIter < s32 && tailEnd > s32 && nav.IndexOf("IL_09cd:", tailIter, StringComparison.Ordinal) > tailEnd,
                "S2-32 in the Area-lane tail (after IterateTarget, before goto IL_0a0a), not in the Connection / road branch");
            string b32 = s32 < 0 ? "" : nav.Substring(s32, nav.IndexOf("// JR-STAGE2 END", s32, StringComparison.Ordinal) - s32);
            Check(b32.Contains("m_JR.m_ExitRule") && b32.Contains("CarLaneFlags.Area") && b32.Contains("ExitAreaHold(") && b32.Contains("navigationLanes[0]") && !b32.Contains("m_BoxRule"),
                "S2-32 gated on the exit setting and the Area flag, hands the first navigation lane, never on a box");
            int s33 = nav.IndexOf("// JR-STAGE2 BEGIN S2-33", StringComparison.Ordinal);
            Check(s33 > s31 && nav.IndexOf("ApplySideEffects(ref currentLane", s33, StringComparison.Ordinal) > s33 && nav.IndexOf("ExitWaiting", s33, StringComparison.Ordinal) > s33
                && nav.IndexOf("ExitWaiting", s33, StringComparison.Ordinal) < nav.IndexOf("ApplySideEffects(ref currentLane", s33, StringComparison.Ordinal),
                "S2-33 keeps a held lot-exit vehicle on its area lane, before the lane switch");
            Check(glue.Contains("ExitAreaHold(") && glue.Contains("ExitJoinLane(") && glue.Contains("ExitJoinWaits(") && glue.Split(new[] { "ExitJoinWaits(" }, StringSplitOptions.None).Length >= 4,
                "Connection and Area exits share one join-lane test and one decision");
            Check(nav.Contains("SetExits(m_JRExitBuildings, m_JRExitEdges, m_LeftHandTraffic)"), "traffic side from the game's own m_LeftHandTraffic (never hardcoded)");
            Check(!nav.Contains("m_JRBuildingData"), "no 0.4.2 building lookup name reused");
            // The connection exit: the gap is checked first, the box only as the second condition (Core Decide).
            Check(glue.Contains("ExitGiveWay.Decide(true, priority, !busy, m_BoxRule, boxed, room)"), "spawn exit: one decision with gap AND box");
            int dh = glue.IndexOf("public float ExitDrivewayHold", StringComparison.Ordinal);
            int dhEnd = glue.IndexOf("private static bool ExitConflict", StringComparison.Ordinal);
            Check(dh > 0 && dhEnd > dh && !glue.Substring(dh, dhEnd - dh).Contains("m_BoxRule"), "driveway gap check never reads the box rule (the 0.4.3 band hold stays in S2-28)");
            Check(glue.Contains("ExitGiveWay.IsEmergency(priority)"), "both hooks let on-duty emergency vehicles through");
            Check(settings.Contains("ExitGiveWay = true;") && settings.Contains("\"Vehicles leaving buildings give way\""), "setting default on, label");
            Check(settings.Contains("Cars coming out of ANY building must give way, and when that building exit is a yellow box, must ALSO obey the yellow box rules."), "settings description quotes the owner rule");
        }
    }
}
