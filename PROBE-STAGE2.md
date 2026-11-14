# Probe kit: Junction Rules 0.2.0 (stage 2, whole-junction gap decision)

Goal: show in Bevy Valley that the whole-junction rule (a) costs no measurable time against the stock rule, both with
the copy driving, and (b) does what the owner ruled: a car that has to wait stops just before the first point where its
path crosses another lane's path, then goes through every remaining conflict in one go and never stops inside the
junction to give way (it still brakes so as not to hit anything).

Defaults change nothing: **Car driving** = Game's own, **Junction gap rule** = Stock. The rule only acts while Car
driving is the Junction Rules copy.

Build to install: `~/Code/cs2-junction-rules/.staged/0.2.0-burst/`. If that folder holds a `STALE.md`, it has only a
managed build (made while the game was running): do the Burst rebuild in `STALE.md` first (game closed), then put the
new sha256s in the table and in the install block below. Until then the install block refuses (the sums below are
placeholders on purpose).

| File | sha256 |
|---|---|
| `JunctionRules.dll` | `REBUILD-REQUIRED (see STALE.md)` |
| `JunctionRules.pdb` | `REBUILD-REQUIRED` |
| `JunctionRules_win_x86_64.dll` (Burst code) | `REBUILD-REQUIRED` |
| `JunctionRules_win_x86_64.pdb` | `REBUILD-REQUIRED` |

Built for game **1.6.4f1 (1097.158a)** only (same version gate as 0.1.0).

## 1. Install (game closed only; backs up the installed 0.1.0)

```
(
  CS2="$HOME/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
  BACKUP="$HOME/Code/cs2-junction-rules/.staged/0.1.0-installed-backup"
  cd ~/Code/cs2-junction-rules/.staged/0.2.0-burst || exit 1
  if [ "$(ps -eo comm | grep -ci cities2)" != 0 ]; then echo "GAME IS RUNNING - stop, nothing installed"; exit 1; fi
  if [ -e STALE.md ]; then echo "STAGED BUILD IS STALE - see STALE.md, rebuild first; nothing installed"; exit 1; fi
  if [ ! -d "$CS2" ]; then echo "CS2 folder not found - nothing installed"; exit 1; fi
  sha256sum -c --strict - <<'SUMS' || { echo "SHA256 MISMATCH - nothing installed"; exit 1; }
REBUILD-REQUIRED  JunctionRules.dll
REBUILD-REQUIRED  JunctionRules.pdb
REBUILD-REQUIRED  JunctionRules_win_x86_64.dll
REBUILD-REQUIRED  JunctionRules_win_x86_64.pdb
SUMS
  if [ -d "$CS2/Mods/JunctionRules" ] && [ ! -e "$BACKUP/JunctionRules.dll" ]; then
    mkdir -p "$BACKUP" && cp -p "$CS2/Mods/JunctionRules/"* "$BACKUP/" && echo "backed up the installed files to $BACKUP" || { echo "BACKUP FAILED - nothing installed"; exit 1; }
  fi
  mkdir -p "$CS2/Mods/JunctionRules" &&
  cp JunctionRules.dll JunctionRules.pdb JunctionRules_win_x86_64.dll JunctionRules_win_x86_64.pdb "$CS2/Mods/JunctionRules/" &&
  echo "installed 0.2.0"
)
```

Paste the whole block: it runs in a subshell, so a failed check ends only the subshell. It copies nothing unless the
game is closed, the build is not stale and all four sha256s match. The first run keeps the installed files (0.1.0) in
`.staged/0.1.0-installed-backup/` (an existing backup is never overwritten). The 0.1.0 sha256s are in PROBE-STAGE1.md
(`JunctionRules_win_x86_64.dll` `df74ad12...cedce337`).

Launch the game the usual way (Proton 11.0 stable), Bevy Valley.

## 2. Log checks

