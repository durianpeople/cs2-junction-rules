using System.Collections.Generic;
using System.Globalization;

namespace JunctionRules.Core
{
    /// <summary>
    /// JR-T-013b (G4): "watch one junction", a debug log of the holds at ONE junction node the owner picks (default off:
    /// no node, nothing recorded). The timing log only has city-wide counters, so it cannot say what happens at one
    /// junction. While a node is watched, the copy's navigation job writes one event per car-step for every car that
    /// stands (slower than <see cref="Stage2Counters.StandingSpeed"/>) at that junction: on one of its lanes, or
    /// checking one of its lanes ahead (approach). The event names the car, what the game says it waits for (its blocker
    /// and blocker type), the lane it is on and which rule set its binding speed limit (a Junction Rules hold, or the
    /// game's own). The main thread logs the events rate-limited (<see cref="WatchLimiter"/>).
    ///
    /// Shared layout (a persistent NativeArray of longs owned by the copy's bridge): slot 0 = watched node key (0 = off),
    /// slot 1 = write cursor (events ever claimed), then <see cref="Capacity"/> events of <see cref="EventLongs"/> longs:
    /// sequence stamp (written last, = event number + 1), car key, blocker key, current lane key, packed values.
    /// Pure helpers here (no Unity); the job side is Stage2/JRWholeJunction (Watch), the drain Stage2/JRStage2Bridge. Writer: claim event number n = Interlocked.Increment(slot 1) - 1, write the four values at Slot(n) + 1..4, then the stamp n + 1 at Slot(n) (Interlocked.Exchange).
    /// </summary>
    public static class JunctionWatch
    {
        public const int Header = 2;
        public const int EventLongs = 5;
        public const int Capacity = 1024;
        public const int Length = Header + Capacity * EventLongs;

        /// <summary>Which rule set the standing car's binding limit (order = the job's test order).</summary>
        public enum Rule
        {
            Game = 0,
            BoxSpotRoom = 1,
            BoxSpotWait = 2,
            BoxLine = 3,
            EntranceMain = 4,
            EntranceLeaving = 5,
            ZebraPeople = 6,
            ZebraRoom = 7,
            ZebraVehicles = 8,
            WaitLater = 9,
            InsideGiveWay = 10,
            InsideCrossing = 11,
            Safety = 12,
            Exit = 13,
        }

