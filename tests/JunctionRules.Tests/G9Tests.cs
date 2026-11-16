using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// JR-T-014c (G9 + G10, owner 17 Nov 2026): yellow boxes at building ENTRANCES (derived, never saved), vehicles turning
    /// in do not enter the box without room on their way in, no box at a lot-front fallback, hover arrow direction.
    /// Live fixture (read-only, 17 Nov 2026): Alderwood Street building 688898 exit Driveway@(20.6, 22.2) with entry-only
    /// node 1213342; clinic 1178956 / police 999975 on road 312580: two-way "Invisible Road Path - 2xTwoway" sub-nets (real
    /// accesses with no drawn driveway: the owner's G10 shot).
    /// </summary>
    internal static partial class Program
    {
        private const float G9Car = 4.6f;

        private static void G9EntranceDrivingTests()
        {
            // Role: a path that ends inside the band is Entering; with a next lane off the road it is judged (G9)
            BoxDriving.EntranceRole role = BoxDriving.Role(0.0f, 0.5f, 0.45f, 0.55f, out _);
            Check(role == BoxDriving.EntranceRole.Entering, "path ending inside the band = Entering");
            Check(BoxDriving.EnteringApplies(true, false), "Entering with a next lane off the road (edge connection lane) is an entry");
            Check(!BoxDriving.EnteringApplies(true, true), "lane change inside the band (next lane an EdgeLane of the same road) is not an entry");
            Check(!BoxDriving.EnteringApplies(false, false), "path ending on the road lane itself (no next lane) is not an entry");
            Check(BoxDriving.EnteringEntry(0.1f, 0.45f) == 0.45f && BoxDriving.EnteringEntry(0.5f, 0.45f) == 0.5f, "entry = band start, or the range start when already inside the band");

            BoxDriving.Approach before = BoxDriving.Classify(false, 12f, 0.9f, 8f);
            Check(before == BoxDriving.Approach.Check, "before the band and able to stop: the room test decides");

            // entrance full: a standing car 2 m into the drive, nothing moves
            var full = RoomScan.Begin(G9Car, 1f);
            full.Add(2f, G9Car, 0f);
            Check(!full.HasRoom && BoxDriving.EnteringHolds(before, full.HasRoom), "entrance full (standing car 2 m in): held before the box");
            // entrance moving: same car moving with 6 m braking distance credit
            var moving = RoomScan.Begin(G9Car, 1f);
            moving.Add(2f, G9Car, 6f);
            Check(moving.HasRoom && !BoxDriving.EnteringHolds(before, moving.HasRoom), "entrance moving (braking credit): enters");
            // empty drive / path ends inside the building before the room is read: free
            var empty = RoomScan.Begin(G9Car, 1f);
            Check(empty.HasRoom, "empty way in (or path ending inside the building): enters");
            // cannot stop any more / already inside: carries on
            Check(!BoxDriving.EnteringHolds(BoxDriving.Classify(false, 2f, 0.9f, 8f), false), "cannot stop before the band: carries on");
            Check(!BoxDriving.EnteringHolds(BoxDriving.Classify(true, 12f, 0.9f, 8f), false), "already in the band: carries on");

            // truck length (trailers): tractor 6 m + semi-trailer 12 m. 9 m free in the drive: a car fits, the truck does not
            var truck = CombinedLength.Of(-3f, 3f);
            truck.AddTrailer(-2.5f, 6f, -6f, 6f);
            float truckLen = truck.Length;
            Check(truckLen > 14f, $"tractor + trailer length {truckLen:0.0} m counts whole");
            var car9 = RoomScan.Begin(G9Car, 1f); car9.Add(9f, G9Car, 0f);
            var truck9 = RoomScan.Begin(truckLen, 1f); truck9.Add(9f, G9Car, 0f);
            Check(car9.HasRoom && !truck9.HasRoom, "9 m free in the drive: car enters, articulated truck waits on the road");

            // counter + timing line
            Check(Stage2Counters.EntranceHeldEntering < Stage2Counters.Count && Stage2Counters.EntranceHeldEntering > Stage2Counters.ExitRoadStart, "G9 counter in its own block after the 0.4.7 counters");
            var dl = new long[Stage2Counters.Count];
            dl[Stage2Counters.EntranceHeldEntering] = 4;
            string on = Stage2Counters.Format(2, dl, 2, 2, 2);
            Check(on.Contains("leaving driveway 0; entrance box entries held (entrance full) 4"), "timing log: 'entrance box entries held (entrance full) N' after the band holds: " + on);
        }

        private static void G9EntranceBoxTests()
        {
            // entry-only node = an entrance (0.4.3-0.4.7 skipped it)
            Check(AccessRules.Way(false, true) == AccessWay.In && AccessRules.Way(true, false) == AccessWay.Out && AccessRules.Way(true, true) == AccessWay.Both, "sub-net node way from its car lanes");
            // Alderwood Street 688898 (frame: illustrative): exit drive at 164.6 m, entry-only node 12 m further on
            var exit = new ExitCandidate { Kind = ExitKind.Driveway, Key = new ExitKey(ExitKind.Driveway, 20.6, 22.2), RoadId = 1174143, AlongM = 164.6 };
            var entry = new ExitCandidate { Kind = ExitKind.Entrance, Key = new ExitKey(ExitKind.Entrance, 8.6, 22.2), RoadId = 1174143, AlongM = 152.6 };
            var boxes = AccessRules.EntranceBoxes(new List<ExitCandidate> { entry }, new List<ExitCandidate> { exit });
            Check(boxes.Count == 1 && boxes[0] == 0, "entry-only node now gets a box of its own");
            var same = entry; same.AlongM = 163.0;
            Check(AccessRules.EntranceBoxes(new List<ExitCandidate> { same }, new List<ExitCandidate> { exit }).Count == 0, "an entrance at the boxed exit's place adds no second box");
            Check(AccessRules.EntranceBoxes(new List<ExitCandidate> { entry, entry }, null).Count == 1, "two entrance rows at one place: one box");
            Check(AccessRules.EntrancesBoxed(1, false) && AccessRules.EntrancesBoxed(0, true) && !AccessRules.EntrancesBoxed(0, false), "entrances boxed whenever the building has a box (any key, or a legacy box)");
            // Normalise keeps entrances (same rules as exits)
            var norm = ExitRules.Normalise(new[] { entry, exit });
            Check(norm.Count == 2 && norm.Exists(c => c.Kind == ExitKind.Entrance), "Normalise keeps an entrance next to an exit");
            // matching a derived entrance box again
            Check(ExitRules.Match(entry.Key, new List<ExitCandidate> { entry }) == 0, "derived entrance key finds its entrance again");

            // entrance + exit boxes merged: two 8 m bands overlapping on one road = one drawn box (#17 BoxMerge)
            var prm = BoxMergeParams.ForMargin(0.5);
            var exitBox = Rect(0, -4, 8, 4);
            var entryBox = Rect(5, -4, 13, 4);
            var g = BoxMerge.Merge(new[]
            {
                new BoxMergeInput(BoxMergeInput.EntityKey(688898, 1), BoxMergeInput.ExitSub(exit.Key), BoxMergeKind.Exit, exitBox, 77, MedianSplit.AllCarriageways),
                new BoxMergeInput(BoxMergeInput.EntityKey(688898, 1), BoxMergeInput.ExitSub(entry.Key), BoxMergeKind.Exit, entryBox, 77, MedianSplit.AllCarriageways),
            }, prm);
            Check(g.Count == 1 && g[0].Merged && g[0].Members.Count == 2, "entrance box overlapping the building's exit box: drawn as one box");
            Check(BoxMergeInput.ExitSub(exit.Key) != BoxMergeInput.ExitSub(entry.Key), "entrance and exit boxes keep distinct ids");
            // driving: overlapping bands on one lane are one band
            Check(ExitBands.Touch(0.40f, 0.48f, 0.45f, 0.53f, 0.001f), "entrance and exit bands overlap: one band on the lane");

            // save unchanged: the record is the 0.4.3 one (exit keys only), entrance keys never written
            var keys = new ExitKeyList();
            keys.Add(exit.Key);
            var sink = new ArrayBytes { Out = new List<byte>() };
            ExitRecord.Encode(ref sink, BoxRecordFlags.None, ref keys);
            byte[] bytes = sink.Out.ToArray();
            Check(ExitRecord.Version == 2 && bytes.Length == 2 + 2 + 5 && bytes[0] == 2, "save: same v2 record, one exit key, nothing for entrances");
            string root = Path.Combine(RepoRoot(), "JunctionRules");
            string comps = StripComments(File.ReadAllText(Path.Combine(root, "Boxes", "BoxComponents.cs")));
            int start = comps.IndexOf("struct YellowBoxEntrance", StringComparison.Ordinal);
            string ybe = comps.Substring(start, comps.IndexOf("public void Serialize", start, StringComparison.Ordinal) - start);
            int fields = System.Text.RegularExpressions.Regex.Matches(ybe, @"public\s+(byte|FixedList64Bytes<ExitKey>)\s+m_\w+;").Count;
            Check(fields == 2 && !ybe.Contains("Entrance m_") && !comps.Contains("YellowBoxEntranceG9"), "save: YellowBoxEntrance still holds only m_Flags + m_Exits (no new component or field)");
            string store = File.ReadAllText(Path.Combine(root, "Boxes", "BoxStore.cs"));
            Check(store.Contains("if (key.ExitKind == ExitKind.Entrance) return false;"), "BoxStore refuses to save an entrance key");
            string paint = File.ReadAllText(Path.Combine(root, "Paint", "YellowBoxPaintSystem.cs"));
            Check(paint.Contains("private void AddEntranceBoxes(") && paint.Contains("Derived = true") && paint.Contains("!b.Derived)   // JR-T-014c"),
                "paint derives entrance boxes at resolve time and never drops / re-keys them in the save");
            string facts = File.ReadAllText(Path.Combine(root, "Boxes", "BoxFacts.cs"));
            Check(facts.Contains("AccessRules.EntranceBoxes(inCands, boxedCands)"), "driving bands include the boxed building's entrances");
            string yb = File.ReadAllText(Path.Combine(root, "Stage2", "JRYellowBox.cs"));
            Check(yb.Contains("role == BoxDriving.EntranceRole.Entering") && yb.Contains("EnteringHold(") && yb.Contains("m_EdgeLaneData.HasComponent(nextLane)"),
                "BoxHold judges Entering cars; a lane change (EdgeLane of the same road) is not an entry");
            string whole = File.ReadAllText(Path.Combine(root, "Stage2", "JRWholeJunction.cs"));
            Check(whole.Contains("private bool HasRoomInto(") && whole.Contains("Stage2Counters.EntranceHeldEntering"), "room into the building: same RoomScan / ScanLane as HasRoomPast; counted");
        }

        private static void G9EntranceLogTests()
        {
            // record packing
            long f = EntranceLog.Pack(EntranceLog.Event.InBox, true, 3.27f, 0.5f);
            EntranceLog.Unpack(f, out EntranceLog.Event e, out bool room, out float sp, out float t);
            Check(e == EntranceLog.Event.InBox && room && Math.Abs(sp - 3.3f) < 0.01f && Math.Abs(t - 0.5f) < 0.001f, "entrance log record packs event, room, speed, band start");
            // reader: ring protocol, overrun counted
            var ring = new long[EntranceLog.RingLength];
            long head = 0;
            void Put(long v, long r, long facts, long frame)
            {
                long seq = ++head; ring[0] = seq; int b = EntranceLog.SlotBase(seq);
                ring[b + 1] = v; ring[b + 2] = r; ring[b + 3] = facts; ring[b + 4] = frame; ring[b] = seq;
            }
            var reader = new EntranceLogReader();
            for (int i = 0; i < EntranceLog.Slots + 10; i++) Put(i, 1, 0, i);
            int got = reader.Drain(head, i => ring[i], (a, b, c, d) => { });
            Check(got == EntranceLog.Slots && reader.TakeLost() == 10, "reader takes the last ring's worth, counts the overrun");

            var lines = new List<string>();
            var book = new EntranceLogBook(lines.Add);
            const long road = 312580;
            long Pk(EntranceLog.Event ev, bool rm, float speed) => EntranceLog.Pack(ev, rm, speed, 0.4f);
            double now = 0;
            book.Tick(now, 0);
            // vehicle 1: held 40 s on the road (record every 4 frames), then let in
            for (uint fr = 0; fr <= 2400; fr += 4) book.Record(1, road, Pk(EntranceLog.Event.Held, false, 0f), fr, now);
            Check(lines.Count(l => l.Contains("vehicle 1:0 held before the box 30 s")) == 1, "notable once: held before the box over 30 s");
            Check(lines.Count(l => l.StartsWith("entrance box sample: vehicle 1:0 held")) == 1, "one sample line for the hold episode, not one per step");
            // vehicle 2: could not stop, went in without room and stands 3 s
            for (uint fr = 100; fr <= 280; fr += 4) book.Record(2, road, Pk(EntranceLog.Event.InBox, false, fr < 120 ? 4f : 0f), fr, now);
            Check(lines.Count(l => l.Contains("vehicle 2:0 entered the box without room and stands in it")) == 1, "notable: entered without room and stood in the box");
            // vehicle 3: UK turner, room, waits 10 s in the box: normal, no notable
            int before = lines.Count;
            for (uint fr = 0; fr <= 600; fr += 4) book.Record(3, road, Pk(EntranceLog.Event.InBox, true, 0f), fr, now);
            Check(lines.Count == before, "turner waiting in the box with room (10 s): no line");
            // vehicle 4: let in, moving: one sample at most
            for (uint fr = 0; fr <= 40; fr += 4) book.Record(4, road, Pk(EntranceLog.Event.Go, true, 8f), fr, now);
            Check(lines.Count(l => l.Contains("vehicle 4:0 let in")) == 1, "let-in decision sampled once per vehicle");
            book.Tick(now + 61, 2400);
            string sum = lines.LastOrDefault() ?? "";
            Check(sum.StartsWith("entrance boxes (last 61 s): 4 vehicle(s) turning in") && sum.Contains("1 held before the box (way in full)") && sum.Contains("1 went in without room")
                  && sum.Contains("1 stood in the box without room") && sum.Contains("1 held over 30 s"), "summary line per interval: " + sum);
            int afterSum = lines.Count;
            book.Tick(now + 130, 9000);
            Check(lines.Count == afterSum && book.Tracked == 0, "quiet interval: no summary line; old episodes forgotten");

            // caps + expected volume: 10 minutes of a busy town (200 entering vehicles a minute, a third held over 30 s,
            // a tenth stood in the box without room)
            var vol = new List<string>();
            var busy = new EntranceLogBook(vol.Add);
            busy.Tick(0, 0);
            uint frame = 0;
            long id = 1000;
            for (int minute = 0; minute < 10; minute++)
            {
                for (int k = 0; k < 200; k++, id++)
                {
                    uint s0 = frame + (uint)k * 10;
                    if (k % 3 == 0) for (uint fr = 0; fr <= 2000; fr += 16) busy.Record(id, road, Pk(EntranceLog.Event.Held, false, 0f), s0 + fr, minute * 60.0);
                    else if (k % 10 == 1) for (uint fr = 0; fr <= 200; fr += 16) busy.Record(id, road, Pk(EntranceLog.Event.InBox, false, fr < 20 ? 3f : 0f), s0 + fr, minute * 60.0);
                    else for (uint fr = 0; fr <= 64; fr += 16) busy.Record(id, road, Pk(EntranceLog.Event.Go, true, 6f), s0 + fr, minute * 60.0);
                }
                frame += 3600;
                busy.Tick(minute * 60.0 + 60.0, frame);
            }
            int cap = 10 * (EntranceLogBook.MaxNotable + EntranceLogBook.MaxSamples + 1);
            int notableN = vol.Count(l => l.StartsWith("entrance box: ")), sampleN = vol.Count(l => l.StartsWith("entrance box sample"));
            if (Environment.GetEnvironmentVariable("JR_SHOW_VOLUME") == "1") Console.WriteLine("VOL " + vol.Count + " notable " + notableN + " samples " + sampleN);
            Check(vol.Count <= cap && vol.Count(l => l.StartsWith("entrance boxes (last")) == 10, $"busy 10 minutes: {vol.Count} lines (cap {cap}), one summary a minute");
            Check(vol.Last().Contains("over the cap"), "summary reports lines over the cap");

            // repo: always on, job side only the fixed ring
            string root = Path.Combine(RepoRoot(), "JunctionRules");
            string yb = StripComments(File.ReadAllText(Path.Combine(root, "Stage2", "JRYellowBox.cs")));
            int w0 = yb.IndexOf("private unsafe void EntranceWrite(", StringComparison.Ordinal);
            string write = yb.Substring(w0, yb.IndexOf("}", yb.IndexOf("Interlocked.Exchange(ref p[b], seq);", w0, StringComparison.Ordinal), StringComparison.Ordinal) - w0);
            Check(!write.Contains("new ") && !write.Contains("Counting") && write.Contains("EntranceLog.SlotBase(seq)"), "job side: fixed-size ring write, no allocation, not gated by a setting");
            Check(yb.Contains("EntranceWrite(self, road, EntranceLog.Pack(EntranceLog.Event.InBox") && yb.Contains("EntranceWrite(self, road, EntranceLog.Pack(holds ?"), "job writes held / let in / in the box records");
            string bridge = StripComments(File.ReadAllText(Path.Combine(root, "Stage2", "JRStage2Bridge.cs")));
            Check(bridge.Contains("DrainEntrance();") && !System.Text.RegularExpressions.Regex.IsMatch(bridge, @"if\s*\([^)]*(Trace|Timing|Setting)[^)]*\)\s*DrainEntrance"), "bridge drains the entrance log every step, no setting");
            Check(bridge.Contains("s_BoxEdges.m_EntranceLog = EntranceRing();"), "ring handed to the job inside the band set (no new Vanilla block)");
        }

        private static void G10FallbackArrowTests()
        {
            // fallback is never a target (G10)
            var fb = new ExitCandidate { Kind = ExitKind.Fallback, Key = new ExitKey(ExitKind.Fallback, 0, 24), RoadId = 5, AlongM = 16 };
            var drive = new ExitCandidate { Kind = ExitKind.Driveway, Key = new ExitKey(ExitKind.Driveway, -13.4, 16), RoadId = 312580, AlongM = 45.4 };
            var ent = new ExitCandidate { Kind = ExitKind.Entrance, Key = new ExitKey(ExitKind.Entrance, 4, 24), RoadId = 5, AlongM = 20 };
            var t1 = AccessRules.Targets(new List<ExitCandidate> { fb }, null, out string why1);
            Check(t1.Count == 0 && why1 != null && why1.Contains("no real car exit or entrance"), "building with only the fallback: no box target (" + why1 + ")");
            var t2 = AccessRules.Targets(new List<ExitCandidate> { fb, drive }, new List<ExitCandidate> { ent }, out string why2);
            Check(t2.Count == 2 && why2 == null && !t2.Exists(c => c.Kind == ExitKind.Fallback), "real exit + entrance are the targets, the fallback is dropped");
            Check(AccessRules.Fallback(false, 0, 2) == SavedFallbackFate.Marker, "saved fallback on a building with entrances: kept as marker, not drawn");
            Check(AccessRules.Fallback(false, 0, 0) == SavedFallbackFate.NoAccess, "saved fallback on a building with no real access: kept, nothing drawn or obeyed");
            Check(AccessRules.Fallback(true, 1, 0) == SavedFallbackFate.Rekey, "saved fallback within 2 m of a real exit: re-keyed to it (0.4.3 rule)");
            ExitKey marker = AccessRules.MarkerKey(24);
            Check(marker.ExitKind == ExitKind.Fallback && marker.X10 == 0 && marker.Z10 == 240, "tool marker key = the 0.4.3 lot-front fallback key (existing record kind)");

            // arrows
            var ring = Rect(-4, 0, 4, 8, 1);   // box on the road; kerb at z = 8, building beyond
            var access = new V3(0, 1, 13);
            AccessArrow ex = AccessArrow.Build(ring, access, AccessWay.Out, false);
            Check(ex != null && ex.HeadAtBox && !ex.HeadAtAccess && !ex.Dashed, "exit: head at the box (out of the building into the box)");
            Check(Math.Abs(ex.BoxEnd.Z - 8) < 1e-9 && Math.Abs(ex.BoxEnd.X) < 1e-9, "arrow starts on the box mouth on the kerb nearest the access");
            Check(ex.Segments.Count == 3, "exit arrow = shaft + 2 head strokes");
            bool headNearBox = ex.Segments.Skip(1).All(s => Math.Abs(s.B.Z - 8) < 1e-9 && s.A.Z > 8);
            Check(headNearBox, "exit head strokes point at the box end");
            AccessArrow en = AccessArrow.Build(ring, access, AccessWay.In, false);
            Check(en != null && !en.HeadAtBox && en.HeadAtAccess && en.Segments.Skip(1).All(s => Math.Abs(s.B.Z - 13) < 1e-9 && s.A.Z < 13), "entrance: head at the building (in from the box)");
            AccessArrow both = AccessArrow.Build(ring, access, AccessWay.Both, false);
            Check(both != null && both.HeadAtBox && both.HeadAtAccess && both.Segments.Count == 5, "two-way access: double-headed");
            AccessArrow dashed = AccessArrow.Build(ring, access, AccessWay.Both, true);
            Check(dashed != null && dashed.Dashed && dashed.Segments.Count > 5, "fallback: dashed shaft (visibly different)");
            Check(AccessArrow.Build(ring, new V3(0, 1, 8.05), AccessWay.Out, false) == null, "access on the box edge: no arrow");
            string tool = File.ReadAllText(Path.Combine(RepoRoot(), "JunctionRules", "Boxes", "YellowBoxToolSystem.cs"));
            Check(tool.Contains("AccessArrow.Build(ring, new V3(p.x, p.y, p.z), t.Exit.Way, t.Fallback)") && tool.Contains("buffer.DrawLine(c, c, 0f"), "tool draws the arrow on the hover's overlay path, hover colour");
        }
    }
}
