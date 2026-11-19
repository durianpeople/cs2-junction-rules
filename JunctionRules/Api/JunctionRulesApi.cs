using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Game.Buildings;
using Game.Common;
using Game.Tools;
using JunctionRules.Boxes;
using JunctionRules.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Target = JunctionRules.Boxes.BoxTargeting.Target;
using Kind = JunctionRules.Boxes.BoxTargeting.Kind;

namespace JunctionRules.Api
{
    /// <summary>
    /// 0.4.13-probe (JR-T-019): yellow box API v1 (YBOX_API_CONTRACT.md, Technical 17 Nov 2026) for the bridge / crew tools.
    /// The bridge finds this type by reflection (no compile-time reference): keep the type name, ApiVersion (const int
    /// field), ModVersion / IsReady (static properties) and List / Preview / Add / Remove (string -> string) as they are.
    /// JSON in, JSON out. Synchronous, Unity main thread only (the bridge's handlers run there). Never throws: any failure is
    /// {"ok":false,"error":"internal"}. Not ready (JR not loaded / disabled, no world, no tool, no city) = "not_ready".
    /// Same code as the Ctrl+Y tool: the target pick, the shape solve, the no-shape rule and the writes are
    /// Boxes/BoxTargeting.cs (the tool system's own instance), the decision Core/BoxCommand.Decide, the refusal texts the
    /// tool's. The writes are immediate EntityManager changes (BoxStore, as the tool's click): List shows them at once; the
    /// paint and the driving rule pick them up on their next update (one frame), when the merged outlines are republished.
    /// </summary>
    public static class JunctionRulesApi
    {
        public const int ApiVersion = BoxApi.ApiVersion;

        private static int s_MainThread;
        private static string s_Version;

        /// <summary>The mod's version, e.g. "0.4.13-probe" (assembly informational version without the source revision).</summary>
        public static string ModVersion
        {
            get
            {
                if (s_Version != null) return s_Version;
                string v = null;
                try { v = typeof(Mod).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion; }
                catch { }
                s_Version = BoxApi.CleanVersion(v);
                return s_Version;
            }
        }

        /// <summary>True when the API can answer: JR loaded and enabled, the game world and the yellow box tool exist, a city is loaded.</summary>
        public static bool IsReady
        {
            get
            {
                try { return NotReady() == null; }
                catch { return false; }
            }
        }

        /// <summary>Mod.OnLoad: remember the Unity main thread.</summary>
        internal static void MarkMainThread() => s_MainThread = Thread.CurrentThread.ManagedThreadId;

        public static string List(string filterJson) => Run("list", filterJson, ListCore);
        public static string Preview(string targetJson) => Run("preview", targetJson, s => TargetCall("preview", s));
        public static string Add(string targetJson) => Run("add", targetJson, s => TargetCall("add", s));
        public static string Remove(string targetJson) => Run("remove", targetJson, s => TargetCall("remove", s));

        // ------------------------------------------------------------------ plumbing

        private static string Run(string action, string input, Func<string, string> body)
        {
            try
            {
                if (s_MainThread != 0 && Thread.CurrentThread.ManagedThreadId != s_MainThread)
                    return BoxApi.Error(ModVersion, action, BoxApiErrors.Internal, "called off the main thread: call the API from the game's main thread only");
                string why = NotReady();
                if (why != null) return BoxApi.Error(ModVersion, action, BoxApiErrors.NotReady, why);
                return body(input);
            }
            catch (Exception e)
            {
                try { Mod.log.Error(e, $"yellow box API: {action} failed"); } catch { }
                try { return BoxApi.Error(ModVersion, action, BoxApiErrors.Internal, e.GetType().Name + ": " + e.Message); }
                catch { return "{\"ok\":false,\"apiVersion\":1,\"action\":\"" + action + "\",\"error\":\"internal\",\"reason\":\"result could not be written\"}"; }
            }
        }

