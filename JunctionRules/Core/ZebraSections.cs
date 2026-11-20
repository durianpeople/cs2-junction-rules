using System.Runtime.CompilerServices;

namespace JunctionRules.Core
{
    /// <summary>1.0.1: has the car's front passed a point (driving coordinates)? Job: StretchState.FrontPast arithmetic.</summary>
    public interface IFrontPast
    {
        bool Past(float u);
    }

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
        /// JR-T-021C (G17): does the soft-zebra hold bind over the zebra-section hold (BeginZebras returns their minimum)? Only
        /// then is its blocker the hook's blocker; on a tie the section hold names it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SoftHoldBinds(float sectionHold, float softHold) => softHold < sectionHold;

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
        /// gives room when it is clearly moving away (<see cref="LookPastMinSpeed"/> or faster). JR-T-027 (1.2.0, owner rule 20 Nov
        /// 2026): an ordinary vehicle moving away is expected to carry on and stops only behind the back of a visible standing
        /// vehicle / queue or at a red stop line (the scan reads these where they are; the road up to its predicted front must lie
        /// inside the sight). JR-T-029 (1.3.0, owner rule 20 Nov 2026): the sight is <see cref="LookPastSightMetres"/> (100 m); a vehicle
        /// beyond it is not read; when nothing within sight is standing or slow the car goes (no hold because the room it needs
        /// runs past the sight edge); when it sees a standing / slow vehicle or a red stop line, every vehicle between the box
        /// and that blocker is packed nose to tail behind it, skipping the places nobody may stand in (<see cref="NoStopZone"/>:
        /// other yellow boxes, zebras except the soft zebra, other junction interiors): is the car's own room left? A LARGE vehicle keeps the 1.1.0 credit: its position plus the distance it is GUARANTEED to cover even
        /// if it brakes as hard as it can now (<see cref="GuaranteedTravel"/>), less <see cref="LookPastLargeMargin"/>. The look-past is refused (today's free-now verdict) when any vehicle the car follows
        /// through the box (queued ahead of it before / inside the box) is slower than that speed, and every other block
        /// (a standing vehicle, a red / amber light, people on a further zebra, a further box) holds as before. The decision
        /// runs again at every step while the car can still stop at the line, so a queue that brakes holds it there (the
        /// existing safety stop). False = 1.0.1 behaviour bit for bit (every new path tests this flag first).
        /// JR-T-022b (owner sight rule): it judges only what the driver sees: a <see cref="LookPastSightMetres"/> radius around
        /// the driver (<see cref="SightLeft"/>); ordinary cars never block the view, a large vehicle does
        /// (<see cref="BlocksView"/>). JR-T-022d (owner rulings 19 Nov 2026, binding): the view ENDS at a large vehicle: it is
        /// judged itself (position + guaranteed travel), the vehicles beyond it are NOT READ AT ALL (neither for nor against),
        /// and the room the look-past relies on must end within what is seen (at the latest the large vehicle's front).
        /// JR-T-022f (owner call B): a large vehicle queued ahead of the car before / inside the box switches the look-past off
        /// for that decision (today's 1.0.1 free-now verdict, never stricter than off). Buildings block the view: every target the look-past credits (each vehicle credited, the end of the room
        /// relied on) must be in a straight 2D line from the driver that crosses no building footprint
        /// (<see cref="FinishSight{T}"/>, Core <see cref="SightLine.SegmentHitsBox"/>). JR-T-022c: an articulated vehicle
        /// (trailer and tractor read as separate lane objects, named by the same controller) is ONE vehicle: credited by its
        /// leading element in path order, its own elements never hide each other; the view ends at the whole artic's front.
        /// </summary>
        public bool LookPast;

        /// <summary>JR-T-022: "clearly moving away" (m/s, about 18 km/h): slower vehicles (a creeping stop-and-go queue, G19) give no look-past room.
        /// JR-T-028 (1.2.0): 5 m/s, was 3 m/s in 1.1.0: with no travel limit on a carrying-on vehicle, a crawling, decelerating
        /// queue tail counts as standing (owner rule "standing queue").</summary>
        public const float LookPastMinSpeed = 5f;

