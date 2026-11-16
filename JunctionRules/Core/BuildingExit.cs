using System;
using System.Collections.Generic;

namespace JunctionRules.Core
{
    // 0.4.3-probe (JR-T-007c, owner rulings 16 Nov 2026): a building's CAR EXITS, where its vehicles leave the building
    // onto the public road ("if I said entry/exit, just focus on exit from now on"). Game-independent: Boxes/BoxFacts.cs
    // reads the game data into ExitCandidate rows, the rules below decide what is an exit, how exits are keyed in the
    // save, and how saved keys find their exit again. Unit-tested (tests ExitTests).
    //
    // Where a car exit is in the game (decompile ~/Code/decomp/yb-2026-11-13/full):
    // 1. Driveway (sub-net node joined to the road's middle). A building's prefab sub-nets (BuildingPrefab SubNet, e.g.
    //    UK Medium Detached 14: one "Invisible Car Path - 1xTwoway", local (-9.40, 15.52) -> (-9.74, 23.71)) become edge
    //    entities owned by the building (Game.Common.Owner; listed in the building's Game.Net.SubNet buffer). A sub-net node
    //    with LocalConnectData near a road is joined to that road edge's MIDDLE: GenerateEdgesSystem.cs 1240-1261 and
    //    1846-1897 add ConnectedNode(node, t) to the road edge, t = MathUtils.Distance(road curve xz, node xz) (the
    //    nearest point on the road's centre curve); ReferencesSystem.cs 773-780 then lists the road edge in the node's
    //    ConnectedEdge buffer although the road neither starts nor ends there. LaneSystem.cs 645-653 + 2146-2216
    //    (CreateEdgeConnectionLanes -> CreateCarEdgeConnections) lay the car connection lanes from that node into the
    //    road's lanes; sourceBuffer = lanes flowing INTO the node (isSource: true, cars leaving the building).
    //    => exit = the road edge at ConnectedNode.m_CurvePosition, when a car lane of the building's sub-net edge at that
    //    node flows toward the node (a two-way drive counts; an entry-only lane does not).
    // 2. Junction (owner ruling 3): a building's own access ROAD (sub-net edge with Game.Net.Road, owned by the building)
    //    whose node is also the start / end node of a public road edge. That node is a real junction: boxed as a junction
    //    (Core/BoxSelection.JunctionOk "building access junction"), the building road arm yielding unless signed.
    // 3. Spawn location: a car / cargo / parking spawn location among the building's sub-objects (Game.Objects.SpawnLocation;
    //    prefab SpawnLocationData.m_ConnectionType Road / Cargo / Parking with RoadTypes.Car) connects to the nearest car
    //    lane: the building's own lanes first, then any lane within 32 m, else the building road edge
    //    (SpawnLocationConnectionSystem.cs 398-448 TryFindLanes, 590-615). Only a spawn location whose m_ConnectedLane1 is a
    //    lane of a PUBLIC road edge is an exit of its own (one on the building's own lanes leaves through 1. or 2.).
    // 4. Fallback, only when 1-3 found nothing (logged): Building.m_RoadEdge + m_CurvePosition, the point nearest the
    //    middle of the lot front (RoadConnectionSystem.cs 455-509): the 0.4.0-0.4.2 entrance position.

    public enum ExitKind : byte
    {
        None = 0,
        /// <summary>Building sub-net node joined to a public road's middle (ConnectedNode): the drawn drive.</summary>
        Driveway = 1,
        /// <summary>Car / cargo / parking spawn location connected to a public road lane.</summary>
        SpawnLocation = 2,
        /// <summary>The building's own access road meets a public road at a node: boxed as a junction on that node.</summary>
        Junction = 3,
        /// <summary>No exit found in the game data: Building.m_RoadEdge + m_CurvePosition (the 0.4.0 entrance point).</summary>
        Fallback = 4,
    }

