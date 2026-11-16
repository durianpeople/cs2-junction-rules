using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.4 (JR-T-011 part B): road markings under yellow boxes are hidden (Core/MarkingMask.cs maths,
    /// Paint/MarkingHider.cs + Paint/MarkingSaveGuardSystem.cs wiring).
    /// </summary>
    internal static partial class Program
    {
        private static List<V2> Sq(double x0, double z0, double x1, double z1) =>
            new List<V2> { new V2(x0, z0), new V2(x1, z0), new V2(x1, z1), new V2(x0, z1) };

        private static MarkingMask.Curve Line(double x0, double z0, double x1, double z1) =>
            new MarkingMask.Curve(new V3(x0, 0, z0), new V3(x0 + (x1 - x0) / 3, 0, z0 + (z1 - z0) / 3), new V3(x0 + 2 * (x1 - x0) / 3, 0, z0 + 2 * (z1 - z0) / 3), new V3(x1, 0, z1));

        private static bool Near(double a, double b, double tol = 1e-3) => Math.Abs(a - b) <= tol;

        private static void MarkingMaskTests()
        {
            // box 10 x 8 m on a road along X; lane dashes along X at z = 2 (inside), z = 9 (outside, far side of a median)
            var box = new List<IList<V2>> { Sq(10, -4, 20, 4) };

            // a lane dash line from x = 0 to x = 30 through the box: cut [1/3, 2/3]
            var r = MarkingMask.InsideRanges(Line(0, 2, 30, 2), box);
            Check(r.Count == 1 && Near(r[0].Min, 1.0 / 3) && Near(r[0].Max, 2.0 / 3), "lane through the box: one cut exactly at the box sides " + string.Join(",", r));

            // centre line (owner 16 Nov: hidden too): z = 0
            r = MarkingMask.InsideRanges(Line(0, 0, 30, 0), box);
            Check(r.Count == 1 && Near(r[0].Min, 1.0 / 3) && Near(r[0].Max, 2.0 / 3), "centre line under the box is cut as well");

            // the far side of a median (box stops at the median): no cut
            r = MarkingMask.InsideRanges(Line(0, 9, 30, 9), box);
            Check(r.Count == 0, "lane beyond the box (far side of a median) is not cut");

            // lane starting inside the box: range snaps to 0
            r = MarkingMask.InsideRanges(Line(15, 1, 30, 1), box);
            Check(r.Count == 1 && r[0].Min == 0.0 && Near(r[0].Max, 1.0 / 3), "lane starting in the box: cut from exactly 0");

            // lane ending inside: snaps to 1
            r = MarkingMask.InsideRanges(Line(0, 1, 15, 1), box);
            Check(r.Count == 1 && Near(r[0].Min, 2.0 / 3) && r[0].Max == 1.0, "lane ending in the box: cut to exactly 1");

            // node marking wholly inside: [0, 1] (the renderer then draws nothing)
            r = MarkingMask.InsideRanges(Line(12, -2, 18, 2), box);
            Check(r.Count == 1 && r[0].Min == 0.0 && r[0].Max == 1.0, "marking wholly inside the box: whole curve cut");

            // two boxes on one lane (staggered exits): two cuts, sorted
            var two = new List<IList<V2>> { Sq(10, -4, 14, 4), Sq(20, -4, 24, 4) };
            r = MarkingMask.InsideRanges(Line(0, 0, 30, 0), two);
            Check(r.Count == 2 && r[0].Max < r[1].Min && Near(r[0].Min, 10.0 / 30) && Near(r[1].Max, 24.0 / 30), "two separate boxes: two sorted cuts");

            // overlapping boxes (exits facing each other, before part A merges them): one merged cut
            var overlap = new List<IList<V2>> { Sq(10, -4, 18, 1), Sq(14, -1, 22, 4) };
            r = MarkingMask.InsideRanges(Line(0, 0, 30, 0), overlap);
            Check(r.Count == 1 && Near(r[0].Min, 10.0 / 30) && Near(r[0].Max, 22.0 / 30), "overlapping boxes: one continuous cut (no gap at the inner borders)");

            // grazing a corner (shorter than MinCutLength): ignored
            var tri = new List<IList<V2>> { new List<V2> { new V2(10, -4), new V2(20, -4), new V2(15, 0.01) } };
            r = MarkingMask.InsideRanges(Line(0, 0, 30, 0), tri);
            Check(r.Count == 0, "a lane grazing a box tip by a few cm is not cut");

            // curved lane (quarter bend) entering the box from below
            var bend = new MarkingMask.Curve(new V3(15, 0, -20), new V3(15, 0, -5), new V3(15, 0, 0), new V3(30, 0, 0));
            r = MarkingMask.InsideRanges(bend, box);
            Check(r.Count == 1 && r[0].Min > 0 && r[0].Max < 1, "curved lane: one inner cut " + string.Join(",", r));
            if (r.Count == 1)
            {
                V2 a = bend.PositionXZ(r[0].Min), b = bend.PositionXZ(r[0].Max);
                Check(Near(a.Z, -4, 0.01) && Near(b.X, 20, 0.01), $"curved lane: cut ends on the box sides ({a}, {b})");
            }

            // no rings / bounds reject
            Check(MarkingMask.InsideRanges(Line(0, 0, 30, 0), new List<IList<V2>>()).Count == 0, "no box: no cut");
            Check(MarkingMask.InsideRanges(Line(100, 0, 130, 0), box).Count == 0, "lane far away: no cut");

            // L-shaped (non-convex) merged polygon: the notch is not cut
            var ell = new List<IList<V2>> { new List<V2> { new V2(0, 0), new V2(20, 0), new V2(20, 5), new V2(10, 5), new V2(10, 10), new V2(0, 10) } };
            r = MarkingMask.InsideRanges(Line(-5, 7, 25, 7), ell);
            Check(r.Count == 1 && Near(r[0].Min, 5.0 / 30) && Near(r[0].Max, 15.0 / 30), "non-convex (merged) polygon: the notch keeps its marking");

            // same-range comparison
            var a1 = new List<MarkingMask.Range> { new MarkingMask.Range(0.1, 0.2) };
            var a2 = new List<MarkingMask.Range> { new MarkingMask.Range(0.10001, 0.2) };
            var a3 = new List<MarkingMask.Range> { new MarkingMask.Range(0.1, 0.3) };
            Check(MarkingMask.SameRanges(a1, a2) && !MarkingMask.SameRanges(a1, a3) && !MarkingMask.SameRanges(a1, new List<MarkingMask.Range>()), "range lists compare with tolerance");

            // objects: footprint and hide rule
            var fp = MarkingMask.Footprint(new V2(15, 0), new V2(1, 0), new V2(0, 1), -1, 1, -2, 2);
            Check(MarkingMask.SignedArea(fp) > 0 && Near(Math.Abs(MarkingMask.SignedArea(fp)), 8), "footprint: 2 x 4 m, counter-clockwise");
            var rot = MarkingMask.Footprint(new V2(0, 0), new V2(0, -1), new V2(1, 0), -1, 1, -2, 2);   // rotated 90 degrees
            Check(Near(MarkingMask.InsideFraction(rot, new List<IList<V2>> { Sq(-2, -1, 2, 1) }), 1), "rotated footprint lies along X");
            Check(MarkingMask.HideObject(new V2(15, 0), fp, box), "a '40' in the middle of the box is hidden");
            var outside = MarkingMask.Footprint(new V2(30, 0), new V2(1, 0), new V2(0, 1), -1, 1, -2, 2);
            Check(!MarkingMask.HideObject(new V2(30, 0), outside, box), "a '40' outside the box stays");
            var edge = MarkingMask.Footprint(new V2(20.4, 0), new V2(1, 0), new V2(0, 1), -1, 1, -2, 2);   // x 19.4..21.4: 0.6 of 2 m inside
            Check(Near(MarkingMask.InsideFraction(edge, box), 0.3) && MarkingMask.HideObject(new V2(20.4, 0), edge, box), "a '40' straddling the border (30% inside, centre outside) is hidden");
            var sliver = MarkingMask.Footprint(new V2(20.9, 0), new V2(1, 0), new V2(0, 1), -1, 1, -2, 2);   // 0.1 m of 2 m inside
            Check(!MarkingMask.HideObject(new V2(20.9, 0), sliver, box), "an arrow only touching the box border stays");
            Check(Near(MarkingMask.InsideFraction(fp, overlap), 1), "overlapping rings: share capped at 1");
        }

        /// <summary>
        /// The hider takes the merged groups' rings (Core/BoxMerge.Merge, stopped at the median): markings under the
        /// union are cut in one piece, markings on the far carriageway of a divided road stay.
        /// </summary>
        private static void MarkingMergeTests()
        {
            var prm = BoxMergeParams.ForMargin(0.5);
            // divided road along X, median z -1..1; south exits on carriageway 0 (two overlapping), a north exit on carriageway 1
            var s1 = Ex(1, 11, Rect(0, -9, 8, -1), 500, MedianSplit.Bit(0));
            var s2 = Ex(2, 12, Rect(6, -9, 14, -1), 500, MedianSplit.Bit(0));
            var n1 = Ex(3, 13, Rect(20, 1, 28, 9), 500, MedianSplit.Bit(1));
            var inputs = new[] { s1, s2, n1 };
            List<MergedBoxGroup> g = BoxMerge.Merge(inputs, prm);
            Check(g.Count == 2, $"MK the two south boxes merge, the north one stays apart (got {g.Count} groups)");
            int south = BoxMerge.GroupOf(g, new List<BoxMergeInput>(inputs), 1, 11), north = BoxMerge.GroupOf(g, new List<BoxMergeInput>(inputs), 3, 13);
            Check(south >= 0 && north >= 0 && south != north, "MK groups found");
            if (south < 0 || north < 0) return;
            var sr = new List<IList<V2>>(); foreach (List<V3> r in g[south].Rings) sr.Add(BoxPaintGeometry.ToXZ(r));
            var nr = new List<IList<V2>>(); foreach (List<V3> r in g[north].Rings) nr.Add(BoxPaintGeometry.ToXZ(r));

            // near carriageway lane z = -5, x -10..30: one cut over the union x 0..14 (no seam at the overlap)
            var near = MarkingMask.InsideRanges(Line(-10, -5, 30, -5), sr);
            Check(near.Count == 1 && Near(near[0].Min, 10.0 / 40, 2e-3) && Near(near[0].Max, 24.0 / 40, 2e-3), "MK near lane cut once across the merged box " + string.Join(",", near));
            // far carriageway lane z = +5 under nothing from the south group: kept
            Check(MarkingMask.InsideRanges(Line(-10, 5, 30, 5), sr).Count == 0, "MK far side of the median keeps its markings (south group)");
            // median kerb line z = 0 is outside both groups (boxes stop at the median)
            Check(MarkingMask.InsideRanges(Line(-10, 0, 30, 0), sr).Count == 0 && MarkingMask.InsideRanges(Line(-10, 0, 30, 0), nr).Count == 0, "MK nothing on the median is cut");
            // north group cuts only its own carriageway
            Check(MarkingMask.InsideRanges(Line(-10, -5, 30, -5), nr).Count == 0 && MarkingMask.InsideRanges(Line(-10, 5, 30, 5), nr).Count == 1, "MK north group cuts only the north carriageway");
        }

        private static void MarkingRepoTests()
        {
            string root = RepoRoot();
            Check(root != null, "repo root found");
            if (root == null) return;
            string paint = Path.Combine(root, "JunctionRules", "Paint");
            string hider = StripComments(File.ReadAllText(Path.Combine(paint, "MarkingHider.cs")));
            // the hider writes only the render-side buffer / tags, through EntityManager on the main thread
            var writes = new Regex(@"\b(AddComponent|RemoveComponent|AddBuffer|SetComponentData|SetBuffer|CreateEntity|DestroyEntity|EntityCommandBuffer|CommandBuffer)\b(<[^>]+>)?");
            var bad = new List<string>();
            foreach (Match m in writes.Matches(hider))
            {
                string v = m.Value;
                if (v == "AddComponent<BatchesUpdated>" || v == "AddComponent<Hidden>" || v == "RemoveComponent<Hidden>" || v == "RemoveComponent<CutRange>" || v == "AddBuffer<CutRange>") continue;
                bad.Add(v);
            }
            Check(bad.Count == 0, "marking hider writes only CutRange / Hidden / BatchesUpdated: " + string.Join(", ", bad));
            Check(Regex.IsMatch(hider, @"em\.HasBuffer<CutRange>\(lane\)\) \{ foreignLanes\+\+; continue; \}") && Regex.IsMatch(hider, @"em\.HasComponent<Hidden>\(o\)\) \{ foreignObjects\+\+; continue; \}"), "lanes / objects already cut or hidden by someone else are left alone");
            Check(Regex.IsMatch(hider, @"HasComponent<Game\.Net\.CarLane>[\s\S]*PedestrianLane[\s\S]*TrackLane[\s\S]*ParkingLane[\s\S]*ConnectionLane[\s\S]*UtilityLane") && hider.Contains("MeshFlags.Decal"), "only decal-only marking lanes / objects (never zebras, car lanes, signs or poles)");
            // JR-T-013e (G2): strip / restore / clear order is Core/MarkingLedger.cs (behaviour: MarkingLedgerTests); the hider
            // only adapts the EntityManager, logs, and never lets a strip failure pass silently
            Check(hider.Contains("m_Ledger.StripForSave(new EntityWorld(em))") && Regex.Matches(hider, @"Mod\.log\.Critical\(").Count >= 2 && hider.Contains("THIS SAVE MAY CONTAIN HIDDEN ROAD MARKINGS"), "G2 F2: a strip that may leave a cut in the save is logged CRITICAL, never swallowed");
            Check(hider.Contains("public void RemoveCut(Entity lane) => m_Em.RemoveComponent<CutRange>(lane);") && Regex.IsMatch(hider, @"public void EmptyCut\(Entity lane\) \{ if \(m_Em\.HasBuffer<CutRange>\(lane\)\) m_Em\.GetBuffer<CutRange>\(lane\)\.Clear\(\); \}"), "G2 F2: save fallback empties our cut list in place (empty list = whole lane drawn)");
            Check(Regex.IsMatch(hider, @"public void Run\(EntityManager em\)[\s\S]*?if \(p == Pending\.Clear\) \{ ClearNow\(em\); return; \}") && Regex.IsMatch(hider, @"public void ClearNow\(EntityManager em\)[\s\S]*?m_Ledger\.Clear\("), "G2 F3: a clear goes straight to the ledger's clear (re-render even while stripped), no restore first");

            string reg = StripComments(File.ReadAllText(Path.Combine(paint, "PaintRegistration.cs")));
            Check(reg.Contains("updateSystem.UpdateBefore<MarkingSaveGuardSystem>(SystemUpdatePhase.Serialize);"), "save guard runs in the Serialize phase ahead of the serializer");
            Check(reg.Contains("updateSystem.UpdateBefore<MarkingRenderSystem, Game.Rendering.PreCullingSystem>(SystemUpdatePhase.PreCulling);"), "G2 F1: marking writes run before the game's PreCullingSystem (it reads BatchesUpdated the same frame)");
            string render = StripComments(File.ReadAllText(Path.Combine(paint, "MarkingRenderSystem.cs")));
            Check(render.Contains("YellowBoxPaintSystem.RunMarkings(EntityManager);"), "G2 F1: the render system runs the hider");
            string guard = StripComments(File.ReadAllText(Path.Combine(paint, "MarkingSaveGuardSystem.cs")));
            Check(guard.Contains("YellowBoxPaintSystem.StripMarkingsForSave(EntityManager);"), "save guard calls the strip");

            string sys = StripComments(File.ReadAllText(Path.Combine(paint, "YellowBoxPaintSystem.cs")));
            Check(Regex.IsMatch(sys, @"private void Deactivate\(string why\)[\s\S]*?ClearMarkings\(\);"), "painting off puts the markings back");
            Check(Regex.IsMatch(sys, @"private void ApplyMarkings\(\)\s*\{\s*m_MarkVersion = MergedBoxes\.Version;\s*if \(!m_HideMarkings\) \{ ClearMarkings\(\); return; \}"), "setting off = markings shown (0.4.3 look)");
            Check(Regex.IsMatch(sys, @"m_MarkGroups = MarkingGroups\(groups, inputBoxes\);\s*MergedBoxes\.Publish\(inputs, groups, prm\);") && sys.Contains("m_Hider.RequestApply(m_MarkGroups);"), "hiding uses the merged groups (median-clipped rings), one group per MergedBoxGroup");
            // G2 F1: the paint system (after PreCullingSystem) never writes the markings itself; only requests
            var direct = Regex.Matches(sys, @"m_Hider\.(Run|Tick|Apply|Clear|ClearNow)\(");
            var directOk = new List<string>();
            foreach (Match m in direct) directOk.Add(m.Value);
            Check(directOk.Count == 3 && Regex.IsMatch(sys, @"internal static void RunMarkings\(EntityManager em\)[\s\S]*?s\.m_Hider\.Run\(em\)") && Regex.IsMatch(sys, @"internal static void Shutdown\(\)[\s\S]*?s\.m_Hider\.ClearNow\(") && Regex.IsMatch(sys, @"OnDestroy\(\)[\s\S]*?m_Hider\.ClearNow\(EntityManager\)"), "G2 F1: the paint system writes markings only via RunMarkings (render system), Shutdown and OnDestroy: " + string.Join(", ", directOk));
            Check(sys.Contains("private void ClearMarkings() => m_Hider.RequestClear();"), "G2: painting off / setting off requests the clear (done before the next PreCullingSystem)");
            string mod = StripComments(File.ReadAllText(Path.Combine(root, "JunctionRules", "Mod.cs")));
            Check(Regex.IsMatch(mod, @"public void OnDispose\(\)[\s\S]*?PaintControl\.Shutdown\(\)"), "G2 F4: mod dispose puts the markings back");
            Check(Regex.IsMatch(sys, @"foreach \(MergedBoxGroup mg in groups\)[\s\S]*?foreach \(List<V3> r in mg\.Rings\)[\s\S]*?foreach \(int i in mg\.Members\)[\s\S]*?g\.Sources\.Add\(b\.Node\)[\s\S]*?g\.Sources\.Add\(e\)"), "group sources = union of the members' node and edges");
            Check(sys.Contains("if (MergedBoxes.Version != m_MarkVersion) m_NeedMarkings = true;"), "a new merged snapshot re-applies the hide");
            Check(Regex.IsMatch(sys, @"Upload\(\);\s*m_NeedMarkings = true;"), "every box change re-applies the marking hide (removed box: markings back)");
            Check(sys.Contains("m_Hider.Forget();"), "a new city forgets the old entities");

            string set = StripComments(File.ReadAllText(Path.Combine(root, "JunctionRules", "Settings", "JunctionRulesSettings.cs")));
            // JR-T-013e (G2): default OFF until the owner's live gates K1 / K2 pass (probe/G2.md)
            Check(set.Contains("private bool m_HideMarkingsUnderBoxes = false;") && set.Contains("HideMarkingsUnderBoxes = false;") && set.Contains("\"Hide road markings under yellow boxes\""), "setting 'Hide road markings under yellow boxes' exists, default OFF (G2), labelled");
        }
    }
}
