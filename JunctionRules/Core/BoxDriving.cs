using System.Runtime.CompilerServices;

namespace JunctionRules.Core
{
    /// <summary>
    /// 0.4.2 (JR-T-010, owner rule 16 Nov 2026, UK yellow box): setting "Cars obey yellow boxes" (default on). Applies only
    /// to the boxes the owner placed with the 0.4.0 tool (the saved YellowBoxJunction / YellowBoxEntrance components).
    ///
    /// Junction box (the boxed node): a car does not enter the box (the junction lane's start, which is the stop line)
    /// unless there is room for its whole length (trailers included) plus the room margin on its exit beyond the box.
    /// "Room" is #14's measure (<see cref="RoomScan"/>, shared, not duplicated): a moving queue counts the braking
    /// distance of the vehicle ahead, a standing one does not; vehicles already inside the box ahead of it on its own
    /// path take exit room first (<see cref="RoomScan.AddQueued"/>). A car already in the box (its current lane belongs
    /// to the boxed node, or its front is past the line) carries on; a car that cannot stop at the line any more (the
    /// game's own can-stop test) carries on too.
    ///
    /// With the Whole junction gap rule the box also moves the whole-path decision to the stop line (no car waits in
    /// the box), except the UK right-turn exception (setting, default on): a car turning across oncoming traffic gets a
    /// second waiting spot inside the box, just before its first conflict with an oncoming straight lane (the same pair
    /// Core/TurnAcross.cs classifies: a Forward lane whose end edge is the turner's start edge). Points before that
    /// spot are decided at the line, the oncoming ones (and anything after them) at the turn spot. So the turner enters
    /// when its exit has room and the way to the turn spot is clear, and only oncoming traffic stops it in the box. With
    /// the stock gap rule there are no waiting spots to move: the box is the room test at the line only.
    ///
    /// Entrance box (a band across the carriageway in front of a building's access, the painted length centred on the
    /// access point): a main-road car does not enter the band unless there is room for it past the band; a car leaving
    /// the driveway does not join the road (its join point inside the band) unless there is room past the band on its
    /// target lane; a car turning into the driveway (its path on the road lane ends inside the band) was exempt until 0.4.7;
    /// JR-T-014c (G9): it does not enter the band unless its way into the building has room for its whole length.
    ///
    /// Pure arithmetic on plain values (Burst-compatible, no allocation). Job side: Stage2/JRYellowBox.cs.
    /// </summary>
    public static class BoxDriving
    {
        /// <summary>Where a car stands relative to the box entry it is tested against.</summary>
        public enum Approach
        {
            /// <summary>Front past the entry (or already on a lane of the box): carry on.</summary>
            Inside = 0,
            /// <summary>The game's own can-stop test fails at the entry: carry on (as the 0.2.1 decision fix).</summary>
            CannotStop = 1,
            /// <summary>Before the entry and able to stop there: the room test decides.</summary>
            Check = 2,
        }

        /// <summary>
        /// wait = the game's stop distance to the entry (origDistance after its margin, the num5 formula), margin = the
        /// game's stop margin, braking = VehicleUtils.GetBrakingDistance(car, speed, timeStep), inBox = the car's current
        /// lane is a lane of the box.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Approach Classify(bool inBox, float wait, float margin, float braking)
        {
            if (inBox || WholePathRule.FrontPastSpot(wait, margin)) return Approach.Inside;
            return braking <= wait + margin ? Approach.Check : Approach.CannotStop;
        }

        /// <summary>The car is held at the entry: before it, able to stop, and no room past the box.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Holds(Approach a, bool hasRoom) => a == Approach.Check && !hasRoom;

        /// <summary>The UK right-turn exception applies to this lane (setting on, the lane turns across oncoming traffic).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TurnerMayWait(bool exceptionOn, uint ownCarLaneFlags, bool leftHandTraffic) =>
            exceptionOn && TurnAcross.IsTurnAcross(ownCarLaneFlags, leftHandTraffic);

        /// <summary>The other lane is the turner's oncoming straight (Core/TurnAcross.cs pair test).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsOncomingStraight(uint otherCarLaneFlags, bool ownStartIsOtherEnd) =>
            (otherCarLaneFlags & TurnAcross.Forward) != 0 && ownStartIsOtherEnd;

