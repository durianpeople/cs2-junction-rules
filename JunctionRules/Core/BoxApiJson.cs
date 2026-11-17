using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace JunctionRules.Core
{
    // 0.4.13-probe (JR-T-019, YBOX_API_CONTRACT v1): the small JSON reader / writer behind JunctionRules.Api.JunctionRulesApi.
    // Strings in, strings out, so the bridge reaches the API by reflection without any JR type. No new dependency: the game
    // ships no JSON library the mod may rely on, so this is a plain recursive-descent reader (objects, arrays, strings,
    // numbers, true / false / null) and an append-only writer. Game-independent, unit-tested (tests ApiTests).

    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>One parsed JSON value.</summary>
    public sealed class JsonValue
    {
        public JsonKind Kind;
        public bool Bool;
        public double Number;
        public string String;
        public List<JsonValue> Array;
        /// <summary>Object members in file order (a repeated name: the last one wins on lookup).</summary>
        public List<KeyValuePair<string, JsonValue>> Members;

        public bool IsObject => Kind == JsonKind.Object;

        public JsonValue Get(string name)
        {
            if (Members == null) return null;
            for (int i = Members.Count - 1; i >= 0; i--) if (Members[i].Key == name) return Members[i].Value;
            return null;
        }

        public bool Has(string name) => Get(name) != null;

        /// <summary>A whole number that fits an int (JSON numbers are doubles); false for anything else.</summary>
        public bool TryInt(out int v)
        {
            v = 0;
            if (Kind != JsonKind.Number || double.IsNaN(Number) || double.IsInfinity(Number)) return false;
            if (Math.Floor(Number) != Number || Number < int.MinValue || Number > int.MaxValue) return false;
            v = (int)Number;
            return true;
        }

        public bool TryFinite(out double v)
        {
            v = Number;
            return Kind == JsonKind.Number && !double.IsNaN(Number) && !double.IsInfinity(Number);
        }
    }

    public static class Json
    {
        /// <summary>Parses one JSON value (whitespace around it allowed). False with 'error' on any syntax problem.</summary>
        public static bool TryParse(string text, out JsonValue value, out string error)
        {
            value = null;
            error = null;
            if (text == null) { error = "no JSON (null)"; return false; }
            int i = 0;
            try
            {
                SkipWs(text, ref i);
                value = ParseValue(text, ref i, 0);
                SkipWs(text, ref i);
                if (i != text.Length) { error = "unexpected text after the JSON value at " + i; value = null; return false; }
                return true;
            }
            catch (FormatException e)
            {
                error = e.Message;
                value = null;
                return false;
            }
        }

        private const int MaxDepth = 32;

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        private static FormatException Bad(string what, int at) => new FormatException(what + " at " + at);

        private static JsonValue ParseValue(string s, ref int i, int depth)
        {
            if (depth > MaxDepth) throw Bad("JSON nested too deep", i);
            if (i >= s.Length) throw Bad("JSON ends early", i);
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i, depth);
            if (c == '[') return ParseArray(s, ref i, depth);
            if (c == '"') return new JsonValue { Kind = JsonKind.String, String = ParseString(s, ref i) };
            if (c == 't' && Word(s, ref i, "true")) return new JsonValue { Kind = JsonKind.Bool, Bool = true };
            if (c == 'f' && Word(s, ref i, "false")) return new JsonValue { Kind = JsonKind.Bool, Bool = false };
            if (c == 'n' && Word(s, ref i, "null")) return new JsonValue { Kind = JsonKind.Null };
            if (c == '-' || (c >= '0' && c <= '9')) return new JsonValue { Kind = JsonKind.Number, Number = ParseNumber(s, ref i) };
            throw Bad("unexpected character '" + c + "'", i);
        }

        private static bool Word(string s, ref int i, string w)
        {
            if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0) return false;
            i += w.Length;
            return true;
        }

        private static JsonValue ParseObject(string s, ref int i, int depth)
        {
            var v = new JsonValue { Kind = JsonKind.Object, Members = new List<KeyValuePair<string, JsonValue>>() };
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return v; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') throw Bad("expected a member name", i);
                string name = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw Bad("expected ':'", i);
                i++;
                SkipWs(s, ref i);
                v.Members.Add(new KeyValuePair<string, JsonValue>(name, ParseValue(s, ref i, depth + 1)));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return v; }
                throw Bad("expected ',' or '}'", i);
            }
        }

        private static JsonValue ParseArray(string s, ref int i, int depth)
        {
            var v = new JsonValue { Kind = JsonKind.Array, Array = new List<JsonValue>() };
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return v; }
            while (true)
            {
                SkipWs(s, ref i);
                v.Array.Add(ParseValue(s, ref i, depth + 1));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return v; }
                throw Bad("expected ',' or ']'", i);
            }
        }

        private static string ParseString(string s, ref int i)
        {
            int start = i;
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code)) throw Bad("bad \\u escape", i);
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default: throw Bad("bad escape", i - 1);
                }
            }
            throw Bad("string not closed", start);
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            if (s[i] == '-') i++;
            while (i < s.Length && ((s[i] >= '0' && s[i] <= '9') || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-')) i++;
            if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) throw Bad("bad number", start);
            return d;
        }
    }

    /// <summary>Append-only JSON writer (invariant culture, no pretty printing). Callers keep the nesting balanced.</summary>
    public sealed class JsonOut
    {
        private readonly StringBuilder m_Sb = new StringBuilder();
        // per open container: has it got a member / element yet
        private readonly Stack<bool> m_First = new Stack<bool>();
        private bool m_AfterName;

        public override string ToString() => m_Sb.ToString();

        private void Sep()
        {
            if (m_AfterName) { m_AfterName = false; return; }
            if (m_First.Count == 0) return;
            if (m_First.Peek()) { m_First.Pop(); m_First.Push(false); }
            else m_Sb.Append(',');
        }

        public JsonOut BeginObject() { Sep(); m_Sb.Append('{'); m_First.Push(true); return this; }
        public JsonOut EndObject() { m_First.Pop(); m_Sb.Append('}'); return this; }
        public JsonOut BeginArray() { Sep(); m_Sb.Append('['); m_First.Push(true); return this; }
        public JsonOut EndArray() { m_First.Pop(); m_Sb.Append(']'); return this; }

        public JsonOut Name(string name)
        {
            Sep();
            WriteString(name);
            m_Sb.Append(':');
            m_AfterName = true;
            return this;
        }

        public JsonOut Value(string v) { Sep(); if (v == null) m_Sb.Append("null"); else WriteString(v); return this; }
        public JsonOut Value(bool v) { Sep(); m_Sb.Append(v ? "true" : "false"); return this; }
        public JsonOut Value(int v) { Sep(); m_Sb.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }
        public JsonOut Value(long v) { Sep(); m_Sb.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }

        /// <summary>A number rounded to 'digits' decimals; NaN / infinity are written as null (not valid JSON numbers).</summary>
        public JsonOut Value(double v, int digits = 3)
        {
            Sep();
            if (double.IsNaN(v) || double.IsInfinity(v)) m_Sb.Append("null");
            else m_Sb.Append(Math.Round(v, digits).ToString("0.###############", CultureInfo.InvariantCulture));
            return this;
        }

        public JsonOut Null() { Sep(); m_Sb.Append("null"); return this; }

        public JsonOut Prop(string name, string v) => Name(name).Value(v);
        public JsonOut Prop(string name, bool v) => Name(name).Value(v);
        public JsonOut Prop(string name, int v) => Name(name).Value(v);
        public JsonOut Prop(string name, double v, int digits = 3) => Name(name).Value(v, digits);

        private void WriteString(string s)
        {
            m_Sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': m_Sb.Append("\\\""); break;
                    case '\\': m_Sb.Append("\\\\"); break;
                    case '\n': m_Sb.Append("\\n"); break;
                    case '\r': m_Sb.Append("\\r"); break;
                    case '\t': m_Sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) m_Sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else m_Sb.Append(c);
                        break;
                }
            }
            m_Sb.Append('"');
        }
    }
}