        /// <summary>
        /// JR-T-022b (owner sight rule 19 Nov 2026, replaces the 50 m along the path): the driver's line of sight is a RADIUS
        /// (m, straight line, not path length) around the driver: on a turn with a free corner it reaches the part of the
        /// road turned into that lies within it. The look-past accepts room only when the whole room the car needs (past the
        /// vehicles it follows) ends on road seen within this radius (<see cref="SightLeft"/>); beyond it nothing counts for
        /// the look-past, so the car falls back to the free-now rule. The one sight constant. JR-T-029 (1.3.0, owner rule 20 Nov 2026: driving manuals scan one to two city
        /// blocks; a car's motion at town speed is not perceptible beyond about 100 m): 100 m (was 50 m). A vehicle (or a red
        /// stop line) beyond it is not read at all; the room needed may run past it (nothing slow seen = the car goes).
        /// </summary>
        public const float LookPastSightMetres = 100f;

        /// <summary>
        /// JR-T-022b: how far past the window start (the box's far end), along the car's path in the scan's coordinates, the
        /// road is seen: where the path first leaves the <see cref="LookPastSightMetres"/> radius around the driver
        /// (<see cref="SightLine.FirstExit"/>), cut further by <see cref="LimitSight"/> (the end of the path read; the next
        /// junction only when the driver's position is unknown, JR-T-022d: buildings are tested instead,
        /// <see cref="FinishSight{T}"/>). Set by the caller with the look-past; float.MaxValue = no cut found yet.
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
        /// with its own position + guaranteed travel). (JR-T-022f: one queued ahead before / inside the box no longer hides:
        /// it switches the look-past off, <see cref="AddQueued(float, float, int, int, bool)"/>.) JR-T-022d (owner ruling 1, 19 Nov 2026: "I don't want it to see the hidden queue, so just
        /// limit the visibility"): every vehicle after it in path order is NOT READ AT ALL: no credit, no block, no room taken
        /// (<see cref="HiddenSeen"/> counts them); the view ends at the large vehicle (<see cref="m_ViewEnd"/>).
        /// JR-T-022c: never the hiding vehicle's own other elements (an artic's tractor behind its trailer): see
        /// <see cref="m_HiderIndex"/> and the join in <see cref="AddLook"/>.
        /// </summary>
        private bool m_Hidden;
        /// <summary>
        /// JR-T-022d: where the view ends (metres past the window start, scan coordinates): the front of the large vehicle
        /// that hides the rest (the whole artic's front once its other elements joined). The room the look-past relies on must end here at the latest (<see cref="FinishLookPast"/>).
        /// float.MaxValue = nothing hides the view.
        /// </summary>
        private float m_ViewEnd;
        /// <summary>
        /// JR-T-022c (review finding 1): the vehicle that set <see cref="m_Hidden"/>, named as passed to Add / AddQueued (its
        /// controller for a trailer). An element never hides another element of the same vehicle: the trailer of an
        /// articulated vehicle (read first when it drives away) does not hide its own tractor.
        /// </summary>
        private int m_HiderIndex, m_HiderVersion;
        /// <summary>
        /// JR-T-022c: the vehicle the last <see cref="AddLook"/> judged (by name), its rear and front in the scan's
        /// coordinates and the occupied length before it. A further element of the same vehicle read straight after it
        /// (an articulated vehicle's tractor after its trailer) is part of that one object: it only stretches the object's
        /// length; the object is judged once, by its leading element in path order (the one nearest the car: its rear),
        /// with that element's position + guaranteed travel.
        /// </summary>
        private bool m_GroupOpen;
        private int m_GroupIndex, m_GroupVersion;
        private float m_GroupRear, m_GroupFront, m_GroupOccupied;
        /// <summary>JR-T-022b / 022d (tests / probe): vehicles behind a large vehicle, not read (022b: judged free-now).</summary>
        public int HiddenSeen;
        /// <summary>
        /// JR-T-022f (owner call B, 19 Nov 2026): a large vehicle queued ahead of the car (before / inside the box) switched the
        /// look-past OFF for this decision: the rest of the scan and the verdict are the 1.0.1 free-now rule (tests / probe).
        /// </summary>
        public bool QueuedLargeFreeNow;
        /// <summary>JR-T-022f: set by a large vehicle queued ahead: <see cref="LookPastOn"/> is false from then on.</summary>
        private bool m_LookPastOff;
        /// <summary>JR-T-022d (owner ruling 2, tests / probe): a building blocked the line of sight to a look-past target.</summary>
        public bool BuildingCut;
        /// <summary>JR-T-025 (why slowed): why a look-past gave no room (Core LookPastRefusal code; 0 = not refused by the look-past).</summary>
        public byte Refusal;
        /// <summary>JR-T-025 (why slowed): the least room seen (m; float.MaxValue = none read). Room short by = Needed - this.</summary>
        public readonly float MinRoomNow => m_MinRoom;
        /// <summary>
        /// JR-T-027 (why slowed): the least PREDICTED room with the look-past (m; float.MaxValue = none): vehicles that carry on
        /// do not count, a standing vehicle / a light counts where it is (less the vehicles queued before it), a view-blocking
        /// vehicle at its position + guaranteed travel less the margin. Predicted room short by = Needed - this.
        /// </summary>
        public readonly float MinRoomPredicted => m_MinPred;