        /// <summary>
        /// JR-T-013b (G3): the turner's room test runs again at its turn spot. A turner that entered the box with room on its
        /// exit can find the exit full by the time the oncoming gap comes (the exit queue grew while it waited, an oncoming
        /// left-turner took the room); it must then stay at the turn spot, clear of the oncoming lane, instead of crossing
        /// and stopping across it. Applies while the turn spot is the car's next spot (front past the line and every earlier
        /// spot, not yet past the turn spot; <paramref name="turnIndex"/> = its index, -1 = none) and the car can still stop
        /// before it (the game's can-stop test). The exit room itself is the line's test (RoomScan via HasRoomPast).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TurnSpotRoomCheck(int turnIndex, int next, int spotCount, bool canStopBefore) =>
            turnIndex > 0 && turnIndex < spotCount && next == turnIndex && canStopBefore;

        /// <summary>
        /// The waiting spots of a boxed junction lane under the Whole junction rule: the stop line (0) and, for a turner
        /// with the exception, its turn spot (driving coordinates, 2 = none). False when the lane has no conflict point
        /// (the rule does not cover it: stock).
        /// </summary>
        public static bool JunctionSpots(bool hasConflict, float turnSpot, out WaitingSpots spots)
        {
            spots = default;
            if (!hasConflict) return false;
            spots.Add(0f);
            if (turnSpot > WholePathRule.SpotEpsilon && turnSpot < 1f) spots.Add(turnSpot);
            return true;
        }

        /// <summary>
        /// The entrance band on a lane, driving coordinates (0 = where the car enters the lane): the access point at lane
        /// offset <paramref name="accessOffset"/> (game offset), <paramref name="bandLength"/> metres long, clipped to the lane.
        /// False when the band misses the lane or is shorter than 0.5 m on it.
        /// </summary>
        public static bool Band(float accessOffset, float laneLength, float bandLength, bool inverse, out float b0, out float b1)
        {
            b0 = b1 = 0f;
            if (!(laneLength > 0f) || !(bandLength > 0f)) return false;
            float h = bandLength * 0.5f / laneLength;
            float lo = accessOffset - h, hi = accessOffset + h;
            if (lo < 0f) lo = 0f;
            if (hi > 1f) hi = 1f;
            if ((hi - lo) * laneLength < 0.5f) return false;
            b0 = inverse ? 1f - hi : lo;
            b1 = inverse ? 1f - lo : hi;
            return true;
        }

        /// <summary>
        /// 0.4.10 (sweep): a light that holds a car at a junction lane inside a box's room window: red (Stop) or amber
        /// (SafeStop: the car stops if it can). Values = Game.Net.LaneSignalType.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool LightStopsInWindow(int signal) => signal == WholePathRule.SignalStop || signal == WholePathRule.SignalSafeStop;

        /// <summary>
        /// 0.4.10 (sweep): a band [b0, b1] (driving coordinates) of a later path lane whose part on the path [startD, endD]
        /// starts inside the room window (read = metres from the window start to where the path enters this lane, needed
        /// = the room window so far): the room is then needed past the band (pastEnd = metres from the window start).
        /// </summary>
        public static bool BandInWindow(float b0, float b1, float startD, float endD, float laneLength, float read, float needed, out float pastEnd)
        {
            pastEnd = 0f;
            if (!(b1 > startD) || !(b0 < endD)) return false;
            float from = read + (b0 > startD ? b0 - startD : 0f) * laneLength;
            if (!(from < needed)) return false;
            pastEnd = read + ((b1 < endD ? b1 : endD) - startD) * laneLength;
            return true;
        }

        /// <summary>0.4.9 (G15b): the overlap [t0, t1] on this lane (driving coordinates) lies in the window from the band entry to the end of the room needed.</summary>
        public static bool SideOverlapInWindow(float t0, float t1, float from, float windowEnd) => t1 > from && t0 < windowEnd;

        /// <summary>0.4.9 (G15b): a body [rear, front] (metres on the driveway lane) still covers the lane's overlap with the road [oS, oE].</summary>
        public static bool SideBodyCovers(float rear, float front, float oS, float oE) => front > oS && rear < oE;

