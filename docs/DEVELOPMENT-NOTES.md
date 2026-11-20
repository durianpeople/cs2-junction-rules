# Junction Rules (working name)

A Cities: Skylines II mod. Stage 1 (0.1.0): switch off the game's car-driving system
(`Game.Simulation.CarNavigationSystem` and its `Actions` system) and run an unchanged, Burst-compiled copy of it
(plus `CarLaneSpeedIterator`) in the same slot, switchable in game, to prove the copy is as fast and behaves the same.

Stage 2 (0.2.0): the first driving rule, inside the copy only, default off. **Junction gap rule = Whole junction**: a
car that has to wait stops just before the first point where its path crosses another lane's path, then makes one go/
wait decision for every remaining conflict with the game's own gap judgement, and never stops again inside the junction
to give way (collision braking stays). The decision is taken only by the car at its waiting spot: a queued car follows
with the game's own car-following and decides for itself when it reaches the spot; amber lanes count as live; exit
queues stay stock (yellow boxes are stage 3); no starvation safeguard (owner rulings 14 Nov 2026).

0.2.1 (collision fix): mid-junction waiting spots where the whole car fits between two crossings (e.g. a wide central
reservation): one decision per stretch up to the next spot; while crossing, the car re-checks the conflicts it has not
reached yet and makes a safety stop just before the first of them if one is no longer clear and it can still stop with
normal braking (else collision braking only); a car that can no longer stop at its spot keeps the game's own give-way.
Root causes and rulings: `docs/STAGE2-ANALYSIS.md` sections 7-8.
0.2.2: a mid-junction spot must fit the whole vehicle, trailers included (stock attach points; owner ruling 14 Nov
2026, `docs/STAGE2-ANALYSIS.md` section 9), so articulated trucks never wait on a central reservation too short for them.
0.2.3 (owner rulings 14 Nov 2026, `docs/STAGE2-ANALYSIS.md` section 10): **no mid-junction waiting spots** any more
(supersedes 0.2.1 mid spots and 0.2.2 trailers; that length code stays in the repo, unused). The car waits only just
before its first crossing and decides once for its whole path, with an **arrival-time projection**: its own time to
reach each conflict point (game's own acceleration, speed-in-turns cap and braking) is added to the game's 2 s
look-ahead for cars on conflicting junction lanes, and every car on the approach lanes feeding a conflicting lane counts
as a virtual reservation (braking distance + speed x arrival time) in the game's own reservation test. The safety
re-check while crossing uses the same projection. Expect longer waits at the entry and fewer bold right turns.
0.2.4 (owner rulings 14 Nov 2026, `docs/STAGE2-ANALYSIS.md` section 11): approach-lane cars whose own conflicting
lane shows **red** no longer count; on **amber** they count only when the game's own amber test says they cannot stop.
The approach-lane scan is **cheap**: only the car at its entry spot (or re-checking while crossing) scans, followers
never, no repeat scan per car-update, capped work (cars not read are not counted; diagnostics count scans, skips and
cap hits). New setting **Whole junction: turning across oncoming traffic gives way** (default on): with equal sign
status a turn across the oncoming flow (right turn in left-hand traffic, mirrored for right-hand) gives way to the
oncoming straight, and the straight car no longer waits for the turner's reservation (owner-approved deviation).
Analysis, per-case behaviour and owner rulings: `docs/STAGE2-ANALYSIS.md`;
code changes: DEVIATIONS.md section S2; in-game test: PROBE-STAGE2.md.

0.4.12-probe (JR-T-018, gap G26; PROBE-STAGE4-BOXTOOL.md section 22): a yellow box never goes over a median. Exit /
entrance boxes stop 0.5 m before ANY median (every composition piece the game marks BlockTraffic: raised, planted,
platform, barrier, tram reservation kerbs; also on one-way roads) and cover / drive only the building's own lanes;
junction boxes keep 0.5 m clear of median noses (mouth held back as for a zebra); no box where a median runs through a
junction or on a roundabout island. No copy change.

0.4.2-probe (JR-T-010; PROBE-STAGE4-BOXTOOL.md section 11): new setting **Cars obey yellow boxes** (default on;
off = 0.4.1). At the boxes placed with the tool (junctions and building entrances), a vehicle does not enter the box
unless there is room for its whole length (trailers included) past it, the zebra rule's room measure (a moving queue
counts). UK right-turn exception (setting, default on): a vehicle turning across oncoming traffic may enter and wait
in the box when its exit has room. Entrance band: main-road vehicles keep it clear, vehicles leaving the driveway need
room past it. Also refines the 0.4.1 zebra sections (owner): a zebra followed by a gap that holds the whole vehicle
before the first crossing traffic (Z O C C) lets the car cross the zebra and wait in the gap; and the room scan reads
each lane's vehicles in position order (the game's buffer is in look-ahead order). Carries 0.4.1.

