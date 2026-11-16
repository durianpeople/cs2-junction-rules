using System.Globalization;

namespace JunctionRules.Core
{
    /// <summary>
    /// 0.4.7 (JR-T-013a, gap G1): every way a vehicle hands over from a non-road lane to a public road lane, classified.
    /// The 0.4.6 timing lines showed "hand-overs to the road from: lot 0, gate 0, other 2..28": the hand-over real
    /// vehicles take was none of the two the exit rule judged. Decompile (~/Code/decomp/yb-2026-11-13/full):
    /// <list type="bullet">
    /// <item>Spawn point (the 'other'): Game.Pathfind/PathfindTargetSeeker.cs FindTargets (427-479) starts a building's
    /// path at its spawn locations; a Road-type spawn location adds the SpawnLocation OBJECT as the target (AddSpawnLocation,
    /// ~680 AddTarget(spawnLocationEntity)), so the first path element is an object with a Transform and the navigation
    /// lane gets CarLaneFlags.TransformTarget (Game.Simulation/CarNavigationSystem.cs 1078-1091). Its next element is the
    /// lane the spawn location is tied to: Game.Objects/SpawnLocationConnectionSystem.cs (535-620, TryFindLanes 400-448)
    /// takes the building's own sub-lanes first, else the nearest lane within 32 m, i.e. the PUBLIC road lane. The
    /// TransformTarget branch of the navigation update (copy ~1658-1668) only runs IterateParkingTarget / IterateTarget,
    /// never a road lane, and the hand-over loop (copy ~1903-1918 then ~2017+) moves it onto the road lane as soon as it
    /// reaches the spawn point; it stays Unspawned until then (Game.Vehicles/VehicleUtils.cs CheckUnspawned 1806-1821) and
    /// appears in the road lane with no gap and no box check. The spawn location's Owner is the building, so the 0.4.6
    /// probe counted it as 'other'.</item>
    /// <item>Parking (building-owned parking lane) and kerbside parking (a parking lane owned by the road edge): the
    /// path starts on the parking lane (ParkingSpace | FixedLane, CarNavigationSystem 1003-1023), the same branch as a
    /// spawn point, hand-over straight to the road lane. Kerbside is not building-owned: never counted in 0.4.6.</item>
    /// <item>Building lane: a car lane of a building's own road (sub-net) whose next lane is a public junction lane
    /// (Game.Net/LaneSystem.cs: a sub-net edge joined at a public node is only "isSideConnection" for its carriageway group,
    /// 5161 / 7033; no SideConnection / Yield flag, the node lanes belong to the public node).</item>
    /// <item>Drive: the SideConnection lane of a driveway (owned by the road edge; LaneSystem.cs CreateEdgeConnectionLane
    /// 3646+, flags 3868), judged in the lane speed iterator (S2-29) before the hand-over; counted only.</item>
    /// <item>Lot (Area lane) and gate (building Connection lane): judged since 0.4.5 / 0.4.4 (S2-32 / S2-30).</item>
    /// </list>
    /// Game-independent, Burst-compatible (static methods on plain values), unit-tested (tests ExitPathTests).
    /// </summary>
    public static class ExitPaths
    {
        /// <summary>Game.Vehicles.CarLaneFlags values (Game.Vehicles/CarLaneFlags.cs) the classification reads.</summary>
        public const uint NavTransformTarget = 0x8u, NavParkingSpace = 0x10u, NavConnection = 0x1000000u, NavArea = 0x4000000u;

        public enum Path : byte
        {
            /// <summary>Not a way out of a building / parking (road to road, outside connection, ...): not counted.</summary>
            None = 0,
            /// <summary>A lot's AREA lane (S2-32).</summary>
            Lot = 1,
            /// <summary>A building Connection lane (car park gate; S2-30).</summary>
            Gate = 2,
            /// <summary>0.4.7: a building's spawn location object (TransformTarget; S2-35). The 0.4.6 'other'.</summary>
            SpawnPoint = 3,
            /// <summary>0.4.7: a building-owned parking lane (ParkingSpace; S2-35).</summary>
            Parking = 4,
            /// <summary>0.4.7: a kerbside parking lane of the road itself (ParkingSpace; S2-35): "give way when moving off".</summary>
            KerbParking = 5,
            /// <summary>0.4.7: a car lane of a building's own road joining a public junction lane (S2-36).</summary>
            BuildingLane = 6,
            /// <summary>A driveway's SideConnection lane (judged by S2-29 in the lane speed iterator, before the hand-over).</summary>
            Drive = 7,
            /// <summary>Any other building-owned lane (a way out the rule does not judge; expected 0).</summary>
            Other = 8,
        }

