# Stage 3 paint research: how to paint a UK yellow box (JR-T-003)

Date: 14 Nov 2026. Research only: no game calls, nothing installed, no mod code run. Game: CS2 1.6.4f1 (decompile of
13 Nov). Builds on `~/Code/cs2-gap-acceptance/docs/YELLOW-BOX-STUDY.md` section 3 and corrects it on elevated roads.

Claim tags: **PROVEN** (cite to code or asset file), **INFERRED** (follows from proven code, not seen running),
**UNKNOWN** (needs a live probe). Shader source is not in any decompile, so anything about what a shader does inside
its volume is at best INFERRED.

## Sources

| Short | Path |
|---|---|
| YB | `~/Code/decomp/yb-2026-11-13/full/` (Game.dll) |
| EAI | `~/Code/decomp/eai/` (ExtraAssetsImporter 80529_43, decompiled for this job) |
| EDT | `~/Code/decomp/edt/` (ExtraDetailingTools 80528_16, decompiled for this job) |
| TLE | `~/Code/decomp/tle/` |
| G87 | `<pdx_mods>/97828_5/` (G87 UK Road Markings), `<pdx_mods>` = `~/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II/.cache/Mods/pdx_mods` |
| BRIDGE | `~/Code/cities-skylines-2-mcp`, branch `feature/t178a-object-tool`, `CS2MCP.Bridge/BridgeToolSystem.cs` |
| TSM5 | DfT Traffic Signs Manual chapter 5 (2019), section 8 "Yellow box junction markings" (web) |

---

## Short answer to the owner's worry

- **A surface (G87 "UK Junction Box", or any area) cannot paint an elevated deck.** Its paint volume is built around
  the **terrain** under it, not around the road. On a flyover it would paint the ground (and any road) underneath, not
  the deck. PROVEN for the volume, INFERRED for the visible result (probe S2).
- **Lane markings (netlanes) and object decals do sit at deck height**, because their paint box follows their own 3D
  curve or position. PROVEN for the geometry.
- **Best route: the mod draws the box itself with the game's own surface shader**, at the node's real 3D shape. Same
  look as a surface, works at any height and any shape, follows edits for free, nothing saved in the city.
  INFERRED feasible; one probe build decides it (probe S6).

---

## Q1. How surfaces render, and where their height comes from

1. **A surface is drawn as one "triangle volume" per area triangle**: a 6-vertex prism mesh named "Area triangle
   volume", drawn with `Graphics.RenderMeshIndirect` and the batch material. PROVEN: YB
   `Game.Rendering/AreaRenderSystem.cs:221-262` (mesh, name at 259), draw loop `:182-211`. So a surface is a
   **projected decal volume**, not a mesh draped on the terrain. INFERRED from the mesh shape (shader not visible).
2. **Which layers receive it** is the prefab's `RenderedArea.m_DecalLayerMask` (default Terrain only), set on the
   batch material as `colossal_DecalLayerMask`. PROVEN: YB `Game.Prefabs/RenderedArea.cs:41-42`,
   `Game.Rendering/AreaBatchSystem.cs:2098`; layers `Game.Rendering/DecalLayers.cs:6-13` (Terrain 1, Roads 2,
   Buildings 4, Vehicles 8, Creatures 16, Other 32).
   - G87 UK Junction Box: mask **39** = Terrain + Roads + Buildings + Other (not Vehicles). PROVEN: G87
     `Surfaces/Misc/G87 UK Junction Box/Material.json:4`; EAI copies it into the prefab
     (`ExtraAssetsImporter.AssetImporter.Importers/SurfacesImporterNew.cs:50`). So road meshes, bridge decks included,
     *can* receive it. The limit is height, not layers.
3. **The volume's height range** per triangle is
   `YMin = HeightRange.min - offset + lowest node y`, `YMax = HeightRange.max + offset + highest node y`.
   PROVEN: YB `Game.Rendering/AreaBatchSystem.cs:1328-1329`. `offset` = 2 x min node distance of the area type =
   **1.5 m for surfaces** (0.75 x 2). PROVEN: `AreaBatchSystem.cs:2075-2079`, `Game.Areas/AreaUtils.cs:410-418`.
