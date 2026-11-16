// Junction Rules 0.4.0-probe (JR-T-007): yellow box tool button, top left of the city screen.
// Hand-written UI module (no bundler): uses the game's globals the way built mods do (window.React, window["cs2/api"],
// window["cs2/ui"]; see e.g. Traffic.mjs: Button variant "floating" appended to "GameTopLeft").
// C# side: JunctionRules/Boxes/YellowBoxUISystem.cs (trigger "JunctionRules"."toggleBoxTool", value "boxToolActive").
const React = window.React;
const Api = window["cs2/api"];
const Ui = window["cs2/ui"];

const ICON = "data:image/svg+xml;utf8," + encodeURIComponent(
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">' +
    '<rect x="4" y="4" width="24" height="24" fill="none" stroke="#f5c400" stroke-width="2.5"/>' +
    '<path d="M4 12 L12 4 M4 20 L20 4 M4 28 L28 4 M12 28 L28 12 M20 28 L28 20 M12 4 L28 20 M4 4 L28 28 M4 12 L20 28 M4 20 L12 28 M20 4 L28 12" stroke="#f5c400" stroke-width="1.5"/>' +
    '</svg>');

const activeBinding = Api && Api.bindValue ? Api.bindValue("JunctionRules", "boxToolActive", false) : null;

function YellowBoxButton() {
    const active = activeBinding && Api.useValue ? Api.useValue(activeBinding) : false;
    const onSelect = () => { if (Api && Api.trigger) Api.trigger("JunctionRules", "toggleBoxTool"); };
    if (Ui && Ui.Button) {
        return React.createElement(Ui.Button, {
            src: ICON,
            variant: "floating",
            selected: !!active,
            tooltipLabel: "Yellow box tool (Junction Rules)",
            onSelect: onSelect,
        });
    }
    return React.createElement("button", { onClick: onSelect, title: "Yellow box tool (Junction Rules)" }, "YB");
}

export const register = (moduleRegistry) => {
    if (!React || !moduleRegistry || !moduleRegistry.append) {
        console.error("[JunctionRules] cannot register the yellow box tool button");
        return;
    }
    moduleRegistry.append("GameTopLeft", YellowBoxButton);
};

export const hasCSS = false;
export default register;
