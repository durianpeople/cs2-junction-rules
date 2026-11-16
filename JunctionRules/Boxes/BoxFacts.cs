using Colossal.Mathematics;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using JunctionRules.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace JunctionRules.Boxes
{
    /// <summary>
    /// 0.4.0-probe (JR-T-007): reads the game data behind the selection rules (Core/BoxSelection.cs has the rules and the
    /// decompile cites). Read-only, main thread, EntityManager calls only. Shared by the tool and the paint system.
    /// </summary>
    internal static class BoxFacts
    {
        private const CompositionFlags.General OffGround = CompositionFlags.General.Elevated | CompositionFlags.General.Tunnel;

        internal static bool Alive(EntityManager em, Entity e)
        {
            return e != Entity.Null && em.Exists(e) && !em.HasComponent<Deleted>(e) && !em.HasComponent<Temp>(e);
        }

        internal static JunctionFacts Junction(EntityManager em, Entity node)
        {
            var f = new JunctionFacts();
            f.Exists = Alive(em, node);
            if (!f.Exists) return f;
            f.IsRoadNode = em.HasComponent<Game.Net.Node>(node) && em.HasComponent<Road>(node) && em.HasBuffer<ConnectedEdge>(node);
            if (!f.IsRoadNode) return f;
            f.Owned = em.HasComponent<Owner>(node);
            if (em.HasComponent<Game.Net.Elevation>(node))
            {
                float2 h = em.GetComponentData<Game.Net.Elevation>(node).m_Elevation;
                f.NodeOffGround = math.abs(h.x) > BoxSelection.ElevationTolerance || math.abs(h.y) > BoxSelection.ElevationTolerance;
            }
            f.Roundabout = em.HasComponent<Roundabout>(node);
            DynamicBuffer<ConnectedEdge> connected = em.GetBuffer<ConnectedEdge>(node, true);
            for (int i = 0; i < connected.Length; i++)
            {
                Entity e = connected[i].m_Edge;
                if (!em.Exists(e) || !em.HasComponent<Edge>(e)) continue;
                Edge edge = em.GetComponentData<Edge>(e);
                bool isStart = edge.m_Start == node, isEnd = edge.m_End == node;
                if (!isStart && !isEnd) continue;
                if (!em.HasComponent<Road>(e)) { f.PathArms++; continue; }
                f.RoadArms++;
                if (IsHighway(em, e)) f.AnyHighwayArm = true;
                if (em.HasComponent<Composition>(e))
                {
                    Composition c = em.GetComponentData<Composition>(e);
                    if (em.HasComponent<NetCompositionData>(c.m_Edge) && (em.GetComponentData<NetCompositionData>(c.m_Edge).m_Flags.m_General & OffGround) != 0) f.AnyElevatedOrTunnelArm = true;
                    Entity nc = isStart ? c.m_StartNode : c.m_EndNode;
                    if (em.HasComponent<NetCompositionData>(nc))
                    {
                        CompositionFlags.General g = em.GetComponentData<NetCompositionData>(nc).m_Flags.m_General;
                        if ((g & CompositionFlags.General.Roundabout) != 0) f.Roundabout = true;
                        if ((g & CompositionFlags.General.LevelCrossing) != 0) f.LevelCrossing = true;
                        if ((g & OffGround) != 0) f.AnyElevatedOrTunnelArm = true;
                    }
                }
            }
            return f;
        }

        internal static bool IsHighway(EntityManager em, Entity edge)
        {
            if (!em.HasComponent<PrefabRef>(edge)) return false;
            Entity prefab = em.GetComponentData<PrefabRef>(edge).m_Prefab;
            return em.HasComponent<RoadData>(prefab) && (em.GetComponentData<RoadData>(prefab).m_Flags & Game.Prefabs.RoadFlags.UseHighwayRules) != 0;
        }

        internal static bool EdgeOffGround(EntityManager em, Entity edge)
        {
            if (!em.HasComponent<Composition>(edge)) return false;
            Entity c = em.GetComponentData<Composition>(edge).m_Edge;
            return em.HasComponent<NetCompositionData>(c) && (em.GetComponentData<NetCompositionData>(c).m_Flags.m_General & OffGround) != 0;
        }

        /// <summary>The building a raycast hit stands for: an extension / sub-building's main building (via Owner).</summary>
        internal static Entity MainBuilding(EntityManager em, Entity hit)
        {
            Entity e = hit;
            for (int i = 0; i < 4 && e != Entity.Null && em.HasComponent<Owner>(e); i++)
            {
                Entity o = em.GetComponentData<Owner>(e).m_Owner;
                if (!em.Exists(o) || !em.HasComponent<Building>(o)) break;
                e = o;
            }
            return e;
        }

        internal static EntranceFacts Entrance(EntityManager em, Entity building, out Access access)
        {
            access = default;
            var f = new EntranceFacts();
            f.Exists = Alive(em, building);
            if (!f.Exists) return f;
            f.IsBuilding = em.HasComponent<Building>(building);
            if (!f.IsBuilding) return f;
            f.Owned = em.HasComponent<Owner>(building) && em.HasComponent<Building>(em.GetComponentData<Owner>(building).m_Owner);
            Building b = em.GetComponentData<Building>(building);
            Entity road = b.m_RoadEdge;
            f.HasRoadEdge = road != Entity.Null && em.Exists(road) && !em.HasComponent<Deleted>(road) && em.HasComponent<Curve>(road);
            if (em.HasComponent<PrefabRef>(building))
            {
                Entity prefab = em.GetComponentData<PrefabRef>(building).m_Prefab;
                if (em.HasComponent<BuildingData>(prefab))
                {
                    Game.Prefabs.BuildingFlags bf = em.GetComponentData<BuildingData>(prefab).m_Flags;
                    f.OnRoad = (bf & (Game.Prefabs.BuildingFlags.CanBeOnRoad | Game.Prefabs.BuildingFlags.CanBeOnRoadArea)) != 0 && (bf & Game.Prefabs.BuildingFlags.CanBeRoadSide) == 0;
                }
            }
            if (!f.HasRoadEdge) return f;
            f.RoadIsRoad = em.HasComponent<Road>(road) && em.HasComponent<Edge>(road);
            if (!f.RoadIsRoad) return f;
            f.RoadOwned = em.HasComponent<Owner>(road);
            f.RoadHighway = IsHighway(em, road);
            f.RoadElevatedOrTunnel = EdgeOffGround(em, road);
            access = AccessOf(em, road, b.m_CurvePosition);
            f.AccessInsideEdge = access.Valid && access.AlongM >= access.MinM - 0.01 && access.AlongM <= access.MaxM + 0.01;
            return f;
        }

        /// <summary>Where a building's access sits on its road edge, in metres along the edge's centre curve.</summary>
        internal struct Access
        {
            public bool Valid;
            public Entity Edge;
            public float3 Point;
            public float T;
            /// <summary>Access, and the edge's own part (between its node slices), in metres along the curve.</summary>
            public float AlongM, MinM, MaxM, LengthM;
        }

        internal static Access AccessOf(EntityManager em, Entity road, float curvePosition)
        {
            var a = new Access { Edge = road, T = math.saturate(curvePosition) };
            if (!em.HasComponent<Curve>(road)) return a;
            Curve curve = em.GetComponentData<Curve>(road);
            a.Point = MathUtils.Position(curve.m_Bezier, a.T);
            a.LengthM = MathUtils.Length(curve.m_Bezier);
            a.AlongM = MathUtils.Length(curve.m_Bezier, new Bounds1(0f, a.T));
            a.MinM = 0f;
            a.MaxM = a.LengthM;
            if (em.HasComponent<EdgeGeometry>(road))
            {
                // The edge's own part runs from its start mouth (EdgeGeometry m_Start .a ends) to its end mouth (m_End .d ends).
                EdgeGeometry g = em.GetComponentData<EdgeGeometry>(road);
                float3 s = (g.m_Start.m_Left.a + g.m_Start.m_Right.a) * 0.5f;
                float3 e = (g.m_End.m_Left.d + g.m_End.m_Right.d) * 0.5f;
                MathUtils.Distance(curve.m_Bezier.xz, s.xz, out float ts);
                MathUtils.Distance(curve.m_Bezier.xz, e.xz, out float te);
                a.MinM = MathUtils.Length(curve.m_Bezier, new Bounds1(0f, math.min(ts, te)));
                a.MaxM = MathUtils.Length(curve.m_Bezier, new Bounds1(0f, math.max(ts, te)));
            }
            a.Valid = a.LengthM > 0.5f;
            return a;
        }

        /// <summary>Curve parameter at 'metres' along the curve (bisection on MathUtils.Length).</summary>
        internal static float TAt(Bezier4x3 c, float metres)
        {
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 24; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (MathUtils.Length(c, new Bounds1(0f, mid)) < metres) lo = mid; else hi = mid;
            }
            return (lo + hi) * 0.5f;
        }
    }
}
