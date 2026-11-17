using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Vehicles;
using JunctionRules.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace JunctionRules.Stage2
{
    /// <summary>
    /// 0.4.11 (JR-T-017, gap G25, owner decision option (c), 17 Nov 2026): at an UNBOXED junction the game's own room hold
    /// (CarLaneSpeedIterator.IterateNextLane -> CheckSpace: a car entering a junction lane with a true crossing overlap waits
    /// at the entry until its next lane has room for its length) is skipped when every lane the turner crosses is clear by
    /// the Junction Rules gap check (nothing on it or on the lanes feeding it that reaches the crossing before the turner has
    /// cleared it with its whole length; a vehicle standing within 10 m of the crossing counts; one queued at a red does not).
    /// The turner then enters and waits inside the junction for exit room, never across moving traffic. Holding outside a
    /// junction for room past it is yellow-box behaviour only (owner rule G25). Kept stock: boxed junctions (the box rules
    /// decide), signalled / level-crossing junction lanes (their signal request runs only on the stock path), roundabouts
    /// (CheckSpace is never called there). Needs the Whole junction gap rule and the exit setting's lookups (both default on).
    /// Block S2-54 in the copy (after the stock chain); fields in JRWholeJunction.cs.
    /// </summary>
    public partial struct JRWholeJunction
    {
        /// <summary>
        /// S2-54: the stock chain just held the car at the entry of junction lane <paramref name="lane"/> for room (driveSpeed 0,
        /// blocker type Continuing, set only by a failed CheckSpace in that chain). True = drop that hold (the caller restores the
        /// lane's drive speed). frontToStart = the stock num2 (metres from the car's front to the lane start), inverse = the lane
        /// is driven against its curve (the stock flag2).
        /// </summary>
        public bool SkipStockRoomHold(Entity lane, Game.Net.CarLaneFlags laneFlags, float laneLength, float frontToStart, bool inverse, CarData car,
            float currentSpeed, float timeStep, Entity self, ref ComponentLookup<Game.Net.CarLane> carLanes, ref ComponentLookup<LaneSignal> signals,
            ref BufferLookup<LaneObject> laneObjects, ref ComponentLookup<Curve> curves, ref ComponentLookup<Moving> moving, ref ComponentLookup<Car> cars,
            ref ComponentLookup<Controller> controllers, ref ComponentLookup<PrefabRef> prefabRefs, ref ComponentLookup<CarData> prefabCars,
            ref ComponentLookup<ObjectGeometryData> geometries, ref BufferLookup<LaneOverlap> overlapData)
        {
            if (!StockRoomSkip.Applies(RuleOn, m_ExitLookups, (uint)laneFlags, m_NodeLaneData.HasComponent(lane), IsBoxedJunctionLane(lane))) return false;
            if (!overlapData.TryGetBuffer(lane, out DynamicBuffer<LaneOverlap> overlaps) || overlaps.Length == 0) return false;
            if (!carLanes.TryGetComponent(lane, out Game.Net.CarLane own)) return false;
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
            float drive = VehicleUtils.GetMaxDriveSpeed(car, own);
            float ownLength = m_VehicleLength > 0f ? m_VehicleLength : 4f;
            int crossings = 0;
            for (int i = 0; i < overlaps.Length; i++)
            {
                LaneOverlap o = overlaps[i];
                if (!StockRoomSkip.IsCrossedLane((int)o.m_Flags, carLanes.HasComponent(o.m_Other))) continue;
                if (!curves.TryGetComponent(o.m_Other, out Curve otherCurve)) continue;
                if (++crossings > ExitGiveWay.MaxConflictLanes) return false;   // conservative: too much to judge
                StretchGeometry.ToConflict((int)o.m_Flags, o.m_ThisStart, o.m_ThisEnd, inverse, false, out float s, out float e);
                float a = o.m_OtherStart * WholePathRule.ByteToOffset, b = o.m_OtherEnd * WholePathRule.ByteToOffset;
                float oS = math.min(a, b) * otherCurve.m_Length, oE = math.max(a, b) * otherCurve.m_Length;
                float dist = ExitGiveWay.ClearMetres(frontToStart + laneLength * s, laneLength * (e - s), ownLength);
                float delta = ArrivalTime.Compute(ref motion, dist, currentSpeed, drive, 0f, timeStep);
                if (ConflictBusy(o.m_Other, otherCurve, oS, oE, delta, ref reads, ref budget) || budget.m_CapHit) return false;
            }
            if (crossings == 0) return false;   // nothing crossed: the stock hold had another reason (stays)
            if (Counting) m_StockRoomSkipped = true;
            return true;
        }
    }
}
