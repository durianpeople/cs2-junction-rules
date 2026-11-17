using System;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.12 OWNER DECISION (17 Nov, G22 / G28 / G29): "for this exception, it is allowed to block non-zebra and pavement
    /// pedestrian paths". The same-exit-lane exception's waiting footprint may lie over unmarked crossings, pedestrian-street
    /// (tiled) crossings and pavement / footway paths (a drive's sidewalk band); never over a zebra (and the no-stop-on-zebra rule
    /// stays); vehicle paths into other lanes still disqualify. Case: the BOTTOM police car at 621408 (Lilac Street arm, near-side
    /// left turn into 953631's east lane), crossing 0-2.2 m in. Zebra = the game's marked crosswalk, as the no-stop rule (review:
    /// one definition for both rules; the tiled pedestrian-street crossing question is open with the owner, PROBE 22.9).
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
            Check(SameExitException.BlocksAsZebra(cw), "owner: a marked crosswalk is a zebra (never blocked)");
            Check(!SameExitException.BlocksAsZebra(cw | uns), "owner: an unmarked crossing (Unsafe) may be blocked");
            Check(!SameExitException.BlocksAsZebra(0), "owner: a pavement / footway path (no Crosswalk flag, e.g. a drive's sidewalk band) may be blocked");
            for (int f = 0; f < 8; f++) Check(SameExitException.BlocksAsZebra(f) == ZebraSections.IsZebra(f), $"review: the exception and the no-stop-on-zebra rule agree on what a zebra is (flags {f})");
            Check(SameExitException.PedestrianKind(cw) == SameExitException.Path.Other && SameExitException.PedestrianKind(cw | uns) == SameExitException.Path.Pavement,
                "kinds: zebra = Other (refuses), non-zebra = Pavement (allowed)");
            // the bottom police car, a short turn that puts its tail over the crossing (0..2.2 m): turn 8.0 m, car 4.7 m, margin 1
            SameExitException.Footprint(8f, float.MaxValue, float.MaxValue, 4.7f, 1f, out float from, out float to);
            Check(from < 2.2f && SameExitException.WithinLane(from), $"bottom police car (short turn): tail over the crossing (footprint {from:0.0}..{to:0.0} m)");
            var unmarked = new PedPaths { S = new[] { 0f, 3f }, E = new[] { 2.2f, 7f }, K = new[] { SameExitException.PedestrianKind(cw | uns), SameExitException.Path.SameExit } };
            Check(SameExitException.Admits(from, to, ref unmarked), "owner case: tail over the UNMARKED south-arm crossing -> enters");
            var zebra = new PedPaths { S = new[] { 0f, 3f }, E = new[] { 2.2f, 7f }, K = new[] { SameExitException.PedestrianKind(cw), SameExitException.Path.SameExit } };
            Check(!SameExitException.Admits(from, to, ref zebra), "owner case with a ZEBRA there -> waits");
            var other = new PedPaths { S = new[] { 0f, 4f }, E = new[] { 2.2f, 6f }, K = new[] { SameExitException.PedestrianKind(cw | uns), SameExitException.Path.OtherExit } };
            Check(!SameExitException.Admits(from, to, ref other), "a vehicle path into another lane under the footprint still disqualifies");
            // G28: a drive exit's sidewalk band is pavement: only a zebra limits the leaving lane footprint (walkEnd -1 = none)
            Check(ExitSameLane.Footprint(19.0f, 47f, 25.1f, 6.5f, 1f, 10.1f, -1f, out _, out _, out float wf) && wf < 8.35f,
                $"G28: a 6.5 m van's tail may now stand on the drive's sidewalk band (from {wf:0.0} m, pavement ends 8.35 m)");
            Check(!ExitSameLane.Footprint(19.0f, 47f, 25.1f, 6.5f, 1f, 10.1f, 8.35f, out _, out _, out _), "G28: the same band as a ZEBRA still refuses");
            string root = RepoRoot();
            string se = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRSameExit.cs"));
            string ex = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRExitGiveWay.cs"));
            Check(se.Contains("private static bool PedestrianBlocksAsZebra(Entity ped, int pedFlags, ref ComponentLookup<Curve> curves) => SameExitException.BlocksAsZebra(pedFlags);")
                  && se.Contains("pavement |= 1UL << i; continue;"), "repo: G22 footprint: non-zebra pedestrian overlaps allowed (mask), zebras limit the front and refuse");
            Check(ex.Contains("if (PedestrianBlocksAsZebra(o.m_Other, (int)wp.m_Flags, ref curves) && e * wayOutLength > walkEnd) walkEnd = e * wayOutLength;"), "repo: G28 only a zebra limits the leaving lane footprint");
        }
    }
}
