using System;
using System.IO;
using JunctionRules.Core;
using P = JunctionRules.Core.PointPhase;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 1.5.0 (JR-T-038). Item A, owner rule 21 Nov 2026 ("the problem is carving out things, making exceptions for the right
    /// turning"): at a yellow box a car enters only when its exit is not blocked (box room test at the line, G22 / G28
    /// admissions kept); then it drives as at any junction: up to just before its first LIVE conflict (red-held lanes not
    /// counted) and waits there; its exit filling meanwhile keeps it at that spot (G3, now for every boxed lane). Removed:
    /// the stop-line decision for boxed lanes and the UK right-turn turn spot. Item B: never wait inside the lane you give
    /// way to (Core StretchState.Classify: a car that cannot stop before a point any more commits and clears).
    /// Scenes: a 1-D model of one car on its path through a boxed junction (metres from the stop line), the three rules
    /// side by side: off (the game's own give-ways), 1.4.1 (re-implemented here as it was) and 1.5.0 (the Core code).
    /// </summary>
    internal static partial class Program
    {
        // ---------------------------------------------------------------- Core: item B (StretchState.Classify)

        /// <summary>A car on a boxed lane of <paramref name="lane"/> m: front <paramref name="front"/> m from the line (negative = before it).</summary>
        private static StretchState BgState(float lane, float front, float speed, float margin, bool atLine, params float[] startsM)
        {
            var items = new (int, float, float)[startsM.Length];
            for (int i = 0; i < startsM.Length; i++) items[i] = Cv(startsM[i] / lane, (startsM[i] + 3f) / lane);
            var pts = new ZConflicts { Items = items };
            var s = default(StretchState);
            float orig = front <= 0f ? -front - margin : -margin;
            float x = front <= 0f ? 0f : front / lane;
            s.Begin(ref pts, orig, lane, x, margin, BrakingDistance(speed), atLine);
            return s;
        }

        /// <summary>1.4.1 Classify (before JR-T-038 item B), for the differential.</summary>
        private static P Classify141(in StretchState s, float u, out float stopWait)
        {
            int k = s.Spots.StretchOf(u);
            stopWait = 0f;
            if (k >= s.Next)
            {
                float spot = s.Spots[k];
                stopWait = s.Wait(spot);
                return s.CanStopBefore(spot) ? P.Deciding : P.Stock;
            }
            if (s.FrontPast(u)) return P.Crossing;
            float first = s.FirstUncrossed < u ? s.FirstUncrossed : u;
            stopWait = s.Wait(first);
            return s.CanStopBefore(first) ? P.Safety : P.Crossing;
        }

        private static void BoxGeneralCoreTests()
        {
            const float L = 20f;
            // Spots: W = first conflict (6 m), no stop-line variant for a boxed lane any more.
            StretchState a = BgState(L, -5f, 12f, M, false, 6f, 12f);
            Check(a.Spots.Count == 1 && Near(a.Spots[0], 0.3f) && Near(a.FirstAhead, 0.3f), "boxed lane: one spot, its first conflict (no stop-line decision)");
            // 12 m/s, 5 m before the line: cannot stop before the 6 m point (needs 12.2 m, has 11 m), can before the 12 m one.
            Check(!a.CanStopBefore(0.3f) && a.CanStopBefore(0.6f), "fixture: cannot stop at W, can before the later point");
            Check(a.Classify(true, 0.3f, out float w0) == P.Crossing && w0 == 0f && Classify141(a, 0.3f, out _) == P.Stock,
                "item B: the point it cannot stop before any more: commit and clear (1.4.1: the game's give-way, which stops it inside the lane)");
            Check(a.Classify(true, 0.6f, out float w1) == P.Stock && w1 == 0f, "a later point it can still stop before: the game's give-way (stop before that lane's overlap start)");
            Check(StretchState.ForPrediction(P.Crossing) == WholePathRule.YieldAction.Suppress && StretchState.UndoProjection(P.Crossing, false),
                "committed: no give-way to predictions, the 2 s look-ahead undone (collision braking for vehicles physically there stays: ForVehicle physical = Stock)");
            Check(StretchState.ForVehicle(P.Crossing, false, false) == WholePathRule.YieldAction.Stock, "a vehicle physically in the overlap still brakes the car (game's collision braking)");

            // Stop-line setting (the general 'wait at the stop line'): spot 0, FirstAhead = the first point.
            StretchState b = BgState(L, -2f, 8f, M, true, 6f, 12f);
            Check(b.Spots.Count == 1 && b.Spots[0] == 0f && Near(b.FirstAhead, 0.3f), "stop-line variant: spot at the line; first point ahead 6 m");
            Check(!b.CanStopBefore(0f) && b.CanStopBefore(0.3f), "fixture: past stopping at the line, can stop before 6 m");
            Check(b.Classify(true, 0.6f, out float sw) == P.Safety && Near(sw, b.Wait(0.3f)) && Classify141(b, 0.6f, out _) == P.Stock,
                "item B: a later point while the earlier one is still stoppable: stop before the EARLIER lane (1.4.1: before the later one, inside the earlier lane)");
            Check(b.Classify(true, 0.3f, out _) == P.Stock, "the earlier point itself: the game's give-way (it can stop before it)");

            // Bicycle / motorbike margin 0.4 m: the same test with its own margin (can-stop = braking <= Wait + margin = distance front -> start).
            StretchState bike = BgState(L, 1f, 12f, 0.4f, false, 12.6f);
            Check(bike.Margin == 0.4f && Near(bike.Wait(0.63f) + bike.Margin, 11.6f), "bike: front to the overlap start = Wait + 0.4");
            Check(!bike.CanStopBefore(0.63f) && bike.Classify(true, 0.63f, out _) == P.Crossing && Classify141(bike, 0.63f, out _) == P.Stock,
                "Lavender x Ashford (motorbike, cannot stop before the oncoming lane): 1.5.0 commits, 1.4.1 gave the game's give-way (stood inside)");
            StretchState bikeSlow = BgState(L, 1f, 6f, 0.4f, false, 12.6f);
            Check(bikeSlow.CanStopBefore(0.63f) && bikeSlow.Classify(true, 0.63f, out float bw) == P.Deciding && Near(bw + 0.4f, 11.6f),
                "bike that can stop: waits at its spot, front at the overlap start minus its 0.4 m margin");

            // Invariant (random): whenever 1.5.0 leaves a give-way demand (Deciding, Safety, Stock), the car can stop before the
            // point (its front stays before that lane's overlap start). 1.4.1 broke it in the Stock case.
            var rr = new Random(3801);
            int n = 0, bad150 = 0, bad141 = 0, changed = 0;
            for (int i = 0; i < 40000; i++)
            {
                float mg = rr.Next(3) == 0 ? 0.4f : M;
                int c = rr.Next(1, 4);
                var st = new float[c];
                for (int j = 0; j < c; j++) st[j] = 1f + (float)rr.NextDouble() * 15f;
                StretchState s = BgState(L, -10f + (float)rr.NextDouble() * 25f, (float)rr.NextDouble() * 14f, mg, rr.Next(4) == 0, st);
                for (int j = 0; j < c; j++)
                {
                    float u = st[j] / L;
                    if (s.FrontPast(u)) continue;
                    n++;
                    P p5 = s.Classify(true, u, out float sw5), p4 = Classify141(s, u, out _);
                    if (p5 != p4) changed++;
                    bool demand5 = p5 != P.Crossing, demand4 = p4 != P.Crossing;
                    float stopAt5 = p5 == P.Stock ? s.Wait(u) : sw5;
                    if (demand5 && !(s.Braking <= stopAt5 + mg + 1e-4f)) bad150++;
                    if (demand4 && !s.CanStopBefore(u)) bad141++;
                }
            }
            Console.WriteLine($"  item B Classify differential ({n} demands): 1.4.1 told to stop where it cannot {bad141}, 1.5.0 {bad150}; phases changed {changed}");
            Check(bad150 == 0 && bad141 > 0, "1.5.0 never asks a car to stop where it cannot (front would end inside the lane); 1.4.1 did");
        }

        // ---------------------------------------------------------------- scenes

        private enum BxRule { Off = 0, R141 = 1, R150 = 2 }

        private sealed class BxConflict
        {
            public float S, E;
            public bool Red;
            public float[] Arrivals = Array.Empty<float>();
            /// <summary>Time from which each arrival is visible (the car's judgement can see it); default 0.</summary>
            public float[] Visible;
            public float Occ = 1.2f;
        }

        private sealed class BxScene
        {
            public string Name;
            public float Lc = 4.5f, Mg = 0.9f, Box = 20f, Front0 = -30f, V0 = 8f, T = 60f;
            public BxConflict[] Cs = Array.Empty<BxConflict>();
            /// <summary>Free metres past the box end to the first standing vehicle of the exit (large = free).</summary>
            public Func<float, float> ExitRear = _ => 500f;
            public bool Turner, Zebras, G22;
            /// <summary>1.4.1 turn spot (metres; first oncoming conflict start), -1 = none.</summary>
            public float TurnSpot = -1f;
        }

        private struct BxResult
        {
            public int StoodFrontInLane, StoodInLaneExitQueue, ClearStep, HeldLine, Needless, StoodInBox, Nose, NoseExitBlocked, EnteredBlocked, Admitted, StoodInLane, Blocked, Shared, ToldInside;
            public float MaxStandFront;
            public override string ToString() =>
                $"clear {(ClearStep < 0 ? "never" : ClearStep.ToString())}, held at line {HeldLine}, needless {Needless}, stood in box {StoodInBox}, nose {Nose} (exit blocked {NoseExitBlocked}), entered blocked {EnteredBlocked}, stood in give-way lane {StoodInLane} (front inside the lane {StoodFrontInLane}, behind its exit queue {StoodInLaneExitQueue}), told to stop inside {ToldInside}, blocked others {Blocked}, shared overlap {Shared}";
            public void Add(BxResult o)
            {
                HeldLine += o.HeldLine; Needless += o.Needless; StoodInBox += o.StoodInBox; Nose += o.Nose; NoseExitBlocked += o.NoseExitBlocked;
                EnteredBlocked += o.EnteredBlocked; StoodInLaneExitQueue += o.StoodInLaneExitQueue; StoodFrontInLane += o.StoodFrontInLane; Admitted += o.Admitted; StoodInLane += o.StoodInLane; Blocked += o.Blocked; Shared += o.Shared; ToldInside += o.ToldInside;
                ClearStep += o.ClearStep < 0 ? 0 : o.ClearStep;
            }
        }

        /// <summary>Discretisation tolerance of the can-stop tests (the game's step 0.27 s at 10 m/s = 2.7 m; the model brakes on its curve).</summary>
        private const float BxTol = 0.5f;
        private const float BxDt = 0.25f, BxAcc = 2.5f, BxVMax = 10f, BxRoomMargin = 1f;

        private static float BxStopSpeed(float d) => d <= 0f ? 0f : Braking * (-Dt + (float)Math.Sqrt(Dt * Dt + 2f * d / Braking));

        private static float BxTimeTo(float d, float v)
        {
            if (d <= 0f) return 0f;
            float tAcc = (BxVMax - v) / BxAcc, dAcc = v * tAcc + 0.5f * BxAcc * tAcc * tAcc;
            if (d <= dAcc) return (-v + (float)Math.Sqrt(v * v + 2f * BxAcc * d)) / BxAcc;
            return tAcc + (d - dAcc) / BxVMax;
        }

        /// <summary>The stock gap judgement for one conflict: no visible vehicle there while the car would be in it.</summary>
        private static bool BxClear(BxScene sc, BxConflict c, float t, float front, float v)
        {
            float a = t + BxTimeTo(c.S - front, v), b = t + BxTimeTo(c.E + sc.Lc - front, v);
            for (int i = 0; i < c.Arrivals.Length; i++)
            {
                if (c.Visible != null && t < c.Visible[i]) continue;
                float ta = c.Arrivals[i];
                if (ta + c.Occ + 0.5f > a && ta - 0.5f < b) return false;
            }
            return true;
        }

        private static bool BxPresent(BxConflict c, float t)
        {
            foreach (float ta in c.Arrivals) if (t >= ta && t <= ta + c.Occ) return true;
            return false;
        }

        private static bool BxDue(BxConflict c, float t, float ahead)
        {
            foreach (float ta in c.Arrivals) if (ta + c.Occ >= t && ta <= t + ahead) return true;
            return false;
        }

        private static BxResult BxRun(BxScene sc, BxRule rule)
        {
            var r = new BxResult { ClearStep = -1, MaxStandFront = float.MinValue };
            float front = sc.Front0, v = sc.V0;
            int steps = (int)(sc.T / BxDt);
            // live conflicts and the spots
            bool lastRoomWhenStoppable = true;   // the exit room the last time the car could still stop at the line
            float w = 2000f;
            foreach (var c in sc.Cs) if (!c.Red && c.S < w) w = c.S;
            for (int step = 0; step < steps; step++)
            {
                float t = step * BxDt;
                bool room = sc.ExitRear(t) >= sc.Lc + BxRoomMargin;
                float limit = BxVMax;
                bool lineRefuses = false;
                if (front <= 0f && BrakingDistance(v) <= -front + BxTol + v * BxDt) lastRoomWhenStoppable = room;
                void Hold(float point, BxConflict c)
                {
                    float target = point - sc.Mg;
                    if (c != null && front < c.S && !(BrakingDistance(v) <= c.S - front + BxTol + v * BxDt)) r.ToldInside++;
                    limit = Math.Min(limit, BxStopSpeed(target - front));
                }
                // exit queue (car-following, every rule)
                limit = Math.Min(limit, BxStopSpeed(sc.Box + sc.ExitRear(t) - 1f - front));
                // physical presence (the game's collision braking, every rule)
                foreach (var c in sc.Cs)
                    if (!c.Red && BxPresent(c, t) && front <= c.S && front - sc.Lc < c.E) limit = Math.Min(limit, BxStopSpeed(c.S - sc.Mg - front));
                if (rule == BxRule.Off)
                {
                    foreach (var c in sc.Cs)
                        if (!c.Red && front < c.S && !BxClear(sc, c, t, front, v)) Hold(c.S, c);
                }
                else
                {
                    // box: the room test at the line (G22 admits)
                    bool beforeLine = front <= 0f, canStopLine = BrakingDistance(v) <= -front + BxTol + v * BxDt;
                    lineRefuses = beforeLine && canStopLine && !room && !sc.G22;
                    if (lineRefuses) Hold(0f, null);
                    // spots
                    float[] spots;
                    if (rule == BxRule.R141)
                    {
                        bool ts = sc.Turner && sc.TurnSpot > 0f && (!sc.Zebras || sc.TurnSpot - sc.Mg >= sc.Lc);
                        spots = ts ? new[] { 0f, sc.TurnSpot } : new[] { 0f };
                    }
                    else spots = w < 1000f ? new[] { w } : Array.Empty<float>();
                    int next = 0;
                    while (next < spots.Length && front > spots[next]) next++;
                    int StretchOf(float s) { int k = 0; for (int i = 1; i < spots.Length; i++) if (spots[i] <= s + 1e-3f) k = i; return k; }
                    float FirstFrom(int k)
                    {
                        float lo = spots[k] - 1e-3f, hi = k + 1 < spots.Length ? spots[k + 1] - 1e-3f : 1e9f, f = 1e9f;
                        foreach (var c in sc.Cs) if (!c.Red && c.S >= lo && c.S < hi && front < c.S && c.S < f) f = c.S;
                        return f;
                    }
                    bool CanStop(float p) => BrakingDistance(v) <= p - front + BxTol + v * BxDt;
                    foreach (var c in sc.Cs)
                    {
                        if (c.Red || front >= c.S || spots.Length == 0) continue;
                        bool clear = BxClear(sc, c, t, front, v);
                        int k = StretchOf(c.S);
                        if (k >= next)
                        {
                            float spot = spots[k];
                            if (CanStop(spot)) { if (!clear) Hold(spot, null); continue; }
                            if (rule == BxRule.R141) { if (!clear) Hold(c.S, c); continue; }   // Stock: the game's give-way, can stop or not
                            float earliest = k == next ? Math.Min(FirstFrom(k), c.S) : c.S;
                            if (earliest < c.S && CanStop(earliest)) { if (!clear) Hold(earliest, null); }
                            else if (CanStop(c.S)) { if (!clear) Hold(c.S, c); }
                            // else Crossing: commit and clear
                            continue;
                        }
                        float first = Math.Min(FirstFrom(k), c.S);
                        if (CanStop(first) && !clear) Hold(first, null);
                    }
                    // G3: the room test again at the spot in the box
                    if (!room && !sc.G22)
                    {
                        if (rule == BxRule.R141 && spots.Length == 2 && next == 1 && CanStop(spots[1])) Hold(spots[1], null);
                        if (rule == BxRule.R150 && next < spots.Length && spots[next] > 0.05f && !(beforeLine && canStopLine) && CanStop(spots[next])) Hold(spots[next], null);
                    }
                }
                float nv = Math.Min(Math.Min(limit, v + BxAcc * BxDt), BxVMax);
                nv = Math.Max(nv, Math.Max(0f, v - Braking * BxDt));   // the limit is a speed target; the car brakes at its prefab braking
                float prev = front;
                front += nv * BxDt;
                v = nv;
                float tn = t + BxDt, rear = front - sc.Lc;
                bool roomN = sc.ExitRear(tn) >= sc.Lc + BxRoomMargin;
                if (prev <= 0f && front > 0f)
                {
                    if (!lastRoomWhenStoppable && sc.G22) r.Admitted++;
                    else if (!lastRoomWhenStoppable) r.EnteredBlocked++;   // the last time it could stop at the line the exit was blocked
                }
                bool standing = v < 0.1f;
                foreach (var c in sc.Cs)
                {
                    if (c.Red) continue;
                    bool inLane = front > c.S + 0.05f && rear < c.E;
                    if (inLane && BxDue(c, tn, 2f)) r.Blocked++;
                    if (inLane && BxPresent(c, tn)) r.Shared++;
                    if (standing && inLane && BxDue(c, tn, 5f)) { r.StoodInLane++; if (front <= c.E) r.StoodFrontInLane++; if (sc.Box + sc.ExitRear(tn) - 1f - front < 1.5f) r.StoodInLaneExitQueue++; }
                }
                if (standing)
                {
                    if (front >= -sc.Mg - 1f && front <= 0.05f)
                    {
                        r.HeldLine++;
                        if (roomN && w < 1000f && w - sc.Mg > 0.5f) r.Needless++;
                    }
                    if (front > 0.1f && rear < sc.Box) r.StoodInBox++;
                    if (front > 0.1f && rear < 0f) { r.Nose++; if (!roomN && !sc.G22) r.NoseExitBlocked++; }
                    if (front > r.MaxStandFront && rear < sc.Box) r.MaxStandFront = front;
                }
                if (r.ClearStep < 0 && rear > sc.Box) r.ClearStep = step + 1;
            }
            return r;
        }

        private static float[] BxStream(float from, float to, float every, float jitterSeed = 0f)
        {
            var l = new System.Collections.Generic.List<float>();
            for (float t = from; t < to; t += every) l.Add(t);
            return l.ToArray();
        }

        private static BxScene[] BoxGeneralScenes()
        {
            return new[]
            {
                new BxScene { Name = "right-turner, exit clear, oncoming stream 0-20 s", Turner = true, TurnSpot = 8f,
                    Cs = new[] { new BxConflict { S = 8f, E = 11.5f, Arrivals = BxStream(2f, 20f, 4f) }, new BxConflict { S = 11.5f, E = 15f, Arrivals = BxStream(3f, 20f, 4f) } } },
                new BxScene { Name = "straight, exit clear, side-road stream 0-20 s",
                    Cs = new[] { new BxConflict { S = 6f, E = 9.5f, Arrivals = BxStream(2f, 20f, 4f) }, new BxConflict { S = 12f, E = 15.5f, Arrivals = BxStream(4f, 20f, 4f) } } },
                new BxScene { Name = "exit blocked 0-25 s",
                    ExitRear = t => t < 25f ? 2f : 500f,
                    Cs = new[] { new BxConflict { S = 6f, E = 9.5f, Arrivals = BxStream(2f, 10f, 4f) } } },
                new BxScene { Name = "exit fills at 8 s while it waits in the box (until 30 s)",
                    ExitRear = t => t < 8f || t >= 30f ? 500f : 2f,
                    Cs = new[] { new BxConflict { S = 6f, E = 9.5f, Arrivals = BxStream(2f, 14f, 3f) } } },
                new BxScene { Name = "first lane held by red, second live (dense stream)",
                    Cs = new[] { new BxConflict { S = 4f, E = 7.5f, Red = true }, new BxConflict { S = 10f, E = 13.5f, Arrivals = BxStream(2f, 18f, 2.5f) } } },
                new BxScene { Name = "Fernbrook x Embershade (pickup 5.2 m, right-only lane, oncoming 21 m/s)", Lc = 5.2f, Turner = true, Zebras = true, TurnSpot = 5.37f,
                    Cs = new[] { new BxConflict { S = 5.37f, E = 8.87f, Arrivals = new[] { 1f, 3.5f, 5f, 8f, 9.5f, 13f, 15f, 19f, 20.5f }, Occ = 0.4f },
                                 new BxConflict { S = 8.87f, E = 12.37f, Arrivals = new[] { 2f, 6.5f, 11f, 16.5f }, Occ = 0.4f } } },
                new BxScene { Name = "G22: exit blocked, same-exit footprint admits", G22 = true, T = 30f,
                    ExitRear = _ => 2f },
                new BxScene { Name = "item B: car, conflict 3 m past the line seen late (cannot stop before it)", Front0 = -25f, V0 = 10f, T = 20f,
                    Cs = new[] { new BxConflict { S = 3f, E = 6f, Arrivals = new[] { 3.4f }, Visible = new[] { 2.5f }, Occ = 0.6f } } },
                new BxScene { Name = "item B: motorbike (2 m, margin 0.4), same", Lc = 2f, Mg = 0.4f, Front0 = -25f, V0 = 10f, T = 20f,
                    Cs = new[] { new BxConflict { S = 3f, E = 6f, Arrivals = new[] { 3.4f }, Visible = new[] { 2.5f }, Occ = 0.6f } } },
            };
        }

        private static void BoxGeneralSceneTests()
        {
            BxScene[] sc = BoxGeneralScenes();
            var res = new BxResult[sc.Length, 3];
            for (int i = 0; i < sc.Length; i++)
            {
                for (int k = 0; k < 3; k++) res[i, k] = BxRun(sc[i], (BxRule)k);
                Console.WriteLine($"  box scene {i + 1} {sc[i].Name}:");
                Console.WriteLine($"     off   {res[i, 0]}");
                Console.WriteLine($"     1.4.1 {res[i, 1]}");
                Console.WriteLine($"     1.5.0 {res[i, 2]}");
            }
            BxResult Of(int i, BxRule k) => res[i, (int)k];
            // 1: right-turner, exit clear: drives up to just before the oncoming lane and waits there.
            Check(Of(0, BxRule.R150).HeldLine == 0 && Of(0, BxRule.R150).StoodInBox > 0 && Near(Of(0, BxRule.R150).MaxStandFront, 8f - M, 0.3f) && Of(0, BxRule.R150).StoodInLane == 0,
                "right-turner in a box, exit clear: drives up to just before the oncoming lane (front 7.1 m) and waits there");
            // 2: straight car, exit clear: waits before the live crossing, not at the line (1.4.1: at the line).
            Check(Of(1, BxRule.R150).HeldLine == 0 && Near(Of(1, BxRule.R150).MaxStandFront, 6f - M, 0.3f) && Of(1, BxRule.R141).HeldLine > 0 && Of(1, BxRule.R141).StoodInBox == 0,
                "straight car in a box, exit clear: waits before its first live crossing (1.4.1 waited at the line)");
            // 3: exit blocked: stop line, never enters while blocked.
            Check(Of(2, BxRule.R150).EnteredBlocked == 0 && Of(2, BxRule.R141).EnteredBlocked == 0 && Of(2, BxRule.R150).HeldLine > 0 && Of(2, BxRule.R150).StoodInBox == 0
                && Of(2, BxRule.Off).EnteredBlocked > 0, "exit blocked: waits at the stop line (off drives in)");
            // 4: exit fills while it waits at its spot in the box: stays at the spot (clear of the lane), never stands across it.
            Check(Of(3, BxRule.R150).EnteredBlocked == 0 && Of(3, BxRule.R150).StoodInLane == 0 && Of(3, BxRule.R150).Blocked <= Of(3, BxRule.R141).Blocked
                && Near(Of(3, BxRule.R150).MaxStandFront, 6f - M, 0.3f), "exit fills while it waits in the box: it stays at its spot until there is room (G3 for every lane)");
            // 5: red-held lane not counted: the spot is before the live lane.
            Check(Near(Of(4, BxRule.R150).MaxStandFront, 10f - M, 0.3f) && Of(4, BxRule.R150).HeldLine == 0, "first lane held by red: not counted, waits before the live lane (10 m)");
            // 6: Fernbrook x Embershade: 1.4.1 held at the line, 1.5.0 drives up.
            Check(Of(5, BxRule.R141).HeldLine > 0 && Of(5, BxRule.R141).StoodInBox == 0, "Fernbrook (1.4.1): pickup held at the line (turn spot dropped: 4.47 m < 5.2 m)");
            Check(Of(5, BxRule.R150).HeldLine == 0 && Near(Of(5, BxRule.R150).MaxStandFront, 4.47f, 0.3f) && Of(5, BxRule.R150).StoodInLane == 0 && Of(5, BxRule.R150).NoseExitBlocked == 0,
                "Fernbrook (1.5.0): drives up to just before the oncoming lanes (front 4.47 m past the line) and waits there");
            // 7: G22 admission under 1.5.0 as under 1.4.1.
            Check(Of(6, BxRule.R150).Admitted == 1 && Of(6, BxRule.R141).Admitted == 1 && Of(6, BxRule.R150).EnteredBlocked == 0, "G22: the same-exit footprint still admits the car into a blocked box (1.5.0 = 1.4.1)");
            // 8 / 9: item B.
            for (int i = 7; i <= 8; i++)
            {
                Check(Of(i, BxRule.R141).ToldInside > 0, "item B (1.4.1): " + sc[i].Name + ": told to stop where it could not (front would end inside the lane)");
                Check(Of(i, BxRule.R150).ToldInside == 0 && Of(i, BxRule.R150).StoodInLane == 0 && Of(i, BxRule.R150).Shared <= Of(i, BxRule.R141).Shared
                    && Of(i, BxRule.R150).ClearStep < Of(i, BxRule.R141).ClearStep,
                    "item B (1.5.0): " + sc[i].Name + ": commits and clears, never stands in the lane (shares the overlap no longer than 1.4.1)");
            }

            // Random set: the safety rules.
            var rr = new Random(3802);
            var tot = new BxResult[3];
            int scenes = 300;
            for (int n = 0; n < scenes; n++)
            {
                var s = new BxScene { Name = "random", T = 45f };
                int lc = rr.Next(4);
                s.Lc = lc == 0 ? 2f : lc == 1 ? 4.5f : lc == 2 ? 5.2f : 12f;
                s.Mg = lc == 0 ? 0.4f : M;
                s.V0 = (float)rr.NextDouble() * 12f;
                s.Front0 = -10f - (float)rr.NextDouble() * 25f;
                int c = rr.Next(1, 4);
                s.Cs = new BxConflict[c];
                float at = 2f + (float)rr.NextDouble() * 6f;
                for (int j = 0; j < c; j++)
                {
                    float st = at; at += 3f + (float)rr.NextDouble() * 4f;
                    var arr = BxStream((float)rr.NextDouble() * 3f, 10f + (float)rr.NextDouble() * 20f, 2f + (float)rr.NextDouble() * 5f);
                    var vis = new float[arr.Length];
                    for (int q = 0; q < arr.Length; q++) vis[q] = arr[q] - (rr.Next(4) == 0 ? 0.8f + (float)rr.NextDouble() * 1.2f : 30f);
                    s.Cs[j] = new BxConflict { S = st, E = st + 3.5f, Red = rr.Next(6) == 0, Arrivals = arr, Visible = vis, Occ = 0.4f + (float)rr.NextDouble() };
                }
                s.Turner = rr.Next(2) == 0;
                s.TurnSpot = s.Cs[0].Red && c > 1 ? s.Cs[1].S : s.Cs[0].S;
                s.Zebras = rr.Next(2) == 0;
                float b0 = (float)rr.NextDouble() * 20f, b1 = b0 + (float)rr.NextDouble() * 20f;
                s.ExitRear = rr.Next(3) == 0 ? (_ => 500f) : (t => t >= b0 && t < b1 ? 2f : 500f);
                for (int k = 0; k < 3; k++) tot[k].Add(BxRun(s, (BxRule)k));
            }
            Console.WriteLine($"  random {scenes} box scenes (sum of clear steps; metrics in car-steps):");
            Console.WriteLine($"     off   {tot[0]}");
            Console.WriteLine($"     1.4.1 {tot[1]}");
            Console.WriteLine($"     1.5.0 {tot[2]}");
            Check(tot[2].EnteredBlocked == 0 && tot[1].EnteredBlocked == 0, "random: never enters a box with its exit blocked while it could still stop at the line (1.4.1 and 1.5.0)");
            Check(tot[2].ToldInside == 0, "random: 1.5.0 never tells a car to stop inside a lane it gives way to");
            // Stood in a lane (front inside, no give-way demand there): the stretch rule's Safety stop before the NEXT lane while
            // the car is still in the one it judged clear, physical braking for a vehicle there, and its exit queue; the same
            // mechanisms as at any junction (1.4.1 116 car-steps: 75 / 29 / 12; 1.5.0 151: 100 / 39 / 12). More cars now wait
            // inside the box (at their spot), so more exposure; reported, not a give-way demand inside a lane (told inside = 0).
            Check(tot[2].StoodFrontInLane <= 2 * tot[1].StoodFrontInLane, "random: front standing inside a lane, not a give-way demand, same order as 1.4.1 (see note)");
        }

        private static void BoxGeneralRepoTests()
        {
            string root = RepoRoot();
            string src = Path.Combine(root, "JunctionRules");
            string wj = File.ReadAllText(Path.Combine(src, "Stage2", "JRWholeJunction.cs"));
            string yb = File.ReadAllText(Path.Combine(src, "Stage2", "JRYellowBox.cs"));
            string ez = File.ReadAllText(Path.Combine(src, "Stage2", "JRExitZebraWait.cs"));
            string du = File.ReadAllText(Path.Combine(src, "Stage2", "JRZebraDriveUp.cs"));
            Check(wj.Contains("(m_Mode & Stage2Mode.AtLine) != 0))\n            {\n                return;\n            }\n            m_BoxLane = RuleOn && IsBoxedJunctionLane(lane);"),
                "BeginLane: the stop-line variant only from the general setting; the box no longer moves the decision to the line");
            Check(wj.Contains("bool atLine = RuleOn && (m_Mode & Stage2Mode.AtLine) != 0;"), "BeginZebras: no stop-line variant for boxed lanes");
            Check(!wj.Contains("m_BoxTurnSpot") && !wj.Contains("m_BoxTurnIndex") && !wj.Contains("m_QueuedTurnersMayStand") && !yb.Contains("TurnSpot(") && !ez.Contains("m_QueuedTurnersMayStand"),
                "the UK right-turn turn spot and the queued-turner exception are gone");
            // Kept owner rules (correction 21 Nov): G22 at the line and at the spot, ZebraDriveUp's box reason, G28, G21.
            Check(yb.Contains("// 0.4.11 (G22, owner): a car from the lowest-priority approach may enter") && yb.Contains("public float BoxSpotRoomHold(")
                && yb.Substring(yb.IndexOf("public float BoxSpotRoomHold(", StringComparison.Ordinal)).Contains("SameExitAdmits("), "G22 same-exit exception kept at the line and at the spot in the box");
            Check(du.Contains("ZebraDriveUp.Eligible(RuleOn, m_BoxLane,") && du.Contains("if (!m_BoxLane && next > 0 && next <= count)"), "ZebraDriveUp box reason kept (proposed removal only)");
            string ex = File.ReadAllText(Path.Combine(src, "Stage2", "JRExitGiveWay.cs"));
            Check(ex.Contains("ExitSameLane.Footprint(") && ex.Contains("ExitSameLane.MergeOnly(") && wj.Contains("EntryGiveWay.IsDrivewayEntryLane((uint)wayIn.m_Flags) && WayInBoxed(lane, ref carLanes)"),
                "G28 (exit same lane) and G21 (way in decided at the box line) kept");
            string it = File.ReadAllText(Path.Combine(src, "Vanilla", "JRCarLaneSpeedIterator.cs"));
            Check(it.Contains("m_JR.BoxSpotRoomHold(") && it.Contains("WhyRule.JrBoxSpotRoom)") && !it.Contains("JrBoxTurnSpot"), "S2-37 calls the general spot room hold; why id jr_box_spot_room");
            Check(WhyRules.Id(WhyRule.JrBoxSpotRoom) == "jr_box_spot_room" && (int)WhyRule.JrBoxSpotRoom == 58 && !Enum.IsDefined(typeof(WhyRule), (byte)48)
                && WhyBook.ExtraText(WhyRule.JrBoxSpotRoom, 2.5f) == "room short by 2.5 m", "why ids: 48 (jr_box_turn_spot) retired, 58 new; numbers stable");
            Check(JunctionWatch.RuleName((int)JunctionWatch.Rule.BoxSpotRoom).Contains("spot in the box") && (int)JunctionWatch.Rule.BoxSpotRoom == 1 && (int)JunctionWatch.Rule.BoxSpotWait == 2,
                "watch rule names follow (ids stable)");
            string sr = File.ReadAllText(Path.Combine(src, "Core", "StretchRule.cs"));
            Check(sr.Contains("return CanStopBefore(u) ? PointPhase.Stock : PointPhase.Crossing;") && sr.Contains("FirstAhead"), "Classify: item B commit-and-clear in the Core rule");
        }

        /// <summary>G22 / G28 admissions under 1.5.0 (owner correction 21 Nov: exit blocked -> stop line unless G22 / G28 admits).</summary>
        private static void BoxGeneralAdmissionTests()
        {
            // G22: T junction YIELD side road, left turn merging into the same exit lane: footprint admits (Core unchanged).
            const SameExitException.Path Same = SameExitException.Path.SameExit, Other = SameExitException.Path.OtherExit;
            Check(G22Admit(11f, float.MaxValue, float.MaxValue, 4.5f, G22P((6f, 11f, Same))), "G22 admits under 1.5.0 (same-exit merge only)");
            Check(!G22Admit(11f, float.MaxValue, float.MaxValue, 4.5f, G22P((6f, 11f, Other))), "G22 refuses a footprint over another exit's path");
            // The 1.5.0 spot room gate leaves G22 to decide (the hold returns before holding when SameExitAdmits): see the repo test.
            // G28: the same rule for building exits (Core ExitSameLane unchanged).
            Check(ExitSameLane.MergeOnly(2, 2) && !ExitSameLane.MergeOnly(3, 2), "G28: merge-only test unchanged");
        }
    }
}
