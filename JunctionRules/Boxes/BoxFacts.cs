using System.Collections.Generic;
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
            // 0.4.3 (owner ruling 3): whose arms are these (Core/BoxSelection.cs header)
            Entity siteBuilding = Entity.Null;
            if (f.Owned)
            {
                siteBuilding = OwnerBuilding(em, node);
                f.NodeOwnerIsBuilding = siteBuilding != Entity.Null;
            }
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
                if (!em.HasComponent<Owner>(e)) f.PublicRoadArms++;
                else
                {
                    Entity ob = OwnerBuilding(em, e);
                    if (ob == Entity.Null) f.OtherOwnedRoadArms++;
                    else
                    {
                        f.BuildingRoadArms++;
                        if (siteBuilding == Entity.Null) siteBuilding = ob;
                        else if (siteBuilding != ob) f.MixedOwners = true;
                    }
                }
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

        /// <summary>
        /// 0.4.3: the building at the end of e's owner chain (Owner -> Owner, at most 4 steps; the top-most Building, so a
        /// ServiceUpgrade / extension resolves to its main building), or Entity.Null when the chain does not reach a building.
        /// </summary>
        internal static Entity OwnerBuilding(EntityManager em, Entity e)
        {
            Entity found = Entity.Null;
            Entity cur = e;
            for (int i = 0; i < 5 && cur != Entity.Null && em.Exists(cur) && em.HasComponent<Owner>(cur); i++)
            {
                cur = em.GetComponentData<Owner>(cur).m_Owner;
                if (cur != Entity.Null && em.Exists(cur) && em.HasComponent<Building>(cur)) found = cur;
            }
            return found;
        }

        /// <summary>0.4.3: true when an arm edge at this node is a building's own road (for the arm-yield rule).</summary>
        internal static bool IsBuildingRoad(EntityManager em, Entity edge) => em.HasComponent<Owner>(edge) && OwnerBuilding(em, edge) != Entity.Null;

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

        /// <summary>0.4.3: one car exit of a building (Core/BuildingExit.cs: where it comes from, with cites).</summary>
        internal struct Exit
        {
            public ExitCandidate C;
            /// <summary>Driveway / spawn / fallback: the public road edge; junction: Entity.Null.</summary>
            public Entity Road;
            /// <summary>Junction: the node where the building's road meets the public road; driveway: the sub-net node.</summary>
            public Entity Node;
            /// <summary>Curve position on Road (driveway / spawn / fallback).</summary>
            public float T;
            public bool IsJunction => C.Kind == ExitKind.Junction;
            /// <summary>JR-T-014c (G9 / G10): which way cars use it (exit Out, entrance In, two-way drive / spawn location Both).</summary>
            public AccessWay Way;
            /// <summary>JR-T-014c (G10): the access on the building side (sub-net node, spawn location; fallback: lot-front point) for the hover arrow.</summary>
            public float3 Inner;
            public bool HasInner;
            public bool IsEntrance => C.Kind == ExitKind.Entrance;
            public bool IsFallback => C.Kind == ExitKind.Fallback;
        }

        /// <summary>
        /// JR-T-014c (G9 / G10): every car access of a building, read-only: exits (as 0.4.3, but never a Fallback: G10) and
        /// entrances (entry-only sub-net nodes: driveway kind Entrance on a public road's middle, or Junction kind where the
        /// building's own road meets a public road end; never saved, Core/BuildingAccess.cs). 'fallback' = the lot-front
        /// point (Building.m_RoadEdge + m_CurvePosition), kept apart: never a box target, used for a saved Fallback marker
        /// key (hover / removal) and for the tool's marker key. HasFallback false when the building has no road edge.
        /// </summary>
        internal static void Accesses(EntityManager em, Entity building, List<string> notes, out List<Exit> exits, out List<Exit> entrances, out Exit fallback, out bool hasFallback)
        {
            exits = Scan(em, building, notes, out entrances, out fallback, out hasFallback);
        }

        /// <summary>JR-T-014c (G9): the building's entrances (Accesses).</summary>
        internal static List<Exit> Entrances(EntityManager em, Entity building, List<string> notes)
        {
            Scan(em, building, notes, out List<Exit> entrances, out _, out _);
            return entrances;
        }

        /// <summary>
        /// 0.4.3 (JR-T-007c, owner rulings 16 Nov 2026): every car exit of a building, read-only (Core/BuildingExit.cs header
        /// for the game data and cites). The building and its installed upgrades (Game.Buildings.InstalledUpgrade) are
        /// searched: their Game.Net.SubNet edges (driveways, access roads) and their Game.Objects.SubObject spawn locations.
        /// Fallback (Building.m_RoadEdge + m_CurvePosition) only when nothing else is found. 'notes' gets one line per
        /// rejected candidate (entry-only drives, spawn locations on the building's own lanes) for the log.
        /// </summary>
        internal static List<Exit> Exits(EntityManager em, Entity building, List<string> notes) => Scan(em, building, notes, out _, out _, out _);

        private static List<Exit> Scan(EntityManager em, Entity building, List<string> notes, out List<Exit> entrances, out Exit fallback, out bool hasFallback)
        {
            var found = new List<Exit>();
            var foundIn = new List<Exit>();
            entrances = foundIn;
            fallback = default;
            hasFallback = false;
            if (!Alive(em, building) || !em.HasComponent<Building>(building)) return found;
            GetFrame(em, building, out V2 bpos, out V2 fwd);
            var sources = new List<Entity> { building };
            if (em.HasBuffer<InstalledUpgrade>(building))
            {
                DynamicBuffer<InstalledUpgrade> up = em.GetBuffer<InstalledUpgrade>(building, true);
                for (int i = 0; i < up.Length; i++) if (Alive(em, up[i].m_Upgrade)) sources.Add(up[i].m_Upgrade);
            }
            var ownEdges = new HashSet<Entity>();
            foreach (Entity src in sources)
            {
                if (!em.HasBuffer<Game.Net.SubNet>(src)) continue;
                DynamicBuffer<Game.Net.SubNet> sn = em.GetBuffer<Game.Net.SubNet>(src, true);
                for (int i = 0; i < sn.Length; i++)
                {
                    Entity x = sn[i].m_SubNet;
                    if (Alive(em, x) && em.HasComponent<Edge>(x)) ownEdges.Add(x);
                }
            }
            // 1 + 2: sub-net nodes that meet a public road
            var seenNodes = new HashSet<Entity>();
            foreach (Entity own in ownEdges)
            {
                Edge oe = em.GetComponentData<Edge>(own);
                foreach (Entity n in new[] { oe.m_Start, oe.m_End })
                {
                    if (!seenNodes.Add(n) || !Alive(em, n) || !em.HasBuffer<ConnectedEdge>(n) || !em.HasComponent<Game.Net.Node>(n)) continue;
                    float3 np = em.GetComponentData<Game.Net.Node>(n).m_Position;
                    DynamicBuffer<ConnectedEdge> ce = em.GetBuffer<ConnectedEdge>(n, true);
                    var publicEnds = new List<Entity>();
                    var publicMiddles = new List<Entity>();
                    bool carOut = false, carIn = false, carAny = false, ownRoad = false;
                    for (int i = 0; i < ce.Length; i++)
                    {
                        Entity r = ce[i].m_Edge;
                        if (!Alive(em, r) || !em.HasComponent<Edge>(r)) continue;
                        Edge re = em.GetComponentData<Edge>(r);
                        bool atEnd = re.m_Start == n || re.m_End == n;
                        if (ownEdges.Contains(r) || (em.HasComponent<Owner>(r) && OwnerBuilding(em, r) == building))
                        {
                            if (!atEnd) continue;
                            CarFlow(em, r, np, ref carOut, ref carIn, ref carAny);
                            ownRoad |= em.HasComponent<Road>(r);
                            continue;
                        }
                        if (!em.HasComponent<Road>(r) || em.HasComponent<Owner>(r)) continue;   // paths, other sites' roads
                        if (atEnd) publicEnds.Add(r); else publicMiddles.Add(r);
                    }
                    if (publicEnds.Count == 0 && publicMiddles.Count == 0) continue;
                    V2 local = ExitRules.ToLocal(new V2(np.x, np.z), bpos, fwd);
                    if (!carAny) { Note(notes, $"sub-net node {n} meets a public road but carries no car lane of the building (path)"); continue; }
                    AccessWay way = AccessRules.Way(carOut, carIn);
                    if (!carOut)
                    {
                        // JR-T-014c (G9): an entry-only node is a car ENTRANCE (0.4.3-0.4.7 skipped it: "is an entry only")
                        if (publicEnds.Count > 0)
                        {
                            Note(notes, $"sub-net node {n} is an entry-only access road junction (no derived box: box the junction node with the tool)");
                            foundIn.Add(new Exit
                            {
                                Node = n, Way = AccessWay.In, Inner = np, HasInner = true,
                                C = new ExitCandidate
                                {
                                    Kind = ExitKind.Junction, Key = new ExitKey(ExitKind.Junction, local.X, local.Z), RoadId = n.Index, World = new V2(np.x, np.z),
                                    Source = $"entrance junction node {n}: building {(ownRoad ? "access road" : "car path")} meets {publicEnds.Count} public road end(s), car lanes flow in only",
                                },
                            });
                            continue;
                        }
                        foreach (Entity r in publicMiddles)
                        {
                            if (!ConnectedNodeT(em, r, n, out float t)) continue;
                            Exit x = OnRoad(em, r, t, ExitKind.Entrance, local, $"ENTRANCE: sub-net node {n} joined to road {r} at t {t:0.000} (road ConnectedNode; building edge car lanes flow in only)");
                            x.Node = n; x.Way = AccessWay.In; x.Inner = np; x.HasInner = true;
                            foundIn.Add(x);
                        }
                        continue;
                    }
                    if (publicEnds.Count > 0)
                    {
                        found.Add(new Exit
                        {
                            Node = n, Way = way, Inner = np, HasInner = true,
                            C = new ExitCandidate
                            {
                                Kind = ExitKind.Junction, Key = new ExitKey(ExitKind.Junction, local.X, local.Z), RoadId = n.Index, World = new V2(np.x, np.z),
                                Source = $"junction node {n}: building {(ownRoad ? "access road" : "car path")} meets {publicEnds.Count} public road end(s) (ConnectedEdge, Edge.m_Start/m_End)",
                            },
                        });
                        continue;
                    }
                    foreach (Entity r in publicMiddles)
                    {
                        if (!ConnectedNodeT(em, r, n, out float t)) continue;
                        Exit x = OnRoad(em, r, t, ExitKind.Driveway, local, $"sub-net node {n} joined to road {r} at t {t:0.000} (road ConnectedNode; building edge car lanes flow {(carIn ? "both ways" : "out")})");
                        x.Node = n; x.Way = way; x.Inner = np; x.HasInner = true;
                        found.Add(x);
                    }
                }
            }
            // 3: car spawn locations wired to a public road lane
            foreach (Entity src in sources)
            {
                if (!em.HasBuffer<Game.Objects.SubObject>(src)) continue;
                DynamicBuffer<Game.Objects.SubObject> so = em.GetBuffer<Game.Objects.SubObject>(src, true);
                for (int i = 0; i < so.Length; i++)
                {
                    Entity o = so[i].m_SubObject;
                    if (!Alive(em, o) || !em.HasComponent<Game.Objects.SpawnLocation>(o) || !em.HasComponent<PrefabRef>(o) || !em.HasComponent<Game.Objects.Transform>(o)) continue;
                    Entity pf = em.GetComponentData<PrefabRef>(o).m_Prefab;
                    if (!em.HasComponent<SpawnLocationData>(pf)) continue;
                    SpawnLocationData sd = em.GetComponentData<SpawnLocationData>(pf);
                    bool car = (sd.m_ConnectionType == RouteConnectionType.Road || sd.m_ConnectionType == RouteConnectionType.Cargo || sd.m_ConnectionType == RouteConnectionType.Parking)
                               && (sd.m_RoadTypes & Game.Net.RoadTypes.Car) != 0;
                    if (!car) continue;
                    Game.Objects.SpawnLocation sl = em.GetComponentData<Game.Objects.SpawnLocation>(o);
                    Entity lane = sl.m_ConnectedLane1;
                    if (!Alive(em, lane) || !em.HasComponent<Owner>(lane) || !em.HasComponent<Curve>(lane)) continue;
                    Entity r = em.GetComponentData<Owner>(lane).m_Owner;
                    if (ownEdges.Contains(r) || !Alive(em, r) || !em.HasComponent<Edge>(r) || !em.HasComponent<Road>(r) || em.HasComponent<Owner>(r)) continue;   // own lanes: leaves through a drive / access road
                    float3 lp = MathUtils.Position(em.GetComponentData<Curve>(lane).m_Bezier, sl.m_CurvePosition1);
                    MathUtils.Distance(em.GetComponentData<Curve>(r).m_Bezier.xz, lp.xz, out float t);
                    float3 op = em.GetComponentData<Game.Objects.Transform>(o).m_Position;
                    V2 local = ExitRules.ToLocal(new V2(op.x, op.z), bpos, fwd);
                    Exit sx = OnRoad(em, r, t, ExitKind.SpawnLocation, local, $"spawn location {o} -> lane {lane} of road {r} at t {t:0.000} (SpawnLocation.m_ConnectedLane1)");
                    sx.Way = AccessWay.Both; sx.Inner = op; sx.HasInner = true;   // a spawn location / gate is used both ways
                    found.Add(sx);
                }
            }
            // 4: fallback. JR-T-014c (G10): never a box target (only real accesses get boxes); read always, kept apart
            {
                Building b = em.GetComponentData<Building>(building);
                if (Alive(em, b.m_RoadEdge) && em.HasComponent<Curve>(b.m_RoadEdge) && em.HasComponent<Edge>(b.m_RoadEdge))
                {
                    int depth = 0;
                    if (em.HasComponent<PrefabRef>(building))
                    {
                        Entity pf = em.GetComponentData<PrefabRef>(building).m_Prefab;
                        if (em.HasComponent<BuildingData>(pf)) depth = em.GetComponentData<BuildingData>(pf).m_LotSize.y;
                    }
                    fallback = OnRoad(em, b.m_RoadEdge, b.m_CurvePosition, ExitKind.Fallback, new V2(0, depth * 4.0),
                        $"FALLBACK: lot front middle, Building.m_RoadEdge {b.m_RoadEdge} at t {b.m_CurvePosition:0.000} (not a real car access: never boxed, G10)");
                    fallback.Way = AccessWay.Both;
                    V2 front = ExitRules.ToWorld(new V2(0, depth * 4.0), bpos, fwd);
                    float y = em.HasComponent<Game.Objects.Transform>(building) ? em.GetComponentData<Game.Objects.Transform>(building).m_Position.y : 0f;
                    fallback.Inner = new float3((float)front.X, y, (float)front.Z);
                    fallback.HasInner = true;
                    hasFallback = true;
                }
            }
            entrances = Keep(foundIn);
            return Keep(found);
        }

        /// <summary>Fixed order + same-place merge (ExitRules.Normalise), mapped back to the read rows.</summary>
        private static List<Exit> Keep(List<Exit> found)
        {
            var cands = new List<ExitCandidate>(found.Count);
            foreach (Exit x in found) cands.Add(x.C);
            List<ExitCandidate> keep = ExitRules.Normalise(cands);
            var r2 = new List<Exit>(keep.Count);
            foreach (ExitCandidate c in keep)
                foreach (Exit x in found)
                    if (x.C.Kind == c.Kind && x.C.Key.Equals(c.Key) && x.C.RoadId == c.RoadId) { r2.Add(x); break; }
            return r2;
        }

        /// <summary>
        /// 0.4.3 driving rule: the building's boxed car exits as bands (road edge key + curve position), read-only. Saved
        /// keys are matched to the building's current exits as the paint matches them; a legacy (0.4.0-0.4.2) box not yet
        /// migrated by the paint takes the exit nearest its old access point (Core/ExitBands.Boxed). Junction exits are
        /// junction boxes on their node (driven through YellowBoxJunction), never bands. JR-T-021: a saved entrance key is a
        /// band at its entrance (matched as the paint matches it: entrances first, then exits at the same place); nothing is
        /// derived. Returns the keys without an access.
        /// </summary>
        internal static int BoxedExitBands(EntityManager em, Entity building, List<DrivingBands.Source> into)
        {
            if (!Alive(em, building) || !em.HasComponent<YellowBoxEntrance>(building) || !em.HasComponent<Building>(building)) return 0;
            YellowBoxEntrance c = em.GetComponentData<YellowBoxEntrance>(building);
            List<Exit> exits = Scan(em, building, null, out List<Exit> entrances, out _, out _);
            var cands = new List<ExitCandidate>(exits.Count);
            foreach (Exit x in exits) cands.Add(x.C);
            var inCands = new List<ExitCandidate>(entrances.Count);
            foreach (Exit x in entrances) inCands.Add(x.C);
            var keys = new List<ExitKey>(c.m_Exits.Length);
            var inHit = new bool[entrances.Count];
            int unmatchedIn = 0;
            for (int i = 0; i < c.m_Exits.Length; i++)
            {
                ExitKey k = c.m_Exits[i];
                // JR-T-014c (G10): a Fallback key is a marker unless it re-keys to a real exit (ExitRules.Match); a marker is
                // not a band and not 'unmatched' (the paint keeps it untouched)
                if (k.ExitKind == ExitKind.Fallback && ExitRules.Match(k, cands) < 0) continue;
                if (k.ExitKind == ExitKind.Entrance)
                {
                    int e = ExitRules.Match(k, inCands);
                    if (e >= 0) { inHit[e] = true; continue; }
                    if (ExitRules.Match(k, cands) < 0) { unmatchedIn++; continue; }   // gone: the paint drops it
                }
                keys.Add(k);
            }
            V2 old = default;
            if (c.Legacy)
            {
                Building bd = em.GetComponentData<Building>(building);
                Access a = Alive(em, bd.m_RoadEdge) ? AccessOf(em, bd.m_RoadEdge, bd.m_CurvePosition) : default;
                if (a.Valid) old = new V2(a.Point.x, a.Point.z);
                else GetFrame(em, building, out old, out _);
            }
            List<int> boxed = ExitBands.Boxed(keys, c.Legacy, old, cands, out int unmatched);
            GetFrame(em, building, out V2 bpos, out _);
            foreach (int i in boxed) AddSource(em, building, exits[i], bpos, into);
            for (int i = 0; i < entrances.Count; i++) if (inHit[i]) AddSource(em, building, entrances[i], bpos, into);
            return unmatched + unmatchedIn;
        }

        /// <summary>
        /// 0.4.10 (G18): one boxed exit / entrance as the paint builds it (Paint/YellowBoxPaintSystem RebuildEntrance: ExitFacts,
        /// BoxSelection.ExitOk, the building position for the median side); its window is made by Boxes/DrivingBands.cs with
        /// the paint's own BoxShapes.Entrance. An exit the paint would not draw (invalid now) is not driven either.
        /// </summary>
        private static void AddSource(EntityManager em, Entity building, Exit x, V2 bpos, List<DrivingBands.Source> into)
        {
            if (!Alive(em, x.Road)) return;
            if (!BoxSelection.ExitOk(ExitFacts(em, building, x, out Access a), out _)) return;
            into.Add(new DrivingBands.Source { Building = building, Exit = x, Access = a, BuildingPos = bpos });
        }

        private static Exit OnRoad(EntityManager em, Entity road, float t, ExitKind kind, V2 local, string source)
        {
            Bezier4x3 c = em.GetComponentData<Curve>(road).m_Bezier;
            float3 p = MathUtils.Position(c, math.saturate(t));
            return new Exit
            {
                Road = road,
                T = math.saturate(t),
                C = new ExitCandidate
                {
                    Kind = kind, Key = new ExitKey(kind, local.X, local.Z), RoadId = road.Index,
                    AlongM = MathUtils.Length(c, new Bounds1(0f, math.saturate(t))), World = new V2(p.x, p.z), Source = source,
                },
            };
        }

        /// <summary>The road's ConnectedNode curve position for this node (GenerateEdgesSystem.cs 1261 / 1897).</summary>
        private static bool ConnectedNodeT(EntityManager em, Entity road, Entity node, out float t)
        {
            t = 0f;
            if (!em.HasBuffer<ConnectedNode>(road)) return false;
            DynamicBuffer<ConnectedNode> cn = em.GetBuffer<ConnectedNode>(road, true);
            for (int i = 0; i < cn.Length; i++) if (cn[i].m_Node == node) { t = cn[i].m_CurvePosition; return true; }
            return false;
        }

        /// <summary>
        /// Car lanes of a building edge at this node: any at all, any flowing TOWARD the node (out of the building) and
        /// (JR-T-014c) any flowing AWAY from it (into the building).
        /// </summary>
        private static void CarFlow(EntityManager em, Entity edge, float3 nodePos, ref bool toward, ref bool away, ref bool any)
        {
            if (!em.HasBuffer<Game.Net.SubLane>(edge)) return;
            DynamicBuffer<Game.Net.SubLane> subs = em.GetBuffer<Game.Net.SubLane>(edge, true);
            for (int i = 0; i < subs.Length; i++)
            {
                Entity l = subs[i].m_SubLane;
                if (!Alive(em, l) || !em.HasComponent<Game.Net.CarLane>(l) || !em.HasComponent<Curve>(l)) continue;
                any = true;
                Bezier4x3 bz = em.GetComponentData<Curve>(l).m_Bezier;
                if (math.distance(bz.d.xz, nodePos.xz) < math.distance(bz.a.xz, nodePos.xz)) toward = true;
                else away = true;
            }
        }

        /// <summary>The building's frame: position (x, z) and the lot front direction (Transform.m_Rotation * +z).</summary>
        internal static void GetFrame(EntityManager em, Entity building, out V2 pos, out V2 forward)
        {
            pos = default;
            forward = new V2(0, 1);
            if (!em.HasComponent<Game.Objects.Transform>(building)) return;
            Game.Objects.Transform tr = em.GetComponentData<Game.Objects.Transform>(building);
            float3 f = math.mul(tr.m_Rotation, new float3(0f, 0f, 1f));
            pos = new V2(tr.m_Position.x, tr.m_Position.z);
            if (math.lengthsq(f.xz) > 1e-6f) forward = new V2(f.x, f.z);
        }

        private static void Note(List<string> notes, string s) { if (notes != null && !notes.Contains(s)) notes.Add(s); }

        /// <summary>
        /// Facts for one non-junction exit (driveway / spawn location / fallback) of a building: the exit road and the exit
        /// point on it, for Core/BoxSelection.ExitOk. 'access' = where the box goes (the exit point).
        /// </summary>
        internal static EntranceFacts ExitFacts(EntityManager em, Entity building, Exit exit, out Access access)
        {
            access = default;
            var f = new EntranceFacts();
            f.Exists = Alive(em, building);
            if (!f.Exists) return f;
            f.IsBuilding = em.HasComponent<Building>(building);
            if (!f.IsBuilding) return f;
            f.Owned = em.HasComponent<Owner>(building) && em.HasComponent<Building>(em.GetComponentData<Owner>(building).m_Owner);
            Entity road = exit.Road;
            f.HasRoadEdge = road != Entity.Null && em.Exists(road) && !em.HasComponent<Deleted>(road) && em.HasComponent<Curve>(road);
            if (exit.C.Kind == ExitKind.Fallback && em.HasComponent<PrefabRef>(building))
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
            access = AccessOf(em, road, exit.T);
            f.AccessInsideEdge = access.Valid && access.AlongM >= access.MinM - 0.01 && access.AlongM <= access.MaxM + 0.01;
            return f;
        }

        /// <summary>The exit of 'building' a saved key stands for (Core/BuildingExit.cs ExitRules.Match); false when none.</summary>
        internal static bool FindExit(List<Exit> exits, ExitKey key, out Exit exit)
        {
            exit = default;
            var cands = new List<ExitCandidate>(exits.Count);
            foreach (Exit x in exits) cands.Add(x.C);
            int i = ExitRules.Match(key, cands);
            if (i < 0) return false;
            exit = exits[i];
            return true;
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
                // 0.4.2: from the NEARER corner of each mouth (Core/EntranceBox.EdgePart), not the mouth middle.
                EdgeGeometry g = em.GetComponentData<EdgeGeometry>(road);
                float sa = AlongOf(curve.m_Bezier, g.m_Start.m_Left.a), sb = AlongOf(curve.m_Bezier, g.m_Start.m_Right.a);
                float ea = AlongOf(curve.m_Bezier, g.m_End.m_Left.d), eb = AlongOf(curve.m_Bezier, g.m_End.m_Right.d);
                if (math.min(sa, sb) > math.max(ea, eb)) { float t1 = sa, t2 = sb; sa = ea; sb = eb; ea = t1; eb = t2; }
                EntranceBox.EdgePart(sa, sb, ea, eb, out double mn, out double mx);
                a.MinM = (float)mn;
                a.MaxM = (float)mx;
            }
            a.Valid = a.LengthM > 0.5f;
            return a;
        }

        private static float AlongOf(Bezier4x3 c, float3 p)
        {
            MathUtils.Distance(c.xz, p.xz, out float t);
            return MathUtils.Length(c, new Bounds1(0f, t));
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
