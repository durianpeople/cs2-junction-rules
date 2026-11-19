using System;
using System.Collections.Generic;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// JR-T-024 (owner ruling 19 Nov 2026, BINDING): a car about to cross a junction box whose exit has a zebra. People MOVING
    /// across it = a short wait: the car may enter and wait just before the zebra if the road beyond has room for its whole
    /// length (never on the zebra); people STATIONARY on it = it waits at the box line; vehicles blocking the exit = the line.
    /// Behind the setting "Look past the car in front at a box" (Core/ExitZebraWait.cs, Stage2/JRExitZebraWait.cs).
    /// </summary>
    internal static partial class Program
    {
        // ---------------------------------------------------------------------------------------------------------------
        // Today's behaviour (1.0.1 / 1.1.0 before JR-T-024), reproduced on the zebra sections the job builds
        // ---------------------------------------------------------------------------------------------------------------

        private const float EzLane = 24f;   // a boxed junction lane through a 4-way (m)

        /// <summary>A car whose front is <paramref name="before"/> m before the lane start (moving at speed), on the lane's spots.</summary>
        private static StretchState EzApproach(ZebraLayout layout, float before, float speed, params (int, float, float)[] items)
        {
            var list = new ZConflicts { Items = items };
            var s = default(StretchState);
            s.BeginWithSpots(ref list, layout.Spots, before - M, EzLane, 0f, M, BrakingDistance(speed));
            return s;
        }

        private static void ExitZebraTodayTests()
        {
            // Box lane, Whole junction rule (BeginZebras: sections on, at the line, no Z O gap spots in a box): entry zebra Z1 at the
            // line, the crossing traffic, exit zebra Z2 at the far end. The car does not fit between the last crossing point and Z2:
            // Z2 is one decision with the line.
            var lane = new[] { Zb(0.00f, 0.10f), Cv(0.20f, 0.28f), Cv(0.45f, 0.55f), Cv(0.70f, 0.80f), Zb(0.85f, 0.95f) };
            var elems = new ZElems { Items = lane };
            Check(ZebraSections.Build(ref elems, true, true, EzLane, M, ZCar, 2f, -1f, out ZebraLayout whole), "today (Whole junction, box): layout built");
            Check(whole.Spots.Count == 1 && whole.Spots[0] == 0f && whole.Merged == 1, "today (Whole junction, box): the exit zebra is merged into the line's section (car does not fit after the last crossing point)");
            StretchState car = EzApproach(whole, 15f, 6f, lane);
            PointPhase ph = car.Classify(true, 0.85f, out float wait);
            float stock = 15f - M + EzLane * 0.85f;   // the game's own distance to the exit zebra
            float today = WholePathRule.Distance(StretchState.ForPedestrian(ph), stock, wait);
            Check(ph == PointPhase.Deciding && Near(today, 15f - M), "today (Whole junction, box): ANY person the game's check counts on the exit zebra, walking or standing, holds the car at the box line");
            // Stock gap rule + "Cars never stop on a zebra" (default settings): zebra sections only, no line variant. A car fits
            // between Z1 and Z2: Z2 is its own section with its own spot, INSIDE the box.
            elems = new ZElems { Items = lane };
            Check(ZebraSections.Build(ref elems, false, false, EzLane, M, ZCar, out ZebraLayout stockLayout) && stockLayout.Spots.Count == 2
                && Near(stockLayout.Spots[1], 0.85f), "today (stock gap rule): the exit zebra has its own waiting spot inside the box");
            car = EzApproach(stockLayout, 15f, 6f, lane);
            ph = car.Classify(true, 0.85f, out wait);
            today = WholePathRule.Distance(StretchState.ForPedestrian(ph), stock, wait);
            Check(ph == PointPhase.Deciding && Near(today, stock), "today (stock gap rule): people on the exit zebra, walking OR standing, let the car into the box and hold it just before the zebra");
            // A bus does not fit between Z1 and Z2 (stock gap rule): merged, held at Z1's spot (the box line).
            elems = new ZElems { Items = lane };
            Check(ZebraSections.Build(ref elems, false, false, EzLane, M, ZBus + 8f, out ZebraLayout busLayout) && busLayout.Spots.Count == 1 && busLayout.Merged == 1,
                "today (stock gap rule): a long vehicle that does not fit before the exit zebra decides it at the entry zebra (the line)");
            // The box room test reads only the road past the box's end: people on the box's own exit zebra never enter it.
            string wj = File.ReadAllText(Path.Combine(RepoRoot(), "JunctionRules", "Stage2", "JRYellowBox.cs"));
            Check(wj.Contains("bool room = HasRoomPast(lane, 1f, inverse, laneLength, 0f, owner.m_Owner,"), "today: the junction box room is measured from the lane end (past the exit zebra)");
        }

        // ---------------------------------------------------------------------------------------------------------------
        // The rule (Core)
        // ---------------------------------------------------------------------------------------------------------------

        private static void ExitZebraRuleTests()
        {
            Check(ExitZebraWait.Person(true, 1.69f, true, false) == ExitZebraWait.Moving, "a person walking (1.3 m/s) is moving");
            Check(ExitZebraWait.Person(true, 0.0025f, true, false) == ExitZebraWait.Stationary, "a person at 0.05 m/s stands (the game's walking test: speed^2 >= 0.01)");
            Check(ExitZebraWait.Person(false, 0f, true, false) == ExitZebraWait.Stationary, "no Moving component = standing");
            Check(ExitZebraWait.Person(true, 0f, true, true) == ExitZebraWait.None, "standing in the waiting area at the kerb (G23, no-push on) is not on the zebra");
            Check(ExitZebraWait.Person(true, 0f, false, true) == ExitZebraWait.Stationary, "without no-push the game's own check sees them: stationary");
            Check(ExitZebraWait.Person(true, 1.69f, true, true) == ExitZebraWait.Moving, "walking off the kerb onto the zebra is moving");
            Check(ExitZebraWait.Worse(ExitZebraWait.Moving, ExitZebraWait.Stationary) == ExitZebraWait.Stationary && ExitZebraWait.Worse(ExitZebraWait.None, ExitZebraWait.Moving) == ExitZebraWait.Moving,
                "one standing person makes the zebra stationary");
            Check(ExitZebraWait.IsExitZebra(0.85f, 0.85f) && !ExitZebraWait.IsExitZebra(0.0f, 0.0f) && !ExitZebraWait.IsExitZebra(0.6f, 0.85f),
                "exit zebra = the last zebra on the lane, in its second half (an entry zebra alone is not one)");
            Check(ExitZebraWait.Decide(true, ExitZebraWait.Moving, true) == ExitZebraWait.Verdict.WaitBeforeZebra, "moving + room free now: enter, wait just before the zebra");
            Check(ExitZebraWait.Decide(true, ExitZebraWait.Stationary, true) == ExitZebraWait.Verdict.Line, "stationary: the box line");
            Check(ExitZebraWait.Decide(true, ExitZebraWait.Moving, false) == ExitZebraWait.Verdict.Line, "moving but the road past not free now: the box line");
            Check(ExitZebraWait.Decide(true, ExitZebraWait.None, false) == ExitZebraWait.Verdict.Today, "nobody on the zebra: today's driving");
            bool offToday = true;
            foreach (int p in new[] { ExitZebraWait.None, ExitZebraWait.Moving, ExitZebraWait.Stationary })
                foreach (bool r in new[] { false, true }) offToday &= ExitZebraWait.Decide(false, p, r) == ExitZebraWait.Verdict.Today;
            Check(offToday, "setting off: always today's driving");
            Check(ExitZebraWait.PedestrianDistance(true, true, 30f, 14f) == 30f && ExitZebraWait.PedestrianDistance(true, false, 30f, 14f) == 14f
                && ExitZebraWait.PedestrianDistance(false, true, 30f, 14f) == 14f, "pedestrian check: the game's own distance only for the exit zebra of a lane let in");
            Check(ExitZebraWait.WaitingAtZebra(M, M, ExitZebraWait.Moving) && !ExitZebraWait.WaitingAtZebra(M + 2.5f, M, ExitZebraWait.Moving)
                && !ExitZebraWait.WaitingAtZebra(-0.5f, M, ExitZebraWait.Moving) && !ExitZebraWait.WaitingAtZebra(M, M, ExitZebraWait.None),
                "counted as waiting at the zebra: front within the stop margin + 2 m before it, never on it, somebody there");
            // with the decision: the overlap the job marks gets the stock distance (front a margin before the zebra), not the line
            var lane = new[] { Zb(0.00f, 0.10f), Cv(0.20f, 0.28f), Cv(0.45f, 0.55f), Cv(0.70f, 0.80f), Zb(0.85f, 0.95f) };
            var elems = new ZElems { Items = lane };
            ZebraSections.Build(ref elems, true, true, EzLane, M, ZCar, 2f, -1f, out ZebraLayout whole);
            StretchState car = EzApproach(whole, 15f, 6f, lane);
            PointPhase ph = car.Classify(true, 0.85f, out float wait);
            float stock = 15f - M + EzLane * 0.85f;
            float d = ExitZebraWait.PedestrianDistance(true, true, stock, WholePathRule.Distance(StretchState.ForPedestrian(ph), stock, wait));
            Check(Near(d, stock) && d > 15f, "Whole junction box: walking people on the exit zebra, room free now: the car's stop moves from the line to just before the zebra");

            string core = File.ReadAllText(Path.Combine(RepoRoot(), "JunctionRules", "Core", "ExitZebraWait.cs"));
            string code = System.Text.RegularExpressions.Regex.Replace(core, @"//.*", "");
            Check(!System.Text.RegularExpressions.Regex.IsMatch(code, @"\bnew\s+\w") && !code.Contains("string"),
                "Burst-safe: the rule is static arithmetic, no allocation, no managed types");
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Scenario sim: one car, a boxed 4-way, its exit zebra, people and the road beyond (1D, the BoxScenario step model)
        // ---------------------------------------------------------------------------------------------------------------

        private const float EzBoxEnd = 22f, EzZs = 18f, EzZe = 21f;   // box from the line (0) to 22 m; exit zebra 18-21 m

        private sealed class EzScene
        {
            public string Name;
            public float Length = 4.5f;
            /// <summary>People on the exit zebra at a step: None / Moving / Stationary (from the car's own state).</summary>
            public Func<int, SimVehicle, int> People;
            /// <summary>Rear of a standing vehicle past the box (m from the line), until the step it drives off (-1 = never there).</summary>
            public float BlockRear = -1f;
            public int BlockLeaves;
            /// <summary>The box room test let the car in only by the look-past (free now short, queue moving away).</summary>
            public bool RoomByLookOnly;
            public bool NoPush = true;
        }

        private sealed class EzResult
        {
            public int EnterStep = -1, ClearStep = -1;
            public int OnZebraStanding, StoodInBox, TailOverLineMax, PeopleSteps, EnteredWithStanding, EnteredNoRoom;
            public float StopFront = float.NaN;
            public List<long> Trace = new List<long>();
        }

        /// <summary>The road past the box free now for the whole car + margin (a standing vehicle there, until it leaves).</summary>
        private static bool EzFreeNow(EzScene sc, SimVehicle v, int step) =>
            sc.BlockRear < 0f || step >= sc.BlockLeaves || sc.BlockRear - EzBoxEnd >= v.Length + SimMargin;

        /// <summary>
        /// One run. lookPast = the setting. Box rule as the job: before the line (able to stop) the box room test holds it at
        /// the line when the road past is not free (or only by the look-past while look-past is off). Today's people hold at the
        /// box line (Whole junction rule: the exit zebra merged into the line's section); the new rule as in BoxHold +
        /// PedestrianDistance. Inside the box: the game's own pedestrian stop just before the zebra.
        /// </summary>
        private static EzResult EzRun(EzScene sc, bool lookPast)
        {
            var r = new EzResult();
            var v = new SimVehicle { Id = 1, Front = -70f, Length = sc.Length, Speed = 8f, Accel = 1.5f, Decel = 4f };
            int tailOver = 0;
            for (int step = 0; step < 400; step++)
            {
                int people = sc.People(step, v);
                if (people != ExitZebraWait.None) r.PeopleSteps++;
                bool free = EzFreeNow(sc, v, step);
                bool boxRoom = free || (sc.RoomByLookOnly && lookPast && step < sc.BlockLeaves);
                float target = 1e9f;
                bool before = v.Front <= 0f;
                if (before && SimCanStop(v, -SimMargin - v.Front))
                {
                    if (!boxRoom) target = -SimMargin;
                    else
                    {
                        ExitZebraWait.Verdict verdict = ExitZebraWait.Decide(lookPast, people, free);
                        if (verdict == ExitZebraWait.Verdict.Line) target = -SimMargin;
                        else if (verdict == ExitZebraWait.Verdict.WaitBeforeZebra) target = EzZs - SimMargin;   // the game's own stop
                        else if (people != ExitZebraWait.None) target = -SimMargin;                               // today: the line
                    }
                }
                else if (people != ExitZebraWait.None) target = EzZs - SimMargin;   // committed / inside: the game's own pedestrian stop
                if (sc.BlockRear >= 0f && step < sc.BlockLeaves) target = Math.Min(target, sc.BlockRear - 1f);
                float vNext = Math.Min(v.Speed + v.Accel * SimDt, v.VMax);
                vNext = Math.Min(vNext, SimSafeSpeed(v, target - v.Front));
                vNext = Math.Max(vNext, Math.Max(0f, v.Speed - v.Decel * SimDt));
                float move = vNext * SimDt;
                float was = v.Front;
                v.Speed = vNext;
                v.Front += move;
                if (was <= 0f && v.Front > 0f)
                {
                    r.EnterStep = step;
                    if (people == ExitZebraWait.Stationary) r.EnteredWithStanding++;
                    if (!free) r.EnteredNoRoom++;
                }
                if (r.ClearStep < 0 && v.Front - v.Length > EzBoxEnd) r.ClearStep = step;
                bool standing = v.Speed < 0.5f;
                bool bodyInBox = v.Front > 0.01f && v.Front - v.Length < EzBoxEnd;
                if (standing && bodyInBox) r.StoodInBox++;
                if (standing && v.Front > EzZs + 0.01f && v.Front - v.Length < EzZe) r.OnZebraStanding++;
                if (standing && v.Front > 0.01f && v.Front - v.Length < 0f) { tailOver++; r.TailOverLineMax = Math.Max(r.TailOverLineMax, tailOver); }
                else tailOver = 0;
                if (standing && bodyInBox && float.IsNaN(r.StopFront)) r.StopFront = v.Front;
                r.Trace.Add(((long)step << 40) ^ BitConverter.SingleToInt32Bits(v.Front) ^ ((long)BitConverter.SingleToInt32Bits(v.Speed) << 20));
                if (v.Front > EzBoxEnd + 60f) break;
            }
            return r;
        }

        /// <summary>People walking over the zebra in steps [from, from + steps) (front of the car decides when they start: -1 = at once).</summary>
        private static Func<int, SimVehicle, int> EzWalk(int from, int steps) => (s, v) => s >= from && s < from + steps ? ExitZebraWait.Moving : ExitZebraWait.None;

        private static void ExitZebraScenarioTests()
        {
            // People take ~8 s = 16 steps of 0.5 s to cross a 10 m road at 1.3 m/s. The car reaches the line at about step 17.
            const int Cross = 16;
            var walking = new EzScene { Name = "people walking, room past", People = EzWalk(10, Cross) };
            EzResult off = EzRun(walking, false), on = EzRun(walking, true);
            Console.WriteLine($"  exit zebra '{walking.Name}': OFF enters step {off.EnterStep}, clear {off.ClearStep}, stood in box {off.StoodInBox}; ON enters step {on.EnterStep}, stops front at {on.StopFront:0.0} m (zebra at {EzZs}), clear {on.ClearStep}, stood in box {on.StoodInBox} steps (crossing {Cross})");
            Check(off.StoodInBox == 0 && off.EnterStep >= 10 + Cross, "OFF (today): the car waits at the box line until the people are across");
            Check(on.EnterStep < 10 + Cross && on.OnZebraStanding == 0 && !float.IsNaN(on.StopFront) && on.StopFront <= EzZs && on.StopFront >= EzZs - SimMargin - 1f,
                "ON: moving people + room: enters, stops just before the zebra, never on it");
            Check(on.StoodInBox <= Cross && on.ClearStep < off.ClearStep, "ON: the wait in the box is no longer than the crossing, and the car clears the box earlier");

            var artic = new EzScene { Name = "artic, people walking", Length = 16.5f, People = EzWalk(14, Cross) };
            EzResult aOn = EzRun(artic, true), aOff = EzRun(artic, false);
            Console.WriteLine($"  exit zebra '{artic.Name}': ON stood in box {aOn.StoodInBox}, tail over the line at most {aOn.TailOverLineMax} steps (crossing {Cross}); OFF clear {aOff.ClearStep} -> ON {aOn.ClearStep}");
            Check(aOn.StoodInBox > 0 && aOn.OnZebraStanding == 0 && aOn.TailOverLineMax <= Cross && aOn.StoodInBox <= Cross,
                "ON, 16.5 m artic: never on the zebra; its tail blocks the box entry no longer than the crossing");

            var standing = new EzScene { Name = "a person standing on the zebra", People = (s, v) => s < 40 ? ExitZebraWait.Stationary : s < 44 ? ExitZebraWait.Moving : ExitZebraWait.None };
            EzResult sOn = EzRun(standing, true), sOff = EzRun(standing, false);
            Check(sOn.EnteredWithStanding == 0 && sOn.EnterStep >= 40 && sOn.OnZebraStanding == 0, "ON: people stationary on the zebra: waits at the box line");
            Check(sOff.EnteredWithStanding == 0, "OFF: same (today's line hold)");

            var blocked = new EzScene { Name = "people walking, exit blocked", People = EzWalk(6, Cross), BlockRear = EzBoxEnd + 2f, BlockLeaves = 60 };
            EzResult bOn = EzRun(blocked, true);
            Check(bOn.EnteredNoRoom == 0 && bOn.EnterStep >= 60 && bOn.StoodInBox == 0, "ON: vehicles past the box blocking the exit: waits at the line");

            var lookOnly = new EzScene { Name = "people walking, room only by the look-past", People = EzWalk(6, Cross), BlockRear = EzBoxEnd + 2f, BlockLeaves = 20, RoomByLookOnly = true };
            EzResult lOn = EzRun(lookOnly, true);
            Check(lOn.EnteredNoRoom == 0 && lOn.EnterStep >= 20, "ON: room past only by the look-past (not free now): the zebra wait needs room free now: waits at the line");

            int stepOnFrom = -1;
            var stepOn = new EzScene
            {
                Name = "people step on after the car entered",
                People = (s, v) =>
                {
                    if (stepOnFrom < 0 && v.Front > 2f) stepOnFrom = s;
                    return stepOnFrom >= 0 && s < stepOnFrom + Cross ? ExitZebraWait.Moving : ExitZebraWait.None;
                },
            };
            EzResult pOn = EzRun(stepOn, true);
            stepOnFrom = -1;
            EzResult pOff = EzRun(stepOn, false);
            Check(pOn.OnZebraStanding == 0 && pOff.OnZebraStanding == 0 && pOn.StoodInBox > 0 && pOn.StoodInBox <= Cross,
                "people stepping on after the car entered: it stops before the zebra (still never on it), ON and OFF");

            bool entered = false;
            var standAfter = new EzScene
            {
                Name = "walking people stop on the zebra after the car entered",
                People = (s, v) =>
                {
                    if (v.Front > 2f) entered = true;
                    if (s < 6) return ExitZebraWait.None;
                    if (!entered) return ExitZebraWait.Moving;
                    return s < 40 ? ExitZebraWait.Stationary : s < 44 ? ExitZebraWait.Moving : ExitZebraWait.None;
                },
            };
            EzResult saOn = EzRun(standAfter, true);
            Check(entered && saOn.OnZebraStanding == 0 && saOn.ClearStep > 44, "people who stop on the zebra after the car entered: it still waits before the zebra, never on it");

            // OFF = today, bit for bit: random people / blocker schedules, the setting off against the pre-JR-T-024 decision (any person = line)
            var rr = new Random(2424);
            bool same = true;
            for (int i = 0; i < 400 && same; i++)
            {
                int[] sched = new int[400];
                int k = 0;
                while (k < sched.Length) { int len = rr.Next(1, 30), p = rr.Next(3); for (int j = 0; j < len && k < sched.Length; j++) sched[k++] = p; }
                var sc = new EzScene { Name = "random", Length = rr.Next(2) == 0 ? 4.5f : 16.5f, People = (s, v) => sched[Math.Min(s, sched.Length - 1)],
                    BlockRear = rr.Next(2) == 0 ? EzBoxEnd + (float)rr.NextDouble() * 20f : -1f, BlockLeaves = rr.Next(0, 120), RoomByLookOnly = rr.Next(2) == 0 };
                EzResult a = EzRun(sc, false), b = EzLegacyRun(sc);
                same = a.Trace.Count == b.Trace.Count;
                for (int j = 0; same && j < a.Trace.Count; j++) same = a.Trace[j] == b.Trace[j];
            }
            Check(same, "setting off: identical step-by-step trace to the pre-JR-T-024 box + zebra decision (400 random scenes)");
        }

        /// <summary>The pre-JR-T-024 decision (the reference for the OFF differential): any person on the exit zebra = the box line.</summary>
        private static EzResult EzLegacyRun(EzScene sc)
        {
            var r = new EzResult();
            var v = new SimVehicle { Id = 1, Front = -70f, Length = sc.Length, Speed = 8f, Accel = 1.5f, Decel = 4f };
            for (int step = 0; step < 400; step++)
            {
                int people = sc.People(step, v);
                bool free = EzFreeNow(sc, v, step);
                float target = 1e9f;
                if (v.Front <= 0f && SimCanStop(v, -SimMargin - v.Front))
                {
                    if (!free || people != ExitZebraWait.None) target = -SimMargin;
                }
                else if (people != ExitZebraWait.None) target = EzZs - SimMargin;
                if (sc.BlockRear >= 0f && step < sc.BlockLeaves) target = Math.Min(target, sc.BlockRear - 1f);
                float vNext = Math.Min(v.Speed + v.Accel * SimDt, v.VMax);
                vNext = Math.Min(vNext, SimSafeSpeed(v, target - v.Front));
                vNext = Math.Max(vNext, Math.Max(0f, v.Speed - v.Decel * SimDt));
                v.Speed = vNext;
                v.Front += vNext * SimDt;
                r.Trace.Add(((long)step << 40) ^ BitConverter.SingleToInt32Bits(v.Front) ^ ((long)BitConverter.SingleToInt32Bits(v.Speed) << 20));
                if (v.Front > EzBoxEnd + 60f) break;
            }
            return r;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Job side (source checks): every new path behind the look-past setting; Burst-safe
        // ---------------------------------------------------------------------------------------------------------------

        private static void ExitZebraRepoTests()
        {
            string root = RepoRoot();
            string job = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRExitZebraWait.cs"));
            string yb = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRYellowBox.cs"));
            string wj = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            string jobCode = System.Text.RegularExpressions.Regex.Replace(job, @"//.*", "");
            Check(!System.Text.RegularExpressions.Regex.IsMatch(jobCode, @"\bnew\s+\w"), "job side: no allocation (Burst)");
            int hold = job.IndexOf("private float ExitZebraHold(", StringComparison.Ordinal);
            Check(hold > 0 && job.IndexOf("if (!m_BoxLookPast) return float.MaxValue;", hold, StringComparison.Ordinal) > hold
                && job.IndexOf("if (!m_BoxLookPast) return float.MaxValue;", hold, StringComparison.Ordinal) < job.IndexOf("ExitZebraOf(lane", hold, StringComparison.Ordinal),
                "ExitZebraHold tests the setting first");
            Check(job.Contains("if (!m_BoxLookPast || !ExitZebraOf(lane"), "the in-box counting test tests the setting first");
            Check(yb.Contains("if (!BoxDriving.Holds(a, room)) return m_BoxLookPast ? ExitZebraHold(") && yb.Contains(": float.MaxValue;"),
                "BoxHold: with the setting off the room-ok return is float.MaxValue as before");
            Check(yb.Contains("bool zebraWait = inBox && m_BoxLookPast && StandingAtExitZebra("), "counting: off = zebraWait false (the 1.0.1 counters)");
            Check(System.Text.RegularExpressions.Regex.Matches(job + yb + wj, @"m_ZebraGoLane = (?!Entity\.Null)").Count == 1 && job.Contains("m_ZebraGoLane = lane;"),
                "the pedestrian-distance mark is set only inside ExitZebraHold (after the setting test)");
            Check(wj.Contains("if (m_ZebraGoLane == Entity.Null) return today;"), "PedestrianDistance: no mark = today's distance");
            Check(job.Contains("m_BoxLookPast = false;") && job.Contains("m_BoxLookPast = true;") && job.Contains("freeNow: true,"),
                "room for the zebra wait is the free-now rule (no look-past credit), the setting restored after");
        }
    }
}