        private static string NotReady()
        {
            World w = World.DefaultGameObjectInjectionWorld;
            bool worldOk = w != null && w.IsCreated;
            BoxTargeting t = worldOk ? Targeting(w) : null;
            return BoxApi.NotReadyReason(Mod.Settings != null, worldOk, t != null, YellowBoxUISystem.InCity);
        }

        private static BoxTargeting Targeting(World w)
        {
            YellowBoxToolSystem tool = w.GetExistingSystemManaged<YellowBoxToolSystem>();
            return tool?.Targeting;
        }

        private static BoxTargeting Targeting() => Targeting(World.DefaultGameObjectInjectionWorld);

        private static EntityManager Em => World.DefaultGameObjectInjectionWorld.EntityManager;

        // ------------------------------------------------------------------ preview / add / remove

        private static string TargetCall(string action, string json)
        {
            if (!BoxApi.ParseTarget(json, out ApiTarget at, out string bad)) return BoxApi.Error(ModVersion, action, BoxApiErrors.BadTarget, bad);
            BoxTargeting tg = Targeting();
            tg.ClearCache();   // the tool ages its exit lists per frame while it runs; an API call always reads them fresh
            string err = Pick(tg, at, action, out Target t, out string errJson);
            if (errJson != null) return errJson;
            if (err != null) return BoxApi.Error(ModVersion, action, BoxApiErrors.NotFound, err);

            // the tool solves a shape only for a valid or boxed target (its hover), then applies F3 (no shape = not selectable)
            BoxTargeting.SolvedShape shape = t.Valid || t.HasBox ? tg.Solve(t) : null;
            BoxTargeting.ApplyShapeRule(ref t, shape);

            var r = new ApiTargetResult { Label = tg.What(t) };
            FillRef(tg, t, r);
            if (shape != null)
            {
                r.Outline = shape.Own;
                r.GroupRings = shape.Own != null ? shape.GroupRings : new List<List<V3>>();
                foreach (BoxMergeInput m in shape.GroupMates) if (RefOf(tg, m, out ApiBoxRef mr)) r.MergedGroup.Add(mr);
                r.Notes.AddRange(shape.NoteList);
            }

            if (action == "preview")
            {
                BoxDecision d = BoxCommand.Decide(t.HasBox ? BoxRequest.Remove : BoxRequest.Add, BoxTargeting.KindOf(t), t.HasBox, t.Valid, t.Why, t.Fallback);
                r.Boxable = t.Valid;
                r.Boxed = t.HasBox;
                r.Changed = false;
                if (d.Step == BoxStep.None) { r.Would = "refuse"; r.Error = d.Error; r.Reason = d.Reason; }
                else r.Would = d.Add ? "add" : "remove";
                if (!t.Valid && r.Error == null && !string.IsNullOrEmpty(t.Why)) r.Notes.Add("target no longer valid (" + t.Why + "): the box can be removed, not added");
                return BoxApi.TargetResult(ModVersion, action, r);
            }

            BoxRequest req = action == "add" ? BoxRequest.Add : BoxRequest.Remove;
            BoxTargeting.Outcome o = tg.Apply(t, req, "yellow box API", shape?.Summary);
            if (o.Decision.Step == BoxStep.None) return BoxApi.Error(ModVersion, action, o.Error, o.Reason);
            r.Boxable = t.Valid;
            r.Boxed = req == BoxRequest.Add;
            r.Changed = o.Changed;
            r.Applied = "immediate";
            r.Notes.Add("saved at once (the tool's write); painted and obeyed from the next frame (List's merged outlines too)");
            return BoxApi.TargetResult(ModVersion, action, r);
        }

