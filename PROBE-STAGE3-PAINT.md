# Probe kit: Junction Rules 0.3.6-probe (stage 3, yellow box paint test, option E; plus the crossing settings)

**0.3.6-probe (JR-T-008), what changed since 0.3.5:** one crowd change, nothing in the paint test or in car driving.
New settings **People pass a car that blocks them** (on/off, default **on**; off = exactly the game's behaviour, for
A/B tests) and **People pass a car that blocks a crossing after (s)** (slider right below it, default **2** seconds,
greyed out while the switch is off): a person held on a zebra or junction crosswalk by a car (or bus, tram, truck) standing
across it walks on through the vehicle after that wait; a cyclist riding the crossing rides on through it at walking
pace. Cars still never push through people (0.3.2 setting unchanged). Works with either **Car driving** setting. What
to watch: section 15. Car driving is 0.3.5's as it is (`docs/vanilla_copy_check_0.3.6.txt` equals 0.3.5's).

**0.3.5-probe (JR-T-003e), what changed since 0.3.4:** paint test only, no driving change. (1) The box is now on the
**carriageway only**: kerb to kerb, following the kerb corner curves, from each arm's stop / give-way line, or from the
far side of a zebra on that arm. 0.3.4 boxed the whole node outline, pavements included: the footway step read the
junction's own (node) road data, which never marks the road-lane pieces, so it always found no footway ("no road-lane
pieces: kerb inset 0"). It now reads the arm's edge road data (section 14). (2) **paint onto** defaults to **Roads**, and
a saved Roads + Other (left over from the old step S6 advice) is moved back to Roads once; Roads + Other stays
selectable. (3) Kerb curves are kept within 5 cm (the point thinning no longer adds up along a curve). What to look at
live: section 14. Driving is 0.3.4's as it is (`docs/vanilla_copy_check_0.3.5.txt` equals 0.3.4's).

**0.3.4-probe (JR-T-003d), what changed since 0.3.3:** the paint test's decal should now draw (cause found by reading the shader; not yet seen live). Cause of "nothing visible" in
0.3.0-0.3.3: the paint list's default `edgeNoise` was `[0, 0]`; the game's surface shader divides by the second value, so
every paint pixel came out fully transparent (section 13). 0.3.4 never hands the shader a zero there (default `[0, 1]`:
clean edges). No driving change: it carries 0.3.3 as it is (`docs/vanilla_copy_check_0.3.4.txt` equals 0.3.3's, copy IL
identical). Short live test: section 13, one restart.


**0.3.3-probe (JR-T-005 + JR-T-006), what changed since 0.3.2:** two driving changes in the **Whole junction** rule,
nothing in the paint test, no new setting. (1) Moonflower Street x Anchor Street: the car turning left out of
Moonflower no longer waits for a car on Anchor that is turning right into Moonflower (their paths never meet; the mod
had counted that car as a possible U-turner). (2) Pedestrian and cyclist crossings are no longer part of the decision
at the waiting spot: a car decides there on the other cars only, and a crossing further along its path is checked by the
game's own pedestrian check when the car gets to it. A clear crossing is driven straight through (no stop, no slowing);
a busy one is waited for just before it. What to watch: section 12. It carries everything in 0.3.2.

**0.3.2-probe (JR-T-004), what changed since 0.3.1:** one driving change, nothing in the paint test. New setting
**Cars never push through people on crossings** (Options > Junction Rules, driving group, **on** by default): a car that
waits for people on a zebra or a junction crosswalk keeps waiting until the crossing is clear. No timeout (owner ruling:
no starvation safeguard). Before, the game let it creep through at walking pace after 5 s. Car against car is exactly
as before. What to watch: section 11. The paint test below is unchanged from 0.3.1.

**0.3.1-probe (JR-T-003c), what changed since 0.3.0:** in 0.3.0 the boxes were built (80 hatch / 11 plain triangles),
the draw ran every frame without errors, and nothing showed in any mode. A static check of the game code and of the
shader itself found no certain cause (section 10 has the evidence and the ranked suspects). So 0.3.1 adds **opt-in
diagnostic switches** (a `"diag"` object in `paint-test.json`, section 10) and more log lines, so that **one game
restart** settles it: edit the file, press **Refresh test list**, look, and go on to the next switch. Without a `"diag"` object
the draw is the same as 0.3.0 except: the material buffers are set before each draw (as the game does), the DBufferMesh
pass is checked and switched on if it is off, and `volumeBelow`/`volumeAbove` may go up to 200 m. Driving is unchanged.

0.3.0-probe adds a **visual test only**: Junction Rules draws a UK yellow box (TSRGD diagram 1043) on junctions you
list, with the game's own surface shader (option E of `docs/STAGE3-PAINT-RESEARCH.md`). It answers one question:
does option E look right and cost little? (Probe steps S6-S11 of the research doc, section 8.)

- **Off by default.** Setting **Yellow box paint test** = off: the paint system does not run at all.
- **Draw only.** Nothing is written to the city or the save: no entities, no components, no areas, no nets. The boxes
  exist only on screen. The only file it writes is the test list (when you press the add button).
- **No change to driving from the paint test.** (0.3.2 adds only the crossing setting, section 11.) Up to 0.3.1 the navigation copy and stage 2 were exactly as 0.2.4 (`docs/vanilla_copy_check_0.3.1.txt`
  equals the 0.3.0 and 0.2.4 files; the copy's IL in the 0.3.1 dll equals 0.3.0's).
- **Fail safe.** If anything in the paint test fails, it logs one `FAILED` line and switches itself off until the game
  restarts. The rest of the mod keeps working.

Build to install: `~/Code/cs2-junction-rules/.staged/0.3.6-probe/`. If that folder holds a `STALE.md`, it has only a
managed build (made while the game was running): main does the Burst rebuild in `STALE.md` (game closed) when the owner
quits, then puts the new sha256s in the table and the install block below. Until then the install block refuses (the
sums below are placeholders on purpose: `0000...`). 0.3.6-probe was Burst-built from commit 784ba28 and installed by main on 15 Nov. 0.3.5-probe was Burst-built from commit bb28fef and installed by main on 15 Nov (dll `3af93435...`, Burst dll `a3949df1...`; the rollback, in `.staged/0.3.5-probe`). 0.3.4-probe was Burst-built from commit 559ae78 and installed by main on 15 Nov (dll `405b5772...`, Burst dll `d3f10173...`; the rollback, in `.staged/0.3.4-probe`); 0.3.3 was never
Burst-built or installed, 0.3.4 carries it.
0.3.2-probe was Burst-built from commit 319fac8 and installed by main on 15 Nov (dll `4043fcc9...`, Burst dll `5eb55ab6...`).
(0.3.0-probe was Burst-built from commit 82ec0ce and installed by main on 15 Nov.)

