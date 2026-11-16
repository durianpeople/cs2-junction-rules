using System.Collections.Generic;

namespace JunctionRules.Core
{
    // 0.4.0-probe (JR-T-007): which game objects can carry a yellow box. Game-independent: the tool
    // (Boxes/YellowBoxToolSystem.cs) and the paint system read the game data into these facts; the rules live here so
    // they are unit-tested (tests BoxSelectionTests). Every "no" carries the reason the tool shows / logs.
    //
    // Game data behind each fact (decompile ~/Code/decomp/yb-2026-11-13/full):
    // - Road arm: a ConnectedEdge of the node whose edge has Game.Net.Road and starts or ends at the node (Edge.m_Start /
    //   m_End). An edge connected in its middle (a building's sub-net joint) is not an arm. Paths (no Road) do not count.
    // - Highway: the arm edge's prefab RoadData.m_Flags has RoadFlags.UseHighwayRules (Game.Prefabs/RoadFlags.cs).
    // - Elevated / tunnel: the arm's EDGE composition NetCompositionData.m_Flags.m_General has Elevated or Tunnel
    //   (Game.Prefabs/CompositionFlags.cs); the game itself never connects a building to such an edge
    //   (Game.Buildings/RoadConnectionSystem.cs 358-361), and a node with a Game.Net.Elevation component whose height is
    //   not ~0 is off the ground.
    // - Roundabout / level crossing: node composition general flags Roundabout / LevelCrossing (or the node slice's
    //   m_MiddleRadius > 0 for a roundabout).
    // - Owned: the node or building has Game.Common.Owner (part of another object's sub-net or a building extension).
    // - 0.4.3 (JR-T-007c, owner ruling 3): a building's own access road meeting a public road. Every arm edge and the node
    //   are read for Game.Common.Owner; an owner chain (Owner -> Owner, at most 4 steps, e.g. a ServiceUpgrade building
    //   owned by its main building) that ends at a Game.Buildings.Building makes the arm / node "the building's own"
    //   (prefab sub-nets get Owner = the building: GenerateEdgesSystem.cs 1190-1194 adds the Owner of the creation
    //   definition; the building lists them in its Game.Net.SubNet buffer). Public = no Owner. A node with at least one
    //   public road arm and at least one road arm of ONE building (and, when the node itself is owned, owned by that same
    //   building) is a "building access junction": selectable like any junction (3+ road arms). A node whose road arms
    //   are ALL owned (inside a parking lot / site) stays excluded, so do nodes owned by a non-building (an asset's own
    //   network) or by two different buildings.
    // - Entrance access point: Building.m_RoadEdge + m_CurvePosition = the point on that road edge's centre curve nearest
    //   the middle of the lot's front (BuildingUtils.CalculateFrontPosition: lot depth * 4 m forward of the building
    //   position; RoadConnectionSystem.cs 455-458, 505-509; the curve position is MathUtils.Distance(curve.m_Bezier.xz,
    //   front.xz, out t), lines 371-385). The game skips road sides with a Raised / Lowered composition flag
    //   (lines 375-381) and never picks elevated / tunnel edges (358-361). 0.4.3 (JR-T-007c): that point is only the
    //   FALLBACK exit now; the real car exits come from the building's sub-nets and spawn locations (Core/BuildingExit.cs).
    //   A building with its own sub-net roads gets m_RoadEdge = its OWN road (RoadConnectionSystem.cs 461-486 checks the
    //   building's SubNet edges first), which is why such a fallback is ruled out (RoadOwned).

