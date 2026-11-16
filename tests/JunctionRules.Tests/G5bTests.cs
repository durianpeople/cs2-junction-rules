using System;
using System.Collections.Generic;
using JunctionRules.Core;
using G = JunctionRules.Core.BoxPaintGeometry;
using CW = JunctionRules.Core.CarriagewayOutline;

namespace JunctionRules.Tests
{
    /// <summary>
    /// JR-T-014b (gap G5, still refused in 0.4.7): Crescent Street x Eastgate Lane from the live node (LiveFixturesG5). Cause:
    /// the 0.4.0 "one kerb, one inset" rule gave both halves of each corner kerb the LARGER footway (Crescent W 5 m) as a
    /// constant fraction of each half; 5 m on the Alley's 4 m half clamps to the whole half, so the Alley side's "kerb" was
    /// the Alley's centre line and no box fitted. Fix: when that shape is refused, corners between different footways are
    /// drawn as the game draws them (KerbRemap: the game's LerpRemap with the node sync offsets / targets).
    /// </summary>
    internal static partial class Program
    {
        private static V3[] G5bCtrl(double[] a) => Pts(a).ToArray();

        private static List<V3> G5bSample(double[] ctrl)
        {
            var c = Pts(ctrl);
            var r = new List<V3>();
            for (int i = 0; i <= 16; i++)
            {
                double t = i / 16.0, u = 1 - t;
                double w0 = u * u * u, w1 = 3 * u * u * t, w2 = 3 * u * t * t, w3 = t * t * t;
                r.Add(new V3(c[0].X * w0 + c[1].X * w1 + c[2].X * w2 + c[3].X * w3, c[0].Y * w0 + c[1].Y * w1 + c[2].Y * w2 + c[3].Y * w3, c[0].Z * w0 + c[1].Z * w1 + c[2].Z * w2 + c[3].Z * w3));
            }
            return r;
        }

        /// <summary>The live node's arms as BoxShapes.Junction builds them; withRemap = with the game's corner kerb data (0.4.8), else as 0.4.7 read it.</summary>
        private static List<ArmInput> G5bInputs(bool withRemap, out List<CrosswalkStrip> zebras)
        {
            var arms = new List<ArmInput>();
            zebras = new List<CrosswalkStrip>();
            var centre = new V2(LiveFixturesG5.CrescentCentre[0], LiveFixturesG5.CrescentCentre[1]);
            foreach (var a in LiveFixturesG5.CrescentArms)
            {
                var x = new ArmInput { OuterA = G5bSample(a.OuterA), InnerA = G5bSample(a.InnerA), OuterB = G5bSample(a.OuterB), InnerB = G5bSample(a.InnerB), InsetA = a.InsetA, InsetB = a.InsetB, Yielding = a.Yielding };
                x.HalfA = V2.Distance(x.OuterA[0].XZ, x.InnerA[0].XZ);
                x.HalfB = V2.Distance(x.OuterB[0].XZ, x.InnerB[0].XZ);
                if (withRemap)
                {
                    x.RemapA = new KerbRemap { Outer = G5bCtrl(a.OuterA), Inner = G5bCtrl(a.InnerA), Offsets = a.OffsetsLeft, Targets = a.TargetsLeft, OuterIsLeft = true, Half = a.CompHalf };
                    x.RemapB = new KerbRemap { Outer = G5bCtrl(a.OuterB), Inner = G5bCtrl(a.InnerB), Offsets = a.OffsetsRight, Targets = a.TargetsRight, OuterIsLeft = false, Half = a.CompHalf };
                }
                arms.Add(x);
                if (a.Zebra != null)
                {
                    V2 p = x.OuterA[0].XZ, q = x.OuterB[0].XZ, mid = (p + q) * 0.5, across = (q - p).Normalized;
                    var n = new V2(-across.Z, across.X);
                    if (V2.Dot(n, centre - mid) < 0) n = n * -1;
                    zebras.Add(new CrosswalkStrip(mid - across * a.Zebra[1] + n * a.Zebra[0], mid + across * a.Zebra[1] + n * a.Zebra[0], 2.0));
                }
            }
            return arms;
        }