| File | sha256 |
|---|---|
| `JunctionRules.dll` | `f4aad4bc74c80bf7cd32dd100e1ee576ea19aa1f63762693fd2822173ddda967` |
| `JunctionRules.pdb` | `ec98366ae727bcf12cd0c9f09dc97cfdbc30dea5e2f1587828d10d57b34f0a48` |
| `JunctionRules_win_x86_64.dll` (Burst code) | `ae3b2b06dbb9a9043aa39d6335443cb44166745ee6e2bb8d3560237bebea2a1b` |
| `JunctionRules_win_x86_64.pdb` | `332454f7a88ac09823c165e5b04cbb859ee6763251a72785d010a862dd105e61` |

Built for game **1.6.4f1 (1097.158a)** only (same version gate as before: on another game version the paint test does not start, and the status says so).

## 1. Install over 0.3.5-probe / 0.3.4-probe / 0.3.3-probe / 0.3.2-probe / 0.3.1-probe / 0.3.0-probe / 0.2.4 (game closed only; the 0.2.4 backup made by the 0.3.0 install is kept as it is)

```
(
  CS2="$HOME/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
  BACKUP="$HOME/Code/cs2-junction-rules/.staged/0.2.4-installed-backup"
  cd ~/Code/cs2-junction-rules/.staged/0.3.6-probe || exit 1
  if [ "$(ps -eo comm | grep -ci cities2)" != 0 ]; then echo "GAME IS RUNNING - stop, nothing installed"; exit 1; fi
  if [ -e STALE.md ]; then echo "STAGED BUILD IS STALE - see STALE.md, rebuild first; nothing installed"; exit 1; fi
  if [ ! -d "$CS2" ]; then echo "CS2 folder not found - nothing installed"; exit 1; fi
  sha256sum -c --strict - <<'SUMS' || { echo "SHA256 MISMATCH - nothing installed"; exit 1; }
f4aad4bc74c80bf7cd32dd100e1ee576ea19aa1f63762693fd2822173ddda967  JunctionRules.dll
ec98366ae727bcf12cd0c9f09dc97cfdbc30dea5e2f1587828d10d57b34f0a48  JunctionRules.pdb
ae3b2b06dbb9a9043aa39d6335443cb44166745ee6e2bb8d3560237bebea2a1b  JunctionRules_win_x86_64.dll
332454f7a88ac09823c165e5b04cbb859ee6763251a72785d010a862dd105e61  JunctionRules_win_x86_64.pdb
SUMS
  if [ -d "$CS2/Mods/JunctionRules" ] && [ ! -e "$BACKUP/JunctionRules.dll" ]; then
    mkdir -p "$BACKUP" && cp -p "$CS2/Mods/JunctionRules/"* "$BACKUP/" && echo "backed up the installed files to $BACKUP" || { echo "BACKUP FAILED - nothing installed"; exit 1; }
  fi
  mkdir -p "$CS2/Mods/JunctionRules" &&
  cp JunctionRules.dll JunctionRules.pdb JunctionRules_win_x86_64.dll JunctionRules_win_x86_64.pdb "$CS2/Mods/JunctionRules/" &&
  echo "installed 0.3.6-probe"
)
```

Paste the whole block. It copies nothing unless the game is closed, the build is not stale and all four sums match.

## 2. Log checks

`$CS2/Logs/JunctionRules.Mod.log` after the main menu:
- `OnLoad version 0.3.6.0, ...`; the settings line names `people pass a car that blocks a crossing after 2.0 s`
- first start of 0.3.5 only: `settings migrated (schema 0 -> 1): yellow box paint onto RoadsAndOther -> Roads ...` (or
  `settings schema 0 -> 1: ... kept` when it was not Roads + Other)
- `settings: ... cars never push through people on crossings on, ... yellow box paint test off, ...` (defaults)

Options > Junction Rules has a new group **Yellow box paint test (stage 3 probe)**:
- **Yellow box paint test** (on/off)
- **what to draw**: Box (border + criss-cross) / Plain fill / Outline only
- **paint onto**: Roads / Roads + Other / Everything but vehicles (G87 value)
- buttons **Refresh test list**, **Add junction at camera centre**, **Log paint cost now**
- **Paint test status** (off / on with N junctions / failed: what)

## 3. The test list (which junctions get a box)

The list file is
`$CS2/ModsData/JunctionRules/paint-test.json`. The mod only reads it (and adds to it when you press the button).

**Easiest way:** switch the test on, point the camera centre at a junction, press **Add junction at camera centre**.
It picks the junction node (3 or more road arms) within 8 m of the screen centre line that is nearest to the camera (so an elevated
junction wins over the road below it). The log says `added node at (x, y, z) to ...`.

**By hand:** write the file, then press **Refresh test list**. Positions are game x/z in metres (y only to choose
between stacked nodes; a node within 6 m in plan is taken):

```
{
  "junctions": [
    { "name": "Valley x Carlisle", "x": 1092.7, "z": -81.5 },
    { "name": "deck junction", "x": 500.0, "y": 812.0, "z": 300.0 }
  ],
  // optional; every dimension is a parameter (defaults shown)
  "paint": {
    "borderWidth": 0.20, "lineWidth": 0.15, "spacing": 0, "angle": 45,
    "kerbInset": 0, "color": [1.0, 0.78, 0.05, 1.0],
    "volumeBelow": 1.0, "volumeAbove": 1.0, "logIntervalSeconds": 30
  }
}
```

