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
            // 0.4.7 (JR-T-013d, G6): no parking in the boxes. Its own try/catch: a failure here leaves the tool working.
            // Review F2: only when the version gate allows the copy (it writes the game's LaneOverlap buffers by the 1.6.4f1
            // parking code and its save guard runs a copy of the game's free-space code; pattern of PeoplePassSystem, Mod.cs).
            if (!Mod.Gate.CopyAllowed)
            {
                try { Mod.log.Info("yellow boxes: no parking in boxes not registered (version check did not allow the copy); parking in boxes stays as the game has it"); } catch { }
                return;
            }
            try
            {
                // ModificationEnd, ahead of the game's ParkingLaneDataSystem (after LaneOverlapSystem, Modification4B, and
                // FixParkingLocationSystem, Modification5): see Parking/BoxParkingSystem.cs.
                updateSystem.UpdateBefore<JunctionRules.Parking.BoxParkingSystem, Game.Pathfind.ParkingLaneDataSystem>(SystemUpdatePhase.ModificationEnd);
                // every save: ahead of the game's serializer, like jr-t-011's marking save guard
                updateSystem.UpdateBefore<JunctionRules.Parking.BoxParkingSaveGuardSystem>(SystemUpdatePhase.Serialize);
            }
            catch (Exception e)
            {
                try { Mod.log.Error(e, "yellow boxes: no-parking systems not registered; parking in boxes stays as the game has it"); } catch { }
            }
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
