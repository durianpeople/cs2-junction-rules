using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;

namespace JunctionRules.Core
{
    /// <summary>
    /// A hash of a type's code that ignores metadata token numbers (which change whenever anything else in the
    /// assembly changes) but not the code: every IL instruction, every operand, the members it calls or touches (by
    /// name), locals, exception clauses, and the declared fields. Nested types are included. Uses only
    /// System.Reflection, so the same code runs in the game (Mono) and in the offline tool (tools/IlHash, .NET 8),
    /// and both must print the same hash for the same Game.dll.
    /// </summary>
    public static class IlHasher
    {
        private const BindingFlags kDeclared = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic |
                                               BindingFlags.Instance | BindingFlags.Static;

        private static readonly Dictionary<short, OpCode> s_OpCodes = BuildOpCodeTable();

        private static Dictionary<short, OpCode> BuildOpCodeTable()
        {
            var table = new Dictionary<short, OpCode>();
            foreach (FieldInfo f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (f.GetValue(null) is OpCode op) table[op.Value] = op;
            }
            return table;
        }

        /// <summary>Hex SHA-256 over the normalised text of all <paramref name="types"/> (and their nested types).</summary>
        public static string Hash(params Type[] types)
        {
            return Sha256(Describe(types));
        }

        /// <summary>Per-type hashes (outer types only, nested types folded in), for diagnosing a mismatch.</summary>
        public static List<KeyValuePair<string, string>> HashEach(params Type[] types)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (Type t in types) list.Add(new KeyValuePair<string, string>(FormatType(t), Sha256(Describe(t))));
            return list;
        }

        /// <summary>The normalised text that is hashed (useful to diff two runtimes' views of the same code).</summary>
        public static string Describe(params Type[] types)
        {
            var sb = new StringBuilder(1 << 16);
            foreach (Type t in types) DescribeType(t, sb);
            return sb.ToString();
        }