`spacing` 0 = TSM rule (2 m if the shortest box side is under 9 m, else 2.5 m). `kerbInset` moves the box edge in
from the kerb (metres). `volumeBelow`/`volumeAbove`: how far below/above the road the paint reaches (S10).
Other keys (for the shader tests): `expand`, `smoothness`, `metallic`, `normalOpacity`, `edgeFade` [2], `edgeNormal`,
`edgeNoise` [2], `uvScale`, `plainTextures`, `swapPrevNext`, `snapRadius`, `mitreLimit`, `straightenTolerance`,
`maxLinesPerFamily`. Bad values are clamped and logged. 0.3.1: an optional `"diag"` object holds the diagnostic
switches (section 10); the add button keeps the ones you set.

## 4. The steps

Do S6 first. **If S6 is NO-GO, stop**: option E is dropped (the research doc's fallback is option A at grade + B).

| Step | Do | Look at | Decides |
|---|---|---|---|
| S6 | List 4 junctions: an at-grade cross, an elevated junction, a junction over a tunnel, a roundabout node. Test on, Box mode. | Does it look like paint (day, night, rain, zoomed out: do the 0.15 m lines shimmer)? Deck painted and the ground under it clean? Cars drive over it, not painted? Frame rate change? If nothing shows: try **Plain fill**, then **paint onto** = Roads + Other. | **GO/NO-GO for E.** Shader inputs (U2, U9), look (U3), decal layers (U4), hatch alignment (U8) |
| S7 | Skewed junction (arms about 60 deg). Try **Outline only** too. | Outline and box follow the visible kerbs and mouths? Box not on the footway? | Outline merge and footway inset (U7) |
| S8 | T junction at an angle, and a Y. | Box stops at each mouth, does not run up the arms? Hatch clipped clean at the edge? | Hatch layout rule |
| S9 | Divided-road junction. Then: upgrade one arm, move the node, add a 5th arm, delete one arm. | Box follows each edit at once? Median noses outside the box? No old pieces left? | Edit handling, re-finding a replaced node |
| S10 | Elevated junction with a ground junction right below it, both listed. | Each box only on its own level? | Volume height (+-1 m), decal layer Roads |
| S11 | Save, quit, turn the test off (or remove the mod), load. | No boxes, no errors, nothing left. | Save safety of E |

For each step note: what you see (screenshots), the per-box log line, and the cost line.

## 5. Log lines to read

All in `$CS2/Logs/JunctionRules.Mod.log`:
- `yellow box paint test ON (draw-only probe; mode Hatch, decal layers Roads = 2); list file ...`
- `yellow box paint test: list ...: N junction(s); border ...` and any `list file: ...` warnings (bad JSON, bad values)
- Per box, on every rebuild: `yellow box paint test: <name>: node ... at (...), 4 arms, outline .. pts / .. m2, box .. x .. m, spacing 2.5 m, pieces .., triangles .., built in .. ms` (plus notes, e.g. the roundabout note)
- Skips: `no junction node within 6 m; skipped`, `N other node(s) stacked within 1 m` (add `"y"`), `node removed ...; box dropped until Refresh`, `no box: <reason>`
- **Cost**, every 30 s while boxes exist, and on **Log paint cost now**:
  `yellow box paint cost over 30.0 s: boxes N, triangles T; draw .. us/frame avg, .. max (n frames); change check .. ; rebuilds n, .. ms avg, .. max`
- **Failure** (once, then the test is off for the session):
  `yellow box paint test FAILED while <step>; painting is off for this session (the rest of Junction Rules is not affected)`
  with the exception. `Paint Test status` then shows `failed: <step>`. Restart the game to try again.
- `shader lacks optional properties ...`: a warning only; report it.

## 6. Switch off and clean up

- **Off:** Options > Junction Rules > **Yellow box paint test** off. The boxes go at once; the system stops running.
- **Remove the list:** delete `$CS2/ModsData/JunctionRules/paint-test.json` (game running or not). While the test is on, press **Refresh test list** afterwards: the list is read when the test is switched on and on Refresh, so the boxes stay until then.
- **Nothing else to clean:** no data goes in the save. Removing the mod or the file leaves no trace.
- Back to 0.2.4 (game closed): copy back `.staged/0.2.4-installed-backup/` (made by the install), or the staged
  0.2.4 Burst build after checking its sums:

  ```
  (
    CS2="$HOME/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
    cd ~/Code/cs2-junction-rules/.staged/0.2.4-burst || exit 1
    if [ ! -d "$CS2/Mods/JunctionRules" ]; then echo "mod folder not found - nothing changed"; exit 1; fi
    if [ "$(ps -eo comm | grep -ci cities2)" != 0 ]; then echo "GAME IS RUNNING - stop, nothing changed"; exit 1; fi
    sha256sum -c --strict - <<'SUMS' || { echo "SHA256 MISMATCH - nothing changed"; exit 1; }
864f8124590d3729a30ab4c8411351a6c6c996f9ae5b5f216294e74a22951228  JunctionRules.dll
92a8828f2d9eef2ef83c3e3709e0d7fc2ceeb95fc78ec255928cf871cc890441  JunctionRules.pdb
7c453e85de1c0d41e976dfa958a1114a25879431d7ac4b802d35fd6ad980e3f9  JunctionRules_win_x86_64.dll
0a29d7b7460828145da7569c8937491f32f9f993af9bb164178e2e0e06a6d6ed  JunctionRules_win_x86_64.pdb
SUMS
    cp JunctionRules.dll JunctionRules.pdb JunctionRules_win_x86_64.dll JunctionRules_win_x86_64.pdb "$CS2/Mods/JunctionRules/" &&
    echo "0.2.4 restored"
  )
  ```

## 7. Box dimensions (source)

Confirmed from the **Traffic Signs Manual chapter 5 (2018)**, section 8 "Box junction markings":
https://assets.publishing.service.gov.uk/media/5c4ace6ded915d38a0611abc/traffic-signs-manual-chapter-05.pdf

- Border line 200 mm (figure 8-1). Diagonal lines 150 mm (8.4.1).
- Diagonal spacing about 2 m when the shortest side of the box is under 9 m, otherwise 2.5 m (8.4.1).
- The two line families at 90 deg to each other is the aim (8.2.4); main diagonal corner to corner of the box, the
  others parallel at the grid interval (8.4.2). Roundabout boxes: diagonals at about 45 deg to the exit line (8.4.4),
  and only at fully signalled roundabouts (8.1.4).
- TSRGD 2016: diagram 1043 is Schedule 9 Part 6 item 25; Schedule 9 Part 8 para 5(1) lets the box shape follow the
  junction.

What the test draws: border 0.20 m, lines 0.15 m, spacing by the TSM rule, families at +-45 deg to the box's long
side (90 deg apart), box limited to the conflict area (inside the kerbs, up to the mouths). Note: the test draws the
lines at a fixed angle to the box's long side, not the exact corner-to-corner main diagonal of 8.4.2; on a long thin
box this differs a little. Say if it matters.

## 8. Still unknown (this probe settles them)

- **U2/U9:** does the surface shader accept our buffers as given (zero colour buffer, all visible indices, `expand`,
  prev/next neighbour order, edge fade on thin 0.15 m strips)? If lines look broken or wedge-shaped, try
  `"swapPrevNext": true`, then `"edgeFade": [0, -1]` (no edge fade; from 0.3.4 an `edgeFade` with x not above y is reset to `[1, 0]`).
- **U3:** does it look like painted G87 (wetness, wear, night, shimmer at distance)?
- **U4:** which decal layer the junction and deck take (Roads, or Roads + Other).
- **U7:** footway inset from the road data matches the visible kerb on all road types. 0.3.5: read from the arm's edge
  road data, side found from the edge's own left kerb (no mirroring assumption); see section 14. Use `kerbInset` to
  test corrections.
- **U8:** texture mapping (`uvScale`, `plainTextures`; with `plainTextures` false the material's own textures are used).
- **Roundabout node:** boxed from the kerbs (0.3.5); the island is not cut out yet.
- **Node replacement:** an edit that replaces the node is followed if the new node is within 2 m; otherwise press
  **Refresh test list**.

## 9. Report back

The log lines of section 2; per step: screenshots, the per-box line, the cost line, any `FAILED` or `list file` line,
and which mode / layer setting worked. S6 verdict first: GO or NO-GO.

## 10. 0.3.1: why nothing showed, and the one-restart switch order

### What the static check found

Sources: the 13 Nov 2026 decompile (`~/Code/decomp/yb-2026-11-13/full`) and the shader itself (AreaDecalShader,
`Cities2_Data/resources.assets` path id 559, DBufferMesh pass, variant `_MATERIAL_AFFECTS_*` + `PROCEDURAL_INSTANCING_ON`,
disassembled from the DXBC). Our material, mesh, buffers and draw call match the game's in every value checked:

| Suspect (brief) | Evidence | Verdict |
|---|---|---|
| Colour buffer all zero | The pixel shader reads `colossal_AreaBatchColors[i].z` only when `colossal_InfoviewOn` = 1; then alpha = z x edge alpha. Outside infoviews it is not read. `AreaColorSystem` gives vanilla surfaces zero too. | Cause only with an infoview on (same as vanilla surfaces) |
| LOD factor / offsetDir | LodDistanceFactor only fades alpha: `max(0, 1 - (d^2 / (lod x colossal_LodParameters.x)^2)^2)`; ours (about 12.7 for a 31 m box) gives several km. OffsetDir only feeds the UVs. | Not the cause |
| Volume (YMinMax, AreaParameters.y) | The vertex shader builds the prism from A/B/C xz and YMinMax only; AreaParameters.y is unused there. +-1 m encloses the road surface. | Not the cause (unless the heights are wrong: the 0.3.1 upload line shows them) |
| Shader enabled / keywords | ENABLE_VT is a local keyword the area materials do not set; `RenderingSystem.IsShaderEnabled` is a debug switch only. The base material ('Rendered Area Material') does not disable DBufferMesh. | Not the cause |
| Decal layer bits | The pixel shader discards unless `(decal-layer prepass value & colossal_DecalLayerMask) != 0`; a prepass value 0 counts as 1 (Terrain). Net compositions write 2 (Roads; `ManagedBatchSystem.cs:888`), so mask 2 is right on paper. | **Top runtime suspect**: a single discard that hides everything if road pixels do not carry 2 |
| Winding | The shader discards a pixel unless it is inside the triangle with (B-A) x (C-A) > 0 in plan, the same as the game's triangles (`GeometrySystem` Snip) and ours (test `diag: every box triangle has the winding the area shader keeps`). | Matches |
| Bounds | Same `RenderingUtils.ToBounds` of the triangle bounds as the game. | Matches |

Ranked suspects for the restart: (1) decal layer discard; (2) the decal draw not reaching the DBuffer at run time (only
a live test shows it: the overlay draw path); (3) wrong triangle heights or positions at run time (upload line, test
square); (4) alpha forced to 0 by a value we have not seen (forced-alpha step); (5) something about the cloned material
(vanilla surface material step).

### New log lines (0.3.1)

- `diag switches (effective): drawPath decal, materialFrom (areas configuration), decalLayerMask (settings), ...` on every list read.
- `decal material from '...' (shader '...', render queue ..., shader queue ...); passes [...], keywords [...]; DBufferMesh pass enabled`
- `upload: N triangles, 0 with non-positive shader winding; bounds ... ; lod factor ..; first: A .. B .. C .., y (min, max), offsetDir ..`
  Check: 0 bad winding, the y values within a metre of the node height in the per-box line.
- `first draw: N triangles, bounds centre .. size ..; cameras ['Main Camera' Game, ...]; decal draws n, overlay draws m`
  Check: a Game camera is listed and the decal draws count is above 0.

### The switches (all optional, in a top-level `"diag"` object; defaults = normal draw)

| Key | Default | Does |
|---|---|---|
| `drawPath` | `"decal"` | `"overlay"`: draw with the area tool's overlay shader (AreaShader of the Surface area type, not a decal; like the colour shown while drawing a surface); `"both"` |
| `materialFrom` | `""` | name of an area prefab whose own material is cloned instead of the areas configuration's (a wrong name logs the list of names) |
| `decalLayerMask` | `-1` (settings) | the mask as a number: 255 = every layer, 1 Terrain, 2 Roads, 4 Buildings, 32 Other |
| `colorBuffer` | `[0, 0, 0]` | the per-triangle colour; `[0, 0, 1]` keeps the paint visible with an infoview on |
| `lodFactor` | `0` (computed) | LodDistanceFactor; `1e6` = never fades |
| `flipWinding` | `false` | clockwise triangles; a control: should show **nothing** |
| `offsetDir` | box | `[x, z, dirX, dirZ]` UV origin and direction |
| `testSquare` | `0` | metres: one plain square of that side on each listed node, at node height, instead of the box |
| `setBuffersEachFrame` | `true` | bind the buffers before every draw (game does) |
| `rendererPriority` | `0` | RenderParams priority |
| `renderQueue` | `-1` (shader's) | material queue, e.g. 1904 (the base material's own queue) |
| `cameras` | `"nonPreview"` | `"game"`: Game and SceneView cameras only |

Related `"paint"` keys: `volumeBelow` / `volumeAbove` up to 200; `expand` and `edgeFade` (`"expand": 0.5, "edgeFade": [0, -1]` gives full alpha everywhere inside each triangle).

### Order for the one restart

Setup: install 0.3.2 (it carries the 0.3.1 switches), start the game, load the city, Options > Junction Rules: test **on**, **Plain fill**, list one
at-grade junction (the 0.3.0 one is fine). Daylight, **no infoview**, camera 100-200 m away, looking down at it. After
each edit of `paint-test.json` press **Refresh test list**, wait a second, take a screenshot and copy the new log lines.
Keep the junction list; replace only the `"diag"` / `"paint"` parts as shown. Stop at the first step that shows paint
and report that step: it names the cause.

| Step | `paint-test.json` (besides `"junctions"`) | If paint shows | If nothing shows |
|---|---|---|---|
| D0 | no `"diag"` (defaults) | 0.3.1's buffer binding / pass check fixed it | read the 4 log lines above, go on |
| D1 | `"diag": { "testSquare": 20 }, "paint": { "volumeBelow": 50, "volumeAbove": 50 }` | yellow 20 m square on the node: the box geometry or heights were the cause | go on |
| D2 | D1 + `"decalLayerMask": 255` in `"diag"` | **decal layer** was the cause: try 1, then 32, then 4 to find which layer the road carries | go on |
| D3 | D2 + `"colorBuffer": [0, 0, 1], "lodFactor": 1e6` in `"diag"`, `"expand": 0.5, "edgeFade": [0, -1]` in `"paint"` | an alpha factor was the cause: remove them one at a time | go on |
| D4 | D3 with `"drawPath": "overlay"` | square visible as an area-tool style overlay: the triangles and draw call reach the screen, so the **decal path** is the cause (report D0-D3 lines) | the draw itself or the triangle positions are wrong: report the upload and first-draw lines |
| D5 | D3 with `"materialFrom": "?"` (logs the area prefab names), then a vanilla surface name from that list | the cloned configuration material was the cause | go on |
| D6 | D3 with `"renderQueue": 1904, "rendererPriority": 100, "cameras": "game"` | queue / priority / camera filter was the cause | report all lines: no cause found by switches |
| D7 | (control, after a step that showed paint) that step + `"flipWinding": true` | winding convention is the wrong way round (report it) | expected: nothing (the shader keeps CCW only) |

Then remove the `"diag"` object, press Refresh, and carry on with S6-S11 using whatever setting worked.

## 11. 0.3.2: cars never push through people on crossings

**The setting:** Options > Junction Rules > **Cars never push through people on crossings**, on by default. It acts
while **Car driving** is on (the Junction Rules copy drives), with either junction gap rule. Off = the game's behaviour.

**Why cars drove through people before:** the game gives up waiting in two ways (both in CarLaneSpeedIterator.CheckPedestrian):
- **5 s push:** once a car has waited 5 s for people, the game lets it creep at about 3 m/s (11 km/h) through anyone
  still on the crossing. This is "waits a few seconds, then drives through the pedestrians". It happened at mid-block
  zebras and at junction crosswalks. At junctions it also overrode the whole-junction rule's wait at the spot.
- **Standing person:** someone standing still on the crossing (or a collapsed accident victim) is crept through at once.
With the setting on, both become a full stop. Cyclists riding a crossing are also never crept through.

**What to watch** (game speed 1, close camera, timing log on if you want the counter):

| Where | Look for | Good | Bad |
|---|---|---|---|
| Busy mid-block zebra | a car stopped at the zebra while people keep crossing, for longer than 5 s | it stays stopped until the zebra is clear, then goes | it rolls through people after a few seconds |
| Busy junction crosswalk (turning car) | a car turning into a road whose crosswalk is busy | waits at the stop spot before the junction (whole-junction rule) or at the crosswalk (stock rule), then goes when clear | creeps into the crossing people |
| Same junctions, setting **off** | the same scene | old behaviour returns (creep after about 5 s) | |
| Car against car | queues, gap taking at junctions | exactly as 0.3.1 | any change |

Expect longer waits and more queueing behind busy crossings (no timeout on purpose). Look out for a car stuck for
minutes behind a person who never leaves the crossing (a collapsed accident victim): that is the known cost of "no
timeout"; report where. Note that the game gives emergency vehicles no exemption in this check, so an ambulance or hearse
sent to such a victim is held by the same person if its path crosses that crossing: the victim cannot be collected, a real
mutual wait. Rare (it needs a collapsed person lying on a crossing), not tested offline: report it if you see it.

**Log:** with **timing log** on, each timing line has `; never push through people on crossings on` and
`; crossing give-ups refused N (5 s push A, standing person B, cyclist C)`: N = car-steps in the window where the game
would have crept through a person and the setting made the car stop instead (cyclist: car-steps in which the car's ignored
blocker was a cyclist on a crossing; counted when seen, whether or not the creep would have mattered that step). N above 0 at a busy zebra shows
the setting at work; N = 0 with the setting on and cars still driving through people means another path: report it
with the place and a screenshot.

**Report back:** zebra and junction verdicts (good/bad), a log excerpt with the counter, and any car stuck longer than
about a minute (place, what blocked it).

## 12. 0.3.3: Moonflower x Anchor, and crossings out of the whole-junction gap

Both changes act only with **Car driving** = Junction Rules copy and **Junction gap rule** = Whole junction. No new
setting: the crossing change is how Whole junction now works (owner ruling), not a toggle.

**(1) Moonflower Street x Anchor Street** (T junction, Moonflower STOP). Yellow = from Moonflower turning left onto Anchor
eastbound; red = from Anchor westbound turning right into Moonflower. Their paths stay about 2.9 m apart (centre to
centre), more than the 2.4 m at which the game counts two lanes as overlapping, so neither the game nor the mod has a
conflict between them. But the junction also has the game's unsafe U-turn from Anchor westbound back to Anchor eastbound,
which ends in yellow's exit lane. The mod (0.2.3 arrival projection) counted every car on Anchor westbound approaching the
junction as if it might take that U-turn, and held yellow for it. 0.3.3 never does that for a lane the game marks unsafe.
A car that really U-turns there still holds yellow (it reserves the lane and is on it).

**(2) Crossings.** A car waits at its spot only for cars. A crosswalk further along its path (usually the one on the
road it turns into) is checked when the car reaches it, by the game's own pedestrian check (and the 0.3.2 "never push
through people" stays on). Clear crosswalk: the car drives through without stopping or slowing. People on it or about
to step on: the car stops just before the crosswalk and goes when it is clear. The crosswalk on the car's own arm (before
the spot) is checked first, as before. Inside the junction each later car conflict is re-checked as before (stop just
before it only if it is no longer clear).
- **Vehicle must fit:** this applies only when the whole vehicle (trailers counted) fits between the last car conflict
  before the crosswalk and the crosswalk. If it does not fit (short junction, long bus or truck), the crosswalk is judged
  at the spot as in 0.3.2 (the car waits at the spot until the crosswalk is clear too). Merges into the same exit lane
  do not count as conflicts for this (standing there is ordinary queueing in that lane). Owner decides if that fallback
  should stay.

**What to watch** (game speed 1, close camera, timing log on for the counters):

| Where | Look for | Good | Bad |
|---|---|---|---|
| Moonflower St x Anchor St | car leaving Moonflower to the left while a car on Anchor (from the east) turns right into Moonflower | leaves without waiting for it (it still waits for traffic from the west going east) | still waits for the right-turner |
| Lindbergh St x Anchor St | right-turner meeting a car already in the junction | gives way to the car already in the junction (as before) | drives into it |
| Junction with a busy zebra on the exit arm | turning car whose exit crosswalk is busy, cars clear | leaves the spot, stops just before the crosswalk, goes when clear | waits at the spot for the people (0.3.2 behaviour), or drives into people |
| Same junction, exit crosswalk clear | turning car | drives straight through, no slowing at the crosswalk | brakes or stops at a clear crosswalk |
| Long bus / truck at a tight junction | turning into a busy crosswalk | waits at the spot (does not fit) | stops with its tail across another lane's path |

**Deadlock / blocking to look out for (reported, no safeguard):** the car stopped before a busy exit crosswalk stands
inside the junction, but the fit test keeps its own tail clear of the car conflicts it has passed. The followers are
the risk: a car behind it on the same path decides at its own spot on the cross traffic only (a car standing ahead on
its own path is not a conflict), drives in and queues behind it by ordinary car-following, and can then stand across
another lane's path (no room check for followers; the yellow box question of stage 3). Cross traffic is then held by
that follower until the people clear the crosswalk; with a steady stream of people this can back up the other road.
No mutual wait with people: people never wait for cars (0.3.2 finding), so it always clears when the crosswalk does.
Same exposure as 0.3.2 had at a mid-block zebra just past a junction. Report long queues at such junctions with place
and screenshot.

**Log:** each timing line now also has `; unsafe lane N` inside the feeding-scan brackets (car-steps in which an unsafe
conflicting lane was not scanned: above 0 at Moonflower x Anchor shows fix (1) at work) and
`; crossings after W judged at the crossing A, kept at W (vehicle does not fit) B` (car-steps).

**Report back:** verdicts for each table row, a log excerpt with the counters, and any car stuck in a junction before a
crosswalk for longer than about a minute (place, what it blocked).

## 13. 0.3.4: why the decal never showed (cause found), and the one-restart test

### Cause

Every 0.3.0-0.3.3 draw sent `colossal_EdgeNoise = (0, 0)` (our old default `"edgeNoise": [0, 0]`). The diag runs (D0-D6)
never changed it, so every switch failed the same way.

- The DBufferMesh pixel shader (`docs/shader-areadecal/blob054.asm`) divides by the noise size. Lines 208-210 compute
  `r6.w = 1 / (cb4[12].z * cb4[17].x)`, where `cb4[12].z` = `colossal_EdgeNoise.y` (UnityPerMaterial byte 200) and
  `cb4[17].x` = `colossal_AreaParameters.x` (expand, byte 272). The layout comes from the shader's parameter blob 44
  (`docs/shader-areadecal/blob044.params.txt`).
- Size 0 gives 1/0 = inf, so the simplex noise coordinates become inf and `frc` returns NaN (lines 353, 375). The noise
  amount (0) times NaN is still NaN (lines 604-608). `mul_sat` of NaN is 0 under D3D rules (lines 611-617), so the edge
  fade is 0 on every pixel.
- Alpha = BaseColorMap.a x `_BaseColor.a` x LOD fade x edge fade = 0 (lines 668-670, output `o0.w` line 968). The
  normal and mask targets carry the same 0 (`o1.w`, `o2.w`, `o3.xy`). HDRP blends DBufferMesh with SrcAlpha /
  OneMinusSrcAlpha, so every write was a no-op: the draw ran, the pixels passed, nothing changed.
- The game never sends 0. `Game.Prefabs.RenderedArea.m_EdgeNoise` is `[RangeN(0f, 1f, 0.01f, 10f)]`, default (0.5, 0.5)
  (decompile `Game.Prefabs/RenderedArea.cs`, lines 87-88). Expand 0 would divide by zero the same way (line 600).

Ruled out by the same reading, besides section 10's table: the material clone, mesh, draw call, render queue (2000 is
inside HDRP's DBufferMesh range), bounds, winding, colour masks (the cloned 'Rendered Area Material' has DBuffer masks
15/15/11/8) and the decal layers.

