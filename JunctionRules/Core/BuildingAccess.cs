using System;
using System.Collections.Generic;

namespace JunctionRules.Core
{
    // JR-T-014c (G9 + G10, owner 17 Nov 2026): a building's car ACCESSES as box targets: exits (cars leave), entrances (cars
    // go in) and two-way accesses (both). Game-independent rules; Boxes/BoxFacts.cs reads the game data (sub-net node car
    // lane flow, spawn locations) into ExitCandidate rows.
    //
    // JR-T-021 (owner 19 Nov 2026, replaces G9's derivation): every car access (exit, entrance, two-way) is boxed on its
    // own: the owner picks the access, and only that access gets a box. An entrance box is an ExitKind.Entrance key in the
    // building's saved record (same v2 record, a new key kind). Saves from 1.0.1 and older had G9 entrance boxes DERIVED at
    // every entry-only entrance of a boxed building; the first load with this build turns each of those into a saved
    // entrance key once (MigrateEntrances, versioned by BoxRecordFlags.EntrancesExplicit), and drops the lot-front marker
    // keys that only marked the building "boxed".
    // G10: boxes only at a REAL car access. The 0.4.3 Fallback (Building.m_RoadEdge + m_CurvePosition, the lot-front
    // middle, used when the game data shows no car exit) is never a box target and is never drawn or obeyed. A saved
    // Fallback key that matches no real exit and is not dropped by the migration (no entrance to stand for) stays in the
    // save untouched, draws nothing and is offered by the tool for removal.

    /// <summary>Which way cars use one access of a building.</summary>
    [Flags]
    public enum AccessWay : byte
    {
        None = 0,
        /// <summary>Cars leave the building here (exit).</summary>
        Out = 1,
        /// <summary>Cars go into the building here (entrance).</summary>
        In = 2,
        Both = Out | In,
    }

    /// <summary>What happens to a saved Fallback key now that the fallback is never drawn (G10).</summary>
    public enum SavedFallbackFate
    {
        /// <summary>The key re-keys to a real exit within ExitRules.KeyMatch (the paint then saves that exit's key, as 0.4.3).</summary>
        Rekey = 0,
        /// <summary>Kept untouched as a 1.0.1 "building is boxed" marker the migration did not drop (no entrance made explicit); not drawn.</summary>
        Marker = 1,
        /// <summary>Kept untouched; the building has no real car access: nothing drawn or obeyed (removable with the tool).</summary>
        NoAccess = 2,
    }

    public static class AccessRules
    {
        /// <summary>
        /// The way of a sub-net node from its car lanes at the node: a lane flowing TOWARD the node = cars leave (Out), a lane
        /// flowing AWAY from it = cars go in (In). LaneSystem.cs 2146-2216: sourceBuffer = lanes flowing into the node (leave
        /// the building), targetBuffer = lanes flowing out of it (enter the building).
        /// </summary>
        public static AccessWay Way(bool carOut, bool carIn) => (carOut ? AccessWay.Out : AccessWay.None) | (carIn ? AccessWay.In : AccessWay.None);

        /// <summary>
        /// G10 box targets of a building: its real exits and its entry-only entrances; Fallback rows are dropped (never a
        /// target). Empty with 'why' when the building has no real car access.
        /// </summary>
        public static List<ExitCandidate> Targets(IList<ExitCandidate> exits, IList<ExitCandidate> entrances, out string why)
        {
            var r = new List<ExitCandidate>();
            if (exits != null) foreach (var e in exits) if (e.Kind != ExitKind.Fallback && e.Kind != ExitKind.None) r.Add(e);
            if (entrances != null) foreach (var e in entrances) if (e.Kind != ExitKind.Fallback && e.Kind != ExitKind.None) r.Add(e);
            why = r.Count == 0 ? "no real car exit or entrance in the game data (no box at a lot-front fallback point)" : null;
            return r;
        }

        /// <summary>A saved Fallback key that ExitRules.Match did not re-key: marker or no access (G10).</summary>
        public static SavedFallbackFate Fallback(bool rekeyed, int realExits, int entrances)
        {
            if (rekeyed) return SavedFallbackFate.Rekey;
            return realExits + entrances > 0 ? SavedFallbackFate.Marker : SavedFallbackFate.NoAccess;
        }

        /// <summary>1.0.1 G9 rule (used only by the migration): the building's entrances counted as boxed when its component held any key (exit or marker) or was a legacy box.</summary>
        public static bool EntrancesBoxed(int savedKeys, bool legacy) => savedKeys > 0 || legacy;

