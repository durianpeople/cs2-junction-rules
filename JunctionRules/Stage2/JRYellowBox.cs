using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Vehicles;
using JunctionRules.Core;
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
        public unsafe void SetBoxes(ComponentLookup<JunctionRules.Boxes.YellowBoxJunction> junctions, ComponentLookup<Game.Net.EdgeLane> edgeLanes, ref JRBoxEdges boxEdges)
        {
            m_BoxJunctionData = junctions;
            m_EdgeLaneData = edgeLanes;
            m_BoxEdges = (JRBoxEdges*)UnsafeUtility.AddressOf(ref boxEdges);
            m_BoxLookups = true;
        }

        /// <summary>The lane is a node lane of a boxed junction (setting on, junction boxes exist).</summary>
        private bool IsBoxedJunctionLane(Entity lane) =>
            m_BoxRule && m_BoxJunctions && m_BoxLookups && m_NodeLaneData.HasComponent(lane)
            && m_OwnerData.TryGetComponent(lane, out Owner owner) && m_BoxJunctionData.HasComponent(owner.m_Owner);

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
                BoxDriving.Approach a = BoxDriving.Classify(inBox, wait, margin, VehicleUtils.GetBrakingDistance(car, currentSpeed, timeStep));
                if (a != BoxDriving.Approach.Check) return float.MaxValue;
                float hold = MathUtils.Clamp(VehicleUtils.GetMaxBrakingSpeed(car, wait, safeTimeStep), speedRange);
                if (!(hold < currentMax)) return float.MaxValue;
                bool room = HasRoomPast(lane, 1f, inverse, laneLength, 0f, owner.m_Owner, self, safeTimeStep, ref carLanes, ref laneObjects, ref curves,
                    ref moving, ref controllers, ref prefabRefs, ref geometries, ref prefabCars, ref overlapData, ref pedestrianLanes);
                if (!BoxDriving.Holds(a, room)) return float.MaxValue;
                if (Counting) Min(ref m_HasBoxRoom, ref m_BoxRoomSpeed, hold);
                return hold;
            }
            // 0.4.3: the boxed CAR EXITS on this road (JRBoxEdges: edge key + curve position per exit, resolved on the main
            // thread from the saved exit keys); one binary search finds the edge's first band. Buildings are not read here.
            if (!m_BoxEntrances || !m_EdgeLaneData.TryGetComponent(lane, out Game.Net.EdgeLane edgeLane) || !m_OwnerData.TryGetComponent(lane, out Owner edgeOwner))
                return float.MaxValue;
            JRBoxEdges* bands = m_BoxEdges;
            int first = bands->First(edgeOwner.m_Owner);
            if (first < 0) return float.MaxValue;
            long edgeKey = bands->Key(first);
            float delta = edgeLane.m_EdgeDelta.y - edgeLane.m_EdgeDelta.x;
            if (math.abs(delta) < 1e-4f) return float.MaxValue;
            float braking = -1f;
            float result = float.MaxValue;
            for (int i = first; i < bands->Count && bands->Key(i) == edgeKey; i++)
            {
                // The exit point on this lane: the edge curve position mapped through the lane's own edge range.
                float access = (bands->T(i) - edgeLane.m_EdgeDelta.x) / delta;
                if (!BoxDriving.Band(access, laneLength, m_BoxEntranceLength, inverse, out float b0, out float b1)) continue;
                BoxDriving.EntranceRole role = BoxDriving.Role(start, end, b0, b1, out float entry);
                if (role != BoxDriving.EntranceRole.Main && role != BoxDriving.EntranceRole.Leaving) continue;
                bool inBand = m_FirstLane && start >= b0 - WholePathRule.SpotEpsilon;
                float wait = wait0 + laneLength * (entry - start);
                if (braking < 0f) braking = VehicleUtils.GetBrakingDistance(car, currentSpeed, timeStep);
                BoxDriving.Approach a = BoxDriving.Classify(inBand, wait, margin, braking);
                if (a != BoxDriving.Approach.Check) continue;
                float hold = MathUtils.Clamp(VehicleUtils.GetMaxBrakingSpeed(car, wait, safeTimeStep), speedRange);
                if (!(hold < currentMax) || !(hold < result)) continue;
                bool room = HasRoomPast(lane, b1, inverse, laneLength, entry, Entity.Null, self, safeTimeStep, ref carLanes, ref laneObjects, ref curves,
                    ref moving, ref controllers, ref prefabRefs, ref geometries, ref prefabCars, ref overlapData, ref pedestrianLanes);
                if (!BoxDriving.Holds(a, room)) continue;
                result = hold;
                if (Counting)
                {
                    if (role == BoxDriving.EntranceRole.Leaving) Min(ref m_HasEntranceLeaving, ref m_EntranceLeavingSpeed, hold);
                    else Min(ref m_HasEntranceMain, ref m_EntranceMainSpeed, hold);
                }
            }
            return result;
        }
    }
}