### How the game gets area decals into the DBuffer (and ours now does the same)

1. `Game.Rendering.AreaBatchSystem` clones the batch material: `new Material(RenderedArea.m_Material ??
   AreasConfigurationPrefab.m_RenderedAreaMaterial)`, with render queue = shader queue and the per-prefab
   `colossal_AreaParameters`, `colossal_EdgeNoise` etc. (`AreaBatchSystem.cs` lines 2083-2119).
2. `Game.Rendering.AreaRenderSystem.Render`, hooked to `RenderPipelineManager.beginContextRendering`, calls
   `Graphics.RenderMeshIndirect` for each non-preview camera with the "Area triangle volume" mesh, that material and the
   triangle buffers (`AreaRenderSystem.cs`, `Render`, lines 71-210).
3. HDRP's `RenderDBuffer` builds a renderer list for the `DBufferMesh` pass, opaque queues 1000-2500, draws it with
   `CoreUtils.DrawRendererList`, then calls `DecalSystem.instance.RenderIntoDBuffer` for projector decals. HDRP 14.0.12
   (the game runs Unity 2022.3.71f1 with HDRP 14.0.x): `Runtime/RenderPipeline/HDRenderPipeline.Prepass.cs`
   `RenderDBuffer` line 933, renderer list lines 973-977, `DrawRendererList` line 998, `RenderIntoDBuffer` line 1000;
   `Runtime/RenderPipeline/HDRenderQueue.cs` line 91 (`k_RenderQueue_AllOpaque`); the DBufferMesh blend, colour mask
   and stencil states are in `Editor/Material/Decal/ShaderGraph/DecalSubTarget.cs` lines 424-425 and 457-469. The
   game's own HDRP build has the same code (decompiled `HDRenderPipeline.RenderDBuffer`).