`$CS2/Logs/Modding.log`: `JunctionRules` loaded, no exception; `Loaded additional Burst code ...JunctionRules_win_x86_64.dll`
(without it the copy runs as slow managed code: stop and report).

`$CS2/Logs/JunctionRules.Mod.log` after the main menu:
- `OnLoad version 0.2.0.0, copy made from game 1.6.4f1 (1097.158a)`
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
`timing Junction Rules copy: frames a-b (1024 sim frames in S s = R frames/s), cars C, navigation job schedule-to-done ms over N runs: mean .. p50 .. p95 .. max .. (skipped k); junction gap rule stock; car-steps: whole-path decisions D, waited at spot for a later conflict W, stopped inside for a give-way I, stopped inside behind a crossing car X`

Counters are car navigation updates (each car is updated every 16 frames), summed over the window:
- **whole-path decisions**: updates of cars before their waiting spot on a junction lane with the rule on (0 on stock).
- **waited at spot for a later conflict**: updates of cars standing at their waiting spot because a conflict AFTER the
  first one was busy (0 on stock).
- **stopped inside for a give-way**: updates of cars standing inside a junction (front past their waiting spot) held by
  a give-way. Counted on both rules: expect a real number on stock and about 0 with whole junction.
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
- W `mean` and `p95` within about 10 % of S at the same speed (S3 shows the normal drift); S close to the stage 1
  copy numbers (1x mean ~0.55 / p95 ~0.68 ms; 4x ~0.6 / ~1.05 ms).
- `frames/s` and FPS the same within noise; `skipped` 0 or near it.
- Each line says the rule that ran (`stock` / `whole junction`).
- Counters: `stopped inside for a give-way` clearly lower with whole junction (about 0); `waited at spot for a later
  conflict` > 0 with whole junction, 0 on stock.

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
- **STOP arms**: full stop at the line as before, then roll up to the waiting spot if it is further in, then one go.
- **Minor arms still get out**: note roughly how long the longest wait is on each rule. Longer waits with Whole
  junction are expected on a busy major road (it needs both directions clear at once); report a minor arm that never
  gets out.
- **No new jams or deadlocks**: no cars frozen facing each other, no queue that never clears; red/green obeyed.
- No cars driving through each other; no new despawns; `JunctionRules.Mod.log`, `Modding.log`, `Player.log` free of
  new exceptions.
- Optional: one extra 6-line pass with **Whole junction: waiting spot = At the stop line** at one junction (turners then
  wait at the line). Set it back to the default after.

## 6. Rollback

- Immediate: **Junction gap rule** back to Stock (next step); or **Car driving** back to Game's own.
- Back to 0.1.0 (game closed):

  ```
  (
    CS2="$HOME/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
    BACKUP="$HOME/Code/cs2-junction-rules/.staged/0.1.0-installed-backup"
    if [ ! -d "$CS2/Mods" ]; then echo "CS2 folder not found - nothing changed"; exit 1; fi
    if [ "$(ps -eo comm | grep -ci cities2)" != 0 ]; then echo "GAME IS RUNNING - stop, nothing changed"; exit 1; fi
    if [ ! -e "$BACKUP/JunctionRules_win_x86_64.dll" ]; then echo "no 0.1.0 backup - nothing changed"; exit 1; fi
    cp -p "$BACKUP/"* "$CS2/Mods/JunctionRules/" && echo "0.1.0 restored"
  )
  ```

- Remove the mod entirely: the rollback block of PROBE-STAGE1.md, section 7. The settings file
  `$CS2/JunctionRules.coc` keeps the two new settings; 0.1.0 ignores them. The mod adds no data to saves.

## 7. Report back

The three log lines of section 2; per pass the 5 timing lines (or mean/p95/frames per second, and the four counters)
and FPS; per junction and rule: where waiting cars stood, any car stopped inside to give way, turners' behaviour, STOP
roll-up, longest minor-arm wait, jams/deadlocks; any exception text.
