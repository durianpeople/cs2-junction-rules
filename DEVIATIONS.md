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
No hook point was added inside the copy for stage 1: registration is done from `Mod.cs` with `UpdateSystem` and
enabling is done from outside through `ComponentSystemBase.Enabled`. Stage 2 adds the marked blocks of section S2.

## S2: stage 2 marked blocks (JR-T-002, whole-junction gap decision)

Stage 2 code inside the copies sits only between `// JR-STAGE2 BEGIN <id>: <note>` and `// JR-STAGE2 END` lines.
Every block **only inserts lines**: no vanilla line is changed or removed. `python3 tools/vanilla_copy.py check`
removes the blocks and proves the rest equals decompile + the rules above; it also fails on an unbalanced, nested,
undeclared (not in `marked_blocks` of `tools/vanilla_rules.json`), miscounted or undocumented block. `generate` keeps
the blocks (re-inserted after the same three preceding generated lines; a non-unique match stops it). The logic lives
outside the copies: `JunctionRules/Core/WholePathRule.cs` (pure, unit-tested) and `JunctionRules/Stage2/` (job side
`JRWholeJunction`, main-thread `JRStage2Bridge`). With the setting on Stock and the timing log off (`m_Mode == 0`)
every block is inert (an int test, or a value copied and restored unchanged).

Owner rulings implemented (14 Nov 2026): stock only; stock boldness; whole junction at once; green turners wait
before the first oncoming lane and cross all oncoming lanes in one decision; generalised: at every junction wait just
before the FIRST path crossing (never inside another lane's path; node-edge conflict = the stop line), one decision
there for all remaining conflicts with stock judgement, no give-way stop inside after that, vanilla collision braking
stays, STOP = stock stop at the line then roll-up; default = this rule, "wait at the stop line" only as a non-default
variant (setting "Whole junction: waiting spot"). Analysis and flagged cases: `docs/STAGE2-ANALYSIS.md`.

Further owner rulings implemented (14 Nov 2026, JR-T-002b, version stays 0.2.0): **amber = live** (only a red
`LaneSignal` keeps a lane from placing the waiting spot; `WholePathRule.IsHeldByRed`); **roundabouts** one decision per
node lane, as built (no block touches the roundabout code); **decide only at the waiting spot / first in line decides**:
"committed" is now only "the car's own front is past its waiting spot" (`WholePathRule.IsCommitted(waitDistance,
margin)`), so no car commits from behind the spot through the braking-distance test, its reservation, a look-ahead lane
or a leader's go; a queued follower uses stock car-following, rolls up to the spot and decides there itself; **exit
queues** stay stock car-following (no room check; yellow boxes are stage 3); **no starvation safeguard**, ever.
Block change for this: S2-04 only (same 4 lines; the call no longer passes `origMinOffset` and the game's braking
distance). All other blocks are unchanged; the logic change is in `Core/WholePathRule.cs` and `Stage2/JRWholeJunction.cs`.

| Id | File | Sites | Lines inside | Where | What |
|---|---|---|---|---|---|
| S2-01 | iterator | 1 | 1 | after field `m_LaneOffsetPush` | Field `m_JR` (JRWholeJunction): the car's stage 2 state. |
| S2-02 | iterator | 2 | 1 each | first statement of both `IterateFirstLane` | `m_JR.m_FirstLane = true` (the lane checked next is the car's current lane). |
| S2-03 | iterator | 1 | 1 | first statement of `IterateNextLane` | `m_JR.m_FirstLane = false`. |
| S2-04 | iterator | 1 | 4 | `CheckOverlappingLanes`, before the overlap loop | `BeginLane`: waiting spot W of a junction (node) lane (red lanes excluded, amber live) and whether the car has committed (front past W only; JR-T-002b: no longer passes `origMinOffset` or the braking distance). |
| S2-05 | iterator | 1 | 2 | after `blockerType` in the loop | `jrStart` (overlap start seen from the car), `jrInScope` (at or after W and yield-checked). |
| S2-06 | iterator | 1 | 1 | after `maxBrakingSpeed` (blocking reservation) | Before commit: brake to W; after commit: no give-way (`float.MaxValue`). |
| S2-07 | iterator | 1 | 1 | after `maxBrakingSpeed3` (space request) | Same as S2-06. |
| S2-08 / S2-08b | iterator | 1 / 1 | 2 / 1 | around `CheckPedestrian` | Before commit the pedestrian check uses the distance to W; the game's `num5` is restored after. |
| S2-09 | iterator | 1 | 1 | after `GetObjectSpeed` in the object loop | Keeps the other car's position before the 2 s priority look-ahead. |
| S2-10 | iterator | 1 | 2 | after the look-ahead block | After commit the look-ahead is undone unless the other car is already past the overlap start (the game's own committed semantics); `jrProjected` = the car counts only through the look-ahead. |
| S2-11 / S2-11b | iterator | 1 / 1 | 3 / 2 | around `UpdateMaxSpeed` in `CheckOverlappingLanes` | Before commit: braking distance to W for projected cars and cars in a crossing (merge followers stay stock); `num5` restored after; debug counters. |
| S2-20 | nav | 1 | 5 | `UpdateNavigationJob` fields | `m_JRMode` (int, by value) and `m_JRCounters` (persistent `NativeArray<long>`, atomic adds, safety restriction off: debug-only counters). |
| S2-21 | nav | 1 | 1 | after the iterator is set up in `UpdateNavigationTarget` | `m_JR = JRWholeJunction.Create(m_JRMode, m_NodeLaneData)`. |
| S2-22 | nav | 1 | 4 | after `CheckBlocker` at `IL_09cd` | Counters, once per car-step, only when the count bit is set. |
| S2-23 | nav | 1 | 2 | `OnUpdate`, before `ScheduleParallel` | Main thread: takes this step's mode from `Stage2Handoff` (published by `CarDrivingSwitchSystem` earlier in the same step) into the job struct by value. |
| S2-24 | nav | 1 | 7 | new `OnDestroy` (none in vanilla) | Completes the job and frees the counter array. |

Total: 19 blocks, 43 lines inside (as `vanilla_copy.py check` reports).

Effect on `tools/IlHash --copy` (expected): 48 of 80 identical (was 54), 21 shape only (was 19), 11 content
differences (was 7: the four stage 2 methods `IterateFirstLane` x2, `IterateNextLane`, `UpdateNavigationTarget` join
`CheckOverlappingLanes` and the others listed below, which also contain blocks now), 1 extra (`OnDestroy`, S2-24).
Field blocks of the iterator and the job are "shape only" because of S2-01 and S2-20.

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
- **Dead zero store** (`flag2` in `JRCarLaneSpeedIterator.cs` line 1173 with the stage 2 blocks, 1159 before): a local assigned and never read; the build
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
