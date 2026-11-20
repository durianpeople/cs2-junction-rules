using Colossal.Mathematics;
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
    /// JR-T-033 (1.4.0, owner rule 20 Nov 2026, BINDING; Core/ZebraDriveUp.cs): job side of "wait for a zebra right before the
    /// zebra". Fields live in JRWholeJunction.cs. Called from BeginZebras (block S2-25, zebra sections only); read-only
    /// lookups handed in; nothing shared is written except the debug counters. Without people on a merged zebra nothing
    /// changes; a yellow box lane keeps the box rules (no drive-up, no crossing hold).
    /// </summary>
    public partial struct JRWholeJunction
    {
        private void DriveUpReset()
        {
            m_DriveUp = false;
            m_DriveUpSection = -1;
            m_DriveUpStart = 2f;
            m_DriveUpDeltaZebra = 0f;
            m_DriveUpWait = 0f;
            m_DriveUpClear = 0f;
            m_DriveUpReason = (byte)ZebraDriveUp.Reason.None;
            m_CrossSection = -1;
            m_CrossZebra = 2f;
            m_CrossNoFit = 0;
        }

        /// <summary>
        /// BeginZebras, after the sections and the car's motion are set: (1) the section being crossed: the first zebra still
        /// ahead, the later zebras the car would not fit clear of, and the room asked again before the car moves onto that
        /// zebra (people on it, or the car standing at it; returns that hold, float.MaxValue = none); (2) the sections still
        /// ahead: the first zebra with people; merged into an earlier spot = a drive-up when eligible (else the reason).
        /// </summary>
        private float DriveUpZebras(Entity lane, DynamicBuffer<LaneOverlap> overlaps, ref ZebraElements elements, in ZebraLayout layout, float length,
            float margin, bool inverse, CarData car, float currentSpeed, float safeTimeStep, Bounds1 speedRange, float currentMax, Entity self,
            ref ComponentLookup<Game.Creatures.Creature> creatures, ref ComponentLookup<Game.Net.CarLane> carLanes, ref BufferLookup<LaneObject> laneObjects,
            ref ComponentLookup<Curve> curves, ref ComponentLookup<Moving> moving, ref ComponentLookup<Controller> controllers,
            ref ComponentLookup<PrefabRef> prefabRefs, ref ComponentLookup<ObjectGeometryData> geometries, ref ComponentLookup<CarData> prefabCars,
            ref BufferLookup<LaneOverlap> overlapData, ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes)
        {
            int next = m_Stretch.Next;
            int count = m_Stretch.Spots.Count;
            float hold = float.MaxValue;
            if (!m_BoxLane && next > 0 && next <= count)
            {
                hold = CrossingZebraHold(lane, overlaps, ref elements, layout, next - 1, length, margin, inverse, car, currentSpeed, safeTimeStep,
                    speedRange, currentMax, self, ref creatures, ref carLanes, ref laneObjects, ref curves, ref moving, ref controllers, ref prefabRefs,
                    ref geometries, ref prefabCars, ref overlapData, ref pedestrianLanes);
            }
            if (next < count)
            {
                DriveUpScan(lane, overlaps, ref elements, layout, next, length, margin, inverse, self, safeTimeStep, ref creatures, ref carLanes,
                    ref laneObjects, ref curves, ref moving, ref controllers, ref prefabRefs, ref geometries, ref prefabCars, ref overlapData, ref pedestrianLanes);
            }
            return hold;
        }

        /// <summary>The section being crossed (see <see cref="DriveUpZebras"/> (1)).</summary>
        private float CrossingZebraHold(Entity lane, DynamicBuffer<LaneOverlap> overlaps, ref ZebraElements elements, in ZebraLayout layout, int c,
            float length, float margin, bool inverse, CarData car, float currentSpeed, float safeTimeStep, Bounds1 speedRange, float currentMax,
            Entity self, ref ComponentLookup<Game.Creatures.Creature> creatures, ref ComponentLookup<Game.Net.CarLane> carLanes,
            ref BufferLookup<LaneObject> laneObjects, ref ComponentLookup<Curve> curves, ref ComponentLookup<Moving> moving,
            ref ComponentLookup<Controller> controllers, ref ComponentLookup<PrefabRef> prefabRefs, ref ComponentLookup<ObjectGeometryData> geometries,
            ref ComponentLookup<CarData> prefabCars, ref BufferLookup<LaneOverlap> overlapData, ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes)
        {
            if (!layout.HasZebra(c)) return float.MaxValue;
            float za = 2f;
            for (int i = 0; i < overlaps.Length; i++)
            {
                elements.Get(i, out int kind, out float s, out _);
                if ((kind & ZebraSections.Zebra) == 0 || !(s < za) || m_Stretch.Spots.StretchOf(s) != c || m_Stretch.FrontPast(s)) continue;
                za = s;
            }
            if (za > 1f || !m_Stretch.CanStopBefore(za)) return float.MaxValue;
            m_CrossSection = c;
            m_CrossZebra = za;
            for (int i = 0; i < m_ZebraStarts.Count; i++)
            {
                float s = m_ZebraStarts[i];
                if (!(s > za + WholePathRule.SpotEpsilon) || m_Stretch.Spots.StretchOf(s) != c) continue;
                float prev = ZebraSections.PreviousEnd(ref elements, false, s);
                if (!CrossingPoint.Fits(length, s, prev, margin, m_VehicleLength)) m_CrossNoFit |= 1 << i;
            }
            bool people = false;
            for (int i = 0; i < overlaps.Length && !people; i++)
            {
                elements.Get(i, out int kind, out float s, out _);
                if ((kind & ZebraSections.Zebra) == 0 || s < za - WholePathRule.SpotEpsilon || m_Stretch.Spots.StretchOf(s) != c) continue;
                people = ZebraPeople(overlaps[i], ref creatures, ref laneObjects, ref moving, ref curves, out _) != ExitZebraWait.None;
            }
            float wait = m_Stretch.Wait(za);
            if (!ZebraDriveUp.RoomAgainAtZebra(people, currentSpeed < Stage2Counters.StandingSpeed, wait + margin, margin)) return float.MaxValue;
            float hold = MathUtils.Clamp(VehicleUtils.GetMaxBrakingSpeed(car, wait, safeTimeStep), speedRange);
            if (!(hold < currentMax)) return float.MaxValue;
            // the zebra rule's own room scan (moving credit, as at the spot): the car is already inside and must not wait longer than that
            if (HasRoomPast(lane, math.min(layout.SectionEnd(c), 1f), inverse, length, -1f, Entity.Null, self, safeTimeStep, ref carLanes, ref laneObjects,
                    ref curves, ref moving, ref controllers, ref prefabRefs, ref geometries, ref prefabCars, ref overlapData, ref pedestrianLanes))
            {
                return float.MaxValue;
            }
            if (Counting) Min(ref m_HasZebraRoom, ref m_ZebraRoomSpeed, hold);
            WhyNoteRoom(WhyRule.JrZebraSection, wait);
            return hold;
        }

        /// <summary>The sections still ahead (see <see cref="DriveUpZebras"/> (2)).</summary>
        private void DriveUpScan(Entity lane, DynamicBuffer<LaneOverlap> overlaps, ref ZebraElements elements, in ZebraLayout layout, int next,
            float length, float margin, bool inverse, Entity self, float safeTimeStep, ref ComponentLookup<Game.Creatures.Creature> creatures,
            ref ComponentLookup<Game.Net.CarLane> carLanes, ref BufferLookup<LaneObject> laneObjects, ref ComponentLookup<Curve> curves,
            ref ComponentLookup<Moving> moving, ref ComponentLookup<Controller> controllers, ref ComponentLookup<PrefabRef> prefabRefs,
            ref ComponentLookup<ObjectGeometryData> geometries, ref ComponentLookup<CarData> prefabCars, ref BufferLookup<LaneOverlap> overlapData,
            ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes)
        {
            float first = 2f;
            int section = -1;
            for (int i = 0; i < overlaps.Length; i++)
            {
                elements.Get(i, out int kind, out float s, out _);
                if ((kind & ZebraSections.Zebra) == 0 || !(s < first)) continue;
                int sec = m_Stretch.Spots.StretchOf(s);
                if (sec < next || ZebraPeople(overlaps[i], ref creatures, ref laneObjects, ref moving, ref curves, out _) == ExitZebraWait.None) continue;
                first = s;
                section = sec;
            }
            // nobody on a zebra ahead, or the first zebra with people starts its own section (already judged right before it)
            if (section < 0 || !ZebraDriveUp.MergedZebra(first, m_Stretch.Spots[section])) return;
            m_DriveUpSection = section;
            m_DriveUpStart = first;
            float prev = ZebraSections.PreviousEnd(ref elements, false, first);
            bool fits = ZebraDriveUp.FitsClearOfZebras(prev > 0f, length, first, prev, margin, m_VehicleLength);
            // JR-T-035 (F1): only the Whole junction rule has the safeguard (the gap check over the window); Stock = the spot, as 1.3.0
            ZebraDriveUp.Reason r = ZebraDriveUp.Eligible(RuleOn, m_BoxLane, fits, m_Stretch.CanStopBefore(first), true);
            float end = math.min(layout.SectionEnd(section), 1f);
            if (r == ZebraDriveUp.Reason.None && !RoomFreeNow(lane, end, inverse, length, self, safeTimeStep, ref carLanes, ref laneObjects, ref curves,
                    ref moving, ref controllers, ref prefabRefs, ref geometries, ref prefabCars, ref overlapData, ref pedestrianLanes))
            {
                r = ZebraDriveUp.Reason.ExitBlocked;
            }
            m_DriveUpReason = (byte)r;
            if (r != ZebraDriveUp.Reason.None) return;
            // the estimated wait: everyone on the target and the later zebras of its section (the car waits before the target for them all)
            float wait = 0f;
            int worst = ExitZebraWait.None;
            for (int i = 0; i < overlaps.Length; i++)
            {
                elements.Get(i, out int kind, out float s, out _);
                if ((kind & ZebraSections.Zebra) == 0 || s < first - WholePathRule.SpotEpsilon || m_Stretch.Spots.StretchOf(s) != section) continue;
                int p = ZebraPeople(overlaps[i], ref creatures, ref laneObjects, ref moving, ref curves, out float w);
                if (p == ExitZebraWait.None) continue;
                wait = ZebraDriveUp.WaitSeconds(wait, w);
                worst = ExitZebraWait.Worse(worst, p);
            }
            // JR-T-035 (F4): everyone already past the car's way and nobody standing: they cannot hold the car, so no drive-up
            // (today's distance and the normal gap time)
            if (!ZebraDriveUp.PeopleCanHold(wait, worst == ExitZebraWait.Stationary)) return;
            m_DriveUp = true;
            m_DriveUpWait = wait;
            // the arrival time at the zebra and, from standstill there, the time to move past the rest of the section and the whole vehicle
            m_DriveUpDeltaZebra = Delta(first);
            GameMotion motion = default;
            motion.m_Car = m_Car;
            float clear = (end - first) * length + margin + m_VehicleLength;
            m_DriveUpClear = ArrivalTime.Compute(ref motion, clear > 0f ? clear : 0f, 0f, m_DriveSpeed, 0f, m_TimeStep);
        }

        /// <summary>
        /// Room for the whole vehicle plus the margin past <paramref name="fromDriving"/>, free now (no credit for a moving queue,
        /// no look-past): the car will stand before the zebra, so the room must already be there. The scan's side effects on the
        /// hold's blocker and why note are undone (this is a verdict, not a hold).
        /// </summary>
        private bool RoomFreeNow(Entity lane, float fromDriving, bool inverse, float length, Entity self, float safeTimeStep,
            ref ComponentLookup<Game.Net.CarLane> carLanes, ref BufferLookup<LaneObject> laneObjects, ref ComponentLookup<Curve> curves,
            ref ComponentLookup<Moving> moving, ref ComponentLookup<Controller> controllers, ref ComponentLookup<PrefabRef> prefabRefs,
            ref ComponentLookup<ObjectGeometryData> geometries, ref ComponentLookup<CarData> prefabCars, ref BufferLookup<LaneOverlap> overlapData,
            ref ComponentLookup<Game.Net.PedestrianLane> pedestrianLanes)
        {
            Entity blocker = m_LastBlocker, light = m_LastLightLane;
            float roomShort = m_WhyRoomShort;
            byte refusal = m_WhyRefusal;
            bool freeNowHeld = m_FreeNowHeld;
            bool room = HasRoomPast(lane, fromDriving, inverse, length, -1f, Entity.Null, self, safeTimeStep, ref carLanes, ref laneObjects, ref curves,
                ref moving, ref controllers, ref prefabRefs, ref geometries, ref prefabCars, ref overlapData, ref pedestrianLanes, freeNow: true, lookPast: false);
            m_LastBlocker = blocker;
            m_LastLightLane = light;
            m_WhyRoomShort = roomShort;
            m_WhyRefusal = refusal;
            m_FreeNowHeld = freeNowHeld;
            return room;
        }

        /// <summary>
        /// Everyone on a zebra's pedestrian lane, classified as the box exit zebra wait does (Core ExitZebraWait.Person: creatures
        /// only, G23 waiting area excluded, a standing person only on or beside the car's way), and the estimated seconds until
        /// they have left the car's way (Core ZebraDriveUp.PersonSeconds, the slowest).
        /// </summary>
        private int ZebraPeople(LaneOverlap o, ref ComponentLookup<Game.Creatures.Creature> creatures, ref BufferLookup<LaneObject> laneObjects,
            ref ComponentLookup<Moving> moving, ref ComponentLookup<Curve> curves, out float wait)
        {
            wait = 0f;
            if (!laneObjects.TryGetBuffer(o.m_Other, out DynamicBuffer<LaneObject> objects) || objects.Length == 0) return ExitZebraWait.None;
            bool hasCurve = curves.TryGetComponent(o.m_Other, out Curve zc);
            float zebraLength = hasCurve ? zc.m_Length : 0f;
            float2 range = math.float2(o.m_OtherStart, o.m_OtherEnd) * WholePathRule.ByteToOffset;
            int people = ExitZebraWait.None;
            for (int i = 0; i < objects.Length; i++)
            {
                Entity obj = objects[i].m_LaneObject;
                bool hasMoving = moving.TryGetComponent(obj, out Moving m);
                float pos = objects[i].m_CurvePosition.x;
                int p = ExitZebraWait.Person(creatures.HasComponent(obj), hasMoving, hasMoving ? math.lengthsq(m.m_Velocity) : 0f, m_NoPushPeople,
                    CrossingPush.InWaitZone(pos, zebraLength), ExitZebraWait.OnCarsWay(pos, range.x, range.y, zebraLength));
                if (p == ExitZebraWait.None) continue;
                float along = hasMoving && hasCurve ? math.dot(m.m_Velocity, math.normalizesafe(MathUtils.Tangent(zc.m_Bezier, pos))) : 0f;
                wait = ZebraDriveUp.WaitSeconds(wait, ZebraDriveUp.PersonSeconds(p == ExitZebraWait.Stationary, pos, range.x, range.y, zebraLength, along));
                people = ExitZebraWait.Worse(people, p);
            }
            return people;
        }

        /// <summary>
        /// PedestrianDistance for a zebra of the zebra sections (m_OvZebra) with a drive-up pending or a crossing section with
        /// later zebras the car would not fit clear of: the stop right before the target / first zebra ahead; else today's.
        /// </summary>
        private float DriveUpPedestrianDistance(float today)
        {
            int sec = m_Stretch.Spots.StretchOf(m_OvStart);
            if (m_DriveUp && m_OvPhase == PointPhase.Deciding)
            {
                float d = ZebraDriveUp.PedestrianDistance(true, sec == m_DriveUpSection, m_OvStart, m_DriveUpStart, m_Stretch.Wait(m_DriveUpStart), today);
                if (sec == m_DriveUpSection && m_OvStart >= m_DriveUpStart - WholePathRule.SpotEpsilon) m_OvDriveUpZebra = true;
                return d;
            }
            if (m_CrossNoFit != 0 && sec == m_CrossSection && (m_CrossNoFit & ZebraBit(m_OvStart)) != 0)
            {
                float d = ZebraDriveUp.CrossingDistance(true, m_Stretch.Wait(m_CrossZebra), today);
                if (d != today) m_OvDriveUpZebra = true;
                return d;
            }
            return today;
        }

        private readonly int ZebraBit(float u)
        {
            for (int i = 0; i < m_ZebraStarts.Count; i++)
            {
                float d = m_ZebraStarts[i] - u;
                if (d < WholePathRule.SpotEpsilon && d > -WholePathRule.SpotEpsilon) return 1 << i;
            }
            return 0;
        }

        // ---- why slowed (JR-T-025 ids; JR-T-033 adds jr_people_wait_zebra and the reason of jr_people_wait_spot) ----

        /// <summary>S2-08b: the rule of a people hold (atSpot = the distance was changed to a spot).</summary>
        public WhyRule WhyPeople(bool atSpot) =>
            m_OvDriveUpZebra ? WhyRule.JrPeopleWaitZebra : atSpot ? WhyRule.JrPeopleWaitSpot : WhyRule.StockPedestrian;

        /// <summary>S2-08b: the extra of a people hold at the spot: why not right before the zebra (ZebraDriveUp.Reason), else NaN.</summary>
        public float WhyPeopleExtra(bool atSpot)
        {
            if (!atSpot || m_OvDriveUpZebra) return float.NaN;
            if (!m_OvZebra) return (float)ZebraDriveUp.Reason.NotZebraSection;
            if (m_DriveUpReason != 0 && m_Stretch.Spots.StretchOf(m_OvStart) == m_DriveUpSection) return m_DriveUpReason;
            return float.NaN;
        }

        /// <summary>Gap holds (S2-06, S2-11b, S2-12): with a drive-up pending the gap hold of its section is the people's spot wait, reason traffic.</summary>
        public WhyRule WhyGap(WhyRule r) => m_OvDriveUp && r == WhyRule.JrWholeJunctionGap ? WhyRule.JrPeopleWaitSpot : r;

        /// <summary>The extra that goes with <see cref="WhyGap"/>.</summary>
        public float WhyGapExtra(WhyRule r, float extra) =>
            m_OvDriveUp && r == WhyRule.JrWholeJunctionGap ? (float)ZebraDriveUp.Reason.Traffic : extra;
    }
}
