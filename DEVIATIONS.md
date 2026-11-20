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

0.2.2 (JR-T-002d, owner ruling 14 Nov 2026; SUPERSEDED by 0.2.3, mid spots removed, the length code is kept but no
longer called): **trailers count in the vehicle length for mid spots.** The room a mid
spot needs is the whole vehicle: controller + every trailer, chained by the game's own attach points (the trailer's
`CarTrailerData.m_AttachPosition` is placed on the puller's `CarTractorData.m_AttachPosition`: CarTrailerMoveSystem,
`Game.Vehicles.InitializeSystem` 323-324, `ObjectInterpolateSystem` 1754-1768; attach z from prefab bounds and stock
offsets, `VehicleInitializeSystem` 406-407 / 421-422), length = straight extent front-most front to rear-most rear. No
invented constant: the hitch spacing is entirely in the stock attach points. Bicycles and cars without trailers get
exactly the 0.2.1 length. Computed once per car and navigation update, inside `UpdateNavigationTarget` where the
iterator is built (block S2-21, only when `m_JRMode != 0`), as a LOCAL from the car's `LayoutElement` buffer (read-only
`BufferLookup`), and handed to the iterator through `JRWholeJunction.Create`. It is deliberately not a job-struct field:
`UpdateNavigationJob` runs in parallel and the workers share the one job struct, so a per-car field written in
`Execute` would be a data race (a car could pick up another car's length; found in review JR-T-002d, fixed by
JR-T-002dr). Logic: new `JunctionRules/Core/VehicleLength.cs` (pure, unit-tested) and
`Stage2/JRVehicleLength.cs` (lookups, Burst-compatible, no allocations); `BeginLane` uses it instead of the controller's
bounds (S2-04 unchanged). Block changes for 0.2.2: S2-20 (+9 lines: read-only `CarTractorData` / `CarTrailerData` /
`LayoutElement` lookups), S2-21 (1 line -> 6 lines: the local length and its call), S2-23 (+3 lines: the three lookups,
set every step on the main thread with `SystemBase.GetComponentLookup` / `GetBufferLookup(isReadOnly: true)`). No new
block (the 0.2.2 first cut had S2-25 with a job field; removed in the review fix). With `m_JRMode == 0` the added code
in S2-21 is one int test; the three lookups are always set (read-only: prefab data and the layout buffer, which the
job already reads through its read-only type handle).

Further owner rulings recorded (14 Nov 2026, no code change): (1) a car physically in a later conflict point -> the
crossing car brakes for it there (stock collision braking at that point): KEEP; (2) a follower queued behind a car
waiting at a mid-junction spot may stand in the near carriageway: ALLOWED (stock car-following, no room check);
(3) trailers count in the vehicle length for mid spots: 0.2.2 (above); (4) the "At the stop line" waiting-spot setting
keeps using mid-junction spots: KEEP (only the entry spot moves to the line). Analysis: `docs/STAGE2-ANALYSIS.md`
owner choices 6-9.

0.2.3 (JR-T-002f, owner rulings 14 Nov 2026 + owner clarification the same day, binding). **No mid-junction waiting
spots** (supersedes the 0.2.1 mid spots, the 0.2.2 trailer length for mid spots and rulings (2)-(4) above): the car
waits only just before its FIRST conflict point (W) and decides there once for its whole path; central reservations
are not special. `StretchState.FindSpots` returns only W (one stretch); the "waited at a mid-junction spot" counter is
gone. **Arrival-time projection (fix (a))**: the car's OWN arrival time at each conflict point is computed from its
actual state with the game's own motion functions (`VehicleUtils.CalculateSpeedRange` per navigation step,
`GetMaxDriveSpeed` + `ModifyDriveSpeed` for the junction lane's speed / speed-in-turns cap, `GetMaxBrakingSpeed` to
brake down to it before the lane; no margin), `Core/ArrivalTime.cs`. The other cars are projected to that time at
constant speed through the unchanged stock tests: a car on a conflicting junction lane gets the stock look-ahead
`2 s + arrival time` instead of `2 s` (S2-10); every car on a lane FEEDING a conflicting junction lane (node lane start
= feed lane end PathNode, found through the node's `ConnectedEdge` buffer and the edge's `SubLane` buffer, as
`TrafficLightSystem.CheckNextLane` does) becomes a virtual reservation (stock reservation reach + speed x arrival
time) through the stock reservation test `offset > num10 || priority > num6` (new block S2-12). The same projection
runs for the safety re-check while crossing (from the current state to each uncrossed point); the stock can-stop test
and stock collision braking are unchanged. Diagnostics b1 (debug log only): safety-stop EVENTS (a car's transition into
a safety stop counted once, fixed lock-free table of 8192 slots), and per point at W the distance from W and the own
arrival time as 8-bucket histograms. Block changes for 0.2.3: S2-02 (1 -> 2 / 3 lines: also remember the current lane(s),
the car's own approach, so its own followers are never counted), S2-04 (same 4 lines, new arguments: the car's speed
limit instead of the length), S2-10 (same 2 lines, calls `AfterProjection`: undo while crossing as before, extend while
deciding / re-checking), S2-12 NEW (10 lines), S2-20 (14 -> 12 lines: the three 0.2.2 trailer lookups replaced by the
event table and the read-only `ConnectedEdge` lookup), S2-21 (6 -> 1 line: `Create` with the read-only owner / lane /
sub-lane / connected-edge lookups, no local length), S2-22 (same 4 lines: passes the event table, entity and frame),
S2-23 (5 -> 4 lines: sets the event table and the `ConnectedEdge` lookup instead of the trailer lookups). The 0.2.2
files `Core/VehicleLength.cs` and `Stage2/JRVehicleLength.cs` are kept (tested, harmless) but nothing calls them.

0.2.3 review fix (JR-T-002g, owner ruling 14 Nov 2026, binding; version stays 0.2.3). In S2-12 a feeding-lane car
counts ONLY when it would win the stock priority test against the deciding car, `VehicleUtils.GetPriority(car) - num6
> 0`, the same condition stock applies its look-ahead under and S2-10 extends under (same stock priority source as a
car on, or a reservation of, the conflicting lane; the game's own `num6`). Combined:
`GetPriority(car) - num6 > 0 && (offset > num10 || priority > num6)` (`ArrivalProjection.FeedingCarBlocks`), so a
main-road car never brakes for a minor-arm car heading to its STOP / YIELD line, and equal priority is not counted (as
S2-10). Implemented in `Stage2/JRWholeJunction.FeedingLaneYield` only: no marked block changed (S2-12 still passes the
stock `num10` / `num6`), `tools/vanilla_rules.json` unchanged. Owner ruling (2) the same day: the arrival time
including the stock 0.9 m stop margin is accepted as is (no change).

0.2.4 (JR-T-002h, owner rulings 14 Nov 2026, binding; `docs/STAGE2-ANALYSIS.md` section 11). (a) S2-12 (same 10
lines, new arguments): passes the conflicting lane's `LaneSignal` exactly as stock just read it for this overlap
(`componentData2`), and the iterator's read-only `CarLane`, `ObjectGeometryData` and `LaneReservation` lookups. A feeding
car its own signal stops is skipped (red always; amber when the stock amber test says it stops; emergency vehicles
ignore a non-physical signal as in stock), and the scan runs only for the deciding car (not a follower, not when it is
already held as hard, not when the conflicting lane's own reservation holds it), with a per-car-update memo and a work
cap (unread cars count as not busy, as stock). (b) S2-13 NEW (1 line), a **deviation from stock priority, owner-approved
14 Nov 2026** (setting "Turning across oncoming traffic gives way", default on, whole junction only): right after the
stock `num6 -= num9`, for a pair with equal sign status (no sign / signal override, no Stop / Yield / RightOfWay /
Unsafe difference) where one lane turns across the oncoming flow and the other is that oncoming straight (lane flags
and PathNode edges; traffic side from `CityConfigurationSystem.leftHandTraffic`), the turner's overlap delta is `+1`
and the straight's `-1` in place of the stock delta (stock's geometric tie-break). For the straight car the same block
marks the overlap so S2-06 (text unchanged, `Yield`) does not hold it for the turner's reservation alone unless that
reservation's priority beats `num6`. No shared component or `LaneOverlap` data is written. The traffic side and the
setting travel in the stage 2 mode bits (set only with the rule on, so the rule-off mode is unchanged); no navigation
job block changed for them. Version 0.2.4.


0.3.2 (JR-T-004, owner ask 15 Nov 2026: "keep stopping until it has a gap, no timeout"; no starvation safeguard,
standing owner ruling). A **deviation from stock behaviour**, setting **Cars never push through people on crossings**
(default on; acts whenever the copy drives, with either gap rule). The game lets a car creep at >= 3 m/s through people
on a pedestrian lane its path crosses in `CheckPedestrian`: (a) after the car's `PushBlockers` lane flag, set by
`UpdateNavigationTarget` after 5 s of lane duration held by a `Temporary` (people) blocker; (b) at once for a person with
no `Moving` component or slower than 0.1 m/s. With the setting on both become the game's walking-person value (full
stop at the same distance), so the car waits until the crossing is clear. At a junction this acts on the distance S2-08
chose (the spot W before commit), so it completes the S2-08 hold instead of duplicating it; mid-block zebras (no W) and
crosswalks before W or after commit get the same refusal at the game's own distance. A cyclist on a crossing (bicycle on
a pedestrian lane) is never the ignored blocker (IgnoreBlocker 3 m/s creep). No new block ids: S2-08, S2-08b, S2-11 and
S2-11b grow by 2, 1, 2 and 1 lines (adding blocks at those anchors would sit next to them). Nav-side flags are left
as stock (`PushBlockers`, `IgnoreBlocker` still set; their effect on people and cyclists is refused in the iterator).
The setting bit (`Stage2Mode.NoPushPeople` = 32) is stripped from the job's stage 2 mode, so the 0.2.x blocks are
unchanged with the rule off. Counter: timing line `crossing give-ups refused N (5 s push A, standing person B, cyclist
C)`. Pure logic in `Core/CrossingPush.cs` (tests `CrossingPushTests`, `CrossingRepoTests`). Version 0.3.2-probe.

