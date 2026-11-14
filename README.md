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

Stage 3 probe (0.3.0-probe): a **yellow box paint test**, visual only and default off. With **Yellow box paint test** on,
the mod draws a UK yellow box (TSRGD diagram 1043; TSM chapter 5 dimensions: 200 mm border, 150 mm diagonals, 2 m /
2.5 m spacing) on the junctions in `ModsData/JunctionRules/paint-test.json`, with the game's own surface shader and its
own buffers (option E of `docs/STAGE3-PAINT-RESEARCH.md`). Draw only: nothing goes into the city or the save, driving is
unchanged. Off = the system does not run. Any failure logs one line and switches the test off for the session.
In-game test: PROBE-STAGE3-PAINT.md.

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
| `docs/STAGE2-ANALYSIS.md` | Where the game decides conflict by conflict, the change, per-case table, deadlock/starvation, owner rulings. `docs/vanilla_copy_*_0.2.0.txt` ... `*_0.3.0.txt`: the diff script's output per version. |

## Settings

- **Car driving**: `Game's own` (default) or `Junction Rules copy`. Applies at the next simulation step. The copy only
  runs if the version gate passed at game start.
- **Junction gap rule** (stage 2): `Stock (conflict by conflict)` (default) or `Whole junction`. Acts only while
  Car driving is the copy; applies at the next simulation step (the switch system publishes it each step, the copy
  takes it by value into its job when it schedules).
- **Whole junction: waiting spot**: `Before the first crossing` (default, owner ruling) or `At the stop line`.
- **Whole junction: turning across oncoming traffic gives way** (0.2.4): on (default) or off (= 0.2.3). Acts only
  with the Whole junction rule; applies at the next simulation step; traffic side from the game's own city setting.
- **Yellow box paint test** (stage 3 probe): off (default) or on; **what to draw** (Box / Plain fill / Outline only);
  **paint onto** (Roads (default) / Roads + Other / Everything but vehicles); buttons Refresh test list, Add junction at
  camera centre, Log paint cost now; Paint test status. See PROBE-STAGE3-PAINT.md.
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
