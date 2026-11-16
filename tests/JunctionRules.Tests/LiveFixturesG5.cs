// Generated from live read-only cs2 CLI reads on 17 Nov 2026 (game running 0.4.7-probe; node 551717:1 this session):
// cs2_road_outline {x:-518.49,z:545.74} (node slice curves m_Left.m_Left / m_Left.m_Right / m_Right.m_Right / m_Right.m_Left
// control points a-d, a = the mouth; node composition sync offsets and node-end sync targets per half) and cs2_junction_lanes
// curves:true (the non-unsafe car connection curves, for the conflict points). Footways per arm as the 0.4.7 refusal log
// gives them (5|5, 1|1, 1|1, 3|3), yield flags as it logged (both Eastgate Lane arms STOP). Zebras: not readable through
// the CLI; rebuilt from the game rule (a straight line across the arm, lane prefab width 2 m = NetLaneData.m_Width of
// "EU Crosswalk Lane 2" / "EU Alley Crosswalk Lane 2", ends at the sidewalk pedestrian lanes: +-6 m Crescent W, +-3.5 m
// Alley) at the distance in from the mouth that reproduces the logged box starts (+2.8, +2.7, +2.8 m = far edge + 0.5 m).
// Arm order = the refusal log's (ConnectedEdge order): 1 Crescent W, 2 Eastgate S, 3 Eastgate N, 4 Crescent E.
namespace JunctionRules.Tests
{
    internal sealed class G5LiveArm
    {
        public string Name;
        public double[] OuterA, InnerA, OuterB, InnerB;   // control points a-d (x, y, z) of m_Left.m_Left, m_Left.m_Right, m_Right.m_Right, m_Right.m_Left
        public double InsetA, InsetB;
        public bool Yielding;
        public double[] OffsetsLeft, TargetsLeft, OffsetsRight, TargetsRight;
        public double[] Zebra;   // null, or { centre distance in from the mouth, half length across }
        /// <summary>Node composition half width (width / 2; middle offset 0 at all four arms).</summary>
        public double CompHalf => Name.Contains("Alley") ? 4.0 : 8.0;
        public G5LiveArm(string name, double[] oa, double[] ia, double[] ob, double[] ib, double insetA, double insetB, bool yielding,
            double[] offL, double[] tgtL, double[] offR, double[] tgtR, double[] zebra)
        {
            Name = name; OuterA = oa; InnerA = ia; OuterB = ob; InnerB = ib; InsetA = insetA; InsetB = insetB; Yielding = yielding;
            OffsetsLeft = offL; TargetsLeft = tgtL; OffsetsRight = offR; TargetsRight = tgtR; Zebra = zebra;
        }
    }

