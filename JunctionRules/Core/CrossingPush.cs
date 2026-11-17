using System.Runtime.CompilerServices;

namespace JunctionRules.Core
{
    /// <summary>
    /// 0.3.2 (JR-T-004, owner ask 15 Nov 2026: "keep stopping until it has a gap, no timeout"): setting "Cars never push
    /// through people on crossings" (default on; acts while the Junction Rules copy drives, with either gap rule).
    ///
    /// The game lets a car creep through people on a pedestrian lane its path crosses (zebra, junction crosswalk,
    /// footway across a driveway) in two ways, both in CarLaneSpeedIterator.CheckPedestrian (copy: Vanilla/
    /// JRCarLaneSpeedIterator.cs, the line after block S2-08):
    /// <c>position = (m_PushBlockers || !Moving || lengthsq(velocity) &lt; 0.01) ? max(3, GetMaxBrakingSpeed(car, d, 3, t))
    /// : GetMaxBrakingSpeed(car, d, t)</c>, i.e. at least 3 m/s instead of a stop:
    /// - <see cref="PushReason.Timer"/>: the car's PushBlockers lane flag, set by CarNavigationSystem.UpdateNavigationTarget
    ///   once the car has been held by people (blocker type Temporary) for 5 s of lane duration, cleared only after 5 s
    ///   without (copy: Vanilla/JRCarNavigationSystem.cs "currentLane.m_Duration &gt;= 5f"). This is the owner's
    ///   "waits a few seconds, then drives through the pedestrians".
    /// - <see cref="PushReason.Standing"/>: the person has no Moving component (a collapsed accident victim:
    ///   AccidentCreatureSystem.StopMoving) or moves slower than 0.1 m/s: the car creeps at once, no wait at all.
    /// With the setting on, the person is braked for exactly as a walking person in the first seconds (full stop), so the
    /// car stays stopped until the crossing is clear.
    ///
    /// Cyclists on a crossing (a bicycle riding a pedestrian lane, PedestrianLaneFlags.AllowBicycle) are vehicles, not
    /// people, to the game: they go through UpdateMaxSpeed, whose only give-up is the IgnoreBlocker creep (m_Ignore, 3 m/s)
    /// that CarNavigationSystem grants after IsBlocked, which CheckBlocker sets only for a blocker without a Moving
    /// component (a stopped vehicle). With the setting on that creep is refused when the ignored blocker is a bicycle on a
    /// pedestrian lane of the car's path. Car against car stays exactly stock (also a bicycle on a road lane).
    ///
    /// Pure logic, Burst-compatible (static methods on plain values).
    /// </summary>
    public static class CrossingPush
    {
        /// <summary>The game's "standing" test in CheckPedestrian: lengthsq(velocity) &gt;= 0.01 counts as walking.</summary>
        public const float WalkingSpeedSq = 0.01f;

        /// <summary>The game's creep speed through a pushed person (CheckPedestrian) or an ignored blocker (UpdateMaxSpeed).</summary>
        public const float CreepSpeed = 3f;

        /// <summary>Why the game would let the car creep through a person instead of stopping.</summary>
        public enum PushReason
        {
            /// <summary>A walking person, no timer: the game stops for them (full braking).</summary>
            None = 0,
            /// <summary>The 5 s PushBlockers timer of the car.</summary>
            Timer = 1,
            /// <summary>The person stands (no Moving component or slower than 0.1 m/s).</summary>
            Standing = 2,
        }

        /// <summary>
        /// The game's own branch choice in CheckPedestrian, as a reason. Mirrors
        /// <c>m_PushBlockers || !m_MovingData.TryGetComponent(obj, out v) || !(math.lengthsq(v.m_Velocity) &gt;= 0.01f)</c>
        /// (the negated comparison keeps a NaN speed on the creep side, as the game does).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PushReason Pedestrian(bool pushBlockers, bool hasMoving, float speedSq)
        {
            if (pushBlockers) return PushReason.Timer;
            if (!hasMoving || !(speedSq >= WalkingSpeedSq)) return PushReason.Standing;
            return PushReason.None;
        }

        /// <summary>
        /// The speed the car uses for one person after the game's own check: the game's value, or with the setting on and
        /// a push reason, the full-stop braking speed (the game's walking-person value). fullStop &lt;= stock always
        /// (the creep adds 3 m/s squared under the root and floors at 3), so this only ever lowers the limit.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float PedestrianSpeedInPath(bool settingOn, PushReason reason, bool personInPath, float stockSpeed, float fullStopSpeed) =>
            personInPath ? PedestrianSpeed(settingOn, reason, stockSpeed, fullStopSpeed) : stockSpeed;

        /// <summary>
        /// 0.4.11 (JR-T-017, gap G23, owner live 17 Nov: "what makes this car not want to come out on green?", Crescent St x
        /// Eastgate Lane): the game's CheckPedestrian also brakes for a person who is NOT on the car's way over the crossing
        /// but stands within the car's half width + the person's size of it (2026-11-13 copy, CheckPedestrian: targetOffset
        /// outside the car's range on the pedestrian lane, then the distance to the overlap line); for a standing person it
        /// only creeps by (3 m/s). A short left turn (left-hand traffic) hugs the corner where people wait at the kerb for
        /// their own green: 0.3.2's full stop held such a car for people who were not crossing, until they got their green,
        /// i.e. until the car's red (live: hatchback 910624, queue head, blocker type Temporary = a person, 22 queued).
        /// Now the full stop applies only to a person whose position on the crossing lies inside the car's range on it
        /// (personPos, rangeA / rangeB = game offsets on the pedestrian lane, laneLength in metres) widened by
        /// <see cref="InPathToleranceMetres"/>; a person beside it gets the game's own creep past.
        /// </summary>
        public static bool PersonInPath(float personPos, float rangeA, float rangeB, float laneLength)
        {
            float lo = (rangeA < rangeB ? rangeA : rangeB) * laneLength - InPathToleranceMetres;
            float hi = (rangeA < rangeB ? rangeB : rangeA) * laneLength + InPathToleranceMetres;
            float p = personPos * laneLength;
            return p >= lo && p <= hi;
        }

        /// <summary>0.4.11 (G23): metres either side of the car's range on the crossing within which a standing person counts as on its way.</summary>
        public const float InPathToleranceMetres = 0.5f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float PedestrianSpeed(bool settingOn, PushReason reason, float stockSpeed, float fullStopSpeed) =>
            settingOn && reason != PushReason.None && fullStopSpeed < stockSpeed ? fullStopSpeed : stockSpeed;

        /// <summary>
        /// The m_Ignore entity the game's UpdateMaxSpeed gets for one object on an overlapping lane: unchanged, or none
        /// when the setting is on and the object is the car's ignored blocker AND a bicycle on a pedestrian lane (a cyclist
        /// on a crossing). The caller compares the entities; the core stays game-independent.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RefuseIgnore(bool settingOn, bool objectIsIgnored, bool objectIsBicycle, bool laneIsPedestrian) =>
            settingOn && objectIsIgnored && objectIsBicycle && laneIsPedestrian;
    }
}
