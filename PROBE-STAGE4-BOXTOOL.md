# Probe kit: Junction Rules 0.4.1-probe (stage 4, yellow box selection tool; cars never stop on a zebra)

**0.4.1-probe (JR-T-009), what changed since 0.4.0:** new driving rule **Cars never stop on a zebra** (default on;
section 10). Driving changes only while Car driving is the Junction Rules copy; with the setting off cars drive exactly
as in 0.4.0. `docs/vanilla_copy_check_0.4.1.txt`: 0 unexplained differences, three new marked blocks (S2-25, S2-26,
S2-27; DEVIATIONS.md). Carries 0.4.0 (below), 0.3.6 and everything before.

**0.4.0-probe (JR-T-007), what changed since 0.3.6:** a tool to put real yellow boxes on junctions and in front of
building accesses, saved with the city and painted at once. **Paint only: cars drive exactly as in 0.3.6** (no
yellow-box driving rule exists yet; section 8). `docs/vanilla_copy_check_0.4.0.txt` equals 0.3.6's (0 unexplained
differences). Also: the T-junction through-side kerb fix (section 6) and the entrance box shape (section 4). Carries
0.3.6 (JR-T-008, people pass a car blocking their crossing; merged from 784ba28) and everything before it
(PROBE-STAGE3-PAINT.md).

