using System.Runtime.CompilerServices;

namespace JunctionRules.Core
{
    /// <summary>
    /// 0.4.1 (JR-T-009): the overlaps of one car lane as path elements for the zebra sections, in driving coordinates
    /// (0 = where the car enters the lane). The job side wraps the game's LaneOverlap buffer (Stage2/JRWholeJunction.cs),
    /// the tests an array. Burst: generic constraint only.
    /// </summary>
    public interface IPathElements
    {
        int Count { get; }

        /// <summary>Element i: its <see cref="ZebraSections"/> kind bits (0 = not an element) and its range.</summary>
        void Get(int i, out int kind, out float start, out float end);
    }

    /// <summary>
    /// 0.4.1 (JR-T-009, owner rule 16 Nov 2026, "a car never stops ON a zebra"): zebras split a car's path through a node
    /// (a junction, or a mid-block crossing, which the game also builds as a node: Game.Net.LaneSystem
    /// CreateNodePedestrianLanes) into sections. A car enters a zebra only when the zebra is clear of people (the game's
    /// own pedestrian check, 0.3.2 never push), every vehicle conflict point from that zebra up to the next zebra or the
    /// end of the lane is clear (with the Whole junction rule: the section decision, taken before the zebra) and there is
    /// room for its whole length past the section (<see cref="RoomScan"/>). Vehicle points before the first zebra form
    /// section 0, decided at the entry spot W as before.
    ///
    /// Waiting spots: the start of each section (W, or a zebra's start). Owner rule (detail 2 + the ZCZ case, binding):
    /// a waiting spot is not a waiting spot if the whole vehicle (trailers included) does not fit there clear of
    /// everything behind it that it must not stand on: the zebras, and (Whole junction) the vehicle points. Such a zebra
    /// is merged into the section before it, chaining (Z C Z C Z with short gaps becomes one decision). The fit uses the
    /// game's stop point (the front a margin before the zebra), the same formula as 0.3.3 (Core/CrossingPoint.Fits).
    ///
    /// Pure arithmetic on plain values (Burst-compatible, no allocation).
    /// </summary>
    public static class ZebraSections
    {
        /// <summary>A vehicle point that places the entry spot W (WholePathRule.DefinesWaitingSpot: live road / track lane).</summary>
        public const int SpotPoint = 1;
        /// <summary>A vehicle point a waiting car must not stand on (Core/CrossingPoint.IsClearancePoint: red-held lanes too, end merges not).</summary>
        public const int Clearance = 2;
        /// <summary>A zebra: a crosswalk (PedestrianLaneFlags.Crosswalk) that is not Unsafe (a removed zebra leaves an Unsafe lane).</summary>
        public const int Zebra = 4;

        /// <summary>Most zebras one lane is read for (more are ignored: stock behaviour for them).</summary>
        public const int MaxZebras = 8;

        /// <summary>Game.Net.PedestrianLaneFlags values (mirrored; checked by a test against the decompile).</summary>
        public const int PedestrianUnsafe = 1;
        public const int PedestrianCrosswalk = 2;

        /// <summary>The pedestrian lane is a zebra (crosswalk, not the Unsafe lane a removed zebra leaves).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsZebra(int pedestrianLaneFlags) =>
            (pedestrianLaneFlags & PedestrianCrosswalk) != 0 && (pedestrianLaneFlags & PedestrianUnsafe) == 0;

        /// <summary>
        /// The sections of one lane. sections = the Whole junction rule is on (vehicle points split and fill sections);
        /// off (stock gap rule) only zebras count. atLine = the "wait at the stop line" variant (spot 0 at the lane start).
        /// laneLength / margin (the game's stop margin) / vehicleLength (whole vehicle) for the fit. Returns false when the
        /// lane has no zebra (nothing changes then).
        /// </summary>
        public static bool Build<T>(ref T list, bool sections, bool atLine, float laneLength, float margin, float vehicleLength, out ZebraLayout layout)
            where T : struct, IPathElements => Build(ref list, sections, atLine, laneLength, margin, vehicleLength, 2f, out layout);

