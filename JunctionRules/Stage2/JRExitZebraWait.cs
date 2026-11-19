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
    /// JR-T-024 (owner ruling 19 Nov 2026, BINDING; Core/ExitZebraWait.cs): job side of "wait in the box before the exit zebra
    /// while people are crossing", behind the setting "Look past the car in front at a box" (m_BoxLookPast). Fields live in
    /// JRWholeJunction.cs. Read-only lookups handed in by BoxHold; nothing shared is written except the debug counters.
    /// With the setting off every entry point returns at its first test: driving equals 1.0.1.
    /// </summary>
    public partial struct JRWholeJunction
    {
        /// <summary>
        /// The exit zebra of a boxed junction lane (Core ExitZebraWait.IsExitZebra): its pedestrian lane and its start in
        /// driving coordinates. Zebras as the zebra sections read them (crosswalk, not Unsafe; water overlaps never).
        /// </summary>
        private static bool ExitZebraOf(Entity lane, bool inverse, ref BufferLookup<LaneOverlap> overlapData,
            ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes, out Entity zebraLane, out float zebraStart, out float previousEnd)
        {
            zebraLane = Entity.Null;
            zebraStart = -1f;
            previousEnd = 0f;
            if (!overlapData.TryGetBuffer(lane, out DynamicBuffer<LaneOverlap> overlaps)) return false;
            for (int i = 0; i < overlaps.Length; i++)
            {
                LaneOverlap o = overlaps[i];
                int flags = (int)o.m_Flags;
                if ((flags & WholePathRule.Water) != 0 || !pedestrianLanes.TryGetComponent(o.m_Other, out Game.Net.PedestrianLane ped)
                    || !ZebraSections.IsZebra((int)ped.m_Flags)) continue;
                StretchGeometry.ToConflict(flags, o.m_ThisStart, o.m_ThisEnd, inverse, false, out float s, out _);
                if (s > zebraStart)
                {
                    zebraStart = s;
                    zebraLane = o.m_Other;
                }
            }
            if (zebraLane == Entity.Null || !ExitZebraWait.IsExitZebra(zebraStart, zebraStart)) return false;
            // review fix: the end of the zebra(s) before it (the entry zebra), for the fit of a waiting vehicle; 0 = the box line
            for (int i = 0; i < overlaps.Length; i++)
            {
                LaneOverlap o = overlaps[i];
                int flags = (int)o.m_Flags;
                if ((flags & WholePathRule.Water) != 0 || !pedestrianLanes.TryGetComponent(o.m_Other, out Game.Net.PedestrianLane ped)
                    || !ZebraSections.IsZebra((int)ped.m_Flags)) continue;
                StretchGeometry.ToConflict(flags, o.m_ThisStart, o.m_ThisEnd, inverse, false, out float s, out float e);
                if (s < zebraStart - WholePathRule.SpotEpsilon && e > previousEnd) previousEnd = e;
            }
            return true;
        }

        /// <summary>
        /// Everyone on the zebra's pedestrian lane, classified (Core ExitZebraWait.Person: the game's walking test; with
        /// "Cars never push through people" a person standing in the waiting area at a lane end is not on it).
        /// <paramref name="stander"/> = the first standing person (the blocker named for a line hold).
        /// </summary>
        private int ExitZebraPeople(Entity zebraLane, ref BufferLookup<LaneObject> laneObjects, ref ComponentLookup<Moving> moving,
            ref ComponentLookup<Curve> curves, out Entity stander)
        {
            stander = Entity.Null;
            if (!laneObjects.TryGetBuffer(zebraLane, out DynamicBuffer<LaneObject> objects) || objects.Length == 0) return ExitZebraWait.None;
            float zebraLength = curves.TryGetComponent(zebraLane, out Curve zc) ? zc.m_Length : 0f;
            int people = ExitZebraWait.None;
            for (int i = 0; i < objects.Length; i++)
            {
                Entity obj = objects[i].m_LaneObject;
                bool hasMoving = moving.TryGetComponent(obj, out Moving m);
                int p = ExitZebraWait.Person(hasMoving, hasMoving ? math.lengthsq(m.m_Velocity) : 0f, m_NoPushPeople,
                    CrossingPush.InWaitZone(objects[i].m_CurvePosition.x, zebraLength));
                if (p == ExitZebraWait.Stationary && stander == Entity.Null) stander = obj;
                people = ExitZebraWait.Worse(people, p);
            }
            return people;
        }

        /// <summary>
        /// Called by BoxHold (junction box, car before the line and able to stop there) when the box room test let the car in.
        /// People standing on the exit zebra, or walking on it while the road past the box is not free now (no look-past
        /// credit: the car will stand before the zebra, or it does not fit between the entry zebra / line and the stop point before
        /// the exit zebra): the box hold <paramref name="hold"/> at the line. People walking, the room free now and the vehicle fitting: no hold, and this lane's exit zebra is marked so the pedestrian check stops the car just before
        /// it (PedestrianDistance). Nobody there: no change. float.MaxValue = no hold.
        /// </summary>
        private float ExitZebraHold(Entity lane, float laneLength, bool inverse, float hold, float margin, Entity boxNode, float frontToEnd, Entity self, float safeTimeStep,
            ref ComponentLookup<Game.Net.CarLane> carLanes, ref BufferLookup<LaneObject> laneObjects, ref ComponentLookup<Curve> curves,
            ref ComponentLookup<Moving> moving, ref ComponentLookup<Controller> controllers, ref ComponentLookup<PrefabRef> prefabRefs,
            ref ComponentLookup<ObjectGeometryData> geometries, ref ComponentLookup<CarData> prefabCars,
            ref BufferLookup<LaneOverlap> overlapData, ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes)
        {
            if (!m_BoxLookPast) return float.MaxValue;
            if (!ExitZebraOf(lane, inverse, ref overlapData, ref pedestrianLanes, out Entity zebra, out float zebraStart, out float previousEnd)) return float.MaxValue;
            int people = ExitZebraPeople(zebra, ref laneObjects, ref moving, ref curves, out Entity stander);
            if (people == ExitZebraWait.None) return float.MaxValue;
            bool roomFreeNow = false;
            // review fix: a vehicle that does not fit between the entry zebra (or the line) and the stop point before the exit zebra
            // would stand with its tail on a zebra / past the line: it waits at the line (as the zebra sections decide it today)
            if (people == ExitZebraWait.Moving && ExitZebraWait.FitsBeforeZebra(laneLength, zebraStart, previousEnd, margin, m_VehicleLength))
            {
                // the box room test above may have counted on the look-past: ask again with the free-now rule alone
                m_BoxLookPast = false;
                m_QueuedTurnersMayStand = m_BoxTurnWait && BoxDriving.TurnerMayWait(true, (uint)carLanes[lane].m_Flags, m_BoxLeftHand);
                roomFreeNow = HasRoomPast(lane, 1f, inverse, laneLength, 0f, boxNode, self, safeTimeStep, ref carLanes, ref laneObjects, ref curves,
                    ref moving, ref controllers, ref prefabRefs, ref geometries, ref prefabCars, ref overlapData, ref pedestrianLanes, freeNow: true,
                    frontToStart: frontToEnd);
                m_QueuedTurnersMayStand = false;
                m_BoxLookPast = true;
            }
            ExitZebraWait.Verdict v = ExitZebraWait.Decide(true, people, roomFreeNow);
            if (v == ExitZebraWait.Verdict.WaitBeforeZebra)
            {
                m_ZebraGoLane = lane;
                m_ZebraGoStart = zebraStart;
                m_LastBlocker = Entity.Null;
                return float.MaxValue;
            }
            if (v != ExitZebraWait.Verdict.Line) return float.MaxValue;
            m_LookPastAdmitted = false;   // probe: the look-past room did not let it in after all (held at the line)
            if (people == ExitZebraWait.Stationary) m_LastBlocker = stander;   // the room scan named its blocker otherwise
            else NoteLightPetition(hold);   // review R1: a light inside the room window holds it: ask that light for green
            if (Counting) Min(ref m_HasBoxZebraLine, ref m_BoxZebraLineSpeed, hold);
            return hold;
        }

        /// <summary>
        /// Counting only (a car standing on a lane of the box): it waits at the exit zebra of this lane while somebody is on
        /// it (Core ExitZebraWait.WaitingAtZebra): counted apart from "standing inside a box" and the nose count (owner rule).
        /// </summary>
        private bool StandingAtExitZebra(Entity lane, float laneLength, bool inverse, float wait0, float margin, float start,
            ref BufferLookup<LaneOverlap> overlapData, ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes,
            ref BufferLookup<LaneObject> laneObjects, ref ComponentLookup<Moving> moving, ref ComponentLookup<Curve> curves)
        {
            if (!m_BoxLookPast || !ExitZebraOf(lane, inverse, ref overlapData, ref pedestrianLanes, out Entity zebra, out float zebraStart, out _)) return false;
            float frontToZebra = BoxDriving.FrontToLine(wait0 + laneLength * (zebraStart - start), margin);
            if (frontToZebra < -BoxDriving.NoseTolerance || frontToZebra > margin + ExitZebraWait.AtZebraMetres) return false;
            return ExitZebraWait.WaitingAtZebra(frontToZebra, margin, ExitZebraPeople(zebra, ref laneObjects, ref moving, ref curves, out _));
        }
    }
}
