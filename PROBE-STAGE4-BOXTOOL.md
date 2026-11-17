# Probe kit: Junction Rules 0.4.11-probe (stage 4, yellow box selection tool; cars never stop on a zebra; cars obey yellow boxes; straight-sided boxes; boxes at car exits and entrances; vehicles leaving and turning into buildings give way; merged boxes; median; markings hidden; gaps G1-G21)

**0.4.11-probe (JR-T-017: 0.4.10 live gap G21), what changed since 0.4.10 (section 21):** a vehicle turning INTO a
building across traffic (a right turn in, left-hand traffic) now gives way like a vehicle leaving one: it waits in its
own lane BEFORE the yellow box (no box: where it would leave its lane) until the lanes its turn crosses have a gap long
enough for its WHOLE length to clear them, judged from the speed of the approaching vehicles (also those still in the
junction before the road). 0.4.10 let it into the box, where it waited at the game's own give-way with its nose at the
oncoming lane and then judged only the time to REACH that lane (van 380934 at No. 40 Alderwood, 19:00). A left turn in
that crosses no lane never waits. The yellow box room test into the building (G9) is unchanged. No change to the copied
game code (`vanilla_copy.py check`: 0 unexplained differences, 40 marked blocks / 318 lines as 0.4.10). Same setting as
the exits ("Vehicles leaving buildings give way"). Carries 0.4.10 and everything before.

**0.4.10-probe (JR-T-016: 0.4.9 live gaps G18, G19, G20), what changed since 0.4.9 (section 20):** cars no longer stop
inside a painted exit / entrance box: the driving rule now uses exactly the painted box (0.4.3-0.4.9 placed the driven
box metres away from the paint: up to 12-13 m at Alderbrook Street, because the road's lanes are measured from mouth to
mouth and the box position was measured from node to node). Boxes built as one box are driven as that one box. Every
yellow box (junction, exit, entrance; through traffic, vehicles leaving a building, the right-turner's re-check at its
in-box spot) now needs room that is **free now** past the box for the whole vehicle: a creeping queue no longer counts
as room. G20: boxes on one road less than 6 m apart (was 2 m) are built and driven as one box, with a log line per pair saying merged or not and why. Sweep (20.5): one box definition for every vehicle kind; a car never waits with any part in a box, also where the room past a box ends in another box, a boxed junction or a red light; on-duty emergency vehicles pass boxes. New timing counter at the end of the line. `docs/vanilla_copy_check_0.4.10.txt`: 0 unexplained differences (block conditions S2-28 / S2-28b / S2-37 and the S2-26 call changed, DEVIATIONS.md). Carries 0.4.9 and everything before.

**0.4.9-probe (JR-T-015: 0.4.8 live gaps G12-G15), what changed since 0.4.8 (section 19):** G16 boxes stay yellow in snow; G17 holds name the vehicle they wait for, so No Vehicle Despawn and the cs2 deadlock finder see loops through Junction Rules holds. G15 the Alderwood
deadlock (No. 40: a van waiting to turn in stood in No. 40's own exit box and the exit waited for it) cannot form: a
vehicle never waits with any part of it in a box (bands closer than its length are one band for it, so it waits before
the building's exit box too), and an exit does not wait for a vehicle that only waits for room INTO a building. G14
boxes on one road less than 2 m apart (exit + exit, exit + entrance) are built and driven as ONE box. G13 a vehicle held
at a driveway no longer makes road traffic stop to keep the drive clear for it. G12 the proof counters count the right
moment (the front passing the sidewalk spot) and a Go'd exit crosses pavement and road in one move. Diagnostics stay on
by default. `docs/vanilla_copy_check_0.4.9.txt`: 0 unexplained differences, 39 marked blocks / 311 lines (new S2-51 /
S2-51b; S2-22 condition and S2-26 extended; DEVIATIONS.md). Carries 0.4.8 and everything before.

**0.4.8-probe (JR-T-014: G7, G5 again, G9, G10, G11), what changed since 0.4.7 (section 18):** G7 vehicles leaving a
building now take the WHOLE decision **before the sidewalk** (owner rule: the pavement an exit crosses is a zebra):
people on the pavement, the near-lane gap (a road vehicle standing right next to the exit has priority; a lorry needs
the time to CLEAR the lanes it crosses), the far-lane gap and **room for its whole length on the lane it joins** (past
the box at a boxed exit); G5 Crescent St x Eastgate Lane boxable (corner kerbs drawn as the game draws them); G9
boxed buildings get boxes at their **entrances** too and cars turning in wait on the road unless they fit into the
building; G10 hover arrow from the box to the building access, no boxes at lot-front fallbacks; G11 people give way at
**unmarked** crossings (section 18.5). **Diagnostics are ON by default** in this probe (timing log, compact exit trace,
entrance box log, unmarked-crossing log): nothing to switch on. Driving changes only while Car driving is the Junction Rules copy.
`docs/vanilla_copy_check_0.4.8.txt`: 0 unexplained differences, 37 marked blocks / 289 lines (new S2-50 / S2-50b for G11;
S2-22, S2-26, S2-29, S2-32, S2-34, S2-35 extended; DEVIATIONS.md). Carries 0.4.7 and everything before.


**0.4.7-probe (JR-T-013m: #17 + gaps G1-G6), what changed since 0.4.6:** (1) #17 (JR-T-011, built on 0.4.3 as an
uninstalled "0.4.4"): boxes that overlap or touch are **drawn as one box** (one border, one hatch; each box still saved
on its own; the hover shows the merged outline); on a **divided road** an exit box covers only the building's side,
kerb to median; driving follows the merged paint (section 16.1). New setting **Hide road markings under yellow boxes**
(default **off** until K1 passes; section 16.2, with the G2 review fixes). (2) Gaps (section 17): G1 every way out of
a building or parking space gives way (spawn points, building and kerbside parking, building roads; new debug
setting "Exit trace (log)"); G3 a right-turner waiting in a box crosses only when its exit has room; G4 debug "Watch
the junction at camera centre (log)"; G5 chamfered junction box corners where mouths cannot slide to meet on the
kerb, and richer refusal logs; G6 **No parking in yellow boxes** (default on, runtime only). Driving changes only
while Car driving is the Junction Rules copy. `docs/vanilla_copy_check_0.4.7.txt`: 0 unexplained differences, new
marked blocks S2-35 / S2-36 (G1) and S2-37 (G3), extended S2-20..S2-23 (DEVIATIONS.md). Carries 0.4.6 (section 15)
and everything before.

**0.4.4-probe (JR-T-012), what changed since 0.4.3:** new driving rule **Vehicles leaving buildings give way**
(default on; section 15). Owner rule: "Cars coming out of ANY building must give way, and when that building exit is a
yellow box, must ALSO obey the yellow box rules." Driving changes only while Car driving is the Junction Rules copy;
with the setting off cars drive exactly as in 0.4.3. `docs/vanilla_copy_check_0.4.4.txt`: 0 unexplained differences,
three new marked blocks (S2-29, S2-30, S2-31) and extended S2-20 / S2-23 / S2-26 (DEVIATIONS.md). Carries 0.4.3 and
everything before.


**0.4.3-probe (JR-T-007c), what changed since 0.4.2:** building boxes sit at each building's **car exit** (the drawn
driveway, car park / depot gate), not the lot-front middle; a building with several exits gets **one box per exit**;
where a service building's own access road meets a public road, that node is a **junction** box (building road
yields unless signed). UI and log say "exit" instead of "entrance". Saved boxes from 0.4.0-0.4.2 move to the nearest
exit on load (or are dropped with a log line). Sections 12 and 14. Also first install of JR-T-007b's straight-sided
boxes, marking margin and hover outline (section 11; built as a "0.4.2" that was never installed). The box driving rule
of the installed 0.4.2 (section 13) now reads exit boxes: each boxed exit has its own band; boxes at a building's
access-road junction are junction boxes. `docs/vanilla_copy_check_0.4.3.txt`: 0 unexplained differences.
Carries the installed 0.4.2 (JR-T-010) and everything before.

