using System;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.11 (JR-T-017, gap G22), owner rule 17 Nov 2026: "Cars from a yielding (or if everyone is yielding, then the one
    /// getting STOP is 'prioritised' vs YIELD exits) approach: may enter a box whose exit is blocked iff the spot where the car
    /// would wait (its whole body) only crosses paths into the same exit lane it is queued for." Scenes in metres along the
    /// car's junction lane (driving direction), left-hand traffic, lanes 3.5 m.
    /// </summary>
    internal static partial class Program
    {
        private const uint G22Stop = 0x800u, G22Yield = 0x400u, G22Row = 0x4000000u, G22Lights = 0x8000000u, G22Unsafe = 0x1u, G22Fwd = 0x100000u;

        private struct G22Paths : IFootprintPaths
        {
            public float[] S, E;
            public SameExitException.Path[] K;
            public int Count => S.Length;
            public bool Get(int i, out float start, out float end, out SameExitException.Path kind) { start = S[i]; end = E[i]; kind = K[i]; return true; }
        }

        private static G22Paths G22P(params (float s, float e, SameExitException.Path k)[] p)
        {
            var r = new G22Paths { S = new float[p.Length], E = new float[p.Length], K = new SameExitException.Path[p.Length] };
            for (int i = 0; i < p.Length; i++) { r.S[i] = p[i].s; r.E[i] = p[i].e; r.K[i] = p[i].k; }
            return r;
        }

        private static bool G22Admit(float laneLen, float zebra, float rearAhead, float ownLen, G22Paths paths)
        {
            SameExitException.Footprint(laneLen, zebra, rearAhead, ownLen, 1f, out float from, out float to);
            return SameExitException.Admits(from, to, ref paths);
        }

        private static void G22Tests()
        {
            const SameExitException.Path Same = SameExitException.Path.SameExit, Other = SameExitException.Path.OtherExit, Ped = SameExitException.Path.Other;
            const float none = float.MaxValue;

            // --- eligible approaches ---
            int stopR = SameExitException.Rank(G22Stop | G22Fwd), yieldR = SameExitException.Rank(G22Yield | G22Fwd), prioR = SameExitException.Rank(G22Fwd), rowR = SameExitException.Rank(G22Row | G22Fwd);
            Check(stopR == 0 && yieldR == 1 && prioR == 2 && rowR == 2, "ranks: STOP 0 < YIELD 1 < priority / right of way 2");
            Check(SameExitException.Rank(G22Yield | 0x200000u) == 2, "an Approach lane has its signs cleared (stock)");
            // T junction: priority road + YIELD side road
            Check(SameExitException.Eligible(yieldR, 1, 2, false) && !SameExitException.Eligible(prioR, 1, 2, false), "T: the YIELD side road is eligible, the priority road is not");
            // priority road + one YIELD + one STOP arm: both yielding arms
            Check(SameExitException.Eligible(yieldR, 0, 2, false) && SameExitException.Eligible(stopR, 0, 2, false), "priority road present: every yielding arm (YIELD and STOP)");
            // all yield: STOP vs YIELD
            Check(SameExitException.Eligible(stopR, 0, 1, false) && !SameExitException.Eligible(yieldR, 0, 1, false), "all-yield junction, STOP and YIELD arms: only the STOP arms");
            Check(SameExitException.Eligible(stopR, 0, 0, false) && SameExitException.Eligible(yieldR, 1, 1, false), "all-STOP or all-YIELD: every arm");
            Check(!SameExitException.Eligible(yieldR, 1, 2, true), "signals / level crossing / roundabout: never");
            Check(SameExitException.Excludes(G22Lights) && SameExitException.Excludes(0x40u) && SameExitException.Excludes(0x400000u) && !SameExitException.Excludes(G22Yield), "excluded junction kinds");
            Check(!SameExitException.Ranked(G22Unsafe) && !SameExitException.Ranked(0x8u) && SameExitException.Ranked(G22Yield), "U-turn (Unsafe) and drive lanes do not rank (an unsigned U-turn must not look like a priority road)");

            // --- T junction, side road (YIELD) from the south, priority road east-west, left-hand traffic ---
            // Left turn into the westbound (near) lane: 11 m junction lane; the westbound through path merges into the same exit lane over its last 5 m.
            var leftIn = G22P((6f, 11f, Same));
            Check(G22Admit(11f, none, none, 4.5f, leftIn), "T left turn, car: waits at the lane end where only the westbound through path (same exit lane) merges: may enter");
            // Right turn into the eastbound (far) lane: 19 m; crosses the westbound through lane at 5-9 m (another exit lane), merges with the eastbound through over 14-19 m.
            var rightIn = G22P((5f, 9f, Other), (14f, 19f, Same));
            Check(G22Admit(19f, none, none, 4.5f, rightIn), "T right turn, car (4.5 m): body 13.5-18 m touches only the eastbound merge: may enter");
            Check(!G22Admit(19f, none, none, 18.5f, rightIn), "T right turn, artic (18.5 m): body reaches back over the westbound through path: waits before the box");
            // Box chain: a car already standing ahead on its lane with its rear at 13 m: footprint 6.5-12 m now covers the westbound crossing.
            Check(!G22Admit(19f, none, 13f, 4.5f, rightIn), "a vehicle already waiting ahead pushes the footprint back over the through path: waits before the box");
            // Exit zebra at 16-19 m: footprint before it (10.5-15 m) touches the same-exit merge only.
            var withZebra = G22P((5f, 9f, Other), (14f, 19f, Same), (16f, 19f, Ped));
            Check(G22Admit(19f, 16f, none, 4.5f, withZebra), "exit zebra: the footprint stands before it (zebra rule) and touches only the same-exit merge: may enter");
            Check(!G22Admit(19f, none, none, 4.5f, withZebra), "a footprint on a pedestrian crossing is never admitted (conservative)");

            // --- 4-way, YIELD side roads ---
            // Straight across from the south arm into the northbound... (LHT: northbound lane of the north arm): 22 m, crosses
            // eastbound and westbound through paths (2-10 m) and the opposite side road's right turn (12-15 m, to another exit),
            // merged at 16-22 m by the east arm's left turn into the same lane.
            var across = G22P((2f, 6f, Other), (6f, 10f, Other), (12f, 15f, Other), (16f, 22f, Same));
            Check(G22Admit(22f, none, none, 4.5f, across), "4-way straight across, car: body 16.5-21 m touches only the same-exit merge: may enter");
            Check(!G22Admit(22f, none, none, 8f, across), "4-way straight across, 8 m van: body 13-21 m crosses the opposite side road's right turn (another exit): waits");
            var throughAtEnd = G22P((15f, 22f, Other));
            Check(!G22Admit(22f, none, none, 4.5f, throughAtEnd), "a footprint touching a priority through path to another exit lane: waits before the box");

            // --- deadlock property: whatever its footprint touches needs the same full exit lane ---
            var rnd = new Random(2222);
            int admitted = 0, bad = 0;
            for (int k = 0; k < 5000; k++)
            {
                int n = 1 + rnd.Next(5);
                var p = new (float, float, SameExitException.Path)[n];
                for (int i = 0; i < n; i++)
                {
                    float s = (float)rnd.NextDouble() * 20f;
                    p[i] = (s, s + 1f + (float)rnd.NextDouble() * 6f, rnd.Next(3) == 0 ? Other : Same);
                }
                var paths = G22P(p);
                float len = 4f + (float)rnd.NextDouble() * 15f;
                SameExitException.Footprint(22f, none, none, len, 1f, out float from, out float to);
                if (!SameExitException.Admits(from, to, ref paths)) continue;
                admitted++;
                for (int i = 0; i < n; i++) if (paths.E[i] > from && paths.S[i] < to && paths.K[i] != Same) bad++;
            }
            Check(admitted > 0 && bad == 0, $"5000 random footprints: every admitted one ({admitted}) touches only same-exit paths (nobody blocked who could otherwise move)");
            SameExitException.Footprint(20f, none, none, 6f, 1f, out float f0, out float f1);
            Check(Math.Abs(f1 - 19f) < 1e-4f && Math.Abs(f0 - 12f) < 1e-4f, "footprint: front a margin before the lane end, whole length + margin behind");
        }

        private static void G22RepoTests()
        {
            string root = RepoRoot();
            string box = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "JunctionRules", "Stage2", "JRYellowBox.cs"));
            int hold = box.IndexOf("if (!BoxDriving.Holds(a, room)) return float.MaxValue;\n                // 0.4.11 (G22", StringComparison.Ordinal);
            Check(hold > 0 && box.IndexOf("if (SameExitAdmits(lane, owner.m_Owner, laneLength, inverse, start, self,", hold, StringComparison.Ordinal) > hold,
                "junction box: the exception is tested only after the room test failed at the stop line (the gap rule is untouched)");
            Check(box.Contains("if (SameExitAdmits(lane, owner.m_Owner, length, inverse, math.max(0f, m_Stretch.CurveX), self,"), "also at the UK turn spot's room re-check");
            string se = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "JunctionRules", "Stage2", "JRSameExit.cs"));
            Check(se.Contains("kind = other.m_EndNode.Equals(m_ExitNode) ? SameExitException.Path.SameExit : SameExitException.Path.OtherExit;"), "same exit lane = the other lane's end node equals the car's");
            Check(se.Contains("m_VehicleLength > 0f ? m_VehicleLength : 4f, m_RoomMargin"), "whole length incl. trailers + the room margin");
            string wj = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            Check(wj.Contains("ExitEvent(eventTable, entity, frame, ExitGiveWay.SameExitEntryKind)) Add(counters, Stage2Counters.BoxSameExitEntries, 1);"), "counter: entered a box under the same-exit-lane exception (per vehicle)");
            Check(Stage2Counters.BoxSameExitEntries == Stage2Counters.Count - 4 && Stage2Counters.BoxSameExitStanding == Stage2Counters.Count - 3, "G22 counters at the end");
            Check(Stage2Counters.Format(2, new long[Stage2Counters.Count], 0, 0, 2, 2).Contains("entered a box under the same-exit-lane exception 0"), "timing line names the counter");
        }
    }
}
