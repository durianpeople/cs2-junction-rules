using System;
using System.Runtime.CompilerServices;
using Game;

namespace JunctionRules.Boxes
{
    /// <summary>
    /// 0.4.0-probe (JR-T-007): registers the yellow box tool and its UI system (Mod.OnLoad calls this inside its own
    /// try/catch, after the paint system). Both stay idle until the owner opens the tool: the tool is a disabled
    /// ToolBaseSystem until it is the active tool; the UI system only serves two bindings and checks one shortcut.
    /// </summary>
    internal static class BoxRegistration
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Register(UpdateSystem updateSystem)
        {
            updateSystem.UpdateAt<YellowBoxToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<YellowBoxUISystem>(SystemUpdatePhase.UIUpdate);
        }
    }

    /// <summary>Settings-side entry point (never throws into the options UI).</summary>
    internal static class BoxToolControl
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Toggle()
        {
            try { YellowBoxUISystem.ToggleFromSettings(); }
            catch (Exception e) { try { Mod.log.Error(e, "yellow box tool: toggle failed"); } catch { } }
        }
    }
}
