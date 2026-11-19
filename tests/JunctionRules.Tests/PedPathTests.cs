using System;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.12 OWNER DECISION REVISED (17 Nov, supersedes the zebra limit): "it can block ANY pedestrian paths for the yielding exit at
    /// the box". Under the same-exit-lane exception (G22 junction boxes, G28 building drive exits) the waiting footprint may lie over any
    /// pedestrian path, zebras included; only vehicle paths into other lanes disqualify; "never stop on a zebra" does not hold an admitted
    /// car. Kept: cars never drive into people; people pass a blocking car after 2 s. Case: the bottom police car at 621408 (Lilac
    /// Street, near-side left turn into 953631's east lane), crossing 0-2.2 m in.
    /// </summary>
    internal static partial class Program
    {
        private struct PedPaths : IFootprintPaths
        {
            public float[] S, E; public SameExitException.Path[] K;
            public int Count => S.Length;
            public bool Get(int i, out float s, out float e, out SameExitException.Path k) { s = S[i]; e = E[i]; k = K[i]; return true; }
        }

        private static void PedPathTests()
        {
            const int cw = SameExitException.PedCrosswalk, uns = SameExitException.PedUnsafe;
            for (int f = 0; f < 8; f++) Check(SameExitException.PedestrianKind(f) == SameExitException.Path.Pavement, $"owner revised: every pedestrian path may be under the footprint (flags {f})");
            Check(SameExitException.BlocksAsZebra(cw) && !SameExitException.BlocksAsZebra(cw | uns) && !SameExitException.BlocksAsZebra(0), "zebra test (counter only) = the game's marked crosswalk");
            // the bottom police car, a short turn that puts its tail over the crossing (0..2.2 m): turn 8.0 m, car 4.7 m, margin 1
            SameExitException.Footprint(8f, float.MaxValue, float.MaxValue, 4.7f, 1f, out float from, out float to);
            Check(from < 2.2f && SameExitException.WithinLane(from), $"bottom police car (short turn): tail over the crossing (footprint {from:0.0}..{to:0.0} m)");
            var unmarked = new PedPaths { S = new[] { 0f, 3f }, E = new[] { 2.2f, 7f }, K = new[] { SameExitException.PedestrianKind(cw | uns), SameExitException.Path.SameExit } };
            Check(SameExitException.Admits(from, to, ref unmarked), "owner test: tail over an UNMARKED crossing -> enters");
            var zebra = new PedPaths { S = new[] { 0f, 3f }, E = new[] { 2.2f, 7f }, K = new[] { SameExitException.PedestrianKind(cw), SameExitException.Path.SameExit } };
            Check(SameExitException.Admits(from, to, ref zebra), "owner test: tail over a ZEBRA -> enters");
            var other = new PedPaths { S = new[] { 0f, 4f }, E = new[] { 2.2f, 6f }, K = new[] { SameExitException.PedestrianKind(cw), SameExitException.Path.OtherExit } };
            Check(!SameExitException.Admits(from, to, ref other), "owner test: footprint over another lane's path -> waits");
            var track = new PedPaths { S = new[] { 4f }, E = new[] { 6f }, K = new[] { SameExitException.Path.Other } };
            Check(!SameExitException.Admits(from, to, ref track), "a track / unknown path under it still refuses");
            // G28: the drive's sidewalk band (or a zebra there) never limits the leaving-lane footprint now (walkEnd -1)
            Check(ExitSameLane.Footprint(19.0f, 47f, 25.1f, 6.5f, 1f, 10.1f, -1f, out _, out _, out float wf) && wf < 8.35f,
                $"G28: a 6.5 m van's tail may stand on the drive's sidewalk / crossing (from {wf:0.0} m, band ends 8.35 m)");
            string root = RepoRoot();
            string se = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRSameExit.cs"));
            string ex = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRExitGiveWay.cs"));
            string wj = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            Check(se.Contains("if (m_Pedestrian.HasComponent(o.m_Other)) { kind = SameExitException.Path.Pavement; return true; }"), "repo: G22 footprint: every pedestrian overlap allowed");
            Check(!ex.Contains("walkEnd = e * wayOutLength"), "repo: G28: no pedestrian path limits the leaving-lane footprint");
            Check(wj.Contains("if (m_BoxRule && SameExitZebraExempt(lane, overlaps, curveX, length, inverse, self,") && wj.IndexOf("SameExitZebraExempt(lane") > wj.IndexOf("if (HasRoomPast(lane, layout.SectionEnd(k)"),
                "repo: the zebra room hold is skipped only for a car the exception admits (after the room test, so cars with room are unaffected)");
            Check(wj.Contains("if (m_SameExitZebraTail) Add(counters, Stage2Counters.BoxSameExitZebraTail, 1);") && Stage2Counters.BoxSameExitZebraTail == Stage2Counters.Count - 4,
                "counter: admitted under the exception with a tail on a zebra (per entry episode), the last before the JR-T-022 look-past counter");
        }
    }
}
