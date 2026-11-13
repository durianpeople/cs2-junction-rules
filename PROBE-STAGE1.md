# Probe kit: Junction Rules 0.1.0 (stage 1, parity test)

Goal: show in the game that the Junction Rules copy of the car-driving system is as fast as the game's own and
behaves the same. Stage 1 adds no features. The setting defaults to the game's own system, so installing the mod
changes nothing until **Car driving** is switched.

Build to install: `~/Code/cs2-junction-rules/.staged/0.1.0-burst/`. If that folder holds a `STALE.md`, the staged
build predates a code fix: rebuild it first with the game closed (command in `STALE.md`) and use the new sha256s.

| File | sha256 |
|---|---|
| `JunctionRules.dll` | `08c48ac3a206ad49c22cef6e4962184623b174db08e27e006da618a8bae21efa` |
| `JunctionRules.pdb` | `6ec437e94efd6cdb0b05d98fff24e1f72a6d98276a2e24f061cbcb999ea209c2` |
| `JunctionRules_win_x86_64.dll` (Burst code) | `0103b33c40e1d387ac8a27de789bdd4f99dc32c58ac89fbfa53fc6950bdb7288` |
| `JunctionRules_win_x86_64.pdb` | `95a45e9368251bb2a50c87a7dcbc26a8e0ef69c6ba9cde16aa7494be54347029` |

Built for game **1.6.4f1 (1097.158a)** only. On any other version the mod keeps the game's own system and says so.

## 1. Install (game closed only)

```
(
  CS2="$HOME/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
  cd ~/Code/cs2-junction-rules/.staged/0.1.0-burst || exit 1
  if pgrep -f 'Cities2[.]exe' >/dev/null; then echo "GAME IS RUNNING - stop, nothing installed"; exit 1; fi
  if [ -e STALE.md ]; then echo "STAGED BUILD IS STALE - see STALE.md, rebuild first; nothing installed"; exit 1; fi
  if [ ! -d "$CS2" ]; then echo "CS2 folder not found - nothing installed"; exit 1; fi
  sha256sum -c --strict - <<'SUMS' || { echo "SHA256 MISMATCH - nothing installed"; exit 1; }
08c48ac3a206ad49c22cef6e4962184623b174db08e27e006da618a8bae21efa  JunctionRules.dll
6ec437e94efd6cdb0b05d98fff24e1f72a6d98276a2e24f061cbcb999ea209c2  JunctionRules.pdb
0103b33c40e1d387ac8a27de789bdd4f99dc32c58ac89fbfa53fc6950bdb7288  JunctionRules_win_x86_64.dll
95a45e9368251bb2a50c87a7dcbc26a8e0ef69c6ba9cde16aa7494be54347029  JunctionRules_win_x86_64.pdb
SUMS
  mkdir -p "$CS2/Mods/JunctionRules" &&
  cp JunctionRules.dll JunctionRules.pdb JunctionRules_win_x86_64.dll JunctionRules_win_x86_64.pdb "$CS2/Mods/JunctionRules/" &&
  echo "installed"
)
```

