# Probe kit: Junction Rules 0.3.2-probe (stage 3, yellow box paint test, option E; plus the crossing setting)

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

Build to install: `~/Code/cs2-junction-rules/.staged/0.3.2-probe/`. If that folder holds a `STALE.md`, it has only a
managed build (made while the game was running): main does the Burst rebuild in `STALE.md` (game closed) when the owner
quits, then puts the new sha256s in the table and the install block below. Until then the install block refuses (the
sums below are placeholders on purpose: `0000...`). 0.3.2: managed build only so far (STALE.md present).
(0.3.0-probe was Burst-built from commit 82ec0ce and installed by main on 15 Nov.)

| File | sha256 |
|---|---|
| `JunctionRules.dll` | `0000000000000000000000000000000000000000000000000000000000000000` |
| `JunctionRules.pdb` | `0000000000000000000000000000000000000000000000000000000000000000` |
| `JunctionRules_win_x86_64.dll` (Burst code) | `0000000000000000000000000000000000000000000000000000000000000000` |
| `JunctionRules_win_x86_64.pdb` | `0000000000000000000000000000000000000000000000000000000000000000` |

Built for game **1.6.4f1 (1097.158a)** only (same version gate as before: on another game version the paint test does not start, and the status says so).

## 1. Install over 0.3.1-probe / 0.3.0-probe / 0.2.4 (game closed only; the 0.2.4 backup made by the 0.3.0 install is kept as it is)

```
(
  CS2="$HOME/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
  BACKUP="$HOME/Code/cs2-junction-rules/.staged/0.2.4-installed-backup"
  cd ~/Code/cs2-junction-rules/.staged/0.3.2-probe || exit 1
  if [ "$(ps -eo comm | grep -ci cities2)" != 0 ]; then echo "GAME IS RUNNING - stop, nothing installed"; exit 1; fi
  if [ -e STALE.md ]; then echo "STAGED BUILD IS STALE - see STALE.md, rebuild first; nothing installed"; exit 1; fi
  if [ ! -d "$CS2" ]; then echo "CS2 folder not found - nothing installed"; exit 1; fi
  sha256sum -c --strict - <<'SUMS' || { echo "SHA256 MISMATCH - nothing installed"; exit 1; }
0000000000000000000000000000000000000000000000000000000000000000  JunctionRules.dll
0000000000000000000000000000000000000000000000000000000000000000  JunctionRules.pdb
0000000000000000000000000000000000000000000000000000000000000000  JunctionRules_win_x86_64.dll
0000000000000000000000000000000000000000000000000000000000000000  JunctionRules_win_x86_64.pdb
SUMS
  if [ -d "$CS2/Mods/JunctionRules" ] && [ ! -e "$BACKUP/JunctionRules.dll" ]; then
    mkdir -p "$BACKUP" && cp -p "$CS2/Mods/JunctionRules/"* "$BACKUP/" && echo "backed up the installed files to $BACKUP" || { echo "BACKUP FAILED - nothing installed"; exit 1; }
  fi
  mkdir -p "$CS2/Mods/JunctionRules" &&
  cp JunctionRules.dll JunctionRules.pdb JunctionRules_win_x86_64.dll JunctionRules_win_x86_64.pdb "$CS2/Mods/JunctionRules/" &&
  echo "installed 0.3.2-probe"
)
```

Paste the whole block. It copies nothing unless the game is closed, the build is not stale and all four sums match.

## 2. Log checks

`$CS2/Logs/JunctionRules.Mod.log` after the main menu:
- `OnLoad version 0.3.2.0, ...`
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
  `"swapPrevNext": true`, then `"edgeFade": [0, 0]`.
- **U3:** does it look like painted G87 (wetness, wear, night, shimmer at distance)?
- **U4:** which decal layer the junction and deck take (Roads, or Roads + Other).
- **U7:** footway inset from the road data matches the visible kerb on all road types (the end-node side is mirrored:
  an assumption). Use `kerbInset` to test corrections.
- **U8:** texture mapping (`uvScale`, `plainTextures`; with `plainTextures` false the material's own textures are used).
- **Roundabout node:** boxed whole from the outer kerbs; the island is not cut out yet.
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

Setup: install 0.3.1, start the game, load the city, Options > Junction Rules: test **on**, **Plain fill**, list one
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
minutes behind a person who never leaves the crossing (an accident victim, until the ambulance comes): that is the
known cost of "no timeout"; report where.

**Log:** with **timing log** on, each timing line has `; never push through people on crossings on` and
`; crossing give-ups refused N (5 s push A, standing person B, cyclist C)`: N = car-steps in the window where the game
would have crept through a person or cyclist and the setting made the car stop instead. N above 0 at a busy zebra shows
the setting at work; N = 0 with the setting on and cars still driving through people means another path: report it
with the place and a screenshot.

**Report back:** zebra and junction verdicts (good/bad), a log excerpt with the counter, and any car stuck longer than
about a minute (place, what blocked it).
