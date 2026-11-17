using System;
using System.Collections.Generic;

namespace JunctionRules.Core
{
    /// <summary>
    /// 0.4.3-probe (JR-T-007c driving): one boxed building car exit as the driving rule sees it: the public road edge
    /// (entity key, SortedKeys.KeyOf) and the exit's curve position on that edge (the edge curve's own t, as
    /// ConnectedNode.m_CurvePosition / Building.m_CurvePosition). Exits through the building's own access road are
    /// junction boxes on the node (YellowBoxJunction) and never bands.
    /// </summary>
    public struct ExitBand
    {
        public long EdgeKey;
        public float T;
        /// <summary>
        /// 0.4.4 (JR-T-011): the carriageways the exit's box covers (Core/MedianSplit: bit = NetCompositionLane.m_Carriageway;
        /// MedianSplit.AllCarriageways on an undivided road). The band holds only lanes whose CarLane.m_CarriagewayGroup
        /// low byte is in the mask: on a divided road the lanes on the building's side of the median.
        /// </summary>
        public uint Carriageways;

        /// <summary>
        /// 0.4.10 (JR-T-016, G18 / G19): the PAINTED window's two end points on the edge's centre curve (world x / z; the
        /// window of Boxes/BoxShapes.Entrance, the joint window when boxes are built as one, Core/ExitBoxWindows). The job
        /// projects them onto each lane (Core/LaneWindow.cs): the band is exactly the painted box along the lane. A band
        /// without a window (<see cref="HasWindow"/> false) is not driven (0.4.3-0.4.9 used t + the box length here).
        /// </summary>
        public float X0, Z0, X1, Z1;
        public bool HasWindow;

        public ExitBand(long edgeKey, float t) { EdgeKey = edgeKey; T = t; Carriageways = MedianSplit.AllCarriageways; X0 = Z0 = X1 = Z1 = 0f; HasWindow = false; }
        public ExitBand(long edgeKey, float t, uint carriageways) { EdgeKey = edgeKey; T = t; Carriageways = carriageways; X0 = Z0 = X1 = Z1 = 0f; HasWindow = false; }

        /// <summary>0.4.10: a band over the painted window [(x0, z0), (x1, z1)] (t = where it sorts on the edge: its middle).</summary>
        public ExitBand(long edgeKey, float t, uint carriageways, float x0, float z0, float x1, float z1)
        {
            EdgeKey = edgeKey; T = t; Carriageways = carriageways; X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; HasWindow = true;
        }

        /// <summary>0.4.10: the same window (members of one joint box carry the same one: kept once).</summary>
        public bool SameWindow(ExitBand o) =>
            HasWindow == o.HasWindow && X0 == o.X0 && Z0 == o.Z0 && X1 == o.X1 && Z1 == o.Z1;
    }

    /// <summary>
    /// 0.4.3-probe: which saved exit boxes of a building become driving bands, and the hand-over list (sorted by edge,
    /// then t). Replaces 0.4.2's one band per building at Building.m_RoadEdge + m_CurvePosition (the lot-front middle).
    /// Game-independent; Boxes/BoxFacts.BoxedExitBands reads the game data, Systems/CarDrivingSwitchSystem.cs publishes
    /// (BoxEdgeHandoff), Stage2/JRBoxEdges.cs carries the list into the job by value.
    /// </summary>
    public static class ExitBands
    {
        /// <summary>Most bands the job carries (Stage2/JRBoxEdges: FixedList512Bytes&lt;long&gt; keys + FixedList512Bytes&lt;float&gt; t).</summary>
        public const int Capacity = 63;

        /// <summary>Two bands on the same edge closer than this (curve t) and on the same carriageways are one band.</summary>
        public const float SameT = 1e-4f;

        /// <summary>
        /// 0.4.4 (JR-T-011): bands on one lane whose ends are at most this far apart (metres) are one band (the paint draws
        /// overlapping / touching exit boxes as one box, Core/BoxMerge.cs). 0.4.9 (G14, owner 17 Nov: two Alderbrook Street
        /// boxes ~0.5-1 m apart stayed two boxes): 0.1 -> <see cref="MergeGapMetres"/>, the same gap the paint now closes
        /// (Core/ExitBoxWindows), so what is drawn as one box is driven as one box.
        /// </summary>
        public const float TouchMetres = MergeGapMetres;

        /// <summary>
        /// 0.4.9 (G14): exit / entrance boxes on one road (same carriageways) closer than this along it (m) are ONE box, in
        /// the paint and in driving (one constant). 0.4.10 (G20, owner 17 Nov 2026, No. 40 Alderwood: an entrance box and an
        /// exit box 2.8 m apart were drawn as two boxes with a strip of road between: "you said within 2 m it will be merged
        /// but this is not"): 2 m -> 6 m. A gap is only useful if a vehicle can wait in it: the shortest road vehicle in the
        /// city is a small car of about 4.2-4.5 m and the box rule wants it to stand clear of both boxes with the room margin
        /// (default 1 m, RoomScan.DefaultMargin) = about 5.5 m; a gap under 6 m holds no vehicle, so it is part of the box
        /// (the 0.4.9 driving rule already treated such boxes as one per vehicle). Never across a road marking or into a
        /// junction mouth margin (EntranceBox.WindowFits keeps them apart then).
        /// </summary>
        public const float MergeGapMetres = 6.0f;

