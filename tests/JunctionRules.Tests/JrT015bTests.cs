using System;
using System.Collections.Generic;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>0.4.9 (JR-T-015): owner items G16 (boxes turn white in snow) and G17 (No Vehicle Despawn did not see the G15 loop).</summary>
    internal static partial class Program
    {
        private static void G17BlockerTests()
        {
            // RoomScan names the vehicle that took the room
            RoomScan r = RoomScan.Begin(6.5f, 1f);
            r.Add(20f, 6f, 0f, 101, 1);            // plenty of room before it
            Check(r.HasRoom && r.BlockerIndex == 0, "room left: nobody named");
            r.Add(9f, 6f, 0f, 102, 3);             // 9 - 7 occupied = 2 m < 7.5 needed
            Check(!r.HasRoom && r.BlockerIndex == 102 && r.BlockerVersion == 3, "no room: the vehicle that took it is named (102:3)");
            RoomScan z = RoomScan.Begin(4f, 1f);
            z.Add(6f, 4f, 0f, 201, 1);             // room 6 >= 5: fine
            z.ExtendPast(3f);                      // a further zebra: room needed 8 > 6
            Check(!z.HasRoom && z.BlockerIndex == 201, "room lost to a further zebra: the vehicle with the least room is named");
            RoomScan p = RoomScan.Begin(4f, 1f);
            p.FurtherZebra(0f, 3f, people: true);
            Check(!p.HasRoom && p.BlockerIndex == 0, "people on a further zebra: no vehicle named (the hold keeps Entity.Null)");

            // The No Vehicle Despawn chain walk (TagStuckObjectsJob.IsBlocked: follow Blocker.m_Blocker; Entity.Null or a
            // Temporary type or m_MaxSpeed >= minStuckSpeed (3) ends the walk "not stuck"; back to the start = deadlock).
            bool NtdLoop(Dictionary<int, (int next, byte maxSpeed)> blockers, int start)
            {
                if (!blockers.TryGetValue(start, out var b) || b.next == 0 || b.maxSpeed >= 3) return false;
                int cur = b.next;
                for (int n = 0; n < 100; n++)
                {
                    if (cur == start) return true;
                    if (!blockers.TryGetValue(cur, out var c) || c.next == 0 || c.maxSpeed >= 3) return false;
                    cur = c.next;
                }
                return false;
            }
            // G15 live: 1 van (entering hold), 2 van 380843 in the drive (stock Continuing behind the yard queue), 3 yard van
            // 174655 (stock Crossing for 174632), 4 = 174632 (stock Continuing behind the exit queue), 5 exit head 380901 (exit hold)
            Dictionary<int, (int, byte)> Chain(bool rule049) => new Dictionary<int, (int, byte)>
            {
                [1] = (rule049 ? 2 : 0, 0),   // 0.4.8 wrote Entity.Null for the entering hold
                [2] = (3, 0),
                [3] = (4, 0),
                [4] = (5, 0),
                [5] = (rule049 ? 1 : 0, 0),   // 0.4.8 wrote Entity.Null for the exit hold
            };
            Check(!NtdLoop(Chain(false), 5) && !NtdLoop(Chain(false), 2), "0.4.8: the chain stops at the Junction Rules holds (Entity.Null): No Vehicle Despawn sees no loop");
            Check(NtdLoop(Chain(true), 5) && NtdLoop(Chain(true), 2), "0.4.9: holds name the vehicle they wait for: the same loop is visible to blocker-chain finders");

            string root = RepoRoot();
            string it = File.ReadAllText(Path.Combine(root, "JunctionRules", "Vanilla", "JRCarLaneSpeedIterator.cs"));
            string nav = File.ReadAllText(Path.Combine(root, "JunctionRules", "Vanilla", "JRCarNavigationSystem.cs"));
            int named = 0, idx = 0;
            while ((idx = it.IndexOf("m_Blocker = m_JR.HoldBlocker;", idx, StringComparison.Ordinal)) >= 0) { named++; idx++; }
            Check(named == 5, $"iterator holds S2-25, S2-28, S2-28b, S2-37, S2-29 name the blocker ({named})");
            int navNamed = 0; idx = 0;
            while ((idx = nav.IndexOf("m_JR.HoldBlocker;", idx, StringComparison.Ordinal)) >= 0) { navNamed++; idx++; }
            Check(navNamed == 4 && nav.Contains("blocker.m_MaxSpeed = (byte)math.clamp(Mathf.RoundToInt(jrAreaExit * 2.2949998f), 0, 255);")
                && nav.Contains("blocker.m_MaxSpeed = (byte)math.clamp(Mathf.RoundToInt(jrTargetExit * 2.2949998f), 0, 255);"),
                "navigation holds S2-30, S2-32, S2-35, S2-36 name the blocker; the area / target branches also write the held speed (0.4.8 left 255 = 'moving')");
            string all = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRExitGiveWay.cs")) + File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRYellowBox.cs"))
                + File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            int begins = 0; idx = 0;
            while ((idx = all.IndexOf("BeginHoldBlocker();   // 0.4.9 (G17)", idx, StringComparison.Ordinal)) >= 0) { begins++; idx++; }
            Check(begins == 8, $"every hold hook clears the blocker first ({begins} of 8)");
        }

        private static void G16SnowTests()
        {
            string root = RepoRoot();
            string gpu = File.ReadAllText(Path.Combine(root, "JunctionRules", "Paint", "PaintGpu.cs"));
            Check(gpu.Contains("Shader.PropertyToID(\"_Snow_BaseColorMap\")") && gpu.Contains("SetT(s_SnowBaseColorMap, \"_Snow_BaseColorMap\", m_SnowPaint);"),
                "G16: the snow colour map of the box material is set");
            Check(gpu.Contains("Color paint = new Color((float)rp.Color[0], (float)rp.Color[1], (float)rp.Color[2], 1f);") && gpu.Contains("m_SnowPaint = Plain(paint, false);"),
                "G16: the snow colour is the paint colour (sRGB, like the base colour)");
            Check(gpu.Contains("if (m_SnowPaint != null) { UnityEngine.Object.Destroy(m_SnowPaint); m_SnowPaint = null; }"), "G16: the texture is freed with the material");
            string asm = Path.Combine(root, "docs", "shader-areadecal", "blob054.asm");
            if (File.Exists(asm))
            {
                string a = File.ReadAllText(asm);
                Check(a.Contains("sample_b_indexable(texture2d) r8.xyzw, r6.zwzz, t5.xyzw, s0") && a.Contains("mad o0.xyz, r3.x, r8.xyzx, r7.yzwy"),
                    "G16 evidence: the DBufferMesh pixel shader lerps the albedo to the t5 (_Snow_BaseColorMap) sample by the snow factor");
            }
        }
    }
}