    internal static class LiveFixturesG5
    {
        public static readonly double[] CrescentCentre = { -518.49, 545.74 };
        public static readonly G5LiveArm[] CrescentArms = new[]
        {
            // 688906:1 Crescent Street (start node end)
            new G5LiveArm("Crescent Street W (RoadBuilder 16 m, footways 5|5)",
                new double[] { -522.430, 595.450, 553.960, -522.300, 595.450, 553.960, -522.170, 595.480, 554.020, -522.070, 595.510, 554.120 },
                new double[] { -522.070, 595.450, 545.960, -520.850, 595.450, 545.980, -519.800, 595.450, 546.770, -519.200, 595.460, 547.840 },
                new double[] { -521.710, 595.450, 537.970, -521.510, 595.450, 537.970, -521.310, 595.450, 537.930, -521.140, 595.460, 537.840 },
                new double[] { -522.070, 595.450, 545.960, -520.850, 595.450, 545.980, -519.870, 595.450, 545.060, -519.210, 595.450, 544.040 },
                5.00, 5.00, false,
                new double[] { 0.000, 0.575, 0.625, 1.000 }, new double[] { 0.000, 0.475, 0.537, 1.000 }, new double[] { 0.000, 0.375, 0.425, 1.000 }, new double[] { 0.000, 0.467, 0.529, 1.000 },
                new double[] { 1.30, 6.00 }),
            // 1003231:1 Eastgate Lane (end node end)
            new G5LiveArm("Eastgate Lane S (Alley 8 m, footways 1|1)",
                new double[] { -520.830, 595.470, 537.480, -520.880, 595.470, 537.630, -521.000, 595.460, 537.760, -521.140, 595.460, 537.840 },
                new double[] { -516.660, 595.480, 537.540, -517.360, 595.460, 539.760, -517.930, 595.450, 542.080, -519.210, 595.450, 544.040 },
                new double[] { -512.480, 595.490, 537.590, -512.510, 595.490, 537.710, -512.510, 595.520, 537.850, -512.430, 595.560, 537.940 },
                new double[] { -516.660, 595.480, 537.540, -517.340, 595.460, 539.710, -517.670, 595.450, 542.120, -516.530, 595.490, 544.090 },
                1.00, 1.00, true,
                new double[] { 0.000, 0.150, 0.250, 1.000 }, new double[] { 0.000, 0.471, 0.533, 1.000 }, new double[] { 0.000, 0.750, 0.850, 1.000 }, new double[] { 0.000, 0.476, 0.685, 1.000 },
                new double[] { 1.20, 3.50 }),
            // 1003224:1 Eastgate Lane (end node end)
            new G5LiveArm("Eastgate Lane N (Alley 8 m, footways 1|1)",
                new double[] { -513.920, 595.580, 554.600, -513.920, 595.580, 554.470, -513.860, 595.580, 554.340, -513.760, 595.590, 554.240 },
                new double[] { -517.920, 595.580, 554.550, -517.950, 595.520, 552.240, -517.590, 595.500, 549.890, -516.260, 595.530, 548.010 },
                new double[] { -521.920, 595.570, 554.490, -521.920, 595.570, 554.350, -521.980, 595.540, 554.220, -522.070, 595.510, 554.120 },
                new double[] { -517.920, 595.580, 554.550, -517.950, 595.520, 552.240, -518.070, 595.480, 549.850, -519.200, 595.460, 547.840 },
                1.00, 1.00, true,
                new double[] { 0.000, 0.150, 0.250, 1.000 }, new double[] { 0.000, 0.318, 0.529, 1.000 }, new double[] { 0.000, 0.750, 0.850, 1.000 }, new double[] { 0.000, 0.463, 0.525, 1.000 },
                new double[] { 1.30, 3.50 }),
            // 1174307:1 Crescent Street (end node end)
            new G5LiveArm("Crescent Street E (Small Road Oneway 16 m, footways 3|3)",
                new double[] { -512.050, 595.660, 538.100, -512.190, 595.660, 538.100, -512.340, 595.610, 538.060, -512.430, 595.560, 537.940 },
                new double[] { -512.700, 595.640, 546.090, -514.240, 595.580, 546.080, -515.760, 595.520, 545.420, -516.530, 595.490, 544.090 },
                new double[] { -513.360, 595.620, 554.090, -513.510, 595.610, 554.080, -513.650, 595.600, 554.140, -513.760, 595.590, 554.240 },
                new double[] { -512.700, 595.640, 546.090, -514.130, 595.590, 546.080, -515.430, 595.540, 546.840, -516.260, 595.530, 548.010 },
                3.00, 3.00, false,
                new double[] { 0.000, 0.375, 0.625, 1.000 }, new double[] { 0.000, 0.315, 0.524, 1.000 }, new double[] { 0.000, 0.375, 0.625, 1.000 }, new double[] { 0.000, 0.471, 0.682, 1.000 },
                null),
        };
        public static readonly LivePath[] CrescentPaths = new[]
        {
            new LivePath("1174307:1/5", "688906:1/5", new double[] { -512.580, 595.440, 544.600, -515.720, 595.330, 544.560, -518.860, 595.240, 544.510, -522.000, 595.250, 544.470 }),
            new LivePath("1174307:1/6", "688906:1/6", new double[] { -512.830, 595.440, 547.590, -515.930, 595.310, 547.550, -519.030, 595.240, 547.510, -522.140, 595.250, 547.460 }),
            new LivePath("1003224:1/5", "688906:1/6", new double[] { -516.420, 595.380, 554.570, -516.470, 595.280, 551.050, -518.620, 595.240, 547.520, -522.140, 595.250, 547.460 }),
            new LivePath("1003231:1/5", "688906:1/5", new double[] { -518.220, 595.280, 537.510, -519.030, 595.250, 540.050, -520.170, 595.250, 544.490, -522.000, 595.250, 544.470 }),
            new LivePath("1174307:1/6", "1003224:1/6", new double[] { -512.830, 595.440, 547.590, -516.620, 595.290, 547.540, -519.480, 595.280, 550.740, -519.420, 595.370, 554.530 }),
            new LivePath("1174307:1/5", "1003231:1/6", new double[] { -512.580, 595.440, 544.600, -516.030, 595.320, 544.550, -516.120, 595.250, 540.850, -515.090, 595.290, 537.560 }),
            new LivePath("1003231:1/5", "1003224:1/6", new double[] { -518.220, 595.280, 537.510, -519.970, 595.220, 542.960, -519.510, 595.230, 548.810, -519.420, 595.370, 554.530 }),
            new LivePath("1003224:1/5", "1003231:1/6", new double[] { -516.420, 595.380, 554.570, -516.510, 595.230, 548.850, -516.800, 595.220, 543.020, -515.090, 595.290, 537.560 }),
        };
    }
}