    /// <summary>What the tool / paint system read about a node.</summary>
    public struct JunctionFacts
    {
        public bool Exists;          // entity exists, not Deleted, not Temp
        public bool IsRoadNode;      // has Game.Net.Node and Game.Net.Road
        public int RoadArms;         // road edges that start or end here
        public int PathArms;         // non-road edges (paths) that start or end here
        public bool AnyHighwayArm;
        public bool AnyElevatedOrTunnelArm;
        public bool NodeOffGround;   // Game.Net.Elevation present with |height| > ElevationTolerance
        public bool Roundabout;
        public bool LevelCrossing;
        public bool Owned;
        /// <summary>0.4.3: the node's owner chain ends at a building (Owned and NodeOwnerBuilding).</summary>
        public bool NodeOwnerIsBuilding;
        /// <summary>0.4.3: road arms with no Owner (public roads).</summary>
        public int PublicRoadArms;
        /// <summary>0.4.3: road arms whose owner chain ends at a building (that building's own access road).</summary>
        public int BuildingRoadArms;
        /// <summary>0.4.3: road arms owned by something that is not a building (an asset's own net).</summary>
        public int OtherOwnedRoadArms;
        /// <summary>0.4.3: the owned arms (and an owned node) do not all belong to the same building.</summary>
        public bool MixedOwners;
    }

    /// <summary>What the tool / paint system read about one building car exit (0.4.0-0.4.2: the building's road access).</summary>
    public struct EntranceFacts
    {
        public bool Exists;          // entity exists, not Deleted, not Temp
        public bool IsBuilding;      // has Game.Buildings.Building
        public bool Owned;           // Owner (sub-building / extension): its main building is the one to pick
        public bool HasRoadEdge;     // Building.m_RoadEdge != Entity.Null and that edge exists
        public bool RoadIsRoad;      // the road edge has Game.Net.Road (not a path / track)
        public bool RoadOwned;       // the road edge has Game.Common.Owner: a building's own sub-net road (RoadConnectionSystem.cs 463-486), inside its site
        public bool RoadHighway;
        public bool RoadElevatedOrTunnel;
        public bool OnRoad;          // prefab CanBeOnRoad / CanBeOnRoadArea without CanBeRoadSide: stands on the road itself
        public bool AccessInsideEdge; // the exit point lies on the edge's own part, between its two node slices
    }

    public static class BoxSelection
    {
        /// <summary>Height (m) above which a node's Game.Net.Elevation counts as off the ground.</summary>
        public const double ElevationTolerance = 0.1;

        /// <summary>True when a yellow box may go on this node; otherwise 'why' says what rules it out.</summary>
        public static bool JunctionOk(JunctionFacts f, out string why)
        {
            if (!f.Exists) { why = "gone"; return false; }
            if (!f.IsRoadNode) { why = "not a road node (path, track or other network)"; return false; }
            if (f.Owned || f.BuildingRoadArms > 0 || f.OtherOwnedRoadArms > 0)
            {
                // 0.4.3 (owner ruling 3): a building's own access road meeting a public road is a real junction.
                if (f.Owned && !f.NodeOwnerIsBuilding) { why = "part of an asset's own network"; return false; }
                if (f.OtherOwnedRoadArms > 0) { why = "an asset's own road meets here"; return false; }
                if (f.PublicRoadArms == 0) { why = "inside a building's own network (all its roads belong to the building)"; return false; }
                if (f.MixedOwners) { why = "roads of two different buildings meet here"; return false; }
            }
            if (f.RoadArms <= 1) { why = f.RoadArms == 1 ? "dead end" : "no road arms"; return false; }
            if (f.RoadArms == 2) { why = f.PathArms > 0 ? "a path joining a road, not a junction" : "a mid-road node, not a junction"; return false; }
            if (f.AnyHighwayArm) { why = "highway"; return false; }
            if (f.AnyElevatedOrTunnelArm || f.NodeOffGround) { why = "not at ground level (bridge, elevated road or tunnel)"; return false; }
            if (f.Roundabout) { why = "roundabout (the box would cover the island)"; return false; }
            if (f.LevelCrossing) { why = "level crossing (rail through the junction)"; return false; }
            why = null;
            return true;
        }

