using System;
using System.Collections.Generic;
using System.Globalization;

namespace JunctionRules.Core
{
    /// <summary>
    /// 0.3.1-probe (JR-T-003c): opt-in diagnostic switches of the paint test, read from the optional "diag" object of
    /// paint-test.json. Every default reproduces the normal (game-parity) draw; a switch only changes how the paint test
    /// draws its own boxes. Nothing here is saved by the mod (the file is the owner's), and none of it touches driving.
    /// See PROBE-STAGE3-PAINT.md section 7 for the one-restart order.
    /// </summary>
    public sealed class PaintDiagParams
    {
        /// <summary>"decal" (default: the game's area-surface decal path), "overlay" (the area tool overlay shader, not a decal), or "both".</summary>
        public string DrawPath = "decal";
        /// <summary>Empty (default): clone the areas configuration's rendered-area material. Else the name of an area prefab whose RenderedArea material is cloned instead.</summary>
        public string MaterialFrom = "";
        /// <summary>colossal_DecalLayerMask override; -1 (default) = the settings' decal layers. 255 = every layer.</summary>
        public int DecalLayerMask = -1;
        /// <summary>Per-triangle colour buffer (AreaColorData x, y, z); the shader reads only z, and only while an infoview is on (alpha = z). Default 0,0,0 like a vanilla surface.</summary>
        public double[] ColorBuffer = { 0, 0, 0 };
        /// <summary>LodDistanceFactor override; 0 (default) = computed from the box size like the game.</summary>
        public double LodFactor = 0;
        /// <summary>Swap B and C of every triangle (clockwise winding). A negative control: the shader then discards every pixel.</summary>
        public bool FlipWinding = false;
        /// <summary>OffsetDir override (x, z, dirX, dirZ; UV origin and direction); null (default) = box origin and long side.</summary>
        public double[] OffsetDir = null;
        /// <summary>When above 0: draw one plain square of this side (metres) centred on each listed node at node height, instead of the box.</summary>
        public double TestSquare = 0;
        /// <summary>Set the three material buffers before every draw, as AreaRenderSystem does (default true).</summary>
        public bool SetBuffersEachFrame = true;
        /// <summary>RenderParams.rendererPriority (game area batches use the prefab's priority; default 0).</summary>
        public int RendererPriority = 0;
        /// <summary>Material render queue; -1 (default) = the shader's queue, as the game does.</summary>
        public int RenderQueue = -1;
        /// <summary>"nonPreview" (default, as the game's area batches) or "game" (Game and SceneView cameras only, as the tool overlay).</summary>
        public string Cameras = "nonPreview";

        public static readonly string[] DrawPaths = { "decal", "overlay", "both" };
        public static readonly string[] CameraModes = { "nonPreview", "game" };

        public bool DrawDecal => DrawPath == "decal" || DrawPath == "both";
        public bool DrawOverlay => DrawPath == "overlay" || DrawPath == "both";

        /// <summary>The switches that need the GPU objects made again when they change.</summary>
        public string GpuKey => DrawPath + "|" + MaterialFrom + "|" + RenderQueue.ToString(CultureInfo.InvariantCulture);

        public List<string> Clamp()
        {
            var changed = new List<string>();
            if (Array.IndexOf(DrawPaths, DrawPath) < 0) { DrawPath = "decal"; changed.Add("drawPath"); }
            if (Array.IndexOf(CameraModes, Cameras) < 0) { Cameras = "nonPreview"; changed.Add("cameras"); }
            if (MaterialFrom == null) MaterialFrom = "";
            if (DecalLayerMask < -1 || DecalLayerMask > 255) { DecalLayerMask = -1; changed.Add("decalLayerMask"); }
            if (ColorBuffer == null || ColorBuffer.Length != 3 || Array.Exists(ColorBuffer, v => double.IsNaN(v) || v < 0 || v > 1000)) { ColorBuffer = new double[] { 0, 0, 0 }; changed.Add("colorBuffer"); }
            if (double.IsNaN(LodFactor) || LodFactor < 0 || LodFactor > 1e9) { LodFactor = 0; changed.Add("lodFactor"); }
            if (OffsetDir != null && (OffsetDir.Length != 4 || Array.Exists(OffsetDir, v => double.IsNaN(v) || double.IsInfinity(v)))) { OffsetDir = null; changed.Add("offsetDir"); }
            if (double.IsNaN(TestSquare) || TestSquare < 0 || TestSquare > 500) { TestSquare = 0; changed.Add("testSquare"); }
            if (RendererPriority < -100000 || RendererPriority > 100000) { RendererPriority = 0; changed.Add("rendererPriority"); }
            if (RenderQueue < -1 || RenderQueue > 5000) { RenderQueue = -1; changed.Add("renderQueue"); }
            return changed;
        }

