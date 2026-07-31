# Tier 0 Terrain Spike — Spike Note

> **Date**: 2026-07-25 · **Validates**: ADR-0002 Terrain Data Model (Proposed)
> **Result**: **YES — the model holds; ADR-0002 needs three corrections, none structural**

## Question

Does ADR-0002's terrain data model (chunked array-of-structures of 8-byte `TerrainCell`
behind a single `TerrainWorld` facade) hold up on memory, allocation, sweep cost, and
chunk-rebuild extraction at MVP scale — and is 32×32 the right chunk size? Plus the ADR's
routed open question: **which render backend**, GridMap or MultiMesh?

## How it was run

- **Data half** — `dotnet run -c Release` in `prototypes/terrain-spike/`. Plain .NET 8,
  workstation GC, 4 cores. Implements ADR-0002 verbatim (facade, batched events with
  `Previous`, pooled buffer, mutation-window assertion, bulk `Apply` semantics, material
  manifest, snapshot/restore).
- **Render half** — `prototypes/terrain-spike/render/`, Godot **4.7.1 mono**, Forward+,
  run under Xvfb with **lavapipe software Vulkan**. 3-layer cutaway of a 128×128 mountain
  with a carved colony. Backends: MultiMesh (three upload strategies) and GridMap
  (octant-size sweep).

## Result: YES

**38/38 contract correctness checks pass** — including the ones that would have forced an
ADR revision: `Previous`-state capture (CD-1), destroy-at-zero, repair clamp (CD-7),
bulk validate-all-then-apply with duplicate-cell rejection and no-op dropping, one batch
per multi-cell operation, mutation-window assertion firing on both forbidden write paths
(outside window, and from inside a bus handler), load publishing `WorldReloaded` with zero
per-cell events, snapshot round-trip byte-identity, id remapping when the catalog is
reordered, loud failure on unknown material keys, and identical event streams + byte-identical
state from identical mutation sequences.

### Data model — measured

| Measure | Result | ADR-0002 said | Verdict |
|---|---|---|---|
| `sizeof(TerrainCell)` | **8 bytes** | 8 bytes (contract) | ✅ |
| Memory, MVP 128×128×16 | **2.00 MB** | "~2 MB" | ✅ exact |
| Memory, full-vision 256×256×32 | **16.00 MB** | "16 MB" | ✅ exact |
| Chunk on LOH? | **No** at 16/32/64 (2/8/32 KB) | off-LOH by construction | ✅ |
| Steady-state allocation, dig-heavy | **0.17 B/mutation, 0 Gen0** over 60k mutations | "zero steady-state allocation" | ✅ |
| Per mutation + publish | **0.338 µs** | — | — |
| Realistic frame (10 colonists, 1 dig each) | **0.0101 ms = 0.06%** of 16.6 ms | — | ✅ |
| Full-map walkability sweep (worst case), chunk=32 | **0.290 ms = 1.7%** of frame | flagged as the falsification risk | ✅ **not falsified** |
| Snapshot, MVP | **0.61 ms, 2.01 MB one-shot**; Restore 0.99 ms | strategy deferred to spike | ✅ answered |
| Snapshot, full-vision | 8.90 ms / 16.06 MB; Restore 7.12 ms | — | acceptable at a mode-switch |
| Bulk `Apply` | 2→0.67 µs · 16→7.78 · 64→32.0 · 256→69.9 µs | — | ✅ |
| Chunk extraction (cs=32) | 1 chunk **1.10 µs**; full layer **0.049 ms = 0.3%** frame | — | ✅ |

### The AoS falsification test — falsified in the *good* direction

ADR-0002 explicitly conceded: *"A hot/cold field split (SoA) would roughly double cache
density for those sweeps — that is a real cost we are accepting."* **Measurement says the
concession was unnecessary.** Chunked AoS is **21–46% FASTER** than a flat two-plane SoA
sweep:

| chunk | AoS sweep | SoA (hot fields) | AoS vs SoA |
|---|---|---|---|
| 16 | 0.411 ms | 0.519 ms | **21% faster** |
| 32 | **0.290 ms** | 0.517 ms | **44% faster** |
| 64 | 0.282 ms | 0.520 ms | **46% faster** |

Chunked AoS wins because each 8 KB chunk is one sequential L1/L2-resident stream, while the
SoA variant walks two 512 KB planes with per-access bounds checks. *Caveat: the SoA arm is a
straightforward two-array scan, not a hand-optimised/vectorised SoA — a tuned SoA could close
the gap. The decision-relevant conclusion is unaffected: at 1.7% of a frame for a
whole-world worst-case sweep, no layout change is warranted, and the pre-planned hot/cold
fallback can be retired from the risk list.*

### Chunk size: 32 confirmed