0.3.3 (JR-T-005 + JR-T-006, owner 15 Nov 2026). Two Whole-junction changes, no new setting. **(a) JR-T-005:** the S2-12
feeding scan skips a conflicting junction lane flagged `CarLaneFlags.Unsafe` (`FeedingScan.SkipsConflictingLane`): every
car on its feeding lane was counted as about to take it (routes are not read), so at Moonflower St x Anchor St the
left-turner from Moonflower (STOP) was held for a right-turner on Anchor westbound through the unsafe U-turn lane that ends
in its exit lane; their own paths do not overlap (2.93 m apart, the game's overlap reach is 2.4 m). Stock never sees
such a car; a real U-turner still holds through its reservation (S2-06) and presence (S2-10/S2-11). Code in
`JRWholeJunction.FeedingLaneYield`, no block change (S2-12 text unchanged). **(b) JR-T-006:** a crossing (the other lane
is a `PedestrianLane`) at or after W is out of the rule (`InScope` false, phase Stock: the game's own pedestrian /
cyclist check at the crossing, stock distance; 0.3.2 no-push still applies) when the whole vehicle (trailers incl.,
`JRVehicleLength`, JR-T-002d) fits between the end of the last vehicle point before it (road / track, yield-checked, not
an end merge) and the crossing's stop point (`Core/CrossingPoint.cs`); otherwise it stays in the decision at W as in
0.3.2. Block changes: S2-05 (same 2 lines, passes `m_PedestrianLaneData.HasComponent(m_Lane)`), S2-20 (12 -> 21 lines:
read-only `CarTractorData` / `CarTrailerData` / `LayoutElement` lookups again), S2-21 (1 -> 6 lines: the vehicle length
as a per-car LOCAL, trailers only with the rule on; never a job field), S2-23 (4 -> 7 lines: the three lookups). Counters:
`unsafe lane N` in the feeding-scan brackets, `crossings after W judged at the crossing A, kept at W (vehicle does not
fit) B`. Tests `MoonflowerTests`, `CrossingPointTests`, `ClearPointTests`, `Tech9RepoTests`. Version 0.3.3-probe.
| Id | File | Sites | Lines inside | Where | What |
|---|---|---|---|---|---|
| S2-01 | iterator | 1 | 1 | after field `m_LaneOffsetPush` | Field `m_JR` (JRWholeJunction): the car's stage 2 state. |
| S2-02 | iterator | 2 | 2 / 3 | first statement of both `IterateFirstLane` | `m_JR.m_FirstLane = true` (the lane checked next is the car's current lane); 0.2.3: `m_JR.m_CurrentLane(2) = lane(s)`, the car's own approach (feeding-lane scan skips it). |
| S2-03 | iterator | 1 | 1 | first statement of `IterateNextLane` | `m_JR.m_FirstLane = false`. |
| S2-04 | iterator | 1 | 4 | `CheckOverlappingLanes`, before the overlap loop | `BeginLane`: the entry spot W of a junction (node) lane (0.2.3: the only spot; red lanes excluded, amber live), whether the car is past it and its first uncrossed point; passes the car, speed, time step and the lane's speed limit (can-stop test, 0.2.3 arrival time). |
| S2-05 | iterator | 1 | 2 | after `blockerType` in the loop | `jrStart` (overlap start seen from the car), `jrInScope` (at or after W and yield-checked); 0.3.3: tells `InScope` whether the other lane is a pedestrian lane (a crossing the car fits before is out of the rule); 1.4.0 (JR-T-035): also its `PedestrianLaneFlags` (a marked zebra over a building access stays in the rule). |
| S2-06 | iterator | 1 | 1 | after `maxBrakingSpeed` (blocking reservation) | Per point phase (0.2.1): later stretch / deciding: brake to the spot; crossing with a busy uncrossed point: safety stop before the first uncrossed point if it can stop, else no give-way; cannot stop at the spot: stock. 0.2.4 (text unchanged): no hold when S2-13 marked the overlap (turner's reservation alone, turner gives way). |
| S2-07 | iterator | 1 | 1 | after `maxBrakingSpeed3` (space request) | 0.2.1 `SpaceYield`: holds a car at its spot; dropped while crossing (deadlock). |
| S2-08 / S2-08b | iterator | 1 / 1 | 4 / 2 | around `CheckPedestrian` | Before commit the pedestrian check uses the distance to W; the game's `num5` is restored after. 0.3.2: with "Cars never push through people on crossings" on, the check runs against an open limit (saved before), then `NoPushPedestrian` replaces the game's creep (5 s push timer or standing person) by the full-stop speed at the same distance and merges the saved limit back. |
| S2-09 | iterator | 1 | 1 | after `GetObjectSpeed` in the object loop | Keeps the other car's position before the 2 s priority look-ahead. |
| S2-10 | iterator | 1 | 2 | after the look-ahead block | `AfterProjection`: while crossing (no safety stop) the look-ahead is undone unless the other car is already past the overlap start; 0.2.3: while deciding at W or in the safety re-check it becomes `2 s + own arrival time`; `jrProjected` = the car counts only through the look-ahead. |
| S2-11 / S2-11b | iterator | 1 / 1 | 5 / 3 | around `UpdateMaxSpeed` in `CheckOverlappingLanes` | Before commit: braking distance to W for projected cars and cars in a crossing (merge followers stay stock); `num5` restored after; debug counters. 0.3.2: with the setting on, `m_Ignore` is hidden for this one call when the object is a bicycle on a pedestrian lane (no IgnoreBlocker creep through a cyclist on a crossing); restored after. |
| S2-12 | iterator | 1 | 10 | `CheckOverlappingLanes`, after the reservation priority `num6 -= num9` | 0.2.3 `FeedingLaneYield`: while deciding / re-checking, every car on the lanes feeding this conflicting junction lane that wins the stock priority test (`GetPriority(car) - num6 > 0`, as S2-10; JR-T-002g) as a virtual reservation (stock reach + speed x own arrival time) through the stock test `offset > num10 \|\| priority > num6` with the game's own `num10` / `num6`; on a hit, the stop at the spot / before the first uncrossed point is applied like a blocking reservation. 0.2.4: also passes the stock-read conflicting-lane signal (`componentData2`) and the read-only `CarLane` / `ObjectGeometryData` / `LaneReservation` lookups: cars stopping at that lane's red / amber are skipped; deciding car only, memo, cap. 0.3.3 (block text unchanged): no scan for an Unsafe conflicting lane. |
| S2-25 | iterator | 1 | 9 | `CheckOverlappingLanes`, right after S2-04 | 0.4.1 `BeginZebras` (only with the setting "Cars never stop on a zebra"): a node lane with zebras (pedestrian overlap, `PedestrianLaneFlags.Crosswalk` without `Unsafe`) gets the zebra sections as its waiting spots (Core/ZebraSections.cs, merged where the whole vehicle does not fit before a zebra); the car is held (`GetMaxBrakingSpeed` to the next zebra section's spot, blocker `Entity.Null`, type Continuing, so no IgnoreBlocker creep) while there is no room for the whole vehicle past that section (Core `RoomScan`). JR-T-009-r: the call also passes the iterator's own `ref m_LaneOverlapData` (read-only) for further zebras on later path lanes. |
| S2-26 | nav | 1 | 29 | `UpdateNavigationTarget`, right after S2-21 | 0.4.1 (setting on only): `m_JR.SetPath(navigationLanes)` for the room-past scan; the whole vehicle length with trailers also with the stock gap rule. 0.4.2: also with "Cars obey yellow boxes", which first hands the five read-only box lookups to the iterator, plus the boxed road edge set (`SetBoxes`). 0.4.3: `SetBoxes(m_JRBoxJunctionData, m_JREdgeLaneData, ref m_JRBoxEdges)`: two lookups, and the iterator keeps a pointer to the job's one band set (no per-car copy). G1: with the exit trace on, `SetTrace(m_JRExitTrace)` after `SetExits`. 0.4.9 (JR-T-015): + 5 lines, with the exit or box setting `SetEvents(m_JRSafetyEvents, m_SimulationFrame)`: the shared event table is read for driving (G12 standing Go; G15 an entrant held for room is not 'present' at an exit) and written by the G9 entering hold (`EnteringHeldKind`). |
| S2-28 / S2-28b | iterator | 1 / 1 | 10 / 10 | top of both `CheckCurrentLane` overloads (every car lane the iterator walks, current and ahead, before `CheckOverlappingLanes`) | 0.4.2 `BoxHold` (only with the setting "Cars obey yellow boxes"; Core/BoxDriving.cs, Stage2/JRYellowBox.cs): a node lane of a boxed node (`YellowBoxJunction` on the lane's `Owner`) is entered at its start (the stop line) only with room for the whole vehicle plus the margin past the lane end on the car's own path; an edge lane (`EdgeLane`) of a road with boxed accesses (`ConnectedBuilding` buffer, `YellowBoxEntrance` + `Building.m_RoadEdge`) has a band of the painted length centred on the access (`Building.m_CurvePosition` through `EdgeLane.m_EdgeDelta`): a through car is held before the band, a car joining inside it (leaving the driveway) at its join point, while there is no room past the band; a car whose path ends inside the band (into the driveway) is exempt. Room = the 0.4.1 scan (shared `HasRoomPast`), vehicles ahead inside the box / band count as queued. Hold = `GetMaxBrakingSpeed` to the entry, blocker `Entity.Null`, type Continuing (as S2-25). Cars already in the box, or that cannot stop (the game's can-stop test), carry on. |
| S2-27 | iterator | 1 | 1 | right after S2-08b | 0.4.1 counters only: `AfterPedestrian` (zebra entry held for people). |
| S2-29 | iterator | 1 | 10 | `CheckOverlappingLanes`, right after S2-25 | 0.4.4 `ExitDrivewayHold` (only with the setting "Vehicles leaving buildings give way"; Core/ExitGiveWay.cs, Stage2/JRExitGiveWay.cs): a drive's leaving lane (`SideConnection` + `Yield`) is held before the start of its first overlap with a road car lane (`GetMaxBrakingSpeed`, blocker `Entity.Null`, type Crossing) while any crossed / merged road lane is busy: a road vehicle on it, or on the lanes feeding it (2 levels: the junction lanes, then the road lanes before them; red / amber-can-stop skipped), whose stock reach + speed x own arrival time reaches the conflict. Only the vehicle the hold binds (hold < current max) scans. On-duty emergency (priority 108) never. 0.4.8 (G7): the call also hands `m_LaneOverlapData` / `m_PedestrianLaneData`; the hold point is the SIDEWALK (first pedestrian overlap) when it comes before the first road lane, the decision adds room on the joined lane (unboxed) or past the box band (boxed), crossed lanes use the time to clear them with the whole vehicle, a road vehicle within 10 m of a conflict blocks even standing, and the scan also runs within 12 m of the spot whatever the current limit. |
| S2-30 | nav | 1 | 12 | `UpdateNavigationTarget`, right after the `IL_09cd:` label | 0.4.4 `ExitConnectionHold`: a vehicle on a building's `Connection` lane whose next navigation lane is a public road car lane (edge with no `Owner`) gets max speed 0 (blocker `Entity.Null`, type Crossing) while the join point or a road lane it crosses (geometry) is busy (same scan), or (with "Cars obey yellow boxes") its join point lies in a boxed exit band without room past it (0.4.3 `HasRoomPast`). G1 (JR-T-013a): + 2 lines, `laneIterator.m_JR.TraceWait(m_JRSafetyEvents, entity, m_SimulationFrame)` with its comment (the exit trace's first-wait line; nothing unless "Exit trace (log)" is on and the vehicle was held). |
| S2-31 | nav | 1 | 4 | target loop, `Connection` current lane joining a non-connection lane, before the stock 1 m / 3 m/s join test | 0.4.4: `if (laneIterator.m_JR.ExitWaiting) break;` keeps a vehicle held by S2-30 on its Connection lane. |
| S2-32 | nav | 1 | 19 | `UpdateNavigationTarget`, end of the branch for a vehicle on an `Area` lane (after `blocker.m_MaxSpeed = byte.MaxValue;`) | 0.4.5 `ExitAreaHold` (only with the exit setting): a vehicle on a building lot's area lane whose next navigation lane is a public road car lane brakes (`GetMaxBrakingSpeed` to a stop point 1 m before the join point, blocker type Crossing) while the join lane or a crossed road lane is busy (the S2-30 scan, arrival time from its own motion) or a boxed exit band has no room past it. 0.4.6: every vehicle within `AreaScanMetres` of the join point is judged on every update (0.4.5 judged only a vehicle whose braking speed was already below its limit, while the stock hand-over to the road lane comes up to ~20 m before the join point), the distance runs from the vehicle's front (`prefabObjectGeometryData.m_Bounds.max.z`), and the call feeds `AddCounts` (this branch skips the later one, so 0.4.5 lost these counters). G1 (JR-T-013a): `laneIterator.m_JR.TraceWait(m_JRSafetyEvents, entity, m_SimulationFrame)` with its comment, before `AddCounts` (the exit trace's first-wait line; nothing unless "Exit trace (log)" is on and the vehicle was held; the 16 lines include it). |
| S2-33 | nav | 1 | 5 | target loop, before `ApplySideEffects` of the lane switch | 0.4.5: `if (Area current lane && laneIterator.m_JR.ExitWaiting) break;` keeps a vehicle held by S2-32 on its area lane. G1 (JR-T-013a): `if (laneIterator.m_JR.ExitWaiting) break;` for every current lane: a vehicle held at its exit in this update by S2-32, S2-35 or S2-36 stays on its lane (the flag lives in the per-vehicle, per-update `m_JR`; false unless a hold set it). |
| S2-34 | nav | 1 | 5 | target loop, right before `ApplySideEffects` of the lane switch (after S2-33) | 0.4.6 probe, counting only: `ExitJoinProbe` counts a hand-over from an area lane / Connection lane / other building-owned lane to a public road lane (timing line `hand-overs to the road from: lot / gate / other`). No driving effect. G1 (JR-T-013a): `ExitHandOver` (counting or "Exit trace (log)" on) classifies the path (Core/ExitPaths.cs) into lot / gate / spawn point / building parking / kerbside parking / building road / drive / other, counts it and a judged path handed over with no decision in the update (`not judged`), and writes one trace record. No driving effect. 0.4.8 (G7): also passes the event table; a vehicle entering a driveway's leaving lane is counted (`driveway exits crossing the sidewalk`) and checked for a recent full Go judgement (`of them without a full judgement`), and a hand-over after a recent Go is no longer `not judged`. No driving effect. |
| S2-35 | nav | 1 | 18 | `UpdateNavigationTarget`, end of the `TransformTarget \| ParkingSpace` branch (after `CheckBlocker`) | G1 (JR-T-013a) `ExitTargetHold` (only with the exit setting): a vehicle at a building's spawn location object (`TransformTarget`; the 0.4.6 `other`) or in a parking space (building parking lane, garage `Connection \| ParkingSpace`, kerbside parking lane of the road) whose next navigation lane is a public road car lane is held (spawn point: `GetMaxBrakingSpeed` to the stock target, i.e. the spawn location; parking: 0; blocker `Entity.Null`, type Crossing) while the join lane or a crossed lane is busy (the S2-30 scan, own arrival time 0) or a boxed exit band has no room past it; sets `ExitWaiting` for S2-33; `TraceWait`; `AddCounts` (this branch skips S2-22). |
| S2-36 | nav | 1 | 11 | `UpdateNavigationTarget` at `IL_09cd`, right after S2-30, before the max speed is taken | G1 (JR-T-013a) `ExitLaneHold` (only with the exit setting): a vehicle on a car lane of a building's own road (owner chain reaches a `Building`; not a drive's `SideConnection` lane) whose next navigation lane is a public road car lane brakes to stop 1 m short of the join point (the S2-32 approach hold from `AreaScanMetres`, front-based) while the joined lane or a lane it crosses is busy or a boxed exit band has no room past it; sets `ExitWaiting` for S2-33; `TraceWait`. For every other vehicle: the next lane's flags and the current lane's owner chain (two lookups for a road lane), then nothing. |
| S2-37 | iterator | 1 | 10 | `CheckOverlappingLanes`, right after S2-25 (before S2-29) | JR-T-013b (G3), generalised in 1.5.0 (JR-T-038) `BoxSpotRoomHold` (only with "Cars obey yellow boxes" and the Whole junction rule; Core `BoxDriving.SpotRoomCheck`): a car on a boxed lane whose next spot lies inside the box (its first live conflict; front past the line, can still stop before the spot, the line test did not run this step) gets the box exit-room test again (`HasRoomPast` from its front, its own path only; G22 `SameExitAdmits` kept); no room = stop at the spot (`GetMaxBrakingSpeed`, blocker `Entity.Null`, type Continuing, why `jr_box_spot_room`), before the first live overlap. 0.4.7-1.4.1: right-turners at the UK turn spot only (`BoxTurnRoomHold`, `TurnSpotRoomCheck`, why `jr_box_turn_spot`). |
| S2-52 | nav | 1 | 4 | `UpdateNavigationTarget`, after S2-36, before `navigation.m_MaxSpeed` | 0.4.10 (JR-T-016, review R1): a car whose binding limit is a yellow box hold waiting for a red / amber light inside the room past the box (`JRWholeJunction.LightPetition`) enqueues the game's own signal request `CarNavigationHelpers.LaneSignal(entity, lane, priority)` for that light's lane, as `IterateNextLane`'s `needSignal` does (copy 2246-2249). Its look-ahead ends at the box hold, so it never reaches the light's lane, and `TrafficLightSystem.GetNextSignalGroup` changes only to a group a car asked for: without the request a quiet approach would never get green. |
| S2-50 | iterator | 1 | 8 | `CheckOverlappingLanes`, creature branch, right before S2-08 | JR-T-014d (G11) `UnmarkedPerson` (only with the setting "People give way at unmarked crossings"; Core/UnmarkedCrossing.cs, Stage2/JRUnmarkedCrossing.cs): a person on an unmarked crossing (crosswalk lane with `Unsafe`, no `LaneSignal`; never a driveway `SideConnection` car lane, never a sidewalk) who is not yet on its carriageway (position along the lane before the kerb edge of the first road overlap + 0.5 m in their walking direction) is skipped (`continue`: no give-way); once per car-step the game's `CheckPedestrian` runs against the saved limit only to count "car-steps not held for people" (state restored). A person already on the carriageway: the game's check at the crossing (`jrInScope = false`: no whole-junction waiting spot for them), the safety floor. |
| S2-53 | iterator | 1 | 4 | `CheckOverlappingLanes`, creature branch, right after S2-50, before S2-08 | 0.4.11 (JR-T-017, G23, owner live 17 Nov: a car on green held by a person waiting just off the kerb; only with "Cars never push through people on crossings") `PersonNotCrossing`: a person STANDING within 1 m (at most a quarter of the lane) of the end of the overlapped pedestrian lane is not crossing (the game keeps a person waiting for a walk signal on the PREVIOUS lane at its end, HumanNavigationSystem 779-796 / 1160-1171, ReferencesSystem 128-130; the body spills up to ~1 m into the road): `continue`, the game's pedestrian check never brakes for them. People walking, and people standing on the car's way further out, keep the game's check and the 0.3.2 full stop; the G11 floor (already on an unmarked crossing) is never skipped. |
| S2-54 | iterator | 1 | 6 | `IterateNextLane`, right after the stock chain (signal / STOP / turn `CheckSpace` holds), before `num2 += m_DistanceOffset` | 0.4.11 (JR-T-017, G25, owner decision option (c): "holding a vehicle outside a junction because there is no room past it is yellow-box behaviour only") `SkipStockRoomHold`: when the stock chain stopped the car at a junction lane's entry because `CheckSpace` failed (driveSpeed 0, blocker type Continuing: only `CheckSpace` sets Continuing there) at an UNBOXED junction lane without signals / level crossing / roundabout, with the Whole junction rule and the exit lookups, every true crossing of that junction lane is judged by the exits' gap scan (vehicles on the crossed lane and its feeders, whole-length clear time, present / red-queue rules); all clear = the stock hold is dropped (drive speed restored, blocker none, type Limit): the car enters and waits inside the junction for exit room. Boxed junctions keep the box rules. Counter `stock room hold skipped (unboxed, crossings clear)`. Review: a junction lane that also crosses a track keeps the stock hold; the turner's own time uses the stock-scaled speed limit (the block hands `componentData2`) and the lane condition. Gating: Whole junction rule + "Vehicles leaving buildings give way" (its lookups); with "Cars obey yellow boxes" off every junction counts as unboxed. |
| S2-50b | iterator | 1 | 1 | right after S2-27 | JR-T-014d (G11), diagnostics only: `AfterUnmarkedFloor` counts (always-on counter) and records (ring) a car braked for a person already on the carriageway of an unmarked crossing; restores `jrInScope`. |
| S2-51 | nav | 1 | 7 | `ReserveNavigationLanes`, right before the `RequestSpace` reservation of the current lane | 0.4.9 (JR-T-015, G13): a vehicle the exit rule held at a driveway exit in this update (S2-29; `NoteExit` records event `DriveHeldKind` at this frame, read by `JRWholeJunction.HeldAtDriveway`) has `RequestSpace` cleared on its driveway leaving lane (current lane and / or the first navigation lane; `JRWholeJunction.WithdrawKeepClear`), so the two stock priority-96 keep-clear reservations are not made in this update and road vehicles crossing the drive do not stop to give it space (stock: `CarLaneSpeedIterator.CheckOverlappingLanes` stops a road car before a drive whose lane carries a 96 reservation when `CheckOverlapSpace` finds no space past it). Counted (`held exits' keep-clear requests withdrawn`, counting only). Only with the exit setting on (`Stage2Mode.ExitGiveWay` in `m_JRMode`). |
| S2-51b | nav | 1 | 10 | `ReserveNavigationLanes`, right after the `RequestSpace` reservation of the first navigation lane | 0.4.9 (JR-T-015, G13): restores the `RequestSpace` flags S2-51 cleared (current lane flags and the first navigation lane), so the vehicle's stored state is exactly the game's. |
| S2-13 | iterator | 1 | 1 | `CheckOverlappingLanes`, right after `num6 -= num9` (before S2-12) | 0.2.4 `TurnAcrossPriority` (deviation from stock priority, owner-approved 14 Nov 2026; setting on and whole junction only): turn across the oncoming flow `+1`, the oncoming straight `-1` when sign status is equal and no sign / signal override applies; marks the straight car's overlap so `Yield` (S2-06) does not hold it for the turner's reservation alone (unless that reservation's priority beats `num6`). |
| S2-20 | nav | 1 | 48 | `UpdateNavigationJob` fields | `m_JRMode` (int, by value), `m_JRCounters` (persistent `NativeArray<long>`, atomic adds, safety restriction off: debug-only counters); 0.2.3: `m_JRSafetyEvents` (persistent `NativeArray<long>` event table, compare-exchange, debug only) and `[ReadOnly] m_JRConnectedEdges` (`BufferLookup<ConnectedEdge>`). 0.3.3: `[ReadOnly]` `CarTractorData` / `CarTrailerData` / `LayoutElement` lookups again (vehicle length for the crossing fit). 0.4.2: the five read-only yellow box lookups and `m_JRBoxEdges` (`JRBoxEdges`, a by-value set of up to 15 road edges with boxed accesses, built on the main thread). 0.4.3: two lookups left (`YellowBoxJunction`, `EdgeLane`; the `YellowBoxEntrance` / `Building` / `ConnectedBuilding` lookups are gone, the job reads no building) and `m_JRBoxEdges` is the set of boxed car exits (up to 63 bands: road edge key + curve position), one copy per job. G1 (JR-T-013a): `m_JRExitTraceOn` (bool) and `m_JRExitTrace` (persistent `NativeArray<long>` ring, safety restriction off, written with `Interlocked`: debug trace only). |
| S2-21 | nav | 1 | 10 | after the iterator is set up in `UpdateNavigationTarget` | `m_JR = JRWholeJunction.Create(m_JRMode, m_NodeLaneData, m_OwnerData, m_LaneData, m_Lanes, m_JRConnectedEdges, m_JRCounters)` (0.2.3: read-only lookups of the job for the feeding-lane scan); 0.3.3: plus the whole vehicle length, a per-car local (controller bounds; with the rule on `JRVehicleLength.WithTrailers` from the car's layout buffer). |
| S2-22 | nav | 1 | 7 | after `CheckBlocker` at `IL_09cd` | Counters, once per car-step, only when the count bit is set; 0.2.3: safety-stop events through the event table (entity index, simulation frame). 0.4.7 review (G1 F1): + 2 lines, `CountRoadStart(..., currentLane.m_Lane, currentLane.m_LaneFlags, m_UnspawnedData.HasComponent(entity), ...)` with its comment: counts (once per vehicle and episode, event kind `ExitGiveWay.RoadStartKind`) a vehicle that is `Unspawned` on a public road car lane with no `Connection` / `Area` / `TransformTarget` / `ParkingSpace` / `ResetSpeed` flag, i.e. placed straight onto the road lane (counter `ExitRoadStart`; read-only, no driving effect). 0.4.8 (G7): + 5 lines, `NoteExit` (counting or exit trace on): records an exit's Go judgement in the event table (kind `ExitGiveWay.DriveGoKind`) and writes the driveway trace's `waits` line; no driving effect. S2-32 and S2-35 call `NoteExit` too (+1 line each; their branches skip S2-22). 0.4.9 (G12 / G13): `NoteExit` runs whenever the exit setting is on (same lines, condition `laneIterator.m_JR.m_ExitRule`): its Go / Wait records now DRIVE (a standing Go commits a driveway exit past its sidewalk spot; a held exit's `DriveHeldKind` record feeds S2-51), and it counts the sidewalk crossing where the front passes the spot. |
| S2-23 | nav | 1 | 15 | `OnUpdate`, before `ScheduleParallel` | Main thread: takes this step's mode from `Stage2Handoff` (published by `CarDrivingSwitchSystem` earlier in the same step) into the job struct by value; 0.2.3: sets the event table and `GetBufferLookup<ConnectedEdge>(isReadOnly: true)`; 0.3.3: the three read-only lookups for the vehicle length; 0.4.2: the five yellow box lookups and the boxed road edge set (`JRStage2Bridge.BoxEdges`, rebuilt only when `CarDrivingSwitchSystem` publishes a new version). 0.4.3: two box lookups (`YellowBoxJunction`, `EdgeLane`) and the boxed exit bands (`JRStage2Bridge.BoxEdges`, same version rule). G1: `JRStage2Bridge.TraceOn` / `TraceRing` (setting "Exit trace (log)"). |
| S2-55 | iterator | 1 | 1 | `IterateNextLane`, right after the stock chain's `num3` limit | 1.1.1 (JR-T-025, why slowed, diagnostics only): for the tracked car names the stock chain hold (signal / STOP / junction room / lane speed, `WhyRules.EntryChain`); no driving effect |
| S2-56 | iterator | 1 | 1 | `CheckOverlappingLanes`, right after the stock `maxBrakingSpeed2` give-way slow-down | 1.1.1 (JR-T-025, diagnostics only): names the stock give-way slow-down for the tracked car |
| S2-57 | nav | 1 | 1 | `UpdateNavigationTarget`, inside the stock collision branch | 1.1.1 (JR-T-025, diagnostics only): offers `StockCollision` for the tracked car |
| S2-58 | nav | 1 | 5 | `UpdateNavigationTarget`, right after the stock target-distance cap | 1.1.1 (JR-T-025, diagnostics only): offers `StockTargetDistance` and keeps the limit before steering for the tracked car |
| S2-59 | nav | 1 | 4 | `Execute`, right after the `UpdateNavigationTarget` call | 1.1.1 (JR-T-025, diagnostics only): only when the entity is the tracked one, resolves the record and writes the one shared slot (`JRWhy.Write`, seqlock); nothing tracked = nothing written |
| S2-24 | nav | 1 | 8 | new `OnDestroy` (none in vanilla) | Completes the job and frees the counter array. |

0.4.9 (JR-T-015, G17, owner 17 Nov: the No Vehicle Despawn mod did not see the Alderwood deadlock): every Junction Rules hold block wrote `m_Blocker = Entity.Null` (iterator S2-25, S2-28, S2-28b, S2-29, S2-37; navigation S2-30, S2-36, S2-35, and S2-32 kept the stock area branch's `Entity.Null` with `m_MaxSpeed = 255`). Blocker-chain deadlock finders (No Vehicle Despawn's `TagStuckObjectsJob.IsBlocked`, the game's `StuckMovingObjectSystem`, the cs2 bridge's `cs2_vehicles` deadlocks) treat `Entity.Null` as "not stuck" and stop there, so no loop through a Junction Rules hold was ever visible. The blocks now write `m_JR.HoldBlocker`: the vehicle that took the room the hold needs (RoomScan names it) or the road vehicle an exit gives way to (`VehiclesBlock`), `Entity.Null` only when nobody is to blame (people on a crossing, a cap); S2-32 / S2-35 also write the held speed into `blocker.m_MaxSpeed` exactly as `CheckBlocker` does. Same content as stock for an equivalent stop (stock names the car it stops behind); the stock `IgnoreBlocker` creep is not triggered (`CheckBlocker` sets `IsBlocked` only for a blocker without `Moving`).

0.3.6-probe (JR-T-008) changes nothing in the copy: the people / cyclist pass is its own system (`Systems/PeoplePassSystem.cs`, no copied game code; it only sets `HumanCurrentLane.m_CurvePosition.x` and the game's own `CarLaneFlags.IgnoreBlocker` on bicycles). `docs/vanilla_copy_check_0.3.6.txt` equals 0.3.5's.

0.4.1-probe (JR-T-009, cars never stop on a zebra) adds S2-25, S2-26, S2-27 and changes one line of S2-05: `jrInScope` is also computed when `m_JR.m_ZebraActive` (a zebra lane with the stock gap rule; `InScope` then takes only the zebras, and only while their section's spot is ahead). With the setting off S2-25 / S2-26 are one bool test each, `m_ZebraActive` stays false and S2-05 / S2-27 behave as in 0.4.0. The setting travels in the stage 2 int (`Stage2Mode.ZebraClear` + margin in bits 8-15) and `JRWholeJunction.Create` strips it (`Stage2Mode.Stripped`), so every 0.2.x `m_Mode == 0` exit is unchanged. Stock hooks used: crosswalks are node pedestrian lanes (`Game.Net/LaneSystem.cs` `CreateNodePedestrianLanes`, `isCrosswalk: true`, flags at 8571-8581: `Crosswalk`, plus `Unsafe` where the zebra was removed), also at mid-block crossing nodes; the overlap with them comes from `LaneOverlapSystem.CheckOverlaps`; room past mirrors the copy's `CheckSpace` (braking-distance credit of a moving car, +1 m per queued car).

0.4.1-probe review fixes (JR-T-009-r) change one line of S2-25 only (the `BeginZebras` call gains `ref m_LaneOverlapData`; block and line counts unchanged); everything else is in `Stage2/JRWholeJunction.cs` and `Core/ZebraSections.cs`, run only with the setting on. (1) A zebra section is decided at the last point where the car can still stop before it: when the game's braking distance at the car's speed (`GetBrakingDistance(car, speed, timeStep)`, the `canStop` value) is longer than the distance from the car's next spot to the following zebra spot, that zebra section is merged into the next spot's section for this car-step (`ZebraSections.MergeUnstoppable`, chaining): people on it, its Cs and the room past it hold the car at the earlier spot, as the 'does not fit' merge. The room check now also runs from before W for the first zebra section after it (`ZebraSections.RoomSection`, CCCZ: hold at Z while the car can stop there), so a car never passes W too fast to stop for the zebra on a full exit. (2) The room scan along the navigation lanes treats a zebra on a later node lane (another node, e.g. a mid-block crossing after an edge shorter than the vehicle) that starts inside the room window as part of the decision: people on it (any object on the crosswalk lane) = no room, else room is needed past its end too (`RoomScan.FurtherZebra` / `ExtendPast`, vehicles already read are re-judged). Known limit: people still on the pavement about to step onto that further zebra are not seen (the game's own check holds the car before it, its tail may then be on the first zebra). (3) The scan flags a LaneObject buffer read out of curve-position order (counter, probe only).

0.4.2-probe (JR-T-010, cars obey yellow boxes) adds S2-28 / S2-28b and extends S2-20 (five `[ReadOnly]` lookups: `YellowBoxJunction`, `YellowBoxEntrance`, `Game.Buildings.Building`, `ConnectedBuilding`, `EdgeLane`), S2-23 (their `GetComponentLookup` / `GetBufferLookup(isReadOnly: true)`) and S2-26 (`SetBoxes`; `SetPath` and the trailer length also with the box setting). No S2-04 / S2-25 line changes: the box's stop-line decision and the turner's spot in the box are made inside `BeginLane` / `BeginZebras` (job side only). With the setting off S2-28 / S2-28b are one bool test each, `m_BoxRule` is false, `BeginLane` / `BeginZebras` take exactly the 0.4.1 paths and the stage 2 int carries no box bit (`Stage2Mode.BoxBits` returns 0), so driving equals 0.4.1. The setting travels in the stage 2 int (`Stage2Mode.BoxObey` + `BoxTurnWait` / `BoxLeftHand` / `BoxJunctions` / `BoxEntrances`, entrance length in bits 21-29, the room margin shared in bits 8-15) and `JRWholeJunction.Create` strips it. Stock hooks used: `CheckCurrentLane` is called for every car lane the iterator walks (`IterateFirstLane` 211 / 357 / 368 / 372, `IterateNextLane` 540 of the copy) with the game's distance to the lane point; the game's own "space after the junction" test (`CheckSpace`, copy line 670, used at 470-500 only for the first node lane after an approach with turn / forward flags and only against the next lane's merge neighbours) stays unchanged and runs before the box test; the box only adds a stricter hold.

0.4.2 also (job side only, no block change): (1) owner refinement of the zebra sections (Core `ZebraSections.Build` with `gapMargin`, the room margin): with the Whole junction rule a zebra followed by a gap that holds the whole vehicle plus stop and room margin before the first vehicle point (Z O C C) gets a waiting spot at O; the zebra section then ends at the zebra (room past it = O free) and the Cs are decided at O. This brings back a mid-junction waiting spot ONLY right after a zebra: 0.2.3 (owner ruling: no waiting inside the junction, decide everything at W) stays everywhere else; Z C C (no gap) keeps the 0.4.1 layout; `MergeUnstoppable` folds O (and the box turn spot) into the earlier spot when the car cannot stop there any more; inside a yellow box no O spot (the box decides at the line). (2) `ScanLane` reads a lane's LaneObject buffer in curve order (Core `ScanOrder`) instead of storage order: the game sorts that buffer by `m_CurvePosition.y` (`Game.Net/NetUtils.cs` `AddLaneObject` / `UpdateLaneObject` 435-487 insert by `.y`; `Game.Simulation/CarNavigationHelpers.cs` `CurrentLaneCache` 185 / 198 / 228 write `CarCurrentLane.m_CurvePosition.xy`), and for a driving car `.y` is the reservation look-ahead (`CarNavigationSystem.ReserveNavigationLanes` ~2263: position `x` + braking distance at its max speed, capped at the lane target `z`), so a faster car close behind a slower one sorts after it and a car whose target lies short of the lane end sorts early: not position order (0.4.1 probe: "lane objects out of curve order" 2-4). JR-T-010-r: a buffer already in reading order (inverse lanes read from the end) is read straight; selection only otherwise. (3) The entrance band asks a sorted set of road edges with boxed accesses (`Stage2/JRBoxEdges.cs`, `Building.m_RoadEdge` of every `YellowBoxEntrance` building, up to 15, more = fall back to the building loop) before reading any `ConnectedBuilding` buffer; it is built on the main thread by `CarDrivingSwitchSystem` when the entrance box count changes (and every 64 steps) and handed to the job by value in S2-23 / S2-26 (a fixed-size struct: no container to dispose, no job dependency). The stock loops read the whole buffer and do not rely on order (`CarLaneSpeedIterator.CheckCurrentLane` 1059).

Total: 39 blocks, 314 lines inside (as `vanilla_copy.py check` reports; 0.4.9 (JR-T-015): G17 hold blocks name their blocker (S2-25, S2-28, S2-28b, S2-29, S2-30, S2-36, S2-37 same lines; S2-32 + 2, S2-35 + 1: `blocker.m_Blocker` / `m_MaxSpeed`); S2-51 / S2-51b (+17 lines, G13 keep-clear request of a held exit), S2-26 + 5 lines (event table handed in for driving), S2-22 condition changed (same lines, NoteExit always with the exit rule); 0.4.8 merge (JR-T-014 + 014b/c/d): G11 adds S2-50 / S2-50b (+9 lines), G7: S2-22 + 5, S2-32 + 1, S2-35 + 1 (NoteExit), S2-26 condition and S2-29 call extended (same lines); G7 alone: 35 blocks, 280 lines; 0.4.7 review fixes: S2-22 + 2 lines (vehicles placed straight onto a road lane, counted only); 0.4.7 (JR-T-013m merge), 271 lines: G1 adds S2-35 / S2-36 and extends S2-20..S2-23, S2-30, S2-33 (alone: 34 blocks, 251 lines), G3 adds S2-37 and the G4 watch extends S2-20..S2-23 (alone: 33 blocks, 228 lines); 0.4.6: 32 blocks, 207 lines; 0.4.3: 26 blocks, 144 lines; 0.4.2 and 0.4.3 before the 0.4.3 review fixes: 26 blocks, 155 lines; 0.4.2 before the review fixes: 26 blocks, 152 lines; 0.4.1: 24 blocks, 108 lines; 0.3.3 to 0.4.0: 21 blocks, 89 lines; 0.3.2: 21 blocks, 72 lines; 0.2.4 to 0.3.1: 21 blocks, 66 lines; 0.2.3: 20 blocks, 65 lines; 0.2.2: 19 blocks, 60 lines; 0.2.1: 19 blocks, 43 lines; S2-24 counts its trailing blank line).

JR-T-014d (G11, owner rule 17 Nov 2026: pedestrians crossing a road not on a zebra have no right of way; the person gives way) adds S2-50 / S2-50b (37 blocks, 282 lines inside as `vanilla_copy.py check` reports on the jr-t-014d branch alone). Stock cause (decompile 2026-11-13): an unmarked crossing is the crosswalk lane of a node composition without the Crosswalk marking (`Game.Net/LaneSystem.cs` 8572-8582: `Crosswalk` then `Unsafe`, signals dropped); its overlap priority delta is 0 (`LaneOverlapSystem` 1361-1369) but `CheckOverlappingLanes` never reads it for creatures: every person goes to `CheckPedestrian`, so stock cars give way on unmarked crossings exactly as on zebras. The person side (`CreatureTargetIterator` 36-103) waits only for a car whose reservation already reaches past the crossing (braking distance) or a Moving object on the overlap: no gap judgement. The person side of G11 is its own system (`Systems/UnmarkedCrossingSystem.cs`, after `HumanNavigationSystem`, no game code copied: it pulls the target back to the kerb edge the way the game's own clamp does). New stage 2 mode bit `Stage2Mode.UnmarkedGiveWay` (bit 7, stripped in `JRWholeJunction.Create`); with the setting off S2-50 is one bool test and driving equals 0.4.7. Diagnostics are always on (owner rule for 0.4.8) and independent of the count bit: `Systems/UnmarkedTrace.cs` (SharedStatic buffer, Interlocked counters, fixed ring), drained on the main thread with line caps.

0.4.3-probe (JR-T-007c: JR-T-007b paint at car exits merged with JR-T-010 driving) changes S2-20, S2-23 and S2-26 only (block count unchanged, 155 -> 144 lines with the review fixes); S2-28 / S2-28b text unchanged, the change is job side. The entrance band no longer reads the building (`Building.m_RoadEdge` / `m_CurvePosition`, `ConnectedBuilding`, `YellowBoxEntrance`): every saved exit key of every boxed building is matched on the main thread to the building's current car exits (Boxes/BoxFacts.cs `BoxedExitBands`, Core/ExitBands.cs; same kind + 2 m match as the paint) and becomes its own band at that exit's road edge and curve position; a 0.4.0-0.4.2 box not yet re-keyed drives at the exit nearest its old centre; an exit through the building's own access road is a junction box. `CarDrivingSwitchSystem` publishes the bands (`Core/Stage2Handoff.cs` `BoxEdgeHandoff`, sorted by edge then t, at most 63; more = a Warn line whenever the over-limit count changes) when the boxed building count or `BoxStore.Version` changes and every 64 steps. In the job `Stage2/JRBoxEdges.cs` holds the bands (`FixedList512Bytes` keys + t, about 1 KB) ONCE per job (S2-23); S2-26 hands each car's iterator a pointer to it (`UnsafeUtility.AddressOf`, valid while the job runs: the job struct does not move during `Execute`), and `JRYellowBox.BoxHold` finds the edge's first band by binary search and checks each band on that edge. Known limit: a band is a fixed +-L/2 of the entrance length around the exit's t, not the painted (clipped, bend-shifted) box outline. With the setting off nothing in the blocks runs (as 0.4.2).

0.4.4-probe (JR-T-012, vehicles leaving buildings give way; owner rule 16 Nov 2026: "Cars coming out of ANY building must give way, and when that building exit is a yellow box, must ALSO obey the yellow box rules.") adds S2-29, S2-30, S2-31 and extends S2-20 (read-only `Game.Buildings.Building` and `Edge` lookups, `m_JRExitBuildings` / `m_JRExitEdges`), S2-23 (their `GetComponentLookup(isReadOnly: true)`) and S2-26 (`SetExits(..., m_LeftHandTraffic)`: the game's own traffic side). Stock cause (decompile 2026-11-13): drive leaving lanes are node lanes owned by the road edge with `Unsafe | SideConnection | Yield` (`Game.Net/LaneSystem.cs` `CreateEdgeConnectionLane` 3646+, flags 3864-3876); stock `CheckOverlappingLanes` gives way there only to reservations (road vehicles never reserve their current lane) and to vehicles on the crossed lane whose 2 s look-ahead already reaches the overlap, so a vehicle in the junction or on the road before it is not seen. Spawn / car park exits: the vehicle drives its `Connection` lane unspawned at 277 m/s (`CarNavigationSystem` 1492-1497), the road lane is never iterated from a Connection lane (`goto IL_09bf`, 2022-2024) and it joins the road at <= 3 m/s within 1 m of the target (1873-1886): no give-way at all. On-duty emergency: `VehicleUtils.GetPriority` 108 (654-657), kept as stock. With the setting off S2-29 / S2-30 are one bool test each, S2-31 reads a false flag, the stage 2 int carries no exit bit (`Stage2Mode.ExitGiveWay`, bit 30, stripped in `JRWholeJunction.Create`): driving equals 0.4.3. The gap check is never gated on a box; at a boxed driveway exit the 0.4.3 S2-28 band hold still applies on the road lane, at a boxed spawn exit S2-30 checks both (Core `ExitGiveWay.Decide`).

1.4.0 (JR-T-033, owner rule 20 Nov 2026: wait for a zebra RIGHT BEFORE the zebra; Core/ZebraDriveUp.cs, Stage2/JRZebraDriveUp.cs). No new block ids; changed lines inside existing blocks only:
- S2-25: `BeginZebras` also gets the iterator's `ref m_CreatureData` (read-only; the game's people test for the zebra's people). Inside it: people on a zebra that was merged into an earlier spot's section, with the car eligible (not a box lane, its whole length clear of the zebras behind, able to stop before the zebra, room for the whole vehicle plus the margin past the section FREE NOW), make the people's distance the stop right before that zebra; the section's vehicle points are then judged over the drive-up window (arrival + estimated people wait, at most 10 s, + clear time) in `InScope`. Crossing a section, the room is asked again (the zebra rule's own scan) before the car moves onto a merged zebra with people on it or while it stands at it (hold `GetMaxBrakingSpeed` to the stop before that zebra, blocker = the room scan's, type Continuing, why `jr_zebra_section`).
- S2-08b: the why rule of a people hold is `m_JR.WhyPeople(...)` (new id `jr_people_wait_zebra`; `jr_people_wait_spot` now carries a reason as its extra: traffic / exit blocked / yellow box / tail on zebra / cannot stop / not a zebra section).
- S2-06, S2-11b, S2-12: the why line goes through `m_JR.WhyGap(...)` / `WhyGapExtra(...)`: a whole-junction gap hold of a drive-up section reads `jr_people_wait_spot`, reason traffic. Driving unchanged by these three lines (why tracking only).
- `HasRoomPast` (not a vanilla line): `FurtherBoxes` runs only with the box lookups set (`m_BoxLookups`); every free-now caller before 1.4.0 was a box caller, so unchanged.
Without people on a merged zebra nothing differs from 1.3.0 (offline differential, ZebraDriveUpSceneTests). JR off (stock CarDriving): not our code.
- JR-T-035 (review F1): the drive-up needs the Whole junction gap rule (its safeguard is the whole-junction gap check over the window). With the Stock gap rule (zebra rule on) the car waits at its spot as in 1.3.0; `jr_people_wait_spot` carries reason `stock_gap_rule`. (review F4): no drive-up when the people on the target zebra cannot hold the car (estimated wait 0, nobody standing): today's distance and the normal gap time.
- JR-T-036 (1.4.1, owner ruling 20 Nov 2026: a person STANDING STILL on the target zebra = exit blocked, as the yellow box rule). No vanilla line changed (job side, S2-25's `BeginZebras` -> `DriveUpScan`): the people on the target zebra and the later zebras of its section are classified first with the box exit zebra wait's own test (`ExitZebraWait.Person`: the game's walking test speed^2 >= 0.01, a standing person only on or beside the car's way, the G23 waiting area excluded; no new threshold). One standing person (mixed included) = no drive-up: the car waits at its spot as 1.3.0, `jr_people_wait_spot` with reason `standing_on_zebra` (`ZebraDriveUp.Reason.PeopleStanding` = 8; order: stock gap rule, yellow box, standing, tail on zebra, cannot stop, exit blocked). Walkers only: the 1.4.0 drive-up unchanged. A person who stops after the car has passed its spot: the section is being crossed, so the stock pedestrian stop right before the zebra and the `CrossingZebraHold` room re-check apply (never onto the zebra, never pushing); a car that can no longer stop at its spot keeps the game's own distance (0.2.1 decision fix) and also stops before the zebra. F4's `PeopleCanHold` is now `wait > 0` (standing people never reach it).

1.5.0 (JR-T-038, owner rule 21 Nov 2026: a yellow box is an entry rule only; once in, a car drives as at any junction; owner correction the same day: G21 / G22 / G28 and every other owner-ruled box behaviour stay). No new block id; S2-37 changed (row above), no other vanilla line changed:
- Job side: `BeginLane` / `BeginZebras` take the stop-line variant only from the general "waiting spot" setting (a boxed lane no longer forces `atLine`); the UK right-turn turn spot (`TurnSpot`, `m_BoxTurnSpot`, the extra spot in `ZebraSections.Build`, `QueuedTurnersMayStand` in the room scan, setting `BoxTurnWait`, why id 48 `jr_box_turn_spot`) is removed. A standing vehicle ahead in the box now blocks entry for every car (the queued-turner exception went with the turn spot).
- Kept: the box room test at the line (`BoxHold`: look past, 1.3.0 sight, queue packing), G22 (`SameExitException`, at the line and in `BoxSpotRoomHold`), G28 (`ExitSameLane`), G21 (`EntryGiveWay`), the JR-T-024 exit zebra wait and `ZebraDriveUp` reason `Box` (proposed for removal to the owner, not removed).
- Core `StretchState.Classify` (item B, every junction under the Whole junction rule): a point the car can no longer stop before is never left as a give-way demand. It stops before an earlier conflict of the same stretch if it still can (Safety), else gets the game's give-way if it can stop before that lane's overlap start (Stock), else commits (Crossing: no give-way to predictions, the 2 s projection undone; braking for a vehicle physically in the overlap stays). 1.4.1 gave the game's give-way there, whose `GetMaxBrakingSpeed(num5 <= 0)` stopped the car inside the lane.
Offline: `BoxGeneralCoreTests`, `BoxGeneralSceneTests` (off / 1.4.1 / 1.5.0), `BoxGeneralRepoTests`, `BoxGeneralAdmissionTests`; existing box scenario, look-past and Fernbrook replay numbers unchanged.

1.4.0 (JR-T-033 B1, owner 20 Nov 2026: at a building access the pavement no longer counts like a zebra; cars have the right of way; Core/AccessCrossing.cs). One changed line inside an existing block (S2-08b), the rest job side:
- S2-08b: `NoPushPedestrian` also gets `JRWholeJunction.IsAccess(lane, ref m_CarLaneData)` (the car lane has `CarLaneFlags.SideConnection`, a driveway out or way in; evaluated only with the no-push rule on), and the why of a full stop is `m_JR.WhyNoPush()` (`jr_access_person_in_way` at an access, else `jr_never_push_people` as before). At an access the 0.3.2 full stop (no timeout) applies only to a person in the car's way (`AccessCrossing.PersonInWay`: the car's range on the pavement shrunk 0.25 m each side); a person beside it, also one the game holds at the range edge (G23's 0.5 m widening counted that one, so car and person waited for each other), gets the game's own creep (3 m/s). Never into a person in the way (safety floor): the full stop stays for them, and the game's own check runs for everyone.
- Job side (no vanilla line): the zebra sections never class an access's pavement as a zebra (`ZebraElements.Get`, `AccessCrossing.SectionKind`; 0.4.8 G7 made a leaving driveway's pavement a hard zebra: people on it held the car before it, plus its room-past hold); `InScope` leaves an access's pavement to the game's own check (no wait at the whole-junction spot for people there). Kept: S2-29's stop before the pavement for the ROAD gap and room on the joined lane (G7), the look-past no-stop zones (they never counted a pavement), G11 unmarked crossings (not touched).
- JR-T-035 (review F3): only the UNMARKED pavement is exempt (pedestrian lane without `PedestrianLaneFlags.Crosswalk`; `AccessCrossing.AccessPavement`). A marked zebra an access lane crosses stays a zebra: zebra sections, the whole-junction crossing (`InScope` now gets the crossing's flags from S2-05: `TryGetComponent` instead of `HasComponent`, same lane) and the G23 full stop (S2-08b passes `JRWholeJunction.IsAccessPavement(lane, m_Lane, ...)` instead of `IsAccess(lane, ...)`).
- A3: B1's in-way full stop needs "Cars never push through people on crossings" on; with it off an access gets the game's own creep (also for a person in the way), as "off = the game's behaviour" says.
- Counters (debug counting only, appended): `AccessPeopleStanding`, `AccessCarsHeld`. Always on (not a vanilla line): `Systems/PeoplePassSystem.cs` counts the people passes past a car on an access (`CountPeopleAtAccess`, in the nav timing log line as "people passed a car at a building access").

Effect on `tools/IlHash --copy` (expected): 48 of 80 identical (was 54), 21 shape only (was 19), 11 content
differences (was 7: the four stage 2 methods `IterateFirstLane` x2, `IterateNextLane`, `UpdateNavigationTarget` join
`CheckOverlappingLanes` and the others listed below, which also contain blocks now), 1 extra (`OnDestroy`, S2-24).
Field blocks of the iterator and the job are "shape only" because of S2-01 and S2-20.
0.2.2 (`tools/IlHash --copy`, managed build of 0.2.2): unchanged from 0.2.1: 48 of 80 identical, 21 shape only, 11
content differences, 1 extra (`UpdateNavigationTarget` already differed).
0.2.3 (`tools/IlHash --copy`, managed build of 0.2.3): unchanged: 48 of 80 identical, 21 shape only, 11 content
differences, 1 extra (S2-12 is inside `CheckOverlappingLanes`, which already differed).

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
- **Dead zero store** (`flag2` in `JRCarLaneSpeedIterator.cs` line 1176 with the 0.2.3 stage 2 blocks (0.2.2: 1173), 1159 before): a local assigned and never read; the build
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

0.4.5-probe (JR-T-012 fix; owner screenshot, 0.4.4 live: a vehicle leaving an industrial building onto a boxed Alderwood exit pulled out; timing log `spawn/gate 0` always) adds S2-32 and S2-33 (navigation only). Cause: S2-30 ran only for a `Connection` current lane, but the usual building exit is a lot: `InitializeSystem.cs` 288, `CarNavigationSystem.cs` 672 / 1037 turn a `ConnectionLane` with `ConnectionLaneFlags.Area` into `CarLaneFlags.Area` (not `Connection`), and the stock branch for an `Area` current lane (1795-1803, `MoveAreaTarget`, then the lane switch at 1873+ without the Connection join test) never reaches the iterator (`if ((LaneFlags & Area) == 0)` 1596 in the stock listing), so neither the road lane it joins nor any gap was ever looked at, and S2-30 (after `IL_09cd`) was never executed. S2-32 / S2-33 apply the same decision to an area lane's last stretch. Counted under `spawn/gate` of the timing log (also lot exits).

0.4.6-probe (JR-T-012 fix 2; owner, 0.4.5 live: a tractor-trailer and a dark truck leaving an industrial lot onto boxed Alderwood exits pulled out across / into traffic, timing log `spawn/gate 0`, `emergency passthroughs` 64 in one window). Causes: (1) S2-32 judged a lot-exit vehicle only when its braking speed to the stop point was already below its current limit (`hold < currentMax`), but the stock area branch hands the vehicle over to the road lane when its area-path target is within max(look-ahead, braking distance at 11.1 m/s + 1 m + pivot) (`CarNavigationSystem` num8, 1767-1772; `MoveAreaTarget` 2529), up to ~20 m before the join point, and S2-33 only held a vehicle that S2-32 had already judged busy in that same update, so a vehicle was handed over unjudged; (2) the distance to the join point was reduced by half the WHOLE train (`m_VehicleLength` with trailers), so a tractor-trailer looked past the stop point (`CanHoldArea` false = left to the game) ~8 m too early; (3) `ExitJoinLane` demanded `LeavesBuilding` of an area lane's owner chain and an edge-owned join lane (a lot beside a junction joins a node lane); (4) the area branch skips the later `AddCounts` (`goto IL_0a0a`), so S2-32's own counters were never added (`spawn/gate 0` and the missing `waits`), and every exit counter was added per car-step, not per vehicle. `emergency passthroughs` was NOT a misclassification: priority 108 exists only with `CarFlags.Emergency` (`VehicleUtils.GetPriority(Car)` 654-657; set only by the ambulance / fire / police / transport AIs), and every car-step of an on-duty emergency vehicle at a driveway or lot added 1. Fix: scan from `ExitGiveWay.AreaScanMetres` before the join point, front-based distance, area lane needs no building owner, node lanes allowed, counters from S2-32, per-episode counters through the shared event table (kinds, `ExitGiveWay.EventKey`), S2-34 probe counters.

G1 (JR-T-013a; owner, 0.4.6 live: vehicles leaving buildings still cut out and a dark truck turned right out of a building into the boxed Alderwood exit and blocked it; every timing window showed `hand-overs to the road from: lot 0, gate 0, other 2..28`) adds S2-35 and S2-36, widens S2-33, replaces the S2-34 probe body and extends S2-20 / S2-23 / S2-26 (`m_JRExitTraceOn`, `m_JRExitTrace` ring, `SetTrace`). Cause (decompile 2026-11-13): the hand-over real vehicles take is from a building's SPAWN LOCATION OBJECT, not a lane. `Game.Pathfind/PathfindTargetSeeker.cs` `FindTargets` (427-479) starts a building's path at its spawn locations; a Road-type spawn location adds the `SpawnLocation` entity itself as the target (`AddSpawnLocation`, ~680 `AddTarget(spawnLocationEntity, 1f)`), so the first path element is an object with a `Transform` and its navigation lane gets `CarLaneFlags.TransformTarget` (`CarNavigationSystem.cs` 1078-1091); the next element is the lane the spawn location is tied to, which `Game.Objects/SpawnLocationConnectionSystem.cs` (535-620, `TryFindLanes` 400-448) takes from the building's own sub-lanes, else the nearest lane within 32 m: the public road lane. The stock `TransformTarget | ParkingSpace` branch (copy ~1658) runs only `IterateParkingTarget` / `IterateTarget` (no road lane, so neither the 0.4.4-0.4.6 exit hooks nor the 0.4.3 box band S2-28 ever see the vehicle) and the target loop moves it onto the road lane as soon as it reaches the spawn point; it is `Unspawned` until then (`Game.Vehicles/VehicleUtils.cs` `CheckUnspawned` 1806-1821: `Connection \| ResetSpeed` or `TransformTarget \| ParkingSpace`) and appears in the road lane with no gap and no box check. The spawn object's `Owner` is the building, so the 0.4.6 probe counted it as `other`; lot 0 / gate 0 = these buildings have no lot path. The same branch carries vehicles leaving a parking space (`ParkingSpace \| FixedLane`, 1003-1023): building parking lanes, garages (`Connection \| ParkingSpace`, 1026-1043; they never reach S2-30 either) and kerbside parking lanes owned by the road (not building-owned: never counted in 0.4.6). A building's own road joined at a public node (`LaneSystem.cs` 5161 / 7033: no `SideConnection` / `Yield` flag, the node lanes belong to the public node) was not judged either. `cap hits` was the per-decision work cap (24 lanes / 96 vehicles), not the 63-band boxed-exit limit; `VehiclesBlock` counted every vehicle on a read lane against the 96 before its past-the-conflict / out-of-reach filters, and a cap hit erred to go. Fix: S2-35 (spawn point and parking), S2-36 (building road) with the same decision (`ExitJoinWaits`: join lane + crossed lanes, whole-junction gap judgement, then the 0.4.3 band room-past rule at a boxed exit); a junction-lane join takes its conflicts from the joined lane's `LaneOverlap`s (not `MergeStart`, not water) instead of the edge-lane lateral rule; S2-33 breaks for every held exit; only relevant vehicles count against the vehicle budget and a vehicle cap hit is busy (Core `ExitPaths.CapBusy`), lane budget 48 (a lane cap hit still errs to go). Kerbside parking is judged (owner rule "a building exit is a yielding arm"; give way when moving off). Not hooked: a vehicle whose path starts directly on a public road lane (`FindTargets` front-connection fallback, `AddEdgeTargets(Building.m_RoadEdge)` 466-478) is placed in the lane and drives as road traffic; a truck stopped in the road lane pulling away is already in its lane. Review F1 (0.4.7): the same holds for the Cargo spawn-location branch of `PathfindTargetSeeker` (`AddSpawnLocation`, 610-613 sets the flag when the target methods have `CargoLoading` and the path parameters do not; 660-668 then adds the spawn location's `m_ConnectedLane1` / `m_ConnectedLane2` as the path start; `SpawnLocationConnectionSystem` `TryFindLanes` 399-430 may tie them to a nearby road lane). In the 2026-11-13 decompile only `VehicleWorkType.Move` work vehicles take it (`WorkCarAISystem.cs` 239-243, `AreaLotSimulationSystem.cs` 504-508); their parameters drop `Road` (Offroad only), so they cannot drive a public road lane; every road vehicle that loads cargo has `CargoLoading` in both (`DeliveryTruckAISystem` 1091-1102, `ResourceExporterSystem` 183-193, `ResourceBuyerSystem` 881-891, `GarbageTransferDispatchSystem` 269-283, `MailTransferDispatchSystem` 268-280, `StorageTransferSystem` 288-298, `RandomTrafficDispatchSystem` 249-251) and goes through the spawn object (S2-35). Not held: the vehicle's current lane is already the road lane (`UpdateNavigationLanes` 662-675 takes the first path element with only `FixedLane`), `CheckUnspawned` (`VehicleUtils.cs` 1806-1821) shows it at the next AI step, and a hold would leave it standing in the traffic lane, worse than stock. Counted instead (S2-22, `ExitRoadStart`, both cases above): a vehicle unspawned on a public road lane without `ResetSpeed` (a gate exit's first road lane carries `ResetSpeed`, `CarNavigationSystem.cs` 1877-1886, and is not counted). With the setting off S2-35 / S2-36 are one bool test each and S2-33 reads a false flag: driving equals 0.4.6. Counters appended (`Stage2Counters` FeedingScans+34..+41) and the setting "Exit trace (log)" (default off; Core `ExitTrace` ring of 256 records written with `Interlocked` by the job, read without waiting by `JRStage2Bridge.BeginStep`, at most 20 lines a second).
0.4.7-probe (JR-T-011 part A, #17; built on 0.4.3 as an uninstalled "0.4.4", merged into 0.4.7 by JR-T-013m; touching boxes drawn as one; exit boxes stop at a median) changes no block (no block count or line change of its own); the change is job side and main-thread side. (1) Each band carries a carriageway mask (`Core/ExitBands.cs` `ExitBand.Carriageways`, `Core/MedianSplit.cs`): on a divided road (a `NetDividerPiece` with `m_BlockTraffic` starts a new `NetCompositionLane.m_Carriageway`, `NetCompositionHelpers.cs` 1207-1212 / 1357 / 1444) the exit's band holds only lanes whose `CarLane.m_CarriagewayGroup` low byte (`LaneSystem.cs` 4153) is on the building's side; `JRBoxEdges` gains `m_Mask` (`FixedList512Bytes<uint>`, about 256 B more). (2) `JRYellowBox.BoxHold` merges the bands of one lane that overlap or touch (`ExitBands.TouchMetres`) into one interval before the hold test, so a car is held once before the whole painted group, not let into the first band and then held inside the second (the paint draws them as one box). (3) A band ending within `ExitBands.JunctionJoinMetres` (2.5 m) of the lane end, where the car's next path lane is a node lane of a boxed junction, asks for room past that junction box too (`HasRoomPast` with that node as the box node): the paint bridges an exit box to a junction box across the marking margin, so they are one area. Paint and hover use `Core/BoxMerge.cs` (union of the boxes' straight-sided outlines, one hatch anchor per group); storage is still one record per box.

JR-T-013e (G2, #17 review fixes for the road-marking hide, part B) changes no block (no block count or line change of its own) and no copied file; it is main-thread paint side only. The marking writes (`CutRange` buffer and `Hidden` tag on the road's own marking lanes / objects, each with `BatchesUpdated`) moved out of `YellowBoxPaintSystem` (PreCulling, after the game's `PreCullingSystem`) into a new `Paint/MarkingRenderSystem.cs` registered `UpdateBefore<MarkingRenderSystem, PreCullingSystem>(SystemUpdatePhase.PreCulling)`: `PreCullingSystem` collects `BatchesUpdated` entities (`PreCullingSystem.cs` 2428-2436, 652, 1895) and `PrepareCleanUpSystem` / `CleanUpSystem` remove the tag at the end of the same frame (`SystemOrder.cs` 50-54, `PrepareCleanUpSystem.cs` 29-40, `CleanUpSystem.cs` 24-31, 53), so a tag added after `PreCullingSystem` was never seen and the hide / unhide did not re-render. The bookkeeping (strip before a save, restore after, clear in any state, save fallbacks) is `Core/MarkingLedger.cs`, tested offline (`MarkingLedgerTests`). The setting "Hide road markings under yellow boxes" now defaults OFF until the owner's live gates pass (PROBE-STAGE4-BOXTOOL.md sections 16.2 and 17 G2).
JR-T-013b (gaps G3 and G4; no version bump; 1.5.0 generalised G3 to every boxed lane, see S2-37). **G3** (owner shot, Alderwood St x Alderbrook St, boxed 4-way, node at (-1896.5, 594.3, 272.6): a lorry turning right from the side road stood across the oncoming lane with its exit full; the main-road straight traffic waited for it). Cause: the box exit-room test ran only at the stop line. `JRYellowBox.BoxHold` classifies a car whose current lane is a lane of the boxed node as `Inside` (`inBox = m_FirstLane || OnNode(m_CurrentLane, ..)`, then `BoxDriving.Classify` returns `Inside`, which never holds), so a turner that entered with room and waited at its turn spot (0.4.2 UK right-turn exception, `BeginLane` second spot from `TurnSpot`) was never re-tested. When the oncoming gap came its whole-junction decision at the turn spot judged only the oncoming points, so it crossed on the gap even though its exit had filled meanwhile (exit queue grew while it waited, an oncoming left-turner took the room, the Alderbrook boxed car exits' entrance bands held the exit queue ~16 m from the node) and stopped behind the exit queue with its body across the oncoming lane. Straight traffic was then held by the stock car-following / lane-object checks against a vehicle physically on its path (the lane-object test in `CheckOverlappingLanes`, decompile `Game.Simulation/CarLaneSpeedIterator.cs` 1352-1388: a vehicle on the overlapping lane whose `curvePosition.y` is past the overlap start goes to `UpdateMaxSpeed`); the priority itself was never inverted (timing log `priority changed 0` all session: `LaneOverlapSystem` 1360-1400 already gives the LHT right-turner the lower priority). A turner waiting at its spot does not hold the oncoming straight: it stands before the overlap start, and the stock skip `else if (curvePosition.y <= @float.z + num3) continue;` (same file 1382) ignores it; a standing car gets no 2 s look-ahead there (`objectSpeed` 0, 1371-1374). Repo cites: `Stage2/JRYellowBox.cs` 88-91, `Core/BoxDriving.cs` 53, `Stage2/JRWholeJunction.cs` 390-394. Fix: block S2-37 re-runs the room test at the turn spot (see the table). The scan reads only the turner's own path, never an oncoming lane, so the new wait ends on the exit, not on oncoming traffic (no new wait cycle). Counter appended at the end: `right-turn waits for exit room N` (`Stage2Counters.BoxTurnRoomWaits`, timing line after the exit part). Tests `G3TurnSpotRoomTests`, `G3CounterTests`, `G3RepoTests`.

**G4** (watch one junction, debug, default off): settings buttons "Watch the junction at camera centre (log)" / "Stop watching the junction" (Debug group). The pick is read-only (`Systems/JunctionWatchPick.cs`, the paint test's camera-centre pick, any junction); the node goes through Core `WatchHandoff` to `JRStage2Bridge`, slot 0 of the watch ring. While a node is watched the count bit is on (the per-car results the counters use say which rule bound the limit). S2-21 / S2-22 changes above: per standing car at that node (one of its lanes is the current lane, or `BeginLane` checked one of its lanes) one event: car, blocker, current lane, rule (`JunctionWatch.Rule`), speed and limit. `BeginStep` drains and logs them on the main thread (`WatchLimiter`: one line per car, rule and blocker per 512 steps, 20 lines per 256 steps, the rest summed). No driving effect; nothing saved; a load clears it. Tests `WatchTests`, `WatchRepoTests`.
## G6: no parking in yellow boxes (0.4.7, JR-T-013d)

Not a change to the driving copy (Vanilla/ and `tools/vanilla_copy.py check` are untouched). Two things are listed here
because they rely on game code:

- **Copied excerpt outside Vanilla/: `JunctionRules/Parking/ParkingFreeSpaceCopy.cs`.** Source: `Game.Pathfind.ParkingLaneDataSystem.UpdateLaneDataJob`,
  Game.dll 1.6.4f1, `GetBlockedRange` (decompile 310-334) and `CalculateFreeSpace` (547-731). The method bodies are unchanged;
  `ParkingRepoTests` proves them equal to the decompile after whitespace is collapsed. Deviations: the bodies sit in an
  own struct, the methods are `internal` instead of `private`, and `laneObjects` / `laneOverlaps` are `NativeArray`
  instead of `DynamicBuffer` (same `Length` / indexer). The caller sorts the lane objects first, as the job does (226),
  and passes the lane's overlaps without the mod's own entries. It is used only right before a save, to put the game's
  own `ParkingLane.m_FreeSpace` back on the lanes the mod blocks. `VanillaHeaderTests` allows this one file by name.
- **Own entries in the game's `LaneOverlap` buffer of parking lanes.** One entry per blocked curve range, marked by
  `m_Other == the lane itself`, no flags, Other range = This range. The game never makes a lane overlap itself.
  Stock readers that skip such ranges: `VehicleUtils.FindFreeParkingSpace` 1477-1600 (called by `ValidateParkingSpace` 1344 and
  `SetParkingCurvePos` 321 with `ignoreDriveways: false` for personal cars, maintenance vehicles and taxis),
  `ParkingLaneDataSystem.CalculateFreeSpace` (the pathfinder's free space) and `FixParkingLocationSystem` (only for
  Updated / Deleted lanes, 1778-1795, so parked cars are never moved by the mod). `LaneOverlap` is `IEmptySerializable`
  (`Game.Net/LaneOverlap.cs` 9): its entries are never saved. `LaneOverlapSystem` (Modification4B, `SystemOrder.cs` 178) rebuilds the
  buffer of an Updated lane without them; `BoxParkingSystem` (ModificationEnd, ahead of `ParkingLaneDataSystem`,
  `SystemOrder.cs` 251) puts them back the same frame. On a game patch: re-run `ParkingRepoTests` against the new
  decompile and re-check that `FindFreeParkingSpace` / `CalculateFreeSpace` still skip overlap ranges.
- **Saved value touched: `ParkingLane.m_FreeSpace` only (`ParkingLane` is `ISerializable`, `Game.Net/ParkingLane.cs` 7, 25).**
  `BoxParkingSaveGuardSystem` runs in `SystemUpdatePhase.Serialize` ahead of `SerializerSystem` (`SystemOrder.cs` 735).
  It writes the stock value, computed by the excerpt above, so the save holds what the game would have saved without the
  mod. After the save the lanes get `PathfindUpdated` and the game recomputes the value with the box ranges. If the
  value cannot be computed for a lane, the guard writes max(current, one car length): the save may show the lane as
  freer than stock, never fuller. `ParkingLane.m_Flags` (`ParkingDisabled`, 0x2000) is never written.
- **Review fixes (0.4.7, G6 review).** F1: `LaneOverlapSystem` also rebuilds (clears, `UpdateLaneOverlapsJob` 1077) the
  buffers of every edge connected to an Updated node without tagging those edges or their lanes Updated
  (`AddNonUpdatedEdgesJob`, `Game.Net/LaneOverlapSystem.cs` 26-56; owners from `m_UpdatedOwnersQuery` 1893). A junction box
  lists its node and edges but not the far-end nodes of the edges, so an edit at the next junction wiped our entries
  unseen. `BoxParkingSystem` now re-checks every lane it blocks whenever a road node / edge with lanes is Updated (not
  Temp) and re-applies when one no longer holds exactly its entries (`ParkingMask.HasExactly`; unchanged lanes are not
  written). F2: the no-parking system and its save guard are registered only when the version gate allows the copy
  (`Mod.Gate.CopyAllowed`, as `PeoplePassSystem`); otherwise one log line and parking stays stock. F3: a failure inside
  `Apply` also opens the lanes that Apply had already written (tracked before each write), not only the previous set.

0.4.7-probe (JR-T-013m merge) = installed 0.4.6 + #17 (JR-T-011 part A + B with the G2 review fixes) + G1 (S2-35, S2-36), G3 (S2-37; JR-T-013b named it S2-35 on its branch, renumbered because G1 owns S2-35 / S2-36), G4, G5, G6. Counter order (`Stage2Counters`): the 0.4.4-0.4.6 exit counters, then the eight G1 counters (`ExitJoinSpawnPoint` .. `ExitCapVehicles`, FeedingScans + 34..41), then the G3 `BoxTurnRoomWaits` (FeedingScans + 42), then the G1 review counter `ExitRoadStart` (FeedingScans + 43); no new stage 2 mode bit in 0.4.7 (`Stage2Mode.ExitGiveWay`, bit 30, dates from 0.4.4); the exit trace and the junction watch travel in their own handoffs, not in the mode int. `docs/vanilla_copy_check_0.4.7.txt`: 35 blocks, 273 lines, 0 unexplained (271 before the review fixes; S2-22 + 2 lines).


0.4.8-probe G7 (JR-T-014; owner, 0.4.7 live 17 Nov, Alderwood Street, left-hand traffic): trucks from the SOUTH lots (driveway exits of buildings 1011673 and 688899, boxed) turned right across the westbound near lane into a stopped eastbound queue and stood across the road, in front of a westbound truck. Causes (code): S2-29 waited before the first ROAD lane with no room test on the joined lane; the only room test was the 0.4.3 band hold S2-28 role `Leaving` on the joined (far) road lane, i.e. with the vehicle already across the near lane (timing `entrance band holds: leaving driveway` stuck at 128 car-steps per window for ~15 minutes); `ExitGiveWay.VehicleBlocks` let a road vehicle standing before the conflict never block (the westbound truck held before the band by `entrance band holds: main road` ~384); the scan ran only when the exit hold was binding; crossed lanes used the time to reach, not to clear. Owner rule 17 Nov: the sidewalk an exit crosses is a zebra Z directly followed by the road lanes: the whole decision before the sidewalk (S2-25 zebra sections now count a driveway lane's pedestrian overlaps as Z). Building-road `not judged` (== `building road` in almost every 0.4.7 window) was a counting artefact: the hand-over update has the front past the stop point, so no decision in that update; a recent recorded Go now counts.

0.4.10-probe (JR-T-016: G18 / G19 / G20 and the owner's box-rule sweep). 40 blocks, 318 lines (new S2-52, 4 lines: the
light request of review R1, table above); three block conditions and one call changed: S2-28 / S2-28b / S2-37 `if (m_JR.m_BoxRule && !JunctionRules.Core
.ExitGiveWay.IsEmergency(m_Priority))` (on-duty emergency vehicles, stock priority 108, pass yellow boxes as they pass
exits: stock behaviour for them); S2-26 `SetBoxes(..., ref m_JRBoxEdges, m_LaneSignalData)` (the job's existing
read-only signal lookup, for "a red / amber light inside the room window past a box = no room") (1.1.0 JR-T-024b: `..., m_LaneSignalData,
m_CreatureData, blocker.m_Blocker)`, see the JR-T-024 section). Everything else is job
side: the exit / entrance bands are the painted windows projected onto each lane (Core/LaneWindow.cs; 0.4.3's "known
limit" above is gone: 0.4.3-0.4.9 mapped the edge t through `EdgeLane.m_EdgeDelta`, but lanes are built on the cut
`EdgeGeometry` (`Game.Net/LaneSystem.cs` 2652, 2661-2662, 4101-4105), so bands sat up to 13 m off the paint); box room
is free now (RoomScan.FreeNow); the room window extends past a further box / boxed junction and stops at a red / amber
light (`JRWholeJunction.FurtherBoxes`).

0.4.11-probe (JR-T-017: G21, vehicles turning INTO a building give way). No copy change: 40 blocks, 318 lines as 0.4.10
(`vanilla_copy.py check`: 0 unexplained differences). Job side only: the S2-29 call (`ExitDrivewayHold`, every lane the
look-ahead walks) now hands a ROAD lane whose next path lane is a drive's way in (`SideConnection` without `Yield`) to
`JRExitGiveWay.EntryRoadHold` (Core/EntryGiveWay.cs), which holds the vehicle on its own lane before the painted box line
(or, unboxed, where the way in leaves the lane) while a crossed road lane of the way in is busy by the exits' judgement
with the whole-length clear time; the block's speed / blocker / blocker type lines are unchanged (type Crossing, blocker =
the road vehicle it gives way to). Stock cause: `Game.Net/LaneSystem.cs` `CreateEdgeConnectionLane` (way in = edge-owned
node lane, `Unsafe | SideConnection`, no Yield: 3858, 3868-3880); `LaneOverlapSystem` 1161, 1386-1389 (Unsafe tie-break,
the stock give-way is the 2 s look-ahead on the crossed lane only); `CarNavigationSystem` 920-923 / 2277-2287 (the way in's
RequestSpace keep-clear at priority 96, queues only). PROBE section 21. Owner box rule (17 Nov): at a boxed entrance the turner waits only before the box line and never inside the box;
`JRWholeJunction.BeginLane` (S2-04, block unchanged) leaves out the whole-path decision on a way in that leaves inside a
painted box (its spot lay inside the box), using the curve lookup S2-28's `BoxHold` keeps for the step. Review 1: keep-clear
of other drives' crossings, nearest-only present vehicle for entrant scans, true crossings only, way in must start on the
lane.
G23 (0.4.11): S2-08b's call hands three more stock values (`laneObject.m_CurvePosition.x`, `m_CurveOffset`, `m_Curve.m_Length`;
same line, block line count unchanged): the 0.3.2 full stop for people applies only to a person whose position on the
crossing lies inside the car's range on it (+-0.5 m); a standing person beside it (waiting at the kerb for their own green)
gets the game's own creep past (CheckPedestrian's distance test) instead of holding the car until the car's red. Job side:
the exit / entry gap scans no longer count a vehicle standing in a queue held by a red / amber light as "present".
G23 owner follow-up (0.4.11): new block S2-53 (4 lines, table above): 41 blocks, 322 lines inside. Main thread: the watch pick takes the junction nearest the camera's aim point within 25 m (was: within 8 m of the centre line); watch lines name the blocker's position (person: lane, walking speed, waiting for a walk signal).
G25 (0.4.11, owner option c): new block S2-54 (6 lines, table above): 42 blocks, 328 lines inside.

0.4.12-probe (JR-T-018: G26, a yellow box never goes over a median). No copy change: 42 blocks, 328 lines as 0.4.11
(`docs/vanilla_copy_check_0.4.12.txt`: 0 unexplained differences). Box shapes only (`Core/MedianSplit.cs`,
`Core/MedianNose.cs`, `Boxes/BoxShapes.cs`, `CrosswalkStrip.Median`); the driving rule reads the same shape and mask.
PROBE section 22.

1.0.1 (owner 19 Nov 2026: signalised zebra with people not on green). No copy change: the S2-25 hook and its line count
are unchanged; job side only (`Stage2/JRWholeJunction.cs` `BeginZebras` / `SoftZebraHold`, `Core/ZebraSections.cs`
`ZebraKind` / `SoftHoldSpot`). A zebra whose pedestrian lane has a `LaneSignal` showing Stop or SafeStop (people's light
red or ending) is a soft zebra: it does not split the path into zebra sections, so a car may stand on it at its normal
waiting spot while it gives way (e.g. a turner waiting for oncoming traffic). It still does not move onto it while there
is no room past everything it crosses on the lane (exit blocked): the same room scan and the same same-exit exception as
the zebra sections. Unsignalised zebras, zebras with people on green and further zebras on the route (room scan) are as
in 1.0.0; people on the crossing still stop cars (stock check). JR-T-021C: the hook's blocker (G17) is the one of the
hold actually returned (soft or section hold, the smaller; `ZebraSections.SoftHoldBinds`).

1.1.0 (JR-T-022 / 022b / 022c / 022d: look past the car in front at a box; owner 19 Nov 2026). Copy change: one call
added inside block S2-26 (JR-T-022d: `SetSight` hands the job's own stock `m_StaticObjectSearchTree`, `m_TransformData`
and `m_JRExitBuildings` to the iterator by reference; the stock system already takes the tree read-only and registers the
job as its reader, so no dependency changes; `vanilla_copy.py check` unchanged outside the blocks). Job side
(`Stage2/JRWholeJunction.cs` box room test, `Stage2/JRSight.cs`, `Core/ZebraSections.cs` RoomScan, `Core/SightLine.cs`), all behind the new
setting **Look past the car in front at a box** (default on; off = 1.0.1 bit for bit, proven by a 20000-case RoomScan
differential and a step-by-step trace hash over the box scenario set):
- Mode bit 31 (`Stage2Mode.BoxLookPast = 1 << 31`, set only with the box rule; in `Stage2Mode.Stripped`; every use is a
  bit test). New counter `Stage2Counters.BoxLookPastAdmitted` (last index), on the timing line only while the setting is on.
- Rule: in a box room test (free-now rule) a vehicle past the box whose free-now room is short still gives room when it
  moves away at 3 m/s or more and its rear plus `GuaranteedTravel` (its braking distance less two steps of travel) leaves
  the car's length plus margin; a vehicle the car follows through the box below 3 m/s refuses.
- Sight: a 50 m straight-line radius around the driver (100 m since 1.3.0; `SightLine.FirstExit` per path lane); the room relied on must end
  inside it and within the path read (until 1.2.0; 1.3.0: nothing slow within sight -> go). Cars and vans never block the view. JR-T-022d (owner rulings 19 Nov, binding):
  - A large vehicle (`CarData.m_SizeClass` Large) ends the view: it is judged itself with its position plus guaranteed
    travel less `RoomScan.LookPastLargeMargin` (5 m, the one general caution: room for a car standing unseen ahead of
    it), the vehicles beyond it are not read at all (neither for nor against), and the room relied on must end by its
    front. JR-T-022f (owner call 19 Nov): a large vehicle queued ahead of the car in / before the box switches the
    look-past off for that decision (`RoomScan.AddQueued` large: the rest of the scan takes the 1.0.1 path and the verdict
    is the free-now verdict; no hold of its own), so the setting never refuses what off allows (random differential with
    queued large vehicles: 0 refusals).
  - Buildings block the view: when the look-past is about to give the room, the straight 2D line from the driver to each
    vehicle it credited and to the end of the room relied on must cross no building footprint (prefab
    `ObjectGeometryData` bounds placed by the `Transform`: the lot box for ordinary buildings, the mesh bounds for
    standing ones; a footprint that contains the driver does not block, JR-T-022e; `SightLine.SightBlocked`). Buildings
    are found per line in the stock static object search tree (cells and items the line itself crosses,
    `SightLine.SegmentHitsAabb`; `Building` entities only). No stored data. This replaces the interim "next junction ends the sight" cut, which stays only when the driver's position is
    not known.
  - An articulated vehicle is one vehicle: its trailer and tractor (separate lane objects, named by the same controller)
    never hide each other and the whole artic is credited by its leading element in path order (JR-T-022c).
- The safety stop is untouched (the decision runs again every step while the car can still stop at the line). Rare
  misjudgements that end with a car standing in the box (offline: a queue past the box that stops dead, harder than any
  game vehicle can brake) are accepted by design.

1.3.0 (JR-T-029: 100 m sight, nothing slow within sight -> go, the packed queue skips no-stop zones; owner rule 20 Nov
2026, binding). No copy change (`vanilla_copy.py check` unchanged). Behind the same setting (off = 1.0.1 bit for bit:
every new call tests `scan.LookPastOn`; the off differential and trace hash are unchanged).
- Sight 50 m -> 100 m (`RoomScan.LookPastSightMetres`). A vehicle whose rear lies beyond the sight edge is not read at all
  (`AddLook`, counted in `RoomScan.BeyondSightSkipped`). Large vehicles and buildings still block the view (unchanged); a
  large vehicle queued ahead keeps the free-now rule (unchanged).
- Nothing stopped or slow (< 5 m/s) within sight and no red line: the car goes. The 1.2.0 sight-end hold (refusal
  `BeyondSight`, "the room relied on must end inside the sight") is removed, in `ExtendPast`, `FinishLookPast` and the
  building test (`FinishSight` no longer tests the end of the room; credited vehicles still are). Standing vehicles
  behind a building are still read (a hidden standing car is never assumed away).
- A stopped / slow vehicle or a red line seen: the vehicles between are packed nose to tail behind it
  (`RoomScan.PackedRoom`), each that would overlap a no-stop zone standing before it; the scan reads until the packed
  queue can no longer reach the car's room (`RoomScan.PackedReach`) or the sight edge. No-stop zones
  (`RoomScan.NoStopZone`, at most 8, nearest kept): other yellow boxes (a boxed junction's lanes; exit / entrance bands on
  the path lanes, also later on the box's own lane), zebras (`ZebraSections.ZebraKind == Zebra`: the soft zebra of a
  signal whose people have red is not a zone) and other junction interiors (node lanes of a node with 3+ road arms: `RoomScan.IsRoadArm`, a
  footpath or track joined to a road does not count, JR-T-030; and level crossings, a node car lane with
  `CarLaneFlags.LevelCrossing`: the queue may not stand on the track, also with only 2 road arms, JR-T-031). The job reads up to 6 path lanes past the box with the look-past on (`JRWholeJunction.MaxLookLanes`; 3 off, unchanged).
- New refusals when a zone push alone turns go into a hold: `queue_pushed_back_by_zebra`, `queue_pushed_back_by_box`,
  `queue_pushed_back_by_junction` (`LookPastRefusal` 6-8). `BeyondSight` (3) is no longer set.
- Unchanged: people on zebras, waiting turners, the exit-zebra rule, the soft-zebra rule, the safety stop.
- Offline: Fernbrook replay held / needless / stood-in-box 9 / 7 / 0 (1.2.0) -> 1 / 1 / 0. The box scenario set with
  its exits closing dead past the sight edge grows (unseen 55 -> 200 standing, nose 0 -> 3; seen 57 -> 211, nose 0 -> 3):
  the cost of reading nothing beyond 100 m (keeping the 1.2.0 sight-end hold: 52 / 45, nose 0). Accepted misjudgement
  class; guarded at the measured levels.

1.2.0 (JR-T-027: predict each vehicle ahead from what is visibly ahead; owner rule and simplification 20 Nov 2026,
binding). No copy change (`vanilla_copy.py check` unchanged). Core only (`Core/ZebraSections.cs` `RoomScan.AddLook`),
behind the same setting (off = 1.0.1 bit for bit, unchanged path). Replaces the 1.1.0 worst-case `GuaranteedTravel`
credit for ordinary vehicles in and past the box:
- An ordinary vehicle (not large) moving at 5 m/s (about 18 km/h) or more is expected to carry on; it stops only behind the first
  visible standing vehicle / tail of a standing queue, or at a red stop line (the scan reads these where they are). People
  on a zebra and waiting turners do not count (exit-zebra rule and turner exception handle them).
- A vehicle standing or crawling (below 5 m/s) and a red stop line count where they are now. `LookPastMinSpeed` was 3 m/s
  (about 11 km/h) in 1.1.0; 1.2.0 raises it to 5 m/s (JR-T-028: a crawling, decelerating queue tail counts as standing;
  the same threshold refuses a vehicle followed through the box, and applies to large vehicles).
- The car enters when, with every vehicle ahead placed at that point, there is room for its whole length plus margin
  past the exit, and the road up to the predicted front of each vehicle it relies on lies inside the 50 m sight;
  otherwise it waits at the line (refusal `BeyondSight` when only the sight cut refuses).
- Large vehicles are as 1.1.0 (guaranteed travel less 5 m; they end the view); a large vehicle queued ahead = free-now.
- Safety stop unchanged; a queue that closes after the car committed can leave it standing in the box (accepted).
  The why-slowed readout reports the predicted-room shortfall when the look-past is on.

1.1.0 also (JR-T-024, owner ruling 19 Nov 2026, BINDING: waiting for people at a box's exit zebra). No copy change (no
block or line count changes; `vanilla_copy.py check` unchanged). Job side only (`Stage2/JRExitZebraWait.cs`, the
room-ok return of the junction box hold in `Stage2/JRYellowBox.cs` `BoxHold`, `PedestrianDistance` in
`Stage2/JRWholeJunction.cs`, rule in `Core/ExitZebraWait.cs`), all behind **Look past the car in front at a box** (off =
1.0.1 bit for bit: every new path tests the setting first; offline trace differential over 400 random scenes).
- Before: the box room test reads only the road past the box (from its lane end), never the box's own exit zebra.
  People on that zebra held the car where the zebra sections put the zebra's decision: with the Whole junction rule the
  exit zebra is almost always one decision with the box line (the car does not fit between the last crossing point and
  the zebra), so ANY person the game's check counts there, walking or standing, held the car at the line; with the
  Stock gap rule the exit zebra usually has its own spot inside the box, so the car drove in and waited just before it
  for anyone, walking or standing (a long vehicle that does not fit: at the entry zebra / line); with "Cars never stop on
  a zebra" off, the game's own pedestrian stop at the zebra (inside the box).
- Now, for a car before the line that can stop there and whose box room test passed: the exit zebra of the lane (its last
  zebra, starting in the lane's second half) is read: everyone on its pedestrian lane, by the game's own walking test
  (`Moving`, speed^2 >= 0.01; with "Cars never push through people" a person standing in the 1 m waiting area at a lane
  end is not on it, as G23). One standing person = hold at the box line (blocker = that person). Everyone walking: room
  past the box is asked again with the free-now rule alone (no look-past credit: the car will stand before the zebra;
  since the merge with JR-T-022d an explicit `HasRoomPast(..., lookPast: false)`, so no sight test, building query or
  hidden-vehicle hold either);
  free and the whole vehicle fits between the entry zebra (or the line) and the stop point before the exit zebra (the
  zebra sections' own fit: a bus in a small box would otherwise stand with its tail on the entry zebra) = no hold, and
  the pedestrian check for that zebra on that lane this car-step uses the game's own distance (the car stops a stop margin
  before the zebra, inside the box) instead of the section's spot; not free or not fitting = hold at the line (asks a
  red light inside the window for green, review R1). Nobody there = unchanged. Inside the box nothing changes (the game's
  own stop before the zebra; people who step on, or stop, after the car entered keep it before the zebra).
- Counters `Stage2Counters.BoxExitZebraWait` (car-steps standing in a box just before its exit zebra while somebody is on
  it: not counted as standing inside a box or as a nose) and `BoxExitZebraLine` (car-steps standing at a box line held for
  its exit zebra), the last two indices, on the timing line only while the setting is on.
- Limits: only the exit zebra on the junction lane itself (a box whose path continues on a further lane of the same node
  is not read past the first lane); exit / entrance bands are not covered (no zebra inside a band).
- JR-T-024b (review findings O1-O3). One copy change: S2-26's `SetBoxes` call gets two more arguments (same line, block
  and line counts unchanged): the job's existing read-only `m_CreatureData` lookup and `blocker.m_Blocker`, the car's own
  `Blocker` of its last step (the game's `CheckBlocker` writes this step's after the iteration). (O1) A person STANDING on
  the exit zebra counts only on the car's way over it or beside it: inside the overlap's range on the pedestrian lane
  (`LaneOverlap.m_OtherStart` / `m_OtherEnd`, the range the game's `CheckPedestrian` and G23 `PersonInPath` use) widened by
  G23's 0.5 m plus 1 m for the game's beside test (`CheckPedestrian` counts a person outside the range within half the car's
  width + half their size of the car's overlap line, less up to 1 m for facing it; the range is the car lane's footprint,
  so that reaches at most about 1 m past it). Someone standing on the far half of a wide zebra no longer holds the car at
  the line. Walking people still count anywhere on the zebra (conservative). (O2) Only creatures are people: the game's own
  check calls `CheckPedestrian` only for lane objects with a `Creature` component (copy `CheckOverlappingLanes`); a
  cyclist on the crossing is a vehicle there (`UpdateMaxSpeed`), so a cyclist on the exit zebra is not counted for the
  zebra wait (the game still brakes for them as a vehicle inside the box). (O3) `BoxExitZebraWait` counts a car standing
  at the spot before the exit zebra only when its own blocker is a person on that zebra (the game's pedestrian stop names
  that person); standing there for another reason (a vehicle ahead, the exit blocked, a light) while somebody is on the
  zebra counts as standing inside the box / a nose, as before JR-T-024. All three behind the setting (off = 1.0.1).

1.1.1 (JR-T-025, why is this car slowing; player-facing: "select a car to see why it slows"). Diagnostics only, no
driving effect. New blocks S2-55 .. S2-59 (12 lines, table above): 47 blocks, 406 lines inside. Existing blocks extended
with record lines only (each behind the tracked-car flag: the iterator's `m_WhyOn` / the local `jrWhyOn`, set in S2-21
by one entity compare per car; the job writes no job field per car: the record lives in a one-element scratch array
that only the tracked car's worker touches, `JRWhy.Ref`): S2-01, S2-02, S2-03, S2-06, S2-07, S2-08b, S2-11b, S2-12, S2-25, S2-28, S2-28b, S2-29, S2-37 (iterator) and
S2-20, S2-21, S2-22, S2-23, S2-30, S2-32, S2-35, S2-36 (nav). The binding (smallest) limit is recorded with its rule
(stock or Junction Rules), blocker, lane, distance and one rule number (`Core/WhySlowed.cs`); a lowering no site tagged
is classified from the game's own blocker record (`WhyTrack.Settle`) and anything still unexplained reports as stock
"other", never as a Junction Rules rule. One car is tracked: the game's selected vehicle wins, else the car the API asked
for (`JunctionRulesApi.WhySlowed`). Output: JR log lines (tracking change, each reason change, at most 2 per second)
and the API (current reason and the last 20 changes per car). A Junction Rules hold whose hook named no rule reports as
"jr_other". The readout (tracked API car, histories) is reset on every city load and on the copy's release, so a reused
entity id never maps to an old car. No info-panel line in 1.1.1.