        private static void G5bTests()
        {
            var centre = new V2(LiveFixturesG5.CrescentCentre[0], LiveFixturesG5.CrescentCentre[1]);
            var cps = LiveConflicts(LiveFixturesG5.CrescentPaths);
            const string tag = "Crescent x Eastgate (live 17 Nov)";

            // 1. 0.4.7 reproduced from the live numbers: same refusal, same merged-kerb notes, same box starts
            {
                var arms = G5bInputs(false, out var zebras);
                var old = CW.BuildStraight(arms, centre, zebras, 0.5, cps, false);
                string why047 = old.Notes.Find(s => s.Contains("the 0.4.7 shape was refused ("));
                Check(why047 != null && why047.Contains("corner arm 3 - arm 1 still crosses the kerb or a marking margin: no allowed shift clears it and no straight cut fits (a cut clear of the kerb would take more than half of a mouth side)"), $"{tag}: 0.4.7 refusal reproduced ({why047})");
                Check(old.Notes.Exists(s => s.Contains("kerb between arm 1A and arm 3B: one kerb, inset 5.00 m (halves were 5.00|1.00)")), $"{tag}: 0.4.7 merged-kerb note reproduced");
                Check(old.StopDistance != null && Math.Abs(old.StopDistance[1] - 2.7) < 0.06 && Math.Abs(old.StopDistance[2] - 2.8) < 0.06, $"{tag}: logged box starts +2.7 / +2.8 m reproduced ({(old.StopDistance == null ? "-" : string.Join(", ", Array.ConvertAll(old.StopDistance, d => d.ToString("0.00"))))})");
                Check(old.Notes.Exists(s => s.Contains("arm 1: marking (+ margin) ends 2.8 m in, past the kerb lines")), $"{tag}: arm 1 zebra past its kerb lines, as logged");
                // the cause: the Alley side of each merged corner is pulled to the Alley's middle (5 m inset on a 4 m half)
                var k = CW.KerbLine(arms[2].OuterB, arms[2].InnerB, arms[2].HalfB, 5.0, arms[2].OuterA[0].XZ);
                double off = 0;
                for (int i = 0; i < k.Count; i++) off = Math.Max(off, V2.Distance(k[i].XZ, arms[2].InnerB[i].XZ));
                Check(arms[2].HalfB < 4.1 && off < 1e-9, $"{tag}: cause: the 5 m 'one kerb' inset on the Alley's {arms[2].HalfB:0.00} m half IS the Alley's centre line");
            }

            // 2. the game's corner kerbs match what the game draws (bridge kerb polylines, live): mouth points and the shared end point
            {
                var arms = G5bInputs(true, out _);
                var w = LiveFixturesG5.CrescentArms;
                List<V3> wB = arms[0].RemapB.KerbLine(5, arms[0].HalfB), sA = arms[1].RemapA.KerbLine(1, arms[1].HalfA);
                List<V3> wA = arms[0].RemapA.KerbLine(5, arms[0].HalfA), nB = arms[2].RemapB.KerbLine(1, arms[2].HalfB);
                bool Near(V3 p, double x, double z, double tol) => V2.Distance(p.XZ, new V2(x, z)) <= tol;
                Check(Near(wB[0], -521.937, 542.966, 0.03) && Near(wB[16], -520.107, 541.145, 0.03), $"{tag}: Crescent W south kerb = the game's (mouth {wB[0]}, end {wB[16]})");
                Check(Near(sA[0], -519.789, 537.493, 0.03) && Near(sA[16], -520.107, 541.145, 0.03), $"{tag}: Eastgate S west kerb = the game's (mouth {sA[0]}, end {sA[16]})");
                Check(Near(wA[0], -522.207, 548.963, 0.03) && Near(wA[16], -520.534, 550.746, 0.03), $"{tag}: Crescent W north kerb = the game's (mouth {wA[0]}, end {wA[16]})");
                Check(Near(nB[0], -520.918, 554.506, 0.03) && Near(nB[16], -520.534, 550.746, 0.03), $"{tag}: Eastgate N west kerb = the game's (mouth {nB[0]}, end {nB[16]})");
                Check(V2.Distance(wB[16].XZ, sA[16].XZ) < 0.01 && V2.Distance(wA[16].XZ, nB[16].XZ) < 0.01, $"{tag}: both halves of each corner kerb meet");
                // constant fraction (0.4.7 per-half insets) would put the Alley half's end 1.6 m from the outer corner: on the pavement
                Check(KerbRemap.Remap(0.625, w[0].OffsetsLeft, w[0].TargetsLeft) == w[0].TargetsLeft[2], $"{tag}: Remap at a sync offset = its target (game Remap)");
                Check(CW.IsCorner(arms[0], arms[1], wB[16].XZ) && CW.IsCorner(arms[0], arms[2], wA[16].XZ), $"{tag}: Crescent W - Eastgate pairs are corners");
                Check(!CW.IsCorner(arms[0], arms[3], centre), $"{tag}: Crescent W - Crescent E face opposite ways (a through side keeps one kerb, one inset)");
            }

            // 3. 0.4.8: a box, straight-sided, on the asphalt, clear of the zebras, every conflict point inside, shape named
            {
                var arms = G5bInputs(true, out var zebras);
                var res = CW.BuildStraight(arms, centre, zebras, 0.5, cps, false);
                Check(res.Outline != null, $"{tag}: 0.4.8 builds a box ({res.Failure})");
                if (res.Outline != null)
                {
                    Check(res.GameCorners == 4, $"{tag}: the four corners between different footways drawn the game's way ({res.GameCorners})");
                    Check(res.Notes.Exists(s => s.StartsWith("built the 0.4.8 way (4 corner kerb(s) as the game draws them, 1 mouth(s) held clear of a marking past the kerb lines); the 0.4.7 shape was refused (")), $"{tag}: notes say why the game corners were used");
                    Check(res.Notes.Exists(s => s.Contains("kerb between arm 1A and arm 3B: corner kerb as the game draws it")), $"{tag}: each game corner named");
                    string sum = res.ShapeSummary();
                    Check(sum != null && sum.StartsWith($"box shape: {res.Outline.Count}-sided straight box") && sum.Contains("4 corner kerb(s) as the game draws them") && sum.Contains("rule " + res.Straight.RuleCode), $"{tag}: shape summary for the log ({sum})");
                    // reference carriageway: the game's kerbs (all pairs the game's way), mouths at the slice ends
                    var refArms = G5bInputs(true, out _);
                    var kerbs = new List<ArmSlice>();
                    for (int i = 0; i < refArms.Count; i++) kerbs.Add(new ArmSlice(refArms[i].RemapA.KerbLine(refArms[i].InsetA, refArms[i].HalfA), refArms[i].RemapB.KerbLine(refArms[i].InsetB, refArms[i].HalfB)));
                    var reference = G.ToXZ(G.BuildOutline(kerbs, centre, 0, out string why));
                    Check(reference.Count > 0, $"{tag}: reference carriageway builds ({why})");
                    double worst = WorstOutside(res.Outline, reference);
                    Check(worst <= StraightBox.KerbTolerance + 1e-6, $"{tag}: box on the asphalt (worst {worst:0.000} m over the game's kerb)");
                    var xz = G.ToXZ(res.Outline);
                    // three live conflict points lie 2.5-2.6 m in from the Crescent W mouth, under its zebra's 0.5 m margin
                    // (painted to 2.3 m, held to 2.8 m): no box clear of the zebra can hold them; the solver says so and
                    // every other point is inside
                    int inside = 0, underZebra = 0;
                    foreach (var c in cps)
                    {
                        if (G.Contains(xz, c, 1e-3)) inside++;
                        else if (c.X < xz[0].X + 0.5 && c.X < -519.0) underZebra++;
                    }
                    bool said = res.Notes.Exists(s => s == "3 conflict point(s) behind a mouth line, not held inside");
                    Check(cps.Count == 7 && inside == 4 && underZebra == 3 && said, $"{tag}: every conflict point inside except the 3 under the Crescent W zebra margin (inside {inside}, under zebra {underZebra}, noted {said})");
                    Check(G.IsSimple(xz), $"{tag}: ring simple");
                    double gap = G5MarkingGap(res.Outline, zebras);
                    Check(gap >= 0.5 - 1e-3, $"{tag}: every side keeps 0.5 m from the zebras ({gap:0.000} m)");
                    Check(res.Outline.Count <= 10, $"{tag}: straight-sided, few vertices ({res.Outline.Count})");
                    // yielding (Eastgate) mouths only slide forward; never past the centre
                    for (int i = 0; i < res.Straight.Shift.Length; i++) Check(res.Straight.Shift[i] >= 0, $"{tag}: arm {i + 1} shift >= 0");
                    Console.WriteLine($"    {tag}: {sum}");
                    Console.WriteLine("      outline " + string.Join(" ", res.Outline.ConvertAll(v => $"({v.X:0.00},{v.Z:0.00})")));
                }
            }

            // 4. never a different shape where 0.4.7 had one: the same live node without conflict points / zebras built in
            //    0.4.7; with the game data present it is the identical ring (the game corners are only a fallback)
            {
                var plain = CW.BuildStraight(G5bInputs(false, out _), centre, null, 0.5, null, false);
                var withData = CW.BuildStraight(G5bInputs(true, out _), centre, null, 0.5, null, false);
                Check(plain.Outline != null && SameRing(plain.Outline, withData.Outline) && withData.GameCorners == 0, $"{tag}, no zebras / conflict points: 0.4.7 box identical with the game data present ({plain.Failure})");
                // ShareJoinedInsets without gameCorners: as 0.4.7 (larger inset everywhere)
                CW.ShareJoinedInsets(G5bInputs(true, out _), CW.JoinTolerance, out double[] ia, out double[] ib);
                Check(ia[0] == 5 && ib[0] == 5 && ia[1] == 5 && ib[1] == 3 && ia[2] == 3 && ib[2] == 5 && ia[3] == 3 && ib[3] == 3, $"{tag}: one kerb, one inset unchanged by default ({string.Join(",", ia)} | {string.Join(",", ib)})");
                // a through side with different footways keeps 0.4.0's one inset even with game data (Moonflower x Anchor rule)
                var t = Synth(new double[] { 0, 90, 180 }, 8, 20, 6, 3, new[] { false, true, false });
                foreach (var a in t)
                {
                    a.RemapA = new KerbRemap { Outer = new[] { a.OuterA[0], a.OuterA[0], a.OuterA[0], a.OuterA[0] }, Inner = new[] { a.OuterA[0], a.OuterA[0], a.OuterA[0], a.OuterA[0] }, Offsets = new double[] { 0, 0.3, 0.4, 1 }, Targets = new double[] { 0, 0.3, 0.4, 1 }, OuterIsLeft = true };
                    a.RemapB = a.RemapA;
                }
                t[0].InsetB = 2; t[0].InsetA = 2;
                CW.ShareJoinedInsets(t, CW.JoinTolerance, true, out ia, out ib, out bool[] ra, out bool[] rb, out int gp);
                int throughPairs = 0;
                for (int i = 0; i < t.Count; i++) if (!ra[i] && !rb[i] && (ia[i] == 3 && ib[i] == 3)) throughPairs++;
                Check(Math.Max(ia[0], ib[0]) == 3 && Math.Min(ia[0], ib[0]) == 2 && gp == 1, $"{tag}: synthetic T with a 2 m footway on one through half: the through side keeps one inset (3 m), the corner goes the game's way (pairs {gp}; arm 1 insets {ia[0]}|{ib[0]})");
            }
        }
    }
}