### Fix (0.3.4)

`"edgeNoise"` defaults to `[0, 1]` (no wobble: clean edges). Values are clamped to the game's range: amount 0-1, size
0.01-10. `"expand"` has a floor of 0.001. An `"edgeFade"` with no gap (x - y under 0.001) resets to `[1, 0]`. A clamp
writes the usual warning line, and the `diag switches (effective)` line now shows `edgeNoise [x, y]`. Nothing else
changed: the diag switches stay, and driving is the same as 0.3.3.

### Live test order (one restart)

1. Game closed: install 0.3.4 (section 1). In `paint-test.json` delete the `"diag"` object and any `"edgeNoise"` key (an
   old `[0, 0]` would be raised to `[0, 0.01]`, which also works, with a warning).
2. Start the game and load the city. Check the log for `OnLoad version 0.3.4.0` and
   `diag switches (effective): ... edgeNoise [0, 1]`.
3. Options > Junction Rules: test **on**, **Plain fill**, one at-grade junction listed (the 0.3.0 one is fine). Daylight,
   no infoview, camera 100-200 m away. **Expect yellow paint on the road, under the cars** (cars are not tinted).
   Screenshot.
4. Switch to **Box mode** (hatch). Screenshot. Then run S6-S11 (section 4) as written.
5. Optional, for the game's look: add `"edgeNoise": [0.5, 0.5]` to `"paint"` and press **Refresh test list** (worn,
   wobbly edges like vanilla surfaces).

