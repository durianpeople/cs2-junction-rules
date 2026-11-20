using System;
using System.Collections.Generic;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// JR-T-033 item B1 (1.4.0, owner 20 Nov 2026): cars entering or leaving a building access do not treat the pavement
    /// as a zebra (no section, no wait at the spot for people there), and the 0.3.2 full stop applies only to a person in the
    /// car's way, not to one standing beside it (held at the edge by the game). Core/AccessCrossing.cs.
    /// </summary>
    internal static partial class Program
    {
        private static void AccessCrossingCoreTests()
        {
            Check(AccessCrossing.IsAccessLane(0x8u) && AccessCrossing.IsAccessLane(0x8u | 0x400u) && !AccessCrossing.IsAccessLane(0x400u) && !AccessCrossing.IsAccessLane(0u),
                "access lane = SideConnection (way in or way out); a Yield-only lane is not");
            Check(ExitGiveWay.IsDrivewayExitLane(0x8u | 0x400u) && AccessCrossing.IsAccessLane(0x8u | 0x400u) && EntryGiveWay.IsDrivewayEntryLane(0x8u) && AccessCrossing.IsAccessLane(0x8u),
                "both driveway directions are accesses");

            // pavement lane 20 m, the car's range on it 8.5 .. 11.5 m (0.425 .. 0.575)
            const float len = 20f, a = 0.425f, b = 0.575f;
            Check(AccessCrossing.PersonInWay(0.5f, a, b, len) && AccessCrossing.PersonInWay(0.5f, b, a, len), "a person in the middle of the car's range is in its way (either order)");
            Check(!AccessCrossing.PersonInWay(a, a, b, len) && !AccessCrossing.PersonInWay(b, a, b, len), "a person held at the range edge (the game's stop point) is beside, not in the way");
            Check(CrossingPush.PersonInPath(a, a, b, len), "G23's widened test counts the edge person as in the path (the 1.3.0 mutual wait)");
            Check(!AccessCrossing.PersonInWay(0.3f, a, b, len) && !AccessCrossing.PersonInWay(0.7f, a, b, len), "a person well beside the range is not in the way");
            Check(AccessCrossing.PersonInWay((8.5f + 0.3f) / len, a, b, len), "0.3 m inside the range: in the way");
            Check(AccessCrossing.PersonInWay(0.5f, 0.5f, 0.5f, len) && !AccessCrossing.PersonInWay(0.51f, 0.5f, 0.5f, len), "a range narrower than the shrink: its middle point");

            Check(AccessCrossing.FullStopApplies(true, true, false) == false && AccessCrossing.FullStopApplies(true, false, true) && AccessCrossing.FullStopApplies(false, true, false),
                "full stop: on an access only in the way; elsewhere G23 unchanged");
            const int pave = 0, marked = ZebraSections.PedestrianCrosswalk, removed = ZebraSections.PedestrianCrosswalk | ZebraSections.PedestrianUnsafe;
            Check(AccessCrossing.SectionKind(true, pave, ZebraSections.Zebra) == 0 && AccessCrossing.SectionKind(true, pave, ZebraSections.SoftZebra) == 0
                && AccessCrossing.SectionKind(false, marked, ZebraSections.Zebra) == ZebraSections.Zebra && AccessCrossing.SectionKind(false, marked, ZebraSections.SoftZebra) == ZebraSections.SoftZebra
                && AccessCrossing.SectionKind(false, pave, 0) == 0, "section kind: an access's pavement is never a zebra; real zebras unchanged");
            Check(!AccessCrossing.CrossingInRule(true, pave) && AccessCrossing.CrossingInRule(false, pave) && AccessCrossing.CrossingInRule(false, marked),
                "an access's pavement is no crossing of the whole-junction rule");
            // JR-T-035 (review F3): a marked zebra an access lane crosses stays a zebra (only the unmarked pavement is exempt)
            Check(AccessCrossing.SectionKind(true, marked, ZebraSections.ZebraKind(marked, false, 0)) == ZebraSections.Zebra
                && AccessCrossing.SectionKind(true, marked, ZebraSections.ZebraKind(marked, true, WholePathRule.SignalStop)) == ZebraSections.SoftZebra,
                "access lane + marked zebra: stays a zebra (soft zebra when signalised and red for people)");
            Check(AccessCrossing.CrossingInRule(true, marked) && !AccessCrossing.AccessPavement(true, marked) && AccessCrossing.AccessPavement(true, pave) && !AccessCrossing.AccessPavement(false, pave),
                "access lane + marked zebra: a crossing of the rule, not the exempt pavement");
            Check(AccessCrossing.AccessPavement(true, removed) == false && AccessCrossing.SectionKind(true, removed, ZebraSections.ZebraKind(removed, false, 0)) == 0,
                "a removed zebra (Crosswalk | Unsafe) over an access: not the B1 pavement (has the Crosswalk flag), no zebra either (ZebraKind)");
            var mz = new ZElems { Items = new[] { (AccessCrossing.SectionKind(true, marked, ZebraSections.ZebraKind(marked, false, 0)), 0.05f, 0.30f), Cv(0.45f, 0.65f), Cv(0.70f, 0.90f) } };
            Check(ZebraSections.Build(ref mz, true, false, 14f, M, ZCar, out ZebraLayout lm) && lm.Spots.Count >= 1, "access lane + marked zebra: the zebra section is built as for any zebra");

            // zebra sections: a leaving drive (pavement, then two road lanes). 1.3.0 (G7): pavement = Z, one section; 1.4.0: none.
            var g7 = new ZElems { Items = new[] { Zb(0.05f, 0.30f), Cv(0.45f, 0.65f), Cv(0.70f, 0.90f) } };
            Check(ZebraSections.Build(ref g7, true, false, 14f, M, ZCar, out ZebraLayout l130) && l130.Spots.Count >= 1, "1.3.0: the driveway pavement makes a zebra section");
            var b1 = new ZElems { Items = new[] { (AccessCrossing.SectionKind(true, 0, ZebraSections.Zebra), 0.05f, 0.30f), Cv(0.45f, 0.65f), Cv(0.70f, 0.90f) } };
            Check(!ZebraSections.Build(ref b1, true, false, 14f, M, ZCar, out _), "1.4.0: no zebra section on an access (people there never hold the car at a spot)");
            var real = new ZElems { Items = new[] { (AccessCrossing.SectionKind(false, ZebraSections.PedestrianCrosswalk, ZebraSections.Zebra), 0.05f, 0.30f), Cv(0.45f, 0.65f), Cv(0.70f, 0.90f) } };
            Check(ZebraSections.Build(ref real, true, false, 14f, M, ZCar, out ZebraLayout lr) && lr.Spots.Count == l130.Spots.Count, "a real zebra on a road lane: sections unchanged");

            Check(WhyRules.Id(WhyRule.JrAccessPersonInWay) == "jr_access_person_in_way" && (int)WhyRule.JrAccessPersonInWay == 57 && WhyRules.IsJr(WhyRule.JrAccessPersonInWay),
                "why: jr_access_person_in_way = 57");
            Check(Stage2Counters.AccessPeopleStanding == Stage2Counters.Count - 2 && Stage2Counters.AccessCarsHeld == Stage2Counters.Count - 1, "access counters appended at the end");
            long[] d = new long[Stage2Counters.Count];
            d[Stage2Counters.AccessPeopleStanding] = 7; d[Stage2Counters.AccessCarsHeld] = 2;
            string f = Stage2Counters.Format(2, d, 0, 0, 2, 2, 2);
            Check(f.Contains("people standing on a building access 7") && f.Contains("cars held by a person at a building access 2"), "access counters in the debug line");
        }

        // ---- offline access scene: one car at a time crosses the pavement of a building access; people walk along it ----

        internal enum AcPolicy { Off, V130, V140 }

        internal struct AcResult
        {
            public int Cars, PeopleStandingSteps, CarHeldSteps, PushedInWay, BrakedForWalker, PeoplePassed, PeoplePasses;
            public float CarWait, PersonWait;
            public override string ToString() =>
                $"cars {Cars}, people standing {PeopleStandingSteps}, car held {CarHeldSteps}, car wait {CarWait:0}s, people wait {PersonWait:0}s, walked through {PeoplePassed}, people passes {PeoplePasses}, into a person in the way {PushedInWay}";
        }

        private sealed class AcPerson
        {
            public float Pos, Dir, Speed, StandLeft, Waited, HeldFor;
            public bool Loiter, HeldByCar, Done;
        }

        private const float AcPave = 20f, AcRangeA = 8.5f, AcRangeB = 11.5f, AcCrossStart = 0f, AcCrossEnd = 3.5f, AcCarLen = 4.5f, AcDt = 0.1f;

        /// <summary>
        /// One run: cars arrive every <paramref name="carGap"/> s (one at a time on the access); walkers along the pavement every
        /// <paramref name="walkGap"/> s from either end at 1.3 m/s; a fraction stand for 4-12 s at a random point. Game model
        /// (as CreatureTargetIterator / CheckPedestrian): a walker reaching the car's range while the car moves toward it or
        /// occupies it waits at the range edge (standing); it walks on once the car has stood 2 s or cleared. Car: the game's
        /// check stops for a walking person in its range and creeps (3 m/s) past a standing one; 1.3.0 adds the 0.3.2 full
        /// stop for a standing person in the G23 widened path, 1.4.0 only for one in the car's way (B1).
        /// </summary>
        internal static AcResult AcRun(int seed, AcPolicy pol, float seconds, float walkGap, float carGap, float loiterShare)
        {
            var rng = new Random(seed);
            var people = new List<AcPerson>();
            var res = default(AcResult);
            float nextWalker = 0f, nextCar = 0f, carX = 0f, carV = 0f, carStood = 0f;
            bool car = false;
            for (float t = 0f; t < seconds; t += AcDt)
            {
                if (t >= nextWalker)
                {
                    bool left = rng.NextDouble() < 0.5;
                    var p = new AcPerson { Pos = left ? 0f : AcPave, Dir = left ? 1f : -1f, Speed = 1.3f };
                    if (rng.NextDouble() < loiterShare) { p.Loiter = true; p.StandLeft = 4f + (float)rng.NextDouble() * 8f; p.Waited = 2f + (float)rng.NextDouble() * (AcPave - 4f); }
                    people.Add(p);
                    nextWalker = t + walkGap * (0.5f + (float)rng.NextDouble());
                }
                if (!car && t >= nextCar) { car = true; carX = -25f; carV = 8f; carStood = 0f; }

                bool carOn = car && carX + 0f >= AcCrossStart - 0.01f && carX - AcCarLen < AcCrossEnd;   // carX = front
                bool carComing = car && carX < AcCrossStart && carV > 0.5f && AcCrossStart - carX < 8f;
                // people move
                foreach (var p in people)
                {
                    if (p.Done) continue;
                    if (p.Loiter && p.StandLeft > 0f && Math.Abs(p.Pos - p.Waited) < 0.7f) { p.StandLeft -= AcDt; continue; }
                    float next = p.Pos + p.Dir * p.Speed * AcDt;
                    float edge = p.Dir > 0f ? AcRangeA : AcRangeB;
                    bool entering = p.Dir > 0f ? p.Pos <= edge && next > edge : p.Pos >= edge && next < edge;
                    if (entering && (carOn || carComing) && !(carStood >= 2f && !carOn))
                    {
                        // JR people pass (rule on, Systems/PeoplePassSystem.cs): held 2 s by a standing car on the access -> walks through
                        if (pol != AcPolicy.Off && carOn && carV < 0.5f && p.HeldFor >= 2f) { res.PeoplePasses++; p.HeldFor = 0f; p.HeldByCar = false; p.Pos = next; continue; }
                        p.Pos = edge; p.HeldByCar = true; p.HeldFor += AcDt; res.PersonWait += AcDt;
                        continue;
                    }
                    p.HeldByCar = false; p.HeldFor = 0f;
                    p.Pos = next;
                    if (p.Pos < 0f || p.Pos > AcPave) { p.Done = true; res.PeoplePassed++; }
                }
                if (!car) continue;

                // car limit from the people on the pavement (only before the car has cleared it)
                float limit = 8f;
                bool held = false, standingSeen = false;
                foreach (var p in people)
                {
                    if (p.Done || carX - AcCarLen >= AcCrossEnd) continue;
                    bool counted = p.Pos >= AcRangeA - 0.5f && p.Pos <= AcRangeB + 0.5f && AcCrossStart - carX < 20f;
                    if (!counted) continue;
                    bool standing = p.HeldByCar || (p.Loiter && p.StandLeft > 0f && Math.Abs(p.Pos - p.Waited) < 0.7f);
                    float stop = carX < AcCrossStart - 1f ? (float)Math.Sqrt(2f * 5f * Math.Max(0f, AcCrossStart - 1f - carX)) : 0f;
                    float v;
                    if (!standing) { v = stop; if (v < carV) res.BrakedForWalker++; }
                    else
                    {
                        standingSeen = true;
                        bool inPathG23 = CrossingPush.PersonInPath(p.Pos / AcPave, AcRangeA / AcPave, AcRangeB / AcPave, AcPave);
                        bool inWay = AccessCrossing.PersonInWay(p.Pos / AcPave, AcRangeA / AcPave, AcRangeB / AcPave, AcPave);
                        bool full = pol == AcPolicy.Off ? false : AccessCrossing.FullStopApplies(pol == AcPolicy.V140, inPathG23, inWay);
                        v = full ? stop : CrossingPush.CreepSpeed;
                    }
                    if (v < limit) limit = v;
                    if (v < CrossingPush.CreepSpeed) held = true;
                }
                // never into a person in the way: the car's body moving over the pavement while a standing person is in its way
                foreach (var p in people)
                {
                    if (p.Done || !carOn || carV < 0.5f) continue;
                    bool standing = p.HeldByCar || (p.Loiter && p.StandLeft > 0f && Math.Abs(p.Pos - p.Waited) < 0.7f);
                    if (standing && AccessCrossing.PersonInWay(p.Pos / AcPave, AcRangeA / AcPave, AcRangeB / AcPave, AcPave)) { res.PushedInWay++; break; }
                }
                if (standingSeen) res.PeopleStandingSteps++;
                float target = Math.Min(limit, 8f);
                carV = target < carV ? Math.Max(target, carV - 8f * AcDt) : Math.Min(target, carV + 3f * AcDt);
                if (carV < 0.5f)
                {
                    carStood += AcDt; res.CarWait += AcDt;
                    if (held) res.CarHeldSteps++;
                }
                else carStood = 0f;
                carX += carV * AcDt;
                if (carX - AcCarLen > AcCrossEnd + 2f) { car = false; res.Cars++; nextCar = t + carGap; }
            }
            return res;
        }

        private static void AccessCrossingSceneTests()
        {
            AcResult offT = default, v13T = default, v14T = default;
            int seeds = 40;
            for (int s = 0; s < seeds; s++)
            {
                AcResult o = AcRun(1000 + s, AcPolicy.Off, 600f, 3f, 4f, 0.15f), p = AcRun(1000 + s, AcPolicy.V130, 600f, 3f, 4f, 0.15f), q = AcRun(1000 + s, AcPolicy.V140, 600f, 3f, 4f, 0.15f);
                offT = Sum(offT, o); v13T = Sum(v13T, p); v14T = Sum(v14T, q);
            }
            Console.WriteLine($"  access (40 x 600 s, walker every ~3 s, 15% stand 4-12 s): off [{offT}] | 1.3.0 [{v13T}] | 1.4.0 [{v14T}]");
            Check(v13T.PushedInWay == 0 && v14T.PushedInWay == 0, "safety floor: with the rule on no car moves over a person standing in its way (1.3.0 and 1.4.0)");
            Check(offT.PushedInWay > 0, "the scene has people standing in the way (stock creeps into them)");
            Check(v14T.CarHeldSteps < v13T.CarHeldSteps / 2, "B1: cars held at the access far less than 1.3.0 (no full stop for a person beside the car)");
            Check(v14T.Cars > v13T.Cars, "B1: more cars through the access than 1.3.0");
            Check(v14T.PeoplePasses < v13T.PeoplePasses / 2, "B1: far fewer people passes at the access (the mutual-wait breaker); the rest are cars stopped for a person in the way");
            Check(v14T.CarHeldSteps >= offT.CarHeldSteps, "1.4.0 still holds for people in the way (at least stock's holds)");

            // direct cases
            Check(!AccessCrossing.FullStopApplies(true, CrossingPush.PersonInPath(AcRangeA / AcPave, AcRangeA / AcPave, AcRangeB / AcPave, AcPave), AccessCrossing.PersonInWay(AcRangeA / AcPave, AcRangeA / AcPave, AcRangeB / AcPave, AcPave)),
                "driveway, a person held at the edge: the car goes (creeps like stock)");
            Check(AccessCrossing.FullStopApplies(true, true, AccessCrossing.PersonInWay(10f / AcPave, AcRangeA / AcPave, AcRangeB / AcPave, AcPave)), "driveway, a person directly in the path: full stop");
        }

        private static AcResult Sum(AcResult a, AcResult b) => new AcResult
        {
            Cars = a.Cars + b.Cars, PeopleStandingSteps = a.PeopleStandingSteps + b.PeopleStandingSteps, CarHeldSteps = a.CarHeldSteps + b.CarHeldSteps,
            PushedInWay = a.PushedInWay + b.PushedInWay, PeoplePasses = a.PeoplePasses + b.PeoplePasses, BrakedForWalker = a.BrakedForWalker + b.BrakedForWalker, PeoplePassed = a.PeoplePassed + b.PeoplePassed,
            CarWait = a.CarWait + b.CarWait, PersonWait = a.PersonWait + b.PersonWait,
        };

        private static void AccessCrossingRepoTests()
        {
            string root = RepoRoot();
            string wj = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            string it = File.ReadAllText(Path.Combine(root, "JunctionRules", "Vanilla", "JRCarLaneSpeedIterator.cs"));
            string dev = File.ReadAllText(Path.Combine(root, "DEVIATIONS.md"));
            Check(wj.Contains("if (isCrossing && !m_OvZebra && !AccessCrossing.CrossingInRule(m_LaneAccess, pedestrianFlags))"), "InScope: an access's pavement is the game's own check");
            Check(wj.Contains("m_LaneAccess = IsAccess(lane, ref carLanes);   // JR-T-033 (B1)\n            if (!m_NodeLaneData.HasComponent(lane)) return;"), "BeginLane sets the access flag before any early return");
            Check(wj.Contains("if (accessLane) inPath = AccessCrossing.FullStopApplies(true, inPath, AccessCrossing.PersonInWay("), "NoPushPedestrian: full stop at an access only for a person in the way");
            Check(it.Contains("m_Curve.m_Length, JunctionRules.Stage2.JRWholeJunction.IsAccessPavement(lane, m_Lane, ref m_CarLaneData, ref m_PedestrianLaneData));") && it.Contains("m_Why.Bind(m_MaxSpeed < jrWhyPed ? m_JR.WhyNoPush() :"),
                "S2-08b passes the access flag (only evaluated with the no-push rule on) and names the access full stop");
            Check(!wj.Contains("m_SidewalkZebra"), "the G7 sidewalk zebra is gone");
            Check(wj.Contains("|| AccessCrossing.AccessPavement(true, (int)ped.m_Flags));") && it.Contains("(int)jrPedLane.m_Flags);   // JR-T-035 (F3)"),
                "F3: S2-08b and InScope exempt only the unmarked pavement of an access");
            Check(dev.Contains("A marked zebra an access lane crosses stays a zebra"), "DEVIATIONS: F3");
            Check(dev.Contains("JR-T-033 B1"), "DEVIATIONS documents B1");
        }
    }
}
