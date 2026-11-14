# Stage 2 analysis: whole-junction gap decision (JR-T-002)

Game code: Game.dll 1.6.4f1. Citations are to the decompile in `~/Code/decomp/yb-2026-11-13/`
(`CarLaneSpeedIterator.cs` = I, `CarNavigationSystem.cs` = N, `full/Game.Net/...`). The copy in
`JunctionRules/Vanilla/` has the same code; its line numbers are about +10 (header) and more after the stage 2 blocks.

Owner rulings (14 Nov 2026, binding): stock only; stock boldness; one decision for the whole path; the generalised
waiting spot (wait just before the FIRST path crossing, never inside another lane's path; one decision from there for
all remaining conflicts; no more give-way stops inside; vanilla collision braking stays; STOP signs keep the stock stop
at the line, then may roll up; default = this rule, a "wait at the line" variant allowed but not default).

## 1. Where vanilla decides conflict by conflict

Each car is updated every 16 frames (N, UpdateNavigationJob, `UpdateFrame` filter). Its speed limit for the step comes
from `CarLaneSpeedIterator`:

- `IterateFirstLane` (I 118, overload with lane change I 242) checks the lane the car is on, then
  `IterateNextLane` (I 366) walks the navigation lanes ahead (N 2039) until
  `m_Distance + m_DistanceOffset - 20 >= brakingDistance` (I 239, 363, 620): it looks one braking distance plus 20 m
  ahead, so at normal speeds the whole junction lane (node lane) and often the next junction are seen.
- For every lane it calls `CheckOverlappingLanes` (I 1172; calls at I 192, 226, 333, 349, 513, 607). That loops over
  the lane's `LaneOverlap` buffer (built by `Game.Net.LaneOverlapSystem`, `full/Game.Net/LaneOverlapSystem.cs` ~1400:
  every pair of lanes of the same node whose paths overlap, with the overlap's start and end on both lanes in 1/255
  steps and `OverlapFlags`).
- **Per overlap, independently** (I 1191-1390):
  - distance to that overlap's start: `num5 = origDistance + length * (start - curveOffset.x)` (I 1233-1234);
  - overlaps already "committed" (start at or before `origMinOffset`, the end of the car's own lane reservation =
    its braking distance, N 2251 `ReserveNavigationLanes`) skip the give-way part (I 1241);
  - otherwise the give-way test: priority from the overlap's `PriorityDelta`, overridden by signs and signals through
    `yieldOverride` (I 1267-1281); a reservation on the other lane that reaches into the overlap or has higher priority
    makes the car brake to stop at `num5` (I 1288-1297); a car asking for space (reservation priority 96) the same
    (I 1309-1317); "Slow" overlaps and a positive priority delta only cap speed at half the limit (I 1299-1307);
  - then every object on the other lane: pedestrians (`CheckPedestrian`, I 1348) and vehicles. A vehicle of higher
    priority is projected 2 s ahead (`curvePosition.y += objectSpeed * 2 / length`, I 1372; this is the stock gap
    judgement: a gap is accepted when the approaching car will not reach the overlap within 2 s); a vehicle that is
    (or is projected to be) past the overlap's start on its lane brakes the car via `UpdateMaxSpeed` (I 1388) to stop
    at `num5` for a crossing (`distanceFactor = parallelism/128` is about 0 for crossings) or to follow it on a merge.
- The result is the minimum over all overlaps. **Each conflict only ever brakes the car to the start of that conflict.**
  Nothing ties the conflicts of one path together.

Signs and signals (I 382-460): a STOP lane (`CarLaneFlags.Stop`) gives `driveSpeed = 0` until the car is within 1.1 m
of the line (I 438-451), then `yieldOverride = 1` (yield to all). A red signal stops the car at the line (I 411-418);
a car that cannot stop any more gets `yieldOverride = -1` (others yield to it, it clears the junction). Signal Yield
(green with conflicting traffic, e.g. turning across oncoming) gives `yieldOverride = 1` (I 432-433). Before the line
the stock code also refuses to enter when there is no space after the junction (`CheckSpace`, I 401, 445, 453).

CarNavigationSystem: `ReserveNavigationLanes` (N 2251) reserves the car's own lanes up to its braking distance at
its intended speed; others see that reservation in their give-way test. `CheckBlocker` (N 1365) only records what
limited the car (blocker entity, type, speed) for the next step and for queueing; it decides nothing about gaps.

## 2. How a car ends up stopped inside a junction (stock)