The whole block runs in a subshell `( ... )`: paste it whole; a failed check ends only the subshell, never your
terminal. It copies nothing unless the game is closed, the build is not stale and all four sha256s match the list in
the block (which must equal the table above; update both after a rebuild).
(`'Cities2[.]exe'` matches the game's process but never the shell that runs the check.)

Do not install while the game runs. Launch the game the usual way (Proton 11.0 stable).

## 2. Check the logs after the main menu appears

`$CS2` below is the game folder set on the first line of the install block.

`$CS2/Logs/Modding.log`:
- `JunctionRules` is listed as loaded, no exception next to it.
- `Loaded additional Burst code ...JunctionRules_win_x86_64.dll`. Without this line the copy would run as slow
  managed code: stop and report.

`$CS2/Logs/JunctionRules.Mod.log`:
- `OnLoad version 0.1.0.0, copy made from game 1.6.4f1 (1097.158a)`
- `version check: Pass: game version '1.6.4f1 (1097.158a)' and vanilla code hash 5a15f3b9a5f9b2db match`
- `settings: car driving GameOwn, timing log off, skip code hash check off`

On any verdict other than `Pass`/`PassHashSkipped` the log also says `Junction Rules copy and timing probes not
registered`: the copy is then not loaded at all, Car driving always runs the game's own system and Log navigation
timing does nothing.

If the version check says `CodeMismatch` (version matches, hash differs): the game's runtime may hash IL slightly
differently from the offline tool. Copy the logged `vanilla code hash` lines into the report, then turn on
**Options > Junction Rules > Skip code hash check (probe aid)** and restart the game. Never do this on
`VersionMismatch`: then the copy is for another game version and must be re-made.

After loading a city: `car driving starts with game's own (game nav on/actions on, copy nav off/actions off)` and
`navigation timing log off`. Options > Junction Rules > Status then says `Copy: available`; `Copy: creation failed,
cannot run` or `Copy: not registered, cannot run` means Car driving stays on the game's own system: stop and report
(with any exception from `JunctionRules.Mod.log`).

## 3. Before the A/B passes

The stage 1 probe runs on **Bevy Valley only**. A large-city check (e.g. Sirater) is deferred to a later stage.

- **Gap Acceptance: City-wide OFF** for the timing passes (Options > Gap Acceptance). Gap Acceptance does not
  reference the navigation system and works with either pair, but city-wide mode adds its own per-frame work and
  holds cars, which adds noise to a timing comparison. The opt-in list may stay. The final pass (E in section 4)
  runs with it back ON. If a STOP/YIELD jam appears at Valley x Carlisle during any pass, report it to the Gap
  Acceptance session as usual.
- Options > Junction Rules > **Log navigation timing** ON. Every 1024 simulation frames the mod log gets one line for
  the system that ran in that window:
  `timing game's own CarNavigationSystem: frames a-b (1024 sim frames in S s = R frames/s), cars C, navigation job schedule-to-done ms over N runs: mean .. p50 .. p95 .. max .. (skipped k)`
  or the same starting `timing Junction Rules copy`. Both are measured the same way (from the end of the scheduling
  to the end of the navigation job, upstream waits included, which are the same for both).
- Same camera spot, same graphics settings, no building during a pass.

Switching: Options > Junction Rules > **Car driving**: `Game's own` / `Junction Rules copy`. It applies at the next
simulation step, also while the city runs; the log says `car driving switched to ...`. A window that straddles a
switch has samples of only one system; skip the first line after each switch.

**Same in-game duration for every pass:** a pass is 6 timing lines (skip the first, keep 5). Each line is exactly 1024
simulation frames, so every pass covers the same in-game time at either speed.

## 4. Speed and timing: game's own vs copy (Bevy Valley)

At the owner's speeds, the in-game speed buttons **1x** and **4x**; Gap Acceptance City-wide OFF except pass E.

| Pass | Speed | Car driving | Record |
|---|---|---|---|
| A1 | 1x | Game's own | 5 timing lines; FPS over the pass |
| B1 | 1x | Junction Rules copy | 5 timing lines; FPS over the pass |
| A2 | 4x | Game's own | 5 timing lines; FPS over the pass |
| B2 | 4x | Junction Rules copy | 5 timing lines; FPS over the pass |
| A3 | 4x | Game's own | 5 timing lines (checks drift between A2 and B2) |
| C | 1x, then 4x | copy selected **before** loading the save | load log lines; 5 timing lines at each speed |
| E | 4x | copy, Gap Acceptance City-wide back **ON** | 5 timing lines; then watch the section 5 junctions with the copy |

Pass C also exercises the load phase: the copy is registered in the game's load simulation too, so loading the save
with the copy selected must look the same as with the game's own (cars appear where they were, no burst of jams).

What passes:
- Copy `mean` and `p95` within about 10 % of the game's own at the same speed (Burst on both sides; A3 shows the
  normal drift).
- `frames/s` (simulation rate) the same within noise; FPS the same within noise.
- `skipped` stays 0 or near it. A copy 2x slower or more means its Burst code is not running: check the Modding.log
  Burst line.

## 5. Behaviour check at known Bevy Valley junctions

Watch each junction for the same in-game time (6 timing lines) once with `Game's own` and once with
`Junction Rules copy`, same speed, same camera spot:

- **Valley x Carlisle** at (1092.7, -81.5): a STOP/YIELD junction that Gap Acceptance holds (no sign, lane or
  connection changes there during tests).
- **1-2 busy signalised junctions or merges: the owner picks them** (the repo docs name no others).

Compare between the two settings, per junction:
- Queues: length and how fast they clear.
- Stuck cars: none frozen, no new gridlock; cars stop at red and go on green; STOP/YIELD obeyed as before.
- Odd lane changes: lane changes and lane reservations look the same; no cars driving through each other.
- Despawns: no cars vanishing that the game's own pass does not also show.
- Also: parking, bus stops, delivery trucks, trailers behave as before; `JunctionRules.Mod.log`, `Modding.log` and
  `Player.log` stay free of new exceptions.

## 6. ClearTheWay siren check

ClearTheWay runs right before the car-driving slot. With the copy active, follow an emergency vehicle with its siren
on (fire engine, ambulance or police car on a call). Other cars must pull aside / let it through as with the game's
own system. `Logs/ClearTheWay.log` must show no new errors.

## 7. Rollback

- Immediate: set **Car driving** back to `Game's own` (applies at the next step).
- Full: quit the game, then remove the mod folder (self-contained; only runs with the game closed and refuses when the
  game folder is not found, so it never removes a path built from an empty variable):

  ```
  (
    CS2="$HOME/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
    if [ -z "$CS2" ] || [ ! -d "$CS2/Mods" ]; then echo "CS2 folder not found - nothing removed"; exit 1; fi
    if pgrep -f 'Cities2[.]exe' >/dev/null; then echo "GAME IS RUNNING - stop, nothing removed"; exit 1; fi
    if [ ! -d "$CS2/Mods/JunctionRules" ]; then echo "not installed"; exit 0; fi
    rm -r -- "$CS2/Mods/JunctionRules" && echo "removed"
  )
  ```

  The settings file is `$CS2/JunctionRules.coc` (same place as Gap Acceptance's `GapAcceptance.coc`; may be left).
  The mod adds no data to saves; saves load the same without it.

## 8. Report back

Per pass: the 5 timing lines (or their mean/p95/frames per second) and FPS; per junction: queues, stuck cars, odd
lane changes and despawns for each setting; the siren check result; the three version-check lines from section 2;
any exception text.
