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
