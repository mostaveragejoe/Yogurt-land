# Godot GridMap / MeshLibrary — Quick Reference

Last verified: 2026-08-25 | Engine: **Godot 4.7.1** (pinned)

**Verification method**: read directly from the `4.7.1-stable` tag of `godotengine/godot`
(`version.py` confirms `major=4 minor=7 patch=1 status=stable`), not from training data and
not from the docs website. Sources are the in-repo class references, which are the same text
the website renders:

- `modules/gridmap/doc_classes/GridMap.xml` @ `4.7.1-stable`
- `doc/classes/MeshLibrary.xml` @ `4.7.1-stable`
- `doc/classes/Light3D.xml`, `doc/classes/AreaLight3D.xml` @ `4.7.1-stable`

> **Why this file exists.** `architecture-review-2026-08-08.md` lists *"Author
> `docs/engine-reference/godot/modules/gridmap.md` (version-pinned) before render-backend
> work"* as an open engine-verification gate, and records that `/story-readiness` returns
> **BLOCKED** for render-backend stories until it exists. This file closes that gate.

---

## Headline: the knowledge-gap risk here is LOWER than the project assumed

`VERSION.md` classes 4.7 as **HIGH RISK** globally, and ADR-0002 inherits that rating for the
render backend. For **GridMap specifically that is now measurably wrong**, and the correction
is worth having because it changes how much suspicion render-backend code deserves.

Diffing the GridMap class reference across `4.3-stable → 4.4 → 4.5 → 4.6 → 4.7.1-stable`:

| Change class | Count | Detail |
|---|---|---|
| Methods **removed** | **0** | Nothing the model knows from 4.3 has disappeared |
| Existing method **signatures changed** | **0** | Verified field-by-field, 4.3 vs 4.7.1 |
| Members removed / retyped | **0** | One member *added* (`collision_visibility_mode`) |
| Methods **added** | **7** | All in 4.7, all octant queries (below) |

**Assessment: GridMap is LOW risk, additive-only.** The 4.3-era API in the model's training
data is intact and safe to rely on. The only genuinely post-cutoff surface is the seven new
octant-query methods — and being *new*, they are opt-in: nothing breaks by not knowing them.

This does **not** downgrade 4.7's global HIGH rating (rendering, animation, audio, input and
shader-preprocessor changes listed in `breaking-changes.md` are unaffected by this finding).
It downgrades **GridMap's** rating only.

---

## New in 4.7 — the seven octant-query methods

These did not exist in 4.6 or earlier. They are the only post-cutoff GridMap API.

```
Vector3i    get_octant_coords_from_cell_coords(Vector3i cell_coords)  const
Vector3i[]  get_octants_in_bounds(AABB bounds)                        const
Vector3i[]  get_used_octants()                                        const
Vector3i[]  get_used_octants_by_item(int item)                        const
Vector3i[]  get_used_octants_in_bounds(AABB bounds)                   const
Vector3i[]  get_used_cells_in_octant(Vector3i octant_coords)          const
Vector3i[]  get_used_cells_in_octant_by_item(Vector3i octant_coords, int item)  const
```

Exact doc text for the two that matter most here:

- `get_octant_coords_from_cell_coords` — *"Returns the Vector3i octant coordinates of the
  octant that the cell at [cell_coords] belongs to."*
- `get_octants_in_bounds` — *"Returns an array of Vector3i octant coordinates that are inside
  the given [bounds], **including octants that have no cells in use**."*

### Relevance to Hollowdeep, stated carefully

The render backend (ADR-0002; `design/quick-specs/terrain-rendering-cutaway.md` C2, C8)
rests on a **chunk ↔ octant 1:1 mapping** at size 32, and C8 rebuilds dirty octants once per
frame. `get_octant_coords_from_cell_coords` is the engine's own answer to
"which octant is this cell in", which is the render-side half of that mapping.

**This is an option, not a mandate, and it must not be misread as licence to break a
locked rule.** `technical-preferences.md` forbids **caller-side chunk math**:
`TerrainWorld.ChunkOf()` / `ChunkSize` are the only sanctioned `CellCoord → chunk` mapping.
That rule governs the **simulation** side and is untouched — `TerrainWorld` is plain C# and
cannot call a Godot node. The engine method is available only to the **view** assembly, and
only for the render layer's own bookkeeping.

