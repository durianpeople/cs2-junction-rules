using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Vehicles;
using JunctionRules.Core;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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
            /// <summary>0.4.7: which cap stopped the scan (lane cap: errs to go; vehicle cap: busy, Core ExitPaths.CapBusy).</summary>
            public bool m_LaneCap;
            public bool m_VehicleCap;
        }

        /// <summary>0.4.7: the cap kinds of one scan, for the counters and the trace.</summary>
        private void NoteBudget(ExitBudget budget)
        {
            if (budget.m_LaneCap) m_ExitLaneCap = true;
            if (budget.m_VehicleCap) m_ExitVehicleCap = true;
        }

        /// <summary>The lookups one exit scan reads (the caller's read-only stock lookups, passed by ref).</summary>
        private struct ExitReads
        {
            public Entity m_Self;
            /// <summary>Review F3 (0.4.7): the leaving vehicle's own lane (a building road / drive lane): never a feeding lane of a conflict (a follower queued behind the held vehicle is not crossing traffic).</summary>
            public Entity m_OwnLane;
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
        /// 0.4.8 (JR-T-014, G7, owner rule 17 Nov: the sidewalk an exit crosses is a ZEBRA, Z directly followed by the road
        /// lanes C, so the vehicle stops BEFORE THE SIDEWALK and takes the WHOLE decision there). Three 0.4.7 holes closed:
        /// (1) the spot was the first ROAD lane (Core ExitGiveWay.ExitSpot now takes the sidewalk overlap when it comes
        /// first), so a held vehicle stood on the sidewalk; (2) no room test on the lane it joins: a right-turner (left-hand
        /// traffic) crossed the near lane into a stopped far-lane queue, and at a boxed exit the 0.4.3 band test (S2-28,
        /// role Leaving) only ran on the joined road lane, i.e. with the vehicle already across the near lane; now the room
        /// past the band (boxed) or past the join point (unboxed) is part of this decision (Core ExitGiveWay.Decide 7-arg);
        /// (3) the scan ran only when the exit hold was the binding limit ("hold &lt; currentMax"): a vehicle creeping under a
        /// lower limit (the car ahead, a curve) crossed the spot unjudged; now every update judges while it can still stop
        /// before the spot. Crossed lanes use the time to CLEAR them with the whole vehicle (Core ExitGiveWay.ClearMetres),
        /// and a road vehicle within Core ExitGiveWay.PresentMetres of a conflict blocks even when standing.
        /// </summary>
        public float ExitDrivewayHold(Entity lane, DynamicBuffer<LaneOverlap> overlaps, float curveX, float length, float origDistance, float margin, bool inverse,
            int priority, CarData car, float currentSpeed, float timeStep, float safeTimeStep, Bounds1 speedRange, float currentMax, float speedLimit, Entity self,
            ref ComponentLookup<Game.Net.CarLane> carLanes, ref ComponentLookup<LaneCondition> laneConditions, ref ComponentLookup<LaneSignal> signals,
            ref BufferLookup<LaneObject> laneObjects, ref ComponentLookup<Curve> curves, ref ComponentLookup<Moving> moving, ref ComponentLookup<Car> cars,
            ref ComponentLookup<Controller> controllers, ref ComponentLookup<PrefabRef> prefabRefs, ref ComponentLookup<CarData> prefabCars,
            ref ComponentLookup<ObjectGeometryData> geometries, ref BufferLookup<LaneOverlap> overlapData, ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes)
        {
            if (!m_ExitRule || !m_ExitLookups) return float.MaxValue;
            if (!carLanes.TryGetComponent(lane, out Game.Net.CarLane own) || !ExitGiveWay.IsDrivewayExitLane((uint)own.m_Flags)) return float.MaxValue;
            if (ExitGiveWay.IsEmergency(priority))
            {
                m_ExitEmergency = true;
                return float.MaxValue;
            }
            float start = StretchGeometry.ToDriving(curveX, inverse);
            // The first road lane and the first sidewalk (any pedestrian lane overlap) still ahead.
            float firstRoad = 2f, firstWalk = 2f;
            for (int i = 0; i < overlaps.Length; i++)
            {
                if (ExitConflict(overlaps[i], inverse, start, ref carLanes, out float s, out _)) { if (s < firstRoad) firstRoad = s; }
                else if (ExitSidewalk(overlaps[i], inverse, start, ref pedestrianLanes, out float w) && w < firstWalk) firstWalk = w;
            }
            if (firstRoad > 1f) return float.MaxValue;
            float spot = ExitGiveWay.ExitSpot(firstRoad, firstWalk);
            float wait = origDistance + length * (spot - start);
            // Past the spot (front on the sidewalk) or unable to stop before it: committed, the decision it took before stands.
            if (!ExitGiveWay.CanHold(wait, margin, VehicleUtils.GetBrakingDistance(car, currentSpeed, timeStep))) return float.MaxValue;
            float hold = MathUtils.Clamp(VehicleUtils.GetMaxBrakingSpeed(car, wait > 0f ? wait : 0f, safeTimeStep), speedRange);
            // Cheap for a queue on the building road: a vehicle further back than JudgeNearMetres that something else holds
            // harder does not scan yet; within it every update judges (it cannot pass the spot between two updates).
            if (!ExitGiveWay.MustJudge(hold, currentMax, wait)) return float.MaxValue;
            float drive = speedLimit;
            {
                Game.Net.CarLane limited = own;
                limited.m_SpeedLimit = speedLimit;
                drive = VehicleUtils.GetMaxDriveSpeed(car, limited);
                if (laneConditions.TryGetComponent(lane, out LaneCondition condition)) VehicleUtils.ModifyDriveSpeed(ref drive, condition);
            }
            bool hasJoin = PathLaneAfter(lane, out CarNavigationLane joinNav);
            ExitReads reads = default;
            reads.m_Self = self;
            reads.m_OwnLane = lane;
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
            float ownLength = m_VehicleLength > 0f ? m_VehicleLength : 4f;
            bool busy = false;
            int judged = 0;
            for (int i = 0; i < overlaps.Length && !busy && !budget.m_CapHit; i++)
            {
                LaneOverlap o = overlaps[i];
                if (!ExitConflict(o, inverse, start, ref carLanes, out float s, out float e)) continue;
                if (++judged > ExitGiveWay.MaxConflictLanes) break;
                if (!curves.TryGetComponent(o.m_Other, out Curve otherCurve)) continue;
                // The other lane's range of the overlap, in its own (driving) direction: road lanes run along their curve.
                float a = o.m_OtherStart * WholePathRule.ByteToOffset, b = o.m_OtherEnd * WholePathRule.ByteToOffset;
                float oS = math.min(a, b) * otherCurve.m_Length, oE = math.max(a, b) * otherCurve.m_Length;
                // The leaving vehicle's own time: to reach the lane it joins, to CLEAR a lane it crosses (whole length).
                float toConflict = origDistance + margin + length * (s - start);
                bool merge = hasJoin && o.m_Other == joinNav.m_Lane;
                float dist = merge ? toConflict : ExitGiveWay.ClearMetres(toConflict, length * (e - s), ownLength);
                float delta = ArrivalTime.Compute(ref motion, dist, currentSpeed, drive, 0f, timeStep);
                busy = ConflictBusy(o.m_Other, otherCurve, oS, oE, delta, ref reads, ref budget);
            }
            if (Counting && budget.m_CapHit) m_ExitCapHit = true;
            NoteBudget(budget);
            // Room: past the band on the lane it joins (boxed exit, 0.4.3 band set) or past the join point (every other exit).
            bool boxed = false, roomBox = true, roomJoin = true;
            if (!busy && hasJoin && curves.TryGetComponent(joinNav.m_Lane, out Curve joinCurve))
            {
                if (m_BoxRule)
                    roomBox = !ExitBandBlocks(joinNav.m_Lane, joinCurve.m_Length, joinNav.m_CurvePosition.x, self, safeTimeStep, out boxed, ref carLanes, ref laneObjects, ref curves,
                        ref moving, ref controllers, ref prefabRefs, ref geometries, ref prefabCars, ref overlapData, ref pedestrianLanes);
                if (!boxed || !m_BoxRule)
                    roomJoin = HasRoomPast(lane, 1f, inverse, length, -1f, Entity.Null, self, safeTimeStep, ref carLanes, ref laneObjects, ref curves,
                        ref moving, ref controllers, ref prefabRefs, ref geometries, ref prefabCars, ref overlapData, ref pedestrianLanes);
            }
            ExitGiveWay.Decision d = ExitGiveWay.Decide(true, priority, !busy, m_BoxRule, boxed, roomBox, roomJoin);
            m_DriveJudged = true;
            m_DriveDecision = d;
            m_DriveBoxed = boxed;
            m_DriveLane = lane;
            if (d == ExitGiveWay.Decision.Go)
            {
                if (Counting) m_ExitGo = true;
                return float.MaxValue;
            }
            Min(ref m_HasDriveHold, ref m_DriveHoldSpeed, hold);
            m_DriveRoomHold = d == ExitGiveWay.Decision.WaitRoom;
            if (Counting)
            {
                if (d == ExitGiveWay.Decision.WaitGap) Min(ref m_HasExitHold, ref m_ExitHoldSpeed, hold);
                m_ExitAcross = ExitGiveWay.TurnsAcross((own.m_Flags & Game.Net.CarLaneFlags.TurnLeft) != 0, (own.m_Flags & Game.Net.CarLaneFlags.TurnRight) != 0, m_ExitLeftHand);
            }
            return hold;
        }

        /// <summary>0.4.8 (G7): an overlap of a drive's leaving lane with a pedestrian lane (the sidewalk it crosses, marked or not), not yet passed.</summary>
        private static bool ExitSidewalk(LaneOverlap o, bool inverse, float start, ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes, out float s)
        {
            s = 2f;
            if (((int)o.m_Flags & WholePathRule.Water) != 0 || !pedestrianLanes.HasComponent(o.m_Other)) return false;
            float a = o.m_ThisStart * WholePathRule.ByteToOffset, b = o.m_ThisEnd * WholePathRule.ByteToOffset;
            s = inverse ? 1f - b : a;
            float e = inverse ? 1f - a : b;
            return e > start;
        }

        /// <summary>0.4.8 (G7): the navigation lane after <paramref name="lane"/> on the car's path (S2-26 SetPath): the road lane a drive lane joins.</summary>
        private bool PathLaneAfter(Entity lane, out CarNavigationLane next)
        {
            next = default;
            if (!m_HasPath) return false;
            int at = -1;
            for (int i = 0; i < m_Path.Length; i++)
            {
                if (m_Path[i].m_Lane == lane) { at = i + 1; break; }
            }
            if (at < 0 && (lane == m_CurrentLane || lane == m_CurrentLane2)) at = 0;
            if (at < 0 || at >= m_Path.Length) return false;
            next = m_Path[at];
            return true;
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
            if (!ExitJoinLane(connection, priority, next, true, ref carLanes, ref curves, out Entity road, out Curve roadCurve, out Owner roadOwner)) return float.MaxValue;
            if (!curves.TryGetComponent(connection, out Curve connCurve)) return float.MaxValue;
            NoteFrom(connection, (uint)Game.Vehicles.CarLaneFlags.Connection, roadOwner.m_Owner);
            // 0.4.4: the vehicle appears on the join lane with its body behind the join point and no arrival time of its own.
            bool wait = ExitJoinWaits(self, connection, priority, next.m_CurvePosition.x, road, roadCurve, roadOwner, connCurve.m_Bezier.a, 0f, safeTimeStep,
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
        /// 0.4.6: the vehicle is judged from Core <see cref="ExitGiveWay.AreaScanMetres"/> before the join point on EVERY update
        /// (0.4.5 skipped a vehicle whose braking speed was not yet below its limit, and the stock hand-over to the road lane
        /// happens up to ~20 m before the join point, so a vehicle was handed over unjudged); the distance is measured from the
        /// vehicle's FRONT (<paramref name="frontOffset"/> = geometry max z of the tractor), not half the whole train (a
        /// tractor-trailer's trailers made it look committed 8 m too early, which left it to the game).
        /// </summary>
        public float ExitAreaHold(Entity self, int priority, Entity areaLane, CarNavigationLane next, float3 position, float frontOffset, CarData car, float currentSpeed, float timeStep,
            float safeTimeStep, Bounds1 speedRange, float currentMax,
            ref ComponentLookup<Game.Net.CarLane> carLanes, ref ComponentLookup<LaneSignal> signals, ref BufferLookup<LaneObject> laneObjects,
            ref ComponentLookup<Curve> curves, ref ComponentLookup<Moving> moving, ref ComponentLookup<Car> cars, ref ComponentLookup<Controller> controllers,
            ref ComponentLookup<PrefabRef> prefabRefs, ref ComponentLookup<CarData> prefabCars, ref ComponentLookup<ObjectGeometryData> geometries,
            ref BufferLookup<LaneOverlap> overlapData, ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes)
        {
            m_ExitWaiting = false;
            if (!m_ExitRule || !m_ExitLookups) return float.MaxValue;
            if (!ExitJoinLane(areaLane, priority, next, false, ref carLanes, ref curves, out Entity road, out Curve roadCurve, out Owner roadOwner)) return float.MaxValue;
            NoteFrom(areaLane, (uint)Game.Vehicles.CarLaneFlags.Area, roadOwner.m_Owner);
            return ExitApproachHold(self, areaLane, true, priority, next, road, roadCurve, roadOwner, position, frontOffset, car, currentSpeed, timeStep, safeTimeStep, speedRange,
                ref carLanes, ref signals, ref laneObjects, ref curves, ref moving, ref cars, ref controllers, ref prefabRefs, ref prefabCars, ref geometries,
                ref overlapData, ref pedestrianLanes);
        }

        /// <summary>
        /// 0.4.7 (S2-36, navigation job, plain lane branch): a vehicle on a car lane of a building's own road (sub-net; not a
        /// drive's SideConnection lane, those are S2-29) whose next navigation lane is a public road lane, in practice a lane
        /// of the junction where the building's road meets the public road (Game.Net/LaneSystem.cs 5161 / 7033: no Yield or
        /// SideConnection flag there; "a building exit is a yielding arm"). The same approach hold as a lot exit.
        /// Cheap for every other vehicle: the next lane's flags, then the owner chain of its current lane (road lanes stop at
        /// the edge, which has no owner).
        /// </summary>
        public float ExitLaneHold(Entity self, int priority, Entity lane, CarNavigationLane next, float3 position, float frontOffset, CarData car, float currentSpeed, float timeStep,
            float safeTimeStep, Bounds1 speedRange,
            ref ComponentLookup<Game.Net.CarLane> carLanes, ref ComponentLookup<LaneSignal> signals, ref BufferLookup<LaneObject> laneObjects,
            ref ComponentLookup<Curve> curves, ref ComponentLookup<Moving> moving, ref ComponentLookup<Car> cars, ref ComponentLookup<Controller> controllers,
            ref ComponentLookup<PrefabRef> prefabRefs, ref ComponentLookup<CarData> prefabCars, ref ComponentLookup<ObjectGeometryData> geometries,
            ref BufferLookup<LaneOverlap> overlapData, ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes)
        {
            m_ExitWaiting = false;
            if (!m_ExitRule || !m_ExitLookups) return float.MaxValue;
            if ((next.m_Flags & NotRoadLane) != 0 || !LeavesBuilding(lane)) return float.MaxValue;
            if (!carLanes.TryGetComponent(lane, out Game.Net.CarLane own) || (own.m_Flags & Game.Net.CarLaneFlags.SideConnection) != 0) return float.MaxValue;
            if (!ExitJoinLane(lane, priority, next, true, ref carLanes, ref curves, out Entity road, out Curve roadCurve, out Owner roadOwner)) return float.MaxValue;
            // A signalled arm follows its own signal (the conflicting flows are held by theirs): no exit hold there.
            if (carLanes.TryGetComponent(road, out Game.Net.CarLane joined) && (joined.m_Flags & (Game.Net.CarLaneFlags.TrafficLights | Game.Net.CarLaneFlags.LevelCrossing)) != 0)
            {
                m_ExitDecided = true;   // decided by the signal (not an unjudged hand-over)
                m_ExitDecision = ExitGiveWay.Decision.Go;
                m_ExitSignalArm = true;   // review F6: the trace says "go (signalled arm)", not "go (gap clear)"
                return float.MaxValue;
            }
            NoteFrom(lane, 0u, roadOwner.m_Owner);
            return ExitApproachHold(self, lane, false, priority, next, road, roadCurve, roadOwner, position, frontOffset, car, currentSpeed, timeStep, safeTimeStep, speedRange,
                ref carLanes, ref signals, ref laneObjects, ref curves, ref moving, ref cars, ref controllers, ref prefabRefs, ref prefabCars, ref geometries,
                ref overlapData, ref pedestrianLanes);
        }

        /// <summary>
        /// The approach hold shared by a lot exit (S2-32) and a building road (S2-36): judged from Core
        /// ExitGiveWay.AreaScanMetres before the join point on every update, distance from the vehicle's front, brakes to stop
        /// one margin short of the join point and sets <see cref="ExitWaiting"/> (S2-33 keeps it off the road lane).
        /// </summary>
        private float ExitApproachHold(Entity self, Entity from, bool lot, int priority, CarNavigationLane next, Entity road, Curve roadCurve, Owner roadOwner, float3 position, float frontOffset,
            CarData car, float currentSpeed, float timeStep, float safeTimeStep, Bounds1 speedRange,
            ref ComponentLookup<Game.Net.CarLane> carLanes, ref ComponentLookup<LaneSignal> signals, ref BufferLookup<LaneObject> laneObjects,
            ref ComponentLookup<Curve> curves, ref ComponentLookup<Moving> moving, ref ComponentLookup<Car> cars, ref ComponentLookup<Controller> controllers,
            ref ComponentLookup<PrefabRef> prefabRefs, ref ComponentLookup<CarData> prefabCars, ref ComponentLookup<ObjectGeometryData> geometries,
            ref BufferLookup<LaneOverlap> overlapData, ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes)
        {
            float t = next.m_CurvePosition.x;
            float3 join = MathUtils.Position(roadCurve.m_Bezier, t);
            float front = frontOffset > 0f ? frontOffset : 0f;
            // Metres its front may still travel before the join point (the pivot sits a front offset behind the front).
            float toJoin = math.distance(position.xz, join.xz) - front;
            // 0.4.8 (G7): a lot exit stops before the SIDEWALK (owner rule 17 Nov: the sidewalk is a zebra), not one margin
            // short of the join point (which for a far-lane join is across the near lane): the metres before the join point
            // where its straight way meets the outer line of the building-side sidewalk.
            float back = lot ? SidewalkBack(roadOwner.m_Owner, roadCurve, t, join, position, ref curves, ref pedestrianLanes) : 0f;
            float scan = ExitGiveWay.AreaScanMetres(VehicleUtils.GetBrakingDistance(car, math.min(car.m_MaxSpeed, AreaLaneSpeed), timeStep) + 1f, front) + back;
            if (toJoin > scan) return float.MaxValue;
            float wait = toJoin - back - ExitAreaMargin;
            if (!ExitGiveWay.CanHoldArea(wait, VehicleUtils.GetBrakingDistance(car, currentSpeed, timeStep))) return float.MaxValue;
            float hold = MathUtils.Clamp(VehicleUtils.GetMaxBrakingSpeed(car, wait > 0f ? wait : 0f, safeTimeStep), speedRange);
            float arrival = 0f;
            if (carLanes.TryGetComponent(road, out Game.Net.CarLane roadLane))
            {
                float drive = VehicleUtils.GetMaxDriveSpeed(car, roadLane);
                GameMotion motion = default;
                motion.m_Car = car;
                arrival = ArrivalTime.Compute(ref motion, toJoin > 0f ? toJoin : 0f, currentSpeed, drive, 0f, timeStep);
            }
            bool busy = ExitJoinWaits(self, from, priority, t, road, roadCurve, roadOwner, position, arrival, safeTimeStep,
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

        /// <summary>
        /// 0.4.7 (S2-35, navigation job, the TransformTarget / ParkingSpace branch): the vehicle's current navigation lane
        /// is a building's spawn location OBJECT (TransformTarget: the 0.4.6 'other', see Core/ExitPaths.cs) or a parking
        /// space (a building's parking lane or garage, or a kerbside parking lane of the road), and its next navigation lane
        /// is a public road lane. The stock branch never iterates the road lane and the hand-over loop puts it on the road
        /// lane as soon as it reaches the spawn point / leaves the space. Same gap and box decision as every exit; the
        /// vehicle stands at its spawn point (unspawned: CheckUnspawned keeps it hidden on a TransformTarget lane) or in its
        /// parking space while busy, and <see cref="ExitWaiting"/> keeps it off the road lane (S2-33). target = the stock
        /// navigation target (the spawn location's position); a parking space holds where the vehicle stands.
        /// Kerbside parking: owner rule "a building exit is a yielding arm" and the real-life "give way when moving off".
        /// </summary>
        public float ExitTargetHold(Entity self, int priority, Entity from, Game.Vehicles.CarLaneFlags fromFlags, CarNavigationLane next, float3 position, float3 target,
            CarData car, float safeTimeStep, Bounds1 speedRange,
            ref ComponentLookup<Game.Net.CarLane> carLanes, ref ComponentLookup<LaneSignal> signals, ref BufferLookup<LaneObject> laneObjects,
            ref ComponentLookup<Curve> curves, ref ComponentLookup<Moving> moving, ref ComponentLookup<Car> cars, ref ComponentLookup<Controller> controllers,
            ref ComponentLookup<PrefabRef> prefabRefs, ref ComponentLookup<CarData> prefabCars, ref ComponentLookup<ObjectGeometryData> geometries,
            ref BufferLookup<LaneOverlap> overlapData, ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes)
        {
            m_ExitWaiting = false;
            if (!m_ExitRule || !m_ExitLookups) return float.MaxValue;
            bool parking = (fromFlags & Game.Vehicles.CarLaneFlags.ParkingSpace) != 0;
            // A spawn point must belong to a building (other transform targets stay stock); a parking space needs no owner (kerbside parking gives way too).
            if (!ExitJoinLane(from, priority, next, !parking, ref carLanes, ref curves, out Entity road, out Curve roadCurve, out Owner roadOwner)) return float.MaxValue;
            NoteFrom(from, (uint)fromFlags, roadOwner.m_Owner);
            float3 side = parking ? position : target;
            bool busy = ExitJoinWaits(self, from, priority, next.m_CurvePosition.x, road, roadCurve, roadOwner, side, 0f, safeTimeStep,
                ref carLanes, ref signals, ref laneObjects, ref curves, ref moving, ref cars, ref controllers, ref prefabRefs, ref prefabCars, ref geometries,
                ref overlapData, ref pedestrianLanes, out ExitGiveWay.Decision d);
            if (!busy)
            {
                if (Counting) m_ExitGo = true;
                return float.MaxValue;
            }
            m_ExitWaiting = true;
            float wait = parking ? 0f : math.distance(position.xz, target.xz);
            float hold = MathUtils.Clamp(VehicleUtils.GetMaxBrakingSpeed(car, wait, safeTimeStep), speedRange);
            if (Counting)
            {
                if (d == ExitGiveWay.Decision.WaitGap) { Min(ref m_HasExitHold, ref m_ExitHoldSpeed, hold); m_ExitSpawn = true; }
                else Min(ref m_HasExitBox, ref m_ExitBoxSpeed, hold);
            }
            return hold;
        }

        /// <summary>
        /// 0.4.8 (G7): metres before the join point at which a lot exit's straight way (vehicle -> join point) reaches the
        /// outer line of the sidewalk on the building side of the road edge: the outermost pedestrian lane of the edge on that
        /// side, plus Core ExitGiveWay.SidewalkHalfWidth, measured across the road and divided by the way's crossing angle
        /// (Core ExitGiveWay.SidewalkBack). 0 when the road is no edge or has no sidewalk on that side.
        /// </summary>
        private float SidewalkBack(Entity edge, Curve roadCurve, float t, float3 join, float3 position, ref ComponentLookup<Curve> curves,
            ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes)
        {
            if (!m_ExitEdgeData.HasComponent(edge) || !m_SubLanes.TryGetBuffer(edge, out DynamicBuffer<Game.Net.SubLane> lanes)) return 0f;
            float3 tangent = MathUtils.Tangent(roadCurve.m_Bezier, t);
            float2 normal = math.normalizesafe(new float2(-tangent.z, tangent.x));
            float side = math.dot(position.xz - join.xz, normal) >= 0f ? 1f : -1f;
            float outer = -1f;
            for (int i = 0; i < lanes.Length; i++)
            {
                Entity p = lanes[i].m_SubLane;
                if (!pedestrianLanes.HasComponent(p) || !curves.TryGetComponent(p, out Curve pc)) continue;
                MathUtils.Distance(pc.m_Bezier.xz, join.xz, out float tp);
                float lateral = math.dot(MathUtils.Position(pc.m_Bezier, tp).xz - join.xz, normal) * side;
                if (lateral > outer) outer = lateral;
            }
            if (!(outer > 0f)) return 0f;
            float2 way = math.normalizesafe(join.xz - position.xz);
            return ExitGiveWay.SidewalkBack(outer, math.abs(math.dot(way, normal)));
        }

        /// <summary>Navigation lane flags of a lane that is not a road lane (the joined lane must have none).</summary>
        private const Game.Vehicles.CarLaneFlags NotRoadLane = Game.Vehicles.CarLaneFlags.Connection | Game.Vehicles.CarLaneFlags.Area | Game.Vehicles.CarLaneFlags.TransformTarget | Game.Vehicles.CarLaneFlags.ParkingSpace;

        /// <summary>0.4.7: what the decision is for (trace).</summary>
        private void NoteFrom(Entity from, uint flags, Entity roadOwner)
        {
            m_ExitFrom = from;
            m_ExitFromFlags = flags;
            m_ExitRoadOwner = roadOwner;
        }

        /// <summary>The speed (m/s) the stock navigation takes as the area lane's target speed (CarNavigationSystem num3 = 11.111): its hand-over distance uses the braking distance from it.</summary>
        public const float AreaLaneSpeed = 11.111112f;

        /// <summary>
        /// S2-34 (0.4.6 probe; 0.4.7 classification, Core/ExitPaths.cs): a vehicle hands over from its current lane to its next
        /// navigation lane and the next lane is a public road lane. Counts the path it came from (lot, gate, spawn point,
        /// building parking, kerbside parking, building road, drive, other) and a judged path handed over with no decision in
        /// this update ("not judged", expected 0); with the exit trace on, one trace line per hand-over. Road to road
        /// hand-overs are not counted. Counting or tracing only (debug).
        /// </summary>
        public void ExitHandOver(NativeArray<long> counters, NativeArray<long> eventTable, Entity self, uint frame, Entity from, Game.Vehicles.CarLaneFlags fromFlags, CarNavigationLane next,
            ref ComponentLookup<Game.Net.CarLane> carLanes, ref ComponentLookup<Curve> curves)
        {
            if (!m_ExitRule || !m_ExitLookups) return;
            bool count = Counting && counters.IsCreated;
            if (!count && !m_TraceOn) return;
            // 0.4.8 (G7): entering a driveway's leaving lane = crossing the sidewalk onto the road. It must carry a Go from a full
            // judgement (S2-29) taken while it could still stop before the sidewalk: this update's, or a recent one (NoteDrive).
            if (carLanes.TryGetComponent(next.m_Lane, out Game.Net.CarLane nextLane) && ExitGiveWay.IsDrivewayExitLane((uint)nextLane.m_Flags))
            {
                DriveEntry(counters, eventTable, self, frame, from, next.m_Lane, count);
                return;
            }
            if (!ExitJoinLane(from, 0, next, false, ref carLanes, ref curves, out _, out _, out Owner roadOwner)) return;
            bool isCar = carLanes.TryGetComponent(from, out Game.Net.CarLane fromLane);
            bool side = isCar && (fromLane.m_Flags & Game.Net.CarLaneFlags.SideConnection) != 0;
            Entity building = ExitBuilding(from);
            ExitPaths.Path path = ExitPaths.Classify((uint)fromFlags, building != Entity.Null, isCar, side);
            if (path == ExitPaths.Path.None) return;
            bool unjudged = ExitPaths.JudgedAtHandOver(path) && !m_ExitDecided && !m_ExitEmergency;
            // 0.4.8 (G7): at the hand-over update the front is already past the stop point (the pivot reaches the lane end), so
            // no decision is taken in THIS update; 0.4.7 counted every building-road hand-over as "not judged" for that reason
            // (the 07:04-07:23 17 Nov windows: not judged == building road in almost every window). A Go recorded in a recent
            // update (NoteExit, while it could still stop) is its judgement.
            if (unjudged && FreshGo(eventTable, self, frame)) unjudged = false;
            if (count)
            {
                Add(counters, ExitPaths.Counter(path), 1);
                if (unjudged) Add(counters, Stage2Counters.ExitJoinUnjudged, 1);
            }
            if (m_TraceOn)
            {
                ExitPaths.LaneKind kind = ExitPaths.Kind((uint)fromFlags, isCar, side);
                TraceWrite(m_TraceRing, self, building, ExitTrace.Pack(ExitTrace.Event.HandOver, path, kind, m_ExitDecision, TraceReasons(), m_ExitBoxed), frame, roadOwner.m_Owner);
            }
        }

        /// <summary>0.4.8 (G7): a vehicle enters a drive's leaving lane: counted, and "without a full judgement" when it has no fresh Go.</summary>
        private unsafe void DriveEntry(NativeArray<long> counters, NativeArray<long> eventTable, Entity self, uint frame, Entity from, Entity drive, bool count)
        {
            if (m_ExitEmergency) return;   // on-duty emergency: stock, never judged (counted as emergency passthroughs)
            bool judged = (m_DriveJudged && m_DriveLane == drive && m_DriveDecision == ExitGiveWay.Decision.Go) || FreshGo(eventTable, self, frame);
            if (count)
            {
                Add(counters, Stage2Counters.ExitDriveEntries, 1);
                if (!judged) Add(counters, Stage2Counters.ExitDriveUnjudged, 1);
            }
            if (m_TraceOn)
            {
                Entity building = ExitBuilding(from);
                int reasons = judged ? ExitTrace.ReasonDecided : 0;
                TraceWrite(m_TraceRing, self, building, ExitTrace.Pack(ExitTrace.Event.EnterDrive, ExitPaths.Path.Drive, ExitPaths.LaneKind.DriveLane, ExitGiveWay.Decision.Go, reasons, m_DriveBoxed), frame, OwnerOf(drive));
            }
        }

        /// <summary>0.4.8 (G7): a Go judgement of this vehicle's exit was recorded (NoteExit) within Core ExitGiveWay.DriveGoWindow frames.</summary>
        private static unsafe bool FreshGo(NativeArray<long> eventTable, Entity self, uint frame)
        {
            if (!eventTable.IsCreated) return false;
            TableSlots slots = default;
            slots.m_P = (long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(eventTable);
            slots.m_N = eventTable.Length;
            bool found = EventLookup.Last(ref slots, ExitGiveWay.EventKey(self.Index, ExitGiveWay.DriveGoKind), out uint last);
            return EventLookup.Fresh(found, last, frame, ExitGiveWay.DriveGoWindow);
        }

        private Entity OwnerOf(Entity lane) => m_OwnerData.TryGetComponent(lane, out Owner o) ? o.m_Owner : Entity.Null;

        /// <summary>
        /// 0.4.8 (G7, S2-22, counting or tracing): after this update's iterator ran, record a full Go judgement of a driveway exit
        /// (S2-29) in the event table (read back at the drive entry, <see cref="DriveEntry"/>, and at every exit hand-over), and the exit trace's "waits" line
        /// for a driveway exit held before the sidewalk (once per wait episode).
        /// </summary>
        public void NoteExit(NativeArray<long> eventTable, Entity self, uint frame, Entity currentLane)
        {
            if (!m_ExitRule) return;
            // Any exit path's Go (S2-29 drive, S2-30/32/35/36 through ExitJoinWaits or a signalled arm): read back at its hand-over.
            if (m_ExitDecided && m_ExitDecision == ExitGiveWay.Decision.Go && !m_DriveJudged)
            {
                ExitEvent(eventTable, self, frame, ExitGiveWay.DriveGoKind);
                return;
            }
            if (!m_DriveJudged) return;
            if (m_DriveDecision == ExitGiveWay.Decision.Go)
            {
                ExitEvent(eventTable, self, frame, ExitGiveWay.DriveGoKind);
                return;
            }
            if (m_TraceOn && ExitEvent(eventTable, self, frame, ExitGiveWay.DriveTraceWaitKind))
            {
                Entity building = ExitBuilding(currentLane);
                TraceWrite(m_TraceRing, self, building, ExitTrace.Pack(ExitTrace.Event.Wait, ExitPaths.Path.Drive, ExitPaths.LaneKind.DriveLane, m_DriveDecision,
                    ExitTrace.ReasonDecided | TraceReasons(), m_DriveBoxed), frame, OwnerOf(m_DriveLane));
            }
        }

        /// <summary>
        /// 0.4.7 review (G1 F1, S2-22, counting only): a vehicle that is unspawned on a public road car lane with no Connection /
        /// ResetSpeed flag appeared directly in that lane: its path started on the road lane itself, not on a building's lane,
        /// spawn object or parking space (Game.Pathfind/PathfindTargetSeeker.cs 610-613 / 660-668: a Cargo spawn location
        /// adds its m_ConnectedLane1/2 as the path start when the path parameters lack CargoLoading; in the 2026-11-13
        /// decompile only VehicleWorkType.Move work vehicles, whose parameters drop Road: WorkCarAISystem.cs 239-243,
        /// AreaLotSimulationSystem.cs 504-508; also the front-connection fallback AddEdgeTargets(Building.m_RoadEdge)). It is
        /// already in the lane (CheckUnspawned, VehicleUtils.cs 1806-1821, shows it at the next AI step), so it is not held;
        /// counted once per vehicle and episode so the probe shows whether real road traffic takes this way. A normal
        /// gate exit carries ResetSpeed on its first road lane (CarNavigationSystem.cs 1877-1886) and is not counted.
        /// </summary>
        public void CountRoadStart(NativeArray<long> counters, NativeArray<long> eventTable, Entity self, uint frame, Entity lane, Game.Vehicles.CarLaneFlags flags, bool unspawned,
            ref ComponentLookup<Game.Net.CarLane> carLanes)
        {
            if (!unspawned || !m_ExitRule || !counters.IsCreated) return;
            if ((flags & (NotRoadLane | Game.Vehicles.CarLaneFlags.ResetSpeed)) != 0) return;
            if (!carLanes.TryGetComponent(lane, out Game.Net.CarLane roadLane) || (roadLane.m_Flags & Game.Net.CarLaneFlags.SideConnection) != 0) return;
            if (!m_OwnerData.TryGetComponent(lane, out Owner owner) || m_OwnerData.HasComponent(owner.m_Owner) || !m_SubLanes.HasBuffer(owner.m_Owner)) return;
            if (!ExitEvent(eventTable, self, frame, ExitGiveWay.RoadStartKind)) return;
            Add(counters, Stage2Counters.ExitRoadStart, 1);
        }

        /// <summary>
        /// 0.4.7: the exit trace's "starts waiting" line, once per wait episode of the vehicle (Core SafetyStopEvents on the
        /// shared event table, kind ExitGiveWay.TraceWaitKind). Called by the navigation blocks after a hold.
        /// </summary>
        public void TraceWait(NativeArray<long> eventTable, Entity self, uint frame)
        {
            if (!m_TraceOn || !m_ExitWaiting || !m_ExitDecided) return;
            if (!ExitEvent(eventTable, self, frame, ExitGiveWay.TraceWaitKind)) return;
            Entity building = ExitBuilding(m_ExitFrom);
            bool isCar = m_ExitFrom != Entity.Null && m_LaneData.HasComponent(m_ExitFrom) && (m_ExitFromFlags & (uint)NotRoadLane) == 0;
            ExitPaths.Path path = ExitPaths.Classify(m_ExitFromFlags, building != Entity.Null, isCar, false);
            ExitPaths.LaneKind kind = ExitPaths.Kind(m_ExitFromFlags, isCar, false);
            TraceWrite(m_TraceRing, self, building, ExitTrace.Pack(ExitTrace.Event.Wait, path, kind, m_ExitDecision, TraceReasons(), m_ExitBoxed), frame, m_ExitRoadOwner);
        }

        /// <summary>0.4.7 (S2-26): the exit trace is on; ring = Stage2/JRStage2Bridge.cs TraceRing.</summary>
        public void SetTrace(NativeArray<long> ring)
        {
            if (!ring.IsCreated || ring.Length < ExitTrace.RingLength) return;
            m_TraceRing = ring;
            m_TraceOn = true;
        }

        private int TraceReasons() =>
            (m_ExitDecided ? ExitTrace.ReasonDecided : 0) | (m_ExitLaneCap ? ExitTrace.ReasonLaneCap : 0) | (m_ExitVehicleCap ? ExitTrace.ReasonVehicleCap : 0)
            | (m_ExitEmergency ? ExitTrace.ReasonEmergency : 0) | (m_ExitNodeJoin ? ExitTrace.ReasonNodeJoin : 0) | (m_ExitSignalArm ? ExitTrace.ReasonSignal : 0);

        /// <summary>One trace record (Core ExitTrace layout): the stamp is cleared first and written last, so the main-thread reader never takes a half-written record.</summary>
        private static unsafe void TraceWrite(NativeArray<long> ring, Entity self, Entity building, long facts, uint frame, Entity roadOwner)
        {
            if (!ring.IsCreated || ring.Length < ExitTrace.RingLength) return;
            long* p = (long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(ring);
            long seq = Interlocked.Increment(ref p[0]);
            int b = ExitTrace.SlotBase(seq);
            Interlocked.Exchange(ref p[b], 0L);
            p[b + 1] = ExitTrace.PackEntity(self.Index, self.Version);
            p[b + 2] = building == Entity.Null ? 0L : ExitTrace.PackEntity(building.Index, building.Version);
            p[b + 3] = facts;
            p[b + 4] = (long)frame | ((long)roadOwner.Index << 32);
            Interlocked.Exchange(ref p[b], seq);
        }

        /// <summary>Metres short of the join point an area exit holds its vehicle's front (the stop margin of a waiting vehicle).</summary>
        public const float ExitAreaMargin = 1f;

        /// <summary>
        /// The lane a vehicle leaving a building (from its Connection lane or its area lane) joins: the next navigation lane,
        /// a public road car lane (a car lane of an edge or junction node nobody owns, not a drive's side connection). False = stock (not a
        /// building exit, an on-duty emergency vehicle, a lane against its curve, no geometry).
        /// </summary>
        private bool ExitJoinLane(Entity from, int priority, CarNavigationLane next, bool requireBuilding, ref ComponentLookup<Game.Net.CarLane> carLanes, ref ComponentLookup<Curve> curves,
            out Entity road, out Curve roadCurve, out Owner roadOwner)
        {
            road = next.m_Lane;
            roadCurve = default;
            roadOwner = default;
            if ((next.m_Flags & NotRoadLane) != 0) return false;
            // A public road lane: a car lane of a road edge that no building / other object owns.
            if (!carLanes.TryGetComponent(road, out Game.Net.CarLane roadLane) || (roadLane.m_Flags & Game.Net.CarLaneFlags.SideConnection) != 0) return false;
            // 0.4.6: an edge's lane or a junction node's lane (a lot beside a junction joins a node lane); the owner is ownerless (public network).
            if (!m_OwnerData.TryGetComponent(road, out roadOwner) || m_OwnerData.HasComponent(roadOwner.m_Owner) || !m_SubLanes.HasBuffer(roadOwner.m_Owner))
                return false;
            // A Connection lane must belong to a building (outside connections stay stock); an AREA lane (a lot, a surface) needs no owner chain: any area leaving onto a public road gives way.
            if (requireBuilding && !LeavesBuilding(from)) return false;
            if (ExitGiveWay.IsEmergency(priority))
            {
                m_ExitEmergency = true;   // 0.4.7: always (the trace and the "not judged" counter read it)
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
        private bool ExitJoinWaits(Entity self, Entity ownLane, int priority, float t, Entity road, Curve roadCurve, Owner roadOwner, float3 buildingSide, float delta, float safeTimeStep,
            ref ComponentLookup<Game.Net.CarLane> carLanes, ref ComponentLookup<LaneSignal> signals, ref BufferLookup<LaneObject> laneObjects,
            ref ComponentLookup<Curve> curves, ref ComponentLookup<Moving> moving, ref ComponentLookup<Car> cars, ref ComponentLookup<Controller> controllers,
            ref ComponentLookup<PrefabRef> prefabRefs, ref ComponentLookup<CarData> prefabCars, ref ComponentLookup<ObjectGeometryData> geometries,
            ref BufferLookup<LaneOverlap> overlapData, ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes, out ExitGiveWay.Decision d)
        {
            ExitReads reads = default;
            reads.m_Self = self;
            reads.m_OwnLane = ownLane;
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
            Entity joinOwner = roadOwner.m_Owner;
            bool nodeJoin = !m_ExitEdgeData.HasComponent(joinOwner);
            m_ExitNodeJoin = nodeJoin;
            m_ExitSignalArm = false;   // review F2: no stale "signalled arm" reason from an earlier decision of this vehicle
            if (!busy && nodeJoin)
            {
                // 0.4.7: a junction lane (a lot / building road meeting the public road at a node). The lateral rule below is
                // for the parallel lanes of one edge; inside a junction the conflicts are the joined lane's own LaneOverlaps
                // with other car lanes still ahead of the join point (the game's crossing / merging points, as S2-29), not
                // lanes that start with it (MergeStart: the same arm).
                if (overlapData.TryGetBuffer(road, out DynamicBuffer<LaneOverlap> overlaps))
                {
                    int judged = 0;
                    for (int i = 0; i < overlaps.Length && !busy && !budget.m_CapHit; i++)
                    {
                        LaneOverlap o = overlaps[i];
                        if (((int)o.m_Flags & (WholePathRule.Water | WholePathRule.MergeStart)) != 0) continue;
                        if (!carLanes.TryGetComponent(o.m_Other, out Game.Net.CarLane ol) || (ol.m_Flags & Game.Net.CarLaneFlags.SideConnection) != 0) continue;
                        float thisEnd = math.max((int)o.m_ThisStart, (int)o.m_ThisEnd) * WholePathRule.ByteToOffset;
                        if (thisEnd <= t) continue;   // behind the join point
                        if (!curves.TryGetComponent(o.m_Other, out Curve oc)) continue;
                        if (++judged > ExitGiveWay.MaxConflictLanes) break;
                        float a = o.m_OtherStart * WholePathRule.ByteToOffset, b = o.m_OtherEnd * WholePathRule.ByteToOffset;
                        busy = ConflictBusy(o.m_Other, oc, math.min(a, b) * oc.m_Length, math.max(a, b) * oc.m_Length, delta, ref reads, ref budget);
                    }
                }
            }
            else if (!busy)
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
            NoteBudget(budget);
            // At a boxed exit also the 0.4.3 band rule: room past the band on the join lane (stock never iterates the road
            // lane from a Connection / area lane, so the 0.4.3 S2-28 test cannot see this vehicle).
            bool boxed = false, room = true, roomJoin = true;
            if (!busy && m_BoxRule) room = !ExitBandBlocks(road, roadCurve.m_Length, t, self, safeTimeStep, out boxed, ref carLanes, ref laneObjects, ref curves,
                ref moving, ref controllers, ref prefabRefs, ref geometries, ref prefabCars, ref overlapData, ref pedestrianLanes);
            // 0.4.8 (G7): every exit, boxed or not, needs room for its whole length on the lane it joins (past the join point;
            // a boxed one past the band, above). Without it the vehicle would stop across the lanes it crossed.
            if (!busy && (!boxed || !m_BoxRule))
                roomJoin = HasRoomPast(road, t, false, roadCurve.m_Length, -1f, Entity.Null, self, safeTimeStep, ref carLanes, ref laneObjects, ref curves,
                    ref moving, ref controllers, ref prefabRefs, ref geometries, ref prefabCars, ref overlapData, ref pedestrianLanes);
            d = ExitGiveWay.Decide(true, priority, !busy, m_BoxRule, boxed, room, roomJoin);
            if (d == ExitGiveWay.Decision.WaitRoom) m_DriveRoomHold = true;
            m_ExitDecided = true;
            m_ExitDecision = d;
            m_ExitBoxed = boxed;
            return d != ExitGiveWay.Decision.Go;
        }

        /// <summary>Half the length of road lane a crossing at a connection exit takes (m): about a car's width plus a margin.</summary>
        public const float ExitCrossHalfWidth = 2f;

        /// <summary>S2-30 / S2-32 / S2-35 / S2-36 -> S2-31 / S2-33 (same vehicle, same update): the vehicle is held at its exit and must not join the road lane.</summary>
        public bool ExitWaiting => m_ExitWaiting;

        /// <summary>0.4.7: the building a lane / spawn object belongs to (owner chain, as <see cref="LeavesBuilding"/>), else Entity.Null.</summary>
        private Entity ExitBuilding(Entity lane)
        {
            Entity e = lane;
            for (int i = 0; i < 4; i++)
            {
                if (!m_OwnerData.TryGetComponent(e, out Owner owner) || owner.m_Owner == Entity.Null) return Entity.Null;
                e = owner.m_Owner;
                if (m_ExitBuildingData.HasComponent(e)) return e;
            }
            return Entity.Null;
        }

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
        /// no room for the whole vehicle past the band (the same HasRoomPast as S2-28, entering the band at t). 0.4.7: the
        /// band set is BoxHold's (own carriageway only, touching bands merged, junction join at the lane end).
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
            // 0.4.7 (JR-T-013g, review F1): the same band set as S2-28 BoxHold (JRYellowBox.cs): only the bands of this lane's
            // carriageway (#17 median), and bands that overlap or touch on this lane are ONE band (#17 merged boxes).
            int carriageway = carLanes.TryGetComponent(road, out Game.Net.CarLane roadCarLane) ? roadCarLane.m_CarriagewayGroup : 0;
            float touch = ExitBands.TouchMetres / length;
            FixedList128Bytes<float2> merged = default;
            for (int i = first; i < bands->Count && bands->Key(i) == edgeKey; i++)
            {
                if (!MedianSplit.InMask(bands->Mask(i), carriageway)) continue;
                float access = (bands->T(i) - edgeLane.m_EdgeDelta.x) / span;
                if (!BoxDriving.Band(access, length, m_BoxEntranceLength, false, out float nb0, out float nb1)) continue;
                int last = merged.Length - 1;
                if (last >= 0 && ExitBands.Touch(merged[last].x, merged[last].y, nb0, nb1, touch))
                {
                    merged[last] = math.float2(math.min(merged[last].x, nb0), math.max(merged[last].y, nb1));
                    continue;
                }
                if (merged.Length >= merged.Capacity) break;
                merged.Add(math.float2(nb0, nb1));
            }
            for (int m = 0; m < merged.Length; m++)
            {
                float b0 = merged[m].x, b1 = merged[m].y;
                if (t < b0 - WholePathRule.SpotEpsilon || t > b1 + WholePathRule.SpotEpsilon) continue;
                boxed = true;
                // As BoxHold: a merged band reaching the lane end where the leaving car's path enters a boxed junction is one
                // box with it, so room is needed past the junction box (its lanes count as inside the box).
                Entity joined = (1f - b1) * length <= ExitBands.JunctionJoinMetres ? NextBoxedJunction(road) : Entity.Null;
                bool room = HasRoomPast(road, b1, false, length, t, joined, self, safeTimeStep, ref carLanes, ref laneObjects, ref curves,
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
            if (++budget.m_Lanes > ExitGiveWay.MaxScanLanes) { budget.m_CapHit = budget.m_LaneCap = true; return false; }
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
                if (++budget.m_Lanes > ExitGiveWay.MaxScanLanes) { budget.m_CapHit = budget.m_LaneCap = true; return false; }
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
                    if (++budget.m_Lanes > ExitGiveWay.MaxScanLanes) { budget.m_CapHit = budget.m_LaneCap = true; return false; }
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
                // Review F3: never the leaving vehicle's own lane (its followers queue behind it; they are not crossing traffic).
                if (feed == lane || feed == reads.m_OwnLane || !m_LaneData.TryGetComponent(feed, out Lane feedLane) || !feedLane.m_EndNode.Equals(start)) continue;
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
                float front = x * curve.m_Length, rear = front;
                if (reads.m_Geometries.TryGetComponent(pr.m_Prefab, out ObjectGeometryData g))
                {
                    front += g.m_Bounds.max.z;
                    rear += g.m_Bounds.min.z;
                }
                float gap = cStart - front;
                float speed = 0f;
                if (reads.m_Moving.TryGetComponent(e, out Moving m))
                {
                    speed = math.dot(m.m_Velocity, math.normalizesafe(MathUtils.Tangent(curve.m_Bezier, x)));
                    if (speed < 0f) speed = 0f;
                }
                // 0.4.7: only a vehicle before the conflict end and within reach counts against the budget (0.4.4-0.4.6 counted
                // every vehicle on the lane, also those past the conflict or far away); a hit is dense traffic: busy (Core ExitPaths.CapBusy).
                // Review F2: a vehicle standing before the conflict never blocks (VehicleBlocks), so it does not count either.
                if (!ExitPaths.CountsAgainstBudget(rear, cEnd, gap, speed)) continue;
                if (++budget.m_Vehicles > ExitGiveWay.MaxVehiclesPerScan)
                {
                    budget.m_CapHit = budget.m_VehicleCap = true;
                    return ExitPaths.CapBusy(false, true);
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