        /// <summary>
        /// 0.4.2 (JR-T-010): as above with one extra waiting spot <paramref name="extraSpot"/> (driving coordinates; 2 = none):
        /// the yellow box turn spot of a car turning across oncoming traffic (Core/BoxDriving.cs). It becomes a section start
        /// without a zebra, but only where the zebra rule allows a car to stand: not on a zebra and with the whole vehicle
        /// clear of every zebra behind it (the zebra rule wins). Otherwise it is left out and the car decides at the spot
        /// before it. <see cref="ZebraLayout.ExtraIndex"/> tells where it went (-1 = left out).
        /// </summary>
        public static bool Build<T>(ref T list, bool sections, bool atLine, float laneLength, float margin, float vehicleLength, float extraSpot, out ZebraLayout layout)
            where T : struct, IPathElements => Build(ref list, sections, atLine, laneLength, margin, vehicleLength, extraSpot, -1f, out layout);

        /// <summary>
        /// 0.4.2 (owner refinement of the 0.4.1 sections, 16 Nov 2026): as above; with <paramref name="sections"/> and
        /// <paramref name="gapMargin"/> &gt;= 0 (the room margin) a zebra followed by a gap O big enough for the whole
        /// vehicle plus the stop margin plus <paramref name="gapMargin"/> before the first vehicle point C after it (Z O C C)
        /// gets a waiting spot at O (the start of that C, a section without a zebra): the car crosses Z when Z is clear of
        /// people and O is free (room past Z), waits at O, and the Cs are decided at O (the normal whole-junction decision).
        /// A section therefore ends at the next WAITING SPOT (a point where the whole vehicle stands clear of every zebra
        /// behind it), which is not always the next zebra. No such gap (Z C C): the 0.4.1 layout. Only right after a zebra:
        /// 0.2.3 has no other mid-junction waiting spots. <paramref name="gapMargin"/> &lt; 0 = off (the 0.4.1 layout).
        /// </summary>
        public static bool Build<T>(ref T list, bool sections, bool atLine, float laneLength, float margin, float vehicleLength, float extraSpot, float gapMargin, out ZebraLayout layout)
            where T : struct, IPathElements
        {
            layout = default;
            // Zebras in driving order (insertion sort into the fixed list).
            ZebraList z = default;
            float firstPoint = 2f;
            for (int i = 0; i < list.Count; i++)
            {
                list.Get(i, out int kind, out float s, out float e);
                if ((kind & Zebra) != 0) z.Insert(s, e);
                else if (sections && (kind & SpotPoint) != 0 && s < firstPoint) firstPoint = s;
            }
            if (z.Count == 0) return false;
            WaitingSpots spots = default;
            if (sections && firstPoint < z.Start(0) - WholePathRule.SpotEpsilon) spots.Add(firstPoint);
            for (int j = 0; j < z.Count; j++)
            {
                float zs = z.Start(j);
                if (spots.Count == 0)
                {
                    spots.Add(zs);
                    layout.SetZebra(0, zs);
                }
                else
                {
                    float prev = PreviousEnd(ref list, sections, zs);
                    if (CrossingPoint.Fits(laneLength, zs, prev, margin, vehicleLength) && spots.Add(zs))
                    {
                        layout.SetZebra(spots.Count - 1, zs);
                    }
                    else
                    {
                        // Detail 2 / ZCZ: no room to wait before this zebra clear of what lies behind: one decision with the section before.
                        layout.SetZebra(spots.Count - 1, zs);
                        layout.Merged++;
                    }
                }
                // 0.4.2 Z O C: a waiting gap after this zebra, before the next zebra.
                if (sections && gapMargin >= 0f)
                {
                    float o = NextPointAfter(ref list, zs);
                    float nextZebra = j + 1 < z.Count ? z.Start(j + 1) : 2f;
                    if (o < 1f && o < nextZebra - WholePathRule.SpotEpsilon && o > spots[spots.Count - 1] + WholePathRule.SpotEpsilon
                        && CrossingPoint.Fits(laneLength, o, PreviousEnd(ref list, true, o), margin + gapMargin, vehicleLength) && spots.Add(o))
                    {
                        layout.Gaps++;
                    }
                }
            }
            // The stop-line variant first (0.4.2: before the extra spot goes in; section membership is the same either way).
            if (atLine) spots[0] = 0f;
            layout.ExtraIndex = -1;
            if (extraSpot > WholePathRule.SpotEpsilon && extraSpot < 1f && !OnZebra(ref list, extraSpot)
                && CrossingPoint.Fits(laneLength, extraSpot, PreviousEnd(ref list, false, extraSpot), margin, vehicleLength))
            {
                layout.ExtraIndex = layout.InsertPlainSpot(ref spots, extraSpot);
            }
            // Section ends: the end of the last element (zebra, or with sections a vehicle point) of each section.
            for (int k = 0; k < spots.Count; k++) layout.SetEnd(k, spots[k]);
            for (int i = 0; i < list.Count; i++)
            {
                list.Get(i, out int kind, out float s, out float e);
                bool counts = (kind & Zebra) != 0 || (sections && (kind & (Clearance | SpotPoint)) != 0);
                if (!counts) continue;
                int k = spots.StretchOf(s);
                if (e > layout.SectionEnd(k)) layout.SetEnd(k, e);
            }
            layout.Spots = spots;
            layout.LastEnd = layout.SectionEnd(spots.Count - 1);
            return true;
        }

