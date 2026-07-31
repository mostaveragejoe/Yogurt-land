# Tier 0 Pathfinding Spike — Spike Note

> **Date**: 2026-07-26 · **Validates**: ADR-0003 validation criterion 5 (mode-aware composite
> walkability) + ADR-0002's stair Z-linkage + the concept doc's open question
> *"Can pathfinding stay correct and performant while the player digs mid-route?"*
> **Result**: **YES on correctness (36/36). One real performance constraint found: the region
> flood fill costs 19.9% of a frame and must never run per dig.**

## How it was run

`dotnet run -c Release` in `prototypes/pathfinding-spike/`. Plain .NET 8, no engine. Grid A*
over the ADR-0002 terrain model (reused, including the corrected struct `MutationWindow`), with
composite walkability composed from terrain + `DoorStore` + `UnitOccupancyIndex` exactly as
ADR-0003 specifies. Benchmarks at MVP scale: 128×128×16 = 262,144 cells, a carved colony of
rooms + trunk corridors + a stair shaft, 119 regions.

## Correctness: 36/36

**Mode-aware composite walkability — ADR-0003's rule, verified in both directions:**

| Case | RealTime | TurnBased |
|---|---|---|
| Closed door | **passable** (colonists auto-open in transit, +1 step cost) | **blocks** (opening is an action) |
| Open door | passable | passable |
| Broken door | passable | **unblocks IMMEDIATELY — the breach lands the same turn** |
| Cell occupied by another unit | **does not block** (advisory — no colony traffic deadlock) | **blocks** (tactics legality) |
| Cell occupied by self | never blocks | never blocks |

Closed unbroken doors also block LOS, and stop doing so once broken — the same read Spatial
Query needs.

**Stair Z-linkage (ADR-0002)**: layers are disconnected until a stair floor exists; a stair
Z-links its cell to the layer below (Z increases downward) and carries movement in both
directions. Verified that descent paths actually traverse the stair cell.

**Mid-route digs — the concept doc's question, answered directly:**

- No world change → **polling costs nothing** (revision equality short-circuits).
- Off-route dig → **one revalidation, zero recomputes**, path untouched.
- Build **on the remaining route** → exactly one recompute, and the new path routes *around* the
  new wall while still reaching the goal.
- Change **behind the colonist's cursor** → **no invalidation**. Only the remaining route matters.
- Sealing the only passage → **empty path, not a stale one**. Failure is explicit, not corruption.

**Determinism**: identical queries return identical paths (index-tiebroken heap), and a fresh
world built from the same inputs produces the same path.

## Cost (MVP scale)

| Measure | Result | Frame budget |
|---|---|---|
| A* short path (11 cells) | 8.1 µs | 0.05% |
| A* medium (111 cells) | 87.3 µs | 0.53% |
| A* long, corner to corner (126 cells) | 91.4 µs | 0.55% |
| A* descent via stairs (5 layers) | 10.0 µs | 0.06% |
| **10 colonists ALL repathing long routes in one frame** | 0.651 ms | **3.9%** |
| Dig + poll/revalidate 10 cached paths | 63.2 µs per dig | 0.38% |
| **FULL region flood fill (whole world)** | **3.30 ms** | **19.9%** |
| A* allocation | **0.00 B/query** over 5,000 queries, 0 Gen0 | — |

## The one real constraint: region rebuild must not run per dig

A full connectivity flood fill over all 262k cells costs **3.30 ms — one fifth of the entire
frame budget**. Digging is the game's core verb and happens constantly, so a naive
"terrain changed → rebuild regions" would spend 20% of every frame recomputing reachability.
**This is the spike's load-bearing finding.** Mitigations for the Pathfinding quick-spec, in
order of preference:

1. **Incremental region updates** — a dig merges or splits at most a local neighbourhood; only a
   full *split* is expensive, and splits are rare compared to merges.
2. **Deferred/amortised rebuild** — mark stale on change (already implemented and tested via
   `Revision`), rebuild at most once every N ticks or when a reachability query actually misses.
3. **Per-layer regions** — chunk the flood fill by Z so a dig only rebuilds its own layer
   (~0.2 ms), matching ADR-0002's per-layer chunking.

The index itself is correct and already Revision-staleness-aware; only the *rebuild trigger*
needs design. Note this cost is **not** on the A* path — pathfinding without the region index
is comfortably in budget; the index is an optimisation for answering "is this even reachable?"
in O(1), and it must not cost more than it saves.

## Revision polling: affordable, with a stated scaling limit

ADR-0003 chose Revision polling over an entity event bus and pre-planned a "narrow change list"
upgrade if a spike falsified the rescan. **It did not**: 63.2 µs per real dig (0.38% of a frame)
with 10 cached paths of ~126 cells. But the honest shape of the cost is
**O(cached paths × remaining path length) per terrain mutation**, because polling knows only
*that* the world changed, never *which cell* — so every live path rescans on every mutation.
At MVP caps (10 colonists, ~126-cell paths) that is fine. **The change-list upgrade trigger is
concrete**: if colonist count, path length, or simultaneous dig rate rise by ~5× the rescan
approaches 2% of frame per dig and the narrow change list (which would let a path skip the
rescan entirely when the changed cell is not on it) becomes worth its complexity.

## Routed to the Pathfinding quick-spec

- **Diagonal movement is NOT implemented — movement is 4-connected.** This is a real gameplay
  decision the spike surfaced concretely: two rooms touching only corner-to-corner are *not*
  connected, which affects level design, corridor feel, and path length (Manhattan, not
  Euclidean). Decide diagonals — and if adopted, whether corner-cutting past wall corners is
  legal — in the quick-spec. Cost impact is roughly 2× neighbours per expansion.
- **Door step cost** is a placeholder (+1 over a normal step) representing open-in-transit time;
  tune with the Construction/Doors spec.
- **Whether TurnBased occupancy blocks traversal or only end-of-move** (XCOM permits move-through-ally)
  is still a Combat: Movement & Reachability decision, as ADR-0003 says. The spike implements
  hard blocking; the index answers "occupied by whom" either way.
- **Region rebuild trigger policy** — the constraint above.

## What this spike did NOT answer

- **Flow-field or hierarchical pathfinding** for larger colonies — unnecessary at MVP caps, and
  premature before the region-rebuild policy is chosen.
- **Job-to-colonist assignment cost** (which colonist takes which job) — that is Job Assignment's
  problem, not pathfinding's.
- **Path following / movement interpolation** — presentation, not simulation.
- **Combat reachability ranges** (AP-limited move sets) — Combat GDD-era, though the same A* and
  the TurnBased walkability mode serve it directly.

## Status

Concluded. **ADR-0003 criterion 5 passes**, ADR-0002's stair rule is validated, and the concept
doc's mid-route-dig question is answered YES. The region-rebuild trigger is a design task for the
Pathfinding quick-spec, not an ADR revision — no contract changed.
