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
// 0.4.13 (JR-T-019): the target model lives in Boxes/BoxTargeting.cs (shared with JunctionRules.Api.JunctionRulesApi)
using Kind = JunctionRules.Boxes.BoxTargeting.Kind;
using Target = JunctionRules.Boxes.BoxTargeting.Target;

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
    ///   (Core/BoxSelection.JunctionOk) or building car exit (ExitOk): white = click adds a box, orange = has a box,
    ///   click removes it (Core/BoxHover.cs: Traffic's colours and line width, with cites). No fill. Nothing on anything else.
    /// - 0.4.3 (JR-T-007c, owner rulings 16 Nov 2026): building targets are its CAR EXITS (BoxFacts.Exits; Core/BuildingExit.cs),
    ///   each hovered and toggled on its own: pointing at the building picks the exit nearest the cursor, pointing at the
    ///   road picks the exit on that road nearest the cursor, and the outline sits at the exit (the drive), not the lot
    ///   middle. An exit through the building's own access road is that junction node (junction box).
    /// - JR-T-014c (G9 / G10, owner 17 Nov 2026): building targets are its real car ACCESSES: exits and entrances
    ///   (BoxFacts.Accesses; Core/BuildingAccess.cs). Never the lot-front fallback: a building with no real car access is not
    ///   selectable (a saved fallback key on it is offered for removal only). An entrance box comes with the building
    ///   (derived, never saved): clicking a white entrance boxes the building (saves the lot-front marker key when no exit is
    ///   boxed); clicking an orange one removes ALL the building's boxes. The hover draws an ARROW from the box mouth on the
    ///   kerb to the access (Core AccessArrow): head at the box = exit, at the building = entrance, both = two-way; dashed =
    ///   the lot-front fallback.
    /// - Left click (the game's Apply action): toggles the box component on the node / building (Boxes/BoxComponents.cs),
    ///   then asks the paint system to redraw at once.
    /// - Right click (the game's Cancel action) or Esc: back to the game's default tool.
    /// Writes only its own two components; never adds Updated (the node / building is not rebuilt by a toggle).
    /// - 0.4.13 (JR-T-019): the pick, the shape solve, the no-shape rule and the click's writes live in Boxes/BoxTargeting.cs
    ///   (decision: Core/BoxCommand.Decide), shared with JunctionRules.Api.JunctionRulesApi; this system keeps the raycast,
    ///   the hover cache / drawing and the input.
    /// </summary>
    public partial class YellowBoxToolSystem : ToolBaseSystem
    {
        public const string kToolID = "JunctionRules.YellowBoxTool";

        public override string toolID => kToolID;

        private OverlayRenderSystem m_Overlay;
        private Target m_Hover;
        private string m_LastWhy;

        // 0.4.13 (JR-T-019): the shared selection + shape + apply path (Boxes/BoxTargeting.cs); the API borrows this instance.
        private BoxTargeting m_Targeting;
        internal BoxTargeting Targeting => m_Targeting;

        // hover outline cache (JR-T-007b-r F5, Core/HoverCache.cs): solved once per hovered target; again only when the
        // target, its box state or the shape parameters change, or a source entity of the shape is Updated / gone.
        private readonly HoverCache<BoxTargeting.SolvedShape> m_Cache = new HoverCache<BoxTargeting.SolvedShape>();
        // F3: targets already warned about having no box shape (once per entity per tool session)
        private readonly HashSet<(Entity, long)> m_NoShapeWarned = new HashSet<(Entity, long)>();

        public override PrefabBase GetPrefab() => null;

        public override bool TrySetPrefab(PrefabBase prefab) => false;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_Targeting = new BoxTargeting(World);
        }

        [Preserve]
        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_Hover = default;
            m_LastWhy = null;
            m_Cache.Reset();
            m_NoShapeWarned.Clear();
            BoxStore.LoadParams("tool start");   // F6: hover uses the paint's parameters even before the paint system ran
            applyAction.shouldBeEnabled = true;
            cancelAction.shouldBeEnabled = true;
            Mod.log.Info("yellow box tool: on (left click a junction or a building's car exit to add / remove a box; right click or Esc to leave)");
        }

        [Preserve]
        protected override void OnStopRunning()
        {
            m_Hover = default;
            m_Cache.Reset();
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
                m_Targeting.TickCache();
                m_Hover = GetRaycastResult(out Entity hitEntity, out Game.Common.RaycastHit hit) ? m_Targeting.Resolve(hitEntity, hit.m_HitPosition) : default;
                if (m_Hover.Kind != Kind.None && (m_Hover.Valid || m_Hover.HasBox))
                {
                    BoxTargeting.SolvedShape h = Outline(m_Hover);
                    if (BoxTargeting.ApplyShapeRule(ref m_Hover, h))
                    {
                        // F3 (shared rule, Boxes/BoxTargeting.ApplyShapeRule): no shape and no box yet = not selectable
                        if (m_NoShapeWarned.Add((m_Hover.Entity, ExitSub(m_Hover))))   // review F5: once per exit, not per building
                            Mod.log.Warn($"yellow box tool: {What(m_Hover)} not selectable: {m_Hover.Why}" + (string.IsNullOrEmpty(h.Notes) ? "" : $"; notes: {h.Notes}"));
                    }
                    else Draw(m_Hover, h.Segments, h.Ring);
                }
                if (m_Hover.Kind != Kind.None && !m_Hover.Valid && !m_Hover.HasBox && !m_Hover.NoShape && m_Hover.Why != m_LastWhy)
                {
                    m_LastWhy = m_Hover.Why;
                    Mod.log.Info($"yellow box tool: {What(m_Hover)} not selectable: {m_Hover.Why}");
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

        // ------------------------------------------------------------------ shared with the API (Boxes/BoxTargeting.cs)

        private string What(Target t) => m_Targeting.What(t);

        private static long ExitSub(Target t) => BoxTargeting.ExitSub(t);

        // ------------------------------------------------------------------ hover outline and click

        /// <summary>
        /// The box outline as Traffic draws its hover (Core/BoxHover.cs): one OverlayRenderSystem.Buffer.DrawLine per ring
        /// side (decompile Game.Rendering/OverlayRenderSystem.cs 207: DrawLine(outlineColor, fillColor, outlineWidth,
        /// styleFlags, Line3.Segment, width, roundness)) with outline = fill colour, outlineWidth 0, StyleFlags 0, width
        /// BoxHover.LineWidth, roundness (1, 1). Nothing is filled.
        /// </summary>
        private void Draw(Target t, List<(V3 A, V3 B)> segs, List<V3> ring)
        {
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
            // JR-T-014c (G10): the arrow from the box mouth on the kerb to the building access, same colour and line path
            if (t.Kind == Kind.Exit && ring != null && t.Exit.HasInner)
            {
                float3 p = t.Exit.Inner;
                AccessArrow arrow = AccessArrow.Build(ring, new V3(p.x, p.y, p.z), t.Exit.Way, t.Fallback);
                if (arrow == null) return;
                foreach (var (a, b) in arrow.Segments)
                {
                    var line = new Line3.Segment(new float3((float)a.X, (float)a.Y, (float)a.Z), new float3((float)b.X, (float)b.Y, (float)b.Z));
                    buffer.DrawLine(c, c, 0f, (OverlayRenderSystem.StyleFlags)0, line, BoxHover.LineWidth, new float2(1f, 1f));
                }
            }
        }

        /// <summary>
        /// The hovered target's box outline (solved by Boxes/BoxShapes.cs, the paint's shape code), cached per target,
        /// box state and shape parameters (Core/HoverCache.cs); re-solved when a source entity is Updated or gone.
        /// </summary>
        private BoxTargeting.SolvedShape Outline(Target t)
        {
            // review F5: no box state in the key (colour is picked at draw time); exits carry their key and current road + t
            long accessEdge = t.Kind == Kind.Exit && t.Access.Edge != Entity.Null ? ((long)t.Access.Edge.Index << 32) | (uint)t.Access.Edge.Version : 0;
            int accessT = t.Kind == Kind.Exit ? HoverKey.QuantiseT(t.Access.T) : 0;
            var key = new HoverKey(((long)t.Entity.Index << 32) | (uint)t.Entity.Version, (int)t.Kind, BoxStore.KerbInset, BoxStore.EntranceLength, BoxStore.MarkingMargin, ExitSub(t), accessEdge, accessT);
            key.GroupsVersion = MergedBoxes.Version;   // 0.4.4: the drawn boxes changed = the merged hover changes
            return m_Cache.Get(key, SourcesChanged(m_Cache.Value, t.Entity), () => m_Targeting.Solve(t));
        }

        /// <summary>F5: a source of the cached shape (or the target) was rebuilt this frame (Updated) or is gone.</summary>
        private bool SourcesChanged(BoxTargeting.SolvedShape cached, Entity target)
        {
            if (cached == null) return true;
            EntityManager em = EntityManager;
            if (em.Exists(target) && em.HasComponent<Updated>(target)) return true;
            foreach (Entity e in cached.Sources)
                if (!em.Exists(e) || em.HasComponent<Updated>(e) || em.HasComponent<Deleted>(e)) return true;
            return false;
        }

        /// <summary>
        /// The click: the shared write path (Boxes/BoxTargeting.Apply, Core/BoxCommand.Decide with BoxRequest.Toggle), the
        /// same code the API's Add / Remove run. An added junction box logs the hover's solved shape (cached: no second solve).
        /// </summary>
        private void Toggle(Target t)
        {
            string shape = m_Cache.Value != null && m_Cache.Value.Target == t.Entity ? m_Cache.Value.Summary : null;
            m_Targeting.Apply(t, BoxRequest.Toggle, "yellow box tool", shape);
        }
    }
}
