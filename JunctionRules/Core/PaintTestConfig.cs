using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace JunctionRules.Core
{
    /// <summary>One junction on the paint test list: a world position; the nearest node within the snap radius is boxed.</summary>
    public sealed class PaintTestJunction
    {
        public double X, Z;
        /// <summary>Height, or NaN when not given (then the nearest node in plan wins; give y to choose between stacked nodes).</summary>
        public double Y = double.NaN;
        public string Name = "";
        public bool HasY => !double.IsNaN(Y);
    }

    /// <summary>Render-side settings of the paint test (shader inputs; all probe aids).</summary>
    public sealed class PaintRenderParams
    {
        /// <summary>RGBA 0..1. Default: a UK road-marking yellow.</summary>
        public double[] Color = { 1.0, 0.78, 0.05, 1.0 };
        /// <summary>Paint volume below / above the lowest / highest vertex of each triangle, metres (research: about +-1 m round the deck; 0.3.1: up to 200 m as a probe aid).</summary>
        public double VolumeBelow = 1.0, VolumeAbove = 1.0;
        /// <summary>colossal_AreaParameters.x (the game's corner rounding / expand amount; game minimum for surfaces 0.0075).</summary>
        public double Expand = 0.0075;
        public double Smoothness = 0.35;
        public double Metallic = 0.0;
        /// <summary>colossal_NormalOpacity (0 = paint does not change the road's normals).</summary>
        public double NormalOpacity = 0.0;
        /// <summary>colossal_EdgeFadeRange.xy, colossal_EdgeNormal (G87 junction box: fade 1,0; edge normal 0).</summary>
        public double EdgeFadeX = 1.0, EdgeFadeY = 0.0, EdgeNormal = 0.0;
        /// <summary>
        /// colossal_EdgeNoise.xy = (edge wobble amount, wobble size). 0.3.4 (JR-T-003d): the AreaDecalShader divides by
        /// size x expand (DBufferMesh pixel shader, docs/shader-areadecal/blob054.asm lines 208-210), so size 0 makes the edge
        /// noise NaN and every pixel's alpha 0: the cause of "nothing visible" in 0.3.0-0.3.2. The game never allows it:
        /// RenderedArea.m_EdgeNoise is [RangeN(0, 1, 0.01, 10)], default (0.5, 0.5). Amount 0 = clean edges (0 x finite noise).
        /// </summary>
        public double EdgeNoiseX = 0.0, EdgeNoiseY = 1.0;
        /// <summary>Game limits for colossal_EdgeNoise (Game.Prefabs.RenderedArea.m_EdgeNoise RangeN attribute).</summary>
        public const double EdgeNoiseAmountMax = 1.0, EdgeNoiseSizeMin = 0.01, EdgeNoiseSizeMax = 10.0;
        /// <summary>Smallest expand (colossal_AreaParameters.x); the shader divides by it. Game minimum for surfaces: 0.01 x 0.75 x 0.5 = 0.00375.</summary>
        public const double ExpandMin = 0.001;
        /// <summary>colossal_UVScale (only matters if a texture is used; we use 1x1 plain textures).</summary>
        public double UVScale = 1.0;
        /// <summary>Replace the base material's textures with plain 1x1 textures (white colour, flat normal, non-metal mask).</summary>
        public bool PlainTextures = true;
        /// <summary>Flip the prev/next neighbours handed to the shader (probe aid for U9).</summary>
        public bool SwapPrevNext = false;
        /// <summary>Snap radius for list positions, metres (plan distance to the node).</summary>
        public double SnapRadius = 6.0;
        /// <summary>Seconds between cost lines in the log while painting.</summary>
        public double LogIntervalSeconds = 30.0;

        public List<string> Clamp()
        {
            var changed = new List<string>();
            if (Color == null || Color.Length < 3) { Color = new[] { 1.0, 0.78, 0.05, 1.0 }; changed.Add("color"); }
            if (Color.Length == 3) Color = new[] { Color[0], Color[1], Color[2], 1.0 };
            for (int i = 0; i < 4; i++) Color[i] = C(Color[i], 0, 1, i == 3 ? 1.0 : 0.5, "color", changed);
            VolumeBelow = C(VolumeBelow, 0.05, 200, 1.0, "volumeBelow", changed);
            VolumeAbove = C(VolumeAbove, 0.05, 200, 1.0, "volumeAbove", changed);
            Expand = C(Expand, ExpandMin, 1, 0.0075, "expand", changed);
            Smoothness = C(Smoothness, 0, 1, 0.35, "smoothness", changed);
            Metallic = C(Metallic, 0, 1, 0, "metallic", changed);
            NormalOpacity = C(NormalOpacity, 0, 1, 0, "normalOpacity", changed);
            EdgeFadeX = C(EdgeFadeX, -10, 10, 1, "edgeFade", changed);
            EdgeFadeY = C(EdgeFadeY, -10, 10, 0, "edgeFade", changed);
            EdgeNormal = C(EdgeNormal, -10, 10, 0, "edgeNormal", changed);
            EdgeNoiseX = C(EdgeNoiseX, 0, EdgeNoiseAmountMax, 0, "edgeNoise", changed);
            EdgeNoiseY = C(EdgeNoiseY, EdgeNoiseSizeMin, EdgeNoiseSizeMax, 1, "edgeNoise", changed);
            // The shader divides by (fade.x - fade.y) x expand (blob054.asm lines 611-614): a reversed range (x < y) gives a negative divisor = alpha 0 inside, equal values 1/0; both reset.
            if (!(EdgeFadeX - EdgeFadeY >= 0.001)) { EdgeFadeX = 1; EdgeFadeY = 0; if (!changed.Contains("edgeFade")) changed.Add("edgeFade"); }
            UVScale = C(UVScale, 0.001, 1000, 1, "uvScale", changed);
            SnapRadius = C(SnapRadius, 0.5, 50, 6, "snapRadius", changed);
            LogIntervalSeconds = C(LogIntervalSeconds, 2, 3600, 30, "logIntervalSeconds", changed);
            return changed;
        }

        private static double C(double v, double lo, double hi, double fallback, string name, List<string> changed)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) { if (!changed.Contains(name)) changed.Add(name); return fallback; }
            if (v < lo || v > hi) { if (!changed.Contains(name)) changed.Add(name); return Math.Max(lo, Math.Min(hi, v)); }
            return v;
        }
    }

    /// <summary>
    /// The paint test list file (ModsData/JunctionRules/paint-test.json). Pure parse/format; the mod does the file IO.
    /// Unknown keys are ignored; bad values fall back to defaults and are reported in Problems.
    /// </summary>
    public sealed class PaintTestConfig
    {
        public const string FileName = "paint-test.json";

        public List<PaintTestJunction> Junctions = new List<PaintTestJunction>();
        public BoxPaintParams Geometry = new BoxPaintParams();
        public PaintRenderParams Render = new PaintRenderParams();
        /// <summary>0.3.1-probe: opt-in diagnostic switches (the optional "diag" object); defaults = normal draw.</summary>
        public PaintDiagParams Diag = new PaintDiagParams();
        public List<string> Problems = new List<string>();

        public static PaintTestConfig Parse(string text)
        {
            var cfg = new PaintTestConfig();
            if (string.IsNullOrWhiteSpace(text)) return cfg;
            object root;
            try { root = MiniJson.Parse(text); }
            catch (FormatException e) { cfg.Problems.Add("not valid JSON: " + e.Message); return cfg; }
            if (!(root is Dictionary<string, object> obj)) { cfg.Problems.Add("top level is not an object"); return cfg; }

            if (obj.TryGetValue("junctions", out object js))
            {
                if (js is List<object> list)
                {
                    int i = 0;
                    foreach (object o in list)
                    {
                        i++;
                        if (!(o is Dictionary<string, object> j)) { cfg.Problems.Add($"junction {i} is not an object"); continue; }
                        double x = Num(j, "x"), z = Num(j, "z");
                        if (double.IsNaN(x) || double.IsNaN(z)) { cfg.Problems.Add($"junction {i} needs numbers x and z"); continue; }
                        var pj = new PaintTestJunction { X = x, Z = z, Y = Num(j, "y") };
                        if (j.TryGetValue("name", out object nm) && nm is string s) pj.Name = s;
                        cfg.Junctions.Add(pj);
                    }
                }
                else cfg.Problems.Add("junctions is not a list");
            }

            if (obj.TryGetValue("paint", out object po) && po is Dictionary<string, object> p)
            {
                var g = cfg.Geometry;
                g.BorderWidth = NumOr(p, "borderWidth", g.BorderWidth);
                g.LineWidth = NumOr(p, "lineWidth", g.LineWidth);
                g.Spacing = NumOr(p, "spacing", g.Spacing);
                g.AngleDeg = NumOr(p, "angle", g.AngleDeg);
                g.KerbInset = NumOr(p, "kerbInset", g.KerbInset);
                g.MitreLimit = NumOr(p, "mitreLimit", g.MitreLimit);
                g.StraightenTolerance = NumOr(p, "straightenTolerance", g.StraightenTolerance);
                g.EntranceLength = NumOr(p, "entranceLength", g.EntranceLength);
                g.EntranceLength = NumOr(p, "exitLength", g.EntranceLength);   // 0.4.3: "exit" alias; the key stays entranceLength
                if (p.ContainsKey("markingMargin"))
                {
                    g.MarkingMargin = NumOr(p, "markingMargin", double.NaN);
                    if (double.IsNaN(g.MarkingMargin) || double.IsInfinity(g.MarkingMargin)) { g.MarkingMargin = double.NaN; cfg.Problems.Add("paint.markingMargin is not a number, ignored (the Options value applies)"); }
                }
                double ml = NumOr(p, "maxLinesPerFamily", g.MaxLinesPerFamily);
                g.MaxLinesPerFamily = (int)Math.Round(ml);

                var r = cfg.Render;
                if (p.TryGetValue("color", out object co) && co is List<object> cl)
                {
                    var c = new List<double>();
                    foreach (object v in cl) c.Add(v is double d ? d : double.NaN);
                    r.Color = c.ToArray();
                }
                r.VolumeBelow = NumOr(p, "volumeBelow", r.VolumeBelow);
                r.VolumeAbove = NumOr(p, "volumeAbove", r.VolumeAbove);
                r.Expand = NumOr(p, "expand", r.Expand);
                r.Smoothness = NumOr(p, "smoothness", r.Smoothness);
                r.Metallic = NumOr(p, "metallic", r.Metallic);
                r.NormalOpacity = NumOr(p, "normalOpacity", r.NormalOpacity);
                if (p.TryGetValue("edgeFade", out object ef) && ef is List<object> efl && efl.Count >= 2) { r.EdgeFadeX = AsNum(efl[0]); r.EdgeFadeY = AsNum(efl[1]); }
                r.EdgeNormal = NumOr(p, "edgeNormal", r.EdgeNormal);
                if (p.TryGetValue("edgeNoise", out object en) && en is List<object> enl && enl.Count >= 2) { r.EdgeNoiseX = AsNum(enl[0]); r.EdgeNoiseY = AsNum(enl[1]); }
                r.UVScale = NumOr(p, "uvScale", r.UVScale);
                r.PlainTextures = BoolOr(p, "plainTextures", r.PlainTextures);
                r.SwapPrevNext = BoolOr(p, "swapPrevNext", r.SwapPrevNext);
                r.SnapRadius = NumOr(p, "snapRadius", r.SnapRadius);
                r.LogIntervalSeconds = NumOr(p, "logIntervalSeconds", r.LogIntervalSeconds);
            }
            else if (obj.ContainsKey("paint")) cfg.Problems.Add("paint is not an object");

            if (obj.TryGetValue("diag", out object dg))
            {
                if (dg is Dictionary<string, object> dd) cfg.Diag = PaintDiagParams.Read(dd, cfg.Problems);
                else cfg.Problems.Add("diag is not an object");
            }

            foreach (string c in cfg.Geometry.Clamp()) cfg.Problems.Add("paint." + c + " out of range, adjusted");
            foreach (string c in cfg.Render.Clamp()) cfg.Problems.Add("paint." + c + " out of range, adjusted");
            return cfg;
        }

        /// <summary>Writes the file back (used by the "add junction at camera centre" button). Every known key is written.</summary>
        public string Format()
        {
            var ci = CultureInfo.InvariantCulture;
            string F(double v) => v.ToString("0.####", ci);
            var sb = new StringBuilder();
            sb.Append("{\n  \"junctions\": [\n");
            for (int i = 0; i < Junctions.Count; i++)
            {
                var j = Junctions[i];
                sb.Append("    { \"x\": ").Append(F(j.X));
                if (j.HasY) sb.Append(", \"y\": ").Append(F(j.Y));
                sb.Append(", \"z\": ").Append(F(j.Z));
                if (!string.IsNullOrEmpty(j.Name)) sb.Append(", \"name\": ").Append(MiniJson.Quote(j.Name));
                sb.Append(" }").Append(i < Junctions.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ],\n  \"paint\": {\n");
            var g = Geometry; var r = Render;
            var lines = new List<string>
            {
                $"\"borderWidth\": {F(g.BorderWidth)}", $"\"lineWidth\": {F(g.LineWidth)}", $"\"spacing\": {F(g.Spacing)}",
                $"\"angle\": {F(g.AngleDeg)}", $"\"kerbInset\": {F(g.KerbInset)}", $"\"mitreLimit\": {F(g.MitreLimit)}",
                $"\"straightenTolerance\": {F(g.StraightenTolerance)}", $"\"maxLinesPerFamily\": {g.MaxLinesPerFamily}", $"\"entranceLength\": {F(g.EntranceLength)}",
                $"\"color\": [{F(r.Color[0])}, {F(r.Color[1])}, {F(r.Color[2])}, {F(r.Color[3])}]",
                $"\"volumeBelow\": {F(r.VolumeBelow)}", $"\"volumeAbove\": {F(r.VolumeAbove)}", $"\"expand\": {F(r.Expand)}",
                $"\"smoothness\": {F(r.Smoothness)}", $"\"metallic\": {F(r.Metallic)}", $"\"normalOpacity\": {F(r.NormalOpacity)}",
                $"\"edgeFade\": [{F(r.EdgeFadeX)}, {F(r.EdgeFadeY)}]", $"\"edgeNormal\": {F(r.EdgeNormal)}",
                $"\"edgeNoise\": [{F(r.EdgeNoiseX)}, {F(r.EdgeNoiseY)}]", $"\"uvScale\": {F(r.UVScale)}",
                $"\"plainTextures\": {(r.PlainTextures ? "true" : "false")}", $"\"swapPrevNext\": {(r.SwapPrevNext ? "true" : "false")}",
                $"\"snapRadius\": {F(r.SnapRadius)}", $"\"logIntervalSeconds\": {F(r.LogIntervalSeconds)}",
            };
            // 0.4.2: markingMargin only when the file had it (absent = the Options value applies; writing the default would pin it)
            if (!double.IsNaN(g.MarkingMargin)) lines.Insert(9, $"\"markingMargin\": {F(g.MarkingMargin)}");
            for (int i = 0; i < lines.Count; i++) sb.Append("    ").Append(lines[i]).Append(i < lines.Count - 1 ? ",\n" : "\n");
            sb.Append("  }");
            // Diagnostic switches the owner set are kept (only the non-default ones; the mod never adds any).
            List<string> diag = Diag.FormatMembers();
            if (diag.Count > 0)
            {
                sb.Append(",\n  \"diag\": {\n");
                for (int i = 0; i < diag.Count; i++) sb.Append("    ").Append(diag[i]).Append(i < diag.Count - 1 ? ",\n" : "\n");
                sb.Append("  }");
            }
            sb.Append("\n}\n");
            return sb.ToString();
        }

        private static double AsNum(object o) => o is double d ? d : double.NaN;

        private static double Num(Dictionary<string, object> o, string key) => o.TryGetValue(key, out object v) && v is double d ? d : double.NaN;

        private static double NumOr(Dictionary<string, object> o, string key, double fallback)
        {
            if (!o.TryGetValue(key, out object v)) return fallback;
            return v is double d ? d : double.NaN;   // NaN -> Clamp reports it and restores the default
        }

        private static bool BoolOr(Dictionary<string, object> o, string key, bool fallback) => o.TryGetValue(key, out object v) && v is bool b ? b : fallback;

        /// <summary>
        /// Picks the node for a list entry from candidate node positions: within snapRadius in plan; with y given, the
        /// smallest 3D distance wins, else the smallest plan distance. Returns -1 when none; 'stacked' counts other
        /// candidates within 1 m in plan of the winner (a hint to add y).
        /// </summary>
        public static int PickNode(PaintTestJunction j, IList<V3> nodes, double snapRadius, out int stacked)
        {
            stacked = 0;
            int best = -1;
            double bestScore = double.MaxValue;
            var target = new V2(j.X, j.Z);
            for (int i = 0; i < nodes.Count; i++)
            {
                double dp = V2.Distance(nodes[i].XZ, target);
                if (dp > snapRadius) continue;
                double score = j.HasY ? Math.Sqrt(dp * dp + (nodes[i].Y - j.Y) * (nodes[i].Y - j.Y)) : dp;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            if (best >= 0)
            {
                for (int i = 0; i < nodes.Count; i++)
                    if (i != best && V2.Distance(nodes[i].XZ, nodes[best].XZ) <= 1.0) stacked++;
            }
            return best;
        }

        /// <summary>
        /// Node nearest the camera along its view ray: candidates within radius of the ray (in front of the camera);
        /// the one closest to the camera wins (so a deck above a ground junction wins when the camera looks at it).
        /// </summary>
        public static int PickAlongRay(V3 origin, V3 dir, IList<V3> nodes, double radius)
        {
            double len = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y + dir.Z * dir.Z);
            if (len < 1e-9) return -1;
            double dx = dir.X / len, dy = dir.Y / len, dz = dir.Z / len;
            int best = -1;
            double bestT = double.MaxValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                double vx = nodes[i].X - origin.X, vy = nodes[i].Y - origin.Y, vz = nodes[i].Z - origin.Z;
                double t = vx * dx + vy * dy + vz * dz;
                if (t <= 0) continue;
                double px = vx - dx * t, py = vy - dy * t, pz = vz - dz * t;
                double dist = Math.Sqrt(px * px + py * py + pz * pz);
                if (dist <= radius && t < bestT) { bestT = t; best = i; }
            }
            return best;
        }
    }

    /// <summary>Minimal JSON reader: objects (Dictionary), arrays (List), strings, doubles, bools, null.</summary>
    public static class MiniJson
    {
        public static object Parse(string s)
        {
            int i = 0;
            object v = Value(s, ref i);
            Ws(s, ref i);
            if (i != s.Length) throw new FormatException($"unexpected text at {i}");
            return v;
        }

        public static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        private static void Ws(string s, ref int i)
        {
            while (i < s.Length)
            {
                if (char.IsWhiteSpace(s[i])) { i++; continue; }
                if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
                break;
            }
        }

        private static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end");
            char c = s[i];
            if (c == '{')
            {
                i++;
                var d = new Dictionary<string, object>();
                Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != '"') throw new FormatException($"expected a key at {i}");
                    string k = Str(s, ref i);
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new FormatException($"expected ':' at {i}");
                    i++;
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return d; }
                    throw new FormatException($"expected ',' or '}}' at {i}");
                }
            }
            if (c == '[')
            {
                i++;
                var l = new List<object>();
                Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return l; }
                    throw new FormatException($"expected ',' or ']' at {i}");
                }
            }
            if (c == '"') return Str(s, ref i);
            if (Lit(s, ref i, "true")) return true;
            if (Lit(s, ref i, "false")) return false;
            if (Lit(s, ref i, "null")) return null;
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (st == i) throw new FormatException($"unexpected '{c}' at {i}");
            if (!double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out double num)) throw new FormatException($"bad number at {st}");
            return num;
        }

        private static bool Lit(string s, ref int i, string lit)
        {
            if (string.CompareOrdinal(s, i, lit, 0, lit.Length) == 0) { i += lit.Length; return true; }
            return false;
        }

        private static string Str(string s, ref int i)
        {
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    if (i >= s.Length) break;
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw new FormatException("bad \\u escape");
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            throw new FormatException("unterminated string");
        }
    }

    /// <summary>Cost counters of the paint test (pure; the mod feeds Stopwatch ticks). One line per log interval.</summary>
    public sealed class PaintCostMeter
    {
        public long DrawFrames, DrawTicks, DrawMaxTicks;
        public long UpdateFrames, UpdateTicks, UpdateMaxTicks;
        public long Rebuilds, RebuildTicks, RebuildMaxTicks;

        public void AddDraw(long ticks) { DrawFrames++; DrawTicks += ticks; if (ticks > DrawMaxTicks) DrawMaxTicks = ticks; }
        public void AddUpdate(long ticks) { UpdateFrames++; UpdateTicks += ticks; if (ticks > UpdateMaxTicks) UpdateMaxTicks = ticks; }
        public void AddRebuild(long ticks) { Rebuilds++; RebuildTicks += ticks; if (ticks > RebuildMaxTicks) RebuildMaxTicks = ticks; }

        public string Line(double ticksPerSecond, double seconds, int boxes, int triangles)
        {
            var ci = CultureInfo.InvariantCulture;
            double us(long t) => t * 1e6 / ticksPerSecond;
            double avg(long t, long n) => n > 0 ? us(t) / n : 0;
            return string.Format(ci,
                "paint cost over {0:0.0} s: boxes {1}, triangles {2}; draw {3:0.0} us/frame avg, {4:0.0} max ({5} frames); change check {6:0.0} us/frame avg, {7:0.0} max ({8} frames); rebuilds {9}, {10:0.000} ms avg, {11:0.000} max",
                seconds, boxes, triangles, avg(DrawTicks, DrawFrames), us(DrawMaxTicks), DrawFrames,
                avg(UpdateTicks, UpdateFrames), us(UpdateMaxTicks), UpdateFrames,
                Rebuilds, avg(RebuildTicks, Rebuilds) / 1000.0, us(RebuildMaxTicks) / 1000.0);
        }

        public void Reset() { DrawFrames = DrawTicks = DrawMaxTicks = UpdateFrames = UpdateTicks = UpdateMaxTicks = Rebuilds = RebuildTicks = RebuildMaxTicks = 0; }
    }

    /// <summary>Fail-once latch for the paint test: the first failure is kept (what failed + why); later ones are counted, never logged again.</summary>
    public sealed class PaintFailLatch
    {
        public bool Failed { get; private set; }
        public string What { get; private set; }
        public int Suppressed { get; private set; }

        /// <summary>Returns true only for the first failure (the caller logs it then).</summary>
        public bool Trip(string what)
        {
            if (Failed) { Suppressed++; return false; }
            Failed = true;
            What = what;
            return true;
        }
    }
}
