using System;
using System.Collections.Generic;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// JR-T-013b. G3: a turner across oncoming traffic (UK right-turn exception) re-runs the box exit-room test at its
    /// turn spot (Core BoxDriving.TurnSpotRoomCheck, Stage2/JRYellowBox.cs BoxTurnRoomHold, block S2-37). G4: the
    /// default-off watch-one-junction log (Core/JunctionWatch.cs, Stage2/JRJunctionWatch.cs, S2-20..S2-23).
    /// </summary>
    internal static partial class Program
    {
        private const float G3Lane = 20f;   // node lane of the boxed 4-way, metres
        private const float G3Spot = 0.55f; // the turn spot (driving coordinates): start of the first oncoming overlap

        /// <summary>A turner on its boxed node lane: front at <paramref name="front"/> (driving), braking distance.</summary>
        private static StretchState G3Car(float front, float braking, bool onLane = true)
        {
            BoxDriving.JunctionSpots(true, G3Spot, out WaitingSpots spots);
            var pts = new ZConflicts { Items = new[] { Cv(0.3f, 0.35f), Cv(G3Spot, 0.62f) } };
            var s = default(StretchState);
            // On the lane: the game's distance to the car's own offset is 0, minus the margin. Approaching: 10 m to the line.
            s.BeginWithSpots(ref pts, spots, onLane ? -M : 10f - M, G3Lane, onLane ? front : 0f, M, braking);
            return s;
        }

        private static bool G3Check(StretchState s, int turnIndex = 1) =>
            BoxDriving.TurnSpotRoomCheck(turnIndex, s.Next, s.Spots.Count, turnIndex >= 0 && turnIndex < s.Spots.Count && s.CanStopBefore(s.Spots[turnIndex]));

        private static void G3TurnSpotRoomTests()
        {
            // When the room test runs again (the turn spot is the next spot and the car can stop before it).
            StretchState waiting = G3Car(0.30f, 0f);
            Check(waiting.Next == 1 && G3Check(waiting), "turner standing in the box before its turn spot: exit room tested again at the turn spot");
            StretchState rolling = G3Car(0.20f, BrakingDistance(4f));
            Check(rolling.Next == 1 && G3Check(rolling), "turner rolling slowly to its turn spot (can stop): tested");
            StretchState approach = G3Car(0f, BrakingDistance(5f), onLane: false);
            Check(approach.Next == 0 && !G3Check(approach), "before the stop line: the line's own room test (BoxHold), not this one");
            StretchState crossing = G3Car(0.60f, BrakingDistance(4f));
            Check(crossing.Next == 2 && !G3Check(crossing), "front past the turn spot (crossing the oncoming lane): never held there (no stop across the lane)");
            StretchState fast = G3Car(0.30f, BrakingDistance(15f));
            Check(fast.Next == 1 && !G3Check(fast), "cannot stop before the turn spot any more (game's can-stop test): carries on");
            Check(!G3Check(waiting, -1), "no turn spot (exception off, straight, T junction, zebra refused it): nothing new");
            Check(!G3Check(waiting, 0), "index 0 is the stop line: never the turn spot");
            Check(!BoxDriving.TurnSpotRoomCheck(2, 2, 2, true), "turn index past the spots: nothing");

            // LHT both directions on the main road: the turn spot comes from the overlap in driving coordinates, so a
            // lane drawn the other way (inverse) gives the same spot and the same test.
            int f = ZebraSections.SpotPoint;
            StretchGeometry.ToConflict(f, 140, 158, false, false, out float east, out _);
            StretchGeometry.ToConflict(f, 255 - 158, 255 - 140, true, false, out float west, out _);
            Check(Near(east, west), "LHT: eastbound and westbound right-turners get the same turn spot (inverse lane mirrored)");
            Check(BoxDriving.TurnerMayWait(true, TurnAcross.TurnRight, true) && !BoxDriving.TurnerMayWait(true, TurnAcross.TurnRight, false) && BoxDriving.TurnerMayWait(true, TurnAcross.TurnLeft, false),
                "the re-test applies to the turn across oncoming traffic only (LHT right, RHT left)");

            // The exit room (RoomScan, the line's own measure): own path only.
            Check(!BoxRoom(BCar, 1f, 0, (2f, BCar, 0f)), "G3: exit queue standing 2 m past the box: the turner stays at its turn spot (not across the oncoming lane)");
            Check(BoxRoom(BCar, 1f, 0), "gap and empty exit: the turner goes");
            Check(BoxRoom(BCar, 1f, 0, (2f, BCar, BrakingDistance(8f))), "exit queue moving: goes (no crawl)");
            Check(BoxRoom(BCar, 1f, 0, (10f, BCar, 0f)) && !BoxRoom(BCar, 1f, 1, (10f, BCar, 0f)),
                "a car ahead of it in the box on the same exit takes 5.5 m of exit room first");
            Check(!BoxRoom(BArtic, 1f, 0, (13f, BCar, 0f)), "an 18 m truck needs its whole length on the exit before it crosses");

            // No new deadlock: the hold reads only the turner's own path (rest of its node lane, then its exit lanes). The
            // oncoming straight lane is never an input, so the hold cannot wait on the oncoming queue; it ends when its
            // exit moves. And the turner stands BEFORE the first oncoming overlap (stop point at the turn spot minus the
            // margin), where the stock lane-object test of an oncoming straight car skips it.
            StretchState held = G3Car(G3Spot - M / G3Lane, 0f);
            Check(held.Next == 1 && held.Wait(G3Spot) <= 0.01f && held.Wait(G3Spot) >= -0.01f, "held turner: front at the turn spot minus the margin");
            Check(G3Spot - M / G3Lane < G3Spot, "its front is before the oncoming overlap start: oncoming straight traffic is not blocked");
        }

        private static void G3CounterTests()
        {
            Check(Stage2Counters.BoxTurnRoomWaits == Stage2Counters.ExitCapVehicles + 1 && Stage2Counters.BoxTurnRoomWaits == Stage2Counters.Count - 21, "G3 counter appended after the G1 counters (the review F1 counter after it)");
            var dl = new long[Stage2Counters.Count];
            dl[Stage2Counters.BoxTurnRoomWaits] = 5;
            dl[Stage2Counters.BoxTurnWaits] = 9;
            string on = Stage2Counters.Format(2, dl, 2, 2, 2, 2);
            int exitPart = on.IndexOf("; building exits:", StringComparison.Ordinal), g3 = on.IndexOf("; right-turn waits for exit room 5", StringComparison.Ordinal);
            Check(g3 > exitPart && exitPart > 0 && on.IndexOf("; points at W", StringComparison.Ordinal) > g3, "timing log: the G3 count at the end of the text (before the points): " + on);
            Check(on.Contains("right-turn waits 9;"), "the 0.4.2 right-turn wait count is unchanged");
            Check(!Stage2Counters.Format(2, dl, 2, 2, 1, 2).Contains("exit room"), "box setting off: no G3 count");
        }

        private static void G3RepoTests()
        {
            string root = RepoRoot();
            string src = Path.Combine(root, "JunctionRules");
            string it = File.ReadAllText(Path.Combine(src, "Vanilla", "JRCarLaneSpeedIterator.cs"));
            int s25 = it.IndexOf("// JR-STAGE2 BEGIN S2-25:", StringComparison.Ordinal);
            int s35 = it.IndexOf("// JR-STAGE2 BEGIN S2-37:", StringComparison.Ordinal);
            int s29 = it.IndexOf("// JR-STAGE2 BEGIN S2-29:", StringComparison.Ordinal);
            Check(s25 > 0 && s35 > s25 && s29 > s35, "S2-37 after S2-25 (zebra spots known) and before S2-29");
            Check(it.IndexOf("// JR-STAGE2 BEGIN S2-37:", s35 + 1, StringComparison.Ordinal) < 0, "S2-37 once");
            string block = it.Substring(s35, it.IndexOf("JR-STAGE2 END", s35, StringComparison.Ordinal) - s35);
            Check(block.Contains("if (m_JR.m_BoxRule && !JunctionRules.Core.ExitGiveWay.IsEmergency(m_Priority))") && block.Contains("m_JR.BoxTurnRoomHold(") && block.Contains("m_Blocker = m_JR.HoldBlocker;") && block.Contains("m_BlockerType = BlockerType.Continuing;"),
                "S2-37 gated by the box setting; 0.4.9 (G17): names the vehicle that took its exit room as blocker");
            Check(it.Substring(0, 2000).Contains("S2-29, S2-37") /* JR-T-014d: S2-50 ids follow */, "iterator header lists S2-37");
            string rules = File.ReadAllText(Path.Combine(root, "tools", "vanilla_rules.json"));
            string dev = File.ReadAllText(Path.Combine(root, "DEVIATIONS.md"));
            Check(rules.Contains("\"S2-37\"") && dev.Contains("| S2-37 |"), "S2-37 declared in vanilla_rules.json and DEVIATIONS.md");
            string yb = File.ReadAllText(Path.Combine(src, "Stage2", "JRYellowBox.cs"));
            int m = yb.IndexOf("public float BoxTurnRoomHold(", StringComparison.Ordinal);
            string body = yb.Substring(m, yb.IndexOf("\n        }\n", m, StringComparison.Ordinal) - m);
            Check(body.Contains("BoxDriving.TurnSpotRoomCheck(") && body.Contains("HasRoomPast(lane, 1f,") && body.Contains("m_BoxTurnIndex"), "BoxTurnRoomHold: Core check, room on its own path, the turn spot index");
            Check(!body.Contains("m_LaneOverlaps") && !body.Contains("overlaps["), "BoxTurnRoomHold reads no overlap lane (no wait on oncoming traffic)");
        }

        private static void WatchTests()
        {
            long car = JunctionWatch.Key(373495, 1);
            Check(JunctionWatch.KeyIndex(car) == 373495 && JunctionWatch.KeyVersion(car) == 1 && JunctionWatch.EntityText(car) == "Entity(373495:1)", "entity key round trip");
            Check(JunctionWatch.Key(0, 0) == 0 && JunctionWatch.EntityText(0) == "none", "Entity.Null = 0 = none");
            long packed = JunctionWatch.Pack((int)JunctionWatch.Rule.BoxTurnRoom, 1, 0.12f, 0f);
            JunctionWatch.Unpack(packed, out int rule, out int type, out float speed, out float max);
            Check(rule == 1 && type == 1 && Near(speed, 0.12f) && max == 0f, "pack / unpack");
            JunctionWatch.Unpack(JunctionWatch.Pack(13, 8, 900f, -1f), out _, out int t8, out float big, out float neg);
            Check(t8 == 8 && Near(big, 655.35f) && neg == 0f, "speeds clamped to 0..655.35 m/s");
            string line = JunctionWatch.Line(car, JunctionWatch.Key(12, 3), JunctionWatch.Key(77, 1), JunctionWatch.Pack(0, 1, 0f, 0f));
            Check(line == "car Entity(373495:1) stands (0.0 m/s, limit 0.0 m/s) on lane Entity(77:1); waits for Entity(12:3) (Continuing); rule: game's own (give-way / car ahead / signal, see blocker)", "log line: " + line);
            Check(JunctionWatch.Line(car, 0, 0, JunctionWatch.Pack(3, 0, 0f, 0f)).Contains("waits for no vehicle named (None); rule: yellow box: held at the line"), "JR hold: no blocker vehicle, the rule named");
            for (int r = 0; r <= 13; r++) Check(!JunctionWatch.RuleName(r).StartsWith("rule "), "rule name " + r);
            Check(JunctionWatch.BlockerTypeName(8) == "Oncoming" && JunctionWatch.BlockerTypeName(2) == "Crossing", "blocker type names (Game.Vehicles.BlockerType order)");

            // Ring buffer: writer = claim, values, stamp last; drain reads in order, stops at an unstamped slot.
            var a = new long[JunctionWatch.Length];
            void W(long c)
            {
                long seq = a[1]++;
                int s = JunctionWatch.Slot(seq);
                a[s + 1] = c; a[s + 2] = 0; a[s + 3] = 0; a[s + 4] = 0; a[s] = seq + 1;
            }
            long readPos = 0;
            var ev = new List<(long, long, long, long)>();
            W(11); W(12);
            JunctionWatch.Drain(i => a[i], ref readPos, a[1], ev, out int dropped);
            Check(ev.Count == 2 && ev[0].Item1 == 11 && ev[1].Item1 == 12 && dropped == 0 && readPos == 2, "drain: events in order");
            long claimed = a[1]++; // claimed, not stamped yet (job still running)
            ev.Clear();
            JunctionWatch.Drain(i => a[i], ref readPos, a[1], ev, out dropped);
            Check(ev.Count == 0 && readPos == claimed, "drain: an unstamped slot waits for the next step");
            a[JunctionWatch.Slot(claimed) + 1] = 13; a[JunctionWatch.Slot(claimed)] = claimed + 1;
            JunctionWatch.Drain(i => a[i], ref readPos, a[1], ev, out dropped);
            Check(ev.Count == 1 && ev[0].Item1 == 13, "drain: read once stamped");
            for (int i = 0; i < JunctionWatch.Capacity + 10; i++) W(100 + i);
            ev.Clear();
            JunctionWatch.Drain(i => a[i], ref readPos, a[1], ev, out dropped);
            Check(dropped == 10 && readPos == a[1] && ev.Count == JunctionWatch.Capacity && ev[0].Item1 == 110, "drain: more than the ring holds: the oldest counted as dropped");

            // Rate limit.
            var lim = new WatchLimiter();
            Check(lim.Tick(0) == null, "first window opens silently");
            Check(lim.Allow(1, 0, 5, 0) && !lim.Allow(1, 0, 5, 16), "same car, rule and blocker: one line per repeat period");
            Check(lim.Allow(1, 3, 0, 16) && lim.Allow(1, 0, 6, 16), "a new rule or a new blocker for the same car: logged");
            Check(lim.Allow(1, 0, 5, WatchLimiter.RepeatFrames), "after the repeat period: logged again");
            int allowed = 0;
            for (int c = 10; c < 60; c++) if (lim.Allow(c, 0, 0, 20)) allowed++;
            Check(allowed == WatchLimiter.MaxLines - 4, "line cap per window");
            string sum = lim.Tick(WatchLimiter.WindowFrames + 30);
            Check(sum != null && sum.StartsWith("35 more hold event(s)"), "window summary counts the rest: " + sum);
            Check(lim.Tick(WatchLimiter.WindowFrames + 31) == null, "same window: no second summary");

            // Default off: no node, nothing handed over; set / clear bump the version.
            int v = WatchHandoff.Version;
            WatchHandoff.Clear();
            Check(WatchHandoff.Node == 0 && WatchHandoff.Version == v, "nothing watched by default; clearing nothing changes nothing");
            WatchHandoff.Set(car);
            Check(WatchHandoff.Node == car && WatchHandoff.Version == v + 1, "set");
            WatchHandoff.Clear();
            Check(WatchHandoff.Node == 0 && WatchHandoff.Version == v + 2, "clear");
        }

        private static void WatchRepoTests()
        {
            string root = RepoRoot();
            string src = Path.Combine(root, "JunctionRules");
            string nav = File.ReadAllText(Path.Combine(src, "Vanilla", "JRCarNavigationSystem.cs"));
            int s22 = nav.IndexOf("// JR-STAGE2 BEGIN S2-22", StringComparison.Ordinal);
            string b22 = nav.Substring(s22, nav.IndexOf("JR-STAGE2 END", s22, StringComparison.Ordinal) - s22);
            Check(b22.Contains("if ((m_JRMode & JunctionRules.Core.Stage2Mode.Count) != 0)") && b22.Contains("laneIterator.m_JR.Watch(m_JRWatch, entity,"), "S2-22: watch events only with the count bit");
            int s21 = nav.IndexOf("// JR-STAGE2 BEGIN S2-21", StringComparison.Ordinal);
            string b21 = nav.Substring(s21, nav.IndexOf("JR-STAGE2 END", s21, StringComparison.Ordinal) - s21);
            Check(b21.Contains("SetWatch(m_JRWatch)"), "S2-21 hands the watched node to the iterator");
            int s23 = nav.IndexOf("// JR-STAGE2 BEGIN S2-23", StringComparison.Ordinal);
            Check(nav.Substring(s23, nav.IndexOf("JR-STAGE2 END", s23, StringComparison.Ordinal) - s23).Contains("jobData.m_JRWatch = JunctionRules.Stage2.JRStage2Bridge.Watch;"), "S2-23 passes the watch array");
            string watch = File.ReadAllText(Path.Combine(src, "Stage2", "JRJunctionWatch.cs"));
            Check(!watch.Contains("m_MaxSpeed") && !watch.Contains("m_Blocker =") && !watch.Contains("ref ComponentLookup"), "watch has no driving effect (writes only the shared array)");
            string sw = File.ReadAllText(Path.Combine(src, "Systems", "CarDrivingSwitchSystem.cs"));
            Check(sw.Contains("probes || WatchHandoff.Node != 0"), "a watched node switches the counters' per-car results on");
            string set = File.ReadAllText(Path.Combine(src, "Settings", "JunctionRulesSettings.cs"));
            Check(set.Contains("public bool WatchCameraJunction { set { WatchHandoff.PickRequested = true; } }") && set.Contains("public bool WatchStop {"), "settings: watch buttons (no saved value: default off)");
            Check(set.Contains("nameof(JunctionRulesSettings.WatchCameraJunction)), \"Watch the junction at camera centre (log)\""), "settings: locale text");
        }
    }
}