    /// <summary>
    /// How a saved exit box names its exit: the kind and the exit's position in the BUILDING's own frame (metres right of
    /// / ahead of the building position, 0.1 m units). Survives save / load (entity ids change on every load) and a
    /// road rebuilt under the drive (new road entity, same building); the prefab's sub-net node is at a fixed local spot.
    /// 5 bytes in the save. Not used for junction exits (those boxes live on the junction node).
    /// </summary>
    public struct ExitKey : IEquatable<ExitKey>
    {
        public byte Kind;
        public short X10, Z10;

        public ExitKey(ExitKind kind, double localX, double localZ)
        {
            Kind = (byte)kind;
            X10 = Clamp16(localX * 10.0);
            Z10 = Clamp16(localZ * 10.0);
        }

        public ExitKind ExitKind => (ExitKind)Kind;
        public double X => X10 / 10.0;
        public double Z => Z10 / 10.0;

        private static short Clamp16(double v)
        {
            if (double.IsNaN(v)) return 0;
            return (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(v)));
        }

        public static double Distance(ExitKey a, ExitKey b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

        public bool Equals(ExitKey o) => Kind == o.Kind && X10 == o.X10 && Z10 == o.Z10;
        public override bool Equals(object obj) => obj is ExitKey k && Equals(k);
        public override int GetHashCode() => (Kind << 24) ^ (X10 << 12) ^ Z10;
        public override string ToString() => $"{ExitKind}@({X:0.0}, {Z:0.0})";
    }

    /// <summary>One exit as BoxFacts read it (game-independent copy for the rules and the log).</summary>
    public struct ExitCandidate
    {
        public ExitKind Kind;
        public ExitKey Key;
        /// <summary>Opaque id of the public road edge (driveway / spawn / fallback) or of the junction node (junction).</summary>
        public long RoadId;
        /// <summary>Metres along the road's centre curve (driveway / spawn / fallback).</summary>
        public double AlongM;
        /// <summary>World position of the exit on the road (x, z).</summary>
        public V2 World;
        /// <summary>Where it came from, for the log (entity ids, buffer, curve position).</summary>
        public string Source;
    }

    public static class ExitRules
    {
        /// <summary>Two exits on the same road closer than this (m along it) are one exit (first kept: driveway before spawn).</summary>
        public const double SameExitAlong = 3.0;
        /// <summary>A saved key finds its exit when one of the same kind lies within this distance (m, building frame).</summary>
        public const double KeyMatch = 2.0;
        /// <summary>Most exit boxes one building's component holds (the save record caps at 50, the component at this).</summary>
        public const int MaxBoxesPerBuilding = 10;

        /// <summary>
        /// Exit list in a fixed order: driveways, spawn locations, junctions, each by road then position along it; spawn
        /// locations within SameExitAlong of a driveway on the same road dropped (the same drive). A fallback is kept only
        /// when nothing else is left.
        /// </summary>
        public static List<ExitCandidate> Normalise(IEnumerable<ExitCandidate> found)
        {
            var all = new List<ExitCandidate>();
            if (found != null) all.AddRange(found);
            all.Sort((a, b) =>
            {
                int c = Order(a.Kind).CompareTo(Order(b.Kind));
                if (c != 0) return c;
                c = a.RoadId.CompareTo(b.RoadId);
                if (c != 0) return c;
                c = a.AlongM.CompareTo(b.AlongM);
                return c != 0 ? c : a.Key.X10.CompareTo(b.Key.X10);
            });
            var r = new List<ExitCandidate>();
            foreach (var e in all)
            {
                if (e.Kind == ExitKind.None) continue;
                bool dup = false;
                foreach (var k in r)
                {
                    if (e.Kind == ExitKind.Junction || k.Kind == ExitKind.Junction) { dup |= e.Kind == k.Kind && e.RoadId == k.RoadId; continue; }
                    if (k.RoadId == e.RoadId && Math.Abs(k.AlongM - e.AlongM) < SameExitAlong && (e.Kind != ExitKind.Fallback || k.Kind == ExitKind.Fallback)) dup = true;
                }
                if (!dup) r.Add(e);
            }
            bool real = r.Exists(x => x.Kind != ExitKind.Fallback);
            if (real) r.RemoveAll(x => x.Kind == ExitKind.Fallback);
            return r;
        }

