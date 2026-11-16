using Game;
using UnityEngine.Scripting;

namespace JunctionRules.Paint
{
    /// <summary>
    /// JR-T-013e (G2): runs the marking hide (Paint/MarkingHider.Run) in SystemUpdatePhase.PreCulling BEFORE the game's
    /// PreCullingSystem (PaintRegistration: UpdateBefore&lt;MarkingRenderSystem, PreCullingSystem&gt;, like the game's own
    /// UpdateBefore&lt;CameraUpdateSystem&gt;, SystemOrder.cs 632-634). PreCullingSystem picks up BatchesUpdated in that same
    /// frame (PreCullingSystem.cs 2428-2436, 652, 1895) before PrepareCleanUp / CleanUp take the tag off (end of MainLoop:
    /// SystemOrder.cs 50-54, PrepareCleanUpSystem.cs 29-40). The paint system itself stays after PreCullingSystem (it
    /// reads Updated tags) and only posts requests. Idle cost: one null check and two flags. Never throws.
    /// </summary>
    public class MarkingRenderSystem : GameSystemBase
    {
        [Preserve]
        protected override void OnUpdate()
        {
            YellowBoxPaintSystem.RunMarkings(EntityManager);
        }

        [Preserve]
        public MarkingRenderSystem()
        {
        }
    }
}