        public static string Sha256(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(h.Length * 2);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static void DescribeType(Type t, StringBuilder sb)
        {
            sb.Append("type ").Append(FormatType(t)).Append(" : ").Append(FormatType(t.BaseType)).Append('\n');

            var fields = new List<string>();
            foreach (FieldInfo f in t.GetFields(kDeclared))
            {
                string lit = f.IsLiteral ? " = " + FormatConstant(f.GetRawConstantValue()) : "";
                fields.Add("  field " + (f.IsStatic ? "static " : "") + FormatType(f.FieldType) + " " + f.Name + lit);
            }
            fields.Sort(StringComparer.Ordinal);
            foreach (string f in fields) sb.Append(f).Append('\n');

            var methods = new List<KeyValuePair<string, MethodBase>>();
            foreach (MethodInfo m in t.GetMethods(kDeclared)) methods.Add(new KeyValuePair<string, MethodBase>(FormatMethodSignature(m), m));
            foreach (ConstructorInfo c in t.GetConstructors(kDeclared)) methods.Add(new KeyValuePair<string, MethodBase>(FormatMethodSignature(c), c));
            methods.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            foreach (var kv in methods)
            {
                sb.Append("  method ").Append(kv.Key).Append('\n');
                DescribeBody(kv.Value, sb);
            }

            var nested = new List<Type>(t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
            nested.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            foreach (Type n in nested) DescribeType(n, sb);
        }

        private static void DescribeBody(MethodBase m, StringBuilder sb)
        {
            MethodBody body;
            try { body = m.GetMethodBody(); }
            catch (Exception e) { sb.Append("    body ?").Append(e.GetType().Name).Append('\n'); return; }
            if (body == null) { sb.Append("    no body\n"); return; }

            sb.Append("    maxstack ").Append(body.MaxStackSize).Append(" initlocals ").Append(body.InitLocals).Append('\n');
            foreach (LocalVariableInfo l in body.LocalVariables)
            {
                sb.Append("    local ").Append(l.LocalIndex).Append(' ').Append(FormatType(l.LocalType)).Append(l.IsPinned ? " pinned" : "").Append('\n');
            }
            foreach (ExceptionHandlingClause c in body.ExceptionHandlingClauses)
            {
                sb.Append("    clause ").Append(c.Flags).Append(' ').Append(c.TryOffset).Append('+').Append(c.TryLength)
                  .Append(' ').Append(c.HandlerOffset).Append('+').Append(c.HandlerLength);
                if (c.Flags == ExceptionHandlingClauseOptions.Clause) sb.Append(' ').Append(Safe(() => FormatType(c.CatchType)));
                if (c.Flags == ExceptionHandlingClauseOptions.Filter) sb.Append(" filter ").Append(c.FilterOffset);
                sb.Append('\n');
            }

            byte[] il = body.GetILAsByteArray() ?? new byte[0];
            Type[] typeArgs = m.DeclaringType != null && m.DeclaringType.IsGenericType ? m.DeclaringType.GetGenericArguments() : null;
            Type[] methodArgs = m.IsGenericMethod ? m.GetGenericArguments() : null;
            Module module = m.Module;
            int pos = 0;
            while (pos < il.Length)
            {
                int offset = pos;
                short code = il[pos++];
                if (code == 0xFE && pos < il.Length) code = unchecked((short)(0xFE00 | il[pos++]));
                if (!s_OpCodes.TryGetValue(code, out OpCode op))
                {
                    sb.Append("    IL_").Append(offset.ToString("x4")).Append(" ?op ").Append(code).Append('\n');
                    continue;
                }
                sb.Append("    IL_").Append(offset.ToString("x4")).Append(' ').Append(op.Name);
                switch (op.OperandType)
                {
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        sb.Append(' ').Append(op.OperandType == OperandType.ShortInlineI ? (sbyte)il[pos] : il[pos]);
                        pos += 1;
                        break;
                    case OperandType.InlineVar:
                        sb.Append(' ').Append(BitConverter.ToUInt16(il, pos));
                        pos += 2;
                        break;
                    case OperandType.InlineBrTarget:
                    case OperandType.InlineI:
                        sb.Append(' ').Append(BitConverter.ToInt32(il, pos));
                        pos += 4;
                        break;
                    case OperandType.ShortInlineR:
                        sb.Append(' ').Append(BitConverter.ToInt32(il, pos).ToString("x8"));
                        pos += 4;
                        break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        sb.Append(' ').Append(BitConverter.ToInt64(il, pos).ToString("x16"));
                        pos += 8;
                        break;
                    case OperandType.InlineSwitch:
                    {
                        int n = BitConverter.ToInt32(il, pos);
                        pos += 4;
                        sb.Append(" [");
                        for (int i = 0; i < n; i++) { sb.Append(i == 0 ? "" : ",").Append(BitConverter.ToInt32(il, pos)); pos += 4; }
                        sb.Append(']');
                        break;
                    }
                    case OperandType.InlineString:
                    {
                        int tok = BitConverter.ToInt32(il, pos);
                        pos += 4;
                        sb.Append(" \"").Append(Safe(() => Escape(module.ResolveString(tok)))).Append('"');
                        break;
                    }
                    case OperandType.InlineSig:
                    {
                        int tok = BitConverter.ToInt32(il, pos);
                        pos += 4;
                        sb.Append(" sig ").Append(Safe(() => Convert.ToBase64String(module.ResolveSignature(tok))));
                        break;
                    }
                    case OperandType.InlineField:
                    case OperandType.InlineMethod:
                    case OperandType.InlineType:
                    case OperandType.InlineTok:
                    {
                        int tok = BitConverter.ToInt32(il, pos);
                        pos += 4;
                        sb.Append(' ').Append(Safe(() => FormatMember(module.ResolveMember(tok, typeArgs, methodArgs))));
                        break;
                    }
                    default:
                        sb.Append(" ?operand ").Append(op.OperandType);
                        break;
                }
                sb.Append('\n');
            }
        }

        private static string Safe(Func<string> f)
        {
            try { return f(); }
            catch (Exception e) { return "?" + e.GetType().Name; }
        }

        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

        private static string FormatConstant(object v)
        {
            switch (v)
            {
                case null: return "null";
                case float f: return "f" + BitConverter.ToInt32(BitConverter.GetBytes(f), 0).ToString("x8");
                case double d: return "d" + BitConverter.DoubleToInt64Bits(d).ToString("x16");
                case string s: return "\"" + Escape(s) + "\"";
                default: return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        public static string FormatMember(MemberInfo mi)
        {
            switch (mi)
            {
                case null: return "null";
                case Type t: return "T:" + FormatType(t);
                case FieldInfo f: return "F:" + FormatType(f.DeclaringType) + "::" + f.Name;
                case MethodBase m: return "M:" + FormatType(m.DeclaringType) + "::" + FormatMethodSignature(m);
                default: return mi.MemberType + ":" + FormatType(mi.DeclaringType) + "::" + mi.Name;
            }
        }

        public static string FormatMethodSignature(MethodBase m)
        {
            var sb = new StringBuilder();
            if (m is MethodInfo mi) sb.Append(FormatType(mi.ReturnType)).Append(' ');
            sb.Append(m.Name);
            if (m.IsGenericMethod)
            {
                sb.Append('<');
                Type[] args = m.GetGenericArguments();
                for (int i = 0; i < args.Length; i++) sb.Append(i == 0 ? "" : ",").Append(FormatType(args[i]));
                sb.Append('>');
            }
            sb.Append('(');
            ParameterInfo[] ps = m.GetParameters();
            for (int i = 0; i < ps.Length; i++) sb.Append(i == 0 ? "" : ",").Append(FormatType(ps[i].ParameterType));
            sb.Append(')');
            if (m.IsStatic) sb.Append(" static");
            return sb.ToString();
        }

        /// <summary>Namespace.Outer/Inner`1&lt;Arg&gt; with no assembly names, so both runtimes agree.</summary>
        public static string FormatType(Type t)
        {
            if (t == null) return "-";
            if (t.IsGenericParameter) return (t.DeclaringMethod != null ? "!!" : "!") + t.GenericParameterPosition;
            if (t.IsByRef) return FormatType(t.GetElementType()) + "&";
            if (t.IsPointer) return FormatType(t.GetElementType()) + "*";
            if (t.IsArray) return FormatType(t.GetElementType()) + "[" + new string(',', t.GetArrayRank() - 1) + "]";

            string name;
            if (t.IsNested && t.DeclaringType != null) name = FormatType(t.DeclaringType.IsGenericType ? t.DeclaringType.GetGenericTypeDefinition() : t.DeclaringType) + "/" + t.Name;
            else name = string.IsNullOrEmpty(t.Namespace) ? t.Name : t.Namespace + "." + t.Name;

            if (t.IsGenericType && !t.IsGenericTypeDefinition)
            {
                Type[] args = t.GetGenericArguments();
                var sb = new StringBuilder(name).Append('<');
                for (int i = 0; i < args.Length; i++) sb.Append(i == 0 ? "" : ",").Append(FormatType(args[i]));
                return sb.Append('>').ToString();
            }
            return name;
        }
    }
}
