using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Objects;
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
    /// 0.4.2 (JR-T-010), job side of "Cars obey yellow boxes" (Core/BoxDriving.cs). Fields live in JRWholeJunction.cs (one
    /// declaration holds every field, so the struct layout stays defined). Read-only lookups handed in by the copy's
    /// navigation job (S2-26 SetBoxes); nothing shared is written except the debug counters (AddCounts).
    /// </summary>
    public partial struct JRWholeJunction
    {
        /// <summary>
        /// 0.4.2 (block S2-26, only with m_BoxRule): the read-only box lookups. Junction boxes are YellowBoxJunction on the
        /// node (the Owner of its node lanes; 0.4.3: also where a building's own access road meets a public road, and
        /// 0.4.0-0.4.2 boxes migrated there). 0.4.3: exit boxes come as bands in 'boxEdges' (one per boxed car exit: road
        /// edge + curve position, resolved on the main thread), handed in by reference: the iterator keeps a pointer to the
        /// navigation job's single copy (0.4.3 review F1: no ~1 KB copy per car). No building is read (no
        /// one-box-per-building assumption; the 0.4.2 entrance / building / ConnectedBuilding lookups are gone).
        /// </summary>
        public unsafe void SetBoxes(ComponentLookup<JunctionRules.Boxes.YellowBoxJunction> junctions, ComponentLookup<Game.Net.EdgeLane> edgeLanes, ref JRBoxEdges boxEdges,
            ComponentLookup<LaneSignal> signals)
        {
            m_BoxJunctionData = junctions;
            m_EdgeLaneData = edgeLanes;
            m_BoxSignalData = signals;   // 0.4.10 (sweep): a red / amber light inside a box's room window = no room
            m_BoxEdges = (JRBoxEdges*)UnsafeUtility.AddressOf(ref boxEdges);
            m_BoxLookups = true;
        }

        /// <summary>The lane is a node lane of a boxed junction (setting on, junction boxes exist).</summary>
        private bool IsBoxedJunctionLane(Entity lane) =>
            m_BoxRule && m_BoxJunctions && m_BoxLookups && m_NodeLaneData.HasComponent(lane)
            && m_OwnerData.TryGetComponent(lane, out Owner owner) && m_BoxJunctionData.HasComponent(owner.m_Owner);

        /// <summary>
        /// 0.4.4 (JR-T-011): the boxed junction node the car's path enters right after 'lane' (the next path lane is a node
        /// lane of a node with YellowBoxJunction); Entity.Null when none or no path.
        /// </summary>
        private Entity NextBoxedJunction(Entity lane)
        {
            if (!m_BoxJunctions || !m_HasPath) return Entity.Null;
            int next = -1;
            for (int i = 0; i < m_Path.Length; i++)
            {
                if (m_Path[i].m_Lane == lane) { next = i + 1; break; }
            }
            if (next < 0 && (lane == m_CurrentLane || lane == m_CurrentLane2)) next = 0;
            if (next < 0 || next >= m_Path.Length) return Entity.Null;
            Entity nl = m_Path[next].m_Lane;
            if (m_NodeLaneData.HasComponent(nl) && m_OwnerData.TryGetComponent(nl, out Owner o) && m_BoxJunctionData.HasComponent(o.m_Owner)) return o.m_Owner;
            return Entity.Null;
        }

        private bool OnNode(Entity lane, Entity node) =>
            lane != Entity.Null && m_NodeLaneData.HasComponent(lane) && m_OwnerData.TryGetComponent(lane, out Owner o) && o.m_Owner == node;

        /// <summary>
        /// The turn spot of a boxed junction lane (driving coordinates; 2 = none): with the right-turn exception on and the
        /// lane turning across oncoming traffic, the start of its first yield-checked overlap with an oncoming straight lane
        /// (a Forward lane whose end edge is this lane's start edge: the Core/TurnAcross.cs pair).
        /// </summary>
        private float TurnSpot(Entity lane, DynamicBuffer<LaneOverlap> overlaps, bool inverse, ref ComponentLookup<Game.Net.CarLane> carLanes)
        {
            if (!m_BoxTurnWait || !carLanes.TryGetComponent(lane, out Game.Net.CarLane own) || !m_LaneData.TryGetComponent(lane, out Lane ownLane)) return 2f;
            if (!BoxDriving.TurnerMayWait(true, (uint)own.m_Flags, m_BoxLeftHand)) return 2f;
            float spot = 2f;
            for (int i = 0; i < overlaps.Length; i++)
            {
                LaneOverlap o = overlaps[i];
                if (!carLanes.TryGetComponent(o.m_Other, out Game.Net.CarLane other) || !m_LaneData.TryGetComponent(o.m_Other, out Lane otherLane)) continue;
                if (!BoxDriving.IsOncomingStraight((uint)other.m_Flags, ownLane.m_StartNode.OwnerEquals(otherLane.m_EndNode))) continue;
                if (!StretchGeometry.ToConflict((int)o.m_Flags, o.m_ThisStart, o.m_ThisEnd, inverse, false, out float s, out _)) continue;
                if (s < spot) spot = s;
            }
            return spot;
        }

        /// <summary>
        /// 0.4.2 (blocks S2-28 / S2-28b, top of both CheckCurrentLane overloads, only with m_BoxRule): the hold for the lane
        /// being iterated (m_Lane, its curve length, m_CurveOffset in game offsets, the game's distance from the car's front
        /// to the point at m_CurveOffset.x before its stop margin). Returns the speed limit (clamped) or float.MaxValue.
        /// Junction box: the lane is a node lane of a boxed node, the entry is the lane start (the stop line). Exit band
        /// (0.4.3): the lane is an edge lane of a road with boxed car exits; every exit on it is its own band. The hold is the game's own stop at the entry,
        /// GetMaxBrakingSpeed(car, distance, safe time step), as the zebra hold.
        /// </summary>
        public unsafe float BoxHold(Entity lane, float laneLength, float2 curveOffset, float distance, float margin, bool inverse, CarData car, float currentSpeed,
            float timeStep, float safeTimeStep, Bounds1 speedRange, float currentMax, Entity self, ref ComponentLookup<Game.Net.CarLane> carLanes,
            ref BufferLookup<LaneObject> laneObjects, ref ComponentLookup<Curve> curves, ref ComponentLookup<Moving> moving, ref ComponentLookup<Controller> controllers,
            ref ComponentLookup<PrefabRef> prefabRefs, ref ComponentLookup<ObjectGeometryData> geometries, ref ComponentLookup<CarData> prefabCars,
            ref BufferLookup<LaneOverlap> overlapData, ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes)
        {
            BeginHoldBlocker();   // 0.4.9 (G17)
            // 0.4.11 (G21 box rule): BoxHold runs at the top of every lane's CheckCurrentLane, before that lane's BeginLane (S2-04):
            // keep the curve lookup for BeginLane's "is this way in boxed" test (WayInBoxed).
            m_BoxCurves = curves;
            m_HasBoxCurves = true;
            m_BoxPedestrians = pedestrianLanes;   // 0.4.12 (G29): for SkipStockRoomHold's G22 test (S2-54)
            m_HasBoxPedestrians = true;
            if (!m_BoxLookups || !carLanes.HasComponent(lane)) return float.MaxValue;
            float start = StretchGeometry.ToDriving(curveOffset.x, inverse);
            float end = StretchGeometry.ToDriving(curveOffset.y, inverse);
            // The game's num5 formula: stop distance to driving coordinate u = (distance - margin) + length * (u - start).
            float wait0 = distance - margin;
            if (m_NodeLaneData.HasComponent(lane))
            {
                if (!m_BoxJunctions || !m_OwnerData.TryGetComponent(lane, out Owner owner) || !m_BoxJunctionData.HasComponent(owner.m_Owner)) return float.MaxValue;
                bool inBox = m_FirstLane || OnNode(m_CurrentLane, owner.m_Owner) || OnNode(m_CurrentLane2, owner.m_Owner);
                float wait = wait0 - laneLength * start;
                if (Counting && currentSpeed < Stage2Counters.StandingSpeed)
                {
                    // 0.4.10 probes (not counted at the right-turn spot, AddCounts): standing on the box's lanes, and a nose in the
                    // box (front bumper past the stop line, rear still before it). 0.4.11 (G22): a car standing in the box under
                    // the same-exit-lane exception is counted apart (allowed by the owner rule).
                    bool sameExitChecked = false, sameExitOk = false;
                    if (inBox)
                    {
                        sameExitChecked = true;
                        sameExitOk = SameExitAdmits(lane, owner.m_Owner, laneLength, inverse, start, self, ref carLanes, ref laneObjects, ref curves, ref controllers, ref prefabRefs, ref geometries, ref overlapData, ref pedestrianLanes, ref moving);
                        if (sameExitOk) m_BoxSameExitStanding = true;
                        else m_BoxStoodInside = true;
                    }
                    float fl = BoxDriving.FrontToLine(wait, margin);
                    // Review 3 F3: a car admitted by the G22 exception is counted apart, never as a nose in the box.
                    if (BoxDriving.NoseIn(fl, m_VehicleLength) && !m_BoxSameExitStanding
                        && !(sameExitChecked ? sameExitOk : SameExitAdmits(lane, owner.m_Owner, laneLength, inverse, start, self, ref carLanes, ref laneObjects, ref curves, ref controllers, ref prefabRefs, ref geometries, ref overlapData, ref pedestrianLanes, ref moving)))
                    {
                        m_BoxStoodInside = true;
                        // 0.4.12 (G29): stopped past the line by the lane's own red / amber light (the game's amber rule) is counted
                        // apart; every other nose is a violation (must be 0) and gets one trace line naming it
                        if (m_BoxSignalData.TryGetComponent(lane, out LaneSignal noseSig) && BoxDriving.LightStopsInWindow((int)noseSig.m_Signal)) m_BoxNoseLight = true;
                        else
                        {
                            m_BoxNose = true;
                            m_NoseOwner = owner.m_Owner;
                            m_NoseFront = -fl;
                            m_NoseSameExitEligible = sameExitChecked && sameExitOk;
                        }
                    }
                }
                BoxDriving.Approach a = BoxDriving.Classify(inBox, wait, margin, VehicleUtils.GetBrakingDistance(car, currentSpeed, timeStep));
                if (a != BoxDriving.Approach.Check) return float.MaxValue;
                float hold = MathUtils.Clamp(VehicleUtils.GetMaxBrakingSpeed(car, wait, safeTimeStep), speedRange);
                if (!(hold < currentMax)) return float.MaxValue;
                // 0.4.10 (G19): room free NOW past the box (no credit for a moving queue). Chains: a turner with the UK exception may
                // queue in the box behind other turners (Highway Code 174), so standing vehicles on its own path in the box do not block it.
                m_QueuedTurnersMayStand = m_BoxTurnWait && BoxDriving.TurnerMayWait(true, (uint)carLanes[lane].m_Flags, m_BoxLeftHand);
                bool room = HasRoomPast(lane, 1f, inverse, laneLength, 0f, owner.m_Owner, self, safeTimeStep, ref carLanes, ref laneObjects, ref curves,
                    ref moving, ref controllers, ref prefabRefs, ref geometries, ref prefabCars, ref overlapData, ref pedestrianLanes, freeNow: true);
                m_QueuedTurnersMayStand = false;
                if (!BoxDriving.Holds(a, room)) return float.MaxValue;
                // 0.4.11 (G22, owner): a car from the lowest-priority approach may enter although its exit lane is full when the
                // spot where it would wait (whole body) crosses only paths into that same exit lane (Core/SameExitException.cs).
                if (SameExitAdmits(lane, owner.m_Owner, laneLength, inverse, start, self, ref carLanes, ref laneObjects, ref curves, ref controllers, ref prefabRefs, ref geometries, ref overlapData, ref pedestrianLanes, ref moving))
                {
                    m_BoxSameExitEntry = true;
                    return float.MaxValue;
                }
                NoteLightPetition(hold);   // review R1
                if (Counting)
                {
                    Min(ref m_HasBoxRoom, ref m_BoxRoomSpeed, hold);
                    if (m_FreeNowHeld) Min(ref m_HasBoxFreeNow, ref m_BoxFreeNowSpeed, hold);
                }
                return hold;
            }
            // 0.4.3: the boxed CAR EXITS on this road (JRBoxEdges: edge key + curve position per exit, resolved on the main
            // thread from the saved exit keys); one binary search finds the edge's first band. Buildings are not read here.
            if (!m_BoxEntrances || !m_EdgeLaneData.HasComponent(lane) || !m_OwnerData.TryGetComponent(lane, out Owner edgeOwner))
                return float.MaxValue;
            if (!LaneBands(lane, edgeOwner.m_Owner, laneLength, inverse, carLanes[lane].m_CarriagewayGroup, ref curves, out FixedList128Bytes<float2> merged)) return float.MaxValue;
            float braking = -1f;
            float result = float.MaxValue;
            for (int m = 0; m < merged.Length; m++)
            {
                float b0 = merged[m].x, b1 = merged[m].y;
                // 0.4.10 probe: a standing car with any part of its body inside this box (front past the line, rear not yet past the far edge)
                if (Counting && currentSpeed < Stage2Counters.StandingSpeed && G28Standing(self))
                {
                    // review F4 (G28): an exiter that entered this box under the same-exit-lane exception (episode still running)
                    // waits there by the owner's rule: counted as same-exit standing, not as a box violation
                    m_BoxSameExitStanding = true;
                    m_BoxSameExitEntry = true;   // keeps the episode running (one entry counted)
                }
                else if (Counting && currentSpeed < Stage2Counters.StandingSpeed)
                {
                    float f0 = BoxDriving.FrontToLine(wait0 + laneLength * (b0 - start), margin);
                    if (BoxDriving.BodyInside(f0, f0 + (b1 - b0) * laneLength, m_VehicleLength)) m_BoxStoodInside = true;
                    if (BoxDriving.NoseIn(f0, m_VehicleLength))   // review 3 F1: the nose counter measures standing cars, not holds
                    {
                        m_BoxNose = true;
                        m_NoseOwner = edgeOwner.m_Owner;   // 0.4.12 (G29 trace): the road edge of the exit / entrance box
                        m_NoseFront = -f0;
                        m_NoseSameExitEligible = false;
                    }
                }
                BoxDriving.EntranceRole role = BoxDriving.Role(start, end, b0, b1, out float entry);
                if (role == BoxDriving.EntranceRole.Entering)
                {
                    // JR-T-014c (G9): turning INTO the building inside this band: held before the band while its way in is full.
                    Entity keepEnter = m_LastBlocker;   // review 2 (G17): only the binding hold names its blocker
                    float enterHold = EnteringHold(lane, edgeOwner.m_Owner, start, b0, laneLength, wait0, margin, car, currentSpeed, timeStep, safeTimeStep, speedRange,
                        currentMax < result ? currentMax : result, self, ref braking, ref carLanes, ref laneObjects, ref curves, ref moving, ref controllers, ref prefabRefs,
                        ref geometries, ref prefabCars);
                    if (enterHold < result) result = enterHold;
                    else m_LastBlocker = keepEnter;
                    continue;
                }
                if (role != BoxDriving.EntranceRole.Main && role != BoxDriving.EntranceRole.Leaving) continue;
                bool inBand = m_FirstLane && start >= b0 - WholePathRule.SpotEpsilon;
                float wait = wait0 + laneLength * (entry - start);
                if (braking < 0f) braking = VehicleUtils.GetBrakingDistance(car, currentSpeed, timeStep);
                BoxDriving.Approach a = BoxDriving.Classify(inBand, wait, margin, braking);
                if (a != BoxDriving.Approach.Check) continue;
                float hold = MathUtils.Clamp(VehicleUtils.GetMaxBrakingSpeed(car, wait, safeTimeStep), speedRange);
                if (!(hold < currentMax) || !(hold < result)) continue;
                // 0.4.4: a band reaching the lane end where the path enters a boxed junction is one box with it (the paint
                // bridges that gap): room is then needed past the junction box (its lanes count as inside the box).
                Entity joined = (1f - b1) * laneLength <= ExitBands.JunctionJoinMetres ? NextBoxedJunction(lane) : Entity.Null;
                // 0.4.9 (G15b, review 1): vehicles standing across the lane from a driveway count only for traffic driving THROUGH the
                // band (Main); a vehicle leaving the drive (Leaving) never waits for one (it could be the entrant that waits for it).
                Entity keepBand = m_LastBlocker;
                m_SideHeld = false;
                // 0.4.10 (G19): room free NOW past the band, for through traffic and for a vehicle leaving into it alike.
                bool room = HasRoomPast(lane, b1, inverse, laneLength, entry, joined, self, safeTimeStep, ref carLanes, ref laneObjects, ref curves,
                    ref moving, ref controllers, ref prefabRefs, ref geometries, ref prefabCars, ref overlapData, ref pedestrianLanes, role == BoxDriving.EntranceRole.Main,
                    freeNow: true);
                if (!BoxDriving.Holds(a, room)) { m_LastBlocker = keepBand; continue; }
                // 0.4.12 (G28): a vehicle leaving a drive into this band under the same-exit-lane exception (S2-29 G28Check this
                // update: near-side merge, footprint over only this lane's own traffic) waits in the box at the back of the queue.
                if (role == BoxDriving.EntranceRole.Leaving && lane != Entity.Null && (m_G28JoinLane == lane || m_G28Latched == lane))
                {
                    // review F4: counted where the exit judgement lets it go (S2-29), not here (it may still be held for a gap there)
                    m_LastBlocker = keepBand;
                    continue;
                }
                result = hold;
                NoteLightPetition(hold);   // review R1
                if (Counting && m_FreeNowHeld) Min(ref m_HasBoxFreeNow, ref m_BoxFreeNowSpeed, hold);
                // 0.4.9 (G15b, final review): held only because a vehicle turning into (or out of) a driveway stands across the
                // lane: it waits for that building like an entrant, so an exit within 10 m does not wait for it (no
                // through car -> entrant -> yard -> exit -> through car loop). On the exit's conflict it still blocks.
                if (m_SideHeld) NoteEnteringHeld(self);
                if (Counting)
                {
                    if (role == BoxDriving.EntranceRole.Leaving) Min(ref m_HasEntranceLeaving, ref m_EntranceLeavingSpeed, hold);
                    else Min(ref m_HasEntranceMain, ref m_EntranceMainSpeed, hold);
                }
            }
            return result;
        }

        /// <summary>
        /// The boxed exit / entrance bands on one road lane (driving coordinates), shared by BoxHold (S2-28) and the exit
        /// rule's ExitBandBlocks (JRExitGiveWay.cs). 0.4.4 (JR-T-011): only the bands of this lane's carriageway (a divided
        /// road: the exit's side of the median, CarLane.m_CarriagewayGroup low byte = NetCompositionLane.m_Carriageway,
        /// LaneSystem.cs 4153), and bands that overlap or touch on this lane are ONE band. 0.4.9 (G14 / G15): bands closer
        /// than this vehicle's whole length + room margin are one band for its hold (UK yellow box: no part of it may stop
        /// in a box), at least the 2 m the paint merges (Core ExitBands.HoldTouchMetres).
        /// 0.4.10 (JR-T-016, G18 / G19): each band is the PAINTED window (joint windows included) projected onto this lane's
        /// own curve (Core/LaneWindow.cs), not the edge curve position mapped through EdgeLane.m_EdgeDelta plus the box
        /// length: lanes are built on the edge's cut geometry (mouth to mouth), so that mapping put the bands metres off the
        /// paint (12-13 m at Alderbrook Street's 843726 exit). Bands come sorted by t on the edge and each window lies
        /// around its t, so neighbours in this order are neighbours on the lane. False = no band on this lane.
        /// </summary>
        private unsafe bool LaneBands(Entity lane, Entity edge, float laneLength, bool inverse, int carriageway, ref ComponentLookup<Curve> curves,
            out FixedList128Bytes<float2> merged)
        {
            merged = default;
            JRBoxEdges* bands = m_BoxEdges;
            int first = bands->First(edge);
            if (first < 0 || !(laneLength > 0f) || !curves.TryGetComponent(lane, out Curve curve)) return false;
            long edgeKey = bands->Key(first);
            Bezier4x3 bz = curve.m_Bezier;
            float touch = ExitBands.HoldTouchMetres(m_VehicleLength, m_RoomMargin) / laneLength;
            for (int i = first; i < bands->Count && bands->Key(i) == edgeKey; i++)
            {
                if (!MedianSplit.InMask(bands->Mask(i), carriageway) || !bands->HasWindow(i)) continue;
                if (!LaneWindow.OnLane(bz.a.x, bz.a.z, bz.b.x, bz.b.z, bz.c.x, bz.c.z, bz.d.x, bz.d.z, laneLength, inverse,
                    bands->m_X0[i], bands->m_Z0[i], bands->m_X1[i], bands->m_Z1[i], out float b0, out float b1)) continue;
                int last = merged.Length - 1;
                if (last >= 0 && ExitBands.Touch(merged[last].x, merged[last].y, b0, b1, touch))
                {
                    merged[last] = math.float2(math.min(merged[last].x, b0), math.max(merged[last].y, b1));
                    continue;
                }
                if (merged.Length >= merged.Capacity) break;
                merged.Add(math.float2(b0, b1));
            }
            return merged.Length > 0;
        }

        /// <summary>
        /// JR-T-014c (G9, owner 17 Nov 2026): a vehicle whose path leaves the road lane inside an exit / entrance band into a
        /// building (BoxDriving.EntranceRole.Entering; 0.4.2-0.4.7 exempt) does not enter the band unless there is room for its
        /// whole length (trailers included, m_VehicleLength) plus the room margin on its way in: the path lanes after this
        /// road lane (the drive's connection lane, the drive / building lanes; up to MaxRoomLanes), read by the same RoomScan as
        /// boxes and zebras (moving vehicles credit their braking distance, standing ones do not; the unknown rest and a path
        /// that ends inside the building count as free). A lane change inside the band is not an entry (BoxDriving
        /// .EnteringApplies). 0.4.8-0.4.10: a car turning across oncoming traffic that had room entered and waited for the
        /// oncoming gap inside the box at the game's own give-way of its connection lane. 0.4.11 (G21, owner): it waits BEFORE
        /// the box in its own lane until the traffic its turn crosses leaves a gap (JRExitGiveWay.EntryRoadHold, same road
        /// lane, same step, after this hook); this hook keeps the room test. Returns the speed limit or float.MaxValue.
        /// </summary>
        private float EnteringHold(Entity lane, Entity road, float start, float b0, float laneLength, float wait0, float margin, CarData car, float currentSpeed,
            float timeStep, float safeTimeStep, Bounds1 speedRange, float currentMax, Entity self, ref float braking, ref ComponentLookup<Game.Net.CarLane> carLanes,
            ref BufferLookup<LaneObject> laneObjects, ref ComponentLookup<Curve> curves, ref ComponentLookup<Moving> moving, ref ComponentLookup<Controller> controllers,
            ref ComponentLookup<PrefabRef> prefabRefs, ref ComponentLookup<ObjectGeometryData> geometries, ref ComponentLookup<CarData> prefabCars)
        {
            int next = NextPathIndex(lane);
            bool hasNext = next >= 0 && next < m_Path.Length;
            // A lane change stays on an EDGE lane of the same road. The drive's way in starts on an edge CONNECTION lane, which
            // the game also owns by the road edge but builds as a NodeLane, not an EdgeLane (LaneSystem.cs 645-653 owner = the
            // edge; CreateCarEdgeConnections adds NodeLane): so only an EdgeLane of this road counts as staying on it.
            Entity nextLane = hasNext ? m_Path[next].m_Lane : Entity.Null;
            bool sameRoad = hasNext && m_EdgeLaneData.HasComponent(nextLane) && m_OwnerData.TryGetComponent(nextLane, out Owner nextOwner) && nextOwner.m_Owner == road;
            if (!BoxDriving.EnteringApplies(hasNext, sameRoad)) return float.MaxValue;
            float entry = BoxDriving.EnteringEntry(start, b0);
            bool inBand = m_FirstLane && start >= b0 - WholePathRule.SpotEpsilon;
            float wait = wait0 + laneLength * (entry - start);
            if (braking < 0f) braking = VehicleUtils.GetBrakingDistance(car, currentSpeed, timeStep);
            BoxDriving.Approach a = BoxDriving.Classify(inBand, wait, margin, braking);
            if (a != BoxDriving.Approach.Check)
            {
                // Always-on entrance log (owner rule 0.4.8): in the box (or too close to stop): carries on; the room read
                // here is for the log only (did it go in without room, does it stand in the box).
                Entity keepLog = m_LastBlocker;
                bool roomNow = HasRoomInto(next, self, safeTimeStep, ref carLanes, ref laneObjects, ref curves, ref moving, ref controllers, ref prefabRefs, ref geometries, ref prefabCars);
                m_LastBlocker = keepLog;   // review 2: a log-only read names nobody
                EntranceWrite(self, road, EntranceLog.Pack(EntranceLog.Event.InBox, roomNow, currentSpeed, b0));
                return float.MaxValue;
            }
            float hold = MathUtils.Clamp(VehicleUtils.GetMaxBrakingSpeed(car, wait, safeTimeStep), speedRange);
            if (!(hold < currentMax))
            {
                // 0.4.9 review F1 (G15): a STANDING entrant held harder by something else (in practice the entrant ahead of it in
                // the same queue for the full building) would also be held by this rule: record it too, so an exit within 10 m
                // does not wait for the second van of the queue either. Read only while it stands (one room read per update).
                Entity keepF1 = m_LastBlocker;
                if (currentSpeed < ExitGiveWay.EnteringStandSpeed
                    && !HasRoomInto(next, self, safeTimeStep, ref carLanes, ref laneObjects, ref curves, ref moving, ref controllers, ref prefabRefs, ref geometries, ref prefabCars))
                    NoteEnteringHeld(self);
                m_LastBlocker = keepF1;   // review 2: not this hook's binding hold
                return float.MaxValue;
            }
            bool room = HasRoomInto(next, self, safeTimeStep, ref carLanes, ref laneObjects, ref curves, ref moving, ref controllers, ref prefabRefs, ref geometries, ref prefabCars);
            bool holds = BoxDriving.EnteringHolds(a, room);
            EntranceWrite(self, road, EntranceLog.Pack(holds ? EntranceLog.Event.Held : EntranceLog.Event.Go, room, currentSpeed, b0));
            if (!holds) return float.MaxValue;
            // 0.4.9 (G15): exits nearby do not wait for a vehicle that waits for room INTO a building (JRExitGiveWay.EnteringHeld).
            NoteEnteringHeld(self);
            if (Counting) Min(ref m_HasEntranceEntering, ref m_EntranceEnteringSpeed, hold);
            return hold;
        }

        /// <summary>
        /// JR-T-014c (G9): one always-on entrance log record (Core/EntranceLog.cs layout) into the fixed ring the job's band
        /// set carries (JRBoxEdges.m_EntranceLog): an atomic sequence number, four plain stores, the stamp last. Nothing else.
        /// </summary>
        private unsafe void EntranceWrite(Entity self, Entity road, long facts)
        {
            JRBoxEdges* bands = m_BoxEdges;
            if (bands == null || !bands->m_EntranceLog.IsCreated || bands->m_EntranceLog.Length < EntranceLog.RingLength) return;
            long* p = (long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(bands->m_EntranceLog);
            long seq = System.Threading.Interlocked.Increment(ref p[0]);
            int b = EntranceLog.SlotBase(seq);
            System.Threading.Interlocked.Exchange(ref p[b], 0L);
            p[b + 1] = EntranceLog.PackEntity(self.Index, self.Version);
            p[b + 2] = EntranceLog.PackEntity(road.Index, road.Version);
            p[b + 3] = facts;
            p[b + 4] = bands->m_Frame;
            System.Threading.Interlocked.Exchange(ref p[b], seq);
        }

        /// <summary>JR-T-014c: index in the car's navigation lanes of the lane after 'lane' (-1 = no path / not on it).</summary>
        private int NextPathIndex(Entity lane)
        {
            if (!m_HasPath) return -1;
            for (int i = 0; i < m_Path.Length; i++) if (m_Path[i].m_Lane == lane) return i + 1;
            return lane == m_CurrentLane || lane == m_CurrentLane2 ? 0 : -1;
        }

        /// <summary>
        /// JR-T-013b (G3, block S2-37 in CheckOverlappingLanes after BeginLane / BeginZebras, only with m_BoxRule): the UK
        /// right-turn exception re-runs the box room test at the turn spot. BoxHold tests the exit room only at the stop line
        /// (a car in the box is Inside there), so a turner that entered with room crossed the oncoming lane on the first
        /// gap even when its exit had filled meanwhile, and stood across the oncoming lane (straight traffic then waited
        /// for it: the owner's G3). Now, while the turn spot is its next spot and it can stop before it, it stays at the
        /// spot (before the first oncoming conflict) until its exit has room. The room scan reads only its own path (rest of
        /// this lane from its front, then its exit lanes), never the oncoming lanes, so the hold waits only on the exit
        /// queue: no new wait on oncoming traffic. Returns the speed limit (clamped) or float.MaxValue.
        /// </summary>
        public float BoxTurnRoomHold(Entity lane, float length, bool inverse, CarData car, float safeTimeStep, Bounds1 speedRange, float currentMax, Entity self,
            ref ComponentLookup<Game.Net.CarLane> carLanes, ref BufferLookup<LaneObject> laneObjects, ref ComponentLookup<Curve> curves,
            ref ComponentLookup<Moving> moving, ref ComponentLookup<Controller> controllers, ref ComponentLookup<PrefabRef> prefabRefs,
            ref ComponentLookup<ObjectGeometryData> geometries, ref ComponentLookup<CarData> prefabCars,
            ref BufferLookup<LaneOverlap> overlapData, ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes)
        {
            BeginHoldBlocker();   // 0.4.9 (G17)
            if (!RuleOn || !m_BoxLane || !m_LaneActive || m_LaneEntity != lane || !m_BoxLookups) return float.MaxValue;
            int turn = m_BoxTurnIndex;
            if (turn < 0 || turn >= m_Stretch.Spots.Count) return float.MaxValue;
            float spot = m_Stretch.Spots[turn];
            if (!BoxDriving.TurnSpotRoomCheck(turn, m_Stretch.Next, m_Stretch.Spots.Count, m_Stretch.CanStopBefore(spot))) return float.MaxValue;
            float hold = MathUtils.Clamp(VehicleUtils.GetMaxBrakingSpeed(car, m_Stretch.Wait(spot), safeTimeStep), speedRange);
            if (!(hold < currentMax)) return float.MaxValue;
            if (!m_OwnerData.TryGetComponent(lane, out Owner owner)) return float.MaxValue;
            // 0.4.10 (G19): the right-turner may wait at its in-box spot only while its exit has room free NOW (other turners
            // standing ahead of it on its path in the box do not block it: Highway Code 174).
            m_QueuedTurnersMayStand = true;
            bool room = HasRoomPast(lane, 1f, inverse, length, math.max(0f, m_Stretch.CurveX), owner.m_Owner, self, safeTimeStep, ref carLanes, ref laneObjects, ref curves,
                ref moving, ref controllers, ref prefabRefs, ref geometries, ref prefabCars, ref overlapData, ref pedestrianLanes, freeNow: true);
            m_QueuedTurnersMayStand = false;
            if (room) return float.MaxValue;
            // 0.4.11 (G22): the same-exit-lane exception also at the UK turn spot (a turner that entered under it is not stopped
            // there, on a footprint the exception did not check; it still gives way to oncoming traffic by the gap rule).
            if (SameExitAdmits(lane, owner.m_Owner, length, inverse, math.max(0f, m_Stretch.CurveX), self, ref carLanes, ref laneObjects, ref curves, ref controllers, ref prefabRefs, ref geometries, ref overlapData, ref pedestrianLanes, ref moving))
            {
                m_BoxSameExitEntry = true;
                return float.MaxValue;
            }
            NoteLightPetition(hold);   // review R1
            if (Counting)
            {
                Min(ref m_HasBoxTurnRoom, ref m_BoxTurnRoomSpeed, hold);
                if (m_FreeNowHeld) Min(ref m_HasBoxFreeNow, ref m_BoxFreeNowSpeed, hold);
            }
            return hold;
        }
    }
}