        public const int PathCount = 9;

        /// <summary>The kind of lane a hand-over comes from (trace text; also tells which 'other' remains).</summary>
        public enum LaneKind : byte
        {
            Unknown = 0,
            AreaLane = 1,
            ConnectionLane = 2,
            SpawnObject = 3,
            ParkingLane = 4,
            CarLane = 5,
            DriveLane = 6,
            NoCarLane = 7,
        }

        /// <summary>
        /// The lane a vehicle hands over from (its current navigation lane flags and what the lane is). The next lane must
        /// already be a public road car lane (the caller checks). fromBuilding = the lane (or the spawn object) belongs to a
        /// building (owner chain).
        /// </summary>
        public static Path Classify(uint navFlags, bool fromBuilding, bool isCarLane, bool isSideConnection)
        {
            if ((navFlags & NavArea) != 0) return Path.Lot;
            // A parking connection (garage: Connection | ParkingSpace, CarNavigationSystem 1026-1043) takes the stock
            // TransformTarget / ParkingSpace branch, so it is judged as parking (S2-35), not as a gate.
            if ((navFlags & NavParkingSpace) != 0) return fromBuilding ? Path.Parking : Path.KerbParking;
            if ((navFlags & NavConnection) != 0) return fromBuilding ? Path.Gate : Path.None;
            if ((navFlags & NavTransformTarget) != 0) return fromBuilding ? Path.SpawnPoint : Path.None;
            if (isCarLane && isSideConnection) return Path.Drive;
            if (!fromBuilding) return Path.None;
            return isCarLane ? Path.BuildingLane : Path.Other;
        }

        public static LaneKind Kind(uint navFlags, bool isCarLane, bool isSideConnection)
        {
            if ((navFlags & NavArea) != 0) return LaneKind.AreaLane;
            if ((navFlags & NavParkingSpace) != 0) return LaneKind.ParkingLane;
            if ((navFlags & NavConnection) != 0) return LaneKind.ConnectionLane;
            if ((navFlags & NavTransformTarget) != 0) return LaneKind.SpawnObject;
            if (isCarLane) return isSideConnection ? LaneKind.DriveLane : LaneKind.CarLane;
            return LaneKind.NoCarLane;
        }

        /// <summary>The exit rule decides this path at the hand-over (0.4.7: every real way out; Drive earlier in the iterator).</summary>
        public static bool JudgedAtHandOver(Path p) =>
            p == Path.Lot || p == Path.Gate || p == Path.SpawnPoint || p == Path.Parking || p == Path.KerbParking || p == Path.BuildingLane;

        /// <summary>The hand-over counter of a path (Stage2Counters), -1 for None.</summary>
        public static int Counter(Path p)
        {
            switch (p)
            {
                case Path.Lot: return Stage2Counters.ExitJoinArea;
                case Path.Gate: return Stage2Counters.ExitJoinConnection;
                case Path.SpawnPoint: return Stage2Counters.ExitJoinSpawnPoint;
                case Path.Parking: return Stage2Counters.ExitJoinParking;
                case Path.KerbParking: return Stage2Counters.ExitJoinKerbParking;
                case Path.BuildingLane: return Stage2Counters.ExitJoinBuildingLane;
                case Path.Drive: return Stage2Counters.ExitJoinDrive;
                case Path.Other: return Stage2Counters.ExitJoinOther;
                default: return -1;
            }
        }

        /// <summary>
        /// 0.4.7 cap policy. A LANE cap hit (the scan reached its lane budget, far feeding lanes not read) errs to go, as
        /// before: the lane budget is fixed by the geometry, erring "busy" would hold that exit for ever. A VEHICLE cap hit
        /// (more relevant vehicles in reach than the budget: only vehicles before the conflict and within the scan distance
        /// count since 0.4.7) means dense traffic: busy (wait). 0.4.4-0.4.6 counted every vehicle on a read lane against the
        /// budget, also those past the conflict or far away, and erred to go: a long busy road made the exit cut out.
        /// </summary>
        public static bool CapBusy(bool laneCapHit, bool vehicleCapHit) => vehicleCapHit;

        /// <summary>A vehicle on a read lane counts against the vehicle budget only when it is still before the conflict end and within reach.</summary>
        public static bool CountsAgainstBudget(float rear, float conflictEnd, float gapToConflict) =>
            rear < conflictEnd && gapToConflict <= ExitGiveWay.MaxScanMetres;

