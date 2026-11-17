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
        /// <summary>One boxed exit or derived entrance (Boxes/BoxFacts.BoxedExitBands).</summary>
        internal struct Source
        {
            public Entity Building;
            public BoxFacts.Exit Exit;
            public BoxFacts.Access Access;
            public V2 BuildingPos;
            public bool Derived;
        }

        private static string s_Signature;
        private static List<ExitBand> s_Bands = new List<ExitBand>();
        private static int s_NotPainted, s_Joint;

        /// <summary>
        /// The bands for these sources (one per drawn box; a joint box once). 'notPainted' = sources the paint would not draw
        /// (not driven); 'joint' = joint boxes (two or more sources built as one).
        /// </summary>
        internal static List<ExitBand> Build(EntityManager em, List<Source> sources, out int notPainted, out int joint)
        {
            BoxShapeParams p = BoxStore.Params;
            string sig = Signature(em, sources, p);
            if (sig == s_Signature)
            {
                notPainted = s_NotPainted;
                joint = s_Joint;
                return new List<ExitBand>(s_Bands);
            }
            var bands = new List<ExitBand>();
            notPainted = 0;
            joint = 0;
            var ok = new List<int>();
            var windows = new List<ExitWindow>();
            var masks = new List<uint>();
            for (int i = 0; i < sources.Count; i++)
            {
                Source src = sources[i];
                BoxShapes.Shape shape = BoxShapes.Entrance(em, src.Access, p.KerbInset, p.EntranceLength, p.MarkingMargin, src.BuildingPos);
                if (shape.Outline == null) { notPainted++; continue; }
                Entity e = src.Access.Edge;
                ok.Add(i);
                windows.Add(new ExitWindow(SortedKeys.KeyOf(e.Index, e.Version), shape.Carriageways, shape.From, shape.To));
                masks.Add(shape.Carriageways);
            }
            var done = new bool[ok.Count];
            foreach (var chain in ExitBoxWindows.Chains(windows, ExitBands.MergeGapMetres))
            {
                Source first = sources[ok[chain.Members[0]]];
                BoxShapes.Shape shape = BoxShapes.Entrance(em, first.Access, p.KerbInset, p.EntranceLength, p.MarkingMargin, first.BuildingPos, chain.From, chain.To);
                if (shape.Outline == null) continue;   // the paint keeps them apart (logged by the paint): each its own band below
                foreach (int m in chain.Members) done[m] = true;
                if (Window(em, first.Access.Edge, chain.From, chain.To, masks[chain.Members[0]], out ExitBand b)) { bands.Add(b); joint++; }
            }
            for (int k = 0; k < ok.Count; k++)
            {
                if (done[k]) continue;
                if (Window(em, sources[ok[k]].Access.Edge, windows[k].From, windows[k].To, masks[k], out ExitBand b)) bands.Add(b);
            }
            s_Signature = sig;
            s_Bands = new List<ExitBand>(bands);
            s_NotPainted = notPainted;
            s_Joint = joint;
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
        /// Everything the windows depend on that is cheap to read: the shape parameters, the paint's merged-box version
        /// (bumped whenever the paint regroups: a box or a tracked road / node changed), and per source its road edge, the
        /// edge's composition pieces (a crosswalk / marking change on the edge or its ends), its geometry ends (mouths), the
        /// exit point and the building position.
        /// </summary>
        private static string Signature(EntityManager em, List<Source> sources, BoxShapeParams p)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append(p.Describe()).Append('|').Append(MergedBoxes.Version).Append('|');
            foreach (Source s in sources)
            {
                Entity e = s.Access.Edge;
                sb.Append(s.Building.Index).Append(':').Append(e.Index).Append('.').Append(e.Version).Append('@').Append(s.Access.T.ToString("R", ci))
                  .Append('/').Append(s.BuildingPos.X.ToString("0.###", ci)).Append(',').Append(s.BuildingPos.Z.ToString("0.###", ci));
                if (em.Exists(e) && em.HasComponent<Composition>(e))
                {
                    Composition comp = em.GetComponentData<Composition>(e);
                    sb.Append('c').Append(comp.m_Edge.Index).Append('.').Append(comp.m_StartNode.Index).Append('.').Append(comp.m_EndNode.Index);
                }
                if (em.Exists(e) && em.HasComponent<EdgeGeometry>(e))
                {
                    EdgeGeometry g = em.GetComponentData<EdgeGeometry>(e);
                    sb.Append('g').Append(g.m_Start.m_Left.a.x.ToString("0.##", ci)).Append(',').Append(g.m_Start.m_Left.a.z.ToString("0.##", ci))
                      .Append(',').Append(g.m_End.m_Right.d.x.ToString("0.##", ci)).Append(',').Append(g.m_End.m_Right.d.z.ToString("0.##", ci));
                }
                sb.Append(';');
            }
            return sb.ToString();
        }
    }
}
