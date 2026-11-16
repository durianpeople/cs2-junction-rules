using System;
using System.Collections.Generic;
using JunctionRules.Core;
using G = JunctionRules.Core.BoxPaintGeometry;
using CW = JunctionRules.Core.CarriagewayOutline;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.0-probe (JR-T-007): T through-side inset (one kerb, one inset), stop-bay footway, selection rules, the saved
    /// box record (round trip + read by a reader without the mod), and the entrance box shape.
    /// </summary>
    internal static partial class Program
    {
        // ---- T junction through side

        private static void BoxThroughSideTests()
        {
            const double Hh = 7, w = 3, h = Hh - w, r = 16, R = 3;
            // T: through road 0/180, stem 90. Arm 0 (east) OuterB and arm 2 (west) OuterA are the two halves of the
            // straight south kerb, meeting end to end at the foot point (0, -7) with no arm between them.
            var t = GameArms(new[] { 0.0, 90, 180 }, Hh, r, R, out _);
            SetInsets(t, w);
            Check(V2.Distance(t[0].OuterB[t[0].OuterB.Count - 1].XZ, t[2].OuterA[t[2].OuterA.Count - 1].XZ) < 1e-9, "T: the two halves of the through kerb meet end to end (test shape)");
            // the defect: the east half's own edge says 2 m (bus stop bay on that edge), the west half 3 m
            t[0].InsetB = 2;
            var res = CW.Build(t, new V2(0, 0), null, 0.05);
            Check(res.Outline != null, "T through side: outline merges (" + res.Failure + ")");
            if (res.Outline != null)
            {
                var xz = G.ToXZ(res.Outline);
                double minZ = double.MaxValue; int onKerb = 0;
                foreach (var p in xz) { minZ = Math.Min(minZ, p.Z); if (Math.Abs(p.Z + h) < 1e-6) onKerb++; }
                Check(Math.Abs(minZ + h) < 1e-6, $"T through side: the whole through kerb is inset by the pavement width (min z {minZ:0.###}, kerb {-h})");
                Check(onKerb == 2, $"T through side: one straight edge mouth to mouth, no step at the middle ({onKerb} points on the kerb)");
                Check(!G.Contains(xz, new V2(r - 0.5, -h - 0.5)) && !G.Contains(xz, new V2(-r + 0.5, -h - 0.5)), "T through side: pavement clean on both halves");
                Check(res.Notes.Exists(s => s.Contains("one kerb") && s.Contains("3.00")), "T through side: the shared inset is logged");
            }
            // ShareJoinedInsets alone: the pair takes the larger; the corners' pairs (equal) unchanged; no arm pairs with itself
            CW.ShareJoinedInsets(t, CW.JoinTolerance, out double[] ia, out double[] ib);
            Check(ia[2] == 3 && ib[0] == 3 && ia[0] == 3 && ib[1] == 3, "shared inset: larger of the two halves, other kerbs untouched");
            var dead = new List<ArmInput> { new ArmInput { OuterA = { new V3(0, 0, 1), new V3(5, 0, 0) }, OuterB = { new V3(0, 0, -1), new V3(5, 0, 0) }, InsetA = 1, InsetB = 2 } };
            CW.ShareJoinedInsets(dead, CW.JoinTolerance, out ia, out ib);
            Check(ia[0] == 1 && ib[0] == 2, "shared inset: an arm's own two curves never pair (dead-end cap)");
            // a corner (curves meeting at the fillet middle) gets the same treatment
            var x = GameArms(new[] { 0.0, 90, 180, 270 }, Hh, r, R, out _);
            SetInsets(x, w);
            x[0].InsetA = 1.5;
            CW.ShareJoinedInsets(x, CW.JoinTolerance, out ia, out ib);
            Check(ia[0] == 3 && ib[1] == 3, "shared inset: a corner's two halves take the larger too");

            // live geometry, Moonflower x Anchor (node at (-437.94, 24.88), 0.3.5 log: footways 3.00|2.00, 3.00|3.00,
            // 3.00|3.00): node slice outer curves (bezier a, b, c, d) as the game gave them.
            var live = new List<ArmInput>
            {
                LiveArm(new[] { -447.83, 597.6, 32.54, -447.66, 597.61, 32.55, -447.49, 598, 32.63, -447.39, 598.17, 32.77 },
                        new[] { -445.58, 597.67, 16.61, -442.96, 597.78, 16.71, -440.34, 597.83, 16.8, -437.72, 597.88, 16.88 }, 3, 2),
                LiveArm(new[] { -429.86, 598.11, 17.13, -432.48, 598, 17.05, -435.1, 597.94, 16.97, -437.72, 597.88, 16.88 },
                        new[] { -430.44, 598.11, 33.12, -430.62, 598.1, 33.11, -430.78, 598.21, 33.19, -430.9, 598.35, 33.31 }, 3, 3),
                LiveArm(new[] { -431.17, 598.71, 33.88, -431.14, 598.68, 33.66, -431.06, 598.51, 33.46, -430.9, 598.35, 33.31 },
                        new[] { -447.26, 598.88, 33.34, -447.23, 598.86, 33.14, -447.27, 598.38, 32.93, -447.39, 598.17, 32.77 }, 3, 3),
            };
            var centre = new V2(-437.944, 24.875);
            var resLive = CW.Build(live, centre, null, 0.05);
            foreach (var a in live) { a.InsetA = 3; a.InsetB = 3; }
            var resAll3 = CW.Build(live, centre, null, 0.05);
            Check(resLive.Outline != null && resAll3.Outline != null, "Moonflower x Anchor: outline merges");
            if (resLive.Outline != null && resAll3.Outline != null)
            {
                double a1 = G.SignedArea(resLive.Outline), a3 = G.SignedArea(resAll3.Outline);
                Check(Math.Abs(a1 - a3) < 1e-6, $"Moonflower x Anchor: 3|2 on the west arm now paints the same box as 3 m all round ({a1:0.0} vs {a3:0.0} m2; 0.3.5 painted 216.9)");
                double minD = double.MaxValue;
                foreach (var p in resLive.Outline)
                    foreach (var line in new[] { live[0].OuterB, live[1].OuterA })
                        for (int i = 0; i + 1 < line.Count; i++) minD = Math.Min(minD, G.DistanceToSegment(p.XZ, line[i].XZ, line[i + 1].XZ));
                Check(minD > 2.95, $"Moonflower x Anchor: the through (south) side stays 3 m off the back of the pavement on both halves (min {minD:0.00} m)");
            }

            // stop bay footway (heuristic): a side with a stop takes the larger of its own and the other side's footway
            Check(CW.StopBayFootway(2, 3, true) == 3 && CW.StopBayFootway(2, 3, false) == 2 && CW.StopBayFootway(4, 3, true) == 4, "stop bay: side with a stop never narrower than the other side; no stop unchanged");
        }

        private static ArmInput LiveArm(double[] a, double[] b, double ia, double ib)
        {
            return new ArmInput { OuterA = Bez(a), OuterB = Bez(b), InsetA = ia, InsetB = ib };
        }

        private static List<V3> Bez(double[] c)
        {
            var r = new List<V3>();
            for (int i = 0; i <= 16; i++)
            {
                double t = i / 16.0, u = 1 - t;
                var p = new double[3];
                for (int k = 0; k < 3; k++) p[k] = u * u * u * c[k] + 3 * u * u * t * c[3 + k] + 3 * u * t * t * c[6 + k] + t * t * t * c[9 + k];
                r.Add(new V3(p[0], p[1], p[2]));
            }
            return r;
        }

        // ---- selection rules

        private static JunctionFacts GoodJunction() => new JunctionFacts { Exists = true, IsRoadNode = true, RoadArms = 3 };

        private static void BoxSelectionTests()
        {
            Check(BoxSelection.JunctionOk(GoodJunction(), out string why) && why == null, "junction: at-grade 3-arm road node is valid");
            var f = GoodJunction(); f.RoadArms = 4; f.PathArms = 1;
            Check(BoxSelection.JunctionOk(f, out _), "junction: 4 road arms plus a path is valid");
            f = GoodJunction(); f.RoadArms = 2;
            Check(!BoxSelection.JunctionOk(f, out why) && why.Contains("mid-road"), "junction: 2 road arms (mid-road node) excluded");
            f.PathArms = 1;
            Check(!BoxSelection.JunctionOk(f, out why) && why.Contains("path joining"), "junction: path joining a road excluded");
            f = GoodJunction(); f.RoadArms = 1;
            Check(!BoxSelection.JunctionOk(f, out why) && why == "dead end", "junction: dead end excluded");
            f = GoodJunction(); f.IsRoadNode = false;
            Check(!BoxSelection.JunctionOk(f, out why) && why.Contains("not a road node"), "junction: pure path node excluded");
            f = GoodJunction(); f.AnyHighwayArm = true;
            Check(!BoxSelection.JunctionOk(f, out why) && why == "highway", "junction: highway excluded");
            f = GoodJunction(); f.AnyElevatedOrTunnelArm = true;
            Check(!BoxSelection.JunctionOk(f, out why) && why.Contains("ground level"), "junction: bridge / elevated / tunnel arm excluded");
            f = GoodJunction(); f.NodeOffGround = true;
            Check(!BoxSelection.JunctionOk(f, out why) && why.Contains("ground level"), "junction: node off the ground excluded");
            f = GoodJunction(); f.Roundabout = true;
            Check(!BoxSelection.JunctionOk(f, out why) && why.Contains("roundabout"), "junction: roundabout excluded");
            f = GoodJunction(); f.LevelCrossing = true;
            Check(!BoxSelection.JunctionOk(f, out why) && why.Contains("level crossing"), "junction: level crossing excluded");
            f = GoodJunction(); f.Owned = true;
            Check(!BoxSelection.JunctionOk(f, out why) && why.Contains("own network"), "junction: node of a building's own network excluded");
            f = GoodJunction(); f.Exists = false;
            Check(!BoxSelection.JunctionOk(f, out why) && why == "gone", "junction: deleted / temp node excluded");

            EntranceFacts Good() => new EntranceFacts { Exists = true, IsBuilding = true, HasRoadEdge = true, RoadIsRoad = true, AccessInsideEdge = true };
            Check(BoxSelection.EntranceOk(Good(), out why) && why == null, "entrance: building with an at-grade road access is valid");
            var e = Good(); e.Owned = true;
            Check(!BoxSelection.EntranceOk(e, out why) && why.Contains("main building"), "entrance: extension / sub-building excluded");
            e = Good(); e.HasRoadEdge = false;
            Check(!BoxSelection.EntranceOk(e, out why) && why == "no road access", "entrance: no road access excluded");
            e = Good(); e.RoadIsRoad = false;
            Check(!BoxSelection.EntranceOk(e, out _), "entrance: access on a path excluded");
            e = Good(); e.RoadHighway = true;
            Check(!BoxSelection.EntranceOk(e, out why) && why.Contains("highway"), "entrance: highway access excluded");
            e = Good(); e.RoadElevatedOrTunnel = true;
            Check(!BoxSelection.EntranceOk(e, out _), "entrance: elevated / tunnel access excluded");
            e = Good(); e.OnRoad = true;
            Check(!BoxSelection.EntranceOk(e, out why) && why.Contains("on the road"), "entrance: building standing on the road excluded");
            e = Good(); e.AccessInsideEdge = false;
            Check(!BoxSelection.EntranceOk(e, out why) && why.Contains("junction"), "entrance: access inside a junction's node area excluded");

            Check(BoxSelection.Toggle(false, true) == ToggleAction.Add, "toggle: valid target without a box adds one");
            Check(BoxSelection.Toggle(true, true) == ToggleAction.Remove && BoxSelection.Toggle(true, false) == ToggleAction.Remove, "toggle: a box is always removable, valid or not");
            Check(BoxSelection.Toggle(false, false) == ToggleAction.None, "toggle: invalid target without a box does nothing");
        }

        // ---- saved record

        private static void BoxRecordTests()
        {
            var rec = new BoxRecord(BoxRecordFlags.PaintOnly);
            byte[] bytes = rec.Encode();
            Check(bytes.Length == 3 && bytes[0] == BoxRecord.CurrentVersion && bytes[1] == BoxRecord.V1PayloadLength, "record: version 1 layout [version, length, flags]");
            int p = 0;
            Check(BoxRecord.Decode(() => bytes[p++], out BoxRecord back, out byte v) && back.Equals(rec) && v == 1 && p == bytes.Length, "record: round trip, every byte consumed");
            var none = new BoxRecord(BoxRecordFlags.None).Encode();
            Check(none.Length > 0, "record: never zero bytes (the game rejects an empty write of a non-empty serializable)");
            // a newer writer with a longer payload: this reader keeps what it knows and consumes exactly the record
            byte[] newer = { 2, 4, 1, 9, 9, 9, 0xAB };
            p = 0;
            Check(BoxRecord.Decode(() => newer[p++], out back, out v) && v == 2 && back.Flags == BoxRecordFlags.PaintOnly && p == 6, "record: newer version with extra payload is read and its extra bytes skipped");
            byte[] zero = { 0, 0 };
            p = 0;
            Check(!BoxRecord.Decode(() => zero[p++], out _, out _) && p == 2, "record: version 0 refused but consumed");

            // save with three boxed entities, then another component's block
            var stream = new List<byte>();
            var recs = new List<BoxRecord> { new BoxRecord(BoxRecordFlags.None), new BoxRecord(BoxRecordFlags.PaintOnly), new BoxRecord(BoxRecordFlags.None) };
            BlockModel.WriteBlock(stream, recs);
            int nextStart = stream.Count;
            stream.AddRange(BitConverter.GetBytes(4));
            stream.AddRange(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
            byte[] save = stream.ToArray();
            // with the mod: exact read
            var read = new List<BoxRecord>();
            int after = BlockModel.ReadBlock(save, 0, 3, read);
            Check(after == nextStart && read.Count == 3 && read[1].Flags == BoxRecordFlags.PaintOnly, "save: with the mod, the block reads back exactly (no size mismatch)");
            // without the mod: the obsolete serializer skips the block, the next component reads from the right place
            int skipped = BlockModel.SkipBlock(save, 0);
            Check(skipped == nextStart && BitConverter.ToInt32(save, skipped) == 4 && save[skipped + 4] == 0xDE, "save: without the mod, the block is skipped by its size and the next component reads intact");
        }

        // ---- entrance box

        private static void EntranceBoxTests()
        {
            Check(EntranceBox.Window(50, 8, 10, 90, out double a, out double b) && a == 46 && b == 54, "entrance window: centred on the access");
            Check(EntranceBox.Window(12, 8, 10, 90, out a, out b) && a == 10 && b == 16, "entrance window: clipped at the junction end");
            Check(!EntranceBox.Window(10.2, 8, 10, 10.5, out _, out _), "entrance window: under 1 m left = no box");
            // straight 16 m road along +x, 3 m footways: kerbs at z = +-5, box 8 m long
            var c = new List<V3>();
            for (int i = 0; i <= 8; i++) c.Add(new V3(46 + i, 0, 0));
            var ring = EntranceBox.Build(c, 8, 3, 3, out string why);
            Check(ring != null, "entrance: builds (" + why + ")");
            if (ring != null)
            {
                var xz = G.ToXZ(ring);
                double minZ = double.MaxValue, maxZ = double.MinValue, minX = double.MaxValue, maxX = double.MinValue;
                foreach (var p in xz) { minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z); minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); }
                Check(Math.Abs(minZ + 5) < 1e-9 && Math.Abs(maxZ - 5) < 1e-9, $"entrance: kerb to kerb across the carriageway ({minZ:0.##}..{maxZ:0.##})");
                Check(Math.Abs(minX - 46) < 1e-9 && Math.Abs(maxX - 54) < 1e-9, "entrance: 8 m along the road");
                Check(Math.Abs(G.SignedArea(ring) - 80) < 1e-6 && G.IsSimple(xz), "entrance: 8 x 10 m, CCW, simple");
            }
            // same inset logic as the junction: unequal footways (left 2 / right 4) give left kerb z = +6, right z = -4
            ring = EntranceBox.Build(c, 8, 2, 4, out _);
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (var p in ring) { lo = Math.Min(lo, p.Z); hi = Math.Max(hi, p.Z); }
            Check(Math.Abs(hi - 6) < 1e-9 && Math.Abs(lo + 4) < 1e-9, "entrance: each side inset by its own footway (left = left of the road direction)");
            // follows a curved road
            var arc = new List<V3>();
            for (int i = 0; i <= 8; i++) { double t = i / 8.0 * 0.4; arc.Add(new V3(50 * Math.Sin(t), 0, 50 - 50 * Math.Cos(t))); }
            ring = EntranceBox.Build(arc, 8, 3, 3, out why);
            Check(ring != null && G.IsSimple(G.ToXZ(ring)), "entrance: follows a curve (" + why + ")");
            Check(EntranceBox.Build(c, 3, 3, 3, out why) == null && why.Contains("0.5 m"), "entrance: no carriageway = no box, says why");
        }
    }
}