        /// <summary>0.4.2: start of the first vehicle point (not a zebra) that starts after <paramref name="u"/>; 2 = none.</summary>
        public static float NextPointAfter<T>(ref T list, float u) where T : struct, IPathElements
        {
            float next = 2f;
            for (int i = 0; i < list.Count; i++)
            {
                list.Get(i, out int kind, out float s, out _);
                if ((kind & SpotPoint) != 0 && (kind & Zebra) == 0 && s > u + WholePathRule.SpotEpsilon && s < next) next = s;
            }
            return next;
        }

        /// <summary>
        /// End of everything a car waiting before a zebra at <paramref name="zebraStart"/> must be clear of: every zebra and
        /// (sections) every vehicle clearance point that starts before it; 0 (the lane start) when there is none.
        /// </summary>
        public static float PreviousEnd<T>(ref T list, bool sections, float zebraStart) where T : struct, IPathElements
        {
            float prev = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                list.Get(i, out int kind, out float s, out float e);
                bool counts = (kind & Zebra) != 0 || (sections && (kind & Clearance) != 0);
                if (counts && s < zebraStart - WholePathRule.SpotEpsilon && e > prev) prev = e;
            }
            return prev;
        }

        /// <summary>0.4.2: <paramref name="u"/> lies on a zebra (inside its range, edges included).</summary>
        public static bool OnZebra<T>(ref T list, float u) where T : struct, IPathElements
        {
            for (int i = 0; i < list.Count; i++)
            {
                list.Get(i, out int kind, out float s, out float e);
                if ((kind & Zebra) != 0 && u >= s - WholePathRule.SpotEpsilon && u <= e + WholePathRule.SpotEpsilon) return true;
            }
            return false;
        }

        /// <summary>
        /// The room-past check is due for section <paramref name="next"/> (the next spot ahead of the car): the section
        /// holds a zebra and the car can still stop at its spot (the game's can-stop test). Room is measured from the
        /// section's end (<see cref="ZebraLayout.SectionEnd"/>): the car must fit past everything of the section.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RoomCheckDue(in ZebraLayout layout, int next, bool canStopBefore) =>
            next >= 0 && next < layout.Spots.Count && layout.HasZebra(next) && canStopBefore;

