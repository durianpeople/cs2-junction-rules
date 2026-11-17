using System;
using System.Collections.Generic;

namespace JunctionRules.Core
{
    // 0.3.5-probe (JR-T-003e): the yellow box outline on the CARRIAGEWAY only (kerb to kerb, from each arm's stop line or
    // the far side of its zebra), built from the game's own geometry. Game-independent; the paint system
    // (Paint/YellowBoxPaintSystem.cs) reads the game data and hands it here. Unit-tested in BoxPaintTests (BoxCarriageway*).
    //
    // Where each input comes from in the game (decompile ~/Code/decomp/yb-2026-11-13/full):
    // - Outer edges of the arm's node slice: EdgeNodeGeometry m_Left.m_Left / m_Right.m_Right of the edge's
    //   Start/EndNodeGeometry; the game itself treats those two curves as the slice's full width (LaneSystem.cs 7918-7938:
    //   left.m_Left = m_Left.m_Left, left.m_Right = m_Right.m_Right; a lateral composition position x lies at
    //   t = x / width + 0.5 between them, lines 7945-7947).
    // - Footway widths: the EDGE composition (Composition.m_Edge) LANES, not its pieces. The carriageway runs from the
    //   outer edge of the outermost road / bicycle / parking lane on one side to the other: NetCompositionLane.m_Position.x
    //   (= piece offset + the piece lane's position, NetCompositionHelpers.cs 1443) +- NetLaneData(m_Lane).m_Width / 2.
    //   Pieces are no good for this: on Small Road the "Sidewalk With Parking Piece 5" (x 3..8) holds both the parking
    //   lane (x 4, width 2) and the pavement, and its Bay variant even carries NetPieceFlags.HasRoadLanes, so a piece
    //   test calls the whole 8 m half carriageway (0.3.5-probe review). The visible kerb is at the parking lane's outer
    //   edge (asphalt to x 5, pavement 5..8: footway 3 m each side, as the bridge's laneMin/laneMax and clearWidth say).
    //   Why 0.3.0-0.3.4 always logged "no road-lane pieces: kerb inset 0": they read the NODE composition, and
    //   HasRoadLanes is set only in NetCompositionHelpers.AddCompositionLanes (NetCompositionHelpers.cs 1464-1465),
    //   called only for composition chunks that hold a NetCompositionLane buffer (NetCompositionSystem.cs 163-181); only
    //   EDGE compositions have that buffer (NetGeometryPrefab.cs 106-112), so a node composition never has the flag.
    // - Which outer curve is the edge's left: composition x &lt; 0 is the edge's left (LaneSystem.cs 3648,
    //   AttachPositionSystem.cs 299: Lerp(segment.m_Left, segment.m_Right, x / width + 0.5)); the node slice outer curve
    //   whose mouth point touches EdgeGeometry's left kerb end at that node takes the left footway.
    // - Zebras: the node's crosswalk sub-lanes (PedestrianLane.Crosswalk without Unsafe; Unsafe = no marking, the node
    //   composition has no Crosswalk flag, LaneSystem.cs 8571-8580). Each is a straight line across its arm
    //   (LaneSystem.cs 8239-8242: mouth line + tangent * crosswalk z, Curve = StraightCurve, line 8582) with the lane
    //   prefab's width (NetLaneData.m_Width, plus NodeLane.m_WidthOffset at an end, lines 8566-8567).

    /// <summary>One lane across an edge composition (composition metres: x = 0 at the centre, -width/2 = the edge's left).</summary>
    public struct CompositionLaneSpan
    {
        public double Position, Width;
        public bool Carriageway;
        public CompositionLaneSpan(double position, double width, bool carriageway) { Position = position; Width = width; Carriageway = carriageway; }
    }

    /// <summary>
    /// A painted marking across the road at a junction: a zebra (node crosswalk sub-lane) or, 0.4.2, a stop / give-way
    /// line (edge secondary crossing lane). Its centre line (A to B, across the arm) and its PAINTED width along the road
    /// (the junction-side painted edge is the centre line + Width / 2).
    /// </summary>
    public struct CrosswalkStrip
    {
        public V2 A, B;
        public double Width;
        /// <summary>0.4.2: true for a stop / give-way line, false for a zebra (notes only).</summary>
        public bool StopLine;
        /// <summary>
        /// 0.4.12 (JR-T-018, G26): true for a median nose / island strip (BoxShapes.MedianNoses): the box keeps the same
        /// margin clear of it as of a zebra / stop / give-way line and its mouth is held clear of it (never on a median).
        /// </summary>
        public bool Median;
        public CrosswalkStrip(V2 a, V2 b, double width) { A = a; B = b; Width = width; StopLine = false; Median = false; }
        public CrosswalkStrip(V2 a, V2 b, double width, bool stopLine) { A = a; B = b; Width = width; StopLine = stopLine; Median = false; }
        public CrosswalkStrip(V2 a, V2 b, double width, bool stopLine, bool median) { A = a; B = b; Width = width; StopLine = stopLine; Median = median; }
    }

    /// <summary>
    /// 0.4.2-probe (JR-T-007b, owner 16 Nov 2026): how far every box edge keeps clear of painted road markings (zebra
    /// stripes, stop / give-way lines) and, for entrance boxes, of the junction mouth. Measured from the marking's
    /// painted edge on the box side (centre line +- painted width / 2), never from its centre line. Options slider; the
    /// paint list's "markingMargin" key overrides it when present (tuning).
    /// </summary>
    public static class MarkingMargin
    {
        public const float Default = 0.5f, Min = 0f, Max = 2f, Step = 0.1f;

        /// <summary>The margin in force: the list file's value when it has one (not NaN), else the Options value; clamped to [Min, Max].</summary>
        public static double Effective(double setting, double listOverride, out string source)
        {
            double v = setting;
            source = "options";
            if (!double.IsNaN(listOverride)) { v = listOverride; source = "paint-test.json markingMargin"; }
            if (double.IsNaN(v) || double.IsInfinity(v)) { v = Default; source += " (invalid, default used)"; }
            return Math.Max(Min, Math.Min(Max, v));
        }
    }