        /// <summary>
        /// 1.0.1 G9 derivation (used only by the migration): one entrance box per entry-only access; an entrance on the same
        /// road within ExitRules.SameExitAlong of a boxed exit (or of another entrance) is the same place and adds no box of its
        /// own. Returns indices into 'entrances'.
        /// </summary>
        public static List<int> EntranceBoxes(IList<ExitCandidate> entrances, IList<ExitCandidate> boxedExits)
        {
            var r = new List<int>();
            if (entrances == null) return r;
            for (int i = 0; i < entrances.Count; i++)
            {
                ExitCandidate e = entrances[i];
                if (e.Kind != ExitKind.Entrance) continue;
                bool dup = false;
                if (boxedExits != null)
                    foreach (var x in boxedExits)
                        dup |= x.Kind != ExitKind.Junction && x.RoadId == e.RoadId && Math.Abs(x.AlongM - e.AlongM) < ExitRules.SameExitAlong;
                foreach (int k in r)
                    dup |= entrances[k].RoadId == e.RoadId && Math.Abs(entrances[k].AlongM - e.AlongM) < ExitRules.SameExitAlong;
                if (!dup) r.Add(i);
            }
            return r;
        }

        /// <summary>
        /// JR-T-021: the one-time migration of a building's saved keys from 1.0.1 (G9 derived entrances) to explicit entrance
        /// keys. 'saved' = the record's keys, 'exits' = the building's exits as BoxFacts reads them (the 1.0.1 paint's list),
        /// 'entrances' = its entry-only accesses. Every entrance 1.0.1 drew (EntrancesBoxed + EntranceBoxes over the exits the
        /// saved keys match) becomes an ExitKind.Entrance key unless one is already saved for it; then every Fallback key that
        /// matches no exit (a lot-front marker that only said "boxed") is dropped, but only when the building keeps at least
        /// one entrance key (its entrances are explicit). Exit keys are untouched. At most 'maxKeys' keys: entrances that do
        /// not fit are counted in NotFitted (the caller logs them). Pure; running it on its own result changes nothing.
        /// </summary>
        public static EntranceMigration MigrateEntrances(IList<ExitKey> saved, IList<ExitCandidate> exits, IList<ExitCandidate> entrances, int maxKeys)
        {
            var r = new EntranceMigration { Keys = new List<ExitKey>() };
            if (saved != null) r.Keys.AddRange(saved);
            if (!EntrancesBoxed(r.Keys.Count, false)) return r;
            var exitList = exits ?? new List<ExitCandidate>();
            var boxed = new List<ExitCandidate>();
            foreach (ExitKey k in r.Keys)
            {
                int i = ExitRules.Match(k, exitList);
                if (i >= 0) boxed.Add(exitList[i]);
            }
            var toAdd = new List<ExitKey>();
            if (entrances != null)
            {
                foreach (int i in EntranceBoxes(entrances, boxed))
                {
                    ExitCandidate e = entrances[i];
                    bool have = false;
                    foreach (ExitKey k in r.Keys) have |= k.ExitKind == ExitKind.Entrance && ExitKey.Distance(k, e.Key) <= ExitRules.KeyMatch;
                    if (!have) toAdd.Add(e.Key);
                }
            }
            bool anyEntranceKey = toAdd.Count > 0;
            foreach (ExitKey k in r.Keys) anyEntranceKey |= k.ExitKind == ExitKind.Entrance;
            if (anyEntranceKey)
            {
                for (int i = r.Keys.Count - 1; i >= 0; i--)
                {
                    ExitKey k = r.Keys[i];
                    if (k.ExitKind != ExitKind.Fallback || ExitRules.Match(k, exitList) >= 0) continue;
                    r.Keys.RemoveAt(i);
                    r.MarkersDropped++;
                }
            }
            foreach (ExitKey k in toAdd)
            {
                if (r.Keys.Count >= maxKeys) { r.NotFitted++; continue; }
                r.Keys.Add(k);
                r.Added++;
            }
            return r;
        }
    }

    /// <summary>JR-T-021: what AccessRules.MigrateEntrances did to one building's keys.</summary>
    public struct EntranceMigration
    {
        /// <summary>The keys to save (exit keys first as saved, then the new entrance keys).</summary>
        public List<ExitKey> Keys;
        public int Added, MarkersDropped, NotFitted;
        public bool Changed => Added > 0 || MarkersDropped > 0;
    }

