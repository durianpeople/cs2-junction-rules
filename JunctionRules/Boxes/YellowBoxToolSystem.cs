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
    /// </summary>
    public partial class YellowBoxToolSystem : ToolBaseSystem
    {
        public const string kToolID = "JunctionRules.YellowBoxTool";

        public override string toolID => kToolID;

        private OverlayRenderSystem m_Overlay;
        private Game.UI.NameSystem m_Names;
        private PrefabSystem m_Prefabs;
        private Target m_Hover;
        private string m_LastWhy;

        // hover outline cache (JR-T-007b-r F5, Core/HoverCache.cs): solved once per hovered target; again only when the
        // target, its box state or the shape parameters change, or a source entity of the shape is Updated / gone.
        private sealed class HoverShape
        {
            public List<(V3 A, V3 B)> Segments;
            public string Failure;
            public List<Entity> Sources;
            /// <summary>0.4.7 (G5): the solver's notes (footways, arms yield / priority, conflict points, shifts), logged with a no-shape refusal.</summary>
            public string Notes;
            /// <summary>JR-T-014c (G10): the target's own box outline (the arrow starts on it); null for a junction.</summary>
            public List<V3> Ring;
        }
        private readonly HoverCache<HoverShape> m_Cache = new HoverCache<HoverShape>();
        // F3: targets already warned about having no box shape (once per entity per tool session)
        private readonly HashSet<(Entity, long)> m_NoShapeWarned = new HashSet<(Entity, long)>();
        // exit lists are re-read every kExitListFrames frames while the tool runs (buildings grow / move their drives)
        private const int kExitListFrames = 30;

        /// <summary>Junction = a node box; Exit = a box at one car exit of a building (0.4.0-0.4.2 "Entrance").</summary>
        internal enum Kind { None, Junction, Exit }

        internal struct Target
        {
            public Kind Kind;
            public Entity Entity;
            public bool Valid, HasBox;
            /// <summary>F3: valid by the selection rules, but no box shape can be built there (not selectable).</summary>
            public bool NoShape;
            public string Why;
            public float3 Position;
            public BoxFacts.Access Access;   // exit: the exit point on its road
            public BoxFacts.Exit Exit;       // exit: which exit (key = the saved name)
            /// <summary>Junction reached from a building (its own access road meets the public road here).</summary>
            public Entity FromBuilding;
            /// <summary>JR-T-014c (G9 / G10): the target is an entrance (derived box) / a saved lot-front fallback (remove only).</summary>
            public bool Entrance, Fallback;
        }

        /// <summary>JR-T-014c: one building's accesses (BoxFacts.Accesses), cached with the exit lists.</summary>
        private sealed class AccessList
        {
            public List<BoxFacts.Exit> Exits, Entrances;
            public BoxFacts.Exit FallbackExit;
            public bool HasFallback;
            /// <summary>Exits then entrances: every real access (G10 targets).</summary>
            public IEnumerable<BoxFacts.Exit> All()
            {
                foreach (var x in Exits) yield return x;
                foreach (var x in Entrances) yield return x;
            }
        }

        // exits per building, re-read every kExitListFrames frames (hovering a road reads the exits of the buildings along it)
        private readonly Dictionary<Entity, AccessList> m_ExitCache = new Dictionary<Entity, AccessList>();
        private int m_ExitCacheAge;

        public override PrefabBase GetPrefab() => null;

        public override bool TrySetPrefab(PrefabBase prefab) => false;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_Names = World.GetOrCreateSystemManaged<Game.UI.NameSystem>();
            m_Prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
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
                if (++m_ExitCacheAge >= kExitListFrames) { m_ExitCacheAge = 0; m_ExitCache.Clear(); }
                m_Hover = GetRaycastResult(out Entity hitEntity, out Game.Common.RaycastHit hit) ? Resolve(hitEntity, hit) : default;
                if (m_Hover.Kind != Kind.None && (m_Hover.Valid || m_Hover.HasBox))
                {
                    HoverShape h = Outline(m_Hover);
                    if (h.Segments.Count == 0 && !m_Hover.HasBox)
                    {
                        // F3: no shape and no box yet = not selectable (a click would save an invisible box); a target
                        // that HAS a box stays removable even when its shape fails now.
                        m_Hover.Valid = false;
                        m_Hover.NoShape = true;
                        m_Hover.Why = "no box shape can be built here: " + h.Failure;
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

        // ------------------------------------------------------------------ what the cursor is on

        private Target Resolve(Entity hitEntity, Game.Common.RaycastHit hit)
        {
            EntityManager em = EntityManager;
            if (em.HasComponent<Game.Net.Node>(hitEntity)) return JunctionTarget(hitEntity);
            if (em.HasComponent<Edge>(hitEntity))
            {
                // On the road: the end node when the hit lies inside that node's area, else the car exit on this road
                // nearest the hit (within half the exit box length + 2 m), of any building whose exit is on this road.
                Edge edge = em.GetComponentData<Edge>(hitEntity);
                foreach (Entity n in new[] { edge.m_Start, edge.m_End })
                {
                    if (!em.HasComponent<NodeGeometry>(n)) continue;
                    Bounds3 b = em.GetComponentData<NodeGeometry>(n).m_Bounds;
                    if (MathUtils.Intersect(b.xz, hit.m_HitPosition.xz)) return JunctionTarget(n);
                }
                return NearestExitOnRoad(hitEntity, hit.m_HitPosition);
            }
            if (em.HasComponent<Building>(hitEntity) || em.HasComponent<Owner>(hitEntity))
            {
                Entity b = BoxFacts.MainBuilding(em, hitEntity);
                if (em.HasComponent<Building>(b)) return NearestExitOfBuilding(b, hit.m_HitPosition);
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

        private AccessList ExitsOf(Entity building)
        {
            if (m_ExitCache.TryGetValue(building, out var list)) return list;
            list = new AccessList();
            BoxFacts.Accesses(EntityManager, building, null, out list.Exits, out list.Entrances, out list.FallbackExit, out list.HasFallback);
            m_ExitCache[building] = list;
            return list;
        }

        /// <summary>The buildings that may have an exit on this road: its ConnectedBuilding entries and the owners of its ConnectedNode sub-net nodes.</summary>
        private Target NearestExitOnRoad(Entity edge, float3 hitPos)
        {
            EntityManager em = EntityManager;
            var buildings = new HashSet<Entity>();
            if (em.HasBuffer<ConnectedBuilding>(edge))
            {
                DynamicBuffer<ConnectedBuilding> cb = em.GetBuffer<ConnectedBuilding>(edge, true);
                for (int i = 0; i < cb.Length; i++) if (em.Exists(cb[i].m_Building) && em.HasComponent<Building>(cb[i].m_Building)) buildings.Add(BoxFacts.MainBuilding(em, cb[i].m_Building));
            }
            if (em.HasBuffer<ConnectedNode>(edge))
            {
                DynamicBuffer<ConnectedNode> cn = em.GetBuffer<ConnectedNode>(edge, true);
                for (int i = 0; i < cn.Length; i++)
                {
                    Entity ob = BoxFacts.OwnerBuilding(em, cn[i].m_Node);
                    if (ob != Entity.Null) buildings.Add(ob);
                }
            }
            float reach = (float)(BoxStore.EntranceLength * 0.5 + 2.0);
            Entity best = Entity.Null;
            BoxFacts.Exit bestExit = default;
            float bestD = float.MaxValue;
            foreach (Entity b in buildings)
            {
                foreach (BoxFacts.Exit x in ExitsOf(b).All())
                {
                    if (x.IsJunction || x.Road != edge) continue;
                    float d = math.distance(new float2((float)x.C.World.X, (float)x.C.World.Z), hitPos.xz);
                    if (d < bestD) { bestD = d; best = b; bestExit = x; }
                }
            }
            return best != Entity.Null && bestD <= reach ? ExitTarget(best, bestExit) : default;
        }

        /// <summary>Pointing at a building: its exit nearest the cursor (several exits = several targets, owner ruling 2).</summary>
        private Target NearestExitOfBuilding(Entity building, float3 hitPos)
        {
            AccessList acc = ExitsOf(building);
            var exits = new List<BoxFacts.Exit>(acc.All());
            if (exits.Count == 0)
            {
                // JR-T-014c (G10): no real access = not selectable; a saved lot-front fallback key there is offered for removal
                AccessRules.Targets(null, null, out string why);
                if (acc.HasFallback && HasFallbackKey(building))
                {
                    Target f = ExitTarget(building, acc.FallbackExit);
                    f.Valid = false;
                    f.Why = why;
                    return f;
                }
                return new Target { Kind = Kind.Exit, Entity = building, Why = why, Position = hitPos };
            }
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < exits.Count; i++)
            {
                float d = math.distance(new float2((float)exits[i].C.World.X, (float)exits[i].C.World.Z), hitPos.xz);
                if (d < bestD) { bestD = d; best = i; }
            }
            BoxFacts.Exit x = exits[best];
            if (x.IsJunction)
            {
                Target j = JunctionTarget(x.Node);
                j.FromBuilding = building;
                return j;
            }
            return ExitTarget(building, x);
        }

        private Target ExitTarget(Entity building, BoxFacts.Exit exit)
        {
            EntityManager em = EntityManager;
            var t = new Target { Kind = Kind.Exit, Entity = building, Exit = exit, Entrance = exit.IsEntrance, Fallback = exit.IsFallback };
            t.Valid = BoxSelection.ExitOk(BoxFacts.ExitFacts(em, building, exit, out BoxFacts.Access a), out t.Why);
            // JR-T-014c (G9): an entrance is boxed with its building (any saved key); a fallback has a box when a Fallback key is saved
            t.HasBox = exit.IsEntrance ? BoxStore.Has(em, building, false) : exit.IsFallback ? HasFallbackKey(building) : BoxStore.HasExit(em, building, exit.C.Key);
            t.Access = a;
            if (a.Valid) t.Position = a.Point;
            else if (em.HasComponent<Game.Objects.Transform>(building)) t.Position = em.GetComponentData<Game.Objects.Transform>(building).m_Position;
            return t;
        }

        /// <summary>JR-T-014c (G10): the building's component holds a lot-front Fallback key (marker / old fallback box).</summary>
        private bool HasFallbackKey(Entity building)
        {
            EntityManager em = EntityManager;
            if (!BoxStore.Has(em, building, false)) return false;
            YellowBoxEntrance c = em.GetComponentData<YellowBoxEntrance>(building);
            for (int i = 0; i < c.m_Exits.Length; i++) if (c.m_Exits[i].ExitKind == ExitKind.Fallback) return true;
            return false;
        }

        /// <summary>
        /// The target for log lines. 0.4.7 (JR-T-013c, G5): with its position and what meets there (junction: every road
        /// arm's street name, prefab and width, numbered as the shape failure numbers them; exit: its road), so a refusal
        /// can be found again without the game (0.4.6 logged only "node Entity(551713:1)").
        /// </summary>
        private string What(Target t)
        {
            EntityManager em = EntityManager;
            V3 pos = new V3(t.Position.x, t.Position.y, t.Position.z);
            if (t.Kind == Kind.Junction)
            {
                var labels = new List<ArmLabel>();
                foreach (Entity e in BoxShapes.ArmEdges(em, t.Entity)) labels.Add(BoxShapes.Label(em, e, m_Names, m_Prefabs));
                string where = BoxSelection.Describe(pos, labels);
                return t.FromBuilding != Entity.Null ? $"node {t.Entity} {where} (where building {t.FromBuilding}'s own road meets the public road)" : $"node {t.Entity} {where}";
            }
            Entity road = t.Access.Edge;
            ArmLabel? rl = road != Entity.Null && em.Exists(road) ? BoxShapes.Label(em, road, m_Names, m_Prefabs) : (ArmLabel?)null;
            string role = t.Entrance ? "entrance" : t.Fallback ? "lot-front fallback" : (t.Exit.Way == AccessWay.Both ? "two-way access" : "exit");
            return $"building {t.Entity} {role} {t.Exit.C.Key} [{t.Exit.C.Kind}] {BoxSelection.DescribeExit(pos, rl)}";
        }

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
        private HoverShape Outline(Target t)
        {
            // review F5: no box state in the key (colour is picked at draw time); exits carry their key and current road + t
            long accessEdge = t.Kind == Kind.Exit && t.Access.Edge != Entity.Null ? ((long)t.Access.Edge.Index << 32) | (uint)t.Access.Edge.Version : 0;
            int accessT = t.Kind == Kind.Exit ? HoverKey.QuantiseT(t.Access.T) : 0;
            var key = new HoverKey(((long)t.Entity.Index << 32) | (uint)t.Entity.Version, (int)t.Kind, BoxStore.KerbInset, BoxStore.EntranceLength, BoxStore.MarkingMargin, ExitSub(t), accessEdge, accessT);
            key.GroupsVersion = MergedBoxes.Version;   // 0.4.4: the drawn boxes changed = the merged hover changes
            return m_Cache.Get(key, SourcesChanged(m_Cache.Value, t.Entity), () =>
            {
                V2? side = null;
                if (t.Kind == Kind.Exit) { BoxFacts.GetFrame(EntityManager, t.Entity, out V2 bpos, out _); side = bpos; }   // 0.4.4: building side of a median
                BoxShapes.Shape shape = t.Kind == Kind.Junction
                    ? BoxShapes.Junction(EntityManager, t.Entity, BoxStore.KerbInset, BoxStore.MarkingMargin)
                    : BoxShapes.Entrance(EntityManager, t.Access, BoxStore.KerbInset, BoxStore.EntranceLength, BoxStore.MarkingMargin, side);
                var sources = new List<Entity>(shape.Sources) { t.Entity };
                if (shape.Outline == null) return new HoverShape { Segments = BoxHover.Segments(null), Failure = shape.Failure, Sources = sources, Notes = string.Join("; ", shape.Notes) };
                // 0.4.4 (JR-T-011): the merged outline of the group the target belongs to (has a box) or would join (adding)
                var self = new BoxMergeInput(BoxMergeInput.EntityKey(t.Entity.Index, t.Entity.Version), ExitSub(t), t.Kind == Kind.Exit ? BoxMergeKind.Exit : BoxMergeKind.Junction,
                    shape.Outline, t.Kind == Kind.Exit && t.Access.Edge != Entity.Null ? SortedKeys.KeyOf(t.Access.Edge.Index, t.Access.Edge.Version) : 0, shape.Carriageways);
                var segs = new List<(V3 A, V3 B)>();
                foreach (List<V3> ring in MergedBoxes.HoverRings(self)) segs.AddRange(BoxHover.Segments(ring));
                return new HoverShape { Segments = segs, Failure = null, Sources = sources, Notes = string.Join("; ", shape.Notes), Ring = t.Kind == Kind.Exit ? shape.Outline : null };
            });
        }

        /// <summary>0.4.3: which exit of the building (its saved key packed in 40 bits); 0 for a junction.</summary>
        private static long ExitSub(Target t) => t.Kind != Kind.Exit ? 0 : BoxMergeInput.ExitSub(t.Exit.C.Key);

        /// <summary>F5: a source of the cached shape (or the target) was rebuilt this frame (Updated) or is gone.</summary>
        private bool SourcesChanged(HoverShape cached, Entity target)
        {
            if (cached == null) return true;
            EntityManager em = EntityManager;
            if (em.Exists(target) && em.HasComponent<Updated>(target)) return true;
            foreach (Entity e in cached.Sources)
                if (!em.Exists(e) || em.HasComponent<Updated>(e) || em.HasComponent<Deleted>(e)) return true;
            return false;
        }

        private void Toggle(Target t)
        {
            EntityManager em = EntityManager;
            ToggleAction act = BoxSelection.Toggle(t.HasBox, t.Valid);
            if (act == ToggleAction.None)
            {
                Mod.log.Info($"yellow box tool: {What(t)}: not selectable ({t.Why}); nothing changed");
                return;
            }
            bool add = act == ToggleAction.Add;
            if (t.Kind == Kind.Junction)
            {
                if (add) BoxStore.Add(em, t.Entity);
                else BoxStore.Remove(em, t.Entity, true);
            }
            else if (t.Entrance || t.Fallback)
            {
                // JR-T-014c (G9 / G10): entrances come with the building (derived); a fallback is a marker only
                if (add)
                {
                    // not boxed yet (else HasBox): save the lot-front marker key, an existing record kind, never drawn
                    AccessList acc = ExitsOf(t.Entity);
                    if (!acc.HasFallback || !BoxStore.AddExit(em, t.Entity, acc.FallbackExit.C.Key))
                    {
                        Mod.log.Info($"yellow box tool: {What(t)}: the building has no road edge for its marker key; nothing changed");
                        return;
                    }
                }
                else if (t.Entrance) BoxStore.Remove(em, t.Entity, false);   // all the building's boxes (its entrances are boxed with it)
                else
                {
                    YellowBoxEntrance c = em.GetComponentData<YellowBoxEntrance>(t.Entity);
                    var keys = new List<ExitKey>();
                    for (int i = 0; i < c.m_Exits.Length; i++) if (c.m_Exits[i].ExitKind == ExitKind.Fallback) keys.Add(c.m_Exits[i]);
                    foreach (ExitKey k in keys) BoxStore.RemoveExit(em, t.Entity, k);
                }
            }
            else if (add)
            {
                if (!BoxStore.AddExit(em, t.Entity, t.Exit.C.Key))
                {
                    Mod.log.Info($"yellow box tool: {What(t)}: the building already has {ExitRules.MaxBoxesPerBuilding} exit boxes; nothing changed");
                    return;
                }
            }
            else BoxStore.RemoveExit(em, t.Entity, t.Exit.C.Key);
            m_ExitCache.Clear();
            string where = t.Kind == Kind.Junction ? "junction " + What(t) : $"{What(t)} (source: {t.Exit.C.Source})"
                + (t.Entrance ? (add ? "; the building's entrances are boxed with it" : "; removed ALL the building's boxes (entrances come with the building)") : "");
            Mod.log.Info($"yellow box tool: {(add ? "box added on" : "box removed from")} {where} at {t.Position}" + (!add && !t.Valid ? " (target no longer valid: " + t.Why + ")" : ""));
        }
    }
}