If step 3 still shows nothing, first set `"swapPrevNext": true` in `"paint"` and press **Refresh test list** (a wrong prev/next order could make the shader discard every pixel, a second possible cause that edge noise does not touch), then run section 10's D1-D3 once with the new default in place and report the log lines.
`"drawPath": "overlay"` is not paint: it is the area tool's unlit overlay, which tints cars too.

## 14. 0.3.5: box on the carriageway only, Roads by default

### Why 0.3.4 painted the pavements ("no road-lane pieces: kerb inset 0")

The footway step looked for road-lane pieces in the junction's **node** composition (`Composition.m_StartNode` /
`m_EndNode`). The game marks a piece as carrying road lanes (`NetPieceFlags.HasRoadLanes`) in one place only,
`NetCompositionHelpers.AddCompositionLanes` (decompile `Game.Prefabs/NetCompositionHelpers.cs` lines 1464-1465), and
`NetCompositionSystem` calls that only for compositions that have a `NetCompositionLane` buffer
(`Game.Prefabs/NetCompositionSystem.cs` lines 163-181). Only **edge** compositions have that buffer: the node archetype
gets `NetCompositionCrosswalk` instead (`Game.Prefabs/NetGeometryPrefab.cs` lines 106-112). So a node composition never
has the flag, the step found no carriageway at any junction, and the box used the outer edge of the node: pavements
and corner kerbs included. Not a Moonflower x Anchor quirk.

