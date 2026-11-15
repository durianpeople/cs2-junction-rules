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

    /// <summary>A marked crossing on the junction: its centre line (A to B, across the arm) and its width along the road.</summary>
    public struct CrosswalkStrip
    {
        public V2 A, B;
        public double Width;
        public CrosswalkStrip(V2 a, V2 b, double width) { A = a; B = b; Width = width; }
    }

    /// <summary>One arm as the game gives it: the two outer edges of its node slice (index 0 = the mouth, the .a end) and the inset (footway + extra) for each.</summary>
    public sealed class ArmInput
    {
        public List<V3> OuterA = new List<V3>();
        public List<V3> OuterB = new List<V3>();
        public double InsetA, InsetB;
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
        /// How far into the junction each arm's box starts: the far (junction-side) edge of the marked crossings on that
        /// arm, measured square to the mouth line; 0 where there is none (the mouth is the stop / give-way line, where
        /// the edge's lanes end). A crossing goes to the arm whose mouth segment is nearest its middle (within
        /// CrossingAssignMax).
        /// </summary>
        public static double[] StopDistances(IList<ArmSlice> arms, V2 centre, IList<CrosswalkStrip> crossings, List<string> notes = null)
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
                double far = Math.Max(V2.Dot(c.A - mids[best], ins[best]), V2.Dot(c.B - mids[best], ins[best])) + half;
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

        /// <summary>
        /// The whole carriageway outline: each arm's outer edges moved in by its insets (the kerb lines, following the
        /// corner curves), trimmed to the stop line / far side of the zebra, merged into one ring (BuildOutline).
        /// </summary>
        public static CarriagewayResult Build(IList<ArmInput> arms, V2 centre, IList<CrosswalkStrip> crossings, double straightenTol)
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
            var kerbs = new List<ArmSlice>(arms.Count);
            foreach (var a in arms)
            {
                if (a.OuterA == null || a.OuterB == null || a.OuterA.Count < 2 || a.OuterB.Count < 2) { res.Failure = "an arm has an edge line with fewer than 2 points"; return res; }
                if (!IsFinite(a.InsetA) || !IsFinite(a.InsetB) || !a.OuterA.TrueForAll(IsFinite) || !a.OuterB.TrueForAll(IsFinite)) { res.Failure = "non-finite input (NaN/Inf) in an arm's edge lines or insets"; return res; }
                var ka = BoxPaintGeometry.OffsetToward(a.OuterA, a.InsetA, a.OuterB[0].XZ);
                var kb = BoxPaintGeometry.OffsetToward(a.OuterB, a.InsetB, a.OuterA[0].XZ);
                kerbs.Add(new ArmSlice(ka, kb));
            }
            res.StopDistance = StopDistances(kerbs, centre, crossings, res.Notes);
            for (int i = 0; i < kerbs.Count; i++)
            {
                if (res.StopDistance[i] <= 1e-6) continue;
                if (TrimToStopLine(kerbs[i], centre, res.StopDistance[i], out ArmSlice t)) kerbs[i] = t;
                else
                {
                    res.Notes.Add($"arm {i + 1}: crossing ends {res.StopDistance[i]:0.0} m in, past the kerb lines; box starts at the mouth");
                    res.StopDistance[i] = 0;
                }
            }
            res.Kerbs = kerbs;
            res.Outline = BoxPaintGeometry.BuildOutline(kerbs, centre, straightenTol, out string why);
            if (res.Outline == null) res.Failure = why;
            return res;
        }
    }
}
