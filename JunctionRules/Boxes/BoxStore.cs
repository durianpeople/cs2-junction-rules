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

        /// <summary>0.4.3: a box on one exit of the building (keys: Core/BuildingExit.cs). False when the building is full.</summary>
        internal static bool AddExit(EntityManager em, Entity building, ExitKey key, bool notify = true)
        {
            if (building == Entity.Null || !em.Exists(building)) return false;
            if (key.ExitKind == ExitKind.Entrance) return false;   // JR-T-014c (G9): entrance boxes are derived, never saved
            if (!em.HasComponent<YellowBoxEntrance>(building))
            {
                var c = new YellowBoxEntrance();
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