4. **`HeightRange` for a surface = terrain height minus the triangle's own plane**, sampled over the triangle.
   PROVEN: YB `Game.Areas/GeometrySystem.cs:133-151` (a non-Space area uses terrain height), `:380-411`
   (`UpdateHeightRange`; for a standalone surface the range is terrain-only, `useTriangleHeight` is only true for
   Space areas or areas owned by a building, `:135`), `:413-483` (`GetHeightRange`: `heights[] - plane`, scaled back
   at `:481-482`).
   - So the volume always hugs the **terrain** +-1.5 m, wherever the nodes are. With nodes at deck height 10 m above
     the ground, range ~ [-10, -10] and the volume is still [ground - 1.5, ground + 1.5]. INFERRED (arithmetic of
     the cited lines).
5. **Node heights**: an area node with `m_Elevation == float.MinValue` is snapped to terrain when the area is
   generated; any other value keeps the given y. PROVEN: YB `Game.Tools/GenerateAreasSystem.cs:305-313`,
   `Game.Areas/AreaUtils.cs` `AdjustPosition`. In game mode the area tool always writes `float.MinValue`
   (`Game.Tools/AreaToolSystem.cs:1879-1882`); only the editor path, and sub-areas of objects with a parent mesh, lift
   nodes (`AreaToolSystem.cs:1822-1869`, `Game.Tools/ObjectToolBaseSystem.cs:2019-2021`). The bridge also writes
   `float.MinValue` (BRIDGE `BridgeToolSystem.cs:1336-1339`). Terrain edits re-snap only `MinValue` nodes
   (YB `Game.Simulation/GroundHeightSystem.cs:362`).
   - Even a code-placed surface with nodes at deck height still gets the terrain-hugging volume (point 4). INFERRED.
6. **Results by place:**
   - At-grade junction: road surface is at terrain level, inside +-1.5 m: paints. INFERRED (also the owner already
     hand-draws G87 surfaces on roads).
   - Elevated junction / bridge deck: **does not paint the deck**; paints the ground and any road below inside the
     footprint. INFERRED. Probe S2.
   - Road over a tunnel (at grade): paints; a tunnel is metres below, outside +-1.5 m. INFERRED.
   - Junction under a flyover: paints the ground junction; the deck above is out of range. INFERRED.
7. **Escape hatches seen in code** (none is clean):
   - A **Space**-type area skips terrain sampling, so its volume sits +-2 m around its own nodes
     (`GeometrySystem.cs:133-151`, `UpdateHeightRange` else branch; Space min node distance 1 m, `AreaUtils.cs:410-418`).
     `RenderedArea` only adds a `Batch` component (`RenderedArea.cs:95-98`) and the batch code has no type filter
     (`AreaBatchSystem.cs:1043-2120`). A custom Space prefab with a RenderedArea could therefore paint at deck height.
     INFERRED; it is an unusual combination nobody ships (RenderedArea's menu lists only Surface and Lot,
     `RenderedArea.cs:15-19`) and needs our own asset. UNKNOWN in practice.
   - A mod system could overwrite `Triangle.m_HeightRange` of its own box areas after `Game.Areas.GeometrySystem`.
     `Triangle` is not saved (`Game.Areas/Triangle.cs:9`, `IEmptySerializable`) so it must be redone after every load
     and update; without the mod, elevated boxes fall to the ground. INFERRED.

## Q2. Can code place a surface over a junction without anarchy?

- **Yes, with one exception.** In game mode a surface has no `PhysicalGeometry` flag, so validation never checks it
  against nets or objects. PROVEN: YB `Game.Prefabs/AreaInitializeSystem.cs:322-331` (Surface/Space get no flags; Lot
  gets PhysicalGeometry), `Game.Areas/AreaUtils.cs:680-690` + `Game.Areas/ValidationHelpers.cs:980` (the editor-only
  marker rule), `ValidationHelpers.cs:981-1021` (net and object checks run only with PhysicalGeometry).
