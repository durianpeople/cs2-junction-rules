using System.Runtime.CompilerServices;

namespace JunctionRules.Core
{
    /// <summary>
    /// 0.4.1 (JR-T-009): the overlaps of one car lane as path elements for the zebra sections, in driving coordinates
    /// (0 = where the car enters the lane). The job side wraps the game's LaneOverlap buffer (Stage2/JRWholeJunction.cs),
    /// the tests an array. Burst: generic constraint only.
    /// </summary>
    /// <summary>1.0.1: has the car's front passed a point (driving coordinates)? Job: StretchState.FrontPast arithmetic.</summary>
    public interface IFrontPast
    {
        bool Past(float u);
    }

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

        /// <summary>
        /// 1.0.1 (owner 19 Nov 2026): a signalised zebra whose people do not have green (light red or ending). It is road for
        /// the sections: no waiting spot before it, a car may stand on it while it gives way (e.g. a turner waiting for
        /// oncoming traffic). The room past still counts: the car does not move onto it while its exit is blocked
        /// (<see cref="SoftHoldSpot"/>). Build ignores this bit.
        /// </summary>
        public const int SoftZebra = 8;

        /// <summary>Most zebras one lane is read for (more are ignored: stock behaviour for them).</summary>
        public const int MaxZebras = 8;

        /// <summary>Game.Net.PedestrianLaneFlags values (mirrored; checked by a test against the decompile).</summary>
        public const int PedestrianUnsafe = 1;
        public const int PedestrianCrosswalk = 2;

        /// <summary>The pedestrian lane is a zebra (crosswalk, not the Unsafe lane a removed zebra leaves).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsZebra(int pedestrianLaneFlags) =>
            (pedestrianLaneFlags & PedestrianCrosswalk) != 0 && (pedestrianLaneFlags & PedestrianUnsafe) == 0;

        /// <summary>1.0.1: the zebra kind of a pedestrian lane: <see cref="Zebra"/>, <see cref="SoftZebra"/> (signalised, people
        /// not on green: LaneSignalType Stop / SafeStop) or 0. Unsignalised zebras are always <see cref="Zebra"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ZebraKind(int pedestrianLaneFlags, bool hasSignal, int signal) =>
            !IsZebra(pedestrianLaneFlags) ? 0 : hasSignal && (signal == WholePathRule.SignalStop || signal == WholePathRule.SignalSafeStop) ? SoftZebra : Zebra;