| chunk | sweep | 1-chunk rebuild | chunks (MVP) | note |
|---|---|---|---|---|
| 16 | 0.411 ms | 0.31 µs | 1024 | cheapest rebuild, worst sweep, 4× bookkeeping |
| **32** | **0.290 ms** | 1.10 µs | 256 | **best balance — adopt** |
| 64 | 0.282 ms | 4.79 µs | 64 | sweep plateaus; 4× rebuild waste per dig |

Sweep gains plateau after 32; rebuild cost grows with chunk area. **32×32 stands as specified.**

### Render backend — GridMap wins decisively (the ADR's open question, answered)

3-layer cutaway, 128×128, 589,824 primitives, **14.25 MB video memory (identical across all
backends** — geometry, not backend, sets it).

| Backend | Draw calls | Initial build | **Per-dig update** | Nodes |
|---|---|---|---|---|
| MultiMesh, per-instance `SetInstanceTransform` | 82 | 521 ms | 502 µs | 82 |
| MultiMesh, bulk `Buffer` upload | 82 | 70 ms | 524 µs | 82 |
| MultiMesh, pooled scratch + bulk `Buffer` | 82 | 49 ms | 452 µs | 82 |
| GridMap, octant 4 | 1233 | 36 ms | 1.32 µs | 1 |
| GridMap, octant 8 (default) | 343 | 36 ms | 1.94 µs | 1 |
| GridMap, octant 16 | 108 | 32 ms | 1.80 µs | 1 |
| GridMap, octant 32 | 32 | 33 ms | 1.85 µs | 1 |
| **TWO GridMaps (floor + wall), octant 32** | **32** | **39–42 ms** | **~2 µs** (median of 5; 1.75–9.6 range) | **2** |
| TWO GridMaps (floor + wall), octant 64 | 8 | 38 ms | ~2 µs | 2 |
| TWO GridMaps (floor + wall), octant 16 | 128 | 124 ms | 1.65 µs | 2 |

**GridMap at octant 32 beats MultiMesh on both axes at once**: 32 draw calls vs 82 (2.6×
fewer) *and* ~1.9 µs vs ~452 µs per dig (**~240× cheaper**). MultiMesh's cost is structural,
not an implementation flaw — a one-cell change rewrites the whole chunk's ~2000-instance
buffer, and neither bulk upload nor pooling fixes that (they only sped up the initial build,
521→49 ms). GridMap updates one octant in place. At 10 concurrent diggers, MultiMesh costs
~4.5 ms/frame (27% of budget) against GridMap's ~0.02 ms.

*This does not make GridMap authoritative.* `TerrainWorld` remained the single source of
truth throughout; GridMap was a pure write target — exactly the "render backend reading from
the model" role ADR-0002 permits. The forbidden pattern (GridMap as authoritative state)
is untouched.

## What to do next

1. **Adopt GridMap as the MVP render backend**, `cell_octant_size = 32`, in the Terrain
   Rendering & Cutaway quick-spec. Retire the MultiMesh option unless a later requirement
   (per-instance custom data for per-cell shader variation) forces it back.
