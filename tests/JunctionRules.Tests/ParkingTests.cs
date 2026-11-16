using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.7 (JR-T-013d, G6): no parking in yellow boxes. Core/ParkingMask.cs (which curve ranges of a parking lane lie in
    /// a box, as LaneOverlap bytes; the game's slot walk; the overlap list plan) and repo checks of Parking/BoxParkingSystem.cs
    /// and the copied free-space code (Parking/ParkingFreeSpaceCopy.cs) against the decompile.
    /// </summary>
    internal static partial class Program
    {
        private static List<V2> Rect(double x0, double z0, double x1, double z1)
            => new List<V2> { new V2(x0, z0), new V2(x1, z0), new V2(x1, z1), new V2(x0, z1) };

        private static IList<IList<V2>> Rings(params List<V2>[] r)
        {
            var l = new List<IList<V2>>();
            foreach (var x in r) l.Add(x);
            return l;
        }

        // straight lane along +X at z, from x0 to x1 (control points on thirds)
        private static List<TRange> Straight(double x0, double x1, double z, double halfWidth, IList<IList<V2>> rings)
            => ParkingMask.InsideRanges(new V3(x0, 0, z), new V3(x0 + (x1 - x0) / 3, 0, z), new V3(x0 + 2 * (x1 - x0) / 3, 0, z), new V3(x1, 0, z), halfWidth, rings);

        private static bool Near(double a, double b, double eps = 2e-3) => Math.Abs(a - b) <= eps;

        private static void ParkingMaskTests()
        {
            // a 40 m kerbside parking lane (2 m wide) at z = 4; a box x 10..20 across the road
            var box = Rings(Rect(10, -6, 20, 6));
            var r = Straight(0, 40, 4, 1, box);
            Check(r.Count == 1 && Near(r[0].Min, 0.25) && Near(r[0].Max, 0.5), "partial overlap: the box part only (0.25..0.5)");
            r = Straight(0, 40, 4, 1, Rings(Rect(-5, -6, 45, 6)));
            Check(r.Count == 1 && r[0].Min == 0 && r[0].Max == 1, "full overlap: the whole lane");
            r = Straight(0, 40, 4, 1, Rings(Rect(10, 8, 20, 12)));
            Check(r.Count == 0, "no overlap: box beside the lane");
            r = Straight(0, 40, 4, 1, Rings(Rect(10, 6, 20, 12)));
            Check(r.Count == 0, "no overlap: box edge exactly at the lane's outer side");
            // side tolerance: strip reaches z 3..5; a box from z 4.95 overlaps 5 cm (< 10 cm tolerance), from z 4.8 overlaps 20 cm
            Check(Straight(0, 40, 4, 1, Rings(Rect(10, 4.95, 20, 12))).Count == 0, "side tolerance: a 5 cm graze does not block");
            Check(Straight(0, 40, 4, 1, Rings(Rect(10, 4.8, 20, 12))).Count == 1, "side tolerance: 20 cm into the lane blocks");
            // both sides of the road (left-hand traffic: the near-side lane runs +X at z = -4, the far side -X at z = +4)
            var nearSide = Straight(0, 40, -4, 1, box);
            var farSide = ParkingMask.InsideRanges(new V3(40, 0, 4), new V3(26.67, 0, 4), new V3(13.33, 0, 4), new V3(0, 0, 4), 1, box);
            Check(nearSide.Count == 1 && Near(nearSide[0].Min, 0.25) && Near(nearSide[0].Max, 0.5), "both sides: lane one way");
            Check(farSide.Count == 1 && Near(farSide[0].Min, 0.5) && Near(farSide[0].Max, 0.75), "both sides: lane the other way (curve positions reversed)");
            // two boxes on one lane (two exit boxes, or a junction box and an exit box) -> two ranges; overlapping ones merge
            r = Straight(0, 40, 4, 1, Rings(Rect(2, -6, 6, 6), Rect(30, -6, 34, 6)));
            Check(r.Count == 2 && Near(r[0].Min, 0.05) && Near(r[1].Max, 0.85), "two boxes: two ranges");
            r = Straight(0, 40, 4, 1, Rings(Rect(10, -6, 20, 6), Rect(18, -6, 26, 6)));
            Check(r.Count == 1 && Near(r[0].Min, 0.25) && Near(r[0].Max, 0.65), "overlapping (merged) boxes: one range = the union");
            // an exit box: a short kerb-to-kerb rectangle in front of a driveway (6 m along the road), a slanted one too
            r = Straight(0, 40, 4, 1, Rings(Rect(17, -5, 23, 5)));
            Check(r.Count == 1 && Near(r[0].Min, 17 / 40.0) && Near(r[0].Max, 23 / 40.0), "exit box: its 6 m only");
            var slanted = new List<V2> { new V2(17, -5), new V2(23, -5), new V2(25, 5), new V2(19, 5) };
            r = Straight(0, 40, 4, 1, Rings(slanted));
            // at z 3..5 the box runs x 18.6..24.6 (+-0.1 tolerance on the side): from about 18.62 to 24.98
            Check(r.Count == 1 && r[0].Min > 18.5 / 40 && r[0].Min < 18.8 / 40 && r[0].Max > 24.8 / 40 && r[0].Max < 25.0 / 40, "slanted exit box: the lane's own crossing range");
            // a curved lane (quarter bend, radius ~20): a box over its end
            r = ParkingMask.InsideRanges(new V3(0, 0, 0), new V3(11, 0, 0), new V3(20, 0, 9), new V3(20, 0, 20), 1, Rings(Rect(15, 15, 25, 25)));
            Check(r.Count == 1 && r[0].Max == 1 && r[0].Min > 0.6 && r[0].Min < 0.95, "curved lane: the end in the box");
            // a reversed ring (other winding) gives the same
            var rev = Rect(10, -6, 20, 6); rev.Reverse();
            r = Straight(0, 40, 4, 1, Rings(rev));
            Check(r.Count == 1 && Near(r[0].Min, 0.25) && Near(r[0].Max, 0.5), "ring winding does not matter");

            // bytes: widened outward, merged after rounding
            var b = ParkingMask.ToBytes(new List<TRange> { new TRange(0.25, 0.5) });
            Check(b.Count == 1 && b[0].Start == 63 && b[0].End == 128, "bytes floor / ceil (63..128)");
            b = ParkingMask.ToBytes(new List<TRange> { new TRange(0.0, 1.0) });
            Check(b.Count == 1 && b[0].Start == 0 && b[0].End == 255, "bytes whole lane 0..255");
            b = ParkingMask.ToBytes(new List<TRange> { new TRange(0.1, 0.2), new TRange(0.2005, 0.3) });
            Check(b.Count == 1, "ranges touching after rounding merge");

            // the game's slots: 40 m lane, 6 slots of 6 m, centred (2 m each end); a box x 10..19 blocks slots 2 and 3 only
            var slots = ParkingMask.SlotRanges(new V3(0, 0, 4), new V3(13.33, 0, 4), new V3(26.67, 0, 4), new V3(40, 0, 4), 40, 6, 6, false, false);
            Check(slots.Count == 6 && Near(slots[0].Min, 2 / 40.0) && Near(slots[0].Max, 8 / 40.0) && Near(slots[5].Max, 38 / 40.0), "stock slot walk: 6 slots centred");
            var ov = ParkingMask.ToBytes(Straight(0, 40, 4, 1, Rings(Rect(10, -6, 19, 6))));
            Check(ParkingMask.BlockedSlots(slots, ov) == 2 && !ParkingMask.SlotBlocked(slots[0], ov) && ParkingMask.SlotBlocked(slots[1], ov)
                && ParkingMask.SlotBlocked(slots[2], ov) && !ParkingMask.SlotBlocked(slots[3], ov), "box x 10..19 blocks the 2 slots it covers");
            Check(ParkingMask.BlockedSlots(slots, ParkingMask.ToBytes(Straight(0, 40, 4, 1, Rings(Rect(-5, -6, 45, 6))))) == 6, "full box blocks every slot");
            Check(ParkingMask.BlockedSlots(slots, new List<(byte, byte)>()) == 0, "no box: no slot blocked");
            var startSlots = ParkingMask.SlotRanges(new V3(0, 0, 4), new V3(13.33, 0, 4), new V3(26.67, 0, 4), new V3(40, 0, 4), 40, 6, 6, true, false);
            Check(startSlots.Count == 6 && Near(startSlots[0].Min, 4 / 40.0), "starting lane: slots pushed to the end");

            // the overlap list: stock entries kept in order, ours sorted in by the game's key
            var cur = new List<OverlapEntry> { new OverlapEntry(10, 20, false, 0), new OverlapEntry(200, 210, false, 1) };
            var want = new List<(byte, byte)> { (63, 128) };
            var plan = ParkingMask.Plan(cur, want);
            Check(plan.Count == 3 && !plan[0].Ours && plan[0].Start == 10 && plan[1].Ours && plan[1].Start == 63 && !plan[2].Ours && plan[2].Start == 200
                && plan[0].Index == 0 && plan[2].Index == 1, "plan: ours sorted between the stock entries, stock indexes kept");
            var applied = new List<OverlapEntry>();
            for (int i = 0; i < plan.Count; i++) applied.Add(new OverlapEntry(plan[i].Start, plan[i].End, plan[i].Ours, i));
            Check(ParkingMask.HasExactly(applied, want), "idempotent: nothing to write the second time");
            Check(!ParkingMask.HasExactly(applied, new List<(byte, byte)> { (63, 130) }), "a changed box rewrites");
            Check(!ParkingMask.HasExactly(cur, want), "a rebuilt lane (ours wiped by the game) is written again");
            // removal restores: no wanted ranges -> exactly the stock list, in its order
            var removed = ParkingMask.Plan(applied, null);
            Check(removed.Count == 2 && removed[0].Start == 10 && removed[1].Start == 200 && !removed[0].Ours && !removed[1].Ours, "removal: the stock list back");
            Check(ParkingMask.HasExactly(cur, null) && ParkingMask.HasExactly(cur, new List<(byte, byte)>()), "nothing of ours on a stock lane");
            // removal restores the slots: the stock entries alone block none of the box's slots
            var stockOnly = new List<(byte, byte)>();
            foreach (var e in removed) stockOnly.Add((e.Start, e.End));
            // stock (10..20) and (200..210) block slots 0, 4 and 5; ours (63..128) adds 1, 2 and 3 (the box edge at 0.502 reaches slot 3)
            Check(ParkingMask.BlockedSlots(slots, stockOnly) == 3 && ParkingMask.BlockedSlots(slots, new List<(byte, byte)> { (10, 20), (63, 128), (200, 210) }) == 6, "removal: only the stock blocks remain (3 of 6)");
        }

        /// <summary>Extracts a method body (from the signature's first '{' to its matching '}'), whitespace collapsed.</summary>
        private static string Body(string text, string signatureStart)
        {
            int i = text.IndexOf(signatureStart, StringComparison.Ordinal);
            if (i < 0) return null;
            int open = text.IndexOf('{', i);
            int depth = 0;
            for (int k = open; k < text.Length; k++)
            {
                if (text[k] == '{') depth++;
                else if (text[k] == '}' && --depth == 0) return Regex.Replace(text.Substring(open, k - open + 1), @"\s+", " ");
            }
            return null;
        }

        private static void ParkingRepoTests()
        {
            string root = RepoRoot();
            if (root == null) { Check(false, "repo root found"); return; }
            string copy = File.ReadAllText(Path.Combine(root, "JunctionRules", "Parking", "ParkingFreeSpaceCopy.cs"));
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string game = Path.Combine(home, "Code", "decomp", "yb-2026-11-13", "full", "Game.Pathfind", "ParkingLaneDataSystem.cs");
            if (File.Exists(game))
            {
                string g = File.ReadAllText(game);
                string a1 = Body(copy, "Bounds1 GetBlockedRange("), b1 = Body(g, "Bounds1 GetBlockedRange(");
                string a2 = Body(copy, "float CalculateFreeSpace("), b2 = Body(g, "float CalculateFreeSpace(");
                Check(a1 != null && a1 == b1, "copied GetBlockedRange body = the game's");
                Check(a2 != null && a2 == b2, "copied CalculateFreeSpace body = the game's");
                Check(g.Contains("laneObjects.AsNativeArray().Sort();") && g.Contains("parkingLane.m_FreeSpace = CalculateFreeSpace(curve, parkingLane, parkingLaneData, laneObjects, laneOverlaps, blockedRange);"),
                    "the game sorts the lane objects before CalculateFreeSpace (as PrepareForSave does)");
                string lo = Path.Combine(home, "Code", "decomp", "yb-2026-11-13", "full", "Game.Net", "LaneOverlap.cs");
                if (File.Exists(lo)) Check(File.ReadAllText(lo).Contains("IEmptySerializable"), "LaneOverlap is never saved (IEmptySerializable)");
            }
            else Console.WriteLine("note: decompile not found, copied parking code not cross-checked");

            string sys = File.ReadAllText(Path.Combine(root, "JunctionRules", "Parking", "BoxParkingSystem.cs"));
            Check(!Regex.IsMatch(sys, @"\.m_Flags\s*(\||&|\^)?=[^=]"), "the parking system never writes ParkingLane flags");
            Check(!sys.Contains("SetComponentData(lane, pl)") || Regex.Matches(sys, @"SetComponentData\(lane, pl\)").Count == 2, "ParkingLane written only by the save guard (stock value / optimistic value)");
            Check(sys.Contains("if (SaveRunning) return;"), "no lane changes while a save is written");
            Check(!sys.Contains("Despawn") && !sys.Contains("AddComponent<Deleted>") && !sys.Contains("AddComponent<Updated>"), "parked cars untouched: no despawn, no Updated (FixParkingLocationSystem not woken)");
            string reg = File.ReadAllText(Path.Combine(root, "JunctionRules", "Boxes", "BoxRegistration.cs"));
            Check(reg.Contains("updateSystem.UpdateBefore<JunctionRules.Parking.BoxParkingSystem, Game.Pathfind.ParkingLaneDataSystem>(SystemUpdatePhase.ModificationEnd);"), "registered ahead of the game's ParkingLaneDataSystem");
            Check(reg.Contains("updateSystem.UpdateBefore<JunctionRules.Parking.BoxParkingSaveGuardSystem>(SystemUpdatePhase.Serialize);"), "save guard in the Serialize phase");
            string set = File.ReadAllText(Path.Combine(root, "JunctionRules", "Settings", "JunctionRulesSettings.cs"));
            Check(set.Contains("NoParkingInBoxes = true;") && set.Contains("\"No parking in yellow boxes\""), "setting 'No parking in yellow boxes', default ON");
            string paint = File.ReadAllText(Path.Combine(root, "JunctionRules", "Paint", "YellowBoxPaintSystem.cs"));
            Check(Regex.Matches(paint, @"PublishParking\(\);").Count == 3, "boxes handed over on change, deactivation and load");
            Check(paint.Contains("b.Kind != BoxKind.ListJunction && b.Tris.Length > 0"), "only saved, painted boxes block parking");
        }
    }
}
