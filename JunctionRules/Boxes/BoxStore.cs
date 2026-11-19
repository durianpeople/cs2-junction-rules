using System;
using System.IO;
using JunctionRules.Core;
using Unity.Entities;

namespace JunctionRules.Boxes
{
    /// <summary>
    /// 0.4.0-probe (JR-T-007): the one place that adds or removes a saved yellow box (our own two components, nothing
    /// else; 0.4.3: one key per boxed building exit), and the hand-over between the tool and the paint system so neither names the other: the tool calls
    /// <see cref="Add"/> / <see cref="Remove"/> and the paint system listens on <see cref="Changed"/>; the paint system
    /// publishes its shape parameters (exit box length, kerb inset, marking margin) here for the tool's hover outline. Main thread only.
    /// </summary>
    internal static class BoxStore
    {
        /// <summary>0.4.3: bumped on every box write (tool, paint migration / re-key / drop): the driving rule's exit list is rebuilt when it moves.</summary>
        internal static int Version;

        /// <summary>Raised after the tool added or removed a box (the paint system redraws at once).</summary>
        internal static event Action Changed;

        /// <summary>
        /// 0.4.2: shape parameters shared by the paint and the tool's hover outline (same shape as the paint).
        /// JR-T-007b-r F6: published at mod load and tool start from the paint list file + Options (LoadParams), and by
        /// the paint system at every list read (PublishParams); the Options margin setter keeps a list override (SetMarginSetting).
        /// </summary>
        internal static BoxShapeParams Params = new BoxShapeParams();
        /// <summary>True once the paint system published its own list read (the tool start then keeps it, never re-reads the file).</summary>
        internal static bool PaintPublished;

        /// <summary>Exit box length along the road in metres (the paint list's "entranceLength" / "exitLength"; default 8).</summary>
        internal static double EntranceLength => Params.EntranceLength;
        internal static double KerbInset => Params.KerbInset;
        internal static double MarkingMargin => Params.MarkingMargin;

        /// <summary>The paint system's list read (its m_Config) is the source from now on.</summary>
        internal static void PublishParams(BoxShapeParams p)
        {
            if (p == null) return;
            Params = p;
            PaintPublished = true;
        }

        /// <summary>The Options slider moved: the margin follows it unless the list file overrides it.</summary>
        internal static void SetMarginSetting(double setting) => Params.ApplySetting(setting);

        /// <summary>The paint list file: &lt;user data&gt;/ModsData/JunctionRules/paint-test.json (moved here from the paint system, JR-T-007b-r).</summary>
        internal static string ListPath()
        {
            string root;
            try { root = Colossal.PSI.Environment.EnvPath.kUserDataPath; }
            catch { root = UnityEngine.Application.persistentDataPath; }
            return Path.Combine(Path.Combine(Path.Combine(root, "ModsData"), "JunctionRules"), PaintTestConfig.FileName);
        }

        /// <summary>
        /// Mod load / tool start: read the paint list file (if any) and the Options margin, unless the paint system has
        /// already published its own read. Never throws (a bad file leaves the defaults, with a warning).
        /// </summary>
        internal static void LoadParams(string when)
        {
            if (PaintPublished) return;
            double setting = Mod.Settings != null ? Mod.Settings.YellowBoxMarkingMargin : Core.MarkingMargin.Default;
            try
            {
                string path = ListPath();
                PaintTestConfig cfg = File.Exists(path) ? PaintTestConfig.Parse(File.ReadAllText(path)) : null;
                Params = BoxShapeParams.From(cfg, setting);
                Mod.log.Info($"yellow boxes: shape parameters at {when}: {Params.Describe()}" + (cfg == null ? " (no paint list file)" : ""));
            }
            catch (Exception e)
            {
                Params = BoxShapeParams.From(null, setting);
                Mod.log.Warn($"yellow boxes: shape parameters at {when}: paint list not read ({e.GetType().Name}: {e.Message}); {Params.Describe()}");
            }
        }

        /// <summary>Junction box on the node / any exit box (or a legacy 0.4.0-0.4.2 box) on the building.</summary>
        internal static bool Has(EntityManager em, Entity e, bool junction)
        {
            if (e == Entity.Null || !em.Exists(e)) return false;
            return junction ? em.HasComponent<YellowBoxJunction>(e) : em.HasComponent<YellowBoxEntrance>(e);
        }

        /// <summary>0.4.3: this exit of the building has a box.</summary>
        internal static bool HasExit(EntityManager em, Entity building, ExitKey key)
        {
            if (!Has(em, building, false)) return false;
            return em.GetComponentData<YellowBoxEntrance>(building).Has(key);
        }

        /// <summary>Junction box on a node (0.4.3: also the junction where a building's own road meets the public road).</summary>
        internal static void Add(EntityManager em, Entity node, bool notify = true)
        {
            if (node == Entity.Null || !em.Exists(node) || Has(em, node, true)) return;
            em.AddComponentData(node, new YellowBoxJunction());
            Version++;
            if (notify) RaiseChanged();
        }