        /// <summary>Reads the "diag" object (unknown keys ignored; bad values reported by Clamp).</summary>
        public static PaintDiagParams Read(Dictionary<string, object> o, List<string> problems)
        {
            var d = new PaintDiagParams();
            if (o == null) return d;
            d.DrawPath = Str(o, "drawPath", d.DrawPath, problems);
            d.MaterialFrom = Str(o, "materialFrom", d.MaterialFrom, problems);
            d.DecalLayerMask = Int(o, "decalLayerMask", d.DecalLayerMask, problems);
            d.ColorBuffer = Arr(o, "colorBuffer", d.ColorBuffer, problems);
            d.LodFactor = Num(o, "lodFactor", d.LodFactor);
            d.FlipWinding = Bool(o, "flipWinding", d.FlipWinding, problems);
            if (o.TryGetValue("offsetDir", out object od)) d.OffsetDir = od == null ? null : Arr(o, "offsetDir", null, problems) ?? new double[0];
            d.TestSquare = Num(o, "testSquare", d.TestSquare);
            d.SetBuffersEachFrame = Bool(o, "setBuffersEachFrame", d.SetBuffersEachFrame, problems);
            d.RendererPriority = Int(o, "rendererPriority", d.RendererPriority, problems);
            d.RenderQueue = Int(o, "renderQueue", d.RenderQueue, problems);
            d.Cameras = Str(o, "cameras", d.Cameras, problems);
            foreach (string c in d.Clamp()) problems.Add("diag." + c + " invalid, default used");
            return d;
        }

        /// <summary>One line with every effective value (logged once when the list is read).</summary>
        public string Describe()
        {
            var ci = CultureInfo.InvariantCulture;
            string F(double v) => v.ToString("0.####", ci);
            return "drawPath " + DrawPath
                + ", materialFrom " + (MaterialFrom.Length == 0 ? "(areas configuration)" : "'" + MaterialFrom + "'")
                + ", decalLayerMask " + (DecalLayerMask < 0 ? "(settings)" : DecalLayerMask.ToString(ci))
                + ", colorBuffer [" + F(ColorBuffer[0]) + ", " + F(ColorBuffer[1]) + ", " + F(ColorBuffer[2]) + "]"
                + ", lodFactor " + (LodFactor > 0 ? F(LodFactor) : "(computed)")
                + ", flipWinding " + (FlipWinding ? "true" : "false")
                + ", offsetDir " + (OffsetDir == null ? "(box)" : "[" + F(OffsetDir[0]) + ", " + F(OffsetDir[1]) + ", " + F(OffsetDir[2]) + ", " + F(OffsetDir[3]) + "]")
                + ", testSquare " + (TestSquare > 0 ? F(TestSquare) + " m" : "off")
                + ", setBuffersEachFrame " + (SetBuffersEachFrame ? "true" : "false")
                + ", rendererPriority " + RendererPriority.ToString(ci)
                + ", renderQueue " + (RenderQueue < 0 ? "(shader)" : RenderQueue.ToString(ci))
                + ", cameras " + Cameras;
        }

