using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// JR-T-025 (1.1.1): "why is this car slowing" (Core/WhySlowed.cs). One tracked car (the game's selected vehicle wins,
    /// else the API's car) records the binding (smallest) limit and its source each step; the main thread reads the one
    /// shared slot (seqlock), keeps a per-car history of reason changes, logs rate-limited lines and answers WhySlowed.
    /// </summary>
    internal static partial class Program
    {
        private static readonly long WhyCar = JunctionWatch.Key(878675, 1), WhyCar2 = JunctionWatch.Key(643963, 1);
        private static readonly long WhyBlk = JunctionWatch.Key(878118, 1), WhyLane = JunctionWatch.Key(49960, 1);

        private static bool WhyNaN(float v) => float.IsNaN(v);

        private static void WhyRankingTests()
        {
            var t = new WhyTrack();
            t.Begin(22f);
            Check(t.Bound.Rule == WhyRule.Free && t.Bound.Limit == 22f && WhyNaN(t.Bound.Distance) && WhyNaN(t.Bound.Extra) && WhyNaN(t.BeforeSteer), "Begin: free at the car's top speed, no distance / extra / steering mark");
            // binding = smallest limit
            Check(t.Offer(WhyRule.StockCollision, 15f, WhyBlk, WhyLane, 10f, float.NaN), "Offer below binds");
            Check(!t.Offer(WhyRule.StockTargetDistance, 18f, 0, WhyLane, 3f, float.NaN) && t.Bound.Rule == WhyRule.StockCollision, "Offer above does not bind");
            Check(t.Offer(WhyRule.StockTargetDistance, 9f, 0, WhyLane, 3f, float.NaN) && t.Bound.Rule == WhyRule.StockTargetDistance && t.Bound.Limit == 9f, "the smaller of several offers wins");
            // ties are stable: the earlier source stays
            Check(!t.Offer(WhyRule.StockCollision, 9f, WhyBlk, WhyLane, 1f, float.NaN) && t.Bound.Rule == WhyRule.StockTargetDistance && t.Bound.Blocker == 0, "a tie keeps the earlier source");
            // many offers in any order: the minimum and, at a tie, the first of its value
            var rnd = new Random(25);
            for (int n = 0; n < 200; n++)
            {
                var w = new WhyTrack();
                w.Begin(30f);
                float min = 30f;
                WhyRule first = WhyRule.Free;
                for (int k = 0; k < 8; k++)
                {
                    float v = rnd.Next(1, 12);   // small range: many ties
                    WhyRule r = (WhyRule)(2 + k);
                    if (v < min) { min = v; first = r; }
                    w.Offer(r, v, 0, 0, float.NaN, float.NaN);
                }
                if (w.Bound.Limit != min || w.Bound.Rule != first) { Check(false, $"random offers: smallest wins, first at a tie (case {n})"); break; }
            }
            Check(true, "random offers: smallest wins, first at a tie (200 cases)");
        }

        private static void WhyClassifyTests()
        {
            // the game's blocker record -> stock rule
            Check(WhyRules.Stock(0, false) == WhyRule.StockTargetEnd, "type None: end of the path read");
            Check(WhyRules.Stock(1, true) == WhyRule.StockCarAhead && WhyRules.Stock(1, false) == WhyRule.StockGiveWay, "Continuing: car ahead with a blocker, else give-way");
            Check(WhyRules.Stock(2, true) == WhyRule.StockConflictVehicle && WhyRules.Stock(2, false) == WhyRule.StockGiveWay, "Crossing: conflict vehicle with a blocker, else give-way");
            Check(WhyRules.Stock(3, false) == WhyRule.StockSignal && WhyRules.Stock(4, true) == WhyRule.StockPedestrian && WhyRules.Stock(5, false) == WhyRule.StockLaneSpeed
                  && WhyRules.Stock(6, false) == WhyRule.StockCaution && WhyRules.Stock(8, true) == WhyRule.StockOncoming, "Signal / Temporary / Limit / Caution / Oncoming");
            Check(WhyRules.Stock(7, false) == WhyRule.StockOther && WhyRules.Stock(99, true) == WhyRule.StockOther, "anything else: stock other, never a JR rule");
            for (int b = 0; b < 256; b++)
            {
                if (WhyRules.IsJr(WhyRules.Stock(b, true)) || WhyRules.IsJr(WhyRules.Stock(b, false))) { Check(false, $"blocker type {b} classified as a JR rule"); break; }
            }
            Check(true, "no blocker record ever classifies as a Junction Rules rule");
            // junction entry chain
            Check(WhyRules.EntryChain(true, 3) == WhyRule.StockSignal && WhyRules.EntryChain(true, 5) == WhyRule.StockStopSign && WhyRules.EntryChain(true, 1) == WhyRule.StockJunctionRoom
                  && WhyRules.EntryChain(true, 2) == WhyRule.StockOther && WhyRules.EntryChain(false, 3) == WhyRule.StockLaneSpeed, "entry chain: light / STOP / room / lane speed");
            // give-way sites the whole-junction rule may change
            Check(WhyRules.GiveWay(false, true, WhyRule.StockGiveWay) == WhyRule.StockGiveWay && WhyRules.GiveWay(true, false, WhyRule.StockGiveWay) == WhyRule.JrWholeJunctionGap
                  && WhyRules.GiveWay(true, true, WhyRule.StockGiveWay) == WhyRule.JrSafetyRecheck, "give-way: stock unless JR changed it (gap / safety re-check)");
            Check(WhyRules.Hook(0, WhyRule.JrBoxRoom) == WhyRule.JrBoxRoom && WhyRules.Hook((byte)WhyRule.JrSoftZebra, WhyRule.JrZebraSection) == WhyRule.JrSoftZebra, "hook: the noted rule, else the hook's family");
            // Settle binds an untagged lowering only
            var t = new WhyTrack();
            t.Begin(20f);
            t.Settle(20f, 1, WhyBlk, WhyLane);
            Check(t.Bound.Rule == WhyRule.Free, "Settle: limit unchanged, nothing bound");
            t.Settle(12f, 1, WhyBlk, WhyLane);
            Check(t.Bound.Rule == WhyRule.StockCarAhead && t.Bound.Limit == 12f && t.Bound.Blocker == WhyBlk && t.Bound.Lane == WhyLane && WhyNaN(t.Bound.Distance), "Settle: untagged lowering classified (car ahead), distance unknown");
            t.Bind(WhyRule.JrBoxRoom, 8f, 0, WhyLane, 12f, 3f);
            t.Settle(8f, 3, 0, WhyLane);
            Check(t.Bound.Rule == WhyRule.JrBoxRoom, "Settle after a tagged limit at the same value keeps the tag");
            // every rule has a name; ids unique
            var ids = new HashSet<string>();
            bool named = true;
            foreach (WhyRule r in Enum.GetValues(typeof(WhyRule)))
            {
                string id = WhyRules.Id(r);
                named &= !id.StartsWith("rule_", StringComparison.Ordinal) && ids.Add(id) && !string.IsNullOrEmpty(WhyRules.Text(r));
                named &= WhyRules.IsJr(r) == id.StartsWith("jr_", StringComparison.Ordinal);
            }
            Check(named, "every rule has a unique id and a text; jr_ ids exactly for JR rules");
        }

        private static void WhyFinishTests()
        {
            var t = new WhyTrack();
            t.Begin(20f);
            t.Bind(WhyRule.JrBoxRoom, 6f, 0, WhyLane, 9f, 2f);
            t.Finish(6.0004f, 6f);
            Check(t.Bound.Rule == WhyRule.JrBoxRoom && Math.Abs(t.Bound.Limit - 6.0004f) < 1e-6, "Finish: record explains the final limit (within tolerance) -> kept");
            t.Bind(WhyRule.JrBoxRoom, 6f, 0, WhyLane, 9f, 2f);
            t.Finish(4f, float.NaN);
            Check(t.Bound.Rule == WhyRule.StockOther && t.Bound.Limit == 4f && t.Bound.Blocker == 0, "Finish: unexplained lower final -> stock other (never the JR rule)");
            t.Bind(WhyRule.JrBoxRoom, 6f, 0, WhyLane, 9f, 2f);
            t.Finish(7f, float.NaN);
            Check(t.Bound.Rule == WhyRule.StockOther, "Finish: a tagged value later undone -> stock other");
            t.Bind(WhyRule.StockCarAhead, 6f, WhyBlk, WhyLane, 9f, float.NaN);
            t.Finish(3f, 6f);
            Check(t.Bound.Rule == WhyRule.StockSteering && t.Bound.Limit == 3f && t.Bound.Lane == WhyLane, "Finish: the turning tail changed the limit -> stock steering");
            t.Bind(WhyRule.StockCarAhead, 2f, WhyBlk, WhyLane, 9f, float.NaN);
            t.Finish(-2f, 2f);
            Check(t.Bound.Rule == WhyRule.StockSteering && t.Bound.Limit == 2f && t.Bound.Extra == 1f, "Finish: reversing -> steering, by size, extra 1");
            t.Bind(WhyRule.StockCarAhead, 2f, WhyBlk, WhyLane, 9f, float.NaN);
            t.Resolve(-2f, WhyRule.StockOther);
            Check(t.Bound.Rule == WhyRule.StockCarAhead, "Resolve compares by size");
            var empty = new WhyTrack();
            empty.Resolve(5f, WhyRule.StockOther);
            Check(empty.Bound.Rule == WhyRule.StockOther && empty.Bound.Limit == 5f, "Resolve with no record -> stock other");
        }

        private static WhySample WhyWriteRead(WhyRecord r, out int writes, uint frame = 37194508, long car = 0)
        {
            var store = new WhyArrayStore(new long[WhySlot.Length]);
            WhySlot.Write(ref store, frame, car == 0 ? WhyCar : car, in r, 8.9f, 1212.34f, 505.5f, r.Limit, WhyBlk, 1);
            writes = store.Writes;
            WhySlot.TryRead(ref store, out WhySample s);
            return s;
        }

        private static void WhySlotTests()
        {
            var r = new WhyRecord { Rule = WhyRule.JrSoftZebra, Blocker = WhyBlk, Lane = WhyLane, Distance = 10.257f, Limit = 8.9f, Extra = 2.5f };
            WhySample s = WhyWriteRead(r, out int writes);
            Check(s.Seq == 2 && s.Frame == 37194508 && s.Car == WhyCar && s.Rule == WhyRule.JrSoftZebra && s.Blocker == WhyBlk && s.Lane == WhyLane, "slot round trip: ids, rule, frame, even sequence");
            Check(Math.Abs(s.Distance - 10.257f) < 0.001 && Math.Abs(s.Limit - 8.9f) < 0.001 && Math.Abs(s.Extra - 2.5f) < 0.001 && Math.Abs(s.Speed - 8.9f) < 0.001
                  && Math.Abs(s.X - 1212.34f) < 0.01 && Math.Abs(s.Z - 505.5f) < 0.01 && s.GameBlocker == WhyBlk && s.GameBlockerType == 1, "slot round trip: fixed-point values");
            var n = new WhyRecord { Rule = WhyRule.StockCarAhead, Distance = float.NaN, Limit = 3f, Extra = float.NaN };
            s = WhyWriteRead(n, out _);
            Check(WhyNaN(s.Distance) && WhyNaN(s.Extra) && s.Limit == 3f, "NaN distance / extra survive the slot");
            Check(WhySlot.ToFixed(float.PositiveInfinity, 1000f) == WhySlot.NaNFixed && WhySlot.ToFixed(3e9f, 1000f) == WhySlot.NaNFixed && WhySlot.ToFixed(-1.2345f, 1000f) == -1235, "fixed point: infinite / out of range -> NaN; rounding away from zero");
            Check(WhySlot.Hi(WhySlot.Pack2(-7, -9)) == -7 && WhySlot.Lo(WhySlot.Pack2(-7, -9)) == -9, "Pack2 keeps signed halves");
            // seqlock: odd = writing, nothing written = no sample, torn read rejected
            var store = new WhyArrayStore(new long[WhySlot.Length]);
            Check(!WhySlot.TryRead(ref store, out _), "nothing written yet: no sample");
            store.Data[WhySlot.Seq] = 5;
            Check(!WhySlot.TryRead(ref store, out _), "odd sequence (write in progress): no sample");
            store.Data[WhySlot.Seq] = 6;
            WhySlot.Write(ref store, 1, WhyCar, in r, 1f, 0f, 0f, 1f, 0, 0);
            Check(store.Data[WhySlot.Seq] == 8, "a write moves an even sequence to the next even one");
            store.Data[WhySlot.Seq] = 7;   // a writer that died mid-write
            WhySlot.Write(ref store, 2, WhyCar, in r, 1f, 0f, 0f, 1f, 0, 0);
            Check(store.Data[WhySlot.Seq] == 8, "a write from an odd sequence ends even");
            Check(writes == 12, "one write = 12 stores (two sequence writes + 10 values)");
        }

        private static void WhyOffCostTests()
        {
            // the gate: nothing tracked -> no car writes; tracked -> only that car
            Check(!WhySlot.ShouldWrite(0, 0) && !WhySlot.ShouldWrite(0, WhyCar) && !WhySlot.ShouldWrite(WhyCar, WhyCar2) && WhySlot.ShouldWrite(WhyCar, WhyCar), "ShouldWrite: nothing tracked = nobody; tracked = only that car");
            var store = new WhyArrayStore(new long[WhySlot.Length]);
            var r = new WhyRecord { Rule = WhyRule.Free, Limit = 10f, Distance = float.NaN, Extra = float.NaN };
            for (int i = 1; i <= 5000; i++)
            {
                long car = JunctionWatch.Key(i, 1);
                if (WhySlot.ShouldWrite(0, car)) WhySlot.Write(ref store, 1, car, in r, 1f, 0f, 0f, 1f, 0, 0);
            }
            Check(store.Writes == 0, "5000 cars, nothing tracked: no slot write");
            for (int i = 1; i <= 5000; i++)
            {
                long car = JunctionWatch.Key(i, 1);
                if (WhySlot.ShouldWrite(WhyCar2, car)) WhySlot.Write(ref store, 1, car, in r, 1f, 0f, 0f, 1f, 0, 0);
            }
            Check(store.Writes == 0, "tracked car not among them: no slot write");
            for (int i = 1; i <= 5000; i++)
            {
                long car = JunctionWatch.Key(i, 1);
                if (WhySlot.ShouldWrite(JunctionWatch.Key(4321, 1), car)) WhySlot.Write(ref store, 1, car, in r, 1f, 0f, 0f, 1f, 0, 0);
            }
            Check(store.Writes == 12 && store.Data[WhySlot.Car] == JunctionWatch.Key(4321, 1), "one tracked car among 5000: exactly one write, that car");
            // tracking choice: the selection wins
            Check(WhyHandoff.Choose(WhyCar, WhyCar2, out string src) == WhyCar && src == "selection", "selected vehicle wins over the API car");
            Check(WhyHandoff.Choose(0, WhyCar2, out src) == WhyCar2 && src == "api", "nothing selected: the API car");
            Check(WhyHandoff.Choose(0, 0, out src) == 0 && src == "none", "neither: nothing tracked");
        }

        private static WhySample WhyS(long seq, WhyRule rule, long blocker = 0, uint frame = 100, long car = 0, float limit = 8f)
            => new WhySample { Seq = seq, Frame = frame, Car = car == 0 ? WhyCar : car, Rule = rule, Blocker = blocker, Limit = limit, Distance = 10f, Extra = float.NaN, Speed = 9f, X = 1212f, Z = 505f, Lane = WhyLane };

        private static void WhyBookTests()
        {
            var b = new WhyBook();
            Check(b.Track(0, "none") == null, "tracking nothing at start: no line");
            string l = b.Track(WhyCar, "selection");
            Check(l != null && l.Contains("878675:1") && l.Contains("selection"), "selection change: a log line naming the car and source");
            Check(b.Track(WhyCar, "selection") == null, "same car, same source: no line");
            Check(b.Track(WhyCar, "api") != null && b.Car == WhyCar && b.Source == "api", "same car, other source: a line");
            // change detector
            string a1 = b.Observe(WhyS(2, WhyRule.Free), 0.0);
            Check(a1 != null && a1.Contains("[free]") && b.Changes.Count == 1, "first reading: a change, logged");
            Check(b.Observe(WhyS(2, WhyRule.JrSoftZebra), 0.0) == null && b.Changes.Count == 1, "repeated sequence ignored");
            Check(b.Observe(WhyS(4, WhyRule.Free, frame: 101), 0.6) == null && b.Changes.Count == 1, "same reason again: no change, no line");
            Check(b.Observe(WhyS(6, WhyRule.Free, car: WhyCar2), 0.6) == null && b.Changes.Count == 1, "a sample of another car is ignored");
            string a2 = b.Observe(WhyS(8, WhyRule.JrSoftZebra, WhyBlk, 102), 0.7);
            Check(a2 != null && a2.Contains("jr_soft_zebra") && a2.Contains("frame 102") && a2.Contains("878118:1") && b.Changes.Count == 2, "rule change: logged with frame and blocker");
            // rate limit: 2 lines a second; dropped lines still in the history; the next line counts them
            Check(b.Observe(WhyS(10, WhyRule.JrSoftZebra, WhyCar2, 103), 0.8) == null && b.Changes.Count == 3, "blocker change in the same second (3rd): history yes, line dropped");
            Check(b.Observe(WhyS(12, WhyRule.StockSignal, 0, 104), 0.9) == null && b.Changes.Count == 4 && b.DroppedLines == 2, "4th change in the second: dropped too");
            string a5 = b.Observe(WhyS(14, WhyRule.StockCarAhead, WhyBlk, 105), 1.05);
            Check(a5 != null && a5.Contains("2 earlier change line(s) dropped"), "next second: line again, says how many were dropped");
            // history capped at MaxChanges, oldest dropped
            for (int i = 0; i < 30; i++) b.Observe(WhyS(100 + 2 * i, i % 2 == 0 ? WhyRule.StockSignal : WhyRule.StockLaneSpeed, 0, (uint)(200 + i)), 10 + i);
            Check(b.Changes.Count == WhyBook.MaxChanges && b.Changes[WhyBook.MaxChanges - 1].Frame == 229 && b.Changes[0].Frame == 210, "history keeps the last 20 changes");
            // per-car history: archived on a car change, restored when tracked again
            b.Track(WhyCar2, "selection");
            Check(b.Changes.Count == 0 && !b.HasCurrent && b.HistoryOf(WhyCar) != null && b.HistoryOf(WhyCar).Count == 20, "other car: fresh history, the first car's kept");
            b.Observe(WhyS(2, WhyRule.JrBoxRoom, 0, 300, WhyCar2), 50);
            b.Track(0, "none");
            Check(b.Car == 0 && b.Source == "none" && b.HistoryOf(WhyCar2).Count == 1, "tracking stopped: the history stays readable");
            b.Track(WhyCar, "api");
            Check(b.Changes.Count == 20 && b.Changes[19].Frame == 229, "a car tracked again gets its history back");
            var cars = new List<long>(b.KnownCars);
            Check(cars.Count == 2 && cars[0] == WhyCar && cars[1] == WhyCar2, "known cars: the tracked one first");
            for (int i = 1; i <= WhyBook.MaxCars + 3; i++)
            {
                b.Track(JunctionWatch.Key(1000 + i, 1), "api");
                b.Observe(WhyS(2, WhyRule.Free, 0, 1, JunctionWatch.Key(1000 + i, 1)), 100 + i);
            }
            Check(new List<long>(b.KnownCars).Count == WhyBook.MaxCars + 1, "at most 8 earlier cars kept (plus the tracked one)");
            Check(WhyBook.ExtraText(WhyRule.JrBoxLookPastRefused, (float)LookPastRefusal.Building) == "look-past refused: building_blocks_sight"
                  && WhyBook.ExtraText(WhyRule.JrBoxRoom, 2.04f) == "room short by 2.0 m" && WhyBook.ExtraText(WhyRule.StockGiveWay, 32f) == "priority delta 32", "extra in words per rule");
        }

        private static void WhyJsonTests()
        {
            var b = new WhyBook();
            string empty = b.Json("1.1.1", new List<string> { "n1" });
            Check(Json.TryParse(empty, out JsonValue e, out _) && e.Get("ok").Bool && e.Get("action").String == "why_slowed" && e.Get("apiVersion").Number == 1
                  && e.Get("tracking").Get("car").Kind == JsonKind.Null && e.Get("tracking").Get("source").String == "none" && e.Get("current").Kind == JsonKind.Null
                  && e.Get("changes").Array.Count == 0 && e.Get("notes").Array[0].String == "n1", "JSON nothing tracked: ok, tracking car null, current null, no changes, notes");
            b.Track(WhyCar, "selection");
            var s1 = WhyS(2, WhyRule.Free, 0, 10);
            var s2 = WhyS(4, WhyRule.JrBoxLookPastRefused, WhyBlk, 11, limit: 5.5f);
            s2.Extra = (float)LookPastRefusal.SlowAhead;
            s2.BlockerKind = WhyKind.Vehicle;
            s2.BlockerX = 1213f;
            s2.BlockerZ = 540f;
            b.Observe(s1, 0);
            b.Observe(s2, 0.1);
            string js = b.Json("1.1.1", null);
            Check(Json.TryParse(js, out JsonValue v, out string err), "JSON parses (" + err + ")");
            var keys = new List<string>();
            foreach (var m in v.Members) keys.Add(m.Key);
            Check(string.Join(",", keys) == "ok,apiVersion,modVersion,action,tracking,current,historyCar,changes,knownCars,notes", "top-level keys in order: " + string.Join(",", keys));
            JsonValue cur = v.Get("current");
            Check(v.Get("tracking").Get("car").String == "878675:1" && v.Get("tracking").Get("source").String == "selection" && v.Get("historyCar").String == "878675:1", "tracking and historyCar");
            Check(cur.Get("rule").String == "jr_box_look_past_refused" && cur.Get("jr").Bool && Math.Abs(cur.Get("limit").Number - 5.5) < 1e-6 && cur.Get("frame").Number == 11
                  && cur.Get("extraText").String == "look-past refused: slow_vehicle_ahead", "current: rule, jr flag, limit, frame, extra text");
            JsonValue blk = cur.Get("blocker");
            Check(blk.Get("id").String == "878118:1" && blk.Get("kind").String == "vehicle" && blk.Get("position").Get("x").Number == 1213, "blocker id, kind, position");
            var ck = new List<string>();
            foreach (var m in cur.Members) ck.Add(m.Key);
            Check(string.Join(",", ck) == "frame,rule,text,jr,limit,speed,distance,extra,extraText,position,blocker,lane,finalLimit,gameBlocker,gameBlockerType", "reading keys: " + string.Join(",", ck));
            JsonValue ch = v.Get("changes");
            Check(ch.Array.Count == 2 && ch.Array[0].Get("frame").Number == 11 && ch.Array[1].Get("frame").Number == 10 && ch.Array[1].Get("blocker").Kind == JsonKind.Null
                  && ch.Array[1].Get("extra").Kind == JsonKind.Null && !ch.Array[1].Get("jr").Bool, "changes newest first; no blocker / NaN extra = null");
            b.Track(WhyCar2, "api");
            JsonValue h;
            Check(Json.TryParse(b.Json("1.1.1", null, WhyCar), out h, out _) && h.Get("historyCar").String == "878675:1" && h.Get("changes").Array.Count == 2
                  && h.Get("tracking").Get("car").String == "643963:1" && h.Get("knownCars").Array.Count == 2, "history of another kept car on request");
            // arguments
            Check(WhyBook.ParseArgs(null, out bool set, out long car, out long hist, out _) && !set && car == 0 && hist == 0, "no args: read only");
            Check(WhyBook.ParseArgs("{}", out set, out car, out _, out _) && !set, "{}: read only");
            Check(WhyBook.ParseArgs("{\"car\":\"878675:1\"}", out set, out car, out _, out _) && set && car == WhyCar, "car \"I:V\"");
            Check(WhyBook.ParseArgs("{\"car\":{\"index\":643963,\"version\":1}}", out set, out car, out _, out _) && set && car == WhyCar2, "car {index, version}");
            Check(WhyBook.ParseArgs("{\"car\":null}", out set, out car, out _, out _) && set && car == 0, "car null: stop the API tracking");
            Check(WhyBook.ParseArgs("{\"clear\":true}", out set, out car, out _, out _) && set && car == 0, "clear true");
            Check(WhyBook.ParseArgs("{\"history\":\"878675:1\"}", out set, out _, out hist, out _) && !set && hist == WhyCar, "history: read a kept car without changing tracking");
            foreach (string bad in new[] { "[1]", "nope", "{\"car\":\"x\"}", "{\"car\":\"0:1\"}", "{\"car\":5}", "{\"clear\":true,\"car\":\"1:1\"}", "{\"clear\":1}", "{\"vehicle\":\"1:1\"}", "{\"history\":null}" })
                Check(!WhyBook.ParseArgs(bad, out _, out _, out _, out string why) && !string.IsNullOrEmpty(why), "bad argument refused with a reason: " + bad);
            // notes
            Check(WhyHandoff.Notes(0, 0, false, 0).Exists(n => n.Contains("has not run")), "notes: copy driving not run");
            Check(WhyHandoff.Notes(5, 9, false, 0).Exists(n => n.Contains("nothing tracked")), "notes: nothing tracked says how to track");
        }

        private static string WhyBlock(string src, string id)
        {
            Match m = Regex.Match(src, @"// JR-STAGE2 BEGIN " + Regex.Escape(id) + @":[^\n]*\n(.*?)// JR-STAGE2 END", RegexOptions.Singleline);
            return m.Success ? m.Groups[1].Value : null;
        }

        private static void WhyRepoTests()
        {
            string root = Path.Combine(RepoRoot(), "JunctionRules");
            string nav = File.ReadAllText(Path.Combine(root, "Vanilla", "JRCarNavigationSystem.cs"));
            string it = File.ReadAllText(Path.Combine(root, "Vanilla", "JRCarLaneSpeedIterator.cs"));
            string s59 = WhyBlock(nav, "S2-59"), s21 = WhyBlock(nav, "S2-21"), s23 = WhyBlock(nav, "S2-23");
            Check(s59 != null && s59.Contains("if (m_JRWhyEntity != Entity.Null && entity2 == m_JRWhyEntity)") && s59.Contains("JRWhy.Write("), "S2-59: only the tracked car writes the slot");
            Check(s21 != null && s21.Contains("bool jrWhyOn = m_JRWhyEntity != Entity.Null && entity == m_JRWhyEntity;"), "S2-21: tracked flag is a LOCAL from one entity compare (Entity.Null = off for every car; no job field written per car)");
            Check(s23 != null && s23.Contains("m_JRWhyEntity = JunctionRules.Stage2.JRStage2Bridge.WhyEntity"), "S2-23: the job gets the tracked entity from the bridge");
            Check(Regex.Matches(nav + it, @"JRWhy\.Write\(").Count == 1, "JRWhy.Write is called once in the copy (S2-59)");
            Check(!Regex.IsMatch(nav, @"public (bool|float|JunctionRules\.Core\.WhyTrack) m_JRWhy"), "no per-car why state in job fields (the record lives in the one-element scratch)");
            // every recording line in the copy is behind the tracked-car flag
            var bad = new List<string>();
            foreach (string text in new[] { nav, it })
            {
                string[] lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string ln = lines[i];
                    bool act = ln.Contains("Why") && (ln.Contains(".Bind(") || ln.Contains(".Offer(") || ln.Contains(".Settle(") || ln.Contains(".Begin(") || ln.Contains("JRWhy.Write(")
                                                      || ln.Contains("JRWhy.Ref(m_JRWhyScratch) =") || ln.Contains(".BeforeSteer ="));
                    if (!act) continue;
                    bool guarded = ln.Contains("if (m_WhyOn") || ln.Contains("if (jrWhyOn") || ln.Contains("if (m_JRWhyEntity != Entity.Null && entity2 == m_JRWhyEntity)");
                    for (int k = 1; k <= 4 && !guarded && i - k >= 0; k++) guarded = lines[i - k].Contains("if (m_WhyOn") || lines[i - k].Contains("if (jrWhyOn") || lines[i - k].Contains("if (m_JRWhyEntity != Entity.Null && entity2 == m_JRWhyEntity)");
                    if (!guarded) bad.Add(ln.Trim());
                }
            }
            Check(bad.Count == 0, "every why-record line in the copy is behind m_WhyOn / jrWhyOn" + (bad.Count > 0 ? ": " + bad[0] : ""));
            // the hook side records only for the tracked car; the bridge hands Entity.Null when nothing is tracked
            string why = StripComments(File.ReadAllText(Path.Combine(root, "Stage2", "JRWhy.cs")));
            Check(Regex.Matches(why, @"private void WhyNote(Room|Scan|Exit)?\([^)]*\)\s*\{\s*if \(!m_WhyOn\) return;").Count == 4, "WhyNote / WhyNoteRoom / WhyNoteScan / WhyNoteExit return at once unless tracked");
            // JR-T-025b (A3): an exit / drive hold whose hook named no rule reads as jr_other, not as a specific rule
            Check(!(nav + it).Contains("WhyRuleOr(JunctionRules.Core.WhyRule.JrExitGiveWay)") && Regex.Matches(nav + it, @"WhyRuleOr\(JunctionRules\.Core\.WhyRule\.JrOther\)").Count == 5,
                  "exit holds (S2-29/30/32/35/36): fallback jr_other when the hook named no rule");
            string br = StripComments(File.ReadAllText(Path.Combine(root, "Stage2", "JRStage2BridgeWhy.cs")));
            Check(br.Contains("WhyEntity = car == 0 ? Entity.Null") && br.Contains("if (car == 0) return;"), "bridge: nothing tracked = Entity.Null to the job, no slot read");
            Check(br.Contains("em.HasComponent<Game.Vehicles.CarNavigation>(sel)"), "bridge: only a selected car (CarNavigation) counts as a selection");
            string api = StripComments(File.ReadAllText(Path.Combine(root, "Api", "JunctionRulesApi.cs")));
            Check(api.Contains("public static string WhySlowed(string argsJson) => Run(\"why_slowed\", argsJson, WhySlowedCore);"), "API: WhySlowed in v1 style (Run wrapper)");
            Check(!api.Contains("Stage2") && !api.Contains("Vanilla"), "API talks to the copy only through Core (WhyHandoff)");
            // JR-T-025b (M1): nothing survives a city load / exit / the copy's release
            Check(api.Contains("WhyHandoff.StepsAtApiCall") && !api.Contains("s_WhyStepsAtCall"), "API: its last-call step count lives in WhyHandoff (reset with it)");
            Check(Regex.IsMatch(br, @"private static void ReleaseWhy\(\)\s*\{[^}]*WhyHandoff\.Reset\(\);"), "bridge: ReleaseWhy resets the readout (WhyHandoff.Reset)");
            Check(br.Contains("if (s_WhyGeneration != WhyHandoff.Generation)") && br.Contains("s_WhySeenSeq = SlotSeqSeen();"), "bridge: after a reset the slot's old sample is marked seen (never fed to the new book)");
            string sw = StripComments(File.ReadAllText(Path.Combine(root, "Systems", "CarDrivingSwitchSystem.cs")));
            Check(Regex.IsMatch(sw, @"protected override void OnGameLoaded\([^)]*\)\s*\{[^}]*WhyHandoff\.Reset\(\);"), "OnGameLoaded (city load / leaving the city) resets the readout");
        }

        /// <summary>JR-T-025b (M1): WhyHandoff.Reset clears the API car, the step counts and the book (entity ids are reused across saves).</summary>
        private static void WhyResetTests()
        {
            WhyHandoff.Reset();
            int gen = WhyHandoff.Generation;
            WhyBook b = WhyHandoff.Book;
            WhyHandoff.ApiCar = WhyCar2;
            WhyHandoff.Steps = 40;
            WhyHandoff.StepsAtApiCall = 39;
            WhyHandoff.TrackedExists = true;
            b.Track(WhyCar, "selection");
            b.Observe(WhyS(2, WhyRule.StockCarAhead, WhyBlk), 0.0);
            b.Track(WhyCar2, "api");   // WhyCar's history goes to the earlier cars
            b.Observe(WhyS(4, WhyRule.JrBoxRoom, WhyBlk, car: WhyCar2), 1.0);
            Check(b.HistoryOf(WhyCar) != null && b.HistoryOf(WhyCar2) != null && b.HasCurrent, "before the reset: two cars with history and a current reading");

            WhyHandoff.Reset();
            WhyBook n = WhyHandoff.Book;
            Check(WhyHandoff.ApiCar == 0 && WhyHandoff.Steps == 0 && WhyHandoff.StepsAtApiCall == 0 && !WhyHandoff.TrackedExists, "reset: API car, step counts and the tracked flag cleared");
            Check(WhyHandoff.Generation == gen + 1, "reset: generation bumped (the bridge drops the slot's old sample)");
            Check(!ReferenceEquals(n, b) && n.Car == 0 && n.Source == "none" && !n.HasCurrent && n.Changes.Count == 0 && n.DroppedLines == 0, "reset: a fresh book (no car, no reading, no changes)");
            Check(n.HistoryOf(WhyCar) == null && n.HistoryOf(WhyCar2) == null && !n.KnownCars.GetEnumerator().MoveNext(), "reset: no old city's history for a reused id");
            // a reused key in the new city starts empty (no TakePast of the old car's history)
            n.Track(WhyCar, "selection");
            Check(n.Changes.Count == 0 && !n.HasCurrent, "reused key after the reset: empty history");
            Check(WhyHandoff.Notes(0, WhyHandoff.Steps, n.HasCurrent, 0).Exists(x => x.Contains("has not run")), "reset: API notes say the driving has not run (no step yet in the new city)");
            WhyHandoff.Reset();
        }
    }
}
