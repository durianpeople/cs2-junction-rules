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
                    continue;
                }
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
            if (atLine) spots[0] = 0f;
            layout.Spots = spots;
            layout.LastEnd = layout.SectionEnd(spots.Count - 1);
            return true;
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

        /// <summary>
        /// The room-past check is due for section <paramref name="next"/> (the next spot ahead of the car): the section
        /// holds a zebra and the car can still stop at its spot (the game's can-stop test). Room is measured from the
        /// section's end (<see cref="ZebraLayout.SectionEnd"/>): the car must fit past everything of the section.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RoomCheckDue(in ZebraLayout layout, int next, bool canStopBefore) =>
            next >= 0 && next < layout.Spots.Count && layout.HasZebra(next) && canStopBefore;

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
        private float m_Occupied;

        public static RoomScan Begin(float vehicleLength, float margin)
        {
            RoomScan r = default;
            r.Needed = vehicleLength + (margin > 0f ? margin : 0f);
            return r;
        }

        /// <summary>The next vehicle ahead on the path (in path order).</summary>
        public void Add(float rear, float length, float credit)
        {
            if (Blocked) return;
            float room = rear + (credit > 0f ? credit : 0f) - m_Occupied;
            if (!(room >= Needed)) Blocked = true;
            m_Occupied += (length > 0f ? length : 0f) + Spacing;
            Obstacles++;
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
}