        /// <summary>The non-default switches as JSON members (so writing the list file back keeps what the owner set).</summary>
        public List<string> FormatMembers()
        {
            var ci = CultureInfo.InvariantCulture;
            string F(double v) => v.ToString("0.####", ci);
            var d = new PaintDiagParams();
            var r = new List<string>();
            if (DrawPath != d.DrawPath) r.Add("\"drawPath\": " + MiniJson.Quote(DrawPath));
            if (MaterialFrom != d.MaterialFrom) r.Add("\"materialFrom\": " + MiniJson.Quote(MaterialFrom));
            if (DecalLayerMask != d.DecalLayerMask) r.Add("\"decalLayerMask\": " + DecalLayerMask.ToString(ci));
            if (ColorBuffer[0] != 0 || ColorBuffer[1] != 0 || ColorBuffer[2] != 0) r.Add($"\"colorBuffer\": [{F(ColorBuffer[0])}, {F(ColorBuffer[1])}, {F(ColorBuffer[2])}]");
            if (LodFactor != d.LodFactor) r.Add("\"lodFactor\": " + F(LodFactor));
            if (FlipWinding) r.Add("\"flipWinding\": true");
            if (OffsetDir != null) r.Add($"\"offsetDir\": [{F(OffsetDir[0])}, {F(OffsetDir[1])}, {F(OffsetDir[2])}, {F(OffsetDir[3])}]");
            if (TestSquare != d.TestSquare) r.Add("\"testSquare\": " + F(TestSquare));
            if (!SetBuffersEachFrame) r.Add("\"setBuffersEachFrame\": false");
            if (RendererPriority != d.RendererPriority) r.Add("\"rendererPriority\": " + RendererPriority.ToString(ci));
            if (RenderQueue != d.RenderQueue) r.Add("\"renderQueue\": " + RenderQueue.ToString(ci));
            if (Cameras != d.Cameras) r.Add("\"cameras\": " + MiniJson.Quote(Cameras));
            return r;
        }

        // ---- pure helpers the paint system uses (tested without the game)

        /// <summary>The winding the area shader needs: (B-A) x (C-A) in plan, (Bx-Ax)(Cz-Az) - (Bz-Az)(Cx-Ax), must be &gt; 0 (game: Game.Areas.GeometrySystem Snip).</summary>
        public static double ShaderCross(V3 a, V3 b, V3 c) => (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);

        /// <summary>A square ring (counter-clockwise for the shader) of the given side centred on c, and its two triangles.</summary>
        public static PaintPiece Square(V3 c, double side)
        {
            double h = side * 0.5;
            var p = new PaintPiece { Kind = PaintPieceKind.Border };
            // Shader-positive order (same as the geometry's CCW rings): (-h,-h) -> (+h,-h) -> (+h,+h) -> (-h,+h) gives ShaderCross > 0 for (0,1,2) and (0,2,3).
            p.Ring.Add(new V3(c.X - h, c.Y, c.Z - h));
            p.Ring.Add(new V3(c.X + h, c.Y, c.Z - h));
            p.Ring.Add(new V3(c.X + h, c.Y, c.Z + h));
            p.Ring.Add(new V3(c.X - h, c.Y, c.Z + h));
            p.Triangles.AddRange(new[] { 0, 1, 2, 0, 2, 3 });
            return p;
        }

        private static string Str(Dictionary<string, object> o, string key, string fallback, List<string> problems)
        {
            if (!o.TryGetValue(key, out object v)) return fallback;
            if (v is string s) return s.Trim();
            problems.Add("diag." + key + " is not a string, default used");
            return fallback;
        }

        private static int Int(Dictionary<string, object> o, string key, int fallback, List<string> problems)
        {
            if (!o.TryGetValue(key, out object v)) return fallback;
            if (v is double d && !double.IsNaN(d) && Math.Abs(d) < 1e7 && d == Math.Floor(d)) return (int)d;
            problems.Add("diag." + key + " is not a whole number, default used");
            return fallback;
        }

        private static double Num(Dictionary<string, object> o, string key, double fallback)
        {
            if (!o.TryGetValue(key, out object v)) return fallback;
            return v is double d ? d : double.NaN;   // NaN -> Clamp reports it and restores the default
        }

        private static bool Bool(Dictionary<string, object> o, string key, bool fallback, List<string> problems)
        {
            if (!o.TryGetValue(key, out object v)) return fallback;
            if (v is bool b) return b;
            problems.Add("diag." + key + " is not true/false, default used");
            return fallback;
        }

        private static double[] Arr(Dictionary<string, object> o, string key, double[] fallback, List<string> problems)
        {
            if (!o.TryGetValue(key, out object v)) return fallback;
            if (!(v is List<object> l)) { problems.Add("diag." + key + " is not a list, default used"); return fallback; }
            var r = new double[l.Count];
            for (int i = 0; i < l.Count; i++) r[i] = l[i] is double d ? d : double.NaN;
            return r;
        }
    }
}