If the view layer ever uses it, the AC-3 startup assertion
(`cell_octant_size == TerrainWorld.ChunkSize`) becomes *more* load-bearing, not less: the two
mappings agree only while the sizes are locked equal. Treat it as a convenience for
enumerating dirty octants, never as a second source of truth for chunk identity.

---

## Confirmed: GridMap has NO per-instance data channel

The entire per-cell write surface, **unchanged since 4.3**:

```
void set_cell_item(Vector3i position, int item, int orientation = 0)
```

*"Sets the mesh index for the cell referenced by its grid coordinates. A negative item index
such as [-1] will clear the cell. Optionally, the item's orientation can be passed."*

Three parameters: **position, item id, orientation.** There is no per-cell colour, no
per-cell custom data, no per-cell shader/instance uniform, and no per-instance override of
any kind — confirmed by exhaustive enumeration of the class's 30 methods and 11 members, not
by inference.

**Consequences, all of which the project already reached and which are now verified rather
than assumed:**

1. **Any per-cell visual variation must be a distinct `MeshLibrary` item id.** This is the
   mechanism behind the measured style-variety draw-call curve (1 variant → 32 draw calls,
   2 → 48, 4 → 80, 8 → 144, 16 → 272 against a ≤150 terrain budget).
2. **Damage cannot ride on GridMap.** A damage tier would need a distinct mesh item *per
   material × style combo*, multiplying against the ~8-variants-per-tier ceiling. This is
   exactly `architecture-review-2026-08-08.md` finding 2, and it is **correct**.
3. **The sparse-overlay decision is validated.**
   `terrain-rendering-cutaway.md` C7 (one `MultiMeshInstance3D` per damage state, instanced
   only on damaged cells, plus rubble) is the right shape *because* of this API limit —
   and ADR-0002's Amendment 2026-08-24 recording that supersession is well-founded.
   The draw-call **measurement** (AC-10 / TR-terrain-044) is still owed; the **design** is
   engine-verified.

---

## MeshLibrary: `set_item_mesh_transform` is PER-ITEM, not per-cell

```
void set_item_mesh_transform(int id, Transform3D mesh_transform)
```

*"Sets the transform to apply to the item's mesh."* — keyed by **library item `id`**.

