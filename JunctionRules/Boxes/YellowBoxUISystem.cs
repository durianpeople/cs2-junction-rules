using System;
using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using Game;
using Game.Input;
using Game.Tools;
using Game.UI;
using UnityEngine.Scripting;

namespace JunctionRules.Boxes
{
    /// <summary>
    /// 0.4.0-probe (JR-T-007): switches the yellow box tool on and off: the toolbar button (UI module JunctionRules.mjs:
    /// trigger "JunctionRules"/"toggleBoxTool", reads "JunctionRules"/"boxToolActive"), the rebindable keyboard shortcut
    /// (setting "Yellow box tool", default Ctrl+Y; Traffic.UISystems/ModUISystem.cs 404-414 is the same pattern) and the
    /// Options button. The shortcut is enabled only in a city or the editor (Traffic: OnGameLoadingComplete).
    /// </summary>
    public partial class YellowBoxUISystem : UISystemBase
    {
        public const string kGroup = "JunctionRules";

        private static YellowBoxUISystem s_Instance;
        private ToolSystem m_ToolSystem;
        private DefaultToolSystem m_DefaultTool;
        private YellowBoxToolSystem m_Tool;
        private ProxyAction m_ToggleAction;
        private bool m_InCity;

        /// <summary>0.4.13 (JR-T-019): a city (or the editor) is loaded (the API's readiness check).</summary>
        internal static bool InCity => s_Instance != null && s_Instance.m_InCity;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            s_Instance = this;
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_DefaultTool = World.GetOrCreateSystemManaged<DefaultToolSystem>();
            m_Tool = World.GetOrCreateSystemManaged<YellowBoxToolSystem>();
            try { m_ToggleAction = Mod.Settings?.GetAction(Settings.JunctionRulesSettings.kToggleBoxToolAction); }
            catch (Exception e) { Mod.log.Warn("yellow box tool shortcut unavailable: " + e.GetType().Name + ": " + e.Message); }
            if (m_ToggleAction != null) m_ToggleAction.shouldBeEnabled = false;
            AddBinding(new TriggerBinding(kGroup, "toggleBoxTool", Toggle));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "boxToolActive", () => m_ToolSystem != null && m_ToolSystem.activeTool == m_Tool));
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
            base.OnDestroy();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_InCity = mode.IsGameOrEditor();
            if (m_ToggleAction != null) m_ToggleAction.shouldBeEnabled = m_InCity;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            base.OnUpdate();
            if (m_ToggleAction != null && m_ToggleAction.WasPerformedThisFrame()) Toggle();
        }

        /// <summary>Options button / toolbar button / shortcut. Never throws.</summary>
        internal static void ToggleFromSettings()
        {
            try { s_Instance?.Toggle(); }
            catch (Exception e) { Mod.log.Error(e, "yellow box tool: toggling failed"); }
        }

        private void Toggle()
        {
            if (!m_InCity) { Mod.log.Info("yellow box tool: open a city first"); return; }
            m_ToolSystem.activeTool = m_ToolSystem.activeTool == m_Tool ? (ToolBaseSystem)m_DefaultTool : m_Tool;
        }
    }
}
