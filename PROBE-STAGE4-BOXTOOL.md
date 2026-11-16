# Probe kit: Junction Rules 0.4.3-probe (stage 4, yellow box selection tool; cars never stop on a zebra; cars obey yellow boxes; straight-sided boxes; boxes at car exits)

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

Build to install: `~/Code/cs2-junction-rules/.staged/0.4.3-probe/`. 0.4.2-probe (jr-t-010 line) is installed live by main (code 59e731f, sums c781df0); never write into `.staged/0.4.2-probe`. While `.staged/0.4.3-probe` holds a `STALE.md` it is a managed
build only: main does the Burst build in `STALE.md` (game closed, at the owner's quit), then puts the five sums into the
install block below. (0.4.1-probe stays in `.staged/0.4.1-probe` as the rollback.) Until then the block refuses (placeholder sums `0000...`).

Built for game **1.6.4f1 (1097.158a)** only (same version gate: on another version saved boxes are not painted and the
copy does not run; the boxes stay in the save).

## 1. Install (game closed only)

```
(
  CS2="$HOME/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
  BACKUP="$HOME/Code/cs2-junction-rules/.staged/0.2.4-installed-backup"
  cd ~/Code/cs2-junction-rules/.staged/0.4.3-probe || exit 1
  if [ "$(ps -eo comm | grep -ci cities2)" != 0 ]; then echo "GAME IS RUNNING - stop, nothing installed"; exit 1; fi
  if [ -e STALE.md ]; then echo "STAGED BUILD IS STALE - see STALE.md, rebuild first; nothing installed"; exit 1; fi
  if [ ! -d "$CS2" ]; then echo "CS2 folder not found - nothing installed"; exit 1; fi
  sha256sum -c --strict - <<'SUMS' || { echo "SHA256 MISMATCH - nothing installed"; exit 1; }
0000000000000000000000000000000000000000000000000000000000000000  JunctionRules.dll
0000000000000000000000000000000000000000000000000000000000000000  JunctionRules.pdb
9e8f8a397f70bbeb121ef7224981278773595c1fe2ce7e31de01d4c4df2b65bf  JunctionRules.mjs
0000000000000000000000000000000000000000000000000000000000000000  JunctionRules_win_x86_64.dll
0000000000000000000000000000000000000000000000000000000000000000  JunctionRules_win_x86_64.pdb
SUMS
  if [ -d "$CS2/Mods/JunctionRules" ] && [ ! -e "$BACKUP/JunctionRules.dll" ]; then
    mkdir -p "$BACKUP" && cp -p "$CS2/Mods/JunctionRules/"* "$BACKUP/" && echo "backed up the installed files to $BACKUP" || { echo "BACKUP FAILED - nothing installed"; exit 1; }
  fi
  mkdir -p "$CS2/Mods/JunctionRules" &&
  cp JunctionRules.dll JunctionRules.pdb JunctionRules.mjs JunctionRules_win_x86_64.dll JunctionRules_win_x86_64.pdb "$CS2/Mods/JunctionRules/" &&
  echo "installed 0.4.3-probe"
)
```

Rollback: to 0.4.2 (the installed JR-T-010 build: box driving rule, old entrance boxes; 0.4.3 exit keys are skipped by
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
building with two boxed exits on one road has two separate bands (a car passing both checks each one); a car leaving at
one exit is not held by the building's other exit box.

| # | Do | Expect |
|---|---|---|
| D1 | Load the city (0.4.2 entrance boxes) | log `migrated` lines (section 12 X3), then one `yellow boxes (driving): N boxed car exit(s)` line; N = boxed exits (junction-migrated ones not counted) |
| D2 | Watch a boxed house drive (Capper Street) with a queue on the road | queue stops short of the band centred on the **drive** (not the lot middle); a car leaving the drive waits for room |
| D3 | Box a second gate of a car park / depot (X2) | the driving line count goes up by one; each gate keeps its own band clear |
| D4 | Remove one exit box of a two-box building | its band goes (line count down by one); the other gate still kept clear |
| D5 | Lindbergh x Anchor (junction box, section 13) | as 0.4.2: no vehicle stops inside; timing log `box entries held`, `right-turn waits` |
| D6 | A boxed exit on a bend | band centred on the drive; the painted box (section 12 X7) covers the drive |
| D7 | Timing log on | counters as 0.4.2: `box entries held N, right-turn waits N`, entrance band holds (main / leaving) |

Risks to watch: the job now carries the exit list by value (1 KB per car step, 0.4.2: 128 bytes): compare the
navigation timing with 0.4.2 (timing log) and report a clear slowdown; a building whose drive moved more than 2 m
since the box was placed has no band until the paint re-keys or drops it (log `without an exit now`).
