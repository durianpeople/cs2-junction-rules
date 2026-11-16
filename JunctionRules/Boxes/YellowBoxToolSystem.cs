using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using JunctionRules.Core;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Scripting;

namespace JunctionRules.Boxes
{
    /// <summary>
    /// 0.4.0-probe (JR-T-007): the yellow box selection tool. A ToolBaseSystem like the game's own tools and Traffic /
    /// TLE (Traffic.Tools/PriorityToolSystem.cs: toolID, GetPrefab null, TrySetPrefab false, InitializeRaycast with
    /// TypeMask.Net; TLE Systems.Tool/ToolSystem.cs: the game's applyAction, OverlayRenderSystem circles).
    /// - Inert until used: created disabled by the game's ToolBaseSystem.OnCreate (Enabled = false); it runs only while it
    ///   is the active tool (toolbar button, the Options button, or the keyboard shortcut).
    /// - Hover (0.4.2, owner 16 Nov 2026: look like Traffic's Priorities tool, not a yellow blob): a thin outline of the
    ///   box itself (the same straight-sided shape the paint draws, Boxes/BoxShapes.cs) on a valid junction node
    ///   (Core/BoxSelection.JunctionOk) or building access (EntranceOk): white = click adds a box, orange = has a box,
    ///   click removes it (Core/BoxHover.cs: Traffic's colours and line width, with cites). No fill. Nothing on anything else.
    /// - Left click (the game's Apply action): toggles the box component on the node / building (Boxes/BoxComponents.cs),
    ///   then asks the paint system to redraw at once.
    /// - Right click (the game's Cancel action) or Esc: back to the game's default tool.
    /// Writes only its own two components; never adds Updated (the node / building is not rebuilt by a toggle).
    /// </summary>
    public partial class YellowBoxToolSystem : ToolBaseSystem
    {
        public const string kToolID = "JunctionRules.YellowBoxTool";

        public override string toolID => kToolID;

        private OverlayRenderSystem m_Overlay;
        private Target m_Hover;
        private string m_LastWhy;

        // hover outline cache: the shape is solved once per hovered target (re-solved every kRefreshFrames while hovered)
        private Entity m_CacheEntity;
        private List<(V3 A, V3 B)> m_CacheSegments;
        private string m_CacheFailure;
        private int m_CacheAge;
        private const int kRefreshFrames = 30;

        internal enum Kind { None, Junction, Entrance }

        internal struct Target
        {
            public Kind Kind;
            public Entity Entity;
            public bool Valid, HasBox;
            public string Why;
            public float3 Position;
            public BoxFacts.Access Access;   // entrance: the building's access on its road
        }

        public override PrefabBase GetPrefab() => null;

        public override bool TrySetPrefab(PrefabBase prefab) => false;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
        }

