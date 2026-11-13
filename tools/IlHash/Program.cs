using System;
using System.IO;
using System.Reflection;
using JunctionRules.Core;

namespace JunctionRules.Tools
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string managed = Environment.GetEnvironmentVariable("CSII_MANAGEDPATH");
            string describe = null;
            string copyDll = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--describe" && i + 1 < args.Length) describe = args[++i];
                else if (args[i] == "--copy" && i + 1 < args.Length) copyDll = args[++i];
                else managed = args[i];
            }
            if (string.IsNullOrEmpty(managed))
            {
                managed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local/share/Steam/steamapps/common/Cities Skylines II/Cities2_Data/Managed");
            }
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
            {
                string p = Path.Combine(managed, new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(p) ? Assembly.LoadFrom(p) : null;
            };
            Assembly game = Assembly.LoadFrom(Path.Combine(managed, "Game.dll"));
            Type nav = game.GetType("Game.Simulation.CarNavigationSystem", true);
            Type it = game.GetType("Game.Simulation.CarLaneSpeedIterator", true);
            Console.WriteLine("hash " + IlHasher.Hash(nav, it));
            foreach (var kv in IlHasher.HashEach(nav, it)) Console.WriteLine("  " + kv.Key + " " + kv.Value);
            if (describe != null)
            {
                File.WriteAllText(describe, IlHasher.Describe(nav, it));
                Console.WriteLine("described to " + describe);
            }
            if (copyDll != null) return CompareCopy(nav, it, Assembly.LoadFrom(Path.GetFullPath(copyDll)));
            return 0;
        }

        /// <summary>
        /// Compares the compiled copy's IL with the game's, method by method, after mapping the copy's names back
        /// (JR prefix, namespace). Identical IL proves the copy compiles to the game's code; differences are listed
        /// (compiler version differences can cause some; the source-level proof is tools/vanilla_copy.py check).
        /// </summary>
        private static int CompareCopy(Type nav, Type it, Assembly copy)
        {
            Type cnav = copy.GetType("JunctionRules.Vanilla.JRCarNavigationSystem", true);
            Type cit = copy.GetType("JunctionRules.Vanilla.JRCarLaneSpeedIterator", true);
            string a = IlHasher.Describe(nav, it);
            string b = IlHasher.Describe(cnav, cit)
                .Replace("JunctionRules.Vanilla.JRCarNavigationSystem", "Game.Simulation.CarNavigationSystem")
                .Replace("JunctionRules.Vanilla.JRCarLaneSpeedIterator", "Game.Simulation.CarLaneSpeedIterator");
            var ma = SplitMethods(a);
            var mb = SplitMethods(b);
            int same = 0, diff = 0, missing = 0, renumbered = 0, shapeOnly = 0;
            foreach (var kv in ma)
            {
                if (!mb.TryGetValue(kv.Key, out string body)) { missing++; Console.WriteLine("MISSING in copy: " + kv.Key); continue; }
                if (body == kv.Value) same++;
                else if (Canonical(body) == Canonical(kv.Value)) { same++; renumbered++; }
                else if (Operands(body) == Operands(kv.Value)) { shapeOnly++; Console.WriteLine("SHAPE ONLY: " + kv.Key); }
                else
                {
                    diff++;
                    Console.WriteLine("IL DIFFERS: " + kv.Key);
                    string dump = Environment.GetEnvironmentVariable("ILHASH_DUMP_DIR");
                    if (!string.IsNullOrEmpty(dump))
                    {
                        string n = diff.ToString("000");
                        File.WriteAllText(Path.Combine(dump, n + ".game.txt"), kv.Key + "\n" + kv.Value);
                        File.WriteAllText(Path.Combine(dump, n + ".copy.txt"), kv.Key + "\n" + body);
                    }
                }
            }
            foreach (var k in mb.Keys) if (!ma.ContainsKey(k)) { missing++; Console.WriteLine("EXTRA in copy: " + k); }
            Console.WriteLine($"copy IL vs game IL: {same} of {ma.Count} methods/field blocks identical ({renumbered} of them after " +
                              $"canonical renumbering of locals and branch labels), " +
                              $"{shapeOnly} differ only in control-flow shape (same calls, fields, types, strings and constants), " +
                              $"{diff} differ in content, {missing} missing/extra");
            return diff + missing == 0 ? 0 : 1;
        }

        /// <summary>
        /// Sorted multiset of the instructions that carry meaning independent of control-flow layout: member and type
        /// references (calls, field access, casts, struct construction as "ctor"), strings and numeric constants
        /// (short and long load forms merged). Branches, local loads/stores, dup and pop are left out.
        /// </summary>
        private static string Operands(string body)
        {
            var items = new System.Collections.Generic.List<string>();
            foreach (string raw in body.Split('\n'))
            {
                string l = raw.Trim();
                if (!l.StartsWith("IL_")) continue;
                string rest = l.Substring(l.IndexOf(' ') + 1);
                int sp = rest.IndexOf(' ');
                string op = sp < 0 ? rest : rest.Substring(0, sp);
                string arg = sp < 0 ? "" : rest.Substring(sp + 1);
                if (arg.StartsWith("M:") || arg.StartsWith("F:") || arg.StartsWith("T:") || arg.StartsWith("\""))
                {
                    if (arg.Contains("::.ctor(") && (op == "newobj" || op == "call")) op = "ctor";
                    if (op == "ldflda") op = "ldfld";
                    if (op == "ldsflda") op = "ldsfld";
                    if (op == "callvirt" || op == "constrained.") op = op == "callvirt" ? "call" : op;
                    items.Add(op + " " + arg);
                }
                else if (op.StartsWith("ldc."))
                {
                    string kind = op.Substring(4, 2);
                    string val = arg;
                    if (op.StartsWith("ldc.i4.") && op.Length > 7 && op != "ldc.i4.s") val = op == "ldc.i4.m1" ? "-1" : op.Substring(7);
                    items.Add("ldc." + kind + " " + val);
                }
            }
            items.Sort(StringComparer.Ordinal);
            return string.Join("\n", items);
        }

        /// <summary>
        /// Canonical form of one method's IL text: locals renumbered by first use (declarations sorted to match),
        /// branch targets as instruction ordinals, short/long forms (ldloc.s/ldloc.0, br.s/br) merged, offsets dropped.
        /// Two compilers' output for the same source then compares equal unless the instruction stream really differs.
        /// </summary>
        private static string Canonical(string body)
        {
            var lines = body.Split('\n');
            var ins = new System.Collections.Generic.List<(int off, string op, string arg)>();
            var locals = new System.Collections.Generic.Dictionary<int, string>();
            var other = new System.Collections.Generic.List<string>();
            foreach (string raw in lines)
            {
                string l = raw.Trim();
                if (l.StartsWith("IL_"))
                {
                    int sp = l.IndexOf(' ');
                    int off = Convert.ToInt32(l.Substring(3, sp - 3), 16);
                    string rest = l.Substring(sp + 1);
                    int sp2 = rest.IndexOf(' ');
                    ins.Add((off, sp2 < 0 ? rest : rest.Substring(0, sp2), sp2 < 0 ? "" : rest.Substring(sp2 + 1)));
                }
                else if (l.StartsWith("local "))
                {
                    var parts = l.Split(new[] { ' ' }, 3);
                    locals[int.Parse(parts[1])] = parts[2];
                }
                else if (l.Length > 0 && !l.StartsWith("maxstack")) other.Add(l);
            }
            var ordinal = new System.Collections.Generic.Dictionary<int, int>();
            for (int i = 0; i < ins.Count; i++) ordinal[ins[i].off] = i;
            int NextOff(int i) => i + 1 < ins.Count ? ins[i + 1].off : int.MaxValue;
            var localMap = new System.Collections.Generic.Dictionary<int, int>();
            int MapLocal(int n) { if (!localMap.TryGetValue(n, out int c)) { c = localMap.Count; localMap[n] = c; } return c; }
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < ins.Count; i++)
            {
                string op = ins[i].op, arg = ins[i].arg;
                string baseOp = op.EndsWith(".s") ? op.Substring(0, op.Length - 2) : op;
                foreach (string lo in new[] { "ldloc", "stloc" })
                {
                    if (op.StartsWith(lo + ".") && op.Length == lo.Length + 2 && char.IsDigit(op[op.Length - 1]))
                    {
                        baseOp = lo; arg = op.Substring(op.Length - 1);
                    }
                }
                if (baseOp == "ldloc" || baseOp == "stloc" || baseOp == "ldloca") arg = "L" + MapLocal(int.Parse(arg));
                else if (baseOp.StartsWith("b") || baseOp == "leave")
                {
                    if (int.TryParse(arg, out int rel) && (op.StartsWith("br") || op.StartsWith("b") && op != "box" && op != "break" || op.StartsWith("leave")))
                    {
                        int target = NextOff(i) == int.MaxValue ? -1 : NextOff(i) + rel;
                        arg = ordinal.TryGetValue(target, out int o) ? "#" + o : "@" + rel;
                    }
                }
                else if (op == "switch")
                {
                    var t = arg.Trim('[', ']').Split(',');
                    var outT = new System.Collections.Generic.List<string>();
                    foreach (string x in t) if (x.Length > 0) { int target = NextOff(i) + int.Parse(x); outT.Add(ordinal.TryGetValue(target, out int o) ? "#" + o : "@" + x); }
                    arg = "[" + string.Join(",", outT) + "]";
                }
                sb.Append(baseOp).Append(' ').Append(arg).Append('\n');
            }
            var decl = new System.Collections.Generic.List<string>();
            foreach (var kv in locals) decl.Add((localMap.TryGetValue(kv.Key, out int c) ? c.ToString("0000") : "unused") + " " + kv.Value);
            decl.Sort(StringComparer.Ordinal);
            other.Sort(StringComparer.Ordinal);
            return string.Join("\n", decl) + "\n" + string.Join("\n", other) + "\n" + sb;
        }

        private static System.Collections.Generic.Dictionary<string, string> SplitMethods(string text)
        {
            var d = new System.Collections.Generic.Dictionary<string, string>();
            string type = "", key = null;
            var sb = new System.Text.StringBuilder();
            foreach (string line in text.Split('\n'))
            {
                if (line.StartsWith("type ")) { Flush(); type = line; key = type + " [fields]"; continue; }
                if (line.StartsWith("  method ")) { Flush(); key = type + " :: " + line.Trim(); continue; }
                sb.Append(line).Append('\n');
            }
            Flush();
            return d;

            void Flush()
            {
                if (key != null) d[key] = (d.TryGetValue(key, out string prev) ? prev : "") + sb.ToString();
                sb.Clear();
            }
        }
    }
}
