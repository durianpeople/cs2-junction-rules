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
    // - Entrance access point: Building.m_RoadEdge + m_CurvePosition = the point on that road edge's centre curve nearest
    //   the middle of the lot's front (BuildingUtils.CalculateFrontPosition: lot depth * 4 m forward of the building
    //   position; RoadConnectionSystem.cs 455-458, 505-509; the curve position is MathUtils.Distance(curve.m_Bezier.xz,
    //   front.xz, out t), lines 371-385). The game skips road sides with a Raised / Lowered composition flag
    //   (lines 375-381) and never picks elevated / tunnel edges (358-361).

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
    }

    /// <summary>What the tool / paint system read about a building's road access.</summary>
    public struct EntranceFacts
    {
        public bool Exists;          // entity exists, not Deleted, not Temp
        public bool IsBuilding;      // has Game.Buildings.Building
        public bool Owned;           // Owner (sub-building / extension): its main building is the one to pick
        public bool HasRoadEdge;     // Building.m_RoadEdge != Entity.Null and that edge exists
        public bool RoadIsRoad;      // the road edge has Game.Net.Road (not a path / track)
        public bool RoadHighway;
        public bool RoadElevatedOrTunnel;
        public bool OnRoad;          // prefab CanBeOnRoad / CanBeOnRoadArea without CanBeRoadSide: stands on the road itself
        public bool AccessInsideEdge; // the access point lies on the edge's own part, between its two node slices
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
            if (f.Owned) { why = "part of a building's or asset's own network"; return false; }
            if (f.RoadArms <= 1) { why = f.RoadArms == 1 ? "dead end" : "no road arms"; return false; }
            if (f.RoadArms == 2) { why = f.PathArms > 0 ? "a path joining a road, not a junction" : "a mid-road node, not a junction"; return false; }
            if (f.AnyHighwayArm) { why = "highway"; return false; }
            if (f.AnyElevatedOrTunnelArm || f.NodeOffGround) { why = "not at ground level (bridge, elevated road or tunnel)"; return false; }
            if (f.Roundabout) { why = "roundabout (the box would cover the island)"; return false; }
            if (f.LevelCrossing) { why = "level crossing (rail through the junction)"; return false; }
            why = null;
            return true;
        }

        /// <summary>True when a building's access may take an entrance box; otherwise 'why' says what rules it out.</summary>
        public static bool EntranceOk(EntranceFacts f, out string why)
        {
            if (!f.Exists) { why = "gone"; return false; }
            if (!f.IsBuilding) { why = "not a building"; return false; }
            if (f.Owned) { why = "part of another building (pick the main building)"; return false; }
            if (!f.HasRoadEdge) { why = "no road access"; return false; }
            if (!f.RoadIsRoad) { why = "access is not on a road"; return false; }
            if (f.OnRoad) { why = "stands on the road itself"; return false; }
            if (f.RoadHighway) { why = "access on a highway"; return false; }
            if (f.RoadElevatedOrTunnel) { why = "access road not at ground level"; return false; }
            if (!f.AccessInsideEdge) { why = "access is inside a junction (box that junction instead)"; return false; }
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
}