0.4.1-probe (JR-T-009; PROBE-STAGE4-BOXTOOL.md section 10): new setting **Cars never stop on a zebra** (default
on; off = 0.4.0). Zebras split a car's path through a node (junctions and mid-block crossings) into sections; a car
drives onto a zebra only when the zebra is clear of people, (Whole junction) every crossing point with other lanes
after it up to the next zebra is clear, and there is room for the whole vehicle past it in the queue ahead (the
game's own measure: a moving car ahead counts its braking distance; **room margin (m)** setting, default 1 m). Where
the whole vehicle cannot wait between two zebras (Z C Z), or between Cs and a zebra (a bus), they are one decision
before the first. Carries 0.4.0 and 0.3.6.

0.4.8-probe (JR-T-014: G7, G5, G9, G10, G11; PROBE-STAGE4-BOXTOOL.md section 18): vehicles leaving a building take
the whole decision **before the sidewalk** (people on the pavement, near-lane gap incl. a vehicle standing next to the
exit, clear time for lorries, far-lane gap, room for the whole vehicle on the lane it joins / past the box); boxes at
building **entrances** too (cars turning in wait on the road unless they fit in); hover arrow from a box to its
building access, no boxes at lot-front fallbacks; Crescent x Eastgate boxable (game-drawn corner kerbs); people give
way at **unmarked** crossings (setting, default on). Diagnostics (timing log, compact exit trace, entrance box log)
are on by default in this probe.

0.4.7-probe (JR-T-011 #17 + JR-T-013 gaps G1-G6; PROBE-STAGE4-BOXTOOL.md sections 16 and 17): yellow boxes that
**overlap or touch are drawn as one box** (one border, one hatch; each box still saved on its own) and the hover shows
the merged outline; an exit box on a **divided road** covers only the building's side, kerb to median; driving follows
the merged paint (section 16.1). New setting **Hide road markings under yellow boxes** (default **off** until the
owner's save gate K1 passes; section 16.2). Gaps (section 17): G1 vehicles leaving by a spawn point, building or
kerbside parking, or a building's own road now give way (and obey exit boxes), new debug setting "Exit trace (log)";
G2 the marking hide re-renders at once, save strip retried / emptied / logged, cleared on unload; G3 a right-turner
waiting in a box crosses only when its exit has room (timing line "right-turn waits for exit room"); G4 Debug "Watch
the junction at camera centre (log)" logs every standing car at one junction (who, waits for whom, which rule); G5 a
box corner that cannot close by sliding is cut by one straight chamfer (on the carriageway, conflict points inside),
refusal log lines give position, arm names and widths; G6 **No parking in yellow boxes** (default on): kerbside
slots under junction and exit boxes are closed at runtime, parked cars leave normally, nothing extra is saved.
Carries 0.4.6.

0.4.5-probe / 0.4.6-probe (JR-T-012 fixes; PROBE-STAGE4-BOXTOOL.md section 15): building lot exits are gated (0.4.5)
and judged before the game hands the vehicle to the road (0.4.6); exit counters per vehicle.

0.4.4-probe (JR-T-012; PROBE-STAGE4-BOXTOOL.md section 15): **vehicles leaving buildings give way** (setting, default
on): every vehicle leaving any building (drive, car park or depot gate) waits until the lanes it crosses or merges into
are clear (Whole junction gap judgement); at a boxed exit it must also have room past the box. On-duty emergency
vehicles as stock; no time limit. Off = 0.4.3.

0.4.3-probe (JR-T-007c; PROBE-STAGE4-BOXTOOL.md section 12): building boxes sit at each **car exit** (driveway,
car park / depot gate; fallback the road access), **one box per exit**; a service building's own access road meeting a
public road is a selectable junction (building arm yields unless signed). Old entrance boxes migrate to the nearest
exit on load. Carries the installed 0.4.2 (JR-T-010, cars obey yellow boxes), whose driving rule now reads one band
per boxed exit (section 14), and first installs JR-T-007b's straight-sided boxes (below; never installed as 0.4.2).

JR-T-007b (built as 0.4.2, never installed; ships in 0.4.3; PROBE-STAGE4-BOXTOOL.md section 11): **straight-sided yellow boxes** (junction and
entrance): mouths slide forward along their arm until the straight sides stay on the carriageway; priority arms keep
their mouths, give-way / STOP arms slide, signals = all equal (Core/StraightBox.cs). Boxes keep **0.5 m from the
painted edge** of zebras and stop / give-way lines (setting **Yellow box: gap to road markings**, paint list
`"markingMargin"`). The tool hover is a thin outline like Traffic's Priorities tool (white = adds, orange = removes).
Paint and tool only: cars drive as in 0.4.1.

Stage 4 probe (0.4.0-probe, JR-T-007; PROBE-STAGE4-BOXTOOL.md): the **yellow box tool** (toolbar button top left,
Ctrl+Y, rebindable; Options > Yellow boxes). Click an at-grade road junction (3+ road arms; not highways, bridges,
tunnels, elevated roads, roundabouts, level crossings, assets' own nodes) or a building's road access to add / remove a
box; it is painted at once and saved with the city as a component on the node / building (Boxes/BoxComponents.cs,
Core/BoxRecord.cs; a save loads cleanly without the mod, the box is skipped). Entrance box: kerb to kerb across the
road, 8 m along it at the access. Bulldozing the node / building drops the box; an in-place node update keeps it.
Paint only: no yellow-box driving rule yet. Also fixes the T-junction through-side kerb (one kerb, one inset).

Stage 3 probe (0.3.0-probe, 0.3.1-probe): a **yellow box paint test**, visual only and default off. With **Yellow box paint test** on,
the mod draws a UK yellow box (TSRGD diagram 1043; TSM chapter 5 dimensions: 200 mm border, 150 mm diagonals, 2 m /
2.5 m spacing) on the junctions in `ModsData/JunctionRules/paint-test.json`, with the game's own surface shader and its
own buffers (option E of `docs/STAGE3-PAINT-RESEARCH.md`). Draw only: nothing goes into the city or the save, driving is
unchanged. Off = the system does not run. Any failure logs one line and switches the test off for the session.
0.3.6-probe (no car-driving change; PROBE-STAGE3-PAINT.md section 15): settings **People pass a car that blocks them** (default on; off = the game's behaviour) and **People pass a car that blocks a crossing after (s)** (default 2 s): a person held on a crossing by a vehicle standing across it walks on through it after the wait, a cyclist rides through at walking pace (own system after the game's HumanNavigationSystem, no game code copied; timing log `people passed blocking cars N (cyclists C)`). 0.3.5-probe (paint test only, no driving change; PROBE-STAGE3-PAINT.md section 14): the box is on the carriageway only (kerb to kerb from the edge road data, round the kerb corners, from the stop line or the far side of a zebra), and paint onto defaults to Roads (a saved Roads + Other is moved back once). 0.3.4-probe (paint test only, no driving change; PROBE-STAGE3-PAINT.md section 13): the paint decal should now show (cause found by reading the shader, live check pending). The old default `"edgeNoise": [0, 0]` made the game's surface shader divide by zero, so every paint pixel was transparent; the paint list now defaults to `[0, 1]` and clamps to the game's range. 0.3.3-probe (Whole junction only, no new setting; PROBE-STAGE3-PAINT.md section 12): no feeding-lane scan for a conflicting lane the game marks unsafe (fixes a false give-way at Moonflower St x Anchor St), and pedestrian / cyclist crossings are no longer part of the decision at the waiting spot: a crossing further along the path is checked by the game's own pedestrian check at the crossing, when the whole vehicle fits before it (else as 0.3.2). 0.3.2-probe adds the driving setting **Cars never push through people on crossings** (default on: no 5 s creep through people on zebras and crosswalks; PROBE-STAGE3-PAINT.md section 11). In-game test: PROBE-STAGE3-PAINT.md. 0.3.1-probe adds opt-in diagnostic switches (a `"diag"` object in the same
file: draw path, decal layer mask, test square, forced alpha, ...) because 0.3.0 drew nothing visible; see
PROBE-STAGE3-PAINT.md section 10.

## Layout

| Path | What |
|---|---|
| `JunctionRules/Vanilla/` | Copied game code (generated, do not edit by hand) plus stage 2 code only inside `// JR-STAGE2 BEGIN/END` blocks. Each file has a header with its source. |
| `JunctionRules/Core/` | Game-independent logic: version gate, driving switch, timing window, IL hasher, whole-junction rule arithmetic (`WholePathRule`, `StretchRule`), 0.2.3 arrival time / projection / safety-stop events / histograms (`ArrivalTime.cs`), stage 2 handoff and counter texts. `VehicleLength.cs` (0.2.2) is unused since 0.2.3. Unit-tested. |
| `JunctionRules/Stage2/` | Job side of the whole-junction rule (`JRWholeJunction`: spots, phases, arrival-time projection, feeding-lane scan) and the copy's main-thread stage 2 state (`JRStage2Bridge`: counters, safety-event table); named only by the copy and the copy's timing probe. `JRVehicleLength.cs` (0.2.2) is unused since 0.2.3. |
| `JunctionRules/Paint/` | Stage 3 paint test (draw-only): `YellowBoxPaintSystem` (list, read-only node lookup, edit tracking, cost meter, draw hook), `PaintGpu` (own material, mesh, buffers), `PaintRegistration` (the only place outside `Paint/` naming them). Pure geometry and list parsing are in `Core/BoxPaintGeometry.cs` and `Core/PaintTestConfig.cs` (unit-tested). |
| `JunctionRules/Systems/` | `CarDrivingSwitchSystem` (chooses the pair each step), `CopyPair` (the only place that registers/creates the copy), navigation timing probes. |
| `JunctionRules/Settings/` | Options page and English texts. |
| `JunctionRules/Mod.cs` | Version gate and system registration. |
| `tools/vanilla_copy.py`, `tools/vanilla_rules.json` | Makes the copy from the decompile and proves it unchanged. |
| `tools/IlHash/` | Prints the vanilla code hash used by the version gate; `--copy` compares the copy's IL with Game.dll. |
| `tests/JunctionRules.Tests/` | Console test runner. |
| `DEVIATIONS.md`, `REFERENCERS.md`, `PROBE-STAGE1.md`, `PROBE-STAGE2.md`, `PROBE-STAGE3-PAINT.md` | Every change to the game code; who uses the copied types; in-game test kits. |
| `docs/STAGE2-ANALYSIS.md` | Where the game decides conflict by conflict, the change, per-case table, deadlock/starvation, owner rulings. `docs/vanilla_copy_*_0.2.0.txt` ... `*_0.3.2.txt`: the diff script's output per version. |

## Settings

- **Car driving**: `Game's own` (default) or `Junction Rules copy`. Applies at the next simulation step. The copy only
  runs if the version gate passed at game start.
- **Junction gap rule** (stage 2): `Stock (conflict by conflict)` (default) or `Whole junction`. Acts only while
  Car driving is the copy; applies at the next simulation step (the switch system publishes it each step, the copy
  takes it by value into its job when it schedules).
- **Whole junction: waiting spot**: `Before the first crossing` (default, owner ruling) or `At the stop line`.
- **Whole junction: turning across oncoming traffic gives way** (0.2.4): on (default) or off (= 0.2.3). Acts only
  with the Whole junction rule; applies at the next simulation step; traffic side from the game's own city setting.
- **Vehicles leaving buildings give way** (0.4.4, lot exits 0.4.5, judged earlier 0.4.6): on (default) or off (= 0.4.3). "Cars coming out of ANY building
  must give way, and when that building exit is a yellow box, must ALSO obey the yellow box rules." Timing log:
  building exits: waits (turning across, spawn/gate), go decisions, emergency passthroughs, cap hits, hand-overs to the road (lot, gate, other); since 0.4.6 counted per vehicle, not per car-step.
  0.4.11 (G21): also a vehicle turning INTO a building across traffic: it waits in its own lane before the yellow box (no box:
  where it would leave its lane) until its whole length can clear the lanes it crosses (PROBE section 21).
- **Exit trace (log)** (0.4.7, debug; 0.4.8 probe: default ON and compact, switched on once on first load): one line when a vehicle starts waiting at a building / parking
  exit and one when it joins the road (decision, path kind, boxed), at most 20 lines a second. Needs Vehicles leaving
  buildings give way. 0.4.7 timing log adds hand-overs by path (spawn point, building parking, kerbside parking,
  building road, drive), hand-overs not judged, cap hits by kind, and "right-turn waits for exit room" (G3).
- **Watch the junction at camera centre (log)** / **Stop watching the junction** (0.4.7, debug buttons): logs every
  standing car at one junction (blocker, rule), rate-limited; no driving effect; nothing saved.
- **Hide road markings under yellow boxes** (0.4.7, Yellow boxes, default off): render-only cut of the road's own
  markings inside every painted box; taken off before every save.
- **No parking in yellow boxes** (0.4.7, Yellow boxes, default on): kerbside parking slots under boxes closed at
  runtime; the game's own free space is put back before every save.
- **Cars never stop on a zebra** (0.4.1): on (default) or off (= 0.4.0); **room margin (m)** 0-10 m, default 1 m
  (extra free space past a zebra on top of the vehicle's length). Acts while Car driving is the copy, with either gap
  rule (Stock: zebras and room only); applies at the next simulation step. Timing log: zebra entries held (people,
  Cs after, no room past) and merged sections.
  1.4.0 (JR-T-033, owner rule 20 Nov 2026): people on a zebra merged into an earlier spot's section hold the car right
  before that zebra (drive-up) when it is eligible (not a box lane, whole length clear of the zebras behind, can still
  stop before it, room free now past the section); the section's vehicle points are then judged over the drive-up
  window (arrival + people wait estimate, at most 10 s, + clear time). Else the spot as before; why
  `jr_people_wait_zebra`, or `jr_people_wait_spot` with a reason (traffic / exit blocked / yellow box / tail on zebra /
  cannot stop / not a zebra section / stock gap rule). Core `ZebraDriveUp`, job `Stage2/JRZebraDriveUp.cs`. JR-T-035:
  Whole junction gap rule only (Stock: the spot as 1.3.0, reason `stock_gap_rule`); no drive-up when the people cannot
  hold the car (estimated wait 0, nobody standing). 1.4.1 (JR-T-036, owner 20 Nov 2026): a person STANDING on the
  target zebra (or a later zebra of its section; `ExitZebraWait.Person` Stationary, the box exit zebra wait's own test)
  = exit blocked: no drive-up, the spot, `jr_people_wait_spot` reason `standing_on_zebra`. Walkers only: unchanged.
  1.4.0 (JR-T-033 B1, owner 20 Nov 2026): at a building access (`CarLaneFlags.SideConnection`, way in or out) the
  unmarked pavement (no Crosswalk flag; a marked zebra there stays a zebra, JR-T-035) is no zebra (the 0.4.8 G7 hard zebra is gone; S2-29 still stops a leaving car before the pavement for the
  road gap and room) and no crossing of the whole-junction rule; the never-push full stop applies there only to a person
  in the car's way (Core `AccessCrossing.PersonInWay`), not one beside it or held at its edge (that made a mutual wait
  broken only by the 2 s people pass). Why `jr_access_person_in_way`. Timing log: "people standing on a building
  access", "cars held by a person at a building access" (debug counting) and, always, "people passed a car at a
  building access".
- **Yellow box: gap to road markings (m)** (0.4.2): 0-2 m, default 0.5 m, from the painted edge of zebras and stop /
  give-way lines (entrance boxes: also from junction mouths); redraws all boxes at once.
- **Cars obey yellow boxes** (0.4.2): on (default) or off (= 0.4.1); **Yellow boxes: turning across oncoming traffic may wait in the box**
  on (default, UK) or off. Only boxes placed with the yellow box tool; shares the room margin slider (now "Room
  margin past a zebra or yellow box"). Timing log: box entries held, right-turn waits, entrance band holds.
- **Look past the car in front at a box** (1.1.0, Yellow boxes, default on; off = 1.0.1 bit for bit): acts only in a
  yellow box room test (free-now rule). The car judges every vehicle it can see past the box: one whose free-now room is
  short still gives room when it moves away at 5 m/s (about 18 km/h; 3 m/s in 1.1.0) or more (1.2.0, JR-T-027, owner rule 20 Nov: an ordinary moving vehicle
  is expected to carry on and stops only behind the back of a visible standing vehicle / queue or at a red stop line,
  which the scan reads where they are; the road up to its predicted front must lie inside the sight; 1.1.0 credited only
  its guaranteed travel, which is now kept for large vehicles alone); any vehicle the car follows through the box below
  5 m/s refuses. Sight rule: a 100 m straight-line radius around the driver (50 m until 1.2.0; on a turn it reaches into the road turned
  into); vehicles beyond it are not read; nothing stopped or slow within sight (and no red line) = the car goes (1.3.0,
  JR-T-029: the 1.2.0 rule "the room relied on must end inside the sight" is gone). With a stopped / slow vehicle or red
  line seen, the vehicles between are packed nose to tail behind it, never in another yellow box, on a zebra (the soft
  zebra excepted) or in another junction interior (`RoomScan.NoStopZone` / `PackedRoom` / `PackedReach`; refusals
  queue_pushed_back_by_zebra / box / junction). Ordinary cars and vans never block the view. A
  large vehicle (the game's `CarData.m_SizeClass` Large: lorries, buses, artics) ends the view: it is seen and credited
  itself, with its guaranteed travel less 5 m (`RoomScan.LookPastLargeMargin`: room for a car standing unseen ahead of
  it), the vehicles beyond it are not read at all (owner ruling 19 Nov: neither for nor against), and the room relied on
  must end by its front. A large vehicle queued ahead in or before the box (owner call 19 Nov, JR-T-022f) switches the
  look-past off for that decision: the car judges as with the setting off (room free now past the box), no hold of its
  own, so the setting never refuses what off allows.
  Buildings block the view (owner ruling 19 Nov): before the look-past gives the room, the straight 2D line from the
  driver to each vehicle it credited (until 1.2.0 also to the end of the room relied on) is tested against the footprints of the
  buildings near that line (stock static object search tree, cells and items pruned by the line itself; no stored
  data). Footprint = the prefab's bounds placed by the building's Transform: the lot box for ordinary buildings, the mesh
  bounds for standing ones; a footprint that contains the driver does not block (a car leaving its own building or in a
  car park lot stands inside its lot box). A crossed footprint withdraws the look-past credit. This replaced the interim
  "next junction ends the sight" cut (kept only when the driver's position is unknown). An artic is one vehicle
  (trailer and tractor share a controller): credited by its leading element, its elements never hide each other (1.1.0
  review fix). Lights are always seen. The decision runs again every step while the car can still stop at the line, so the safety stop stays; rare misjudgements that end with a car in the box (since 1.2.0 also a queue past it that closes after the car could no
  longer stop; measured offline, rises clearly) are accepted by design. Mode bit 31 (`Stage2Mode.BoxLookPast`, stripped from
  the job's stage 2 mode, toggling it logs a rule switch). Timing log: "let into a box by looking past the car in front"
  (`Stage2Counters.BoxLookPastAdmitted`), printed only when the setting was on in the window.
  JR-T-024 (owner ruling 19 Nov 2026), same setting: a car about to cross a junction box whose exit zebra has people on
  it may drive in and wait just before that zebra (the game's own stop, never on it) when everyone there is walking and
  the road past the box has room free now for its whole length plus margin and it fits between the entry zebra (or the
  line) and the stop point before the exit zebra; one person standing on the zebra, or no room
  free now, holds it at the box line (before: people walking or standing held it at the line under the Whole junction
  rule, or let it in to wait before the zebra under the Stock gap rule). "Walking" = the game's own test (`Moving`,
  speed^2 >= 0.01); people waiting at the kerb are not on the zebra (G23). Core `ExitZebraWait`, job
  `Stage2/JRExitZebraWait.cs`. Timing log: "waiting in a box just before its exit zebra while people cross"
  (`BoxExitZebraWait`) and "held at a box line for people standing on its exit zebra" (`BoxExitZebraLine`).
  JR-T-024b: "standing" counts only a person on the car's way over the zebra or beside it (the overlap's range on the
  pedestrian lane + 1.5 m: Core `ExitZebraWait.OnCarsWay`); walking people anywhere; cyclists are not people (as the
  game's own pedestrian check); the zebra-wait counter needs the car's own blocker to be a person on that zebra.
- **Yellow box paint test** (stage 3 probe): off (default) or on; **what to draw** (Box / Plain fill / Outline only);
  **paint onto** (Roads (default) / Roads + Other / Everything but vehicles); buttons Refresh test list, Add junction at
  camera centre, Log paint cost now; Paint test status. See PROBE-STAGE3-PAINT.md.
- **Yellow boxes** (0.4.0): **Yellow box tool shortcut** (default Ctrl+Y) and **Open / close the yellow box tool**.
  Saved boxes are painted with the paint test's mode and layer settings whether or not the paint test is on.
- **Status**: what runs now, the version check result, and whether the copy can run.
- **Log navigation timing** (debug): one line per 1024 simulation frames with the navigation job timing of the pair
  that ran; the copy's line also names the junction gap rule and the stage 2 counters (0.2.3: whole-path decisions,
  waits at the entry spot for a later conflict, safety stops inside with their EVENT count (one per car transition),
  stopped inside for a give-way / behind a crossing car, "busy only by projection" split junction-lane / feeding-lane
  car, and for points at W the distance from W and the own arrival time as mean + histogram with the median bucket); 0.2.4: feeding scans with follower / memo skips and cap hits, turning-across-oncoming priority changes and
  turner reservations not waited for). Off: the probe systems are
  disabled and the counters are not counted, so they cost nothing.

When the version gate does not allow the copy, the copy pair and the timing probes are not registered at all (their
types are only named inside `Systems/CopyPair.cs`, behind NoInlining methods), so after a game patch the mod's own code
never loads a copy type (checked offline by the CopyIsolationTests); the switch then only keeps the game's own pair
running. Not proven: whether the game itself, reflecting over every type of the mod assembly at load (e.g. when it
collects systems or Burst code), trips over a copy type that no longer matches a patched game. That has not been
tested on a patched game.
The switch gets the copy systems in its first simulation update, not when it is created (the game creates it during
registration). The options **Status** line says `Copy: available`, `not registered, cannot run` or
`creation failed, cannot run`, so it never reads as runnable when the copy cannot run.
If registering or creating the copy fails partway, every copy or probe system that already exists is switched off
(the game creates a system, enabled, when it is registered) and the game's own pair keeps running; the log says how many.
- **Skip code hash check (probe aid)**: lets the copy run on the right game version when only the code hash differs.
  Read at game start.

Mod log: `LocalLow/Colossal Order/Cities Skylines II/Logs/JunctionRules.Mod.log`.

## Build

Requires the .NET SDK and an installed game. Here the SDK lives in `~/.local/opt/dotnet`.

```
cd JunctionRules
PATH=~/.local/opt/dotnet:$PATH dotnet build -c Release -p:SkipDeploy=true
```

Burst build (needs the CS2 modding toolchain env file). **Game closed only**: the official post-processor
(`ModPostProcessor.sh`) runs Proton Experimental's wine inside the game's own Proton prefix (`compatdata/949230`), which
must not happen while the game runs there under Proton 11.0. The build checks `ps -eo comm | grep -i cities2` first and fails
with a clear message while the game runs (override at your own risk: `-p:AllowBurstWhileGameRuns=true`). A separate
scratch wine prefix is not used: the generated script hard-codes the game's prefix and the post-processor needs the
Windows .NET runtime installed in its prefix.

```
cd JunctionRules
set -a; . ~/.config/environment.d/60-cs2-modding.conf; set +a
PATH=~/.local/opt/dotnet:$PATH dotnet build -c Release -p:Burst=true -p:SkipDeploy=true -o <out-dir>
```

It writes `JunctionRules.dll` and `JunctionRules_win_x86_64.dll`. The build never copies into the game's Mods folder
unless `-p:Deploy=true` is given. The game folder comes from `CS2_PATH`, `CSII_INSTALLATIONPATH` or the default Steam
path; `-p:GamePath=...` overrides it.

## Tests and checks

```
PATH=~/.local/opt/dotnet:$PATH DOTNET_ROOT=~/.local/opt/dotnet dotnet run -c Release --project tests/JunctionRules.Tests
python3 tools/vanilla_copy.py check        # copy outside JR-STAGE2 blocks == decompile + documented rules; 0 unexplained differences
python3 tools/vanilla_copy.py selftest     # the diff script's own tests
cd tools/IlHash && PATH=~/.local/opt/dotnet:$PATH DOTNET_ROOT=~/.local/opt/dotnet dotnet run -c Release                       # vanilla code hash
cd tools/IlHash && PATH=~/.local/opt/dotnet:$PATH DOTNET_ROOT=~/.local/opt/dotnet dotnet run -c Release -- --copy ../../JunctionRules/bin/Release/JunctionRules.dll
```

`vanilla_copy.py` defaults to the decompile in `~/Code/decomp/yb-2026-11-13/full`; `--decomp DIR` changes it.
The test runner takes group-name filters and `--list`.

## After a game patch

See DEVIATIONS.md, "Updating after a game patch". Until the copy is re-made, the version gate keeps the game's own
system running on the new version.