        /// <summary>
        /// 0.4.3: a box on one access of the building (keys: Core/BuildingExit.cs; JR-T-021: entrances too, ExitKind.Entrance).
        /// False when the building is full. A new component is marked EntrancesExplicit (nothing to migrate).
        /// </summary>
        internal static bool AddExit(EntityManager em, Entity building, ExitKey key, bool notify = true)
        {
            if (building == Entity.Null || !em.Exists(building)) return false;
            if (!em.HasComponent<YellowBoxEntrance>(building))
            {
                var c = new YellowBoxEntrance { m_Flags = (byte)BoxRecordFlags.EntrancesExplicit };
                c.m_Exits.Add(key);
                em.AddComponentData(building, c);
            }
            else
            {
                YellowBoxEntrance c = em.GetComponentData<YellowBoxEntrance>(building);
                if (c.Has(key)) return true;
                if (c.m_Exits.Length >= ExitRules.MaxBoxesPerBuilding) return false;
                c.m_Exits.Add(key);
                em.SetComponentData(building, c);
            }
            Version++;
            if (notify) RaiseChanged();
            return true;
        }

        /// <summary>0.4.3: takes one exit's box off the building; the component goes when no exit box is left.</summary>
        internal static void RemoveExit(EntityManager em, Entity building, ExitKey key, bool notify = true)
        {
            if (!Has(em, building, false)) return;
            YellowBoxEntrance c = em.GetComponentData<YellowBoxEntrance>(building);
            for (int i = c.m_Exits.Length - 1; i >= 0; i--) if (c.m_Exits[i].Equals(key)) c.m_Exits.RemoveAt(i);
            if (c.m_Exits.Length == 0) em.RemoveComponent<YellowBoxEntrance>(building);
            else em.SetComponentData(building, c);
            Version++;
            if (notify) RaiseChanged();
        }

        /// <summary>0.4.3: replaces a legacy (key-less) box's keys after migration (no notification: the paint system calls it).</summary>
        internal static void SetExits(EntityManager em, Entity building, ExitKey key)
        {
            if (!Has(em, building, false)) return;
            YellowBoxEntrance c = em.GetComponentData<YellowBoxEntrance>(building);
            c.m_Exits.Clear();
            c.m_Exits.Add(key);
            em.SetComponentData(building, c);
            Version++;
        }

        /// <summary>
        /// JR-T-021: the one-time migration of a building saved by 1.0.1 or older (record without EntrancesExplicit): every
        /// entrance box 1.0.1 derived for it becomes a saved entrance key, and lot-front marker keys that only marked the
        /// building as boxed are dropped once it has an entrance key (Core AccessRules.MigrateEntrances); then the flag is
        /// set, so it never runs again for that building (an entrance the owner removes later stays removed). Skipped (left
        /// for a later call) for a legacy key-less box (the paint's MigrateLegacy goes first) and while the game data shows
        /// the building no real car access at all (not loaded yet / nothing to derive). Logged; no change notification
        /// (what is drawn stays the same). Callers: the paint's load resolve, the driving rule's rebuild, the tool / API
        /// access lists. True when the record changed.
        /// </summary>
        internal static bool MigrateEntrances(EntityManager em, Entity building)
        {
            if (!Has(em, building, false)) return false;
            YellowBoxEntrance c = em.GetComponentData<YellowBoxEntrance>(building);
            if ((c.m_Flags & (byte)BoxRecordFlags.EntrancesExplicit) != 0 || c.Legacy) return false;
            if (!em.HasComponent<Game.Buildings.Building>(building) || em.HasComponent<Game.Common.Deleted>(building) || em.HasComponent<Game.Tools.Temp>(building)) return false;
            BoxFacts.Accesses(em, building, null, out var exits, out var entrances, out _, out _);
            bool real = entrances.Count > 0;
            foreach (var x in exits) real |= !x.IsFallback;
            if (!real) return false;
            var saved = new System.Collections.Generic.List<ExitKey>(c.m_Exits.Length);
            for (int i = 0; i < c.m_Exits.Length; i++) saved.Add(c.m_Exits[i]);
            var exitCands = new System.Collections.Generic.List<ExitCandidate>(exits.Count);
            foreach (var x in exits) exitCands.Add(x.C);
            var inCands = new System.Collections.Generic.List<ExitCandidate>(entrances.Count);
            foreach (var x in entrances) inCands.Add(x.C);
            EntranceMigration m = AccessRules.MigrateEntrances(saved, exitCands, inCands, ExitRules.MaxBoxesPerBuilding);
            c.m_Exits.Clear();
            foreach (ExitKey k in m.Keys) c.m_Exits.Add(k);
            c.m_Flags |= (byte)BoxRecordFlags.EntrancesExplicit;
            em.SetComponentData(building, c);
            if (!m.Changed && m.NotFitted == 0) return false;
            Version++;
            Mod.log.Info($"yellow boxes: building {building}: 1.0.1 entrance boxes made explicit: {m.Added} entrance key(s) saved, {m.MarkersDropped} lot-front marker key(s) dropped"
                + (m.NotFitted > 0 ? $", {m.NotFitted} entrance(s) NOT saved (the building already has {ExitRules.MaxBoxesPerBuilding} boxes)" : "")
                + $"; keys now: {string.Join(", ", m.Keys)}");
            return m.Changed;
        }

        /// <summary>Removes the box (tool click, or the paint system dropping a box whose target is no longer valid); building: all its exit boxes.</summary>
        internal static void Remove(EntityManager em, Entity e, bool junction, bool notify = true)
        {
            if (!Has(em, e, junction)) return;
            if (junction) em.RemoveComponent<YellowBoxJunction>(e);
            else em.RemoveComponent<YellowBoxEntrance>(e);
            Version++;
            if (notify) RaiseChanged();
        }

        private static void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception ex) { Mod.log.Error(ex, "yellow boxes: change notification failed"); }
        }
    }
}