        public static string RuleName(int rule)
        {
            switch ((Rule)rule)
            {
                case Rule.Game: return "game's own (give-way / car ahead / signal, see blocker)";
                case Rule.BoxSpotRoom: return "yellow box: waits at its spot in the box, no room on its exit";
                case Rule.BoxSpotWait: return "yellow box: waits at its spot in the box for a live conflict";
                case Rule.BoxLine: return "yellow box: held at the line, no room past the box";
                case Rule.EntranceMain: return "entrance band: no room past the band (main road)";
                case Rule.EntranceLeaving: return "entrance band: no room past the band (leaving driveway)";
                case Rule.ZebraPeople: return "zebra: people on it";
                case Rule.ZebraRoom: return "zebra: no room past it";
                case Rule.ZebraVehicles: return "zebra: a vehicle conflict after it not clear";
                case Rule.WaitLater: return "whole junction: waits at its entry spot for a later conflict";
                case Rule.InsideGiveWay: return "whole junction: stopped inside for a give-way";
                case Rule.InsideCrossing: return "whole junction: stopped inside behind a crossing car";
                case Rule.Safety: return "whole junction: safety stop inside";
                case Rule.Exit: return "building exit give-way";
                default: return "rule " + rule.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Names of Game.Vehicles.BlockerType (decompile Game.Vehicles/BlockerType.cs 3-14, byte order).</summary>
        public static string BlockerTypeName(int type)
        {
            switch (type)
            {
                case 0: return "None";
                case 1: return "Continuing";
                case 2: return "Crossing";
                case 3: return "Signal";
                case 4: return "Temporary";
                case 5: return "Limit";
                case 6: return "Caution";
                case 7: return "Spawn";
                case 8: return "Oncoming";
                default: return "type " + type.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>0.4.11 (G23): the watch pick radius around the camera's aim point (m).</summary>
        public const double PivotRadius = 25.0;

        /// <summary>
        /// 0.4.11 (G23, owner: "pick the nearest node to the camera pivot within ~25 m"): the camera orbits a pivot on the
        /// ground, which the centre line of the view meets. For each node: the point where the centre line reaches the
        /// node's height, horizontal distance from the node to it; the nearest within <paramref name="radius"/> wins (-1 =
        /// none). A line that never comes down to the node's height (looking up / level) uses the closest approach instead.
        /// </summary>
        public static int PickNearPivot(V3 origin, V3 dir, List<V3> nodes, double radius, out double distance)
        {
            int best = -1;
            distance = double.MaxValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                V3 n = nodes[i];
                double d;
                if (dir.Y < -1e-6 && origin.Y > n.Y)
                {
                    double t = (n.Y - origin.Y) / dir.Y;
                    double hx = origin.X + dir.X * t, hz = origin.Z + dir.Z * t;
                    d = System.Math.Sqrt((hx - n.X) * (hx - n.X) + (hz - n.Z) * (hz - n.Z));
                }
                else
                {
                    double len = System.Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y + dir.Z * dir.Z);
                    if (!(len > 0)) continue;
                    double ux = dir.X / len, uy = dir.Y / len, uz = dir.Z / len;
                    double vx = n.X - origin.X, vy = n.Y - origin.Y, vz = n.Z - origin.Z;
                    double along = vx * ux + vy * uy + vz * uz;
                    if (along < 0) continue;
                    double px = vx - along * ux, py = vy - along * uy, pz = vz - along * uz;
                    d = System.Math.Sqrt(px * px + py * py + pz * pz);
                }
                if (d <= radius && d < distance) { distance = d; best = i; }
            }
            return best;
        }

        /// <summary>Entity (index, version) as one long; 0 = Entity.Null (index 0 is never a live entity).</summary>
        public static long Key(int index, int version) => index <= 0 ? 0L : ((long)index << 32) | (uint)version;

        public static int KeyIndex(long key) => (int)(key >> 32);

        public static int KeyVersion(long key) => (int)(key & 0xFFFFFFFFL);

        public static string EntityText(long key) =>
            key == 0 ? "none" : "Entity(" + KeyIndex(key).ToString(CultureInfo.InvariantCulture) + ":" + KeyVersion(key).ToString(CultureInfo.InvariantCulture) + ")";

        private static long Cm(float metresPerSecond)
        {
            float cm = metresPerSecond * 100f;
            if (!(cm > 0f)) return 0;
            return cm >= 65535f ? 65535 : (long)(cm + 0.5f);
        }

        /// <summary>rule (8 bits) | blocker type (8 bits) | speed cm/s (16 bits) | binding limit cm/s (16 bits).</summary>
        public static long Pack(int rule, int blockerType, float speed, float maxSpeed) =>
            ((long)rule & 0xFF) | ((long)(blockerType & 0xFF) << 8) | (Cm(speed) << 16) | (Cm(maxSpeed) << 32);

        public static void Unpack(long packed, out int rule, out int blockerType, out float speed, out float maxSpeed)
        {
            rule = (int)(packed & 0xFF);
            blockerType = (int)((packed >> 8) & 0xFF);
            speed = ((packed >> 16) & 0xFFFF) / 100f;
            maxSpeed = ((packed >> 32) & 0xFFFF) / 100f;
        }

        /// <summary>Index of event number <paramref name="seq"/> in the shared array.</summary>
        public static int Slot(long seq) => Header + (int)(seq % Capacity) * EventLongs;

        /// <summary>One log line for an event.</summary>
        public static string Line(long car, long blocker, long lane, long packed)
        {
            Unpack(packed, out int rule, out int type, out float speed, out float max);
            var ci = CultureInfo.InvariantCulture;
            return "car " + EntityText(car) + " stands (" + speed.ToString("0.0", ci) + " m/s, limit " + max.ToString("0.0", ci) + " m/s) on lane " + EntityText(lane)
                   + "; waits for " + (blocker == 0 ? "no vehicle named" : EntityText(blocker)) + " (" + BlockerTypeName(type) + ")"
                   + "; rule: " + RuleName(rule);
        }

        /// <summary>
        /// Main thread: collects the events written since <paramref name="readPos"/> (event number) up to the write cursor
        /// <paramref name="cursor"/>. <paramref name="read"/> reads one long of the shared array. An event whose stamp is
        /// not written yet (a job still running) stops the drain there (read again next time); one already overwritten
        /// (stamp newer) and events further back than <see cref="Capacity"/> count as dropped.
        /// </summary>
        public static void Drain(System.Func<int, long> read, ref long readPos, long cursor, List<(long Car, long Blocker, long Lane, long Packed)> events, out int dropped)
        {
            dropped = 0;
            if (cursor - readPos > Capacity)
            {
                dropped += (int)System.Math.Min(int.MaxValue, cursor - Capacity - readPos);
                readPos = cursor - Capacity;
            }
            while (readPos < cursor)
            {
                int s = Slot(readPos);
                long stamp = read(s);
                if (stamp < readPos + 1) break;
                bool ok = false;
                if (stamp == readPos + 1)
                {
                    var e = (read(s + 1), read(s + 2), read(s + 3), read(s + 4));
                    // Re-read the stamp: a writer that lapped the ring meanwhile makes the values torn.
                    if (read(s) == stamp) { events.Add(e); ok = true; }
                }
                if (!ok) dropped++;
                readPos++;
            }
        }
    }

    /// <summary>
    /// JR-T-013b (G4): rate limit of the watch log (main thread). One line per (car, rule, blocker) at most every
    /// <see cref="RepeatFrames"/> simulation frames, at most <see cref="MaxLines"/> lines per window of
    /// <see cref="WindowFrames"/> frames; the rest is counted and reported in one summary line per window.
    /// </summary>
    public sealed class WatchLimiter
    {
        public const uint RepeatFrames = 512;
        public const uint WindowFrames = 256;
        public const int MaxLines = 20;

        private readonly Dictionary<(long, int, long), uint> m_Last = new Dictionary<(long, int, long), uint>();
        private uint m_WindowStart;
        private bool m_HasWindow;
        private int m_Lines;

        /// <summary>Events suppressed in the current window (repeats + over the line cap).</summary>
        public int Suppressed { get; private set; }

        public void Reset()
        {
            m_Last.Clear();
            m_HasWindow = false;
            m_Lines = 0;
            Suppressed = 0;
        }

        /// <summary>
        /// Opens a new window when <paramref name="frame"/> left the old one; returns the summary line for the window that
        /// closed (null when nothing was suppressed).
        /// </summary>
        public string Tick(uint frame)
        {
            if (m_HasWindow && frame - m_WindowStart < WindowFrames) return null;
            string summary = m_HasWindow && Suppressed > 0
                ? Suppressed.ToString(CultureInfo.InvariantCulture) + " more hold event(s) at the watched junction not logged (repeats within " + RepeatFrames.ToString(CultureInfo.InvariantCulture) + " navigation steps, or over " + MaxLines.ToString(CultureInfo.InvariantCulture) + " lines per " + WindowFrames.ToString(CultureInfo.InvariantCulture) + " steps)"
                : null;
            m_HasWindow = true;
            m_WindowStart = frame;
            m_Lines = 0;
            Suppressed = 0;
            if (m_Last.Count > 4096)
            {
                var old = new List<(long, int, long)>();
                foreach (var kv in m_Last) if (frame - kv.Value >= RepeatFrames) old.Add(kv.Key);
                foreach (var k in old) m_Last.Remove(k);
            }
            return summary;
        }

        /// <summary>True when this event gets a log line.</summary>
        public bool Allow(long car, int rule, long blocker, uint frame)
        {
            var key = (car, rule, blocker);
            if (m_Last.TryGetValue(key, out uint last) && frame - last < RepeatFrames) { Suppressed++; return false; }
            if (m_Lines >= MaxLines) { Suppressed++; return false; }
            m_Last[key] = frame;
            m_Lines++;
            return true;
        }
    }

    /// <summary>
    /// JR-T-013b (G4): the watched node, main thread only. Set by Systems/CarDrivingSwitchSystem.cs (the camera-centre pick,
    /// which may not name copy types), taken by Stage2/JRStage2Bridge.cs at each copy step. Never saved: a new game / load
    /// or "Stop watching" clears it.
    /// </summary>
    public static class WatchHandoff
    {
        /// <summary>Watched node key (<see cref="JunctionWatch.Key"/>), 0 = off.</summary>
        public static long Node { get; private set; }

        /// <summary>Bumped on every change, so the bridge resets its read position and limiter.</summary>
        public static int Version { get; private set; }

        /// <summary>Settings buttons (any thread that runs the options UI): handled in the switch system's next update.</summary>
        public static volatile bool PickRequested;
        public static volatile bool StopRequested;

        public static void Set(long node)
        {
            Node = node;
            Version++;
        }

        public static void Clear()
        {
            if (Node == 0) return;
            Node = 0;
            Version++;
        }
    }
}