- **Exception: overlap with another surface.** A standalone surface is checked against other areas of the same type and
  errors on overlap (`ValidationHelpers.cs:1022-1047`, `:541-571`). Building lots (type Lot) do not block it
  (`:553`). So a box over a junction that already has a hand-drawn surface fails. PROVEN by code; probe S1b.
- Other checks still apply: min node distance / shape (`InvalidShape`, 0.75 m) (`ValidationHelpers.cs:866-920`). PROVEN.
- The bridge path is the vanilla one: `CreationDefinition` + node buffer -> Temp -> ValidationSystem ->
  `GetAllowApply()` -> apply. PROVEN: BRIDGE `BridgeToolSystem.cs:1312-1340`, `:518-521`. Not anarchy.
- **Practical catch**: that path needs our tool to be the *active tool* for a few frames (`Activate()` sets
  `m_ToolSystem.activeTool`, BRIDGE `BridgeToolSystem.cs` `Activate`). Re-placing boxes after every road edit would
  steal the player's tool mid-edit. Writing area entities directly skips validation (owner's no-anarchy rule).
  INFERRED; this is the main reason not to use saved surfaces for auto-following boxes.

## Q3. Decals and netlane markings on elevated roads

- **Netlane markings (vanilla road markings and G87 lines) follow their lane's full 3D curve.** A
  `NetLaneGeometryPrefab` draws its mesh bent along the lane's `Bezier4x3` (x, y, z) via `BuildCurveMatrix`. PROVEN: YB
  `Game.Rendering/BatchDataHelpers.cs:532-547`, `Game.Prefabs/NetLaneGeometryPrefab.cs`. G87 lines are such prefabs
  with a "CurvedDecal" box mesh (EAI `NetLanesDecalImporterNew.cs:26-38`) and a thin paint box:
  `colossal_MeshSize.y = 0.5` m, mask 3 = Terrain + Roads (G87 `NetLanesDecal/RoadMarking/G87 RM Line Yellow 30cm
  Wide/Material.json`). So a yellow line lying on a deck paints the deck. INFERRED (vanilla markings on elevated roads
  are the same mechanism).
- **Standalone netlanes are editor-only in vanilla.** The net tool uses the lane container only when
  `IsEditor()` (YB `Game.Tools/NetToolSystem.cs:5703-5709`); EDT makes that call return true by a Harmony patch that
  inspects the call stack (EDT `ExtraDetailingTools.Patches/GameModeExtensionsPatches.cs:11-20`). So every G87 line in
  the city depends on EDT to edit, and a JR box built from them would sit in the save as editor-container nets.
  PROVEN.
- Height when drawn by hand: control points come from terrain + elevation, or from the hit point on an object surface
  (`NetToolSystem.cs:1871-1905`). Whether a line drawn on a deck snaps to the deck: UNKNOWN (probe S3). By code we set y
  ourselves.
- **A box from netlanes**: border = 4-8 line edges; hatch = 2.5 m criss-cross = 15-40 more edges per box. Crossing
  hatch lines may split into nodes like other nets: UNKNOWN. Heavy, saved, G87+EDT dependent. Not recommended.
- **Object decals**: EDT adds `NetArea` + `ObjectSurface` snapping for decals (EDT
  `Patches/ObjectToolSystemPatch.cs:80-90`), so decals can sit on a deck. But they are fixed rectangles; G87 has no
  hatch decal (G87 `Decals/RoadMarkings` list). Not usable for odd shapes. PROVEN (list) / INFERRED (deck).

## Q4. Mod-drawn paint

Two ways:

- **(B) Tool overlay** (`OverlayRenderSystem`): lines, curves, circles only. "Projected" items get a box spanning the
  whole terrain height range (YB `Game.Rendering/OverlayRenderSystem.cs:172-182`, range at `:632`), so they hit deck
  *and* ground. Non-projected items are flat quads at their own 3D height. TLE draws non-projected curves on the
  node's own `EdgeNodeGeometry` (TLE `C2VM.TrafficLightsEnhancement.Systems.Overlay/OverlayRenderingHelpers.cs:37-53`),
  which is why its overlay sits right on elevated nodes. PROVEN. Looks like a glowing tool overlay, not paint;
  must be refilled every frame. Good for the selection tool and a debug "show boxes" view.
