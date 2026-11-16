using System.Runtime.CompilerServices;

namespace JunctionRules.Core
{
    /// <summary>
    /// 0.3.6 (JR-T-008, owner ask 16 Nov 2026): setting "People pass a car that blocks a crossing after (s)", default
    /// 2 s (owner ruling 16 Nov), 0 = off (the game's behaviour). Cars still never push through people (0.3.2); people
    /// pass through a car that stands on their crossing.
    ///
    /// Stock (Game.dll 1.6.4f1 decompile): a person walking a pedestrian lane never collides with cars on a curve lane
    /// (CreatureCollisionIterator 112-146: only the person's own lane objects and the static tree), but the TARGET step
    /// stops at a crossing: CreatureTargetIterator.CheckOverlapLane (62-103) clamps the person's target to the near edge
    /// of the overlap with a road / track lane while a Moving object (a car, standing or not) on that lane covers the
    /// overlap, blocker = that car, type Crossing (90-94). There is no timer: the person waits as long as the car stands.
    /// HumanNavigationSystem (MoveLaneTarget 1503-1531) writes the clamped delta into HumanCurrentLane.m_CurvePosition.x.
    /// The pass moves m_CurvePosition.x to the far edge of that overlap; the game's own check then skips it
    /// (IterateLane tests only overlaps whose near edge is at or after x, 47-54) and the person walks on through the car.
    ///
    /// Cyclists are vehicles: a bicycle riding a pedestrian lane meets a car standing on a crossed road lane in
    /// CarLaneSpeedIterator.CheckOverlappingLanes / UpdateMaxSpeed (full stop, blocker = the car, type Crossing). The game's
    /// only give-up is the IgnoreBlocker creep (3 m/s), granted by CarNavigationSystem only for a blocker with no Moving
    /// component (CheckBlocker 1365-1410, FillNavigationPaths 1123-1140), so a standing car (Moving, zero speed) holds a
    /// cyclist for good. The pass sets that same IgnoreBlocker flag after the wait; the game clears it when the blocker
    /// changes or the bicycle moves to its next lane.
    ///
    /// Pure logic, Burst-compatible (static methods on plain values).
    /// </summary>
    public static class PeoplePass
    {
        /// <summary>Owner ruling 16 Nov 2026: 2 s.</summary>
        public const float DefaultSeconds = 2f;

        /// <summary>Slider range of the setting (0 = off).</summary>
        public const float MinSeconds = 0f;
        public const float MaxSeconds = 30f;
        public const float StepSeconds = 0.5f;

        /// <summary>Simulation frames per second of game time (HumanNavigationSystem: 16 frames = 4/15 s).</summary>
        public const float FramesPerSecond = 60f;

        /// <summary>A car slower than this (m/s) counts as standing on the crossing.</summary>
        public const float StandingSpeed = 0.5f;

        /// <summary>
        /// Tolerance (curve delta) for "the person's target is at the overlap edge": overlap bounds are bytes / 255
        /// (LaneOverlap), and the game's clamp writes exactly that value, so a small fraction of one step is enough.
        /// </summary>
        public const float EdgeTolerance = 0.25f / 255f;

        /// <summary>The setting as used: NaN or negative = off (0), above the slider range clamped.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Sanitize(float seconds)
        {
            if (!(seconds > 0f)) return 0f;
            return seconds > MaxSeconds ? MaxSeconds : seconds;
        }

        /// <summary>
        /// The wait the system uses: 0 (does nothing, the game's behaviour) when the "People pass a car that blocks them"
        /// switch is off, else the sanitized slider value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Effective(bool switchOn, float seconds) => switchOn ? Sanitize(seconds) : 0f;

        /// <summary>Whether the rule acts at all.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool On(float seconds) => Sanitize(seconds) > 0f;

        /// <summary>
        /// Whether a hold that began at <paramref name="startFrame"/> has lasted the setting by <paramref name="frame"/>
        /// (unsigned frame difference, so a wrap of the frame counter is harmless).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Due(uint startFrame, uint frame, float seconds)
        {
            float s = Sanitize(seconds);
            if (s <= 0f) return false;
            uint held = unchecked(frame - startFrame);
            return held >= s * FramesPerSecond - 0.5f;
        }

        /// <summary>Whether a blocking car counts as standing (its speed along any direction).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Standing(bool hasMoving, float speedSq) => !hasMoving || speedSq < StandingSpeed * StandingSpeed;

        /// <summary>
        /// For one overlap of the person's lane with a road / track lane (bounds already / 255): whether the person's
        /// target was clamped at its near edge by the game's check, and if so the curve delta just past its far edge.
        /// Forward walk (end &gt; x): the game clamps at <paramref name="start"/>, the pass moves x to <paramref name="end"/>.
        /// Backward walk (end &lt; x): the game clamps at <paramref name="end"/> (the overlap's high bound), the pass moves
        /// x to <paramref name="start"/>. Mirrors CreatureTargetIterator.IterateLane 47-54.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool PastOverlap(float x, float pathEnd, float start, float end, out float newX)
        {
            newX = x;
            if (pathEnd > x)
            {
                if (abs(x - start) <= EdgeTolerance && end > x)
                {
                    newX = end > pathEnd ? pathEnd : end;
                    return true;
                }
            }
            else if (pathEnd < x)
            {
                if (abs(x - end) <= EdgeTolerance && start < x)
                {
                    newX = start < pathEnd ? pathEnd : start;
                    return true;
                }
            }
            return false;
        }

        /// <summary>The overlap flags the game's check considers: road or track, and not a merge (IterateLane 44).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CountsAsCrossing(bool roadOrTrack, bool merge) => roadOrTrack && !merge;

        /// <summary>Whether the other lane's object covers the overlap's range on that lane (CheckOverlapLane 88-90).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Covers(float objA, float objB, float otherStart, float otherEnd)
        {
            float lo = objA < objB ? objA : objB;
            float hi = objA < objB ? objB : objA;
            return lo <= otherEnd && hi >= otherStart;
        }

        /// <summary>
        /// Cyclist side: whether a bicycle held by <paramref name="blockerIsCar"/> (a motor vehicle, not a bicycle), with
        /// blocker type Crossing, on a pedestrian lane, gets the IgnoreBlocker creep now.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CyclistHeld(bool onPedestrianLane, bool blockerIsCar, bool blockerIsBicycle, bool crossingType, bool carStanding) =>
            onPedestrianLane && blockerIsCar && !blockerIsBicycle && crossingType && carStanding;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float abs(float v) => v < 0f ? -v : v;
    }
}

namespace JunctionRules.Core
{
    /// <summary>0.3.6 (JR-T-008): the timing-log part for the people-pass counters.</summary>
    public static class PeoplePassCounts
    {
        /// <summary>
        /// "; people passed blocking cars N (cyclists C)"; negative = not counted in this window (the people-pass system is
        /// off, not registered, or started inside the window).
        /// </summary>
        public static string Format(long people, long cyclists)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (people < 0 || cyclists < 0) return "; people passed blocking cars n/a";
            return "; people passed blocking cars " + people.ToString(ci) + " (cyclists " + cyclists.ToString(ci) + ")";
        }
    }
}