        /// <summary>
        /// 0.4.7 review (G1 F2): as above, and a vehicle standing still before the conflict (not on it) does not count
        /// either: <see cref="ExitGiveWay.VehicleBlocks"/> never lets it block, so a standing queue of more than the budget
        /// on a road lane (a jam that stopped short of the exit) must not turn into "dense traffic: wait" for ever.
        /// speed = its speed along the lane (m/s, &gt;= 0).
        /// </summary>
        public static bool CountsAgainstBudget(float rear, float conflictEnd, float gapToConflict, float speed) =>
            CountsAgainstBudget(rear, conflictEnd, gapToConflict) && !(gapToConflict > 0f && !(speed > 0f));

        public static string Name(Path p)
        {
            switch (p)
            {
                case Path.Lot: return "lot";
                case Path.Gate: return "gate";
                case Path.SpawnPoint: return "spawn point";
                case Path.Parking: return "building parking";
                case Path.KerbParking: return "kerbside parking";
                case Path.BuildingLane: return "building road";
                case Path.Drive: return "drive";
                case Path.Other: return "other";
                default: return "none";
            }
        }

        public static string Name(LaneKind k)
        {
            switch (k)
            {
                case LaneKind.AreaLane: return "area lane";
                case LaneKind.ConnectionLane: return "connection lane";
                case LaneKind.SpawnObject: return "spawn location object";
                case LaneKind.ParkingLane: return "parking lane";
                case LaneKind.CarLane: return "car lane";
                case LaneKind.DriveLane: return "drive lane";
                case LaneKind.NoCarLane: return "lane without car lane data";
                default: return "unknown lane";
            }
        }
    }

    /// <summary>
    /// 0.4.7 (G1): setting "Exit trace (log)": one log line per leaving vehicle event, written by the navigation job into a
    /// small ring (Stage2/JRStage2Bridge.cs) and logged on the main thread at most <see cref="MaxLinesPerSecond"/> lines a
    /// second. Two events per vehicle: its first wait at an exit (episode, Core SafetyStopEvents) and its hand-over to the
    /// road lane. Record layout (longs): [0] sequence stamp (written last), [1] vehicle (index | version &lt;&lt; 32),
    /// [2] building (index | version &lt;&lt; 32, 0 = none), [3] packed facts (<see cref="Pack"/>), [4] frame | road lane owner index &lt;&lt; 32.
    /// </summary>
    public static class ExitTrace
    {
        public const int Words = 5;
        public const int Slots = 256;
        /// <summary>Ring length: one head counter + the slots.</summary>
        public const int RingLength = 1 + Slots * Words;
        public const int MaxLinesPerSecond = 20;

        public enum Event : byte
        {
            /// <summary>The vehicle starts waiting at an exit (once per wait episode).</summary>
            Wait = 0,
            /// <summary>The vehicle hands over from its exit lane to the road lane.</summary>
            HandOver = 1,
        }

        /// <summary>Why the decision came out as it did (bit flags).</summary>
        public const int ReasonDecided = 1, ReasonLaneCap = 2, ReasonVehicleCap = 4, ReasonEmergency = 8, ReasonNodeJoin = 16;

        /// <summary>Review F6 (0.4.7): a building road joining a signalled arm: the signal decides (S2-36 holds nothing there).</summary>
        public const int ReasonSignal = 32;

        /// <summary>Slot base index (into the ring) of sequence number seq (1-based).</summary>
        public static int SlotBase(long seq) => 1 + (int)((seq - 1) % Slots) * Words;

        public static long Pack(Event e, ExitPaths.Path path, ExitPaths.LaneKind kind, ExitGiveWay.Decision decision, int reasons, bool boxed) =>
            (long)(byte)e | ((long)(byte)path << 8) | ((long)(byte)kind << 16) | ((long)(byte)decision << 24) | ((long)(reasons & 0xFF) << 32) | (boxed ? 1L << 40 : 0L);

        public static void Unpack(long v, out Event e, out ExitPaths.Path path, out ExitPaths.LaneKind kind, out ExitGiveWay.Decision decision, out int reasons, out bool boxed)
        {
            e = (Event)(byte)v;
            path = (ExitPaths.Path)(byte)(v >> 8);
            kind = (ExitPaths.LaneKind)(byte)(v >> 16);
            decision = (ExitGiveWay.Decision)(byte)(v >> 24);
            reasons = (int)((v >> 32) & 0xFF);
            boxed = ((v >> 40) & 1) != 0;
        }

        public static long PackEntity(int index, int version) => (uint)index | ((long)version << 32);

