using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// JR-T-027: the Fernbrook box (node 49960, northbound left lane) replayed offline from the JR-T-026 track log
    /// (data/fernbrook_northbound.tsv: every vehicle on the lane within reach, per poll of about 0.26 s). For every poll and
    /// every car that can still stop at the stop line, the box room test is run with the same Core calls as the job
    /// (Stage2/JRWholeJunction.cs HasRoomPast: AheadFirst / in-box vehicles RoomScan.AddQueued, vehicles past the box
    /// RoomScan.Add with speed and guaranteed travel, the 50 m sight radius from the driver, FinishLookPast).
    ///
    /// Frame (JR-T-026 analysis): s = metres along northbound from the box centre; stop line (front bumper) at -9.7, box line
    /// -7, north mouth (the window start) +8.6. Braking as measured (15.5 m/s2, time step 4/15 s).
    ///
    /// Metrics, per pass of a car through the box:
    /// - HELD: at some poll it could still stop at the line and the room test said no room (a line hold binds);
    /// - NEEDLESS: held, and ignoring the hold from its first held poll it would have cleared the box without standing in it
    ///   (counterfactual: the car keeps its speed, never closer than 1 m behind the vehicles ahead, which move as logged;
    ///   they never react to a car behind them);
    /// - MISJUDGED: at its last poll able to stop the room test let it go, and the same counterfactual has it standing in the
    ///   box (the risk of any look-ahead).
    /// </summary>
    internal static partial class Program
    {
        private const float FbExit = 8.6f, FbStopLine = -9.7f, FbBoxLine = -7f, FbBraking = 15.5f, FbStep = 4f / 15f, FbFrameSeconds = 0.26f / 19f, FbPollSeconds = 0.26f;

        private struct FbVehicle
        {
            public int Id;
            public float S, Speed, Length;
            public bool Large;
        }

        private sealed class FbFrame
        {
            public int Frame;
            public List<FbVehicle> Vs = new List<FbVehicle>();
        }

        private static List<FbFrame> FbLoad()
        {
            string path = Path.Combine(RepoRoot(), "tests", "JunctionRules.Tests", "data", "fernbrook_northbound.tsv");
            var frames = new List<FbFrame>();
            FbFrame cur = null;
            foreach (string line in File.ReadLines(path))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                string[] p = line.Split('\t');
                int frame = int.Parse(p[0], CultureInfo.InvariantCulture);
                if (cur == null || cur.Frame != frame) { cur = new FbFrame { Frame = frame }; frames.Add(cur); }
                char k = p[4][0];
                cur.Vs.Add(new FbVehicle
                {
                    Id = int.Parse(p[1], CultureInfo.InvariantCulture), S = float.Parse(p[2], CultureInfo.InvariantCulture),
                    Speed = float.Parse(p[3], CultureInfo.InvariantCulture), Length = k == 'L' ? 12f : k == 'v' ? 6.5f : 4.5f, Large = k == 'L',
                });
            }
            return frames;
        }

        /// <summary>The game's braking distance (VehicleUtils.GetBrakingDistance shape: v^2 / 2b + v * t).</summary>
        private static float FbBrakingDistance(float v) => v > 0.001f ? v * v / (2f * FbBraking) + v * FbStep : 0f;


        /// <summary>The job's box room test for car <paramref name="x"/> in this poll (look-past on / off).</summary>
        private static bool FbRoom(FbFrame f, in FbVehicle x, bool lookPast, out RoomScan scan)
        {
            scan = RoomScan.Begin(x.Length, RoomScan.DefaultMargin);
            scan.FreeNow = true;
            scan.LookPast = lookPast;
            if (scan.LookPast) scan.SightLeft = x.S + RoomScan.LookPastSightMetres - FbExit;   // straight road: the radius from the driver's point
            var ahead = new List<FbVehicle>();
            foreach (FbVehicle v in f.Vs) if (v.Id != x.Id && v.S > x.S) ahead.Add(v);
            ahead.Sort((a, b) => a.S.CompareTo(b.S));
            var past = new List<FbVehicle>();
            foreach (FbVehicle v in ahead)
            {
                // the game lists a vehicle on the lane its front is on: front still in the node = in the box (queued), front on
                // the exit road = past the box, judged by its rear (which may still be in the box: negative room)
                if (v.S + 0.5f * v.Length < FbExit)
                {
                    // AheadFirst / in the box: a standing one blocks (named); a moving one takes exit room first
                    if (v.Speed < 0.5f) scan.BlockBy(v.Id, 1);
                    else scan.AddQueued(v.Length, v.Speed, v.Id, 1, v.Large);
                }
                else past.Add(v);
            }
            foreach (FbVehicle v in past)
            {
                float credit = FbBrakingDistance(v.Speed);
                scan.Add(v.S - 0.5f * v.Length - FbExit, v.Length, credit, v.Id, 1, v.Speed, RoomScan.GuaranteedTravel(credit, v.Speed, FbStep), v.Large);
            }
            scan.FinishLookPast();
            return scan.HasRoom;
        }

        /// <summary>
        /// Counterfactual from poll <paramref name="from"/>: car <paramref name="id"/> keeps its speed (never closer than 1 m
        /// behind the vehicles that were ahead of it, which move by their logged speeds). True = its rear passes the box exit
        /// without it standing still with its front past the box line.
        /// </summary>
        private static bool FbWouldClear(List<FbFrame> frames, int from, int id) => FbCounterfactual(frames, from, id) == 1;

        /// <summary>1 = clears, 2 = stands with its front past the box line (in the box), 0 = stops before the box (a light, a queue).</summary>
        private static int FbCounterfactual(List<FbFrame> frames, int from, int id)
        {
            bool[] red = FbRed(frames);
            FbVehicle x = default;
            bool found = false;
            foreach (FbVehicle v in frames[from].Vs) if (v.Id == id) { x = v; found = true; }
            if (!found) return 0;
            var pos = new Dictionary<int, float>();
            var len = new Dictionary<int, float>();
            var spd = new Dictionary<int, float>();
            foreach (FbVehicle v in frames[from].Vs)
                if (v.Id != id && v.S > x.S) { pos[v.Id] = v.S; len[v.Id] = v.Length; spd[v.Id] = v.Speed; }
            float s = x.S, speed = Math.Max(x.Speed, 1f);
            for (int i = from; i + 1 < frames.Count && i < from + 400; i++)
            {
                float dt = (frames[i + 1].Frame - frames[i].Frame) * FbFrameSeconds;
                foreach (FbVehicle v in frames[i].Vs) if (spd.ContainsKey(v.Id)) spd[v.Id] = v.Speed;
                var keys = new List<int>(pos.Keys);
                foreach (int k in keys) pos[k] += spd[k] * dt;
                float limit = float.MaxValue;
                foreach (int k in keys) limit = Math.Min(limit, pos[k] - 0.5f * len[k] - 1f - 0.5f * x.Length);
                float next = Math.Min(s + speed * dt, limit);
                if (next < s) next = s;
                // the light is red (or amber) when the car would cross the stop line: it would have stopped anyway
                if (red[i + 1] && s + 0.5f * x.Length <= FbStopLine && next + 0.5f * x.Length > FbStopLine) return 0;
                bool stands = (next - s) < 0.5f * dt;
                s = next;
                if (s - 0.5f * x.Length > FbExit) return 1;
                if (stands && s + 0.5f * x.Length > FbBoxLine) return 2;
                if (stands) return 0;   // stopped before the box: did not clear without stopping
            }
            return 0;
        }

        private static bool[] s_FbRed;

        /// <summary>
        /// Polls with the northbound light not green (the log has no signal states): a vehicle stands with its front at the
        /// stop line (the first car at red), and the 3 s before (amber, and the first car braking to the line).
        /// </summary>
        private static bool[] FbRed(List<FbFrame> frames)
        {
            if (s_FbRed != null && s_FbRed.Length == frames.Count) return s_FbRed;
            var red = new bool[frames.Count];
            for (int i = 0; i < frames.Count; i++)
                foreach (FbVehicle v in frames[i].Vs)
                {
                    float front = v.S + 0.5f * v.Length;
                    if (v.Speed < 0.5f && front > FbStopLine - 3f && front < FbStopLine + 0.5f)
                    {
                        for (int j = i; j >= 0 && frames[i].Frame - frames[j].Frame <= 3f / FbFrameSeconds; j--) red[j] = true;
                        break;
                    }
                }
            return s_FbRed = red;
        }

        private struct FbTally
        {
            public int Passes, Held, Needless, Misjudged;
            public HashSet<int> HeldIds, NeedlessIds, MisjudgedIds;
        }

        private static readonly int s_FbDebug = int.TryParse(Environment.GetEnvironmentVariable("JR_FB_DEBUG"), out int fbd) ? fbd : 0;

        private static FbTally FbReplay(List<FbFrame> frames, bool lookPast)
        {
            var t = new FbTally { HeldIds = new HashSet<int>(), NeedlessIds = new HashSet<int>(), MisjudgedIds = new HashSet<int>() };
            var firstHeld = new Dictionary<int, int>();
            var lastCanStop = new Dictionary<int, (int frame, bool room)>();
            var seenBefore = new HashSet<int>();
            var crossed = new HashSet<int>();
            for (int i = 0; i < frames.Count; i++)
            {
                foreach (FbVehicle x in frames[i].Vs)
                {
                    float front = x.S + 0.5f * x.Length;
                    float d = FbStopLine - front;
                    if (d > 0f && d < 60f) seenBefore.Add(x.Id);
                    if (front - 0.5f * x.Length > FbExit && seenBefore.Contains(x.Id)) crossed.Add(x.Id);
                    if (!(d > 0f) || d > 60f) continue;
                    // the box decision is taken where the car's look-ahead reaches the line (its braking distance): a hold binds
                    // (brakes the car) only from the poll before its last chance to stop
                    float bd = FbBrakingDistance(x.Speed);
                    bool canStop = bd <= d;
                    if (!canStop || bd + x.Speed * FbPollSeconds < d) continue;
                    bool room = FbRoom(frames[i], x, lookPast, out RoomScan sc);
                    if (s_FbDebug == x.Id)
                        Console.WriteLine($"      dbg {(lookPast ? "on " : "off")} poll {frames[i].Frame} s {x.S:0.0} v {x.Speed:0.0} d {d:0.0} room {room} blocker {sc.BlockerIndex} refusal {sc.Refusal} minNow {sc.MinRoomNow:0.0} needed {sc.Needed:0.0} red {FbRed(frames)[i]}");
                    lastCanStop[x.Id] = (i, room);
                    if (!room && !firstHeld.ContainsKey(x.Id)) firstHeld[x.Id] = i;
                }
            }
            foreach (int id in crossed)
            {
                t.Passes++;
                if (firstHeld.TryGetValue(id, out int fh))
                {
                    t.Held++; t.HeldIds.Add(id);
                    if (FbWouldClear(frames, fh, id)) { t.Needless++; t.NeedlessIds.Add(id); }
                }
                if (lastCanStop.TryGetValue(id, out var lc) && lc.room && FbCounterfactual(frames, lc.frame, id) == 2) { t.Misjudged++; t.MisjudgedIds.Add(id); }
            }
            return t;
        }

        private static void FernbrookReplayTests()
        {
            List<FbFrame> frames = FbLoad();
            Check(frames.Count > 400, $"Fernbrook log loaded ({frames.Count} polls)");
            FbTally off = FbReplay(frames, false), on = FbReplay(frames, true);
            string Ids(HashSet<int> s) { var l = new List<int>(s); l.Sort(); return string.Join(",", l); }
            Console.WriteLine($"  Fernbrook replay ({on.Passes} northbound passes): held at the line off {off.Held} / look-past {on.Held}; "
                + $"needless (would have cleared) off {off.Needless} / look-past {on.Needless}; let go then would stand in the box off {off.Misjudged} / look-past {on.Misjudged}");
            Console.WriteLine($"    look-past held: {Ids(on.HeldIds)}; needless: {Ids(on.NeedlessIds)}; misjudged: {Ids(on.MisjudgedIds)}");
            Console.WriteLine($"    off needless: {Ids(off.NeedlessIds)}; off misjudged: {Ids(off.MisjudgedIds)}");
            foreach (int id in new[] { 878675, 878858, 878489, 878522, 878444, 878447 })
                Console.WriteLine($"    case {id}: look-past held {on.HeldIds.Contains(id)} (needless {on.NeedlessIds.Contains(id)}), off held {off.HeldIds.Contains(id)}");
            // Before (1.1.0 look-past, worst-case guaranteed travel; the same replay at the commit before JR-T-027's Core change):
            // held 22, needless 19, misjudged 0; 878675 / 878858 / 878447 held, 878522 / 878444 not (878489: no poll in its
            // last-chance window in this log). Off (1.0.1): held 26, needless 23, misjudged 0.
            Check(off.Held == 26 && off.Needless == 23 && off.Misjudged == 0, "Fernbrook replay, off: the 1.0.1 numbers (26 held, 23 needless)");
            // JR-T-027 (1.2.0, 50 m sight with the sight-end hold): held 9, needless 7, misjudged 0 (the needless ones were the
            // sight limit: from the last-chance point it saw only ~9-10 m past the exit).
            // JR-T-029 (1.3.0, 100 m, nothing slow within sight -> go, packing skips no-stop zones): measured 1 / 1 / 0.
            Console.WriteLine($"  Fernbrook table (held / needless / stood in box): off {off.Held} / {off.Needless} / {off.Misjudged}; 1.2.0 9 / 7 / 0; 1.3.0 {on.Held} / {on.Needless} / {on.Misjudged}");
            Check(on.Needless <= 1 && on.Held <= 1 && on.Misjudged == 0,
                $"JR-T-029 Fernbrook replay: needless holds 7 (1.2.0) -> {on.Needless}, held 9 -> {on.Held}, no car let go that would stand in the box ({on.Misjudged})");
            Check(!on.HeldIds.Contains(878675) && !on.HeldIds.Contains(878522) && !on.HeldIds.Contains(878444),
                "JR-T-027 Fernbrook: 878675 (held live under 1.1.0) now goes; 878522 / 878444 still go");
            // why the holds left are held: the refusal / blocker of each last-chance poll
            foreach (int id in on.HeldIds)
                for (int i = 0; i < frames.Count; i++)
                    foreach (FbVehicle x in frames[i].Vs)
                    {
                        if (x.Id != id) continue;
                        float d = FbStopLine - (x.S + 0.5f * x.Length), bd = FbBrakingDistance(x.Speed);
                        if (!(d > 0f) || bd > d || bd + x.Speed * FbPollSeconds < d) continue;
                        bool room = FbRoom(frames[i], x, true, out RoomScan sc);
                        Console.WriteLine($"    1.3.0 held {id} (needless {on.NeedlessIds.Contains(id)}): poll {i} room {room}, blocker {sc.BlockerIndex}, refusal {WhyRules.RefusalId((LookPastRefusal)sc.Refusal)}, skipped beyond sight {sc.BeyondSightSkipped}");
                    }
        }
    }
}
