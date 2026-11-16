using System;
using Unity.Entities;

namespace JunctionRules.Boxes
{
    /// <summary>
    /// 0.4.0-probe (JR-T-007): the one place that adds or removes a saved yellow box (our own two components, nothing
    /// else), and the hand-over between the tool and the paint system so neither names the other: the tool calls
    /// <see cref="Add"/> / <see cref="Remove"/> and the paint system listens on <see cref="Changed"/>; the paint system
    /// publishes its entrance length here for the tool's glow. Main thread only.
    /// </summary>
    internal static class BoxStore
    {
        /// <summary>Raised after the tool added or removed a box (the paint system redraws at once).</summary>
        internal static event Action Changed;

        /// <summary>Entrance box length along the road in metres (the paint list's "entranceLength"; default 8).</summary>
        internal static double EntranceLength = 8.0;

        internal static bool Has(EntityManager em, Entity e, bool junction)
        {
            if (e == Entity.Null || !em.Exists(e)) return false;
            return junction ? em.HasComponent<YellowBoxJunction>(e) : em.HasComponent<YellowBoxEntrance>(e);
        }

        internal static void Add(EntityManager em, Entity e, bool junction)
        {
            if (e == Entity.Null || !em.Exists(e) || Has(em, e, junction)) return;
            if (junction) em.AddComponentData(e, new YellowBoxJunction());
            else em.AddComponentData(e, new YellowBoxEntrance());
            RaiseChanged();
        }

        /// <summary>Removes the box (tool click, or the paint system dropping a box whose target is no longer valid).</summary>
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
