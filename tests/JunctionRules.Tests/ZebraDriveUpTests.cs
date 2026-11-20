using System;
using System.Collections.Generic;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// JR-T-033 (1.4.0, owner rule 20 Nov 2026, BINDING): a car whose only reason to hold is people on a zebra of its path waits
    /// RIGHT BEFORE that zebra, not at its spot before the junction; exceptions: exit blocked, something before the zebra not
    /// clear (gap over the drive-in time plus the wait), a yellow box. Core/ZebraDriveUp.cs, Stage2/JRZebraDriveUp.cs.
    /// </summary>
    internal static partial class Program
    {
        // The owner's case: a turn across one conflicting lane P1 with an exit zebra Z right after it; the car does not fit
        // between P1 and the stop point before Z, so Z is merged into the entry spot's section (1.3.0: people on Z = wait at W).
        private const float DuLane = 24f;
        private static readonly (int, float, float)[] DuItems = { Cv(0.40f, 0.62f), Zb(0.72f, 0.84f) };

        private static ZebraLayout DuLayout(float vehicle, (int, float, float)[] items)
        {
            var list = new ZElems { Items = items };
            Check(ZebraSections.Build(ref list, true, false, DuLane, M, vehicle, out ZebraLayout layout), "drive-up: layout built");
            return layout;
        }

        private static StretchState DuCar(ZebraLayout layout, float before, float speed, (int, float, float)[] items)
        {
            var list = new ZConflicts { Items = items };
            var s = default(StretchState);
            s.BeginWithSpots(ref list, layout.Spots, before - M, DuLane, 0f, M, BrakingDistance(speed));
            return s;
        }

        private static void ZebraDriveUpCoreTests()
        {
            ZebraLayout car = DuLayout(ZCar, DuItems);
            Check(car.Spots.Count == 1 && Near(car.Spots[0], 0.40f) && car.Merged == 1, "owner case: the exit zebra is merged into the entry spot's section");
            Check(ZebraDriveUp.MergedZebra(0.72f, car.Spots[0]) && !ZebraDriveUp.MergedZebra(0.40f, 0.40f), "MergedZebra: a zebra after its section's spot; a zebra that starts its section is not");

            // no conflict: the car drives up; the game's pedestrian check gets the distance to the stop right before the zebra
            StretchState s = DuCar(car, 20f, 8f, DuItems);
            PointPhase ph = s.Classify(true, 0.72f, out float spotWait);
            float stock = 20f - M + DuLane * 0.72f;
            Check(ph == PointPhase.Deciding && Near(spotWait, 20f - M + DuLane * 0.40f), "1.3.0: people on Z are decided at the entry spot");
            Check(Near(s.Wait(0.72f), stock), "Wait(zebra) = the game's own distance to the zebra");
            ZebraDriveUp.Reason r = ZebraDriveUp.Decide(false, true, s.CanStopBefore(0.72f), true, false);
            Check(r == ZebraDriveUp.Reason.None, "no conflict, room free now: drives up");
            float d = ZebraDriveUp.PedestrianDistance(r == ZebraDriveUp.Reason.None, true, 0.72f, 0.72f, s.Wait(0.72f), spotWait);
            Check(Near(d, stock) && d > spotWait, "no conflict: the people hold lands right before the zebra (not at the spot)");
            Check(Near(ZebraDriveUp.PedestrianDistance(false, true, 0.72f, 0.72f, stock, spotWait), spotWait), "no drive-up: today's spot distance");

            // conflict within the window: waits at the spot; after the window: drives up
            float deltaP1 = 2.0f, deltaZ = 3.0f, wait = 6f, clear = 3f;
            float window = ZebraDriveUp.Window(deltaP1, deltaZ, wait, clear);
            Check(Near(window, 12f), "window = max(arrival at the point, at the zebra) + people wait + clear time");
            Check(ZebraDriveUp.Decide(false, true, true, true, 9f < window) == ZebraDriveUp.Reason.Traffic, "a conflicting vehicle reaching the point within the window: waits at the spot (traffic)");
            Check(ZebraDriveUp.Decide(false, true, true, true, 14f < window) == ZebraDriveUp.Reason.None, "a conflicting vehicle arriving after the window: drives up");
            Check(ZebraDriveUp.Window(5f, 3f, -1f, -1f) == 5f, "window never shorter than the drive-through arrival time");

            // exit blocked / box / cannot stop / tail on a zebra
            Check(ZebraDriveUp.Decide(false, true, true, false, false) == ZebraDriveUp.Reason.ExitBlocked, "exit blocked (no room free now past the section): no entry");
            Check(ZebraDriveUp.Decide(true, true, true, true, false) == ZebraDriveUp.Reason.Box, "yellow box: the box rules decide (unchanged)");
            Check(ZebraDriveUp.Decide(false, true, false, true, false) == ZebraDriveUp.Reason.CannotStop, "cannot stop before the zebra any more: not a drive-up");
            Check(ZebraDriveUp.Decide(false, false, true, true, false) == ZebraDriveUp.Reason.TailOnZebra, "tail would stand on another zebra: not a drive-up");
            Check(ZebraDriveUp.Decide(true, false, false, false, true) == ZebraDriveUp.Reason.Box, "the exceptions are checked in order: box first");

            // JR-T-035 (review F1): the drive-up needs the Whole junction gap rule (its safeguard); Stock = the spot, as 1.3.0
            Check(ZebraDriveUp.Eligible(false, false, true, true, true) == ZebraDriveUp.Reason.StockGapRule
                && ZebraDriveUp.Decide(false, false, true, true, true, false) == ZebraDriveUp.Reason.StockGapRule
                && ZebraDriveUp.Decide(false, true, false, false, false, true) == ZebraDriveUp.Reason.StockGapRule, "Stock gap rule: never a drive-up, the reason is stock_gap_rule (checked first)");
            Check(ZebraDriveUp.Eligible(true, false, true, true, true) == ZebraDriveUp.Reason.None && ZebraDriveUp.Decide(true, false, true, true, true, false) == ZebraDriveUp.Reason.None,
                "Whole junction: unchanged");
            Check((int)ZebraDriveUp.Reason.StockGapRule == 7 && ZebraDriveUp.ReasonId(ZebraDriveUp.Reason.StockGapRule) == "stock_gap_rule"
                && WhyBook.ExtraText(WhyRule.JrPeopleWaitSpot, (float)ZebraDriveUp.Reason.StockGapRule) == "reason stock_gap_rule", "why: jr_people_wait_spot reason stock_gap_rule (7)");

            // JR-T-035 (review F4): people who cannot hold the car (all past its way, nobody standing) make no drive-up
            Check(!ZebraDriveUp.PeopleCanHold(0f, false) && ZebraDriveUp.PeopleCanHold(0f, true) && ZebraDriveUp.PeopleCanHold(0.5f, false),
                "drive-up only when the people can hold the car: a wait above 0 or somebody standing");
            Check(!ZebraDriveUp.PeopleCanHold(ZebraDriveUp.PersonSeconds(false, 0.9f, 0.4f, 0.6f, 10f, 1.4f), false), "a walker already past the car's way: no drive-up");

            // bus: rear over the crossed lane is allowed (no zebra behind); a zebra behind it within its length is not
            ZebraLayout bus = DuLayout(ZBus, DuItems);
            Check(bus.Merged == 1 && ZebraDriveUp.FitsClearOfZebras(false, DuLane, 0.72f, 0f, M, ZBus), "bus: rear over the crossed conflict lane is not an exception");
            var twoZ = new[] { Zb(0.00f, 0.08f), Cv(0.40f, 0.62f), Zb(0.72f, 0.84f) };
            Check(!ZebraDriveUp.FitsClearOfZebras(true, DuLane, 0.72f, 0.08f, M, 16.5f) && ZebraDriveUp.FitsClearOfZebras(true, DuLane, 0.72f, 0.08f, M, ZBus),
                "entry zebra behind: a 16.5 m artic would stand on it (tail on zebra), a 12 m bus fits clear of it");

            // two zebras: wait before the FIRST one with people; a later zebra of the same section holds the car there too
            var lane2 = new[] { Cv(0.30f, 0.45f), Zb(0.55f, 0.62f), Zb(0.68f, 0.76f) };
            ZebraLayout l2 = DuLayout(ZCar, lane2);
            Check(l2.Spots.Count == 1 && l2.Merged == 2, "two zebras: both merged into the entry spot's section");
            StretchState c2 = DuCar(l2, 20f, 8f, lane2);
            float target = c2.Wait(0.55f), spot2 = c2.Wait(0.30f);
            Check(Near(ZebraDriveUp.PedestrianDistance(true, true, 0.68f, 0.55f, target, spot2), target), "two zebras: people on the second hold the car before the first (target) zebra");
            Check(Near(ZebraDriveUp.PedestrianDistance(true, true, 0.55f, 0.68f, c2.Wait(0.68f), spot2), spot2),
                "two zebras: with the target the second zebra, a person on the first (zebra before the target) keeps today's spot distance");
            Check(Near(ZebraDriveUp.PedestrianDistance(true, false, 0.68f, 0.55f, target, spot2), spot2), "another section: today's distance");
            Check(Near(ZebraDriveUp.CrossingDistance(true, c2.Wait(0.55f), c2.Wait(0.68f)), c2.Wait(0.55f)) && Near(ZebraDriveUp.CrossingDistance(false, 1f, 5f), 5f),
                "crossing the section: a later zebra the car does not fit clear of holds it before the first zebra ahead");

            // the people's wait estimate
            Check(ZebraDriveUp.PersonSeconds(true, 0.5f, 0.4f, 0.6f, 10f, 0f) == ZebraDriveUp.MaxWaitSeconds, "standing on the car's way: the bound");
            float t = ZebraDriveUp.PersonSeconds(false, 0.2f, 0.4f, 0.6f, 10f, 1.4f);   // 2 m before the way, way 4..6 m +1.5 m margin
            Check(Near(t, (6f + ZebraDriveUp.WayMarginMetres - 2f) / 1.4f, 1e-3f), "walking towards the far edge: distance over speed");
            Check(ZebraDriveUp.PersonSeconds(false, 0.9f, 0.4f, 0.6f, 10f, 1.4f) == 0f, "already past the car's way: 0");
            Check(ZebraDriveUp.PersonSeconds(false, 0.5f, 0.4f, 0.6f, 10f, 0.01f) <= ZebraDriveUp.MaxWaitSeconds, "a very slow walker: bounded");
            Check(ZebraDriveUp.WaitSeconds(ZebraDriveUp.WaitSeconds(0f, 3f), 2f) == 3f, "the slowest person binds");
            Check(ZebraDriveUp.MaxWaitSeconds < ArrivalTime.Horizon(4f / 15f) + 0.01f, "the wait bound stays inside the arrival-time horizon");

            // post-entry: the room is asked again before moving onto the zebra (people, or standing at it)
            Check(ZebraDriveUp.RoomAgainAtZebra(true, false, 30f, M) && ZebraDriveUp.RoomAgainAtZebra(false, true, M + 1f, M)
                && !ZebraDriveUp.RoomAgainAtZebra(false, true, 10f, M) && !ZebraDriveUp.RoomAgainAtZebra(false, false, M, M), "room again at the zebra: people on it or standing at it");

            // SoftZebra and G23 unchanged
            Check((ZebraSections.ZebraKind(ZebraSections.PedestrianCrosswalk, true, WholePathRule.SignalStop) & ZebraSections.Zebra) == 0,
                "SoftZebra (signalised, people not on green) is not a zebra here: no drive-up (unchanged)");
            Check(ExitZebraWait.Person(true, true, 0f, true, true, true) == ExitZebraWait.None, "G23: a person standing in the waiting area does not hold the car (no drive-up target)");
            Check(ExitZebraWait.Person(true, true, 1.69f, true, false, false) == ExitZebraWait.Moving, "a walking person on the zebra counts");

            // why ids
            Check(WhyRules.Id(WhyRule.JrPeopleWaitZebra) == "jr_people_wait_zebra" && (int)WhyRule.JrPeopleWaitZebra == 56, "why: jr_people_wait_zebra = 56");
            Check(WhyRules.Id(WhyRule.JrPeopleWaitSpot) == "jr_people_wait_spot" && (int)WhyRule.JrPeopleWaitSpot == 42 && WhyRules.Id(WhyRule.JrSoftZebra) == "jr_soft_zebra",
                "why: existing ids stable");
            Check(WhyBook.ExtraText(WhyRule.JrPeopleWaitSpot, (float)ZebraDriveUp.Reason.Traffic) == "reason traffic_before_zebra"
                && WhyBook.ExtraText(WhyRule.JrPeopleWaitSpot, (float)ZebraDriveUp.Reason.ExitBlocked) == "reason exit_blocked"
                && WhyBook.ExtraText(WhyRule.JrPeopleWaitSpot, (float)ZebraDriveUp.Reason.Box) == "reason yellow_box"
                && WhyBook.ExtraText(WhyRule.JrPeopleWaitSpot, float.NaN) == "none", "why: jr_people_wait_spot reason text (traffic / exit blocked / box)");
            Check(WhyRules.IsJr(WhyRule.JrPeopleWaitZebra), "why: the new id is a Junction Rules id");
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Offline scene set (1-D): off (stock gap rule, no zebra rule) / 1.3.0 / 1.4.0
        // ---------------------------------------------------------------------------------------------------------------

        private enum DuPolicy { Off, V130, V140 }

        private sealed class DuScene
        {
            public string Name = "";
            public float Length = ZCar;
            /// <summary>People on the exit zebra from step a to b (walking), or standing until b when Standing.</summary>
            public int PeopleFrom, PeopleTo;
            public bool Standing;
            /// <summary>Arrival times (s) of conflicting vehicles at P1.</summary>
            public float[] Oncoming = Array.Empty<float>();
            /// <summary>Exit queue standing past the zebra until this step (0 = none).</summary>
            public int ExitBlockedUntil;
            /// <summary>Red light / give-way not cleared at the line until this step.</summary>
            public int LineHoldUntil;
            /// <summary>JR-T-035 (F4): from this step the walkers are past the car's way (still on the zebra, they cannot hold it); -1 = never.</summary>
            public int OffWayFrom = -1;
        }

        private struct DuResult
        {
            public int ClearStep, NeedlessSpot, StoodInJunction, BlockedOthers, OnZebraStanding, EnteredWithBlockedExit;
            public override string ToString() => $"clear {ClearStep}, needless spot {NeedlessSpot}, stood in junction {StoodInJunction}, blocked others {BlockedOthers}, on zebra {OnZebraStanding}";
        }

        private const float DuDt = 0.5f;
        private static float DuP1s => 0.40f * DuLane;
        private static float DuP1e => 0.62f * DuLane;
        private static float DuZs => 0.72f * DuLane;
        private static float DuZe => 0.84f * DuLane;

        private static DuResult DuRun(DuScene sc, DuPolicy pol, bool f4 = true)
        {
            var res = new DuResult { ClearStep = -1 };
            float x = -30f, v = 10f;
            float spot = DuP1s - M, zStop = DuZs - M;
            var onc = new List<float>(sc.Oncoming);
            for (int step = 0; step < 400; step++)
            {
                float now = step * DuDt;
                bool onZebra = step >= sc.PeopleFrom && step < sc.PeopleTo;
                bool people = onZebra && (sc.Standing || sc.OffWayFrom < 0 || step < sc.OffWayFrom);   // people who hold the car
                float peopleLeft = people ? (sc.Standing ? ZebraDriveUp.MaxWaitSeconds : Math.Min((sc.PeopleTo - step) * DuDt, ZebraDriveUp.MaxWaitSeconds)) : 0f;
                bool room = step >= sc.ExitBlockedUntil;
                float rear = x - sc.Length;
                bool overP1 = x > DuP1s && rear < DuP1e;
                // conflicting vehicles: each occupies P1 for 1.5 s from its arrival; blocked (waits) while our car is over P1
                for (int i = 0; i < onc.Count; i++)
                {
                    if (onc[i] <= now && onc[i] + 1.5f > now && overP1) { onc[i] += DuDt; res.BlockedOthers++; }
                }
                float vEff = Math.Max(v, 3f);
                bool Conflict(float window) { foreach (float a in onc) if (a + 1.5f >= now && a - now <= window) return true; return false; }
                float normal = (DuP1e + sc.Length - x) / vEff + 1f;
                float extended = ZebraDriveUp.Window(normal - 1f, (zStop - x) / vEff, peopleLeft, (sc.Length + M + DuZe - DuZs) / 3f);
                bool beforeSpot = x <= spot + 0.01f;
                float target = float.MaxValue;
                if (step < sc.LineHoldUntil && x <= -M + 0.01f) target = Math.Min(target, -M);
                bool conflictNormal = beforeSpot && Conflict(normal);
                switch (pol)
                {
                    case DuPolicy.Off:
                        if (conflictNormal) target = Math.Min(target, spot);
                        if (people && x <= zStop + 0.01f) target = Math.Min(target, zStop);
                        if (!room) target = Math.Min(target, DuZe + 1f);   // stock: queues behind the exit queue (on the zebra)
                        break;
                    case DuPolicy.V130:
                        if (beforeSpot && (conflictNormal || people || !room)) target = Math.Min(target, spot);
                        if (!beforeSpot && people && x <= zStop + 0.01f) target = Math.Min(target, zStop);
                        if (!room && !beforeSpot) target = Math.Min(target, DuZe + 1f);
                        break;
                    default:
                        if (beforeSpot)
                        {
                            bool fits = ZebraDriveUp.FitsClearOfZebras(false, DuLane, 0.72f, 0f, M, sc.Length);
                            // JR-T-035 (F4): a drive-up (and its window) only when the people can hold the car; f4 false = 1.4.0 as reviewed
                            bool driveUp = onZebra && (!f4 || ZebraDriveUp.PeopleCanHold(peopleLeft, people && sc.Standing));
                            ZebraDriveUp.Reason r = ZebraDriveUp.Decide(false, fits, true, room, driveUp && Conflict(extended));
                            if (conflictNormal || !room || (driveUp && r != ZebraDriveUp.Reason.None)) target = Math.Min(target, spot);
                            else if (people) target = Math.Min(target, zStop);
                        }
                        else
                        {
                            if (people && x <= zStop + 0.01f) target = Math.Min(target, zStop);
                            if (!room && x <= zStop + 0.01f && ZebraDriveUp.RoomAgainAtZebra(people, v < 0.5f, zStop - x + M, M)) target = Math.Min(target, zStop);
                            if (!room) target = Math.Min(target, DuZe + 1f);
                        }
                        break;
                }
                // motion
                float gap = target - x;
                float vMax = gap <= 0f ? 0f : (float)Math.Sqrt(2f * 5f * gap);
                float nv = Math.Min(Math.Min(v + 2.5f * DuDt, 10f), vMax);
                bool wasBefore = beforeSpot;
                x = Math.Min(x + nv * DuDt, target == float.MaxValue ? float.MaxValue : Math.Max(x, target));
                v = nv;
                if (wasBefore && x > spot + 0.01f && !room) res.EnteredWithBlockedExit++;
                bool standing = v < 0.5f;
                rear = x - sc.Length;
                if (standing && Math.Abs(x - spot) < 1f && onZebra && room && !Conflict(normal)) res.NeedlessSpot++;
                if (standing && x > spot + 0.5f && rear < DuLane) res.StoodInJunction++;
                if (standing && x > DuZs && rear < DuZe) res.OnZebraStanding++;
                if (res.ClearStep < 0 && rear > DuLane) { res.ClearStep = step; break; }
            }
            return res;
        }

        private static readonly List<(string Scene, DuResult Off, DuResult V130, DuResult V140)> s_DuTable = new List<(string, DuResult, DuResult, DuResult)>();

        private static (DuResult, DuResult, DuResult) DuAll(DuScene sc)
        {
            DuResult a = DuRun(sc, DuPolicy.Off), b = DuRun(sc, DuPolicy.V130), c = DuRun(sc, DuPolicy.V140);
            s_DuTable.Add((sc.Name, a, b, c));
            Console.WriteLine($"  drive-up '{sc.Name}': off [{a}] | 1.3.0 [{b}] | 1.4.0 [{c}]");
            return (a, b, c);
        }

        private static void ZebraDriveUpSceneTests()
        {
            s_DuTable.Clear();
            // people walk 8 s over the exit zebra while the car arrives (it reaches the spot at about step 7)
            var (_, b1, c1) = DuAll(new DuScene { Name = "no conflict, people on the exit zebra", PeopleFrom = 0, PeopleTo = 24 });
            Check(b1.NeedlessSpot > 0 && c1.NeedlessSpot == 0, "no conflict: 1.3.0 holds at the spot for the people, 1.4.0 never does");
            Check(c1.OnZebraStanding == 0 && c1.ClearStep < b1.ClearStep, "no conflict: 1.4.0 drives up, waits before the zebra (never on it), clears earlier");

            var (_, b2, c2) = DuAll(new DuScene { Name = "conflict within the wait window (bus)", Length = ZBus, PeopleFrom = 0, PeopleTo = 30, Oncoming = new[] { 9f } });
            Check(c2.BlockedOthers == 0 && c2.OnZebraStanding == 0, "conflict within the window: 1.4.0 waits at the spot; the bus never blocks the conflicting vehicle");
            Check(b2.BlockedOthers == 0, "1.3.0: same, no blocking");

            var (_, b3, c3) = DuAll(new DuScene { Name = "conflict after the window", PeopleFrom = 0, PeopleTo = 16, Oncoming = new[] { 40f } });
            Check(c3.NeedlessSpot == 0 && c3.BlockedOthers == 0 && c3.ClearStep <= b3.ClearStep, "conflict after the window: drives up, nobody blocked");

            var (a4, b4, c4) = DuAll(new DuScene { Name = "exit blocked", PeopleFrom = 0, PeopleTo = 24, ExitBlockedUntil = 50 });
            Check(c4.EnteredWithBlockedExit == 0 && c4.StoodInJunction == 0 && b4.EnteredWithBlockedExit == 0, "exit blocked: 1.4.0 (and 1.3.0) do not enter");
            Check(a4.OnZebraStanding > 0, "exit blocked, off: the stock car queues on the zebra (reference)");

            var (_, b5, c5) = DuAll(new DuScene { Name = "red / give-way not cleared", PeopleFrom = 0, PeopleTo = 30, LineHoldUntil = 20 });
            Check(c5.StoodInJunction == 0 || c5.ClearStep <= b5.ClearStep, "line hold: the car waits at its line as today");

            var (_, b6, c6) = DuAll(new DuScene { Name = "a person standing on the zebra", PeopleFrom = 0, PeopleTo = 40, Standing = true });
            Check(c6.OnZebraStanding == 0 && c6.ClearStep <= b6.ClearStep, "standing person: bounded wait estimate, never on the zebra");

            var (_, b7, c7) = DuAll(new DuScene { Name = "bus, oncoming stream", Length = ZBus, PeopleFrom = 0, PeopleTo = 20, Oncoming = new[] { 6f, 14f, 22f } });
            Check(c7.BlockedOthers == 0 && c7.OnZebraStanding == 0, "bus with an oncoming stream: the window keeps it at the spot while a vehicle can come; nobody blocked");

            // JR-T-035 (F4): walkers all past the car's way (still on the zebra) and a vehicle inside the drive-up window but not the
            // normal one: 1.4.0 as reviewed held the car at its spot for that traffic; with F4 it drives as 1.3.0 / off
            var f4Scene = new DuScene { Name = "walkers past the car's way, traffic only in the drive-up window", PeopleFrom = 0, PeopleTo = 40, OffWayFrom = 0, Oncoming = new[] { 7f } };
            var (a8, b8, c8) = DuAll(f4Scene);
            DuResult c8pre = DuRun(f4Scene, DuPolicy.V140, f4: false);
            Console.WriteLine($"  drive-up '{f4Scene.Name}': 1.4.0 before F4 [{c8pre}]");
            Check(c8pre.ClearStep > b8.ClearStep, "F4 scene: 1.4.0 before F4 holds the car back for traffic that could not matter (clears later than 1.3.0)");
            Check(c8.NeedlessSpot == 0 && c8.ClearStep == b8.ClearStep && c8.ClearStep == a8.ClearStep && c8.StoodInJunction == b8.StoodInJunction,
                "F4: no drive-up for people who cannot hold the car: drives as 1.3.0 and off");

            // no people: 1.4.0 = 1.3.0, step for step (random oncoming / exit / line schedules)
            var rr = new Random(3303);
            bool same = true;
            for (int i = 0; i < 300 && same; i++)
            {
                var sc = new DuScene { Name = "random", Length = rr.Next(2) == 0 ? ZCar : ZBus, Oncoming = new[] { (float)rr.NextDouble() * 30f, (float)rr.NextDouble() * 30f },
                    ExitBlockedUntil = rr.Next(0, 60), LineHoldUntil = rr.Next(0, 20) };
                DuResult p = DuRun(sc, DuPolicy.V130), q = DuRun(sc, DuPolicy.V140);
                same = p.ClearStep == q.ClearStep && p.StoodInJunction == q.StoodInJunction && p.BlockedOthers == q.BlockedOthers;
            }
            Check(same, "no people on the zebra: 1.4.0 drives exactly as 1.3.0 (300 random scenes)");
            // random scenes with people: 1.4.0 never stands on the zebra and never blocks more than 1.3.0
            bool safe = true;
            int needless130 = 0, needless140 = 0;
            for (int i = 0; i < 300 && safe; i++)
            {
                int from = rr.Next(0, 20);
                var sc = new DuScene { Name = "random people", Length = rr.Next(2) == 0 ? ZCar : ZBus, PeopleFrom = from, PeopleTo = from + rr.Next(4, 30), Standing = rr.Next(4) == 0,
                    Oncoming = new[] { (float)rr.NextDouble() * 30f, (float)rr.NextDouble() * 30f }, ExitBlockedUntil = rr.Next(2) == 0 ? 0 : rr.Next(0, 60) };
                DuResult p = DuRun(sc, DuPolicy.V130), q = DuRun(sc, DuPolicy.V140);
                needless130 += p.NeedlessSpot;
                needless140 += q.NeedlessSpot;
                safe = q.OnZebraStanding == 0 && q.BlockedOthers <= p.BlockedOthers && q.EnteredWithBlockedExit == 0;
            }
            Console.WriteLine($"  drive-up random (300 scenes with people): needless spot holds 1.3.0 {needless130} -> 1.4.0 {needless140}");
            Check(safe, "random scenes with people: 1.4.0 never stands on the zebra, never blocks more than 1.3.0, never enters with the exit blocked");
            Check(needless140 < needless130, "random scenes with people: fewer needless spot holds");
        }

        private static void ZebraDriveUpRepoTests()
        {
            string root = RepoRoot();
            string it = File.ReadAllText(Path.Combine(root, "JunctionRules", "Vanilla", "JRCarLaneSpeedIterator.cs"));
            string wj = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            string du = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRZebraDriveUp.cs"));
            string dev = File.ReadAllText(Path.Combine(root, "DEVIATIONS.md"));
            Check(it.Contains("ref m_PrefabCarData, ref m_LaneOverlapData, ref m_CreatureData);"), "S2-25: BeginZebras gets the game's people lookup");
            Check(it.Contains("m_JR.WhyPeople(num5 != jrPedNum5)") && it.Contains("m_JR.WhyPeopleExtra(num5 != jrPedNum5)"), "S2-08b: people why = spot / zebra / stock, with the spot reason");
            Check(it.Contains("m_Why.Bind(m_JR.WhyGap(jrWhyGap)") && it.Contains("m_Why.Bind(m_JR.WhyGap(jrWhyGw)") && it.Contains("m_Why.Bind(m_JR.WhyGap(jrWhyV)"), "S2-06 / S2-11b / S2-12: gap holds with a drive-up pending = people at the spot, reason traffic");
            Check(wj.Contains("if (m_DriveUp && !isCrossing && m_OvPhase == PointPhase.Deciding && m_Stretch.Spots.StretchOf(u) == m_DriveUpSection)")
                && wj.Contains("m_OvDelta = ZebraDriveUp.Window(m_OvDelta, m_DriveUpDeltaZebra, m_DriveUpWait, m_DriveUpClear);"), "InScope: the safeguard window on the section's vehicle points");
            Check(wj.Contains("if (m_OvZebra && (m_DriveUp || m_CrossNoFit != 0)) today = DriveUpPedestrianDistance(today);"), "PedestrianDistance: the drive-up distance");
            Check(wj.Contains("DriveUpReset();   // JR-T-033") && wj.Contains("if (freeNow && !inBox && m_BoxLookups) FurtherBoxes("), "BeginZebras resets the drive-up; free-now scans without box lookups skip FurtherBoxes");
            Check(du.Contains("freeNow: true, lookPast: false") && !du.Contains("new ") && !du.Contains("string") && !du.Contains("class "), "job side: free-now room, no allocation, no managed types");
            Check(dev.Contains("JR-T-033"), "DEVIATIONS: the 1.4.0 entry");
            // JR-T-035 (review F1 / F4 / A3)
            Check(du.Contains("ZebraDriveUp.Reason r = ZebraDriveUp.Eligible(RuleOn, m_BoxLane, fits, m_Stretch.CanStopBefore(first), true);"), "F1: the drive-up is gated on the Whole junction rule");
            Check(du.Contains("if (!ZebraDriveUp.PeopleCanHold(wait, worst == ExitZebraWait.Stationary)) return;") && du.IndexOf("PeopleCanHold", StringComparison.Ordinal) < du.IndexOf("m_DriveUp = true;", StringComparison.Ordinal),
                "F4: no drive-up for people who cannot hold the car");
            Check(dev.Contains("stock_gap_rule") && dev.Contains("needs the Whole junction"), "DEVIATIONS: drive-up needs the Whole junction rule (F1)");
            Check(dev.Contains("B1's in-way full stop needs"), "DEVIATIONS: A3 line");
            string st = File.ReadAllText(Path.Combine(root, "JunctionRules", "Settings", "JunctionRulesSettings.cs"));
            Check(st.Contains("drives up") && st.Contains("Whole junction rule only"), "settings: the zebra text names the drive-up and its Whole junction rule");
        }
    }
}