1. The car's first conflicts are clear, it enters; a later conflict on the same path then gets a priority car within
   2 s (or a reservation) and the car stops at the start of that later conflict, inside the junction, in the path of
   the conflicts it has already entered. Typical: minor-arm car crossing a two-way major road (clear near side, stop
   in the middle for the far side); turner across oncoming on a green (clears oncoming lane 1, stops before lane 2).
2. The car was committed (could not stop) when a conflict became busy: it brakes for physical cars only; this is the
   collision braking that stays.
3. The exit is queued: the car follows a queue on the merge part of its path (stock `CheckSpace` at the line prevents
   most of this; stage 3 yellow-box topic).
4. Pedestrians stepping onto a crossing inside the path.

## 3. The change (implemented, default off: setting "Junction gap rule" = Stock)

For every junction lane (a lane with `NodeLane`; edge lanes are untouched):

- **Waiting spot W** = the start of the first conflict on the lane, i.e. the last point of the path that is not
  inside any other lane's path. Conflicts that count: overlaps the game gives its yield test (not a diverging sibling
  from the same start lane: vanilla skips those with `MergeStart|MergeMiddleStart`, End flags when inverted), not
  water, with a road or track lane. A road lane whose signal is red right now (`LaneSignal` Stop/SafeStop) is not a
  live path and does not place W (owner ruling for green turners); track lanes always do. Pedestrian crossings are
  judged in the decision but do not place W (the car keeps stopping for people at a zebra before W as stock does).
  Variant setting "At the stop line": W = start of the junction lane.
- **Committed** = the car cannot stop before W any more (the game's own "can brake" test,
  braking distance > distance to W + 0.9 m margin) or its reservation already reaches W (the game's own committed
  test, `start <= origMinOffset`). A car standing at W is not committed.
- **Before commit**: every give-way the stock test raises for a conflict at or after W (reservation, space request,
  projected priority car, a car physically in a crossing, a pedestrian on a crossing) stops the car at W instead of at
  that conflict. Same stock judgement on each conflict (same 2 s projection, priorities, signs, signals), applied to all
  conflicts at once: the car goes only when no conflict of the path raises a stop.
- **After commit**: no more give-way stops: reservation and space yields are dropped; the 2 s projection of approaching
  priority cars is undone unless that car is already past the overlap's start (exactly the game's own committed
  semantics, I 1354). Physical cars in the path and pedestrians are still braked for (collision braking).
- A physical car on the merge part (both lanes run into the same exit) before commit is followed as stock (that is a
  queue, not a gap; stage 3).

The distance to W uses the stock formula (`num5`), so W at the node edge gives exactly the stock stop at the line.
STOP signs: the stock stop at the line happens before the junction lane (I 438-451, untouched); after it the car is
not committed and may roll up to W.

Code: `JunctionRules/Core/WholePathRule.cs` (pure arithmetic, unit-tested), `JunctionRules/Stage2/JRWholeJunction.cs`
(job side), insert-only blocks S2-01..S2-24 in the copies (DEVIATIONS.md, section S2).

## 4. Per case

