using System;
using System.Collections.Generic;

namespace JunctionRules.Core
{
    /// <summary>
    /// The entity side of the marking hide (Paint/MarkingHider.cs implements it on the EntityManager; the tests on a fake).
    /// Lanes carry our cut list (Game.Net.CutRange buffer), objects our hide tag (Game.Tools.Hidden); a re-render is the
    /// BatchesUpdated tag.
    /// </summary>
    internal interface IMarkingWorld<T>
    {
        /// <summary>The entity exists (deleted or not).</summary>
        bool Exists(T e);
        /// <summary>The entity exists and is not being deleted.</summary>
        bool Alive(T e);
        bool IsTemp(T e);
        bool HasCut(T lane);
        /// <summary>Adds or overwrites the cut list and queues a re-render.</summary>
        void WriteCut(T lane, IList<MarkingMask.Range> ranges);
        /// <summary>Takes the cut list off (structural change). No re-render.</summary>
        void RemoveCut(T lane);
        /// <summary>Empties the cut list in place (no structural change; an empty list cuts nothing). No re-render.</summary>
        void EmptyCut(T lane);
        bool HasHidden(T o);
        /// <summary>Adds the hide tag and queues a re-render.</summary>
        void AddHidden(T o);
        /// <summary>Takes the hide tag off. No re-render.</summary>
        void RemoveHidden(T o);
        /// <summary>Queues a re-render of the entity.</summary>
        void Rerender(T e);
    }

    /// <summary>Outcome of <see cref="MarkingLedger{T}.StripForSave"/>.</summary>
    internal struct MarkingStripResult
    {
        /// <summary>Nothing to strip (nothing hidden, or already stripped for this save).</summary>
        public bool Skipped;
        public int Lanes;
        public int Objects;
        /// <summary>Entities whose first take-off failed and were tried again.</summary>
        public int Retried;
        /// <summary>Lanes whose buffer could not be taken off but was emptied in place (saved empty = nothing cut).</summary>
        public int Emptied;
        /// <summary>Lanes that may still carry our cuts in the save (all three ways failed).</summary>
        public int StillCut;
        /// <summary>Objects whose Hidden could not be taken off (Hidden is not a saved type).</summary>
        public int HiddenLeft;
        public Exception First;

        public bool Clean => StillCut == 0 && HiddenLeft == 0;
    }

    /// <summary>
    /// JR-T-013e (G2): the bookkeeping of the marking hide, game-free so the strip / restore / clear order is tested
    /// (tests/JunctionRules.Tests/MarkingLedgerTests.cs). Tracks which lanes carry OUR cut list (with its ranges) and which
    /// objects carry OUR hide tag. Rules:
    /// - A lane / object is tracked BEFORE its write, so a failure half way still leaves it in Clear / StripForSave's reach.
    /// - StripForSave takes ours off without a re-render (no flicker during a save); the next Restore (Sync / Tick) puts
    ///   them back with a re-render. A lane that someone else cut meanwhile is dropped ("theirs now").
    /// - StripForSave never leaves a cut silently: failed lanes are retried, then emptied in place; the result counts what
    ///   is left so the caller logs it.
    /// - Clear always queues a re-render of every tracked entity; while stripped it only takes off what is still ours
    ///   (emptied buffers) and drops the pending restore, so the render never stays cut.
    /// </summary>
    internal sealed class MarkingLedger<T>
    {
        private readonly Dictionary<T, List<MarkingMask.Range>> m_Lanes = new Dictionary<T, List<MarkingMask.Range>>();
        private readonly HashSet<T> m_Objects = new HashSet<T>();
        /// <summary>Lanes whose cut list was emptied in place by a failed strip (the buffer left on is still ours).</summary>
        private readonly HashSet<T> m_Emptied = new HashSet<T>();
        private bool m_Stripped;

        public int LaneCount => m_Lanes.Count;
        public int ObjectCount => m_Objects.Count;
        public bool IsEmpty => m_Lanes.Count == 0 && m_Objects.Count == 0;
        public bool Stripped => m_Stripped;
        public bool TracksLane(T lane) => m_Lanes.ContainsKey(lane);
        public bool TracksObject(T o) => m_Objects.Contains(o);
        public IEnumerable<T> Lanes => m_Lanes.Keys;
        public IEnumerable<T> Objects => m_Objects;

        /// <summary>Makes ours exactly the wanted cuts / hides (puts a stripped state back first). Throws on a write failure.</summary>
        public void Sync(IMarkingWorld<T> w, IDictionary<T, List<MarkingMask.Range>> wantLanes, ICollection<T> wantObjects)
        {
            RestoreIfStripped(w);
            foreach (T lane in new List<T>(m_Lanes.Keys))
            {
                if (!wantLanes.ContainsKey(lane)) { UncutLane(w, lane); m_Lanes.Remove(lane); }
            }
            foreach (var kv in wantLanes)
            {
                if (m_Lanes.TryGetValue(kv.Key, out var have) && MarkingMask.SameRanges(have, kv.Value) && w.HasCut(kv.Key)) continue;
                m_Lanes[kv.Key] = kv.Value;   // tracked BEFORE the write
                w.WriteCut(kv.Key, kv.Value);
            }
            foreach (T o in new List<T>(m_Objects))
            {
                if (!wantObjects.Contains(o)) { Unhide(w, o); m_Objects.Remove(o); }
            }
            foreach (T o in wantObjects)
            {
                m_Objects.Add(o);   // tracked BEFORE the write
                if (!w.HasHidden(o)) w.AddHidden(o);
            }
        }