        /// <summary>
        /// 0.4.1 (JR-T-009-r, review finding 1): the decision for a zebra section is taken at the last point where the car
        /// can still stop before it. The car is before spot <paramref name="next"/> (and can stop there). When it could
        /// not stop in normal braking between that spot and the following zebra spot (the game's braking distance at its
        /// current speed, <see cref="StretchState.Braking"/>, longer than the distance between the two spots: passing the
        /// first spot, the car's can-stop test for the zebra fails), the zebra section is merged into the section of
        /// <paramref name="next"/> for this car-step: people on that zebra, its vehicle points and the room past it hold the
        /// car at <paramref name="next"/>, exactly as the "does not fit" merge. Chains (the distance is taken from the
        /// merged zebra's own spot to the one after it). Returns how many sections were merged.
        /// </summary>
        public static int MergeUnstoppable(ref ZebraLayout layout, int next, float braking, float laneLength)
        {
            if (next < 0 || next >= layout.Spots.Count) return 0;
            int merged = 0;
            float prev = layout.Spots[next];
            // 0.4.2: every following spot, not only zebra ones (a Z O gap spot or the box turn spot the car cannot stop at
            // any more is decided at the earlier spot too). 0.4.1 layouts had no other spots after the first.
            while (next + 1 < layout.Spots.Count)
            {
                float s = layout.Spots[next + 1];
                if (!(braking > laneLength * (s - prev))) break;
                prev = s;
                layout.MergeIntoPrevious(next + 1);
                merged++;
            }
            layout.EarlyMerged += merged;
            return merged;
        }

        /// <summary>
        /// 0.4.1 (JR-T-009-r): the section whose room past it is checked this car-step, or -1. It is the first zebra
        /// section at or after the car's next spot (the car's own section when that holds a zebra, else the one after it,
        /// e.g. CCCZ before W), when the car can still stop before that section's spot; the hold is at that spot. Checking
        /// the zebra section already before W keeps the car's speed such that it can still stop before the zebra while
        /// there is no room past it (it never passes W too fast to stop for the zebra on a full exit).
        /// </summary>
        public static int RoomSection(in ZebraLayout layout, in StretchState stretch)
        {
            int k = stretch.Next;
            if (k < 0 || k >= layout.Spots.Count) return -1;
            int j = layout.HasZebra(k) ? k : k + 1;
            if (!RoomCheckDue(layout, j, true)) return -1;
            return stretch.CanStopBefore(layout.Spots[j]) ? j : -1;
        }

        /// <summary>Fixed list of zebra ranges sorted by start.</summary>
        private struct ZebraList
        {
            private WaitingSpots m_S;
            private WaitingSpots m_E;
            public int Count => m_S.Count;

            public float Start(int i) => m_S[i];