Build to install: `~/Code/cs2-junction-rules/.staged/0.4.1-probe/`. While that folder holds a `STALE.md` it is a managed
build only: main does the Burst build in `STALE.md` (game closed, at the owner's quit), then puts the five sums into the
install block below. Until then the block refuses (placeholder sums `0000...`).

Built for game **1.6.4f1 (1097.158a)** only (same version gate: on another version saved boxes are not painted and the
copy does not run; the boxes stay in the save).

## 1. Install (game closed only)

```
(
  CS2="$HOME/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
  BACKUP="$HOME/Code/cs2-junction-rules/.staged/0.2.4-installed-backup"
  cd ~/Code/cs2-junction-rules/.staged/0.4.1-probe || exit 1
  if [ "$(ps -eo comm | grep -ci cities2)" != 0 ]; then echo "GAME IS RUNNING - stop, nothing installed"; exit 1; fi
  if [ -e STALE.md ]; then echo "STAGED BUILD IS STALE - see STALE.md, rebuild first; nothing installed"; exit 1; fi
  if [ ! -d "$CS2" ]; then echo "CS2 folder not found - nothing installed"; exit 1; fi
  sha256sum -c --strict - <<'SUMS' || { echo "SHA256 MISMATCH - nothing installed"; exit 1; }
0000000000000000000000000000000000000000000000000000000000000000  JunctionRules.dll
0000000000000000000000000000000000000000000000000000000000000000  JunctionRules.pdb
0000000000000000000000000000000000000000000000000000000000000000  JunctionRules.mjs
0000000000000000000000000000000000000000000000000000000000000000  JunctionRules_win_x86_64.dll
0000000000000000000000000000000000000000000000000000000000000000  JunctionRules_win_x86_64.pdb
SUMS
  if [ -d "$CS2/Mods/JunctionRules" ] && [ ! -e "$BACKUP/JunctionRules.dll" ]; then
    mkdir -p "$BACKUP" && cp -p "$CS2/Mods/JunctionRules/"* "$BACKUP/" && echo "backed up the installed files to $BACKUP" || { echo "BACKUP FAILED - nothing installed"; exit 1; }
  fi
  mkdir -p "$CS2/Mods/JunctionRules" &&
  cp JunctionRules.dll JunctionRules.pdb JunctionRules.mjs JunctionRules_win_x86_64.dll JunctionRules_win_x86_64.pdb "$CS2/Mods/JunctionRules/" &&
  echo "installed 0.4.1-probe"
)
```

Rollback: to 0.4.0 (boxes kept, zebra rule gone): the same block with `.staged/0.4.0-probe` and its sums (switching the
setting off in Options does the same without reinstalling). Further back: the block from `.staged/0.3.5-probe`
(PROBE-STAGE3-PAINT.md), then delete `Mods/JunctionRules/JunctionRules.mjs`.
A save with boxes still loads after a rollback or with the mod removed (section 7).

## 2. Using the tool

- Open it with the **yellow box button** at the top left of the screen, the shortcut **Ctrl+Y** (rebind in Options >
  Junction Rules > Yellow boxes > Yellow box tool shortcut, or the game's Keybinding page under "Junction Rules"), or the
  Options button **Open / close the yellow box tool**. The same button / shortcut closes it.
- Point at a junction, or at the road in front of a building's access (or at the building itself). What can take a box
  **glows**: yellow = click adds a box, orange = it has one, click removes it. A junction glows as a circle over the
  node, an entrance as a band across the road at the access point, entrance length long.
- **Left click** toggles. The paint appears / disappears at once (no Refresh, no restart).
- **Right click or Esc** leaves the tool (back to the game's default tool).
- Pointing at something that cannot take a box logs one line with the reason (once per reason in a row), e.g.
  `yellow box tool: node ... not selectable: highway`.

## 3. What can take a box

**Junction** = a `Game.Net.Node` with `Game.Net.Road`, at ground level, with **3 or more road arms** (a road edge that
starts or ends at the node). Excluded, with the reason the tool logs:

| Excluded | Why |
|---|---|
| 1 road arm (dead end) | nothing to keep clear |
| 2 road arms | a mid-road node, not a junction (an entrance box is the tool there) |
| road + paths only | a path joining a road is a crossing, not a junction |
| any highway arm (RoadFlags.UseHighwayRules) | no box on highways (the brief) |
| any elevated / tunnel arm, or node Elevation not ~0 | bridges, elevated roads, tunnels |
| roundabout (Roundabout component / node composition flag) | the box would cover the island |
| level crossing (node composition flag) | rail runs through it; the game's own rail rules apply |
| node with an Owner | part of a building's / asset's own network (not the city's road) |
| Deleted / Temp | gone, or only a tool preview |

**Entrance** = a building's road access: `Building.m_RoadEdge` + `m_CurvePosition`, the point on that road edge's centre
curve nearest the middle of the lot front (lot depth x 4 m forward of the building; Game.Buildings/RoadConnectionSystem.cs
371-385, 455-458, 505-509; BuildingUtils.CalculateFrontPosition 88-92). The game itself never connects a building to an
elevated / tunnel edge (358-361) or a Raised / Lowered side (375-381). Excluded: a sub-building / extension (pick the
main building: the tool does that when you point at the extension), no road access, access on a path / track, highway,
elevated / tunnel road, access on a road the building owns (its own sub-net inside its site; JR-T-007-r), a building that stands on the road itself (CanBeOnRoad / CanBeOnRoadArea without CanBeRoadSide),
an access inside a junction's node area.

## 4. Box shapes

- **Junction box**: as 0.3.5 (carriageway only, kerb to kerb round the corner curves, from each arm's stop line or the
  far side of a zebra), with the 0.4.0 T through-side fix (section 6).
- **Entrance box**: a band across the **whole carriageway** (kerb line to kerb line, both directions), **8 m along the
  road** (paint list `"entranceLength"`, 2..40 m), centred on the access point, following the road's curve, clipped
  to the edge's own part (never into a junction). Kerb lines use the same inset logic as the junction box (edge
  composition footway each side, the stop-bay rule, plus `kerbInset`). UK style: TSRGD diagram 1043 hatching (the
  paint mode setting applies), the same as at junctions.

## 5. Checks to run (one session, any city; Bevy Valley fine)

| # | Do | Expect |
|---|---|---|
| B1 | Load the city | log `yellow boxes: 0 junction box(es) and 0 entrance box(es) saved in this city`; no paint system running (`yellow box painting` not ON) |
| B2 | Ctrl+Y, the toolbar button, the Options button | tool on / off each time; log `yellow box tool: on` / `off` |
| B3 | Hover a 4-way, a T, a highway node, a bridge end, a path joint, a dead end, a roundabout | only the 4-way and the T glow; others log their reason |
| B4 | Click the T at **Moonflower St x Anchor St** | box appears at once; **the through-side kerb (south) is inset like the arms**: no paint on the pavement, no step halfway along |
| B5 | Click it again | box disappears at once |
| B6 | Hover the road in front of a house / shop, click | band across the road at the driveway, 8 m long, kerb to kerb |
| B7 | Right click, Esc | tool closes; Esc may also open the pause menu (game key), harmless |
| B8 | Save (city name first), reload | same boxes painted; log counts them |
| B9 | Change one arm's road type at a boxed junction (upgrade) | box stays (node updated in place) and repaints |
| B10 | Bulldoze a boxed junction's node / a boxed building | log `... was deleted, the box went with it`; paint gone |
| B11 | Make a boxed junction invalid (remove an arm so it has 2) | log `box dropped (node no longer valid: ...)` |
| B12 | Options > Log paint cost now | cost line as 0.3.x (`paint cost`); stays in the log |

## 6. 0.4.0: T-junction through-side kerb (Moonflower x Anchor)

Cause: the node's outer kerb curves split each kerb between the two neighbouring arms (Game.Net/GeometrySystem.cs
1945-1997, 2738-2778), and 0.3.5 inset each half by its own arm's footway. At Moonflower x Anchor the west Anchor arm
has a bus-stop bay (PrimaryStop composition: the pedestrian lane sits further out, so its footway computed as 2 m instead
of 3 m), so the west half of the through kerb was inset 1 m less, onto the pavement, with a step at the middle.
Fix (Core/CarriagewayOutline.cs): halves of one kerb (inner points meeting within 0.25 m) share one inset, the larger;
an arm with a stop bay uses the other side's footway when larger (`StopBayFootway`, a heuristic). The entrance box uses
the same logic. Tests: BoxThroughSideTests (synthetic T and the live Moonflower geometry).

## 7. Save safety: load a save without the mod (on a COPY)

Each box is a component on the game's own entity (`JunctionRules.Boxes.YellowBoxJunction` on the node,
`YellowBoxEntrance` on the building), saved by the game's component serializer, 3 bytes per entity
(version, length, flags; Core/BoxRecord.cs). Without the mod the game cannot resolve the type, logs
"Not serializable type", gives it the obsolete serializer, skips its size-prefixed block and builds the entities without
it (Colossal.Serialization.Entities: ComponentSerializer.cs 43-73, EntityDeserializer.cs 337-374 and 499-513,
ObsoleteComponentSerializer.cs 21-31 and 56-66). The next save without the mod has no boxes.

Test it on a copy (never on the owner's only save):

1. With 0.4.0 installed, put 2 junction boxes and 1 entrance box in a city; save as `<City> boxtest`; quit.
2. Copy that save file in `Saves/` to `<City> boxtest copy` (the `.cok` and any file of the same name beside it).
3. Move `Mods/JunctionRules` out of the Mods folder (e.g. to `.staged/0.4.0-removed/`); start the game; load the copy.
4. Expect: the city loads, no error popup; the game's logs (`Logs/`) show
   `Not serializable type` naming `JunctionRules.Boxes.YellowBox...` (twice at most); junctions and buildings work; no paint.
5. Quit without saving, move `Mods/JunctionRules` back, load the original `<City> boxtest`: boxes still there.

## 8. Driving

No yellow-box driving rule exists in 0.4.0 (stage 2 leaves exit queues stock; no code in Vanilla/ or Stage2/ reads the
box components). Plan for a separate brief: in the copy's CheckSpace (Vanilla/JRCarLaneSpeedIterator.cs 670), apply the
"don't enter unless the exit is clear" test only at boxed nodes / in front of boxed entrances when a "boxes only"
setting is on (component lookup in the job), keep level crossings as stock; Burst build and a live probe. Effort:
small to medium code, one Burst build, one probe session.

## 9. Report back

B1-B12 results, a screenshot of B4 and B6, the save-without-mod result (section 7, step 4 log lines), and any `FAILED` line.
0.4.1: Z1-Z10 results (section 10) and two consecutive timing-log lines with the zebra counters.

## 10. 0.4.1: cars never stop on a zebra (JR-T-009)

Owner rule (16 Nov 2026): a car never stops ON a zebra. Zebras split a car's path through a node into sections; a car
drives onto a zebra only when (a) the zebra is clear of people (the game's own pedestrian check; 0.3.2 never push
still applies), (b) with **Junction gap rule = Whole junction**: every vehicle conflict point (C) from that zebra up
to the next zebra or the end of the junction is clear (the game's own judgement per point, decided before the
zebra), and (c) there is room for the **whole vehicle** (trailers included) past the section in the exit queue.
Cs before the first zebra are decided at the entry spot W as before. Where the whole vehicle cannot wait between a
zebra and what lies behind it (the Cs before it, or another zebra), the two sections are **one decision** before the
first of them, chaining (Z C Z C Z with short gaps = one decision): a waiting spot is not a waiting spot if the
vehicle does not fit there.

Settings (Options > Junction Rules, driving group): **Cars never stop on a zebra** (default on; off = 0.4.0) and
**Cars never stop on a zebra: room margin (m)** (0-10 m, step 0.5, default 1 m; greyed while off). With the Stock gap
rule the switch still applies: zebras only (people on any zebra of a merged section, and room past), no C sections.

Room past (detail 1): from the end of the section, each vehicle ahead on the car's own path (this lane, then up to
three navigation lanes) counts with `rear distance + credit - (length + 1 m of every vehicle before it)`; there is
room when every value is at least the car's whole length + the margin. credit = that vehicle's own braking distance
when it moves forward (the game's own measure in its CheckSpace "is there space after the junction" test), so a
queue that keeps moving lets the next car in (no crawl), a standing queue holds it. An unknown rest of the path
(destination, parking, end of the read lanes) counts as free.

Signal-controlled crossings: red already holds the car at the stop line (game's own); when it shows green the rule
applies as at any zebra (people still crossing, Cs, room past). Mid-block crossings are nodes in the game too, so they
are covered (one section: the zebra; room past it).

Checks (copy on, gap rule Whole junction unless named; timing log on):

| # | Where | Expect |
|---|-------|--------|
| Z1 | Junction where the zebra is the first thing on the path (ZCCCC, e.g. a left turn across a side road zebra) | Car waits before the zebra until people AND the crossing traffic are clear; never stops on the zebra for a car |
| Z2 | Straight-on whose zebra is the last thing (CCCZ) | Waits at W for the Cs; then stops before the exit zebra only if people / no room |
| Z3 | Turn with Cs, a zebra, more Cs (CCZCC) | Waits at W; then before the zebra for the zebra + the last Cs |
| Z4 | Same as Z3 with a bus (or articulated truck) | No stop between the Cs and the zebra if it does not fit there: one wait at W |
| Z5 | Z C Z, zebras close together (central island zebra) | Car never stands on either zebra: waits before the first until both zebras, C and room past the second are clear |
| Z6 | Exit queue backed up to the zebra | Cars hold before the zebra (counter "no room past" rises); none stands on it |
| Z7 | Exit queue moving slowly | Cars still enter (no crawl gaps of a full car length) |
| Z8 | Mid-block zebra with a queue past it | Same as Z6 at the mid-block crossing |
| Z9 | Signalled crossing at a junction | Red holds as before; on green no car stops on the crossing for a queue |
| Z10 | Setting off, then on (no restart) | Off: 0.4.0 behaviour (cars may stop on zebras); log line says "never stop on a zebra switched in this window" |

Timing log (copy's line): `; never stop on a zebra on|off|switched in this window` and, while on, `; zebra entries
held: people N, Cs after N, no room past N (zebra sections merged, vehicle does not fit between: M)`: car-steps a car
stood held before a zebra by people, by a vehicle point after the zebra, by no room past, and car-steps standing at a
lane whose zebra sections were merged.

Watch for (report, no safeguard by brief): a car standing before a zebra for a long time because the exit never
clears (gridlock now forms before the zebra instead of on it); a person stepping onto a later zebra of a merged
section after the car went (the car then stops before that zebra; it may stand on the earlier one: same as 0.4.0).