    /// <summary>One arm as the game gives it: the two outer edges of its node slice (index 0 = the mouth, the .a end) and the inset (footway + extra) for each.</summary>
    public sealed class ArmInput
    {
        public List<V3> OuterA = new List<V3>();
        public List<V3> OuterB = new List<V3>();
        public double InsetA, InsetB;
        /// <summary>
        /// 0.4.2: the node slice's INNER curves (EdgeNodeGeometry m_Left.m_Right for side A = m_Left.m_Left, m_Right.m_Left
        /// for side B = m_Right.m_Right), sampled at the same curve parameters as the outer ones, and each half's width
        /// (node composition width / 2 +- middle offset). When given, the kerb is Lerp(outer, inner, inset / half): the
        /// game lays a lateral position across a node half linearly between those two curves (the slice halves of
        /// LaneSystem.cs 7918-7947). Null = the old sideways offset of the outer curve (roundabouts, tests).
        /// </summary>
        public List<V3> InnerA, InnerB;
        public double HalfA, HalfB;
        /// <summary>0.4.2: STOP / Give Way on this approach (CarLaneFlags.Stop / Yield on the node's car lanes from this arm).</summary>
        public bool Yielding;
        /// <summary>
        /// 0.4.8 (JR-T-014b, G5): the game's own corner kerb of side A / B (KerbRemap), or null (tests, roundabouts). Used
        /// only for a corner whose two halves have different footways when the 0.4.7 "one kerb" shape is refused
        /// (CarriagewayOutline.BuildStraight).
        /// </summary>
        public KerbRemap RemapA, RemapB;
    }

    /// <summary>
    /// 0.4.8 (JR-T-014b, gap G5): one node-slice half as the game lays a lateral position across it, WITH the vertex sync
    /// that bends a kerb from its own arm's footway at the mouth to the point it shares with the neighbouring arm.
    /// Game code (decompile ~/Code/decomp/yb-2026-11-13/full): SecondaryObjectSystem.LerpRemap / Remap (lines 1213-1248)
    /// places a lateral position t of a node half between the half's m_Left and m_Right curves with control points
    /// a, b at t, c at a width-weighted blend and d at Remap(t, offsets, targets); offsets = the NODE composition's
    /// NetCompositionData.m_SyncVertexOffsetsLeft / Right, targets = EdgeNodeGeometry.m_SyncVertexTargetsLeft / Right
    /// (GeometrySystem.CalculateVertexSyncTarget, lines 2069-2086). Two neighbouring arms' kerb vertices get the same
    /// end point that way, so the kerb of a corner between a 5 m and a 1 m footway runs from 5 m in at one mouth to
    /// 1 m in at the other, meeting in between (Crescent St x Eastgate Ln, live 17 Nov: both halves end at
    /// (-520.107, 541.145), t 0.4665 / 0.5335 of the shared slice end). The constant-fraction KerbLine cannot do that.
    /// </summary>
    public sealed class KerbRemap
    {
        /// <summary>Control points a..d (a = the mouth) of the slice's OUTER edge curve of this side and of the INNER (middle) curve.</summary>
        public V3[] Outer, Inner;
        /// <summary>Game sync data of this half (4 values each, game order).</summary>
        public double[] Offsets, Targets;
        /// <summary>
        /// The half's width in the node composition (NetCompositionData.m_Width / 2 + m_MiddleOffset for side A, - for side
        /// B): the game's lateral fraction is footway / this. 0 = use the half width measured at the mouth (KerbLine's
        /// 'half'); a skewed mouth is wider than the composition (Eastgate Lane: 4.18 m measured, 4 m composition).
        /// </summary>
        public double Half;
        /// <summary>True for side A (the game's m_Left half: m_Left = outer curve, t from the outer edge); false for side B (m_Right half: m_Left = the middle curve, t from the middle).</summary>
        public bool OuterIsLeft;

        public bool IsValid =>
            Outer != null && Inner != null && Outer.Length == 4 && Inner.Length == 4 && Offsets != null && Targets != null && Offsets.Length == 4 && Targets.Length == 4
            && Array.TrueForAll(Outer, p => !double.IsNaN(p.X) && !double.IsInfinity(p.X) && !double.IsNaN(p.Z) && !double.IsInfinity(p.Z))
            && Array.TrueForAll(Inner, p => !double.IsNaN(p.X) && !double.IsInfinity(p.X) && !double.IsNaN(p.Z) && !double.IsInfinity(p.Z))
            && Array.TrueForAll(Offsets, v => !double.IsNaN(v) && !double.IsInfinity(v)) && Array.TrueForAll(Targets, v => !double.IsNaN(v) && !double.IsInfinity(v));

        /// <summary>The game's Remap (SecondaryObjectSystem.cs): piecewise linear from the sync offsets to the sync targets.</summary>
        public static double Remap(double t, double[] o, double[] g)
        {
            double Sat(double v) => Math.Max(0, Math.Min(1, v));
            double Lerp(double a, double b, double f) => a + (b - a) * f;
            if (t < o[0]) return g[0] * Sat(o[0] > 0 ? t / o[0] : 0);
            if (t < o[1]) return Lerp(g[0], g[1], Sat((t - o[0]) / (o[1] - o[0])));
            if (t < o[2]) return Lerp(g[1], g[2], Sat((t - o[1]) / (o[2] - o[1])));
            if (t < o[3]) return Lerp(g[2], g[3], Sat((t - o[2]) / (o[3] - o[2])));
            return Lerp(g[3], 1, Sat(o[3] < 1 ? (t - o[3]) / (1 - o[3]) : 0));
        }

