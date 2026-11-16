# Probe kit: Junction Rules 0.4.2-probe (stage 4, yellow box selection tool; cars never stop on a zebra; cars obey yellow boxes)

**0.4.2-probe (JR-T-010), what changed since 0.4.1:** new driving rule **Cars obey yellow boxes** (default on;
section 11): at the boxes placed with the tool, a vehicle does not enter a box without room for its whole length past
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

Build to install: `~/Code/cs2-junction-rules/.staged/0.4.2-probe/` (0.4.1-probe was Burst-built from bf4a26b and installed by main on 16 Nov). While that folder holds a `STALE.md` it is a managed
build only: main does the Burst build in `STALE.md` (game closed, at the owner's quit), then puts the five sums into the
install block below. (0.4.1-probe stays in `.staged/0.4.1-probe` as the rollback.) Until then the block refuses (placeholder sums `0000...`).

Built for game **1.6.4f1 (1097.158a)** only (same version gate: on another version saved boxes are not painted and the
copy does not run; the boxes stay in the save).

## 1. Install (game closed only)

```
(
  CS2="$HOME/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
  BACKUP="$HOME/Code/cs2-junction-rules/.staged/0.2.4-installed-backup"
  cd ~/Code/cs2-junction-rules/.staged/0.4.2-probe || exit 1
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
  echo "installed 0.4.2-probe"
)
```

Rollback: to 0.4.1 (boxes kept, box driving rule gone): the same block with `.staged/0.4.1-probe` and its sums
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

## 11. 0.4.2: cars obey yellow boxes (JR-T-010)

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

Settings (Options > Junction Rules, yellow box group): **Cars obey yellow boxes** (default on; off = 0.4.1) and
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
buffer sorted by the objects' TARGET curve position (`m_CurvePosition.y`; NetUtils.AddLaneObject / UpdateLaneObject),
not by where they are (`.x`), so a car whose target lies short of the lane end (stopping) sits in the buffer before a
car behind it that drives on. 0.4.1 read the buffer in storage order and could therefore judge "no room" wrongly (a
near car read after a far one). 0.4.2 reads each lane in curve order (Core ScanOrder; cost one pass per vehicle read,
the scan stops after a few vehicles, and it now also stops once the next vehicle is past the window). The counter
stays as a diagnostic.

Deadlock notes (report, no safeguard by brief):
- Gridlock moves to the box entry: if the exit never clears, vehicles stand at the line indefinitely (by design).
- Opposing right-turners both waiting in the box can block each other's view of the oncoming gap (as in 0.4.1).
- A turner waiting in the box waits as long as oncoming traffic never gaps (UK practice; no timeout).
- A boxed entrance in front of a queue that never clears keeps the driveway vehicle waiting (it never joins).
- Vehicles from another arm already in the box are not counted against the exit (only the own path ahead).
- An unknown rest of the path (destination, parking, end of the read lanes) counts as free, as in section 10.
- Z O: a car waiting at O blocks the zebra for nobody, but cars behind it wait before the zebra (O occupied) until it goes.
