using System;
using System.Runtime.CompilerServices;
using Game;

namespace JunctionRules.Paint
{
    /// <summary>
    /// The only place outside JunctionRules/Paint/ that the paint test's system type is named from (Mod.OnLoad calls
    /// these inside its own try/catch; NoInlining keeps a load failure of the paint types inside the call).
    /// The settings call the static Request*/ApplySettings methods through <see cref="PaintControl"/>.
    /// </summary>
    internal static class PaintRegistration
    {
        /// <summary>Registers the paint system in PreCulling (after Game.Net.GeometrySystem in Modification4, while Updated tags still exist). It starts disabled unless the setting is on.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Register(UpdateSystem updateSystem)
        {
            updateSystem.UpdateAt<YellowBoxPaintSystem>(SystemUpdatePhase.PreCulling);
            // 0.4.4 (JR-T-011 B): takes the marking cuts / hides off before every save (Paint/MarkingSaveGuardSystem.cs)
            updateSystem.UpdateBefore<MarkingSaveGuardSystem>(SystemUpdatePhase.Serialize);
        }

        /// <summary>After a failed registration: makes sure an already created paint system is off. Never throws.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void DisableLeftover(Unity.Entities.World world)
        {
            try
            {
                YellowBoxPaintSystem.Fail("registering the paint system", null);
                var s = world?.GetExistingSystemManaged<YellowBoxPaintSystem>();
                if (s != null) s.Enabled = false;
            }
            catch (Exception e)
            {
                Mod.log.Error(e, "switching off the yellow box paint test after its failed registration failed");
            }
        }
    }

    /// <summary>Settings-side entry points: every call is wrapped so a settings change can never throw into the options UI.</summary>
    internal static class PaintControl
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ApplySettings() { try { YellowBoxPaintSystem.ApplySettings(); } catch (Exception e) { Log(e); } }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Refresh() { try { YellowBoxPaintSystem.RequestReload(); } catch (Exception e) { Log(e); } }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void AddCameraJunction() { try { YellowBoxPaintSystem.RequestAddCameraJunction(); } catch (Exception e) { Log(e); } }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void LogCost() { try { YellowBoxPaintSystem.RequestCostLog(); } catch (Exception e) { Log(e); } }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string Status()
        {
            try { return YellowBoxPaintSystem.StatusText; }
            catch (Exception e) { return "unavailable: " + e.GetType().Name; }
        }

        private static void Log(Exception e)
        {
            try { Mod.log.Error(e, "yellow box paint test control failed"); } catch { }
        }
    }
}
