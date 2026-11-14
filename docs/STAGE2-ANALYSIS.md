# Stage 2 analysis: whole-junction gap decision (JR-T-002)

Game code: Game.dll 1.6.4f1. Citations are to the decompile in `~/Code/decomp/yb-2026-11-13/`
(`CarLaneSpeedIterator.cs` = I, `CarNavigationSystem.cs` = N, `full/Game.Net/...`). The copy in
`JunctionRules/Vanilla/` has the same code; its line numbers are about +10 (header) and more after the stage 2 blocks.

Owner rulings (14 Nov 2026, binding): stock only; stock boldness; one decision for the whole path; the generalised
waiting spot (wait just before the FIRST path crossing, never inside another lane's path; one decision from there for
all remaining conflicts; no more give-way stops inside; vanilla collision braking stays; STOP signs keep the stock stop
at the line, then may roll up; default = this rule, a "wait at the line" variant allowed but not default).
Further owner rulings of 14 Nov 2026 (JR-T-002b): amber = live; one decision per roundabout node; the decision is taken
only at the waiting spot (first in line), exit queues stay stock; no starvation safeguard ever. See "Owner choices".

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
   some of this). Owner ruling 14 Nov: this stays stock car-following in stage 2; yellow boxes (stage 3, only where
   the owner paints one) are the answer, not the gap rule.
4. Pedestrians stepping onto a crossing inside the path.

## 3. The change (implemented, default off: setting "Junction gap rule" = Stock)

> 0.2.1 note (JR-T-002cr): this section describes the 0.2.0 rule (one decision at W, no give-way at all after commit). In
> 0.2.1 the "After commit" bullet no longer holds as written: the car keeps re-checking the uncrossed points of its stretch
> (safety stop) and mid-junction spots add more decisions. Sections 7 and 8 are binding where they differ; sections 4 and 5
> that speak of "committed" cars read as "past the entry spot".

For every junction lane (a lane with `NodeLane`; edge lanes are untouched):

- **Waiting spot W** = the start of the first conflict on the lane, i.e. the last point of the path that is not
  inside any other lane's path. Conflicts that count: overlaps the game gives its yield test (not a diverging sibling
  from the same start lane: vanilla skips those with `MergeStart|MergeMiddleStart`, End flags when inverted), not
  water, with a road or track lane. A road lane whose signal is **red** right now (`LaneSignal.m_Signal == Stop`) is not a
  live path and does not place W (owner ruling for green turners). A lane on **amber** (`SafeStop`) is live: it places W
  and is judged in the decision like any other lane (owner ruling 14 Nov: only red does not count; the stock yield test
  itself never treated amber specially, it only adds +1 for red and -1 for a yield signal, I 1273-1276). Track lanes always place W.
  Pedestrian crossings are judged in the decision but do not place W (the car keeps stopping for people at a zebra
  before W as stock does). Variant setting "At the stop line": W = start of the junction lane.
- **Committed** = the car's own front is past W (by the game's stop margin, 0.9 m, 0.4 m for bicycles): it has left
  the spot and is inside the junction's paths. Nothing else commits a car (owner ruling 14 Nov, "decide only at W"):
  not the braking-distance test, not the car's lane reservation, not a look-ahead lane, not a leader's go. A car
  standing at W or anywhere behind it is not committed, whatever its speed.
  (Background, reviewer advisory b: the first version also used the game's "can brake" test and the game's reservation
  test `start <= origMinOffset`. For look-ahead lanes the navigation job passes `origMinOffset = -1`, or `2` when the
  lane is driven inverted (N 2036-2037; the car's own current/change lane gets its curve position instead), and
  IterateNextLane replaces it only by a start offset > 0 (I 511), so that
  reservation clause could only ever fire on the car's current lane; look-ahead lanes committed only through the
  braking-distance test, as in stock. Both clauses are gone now: on a look-ahead lane the wait distance is always
  positive, so a car is never committed to a junction it has not reached.)
  Residual stock behaviour (JR-T-002br, outside every block, unchanged): the game's own per-overlap skip
  `start <= origMinOffset` (I 1241) still applies on the car's CURRENT lane: an overlap that the car's own lane
  reservation already reaches gets no give-way test at all. The reservation is the braking distance at the car's
  planned speed, and that speed is clamped to the speed range around the current speed (I 161, 212, N 1503), so this
  only happens to a car that physically cannot stop before that conflict, as in stock. Such a car is not "committed"
  for the rule (front not past W), but the game skips the give-way for the conflicts its reservation covers; conflicts
  beyond the reservation still stop it at W (hard braking, it overruns W and is then committed). A queued or rolling-up
  follower is slow, so its reservation ends before W (a car braking for W reserves up to the stop point) and the
  test is applied in full. A car that comes up in a leader's wake too fast to stop is the only way past W without a
  decision at W; stage 1 stock does the same.
- **Before commit**: every give-way the stock test raises for a conflict at or after W (reservation, space request,
  projected priority car, a car physically in a crossing, a pedestrian on a crossing) stops the car at W instead of at
  that conflict. Same stock judgement on each conflict (same 2 s projection, priorities, signs, signals), applied to all
  conflicts at once: the car goes only when no conflict of the path raises a stop. This is re-evaluated every update
  until the front passes W, so the decision that lets a car through is the one judged when its front reaches W.
  Pedestrians: a person on a crossing that the path meets at or after W (inside the junction) also holds the car at W
  (reviewer advisory c); people on a crossing before W are braked for at the crossing, as stock.
- **After commit**: no more give-way stops: reservation and space yields are dropped; the 2 s projection of approaching
  priority cars is undone unless that car is already past the overlap's start (exactly the game's own committed
  semantics, I 1354). Physical cars in the path and pedestrians are still braked for (collision braking).