### How the outline is built now (`Core/CarriagewayOutline.cs`, unit-tested)

1. Each arm's two outer edges: the edge's `Start/EndNodeGeometry` curves `m_Left.m_Left` and `m_Right.m_Right` (the
   pair the game uses as the slice's full width, `Game.Net/LaneSystem.cs` lines 7918-7938), from the mouth inward,
   16 chords each.
2. Footway width each side from the arm's **edge** composition (`Composition.m_Edge`) **lanes**: the carriageway runs
   from the outer edge of the outermost road / bicycle / parking lane on one side to the other
   (`NetCompositionLane.m_Position.x` +- `NetLaneData.m_Width / 2`; lane position = piece offset + piece lane
   position, `NetCompositionHelpers.cs` line 1443); the rest each side is footway, never below 0. Not pieces (review
   of the first 0.3.5 commit): on Small Road the "Sidewalk With Parking Piece 5" (x 3..8) holds both the parking lane
   (x 4, 2 m wide) and the pavement, and its "- Bay" variant carries `HasRoadLanes` itself, so a piece test gave
   footway 0|0. The kerb is at the parking lane's outer edge (asphalt to x 5): **Small Road expects footway 3|3**,
   matching the bridge's laneMin/laneMax +-5 and clearWidth 3. If the edge has no such lanes, the node composition is
   tried with the piece prefabs' lane lists (log shows `n` after the widths); if that fails too, the log says
   `footway unknown (...)` and the arm shows `unknown`, never a silent `0|0`. Non-road edges (paths) joined to the
   node are not arms (log note `non-road edge(s) (path) at the node skipped as arms`). NaN/Inf geometry gives no box
   and a clean `non-finite input` reason.
3. Which outer edge is the edge's left: the one whose mouth point touches the edge's own left kerb end
   (`EdgeGeometry`; composition x < 0 = left, `LaneSystem.cs` line 3648). No start/end mirroring assumption.
4. Each outer edge is moved in by its footway (+ `kerbInset`): the kerb lines, round the corner curves.
5. Stop line: the mouth (where the edge's lanes end, where the white stop / give-way line is). Where the arm has a
   **marked** zebra (crosswalk lane without the Unsafe flag, `LaneSystem.cs` lines 8571-8580), the arm starts at the
   zebra's junction-side edge instead (its line plus half the crossing lane width). Unmarked crossings are boxed over.
6. The kerb lines are merged into one ring as before (corners cut where neighbouring kerbs meet).

Log per box: `footways (m) per arm 3.00|3.00, ...` and, when the node has crossings,
`N zebra(s), M unmarked crossing(s); box starts per arm at +[...] m from the mouth`.

### The jagged west border in the 0.3.4 hatch shot

Not the outline. The 0.3.4 outline's west side is one straight mouth segment (the two mouth points of the west arm;
the merge adds no point on a mouth), and the jag follows the hedge's leafy silhouette in both the plain and the hatch
shots: the hedge on the south-west pavement stands in front of the painted pavement from that camera, and its shadow
falls on it. With 0.3.5 the box stops at the kerb, so that pavement is no longer painted. (With Roads + Other the
hedge may also take paint.) If a jag stays on the carriageway, report it with a top-down shot: that would be outline.

### What to look at live (one restart, game closed for the install)

1. Install 0.3.5 (section 1, after main's Burst build). Log: `OnLoad version 0.3.5.0`, the settings migration line,
   and Options shows **paint onto = Roads**. (The schema version is not reset by "Reset to defaults", so a Roads +
   Other chosen later survives restarts.)
2. Moonflower St x Anchor St (node 874687), **Plain fill**, then **Box**:
   - **Pavements clean**: no paint on any footway or the corner kerbs.
   - **Kerb curves followed**: the box edge runs along the kerb line round both corners by Moonflower St.
   - **Stop lines**: the box starts at the Moonflower stop line and at each Anchor St mouth, not up the arms.
   - **West edge**: straight along the west mouth, ending at the kerb (no jag near the hedge).
   - **Poles clean**: street lights and signs not yellow (Roads layer).
   - Log line: `footways (m) per arm` shows the real widths: **3.00|3.00 on each Small Road arm** (Moonflower St,
     Anchor St incl. the Bay arm on seg 1175972), no `unknown`, no `footway unknown`. Copy the line into the report.
3. A junction with **zebras** on its arms: box ends at the zebra's junction-side edge, never on the stripes.
4. A junction with an asymmetric road (footway one side only, or parking one side): box on the carriageway on both sides.
5. Then S7-S10 (section 4) as written.

Report: screenshots of 2-4 (one top-down), the `footways` / `zebra` log lines, and any arm where the box edge is off
the kerb (by how much, which side).

## 15. 0.3.6: people pass a car that blocks a crossing

**The settings:** Options > Junction Rules > **People pass a car that blocks them** (on/off, default **on**; off =
exactly the game's behaviour: the pass does nothing and its counter stays 0) and, right below it, **People pass a car
that blocks a crossing after (s)**, slider 0-30 s in 0.5 s steps, default **2 s** (owner rulings 16 Nov; greyed out
while the switch is off; 0 also means off). Both work with either **Car driving** setting (not part of the car copy)
and apply at the next simulation step.

**Why people stood still behind a car (stock, Game.dll 1.6.4f1):** people never bump into cars on a crossing, but the
game stops a person at the edge of each road lane of the crossing while any vehicle stands on that lane across the
crossing (CreatureTargetIterator.CheckOverlapLane), with **no time limit**. In the game that rarely lasts: the car
creeps on after 5 s (or at once past a person standing still). With **Cars never push through people** on (0.3.2), a car
standing on a crossing because it waits for people further on (0.3.3/0.3.4: inside the junction before the exit
crosswalk, its tail on the entry crosswalk) or in a queue that spills onto a zebra no longer creeps, so the people it
covers waited until it moved. Worst case, a mutual wait: the person stands at the edge of lane 2 (car B there), inside
lane 1's part of the crossing; the car on lane 1 waits for that standing person (0.3.2 refuses the creep); B waits in
a queue behind lane 1's car. 0.3.6 lets the person walk through B after the wait, which ends all of these.

**What it does:** after the wait the person's next step goes to the far side of that lane (or of the vehicle, if it
covers two lanes) and they walk on through the vehicle at normal pace. A cyclist riding the crossing gets the game's own
"ignore this blocker" pass and rides through at about 11 km/h. Only vehicles that **stand** (under 0.5 m/s) count; a
car driving across holds people as in the game. People waiting at a red pedestrian signal are not affected.

**What to watch** (game speed 1, close camera; timing log on for the counter):

| Where | Look for | Good | Bad |
|---|---|---|---|
| Junction crosswalk, turning car stopped inside the junction before a busy exit crosswalk | people on the entry crosswalk behind its tail | they stop at the car for about 2 s, then walk through it | they stand until the car leaves |
| Mid-block zebra with a queue spilling onto it | people at the queued car | about 2 s, then through | they wait for the queue |
| Same scene, a car still rolling across the zebra | people | they wait for it as before (no pass through a moving car) | they walk into a moving car |
| Signalled crossing, red man | people | wait for green as before | walk on red |
| Cyclist on a crossing with a car standing across it | the cyclist | about 2 s, then rides through slowly | stuck |
| **People pass a car that blocks them** off (A/B) | the same scenes | the game's behaviour (people wait); log counter `n/a` or 0 | any pass while off |

**Log:** each timing line (game's own and copy) ends with `; people passed blocking cars N (cyclists C)`: people and
cyclists who passed a standing vehicle in that window (`n/a` when the pass has not run yet, e.g. switched off since the game started; it never moves while switched off).

**Report back:** verdicts per table row, a log excerpt with the counter, and any person or cyclist stuck at a car for
longer than about 10 s (place, screenshot).