        /// <summary>
        /// The kerb of this side with footway 'inset' (half width 'half' at the mouth): the game's LerpRemap curve
        /// sampled at 'chords' chords (index 0 = the mouth, as the paint system's Sample). Null when the data is unusable.
        /// </summary>
        public List<V3> KerbLine(double inset, double half, int chords = 16)
        {
            double h = Half > 1e-6 ? Half : half;
            if (!IsValid || h <= 1e-6 || chords < 1) return null;
            double f = Math.Max(0, Math.Min(1, inset / h));
            V3[] l = OuterIsLeft ? Outer : Inner, r = OuterIsLeft ? Inner : Outer;
            double t = OuterIsLeft ? f : 1 - f;
            double t2 = Remap(t, Offsets, Targets);
            double num = V2.Distance(l[0].XZ, r[0].XZ), num2 = V2.Distance(l[3].XZ, r[3].XZ);
            double den = Math.Max(1e-6, 0.5 * (num + num2));
            var mid = new double[4];
            for (int k = 0; k < 4; k++) mid[k] = 0.5 * (Offsets[k] * num + Targets[k] * num2) / den;
            double t3 = Remap(t, Offsets, mid);
            V3 L(V3 a, V3 b, double s) => new V3(a.X + (b.X - a.X) * s, a.Y + (b.Y - a.Y) * s, a.Z + (b.Z - a.Z) * s);
            V3 c0 = L(l[0], r[0], t), c1 = L(l[1], r[1], t), c2 = L(l[2], r[2], t3), c3 = L(l[3], r[3], t2);
            var pts = new List<V3>(chords + 1);
            for (int i = 0; i <= chords; i++)
            {
                double s = i / (double)chords, u = 1 - s;
                double w0 = u * u * u, w1 = 3 * u * u * s, w2 = 3 * u * s * s, w3 = s * s * s;
                pts.Add(new V3(c0.X * w0 + c1.X * w1 + c2.X * w2 + c3.X * w3, c0.Y * w0 + c1.Y * w1 + c2.Y * w2 + c3.Y * w3, c0.Z * w0 + c1.Z * w1 + c2.Z * w2 + c3.Z * w3));
            }
            return pts;
        }
    }

    public sealed class CarriagewayResult
    {
        public List<V3> Outline;
        public string Failure;
        /// <summary>Per arm (input order): how far the box starts in from the mouth (0 = at the mouth / stop line).</summary>
        public double[] StopDistance;
        /// <summary>Per arm: the trimmed kerb lines that went into the merge.</summary>
        public List<ArmSlice> Kerbs = new List<ArmSlice>();
        public List<string> Notes = new List<string>();
        /// <summary>0.4.2: the straight-sided solve (shifts per arm in INPUT order, rule), when BuildStraight made it.</summary>
        public StraightBoxResult Straight;
        /// <summary>0.4.8 (JR-T-014b, G5): corner kerb pairs drawn as the game draws them (KerbRemap) because the one-kerb shape was refused; 0 = the 0.4.7 shape.</summary>
        public int GameCorners;

        /// <summary>0.4.8 (JR-T-014b): one line naming the solved shape for logs (sides, area, rule, chamfers, game corner kerbs); null without an outline.</summary>
        public string ShapeSummary()
        {
            if (Outline == null) return null;
            string s = $"box shape: {Outline.Count}-sided straight box, {Math.Abs(BoxPaintGeometry.SignedArea(Outline)):0.0} m2";
            if (Straight != null)
            {
                s += $", rule {Straight.RuleCode}";
                if (Straight.Shift != null)
                {
                    var sh = new List<string>();
                    foreach (double d in Straight.Shift) sh.Add(d.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                    s += $", mouth shifts [{string.Join(", ", sh)}] m";
                }
                if (Straight.Chamfers.Count > 0) s += $", {Straight.Chamfers.Count} chamfered corner(s)";
                if (Straight.SkippedConflicts > 0) s += $", WARNING {Straight.SkippedConflicts} conflict point(s) left outside (under a marking margin past the kerb lines)";
            }
            if (GameCorners > 0) s += $", {GameCorners} corner kerb(s) as the game draws them";
            return s;
        }
    }

    public static class CarriagewayOutline
    {
        /// <summary>Most a crossing's middle may lie from an arm's mouth line and still belong to it (metres).</summary>
        public const double CrossingAssignMax = 12.0;

        // Game LaneFlags values (decompile Game.Prefabs/LaneFlags.cs): Road 0x8, Pedestrian 0x10, Parking 0x20.
        // Bicycle lanes are Road | BicyclesOnly, so Road covers them.
        public const int LaneFlagRoad = 0x8, LaneFlagPedestrian = 0x10, LaneFlagParking = 0x20;

        /// <summary>A lane on the asphalt between the kerbs: road (incl. bicycle) or parking, never a pavement lane.</summary>
        public static bool IsCarriagewayLane(int laneFlags)
        {
            if ((laneFlags & LaneFlagPedestrian) != 0) return false;
            return (laneFlags & (LaneFlagRoad | LaneFlagParking)) != 0;
        }

        /// <summary>
        /// Footway width each side from an edge composition's lanes: the carriageway runs from the outer edge of the
        /// leftmost carriageway lane (position - width/2) to the outer edge of the rightmost one; what is left of the full
        /// width each side is footway (verge, kerb and pavement), clamped to >= 0. False (with 'why') when there is no
        /// carriageway lane or the span is odd: the caller must then log "footway unknown", never a silent 0.
        /// </summary>
        public static bool FootwayWidths(IList<CompositionLaneSpan> lanes, double width, out double left, out double right, out string why)
        {
            left = right = 0;
            why = null;
            if (lanes == null || lanes.Count == 0) { why = "no composition lanes"; return false; }
            if (!(width > 0) || double.IsInfinity(width)) { why = "composition width " + width; return false; }
            double cmin = double.MaxValue, cmax = double.MinValue;
            foreach (var l in lanes)
            {
                if (!l.Carriageway) continue;
                if (!IsFinite(l.Position) || !IsFinite(l.Width) || l.Width < 0) continue;
                cmin = Math.Min(cmin, l.Position - l.Width * 0.5);
                cmax = Math.Max(cmax, l.Position + l.Width * 0.5);
            }
            if (cmin > cmax) { why = "no road, bicycle or parking lanes"; return false; }
            double half = width * 0.5;
            double lw = cmin + half, rw = half - cmax;
            const double tol = 0.01;
            if (lw < -tol || rw < -tol || cmax - cmin < 0.5) { why = $"odd carriageway span {cmin:0.00}..{cmax:0.00} of width {width:0.00}"; return false; }
            left = Math.Max(0, lw);
            right = Math.Max(0, rw);
            return true;
        }

        public static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        public static bool IsFinite(V3 p) => IsFinite(p.X) && IsFinite(p.Y) && IsFinite(p.Z);
        public static bool IsFinite(V2 p) => IsFinite(p.X) && IsFinite(p.Z);

