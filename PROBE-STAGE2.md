# Probe kit: Junction Rules 0.2.1 (stage 2, whole-junction gap decision + collision fix); 0.2.2 in section 8; 0.2.3 (arrival-time projection, no mid spots) in section 9

0.2.1 replaces the installed 0.2.0 Burst build. It fixes the collisions between cars that had already gone (root causes:
`docs/STAGE2-ANALYSIS.md` section 7) with the owner ruling of 14 Nov 2026 (section 8):
- **Waiting spots**: W (just before the first crossing, as 0.2.0) and **mid-junction spots** where the whole car fits
  between two crossings without touching any other lane's path (e.g. a wide central reservation on a divided road).
  At each spot ONE decision for the crossings up to the next spot (a "stretch"), stock judgement on each. Only the car
  at the spot decides; followers queue with stock car-following.
- **Safety re-check while crossing**: the car keeps checking every crossing of its stretch it has not reached yet. If
  one is no longer clear (a car it missed or misjudged is coming) and it can still stop with the game's normal braking,
  it stops just before the first one it has not reached (safety stop) and waits until the rest of the stretch is clear.
  If it cannot stop: collision braking only, as before.
- **Decision fix**: a car that can no longer stop at its spot keeps the game's own give-way at the busy crossing
  (0.2.0 sent it to the unreachable spot, it overran it and then ignored the crossing).

Everything else as 0.2.0 (amber live, exit queues stock, no starvation safeguard, STOP arms stock). Defaults change
nothing: **Car driving** = Game's own, **Junction gap rule** = Stock.

Build to install: `~/Code/cs2-junction-rules/.staged/0.2.1-burst/`. If that folder holds a `STALE.md`, it has only a
managed build (made while the game was running): do the Burst rebuild in `STALE.md` first (game closed), then put the
new sha256s in the table and in the install block below. Until then the install block refuses (the sums below are
placeholders on purpose: `0000...`).

| File | sha256 |
|---|---|
| `JunctionRules.dll` | `PLACEHOLDER - see .staged/0.2.1-burst/STALE.md` |
| `JunctionRules.pdb` | `PLACEHOLDER` |
| `JunctionRules_win_x86_64.dll` (Burst code) | `PLACEHOLDER` |
| `JunctionRules_win_x86_64.pdb` | `PLACEHOLDER` |

Built for game **1.6.4f1 (1097.158a)** only (same version gate as 0.1.0/0.2.0).

## 1. Install over 0.2.0 (game closed only; backs up the installed 0.2.0)

```
(
  CS2="$HOME/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
  BACKUP="$HOME/Code/cs2-junction-rules/.staged/0.2.0-installed-backup"
  cd ~/Code/cs2-junction-rules/.staged/0.2.1-burst || exit 1
  if [ "$(ps -eo comm | grep -ci cities2)" != 0 ]; then echo "GAME IS RUNNING - stop, nothing installed"; exit 1; fi
  if [ -e STALE.md ]; then echo "STAGED BUILD IS STALE - see STALE.md, rebuild first; nothing installed"; exit 1; fi
  if [ ! -d "$CS2" ]; then echo "CS2 folder not found - nothing installed"; exit 1; fi
  sha256sum -c --strict - <<'SUMS' || { echo "SHA256 MISMATCH - nothing installed"; exit 1; }
94a9226d9972857d0b561abb9f704902180607de6e70707bcded335b3164b7cf  JunctionRules.dll
521a1de6ca8a82f95cea471ac3205d82b2582b2292201508494653f2a0b7fc31  JunctionRules.pdb
2ec79dc48b95592824a4cd931d900c7359a936b95bfed1e24dd2fbc535673bf7  JunctionRules_win_x86_64.dll
30311c033082de2d46fbfbd5298132701d3125c2f261bfe151e3244f5311159a  JunctionRules_win_x86_64.pdb
SUMS
  if [ -d "$CS2/Mods/JunctionRules" ] && [ ! -e "$BACKUP/JunctionRules.dll" ]; then
    mkdir -p "$BACKUP" && cp -p "$CS2/Mods/JunctionRules/"* "$BACKUP/" && echo "backed up the installed files to $BACKUP" || { echo "BACKUP FAILED - nothing installed"; exit 1; }
  fi
  mkdir -p "$CS2/Mods/JunctionRules" &&
  cp JunctionRules.dll JunctionRules.pdb JunctionRules_win_x86_64.dll JunctionRules_win_x86_64.pdb "$CS2/Mods/JunctionRules/" &&
  echo "installed 0.2.1"
)
```

Paste the whole block: it runs in a subshell, so a failed check ends only the subshell. It copies nothing unless the
game is closed, the build is not stale and all four sha256s match. The first run keeps the installed files (0.2.0) in
`.staged/0.2.0-installed-backup/` (an existing backup is never overwritten). The 0.2.0 Burst build itself stays in
`.staged/0.2.0-burst/` (sums in section 6).

Launch the game the usual way (Proton 11.0 stable), Bevy Valley.

## 2. Log checks

`$CS2/Logs/Modding.log`: `JunctionRules` loaded, no exception; `Loaded additional Burst code ...JunctionRules_win_x86_64.dll`
(without it the copy runs as slow managed code: stop and report).

`$CS2/Logs/JunctionRules.Mod.log` after the main menu:
- `OnLoad version 0.2.1.0, copy made from game 1.6.4f1 (1097.158a)`
- `version check: Pass: ...` (as for 0.1.0)
- `settings: car driving GameOwn, junction gap rule Stock, waiting spot BeforeFirstCrossing, timing log off, skip code hash check off`