        /// <summary>
        /// The target an API target means. Returns a not_found reason, or sets errJson (ambiguous_exit / entrance_derived /
        /// exit out of range with the building's accesses listed). Node / building ids are checked; x / z = the tool's pick.
        /// </summary>
        private static string Pick(BoxTargeting tg, ApiTarget at, string action, out Target t, out string errJson)
        {
            t = default;
            errJson = null;
            EntityManager em = Em;
            switch (at.Form)
            {
                case ApiTargetForm.Node:
                {
                    Entity n = Find(em, at.Index, at.Version, ComponentType.ReadOnly<Game.Net.Node>());
                    if (n == Entity.Null) return $"no node {at.Index}" + (at.Version >= 0 ? ":" + at.Version : "") + " in this city";
                    t = tg.JunctionTarget(n);
                    return null;
                }
                case ApiTargetForm.Building:
                {
                    Entity b = Find(em, at.Index, at.Version, ComponentType.ReadOnly<Building>());
                    if (b == Entity.Null) return $"no building {at.Index}" + (at.Version >= 0 ? ":" + at.Version : "") + " in this city";
                    b = BoxFacts.MainBuilding(em, b);   // the tool's rule: an extension / sub-building stands for its main building
                    BoxTargeting.AccessList acc = tg.ExitsOf(b);
                    var all = new List<BoxFacts.Exit>(acc.All());
                    float3 bpos = em.HasComponent<Game.Objects.Transform>(b) ? em.GetComponentData<Game.Objects.Transform>(b).m_Position : default;
                    if (all.Count == 0)
                    {
                        if (at.Exit >= 0) return "the building has no real car exit or entrance";
                        t = tg.NoAccessTarget(b, bpos);
                        return null;
                    }
                    List<ApiExitChoice> choices = Choices(tg, b, acc);
                    int k = BoxApi.PickExit(at.Exit, choices, out string code, out string reason);
                    if (k < 0)
                    {
                        errJson = BoxApi.ExitChoiceError(ModVersion, action, code, reason, new ApiBoxRef(ApiBoxKind.BuildingExit, b.Index, b.Version), choices);
                        return null;
                    }
                    t = tg.AccessTarget(b, all[k]);
                    return null;
                }
                case ApiTargetForm.Point:
                {
                    var xz = new float2((float)at.X, (float)at.Z);
                    if (!tg.PointHit(xz, out Entity hit, out float3 pos)) return $"nothing the tool can pick at ({at.X:0.##}, {at.Z:0.##}) (no road, node or building there)";
                    t = tg.Resolve(hit, pos);
                    if (t.Kind == Kind.None) return $"no junction or building car exit at ({at.X:0.##}, {at.Z:0.##}) (hit {hit})";
                    return null;
                }
                default: return "no target";
            }
        }

