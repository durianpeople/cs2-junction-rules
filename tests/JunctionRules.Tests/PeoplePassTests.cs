using System;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>0.3.6-probe (JR-T-008): people and cyclists pass a vehicle standing on their crossing after the setting (default 2 s).</summary>
    internal static partial class Program
    {
        private static void PeoplePassTests()
        {
            // Setting.
            Check(PeoplePass.DefaultSeconds == 2f, "default 2 s (owner ruling 16 Nov 2026)");
            Check(PeoplePass.Sanitize(0f) == 0f && !PeoplePass.On(0f), "0 = off");
            Check(PeoplePass.Sanitize(-1f) == 0f && PeoplePass.Sanitize(float.NaN) == 0f, "negative / NaN = off");
            Check(PeoplePass.Sanitize(99f) == PeoplePass.MaxSeconds, "above the slider range clamped");
            Check(PeoplePass.On(0.5f) && PeoplePass.Sanitize(2f) == 2f, "inside the range kept");

            // On/off switch (second owner ruling 16 Nov): off = exact stock, whatever the slider says.
            Check(PeoplePass.Effective(true, 2f) == 2f, "switch on: the slider's wait");
            Check(PeoplePass.Effective(false, 2f) == 0f && PeoplePass.Effective(false, 30f) == 0f, "switch off: wait 0 (system does nothing)");
            Check(!PeoplePass.Due(0, 1000000, PeoplePass.Effective(false, 2f)), "switch off: a hold is never due, so no pass is applied and no counter moves");
            Check(PeoplePass.Effective(true, 0f) == 0f, "switch on, slider 0: also off");

            // Timing: 60 simulation frames per second; a person is updated every 16 frames (4/15 s).
            Check(!PeoplePass.Due(1000, 1000, 2f), "just started: not due");
            Check(!PeoplePass.Due(1000, 1000 + 112, 2f), "7 updates (1.87 s): not due");
            Check(PeoplePass.Due(1000, 1000 + 128, 2f), "8 updates (2.13 s): due");
            Check(PeoplePass.Due(1000, 1000 + 120, 2f), "exactly 2 s: due");
            Check(!PeoplePass.Due(1000, 1000 + 119, 2f), "just under 2 s: not due");
            Check(!PeoplePass.Due(0, 100000, 0f), "off: never due");
            Check(PeoplePass.Due(uint.MaxValue - 50, 100, 2f), "frame counter wrap: still counts the frames held");
            Check(PeoplePass.Due(1000, 1000 + 300, 5f) && !PeoplePass.Due(1000, 1000 + 290, 5f), "other settings scale (5 s = 300 frames)");

            // Standing vehicle.
            Check(PeoplePass.Standing(true, 0f) && PeoplePass.Standing(false, 0f), "speed 0 or no Moving: standing");
            Check(PeoplePass.Standing(true, 0.24f) && !PeoplePass.Standing(true, 0.25f), "0.5 m/s boundary");
            Check(!PeoplePass.Standing(true, 25f), "moving car (5 m/s): not a standing hold (the person waits as in the game)");

            // Overlap edge: the game clamps the target at the near edge; the pass moves it past the far edge.
            float s = 51f / 255f, e = 102f / 255f;
            Check(PeoplePass.PastOverlap(s, 1f, s, e, out float nx) && nx == e, "forward walk at the near edge: moved to the far edge");
            Check(!PeoplePass.PastOverlap(s - 0.02f, 1f, s, e, out _), "forward walk not at the edge (still walking up): untouched");
            Check(!PeoplePass.PastOverlap(e, 1f, s, e, out _), "forward walk already past: untouched");
            Check(PeoplePass.PastOverlap(e, 0f, s, e, out nx) && nx == s, "backward walk at the high edge: moved to the low edge");
            Check(!PeoplePass.PastOverlap(s, 0f, s, e, out _), "backward walk at the low edge (already through): untouched");
            Check(PeoplePass.PastOverlap(s, 0.3f, s, e, out nx) && nx == 0.3f, "never moved beyond the end of the person's own path on this lane");
            Check(!PeoplePass.PastOverlap(s, s, s, e, out _), "target already at the path end: nothing to do");
            Check(PeoplePass.PastOverlap(s + 0.2f / 255f, 1f, s, e, out _), "byte rounding tolerated");

            // Which overlaps count (the game's IterateLane filter) and the car-covers-overlap test.
            Check(PeoplePass.CountsAsCrossing(true, false) && !PeoplePass.CountsAsCrossing(true, true) && !PeoplePass.CountsAsCrossing(false, false), "road / track, not merges");
            Check(PeoplePass.Covers(0.4f, 0.6f, 0.5f, 0.55f) && PeoplePass.Covers(0.6f, 0.4f, 0.5f, 0.55f), "car over the crossing (either direction on its lane)");
            Check(!PeoplePass.Covers(0.1f, 0.3f, 0.5f, 0.55f), "car short of the crossing: not on it");
            Check(PeoplePass.Covers(0.3f, 0.5f, 0.5f, 0.55f), "touching counts, as in the game");

            // Cyclists.
            Check(PeoplePass.CyclistHeld(true, true, false, true, true), "cyclist on a pedestrian lane held by a standing car at a crossing");
            Check(!PeoplePass.CyclistHeld(false, true, false, true, true), "cyclist on a road lane: car queue stays stock");
            Check(!PeoplePass.CyclistHeld(true, true, true, true, true), "held by another bicycle: stock");
            Check(!PeoplePass.CyclistHeld(true, true, false, false, true), "car ahead on the same path (Continuing): stock");
            Check(!PeoplePass.CyclistHeld(true, true, false, true, false), "moving car: stock");
            Check(!PeoplePass.CyclistHeld(true, false, false, true, true), "no vehicle blocker: stock");

            // Log text.
            Check(PeoplePassCounts.Format(3, 1) == "; people passed blocking cars 3 (cyclists 1)", "log part");
            Check(PeoplePassCounts.Format(-1, -1) == "; people passed blocking cars n/a", "log part when not counted");
        }

        private static void PeoplePassRepoTests()
        {
            string src = Path.Combine(RepoRoot(), "JunctionRules");
            string settings = File.ReadAllText(Path.Combine(src, "Settings", "JunctionRulesSettings.cs"));
            Check(settings.Contains("PeoplePassSeconds = PeoplePass.DefaultSeconds;"), "setting defaults to PeoplePass.DefaultSeconds (2 s)");
            Check(settings.Contains("PeoplePassOn = true;") && settings.Contains("\"People pass a car that blocks them\""), "on/off switch 'People pass a car that blocks them', default on");
            Check(settings.IndexOf("public bool PeoplePassOn", StringComparison.Ordinal) < settings.IndexOf("public float PeoplePassSeconds", StringComparison.Ordinal), "switch shown right before the slider");
            Check(settings.Contains("[SettingsUIDisableByCondition(typeof(JunctionRulesSettings), nameof(PeoplePassSliderDisabled))]") && settings.Contains("public bool PeoplePassSliderDisabled() => !PeoplePassOn;"), "slider greyed out while the switch is off");
            Check(settings.Contains("PeoplePass.Effective(PeoplePassOn, PeoplePassSeconds)"), "effective wait = 0 while the switch is off");
            Check(settings.Contains("[SettingsUISlider(min = PeoplePass.MinSeconds, max = PeoplePass.MaxSeconds, step = PeoplePass.StepSeconds"), "setting is a slider in Options");
            Check(settings.Contains("\"People pass a car that blocks a crossing after (s)\""), "setting label as the brief names it");
            string mod = File.ReadAllText(Path.Combine(src, "Mod.cs"));
            Check(mod.Contains("updateSystem.UpdateAfter<PeoplePassSystem, HumanNavigationSystem>(SystemUpdatePhase.GameSimulation);"), "system runs after the game's HumanNavigationSystem");
            int gate = mod.IndexOf("if (Gate.CopyAllowed)", mod.IndexOf("0.3.6 (JR-T-008)", StringComparison.Ordinal), StringComparison.Ordinal);
            Check(gate > 0 && gate < mod.IndexOf("UpdateAfter<PeoplePassSystem", StringComparison.Ordinal), "registered only when the version gate allowed the copy");
            string sys = File.ReadAllText(Path.Combine(src, "Systems", "PeoplePassSystem.cs"));
            int eff = sys.IndexOf("PeoplePassEffectiveSeconds()", StringComparison.Ordinal);
            int ret = sys.IndexOf("return;", eff, StringComparison.Ordinal);
            int sched = sys.IndexOf(".Schedule(", eff, StringComparison.Ordinal);
            Check(eff > 0 && sys.Contains("if (seconds <= 0f)") && ret > 0 && ret < sched, "switch off: OnUpdate returns before scheduling anything (no pass, counter unchanged)");
            Check(sys.Contains("SetSharedComponentFilter(slice)") && sys.Contains("new UpdateFrame(frame % 16)"), "same UpdateFrame slice as the game's navigation");
            Check(sys.Contains("CreatureLaneFlags.WaitSignal") , "people waiting at a red signal are never passed (signal holds stay the game's)");
            Check(sys.Contains("CarLaneFlags.IgnoreBlocker"), "cyclists: the game's own IgnoreBlocker creep");
            Check(!sys.Contains("JunctionRules.Vanilla") && !sys.Contains("JRCarNavigationSystem"), "no copy type named (no new vanilla copy)");
            string probes = File.ReadAllText(Path.Combine(src, "Systems", "NavTimingProbeSystems.cs"));
            Check(probes.Contains("CloseWindow() + ClosePassWindow()"), "timing log carries the people-pass counter (both probes)");
            string kit = File.ReadAllText(Path.Combine(RepoRoot(), "PROBE-STAGE3-PAINT.md"));
            Check(kit.Contains("## 15. 0.3.6") && kit.Contains("People pass a car that blocks a crossing after (s)") && kit.Contains("people passed blocking cars"), "probe note section 15 names the setting and the log counter");
        }
    }
}