        /// <summary>JR-T-022d: at most this many credited vehicles are kept as sight targets (the nearest ones and the furthest).</summary>
        public const int MaxSightTargets = 4;

        /// <summary>
        /// JR-T-022d (owner ruling 1: "a cheap GENERAL way to be careful with a view-blocking vehicle is fine if needed, but
        /// it must not use hidden information"): a large vehicle's look-past credit is its guaranteed travel LESS this many
        /// metres: the driver allows for one car (4.2 m + the 1 m spacing) standing unseen just ahead of it. Measured in the
        /// offline scenes (JR-T-022d digest): without it the cars the look-past let in stood in a box 575 vehicle-steps under
        /// physical braking, with 2 m 198, 4 m 55, 5 m 2. One constant, no special cases.
        /// </summary>
        public const float LookPastLargeMargin = 5f;
        /// <summary>JR-T-022d: the vehicles the look-past credited (rear, scan coordinates; name), for the building test.</summary>
        private float m_T0, m_T1, m_T2, m_T3;
        private int m_TI0, m_TV0, m_TI1, m_TV1, m_TI2, m_TV2, m_TI3, m_TV3;
        private int m_Targets;
        /// <summary>JR-T-022c (tests / probe): further elements of a vehicle just read, taken as part of it (artic tractors).</summary>
        public int ElementsJoined;

        /// <summary>JR-T-029 (tests / probe): vehicles / stop lines beyond the sight (<see cref="SightLeft"/>), not read.</summary>
        public int BeyondSightSkipped;

        /// <summary>
        /// JR-T-029 (owner rule 4, 20 Nov 2026): the kinds of place nobody may stand in that the look-past's queue packing skips
        /// (<see cref="NoStopZone"/>). Refusal codes: <see cref="LookPastRefusal.QueuePushedByZebra"/> + kind - 1.
        /// </summary>
        public const byte ZoneZebra = 1, ZoneBox = 2, ZoneJunction = 3;

        /// <summary>JR-T-029: most no-stop zones kept per scan (path order: the nearest; further ones are ignored).</summary>
        public const int MaxZones = WaitingSpots.Capacity;

        /// <summary>JR-T-029: most packed vehicles kept one by one (more: the furthest absorbs the rest as one block).</summary>
        public const int MaxPacked = 2 * WaitingSpots.Capacity;

        /// <summary>JR-T-029: the no-stop zones (metres past the window start, sorted by start) and their kinds.</summary>
        private WaitingSpots m_ZoneS, m_ZoneE, m_ZoneK;
        /// <summary>JR-T-029: the vehicles read so far that occupy room (path order): length + <see cref="Spacing"/> each.</summary>
        private WaitingSpots m_PackA, m_PackB;
        private int m_PackN;