        /// <summary>
        /// 0.4.9 (G14 / G15, UK yellow box: never stop with any part of the vehicle in a box): for one vehicle's own hold,
        /// two bands on its lane closer than its whole length (trailers incl.) plus the room margin are one band: held
        /// before the first, room needed past the last. 0.4.8 held a van turning into No. 40 Alderwood St before No. 40's
        /// entrance band with its body standing in No. 40's EXIT band 2.5 m upstream (the G15 lock), and let an eastbound
        /// artic enter the first of two Alderbrook boxes with room only in the gap before the second (G14 shot).
        /// </summary>
        public static float HoldTouchMetres(float vehicleLength, float roomMargin)
        {
            float own = (vehicleLength > 0f ? vehicleLength : 0f) + (roomMargin > 0f ? roomMargin : 0f);
            return own > TouchMetres ? own : TouchMetres;
        }

        /// <summary>
        /// 0.4.9: the bands of one lane (driving coordinates, sorted by start) chained the way the job does it (BoxHold /
        /// ExitBandBlocks): a band touching the previous within 'tol' (driving units) extends it. For tests and the doc.
        /// </summary>
        public static List<(float b0, float b1)> Chain(IList<(float b0, float b1)> bands, float tol)
        {
            var r = new List<(float b0, float b1)>();
            if (bands == null) return r;
            foreach (var b in bands)
            {
                int last = r.Count - 1;
                if (last >= 0 && Touch(r[last].b0, r[last].b1, b.b0, b.b1, tol)) { r[last] = (Math.Min(r[last].b0, b.b0), Math.Max(r[last].b1, b.b1)); continue; }
                r.Add(b);
            }
            return r;
        }

        /// <summary>
        /// 0.4.4 (JR-T-011): a band that reaches this close (metres) to the end of its lane, where the car's path enters a
        /// boxed junction, is one box with that junction box (the paint bridges the exit box's marking-margin gap to a
        /// junction box, BoxMerge JoinGap = margin + 0.1 m, margin at most 2 m): the band's room test then asks for room
        /// past the junction box (lanes of the boxed node count as inside the box).
        /// </summary>
        public const float JunctionJoinMetres = 2.5f;

        /// <summary>Two bands on one lane (driving coordinates) overlap or touch within 'tol' (driving units): one band.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static bool Touch(float a0, float a1, float b0, float b1, float tol) => b0 <= a1 + tol && a0 <= b1 + tol;

        /// <summary>
        /// Indices into 'exits' of the exits that carry a box and drive as a band: each saved key matched as the paint
        /// matches it (ExitRules.Match: same kind within KeyMatch, else any non-junction kind); a legacy (key-less
        /// 0.4.0-0.4.2) box takes the exit nearest its old access point (ExitRules.Migrate), as the paint's load migration
        /// does. Junction exits and keys without an exit are left out ('unmatched' counts the keys without an exit).
        /// Distinct, in exit order.
        /// </summary>
        public static List<int> Boxed(IList<ExitKey> keys, bool legacy, V2 legacyAccess, IList<ExitCandidate> exits, out int unmatched)
        {
            unmatched = 0;
            var r = new List<int>();
            if (exits == null || exits.Count == 0)
            {
                unmatched = legacy ? 1 : keys?.Count ?? 0;
                return r;
            }
            var hit = new bool[exits.Count];
            if (legacy)
            {
                int i = ExitRules.Migrate(legacyAccess, exits, out _);
                if (i < 0) unmatched++;
                else hit[i] = true;
            }
            else if (keys != null)
            {
                foreach (ExitKey k in keys)
                {
                    int i = ExitRules.Match(k, exits);
                    if (i < 0) unmatched++;
                    else hit[i] = true;
                }
            }
            for (int i = 0; i < exits.Count; i++) if (hit[i] && exits[i].Kind != ExitKind.Junction) r.Add(i);
            return r;
        }

        /// <summary>
        /// The hand-over order: by edge key, then t; near-equal t on one edge merged. At most 'capacity' kept (the rest
        /// counted in 'dropped': those exits are not obeyed, logged by the caller).
        /// </summary>
        public static ExitBand[] Normalise(IEnumerable<ExitBand> bands, int capacity, out int dropped)
        {
            var all = new List<ExitBand>();
            if (bands != null) all.AddRange(bands);
            all.Sort((a, b) => a.EdgeKey != b.EdgeKey ? a.EdgeKey.CompareTo(b.EdgeKey) : a.T.CompareTo(b.T));
            var r = new List<ExitBand>(all.Count);
            foreach (ExitBand b in all)
            {
                if (float.IsNaN(b.T)) continue;
                bool dup = false;
                // 0.4.4: same edge + near-equal t kept once per carriageway mask (two sides of a median are two bands).
                // 0.4.10: and the same painted window (a joint box's members share one window and t: kept once).
                for (int k = r.Count - 1; k >= 0 && r[k].EdgeKey == b.EdgeKey && Math.Abs(r[k].T - b.T) < SameT; k--)
                    if (r[k].Carriageways == b.Carriageways && r[k].SameWindow(b)) { dup = true; break; }
                if (dup) continue;
                r.Add(b);
            }
            dropped = Math.Max(0, r.Count - capacity);
            if (dropped > 0) r.RemoveRange(capacity, dropped);
            return r.ToArray();
        }
    }
}