        /// <summary>Once per frame: puts a stripped state back; re-hides an object a tool un-hid; forgets gone objects.</summary>
        public void Tick(IMarkingWorld<T> w)
        {
            RestoreIfStripped(w);
            if (m_Objects.Count == 0) return;
            List<T> gone = null;
            foreach (T o in m_Objects)
            {
                if (!w.Alive(o)) { (gone ??= new List<T>()).Add(o); continue; }
                if (!w.HasHidden(o) && !w.IsTemp(o)) w.AddHidden(o);
            }
            if (gone != null) foreach (T o in gone) m_Objects.Remove(o);
        }

        /// <summary>
        /// Removes everything of ours and queues a re-render of every tracked entity (also while stripped: the save took the
        /// buffers off without one, so the render is still cut). Drops a pending restore. Never throws; returns the failures.
        /// </summary>
        public int Clear(IMarkingWorld<T> w, out Exception first)
        {
            first = null;
            int failed = 0;
            foreach (T lane in m_Lanes.Keys)
            {
                try
                {
                    if (!w.Alive(lane)) continue;
                    // stripped: the buffer still on is ours only if we emptied it; otherwise it is someone else's
                    if (w.HasCut(lane) && (!m_Stripped || m_Emptied.Contains(lane))) w.RemoveCut(lane);
                    w.Rerender(lane);
                }
                catch (Exception e) { failed++; first = first ?? e; }
            }
            foreach (T o in m_Objects)
            {
                try
                {
                    if (!w.Alive(o)) continue;
                    if (!m_Stripped && w.HasHidden(o)) w.RemoveHidden(o);
                    w.Rerender(o);
                }
                catch (Exception e) { failed++; first = first ?? e; }
            }
            Forget();
            return failed;
        }

        /// <summary>A new city: the old entities are gone; just forget them.</summary>
        public void Forget()
        {
            m_Lanes.Clear();
            m_Objects.Clear();
            m_Emptied.Clear();
            m_Stripped = false;
        }

        /// <summary>
        /// Right before the city is serialized: takes our cut lists / hide tags off (no re-render). One entity at a time,
        /// failures retried once, then (lanes) emptied in place. Never throws.
        /// </summary>
        public MarkingStripResult StripForSave(IMarkingWorld<T> w)
        {
            var r = new MarkingStripResult();
            if (m_Stripped || IsEmpty) { r.Skipped = true; return r; }
            m_Stripped = true;   // set first: whatever happens below, the next Restore puts back what is still ours
            r.Lanes = m_Lanes.Count;
            r.Objects = m_Objects.Count;
            var failedLanes = new List<T>();
            var failedObjects = new List<T>();
            foreach (T lane in m_Lanes.Keys)
            {
                try { if (w.Exists(lane) && w.HasCut(lane)) w.RemoveCut(lane); }
                catch (Exception e) { failedLanes.Add(lane); r.First = r.First ?? e; }
            }
            foreach (T o in m_Objects)
            {
                try { if (w.Exists(o) && w.HasHidden(o)) w.RemoveHidden(o); }
                catch (Exception e) { failedObjects.Add(o); r.First = r.First ?? e; }
            }
            r.Retried = failedLanes.Count + failedObjects.Count;
            foreach (T lane in failedLanes)
            {
                try { if (w.Exists(lane) && w.HasCut(lane)) w.RemoveCut(lane); continue; }
                catch (Exception e) { r.First = r.First ?? e; }
                try { if (!w.Exists(lane)) continue; w.EmptyCut(lane); m_Emptied.Add(lane); r.Emptied++; }
                catch (Exception e) { r.StillCut++; r.First = r.First ?? e; }
            }
            foreach (T o in failedObjects)
            {
                try { if (w.Exists(o) && w.HasHidden(o)) w.RemoveHidden(o); }
                catch (Exception e) { r.HiddenLeft++; r.First = r.First ?? e; }
            }
            return r;
        }

        private void RestoreIfStripped(IMarkingWorld<T> w)
        {
            if (!m_Stripped) return;
            m_Stripped = false;
            foreach (var kv in new List<KeyValuePair<T, List<MarkingMask.Range>>>(m_Lanes))
            {
                if (!w.Alive(kv.Key)) { m_Lanes.Remove(kv.Key); continue; }
                if (w.HasCut(kv.Key) && !m_Emptied.Contains(kv.Key)) { m_Lanes.Remove(kv.Key); continue; }   // someone else's now
                w.WriteCut(kv.Key, kv.Value);   // with a re-render: re-instanced meanwhile (LOD / camera)? rebuild to be sure
            }
            m_Emptied.Clear();
            foreach (T o in new List<T>(m_Objects))
            {
                if (!w.Alive(o)) { m_Objects.Remove(o); continue; }
                if (!w.HasHidden(o)) w.AddHidden(o);
            }
        }

        private static void UncutLane(IMarkingWorld<T> w, T lane)
        {
            if (!w.Alive(lane)) return;
            if (w.HasCut(lane)) w.RemoveCut(lane);
            w.Rerender(lane);
        }

        private static void Unhide(IMarkingWorld<T> w, T o)
        {
            if (!w.Alive(o)) return;
            if (w.HasHidden(o)) w.RemoveHidden(o);
            w.Rerender(o);
        }
    }
}
