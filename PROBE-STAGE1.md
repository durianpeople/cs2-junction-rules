# Probe kit: Junction Rules 0.1.0 (stage 1, parity test)

Goal: show in the game that the Junction Rules copy of the car-driving system is as fast as the game's own and
behaves the same. Stage 1 adds no features. The setting defaults to the game's own system, so installing the mod
changes nothing until **Car driving** is switched.

Build to install: `~/Code/cs2-junction-rules/.staged/0.1.0-burst/`

| File | sha256 |
|---|---|
| `JunctionRules.dll` | `08c48ac3a206ad49c22cef6e4962184623b174db08e27e006da618a8bae21efa` |
| `JunctionRules.pdb` | `6ec437e94efd6cdb0b05d98fff24e1f72a6d98276a2e24f061cbcb999ea209c2` |
| `JunctionRules_win_x86_64.dll` (Burst code) | `0103b33c40e1d387ac8a27de789bdd4f99dc32c58ac89fbfa53fc6950bdb7288` |
| `JunctionRules_win_x86_64.pdb` | `95a45e9368251bb2a50c87a7dcbc26a8e0ef69c6ba9cde16aa7494be54347029` |

Built for game **1.6.4f1 (1097.158a)** only. On any other version the mod keeps the game's own system and says so.

## 1. Install (game closed only)

```
pgrep -f Cities2.exe && echo "GAME IS RUNNING - stop here" || echo "game closed, ok"
CS2="$HOME/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
cd ~/Code/cs2-junction-rules/.staged/0.1.0-burst && sha256sum JunctionRules*      # compare with the table above
mkdir -p "$CS2/Mods/JunctionRules"
cp JunctionRules.dll JunctionRules.pdb JunctionRules_win_x86_64.dll JunctionRules_win_x86_64.pdb "$CS2/Mods/JunctionRules/"
```

Do not install while the game runs. Launch the game the usual way (Proton 11.0 stable).

## 2. Check the logs after the main menu appears

`$CS2/Logs/Modding.log`:
- `JunctionRules` is listed as loaded, no exception next to it.
- `Loaded additional Burst code ...JunctionRules_win_x86_64.dll`. Without this line the copy would run as slow
  managed code: stop and report.

`$CS2/Logs/JunctionRules.Mod.log`:
- `OnLoad version 0.1.0.0, copy made from game 1.6.4f1 (1097.158a)`
- `version check: Pass: game version '1.6.4f1 (1097.158a)' and vanilla code hash 5a15f3b9a5f9b2db match`
- `settings: car driving GameOwn, timing log off, skip code hash check off`

If the version check says `CodeMismatch` (version matches, hash differs): the game's runtime may hash IL slightly
differently from the offline tool. Copy the logged `vanilla code hash` lines into the report, then turn on
**Options > Junction Rules > Skip code hash check (probe aid)** and restart the game. Never do this on
`VersionMismatch`: then the copy is for another game version and must be re-made.

After loading a city: `car driving starts with game's own (game nav on/actions on, copy nav off/actions off)` and
`navigation timing log off`.

## 3. Before the A/B passes

- **Gap Acceptance: City-wide OFF** for the timing passes (Options > Gap Acceptance). Gap Acceptance does not
  reference the navigation system and works with either pair, but city-wide mode adds its own per-frame work and
  holds cars, which adds noise to a timing comparison. The opt-in list may stay. Pass E below runs with it back ON.
  If a STOP/YIELD jam appears at Valley x Carlisle during any pass, report it to the Gap Acceptance session as usual.
- Options > Junction Rules > **Log navigation timing** ON. Every 1024 simulation frames the mod log gets one line for
  the system that ran in that window:
  `timing game's own CarNavigationSystem: frames a-b (1024 sim frames in S s = R frames/s), cars C, navigation job schedule-to-done ms over N runs: mean .. p50 .. p95 .. max .. (skipped k)`
  or the same starting `timing Junction Rules copy`. Both are measured the same way (from the end of the scheduling
  to the end of the navigation job, upstream waits included, which are the same for both).
- Same camera spot, same graphics settings, no building during a pass.

## 4. A/B passes

Switching: Options > Junction Rules > **Car driving**: `Game's own` / `Junction Rules copy`. It applies at the next
simulation step, also while the city runs; the log says `car driving switched to ...`. A window that straddles a
switch has samples of only one system; skip the first line after each switch.

| Pass | City | Speed | Car driving | Record |
|---|---|---|---|---|
| A1 | Sirater | 3 | Game's own | wait 1 line, then 5 timing lines; FPS over 60 s |
| B1 | Sirater | 3 | Junction Rules copy | skip 1 line, then 5 timing lines; FPS over 60 s |
| A2 | Sirater | 3 | Game's own | 5 timing lines (checks drift between A1 and B1) |
| B2 | Sirater | 3 | Junction Rules copy | 5 timing lines; then 10 minutes of watching (section 5) |
| C | Bevy Valley | 1 and 3 | copy selected **before** loading the save | load log lines; 5 timing lines; watching (section 5) |
| D | Bevy Valley | 3 | Game's own, then copy | 3 lines each |
| E | Sirater | 3 | copy, Gap Acceptance City-wide back ON | 5 timing lines; 5 minutes of watching |

Pass C also exercises the load phase: the copy is registered in the game's load simulation too, so loading a save with
the copy selected must look the same as with the game's own (cars appear where they were, no burst of jams).

## 5. What to measure and what passes

Speed (from the timing lines, Sirater at speed 3):
- Copy `mean` and `p95` within about 10 % of the game's own (Burst on both sides; the A2 pass shows the normal drift).
- `frames/s` (simulation rate) the same within noise; FPS the same within noise.
- `skipped` stays 0 or near it. A copy 2x slower or more means its Burst code is not running: check the Modding.log
  Burst line.

Behaviour (watch with the copy active, busy junctions and a highway):
- Cars stop at red lights and go on green; STOP/YIELD signs are obeyed as before.
- Lane changes and lane reservations at junctions look normal; no cars driving through each other.
- Parking, bus stops, delivery trucks, trailers behave as before.
- No new gridlock compared with the game's own pass; no frozen cars.
- `JunctionRules.Mod.log`, `Modding.log` and `Player.log` stay free of new exceptions.

**ClearTheWay check** (ClearTheWay runs right before the car-driving slot): with the copy active, follow an emergency
vehicle with its siren on (fire engine, ambulance or police car on a call). Other cars must pull aside / let it
through as with the game's own system. `Logs/ClearTheWay.log` must show no new errors.

## 6. Rollback

- Immediate: set **Car driving** back to `Game's own` (applies at the next step).
- Full: quit the game, check `pgrep -f Cities2.exe`, then remove the mod folder:
  `rm -r "$CS2/Mods/JunctionRules"`. The settings file is `$CS2/ModsSettings/JunctionRules/JunctionRules.coc`
  (may be left). The mod adds no data to saves; saves load the same without it.

## 7. Report back

Per pass: the 5 timing lines (or their mean/p95/frames per second), FPS, and anything odd seen while watching; the
three version-check lines from section 2; any exception text.