        /// <summary>A car's role at an entrance band on one road lane.</summary>
        public enum EntranceRole
        {
            /// <summary>The band is not on the car's path on this lane.</summary>
            None = 0,
            /// <summary>
            /// The path ends inside the band (turning into the driveway). 0.4.2-0.4.7: exempt. JR-T-014c (G9): held before
            /// the band unless its way in has room for the whole vehicle (<see cref="EnteringApplies"/>).
            /// </summary>
            Entering = 1,
            /// <summary>Driving along the road through the band: entry = the band start.</summary>
            Main = 2,
            /// <summary>Joining the road inside the band (leaving the driveway): entry = the join point.</summary>
            Leaving = 3,
        }

        /// <summary>
        /// JR-T-014c (G9, owner 17 Nov 2026): an Entering car is judged only when its path really leaves the road inside the
        /// band (the next path lane exists and is not a lane of the same road edge). A lane change inside the band also ends
        /// the path on this lane early: that car stays on the road and is not an entry.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EnteringApplies(bool hasNextLane, bool nextOnSameRoad) => hasNextLane && !nextOnSameRoad;

        /// <summary>
        /// JR-T-014c (G9): where an entering car must stop when its way in is full: the band start, or where its range on
        /// this lane begins when that is already inside the band (it came onto this lane inside the band).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float EnteringEntry(float start, float b0) => start > b0 ? start : b0;

        /// <summary>
        /// JR-T-014c (G9): an entering car is held at the band entry when it can stop there and its way in (the path lanes
        /// after the road lane: the drive's connection lane, the drive, the building lane) has no room for its whole length
        /// plus the margin (RoomScan, the same measure as boxes and zebras: a moving queue gives its braking distance, a
        /// standing one does not; a path that ends inside the building before the room is read counts as free).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EnteringHolds(Approach a, bool roomInside) => Holds(a, roomInside);

        /// <summary>
        /// The role from the car's range on the lane [start, end] (driving coordinates) and the band [b0, b1]; entry = the
        /// point the car must not pass without room past b1.
        /// </summary>
        public static EntranceRole Role(float start, float end, float b0, float b1, out float entry)
        {
            entry = 0f;
            const float eps = WholePathRule.SpotEpsilon;
            if (start > b1 - eps || end < b0 + eps) return EntranceRole.None;
            if (end < b1 - eps && end < 1f - eps) return EntranceRole.Entering;
            if (start > b0 + eps)
            {
                entry = start;
                return EntranceRole.Leaving;
            }
            entry = b0;
            return EntranceRole.Main;
        }
    }
}

namespace JunctionRules.Core
{
    /// <summary>0.4.2 (JR-T-010-r): a list of keys kept in ascending order.</summary>
    public interface IKeyList
    {
        int Count { get; }
        long Key(int i);
    }

    /// <summary>
    /// 0.4.2 (JR-T-010-r, review item 1): the set of road edges that carry a boxed building access, kept sorted so the
    /// entrance-band test asks one binary search per edge lane instead of looping the edge's ConnectedBuilding buffer.
    /// Burst-compatible, no allocation. Job side: Stage2/JRBoxEdges.cs.
    /// </summary>
    public static class SortedKeys
    {
        /// <summary>Binary search: the key is in the (ascending) list.</summary>
        public static bool Contains<T>(ref T keys, long key) where T : struct, IKeyList
        {
            int lo = 0, hi = keys.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                long k = keys.Key(mid);
                if (k == key) return true;
                if (k < key) lo = mid + 1;
                else hi = mid - 1;
            }
            return false;
        }

        /// <summary>
        /// 0.4.3: first index whose key equals <paramref name="key"/> in an ascending list that may repeat keys (one edge
        /// with several boxed exits); -1 when absent.
        /// </summary>
        public static int First<T>(ref T keys, long key) where T : struct, IKeyList
        {
            int lo = 0, hi = keys.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (keys.Key(mid) < key) lo = mid + 1;
                else hi = mid;
            }
            return lo < keys.Count && keys.Key(lo) == key ? lo : -1;
        }

        /// <summary>The key of an entity (index, version): a rebuilt edge that reuses an index has another key.</summary>
        public static long KeyOf(int index, int version) => ((long)index << 32) | (uint)version;

        /// <summary>Where <paramref name="key"/> goes to keep the list ascending; -1 when it is already there.</summary>
        public static int InsertAt<T>(ref T keys, long key) where T : struct, IKeyList
        {
            int lo = 0, hi = keys.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                long k = keys.Key(mid);
                if (k == key) return -1;
                if (k < key) lo = mid + 1;
                else hi = mid - 1;
            }
            return lo;
        }
    }
}
