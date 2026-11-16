using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Vehicles;
using JunctionRules.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace JunctionRules.Stage2
{
    /// <summary>
    /// 0.4.4 (JR-T-012), job side of "Vehicles leaving buildings give way" (Core/ExitGiveWay.cs; owner: "Cars coming out of
    /// ANY building must give way, and when that building exit is a yellow box, must ALSO obey the yellow box rules").
    /// Fields live in JRWholeJunction.cs (one declaration holds every field). Read-only lookups handed in by the copy's
    /// navigation job (S2-26 SetExits); nothing shared is written except the debug counters (AddCounts).
    ///
    /// Two hooks, both only for the vehicle at its own exit (cheap: a vehicle further back than its braking distance, or
    /// already held harder by the vehicle in front of it, never scans):
    /// <list type="bullet">
    /// <item>Driveway exits (block S2-29, CheckOverlappingLanes of a SideConnection + Yield lane): the conflicts are the
    /// game's own LaneOverlap entries of the drive lane with the road's car lanes (crossed and merged into); the vehicle
    /// waits before the first one (the game's stop margin) until every one is clear (<see cref="ConflictBusy"/>).</item>
    /// <item>Connection exits (spawn location / car park gate; blocks S2-30 / S2-31 in the navigation job): the vehicle is
    /// on a building's Connection lane and its next lane is a public road lane. Conflicts: that road lane at the join
    /// point and every car lane of the same road between the building side and it (geometry, Core ExitGiveWay.Role).
    /// While not clear (or, at a boxed exit, no room past the band) it stands on its Connection lane (inside the building,
    /// unspawned) and is kept off the road lane.</item>
    /// </list>
    /// </summary>
    public partial struct JRWholeJunction
    {
        /// <summary>0.4.4 (S2-26): the exit lookups and the game's traffic side (job field m_LeftHandTraffic, the city's own flag).</summary>
        public void SetExits(ComponentLookup<Game.Buildings.Building> buildings, ComponentLookup<Game.Net.Edge> edges, bool leftHandTraffic)
        {
            m_ExitBuildingData = buildings;
            m_ExitEdgeData = edges;
            m_ExitLeftHand = leftHandTraffic;
            m_ExitLookups = true;
        }

        /// <summary>Work done by one exit decision (caps: Core ExitGiveWay.MaxScanLanes / MaxVehiclesPerScan).</summary>
        private struct ExitBudget
        {
            public int m_Lanes;
            public int m_Vehicles;
            public bool m_CapHit;
        }

        /// <summary>The lookups one exit scan reads (the caller's read-only stock lookups, passed by ref).</summary>
        private struct ExitReads
        {
            public Entity m_Self;
            public ComponentLookup<Game.Net.CarLane> m_CarLanes;
            public ComponentLookup<LaneSignal> m_Signals;
            public BufferLookup<LaneObject> m_LaneObjects;
            public ComponentLookup<Curve> m_Curves;
            public ComponentLookup<Moving> m_Moving;
            public ComponentLookup<Car> m_Cars;
            public ComponentLookup<Controller> m_Controllers;
            public ComponentLookup<PrefabRef> m_PrefabRefs;
            public ComponentLookup<CarData> m_PrefabCars;
            public ComponentLookup<ObjectGeometryData> m_Geometries;
        }

        /// <summary>
        /// S2-29: the hold for a vehicle about to use a drive's leaving lane (m_Lane of CheckOverlappingLanes; origDistance
        /// = the game's distance from its front to the point at curveX, after its "- margin"). Returns the speed limit or
        /// float.MaxValue. On-duty emergency vehicles are never held (stock).
        /// </summary>
        public float ExitDrivewayHold(Entity lane, DynamicBuffer<LaneOverlap> overlaps, float curveX, float length, float origDistance, float margin, bool inverse,
            int priority, CarData car, float currentSpeed, float timeStep, float safeTimeStep, Bounds1 speedRange, float currentMax, float speedLimit, Entity self,
            ref ComponentLookup<Game.Net.CarLane> carLanes, ref ComponentLookup<LaneCondition> laneConditions, ref ComponentLookup<LaneSignal> signals,
            ref BufferLookup<LaneObject> laneObjects, ref ComponentLookup<Curve> curves, ref ComponentLookup<Moving> moving, ref ComponentLookup<Car> cars,
            ref ComponentLookup<Controller> controllers, ref ComponentLookup<PrefabRef> prefabRefs, ref ComponentLookup<CarData> prefabCars,
            ref ComponentLookup<ObjectGeometryData> geometries)
        {
            if (!m_ExitRule || !m_ExitLookups) return float.MaxValue;
            if (!carLanes.TryGetComponent(lane, out Game.Net.CarLane own) || !ExitGiveWay.IsDrivewayExitLane((uint)own.m_Flags)) return float.MaxValue;
            if (ExitGiveWay.IsEmergency(priority))
            {
                if (Counting) m_ExitEmergency = true;
                return float.MaxValue;
            }
            float start = StretchGeometry.ToDriving(curveX, inverse);
            // First conflict on the way: the start of the first overlap with a road car lane not yet passed.
            float first = 2f;
            for (int i = 0; i < overlaps.Length; i++)
            {
                if (!ExitConflict(overlaps[i], inverse, start, ref carLanes, out float s, out _)) continue;
                if (s < first) first = s;
            }
            if (first > 1f) return float.MaxValue;
            float wait = origDistance + length * (first - start);
            if (!ExitGiveWay.CanHold(wait, margin, VehicleUtils.GetBrakingDistance(car, currentSpeed, timeStep))) return float.MaxValue;
            float hold = MathUtils.Clamp(VehicleUtils.GetMaxBrakingSpeed(car, wait > 0f ? wait : 0f, safeTimeStep), speedRange);
            // Only the vehicle at its exit decides: further back (or held harder by the vehicle in front) it does not scan.
            if (!(hold < currentMax)) return float.MaxValue;
            float drive = speedLimit;
            {
                Game.Net.CarLane limited = own;
                limited.m_SpeedLimit = speedLimit;
                drive = VehicleUtils.GetMaxDriveSpeed(car, limited);
                if (laneConditions.TryGetComponent(lane, out LaneCondition condition)) VehicleUtils.ModifyDriveSpeed(ref drive, condition);
            }
            ExitReads reads = default;
            reads.m_Self = self;
            reads.m_CarLanes = carLanes;
            reads.m_Signals = signals;
            reads.m_LaneObjects = laneObjects;
            reads.m_Curves = curves;
            reads.m_Moving = moving;
            reads.m_Cars = cars;
            reads.m_Controllers = controllers;
            reads.m_PrefabRefs = prefabRefs;
            reads.m_PrefabCars = prefabCars;
            reads.m_Geometries = geometries;
            ExitBudget budget = default;
            GameMotion motion = default;
            motion.m_Car = car;
            bool busy = false;
            int judged = 0;
            for (int i = 0; i < overlaps.Length && !busy && !budget.m_CapHit; i++)
            {
                LaneOverlap o = overlaps[i];
                if (!ExitConflict(o, inverse, start, ref carLanes, out float s, out _)) continue;
                if (++judged > ExitGiveWay.MaxConflictLanes) break;
                if (!curves.TryGetComponent(o.m_Other, out Curve otherCurve)) continue;
                // The other lane's range of the overlap, in its own (driving) direction: road lanes run along their curve.
                float a = o.m_OtherStart * WholePathRule.ByteToOffset, b = o.m_OtherEnd * WholePathRule.ByteToOffset;
                float oS = math.min(a, b) * otherCurve.m_Length, oE = math.max(a, b) * otherCurve.m_Length;
                // The leaving vehicle's own arrival time at this conflict (its game motion, from where it is now).
                float toConflict = origDistance + margin + length * (s - start);
                float delta = ArrivalTime.Compute(ref motion, toConflict, currentSpeed, drive, 0f, timeStep);
                busy = ConflictBusy(o.m_Other, otherCurve, oS, oE, delta, ref reads, ref budget);
            }
            if (Counting && budget.m_CapHit) m_ExitCapHit = true;
            if (!busy)
            {
                if (Counting) m_ExitGo = true;
                return float.MaxValue;
            }
            if (Counting)
            {
                Min(ref m_HasExitHold, ref m_ExitHoldSpeed, hold);
                m_ExitAcross = ExitGiveWay.TurnsAcross((own.m_Flags & Game.Net.CarLaneFlags.TurnLeft) != 0, (own.m_Flags & Game.Net.CarLaneFlags.TurnRight) != 0, m_ExitLeftHand);
            }
            return hold;
        }

        /// <summary>An overlap of a drive's leaving lane that the give-way judges: a road car lane (not another drive lane), not yet passed.</summary>
        private static bool ExitConflict(LaneOverlap o, bool inverse, float start, ref ComponentLookup<Game.Net.CarLane> carLanes, out float s, out float e)
        {
            s = e = 0f;
            if (((int)o.m_Flags & WholePathRule.Water) != 0) return false;
            if (!carLanes.TryGetComponent(o.m_Other, out Game.Net.CarLane other) || (other.m_Flags & Game.Net.CarLaneFlags.SideConnection) != 0) return false;
            float a = o.m_ThisStart * WholePathRule.ByteToOffset, b = o.m_ThisEnd * WholePathRule.ByteToOffset;
            s = inverse ? 1f - b : a;
            e = inverse ? 1f - a : b;
            return e > start;
        }

        /// <summary>
        /// S2-30 (navigation job, the vehicle's current lane is a Connection lane): the hold for a vehicle leaving a
        /// building through a spawn location / car park gate. next = its first navigation lane. Returns 0 (stand on the
        /// Connection lane) or float.MaxValue, and sets <see cref="ExitWaiting"/> for S2-31.
        /// </summary>
        public float ExitConnectionHold(Entity self, int priority, Entity connection, CarNavigationLane next, float safeTimeStep,
            ref ComponentLookup<Game.Net.CarLane> carLanes, ref ComponentLookup<LaneSignal> signals, ref BufferLookup<LaneObject> laneObjects,
            ref ComponentLookup<Curve> curves, ref ComponentLookup<Moving> moving, ref ComponentLookup<Car> cars, ref ComponentLookup<Controller> controllers,
            ref ComponentLookup<PrefabRef> prefabRefs, ref ComponentLookup<CarData> prefabCars, ref ComponentLookup<ObjectGeometryData> geometries,
            ref BufferLookup<LaneOverlap> overlapData, ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes)
        {
            m_ExitWaiting = false;
            if (!m_ExitRule || !m_ExitLookups) return float.MaxValue;
            if (!ExitJoinLane(connection, priority, next, ref carLanes, ref curves, out Entity road, out Curve roadCurve, out Owner roadOwner)) return float.MaxValue;
            if (!curves.TryGetComponent(connection, out Curve connCurve)) return float.MaxValue;
            // 0.4.4: the vehicle appears on the join lane with its body behind the join point and no arrival time of its own.
            bool wait = ExitJoinWaits(self, priority, next.m_CurvePosition.x, road, roadCurve, roadOwner, connCurve.m_Bezier.a, 0f, safeTimeStep,
                ref carLanes, ref signals, ref laneObjects, ref curves, ref moving, ref cars, ref controllers, ref prefabRefs, ref prefabCars, ref geometries,
                ref overlapData, ref pedestrianLanes, out ExitGiveWay.Decision d);
            if (!wait)
            {
                if (Counting) m_ExitGo = true;
                return float.MaxValue;
            }
            m_ExitWaiting = true;
            if (Counting)
            {
                if (d == ExitGiveWay.Decision.WaitGap) { Min(ref m_HasExitHold, ref m_ExitHoldSpeed, 0f); m_ExitSpawn = true; }
                else Min(ref m_HasExitBox, ref m_ExitBoxSpeed, 0f);
            }
            return 0f;
        }

        /// <summary>
        /// S2-32 (navigation job, the vehicle's current lane is an AREA lane): the usual building exit. A building with a
        /// lot (industry, depots, car parks with a surface) lets its vehicles drive the lot's area lanes (ConnectionLane with
        /// ConnectionLaneFlags.Area: CarLaneFlags.Area, not Connection; InitializeSystem 288, CarNavigationSystem 672 / 1037)
        /// and those never reach the lane speed iterator (the stock branch "(LaneFlags &amp; Area) == 0", 1596, skips it), so
        /// S2-30 never ran for them and the road lane they join (the next navigation lane, LaneConnectionSystem ties the lot
        /// to the nearest road lane) was never looked at. Same decision as a Connection exit, applied while the vehicle drives
        /// up to the join point: it brakes to stop short of it (the hold is the braking speed to a point one margin before
        /// the join point, never an abrupt 0 far from it) and <see cref="ExitWaiting"/> keeps it on its area lane (S2-33).
        /// </summary>
        public float ExitAreaHold(Entity self, int priority, Entity areaLane, CarNavigationLane next, float3 position, CarData car, float currentSpeed, float timeStep,
            float safeTimeStep, Bounds1 speedRange, float currentMax,
            ref ComponentLookup<Game.Net.CarLane> carLanes, ref ComponentLookup<LaneSignal> signals, ref BufferLookup<LaneObject> laneObjects,
            ref ComponentLookup<Curve> curves, ref ComponentLookup<Moving> moving, ref ComponentLookup<Car> cars, ref ComponentLookup<Controller> controllers,
            ref ComponentLookup<PrefabRef> prefabRefs, ref ComponentLookup<CarData> prefabCars, ref ComponentLookup<ObjectGeometryData> geometries,
            ref BufferLookup<LaneOverlap> overlapData, ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes)
        {
            m_ExitWaiting = false;
            if (!m_ExitRule || !m_ExitLookups) return float.MaxValue;
            if (!ExitJoinLane(areaLane, priority, next, ref carLanes, ref curves, out Entity road, out Curve roadCurve, out Owner roadOwner)) return float.MaxValue;
            float t = next.m_CurvePosition.x;
            float3 join = MathUtils.Position(roadCurve.m_Bezier, t);
            float ownLength = m_VehicleLength > 0f ? m_VehicleLength : 4f;
            // Metres its front may still travel before the join point (the pivot is taken as the middle of the body).
            float toJoin = math.distance(position.xz, join.xz) - ownLength * 0.5f;
            float wait = toJoin - ExitAreaMargin;
            if (!ExitGiveWay.CanHoldArea(wait, VehicleUtils.GetBrakingDistance(car, currentSpeed, timeStep))) return float.MaxValue;
            float hold = MathUtils.Clamp(VehicleUtils.GetMaxBrakingSpeed(car, wait > 0f ? wait : 0f, safeTimeStep), speedRange);
            // Only the vehicle at its exit decides: further back (or held harder already) it does not scan.
            if (!(hold < currentMax)) return float.MaxValue;
            float arrival = 0f;
            if (carLanes.TryGetComponent(road, out Game.Net.CarLane roadLane))
            {
                float drive = VehicleUtils.GetMaxDriveSpeed(car, roadLane);
                GameMotion motion = default;
                motion.m_Car = car;
                arrival = ArrivalTime.Compute(ref motion, toJoin > 0f ? toJoin : 0f, currentSpeed, drive, 0f, timeStep);
            }
            bool busy = ExitJoinWaits(self, priority, t, road, roadCurve, roadOwner, position, arrival, safeTimeStep,
                ref carLanes, ref signals, ref laneObjects, ref curves, ref moving, ref cars, ref controllers, ref prefabRefs, ref prefabCars, ref geometries,
                ref overlapData, ref pedestrianLanes, out ExitGiveWay.Decision d);
            if (!busy)
            {
                if (Counting) m_ExitGo = true;
                return float.MaxValue;
            }
            m_ExitWaiting = true;
            if (Counting)
            {
                if (d == ExitGiveWay.Decision.WaitGap) { Min(ref m_HasExitHold, ref m_ExitHoldSpeed, hold); m_ExitSpawn = true; }
                else Min(ref m_HasExitBox, ref m_ExitBoxSpeed, hold);
            }
            return hold;
        }

        /// <summary>Metres short of the join point an area exit holds its vehicle's front (the stop margin of a waiting vehicle).</summary>
        public const float ExitAreaMargin = 1f;

        /// <summary>
        /// The lane a vehicle leaving a building (from its Connection lane or its area lane) joins: the next navigation lane,
        /// a public road car lane (a car lane of an edge nobody owns, not a drive's side connection). False = stock (not a
        /// building exit, an on-duty emergency vehicle, a lane against its curve, no geometry).
        /// </summary>
        private bool ExitJoinLane(Entity from, int priority, CarNavigationLane next, ref ComponentLookup<Game.Net.CarLane> carLanes, ref ComponentLookup<Curve> curves,
            out Entity road, out Curve roadCurve, out Owner roadOwner)
        {
            road = next.m_Lane;
            roadCurve = default;
            roadOwner = default;
            const Game.Vehicles.CarLaneFlags notRoad = Game.Vehicles.CarLaneFlags.Connection | Game.Vehicles.CarLaneFlags.Area | Game.Vehicles.CarLaneFlags.TransformTarget | Game.Vehicles.CarLaneFlags.ParkingSpace;
            if ((next.m_Flags & notRoad) != 0) return false;
            // A public road lane: a car lane of a road edge that no building / other object owns.
            if (!carLanes.TryGetComponent(road, out Game.Net.CarLane roadLane) || (roadLane.m_Flags & Game.Net.CarLaneFlags.SideConnection) != 0) return false;
            if (!m_OwnerData.TryGetComponent(road, out roadOwner) || !m_ExitEdgeData.HasComponent(roadOwner.m_Owner) || m_OwnerData.HasComponent(roadOwner.m_Owner))
                return false;
            if (!LeavesBuilding(from)) return false;
            if (ExitGiveWay.IsEmergency(priority))
            {
                if (Counting) m_ExitEmergency = true;
                return false;
            }
            if (next.m_CurvePosition.y < next.m_CurvePosition.x) return false;   // against the lane's curve (two-way lane): stock
            return curves.TryGetComponent(road, out roadCurve);
        }

        /// <summary>
        /// The shared decision of a Connection and an area exit: must the vehicle wait at the join point t of road lane
        /// <paramref name="road"/>? buildingSide = a point on the building's side of the road (the Connection lane's start,
        /// or the vehicle itself): the lanes nearer to it than the join lane are crossed, the join lane is merged into.
        /// delta = the leaving vehicle's own arrival time at the join point. True = wait (gap busy, or boxed exit with no room
        /// past the band); decision tells which.
        /// </summary>
        private bool ExitJoinWaits(Entity self, int priority, float t, Entity road, Curve roadCurve, Owner roadOwner, float3 buildingSide, float delta, float safeTimeStep,
            ref ComponentLookup<Game.Net.CarLane> carLanes, ref ComponentLookup<LaneSignal> signals, ref BufferLookup<LaneObject> laneObjects,
            ref ComponentLookup<Curve> curves, ref ComponentLookup<Moving> moving, ref ComponentLookup<Car> cars, ref ComponentLookup<Controller> controllers,
            ref ComponentLookup<PrefabRef> prefabRefs, ref ComponentLookup<CarData> prefabCars, ref ComponentLookup<ObjectGeometryData> geometries,
            ref BufferLookup<LaneOverlap> overlapData, ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes, out ExitGiveWay.Decision d)
        {
            ExitReads reads = default;
            reads.m_Self = self;
            reads.m_CarLanes = carLanes;
            reads.m_Signals = signals;
            reads.m_LaneObjects = laneObjects;
            reads.m_Curves = curves;
            reads.m_Moving = moving;
            reads.m_Cars = cars;
            reads.m_Controllers = controllers;
            reads.m_PrefabRefs = prefabRefs;
            reads.m_PrefabCars = prefabCars;
            reads.m_Geometries = geometries;
            ExitBudget budget = default;
            float joinM = t * roadCurve.m_Length;
            float ownLength = m_VehicleLength > 0f ? m_VehicleLength : 4f;
            // The road lane it joins: it appears there with its body behind the join point (it drives on from there).
            bool busy = ConflictBusy(road, roadCurve, joinM - ownLength, joinM + 1f, delta, ref reads, ref budget);
            if (!busy)
            {
                // The lanes of the same road between the building and the join lane (crossed), by geometry: lateral offset
                // of each lane's point nearest the join point, signed towards the building side.
                float3 join = MathUtils.Position(roadCurve.m_Bezier, t);
                float3 tangent = MathUtils.Tangent(roadCurve.m_Bezier, t);
                float2 normal = math.normalizesafe(new float2(-tangent.z, tangent.x));
                float side = math.dot(buildingSide.xz - join.xz, normal) >= 0f ? 1f : -1f;
                if (m_SubLanes.TryGetBuffer(roadOwner.m_Owner, out DynamicBuffer<Game.Net.SubLane> roadLanes))
                {
                    int judged = 0;
                    for (int i = 0; i < roadLanes.Length && !busy && !budget.m_CapHit; i++)
                    {
                        Entity c = roadLanes[i].m_SubLane;
                        if (c == road || !carLanes.TryGetComponent(c, out Game.Net.CarLane cl) || (cl.m_Flags & Game.Net.CarLaneFlags.SideConnection) != 0) continue;
                        if (!curves.TryGetComponent(c, out Curve cc)) continue;
                        MathUtils.Distance(cc.m_Bezier.xz, join.xz, out float tc);
                        float lateral = math.dot(MathUtils.Position(cc.m_Bezier, tc).xz - join.xz, normal) * side;
                        // Core rule: crossed = nearer to the building than the join lane (distance measured from the building side).
                        if (ExitGiveWay.Role(false, -lateral, 0f) != ExitGiveWay.ConflictRole.Cross) continue;
                        if (++judged > ExitGiveWay.MaxConflictLanes) break;
                        float cm = tc * cc.m_Length;
                        busy = ConflictBusy(c, cc, cm - ExitCrossHalfWidth, cm + ExitCrossHalfWidth, delta, ref reads, ref budget);
                    }
                }
            }
            if (Counting && budget.m_CapHit) m_ExitCapHit = true;
            // At a boxed exit also the 0.4.3 band rule: room past the band on the join lane (stock never iterates the road
            // lane from a Connection / area lane, so the 0.4.3 S2-28 test cannot see this vehicle).
            bool boxed = false, room = true;
            if (!busy && m_BoxRule) room = !ExitBandBlocks(road, roadCurve.m_Length, t, self, safeTimeStep, out boxed, ref carLanes, ref laneObjects, ref curves,
                ref moving, ref controllers, ref prefabRefs, ref geometries, ref prefabCars, ref overlapData, ref pedestrianLanes);
            d = ExitGiveWay.Decide(true, priority, !busy, m_BoxRule, boxed, room);
            return d != ExitGiveWay.Decision.Go;
        }

        /// <summary>Half the length of road lane a crossing at a connection exit takes (m): about a car's width plus a margin.</summary>
        public const float ExitCrossHalfWidth = 2f;

        /// <summary>S2-30 -> S2-31 (same vehicle, same update): the vehicle is held on its Connection lane and must not join the road lane.</summary>
        public bool ExitWaiting => m_ExitWaiting;

        /// <summary>The lane belongs to a building (owner chain, a few levels: building, its sub-object / extension).</summary>
        private bool LeavesBuilding(Entity lane)
        {
            Entity e = lane;
            for (int i = 0; i < 4; i++)
            {
                if (!m_OwnerData.TryGetComponent(e, out Owner owner) || owner.m_Owner == Entity.Null) return false;
                e = owner.m_Owner;
                if (m_ExitBuildingData.HasComponent(e)) return true;
            }
            return false;
        }

        /// <summary>
        /// 0.4.3 band rule for a connection exit: the join point t lies in a boxed exit's band on the road lane and there is
        /// no room for the whole vehicle past the band (the same HasRoomPast as S2-28, entering the band at t).
        /// </summary>
        private unsafe bool ExitBandBlocks(Entity road, float length, float t, Entity self, float safeTimeStep, out bool boxed,
            ref ComponentLookup<Game.Net.CarLane> carLanes, ref BufferLookup<LaneObject> laneObjects, ref ComponentLookup<Curve> curves,
            ref ComponentLookup<Moving> moving, ref ComponentLookup<Controller> controllers, ref ComponentLookup<PrefabRef> prefabRefs,
            ref ComponentLookup<ObjectGeometryData> geometries, ref ComponentLookup<CarData> prefabCars,
            ref BufferLookup<LaneOverlap> overlapData, ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes)
        {
            boxed = false;
            if (!m_BoxLookups || !m_BoxEntrances || !m_EdgeLaneData.TryGetComponent(road, out Game.Net.EdgeLane edgeLane)
                || !m_OwnerData.TryGetComponent(road, out Owner edgeOwner)) return false;
            JRBoxEdges* bands = m_BoxEdges;
            int first = bands->First(edgeOwner.m_Owner);
            if (first < 0) return false;
            long edgeKey = bands->Key(first);
            float span = edgeLane.m_EdgeDelta.y - edgeLane.m_EdgeDelta.x;
            if (math.abs(span) < 1e-4f) return false;
            for (int i = first; i < bands->Count && bands->Key(i) == edgeKey; i++)
            {
                float access = (bands->T(i) - edgeLane.m_EdgeDelta.x) / span;
                if (!BoxDriving.Band(access, length, m_BoxEntranceLength, false, out float b0, out float b1)) continue;
                if (t < b0 - WholePathRule.SpotEpsilon || t > b1 + WholePathRule.SpotEpsilon) continue;
                boxed = true;
                bool room = HasRoomPast(road, b1, false, length, t, Entity.Null, self, safeTimeStep, ref carLanes, ref laneObjects, ref curves,
                    ref moving, ref controllers, ref prefabRefs, ref geometries, ref prefabCars, ref overlapData, ref pedestrianLanes);
                if (!room) return true;
            }
            return false;
        }

        /// <summary>
        /// Is the conflict [cStart, cEnd] (metres along <paramref name="lane"/>, in its driving direction) busy: a vehicle on
        /// it, or one on the lane before it or on the lanes feeding that lane (up to Core ExitGiveWay.MaxFeedDepth levels
        /// upstream: the junction lanes, then the road lanes before them) whose reach meets it (Core
        /// ExitGiveWay.VehicleBlocks; delta = the leaving vehicle's own arrival time). A vehicle its signal stops before
        /// the lane it would enter is skipped (Core FeedingSignal: red always, amber when it can stop).
        /// </summary>
        private bool ConflictBusy(Entity lane, Curve curve, float cStart, float cEnd, float delta, ref ExitReads reads, ref ExitBudget budget)
        {
            if (++budget.m_Lanes > ExitGiveWay.MaxScanLanes) { budget.m_CapHit = true; return false; }
            if (VehiclesBlock(lane, curve, cStart, cEnd, delta, false, default, ref reads, ref budget)) return true;
            if (budget.m_CapHit || !m_LaneData.TryGetComponent(lane, out Lane laneData)) return false;
            // Depth 1: the lanes ending where this lane starts.
            Feeders f1 = default;
            if (!FindFeeders(lane, laneData.m_StartNode, ref f1, ref reads)) return false;
            for (int i = 0; i < f1.Count; i++)
            {
                Entity a = f1.Get(i);
                if (!reads.m_Curves.TryGetComponent(a, out Curve ca)) continue;
                float aStart = ca.m_Length + cStart;   // the conflict start in metres along a
                if (aStart - ca.m_Length > ExitGiveWay.MaxScanMetres) continue;
                if (++budget.m_Lanes > ExitGiveWay.MaxScanLanes) { budget.m_CapHit = true; return false; }
                if (VehiclesBlock(a, ca, aStart, float.MaxValue, delta, false, default, ref reads, ref budget)) return true;
                if (budget.m_CapHit) return false;
                if (ExitGiveWay.MaxFeedDepth < 2 || aStart > ExitGiveWay.MaxScanMetres || !m_LaneData.TryGetComponent(a, out Lane aData)) continue;
                // Depth 2: the lanes ending where a starts. a's signal faces the vehicles on them (it is the lane they enter).
                Feeders f2 = default;
                if (!FindFeeders(a, aData.m_StartNode, ref f2, ref reads)) continue;
                bool aSignalled = false, aApproach = false;
                LaneSignal aSignal = default;
                if (reads.m_CarLanes.TryGetComponent(a, out Game.Net.CarLane aLane))
                {
                    aSignalled = (aLane.m_Flags & (Game.Net.CarLaneFlags.LevelCrossing | Game.Net.CarLaneFlags.TrafficLights)) != 0;
                    aApproach = (aLane.m_Flags & Game.Net.CarLaneFlags.Approach) != 0;
                    if (aSignalled) reads.m_Signals.TryGetComponent(a, out aSignal);
                }
                for (int j = 0; j < f2.Count; j++)
                {
                    Entity bLane = f2.Get(j);
                    if (!reads.m_Curves.TryGetComponent(bLane, out Curve cb)) continue;
                    if (++budget.m_Lanes > ExitGiveWay.MaxScanLanes) { budget.m_CapHit = true; return false; }
                    bool applies = false;
                    if (aSignalled && FeedingSignal.CanSkip((int)aSignal.m_Signal))
                    {
                        bool bApproach = reads.m_CarLanes.TryGetComponent(bLane, out Game.Net.CarLane bl) && (bl.m_Flags & Game.Net.CarLaneFlags.Approach) != 0;
                        applies = FeedingSignal.Applies(true, aApproach, bApproach);
                    }
                    if (VehiclesBlock(bLane, cb, cb.m_Length + aStart, float.MaxValue, delta, applies, aSignal, ref reads, ref budget)) return true;
                    if (budget.m_CapHit) return false;
                }
            }
            return false;
        }

        /// <summary>Up to 8 feeding lanes (fixed fields: Burst, no allocation).</summary>
        private struct Feeders
        {
            private Entity m_0, m_1, m_2, m_3, m_4, m_5, m_6, m_7;
            public int Count;

            public bool Add(Entity e)
            {
                switch (Count)
                {
                    case 0: m_0 = e; break;
                    case 1: m_1 = e; break;
                    case 2: m_2 = e; break;
                    case 3: m_3 = e; break;
                    case 4: m_4 = e; break;
                    case 5: m_5 = e; break;
                    case 6: m_6 = e; break;
                    case 7: m_7 = e; break;
                    default: return false;
                }
                Count++;
                return true;
            }

            public Entity Get(int i)
            {
                switch (i)
                {
                    case 0: return m_0;
                    case 1: return m_1;
                    case 2: return m_2;
                    case 3: return m_3;
                    case 4: return m_4;
                    case 5: return m_5;
                    case 6: return m_6;
                    default: return m_7;
                }
            }
        }

        /// <summary>
        /// The car lanes ending at <paramref name="start"/> (Lane.m_EndNode == the lane's m_StartNode, the stock test of
        /// TrafficLightSystem.CheckNextLane). Edge lane: the lanes of its two nodes and of the edge itself. Node lane: the
        /// lanes of the connected edge its start node names (Game.Pathfind.PathNode.OwnerEquals, as S2-12), else of the node itself.
        /// </summary>
        private bool FindFeeders(Entity lane, Game.Pathfind.PathNode start, ref Feeders found, ref ExitReads reads)
        {
            if (!m_OwnerData.TryGetComponent(lane, out Owner owner)) return false;
            Entity o = owner.m_Owner;
            if (m_ExitEdgeData.TryGetComponent(o, out Game.Net.Edge edge))
            {
                AddFeeders(edge.m_Start, lane, start, ref found, ref reads);
                AddFeeders(edge.m_End, lane, start, ref found, ref reads);
                AddFeeders(o, lane, start, ref found, ref reads);
                return found.Count > 0;
            }
            Entity source = Entity.Null;
            if (m_ConnectedEdges.TryGetBuffer(o, out DynamicBuffer<ConnectedEdge> edges))
            {
                for (int i = 0; i < edges.Length; i++)
                {
                    Entity e = edges[i].m_Edge;
                    if (new Game.Pathfind.PathNode(e, 0).OwnerEquals(start)) { source = e; break; }
                }
            }
            AddFeeders(source != Entity.Null ? source : o, lane, start, ref found, ref reads);
            return found.Count > 0;
        }

        private void AddFeeders(Entity owner, Entity lane, Game.Pathfind.PathNode start, ref Feeders found, ref ExitReads reads)
        {
            if (owner == Entity.Null || !m_SubLanes.TryGetBuffer(owner, out DynamicBuffer<Game.Net.SubLane> subLanes)) return;
            for (int i = 0; i < subLanes.Length; i++)
            {
                Entity feed = subLanes[i].m_SubLane;
                if (feed == lane || !m_LaneData.TryGetComponent(feed, out Lane feedLane) || !feedLane.m_EndNode.Equals(start)) continue;
                if (!reads.m_CarLanes.HasComponent(feed)) continue;
                if (!found.Add(feed)) return;
            }
        }

        /// <summary>
        /// The vehicles on one lane against a conflict starting cStart metres along it (beyond its end for a feeding lane)
        /// and ending at cEnd. Trailers count through their controller; the leaving vehicle itself and its trailers never.
        /// </summary>
        private bool VehiclesBlock(Entity lane, Curve curve, float cStart, float cEnd, float delta, bool signalApplies, LaneSignal signal,
            ref ExitReads reads, ref ExitBudget budget)
        {
            if (!reads.m_LaneObjects.TryGetBuffer(lane, out DynamicBuffer<LaneObject> objects) || objects.Length == 0) return false;
            int signalType = (int)signal.m_Signal, signalFlags = (int)signal.m_Flags;
            for (int j = 0; j < objects.Length; j++)
            {
                LaneObject o = objects[j];
                Entity e = o.m_LaneObject;
                if (e == reads.m_Self) continue;
                if (reads.m_Controllers.TryGetComponent(e, out Controller c) && c.m_Controller != Entity.Null && c.m_Controller != e) continue;
                if (!reads.m_Cars.TryGetComponent(e, out Car otherCar)) continue;
                float x = o.m_CurvePosition.x;
                if (o.m_CurvePosition.y < x) continue;   // driving this lane against its curve (two-way lane): not towards the conflict
                if (!reads.m_PrefabRefs.TryGetComponent(e, out PrefabRef pr) || !reads.m_PrefabCars.TryGetComponent(pr.m_Prefab, out CarData data)) continue;
                if (++budget.m_Vehicles > ExitGiveWay.MaxVehiclesPerScan) { budget.m_CapHit = true; return false; }
                float front = x * curve.m_Length, rear = front;
                if (reads.m_Geometries.TryGetComponent(pr.m_Prefab, out ObjectGeometryData g))
                {
                    front += g.m_Bounds.max.z;
                    rear += g.m_Bounds.min.z;
                }
                if (rear >= cEnd) continue;   // past the conflict
                float gap = cStart - front;
                if (gap > ExitGiveWay.MaxScanMetres) continue;
                float speed = 0f;
                if (reads.m_Moving.TryGetComponent(e, out Moving m))
                {
                    speed = math.dot(m.m_Velocity, math.normalizesafe(MathUtils.Tangent(curve.m_Bezier, x)));
                    if (speed < 0f) speed = 0f;
                }
                int otherPriority = VehicleUtils.GetPriority(otherCar);
                if (signalApplies && FeedingSignal.Obeys(otherPriority, signalFlags))
                {
                    float stockSpeed = math.min(math.length(m.m_Velocity), data.m_MaxSpeed);
                    float toStop = FeedingSignal.RestOfLane(math.distance(MathUtils.Position(curve.m_Bezier, x), curve.m_Bezier.d), curve.m_Length, x)
                        - (front - x * curve.m_Length);
                    if (FeedingSignal.Skips(true, signalType, signalFlags, otherPriority, VehicleUtils.GetBrakingDistance(data, stockSpeed, 0f), toStop)) continue;
                }
                bool occupies = front > cStart && rear < cEnd;
                float braking = VehicleUtils.GetBrakingDistance(data, speed, ReservationTimeStep);
                if (ExitGiveWay.VehicleBlocks(gap, occupies, speed, braking, delta)) return true;
            }
            return false;
        }
    }
}
