# Junction Rules (working name)

A Cities: Skylines II mod. Stage 1 (0.1.0): switch off the game's car-driving system
(`Game.Simulation.CarNavigationSystem` and its `Actions` system) and run an unchanged, Burst-compiled copy of it
(plus `CarLaneSpeedIterator`) in the same slot, switchable in game, to prove the copy is as fast and behaves the same.
No new driving rules yet.

## Layout

| Path | What |
|---|---|
| `JunctionRules/Vanilla/` | Copied game code only (generated, do not edit by hand). Each file has a header with its source. |
| `JunctionRules/Core/` | Game-independent logic: version gate, driving switch, timing window, IL hasher. Unit-tested. |
| `JunctionRules/Systems/` | `CarDrivingSwitchSystem` (chooses the pair each step), `CopyPair` (the only place that registers/creates the copy), navigation timing probes. |
| `JunctionRules/Settings/` | Options page and English texts. |
| `JunctionRules/Mod.cs` | Version gate and system registration. |
| `tools/vanilla_copy.py`, `tools/vanilla_rules.json` | Makes the copy from the decompile and proves it unchanged. |
| `tools/IlHash/` | Prints the vanilla code hash used by the version gate; `--copy` compares the copy's IL with Game.dll. |
| `tests/JunctionRules.Tests/` | Console test runner. |
| `DEVIATIONS.md`, `REFERENCERS.md`, `PROBE-STAGE1.md` | Every change to the game code; who uses the copied types; in-game test kit. |

## Settings

- **Car driving**: `Game's own` (default) or `Junction Rules copy`. Applies at the next simulation step. The copy only
  runs if the version gate passed at game start.
- **Status**: what runs now and the version check result.
- **Log navigation timing** (debug): one line per 1024 simulation frames with the navigation job timing of the pair
  that ran. Off: the probe systems are disabled and cost nothing.

When the version gate does not allow the copy, the copy pair and the timing probes are not registered at all (their
types are only named inside `Systems/CopyPair.cs`, behind NoInlining methods), so a game patch that renames something
the copy uses cannot break loading; the switch then only keeps the game's own pair running.
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
must not happen while the game runs there under Proton 11.0. The build checks `pgrep -f 'Cities2[.]exe'` first and fails
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
python3 tools/vanilla_copy.py check        # copy == decompile + documented rules; 0 unexplained differences
python3 tools/vanilla_copy.py selftest     # the diff script's own tests
cd tools/IlHash && PATH=~/.local/opt/dotnet:$PATH DOTNET_ROOT=~/.local/opt/dotnet dotnet run -c Release                       # vanilla code hash
cd tools/IlHash && PATH=~/.local/opt/dotnet:$PATH DOTNET_ROOT=~/.local/opt/dotnet dotnet run -c Release -- --copy ../../JunctionRules/bin/Release/JunctionRules.dll
```

`vanilla_copy.py` defaults to the decompile in `~/Code/decomp/yb-2026-11-13/full`; `--decomp DIR` changes it.
The test runner takes group-name filters and `--list`.

## After a game patch

See DEVIATIONS.md, "Updating after a game patch". Until the copy is re-made, the version gate keeps the game's own
system running on the new version.
