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
outside the copies: `JunctionRules/Core/WholePathRule.cs` and `StretchRule.cs` (pure, unit-tested) and `JunctionRules/Stage2/` (job side
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

0.2.1 (JR-T-002c, owner ruling 14 Nov 2026, binding: stretches and the safety re-check). A node lane's ordered conflict
points are split by **waiting spots**: W (entry spot, as before) and **mid spots** = the start of a conflict point
after a gap in which the whole car fits (car length from the prefab bounds + the game's stop margin), so the car is
inside no other lane's path there; touching points form one group. At each spot ONE decision for its **stretch** (the
points up to the next spot), stock judgement on each; only the car at the spot decides, followers queue with stock
car-following. **While crossing** the car re-checks every uncrossed point of its stretch: a blocking reservation or a
2 s look-ahead car on one of them, and it can still stop by the game's own can-stop test (`GetBrakingDistance <=
distance`, no new margin) -> **safety stop** just before the FIRST uncrossed point until all remaining points of the
stretch are clear; cannot stop -> stock collision braking only. A physical car in a later point and space requests
keep stock braking at that point (deadlock, `docs/STAGE2-ANALYSIS.md` section 8). With no mid spot this is the 0.2.0
decision at W plus the safety net. **Root-cause fix, separate (section 7):** a car that can no longer stop at its spot
keeps the game's own give-way at the busy point instead of being sent to an unreachable spot (and then committed past
it). Logic: new `JunctionRules/Core/StretchRule.cs` (pure, unit-tested) and `Stage2/JRWholeJunction.cs`.
Block changes for 0.2.1: S2-04 (same 4 lines; `BeginLane` now also takes the car, its speed, the time step and its
geometry for the can-stop test and the car length) and S2-07 (same 1 line; calls `SpaceYield`: space requests hold a
car at its spot but are dropped while crossing). All other blocks unchanged (only comments reworded in S2-06/S2-10).
Counters (timing log): stretch decisions, waits at the entry spot for a later conflict, waits at a mid-junction spot,
safety stops inside, stopped inside for a give-way, stopped inside behind a crossing car.

0.2.2 (JR-T-002d, owner ruling 14 Nov 2026): **trailers count in the vehicle length for mid spots.** The room a mid
spot needs is the whole vehicle: controller + every trailer, chained by the game's own attach points (the trailer's
`CarTrailerData.m_AttachPosition` is placed on the puller's `CarTractorData.m_AttachPosition`: CarTrailerMoveSystem,
`Game.Vehicles.InitializeSystem` 323-324, `ObjectInterpolateSystem` 1754-1768; attach z from prefab bounds and stock
offsets, `VehicleInitializeSystem` 406-407 / 421-422), length = straight extent front-most front to rear-most rear. No
invented constant: the hitch spacing is entirely in the stock attach points. Bicycles and cars without trailers get
exactly the 0.2.1 length. Computed once per car and navigation update by the navigation job (new block S2-25, only
when `m_JRMode != 0`) from the `LayoutElement` buffer the job already reads, then handed to the iterator through
`JRWholeJunction.Create` (S2-21). Logic: new `JunctionRules/Core/VehicleLength.cs` (pure, unit-tested) and
`Stage2/JRVehicleLength.cs` (lookups, Burst-compatible, no allocations); `BeginLane` uses it instead of the controller's
bounds (S2-04 unchanged). Block changes for 0.2.2: S2-20 (+8 lines: read-only `CarTractorData` / `CarTrailerData`
lookups and the per-car length field), S2-21 (same 1 line, one more argument), S2-23 (+2 lines: the two lookups, set
every step on the main thread with `SystemBase.GetComponentLookup(isReadOnly: true)`), new S2-25 (5 lines). With
`m_JRMode == 0` S2-25 is one int test plus a float store; the two lookups are always set (read-only, prefab data).

Further owner rulings recorded (14 Nov 2026, no code change): (1) a car physically in a later conflict point -> the
crossing car brakes for it there (stock collision braking at that point): KEEP; (2) a follower queued behind a car
waiting at a mid-junction spot may stand in the near carriageway: ALLOWED (stock car-following, no room check);
(3) trailers count in the vehicle length for mid spots: 0.2.2 (above); (4) the "At the stop line" waiting-spot setting
keeps using mid-junction spots: KEEP (only the entry spot moves to the line). Analysis: `docs/STAGE2-ANALYSIS.md`
owner choices 6-9.

| Id | File | Sites | Lines inside | Where | What |
|---|---|---|---|---|---|
| S2-01 | iterator | 1 | 1 | after field `m_LaneOffsetPush` | Field `m_JR` (JRWholeJunction): the car's stage 2 state. |
| S2-02 | iterator | 2 | 1 each | first statement of both `IterateFirstLane` | `m_JR.m_FirstLane = true` (the lane checked next is the car's current lane). |
| S2-03 | iterator | 1 | 1 | first statement of `IterateNextLane` | `m_JR.m_FirstLane = false`. |
| S2-04 | iterator | 1 | 4 | `CheckOverlappingLanes`, before the overlap loop | `BeginLane`: waiting spots of a junction (node) lane (W + mid spots, red lanes excluded, amber live), the stretch the car is in and its first uncrossed point (0.2.1: also passes the car, speed, time step and geometry for the can-stop test and the car length). |
| S2-05 | iterator | 1 | 2 | after `blockerType` in the loop | `jrStart` (overlap start seen from the car), `jrInScope` (at or after W and yield-checked). |
| S2-06 | iterator | 1 | 1 | after `maxBrakingSpeed` (blocking reservation) | Per point phase (0.2.1): later stretch / deciding: brake to the spot; crossing with a busy uncrossed point: safety stop before the first uncrossed point if it can stop, else no give-way; cannot stop at the spot: stock. |
| S2-07 | iterator | 1 | 1 | after `maxBrakingSpeed3` (space request) | 0.2.1 `SpaceYield`: holds a car at its spot; dropped while crossing (deadlock). |
| S2-08 / S2-08b | iterator | 1 / 1 | 2 / 1 | around `CheckPedestrian` | Before commit the pedestrian check uses the distance to W; the game's `num5` is restored after. |
| S2-09 | iterator | 1 | 1 | after `GetObjectSpeed` in the object loop | Keeps the other car's position before the 2 s priority look-ahead. |
| S2-10 | iterator | 1 | 2 | after the look-ahead block | After commit the look-ahead is undone unless the other car is already past the overlap start (the game's own committed semantics); `jrProjected` = the car counts only through the look-ahead. |
| S2-11 / S2-11b | iterator | 1 / 1 | 3 / 2 | around `UpdateMaxSpeed` in `CheckOverlappingLanes` | Before commit: braking distance to W for projected cars and cars in a crossing (merge followers stay stock); `num5` restored after; debug counters. |
| S2-20 | nav | 1 | 13 | `UpdateNavigationJob` fields | `m_JRMode` (int, by value) and `m_JRCounters` (persistent `NativeArray<long>`, atomic adds, safety restriction off: debug-only counters); 0.2.2: `[ReadOnly] m_JRTractorData` / `m_JRTrailerData` (prefab lookups) and `m_JRVehicleLength` (this car's whole length, written by S2-25). |
| S2-21 | nav | 1 | 1 | after the iterator is set up in `UpdateNavigationTarget` | `m_JR = JRWholeJunction.Create(m_JRMode, m_NodeLaneData, m_JRVehicleLength)` (0.2.2: plus the whole vehicle length). |
| S2-22 | nav | 1 | 4 | after `CheckBlocker` at `IL_09cd` | Counters, once per car-step, only when the count bit is set. |
| S2-23 | nav | 1 | 4 | `OnUpdate`, before `ScheduleParallel` | Main thread: takes this step's mode from `Stage2Handoff` (published by `CarDrivingSwitchSystem` earlier in the same step) into the job struct by value; 0.2.2: sets the read-only `CarTractorData` / `CarTrailerData` lookups. |
| S2-24 | nav | 1 | 8 | new `OnDestroy` (none in vanilla) | Completes the job and frees the counter array. |
| S2-25 | nav | 1 | 5 | moving-car loop of `UpdateNavigationJob.Execute`, after the controller's `ObjectGeometryData` lookup, before `UpdateNavigationTarget` | 0.2.2: `m_JRVehicleLength` = 0, or, when `m_JRMode != 0` and the chunk has a `LayoutElement` buffer, `JRVehicleLength.WithTrailers(...)` (controller + trailers by the stock attach points). |

Total: 20 blocks, 58 lines inside (as `vanilla_copy.py check` reports; 0.2.1: 19 blocks, 43 lines; S2-24 counts its trailing blank line).

Effect on `tools/IlHash --copy` (expected): 48 of 80 identical (was 54), 21 shape only (was 19), 11 content
differences (was 7: the four stage 2 methods `IterateFirstLane` x2, `IterateNextLane`, `UpdateNavigationTarget` join
`CheckOverlappingLanes` and the others listed below, which also contain blocks now), 1 extra (`OnDestroy`, S2-24).
Field blocks of the iterator and the job are "shape only" because of S2-01 and S2-20.
0.2.2 (`tools/IlHash --copy`, managed build of 0.2.2): 48 of 80 identical, 20 shape only, 12 content differences,
1 extra. `UpdateNavigationJob.Execute` joins the content differences (S2-25; it was "shape only" before), the rest as
0.2.1.

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