    /// <summary>
    /// G10 hover arrow (owner 17 Nov 2026): links the hovered box to the building access it belongs to, drawn from the box
    /// mouth on the kerb (the box outline point nearest the access) across the pavement to the access point (sub-net node,
    /// spawn location, gate). Exit = the head at the box end (pointing OUT of the building into the box); entrance = the head
    /// at the building end (IN from the box); both = a head at each end. A Fallback access (lot-front point, no real access)
    /// gets a dashed shaft. Plain segments for OverlayRenderSystem lines.
    /// </summary>
    public sealed class AccessArrow
    {
        public V3 BoxEnd, AccessEnd;
        public bool HeadAtBox, HeadAtAccess, Dashed;
        public List<(V3 A, V3 B)> Segments = new List<(V3 A, V3 B)>();

        public const double HeadLength = 1.2;
        public const double HeadHalfWidth = 0.7;
        public const double Dash = 0.8;
        public const double Gap = 0.6;

        /// <summary>Heads by way: Out = at the box, In = at the access, Both = both; None draws a plain line.</summary>
        public static void Heads(AccessWay way, out bool atBox, out bool atAccess)
        {
            atBox = (way & AccessWay.Out) != 0;
            atAccess = (way & AccessWay.In) != 0;
        }

        /// <summary>The point of the closed ring nearest p (the box mouth on the kerb facing the access).</summary>
        public static V3 NearestOnRing(IList<V3> ring, V2 p)
        {
            V3 best = ring != null && ring.Count > 0 ? ring[0] : default;
            double bestD = double.MaxValue;
            if (ring == null) return best;
            for (int i = 0; i < ring.Count; i++)
            {
                V3 a = ring[i], b = ring[(i + 1) % ring.Count];
                V2 d = b.XZ - a.XZ;
                double len2 = V2.Dot(d, d);
                double t = len2 > 1e-12 ? Math.Max(0, Math.Min(1, V2.Dot(p - a.XZ, d) / len2)) : 0;
                V2 q = a.XZ + d * t;
                double dist = V2.Distance(q, p);
                if (dist < bestD) { bestD = dist; best = new V3(q.X, a.Y + (b.Y - a.Y) * t, q.Z); }
            }
            return best;
        }

        /// <summary>The arrow from the box ring to the access point. Null when the two ends coincide (under 0.2 m apart).</summary>
        public static AccessArrow Build(IList<V3> boxRing, V3 access, AccessWay way, bool fallback)
        {
            if (boxRing == null || boxRing.Count < 3) return null;
            V3 boxEnd = NearestOnRing(boxRing, access.XZ);
            if (V2.Distance(boxEnd.XZ, access.XZ) < 0.2) return null;
            var r = new AccessArrow { BoxEnd = boxEnd, AccessEnd = access, Dashed = fallback };
            Heads(way, out r.HeadAtBox, out r.HeadAtAccess);
            if (fallback) Dashes(boxEnd, access, r.Segments);
            else r.Segments.Add((boxEnd, access));
            if (r.HeadAtBox) Head(access, boxEnd, r.Segments);
            if (r.HeadAtAccess) Head(boxEnd, access, r.Segments);
            return r;
        }

        /// <summary>Two head strokes at 'tip', pointing from 'from' to 'tip'.</summary>
        private static void Head(V3 from, V3 tip, List<(V3 A, V3 B)> into)
        {
            V2 dir = (tip.XZ - from.XZ).Normalized;
            double len = Math.Min(HeadLength, V2.Distance(from.XZ, tip.XZ) * 0.5);
            V2 back = tip.XZ - dir * len;
            V2 side = dir.Left * (HeadHalfWidth * len / HeadLength);
            into.Add((V3.FromXZ(back + side, tip.Y), tip));
            into.Add((V3.FromXZ(back - side, tip.Y), tip));
        }

        private static void Dashes(V3 a, V3 b, List<(V3 A, V3 B)> into)
        {
            double len = V2.Distance(a.XZ, b.XZ);
            for (double s = 0; s < len; s += Dash + Gap)
            {
                double e = Math.Min(len, s + Dash);
                into.Add((Lerp(a, b, s / len), Lerp(a, b, e / len)));
            }
        }

        private static V3 Lerp(V3 a, V3 b, double t) => new V3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
    }
}