ADR-0002 relies on this for the floor map ("thin slab items offset to the cell bottom via
`MeshLibrary.SetItemMeshTransform` — a per-*library-item* transform, not a per-placed-cell
call"). **Confirmed at 4.7.1.** This also discharges `architecture-review-2026-08-08.md`
finding 4 (the "minor — ADR-0002 text precision" item asking that a future reader cannot
misread it as a per-instance runtime override): it is not one, and it cannot become one,
because per-instance overrides do not exist (previous section).

Full MeshLibrary surface is 25 methods, all `id`-keyed (`set_item_mesh`, `set_item_shapes`,
`set_item_navigation_mesh`, `set_item_mesh_cast_shadow`, …). **There is no per-placed-cell
call anywhere in the class.**

---

## Note — GridMap is not a `VisualInstance3D`

Quoted verbatim from the 4.7.1 class description:

> *"**Note:** GridMap doesn't extend `VisualInstance3D` and therefore can't be hidden or cull
> masked based on [layers]. If you make a light not affect the first layer, the whole GridMap
> won't be lit by the light in question."*

In plain terms: the terrain is one object as far as hiding and per-light filtering go. It
affects exactly one thing in practice — how the cutaway's depth cue is built.

### The cutaway's depth cue (`terrain-rendering-cutaway.md` C3/C4)

**The window itself is settled and measured.** `prototypes/terrain-spike/render/RenderBench.cs`
only writes cells inside the window into the maps (`VisibleLayers = 3, TopLayer = 6`; every
population loop runs `for (int z = TopLayer; z < TopLayer + VisibleLayers; z++)`, lines 158,
292, 323). Layers above the focus simply aren't in the map. That is the measured 32-draw-call
configuration and it works.

**The depth cue on lower layers is a build-time tuning job, not a blocker.** The intent is a
*slight* de-emphasis — lower layers stay clearly visible and readable, just less prominent
than the layer the player is working on. The spike didn't implement it, so the exact treatment
and strength are still to be dialled in.

C4's current numbers (`DepthDimStep 0.35`, `DepthDesaturateStep 0.20`) are first-pass
placeholders and are already declared tunable in the quick-spec's §6 knob table. **Adjust them
during implementation against how it actually looks** — that is the intended workflow, and the
existing ranges (0.15–0.6 and 0–0.5) exist precisely so this gets tuned by eye rather than
argued in advance. If the current defaults read as too strong, lower them; the spec's wording
("progressive darkening toward the ambient floor") should be read as *de-emphasis*, not
*fade to obscurity*.

The one implementation note worth carrying: because there is no per-cell channel, a per-depth
tint comes from a shader reading world height rather than from distinct meshes per depth. That
keeps it off the draw-call budget. It is a normal shader task, not a design question.

---

## Reference: full 4.7.1 surface

### Members (11)

| Member | Type | Default | Note |
|---|---|---|---|
| `cell_size` | `Vector3` | `Vector3(2, 2, 2)` | |
| `cell_octant_size` | `int` | **8** | Project pins **32** (locked == `TerrainWorld.ChunkSize`) |
| `cell_scale` | `float` | `1.0` | |
| `cell_center_x` / `_y` / `_z` | `bool` | `true` | |
| `collision_layer` / `collision_mask` | `int` | `1` | Terrain is not physics-driven; shapes disabled |
| `collision_priority` | `float` | `1.0` | |
| `collision_visibility_mode` | `int` | `0` | The only member added since 4.3 |
| `bake_navigation` | `bool` | `false` | Project uses its own A*, not `NavigationServer` |

> **Naming gotcha (real, and easy to lose an hour to):** the property is `cell_octant_size`
> but its accessors are `set_octant_size` / `get_octant_size` — the `cell_` prefix is **not**
> in the method names. In C#: property `CellOctantSize`, methods `SetOctantSize()` /
> `GetOctantSize()`.

`cell_octant_size` doc text is exactly: *"The size of each octant measured in number of
cells. This applies to all three axis."* Note it is **cubic** — an octant at 32 spans 32
cells in Z as well, while `TerrainWorld` chunks are per-layer `32×32×1`. The 1:1
chunk↔octant claim therefore holds **per layer**, which is how the two stacked maps are
built; it is not a 3D volume correspondence.

### Methods (30 = 23 pre-4.7 + 7 new)

Cells: `set_cell_item`, `get_cell_item`, `get_cell_item_basis`, `get_cell_item_orientation`,
`get_used_cells`, `get_used_cells_by_item`, `clear`, `local_to_map`, `map_to_local`.
Orientation: `get_basis_with_orthogonal_index`, `get_orthogonal_index_from_basis`.
Baking: `make_baked_meshes(bool gen_lightmap_uv=false, float lightmap_uv_texel_size=0.1)`,
`clear_baked_meshes`, `get_bake_meshes`, `get_bake_mesh_instance`, `get_meshes`.
Collision/nav: `set_collision_layer_value`, `get_collision_layer_value`,
`set_collision_mask_value`, `get_collision_mask_value`, `set_navigation_map`,
`get_navigation_map`. Misc: `resource_changed`.
Octants (**4.7-new**): the seven listed above.

---

## Open items this file does NOT close

- **TR-terrain-044 / AC-10 — the damage-overlay draw-call measurement.** The *design* is now
  engine-verified; the *number* is still unmeasured.
- **C4's depth cue is unimplemented** — a build-time tuning task (shader reading world height),
  not a blocker. The cutaway *window* is settled and spike-measured.
- **Octant rebake granularity under `set_cell_item`.** The class docs state the octant split
  exists "for efficient rendering and physics processing" but do not specify rebuild
  granularity as a contract. The project's 2026-07-25/26 spike measured it empirically
  (~1.85 µs per dig at octant 32) — that measurement remains the authority; it is not a
  documented guarantee, so re-measure if the engine is upgraded.