        [Preserve]
        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_Hover = default;
            m_LastWhy = null;
            applyAction.shouldBeEnabled = true;
            cancelAction.shouldBeEnabled = true;
            Mod.log.Info("yellow box tool: on (left click a junction or a building's access to add / remove a box; right click or Esc to leave)");
        }

        [Preserve]
        protected override void OnStopRunning()
        {
            m_Hover = default;
            base.OnStopRunning();   // the game's ResetActions disables apply / cancel again
            Mod.log.Info("yellow box tool: off");
        }

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            m_ToolRaycastSystem.collisionMask = CollisionMask.OnGround | CollisionMask.Overground;
            m_ToolRaycastSystem.typeMask = TypeMask.Net | TypeMask.StaticObjects;
            m_ToolRaycastSystem.netLayerMask = Layer.Road;
        }

        [Preserve]
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            try
            {
                if (cancelAction.WasPressedThisFrame() || EscPressed())
                {
                    m_ToolSystem.activeTool = m_DefaultToolSystem;
                    return inputDeps;
                }
                m_Hover = GetRaycastResult(out Entity hitEntity, out Game.Common.RaycastHit hit) ? Resolve(hitEntity, hit) : default;
                if (m_Hover.Kind != Kind.None && (m_Hover.Valid || m_Hover.HasBox)) Draw(m_Hover);
                if (m_Hover.Kind != Kind.None && !m_Hover.Valid && !m_Hover.HasBox && m_Hover.Why != m_LastWhy)
                {
                    m_LastWhy = m_Hover.Why;
                    Mod.log.Info($"yellow box tool: {(m_Hover.Kind == Kind.Junction ? "node" : "building")} {m_Hover.Entity} not selectable: {m_Hover.Why}");
                }
                if (applyAction.WasPressedThisFrame() && m_Hover.Kind != Kind.None) Toggle(m_Hover);
            }
            catch (Exception e)
            {
                Mod.log.Error(e, "yellow box tool failed; back to the default tool (nothing else of Junction Rules affected)");
                m_ToolSystem.activeTool = m_DefaultToolSystem;
            }
            return inputDeps;
        }

        private static bool EscPressed()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
        }

        // ------------------------------------------------------------------ what the cursor is on

        private Target Resolve(Entity hitEntity, Game.Common.RaycastHit hit)
        {
            EntityManager em = EntityManager;
            if (em.HasComponent<Game.Net.Node>(hitEntity)) return JunctionTarget(hitEntity);
            if (em.HasComponent<Edge>(hitEntity))
            {
                // On the road: the end node when the hit lies inside that node's area, else the access of a building
                // connected to this edge nearest the hit (within half the entrance length + 2 m along the road).
                Edge edge = em.GetComponentData<Edge>(hitEntity);
                foreach (Entity n in new[] { edge.m_Start, edge.m_End })
                {
                    if (!em.HasComponent<NodeGeometry>(n)) continue;
                    Bounds3 b = em.GetComponentData<NodeGeometry>(n).m_Bounds;
                    if (MathUtils.Intersect(b.xz, hit.m_HitPosition.xz)) return JunctionTarget(n);
                }
                return NearestEntrance(hitEntity, hit.m_HitPosition);
            }
            if (em.HasComponent<Building>(hitEntity) || em.HasComponent<Owner>(hitEntity))
            {
                Entity b = BoxFacts.MainBuilding(em, hitEntity);
                if (em.HasComponent<Building>(b)) return EntranceTarget(b);
            }
            return default;
        }

        private Target JunctionTarget(Entity node)
        {
            EntityManager em = EntityManager;
            var t = new Target { Kind = Kind.Junction, Entity = node };
            t.Valid = BoxSelection.JunctionOk(BoxFacts.Junction(em, node), out t.Why);
            t.HasBox = em.HasComponent<YellowBoxJunction>(node);
            if (em.HasComponent<Game.Net.Node>(node)) t.Position = em.GetComponentData<Game.Net.Node>(node).m_Position;
            return t;
        }

        private Target NearestEntrance(Entity edge, float3 hitPos)
        {
            EntityManager em = EntityManager;
            if (!em.HasBuffer<ConnectedBuilding>(edge)) return default;
            DynamicBuffer<ConnectedBuilding> cb = em.GetBuffer<ConnectedBuilding>(edge, true);
            float reach = (float)(BoxStore.EntranceLength * 0.5 + 2.0);
            Entity best = Entity.Null;
            float bestD = float.MaxValue;
            for (int i = 0; i < cb.Length; i++)
            {
                Entity b = cb[i].m_Building;
                if (!em.Exists(b) || !em.HasComponent<Building>(b)) continue;
                Building bd = em.GetComponentData<Building>(b);
                if (bd.m_RoadEdge != edge) continue;
                BoxFacts.Access a = BoxFacts.AccessOf(em, edge, bd.m_CurvePosition);
                if (!a.Valid) continue;
                float d = math.distance(a.Point.xz, hitPos.xz);
                if (d < bestD) { bestD = d; best = b; }
            }
            return best != Entity.Null && bestD <= reach ? EntranceTarget(best) : default;
        }

        private Target EntranceTarget(Entity building)
        {
            EntityManager em = EntityManager;
            var t = new Target { Kind = Kind.Entrance, Entity = building };
            t.Valid = BoxSelection.EntranceOk(BoxFacts.Entrance(em, building, out BoxFacts.Access a), out t.Why);
            t.HasBox = em.HasComponent<YellowBoxEntrance>(building);
            t.Access = a;
            if (a.Valid) t.Position = a.Point;
            else if (em.HasComponent<Game.Objects.Transform>(building)) t.Position = em.GetComponentData<Game.Objects.Transform>(building).m_Position;
            return t;
        }

        // ------------------------------------------------------------------ hover outline and click

        /// <summary>
        /// The box outline as Traffic draws its hover (Core/BoxHover.cs): one OverlayRenderSystem.Buffer.DrawLine per ring
        /// side (decompile Game.Rendering/OverlayRenderSystem.cs 207: DrawLine(outlineColor, fillColor, outlineWidth,
        /// styleFlags, Line3.Segment, width, roundness)) with outline = fill colour, outlineWidth 0, StyleFlags 0, width
        /// BoxHover.LineWidth, roundness (1, 1). Nothing is filled.
        /// </summary>
        private void Draw(Target t)
        {
            List<(V3 A, V3 B)> segs = Outline(t);
            if (segs == null || segs.Count == 0) return;
            float[] rgba = BoxHover.Colour(t.HasBox);
            var c = new Color(rgba[0], rgba[1], rgba[2], rgba[3]);
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle deps);
            deps.Complete();
            foreach (var (a, b) in segs)
            {
                var line = new Line3.Segment(new float3((float)a.X, (float)a.Y, (float)a.Z), new float3((float)b.X, (float)b.Y, (float)b.Z));
                buffer.DrawLine(c, c, 0f, (OverlayRenderSystem.StyleFlags)0, line, BoxHover.LineWidth, new float2(1f, 1f));
            }
        }

        /// <summary>The hovered target's box outline (cached per target; solved by Boxes/BoxShapes.cs, the paint's shape code).</summary>
        private List<(V3 A, V3 B)> Outline(Target t)
        {
            if (t.Entity == m_CacheEntity && m_CacheSegments != null && ++m_CacheAge < kRefreshFrames) return m_CacheSegments;
            m_CacheEntity = t.Entity;
            m_CacheAge = 0;
            BoxShapes.Shape shape = t.Kind == Kind.Junction
                ? BoxShapes.Junction(EntityManager, t.Entity, BoxStore.KerbInset, BoxStore.MarkingMargin)
                : BoxShapes.Entrance(EntityManager, t.Access, BoxStore.KerbInset, BoxStore.EntranceLength, BoxStore.MarkingMargin);
            m_CacheSegments = BoxHover.Segments(shape.Outline);
            if (shape.Outline == null && shape.Failure != m_CacheFailure)
                Mod.log.Info($"yellow box tool: {(t.Kind == Kind.Junction ? "node" : "building")} {t.Entity}: no box outline to show ({shape.Failure})");
            m_CacheFailure = shape.Outline == null ? shape.Failure : null;
            return m_CacheSegments;
        }

        private void Toggle(Target t)
        {
            EntityManager em = EntityManager;
            ToggleAction act = BoxSelection.Toggle(t.HasBox, t.Valid);
            if (act == ToggleAction.None)
            {
                Mod.log.Info($"yellow box tool: {(t.Kind == Kind.Junction ? "node" : "building")} {t.Entity}: not selectable ({t.Why}); nothing changed");
                return;
            }
            bool add = act == ToggleAction.Add;
            bool junction = t.Kind == Kind.Junction;
            if (add) BoxStore.Add(em, t.Entity, junction);
            else BoxStore.Remove(em, t.Entity, junction);
            Mod.log.Info($"yellow box tool: {(add ? "box added on" : "box removed from")} {(t.Kind == Kind.Junction ? "junction node" : "building access")} {t.Entity} at {t.Position}" + (!add && !t.Valid ? " (target no longer valid: " + t.Why + ")" : ""));
        }
    }
}