        /// <summary>JR-T-029 (tests / probe): no-stop zones recorded.</summary>
        public readonly int Zones => m_ZoneS.Count;

        private readonly float PackBlock(int i) => i < WaitingSpots.Capacity ? m_PackA[i] : m_PackB[i - WaitingSpots.Capacity];

        private void SetPackBlock(int i, float size)
        {
            if (i < WaitingSpots.Capacity) { WaitingSpots a = m_PackA; a[i] = size; if (a.Count <= i) a.Count = i + 1; m_PackA = a; }
            else { WaitingSpots b = m_PackB; b[i - WaitingSpots.Capacity] = size; if (b.Count <= i - WaitingSpots.Capacity) b.Count = i - WaitingSpots.Capacity + 1; m_PackB = b; }
        }

        /// <summary>JR-T-029: one more vehicle occupies room (its length + spacing), in path order; full = joins the last block.</summary>
        private void PushPack(float size)
        {
            if (m_PackN < MaxPacked) SetPackBlock(m_PackN++, size);
            else SetPackBlock(MaxPacked - 1, PackBlock(MaxPacked - 1) + size);
        }

        /// <summary>
        /// JR-T-029 (owner rule 4): a stretch of the car's path past the box (metres past the window start, path order) where
        /// nobody may stand: another yellow box (junction, exit / entrance band), a zebra (not the soft zebra of a signal whose
        /// people have red: waiting is allowed there) or another junction's interior. With the look-past only (else ignored:
        /// 1.0.1 unchanged); it never changes the car's own room need (<see cref="ExtendPast"/> does that, unchanged), only where
        /// the vehicles ahead can be packed (<see cref="PackedRoom"/>). Kept sorted by start; at most <see cref="MaxZones"/>.
        /// </summary>
        public void NoStopZone(float start, float end, byte kind)
        {
            if (!LookPastOn || !(end > start) || !(end > 0f) || m_ZoneS.Count >= MaxZones) return;
            WaitingSpots s = m_ZoneS, e = m_ZoneE, k = m_ZoneK;
            int i = s.Count;
            s.Add(start); e.Add(end); k.Add(kind);
            while (i > 0 && s[i - 1] > start)
            {
                s[i] = s[i - 1]; e[i] = e[i - 1]; k[i] = k[i - 1];
                i--;
            }
            s[i] = start; e[i] = end; k[i] = kind;
            m_ZoneS = s; m_ZoneE = e; m_ZoneK = k;
        }

        /// <summary>
        /// JR-T-029: as <see cref="NoStopZone"/> for the range [<paramref name="s"/>, <paramref name="e"/>] of a lane in driving
        /// coordinates (0..1), the path covering [<paramref name="startD"/>, <paramref name="endD"/>] of it and starting
        /// <paramref name="read"/> metres past the window start (clipped to that span).
        /// </summary>
        public void NoStopOnLane(float s, float e, float startD, float endD, float laneLength, float read, byte kind)
        {
            if (!(e > startD) || !(s < endD)) return;
            float a = read + ((s > startD ? s : startD) - startD) * laneLength;
            float b = read + ((e < endD ? e : endD) - startD) * laneLength;
            NoStopZone(a, b, kind);
        }

        /// <summary>
        /// JR-T-029 (owner rule 3 + 4): the room left for the car when every vehicle read so far is packed nose to tail behind a
        /// blocker whose rear is at <paramref name="rear"/> (the nearest-to-the-blocker first, each its length + spacing), a
        /// vehicle whose body would overlap a no-stop zone moving back to stand before it. Without zones exactly
        /// rear - (sum of length + spacing). <paramref name="pushKind"/> = the kind of the last zone that pushed a vehicle back (0 = none).
        /// </summary>
        public readonly float PackedRoom(float rear, out byte pushKind)
        {
            pushKind = 0;
            float x = rear;
            int nz = m_ZoneS.Count;
            for (int i = m_PackN - 1; i >= 0; i--)
            {
                float size = PackBlock(i);
                float body = size - Spacing;
                float front = x - Spacing;
                if (body > 0f)
                {
                    for (int j = nz - 1; j >= 0; j--)
                    {
                        if (!(m_ZoneS[j] < front)) continue;            // the zone lies ahead of this body
                        if (!(m_ZoneE[j] > front - body)) continue;     // the zone lies behind it
                        front = m_ZoneS[j];                             // stands before the zone
                        pushKind = (byte)m_ZoneK[j];
                    }
                }
                x = front - (body > 0f ? body : 0f);
            }
            return x;
        }

