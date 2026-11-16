using System.Runtime.CompilerServices;
using System.Threading;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Pathfind;
using Game.Prefabs;
using Game.Vehicles;
using JunctionRules.Core;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace JunctionRules.Stage2
{
    /// <summary>
    /// Stage 2, job side: the whole-junction rule as the copy's lane-speed iterator (Vanilla/JRCarLaneSpeedIterator.cs)
    /// calls it from its JR-STAGE2 blocks. One value lives in each iterator (one per car and step, a local of the
    /// navigation job), so all state here is per car; nothing is shared between threads except the debug counters and
    /// the safety-event table (Interlocked, debug only). Burst-compatible: blittable fields, no managed allocations.
    ///
    /// Per junction lane (BeginLane, once per CheckOverlappingLanes call): find the entry spot W (0.2.3: the only spot;
    /// the whole path is one decision), where the car is and the first uncrossed point. Per overlap (InScope): classify
    /// it (Core/StretchRule.cs: Deciding / Safety / Crossing / Stock) and, when Deciding or Safety, the car's own arrival
    /// time at it (0.2.3, Core/ArrivalTime.cs, the game's own motion). Per stock yield demand: leave the game's value
    /// (Stock), stop at W or before the first uncrossed point (StopAtSpot) or drop the give-way (Suppress). 0.2.3 also
    /// projects the other cars to that arrival time: the stock 2 s look-ahead becomes 2 s + arrival time
    /// (AfterProjection) and cars on the lanes feeding a conflicting lane count as virtual reservations
    /// (FeedingLaneYield). With m_Mode == 0 every entry point returns at its first test.
    /// </summary>
    public partial struct JRWholeJunction
    {
        /// <summary>The stock reservation time step (CarNavigationSystem.ReserveNavigationLanes: timeStep = 4f / 15f).</summary>
        public const float ReservationTimeStep = 4f / 15f;

        public ComponentLookup<NodeLane> m_NodeLaneData;
        // 0.2.3: read-only stock lookups for the feeding lanes of a conflicting junction lane (the job's own read-only
        // m_OwnerData / m_LaneData / m_Lanes, plus the S2-20 ConnectedEdge lookup).
        public ComponentLookup<Owner> m_OwnerData;
        public ComponentLookup<Lane> m_LaneData;
        public BufferLookup<Game.Net.SubLane> m_SubLanes;
        public BufferLookup<ConnectedEdge> m_ConnectedEdges;
        /// <summary>Debug counters (created only when counting; the job's persistent array).</summary>
        public NativeArray<long> m_Counters;
        /// <summary>Stage2Mode bits; 0 = off. Never holds Stage2Mode.NoPushPeople (stripped in Create, see m_NoPushPeople).</summary>
        public int m_Mode;
        /// <summary>
        /// 0.3.2 (JR-T-004): setting "Cars never push through people on crossings" (Stage2Mode.NoPushPeople, kept out of
        /// m_Mode so the 0.2.x code's "m_Mode == 0" exits are unchanged). Read only by blocks S2-08 / S2-08b / S2-11.
        /// </summary>
        public bool m_NoPushPeople;
        /// <summary>
        /// 0.3.3 (JR-T-006): the whole vehicle's length, trailers included (Stage2/JRVehicleLength.cs, the JR-T-002d
        /// length), handed in by S2-21 as a per-car local (never a job field). Used only for the crossing fit test.
        /// </summary>
        public float m_VehicleLength;

        /// <summary>Set by IterateFirstLane (S2-02): the car's current lane(s); feeding lanes equal to them are its own approach.</summary>
        public Entity m_CurrentLane;
        public Entity m_CurrentLane2;

        // ---- state of the junction lane being checked (BeginLane) ----
        public bool m_LaneActive;
        /// <summary>The car's front is past the entry spot W (0.2.0 "committed").</summary>
        public bool m_Committed;
        public bool m_Inverse;
        /// <summary>Entry spot W in the game's lane offsets (as 0.2.0).</summary>
        public float m_Spot;
        /// <summary>Spot, stretch and the car's place in it (Core/StretchRule.cs; one stretch since 0.2.3).</summary>
        public StretchState m_Stretch;
        /// <summary>Set by IterateFirstLane, cleared by IterateNextLane: the lane being checked is the car's current lane.</summary>
        public bool m_FirstLane;
        /// <summary>The car's front is past the entry spot of its current lane (it is inside the junction's paths).</summary>
        public bool m_Inside;
        private Entity m_LaneEntity;
        /// <summary>0.3.3: the lane's overlap buffer (BeginLane), for the crossing fit test (Core/CrossingPoint.cs).</summary>
        private DynamicBuffer<LaneOverlap> m_LaneOverlaps;

        // ---- 0.2.3: the car's own motion for the arrival time (BeginLane) ----
        private CarData m_Car;
        private float m_Speed;
        private float m_TimeStep;
        /// <summary>The junction lane's stock drive speed (GetMaxDriveSpeed: speed limit and turn cap, lane wear).</summary>
        private float m_DriveSpeed;
        /// <summary>Metres from the car's front to the start of the junction lane (0 when on it).</summary>
        private float m_DriveFrom;
        private float m_DeltaU;
        private float m_DeltaValue;
        private bool m_OwnStartRead;
        private bool m_HasOwnStart;
        private PathNode m_OwnStart;
        /// <summary>Diagnostics: the car stands at its entry spot (points are sampled for the timing log).</summary>
        private bool m_AtSpot;

        // ---- the overlap being checked (InScope, once per overlap before the game's yield and object checks) ----
        public PointPhase m_OvPhase;
        public float m_OvStopWait;
        public float m_OvStart;
        /// <summary>0.2.3: the car's own arrival time at this overlap (Deciding / Safety), else 0.</summary>
        public float m_OvDelta;

        // ---- per-car results for the debug counters ----
        public bool m_Decision;
        public bool m_HasLater;
        public float m_LaterSpeed;
        public bool m_HasInsideYield;
        public float m_InsideYieldSpeed;
        public bool m_HasInsideCrossing;
        public float m_InsideCrossingSpeed;
        public bool m_HasSafety;
        public float m_SafetySpeed;
        public bool m_ProjectedLaneHit;
        public bool m_FeedingLaneHit;

        // ---- 0.2.4: cheap feeding scan (per car-update; Core/FeedingScan.cs) ----
        private FeedScanMemo<PathNode> m_FeedMemo;
        private int m_FeedCarsRead;
        private bool m_AheadRead;
        private bool m_HasAhead;
        // ---- 0.2.4: turning across oncoming gives way (Core/TurnAcross.cs) ----
        /// <summary>This overlap: the other lane is a turner that gives way to the car and its reservation alone does not hold it.</summary>
        private bool m_OvDiscountReservation;
        private bool m_OwnTurnRead;
        private uint m_OwnFlags;
        private PathNode m_OwnTurnStart;
        private PathNode m_OwnTurnEnd;
        private int m_TurnOverrides;
        private int m_TurnDiscounts;
        // diagnostics (counted only when counting)
        private int m_FeedScans;
        private int m_FeedFollowerSkips;
        private int m_FeedMemoSkips;
        private int m_FeedCapHits;
        // ---- 0.3.2: crossing give-ups refused this car-step (counted only when counting) ----
        private bool m_RefusedTimer;
        private bool m_RefusedStanding;
        private bool m_RefusedCyclist;
        // ---- 0.3.3: unsafe-lane feeding skips; crossings judged at the crossing / kept at W (counted only when counting) ----
        private bool m_FeedUnsafeSkip;
        private bool m_CrossingAtCrossing;
        private bool m_CrossingNoFit;

        // ---- 0.4.1 (JR-T-009): cars never stop on a zebra (Core/ZebraSections.cs) ----
        /// <summary>Setting "Cars never stop on a zebra" (Stage2Mode.ZebraClear, stripped from m_Mode in Create).</summary>
        public bool m_ZebraRule;
        /// <summary>The owner's room margin past a zebra (m).</summary>
        public float m_RoomMargin;
        /// <summary>The car's navigation lanes (S2-26), for the room-past scan beyond the junction lane.</summary>
        private DynamicBuffer<CarNavigationLane> m_Path;
        private bool m_HasPath;
        /// <summary>The lane being checked has zebras and its spots are the zebra sections (BeginZebras).</summary>
        public bool m_ZebraActive;
        private ZebraLayout m_ZebraLayout;
        /// <summary>Zebra starts of the active lane (driving coordinates).</summary>
        private WaitingSpots m_ZebraStarts;
        /// <summary>The overlap being checked is a zebra of the active lane.</summary>
        private bool m_OvZebra;
        private bool m_HasZebraPeople;
        private float m_ZebraPeopleSpeed;
        private bool m_HasZebraCs;
        private float m_ZebraCsSpeed;
        private bool m_HasZebraRoom;
        private float m_ZebraRoomSpeed;
        private bool m_ZebraMergedSeen;
        /// <summary>0.4.1 (JR-T-009-r): this car-step a zebra section was decided early at the previous spot (cannot stop before it).</summary>
        private bool m_ZebraEarlySeen;
        /// <summary>0.4.1 (JR-T-009-r): this car-step the room scan met a further zebra (another node) inside its window.</summary>
        private bool m_ZebraFurtherSeen;
        /// <summary>0.4.1 (JR-T-009-r, probe): this car-step the room scan read a LaneObject buffer not in curve order.</summary>
        private bool m_ZebraUnsortedSeen;

        // ---- 0.4.2 (JR-T-010): cars obey yellow boxes (Core/BoxDriving.cs; methods in Stage2/JRYellowBox.cs) ----
        /// <summary>Setting "Cars obey yellow boxes" (Stage2Mode.BoxObey, stripped from m_Mode in Create).</summary>
        public bool m_BoxRule;
        private bool m_BoxTurnWait;
        private bool m_BoxLeftHand;
        private bool m_BoxJunctions;
        private bool m_BoxEntrances;
        private float m_BoxEntranceLength;
        /// <summary>SetBoxes ran (the box lookups below are valid).</summary>
        private bool m_BoxLookups;
        private ComponentLookup<JunctionRules.Boxes.YellowBoxJunction> m_BoxJunctionData;
        private ComponentLookup<Game.Net.EdgeLane> m_EdgeLaneData;
        /// <summary>
        /// 0.4.3: the boxed car exits (Stage2/JRBoxEdges.cs). A POINTER to the navigation job's single copy (its field
        /// m_JRBoxEdges, S2-26), so the ~1 KB list is not zeroed / copied with every car's iterator. Valid only inside the
        /// job's Execute (the job struct does not move while it runs); null until SetBoxes (m_BoxLookups guards every read).
        /// </summary>
        private unsafe JRBoxEdges* m_BoxEdges;
        /// <summary>The lane being checked (BeginLane, Whole junction) is a lane of a boxed junction: its decision is at the stop line.</summary>
        private bool m_BoxLane;
        /// <summary>Its turn spot (UK right-turn exception, driving coordinates; 2 = none) and that spot's index in the stretch spots (-1 = none).</summary>
        private float m_BoxTurnSpot;
        private int m_BoxTurnIndex;
        private bool m_HasBoxRoom;
        private float m_BoxRoomSpeed;
        private bool m_HasBoxTurnWait;
        private float m_BoxTurnWaitSpeed;
        /// <summary>JR-T-013b (G3): this car-step a turner was held at its turn spot because its exit had no room (S2-35).</summary>
        private bool m_HasBoxTurnRoom;
        private float m_BoxTurnRoomSpeed;
        private bool m_HasEntranceMain;
        private float m_EntranceMainSpeed;
        private bool m_HasEntranceLeaving;
        private float m_EntranceLeavingSpeed;

        // ---- 0.4.4 (JR-T-012): vehicles leaving buildings give way (Core/ExitGiveWay.cs; methods in Stage2/JRExitGiveWay.cs) ----
        /// <summary>Setting "Vehicles leaving buildings give way" (Stage2Mode.ExitGiveWay, stripped from m_Mode in Create).</summary>
        public bool m_ExitRule;
        /// <summary>SetExits ran (the exit lookups below are valid).</summary>
        private bool m_ExitLookups;
        /// <summary>The game's traffic side (navigation job m_LeftHandTraffic); labels crossing holds only.</summary>
        private bool m_ExitLeftHand;
        private ComponentLookup<Game.Buildings.Building> m_ExitBuildingData;
        private ComponentLookup<Game.Net.Edge> m_ExitEdgeData;
        /// <summary>S2-30 -> S2-31: held on its building Connection lane this update.</summary>
        private bool m_ExitWaiting;
        private bool m_HasExitHold;
        private float m_ExitHoldSpeed;
        private bool m_HasExitBox;
        private float m_ExitBoxSpeed;
        private bool m_ExitAcross;
        private bool m_ExitSpawn;
        private bool m_ExitGo;
        private bool m_ExitEmergency;
        private bool m_ExitCapHit;

        // ---- JR-T-013b (G4): watch one junction (Core/JunctionWatch.cs; methods in Stage2/JRJunctionWatch.cs) ----
        /// <summary>The watched node (S2-21 SetWatch; Entity.Null = off, nothing recorded).</summary>
        private Entity m_WatchNode;
        /// <summary>This car-step the iterator checked a lane of the watched node (BeginLane).</summary>
        private bool m_WatchSeen;

        public bool RuleOn
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (m_Mode & Stage2Mode.Rule) != 0;
        }

        public bool Counting
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (m_Mode & Stage2Mode.Count) != 0;
        }

        /// <summary>The overlap being checked gets the arrival-time projection (rule on, Deciding or Safety).</summary>
        public bool Projecting
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => RuleOn && (m_OvPhase == PointPhase.Deciding || m_OvPhase == PointPhase.Safety);
        }

        public static JRWholeJunction Create(int mode, ComponentLookup<NodeLane> nodeLaneData, ComponentLookup<Owner> ownerData,
            ComponentLookup<Lane> laneData, BufferLookup<Game.Net.SubLane> subLanes, BufferLookup<ConnectedEdge> connectedEdges,
            NativeArray<long> counters, float vehicleLength = 0f)
        {
            JRWholeJunction j = default;
            // 0.3.2: the no-push setting travels in the same handed-over int but is not part of the stage 2 mode.
            j.m_NoPushPeople = (mode & Stage2Mode.NoPushPeople) != 0;
            // 0.4.1: so does the zebra rule with its room margin.
            j.m_ZebraRule = (mode & Stage2Mode.ZebraClear) != 0;
            j.m_RoomMargin = Stage2Mode.ZebraMargin(mode);
            // 0.4.2: and the yellow box rule (the margin bits are shared).
            j.m_BoxRule = (mode & Stage2Mode.BoxObey) != 0;
            j.m_BoxTurnWait = (mode & Stage2Mode.BoxTurnWait) != 0;
            j.m_BoxLeftHand = (mode & Stage2Mode.BoxLeftHand) != 0;
            j.m_BoxJunctions = (mode & Stage2Mode.BoxJunctions) != 0;
            j.m_BoxEntrances = (mode & Stage2Mode.BoxEntrances) != 0;
            j.m_BoxEntranceLength = Stage2Mode.BoxEntranceLength(mode);
            j.m_BoxTurnSpot = 2f;
            j.m_BoxTurnIndex = -1;
            // 0.4.4: and the exit give-way rule.
            j.m_ExitRule = (mode & Stage2Mode.ExitGiveWay) != 0;
            mode &= ~Stage2Mode.Stripped;
            j.m_Mode = mode;
            j.m_VehicleLength = vehicleLength;
            if (mode != 0 || j.m_ZebraRule || j.m_BoxRule || j.m_ExitRule)
            {
                j.m_NodeLaneData = nodeLaneData;
                j.m_OwnerData = ownerData;
                j.m_LaneData = laneData;
                j.m_SubLanes = subLanes;
                j.m_ConnectedEdges = connectedEdges;
                if ((mode & Stage2Mode.Count) != 0) j.m_Counters = counters;
            }
            return j;
        }

        /// <summary>The lane's overlaps as conflict points in driving coordinates (red-held road lanes are not live).</summary>
        private struct OverlapConflicts : IConflictList
        {
            public DynamicBuffer<LaneOverlap> m_Overlaps;
            public bool m_Inverse;
            /// <summary>Bit i = overlap i is held by red (first 64 overlaps; the rest are looked up).</summary>
            public ulong m_Held;
            public ComponentLookup<LaneSignal> m_Signals;

            public int Count => m_Overlaps.Length;

            public bool Conflict(int i, out float start, out float end)
            {
                LaneOverlap o = m_Overlaps[i];
                bool held = i < 64 ? (m_Held & (1UL << i)) != 0 : HeldByRed(o, ref m_Signals);
                return StretchGeometry.ToConflict((int)o.m_Flags, o.m_ThisStart, o.m_ThisEnd, m_Inverse, held, out start, out end);
            }
        }

        /// <summary>0.3.3: the lane's overlaps with their raw flags in driving coordinates (Core/CrossingPoint.cs).</summary>
        private struct OverlapRanges : IOverlapRanges
        {
            public DynamicBuffer<LaneOverlap> m_Overlaps;
            public bool m_Inverse;

            public int Count => m_Overlaps.Length;

            public void Get(int i, out int flags, out float start, out float end)
            {
                LaneOverlap o = m_Overlaps[i];
                flags = (int)o.m_Flags;
                StretchGeometry.ToConflict(flags, o.m_ThisStart, o.m_ThisEnd, m_Inverse, false, out start, out end);
            }
        }

        /// <summary>The game's own motion functions on the car's prefab data (Core/ArrivalTime.cs ICarMotion).</summary>
        private struct GameMotion : ICarMotion
        {
            public CarData m_Car;

            public float MaxNextSpeed(float speed, float timeStep) => VehicleUtils.CalculateSpeedRange(m_Car, speed, timeStep).max;

            public float MinNextSpeed(float speed, float timeStep) => VehicleUtils.CalculateSpeedRange(m_Car, speed, timeStep).min;

            public float BrakingSpeed(float distance, float maxResultSpeed, float timeStep) =>
                VehicleUtils.GetMaxBrakingSpeed(m_Car, distance, maxResultSpeed, timeStep);
        }

        private static bool HeldByRed(LaneOverlap o, ref ComponentLookup<LaneSignal> signals)
        {
            int flags = (int)o.m_Flags;
            // Only red holds a lane; amber (SafeStop) is live (owner ruling 14 Nov 2026). Track lanes are always live.
            return (flags & WholePathRule.Road) != 0 && (flags & WholePathRule.Track) == 0
                && signals.TryGetComponent(o.m_Other, out LaneSignal s) && WholePathRule.IsHeldByRed((int)s.m_Signal);
        }

        /// <summary>
        /// Start of CheckOverlappingLanes for one lane: decide whether the rule covers this lane (a node lane with at
        /// least one conflict point), find its entry spot W, where the car is and the first uncrossed point of its path.
        /// origDistance is the game's value after its own "- margin". The car, its speed, the time step and the lane's
        /// speed limit (the value the game passes to CheckOverlappingLanes, speed-limit factor applied) feed the game's
        /// can-stop test and, 0.2.3, the arrival time: drive speed = VehicleUtils.GetMaxDriveSpeed(car, lane) with that
        /// limit, reduced by lane wear (ModifyDriveSpeed) exactly as IterateFirstLane / IterateNextLane do.
        /// </summary>
        public void BeginLane(Entity lane, DynamicBuffer<LaneOverlap> overlaps, ref ComponentLookup<LaneSignal> signals,
            ref ComponentLookup<Game.Net.CarLane> carLanes, ref ComponentLookup<LaneCondition> laneConditions,
            float curveX, float length, float origDistance, float margin, bool inverse, CarData car, float currentSpeed,
            float timeStep, float speedLimit)
        {
            m_LaneActive = false;
            m_Committed = false;
            m_Inside = false;
            m_BoxLane = false;
            m_BoxTurnSpot = 2f;
            m_BoxTurnIndex = -1;
            if (!m_NodeLaneData.HasComponent(lane)) return;
            // JR-T-013b (G4): the car checks a lane of the watched junction (debug log only; no driving effect).
            if (m_WatchNode != Entity.Null && !m_WatchSeen && m_OwnerData.TryGetComponent(lane, out Owner watchOwner) && watchOwner.m_Owner == m_WatchNode) m_WatchSeen = true;
            OverlapConflicts list = default;
            list.m_Overlaps = overlaps;
            list.m_Inverse = inverse;
            list.m_Signals = signals;
            int n = overlaps.Length < 64 ? overlaps.Length : 64;
            for (int i = 0; i < n; i++)
            {
                if (HeldByRed(overlaps[i], ref signals)) list.m_Held |= 1UL << i;
            }
            float braking = VehicleUtils.GetBrakingDistance(car, currentSpeed, timeStep);
            // 0.4.2: a lane of a boxed junction takes its decision at the stop line (Core/BoxDriving.cs).
            bool box = RuleOn && IsBoxedJunctionLane(lane);
            if (!m_Stretch.Begin(ref list, origDistance, length, StretchGeometry.ToDriving(curveX, inverse), margin, braking,
                    (m_Mode & Stage2Mode.AtLine) != 0 || box))
            {
                return;
            }
            if (box)
            {
                // UK right-turn exception: a second spot inside the box before the first oncoming conflict.
                m_BoxLane = true;
                m_BoxTurnSpot = TurnSpot(lane, overlaps, inverse, ref carLanes);
                if (m_BoxTurnSpot < 1f && BoxDriving.JunctionSpots(true, m_BoxTurnSpot, out WaitingSpots boxSpots) && boxSpots.Count > 1)
                {
                    m_Stretch.BeginWithSpots(ref list, boxSpots, origDistance, length, StretchGeometry.ToDriving(curveX, inverse), margin, braking);
                    m_BoxTurnIndex = 1;
                }
            }
            m_Inverse = inverse;
            m_Spot = StretchGeometry.ToDriving(m_Stretch.Spots[0], inverse);
            m_Committed = m_Stretch.PastEntry;
            m_Inside = m_FirstLane && m_Committed;
            m_LaneActive = true;
            m_LaneEntity = lane;
            m_LaneOverlaps = overlaps;
            m_OwnStartRead = false;
            m_OwnTurnRead = false;
            if (RuleOn) SetMotion(lane, ref carLanes, ref laneConditions, margin, car, currentSpeed, timeStep, speedLimit);
        }

        /// <summary>0.2.3 motion for the arrival time (BeginLane; 0.4.1 also BeginZebras), after m_Stretch is set.</summary>
        private void SetMotion(Entity lane, ref ComponentLookup<Game.Net.CarLane> carLanes, ref ComponentLookup<LaneCondition> laneConditions,
            float margin, CarData car, float currentSpeed, float timeStep, float speedLimit)
        {
            {
                if (m_Stretch.Next < m_Stretch.Spots.Count) m_Decision = true;
                m_Car = car;
                m_Speed = currentSpeed;
                m_TimeStep = timeStep;
                float drive = speedLimit;
                if (carLanes.TryGetComponent(lane, out Game.Net.CarLane carLane))
                {
                    carLane.m_SpeedLimit = speedLimit;
                    drive = VehicleUtils.GetMaxDriveSpeed(car, carLane);
                    if (laneConditions.TryGetComponent(lane, out LaneCondition condition)) VehicleUtils.ModifyDriveSpeed(ref drive, condition);
                }
                m_DriveSpeed = drive;
                float toStart = m_Stretch.Wait(0f) + margin;
                m_DriveFrom = toStart > 0f ? toStart : 0f;
                m_DeltaU = -1f;
                m_AtSpot = Counting && !m_Stretch.PastEntry && currentSpeed < Stage2Counters.StandingSpeed
                    && m_Stretch.Wait(m_Stretch.Spots[0]) <= margin;
            }
        }

        /// <summary>
        /// The overlap starting at <paramref name="start"/> (game lane offset) is part of the rule (yield-checked, at or
        /// after the entry spot). Also classifies it for the checks that follow and, 0.2.3, computes the car's own
        /// arrival time at it when it is being decided or re-checked. 0.3.3 (JR-T-006): <paramref name="isCrossing"/> =
        /// the other lane is a pedestrian lane (a crossing); with the rule on and the whole vehicle fitting before it
        /// (Core/CrossingPoint.cs) it is out of the rule: the game's own pedestrian / cyclist check at the crossing.
        /// </summary>
        public bool InScope(float start, bool yieldChecked, bool isCrossing = false)
        {
            m_OvDelta = 0f;
            m_OvDiscountReservation = false;
            m_OvZebra = false;
            if ((m_Mode == 0 && !m_ZebraActive) || !m_LaneActive || !yieldChecked || !WholePathRule.AtOrAfterSpot(start, m_Spot, m_Inverse))
            {
                m_OvPhase = PointPhase.Stock;
                return false;
            }
            float u = StretchGeometry.ToDriving(start, m_Inverse);
            // 0.4.1: a zebra of the zebra sections is decided at its section's spot (never judged at the crossing).
            m_OvZebra = isCrossing && m_ZebraActive && IsZebraStart(u);
            if (m_OvZebra && !RuleOn)
            {
                // Stock gap rule: only the zebras are ours, and only before their section (the hold at the section's spot).
                m_OvStart = u;
                m_OvPhase = m_Stretch.Classify(true, u, out m_OvStopWait);
                if (m_OvPhase != PointPhase.Deciding) m_OvPhase = PointPhase.Stock;
                return true;
            }
            if (isCrossing && RuleOn && !m_OvZebra)
            {
                OverlapRanges ranges = default;
                ranges.m_Overlaps = m_LaneOverlaps;
                ranges.m_Inverse = m_Inverse;
                float previous = CrossingPoint.PreviousEnd(ref ranges, u, m_Inverse);
                bool fits = CrossingPoint.Fits(m_Stretch.Length, u, previous, m_Stretch.Margin, m_VehicleLength);
                if (CrossingPoint.JudgedAtCrossing(true, true, fits))
                {
                    if (Counting) m_CrossingAtCrossing = true;
                    m_OvPhase = PointPhase.Stock;
                    return false;
                }
                if (Counting) m_CrossingNoFit = true;
            }
            m_OvStart = u;
            m_OvPhase = m_Stretch.Classify(RuleOn, u, out m_OvStopWait);
            if (Projecting)
            {
                m_OvDelta = Delta(u);
                if (m_AtSpot && m_OvPhase == PointPhase.Deciding) Sample(u, m_OvDelta);
            }
            return true;
        }

        private readonly bool IsZebraStart(float u)
        {
            for (int i = 0; i < m_ZebraStarts.Count; i++)
            {
                float d = m_ZebraStarts[i] - u;
                if (d < WholePathRule.SpotEpsilon && d > -WholePathRule.SpotEpsilon) return true;
            }
            return false;
        }

        /// <summary>0.4.1 (S2-26): the car's navigation lanes for the room-past scan.</summary>
        public void SetPath(DynamicBuffer<CarNavigationLane> path)
        {
            m_Path = path;
            m_HasPath = true;
        }

        /// <summary>0.4.1: the lane's overlaps as zebra-section elements (Core/ZebraSections.cs), driving coordinates.</summary>
        private struct ZebraElements : IPathElements
        {
            public DynamicBuffer<LaneOverlap> m_Overlaps;
            public bool m_Inverse;
            public ComponentLookup<LaneSignal> m_Signals;
            public ComponentLookup<Game.Net.PedestrianLane> m_Pedestrian;

            public int Count => m_Overlaps.Length;

            public void Get(int i, out int kind, out float start, out float end)
            {
                LaneOverlap o = m_Overlaps[i];
                int flags = (int)o.m_Flags;
                kind = 0;
                if (m_Pedestrian.TryGetComponent(o.m_Other, out Game.Net.PedestrianLane ped))
                {
                    StretchGeometry.ToConflict(flags, o.m_ThisStart, o.m_ThisEnd, m_Inverse, false, out start, out end);
                    if ((flags & WholePathRule.Water) == 0 && ZebraSections.IsZebra((int)ped.m_Flags)) kind = ZebraSections.Zebra;
                    return;
                }
                if (StretchGeometry.ToConflict(flags, o.m_ThisStart, o.m_ThisEnd, m_Inverse, HeldByRed(o, ref m_Signals), out start, out end)) kind |= ZebraSections.SpotPoint;
                if (CrossingPoint.IsClearancePoint(flags, m_Inverse)) kind |= ZebraSections.Clearance;
            }
        }

        /// <summary>
        /// 0.4.1 (JR-T-009, block S2-25, only with m_ZebraRule; right after BeginLane): when the node lane (a junction or
        /// a mid-block crossing node) has zebras, its waiting spots become the zebra sections (Core/ZebraSections.cs):
        /// every later overlap is classified against them, so people on a zebra (and, Whole junction, vehicle points up to
        /// the next zebra) hold the car at its section's spot through the existing StopAtSpot paths. Returns the speed of
        /// the room-past hold (Core RoomScan) for the next section, or float.MaxValue. Lanes without a zebra keep
        /// BeginLane's state (the 0.4.0 behaviour).
        /// </summary>
        public float BeginZebras(Entity lane, DynamicBuffer<LaneOverlap> overlaps, ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes,
            ref ComponentLookup<LaneSignal> signals, ref ComponentLookup<Game.Net.CarLane> carLanes, ref ComponentLookup<LaneCondition> laneConditions,
            float curveX, float length, float origDistance, float margin, bool inverse, CarData car, float currentSpeed, float timeStep,
            float speedLimit, float safeTimeStep, Bounds1 speedRange, float currentMax, Entity self, ref BufferLookup<LaneObject> laneObjects,
            ref ComponentLookup<Curve> curves, ref ComponentLookup<Moving> moving, ref ComponentLookup<Controller> controllers,
            ref ComponentLookup<PrefabRef> prefabRefs, ref ComponentLookup<ObjectGeometryData> geometries, ref ComponentLookup<CarData> prefabCars,
            ref BufferLookup<LaneOverlap> overlapData)
        {
            m_ZebraActive = false;
            if (m_Mode == 0)
            {
                m_LaneActive = false;
                m_Committed = false;
                m_Inside = false;
            }
            if (!m_NodeLaneData.HasComponent(lane)) return float.MaxValue;
            ZebraElements elements = default;
            elements.m_Overlaps = overlaps;
            elements.m_Inverse = inverse;
            elements.m_Signals = signals;
            elements.m_Pedestrian = pedestrianLanes;
            bool atLine = RuleOn && ((m_Mode & Stage2Mode.AtLine) != 0 || m_BoxLane);
            // 0.4.2: a boxed junction lane: stop-line decision and the turner's spot in the box, where the zebra rule allows it.
            // Z O gaps (a waiting spot right after a zebra, owner refinement) everywhere but inside a box (the box decides at the line).
            if (!ZebraSections.Build(ref elements, RuleOn, atLine, length, margin, m_VehicleLength, m_BoxLane ? m_BoxTurnSpot : 2f, m_BoxLane ? -1f : m_RoomMargin,
                    out ZebraLayout layout)) return float.MaxValue;
            if (m_BoxLane) m_BoxTurnIndex = layout.ExtraIndex;
            m_ZebraStarts = default;
            for (int i = 0; i < overlaps.Length; i++)
            {
                elements.Get(i, out int kind, out float s, out _);
                if ((kind & ZebraSections.Zebra) != 0) m_ZebraStarts.Add(s);
            }
            OverlapConflicts list = default;
            list.m_Overlaps = overlaps;
            list.m_Inverse = inverse;
            list.m_Signals = signals;
            int n = overlaps.Length < 64 ? overlaps.Length : 64;
            for (int i = 0; i < n; i++)
            {
                if (HeldByRed(overlaps[i], ref signals)) list.m_Held |= 1UL << i;
            }
            float braking = VehicleUtils.GetBrakingDistance(car, currentSpeed, timeStep);
            m_Stretch.BeginWithSpots(ref list, layout.Spots, origDistance, length, StretchGeometry.ToDriving(curveX, inverse), margin, braking);
            // JR-T-009-r (finding 1): a zebra section the car could not stop for after its spot is decided at that spot.
            if (m_Stretch.Next < m_Stretch.Spots.Count && m_Stretch.CanStopBefore(m_Stretch.Spots[m_Stretch.Next])
                && ZebraSections.MergeUnstoppable(ref layout, m_Stretch.Next, braking, length) > 0)
            {
                m_Stretch.Spots = layout.Spots;
                if (Counting) m_ZebraEarlySeen = true;
            }
            m_ZebraLayout = layout;
            m_ZebraActive = true;
            m_Inverse = inverse;
            m_Spot = StretchGeometry.ToDriving(m_Stretch.Spots[0], inverse);
            m_Committed = m_Stretch.PastEntry;
            m_Inside = m_FirstLane && m_Committed;
            m_LaneActive = true;
            m_LaneEntity = lane;
            m_LaneOverlaps = overlaps;
            m_OwnStartRead = false;
            m_OwnTurnRead = false;
            if (RuleOn) SetMotion(lane, ref carLanes, ref laneConditions, margin, car, currentSpeed, timeStep, speedLimit);
            if (Counting && layout.Merged > 0 && m_Stretch.Next < m_Stretch.Spots.Count) m_ZebraMergedSeen = true;
            int k = ZebraSections.RoomSection(layout, m_Stretch);
            if (k < 0) return float.MaxValue;
            float hold = MathUtils.Clamp(VehicleUtils.GetMaxBrakingSpeed(car, m_Stretch.Wait(m_Stretch.Spots[k]), safeTimeStep), speedRange);
            if (!(hold < currentMax)) return float.MaxValue;
            if (HasRoomPast(lane, layout.SectionEnd(k), inverse, length, -1f, Entity.Null, self, safeTimeStep, ref carLanes, ref laneObjects, ref curves,
                    ref moving, ref controllers, ref prefabRefs, ref geometries, ref prefabCars, ref overlapData, ref pedestrianLanes))
            {
                return float.MaxValue;
            }
            if (Counting) Min(ref m_HasZebraRoom, ref m_ZebraRoomSpeed, hold);
            return hold;
        }

        /// <summary>
        /// 0.4.1 room past (Core RoomScan), shared since 0.4.2 by the zebra rule and the yellow boxes (one mechanism): is there
        /// room for the whole vehicle plus the margin on the car's own path beyond <paramref name="fromDriving"/> (driving
        /// coordinates of <paramref name="lane"/>)? Vehicles ahead on the rest of this lane and on up to MaxRoomLanes of the
        /// car's next navigation lanes are fed in path order (moving: their braking distance counts as room). 0.4.2:
        /// <paramref name="queueFrom"/> &gt;= 0 = the vehicles on this lane between queueFrom and fromDriving are inside the
        /// box / band ahead of the car and take exit room first (RoomScan.AddQueued); path lanes owned by
        /// <paramref name="boxNode"/> (further lanes of the same boxed junction) likewise. The zebra rule passes -1 /
        /// Entity.Null: exactly the 0.4.1 scan.
        /// </summary>
        private bool HasRoomPast(Entity lane, float fromDriving, bool inverse, float length, float queueFrom, Entity boxNode, Entity self, float safeTimeStep,
            ref ComponentLookup<Game.Net.CarLane> carLanes, ref BufferLookup<LaneObject> laneObjects, ref ComponentLookup<Curve> curves,
            ref ComponentLookup<Moving> moving, ref ComponentLookup<Controller> controllers, ref ComponentLookup<PrefabRef> prefabRefs,
            ref ComponentLookup<ObjectGeometryData> geometries, ref ComponentLookup<CarData> prefabCars,
            ref BufferLookup<LaneOverlap> overlapData, ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes)
        {
            RoomScan scan = RoomScan.Begin(m_VehicleLength, m_RoomMargin);
            bool unsorted = false;
            if (queueFrom >= 0f && fromDriving > queueFrom)
            {
                ScanLane(ref scan, lane, StretchGeometry.ToDriving(queueFrom, inverse), inverse, length, (fromDriving - queueFrom) * length, 0f, true, self, safeTimeStep,
                    ref laneObjects, ref curves, ref moving, ref controllers, ref prefabRefs, ref geometries, ref prefabCars, ref unsorted);
            }
            float read = (1f - fromDriving) * length;
            ScanLane(ref scan, lane, StretchGeometry.ToDriving(fromDriving, inverse), inverse, length, read, 0f, false, self, safeTimeStep, ref laneObjects, ref curves,
                ref moving, ref controllers, ref prefabRefs, ref geometries, ref prefabCars, ref unsorted);
            if (!scan.Done(read) && m_HasPath)
            {
                int start = -1;
                for (int i = 0; i < m_Path.Length; i++)
                {
                    if (m_Path[i].m_Lane == lane) { start = i + 1; break; }
                }
                if (start < 0 && (lane == m_CurrentLane || lane == m_CurrentLane2)) start = 0;
                for (int i = start < 0 ? m_Path.Length : start, cap = 0; i < m_Path.Length && cap < MaxRoomLanes && !scan.Done(read); i++, cap++)
                {
                    CarNavigationLane nav = m_Path[i];
                    if (!carLanes.HasComponent(nav.m_Lane) || !curves.TryGetComponent(nav.m_Lane, out Curve navCurve)) break;
                    bool navInverse = nav.m_CurvePosition.y < nav.m_CurvePosition.x;
                    float span = math.abs(nav.m_CurvePosition.y - nav.m_CurvePosition.x) * navCurve.m_Length;
                    // 0.4.2: a later lane of the same boxed junction is still inside the box: its vehicles queue for the exit.
                    bool inBox = boxNode != Entity.Null && m_OwnerData.TryGetComponent(nav.m_Lane, out Owner navOwner) && navOwner.m_Owner == boxNode;
                    if (!inBox && FurtherZebras(ref scan, nav.m_Lane, nav.m_CurvePosition, navInverse, navCurve.m_Length, read, ref overlapData, ref pedestrianLanes, ref laneObjects)
                        && Counting) m_ZebraFurtherSeen = true;
                    ScanLane(ref scan, nav.m_Lane, nav.m_CurvePosition.x, navInverse, navCurve.m_Length, span, inBox ? 0f : read, inBox, self, safeTimeStep, ref laneObjects,
                        ref curves, ref moving, ref controllers, ref prefabRefs, ref geometries, ref prefabCars, ref unsorted);
                    if (!inBox) read += span;
                }
            }
            if (unsorted && Counting) m_ZebraUnsortedSeen = true;
            return scan.HasRoom;
        }

        /// <summary>Most path lanes past the junction lane read for the room-past scan (the unknown rest counts as free).</summary>
        public const int MaxRoomLanes = 3;

        /// <summary>
        /// 0.4.1 (JR-T-009-r, review finding 2): zebras of a later node lane of the path (another node, e.g. a mid-block
        /// crossing after an edge shorter than the vehicle) that start inside the room window (the room needed so far,
        /// metres from the section end). The car must not stop on them either: people on one = no room
        /// (<see cref="RoomScan.Block"/>); else the room is needed past its end (<see cref="RoomScan.ExtendPast"/>, which
        /// can bring a further zebra of the lane into the window: re-read until nothing changes, at most a few passes).
        /// People = any object on the crosswalk lane (the game puts walkers and cyclists on the zebra there); people still
        /// on the pavement are not seen (PROBE section 10). Returns true when a zebra was inside the window.
        /// </summary>
        private bool FurtherZebras(ref RoomScan scan, Entity navLane, float2 curvePosition, bool navInverse, float navLength, float baseDistance,
            ref BufferLookup<LaneOverlap> overlapData, ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes, ref BufferLookup<LaneObject> laneObjects)
        {
            if (!m_NodeLaneData.HasComponent(navLane) || !overlapData.TryGetBuffer(navLane, out DynamicBuffer<LaneOverlap> overlaps)) return false;
            float startD = StretchGeometry.ToDriving(curvePosition.x, navInverse), endD = StretchGeometry.ToDriving(curvePosition.y, navInverse);
            bool hit = false;
            for (int pass = 0, extensions = -1; pass < 3 && extensions != scan.Extensions && !scan.Blocked; pass++)
            {
                extensions = scan.Extensions;
                for (int i = 0; i < overlaps.Length && !scan.Blocked; i++)
                {
                    LaneOverlap o = overlaps[i];
                    int flags = (int)o.m_Flags;
                    if ((flags & WholePathRule.Water) != 0 || !pedestrianLanes.TryGetComponent(o.m_Other, out Game.Net.PedestrianLane ped)
                        || !ZebraSections.IsZebra((int)ped.m_Flags)) continue;
                    StretchGeometry.ToConflict(flags, o.m_ThisStart, o.m_ThisEnd, navInverse, false, out float zs, out float ze);
                    if (ze <= startD || zs >= endD) continue;
                    bool people = laneObjects.TryGetBuffer(o.m_Other, out DynamicBuffer<LaneObject> walkers) && walkers.Length > 0;
                    if (scan.FurtherZebra(baseDistance + (zs - startD) * navLength, baseDistance + (ze - startD) * navLength, people)) hit = true;
                }
            }
            return hit;
        }

        /// <summary>
        /// 0.4.2: the order keys of a LaneObject buffer for the room scan (Core ScanOrder): the object's current curve
        /// position ahead of <see cref="m_From"/> in driving direction (lane offsets); oncoming objects are skipped.
        /// </summary>
        private struct LaneObjectKeys : IOrderKeys
        {
            public DynamicBuffer<LaneObject> m_Objects;
            public float m_From;
            public bool m_Inverse;
            public int Count => m_Objects.Length;

            public float Key(int i)
            {
                float2 cp = m_Objects[i].m_CurvePosition;
                if (cp.y != cp.x && (cp.y < cp.x) != m_Inverse) return float.MaxValue;
                return m_Inverse ? m_From - cp.x : cp.x - m_From;
            }
        }

        /// <summary>Longest rear extent behind a lane object's reference point the scan allows for (an articulated vehicle).</summary>
        private const float MaxRearReach = 30f;

        /// <summary>
        /// 0.4.1: feeds the vehicles on <paramref name="lane"/> ahead of <paramref name="from"/> (game offset; driving
        /// direction <paramref name="inverse"/>) up to <paramref name="span"/> metres into the scan, nearest first, as the
        /// stock CheckSpace measures them: rear from the object's reference point minus its rear extent (here along the
        /// lane: curve offset times lane length), credit = its own braking distance (its prefab CarData, safe time step)
        /// when it moves forward. Oncoming objects, the car itself and its own trailers are skipped.
        /// 0.4.2: nearest first by the objects' CURRENT curve position (Core ScanOrder), not by buffer order. The game keeps
        /// a LaneObject buffer sorted by m_CurvePosition.y (Game.Net NetUtils.AddLaneObject / UpdateLaneObject, 435-487,
        /// insert by .y; Game.Simulation CarNavigationHelpers CurrentLaneCache 185/198/228 write
        /// CarCurrentLane.m_CurvePosition.xy). For a driving car .y is its reservation look-ahead, set in
        /// CarNavigationSystem.ReserveNavigationLanes (~2263-2269): current position x + its braking distance at its max
        /// speed, capped at the lane target z. So the buffer is in look-ahead order, not position order: a faster car
        /// close behind a slower one (longer braking distance) sorts after it, and a car whose target lies short of the
        /// lane end sorts early (the 0.4.1 probe counter "lane objects out of curve order" &gt; 0). The stock loops read the
        /// whole buffer without relying on its order (CarLaneSpeedIterator.CheckCurrentLane 1059); RoomScan relies on
        /// path order. JR-T-010-r: a buffer already in reading order is read straight; objects more than MaxRearReach
        /// behind <paramref name="from"/> are never selected.
        /// </summary>
        private static void ScanLane(ref RoomScan scan, Entity lane, float from, bool inverse, float laneLength, float span, float baseDistance,
            bool queued, Entity self, float safeTimeStep, ref BufferLookup<LaneObject> laneObjects, ref ComponentLookup<Curve> curves, ref ComponentLookup<Moving> moving,
            ref ComponentLookup<Controller> controllers, ref ComponentLookup<PrefabRef> prefabRefs, ref ComponentLookup<ObjectGeometryData> geometries,
            ref ComponentLookup<CarData> prefabCars, ref bool unsorted)
        {
            if (!laneObjects.TryGetBuffer(lane, out DynamicBuffer<LaneObject> objects) || objects.Length == 0) return;
            bool hasCurve = curves.TryGetComponent(lane, out Curve curve);
            LaneObjectKeys keys = default;
            keys.m_Objects = objects;
            keys.m_From = from;
            keys.m_Inverse = inverse;
            // JR-T-010-r: a buffer already in reading order is read straight (no selection); else nearest first by selection.
            bool ordered = ScanOrder.InOrder(ref keys, inverse);
            if (!ordered) unsorted = true;
            // Objects further back than MaxRearReach behind `from` cannot reach past it: start the order there.
            float lastKey = -MaxRearReach / math.max(laneLength, 0.001f);
            int lastIndex = -1;
            for (int n = 0; n < objects.Length && !scan.Blocked; n++)
            {
                int jj;
                float key;
                if (ordered)
                {
                    jj = inverse ? objects.Length - 1 - n : n;
                    key = keys.Key(jj);
                    if (!(key < float.MaxValue) || key < lastKey) continue;
                }
                else
                {
                    jj = ScanOrder.Next(ref keys, lastKey, lastIndex);
                    if (jj < 0) break;
                    key = keys.Key(jj);
                    lastKey = key;
                    lastIndex = jj;
                }
                float d = key * laneLength;
                if (d - MaxRearReach > span) break;
                LaneObject o = objects[jj];
                Entity e = o.m_LaneObject;
                if (e == self) continue;
                if (controllers.TryGetComponent(e, out Controller c) && c.m_Controller == self) continue;
                if (!prefabRefs.TryGetComponent(e, out PrefabRef pr) || !geometries.TryGetComponent(pr.m_Prefab, out ObjectGeometryData g)) continue;
                if (d + g.m_Bounds.max.z < 0f || d + g.m_Bounds.min.z > span) continue;
                // 0.4.2: inside the box / band ahead of the car: takes exit room first (RoomScan.AddQueued).
                if (queued)
                {
                    scan.AddQueued(MathUtils.Size(g.m_Bounds.z));
                    continue;
                }
                float credit = 0f;
                if (hasCurve && moving.TryGetComponent(e, out Moving m) && prefabCars.TryGetComponent(pr.m_Prefab, out CarData cd))
                {
                    float3 dir = math.normalizesafe(MathUtils.Tangent(curve.m_Bezier, o.m_CurvePosition.x));
                    float v = math.dot(m.m_Velocity, inverse ? -dir : dir);
                    if (v > 0.001f) credit = VehicleUtils.GetBrakingDistance(cd, v, safeTimeStep);
                }
                scan.Add(baseDistance + d + g.m_Bounds.min.z, MathUtils.Size(g.m_Bounds.z), credit);
            }
        }

        /// <summary>
        /// 0.4.1 (block S2-27, counters only): after the game's pedestrian check on an overlap in scope; the car's limit
        /// went from <paramref name="before"/> to <paramref name="after"/>. A zebra decided at its section's spot counts as
        /// "zebra entry held: people".
        /// </summary>
        public void AfterPedestrian(float before, float after)
        {
            if (!Counting || !m_OvZebra || m_OvPhase != PointPhase.Deciding || !(after < before)) return;
            Min(ref m_HasZebraPeople, ref m_ZebraPeopleSpeed, after);
        }

        /// <summary>The car's own arrival time at the point u (driving coordinates), cached for repeated starts.</summary>
        private float Delta(float u)
        {
            if (u == m_DeltaU) return m_DeltaValue;
            GameMotion motion = default;
            motion.m_Car = m_Car;
            float distance = m_Stretch.Wait(u) + m_Stretch.Margin;
            m_DeltaValue = ArrivalTime.Compute(ref motion, distance, m_Speed, m_DriveSpeed, m_DriveFrom, m_TimeStep);
            m_DeltaU = u;
            return m_DeltaValue;
        }

        /// <summary>
        /// A stock give-way demand from a blocking reservation (a prediction): stockSpeed is the game's
        /// GetMaxBrakingSpeed(car, distance to this conflict, safe time step). Returns the speed the game then uses.
        /// </summary>
        public float Yield(bool inScope, float stockSpeed, float start, CarData car, float safeTimeStep, Bounds1 speedRange)
        {
            if (!inScope) return stockSpeed;
            // 0.2.4: a turner across oncoming that gives way to this car does not hold it by its reservation alone.
            if (m_OvDiscountReservation)
            {
                if (Counting) m_TurnDiscounts++;
                return float.MaxValue;
            }
            return Apply(StretchState.ForPrediction(m_OvPhase), stockSpeed, car, safeTimeStep, speedRange);
        }

        /// <summary>A car asking for space (reservation priority 96): held at the spot before it, dropped while crossing.</summary>
        public float SpaceYield(bool inScope, float stockSpeed, float start, CarData car, float safeTimeStep, Bounds1 speedRange)
        {
            if (!inScope) return stockSpeed;
            return Apply(StretchState.ForSpaceRequest(m_OvPhase), stockSpeed, car, safeTimeStep, speedRange);
        }

        private float Apply(WholePathRule.YieldAction a, float stockSpeed, CarData car, float safeTimeStep, Bounds1 speedRange)
        {
            float result = a == WholePathRule.YieldAction.StopAtSpot ? VehicleUtils.GetMaxBrakingSpeed(car, m_OvStopWait, safeTimeStep)
                : a == WholePathRule.YieldAction.Suppress ? float.MaxValue : stockSpeed;
            if (Counting && a != WholePathRule.YieldAction.Suppress)
            {
                Record(MathUtils.Clamp(result, speedRange), a, false);
            }
            return result;
        }

        /// <summary>Distance for the game's pedestrian check on a crossing of the path.</summary>
        public float PedestrianDistance(bool inScope, float stockDistance)
        {
            if (!inScope) return stockDistance;
            return WholePathRule.Distance(StretchState.ForPedestrian(m_OvPhase), stockDistance, m_OvStopWait);
        }

        /// <summary>
        /// 0.3.2 (JR-T-004, block S2-08b; only called with m_NoPushPeople): the game's CheckPedestrian has just run for
        /// person <paramref name="obj"/> against an open limit (S2-08 saved the iterator's limit, blocker and blocker type
        /// and set the limit to float.MaxValue), so maxSpeed &lt; float.MaxValue exactly when the game's own gate counted
        /// this person, and then maxSpeed is the game's own speed for them (creep or stop). With a push reason
        /// (Core/CrossingPush.cs: the car's 5 s PushBlockers timer, or a standing person) the car brakes to a full stop
        /// instead, the game's walking-person value <c>GetMaxBrakingSpeed(car, distance, safeTimeStep)</c> clamped to the
        /// speed range. Then the saved state is restored and the result applied exactly as CheckPedestrian applies its own
        /// (lower than the limit: limit, blocker = the person, type Temporary). Without a push reason, or when the gate did
        /// not count the person, the outcome equals the game's. distance = the value the game's check got (S2-08's
        /// waiting-spot distance before commit, else the game's), so the stop lands where stage 2 put it.
        /// </summary>
        public void NoPushPedestrian(ref float maxSpeed, ref Entity blocker, ref BlockerType blockerType, float savedMax, Entity savedBlocker,
            BlockerType savedType, Entity obj, float distance, bool pushBlockers, ref ComponentLookup<Moving> movingData, CarData car,
            float safeTimeStep, Bounds1 speedRange)
        {
            float result = float.MaxValue;
            if (maxSpeed < float.MaxValue)
            {
                float stock = maxSpeed;
                bool hasMoving = movingData.TryGetComponent(obj, out Moving moving);
                CrossingPush.PushReason reason = CrossingPush.Pedestrian(pushBlockers, hasMoving, hasMoving ? math.lengthsq(moving.m_Velocity) : 0f);
                float fullStop = reason == CrossingPush.PushReason.None ? stock
                    : MathUtils.Clamp(VehicleUtils.GetMaxBrakingSpeed(car, distance, safeTimeStep), speedRange);
                result = CrossingPush.PedestrianSpeed(true, reason, stock, fullStop);
                if (Counting && result < stock && result < savedMax)
                {
                    if (reason == CrossingPush.PushReason.Timer) m_RefusedTimer = true;
                    else m_RefusedStanding = true;
                }
            }
            maxSpeed = savedMax;
            blocker = savedBlocker;
            blockerType = savedType;
            if (result < maxSpeed)
            {
                maxSpeed = result;
                blocker = obj;
                blockerType = BlockerType.Temporary;
            }
        }

        /// <summary>
        /// 0.3.2 (JR-T-004, block S2-11; only called with m_NoPushPeople): the m_Ignore entity for the game's
        /// UpdateMaxSpeed call on object <paramref name="obj"/> of the overlapping lane <paramref name="lane"/>: Entity.Null
        /// when obj is the car's ignored blocker (IgnoreBlocker creep) and a bicycle on a pedestrian lane (a cyclist on a
        /// crossing), else unchanged. S2-11b restores the field after the call.
        /// </summary>
        public Entity CrossingIgnore(Entity ignore, Entity obj, Entity lane, ref ComponentLookup<Game.Vehicles.Bicycle> bicycles,
            ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes)
        {
            if (ignore == Entity.Null || obj != ignore) return ignore;
            if (!CrossingPush.RefuseIgnore(true, true, bicycles.HasComponent(obj), pedestrianLanes.HasComponent(lane))) return ignore;
            if (Counting) m_RefusedCyclist = true;
            return Entity.Null;
        }

        /// <summary>
        /// After the game's priority look-ahead: returns the curve position the game goes on with. Crossing: undone
        /// (0.2.0 committed semantics). 0.2.3, Deciding / Safety: the stock y + v * 2 / len becomes y + v * (2 + arrival
        /// time) / len (only where stock applied its own). threshold = the stock skip limit (otherStart + num3, or
        /// otherEnd - num3 when the other car drives its lane inverted, flag2), for the diagnostics only.
        /// </summary>
        public float AfterProjection(bool inScope, float originalY, float projectedY, bool objectPastStart, bool flag2, float threshold)
        {
            if (!inScope || projectedY == originalY) return projectedY;
            if (StretchState.UndoProjection(m_OvPhase, objectPastStart)) return originalY;
            if (!Projecting) return projectedY;
            float extended = ArrivalProjection.ExtendLookAhead(originalY, projectedY, m_OvDelta);
            if (Counting)
            {
                bool stockCounts = flag2 ? projectedY < threshold : projectedY > threshold;
                bool extendedCounts = flag2 ? extended < threshold : extended > threshold;
                if (!stockCounts && extendedCounts) m_ProjectedLaneHit = true;
            }
            return extended;
        }

        /// <summary>
        /// 0.2.4 (JR-STAGE2 block S2-13, owner ruling 14 Nov 2026, setting "Turning across oncoming traffic gives way"):
        /// called right after the stock <c>num6 -= num9</c> for a yield-checked overlap with junction lane
        /// <paramref name="other"/>; returns num6 with the rule's priority delta in place of the stock one when the pair
        /// is a turn across the oncoming flow and that oncoming straight with equal signs (Core/TurnAcross.cs), else
        /// num6 unchanged. num8 = the stock sign / signal override (any override keeps stock), num9 = the delta stock just
        /// subtracted. For the straight car it also marks the overlap so the turner's reservation alone does not hold it
        /// (S2-06, Yield). Every later use of num6 for this overlap (stock look-ahead and reservation tests, S2-10, S2-12
        /// feeding cars, the safety re-check) sees the result. Read-only lookups; nothing shared is written.
        /// </summary>
        public int TurnAcrossPriority(bool inScope, Entity other, int num6, int num8, int num9, bool requestSpace,
            ref ComponentLookup<Game.Net.CarLane> carLanes, ref ComponentLookup<LaneReservation> reservations)
        {
            if (!inScope || !RuleOn || (m_Mode & Stage2Mode.TurnGivesWay) == 0 || num8 != 0) return num6;
            if (!m_OwnTurnRead)
            {
                m_OwnTurnRead = true;
                m_OwnFlags = 0u;
                if (carLanes.TryGetComponent(m_LaneEntity, out Game.Net.CarLane ownCarLane) && m_LaneData.TryGetComponent(m_LaneEntity, out Lane ownLane))
                {
                    m_OwnFlags = (uint)ownCarLane.m_Flags;
                    m_OwnTurnStart = ownLane.m_StartNode;
                    m_OwnTurnEnd = ownLane.m_EndNode;
                }
            }
            if (m_OwnFlags == 0u) return num6;
            if (!carLanes.TryGetComponent(other, out Game.Net.CarLane otherCarLane) || !m_LaneData.TryGetComponent(other, out Lane otherLane)) return num6;
            uint otherFlags = (uint)otherCarLane.m_Flags;
            bool lht = (m_Mode & Stage2Mode.LeftHandTraffic) != 0;
            int delta = TurnAcross.Delta(m_OwnFlags, otherFlags, m_OwnTurnStart.OwnerEquals(otherLane.m_EndNode),
                otherLane.m_StartNode.OwnerEquals(m_OwnTurnEnd), num8, lht);
            if (delta == 0) return num6;
            int result = TurnAcross.ApplyNum6(num6, num9, delta, requestSpace);
            if (Counting && result != num6) m_TurnOverrides++;
            int reservationPriority = reservations.TryGetComponent(other, out LaneReservation reservation) ? reservation.GetPriority() : 0;
            m_OvDiscountReservation = TurnAcross.DiscountReservation(delta, reservationPriority, result);
            return result;
        }

        /// <summary>
        /// 0.2.3 (owner ruling 14 Nov 2026): cars on the lanes feeding the conflicting junction lane <paramref name="other"/>,
        /// as virtual reservations on it, through the unchanged stock reservation test. Every car on a feeding lane counts
        /// (its route is not read: owner choice, the stricter option), but ONLY when it would win the stock priority test
        /// against the deciding car (JR-T-002g, owner ruling 14 Nov 2026): VehicleUtils.GetPriority(car) - num6 &gt; 0,
        /// the same test and the same priority source stock uses for a car on (and a reservation of) the conflicting lane,
        /// and the condition S2-10 projects under (ArrivalProjection.FeedingCarBlocks). num10 / num6 = the stock test's
        /// values for this overlap. Returns the speed limit this adds (already clamped to the speed range), or float.MaxValue.
        ///
        /// Feeding lanes, from stock data only: a node lane starts at PathNode(source edge, lane index, segment) of the
        /// edge lane that ends there (Game.Net.LaneSystem node-lane creation; edge lanes end at PathNode(edge, ...)), so
        /// the source is the connected edge of the lane's owner node whose index the start node carries
        /// (PathNode.OwnerEquals), or the node itself for a lane chained inside the node; its sub-lanes whose
        /// Lane.m_EndNode equals the junction lane's m_StartNode are the feeding lanes (the same test as
        /// Game.Simulation.TrafficLightSystem.CheckNextLane). One level upstream only, as stock reservations.
        ///
        /// 0.2.4 (JR-T-002h, owner rulings 14 Nov 2026, binding): a feeding car its own signal stops is skipped
        /// (Core FeedingSignal): <paramref name="otherSignal"/> is the conflicting junction lane's LaneSignal exactly as
        /// stock CheckOverlappingLanes just read it for this overlap (componentData2, default = None when the lane has
        /// none); that lane is the one the feeding car would enter, so it is the signal facing that car. Red skips,
        /// amber skips when the stock amber test (GetBrakingDistance(car, speed, 0f) &lt;= distance from its front to
        /// the junction lane start) says it stops; emergency vehicles ignore a non-physical signal as in stock.
        /// carLanes / geometries: the iterator's read-only stock lookups (CarLane flags of the junction and feeding
        /// lanes, the feeding car's ObjectGeometryData front offset).
        /// </summary>
        public float FeedingLaneYield(Entity other, float otherLength, float num10, int num6, LaneSignal otherSignal, Entity self,
            ref BufferLookup<LaneObject> laneObjects, ref ComponentLookup<Curve> curves, ref ComponentLookup<Moving> moving,
            ref ComponentLookup<Car> cars, ref ComponentLookup<Controller> controllers, ref ComponentLookup<PrefabRef> prefabRefs,
            ref ComponentLookup<CarData> prefabCars, ref ComponentLookup<Game.Net.CarLane> carLanes,
            ref ComponentLookup<ObjectGeometryData> geometries, ref ComponentLookup<LaneReservation> reservations,
            CarData car, float safeTimeStep, Bounds1 speedRange, float currentMax)
        {
            if (!Projecting) return float.MaxValue;
            // 0.3.3 (JR-T-005): never for an Unsafe conflicting lane (unsafe U-turn etc.): its feeding cars are not taking it.
            if (carLanes.TryGetComponent(other, out Game.Net.CarLane unsafeTest) && FeedingScan.SkipsConflictingLane((uint)unsafeTest.m_Flags))
            {
                if (Counting) m_FeedUnsafeSkip = true;
                return float.MaxValue;
            }
            // JR-T-002g: no stock car priority can win against num6 (e.g. a main-road car): nothing on a feeding lane counts.
            if (!ArrivalProjection.WinsPriority(ArrivalProjection.MaxCarPriority, num6)) return float.MaxValue;
            // 0.2.4 cheap scan: only the deciding car (not a follower) or the crossing car's own re-check; not when it is
            // already held as hard (also every car further from W than its braking distance); not when the conflicting
            // lane's own reservation already makes stock hold it the same way (the stock test right after this block).
            float hold = MathUtils.Clamp(VehicleUtils.GetMaxBrakingSpeed(car, m_OvStopWait, safeTimeStep), speedRange);
            if (!(hold < currentMax)) return float.MaxValue;
            bool reservationBlocks = reservations.TryGetComponent(other, out LaneReservation reservation)
                && ArrivalProjection.ReservationBlocks(reservation.GetOffset(), num10, reservation.GetPriority(), num6);
            bool ahead = m_OvPhase == PointPhase.Deciding && HasCarAhead(ref laneObjects, ref controllers, ref cars, self);
            if (!FeedingScan.ShouldScan(m_OvPhase, ahead, hold, currentMax, reservationBlocks))
            {
                if (Counting && ahead) m_FeedFollowerSkips++;
                return float.MaxValue;
            }
            if (!m_LaneData.TryGetComponent(other, out Lane otherLane) || !m_OwnerData.TryGetComponent(other, out Owner owner)) return float.MaxValue;
            PathNode start = otherLane.m_StartNode;
            if (!m_OwnStartRead)
            {
                m_OwnStartRead = true;
                m_HasOwnStart = m_LaneData.TryGetComponent(m_LaneEntity, out Lane ownLane);
                m_OwnStart = ownLane.m_StartNode;
            }
            // Fed from the car's own approach (a sibling lane): cars there are behind it (stock queueing).
            if (m_HasOwnStart && start.Equals(m_OwnStart)) return float.MaxValue;
            // 0.2.4: a finished scan of the same source in this car-update bounds what any car there could do.
            if (m_FeedMemo.TryGet(start, out int memoPriority, out float memoReach, out float memoSpeed))
            {
                if (FeedingScan.NothingCanCount(memoPriority, memoReach, memoSpeed, num6, m_OvDelta))
                {
                    if (Counting) m_FeedMemoSkips++;
                    return float.MaxValue;
                }
            }
            if (m_FeedCarsRead >= FeedingScan.MaxCarsPerUpdate) return float.MaxValue;
            Entity source = Entity.Null;
            if (m_ConnectedEdges.TryGetBuffer(owner.m_Owner, out DynamicBuffer<ConnectedEdge> edges))
            {
                for (int i = 0; i < edges.Length; i++)
                {
                    Entity e = edges[i].m_Edge;
                    if (new PathNode(e, 0).OwnerEquals(start)) { source = e; break; }
                }
            }
            if (source == Entity.Null && new PathNode(owner.m_Owner, 0).OwnerEquals(start)) source = owner.m_Owner;
            if (source == Entity.Null || !m_SubLanes.TryGetBuffer(source, out DynamicBuffer<Game.Net.SubLane> subLanes)) return float.MaxValue;
            if (Counting) m_FeedScans++;
            // 0.2.4: the junction lane's signal as the feeding cars see it (stock reads it only on a signalled lane).
            int signal = (int)otherSignal.m_Signal;
            int signalFlags = (int)otherSignal.m_Flags;
            bool junctionSignalled = false, junctionApproach = false;
            if (FeedingSignal.CanSkip(signal) && carLanes.TryGetComponent(other, out Game.Net.CarLane otherCarLane))
            {
                junctionSignalled = (otherCarLane.m_Flags & (Game.Net.CarLaneFlags.LevelCrossing | Game.Net.CarLaneFlags.TrafficLights)) != 0;
                junctionApproach = (otherCarLane.m_Flags & Game.Net.CarLaneFlags.Approach) != 0;
            }
            int maxPriority = -1, lanes = 0;
            float maxReach = float.NegativeInfinity, maxSpeed = 0f;
            for (int i = 0; i < subLanes.Length; i++)
            {
                Entity feed = subLanes[i].m_SubLane;
                if (feed == other || feed == m_CurrentLane || feed == m_CurrentLane2) continue;
                if (!m_LaneData.TryGetComponent(feed, out Lane feedLane) || !feedLane.m_EndNode.Equals(start)) continue;
                if (++lanes > FeedingScan.MaxLanesPerScan) { CapHit(); return float.MaxValue; }
                if (!laneObjects.TryGetBuffer(feed, out DynamicBuffer<LaneObject> objects) || objects.Length == 0 || !curves.TryGetComponent(feed, out Curve feedCurve)) continue;
                bool applies = false;
                if (junctionSignalled)
                {
                    bool feedApproach = carLanes.TryGetComponent(feed, out Game.Net.CarLane feedCarLane) && (feedCarLane.m_Flags & Game.Net.CarLaneFlags.Approach) != 0;
                    applies = FeedingSignal.Applies(true, junctionApproach, feedApproach);
                }
                for (int j = 0; j < objects.Length; j++)
                {
                    LaneObject o = objects[j];
                    Entity e = o.m_LaneObject;
                    if (e == self) continue;
                    // A trailer (or anything driven by another controller, incl. the car's own trailers): its controller is counted.
                    if (controllers.TryGetComponent(e, out Controller c) && c.m_Controller != Entity.Null && c.m_Controller != e) continue;
                    if (!cars.TryGetComponent(e, out Car otherCar)) continue;
                    float x = o.m_CurvePosition.x;
                    if (o.m_CurvePosition.y < x) continue;   // driving this lane inverted (two-way lane): not towards its end
                    if (!moving.TryGetComponent(e, out Moving m)) continue;
                    float speed = math.dot(m.m_Velocity, math.normalizesafe(MathUtils.Tangent(feedCurve.m_Bezier, x)));
                    if (!(speed > 0f)) continue;
                    if (!prefabRefs.TryGetComponent(e, out PrefabRef pr) || !prefabCars.TryGetComponent(pr.m_Prefab, out CarData otherData)) continue;
                    if (++m_FeedCarsRead > FeedingScan.MaxCarsPerUpdate) { CapHit(); return float.MaxValue; }
                    int otherPriority = VehicleUtils.GetPriority(otherCar);
                    float bd = VehicleUtils.GetBrakingDistance(otherData, speed, ReservationTimeStep);
                    float remaining = (1f - x) * feedCurve.m_Length;
                    // Memo bound over every candidate car (before the priority and signal tests, so it stays an upper bound).
                    maxPriority = math.max(maxPriority, otherPriority);
                    maxReach = math.max(maxReach, bd - 0.01f - remaining);
                    maxSpeed = math.max(maxSpeed, speed);
                    // JR-T-002g: only a car that would win the stock priority test against the deciding car counts.
                    if (!ArrivalProjection.WinsPriority(otherPriority, num6)) continue;
                    // 0.2.4: red skips; amber skips when the stock amber test says it stops (emergency: stock ignores a non-physical signal).
                    if (applies && FeedingSignal.Obeys(otherPriority, signalFlags))
                    {
                        float stockSpeed = math.min(math.length(m.m_Velocity), otherData.m_MaxSpeed);
                        float front = geometries.TryGetComponent(pr.m_Prefab, out ObjectGeometryData geometry) ? geometry.m_Bounds.max.z : 0f;
                        float toStop = FeedingSignal.RestOfLane(math.distance(MathUtils.Position(feedCurve.m_Bezier, x), feedCurve.m_Bezier.d), feedCurve.m_Length, x) - front;
                        if (FeedingSignal.Skips(true, signal, signalFlags, otherPriority, VehicleUtils.GetBrakingDistance(otherData, stockSpeed, 0f), toStop)) continue;
                    }
                    float offset = ArrivalProjection.VirtualOffset(remaining, bd, speed, m_OvDelta, otherLength);
                    if (offset < 0f || !ArrivalProjection.FeedingCarBlocks(offset, num10, otherPriority, num6)) continue;
                    if (Counting) m_FeedingLaneHit = true;
                    return MathUtils.Clamp(Apply(StretchState.ForPrediction(m_OvPhase), float.MaxValue, car, safeTimeStep, speedRange), speedRange);
                }
            }
            m_FeedMemo.Put(start, maxPriority, maxReach, maxSpeed);
            return float.MaxValue;
        }

        private void CapHit()
        {
            if (Counting) m_FeedCapHits++;
        }

        /// <summary>
        /// 0.2.4: the car is a follower: another car is ahead of it on its own (edge) approach lane, so before its entry
        /// spot W (W lies on the junction lane). Read once per car-update from the lane's LaneObject buffer (same
        /// direction only; trailers belong to their controller). On a node lane the test is not made (no skip).
        /// </summary>
        private bool HasCarAhead(ref BufferLookup<LaneObject> laneObjects, ref ComponentLookup<Controller> controllers, ref ComponentLookup<Car> cars, Entity self)
        {
            if (m_AheadRead) return m_HasAhead;
            m_AheadRead = true;
            m_HasAhead = false;
            if (m_CurrentLane == Entity.Null || m_NodeLaneData.HasComponent(m_CurrentLane)) return false;
            if (!laneObjects.TryGetBuffer(m_CurrentLane, out DynamicBuffer<LaneObject> objects) || objects.Length < 2) return false;
            float2 own = default;
            bool found = false;
            for (int j = 0; j < objects.Length; j++)
            {
                if (objects[j].m_LaneObject == self) { own = objects[j].m_CurvePosition; found = true; break; }
            }
            if (!found) return false;
            bool forward = own.y >= own.x;
            for (int j = 0; j < objects.Length; j++)
            {
                LaneObject o = objects[j];
                Entity e = o.m_LaneObject;
                if (e == self) continue;
                if (controllers.TryGetComponent(e, out Controller c) && c.m_Controller != Entity.Null && c.m_Controller != e) continue;
                if (!cars.TryGetComponent(e, out Car _)) continue;
                bool sameWay = (o.m_CurvePosition.y >= o.m_CurvePosition.x) == forward;
                if (!sameWay) continue;
                if (forward ? o.m_CurvePosition.x > own.x : o.m_CurvePosition.x < own.x) { m_HasAhead = true; break; }
            }
            return m_HasAhead;
        }

        /// <summary>Distance for the game's vehicle braking (UpdateMaxSpeed) on a conflicting lane.</summary>
        public float VehicleDistance(bool inScope, float stockDistance, bool projected, bool merge)
        {
            if (!inScope) return stockDistance;
            return WholePathRule.Distance(StretchState.ForVehicle(m_OvPhase, projected, merge), stockDistance, m_OvStopWait);
        }

        /// <summary>Counters only: after the game's vehicle braking, note what limited the car.</summary>
        public void AfterVehicle(bool inScope, float maxSpeedBefore, float maxSpeedAfter, bool projected, bool merge, float start)
        {
            if (!inScope || !Counting || !(maxSpeedAfter < maxSpeedBefore)) return;
            if (merge && !projected) return;
            Record(maxSpeedAfter, StretchState.ForVehicle(m_OvPhase, projected, merge), !projected);
        }

        private void Record(float speed, WholePathRule.YieldAction a, bool physical)
        {
            if (a == WholePathRule.YieldAction.StopAtSpot)
            {
                // 0.4.1: a vehicle point after the zebra of its section, decided before that zebra ("Cs after").
                if (m_ZebraActive && !m_OvZebra && m_OvPhase == PointPhase.Deciding
                    && m_ZebraLayout.IsAfterZebra(m_Stretch.Spots.StretchOf(m_OvStart), m_OvStart))
                {
                    Min(ref m_HasZebraCs, ref m_ZebraCsSpeed, speed);
                }
                // 0.4.2: a turner standing at its turn spot inside the box (UK right-turn exception).
                if (m_BoxTurnIndex >= 0 && m_OvPhase == PointPhase.Deciding && m_Stretch.Next == m_BoxTurnIndex
                    && m_Stretch.Spots.StretchOf(m_OvStart) == m_BoxTurnIndex)
                {
                    Min(ref m_HasBoxTurnWait, ref m_BoxTurnWaitSpeed, speed);
                }
                if (m_OvPhase == PointPhase.Safety) Min(ref m_HasSafety, ref m_SafetySpeed, speed);
                else if (m_Stretch.Spots.Count > 0 && m_OvStart > m_Stretch.Spots[0] + WholePathRule.SpotEpsilon) Min(ref m_HasLater, ref m_LaterSpeed, speed);
            }
            else if (a == WholePathRule.YieldAction.Stock && m_Inside)
            {
                if (physical) Min(ref m_HasInsideCrossing, ref m_InsideCrossingSpeed, speed);
                else Min(ref m_HasInsideYield, ref m_InsideYieldSpeed, speed);
            }
        }

        private static void Min(ref bool has, ref float value, float speed)
        {
            value = has ? (speed < value ? speed : value) : speed;
            has = true;
        }

        /// <summary>Diagnostics (b1): one conflict point seen from the car standing at W: its distance from W and the projected own arrival time.</summary>
        private void Sample(float u, float delta)
        {
            if (!m_Counters.IsCreated) return;
            float d = m_Stretch.Length * (u - m_Stretch.Spots[0]);
            if (d < 0f) d = 0f;
            Add(m_Counters, Stage2Counters.PointSamples, 1);
            Add(m_Counters, Stage2Counters.PointDistanceSumCm, (long)(d * 100f));
            Add(m_Counters, Stage2Counters.ArrivalSumMs, (long)(delta * 1000f));
            Add(m_Counters, Stage2Counters.DistanceHistogram + PointBuckets.Distance(d), 1);
            Add(m_Counters, Stage2Counters.ArrivalHistogram + PointBuckets.Arrival(delta), 1);
        }

        /// <summary>The safety-event table (a persistent NativeArray&lt;long&gt;) as atomic slots.</summary>
        private unsafe struct TableSlots : IAtomicSlots
        {
            public long* m_P;
            public int m_N;

            public int Length => m_N;

            public long Read(int i) => Interlocked.Read(ref m_P[i]);

            public bool CompareExchange(int i, long value, long comparand) => Interlocked.CompareExchange(ref m_P[i], value, comparand) == comparand;
        }

        /// <summary>
        /// Called once per car and step by the navigation job (JR-STAGE2 block S2-22) when counting: adds this car's
        /// events to the shared counters. finalMaxSpeed is the iterator's result, currentSpeed the car's speed;
        /// eventTable / entity / frame: 0.2.3 safety-stop events (a car's transition into a safety stop counted once).
        /// </summary>
        public unsafe void AddCounts(NativeArray<long> counters, NativeArray<long> eventTable, Entity entity, uint frame, float finalMaxSpeed, float currentSpeed)
        {
            if (!Counting || !counters.IsCreated) return;
            if (m_Decision) Add(counters, Stage2Counters.Decisions, 1);
            if (Stage2Counters.IsStandingOn(m_HasLater, m_LaterSpeed, finalMaxSpeed, currentSpeed)) Add(counters, Stage2Counters.WaitedForLater, 1);
            if (Stage2Counters.IsStandingOn(m_HasInsideYield, m_InsideYieldSpeed, finalMaxSpeed, currentSpeed)) Add(counters, Stage2Counters.StoppedInsideYield, 1);
            if (Stage2Counters.IsStandingOn(m_HasInsideCrossing, m_InsideCrossingSpeed, finalMaxSpeed, currentSpeed)) Add(counters, Stage2Counters.StoppedInsideCrossing, 1);
            if (m_ProjectedLaneHit) Add(counters, Stage2Counters.ProjectedLaneHits, 1);
            if (m_FeedingLaneHit) Add(counters, Stage2Counters.FeedingLaneHits, 1);
            if (m_FeedScans != 0) Add(counters, Stage2Counters.FeedingScans, m_FeedScans);
            if (m_FeedFollowerSkips != 0) Add(counters, Stage2Counters.FeedingFollowerSkips, m_FeedFollowerSkips);
            if (m_FeedMemoSkips != 0) Add(counters, Stage2Counters.FeedingMemoSkips, m_FeedMemoSkips);
            if (m_FeedCapHits != 0) Add(counters, Stage2Counters.FeedingCapHits, m_FeedCapHits);
            if (m_TurnOverrides != 0) Add(counters, Stage2Counters.TurnOverrides, m_TurnOverrides);
            if (m_TurnDiscounts != 0) Add(counters, Stage2Counters.TurnReservationDiscounts, m_TurnDiscounts);
            if (m_RefusedTimer || m_RefusedStanding || m_RefusedCyclist)
            {
                Add(counters, Stage2Counters.CrossingRefused, 1);
                if (m_RefusedTimer) Add(counters, Stage2Counters.CrossingRefusedTimer, 1);
                if (m_RefusedStanding) Add(counters, Stage2Counters.CrossingRefusedStanding, 1);
                if (m_RefusedCyclist) Add(counters, Stage2Counters.CrossingRefusedCyclist, 1);
            }
            if (m_FeedUnsafeSkip) Add(counters, Stage2Counters.FeedingUnsafeSkips, 1);
            if (m_CrossingAtCrossing) Add(counters, Stage2Counters.CrossingsAtCrossing, 1);
            if (m_CrossingNoFit) Add(counters, Stage2Counters.CrossingsNoFit, 1);
            if (Stage2Counters.IsStandingOn(m_HasZebraPeople, m_ZebraPeopleSpeed, finalMaxSpeed, currentSpeed)) Add(counters, Stage2Counters.ZebraHeldPeople, 1);
            if (Stage2Counters.IsStandingOn(m_HasZebraCs, m_ZebraCsSpeed, finalMaxSpeed, currentSpeed)) Add(counters, Stage2Counters.ZebraHeldCs, 1);
            if (Stage2Counters.IsStandingOn(m_HasZebraRoom, m_ZebraRoomSpeed, finalMaxSpeed, currentSpeed)) Add(counters, Stage2Counters.ZebraHeldRoom, 1);
            if (m_ZebraMergedSeen && currentSpeed < Stage2Counters.StandingSpeed) Add(counters, Stage2Counters.ZebraMerged, 1);
            if (m_ZebraEarlySeen) Add(counters, Stage2Counters.ZebraEarly, 1);
            if (m_ZebraFurtherSeen) Add(counters, Stage2Counters.ZebraFurther, 1);
            if (m_ZebraUnsortedSeen) Add(counters, Stage2Counters.ZebraScanUnsorted, 1);
            if (Stage2Counters.IsStandingOn(m_HasBoxRoom, m_BoxRoomSpeed, finalMaxSpeed, currentSpeed)) Add(counters, Stage2Counters.BoxHeldRoom, 1);
            if (Stage2Counters.IsStandingOn(m_HasBoxTurnWait, m_BoxTurnWaitSpeed, finalMaxSpeed, currentSpeed)) Add(counters, Stage2Counters.BoxTurnWaits, 1);
            if (Stage2Counters.IsStandingOn(m_HasBoxTurnRoom, m_BoxTurnRoomSpeed, finalMaxSpeed, currentSpeed)) Add(counters, Stage2Counters.BoxTurnRoomWaits, 1);
            if (Stage2Counters.IsStandingOn(m_HasEntranceMain, m_EntranceMainSpeed, finalMaxSpeed, currentSpeed)) Add(counters, Stage2Counters.EntranceHeldMain, 1);
            if (Stage2Counters.IsStandingOn(m_HasEntranceLeaving, m_EntranceLeavingSpeed, finalMaxSpeed, currentSpeed)) Add(counters, Stage2Counters.EntranceHeldLeaving, 1);
            // 0.4.4: vehicles leaving buildings give way. 0.4.6: counted once per vehicle episode (consecutive updates of one vehicle
            // are one event, per kind; 0.4.4 / 0.4.5 added one per car-step, so a single waiting truck made dozens).
            if (Stage2Counters.IsStandingOn(m_HasExitHold, m_ExitHoldSpeed, finalMaxSpeed, currentSpeed) && ExitEvent(eventTable, entity, frame, 1))
            {
                Add(counters, Stage2Counters.ExitHeld, 1);
                if (m_ExitAcross) Add(counters, Stage2Counters.ExitHeldAcross, 1);
                if (m_ExitSpawn) Add(counters, Stage2Counters.ExitHeldSpawn, 1);
            }
            if (Stage2Counters.IsStandingOn(m_HasExitBox, m_ExitBoxSpeed, finalMaxSpeed, currentSpeed) && ExitEvent(eventTable, entity, frame, 2)) Add(counters, Stage2Counters.ExitSpawnBoxHeld, 1);
            if (m_ExitGo && ExitEvent(eventTable, entity, frame, 3)) Add(counters, Stage2Counters.ExitGo, 1);
            if (m_ExitEmergency && ExitEvent(eventTable, entity, frame, 4)) Add(counters, Stage2Counters.ExitEmergencyPass, 1);
            if (m_ExitCapHit) Add(counters, Stage2Counters.ExitCapHits, 1);
            if (Stage2Counters.IsBinding(m_HasSafety, m_SafetySpeed, finalMaxSpeed))
            {
                Add(counters, Stage2Counters.SafetyStops, 1);
                bool isNew = true;
                if (eventTable.IsCreated)
                {
                    TableSlots slots = default;
                    slots.m_P = (long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(eventTable);
                    slots.m_N = eventTable.Length;
                    isNew = SafetyStopEvents.Record(ref slots, entity.Index, frame);
                }
                if (isNew) Add(counters, Stage2Counters.SafetyStopEvents, 1);
            }
        }

        /// <summary>
        /// 0.4.6: true when this update starts a new event of the given kind (1 wait, 2 box wait, 3 go, 4 emergency, 5 hand-over) for
        /// the vehicle, false when it continues one (the shared event table, keys in a per-kind range: Core ExitGiveWay.EventKey).
        /// Without a table every update counts (as before).
        /// </summary>
        private static unsafe bool ExitEvent(NativeArray<long> eventTable, Entity entity, uint frame, int kind)
        {
            if (!eventTable.IsCreated) return true;
            TableSlots slots = default;
            slots.m_P = (long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(eventTable);
            slots.m_N = eventTable.Length;
            return SafetyStopEvents.Record(ref slots, ExitGiveWay.EventKey(entity.Index, kind), frame);
        }

        private static unsafe void Add(NativeArray<long> counters, int index, long value)
        {
            long* p = (long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(counters);
            Interlocked.Add(ref p[index], value);
        }
    }
}
