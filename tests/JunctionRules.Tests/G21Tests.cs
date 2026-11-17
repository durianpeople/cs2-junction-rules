using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.11 (JR-T-017, gap G21, 0.4.10 live 17 Nov 19:00): a vehicle turning INTO a building across oncoming traffic did not
    /// give way. Live: van 380934 (dest 839552 = Scandic Baron, 40 Alderwood St), eastbound lane (north, z ~278), turned right
    /// (left-hand traffic) across the westbound lane (z ~271) into No. 40's entrance (x ~-1994); log 18:59:29 "held before the
    /// box (way in full)", 19:00:05 "stands in the box 30 s (way in has room: waiting to turn)", then read-only at frame 36633332
    /// at (-1995.1, 275.6) heading 155 at 2.5 m/s, crossing in front of westbound traffic (owner shot 60253f58).
    /// Scene below from the live and logged positions: eastbound lane centre z 278.0, westbound lane 269.25..272.75 (centre
    /// 271.0, the 0.4.7 track's sidewalk edge 269.25), lane width 3.5 m; the way in's overlap with the westbound lane starts
    /// where the two lane bodies (0.4 x width each, LaneOverlapSystem 1167-1168) meet: z 271.0 + 2.8 = 273.8, about 5 m along
    /// the way in from its start, and runs about 6 m along it; No. 40's entrance box (34.7..42.7 m along the edge, merged with
    /// the exit box 45.5..53.5 since 0.4.10) puts the box line about 4 m before the drive start on the eastbound lane.
    /// </summary>
    internal static partial class Program
    {
        private const float G21Dt = 4f / 15f;
        // Kinematics (stock-like): car as ArrivalTests' typical car, a delivery van, an articulated lorry (G7).
        private static StockCarMotion G21Car => StockCarMotion.Of(40f, 5f, 8f);
        private static StockCarMotion G21Van => StockCarMotion.Of(30f, 3f, 6f);
        private static StockCarMotion G21Lorry => StockCarMotion.Of(22f, 1.6f, 4f);
        private const float G21DriveToConflict = 5f, G21ConflictLength = 6f, G21BoxLineBack = 4f, G21WaySpeed = 6f;
        private const float G21Margin = 0.9f;

        private static float G21Braking(float v) => v * v / (2f * 8f);

        // Game.Net.CarLaneFlags of the lanes LaneSystem.CreateEdgeConnectionLane lays (3864-3878).
        private const uint G21Unsafe = 0x1u, G21SideConnection = 0x8u, G21TurnLeft = 0x10u, G21TurnRight = 0x20u, G21Yield = 0x400u;
        // OverlapFlags (Game.Net/OverlapFlags.cs): MergeStart 0x1, OverlapLeft 0x4, OverlapRight 0x8, MergeMiddleStart 0x10, Road 0x80, Water 0x800;
        // a crossing gets OverlapLeft | OverlapRight (LaneOverlapSystem 1265), the lane it leaves MergeStart (1200-1209).
        private const int G21OvCross = WholePathRule.Road | 0x4 | 0x8, G21OvOwnLane = WholePathRule.Road | WholePathRule.MergeStart;

        private static void G21EntryTests()
        {
            // --- which lanes: the way in, its crossed lanes ---
            Check(EntryGiveWay.IsDrivewayEntryLane(G21Unsafe | G21SideConnection | G21TurnRight), "way in (Unsafe | SideConnection | TurnRight, no Yield) is an entry lane");
            Check(!EntryGiveWay.IsDrivewayEntryLane(G21Unsafe | G21SideConnection | G21Yield | G21TurnRight), "the way OUT (Yield) is not (the exit rule's lane)");
            Check(!EntryGiveWay.IsDrivewayEntryLane(0x100000u) && !EntryGiveWay.IsDrivewayEntryLane(0u), "road lanes are not entry lanes");
            Check(EntryGiveWay.IsCrossedRoad(G21OvCross, true, false, false), "the westbound lane the right turn crosses is judged");
            Check(!EntryGiveWay.IsCrossedRoad(G21OvOwnLane, true, false, false), "the lane it leaves (merge at the start of the way in) is not judged");
            Check(!EntryGiveWay.IsCrossedRoad(WholePathRule.Road | WholePathRule.MergeMiddleStart, true, false, false), "a middle-start merge with the lane it leaves is not judged");
            Check(!EntryGiveWay.IsCrossedRoad(G21OvCross, true, false, true), "own road lane excluded by entity too");
            Check(!EntryGiveWay.IsCrossedRoad(G21OvCross, true, true, false), "another drive's way in / out is not road traffic (stock decides)");
            Check(!EntryGiveWay.IsCrossedRoad(G21OvCross, false, false, false), "a sidewalk (not a car lane) is not a gap conflict");
            Check(!EntryGiveWay.IsCrossedRoad(G21OvCross | WholePathRule.Water, true, false, false), "water is never judged");
            // Left in (left-hand traffic, near side): its overlaps are the lane it leaves and the sidewalk: nothing to give way to.
            int[] leftIn = { G21OvOwnLane };
            Check(!leftIn.Any(f => EntryGiveWay.IsCrossedRoad(f, true, false, false)), "left turn in on the near side: no crossed road lane, no wait (merge only)");

            // --- the turn-across rule could not see it (0.4.10 root cause) ---
            uint way = G21Unsafe | G21SideConnection | G21TurnRight;
            Check(TurnAcross.SignStatus(way) != TurnAcross.SignStatus(0u), "the way in carries Unsafe, the road lane does not: sign status differs");
            Check(TurnAcross.Delta(way, 0u, false, false, 0, true) == 0, "so the 0.2.4 turn-across rule keeps stock for the way in (and the road lane has no Forward flag)");

            // --- the spot: before the box, else at the drive start in its own lane, else committed ---
            float vanBraking8 = 8.3f * 8.3f / (2f * 6f);   // a van at 30 km/h
            Check(EntryGiveWay.Choose(true, false, 20f, 24f, G21Margin, vanBraking8) == EntryGiveWay.Spot.BoxLine, "20 m before the box line at 30 km/h: waits before the box");
            Check(EntryGiveWay.Choose(true, false, 2f, 6f, G21Margin, vanBraking8) == EntryGiveWay.Spot.DriveStart, "too late for the box line: at the drive start, still in its own lane");
            Check(EntryGiveWay.Choose(true, true, -1f, 3f, G21Margin, 1f) == EntryGiveWay.Spot.DriveStart, "already in the band: at the drive start");
            Check(EntryGiveWay.Choose(false, false, 0f, 15f, G21Margin, vanBraking8) == EntryGiveWay.Spot.DriveStart, "no box: at the drive start");
            Check(EntryGiveWay.Choose(true, true, -6f, -0.5f, G21Margin, 3f) == EntryGiveWay.Spot.None, "past the drive start (or cannot stop): committed, never stopped across the oncoming lane by this rule");
            Check(EntryGiveWay.Choose(false, false, 0f, 0.1f, G21Margin, 0f) == EntryGiveWay.Spot.DriveStart, "standing at the drive start: judged there");
            Check(EntryGiveWay.Holds(true, EntryGiveWay.Spot.BoxLine, false) && !EntryGiveWay.Holds(true, EntryGiveWay.Spot.BoxLine, true)
                  && !EntryGiveWay.Holds(true, EntryGiveWay.Spot.None, false) && !EntryGiveWay.Holds(false, EntryGiveWay.Spot.DriveStart, false),
                "holds only for a busy gap at a spot it can still stop at, setting on");
            Check(!EntryGiveWay.Applies(true, 108) && EntryGiveWay.Applies(true, 100) && !EntryGiveWay.Applies(false, 100), "on-duty emergency vehicles: stock; setting off: stock");

            // --- clear distance: from the waiting spot, the whole vehicle past the crossed lane ---
            Check(Math.Abs(EntryGiveWay.ClearMetres(G21BoxLineBack + G21Margin, G21DriveToConflict, G21ConflictLength, 6.5f) - (4.9f + 5f + 6f + 6.5f)) < 1e-4f,
                "clear metres = to the drive start + along the way in + the crossed lane + own length");
            Check(EntryGiveWay.ClearMetres(-3f, -1f, 6f, 4f) == 10f, "negative parts count as 0");

            // --- the Alderwood scene, grid of oncoming vehicles (gap 2..150 m, speed 1..22 m/s): 0.4.10 vs 0.4.11 vs what happens ---
            G21Scene("van", G21Van, 6.5f, out int vanOldUnsafe, out int vanNewUnsafe, out int vanNewWaits, out int vanOldWaits);
            G21Scene("car", G21Car, 4.5f, out int carOldUnsafe, out int carNewUnsafe, out _, out _);
            G21Scene("lorry", G21Lorry, 18.5f, out int lorryOldUnsafe, out int lorryNewUnsafe, out _, out _);
            Check(vanOldUnsafe > 0, $"0.4.10 reproduces G21: the in-box decision (time to REACH the conflict) lets the van turn in front of oncoming traffic that reaches the lane before the van has cleared it ({vanOldUnsafe} scenes)");
            Check(lorryOldUnsafe > vanOldUnsafe && vanOldUnsafe >= carOldUnsafe, $"the longer and slower the turner, the bigger the 0.4.10 hole (car {carOldUnsafe}, van {vanOldUnsafe}, lorry {lorryOldUnsafe})");
            Check(vanNewUnsafe == 0 && carNewUnsafe == 0 && lorryNewUnsafe == 0, "0.4.11: never turns in when an oncoming vehicle at its speed reaches the crossed lane before the whole turner has cleared it (car, van, lorry)");
            Check(vanNewWaits > vanOldWaits, $"0.4.11 waits in more scenes than 0.4.10 ({vanNewWaits} vs {vanOldWaits}): the price of clearing with the whole length from before the box");
            {
                // The shot: westbound traffic about 25-30 m away at ~10 m/s (red / yellow cars in the shot). 0.4.10 went, 0.4.11 waits.
                var van = G21Van;
                float tReach = ArrivalTime.Compute(ref van, G21Margin, 0f, G21WaySpeed, 0f, G21Dt);
                var van2 = G21Van;
                float tClear = ArrivalTime.Compute(ref van2, EntryGiveWay.ClearMetres(G21BoxLineBack + G21Margin, G21DriveToConflict, G21ConflictLength, 6.5f), 0f, G21WaySpeed, 0f, G21Dt);
                bool oldBlocks = 10f * (ArrivalProjection.StockLookAhead + tReach) >= 28f;
                bool newBlocks = EntryGiveWay.VehicleBlocks(28f, false, 10f, G21Braking(10f), tClear, false);
                Check(!oldBlocks && newBlocks, $"oncoming car 28 m away at 10 m/s: 0.4.10 turned (reach {tReach:0.0} s), 0.4.11 waits before the box (clear {tClear:0.0} s)");
            }

            // --- standing vehicles: present rule (as exits) and the entrant exception (no facing-entrances deadlock) ---
            Check(EntryGiveWay.VehicleBlocks(4f, false, 0f, 0f, 3f, false), "a westbound vehicle standing 4 m before the crossed lane (present, as the exits' G7 rule) blocks the turn in");
            Check(!EntryGiveWay.VehicleBlocks(40f, false, 0f, 0f, 3f, false), "a queue that stopped 40 m back does not");
            Check(EntryGiveWay.VehicleBlocks(0f, true, 0f, 0f, 3f, true), "a vehicle standing ON the crossed lane always blocks, even one waiting to turn in");
            {
                // Two vans waiting to turn into facing entrances (A eastbound into No. 40, C westbound into the north side), each
                // standing 3-6 m before the other's crossing. 0.4.10's present rule alone: each waits for the other for ever.
                bool aWaitsC_present = EntryGiveWay.VehicleBlocks(3f, false, 0f, 0f, 3f, false);
                bool cWaitsA_present = EntryGiveWay.VehicleBlocks(6f, false, 0f, 0f, 3f, false);
                bool aWaitsC = EntryGiveWay.VehicleBlocks(3f, false, 0f, 0f, 3f, true);
                bool cWaitsA = EntryGiveWay.VehicleBlocks(6f, false, 0f, 0f, 3f, true);
                Check(aWaitsC_present && cWaitsA_present, "without the entrant exception two facing turners would wait for each other (loop)");
                Check(!aWaitsC && !cWaitsA, "with it (a standing vehicle that waits to turn in is not present) neither waits for the other: no loop; the game's own lane checks order the two turns");
                Check(EntryGiveWay.VehicleBlocks(3f, false, 1f, G21Braking(1f), 3f, true), "once the other turner moves (above the standing speed) it is judged as moving again");
            }
            // Random standing scenes: the gap rule only ever waits on road vehicles on the crossed lanes (never on the vehicles
            // behind it, never on an exit, never on another standing entrant), and road traffic never waits for it: no loop
            // through the entry hold alone. 5000 scenes: two facing entrants, random gaps 0.5..12 m, both standing.
            var rnd = new Random(2117);
            int loops = 0;
            for (int k = 0; k < 5000; k++)
            {
                float g1 = 0.5f + (float)rnd.NextDouble() * 11.5f, g2 = 0.5f + (float)rnd.NextDouble() * 11.5f;
                if (EntryGiveWay.VehicleBlocks(g1, false, 0f, 0f, 3f, true) && EntryGiveWay.VehicleBlocks(g2, false, 0f, 0f, 3f, true)) loops++;
            }
            Check(loops == 0, "5000 standing facing-entrant scenes: no mutual wait");

            // --- event kinds: the entry gap record has its own kind, keys fit an int ---
            Check(ExitGiveWay.EntryGapHeldKind < ExitGiveWay.EventKinds && ExitGiveWay.EntryGapHeldKind != ExitGiveWay.EnteringHeldKind
                  && (long)ExitGiveWay.EventKinds * ExitGiveWay.EventKindStride < int.MaxValue && ExitGiveWay.EventKey(1 << 25, ExitGiveWay.EventKinds - 1) > 0,
                "entry gap holds: own event kind, keys fit");
        }

        /// <summary>
        /// Grid over oncoming vehicles: gap 2..150 m (front to the crossed lane), speed 1..22 m/s, constant speed (what the turner
        /// must allow for). 0.4.10 decided in the box, standing one stop margin before the crossed lane, with the whole-path
        /// projection (stock y + speed x (2 s + own time to REACH the conflict), S2-10). 0.4.11 decides standing at the box line
        /// with the exit measure (Core ExitGiveWay.VehicleBlocks with the time to CLEAR the lane with the whole vehicle).
        /// Unsafe go = the oncoming vehicle reaches the crossed lane before the turner's rear has left it (from its spot).
        /// </summary>
        private static void G21Scene(string name, StockCarMotion kind, float ownLength, out int oldUnsafe, out int newUnsafe, out int newWaits, out int oldWaits)
        {
            oldUnsafe = newUnsafe = newWaits = oldWaits = 0;
            var m1 = kind; float oldReach = ArrivalTime.Compute(ref m1, G21Margin, 0f, G21WaySpeed, 0f, G21Dt);
            var m2 = kind; float oldClear = ArrivalTime.Compute(ref m2, EntryGiveWay.ClearMetres(G21Margin, 0f, G21ConflictLength, ownLength), 0f, G21WaySpeed, 0f, G21Dt);
            var m3 = kind; float newClear = ArrivalTime.Compute(ref m3, EntryGiveWay.ClearMetres(G21BoxLineBack + G21Margin, G21DriveToConflict, G21ConflictLength, ownLength), 0f, G21WaySpeed, 0f, G21Dt);
            for (int gap = 2; gap <= 150; gap++)
            {
                for (int v = 1; v <= 22; v++)
                {
                    bool oldBlocks = v * (ArrivalProjection.StockLookAhead + oldReach) >= gap;
                    bool newBlocks = EntryGiveWay.VehicleBlocks(gap, false, v, G21Braking(v), newClear, false);
                    if (oldBlocks) oldWaits++;
                    if (newBlocks) newWaits++;
                    if (!oldBlocks && gap / (float)v < oldClear) oldUnsafe++;
                    if (!newBlocks && gap / (float)v < newClear) newUnsafe++;
                }
            }
            Console.WriteLine($"  G21 {name}: own time 0.4.10 reach {oldReach:0.00} s / clear from the in-box spot {oldClear:0.00} s; 0.4.11 clear from the box line {newClear:0.00} s; unsafe goes 0.4.10 {oldUnsafe}, 0.4.11 {newUnsafe} (of 3278 scenes)");
        }

        private static void G21LogTests()
        {
            var lines = new List<string>();
            var book = new EntranceLogBook(lines.Add);
            const long road = 557050;
            long Pk(EntranceLog.Event ev, bool rm, float speed) => EntranceLog.Pack(ev, rm, speed, 0.29f);
            book.Tick(0, 0);
            // van 1: room in the yard, but held 35 s before the box for the oncoming gap (Go then HeldGap records each step)
            for (uint fr = 0; fr <= 2100; fr += 16)
            {
                book.Record(1, road, Pk(EntranceLog.Event.Go, true, 0f), fr, 0);
                book.Record(1, road, Pk(EntranceLog.Event.HeldGap, true, 0f), fr, 0);
            }
            Check(lines.Count(l => l.Contains("vehicle 1:0 held before the box (way in has room; waits for a gap")) == 1, "one sample for the gap hold episode");
            Check(lines.Count(l => l.Contains("vehicle 1:0 held before the box 30 s waiting for a gap")) == 1, "notable once after 30 s waiting for the gap");
            Check(lines.Count(l => l.Contains("vehicle 1:0 way in has room")) <= 1, "the room sample says only that the room test passed");
            book.Tick(61, 2400);
            string sum = lines.LastOrDefault() ?? "";
            Check(sum.Contains("1 held before the box for a gap in the traffic their turn crosses (1 over 30 s)"), "summary counts gap holds: " + sum);
            EntranceLog.Unpack(EntranceLog.Pack(EntranceLog.Event.HeldGap, true, 2.5f, 0.5f), out EntranceLog.Event e, out bool room, out float sp, out _);
            Check(e == EntranceLog.Event.HeldGap && room && Math.Abs(sp - 2.5f) < 0.05f, "HeldGap packs and unpacks");
        }

        private static void G21RepoTests()
        {
            string root = RepoRoot();
            string exit = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRExitGiveWay.cs"));
            int hold = exit.IndexOf("public float ExitDrivewayHold(", StringComparison.Ordinal);
            int gate = exit.IndexOf("if (!ExitGiveWay.IsDrivewayExitLane((uint)own.m_Flags))", hold, StringComparison.Ordinal);
            int call = exit.IndexOf("return EntryRoadHold(", hold, StringComparison.Ordinal);
            Check(hold > 0 && gate > hold && call > gate && call - gate < 200, "S2-29's call hands every non-exit lane to EntryRoadHold (no new copy block)");
            int eh = exit.IndexOf("private float EntryRoadHold(", StringComparison.Ordinal);
            Check(eh > 0, "EntryRoadHold exists");
            string body = eh > 0 ? exit.Substring(eh, exit.IndexOf("private static bool EntryConflict(", eh, StringComparison.Ordinal) - eh) : "";
            Check(body.Contains("EntryGiveWay.IsDrivewayEntryLane((uint)driveLane.m_Flags)") && body.Contains("PathLaneAfter(lane, out CarNavigationLane nextNav)"), "only a path whose next lane is a way in");
            Check(body.Contains("LaneBands(lane, edgeOwner.m_Owner, length, inverse, own.m_CarriagewayGroup, ref curves, out FixedList128Bytes<float2> merged)"), "the box line is the painted band on the lane (0.4.10 windows)");
            Check(body.Contains("EntryGiveWay.Choose(boxed, inBox, boxLineWait, driveStartWait, margin, braking)"), "spot: box line, else drive start, else committed");
            Check(body.Contains("EntryGiveWay.ClearMetres(toDriveStart, driveCurve.m_Length * s, driveCurve.m_Length * (e - s), ownLength)") && body.Contains("ConflictBusy(o.m_Other, otherCurve, oS, oE, delta, ref reads, ref budget)"),
                "crossed lanes judged with the exits' scan and the whole-length clear time");
            Check(body.Contains("m_EntryScan = true;") && body.Contains("m_EntryScan = false;") && body.Contains("NoteEntryGapHeld(self);"), "entrant scan flag set around the scan; the hold is recorded for other entrants");
            Check(body.Contains("reads.m_OwnLane = lane;"), "the turner's own road lane is never a feeding lane");
            Check(exit.Contains("(EnteringHeld(e) || (m_EntryScan && EntryGapHeld(e)))"), "an entrant's scan skips standing vehicles that wait to turn in; exits still wait for a gap-held entrant");
            string copy = File.ReadAllText(Path.Combine(root, "JunctionRules", "Vanilla", "JRCarLaneSpeedIterator.cs"));
            Check(!copy.Contains("EntryRoadHold") && copy.Contains("float jrExitHold = m_JR.ExitDrivewayHold(lane, bufferData, curveOffset.x"), "the copy is unchanged: S2-29 as in 0.4.10");
            string box = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRYellowBox.cs"));
            Check(box.Contains("0.4.11 (G21, owner): it waits BEFORE"), "the G9 room hook documents that the gap is decided before the box now");
            string proj = File.ReadAllText(Path.Combine(root, "JunctionRules", "JunctionRules.csproj"));
            Check(proj.Contains("<InformationalVersion>0.4.11-probe</InformationalVersion>"), "version 0.4.11-probe");
        }
    }
}