        /// <summary>
        /// JR-T-029: the least rear a blocker past the box must have for the packed queue to leave the car its room
        /// (<see cref="Needed"/>): the vehicles read so far packed forward from the car's room, skipping the no-stop zones.
        /// A vehicle further on (its rear at least here) cannot take the room. Without zones exactly Needed + occupied.
        /// </summary>
        public readonly float PackedReach()
        {
            float r = Needed;
            int nz = m_ZoneS.Count;
            for (int i = 0; i < m_PackN; i++)
            {
                float size = PackBlock(i);
                float body = size - Spacing;
                if (body > 0f)
                {
                    for (int j = 0; j < nz; j++)
                    {
                        if (!(m_ZoneE[j] > r)) continue;               // behind
                        if (!(m_ZoneS[j] < r + body)) break;           // ahead (sorted by start)
                        r = m_ZoneE[j];                                // stands past the zone
                    }
                }
                r += size;
            }
            return r;
        }

        /// <summary>JR-T-022c: two names (index / version as passed to Add) are the same vehicle; the unnamed 0 / 0 never is.</summary>
        private static bool SameVehicle(int index, int version, int otherIndex, int otherVersion)
            => (index != 0 || version != 0) && index == otherIndex && version == otherVersion;

        /// <summary>JR-T-022c: hidden behind a large vehicle that is not this vehicle itself.</summary>
        private readonly bool HiddenFor(int index, int version) => m_Hidden && !SameVehicle(index, version, m_HiderIndex, m_HiderVersion);

        /// <summary>JR-T-022c: from here on the view is blocked by the vehicle named (the first such vehicle is kept).</summary>
        private void Hide(int index, int version)
        {
            if (m_Hidden) return;
            m_Hidden = true; m_HiderIndex = index; m_HiderVersion = version;
        }

