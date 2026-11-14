# Probe kit: Junction Rules 0.2.1 (stage 2, whole-junction gap decision + collision fix)

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

The three log lines of section 2; per pass the 5 timing lines (or mean/p95/frames per second, and the four counters)
and FPS (all six counters); per junction and rule: any collision between cars that had gone, safety stops seen, cars waiting on a central reservation, where waiting cars stood, any car stopped inside to give way, turners' behaviour
(second turner stopping at the spot), STOP roll-up, longest minor-arm wait, jams/deadlocks; the optional passes if
done; any exception text.