| Case | Applies? | What the code does |
|---|---|---|
| Plain crossroads, minor or uncontrolled arm | Yes | W = first crossing lane (often the node edge = the stop line for straight-on, or a few metres in for a turn). Waits there until every crossing and the merge give way at once; then no stop inside. |
| T-junction | Yes | Same. Turn from the minor arm across both major directions: one decision for both. Major-road turner across oncoming: see "priority turns". |
| STOP arm | Yes | Stock full stop at the line first (untouched), then roll-up to W if W is inside, then one decision. |
| YIELD arm | Yes | No stop at the line; stops at W only if a conflict is busy. |
| Priority (major-road) turn across oncoming | Yes | W = start of the first oncoming lane; waits there (inside the junction, as stock already does for the first lane) and crosses all oncoming lanes in one go. |
| Signalised, turn across oncoming on green | Yes (owner ruling) | Red side-road lanes do not place W, so W = before the first oncoming lane; one decision for all oncoming lanes. If the signal changes while it waits there, the now-green side-road paths place W behind its front, it is committed and clears (stock `yieldOverride = -1` also tells others to wait for it). |
| Signalised, straight on green | Yes, mostly inert | Conflicting lanes are red, so W often has no live conflict and nothing changes; a car that entered late is committed and clears. |
| Roundabouts | Partly; FLAGGED | Each roundabout node is its own junction lane, so the rule decides per node lane (entry: one decision for the ring lanes crossing the entry lane at that node). Stock already clamps all priority overlaps of a roundabout lane to the first one (I 1229-1232), so on the entry lane stock and the rule mostly agree. The ring is several node lanes in a row: a car is committed per node and decides again at the next node's W (that is the next node's "first crossing", not a stop inside one node's paths). Not one decision for the whole ring: that would need a multi-node look-ahead, not stock. |
| Merges (two lanes into one exit) | Yes for priority, FLAGGED for queues | A merge overlap is a conflict: a projected priority car on the other lane stops the car at W. A car physically ahead on the merge is followed as stock (following distance), before and after commit: a queue on the exit can still hold a committed car inside (stage 3 yellow box; stock `CheckSpace` at the line already prevents most). |
| Multi-lane overlaps (path crosses several lanes of one approach, or overlaps that start at the same point) | Yes | All conflicts at or after W are in one decision; equal starts (within 1e-4 of the offset) count as the first conflict. Overlapping overlaps cannot create a W inside a path, because W is the minimum start of all live ones. FLAGGED: a red-held lane is excluded from W; if its path physically still holds a car (clearing on amber), the car at W can stand inside that lane's path; it still brakes for that car (collision braking), and stock `yieldOverride = -1` gives the clearing car priority. |
| Driveway / parking exit onto a road | Partly | Parking lanes (edge lanes, never in a lane's overlap list: `LaneOverlapSystem` skips them) and any exit that is not a `NodeLane` stay stock. But a sub-network exit built as a side connection (service road or parking-lot road ending on a road edge) IS a `NodeLane` (`full/Game.Net/LaneSystem.cs` `CreateNodeConnectionLane` ~3339, flags `Unsafe`, `SideConnection`, and `Yield` for the source side, ~3460), so the rule is active there: such a car waits before the first lane it crosses and then makes one decision. Where a driveway is a real node it is a T-junction and the rule applies. (Corrected in review JR-T-002r: the first version said side connections are edge lanes. Building spawn/connection lanes were not checked.) |
| Level crossings / tram tracks in a junction | Yes | Track overlaps always place W; trams and trains get the same stock judgement. |
| Bicycles | Yes | Same code path with the stock bicycle margin (0.4 m). |
| Emergency vehicles, priority >= 108 | Stock judgement | Their priorities make most conflicts yield to them anyway; the rule changes only where they would yield. |

Unsafe or impossible with stock data (flagged): (a) the ring of a roundabout as a whole (above); (b) exit queues
(merge following stays, can stop a committed car inside); (c) a signal turning red for the paths the car waits in front
of while it stands at W inside the junction (it commits and clears: stock clearing behaviour); (d) a later conflict
that becomes busy after commit by a car that was not visible at decision time (not within 2 s): no give-way, only
collision braking, and the other car sees the committed car's reservation (stock `offset > num10` test) and yields to it;
(e) consecutive junction lanes of different nodes very close together: one decision per node.

## 5. Deadlock and starvation

- **Deadlock**: a car waiting at W has speed 0 and is before every conflict of its lane; others never project it (speed
  0), never see it past an overlap start, and its reservation does not reach past W. So a waiting car cannot be the
  reason another car waits, except through the stock reservation priority test, which is antisymmetric (priority
  deltas of a pair of lanes have opposite signs) and unchanged. The only new wait (at W for a LATER conflict) is at a
  point where stock itself already stops cars for the first conflict. No circular wait is created by the rule.
- **Starvation**: needing all conflicts clear at once is stricter than stock (stock could cross a two-way major road
  in two halves). A busy major road can therefore hold a minor arm longer than stock does. No safeguard is added: any
  timeout or forced gap would be new behaviour not in the game (owner ruling "stock only"). Mitigation: the counters
  show waits at W for later conflicts; the setting switches back to stock at once.

## 6. Performance

With the rule off and the timing log off the stage 2 code in the job is: one `m_JR.m_Mode != 0` test per
CheckOverlappingLanes call and per overlap, a field store per Iterate call and a few local copies per object. The
counters run only with the timing log on (atomic adds once per car-step). The timing A/B is in PROBE-STAGE2.md.

## Owner choices

1. **Roundabouts**: per node lane (implemented) or one decision for the whole ring (needs a new multi-node look-ahead;
   not stock). Default: per node lane.
2. **Exit queues / merges**: keep following a queue on the merge (implemented, stock) until stage 3 yellow boxes, or
   also hold the car at W while the exit merge is occupied (not stock: new "space" behaviour).
3. **Starvation**: accept the stricter joint gap (implemented, no safeguard), or add a stock-free fallback such as
   "after N seconds at W use the stock rule for this car" (would be new behaviour; needs a ruling).
4. **Waiting spot**: before the first crossing (default) or at the stop line: setting "Whole junction: waiting spot"
   (implemented as the allowed variant).
