# Referencers of the copied types

Searched on 13 Nov 2026 against Game.dll 1.6.4f1 (decompile `~/Code/decomp/yb-2026-11-13/full/`) and every DLL of
the installed mods (82 DLLs under `.cache/Mods/` and `Mods/` of the Proton prefix; type names searched in both the
UTF-8 metadata strings and UTF-16 user strings, which also catches Harmony patches that name a type or method).

## In the game

| Referencer | What it does | With the copy |
|---|---|---|
| `Game.Common/SystemOrder.cs:311-312` | `UpdateAt<CarNavigationSystem>(GameSimulation)`, `UpdateAfter<CarNavigationSystem.Actions, CarNavigationSystem>(GameSimulation)` | Unchanged: the game's systems stay registered and are only disabled. The copy is registered `UpdateAfter<JRCarNavigationSystem, CarNavigationSystem.Actions>`, its Actions after it, so the copy runs in the same slot. |
| `Game.Common/SystemOrder.cs:611-612` | The same two in `LoadSimulation` | The copy is registered in `LoadSimulation` the same way, and the switch decides there too. |
| `Game.Common/AssemblyTypeRegistry` (code-gen) | Type registry entries for the game's types | Not affected (the copy's own types are registered by the Entities code-gen of the mod assembly). |
| `Game.Debug/NavigationDebugSystem.cs` | Uses the game's `CarLaneSpeedIterator` for a debug overlay | Not affected: a debug view that runs its own iterator; it does not depend on which navigation system ran. |

No other game system reads the navigation system's queues or job handle: the queues are created by the navigation
system and drained and disposed by its own Actions system in the same step. Everything else couples through
components (`CarNavigation`, `CarNavigationLane`, `CarCurrentLane`, `Blocker`, lane reservations, lane signals,
traffic ambience, lane effects), which the copy reads and writes exactly like the game, in the same slot.

## In installed mods

| Mod | Reference | With the copy |
|---|---|---|
| ClearTheWay (`pdx_mods/151583_11/ClearTheWay.dll`) | `Mod.cs:26` `UpdateBefore<ClearTheWaySystem, CarNavigationSystem>(GameSimulation)`; also `ClearTheWayPostSystem` before `CarMoveSystem`; reads/writes car navigation components | Keeps working: its system still sorts before the game's navigation system, and the copy runs after the game's Actions system, so ClearTheWay still runs before whichever pair is active. Its post system runs before `CarMoveSystem`, after both pairs. Verify in game (PROBE-STAGE1.md). |
| Gap Acceptance (own mod) | No reference to either type. `ExitHoldSystem` runs before `CarMoveSystem`; the lane-signal hold runs after `TrafficLightSystem` | Keeps working: both slots are unchanged relative to the active pair. |
| All other installed mods (TLE, Traffic, RoadBuilder, No Traffic Despawn, Realistic Trips/Parking, bridge, ...) | None found | Not affected. |

## Order inside one simulation step (GameSimulation)

```
[systems "before CarNavigationSystem": CarDrivingSwitchSystem, ClearTheWaySystem, ...]
CarNavigationSystem            (game, disabled when the copy runs)
GameNavTimingProbeSystem       (debug timing only)
CarNavigationSystem.Actions    (game, disabled when the copy runs)
JRCarNavigationSystem          (copy, disabled when the game's runs)
CopyNavTimingProbeSystem       (debug timing only)
JRCarNavigationSystem.Actions  (copy, disabled when the game's runs)
[systems "after CarNavigationSystem" / "after its Actions"; then TrainNavigationSystem ...]
```

Systems that other mods place "after CarNavigationSystem" sort after the copy too, whatever the mod load order:
`UpdateSystem` runs a system's after-list in registration order, recursing into each entry's own before/after lists.
The game registers its Actions first in that list, and the copy hangs off the Actions, so the copy comes before any
later entry. Only a mod system placed "after CarNavigationSystem.Actions" and loaded before Junction Rules would run
before the copy instead of after it. No installed mod does that.
