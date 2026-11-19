using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using JunctionRules.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace JunctionRules.Boxes
{
    /// <summary>
    /// 0.4.13-probe (JR-T-019): the ONE selection + shape solve + apply path of the yellow boxes, shared by the Ctrl+Y tool
    /// (Boxes/YellowBoxToolSystem.cs: hover pick, outline, click) and JunctionRules.Api.JunctionRulesApi (List / Preview /
    /// Add / Remove). Moved here from YellowBoxToolSystem unchanged (Resolve, JunctionTarget, the building exit picks,
    /// ExitTarget, What, the outline solve, the no-shape rule F3 and the click's writes), so the tool and the API cannot
    /// drift: the refusal reasons, the pick rules and the BoxStore writes are the same code. The decision of what a call does
    /// is Core/BoxCommand.Decide (unit-tested). Main thread only (EntityManager reads / writes, BoxStore).
    /// One instance per world, owned by the tool system (YellowBoxToolSystem.Targeting); the API borrows it.
    /// </summary>
    internal sealed class BoxTargeting
    {
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
            /// <summary>JR-T-014c (G10): the target is an entrance (JR-T-021: its own saved key) / a saved lot-front fallback (remove only).</summary>
            public bool Entrance, Fallback;
        }

        /// <summary>JR-T-014c: one building's accesses (BoxFacts.Accesses), cached with the exit lists.</summary>
        internal sealed class AccessList
        {
            public List<BoxFacts.Exit> Exits, Entrances;
            public BoxFacts.Exit FallbackExit;
            public bool HasFallback;
            /// <summary>Exits then entrances: every real access (G10 targets). The API's exit index counts this order.</summary>
            public IEnumerable<BoxFacts.Exit> All()
            {
                foreach (var x in Exits) yield return x;
                foreach (var x in Entrances) yield return x;
            }
        }

        /// <summary>A target's solved box (the tool's hover outline; the API's outline / group).</summary>
        internal sealed class SolvedShape
        {
            public List<(V3 A, V3 B)> Segments;
            public string Failure;
            public List<Entity> Sources;
            /// <summary>0.4.7 (G5): the solver's notes (footways, arms yield / priority, conflict points, shifts), logged with a no-shape refusal.</summary>
            public string Notes;
            /// <summary>0.4.8 (JR-T-014b): junction only: the solved shape in one line (BoxShapes.Shape.Summary), logged when a box is added.</summary>
            public string Summary;
            /// <summary>0.4.8 review (G5 F6): the entity this shape was solved for (the click logs its Summary only for the same target).</summary>
            public Entity Target;
            /// <summary>JR-T-014c (G10): the target's own box outline (the arrow starts on it); null for a junction.</summary>
            public List<V3> Ring;
            /// <summary>0.4.13: the target's own box outline (junction or exit; null = no shape).</summary>
            public List<V3> Own;
            /// <summary>0.4.13: the merged group rings the hover draws (the group the target has / would join).</summary>
            public List<List<V3>> GroupRings = new List<List<V3>>();
            /// <summary>0.4.13: the other drawn boxes in that group.</summary>
            public List<BoxMergeInput> GroupMates = new List<BoxMergeInput>();
            public List<string> NoteList = new List<string>();
        }

        /// <summary>What an Apply did: the decision (Step None = refused, Error / Reason say why) and whether the world changed.</summary>
        internal struct Outcome
        {
            public BoxDecision Decision;
            public bool Changed;
            public string Error => Decision.Error;
            public string Reason => Decision.Reason;
        }

        private readonly World m_World;
        private readonly Game.UI.NameSystem m_Names;
        private readonly PrefabSystem m_Prefabs;

        // exits per building, cleared every ExitListFrames tool frames (buildings grow / move their drives) and after every write
        private readonly Dictionary<Entity, AccessList> m_ExitCache = new Dictionary<Entity, AccessList>();
        private int m_ExitCacheAge;
        internal const int ExitListFrames = 30;

        internal BoxTargeting(World world)
        {
            m_World = world;
            m_Names = world.GetOrCreateSystemManaged<Game.UI.NameSystem>();
            m_Prefabs = world.GetOrCreateSystemManaged<PrefabSystem>();
        }

        private EntityManager EntityManager => m_World.EntityManager;

        internal bool Alive => m_World != null && m_World.IsCreated;

        /// <summary>The tool's per-frame cache ageing.</summary>
        internal void TickCache()
        {
            if (++m_ExitCacheAge >= ExitListFrames) { m_ExitCacheAge = 0; m_ExitCache.Clear(); }
        }

        internal void ClearCache() => m_ExitCache.Clear();

        internal static BoxTargetKind KindOf(Target t) => t.Kind == Kind.Junction ? BoxTargetKind.Junction : t.Kind == Kind.Exit ? BoxTargetKind.Exit : BoxTargetKind.None;

        // ------------------------------------------------------------------ what the cursor (or an API point) is on

        /// <summary>The tool's hover pick: the raycast hit entity and position to a target.</summary>
        internal Target Resolve(Entity hitEntity, float3 hitPos)
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
                    if (MathUtils.Intersect(b.xz, hitPos.xz)) return JunctionTarget(n);
                }
                return NearestExitOnRoad(hitEntity, hitPos);
            }
            if (em.HasComponent<Building>(hitEntity) || em.HasComponent<Owner>(hitEntity))
            {
                Entity b = BoxFacts.MainBuilding(em, hitEntity);
                if (em.HasComponent<Building>(b)) return NearestExitOfBuilding(b, hitPos);
            }
            return default;
        }

        /// <summary>
        /// 0.4.13 (API {"x","z"}): stands in for the tool's raycast (which needs the cursor): what a ray straight down at
        /// (x, z) would hit with the tool's masks (road nets, static objects), handed to the same Resolve. Order: a road edge
        /// whose carriageway (centre curve +- half the composition width) holds the point, else a road node whose area
        /// holds it, else a building whose prefab bounds (rotated) hold it. False when nothing is there.
        /// </summary>
        internal bool PointHit(float2 xz, out Entity hit, out float3 pos)
        {
            EntityManager em = EntityManager;
            hit = Entity.Null;
            pos = new float3(xz.x, 0f, xz.y);
            float best = float.MaxValue;
            using (EntityQuery q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<EdgeGeometry>(), ComponentType.ReadOnly<Game.Net.Road>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() },
            }))
            using (NativeArray<Entity> edges = q.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < edges.Length; i++)
                {
                    Entity e = edges[i];
                    Bounds3 b = em.GetComponentData<EdgeGeometry>(e).m_Bounds;
                    if (!MathUtils.Intersect(b.xz, xz)) continue;
                    if (BoxFacts.EdgeOffGround(em, e)) continue;   // a ray from above hits the bridge deck; the tool refuses those anyway
                    Bezier4x3 c = em.GetComponentData<Curve>(e).m_Bezier;
                    float d = MathUtils.Distance(c.xz, xz, out float t);
                    float half = 0f;
                    if (em.HasComponent<Composition>(e))
                    {
                        Entity comp = em.GetComponentData<Composition>(e).m_Edge;
                        if (em.HasComponent<NetCompositionData>(comp)) half = em.GetComponentData<NetCompositionData>(comp).m_Width * 0.5f;
                    }
                    if (d <= half && d < best) { best = d; hit = e; pos = MathUtils.Position(c, t); pos.xz = xz; }
                }
            }
            if (hit != Entity.Null) return true;
            using (EntityQuery q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Net.Node>(), ComponentType.ReadOnly<NodeGeometry>(), ComponentType.ReadOnly<Game.Net.Road>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() },
            }))
            using (NativeArray<Entity> nodes = q.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < nodes.Length; i++)
                {
                    Bounds3 b = em.GetComponentData<NodeGeometry>(nodes[i]).m_Bounds;
                    if (!MathUtils.Intersect(b.xz, xz)) continue;
                    float d = math.distance(em.GetComponentData<Game.Net.Node>(nodes[i]).m_Position.xz, xz);
                    if (d < best) { best = d; hit = nodes[i]; }
                }
            }
            if (hit != Entity.Null) return true;
            using (EntityQuery q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<Game.Objects.Transform>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() },
            }))
            using (NativeArray<Entity> buildings = q.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < buildings.Length; i++)
                {
                    Entity prefab = em.GetComponentData<PrefabRef>(buildings[i]).m_Prefab;
                    if (!em.HasComponent<ObjectGeometryData>(prefab)) continue;
                    Bounds3 lb = em.GetComponentData<ObjectGeometryData>(prefab).m_Bounds;
                    Game.Objects.Transform tr = em.GetComponentData<Game.Objects.Transform>(buildings[i]);
                    float3 local = math.mul(math.inverse(tr.m_Rotation), new float3(xz.x, tr.m_Position.y, xz.y) - tr.m_Position);
                    if (local.x < lb.min.x || local.x > lb.max.x || local.z < lb.min.z || local.z > lb.max.z) continue;
                    float d = math.distance(tr.m_Position.xz, xz);
                    if (d < best) { best = d; hit = buildings[i]; pos = new float3(xz.x, tr.m_Position.y, xz.y); }
                }
            }
            return hit != Entity.Null;
        }

        internal Target JunctionTarget(Entity node)
        {
            EntityManager em = EntityManager;
            var t = new Target { Kind = Kind.Junction, Entity = node };
            t.Valid = BoxSelection.JunctionOk(BoxFacts.Junction(em, node), out t.Why);
            t.HasBox = em.HasComponent<YellowBoxJunction>(node);
            if (em.HasComponent<Game.Net.Node>(node)) t.Position = em.GetComponentData<Game.Net.Node>(node).m_Position;
            return t;
        }

        internal AccessList ExitsOf(Entity building)
        {
            if (m_ExitCache.TryGetValue(building, out var list)) return list;
            BoxStore.MigrateEntrances(EntityManager, building);   // JR-T-021: a 1.0.1 record's derived entrances become saved keys first
            list = new AccessList();
            BoxFacts.Accesses(EntityManager, building, null, out list.Exits, out list.Entrances, out list.FallbackExit, out list.HasFallback);
            m_ExitCache[building] = list;
            return list;
        }

        /// <summary>The buildings that may have an exit on this road: its ConnectedBuilding entries and the owners of its ConnectedNode sub-net nodes.</summary>
        internal Target NearestExitOnRoad(Entity edge, float3 hitPos)
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
        internal Target NearestExitOfBuilding(Entity building, float3 hitPos)
        {
            AccessList acc = ExitsOf(building);
            var exits = new List<BoxFacts.Exit>(acc.All());
            if (exits.Count == 0) return NoAccessTarget(building, hitPos);
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < exits.Count; i++)
            {
                float d = math.distance(new float2((float)exits[i].C.World.X, (float)exits[i].C.World.Z), hitPos.xz);
                if (d < bestD) { bestD = d; best = i; }
            }
            return AccessTarget(building, exits[best]);
        }

        /// <summary>
        /// JR-T-014c (G10): a building with no real access is not selectable; a saved lot-front fallback key there is offered
        /// for removal (the tool's rule, also the API's {"building":I} on such a building).
        /// </summary>
        internal Target NoAccessTarget(Entity building, float3 at)
        {
            AccessList acc = ExitsOf(building);
            AccessRules.Targets(null, null, out string why);
            if (acc.HasFallback && HasFallbackKey(building))
            {
                Target f = ExitTarget(building, acc.FallbackExit);
                f.Valid = false;
                f.Why = why;
                return f;
            }
            return new Target { Kind = Kind.Exit, Entity = building, Why = why, Position = at };
        }

        /// <summary>One access of a building as a target: a junction-kind exit is that junction node (from the building).</summary>
        internal Target AccessTarget(Entity building, BoxFacts.Exit x)
        {
            if (x.IsJunction)
            {
                Target j = JunctionTarget(x.Node);
                j.FromBuilding = building;
                return j;
            }
            return ExitTarget(building, x);
        }

        internal Target ExitTarget(Entity building, BoxFacts.Exit exit)
        {
            EntityManager em = EntityManager;
            var t = new Target { Kind = Kind.Exit, Entity = building, Exit = exit, Entrance = exit.IsEntrance, Fallback = exit.IsFallback };
            t.Valid = BoxSelection.ExitOk(BoxFacts.ExitFacts(em, building, exit, out BoxFacts.Access a), out t.Why);
            // JR-T-021: an entrance has a box when its own key is saved (as an exit); a fallback when a Fallback key is saved
            t.HasBox = exit.IsFallback ? HasFallbackKey(building) : BoxStore.HasExit(em, building, exit.C.Key);
            t.Access = a;
            if (a.Valid) t.Position = a.Point;
            else if (em.HasComponent<Game.Objects.Transform>(building)) t.Position = em.GetComponentData<Game.Objects.Transform>(building).m_Position;
            return t;
        }

        /// <summary>JR-T-014c (G10): the building's component holds a lot-front Fallback key (marker / old fallback box).</summary>
        internal bool HasFallbackKey(Entity building)
        {
            EntityManager em = EntityManager;
            if (!BoxStore.Has(em, building, false)) return false;
            YellowBoxEntrance c = em.GetComponentData<YellowBoxEntrance>(building);
            for (int i = 0; i < c.m_Exits.Length; i++) if (c.m_Exits[i].ExitKind == ExitKind.Fallback) return true;
            return false;
        }

        /// <summary>The tool's role word for an access (log and API).</summary>
        internal static string Role(Target t) => t.Entrance ? "entrance" : t.Fallback ? "lot-front fallback" : (t.Exit.Way == AccessWay.Both ? "two-way access" : "exit");

        /// <summary>
        /// The target for log lines. 0.4.7 (JR-T-013c, G5): with its position and what meets there (junction: every road
        /// arm's street name, prefab and width, numbered as the shape failure numbers them; exit: its road), so a refusal
        /// can be found again without the game (0.4.6 logged only "node Entity(551713:1)").
        /// </summary>
        internal string What(Target t)
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
            return $"building {t.Entity} {Role(t)} {t.Exit.C.Key} [{t.Exit.C.Kind}] {BoxSelection.DescribeExit(pos, rl)}";
        }

        // ------------------------------------------------------------------ the box shape

        /// <summary>0.4.3: which exit of the building (its saved key packed in 40 bits); 0 for a junction.</summary>
        internal static long ExitSub(Target t) => t.Kind != Kind.Exit ? 0 : BoxMergeInput.ExitSub(t.Exit.C.Key);

        /// <summary>
        /// The target's box (solved by Boxes/BoxShapes.cs, the paint's shape code) and the merged group it has / would join
        /// (Core MergedBoxes.HoverGroup: the hover draws those rings). The tool caches it per target (Core/HoverCache.cs).
        /// </summary>
        internal SolvedShape Solve(Target t)
        {
            EntityManager em = EntityManager;
            V2? side = null;
            if (t.Kind == Kind.Exit) { BoxFacts.GetFrame(em, t.Entity, out V2 bpos, out _); side = bpos; }   // 0.4.4: building side of a median
            BoxShapes.Shape shape = t.Kind == Kind.Junction
                ? BoxShapes.Junction(em, t.Entity, BoxStore.KerbInset, BoxStore.MarkingMargin)
                : BoxShapes.Entrance(em, t.Access, BoxStore.KerbInset, BoxStore.EntranceLength, BoxStore.MarkingMargin, side);
            var sources = new List<Entity>(shape.Sources) { t.Entity };
            if (shape.Outline == null)
                return new SolvedShape { Segments = BoxHover.Segments(null), Failure = shape.Failure, Sources = sources, Notes = string.Join("; ", shape.Notes), NoteList = new List<string>(shape.Notes) };
            // 0.4.4 (JR-T-011): the merged outline of the group the target belongs to (has a box) or would join (adding)
            var self = new BoxMergeInput(BoxMergeInput.EntityKey(t.Entity.Index, t.Entity.Version), ExitSub(t), t.Kind == Kind.Exit ? BoxMergeKind.Exit : BoxMergeKind.Junction,
                shape.Outline, t.Kind == Kind.Exit && t.Access.Edge != Entity.Null ? SortedKeys.KeyOf(t.Access.Edge.Index, t.Access.Edge.Version) : 0, shape.Carriageways);
            List<List<V3>> rings = MergedBoxes.HoverGroup(self, out List<BoxMergeInput> mates);
            var segs = new List<(V3 A, V3 B)>();
            foreach (List<V3> ring in rings) segs.AddRange(BoxHover.Segments(ring));
            return new SolvedShape
            {
                Segments = segs, Failure = null, Sources = sources, Notes = string.Join("; ", shape.Notes), Summary = shape.Summary, Target = t.Entity,
                Ring = t.Kind == Kind.Exit ? shape.Outline : null, Own = shape.Outline, GroupRings = rings, GroupMates = mates, NoteList = new List<string>(shape.Notes),
            };
        }

        /// <summary>
        /// F3 (the tool's rule, now shared): no shape and no box yet = not selectable (a click would save an invisible box);
        /// a target that HAS a box stays removable even when its shape fails now. True when the target became NoShape.
        /// </summary>
        internal static bool ApplyShapeRule(ref Target t, SolvedShape s)
        {
            if (s == null || s.Segments.Count != 0 || t.HasBox) return false;
            t.Valid = false;
            t.NoShape = true;
            t.Why = BoxCommand.NoShapePrefix + s.Failure;
            return true;
        }

        // ------------------------------------------------------------------ the write (tool click and API add / remove)

        /// <summary>
        /// THE write path: the tool's click (BoxRequest.Toggle) and the API's Add / Remove. Core/BoxCommand.Decide says what
        /// to do; the writes are BoxStore's (immediate EntityManager changes; BoxStore.Changed makes the paint redraw on its
        /// next update). Logs as 'who' ("yellow box tool" / "yellow box API") with the tool's texts. 'shapeSummary' = the
        /// solved junction shape line for an added junction box (null: none).
        /// </summary>
        internal Outcome Apply(Target t, BoxRequest req, string who, string shapeSummary)
        {
            EntityManager em = EntityManager;
            var o = new Outcome { Decision = BoxCommand.Decide(req, KindOf(t), t.HasBox, t.Valid, t.Why, t.Fallback) };
            BoxDecision d = o.Decision;
            if (d.Step == BoxStep.None)
            {
                if (req == BoxRequest.Toggle) Mod.log.Info($"{who}: {What(t)}: not selectable ({t.Why}); nothing changed");
                else Mod.log.Info($"{who}: {What(t)}: {d.Error} ({d.Reason}); nothing changed");
                return o;
            }
            bool add = d.Add;
            switch (d.Step)
            {
                case BoxStep.AddJunction: BoxStore.Add(em, t.Entity); break;
                case BoxStep.RemoveJunction: BoxStore.Remove(em, t.Entity, true); break;
                case BoxStep.RemoveFallbackKeys:
                {
                    YellowBoxEntrance c = em.GetComponentData<YellowBoxEntrance>(t.Entity);
                    var keys = new List<ExitKey>();
                    for (int i = 0; i < c.m_Exits.Length; i++) if (c.m_Exits[i].ExitKind == ExitKind.Fallback) keys.Add(c.m_Exits[i]);
                    foreach (ExitKey k in keys) BoxStore.RemoveExit(em, t.Entity, k);
                    break;
                }
                case BoxStep.AddExit:
                    if (!BoxStore.AddExit(em, t.Entity, t.Exit.C.Key))
                    {
                        Mod.log.Info($"{who}: {What(t)}: {BoxCommand.FullReason}; nothing changed");
                        o.Decision = new BoxDecision { Step = BoxStep.None, Error = BoxApiErrors.NotBoxable, Reason = BoxCommand.FullReason };
                        return o;
                    }
                    break;
                case BoxStep.RemoveExit: BoxStore.RemoveExit(em, t.Entity, t.Exit.C.Key); break;
            }
            o.Changed = true;
            m_ExitCache.Clear();
            string where = t.Kind == Kind.Junction ? "junction " + What(t) : $"{What(t)} (source: {t.Exit.C.Source})";
            // 0.4.8 (JR-T-014b): an added junction box names its solved shape
            string shape = add && t.Kind == Kind.Junction ? shapeSummary : null;
            // Review G5 F2: a box that leaves conflict points outside is logged as a warning (the owner sees it in the log).
            if (shape != null && shape.Contains("WARNING")) Mod.log.Warn($"{who}: box added on {where} at {t.Position}; {shape}");
            else Mod.log.Info($"{who}: {(add ? "box added on" : "box removed from")} {where} at {t.Position}" + (!add && !t.Valid ? " (target no longer valid: " + t.Why + ")" : "") + (shape != null ? "; " + shape : ""));
            return o;
        }
    }
}
