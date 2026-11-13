# Deviations of the copy from the game's code

The copy in `JunctionRules/Vanilla/` is the ILSpy decompile of Game.dll 1.6.4f1 (1097.158a) [6360.18886]
(sha256 `f076df4b70aa0f21a80345e03a052ca2273bc6754f41dab7e63d837837a43745`, ilspycmd 8.2.0.7535) with exactly the
rules below applied. `tools/vanilla_rules.json` holds them in machine form; `python3 tools/vanilla_copy.py check`
proves the copy equals decompile + rules and that every rule id is listed here.

| Copied file | Original type |
|---|---|
| `JunctionRules/Vanilla/JRCarNavigationSystem.cs` | `Game.Simulation.CarNavigationSystem` with all nested types (Actions system, UpdateNavigationJob, UpdateLaneSignalsJob, UpdateLaneReservationsJob, TrafficAmbienceEffect, ApplyTrafficAmbienceJob, ApplyLaneEffectsJob, TypeHandle) |
| `JunctionRules/Vanilla/JRCarLaneSpeedIterator.cs` | `Game.Simulation.CarLaneSpeedIterator` |

## Renames and namespace

| Id | Change | Where | Why |
|---|---|---|---|
| R-01 | `CarNavigationSystem` -> `JRCarNavigationSystem` (whole word) | nav file, 2 sites | Own type name so the game's system and the copy can both exist in one World. |
| R-02 | `CarLaneSpeedIterator` -> `JRCarLaneSpeedIterator` (whole word) | nav file 4 sites, iterator file 1 | The copy's navigation job must call the copied iterator, not the game's. |
| R-03 | `namespace Game.Simulation;` -> `namespace JunctionRules.Vanilla;` plus `using Game;` and `using Game.Simulation;` | both files | Copied types live in the mod's namespace. The two usings restore exactly the lookups the old namespace gave (an ambiguity would be a compile error, never a silent rebind). |

## Code deviations

| Id | Kind | Change | Sites | Why |
|---|---|---|---|---|
| D-01 | decompiler artefact correction | regex `(\(\|, \|= )0f - ` -> `\1-` | nav 13, iterator 20 | ILSpy 8.2 prints the IL float negation `neg` as `0f - x`, which differs in the sign of zero (`-(+0)` is `-0`, `0f - (+0)` is `+0`). Restores the game's unary minus. Checked with `tools/IlHash --copy`: all 33 sites are `neg` in Game.dll and each is followed by a primary expression, so `-x` parses as the original. |

No access workaround was needed: everything the copy uses is public in Game.dll / Unity (including the public
`InternalCompilerInterface` used by the code-generated parts and the public fields of the Actions system).
No hook point was added inside the copy: registration is done from `Mod.cs` with `UpdateSystem` and enabling is done
from outside through `ComponentSystemBase.Enabled`.

## Build note (not a code change)

| Id | What | Why |
|---|---|---|
| B-01 | The project compiles against the game's own class libraries (`Cities2_Data/Managed/mscorlib.dll`, `System.dll`, `System.Core.dll`, `netstandard.dll`) instead of the .NET Framework 4.7.2 reference pack (target `UseGameClassLibrariesOnly` in the csproj, as the official Mod.props does). | The iterator uses `System.MathF`, which exists in the game's runtime but not in the net472 reference assemblies. With the game's libraries the copy compiles unchanged. |

## Known decompiler artefacts left as they are (equivalent)

Found with `tools/IlHash --copy` (compares the built copy's IL with Game.dll method by method; result for this copy:
54 of 80 methods or field blocks identical, 7 of them after canonical renumbering of locals and branch labels;
19 "shape only"; 7 differ in content, all listed here). None changes behaviour, so they are not patched (patching
them would make the copy harder to diff against later decompiles).

"Shape only" is a weaker check than "identical": that class compares only the sorted multiset of member/type
references (calls, fields, types, casts, constructors), strings and numeric constants. It ignores branches, local
loads/stores, `dup`/`pop` **and arithmetic and comparison opcodes** (`add`, `mul`, `clt`, `ceq`, `and`, `or`, ...), so
a swapped operator or comparison would not show there. Those 19 methods were therefore also read side by side (see the
bullets below); the copy's source is in any case proved equal to the decompile by `tools/vanilla_copy.py check`.

- **Short-circuit `&&`/`||` for the game's non-short-circuit `&`/`|`** (10 operators in 5 methods: 9 `and` + 1 `or`,
  in UpdateNavigationJob.FillNavigationPaths, UpdateNavigationTarget and the iterator's CheckOverlappingLanes,
  CheckParkingLane, UpdateMaxSpeed): the game evaluates both operands without a branch (`and`/`or`); the decompile
  short-circuits. Every right-hand side is a side-effect-free comparison of locals/fields, so the result is identical;
  only the branch shape differs (Burst often turns both into the same code).
- **CheckCurrentLane x2** (the iterator's two overloads): branch layout and boolean-constant placement only (the
  compiler materialises `true`/`false` in a different place around the same tests); same comparisons, same calls and
  fields, same result.
- **Dead zero store** (`flag2` in `JRCarLaneSpeedIterator.cs` line 1159): a local assigned and never read; the build
  warns CS0219 about it. The game's IL has no such store. No effect.
- **Control-flow shape and local numbering**: a different C# compiler (Roslyn here vs Unity's for the game) lays out
  branches and locals differently. Same operations in the same order.

## Updating after a game patch

1. Decompile the new Game.dll (`ilspycmd -p -o <dir>/full Game.dll`).
2. `python3 tools/vanilla_copy.py compare-decomp <old full/> <new full/>` shows what the game changed in the two files.
3. Update `made_from` in `tools/vanilla_rules.json`, run `python3 tools/vanilla_copy.py generate`, then `check`.
   A rule whose count no longer matches fails loudly.
4. Update `MadeFrom` in `JunctionRules/Core/VersionGate.cs` (game version and the hash printed by `tools/IlHash`).
5. Re-run `tools/IlHash --copy` and review any new content difference before adding it here.
