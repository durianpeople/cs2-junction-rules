using System;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// JR-T-029 (owner rule 20 Nov 2026, 1.3.0): the box look-past sees 100 m; vehicles beyond sight are not read; nothing slow
    /// within sight (and no red line) -> the car goes; a stopped / slow vehicle seen -> the vehicles between are packed nose to
    /// tail behind it, skipping the no-stop zones (other yellow boxes, zebras but the soft zebra, other junction interiors).
    /// </summary>
    internal static partial class Program
    {
        private static void Sight100Tests()
        {
            RoomScan Scan(float sight) { RoomScan r = RoomScan.Begin(4.2f, 1f); r.FreeNow = true; r.LookPast = true; r.SightLeft = sight; return r; }
            // eight lorries (10 m, not large) moving away at 10 m/s: rears 2, 13, .. 79, occupied 88 m
            void Lorries(ref RoomScan r) { for (int i = 0; i < 8; i++) r.Add(2f + 11f * i, 10f, 20f, 20 + i, 1, 10f, 8f); }

            // ---- the 100 m cap
            Check(RoomScan.LookPastSightMetres == 100f && RoomScan.SightPast(0f) == 100f && RoomScan.SightPast(30f) == 70f,
                "JR-T-029: sight 100 m from the driver (was 50)");
            RoomScan at90 = Scan(RoomScan.SightPast(5f));   // 95 m of road past the window start seen
            Lorries(ref at90);
            at90.Add(90f, 4.5f, 0f, 7, 1, 0f, 0f);
            at90.FinishLookPast();
            RoomScan at110 = Scan(RoomScan.SightPast(0f));   // 100 m sight: a car standing 110 m on lies beyond it
            Lorries(ref at110);
            at110.Add(110f, 4.5f, 0f, 7, 1, 0f, 0f);
            at110.FinishLookPast();
            Check(!at90.HasRoom && at90.BlockerIndex == 7 && at90.BeyondSightSkipped == 0,
                "100 m cap: a car standing 90 m on is seen; the lorries packed behind it leave 2 m < 5.2 -> held");
            Check(at110.HasRoom && at110.AdmittedByLookPast && at110.BeyondSightSkipped == 1,
                "100 m cap: a car standing 110 m on is not read: nothing slow within sight -> go");

            // ---- nothing slow within sight -> go, even when the room runs past the sight edge (1.2.0: room_beyond_sight)
            RoomScan edge = Scan(50f);
            Lorries(ref edge);          // the packed room ends at 88 + 5.2 = 93.2 > 50
            edge.FinishLookPast();
            Check(edge.HasRoom && edge.AdmittedByLookPast && edge.Refusal == (byte)LookPastRefusal.None,
                "nothing slow within sight: go although the room it counts on runs past the sight edge");
            Check(edge.Done(50f) && !edge.Done(49f), "the scan is done at the sight edge with nothing slow seen");

            // ---- packing skips no-stop zones: one car moving away (rear 2), a car standing at rear 15
            // packed nose to tail: the mover stands at rear 9.5 -> room 9.5 >= 5.2: go; a zone at 8..12 pushes it back to rear
            // 3.5 -> 3.5 < 5.2: held, and the refusal names the zone
            RoomScan Pack(byte kind, float zs, float ze)
            {
                RoomScan r = Scan(100f);
                if (kind != 0) r.NoStopZone(zs, ze, kind);
                r.Add(2f, 4.5f, 10f, 3, 1, 10f, 8f);
                r.Add(15f, 4.5f, 0f, 7, 1, 0f, 0f);
                r.FinishLookPast();
                return r;
            }
            RoomScan plain = Pack(0, 0f, 0f);
            Check(plain.HasRoom && plain.AdmittedByLookPast, "packing: without a zone the mover packs behind the standing car: room 9.5 -> go");
            (byte kind, LookPastRefusal why, string name)[] kinds =
            {
                (RoomScan.ZoneZebra, LookPastRefusal.QueuePushedByZebra, "zebra"),
                (RoomScan.ZoneBox, LookPastRefusal.QueuePushedByBox, "yellow box"),
                (RoomScan.ZoneJunction, LookPastRefusal.QueuePushedByJunction, "junction interior"),
            };
            foreach (var k in kinds)
            {
                RoomScan z = Pack(k.kind, 8f, 12f);
                Check(!z.HasRoom && z.BlockerIndex == 7 && z.Refusal == (byte)k.why,
                    $"packing skips a {k.name}: the mover stands before it, the tail moves back to 3.5 < 5.2: go -> held ({WhyRules.RefusalId(k.why)})");
                RoomScan clear = Pack(k.kind, 15f, 15f + 20f);   // a zone beyond the standing car: no effect
                Check(clear.HasRoom, $"a {k.name} beyond the standing car does not push the queue");
            }
            Check(WhyRules.RefusalId(LookPastRefusal.QueuePushedByZebra) == "queue_pushed_back_by_zebra"
                && WhyRules.RefusalId(LookPastRefusal.QueuePushedByBox) == "queue_pushed_back_by_box"
                && WhyRules.RefusalId(LookPastRefusal.QueuePushedByJunction) == "queue_pushed_back_by_junction",
                "JRWhy names the new holds");
            // the room itself may lie in front of a zone: a zone before the room does not count against it
            RoomScan near = Pack(RoomScan.ZoneZebra, 0.5f, 1.5f);
            Check(near.HasRoom, "a zebra inside the car's own room (before the mover) does not move the mover");

            // two zones in a row push twice
            RoomScan two = Scan(100f);
            two.NoStopZone(20f, 24f, RoomScan.ZoneZebra);
            two.NoStopZone(12f, 19.5f, RoomScan.ZoneJunction);
            two.Add(2f, 4.5f, 10f, 3, 1, 10f, 8f);
            // without zones: rear 20.5; zebra 20..24 -> front 20, body 15.5..20 overlaps the junction 12..19.5 -> front 12, rear 7.5
            float twoRoom = two.PackedRoom(26f, out byte pk);
            two.Add(26f, 4.5f, 0f, 7, 1, 0f, 0f);
            two.FinishLookPast();
            Check(two.HasRoom && Math.Abs(twoRoom - 7.5f) < 1e-4f && pk == RoomScan.ZoneJunction,
                "packing: zones in a row push the queue back one after another (26 -> 7.5, last push the junction)");

            // PackedReach: without zones exactly Needed + occupied; with a zone in the way it grows
            RoomScan reach = Scan(100f);
            reach.Add(2f, 4.5f, 10f, 3, 1, 10f, 8f);
            Check(Math.Abs(reach.PackedReach() - (5.2f + 5.5f)) < 1e-4f, "PackedReach: no zones = Needed + occupied");
            RoomScan reachZ = Scan(100f);
            reachZ.NoStopZone(6f, 9f, RoomScan.ZoneBox);
            reachZ.Add(2f, 4.5f, 10f, 3, 1, 10f, 8f);
            Check(Math.Abs(reachZ.PackedReach() - (9f + 5.5f)) < 1e-4f && !reachZ.Done(14f) && reachZ.Done(14.5f),
                "PackedReach: the mover packs past a box in its way: the scan reads further (14.5)");

            // ---- the soft zebra is not a no-stop zone; zones are ignored with the look-past off (1.0.1 bit for bit)
            Check(ZebraSections.ZebraKind(ZebraSections.PedestrianCrosswalk, true, WholePathRule.SignalStop) == ZebraSections.SoftZebra
                && ZebraSections.ZebraKind(ZebraSections.PedestrianCrosswalk, false, 0) == ZebraSections.Zebra,
                "soft zebra (signalised, people on red) is told apart from a zebra");
            string wj = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            Check(wj.Contains("!= ZebraSections.Zebra) continue;   // not a zebra, or the soft zebra")
                && wj.Contains("scan.NoStopOnLane(zs, ze, startD, endD, len, read, RoomScan.ZoneZebra);"),
                "job: only true zebras become no-stop zones (the soft zebra is skipped)");
            Check(wj.Contains("edges.Length >= 3) { scan.NoStopZone(read, read + span, RoomScan.ZoneJunction); return; }")
                && wj.Contains("m_BoxJunctionData.HasComponent(owner.m_Owner)) { scan.NoStopZone(read, read + span, RoomScan.ZoneBox); return; }"),
                "job: a boxed junction's interior is a box zone, any other junction (3+ edges) a junction zone");
            RoomScan off = RoomScan.Begin(4.2f, 1f);
            off.FreeNow = true; off.SightLeft = 100f;
            off.NoStopZone(8f, 12f, RoomScan.ZoneZebra);
            off.Add(8f, 4.5f, 0f, 3, 1, 0f, 0f);   // a car standing on the zebra: 1.0.1 counts it where it is
            off.Add(15f, 4.5f, 0f, 7, 1, 0f, 0f);
            Check(off.Zones == 0 && off.HasRoom && off.BeyondSightSkipped == 0,
                "look-past off: no zones are kept, 1.0.1 room (8 >= 5.2, 15 - 5.5 = 9.5 >= 5.2)");
            RoomScan offFar = RoomScan.Begin(4.2f, 1f);
            offFar.FreeNow = true; offFar.SightLeft = 50f;
            offFar.Add(200f, 4.5f, 0f, 7, 1, 0f, 0f);
            Check(offFar.BeyondSightSkipped == 0 && offFar.Obstacles == 1 && offFar.HasRoom,
                "look-past off: no sight cap (a vehicle 200 m on is read as in 1.0.1)");
        }
    }
}