        /// <summary>
        /// Which node-slice outer curve takes the edge's left footway: the one whose mouth point is nearer to the edge's
        /// left kerb end at this node. Side-agnostic (works for start and end nodes alike).
        /// </summary>
        public static void AssignSides(V2 mouthA, V2 mouthB, V2 edgeLeftEnd, double footLeft, double footRight, out double insetA, out double insetB)
        {
            if (V2.Distance(mouthA, edgeLeftEnd) <= V2.Distance(mouthB, edgeLeftEnd)) { insetA = footLeft; insetB = footRight; }
            else { insetA = footRight; insetB = footLeft; }
        }

        /// <summary>Mouth line of an arm: its middle and the unit normal pointing into the junction (toward centre).</summary>
        public static void Mouth(ArmSlice arm, V2 centre, out V2 mid, out V2 inward)
        {
            V2 l0 = arm.LeftKerb[0].XZ, r0 = arm.RightKerb[0].XZ;
            mid = (l0 + r0) * 0.5;
            V2 n = (r0 - l0).Normalized.Left;
            if (n.Length < 0.5)
            {
                n = (centre - mid).Normalized;
            }
            else if (V2.Dot(n, centre - mid) < 0) n = n * -1;
            inward = n;
        }

        /// <summary>
        /// How far into the junction each arm's box starts: the far (junction-side) painted edge of the markings on that
        /// arm (zebra stripes; 0.4.2 also stop / give-way lines), measured square to the mouth line, plus 'margin'
        /// (0.4.2); 0 where no marking reaches within 'margin' of the mouth (the box then starts at the mouth, where the
        /// edge's lanes end). Painted edge = centre line + painted width / 2. A marking goes to the arm whose mouth
        /// segment is nearest its middle (within CrossingAssignMax). 0.3.5-0.4.0 had no margin: the box edge lay flush
        /// on the zebra's junction-side painted edge, and stop lines were not read at all.
        /// </summary>
        public static double[] StopDistances(IList<ArmSlice> arms, V2 centre, IList<CrosswalkStrip> crossings, List<string> notes = null, double margin = 0)
        {
            var d = new double[arms.Count];
            if (crossings == null) return d;
            var mids = new V2[arms.Count];
            var ins = new V2[arms.Count];
            for (int i = 0; i < arms.Count; i++) Mouth(arms[i], centre, out mids[i], out ins[i]);
            int ignored = 0;
            foreach (var c in crossings)
            {
                V2 m = (c.A + c.B) * 0.5;
                int best = -1;
                double bestD = double.MaxValue;
                for (int i = 0; i < arms.Count; i++)
                {
                    double dist = BoxPaintGeometry.DistanceToSegment(m, arms[i].LeftKerb[0].XZ, arms[i].RightKerb[0].XZ);
                    if (dist < bestD) { bestD = dist; best = i; }
                }
                if (best < 0 || bestD > CrossingAssignMax) { ignored++; continue; }
                double half = Math.Max(0, c.Width) * 0.5;
                double far = Math.Max(V2.Dot(c.A - mids[best], ins[best]), V2.Dot(c.B - mids[best], ins[best])) + half + Math.Max(0, margin);
                if (far > d[best]) d[best] = far;
            }
            if (ignored > 0 && notes != null) notes.Add($"{ignored} crossing(s) not next to any arm mouth, ignored");
            return d;
        }

        /// <summary>
        /// Cuts both kerb lines of an arm where they reach 'distance' into the junction (square to the mouth line), so
        /// the arm's mouth moves in to that line. False (arm unchanged) when a kerb never gets that far.
        /// </summary>
        public static bool TrimToStopLine(ArmSlice arm, V2 centre, double distance, out ArmSlice trimmed)
        {
            trimmed = arm;
            if (distance <= 1e-6) return true;
            Mouth(arm, centre, out V2 mid, out V2 n);
            var l = CutAt(arm.LeftKerb, mid, n, distance);
            var r = CutAt(arm.RightKerb, mid, n, distance);
            if (l == null || r == null) return false;
            trimmed = new ArmSlice(l, r);
            return true;
        }

        private static List<V3> CutAt(List<V3> line, V2 mid, V2 n, double d)
        {
            for (int i = 0; i < line.Count; i++)
            {
                double s = V2.Dot(line[i].XZ - mid, n);
                if (s < d - 1e-9) continue;
                var r = new List<V3>(line.Count - i + 1);
                if (i > 0)
                {
                    double s0 = V2.Dot(line[i - 1].XZ - mid, n);
                    double t = (d - s0) / (s - s0);
                    V2 p = line[i - 1].XZ + (line[i].XZ - line[i - 1].XZ) * t;
                    r.Add(V3.FromXZ(p, line[i - 1].Y + (line[i].Y - line[i - 1].Y) * t));
                }
                for (int k = i; k < line.Count; k++) if (r.Count == 0 || V2.Distance(r[r.Count - 1].XZ, line[k].XZ) > 1e-6) r.Add(line[k]);
                return r.Count >= 2 ? r : null;
            }
            return null;
        }

        /// <summary>Most two outer curves' inner ends may lie apart (in plan) and still count as one kerb (metres).</summary>
        public const double JoinTolerance = 0.25;

        /// <summary>
        /// 0.4.0 (JR-T-007, T through-side defect at Moonflower x Anchor): the inset of each outer curve, with the two
        /// halves of one kerb made equal. The game splits every kerb of a node between the two arms either side of it:
        /// each node slice's outer curve runs from its own mouth to a shared middle point, where the neighbour's curve
        /// starts (GeometrySystem.cs 1945-1997: the slice's side segments end at the middle connection / middle segment
        /// fitted between this slice's end and the next one's start, CalculateMiddleConnection / CalculateMiddleSegment,
        /// 2738-2778). On the through side of a T there is no arm between the two halves, so the whole straight kerb
        /// is those two curves end to end. Before, each half took the footway of ITS OWN edge's composition: at
        /// Moonflower x Anchor the west Anchor edge carries a bus stop bay on that side (composition side flag
        /// PrimaryStop; carriageway lanes -5..+6, pedestrian lane at +7, so its footway computes as 2 m) while the
        /// junction's pavement there is the normal 3 m, so the west half of the through kerb was inset 1 m too little
        /// (box over the pavement) with a 1 m step at the middle. Rule: curves of DIFFERENT arms whose inner ends meet
        /// within tol take the larger of their two insets (the pavement of a kerb is never narrower than its wider
        /// half; the larger is the conservative side, the box never reaches onto the pavement). Notes name each change.
        /// </summary>
        public static void ShareJoinedInsets(IList<ArmInput> arms, double tol, out double[] insetA, out double[] insetB, List<string> notes = null)
            => ShareJoinedInsets(arms, tol, false, out insetA, out insetB, out _, out _, out _, notes);