        /// <summary>The live entity with this index (and version when given) that has 'with'; Entity.Null when none.</summary>
        private static Entity Find(EntityManager em, int index, int version, ComponentType with)
        {
            if (version > 0)
            {
                var e = new Entity { Index = index, Version = version };
                return em.Exists(e) && em.HasComponent(e, with) && !em.HasComponent<Deleted>(e) && !em.HasComponent<Temp>(e) ? e : Entity.Null;
            }
            using (EntityQuery q = em.CreateEntityQuery(new EntityQueryDesc { All = new[] { with }, None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() } }))
            using (NativeArray<Entity> all = q.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < all.Length; i++) if (all[i].Index == index) return all[i];
            }
            return Entity.Null;
        }

        /// <summary>The building's real accesses in the tool's order (exits, then entrances), for exit indices.</summary>
        private static List<ApiExitChoice> Choices(BoxTargeting tg, Entity b, BoxTargeting.AccessList acc)
        {
            EntityManager em = Em;
            var r = new List<ApiExitChoice>();
            int i = 0;
            foreach (BoxFacts.Exit x in acc.All())
            {
                bool boxed = x.IsJunction ? BoxStore.Has(em, x.Node, true) : BoxStore.HasExit(em, b, x.C.Key);   // JR-T-021: an entrance has its own key
                string role = x.IsEntrance ? "entrance" : x.IsJunction ? "junction" : x.Way == AccessWay.Both ? "two-way access" : "exit";
                r.Add(new ApiExitChoice { Exit = i++, Role = role, Key = x.C.Key.ToString(), Boxed = boxed, At = x.C.World });
            }
            return r;
        }

        /// <summary>The result's target reference: node, or building + its access index (-1 for a lot-front fallback key).</summary>
        private static void FillRef(BoxTargeting tg, Target t, ApiTargetResult r)
        {
            if (t.Kind == Kind.Junction)
            {
                r.Ref = new ApiBoxRef(ApiBoxKind.Junction, t.Entity.Index, t.Entity.Version);
                if (t.FromBuilding != Entity.Null) r.FromBuilding = new ApiBoxRef(ApiBoxKind.BuildingExit, t.FromBuilding.Index, t.FromBuilding.Version);
                return;
            }
            r.Ref = new ApiBoxRef(t.Entrance ? ApiBoxKind.BuildingEntrance : ApiBoxKind.BuildingExit, t.Entity.Index, t.Entity.Version, t.Fallback ? -1 : ExitIndex(tg, t.Entity, t.Exit.C.Key));
            r.Role = BoxTargeting.Role(t);
            if (t.Exit.C.Kind != ExitKind.None) r.ExitKey = t.Exit.C.Key.ToString();
        }

        /// <summary>Index of the access with this key in the tool's order (exits, then entrances); -1 when none.</summary>
        private static int ExitIndex(BoxTargeting tg, Entity b, ExitKey key)
        {
            int i = 0;
            foreach (BoxFacts.Exit x in tg.ExitsOf(b).All())
            {
                if (x.C.Key.Equals(key)) return i;
                i++;
            }
            return -1;
        }

        /// <summary>A drawn box (merge input) as an API reference; false for the paint test list's junctions.</summary>
        private static bool RefOf(BoxTargeting tg, BoxMergeInput m, out ApiBoxRef r)
        {
            r = default;
            int index = (int)(m.Key >> 32), version = (int)(uint)(m.Key & 0xFFFFFFFFL);
            if (m.Sub < 0) return false;   // paint test list junction (not a saved box)
            if (m.Sub == 0) { r = new ApiBoxRef(ApiBoxKind.Junction, index, version); return true; }
            BoxMergeInput.KeyOfSub(m.Sub, out ExitKey k);
            var b = new Entity { Index = index, Version = version };
            int exit = Em.Exists(b) ? ExitIndex(tg, b, k) : -1;
            r = new ApiBoxRef(k.ExitKind == ExitKind.Entrance ? ApiBoxKind.BuildingEntrance : ApiBoxKind.BuildingExit, index, version, exit);
            return true;
        }

        // ------------------------------------------------------------------ list

        private static string ListCore(string json)
        {
            if (!BoxApi.ParseFilter(json, out ApiFilter f, out string bad)) return BoxApi.Error(ModVersion, "list", BoxApiErrors.BadTarget, bad);
            BoxTargeting tg = Targeting();
            tg.ClearCache();   // fresh exit lists (see TargetCall)
            EntityManager em = Em;
            var boxes = new List<ApiBoxInfo>();
            var notes = new List<string>();
            if (MergedBoxes.Inputs.Count == 0) notes.Add("no boxes drawn right now (paint off or not updated yet): outlines solved on demand");

            using (EntityQuery q = em.CreateEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<YellowBoxJunction>() }, None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() } }))
            using (NativeArray<Entity> nodes = q.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < nodes.Length; i++)
                {
                    Entity n = nodes[i];
                    var info = new ApiBoxInfo { Ref = new ApiBoxRef(ApiBoxKind.Junction, n.Index, n.Version) };
                    float3 p = em.HasComponent<Game.Net.Node>(n) ? em.GetComponentData<Game.Net.Node>(n).m_Position : default;
                    FillOutline(tg, info, BoxMergeInput.EntityKey(n.Index, n.Version), 0, () => tg.JunctionTarget(n), new V2(p.x, p.z));
                    if (f.Passes(info.Center.X, info.Center.Z)) boxes.Add(info);
                }
            }

            using (EntityQuery q = em.CreateEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<YellowBoxEntrance>() }, None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() } }))
            using (NativeArray<Entity> buildings = q.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < buildings.Length; i++)
                {
                    Entity b = buildings[i];
                    BoxTargeting.AccessList acc = tg.ExitsOf(b);
                    var all = new List<BoxFacts.Exit>(acc.All());
                    float3 bp = em.HasComponent<Game.Objects.Transform>(b) ? em.GetComponentData<Game.Objects.Transform>(b).m_Position : default;
                    long key = BoxMergeInput.EntityKey(b.Index, b.Version);
                    YellowBoxEntrance c = em.GetComponentData<YellowBoxEntrance>(b);
                    var saved = new List<ExitKey>();
                    for (int s = 0; s < c.m_Exits.Length; s++) saved.Add(c.m_Exits[s]);
                    foreach (ExitKey sk in saved)
                    {
                        var info = new ApiBoxInfo { Ref = new ApiBoxRef(ApiBoxKind.BuildingExit, b.Index, b.Version), ExitKey = sk.ToString() };
                        if (sk.ExitKind == ExitKind.Fallback && !BoxFacts.FindExit(acc.Exits, sk, out _))   // the paint (RebuildEntrance): a Fallback key that matches a real exit is drawn as that exit
                        {
                            info.Center = new V2(bp.x, bp.z);
                            info.Outline = new List<V3>();
                            info.Note = "lot-front marker key (never drawn; remove it with the tool or Remove)";
                        }
                        else if (sk.ExitKind == ExitKind.Entrance && (BoxFacts.FindExit(acc.Entrances, sk, out BoxFacts.Exit x) || BoxFacts.FindExit(acc.Exits, sk, out x))
                                 || sk.ExitKind != ExitKind.Entrance && BoxFacts.FindExit(acc.Exits, sk, out x))   // the paint's match (RebuildEntrance; JR-T-021: entrance keys)
                        {
                            int k = all.FindIndex(e => e.C.Key.Equals(x.C.Key));
                            info.Ref.Exit = k;
                            if (x.IsEntrance) info.Ref.Kind = ApiBoxKind.BuildingEntrance;
                            long sub = MergedBoxes.DrawnOutline(key, BoxMergeInput.ExitSub(sk)) != null ? BoxMergeInput.ExitSub(sk) : BoxMergeInput.ExitSub(x.C.Key);
                            FillOutline(tg, info, key, sub, () => tg.ExitTarget(b, x), x.C.World);
                        }
                        else
                        {
                            info.Center = new V2(bp.x, bp.z);
                            info.Outline = new List<V3>();
                            info.Note = sk.ExitKind == ExitKind.Entrance
                                ? "saved entrance not found among the building's accesses now (the paint drops or re-keys it on its next update)"
                                : "saved exit not found among the building's exits now (the paint drops or re-keys it on its next update)";
                        }
                        if (f.Passes(info.Center.X, info.Center.Z)) boxes.Add(info);
                    }
                }
            }
            return BoxApi.ListResult(ModVersion, boxes, notes);
        }

        /// <summary>
        /// A listed box's outline and group: the paint's drawn outline (MergedBoxes) when it has one (what is on the road),
        /// else the shared solver's (Boxes/BoxTargeting.Solve, as the tool's hover). Centre = the outline's middle.
        /// </summary>
        private static void FillOutline(BoxTargeting tg, ApiBoxInfo info, long key, long sub, Func<Target> target, V2 fallbackCentre)
        {
            List<V3> drawn = MergedBoxes.DrawnOutline(key, sub);
            if (drawn != null)
            {
                info.Outline = drawn;
                foreach (BoxMergeInput m in MergedBoxes.GroupMates(key, sub)) if (RefOf(tg, m, out ApiBoxRef r)) info.MergedGroup.Add(r);
            }
            else
            {
                Target t = target();
                BoxTargeting.SolvedShape s = t.Kind != Kind.None ? tg.Solve(t) : null;
                info.Outline = s?.Own ?? new List<V3>();
                if (s != null) foreach (BoxMergeInput m in s.GroupMates) if (RefOf(tg, m, out ApiBoxRef r)) info.MergedGroup.Add(r);
                if (s != null && s.Own == null) info.Note = "not drawn: " + s.Failure;
            }
            info.Center = info.Outline.Count > 0 ? BoxApi.Centre(info.Outline) : fallbackCentre;
        }
    }
}
