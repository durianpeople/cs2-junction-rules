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
| S2-05 | iterator | 1 | 2 | after `blockerType` in the loop | `jrStart` (overlap start seen from the car), `jrInScope` (at or after W and yield-checked); 0.3.3: tells `InScope` whether the other lane is a pedestrian lane (a crossing the car fits before is out of the rule). |
| S2-06 | iterator | 1 | 1 | after `maxBrakingSpeed` (blocking reservation) | Per point phase (0.2.1): later stretch / deciding: brake to the spot; crossing with a busy uncrossed point: safety stop before the first uncrossed point if it can stop, else no give-way; cannot stop at the spot: stock. 0.2.4 (text unchanged): no hold when S2-13 marked the overlap (turner's reservation alone, turner gives way). |
| S2-07 | iterator | 1 | 1 | after `maxBrakingSpeed3` (space request) | 0.2.1 `SpaceYield`: holds a car at its spot; dropped while crossing (deadlock). |
| S2-08 / S2-08b | iterator | 1 / 1 | 4 / 2 | around `CheckPedestrian` | Before commit the pedestrian check uses the distance to W; the game's `num5` is restored after. 0.3.2: with "Cars never push through people on crossings" on, the check runs against an open limit (saved before), then `NoPushPedestrian` replaces the game's creep (5 s push timer or standing person) by the full-stop speed at the same distance and merges the saved limit back. |
| S2-09 | iterator | 1 | 1 | after `GetObjectSpeed` in the object loop | Keeps the other car's position before the 2 s priority look-ahead. |
| S2-10 | iterator | 1 | 2 | after the look-ahead block | `AfterProjection`: while crossing (no safety stop) the look-ahead is undone unless the other car is already past the overlap start; 0.2.3: while deciding at W or in the safety re-check it becomes `2 s + own arrival time`; `jrProjected` = the car counts only through the look-ahead. |
| S2-11 / S2-11b | iterator | 1 / 1 | 5 / 3 | around `UpdateMaxSpeed` in `CheckOverlappingLanes` | Before commit: braking distance to W for projected cars and cars in a crossing (merge followers stay stock); `num5` restored after; debug counters. 0.3.2: with the setting on, `m_Ignore` is hidden for this one call when the object is a bicycle on a pedestrian lane (no IgnoreBlocker creep through a cyclist on a crossing); restored after. |
| S2-12 | iterator | 1 | 10 | `CheckOverlappingLanes`, after the reservation priority `num6 -= num9` | 0.2.3 `FeedingLaneYield`: while deciding / re-checking, every car on the lanes feeding this conflicting junction lane that wins the stock priority test (`GetPriority(car) - num6 > 0`, as S2-10; JR-T-002g) as a virtual reservation (stock reach + speed x own arrival time) through the stock test `offset > num10 \|\| priority > num6` with the game's own `num10` / `num6`; on a hit, the stop at the spot / before the first uncrossed point is applied like a blocking reservation. 0.2.4: also passes the stock-read conflicting-lane signal (`componentData2`) and the read-only `CarLane` / `ObjectGeometryData` / `LaneReservation` lookups: cars stopping at that lane's red / amber are skipped; deciding car only, memo, cap. 0.3.3 (block text unchanged): no scan for an Unsafe conflicting lane. |
| S2-25 | iterator | 1 | 9 | `CheckOverlappingLanes`, right after S2-04 | 0.4.1 `BeginZebras` (only with the setting "Cars never stop on a zebra"): a node lane with zebras (pedestrian overlap, `PedestrianLaneFlags.Crosswalk` without `Unsafe`) gets the zebra sections as its waiting spots (Core/ZebraSections.cs, merged where the whole vehicle does not fit before a zebra); the car is held (`GetMaxBrakingSpeed` to the next zebra section's spot, blocker `Entity.Null`, type Continuing, so no IgnoreBlocker creep) while there is no room for the whole vehicle past that section (Core `RoomScan`). JR-T-009-r: the call also passes the iterator's own `ref m_LaneOverlapData` (read-only) for further zebras on later path lanes. |
| S2-26 | nav | 1 | 12 | `UpdateNavigationTarget`, right after S2-21 | 0.4.1 (setting on only): `m_JR.SetPath(navigationLanes)` for the room-past scan; the whole vehicle length with trailers also with the stock gap rule. 0.4.2: also with "Cars obey yellow boxes", which first hands the five read-only box lookups to the iterator (`SetBoxes`). |
| S2-28 / S2-28b | iterator | 1 / 1 | 10 / 10 | top of both `CheckCurrentLane` overloads (every car lane the iterator walks, current and ahead, before `CheckOverlappingLanes`) | 0.4.2 `BoxHold` (only with the setting "Cars obey yellow boxes"; Core/BoxDriving.cs, Stage2/JRYellowBox.cs): a node lane of a boxed node (`YellowBoxJunction` on the lane's `Owner`) is entered at its start (the stop line) only with room for the whole vehicle plus the margin past the lane end on the car's own path; an edge lane (`EdgeLane`) of a road with boxed accesses (`ConnectedBuilding` buffer, `YellowBoxEntrance` + `Building.m_RoadEdge`) has a band of the painted length centred on the access (`Building.m_CurvePosition` through `EdgeLane.m_EdgeDelta`): a through car is held before the band, a car joining inside it (leaving the driveway) at its join point, while there is no room past the band; a car whose path ends inside the band (into the driveway) is exempt. Room = the 0.4.1 scan (shared `HasRoomPast`), vehicles ahead inside the box / band count as queued. Hold = `GetMaxBrakingSpeed` to the entry, blocker `Entity.Null`, type Continuing (as S2-25). Cars already in the box, or that cannot stop (the game's can-stop test), carry on. |
| S2-27 | iterator | 1 | 1 | right after S2-08b | 0.4.1 counters only: `AfterPedestrian` (zebra entry held for people). |
| S2-13 | iterator | 1 | 1 | `CheckOverlappingLanes`, right after `num6 -= num9` (before S2-12) | 0.2.4 `TurnAcrossPriority` (deviation from stock priority, owner-approved 14 Nov 2026; setting on and whole junction only): turn across the oncoming flow `+1`, the oncoming straight `-1` when sign status is equal and no sign / signal override applies; marks the straight car's overlap so `Yield` (S2-06) does not hold it for the turner's reservation alone (unless that reservation's priority beats `num6`). |
| S2-20 | nav | 1 | 36 | `UpdateNavigationJob` fields | `m_JRMode` (int, by value), `m_JRCounters` (persistent `NativeArray<long>`, atomic adds, safety restriction off: debug-only counters); 0.2.3: `m_JRSafetyEvents` (persistent `NativeArray<long>` event table, compare-exchange, debug only) and `[ReadOnly] m_JRConnectedEdges` (`BufferLookup<ConnectedEdge>`). 0.3.3: `[ReadOnly]` `CarTractorData` / `CarTrailerData` / `LayoutElement` lookups again (vehicle length for the crossing fit). |
| S2-21 | nav | 1 | 6 | after the iterator is set up in `UpdateNavigationTarget` | `m_JR = JRWholeJunction.Create(m_JRMode, m_NodeLaneData, m_OwnerData, m_LaneData, m_Lanes, m_JRConnectedEdges, m_JRCounters)` (0.2.3: read-only lookups of the job for the feeding-lane scan); 0.3.3: plus the whole vehicle length, a per-car local (controller bounds; with the rule on `JRVehicleLength.WithTrailers` from the car's layout buffer). |
| S2-22 | nav | 1 | 4 | after `CheckBlocker` at `IL_09cd` | Counters, once per car-step, only when the count bit is set; 0.2.3: safety-stop events through the event table (entity index, simulation frame). |
| S2-23 | nav | 1 | 12 | `OnUpdate`, before `ScheduleParallel` | Main thread: takes this step's mode from `Stage2Handoff` (published by `CarDrivingSwitchSystem` earlier in the same step) into the job struct by value; 0.2.3: sets the event table and `GetBufferLookup<ConnectedEdge>(isReadOnly: true)`; 0.3.3: the three read-only lookups for the vehicle length. |
| S2-24 | nav | 1 | 8 | new `OnDestroy` (none in vanilla) | Completes the job and frees the counter array. |

0.3.6-probe (JR-T-008) changes nothing in the copy: the people / cyclist pass is its own system (`Systems/PeoplePassSystem.cs`, no copied game code; it only sets `HumanCurrentLane.m_CurvePosition.x` and the game's own `CarLaneFlags.IgnoreBlocker` on bicycles). `docs/vanilla_copy_check_0.3.6.txt` equals 0.3.5's.

0.4.1-probe (JR-T-009, cars never stop on a zebra) adds S2-25, S2-26, S2-27 and changes one line of S2-05: `jrInScope` is also computed when `m_JR.m_ZebraActive` (a zebra lane with the stock gap rule; `InScope` then takes only the zebras, and only while their section's spot is ahead). With the setting off S2-25 / S2-26 are one bool test each, `m_ZebraActive` stays false and S2-05 / S2-27 behave as in 0.4.0. The setting travels in the stage 2 int (`Stage2Mode.ZebraClear` + margin in bits 8-15) and `JRWholeJunction.Create` strips it (`Stage2Mode.Stripped`), so every 0.2.x `m_Mode == 0` exit is unchanged. Stock hooks used: crosswalks are node pedestrian lanes (`Game.Net/LaneSystem.cs` `CreateNodePedestrianLanes`, `isCrosswalk: true`, flags at 8571-8581: `Crosswalk`, plus `Unsafe` where the zebra was removed), also at mid-block crossing nodes; the overlap with them comes from `LaneOverlapSystem.CheckOverlaps`; room past mirrors the copy's `CheckSpace` (braking-distance credit of a moving car, +1 m per queued car).

0.4.1-probe review fixes (JR-T-009-r) change one line of S2-25 only (the `BeginZebras` call gains `ref m_LaneOverlapData`; block and line counts unchanged); everything else is in `Stage2/JRWholeJunction.cs` and `Core/ZebraSections.cs`, run only with the setting on. (1) A zebra section is decided at the last point where the car can still stop before it: when the game's braking distance at the car's speed (`GetBrakingDistance(car, speed, timeStep)`, the `canStop` value) is longer than the distance from the car's next spot to the following zebra spot, that zebra section is merged into the next spot's section for this car-step (`ZebraSections.MergeUnstoppable`, chaining): people on it, its Cs and the room past it hold the car at the earlier spot, as the 'does not fit' merge. The room check now also runs from before W for the first zebra section after it (`ZebraSections.RoomSection`, CCCZ: hold at Z while the car can stop there), so a car never passes W too fast to stop for the zebra on a full exit. (2) The room scan along the navigation lanes treats a zebra on a later node lane (another node, e.g. a mid-block crossing after an edge shorter than the vehicle) that starts inside the room window as part of the decision: people on it (any object on the crosswalk lane) = no room, else room is needed past its end too (`RoomScan.FurtherZebra` / `ExtendPast`, vehicles already read are re-judged). Known limit: people still on the pavement about to step onto that further zebra are not seen (the game's own check holds the car before it, its tail may then be on the first zebra). (3) The scan flags a LaneObject buffer read out of curve-position order (counter, probe only).

0.4.2-probe (JR-T-010, cars obey yellow boxes) adds S2-28 / S2-28b and extends S2-20 (five `[ReadOnly]` lookups: `YellowBoxJunction`, `YellowBoxEntrance`, `Game.Buildings.Building`, `ConnectedBuilding`, `EdgeLane`), S2-23 (their `GetComponentLookup` / `GetBufferLookup(isReadOnly: true)`) and S2-26 (`SetBoxes`; `SetPath` and the trailer length also with the box setting). No S2-04 / S2-25 line changes: the box's stop-line decision and the turner's spot in the box are made inside `BeginLane` / `BeginZebras` (job side only). With the setting off S2-28 / S2-28b are one bool test each, `m_BoxRule` is false, `BeginLane` / `BeginZebras` take exactly the 0.4.1 paths and the stage 2 int carries no box bit (`Stage2Mode.BoxBits` returns 0), so driving equals 0.4.1. The setting travels in the stage 2 int (`Stage2Mode.BoxObey` + `BoxTurnWait` / `BoxLeftHand` / `BoxJunctions` / `BoxEntrances`, entrance length in bits 21-29, the room margin shared in bits 8-15) and `JRWholeJunction.Create` strips it. Stock hooks used: `CheckCurrentLane` is called for every car lane the iterator walks (`IterateFirstLane` 211 / 357 / 368 / 372, `IterateNextLane` 540 of the copy) with the game's distance to the lane point; the game's own "space after the junction" test (`CheckSpace`, copy line 670, used at 470-500 only for the first node lane after an approach with turn / forward flags and only against the next lane's merge neighbours) stays unchanged and runs before the box test; the box only adds a stricter hold.

0.4.2 also (job side only, no block change): (1) owner refinement of the zebra sections (Core `ZebraSections.Build` with `gapMargin`, the room margin): with the Whole junction rule a zebra followed by a gap that holds the whole vehicle plus stop and room margin before the first vehicle point (Z O C C) gets a waiting spot at O; the zebra section then ends at the zebra (room past it = O free) and the Cs are decided at O. This brings back a mid-junction waiting spot ONLY right after a zebra: 0.2.3 (owner ruling: no waiting inside the junction, decide everything at W) stays everywhere else; Z C C (no gap) keeps the 0.4.1 layout; `MergeUnstoppable` folds O (and the box turn spot) into the earlier spot when the car cannot stop there any more; inside a yellow box no O spot (the box decides at the line). (2) `ScanLane` reads a lane's LaneObject buffer in curve order (Core `ScanOrder`) instead of storage order: the game sorts that buffer by target position (`Game.Net/NetUtils.cs` `AddLaneObject` / `UpdateLaneObject` 435-487 insert by `m_CurvePosition.y`; `Game.Simulation/CarNavigationHelpers.cs` `CurrentLaneCache` 185 / 198 / 228 write `CarCurrentLane.m_CurvePosition.xy` = current, target), which is not position order when a car's target lies short of the lane end (0.4.1 probe: "lane objects out of curve order" 2-4). The stock loops read the whole buffer and do not rely on order (`CarLaneSpeedIterator.CheckCurrentLane` 1059).

Total: 26 blocks, 152 lines inside (as `vanilla_copy.py check` reports; 0.4.1: 24 blocks, 108 lines; 0.3.3 to 0.4.0: 21 blocks, 89 lines; 0.3.2: 21 blocks, 72 lines; 0.2.4 to 0.3.1: 21 blocks, 66 lines; 0.2.3: 20 blocks, 65 lines; 0.2.2: 19 blocks, 60 lines; 0.2.1: 19 blocks, 43 lines; S2-24 counts its trailing blank line).

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
