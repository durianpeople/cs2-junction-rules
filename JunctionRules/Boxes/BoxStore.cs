using System;
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
        /// <summary>Raised after the tool added or removed a box (the paint system redraws at once).</summary>
        internal static event Action Changed;

        /// <summary>Exit box length along the road in metres (the paint list's "entranceLength" / "exitLength"; default 8).</summary>
        internal static double EntranceLength = 8.0;
        /// <summary>0.4.2: shape parameters the paint system publishes for the tool's hover outline (same shape as the paint).</summary>
        internal static double KerbInset = 0.0;
        internal static double MarkingMargin = JunctionRules.Core.MarkingMargin.Default;

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
            if (notify) RaiseChanged();
        }

        /// <summary>0.4.3: a box on one exit of the building (keys: Core/BuildingExit.cs). False when the building is full.</summary>
        internal static bool AddExit(EntityManager em, Entity building, ExitKey key, bool notify = true)
        {
            if (building == Entity.Null || !em.Exists(building)) return false;
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
        }

        /// <summary>Removes the box (tool click, or the paint system dropping a box whose target is no longer valid); building: all its exit boxes.</summary>
        internal static void Remove(EntityManager em, Entity e, bool junction, bool notify = true)
        {
            if (!Has(em, e, junction)) return;
            if (junction) em.RemoveComponent<YellowBoxJunction>(e);
            else em.RemoveComponent<YellowBoxEntrance>(e);
            if (notify) RaiseChanged();
        }

        private static void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception ex) { Mod.log.Error(ex, "yellow boxes: change notification failed"); }
        }
    }
}