After loading the city:
- `car driving starts with game's own (...)`
- `junction gap rule starts as stock (acts only while the Junction Rules copy drives), counters off`
- `navigation timing log off`

Options > Junction Rules shows **Junction gap rule** (Stock / Whole junction) and **Whole junction: waiting spot**
(Before the first crossing / At the stop line). Leave the waiting spot on the default for every pass below.

## 3. Before the passes

- Bevy Valley only. **Gap Acceptance idle**: City-wide OFF and nothing on its opt-in list, exactly as in the stage 1
  passes (it is no longer the reference; it must not hold cars during these passes).
- **Car driving = Junction Rules copy** for every pass (the rule only acts on the copy).
- **Log navigation timing ON** (also turns the stage 2 counters on; log: `junction gap counters on`).
- Same camera spot and graphics settings, no building during a pass.

Switching the rule: Options > Junction Rules > **Junction gap rule**. It applies at the next simulation step; the log
says `junction gap rule switched to whole junction (applies at the next step in which the Junction Rules copy drives)`.
Skip the first timing line after every switch (it may say `junction gap rule mixed (switched in this window)`).

Timing line (stage 1 format plus the rule and counters):
`timing Junction Rules copy: frames a-b (1024 sim frames in S s = R frames/s), cars C, navigation job schedule-to-done ms over N runs: mean .. p50 .. p95 .. max .. (skipped k); junction gap rule stock; car-steps: stretch decisions D, waited at entry spot for a later conflict W, waited at a mid-junction spot M, safety stops inside S, stopped inside for a give-way I, stopped inside behind a crossing car X`

Counters are car navigation updates (each car is updated every 16 frames), summed over the window:
- **stretch decisions**: updates of cars before a waiting spot (entry or mid) on a junction lane, deciding for the
  stretch after it, with the rule on (0 on stock).
- **waited at entry spot for a later conflict**: updates of cars standing at their entry spot (W) because a conflict
  AFTER the first one was busy (0 on stock).
- **waited at a mid-junction spot**: updates of cars standing at a mid spot (e.g. on a central reservation) for their
  next stretch (0 on stock; 0 where no junction has room for a car between crossings).
- **safety stops inside**: updates of cars braking or standing before an uncrossed point of their stretch because a
  car they missed or misjudged showed up (0 on stock). Should be RARE: a few per window at most; a count that grows
  every window at the same junction is a finding (report the junction).
- **stopped inside for a give-way**: updates of cars standing inside a junction (front past their entry spot) held by
  a give-way that is neither a mid-spot wait nor a safety stop. Counted on both rules: expect a real number on stock
  and still about 0 with whole junction (0.2.0 probe: 0 in every window).
- **stopped inside behind a crossing car**: standing inside held by a car physically in their path (collision braking,
  both rules).

## 4. Timing A/B: stock vs whole junction (copy driving)

Each pass: 6 timing lines, skip the first, keep 5 (same in-game time at either speed).

| Pass | Speed | Junction gap rule | Record |
|---|---|---|---|
| S1 | 1x | Stock | 5 timing lines; FPS |
| W1 | 1x | Whole junction | 5 timing lines; FPS |
| S2 | 4x | Stock | 5 timing lines; FPS |
| W2 | 4x | Whole junction | 5 timing lines; FPS |
| S3 | 4x | Stock | 5 timing lines (drift between S2 and W2) |

What passes:
- W `mean` and `p95` within about 10 % of S at the same speed (S3 shows the normal drift).
- S close to the stage 1 copy numbers (1x mean ~0.55 / p95 ~0.68 ms; 4x ~0.6 / ~1.05 ms). This is the check for the
  stage 2 scan cost itself: with the timing log on, the counters are on even on Stock, so the S passes already run the
  per-junction-lane scan (`BeginLane`) and the counter code. S vs W therefore hides that scan; S vs the stage 1 numbers
  shows it.
- `frames/s` and FPS the same within noise; `skipped` 0 or near it.
- Each line says the rule that ran (`stock` / `whole junction`).
- Counters: `stopped inside for a give-way` clearly lower with whole junction (about 0); `waited at entry spot for a
  later conflict` > 0 with whole junction, 0 on stock; `safety stops inside` small against `stretch decisions`.
- Compare W against the 0.2.0 whole-junction numbers too (0.2.1 finds the spots per junction lane: a little more work).

## 5. Behaviour A/B at two junctions

Same speed (1x), same camera, Gap Acceptance idle, copy driving. Per junction: 6 timing lines on Stock, then 6 lines on
Whole junction (switch, skip the first line). Junctions:

- **Valley x Carlisle** at (1092.7, -81.5) (STOP/YIELD arms; no sign, lane or connection changes there).
- **Sunset x Cherry** (the owner's stage 1 junction).

What to look for, comparing the two rules:
- **No car stopped mid-junction to give way** with Whole junction. A car that has to wait stands just before the first
  lane its path crosses: at the stop line when the first crossing is at the junction edge, a few metres in when its
  first crossing is further in.
- **Turners across oncoming traffic** (right turns; Bevy Valley drives on the left): on a green or from the major road
  they pull forward to just before the FIRST oncoming lane, wait there, then cross ALL oncoming lanes in one go; never
  cross one oncoming lane and stop again before the next.
- **Each car decides at the front**: when two turners queue, the second one follows the first only up to the waiting
  spot. It stops at the spot (or reaches it with a clear gap) before crossing and makes its own decision there; it
  never slips through right behind the first one on the first one's gap. Same for queues on any arm: only the car at
  the spot decides.
- **Amber**: a side-road lane on amber still counts. A turner waits before it while it is on amber; only red side-road
  lanes let it pull further in.
- **Exit queues: nothing special**: a car that has gone may still stop behind a queue on the exit, as in stock (no
  yellow-box behaviour in this build). Do not report that as a fault; report only a car that stopped inside to give
  way.
- **STOP arms**: full stop at the line as before, then roll up to the waiting spot if it is further in, then one go.
- **Long waits**: note roughly how long the longest minor-arm wait is on each rule. Longer waits with Whole junction
  are expected on a busy major road (it needs both directions clear at once). The rule has no timeout by owner ruling:
  a very long wait is a road design / city finding, so report it with the junction name, it is not a test failure.
- **No new jams or deadlocks**: no cars frozen facing each other, no queue that never clears; red/green obeyed.
- **Collisions gone (the 0.2.1 fix)**: no car that has already gone drives into another car inside the junction (the
  0.2.0 fault). A car may now brake and stop just before a lane it has not reached yet when a fast car comes (safety
  stop: accepted, should be rare), possibly with its rear in a lane it has partly crossed.
- **Mid-junction spots**: on a divided road with a wide central reservation inside one junction (Carlisle where the
  geometry allows), a crossing or turning car may cross the near side, wait on the reservation, then cross the far side
  in a second decision. No mid spot where the reservation is shorter than the car: then it is one decision as in 0.2.0
  (buses and trucks often get one decision where cars get two).
- **Valley x Carlisle westbound case**: a westbound Carlisle car waiting for an eastbound right-turner is the game's own
  priority-by-claim (the turner was already in or had claimed the crossing); expected occasionally on Stock too. Report
  only a turner that drove on into the westbound lane after the westbound car was clearly there (0.2.1 should stop it
  before that lane if it can still stop).
- No cars driving through each other; no new despawns; `JunctionRules.Mod.log`, `Modding.log`, `Player.log` free of
  new exceptions.
- Optional: one extra 6-line pass with **Whole junction: waiting spot = At the stop line** at one junction (turners then
  wait at the line). Set it back to the default after.

Optional extra passes (same method: 6 lines on Stock, 6 on Whole junction; Bevy Valley only). No coordinates are
given here on purpose: pick the site in game by the description.
- **Roundabout**: any busy roundabout in Bevy Valley (one with queues on at least one entry at the time of the test).
  Look for: entering cars wait before the ring lane they cross or join and then enter in one go; ring cars decide
  again at each roundabout node (one decision per node, owner ruling); no ring gridlock that does not also happen on
  Stock.
- **Green right turn across oncoming**: a signalised junction where right-turners (left-hand traffic) share the green
  with two or more oncoming lanes and turn across them in numbers (a busy arterial junction with a right-turn lane).
  Look for: turners pull up to just before the first oncoming lane, cross all oncoming lanes in one go, the second
  turner stops at the spot before crossing and never slips through behind the first; when the light changes, a turner
  standing in the junction clears it.

## 6. Rollback

- Immediate: **Junction gap rule** back to Stock (next step); or **Car driving** back to Game's own.
- Back to 0.2.0 Burst (game closed): copies the staged 0.2.0 build after checking its sums.

  ```
  (
    CS2="$HOME/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
    cd ~/Code/cs2-junction-rules/.staged/0.2.0-burst || exit 1
    if [ ! -d "$CS2/Mods/JunctionRules" ]; then echo "mod folder not found - nothing changed"; exit 1; fi
    if [ "$(ps -eo comm | grep -ci cities2)" != 0 ]; then echo "GAME IS RUNNING - stop, nothing changed"; exit 1; fi
    sha256sum -c --strict - <<'SUMS' || { echo "SHA256 MISMATCH - nothing changed"; exit 1; }
6baf70bbf10368d28ccbfb6ee6cdeda62f3eb633f70221486d0e69386d4cddea  JunctionRules.dll
f93a0d193bca7906925d6742e9430bd4fbe85d9fe1af23faa04e201a7849d9be  JunctionRules.pdb
001d74ae8d545cbf7cb864aba7befafb0da53f7e865845c3aab2f8602eead998  JunctionRules_win_x86_64.dll
930d13ed0fcc530650085cb4c974d44424319c1931b91116cbf8fe6248e878c8  JunctionRules_win_x86_64.pdb
SUMS
    cp JunctionRules.dll JunctionRules.pdb JunctionRules_win_x86_64.dll JunctionRules_win_x86_64.pdb "$CS2/Mods/JunctionRules/" &&
    echo "0.2.0 restored"
  )
  ```

  (`.staged/0.2.0-installed-backup/` from the install holds the same files as a second copy.) Back to 0.1.0: the
  backup in `.staged/0.1.0-installed-backup/` (PROBE-STAGE1.md sums), same pattern. Remove the mod entirely: the
  rollback block of PROBE-STAGE1.md, section 7. The mod adds no data to saves.

## 7. Report back

The three log lines of section 2; per pass the 5 timing lines (or mean/p95/frames per second, and all six counters)
and FPS; per junction and rule: any collision between cars that had gone, safety stops seen, cars waiting on a central reservation, where waiting cars stood, any car stopped inside to give way, turners' behaviour
(second turner stopping at the spot), STOP roll-up, longest minor-arm wait, jams/deadlocks; the optional passes if
done; any exception text.

## 8. 0.2.2: trailers in the vehicle length (JR-T-002d)

0.2.2 = 0.2.1 plus one change (owner ruling 14 Nov 2026, `docs/STAGE2-ANALYSIS.md` section 9): a mid-junction waiting
spot must fit the **whole vehicle, trailers included** (tractor + trailers by the game's own attach points). Cars,
buses and bicycles without trailers behave exactly as in 0.2.1. Owner rulings recorded with it (no behaviour change):
a car physically in a later crossing makes the crossing car brake for it there (KEEP); a follower queued behind a car
on a central reservation may stand in the near carriageway (ALLOWED); "At the stop line" keeps using mid spots (KEEP).

Build to install: `~/Code/cs2-junction-rules/.staged/0.2.2-burst/`. If that folder holds a `STALE.md`, it has only a
managed build (made while the game was running): do the Burst rebuild in `STALE.md` first (game closed), then put the
new sha256s in the table and in the install block below. Until then the install block refuses (the sums below are
placeholders on purpose: `0000...`).

| File | sha256 |
|---|---|
| `JunctionRules.dll` | `PLACEHOLDER - see .staged/0.2.2-burst/STALE.md` |
| `JunctionRules.pdb` | `PLACEHOLDER` |
| `JunctionRules_win_x86_64.dll` (Burst code) | `PLACEHOLDER` |
| `JunctionRules_win_x86_64.pdb` | `PLACEHOLDER` |

### 8.1 Install over whatever is installed (game closed only; backs up the installed files)

```
(
  CS2="$HOME/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
  BACKUP="$HOME/Code/cs2-junction-rules/.staged/0.2.2-prev-installed-backup"
  cd ~/Code/cs2-junction-rules/.staged/0.2.2-burst || exit 1
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
  echo "installed 0.2.2"
)
```

Same rules as section 1: copies nothing unless the game is closed, the build is not stale and all four sums match. The
first run keeps the installed files (whatever version that is: 0.2.0 or 0.2.1) in `.staged/0.2.2-prev-installed-backup/`;
an existing backup is never overwritten.

Log check (section 2) with `OnLoad version 0.2.2.0, copy made from game 1.6.4f1 (1097.158a)`; everything else as
section 2. Passes and counters as sections 3-5 (same junctions, same method).

### 8.2 What to watch (0.2.2)

- **Articulated trucks never wait on a central reservation too short for them**: a semi-trailer, a truck with a
  drawbar trailer or a car with a caravan crossing or turning across a divided road inside one junction either waits on
  the reservation with the WHOLE combination clear of both carriageways, or (reservation too short for it) waits at the
  entry spot and crosses everything in one go. Report any trailer left standing across the near carriageway while its
  tractor waits on the reservation (that was possible in 0.2.1), with the junction name and the vehicle type.
- Cars on the same reservation behave as in 0.2.1 (mid spot where the car fits).
- A follower queued behind a car on the reservation may stand in the near carriageway: allowed by owner ruling, not a
  fault.
- `waited at a mid-junction spot` may be a little lower than 0.2.1 on roads with truck traffic (fewer spots for long
  vehicles); timing as 0.2.1 within noise (one extra buffer walk per car with trailers while stage 2 is on).

### 8.3 Rollback

- Immediate: **Junction gap rule** back to Stock, or **Car driving** back to Game's own.
- Back to 0.2.1 Burst (game closed), from `.staged/0.2.1-burst/`, checked against the sums in the section 1 install
  block of THIS file (the block reads them from there, so it works once the 0.2.1 sums are filled in; while section 1
  still holds `0000...` placeholders or `.staged/0.2.1-burst/STALE.md` exists it refuses):

  ```
  (
    CS2="$HOME/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
    KIT="$HOME/Code/cs2-junction-rules/PROBE-STAGE2.md"
    cd ~/Code/cs2-junction-rules/.staged/0.2.1-burst || exit 1
    if [ ! -d "$CS2/Mods/JunctionRules" ]; then echo "mod folder not found - nothing changed"; exit 1; fi
    if [ "$(ps -eo comm | grep -ci cities2)" != 0 ]; then echo "GAME IS RUNNING - stop, nothing changed"; exit 1; fi
    if [ -e STALE.md ]; then echo "0.2.1 BUILD IS STALE - see its STALE.md; nothing changed"; exit 1; fi
    SUMS21="$(awk '/<<.SUMS./{n++; if(n==1){on=1; next}} on && /^SUMS$/{exit} on' "$KIT")"
    if [ "$(printf '%s\n' "$SUMS21" | grep -c '  JunctionRules')" != 4 ] || printf '%s\n' "$SUMS21" | grep -q '^0000'; then
      echo "0.2.1 SUMS NOT FILLED IN (section 1 of PROBE-STAGE2.md) - nothing changed"; exit 1; fi
    printf '%s\n' "$SUMS21" | sha256sum -c --strict - || { echo "SHA256 MISMATCH - nothing changed"; exit 1; }
    cp JunctionRules.dll JunctionRules.pdb JunctionRules_win_x86_64.dll JunctionRules_win_x86_64.pdb "$CS2/Mods/JunctionRules/" &&
    echo "0.2.1 restored"
  )
  ```

  (`.staged/0.2.2-prev-installed-backup/` from 8.1 holds the files that were installed before 0.2.2 as a second copy.)
  Further back: section 6 (0.2.0), PROBE-STAGE1.md (0.1.0 / remove).

Report back as section 7, plus any trailer standing across a carriageway while its tractor waits mid-junction.

## 9. 0.2.3: arrival-time projection, no mid-junction spots (JR-T-002f)

0.2.3 replaces 0.2.1/0.2.2 behaviour inside **Junction gap rule = Whole junction** (owner rulings and clarification
14 Nov 2026, `docs/STAGE2-ANALYSIS.md` section 10):
- **No mid-junction waiting spots.** A car waits only just before its FIRST crossing (W) and decides once for its whole
  path, far carriageway included. Central reservations are not special: **no car should wait on a central reservation
  by plan**. A car may still stand there after a safety stop (below), rarely.
- **Arrival-time projection.** At W the car works out when it would reach each crossing (the game's own acceleration,
  speed-in-turns cap and braking). Cars on conflicting junction lanes are judged with the game's 2 s look-ahead plus
  that time. EVERY car on an approach lane feeding a conflicting lane counts as a virtual reservation (braking
  distance + speed x that time) in the game's own reservation test.
- **Safety re-check.** While crossing, the same projection runs from the car's current state. If it finds a crossing
  no longer clear and the car can still stop with normal braking, the car stops just before the first crossing it has
  not reached. It goes when everything remaining is clear. Otherwise collision braking only.

Defaults change nothing: **Car driving** = Game's own, **Junction gap rule** = Stock. With the rule on Stock, 0.2.3
drives exactly as 0.2.2 / stage 1.

Build to install: `~/Code/cs2-junction-rules/.staged/0.2.3-burst/`. If that folder holds a `STALE.md`, it has only a
managed build (made while the game was running): do the Burst rebuild in `STALE.md` first (game closed), then put the
new sha256s in the table and in the install block below. Until then the install block refuses (the sums below are
placeholders on purpose: `0000...`).

| File | sha256 |
|---|---|
| `JunctionRules.dll` | `PLACEHOLDER - see .staged/0.2.3-burst/STALE.md` |
| `JunctionRules.pdb` | `PLACEHOLDER` |
| `JunctionRules_win_x86_64.dll` (Burst code) | `PLACEHOLDER` |
| `JunctionRules_win_x86_64.pdb` | `PLACEHOLDER` |

### 9.1 Install over whatever is installed (game closed only; backs up the installed files)

```
(
  CS2="$HOME/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
  BACKUP="$HOME/Code/cs2-junction-rules/.staged/0.2.3-prev-installed-backup"
  cd ~/Code/cs2-junction-rules/.staged/0.2.3-burst || exit 1
  if [ "$(ps -eo comm | grep -ci cities2)" != 0 ]; then echo "GAME IS RUNNING - stop, nothing installed"; exit 1; fi
  if [ -e STALE.md ]; then echo "STAGED BUILD IS STALE - see STALE.md, rebuild first; nothing installed"; exit 1; fi
  if [ ! -d "$CS2" ]; then echo "CS2 folder not found - nothing installed"; exit 1; fi
  sha256sum -c --strict - <<'SUMS' || { echo "SHA256 MISMATCH - nothing installed"; exit 1; }
021662c3d6626a4d5d58eecc4656fe0d07d607d9c72f915d16ca576f340ea627  JunctionRules.dll
c8cee663e68daed1b9384daf23bca3dbfd6690f14b5dc74cb5cde650da185079  JunctionRules.pdb
6ecb34d0a79522e77d254f70275eac310fb0c6b84812c61a32f11a1e7a27704d  JunctionRules_win_x86_64.dll
92bebfc176113e1962d61fa0c6fd9df4c82ec31f9a9acb7c5afdb1a7ca4ea1d7  JunctionRules_win_x86_64.pdb
SUMS
  if [ -d "$CS2/Mods/JunctionRules" ] && [ ! -e "$BACKUP/JunctionRules.dll" ]; then
    mkdir -p "$BACKUP" && cp -p "$CS2/Mods/JunctionRules/"* "$BACKUP/" && (cd "$BACKUP" && sha256sum JunctionRules*.dll JunctionRules*.pdb > SHA256SUMS) &&
    echo "backed up the installed files to $BACKUP" || { echo "BACKUP FAILED - nothing installed"; exit 1; }
  fi
  mkdir -p "$CS2/Mods/JunctionRules" &&
  cp JunctionRules.dll JunctionRules.pdb JunctionRules_win_x86_64.dll JunctionRules_win_x86_64.pdb "$CS2/Mods/JunctionRules/" &&
  echo "installed 0.2.3"
)
```

Copies nothing unless the game is closed, the build is not stale and all four sums match. The first run keeps the
installed files, whatever version they are (expected: the 0.2.1 Burst build), in `.staged/0.2.3-prev-installed-backup/`
with their sums in `SHA256SUMS`. An existing backup is never overwritten.

Log check (section 2) with `OnLoad version 0.2.3.0, copy made from game 1.6.4f1 (1097.158a)`; everything else as
section 2. Timing A/B as section 4 (rule Stock vs Whole junction, 1x and 4x, 5 lines each). Behaviour passes as
section 5, at Valley x Carlisle (1092.7,-81.5) and Sunset x Cherry.

### 9.2 What to watch (0.2.3)

- **Valley St right turns out across Carlisle are no longer bold.** A car turning right from the Valley St STOP arm
  goes only when BOTH carriageways are clear for the time it needs to reach them. It should not cut in front of
  far-carriageway traffic, and should not stop on the central reservation.
- **Carlisle through-traffic does not slow for Valley St cars approaching their STOP line** (JR-T-002g). A Carlisle
  car going through or turning at Valley x Carlisle keeps its speed while a Valley St car drives up to its STOP line;
  approach-lane cars count only when they have priority over the deciding car. The other way round stays: a Valley St
  car at its line still waits for Carlisle cars approaching on their lanes. Report any Carlisle car that brakes or
  stops with nothing on its path but a Valley St car still short of its line.
- **Safety-stop events are rare.** In the log line, `safety stops inside N (events N)`: the events number is the count
  of cars that went into a safety stop. Expect it to be a small fraction of `whole-path decisions` (well below 0.2.1).
  Report any car seen standing on a central reservation with the junction name. That is now only a safety stop, never
  a planned wait.
- **Entry waits are longer** (expected, owner accepted). `waited at entry spot for a later conflict` rises, most from
  STOP arms onto divided roads. Note the longest minor-arm wait at Valley x Carlisle per rule.
- **Minor-arm throughput.** Count cars out of the Valley St arm over the same 2 in-game minutes, Stock vs Whole
  junction. The arm should still empty between platoons. Starvation on a busy main road is accepted by ruling: report
  it, do not fix it.
- **No deadlocks.** No two cars each waiting for the other, and no car frozen in a junction after the traffic around
  it has gone.
- **No new collisions** between cars that had already gone.
- Timing: rule Whole junction vs Stock should differ by no more than the 0.2.1 A/B. The projection adds arithmetic
  per crossing and a short scan of the approach lanes while a car decides at W.

Diagnostics to read (copy's timing log line, **Log navigation timing** on):
- `whole-path decisions N`, `waited at entry spot for a later conflict N`.
- `safety stops inside N (events N)`: car-steps vs transitions; read the events.
- `arrival projection: busy only by projection, junction-lane car N, feeding-lane car N`: how often the projection,
  not the stock 2 s, held a car. Feeding-lane hits should be the larger at Valley x Carlisle (approaching Carlisle
  traffic holding Valley St cars; never the reverse since JR-T-002g).
- `points at W N, distance from W mean X m [histogram (median in a-b)], own arrival mean Y s [histogram (median in a-b)]`:
  the far carriageway should show up as distance ~12-24 m and arrival ~2.5-4 s.
- Gone: `waited at a mid-junction spot` (no mid spots in 0.2.3).

### 9.3 Rollback

- Immediate: **Junction gap rule** back to Stock (drives as stage 1), or **Car driving** back to Game's own.
- Back to the version that was installed before 0.2.3 (game closed), from the 9.1 backup, checked against the sums
  the install wrote:

  ```
  (
    CS2="$HOME/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
    cd ~/Code/cs2-junction-rules/.staged/0.2.3-prev-installed-backup || { echo "no backup - use the 0.2.1 block below"; exit 1; }
    if [ ! -d "$CS2/Mods/JunctionRules" ]; then echo "mod folder not found - nothing changed"; exit 1; fi
    if [ "$(ps -eo comm | grep -ci cities2)" != 0 ]; then echo "GAME IS RUNNING - stop, nothing changed"; exit 1; fi
    sha256sum -c --strict SHA256SUMS || { echo "BACKUP SHA256 MISMATCH - nothing changed"; exit 1; }
    cp JunctionRules.dll JunctionRules.pdb JunctionRules_win_x86_64.dll JunctionRules_win_x86_64.pdb "$CS2/Mods/JunctionRules/" &&
    echo "previous install restored"
  )
  ```

- Or the 0.2.1 Burst build from `.staged/0.2.1-burst/`, checked against the sums in the section 1 install block of
  THIS file, read the same way as 8.3:

  ```
  (
    CS2="$HOME/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
    KIT="$HOME/Code/cs2-junction-rules/PROBE-STAGE2.md"
    cd ~/Code/cs2-junction-rules/.staged/0.2.1-burst || exit 1
    if [ ! -d "$CS2/Mods/JunctionRules" ]; then echo "mod folder not found - nothing changed"; exit 1; fi
    if [ "$(ps -eo comm | grep -ci cities2)" != 0 ]; then echo "GAME IS RUNNING - stop, nothing changed"; exit 1; fi
    if [ -e STALE.md ]; then echo "0.2.1 BUILD IS STALE - see its STALE.md; nothing changed"; exit 1; fi
    SUMS21="$(awk '/<<.SUMS./{n++; if(n==1){on=1; next}} on && /^SUMS$/{exit} on' "$KIT")"
    if [ "$(printf '%s\n' "$SUMS21" | grep -c '  JunctionRules')" != 4 ] || printf '%s\n' "$SUMS21" | grep -q '^0000'; then
      echo "0.2.1 SUMS NOT FILLED IN (section 1 of PROBE-STAGE2.md) - nothing changed"; exit 1; fi
    printf '%s\n' "$SUMS21" | sha256sum -c --strict - || { echo "SHA256 MISMATCH - nothing changed"; exit 1; }
    cp JunctionRules.dll JunctionRules.pdb JunctionRules_win_x86_64.dll JunctionRules_win_x86_64.pdb "$CS2/Mods/JunctionRules/" &&
    echo "0.2.1 restored"
  )
  ```

  Further back: section 6 (0.2.0), PROBE-STAGE1.md (0.1.0 / remove).

Report back as section 7, plus: the counters line per pass (with the events and histogram parts), Valley St right-turn
behaviour, any car on a central reservation, longest minor-arm wait and cars out per 2 minutes per rule.

## 10. 0.2.4: red/amber feeding cars, cheap feeding scan, turning across oncoming gives way (JR-T-002h)

0.2.4 changes three things inside **Junction gap rule = Whole junction** (owner rulings 14 Nov 2026,
`docs/STAGE2-ANALYSIS.md` section 11, `DEVIATIONS.md` 0.2.4):
- **Red / amber feeding cars.** An approach-lane (feeding) car whose own conflicting junction lane shows RED no longer
  counts. On AMBER it is skipped when the game's own amber test says it can stop (braking distance at its current
  speed <= distance to its stop line), and still counts when it cannot stop (and wins priority). Green / no signal:
  as 0.2.3. Emergency vehicles (priority 108) ignore signals exactly as stock does, so they still count.
- **Cheap feeding scan.** Only the car at its entry spot W (no car ahead of it on its own approach lane) or the car
  re-checking while crossing scans the approach lanes; followers never do. No repeat scan of the same approach in one
  car-update (memo), early exits, and a work cap (96 cars per car-update, 16 lanes per scan; cars not read are NOT
  counted, cap hits are counted).
- **Turning across oncoming traffic gives way** (new setting, default ON, whole junction only, applied at the next
  simulation step). With equal sign status, a turn across the oncoming flow (left-hand traffic: right turns and
  right U-turns; right-hand traffic: the mirror, from the game's own traffic-side flag) has LOWER priority than the
  oncoming straight, and the straight car no longer waits for the turner's lane reservation alone. A turner already
  physically in the way is still braked for (stock collision braking). Setting off = 0.2.3.

Defaults: **Car driving** = Game's own, **Junction gap rule** = Stock. With the rule on Stock, 0.2.4 drives exactly as
stage 1 (the new setting has no effect then).

Build to install: `~/Code/cs2-junction-rules/.staged/0.2.4-burst/`. It holds a `STALE.md` (managed build made while
the game was running): do the Burst rebuild in `STALE.md` first (game closed), then put the new sha256s in the table
and in the install block below. Until then the install block refuses (placeholder sums `0000...` on purpose).

| File | sha256 |
|---|---|
| `JunctionRules.dll` | `PLACEHOLDER - see .staged/0.2.4-burst/STALE.md` |
| `JunctionRules.pdb` | `PLACEHOLDER` |
| `JunctionRules_win_x86_64.dll` (Burst code) | `PLACEHOLDER` |
| `JunctionRules_win_x86_64.pdb` | `PLACEHOLDER` |

### 10.1 Install over the installed version (game closed only; backs up the installed files)

```
(
  CS2="$HOME/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
  BACKUP="$HOME/Code/cs2-junction-rules/.staged/0.2.4-prev-installed-backup"
  cd ~/Code/cs2-junction-rules/.staged/0.2.4-burst || exit 1
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
    mkdir -p "$BACKUP" && cp -p "$CS2/Mods/JunctionRules/"* "$BACKUP/" && (cd "$BACKUP" && sha256sum JunctionRules*.dll JunctionRules*.pdb > SHA256SUMS) &&
    echo "backed up the installed files to $BACKUP" || { echo "BACKUP FAILED - nothing installed"; exit 1; }
  fi
  mkdir -p "$CS2/Mods/JunctionRules" &&
  cp JunctionRules.dll JunctionRules.pdb JunctionRules_win_x86_64.dll JunctionRules_win_x86_64.pdb "$CS2/Mods/JunctionRules/" &&
  echo "installed 0.2.4"
)
```

Copies nothing unless the game is closed, the build is not stale and all four sums match. The first run keeps the
installed files (expected: the 0.2.3 Burst build) in `.staged/0.2.4-prev-installed-backup/` with their sums in
`SHA256SUMS`. An existing backup is never overwritten.

Log check (section 2) with `OnLoad version 0.2.4.0`; the settings line names `turning across oncoming gives way`.
With the rule on Whole junction and the new setting on, the copy's rule name ends `, turning across oncoming gives
way (left-hand traffic)` in Bevy Valley. Timing A/B as section 4; behaviour passes as section 5 at Valley x Carlisle
(1092.7,-81.5) and a signalled junction on a main road (Sunset x Cherry).

### 10.2 What to watch (0.2.4)

Red / amber (signalled junctions):
- **Green main-road cars no longer brake for cars stopping at their red.** A car with green turning or crossing in
  front of a side road whose cars are rolling up to their red stop line keeps going. Report any green car that waits
  with nothing on its path but cars that then stop at a red.
- **Amber runners are still respected.** A car too close to stop when its light turns amber (it carries on through)
  still holds a conflicting car deciding at W. Watch a few light changes at speed: no green car pulls out in front of
  an amber runner. A car that could have stopped on amber is not waited for (expected).
- Red runners: a car that runs its red anyway (stock only lets it when it cannot stop within braking distance + 1 m)
  is NOT counted any more (owner ruling taken literally). Only the stock reservation, safety re-check and collision
  braking separate it. Report any near-collision with a red runner.
- Emergency vehicles on blue lights through a red are still counted (stock: they ignore the signal).

Cost of the approach-lane scan:
- **No post-load spike.** The first timing windows after loading the save should look like later windows. A/B the
  first 3 windows after load with **Car driving** = Game's own vs the copy (rule Whole junction) to see whether any
  spike left is the game's own (`docs/STAGE2-ANALYSIS.md` 11.2: the timing includes waits on upstream jobs).
- **Nav job mean / p95 equal to 0.2.1** (rule Whole junction, same save, same speed, 5 lines each at 1x and 4x).
  Report the numbers next to the 0.2.1 and 0.2.3 lines if you have them.
- **Feeding scans per window**, from the copy's line:
  `feeding scans N (skipped: follower N, memo N; cap hits N)`. Expect followers skipped >> scans in queues,
  `cap hits` 0 or near 0. Any steady cap hits: report the junction (the cap errs on "not busy").

Turning across oncoming (Valley x Carlisle, 1092.7,-81.5; and any junction with a busy turn across a main road):
- **A fast eastbound right-turner into Valley St waits for westbound Carlisle traffic** (equal status, both main-road
  approaches). It should not cut across a westbound car that is close.
- **Westbound Carlisle through traffic is not held by the turner.** A westbound car keeps going while an eastbound car
  is deciding or committed to the right turn; it no longer waits for the turner's reservation.
- **Watch for near-overlaps**: a turner already committed when a westbound car arrives is now separated only by
  collision braking (accepted risk). Report any visible overlap or two cars stopping nose to nose in the junction.
- STOP / YIELD / RightOfWay differences stay stock: a Valley St STOP-arm car still gives way to both Carlisle flows,
  whatever its turn. Left turns (LHT) are unaffected.
- Counters: `turning across oncoming: priority changed N, turner reservation not waited for N`. Both should be > 0 at
  Valley x Carlisle with the setting on and 0 with it off.
- **Setting off = 0.2.3**: switch it off for one pass; behaviour and counters as 0.2.3 (turn counters 0).

Also as 9.2: no deadlocks, no new collisions, safety-stop events rare, no car on a central reservation by plan.

### 10.3 Rollback

- Immediate (no restart): the new setting **Whole junction: turning across oncoming traffic gives way** off (0.2.3
  turn behaviour), **Junction gap rule** back to Stock (drives as stage 1), or **Car driving** back to Game's own.
- Back to the version installed before 0.2.4 (expected 0.2.3; game closed), from the 10.1 backup, checked against the
  sums the install wrote:

  ```
  (
    CS2="$HOME/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
    cd ~/Code/cs2-junction-rules/.staged/0.2.4-prev-installed-backup || { echo "no backup - use the 0.2.3 block below"; exit 1; }
    if [ ! -d "$CS2/Mods/JunctionRules" ]; then echo "mod folder not found - nothing changed"; exit 1; fi
    if [ "$(ps -eo comm | grep -ci cities2)" != 0 ]; then echo "GAME IS RUNNING - stop, nothing changed"; exit 1; fi
    sha256sum -c --strict SHA256SUMS || { echo "BACKUP SHA256 MISMATCH - nothing changed"; exit 1; }
    cp JunctionRules.dll JunctionRules.pdb JunctionRules_win_x86_64.dll JunctionRules_win_x86_64.pdb "$CS2/Mods/JunctionRules/" &&
    echo "previous install restored"
  )
  ```

- Or the 0.2.3 Burst build from `.staged/0.2.3-burst/`, checked against the sums in the section 9.1 install block of
  THIS file (no sums are copied here; the block refuses while 9.1 still has placeholders):

  ```
  (
    CS2="$HOME/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
    KIT="$HOME/Code/cs2-junction-rules/PROBE-STAGE2.md"
    cd ~/Code/cs2-junction-rules/.staged/0.2.3-burst || exit 1
    if [ ! -d "$CS2/Mods/JunctionRules" ]; then echo "mod folder not found - nothing changed"; exit 1; fi
    if [ "$(ps -eo comm | grep -ci cities2)" != 0 ]; then echo "GAME IS RUNNING - stop, nothing changed"; exit 1; fi
    if [ -e STALE.md ]; then echo "0.2.3 BUILD IS STALE - see its STALE.md; nothing changed"; exit 1; fi
    SUMS23="$(awk '/^### 9\.1 /{sec=1} sec && /<<.SUMS./{on=1; next} on && /^SUMS$/{exit} on' "$KIT")"
    if [ "$(printf '%s\n' "$SUMS23" | grep -c '  JunctionRules')" != 4 ] || printf '%s\n' "$SUMS23" | grep -q '^0000'; then
      echo "0.2.3 SUMS NOT FILLED IN (section 9.1 of PROBE-STAGE2.md) - nothing changed"; exit 1; fi
    printf '%s\n' "$SUMS23" | sha256sum -c --strict - || { echo "SHA256 MISMATCH - nothing changed"; exit 1; }
    cp JunctionRules.dll JunctionRules.pdb JunctionRules_win_x86_64.dll JunctionRules_win_x86_64.pdb "$CS2/Mods/JunctionRules/" &&
    echo "0.2.3 restored"
  )
  ```

  Further back: 9.3 (0.2.1), section 6 (0.2.0), PROBE-STAGE1.md (0.1.0 / remove).

Report back as section 7, plus: the counters line per pass (feeding scans / skips / cap hits, turn counters), the
first 3 windows after load (copy vs Game's own), nav mean/p95 vs 0.2.1, any green car braking for a red-stopper,
any amber-runner conflict, and Valley x Carlisle turner / westbound behaviour with the setting on and off.