2. **Floor + wall in the same cell — RESOLVED (2026-07-26): two stacked GridMaps.**
3. **Three ADR-0002 corrections** (recorded in the ADR's Spike Results section): retire the
   AoS/SoA concession and its risk row; fix chunk size at 32; adopt one-shot snapshot
   allocation (no buffer-reuse machinery — 0.61 ms / 2 MB at a non-gameplay moment).
4. **Fill the `[TO BE CONFIGURED]` budgets** in technical-preferences from these numbers.

## Addendum (2026-07-26): the floor+wall problem, solved

The first pass flagged a blocker: a single GridMap stores **one item id per cell**, so it
cannot show a floor *and* a wall in the same cell. Since a floor-and-wall-per-cell grid is the
Gnomoria-style world model this whole game rests on (concept doc Core Mechanic 1), a backend
that cannot express it is disqualified regardless of its draw-call numbers.

**Important scope note: this was never a data-model defect.** `TerrainCell` has always carried
`FloorTypeId` and `WallTypeId` as separate fields, and every correctness check passed. The
limitation was entirely in the chosen render backend — which is exactly the separation ADR-0002's
"GridMap is at most a render backend" rule exists to protect. The model needed no change.

**Fix: two stacked GridMaps** — a wall map (full-cube items, three material tiers) and a floor
map (thin slab items offset to the bottom of the cell via `SetItemMeshTransform`), sharing one
coordinate system, cell size, and octant size. Both are pure write targets fed from
`TerrainWorld`.

**Measured (3-layer cutaway, 128×128, octant 32):**

| | single GridMap | **two GridMaps** |
|---|---|---|
| Floor + wall in one cell | **impossible** | **yes** |
| Draw calls | 32 | **32 (unchanged)** |
| Primitives | 589,824 | 1,157,796 |
| Video memory | 14.25 MB | 16.42 MB (+2.17) |
| Initial build | 33 ms | 39–42 ms |
| Per-dig update | 1.85 µs | ~2 µs |
| Render nodes | 1 | 2 |

**Direct capability proof** (asserted in-engine, not inferred): on the top visible layer,
**15,763 cells carry both a floor and a wall in the render, and that count matches
`TerrainWorld` exactly** (`render_matches_model=True`), alongside 621 floor-only cells (the
carved rooms) and 0 wall-only cells. A low-angle screenshot shows the granite layer with the
floor-slab band of the layer beneath visible in cross-section — the geological-cross-section
read the art bible calls for, which the single-GridMap backend could not have produced.

**Cost of the fix: effectively nothing.** Draw calls are identical, video memory rises 2.17 MB,
per-dig cost is unchanged, and the dig update actually gets *simpler* — clearing a wall is one
`SetCellItem(pos, -1)` because the floor is already present, instead of swapping a wall item for
a floor item.

**Octant recommendation: 32, and it is a LOCKED INVARIANT — not a preference.**
`cell_octant_size` **must equal `TerrainWorld.ChunkSize`** (both 32). At that equality a dirtied
chunk maps 1:1 to a dirtied octant. *Correction (2026-07-26, godot-specialist review): an earlier
version of this note suggested moving to octant 64 "if draw calls ever become the binding
constraint." That is wrong and is withdrawn* — at octant 64 one octant spans four chunks, so a
single-chunk batch would force a rebuild covering three untouched chunks. If draw calls ever
become binding, reduce style variety (below) or revisit chunk size and octant size **together**,
never octant alone.

### Follow-up measurements (2026-07-26) — two unknowns closed

The godot-specialist review flagged two claims as unproven. Both were measured rather than left
as recommendations.

**1. Style variety, not MeshLibrary size, drives draw calls — confirmed, with a ceiling.**
Draw calls scale with how many distinct material/style combos co-occur *in an octant*:

| Style variants per tier | 1 | 2 | 4 | 8 | 16 |
|---|---|---|---|---|---|
| Draw calls (3-layer cutaway, octant 32) | 32 | 48 | 80 | **144** | 272 |

Video memory is flat across all of these (16.42–16.43 MB) — this is a batching effect, not a
memory one. Against the ≤150 terrain draw-call budget, the **practical ceiling is ~8 style
variants per tier**. Record as a hard tuning constraint; the original 32-draw-call figure assumed
a single style per tier and must not be read as headroom for unlimited visual variety.

**2. Concentrated combat AoE is cheap — "one batch is free" now proven, not extrapolated.**
The per-dig figure came from *scattered* single-cell changes; an AoE is spatially concentrated,
lands in one or two octants in a single frame, and fires during TurnBased where a hitch is most
visible. Measured, all cells inside one octant:

| Cells changed in one frame | 8 | 27 | 48 | 75 |
|---|---|---|---|---|
| Total | 14.3 µs | 16.2 µs | 17.2 µs | **21.5 µs** |
| Per cell | 1.79 µs | 0.60 µs | 0.36 µs | **0.29 µs** |

Cost is **sublinear** — the octant rebuild amortises across the batch, so concentrated damage is
cheaper per cell than scattered digging. A 75-cell AoE costs 0.13% of a frame.

**This generalises.** Any future per-cell visual layer (ore veins, floor coverings, ceiling
detail) is another stacked GridMap at ~2 MB of video memory, not a redesign. The rejected
alternative — one GridMap with a MeshLibrary item per (floor, wall) pair — dies on combinatorics
once `StyleId` multiplies in.

## What this spike did NOT answer

- **60 fps on real hardware.** Software Vulkan (lavapipe) ran at 3–4 fps — meaningless as a
  frame-rate signal. Draw calls, video memory, primitive counts, and CPU-side update costs
  *are* hardware-independent and are what this note reports. **ADR-0002 validation criterion 5's
  frame-rate clause remains open and must be re-run on target hardware before promotion to
  Accepted.**
- **Physics/collision cost** — GridMap collision shapes were disabled; the tile grid is not
  physics-driven (per technical-preferences), but confirm when doors/units land.
- **Cutaway Z-level transitions** beyond the full-layer extraction cost (0.049 ms CPU-side).
- **Procgen-era sparse chunk storage** — MVP dense grid only, as scoped.

## Status

Concluded. ADR-0002 is **validated on every criterion this environment can measure (5 of 6)**;
the frame-rate criterion is the sole remaining gate before it is promoted from Proposed to
Accepted. No structural change to the ADR is required.