        /// <summary>Most angle (degrees) between two arms' mouth lines, facing opposite ways, for their joined kerb to count as a THROUGH side (a straight kerb) rather than a corner.</summary>
        public const double ThroughSideMaxAngle = 30.0;

        /// <summary>
        /// 0.4.8 (JR-T-014b, gap G5): as ShareJoinedInsets, and with gameCorners a joined pair at a CORNER (the two arms'
        /// mouths not facing opposite ways within ThroughSideMaxAngle) whose halves have different footways and both carry
        /// the game's corner data (ArmInput.RemapA / RemapB) keeps its own insets and is flagged in remapA / remapB: its
        /// kerbs are then drawn as the game draws them (KerbRemap.KerbLine), meeting at the game's shared point. Why: the
        /// "larger inset" of 0.4.0 is a constant lateral FRACTION of each half along its whole curve; with a 5 m footway
        /// on an 8 m half (Crescent Street) against a 1 m one on a 4 m half (Eastgate Lane, an Alley) the larger inset is
        /// more than the alley's whole half (5 / 4, clamped to 1 by KerbLine), so the alley side's "kerb" became the alley's
        /// centre line and no box could fit. gamePairs = how many pairs would be drawn the game's way.
        /// </summary>
        public static void ShareJoinedInsets(IList<ArmInput> arms, double tol, bool gameCorners, out double[] insetA, out double[] insetB, out bool[] remapA, out bool[] remapB, out int gamePairs, List<string> notes = null)
        {
            int n = arms.Count;
            var ra = new bool[n];
            var rb = new bool[n];
            remapA = ra;
            remapB = rb;
            gamePairs = 0;
            var ia = new double[n];
            var ib = new double[n];
            insetA = ia;
            insetB = ib;
            for (int i = 0; i < n; i++) { ia[i] = arms[i].InsetA; ib[i] = arms[i].InsetB; }
            // curve k = arm k/2, side A (even) or B (odd); its inner end = the last point
            var end = new V2[2 * n];
            for (int i = 0; i < n; i++) { end[2 * i] = arms[i].OuterA[arms[i].OuterA.Count - 1].XZ; end[2 * i + 1] = arms[i].OuterB[arms[i].OuterB.Count - 1].XZ; }
            var partner = new int[2 * n];
            for (int k = 0; k < 2 * n; k++)
            {
                partner[k] = -1;
                double best = tol;
                for (int q = 0; q < 2 * n; q++)
                {
                    if (q / 2 == k / 2) continue;   // never the same arm (a dead end's two curves meet at its end cap)
                    double d = V2.Distance(end[k], end[q]);
                    if (d <= best) { best = d; partner[k] = q; }
                }
            }
            double Get(int k) => (k & 1) == 0 ? ia[k / 2] : ib[k / 2];
            void Set(int k, double v) { if ((k & 1) == 0) ia[k / 2] = v; else ib[k / 2] = v; }
            for (int k = 0; k < 2 * n; k++)
            {
                int q = partner[k];
                if (q < k || partner[q] != k) continue;   // each mutual pair once
                double a = Get(k), b = Get(q);
                if (Math.Abs(a - b) <= 1e-9) continue;
                KerbRemap Rm(int c) => (c & 1) == 0 ? arms[c / 2].RemapA : arms[c / 2].RemapB;
                if (Rm(k) != null && Rm(q) != null && Rm(k).IsValid && Rm(q).IsValid && IsCorner(arms[k / 2], arms[q / 2], end[k]))
                {
                    if (gameCorners)
                    {
                        gamePairs++;
                        if ((k & 1) == 0) ra[k / 2] = true; else rb[k / 2] = true;
                        if ((q & 1) == 0) ra[q / 2] = true; else rb[q / 2] = true;
                        notes?.Add($"kerb between arm {k / 2 + 1}{((k & 1) == 0 ? "A" : "B")} and arm {q / 2 + 1}{((q & 1) == 0 ? "A" : "B")}: corner kerb as the game draws it, footway {a:0.00} m at one mouth to {b:0.00} m at the other");
                        continue;
                    }
                    gamePairs++;
                }
                double m = Math.Max(a, b);
                Set(k, m);
                Set(q, m);
                notes?.Add($"kerb between arm {k / 2 + 1}{((k & 1) == 0 ? "A" : "B")} and arm {q / 2 + 1}{((q & 1) == 0 ? "A" : "B")}: one kerb, inset {m:0.00} m (halves were {a:0.00}|{b:0.00})");
            }
        }

        /// <summary>
        /// 0.4.8 (JR-T-014b): true when two arms whose kerb halves meet at 'joint' form a corner, false for a through side
        /// (their mouth lines facing opposite ways within ThroughSideMaxAngle: the straight kerb of a T, where 0.4.0's
        /// one-kerb-one-inset rule stays).
        /// </summary>
        public static bool IsCorner(ArmInput x, ArmInput y, V2 joint)
        {
            V2 Out(ArmInput a)
            {
                V2 p = a.OuterA[0].XZ, q = a.OuterB[0].XZ;
                V2 d = (q - p).Normalized;
                var nrm = new V2(-d.Z, d.X);
                if (V2.Dot(nrm, (p + q) * 0.5 - joint) < 0) nrm = nrm * -1;
                return nrm;
            }
            return V2.Dot(Out(x), Out(y)) > -Math.Cos(ThroughSideMaxAngle * Math.PI / 180);
        }