        /// <summary>
        /// 0.4.7 (JR-T-013c, G5): where a refused (or toggled) target is and what meets there, for the tool's log lines:
        /// "at (x, z), arms: 1 Name (prefab, width m), ...". The arm numbers are the shape solver's ("corner arm 3 - arm 1"
        /// in a failure names arms 3 and 1 of this list): BoxShapes.ArmEdges gives them in the solver's order.
        /// </summary>
        public static string Describe(V3 position, IList<ArmLabel> arms)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.Append("at (").Append(position.X.ToString("0.00", ci)).Append(", ").Append(position.Z.ToString("0.00", ci)).Append("), arms: ");
            if (arms == null || arms.Count == 0) return sb.Append("none").ToString();
            for (int i = 0; i < arms.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var a = arms[i];
                sb.Append(i + 1).Append(' ').Append(string.IsNullOrEmpty(a.Name) ? "unnamed" : a.Name)
                  .Append(" (").Append(string.IsNullOrEmpty(a.Prefab) ? "?" : a.Prefab).Append(", ").Append(a.Width.ToString("0.0", ci)).Append(" m)");
            }
            return sb.ToString();
        }

        /// <summary>0.4.7 (G5): an exit target for the log: "at (x, z) on Name (prefab, width m)"; no road = "at (x, z), no road".</summary>
        public static string DescribeExit(V3 position, ArmLabel? road)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string at = "at (" + position.X.ToString("0.00", ci) + ", " + position.Z.ToString("0.00", ci) + ")";
            if (road == null) return at + ", no road";
            var a = road.Value;
            return at + " on " + (string.IsNullOrEmpty(a.Name) ? "unnamed" : a.Name) + " (" + (string.IsNullOrEmpty(a.Prefab) ? "?" : a.Prefab) + ", " + a.Width.ToString("0.0", ci) + " m)";
        }

        /// <summary>0.4.3: true when the node is a building access junction (a building's own road meets a public road).</summary>
        public static bool IsBuildingAccessJunction(JunctionFacts f) => f.BuildingRoadArms > 0 && f.PublicRoadArms > 0 && !f.MixedOwners && f.OtherOwnedRoadArms == 0;

        /// <summary>True when a building car exit may take an exit box; otherwise 'why' says what rules it out.</summary>
        public static bool ExitOk(EntranceFacts f, out string why) => EntranceOk(f, out why);

        /// <summary>Same as ExitOk (0.4.0 name, kept for the tests and the save-side code).</summary>
        public static bool EntranceOk(EntranceFacts f, out string why)
        {
            if (!f.Exists) { why = "gone"; return false; }
            if (!f.IsBuilding) { why = "not a building"; return false; }
            if (f.Owned) { why = "part of another building (pick the main building)"; return false; }
            if (!f.HasRoadEdge) { why = "no car exit and no road access"; return false; }
            if (!f.RoadIsRoad) { why = "exit is not on a road"; return false; }
            if (f.RoadOwned) { why = "exit is on a road inside a building's own site (box the junction where that road meets the public road)"; return false; }
            if (f.OnRoad) { why = "stands on the road itself"; return false; }
            if (f.RoadHighway) { why = "exit on a highway"; return false; }
            if (f.RoadElevatedOrTunnel) { why = "exit road not at ground level"; return false; }
            if (!f.AccessInsideEdge) { why = "exit is inside a junction (box that junction instead)"; return false; }
            why = null;
            return true;
        }

        /// <summary>
        /// Toggle result for the tool: given whether the target has a box now and whether it is valid, what the click
        /// does. A box is always removable (also from a target that is no longer valid); a new one only on a valid target.
        /// </summary>
        public static ToggleAction Toggle(bool hasBox, bool valid)
        {
            if (hasBox) return ToggleAction.Remove;
            return valid ? ToggleAction.Add : ToggleAction.None;
        }

        /// <summary>
        /// What happens to a saved box when its node or building changed (paint system keep check): keep it while the
        /// target is valid; drop it (remove the component) once the target is gone or no longer valid.
        /// </summary>
        public static bool KeepSavedBox(bool valid) => valid;
    }

    public enum ToggleAction { None, Add, Remove }

    /// <summary>0.4.7 (JR-T-013c, G5): one road arm of a node for the tool's log lines: street name (null = unnamed), road prefab, composition width.</summary>
    public readonly struct ArmLabel
    {
        public readonly string Name, Prefab;
        public readonly float Width;
        public ArmLabel(string name, string prefab, float width) { Name = name; Prefab = prefab; Width = width; }
    }
}
