# ADR-0002: Terrain Data Model

## Status
Proposed — **spike-validated 2026-07-25 on 5 of 6 criteria; one gate open (see Spike Results)** · **Amended 2026-08-03** (Battle Persistence)

*(Written per the systems-index sequencing: authored as Proposed before the Tier 0 spikes; promoted to Accepted when the terrain spike validates chunk size, memory footprint, allocation behavior, and render-extraction cost. The Tier 0 terrain spike has now run — see **Spike Results (2026-07-25)** below. Every measurable criterion passed and three tuning items are fixed. The **frame-rate clause closed on target hardware 2026-08-24** (p99 ~2 ms against a 16.6 ms budget); promotion to Accepted now awaits only the **checkpoint clause** that the Amendment below adds to criterion 5, which has no implementation yet.)*

> ### Amendment 2026-08-03 — Battle Persistence (user ruling 2026-08-02; propagated via `/propagate-design-change`, see `change-impact-2026-08-03-time-authority-mode-switch.md`)
>
> The Time Authority GDD's Battle Persistence ruling adds a rolling battle checkpoint written **after every resolved actor activation** (~150–300 per battle at MVP scale). Consequences for this ADR:
>
> 1. **`Snapshot()` is no longer confined to non-gameplay moments** — it also runs once per activation, inside the turn loop, directly under the presentation-gated animation. The Spike Results' decision 3 ("one-shot allocation … at a non-gameplay moment; no buffer-reuse machinery is warranted") is **retracted for the checkpoint path**: the measured 21.9 ms synchronous write (~1.2 frames) and 2 MB one-shot allocation per activation are unacceptable at checkpoint cadence. **Decision (user, 2026-08-03, Option A)**: checkpoints are full self-contained saves — snapshot on the sim thread into a **double-buffered pooled buffer** (~0.61 ms measured), gzip (~30 KB at MVP) and write on a background thread with atomic replace. Mechanism detail: ADR-0004 (Battle Checkpoint Architecture, pending). Colony-mode autosaves (switch-in, battle-end) keep the one-shot stance.
> 2. **The checkpoint must carry terrain state (full grid, standard snapshot).** "Reconstruct via mutation replay" is architecturally unavailable by this ADR's own rule 9 — the bus has no replay and terrain keeps no journal — and no player-input log exists. Combat: Targeting & Resolution is a legal TurnBased terrain writer (rule 4), so wall damage between checkpoints is exactly the state a resumed battle needs.
> 3. **Validation criterion 5 is re-scoped**: the target-hardware re-run must additionally measure checkpoint snapshot+write at combat cadence (per-activation) with the double-buffered async path, and confirm no frame-time impact during combat. **This ADR must not be promoted to Accepted on the old criterion 5.**
>
> The data model, facade, event contract, and all other rules are unchanged.

## Date
2026-07-24

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 |
| **Domain** | Core |
| **Knowledge Risk** | HIGH (4.7 is post-cutoff) — mitigated: the authoritative model is plain C# with zero Godot dependency; no post-cutoff API is load-bearing for this decision |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md`, `current-best-practices.md`, `modules/rendering.md` |
| **Post-Cutoff APIs Used** | None in the data model. The *candidate render backends* (GridMap+MeshLibrary with 4.7 editor improvements, or MultiMesh instancing) are post-cutoff-affected but are NOT decided here — they are decided in the Terrain Rendering & Cutaway quick-spec after the terrain spike |
| **Verification Required** | Terrain spike must measure: chunk rebuild → render extraction cost at MVP map size under the 16.6 ms budget; steady-state Gen0 allocation during dig-heavy play; full-map walkability sweep cost (the AoS falsification test); memory at target world size. GridMap-as-backend evaluation happens there, not here |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001 Time Authority (Proposed) — terrain mutations occur only inside authority-driven execution; the mutation-window assertion is provided by `TimeAuthorityManager` |
| **Enables** | ADR-0003 Entity Data Ownership (entities live at `CellCoord`s; occupancy index and door boundary defined there); Pathfinding, Spatial Query/LOS, Terrain Rendering & Cutaway quick-specs; Excavation+Construction, Material-Tier Destructibility GDDs; Map Authoring quick-spec |
| **Blocks** | Tier 0 terrain spike (implements this ADR); every system in the index that lists Terrain Data Model as a dependency (11 of 35 entries) |
| **Ordering Note** | Proposed BEFORE the terrain spike by design; the spike validates chunk size, layout, and extraction strategy. The Terrain Data Model **GDD** additionally waits for spike numbers per the sequencing policy — this ADR is the data contract, the GDD is the gameplay-facing rules. **Shared primitives**: `CellCoord`, `ChunkCoord`, and `EntityId` live in a foundation-primitives namespace (`Hollowdeep.Core.Primitives`) that both ADR-0001 and ADR-0002 consume — neither ADR's assembly depends on the other for its types (companion correction to ADR-0001's dependency note) |

## Context

### Problem Statement
Terrain is the highest-fan-out system in Hollowdeep (11 direct dependents) and the substrate of the core hook: the colony the player carves IS the tactics map. The world is a Gnomoria-style layered tile grid — per Z-level, each cell holds a floor and, separately, a wall — explicitly NOT free-form voxel terrain. The systems index flags God-object risk and GC/memory-layout risk on the 16.6 ms budget, and mandates: pure data, chunked cell arrays, a single mutation API, change events, zero Godot dependency, headless testability. This ADR fixes the cell record, the memory layout, the coordinate system, the mutation/event contract, and the ownership boundaries — the contract every downstream system codes against.