        /// <summary>
        /// 0.4.0 (JR-T-007): footway on a side whose edge composition carries a stop (bus / tram stop bay: composition side
        /// flag PrimaryStop / SecondaryStop / TertiaryStop, CompositionFlags.cs). The bay pushes the pavement back only
        /// AT the stop, so the composition's footway there (narrower) is not the kerb near the node. Heuristic, flagged
        /// as such in the log: the side takes the larger of its computed footway and the other side's. Without a stop
        /// the computed value is returned unchanged.
        /// </summary>
        public static double StopBayFootway(double computed, double otherSide, bool sideHasStop)
        {
            if (!sideHasStop) return computed;
            return Math.Max(computed, otherSide);
        }

        /// <summary>
        /// The whole carriageway outline: each arm's outer edges moved in by its insets (the kerb lines, following the
        /// corner curves), trimmed to the stop line / far side of the zebra, merged into one ring (BuildOutline).
        /// </summary>
        public static CarriagewayResult Build(IList<ArmInput> arms, V2 centre, IList<CrosswalkStrip> crossings, double straightenTol) => Build(arms, centre, crossings, straightenTol, 0);

        /// <summary>0.4.2: as Build, each arm's box edge 'margin' clear of the junction-side painted edge of its markings (MarkingMargin).</summary>
        public static CarriagewayResult Build(IList<ArmInput> arms, V2 centre, IList<CrosswalkStrip> crossings, double straightenTol, double margin)
        {
            var res = new CarriagewayResult();
            if (arms == null || arms.Count < 2) { res.Failure = "fewer than 2 arms"; return res; }
            if (!IsFinite(centre)) { res.Failure = "non-finite input (NaN/Inf) at the node centre"; return res; }
            if (crossings != null)
            {
                var ok = new List<CrosswalkStrip>(crossings.Count);
                foreach (var c in crossings) if (IsFinite(c.A) && IsFinite(c.B) && IsFinite(c.Width)) ok.Add(c);
                if (ok.Count < crossings.Count) res.Notes.Add($"{crossings.Count - ok.Count} crossing(s) with non-finite geometry ignored");
                crossings = ok;
            }
            foreach (var a in arms)
            {
                if (a.OuterA == null || a.OuterB == null || a.OuterA.Count < 2 || a.OuterB.Count < 2) { res.Failure = "an arm has an edge line with fewer than 2 points"; return res; }
                if (!IsFinite(a.InsetA) || !IsFinite(a.InsetB) || !a.OuterA.TrueForAll(IsFinite) || !a.OuterB.TrueForAll(IsFinite)) { res.Failure = "non-finite input (NaN/Inf) in an arm's edge lines or insets"; return res; }
            }
            // 0.4.0 (JR-T-007): one kerb, one inset. Two neighbouring arms' facing outer curves that meet end to end are
            // the two halves of ONE kerb (the through side of a T, or a corner); they get the same inset.
            ShareJoinedInsets(arms, JoinTolerance, out double[] insetA, out double[] insetB, res.Notes);
            var kerbs = new List<ArmSlice>(arms.Count);
            for (int i = 0; i < arms.Count; i++)
            {
                var a = arms[i];
                var ka = BoxPaintGeometry.OffsetToward(a.OuterA, insetA[i], a.OuterB[0].XZ);
                var kb = BoxPaintGeometry.OffsetToward(a.OuterB, insetB[i], a.OuterA[0].XZ);
                kerbs.Add(new ArmSlice(ka, kb));
            }
            res.StopDistance = StopDistances(kerbs, centre, crossings, res.Notes, margin);
            for (int i = 0; i < kerbs.Count; i++)
            {
                if (res.StopDistance[i] <= 1e-6) continue;
                if (TrimToStopLine(kerbs[i], centre, res.StopDistance[i], out ArmSlice t)) kerbs[i] = t;
                else
                {
                    res.Notes.Add($"arm {i + 1}: marking (+ margin) ends {res.StopDistance[i]:0.0} m in, past the kerb lines; box starts at the mouth");
                    res.StopDistance[i] = 0;
                }
            }
            res.Kerbs = kerbs;
            res.Outline = BoxPaintGeometry.BuildOutline(kerbs, centre, straightenTol, out string why);
            if (res.Outline == null) res.Failure = why;
            return res;
        }
            /// <summary>
        /// 0.4.2: the kerb line of one side: Lerp(outer, inner, inset / half) point by point when the inner curve is given
        /// with the same sample count and a half width &gt; 0; else the outer curve offset sideways by 'inset' toward 'toward'.
        /// The lerp matters at skewed nodes (Lindbergh St x Anchor St): there the outer curves are stubs under 1 m long while
        /// the kerb corner lies well inside the slice, between outer and inner curve, and an offset stub never met its
        /// neighbour (0.4.0 then joined the stub ends with a straight chord over the pavement).
        /// </summary>
        public static List<V3> KerbLine(List<V3> outer, List<V3> inner, double half, double inset, V2 toward)
        {
            if (inner != null && inner.Count == outer.Count && half > 1e-6)
            {
                double f = Math.Max(0, Math.Min(1, inset / half));
                var r = new List<V3>(outer.Count);
                for (int k = 0; k < outer.Count; k++)
                {
                    V3 o = outer[k], q = inner[k];
                    r.Add(new V3(o.X + (q.X - o.X) * f, o.Y + (q.Y - o.Y) * f, o.Z + (q.Z - o.Z) * f));
                }
                return r;
            }
            return BoxPaintGeometry.OffsetToward(outer, inset, toward);
        }

