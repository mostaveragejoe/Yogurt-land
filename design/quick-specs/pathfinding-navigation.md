# Pathfinding & Navigation — Quick-Spec

| Field | Value |
|-------|-------|
| **Systems index** | #8 (enumeration) / #10 (design order) — Core, MVP |
| **Doc tier** | Quick-spec (spike + ADR carry it), per the PR-SCOPE routing policy |
| **Status** | Drafted 2026-08-20 |
| **Governing ADRs** | ADR-0002 (terrain passability, stair Z-linkage, chunk mapping), ADR-0003 (composite walkability, `DoorStore`, `UnitOccupancyIndex`, Revision polling), ADR-0001 (dispatch + mutation window), ADR-0005 (determinism / no stock RNG) |
| **Source evidence** | Tier 0 pathfinding spike, 2026-07-26 — 44/44, `prototypes/pathfinding-spike/SPIKE-NOTE.md` |
| **Depends on** | Terrain Data Model (#1, Approved), World Change Event Bus (#3) |

---

## 1. Purpose

Answer two questions, deterministically and inside frame budget, while the player is
digging: **"what is the route from A to B?"** and **"is B reachable at all?"**

Pathfinding owns the movement model, the composite-walkability composition, and the
region/connectivity index. It does **not** own movement execution, job selection, or
combat action economy.

**Explicitly out of scope** (named so they cannot drift in):

| Not owned here | Owner |
|---|---|
| Path following, interpolation, animation | Presentation — not simulation |
| Which colonist takes which job | Job Assignment (#10) |
| AP-limited combat move sets | Combat: Movement & Reachability (#21) |
| Whether TurnBased occupancy blocks *traversal* or only *end-of-move* | Combat: Movement & Reachability (#21) — ADR-0003 assigns it there; this spec serves both readings |
| Door interaction rules (bashing, breaching, opening as an action) | Combat set (#19–#23) / Construction (#16) |
| Flow-field or hierarchical pathfinding | Post-MVP — trigger named in §8 |

---

## 2. Core Rules

### C1 — Movement model (decided 2026-07-26, user)

**8-connected, corner-cutting banned, integer octile costs, octile heuristic.**

1. Eight neighbours per cell in-layer, plus stair Z-links.
2. **A diagonal step is legal only when both shared orthogonal neighbours are walkable.**
   This is what preserves chokepoint play: a one-cell-thick diagonal wall seals, and
   corner-touching rooms stay disconnected. Verified, not assumed.
3. Step costs are integers: **orthogonal 10, diagonal 14**, stair (vertical) **10**.
4. The heuristic is **octile distance**, charging vertical distance at the orthogonal rate.

> **C1 is a correctness pair, not a balance knob.** Scaled Manhattan overestimates true
> cost on an 8-connected grid (charging 20 for a step that costs 14), which makes the
> heuristic inadmissible and silently costs A* its optimality guarantee — it still returns
> *a* path, just not always the best one, and never loudly. The 10/14 costs and the octile
> heuristic must change together or not at all (§7).

### C2 — Composite walkability (composed here, per ADR-0003's delegation)

Terrain contributes `IsPassableTerrain(c)` (ADR-0002 Formula D). Doors contribute
`DoorStore.BlocksMovement(c)`. Occupancy contributes `UnitOccupancyIndex`. Pathfinding
composes them, and **the composition differs by time authority** (§4). A unit never blocks
itself.

### C3 — Region index: per-layer, stair-portalled, terrain-only

1. Connectivity regions are computed **per Z-layer**. Stair cells act as **portals**
   linking a layer's region to the layer below; the cross-layer portal graph is tiny
   (one node per stair) and rebuilt with its layers.
2. The index is composed from **terrain only** — it deliberately ignores doors and
   occupancy. A closed door does not split a region, because a colonist opens it in
   transit; door toggles are frequent and would otherwise dirty the index constantly.
   Consequence, stated rather than hidden: **under TurnBased the index over-reports
   reachability** (closed doors block there), so it must never be used for combat legality.
3. A terrain mutation marks **only its own layer** stale (`MarkLayerDirty(z)`), via the
   existing `Revision` staleness mechanism.

### C4 — Rebuild trigger: lazy, capped, never inside a dig

1. A stale layer is **never** rebuilt inside the mutation window or as a consequence of the
   dig itself. Rebuild is triggered **lazily, by the first reachability query that reads a
   stale layer**.
2. **At most one layer rebuild per dispatch** (`RegionRebuildsPerDispatch`, §7).
3. If a query reads a stale layer and the rebuild budget is already spent, the query
   **degrades to authoritative A\*** — slower, never wrong.

> **Why this shape.** A full-world flood fill costs **4.16 ms = 25.1% of a frame**, and
> digging is the game's core verb; a naive "terrain changed → rebuild" would spend a
> quarter of every frame recomputing reachability. Per-layer granularity cuts a rebuild to
> **~0.26 ms (1.6%)**, and laziness means quiet layers cost nothing at all. Incremental
> merge/split updates were considered and deferred: splits are the hard case and a wrong
> split silently reports reachable cells as unreachable — the exact correctness sink the
> systems index flags this system for.

### C5 — The index is exact, so laziness is determinism-safe

`IsReachable` returns the same boolean whether it was answered from a fresh index or by
A\* fallback. Rebuild *timing* therefore cannot influence any simulation decision, and
cannot desync a replay: it changes wall-clock cost only, and under ADR-0001's fixed-dt
sub-stepping wall clock is not a simulation input. **The index is an accelerator, never an
oracle with its own opinion.**

### C6 — Paths are caller-held derived state

1. Pathfinding is a **stateless query service** owning only the region index. It holds no
   entity-keyed state and needs no row in ADR-0003's write-ownership table.
2. The requesting system (Colonist Movement; later Combat: Movement & Reachability) holds
   its own path and revalidates it by polling `TerrainWorld.Revision` and
   `DoorStore.Revision` at its own cadence.
3. **Paths are never serialized.** They are derived state, recomputed on load — the same
   rule the save/load spike proved for the occupancy index and entity directory, which
   contribute 0 bytes.

### C7 — Revalidation semantics (the mid-route-dig answer)

| Event | Result |
|---|---|
| No world change | Revision equality short-circuits — polling costs nothing |
| Change **off** the remaining route | 1 revalidation, **0 recomputes**; path untouched |
| Change **on** the remaining route | Exactly **1 recompute**, routing around the new obstacle |
| Change **behind** the mover | **No invalidation** — only the remaining route matters |
| Goal sealed off | `PathStatus.Unreachable` and an **empty path — never a stale one** |

### C8 — Determinism

Identical queries return identical paths (index-tiebroken binary heap), and a world rebuilt
from identical inputs produces identical paths. Multi-occupant occupancy results are
returned in **ascending `EntityId`** order. **Pathfinding draws no random numbers at all** —
it takes no `SeededRngStore` handle, which makes ADR-0005's CI-grep gate trivially satisfied
for this namespace.

---

## 3. Public Interface

Plain C#, `Hollowdeep.Core.Navigation`, zero Godot references. Caller-supplied buffers keep
queries allocation-free (the same caller-buffer obligation pattern ADR-0004 uses for
`SnapshotInto`).

```csharp
public enum WalkabilityMode { RealTime, TurnBased }

public enum PathStatus { Found, Unreachable, GoalNotWalkable, BufferTooSmall }

public readonly struct PathResult
{
    public PathStatus Status { get; }
    public int Length    { get; }   // cells written into the caller's buffer
    public int TotalCost { get; }   // octile integer cost
}

/// Composite walkability — read-only, allocation-free, mode-aware.
public interface IWalkabilityView
{
    bool IsWalkable(CellCoord c, EntityId mover, WalkabilityMode mode);
    bool IsStepLegal(CellCoord from, CellCoord to, EntityId mover, WalkabilityMode mode);
    int  StepCost(CellCoord from, CellCoord to, WalkabilityMode mode);
}

public interface IPathfinder
{
    PathResult FindPath(CellCoord from, CellCoord to, EntityId mover,
                        WalkabilityMode mode, Span<CellCoord> buffer);

    /// True iff every remaining step is still legal under `mode`.
    bool IsPathStillValid(ReadOnlySpan<CellCoord> remaining, EntityId mover,
                          WalkabilityMode mode);
}

/// Terrain-only connectivity. Exact (C5). O(1) when fresh; may rebuild one layer
/// or degrade to A* when stale (C4).
public interface IReachabilityIndex
{
    bool IsReachable(CellCoord from, CellCoord to);
    void MarkLayerDirty(int z);
}
```

`GoalNotWalkable` is distinguished from `Unreachable` deliberately: Job Assignment needs to
tell "that cell is solid rock" apart from "no route exists", and the two imply different
recovery.

---

## 4. Behavior Under Each Time Authority

*(Mandatory section per the routing policy — every simulation-bearing spec, any tier.)*

Pathfinding is **passive**: it registers no `ITickable`, owns no `Tick()`, and advances no
state. It is called from inside authority-driven execution by whichever system needs a
route. Only the composition rule and the caller set change by authority.

| | **RealTime** | **TurnBased** |
|---|---|---|
| Walkable | `IsPassableTerrain(c)` | `IsPassableTerrain(c)` ∧ ¬`DoorStore.BlocksMovement(c)` ∧ cell not occupied by a **living** unit |
| Closed door | **Passable**, +`DoorTransitSurcharge` — colonists auto-open in transit | **Blocks** — opening is an action (Combat set) |
| Open door | Passable | Passable |
| Broken door | Passable | **Unblocks immediately — the breach lands the same turn** |
| Cell occupied by another unit | **Does not block** (advisory index — no colony traffic deadlock) | **Blocks** (tactics legality) |
| Cell occupied by self | Never blocks | Never blocks |
| Region index | Used for job/reachability probing | **Not used for legality** — over-reports (C3.2); combat uses A\* under TurnBased walkability |
| Callers | Colonist Movement, Job Assignment, Stockpile & Hauling, Repair & Rebuild | Combat: Movement & Reachability, Combat: Raider Decision-Making |

> **ADR-0003 wording defect — RAISED AND FIXED 2026-08-20.** ADR-0003's RealTime formula
> literally read `IsPassableTerrain(c) ∧ ¬DoorStore.BlocksMovement(c)`, which would make a
> closed door block in RealTime — contradicting its own bracketed note *"colonists auto-open
> doors in transit"* and the spike's verified 44/44 behavior. The table above states the
> verified rule. Because ADR-0003 is **Accepted**, this was raised rather than silently
> overridden; it has since been corrected at source — see *Correction 2026-08-20* in
> `docs/architecture/adr-0003-entity-data-ownership.md`. **The ADR and this table now agree.**

Pathfinding also supplies the walkability predicate for ADR-0003's **pre-switch placement
normalization** ("nearest RealTime-walkable, unoccupied cell, scanning at expanding radius
in fixed ascending (Z, Y, X)-offset order"). Squad Prep decides, Colonist Movement executes,
Pathfinding only answers.

---

## 5. Dependencies

**Upstream** — Terrain Data Model #1 (`IsPassableTerrain`, `Revision`, `ChunkOf`, stair
Z-linkage); World Change Event Bus #3 (layer-dirty marking); ADR-0003 (`DoorStore.BlocksMovement`,
`UnitOccupancyIndex`); ADR-0001 (dispatch, mutation window).

**Downstream** — Job Assignment #10 (reachability probing); Excavation #15 / Construction #16
(work-position adjacency); Stockpile & Hauling #11; Repair & Rebuild #25; Combat: Movement &
Reachability #21; Combat: Raider Decision-Making #23; Squad Preparation #24 (normalization
predicate).

---

## 6. Tuning Knobs

Values live in `assets/data/pathfinding.json`, not hardcoded.

| Knob | Default | Range | Category | Rationale |
|---|---|---|---|---|
| `OrthogonalStepCost` | 10 | **fixed** | correctness | C1 pair — see warning below |
| `DiagonalStepCost` | 14 | **fixed** | correctness | C1 pair — 10/14 ≈ √2, matched to the octile heuristic |
| `StairStepCost` | 10 | 10–30 | gate | Raising it makes descent less casual without making it illegal |
| `DoorTransitSurcharge` | 10 | 0–40 | feel | **Placeholder** representing open-in-transit time; RealTime only; tuned by Construction #16 |
| `RegionRebuildsPerDispatch` | 1 | 1–4 | perf | Above 1, worst-case dispatch cost scales linearly (~0.26 ms each) |
| `MaxPathBufferCells` | 512 | 256–2048 | perf | Caller buffer ceiling; overflow returns `BufferTooSmall`, never a truncated path |

> **Deliberate exception to "gameplay values must be data-driven".** `OrthogonalStepCost`
> and `DiagonalStepCost` are exposed for inspection but **must not be tuned independently**:
> they are a coherent pair with the octile heuristic, and changing either alone breaks
> admissibility (C1). Implementation asserts the heuristic matches the cost pair at startup.

---

## 7. Acceptance Criteria

### (a) Headless / automated — Logic, **BLOCKING**

- [ ] **AC-1** A diagonal step is legal iff both shared orthogonal neighbours are walkable. Verified by: a one-cell-thick diagonal wall seals a room; corner-touching rooms stay disconnected; clearing one orthogonal neighbour legalises the step; breaching one wall cell opens it.
- [ ] **AC-2** The octile heuristic never exceeds true cost over randomised cell pairs, and A\* path cost equals Dijkstra cost on the same pairs (admissibility + optimality).
- [ ] **AC-3** The §4 mode-aware walkability table is reproduced exactly, both authorities, all six rows.
- [ ] **AC-4** A door reaching `IsBroken` unblocks movement **and** LOS within the same dispatch.
- [ ] **AC-5** Layers are disconnected without a stair; a stair Z-links its cell to the layer below and carries movement in both directions.
- [ ] **AC-6** The C7 revalidation table holds in all five rows — in particular, a sealed goal returns `Unreachable` with an empty path, never a stale one.
- [ ] **AC-7** Identical queries return identical paths, including across a save/load round-trip; multi-occupant occupancy results are in ascending `EntityId` order.
- [ ] **AC-8** `src/core/Navigation` contains no random draw of any kind and takes no RNG handle (CI-grep gate, ADR-0005).
- [ ] **AC-9** `IsReachable` agrees with an authoritative A\* answer in **100%** of randomised trials, across fresh, stale-with-budget, and stale-without-budget index states (C5 exactness).
- [ ] **AC-10** No region rebuild ever occurs inside the mutation window (debug assertion).
- [ ] **AC-11** No full-world flood fill exists in the simulation path — rebuild granularity is one layer (CI-grep gate + assertion).

### (b) Performance — headless benchmark, **BLOCKING regression gate**

Bands set above the spike's measured values, per the Terrain / Time Authority precedent.

- [ ] **AC-12** A\* long path (126 cells) ≤ **200 µs** *(measured 118.9)*
- [ ] **AC-13** 10 colonists all repathing long routes in one dispatch ≤ **1.0 ms** *(measured 0.500)*
- [ ] **AC-14** Dig + revalidate 10 cached paths ≤ **50 µs** *(measured 23.8)*
- [ ] **AC-15** Single-layer region rebuild ≤ **0.5 ms** *(measured ~0.26)*
- [ ] **AC-16** At most **1** layer rebuild per dispatch at default settings.
- [ ] **AC-17** **0 B steady-state allocation** and 0 Gen0 collections across 5,000 queries covering `FindPath`, `IsWalkable`, `IsReachable`, and revalidation *(measured 0.00 B/query)*.

### (c) Integration — **BLOCKED on siblings; does not gate this system's Done**

- [ ] **AC-18** Job Assignment's reachability-probing pattern stays in budget at MVP job counts — blocked on #10.
- [ ] **AC-19** AP-limited combat move sets derive correctly from TurnBased walkability — blocked on #21.
- [ ] **AC-20** Raider approach routing produces varied breach approaches (CD-2) — blocked on #23.

### (d) Advisory / playtest

- [ ] **AC-21** Diagonal movement reads as natural motion rather than a staircase (a 16×16 offset is 17 cells, not 33).
- [ ] **AC-22** Chokepoints still feel like chokepoints under diagonals — the mechanical half is already proven by AC-1; this is the felt half.

---

## 8. Open Questions & Routed Items

| # | Item | Routed to | Note |
|---|---|---|---|
| 1 | TurnBased occupancy: blocks traversal, or only end-of-move? | Combat: Movement & Reachability **#21** | ADR-0003 assigns it there. The spike hard-blocks; the index answers "occupied by whom" either way, so both readings are servable without an interface change. |
| 2 | `DoorTransitSurcharge` real value | Construction **#16** | Placeholder 10 until door open/close timing exists. |
| 3 | A "door policy" that forbids opening would make the terrain-only index over-report in **RealTime** too | Squad Prep **#24** / Construction **#16** | CD-16 lists door policy among peacetime standing decisions. If a locked-door policy ships, C3.2 needs revisiting. |
| 4 | ~~ADR-0003 RealTime door-blocking wording defect~~ — **CLOSED 2026-08-20** | technical-director | Corrected at source (ADR-0003 *Correction 2026-08-20*). Wording only; no re-validation, no status change. |
| 5 | Congestion-avoidance cost term in RealTime | **Deferred** (decided 2026-08-20) | Not in MVP: at ~10 colonists there is no jam to solve, and every A\* cost term is permanent tuning surface. **Adoption trigger**: colonist count or corridor contention rising to where walk-through-each-other reads as broken. |
| 6 | Revision polling → narrow change list | **Deferred**, trigger named | Polling costs O(cached paths × remaining length) per mutation — 63.2 µs/dig at MVP caps. **Trigger**: ~5× growth in colonist count, path length, or simultaneous dig rate. |
| 7 | Incremental region merge/split updates | **Deferred**, trigger named | **Trigger**: per-layer rebuild exceeding AC-15's 0.5 ms band, or multi-layer dirtying becoming routine (mass destruction across Z). |
| 8 | Flow-field / hierarchical pathfinding | **Deferred**, trigger named | **Trigger**: colonist count outgrowing AC-13's 1.0 ms burst band. Premature before the rebuild policy is proven in production. |
