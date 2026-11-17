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
    /// <summary>0.4.11 (JR-T-017, G22): job side of the owner's same-exit-lane box exception (Core/SameExitException.cs). Fields live in JRWholeJunction.cs.</summary>
    public partial struct JRWholeJunction
    {
        /// <summary>
        /// 0.4.11 (JR-T-017, G22; Core/SameExitException.cs): the junction lane's car may enter the box without room in its exit
        /// lane: its approach is the lowest-priority one (the game's Stop / Yield flags on the junction's car lanes; signals,
        /// level crossings, roundabouts never) and every overlap its waiting footprint touches is a car lane ending in the same
        /// exit lane (Lane.m_EndNode). Footprint: front at the lane end, before an exit zebra or behind a vehicle standing ahead
        /// on this lane; whole length (trailers incl.) + margin behind. start = the car's own driving position on the lane.
        /// </summary>
        private bool SameExitAdmits(Entity lane, Entity node, float laneLength, bool inverse, float start, Entity self,
            ref ComponentLookup<Game.Net.CarLane> carLanes, ref BufferLookup<LaneObject> laneObjects, ref ComponentLookup<Curve> curves,
            ref ComponentLookup<Controller> controllers, ref ComponentLookup<PrefabRef> prefabRefs, ref ComponentLookup<ObjectGeometryData> geometries,
            ref BufferLookup<LaneOverlap> overlapData, ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes, ref ComponentLookup<Moving> moving)
        {
            if (!carLanes.TryGetComponent(lane, out Game.Net.CarLane own) || !m_SubLanes.TryGetBuffer(node, out DynamicBuffer<Game.Net.SubLane> subs)) return false;
            uint ownFlags = (uint)own.m_Flags;
            int minRank = 2, maxRank = 0;
            bool excluded = SameExitException.Excludes(ownFlags);
            for (int i = 0; i < subs.Length && !excluded; i++)
            {
                if (!carLanes.TryGetComponent(subs[i].m_SubLane, out Game.Net.CarLane cl)) continue;
                uint f = (uint)cl.m_Flags;
                if (!SameExitException.Ranked(f)) continue;
                if (SameExitException.Excludes(f)) { excluded = true; break; }
                int r = SameExitException.Rank(f);
                if (r < minRank) minRank = r;
                if (r > maxRank) maxRank = r;
            }
            if (!SameExitException.Eligible(SameExitException.Rank(ownFlags), minRank, maxRank, excluded)) return false;
            if (!m_LaneData.TryGetComponent(lane, out Lane ownLane) || !overlapData.TryGetBuffer(lane, out DynamicBuffer<LaneOverlap> overlaps)) return false;
            // Exit zebra: the first pedestrian overlap in the far half (the zebra rule never lets the car stand on it).
            float zebra = float.MaxValue;
            // 0.4.12 OWNER DECISION REVISED: ANY pedestrian path may lie under the footprint (zebras too) and the front is not
            // held before an exit zebra; 'zebras' marks the zebra overlaps (first 64) for the counter "tail on a zebra".
            ulong zebras = 0UL;
            for (int i = 0; i < overlaps.Length && i < 64; i++)
                if (pedestrianLanes.TryGetComponent(overlaps[i].m_Other, out Game.Net.PedestrianLane ped) && SameExitException.BlocksAsZebra((int)ped.m_Flags)) zebras |= 1UL << i;
            // Box chain: the rear of the nearest vehicle standing / driving ahead of the car on this lane.
            float rearAhead = float.MaxValue;
            if (laneObjects.TryGetBuffer(lane, out DynamicBuffer<LaneObject> objects))
            {
                for (int i = 0; i < objects.Length; i++)
                {
                    Entity e = objects[i].m_LaneObject;
                    if (e == self || (controllers.TryGetComponent(e, out Controller c) && c.m_Controller == self)) continue;
                    if (!prefabRefs.TryGetComponent(e, out PrefabRef pr)) continue;
                    // Review 3 F4: same direction only (a vehicle driving the lane against the car is no leader).
                    bool against = inverse ? objects[i].m_CurvePosition.y > objects[i].m_CurvePosition.x : objects[i].m_CurvePosition.y < objects[i].m_CurvePosition.x;
                    if (against) continue;
                    // Review 3 F1: only a STANDING leader moves the footprint back (a rolling one stops further on; the lane-end
                    // footprint is the stricter test then).
                    if (moving.TryGetComponent(e, out Moving mv) && math.lengthsq(mv.m_Velocity) >= Stage2Counters.StandingSpeed * Stage2Counters.StandingSpeed) continue;
                    float x = StretchGeometry.ToDriving(objects[i].m_CurvePosition.x, inverse) * laneLength;
                    if (x <= start * laneLength) continue;
                    // Review 3 F4: x is in driving coordinates, so the rear is x + bounds.min.z on either lane direction.
                    float rear = x + (geometries.TryGetComponent(pr.m_Prefab, out ObjectGeometryData g) ? g.m_Bounds.min.z : 0f);
                    if (rear < rearAhead) rearAhead = rear;
                }
            }
            SameExitException.Footprint(laneLength, zebra, rearAhead, m_VehicleLength > 0f ? m_VehicleLength : 4f, m_RoomMargin, out float from, out float to);
            // Review 3 F2: a footprint reaching back before the junction lane (rear on the approach, where its zebra / bands are
            // not checked here): no exception (conservative).
            if (!SameExitException.WithinLane(from)) return false;
            FootprintPaths paths = default;
            paths.m_Overlaps = overlaps;
            paths.m_Inverse = inverse;
            paths.m_Length = laneLength;
            paths.m_ExitNode = ownLane.m_EndNode;
            paths.m_CarLanes = carLanes;
            paths.m_Lanes = m_LaneData;
            paths.m_Pedestrian = pedestrianLanes;
            if (!SameExitException.Admits(from, to, ref paths)) return false;
            // counter only: does a zebra lie under the admitted footprint?
            for (int i = 0; i < overlaps.Length && i < 64; i++)
            {
                if ((zebras & (1UL << i)) == 0) continue;
                StretchGeometry.ToConflict((int)overlaps[i].m_Flags, overlaps[i].m_ThisStart, overlaps[i].m_ThisEnd, inverse, false, out float zs, out float ze);
                if (zs * laneLength < to && ze * laneLength > from) { m_SameExitZebraTail = true; break; }
            }
            return true;
        }

        /// <summary>
        /// 0.4.12 owner decision revised: the zebra room hold (BeginZebras) is skipped for this lane: a boxed junction lane whose car
        /// G22 admits (before the lane: start 0, else its own position), or a drive's leaving lane whose car G28 admits.
        /// </summary>
        private bool SameExitZebraExempt(Entity lane, DynamicBuffer<LaneOverlap> overlaps, float curveX, float length, bool inverse, Entity self,
            ref ComponentLookup<Game.Net.CarLane> carLanes, ref BufferLookup<LaneObject> laneObjects, ref ComponentLookup<Curve> curves,
            ref ComponentLookup<Moving> moving, ref ComponentLookup<Controller> controllers, ref ComponentLookup<PrefabRef> prefabRefs,
            ref ComponentLookup<ObjectGeometryData> geometries, ref BufferLookup<LaneOverlap> overlapData, ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes)
        {
            float start = StretchGeometry.ToDriving(curveX, inverse);
            if (IsBoxedJunctionLane(lane) && m_OwnerData.TryGetComponent(lane, out Owner o))
                return SameExitAdmits(lane, o.m_Owner, length, inverse, start, self, ref carLanes, ref laneObjects, ref curves, ref controllers, ref prefabRefs,
                    ref geometries, ref overlapData, ref pedestrianLanes, ref moving);
            if (m_ExitRule && carLanes.TryGetComponent(lane, out Game.Net.CarLane cl) && ExitGiveWay.IsDrivewayExitLane((uint)cl.m_Flags))
            {
                G28Check(lane, overlaps, start, inverse, length, self, ref carLanes, ref laneObjects, ref curves, ref moving, ref controllers, ref prefabRefs,
                    ref geometries, ref overlapData, ref pedestrianLanes);
                return m_G28JoinLane != Entity.Null || m_G28Latched != Entity.Null;
            }
            return false;
        }


        /// <summary>0.4.11 (G22): the junction lane's overlaps as footprint paths (metres, kind by the other lane's exit lane).</summary>
        private struct FootprintPaths : IFootprintPaths
        {
            public DynamicBuffer<LaneOverlap> m_Overlaps;
            public bool m_Inverse;
            public float m_Length;
            public Game.Pathfind.PathNode m_ExitNode;
            public ComponentLookup<Game.Net.CarLane> m_CarLanes;
            public ComponentLookup<Lane> m_Lanes;
            public ComponentLookup<Game.Net.PedestrianLane> m_Pedestrian;


            public int Count => m_Overlaps.Length;

            public bool Get(int i, out float start, out float end, out SameExitException.Path kind)
            {
                LaneOverlap o = m_Overlaps[i];
                StretchGeometry.ToConflict((int)o.m_Flags, o.m_ThisStart, o.m_ThisEnd, m_Inverse, false, out float s, out float e);
                start = s * m_Length;
                end = e * m_Length;
                kind = SameExitException.Path.Other;
                if (((int)o.m_Flags & WholePathRule.Water) != 0) return false;
                if (m_Pedestrian.HasComponent(o.m_Other)) { kind = SameExitException.Path.Pavement; return true; }   // 0.4.12 owner: any pedestrian path
                if (m_CarLanes.HasComponent(o.m_Other) && m_Lanes.TryGetComponent(o.m_Other, out Lane other))
                    kind = other.m_EndNode.Equals(m_ExitNode) ? SameExitException.Path.SameExit : SameExitException.Path.OtherExit;
                return true;
            }
        }

    }
}