        /// <summary>
        /// 0.4.2-probe (owner rulings 16 Nov 2026): the straight-sided box. Kerb lines (KerbLine) trimmed to each arm's
        /// stop line / zebra + margin; arms sorted round the centre; the kerb chain between neighbouring arms (one arm's
        /// kerb to the corner where it meets the next arm's kerb, then that kerb out to its mouth); then StraightBox.Solve
        /// (mouth sides, straight joining edges, yielding mouths slid forward until the edges clear).
        /// </summary>
        public static CarriagewayResult BuildStraight(IList<ArmInput> arms, V2 centre, IList<CrosswalkStrip> markings, double margin, IList<V2> conflictPoints, bool signals)
        {
            var res = new CarriagewayResult();
            if (arms == null || arms.Count < 2) { res.Failure = "fewer than 2 arms"; return res; }
            if (!IsFinite(centre)) { res.Failure = "non-finite input (NaN/Inf) at the node centre"; return res; }
            var ok = new List<CrosswalkStrip>();
            if (markings != null)
            {
                foreach (var c in markings) if (IsFinite(c.A) && IsFinite(c.B) && IsFinite(c.Width)) ok.Add(c);
                if (ok.Count < markings.Count) res.Notes.Add($"{markings.Count - ok.Count} marking(s) with non-finite geometry ignored");
            }
            foreach (var a in arms)
            {
                if (a.OuterA == null || a.OuterB == null || a.OuterA.Count < 2 || a.OuterB.Count < 2) { res.Failure = "an arm has an edge line with fewer than 2 points"; return res; }
                if (!IsFinite(a.InsetA) || !IsFinite(a.InsetB) || !a.OuterA.TrueForAll(IsFinite) || !a.OuterB.TrueForAll(IsFinite)) { res.Failure = "non-finite input (NaN/Inf) in an arm's edge lines or insets"; return res; }
                if ((a.InnerA != null && !a.InnerA.TrueForAll(IsFinite)) || (a.InnerB != null && !a.InnerB.TrueForAll(IsFinite))) { res.Failure = "non-finite input (NaN/Inf) in an arm's inner curves"; return res; }
            }
            // 0.4.8 (JR-T-014b, G5): first exactly the 0.4.7 shape (every box that had a shape keeps it, cm for cm). Only
            // when that is refused, and a corner between different footways carries the game's corner data or a marking
            // lies past an arm's kerb lines, is it built again the 0.4.8 way: those corners' kerbs as the game draws them
            // (KerbRemap) and such an arm's mouth held clear of its marking (BoxMouth.MinShift) instead of left at the mouth.
            // 0.4.11 (JR-T-017, G24, owner rule 17 Nov, exact list confirmed: the box never covers, with the 0.5 m margin,
            // ZEBRAS, YIELD (give-way) lines and STOP lines; everything else - lane dashes, arrows, centre lines, prefab text -
            // may lie under a box, and those are not inputs here at all: only zebras and stop / give-way lines are,
            // BoxShapes.MarkedCrossings / StopLines): an arm whose zebra or stop / give-way line lies past its kerb lines
            // ALWAYS has its mouth held clear of it (BoxMouth.MinShift), also a priority arm and also when the 0.4.7 shape
            // would build: 0.4.8-0.4.10 held it only when that shape was refused,
            // so a priority arm's mouth stayed at the mouth, ON the zebra (Capper Street node 1205548, arms 1 / 2: "marking
            // (+ margin) ends 2.8 m in, past the kerb lines; box starts at the mouth", shape rule priority-road). The rule
            // "priority arms stay put" yields to "never on a marking": StraightBox.Search gives every arm at least its MinShift.
            // Corners as the game draws them (KerbRemap) are still tried only when the plain corners are refused.
            // Order keeps every box that covers none of them exactly as before: (1) the 0.4.7 shape, taken only when no zebra /
            // stop / give-way line lies past an arm's kerb lines (held == 0); (2) the 0.4.8 way (game corners + held mouths) when there are game corners;
            // (3) plain corners with the held mouths; else no box (never one on a marking).
            CarriagewayResult first = Straight(arms, centre, ok, margin, conflictPoints, signals, false, false, res.Notes, out int gamePairs, out int held, out int zebraHeld);
            if (first.Outline != null && held == 0) return first;
            if (first.Outline == null && gamePairs == 0 && held == 0) return first;
            string why = first.Outline != null ? $"the 0.4.7 shape would cover a zebra / stop / give-way line past the kerb lines on {held} arm(s) (G24: never on one; {zebraHeld} of them zebra arm(s))" : $"the 0.4.7 shape was refused ({first.Failure})";
            CarriagewayResult game = null;
            string holdFailure = null;
            if (gamePairs > 0)
            {
                game = Straight(arms, centre, ok, margin, conflictPoints, signals, true, true, res.Notes, out _, out _, out _);
                if (game.Outline != null)
                {
                    game.GameCorners = gamePairs;
                    game.Notes.Add($"built the 0.4.8 way ({gamePairs} corner kerb(s) as the game draws them, {held} mouth(s) held clear of a marking past the kerb lines); {why}");
                    return game;
                }
            }
            if (held > 0)
            {
                CarriagewayResult hold = Straight(arms, centre, ok, margin, conflictPoints, signals, false, true, res.Notes, out _, out _, out _);
                holdFailure = hold.Failure;
                if (hold.Outline != null)
                {
                    hold.Notes.Add($"built with {held} mouth(s) held clear of a marking past the kerb lines (priority arms too: never on a zebra / stop / give-way line); {why}");
                    return hold;
                }
                if (first.Outline != null)
                {
                    // Never a box on a marking: no box, saying why.
                    var refused = new CarriagewayResult();
                    refused.Notes.AddRange(hold.Notes);
                    refused.StopDistance = hold.StopDistance;
                    refused.Kerbs = hold.Kerbs;
                    refused.Failure = $"no box clear of the zebra(s) / stop / give-way line(s) / median nose(s) past the kerb lines: {hold.Failure}" + (game != null ? $"; game corners: {game.Failure}" : "");
                    return refused;
                }
            }
            first.Notes.Add($"the 0.4.8 way ({gamePairs} corner kerb(s) as the game draws them, {held} mouth(s) held clear of a marking) tried too, also refused: {game?.Failure ?? holdFailure}");
            return first;
        }

