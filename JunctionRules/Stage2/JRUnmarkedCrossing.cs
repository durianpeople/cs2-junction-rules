using Colossal.Mathematics;
using Game.Net;
using Game.Objects;
using JunctionRules.Core;
using JunctionRules.Systems;
using Unity.Entities;
using Unity.Mathematics;

namespace JunctionRules.Stage2
{
    /// <summary>
    /// JR-T-014d (G11), car side of "People give way at unmarked crossings" (Core/UnmarkedCrossing.cs). Called from the
    /// copy's creature branch in CheckOverlappingLanes (blocks S2-50 / S2-50b) for each person on a pedestrian lane the
    /// car's lane crosses. Fields live in JRWholeJunction.cs. Writes only the always-on diagnostics (Systems/UnmarkedTrace.cs:
    /// Interlocked counters, fixed ring).
    /// </summary>
    public partial struct JRWholeJunction
    {
        /// <summary>
        /// UnmarkedCrossing.Stock / Skip / Floor for one person on pedLane, crossed by the car's lane carLane. Unmarked =
        /// a crosswalk without the marking (Unsafe, no signal), never a driveway (SideConnection car lane) or a sidewalk.
        /// The person is on the carriageway when their position along the crossing lies past the kerb edge of the first
        /// road overlap (walking direction from their velocity; standing: not within the tolerance of either kerb). An
        /// unreadable position counts as on the carriageway (the safe side: the game's check).
        /// </summary>
        public int UnmarkedPerson(Entity carLane, Entity pedLane, Entity person, Curve pedCurve,
            ref ComponentLookup<Game.Net.PedestrianLane> peds, ref ComponentLookup<LaneSignal> signals, ref ComponentLookup<Game.Net.CarLane> carLanes,
            ref BufferLookup<LaneOverlap> overlaps, ref ComponentLookup<Transform> transforms, ref ComponentLookup<Moving> moving)
        {
            if (!m_UnmarkedRule || !peds.TryGetComponent(pedLane, out Game.Net.PedestrianLane ped)) return UnmarkedCrossing.Stock;
            uint carFlags = carLanes.TryGetComponent(carLane, out Game.Net.CarLane cl) ? (uint)cl.m_Flags : 0u;
            UnmarkedCrossing.Kind kind = UnmarkedCrossing.Classify((int)ped.m_Flags, signals.HasComponent(pedLane), carFlags);
            if (kind != UnmarkedCrossing.Kind.Unmarked) return UnmarkedCrossing.Stock;
            bool on = true;
            if (transforms.TryGetComponent(person, out Transform t) && overlaps.TryGetBuffer(pedLane, out DynamicBuffer<LaneOverlap> ov))
            {
                float lo = 2f, hi = -1f;
                for (int i = 0; i < ov.Length; i++)
                {
                    LaneOverlap o = ov[i];
                    if ((o.m_Flags & (OverlapFlags.Road | OverlapFlags.Track)) == 0) continue;
                    if ((o.m_Flags & (OverlapFlags.MergeStart | OverlapFlags.MergeEnd | OverlapFlags.MergeMiddleStart | OverlapFlags.MergeMiddleEnd)) != 0) continue;
                    lo = math.min(lo, math.min((float)o.m_ThisStart, o.m_ThisEnd) * (1f / 255f));
                    hi = math.max(hi, math.max((float)o.m_ThisStart, o.m_ThisEnd) * (1f / 255f));
                }
                if (hi > lo)
                {
                    MathUtils.Distance(pedCurve.m_Bezier.xz, t.m_Position.xz, out float tp);
                    float along = 0f;
                    if (moving.TryGetComponent(person, out Moving m))
                        along = math.dot(m.m_Velocity.xz, math.normalizesafe(MathUtils.Tangent(pedCurve.m_Bezier, tp).xz));
                    float len = pedCurve.m_Length;
                    on = UnmarkedCrossing.OnCarriageway(tp * len, lo * len, hi * len, UnmarkedCrossing.Direction(along));
                }
            }
            return UnmarkedCrossing.CarAction(true, kind, on);
        }

        /// <summary>
        /// Skip: the game's check was run against the saved limit only to see whether it would have held the car
        /// (before / after); counted once per car-step (always-on counter "car-steps not held for people").
        /// </summary>
        public bool UnmarkedNeedsCounterfactual => !m_UnmarkedSkipCounted;

        public void AfterUnmarkedSkip(float before, float after)
        {
            if (m_UnmarkedSkipCounted || !(after < before)) return;
            m_UnmarkedSkipCounted = true;
            UnmarkedTrace.Add(UnmarkedTraceLayout.CarStepsNotHeld, 1);
        }

        /// <summary>
        /// Floor: the game's own check ran for a person already on the carriageway; when it made the car brake (limit below
        /// the car's speed now) this is counted once per car-step and recorded as a notable event (car, person, speeds,
        /// person position).
        /// </summary>
        public void AfterUnmarkedFloor(float before, float after, float currentSpeed, Entity car, Entity person, ref ComponentLookup<Transform> transforms)
        {
            if (m_UnmarkedFloorCounted || !(after < before) || !(after < currentSpeed)) return;
            m_UnmarkedFloorCounted = true;
            UnmarkedTrace.Add(UnmarkedTraceLayout.FloorStops, 1);
            float3 pos = transforms.TryGetComponent(person, out Transform t) ? t.m_Position : default;
            UnmarkedTrace.Record(UnmarkedTraceLayout.KindFloor, UnmarkedTrace.Frame, car.Index, person.Index,
                (long)(currentSpeed * 100f), (long)(math.max(0f, after) * 100f), UnmarkedTraceLayout.PackXZ(pos.x, pos.z));
        }
    }
}
