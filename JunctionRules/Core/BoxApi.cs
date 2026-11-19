using System;
using System.Collections.Generic;

namespace JunctionRules.Core
{
    // 0.4.13-probe (JR-T-019, YBOX_API_CONTRACT v1): the game-independent half of JunctionRules.Api.JunctionRulesApi:
    // target / filter parsing, the result JSON and the box list model. The game half (JunctionRules/Api/JunctionRulesApi.cs)
    // reads the city and calls the tool's shared code (Boxes/BoxTargeting.cs). Unit-tested (tests ApiTests).

    public enum ApiTargetForm { None = 0, Node, Building, Point }

    /// <summary>One parsed target: {"node":I} / {"node":{"index":I,"version":V}} / {"building":I,"exit"?:k} / {"x":X,"z":Z}.</summary>
    public struct ApiTarget
    {
        public ApiTargetForm Form;
        public int Index;
        /// <summary>-1 = not given (any live entity with that index).</summary>
        public int Version;
        /// <summary>Building only: -1 = not given.</summary>
        public int Exit;
        public double X, Z;

        public override string ToString()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            switch (Form)
            {
                case ApiTargetForm.Node: return "node " + Index + (Version >= 0 ? ":" + Version : "");
                case ApiTargetForm.Building: return "building " + Index + (Version >= 0 ? ":" + Version : "") + (Exit >= 0 ? " exit " + Exit : "");
                case ApiTargetForm.Point: return "point (" + X.ToString("0.00", ci) + ", " + Z.ToString("0.00", ci) + ")";
                default: return "no target";
            }
        }
    }

    public enum ApiFilterForm { All = 0, Radius, Box }

    /// <summary>List filter: {} / {"x","z","radius"} / {"minX","minZ","maxX","maxZ"}.</summary>
    public struct ApiFilter
    {
        public ApiFilterForm Form;
        public double X, Z, Radius, MinX, MinZ, MaxX, MaxZ;

        /// <summary>Does a box whose centre is (x, z) pass? (radius: centre within; bbox: centre inside, edges inclusive).</summary>
        public bool Passes(double x, double z)
        {
            switch (Form)
            {
                case ApiFilterForm.Radius: { double dx = x - X, dz = z - Z; return dx * dx + dz * dz <= Radius * Radius; }
                case ApiFilterForm.Box: return x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
                default: return true;
            }
        }
    }

    public static class BoxApi
    {
        public const int ApiVersion = 1;

        /// <summary>
        /// The readiness rule (IsReady and the not_ready error): null when ready, else the plain reason. 'modLoaded' = JR's
        /// OnLoad ran and OnDispose did not (settings present); 'worldOk' = the game world exists; 'toolOk' = the yellow box
        /// tool system (and its shared targeting) is registered; 'inCity' = a city or the editor is loaded.
        /// </summary>
        public static string NotReadyReason(bool modLoaded, bool worldOk, bool toolOk, bool inCity)
        {
            if (!modLoaded) return "Junction Rules is not loaded or is disabled";
            if (!worldOk) return "no game world";
            if (!toolOk) return "the yellow box tool is not registered (see the Junction Rules log)";
            if (!inCity) return "no city loaded";
            return null;
        }

        /// <summary>"0.4.13-probe+abc123" (SDK source revision suffix) -> "0.4.13-probe"; null / empty -> "unknown".</summary>
        public static string CleanVersion(string informational)
        {
            if (string.IsNullOrEmpty(informational)) return "unknown";
            int plus = informational.IndexOf('+');
            return plus > 0 ? informational.Substring(0, plus) : informational;
        }

        /// <summary>Parses a target. False with error code bad_target and a plain reason.</summary>
        public static bool ParseTarget(string json, out ApiTarget t, out string reason)
        {
            t = new ApiTarget { Version = -1, Exit = -1 };
            if (!Json.TryParse(json, out JsonValue v, out string err)) { reason = "target is not valid JSON: " + err; return false; }
            if (v == null || !v.IsObject) { reason = "target must be a JSON object: {\"node\":I}, {\"building\":I(,\"exit\":k)} or {\"x\":X,\"z\":Z}"; return false; }
            bool node = v.Has("node"), building = v.Has("building"), point = v.Has("x") || v.Has("z");
            int forms = (node ? 1 : 0) + (building ? 1 : 0) + (point ? 1 : 0);
            if (forms != 1) { reason = forms == 0 ? "target names nothing: give \"node\", \"building\" or \"x\"+\"z\"" : "target must use exactly one form (node, building or x/z)"; return false; }
            if (v.Has("exit") && !building) { reason = "\"exit\" goes only with \"building\""; return false; }
            if (point)
            {
                JsonValue x = v.Get("x"), z = v.Get("z");
                if (x == null || z == null) { reason = "a point target needs both \"x\" and \"z\""; return false; }
                if (!x.TryFinite(out t.X) || !z.TryFinite(out t.Z)) { reason = "\"x\" and \"z\" must be numbers (world metres)"; return false; }
                t.Form = ApiTargetForm.Point;
                reason = null;
                return true;
            }
            JsonValue id = node ? v.Get("node") : v.Get("building");
            string what = node ? "node" : "building";
            if (!ParseId(id, out t.Index, out t.Version, out reason)) { reason = "\"" + what + "\": " + reason; return false; }
            t.Form = node ? ApiTargetForm.Node : ApiTargetForm.Building;
            if (building && v.Has("exit"))
            {
                JsonValue e = v.Get("exit");
                if (e.Kind != JsonKind.Null)
                {
                    if (!e.TryInt(out t.Exit) || t.Exit < 0) { reason = "\"exit\" must be a whole number >= 0 (the exit index, as List or ambiguous_exit give it)"; return false; }
                }
            }
            reason = null;
            return true;
        }

        /// <summary>An entity id: I or {"index":I,"version":V} (version optional).</summary>
        private static bool ParseId(JsonValue id, out int index, out int version, out string reason)
        {
            index = 0; version = -1;
            if (id.Kind == JsonKind.Number)
            {
                if (!id.TryInt(out index) || index <= 0) { reason = "entity index must be a whole number > 0"; return false; }
                reason = null;
                return true;
            }
            if (id.IsObject)
            {
                JsonValue i = id.Get("index"), ver = id.Get("version");
                if (i == null || !i.TryInt(out index) || index <= 0) { reason = "\"index\" must be a whole number > 0"; return false; }
                if (ver != null && ver.Kind != JsonKind.Null && (!ver.TryInt(out version) || version <= 0)) { reason = "\"version\" must be a whole number > 0"; return false; }
                reason = null;
                return true;
            }
            reason = "must be an entity index or {\"index\":I,\"version\":V}";
            return false;
        }

        /// <summary>Parses a List filter (null / empty string = all). False with a plain reason (error code bad_target).</summary>
        public static bool ParseFilter(string json, out ApiFilter f, out string reason)
        {
            f = new ApiFilter { Form = ApiFilterForm.All };
            reason = null;
            if (string.IsNullOrWhiteSpace(json)) return true;
            if (!Json.TryParse(json, out JsonValue v, out string err)) { reason = "filter is not valid JSON: " + err; return false; }
            if (v.Kind == JsonKind.Null) return true;
            if (!v.IsObject) { reason = "filter must be a JSON object: {}, {\"x\",\"z\",\"radius\"} or {\"minX\",\"minZ\",\"maxX\",\"maxZ\"}"; return false; }
            bool circle = v.Has("x") || v.Has("z") || v.Has("radius");
            bool box = v.Has("minX") || v.Has("minZ") || v.Has("maxX") || v.Has("maxZ");
            if (circle && box) { reason = "filter must be either x/z/radius or minX/minZ/maxX/maxZ, not both"; return false; }
            if (circle)
            {
                JsonValue x = v.Get("x"), z = v.Get("z"), r = v.Get("radius");
                if (x == null || z == null || r == null) { reason = "a radius filter needs \"x\", \"z\" and \"radius\""; return false; }
                if (!x.TryFinite(out f.X) || !z.TryFinite(out f.Z) || !r.TryFinite(out f.Radius)) { reason = "\"x\", \"z\" and \"radius\" must be numbers (metres)"; return false; }
                if (f.Radius < 0) { reason = "\"radius\" must be >= 0"; return false; }
                f.Form = ApiFilterForm.Radius;
                return true;
            }
            if (box)
            {
                JsonValue a = v.Get("minX"), b = v.Get("minZ"), c = v.Get("maxX"), d = v.Get("maxZ");
                if (a == null || b == null || c == null || d == null) { reason = "a bbox filter needs \"minX\", \"minZ\", \"maxX\" and \"maxZ\""; return false; }
                if (!a.TryFinite(out f.MinX) || !b.TryFinite(out f.MinZ) || !c.TryFinite(out f.MaxX) || !d.TryFinite(out f.MaxZ)) { reason = "bbox values must be numbers (metres)"; return false; }
                if (f.MinX > f.MaxX || f.MinZ > f.MaxZ) { reason = "bbox min must not exceed max"; return false; }
                f.Form = ApiFilterForm.Box;
                return true;
            }
            return true;
        }

        /// <summary>Plan area (m2) of a ring in x/z (shoelace, absolute; open or closed ring). 0 for fewer than 3 points.</summary>
        public static double AreaM2(IList<V3> ring)
        {
            if (ring == null || ring.Count < 3) return 0;
            double a = 0;
            for (int i = 0; i < ring.Count; i++)
            {
                V3 p = ring[i], q = ring[(i + 1) % ring.Count];
                a += p.X * q.Z - q.X * p.Z;
            }
            return Math.Abs(a) * 0.5;
        }

        /// <summary>Mean of a ring's points in x/z (ring centre for List); (NaN, NaN) when empty.</summary>
        public static V2 Centre(IList<V3> ring)
        {
            if (ring == null || ring.Count == 0) return new V2(double.NaN, double.NaN);
            double x = 0, z = 0;
            foreach (V3 p in ring) { x += p.X; z += p.Z; }
            return new V2(x / ring.Count, z / ring.Count);
        }

        // ------------------------------------------------------------------ result JSON

        /// <summary>Starts a result object with ok / apiVersion / modVersion / action (caller adds the rest and ends it).</summary>
        public static JsonOut Begin(bool ok, string modVersion, string action)
        {
            var j = new JsonOut();
            j.BeginObject().Prop("ok", ok).Prop("apiVersion", ApiVersion).Prop("modVersion", modVersion).Prop("action", action);
            return j;
        }

        /// <summary>{"ok":false, ..., "error":code, "reason":reason}.</summary>
        public static string Error(string modVersion, string action, string code, string reason)
        {
            JsonOut j = Begin(false, modVersion, action);
            j.Prop("error", code).Prop("reason", reason ?? "");
            return j.EndObject().ToString();
        }

        public static void WriteId(JsonOut j, int index, int version)
        {
            j.BeginObject().Prop("index", index).Prop("version", version).EndObject();
        }

        public static void WriteRing(JsonOut j, IList<V3> ring)
        {
            j.BeginArray();
            if (ring != null) foreach (V3 p in ring) j.BeginArray().Value(p.X, 2).Value(p.Z, 2).EndArray();
            j.EndArray();
        }

        public static void WriteRings(JsonOut j, IList<List<V3>> rings)
        {
            j.BeginArray();
            if (rings != null) foreach (List<V3> r in rings) WriteRing(j, r);
            j.EndArray();
        }

        /// <summary>A box reference as a target object (callable back): {"node":{index,version}} or {"building":{..},"exit":k}.</summary>
        public static void WriteRef(JsonOut j, ApiBoxRef r)
        {
            j.BeginObject();
            if (r.Kind == ApiBoxKind.Junction) { j.Name("node"); WriteId(j, r.Index, r.Version); }
            else
            {
                j.Name("building"); WriteId(j, r.Index, r.Version);
                j.Name("exit"); if (r.Exit >= 0) j.Value(r.Exit); else j.Null();
                if (r.Kind == ApiBoxKind.BuildingEntrance) j.Prop("derived", false);   // JR-T-021: kept for API v1, never derived now
            }
            j.EndObject();
        }

        public static string KindName(ApiBoxKind k) => k == ApiBoxKind.Junction ? "junction" : k == ApiBoxKind.BuildingEntrance ? "building_entrance" : "building_exit";

        /// <summary>The Preview / Add / Remove result (contract "Preview/Add/Remove success"; a refused preview carries error + reason).</summary>
        public static string TargetResult(string modVersion, string action, ApiTargetResult r)
        {
            JsonOut j = Begin(true, modVersion, action);
            j.Name("target").BeginObject();
            j.Prop("kind", r.Ref.Kind == ApiBoxKind.Junction ? "junction" : "building_exit");
            j.Name("node"); if (r.Ref.Kind == ApiBoxKind.Junction) WriteId(j, r.Ref.Index, r.Ref.Version); else j.Null();
            j.Name("building"); if (r.Ref.Kind != ApiBoxKind.Junction) WriteId(j, r.Ref.Index, r.Ref.Version); else j.Null();
            j.Name("exit"); if (r.Ref.Kind != ApiBoxKind.Junction && r.Ref.Exit >= 0) j.Value(r.Ref.Exit); else j.Null();
            if (r.FromBuilding != null) { j.Name("fromBuilding"); WriteId(j, r.FromBuilding.Value.Index, r.FromBuilding.Value.Version); }
            if (r.Role != null) j.Prop("role", r.Role);
            if (r.ExitKey != null) j.Prop("exitKey", r.ExitKey);
            j.Prop("label", r.Label);
            j.EndObject();
            j.Prop("boxable", r.Boxable);
            if (r.Error != null) j.Prop("error", r.Error).Prop("reason", r.Reason ?? "");
            j.Prop("boxed", r.Boxed).Prop("changed", r.Changed);
            if (r.Applied != null) j.Prop("applied", r.Applied);
            if (r.Would != null) j.Prop("would", r.Would);
            j.Name("outline"); WriteRing(j, r.Outline);
            j.Prop("areaM2", AreaM2(r.Outline), 2);
            j.Name("groupRings"); WriteRings(j, r.GroupRings);
            j.Name("mergedGroup").BeginArray();
            foreach (ApiBoxRef m in r.MergedGroup) WriteRef(j, m);
            j.EndArray();
            j.Name("notes").BeginArray();
            foreach (string n in r.Notes) j.Value(n);
            j.EndArray();
            return j.EndObject().ToString();
        }

        /// <summary>The List result: {"ok":true, ..., "count":N, "boxes":[...]}.</summary>
        public static string ListResult(string modVersion, IList<ApiBoxInfo> boxes, IList<string> notes = null)
        {
            JsonOut j = Begin(true, modVersion, "list");
            j.Prop("count", boxes.Count);
            j.Name("boxes").BeginArray();
            foreach (ApiBoxInfo b in boxes)
            {
                j.BeginObject();
                j.Prop("kind", KindName(b.Ref.Kind));
                j.Name(b.Ref.Kind == ApiBoxKind.Junction ? "node" : "building"); WriteId(j, b.Ref.Index, b.Ref.Version);
                j.Name("exit"); if (b.Ref.Kind != ApiBoxKind.Junction && b.Ref.Exit >= 0) j.Value(b.Ref.Exit); else j.Null();
                if (b.ExitKey != null) j.Prop("exitKey", b.ExitKey);
                j.Name("center").BeginArray().Value(b.Center.X, 2).Value(b.Center.Z, 2).EndArray();
                j.Name("outline"); WriteRing(j, b.Outline);
                j.Prop("areaM2", AreaM2(b.Outline), 2);
                j.Name("mergedGroup").BeginArray();
                foreach (ApiBoxRef m in b.MergedGroup) WriteRef(j, m);
                j.EndArray();
                j.Prop("derived", false);   // JR-T-021: kept for API v1; every box is saved on its own now
                if (b.Note != null) j.Prop("note", b.Note);
                j.EndObject();
            }
            j.EndArray();
            if (notes != null && notes.Count > 0)
            {
                j.Name("notes").BeginArray();
                foreach (string n in notes) j.Value(n);
                j.EndArray();
            }
            return j.EndObject().ToString();
        }

        /// <summary>ambiguous_exit / an out-of-range exit: the error plus the building's accesses to choose from.</summary>
        public static string ExitChoiceError(string modVersion, string action, string code, string reason, ApiBoxRef building, IList<ApiExitChoice> choices)
        {
            JsonOut j = Begin(false, modVersion, action);
            j.Prop("error", code).Prop("reason", reason ?? "");
            j.Name("building"); WriteId(j, building.Index, building.Version);
            j.Name("exits").BeginArray();
            foreach (ApiExitChoice c in choices)
            {
                j.BeginObject().Prop("exit", c.Exit).Prop("role", c.Role).Prop("exitKey", c.Key).Prop("boxed", c.Boxed);
                j.Name("at").BeginArray().Value(c.At.X, 2).Value(c.At.Z, 2).EndArray();
                j.EndObject();
            }
            j.EndArray();
            return j.EndObject().ToString();
        }

        /// <summary>
        /// Which access of a building a {"building":I,"exit"?:k} target means. Index k counts the building's real accesses
        /// in the tool's order (exits first, then entrances). Omitted: the only exit (as API v1); JR-T-021: with no exit, the
        /// only entrance; otherwise ambiguous_exit (give "exit": k). Returns the index, or -1 with an error code and reason.
        /// </summary>
        public static int PickExit(int requested, IList<ApiExitChoice> accesses, out string error, out string reason)
        {
            error = null; reason = null;
            int exits = 0, firstExit = -1;
            for (int i = 0; i < accesses.Count; i++) if (accesses[i].Role != "entrance") { exits++; if (firstExit < 0) firstExit = i; }
            if (requested >= 0)
            {
                if (requested < accesses.Count) return requested;
                error = BoxApiErrors.NotFound;
                reason = accesses.Count == 0 ? "the building has no real car exit or entrance" : $"exit {requested} not found: the building has {accesses.Count} access(es), 0..{accesses.Count - 1}";
                return -1;
            }
            if (exits == 1) return firstExit;
            if (exits == 0 && accesses.Count == 1) return 0;
            if (accesses.Count == 0)
            {
                error = BoxApiErrors.NotFound;
                reason = "the building has no real car exit or entrance";
                return -1;
            }
            error = BoxApiErrors.AmbiguousExit;
            reason = exits == 0 ? $"the building has {accesses.Count} car entrances: give \"exit\": k" : $"the building has {exits} car exits: give \"exit\": k";
            return -1;
        }
    }

    public enum ApiBoxKind { Junction = 0, BuildingExit, BuildingEntrance }

    /// <summary>A box / target by entity id (+ exit index for a building).</summary>
    public struct ApiBoxRef
    {
        public ApiBoxKind Kind;
        public int Index, Version;
        public int Exit;
        public ApiBoxRef(ApiBoxKind kind, int index, int version, int exit = -1) { Kind = kind; Index = index; Version = version; Exit = exit; }
        public bool SameAs(ApiBoxRef o) => Kind == o.Kind && Index == o.Index && Version == o.Version && Exit == o.Exit;
    }

    public sealed class ApiBoxInfo
    {
        public ApiBoxRef Ref;
        public string ExitKey;
        public V2 Center;
        public List<V3> Outline;
        public List<ApiBoxRef> MergedGroup = new List<ApiBoxRef>();
        public string Note;
    }

    public sealed class ApiTargetResult
    {
        public ApiBoxRef Ref;
        public ApiBoxRef? FromBuilding;
        public string Role, ExitKey, Label;
        public bool Boxable, Boxed, Changed;
        public string Error, Reason;
        /// <summary>Add / Remove that changed something: "immediate" (the tool's EntityManager write) ; null otherwise.</summary>
        public string Applied;
        /// <summary>Preview: what Add / Remove would do now: "add", "remove" or "refuse" (null when not a preview).</summary>
        public string Would;
        public List<V3> Outline;
        public List<List<V3>> GroupRings = new List<List<V3>>();
        public List<ApiBoxRef> MergedGroup = new List<ApiBoxRef>();
        public List<string> Notes = new List<string>();
    }

    public struct ApiExitChoice
    {
        public int Exit;
        /// <summary>"exit", "two-way access", "entrance" (the tool's log roles).</summary>
        public string Role;
        public string Key;
        public bool Boxed;
        public V2 At;
    }
}
