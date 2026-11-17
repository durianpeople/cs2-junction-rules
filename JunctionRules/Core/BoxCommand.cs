namespace JunctionRules.Core
{
    // 0.4.13-probe (JR-T-019): the ONE decision of what a yellow box click / API call does, shared by the Ctrl+Y tool
    // (Boxes/YellowBoxToolSystem -> Boxes/BoxTargeting.Apply with BoxRequest.Toggle) and JunctionRules.Api.JunctionRulesApi
    // (Add / Remove / Preview -> the same BoxTargeting.Apply / Decide). The refusal reasons are the tool's own texts: a
    // target's 'why' from Core/BoxSelection (or the shape solver's "no box shape can be built here: ..."), and the fixed
    // texts below, which the tool logs word for word. Game-independent, unit-tested (tests ApiTests).

    /// <summary>What the caller asks for: the tool's click toggles; the API adds or removes.</summary>
    public enum BoxRequest { Toggle = 0, Add = 1, Remove = 2 }

    /// <summary>What to write (Boxes/BoxStore). None = refused (see BoxDecision.Error / Reason).</summary>
    public enum BoxStep
    {
        None = 0,
        AddJunction,
        RemoveJunction,
        /// <summary>One saved exit key on the building.</summary>
        AddExit,
        RemoveExit,
        /// <summary>Tool only: a white entrance clicked = the building's lot-front marker key (its entrances come with it).</summary>
        AddBuildingMarker,
        /// <summary>Tool only: an orange entrance clicked = ALL the building's boxes.</summary>
        RemoveBuilding,
        /// <summary>A saved lot-front fallback key (marker / old fallback box) taken off.</summary>
        RemoveFallbackKeys,
    }

    /// <summary>The target kinds the decision needs (Boxes/BoxTargeting.Kind maps onto it).</summary>
    public enum BoxTargetKind { None = 0, Junction = 1, Exit = 2 }

    public struct BoxDecision
    {
        public BoxStep Step;
        /// <summary>Null when Step != None; else an API error code (BoxApiErrors).</summary>
        public string Error;
        /// <summary>Plain words, the tool's text.</summary>
        public string Reason;
        public bool Add => Step == BoxStep.AddJunction || Step == BoxStep.AddExit || Step == BoxStep.AddBuildingMarker;
    }

    /// <summary>API error codes (YBOX_API_CONTRACT v1).</summary>
    public static class BoxApiErrors
    {
        public const string NotReady = "not_ready";
        public const string BadTarget = "bad_target";
        public const string NotFound = "not_found";
        public const string NotBoxable = "not_boxable";
        public const string AlreadyBoxed = "already_boxed";
        public const string NotBoxed = "not_boxed";
        public const string AmbiguousExit = "ambiguous_exit";
        public const string EntranceDerived = "entrance_derived";
        public const string Internal = "internal";
    }

    public static class BoxCommand
    {
        /// <summary>The tool's text for a full building (Boxes/BoxStore.AddExit false): "the building already has {ExitRules.MaxBoxesPerBuilding} (10) exit boxes".</summary>
        public static string FullReason => $"the building already has {ExitRules.MaxBoxesPerBuilding} exit boxes";

        /// <summary>The tool's text when a white entrance's building has no road edge for its marker key.</summary>
        public const string NoMarkerEdgeReason = "the building has no road edge for its marker key";

        /// <summary>API: entrance boxes are derived from the building's exits at load; never added or removed alone.</summary>
        public const string EntranceDerivedReason = "entrance boxes come with the building (derived at load from its boxed exits, never saved): add or remove one of the building's exits instead";

        /// <summary>The tool's prefix for a shape failure (YellowBoxToolSystem F3: valid target, but no shape).</summary>
        public const string NoShapePrefix = "no box shape can be built here: ";

        /// <summary>
        /// What happens. Toggle = the tool's click (BoxSelection.Toggle: a box is always removable, a new one only on a valid
        /// target; an entrance boxes / unboxes its whole building; a fallback key is removable only). Add / Remove = the API:
        /// the same rules, but an explicit refusal when the target already has / has no box, and an entrance is refused
        /// (entrance_derived): the API acts on exits only.
        /// </summary>
        public static BoxDecision Decide(BoxRequest req, BoxTargetKind kind, bool hasBox, bool valid, string why, bool entrance, bool fallback)
        {
            if (kind == BoxTargetKind.None) return Refuse(BoxApiErrors.NotFound, string.IsNullOrEmpty(why) ? "nothing to box here" : why);
            if (req != BoxRequest.Toggle && kind == BoxTargetKind.Exit && entrance) return Refuse(BoxApiErrors.EntranceDerived, EntranceDerivedReason);
            ToggleAction act = BoxSelection.Toggle(hasBox, valid);
            if (req == BoxRequest.Add)
            {
                if (hasBox) return Refuse(BoxApiErrors.AlreadyBoxed, "already has a box");
                if (act != ToggleAction.Add) return Refuse(BoxApiErrors.NotBoxable, why ?? "not selectable");
            }
            else if (req == BoxRequest.Remove)
            {
                if (!hasBox) return Refuse(BoxApiErrors.NotBoxed, "has no box");
            }
            else if (act == ToggleAction.None) return Refuse(BoxApiErrors.NotBoxable, why ?? "not selectable");
            bool add = req == BoxRequest.Add || (req == BoxRequest.Toggle && act == ToggleAction.Add);
            if (kind == BoxTargetKind.Junction) return Do(add ? BoxStep.AddJunction : BoxStep.RemoveJunction);
            if (entrance) return Do(add ? BoxStep.AddBuildingMarker : BoxStep.RemoveBuilding);   // Toggle only (above)
            if (fallback) return add ? Refuse(BoxApiErrors.NotBoxable, why ?? "not selectable") : Do(BoxStep.RemoveFallbackKeys);
            return Do(add ? BoxStep.AddExit : BoxStep.RemoveExit);
        }

        private static BoxDecision Refuse(string code, string reason) => new BoxDecision { Step = BoxStep.None, Error = code, Reason = reason };
        private static BoxDecision Do(BoxStep s) => new BoxDecision { Step = s };
    }
}