        private static int Order(ExitKind k) => k == ExitKind.Driveway ? 0 : k == ExitKind.SpawnLocation ? 1 : k == ExitKind.Junction ? 2 : 3;

        /// <summary>
        /// The exit a saved key stands for: same kind within KeyMatch (m, building frame), nearest first. A key whose kind
        /// has changed (e.g. a fallback key once the game shows a real drive) takes the nearest non-junction exit within
        /// KeyMatch. -1 when none (the caller drops the key with a log line).
        /// </summary>
        public static int Match(ExitKey key, IList<ExitCandidate> exits)
        {
            int best = -1;
            double bestD = double.MaxValue;
            for (int pass = 0; pass < 2 && best < 0; pass++)
            {
                for (int i = 0; i < exits.Count; i++)
                {
                    ExitCandidate e = exits[i];
                    if (e.Kind == ExitKind.Junction) continue;
                    if (pass == 0 && e.Kind != key.ExitKind) continue;
                    double d = ExitKey.Distance(key, e.Key);
                    if (d <= KeyMatch && d < bestD) { bestD = d; best = i; }
                }
            }
            return best;
        }

        /// <summary>
        /// 0.4.0-0.4.2 entrance box (no key) -> the exit nearest the old box's centre (the old access point, world x / z):
        /// any kind, junction exits included (that box then moves onto the junction node). -1 = no exit at all: the old
        /// box is dropped (logged). 'why' says what was chosen.
        /// </summary>
        public static int Migrate(V2 oldAccess, IList<ExitCandidate> exits, out string why)
        {
            int best = -1;
            double bestD = double.MaxValue;
            for (int i = 0; i < exits.Count; i++)
            {
                double d = V2.Distance(oldAccess, exits[i].World);
                if (d < bestD) { bestD = d; best = i; }
            }
            why = best < 0
                ? "the building has no car exit and no road access any more"
                : $"nearest of {exits.Count} exit(s): {exits[best].Kind} {bestD:0.0} m from the old box centre ({exits[best].Source})";
            return best;
        }

        /// <summary>
        /// Which junction arms yield (owner ruling 3: "building road arm = yielding unless signed otherwise"): the arms'
        /// own signs (CarLaneFlags.Stop / Yield) stand; with signals all arms are equal (no change); without signals and
        /// without any signed arm, every arm that is the building's own access road yields. When some public arm carries a
        /// STOP / Give Way and the building arm none, the signs say the building road has priority: kept as signed.
        /// </summary>
        public static bool[] ArmYield(IList<bool> signedYield, IList<bool> buildingArm, bool signals)
        {
            int n = signedYield.Count;
            var r = new bool[n];
            bool anySigned = false;
            for (int i = 0; i < n; i++) { r[i] = signedYield[i]; anySigned |= signedYield[i]; }
            if (signals || anySigned) return r;
            for (int i = 0; i < n; i++) if (i < buildingArm.Count && buildingArm[i]) r[i] = true;
            return r;
        }

        /// <summary>Building frame of a world point: metres to the building's right (local x) and ahead (local z, toward the lot front).</summary>
        public static V2 ToLocal(V2 world, V2 buildingPos, V2 forward)
        {
            V2 f = forward.Normalized;
            V2 right = new V2(f.Z, -f.X);   // Unity: local +x = forward rotated -90 deg about +y (quaternion.RotateY)
            V2 d = world - buildingPos;
            return new V2(V2.Dot(d, right), V2.Dot(d, f));
        }

        /// <summary>World point of a building-frame point (inverse of ToLocal).</summary>
        public static V2 ToWorld(V2 local, V2 buildingPos, V2 forward)
        {
            V2 f = forward.Normalized;
            V2 right = new V2(f.Z, -f.X);
            return buildingPos + right * local.X + f * local.Z;
        }
    }