**JR-T-007b (labelled 0.4.2 on its branch, never installed; ships in 0.4.3), what changed since 0.4.1:** box shapes and the tool's hover look only (section 11). Every
yellow box (junction and entrance) is now **straight-sided**: each arm's mouth line slides forward along its own arm,
parallel to itself, until the straight sides between neighbouring mouths stay on the carriageway; arms with priority
keep their mouths, give-way / STOP arms slide (signals: all arms equal). Every box edge keeps **0.5 m clear of the
painted edge** of zebras and stop / give-way lines (new Options slider; paint list `"markingMargin"`). The tool shows
the box it would add as a **thin outline** like Traffic's Priorities tool (white = adds, orange = removes) instead of
the glow. Fixes boxes running over the kerb at skewed junctions (Lindbergh x Anchor). No driving change (its vanilla copy equalled 0.4.1's).

**0.4.2-probe (JR-T-010, installed live 16 Nov), what changed since 0.4.1:** new driving rule **Cars obey yellow boxes** (default on;
section 13): at the boxes placed with the tool, a vehicle does not enter a box without room for its whole length past
it (UK right-turn exception, default on). Driving changes only while Car driving is the Junction Rules copy; with the
setting off cars drive exactly as in 0.4.1. `docs/vanilla_copy_check_0.4.2.txt`: 0 unexplained differences, two new
marked blocks (S2-28, S2-28b) and extended S2-20 / S2-23 / S2-26 (DEVIATIONS.md). Carries 0.4.1 (below) and everything before.

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

Build to install: `~/Code/cs2-junction-rules/.staged/0.4.10-probe/` (JR-T-016, branch jr-t-016; managed build only until main does the Burst build, see its `STALE.md`; the block below refuses on the `0000...` placeholder sums and while `STALE.md` exists). 0.4.9-probe (installed 17 Nov ~midday, `.staged/0.4.9-probe`, Burst from e6c7dd9, sums in commit 84a78fe) is the rollback; never write into `.staged/0.4.9-probe`, `.staged/0.4.8-probe` or `.staged/0.4.7-probe`. Never install the old `.staged/0.4.4-probe` (the paused JR-T-011 managed build). While `.staged/0.4.10-probe` holds a `STALE.md` it is a managed
build only: main does the Burst build in `STALE.md` (game closed, at the owner's quit), then puts the five sums into the
install block below. Until then the block refuses (placeholder sums `0000...`).

Built for game **1.6.4f1 (1097.158a)** only (same version gate: on another version saved boxes are not painted and the
copy does not run; the boxes stay in the save).

## 1. Install (game closed only)

```
(
  CS2="$HOME/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
  BACKUP="$HOME/Code/cs2-junction-rules/.staged/0.2.4-installed-backup"
  cd ~/Code/cs2-junction-rules/.staged/0.4.11-probe || exit 1
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
  echo "installed 0.4.11-probe"
)
```

Rollback: to 0.4.10 (JR-T-016, Burst from 9ea1ac4, installed 17 Nov afternoon; G21 not fixed): the same block with `.staged/0.4.10-probe` and its sums (commit 5ca57b6: dll 15b7467d..., pdb aa74dbd7..., mjs 9e8f8a39..., win dll 203e1ef0..., win pdb fdce39e2...). To 0.4.9 (JR-T-015, Burst from e6c7dd9, installed 17 Nov ~midday; G18 / G19 not fixed): the same block with `.staged/0.4.9-probe` and its sums (commit 84a78fe: dll cf74c32c..., pdb b200ce6b..., mjs 9e8f8a39..., win dll b2d18ddc..., win pdb 1c39bab0...). To 0.4.8 (JR-T-014, Burst from 8fca058, installed 17 Nov 07:5x; G12-G15 not fixed): the same block with `.staged/0.4.8-probe` and its sums (commit 242618d: dll 106bd828..., pdb 01c5735e..., mjs 9e8f8a39..., win dll d3178e58..., win pdb 8b27cc25...). To 0.4.7 (JR-T-013m, Burst from cd6a1eb, installed 17 Nov; no G7 sidewalk rule, no entrance boxes): the same block with `.staged/0.4.7-probe` and its sums (commit 3854a23). To 0.4.6: the same block with `.staged/0.4.6-probe` and its sums (commit 8bf6cf2). To 0.4.5 (JR-T-012 lot-exit build, Burst from 9639e71, installed 16 Nov): the same block with `.staged/0.4.5-probe` and its sums (commit bb676cd). To 0.4.4 (JR-T-012 first build, Burst from ba75f0b, installed 16 Nov): the same block with `.staged/0.4.4-probe-jr012` and its sums (commit 853ea2b). To 0.4.3 (installed 16 Nov, Burst from a14445d): the same block with `.staged/0.4.3-probe` and its sums (commit 1441805). To 0.4.2 (the JR-T-010 build: box driving rule, old entrance boxes; 0.4.3 exit keys are skipped by
its loader, so exit boxes fall back to one entrance box per building): the same block with `.staged/0.4.2-probe` and
its sums (full sums in commit c781df0). To 0.4.1 (boxes kept, box driving rule gone): the same block with `.staged/0.4.1-probe` and its sums
(dll af5b1322..., pdb db04729f..., mjs 9e8f8a39..., win dll 5acf0104..., win pdb d5762071...; full sums in the 0.4.1 commit 07cc519)
(or switch **Cars obey yellow boxes** off in Options). To 0.4.0 (boxes kept, zebra rule gone): the same block with `.staged/0.4.0-probe` and its sums (switching the
setting off in Options does the same without reinstalling). Further back: the block from `.staged/0.3.5-probe`
(PROBE-STAGE3-PAINT.md), then delete `Mods/JunctionRules/JunctionRules.mjs`.
A save with boxes still loads after a rollback or with the mod removed (section 7).

## 2. Using the tool

- Open it with the **yellow box button** at the top left of the screen, the shortcut **Ctrl+Y** (rebind in Options >
  Junction Rules > Yellow boxes > Yellow box tool shortcut, or the game's Keybinding page under "Junction Rules"), or the
  Options button **Open / close the yellow box tool**. The same button / shortcut closes it.
- Point at a junction, or at the road in front of a building's access (or at the building itself). What can take a box
  shows a **thin outline of the exact box it would get** (0.4.2, the same shape the paint uses; Traffic Priorities
  look, no fill): **white** = click adds the box, **orange** = it has one, click removes it.
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

- **Junction box** (0.4.2, section 11): carriageway only, **straight sides**: one straight line across each arm (its
  mouth, kerb to kerb, 0.5 m clear of the arm's zebra or stop / give-way line), joined to the neighbouring arms' mouths
  by straight sides on the kerb lines. Where a straight side would cut over a kerb corner, the give-way / STOP arm's
  mouth slides forward until it does not (priority arms keep theirs). The 0.4.0 T through-side fix (section 6) stays.
- **Entrance box** (0.4.2, same solver): straight sides across the **whole carriageway** (kerb line to kerb line, both
  directions), **8 m along the road** (paint list `"entranceLength"`, 2..40 m: the distance between the two road
  mouths), centred on the access point, clipped to the edge's own part and kept 0.5 m clear of the junction mouths and
  of any zebra or stop line on the road (never into a junction). On a bend the road mouths slide in until both long
  sides stay on the carriageway. Kerb lines use the same inset logic as the junction box (edge
  composition footway each side, the stop-bay rule, plus `kerbInset`). UK style: TSRGD diagram 1043 hatching (the
  paint mode setting applies), the same as at junctions.

## 5. Checks to run (one session, any city; Bevy Valley fine)

| # | Do | Expect |
|---|---|---|
| B1 | Load the city | log `yellow boxes: 0 junction box(es) and 0 entrance box(es) saved in this city`; no paint system running (`yellow box painting` not ON) |
| B2 | Ctrl+Y, the toolbar button, the Options button | tool on / off each time; log `yellow box tool: on` / `off` |
| B3 | Hover a 4-way, a T, a highway node, a bridge end, a path joint, a dead end, a roundabout | only the 4-way and the T show a thin white outline (their box); others log their reason |
| B4 | Click the T at **Moonflower St x Anchor St** | box appears at once; **the through-side kerb (south) is inset like the arms**: no paint on the pavement, no step halfway along; 0.4.2: a straight four-sided box, the Moonflower side on the Anchor north kerb line (section 11, S2) |
| B5 | Click it again | box disappears at once |
| B6 | Hover the road in front of a house / shop, click | white outline, then a straight-sided band across the road at the driveway, about 8 m long, kerb to kerb |
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

0.4.0 painted boxes only. From 0.4.2 cars obey them (section 11): the setting **Cars obey yellow boxes** reads the
saved box components (only boxed nodes and boxed entrances; every other junction stays as before). The hook is not
CheckSpace (the stock "space after junction" test, Vanilla/JRCarLaneSpeedIterator.cs 670) but the top of
CheckCurrentLane (blocks S2-28 / S2-28b), which sees every lane of the car's path; it shares the zebra rule's room
scan (section 10). Level crossings stay stock.

## 9. Report back

B1-B12 results, a screenshot of B4 and B6, the save-without-mod result (section 7, step 4 log lines), and any `FAILED` line.
0.4.1: Z1-Z10 results (section 10) and two consecutive timing-log lines with the zebra counters.
0.4.2: Y1-Y10 results (section 11), a screenshot of Y1 and Y3, and two consecutive timing-log lines with the box counters.

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

Review fix (JR-T-009-r): the decision for a zebra is taken at the **last point where the car can still stop before
it**. When the car, at its current speed, could not stop in normal braking (the game's braking distance) between its
next spot (W) and the zebra, the zebra is merged into W's decision for that moment: people on the zebra and room past
it hold the car at W. A slower car keeps the zebra's own spot, and the room past it is checked from before W already
(the car is held before the zebra, never too fast to stop for it). A zebra on the **next node** of the path (e.g. a
mid-block crossing just past the junction, on an edge shorter than the car) that starts within the car's length +
margin past the section counts too: people on it = no room, else room is needed past that zebra as well.

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
| Z11 | CCCZ with the zebra close to W (a few metres), exit queue backed up | Cars arriving at speed hold at W (counter "checked early at previous spot" rises), none ends up standing on the zebra |
| Z12 | Junction zebra, short edge, mid-block zebra, queue past it | Cars hold before the junction zebra, never with the tail on it (counter "further zebra in reach" rises) |
| Z13 | Timing log, any busy window | "lane objects out of curve order 0": the room scan assumes each lane's LaneObject buffer is sorted by curve position; a non-zero value = report it (the room verdict may then be wrong) |
| Z10 | Setting off, then on (no restart) | Off: 0.4.0 behaviour (cars may stop on zebras); log line says "never stop on a zebra switched in this window" |

Timing log (copy's line): `; never stop on a zebra on|off|switched in this window` and, while on, `; zebra entries
held: people N, Cs after N, no room past N (zebra sections merged, vehicle does not fit between: M; room/people checked
early at previous spot (cannot stop before zebra): E); room scan: further zebra in reach F, lane objects out of curve
order U`: car-steps a car stood held before a zebra by people, by a vehicle point after the zebra, by no room past,
car-steps standing at a lane whose zebra sections were merged, car-steps (moving) in which a zebra was decided early at
the spot before it, car-steps in which the room scan met a zebra on a later node, and car-steps in which it read a
lane's objects out of curve order (expected 0).

Watch for (report, no safeguard by brief): a car standing before a zebra for a long time because the exit never
clears (gridlock now forms before the zebra instead of on it); **mutual gridlock cycles**: the held car's exit queue
stands inside another junction (or across its zebra) and waits on traffic that the held car itself blocks (its cross
traffic, or the car behind it), so nothing ever moves. With No Despawn these cycles are **permanent**: note the
location (junction names / coordinates) and report it, do not wait for it to clear; a person stepping onto a later zebra of a merged
section after the car went (the car then stops before that zebra; it may stand on the earlier one: same as 0.4.0).

## 11. 0.4.2: straight-sided boxes, marking margin, hover outline (JR-T-007b)

**Hover outline.** Drawn like Traffic's Priorities tool (Traffic 0.2.13 OverlayRenderingHelpers.DrawNodeOutline): thin
lines through the game's `OverlayRenderSystem.Buffer.DrawLine`, no fill, 0.225 m wide (Traffic: feedback line width
0.3 x 0.75). **White** = a click adds this box (Traffic's hover colour), **orange** = it has one and a click removes it
(Traffic's warning orange 1, 0.65, 0). The outline is the exact shape the paint will use (same code, Boxes/BoxShapes.cs),
solved once per hovered target and again only when the target, its box state, the kerb inset / entrance length / margin
change or the node / edges are rebuilt (JR-T-007b-r). Where no box shape can be built there is no outline and the
target is **not selectable** (a click would save an invisible box; one Warn line per target); a target that already
has a box stays removable (orange) even if its shape fails now. The tool uses the paint's parameters from mod load /
tool start on (paint list file + Options), also before any box is painted.

**Marking margin.** Options > Yellow boxes > **Yellow box: gap to road markings (m)**, 0-2 m, default 0.5 m; the paint
list key `"markingMargin"` overrides it while present (also when the slider moves afterwards). Measured from the **painted edge** of the marking on the box
side: zebra = centre line + (lane width + the node lane's width offset) / 2; stop / give-way line = centre line +
(painted width = the larger of the lane's width and its geometry size) / 2. 0.4.0-0.4.1 put the box edge flush on the
zebra's painted edge and did not read stop lines at all. Entrance boxes keep the same gap from the junction mouths
and from zebras / stop lines on their road. A junction reads the stop / give-way lines on its own half of each arm only. Changing the slider redraws every box at once (log `yellow boxes: marking
margin X m (options); boxes rebuilt`).

**Straight sides (which mouth moves).** Each arm's mouth is a straight line kerb to kerb; the box's sides join
neighbouring mouths along the kerb lines. When a side would cut over a kerb corner or come within the margin of a
marking, a mouth slides forward along its own arm (staying parallel) until it does not:
1. Priority arms keep their mouths; only give-way / STOP arms slide (`priority-road`). An arm is give-way / STOP when
   its straight-through lane(s) carry the sign; an arm with no through movement (a T stem) when most of its lanes do.
   A single Give Way turn lane on a main-road arm (Code AM-17) leaves that arm priority.
2. Signals at the node: all arms equal.
3. All arms equal (`fallback-equal`), or the yielding arms alone cannot clear (`priority-moved`: priority arms moved:
   yielding arms alone cannot clear): the smallest total slide over all arms, never pushing a point where two car
   paths cross out of the box. Where no slide does both, there is **no box** and a Warn line names the node and the
   corner (owner rule: never onto the pavement or over a zebra / stop line).
Per box the log has `shape rule <code> (<meaning>); mouth shift per arm [...] m` plus per arm its sign status and
counts (`yield (2 lane(s), 2 yielding, through 0/0 yielding)`) and the conflict-point count.
Measured on the live fixtures: **Moonflower x Anchor** (T, STOP on Moonflower) `priority-road`, Anchor mouths 0 m,
Moonflower 3.58 m: a four-sided box with the Moonflower side on the Anchor north kerb line. **Lindbergh x Anchor**
(skewed 4-way, STOP on both Anchor arms) `priority-moved` (the Anchor arms alone cannot clear the Lindbergh SW
corner), Lindbergh SW 3.83 m, NE 3.93 m, Anchor 0 m; 0.4.0's box there ran up to 2.6 m over the pavement.

**Entrance boxes** use the same solver: the road's two directions are two priority mouths at the window ends (entrance
length apart), the driveway is the yielding side with no mouth of its own (the game gives it no geometry). On a
straight road the box is a rectangle; on a bend the mouths slide in (`priority-moved`) until both long sides
stay on the carriageway (no box, with a Warn line, when they cannot). 0.4.1 entrance check at Moonflower (house at the access 32 m along a 7.4-48 m edge part):
the band was 28-36 m, 12 m clear of the junction; the box seen near it was the next junction's box.

**Known, not in 0.4.2: where the entrance box sits.** It is still centred on the building's road access point
(Building.m_RoadEdge + m_CurvePosition = the lot front middle), not on the drawn driveway. Live Capper Street house
(UK Medium Detached 14 (L5), lot 24 x 48 m): its driveway sub-net (Invisible Car Path - 1xTwoway) meets the lot front
about 9.7 m to one side of the middle. Owner ruling 16 Nov: boxes go at each building's car EXIT; that is a separate
job (exit finding, one box per exit, save format). The band no longer covers the pavements (0.4.1's hover was a filled
band over the whole road width; 0.4.2 draws the kerb-to-kerb box).

**No hull fallback.** If no straight-sided box can be built (invalid ring), there is no box and a warning line, never
a convex-hull box over the pavement.

| # | Do | Expect |
|---|---|---|
| S1 | Hover junctions and driveways with the tool | thin white outline exactly where the paint later lands; orange on boxed ones; no glow |
| S2 | Box Moonflower x Anchor | four straight sides, Moonflower side on the Anchor north kerb line; log `shape rule priority-road` |
| S3 | Box Lindbergh x Anchor | no paint on any pavement corner; log `shape rule priority-moved` |
| S4 | Box a junction with zebras / stop lines, slider 0, 0.5, 2 | box edge flush / 0.5 m / 2 m short of the stripes and lines, redrawn at once |
| S5 | Entrance box on a bend | straight-sided, on the carriageway both sides |
| S6 | Signalled junction | log `signals`, rule `fallback-equal` |

## 12. 0.4.3: boxes at car exits; one box per exit; service access junctions (JR-T-007c)

**Where the box goes (owner ruling 16 Nov).** Every building box sits on the building's actual **car exit**, found from
game data in this order (Core/BuildingExit.cs, Boxes/BoxFacts.cs `Exits`; the log names the source per exit):
1. **Driveway**: an end node of the building's own sub-net car lane (prefab SubNet, e.g. "Invisible Car Path") that the
   game joined to the middle of a public road (GenerateEdgesSystem.cs 1240-1261 / 1846-1897 add `ConnectedNode(node, t)`
   to the road; ReferencesSystem.cs 773-780 lists the road in the node's ConnectedEdge; LaneSystem.cs 2146-2216 builds
   the car lanes from it). The box centre is the road point at that `t`, on the carriageway.
2. **Spawn location**: a car / cargo / parking spawn location of the building connected to a public road lane
   (SpawnLocationConnectionSystem.cs 398-448, 590-615). Dropped when within 3 m of a driveway on the same road.
3. **Junction**: the building's own access road ends on a node of a public road: boxed as a junction (below).
4. **Fallback**: none of the above: Building.m_RoadEdge + m_CurvePosition (the lot-front middle, as 0.4.0-0.4.2),
   logged as `fallback`.
Extensions (InstalledUpgrade) are read with their main building. Capper Street house (UK Medium Detached 14): the
exit is the driveway at the lot's right edge, 9.7 m from the old box centre.

**One box per exit.** Hovering a building or the road in front of an exit outlines the exit nearest the cursor; each
exit is added / removed on its own. Saved per exit (kind + position in the building frame to 0.1 m), at most 10 per
building.

**Service building access junctions.** A node where a building's own access road meets a public road (same building
for every owned arm, at least one public arm, 3+ road arms) is a normal selectable junction, boxed with the junction
mouth algorithm. Without signals and without any STOP / Give Way on the arms, the building road arm is treated as
yielding (log `building access junction: the building's own road arm yields`); signs and signals stand as set. Still excluded: nodes whose
road arms all belong to the building (inside a car park / depot), nodes of non-building assets, and nodes where two
buildings' roads meet. Bulldozing: a box lives as a component on the building (exits) or on the node (junctions);
deleting the building deletes its exit boxes; deleting the access road deletes or changes the node and its box is
dropped (log line); if an exit moves the box follows its key within 2 m, else it is dropped with a log line. No orphans.

**Save.** Exit boxes keep the component name `YellowBoxEntrance` (renaming it would orphan old saves); record
version 2 = [2][length][flags][count][kind, x, z per exit]. 0.4.0-0.4.2 boxes (version 1, no exits) move on load to the
exit nearest the old box centre (log `migrated`), or onto the junction node if that is the nearest exit, or are
dropped when the building has no exit (log `DROPPED`). A 0.4.2 build reading a 0.4.3 save reads the flags and skips the
rest by length; the game without the mod skips the whole block.

**Bends (F1).** The exit point is held inside the box, and the slide that keeps the long sides on the carriageway is
shared between both road mouths (sum-of-squares cost), so an exit off the window middle stays inside.

| # | Do | Expect |
|---|---|---|
| X1 | Hover the Capper Street house (or its drive) | outline centred on the drawn driveway at the lot's right edge, not the lot middle |
| X2 | Hover a car park or depot with two gates | each gate outlines on its own; two clicks = two boxes |
| X3 | Load a save with 0.4.2 entrance boxes | log `migrated ... nearest of N exit(s)` per box; boxes on the drives; none left at lot middles |
| X4 | Hover where a depot / hospital access road meets a public road (if one exists) | junction outline; log names the building road arm as yielding |
| X5 | Hover inside a car park | no outline; log `inside a building's own network` |
| X6 | Building with no drive (log `fallback`) | box at the lot-front middle as before |
| X7 | Exit box on a bend | drive inside the box, both long sides on the carriageway |
| X8 | Bulldoze a boxed house / its access road | box goes with it; log line; nothing left painted |

Live note 16 Nov (read-only): every service building checked (bus depots, road maintenance, hospital, police, fire,
car parks) joins its roads mid-road (driveways), none with an access road ending on a public node, so X4 may have no
site in Bevy Valley today.

## 13. 0.4.2 (installed): cars obey yellow boxes (JR-T-010)

0.4.3 reads the boxes from the exit storage instead (section 14); the rest of this section is unchanged.

Owner rule (16 Nov 2026, UK yellow box): at a boxed junction or a boxed building entrance a vehicle does not enter the
box unless it can clear it: there is room for its **whole length** (trailers included) plus the room margin on its
exit beyond the box. Room is the zebra rule's measure (section 10, one mechanism): a moving queue counts the braking
distance of the vehicle ahead, a standing one does not; vehicles already in the box ahead of it on its own path take
exit room first. Vehicles already in the box carry on; a vehicle that can no longer stop at the line carries on.
Only boxes placed with the tool count.

- **UK right-turn exception** (setting, default on): a vehicle turning across oncoming traffic (right in left-hand
  traffic) may enter and wait in the box when its exit has room and only oncoming traffic stops it. With **Junction
  gap rule = Whole junction** the box moves the decision to the stop line (nobody waits in the box), except that the
  turner gets a second waiting spot inside the box just before its first crossing with an oncoming straight lane (the
  same pair "turning across oncoming gives way" uses). Crossing points before that spot are decided at the line, the
  oncoming ones at the turn spot. With the Stock gap rule the box is the room test at the line only (the game's own
  gap judgement inside the junction, as before). The turn spot is never on a zebra and only where the whole vehicle
  fits clear of the zebras behind it (zebra rule wins); otherwise the turner decides at the line.
- **Entrance box**: a band of the painted length centred on the access. Main-road vehicles do not enter the band
  without room past it; a vehicle leaving the driveway does not join the road inside the band without room past the
  band on its target lane; a vehicle turning into the driveway is exempt.
- **With zebras**: both rules apply; the lower speed wins (a car held by either waits). People on a zebra still stop
  everyone; people pass after 2 s; no starvation safeguard.

Settings (Options > Junction Rules, yellow box group): **Cars obey yellow boxes** (default on; off = 0.4.1 **for boxes
only**: with **Cars never stop on a zebra** on, the Z O waiting gaps and the position-ordered room scan below still
apply, since they belong to the zebra rule) and
**Yellow boxes: turning across oncoming traffic may wait in the box** (default on, UK; greyed while off). The room margin slider is shared
with the zebra rule: **Room margin past a zebra or yellow box (m)**.

Where to watch: **Lindbergh St x Anchor St, node 874943** (Bevy Valley, box placed 16 Nov). In 0.4.0 a bus turning
from Anchor into Lindbergh eastbound stood stuck inside the box while the Lindbergh eastbound exit was a solid queue,
and the crossing traffic was blocked.

Checks (copy on; timing log on; gap rule as named):

| # | Where | Expect |
|---|-------|--------|
| Y1 | Lindbergh x Anchor, Lindbergh eastbound exit queue standing | Buses and cars from Anchor hold at the stop line, none stands inside the box; crossing traffic keeps flowing |
| Y2 | Same, queue starts moving | Vehicles enter again without waiting for a full gap (no crawl) |
| Y3 | Same, a bus | Holds until its whole 12 m (+ margin) fits past the box; a car behind it may not overtake its decision (same lane) |
| Y4 | Lindbergh x Anchor, right turn (left-hand traffic) with oncoming traffic, exit clear | Turner enters and waits inside the box before the oncoming lane (counter "right-turn waits" rises) |
| Y5 | Same, exit full | Turner holds at the line, does not enter |
| Y6 | Setting **Yellow boxes: turning across oncoming traffic may wait in the box** off | Turner holds at the line until the oncoming gap and room |
| Y7 | Unboxed junction nearby | Unchanged from 0.4.1 |
| Y8 | Boxed entrance, main road queue standing past it | Main-road cars stop before the band, the driveway stays open |
| Y9 | Same, vehicle leaving the driveway into a standing queue | Waits in the driveway until the target lane has room past the band |
| Y10 | **Cars obey yellow boxes** off, then on (no restart) | Off: 0.4.1 behaviour (vehicles may stop in the box); log says "obey yellow boxes switched in this window" |
| Y11 | A zebra with a car-length gap before the first crossing traffic (Z O C C), Whole junction | Car crosses the zebra once it is clear of people and the gap is free, waits in the gap for the crossing traffic; never on the zebra |
| Y12 | Same, gap occupied by a waiting car | The next car waits before the zebra |
| Y13 | Z13 (section 10) again | "lane objects out of curve order" may stay above 0: from 0.4.2 it is a diagnostic only (the scan reads lanes in curve order) |

Timing log (copy's line): `; obey yellow boxes on|off|switched in this window` and, while on, `; box entries held N,
right-turn waits N; entrance band holds: main road N, leaving driveway N` (car-steps standing held at a box line, a
turner waiting at its turn spot inside a box, cars held before an entrance band, cars held in a driveway). The switch
line also names the boxes the rule sees (`yellow boxes present: junctions yes|no, entrances yes|no`).

**Zebra sections refined (0.4.2, owner 16 Nov; applies with "Cars never stop on a zebra" too, not only at boxes).** A
section ends at the next WAITING SPOT, the next point where the whole vehicle (+ stop margin + room margin) stands
clear of every zebra behind it, which is not always the next zebra. Z O C C: when the gap O after a zebra holds the
whole vehicle before the first crossing point C, the car enters the zebra once it is clear of people and O is free
(room past the zebra up to O), then waits at O; the Cs are decided at O with the normal Whole junction decision. No
such gap (Z C C): as 0.4.1, the Cs decided before the zebra. Only right after a zebra: 0.2.3 has no other
mid-junction waiting spots, and a car too fast to stop at O decides the Cs before the zebra (as the JR-T-009-r early
decision). Whole junction only (with Stock there are no C sections). Inside a yellow box there is no O spot (the box
decides at the line); a gap BEFORE a box (a mid-block zebra, then the road up to the box) works the same way: the car
crosses the zebra into the free gap and the box rule applies at the box line.

**Lane objects out of curve order (live 0.4.1: 2 then 4 per window), cause:** the game keeps each lane's LaneObject
buffer sorted by `m_CurvePosition.y` (NetUtils.AddLaneObject / UpdateLaneObject), and for a driving car that value is
its reservation look-ahead (CarNavigationSystem.ReserveNavigationLanes ~2263: current position + braking distance at
its max speed, capped at its target on the lane), not where it is (`.x`). So a faster car close behind a slower one
(longer braking distance) sorts after it, and a car whose target lies short of the lane end sorts early. 0.4.1 read
the buffer in storage order and could therefore judge "no room" wrongly (a near car read after a far one). 0.4.2 reads
each lane in position order (Core ScanOrder: a buffer already in order is read straight, otherwise one pass per
vehicle read; the scan stops after a few vehicles and once the next vehicle is past the window). On lanes driven in
the inverse direction the counter reads the buffer from its end (no false reports there). The counter stays as a
diagnostic.

Deadlock notes (report, no safeguard by brief):
- Gridlock moves to the box entry: if the exit never clears, vehicles stand at the line indefinitely (by design).
- Opposing right-turners both waiting in the box can block each other's view of the oncoming gap (as in 0.4.1).
- A turner waiting in the box waits as long as oncoming traffic never gaps (UK practice; no timeout).
- A boxed entrance in front of a queue that never clears keeps the driveway vehicle waiting (it never joins).
- Vehicles from another arm already in the box are not counted against the exit (only the own path ahead).
- An unknown rest of the path (destination, parking, end of the read lanes) counts as free, as in section 10.
- Cars from another arm heading to the same exit are not counted: two arms can each see room and both enter, and the
  second may then stand in the box (only the own path ahead, and vehicles already in the box on it, count).
- Emergency vehicles are held by boxes like everyone else (no exemption in this probe): watch for an ambulance or fire
  engine waiting at a box line behind a standing exit queue and report it.
- Z O: a car waiting at O blocks the zebra for nobody, but cars behind it wait before the zebra (O occupied) until it goes.

## 14. 0.4.3: the box driving rule at car exits (JR-T-007c + JR-T-010 merged)

**What changed for driving.** 0.4.2 (installed) put one driving band per boxed building at Building.m_RoadEdge +
m_CurvePosition (the lot-front middle), read inside the job. 0.4.3 reads the saved exit keys instead: on the main
thread, every boxed exit of every boxed building is matched to the building's current exits the same way the paint
does (kind + position in the building frame, 2 m) and becomes its own band at that exit's road and position
(Boxes/BoxFacts.cs `BoxedExitBands`, Core/ExitBands.cs). A 0.4.0-0.4.2 box not yet migrated by the paint drives at the
exit nearest its old box centre (the same choice the migration makes). An exit through the building's own access
road is a junction box on that node and drives as a junction box (stop line, room past, right-turn exception). The
list is rebuilt when a box is added / removed / migrated / re-keyed, when the number of boxed buildings changes and
every 64 simulation steps (a drive re-wired to a rebuilt road); the log says `yellow boxes (driving): N boxed car
exit(s) on M building(s) handed to the box rule` each time it changes. At most **63 boxed exits** city-wide are
obeyed; more gives a Warn line with the count (report it). Junction boxes have no limit.

Per exit band (same rules as section 13, now per exit): main-road vehicles do not enter that exit's band without room
past it; vehicles leaving at that exit need room past its band on their lane; vehicles turning in are not held. A
building with two boxed exits on one road has two separate bands (a car passing both checks each one). Two boxed exits
closer than the band length (8 m: drives under about 8 m apart) have overlapping bands and act as ONE longer box: a car
leaving at one exit whose join point lies inside the other exit's band also needs room past that band when it lies
ahead (so it never stops inside the neighbour's box); a band behind it asks less than its own and changes nothing.
Known 0.4.3 limit: the band a car obeys is a fixed +-L/2 (entrance length) along the road around the exit, not the
painted outline (which is clipped at junctions / neighbours and shifted on bends); on a bend or next to a junction the
two can differ by a metre or two.

| # | Do | Expect |
|---|---|---|
| D1 | Load the city (0.4.2 entrance boxes) | log `migrated` lines (section 12 X3), then one `yellow boxes (driving): N boxed car exit(s)` line; N = boxed exits (junction-migrated ones not counted) |
| D2 | Watch a boxed house drive (Capper Street) with a queue on the road | queue stops short of the band centred on the **drive** (not the lot middle); a car leaving the drive waits for room |
| D3 | Box a second gate of a car park / depot (X2) | the driving line count goes up by one; each gate keeps its own band clear |
| D4 | Remove one exit box of a two-box building | its band goes (line count down by one); the other gate still kept clear |
| D5 | Lindbergh x Anchor (junction box, section 13) | as 0.4.2: no vehicle stops inside; timing log `box entries held`, `right-turn waits` |
| D6 | A boxed exit on a bend | band centred on the drive; the painted box (section 12 X7) covers the drive. Known limit: the obeyed band is the fixed +-L/2 around the exit, not the painted (clipped / bend-shifted) box; a queue may stop up to a metre or two off the paint there (note it, not a fail) |
| D7 | Timing log on | counters as 0.4.2: `box entries held N, right-turn waits N`, entrance band holds (main / leaving) |

Risks to watch: the job carries the exit list (about 1 KB, 0.4.2: 128 bytes) once per job and each car reads it
through a pointer (no per-car copy): still compare the navigation timing with 0.4.2 (timing log) and report a clear
slowdown; the 64-step refresh rebuilds every boxed building's exits on the main thread (BoxFacts.Exits, allocating):
cheap for a few boxed buildings, but watch for a periodic hitch (every 64 steps) with many; a building whose drive moved more than 2 m
since the box was placed has no band until the paint re-keys or drops it (log `without an exit now`).

## 15. 0.4.4: vehicles leaving buildings give way (JR-T-012)

Owner rule (binding wording, 16 Nov 2026): **"Cars coming out of ANY building must give way, and when that building
exit is a yellow box, must ALSO obey the yellow box rules."** Setting **Vehicles leaving buildings give way** (Car
driving group, default ON; OFF = exactly 0.4.3).

**Why vehicles pulled out (owner screenshot, truck out of a boxed exit across Alderwood).** Decompile 2026-11-13:
a drive's leaving lanes are node lanes owned by the road with `Unsafe | SideConnection | Yield`
(`Game.Net/LaneSystem.cs` 3646+, flags 3864-3876). Stock `CheckOverlappingLanes` gives way there only to reservations
(road vehicles never reserve the lane they are on) and to vehicles already on the crossed lane whose 2 s look-ahead
reaches the drive; a vehicle in the junction or on the road before it is not seen, and a slow truck needs much more
than 2 s. At a spawn / car park gate (`Connection` lane) the game never looks at the road at all
(`CarNavigationSystem` 2022-2024) and drops the vehicle onto the road lane at <= 3 m/s (1873-1886). JR 0.4.3 did not
change either (its band rule only asks for room past the box).

**What changes.** Every vehicle leaving any building waits at the exit until every road lane it crosses or merges into
is clear, judged as the Whole junction rule judges: an approaching vehicle on that lane, in the junction before it or
on the road before that junction counts when its braking distance + speed x (2 s + the leaving vehicle's own arrival
time) reaches the exit; a vehicle stopped before the exit does not; red skips it, amber skips it when it can stop. At
every exit, boxed or not. At a boxed exit the 0.4.3 room-past-the-band rule applies too: both must hold. Road vehicles
are never held for a leaving one (apart from the box bands). On-duty emergency vehicles (lights on) leave as in the
game. Works for left- and right-hand traffic (the crossed lanes come from the game's lane overlaps / road geometry;
the "turning across" counter label uses the city's own traffic-side flag).

| # | Do | Expect |
|---|---|---|
| E1 | Alderwood busy exit (the screenshot's), timing log on | trucks / cars wait at the drive while a vehicle comes from either side; go in a real gap; timing log `building exits: waits N (turning across N, spawn/gate N), go decisions N` |
| E2 | Same exit, a queue standing across it (unboxed) | the leaving vehicle waits (it cannot cross a standing vehicle); when the queue stopped short of the drive it may cross the near lane |
| E3 | A boxed exit with a full road | as 0.4.3: road queue stops short of the band; the leaving vehicle needs BOTH a gap and room past the band |
| E4 | A car park / depot gate on a busy road | vehicles appear on the road only in gaps (they wait unseen inside the building: `spawn/gate` count rises) |
| E5 | Ambulance / fire engine on a call leaving its station | leaves at once (`emergency passthroughs` > 0) |
| E6 | Setting OFF | driving as 0.4.3; the log line says `vehicles leaving buildings give way off` |
| E7 | Navigation timing (timing log) | compare with 0.4.3; only the vehicle at its exit scans (a few lanes, at most 24 lanes / 96 vehicles); `cap hits` should stay near 0 |

**Deadlock notes (no starvation safeguard, owner).** An exit onto a road that never clears waits for ever: a drive
onto a permanently queued road where the queue stands ON the drive, or onto a road with a steady stream at speed.
Signs: `waits` keeps rising at one place while `go decisions` stays flat; a building whose deliveries / commuters never
leave; behind a held driveway vehicle the building's internal queue grows (only the front vehicle scans). A vehicle
held at a spawn gate is invisible (unspawned inside the building); the game's "stuck" handling may still despawn it
after a long wait (not checked). Known limits: (0.4.5: exits through an area, a building lot onto the road, ARE gated, see below); trams are not counted as approaching traffic; on a two-way lane only vehicles driving with its curve count;
road vehicles about to turn INTO the building still count as approaching (cautious); when the work cap is hit the
vehicles not read do not count (errs to go). Report any exit that stays blocked with the timing line and a screenshot.

### 0.4.5-probe (JR-T-012 fix): lot exits were never gated in 0.4.4

**Cause.** The live 0.4.4 log showed `spawn/gate 0` and `boxed spawn exit no room 0` although a truck still pulled
out of a boxed exit. 0.4.4 hooked the wait into the `Connection` lane tail only (S2-30, `IL_09cd`). But a building's
lot (the lane from the spawn point / gate across the lot to the road) is a `ConnectionLane` with
`ConnectionLaneFlags.Area`, and that becomes `CarLaneFlags.Area`, not `Connection` (`InitializeSystem.cs` 288,
`CarNavigationSystem.cs` 672, 1037, `TripNeededSystem.cs` 773). `CarNavigationSystem` runs the Area branch
(`if ((LaneFlags & Area) == 0)` ~1596, `MoveAreaTarget` 1795-1803 / 2406) without ever calling the lane speed
iterator, so the road lane the lot joins is never looked at and S2-30 is never reached: the vehicle drove the area
lane at full speed and entered the road. The driveway case (road-owned node lane, S2-29) was fine (24 waits logged).

**Fix (S2-32, S2-33; DEVIATIONS.md).** A vehicle on an Area lane whose next navigation lane is a public road car lane
brakes to stop 1 m before the join point (`ExitAreaHold`) while the same gap decision as every other exit
(`ExitJoinLane` + `ExitJoinWaits`, shared with S2-30; boxed exit: room past the band too) says wait; S2-33 keeps it on
its area lane (like S2-31 for connection lanes). It only scans when it has to brake (`hold < max speed`); a vehicle
that cannot stop in time, or is already clearly past the stop point, is left to the game (`CanHoldArea`). On-duty
emergency vehicles keep stock behaviour. Timing log: lot exits count in `spawn/gate` (waits) and `go decisions`.
Setting OFF = 0.4.4 / 0.4.3. `docs/vanilla_copy_check_0.4.5.txt`: 0 unexplained differences, 31 marked blocks.

Not yet tested live (managed build only): decompile-derived diagnosis. E4 now also covers a lot exit: a held lot
vehicle waits visibly on its lot lane just before the road and goes in a gap (one that cannot stop in time rolls out).

### 0.4.6-probe (JR-T-012 fix 2): the lot-exit scan started too late

**Live 0.4.5** (owner, 16 Nov 22:41): a tractor-trailer leaving an industrial lot drove into a boxed Alderwood exit,
turned right across both lanes and stood in the box; the next boxed exit west: a dark truck pulled into the near
lane under a Delivery Van. Log: `spawn/gate 0` still, `emergency passthroughs` 64 in one window.

**Causes (decompile + code, no live trace; the probe counters below will say if another way out exists).**
1. The stock area branch hands the vehicle over to the road lane as soon as the end of its area path is within
   max(look-ahead, braking distance at 11.1 m/s + 1 m + pivot) (`CarNavigationSystem` num8, 1767-1772;
   `MoveAreaTarget` 2529): up to ~20 m before the join point. 0.4.5 judged a vehicle only when its braking speed to the
   stop point was already below its current limit (`hold < currentMax`), and S2-33 held it only if S2-32 had judged it
   busy in that same update. A vehicle was therefore handed over to the road lane unjudged and drove on as a road
   vehicle (no give-way on a road lane).
2. The distance to the join point was cut by half the WHOLE train (trailers): a tractor-trailer looked "already
   past the stop point" (`CanHoldArea` false = left to the game) ~8 m too early.
3. An area lane had to have a building in its owner chain and the joined lane an edge as owner (a lot beside a
   junction joins a junction node lane).
4. The area branch ends in `goto IL_0a0a`, past the later `AddCounts`: S2-32's counters were never added (that is
   `spawn/gate 0`, not proof that nothing waited). All exit counters were also added per car-step.
`emergency passthroughs` is NOT a misclassification: priority 108 exists only with `CarFlags.Emergency`
(`VehicleUtils.GetPriority(Car)` 654-657, set only by the ambulance / fire / police / transport AIs); one on-duty
vehicle at a driveway or lot added one per car-step (64 car-steps = a few vehicles).

**Fix.** S2-32 judges every vehicle within `ExitGiveWay.AreaScanMetres` (braking distance at 11.1 m/s + front offset
+ 15 m, at least 25 m) of the join point on every update, measured from the vehicle's front
(`prefabObjectGeometryData.m_Bounds.max.z`), so the hold and S2-33 are in place before the stock hand-over. An area lane
needs no building owner; the joined lane may be a junction node lane. Driveway (S2-29) and Connection (S2-30/31)
exits unchanged. Counters are per vehicle episode (shared event table, kinds) and S2-32 feeds them. New probe
S2-34 (counting only) adds `hand-overs to the road from: lot N, gate N, other N` to the building-exits part of the
timing line: lot = area lane, gate = Connection lane, other = any other building-owned lane (should stay 0; if not,
that is a way out of a building the hooks do not cover: report the line). `docs/vanilla_copy_check_0.4.6.txt`: 0
unexplained differences, 32 marked blocks. Setting OFF = 0.4.3 driving.

Not yet tested live (managed build only). E4: a lot vehicle (also a tractor-trailer and a van) waits visibly on its
lot lane just before the road while a vehicle approaches, joins in a gap, never ends overlapping a road vehicle;
the log's `waits` / `go decisions` now count vehicles, not car-steps.
## 16. 0.4.7: touching boxes drawn as one; exit boxes stop at a median; markings hidden (JR-T-011, #17)

### 16.1 Touching boxes drawn as one; exit boxes stop at a median (JR-T-011 part A)

**What changed.** The paint groups the boxes whose outlines overlap or touch (within 5 cm) and draws each group as
ONE box: the union of the members' straight-sided outlines (still straight-sided, every side on a member's side, so
on the carriageway and clear of markings as each member was), one border round the outside, one hatch whose grid is
laid out once for the whole group (Core/BoxMerge.cs `BoxMerge.Merge`, hatch anchor `MergedBoxGroup.Anchor`). An exit
box next to a junction box is joined across the marking margin gap between them (the exit window keeps that margin
from the junction mouth) only when their facing sides are parallel (3 deg), at most margin + 0.1 m apart and share at
least 1 m: a zebra or stop line between them always leaves a wider gap, so markings are never painted over. Groups are
rebuilt only when a box changes and cached per group. If a union ever fails its checks the group's boxes are drawn
one by one as in 0.4.3 (log `merge with N touching box(es) failed (...)`, report it). Storage is unchanged: every
box is its own saved record. The tool's hover shows the merged outline of the group the target belongs to (or would
join). Log: `yellow boxes: N box(es) drawn as M (K merged group(s), ...) in X ms` and one `N boxes drawn as one` line per new merged group; the paint status says `... box(es) painted as M`.

**Median (owner, Ironplace Avenue 16 Nov).** On a road whose cross-section has a divider that blocks traffic (raised
median, barrier: the game gives the lanes on each side a different carriageway number) an exit box spans only the
building's side, from its kerb to the median kerb (`yellow boxes: exit ... kerb to median`); undivided roads stay
kerb to kerb (`kerb to kerb`). Two exits facing across a median are never merged. Driving: the exit's band holds only
the lanes on the building's side; lanes on the far side drive past as if there were no box. Live read (read-only,
16 Nov): Ironplace Avenue is a Medium Road Divided, car lanes at +-2.5 / +-5.5 m (3 m), parking +-8 m, median -1..1 m;
the box should be about 8 m across (kerb at 9 m to median at 1 m).

**Driving follows the paint.** Bands on one lane that overlap or touch are one interval for the hold (a car is held
once before the whole group, never let into one exit's band and then held inside the next). A band that ends within
2.5 m of the lane end where the car's next lane belongs to a boxed junction also needs room past that junction box
(the paint joins the two). 0.4.7: a car LEAVING a driveway inside a band (S2-30 / S2-32 / S2-35 / S2-36) reads the
same band set: only bands on its side of a median, touching bands as one (it waits for room past the whole merged box,
not just its own exit's band), and room past a boxed junction the band runs into.

| # | Do | Expect |
|---|---|---|
| M1 | The owner's screenshot spot: two exits facing each other across an undivided road (boxes drawn on top of each other in 0.4.3) | ONE yellow box: one outer border, a continuous hatch with no line or seam inside, its outline the outside of both boxes |
| M2 | Remove one of the two exit boxes (tool, orange) | the other exit's box is drawn alone, its own 0.4.3 outline; save + reload: still only that one |
| M3 | Add it back | the merged box again; hover over the second exit before clicking shows the merged outline in white |
| M4 | Two staggered exits on one kerb whose boxes overlap; two exits end to end | one box each pair, straight sides, no notch at the joins |
| M5 | An exit box next to a boxed junction (no zebra between them) | drawn as one box; with a zebra or stop line between them: two boxes, the marking left clear |
| M6 | Ironplace Avenue (Medium Road Divided; the owner's screenshot): hover and box a building exit | the hover and the box cover only the building's side, kerb to median, about 8 m wide; nothing on the median or the far carriageway |
| M7 | Ironplace Avenue: box an exit on the far side opposite it | two separate boxes, one each side, not merged across the median |
| M8 | Ironplace Avenue, queue on the far carriageway | far-side cars queue through past the box (not held); near-side cars keep the box clear |
| M9 | A queue through a merged pair of exit boxes | the queue stops before the whole merged box, not between the two |
| M10 | Timing log on, a city with many boxes | no hitch when adding / removing a box (groups rebuilt only then); report `merge ... failed` lines |

Risks to watch: the union is new code (rotated / bent boxes, three or more in a chain); the junction-exit join reads
the bridge only where the sides are parallel, so on a skewed junction the exit may stay a separate box (note it, not
a fail); the median rule relies on the road's divider piece blocking traffic (assumed for vanilla divided roads; a
RoadBuilder road with a painted-only centre stays kerb to kerb, as it should).

### 16.2 Road markings hidden under the boxes (JR-T-011 part B; G2 review fixes)

New setting **Hide road markings under yellow boxes** (Yellow boxes group, default OFF since the G2 review fixes, until K1 passes). Inside every painted box (the
merged outline of section 16.1) the road's own markings are not drawn: lane dashes, edge lines, the centre line (double
solid too, owner 16 Nov: fine), node markings, arrows and the road's painted speed numbers ("40"). Outside the box
nothing changes; on a divided road the box stops at the median (section 16.1), so the far side keeps its markings.
Render-only: the game's own per-lane cut list (`CutRange`) on marking lanes and the tool tag `Hidden` on marking
objects; both are taken off before every save and put back the next frame (docs/MARKINGS-UNDER-BOX-RESEARCH.md).
Driving is not affected. Log line on every box change:
`yellow boxes: road markings under the boxes hidden: N marking lane(s) cut, M marking object(s) hidden; x ms`.

> **K1 is a MUST-PASS GATE.** Run it first. If any marking is missing in K1, switch the setting OFF, keep the save
> copies and report: the setting must not stay ON until K1 passes.

| # | Do | Expect |
|---|---|---|
| **K1 (GATE)** | **Load WITHOUT the mod.** With the setting ON and boxes hiding markings (dashes, a centre line, a "40", an arrow under boxes; note the spots): make a **manual save**, then keep playing until an **autosave** is written. Quit. Copy both saves (as section 7), disable or remove the mod, start the game and load **each** copy | at every boxed spot **every** marking is drawn normally: lane dashes, edge lines, centre lines (double solid too), the "40"s and the arrows; nothing missing, nothing cut short. Any gap = FAIL: setting OFF, report with the save names |
| K2 | Load the city with the setting on; look at the owner's screenshot site (two facing exit boxes, "40"s) | no lane dashes and no "40" inside the hatch; dashes stop at the box border and carry on outside; the road surface inside looks like the road outside (no patch) |
| K3 | A boxed 2-lane road with a centre line (double solid or dashed) under a box | the centre line stops at both box sides and resumes outside (owner: hiding it is fine) |
| K4 | A junction box with arrows / give-way or stop lines near it | arrows inside the box gone; stop / give-way lines and zebras (outside the box by the marking margin) still drawn |
| K5 | A "40" or arrow straddling the box border | gone if about a quarter or more of it lies in the box, else kept whole (never cut in half) |
| K6 | Look just outside a box along a dashed lane line | dashes may sit slightly differently next to the box (the game re-fits the dash pattern to the shortened line): note if it looks odd |
| K7 | A merged pair of boxes (section 16.1 M1) | markings hidden under the whole merged box, no strip of dashes left at the join |
| K8 | Ironplace Avenue (divided): an exit box on one side | markings on the box's carriageway hidden inside it; the median and the far carriageway's markings untouched |
| K9 | Remove a box with the tool | its markings come back at once (a merged partner keeps hiding under its own outline) |
| K10 | Options: switch the setting off, then on | off: markings show through the box exactly as 0.4.3; on: hidden again |
| K11 | Edit the boxed road (upgrade / change lanes / bulldoze a node next to it) | after the edit the markings inside the (rebuilt) box are hidden again; nothing hidden outside it; a hidden arrow may flash for a frame while a tool previews over it |
| K12 | Save (and wait for an autosave), keep playing | markings stay hidden after the save (a one-frame flicker at most) |
| K13 | Log check | the `marking lane(s) cut` line per box change; any `left alone (already cut / hidden by the game or a tool)` count: report it; any `taking the marking cuts off before the save failed` or `putting the road markings back failed` error: report it at once (K1 again on that save) |

## 17. 0.4.7: gaps G1-G6 (JR-T-013)

Six fixes from the owner's 0.4.6 session (16 Nov). Run them after section 16. Driving changes (G1, G3) act only while
Car driving is the Junction Rules copy. Turn on **Log navigation timing** for the counters below.

### G1: every way out of a building or parking space gives way (JR-T-013a)

**What changed.** In 0.4.6 every timing window showed `hand-overs to the road from: lot 0, gate 0, other 2..28`. The
'other' was a building's **spawn location object**: the path starts at that entity (`TransformTarget`), it is tied to
the public road lane, and the stock `TransformTarget | ParkingSpace` branch puts the hidden vehicle straight onto the
road lane with no gap or box check. The same branch also carried building parking / garages, **kerbside parking**
and a building's own road meeting a public junction. Now:
- **S2-35** (spawn point, building parking, kerbside parking): the vehicle waits at its spawn point (hidden) or in its
  space, with the same decision as every exit: gap on the lane it joins and every lane it crosses, then room past the
  band at a boxed exit (the merged band on its side of the road, as cars driving along it: section 16.1). Kerbside
  parking is included (owner rule: a building exit is a yielding arm).
- **S2-36** (building road): a vehicle on a building's own road stops 1 m before the public junction and decides the
  same way; a signalled arm follows its signal.
- **S2-33**: every held exit vehicle stays on its lane (the game cannot hand it to the road while it waits).
- Junction joins take their crossing points from the joined lane's own overlaps (the old side-by-side rule only fits a
  straight road).
- Caps: only vehicles still ahead of the exit and within reach count against the 96-vehicle cap, and (review fix) not
  a vehicle standing still short of the exit (it never blocks, so a long standing queue no longer reads as "dense
  traffic: wait" for ever); hitting the cap means busy (wait); the lane cap went 24 -> 48 and still means go.
- Review fix: cars queued behind the held car on its own building road / drive no longer count as traffic it must
  wait for (only the lanes it crosses or joins do).
- Not hooked: a vehicle whose path starts directly on the road lane (a building with no spawn point; or a cargo
  spawn point whose path starts on its road lane, in the game code only off-road work vehicles inside a lot); it is
  already in the lane, so holding it would leave it standing in traffic. Counted instead (see the log line below);
  expected 0 or close.
- Known (not changed): a kerbside car waiting to pull out is no longer a parked car for the game, so its space shows as
  free and another car may be sent to it meanwhile (the newcomer re-paths or waits behind it).

**Settings.** **Vehicles leaving buildings give way** (existing, default on) now covers all of the above. New **Exit
trace (log)** (Debug, default **off**; needs the setting above and the copy): one line when a vehicle starts waiting at
an exit and one when it joins the road, at most 20 lines a second, with a per-second dropped / lost summary.

**Log lines / counters.** The timing line keeps its 0.4.6 text and appends:
`..., hand-overs to the road from: lot L, gate G, other O, spawn point S, building parking P, kerbside parking K, building road B, drive D; hand-overs not judged U; cap hits by kind: lanes X (go), vehicles Y (wait)`
and, at the very end (after the G3 count, before `points at W`), `; placed straight onto a road lane (not judged) R`
(vehicles that appeared directly in a road lane, once per vehicle).
`other` now means only paths still unclassified (expected 0); `hand-overs not judged` expected 0 (on-duty emergency
not included). Trace lines: `exit trace: waits vehicle ... path spawn point (spawn location object) ... decision wait (gap) boxed yes ...`,
`exit trace: joins road ... decision go (gap clear) ...`; decisions `wait (gap)`, `wait (box: no room past)`,
`wait (dense traffic: vehicle cap)`, `go (gap clear)`, `go (gap clear; lane cap hit, far lanes not read)`,
`go (signalled arm)` (a building road joining at a signal: the signal decides),
`go (on-duty emergency, stock)`, `judged before the hand-over (driveway give-way)`, `not judged` (should not appear).

**What to check in game.**

| # | Do | Expect |
|---|---|---|
| G1-1 | Log navigation timing on; read a few windows | `spawn point` and/or `building parking` / `kerbside parking` above 0; `other` 0 or close; `hand-overs not judged` 0; `placed straight onto a road lane` 0 or close (clearly above 0: send the log, some traffic still skips the give-way); `waits` clearly above 0.4.6's ~5 per window |
| G1-2 | Alderwood boxed exits (the 0.4.6 screenshot), queue past the box in the far lane | a truck turning right out of the building does not enter the box; it waits hidden at its spawn point until there is room past the box and a gap |
| G1-3 | A busy road with kerbside parking | parked cars pull out only into a gap |
| G1-4 | Exit trace (log) on for a minute near Alderwood | each leaving vehicle shows `waits` and/or `joins road`; the boxed-exit truck shows `wait (box: no room past)` or `wait (gap)`; any `not judged` line: report it (names the uncovered path) |
| G1-5 | Watch for deadlocks (no time limit) | an exit onto a road that never clears, or a kerbside car that never gets out: report the trace line |
| G1-6 | An exit beside a long standing queue (a jam that stopped short of it), exit trace on | the exit vehicle goes when nobody is moving towards it (`go (gap clear)`), no endless `wait (dense traffic: vehicle cap)` |

### G2: road-marking hide, #17 review fixes (JR-T-013e)

**What changed.** (F1) The marking writes (`CutRange`, `Hidden`, with `BatchesUpdated`) moved from the paint system
(which ran after the game's `PreCullingSystem`, so the tag was stripped unseen) into a new `MarkingRenderSystem` that
runs before `PreCullingSystem`: hide / unhide re-render within one frame. (F2) A failed save strip is retried once,
then the cut list is emptied in place; if that fails too, a CRITICAL line says the save may contain hidden markings.
(F3) A box removed / setting switched off between the save strip and the restore redraws the markings and drops the
restore. (F4) Mod unload puts the markings back. (F6) Bookkeeping in `Core/MarkingLedger.cs`, tested offline.

**Settings.** **Hide road markings under yellow boxes** now defaults **OFF**; switch it on only for K1 / K2 (section
16.2). If both pass, the default may go back to on in a later build.

**Log lines.** `yellow boxes: road markings under the boxes hidden: N marking lane(s) cut, M marking object(s) hidden[; left alone ...]; x ms`
on every apply; `yellow boxes: road markings under the boxes shown again` on a clear; WARN `... needed a fallback, the save is clean ...`
or CRITICAL `... THIS SAVE MAY CONTAIN HIDDEN ROAD MARKINGS ...`.

**What to check in game (setting ON).** K1 (save gate) and K2 of section 16.2, plus: add a box, its markings vanish at
once with no camera move or road edit; remove it, they come back at once; toggle the setting off / on: same, at once;
repeat once right after a save (strip / restore path). Any CRITICAL line: setting off, report with the save name.

### G3: a right-turner in a box waits for exit room at its turn spot (JR-T-013b)

**What changed.** Alderwood St x Alderbrook St (boxed 4-way, node Entity(373495:1) at (-1896.5, 594.3, 272.6)): a lorry
turning right (across oncoming traffic, left-hand traffic) waited at its turn spot inside the box (UK exception),
then crossed when the oncoming gap came although its exit had filled meanwhile, and stopped with its cab across the
oncoming lane, so main-road straight traffic waited behind it. The box room test ran only at the stop line. New block
**S2-37** (`BoxTurnRoomHold`, numbered S2-35 on its branch): while the turn spot is the turner's next spot and it can
still stop, the room test runs again; with no room on its exit it stays at the turn spot, before the first oncoming
overlap. The test reads only the turner's own path (never an oncoming lane), so no new wait cycle. Applies only with
**Cars obey yellow boxes** on, **turning across oncoming traffic may wait in the box** on and the Whole junction rule.

**Settings.** None new.

**Counters.** Timing line, at the end (after the building-exit part, before `points at W`):
`; right-turn waits for exit room N` (car-steps a turner stood at its turn spot because its exit was full). `right-turn
waits N` (the oncoming wait) is unchanged.

**What to check in game.**

| # | Do | Expect |
|---|---|---|
| G3-1 | Alderwood x Alderbrook at a busy time, exit (Alderbrook up arm) full | a right-turner waiting in the box stays at its turn spot, clear of the oncoming lane, even with a gap; main-road straight traffic keeps flowing past it |
| G3-2 | Same, exit frees | it goes when there is a gap and room on its exit |
| G3-3 | Timing line | `right-turn waits for exit room` above 0 at busy times; the main road does not lock |
| G3-4 | Optional: G4 watch on this node | lines say `rule: yellow box: turner waits at its turn spot, no room on its exit` |

Open points: a long vehicle's rear may still sit over an earlier crossing (as 0.4.2); cars behind a waiting turner in
its lane wait too (UK practice); an oncoming left-turner can still take the exit room first (stock priority).

### G4: watch one junction (debug; JR-T-013b)

**What changed.** The owner's "weird" junction is Bluejay St x Alderwood St, an unboxed T at about (-1758.8, 271.2),
136 m east of the G3 junction. The 0.4.6 log has nothing per junction. Likely explanation (not confirmed): spillback
from the G3 lock reached back through this unboxed T. New diagnostic, no driving effect, nothing saved; loading a city
stops it. While watching, the per-car counter results are on (the log may say `junction gap counters on`).

**Settings.** Debug buttons **Watch the junction at camera centre (log)** (same pick as the paint test's "Add junction
at camera centre"; any junction, boxed or not) and **Stop watching the junction**.

**Log lines.** Start / stop name the node, its position and whether it is boxed
(`watch junction: watching node Entity(..) at (..) (no box)`). Per standing car:
`watch junction Entity(N:V): car Entity(..) stands (0.0 m/s, limit 0.0 m/s) on lane Entity(..); waits for Entity(..) (Continuing); rule: game's own (give-way / car ahead / signal, see blocker)`.
`waits for` = the game's blocker (Continuing = car ahead, Crossing = give-way / crossing car, Signal = red; "no vehicle
named" = e.g. a reservation or a Junction Rules hold). `rule` = yellow box at the line / turn spot oncoming / turn spot
no exit room (G3), entrance band, zebra (people, room, vehicles), whole junction (later conflict, stopped inside,
safety stop), building exit, or the game's own. At most one line per car, rule and blocker every 512 steps and 20 lines
per 256 steps; a summary line counts the rest.

**What to check in game.**

| # | Do | Expect |
|---|---|---|
| G4-1 | Camera on Bluejay x Alderwood, press Watch | log line `watch junction: watching node ... (no box)` |
| G4-2 | When it looks weird again, read the lines | mostly `rule: game's own` with `Continuing` on the lanes into Alderwood = spillback from the west (the explanation above); `whole junction: ...` rules = a Junction Rules hold: send that log |
| G4-3 | Press Stop watching | stop line; no more watch lines |

### G5: chamfered junction box corners; refusal logs (JR-T-013c)

**What changed.** The tool refused Crescent Street x Eastgate Lane (node 551713, about (-518, 546): Crescent 16 m
one-way with kerbside parking, Eastgate Lane an 8 m Alley north and south) with "corner arm 3 - arm 1 still crosses
the kerb or a marking margin: no allowed shift clears it". A straight side across a curved kerb corner always runs
over the pavement, so a corner cleared only when both mouths slid forward to meet on the kerb; here the turning paths
cross inside the alley mouth, so it could not slide far enough. Now (Core/StraightBox.cs) a corner that cannot close
by sliding may be cut by ONE straight chamfer that touches the kerb curve but never crosses it, each end within the
half of its mouth side next to the corner, keeping the marking margin, every conflict point on the box side; the one
cutting the least area wins. The 0.4.6 search runs first and unchanged: every box that existed keeps exactly its shape
(tested on Moonflower, Lindbergh and five synthetic junctions). A chamfered box merges with touching boxes like any
other (#17, tested). A real refusal still happens when no chamfer fits and says why ("... and no straight cut fits (a
cut clear of the kerb would leave 2 conflict point(s) outside)").

**Settings.** None new.

**Log lines.** Every `yellow box tool:` line names the target with position and arms, e.g. `node Entity(551713:1) at
(-517.98, 546.00), arms: 1 Crescent Street (Two-Lane One-Way Road, 16.0 m), 2 Eastgate Lane (Alley, 8.0 m), ...` (arm
numbers as a failure uses them); exit lines end `at (x, z) on <street> (<prefab>, <width> m)`; a no-shape WARN carries
the solver notes; a chamfer appears in the paint log notes as `corner arm N - arm M cut straight D m inside the kerb
curve (chamfer ...)`.

**What to check in game.**

| # | Do | Expect |
|---|---|---|
| G5-1 | Ctrl+Y over Crescent Street x Eastgate Lane | white outline; click adds the box: straight sides, on the asphalt (cut corners at the Eastgate kerb curves), clear of the zebras |
| G5-2 | Hover node 258813 (next to roundabout 258815) | it boxes now, or the WARN line gives its position, arm names and a reason naming what blocks it |
| G5-3 | Look at boxes already in the city | they look the same as in 0.4.6 |

### G6: no parking in yellow boxes (JR-T-013d)

**What changed.** Owner rule (17 Nov): no car parks inside a yellow box (shot: cars parked in an exit box's kerbside
parking on Crescent Street, both sides). Every parking slot of the road under a box (junction and exit boxes, both
sides; a slot whose space overlaps a box outline by more than 10 cm sideways) is closed the way the game closes slots
at a driveway (own `LaneOverlap` entries, never saved): cars looking for a space skip them and the pathfinder no
longer counts them as free. Each box's own outline is used (a merged group blocks the same, a lane's ranges are
unioned). Cars already parked are not moved; they leave when their owner drives off. Runtime only: before every save
(manual, autosave, quit) the blocked lanes get back the game's own free-space value (`ParkingLane.m_FreeSpace`, the
only saved value touched); after the save the blocks are back. Removing a box, switching the setting off or painting
stopping opens the slots again within a frame. Review fixes: an edit at the next junction (a sign, a lane connection)
made the game rebuild the box road's parking lanes without telling them apart; the block is now put back in the same
frame. The feature runs only when the version check allows the copy (else one log line, parking stays stock). A
failure also reopens lanes it had just closed. Known, not changed: the block is rounded outward to the game's 1/255
lane steps, so it may close one extra slot at a box edge (at most ~16 cm of a 40 m lane); a box whose shape could not
be built (no outline: refused / failed paint) does not block parking.

**Settings.** New **No parking in yellow boxes** (Yellow boxes group, default **on**).

**Log lines.** On every change: `yellow boxes: no parking in boxes ON: N box(es), L parking lane(s) with R blocked
range(s) (S marked space(s) on M slotted lane(s)); O lane(s) opened again; U lane(s) recomputed; x ms` (`OFF` when
switched off). Each save: `yellow boxes: no parking in boxes: before the save, L parking lane(s) carry the game's own
free space (C changed)` and `... after the save, L parking lane(s) recomputed with the boxes again`. After an edit next to a box: `yellow boxes: no parking in boxes: a road update next to a box removed the block from a
parking lane; putting it back`, then the `ON` line. Version check refused the copy: `yellow boxes: no parking in boxes
not registered (version check did not allow the copy); ...`. Failure:
`yellow boxes: no parking in boxes failed while ...; switched off until the game restarts, parking in the boxes opened
again`: send that log.

**What to check in game.**

| # | Do | Expect |
|---|---|---|
| G6-1 | Crescent Street exit box (the owner's shot) | cars already in the box drive off over the next in-game hours; no new car parks in it, either side; slots just outside still used |
| G6-2 | A junction box on a road with kerbside parking | no car parks in the box part of the kerb; slots beyond the box edge still fill |
| G6-3 | Watch a car heading for a space near a box | it parks outside the box or drives on to another space; it does not stop or wait in the box |
| G6-4 | Remove a box with the tool | those slots are used again later; log `L lane(s) opened again` |
| G6-5 | Setting off, then on | off: cars may park in boxes; on: slots closed again, parked cars stay until they leave |
| G6-6 | Save (manual and an autosave), then load the save **without the mod** (on a copy, as section 7) | both save log lines appear; kerbside parking under the old boxes intact, cars park there as in 0.4.6, no error, no lane shown full |
| G6-7 | Upgrade / replace a boxed road (or let Traffic / RoadBuilder rewrite its lanes) | the box is rebuilt (paint log) and the parking block comes back (no-parking log line); no parked car moved by the mod |
| G6-8 | Next to a boxed junction with kerbside parking on the joining road: edit the NEXT junction along that road (a sign or a lane connection) | log `... a road update next to a box removed the block from a parking lane; putting it back` and the `ON` line; the box slots stay closed (no new car parks there) |

## 18. 0.4.8: exits decide before the sidewalk (G7), entrance boxes (G9), hover arrow (G10), Crescent x Eastgate (G5), unmarked crossings (G11) (JR-T-014)

Driving changes act only while Car driving is the Junction Rules copy. **Diagnostics are on by default**: the first
load of 0.4.8 switches **Log navigation timing** and **Exit trace (log)** on once (log line `settings: 0.4.8 probe
diagnostics switched on once ...`); you may switch them off again. The exit trace is now **compact**: up to 20 NOTABLE
events, 6 sampled normal events and one summary line a minute (log sizes below are for 1x speed). Expected log size: ~100-140 KB per 10 minutes of play
(timing lines ~2 KB each every 1024 frames ≈ 90 KB, compact exit trace ≤ ~25 KB, entrance box log ≤ 35 KB cap,
unmarked-crossing log ~15 KB typical, see 18.5), more while you use the box tool (hover / refusal lines as before).

### 18.1 G7: vehicles leaving buildings decide BEFORE THE SIDEWALK

**What went wrong in 0.4.7 (Alderwood Street, south lots, left-hand traffic: near lane westbound, far lane
eastbound).** Lorries from the south driveways (the warehouse at the middle box, Kapine 48 Alderwood St at the right
box) turned right across the westbound lane into a STOPPED eastbound queue and stood across the road, in front of a
westbound lorry. Live read 07:2x: a delivery truck stood at the middle box at (-1982.3, 274.4) heading 17°, nose at
the stopped eastbound queue, body across the westbound lane, two more trucks queued behind it in the drive; the timing
line showed `entrance band holds: leaving driveway 128` (car-steps) unchanged for ~15 minutes. Causes:
1. The driveway rule (S2-29) waited before the first ROAD lane and never asked whether there was room on the lane it
   joins. The only room test was the 0.4.3 box band hold on the joined (far) lane, i.e. with the lorry already
   across the near lane.
2. A road vehicle standing before the exit never counted as traffic (a jam far back must not block an exit for ever),
   so the westbound lorry, held just before the box band, was ignored and the exit pulled out in front of it.
3. ROOT CAUSE seen live (07:36, van 380864 leaving 40 Alderwood St by its sub-net driveway onto edge 557049, tracked
   by main): the exit was judged only while its own hold was the tightest limit. The game starts every update with the
   limit = current speed + one step of acceleration, so a vehicle pulling away from a stop a metre or two before its
   spot is NEVER judged (its hold, the braking speed to the spot, is always higher) until it can no longer stop: the van
   went 0 -> 1.9 -> 4.6 m/s across the westbound lane in front of a lorry at 11.2 m/s. 0.4.8 judges every update within
   12 m of the spot. (The driveway is the sub-net node joined to the road's middle: the game's SideConnection drive lane,
   the path S2-29 handles; no other hidden path.)
4. The time a lorry needs was the time to REACH the near lane, not to CLEAR it with its whole length.
5. The 0.4.7 exit trace could not show any of this: a driveway decision is never written (drive lines say `building
   none ... judged before the hand-over ... boxed no`; the boxed flag was only set by the other exit paths, box
   detection itself works by road + position, see the non-zero `leaving driveway` holds), and a vehicle stuck on its
   drive lane never reaches a hand-over (truck 380864, in the middle box since the 07:30 load, has no trace line).
6. "hand-overs not judged" in 0.4.7 equalled "building road" in almost every window: a counting artefact (the
   hand-over update has the front past the stop point, so no decision in that very update), not unjudged exits.

**Now (owner rule 17 Nov: the sidewalk is a zebra Z, directly followed by the road lanes).** A vehicle leaving by a
driveway stops **before the pavement** (inside the lot / drive) and goes only when ALL are clear: people on the
pavement (the zebra rule: cars never push through people, people pass after 2 s), every lane it crosses (for the time
its whole length needs to clear it; any road vehicle within 10 m of the exit counts even when standing), the lane it
joins, and room for its whole length on that lane (a moving queue counts its braking distance as room; at a boxed exit:
room past the box). Then it crosses the pavement and the road in one go. Lot exits stop before the sidewalk line;
spawn points / gates / parking / building roads also need room on the lane they join and use the clear time. No
starvation safeguard (owner): an exit onto a road that never clears waits.

**Counters (timing line, end):** `exits held before the sidewalk N (no room on the lane it joins M)`; `driveway exits
crossing the sidewalk E, of them without a full judgement U (must be 0)`; `standing on the road after leaving a
driveway (5 s+) S` (the G7 failure itself; expected 0 or close); `judged only on the sidewalk (could not stop before
it) L` (expected small); `hand-overs not judged` should now be 0 or close.

**Trace lines (on by default):** `exit trace: NOTABLE crosses the sidewalk ... NOT judged before the sidewalk`,
`exit trace: NOTABLE stands on the road after leaving (driveway lane, 5 s+) vehicle V building B ...`,
`exit trace: NOTABLE held 25 s+ at the exit ... decision wait (no room on the lane it joins)` (or `wait (gap)`,
`wait (box: no room past)`), `exit trace: NOTABLE joins road ...` (a hand-over with no judgement), a few sampled
`waits` / `crosses the sidewalk ... go (full judgement before the sidewalk)` lines, and once a minute
`exit trace (compact) last 60 s: N exit events, notable K (L logged, cap 20 a minute), normal sample 6 of M`.

| # | Do | Expect |
|---|---|---|
| G7-1 | Alderwood St, south lots (middle and right boxes), eastbound queue standing | lorries wait in the lot BEFORE the pavement; none noses into the road; westbound traffic flows past |
| G7-2 | Same, eastbound queue moving with room | a lorry turns right only when the westbound lane is clear for its whole length and there is room east |
| G7-3 | A westbound vehicle standing next to the exit (held before the box) | the exit waits for it |
| G7-4 | Timing line | `without a full judgement 0`; `standing on the road after leaving a driveway` 0 or close; `exits held before the sidewalk` > 0 at busy times |
| G7-5 | Any NOTABLE `stands on the road` or `NOT judged` line | send the log + a screenshot of that spot |
| G7-6 | Pedestrians on the pavement at a busy driveway | the vehicle stops before the pavement, people pass first |
| G7-7 | An exit straight ACROSS the road into an opposite access (e.g. the Ordinateur van, No. 48 -> No. 58 if 58's access is opposite) | both lanes judged as crossed (clear time); a far-lane queue car standing on or within 10 m of the crossing point holds it in the lot; no room in the opposite driveway = waits in the lot |

Confirmed by the owner's route shot (07:33): the van in the middle box (Ordinateur, Returning to 58 Alderwood St, empty)
left No. 48 on the south side turning RIGHT across the westbound lane to join the STATIONARY eastbound queue, then
planned a right turn into No. 58's driveway further east (a G9 entrance case): exactly G7-1. Watch / unproven: a far-lane car held before a box band also holds a crossing exit (road traffic first: a
kept-clear gap does not let exits cut through it while the car beside it waits; owner may want that relaxed); lot exits stop before the sidewalk line but people on that sidewalk are not checked for lot exits
(area lanes never meet the game's pedestrian check; no lot exits in Bevy Valley's 0.4.7 logs: `lot 0`); a driveway
lane that starts on the pavement is judged at the first road lane (`judged only on the sidewalk`); two-way alley lanes
as the joined lane are scanned along their curve (inherited); exits wait longer on busy roads (owner: no safeguard).

### 18.2 G5: Crescent Street x Eastgate Lane boxable

Where footway widths differ at a corner (Crescent 5 m, Alley 1 m), the box now follows the corner kerb the way the
game draws it (the 0.4.7 "one kerb" took the 5 m footway on the Alley side, i.e. the Alley's centre line), and a mouth
whose zebra (+ 0.5 m margin) reaches past its kerb lines is held clear of it. Both apply only when the 0.4.7 shape is
refused, so every existing box keeps its shape. A mouth that would have to be held past the junction centre is
refused. Log on add: `box added on junction ...; box shape: 6-sided straight box, 65.0 m2, rule priority-road, mouth
shifts [...] m, 4 corner kerb(s) as the game draws them, WARNING 3 conflict point(s) left outside (under a marking
margin past the kerb lines)` (logged as WARN: three conflict points lie under the Crescent W zebra margin; no box can
hold them without covering the zebra). Check: the box takes, straight sides, off the pavement at all four Alley
corners, ~0.5 m short of the Crescent W zebra. Node 258813 (old gap list) not re-checked.

### 18.3 G9: boxes at building ENTRANCES; cars turning in

A boxed building now also gets a box at every car entrance (derived on load, never saved: the save is unchanged; an
entrance next to a boxed exit merges into one box). A car turning INTO the building waits on the road before the box
unless its whole length (trailers incl.) fits on the way in (moving cars ahead count their braking distance); a UK
right-turner with room may wait at its spot in the box; through traffic unchanged. Log: `entrance box (building X,
entrance Entrance@(..), derived): ...`; for old saves `lot-front fallback not drawn ...; key kept as the building's box
marker`; timing line `entrance box entries held (entrance full) N`; always-on compact entrance log (`entrance box log
(always on) ...`, per minute `entrance boxes (last 60 s): ...`, up to 5 samples and 10 notable `entrance box: vehicle
... held before the box 30 s / entered the box without room and stands in it / stands in the box 30 s` lines; at most
160 lines per 10 minutes). Check: Alderwood Street entrance boxes; a lorry turning into a full yard waits on the road.
Note: rolling back to 0.4.7 after saving with 0.4.8 draws marker keys as lot-front boxes again (0.4.7 behaviour).

### 18.4 G10: hover arrow; no boxes at lot-front fallbacks

Hovering a building box with Ctrl+Y draws an arrow (white add / orange remove) from the box on the kerb to the
building's access: head at the box = exit, at the building = entrance, both = two-way. Boxes go only at REAL car
accesses; the lot-front fallback is never a target. The confusing 17 Nov hover (box with no visible driveway on road
312580) was a real two-way access ("Invisible Road Path - 2xTwoway") of the police station or clinic, not a fallback.

Known limits (G9 review): clicking an orange entrance box removes ALL of that building's boxes (exit boxes too; the log
says so); a second car turning into the same entrance behind one waiting at its in-box spot may stop in the band
behind it (watch the entrance log); buildings whose access is a spawn location get a box but cars heading in are not
held (their path ends on the road lane); a saved lot-front box that no longer draws gives one WARN per building and
session (`its saved lot-front box is no longer drawn ...`).

### 18.5 G11: people give way at unmarked crossings

New setting **People give way at unmarked crossings** (default on; off = 0.4.7; acts only while Car driving is the Junction Rules copy, both sides). Where a crossing has no zebra and no
signal (the game's crosswalk lane with `Unsafe`: no right of way for people), a person waits at the kerb until the
approaching cars leave a safe gap (braking distance + speed x the person's crossing time + 1 s; standing cars never
block; cars that will stop at a red light do not count), and cars no longer stop for people waiting at or walking up
to such a crossing. Safety floor: a car still stops for a person already on the carriageway (counted). Zebras (people
have priority, cars never push through, people pass after 2 s), signal crossings and the driveway sidewalk (18.1, a
zebra) are unchanged. No second Burst copy: the person side is its own system after the game's human navigation.

Log (always on): timing line `; unmarked crossings: car-steps not held for people N, people held at the kerb N,
stopped for a person already crossing unmarked N` (the last should be rare); once a minute a `G11 unmarked crossings`
summary, up to 5 sample lines and up to 20 notable lines (`braked for person ... already on the carriageway`, `held at
the kerb over 60 s`); about 60-70 lines per 10 minutes, at most 260. Settings line `settings (G11): people give way at
unmarked crossings on`.

| # | Do | Expect |
|---|---|---|
| G11-1 | A junction with unmarked crossings and steady traffic | people wait at the kerb for a gap; cars do not stop for them |
| G11-2 | A zebra nearby | unchanged: cars stop for people on / at the zebra |
| G11-3 | Very busy road with an unmarked crossing | people may wait long (no give-up, owner rule): `held at the kerb over 60 s` lines |
| G11-4 | Any `braked for person ... already on the carriageway` line | rare; many = the kerb judgement lets people out too early: send the log |

Unproven: the 0.5 m kerb tolerance; a feeding car with an unknown route counts (people wait longer); a car that
cannot stop for a person already crossing still collides as in stock.

## 19. 0.4.9: the 0.4.8 live gaps G12-G15 (JR-T-015)

Driving changes act only while Car driving is the Junction Rules copy. Diagnostics stay **on by default** (timing log,
compact exit trace, entrance box log). Evidence: 0.4.8 log 08:46-08:54 (jrlogs `JunctionRules.Mod.2026-11-17_048.log`),
owner shots ded7b442 (G13), a4d15d38 (G14), a7b0fa45 (G15), read-only cs2 reads 08:5x (frames 36670652, 36685203,
36692240).

### 19.1 G15: the Alderwood deadlock (No. 40, warehouse 839552)

**The loop (live read).** No. 40 has an ENTRANCE drive at x -1994 and an EXIT drive at x -1983.4, 10.5 m apart on the
westbound (near) lane; their 8 m boxes are 2.5 m apart. A van for No. 40 came westbound and had to wait because No. 40's
yard was full (G9: do not turn in without room for the whole vehicle). 0.4.8 held it before the ENTRANCE box, so its
6-7 m body stood in the EXIT box behind it (live: van centre x -1986.9, z 271.6). No. 40's exit head (380832, later
380901, at the drive start) then waited for the van twice over: the van stood on its westbound conflict, and even where
it does not, a vehicle standing within 10 m of an exit counts as "present" (0.4.8 G7 rule). The yard could not empty,
the van could not get in: van -> yard -> exit -> van. The lot-internal 'Crossing' wait (905413 on the Invisible Car
Path, then 174655 / 174632) is the same queue inside the yard, not a separate cause; the eastbound vans for No. 40
queued at z ~278 the same way. The game's deadlock detector cannot see Junction Rules holds (0 loops).

**Rules now (UK yellow box: never stop with any part of the vehicle in a box; vehicles leaving buildings give way).**
1. A vehicle held at a box band never stands in another band: for its own hold, bands on its lane closer than its whole
   length (trailers incl.) + the room margin are ONE band (at least 2 m). The van now waits before No. 40's EXIT box,
   clear of the exit mouth. Same for through traffic (room needed past the last of the close boxes) and for vehicles
   leaving a boxed drive.
2. An exit does not wait for a road vehicle that stands before it while it waits for room INTO a building (held by the
   G9 rule this or the last navigation update; also a standing one queued behind such a vehicle when its own way in is
   full). It waits for nothing the exit can give it except room, and that room
   comes from vehicles leaving. A vehicle ON the exit's conflict, or one moving towards it, still blocks (safety floor);
   a through vehicle standing next to the exit still blocks (G7 rule unchanged).
No timeout: both are rules, the loop cannot form.

Regression test: `G15LoopTests` rebuilds the live geometry (bands at x -1983.4 and -1994.04, 6.5 m van) and shows the
0.4.8 rules close the van -> yard -> exit loop and the 0.4.9 rules leave it open.

### 19.1b G15 again: the loop No Vehicle Despawn flagged (owner shot 5d341726, camera rotated 180)

The yard of No. 40 is one-way: in by the entrance drive (x -1994), out by the exit drive (x -1983.4). The 7 outlined
vehicles: the exit column down the exit drive (red van at the pavement edge, held by Junction Rules before the
pavement), a dark van standing on the near lane INSIDE the exit box, a van angled across the entrance box turning in
(rear on the road), a vehicle in the entrance drive, two at the top of the yard turning back to the exit column.
Who waits for whom, with the evidence:
- Exiter (red) -> dark van: the dark van stands on the exit's merge area. This is a PHYSICAL block (the game's own
  check names the dark van as blocker; that is why the detector could follow the chain through it). The exit room test
  counting it is right: (b) is not a fault.
- Dark van -> angled van: 0.4.8 faults. (1) An entrant held by G9 waited before the ENTRANCE box with its body in the
  EXIT box 2.5 m upstream (fixed in 19.1: it now waits before the exit box). (2) A vehicle entering a box needed room past
  it, but the room scan reads only the road lane's own vehicles, and the game lists a vehicle only on its CURRENT lane:
  the angled van's pivot was already on the entrance drive lane, so its rear across the near lane was invisible and the
  box looked clear. Fixed: for traffic driving THROUGH a box, the room scan also reads every driveway lane
  overlapping the road lane in the box / room window; a vehicle on it whose body still covers the road part stands across
  the lane and leaves no room (named as blocker). Never in an exit's own room checks (review: the vehicle across the lane
  may be the entrant that waits for that exit; counting it there would rebuild the loop), nor for zebras / junction boxes.
- Angled van -> entrance drive / yard: 0.4.8 fault (3): G9 let a vehicle turn in when the queue on its way in was MOVING
  (a moving queue counted its braking distance as room). A yard queue that moves can stop, and the yard only empties
  through its exit. Fixed: on the way IN only room that is free now counts.
- Yard -> exit column -> exiter: the yard's own one-way circulation (game layout, stock behaviour).
With 19.1 and these two fixes the loop cannot be built from the road side: no vehicle turns in without free room for its
whole length, no vehicle waits inside the exit box for an entrant, and the exit does not wait for vehicles waiting to
turn in. Regression tests: `G15LoopTests`, `G15bSideObstacleTests`.

### 19.2 G14: near boxes are one box

Cause: the paint joined exit boxes only when they touched within 0.05 m (Alderbrook Street boxes ~0.6 m apart stayed two),
and an exit box with an entrance box ~0.5 m off it was unioned with nearly parallel kerb sides; the union failed (log
08:49:39 `merge with 1 touching box(es) failed (union boundary does not close); drawn separately`), so both outlines were
drawn. Driving merged bands only within 0.1 m, so the eastbound artic of the shot entered the first box with room only in
the 0.6 m gap before the second (cab in one box, a truck across the other).

Now: boxes on the same road and carriageways whose windows overlap or are less than **2 m** apart are BUILT as one box
over the joint window (from the first box's access; never over a road marking or into a junction-mouth margin: then they
stay apart, logged), and driven as one band (2 m, and per vehicle its own length, 19.1). Log: `yellow boxes: N boxes
within 2.0 m on edge E built as ONE box, window a..b m (...)` or `... kept apart: <why>`. Hover and the tool still see
every member (clicking still adds / removes that building's boxes as before).

### 19.3 G13: through traffic waited for an exit that gives way

Cause (game protocol): a vehicle whose next lane is a driveway's leaving lane asks road traffic for space every update
(the game's RequestSpace reservation, priority 96, CarNavigationSystem.ReserveNavigationLanes), and a road car that
would have no space past the drive stops before it (CarLaneSpeedIterator.CheckOverlapSpace). Junction Rules holds that
exiting vehicle (it gives way), so the road car waited for a vehicle that was not allowed to go (sedan 909842 before
the Alderbrook box while 910673 / 910289 waited at the exit). Now a vehicle held at a driveway exit withdraws that
request for the update it is held (new copy blocks S2-51 / S2-51b; its own state is restored). Boxed exits are still
kept clear by the box rule (a road car enters a box only with room past it). Not proven live: the shot alone cannot show
which of the game's checks held the sedan; the new counter shows whether withdrawals happen.

### 19.4 G12: the proof counters and "one move"

Causes: (1) 0.4.8 counted "driveway exits crossing the sidewalk ... without a full judgement" at the PIVOT hand-over onto
the drive lane; a lorry held before the sidewalk is handed over while it still waits there (live: 844033 `waits` and
`NOT judged` in the same frame 36642880; 841993, 180943, 183365 likewise), so most NOTABLE 'NOT judged' lines were
vehicles that had not crossed. (2) Every exit that had passed the sidewalk spot after its Go was judged AGAIN at the road
lane and counted "judged only on the sidewalk" (24 of 28 in the 08:54 window); worse, that second judgement could stop a
Go'd lorry ON the pavement, front at the kerb (live: 380901 at No. 40). (3) "standing on the road after leaving" counted
every vehicle standing on a drive lane that was not judged in that update, also vehicles queued behind a held one more
than 12 m before the spot; it often equalled the crossing count by accident, not by double counting the same event.

Now: the crossing is counted once, where the FRONT passes the sidewalk spot: judged (a Go stood), judged only on the
sidewalk (no Go, decided at the road lane: it could not stop before the pavement) or without a full judgement (neither:
must be 0). A Go'd vehicle past the spot that is still moving is committed: pavement and road in one move (the game's
physical checks still apply); one that stopped on the pavement is judged again at the road lane. "Standing on the road"
counts only vehicles whose front is past the sidewalk spot.

### 19.6 G16: boxes turned WHITE in snow

Cause: the box paint is drawn with a copy of the game's surface-area material (AreaDecalShader, the shader of grass /
sand / pavement areas). That shader lets surfaces snow over: its pixel shader (docs/shader-areadecal/blob054.asm) ends
with `albedo = lerp(paint colour, _Snow_BaseColorMap(world uv), snow factor)`, the factor from the global snow map
(`_SnowMap` x `colossal_SnowScale`, Game.Simulation/SnowSystem). The areas configuration material we copy has no snow
texture, so Unity's default WHITE texture was used: in snow every box turned white (owner shot a868219c, Alderwood,
winter night). Now the box material's snow colour map is a 1x1 texture in the paint colour: in snow the box keeps the
snow response of the surface (normals / smoothness, so it looks snow-dusted like the stock surfaces) but its colour stays
yellow. Rain / wetness is unchanged (the stock wet darkening). No new setting.

### 19.7 G17: the No Vehicle Despawn mod did not see the Alderwood deadlock

What the mod does (NoTrafficDespawn 85211_15, read-only decompile; owner setting `despawnBehavior: DespawnDeadlocksOnly`,
defaults linger 400 frames, search depth 100, 1 removal per pass, stuck below Blocker speed 3): every 4 frames its
`TagStuckObjectsJob` follows each vehicle's game `Blocker` (`m_Blocker`, through trailers to the controller); a vehicle
whose chain comes back to itself (or to its target) is a deadlock and gets `StuckObject` (highlighted). A chain stops as
"not stuck" at a link with `m_Blocker = Entity.Null`, type Temporary, or `m_MaxSpeed >= 3`. After a stuck vehicle has stayed
stuck for 400 frames the mod sets `PathFlags.Stuck` on it (the game then removes it, i.e. despawns ONE vehicle of the loop
per pass), until the loop is gone. It also asks a vehicle that blocks someone at a crossing to request space.

Why it saw nothing: every Junction Rules hold wrote `Blocker.m_Blocker = Entity.Null` (the exit hold, the box / entrance
hold, the zebra and right-turn room holds; the lot-exit branch even left `m_MaxSpeed` 255 = "moving"), so each chain stopped
at the first Junction Rules hold: van (entrance hold) and No. 40's exit head (exit hold) both looked "not blocked by
anyone". The game's own deadlock remover (StuckMovingObjectSystem) and the cs2 bridge's `cs2_vehicles` deadlocks
(CS2MCP.Bridge RequestHandlers.Vehicles.cs 149-161: the same `Blocker.m_Blocker` chain among driving vehicles) were blind
for the same reason; the bridge needs no change.

Now every hold names the vehicle it waits for, as stock names the car it stops behind: the vehicle that took the room the
hold needs (box, entrance, zebra, right-turn spot, exit room), or the road vehicle an exit gives way to; `Entity.Null`
only when no vehicle is to blame (people on a crossing). The lot-exit / spawn-point holds also write the held speed. So a
loop through Junction Rules holds is now visible: No Vehicle Despawn highlights it and, if it persists 400 frames,
despawns one vehicle per pass (its own rule, owner's choice); `cs2_vehicles` lists it under deadlocks. With 0.4.9's G15
rules the Alderwood loop should not form at all; this is the safety net the owner already runs.

When it saw the loop (owner shot 5d341726) and why not earlier (shot a7b0fa45): it flags a vehicle the moment (checked
every 4 game frames) its blocker chain closes; the blue outline is its "highlight stuck objects" option; with
"despawn deadlocks only" it removes one vehicle of the loop after it stayed flagged 400 frames. In 5d341726 every link
was a PHYSICAL stop the game names itself (the red exiter's merge area was occupied by the dark van, so the game's own
check, stricter than ours, named the dark van). In a7b0fa45 the No. 40 exiter stood before the pavement held only by the
Junction Rules exit rule with nothing physically in its way, so its Blocker was Entity.Null and the chain ended there:
JR holds hid the loop until it had grown into physical blocks. 0.4.9 names the blocker for every JR hold, so such a loop
is flagged as soon as it closes.

Intermittent (owner shot 5bf5b86c, the same lock NOT outlined): the red exiter at the pavement edge is held twice in
every update, by the Junction Rules exit hold (stop at its sidewalk spot; 0.4.8 Blocker = Entity.Null) and by the game's
own check (stop before the dark van on its merge area; Blocker = the dark van). The game writes its blocker only when its
limit is strictly lower than the one already set, and with the van standing right at its spot the two limits are almost
equal, so from update to update the Blocker flips between the dark van and nobody. No Vehicle Despawn re-tags every 4
frames and REMOVES its stuck mark as soon as a chain breaks once, which also restarts its 400-frame linger: the loop
flickers between outlined and not, and is never despawned. 0.4.9: the Junction Rules hold names the same vehicle (the
one on the exit's conflict), so the chain stays closed and the mod acts after 400 frames (one vehicle per pass, its own
setting). The loop itself should not form with 19.1 / 19.1b.

Check: `cs2_vehicles x,z` near a jam caused by Junction Rules shows queue heads behind named vehicles (not "Crossing"
with nobody); a JR-caused loop (if any) appears under `deadlocks` and is highlighted by No Vehicle Despawn.

### 19.8 What to watch

| # | Do | Expect |
|---|---|---|
| 19-1 | Alderwood St, No. 40 (warehouse 839552) and neighbours, busy (19.1, 19.1b) | vans for a full yard wait on the road BEFORE the exit box (exit mouth clear); No. 40's lorries leave; no van -> yard -> exit lock; timing `exits not waiting for a vehicle that waits to turn in` > 0 at busy times |
| 19-2 | Alderbrook St boxes of shot a4d15d38 | one box (one outline, one hatch) where boxes were < 2 m apart; log `built as ONE box` |
| 19-3 | Same place, eastbound artics | no artic stops with its cab in one box and its trailer in the next |
| 19-4 | Through traffic past an exit with vehicles waiting at it, lane clear past the box | through traffic keeps going; `held exits' keep-clear requests withdrawn` > 0 |
| 19-5 | Timing line | `without a full judgement` 0 or close; `judged only on the sidewalk` small (no longer about equal to the crossings); `standing on the road after leaving` small |
| 19-6 | NOTABLE `crosses the sidewalk ... NOT judged` or `stands on the road` lines | send the log + a shot of that spot |
| 19-7 | Winter / snow (G16) | boxes stay YELLOW (may look snow-dusted) |
| 19-8 | A jam at a JR hold (G17) | `cs2_vehicles` names the vehicle the held one waits for; a real loop is listed under deadlocks and highlighted by No Vehicle Despawn (which despawns one vehicle of it after ~400 frames) |

Timing line, end: `...; judged only on the sidewalk (could not stop before it) L; exits not waiting for a vehicle that
waits to turn in N; held exits' keep-clear requests withdrawn K`. Trace: a crossing judged only at the road lane is now a
sampled line `crosses the sidewalk ... decision judged only at the road lane (could not stop before the sidewalk)`.

**Unproven / watch:** (a) G13's mechanism is inferred from the game code, not seen; if through traffic still waits at
exits, send a shot + the vehicle ids (the cs2 read of the blocker helps). (b) Withdrawing the keep-clear request lets
queues stand across UNBOXED drives more often (exits then wait longer; owner rule: no safeguard); boxed drives unchanged.
(c) Per-vehicle band merging makes long artics wait before a row of close exit boxes until there is room past the last
one (UK box rule, may look cautious on streets with many boxes). (d) An entrant held for room is excused only while the
entering hold binds (its record is at most 3 navigation updates old); a second vehicle queued behind it is still traffic
for exits within 10 m. (e) Inside a yard, entering and leaving vehicles can still block each other at the lot's own
internal crossing when the yard is full (game lot layout; not a Junction Rules hold); 0.4.9 only removes the road-side
half of that loop. (f) The joint box uses the first box's access for its shape; on a curve the merged box follows the
same edge curve. (g) Driving merges bands less than 2 m apart even where the paint keeps two boxes apart (a road marking
in the gap): cautious, never bolder. (h) The no-parking mask still follows each box's own outline, so the up-to-2 m gap
inside a joint box is not no-parking (no car fits there). (i) Review (Sonnet) fixes in: a second van queued behind the
first for a full yard is recorded as waiting to turn in too (F1); the entrant record is at most 2 updates old and a
vehicle above 0.2 m/s counts as moving (F2); a committed crossing keeps its Go fresh (F3); a failed union draws a joint
box once (F6). (j) G16: the snow colour map was proven from the shader disassembly, not seen in game. (k) G17: naming
blockers also feeds the game's traffic-bottleneck notification (it groups 'Continuing' blockers) and its lane choice
(a named blocker makes a car look for a better lane, as stock); No Vehicle Despawn will now despawn vehicles of
JR-caused loops after 400 frames (the owner's own mod setting). (l) G15b: vehicles turn in only with room free NOW (more waiting on the road at busy yards; a queue waiting to turn in
blocks the lane behind it, as in the UK); a vehicle half across a lane from a driveway now holds box / zebra entries on
that lane until it has cleared. (m) Review (Sonnet) of G16/G17/G15b: only the hold that binds names its blocker (no stale name from a log-only or
non-binding room read); the named blocker also feeds the game's own jam handling as for any stock stop: a car standing
behind a named blocker may look for another lane, and once the Junction Rules hold releases while the same car is still
named, the game's IgnoreBlocker creep (3 m/s) can apply as it does for stock blocks; the lot-exit branch (S2-32) writes
the JR blocker over the game's own Entity.Null (the stock area branch names nobody). (n) G13: the shot alone cannot show
which game check held the sedan; the JR-side causes found in the code are all changed (held exits no longer ask for
keep-clear space, near boxes are one band, Go'd exits are never re-held on the pavement); the counters show the rest.
(o) None of this has run in game yet; everything above is proven from code, the decompile, the shader disassembly, the
logs and the tests (2753).

## 20. 0.4.10: the 0.4.9 live gaps G18 and G19 (JR-T-016)

Driving changes act only while Car driving is the Junction Rules copy. Evidence: 0.4.9 log 09:50:00 (box lines 11-43),
main's live tracks (hatchback 909920 `hatch.txt`, sedan 909747 / truck 380975 / car 910952 `sedan.txt`, frames
36625355-36629298), owner shots 4570fced / ec08fecc (G18) and 413c538d (G19), read-only `cs2_junction_lanes` at the
Alderbrook Street south junction (lane 984373/6 starts at (-1898.21, 281.27)), decompile 2026-11-13.

### 20.1 G18 and G19: the driven box was not where the box is painted (one cause)

0.4.3-0.4.9 told the driving rule only WHERE an exit is on the road (the edge curve position t of the exit) and the job
laid the 8 m box length around t mapped straight onto each lane: `(t - EdgeLane.m_EdgeDelta.x) / (delta.y - delta.x)`
(JRYellowBox BoxHold and JRExitGiveWay ExitBandBlocks, 0.4.9). But the game does not build a road's lanes over its
whole curve: it builds them over the road's CUT geometry, from one junction mouth to the other (LaneSystem
CreateEdgeLanes 2652 / 2661-2662: the combined segment, or EdgeGeometry m_Start with delta (0, 0.5) and m_End with
(0.5, 1); CreateEdgeLane 4101-4105: lane curve = Lerp(segment left, segment right)). The curve t runs from node centre to
node centre. So the driven box slid towards the middle of the road, by more the further the box is from the middle.
Alderbrook Street (edge 984373: curve 192.1 m, lanes on its part 7.2..175.3 m; the south junction mouth is 17 m from
the node centre):

| Box | Painted (z) | Driven by 0.4.9 (z, northbound lane) |
|---|---|---|
| 843726 exit (164.6 m along) | 288.0 .. 296.0 | 301.3 .. 309.3 (13 m north, no overlap at all) |
| 193433 exit + 843726 entrance, ONE box (110.0 .. 127.3 m) | 329.3 .. 346.6 | 337.5 .. 353.6 (entry 8.2 m late) |

- **G18** (hatchback 909920, northbound): the merge of the two boxes did work in driving (both bands were there and
  joined for the car), but the joint band began at z 337.5, not at the painted 329.3. The car past the box (911176,
  z 349.5) left no room, so the hatchback was held at the DRIVEN entry and stopped with its front at ~335.7 (centre
  333.6): 6.4 m inside the paint. That it stopped just before the exit box's own painted start (338.6) was a
  coincidence of the shift.
- **G19** (sedan 909747 northbound, z 294.4 for ~10 s; truck 380975 at z 295.8 and car 910952 at z 288.2 southbound):
  843726's box was driven 13 m north of the paint on both lanes. The car ahead, 910969, crept to z 298.9 and stopped
  there with its front at ~301.1: exactly at the DRIVEN box line (301.3), held there for room past the misplaced box. The
  sedan queued behind it INSIDE the paint, with no driven box under it. The southbound truck and car queued back from
  the main-road junction into the paint, south of the driven box. **The southbound vehicles had the same cause** (the
  misplaced box), not a moving-queue entry. Whether 0.4.9's moving credit would also have let the sedan in with the box
  in the right place cannot be told from the track (the vehicle that held 910969 was not tracked); 0.4.10 needs room
  free now in any case (20.2).

**Now:** the main thread builds every exit / entrance box's window with the paint's own code (Boxes/BoxShapes.Entrance
with the published shape parameters; boxes less than 2 m apart built as ONE box over the joint window exactly as the
paint's JointBoxes; derived entrances exactly as the paint derives them; a box the paint does not draw is not driven
either) and hands the window's two end points on the road's centre line to the job (Boxes/DrivingBands.cs). The job
projects them onto each lane's own curve (Core/LaneWindow.cs, JRYellowBox.LaneBands, shared by through traffic and by
vehicles leaving a building). On Alderbrook the driven boxes are now z 288.0..296.0 and 329.3..346.6, within 0.1 m of
the paint (test `G18G19Tests`). Junction boxes were not affected (they are the junction's own lanes; their entry is
the stop line at the mouth). The driving list is rebuilt when the boxes change, when the paint regroups (a road or
node under a box changed) and every 64 steps; the shape work is cached and runs only when an input changed. Log:
`yellow boxes (driving): N painted exit / entrance box(es) (M boxed exit(s) and entrance(s), J built as one box) on B
building(s) handed to the box rule, each over its painted window[; K not painted, so not obeyed]`.

### 20.2 G19 rule: room must be free NOW (owner, UK: do not enter unless your exit is clear)

0.4.2-0.4.9 counted a MOVING vehicle past the box as giving room up to its braking distance (#14's zebra measure). Now
every yellow box room test counts only room that is free now: the junction box at its line, exit and entrance boxes
for through traffic and for a vehicle leaving a building into a box, and the right-turner's re-check at its in-box spot
(so it waits there only while its exit has free room and only oncoming traffic stops it). Turning INTO a building
already needed free room (0.4.9, G15b). Zebras keep the moving credit (not a box). A vehicle still inside the box ahead
takes exit room first (unchanged); nothing past the box = room (unchanged).

**Deadlock check (G15):** a deadlock is a standing state, and a standing vehicle never had a credit, so free-now gives
the same verdict and names the same blocker as 0.4.9 in every standing scene (test: 5000 random standing scenes). It
adds waiting only while the queue past the box still moves, and only on the vehicles ahead (downstream): no new wait on
anything behind or beside the car, so no new loop. The G15 rules (no part in a box, exits do not wait for entrants,
side obstacles only for through traffic) are untouched; `G15LoopTests` / `G15bSideObstacleTests` still pass.

**G13 (through traffic not held without reason):** through traffic is held only when the space past the box is not
physically free for its length + margin. In flowing traffic the car ahead is usually far enough past the box (test:
rear 12 m past = no hold); the hold can bite in dense, slow platoons (headway shorter than box + own length + margin).
New counter at the end of the timing line: `box holds only because the queue past the box was moving (free-now rule) N`
(car-steps where the binding box hold would have been let through by 0.4.9's credit). If N is large on a busy road
with no jam, tell main.

### 20.3 What to watch

| # | Do | Expect |
|---|---|---|
| 20-1 | Alderbrook St, northbound and southbound past 843726 / 193433 (shots 4570fced, 413c538d), busy | nobody stops inside the painted yellow; queues stop at the painted line |
| 20-2 | Log after load | `yellow boxes (driving): ... built as one box ... each over its painted window`; no `not painted, so not obeyed` unless the paint log says a box was not painted |
| 20-3 | Any boxed exit on a long road near a junction (largest shift was near the road ends) | the stop line of the box = the painted edge |
| 20-4 | Timing line end | `box holds only because the queue past the box was moving` small next to the box hold counters; big numbers without a jam = report |
| 20-5 | Right-turner in a boxed junction with a slow exit queue | waits at the line until its exit has free room; once in, waits only for oncoming traffic |

**Unproven / watch:** (a) None of this has run in game; the misplacement is proven from the decompile, the live lane end
point, the log windows and the tracks (tests rebuild them), not seen after the fix. (b) Free-now makes box entries more
cautious in slow dense traffic (counter 20-4). (c) A window is projected onto a lane perpendicular to that lane; on a
sharp curve the driven box ends follow the lane's perpendicular, the paint's ends the centre line's (same within cm on
normal curves). (d) A box the paint cannot draw (log `not painted`) is no longer driven (0.4.9 drove a box there); the
paint log names why. (e) The 2 m same-road merge for the vehicle's own length (19.1, per vehicle) is unchanged and now
works on the true positions.

### 20.4 G20: boxes 2-6 m apart are one box (owner: "you said within 2 m it will be merged but this is not")

Owner shot ae3ba73f, Alderwood (edge 557050), No. 40 (Scandic Baron, building 839552): the derived entrance box (window
34.7..42.7 m) and the saved exit box (45.5..53.5 m) are 2.8 m apart; 0.4.9 merged boxes only below 2 m, so they were drawn
as two boxes with a strip of road between. A gap is only useful if a vehicle can wait in it; the shortest road vehicle
is a small car of about 4.2-4.5 m, and the box rule wants it clear of both boxes with the room margin (1 m): about
5.5 m. **Now: boxes on one road (same carriageways) less than 6 m apart are built and driven as ONE box** (one constant,
ExitBands.MergeGapMetres, used by the paint's JointBoxes and by the driving list), still never over a road marking or
into a junction mouth margin (then they stay apart, logged). In driving a vehicle longer than 5 m still treats boxes
closer than its own length + margin as one for its own hold (19.1, unchanged). No. 40 becomes one box 34.7..53.5 m
(test `G20MergeGapTests`), unless the road's one marking lies in the 2.8 m gap (the log then says so).

New log, one line per neighbouring pair of boxes on a road less than 15 m apart, once per decision:
`yellow boxes: pair on edge E: <box A> window a..b m | <box B> window c..d m: MERGED: gap 2.8 m < 6.0 m (no vehicle can
wait between them): one box, window 34.7..53.5 m` or `NOT merged: gap 6.5 m >= 6.0 m (a vehicle can wait between them)`
or `NOT merged: gap 2.8 m < 6.0 m but the joint box is refused: <why>`.

| # | Do | Expect |
|---|---|---|
| 20-6 | No. 40 Alderwood (shot ae3ba73f) and other boxes 2-6 m apart | one box, one outline; log `pair ... MERGED` |
| 20-7 | Boxes 6 m or more apart | two boxes; log `NOT merged: gap ... >= 6.0 m` (below 15 m only) |

### 20.5 The sweep (owner: "make 0.4.10 robust"): one box definition, every box kind and vehicle kind

**One definition of the box.** Exit and entrance boxes: the painted window (Boxes/BoxShapes.Entrance, the paint's own
code and parameters; boxes less than 6 m apart on one road built as one, Core ExitBoxWindows.Assemble), projected onto
each lane. A box the paint does not draw is not obeyed. Junction boxes: the junction itself. Its entry is the stop line
(the lane start at the junction mouth), and its exit is where the car's lanes leave the junction. The painted junction
box lies inside that: its mouths only slide INTO the junction (give-way / STOP arms), and it keeps 0.5 m clear of the
crossings, which are inside the junction. So a car held at the line never has any part in the paint. Room past a
junction box is room past the junction. A junction box and an exit box at its mouth are one box for driving, as in the
paint (20.5 rule 2).

| Who | Where it waits | Room it needs (free NOW, whole length incl. trailers + margin) |
|---|---|---|
| Through traffic (car, van, bus, artic) | before the box line (boxes on its lane closer than its own length + margin are one box for it) | past the box |
| Turning into a building inside the box | before the box line | on its way in (into the building) |
| Leaving a building into a box | before the pavement | past the box on the lane it joins |
| Right-turner (UK exception) | at the line; inside the box only at its turn spot | past the box on its exit; at the turn spot only oncoming traffic stops it |
| On-duty emergency vehicle (siren, priority 108) | not held by boxes (as at exits; stock) | n/a |

**Where the car would wait after the box (new).** A car that has room may still have to stop at the far end of that
room. The room window (its length + margin, plus the vehicles queued in the box ahead of it) now also looks at:
1. A further box on the same lane or on its next lanes starting inside the window: the room is needed past THAT box too.
2. A boxed junction starting inside the window (an exit box near a boxed junction, or a box right after a junction box):
   the room is needed past the junction.
3. A red or amber light inside the window: no room now. The car waits before the box until it is green. Example:
   Alderbrook's 843722 box ends 8.4 m before the north light, so a bus or artic would otherwise stand at red with its
   tail in the box.
This also closes review finding F2: on a curved edge built in two lane pieces, a box over the middle continues on the
next piece and its room is counted past the painted end.

**Deadlock check.** Every new wait is on something DOWNSTREAM of the car: vehicles past the box, a further box's
vehicles, a light (lights cycle). A held car stands outside every box, so it blocks nobody inside one. No wait points
back up the road or across to an exit. The G15 rules are unchanged: entrants are not "present" for exits, side obstacles
count only for through traffic, and no part of a vehicle is in a box. G15LoopTests / G15bSideObstacleTests pass.

**Proof offline.** BoxScenarioTests is a step model of No. 40 Alderwood and of Alderbrook northbound. It uses the live
windows, the job's own Core decisions, and cars, vans, buses and artics. It has a full one-way yard with entrants and
exiters, a boxed junction, a light, a stop-and-go exit, and heavy jams; 20 seeds. Result: 7988 vehicles through, 1031
in and 1091 out of No. 40, **0 vehicle-steps standing in a painted box**, and everything drains when demand stops (no
deadlock). The same scenes under 0.4.9's rules: 37653 standing-in-box steps.

### 20.6 Known residuals (proven absent in the model, possible in game)

(a) A light that turns amber / red AFTER a car decided on green can stop the car with part of it in the box. Two
kinds: it entered on green and the amber caught it before the line; or the amber came while it was too close to stop at
the box line, so it entered committed. The light's remaining green is not read; the game's amber ("stop if you safely
can") is kept. In the model it happens at Alderbrook's light 8.4 m past a box (20.8 has the figures); in game, watch
boxes within one vehicle length of a signal. (b) A give-way / STOP line inside the room window is not treated
as a stop. Holding the car before the box would hold it forever, since the sign is always there. Such a box is a paint
mouth-margin question: tell main if one shows up. (c) A bus stopping at a stop inside a box is stock behaviour (the stop
is the bus's target). (d) The free-now counter (20.2) is an upper bound (review F6). (e) The driving list now caches
each box's window by its own inputs (road, mouths, crossings / stop lines, exit point). A failure there keeps the last
good boxes and logs once (review F1).

### 20.7 Owner: "if the car can't clear the whole thing, the car should not enter the box" (hard invariant)

**Where the front bumper stops.** The game measures the distance to a stop point from the vehicle's FRONT BUMPER (the
copy's IterateFirstLane: `num2 = m_Distance - bounds.max.z`; for an artic the tractor's front). A box hold stops the
front 0.9 m (car) before the painted line. A car is held only if it can still stop there: the game's can-stop test,
braking distance <= distance to the line. A car that cannot stop any more is already committed and carries on. A
property test (20000 random approaches) proves it: every car the rule can hold can stop with its front at or before
the line, and a car whose front is past the line is never "held" there. The only exception is the UK right-turn spot
(owner setting).

**Deciding early enough.** The room test runs from the moment the car must start braking for the line. Two changes
make sure the room it sees then is still there when it arrives:
1. Vehicles ahead of it count as going first: those in the box, and now also those between the car and the line. A car
   following another into a box gets room only for both.
2. A light past the box: a red / amber light inside the room = no room (20.5).

**Counters (end of the timing line):**
- `standing with the front bumper past a box line (nose in the box) N (must be 0)`: a standing car whose front bumper is
  past the painted line while its rear is still before it.
- `standing inside a box (not at the right-turn spot) N`: any part of a standing car in a box. This is what the owner
  would see as "a car stopped in the box"; only the light-change cases of 20.6 (a) should cause it.
Both count standing cars at junction, exit and entrance boxes. A right-turner waiting at its spot (for the oncoming gap
or for exit room) is not counted. (Final review 3 F1: the first version counted only HELD cars, and a held car is
before its line by definition, so it could never be non-zero.)

**Review R1 (final Sonnet review): the light request.** A signal changes only to a group a car has asked for, and a car
asks only when its look-ahead reaches the signal's lane (TrafficLightSystem.GetNextSignalGroup; the copy's
IterateNextLane `needSignal`). A car held before a box for the light never got that far (its look-ahead ends at its
hold), so on a quiet approach nobody asked: the light would never turn green. New block S2-52: such a car asks that
light for green itself, with the game's own request. Model: without it 19 of 20 Alderbrook runs never drain; with it
all 20 do.

**Model result (BoxScenarioTests).** Each vehicle kind has its own braking and acceleration, the light is
demand-driven, and there are 20 seeds incl. heavy jams; final figures after the chain rule in 20.8.

| # | Do | Expect |
|---|---|---|
| 20-8 | Timing line | `front bumper past a box line while held 0` always; `standing inside a box` small |
| 20-9 | A quiet signalised approach with a box just before the light (Alderbrook north, bus / artic) | the held vehicle gets green (no endless red) |

### 20.8 Owner: chains of boxes (merged or separate, per vehicle length)

**The rule.** For each vehicle, walk the boxes along its path. A gap between two boxes is a place it may stop only if
the gap fits its whole length (trailers included) plus the margin. If it does not, the boxes are one for that vehicle:
it may not enter the FIRST box unless it has free room for its whole length past the LAST box of the chain, or in a
later gap that does fit it. This holds whatever the paint did. It is never shorter than the paint's 6 m: a gap under
6 m is painted yellow, so no vehicle stops in it.

Example, a car (4.2 m, needs 5.2 m) and an artic (16.5 m, needs 17.5 m):

| Gap between two 8 m boxes | Car | Artic |
|---|---|---|
| 2.8 m (painted as one box) | one box | one box |
| 6 m | may wait in the gap | one box: room past the second |
| 12 m | may wait in the gap | one box: room past the second |
| 25 m | may wait in the gap | may wait in the gap |

**How.** On the same lane, the vehicle's boxes closer than its length + margin are one (ExitBands.Chain). On the next
lanes, a box or boxed junction starting before the car's own room ends extends the room past it (RoomScan.ExtendPast);
the room window includes the vehicles queued in the box ahead of it.

**Three changes made while proving it:**
1. A gap of exactly length + margin fits (it was counted as too short).
2. A vehicle STANDING ahead inside the box / chain, or before its line, now keeps the car out (free now). A moving one
   still goes first. Exception (Highway Code 174): a right-turner with the UK exception may wait behind other vehicles
   waiting to turn right.
3. Two fixes in the shared room scan (both also in the job):
   - `Reach` counted vehicles already past the box, which pulled in boxes the car could never be made to stop at
     (spurious holds). It now counts only vehicles still in or before the box.
   - A red / amber light is now a standing obstacle at its line, after the vehicles queued in front of it. The room is
     the space before the line minus them; before, the light only counted when it fell inside the car's own reach.

**Tests:** ChainTests (car vs artic at 2.8 / 6 / 12 / 25 m; boundaries at 17.5 m for the artic and 6 m for the car;
three boxes with gaps 3 + 10 m; a merged group plus a box 8 m after it; chains across lanes; a vehicle standing in a
gap that fits or does not). The scenario model gained a chained road (gaps 2.8 / 6.1 / 12 / 25 m, then a boxed
junction).

**Final model result** (Alderwood No. 40, Alderbrook, the chained road; car / van / bus / artic; 20 seeds incl.
heavy jams; vehicle-steps; final review 3 F3 split by cause):

| | 0.4.10 | 0.4.9 rules |
|---|---|---|
| Standing in a box, no light change involved | **0** | 16527 |
| Entered on green, then stopped by amber / red in the box | 3873 | 6010 |
| Committed into the box during amber / red (could not stop any more) | 3051 | 13182 |
| Nose in a box while standing, no light change involved | **0** | (6951 incl. light) |
| Nose in a box while standing, light change | 1589 | |

All light-change cases are at Alderbrook's light, 8.4 m past the 843722 box. Everything drains.

The model also counts "committed at a line with less room than needed" (4082; 0.4.9: 1706). This is a snapshot: the
car decided with room (the car ahead still inside the box, or a free gap), and a moment later the measured room dipped
while that car was leaving the box. It is not a car stopping in a box: outside light changes none of them ended
standing in one (row 1).


### 20.9 Owner: merge = one real box

Boxes that merge (gap under 6 m, same road and side of a median, no marking or mouth margin in the way) are BUILT as one
box. Every member gets the single outline of the joint window, from the first box's start to the last box's end, kerb to
kerb, built by the same straight-box code as any exit box (Paint/YellowBoxPaintSystem.JointBoxes -> BoxShapes.Entrance
over the joint window -> EntranceBox.BuildStraight). BoxMerge keeps identical rings once: one border, one continuous
hatch, no strip of plain road, no inner border. Driving uses exactly that window (the gap road is box: Boxes/DrivingBands
-> ExitBoxWindows.Assemble -> LaneWindow on each lane). The kerb inset, marking margin, median side and zebra / mouth
margins apply to the merged outline as to any box; a marking in the gap keeps the boxes apart (logged per pair, 20.4).

Tests (MergedOutlineTests), on the live road (16 m, 3 m footways), for No. 40 Alderwood (34.7..42.7 + 45.5..53.5 ->
34.7..53.5) and the Alderbrook pair (110.0..118.0 + 119.3..127.3 -> 110.0..127.3):
- a single straight-sided polygon (4 corners at the kerbs and at the first start / last end);
- the gap is covered kerb to kerb;
- area = joint length x carriageway;
- drawn as one ring;
- the old separate outlines left the gap uncovered;
- a marking in the gap still keeps them apart.

### 20.10 Final review (Sonnet, APPROVE): what to watch in game

1. The merged outline itself (20.9): the paint and driving code that builds it needs the game, so only the core geometry
   is tested offline. Check one merged pair in game (No. 40 Alderwood): one outline, one hatch, cars stop at its first line.
2. Driving and paint choose their boxes from slightly different lists (driving: every box with a valid window; paint:
   the boxes it drew). Where they differ, driving keeps two boxes the paint merged, never the reverse (the safe side).
3. A vehicle standing in a box ahead (e.g. a bus at a stop inside a box) keeps followers before the box until it moves:
   watch for long queues behind such a stop.
4. The stricter room test makes cars commit at a line with less measured room more often in the model (4082 against
   1706, 20.8). None of them ended standing in a box without a light change.
5. A standing junction-box car with only its TAIL in the box is counted only where its nose / lanes show it (the probe
   sees the nose case and exit / entrance boxes fully).

## 21. 0.4.11: vehicles turning INTO a building give way (JR-T-017, gap G21)

Driving changes act only while Car driving is the Junction Rules copy and the setting "Vehicles leaving buildings give
way" is on (0.4.11: it also covers turning in). Evidence: 0.4.10 live log 17 Nov 18:59-19:00 (jrlogs copy
`JunctionRules.Mod.2026-11-17_0410.log`), main's read-only snapshot at frame 36633332, owner shot 60253f58, decompile
2026-11-13 (`~/Code/decomp/yb-2026-11-13/full`).

### 21.1 What happened (G21)

Van 380934 (to Scandic Baron, No. 40 Alderwood Street) came along the eastbound lane (north, z ~278) and turned right
(left-hand traffic) across the westbound lane (z ~271) into No. 40's entrance (x ~-1994). The always-on entrance log:
18:59:29 `held before the box (way in full, waits on the road)`, 19:00:05 `stands in the box 30 s (way in has room:
waiting to turn)`. Then (frame 36633332) it was at (-1995.1, 275.6), heading 155, 2.5 m/s, crossing in front of the
westbound traffic. So: the G9 room rule worked (it waited before the box while the yard was full); once there was room
it drove INTO the box and waited there, at the game's own give-way, with its nose at the oncoming lane; it then went on a
gap that was long enough to reach the lane but not to clear it.

### 21.2 How the game handles a turn into a driveway (cites)

- The way in is a lane of the ROAD (owner = the road edge), built as a node lane by `Game.Net/LaneSystem.cs`
  `CreateEdgeConnectionLane` (3646+): it starts ON the road lane the vehicle drives (StartNode = PathNode(edge, lane, b,
  t2), 3858; curve from `MathUtils.Position(road lane, t2)`, 3794-3803) and runs across the other lanes to the drive.
  Flags `Unsafe | SideConnection` (3868), TurnLeft / TurnRight by its bend (3873-3880), no `Yield` (only the way OUT,
  isSource, gets Yield: 3869-3872).
- Its overlaps with the lanes it crosses are computed because it is a node lane (`LaneOverlapSystem` 1161), and the Unsafe
  tie-break gives it the lower priority (1386-1389). In the vehicle's own lane check that only means: give way to a road
  vehicle already ON the crossed lane whose stock 2 s look-ahead reaches the overlap, and to reservations. A vehicle still
  in the junction before the road is not seen.
- The vehicle asks road traffic for space on it (`CarNavigationSystem` 920-923: Unsafe and edge-owned = RequestSpace;
  2277-2287: reserved at priority 96). That only stops a road vehicle that would stand across the drive in a queue
  (`CheckOverlapSpace`), never moving oncoming traffic.

### 21.3 Why Junction Rules missed it

- The 0.2.4 turn-across rule (`Core/TurnAcross.cs`, block S2-13) classifies only JUNCTION pairs: oncoming straight = a
  `Forward` lane whose end edge is the turner's start edge. The way in carries `Unsafe` (sign status differs from the road
  lane) and the road lane has no `Forward` flag: the rule keeps stock (test `G21EntryTests`).
- The exit rule's driveway hold (S2-29, 0.4.4-0.4.10) acted only on a way OUT (`SideConnection + Yield`).
- The whole-path rule (S2-04) does run on the way in (any node lane), but it decides at the first crossing, INSIDE the box
  (the 0.4.8 G9 design: "enters and waits for the oncoming gap inside the box at the game's own give-way"), and with the
  time to REACH the conflict, not to CLEAR it with the whole vehicle (the exits' 0.4.8 G7 measure).

### 21.4 Now

`Stage2/JRExitGiveWay.cs EntryRoadHold` (rule in `Core/EntryGiveWay.cs`), reached through the existing S2-29 call with
the ROAD lane (every lane the vehicle's look-ahead walks, its current lane included): no new copy block.

1. **Every building access, boxed or not (owner, 17 Nov).** The box only adds "wait before the box" and "room in the
   entrance". A path whose next lane is a way in: a drive's way in (`SideConnection` without `Yield`; the G21 case, laid
   from every road lane, the far one too), or a building's car park gate (`Connection`), a lot (`Area`) or a building's
   parking lane. On-duty emergency vehicles: stock.
2. The lanes judged. Drive: the way in's overlaps with road car lanes it CROSSES (not the lane it leaves: a merge at its
   start; not another drive lane; not water). Gate / lot / parking: the game ties these to the ONE nearest road lane
   (`LaneConnectionSystem` 291-306, `LanesModifiedSystem` 369-378) and spawn-point targets only to lanes of the same
   direction (`PathfindTargetSeeker` 1483-1486), so they normally cross nothing; where the building-side end of the way in
   lies beyond another road lane anyway, that lane is crossed (lateral geometry, as the exits). None (a left turn in,
   left-hand traffic; a gate on its own side) = no wait. A building road meeting the public road at a JUNCTION is a
   junction turn: the Whole junction rule and the 0.2.4 turn-across rule decide it (unchanged).
3. Where it waits. **Owner box rule (17 Nov): a vehicle turning into a building never blocks the box.** At a boxed
   entrance it waits in its own lane BEFORE the painted box line (0.4.10 windows, `LaneBands`), no part inside, and
   enters the box only when it can go straight through: oncoming gap clear (this rule) AND room in the entrance (G9).
   Once it can no longer stop before the line it goes straight through (counter `could not stop before the spot`); it is
   never held inside the box. The UK in-box turn spot applies to junction boxes only: for a way in that leaves inside a
   box the whole-path decision on the way in (whose spot lay inside the box, 0.4.8-0.4.10) is left out (`BeginLane`,
   `WayInBoxed`; counter `boxed ways in decided at the box line only`); the game's own checks on the way in stay.
   Unboxed: where the way in leaves its own lane (the drive start). It never waits across the oncoming lane.
   Keep clear (review 1 F1): the waiting spot moves back so its body does not stand on another drive's crossing of its
   lane (two turners into facing drives 2-10 m apart otherwise stood each on the other's way: a deadlock).
   The way in must start on the lane the vehicle is on (drive: the game's start node; gate / lot: within 3 m).
4. The judgement is the exits': every road vehicle on a crossed lane and on the lanes feeding it (junction lanes, then
   the road before them; red / amber-can-stop skipped) blocks when its braking distance + speed x (2 s + the turner's time
   to CLEAR the lane with its whole length from the spot) reaches the conflict; one standing within 10 m of the conflict
   blocks (the exits' G7 present rule), unless it is itself waiting to turn into a building (room or gap; G15 rule) or a
   vehicle stands between it and the conflict (only the nearest standing vehicle is present: review 1 F1, queued
   followers). Crossed = a true crossing (both overlap sides, no merge flag).
5. The G9 room test into the building is unchanged (same road lane, earlier in the step): both must pass.

**Loop check.** The entry hold waits only on road vehicles on the crossed lanes; road traffic never waits for it (stock
gives the way in the lower priority; the turner holds nobody but the vehicles behind it in its own lane). Two turners
into facing entrances would wait for each other under the present rule alone; a standing vehicle that waits to turn in
is therefore not present for an entrant's scan (event kind 20, like G15's kind 17). Exits still wait for a gap-held
entrant standing in the lane they join (it waits only for road traffic, not for the exit: no cycle). Facing drives a few
metres apart: keep-clear (item 3) keeps each waiting body off the other's crossing (test `G21FacingDrives`: offsets -12..12 m,
mutual blocking in several offsets without it, none with it); followers queued behind a waiting turner are not "present"
for the facing turner (only the nearest standing vehicle is).

**Exits and the box.** An exiter never stands in the box: it decides before the sidewalk (0.4.8), needs room past the box
for its whole length, free now (0.4.10), and waits only in the lot; unchanged in 0.4.11 (tests G7 / G15 / G18G19).

**Owner box scenario** (`G21OwnerBoxRule`): the No. 40 van at 30 km/h towards the box line with oncoming traffic all along
stops right at the line, no part ever inside the box; an exit with room past the box goes; the 0.4.10 in-box position had
the body inside the box.

**Model** (`G21EntryTests`, the Alderwood No. 40 scene: way in leaves the eastbound lane centre z 278, westbound lane
269.25..272.75, overlap from ~5 m along the way in for ~6 m, box line ~4 m before the drive start; oncoming vehicles at
gap 2..150 m and 1..22 m/s, 3278 scenes; "unsafe go" = the oncoming vehicle reaches the lane before the turner has
cleared it):

| Turner | 0.4.10 own time used | 0.4.10 unsafe goes | 0.4.11 boxed: clear from the box line | unboxed: clear from the drive start | 0.4.11 unsafe goes (both) |
|---|---|---|---|---|---|
| car 4.5 m | reach 0.60 s | 0 | 4.07 s | 3.40 s | 0 |
| van 6.5 m | reach 0.79 s | 149 | 4.88 s | 4.21 s | 0 |
| artic 18.5 m | reach 1.09 s | 858 | 7.98 s | 7.32 s | 0 |

The shot's case (a westbound car ~28 m away at ~10 m/s): 0.4.10 turned, 0.4.11 waits before the box. Price: more waiting
(the van waits in more scenes than in 0.4.10), most for lorries on a busy road.

### 21.5 Log and counters

- Timing line (exit setting on), at the end: `turning into buildings: judged for a gap N (unboxed U, gate / lot / parking
  G), standing held for a gap M (unboxed V), not waiting for another vehicle waiting to turn in J, boxed ways in decided at
  the box line only (no in-box spot) B, spot moved back to keep another drive clear K, could not stop before the spot
  (went on unjudged) C`. C should be small; the 0.4.10 timing counter `standing inside a box` must not rise from turners. G is expected ~0 (see 21.4 item 2); non-zero = a gate / lot whose way in crosses a lane (now judged).
- Entrance log (boxed entrances, always on): sample `held before the box (way in has room; waits for a gap in the traffic
  its turn crosses)`, notable `held before the box 30 s waiting for a gap ...`, summary `X held before the box for a gap
  in the traffic their turn crosses (Y over 30 s)`. The 0.4.10 notable `stands in the box 30 s (way in has room: waiting
  to turn)` should now be ~0 (only a turner that could not stop before the box line and waits at the drive start).

### 21.6 What to watch

| # | Do | Expect |
|---|---|---|
| 21-1 | No. 40 Alderwood, eastbound vans turning in with westbound traffic | they stop BEFORE the box line in their own lane (no part inside) and enter only to go straight in; the box stays empty for westbound and exiting traffic |
| 21-1b | Two drives facing each other a few metres apart, both with turners waiting | the second one stops short of the first one's crossing; no standstill |
| 21-2 | Any unboxed driveway on a busy two-way road | the turner waits in its own lane where the drive leaves it, never part-way across |
| 21-3 | Left turns in (near side) | no wait (apart from the G9 room test at a box) |
| 21-4 | Timing line end | `standing held for a gap` rises on busy roads; `at the drive start inside a box` ~0 |
| 21-5 | Busy road with a stationary oncoming queue held before the entrance box | **owner question:** the turner waits for a queued vehicle standing within 10 m of its crossing (the exits' present rule, kept for consistency). UK practice would let it turn through the box gap. If turners wait behind such queues for long, say so: the change is one condition (a standing vehicle held before that box would not count) |
| 21-6 | Lorries turning in on a busy road | long waits are expected (whole-length clear time); queues behind a waiting turner in its own lane are expected |

### 21.7 G22: the same-exit-lane box exception (owner rule 17 Nov)

Owner, verbatim: "Cars from a yielding (or if everyone is yielding, then the one getting STOP is 'prioritised' vs YIELD
exits) approach: may enter a box whose exit is blocked iff the spot where the car would wait (its whole body) only crosses
paths into the same exit lane it is queued for." (`Core/SameExitException.cs`, `Stage2/JRSameExit.cs`, hooks in
`JRYellowBox.BoxHold` and `BoxTurnRoomHold`; job side only, no copy change.)

Reading (as main's, plus three points stated):
- **Junction boxes only.** Exit / entrance boxes keep G9 / G21 (a vehicle turning into a building never waits in the box).
- **Eligible approach**, from the game's own sign flags on the junction's car lanes (Stop < Yield < none / RightOfWay):
  with a priority approach at the junction every yielding approach (YIELD and STOP) qualifies; if every approach yields,
  only the STOP approaches when STOP and YIELD are mixed; all-STOP or all-YIELD: every approach (point 1: the owner's text
  does not cover these; "everyone equal" is the reading). Signalled / level-crossing / roundabout junctions: never (point
  2: conservative). U-turn (Unsafe) and drive lanes do not count when ranking (an unsigned U-turn would look like a
  priority road).
- **Footprint** = where it would stand with the exit lane full: front a margin before the end of its junction lane, or
  before an exit zebra (the zebra rule never lets it stand on one), or behind a vehicle already standing ahead on its own
  junction lane (box chains); whole length incl. trailers + the room margin behind. Every overlap of its junction lane the
  footprint touches must be a car lane ending in the SAME exit lane (`Lane.m_EndNode` equal). Any other car lane (a
  through path, a path to another exit lane), any pedestrian crossing (point 3: conservative), any track: the normal rule
  (wait before the box).
- Only the ROOM test is replaced; giving way is untouched (whole junction, turn-across, UK right-turn spot). The exception
  is also applied at the UK turn spot's room re-check (a turner that entered under it is not held there for room on a
  footprint nobody checked).
- **Combination**: no-nose / free-now room still bind everyone not admitted; a car standing in the box under the exception
  is counted apart (`standing in a box under it`), not as `standing inside a box`. Box chains: the vehicle ahead moves the
  footprint back (judged where it would really stand). G15 deadlock tests unchanged (entrance boxes). Deadlock: the car
  blocks only paths into the exit lane it waits for; whoever it blocks needs that same full lane: no new wait cycle
  (test: 5000 random footprints, every admitted one touches only same-exit paths).

Tests (`G22Tests`): T junction with a YIELD side road: left turn (merge only) may enter; right turn, car may enter, artic
(18.5 m) may not (its body reaches back over the westbound through path); a vehicle already waiting ahead pushes the
footprint back over the through path (may not); exit zebra (footprint before it, may enter; on it, never); 4-way straight
across: car may enter, 8 m van may not (crosses the opposite side road's right turn); a footprint on a through path to
another exit: may not. Eligibility: T (YIELD yes, priority no), priority + YIELD + STOP (both), all-yield STOP vs YIELD
(STOP only), all-STOP / all-YIELD (all), signals (never).

Timing line (box rule on), at the end: `entered a box under the same-exit-lane exception N (standing in a box under it M)`.

| # | Do | Expect |
|---|---|---|
| 22-1 | A boxed T with a YIELD side road and a queue in the exit lane | short side-road cars turning left (and right where the merge is the only thing under them) enter and queue; lorries wait at the line; the priority road is never blocked |
| 22-2 | All-way junction with STOP and YIELD arms | only STOP-arm cars use the exception |
| 22-3 | Timing line | the exception count rises where queues form; `standing inside a box` does not rise from it |

### 21.8 G23: a car that would not come out on green (Crescent St x Eastgate Lane)

Owner (live): "what makes this car not want to come out on green?" Hatchback 910624, head of the Eastgate south arm (22
queued), short LEFT turn into Crescent westbound, on green, exit lane empty, westbound Crescent at its red, people waiting
at the kerbs; the game's blocker type of the queue head: **Temporary**. In the game (2026-11-13 copy) Temporary is set only
by `CheckPedestrian`: the car was held for a PERSON. `CheckPedestrian` brakes not only for people on the car's way over a
crossing but also for a person within the car's half width + the person's size of its path line (target offset outside
the car's range on the crossing, then the distance test), and for a STANDING person it only creeps by at 3 m/s. A short
left turn hugs the corner where people wait at the kerb for their own green. Junction Rules 0.3.2 ("cars never push
through people", S2-08b) turned that creep into a full stop, so the car waited for people who were not crossing until
they got their green, which is the car's red: it never left on green. (The car had already sat 20 min at a driveway exit
on the same alley earlier, exit trace 18:57-19:18: that was the exit gap rule on a busy alley, a separate matter.)

**Now:** the full stop applies only to a person whose position on the crossing is inside the car's range on it (+-0.5 m;
`CrossingPush.PersonInPath`, S2-08b hands the person's position and the car's range, same line); a standing person beside
it gets the game's own creep past. People walking or standing on the car's way: unchanged (never pushed through).
Counter: `people beside a crossing passed at the game's creep (not on the car's way)`.

**Owner rule: vehicles stopped at a red never count as approaching traffic.** The whole-junction projection and the exits'
feeding-lane scan already skip a vehicle that will stop at its red (0.2.4 `FeedingSignal`). One hole: the exits' (and now
entries') 10 m "present" rule counted a vehicle STANDING in a queue held by a red within 10 m of the conflict. Now a
vehicle standing before the conflict on a lane whose every way on at its end is red / amber is not present
(`ExitGiveWay.QueuedAtRedNotPresent`, `LaneEndsAtRed`); one standing ON the conflict still blocks; moving ones are judged as
before (they may still pass the conflict before their stop line). Counter: `standing vehicles queued at a red not counted as
present`. Ruled out for this car (from code): the box room test (exit empty), the arrival projection (red-held feeders
skipped), the zebra sections (room past the exit zebra), the G22 logic (not a yielding arm: signalled).

**Owner follow-up (he watched it): "it was blocked by a pedestrian that waits a bit outside the kerb".** The first cut
above was not enough: a person waiting just off the kerb stands INSIDE a hugging left turn's range on the crossing. Why
they stand there: the game's waiting spot for a red walk signal is the crossing lane's own end point
(`HumanNavigationSystem` 1160-1171: WaitSignal, target = CalculateTargetPos(crossing, its start, lane position) = that end
offset sideways along the kerb), and the crossing ends where its lane meets the pavement, so waiting bodies (radius
~0.3-0.5 m) spill up to about 1 m past it. **Rule now (new copy block S2-53, before the game's pedestrian check, with "Cars
never push through people"):** cars hold only for people CROSSING. A person STANDING within 1 m of either end of the
crossing (`CrossingPush.WaitZoneMetres`), or standing on a crossing whose walk signal is red / amber, is not crossing and is
skipped: the car does not brake for them at all. People walking (also stepping off, or late on red), and people standing
on the car's way further out on the crossing (stopped mid-crossing, zebras) keep the game's check and the 0.3.2 full stop.
Kept: zebras (never pushed through people on them), people pass a blocking car after 2 s, G11 unmarked rule (its safety
floor is never skipped). Counters: `people waiting at a crossing (kerb / red walk signal) not braked for` and the guard
`car held for a person waiting at the kerb (not crossing)`, **must be 0**.

**Watch button:** picks the junction node nearest the camera's aim point (where the centre line reaches each node's
height) within 25 m (was: within 8 m of the centre line; the owner's off-centre camera found none). Watch lines now end
with the blocker: `[person at (x, z), speed v m/s / not moving, on lane L at p, WAITING for a walk signal]` or
`[vehicle at (x, z), speed v]`.

| # | Do | Expect |
|---|---|---|
| 23-1 | Crescent x Eastgate, south arm left turn on green with people waiting at the corner (also just off the kerb) | the car goes on green without stopping for them |
| 23-1b | Timing line | `car held for a person waiting at the kerb (not crossing) 0` |
| 23-1c | Watch button with the camera roughly over a junction | it finds the node; lines show who blocks and where |
| 23-2 | Same with a person stepping onto the crossing in front of it | it stops (never pushed through) |
| 23-3 | Timing line | `people beside a crossing passed at the game's creep` > 0 where corners are busy |

### 21.9 Review 2 (Sonnet) fixes

- Keep-clear shift taken only when the whole body stays on its lane and outside every box (never back into the junction
  box or another box); else the unshifted spot.
- Entrant scans: a standing vehicle on a lane BEFORE the crossed lane is never present (closed a rare four-vehicle loop
  with facing drives near a junction mouth whose queues spill back across it).
- A boxed way in gets no zebra sections either (their first vehicle point was the old in-box spot).
- Settings: the owner box rule for turning in needs both "Cars obey yellow boxes" and "Vehicles leaving buildings give
  way" (default on). With the exit setting off, turners into buildings behave as in 0.4.10 (stock give-way at the in-box
  spot).
- Watch (owner may confirm): a boxed turner that could not stop before the box line (already in the band, moving) crosses
  with the game's own checks only (counter `could not stop before the spot`); it is never held inside the box.
