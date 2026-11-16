using System;
using System.Collections.Generic;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.3-probe (JR-T-007c, owner rulings 16 Nov): building boxes sit at each car EXIT; one box per exit; service
    /// access-road junctions; fallback; save format v2 + migration of 0.4.0-0.4.2 entrance boxes; F1 (exit held inside
    /// the box on a bend, slide split between both mouths). Live fixtures read-only from the running city 16 Nov 2026.
    /// </summary>
    internal static partial class Program
    {
        private static V2 Fwd(double rotDeg) { double a = rotDeg * Math.PI / 180; return new V2(Math.Sin(a), Math.Cos(a)); }

        private static ExitCandidate Cand(ExitKind kind, long road, double along, V2 world, V2 pos, V2 fwd, string src)
        {
            V2 l = ExitRules.ToLocal(world, pos, fwd);
            return new ExitCandidate { Kind = kind, Key = new ExitKey(kind, l.X, l.Z), RoadId = road, AlongM = along, World = world, Source = src };
        }

        private static void ExitTests()
        {
            // ---- Capper Street house (live: UK Medium Detached 14, pos 491.2826/609.6270, rot 246.412354, lot 24 x 48;
            // its Invisible Car Path 279022 ends at node 83008 (473.450, 591.216), joined mid-edge to Capper Street 985013).
            {
                V2 pos = new V2(491.2826, 609.6270), f = Fwd(246.412354);
                V2 drive = new V2(473.450, 591.216);
                V2 l = ExitRules.ToLocal(drive, pos, f);
                Check(Math.Abs(l.X + 9.7366) < 0.05 && Math.Abs(l.Z - 23.7096) < 0.05, $"Capper: driveway node in the building frame ({l.X:0.000}, {l.Z:0.000}) = prefab sub-net end (-9.737, 23.710)");
                V2 back = ExitRules.ToWorld(l, pos, f);
                Check(V2.Distance(back, drive) < 1e-6, "Capper: ToWorld inverts ToLocal");
                // the old 0.4.0-0.4.2 box centre: lot front middle (the fallback access point)
                V2 front = ExitRules.ToWorld(new V2(0, 24), pos, f);
                double off = V2.Distance(front, drive);
                Check(off > 9 && off < 10.5, $"Capper: the exit is {off:0.0} m from the lot-front middle (owner screenshot: drive at the lot's right edge)");
                // the exit point lies on the Capper Street carriageway line (road 487.567/538.191 -> 447.567/629.800)
                V2 a = new V2(487.567, 538.191), b = new V2(447.567, 629.800);
                V2 d = (b - a).Normalized;
                double t = V2.Dot(drive - a, d), lat = Math.Abs(V2.Dot(drive - a, d.Left));
                Check(t > 0 && t < V2.Distance(a, b) && lat < 10, $"Capper: driveway node joins Capper Street mid-edge ({t:0.0} m along, {lat:0.00} m off the centreline: at the lot line behind the footway; the box uses the road point at t)");
                var exits = ExitRules.Normalise(new[]
                {
                    Cand(ExitKind.Fallback, 985013, t - 4, front, pos, f, "fallback: Building.m_RoadEdge"),
                    Cand(ExitKind.Driveway, 985013, t, drive, pos, f, "driveway: sub-net node joined mid-road"),
                    Cand(ExitKind.SpawnLocation, 985013, t + 1.5, drive + d * 1.5, pos, f, "spawn location on the same drive"),
                });
                Check(exits.Count == 1 && exits[0].Kind == ExitKind.Driveway, $"Capper: one exit, the driveway (spawn on the same drive and the fallback dropped) ({exits.Count})");
                int m = ExitRules.Migrate(front, exits, out string why);
                Check(m == 0 && why.Contains("Driveway"), "Capper: an old entrance box migrates to the driveway exit: " + why);
                Check(ExitRules.Match(exits[0].Key, exits) == 0, "Capper: the saved key finds its exit");
            }

            // ---- Multi-exit: ParkingHall03 (live, centre -614.64/79.77): two Invisible Car Path exits (85685, 85800) and
            // two one-way Invisible Road Path exits (993385, 993407), all joined mid-road. Rotation not captured (frame-free checks).
            {
                V2 pos = new V2(-614.64, 79.77), f = Fwd(90);
                var w = new[] { new V2(-612.160, 135.339), new V2(-618.216, 29.219), new V2(-624.525, 50.972), new V2(-644.261, 94.688) };
                var found = new List<ExitCandidate>();
                for (int i = 0; i < w.Length; i++) found.Add(Cand(ExitKind.Driveway, 100 + i, 10, w[i], pos, f, "driveway " + i));
                found.Add(Cand(ExitKind.Fallback, 100, 12, pos, pos, f, "fallback"));
                var exits = ExitRules.Normalise(found);
                Check(exits.Count == 4, $"multi-exit: car park hall has 4 exits, 4 boxes ({exits.Count})");
                var keys = new HashSet<ExitKey>();
                bool selfMatch = true;
                for (int i = 0; i < exits.Count; i++) { keys.Add(exits[i].Key); selfMatch &= ExitRules.Match(exits[i].Key, exits) == i; }
                Check(keys.Count == 4 && selfMatch, "multi-exit: each exit has its own key and each key finds only its own exit");
                Check(ExitRules.Migrate(new V2(-617, 34), exits, out _) == exits.FindIndex(e => V2.Distance(e.World, w[1]) < 0.01), "multi-exit: old box migrates to the nearest exit");
                var near = new ExitKey(ExitKind.Driveway, exits[0].Key.X + 1.5, exits[0].Key.Z);
                Check(ExitRules.Match(near, exits) == 0, "multi-exit: a key 1.5 m off (game moved the drive a little) still matches");
                var far = new ExitKey(ExitKind.Driveway, exits[0].Key.X + 2.5, exits[0].Key.Z);
                Check(ExitRules.Match(far, exits) < 0, "multi-exit: a key 2.5 m off finds nothing (dropped with a log line)");
                // a fallback key once the game shows a real drive: matches the nearest non-junction exit within 2 m
                var fbKey = new ExitKey(ExitKind.Fallback, exits[2].Key.X, exits[2].Key.Z + 1);
                Check(ExitRules.Match(fbKey, exits) == 2, "multi-exit: a fallback key re-keys onto the real exit next to it");
                // live depot fixture: RoadMaintenanceDepot01 has two driveways on the same road 4.9 m apart -> two exits
                var dep = ExitRules.Normalise(new[]
                {
                    Cand(ExitKind.Driveway, 7, 10.0, new V2(236.821, -464.090), new V2(287.09, -402.82), f, "a"),
                    Cand(ExitKind.Driveway, 7, 14.9, new V2(232.135, -462.561), new V2(287.09, -402.82), f, "b"),
                });
                Check(dep.Count == 2, "multi-exit: two drives 4.9 m apart on one road stay two exits (SameExitAlong 3 m)");
            }

            // ---- Fallback: kept only when nothing else is found
            {
                V2 pos = new V2(0, 0), f = Fwd(0);
                var only = ExitRules.Normalise(new[] { Cand(ExitKind.Fallback, 5, 3, new V2(0, 12), pos, f, "fallback: Building.m_RoadEdge (no sub-net drive, no spawn)") });
                Check(only.Count == 1 && only[0].Kind == ExitKind.Fallback && only[0].Source.StartsWith("fallback"), "fallback: a building without a drive or spawn keeps its road-access exit (logged as fallback)");
                Check(ExitRules.Normalise(null).Count == 0 && ExitRules.Migrate(new V2(0, 0), new List<ExitCandidate>(), out string why) < 0 && why.Contains("no car exit"), "fallback: nothing at all -> no exit, an old box is dropped: " + "no car exit");
            }

            // ---- Service access-road junction (owner ruling 3) and nodes inside a site
            {
                var pub = new JunctionFacts { Exists = true, IsRoadNode = true, RoadArms = 3 };
                Check(BoxSelection.JunctionOk(pub, out _), "service: plain public T junction selectable");
                var svc = new JunctionFacts { Exists = true, IsRoadNode = true, RoadArms = 3, PublicRoadArms = 2, BuildingRoadArms = 1 };
                Check(BoxSelection.JunctionOk(svc, out string w1) && BoxSelection.IsBuildingAccessJunction(svc), "service: depot access road meeting a public road is a selectable junction " + w1);
                var svcOwnedNode = svc; svcOwnedNode.Owned = true; svcOwnedNode.NodeOwnerIsBuilding = true;
                Check(BoxSelection.JunctionOk(svcOwnedNode, out _), "service: same when the node itself belongs to the building");
                var lot = new JunctionFacts { Exists = true, IsRoadNode = true, RoadArms = 3, BuildingRoadArms = 3, Owned = true, NodeOwnerIsBuilding = true };
                Check(!BoxSelection.JunctionOk(lot, out string w2) && w2.Contains("inside a building"), "service: node inside a parking lot (all arms the building's) still excluded: " + w2);
                var asset = new JunctionFacts { Exists = true, IsRoadNode = true, RoadArms = 3, PublicRoadArms = 2, OtherOwnedRoadArms = 1 };
                Check(!BoxSelection.JunctionOk(asset, out string w3) && w3.Contains("asset"), "service: a non-building asset's road still excluded: " + w3);
                var mixed = new JunctionFacts { Exists = true, IsRoadNode = true, RoadArms = 3, PublicRoadArms = 1, BuildingRoadArms = 2, MixedOwners = true };
                Check(!BoxSelection.JunctionOk(mixed, out string w4) && w4.Contains("two different"), "service: two buildings' roads meeting excluded: " + w4);
                var edgeNode = new JunctionFacts { Exists = true, IsRoadNode = true, RoadArms = 2, PublicRoadArms = 1, BuildingRoadArms = 1 };
                Check(!BoxSelection.JunctionOk(edgeNode, out _), "service: access road continuing a public road end to end (2 arms) is not a junction");
                // arm yield: building arm yields unless signed or signalised
                var y = ExitRules.ArmYield(new[] { false, false, false }, new[] { false, false, true }, false);
                Check(!y[0] && !y[1] && y[2], "service: unsigned, no signals -> the building arm yields");
                y = ExitRules.ArmYield(new[] { false, true, false }, new[] { false, false, true }, false);
                Check(!y[0] && y[1] && !y[2], "service: a signed public arm -> signs stand (building arm has priority as signed)");
                y = ExitRules.ArmYield(new[] { false, false, false }, new[] { false, false, true }, true);
                Check(!y[0] && !y[1] && !y[2], "service: signals -> all arms equal");
                // migration onto a junction exit
                var ex = ExitRules.Normalise(new[] { new ExitCandidate { Kind = ExitKind.Junction, Key = new ExitKey(ExitKind.Junction, 0, 20), RoadId = 9, World = new V2(0, 20), Source = "junction" } });
                Check(ExitRules.Migrate(new V2(1, 19), ex, out _) == 0 && ExitRules.Match(ex[0].Key, ex) < 0, "service: an old box migrates onto the junction exit (a junction box), never kept as an exit key");
            }

            // ---- Save format v2 and migration of 0.4.0-0.4.2 records
            {
                var keys = new ExitKeyList();
                keys.Add(new ExitKey(ExitKind.Driveway, -9.7366, 23.7096));
                keys.Add(new ExitKey(ExitKind.SpawnLocation, 12.3, -4.5));
                keys.Add(new ExitKey(ExitKind.Fallback, -3276.0, 3276.0));
                var s = new ArrayBytes { Out = new List<byte>() };
                ExitRecord.Encode(ref s, BoxRecordFlags.None, ref keys);
                byte[] bytes = s.Out.ToArray();
                Check(bytes.Length == 2 + 2 + 5 * 3 && bytes[0] == 2 && bytes[1] == 17, $"save v2: [2][len 17][flags][3][3 x 5] ({bytes.Length} bytes)");
                var src = new ArrayBytes { Data = bytes };
                var back = new ExitKeyList();
                Check(ExitRecord.Decode(ref src, ref back, out var fl, out byte v) && v == 2 && fl == BoxRecordFlags.None && src.Pos == bytes.Length && back.Count == 3, "save v2: round trip, every byte consumed");
                bool same = back.Count == 3;
                for (int i = 0; same && i < 3; i++) same = back.Get(i).Equals(keys.Get(i));
                Check(same && back.Get(0).X10 == -97 && back.Get(0).Z10 == 237, "save v2: keys exact to 0.1 m, negative and large values intact");
                // 0.4.0-0.4.2 reader (BoxRecord.Decode) reading a v2 record: flags read, rest skipped by length
                src = new ArrayBytes { Data = bytes };
                Check(BoxRecord.Decode(ref src, out BoxRecord old, out byte ov) && ov == 2 && old.Flags == BoxRecordFlags.None && src.Pos == bytes.Length, "save v2: an older mod build reads it and skips the keys");
                // a v1 record (0.4.0-0.4.2 entrance box) read by 0.4.3: no keys = legacy, to migrate
                byte[] v1 = ArrayBytes.Encode(new BoxRecord { Flags = BoxRecordFlags.None });
                src = new ArrayBytes { Data = v1 };
                var none = new ExitKeyList();
                Check(ExitRecord.Decode(ref src, ref none, out _, out byte vv) && vv == 1 && none.Count == 0 && src.Pos == v1.Length, "save v1: old entrance box decodes as legacy (no keys) -> migrated on load");
                // truncated key list: no partial key
                byte[] cut = new byte[] { 2, 4, 0, 1, (byte)ExitKind.Driveway, 5 };
                src = new ArrayBytes { Data = cut };
                var part = new ExitKeyList();
                Check(ExitRecord.Decode(ref src, ref part, out _, out _) && part.Count == 0 && src.Pos == cut.Length, "save v2: a truncated key is not invented, length still honoured");
                // version 0 refused
                src = new ArrayBytes { Data = new byte[] { 0, 0 } };
                Check(!ExitRecord.Decode(ref src, ref part, out _, out _), "save: version 0 refused");
                // cap
                var many = new ExitKeyList();
                for (int i = 0; i < 60; i++) many.Add(new ExitKey(ExitKind.Driveway, i, 0));
                s = new ArrayBytes { Out = new List<byte>() };
                ExitRecord.Encode(ref s, BoxRecordFlags.None, ref many);
                Check(s.Out.Count == 4 + 5 * ExitRecord.MaxKeys && s.Out[1] <= 255, "save v2: at most 50 keys, length fits a byte");
                // without the mod: the game skips the whole component block (BlockModel, as BoxToolTests)
                var stream = new List<byte>();
                BlockModel.WriteBlock(stream, new List<BoxRecord> { new BoxRecord() });
                Check(BlockModel.SkipBlock(stream.ToArray(), 0) == stream.Count, "save: a game without the mod skips the block cleanly");
            }

            // ---- F1: exit off-centre on a bend is held inside the box; the slide splits between both mouths
            foreach (double R in new[] { 30.0, 60.0 })
            {
                var c = new List<V3>();
                for (int i = 0; i <= 16; i++) { double a = (i / 16.0) * (8 / R); c.Add(new V3(R * Math.Sin(a), 0, R - R * Math.Cos(a))); }
                // exit 5.6 m along the 8 m window (off-centre), 1 m in from the outer kerb (carriageway half 5)
                double ea = 5.6 / R, er = R + 4;
                V2 ctr = new V2(0, R);
                V2 exit = new V2(er * Math.Sin(ea), R - er * Math.Cos(ea));
                var r = EntranceBox.BuildStraight(c, 8, 3, 3, null, 0.5, exit);
                Check(r.Outline != null, $"F1 R={R}: box built " + r.Failure);
                if (r.Outline == null) continue;
                var xz = BoxPaintGeometry.ToXZ(r.Outline);
                Check(BoxPaintGeometry.Contains(xz, exit, 1e-3), $"F1 R={R}: the off-centre exit stays inside the box (shifts {r.Shift[0]:0.00} | {r.Shift[1]:0.00})");
                double tot = r.Shift[0] + r.Shift[1], mx = Math.Max(r.Shift[0], r.Shift[1]);
                Check(tot == 0 || (r.Shift[0] > 0 && r.Shift[1] > 0) || mx < 0.3, $"F1 R={R}: the slide is shared by both mouths ({r.Shift[0]:0.00} | {r.Shift[1]:0.00})");
                double len = 8 - tot;
                Check(len >= 4, $"F1 R={R}: box stays at least 4 m long ({len:0.0} m of 8; a straight-sided box on a 25 m inner kerb cannot be longer)");
                double worst = 0;
                foreach (var sg in BoxHover.Segments(r.Outline))
                    for (int k = 0; k <= 20; k++)
                    {
                        V2 p = sg.A.XZ + (sg.B.XZ - sg.A.XZ) * (k / 20.0);
                        double rad = V2.Distance(p, ctr);
                        worst = Math.Max(worst, Math.Max((R - 5) - rad, rad - (R + 5)));
                    }
                Check(worst <= StraightBox.KerbTolerance + 0.005, $"F1 R={R}: every side on the carriageway (worst {worst:0.000} m over)");
                // without a hold point (the 0.4.2 call) the result is unchanged in kind
                var r0 = EntranceBox.BuildStraight(c, 8, 3, 3, null, 0.5);
                Check(r0.Outline != null, $"F1 R={R}: old overload still builds");
            }
        }
    }
}
