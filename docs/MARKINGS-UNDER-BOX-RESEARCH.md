# Road markings under yellow boxes (JR-T-011 part B, 16 Nov 2026)

Owner: the box should hide the road's own markings under it (lane dashes, centre lines incl. double solid, arrows, the
road's painted "40"s). Decompile: `~/Code/decomp/yb-2026-11-13/full` (YB), Traffic `~/Code/decomp/traffic`, RoadBuilder
`~/Code/decomp/roadbuilder`.

## How the game draws them
- **Lane markings are lanes.** A lane prefab's `SecondaryLane` component lists marking lanes for its left/right side
  and crossings (YB `Game.Prefabs/SecondaryLane.cs:149-193`, `SecondaryLaneInfo.cs`); `Game.Net.SecondaryLaneSystem`
  creates them as lane entities (tag `Game.Net.SecondaryLane`) owned by the edge / node, each with a `Curve` and a
  prefab whose meshes are decals. Stock trims them where they overlap crosswalks / car lanes by cutting the curve into
  pieces before creating the lanes (`SecondaryLaneSystem.cs:1145-1230`, flags `SecondaryLaneDataFlags.cs`).
- **Drawn as tiled decal meshes along the lane curve** through the batch system: `BatchInstanceSystem.cs:763-822`
  (MeshType.Lane instances, tile count per curve), `BatchDataHelpers.cs:532-547` (curve matrix),
  `ManagedBatchSystem.cs:1015-1029` (decal material: render queue = shader queue + `DecalProperties.m_RendererPriority`,
  `colossal_DecalLayerMask`), i.e. DBuffer decals sorted by render queue.
- **The renderer skips curve ranges listed in a lane's `Game.Net.CutRange` buffer** (`Game.Net/CutRange.cs`,
  `BatchInstanceSystem.cs:787-812`, `BatchDataSystem.cs:1137-1170`; the remaining pieces are re-tiled). Stock writes it
  only on crosswalk pedestrian lanes (`LaneSystem.cs:8694-8712`, Traffic's copy `TrafficLaneSystem.cs:9104-9122`) and
  on fences (`OverrideSystem.cs:418-500`, fences only). It is a saved type (`ISerializable`).
- **The "40" and the arrows are objects**, not lanes: `SecondaryObjectSystem.CreateSecondaryEdgeObjects`
  (`SecondaryObjectSystem.cs:2502-2790`) places the road pieces' `NetCompositionObject`s (`NetPieceObjectInfo.cs`); a
  sign placeholder picks a `TrafficSignObject` by need, e.g. SpeedLimit when the lane speed differs from the lane
  default (`:2749-2752`, scoring `:3080-3096`). RoadBuilder roads set their own speed (`NetworkPrefabGenerationUtil.cs:
  1635`) so the game paints the speed decal; RoadBuilder adds no marking objects itself (its sub-objects are pillars and
  outside connections only, `:1262-1340`). Objects are hidden by `Game.Tools.Hidden` (`BatchDataSystem.cs:605/955/1588`
  `isHidden`), a tool tag that is not saved.
- Rebuild signal: `Game.Common.BatchesUpdated` (`PreCullingSystem.cs:2428-2436`, `BatchInstanceSystem.cs:342`), cleared
  by the game each frame (`PrepareCleanUpSystem.cs`).

## Options
| Option | Effort | Risk | Look |
|---|---|---|---|
| 1. Asphalt fill under the hatch (area decal, higher queue) | small | low | poor: the area decal is a flat colour/texture pass with no road wear, wetness/snow, dirt or tiling of the road material; a patch at every box, and it would also cover kerb-side markings we keep |
| 2a. Patch / copy the batch systems (per-instance mask) | very large (Burst copies of BatchInstance/BatchData) | high (render core, every game update) | best |
| 2b. Delete / clip marking lanes or objects | medium | high: game state, regenerated on every edit, saved | ok |
| **3. Stock mechanism: CutRange on marking lanes + Hidden on marking objects, render-only, stripped before every save** | medium | low-medium (see below) | exact: markings stop at the box edge, rest of the road untouched |

## Pick: option 3 (built, 0.4.4)
Why: uses the renderer's own cut path, so the road surface, wear, snow and every other marking stay exactly as the
game draws them; no vanilla code copied or patched; touches only the boxed road's own sub-lanes / sub-objects.
Risks and how they are handled:
- Saves: `CutRange` is saved, so `MarkingSaveGuardSystem` (Serialize phase, ahead of `SerializerSystem`, like
  `TrimPathsSystem`, `SystemOrder.cs:731-735`) removes ours before every save (manual and autosave run the same phase,
  `SaveGameSystem.cs:82`); the paint system puts them back the next frame. `Hidden` is not saved but is stripped too.
- Other writers: only lanes / objects without a CutRange / Hidden of their own are touched (zebras, car, track,
  parking, connection and utility lanes are excluded outright; only decal-only prefabs). Traffic replaces LaneSystem
  but writes CutRange only on crosswalk lanes, same as stock. Tools take `Hidden` off preview originals
  (`ToolClearSystem.cs:93-107`, `ApplyObjectsSystem.cs:479`): the hider re-hides its objects next frame.
- Edits: a road edit re-creates or reuses lanes (`SecondaryLaneSystem.cs:1374-1385` reuses by key); the box rebuild on
  any edit of its sources re-applies the cuts (stale ranges at most one frame).
- Tiling: the dash pattern outside the box is re-fitted to the shortened piece (stock behaviour for cut lanes); dash
  phase next to the box may shift slightly.
- Cost: work only when a box changes (a few dozen lanes); per frame only a check of our own hidden objects.
