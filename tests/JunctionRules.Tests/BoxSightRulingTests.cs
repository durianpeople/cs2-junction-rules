using System;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// JR-T-022d (owner rulings 19 Nov 2026, binding): (1) vehicles hidden beyond a large vehicle are NOT READ AT ALL; the view
    /// ends at the large vehicle, which itself counts with its own speed (less RoomScan.LookPastLargeMargin); (2) buildings
    /// block the view: a straight 2D line from the driver to each look-past target must cross no building footprint.
    /// </summary>
    internal static partial class Program
    {
        /// <summary>
        /// The tests' sight: the car's path past the window start as a polyline (path metres along it), the driver's eye and a
        /// list of building footprints (cx, cz, fx, fz, minX, maxX, minZ, maxZ each), tested with the same Core geometry as the
        /// job (SightLine.SightBlocked: a footprint containing the eye does not block). Counts its queries (the job queries the search tree once per call).
        /// </summary>
        private struct PolySight : ISightTest
        {
            public float Ex, Ez;
            public float[] Px, Pz;
            public float[] Boxes;
            public int Queries;

            public bool Seen(float d)
            {
                Queries++;
                float x = Px[Px.Length - 1], z = Pz[Pz.Length - 1], at = 0f;
                bool placed = false;
                for (int i = 1; i < Px.Length; i++)
                {
                    float len = (float)Math.Sqrt((Px[i] - Px[i - 1]) * (Px[i] - Px[i - 1]) + (Pz[i] - Pz[i - 1]) * (Pz[i] - Pz[i - 1]));
                    if (d <= at + len)
                    {
                        float u = len > 0f ? (d - at) / len : 0f;
                        x = Px[i - 1] + u * (Px[i] - Px[i - 1]);
                        z = Pz[i - 1] + u * (Pz[i] - Pz[i - 1]);
                        placed = true;
                        break;
                    }
                    at += len;
                }
                if (!placed) return false;   // beyond the path read: not seen (as the job)
                for (int b = 0; Boxes != null && b + 7 < Boxes.Length; b += 8)
                    if (SightLine.SightBlocked(Ex, Ez, x, z, Boxes[b], Boxes[b + 1], Boxes[b + 2], Boxes[b + 3], Boxes[b + 4], Boxes[b + 5], Boxes[b + 6], Boxes[b + 7]))
                        return false;
                return true;
            }
        }

        private static void BoxSightOwnerRulingTests()
        {
            // ======== ruling 1: hidden beyond a large vehicle = not read at all; the view ends at it
            RoomScan Truck(bool lookPast, float need, bool large, float speed, float travel, float beyondRear, float beyondSpeed, out RoomScan r)
            {
                r = RoomScan.Begin(need - 1f, 1f);
                r.FreeNow = true; r.LookPast = lookPast; r.SightLeft = 50f;
                r.Add(1f, 12f, travel + speed, 3, 1, speed, travel, large);   // a 12 m vehicle, rear 1 m past the box (front 13 m)
                if (beyondRear >= 0f) r.Add(beyondRear, 4.2f, beyondSpeed, 4, 1, beyondSpeed, beyondSpeed);
                r.FinishLookPast();
                return r;
            }
            // (a) fast lorry; X (an artic, needs 17.5 m) would need road past the lorry's front (13 m), where only the hidden car
            // beyond gives room (its rear 40 m: free now): unseen -> no credit, X waits naming the lorry; off: held too
            Truck(true, 17.5f, true, 12f, 30f, 40f, 8f, out RoomScan hiddenRoom);
            Truck(false, 17.5f, true, 12f, 30f, 40f, 8f, out RoomScan hiddenRoomOff);
            Truck(true, 17.5f, false, 12f, 30f, 40f, 8f, out RoomScan seenRoom);   // the same 12 m vehicle if it did not block the view
            Check(!hiddenRoom.HasRoom && hiddenRoom.BlockerIndex == 3 && hiddenRoom.HiddenSeen == 1 && !hiddenRoomOff.HasRoom && seenRoom.HasRoom && seenRoom.AdmittedByLookPast,
                "ruling 1: fast truck, the room X needs lies past the truck's front (only behind the hidden car): not seen -> no credit, X waits naming the truck; a vehicle that does not block the view: seen, credited");
            // (b) fast lorry whose own credit suffices (X a car, 5.2 m; pred 1 + 12 - 5 = 8): X goes, a car standing hidden
            // right beyond the lorry is not read (neither for nor against)
            Truck(true, 5.2f, true, 12f, 12f, 13.5f, 0f, out RoomScan ownCredit);
            Truck(false, 5.2f, true, 12f, 12f, 13.5f, 0f, out RoomScan ownCreditOff);
            Check(ownCredit.HasRoom && ownCredit.AdmittedByLookPast && ownCredit.HiddenSeen == 1 && !ownCreditOff.HasRoom,
                "ruling 1: fast truck whose own credit suffices -> X goes; the standing car hidden beyond it is not read (off: held)");
            // (c) slow lorry (2 m/s) -> no credit, X waits naming it
            Truck(true, 5.2f, true, 2f, 12f, -1f, 0f, out RoomScan slow);
            Check(!slow.HasRoom && slow.BlockerIndex == 3, "ruling 1: slow truck -> X waits, naming the truck");
            // (d) the general caution: a large vehicle's credit is its guaranteed travel less LookPastLargeMargin (5 m): travel 6 m
            // (pred 1 + 1 = 2 < 5.2) gives none; the same travel on an ordinary vehicle does (pred 7)
            Truck(true, 5.2f, true, 12f, 6f, -1f, 0f, out RoomScan careful);
            Truck(true, 5.2f, false, 12f, 6f, -1f, 0f, out RoomScan ordinary);
            Check(RoomScan.LookPastLargeMargin == 5f && !careful.HasRoom && careful.BlockerIndex == 3 && ordinary.HasRoom,
                "ruling 1 caution: a view-blocking vehicle is credited with its guaranteed travel less 5 m (no hidden information used)");
            // (e) never negative: a large vehicle with travel under the margin still counts its room free now (no new refusal)
            RoomScan free = RoomScan.Begin(4.2f, 1f);
            free.FreeNow = true; free.LookPast = true; free.SightLeft = 50f;
            free.Add(8f, 12f, 3f, 3, 1, 4f, 2f, true);
            free.ExtendPast(1f);   // needed 6.2: the lorry's room free now (8) still suffices
            free.FinishLookPast();
            Check(free.HasRoom && !free.AdmittedByLookPast, "ruling 1 caution: the margin never takes away room that is free now");
            // (f) JR-T-022f (owner call B): a large vehicle queued ahead of X before / inside the box switches the look-past off for
            // this decision: the 1.0.1 free-now verdict (X goes when the room is free now, as with the setting off)
            RoomScan q = RoomScan.Begin(4.2f, 1f);
            q.FreeNow = true; q.LookPast = true; q.SightLeft = 50f;
            q.AddQueued(12f, 10f, 5, 1, true);
            q.Add(40f, 4.2f, 10f, 7, 1, 8f, 6f);
            q.FinishLookPast();
            RoomScan qOff = RoomScan.Begin(4.2f, 1f);
            qOff.FreeNow = true;
            qOff.AddQueued(12f, 10f, 5, 1, true);
            qOff.Add(40f, 4.2f, 10f, 7, 1, 8f, 6f);
            Check(q.HasRoom && q.QueuedLargeFreeNow && !q.AdmittedByLookPast && q.HiddenSeen == 0 && qOff.HasRoom && !qOff.QueuedLargeFreeNow,
                "JR-T-022f: a lorry queued ahead in the box -> look-past off: room free now past the box -> X goes (as off; no hold of its own)");
            // ... and room only on a car's travel past the box is NOT credited (free-now verdict), naming that car, as off
            RoomScan q2 = RoomScan.Begin(4.2f, 1f), q2Off = RoomScan.Begin(4.2f, 1f), q2Van = RoomScan.Begin(4.2f, 1f);
            q2.FreeNow = q2Off.FreeNow = q2Van.FreeNow = true; q2.LookPast = q2Van.LookPast = true; q2.SightLeft = q2Van.SightLeft = 50f;
            q2.AddQueued(12f, 10f, 5, 1, true); q2.Add(15f, 4.2f, 10f, 7, 1, 8f, 6f); q2.FinishLookPast();
            q2Off.AddQueued(12f, 10f, 5, 1, true); q2Off.Add(15f, 4.2f, 10f, 7, 1, 8f, 6f);
            q2Van.AddQueued(12f, 10f, 5, 1, false); q2Van.Add(15f, 4.2f, 10f, 7, 1, 8f, 6f); q2Van.FinishLookPast();
            Check(!q2.HasRoom && q2.BlockerIndex == 7 && q2.BlockerIndex == q2Off.BlockerIndex && !q2Off.HasRoom && q2Van.HasRoom && q2Van.AdmittedByLookPast,
                "JR-T-022f: lorry queued ahead + a car past the box with free-now room short: held at that car (as off); behind a van it is credited");
            // a large vehicle past the box after the switch is not a hider any more: everything is read as off
            RoomScan q3 = RoomScan.Begin(4.2f, 1f), q3Off = RoomScan.Begin(4.2f, 1f);
            q3.FreeNow = q3Off.FreeNow = true; q3.LookPast = true; q3.SightLeft = 50f;
            q3.AddQueued(4.2f, 10f, 4, 1, false); q3.AddQueued(12f, 10f, 5, 1, true); q3.Add(20f, 12f, 20f, 6, 1, 10f, 12f, true); q3.Add(34f, 4.2f, 0f, 7, 1, 0f, 0f); q3.FinishLookPast();
            q3Off.AddQueued(4.2f); q3Off.AddQueued(12f); q3Off.Add(20f, 12f, 20f, 6, 1); q3Off.Add(34f, 4.2f, 0f, 7, 1);
            Check(q3.HasRoom == q3Off.HasRoom && q3.BlockerIndex == q3Off.BlockerIndex && q3.Reach == q3Off.Reach && q3.Obstacles == q3Off.Obstacles && q3.HiddenSeen == 0,
                "JR-T-022f: after the switch the scan is the 1.0.1 scan call for call (a lorry past the box hides nothing, the standing car beyond it is read)");
            // credit given before the switch (a vehicle read before the queued lorry) is withdrawn: free-now verdict naming it
            RoomScan q4 = RoomScan.Begin(4.2f, 1f);
            q4.FreeNow = true; q4.LookPast = true; q4.SightLeft = 50f;
            q4.Add(2f, 4.2f, 10f, 3, 1, 8f, 6f);   // now 2, pred 8: credited
            q4.AddQueued(12f, 10f, 5, 1, true);
            q4.FinishLookPast();
            Check(!q4.HasRoom && q4.BlockerIndex == 3 && !q4.AdmittedByLookPast, "JR-T-022f: look-past credit given before the switch is withdrawn (free-now verdict, naming that vehicle)");

            // ======== ruling 2: buildings block the straight line
            // geometry: segment vs box (axis-aligned box [-1, 1] x [-1, 1] at the origin)
            bool Hit(float ax, float az, float bx, float bz, float fx = 0f, float fz = 1f, float cx = 0f, float cz = 0f, float minX = -1f, float maxX = 1f, float minZ = -1f, float maxZ = 1f)
                => SightLine.SegmentHitsBox(ax, az, bx, bz, cx, cz, fx, fz, minX, maxX, minZ, maxZ);
            Check(Hit(-5f, 0f, 5f, 0f) && Hit(0f, -5f, 0f, 5f) && !Hit(-5f, 2f, 5f, 2f) && Hit(-5f, 1f, 5f, 1f) && !Hit(-5f, 0f, -2f, 0f) && Hit(-0.5f, 0f, 0.5f, 0f)
                && Hit(0f, 0f, 0f, 0f) && !Hit(3f, 3f, 3f, 3f) && Hit(-3f, -2.5f, 3f, 2.5f) && !Hit(-3f, 0f, 0f, 3f),
                "sight geometry: a segment through / along the edge / inside the box hits; one passing by, ending short or a point outside does not");
            float r = (float)Math.Sqrt(0.5);
            Check(Hit(-5f, 1.2f, 5f, 1.2f, r, r) && !Hit(0.9f, 0.9f, 3f, 3f, r, r) && Hit(0.9f, 0.9f, 3f, 3f) && !Hit(-5f, 1.2f, 5f, 1.2f),
                "sight geometry: the footprint turns with the building (45 degrees: its corner reaches 1.41 m, its side does not)");
            Check(Hit(0f, 0f, 20f, 0f, 0f, 1f, 10f, 0f, 0f, 4f, -1f, 1f) && !Hit(0f, 0f, 9f, 0f, 0f, 1f, 10f, 0f, 0f, 4f, -1f, 1f) && !Hit(0f, 0f, 20f, 0f, 0f, 0f, 10f, 3f, -1f, 1f, -1f, 1f),
                "sight geometry: off-centre bounds are placed by the building's position; a zero rotation axis is taken as unrotated");

            // JR-T-022e (review F2): a footprint that contains the eye does not block (the lot box a car leaves its building through)
            bool Blk(float ax, float az, float bx, float bz, float fx = 0f, float fz = 1f, float cx = 0f, float cz = 0f, float minX = -1f, float maxX = 1f, float minZ = -1f, float maxZ = 1f)
                => SightLine.SightBlocked(ax, az, bx, bz, cx, cz, fx, fz, minX, maxX, minZ, maxZ);
            Check(!Blk(0f, 0f, 5f, 0f) && !Blk(0.9f, 0.9f, 5f, 5f) && !Blk(1f, 0f, 5f, 0f) && !Blk(0f, 0f, 0f, 0f)
                && Blk(-5f, 0f, 5f, 0f) && Blk(-5f, 0f, 0f, 0f) && !Blk(-5f, 2f, 5f, 2f),
                "F2: the eye inside (or on the edge of) a footprint: that footprint does not block; from outside it blocks as before, also when the target is inside it");
            Check(SightLine.PointInBox(0.9f, 0.9f, 0f, 0f, 0f, 1f, -1f, 1f, -1f, 1f) && !SightLine.PointInBox(0.9f, 0.9f, 0f, 0f, r, r, -1f, 1f, -1f, 1f)
                && SightLine.PointInBox(1.3f, 0f, 0f, 0f, r, r, -1f, 1f, -1f, 1f) && !SightLine.PointInBox(1.3f, 0f, 0f, 0f, 0f, 1f, -1f, 1f, -1f, 1f)
                && SightLine.PointInBox(1.3f, 0f, 0f, 0f, 0f, 0f, -1f, 1.4f, -1f, 1f)
                && SightLine.PointInBox(10.5f, 0.5f, 10f, 0f, 0f, 1f, 0f, 4f, -1f, 1f) && !SightLine.PointInBox(9.5f, 0f, 10f, 0f, 0f, 1f, 0f, 4f, -1f, 1f),
                "F2: point in footprint follows the rotation and the off-centre bounds");
            // the frame of PointInBox matches SegmentHitsBox: a point is inside iff the zero-length segment at it hits, at any angle
            var rng = new Random(2219);
            int frameMismatch = 0, aabbMismatch = 0, aabbTrue = 0, eyeInBlocked = 0;
            for (int i = 0; i < 200000; i++)
            {
                float px = (float)(rng.NextDouble() * 40 - 20), pz = (float)(rng.NextDouble() * 40 - 20);
                float qx = (float)(rng.NextDouble() * 40 - 20), qz = (float)(rng.NextDouble() * 40 - 20);
                double ang = rng.NextDouble() * 2 * Math.PI;
                float fx = (float)Math.Sin(ang), fz = (float)Math.Cos(ang), cx = (float)(rng.NextDouble() * 10 - 5), cz = (float)(rng.NextDouble() * 10 - 5);
                float x0 = (float)(rng.NextDouble() * -8), x1 = (float)(rng.NextDouble() * 8), z0 = (float)(rng.NextDouble() * -8), z1 = (float)(rng.NextDouble() * 8);
                bool inside = SightLine.PointInBox(px, pz, cx, cz, fx, fz, x0, x1, z0, z1);
                if (inside != SightLine.SegmentHitsBox(px, pz, px, pz, cx, cz, fx, fz, x0, x1, z0, z1)) frameMismatch++;
                if (inside && SightLine.SightBlocked(px, pz, qx, qz, cx, cz, fx, fz, x0, x1, z0, z1)) eyeInBlocked++;
                // F5: the axis-aligned segment test = the box test unrotated (and placed at the origin)
                bool a = SightLine.SegmentHitsAabb(px, pz, qx, qz, cx + x0, cx + x1, cz + z0, cz + z1);
                if (a) aabbTrue++;
                if (a != SightLine.SegmentHitsBox(px, pz, qx, qz, cx, cz, 0f, 1f, x0, x1, z0, z1)) aabbMismatch++;
            }
            Check(frameMismatch == 0 && eyeInBlocked == 0, $"F2: PointInBox uses SegmentHitsBox's frame (200000 random rotated boxes: {frameMismatch} mismatches); an eye inside never blocks ({eyeInBlocked})");
            Check(aabbMismatch <= 2 && aabbTrue > 20000, $"F5: SegmentHitsAabb = SegmentHitsBox unrotated (200000 cases, {aabbTrue} hits, {aabbMismatch} float-edge mismatches)");
            // F5: a diagonal 50 m line skips a box in the corner of its bounding box (the old prune visited it), keeps one on it
            Check(!SightLine.SegmentHitsAabb(0f, 0f, 35f, 35f, 25f, 34f, 0f, 9f) && SightLine.SegmentHitsAabb(0f, 0f, 35f, 35f, 15f, 20f, 16f, 22f)
                && SightLine.SegmentHitsAabb(0f, 0f, 35f, 35f, 35f, 40f, 35f, 40f) && SightLine.SegmentHitsAabb(3f, 3f, 3f, 3f, 0f, 9f, 0f, 9f)
                && !SightLine.SegmentHitsAabb(0f, 0f, 0f, 30f, 0.01f, 5f, -5f, 40f),
                "F5: the tree prune follows the line itself: a box beside a diagonal line in its bounding box is skipped; on the line, touching its end, holding a point: visited");

            // the scan on a turn: eye (0, 0); the box's far end (window start) at (0, 30); the path runs on to the corner (0, 40)
            // then east along z = 40. X an artic (needs 17.5) behind a car queued in the box (occupied 5.2); a car past the box,
            // rear 12 m (2 m round the corner) moving away: now 6.8, pred 26.8 -> credited; the room relied on ends 22.7 m
            // past the window start, (12.7, 40), round the corner
            RoomScan TurnScan()
            {
                RoomScan t = RoomScan.Begin(16.5f, 1f);
                t.FreeNow = true; t.LookPast = true; t.SightLeft = 50f;
                t.AddQueued(4.2f, 8f, 3, 1);
                t.Add(12f, 4.2f, 30f, 7, 1, 8f, 20f);
                t.FinishLookPast();
                return t;
            }
            PolySight Path(float[] boxes) => new PolySight { Ex = 0f, Ez = 0f, Px = new[] { 0f, 0f, 200f }, Pz = new[] { 30f, 40f, 40f }, Boxes = boxes };
            RoomScan open = TurnScan();
            PolySight openSight = Path(null);
            open.FinishSight(ref openSight);
            // a building on the inside of the corner: footprint x 4..14, z 25..35
            RoomScan corner = TurnScan();
            PolySight cornerSight = Path(new[] { 9f, 30f, 0f, 1f, -5f, 5f, -5f, 5f });
            corner.FinishSight(ref cornerSight);
            // a building on the outside of the corner (x -20..-8, z 42..60) and one far down the road (x 60..80): no effect
            RoomScan outside = TurnScan();
            PolySight outsideSight = Path(new[] { -14f, 51f, 0f, 1f, -6f, 6f, -9f, 9f, 70f, 46f, 0f, 1f, -10f, 10f, -4f, 4f });
            outside.FinishSight(ref outsideSight);
            Check(open.HasRoom && open.AdmittedByLookPast && !open.BuildingCut && openSight.Queries == 2,
                "ruling 2: an open corner: the credited car and the end of the room are seen in a straight line -> X goes (2 sight queries)");
            Check(!corner.HasRoom && corner.BuildingCut && corner.BlockerIndex == 7,
                "ruling 2: a corner building on the turn hides the end of the room round the corner -> no look-past credit, X held as by the free-now rule");
            Check(outside.HasRoom && !outside.BuildingCut, "ruling 2: buildings off the line (outside the corner, further down the road) do not block");
            // a building hiding the credited car itself (it stands between the eye and the car, beside the path)
            RoomScan hidCar = TurnScan();
            PolySight hidCarSight = Path(new[] { 1.9f, 38f, 0f, 1f, -0.3f, 0.3f, -0.5f, 0.5f });   // a kiosk on the line to (2, 40)
            hidCar.FinishSight(ref hidCarSight);
            Check(!hidCar.HasRoom && hidCar.BlockerIndex == 7 && hidCarSight.Queries == 1, "ruling 2: a building between the driver and the credited car: not seen -> no credit (first target fails, one query)");
            // only when the look-past is about to give credit: free-now room or a refused scan makes no query
            RoomScan freeNow = RoomScan.Begin(4.2f, 1f);
            freeNow.FreeNow = true; freeNow.LookPast = true; freeNow.SightLeft = 50f;
            freeNow.Add(20f, 4.2f, 10f, 7, 1, 8f, 6f);
            freeNow.FinishLookPast();
            PolySight fq = Path(new[] { 9f, 30f, 0f, 1f, -5f, 5f, -5f, 5f });
            freeNow.FinishSight(ref fq);
            RoomScan offScan = TurnScan();
            offScan.LookPast = false;
            PolySight oq = Path(null);
            offScan.FinishSight(ref oq);
            Check(freeNow.HasRoom && fq.Queries == 0 && oq.Queries == 0, "ruling 2: no building query unless the look-past is about to give the room (free-now room, setting off: none)");
            // the path read ends before a target: not seen
            RoomScan shortPath = TurnScan();
            PolySight sp = new PolySight { Ex = 0f, Ez = 0f, Px = new[] { 0f, 0f, 10f }, Pz = new[] { 30f, 40f, 40f } };   // 20 m read
            shortPath.FinishSight(ref sp);
            Check(!shortPath.HasRoom, "ruling 2: a target past the road read is not seen");
            // F2: the driver stands in a lot box (leaving its building: a lot x -10..10, z -12..2 around the eye): the look-past
            // still works; a building on the line beyond it still blocks; standing in one lot does not exempt another
            RoomScan inLot = TurnScan();
            PolySight inLotSight = Path(new[] { 0f, -5f, 0f, 1f, -10f, 10f, -7f, 7f });
            inLot.FinishSight(ref inLotSight);
            RoomScan inLotCorner = TurnScan();
            PolySight inLotCornerSight = Path(new[] { 0f, -5f, 0f, 1f, -10f, 10f, -7f, 7f, 9f, 30f, 0f, 1f, -5f, 5f, -5f, 5f });
            inLotCorner.FinishSight(ref inLotCornerSight);
            RoomScan nextToLot = TurnScan();
            PolySight nextToLotSight = Path(new[] { 0f, -5f, 0f, 1f, -10f, 10f, -7f, 7f, 0f, 20f, 0f, 1f, -2f, 2f, -1f, 1f });
            nextToLot.FinishSight(ref nextToLotSight);
            // without the rule the same lot would withdraw every credit (the eye is inside it)
            bool lotWouldBlock = SightLine.SegmentHitsBox(0f, 0f, 2f, 40f, 0f, -5f, 0f, 1f, -10f, 10f, -7f, 7f);
            Check(lotWouldBlock && inLot.HasRoom && inLot.AdmittedByLookPast && !inLot.BuildingCut && inLotSight.Queries == 2
                && !inLotCorner.HasRoom && inLotCorner.BuildingCut && !nextToLot.HasRoom && nextToLot.BuildingCut,
                "F2: a car standing in a lot box (building exit, car park) still looks past: its own lot does not block, a corner building or another building on the line still does");

            // ======== the job wiring (source)
            string root = RepoRoot();
            string js = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "JunctionRules", "Stage2", "JRSight.cs"));
            string wj = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            string nav = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "JunctionRules", "Vanilla", "JRCarNavigationSystem.cs"));
            Check(js.Contains("Tree->Iterate(ref it);") && js.Contains("!Buildings->HasComponent(item)") && js.Contains("SightLine.SightBlocked(A.x, A.y, B.x, B.y, tr.m_Position.x, tr.m_Position.z, f.x, f.z,")
                && js.Contains("g.m_Bounds.min.x, g.m_Bounds.max.x, g.m_Bounds.min.z, g.m_Bounds.max.z") && js.Contains("public bool Intersect(QuadTreeBoundsXZ bounds) => !Hit && OnLine(bounds);")
                && js.Contains("if (Hit || !OnLine(bounds) || !Buildings->HasComponent(item)) return;")
                && js.Contains("&& SightLine.SegmentHitsAabb(A.x, A.y, B.x, B.y, b.min.x - Pad, b.max.x + Pad, b.min.z - Pad, b.max.z + Pad);")
                && js.Contains("if (!hasEye || !m_SightLookups || !scan.AdmittedByLookPast) return;") && js.Contains("if (!m_BoxLookPast) return;"),
                "job: buildings from the static object search tree near the line only, Building entities only, footprint = prefab bounds placed by the Transform; queried only when the look-past is about to give room");
            Check(wj.Contains("BuildingSightCheck(ref scan, hasEye, eye, ref sightSpans, ref prefabRefs, ref geometries);") && wj.Contains("m_SightEye = hasEye && m_SightLookups;")
                && wj.Contains("NoteSpan(ref sightSpans, boxCurve,") && wj.Contains("NoteSpan(ref sightSpans, navCurve,"),
                "job: the building test runs after FinishLookPast on the lane spans read");
            Check(js.Contains("if (d < 0f) d = 0f;"), "job: a credited vehicle straddling the window start (rear before it) is placed at the start of the first span, not 'not seen'");
            Check(nav.Contains("carLaneSpeedIterator.m_JR.SetSight(ref m_StaticObjectSearchTree, ref m_TransformData, ref m_JRExitBuildings);")
                && nav.Contains("jobData.m_StaticObjectSearchTree = m_ObjectSearchSystem.GetStaticSearchTree(readOnly: true, out var dependencies3);")
                && nav.Contains("m_ObjectSearchSystem.AddStaticSearchTreeReader(jobHandle);"),
                "job: the stock static search tree (read-only, the stock system's reader dependency) is handed to the iterator in block S2-26");
        }
    }
}