        /// <summary>One log line (main thread).</summary>
        public static string Format(long vehicle, long building, long facts, long frameRoad)
        {
            Unpack(facts, out Event e, out ExitPaths.Path path, out ExitPaths.LaneKind kind, out ExitGiveWay.Decision decision, out int reasons, out bool boxed);
            CultureInfo ci = CultureInfo.InvariantCulture;
            string decided;
            if ((reasons & ReasonEmergency) != 0) decided = "go (on-duty emergency, stock)";
            else if ((reasons & ReasonDecided) == 0) decided = path == ExitPaths.Path.Drive ? "judged before the hand-over (driveway give-way)" : "not judged";
            else if (decision == ExitGiveWay.Decision.WaitGap) decided = (reasons & ReasonVehicleCap) != 0 ? "wait (dense traffic: vehicle cap)" : "wait (gap)";
            else if (decision == ExitGiveWay.Decision.WaitBox) decided = "wait (box: no room past)";
            else if ((reasons & ReasonSignal) != 0) decided = "go (signalled arm)";
            else decided = (reasons & ReasonLaneCap) != 0 ? "go (gap clear; lane cap hit, far lanes not read)" : "go (gap clear)";
            string bld = building == 0 ? "none" : Ent(building);
            return "exit trace: " + (e == Event.Wait ? "waits" : "joins road") + " vehicle " + Ent(vehicle) + " building " + bld
                   + " path " + ExitPaths.Name(path) + " (" + ExitPaths.Name(kind) + ")"
                   + " road owner " + ((int)(frameRoad >> 32)).ToString(ci) + ((reasons & ReasonNodeJoin) != 0 ? " (junction)" : "")
                   + " decision " + decided + " boxed " + (boxed ? "yes" : "no") + " frame " + ((uint)frameRoad).ToString(ci);
        }

        private static string Ent(long packed) => ((int)(uint)packed).ToString(CultureInfo.InvariantCulture) + ":" + ((int)(packed >> 32)).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Main-thread line budget: at most maxPerSecond lines in any one-second window; the rest are counted as dropped.</summary>
    public sealed class LineRateLimiter
    {
        private readonly int m_Max;
        private double m_WindowStart = double.NegativeInfinity;
        private int m_InWindow;

        public LineRateLimiter(int maxPerSecond) { m_Max = maxPerSecond < 1 ? 1 : maxPerSecond; }

        /// <summary>Lines dropped since the last <see cref="TakeDropped"/>.</summary>
        public long Dropped { get; private set; }

        /// <summary>True = log this line now (seconds = a monotonic clock).</summary>
        public bool Allow(double seconds)
        {
            if (seconds - m_WindowStart >= 1.0)
            {
                m_WindowStart = seconds;
                m_InWindow = 0;
            }
            if (m_InWindow >= m_Max)
            {
                Dropped++;
                return false;
            }
            m_InWindow++;
            return true;
        }

        public long TakeDropped()
        {
            long d = Dropped;
            Dropped = 0;
            return d;
        }
    }

    /// <summary>
    /// 0.4.7: the reader side of the trace ring (main thread, never waits for the job). seq numbers are 1-based; a record
    /// whose stamp is not yet its own sequence number is being written (read again next step) or was overwritten (lost).
    /// </summary>
    public sealed class ExitTraceReader
    {
        private long m_Next = 1;

        public long Lost { get; private set; }

        /// <summary>Review F5 (0.4.7): the next sequence number to read (records 1..Next-1 are done).</summary>
        public long Next => m_Next;

        /// <summary>Review F5: the ring head names a record not read yet (else the caller skips the drain).</summary>
        public static bool HasNew(long head, long next) => head >= next;

        /// <summary>
        /// Reads every finished record up to head. read(i) = the ring value at i. emit(vehicle, building, facts, frameRoad).
        /// Returns the number of records emitted.
        /// </summary>
        public int Drain(long head, System.Func<int, long> read, System.Action<long, long, long, long> emit)
        {
            int n = 0;
            if (head - (m_Next - 1) > ExitTrace.Slots)
            {
                long skip = head - ExitTrace.Slots + 1 - m_Next;
                Lost += skip;
                m_Next += skip;
            }
            while (m_Next <= head)
            {
                int b = ExitTrace.SlotBase(m_Next);
                long stamp = read(b);
                if (stamp < m_Next) break;               // still being written: next step
                if (stamp > m_Next) { Lost++; m_Next++; continue; }   // overwritten by a newer record
                long v = read(b + 1), bl = read(b + 2), f = read(b + 3), fr = read(b + 4);
                if (read(b) != m_Next) { Lost++; m_Next++; continue; }
                emit(v, bl, f, fr);
                n++;
                m_Next++;
            }
            return n;
        }

        public long TakeLost()
        {
            long l = Lost;
            Lost = 0;
            return l;
        }
    }
}
