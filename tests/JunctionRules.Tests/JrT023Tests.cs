using System;
using System.Collections.Generic;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// JR-T-023 (1.0.3, owner 19 Nov 2026): a bigger Ctrl+Y hover target for building accesses (Core/AccessPick). An access
    /// lights up with the cursor on (a) its box area on the road (EntranceLength / 2 + 2 m along, road half width + 2 m
    /// across) or (b) the strip from its road point to the building's access point (EntranceLength / 2 + 2 m either side,
    /// to 2 m past the building point); overlaps: the nearest access line wins. The tool and the API's x,z pick share it
    /// (Boxes/BoxTargeting.Resolve -> PickAt).
    /// </summary>
    internal static partial class Program
    {
        private const double PickLen = 8.0;   // the default EntranceLength

        // a 12 m road along +x at z = 0; the building lies north (+z), its access point 15 m from the road centre
        private static AccessPickArea Area(double x, double innerZ = 15, bool inner = true, double halfWidth = 6)
            => new AccessPickArea { Point = new V2(x, 0), Along = new V2(1, 0), HalfWidth = halfWidth, Inner = new V2(x, innerZ), HasInner = inner };

        private static void JrT023PickTests()
        {
            AccessPickArea a = Area(0);
            // (a) inside the box outline (+ margin)
            Check(AccessPick.InBox(a, new V2(0, 0), PickLen) && AccessPick.InBox(a, new V2(5.9, -7.9), PickLen) && AccessPick.InBox(a, new V2(-5.9, 7.9), PickLen), "box area: road point, corners within length/2 + 2 m along and half width + 2 m across");
            Check(!AccessPick.InBox(a, new V2(6.1, 0), PickLen) && !AccessPick.InBox(a, new V2(0, -8.1), PickLen), "box area ends at length/2 + 2 m along and half width + 2 m across");
            // (b) inside the strip road -> building access (pavement / driveway / verge)
            Check(AccessPick.InStrip(a, new V2(3, 12), PickLen) && AccessPick.InStrip(a, new V2(-5.9, 10), PickLen) && AccessPick.InStrip(a, new V2(0, 16.9), PickLen), "strip: driveway / verge between road and building, length/2 + 2 m either side, to 2 m past the building point");
            Check(!AccessPick.InStrip(a, new V2(6.1, 12), PickLen) && !AccessPick.InStrip(a, new V2(0, 17.1), PickLen) && !AccessPick.InStrip(a, new V2(0, -3), PickLen), "strip: not wider, not past the building point + 2 m, not behind the road point");
            Check(AccessPick.Contains(a, new V2(2, 9), PickLen) && !AccessPick.InBox(a, new V2(2, 9), PickLen), "pavement just off the road (9 m from the centre of a 12 m road) picks the access through the strip");
            // outside both
            foreach (V2 p in new[] { new V2(20, 3), new V2(0, -9), new V2(8, 12), new V2(0, 30) })
                Check(!AccessPick.Contains(a, p, PickLen) && AccessPick.Pick(new List<AccessPickArea> { a }, p, PickLen) == -1, $"outside box and strip {p}: nothing");
            // 1.0.2's road reach (length/2 + 2 m round the road point, road hits only) is kept
            bool kept = true;
            for (double ang = 0; ang < 2 * Math.PI; ang += 0.1)
            {
                var p = new V2(Math.Cos(ang) * 5.99, Math.Sin(ang) * 5.99);
                if (Math.Abs(p.Z) <= 6 && !AccessPick.Contains(a, p, PickLen)) kept = false;
            }
            Check(kept, "every road point 1.0.2 picked (within length/2 + 2 m of the road point) still picks the access");
            // overlap -> nearest
            var two = new List<AccessPickArea> { Area(0), Area(7) };
            Check(AccessPick.Pick(two, new V2(4, 10), PickLen) == 1, "two strips hold the cursor: the nearer access line wins (3 m vs 4 m)");
            Check(AccessPick.Pick(two, new V2(3, -2), PickLen) == 0, "two boxes hold the cursor on the road: the nearer access wins");
            Check(AccessPick.Pick(two, new V2(3.5, 5), PickLen) >= 0, "midway between two accesses: one of them, never none");
            Check(Math.Abs(AccessPick.Distance(a, new V2(3, 20)) - Math.Sqrt(9 + 25)) < 1e-9 && Math.Abs(AccessPick.Distance(a, new V2(3, -4)) - 5) < 1e-9, "distance: to the line road point -> building point (clamped at both ends)");
            // the strip follows the drive, not the road normal (diagonal drive)
            var diag = new AccessPickArea { Point = new V2(0, 0), Along = new V2(1, 0), HalfWidth = 5, Inner = new V2(12, 12), HasInner = true };
            Check(AccessPick.Contains(diag, new V2(9, 10), PickLen) && !AccessPick.Contains(diag, new V2(-3, 12), PickLen), "a diagonal drive: the strip runs along it");
            // a road along any direction (the box area turns with the road)
            var rot = new AccessPickArea { Point = new V2(100, 100), Along = new V2(1, 1), HalfWidth = 4, HasInner = false };
            double k = Math.Sqrt(0.5);
            Check(AccessPick.InBox(rot, new V2(100 + 5.5 * k, 100 + 5.5 * k), PickLen) && !AccessPick.InBox(rot, new V2(100 + 6.5 * k, 100 + 6.5 * k), PickLen)
                  && AccessPick.InBox(rot, new V2(100 - 5.5 * k, 100 + 5.5 * k), PickLen) && !AccessPick.InBox(rot, new V2(100 - 6.5 * k, 100 + 6.5 * k), PickLen), "a diagonal road: box area along and across it");
            // degenerate input
            Check(!AccessPick.InStrip(Area(0, inner: false), new V2(0, 12), PickLen) && AccessPick.InBox(Area(0, inner: false), new V2(0, 3), PickLen), "no building point: box area only");
            Check(!AccessPick.InStrip(Area(0, innerZ: 0.05), new V2(0, 1), PickLen), "building point on the road point: no strip");
            var noDir = new AccessPickArea { Point = new V2(0, 0), HalfWidth = 6 };
            Check(AccessPick.InBox(noDir, new V2(4, 4), PickLen) && !AccessPick.InBox(noDir, new V2(5, 5), PickLen), "no road direction: a circle of length/2 + 2 m");
            Check(AccessPick.Pick(null, new V2(0, 0), PickLen) == -1 && AccessPick.Pick(new List<AccessPickArea>(), new V2(0, 0), PickLen) == -1
                  && AccessPick.Pick(new List<AccessPickArea> { a }, new V2(double.NaN, 0), PickLen) == -1, "no areas / non-finite cursor: none");
            Check(AccessPick.Margin == 2.0 && AccessPick.SearchRadius >= 40, "margin 2 m (1.0.2's road reach kept), search radius >= 40 m");
        }

        private static void JrT023RepoTests()
        {
            string root = Path.Combine(RepoRoot(), "JunctionRules");
            string Read(params string[] p) => StripComments(File.ReadAllText(Path.Combine(root, Path.Combine(p))));
            string tg = Read("Boxes", "BoxTargeting.cs"), tool = Read("Boxes", "YellowBoxToolSystem.cs"), api = Read("Api", "JunctionRulesApi.cs");
            // one pick for tool and API
            Check(tool.Contains("m_Targeting.Resolve(hitEntity, hit.m_HitPosition)") && api.Contains("tg.Resolve(any ? hit : Entity.Null, pos)"), "tool and API x,z pick share BoxTargeting.Resolve (API: nothing hit = ground point)");
            Check(tool.Contains("TypeMask.Net | TypeMask.StaticObjects | TypeMask.Terrain"), "tool raycast takes the terrain (ground point between road and building)");
            Check(tg.Contains("AccessPick.Pick(m_PickAreas, new V2(xz.x, xz.y), BoxStore.EntranceLength)") && !tg.Contains("NearestExitOnRoad"), "PickAt uses Core AccessPick with the box length; 1.0.2's road-only reach is gone");
            Check(!tg.Contains("BoxStore.EntranceLength * 0.5 + 2.0"), "no separate road reach");
            // junction targets unchanged: node hit, and a road hit inside an end node's area, before the access areas
            int resolve = tg.IndexOf("internal Target Resolve(Entity hitEntity, float3 hitPos)", StringComparison.Ordinal);
            string body = resolve >= 0 ? tg.Substring(resolve, tg.IndexOf("private bool InNodeArea", resolve, StringComparison.Ordinal) - resolve) : "";
            int node = body.IndexOf("return JunctionTarget(hitEntity)", StringComparison.Ordinal);
            int area = body.IndexOf("InNodeArea(edge.m_Start, hitPos)", StringComparison.Ordinal);
            int edgePick = body.IndexOf("return PickAt(hitPos.xz);", area < 0 ? 0 : area, StringComparison.Ordinal);
            Check(node > 0 && area > node && body.Contains("InNodeArea(edge.m_End, hitPos)") && edgePick > area, "junction first: node hit, then road hit in an end node's area, then the access areas");
            Check(body.Contains("return NearestExitOfBuilding(b, hitPos)"), "building hit: its nearest access, as before");
            Check(tg.Contains("if (x.IsJunction ||"), "junction-kind accesses are not access areas (their node is the target)");
            // cheap per frame: reused lists, one Temp native list, the game's net search tree
            Check(tg.Contains("private readonly HashSet<Entity> m_PickBuildings") && tg.Contains("private readonly List<AccessPickArea> m_PickAreas")
                  && tg.Contains("GetNetSearchTree(true, out JobHandle deps)") && tg.Contains("found.Dispose()"), "PickAt: reused containers, net search tree, Temp list disposed");
        }
    }
}
