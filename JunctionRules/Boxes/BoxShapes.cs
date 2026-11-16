using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Rendering;
using JunctionRules.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace JunctionRules.Boxes
{
    /// <summary>
    /// 0.4.2-probe (JR-T-007b): the box OUTLINE of a junction node or a building entrance, read from the game (read-only)
    /// and solved by the game-independent Core (CarriagewayOutline.BuildStraight / EntranceBox.BuildStraight, both on
    /// StraightBox.Solve). One shape for the paint (Paint/YellowBoxPaintSystem.cs) and the tool's hover outline
    /// (YellowBoxToolSystem.cs). Moved here from the paint system (FootwayWidths, EdgeLeftEnd, MarkedCrossings, Sample);
    /// new: inner node curves, yield / signal flags, conflict paths, stop / give-way lines. Never writes anything.
    /// </summary>
    internal static class BoxShapes
    {
        internal sealed class Shape
        {
            public List<V3> Outline;
            public string Failure;
            public List<string> Notes = new List<string>();
            public int Arms;
            public bool Roundabout;
            /// <summary>The edges (and nodes) the shape was read from: a change to any of them needs a rebuild.</summary>
            public List<Entity> Sources = new List<Entity>();
            public V3 Position;
            /// <summary>Entrance only: window along the edge (metres) and the edge's own part.</summary>
            public double From, To;
        }

        // ------------------------------------------------------------------ junction

        /// <summary>
        /// The junction box of a node: arms from its connected road edges (node slice outer + inner curves, footway
        /// insets), markings (zebras on the node, stop / give-way lines on the arms), the yield flags of the node's car
        /// lanes per arm, signals, and the car-path conflict points; then CarriagewayOutline.BuildStraight.
        /// </summary>
        internal static Shape Junction(EntityManager em, Entity node, double kerbInset, double margin)
        {
            var s = new Shape();
            if (!em.Exists(node) || em.HasComponent<Deleted>(node) || !em.HasComponent<Game.Net.Node>(node) || !em.HasBuffer<ConnectedEdge>(node)) { s.Failure = "node gone"; return s; }
            float3 np = em.GetComponentData<Game.Net.Node>(node).m_Position;
            s.Position = new V3(np.x, np.y, np.z);
            s.Sources.Add(node);
            var arms = new List<ArmInput>();
            var armEdges = new List<Entity>();
            var kerbNotes = new List<string>();
            DynamicBuffer<ConnectedEdge> connected = em.GetBuffer<ConnectedEdge>(node, true);
            var edges = new List<Entity>(connected.Length);
            for (int i = 0; i < connected.Length; i++) edges.Add(connected[i].m_Edge);
            foreach (Entity e in edges)
            {
                if (!em.Exists(e) || !em.HasComponent<Edge>(e)) continue;
                s.Sources.Add(e);
                if (!em.HasComponent<Game.Net.Road>(e)) { AddOnce(s.Notes, "non-road edge(s) (path) at the node skipped as arms"); continue; }
                Edge edge = em.GetComponentData<Edge>(e);
                bool isStart = edge.m_Start == node, isEnd = edge.m_End == node;
                if (!isStart && !isEnd) continue;
                EdgeNodeGeometry g;
                if (isStart) { if (!em.HasComponent<StartNodeGeometry>(e)) continue; g = em.GetComponentData<StartNodeGeometry>(e).m_Geometry; }
                else { if (!em.HasComponent<EndNodeGeometry>(e)) continue; g = em.GetComponentData<EndNodeGeometry>(e).m_Geometry; }
                // Outer curves m_Left.m_Left / m_Right.m_Right; inner curves m_Left.m_Right / m_Right.m_Left (the halves meet
                // at the middle). Index 0 = the .a end = the mouth.
                var arm = new ArmInput { OuterA = Sample(g.m_Left.m_Left), OuterB = Sample(g.m_Right.m_Right) };
                if (g.m_MiddleRadius > 0f) s.Roundabout = true;
                else
                {
                    arm.InnerA = Sample(g.m_Left.m_Right);
                    arm.InnerB = Sample(g.m_Right.m_Left);
                    // each half's width at the mouth, measured (no middle-offset sign convention needed)
                    arm.HalfA = V2.Distance(arm.OuterA[0].XZ, arm.InnerA[0].XZ);
                    arm.HalfB = V2.Distance(arm.OuterB[0].XZ, arm.InnerB[0].XZ);
                }
                if (FootwayWidths(em, e, isStart, out double footLeft, out double footRight, out string src, s.Notes))
                {
                    V2 edgeLeft = EdgeLeftEnd(em, e, isStart, out bool haveEdge);
                    if (haveEdge) CarriagewayOutline.AssignSides(arm.OuterA[0].XZ, arm.OuterB[0].XZ, edgeLeft, footLeft, footRight, out arm.InsetA, out arm.InsetB);
                    else
                    {
                        arm.InsetA = isStart ? footLeft : footRight; arm.InsetB = isStart ? footRight : footLeft;
                        AddOnce(s.Notes, "no edge geometry: footway sides assumed");
                    }
                    kerbNotes.Add($"{arm.InsetA:0.00}|{arm.InsetB:0.00}{src}");
                }
                else kerbNotes.Add("unknown");
                arm.InsetA += kerbInset; arm.InsetB += kerbInset;
                arms.Add(arm);
                armEdges.Add(e);
            }
            s.Arms = arms.Count;
            if (arms.Count < 2) { s.Failure = $"{arms.Count} arm(s)"; return s; }
            var markings = MarkedCrossings(em, node, out int unmarked);
            int zebras = markings.Count, stopLines = 0;
            foreach (Entity e in armEdges) stopLines += StopLines(em, e, markings);
            ReadLanes(em, node, arms, out bool signals, out List<LanePath> paths);
            var conflicts = StraightBox.ConflictPoints(paths);
            var centre = new V2(np.x, np.z);
            CarriagewayResult cw = CarriagewayOutline.BuildStraight(arms, centre, markings, margin, conflicts, signals);
            s.Notes.Add("footways (m) per arm " + string.Join(", ", kerbNotes) + (kerbInset != 0 ? $" +{kerbInset} m kerbInset" : ""));
            var stops = new List<string>();
            if (cw.StopDistance != null) foreach (double d in cw.StopDistance) stops.Add(d.ToString("0.0"));
            s.Notes.Add($"{zebras} zebra(s), {stopLines} stop / give-way line(s), {unmarked} unmarked crossing(s); margin {margin:0.0} m; box starts per arm at +[{string.Join(", ", stops)}] m from the mouth");
            var yields = new List<string>();
            foreach (var a in arms) yields.Add(a.Yielding ? "yield" : "priority");
            s.Notes.Add($"arms {string.Join(", ", yields)}{(signals ? ", signals" : "")}; {conflicts.Count} conflict point(s)");
            if (cw.Straight != null && cw.Straight.Shift != null)
            {
                var sh = new List<string>();
                foreach (double d in cw.Straight.Shift) sh.Add(d.ToString("0.00"));
                s.Notes.Add($"shape rule {cw.Straight.RuleText}; mouth shift per arm [{string.Join(", ", sh)}] m");
            }
            s.Notes.AddRange(cw.Notes);
            if (s.Roundabout) s.Notes.Add("roundabout node: kerbs from the outer curves offset sideways; island not cut out");
            s.Outline = cw.Outline;
            s.Failure = cw.Failure;
            return s;
        }

        /// <summary>
        /// Per arm: STOP / Give Way (CarLaneFlags.Stop 0x800 / Yield 0x400, set on the node's car lanes by
        /// LaneSystem.cs 7108-7118 from the arm's priority, incl. the Traffic mod's signs) on any car lane that starts
        /// at that arm's mouth; signals (CarLaneFlags.TrafficLights, LaneSystem.cs 7122-7125) make all arms equal. Also
        /// the car paths for the conflict points: the node's car lanes, minus master lanes (Game.Net.MasterLane: the
        /// merged multi-lane stand-in) and unsafe lanes (CarLaneFlags.Unsafe: u-turns / forbidden-looking moves).
        /// </summary>
        private static void ReadLanes(EntityManager em, Entity node, List<ArmInput> arms, out bool signals, out List<LanePath> paths)
        {
            signals = false;
            paths = new List<LanePath>();
            if (!em.HasBuffer<Game.Net.SubLane>(node)) return;
            DynamicBuffer<Game.Net.SubLane> subs = em.GetBuffer<Game.Net.SubLane>(node, true);
            for (int i = 0; i < subs.Length; i++)
            {
                Entity l = subs[i].m_SubLane;
                if (!em.Exists(l) || em.HasComponent<Deleted>(l) || !em.HasComponent<Game.Net.CarLane>(l) || !em.HasComponent<Curve>(l)) continue;
                if (em.HasComponent<MasterLane>(l)) continue;
                CarLaneFlags f = em.GetComponentData<Game.Net.CarLane>(l).m_Flags;
                if ((f & CarLaneFlags.TrafficLights) != 0) signals = true;
                Bezier4x3 bz = em.GetComponentData<Curve>(l).m_Bezier;
                if ((f & (CarLaneFlags.Stop | CarLaneFlags.Yield)) != 0)
                {
                    int best = -1;
                    double bestD = double.MaxValue;
                    var a = new V2(bz.a.x, bz.a.z);
                    for (int k = 0; k < arms.Count; k++)
                    {
                        double d = BoxPaintGeometry.DistanceToSegment(a, arms[k].OuterA[0].XZ, arms[k].OuterB[0].XZ);
                        if (d < bestD) { bestD = d; best = k; }
                    }
                    if (best >= 0 && bestD < CarriagewayOutline.CrossingAssignMax) arms[best].Yielding = true;
                }
                if ((f & CarLaneFlags.Unsafe) != 0 || !em.HasComponent<Lane>(l)) continue;
                Lane lane = em.GetComponentData<Lane>(l);
                var pts = new List<V2>(17);
                for (int k = 0; k <= 16; k++) { float3 p = MathUtils.Position(bz, k / 16f); pts.Add(new V2(p.x, p.z)); }
                paths.Add(new LanePath(pts, lane.m_StartNode.GetHashCode(), lane.m_EndNode.GetHashCode()));
            }
        }

        // ------------------------------------------------------------------ entrance

        /// <summary>
        /// The entrance box: the window along the edge (EntranceLength centred on the access, from the edge part's
        /// nearer mouth corners, margin clear of mouths and markings), then EntranceBox.BuildStraight (road mouths =
        /// priority, the driveway = the yielding arm without a mouth of its own).
        /// </summary>
        internal static Shape Entrance(EntityManager em, BoxFacts.Access a, double kerbInset, double length, double margin)
        {
            var s = new Shape();
            Entity e = a.Edge;
            if (e == Entity.Null || !em.Exists(e) || !em.HasComponent<Edge>(e) || !em.HasComponent<Curve>(e) || !em.HasComponent<Composition>(e)) { s.Failure = "no road at the access"; return s; }
            Edge ed = em.GetComponentData<Edge>(e);
            s.Sources.Add(e); s.Sources.Add(ed.m_Start); s.Sources.Add(ed.m_End);
            s.Position = new V3(a.Point.x, a.Point.y, a.Point.z);
            bool known = FootwayWidths(em, e, true, out double footL, out double footR, out string src, s.Notes);
            Entity compEnt = em.GetComponentData<Composition>(e).m_Edge;
            double half = em.HasComponent<NetCompositionData>(compEnt) ? em.GetComponentData<NetCompositionData>(compEnt).m_Width * 0.5 : 0;
            if (!(half > 0)) { s.Failure = "road has no composition width"; return s; }
            Bezier4x3 curve = em.GetComponentData<Curve>(e).m_Bezier;
            bool compLeftIsGeomLeft = true;
            if (em.HasComponent<EdgeGeometry>(e))
            {
                float3 lp = em.GetComponentData<EdgeGeometry>(e).m_Start.m_Left.a;
                float3 tan = MathUtils.Tangent(curve, 0f);
                compLeftIsGeomLeft = V2.Cross(new V2(tan.x, tan.z), new V2(lp.x - curve.a.x, lp.z - curve.a.z)) >= 0;
            }
            else AddOnce(s.Notes, "no edge geometry: footway sides assumed");
            double gl = (compLeftIsGeomLeft ? footL : footR) + kerbInset, gr = (compLeftIsGeomLeft ? footR : footL) + kerbInset;
            // markings on this stretch: zebras at both end nodes, stop / give-way lines on the edge
            var markings = MarkedCrossings(em, ed.m_Start, out _);
            markings.AddRange(MarkedCrossings(em, ed.m_End, out _));
            StopLines(em, e, markings);
            int nAll = Math.Max(2, (int)Math.Ceiling(a.LengthM) + 1);
            var cl = new List<V2>(nAll);
            var al = new List<double>(nAll);
            for (int i = 0; i < nAll; i++)
            {
                double m = a.LengthM * i / (nAll - 1);
                float3 p = MathUtils.Position(curve, BoxFacts.TAt(curve, (float)m));
                cl.Add(new V2(p.x, p.z)); al.Add(m);
            }
            var spans = new List<AlongSpan>();
            foreach (var m in markings) if (EntranceBox.StripAlong(cl, al, m, half, out AlongSpan sp)) spans.Add(sp);
            if (!EntranceBox.Window(a.AlongM, length, a.MinM, a.MaxM, margin, spans, out double from, out double to, out string why)) { s.Failure = why; return s; }
            s.From = from; s.To = to;
            int n = Math.Max(2, (int)Math.Ceiling(to - from) + 1);
            var centre = new List<V3>(n);
            for (int i = 0; i < n; i++)
            {
                float t = BoxFacts.TAt(curve, (float)(from + (to - from) * i / (n - 1)));
                float3 p = MathUtils.Position(curve, t);
                centre.Add(new V3(p.x, p.y, p.z));
            }
            StraightBoxResult r = EntranceBox.BuildStraight(centre, half, gl, gr, markings, margin);
            s.Notes.Add($"access {a.AlongM:0.0} m along, edge part {a.MinM:0.0}..{a.MaxM:0.0} m, window {from:0.0}..{to:0.0} m (entrance length {length:0.0} m, margin {margin:0.0} m, {spans.Count} marking(s) on the road)");
            s.Notes.Add($"footways {(known ? $"{footL:0.00}|{footR:0.00}{src}" : "unknown")}{(kerbInset != 0 ? $" +{kerbInset} m kerbInset" : "")}");
            if (r.Shift != null) s.Notes.Add($"shape rule {r.RuleText}; road mouth shifts [{r.Shift[0]:0.00}, {r.Shift[1]:0.00}] m");
            s.Notes.AddRange(r.Notes);
            s.Outline = r.Outline;
            s.Failure = r.Failure;
            return s;
        }

        // ------------------------------------------------------------------ shared readers (moved from the paint system, 0.4.0)

        internal static List<V3> Sample(Bezier4x3 c)
        {
            const int n = 16;   // kerb corner curves: 16 chords keep the kerb within a few cm of the curve
            var r = new List<V3>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                float3 p = MathUtils.Position(c, i / (float)n);
                r.Add(new V3(p.x, p.y, p.z));
            }
            return r;
        }

        /// <summary>
        /// Footway width each side of an arm, from the EDGE composition's LANES (CarriagewayOutline.FootwayWidths):
        /// carriageway = min..max of NetCompositionLane.m_Position.x +- NetLaneData(m_Lane).m_Width / 2 over road, bicycle
        /// and parking lanes; footway = the rest of NetCompositionData.m_Width each side. Not pieces: a Small Road
        /// "Sidewalk With Parking Piece 5" holds both the parking lane and the pavement (see CarriagewayOutline header).
        /// Fallback when the edge composition has no lanes: the node composition, rebuilding the lane positions the way
        /// the game does (piece offset + NetPieceLane.m_Position, x mirrored on an inverted section;
        /// NetCompositionHelpers.cs 1424-1443). 'source' is "" for the edge composition, "n" for the fallback. On false a
        /// "footway unknown" note is added.
        /// </summary>
        internal static bool FootwayWidths(EntityManager em, Entity edge, bool isStart, out double left, out double right, out string source, List<string> notes)
        {
            left = right = 0;
            source = "";
            if (!em.HasComponent<Composition>(edge)) { AddOnce(notes, "footway unknown (no composition): box edge at the outer edge"); return false; }
            Composition comp = em.GetComponentData<Composition>(edge);
            string why = "no edge composition";
            if (comp.m_Edge != Entity.Null && em.HasComponent<NetCompositionData>(comp.m_Edge))
            {
                float width = em.GetComponentData<NetCompositionData>(comp.m_Edge).m_Width;
                var spans = new List<CompositionLaneSpan>();
                if (em.HasBuffer<NetCompositionLane>(comp.m_Edge))
                {
                    DynamicBuffer<NetCompositionLane> lanes = em.GetBuffer<NetCompositionLane>(comp.m_Edge, true);
                    for (int k = 0; k < lanes.Length; k++)
                    {
                        NetCompositionLane l = lanes[k];
                        if (!em.HasComponent<NetLaneData>(l.m_Lane)) continue;
                        NetLaneData ld = em.GetComponentData<NetLaneData>(l.m_Lane);
                        spans.Add(new CompositionLaneSpan(l.m_Position.x, ld.m_Width, CarriagewayOutline.IsCarriagewayLane((int)(l.m_Flags | ld.m_Flags))));
                    }
                }
                if (CarriagewayOutline.FootwayWidths(spans, width, out left, out right, out why))
                {
                    // 0.4.0: a stop bay narrows the pavement only at the stop (CarriagewayOutline.StopBayFootway, heuristic)
                    const CompositionFlags.Side stop = CompositionFlags.Side.PrimaryStop | CompositionFlags.Side.SecondaryStop | CompositionFlags.Side.TertiaryStop;
                    CompositionFlags cf = em.GetComponentData<NetCompositionData>(comp.m_Edge).m_Flags;
                    double l0 = left, r0 = right;
                    left = CarriagewayOutline.StopBayFootway(l0, r0, (cf.m_Left & stop) != 0);
                    right = CarriagewayOutline.StopBayFootway(r0, l0, (cf.m_Right & stop) != 0);
                    if (left != l0 || right != r0) { source = "s"; AddOnce(notes, "stop bay on an arm: that side's footway taken as the other side's (heuristic, 's')"); }
                    return true;
                }
                why = "edge composition: " + why;
            }
            Entity c = isStart ? comp.m_StartNode : comp.m_EndNode;
            if (c != Entity.Null && em.HasComponent<NetCompositionData>(c) && em.HasBuffer<NetCompositionPiece>(c))
            {
                float width = em.GetComponentData<NetCompositionData>(c).m_Width;
                DynamicBuffer<NetCompositionPiece> pieces = em.GetBuffer<NetCompositionPiece>(c, true);
                var spans = new List<CompositionLaneSpan>();
                for (int i = 0; i < pieces.Length; i++)
                {
                    NetCompositionPiece p = pieces[i];
                    if (!em.HasBuffer<NetPieceLane>(p.m_Piece)) continue;
                    bool invert = (p.m_SectionFlags & NetSectionFlags.Invert) != 0;
                    DynamicBuffer<NetPieceLane> pl = em.GetBuffer<NetPieceLane>(p.m_Piece, true);
                    for (int k = 0; k < pl.Length; k++)
                    {
                        if (!em.HasComponent<NetLaneData>(pl[k].m_Lane)) continue;
                        NetLaneData ld = em.GetComponentData<NetLaneData>(pl[k].m_Lane);
                        double x = p.m_Offset.x + (invert ? -pl[k].m_Position.x : pl[k].m_Position.x);
                        spans.Add(new CompositionLaneSpan(x, ld.m_Width, CarriagewayOutline.IsCarriagewayLane((int)(ld.m_Flags | pl[k].m_ExtraFlags))));
                    }
                }
                if (CarriagewayOutline.FootwayWidths(spans, width, out left, out right, out string why2))
                {
                    // The start-node composition is mirrored against the edge frame (CompositionSelectSystem.GetNodeFlags
                    // XORs General.Invert and inverts the side flags when isStart; GetCompositionPieces then reverses the
                    // sections), so its x < 0 is the edge's RIGHT. Swap back into the edge frame AssignSides expects.
                    if (isStart) { double t = left; left = right; right = t; }
                    source = "n"; return true;
                }
                why += "; node composition: " + why2;
            }
            AddOnce(notes, "footway unknown (" + why + "): box edge at the outer edge");
            return false;
        }

        /// <summary>The edge's left kerb end at this node (EdgeGeometry m_Start.m_Left.a at the start node, m_End.m_Left.d at the end).</summary>
        internal static V2 EdgeLeftEnd(EntityManager em, Entity edge, bool isStart, out bool ok)
        {
            ok = em.HasComponent<EdgeGeometry>(edge);
            if (!ok) return default;
            EdgeGeometry eg = em.GetComponentData<EdgeGeometry>(edge);
            float3 p = isStart ? eg.m_Start.m_Left.a : eg.m_End.m_Left.d;
            return new V2(p.x, p.z);
        }

        /// <summary>
        /// The node's marked crossings: crosswalk sub-lanes (PedestrianLane.Crosswalk) without Unsafe (Unsafe = the node
        /// composition has no Crosswalk flag, no zebra drawn: LaneSystem.cs 8571-8580). Painted width = the lane prefab's
        /// NetLaneData.m_Width plus the larger NodeLane width offset (LaneSystem.cs 8566-8567).
        /// </summary>
        internal static List<CrosswalkStrip> MarkedCrossings(EntityManager em, Entity node, out int unmarked)
        {
            var r = new List<CrosswalkStrip>();
            unmarked = 0;
            if (node == Entity.Null || !em.Exists(node) || !em.HasBuffer<Game.Net.SubLane>(node)) return r;
            DynamicBuffer<Game.Net.SubLane> subs = em.GetBuffer<Game.Net.SubLane>(node, true);
            for (int i = 0; i < subs.Length; i++)
            {
                Entity l = subs[i].m_SubLane;
                if (!em.Exists(l) || em.HasComponent<Deleted>(l) || !em.HasComponent<Game.Net.PedestrianLane>(l) || !em.HasComponent<Curve>(l)) continue;
                PedestrianLaneFlags f = em.GetComponentData<Game.Net.PedestrianLane>(l).m_Flags;
                if ((f & PedestrianLaneFlags.Crosswalk) == 0) continue;
                if ((f & PedestrianLaneFlags.Unsafe) != 0) { unmarked++; continue; }
                double width = 0;
                if (em.HasComponent<PrefabRef>(l))
                {
                    Entity prefab = em.GetComponentData<PrefabRef>(l).m_Prefab;
                    if (em.HasComponent<NetLaneData>(prefab)) width = em.GetComponentData<NetLaneData>(prefab).m_Width;
                }
                if (em.HasComponent<NodeLane>(l))
                {
                    float2 wo = em.GetComponentData<NodeLane>(l).m_WidthOffset;
                    width += Math.Max(0, Math.Max(wo.x, wo.y));
                }
                Bezier4x3 bz = em.GetComponentData<Curve>(l).m_Bezier;
                r.Add(new CrosswalkStrip(new V2(bz.a.x, bz.a.z), new V2(bz.d.x, bz.d.z), width));
            }
            return r;
        }

        /// <summary>
        /// 0.4.2: the edge's painted stop / give-way lines, added to 'into'; returns how many. They are the edge's
        /// secondary "crossing" lanes (Game.Net.SecondaryLane; SecondaryLaneSystem.cs 453-586: a lane with
        /// SecondaryNetLaneFlags.Crossing laid across the end of each car lane, RequireStop 0x2000 / RequireYield 0x20000
        /// decide which; optional ones are not created at all, line 837). Kept: drawn (CullingInfo present: hidden lanes
        /// get no CullingInfo / MeshBatch, m_HideLaneTypes, line 1915), not parking-bay markings
        /// (SecondaryLaneDataFlags.FitToParkingSpaces 0x10), and running ACROSS the road (|cos| &lt; 0.5 to the edge
        /// curve there; side markings run along it). Painted width (thickness along the road) = the larger of the lane
        /// prefab's NetLaneData.m_Width and its mesh size NetLaneGeometryData.m_Size.x (mesh bounds across the lane,
        /// NetInitializeSystem.cs 1817-1839): the larger is the conservative value.
        /// </summary>
        internal static int StopLines(EntityManager em, Entity edge, List<CrosswalkStrip> into)
        {
            if (!em.HasBuffer<Game.Net.SubLane>(edge) || !em.HasComponent<Curve>(edge)) return 0;
            Bezier4x3 ec = em.GetComponentData<Curve>(edge).m_Bezier;
            DynamicBuffer<Game.Net.SubLane> subs = em.GetBuffer<Game.Net.SubLane>(edge, true);
            int added = 0;
            for (int i = 0; i < subs.Length; i++)
            {
                Entity l = subs[i].m_SubLane;
                if (!em.Exists(l) || em.HasComponent<Deleted>(l) || !em.HasComponent<Game.Net.SecondaryLane>(l) || !em.HasComponent<Curve>(l)) continue;
                if (!em.HasComponent<CullingInfo>(l) || !em.HasComponent<PrefabRef>(l)) continue;
                Entity prefab = em.GetComponentData<PrefabRef>(l).m_Prefab;
                if (em.HasComponent<SecondaryLaneData>(prefab) && (em.GetComponentData<SecondaryLaneData>(prefab).m_Flags & SecondaryLaneDataFlags.FitToParkingSpaces) != 0) continue;
                Bezier4x3 bz = em.GetComponentData<Curve>(l).m_Bezier;
                var a = new V2(bz.a.x, bz.a.z);
                var d = new V2(bz.d.x, bz.d.z);
                V2 dir = (d - a).Normalized;
                if (dir.Length < 0.5) continue;
                MathUtils.Distance(ec.xz, ((bz.a + bz.d) * 0.5f).xz, out float t);
                float3 tan = MathUtils.Tangent(ec, t);
                V2 road = new V2(tan.x, tan.z).Normalized;
                if (Math.Abs(V2.Dot(dir, road)) >= 0.5) continue;
                double width = 0;
                if (em.HasComponent<NetLaneData>(prefab)) width = em.GetComponentData<NetLaneData>(prefab).m_Width;
                if (em.HasComponent<NetLaneGeometryData>(prefab)) width = Math.Max(width, em.GetComponentData<NetLaneGeometryData>(prefab).m_Size.x);
                into.Add(new CrosswalkStrip(a, d, width, true));
                added++;
            }
            return added;
        }

        internal static void AddOnce(List<string> notes, string s) { if (notes != null && !notes.Contains(s)) notes.Add(s); }
    }
}