- **(E) Mod-fed surface batch (recommended)**: copy exactly what `AreaBatchSystem` does for a surface, but with our own
  triangles:
  - Material: `new Material(AreasConfigurationPrefab.m_RenderedAreaMaterial)` + our own textures/colour/decal mask, the
    same calls the game makes (YB `Game.Rendering/AreaBatchSystem.cs:2083-2120`, `Game.Prefabs/AreasConfigurationPrefab.cs:17`).
    No G87 needed. PROVEN recipe.
  - Data: our own `AreaTriangleData` buffer (layout PROVEN: YB `Game.Rendering/AreaTriangleData.cs:5-30`), with
    `m_YMinMax` set around the node's real height (+-~1 m) and `m_OffsetDir` set to the box axis so the hatch is
    rotated to the junction (the game uses the owner's forward there, `AreaBatchSystem.cs:1050-1075`).
  - Draw: the same `RenderMeshIndirect` call with the "Area triangle volume" mesh and buffers
    `colossal_AreaBatchBuffer`, `colossal_AreaBatchColors`, `colossal_VisibleIndices` (YB
    `AreaRenderSystem.cs:45-48, 182-211`). `AreaBatchSystem.GetAreaBatch` is public (`AreaBatchSystem.cs:1710`), so we
    can read a real batch to copy settings.
  - Cost: one indirect draw per material (hatch, border) for all boxes; buffers rebuilt only when a boxed node
    changes. Negligible per frame. INFERRED.
  - Look: same shader as any surface, so it reads as paint (lit, with road wear). INFERRED; probe S6.
  - Unknowns: what the colour buffer and visible-index buffer must hold, and whether the shader reads anything else
    (terrain textures, culling data). UNKNOWN; probe S6 settles it with a tiny debug build.
  - Decal mask: use **Roads only (2)** so a low deck never paints the ground under it and cars are never painted.
    INFERRED that junction surfaces are on the Roads layer (G87 lines with mask 3 paint roads).
- Plain `Graphics.RenderMesh` with some other decal material: needs per-instance data the game's decal shaders expect
  and that we cannot see. UNKNOWN; (E) avoids this by reusing a path whose inputs are all visible in the decompile.

## Q5. Following the junction shape and edits

See the section "Arbitrary junction shapes" below for the polygon, hatch layout and edit handling.

Short version: geometry lives in `StartNodeGeometry`/`EndNodeGeometry` of each connected edge, is recomputed by
`Game.Net.GeometrySystem` for every `Updated` node/edge (YB `Game.Net/GeometrySystem.cs:3378-3381`), and is not saved
(`IEmptySerializable`: `Game.Net/StartNodeGeometry.cs:6`, `EdgeGeometry.cs:7`, `NodeGeometry.cs:7`). A mod-drawn box
rebuilds from it in the same frame; a saved surface has to be deleted and re-placed through a tool.

## Q6. Save safety

| | Mod removed | G87 removed |
|---|---|---|
| (A) G87 surface | Boxes stay as plain G87 surfaces; they no longer follow edits, so they go stale after road works. INFERRED | Area keeps a dead prefab: the prefab becomes an obsolete placeholder on load (YB `Game.Serialization/ResolvePrefabsSystem.cs:527-539`) and renders with the game's "missing rendered area" prefab (`AreaBatchSystem.cs:755-763`, `Game.Prefabs/MeshSettings.cs:16`). Visible junk, INFERRED look. |
| (A2) surface + height fix | Elevated boxes fall to the ground under the flyover. INFERRED | as (A) |
| (C) netlanes | Lines stay; go stale | Dead lane prefabs in editor-container nets: UNKNOWN look |
| (B), (E) mod-drawn | Nothing in the city; boxes vanish. PROVEN by design (nothing written) | No effect (E uses its own texture) |

Selection itself (which nodes are boxed) must be stored somewhere for every option. A serialized component on the node
is the obvious choice; how the game loads a save with an unknown component type when the mod is gone is UNKNOWN (the
study's probe P7). A mod-side store keyed by node position would be the fallback.

---

## Options table

| Option | At-grade | Elevated | Tunnel-top | Roundabout | Driveway | Looks like paint? | Follows edits? | Saved in city? | Validation / anarchy risk | Dependency | Effort |
|---|---|---|---|---|---|---|---|---|---|---|---|
| **A** G87 Junction Box surface (+ G87 line border), placed by code | Yes | **No** (paints ground below) | Yes | Pieces only (no holes in areas) | Yes | Yes, but hatch angle fixed to world axes | No: delete + re-place through our tool (steals the player's tool) | Yes | Passes in game mode; fails over another surface | G87 (+ EDT for border) | Medium |
| **A2** A + mod rewrites triangle height range | Yes | Yes (INFERRED) | Yes | Pieces | Yes | Yes | As A | Yes; falls to ground without mod | As A | G87 | Medium-large |
| **B** Tool overlay (OverlayRenderSystem) | Yes | Yes | Yes | Yes | Yes | **No** (glowing tool lines) | Yes, every frame | No | None | None | Small |
| **C** Netlane lines (border + hatch as nets) | Yes | Probably (INFERRED) | Yes | Awkward | Yes | Yes | No: re-place through tool | Yes, 20-50 nets per box | Unknown (crossing lines) | G87 + EDT | Large |
| **D** Object decals | Rectangles only | Yes (EDT snap) | Yes | No | Rectangle fits | Yes | No | Yes | Low | G87/EDT | n/a: shape fails |
| **E** Mod-fed surface batch (game's surface shader, own triangles) | Yes | **Yes** | Yes | Yes (any triangles, holes fine) | Yes | Yes (same shader), hatch rotatable | **Yes, same frame** | **No** | **None** (no entities placed) | None (own texture) | Medium |

## Recommendation

1. **Build (E)** as the box paint: triangles from the node's own geometry, hatch and border as thin geometry strips
   (exact TSM layout) in a solid-yellow surface material, decal mask Roads, volume +-1 m around the node height.
   Nothing saved, works at any height and shape, no validation, no G87.
2. **Use (B)** for the selection tool highlight and a debug outline (TLE-style, non-projected).
3. **Gate (E) on probe S6.** If the surface shader will not take our buffers, fall back to (A) at grade only and (B)
   for elevated nodes, and look again at (A2) / a Space-type asset.
4. Do not use netlanes (C) or decals (D) for boxes.

## Open unknowns

| # | Unknown | Settled by |
|---|---|---|
| U1 | Exact visible result of a surface near an elevated junction (deck unpainted, ground painted) | S2 |
| U2 | What `colossal_AreaBatchColors` / `colossal_VisibleIndices` must hold; whether the surface shader needs more inputs | S6 |
| U3 | Whether (E) looks like the G87 surface (wetness, wear, night, LOD shimmer of 0.15 m lines) | S6 |
| U4 | Whether junction surfaces and decks receive the Roads decal layer only, or also Other | S6 (try mask 2, then 34) |
| U5 | Whether a hand-drawn G87 line snaps onto a deck | S3 |
| U6 | Loading a save with JR removed while nodes carry a JR component | study P7 |
| U7 | Carriageway inset (sidewalk width) from composition data matches the visible kerb on all road types | S7-S10 |
| U8 | UV mapping of the surface shader (world XZ x UVScale, rotated by `m_OffsetDir`) | S6 |

---

## Arbitrary junction shapes (owner scope addition)

### 1. Where the game keeps a node's real outline

- A node has no outline of its own; `NodeGeometry` holds only bounds and the node surface height `m_Position`. PROVEN:
  YB `Game.Net/NodeGeometry.cs`.
- **Each connected edge owns its slice of the node**: `StartNodeGeometry` / `EndNodeGeometry` ->
  `EdgeNodeGeometry { Segment m_Left, m_Right; Bezier4x3 m_Middle; m_MiddleRadius }`; each `Segment` is a strip
  between two 3D beziers (`m_Left`, `m_Right`). PROVEN: YB `Game.Net/EdgeNodeGeometry.cs`, `Segment.cs`.
- **This is the polygon the game itself uses**: the node's road mesh is one sub-mesh per connected edge
  (`NetSubMesh.StartNode`/`EndNode`) deformed from that edge's `EdgeNodeGeometry` (YB
  `Game.Rendering/BatchDataSystem.cs:926-940`), and clicking a junction tests exactly these strips, plus the
  `m_Middle` pieces, with a separate roundabout case (YB `Game.Net/RaycastJobs.cs:226-258`). PROVEN.
- The `.a` ends of the outer curves are the arm mouth (where the edge ends). TLE draws the mouth line
  `m_Left.m_Left.a -> m_Right.m_Right.a` (TLE `Systems.UI/UISystem.cs:583`). PROVEN.
- **Elevation**: every bezier is 3D, so y comes with it on elevated nodes; the node centre height is
  `NodeGeometry.m_Position` (YB `Game.Net/GeometrySystem.cs:1939-1940`). PROVEN.
- How to get the polygon: for node N, for each `ConnectedEdge` e: g = (e.Start == N) ? StartNodeGeometry : EndNodeGeometry.
  Fill = triangulate each strip g.m_Left, g.m_Right and the two middle pieces exactly as `RaycastJobs.cs:249-256` does
  (left.m_Right = m_Middle, right.m_Left = m_Middle). That gives the whole node surface, including footways.

```
  arm 2                 one connected edge's slice of node N
    \  |                    (EdgeNodeGeometry)
     \ |        mouth a ------------------------- a   (m_Left.m_Left.a .. m_Right.m_Right.a)
 ----+-+-----        |  m_Left strip | m_Right |
 arm1  N   arm3      |               | strip   |
 ----+-+-----        d ----- m_Middle.d ------ d   -> toward node centre
       |
     arm 4         union of all slices = the junction surface the game draws
```

### 2. Each paint option on a non-rectangular polygon

- **Surface (A/A2)**: any simple polygon (no holes); triangulated by the game. Must be deleted and re-placed on every
  edit (Q2 catch). Hatch is a tiled world-aligned texture (G87 texture is one "X" per tile, `_BaseColorMap.png`,
  `colossal_UVScale` 0.36), clipped by the polygon for free, but not aligned to the box. Border needs separate lines.
- **Netlanes (C)**: border = one line per polygon side (kerb curves as beziers). Hatch = clip each diagonal line to
  the polygon (segment vs polygon: find entry/exit points along each line, one net per inside piece). Many entities.
- **Mod-drawn (E)**: we triangulate the polygon ourselves (strips from step 1 are already quads -> 2 triangles each).
  Hatch: generate each diagonal as a 0.15 m wide quad, clip it to the polygon (Sutherland-Hodgman against the fill
  triangles, or clip the line to the outline and then build the quad), lift each vertex to the surface height under it
  (interpolate y from the strip it lies in). Border: a 0.2 m strip along the outline, inset by half its width.

### 3. Laying out the UK hatch on an odd shape

From TSM5 section 8.4: border 200 mm, diagonals 150 mm, spaced about 2 m (shortest side under 9 m) or 2.5 m (9 m or
more), box normally four straight sides, max 30 m; set out from a main diagonal corner to corner of the (notional) box;
a part-width variant and roundabout/gyratory variants exist. Rules for JR:

- **Box = conflict area only**: the carriageway inside the node, closed at each arm by a transverse line at the mouth
  (where the stop / give-way line is), not running up the approaches.
- **Carriageway only**: inset the node outline by the footway width on each side (outer curves are the outer edge of
  the whole road, footways included). Footway width per side = (composition width - carriageway width) / 2 from the
  edge's `NetCompositionData`/pieces; offset with the game's own `NetUtils.OffsetCurveLeftSmooth` (used the same way
  at YB `Game.Net/GeometrySystem.cs:2343-2368`). INFERRED; check by eye (U7).
- **Hatch axis**: take the longest pair of opposite mouths as the box axis; the main diagonal runs corner to corner of
  the polygon's minimum bounding rectangle on that axis; diagonals +-45 deg to the axis, spacing by TSM rule;
  everything clipped to the outline.
- **Skewed / T / Y / 5-arm**: same rule; the outline is whatever the node is. TSM prefers straight sides, so join
  mouths by straight lines when the kerb curve is close to straight (chord error < 0.3 m), else follow the kerb.
- **Divided roads**: the median ends before the node, so it is outside the box; the mouth line spans the whole
  carriageway incl. the median nose gap. A dual-carriageway crossing built as two nodes joined by a short edge = one
  box over both nodes + the short edge (union), or two half-boxes (TSM allows half boxes).
- **Roundabouts**: UK boxes go on the circulating carriageway at an entry, not on the whole ring.
  - Ring of edges (built roundabout): each entry node is a curved T: box that node only.
  - Roundabout node (`m_MiddleRadius > 0`): box = annular sector between the island edge and the outer kerb, spanning
    the arm mouth's angle. Fill triangulated by us (holes fine in E).

```
 Skewed X                    T at an angle               Divided-road junction
   \    \                         \   \                      ||    ||
 ===\====\===               =======\===\=======        ======||====||======
    |\\//|  <- box                 |XXX|                   |XXXXXXXXXXX|  <- one box,
 ===|//\\|===               =======|XXX|=======        ====|XXXXXXXXXXX|====  median noses
     \    \                   (box ends at the         ======||====||======   outside
      \    \                   mouths, not up arms)         ||    ||

 Roundabout node (sector at one entry)        Elevated junction, side view
        .-~~~~-.                                  ===[XXXX]===  <- E: volume +-1 m around deck
      /  .--.   \                                  |        |
     |  ( () )XX |<-- entry arm                    |        |
      \  '--'   /                              ~~~~[xxxx]~~~~~  <- A: volume +-1.5 m around ground
        '-~~~~-'                                                  (paints the ground, not the deck)
```

### 4. Edits: what changes and what to watch

- Any change to a node or its edges marks them `Updated`; `Game.Net.GeometrySystem` recomputes `NodeGeometry`,
  `EdgeGeometry`, `Start/EndNodeGeometry` for those (YB `Game.Net/GeometrySystem.cs:3378-3381`, `3403-3405`; on load it
  runs on all). PROVEN.
- Applying a tool edit keeps the original entity when the Temp copy has `m_Original` and no Replace/Delete flag
  (components copied onto it); otherwise the original gets `Deleted` (YB `Game.Tools/ApplyNetSystem.cs:390-412`).
  PROVEN.
- Cases:
  - **Arm added/removed at the node**: node `Updated`, edge `Created`/`Deleted`, `ConnectedEdge` buffer changes. Box
    polygon rebuilt from the new set of edges.
  - **Road upgraded/widened**: edge (and nodes) `Updated` with new composition; rebuild.
  - **Node moved**: node `Updated`; rebuild.
  - **Node replaced** (Delete + new node, e.g. some replace operations): box tag lost; JR must re-map: on `Deleted` of a
    boxed node, look for a `Created` node within ~1 m in the same frame and move the tag. INFERRED; probe S9.
  - **Road split next to the node** (a new junction on an arm): edges replaced, the node itself kept; rebuild.
- **For (E)**: one system ordered after `Game.Net.GeometrySystem`: query boxed nodes with `Updated`, plus boxed nodes
  whose connected edges have `Updated`/`Deleted`; rebuild their triangles into the GPU buffer. Nothing else to do.
- **For (A)**: the same trigger, then delete + re-create the area through a `CreationDefinition` (our tool active for
  2-3 frames), in a frame where the player is not mid-edit. This is the weak spot of saved paint.
- Driveways: box from the road edge's `EdgeGeometry` left/right curves cut between the building's access curve
  positions (s0..s1); triggers are the edge `Updated`/`Deleted` and the building `Deleted`.

---

## Live probe plan (owner, hand steps unless noted)

Each step: what to do, what to look at, what it decides. S1-S5 need no new code; S6-S11 need a JR debug build (later
job).

| Step | Do | Look at | Decides |
|---|---|---|---|
| S1 | Hand-draw G87 UK Junction Box over an at-grade junction | Hatch shows on asphalt; hatch angle vs road | Baseline: surfaces paint roads at grade; hatch is world-aligned (expected yes / not aligned) |
| S1b | Draw a second surface overlapping the first | Red overlap error? | Confirms Q2 overlap rule (expected error) |
| S2 | Under an elevated junction, hand-draw a G87 Junction Box covering the deck's footprint on the ground | Deck: hatch or not? Ground under: hatch? | Proves the owner's worry: surfaces cannot paint decks (expected deck clean, ground painted). If the deck IS painted, (A) becomes viable for elevated |
| S3 | With EDT, draw a G87 "RM Line Yellow 30cm" across an elevated deck | Does the line snap to deck height and show on the deck? | Netlanes at deck height by hand (Q3, U5) |
| S4 | With EDT, place a G87 road decal on a deck (NetArea snap) | Shows on the deck? | Decal boxes follow height (Q3) |
| S5 | Open TLE's tool on an elevated junction | Overlay lines sit on the deck? | (B) works on elevated nodes (expected yes) |
| S6 | (debug build) JR draws one test box with option E at: an at-grade cross, an elevated junction, a junction over a tunnel, a roundabout node | Looks like paint (day/night, rain, zoom out shimmer)? Deck painted, ground under clean? Cars drive over it unpainted? Frame time | GO/NO-GO for E; settles U2, U3, U4, U8 |
| S7 | (debug build) Skewed junction (arms ~60 deg) | Debug outline (B) and box (E) match the visible kerbs and mouths | Polygon + footway inset (U7) |
| S8 | (debug build) T junction at an angle, and a Y | Box stops at each mouth, does not run up the arms; hatch clipped clean | Hatch layout rule |
| S9 | (debug build) Divided-road junction; then upgrade one arm, move the node, add a 5th arm, delete one | Box follows each edit in the same frame; median noses outside; no stale pieces | Edit handling (section 4), re-map of replaced nodes |
| S10 | (debug build) Elevated junction with a ground junction directly below, both boxed | Each box only on its own level | Volume height choice (+-1 m), mask Roads |
| S11 | (debug build) Save, quit, turn JR off, load | No leftovers, no errors | Save safety of E (and selection storage, U6) |

## Load-bearing cites (for review)

1. YB `Game.Rendering/AreaRenderSystem.cs:221-262` - surface = "Area triangle volume" prism, drawn indirect.
2. YB `Game.Rendering/AreaBatchSystem.cs:1328-1329` - volume y = HeightRange +- offset + node y.
3. YB `Game.Areas/GeometrySystem.cs:133-151, 380-411, 413-483` - surface HeightRange = terrain minus plane.
4. YB `Game.Tools/GenerateAreasSystem.cs:305-313` + `Game.Tools/AreaToolSystem.cs:1879-1882` - nodes snap to terrain unless elevation set; game mode always snaps.
5. YB `Game.Rendering/AreaBatchSystem.cs:2075-2098` - height offset 2 x min node distance; decal layer mask on material.
6. G87 `Surfaces/Misc/G87 UK Junction Box/Material.json:4` - mask 39 (roads can receive).
7. YB `Game.Prefabs/AreaInitializeSystem.cs:322-331` + `Game.Areas/ValidationHelpers.cs:980-1021` - surfaces skip net/object validation in game mode.
8. YB `Game.Rendering/BatchDataHelpers.cs:532-547` - netlane decals bent along the 3D lane curve.
9. YB `Game.Net/RaycastJobs.cs:226-258` + `Game.Rendering/BatchDataSystem.cs:926-940` - node surface = per-edge EdgeNodeGeometry strips.
10. YB `Game.Rendering/AreaBatchSystem.cs:2083-2120` + `Game.Rendering/AreaTriangleData.cs:5-30` - recipe and data layout for option E.