### Constraints
- Serialization contract (cross-cutting #2): authoritative state is plain data, `Snapshot()`/`Restore()`, derived state never serialized, cross-object references by **stable ID only**, headless CI round-trip test
- World Change Event Bus (cross-cutting #3): Terrain is the bus's ONE publisher; the bus stays a dumb synchronous dispatcher — no queueing, no replay
- ADR-0001: mutations advance state only inside `Tick()`-driven execution; bus handlers are idempotent bookkeeping only; deterministic iteration everywhere
- MVP scope caps: hand-authored small mountain, 3 strata, 3 material tiers, wall-HP-only destruction (no collapse, no floor damage, no fluids)
- CD-1 (locked): the Combat UI after-action report must show what broke, where, and **which material tier failed** — change events must carry enough to answer this
- CD-7 (locked): Repair & Rebuild consumes hauled materials to restore damaged-but-standing walls — an incremental HP-restore path is MVP-required
- Solo developer: simplest layout that meets the budget wins; optimizations require spike measurements first
- 60 fps / 16.6 ms frame budget; draw-call and memory ceilings to be set BY the terrain spike (technical-preferences TO-BE-CONFIGURED entries)

### Requirements
- One authoritative store; renderer, pathfinding, and combat all read the same data (zero conversion at the mode switch, per ADR-0001)
- Every mutation flows through one API and emits a change event — no back doors
- Cell carries: floor type, wall type, material tier, damage/HP, style dressing, reservation tag (systems index cell-record mandate)
- Deterministic: identical mutation sequences produce identical state and identical event streams
- Non-incremental world population (map load, save restore) must not masquerade as incremental change — subscribers get a full-rebuild signal, not a 262k-entry event storm
- Multi-cell atomic operations (stair excavation, combat AoE, designation-cancel) apply and publish as one batch
- Layout must survive procgen (Alpha #35) without breaking the public contract

## Decision

Adopt a **chunked dense-array grid of packed cell structs** (array-of-structures per chunk), wrapped in a single `TerrainWorld` facade that owns all mutation and publishes all change events. Plain C#, zero Godot dependency.

### Coordinate system and shared primitives

```csharp
// Namespace: Hollowdeep.Core.Primitives — shared foundation types.
// Consumed by ADR-0001 (SwitchTransitionData), ADR-0002, ADR-0003, and everything
// downstream. Owned by no system; changing them is an ADR-level event.

public readonly record struct CellCoord(int X, int Y, int Z);
// X, Y: horizontal position within a layer.
// Z: layer index. Z = 0 is the surface; Z INCREASES DOWNWARD (descent is the
//    frontier — stratum numbering matches the design language "stratum 3").
// This is the SIM coordinate space. The view layer owns the one mapping to
// Godot world space (data Z-down → Godot Y-up, cell size scale) — defined
// once in the Terrain Rendering quick-spec, never duplicated.

public readonly record struct ChunkCoord(int X, int Y, int Z);
// Chunk identity is PUBLIC (subscribers dirty-track by chunk); chunk SIZE is a
// queryable tunable (TerrainWorld.ChunkSize), never a caller-side constant.
// The only sanctioned CellCoord→ChunkCoord mapping is TerrainWorld.ChunkOf().

public readonly record struct EntityId(long Value);
// Defined in ADR-0003 (0 = None; long, monotonic per world via a serialized
// EntityIdSource, NEVER reused). Listed here because it lives in this shared
// namespace and this block declares changing shared primitives an ADR-level event.
```

### Cell record — packed 8-byte struct

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]   // layout is CONTRACT, not accident:
public struct TerrainCell                          // Snapshot/Restore byte-identity depends on it
{
    public ushort FloorTypeId;     // runtime Material Catalog id; 0 = no floor (void/pit)
    public ushort WallTypeId;      // runtime Material Catalog id; 0 = no wall (open cell)
    public ushort WallHp;          // current HP; meaningful only when WallTypeId != 0
    public byte   StyleId;         // ornament vocabulary/dressing variant; 0 = natural
    public byte   Flags;           // bit 0: TerrainJobClaim (see reservation rule); bits 1–7 spare
}                                  // exactly 8 bytes
```

- **Material tier is derived, not stored**: `Catalog.Wall(WallTypeId).Tier`. The systems-index cell-record mandate ("material tier") is satisfied by lookup — storing tier per cell would denormalize Material Catalog data and create a second writer for the same fact. The catalog lookup is a flat array index; if the terrain spike ever shows it hot, caching tier into a spare byte is a non-breaking internal change.
- **Runtime ids are NOT stable ids.** The `ushort` values are runtime indices assigned by the Material Catalog at load. The serialization contract's "stable ID only" rule is honored at the snapshot boundary (see Serialization), never by making cells carry strings.
- **Floor HP does not exist** — MVP destructibility is wall-HP only (concept doc MVP cap). Raiders breach walls laterally; floor destruction would drop units between layers, which is collapse-adjacent and correctly deferred with #34. Adding floor damage later widens the struct or claims spare bits; either is confined to Terrain internals.
- **`StyleId` exists from day one** even though MVP ships one vocabulary (CD-5): the field landing now means the Vertical Slice style picker is a data change, not a save-format migration.
- **`Flags` bit 0 has exactly one meaning**: *this cell is claimed by exactly one terrain-modifying job (dig / build / repair)*. It is a mutex for terrain work, nothing else. Item/stack reservations (a stockpile cell holds multiple stacks; hauling reserves per-stack) are NOT this bit — they are sparse tables owned by Stockpile & Hauling under ADR-0003's write-ownership rules. Job Assignment owns the who/why table for terrain claims; a debug validation pass asserts *bit set ≡ key present in Job Assignment's terrain-claim table* every N ticks in debug builds. Terrain guards the bit; it never interprets it.

### Memory layout — chunked AoS (decision the index delegated here)

**Array-of-structures per chunk**, chosen honestly on **simplicity and reversibility**, not on a claimed uniform access pattern:

- Render extraction genuinely reads multiple fields per cell together and gets ideal locality from AoS. The two hottest sim loops do not: pathfinding walkability reads 4 of 8 bytes, LOS reads 2. A hot/cold field split (SoA) would roughly double cache density for those sweeps — that is a real cost we are accepting.
- What buys the acceptance: an 8-byte cell keeps a full 32×32 chunk at 8 KB (L1-resident, and **off the Large Object Heap** — a single flat world array would not be); AoS is materially simpler for a solo developer; and the layout is a **private implementation detail behind `TerrainWorld`** — if the spike's full-map walkability sweep (the designated falsification test) shows the sweep dominating the frame budget, switching to hot/cold SoA touches zero callers.

**Chunking**: chunks are per-layer tiles of **32×32×1 cells** (candidate size — the terrain spike validates 16 vs 32 against rebuild latency and dirty-granularity waste; callers use `ChunkSize`/`ChunkOf`, so the number is tunable). Per-layer chunks align with the two dominant consumers: cutaway rendering (slices by layer) and dirty-tracking (a dig mutates one layer locally). MVP storage is a **dense fixed grid of chunks** sized by the hand-authored map bounds; sparse chunk storage (procgen-era, #35) is an internal change behind the same API.

**Memory reality check** (spike will confirm): a generous MVP mountain of 128×128 cells × 16 layers = 262k cells × 8 B = **2 MB** of cell data. Even a 256×256×32 full-vision world is 16 MB. Terrain cell data is not the memory risk; *allocation behavior* is — see the allocation policy below and the Risks table.

### The facade — one write path, one event stream

```csharp
public enum TerrainChangeKind : byte
{ WallPlaced, WallRemoved, WallDamaged, WallRepaired, FloorPlaced, FloorRemoved }

public readonly record struct TerrainChange(
    CellCoord Cell,
    TerrainChangeKind Kind,
    TerrainCell Previous);         // state BEFORE the change — the only place it survives.
                                   // CD-1's "which material tier failed" = Catalog.Wall(Previous.WallTypeId).Tier;
                                   // renderer teardown and repair-as-it-was read the same field.

public readonly ref struct TerrainChangeBatch      // ref struct — holding a span REQUIRES it (CS8345),
{                                                   // and it structurally forbids retention (see rule 1)
    public ReadOnlySpan<TerrainChange> Changes { get; }
}

public interface ITerrainChangeSink                 // implemented by the World Change Event Bus adapter
{
    void Publish(in TerrainChangeBatch batch);      // batch valid ONLY for the duration of this call
    void PublishWorldReloaded();                    // non-incremental population signal (load/restore)
}

// One mutation entry for bulk application:
public enum TerrainMutationKind : byte
{ SetWall, ClearWall, SetFloor, ClearFloor, DamageWall, RepairWall }

public readonly record struct TerrainMutation(
    CellCoord Cell, TerrainMutationKind Kind,
    ushort TypeId,          // SetWall/SetFloor only; 0 otherwise
    byte StyleId,           // SetWall/SetFloor only
    int Amount);            // DamageWall/RepairWall only

public readonly record struct DamageResult(DamageOutcome Outcome, int AppliedAmount, ushort RemainingHp);
public enum DamageOutcome : byte { Damaged, Destroyed, NoWall }
public readonly record struct RepairResult(RepairOutcome Outcome, int AppliedAmount, ushort RemainingHp);
public enum RepairOutcome : byte { Repaired, AlreadyAtMax, NoWall }

public sealed class TerrainWorld
{
    public TerrainWorld(WorldBounds bounds, AuthoredWorldData authored,   // load path: populate WITHOUT
                        IMaterialCatalog catalog, ITerrainChangeSink sink); // events, then PublishWorldReloaded

    // Reads — copies or spans over immutable views; never a mutable reference into storage
    public TerrainCell GetCell(CellCoord c);
    public ReadOnlySpan<TerrainCell> GetChunkCells(ChunkCoord chunk);  // bulk read: render extraction,
                                                                       // pathfinding sweeps — one contiguous
                                                                       // walk, not 1024 GetCell calls
    public int ChunkSize { get; }
    public ChunkCoord ChunkOf(CellCoord c);
    public bool InBounds(CellCoord c);
    public bool IsPassableTerrain(CellCoord c);   // floor present && no wall. TERRAIN's contribution only —
                                                  // composite walkability (terrain + doors + occupancy)
                                                  // is owned by Pathfinding (door/occupancy: ADR-0003)
    public bool IsTerrainJobClaimed(CellCoord c);
    public ulong Revision { get; }                // monotonic; +1 per mutating call and per Restore.
                                                  // O(1) "did the world change?" for caches and
                                                  // PostEncounterReconcile. Runtime-only, never serialized.

    // Writes — the ONLY mutation path in the game
    public void SetWall(CellCoord c, ushort wallTypeId, byte styleId);   // HP := catalog max for type
    public void ClearWall(CellCoord c);
    public void SetFloor(CellCoord c, ushort floorTypeId, byte styleId);
    public void ClearFloor(CellCoord c);
    public DamageResult ApplyWallDamage(CellCoord c, int amount);        // clamps; at 0 HP the wall is removed
                                                                         // and the batch reports WallRemoved
    public RepairResult ApplyWallRepair(CellCoord c, int amount);        // clamps at catalog max (CD-7 path)
    public void SetTerrainJobClaim(CellCoord c, bool value);             // NO bus event — see rule 2

    // Bulk — one validated pass, ONE published batch
    public BulkResult Apply(ReadOnlySpan<TerrainMutation> mutations);

    // Serialization contract (cross-cutting #2)
    public TerrainSnapshot Snapshot();
    public void Restore(TerrainSnapshot snapshot);                       // ends with PublishWorldReloaded
}
```

**Contract rules:**

1. **Single publisher, batched events, no retention.** Every mutating call publishes exactly one `TerrainChangeBatch`, synchronously, after state is fully applied. The batch and its span are valid **only for the duration of `Publish`** (the buffer is pooled and reused): handlers copy out the primitives they need (`ChunkCoord`s to dirty, `CellCoord`s to invalidate) and never store the batch — the `ref struct` makes retention a compile error, which is the intended enforcement of ADR-0001's synchronous-bookkeeping rule. The bus adapter's Godot-facing side (if any) must translate to plain C# data first — `TerrainChangeBatch`/`ReadOnlySpan` cannot cross Godot Signal/Variant marshalling.
2. **Terrain-job-claim flips publish nothing.** They are job bookkeeping, not world-shape changes; their owner (Job Assignment) flipped the bit and keeps the who/why table. Bus traffic stays proportional to *physical* world change.
3. **Bulk `Apply` semantics — fixed here, never re-litigated:** validate-all-then-apply (any invalid entry rejects the whole batch; `BulkResult` reports the first offending index; nothing is applied, nothing published); emitted change order preserves caller order; two mutations targeting the same cell in one batch are a programming error (batch rejected); entries that would be no-ops (e.g. `ClearWall` on an open cell) are dropped from the published batch and applied as nothing. Single-cell write methods on an out-of-bounds coord throw (programming error); no-op single calls publish nothing.
4. **Writers per time authority** (the concrete answer to "who mutates terrain in each mode"):

   | Authority | Legal terrain writers |
   |---|---|
   | RealTime | Excavation, Construction, Repair & Rebuild (each via its own Tick) |
   | TurnBased | Combat: Targeting & Resolution only (applying Material-Tier Destructibility results) |
   | Outside both (load window) | Map Authoring / `Restore` — non-incremental population, `WorldReloaded` signal |

   No writer registers with both authorities. UI never writes — it submits designations; the owning system's Tick executes them.
5. **Mutations only inside the mutation window.** `TimeAuthorityManager` opens a mutation window around authority dispatch (and the load path opens one around population); `TerrainWorld` write methods **debug-assert the window is open and `Publish` is not on the stack** — one mechanism catches both UI-callback writes and bus-handler writes, the two forbidden paths. Convention backed by assertion, matching ADR-0001's pattern.
6. **Non-incremental population is not change.** The constructor and `Restore` fill storage without emitting per-cell events, then signal `PublishWorldReloaded`; subscribers respond by full rebuild/rescan of their caches, never by incremental handling. This kills the load-time event storm and the silently-stale-caches-after-load bug in one rule.
7. **Reads return copies or read-only spans.** No caller can obtain a mutable reference into chunk storage; the write path cannot be bypassed.
8. **Determinism**: chunk iteration and storage order follow a fixed, documented order (chunk row-major by (Z, Y, X), cells row-major within). Identical mutation sequences produce byte-identical state AND identical event streams — asserted in CI alongside ADR-0001's TickSequence continuity.
9. **Terrain is passive — it never ticks.** `TerrainWorld` registers with no time authority and has no `Tick()`. Its "Behavior under each time authority" is: *identical inert store; only the legal writer set changes* (rule 4). The mode switch swaps who mutates it, not what it is. **PostEncounterReconcile precondition (explicit)**: the bus has no replay and terrain keeps no journal — live subscribers' own bookkeeping during the encounter is the *only* record of encounter-era change; reconcile enumerates nothing after the fact. `Revision` gives reconcile and cache owners the O(1) "anything changed?" check.

### Serialization — stable ids at the boundary

`TerrainSnapshot` carries: a **schema version**; the world bounds and chunk size; the raw chunk cell arrays (byte-identical, guaranteed by the `StructLayout` contract); and a **material manifest** mapping stable string material keys (Material Catalog's canonical identifiers, e.g. `"granite"`) to the runtime `ushort` ids used in the arrays. `Restore` remaps every cell's type ids through the manifest against the current catalog — inserting or reordering materials between sessions re-targets ids instead of silently re-materializing the world. Unknown keys fail restore loudly. `Revision` and all caches are runtime-only and never serialized. `Snapshot()` runs at the mode-switch autosave, at battle end, **and — since Battle Persistence (Amendment 2026-08-03) — once per resolved actor activation as the battle checkpoint**; the checkpoint path uses a double-buffered pooled buffer with async write (Option A, ADR-0004), while the two colony-mode autosaves keep the spike's one-shot allocation stance.

### Stairs and descent (decided here — it is Pillar 5's write surface)

A stair/ramp is a **`FloorTypeId` whose catalog entry declares Z-linkage** (walkability connects Z and Z+1); the exact connectivity rule lands in the Pathfinding quick-spec. Excavating downward — the game's core progression verb — is one atomic `Apply`: `SetFloor(stair)` at Z plus `ClearWall` at Z+1. Consequences fixed now: `SetFloor`/`ClearFloor` are gameplay-hot (not authoring-only); **no MVP system calls `ClearFloor` except designation-cancel and debug tooling**; floors still carry no HP.

### What Terrain does NOT own (God-object firewall)

The index's stated risk for this system is God-object growth. Hard boundaries:

| Concern | Owner | Terrain's involvement |
|---|---|---|
| Designations / blueprints (dig orders, build plans) | Excavation & Construction (+ Blueprint UI) | None — designations are *intent*, not world state. They reference `CellCoord`s |
| Pathfinding regions, reachability, connectivity, composite walkability | Pathfinding & Navigation | Subscribes to change batches; owns its own caches; composes `IsPassableTerrain` with door/occupancy data |
| Terrain-job-claim semantics (who/why) | Job Assignment | Terrain stores 1 bit under the debug invariant; owner keeps the table |
| Item/stack reservations | Stockpile & Hauling (per ADR-0003) | Nothing — not the Flags bit, ever |
| Zone membership (stockpile areas, home/no-go areas) | Stockpile & Hauling (sparse cell-set data) | Nothing — never a cell field, never a Flags bit |
| Colony-mode occupancy (who stands where) + combat occupancy | Mode-agnostic spatial index owned by the entity layer (ADR-0003) | Nothing in cells; the index keys by `CellCoord` |
| Meshes, instances, dirty-chunk rebuild | Terrain Rendering & Cutaway | Subscribes; reads via `GetChunkCells`; owns all render data |
| Material definitions, tiers, max HP, stable keys | Material Catalog | Terrain holds runtime `ushort` ids and queries the catalog |
| Items on the ground, furniture, doors, workshops, traps | Entity layer (ADR-0003) | Not cells. Anything with per-instance state beyond the 8-byte record is an entity at a `CellCoord` — the door-vs-wall boundary (blocks like a wall, has entity state) is settled in ADR-0003 |
| Combat state (cover values, targeting data) | Combat set / Spatial Query | LOS & cover *derive* from walls via read API; nothing combat-specific is stored in cells |

The growth firewall: **the cell struct only ever describes the architecture itself**. Any proposed new cell field that describes an *occupant*, a *plan*, or a *zone* belongs to ADR-0003's domain, the blueprint system, or a sparse side table — and the firewall table names its owner.

### Allocation policy (the index's named GC risk, answered)

- **Zero steady-state allocation** on: all reads, single-cell writes, and batch publish (the change buffer is pooled, owned by `TerrainWorld`, reused per mutation call — which is *why* retention is forbidden by rule 1).
- Bulk `Apply` uses caller-provided spans in, pooled buffers out.
- Chunks are 8 KB arrays — off the LOH by construction; no full-world array exists.
- Subscriber-side discipline is the subscribers' contract (their handlers copy primitives into their own pre-sized structures), but the spike measures **end-to-end Gen0 allocation during dig-heavy play** as a gate, not just terrain's half.

### Architecture Diagram

```
                writes (mutation window only)                reads
  Excavation ────┐  [RealTime]                       ┌──── Pathfinding (sweeps via GetChunkCells)
  Construction ──┤  [RealTime]                       ├──── Spatial Query / LOS & cover
  Repair ────────┤  [RealTime]                       ├──── Combat movement/targeting
  Combat: T&R ───┤  [TurnBased]                      ├──── Terrain Rendering (GetChunkCells extract)
  Map Authoring ─┘  [load window → WorldReloaded]    └──── Map/debug tooling
                            │
                            ▼
              TerrainWorld (plain C#) — chunked dense grid of 8-byte TerrainCell
                            │
                            │ one TerrainChangeBatch per mutation call (ref struct,
                            │ valid during Publish only; Previous state included)
                            ▼
                 World Change Event Bus (dumb synchronous dispatcher)
                            │
        ┌───────────────────┼──────────────────────┐
        ▼                   ▼                      ▼
   Pathfinding         Job Assignment        Terrain Rendering
   (invalidate         (flag jobs on          (dirty ChunkOf(cell);
    regions/paths)      changed cells)         rebuild in its own _Process)
```

### Key Interfaces
`CellCoord` / `ChunkCoord` / `EntityId` (shared primitives namespace) · `TerrainCell` (8-byte, `StructLayout` contract) · `TerrainWorld` facade (sole write path; `GetChunkCells` bulk read; `Revision`) · `TerrainChange` (with `Previous`) → `TerrainChangeBatch` (ref struct) → `ITerrainChangeSink` (+ `PublishWorldReloaded`) · `Apply` bulk semantics (rule 3) · `Snapshot()/Restore()` (schema version + material manifest) · `IMaterialCatalog` (read-only dependency)

## Alternatives Considered

### Alternative B: GridMap as the authoritative store
- **Description**: Use Godot's `GridMap` node + `MeshLibrary` as both the world data and the renderer; sim systems query the node.
- **Pros**: Engine-native; free editor authoring; rendering comes with it.
- **Cons**: `GridMap` stores one item id per cell — no floor/wall split, no HP, no style, no flags without parallel side-stores (at which point the side-store IS the data model). Ties all simulation reads to a scene-tree node: no headless tests, save entangled with scenes, violates the serialization contract and ADR-0001's plain-C# core. VERSION.md already flags naive per-change GridMap regeneration as a scale risk.
- **Rejection Reason**: The index pre-decided this ("the authoritative model is never a GridMap node") and the analysis confirms it. GridMap remains a *candidate render backend* evaluated in the rendering quick-spec — reading from this model, owning nothing.

### Alternative C: Whole-world struct-of-arrays (no chunks)
- **Description**: Global parallel arrays per field (`floorIds[]`, `wallIds[]`, `hp[]`, …) indexed by a flattened coordinate.
- **Pros**: Simplest indexing math; per-field scans are optimal (genuinely better for walkability sweeps — acknowledged in the AoS rationale); trivially serializable.
- **Cons**: No natural dirty-granularity for rendering (the #1 consumer of change locality); multi-megabyte arrays land on the LOH; resizing for procgen means reallocating the world; multi-field cell reads (render extraction, damage resolution) touch 3+ cache lines.
- **Rejection Reason**: Gives up chunk-level dirty tracking, which the cutaway renderer and spike explicitly need; the sweep advantage is recoverable later via hot/cold split behind the facade if the spike demands it.

### Alternative D: Sparse dictionary of cells
- **Description**: `Dictionary<CellCoord, TerrainCell>` storing only non-empty cells.
- **Pros**: Zero memory for un-excavated void; no bounds management.
- **Cons**: Hash+indirection on the hottest read path (walkability during pathfinding); nondeterministic iteration order by default; GC pressure from resizes; a mountain is mostly *solid*, not sparse — the sparsity premise is inverted here.
- **Rejection Reason**: Cache-hostile on exactly the loops the 16.6 ms budget cares about, and the memory it saves (§reality check) is ~2 MB.

## Consequences

### Positive
- Eleven dependent systems get one stable contract now, before the spike, without waiting for tuning numbers — and the contract answers, rather than defers, the questions each would otherwise re-litigate: bulk semantics, writer sets per mode, load vs. change, repair, prior-state capture, reservation meaning.
- One write path + batched events + the mutation-window assertion make the genre's worst bug classes (stale caches, event storms, renderer/sim divergence, UI writing state) structurally hard to write, not just forbidden by convention.
- Zero Godot dependency: terrain unit tests run headless in plain .NET alongside ADR-0001's; the 4.4–4.7 breaking-change surface is irrelevant to this assembly.
- The mode switch costs terrain nothing — passive store, swapped writer set (ADR-0001's "authority-swap only" made concrete for the biggest shared system).
- `TerrainChange.Previous` makes CD-1's after-action report, renderer teardown, and repair-as-it-was cheap forever; the material manifest makes saves survive catalog evolution — both are 3-month-retrofit classes bought for bytes.

### Negative
- A facade over raw arrays is more ceremony than "just use a GridMap" — accepted as the price of headless testing and the God-object firewall.
- `TerrainChange` at 16+ bytes (with `Previous`) doubles event payload vs. the naive version — accepted: batches are pooled and transient, and the alternative (subscribers needing before-state that no longer exists) is a breaking change to every handler later.
- Derived-not-stored material tier costs a catalog lookup on damage resolution and cover derivation — expected negligible (flat array index), spike will confirm, spare byte reserved as the escape hatch.
- AoS knowingly gives up cache density on walkability/LOS sweeps (the honest version of the layout trade-off); the spike's sweep benchmark is the tripwire, and the hot/cold split is the pre-planned fallback.
- Steady-state terrain mutation is dominated by single-cell batches at colonist work cadence (a colonist digs one cell at a time) — the batch machinery earns its keep on load/restore, stair digs, designation-cancel, and combat AoE, not on routine digging. Accepted: the per-call overhead of a one-entry batch is trivial.

### Risks
- **Cell-struct field creep** (occupancy, zones, job ids, light values migrating into cells). *Mitigation*: the firewall table names an owner for every adjacent concern; review rejects any cell field describing an occupant, plan, or zone.
- **Reservation-bit divergence from Job Assignment's table** — the two-sources-of-truth risk. *Mitigation*: single documented meaning (terrain-job mutex only) + periodic debug invariant assertion (bit ≡ table keyset); item/stack reservations never touch the bit.
- **Allocation creep on the 16.6 ms budget** (the index's named GC risk). *Mitigation*: explicit allocation policy above; pooled batch buffers owned by `TerrainWorld`; 8 KB chunks off-LOH; spike gates on measured Gen0-per-frame during dig-heavy play; snapshot buffer strategy measured at the CD-9 autosave point.
- **Event-batch granularity mismatch** — renderer wants chunk dirt, pathfinding wants regions; a raw cell list serves neither directly. *Mitigation*: handlers aggregate locally via `ChunkOf` (that's their idempotent bookkeeping); if profiling shows aggregation cost matters, the batch can add a precomputed touched-chunk summary without breaking the contract.
- **Spike falsifies AoS or chunk size** — *Mitigation*: both are behind the facade; the ADR is revised before Accepted, callers untouched.
- **Two write paths sneak in** (renderer "fixing" cells, debug console poking arrays). *Mitigation*: storage is private; the mutation-window assertion fires on any write outside authority dispatch; debug console mutates via the same facade; CI greps the terrain assembly for Godot references.

## GDD Requirements Addressed

| GDD Document | System | Requirement | How This ADR Satisfies It |
|-------------|--------|-------------|--------------------------|
| `design/gdd/systems-index.md` #1 | Terrain Data Model | Chunked cell arrays, mutation API, change events, plain C#, zero Godot, memory layout decided in ADR-002 | The decision itself: chunked AoS, `TerrainWorld` facade, batched bus events |
| `design/gdd/systems-index.md` #7, #8, #12 | Rendering & Cutaway, Pathfinding, LOS | Read + change-notification substrate | `GetChunkCells` bulk read, per-layer chunks for cutaway, `ChunkCoord` dirty-tracking, `TerrainChangeBatch` subscription, `WorldReloaded` rebuild signal |
| `design/gdd/systems-index.md` #25 + CD-7 | Repair & Rebuild (PROTECTED) | Incremental restore of damaged walls, consuming hauled materials | `ApplyWallRepair` + `WallRepaired` change kind; materials consumption lives with Repair, HP restoration lives here |
| CD-1 (systems index) | Combat UI after-action | "What broke, where, which material tier failed" | `TerrainChange.Previous` captures the destroyed wall's type/tier/HP at the moment of change |
| `design/gdd/game-concept.md` Core Mechanic 1 | Blueprint carving | Layered floor+wall tile grid (Gnomoria-style, not voxel) | `TerrainCell` floor/wall split; `CellCoord` Z-as-layer; stair-dig atomic `Apply` |
| `design/gdd/game-concept.md` Core Mechanic 4 / MVP cap 2 | Material-tier destructibility | 3 tiers, wall-HP destruction, no collapse | `WallHp` + `ApplyWallDamage` (destroy-at-zero, full `DamageResult`); tier via catalog; no floor HP by design |
| `design/gdd/game-concept.md` Unique Hook + Pillar 5 | Base IS the tactics map; descent is the frontier | Combat reads the same store; descent has a defined write surface | Passive-store rule + writer table; stair excavation as one atomic `Apply` |
| CD-5 (systems index) | Style vocabularies | One vocabulary MVP, picker in VS without migration | `StyleId` field lands in the save format now |

## Performance Implications
- **CPU**: Reads are array index + 8-byte copy, or a contiguous span walk per chunk; passability is two field tests. Mutation adds validation + one pooled batch publish. Catalog tier lookup is a flat array index.
- **Memory**: ~2 MB cell data at a generous MVP world; ≤16 MB at full-vision scale; zero steady-state allocation per the policy; chunks off-LOH.
- **Load Time**: `Restore` is a contiguous per-chunk copy + `ushort` remap through the material manifest + one `WorldReloaded` rebuild — negligible against scene load; the rebuild cost lands on subscribers and is a spike measurement.
- **Network**: N/A.

## Migration Plan
None — greenfield. The Tier 0 terrain spike implements this contract; if it falsifies chunk size, AoS, or extraction strategy, internals change behind the facade and this ADR is revised before promotion to Accepted. The Terrain Data Model GDD (gameplay rules: what is diggable, buildable adjacency, dig-time formulas) is authored after the spike, against this contract plus measured numbers. **Companion edits at adoption** (same changeset as this ADR): ADR-0001's dependency note gains the shared-primitives correction (`CellCoord`/`EntityId` live in `Hollowdeep.Core.Primitives`, not owned by either ADR), and ADR-0001's worked-example table gains the row *Terrain Data Model | neither — passive store | — | — | publishes only; never subscribes*.

## Spike Results (2026-07-25)

Tier 0 terrain spike: `prototypes/terrain-spike/` (data model, plain .NET 8) and
`prototypes/terrain-spike/render/` (Godot 4.7.1 mono, Forward+). Full detail and method:
`prototypes/terrain-spike/SPIKE-NOTE.md`. **38/38 contract correctness checks pass** (criteria 1–4).

**Measured, MVP 128×128×16 unless noted:**

| Measure | Measured | This ADR predicted | |
|---|---|---|---|
| `sizeof(TerrainCell)` | 8 bytes | 8 bytes | ✅ |
| Cell memory, MVP / full-vision | 2.00 MB / 16.00 MB | "~2 MB" / "16 MB" | ✅ exact |
| Chunk on LOH | No (2/8/32 KB at chunk 16/32/64) | off-LOH by construction | ✅ |
| Steady-state allocation, dig-heavy | **0.17 B/mutation, 0 Gen0** over 60k mutations | zero steady-state | ✅ |
| Mutation + publish | 0.338 µs; realistic frame (10 diggers) 0.0101 ms = **0.06%** of budget | — | ✅ |
| Full-map walkability sweep (chunk 32) | **0.290 ms = 1.7%** of a 16.6 ms frame | the falsification risk | ✅ not falsified |
| Chunk extraction | 1 chunk 1.10 µs; full layer 0.049 ms = 0.3% frame | — | ✅ |
| Bulk `Apply` | 2→0.67 µs · 16→7.78 · 64→32.0 · 256→69.9 µs | — | ✅ |
| Snapshot / Restore, MVP | 0.61 ms (2.01 MB one-shot) / 0.99 ms | strategy deferred to spike | ✅ answered |

**Three decisions this ADR deferred, now fixed:**

1. **Chunk size = 32×32×1, confirmed.** Sweep gains plateau after 32 (16: 0.411 ms, 32: 0.290, 64: 0.282) while rebuild cost grows with chunk area (0.31 / 1.10 / 4.79 µs). 32 is the balance point. `ChunkSize`/`ChunkOf` remain the only sanctioned mapping regardless.
2. **The AoS concession is retired — measurement falsified it in the good direction.** This ADR conceded that a hot/cold SoA split "would roughly double cache density" for sweeps. It does not: chunked AoS measured **21–46% faster** than a flat two-plane SoA sweep, because each 8 KB chunk is one L1/L2-resident sequential stream. *(Caveat: the SoA arm was a straightforward two-array scan, not a vectorised SoA; a tuned SoA could narrow the gap. Immaterial to the decision at 1.7% of a frame.)* **The hot/cold-split fallback is removed from the risk list**; the Negative-consequences bullet conceding sweep cache density no longer applies.
3. **Snapshot buffer strategy = one-shot allocation.** 0.61 ms / 2.01 MB at MVP (8.90 ms / 16.06 MB at full-vision) at the CD-9 mode-switch autosave — a non-gameplay moment. No buffer-reuse machinery is warranted. *[Narrowed 2026-08-03 — Battle Persistence]*: this holds for the two colony-mode autosaves only; the per-activation battle checkpoint uses a double-buffered pooled buffer with async write (Amendment above, ADR-0004).

**Render backend (the routed open question) — TWO stacked GridMaps (wall + floor), `cell_octant_size = 32`.** 3-layer cutaway, 589,824 primitives, 14.25 MB video memory (identical across backends — geometry sets it, not backend):

| Backend | Draw calls | Build | Per-dig update |
|---|---|---|---|
| MultiMesh (per-instance / bulk `Buffer` / pooled+bulk) | 82 | 521 / 70 / 49 ms | 502 / 524 / **452 µs** |
| GridMap octant 4 / 8 / 16 / **32** | 1233 / 343 / 108 / **32** | ~33 ms | 1.32 / 1.94 / 1.80 / **1.85 µs** |

GridMap at octant 32 wins **both** axes: 2.6× fewer draw calls and ~240× cheaper incremental update. MultiMesh's cost is structural — a one-cell change rewrites the chunk's ~2000-instance buffer; bulk upload and pooling only improved initial build. At 10 concurrent diggers MultiMesh costs ~4.5 ms/frame (27% of budget) vs GridMap's ~0.02 ms. **This does not alter the forbidden pattern**: `TerrainWorld` remained the single source of truth and GridMap was a pure write target — the "render backend reading from the model" role this ADR already permits.

**Floor + wall in the same cell — RESOLVED 2026-07-26 (two stacked GridMaps).** A single GridMap stores one item id per cell and therefore cannot express the floor-and-wall-per-cell world model this game rests on — disqualifying regardless of draw-call numbers. **This was never a data-model defect**: `TerrainCell` has always carried `FloorTypeId` and `WallTypeId` as separate fields, and all 38 contract checks passed; the limit was entirely in the render backend, which is precisely what this ADR's "GridMap is at most a render backend" rule exists to contain. **The fix is a wall map plus a floor map** (thin slab items offset to the cell bottom via `MeshLibrary.SetItemMeshTransform` — a per-*library-item* transform, not a per-placed-cell call; GridMap has no per-instance override, godot-specialist 2026-08-08), sharing coordinate system, cell size, and octant, both fed from `TerrainWorld` as pure write targets. Measured cost at octant 32: **draw calls unchanged at 32**, video memory 14.25 → 16.42 MB, per-dig ~2 µs (unchanged), build 39–42 ms, 2 render nodes. Verified in-engine rather than inferred: **15,763 cells carry both a floor and a wall, matching `TerrainWorld` exactly** (`render_matches_model=True`), with the floor band visible in cross-section. The dig update also simplifies — clearing a wall becomes one `SetCellItem(pos, -1)` since the floor is already present. Any future per-cell visual layer (ore veins, floor coverings) is another stacked map at ~2 MB, not a redesign; the alternative of one GridMap with a MeshLibrary item per (floor, wall) pair dies on combinatorics once `StyleId` multiplies in. **Caveat — a damage-state overlay is NOT a flat +1 layer like floor+wall (godot-specialist, architecture-review 2026-08-08).** Because GridMap has no per-instance data channel, each damage tier must be a distinct mesh item per material/style combo, so a damage overlay is a *style-variety multiplier* against the measured ~8-variants-per-tier draw-call ceiling (technical-preferences), not the ~2 MB flat cost above. The floor+wall "draw calls unchanged at 32" result does not generalize to it; the damage overlay needs its own draw-call spike in the Terrain Rendering & Cutaway quick-spec before it is treated as settled.

**Frame-rate clause — CLOSED 2026-08-24 (target hardware).** Re-run on an RTX 3060 Ti (Godot 4.7.2 mono, `gridmap_two` at octant 32, sustained 1800-frame window at 8 digs/frame, vsync off): frame-time **p99 2.167 ms on Vulkan and 2.024 ms on D3D12** against the 16.6 ms budget — ~8× headroom — with **0 Gen0/Gen1/Gen2 collections** and 32.7–36.1 B/frame. Draw calls came in at exactly the predicted 32 and `render_matches_model=True` held through 30 s of continuous digging. Evidence: `production/qa/evidence/terrain-target-hardware-2026-08-24/`; detail and caveats in `prototypes/terrain-spike/SPIKE-NOTE.md`. Two caveats recorded rather than waved through: a single ~50 ms frame per run (1 in 1800, beyond p99, reads as environmental — one confirming re-run wanted), and video memory reported at 43–50 MB where `buffer_mem_mb` matched the recorded 16.23 MB, i.e. the earlier figure measured terrain buffers and was mislabelled "video memory" (corrected in technical-preferences).

**Still open before promotion to Accepted:** the **checkpoint clause** the 2026-08-03 amendment added to criterion 5 — checkpoint snapshot+write at per-activation combat cadence on ADR-0004's double-buffered async path, confirming no frame-time impact during combat. **No implementation of that path exists yet**, so criterion 5 is *partially* discharged (two clauses closed, one outstanding) and this ADR **remains Proposed**, exactly as the amendment requires. Also unmeasured: GridMap collision/physics cost (shapes disabled; the grid is not physics-driven) and procgen-era sparse chunk storage (out of MVP scope).

## Validation Criteria
1. Terrain assembly has **zero Godot references** (CI grep) and its full unit suite runs headless: mutation→event pairing with correct `Previous`, damage clamp/destroy-at-zero, repair clamp-at-max, bulk `Apply` rule-3 semantics (reject-on-invalid, duplicate-cell rejection, no-op dropping, order preservation), passability, bounds, mutation-window assertion firing on out-of-window writes.
2. `Snapshot()` → `Restore()` round-trip is byte-identical on unchanged catalogs, and correctly **remaps** ids when the catalog gains/reorders materials; identical mutation sequences yield identical event streams (CI, alongside ADR-0001's determinism gates).
3. `Restore` and initial load publish `WorldReloaded` (no per-cell events), and a subscriber-rebuild integration test shows pathfinding/render caches valid afterward — no stale-cache-after-load.
4. A stair excavation and a multi-cell combat effect each publish exactly **one** batch; routine single-cell digs publish one single-change batch each.
5. Terrain spike hits 60 fps at MVP map size with dig-driven chunk rebuilds and reports: final chunk size, draw-call ceiling, render memory, Gen0-per-frame during dig-heavy play, full-map walkability sweep cost (AoS falsification check), snapshot buffer strategy at the colony-mode autosaves, **and — re-scoped 2026-08-03 (Battle Persistence)** — checkpoint snapshot+write cost at per-activation combat cadence on the double-buffered async path (Option A), confirming zero frame-time impact during combat — which fill technical-preferences' TO-BE-CONFIGURED budgets and gate promotion to Accepted.
6. Six months in: `TerrainCell` still describes only architecture — no occupant, plan, zone, or combat state has leaked into it; the reservation-bit debug invariant has never fired in CI.

## Open Questions (routed, not decided here)
- **Render backend** (GridMap+MeshLibrary vs MultiMesh vs hybrid) → Terrain Rendering & Cutaway quick-spec, after the terrain spike measures both against this model. Include the godot-specialist's authoring-ergonomics note: if Map Authoring uses the Godot editor to paint, the editor shows Y-up axes while the author thinks in sim-Z-down — a tooling UX concern for the Map Authoring quick-spec, not a data-model defect.
- **Damage-overlay draw-call spike** (godot-specialist, architecture-review 2026-08-08) → Terrain Rendering & Cutaway quick-spec. **Design answered 2026-08-24, measurement still owed**: the quick-spec (C7) rejects the third stacked GridMap in favour of a **sparse overlay** — one `MultiMeshInstance3D` per damage state, instanced only on cells currently in that state. This decouples damage from the material x style multiplier entirely: cost is bounded by three meshes rather than material x style x damage, and instance count scales with how much is broken rather than with world size. The spike (quick-spec AC-10) must still confirm it, but it is now expected to measure a small constant rather than a multiplier against the ~8-variants-per-tier ceiling. Validate the third damage-state overlay GridMap against the ~8-variants-per-tier draw-call ceiling: it is a style-variety multiplier (a distinct mesh item per damage tier × material/style combo), not the free flat layer the floor+wall split turned out to be. Also fold this into the pre-render-backend Godot 4.7.1 verification gate (GDD TR-terrain-044) — measure the overlay map's draw-call cost before committing the render backend.
- **World-size ceiling, chunk size, snapshot buffer strategy final numbers** → terrain spike; recorded into technical-preferences and this ADR at promotion.
- **Door-vs-wall boundary and the occupancy index** → ADR-0003: a door blocks like a wall but is an entity with state; the mode-agnostic occupancy index keyed by `CellCoord` is entity-layer-owned.
- **Stair connectivity rule details** (diagonal adjacency, climb cost) → Pathfinding quick-spec, against the Z-linkage catalog flag decided here.
- **Designation/blueprint data shape** → Excavation & Construction GDD; it is intent, not terrain.
- **Zone data structure** (sparse cell sets for stockpiles/home areas) → Stockpile & Hauling quick-spec; never terrain state.

## Related Decisions
- ADR-0001 Time Authority (Proposed) — mutation-window provider; writer-set-per-authority rule; passive-store behavior; companion edits listed in Migration Plan
- ADR-0003 Entity Data Ownership (pending) — the entity/cell boundary defined by the firewall table; door boundary; occupancy index; item/stack reservations
- Seeded RNG ADR (pending) — terrain itself draws no RNG; map generation (Alpha) will
- `design/gdd/systems-index.md` — cell-record mandate, God-object risk entry, cross-cutting contracts #2 and #3, CD-1/CD-5/CD-7 notes
- `design/art/art-bible.md` §1 — lighting carries no gameplay semantics: no light data in cells
