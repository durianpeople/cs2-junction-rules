using System;
using System.Collections.Generic;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// JR-T-022 (owner 19 Nov 2026: "when humans drive over a box, they look over the cars in front of it, and if logically
    /// the human thinks it obviously has space, they don't like to jitter and pause behind the box for the car right in
    /// front to clear it first"; addendum: "I usually can see ahead, but not infinitely ahead"). Setting "Look past the car
    /// in front at a box" (RoomScan.LookPast, default on).
    ///
    /// Metric: a PAUSE = a vehicle stood still at a box line held by the box rule. It was NEEDLESS when the same vehicle,
    /// ignoring that one hold from the moment it began (everything ahead of it unchanged: the step model is deterministic and
    /// vehicles ahead never react to vehicles behind), would have driven through the box without ever standing still. The
    /// counterfactual is run for every pause. "Stood in box" = vehicle-steps standing with any part of the body inside a
    /// painted box or on the boxed junction.
    /// </summary>
    internal static partial class Program
    {
        /// <summary>One lane of the look-past model: boxes, a stop point S past them (a junction / queue head) that opens and closes.</summary>
        private sealed class LpScene
        {
            public string Name;
            public SimLane Lane;
            public float Sink;            // the stop point past the boxes
            public bool HardStop;         // adversarial: the queue head stops dead at S (no braking distance)
            public double OpenRun, ClosedRun; // mean run lengths (steps) of the stop point's open / closed state
            public float SlowShare;       // share of arrivals that are slow lorries (16.5 m, 2.5-3.5 m/s)
            public float[] Lengths = { 4.2f, 4.2f, 4.2f, 6.5f, 6.5f, 12f, 16.5f };
            public double Arrival = 0.2;
        }

        private sealed class LpWorld
        {
            public List<SimVehicle> Vs = new List<SimVehicle>();
            public int Step;
        }

        private static List<SimVehicle> LpClone(List<SimVehicle> vs)
        {
            var r = new List<SimVehicle>(vs.Count);
            foreach (SimVehicle v in vs) r.Add(v.Clone());
            return r;
        }

        /// <summary>One step of every vehicle (front first), as SimRun's vehicle loop, without the yard / light.</summary>
        private static void LpStep(LpScene sc, List<SimVehicle> vs, bool[] open, int step, bool lookPast)
        {
            SimLane lane = sc.Lane;
            bool isOpen = step >= open.Length || open[step];
            vs.Sort((x, y) => y.Front.CompareTo(x.Front));
            SimVehicle ahead = null;
            foreach (SimVehicle v in vs)
            {
                float hard = 1e9f;
                if (ahead != null) hard = ahead.Front - ahead.Length - 1f;
                if (!isOpen && sc.HardStop && v.Front <= sc.Sink + 1e-3f) hard = Math.Min(hard, sc.Sink);
                float soft = 1e9f;
                // car following as the game: a gap it can stop in even if the vehicle ahead brakes as hard as it can
                if (ahead != null) soft = Math.Min(soft, ahead.Front - ahead.Length - 1f + SimMinStop(ahead));
                if (!isOpen && !sc.HardStop && v.Front <= sc.Sink + 1e-3f && SimCanStop(v, sc.Sink - v.Front)) soft = sc.Sink;
                v.HeldByBox = false;
                foreach (var band in SimBands(lane, v.Length))
                {
                    if (v.Front > band.b0 + 1e-3f) continue;
                    if (v.IgnoreLine == band.b0) break;   // counterfactual: this one hold ignored
                    bool canStop = SimCanStop(v, band.b0 - v.Front);
                    bool room = SimRoom(lane, vs, v, band.b0, band.b1, step, true, out _, lookPast);
                    v.LastLookAdmit = lookPast && room && !SimRoom(lane, vs, v, band.b0, band.b1, step, true, out _, false);
                    if (!room && canStop)
                    {
                        soft = Math.Min(soft, band.b0 - 0.5f);
                        v.HeldByBox = true;
                        v.HoldLine = band.b0;
                    }
                    break;
                }
                float vNext = Math.Min(v.Speed + v.Accel * SimDt, v.VMax);
                vNext = Math.Min(vNext, SimSafeSpeed(v, soft - v.Front));
                vNext = Math.Max(vNext, Math.Max(0f, v.Speed - v.Decel * SimDt));
                float move = vNext * SimDt;
                if (move > hard - v.Front) move = Math.Max(0f, hard - v.Front);
                foreach (var band in SimBands(lane, v.Length))
                    if (v.Front <= band.b0 && v.Front + move > band.b0) v.EnteredByLook = v.LastLookAdmit;
                v.Speed = move / SimDt;
                v.Front += move;
                ahead = v;
            }
            for (int i = vs.Count - 1; i >= 0; i--) if (vs[i].Front > sc.Sink + 40f) vs.RemoveAt(i);
        }

        /// <summary>The boxes a standing body may not touch: the painted ones and the boxed junction.</summary>
        private static bool LpInBox(SimLane lane, SimVehicle v, out float boxStart)
        {
            boxStart = 0f;
            foreach (var b in lane.Painted)
                if (v.Front > b.a + 0.01f && v.Front - v.Length < b.b - 0.01f) { boxStart = b.a; return true; }
            if (lane.JunctionBoxed && v.Front > lane.End + 0.01f && v.Front - v.Length < lane.JunctionEnd - 0.01f) { boxStart = lane.End; return true; }
            return false;
        }

        /// <summary>The far end of the box (painted or junction) whose line is at <paramref name="line"/>, via the vehicle's chained bands.</summary>
        private static float LpBandEnd(SimLane lane, float length, float line)
        {
            foreach (var b in SimBands(lane, length)) if (b.b0 == line) return b.b1;
            return line;
        }

        /// <summary>
        /// The counterfactual for a pause: from the world before step <paramref name="from"/>, the vehicle ignores its hold at
        /// <paramref name="line"/>; true when it then passes the whole band without ever standing still.
        /// </summary>
        private static bool LpWouldClear(LpScene sc, List<SimVehicle> snapshot, int id, float line, bool[] open, int from, bool lookPast)
        {
            SimVehicle me0 = snapshot.Find(x => x.Id == id);
            if (me0 == null) return false;
            var vs = new List<SimVehicle>();
            foreach (SimVehicle v in snapshot) if (v.Front >= me0.Front) vs.Add(v.Clone());
            SimVehicle me = vs.Find(x => x.Id == id);
            me.IgnoreLine = line;
            float end = LpBandEnd(sc.Lane, me.Length, line);
            for (int k = 0; k < 600; k++)
            {
                LpStep(sc, vs, open, from + k, lookPast);
                if (!vs.Contains(me) || me.Front - me.Length > end + 0.01f) return true;
                if (me.Speed < 0.01f) return false;
            }
            return false;
        }

        private struct LpResult
        {
            public int Pauses, Needless, StoodSteps, StoodLook, Through, Admitted;
            public bool Drained;
        }

        private static bool[] LpSchedule(LpScene sc, int seed, int steps)
        {
            var rnd = new Random(seed * 7919 + 13);
            var open = new bool[steps];
            bool state = true;
            for (int i = 0; i < steps; i++)
            {
                double mean = state ? sc.OpenRun : sc.ClosedRun;
                if (rnd.NextDouble() < 1.0 / Math.Max(1.0, mean)) state = !state;
                open[i] = state;
            }
            return open;
        }

        private static LpResult LpRun(LpScene sc, int seed, bool lookPast, int steps = 1600, int drain = 1200)
        {
            sc.Lane.NextJunction = sc.Sink;   // the stop point is a junction ahead (the scan does not know when it stops traffic)
            bool[] open = LpSchedule(sc, seed, steps);   // open after 'steps' (drain)
            var rnd = new Random(seed);
            var w = new LpWorld();
            var res = new LpResult();
            var history = new List<List<SimVehicle>>();
            var holdStart = new Dictionary<(int, float), int>();
            var counted = new HashSet<(int, float)>();
            int nextId = 1;
            for (int step = 0; step < steps + drain; step++)
            {
                history.Add(LpClone(w.Vs));
                LpStep(sc, w.Vs, open, step, lookPast);
                foreach (SimVehicle v in w.Vs)
                {
                    if (v.HeldByBox)
                    {
                        var key = (v.Id, v.HoldLine);
                        if (!holdStart.ContainsKey(key)) holdStart[key] = step;
                        if (v.Speed < 0.01f && !counted.Contains(key))
                        {
                            counted.Add(key);
                            res.Pauses++;
                            int from = holdStart[key];
                            if (history[from] != null && LpWouldClear(sc, history[from], v.Id, v.HoldLine, open, from, lookPast)) res.Needless++;
                        }
                    }
                    else
                    {
                        // the hold episode ended (the car went or the hold lifted): a later hold at the same line is a new episode
                        holdStart.Remove((v.Id, v.HoldLine));
                        counted.Remove((v.Id, v.HoldLine));
                    }
                    if (v.Speed < 0.01f && v.Front > 0f && LpInBox(sc.Lane, v, out _))
                    {
                        res.StoodSteps++;
                        if (v.EnteredByLook) res.StoodLook++;   // stood in a box it entered only thanks to the look-past
                    }
                }
                int before = w.Vs.Count;
                if (step < steps && rnd.NextDouble() < sc.Arrival)
                {
                    bool slow = rnd.NextDouble() < sc.SlowShare;
                    float len = slow ? 16.5f : sc.Lengths[rnd.Next(sc.Lengths.Length)];
                    float vmax = slow ? 2.5f + (float)rnd.NextDouble() : SimVmax;
                    float lastRear = 1e9f;
                    foreach (SimVehicle o in w.Vs) lastRear = Math.Min(lastRear, o.Front - o.Length);
                    if (lastRear > SimSpawn + len + 2f)
                        w.Vs.Add(new SimVehicle
                        {
                            Id = nextId++, Front = SimSpawn, Length = len, Speed = vmax, VMax = vmax,
                            Accel = len < 5f ? 2.5f : len < 7f ? 2f : len < 13f ? 1.2f : 1f, Decel = len < 5f ? 5f : len < 7f ? 4.5f : len < 13f ? 3.5f : 3f,
                        });
                }
                // keep the history short (holds begin within a few seconds of a pause; 400 steps = 200 s)
                if (history.Count > 400) history[history.Count - 401] = null;
            }
            res.Through = nextId - 1 - w.Vs.Count;
            res.Drained = w.Vs.Count == 0;
            return res;
        }

        private static List<LpScene> LpScenes()
        {
            SimLane Exit() => new SimLane { Name = "exit box", End = 1000f, JunctionEnd = 1000f, Painted = new List<(float a, float b)> { (40f, 48f) } };
            SimLane Junction() => new SimLane { Name = "boxed junction", End = 60f, JunctionEnd = 74f, JunctionBoxed = true };
            SimLane Chain() => new SimLane { Name = "two boxes", End = 1000f, JunctionEnd = 1000f, Painted = new List<(float a, float b)> { (40f, 48f), (62f, 70f) } };
            return new List<LpScene>
            {
                new LpScene { Name = "exit box, flowing", Lane = Exit(), Sink = 140f, OpenRun = 80, ClosedRun = 12 },
                new LpScene { Name = "exit box, stop-and-go", Lane = Exit(), Sink = 110f, OpenRun = 16, ClosedRun = 16, Arrival = 0.25 },
                new LpScene { Name = "boxed junction, stop-and-go", Lane = Junction(), Sink = 120f, OpenRun = 18, ClosedRun = 14, Arrival = 0.22 },
                new LpScene { Name = "two boxes, flowing", Lane = Chain(), Sink = 150f, OpenRun = 60, ClosedRun = 12 },
                // adversarial
                new LpScene { Name = "ADV queue ahead brakes hard (stop point 14 m past the box)", Lane = Exit(), Sink = 62f, OpenRun = 10, ClosedRun = 8, Arrival = 0.25 },
                new LpScene { Name = "ADV queue ahead stops dead (no braking distance)", Lane = Exit(), Sink = 66f, HardStop = true, OpenRun = 10, ClosedRun = 8, Arrival = 0.25 },
                new LpScene { Name = "ADV slow lorries", Lane = Exit(), Sink = 120f, OpenRun = 30, ClosedRun = 12, SlowShare = 0.25f },
                new LpScene { Name = "ADV trailers (artics)", Lane = Junction(), Sink = 110f, OpenRun = 20, ClosedRun = 14, Lengths = new[] { 16.5f, 16.5f, 12f, 4.2f } },
            };
        }

        /// <summary>What happened to X in an owner case.</summary>
        private struct OwnerOutcome
        {
            public bool StoppedAtLine;   // X stood still before the box line
            public bool StoodInBox;      // X stood still with any part of it in the box
            public bool Cleared;         // X's rear passed the box's far end
            public bool LookUsed;        // before the line, X was let go by the look-past where the free-now rule held it
            public float MinSpeed;       // X's lowest speed before its rear cleared the box
            public int HeldSteps;        // steps X was braking for the box line (held)
        }

        /// <summary>
        /// JR-T-022 owner cases: an exit box from 40 m to <paramref name="boxEnd"/>; X (8 m/s) 14 m before its line; two cars
        /// inside the box (fronts 1 m and 7 m short of the exit) and two cars past it, the nearer one's rear
        /// <paramref name="pastRear"/> m past the exit, all moving at <paramref name="vq"/>. <paramref name="inBoxSpeed"/> /
        /// <paramref name="pastSpeed"/> &gt; 0 give those cars that (lower) speed. <paramref name="brakeAt"/> &gt;= 0: from that
        /// step the car just past the exit brakes as hard as it can to a stop. In-box gap <paramref name="inGap"/>, X's start
        /// <paramref name="xStart"/>.
        /// </summary>
        private static OwnerOutcome OwnerCase(bool lookPast, float vq, float pastRear, float inBoxSpeed, float pastSpeed, float xLength, float boxEnd, int brakeAt, float inGap = 6f, float xStart = 26f)
        {
            var lane = new SimLane { Name = "owner", End = 1000f, JunctionEnd = 1000f, Painted = new List<(float a, float b)> { (40f, boxEnd) } };
            var sc = new LpScene { Name = "owner", Lane = lane, Sink = 1e6f };
            SimVehicle Car(int id, float front, float len, float v) => new SimVehicle
            {
                Id = id, Front = front, Length = len, Speed = v, VMax = v,
                Accel = len < 5f ? 2.5f : len < 13f ? 1.2f : 1f, Decel = len < 5f ? 5f : len < 13f ? 3.5f : 3f,
            };
            float vPast = pastSpeed > 0f ? pastSpeed : vq, vIn = inBoxSpeed > 0f ? inBoxSpeed : vq;
            float f3 = boxEnd + pastRear + 4.2f;
            var vs = new List<SimVehicle>
            {
                Car(4, f3 + 4.2f + 5f, 4.2f, vPast), Car(3, f3, 4.2f, vPast),
                Car(2, boxEnd - 1f, 4.2f, vIn), Car(1, boxEnd - 1f - 4.2f - inGap, 4.2f, vIn),
                Car(9, xStart, xLength, SimVmax),
            };
            var open = new bool[400];
            for (int i = 0; i < open.Length; i++) open[i] = true;
            var res = new OwnerOutcome { MinSpeed = 1e9f };
            SimVehicle x = vs.Find(v => v.Id == 9);
            for (int step = 0; step < 400; step++)
            {
                if (brakeAt >= 0 && step == brakeAt) { SimVehicle f = vs.Find(v => v.Id == 3); if (f != null) sc.Sink = f.Front + SimMinStop(f) + 0.01f; for (int i = step; i < open.Length; i++) open[i] = false; }
                LpStep(sc, vs, open, step, lookPast);
                if (x.Speed < 0.01f && x.Front <= 40.01f) res.StoppedAtLine = true;
                if (x.Speed < 0.01f && LpInBox(lane, x, out _)) res.StoodInBox = true;
                if (x.LastLookAdmit && x.Front <= 40f) res.LookUsed = true;
                if (x.HeldByBox) res.HeldSteps++;
                res.MinSpeed = Math.Min(res.MinSpeed, x.Speed);
                if (x.Front - x.Length > boxEnd) { res.Cleared = true; break; }
                if (brakeAt >= 0 && step > brakeAt + 60) break;
            }
            return res;
        }

        private static void BoxLookPastTests()
        {
            // ---- owner cases (19 Nov): X at the box line, 2 cars in the box, 2 past the exit; look-past on vs off
            {
                // (a) all four moving at 8 m/s (above the minimum), the guaranteed room past the exit holds both in-box cars + X
                OwnerOutcome aOff = OwnerCase(false, 8f, 8f, 0f, 0f, 4.2f, 56f, -1, 7f, 18f);
                OwnerOutcome aOn = OwnerCase(true, 8f, 8f, 0f, 0f, 4.2f, 56f, -1, 7f, 18f);
                Console.WriteLine($"  owner (a) all moving: off brakes to {aOff.MinSpeed:0.0} m/s at the line; on keeps {aOn.MinSpeed:0.0} m/s (look-past used {aOn.LookUsed})");
                Check(aOff.MinSpeed < 7f && aOff.Cleared && !aOff.StoodInBox, "owner (a), off: X brakes for the line (free-now room short) then goes");
                Check(aOn.MinSpeed >= 7.9f && aOn.LookUsed && aOn.Cleared && !aOn.StoodInBox && !aOn.StoppedAtLine, "owner (a), on: X goes through without braking (look-past)");
                // (b) one in-box car crawls (2 m/s < minimum): today's free-now rule, the same as off
                OwnerOutcome bOff = OwnerCase(false, 8f, 8f, 2f, 0f, 4.2f, 56f, -1, 7f, 18f);
                OwnerOutcome bOn = OwnerCase(true, 8f, 8f, 2f, 0f, 4.2f, 56f, -1, 7f, 18f);
                Console.WriteLine($"  owner (b) in-box car slow: off min {bOff.MinSpeed:0.0}, on min {bOn.MinSpeed:0.0} m/s, on used look-past {bOn.LookUsed}");
                Check(!bOn.LookUsed && bOn.MinSpeed == bOff.MinSpeed && bOn.StoppedAtLine == bOff.StoppedAtLine && !bOn.StoodInBox && bOn.MinSpeed < 3f,
                    "owner (b): a slow car in the box -> X holds by the free-now rule, exactly as off");
                // (c) the cars past the box crawl (2.5 m/s < minimum): free-now rule
                OwnerOutcome cOff = OwnerCase(false, 8f, 8f, 0f, 2.5f, 4.2f, 56f, -1, 7f, 18f);
                OwnerOutcome cOn = OwnerCase(true, 8f, 8f, 0f, 2.5f, 4.2f, 56f, -1, 7f, 18f);
                Console.WriteLine($"  owner (c) cars past the box slow: off min {cOff.MinSpeed:0.0}, on min {cOn.MinSpeed:0.0} m/s, on used look-past {cOn.LookUsed}");
                Check(!cOn.LookUsed && cOn.MinSpeed == cOff.MinSpeed && cOn.StoppedAtLine == cOff.StoppedAtLine && !cOn.StoodInBox && cOn.MinSpeed < 3f,
                    "owner (c): slow cars past the box -> X holds by the free-now rule, exactly as off");
                // (d) the car just past the exit brakes as hard as it can, at every moment around X's decision (48 runs)
                int dRuns = 0, dStoodIn = 0, dStoppedLine = 0, dLookThenBrake = 0;
                foreach (float pr in new[] { 2f, 4f, 8f })
                    foreach (float ig in new[] { 3.5f, 7f })
                        for (int k = 0; k < 8; k++)
                        {
                            OwnerOutcome d = OwnerCase(true, 8f, pr, 0f, 0f, 4.2f, 56f, k, ig, 18f);
                            dRuns++;
                            if (d.StoodInBox) dStoodIn++;
                            if (d.StoppedAtLine) dStoppedLine++;
                            if (d.LookUsed) dLookThenBrake++;
                        }
                Console.WriteLine($"  owner (d) car ahead brakes hard ({dRuns} runs, braking at each step 0-7): X stopped at the line {dStoppedLine}, went on a look-past decision {dLookThenBrake}, stood in the box {dStoodIn}");
                Check(dStoodIn == 0 && dStoppedLine > 0 && dLookThenBrake > 0, "owner (d): the queue ahead brakes hard after X decided -> X stops at the line while it can, never stands in the box");
                // (e) the room X needs ends beyond its sight: no look-past credit (decision snapshot)
                var eLane = new SimLane { Name = "sight", End = 1000f, JunctionEnd = 1000f, Painted = new List<(float a, float b)> { (40f, 56f) } };
                List<SimVehicle> EScene(float xFront, float xLen) => new List<SimVehicle>
                {
                    new SimVehicle { Id = 3, Front = 56f + 26f + 4.2f, Length = 4.2f, Speed = 8f, VMax = 8f, Decel = 5f, Accel = 2.5f },   // rear 26 m past the exit
                    new SimVehicle { Id = 2, Front = 55f, Length = 4.2f, Speed = 8f, VMax = 8f, Decel = 5f, Accel = 2.5f },
                    new SimVehicle { Id = 1, Front = 48f, Length = 4.2f, Speed = 8f, VMax = 8f, Decel = 5f, Accel = 2.5f },
                    new SimVehicle { Id = 9, Front = xFront, Length = xLen, Speed = 8f, VMax = 8f, Decel = 3f, Accel = 1f },
                };
                bool ERoom(float xFront, float xLen, bool lp) { var l = EScene(xFront, xLen); return SimRoom(eLane, l, l[3], 40f, 56f, 0, true, out _, lp); }
                // artic 16.5 m: needs 17.5 + 2 x 5.2 = 27.9 m past the exit; free now 26 m, the car there moves away (guaranteed 2.4 m)
                bool eFar = ERoom(30f, 16.5f, true), eNear = ERoom(36f, 16.5f, true), eOff = ERoom(36f, 16.5f, false);
                Console.WriteLine($"  owner (e) sight {RoomScan.LookPastSightMetres} m: artic 26 m before the exit (sees 24 m past it, needs 27.9) room {eFar}; 20 m before (sees 30 m) room {eNear}; off {eOff}");
                Check(!eFar && eNear && !eOff, "owner (e): when the room needed ends beyond the sight distance there is no look-past credit (free-now rule); within sight the same scene is let in");
            }

            // ---- Core: the rule on plain values
            RoomScan a = RoomScan.Begin(4.2f, 1f);
            a.FreeNow = true;
            a.Add(2f, 4.5f, 6f, 7, 1);
            Check(!a.HasRoom, "1.0.1: a car 2 m past the box moving away still holds the next car (free now only)");
            RoomScan b = RoomScan.Begin(4.2f, 1f);
            b.FreeNow = true; b.LookPast = true;
            b.Add(2f, 4.5f, 6f, 7, 1, 8f, RoomScan.GuaranteedTravel(6f, 8f, 0.25f));   // guaranteed 2 m: 4 < 5.2
            b.FinishLookPast();
            Check(!b.HasRoom && b.BlockerIndex == 7, "look-past: guaranteed travel 2 m is not enough (2 + 2 < 5.2): held, naming the car");
            RoomScan c = RoomScan.Begin(4.2f, 1f);
            c.FreeNow = true; c.LookPast = true;
            c.Add(2f, 4.5f, 10f, 7, 1, 8f, RoomScan.GuaranteedTravel(10f, 8f, 0.25f));   // 6 m: 8 >= 5.2
            c.FinishLookPast();
            Check(c.HasRoom && c.AdmittedByLookPast, "look-past: a car moving away whose guaranteed travel leaves the room lets the next car in");
            RoomScan slow = RoomScan.Begin(4.2f, 1f);
            slow.FreeNow = true; slow.LookPast = true;
            slow.Add(2f, 4.5f, 10f, 7, 1, RoomScan.LookPastMinSpeed - 0.5f, 10f);
            slow.FinishLookPast();
            Check(!slow.HasRoom, "look-past: a crawling vehicle (below the minimum speed) gives no look-past room (G19 creeping queue)");
            RoomScan q = RoomScan.Begin(4.2f, 1f);
            q.FreeNow = true; q.LookPast = true;
            q.AddQueued(4.2f, 1f, 5, 1);   // a slow car crossing the box ahead
            q.Add(8f, 4.5f, 10f, 7, 1, 8f, 6f);
            q.FinishLookPast();
            Check(!q.HasRoom && q.BlockerIndex == 5, "look-past: refused while a vehicle the car follows through the box is slow (names it)");
            RoomScan q2 = RoomScan.Begin(4.2f, 1f);
            q2.FreeNow = true; q2.LookPast = true;
            q2.AddQueued(4.2f, 1f, 5, 1);
            q2.Add(30f, 4.5f, 0f, 7, 1, 0f, 0f);
            q2.FinishLookPast();
            Check(q2.HasRoom && !q2.AdmittedByLookPast, "a slow vehicle ahead does not hold a car that has room free now (today's rule unchanged)");
            RoomScan st = RoomScan.Begin(4.2f, 1f);
            st.FreeNow = true; st.LookPast = true;
            st.Add(2f, 4.5f, 0f, 7, 1, 0f, 0f);
            Check(!st.HasRoom, "a standing vehicle past the box holds as before");
            RoomScan light = RoomScan.Begin(4.2f, 1f);
            light.FreeNow = true; light.LookPast = true;
            light.Add(6f, 4.5f, 10f, 7, 1, 8f, 6f);
            light.Add(8.4f, 0f, 0f, 0, 0);   // a red light 8.4 m past the box (standing obstacle, old call)
            light.FinishLookPast();
            Check(!light.HasRoom, "a red / amber light inside the window holds as before (no look-past through a light)");
            RoomScan ext = RoomScan.Begin(4.2f, 1f);
            ext.FreeNow = true; ext.LookPast = true;
            ext.Add(3f, 4.5f, 10f, 7, 1, 8f, 4f);   // now 3, pred 7 >= 5.2: used
            ext.ExtendPast(4f);                       // a further box: needed 9.2 > 7: blocked
            Check(ext.LookPastUsed && !ext.HasRoom && ext.BlockerIndex == 7, "a further box in the window re-judges the look-past room");
            Check(RoomScan.GuaranteedTravel(10f, 8f, 0.25f) == 6f && RoomScan.GuaranteedTravel(1f, 8f, 0.25f) == 0f, "guaranteed travel = braking distance - 2 steps of travel, never negative");
            Check(!new RoomScan().LookPastOn && !RoomScan.Begin(4f, 1f).LookPastOn, "look-past off unless set");
            RoomScan zebra = RoomScan.Begin(4.2f, 1f);
            zebra.LookPast = true;   // without FreeNow (zebra rule): no effect
            zebra.Add(2f, 4.5f, 6f, 7, 1, 8f, 0f);
            Check(zebra.HasRoom && !zebra.LookPastOn, "look-past acts only in box room tests (free now); the zebra rule keeps its credit");

            // ---- limited sight (owner addendum): only what is within LookPastSightMetres of the front bumper counts
            Check(RoomScan.LookPastSightMetres == 50f && RoomScan.SightPast(20f) == 30f && RoomScan.SightPast(-3f) == 50f, "sight 50 m from the front bumper; sight past the box = 50 - front to the box's far end");
            RoomScan near = RoomScan.Begin(16.5f, 1f);
            near.FreeNow = true; near.LookPast = true; near.SightLeft = RoomScan.SightPast(20f);   // 30 m seen past the box
            near.AddQueued(4.2f, 8f, 3, 1);                                                     // occupied 5.2
            near.Add(10f, 4.2f, 30f, 7, 1, 8f, 20f);                                             // pred 24.8 >= 17.5; room ends at 22.7 <= 30
            near.FinishLookPast();
            Check(near.HasRoom && near.AdmittedByLookPast, "sight: the room the artic needs ends within sight (22.7 of 30 m): look-past");
            RoomScan far = RoomScan.Begin(16.5f, 1f);
            far.FreeNow = true; far.LookPast = true; far.SightLeft = RoomScan.SightPast(40f);    // only 10 m seen past the box
            far.AddQueued(4.2f, 8f, 3, 1);
            far.Add(10f, 4.2f, 30f, 7, 1, 8f, 20f);
            far.FinishLookPast();
            Check(!far.HasRoom && far.BlockerIndex == 7, "sight: the same scene seen from 40 m before the box's end: the room is not confirmed within sight: today's rule (held)");
            RoomScan beyond = RoomScan.Begin(4.2f, 1f);
            beyond.FreeNow = true; beyond.LookPast = true; beyond.SightLeft = 10f;
            beyond.Add(40f, 4.2f, 0f, 7, 1, 0f, 0f);   // a standing car far beyond sight: room free now anyway
            beyond.FinishLookPast();
            Check(beyond.HasRoom && !beyond.AdmittedByLookPast, "sight: a vehicle beyond sight never counts against the car (room free now is today's rule)");
            RoomScan extSight = RoomScan.Begin(4.2f, 1f);
            extSight.FreeNow = true; extSight.LookPast = true; extSight.SightLeft = 8f;
            extSight.Add(3f, 4.5f, 10f, 7, 1, 8f, 4f);   // used (5.2 <= 8)
            extSight.ExtendPast(1f);                       // needed 6.2 <= pred 7, but... 6.2 + 5.5 > 8: beyond sight
            extSight.FinishLookPast();
            Check(!extSight.HasRoom, "sight: a further box that pushes the room needed beyond sight falls back to today's rule");

            // ---- OFF = 1.0.1 bit for bit: random differential test of the RoomScan calls
            var rr = new Random(2211);
            bool same = true;
            for (int i = 0; i < 20000 && same; i++)
            {
                RoomScan o = RoomScan.Begin((float)rr.NextDouble() * 18f, (float)rr.NextDouble() * 3f);
                RoomScan n = o;
                bool fn = rr.Next(2) == 0, ms = rr.Next(2) == 0;
                o.FreeNow = n.FreeNow = fn; o.QueuedMayStand = n.QueuedMayStand = ms;
                n.LookPast = false; n.SightLeft = (float)rr.NextDouble() * 60f;
                int ops = rr.Next(1, 9);
                for (int k = 0; k < ops; k++)
                {
                    int op = rr.Next(5);
                    float x = (float)rr.NextDouble() * 40f - 5f, len = (float)rr.NextDouble() * 17f, cr = (float)rr.NextDouble() * 12f, sp = (float)rr.NextDouble() * 12f;
                    int id = rr.Next(100);
                    if (op == 0) { o.Add(x, len, cr, id, 1); n.Add(x, len, cr, id, 1, sp, RoomScan.GuaranteedTravel(cr, sp, 0.25f)); }
                    else if (op == 1) { o.AddQueued(len); n.AddQueued(len, sp, id, 1); }
                    else if (op == 2) { o.ExtendPast(x); n.ExtendPast(x); }
                    else if (op == 3) { o.BlockBy(id, 1); n.BlockBy(id, 1); }
                    else { o.Add(x, len, 0f, 0, 0); n.Add(x, len, 0f, 0, 0); }
                }
                n.FinishLookPast();
                same = o.HasRoom == n.HasRoom && o.Blocked == n.Blocked && o.BlockerIndex == n.BlockerIndex && o.BlockerVersion == n.BlockerVersion
                    && o.Needed == n.Needed && o.Reach == n.Reach && o.Obstacles == n.Obstacles && o.Extensions == n.Extensions
                    && o.HeldForMovingQueue == n.HeldForMovingQueue && o.Done(30f) == n.Done(30f) && !n.LookPastUsed && !n.AdmittedByLookPast;
            }
            Check(same, "setting off: the JR-T-022 calls (speeds, travel, FinishLookPast) give the 1.0.1 RoomScan state exactly (20000 random scans)");

            // OFF on the box scenario set (Alderwood, Alderbrook, chained boxes): every room decision and vehicle position, step by step
            bool traceSame = true;
            int lookViol = 0, lookNose = 0, offViol = 0, offNose = 0, lookResid = 0, offResid = 0;
            bool lookDrained = true;
            for (int seed = 1; seed <= 20; seed++)
                foreach (int which in new[] { 0, 1, 2 })
                {
                    SimLane Make() => which == 0 ? Alderwood(ExitBands.MergeGapMetres) : which == 1 ? Alderbrook(ExitBands.MergeGapMetres) : Chained(ExitBands.MergeGapMetres);
                    var legacy = SimRun(Make(), seed, true, 2400, 2400, false, true, out long tLegacy);
                    var off = SimRun(Make(), seed, true, 2400, 2400, false, false, out long tOff);
                    if (tLegacy != tOff || legacy.violations != off.violations || legacy.served != off.served) traceSame = false;
                    // look-past off vs on with the game's car following (a gap the follower can stop in; the 0.4.10 model has only the 1 m clamp)
                    var on = SimRun(Make(), seed, true, 2400, 2400, true, false, out _, true);
                    off = SimRun(Make(), seed, true, 2400, 2400, false, false, out _, true);
                    offViol += off.violations; offNose += off.nose; offResid += off.residual + off.residualCommitted;
                    lookViol += on.violations; lookNose += on.nose; lookResid += on.residual + on.residualCommitted;
                    lookDrained &= on.drained;
                }
            Check(traceSame, "setting off: identical decisions and positions to the 1.0.1 calls on the whole box scenario set (60 runs, step-by-step hash)");
            Console.WriteLine($"  look-past on the 0.4.10 box scenario set (60 runs): standing in a box not caused by a light {offViol} -> {lookViol}, nose {offNose} -> {lookNose}, light-change residual {offResid} -> {lookResid}, all drained {lookDrained}");
            Check(lookViol == 0 && lookNose == 0, "look-past: still no vehicle standing in a box (not caused by a light change) on the scenario set");
            Check(lookDrained, "look-past: no deadlock on the scenario set (everything drains)");

            // ---- the pause metric, before / after, and stood-in-box, over the look-past scenes (20 seeds each)
            int totPausesOff = 0, totPausesOn = 0, needOff = 0, needOn = 0, stoodOff = 0, stoodOn = 0, stoodLook = 0, stoodLookDead = 0;
            bool allDrained = true;
            foreach (LpScene sc in LpScenes())
            {
                int pOff = 0, pOn = 0, nOff = 0, nOn = 0, sOff = 0, sOn = 0, thOff = 0, thOn = 0, sLook = 0;
                for (int seed = 1; seed <= 20; seed++)
                {
                    LpResult off = LpRun(sc, seed, false);
                    LpResult on = LpRun(sc, seed, true);
                    pOff += off.Pauses; nOff += off.Needless; sOff += off.StoodSteps; thOff += off.Through;
                    pOn += on.Pauses; nOn += on.Needless; sOn += on.StoodSteps; thOn += on.Through; sLook += on.StoodLook;
                    allDrained &= on.Drained && off.Drained;
                }
                Console.WriteLine($"  look-past scene '{sc.Name}': pauses at the line {pOff} -> {pOn}, needless (would have cleared) {nOff} -> {nOn}, stood in box (vehicle-steps) {sOff} -> {sOn} (of which entered by the look-past {sLook}), vehicles through {thOff} -> {thOn}");
                totPausesOff += pOff; totPausesOn += pOn; needOff += nOff; needOn += nOn; stoodOff += sOff; stoodOn += sOn;
                if (sc.HardStop) stoodLookDead += sLook; else stoodLook += sLook;
                Check(sOn <= sOff, $"look-past scene '{sc.Name}': stood in box does not rise ({sOff} -> {sOn})");
            }
            Console.WriteLine($"  look-past total: pauses {totPausesOff} -> {totPausesOn}, needless pauses {needOff} -> {needOn}, stood in box {stoodOff} -> {stoodOn}, of which by a vehicle the look-past let in {stoodLook} (physical braking) + {stoodLookDead} (queue stops dead, no braking distance: no game vehicle can do that)");
            Check(needOff > 0, "measure first: 1.0.1 pauses at the line although the car would have cleared (reproduced)");
            Check(needOn < needOff, $"look-past: fewer needless pauses ({needOff} -> {needOn})");
            Check(stoodLook == 0, $"look-past: no vehicle it let in ever stands in the box when the queue ahead brakes as vehicles can (incl. adversarial: brakes hard, slow lorries, trailers) ({stoodLook})");
            Check(stoodLookDead < stoodOff / 10, $"look-past: the unphysical dead stop (no braking distance) is a known limit, small next to today's standing ({stoodLookDead} vs {stoodOff})");
            Check(allDrained, "look-past scenes drain (no deadlock), on and off");

            // ---- job wiring (source)
            string root = RepoRoot();
            string wj = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            string yb = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "JunctionRules", "Stage2", "JRYellowBox.cs"));
            string eg = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "JunctionRules", "Stage2", "JRExitGiveWay.cs"));
            string settings = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "JunctionRules", "Settings", "JunctionRulesSettings.cs"));
            string sw = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "JunctionRules", "Systems", "CarDrivingSwitchSystem.cs"));
            Check(wj.Contains("scan.LookPast = freeNow && m_BoxLookPast;") && wj.Contains("j.m_BoxLookPast = (mode & Stage2Mode.BoxLookPast) != 0;")
                && wj.Contains("if (scan.LookPast) scan.FinishLookPast();") && wj.Contains("if (scan.LookPast) scan.SightLeft = frontToStart >= 0f ? RoomScan.SightPast(frontToStart) : 0f;"),
                "job: the look-past runs only in box room tests with the setting on (one flag guards every new call)");
            int guarded = 0, at = 0;
            while ((at = wj.IndexOf("if (scan.LookPast", at, StringComparison.Ordinal)) >= 0) { guarded++; at++; }
            Check(guarded == 6 && wj.Contains("if (scan.LookPast && m_NodeLaneData.HasComponent(nav.m_Lane)) scan.LimitSight(read);"),
                $"job: six guarded look-past branches (sight, finish, queued speed, past speed, probe, the next junction ends the look-past) ({guarded})");
            int sightCallers = 0; at = 0;
            string callers = yb + eg;
            while ((at = callers.IndexOf("freeNow: true", at, StringComparison.Ordinal)) >= 0) { sightCallers++; at++; }
            int withSight = 0; at = 0;
            while ((at = callers.IndexOf("frontToStart:", at, StringComparison.Ordinal)) >= 0) { withSight++; at++; }
            Check(sightCallers == 4 && withSight == 4, "every box room test (junction, band, turn spot, leaving into a band) tells the scan how far the car's front is from the box end (sight)");
            Check(settings.Contains("BoxLookPast = true;") && settings.Contains("\"Look past the car in front at a box\"") && settings.Contains("GetOptionDescLocaleID(nameof(JunctionRulesSettings.BoxLookPast))"),
                "setting default on, label and description");
            Check(sw.Contains("Mod.Settings.BoxLookPast"), "the switch system hands the setting over every step");
            Check((Stage2Mode.BoxBits(true, true, false, true, true, 8f, true) & Stage2Mode.BoxLookPast) != 0 && (Stage2Mode.BoxBits(true, true, false, true, true, 8f) & Stage2Mode.BoxLookPast) == 0
                && Stage2Mode.BoxBits(false, true, false, true, true, 8f, true) == 0 && (Stage2Mode.Stripped & Stage2Mode.BoxLookPast) != 0,
                "mode bit: set only with the box rule and the setting; stripped from the job's stage 2 mode");
            var gs = new GapRuleSwitch();
            gs.Step(JunctionGapRule.Stock, false, out int m1, boxObey: true, boxLookPast: true);
            string flip = gs.Step(JunctionGapRule.Stock, false, out int m2, boxObey: true, boxLookPast: false);
            Check(flip != null && flip.Contains("switched") && (m1 & Stage2Mode.BoxLookPast) != 0 && (m2 & Stage2Mode.BoxLookPast) == 0 && Stage2Mode.RuleName(m1).Contains("looking past the car in front"),
                "turning the setting on / off is logged as a rule switch");
            Check(Stage2Counters.BoxLookPastAdmitted == Stage2Counters.Count - 1 && Stage2Counters.Format(2, new long[Stage2Counters.Count], 0, 0, 2, 2).Contains("let into a box by looking past the car in front"),
                "probe counter at the end, on the timing line");
        }
    }
}
