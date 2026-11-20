using System;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.11 (JR-T-017, gap G23, owner live 17 Nov: "what makes this car not want to come out on green?"). Crescent St x
    /// Eastgate Lane (signalled, boxed since 0.4.8): hatchback 910624, head of the Eastgate south arm, short LEFT turn into
    /// Crescent westbound on green, exit lane empty, westbound Crescent at its red, people waiting at the kerbs; blocker type
    /// Temporary (the game's pedestrian blocker; CheckPedestrian sets it, JR's 0.3.2 NoPushPedestrian keeps it at a full stop).
    /// </summary>
    internal static partial class Program
    {
        private static void G23Tests()
        {
            // Crossing lane across the west arm, 10 m kerb to kerb; the left turn hugs the south corner: its range on the
            // crossing 0.08..0.40 (0.8..4.0 m from the south kerb end). People waiting at the south kerb end stand at 0.0..0.03.
            float len = 10f, a = 0.08f, b = 0.40f;
            Check(!CrossingPush.PersonInPath(0.0f, a, b, len) && !CrossingPush.PersonInPath(0.02f, a, b, len), "people waiting at the kerb end (0-0.2 m) are beside the car's way, not on it");
            Check(CrossingPush.PersonInPath(0.05f, a, b, len), "within 0.5 m of the car's range: on its way (tolerance)");
            Check(CrossingPush.PersonInPath(0.2f, a, b, len) && CrossingPush.PersonInPath(0.2f, b, a, len), "a person standing on the crossing in the car's range: on its way (either range order)");
            Check(!CrossingPush.PersonInPath(0.8f, a, b, len), "a person waiting at the far (north) end: not on its way");
            float stockCreep = 3f, fullStop = 0f;
            Check(CrossingPush.PedestrianSpeedInPath(true, CrossingPush.PushReason.Standing, false, stockCreep, fullStop) == stockCreep,
                "G23: standing person beside the way: the game's creep past (0.4.10 held the car at 0 until the people's green = the car's red)");
            Check(CrossingPush.PedestrianSpeedInPath(true, CrossingPush.PushReason.Timer, false, stockCreep, fullStop) == stockCreep, "also after the 5 s push timer");
            Check(CrossingPush.PedestrianSpeedInPath(true, CrossingPush.PushReason.Standing, true, stockCreep, fullStop) == fullStop
                  && CrossingPush.PedestrianSpeedInPath(true, CrossingPush.PushReason.Timer, true, stockCreep, fullStop) == fullStop,
                "a person ON the car's way: full stop, never pushed through (0.3.2 kept)");
            Check(CrossingPush.PedestrianSpeedInPath(true, CrossingPush.PushReason.None, false, 2f, 1f) == 2f, "a walking person: the game's own braking (unchanged)");

            // --- owner: vehicles stopped at a red never count as approaching traffic ---
            Check(ExitGiveWay.QueuedAtRedNotPresent(false, 4f, 0f, true), "standing 4 m before the conflict on a lane that is red ahead: not present");
            Check(!ExitGiveWay.QueuedAtRedNotPresent(false, 4f, 0f, false), "the same vehicle with green / no light ahead: present (G7 rule kept)");
            Check(!ExitGiveWay.QueuedAtRedNotPresent(true, 0f, 0f, true), "standing ON the conflict: still blocks");
            Check(!ExitGiveWay.QueuedAtRedNotPresent(false, 4f, 3f, true), "moving towards the red: judged as before (it may pass the conflict before its stop line)");
            Check(!ExitGiveWay.VehicleBlocks(4f, false, 0f, 0f, 3f, true), "so a red-held standing vehicle reaches nothing in the gap test");
            // Whole junction / feeding lanes: a car that can stop at the red skips (0.2.4 FeedingSignal, unchanged).
            Check(FeedingSignal.Skips(true, WholePathRule.SignalStop, 0, 100, 0f, 5f), "feeding-lane car at its red (can stop): skipped by the whole-junction projection");
        }

        /// <summary>
        /// Owner after watching it: "it was blocked by a pedestrian that waits a bit outside the kerb". Crossing lane 10 m; the
        /// game's waiting spot for a red walk signal is the crossing's own end point (HumanNavigationSystem 1160-1171), offset
        /// sideways along the kerb; a waiting person's body reaches up to ~0.5 m past it.
        /// </summary>
        private static void G23WaitingTests()
        {
            float len = 10f;
            Check(CrossingPush.InWaitZone(0.0f, len) && CrossingPush.InWaitZone(0.05f, len) && CrossingPush.InWaitZone(0.1f, len) && CrossingPush.InWaitZone(0.95f, len),
                "0-1 m from either end of the crossing: the waiting area");
            Check(!CrossingPush.InWaitZone(0.12f, len) && !CrossingPush.InWaitZone(0.5f, len), "beyond 1 m: on the crossing");
            Check(!CrossingPush.InWaitZone(0.5f, 2f) && CrossingPush.InWaitZone(0.2f, 2f) && !CrossingPush.InWaitZone(0.3f, 2f), "final review F3: a 2 m lane: the waiting area is its first / last 0.5 m, never the whole lane");
            string wjs = File.ReadAllText(Path.Combine(RepoRoot(), "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            Check(wjs.Contains("bool notCrossing = CrossingPush.NotCrossing(standing, CrossingPush.InWaitZone(personPos, crossingLength), false);") && wjs.Contains("if (unmarkedFloor) { m_PersonWaitingNow = false; return false; }"),
                "final review F1 / F4: no red-walk-anywhere skip (a person standing out on a signalled crossing still holds the car); the G11 floor is neither skipped nor counted");
            Check(CrossingPush.NotCrossing(true, true, false), "standing just off the kerb (the owner's case): not crossing, never holds the car");
            Check(CrossingPush.NotCrossing(true, false, true), "standing at a red walk signal, anywhere: not crossing (cars with green go)");
            Check(!CrossingPush.NotCrossing(false, true, false) && !CrossingPush.NotCrossing(false, false, true), "walking (stepping off, or late on red): crossing, the game brakes for them");
            Check(!CrossingPush.NotCrossing(true, false, false), "standing 3 m out on a crossing with no red (stopped mid-crossing, zebra): crossing, never pushed through");
            // The owner's car: left turn, its range on the crossing 0.0-0.4, person waiting at 0.5 m (pos 0.05), standing.
            bool inPath = CrossingPush.PersonInPath(0.05f, 0f, 0.4f, len);
            Check(inPath && CrossingPush.NotCrossing(true, CrossingPush.InWaitZone(0.05f, len), false),
                "the owner's case: the waiting person IS inside the car's range (so the 0.4.11 first cut still held it); now classified as waiting and skipped");

            // --- watch pick: nearest to the camera's aim point within 25 m ---
            var origin = new V3(0, 200, -200);
            var dir = new V3(0, -1, 1);   // 45 degrees down, aim point (0, 0, 0) at ground height
            var nodes = new System.Collections.Generic.List<V3> { new V3(14, 0, 3), new V3(-30, 0, 0), new V3(0, 0, -20) };
            int k = JunctionWatch.PickNearPivot(origin, dir, nodes, JunctionWatch.PivotRadius, out double d);
            Check(k == 0 && Math.Abs(d - Math.Sqrt(14 * 14 + 3 * 3)) < 1e-6, $"off-centre camera: picks the node 14.3 m from the aim point (the 8 m line pick found none), got {k} at {d:0.0} m");
            Check(JunctionWatch.PickNearPivot(origin, dir, new System.Collections.Generic.List<V3> { new V3(-30, 0, 0) }, JunctionWatch.PivotRadius, out _) == -1, "nothing within 25 m: none");
            Check(JunctionWatch.PickNearPivot(origin, dir, new System.Collections.Generic.List<V3> { new V3(5, 10, 10) }, JunctionWatch.PivotRadius, out double d2) == 0 && d2 < 25,
                "a node above ground height: measured where the centre line reaches its height");
        }

        private static void G23RepoTests()
        {
            string root = RepoRoot();
            string copy = File.ReadAllText(Path.Combine(root, "JunctionRules", "Vanilla", "JRCarLaneSpeedIterator.cs"));
            Check(copy.Contains("m_PrefabCar, m_SafeTimeStep, m_SpeedRange, laneObject.m_CurvePosition.x, m_CurveOffset, m_Curve.m_Length, "), "S2-08b hands the person's position and the car's range on the crossing");
            string wj = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            Check(wj.Contains("bool inPath = CrossingPush.PersonInPath(personPos, carRange.x, carRange.y, crossingLength);") && wj.Contains("CrossingPush.PedestrianSpeedInPath(true, reason, inPath, stock, fullStop)"), "NoPushPedestrian full-stops only people on the way");
            string ex = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRExitGiveWay.cs"));
            Check(copy.Contains("if (m_JR.m_NoPushPeople && m_JR.PersonNotCrossing(m_Lane, laneObject.m_LaneObject, laneObject.m_CurvePosition.x, m_Curve.m_Length, ref m_MovingData, ref m_LaneSignalData, jrUnmarked == JunctionRules.Core.UnmarkedCrossing.Floor))"),
                "S2-53: waiting people skipped before the game's pedestrian check; the G11 floor never skipped");
            int s53 = copy.IndexOf("JR-STAGE2 BEGIN S2-53", StringComparison.Ordinal), s08 = copy.IndexOf("JR-STAGE2 BEGIN S2-08:", StringComparison.Ordinal), s50 = copy.IndexOf("JR-STAGE2 BEGIN S2-50:", StringComparison.Ordinal);
            Check(s50 > 0 && s53 > s50 && s08 > s53, "S2-53 sits after S2-50 (G11) and before S2-08 (zebra / no-push)");
            string br = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRStage2Bridge.cs"));
            Check(br.Contains("+ BlockerDetail(e.Blocker));") && br.Contains("WAITING for a walk signal"), "watch lines name the blocker, its position and whether it waits for a walk signal");
            Check(ex.Contains("redKnown = LaneEndsAtRed(lane, ref reads) ? 1 : 2;") && ex.Contains("BoxDriving.LightStopsInWindow((int)sig.m_Signal)"), "exit / entry scans: a queue standing at a red is not present");
        }
    }
}