    /// <summary>Destination of decoded exit keys (the component wraps a FixedList; tests a List). No allocation in the game.</summary>
    public interface IExitKeySink { void Add(ExitKey key); }

    /// <summary>Source of exit keys for ExitRecord.Encode (the component wraps its FixedList; tests a List).</summary>
    public interface IExitKeySource { int Count { get; } ExitKey Get(int i); }

    public struct ExitKeyList : IExitKeySink, IExitKeySource
    {
        public List<ExitKey> Keys;
        public void Add(ExitKey key) { (Keys ??= new List<ExitKey>()).Add(key); }
        public int Count => Keys == null ? 0 : Keys.Count;
        public ExitKey Get(int i) => Keys[i];
    }

    /// <summary>
    /// 0.4.3-probe save record of a building's exit boxes (YellowBoxEntrance), extending BoxRecord's layout:
    ///   byte version = 2, byte length, payload: [0] flags, [1] count n, then n x 5 bytes (kind, x10 lo/hi, z10 lo/hi).
    /// Version 1 (0.4.0-0.4.2) = flags only: no keys = a legacy box, migrated on load (ExitRules.Migrate). 0.4.0-0.4.2
    /// builds reading a version 2 record take byte [0] and skip the rest by 'length' (BoxRecord.Decode), so the game's
    /// exact-size check holds either way. Never more than 50 keys (length &lt;= 255).
    /// </summary>
    public static class ExitRecord
    {
        public const byte Version = 2;
        public const int MaxKeys = 50;

        public static void Encode<TSink, TKeys>(ref TSink sink, BoxRecordFlags flags, ref TKeys keys) where TSink : struct, IByteSink where TKeys : struct, IExitKeySource
        {
            int count = Math.Max(0, Math.Min(MaxKeys, keys.Count));
            sink.Put(Version);
            sink.Put((byte)(2 + 5 * count));
            sink.Put((byte)flags);
            sink.Put((byte)count);
            for (int i = 0; i < count; i++)
            {
                ExitKey k = keys.Get(i);
                sink.Put(k.Kind);
                sink.Put((byte)(k.X10 & 0xFF)); sink.Put((byte)((k.X10 >> 8) & 0xFF));
                sink.Put((byte)(k.Z10 & 0xFF)); sink.Put((byte)((k.Z10 >> 8) & 0xFF));
            }
        }

        /// <summary>
        /// Reads one record of any version, consuming exactly 2 + length bytes. Keys go to 'keys' (none for version 1 or a
        /// truncated list: a key needs all 5 bytes). Returns false for version 0 (never written).
        /// </summary>
        public static bool Decode<TSource, TKeys>(ref TSource source, ref TKeys keys, out BoxRecordFlags flags, out byte version)
            where TSource : struct, IByteSource where TKeys : struct, IExitKeySink
        {
            flags = BoxRecordFlags.None;
            version = source.Next();
            int length = source.Next();
            int count = 0;
            byte kind = 0, x0 = 0, x1 = 0, z0 = 0;
            for (int i = 0; i < length; i++)
            {
                byte b = source.Next();
                if (i == 0) { flags = (BoxRecordFlags)b; continue; }
                if (version < 2) continue;
                if (i == 1) { count = b; continue; }
                int j = (i - 2) / 5, o = (i - 2) % 5;
                if (j >= count) continue;
                switch (o)
                {
                    case 0: kind = b; break;
                    case 1: x0 = b; break;
                    case 2: x1 = b; break;
                    case 3: z0 = b; break;
                    case 4: keys.Add(new ExitKey { Kind = kind, X10 = (short)(x0 | (x1 << 8)), Z10 = (short)(z0 | (b << 8)) }); break;
                }
            }
            if (version == 0) { flags = BoxRecordFlags.None; return false; }
            return true;
        }
    }
}
