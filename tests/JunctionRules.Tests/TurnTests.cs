using System;
using System.IO;
using System.Text.RegularExpressions;
using JunctionRules.Core;
using T = JunctionRules.Core.TurnAcross;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.2.4 (JR-T-002h second addition, owner ruling 14 Nov 2026): "Turning across oncoming traffic gives way". The
    /// Valley x Carlisle case (Medium Road Divided, left-hand traffic): eastbound Carlisle turning right into Valley St
    /// across westbound Carlisle. The deciding-car view mirrors S2-13 / S2-06 / S2-12: stock num6 = 100 - delta; the rule
    /// replaces the delta; then the stock tests use num6.
    /// </summary>
    internal static partial class Program
    {
        /// <summary>The stock geometric tie-break of LaneOverlapSystem (decompile line ~1395) for tangents a (lane1) and b (lane2), xz plane.</summary>
        private static int StockTieBreak(double ax, double az, double bx, double bz, bool lht)
        {
            // MathUtils.Right(v) = (v.y, -v.x), Left(v) = (-v.y, v.x) (Colossal.Mathematics, decompiled).
            double t1, t2;
            if (lht) { t1 = az * bx - ax * bz; t2 = bz * ax - bx * az; }
            else { t1 = -az * bx + ax * bz; t2 = -bz * ax + bx * az; }
            return (t1 > 0 ? 1 : 0) + (t2 > 0 ? -1 : 0);
        }

        private static void TurnAcrossTests()
        {
            const uint fwd = T.Forward, right = T.TurnRight, left = T.TurnLeft, row = T.RightOfWay;
            // Carlisle: both from Carlisle, both RightOfWay (or both none): equal sign status.
            // Turner: eastbound, start edge W(est), end Valley. Westbound straight: start E(ast), end W.
            int dTurner = T.Delta(right | row, fwd | row, ownStartIsOtherEnd: true, otherStartIsOwnEnd: false, 0, true);
            int dStraight = T.Delta(fwd | row, right | row, ownStartIsOtherEnd: false, otherStartIsOwnEnd: true, 0, true);
            Check(dTurner == 1 && dStraight == -1, "LHT right turn across the oncoming straight: turner +1 (gives way), straight -1");

            // Stock geometric tie-break for the same pair, any right turn (clockwise, 0..180 deg) vs oncoming west-bound.
            bool stockAlreadyTurnerYields = true;
            for (int deg = 5; deg < 180; deg += 5)
            {
                double th = deg * Math.PI / 180;
                int n4 = StockTieBreak(Math.Cos(th), -Math.Sin(th), -1, 0, true);
                if (n4 != 1) stockAlreadyTurnerYields = false;
                int rht = StockTieBreak(Math.Cos(th), Math.Sin(th), -1, 0, false);   // RHT left turn (counter-clockwise)
                if (rht != 1) stockAlreadyTurnerYields = false;
            }
            Check(stockAlreadyTurnerYields, "finding: the stock tie-break ALREADY gives a turner across an oncoming straight +1 (LHT and RHT): priority alone is not the Carlisle cause");

            // Deciding-car view. Turner (num6 = 100 - (+1) = 99): westbound feeding / reserving car (100) wins -> turner waits.
            int turnerNum6 = T.ApplyNum6(100 - (-1), -1, dTurner, false);    // even if stock had given the turner -1
            Check(turnerNum6 == 99 && ArrivalProjection.WinsPriority(100, turnerNum6), "fast turner now waits for westbound (num6 99; westbound 100 wins: feeding scan, reservation, look-ahead)");
            // Westbound (num6 = 101): the turner (100) does not win; its reservation alone is discounted.
            int straightNum6 = T.ApplyNum6(100 - 1, 1, dStraight, false);    // even if stock had given the straight +1
            Check(straightNum6 == 101 && !ArrivalProjection.WinsPriority(100, straightNum6), "westbound: num6 101, turner priority 100 does not win");
            Check(T.DiscountReservation(dStraight, 100, straightNum6), "westbound is NOT held by the turner's reservation alone");
            Check(!T.DiscountReservation(dStraight, 108, straightNum6), "an emergency turner's reservation (108) still holds westbound");
            Check(!T.DiscountReservation(dTurner, 100, turnerNum6), "the turner never discounts westbound's reservation");
            // Turner already stopped: no reservation (priority 0 / offset at its spot) -> westbound goes, as before.
            Check(T.DiscountReservation(dStraight, 0, straightNum6) && T.ApplyNum6(101, -1, dStraight, false) == 101, "turner already stopped: unchanged (westbound goes; num6 equal to stock)");

            // RHT mirror: left turn across oncoming.
            Check(T.Delta(left, fwd, true, false, 0, false) == 1 && T.Delta(fwd, left, false, true, 0, false) == -1, "RHT mirror: left turn across the oncoming straight gives way");
            Check(T.Delta(right, fwd, true, false, 0, false) == 0, "RHT: a right turn is not across oncoming (stock)");
            // LHT left turn: not across oncoming.
            Check(T.Delta(left, fwd, true, false, 0, true) == 0 && T.Delta(fwd, left, false, true, 0, true) == 0, "LHT left turn: unaffected (stock)");
            Check(T.Delta(T.GentleTurnRight, fwd, true, false, 0, true) == 1 && T.Delta(T.UTurnRight, fwd, true, false, 0, true) == 1, "LHT gentle right / U-turn across oncoming: turner gives way");
            // Not oncoming: straight from a third edge (its end is not the turner's start).
            Check(T.Delta(right, fwd, false, false, 0, true) == 0 && T.Delta(fwd, right, false, false, 0, true) == 0, "straight from another edge (not oncoming): stock");
            Check(T.Delta(right, right, true, true, 0, true) == 0, "two turners: stock");
            Check(T.Delta(right, left, true, false, 0, true) == 0, "oncoming turn (not straight): stock");

            // Sign status differences: stock decides.
            Check(T.Delta(right | T.Stop, fwd, true, false, 0, true) == 0 && T.Delta(right, fwd | T.Yield, true, false, 0, true) == 0, "Stop / Yield difference: stock");
            Check(T.Delta(right | row, fwd, true, false, 0, true) == 0 && T.Delta(right, fwd | row, true, false, 0, true) == 0, "RightOfWay difference: stock");
            Check(T.Delta(right, fwd, true, false, 1, true) == 0 && T.Delta(right, fwd, true, false, -1, true) == 0, "sign / signal override for the overlap (num8 != 0): stock");
            Check(T.Delta(right | T.Approach | T.Stop, fwd, true, false, 0, true) == 1, "Approach lane: Stop / Yield / RightOfWay cleared before comparing, as stock");
            // Space request zeroing as stock.
            Check(T.ApplyNum6(99, 1, 1, true) == 100 && T.ApplyNum6(101, -1, -1, true) == 101, "space request: positive delta zeroed, as stock num9");
            Check(T.ApplyNum6(97, 3, 0, false) == 97, "delta 0: num6 unchanged");

            // Setting off / rule off = 0.2.3: the turn bits exist only with the rule.
            Check(Stage2Mode.For(JunctionGapRule.Stock, false, JunctionWaitingSpot.BeforeFirstCrossing, true, true) == 0, "rule off: mode 0 whatever the turn setting (rule-off path identical)");
            int on = Stage2Mode.For(JunctionGapRule.WholeJunction, false, JunctionWaitingSpot.BeforeFirstCrossing, true, true);
            int off = Stage2Mode.For(JunctionGapRule.WholeJunction, false, JunctionWaitingSpot.BeforeFirstCrossing, false, true);
            Check((on & Stage2Mode.TurnGivesWay) != 0 && (on & Stage2Mode.LeftHandTraffic) != 0 && off == Stage2Mode.Rule, "setting on: turn + LHT bits; off: 0.2.3 mode");
            var sw = new GapRuleSwitch();
            sw.Step(JunctionGapRule.WholeJunction, false, out _, JunctionWaitingSpot.BeforeFirstCrossing, true, true);
            string note = sw.Step(JunctionGapRule.WholeJunction, false, out _, JunctionWaitingSpot.BeforeFirstCrossing, false, true);
            Check(note != null && note.Contains("switched") && !note.Contains("turning"), "switching the setting logs at the next step");
        }

        private static void TurnAcrossRepoTests()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string dec = Path.Combine(home, "Code", "decomp", "yb-2026-11-13", "full");
            string flags = Path.Combine(dec, "Game.Net", "CarLaneFlags.cs");
            string lo = Path.Combine(dec, "Game.Net", "LaneOverlapSystem.cs");
            if (File.Exists(flags) && File.Exists(lo))
            {
                string f = File.ReadAllText(flags);
                Check(f.Contains("Unsafe = 1u,") && f.Contains("UTurnLeft = 2u,") && f.Contains("TurnLeft = 0x10u,") && f.Contains("TurnRight = 0x20u,") && f.Contains("Yield = 0x400u,")
                    && f.Contains("Stop = 0x800u,") && f.Contains("UTurnRight = 0x20000u,") && f.Contains("GentleTurnLeft = 0x40000u,") && f.Contains("GentleTurnRight = 0x80000u,")
                    && f.Contains("Forward = 0x100000u,") && f.Contains("Approach = 0x200000u,") && f.Contains("RightOfWay = 0x4000000u,"), "CarLaneFlags values mirrored by TurnAcross");
                string l = File.ReadAllText(lo);
                Check(l.Contains("carLane1.m_Flags &= ~(CarLaneFlags.Yield | CarLaneFlags.Stop | CarLaneFlags.RightOfWay);"), "stock clears signs on Approach lanes (SignStatus)");
                int iStop = l.IndexOf("if ((carLaneFlags & CarLaneFlags.Stop) != 0)", StringComparison.Ordinal);
                int iRow = l.IndexOf("else if ((carLaneFlags & CarLaneFlags.RightOfWay) != 0)", iStop < 0 ? 0 : iStop, StringComparison.Ordinal);
                int iUns = l.IndexOf("else if ((carLaneFlags & CarLaneFlags.Unsafe) != 0)", iRow < 0 ? 0 : iRow, StringComparison.Ordinal);
                int iLht = l.IndexOf("(!m_LeftHandTraffic)", iUns < 0 ? 0 : iUns, StringComparison.Ordinal);
                Check(iStop > 0 && iRow > iStop && iUns > iRow && iLht > iUns && iLht - iStop < 3000,
                    "stock priority delta: Stop, Yield, RightOfWay, Unsafe, then the geometric tie-break with m_LeftHandTraffic (SignFlags)");
                Check(l.Contains("new float2(math.dot(MathUtils.Right(xz5), xz6), math.dot(MathUtils.Right(xz6), xz5))"), "LHT tie-break uses MathUtils.Right (StockTieBreak)");
            }
            else
            {
                Console.WriteLine("note: decompile not found, turn pins not checked");
            }
            string root = RepoRoot();
            if (root == null) { Check(false, "repo root found"); return; }
            string iter = File.ReadAllText(Path.Combine(root, "JunctionRules", "Vanilla", "JRCarLaneSpeedIterator.cs"));
            int n6 = iter.IndexOf("num6 -= num9;", StringComparison.Ordinal);
            int s13 = iter.IndexOf("num6 = m_JR.TurnAcrossPriority(jrInScope, m_Lane, num6, num8, num9, requestSpace, ref m_CarLaneData, ref m_LaneReservationData);", StringComparison.Ordinal);
            int s12 = iter.IndexOf("float jrFeedSpeed = m_JR.FeedingLaneYield(", StringComparison.Ordinal);
            int res = iter.IndexOf("if (offset > num10 || priority2 > num6)", StringComparison.Ordinal);
            Check(n6 > 0 && s13 > n6 && s12 > s13 && res > s12, "S2-13 right after num6 -= num9, before S2-12 and the stock reservation test");
            string job = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            Check(job.Contains("if (!inScope || !RuleOn || (m_Mode & Stage2Mode.TurnGivesWay) == 0 || num8 != 0) return num6;"), "turn rule only with rule + setting, no override");
            Check(job.Contains("m_OvDiscountReservation = false;") && job.Contains("if (m_OvDiscountReservation)"), "discount reset per overlap and applied in Yield (S2-06)");
            string sys = File.ReadAllText(Path.Combine(root, "JunctionRules", "Systems", "CarDrivingSwitchSystem.cs"));
            Check(sys.Contains("Game.City.CityConfigurationSystem") && sys.Contains(".leftHandTraffic"), "traffic side from the game's CityConfigurationSystem");
            string set = File.ReadAllText(Path.Combine(root, "JunctionRules", "Settings", "JunctionRulesSettings.cs"));
            Check(set.Contains("TurnAcrossGivesWay = true;"), "setting defaults on");
        }
    }
}