        /// <summary>
        /// 1.0.1: the room-past hold for soft zebras. The first soft zebra the car's front has not reached
        /// (<paramref name="frontPast"/>(start) false), returned as its start; <paramref name="roomFrom"/> = the end of
        /// everything on the lane (every zebra and vehicle point): the car must fit past all of it before it moves onto the
        /// zebra. False when there is no such zebra.
        /// </summary>
        public static bool SoftHoldSpot<T, F>(ref T list, ref F frontPast, out float zebraStart, out float roomFrom)
            where T : struct, IPathElements where F : struct, IFrontPast
        {
            zebraStart = 2f;
            roomFrom = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                list.Get(i, out int kind, out float s, out float e);
                if (kind == 0) continue;
                if (e > roomFrom) roomFrom = e;
                if ((kind & SoftZebra) != 0 && s < zebraStart && !frontPast.Past(s)) zebraStart = s;
            }
            return zebraStart < 2f;
        }

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
        /// <summary>0.4.9 (G17): the vehicle that took the room (entity index / version; 0 / 0 = none or unknown, e.g. people on a further zebra).</summary>
        public int BlockerIndex, BlockerVersion;
        private int m_MinIndex, m_MinVersion;

        /// <summary>
        /// 0.4.10 (JR-T-016, G19, owner 17 Nov 2026, UK yellow box: do not enter unless your exit is clear): only room that
        /// is FREE NOW counts: a vehicle ahead that moves gives no braking-distance credit (a creeping stop-and-go queue
        /// can stop again with the car in the box). Set for the yellow box room tests (junction and exit / entrance boxes,
        /// the right-turner's re-check at its turn spot, a vehicle leaving into a box); the zebra rule keeps the credit.
        /// </summary>
        public bool FreeNow;

        /// <summary>
        /// 0.4.10 (G19, probe): with <see cref="FreeNow"/>, the vehicle that blocked had room with its moving credit: the
        /// 0.4.9 rule would have let the car in (counted, to show what the free-now rule costs through traffic).
        /// </summary>
        public bool HeldForMovingQueue;

        /// <summary>
        /// 0.4.10 (chains, Highway Code 174): with <see cref="FreeNow"/> a vehicle STANDING ahead inside the box blocks, except
        /// for a right-turner with the UK exception (true here): it may wait behind other vehicles waiting to turn right.
        /// </summary>
        public bool QueuedMayStand;

        /// <summary>
        /// JR-T-022 (owner 19 Nov 2026: "when humans drive over a box, they look over the cars in front of it, and if logically
        /// the human thinks it obviously has space, they don't like to jitter and pause behind the box for the car right in
        /// front to clear it first"), setting "Look past the car in front at a box" (default on), acts only with
        /// <see cref="FreeNow"/>. ONE rule: the car judges the whole stretch ahead (every vehicle the scan reads: before the
        /// line, in the box, past it), not only the room free now. A vehicle past the box whose free-now room is short still
        /// gives room when it is clearly moving away (<see cref="LookPastMinSpeed"/> or faster) and its position plus the
        /// distance it is GUARANTEED to cover even if it brakes as hard as it can now (<see cref="GuaranteedTravel"/>) leaves the
        /// room: so when the car arrives there, its whole length (trailers included) plus the margin fits behind it even if the
        /// queue ahead brakes at once. The look-past is refused (today's free-now verdict) when any vehicle the car follows
        /// through the box (queued ahead of it before / inside the box) is slower than that speed, and every other block
        /// (a standing vehicle, a red / amber light, people on a further zebra, a further box) holds as before. The decision
        /// runs again at every step while the car can still stop at the line, so a queue that brakes holds it there (the
        /// existing safety stop). False = 1.0.1 behaviour bit for bit (every new path tests this flag first).
        /// JR-T-022b (owner sight rule): it judges only what the driver sees: a <see cref="LookPastSightMetres"/> radius around
        /// the driver (<see cref="SightLeft"/>), cut at the next junction (stand-in for corner buildings); ordinary cars never
        /// block the view, a large vehicle does (<see cref="BlocksView"/>): it is judged itself, the vehicles beyond it get no
        /// look-past credit (free-now rule).
        /// </summary>
        public bool LookPast;

        /// <summary>JR-T-022: "clearly moving away" (m/s, about 11 km/h): slower vehicles (a creeping stop-and-go queue, G19) give no look-past room.</summary>
        public const float LookPastMinSpeed = 3f;

        /// <summary>
        /// JR-T-022b (owner sight rule 19 Nov 2026, replaces the 50 m along the path): the driver's line of sight is a RADIUS
        /// (m, straight line, not path length) around the driver: on a turn with a free corner it reaches the part of the
        /// road turned into that lies within it. The look-past accepts room only when the whole room the car needs (past the
        /// vehicles it follows) ends on road seen within this radius (<see cref="SightLeft"/>); beyond it nothing counts for
        /// the look-past, so the car falls back to the free-now rule. The one sight constant.
        /// </summary>
        public const float LookPastSightMetres = 50f;

        /// <summary>
        /// JR-T-022b: how far past the window start (the box's far end), along the car's path in the scan's coordinates, the
        /// road is seen: where the path first leaves the <see cref="LookPastSightMetres"/> radius around the driver
        /// (<see cref="SightLine.FirstExit"/>), cut further by <see cref="LimitSight"/> (the next junction: the interim stand-in
        /// for corner buildings). Set by the caller with the look-past; float.MaxValue = no cut found yet.
        /// </summary>
        public float SightLeft;

        /// <summary>
        /// JR-T-022 (fallback when the driver's position is not known): the sight left past the window start along the path
        /// for a car whose front bumper is <paramref name="frontToWindowStart"/> path metres before it. Never longer than the
        /// radius rule allows (a path is never shorter than the straight line).
        /// </summary>
        public static float SightPast(float frontToWindowStart) => LookPastSightMetres - (frontToWindowStart > 0f ? frontToWindowStart : 0f);

        /// <summary>
        /// JR-T-022b: the game's SizeClass of a LARGE vehicle (Game.Vehicles.SizeClass: Small 0, Medium 1, Large 2,
        /// Undefined 3; CarData.m_SizeClass from CarBasePrefab.m_SizeClass): lorries, buses, articulated tractors and their
        /// trailers (the class the game bars with heavy-traffic bans: VehicleUtils.GetIgnoredPathfindRules). Cars are Small,
        /// vans Medium. Such a vehicle blocks the driver's view (tall); smaller ones do not.
        /// </summary>
        public const int LargeSizeClass = 2;

        /// <summary>JR-T-022b: a vehicle of this game SizeClass blocks the view (<see cref="LargeSizeClass"/>).</summary>
        public static bool BlocksView(int sizeClass) => sizeClass == LargeSizeClass;

        /// <summary>JR-T-022: the free-now rule alone would have held the car; the look-past found the room (probe).</summary>
        public bool LookPastUsed;

        private float m_MinPred;
        private int m_MinPredIndex, m_MinPredVersion;
        private bool m_LookSlow;
        private int m_SlowIndex, m_SlowVersion;
        /// <summary>JR-T-022: where the room the look-past relies on ends (metres past the window start; the furthest so far).</summary>
        private float m_LookNeedEnd;
        /// <summary>
        /// JR-T-022b: the view is blocked from here on: a LARGE vehicle past the box was read (it is seen and judged itself,
        /// with its own position + guaranteed travel), or one is queued ahead of the car before / inside the box (it hides the
        /// whole road past the box; its own position credit is not modelled: the queued part of the scan has no positions).
        /// Every vehicle after it in path order is hidden: it gives NO look-past credit and is judged by the free-now rule
        /// (owner: "unseen vehicle = no look-past credit, today's free-now rule for that part"). Not skipped: a hidden queue
        /// that is already stopping makes the large vehicle stop short of its own braking distance (measured: skipping them
        /// let cars stand in the box 0 -> 455 vehicle-steps in the offline scenes).
        /// </summary>
        private bool m_Hidden;
        /// <summary>JR-T-022b (tests / probe): vehicles judged free-now because a large vehicle hid them.</summary>
        public int HiddenSeen;

        /// <summary>
        /// JR-T-022: the distance a vehicle moving at <paramref name="speed"/> still covers at the least when it brakes now at its
        /// own stock braking rate: the game's braking distance (VehicleUtils.GetBrakingDistance = v^2 / 2b + v * timeStep)
        /// less two steps of travel (the reaction step it includes, and the discrete step's own slack), never negative.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static float GuaranteedTravel(float brakingDistance, float speed, float timeStep)
        {
            float g = brakingDistance - 2f * (speed > 0f ? speed : 0f) * (timeStep > 0f ? timeStep : 0f);
            return g > 0f ? g : 0f;
        }

        public static RoomScan Begin(float vehicleLength, float margin)
        {
            RoomScan r = default;
            r.Needed = vehicleLength + (margin > 0f ? margin : 0f);
            r.m_Base = r.Needed;
            r.m_MinRoom = float.MaxValue;
            r.m_MinPred = float.MaxValue;
            r.SightLeft = float.MaxValue;
            return r;
        }

        /// <summary>JR-T-022: the look-past acts (setting on, a box room test).</summary>
        public readonly bool LookPastOn => LookPast && FreeNow;

        /// <summary>The next vehicle ahead on the path (in path order).</summary>
        public void Add(float rear, float length, float credit) => Add(rear, length, credit, 0, 0);

        /// <summary>
        /// 0.4.9 (G17): as above, naming the vehicle (entity index / version), so a hold for want of room can name the vehicle
        /// it waits for in the game's Blocker (stock does that for a car it stops behind; 0.4.8 wrote none, which hid every
        /// Junction Rules hold from blocker-chain deadlock finders such as the No Vehicle Despawn mod).
        /// </summary>
        public void Add(float rear, float length, float credit, int index, int version)
        {
            // JR-T-022: with the look-past on, a vehicle given without its speed (a light's stop line, an unnamed obstacle) is
            // a standing one: judged free now, as before.
            if (LookPastOn) { AddLook(rear, length, credit, index, version, 0f, 0f, false, true); return; }
            if (Blocked) return;
            float c = credit > 0f ? credit : 0f;
            float room = rear + (FreeNow ? 0f : c) - m_Occupied;
            if (room < m_MinRoom) { m_MinRoom = room; m_MinIndex = index; m_MinVersion = version; }
            if (!(room >= Needed))
            {
                Blocked = true; BlockerIndex = index; BlockerVersion = version;
                if (FreeNow && room + c >= Needed) HeldForMovingQueue = true;
            }
            m_Occupied += (length > 0f ? length : 0f) + Spacing;
            Obstacles++;
        }

        /// <summary>
        /// JR-T-022: as <see cref="Add(float, float, float, int, int)"/> with the vehicle's forward speed (m/s) and the distance
        /// it is guaranteed to cover (<see cref="GuaranteedTravel"/>). With <see cref="LookPast"/> off exactly that call.
        /// An ordinary (not large) vehicle: it never blocks the view.
        /// </summary>
        public void Add(float rear, float length, float credit, int index, int version, float speed, float travel)
            => Add(rear, length, credit, index, version, speed, travel, false);

        /// <summary>
        /// JR-T-022b: as above; <paramref name="large"/> = the vehicle blocks the view (<see cref="BlocksView"/>): it is seen
        /// and judged itself (position + guaranteed travel), and every vehicle after it in path order is hidden (free-now only).
        /// </summary>
        public void Add(float rear, float length, float credit, int index, int version, float speed, float travel, bool large)
        {
            if (!LookPastOn) { Add(rear, length, credit, index, version); return; }
            AddLook(rear, length, credit, index, version, speed, travel, large, false);
        }

        /// <summary>
        /// JR-T-022 / 022b look-past core. <paramref name="fixedObstacle"/> = a call without a speed (a red / amber light's stop
        /// line): never hidden, standing.
        /// </summary>
        private void AddLook(float rear, float length, float credit, int index, int version, float speed, float travel, bool large, bool fixedObstacle)
        {
            if (Blocked) return;
            float c = credit > 0f ? credit : 0f;
            float now = rear - m_Occupied;
            // JR-T-022b: hidden behind a large vehicle: no look-past credit (free now)
            if (m_Hidden && !fixedObstacle) HiddenSeen++;
            float pred = speed >= LookPastMinSpeed && !m_Hidden ? now + (travel > 0f ? travel : 0f) : now;
            if (now < m_MinRoom) { m_MinRoom = now; m_MinIndex = index; m_MinVersion = version; }
            if (pred < m_MinPred) { m_MinPred = pred; m_MinPredIndex = index; m_MinPredVersion = version; }
            if (!(now >= Needed))
            {
                // the room it needs behind this vehicle must end within sight (limited sight addendum)
                if (pred >= Needed && Needed + m_Occupied <= SightLeft)
                {
                    LookPastUsed = true;
                    if (Needed + m_Occupied > m_LookNeedEnd) m_LookNeedEnd = Needed + m_Occupied;
                }
                else
                {
                    Blocked = true; BlockerIndex = index; BlockerVersion = version;
                    if (now + c >= Needed) HeldForMovingQueue = true;
                }
            }
            m_Occupied += (length > 0f ? length : 0f) + Spacing;
            Obstacles++;
            if (large && !fixedObstacle) m_Hidden = true;
        }

        /// <summary>
        /// JR-T-022: as <see cref="AddQueued(float)"/>, with the vehicle's forward speed (m/s): with <see cref="LookPast"/> on, a
        /// vehicle the car follows through the box that is slower than <see cref="LookPastMinSpeed"/> refuses the look-past
        /// (the queue is not clearly moving). With it off exactly AddQueued(length).
        /// </summary>
        public void AddQueued(float length, float speed, int index, int version) => AddQueued(length, speed, index, version, false);

        /// <summary>
        /// JR-T-022b: as above; <paramref name="large"/> = a vehicle that blocks the view queued ahead of the car (before /
        /// inside the box): the road past the box is hidden behind it, so vehicles there give no look-past credit.
        /// </summary>
        public void AddQueued(float length, float speed, int index, int version, bool large)
        {
            if (LookPastOn && !Blocked && !m_LookSlow && !(speed >= LookPastMinSpeed))
            {
                m_LookSlow = true; m_SlowIndex = index; m_SlowVersion = version;
            }
            if (LookPastOn && large && !Blocked) m_Hidden = true;
            AddQueued(length);
        }

        /// <summary>
        /// JR-T-022: the look-past verdict, after the whole stretch is read: room the free-now rule would refuse is accepted
        /// only when no vehicle the car follows through the box is slow; else the car is held as before, naming that vehicle.
        /// </summary>
        public void FinishLookPast()
        {
            if (!LookPastOn || Blocked || !LookPastUsed) return;
            if (m_LookSlow) { Blocked = true; BlockerIndex = m_SlowIndex; BlockerVersion = m_SlowVersion; }
            // the room relied on ends beyond a junction found later on the path (LimitSight): not clear
            else if (m_LookNeedEnd > SightLeft) { Blocked = true; BlockerIndex = m_MinIndex; BlockerVersion = m_MinVersion; }
        }

        /// <summary>
        /// JR-T-022: a cut of the sight <paramref name="at"/> metres past the window start (along the path): where the path
        /// leaves the sight radius (JR-T-022b), or the next junction on the path (a light in any state, a give-way / STOP line,
        /// turning traffic: the vehicles ahead may all stop there for something the scan does not see; JR-T-022b: also the
        /// interim stand-in for corner buildings cutting the view into the road turned into). The look-past does not count on
        /// room beyond it.
        /// </summary>
        public void LimitSight(float at)
        {
            if (LookPastOn && at < SightLeft) SightLeft = at > 0f ? at : 0f;
        }

        /// <summary>JR-T-022: the car has room only thanks to the look-past (after <see cref="FinishLookPast"/>; probe counter).</summary>
        public readonly bool AdmittedByLookPast => LookPastOn && !Blocked && LookPastUsed;

        /// <summary>
        /// 0.4.2 (JR-T-010): a vehicle that is ahead of the car INSIDE the box (or entrance band) it wants to clear, on its
        /// own path: it leaves the box into the same exit, so it takes exit room first (its length + <see cref="Spacing"/>)
        /// and can never give room itself (no block test, no credit: it has not left the box yet).
        /// </summary>
        public void AddQueued(float length)
        {
            if (Blocked) return;
            m_Occupied += (length > 0f ? length : 0f) + Spacing;
            m_Queued += (length > 0f ? length : 0f) + Spacing;
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
            if (LookPastOn)
            {
                // JR-T-022: the vehicles read so far are judged again on their look-past room
                if (Blocked) return;
                if (m_MinPred < Needed) { Blocked = true; BlockerIndex = m_MinPredIndex; BlockerVersion = m_MinPredVersion; }
                else if (m_MinRoom < Needed)
                {
                    // within sight (conservative: behind every vehicle read so far)
                    if (Needed + m_Occupied <= SightLeft)
                    {
                        LookPastUsed = true;
                        if (Needed + m_Occupied > m_LookNeedEnd) m_LookNeedEnd = Needed + m_Occupied;
                    }
                    else { Blocked = true; BlockerIndex = m_MinIndex; BlockerVersion = m_MinVersion; }
                }
                return;
            }
            if (m_MinRoom < Needed && !Blocked) { Blocked = true; BlockerIndex = m_MinIndex; BlockerVersion = m_MinVersion; }
        }

        /// <summary>0.4.1 (JR-T-009-r): a further zebra inside the room window has people on it: no room.</summary>
        public void Block() => Blocked = true;

        /// <summary>0.4.9 (G15b): a vehicle standing across the path (turning into / out of a driveway): no room, naming it.</summary>
        public void BlockBy(int index, int version)
        {
            if (Blocked) return;
            Blocked = true;
            BlockerIndex = index;
            BlockerVersion = version;
        }

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
        /// <summary>
        /// 0.4.10 (sweep): how far past the window start the car may end up standing when it has room: the vehicles queued
        /// ahead of it (inside the box / before its line) plus its own room. 0.4.10 chains fix: vehicles PAST the box do not
        /// widen it (the car stops behind them, never beyond their current rear: they only move on). Further boxes / lights starting before this matter (JRWholeJunction
        /// .FurtherBoxes).
        /// </summary>
        public readonly float Reach => Needed + m_Queued;

        /// <summary>0.4.10 (chains): the length + spacing of the vehicles queued ahead INSIDE the box (AddQueued) only; vehicles
        /// past the box are judged by their position (Add) and never widen the reach.</summary>
        private float m_Queued;

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