- **First in line decides (owner ruling 14 Nov)**: the whole-junction decision is taken only by the car at W. A follower
  B queued behind a leader A (on the approach or inside the junction behind A) just follows A with the stock
  car-following (`CheckCurrentLane`, untouched): it makes no decision of its own yet and it cannot commit, because its
  front is behind W. It also cannot ride on A's gap: while B is behind W, a conflict that is busy (stock judgement, as
  seen from B) stops B at W, not at that conflict and not after A. When A has gone, B rolls up to W and decides there for
  all its conflicts (gap clear: it goes and is committed once past W; gap busy: it waits at W). B is not required to come
  to a standstill at W when its gap is already clear when it reaches W (that would be a stop sign at every junction,
  not stock).
- **Exit queues**: stock car-following, before and after commit. A physical car on the merge part (both lanes run into
  the same exit) is followed as stock; a committed car may still come to rest behind a queue as in stock. There is no
  room check past the last crossing (that is yellow-box behaviour, stage 3, only where the owner paints a box).

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
| Priority (major-road) turn across oncoming | Yes | W = start of the first oncoming lane; waits there (inside the junction, as stock already does for the first lane) and crosses all oncoming lanes in one go. A second turner queued behind follows to W and decides there itself (never through on the first turner's gap). |
| Signalised, turn across oncoming on green | Yes (owner ruling) | Red side-road lanes do not place W, so W = before the first oncoming lane; one decision for all oncoming lanes. Amber side-road lanes are live: while a side road is on amber its lanes place W, so the turner waits before them. If the signal changes while the car waits (reviewer advisory a): the lanes that become live place a new W. Depending on the geometry it is behind the car's front (then the car is inside a now-live path, is committed and clears; stock `yieldOverride = -1` also tells others to wait for it) or ahead of it (then the car is not committed, rolls up to the new W and decides again there). |
| Signalised, straight on green | Yes, mostly inert | Conflicting lanes are red, so W often has no live conflict and nothing changes; a car that is past W when they turn live is committed and clears. |
| Roundabouts | Yes, per node (RULED 14 Nov) | Each roundabout node is its own junction lane, so the rule decides per node lane (entry: one decision for the ring lanes crossing or joining the entry lane at that node). Stock already clamps all priority overlaps of a roundabout lane to the first one (I 1229-1232; untouched, no stage 2 block touches the roundabout code). The ring is several node lanes in a row: a car decides again at the next node's W. Owner ruling: keep one decision per roundabout node, as built. |
| Merges (two lanes into one exit) | Yes for priority; queues stock (RULED 14 Nov) | A merge overlap is a conflict: a projected priority car on the other lane stops the car at W. A car physically ahead on the merge is followed as stock (following distance), before and after commit: a queue on the exit can still stop a committed car inside, as in stock. Owner ruling: exit queues stay stock; yellow boxes (stage 3) handle them where painted. |
| Multi-lane overlaps (path crosses several lanes of one approach, or overlaps that start at the same point) | Yes | All conflicts at or after W are in one decision; equal starts (within 1e-4 of the offset) count as the first conflict. W is the minimum start of all live ones, so it is never inside a live path. FLAGGED: a red-held lane is excluded from W; if its path physically still holds a car (one that ran the end of amber and clears on red), the car at W can stand inside that lane's path; it still brakes for that car (collision braking), and stock `yieldOverride = -1` gives the clearing car priority. Amber lanes are live, so this is limited to the red phase. |
| Driveway / parking exit onto a road | Partly | Parking lanes (edge lanes, never in a lane's overlap list: `LaneOverlapSystem` skips them) and any exit that is not a `NodeLane` stay stock. But a sub-network exit built as a side connection (service road or parking-lot road ending on a road edge) IS a `NodeLane` (`full/Game.Net/LaneSystem.cs` `CreateNodeConnectionLane` ~3339, flags `Unsafe`, `SideConnection`, and `Yield` for the source side, ~3460), so the rule is active there: such a car waits before the first lane it crosses and then makes one decision. Where a driveway is a real node it is a T-junction and the rule applies. (Corrected in review JR-T-002r: the first version said side connections are edge lanes. Building spawn/connection lanes were not checked.) |
| Level crossings / tram tracks in a junction | Yes | Track overlaps always place W; trams and trains get the same stock judgement. |
| Bicycles | Yes | Same code path with the stock bicycle margin (0.4 m). |
| Emergency vehicles, priority >= 108 | Stock judgement | Their priorities make most conflicts yield to them anyway; the rule changes only where they would yield. |

Unsafe or impossible with stock data (flagged): (a) the ring of a roundabout as a whole (ruled: per node); (b) exit
queues (ruled: stock following, stage 3 yellow boxes); (c) a signal turning red for the paths the car waits in front of
while it stands at W inside the junction (it is not committed; the red lanes stop placing W, so W moves on to the next
live conflict or the lane has no W, and the car follows the new W or the stock rule); (d) a later conflict that becomes
busy after commit by a car that was not visible at decision time (not within 2 s): no give-way, only collision braking,
and the other car sees the committed car's reservation (stock `offset > num10` test) and yields to it; (e) consecutive
junction lanes of different nodes very close together: one decision per node; (f) a fast car that is surprised by a
conflict just before W (no braking-distance commit any more): it brakes as hard as the game allows, like a stock car
yielding to a conflict it can barely stop for; if it overruns W its front is past W, it is committed and clears instead
of stopping inside.

## 5. Deadlock and starvation

- **Deadlock**: a car waiting at W has speed 0 and is before every live conflict of its lane; others never project it
  (speed 0), never see it past an overlap start, and its reservation does not reach past W once it stands. So a waiting
  car cannot be the reason another car waits, except through the stock reservation priority test, which is
  antisymmetric (priority deltas of a pair of lanes have opposite signs) and unchanged. The only new waits are at W, a
  point where stock itself already stops cars for the first conflict. Commitment only past W (ruling 3) adds no new
  wait inside: a committed car is inside and only gives up give-ways. One short new hesitation is possible: a moving
  car whose reservation already covers a conflict can now still be stopped at W while the other car yields to that
  reservation; once it stands its reservation shrinks to W and the other car goes in the next update. A follower waits
  only for its leader (stock following) and then for its own gap at W; it never holds anyone in a conflict area. No
  circular wait is created by the rule.
- **Starvation (RULED 14 Nov: no safeguard, ever)**: needing all conflicts clear at once is stricter than stock (stock
  could cross a two-way major road in two halves), so a busy major road can hold a minor arm longer than stock does.
  Owner ruling: any wait that the rule produces is the rule; a long wait is a road design or city problem, not a mod
  problem. There is no timeout, no forced gap and no fallback to the stock rule. The counters (debug/timing log) keep
  long waits visible: "waited at spot for a later conflict" and the per-junction behaviour notes in PROBE-STAGE2.md.

## 6. Performance

With the rule off and the timing log off the stage 2 code in the job is: one `m_JR.m_Mode != 0` test per
CheckOverlappingLanes call and per overlap, a field store per Iterate call and a few local copies per object. With the
rule on, BeginLane is one pass over the lane's overlap list (already in cache for the stock loop) and one signal lookup
per road overlap; it no longer computes a braking distance. The counters run only with the timing log on (atomic adds
once per car-step). The timing A/B is in PROBE-STAGE2.md.

## Owner choices

All RULED by the owner on 14 Nov 2026 (binding; version stays 0.2.0):

1. **Amber** — RULED: lanes on amber (`SafeStop`) are live conflicts, both when placing W and in the decision; only red
   (`Stop`) does not count. Implemented (`WholePathRule.IsHeldByRed`).
2. **Roundabouts** — RULED: one decision per roundabout node lane, as built. No ring look-ahead.
3. **Who decides / exit queues** — RULED: the whole-junction decision is taken only by the car standing at its waiting
   spot W (first in line). A follower queued behind never counts as committed and never inherits the leader's decision:
   it follows with stock car-following, must not cross on the leader's gap, rolls up to W when the leader has gone and
   then decides for itself. Implemented by making "committed" = front past W only (no braking-distance, reservation,
   look-ahead or leader commit). Exit queues beyond the junction: stock car-following, untouched (a committed car may
   still stop behind a queue as in stock); no room check in stage 2. Yellow boxes stay stage 3, off by default, only
   where the owner paints one.
4. **Starvation** — RULED: no safeguard, no timeout, ever. A long wait is a road design or city problem. Counters keep
   long waits visible.
5. **Waiting spot** — RULED: default stays "before the first crossing"; "at the stop line" remains a non-default
   variant setting ("Whole junction: waiting spot").

Further rulings on the 0.2.1 open points, RULED by the owner on 14 Nov 2026 (binding):

6. **Physical car in a later conflict point** — KEEP: a car that is physically in a later point of the stretch makes
   the crossing car brake for it at THAT point (stock collision braking), never a safety stop further back (deadlock
   reason, section 8). As built in 0.2.1 (`StretchState.ForVehicle`: physical -> Stock).
7. **Followers queued behind a car at a mid spot** (SUPERSEDED 0.2.3: no mid spots) — ALLOWED: a follower may stand with its body in the near
   carriageway while it queues behind a car waiting on the central reservation (stock car-following, no room check;
   like an exit queue). No code change; no longer an open risk.
8. **Trailers** (SUPERSEDED 0.2.3: no mid spots, code unused) — 0.2.2: the vehicle length a mid spot must fit includes every trailer (section 9).
9. **"At the stop line" setting** — KEEP: the variant moves only the entry spot to the stop line; mid-junction spots
   stay in use with it (as built in 0.2.1). 0.2.3: there are no mid spots; the variant still moves W to the line.

## 7. 0.2.1 collisions (JR-T-002c)

Live probe, owner, 14 Nov 2026 (Bevy Valley, 0.2.0 Burst, whole junction on): "stopped inside for a give-way" 0 in
every window (stock 43-164), job time equal to stock, but committed cars occasionally drove into another car inside
the junction, and once at Valley x Carlisle (1092.7, -81.5) a westbound Carlisle car waited for an eastbound
right-turner into Valley. Citations: I = decompile `CarLaneSpeedIterator.cs`, N = decompile `CarNavigationSystem.cs`,
H = `full/Game.Simulation/CarNavigationHelpers.cs`, R = `full/Game.Simulation/NetLaneReservationSystem.cs`.

### How stock keeps two crossing cars apart (the part 0.2.0 broke)

The stock gap test is not a one-off judgement; it is a handshake that runs every update (each car is updated once per
16 frames, N 2982) until the car can no longer stop:

1. A car C reserves its own path ahead up to its braking distance at its planned speed (N 2251-2310). On its CURRENT
   lane that reservation is only its curve position `y` (N 2263, not a LaneReservation entry), which other cars read as
   `LaneObject.m_CurvePosition.y` (H 185/198: lane objects carry `(x, y)` = position and reserved end). Lanes ahead are
   reserved as LaneReservation entries (N 2305), rolled over every 16 frames (R 29-34).
2. Every update, for each overlap of its path, C applies the give-way test (reservation `offset > num10 || priority2 >
   num6`, I 1286-1288; the 2 s look-ahead of a higher-priority car on the other lane, I 1354-1372) **until its own
   reservation covers that overlap**: on the current lane the overlap is then skipped (`start <= origMinOffset`,
   I 1241; and the look-ahead only counts cars already past the start, I 1354). On look-ahead lanes `origMinOffset` is
   -1 (N 2036-2037), so the test always runs there.
3. The moment C's reservation (`y`) passes the overlap start, every other car brakes for C as a car "in" the overlap
   (I 1382-1388), whatever the priorities. So a stock car claims an overlap only when it can no longer stop, and only
   after the 2 s / reservation test said "clear" at every update until that moment.

The 2 s look-ahead and the reservation priority are therefore safe only because they are re-run continuously up to
the car's point of no return for EACH overlap. Cars on the approach of the other road are invisible except through
their reservation of the junction lane (their braking distance), so a car that is not yet within its braking distance
of the junction is not seen at all at decision time.

### Root cause 1 (main): later conflict points lost their protection after the go (0.2.0 design)

0.2.0 judged all points at W once, then, with the front past W, dropped every give-way of the path (S2-06/S2-07
`Suppress`, S2-10 look-ahead undone). For the FIRST point that is harmless (the front is in it, the reservation covers
it, stock skips it too). For every LATER point the car had not claimed it yet (its reservation reaches it only when
within braking distance), but it no longer checked it. Timeline for a car crossing a two-way road from standstill
(far lane 12 m beyond W, car 5 m/s^2, priority car at 50 km/h): decision at W sees the far lane clear (priority car
more than its braking distance, ~16 m, from the junction lane: no reservation, not on the junction lane: no
look-ahead); the car needs ~2.2 s to reach the far lane; the priority car reaches it in ~1.5-2.5 s. Stock would have
seen the priority car's reservation of its junction lane ~1 s after the go and stopped before the far lane (the stock
"stopped inside"). 0.2.0 ignored it; each car braked for the other only when the other's reservation `y` reached the
overlap (I 1382-1388), i.e. when neither could stop: the visible collision. Same for: a car that was stopped inside by
collision braking (behind a crossing car) and then restarted into the next car of a platoon (2 s look-ahead and the
reservation both dropped); and two cars that went in the same update group or within one 16-frame cycle on a shared
gap (oncoming stream clears, both turners go), each committed past its W with the other's later point unchecked.
**Fix: the safety re-check of section 8 (owner ruling B1/stretches): every uncrossed point is re-checked with the stock
test until the front reaches it.** This restores exactly the stock handshake for the later points, with the stop moved
to the first uncrossed point.

### Root cause 2 (decision): stop at an unreachable W, then drop the give-way (0.2.0, since 604c210)

Since 604c210 a car is committed only when its front is past W (no braking-distance commit). Before commit, a busy
later point stops the car at W (`StopAtSpot`, distance to W). A fast car that is surprised just before W (a point
becomes busy, typically a claim by another car's reservation, I 1288 `offset > num10`) cannot stop at W: it brakes at
the full rate (speed range clamp, VehicleUtils.CalculateSpeedRange), overruns W, is then committed and drops the
give-way for that later point entirely, although stock would have braked for the point itself (further away, often in
time). The decision was being applied to a spot the car could no longer reach. **Fix (separate from the safety net,
`StretchState.Classify`, phase `Stock`):** when the car cannot stop at the spot by the game's own can-stop test
(`GetBrakingDistance(car, currentSpeed, timeStep) <= distance`, I 1236), the busy point keeps the game's own give-way
(stop at that point, stock distance) for that update instead of `StopAtSpot`. A car that can stop at its spot is
unchanged (it decides at the spot). This is not a commit: no give-way is dropped. Tests: StretchRootCauseTests.

### Candidates checked and ruled out

- **Look-ahead too short at decision time**: no. A car standing at W has braking distance 0, and the iterator walks on
  while `m_Distance + offset - 20 < brakingDistance` (I 239/363/620), so the junction lane is always reached;
  CheckOverlappingLanes then loops over ALL overlaps of that lane regardless of distance (I 1191 ff.). The only early
  stop is `m_MaxSpeed == m_SpeedRange.min` (already braking at the full rate for something earlier, e.g. a stopped
  leader): such a car is not at W and follows stock.
- **Wrong distance to far points**: no. The distance uses the stock `num5` formula; what is wrong in 0.2.0 is the time
  at which the test runs (root cause 1), not the distance.
- **Opposing right-turners each seeing the other's reservation**: no deadlock and no false wait while both stand at W:
  a car never reserves its own current lane (N 2263), its `y` equals its position when standing, and equal-priority
  cars are not projected (I 1370). They do share gaps (both go when the oncoming stream clears), which is the same-gap
  race of root cause 1.
- **The 604c210 IsCommitted change**: only through root cause 2 (overrun of an unreachable W). The ruling itself
  (commit only past W, followers decide at W) stays.

### Valley x Carlisle: westbound waited for an eastbound right-turner

Carlisle E/W lanes are the priority lanes there (STOP only on the SE-bound Valley approach, node 1125927). By lane
priority the westbound car never yields to the opposing right-turner. It does yield, in stock as well, to a CLAIM: a
right-turner whose reservation (braking distance at its speed) already reaches the westbound overlap makes every car
on that lane yield (I 1288 `offset > num10`, independent of priority), and a turner whose `y` is past the overlap
start is braked for as a car in the path (I 1382-1388). Two ways this shows:
1. The turner had gone (judged at its W, westbound car not yet visible: not on the junction lane, its reservation not
   yet reaching it) and was in, or had claimed, the westbound overlap: the westbound car must stop. Stock does the same.
2. The turner was still approaching at speed and its reservation already covered the overlap (on its approach the
   junction lane is a look-ahead lane, so no stock skip applies), while it was itself being held at its W by the
   westbound car's priority reservation: both hold each other for one rollover (<= 16 frames, R 29-34) until the
   turner stands and its claim lapses. Stock has the same exchange; in 0.2.0 the westbound car stopped at its own W
   (possibly earlier than the overlap). If the turner could not stop at its W, 0.2.0 then let it overrun and drop the
   give-way (root cause 2): that is the case to rule out in the probe.
It is stock priority-by-claim, not a wrongly placed conflict and not the rule itself; 0.2.1 removes the overrun part
(root cause 2) and re-checks the westbound point if the turner overruns (safety net). What to watch: PROBE-STAGE2.md.

## 8. 0.2.1 stretches and the safety re-check (owner ruling, 14 Nov 2026, binding)

> **0.2.3: mid spots SUPERSEDED** (owner clarification 14 Nov 2026, section 10). The whole path is one stretch: the
> only waiting spot is W; the safety re-check (point 3) stays. Text below is the 0.2.1 record.

Owner wording (replaces B1): one general pattern, applied from the start.
1. A car's path through the node (its node lane) has ordered conflict points: the overlaps that place W in 0.2.0
   (road or track, yield-checked, not water, road lanes held by red excluded, amber live). A **waiting spot** is any
   position right before a conflict point where the car is not inside any other lane's path, computed like W: W (the
   start of the first point) is the entry spot; a **mid spot** is the start of a point that follows a gap in which
   the car fits: front at the game's stop margin before the point, rear clear of every earlier point
   (`gap * laneLength >= carLength + margin`, carLength = the car's prefab bounds front to rear, since 0.2.2 the whole
   vehicle including trailers (section 9), margin 0.9 m / 0.4 m bicycles). Points that touch or overlap form one group (no spot inside a path). No gap size is defined: the room is
   the car's own body. Code: `StretchState.FindSpots`.
2. The points from one spot to the next are a **stretch**. At each spot the car makes ONE decision for its stretch
   (stock judgement on each point). Only the car at the spot decides: a follower is behind the spot, so every busy
   point of that stretch holds it at the spot (it cannot cross on the leader's gap); car-following stays stock.
3. **While crossing** (front past the stretch's spot) it re-checks ALL uncrossed points of its stretch (a point is
   crossed once the front has reached its start). If any is busy by the stock gap judgement (a blocking reservation or
   the 2 s look-ahead: "a car it missed or misjudged will be there") and it can stop comfortably before the FIRST
   uncrossed point, it stops there (safety stop; it may stand inside a lane it has partly crossed: accepted), and waits
   until every remaining point of the stretch is clear (each update re-runs the test on all of them). If it cannot stop
   comfortably: no give-way, stock collision braking only (0.2.0 behaviour).
4. With no mid spots this is exactly the 0.2.0 decision at W plus the safety net. Points of a LATER stretch hold the car
   at that stretch's spot (if it can still stop there, else root cause 2's stock give-way).

**Comfortably** = the game's own can-stop test for an overlap, `GetBrakingDistance(car, currentSpeed, timeStep) <=
num5 + num` (I 1236): the car stops before the point within its prefab braking `m_Braking`. CS2 has a single braking
rate (`CalculateSpeedRange` never decelerates faster than `m_Braking`, VehicleUtils 645-651; there is no separate
emergency braking), so "comfortable" = "within the stock braking"; no new margin or gap time.

What a safety stop reacts to: reservations that block (I 1288) and cars counted through the 2 s look-ahead (I 1372).
Not to: a car physically in a later point (stock collision braking at THAT point, as stock and 0.2.0), a car asking for
space (priority 96, dropped while crossing as in 0.2.0), pedestrians (stock pedestrian braking). Reason: deadlock,
below.

### Where mid spots exist (from stock geometry)

The overlap ranges come from `LaneOverlapSystem` (lane areas incl. width), so a mid spot exists exactly where the node
lane runs through an area no other live lane crosses, for at least the car's length plus the margin:
- divided roads with a central reservation inside one node (crossing or turning across Carlisle-type roads, if the
  median opening is longer than the car + 0.9 m along the path);
- wide junctions where a turning path runs clear between the near and far lanes (big skew junctions);
- the stretch before the exit merge of a turn that has crossed everything else (a spot before the merge if the gap
  fits the car).
None exist: plain two-lane crossroads and T-junctions (lanes touch), turners across oncoming lanes that lie side by
side, narrow medians, and every vehicle longer than the gap (buses and trucks often get one decision where cars get
two). Dual carriageways built as two separate nodes are two junctions (one decision per node lane, as before). Red
lanes do not close a gap (not live); track lanes always do.

### Deadlock (0.2.1)

- **Spot waiters hold nobody**: the entry spot is before every live point, a mid spot only exists where the whole car
  fits between points (tested on random lanes, StretchDeadlockTests). A standing car is not projected (speed 0), does
  not reserve its current lane (N 2263) and reserves nothing ahead at speed 0 (N 2259: braking distance 0).
- **Safety-stopped cars**: a safety stop waits only on predictions (reservations, 2 s look-ahead). Both come only from
  cars that are MOVING towards the point (a standing car projects 0 and reserves nothing ahead). Such a car either
  passes the point (clears it) or stops at its own spot or safety point, after which its reservation lapses within one
  rollover (R 29-34) and its look-ahead is 0. So two safety-stopped cars cannot wait for each other; a wait cycle
  needs every car of it to be moving, which ends by itself.
- **A genuine mutual wait if the rule waited on physical cars**: C1 safety-stopped before its first uncrossed point A
  because C2 is physically in a later point B, while C1's body (in a point it has partly crossed) is in C2's path:
  C2 waits for C1, C1 for C2; stock would have pulled C1 forward to B (collision braking at B) and could free C2's path.
  Stock-consistent resolution (no timeout): a physical car in a point gives the stock collision braking at that point
  (`StretchState.ForVehicle`: physical -> Stock), never a wait further back. Same for space requests (priority 96,
  from a standing, blocked car): dropped while crossing, as in 0.2.0. Residual: the stock reservation-priority test
  (`priority2 > num6`, I 1288) also fires on a 96 space reservation when the overlap's priority delta is above 4; the
  game itself would then hold a crossing car the same way (stopped at the point instead of the first uncrossed point).
  The "safety stops inside" counter makes such a case visible (it would grow every window).
- **Queues at a mid spot**: a follower that enters while the mid spot is taken queues by stock car-following and may
  stand with its body in the near half (like an exit queue). Owner ruling 14 Nov 2026 (owner choice 7): ALLOWED.

### Performance (0.2.1)

BeginLane now finds the spots: one pass for W, about three passes per group of touching points, one pass for the first
uncrossed point (node lanes have tens of overlaps; signal lookups once per overlap). With the rule off and the timing
log off nothing runs (`m_Mode == 0`). The probe compares job time against stock and 0.2.0.

## 9. 0.2.2 trailers in the vehicle length (owner ruling, 14 Nov 2026)

> **0.2.3: SUPERSEDED** with the mid spots (section 10). `Core/VehicleLength.cs` and `Stage2/JRVehicleLength.cs` are
> kept (unit-tested, harmless) but nothing calls them; the trailer lookups left the navigation job (S2-20/21/23).

Owner ruling: trailers count in the vehicle length used for mid-junction waiting spots. In 0.2.1 the room was the
controller's prefab bounds only, so a tractor unit could be given a spot on a central reservation that its trailer
did not fit: it waited with the trailer across the near carriageway.

**How the game places trailers (stock data only).** A trailer's attach point is put exactly on the attach point of the
piece that pulls it, each in its own prefab frame (z = forward):
`trailer position = puller position + rotate(puller rotation, CarTractorData.m_AttachPosition) - rotate(trailer rotation,
CarTrailerData.m_AttachPosition)`, in `Game.Simulation.CarTrailerMoveSystem` (all movement types; `Free` also aims the
trailer from its `CarData.m_PivotOffset` to the hitch, the hitch itself stays coincident), `Game.Vehicles.InitializeSystem`
323-324 (spawn) and `Game.Rendering.ObjectInterpolateSystem` 1754-1768. A trailer that pulls another one has its own
CarTractorData and the chain moves on to it (CarTrailerMoveSystem, InitializeSystem 335-338). The attach points come
from the prefab: puller z = `bounds.min.z + CarTractor.m_AttachOffset.z`, trailer z = `bounds.max.z -
CarTrailerPrefab.m_AttachOffset.z` (`Game.Prefabs.VehicleInitializeSystem` 406-407 / 421-422). (CarTrailerMoveSystem is
missing from the yb-2026-11-13 full decompile, which stopped early; it was decompiled on its own from the same Game.dll,
sha256 f076df4b..., for this check.)

**Length.** Chain the pieces along the controller's axis (the combination standing straight, as it waits at a spot):
origin of piece k+1 = origin k + puller attach z - trailer attach z; length = front-most front minus rear-most rear over
all pieces. A semi-trailer's front over the tractor's rear is counted once. No constant is added: the hitch spacing is
entirely in the two stock attach points. Without trailers the result is exactly 0.2.1's bounds length (cars, buses,
bicycles unchanged). Code: `Core/VehicleLength.cs` (`CombinedLength`, unit-tested), `Stage2/JRVehicleLength.cs`
(reads the `LayoutElement` buffer, element 0 = the controller as in the game's UpdateTrailers).

**Where it runs.** Once per car and navigation update, in `UpdateNavigationTarget`, where the navigation job builds the car's
iterator (block S2-21), only when stage 2 is on (`m_JRMode != 0`; the timing log alone also turns it on so the counters
see the same spots). The length is a local of that call, passed to the iterator through `JRWholeJunction.Create` and
used by `BeginLane` for `FindSpots`; it is never a field of the job struct (the job runs in parallel and its workers
share that struct; a first 0.2.2 cut did that and was fixed in review JR-T-002dr). Layout buffer through a read-only
`BufferLookup` (the job already reads it through a read-only type handle). Rule and timing log off: one int test per
car, nothing else.

**Effect.** An articulated truck or a car with a caravan gets a mid spot only where the whole combination plus the stop
margin fits between two conflict points; otherwise it makes one decision at the entry spot for the whole junction, as
any vehicle longer than the gap did in 0.2.1. Deadlock argument of section 8 unchanged (a mid spot still has no other
path under the whole body). Tests: VehicleLengthTests (car unchanged, semi-trailer longer than an 8 m reservation gets
no mid spot while the tractor alone would, multi-trailer chaining and sums, decompile cross-checks).

## 10. 0.2.3 arrival-time projection (JR-T-002f, owner rulings 14 Nov 2026, binding)

**Problem (JR-T-002e boldness analysis).** Stock judges each conflict at the moment the car's reservation reaches it,
with a fixed 2 s look-ahead on the other car. The whole-path decision at W used that same 2 s window for every point,
also for a far point the car needs several seconds to reach. Example: Valley St right turn out across both Carlisle
carriageways. The car went when the far carriageway looked clear for 2 s, reached it 3-4 s later, and then had to
safety-stop or force a gap. The result was "bold" right turns and safety stops on the central reservation.

**Owner rulings (14 Nov 2026).**
1. Fix (a). Compute the car's OWN arrival time at each conflict point from its actual state, with the stock
   CarData / VehicleUtils formulas, the speed-in-turns limit and braking, and no hardcoded margin. Then run the
   unchanged stock test with the other cars projected forward at constant speed:
   - cars on conflicting junction lanes: look-ahead 2 s + arrival time;
   - cars on the approach lanes feeding a conflicting junction lane: a virtual reservation (position + stock braking
     distance + speed x arrival time) through the stock reservation test.
2. Count ALL cars on the feeding lanes. Their routes are not read.
3. The safety re-check while crossing uses the same projection, from the current state to each remaining uncrossed
   point. The stock can-stop test stays, and so does stock collision braking when the car cannot stop.
4. Superseded by the clarification below.
5. b1 diagnostics: zero cost when off, Burst-safe, fixed size.

**Owner clarification (14 Nov 2026, binding).**
- Supersedes: mid spots (section 8 point 1), b3 (JR-T-002e: mid spots on narrow medians) and the trailer length for
  mid spots (section 9, owner choices 7-9). Ruling 4 is superseded too.
- There are NO planned mid-junction waiting spots, and central reservations are not special.
  1. The car waits just before its FIRST crossing point (W). There it makes ONE decision for its whole path, using the
     fix (a) arrival times and counting all approach-lane cars.
  2. While crossing, the re-check uses the same projection. If it finds a miscalculation and the car can still stop
     comfortably, the car stops just before its first uncrossed point. It goes on when everything remaining is clear.
  3. If the car cannot stop: stock collision braking. Standing obstacles: stock following.
- Code: `StretchState.FindSpots` returns only W, so the path is one stretch. The "waited at a mid-junction spot" counter
  is gone. The trailer length code is kept, unused (see section 9).

**Own arrival time** (`Core/ArrivalTime.cs`, `ArrivalTime.Compute`). The time for the car's front to reach the conflict
start `u`, at distance `Wait(u) + margin` (the game's num5 plus its margin). The model steps the game's own motion with
the navigation time step (`m_TimeStep`), as `CarNavigationSystem` advances the car:
- **Speed per step.** At most `CalculateSpeedRange(car, v, dt).max` (VehicleUtils 645-651):
  `v + InverseSmoothStep(m_MaxSpeed, 0, v) * m_Acceleration * dt`, capped at `max(min, m_MaxSpeed)`. Here
  `InverseSmoothStep(a,b,x) = 0.5 - sin(asin(1 - 2 saturate((x-a)/(b-a))) / 3)` (Colossal.Mathematics MathUtils,
  decompiled with ilspycmd). The speed never drops below `.min = max(0, v - m_Braking * dt)`.
- **Drive speed.** Never above the junction lane's drive speed: `GetMaxDriveSpeed(car, carLane)` (VehicleUtils 610ff:
  the speed limit and the turning formula on `m_Curviness`, the speed-in-turns cap), then `ModifyDriveSpeed(ref v,
  LaneCondition)`. The speed limit is the iterator's `speedLimit` for this lane (S2-04).
- **Before the junction lane.** At most `max(v0, driveSpeed)`, and at most `GetMaxBrakingSpeed(car, distance to lane
  start, driveSpeed, dt)` (VehicleUtils 634-638), so the car brakes in time for the turn at the stock rate.
- **Distance and result.** Trapezoidal distance per step, interpolated inside the last step. The loop is bounded by 64
  steps, a 17 s horizon; a point further out gets the horizon, which is stricter, never bolder. A point at or behind the
  front gets 0 s (near point = stock exactly).
- **Job side.** `GameMotion` wraps the real `VehicleUtils` calls on the car's `CarData`. The tests use
  `StockCarMotion`, a line-by-line mirror. ArrivalRepoTests check the mirror against the decompile.
- **Choice made.** This is the time to REACH the point, not the stock "point of no return" (when the reservation
  reaches it, about `v/(2b) + 4/15 s` earlier at speed). The owner text says "arrival time". The difference is a little
  extra caution at speed; from standstill at W the two are nearly equal.

**Cars on conflicting junction lanes** (S2-10, `AfterProjection`). Stock adds `objectSpeed * 2 / max(1, len)` to the
other car's curve position (I 1372) when its priority wins. While deciding at W or in the safety re-check this becomes
`objectSpeed * (2 + delta) / max(1, len)`, with delta the car's own arrival time at this overlap. It applies only where
stock applied its own look-ahead, and stock then compares the result with the overlap. While crossing (no safety stop)
the look-ahead is undone as in 0.2.0/0.2.1. Counter: "busy only by projection, junction-lane car" (the stock 2 s alone
would have let the car go).

**Cars on feeding lanes** (new block S2-12, `FeedingLaneYield`). Placement: for every overlap in scope, after the game
has computed its reservation priority (`num6 -= num9`).

How the feeding lanes are found, from stock data only:
- A node lane's `Lane.m_StartNode` is `PathNode(source edge, lane index, segment)`, the end node of the edge lane that
  feeds it (LaneSystem 7202 / 7727 / 8498; edge lanes end at `PathNode(edge, ...)`, 4089).
- `PathNode` keeps only the entity index, so the edge is found in the owner node's `ConnectedEdge` buffer with
  `new PathNode(e, 0).OwnerEquals(start)`, or the node itself for a lane chained inside the node.
- The edge's `SubLane` buffer is then scanned for lanes whose `m_EndNode.Equals(start)`. This is the same test
  `TrafficLightSystem.CheckNextLane` (590) uses.

What happens to each car on those lanes (`LaneObject`):
- Skipped: the car itself, trailers (their controller is counted) and cars driving a two-way lane inverted.
- Speed = `Moving` velocity along the lane tangent.
- Virtual reservation reach = `max(0, GetBrakingDistance(car, v, 4/15) - 0.01)` (the stock reservation reach,
  CarNavigationSystem 2260) `+ v * delta`, beyond the metres the car still has on its lane. As an offset on the
  junction lane it goes through the stock test `offset > num10 || priority > num6` (I 1288, the game's own num10/num6).
  The STOP arm (num6 99) therefore blocks on any virtual reservation, as stock does for a real one.
- A hit applies the same hold as a blocking reservation: stop at W, or before the first uncrossed point in the re-check.
  Counter: "busy only by projection, feeding-lane car".
- Cheap exits: no scan unless the overlap is being decided or re-checked, and none when the car is already held at
  least as hard. Lanes fed from the car's own approach (same start node, or its current lane) are skipped, so its own
  followers never hold it.

Where the lookup cannot work (gaps):
- Only one level upstream, like stock reservations. A fast car two segments back is not seen until it reaches the
  feeding lane. At 50 km/h with delta ~3 s that is ~40 m, longer than many short segments.
- Lanes whose source is neither a connected edge of the node nor the node itself are not found, for example a
  middle/side connection owned elsewhere.
- Two-way lanes driven against their curve direction are skipped.
- The actual `Moving` speed is used, not the planned speed. The navigation component is written by the same parallel
  job, so reading it would be a data race. A car about to stop at its own line still counts while it is moving.
- Every car on the feeding lane counts, including cars that will turn away (owner ruling 2). This is stricter than
  stock.
- Emergency vehicles: the emergency halving of priority 102 is not modelled in the drive speed. Their priority goes
  through the stock test unchanged.

**Safety re-check consistency.** `Projecting` = rule on and the overlap's phase is Deciding (at W) or Safety (crossing,
can stop before the first uncrossed point). Both phases use the same delta, computed from the CURRENT state to that
point, so the re-check asks the same question as the decision with fresher data. The rest is unchanged:
- Crossing: no give-way, look-ahead undone.
- A physical car in a later point: stock collision braking there.
- Space requests: dropped while crossing.
- Pedestrians: stock.

Deadlock argument (section 8 "Deadlock") unchanged:
- A standing car projects nothing: look-ahead `0 * (2 + delta)`, and no virtual reservation at speed 0.
- So a safety-stopped car never holds another car except physically.
- The only waiting spot (W) is outside every other path.

**Downsides (expected, owner accepted 14 Nov).**
- **Longer waits at W.** Each later point now needs a gap of 2 s + the arrival time at it. Minor-arm throughput drops,
  most on wide divided roads and from STOP arms.
- **Starvation, by ruling.** On a busy main road a minor arm can wait much longer than stock. There is no safeguard
  and never will be.
- **Hesitation.** The re-check from the current state can find a point busy that looked clear at W: a car
  accelerating more slowly than modelled, a car appearing on a feeding lane, a junction-lane car speeding up. The car
  then makes a safety stop (more mid-crossing stops than 0.2.2 in some cases, fewer bold entries).
- **Stop-start near the boundary.** The decision can flip between updates (start, see a feeding car, stop again).
  Each update re-runs the stock test; there is no hysteresis.
- **Phantom conflicts.** Counting all feeding-lane cars can hold a car for a car that turns away. This is stricter
  than stock and is not a collision risk.
- **Point of no return.** Arrival = reaching the point (above), a little stricter than the stock timing.

**Diagnostics (b1, debug/timing log only).** The counters run only when the timing log asks for them.
- **Safety-stop events.** A car's transition into a safety stop is counted once (`SafetyStopEvents`). The table is
  fixed: 8192 slots of (entity index + 1, last frame), lock-free compare-exchange, 8 probes. A car-step at most 48
  frames after the car's last one continues the event (each car is updated every 16 frames). A full probe range
  counts as new: it may over-count but never hides an event.
- **Points at W.** Per point at W, sampled when the car stands within the margin of W: the distance from W and the
  own arrival time. Logged as mean plus an 8-bucket histogram with the median bucket. Distance edges: 2/4/8/12/16/24/32
  m. Arrival edges: 0.5/1/1.5/2/2.5/3/4 s. Samples are weighted by car-steps: a car standing longer contributes more
  samples.
- **"Busy only by projection".** Split into junction-lane car and feeding-lane car.
- **Dropped.** "Waited at a mid-junction spot".

**Performance.**
- Managed microbench (tests, not Burst): `ArrivalTime.Compute` ~0.3 us per call.
- Cost per car-step:
  - One arrival time per in-scope overlap, cached for repeated starts, only while deciding or re-checking.
  - A feeding-lane scan only when it could tighten the hold: a few connected edges, their sub-lanes, and the lane
    objects on the 1-3 feeding lanes.
  - Rule off: one phase test per overlap. The timing log off adds nothing.
- Under Burst the arithmetic should be several times faster. The probe's job-time A/B is the measure.

**Tests.** ArrivalTimeTests (standstill acceleration against a manual step-through, turn cap, near vs far point,
approach braking, horizon, monotone, microbench), ArrivalProjectionTests (far-carriageway car outside the stock window
but inside the projected one -> wait at W; near point unchanged; feeding-lane virtual reservation incl. STOP arm and
standing car; re-check uses the projection and stops before the first uncrossed point; can-stop vs cannot-stop; rule
off = stock), SafetyEventTests (one event per transition, gap > 48 frames = new, overflow, histograms),
ArrivalRepoTests (decompile formulas, S2-10/S2-12 text, Projecting covers Deciding and Safety), and
StretchWholePathTests (one decision at W incl. the far carriageway, re-check stop before the first uncrossed point,
wait there until all remaining points are clear).