        /// <summary>JR-T-022d: a vehicle credited by the look-past at <paramref name="rear"/>: it must be seen (building test).</summary>
        private void NoteTarget(float rear, int index, int version)
        {
            int k = m_Targets < MaxSightTargets ? m_Targets++ : MaxSightTargets - 1;   // full: the furthest replaces the last
            switch (k)
            {
                case 0: m_T0 = rear; m_TI0 = index; m_TV0 = version; break;
                case 1: m_T1 = rear; m_TI1 = index; m_TV1 = version; break;
                case 2: m_T2 = rear; m_TI2 = index; m_TV2 = version; break;
                default: m_T3 = rear; m_TI3 = index; m_TV3 = version; break;
            }
        }

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
            r.m_ViewEnd = float.MaxValue;
            return r;
        }

        /// <summary>JR-T-022: the look-past acts (setting on, a box room test).</summary>
        public readonly bool LookPastOn => LookPast && FreeNow && !m_LookPastOff;

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
            float len = length > 0f ? length : 0f;
            // JR-T-022c: a further element of the vehicle just judged (an artic's tractor after its trailer): one object,
            // already judged by its leading element; it only stretches the object's length (no spacing inside a vehicle).
            // Coupled only (it starts within Spacing of the object's front): the joined object never occupies more than the
            // two elements read apart would, so joining can only relax the verdict.
            if (!fixedObstacle && m_GroupOpen && SameVehicle(index, version, m_GroupIndex, m_GroupVersion) && rear >= m_GroupRear && rear <= m_GroupFront + Spacing)
            {
                if (rear + len > m_GroupFront)
                {
                    m_GroupFront = rear + len;
                    m_Occupied = m_GroupOccupied + (m_GroupFront - m_GroupRear) + Spacing;
                    if (m_PackN > 0) SetPackBlock(m_PackN - 1, (m_GroupFront - m_GroupRear) + Spacing);   // JR-T-029: one packed body
                }
                ElementsJoined++;
                if (large) Hide(index, version);
                // JR-T-022d: the view ends at the whole vehicle's front (an artic's tractor after its trailer)
                if (m_Hidden && SameVehicle(index, version, m_HiderIndex, m_HiderVersion)) m_ViewEnd = m_GroupFront;
                return;
            }
            // JR-T-022d (owner ruling 1): behind a large vehicle the driver sees nothing: not read at all (neither for nor
            // against); JR-T-022c: never hidden by itself. A light's stop line (fixed) is always seen.
            if (!fixedObstacle && HiddenFor(index, version)) { HiddenSeen++; return; }
            // JR-T-029 (owner rule 1): the driver sees at most LookPastSightMetres: a vehicle or stop line beyond it is not read
            if (rear > SightLeft) { BeyondSightSkipped++; m_GroupOpen = false; return; }
            m_GroupOpen = !fixedObstacle;
            m_GroupIndex = index; m_GroupVersion = version;
            m_GroupRear = rear; m_GroupFront = rear + len; m_GroupOccupied = m_Occupied;
            float c = credit > 0f ? credit : 0f;
            float now = rear - m_Occupied;
            float nowFree = now;
            // JR-T-027 (owner rule 20 Nov 2026, 1.2.0): an ordinary vehicle moving away carries on: it stops only at the back of
            // a visible standing queue / standing vehicle or a red stop line, which the scan reads itself (Add of a standing
            // vehicle or a light, in path order: their room counts every vehicle before them, queued at their back). So it is
            // no stopping point of its own (no prediction short of the next visible standing obstacle). A vehicle that blocks
            // the view keeps the 1.1.0 judgement: X sees nothing beyond it, so it counts only on the travel the vehicle cannot
            // avoid (its guaranteed travel less LookPastLargeMargin). Standing / crawling vehicles and lights: where they are.
            bool carriesOn = !fixedObstacle && !large && speed >= LookPastMinSpeed;
            // JR-T-029 (owner rule 3 + 4): a vehicle that stops the queue (standing / slow, a red stop line, a large vehicle):
            // the vehicles before it pack nose to tail at its back, never in a no-stop zone (zebra, other box, junction)
            byte push = 0;
            if (!carriesOn && m_ZoneS.Count > 0)
            {
                float packed = PackedRoom(rear, out push);
                if (packed < now) now = packed; else push = 0;
            }
            float tr = travel > 0f ? travel : 0f;
            if (large && !fixedObstacle) tr = tr > LookPastLargeMargin ? tr - LookPastLargeMargin : 0f;   // JR-T-022d: careful with a vehicle that blocks the view
            bool credited = speed >= LookPastMinSpeed && !fixedObstacle;
            float pred = carriesOn ? float.MaxValue : credited ? now + tr : now;
            if (now < m_MinRoom) { m_MinRoom = now; m_MinIndex = index; m_MinVersion = version; }
            if (pred < m_MinPred) { m_MinPred = pred; m_MinPredIndex = index; m_MinPredVersion = version; }
            if (!(now >= Needed))
            {
                // JR-T-027: behind a vehicle that carries on, X counts on it moving its whole length clear of X's room (the
                // road up to its predicted front: Needed + everything before it + its own length + spacing). JR-T-029 (owner
                // rule 2): that road need not be seen: nothing standing or slow within sight = the way is unblocked (the
                // 1.2.0 room_beyond_sight hold is gone). The end is kept only for the view a large vehicle ends (FinishLookPast).
                float needEnd = carriesOn ? Needed + m_Occupied + len + Spacing : Needed + m_Occupied;
                if (pred >= Needed)
                {
                    LookPastUsed = true;
                    if (needEnd > m_LookNeedEnd) m_LookNeedEnd = needEnd;
                    NoteTarget(rear, index, version);   // JR-T-022d: credited: it must be seen (buildings)
                }
                else
                {
                    Blocked = true; BlockerIndex = index; BlockerVersion = version;
                    if (now + c >= Needed) HeldForMovingQueue = true;
                    // JR-T-029 (why slowed): held only because the packed queue skipped a no-stop zone: say which kind
                    if (push != 0 && (credited ? nowFree + tr : nowFree) >= Needed) Refusal = (byte)((byte)LookPastRefusal.QueuePushedByZebra + push - 1);
                }
            }
            m_Occupied += len + Spacing;
            PushPack(len + Spacing);   // JR-T-029: packed behind a blocker further on
            Obstacles++;
            if (large && !fixedObstacle && !m_Hidden)
            {
                // JR-T-022d: the view ends at this vehicle's front (stretched when its other elements join)
                Hide(index, version);
                m_ViewEnd = rear + len;
            }
        }

        /// <summary>
        /// JR-T-022: as <see cref="AddQueued(float)"/>, with the vehicle's forward speed (m/s): with <see cref="LookPast"/> on, a
        /// vehicle the car follows through the box that is slower than <see cref="LookPastMinSpeed"/> refuses the look-past
        /// (the queue is not clearly moving). With it off exactly AddQueued(length).
        /// </summary>
        public void AddQueued(float length, float speed, int index, int version) => AddQueued(length, speed, index, version, false);

        /// <summary>
        /// JR-T-022b / 022f: as above; <paramref name="large"/> = a vehicle that blocks the view queued ahead of the car (before /
        /// inside the box). JR-T-022f (owner call B, 19 Nov 2026: "humans behind a lorry usually still judge the space"; the
        /// look-past must never be worse than off): it switches the look-past OFF for this decision: from here on every call
        /// takes the 1.0.1 path and the verdict is today's free-now verdict (no hold of its own). Large vehicles PAST the box
        /// keep ruling 1 (<see cref="AddLook"/>).
        /// </summary>
        public void AddQueued(float length, float speed, int index, int version, bool large)
        {
            if (LookPastOn && large && !Blocked) LookPastOffNow();
            if (LookPastOn && !Blocked && !m_LookSlow && !(speed >= LookPastMinSpeed))
            {
                m_LookSlow = true; m_SlowIndex = index; m_SlowVersion = version;
            }
            if (LookPastOn) m_GroupOpen = false;   // JR-T-022c: a queued element has no position: nothing joins it
            AddQueued(length);
        }

        /// <summary>
        /// JR-T-022f: the look-past is off for the rest of this decision (1.0.1 free-now rule). Room the look-past already
        /// relied on (a vehicle read before, credited on its travel) is not room free now: held, naming that vehicle, as the
        /// free-now rule would have held at it.
        /// </summary>
        private void LookPastOffNow()
        {
            m_LookPastOff = true;
            QueuedLargeFreeNow = true;
            if (LookPastUsed)
            {
                Blocked = true;
                Refusal = (byte)LookPastRefusal.LargeQueuedAhead;
                if (m_Targets > 0) { BlockerIndex = m_TI0; BlockerVersion = m_TV0; }
                else { BlockerIndex = m_MinIndex; BlockerVersion = m_MinVersion; }
                LookPastUsed = false;
            }
        }

        /// <summary>
        /// JR-T-022: the look-past verdict, after the whole stretch is read: room the free-now rule would refuse is accepted
        /// only when no vehicle the car follows through the box is slow; else the car is held as before, naming that vehicle.
        /// </summary>
        public void FinishLookPast()
        {
            if (!LookPastOn || Blocked || !LookPastUsed) return;
            if (m_LookSlow) { Blocked = true; Refusal = (byte)LookPastRefusal.SlowAhead; BlockerIndex = m_SlowIndex; BlockerVersion = m_SlowVersion; }
            // JR-T-022d: the room relied on ends beyond the large vehicle that ends the view: not seen, not clear
            else if (m_LookNeedEnd > m_ViewEnd) { Blocked = true; Refusal = (byte)LookPastRefusal.ViewEndsAtLarge; BlockerIndex = m_HiderIndex; BlockerVersion = m_HiderVersion; }
            // JR-T-029 (owner rule 2): the room relied on may end beyond the radius / the path read (1.2.0 held: room_beyond_sight)
        }

        /// <summary>
        /// JR-T-022d (owner ruling 2, 19 Nov 2026: "building = blocked"; "the building is a box and higher than the car"; "just
        /// calculate the straight-line geometry"): after <see cref="FinishLookPast"/>, only when the look-past is about to give
        /// the room: every target it relies on must be seen in a straight line from the driver: each vehicle it credited (its
        /// rear) and the end of the room relied on. <paramref name="sight"/> answers for a path distance (metres past the window
        /// start); false = a building (or anything the caller tests) is in the way: the look-past credit is withdrawn and the
        /// car is held as by the free-now rule, naming the vehicle not seen (the end of the room: the one with least room).
        /// </summary>
        public void FinishSight<T>(ref T sight) where T : struct, ISightTest
        {
            if (!LookPastOn || Blocked || !LookPastUsed) return;
            for (int k = 0; k < m_Targets; k++)
            {
                float d; int i, v;
                switch (k)
                {
                    case 0: d = m_T0; i = m_TI0; v = m_TV0; break;
                    case 1: d = m_T1; i = m_TI1; v = m_TV1; break;
                    case 2: d = m_T2; i = m_TI2; v = m_TV2; break;
                    default: d = m_T3; i = m_TI3; v = m_TV3; break;
                }
                if (!sight.Seen(d)) { BuildingCut = true; Refusal = (byte)LookPastRefusal.Building; Blocked = true; BlockerIndex = i; BlockerVersion = v; return; }
            }
            // JR-T-029 (owner rule 2): the end of the room relied on need not be seen (1.2.0 tested it here too)
        }

        /// <summary>JR-T-022d (tests): the credited vehicles kept as sight targets.</summary>
        public readonly int SightTargets => m_Targets;

        /// <summary>JR-T-022d (tests): where the room the look-past relies on ends (metres past the window start).</summary>
        public readonly float LookNeedEnd => m_LookNeedEnd;

        /// <summary>
        /// JR-T-022: a cut of the sight <paramref name="at"/> metres past the window start (along the path): where the path
        /// leaves the sight radius (JR-T-022b) or the end of the path read; the next junction on the path only when the
        /// driver's position is not known (JR-T-022d: otherwise buildings are tested on the straight line, FinishSight). The
        /// look-past does not count on room beyond it.
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
            if (LookPastOn) PushPack((length > 0f ? length : 0f) + Spacing);   // JR-T-029: it packs behind a blocker too
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
                    // JR-T-029 (owner rule 2): no sight test on the room's end (nothing slow within sight = unblocked)
                    LookPastUsed = true;
                    if (Needed + m_Occupied > m_LookNeedEnd) m_LookNeedEnd = Needed + m_Occupied;
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

        public readonly bool Done(float pathRead)
        {
            if (Blocked) return true;
            if (!LookPastOn) return pathRead >= Needed + m_Occupied;
            // JR-T-029: nothing beyond the sight is read; a blocker past the packed reach (no-stop zones skipped) leaves the room
            return pathRead >= SightLeft || pathRead >= PackedReach();
        }

        /// <summary>
        /// The verdict. When the car's known path ends before the scan is done (no more navigation lanes, a non-road
        /// lane such as a parking or building entrance, or the lane cap) the unknown rest counts as free: the car's own
        /// destination lies there and holding for it could hold forever.
        /// </summary>
        public readonly bool HasRoom => !Blocked;
    }

    /// <summary>
    /// JR-T-022d: the driver sees the point of its path <paramref name="pathMetres"/> past the window start in a straight line
    /// (no building footprint in the way). The job tests the game's buildings (Stage2/JRSight.cs), the tests a list of boxes.
    /// Burst: used only as a generic struct constraint.
    /// </summary>
    public interface ISightTest
    {
        bool Seen(float pathMetres);
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
