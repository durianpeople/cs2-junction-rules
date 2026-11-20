using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.13-probe (JR-T-019): JunctionRulesApi v1 (YBOX_API_CONTRACT.md): target / filter parsing, the result JSON shape,
    /// the refusal mapping (the very texts the Ctrl+Y tool shows), exit picking, the not_ready rule, the JSON reader / writer,
    /// and repo checks that the tool and the API run ONE shared path (Boxes/BoxTargeting.cs + Core/BoxCommand.Decide).
    /// </summary>
    internal static partial class Program
    {
        // ---- target parsing (every form + bad ones)

        private static void ApiTargetParseTests()
        {
            Check(BoxApi.ParseTarget("{\"node\":123}", out ApiTarget t, out string why) && t.Form == ApiTargetForm.Node && t.Index == 123 && t.Version == -1 && why == null, "target: {node:I}");
            Check(BoxApi.ParseTarget("{\"node\":{\"index\":5,\"version\":2}}", out t, out _) && t.Form == ApiTargetForm.Node && t.Index == 5 && t.Version == 2, "target: {node:{index,version}}");
            Check(BoxApi.ParseTarget("{\"node\":{\"index\":5}}", out t, out _) && t.Index == 5 && t.Version == -1, "target: {node:{index}} (version optional)");
            Check(BoxApi.ParseTarget("{\"building\":77}", out t, out _) && t.Form == ApiTargetForm.Building && t.Index == 77 && t.Exit == -1, "target: {building:I}");
            Check(BoxApi.ParseTarget("{\"building\":77,\"exit\":2}", out t, out _) && t.Exit == 2, "target: {building:I, exit:k}");
            Check(BoxApi.ParseTarget("{\"building\":{\"index\":77,\"version\":1},\"exit\":0}", out t, out _) && t.Index == 77 && t.Version == 1 && t.Exit == 0, "target: {building:{..}, exit:0}");
            Check(BoxApi.ParseTarget("{\"building\":77,\"exit\":null}", out t, out _) && t.Exit == -1, "target: exit null = not given");
            Check(BoxApi.ParseTarget(" { \"x\" : 1092.7 , \"z\" : -81.5 } ", out t, out _) && t.Form == ApiTargetForm.Point && Math.Abs(t.X - 1092.7) < 1e-9 && Math.Abs(t.Z + 81.5) < 1e-9, "target: {x,z} (whitespace)");
            Check(BoxApi.ParseTarget("{\"x\":1e2,\"z\":-0.5E1}", out t, out _) && t.X == 100 && t.Z == -5, "target: exponent numbers");
            Check(t.ToString() == "point (100.00, -5.00)", "target ToString: " + t);

            string[] bad =
            {
                null, "", "nope", "[]", "42", "{}", "{\"node\":1,\"building\":2}", "{\"node\":1,\"x\":1,\"z\":2}", "{\"x\":1}", "{\"z\":1}",
                "{\"x\":\"1\",\"z\":2}", "{\"node\":0}", "{\"node\":-3}", "{\"node\":1.5}", "{\"node\":\"12\"}", "{\"node\":{\"version\":1}}",
                "{\"node\":{\"index\":3,\"version\":0}}", "{\"node\":3,\"exit\":1}", "{\"building\":3,\"exit\":-1}", "{\"building\":3,\"exit\":1.5}",
                "{\"building\":3,\"exit\":\"a\"}", "{\"node\":1", "{\"node\":1}x", "{\"node\":99999999999}",
            };
            foreach (string b in bad)
            {
                bool ok = BoxApi.ParseTarget(b, out _, out string reason);
                Check(!ok && !string.IsNullOrEmpty(reason), "bad target refused with a reason: " + (b ?? "null") + " -> " + reason);
            }
        }

        private static void ApiFilterParseTests()
        {
            Check(BoxApi.ParseFilter(null, out ApiFilter f, out _) && f.Form == ApiFilterForm.All, "filter: null = all");
            Check(BoxApi.ParseFilter("", out f, out _) && f.Form == ApiFilterForm.All, "filter: empty = all");
            Check(BoxApi.ParseFilter("{}", out f, out _) && f.Form == ApiFilterForm.All && f.Passes(1e6, -1e6), "filter: {} = all");
            Check(BoxApi.ParseFilter("{\"x\":10,\"z\":20,\"radius\":5}", out f, out _) && f.Form == ApiFilterForm.Radius, "filter: radius");
            Check(f.Passes(13, 24) && !f.Passes(14, 24) && f.Passes(10, 25), "filter: radius inclusive edge, outside refused");
            Check(BoxApi.ParseFilter("{\"minX\":0,\"minZ\":0,\"maxX\":10,\"maxZ\":5}", out f, out _) && f.Form == ApiFilterForm.Box, "filter: bbox");
            Check(f.Passes(0, 0) && f.Passes(10, 5) && !f.Passes(10.01, 5) && !f.Passes(5, -0.01), "filter: bbox edges inclusive");
            foreach (string b in new[] { "[]", "{\"x\":1,\"z\":2}", "{\"x\":1,\"z\":2,\"radius\":-1}", "{\"minX\":0}", "{\"minX\":5,\"minZ\":0,\"maxX\":1,\"maxZ\":1}", "{\"x\":1,\"z\":1,\"radius\":1,\"minX\":0}", "{" })
                Check(!BoxApi.ParseFilter(b, out _, out string r) && !string.IsNullOrEmpty(r), "bad filter refused: " + b);
        }

        // ---- the shared decision: refusal mapping, same texts as the tool

        private static void ApiDecisionTests()
        {
            const string highway = "highway";
            // tool click on an invalid target and API add: the SAME reason text (the target's 'why')
            BoxDecision toolNo = BoxCommand.Decide(BoxRequest.Toggle, BoxTargetKind.Junction, false, false, highway, false);
            BoxDecision apiNo = BoxCommand.Decide(BoxRequest.Add, BoxTargetKind.Junction, false, false, highway, false);
            Check(toolNo.Step == BoxStep.None && apiNo.Step == BoxStep.None, "invalid junction: tool click and API add both refuse");
            Check(apiNo.Error == BoxApiErrors.NotBoxable && apiNo.Reason == highway && toolNo.Reason == apiNo.Reason, "invalid junction: not_boxable with the tool's text: " + apiNo.Reason);
            string noShape = BoxCommand.NoShapePrefix + "corner arm 3 - arm 1 do not meet";
            BoxDecision shapeNo = BoxCommand.Decide(BoxRequest.Add, BoxTargetKind.Junction, false, false, noShape, false);
            Check(shapeNo.Reason == noShape && noShape.StartsWith("no box shape can be built here: "), "shape failure: the tool's F3 text word for word");
            // every BoxSelection 'why' maps through unchanged
            var facts = new JunctionFacts { Exists = true, IsRoadNode = true, RoadArms = 2 };
            BoxSelection.JunctionOk(facts, out string why2);
            Check(BoxCommand.Decide(BoxRequest.Add, BoxTargetKind.Junction, false, false, why2, false).Reason == why2 && why2 == "a mid-road node, not a junction", "selection why passes through: " + why2);
            var ef = new EntranceFacts { Exists = true, IsBuilding = true, HasRoadEdge = true, RoadIsRoad = true, RoadHighway = true, AccessInsideEdge = true };
            BoxSelection.ExitOk(ef, out string why3);
            Check(BoxCommand.Decide(BoxRequest.Add, BoxTargetKind.Exit, false, false, why3, false).Reason == "exit on a highway", "exit why passes through: " + why3);

            // adds / removes
            Check(BoxCommand.Decide(BoxRequest.Add, BoxTargetKind.Junction, false, true, null, false).Step == BoxStep.AddJunction, "add: valid junction");
            Check(BoxCommand.Decide(BoxRequest.Add, BoxTargetKind.Exit, false, true, null, false).Step == BoxStep.AddExit, "add: valid exit");
            BoxDecision already = BoxCommand.Decide(BoxRequest.Add, BoxTargetKind.Junction, true, true, null, false);
            Check(already.Step == BoxStep.None && already.Error == BoxApiErrors.AlreadyBoxed, "add: already_boxed");
            BoxDecision notBoxed = BoxCommand.Decide(BoxRequest.Remove, BoxTargetKind.Exit, false, true, null, false);
            Check(notBoxed.Step == BoxStep.None && notBoxed.Error == BoxApiErrors.NotBoxed, "remove: not_boxed");
            Check(BoxCommand.Decide(BoxRequest.Remove, BoxTargetKind.Junction, true, false, highway, false).Step == BoxStep.RemoveJunction, "remove: a box on a target no longer valid is removable (tool rule)");
            Check(BoxCommand.Decide(BoxRequest.Remove, BoxTargetKind.Exit, true, true, null, false).Step == BoxStep.RemoveExit, "remove: exit");
            // JR-T-021: an entrance is an access like an exit for the tool and the API (its own key; no entrance_derived)
            Check(BoxCommand.Decide(BoxRequest.Add, BoxTargetKind.Exit, false, true, null, false).Step == BoxStep.AddExit
                  && BoxCommand.Decide(BoxRequest.Remove, BoxTargetKind.Exit, true, true, null, false).Step == BoxStep.RemoveExit, "entrance: API add / remove = its own key (no entrance_derived)");
            Check(BoxCommand.Decide(BoxRequest.Toggle, BoxTargetKind.Exit, false, true, null, false).Step == BoxStep.AddExit
                  && BoxCommand.Decide(BoxRequest.Toggle, BoxTargetKind.Exit, true, true, null, false).Step == BoxStep.RemoveExit, "entrance: tool toggles only its own key (never the whole building)");
            Check(BoxCommand.Decide(BoxRequest.Toggle, BoxTargetKind.Exit, true, false, "no real car exit", true).Step == BoxStep.RemoveFallbackKeys, "tool: fallback key removable");
            Check(BoxCommand.Decide(BoxRequest.Remove, BoxTargetKind.Exit, true, false, "no real car exit", true).Step == BoxStep.RemoveFallbackKeys, "API: fallback key removable");
            Check(BoxCommand.Decide(BoxRequest.Add, BoxTargetKind.Exit, false, false, "no real car exit", true).Error == BoxApiErrors.NotBoxable, "API: fallback never added");
            // the tool's toggle matrix is BoxSelection.Toggle's
            foreach (bool has in new[] { false, true })
                foreach (bool valid in new[] { false, true })
                {
                    ToggleAction a = BoxSelection.Toggle(has, valid);
                    BoxDecision d = BoxCommand.Decide(BoxRequest.Toggle, BoxTargetKind.Junction, has, valid, "w", false);
                    BoxStep want = a == ToggleAction.Add ? BoxStep.AddJunction : a == ToggleAction.Remove ? BoxStep.RemoveJunction : BoxStep.None;
                    Check(d.Step == want, $"toggle matrix has={has} valid={valid}: {d.Step}");
                }
            Check(BoxCommand.Decide(BoxRequest.Add, BoxTargetKind.None, false, false, null, false).Error == BoxApiErrors.NotFound, "no target kind = not_found");
            Check(BoxCommand.FullReason == "the building already has " + ExitRules.MaxBoxesPerBuilding + " exit boxes", "full building text = the tool's");
        }

        private static void ApiExitPickTests()
        {
            ApiExitChoice C(int i, string role) => new ApiExitChoice { Exit = i, Role = role, Key = "k" + i };
            int k = BoxApi.PickExit(-1, new[] { C(0, "exit"), C(1, "entrance") }, out string e, out _);
            Check(k == 0 && e == null, "pick: the only exit (entrances ignored)");
            k = BoxApi.PickExit(-1, new[] { C(0, "exit"), C(1, "two-way access") }, out e, out string r);
            Check(k == -1 && e == BoxApiErrors.AmbiguousExit && r.Contains("2 car exits"), "pick: several exits = ambiguous_exit: " + r);
            k = BoxApi.PickExit(-1, new[] { C(0, "entrance") }, out e, out r);
            Check(k == 0 && e == null, "pick (JR-T-021): no exit, one entrance = that entrance");
            k = BoxApi.PickExit(-1, new[] { C(0, "entrance"), C(1, "entrance") }, out e, out r);
            Check(k == -1 && e == BoxApiErrors.AmbiguousExit && r.Contains("2 car entrances"), "pick (JR-T-021): two entrances, no exit = ambiguous_exit: " + r);
            k = BoxApi.PickExit(-1, new ApiExitChoice[0], out e, out r);
            Check(k == -1 && e == BoxApiErrors.NotFound, "pick: no access at all = not_found");
            k = BoxApi.PickExit(1, new[] { C(0, "exit"), C(1, "entrance") }, out e, out _);
            Check(k == 1 && e == null, "pick: an explicit index (an entrance is boxable on its own, JR-T-021)");
            k = BoxApi.PickExit(5, new[] { C(0, "exit") }, out e, out r);
            Check(k == -1 && e == BoxApiErrors.NotFound && r.Contains("0..0"), "pick: index out of range = not_found: " + r);
            k = BoxApi.PickExit(-1, new[] { C(0, "junction") }, out e, out _);
            Check(k == 0, "pick: a junction-kind exit counts as the exit");
        }

        // ---- result JSON shape

        private static void ApiResultShapeTests()
        {
            string err = BoxApi.Error("0.4.13-probe", "add", BoxApiErrors.NotBoxable, "highway \"A1\"");
            Check(Json.TryParse(err, out JsonValue v, out _) && v.IsObject, "error JSON parses");
            Check(v.Get("ok").Kind == JsonKind.Bool && !v.Get("ok").Bool && v.Get("apiVersion").Number == 1 && v.Get("modVersion").String == "0.4.13-probe"
                  && v.Get("action").String == "add" && v.Get("error").String == "not_boxable" && v.Get("reason").String == "highway \"A1\"", "error JSON: ok/apiVersion/modVersion/action/error/reason");

            var ring = new List<V3> { new V3(0, 1, 0), new V3(4, 1, 0), new V3(4, 1, 3), new V3(0, 1, 3) };
            var r = new ApiTargetResult
            {
                Ref = new ApiBoxRef(ApiBoxKind.BuildingExit, 900, 3, 1), Label = "building x", Role = "exit", ExitKey = "Driveway@(1.0, 2.0)",
                Boxable = true, Boxed = true, Changed = true, Applied = "immediate", Outline = ring,
            };
            r.GroupRings.Add(ring);
            r.MergedGroup.Add(new ApiBoxRef(ApiBoxKind.Junction, 12, 1));
            r.Notes.Add("n1");
            string s = BoxApi.TargetResult("0.4.13-probe", "add", r);
            Check(Json.TryParse(s, out v, out string pe), "target result parses: " + pe);
            JsonValue tg = v.Get("target");
            Check(v.Get("ok").Bool && v.Get("action").String == "add" && tg != null && tg.Get("kind").String == "building_exit"
                  && tg.Get("node").Kind == JsonKind.Null && tg.Get("building").Get("index").Number == 900 && tg.Get("building").Get("version").Number == 3
                  && tg.Get("exit").Number == 1, "target result: target block (kind, node null, building {index,version}, exit)");
            Check(v.Get("boxable").Bool && v.Get("boxed").Bool && v.Get("changed").Bool && v.Get("applied").String == "immediate", "target result: boxable / boxed / changed / applied");
            Check(v.Get("outline").Array.Count == 4 && v.Get("outline").Array[1].Array[0].Number == 4 && v.Get("outline").Array[2].Array[1].Number == 3, "target result: outline [[x,z],..]");
            Check(Math.Abs(v.Get("areaM2").Number - 12) < 1e-9, "target result: areaM2 = 12");
            JsonValue mg = v.Get("mergedGroup").Array[0];
            Check(mg.Get("node").Get("index").Number == 12 && mg.Get("node").Get("version").Number == 1, "target result: mergedGroup entries are callable targets");
            Check(v.Get("notes").Array[0].String == "n1" && v.Get("groupRings").Array.Count == 1, "target result: notes / groupRings");
            Check(!v.Has("error"), "target result: no error on success");

            var jr = new ApiTargetResult { Ref = new ApiBoxRef(ApiBoxKind.Junction, 5, 2), Label = "node", Boxable = false, Would = "refuse", Error = BoxApiErrors.NotBoxable, Reason = "roundabout (the box would cover the island)" };
            s = BoxApi.TargetResult("v", "preview", jr);
            Check(Json.TryParse(s, out v, out _) && v.Get("ok").Bool && !v.Get("boxable").Bool && v.Get("error").String == "not_boxable" && v.Get("reason").String.StartsWith("roundabout")
                  && v.Get("would").String == "refuse" && v.Get("target").Get("kind").String == "junction" && v.Get("target").Get("building").Kind == JsonKind.Null
                  && v.Get("outline").Array.Count == 0 && v.Get("areaM2").Number == 0, "refused preview: ok true, boxable false, error + reason, empty outline");

            var list = new List<ApiBoxInfo>
            {
                new ApiBoxInfo { Ref = new ApiBoxRef(ApiBoxKind.Junction, 1, 1), Center = new V2(2, 1.5), Outline = ring },
                new ApiBoxInfo { Ref = new ApiBoxRef(ApiBoxKind.BuildingEntrance, 7, 1, 2), ExitKey = "Entrance@(0.0, 1.0)", Center = new V2(9, 9), Outline = new List<V3>() },
            };
            list[0].MergedGroup.Add(new ApiBoxRef(ApiBoxKind.BuildingExit, 7, 1, 0));
            s = BoxApi.ListResult("v", list, new[] { "a note" });
            Check(Json.TryParse(s, out v, out pe) && v.Get("ok").Bool && v.Get("action").String == "list" && v.Get("count").Number == 2, "list result: ok / action / count: " + pe);
            JsonValue b0 = v.Get("boxes").Array[0], b1 = v.Get("boxes").Array[1];
            Check(b0.Get("kind").String == "junction" && b0.Get("node").Get("index").Number == 1 && b0.Get("exit").Kind == JsonKind.Null && !b0.Get("derived").Bool
                  && b0.Get("center").Array[0].Number == 2 && Math.Abs(b0.Get("areaM2").Number - 12) < 1e-9 && b0.Get("mergedGroup").Array[0].Get("exit").Number == 0, "list result: junction entry");
            Check(b1.Get("kind").String == "building_entrance" && b1.Get("building").Get("index").Number == 7 && b1.Get("exit").Number == 2 && b1.Get("derived").Kind == JsonKind.Bool && !b1.Get("derived").Bool, "list result: entrance entry, 'derived' kept (API v1) and always false (JR-T-021)");

            s = BoxApi.ExitChoiceError("v", "add", BoxApiErrors.AmbiguousExit, "two exits", new ApiBoxRef(ApiBoxKind.BuildingExit, 7, 1),
                new[] { new ApiExitChoice { Exit = 0, Role = "exit", Key = "a", At = new V2(1, 2) }, new ApiExitChoice { Exit = 1, Role = "exit", Key = "b", Boxed = true } });
            Check(Json.TryParse(s, out v, out _) && !v.Get("ok").Bool && v.Get("error").String == "ambiguous_exit" && v.Get("exits").Array.Count == 2
                  && v.Get("exits").Array[1].Get("boxed").Bool && v.Get("exits").Array[0].Get("at").Array[1].Number == 2, "ambiguous_exit lists the exits");
        }

        private static void ApiMiscTests()
        {
            Check(BoxApi.NotReadyReason(false, true, true, true) != null && BoxApi.NotReadyReason(true, false, true, true) != null
                  && BoxApi.NotReadyReason(true, true, false, true) != null && BoxApi.NotReadyReason(true, true, true, false) == "no city loaded"
                  && BoxApi.NotReadyReason(true, true, true, true) == null, "not_ready: each missing part gives a reason; all present = ready");
            Check(BoxApi.NotReadyReason(false, false, false, false).Contains("disabled"), "not_ready: JR not loaded / disabled named first");
            Check(BoxApi.CleanVersion("0.4.13-probe+1558c66abc") == "0.4.13-probe" && BoxApi.CleanVersion("0.4.13-probe") == "0.4.13-probe" && BoxApi.CleanVersion(null) == "unknown", "version: source revision suffix dropped");
            Check(BoxApi.ApiVersion == 1, "ApiVersion = 1");
            Check(Math.Abs(BoxApi.AreaM2(new List<V3> { new V3(0, 0, 0), new V3(0, 0, 2), new V3(3, 0, 2), new V3(3, 0, 0) }) - 6) < 1e-12, "area: clockwise ring too");
            Check(BoxApi.AreaM2(null) == 0 && BoxApi.AreaM2(new List<V3> { new V3(0, 0, 0), new V3(1, 0, 1) }) == 0, "area: degenerate = 0");
            foreach (var key in new[] { new ExitKey(ExitKind.Driveway, 12.3, -45.6), new ExitKey(ExitKind.Entrance, -3276.0, 3276.0), new ExitKey(ExitKind.Fallback, 0, 0) })
            {
                long sub = BoxMergeInput.ExitSub(key);
                Check(BoxMergeInput.KeyOfSub(sub, out ExitKey back) && back.Equals(key), "exit key round trip through ExitSub: " + key);
            }
            Check(!BoxMergeInput.KeyOfSub(0, out _) && !BoxMergeInput.KeyOfSub(-1, out _), "sub 0 (junction) / -1 (test list) has no exit key");
            // JSON writer escapes and the reader reads it back
            var j = new JsonOut();
            j.BeginObject().Prop("s", "a\"b\\c\n\u0001").Prop("n", double.NaN).Name("arr").BeginArray().Value(1).Value(2.5).Null().EndArray().EndObject();
            Check(Json.TryParse(j.ToString(), out JsonValue v, out string e) && v.Get("s").String == "a\"b\\c\n\u0001" && v.Get("n").Kind == JsonKind.Null
                  && v.Get("arr").Array.Count == 3 && v.Get("arr").Array[1].Number == 2.5, "JSON writer / reader round trip: " + j + " " + e);
            Check(Json.TryParse("{\"u\":\"\\u00e9\"}", out v, out _) && v.Get("u").String == "\u00e9", "JSON reader: \\u escape");
            Check(!Json.TryParse(new string('[', 100) + new string(']', 100), out _, out _), "JSON reader: depth cap");
            Check(Json.TryParse("{\"a\":1,\"a\":2}", out v, out _) && v.Get("a").Number == 2, "JSON reader: repeated name, last wins");
        }

        // ---- repo checks: ONE shared path for the tool and the API

        private static void ApiRepoTests()
        {
            string root = RepoRoot();
            if (root == null) { Check(false, "repo root found"); return; }
            string tool = File.ReadAllText(Path.Combine(root, "JunctionRules", "Boxes", "YellowBoxToolSystem.cs"));
            string api = File.ReadAllText(Path.Combine(root, "JunctionRules", "Api", "JunctionRulesApi.cs"));
            string shared = File.ReadAllText(Path.Combine(root, "JunctionRules", "Boxes", "BoxTargeting.cs"));
            string csproj = File.ReadAllText(Path.Combine(root, "JunctionRules", "JunctionRules.csproj"));

            // both call the same shared methods
            foreach (string m in new[] { "Resolve(", "Solve(", "Apply(" })
            {
                Check(tool.Contains("m_Targeting." + m), "tool calls the shared BoxTargeting." + m);
                Check(api.Contains("tg." + m), "API calls the shared BoxTargeting." + m);
            }
            Check(tool.Contains("BoxTargeting.ApplyShapeRule(") && api.Contains("BoxTargeting.ApplyShapeRule("), "tool and API share the no-shape rule (F3)");
            Check(tool.Contains("BoxRequest.Toggle") && api.Contains("BoxRequest.Add") && api.Contains("BoxRequest.Remove"), "tool toggles, API adds / removes, through one Apply");
            Check(Regex.Matches(shared, @"BoxCommand\.Decide\(").Count == 1, "BoxTargeting.Apply decides with Core/BoxCommand.Decide (once)");
            // no second copy of the pick / writes in the tool or the API
            foreach (string direct in new[] { "BoxStore.Add(", "BoxStore.AddExit(", "BoxStore.Remove(", "BoxStore.RemoveExit(", "BoxSelection.JunctionOk(", "BoxSelection.ExitOk(", "BoxShapes.Junction(", "BoxShapes.Entrance(", "BoxFacts.Accesses(" })
            {
                Check(!tool.Contains(direct), "tool has no own " + direct + " (shared code only)");
                Check(!api.Contains(direct), "API has no own " + direct + " (shared code only)");
            }
            Check(shared.Contains("BoxStore.Add(") && shared.Contains("BoxStore.AddExit(") && shared.Contains("BoxStore.RemoveExit(") && shared.Contains("BoxShapes.Junction("), "the writes and the shape solve live in BoxTargeting");
            // the contract members the bridge reflects on, unchanged
            Check(api.Contains("namespace JunctionRules.Api") && api.Contains("public static class JunctionRulesApi"), "API type JunctionRules.Api.JunctionRulesApi, public static");
            Check(Regex.IsMatch(api, @"public const int ApiVersion\s*="), "API: ApiVersion is a public const int field");
            Check(Regex.IsMatch(api, @"public static string ModVersion\s*\r?\n?\s*\{") && Regex.IsMatch(api, @"public static bool IsReady\s*\r?\n?\s*\{"), "API: ModVersion / IsReady static properties");
            foreach (string m in new[] { "List(string filterJson)", "Preview(string targetJson)", "Add(string targetJson)", "Remove(string targetJson)" })
                Check(api.Contains("public static string " + m), "API: public static string " + m);
            Check(api.Contains("catch (Exception e)") && api.Contains("BoxApiErrors.Internal") && api.Contains("BoxApiErrors.NotReady"), "API: catches everything (internal) and answers not_ready");
            // version
            Check(csproj.Contains("<Version>1.3.0</Version>") && csproj.Contains("<InformationalVersion>1.3.0</InformationalVersion>"), "csproj version 1.3.0 (release; API v1 shipped since 0.4.13-probe)");
            string mod = File.ReadAllText(Path.Combine(root, "JunctionRules", "Mod.cs"));
            Check(mod.Contains("JunctionRulesApi.MarkMainThread()"), "Mod.OnLoad marks the main thread for the API");
        }
    }
}