            public void Insert(float s, float e)
            {
                if (m_S.Count >= MaxZebras) return;
                int n = m_S.Count;
                m_S.Add(s);
                m_E.Add(e);
                int i = n;
                while (i > 0 && m_S[i - 1] > s)
                {
                    m_S[i] = m_S[i - 1];
                    m_E[i] = m_E[i - 1];
                    i--;
                }
                m_S[i] = s;
                m_E[i] = e;
            }
        }
    }

    /// <summary>0.4.1: the sections of one lane (ZebraSections.Build). Fixed size, Burst-compatible.</summary>
    public struct ZebraLayout
    {
        /// <summary>The waiting spots (section starts), driving coordinates, nearest first.</summary>
        public WaitingSpots Spots;
        /// <summary>End (driving coordinates) of the last section: its last zebra or vehicle point. The room-past check measures from here.</summary>
        public float LastEnd;
        /// <summary>Zebras merged into the section before them (vehicle does not fit before them).</summary>
        public int Merged;
        /// <summary>0.4.2: index of the extra (yellow box turn) spot in <see cref="Spots"/>, -1 = none.</summary>
        public int ExtraIndex;
        /// <summary>0.4.2: Z O waiting gaps (sections without a zebra right after one).</summary>
        public int Gaps;
        /// <summary>0.4.1 (JR-T-009-r): zebra sections merged this car-step because the car cannot stop before them (<see cref="ZebraSections.MergeUnstoppable"/>).</summary>
        public int EarlyMerged;
        /// <summary>Bit k: section k holds a zebra.</summary>
        private int m_ZebraBits;
        /// <summary>First zebra start per section (8 sections; Capacity of WaitingSpots).</summary>
        private WaitingSpots m_FirstZebra;
        /// <summary>End per section (driving coordinates).</summary>
        private WaitingSpots m_End;

        /// <summary>End (driving coordinates) of section k: its last zebra or (sections) vehicle point; 2 when out of range.</summary>
        public readonly float SectionEnd(int section) => section >= 0 && section < m_End.Count ? m_End[section] : 2f;

        public void SetEnd(int section, float end)
        {
            if (section < 0 || section >= WaitingSpots.Capacity) return;
            while (m_End.Count <= section) m_End.Add(0f);
            m_End[section] = end;
        }

        public readonly bool HasZebra(int section) => section >= 0 && section < WaitingSpots.Capacity && (m_ZebraBits & (1 << section)) != 0;

        /// <summary>Start of the first zebra of section k (2 when it has none).</summary>
        public readonly float FirstZebra(int section) => HasZebra(section) ? m_FirstZebra[section] : 2f;

        public void SetZebra(int section, float start)
        {
            if (section < 0 || section >= WaitingSpots.Capacity || HasZebra(section)) return;
            m_ZebraBits |= 1 << section;
            WaitingSpots first = m_FirstZebra;
            first[section] = start;
            m_FirstZebra = first;
        }

        /// <summary>
        /// 0.4.2: inserts a section start without a zebra into <paramref name="spots"/> (sorted), shifting the zebra marks of
        /// the later sections. Before the section ends are set. Returns its index, or -1 when full or already a spot.
        /// </summary>
        public int InsertPlainSpot(ref WaitingSpots spots, float u)
        {
            if (spots.Count >= WaitingSpots.Capacity) return -1;
            int at = spots.Count;
            for (int i = 0; i < spots.Count; i++)
            {
                float d = spots[i] - u;
                if (d < WholePathRule.SpotEpsilon && d > -WholePathRule.SpotEpsilon) return -1;
                if (spots[i] > u) { at = i; break; }
            }
            spots.Add(0f);
            WaitingSpots first = m_FirstZebra;
            for (int i = spots.Count - 1; i > at; i--)
            {
                spots[i] = spots[i - 1];
                first[i] = first[i - 1];
            }
            spots[at] = u;
            first[at] = 2f;
            m_FirstZebra = first;
            int low = m_ZebraBits & ((1 << at) - 1);
            int high = (m_ZebraBits >> at) << (at + 1);
            m_ZebraBits = low | high;
            return at;
        }

        /// <summary>
        /// 0.4.1 (JR-T-009-r): section <paramref name="i"/> joins section i - 1 (its zebra, its end); later sections move
        /// down one place. Spots[i] is no longer a waiting spot.
        /// </summary>
        public void MergeIntoPrevious(int i)
        {
            if (i <= 0 || i >= Spots.Count) return;
            int k = i - 1;
            float end = SectionEnd(i);
            if (end > SectionEnd(k)) SetEnd(k, end);
            if (HasZebra(i)) SetZebra(k, FirstZebra(i));
            int n = Spots.Count;
            WaitingSpots spots = Spots, ends = m_End, first = m_FirstZebra;
            for (int t = i; t < n - 1; t++)
            {
                spots[t] = spots[t + 1];
                ends[t] = ends[t + 1];
                first[t] = first[t + 1];
            }
            spots.Count = n - 1;
            if (ends.Count > n - 1) ends.Count = n - 1;
            m_ZebraBits = (m_ZebraBits & ((1 << i) - 1)) | ((m_ZebraBits >> (i + 1)) << i);
            Spots = spots;
            m_End = ends;
            m_FirstZebra = first;
            LastEnd = SectionEnd(Spots.Count - 1);
            // 0.4.2: the yellow box turn spot moves with its section (gone when it was the merged one).
            if (ExtraIndex == i) ExtraIndex = -1;
            else if (ExtraIndex > i) ExtraIndex--;
        }

        /// <summary>A vehicle point at u (driving coordinates) lies after the first zebra of its section k: a "C after" the zebra.</summary>
        public readonly bool IsAfterZebra(int section, float u) => HasZebra(section) && u > FirstZebra(section) + WholePathRule.SpotEpsilon;
    }

    /// <summary>
    /// 0.4.1 (JR-T-009, owner detail 1): room past the last zebra section in the exit queue. Vehicles ahead on the car's own
    /// path are fed in path order with the distance from the section end to their rear, their length and their credit.
    ///
    /// Definition ("queue moving vs stuck"): room for obstacle i = rear_i + credit_i - (sum over the vehicles before it of
    /// length + <see cref="Spacing"/>), and there is room when every such value is at least the car's whole length
    /// (trailers included) plus the owner's margin. credit_i = the game's own braking distance of that vehicle at its
    /// current speed (VehicleUtils.GetBrakingDistance with the car's safe time step) when it moves forward, else 0. This is
    /// the stock measure of "space" (CarLaneSpeedIterator.CheckSpace / CheckOverlapSpace add exactly that braking
    /// distance for a moving car and count the cars ahead with +1 m each), so a queue that moves lets the next car in
    /// without waiting for a full car-length gap to open (no crawl), while a standing queue holds it. The credit is a
    /// guarantee, not a guess: a vehicle cannot stop sooner than its braking distance, so the room it promises exists
    /// by the time the car has crossed. The +length of the vehicles before i: a moving car close behind a standing
    /// one will queue behind it.
    /// </summary>
    public struct RoomScan
    {
        /// <summary>The game's spacing per queued car in CheckSpace (num4 += length + 1).</summary>
        public const float Spacing = 1f;
        /// <summary>Owner-tunable margin default (setting "Cars never stop on a zebra: room margin (m)").</summary>
        public const float DefaultMargin = 1f;
        public const float MinMargin = 0f;
        public const float MaxMargin = 10f;
        public const float StepMargin = 0.5f;

        public float Needed;
        public bool Blocked;
        public int Obstacles;
        /// <summary>0.4.1 (JR-T-009-r): further zebras (on later node lanes of the path) that extended the room needed.</summary>
        public int Extensions;
        private float m_Occupied;
        /// <summary>Whole length + margin (the room needed past one zebra).</summary>
        private float m_Base;
        /// <summary>Smallest room of the vehicles added so far (an extension re-judges them).</summary>
        private float m_MinRoom;

        public static RoomScan Begin(float vehicleLength, float margin)
        {
            RoomScan r = default;
            r.Needed = vehicleLength + (margin > 0f ? margin : 0f);
            r.m_Base = r.Needed;
            r.m_MinRoom = float.MaxValue;
            return r;
        }

        /// <summary>The next vehicle ahead on the path (in path order).</summary>
        public void Add(float rear, float length, float credit)
        {
            if (Blocked) return;
            float room = rear + (credit > 0f ? credit : 0f) - m_Occupied;
            if (room < m_MinRoom) m_MinRoom = room;
            if (!(room >= Needed)) Blocked = true;
            m_Occupied += (length > 0f ? length : 0f) + Spacing;
            Obstacles++;
        }

        /// <summary>
        /// 0.4.2 (JR-T-010): a vehicle that is ahead of the car INSIDE the box (or entrance band) it wants to clear, on its
        /// own path: it leaves the box into the same exit, so it takes exit room first (its length + <see cref="Spacing"/>)
        /// and can never give room itself (no block test, no credit: it has not left the box yet).
        /// </summary>
        public void AddQueued(float length)
        {
            if (Blocked) return;
            m_Occupied += (length > 0f ? length : 0f) + Spacing;
            Obstacles++;
        }

        /// <summary>
        /// 0.4.1 (JR-T-009-r, review finding 2): a further zebra (another node, e.g. a mid-block crossing after a short
        /// edge) starts inside the room window: the car must not stop on it either, so the room is needed past its end
        /// (<paramref name="zebraEnd"/>, metres from the section end): Needed = zebraEnd + whole length + margin. Vehicles
        /// already added are judged again against the new need (their room does not depend on it).
        /// </summary>
        public void ExtendPast(float zebraEnd)
        {
            float n = zebraEnd + m_Base;
            if (!(n > Needed)) return;
            Needed = n;
            Extensions++;
            if (m_MinRoom < Needed) Blocked = true;
        }

        /// <summary>0.4.1 (JR-T-009-r): a further zebra inside the room window has people on it: no room.</summary>
        public void Block() => Blocked = true;

        /// <summary>
        /// 0.4.1 (JR-T-009-r): a further zebra on the path from <paramref name="start"/> to <paramref name="end"/> (metres
        /// from the section end). Inside the window (it starts before the room needed so far ends): people on it block,
        /// else the room is needed past it. Returns true when it was inside the window.
        /// </summary>
        public bool FurtherZebra(float start, float end, bool people)
        {
            if (Blocked || !(start < Needed)) return false;
            if (people) Block();
            else ExtendPast(end);
            return true;
        }

        /// <summary>
        /// The scan can stop: blocked, or the path read so far (from the section end) is long enough that no vehicle
        /// further on can take the room (rear &gt;= this length means room &gt;= Needed).
        /// </summary>
        public readonly bool Done(float pathRead) => Blocked || pathRead >= Needed + m_Occupied;

        /// <summary>
        /// The verdict. When the car's known path ends before the scan is done (no more navigation lanes, a non-road
        /// lane such as a parking or building entrance, or the lane cap) the unknown rest counts as free: the car's own
        /// destination lies there and holding for it could hold forever.
        /// </summary>
        public readonly bool HasRoom => !Blocked;
    }

    /// <summary>0.4.2: order keys of a list read in an order of its own (float.MaxValue = skip the entry).</summary>
    public interface IOrderKeys
    {
        int Count { get; }
        float Key(int i);
    }

    /// <summary>
    /// 0.4.2 (JR-T-010, after the live 0.4.1 probe: "lane objects out of curve order" 2-4 per window): reads a list in
    /// ascending key order whatever its storage order, without allocation (selection: each call one pass over the list;
    /// the room scan stops after a few vehicles). The game sorts a lane's LaneObject buffer by m_CurvePosition.y, which
    /// for a driving car is its reservation look-ahead (position + braking distance, capped at its target), not where it
    /// is (m_CurvePosition.x), so the buffer is not in path order when a faster car follows a slower one or a car's
    /// target lies short of the lane end; <see cref="RoomScan"/> needs path order (Stage2/JRWholeJunction.cs ScanLane).
    /// </summary>
    public static class ScanOrder
    {
        /// <summary>
        /// The index with the smallest key after (<paramref name="lastKey"/>, <paramref name="lastIndex"/>) (ties by
        /// index); -1 when none is left. Start with float.MinValue, -1.
        /// </summary>
        public static int Next<T>(ref T keys, float lastKey, int lastIndex) where T : struct, IOrderKeys
        {
            int best = -1;
            float bestKey = float.MaxValue;
            for (int i = 0; i < keys.Count; i++)
            {
                float k = keys.Key(i);
                if (!(k < float.MaxValue)) continue;
                if (k < lastKey || (k == lastKey && i <= lastIndex)) continue;
                if (k < bestKey) { best = i; bestKey = k; }
            }
            return best;
        }

        /// <summary>The list is stored in ascending key order (skipped entries ignored): the probe counter.</summary>
        public static bool InOrder<T>(ref T keys, bool reversed = false) where T : struct, IOrderKeys
        {
            // JR-T-010-r: a lane driven inverse reads the buffer from its end (the game keeps it ascending by curve offset).
            float prev = float.MinValue;
            for (int n = 0; n < keys.Count; n++)
            {
                int i = reversed ? keys.Count - 1 - n : n;
                float k = keys.Key(i);
                if (!(k < float.MaxValue)) continue;
                if (k < prev) return false;
                prev = k;
            }
            return true;
        }
    }
}
