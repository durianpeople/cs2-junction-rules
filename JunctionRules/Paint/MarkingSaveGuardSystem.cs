using Game;
using UnityEngine.Scripting;

namespace JunctionRules.Paint
{
    /// <summary>
    /// 0.4.4 (JR-T-011 part B): runs in SystemUpdatePhase.Serialize ahead of the game's SerializerSystem (registered with
    /// UpdateBefore like the game's own TrimPathsSystem, SystemOrder.cs 731-735; the phase runs only while a save is
    /// written, SaveGameSystem.cs 82). It takes the marking cuts / hides of the yellow boxes off the road entities so none
    /// of them is ever written into a save; the paint system puts them back the next frame. Does nothing when no marking is
    /// hidden. Never throws.
    /// </summary>
    public class MarkingSaveGuardSystem : GameSystemBase
    {
        [Preserve]
        protected override void OnUpdate()
        {
            YellowBoxPaintSystem.StripMarkingsForSave(EntityManager);
        }

        [Preserve]
        public MarkingSaveGuardSystem()
        {
        }
    }
}