        /// <summary>
        /// BuildStraight for valid input. fix048: corners between different footways drawn as the game draws them
        /// (KerbRemap), and an arm whose marking lies past its kerb lines gets its mouth held at least that far in
        /// (BoxMouth.MinShift) instead of left at the mouth, on the marking. gamePairs / held: how many such corners /
        /// arms there are (counted in either mode).
        /// </summary>
        private static CarriagewayResult Straight(IList<ArmInput> arms, V2 centre, List<CrosswalkStrip> ok, double margin, IList<V2> conflictPoints, bool signals, bool gameCornersOn, bool holdMarkings, List<string> preNotes, out int gamePairs, out int held, out int zebraHeld)
        {
            zebraHeld = 0;
            var res = new CarriagewayResult();
            res.Notes.AddRange(preNotes);
            bool gameCorners = gameCornersOn;
            bool fix048 = holdMarkings;
            held = 0;
            ShareJoinedInsets(arms, JoinTolerance, gameCorners, out double[] insetA, out double[] insetB, out bool[] remapA, out bool[] remapB, out gamePairs, res.Notes);
            var kerbs = new List<ArmSlice>(arms.Count);
            for (int i = 0; i < arms.Count; i++)
            {
                var a = arms[i];
                List<V3> ka = remapA[i] ? a.RemapA.KerbLine(insetA[i], a.HalfA) : null, kb = remapB[i] ? a.RemapB.KerbLine(insetB[i], a.HalfB) : null;
                kerbs.Add(new ArmSlice(ka ?? KerbLine(a.OuterA, a.InnerA, a.HalfA, insetA[i], a.OuterB[0].XZ), kb ?? KerbLine(a.OuterB, a.InnerB, a.HalfB, insetB[i], a.OuterA[0].XZ)));
            }
            res.StopDistance = StopDistances(kerbs, centre, ok, res.Notes, margin);
            // 0.4.11 (G24): the same distances from the zebras alone (stop / give-way lines left out), to tell a ZEBRA past the kerb lines.
            var zebrasOnly = ok.FindAll(c => !c.StopLine && !c.Median);
            double[] zebraStop = StopDistances(kerbs, centre, zebrasOnly, null, margin);
            var minShift = new double[kerbs.Count];
            for (int i = 0; i < kerbs.Count; i++)
            {
                if (res.StopDistance[i] <= 1e-6) continue;
                if (TrimToStopLine(kerbs[i], centre, res.StopDistance[i], out ArmSlice t)) kerbs[i] = t;
                else
                {
                    held++;
                    if (zebraStop[i] > 1e-6 && !TrimToStopLine(kerbs[i], centre, zebraStop[i], out _)) zebraHeld++;
                    if (fix048)
                    {
                        res.Notes.Add($"arm {i + 1}: marking (+ margin) ends {res.StopDistance[i]:0.0} m in, past the kerb lines; mouth held at least that far in");
                        minShift[i] = res.StopDistance[i];
                    }
                    else res.Notes.Add($"arm {i + 1}: marking (+ margin) ends {res.StopDistance[i]:0.0} m in, past the kerb lines; box starts at the mouth");
                    res.StopDistance[i] = 0;
                }
            }
            res.Kerbs = kerbs;
            // ring order (as BuildOutline): sort by mouth angle; exit = the kerb whose mouth point is counter-clockwise
            int n = kerbs.Count;
            var order = new List<(double ang, int idx, List<V3> entry, List<V3> exit)>();
            for (int i = 0; i < n; i++)
            {
                var a = kerbs[i];
                V2 l0 = a.LeftKerb[0].XZ - centre, r0 = a.RightKerb[0].XZ - centre;
                V2 m = (l0 + r0) * 0.5;
                bool leftIsExit = V2.Cross(r0, l0) > 0;
                order.Add((Math.Atan2(m.Z, m.X), i, leftIsExit ? a.RightKerb : a.LeftKerb, leftIsExit ? a.LeftKerb : a.RightKerb));
            }
            order.Sort((x, y) => x.ang.CompareTo(y.ang));
            var chains = new List<List<V3>>(n);
            var mouths = new List<BoxMouth>(n);
            for (int k = 0; k < n; k++)
            {
                var ex = order[k].exit;
                var en = order[(k + 1) % n].entry;
                var chain = new List<V3>();
                bool found = false;
                for (int s = 0; s < ex.Count - 1 && !found; s++)
                    for (int q = 0; q < en.Count - 1 && !found; q++)
                        if (BoxPaintGeometry.SegmentIntersect(ex[s].XZ, ex[s + 1].XZ, en[q].XZ, en[q + 1].XZ, out double t, out double u))
                        {
                            for (int z = 0; z <= s; z++) chain.Add(ex[z]);
                            V2 p = ex[s].XZ + (ex[s + 1].XZ - ex[s].XZ) * t;
                            chain.Add(V3.FromXZ(p, 0.5 * (ex[s].Y + (ex[s + 1].Y - ex[s].Y) * t + en[q].Y + (en[q + 1].Y - en[q].Y) * u)));
                            for (int z = q; z >= 0; z--) chain.Add(en[z]);
                            found = true;
                        }
                if (!found)
                {
                    chain.AddRange(ex);
                    for (int z = en.Count - 1; z >= 0; z--) chain.Add(en[z]);
                    res.Notes.Add($"kerbs of arm {order[k].idx + 1} and arm {order[(k + 1) % n].idx + 1} do not meet: joined end to end");
                }
                var dedup = new List<V3>();
                foreach (var v in chain) if (dedup.Count == 0 || V2.Distance(dedup[dedup.Count - 1].XZ, v.XZ) > 1e-6) dedup.Add(v);
                chains.Add(dedup);
                var arm = order[k];
                Mouth(new ArmSlice(arm.entry, arm.exit), centre, out V2 mid, out V2 inward);
                mouths.Add(new BoxMouth
                {
                    Entry = arm.entry[0],
                    Exit = arm.exit[0],
                    Inward = inward,
                    MaxShift = Math.Max(0, V2.Dot(centre - mid, inward)),
                    MinShift = minShift[arm.idx],
                    Yielding = arms[arm.idx].Yielding,
                    Name = $"arm {arm.idx + 1}",
                });
            }
            var sb = StraightBox.Solve(mouths, chains, ok, margin, conflictPoints, signals);
            // shifts back to input order
            if (sb.Shift != null)
            {
                var sh = new double[n];
                for (int k = 0; k < n; k++) sh[order[k].idx] = sb.Shift[k];
                sb.Shift = sh;
            }
            res.Straight = sb;
            res.Notes.AddRange(sb.Notes);
            res.Outline = sb.Outline;
            res.Failure = sb.Failure;
            return res;
        }
    }
}
