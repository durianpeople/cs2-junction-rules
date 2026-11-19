using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.Mathematics;
using Game.Net;
using JunctionRules.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace JunctionRules.Boxes
{
    /// <summary>
    /// 0.4.10-probe (JR-T-016, G18 / G19, owner 17 Nov 2026: "driving must use exactly the painted (merged) outline for every
    /// box kind, through traffic included"): the driving bands of the boxed exits / entrances, made from the SAME window the
    /// paint draws: Boxes/BoxShapes.Entrance with the published shape parameters (BoxStore.Params: exit box length, kerb
    /// inset, marking margin), boxes on one road less than ExitBands.MergeGapMetres apart built as ONE box over the joint
    /// window when the joint window fits (Core/ExitBoxWindows.Chains + BoxShapes.Entrance with the joint window, as
    /// Paint/YellowBoxPaintSystem.JointBoxes), and a box the paint does not draw (no outline: no window, a marking or mouth
    /// in the way) is not driven either. Each band carries its window's end points on the edge's centre curve; the job
    /// projects them onto every lane (Core/LaneWindow.cs). 0.4.3-0.4.9 gave the job the exit's curve position t and the job
    /// laid the box length around t mapped linearly through EdgeLane.m_EdgeDelta, which is metres off the paint because lanes
    /// run mouth to mouth (Core/LaneWindow.cs). Main thread; the shape work runs only when an input changes (cached).
    /// </summary>
    internal static class DrivingBands
    {
        /// <summary>One boxed exit or entrance (Boxes/BoxFacts.BoxedExitBands).</summary>
        internal struct Source
        {
            public Entity Building;
            public BoxFacts.Exit Exit;
            public BoxFacts.Access Access;
            public V2 BuildingPos;
        }

        /// <summary>
        /// 0.4.10 review F3 / F4: the window of each source, cached by its own fingerprint (only a changed source runs the
        /// shape code again), and each joint window's verdict by its own key.
        /// </summary>
        private static Dictionary<string, (bool Ok, double From, double To, uint Mask)> s_Windows = new Dictionary<string, (bool, double, double, uint)>();
        private static Dictionary<string, bool> s_Joint = new Dictionary<string, bool>();

        /// <summary>
        /// The bands for these sources (one per drawn box; a joint box once). 'notPainted' = sources the paint would not draw
        /// (not driven); 'joint' = joint boxes (two or more sources built as one).
        /// </summary>
        internal static List<ExitBand> Build(EntityManager em, List<Source> sources, out int notPainted, out int joint)
        {
            BoxShapeParams p = BoxStore.Params;
            string prm = p.Describe();
            var bands = new List<ExitBand>();
            notPainted = 0;
            joint = 0;
            var ok = new List<int>();
            var windows = new List<ExitWindow>();
            var keys = new List<string>();
            var nextWindows = new Dictionary<string, (bool, double, double, uint)>();
            for (int i = 0; i < sources.Count; i++)
            {
                Source src = sources[i];
                string key = prm + "|" + Fingerprint(em, src);
                if (!s_Windows.TryGetValue(key, out var w))
                {
                    BoxShapes.Shape shape = BoxShapes.Entrance(em, src.Access, p.KerbInset, p.EntranceLength, p.MarkingMargin, src.BuildingPos);
                    w = (shape.Outline != null, shape.From, shape.To, shape.Carriageways);
                }
                nextWindows[key] = w;
                if (!w.Ok) { notPainted++; continue; }
                Entity e = src.Access.Edge;
                ok.Add(i);
                keys.Add(key);
                windows.Add(new ExitWindow(SortedKeys.KeyOf(e.Index, e.Version), w.Mask, w.From, w.To));
            }
            s_Windows = nextWindows;
            var nextJoint = new Dictionary<string, bool>();
            bool Fits(int m, double from, double to)
            {
                Source first = sources[ok[m]];
                string jk = keys[m] + "|joint " + from.ToString("R", CultureInfo.InvariantCulture) + ".." + to.ToString("R", CultureInfo.InvariantCulture);
                if (!s_Joint.TryGetValue(jk, out bool fits))
                    fits = BoxShapes.Entrance(em, first.Access, p.KerbInset, p.EntranceLength, p.MarkingMargin, first.BuildingPos, from, to).Outline != null;
                nextJoint[jk] = fits;
                return fits;
            }
            foreach (var box in ExitBoxWindows.Assemble(windows, ExitBands.MergeGapMetres, Fits))
            {
                int m0 = box.Members[0];
                if (box.Members.Count > 1) joint++;
                if (Window(em, sources[ok[m0]].Access.Edge, box.From, box.To, windows[m0].Carriageways, out ExitBand b)) bands.Add(b);
            }
            s_Joint = nextJoint;
            return bands;
        }

        /// <summary>The band over [from, to] metres along the edge's centre curve: its end points (world x / z) and middle t.</summary>
        private static bool Window(EntityManager em, Entity edge, double from, double to, uint mask, out ExitBand band)
        {
            band = default;
            if (!em.Exists(edge) || !em.HasComponent<Curve>(edge) || !(to > from)) return false;
            Bezier4x3 c = em.GetComponentData<Curve>(edge).m_Bezier;
            float t0 = BoxFacts.TAt(c, (float)from), t1 = BoxFacts.TAt(c, (float)to);
            float3 p0 = MathUtils.Position(c, t0), p1 = MathUtils.Position(c, t1);
            band = new ExitBand(SortedKeys.KeyOf(edge.Index, edge.Version), (t0 + t1) * 0.5f, mask, p0.x, p0.z, p1.x, p1.z);
            return true;
        }

        /// <summary>
        /// Everything one source's window depends on that is cheap to read (review F3 / F4: per source, no global version):
        /// its road edge (index, version), the exit point and the building position (median side), the edge's composition
        /// pieces and geometry ends (mouths), and the sub-lane counts of the edge and its two end nodes (stop / give-way
        /// lines are the edge's secondary lanes, crosswalks the nodes' lanes: BoxShapes.StopLines / MarkedCrossings).
        /// </summary>
        private static string Fingerprint(EntityManager em, Source s)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            Entity e = s.Access.Edge;
            sb.Append(e.Index).Append('.').Append(e.Version).Append('@').Append(s.Access.T.ToString("R", ci))
              .Append('/').Append(s.BuildingPos.X.ToString("0.###", ci)).Append(',').Append(s.BuildingPos.Z.ToString("0.###", ci));
            if (!em.Exists(e)) return sb.Append("|gone").ToString();
            if (em.HasComponent<Composition>(e))
            {
                Composition comp = em.GetComponentData<Composition>(e);
                sb.Append('c').Append(comp.m_Edge.Index).Append('.').Append(comp.m_StartNode.Index).Append('.').Append(comp.m_EndNode.Index);
            }
            if (em.HasComponent<EdgeGeometry>(e))
            {
                EdgeGeometry g = em.GetComponentData<EdgeGeometry>(e);
                sb.Append('g').Append(g.m_Start.m_Left.a.x.ToString("0.##", ci)).Append(',').Append(g.m_Start.m_Left.a.z.ToString("0.##", ci))
                  .Append(',').Append(g.m_End.m_Right.d.x.ToString("0.##", ci)).Append(',').Append(g.m_End.m_Right.d.z.ToString("0.##", ci));
            }
            sb.Append('l').Append(SubLanes(em, e));
            if (em.HasComponent<Edge>(e))
            {
                Edge ed = em.GetComponentData<Edge>(e);
                sb.Append(',').Append(SubLanes(em, ed.m_Start)).Append(',').Append(SubLanes(em, ed.m_End));
            }
            return sb.ToString();
        }

        private static int SubLanes(EntityManager em, Entity e) =>
            e != Entity.Null && em.Exists(e) && em.HasBuffer<Game.Net.SubLane>(e) ? em.GetBuffer<Game.Net.SubLane>(e, true).Length : -1;
    }
}
