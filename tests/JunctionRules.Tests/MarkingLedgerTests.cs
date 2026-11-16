using System;
using System.Collections.Generic;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// JR-T-013e (G2, #17 review F2/F3/F6): behaviour of the marking hide bookkeeping (Core/MarkingLedger.cs) on a fake
    /// entity world: strip before a save, restore after it, clear in every state, failure fallbacks.
    /// </summary>
    internal static partial class Program
    {
        private sealed class FakeMarkWorld : IMarkingWorld<int>
        {
            public readonly HashSet<int> Live = new HashSet<int>();
            public readonly HashSet<int> Deleted = new HashSet<int>();
            public readonly HashSet<int> Temp = new HashSet<int>();
            public readonly Dictionary<int, List<MarkingMask.Range>> Cuts = new Dictionary<int, List<MarkingMask.Range>>();
            public readonly HashSet<int> Hidden = new HashSet<int>();
            public readonly List<int> Rerenders = new List<int>();
            /// <summary>Entity -> number of RemoveCut calls that throw (int.MaxValue = always).</summary>
            public readonly Dictionary<int, int> FailRemoveCut = new Dictionary<int, int>();
            public readonly HashSet<int> FailEmptyCut = new HashSet<int>();
            public readonly HashSet<int> FailWriteCut = new HashSet<int>();
            public readonly HashSet<int> FailRemoveHidden = new HashSet<int>();

            public FakeMarkWorld(params int[] ids) { foreach (int i in ids) Live.Add(i); }

            public bool Exists(int e) => Live.Contains(e);
            public bool Alive(int e) => Live.Contains(e) && !Deleted.Contains(e);
            public bool IsTemp(int e) => Temp.Contains(e);
            public bool HasCut(int lane) => Cuts.ContainsKey(lane);

            public void WriteCut(int lane, IList<MarkingMask.Range> ranges)
            {
                if (FailWriteCut.Contains(lane)) throw new InvalidOperationException("write " + lane);
                Cuts[lane] = new List<MarkingMask.Range>(ranges);
                Rerenders.Add(lane);
            }

            public void RemoveCut(int lane)
            {
                if (FailRemoveCut.TryGetValue(lane, out int n) && n > 0)
                {
                    if (n != int.MaxValue) FailRemoveCut[lane] = n - 1;
                    throw new InvalidOperationException("remove " + lane);
                }
                Cuts.Remove(lane);
            }

            public void EmptyCut(int lane)
            {
                if (FailEmptyCut.Contains(lane)) throw new InvalidOperationException("empty " + lane);
                if (Cuts.ContainsKey(lane)) Cuts[lane] = new List<MarkingMask.Range>();
            }

            public bool HasHidden(int o) => Hidden.Contains(o);
            public void AddHidden(int o) { Hidden.Add(o); Rerenders.Add(o); }

            public void RemoveHidden(int o)
            {
                if (FailRemoveHidden.Contains(o)) throw new InvalidOperationException("unhide " + o);
                Hidden.Remove(o);
            }

            public void Rerender(int e) => Rerenders.Add(e);

            /// <summary>True when the lane is drawn cut (a non-empty cut list).</summary>
            public bool DrawnCut(int lane) => Cuts.TryGetValue(lane, out var r) && r.Count > 0;
        }

        private static readonly List<MarkingMask.Range> s_R1 = new List<MarkingMask.Range> { new MarkingMask.Range(0.25, 0.75) };
        private static readonly List<MarkingMask.Range> s_R2 = new List<MarkingMask.Range> { new MarkingMask.Range(0.1, 0.4) };

        /// <summary>Lanes 1, 2 cut, object 10 hidden; rerender log cleared.</summary>
        private static MarkingLedger<int> LedgerWithTwoLanes(FakeMarkWorld w)
        {
            var l = new MarkingLedger<int>();
            l.Sync(w, new Dictionary<int, List<MarkingMask.Range>> { { 1, s_R1 }, { 2, s_R2 } }, new List<int> { 10 });
            w.Rerenders.Clear();
            return l;
        }

        private static void MarkingLedgerTests()
        {
            // sync: cut + hide, each with a re-render
            {
                var w = new FakeMarkWorld(1, 2, 10);
                var l = new MarkingLedger<int>();
                l.Sync(w, new Dictionary<int, List<MarkingMask.Range>> { { 1, s_R1 }, { 2, s_R2 } }, new List<int> { 10 });
                Check(w.DrawnCut(1) && w.DrawnCut(2) && w.Hidden.Contains(10) && w.Rerenders.Count == 3 && l.LaneCount == 2 && l.ObjectCount == 1, "ML sync cuts lanes / hides objects, one re-render each");
                w.Rerenders.Clear();
                l.Sync(w, new Dictionary<int, List<MarkingMask.Range>> { { 1, s_R1 }, { 2, s_R2 } }, new List<int> { 10 });
                Check(w.Rerenders.Count == 0, "ML same wanted state: no writes, no re-render");
                l.Sync(w, new Dictionary<int, List<MarkingMask.Range>> { { 1, s_R1 } }, new List<int>());
                Check(!w.HasCut(2) && !w.Hidden.Contains(10) && w.Rerenders.Contains(2) && w.Rerenders.Contains(10) && l.LaneCount == 1 && l.ObjectCount == 0, "ML box removed: its lanes uncut / objects shown, with a re-render");
            }

            // strip -> save -> restore
            {
                var w = new FakeMarkWorld(1, 2, 10);
                var l = LedgerWithTwoLanes(w);
                MarkingStripResult r = l.StripForSave(w);
                Check(!r.Skipped && r.Clean && r.Retried == 0 && r.Lanes == 2 && r.Objects == 1, "ML strip clean: " + r.Lanes + "/" + r.Objects);
                Check(w.Cuts.Count == 0 && w.Hidden.Count == 0 && w.Rerenders.Count == 0 && l.Stripped, "ML strip: no cut list / hide tag left for the serializer, no re-render (no flicker)");
                Check(l.StripForSave(w).Skipped, "ML a second strip in the same save does nothing");
                l.Tick(w);
                Check(w.DrawnCut(1) && w.DrawnCut(2) && w.Hidden.Contains(10) && w.Rerenders.Count == 3 && !l.Stripped, "ML next frame: cuts / hides back, re-rendered");
            }

            // restore leaves a lane someone else cut meanwhile, drops gone entities
            {
                var w = new FakeMarkWorld(1, 2, 10);
                var l = LedgerWithTwoLanes(w);
                l.StripForSave(w);
                w.Cuts[2] = new List<MarkingMask.Range> { new MarkingMask.Range(0.9, 1) };   // a crosswalk cut, say
                w.Deleted.Add(1);
                l.Tick(w);
                Check(!l.TracksLane(1) && !l.TracksLane(2) && w.Cuts[2][0].Min == 0.9, "ML restore: foreign cut left alone and forgotten, deleted lane forgotten");
            }

            // F3: clear while stripped re-renders and drops the restore
            {
                var w = new FakeMarkWorld(1, 2, 10);
                var l = LedgerWithTwoLanes(w);
                l.StripForSave(w);
                w.Cuts[2] = new List<MarkingMask.Range> { new MarkingMask.Range(0.9, 1) };   // foreign, added during the save
                int failed = l.Clear(w, out Exception first);
                Check(failed == 0 && first == null && w.Rerenders.Contains(1) && w.Rerenders.Contains(2) && w.Rerenders.Contains(10), "ML F3 clear while stripped queues a re-render of every tracked entity");
                Check(!l.Stripped && l.IsEmpty && w.HasCut(2), "ML F3 clear while stripped: restore dropped, the foreign cut is not taken");
                w.Rerenders.Clear();
                l.Tick(w);
                Check(!w.HasCut(1) && !w.Hidden.Contains(10) && w.Rerenders.Count == 0, "ML F3 nothing is put back after the clear");
            }

            // clear (normal)
            {
                var w = new FakeMarkWorld(1, 2, 10);
                var l = LedgerWithTwoLanes(w);
                int failed = l.Clear(w, out _);
                Check(failed == 0 && w.Cuts.Count == 0 && w.Hidden.Count == 0 && w.Rerenders.Count == 3 && l.IsEmpty, "ML clear removes ours with a re-render each");
            }

            // clear: one failure never stops the rest, never throws
            {
                var w = new FakeMarkWorld(1, 2, 10);
                var l = LedgerWithTwoLanes(w);
                w.FailRemoveCut[1] = int.MaxValue;
                int failed = l.Clear(w, out Exception first);
                Check(failed == 1 && first != null && !w.HasCut(2) && !w.Hidden.Contains(10) && l.IsEmpty, "ML clear: a failing lane is counted, the rest still cleared");
            }

            // F2: strip failure once -> retry succeeds
            {
                var w = new FakeMarkWorld(1, 2, 10);
                var l = LedgerWithTwoLanes(w);
                w.FailRemoveCut[1] = 1;
                MarkingStripResult r = l.StripForSave(w);
                Check(r.Clean && r.Retried == 1 && r.Emptied == 0 && r.First != null && w.Cuts.Count == 0, "ML F2 strip: failed lane retried, save clean, error kept for the log");
            }

            // F2: removal keeps failing -> emptied in place (saved empty = nothing cut); restore treats it as ours
            {
                var w = new FakeMarkWorld(1, 2, 10);
                var l = LedgerWithTwoLanes(w);
                w.FailRemoveCut[1] = int.MaxValue;
                MarkingStripResult r = l.StripForSave(w);
                Check(r.Clean && r.Emptied == 1 && r.StillCut == 0 && w.HasCut(1) && !w.DrawnCut(1) && !w.HasCut(2), "ML F2 strip fallback: our cut list emptied in place, nothing cut in the save");
                l.Tick(w);
                Check(l.TracksLane(1) && w.DrawnCut(1) && w.DrawnCut(2), "ML F2 restore writes the emptied lane back (ours, not foreign)");
            }

            // F2: emptied while stripped, then clear: the empty buffer of ours is taken off
            {
                var w = new FakeMarkWorld(1, 2, 10);
                var l = LedgerWithTwoLanes(w);
                w.FailRemoveCut[1] = 2;   // both strip tries fail, the clear's succeeds
                l.StripForSave(w);
                l.Clear(w, out _);
                Check(!w.HasCut(1) && w.Rerenders.Contains(1), "ML F2 clear after a fallback strip removes our emptied list");
            }

            // F2: everything fails -> counted (caller logs CRITICAL), never thrown
            {
                var w = new FakeMarkWorld(1, 2, 10);
                var l = LedgerWithTwoLanes(w);
                w.FailRemoveCut[1] = int.MaxValue;
                w.FailEmptyCut.Add(1);
                w.FailRemoveHidden.Add(10);
                MarkingStripResult r = l.StripForSave(w);
                Check(!r.Clean && r.StillCut == 1 && r.HiddenLeft == 1 && r.First != null && !w.HasCut(2), "ML F2 strip: lane that stays cut and object that stays hidden are counted, rest stripped");
            }

            // tracked before the write: a failing write is still in the clear's reach
            {
                var w = new FakeMarkWorld(1, 2);
                var l = new MarkingLedger<int>();
                w.FailWriteCut.Add(2);
                bool threw = false;
                try { l.Sync(w, new Dictionary<int, List<MarkingMask.Range>> { { 1, s_R1 }, { 2, s_R2 } }, new List<int>()); }
                catch (InvalidOperationException) { threw = true; }
                Check(threw && l.TracksLane(2), "ML a failed cut write stays tracked");
                l.Clear(w, out _);
                Check(w.Rerenders.Contains(2) && !w.HasCut(1), "ML ... and the clear re-renders it");
            }

            // sync while stripped puts back first, then applies the change
            {
                var w = new FakeMarkWorld(1, 2, 10);
                var l = LedgerWithTwoLanes(w);
                l.StripForSave(w);
                l.Sync(w, new Dictionary<int, List<MarkingMask.Range>> { { 1, s_R2 } }, new List<int> { 10 });
                Check(!l.Stripped && w.DrawnCut(1) && w.Cuts[1][0].Min == 0.1 && !w.HasCut(2) && w.Hidden.Contains(10), "ML sync after a strip: restored, then the new wanted state");
            }

            // tick keeps objects hidden: re-hides after a tool, not while Temp, forgets deleted
            {
                var w = new FakeMarkWorld(10, 11);
                var l = new MarkingLedger<int>();
                l.Sync(w, new Dictionary<int, List<MarkingMask.Range>>(), new List<int> { 10, 11 });
                w.Hidden.Clear();
                w.Temp.Add(11);
                l.Tick(w);
                Check(w.Hidden.Contains(10) && !w.Hidden.Contains(11), "ML tick re-hides an object a tool un-hid, not one in a tool preview (Temp)");
                w.Deleted.Add(10);
                l.Tick(w);
                Check(!l.TracksObject(10) && l.TracksObject(11), "ML tick forgets a deleted object");
            }

            // forget (new city) and empty strip
            {
                var w = new FakeMarkWorld(1, 2, 10);
                var l = LedgerWithTwoLanes(w);
                l.Forget();
                Check(l.IsEmpty && !l.Stripped && l.StripForSave(w).Skipped, "ML forget drops everything; strip with nothing hidden is skipped");
            }
        }
    }
}
