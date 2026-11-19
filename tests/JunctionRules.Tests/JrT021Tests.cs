using System;
using System.Collections.Generic;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// JR-T-021 (owner 19 Nov 2026): every car access (exit, entrance, two-way) is boxed on its own; G9's derived entrance
    /// boxes are gone. Entrance boxes are saved as ExitKind.Entrance keys in the same v2 record; a 1.0.1 save is migrated
    /// once (BoxRecordFlags.EntrancesExplicit): every entrance 1.0.1 derived becomes a saved key, lot-front marker keys that
    /// only marked the building "boxed" are dropped.
    /// Live fixture (read-only List, 1.0.1, 19 Nov 2026): the 10 buildings with derived entrance boxes. Building-frame keys
    /// are the live ones; road ids and metres along are illustrative (one road per building, along = local x).
    /// </summary>
    internal static partial class Program
    {
        private sealed class LiveBuilding
        {
            public int Id;
            public ExitKey[] Saved;
            public ExitKey[] Exits;
            public ExitKey[] Entrances;
            /// <summary>The entrance keys 1.0.1 drew (derived) for it.</summary>
            public ExitKey[] Derived;
        }

        private static ExitKey K(ExitKind k, double x, double z) => new ExitKey(k, x, z);

        private static List<ExitCandidate> Cands(IEnumerable<ExitKey> keys, long road)
        {
            var r = new List<ExitCandidate>();
            if (keys != null) foreach (ExitKey k in keys) r.Add(new ExitCandidate { Kind = k.ExitKind, Key = k, RoadId = road, AlongM = 100 + k.X });
            return r;
        }

        private static List<LiveBuilding> LiveDerived()
        {
            ExitKey D(double x, double z) => K(ExitKind.Driveway, x, z);
            ExitKey E(double x, double z) => K(ExitKind.Entrance, x, z);
            ExitKey F(double z) => K(ExitKind.Fallback, 0, z);
            return new List<LiveBuilding>
            {
                new LiveBuilding { Id = 50750, Saved = new[] { D(25.4, 21.3) }, Exits = new[] { D(25.4, 21.3) }, Entrances = new[] { E(-22.6, 20.4) }, Derived = new[] { E(-22.6, 20.4) } },
                new LiveBuilding { Id = 50751, Saved = new[] { D(25.4, 21.3) }, Exits = new[] { D(25.4, 21.3) }, Entrances = new[] { E(-22.6, 20.4) }, Derived = new[] { E(-22.6, 20.4) } },
                new LiveBuilding { Id = 50752, Saved = new[] { D(25.4, 21.3) }, Exits = new[] { D(25.4, 21.3) }, Entrances = new[] { E(-22.6, 20.4) }, Derived = new[] { E(-22.6, 20.4) } },
                new LiveBuilding { Id = 50794, Saved = new[] { F(64), D(3.2, 61.5) }, Exits = new[] { D(3.2, 61.5) }, Entrances = new[] { E(-44.1, 61.5) }, Derived = new[] { E(-44.1, 61.5) } },
                new LiveBuilding { Id = 74844, Saved = new[] { F(24) }, Exits = new ExitKey[0], Entrances = new[] { E(-3.2, 21.6) }, Derived = new[] { E(-3.2, 21.6) } },
                new LiveBuilding { Id = 74845, Saved = new[] { F(24) }, Exits = new ExitKey[0], Entrances = new[] { E(3.6, 22.1) }, Derived = new[] { E(3.6, 22.1) } },
                new LiveBuilding { Id = 320748, Saved = new[] { F(76) }, Exits = new ExitKey[0], Entrances = new[] { E(-102.0, 78.0) }, Derived = new[] { E(-102.0, 78.0) } },
                new LiveBuilding { Id = 362577, Saved = new[] { D(5.6, 5.6) }, Exits = new[] { D(5.6, 5.6) }, Entrances = new[] { E(-0.2, 5.7) }, Derived = new[] { E(-0.2, 5.7) } },
                new LiveBuilding { Id = 362576, Saved = new[] { D(12.2, 21.7) }, Exits = new[] { D(12.2, 21.7) }, Entrances = new[] { E(-12.6, 21.4) }, Derived = new[] { E(-12.6, 21.4) } },
                new LiveBuilding { Id = 944840, Saved = new[] { F(64), D(-34.9, 64.0), D(34.7, 64.0) }, Exits = new[] { D(-34.9, 64.0), D(34.7, 64.0) }, Entrances = new[] { E(0.0, 64.0) }, Derived = new[] { E(0.0, 64.0) } },
            };
        }

        private static void JrT021MigrationTests()
        {
            // ---- the 10 live buildings: everything the owner sees today stays, as saved keys
            int derivedTotal = 0, added = 0, markers = 0;
            foreach (LiveBuilding b in LiveDerived())
            {
                List<ExitCandidate> exits = Cands(b.Exits, b.Id), ins = Cands(b.Entrances, b.Id);
                // 1.0.1's derivation for this building (the rule the migration replays) = what the owner sees today
                var boxedExits = new List<ExitCandidate>();
                foreach (ExitKey k in b.Saved) { int i = ExitRules.Match(k, exits); if (i >= 0) boxedExits.Add(exits[i]); }
                List<int> old = AccessRules.EntranceBoxes(ins, boxedExits);
                Check(old.Count == b.Derived.Length, $"{b.Id}: 1.0.1 rule derives {b.Derived.Length} entrance box(es) as the live List shows");
                derivedTotal += old.Count;

                EntranceMigration m = AccessRules.MigrateEntrances(b.Saved, exits, ins, ExitRules.MaxBoxesPerBuilding);
                foreach (ExitKey d in b.Derived)
                    Check(m.Keys.Exists(k => k.Equals(d)), $"{b.Id}: derived entrance {d} saved as an explicit key");
                foreach (ExitKey s in b.Saved)
                    if (s.ExitKind != ExitKind.Fallback) Check(m.Keys.Exists(k => k.Equals(s)), $"{b.Id}: saved exit key {s} kept");
                Check(!m.Keys.Exists(k => k.ExitKind == ExitKind.Fallback), $"{b.Id}: lot-front marker dropped once its entrances are explicit");
                Check(m.Keys.Count == b.Saved.Length - Array.FindAll(b.Saved, k => k.ExitKind == ExitKind.Fallback).Length + b.Derived.Length, $"{b.Id}: nothing else added ({string.Join(", ", m.Keys)})");
                added += m.Added;
                markers += m.MarkersDropped;

                // after the migration nothing is derived: the drawn set = the saved keys (exits matched + entrance keys)
                int drawn = 0;
                foreach (ExitKey k in m.Keys)
                    if (k.ExitKind == ExitKind.Entrance ? ExitRules.Match(k, ins) >= 0 : ExitRules.Match(k, exits) >= 0) drawn++;
                Check(drawn == boxedExits.Count + old.Count, $"{b.Id}: same boxes drawn before ({boxedExits.Count} exit + {old.Count} derived) and after ({drawn} saved)");

                // idempotent: running again on its own result changes nothing
                EntranceMigration again = AccessRules.MigrateEntrances(m.Keys, exits, ins, ExitRules.MaxBoxesPerBuilding);
                Check(!again.Changed && again.Keys.Count == m.Keys.Count, $"{b.Id}: migration idempotent");
            }
            Check(derivedTotal == 10 && added == 10 && markers == 5, $"live: 10 derived entrance boxes -> 10 saved entrance keys; 5 marker keys dropped (got {derivedTotal} / {added} / {markers})");

            // ---- rules one by one
            var drive = new ExitCandidate { Kind = ExitKind.Driveway, Key = K(ExitKind.Driveway, 16, 16), RoadId = 9, AlongM = 116 };
            var entr = new ExitCandidate { Kind = ExitKind.Entrance, Key = K(ExitKind.Entrance, -6.2, 17.9), RoadId = 9, AlongM = 93.8 };
            var exits1 = new List<ExitCandidate> { drive };
            var ins1 = new List<ExitCandidate> { entr };
            // an entrance at the boxed exit's place (within 3 m along): 1.0.1 drew no second box, none is saved
            var near = entr; near.AlongM = 114.5; near.Key = K(ExitKind.Entrance, 14.5, 17.9);
            EntranceMigration mn = AccessRules.MigrateEntrances(new[] { drive.Key }, exits1, new List<ExitCandidate> { near }, 10);
            Check(!mn.Changed && mn.Keys.Count == 1, "entrance at the boxed exit's place: no key (1.0.1 drew none)");
            // an entrance key already saved (within KeyMatch): not doubled
            EntranceMigration mh = AccessRules.MigrateEntrances(new[] { drive.Key, K(ExitKind.Entrance, -6.0, 18.5) }, exits1, ins1, 10);
            Check(mh.Added == 0 && mh.Keys.Count == 2, "an entrance key already saved: not added twice");
            // a fallback key within 2 m of a real exit is that exit's box (re-keyed by the paint), never dropped
            var fbNear = K(ExitKind.Fallback, 15, 17);
            EntranceMigration mf = AccessRules.MigrateEntrances(new[] { fbNear }, exits1, ins1, 10);
            Check(mf.Keys.Exists(k => k.Equals(fbNear)) && mf.MarkersDropped == 0 && mf.Added == 1, "fallback key that matches a real exit: kept (it is that exit's box)");
            // a building with no real access: its fallback key stays untouched (nothing to make explicit)
            EntranceMigration mx = AccessRules.MigrateEntrances(new[] { K(ExitKind.Fallback, 0, 24) }, new List<ExitCandidate>(), new List<ExitCandidate>(), 10);
            Check(!mx.Changed && mx.Keys.Count == 1, "no real access: the fallback key stays (removable with the tool)");
            // marker with real exits but no entrance: no entrance becomes explicit, so the marker stays as it was
            EntranceMigration mm = AccessRules.MigrateEntrances(new[] { K(ExitKind.Fallback, 0, 40) }, exits1, new List<ExitCandidate>(), 10);
            Check(!mm.Changed && mm.Keys.Count == 1, "marker on a building without entrances: untouched");
            // the cap: keys beyond MaxBoxesPerBuilding are counted, never written
            var nine = new List<ExitKey>();
            var manyExits = new List<ExitCandidate>();
            for (int i = 0; i < 9; i++)
            {
                var c = new ExitCandidate { Kind = ExitKind.Driveway, Key = K(ExitKind.Driveway, i * 10, 0), RoadId = 1, AlongM = i * 10 };
                manyExits.Add(c); nine.Add(c.Key);
            }
            var threeIn = new List<ExitCandidate>();
            for (int i = 0; i < 3; i++) threeIn.Add(new ExitCandidate { Kind = ExitKind.Entrance, Key = K(ExitKind.Entrance, 200 + i * 10, 0), RoadId = 1, AlongM = 200 + i * 10 });
            EntranceMigration mc = AccessRules.MigrateEntrances(nine, manyExits, threeIn, ExitRules.MaxBoxesPerBuilding);
            Check(mc.Keys.Count == ExitRules.MaxBoxesPerBuilding && mc.Added == 1 && mc.NotFitted == 2, "cap: 9 exits + 3 entrances -> 1 saved, 2 counted as not fitted");
            // exit keys keep their order; entrances follow
            EntranceMigration mo = AccessRules.MigrateEntrances(new[] { drive.Key }, exits1, ins1, 10);
            Check(mo.Keys.Count == 2 && mo.Keys[0].Equals(drive.Key) && mo.Keys[1].Equals(entr.Key), "exit keys first as saved, then the new entrance keys");
            // empty record (legacy): nothing (the paint's MigrateLegacy goes first)
            Check(!AccessRules.MigrateEntrances(new ExitKey[0], exits1, ins1, 10).Changed, "legacy (key-less) record: left to MigrateLegacy");
        }

        private static void JrT021SaveTests()
        {
            // ---- the FireHouse01 case (owner 19 Nov, building 72840): two-way Driveway@(16,16) + entry-only Entrance@(-6.2,17.9).
            // Boxing ONE access saves ONE key; the other access is a separate key, toggled on its own.
            ExitKey two = K(ExitKind.Driveway, 16, 16), ent = K(ExitKind.Entrance, -6.2, 17.9);
            var onlyTwo = new ExitKeyList(); onlyTwo.Add(two);
            var both = new ExitKeyList(); both.Add(two); both.Add(ent);
            var s1 = new ArrayBytes { Out = new List<byte>() };
            ExitRecord.Encode(ref s1, BoxRecordFlags.EntrancesExplicit, ref onlyTwo);
            Check(s1.Out.Count == 4 + 5 && s1.Out[4] == (byte)ExitKind.Driveway, "FireHouse: box the two-way access = one key, no entrance box");

            // ---- round trip with an entrance key and the new flag
            var s = new ArrayBytes { Out = new List<byte>() };
            ExitRecord.Encode(ref s, BoxRecordFlags.EntrancesExplicit, ref both);
            byte[] bytes = s.Out.ToArray();
            Check(bytes[0] == ExitRecord.Version && ExitRecord.Version == 2 && bytes[1] == 2 + 5 * 2 && bytes[2] == (byte)BoxRecordFlags.EntrancesExplicit, "save: same v2 layout, flags byte carries EntrancesExplicit (2)");
            var src = new ArrayBytes { Data = bytes };
            var back = new ExitKeyList();
            Check(ExitRecord.Decode(ref src, ref back, out BoxRecordFlags fl, out byte v) && v == 2 && src.Pos == bytes.Length
                  && back.Count == 2 && back.Get(0).Equals(two) && back.Get(1).Equals(ent) && back.Get(1).ExitKind == ExitKind.Entrance
                  && (fl & BoxRecordFlags.EntrancesExplicit) != 0, "save round trip: entrance key and flag back, every byte consumed");

            // ---- new reader, old save (1.0.1: exit keys, flags 0): read as before; the flag is absent = migrate once
            var old = new ExitKeyList(); old.Add(two);
            var so = new ArrayBytes { Out = new List<byte>() };
            ExitRecord.Encode(ref so, BoxRecordFlags.None, ref old);
            src = new ArrayBytes { Data = so.Out.ToArray() };
            var oldBack = new ExitKeyList();
            Check(ExitRecord.Decode(ref src, ref oldBack, out BoxRecordFlags ofl, out _) && oldBack.Count == 1 && (ofl & BoxRecordFlags.EntrancesExplicit) == 0, "1.0.1 save read by this build: keys as saved, no EntrancesExplicit -> migrate");
            // and a 0.4.0-0.4.2 v1 record: still a legacy box (flags 0)
            byte[] v1 = ArrayBytes.Encode(new BoxRecord(BoxRecordFlags.None));
            src = new ArrayBytes { Data = v1 };
            var none = new ExitKeyList();
            Check(ExitRecord.Decode(ref src, ref none, out BoxRecordFlags v1fl, out byte v1v) && v1v == 1 && none.Count == 0 && v1fl == BoxRecordFlags.None && src.Pos == v1.Length, "v1 record: legacy box, flag absent");

            // ---- old readers of the new record
            // 0.4.0-0.4.2 (BoxRecord.Decode): flags byte only, the rest skipped by length
            src = new ArrayBytes { Data = bytes };
            Check(BoxRecord.Decode(ref src, out BoxRecord r042, out _) && src.Pos == bytes.Length && r042.Flags == BoxRecordFlags.EntrancesExplicit, "0.4.0-0.4.2 reader: consumes exactly the record (unknown flag bit kept, unused)");
            // 0.4.3-1.0.1 (ExitRecord.Decode, unchanged code): reads the entrance key as an ordinary 5-byte key...
            src = new ArrayBytes { Data = bytes };
            var oldReader = new ExitKeyList();
            Check(ExitRecord.Decode(ref src, ref oldReader, out BoxRecordFlags keptFlags, out _) && oldReader.Count == 2 && src.Pos == bytes.Length, "0.4.3-1.0.1 reader: same layout, both keys read, exact size");
            // ...and keeps the flags byte as it is (m_Flags raw) when it saves again
            var reSave = new ArrayBytes { Out = new List<byte>() };
            ExitRecord.Encode(ref reSave, keptFlags, ref oldReader);
            Check(reSave.Out.Count == bytes.Length && reSave.Out[2] == bytes[2], "an older build re-saving keeps the flag byte");
            // ...then matches keys against EXITS only (1.0.1 RebuildEntrance: ExitRules.Match): the entrance key finds no exit
            // and is dropped alone (logged), the exit key is untouched; 1.0.1 re-derives that entrance anyway (G9).
            var fhExits = new List<ExitCandidate> { new ExitCandidate { Kind = ExitKind.Driveway, Key = two, RoadId = 9, AlongM = 116 } };
            Check(ExitRules.Match(oldReader.Get(0), fhExits) == 0 && ExitRules.Match(oldReader.Get(1), fhExits) < 0, "1.0.1 reader: exit key matched, entrance key unmatched (dropped alone, no error)");

            // ---- without the mod: the whole block is skipped by its size prefix
            var stream = new List<byte>();
            stream.AddRange(BitConverter.GetBytes(bytes.Length));
            stream.AddRange(bytes);
            Check(BlockModel.SkipBlock(stream.ToArray(), 0) == stream.Count, "save without the mod: the block (with an entrance key) is skipped cleanly");
            Check((byte)BoxRecordFlags.EntrancesExplicit == 2 && (BoxRecordFlags.EntrancesExplicit & BoxRecordFlags.PaintOnly) == 0, "flag bit 2, apart from PaintOnly");
        }

        private static void JrT021RepoTests()
        {
            string root = Path.Combine(RepoRoot(), "JunctionRules");
            string Read(params string[] p) => StripComments(File.ReadAllText(Path.Combine(root, Path.Combine(p))));
            string store = Read("Boxes", "BoxStore.cs");
            Check(store.Contains("internal static bool MigrateEntrances(EntityManager em, Entity building)")
                  && store.Contains("(c.m_Flags & (byte)BoxRecordFlags.EntrancesExplicit) != 0 || c.Legacy")
                  && store.Contains("c.m_Flags |= (byte)BoxRecordFlags.EntrancesExplicit;")
                  && store.Contains("AccessRules.MigrateEntrances(saved, exitCands, inCands, ExitRules.MaxBoxesPerBuilding)"), "BoxStore.MigrateEntrances: gated by the flag, sets it, runs the Core rule");
            Check(store.Contains("new YellowBoxEntrance { m_Flags = (byte)BoxRecordFlags.EntrancesExplicit }"), "a component made by this build is marked explicit (never migrated)");
            Check(!store.Contains("ExitKind.Entrance) return false"), "AddExit saves entrance keys");
            Check(Read("Paint", "YellowBoxPaintSystem.cs").Contains("BoxStore.MigrateEntrances(em, e[i]);"), "paint load resolve migrates (after MigrateLegacy)");
            Check(Read("Systems", "CarDrivingSwitchSystem.cs").Contains("BoxStore.MigrateEntrances(EntityManager, buildings[i]);"), "driving rebuild migrates before reading bands");
            string tg = Read("Boxes", "BoxTargeting.cs");
            Check(tg.Contains("BoxStore.MigrateEntrances(EntityManager, building);") && tg.Contains("t.HasBox = exit.IsFallback ? HasFallbackKey(building) : BoxStore.HasExit(em, building, exit.C.Key);"),
                "tool / API: access lists migrate first; an entrance has a box only when its own key is saved");
            Check(!tg.Contains("AddBuildingMarker") && !tg.Contains("RemoveBuilding"), "no whole-building toggle from an entrance");
            string core = Read("Core", "BoxCommand.cs") + Read("Core", "BoxApi.cs");
            Check(!core.Contains("entrance_derived") && !core.Contains("EntranceDerived"), "entrance_derived refusal gone");
            Check(Read("Core", "BoxApi.cs").Contains("j.Prop(\"derived\", false);"), "List keeps 'derived' (API v1), always false");
            string api = Read("Api", "JunctionRulesApi.cs");
            Check(!api.Contains("AccessRules.EntranceBoxes(") && api.Contains("BoxFacts.FindExit(acc.Entrances, sk, out BoxFacts.Exit x)"), "API List: saved entrance keys, nothing derived");
            // G10 arrow unchanged: an entrance target still draws the In arrow from its own access way
            Check(Read("Boxes", "YellowBoxToolSystem.cs").Contains("AccessArrow.Build(ring, new V3(p.x, p.y, p.z), t.Exit.Way, t.Fallback)"), "G10 hover arrow unchanged");
        }
    }
}
