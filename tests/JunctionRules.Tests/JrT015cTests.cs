using System;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.9 (JR-T-015, G15b): owner shot 5d341726 (the loop No Vehicle Despawn flagged at No. 40 Alderwood): a van turning
    /// into the entrance stood angled across the near lane, a van behind it stood in the EXIT box, the exiter at the
    /// pavement waited for that van. Westbound lane as in G15LoopTests (70 m, u = (-1960 - x) / 70).
    /// </summary>
    internal static partial class Program
    {
        private static void G15bSideObstacleTests()
        {
            // the dark van (through or entering, 6.5 m) approaches the joint No. 40 band; the angled van's entering lane overlaps
            // the westbound lane around the entrance (x -1992.5 .. -1996.5)
            float entry = G15U(-1979.5f), b1 = G15U(-1998.04f);
            float t0 = G15U(-1992.5f), t1 = G15U(-1996.5f);
            float needed = 6.5f + 1f, windowEnd = b1 + needed / 70f;
            Check(BoxDriving.SideOverlapInWindow(t0, t1, entry, windowEnd), "the entrance overlap lies inside the band the van wants to cross");
            Check(!BoxDriving.SideOverlapInWindow(G15U(-2015f), G15U(-2018f), entry, windowEnd), "a driveway well past the room needed is not looked at");
            Check(!BoxDriving.SideOverlapInWindow(G15U(-1965f), G15U(-1968f), entry, windowEnd), "a driveway behind the band entry is not looked at");
            // the angled van on the entering lane: overlap with the road lane = the lane's first 5 m; its body 0.5..7 m: covers it
            Check(BoxDriving.SideBodyCovers(0.5f, 7f, 0f, 5f), "a van that turned in with its rear still over the road lane stands across it");
            Check(!BoxDriving.SideBodyCovers(6f, 12.5f, 0f, 5f), "a van fully inside the driveway does not");
            Check(!BoxDriving.SideBodyCovers(-9f, -2.5f, 6f, 10f), "an exiter waiting before the pavement (before the overlap on its leaving lane) does not");
            RoomScan r = RoomScan.Begin(6.5f, 1f);
            r.BlockBy(4711, 2);
            Check(!r.HasRoom && r.BlockerIndex == 4711 && r.BlockerVersion == 2, "an obstacle across the path: no room, named for the Blocker");
            r.BlockBy(9, 9);
            Check(r.BlockerIndex == 4711, "the first obstacle stays named");

            string root = RepoRoot();
            string wj = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            string yb = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRYellowBox.cs"));
            string ex = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRExitGiveWay.cs"));
            Check(yb.Contains("ref overlapData, ref pedestrianLanes, role == BoxDriving.EntranceRole.Main);") && !ex.Contains("ref pedestrianLanes, true)")
                && !ex.Contains("sideObstacles: true"), "review 1: side obstacles only for traffic through a band, never in an exit's own room checks (no exit <-> entrant loop)");
            Check(yb.Contains("if (!BoxDriving.Holds(a, room)) { m_LastBlocker = keepBand; continue; }") && yb.Contains("else m_LastBlocker = keepEnter;")
                && yb.Contains("m_LastBlocker = keepLog;") && yb.Contains("m_LastBlocker = keepF1;"), "review 2: only the binding hold's scan names the blocker");
            Check(wj.Contains("bool against = lo.m_CurvePosition.y < lo.m_CurvePosition.x;"), "review 5: a vehicle driving a two-way driveway lane against its curve is measured the right way round");
            Check(wj.Contains("if (sideObstacles && scan.HasRoom) SideObstacles(ref scan, lane,") && wj.Contains("(other.m_Flags & Game.Net.CarLaneFlags.SideConnection) == 0) continue;"),
                "G15b: the room-past scan also reads vehicles across the lane on overlapping driveway lanes");
            Check(wj.Contains("ref prefabCars, ref unsorted, noCredit: true);") && wj.Contains("if (!noCredit && hasCurve && moving.TryGetComponent(e, out Moving m)"),
                "G15b: a vehicle turning in needs room that is free now (no moving-queue credit on the way in)");
        }
    }
}
