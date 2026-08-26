# Hollowdeep — Master Architecture

## Document Status

| Field | Value |
|-------|-------|
| **Version** | 1.0 |
| **Created** | 2026-08-25 |
| **Engine** | Godot 4.7.1 · C# (.NET 8) |
| **Authored by** | `/create-architecture` (review mode: full) |
| **Technical Director Sign-Off** | 2026-08-25 — **APPROVED WITH CONDITIONS** (TD-ARCHITECTURE) |
| **Lead Programmer Feasibility** | 2026-08-25 — **CONCERNS ACCEPTED** (LP-FEASIBILITY) |

> **Conditions on the sign-off.** This document is architecturally sound and nothing in it is
> unimplementable — but it is **not yet codeable**, and both gates independently reached that
> conclusion for the same reason: missing written-down interfaces, not wrong decisions.
> The conditions are tracked as **QQ-23 … QQ-26** in §8 and must close before the systems they
> govern are implemented. §11 records the full gate findings.

**Source documents.** 2 full GDDs (`terrain-data-model.md`, `time-authority-mode-switch.md`),
4 quick-specs (Pathfinding & Navigation, Material Catalog, Colonist Entity & Attributes,
Terrain Rendering & Cutaway), the systems index (35 entries), 5 ADRs (0001–0005), the
cross-cutting contracts annex, `requirements-traceability.md` (97 TR rows), and the
version-pinned engine reference library.

**What this document is.** The whole-system blueprint that gives the ADRs their context.
ADRs record *point decisions and their reasoning*; this records *how the pieces fit* — which
layer each system lives in, who owns which data, how data moves, and what the contracts
between modules are. It is the last Technical Setup artifact and a hard blocker at the
Production gate.

**What this document is not.** It does not re-decide anything an ADR already decided, and it
does not restate ADR rationale. Where an ADR is authoritative, this document points at it.
Two outputs here are genuinely new: the **API boundary table** (§5) and the **consolidated
open-questions register** (§8).

---

## 1. Engine Knowledge Gap Summary

`VERSION.md` pins Godot 4.7.1 and rates 4.7 **HIGH RISK** globally, because the model's
training data ends around 4.3 and versions 4.4–4.7 shipped substantial changes.

**The architecture's primary mitigation is structural, not procedural**: the entire simulation
lives in `src/core/Hollowdeep.Core.csproj`, a plain-C# assembly with **zero Godot references**,
enforced by a CI grep gate that has been **proven** (planted `using Godot;` was caught;
a core file merely mentioning Godot in a doc comment was correctly not flagged). Every
4.4–4.7 breaking change — Jolt, glow reordering, D3D12 default, dual-focus UI, particle and
shader-preprocessor changes — is *irrelevant to the simulation by construction*. All 5 ADRs
declare "Post-Cutoff APIs Used: **None**" and that claim is structurally enforced rather than
trusted.

Engine risk therefore concentrates entirely in the **view layer** (§2, Presentation).

### Verified 2026-08-25 against the `4.7.1-stable` source tag

Two gaps that were open at the 2026-08-08 architecture review are now closed. Detail:
`docs/engine-reference/godot/modules/gridmap.md` and the *Fixed-Timestep Settings* section of
`modules/physics.md`.

| Item | Status | Finding |
|---|---|---|
| **GridMap risk rating** | **Corrected → LOW** | Additive-only 4.3→4.7.1: **0 methods removed, 0 signature changes**, 1 member added. 7 octant-query methods are new in 4.7 and are opt-in. The 4.3-era API the model knows is intact. |
| **No per-instance data channel** | **CONFIRMED** | `set_cell_item(position, item, orientation)` is the entire per-cell write surface — verified by enumerating all 30 methods and 11 members. Validates the sparse-overlay damage design and architecture-review finding 2. |
| **`set_item_mesh_transform` is per-item** | **CONFIRMED** | Keyed by library item id, not placed cell. Discharges architecture-review finding 4 (ADR-0002 text precision). |
| **`max_physics_steps_per_frame`** | **CLOSED** | Name and default (**8**) exactly as ADR-0001 assumed; `physics_ticks_per_second` default **60**. |
| **Runtime read-API (ADR-0001 OQ #9)** | **ANSWERED** | Use the **`Engine` singleton**, not `ProjectSettings`. The ProjectSettings keys are *"only read when the project starts"* — a startup guard reading them validates a value the engine may not be using. **Binding on TR-time-011.** |
| `AreaLight3D` present at 4.7.1 | Confirmed | VERSION.md's claim holds. |

### Remaining engine risk

| Domain | Risk | Where it bites | Mitigation |
|---|---|---|---|
| Rendering / GridMap | **LOW** (was HIGH) | Terrain Rendering #7 | Verified above; spike-measured on target hardware |
| Shaders | **MEDIUM** | Cutaway depth cue; damage overlay | 4.5 shader baker, 4.7 preprocessor tightening — unexercised; shader work is view-layer only |
| Input | **MEDIUM** | Blueprint UI #26, Combat UI #27 | 4.6 dual-focus, 4.5 SDL3 gamepad — route through `InputMap` action names from day one |
| Audio | **MEDIUM** | Audio #33 (Vertical Slice) | 4.7 spectrum-analyzer API change — not MVP |
| Animation / Particles / Physics | **LOW** | — | Unit movement is cell-to-cell, never physics-driven; no `AnimationTree`, no particle rotation dependency in MVP |
| **Simulation core** | **NONE** | — | Zero Godot references, CI-enforced |

---

## 2. System Layer Map

35 systems-index entries assigned to five layers. **Tier** is the priority tier; **Doc**
is the routing tier (Full GDD / Quick-spec / ADR-only / UX spec / Audio brief).

### Platform Layer — the engine surface

Not project code. Godot 4.7.1 runtime, .NET 8 CoreCLR, the physics frame that drives
`_PhysicsProcess`, and the OS file/input surface. **Exactly three touchpoints** are sanctioned:
the `TimeAuthorityRoot` autoload calling `Advance(delta)`, view Nodes reading stores in
`_Process`, and the composition root resolving the save path and root seed.

### Foundation Layer — shared data and contracts, zero dependencies

| # | System | Tier | Doc | Governing ADR | Status |
|---|---|---|---|---|---|
| 1 | Terrain Data Model | MVP | Full GDD | **ADR-0002 (Accepted)** | GDD Approved |
| 2 | Time Authority / Mode-Switch | MVP | Full GDD | **ADR-0001 (Accepted)** | GDD Designed |
| 3 | World Change Event Bus | MVP | ADR-only | ADR-0002 | Not started |
| 4 | Seeded RNG / Determinism | MVP | ADR-only | **ADR-0005 (Proposed)** | Specified |
| 5 | Material Catalog | MVP | Quick-spec | ADR-0002 | Drafted |
| 6 | Save/Load & Serialization | MVP | ADR-only | **ADR-0004 (Proposed)** | Partial |
| 29 | Dev Tools / Debug Console | **Tier 0** | ADR-only | — | **Built** |

Foundation owns every shared primitive: `CellCoord`, `ChunkCoord`, `EntityId` (namespace
`Hollowdeep.Core.Primitives`, owned by no single system — changing them is an ADR-level event).

> **Entity stores sit at the Foundation/Core boundary.** ADR-0003 governs them, and Terrain
> (#1) does not depend on them, but Pathfinding and everything gameplay-facing does. They are
> listed under Core with #9 because that is where their spec lives.

### Core Layer — simulation infrastructure

| # | System | Tier | Doc | Governing ADR | Status |
|---|---|---|---|---|---|
| 7 | Terrain Rendering & Cutaway | MVP | Quick-spec | ADR-0002 | Drafted |
| 8 | Pathfinding & Navigation | MVP | Quick-spec | ADR-0002/0003 | Drafted |
| 9 | Colonist Entity & Attributes | MVP | Quick-spec | **ADR-0003 (Accepted)** | Drafted |
| 10 | Job Assignment & Priority | MVP | Full GDD (paired #13) | ADR-0003 | Not started |
| 11 | Stockpile & Hauling | MVP | Quick-spec | ADR-0003 | Not started |
| 12 | Spatial Query / LOS & Cover | MVP | Quick-spec | ADR-0002 | Not started |
| 14 | Map Authoring / Content Load | MVP | Quick-spec | ADR-0002 | Not started |

> **#7 is the one MVP system whose code is Godot-side rather than plain C#.** It is
> categorised Core because it is infrastructure every mode depends on, but it obeys
> Presentation rules: it reads, never writes, and never ticks.

### Feature Layer — the game loop

| # | System | Tier | Doc | Status |
|---|---|---|---|---|
| 13 | Colonist Needs & Simulation | MVP | Full GDD (paired #10) | Not started |
| 15 · 16 | Excavation · Construction | MVP | Full GDD (combined) | Not started |
| 17 | Material-Tier Destructibility | MVP | Full GDD | Not started |
| 18 | Raid / Threat Trigger | MVP | Full GDD | Not started |
| 19–23 | Combat set (Turn Order · Action Economy · Movement · Targeting · Raider AI) | MVP | Full GDD set | Not started |
| 24 | Squad Preparation | MVP | Quick-spec | Not started |
| 25 | Repair & Rebuild — **PROTECTED** | MVP | Quick-spec | Not started |
| 30 · 31 | Skill & Veterancy · Identity & Memory | Vertical Slice | Quick-spec | Not started |
| 34 · 35 | Structural Collapse · World Generation | Alpha | Full GDD | Not started |

### Presentation Layer — views and UI

| # | System | Tier | Doc | Status |
|---|---|---|---|---|
| 26 | Blueprint / Designation UI | MVP | UX spec | Not started — **highest accessibility load** |
| 27 | Combat UI | MVP | UX spec | Not started — owns CD-1 after-action |
| 28 | Colonist / Roster UI | MVP | UX spec | Not started |
| — | Notifications (shared component) | MVP | across the 3 UX specs | Not started |
| 32 · 33 | Onboarding · Audio | Vertical Slice | UX spec / Audio brief | Not started |

**Presentation is read-only, universally.** Views bind by `EntityId`, poll `Revision`, and
render in `_Process`. Input submits *designations and orders* to owning systems; it never
writes state. This is a forbidden-pattern rule in `technical-preferences.md`, not a guideline.

### Systems not in a layer

**Strata/Depth Progression is data, not a system** — material distribution lives in Material
Catalog (#5), threat scaling in Raid Trigger (#18). **Doors are an entity kind**, not a
numbered system (`DoorStore`, ADR-0003), touched by #16/#22/#21/#8/#12.

### Three new systems the index does not yet have

Consequences of the 2026-08-24 prosthetics-as-unlockable-technology ruling. **Recorded here
because they are structural, not because this document decides them** — they need index
entries and a `/scope-check`:

1. **Research / Technology** — gates the prosthetic unlock (Colonist Entity §8 item 4)
2. **Production chain** — manufactures the prosthetic
3. **Furniture / workstations** — beds for bed-rest recovery, workstations for the chain

---

## 3. Module Ownership

The rule the whole architecture rests on: **exactly one writer per field group per time
authority**, granted as a narrow interface at the composition root. A system physically lacks
a reference to setters it does not own. Ownership review happens in **one file**.

### Foundation

| Module | Owns | Exposes | Consumes | Engine APIs |
|---|---|---|---|---|
| `TerrainWorld` | All terrain cell state (8-byte `TerrainCell`); chunk storage; `ChunkOf`/`ChunkSize`; the change-event stream; `Revision` | `GetCell`, `GetChunkCells`, `IsPassableTerrain`, `SetWall`/`ClearWall`/`SetFloor`/`ClearFloor`, `ApplyWallDamage`/`ApplyWallRepair`, `Apply` (bulk), `Snapshot`/`Restore` | `IMaterialCatalog` (read-only) | **None** |
| `TimeAuthorityManager` | `{Mode, TurnIndex, TickSequence}`; tick dispatch and ordering; the mode switch; the mutation window; `PostEncounterReconcile` | `Register`/`Unregister`, `RequestSwitch`, `ModeTransitioned`, `Advance`, `Snapshot`/`Restore`, `SwitchPending`/`PendingSwitchTarget` | `IPresentationGate` (plain C#) | **None** in core; the `TimeAuthorityRoot` autoload wrapper uses `Node._PhysicsProcess`, `ProcessMode` |
| World Change Event Bus | Synchronous dispatch of `TerrainChangeBatch` | `Publish`, `PublishWorldReloaded` | — | **None** |
| `SeededRngStore` | All RNG streams; `RootSeed`; per-stream `State` | `NextInt`/`NextDouble` via granted mode-tagged handles; `BeginEncounter`; `SnapshotInto`/`Restore` | `RootSeed` injected at composition root | **None** |
| `IMaterialCatalog` | Material definitions, tiers, HP/dig/build/yield/value, stable keys, stratum weights | `Wall(id)`, `Floor(id)`, `TryResolve`, `StableKey`, `GetStratumWeights` | — | **None** |
| Battle Checkpoint Writer | The checkpoint slot; double-buffered pooled buffers; the background write thread | `RestoredFromCheckpoint` resume path | Every state owner's `SnapshotInto` | **None** — .NET `System.IO`, `GZipStream`, `System.Threading` |

**`TerrainWorld` is the bus's only publisher, permanently.** The bus is a dumb synchronous
dispatcher: no queueing, no replay, no priorities beyond declared handler order. It never
grows a second publisher — the entity layer deliberately has no event bus and uses
`Revision` polling instead.

### Core — entity stores (ADR-0003)

| Store | Field groups | RealTime writer | TurnBased writer |
|---|---|---|---|
| `ColonistStore` | Lifecycle | Map Authoring (load window) / reconcile despawn | — |
| | Identity (`Name`, `AppearanceSeed`, `BattlesSurvived`) | Spawn; Identity Bookkeeping | frozen |
| | Position | Colonist Movement | Combat: Movement & Reachability |
| | **Health / body** (incl. `IsDowned`, `BleedOutRemaining`, `Injury`, `MobilityFactor`) | Needs & Simulation | Combat: Targeting & Resolution |
| | Needs · Job state · Squad/draft | Needs & Sim · Job Assignment · Squad Prep | frozen |
| | Skill / veterancy | — (dormant in MVP) | — |
| `RaiderStore` | Lifecycle · Position · Health · AI state | Raid Trigger (spawn) / reconcile (reaps **ALL**) | Combat Movement · Targeting · Raider AI |
| `ItemStore` | Stacks — operation-based ownership | `SpawnStack` (Excavation/Construction/Map Authoring); `ConsumeFromStack` (Construction/Repair, **reservation-gated**); `MoveStack`/`Merge`/`Split` (Stockpile & Hauling) | none in MVP |
| `DoorStore` | `Cell`, `IsOpen`, `Hp`, `IsBroken` | Construction (spawn/despawn); Colonist Movement (`IsOpen`) | Combat Movement (`IsOpen`); Combat Targeting (`Hp`) |

**Derived, never serialized, rebuilt on load**: `UnitOccupancyIndex` (single write path —
store-internal position/death handling only), `EntityDirectory`, pathfinding caches, render
data, all `Revision` counters.

**Encounter-scoped side tables** (initiative, AP, target locks) are owned by their combat
systems, never in stores, never in a colony save — and serialized **only** into the battle
checkpoint by their owners.

### Dependency graph

```
                          PLATFORM (Godot 4.7.1 / .NET 8)
                                     │
     ┌───────────────────────────────┼────────────────────────────────┐
     │                    FOUNDATION │ (plain C#, zero Godot)         │
     │   MaterialCatalog ──► TerrainWorld ──► World Change Event Bus  │
     │                            │                                   │
     │   TimeAuthorityManager ────┼──── SeededRngStore                │
     │        (mutation window, tick dispatch, mode switch)           │
     │                            │                                   │
     │              Battle Checkpoint Writer (ADR-0004)               │
     └───────────────────────────────┼────────────────────────────────┘
                                     │
     ┌───────────────────────────────┼────────────────────────────────┐
     │                          CORE │                                │
     │   Entity stores (ADR-0003) ──► UnitOccupancyIndex              │
     │            │                                                    │
     │   Pathfinding ◄── composes terrain + doors + occupancy         │
     │   Spatial Query/LOS · Job Assignment · Stockpile · Map Authoring│
     └───────────────────────────────┼────────────────────────────────┘
                                     │
     ┌───────────────────────────────┼────────────────────────────────┐
     │                       FEATURE │                                │
     │   Excavation · Construction · Needs · Destructibility · Repair │
     │   Raid Trigger ──► [MODE SWITCH] ──► Combat set (#19–23)       │
     │                          │                                      │
     │            EncounterOutcomeReport ──► one-slot inbox            │
     │                          └──► drained by PostEncounterReconcile │
     └───────────────────────────────┼────────────────────────────────┘
                                     │  reads only ▼
     ┌────────────────────────────────────────────────────────────────┐
     │                     PRESENTATION (Godot-side)                  │
     │   Terrain Rendering (2 GridMaps + sparse damage overlay)       │
     │   Entity views (bind EntityId, poll Revision)                  │
     │   Blueprint UI · Combat UI · Roster UI · Notifications         │
     │   ── submits designations/orders; NEVER writes state ──        │
     └────────────────────────────────────────────────────────────────┘
```

**The graph is acyclic.** The one design-level cycle — Combat ↔ Skill & Veterancy — is broken
by the `EncounterOutcomeReport`: Combat emits a plain data record; Veterancy consumes the
schema; neither depends on the other.

---

## 4. Data Flow

### 4.1 Frame update path

```
Godot physics frame (fixed step, 60 Hz)
  └─ TimeAuthorityRoot._PhysicsProcess(delta)      ← ONLY engine→sim entry point
       └─ TimeAuthorityManager.Advance(delta)
            │
            ├─ RealTimeAuthority                    [colony mode]
            │    accumulate; run N fixed-dt sub-steps (N = speed; 0 = paused)
            │    └─ per sub-step, open mutation window:
            │         Tick(ctx) dispatched by (TickPhase, priority, registration)
            │         Input → Simulation → Reaction → Presentation
            │
            └─ TurnBasedAuthority                   [tactics mode]
                 state machine: AwaitingInput → ResolvingAction
                                → AwaitingPresentation → NextActor
                 discrete Tick(ctx, DeltaSeconds = 0, TurnIndex++)
```

Three rules make this deterministic and they are all debug-asserted:
**speed multiplies sub-step count, never scales dt**; **`DeltaSeconds` is always 0 in
TurnBased**; **the inactive authority receives zero ticks** (true pause — never
`SceneTree.paused`).

Presentation runs on a separate axis entirely: view Nodes read stores in `_Process` and
interpolate. `Tick()` is the only sanctioned simulation path; `_Process` is the only
sanctioned presentation path.

### 4.2 Terrain change path

```
Excavation/Construction/Repair [RealTime]  ─┐
Combat: Targeting & Resolution [TurnBased] ─┼─► TerrainWorld.<mutating call>
Map Authoring / Restore [load window]      ─┘        │
                                                     ├─ validate (bulk: all-or-nothing)
                                                     ├─ apply
                                                     ├─ Revision++
                                                     └─ Publish(TerrainChangeBatch)
                                                            │ synchronous, one per call
                                                            │ ref struct — valid only
                                                            │ during Publish
        ┌──────────────┬──────────────┬─────────────┬───────┴────────┐
        ▼              ▼              ▼             ▼                ▼
   Pathfinding    Job Assignment  Terrain Render  Spatial Query   Notifications
   MarkLayerDirty flag jobs       dirty octants   invalidate LOS  queue
```

**Every handler, in every mode, is idempotent bookkeeping only** — invalidate, mark stale,
dirty. Handlers never advance state and never write stores; the mutation-window assertion
catches attempts. Handlers copy primitives out synchronously; retaining the batch is a
compile error.

Non-incremental population (load, restore) does **not** emit per-cell events — it signals
`WorldReloaded` and subscribers fully rebuild. This kills the load-time event storm and the
stale-cache-after-load bug in one rule.

### 4.3 Mode switch — the integration tax, made explicit

```
Raid Trigger.Tick() ──► RequestSwitch(TurnBased, SwitchTransitionData)
   │                         └─ framing ONLY: EncounterId, reason,
   │                            BreachCells, ParticipantIds — never entity state
   ├─ mid-dispatch? → DeferredMidDispatch; SwitchPending = true
   │     └─ same dispatch, Reaction phase, pinned order:
   │          1. Raid Trigger places raiders (exclusive cells)
   │          2. Squad Prep DECIDES colonist placements
   │          3. Colonist Movement EXECUTES them
   │             (nudge rule: ascending EntityId, each move visible to the next —
   │              evaluated against decisions already made in this pass, NOT
   │              against live occupancy)
   ├─ dispatch completes
   ├─ ModeTransitioned fires (direct manager event, ordered handlers)
   ├─ ActiveAuthority swaps — ZERO state converted, copied, or rebuilt
   └─ occupancy exclusivity assertion arms

... battle ...

Combat: Turn Order writes EncounterOutcomeReport → one-slot inbox
   └─ RequestSwitch(RealTime) ──► PostEncounterReconcile (first RealTime dispatch)
        1. drain the inbox, dispatch to consumers
        2. release reservations held by dead colonists
        3. cancel jobs targeting destroyed cells; invalidate crossing paths
        4. re-run reachability
        5. reap IsDead colonists, IsBroken doors, and ALL raiders
        6. apply injuries for the stabilized; BattlesSurvived++ for survivors
           (colonists still downed are LEFT downed — clock intact)
```

Measured cost: dispatch **0.578 µs/sub-step**, swap **0.31 µs**, reconcile **28.9 µs** once
per battle. *"No state conversion" does not mean the switch is free* — no representation
changes, but reconciliation duties are real, named, and integration-tested.

### 4.4 Save / load and the battle checkpoint

Two paths, one format family, a firewall between them.

| | **Colony save** | **Battle checkpoint** |
|---|---|---|
| When | Switch-in, battle-end, manual, autosave | Once per resolved activation, plus "activation 0" post-swap |
| Tagged | `Mode == RealTime` | `Mode == TurnBased` |
| Legal writer | Colony save writer | **Battle checkpoint writer only** (write-side enforced) |
| Combat state | **Zero, by construction** | Side tables, `RaiderStore`, TurnBased authority state, combat RNG |
| Allocation | One-shot (measured 0.61 ms / 2.01 MB) | Double-buffered pooled; `SnapshotInto` caller buffer |
| Write | Synchronous (21.9 ms) | Background thread, gzip, coalesce-newest |

```
sim thread                              background writer
──────────                              ─────────────────
snapshot → free pooled buffer   ┐
mark "newest pending"           ├──►  take newest pending
return to turn loop (~0.6 ms)   ┘     gzip (~30 KB)
                                      write temp (same volume) → fsync
                                      File.Move(temp, slot, overwrite: true)
                                      release buffer to pool
```

Load order is decided by an **in-file monotonic stamp, never filesystem mtime** (mtime is
fragile against clock changes and cloud restores). Derived state is never checkpointed —
`Restore` → `WorldReloaded` → full subscriber rebuild recovers it. Occupancy rebuild
**filters dead units**; the load path **never reaps**.

### 4.5 Initialisation order

```
1. Godot composition root  — resolve save path; produce RootSeed (OS entropy or player value)
2. MaterialCatalog         — load + validate (tier ordering, EV monotonicity); FAILS LOUDLY
3. TerrainWorld            — construct in load window (needs the catalog for the manifest remap)
4. Entity stores + EntityIdSource
5. SeededRngStore(RootSeed)
6. Derived: EntityDirectory, UnitOccupancyIndex, pathfinding regions
7. Composition root grants writer interfaces + mode-tagged RNG handles
8. TimeAuthorityManager    — register tickables; startup asserts (see §5)
9. Views bind; PublishWorldReloaded; first dispatch
```

The catalog **must** precede `TerrainWorld.Restore`, because restore remaps the save's
material manifest against the current catalog and needs it already standing.

---

## 5. API Boundaries

The contracts programmers implement against. Full signatures live in the ADRs; this is the
boundary table plus the invariants each side must respect.

### 5.1 Boundary table

| Boundary | Contract | Caller must respect | Module guarantees |
|---|---|---|---|
| **Any system → `TerrainWorld` (write)** | `SetWall`/`ClearWall`/`SetFloor`/`ClearFloor`/`ApplyWallDamage`/`ApplyWallRepair`/`Apply` | Be a legal writer for the active authority; be inside the mutation window; never call from a bus handler or UI callback | Exactly one batch published per mutating call, after state is fully applied; `Revision`++; OOB throws; no-ops publish nothing |
| **Any system → `TerrainWorld` (read)** | `GetCell`, `GetChunkCells`, `IsPassableTerrain`, `ChunkOf`, `Revision` | Never retain a span past the call; never do caller-side chunk math | Copies or read-only spans; no mutable reference into storage ever escapes |
| **Bus → subscriber** | `Publish(in TerrainChangeBatch)` | Copy primitives out synchronously; **idempotent bookkeeping only**; never write state | Batch valid for the call duration; `Previous` state captured; deterministic order |
| **System → `TimeAuthorityManager`** | `Register(system, authority, phase, priority)` | Unique (phase, priority); a system integrating `rate × DeltaSeconds` must not register TurnBased | Deterministic dispatch by (phase, priority, registration); inactive authority gets zero ticks |
| **System → entity store (write)** | Per-(system × field group) writer interface | Hold the granted interface; correct authority; correct entity kind; inside the window | Occupancy updated atomically with position; `Revision`++; wrong-kind/mode/window writes fail fast in debug |
| **Consumer → entity store (read)** | Direct read + `Revision` poll | Rescan on `Revision` change at your own cadence | Ascending-`EntityId` iteration; multi-occupant results ascending |
| **System → `IPathfinder`** | `FindPath(from, to, mover, mode, Span<CellCoord>)` | Supply the buffer; pass the right `WalkabilityMode`; hold and revalidate your own path | Allocation-free; deterministic; `Unreachable` returns an **empty** path, never a stale one |
| **System → `IReachabilityIndex`** | `IsReachable`, `MarkLayerDirty` | Never use for **combat legality** — it ignores doors and over-reports under TurnBased | Exact: same answer as authoritative A\*; ≤1 layer rebuild per dispatch; never rebuilds inside the mutation window |
| **System → `IMaterialCatalog`** | `Wall`/`Floor`/`TryResolve`/`StableKey`/`GetStratumWeights` | Treat runtime ids as session-scoped; persist only stable keys | Immutable after load — no write path exists; zero-allocation lookups; never samples |
| **System → `SeededRngStore`** | `NextInt`/`NextDouble` via a granted mode-tagged handle | Draw only inside `Tick()`/authority-driven resolution; never hold your own `PcgRng` | O(1) resume at any draw count; independent streams; allocation-free |
| **Combat → reconcile** | `EncounterOutcomeReport` → one-slot inbox | Write exactly once at battle end | Drained on first RealTime dispatch under existing (phase, priority) ordering |
| **Checkpoint writer → state owners** | `SnapshotInto(IBufferWriter<byte>)` | Fill the caller's buffer; **never** call the allocating `Snapshot()` per activation | Zero sim-thread allocation; snapshot at the `AwaitingPresentation → NextActor` beat |
| **View → anything** | Read-only | Bind by `EntityId`; poll `Revision`; **write nothing** | Stores are passive; `IsDead` + never-reused ids make "gone vs ghost" unambiguous |
| **UI → owning system** | Designations and orders | Never touch a store or `TerrainWorld` directly | The owning system's `Tick()` executes the intent |

### 5.2 Startup assertions

Run once at boot; each is a hard failure, not a warning.

| Assertion | Source |
|---|---|
| `SubStepDuration × EngineFrameRate == 1` | ADR-0001 |
| `SubStepCap ≥ 1` and `≥ max speed multiplier` | ADR-0001 |
| **`Engine.MaxPhysicsStepsPerFrame ≥ SubStepCap`** — read the **`Engine` singleton**, not `ProjectSettings` (§1) | ADR-0001 / TR-time-011 |
| `cell_octant_size == TerrainWorld.ChunkSize` (both 32) | ADR-0002 / #7 AC-3 |
| Octile heuristic matches the 10/14 cost pair | #8 §6 |
| `CriticalBreakpoint < DamagedBreakpoint` | #7 §6 |
| Catalog tier ordering + EV monotonicity | #5 C3/C5 |
| No two tickables share exact (phase, priority) | ADR-0001 |

### 5.3 CI gates

| Gate | Enforces | Status |
|---|---|---|
| Core-is-Godot-free grep | ADR-0001/0002/0003 | **Proven** with planted violations |
| No-stock-RNG grep | ADR-0005 | **Proven** |
| `RequestSwitch` call-site grep | Only Raid Trigger in, Combat out | Specified |
| `SceneTree.paused` grep | Forbidden in sim path | Specified |
| Snapshot round-trip byte-identity | Cross-cutting contract #2 | Spike-validated 24/24 |
| `TickSequence` continuity across save/load + mode cycle | ADR-0001 criterion 2 | Spike-validated |
| Zero-allocation benchmarks | Measured standard | Spike-validated |

---

## 6. Architecture Principles

**1. The simulation is plain C# and owes the engine nothing.**
Zero Godot references in `src/core`, CI-enforced. This buys headless tests with no engine
installed, immunity to every post-cutoff breaking change, and a save format not entangled
with the scene tree. It is the single highest-leverage decision in the project.

**2. One writer per field group per authority, granted at one place.**
Not convention — narrow interfaces handed out at the composition root, so a system physically
cannot call setters it does not own, backed by window/mode/kind assertions at runtime.
Ownership review happens in one reviewable file.

**3. One shared world; the mode switch swaps *who may write*, not *what exists*.**
Zero state conversion, proven by identity: across the swap the store is the same instance with
the same values and an unchanged `Revision`. This is what makes "your base IS the tactics map"
literally true rather than a simulation of itself.

**4. Ticking advances state; everything else is bookkeeping.**
`Tick()` is the only sanctioned simulation path. `_Process` is presentation-only. Bus handlers
invalidate and dirty, never advance. Game speed multiplies sub-step count, never scales dt.

**5. Derived state is rebuilt, never serialized; references are stable ids only.**
Occupancy, directories, caches, render data and `Revision` counters all reconstruct on load.
Cross-object references are `EntityId` and stable material keys. Proven: wiping all derived
state and re-saving produces a byte-identical file.

**6. Measure, then decide — and treat regressions as bugs.**
Chunk size, render backend, AoS-vs-SoA, allocation behaviour and frame time were all settled
by spike measurement, and one measurement (AoS) falsified the ADR's own concession in the good
direction. Zero steady-state allocation is a *measured, enforced standard*, not an aspiration.

---

## 7. ADR Audit

### 7.1 Quality check

| ADR | Status | Engine Compat | Version pinned | GDD linkage | Conflicts | Valid |
|---|---|---|---|---|---|---|
| ADR-0001 Time Authority | **Accepted** | ✅ | ✅ 4.7.1 | ✅ | None | ✅ |
| ADR-0002 Terrain Data Model | **Accepted** | ✅ | ✅ 4.7.1 | ✅ | None | ✅ |
| ADR-0003 Entity Data Ownership | **Accepted** | ✅ | ✅ 4.7.1 | ✅ | None | ✅ |
| ADR-0004 Battle Checkpoint | **Proposed** | ✅ | ✅ 4.7.1 | ✅ | None | ⚠️ gated |
| ADR-0005 Seeded RNG | **Proposed** | ✅ | ✅ 4.7.1 | ✅ | **1 open** | ⚠️ gated |

**5/5 have Engine Compatibility sections, all pin 4.7.1, all declare "Post-Cutoff APIs Used:
None" — and §1 now verifies that claim rather than trusting it.**

### 7.2 Dependency order — acyclic

```
ADR-0001 Time Authority        [Accepted]  ── foundation
   ├─► ADR-0002 Terrain        [Accepted]
   ├─► ADR-0003 Entity         [Accepted]
   └─► ADR-0004 Checkpoint     [Proposed] ◄──► ADR-0005 Seeded RNG [Proposed]
                                    co-dependent on the combat-group
                                    boundary — PROMOTE TOGETHER
```

The ADR-0003-Accepted-depends-on-ADR-0002-Proposed **status inversion is resolved** — ADR-0002
was promoted 2026-08-24.

### 7.3 The one remaining promotion gate

ADR-0004 and ADR-0005 are both Proposed behind a **single** gate: checkpoint snapshot+write
measured at per-activation combat cadence on the double-buffered async path, confirming no
frame-time impact.

**That path does not exist yet.** This was a genuine circular block — ADR-0002 couldn't be
Accepted without the measurement → the measurement needs ADR-0004's path → building it is a
story → stories referencing a Proposed ADR are auto-blocked. The 2026-08-24 split broke the
circle by moving the clause to ADR-0004 where the risk actually lives, and promoting ADR-0002
on its (fully measured) terrain clauses.

**The circle is not fully broken for ADR-0004 itself.** Building the async path is still work
that references a Proposed ADR. **Recommendation: build it as a spike under `prototypes/`,
ADR-exempt like the five Tier 0 spikes** — the precedent exists and it is the cheapest exit.

### 7.4 Traceability

97 requirements: **77 covered (79%), 20 partial (21%), 0 gaps.** The last gap (TR-time-025)
closed with ADR-0005.

Partial rows are **not defects** — the foundation hook exists and the consuming quick-spec is
simply unwritten, exactly as the tiered-doc plan intends. Grouped by owner: Excavation (6),
Notifications (2), Map Authoring (2), Terrain Rendering (4), Combat set (1), Pathfinding (1),
UX specs (2), engine gates (2).

**Two rows this document's §1 changes:**

| Row | Was | Now |
|---|---|---|
| **TR-time-011** engine step-clamp guard | ⚠️ "engine setting unverified in 4.7.1" | **Verifiable** — name/default confirmed, read-API answered. Ready to close when the guard is built against `Engine.MaxPhysicsStepsPerFrame` |
| **TR-terrain-044** pre-render-backend engine gate | ⚠️ "`gridmap.md` not authored" | **Half closed** — the reference doc now exists; the damage-overlay draw-call *measurement* is still owed |

### 7.5 The one open conflict

**TR-time-026 / TR-time-027 vs the 2026-08-24 save-scum ruling.** ADR-0005 derives the combat
stream from `splitmix64(RootSeed, Combat, EncounterId)` precisely so a reload reproduces the
same encounter. The user ruling requires the encounter to **re-roll within its threat band on
reload**, so pre-reveal scouting cannot be exploited (CD-15).

**RESOLVED 2026-08-26 — ADR-0005 Amendment 2026-08-26.** The contradiction was narrower than
this section claimed: one sentence covered two different properties. **Resume determinism**
(`TR-time-025`/AC-67 — Battle Persistence) restores the combat stream's `State` **directly** and
never re-derives, so it was never affected. Only **cross-save re-derivation**, which happens in
`BeginEncounter` at battle start, is the exploit — and closing it is what the ruling asks for.

Resolution: `splitmix64(RootSeed, Combat-key, EncounterId, EncounterAttempt)`, with the attempt
counter in a `user://` profile file — **never the colony save**, since a counter inside the save
restores with it and re-rolls nothing. `TR-time-026` is **not** weakened: `EncounterAttempt`
joins `RootSeed` as a declared determinism input. **No schedule cost, no lost guarantee.**
QQ-01 is closed; this no longer blocks ADR-0005 promotion.

---

## 8. Open Questions Register

Consolidated from all ADRs, quick-specs, GDDs and reviews. **This register is one of the two
genuinely new outputs of this document.**

### Blocking — resolve before the dependent work starts

| ID | Question | Owner | Blocks |
|---|---|---|---|
| ~~QQ-01~~ | ~~Combat RNG re-roll vs identical replay (§7.5)~~ — **CLOSED 2026-08-26** by ADR-0005 Amendment 2026-08-26. No longer blocks ADR-0005 promotion | technical-director + #18 | — |
| **QQ-02** | Build ADR-0004's async checkpoint path to measure it — recommend an ADR-exempt spike | technical-director | ADR-0004 **and** ADR-0005 promotion |
| **QQ-03** | Research/Technology, production chain, furniture — three systems with no index entry, required by the prosthetics ruling | producer + game-designer | Construction #16; next `/scope-check` |
| **QQ-04** | Is reinforced mined or manufactured? Decides whether `BuildCost` is a scalar or a recipe | Construction #16 + creative-director | #16 authoring |
| ~~QQ-23~~ | ~~Bus subscriber registration has no interface~~ — **CLOSED 2026-08-25 by ADR-0006** (Proposed). Defines `ITerrainChangeSubscriber`, priority-ordered registration, the deferred-purge and exception-isolation rules, and records the C#-12 ref-struct trap so the illegal idiom is not retried. Reaches ✅ when ADR-0006 is Accepted | technical-director | — |
| **QQ-24** | **The composition root has no type definition.** Referenced **30 times** across 5 documents as the single review point for every writer grant, RNG handle and buffer pool — and defined nowhere. It is the object the entire one-writer guarantee rests on | technical-director | First implementation of any store |
| **QQ-25** | **Checkpoint multi-owner buffer framing is unspecified.** ADR-0004 §1 has 7 independently-owned content items all writing into one coalesced pooled buffer via `SnapshotInto`, with no ordering, length-prefixing/TLV, or per-section schema versioning defined — so `Restore` cannot find each owner's section | technical-director | ADR-0004 + ADR-0005 implementation |
| **QQ-26** | **Checkpoint writer lock boundary + join timeout.** ADR-0004 requires "the sim never waits" but does not pin the lock to the buffer handoff only (never across gzip/write/fsync); the obvious implementation silently breaks the requirement. No timeout on battle-end quiesce or quit-path join — a stalled disk hangs the process | technical-director | ADR-0004 implementation |

### Non-blocking — resolve at the named trigger

| ID | Question | Owner | Trigger |
|---|---|---|---|
| QQ-05 | Damage-overlay draw-call measurement (TR-terrain-044 / #7 AC-10) | #7 spike | Before render backend is declared settled |
| QQ-06 | Cutaway depth-cue strength — tune by eye; shader reading world height | #7 | During implementation |
| QQ-07 | Post-battle time semantics: does colony time advance by battle duration? | creative-director | Before the Needs GDD |
| QQ-08 | Art bible §3.1/3.3 re-validation — unblocked by the camera decision | art-director | Now |
| QQ-09 | Lighting features for the colony vibe — torches, hearths, claimed-area glow. **A feature set to build, not a constraint** | art-director + technical-artist | Art/lighting pass |
| QQ-10 | Style-variety ceiling at Vertical Slice (~8 variants/tier) | art-director + TD | Art bible palette spec |
| QQ-11 | TurnBased occupancy: blocks traversal or only end-of-move? | Combat: Movement #21 | Combat set authoring |
| QQ-12 | Survivability floor — downed colonists targetable + ~10 roster vs CD-3 | #18 + #23 | Combat set authoring |
| QQ-13 | `Medic` as a `SquadRole`; if roles stay uniform, anyone can stabilize | Squad Prep #24 | Before #24 |
| QQ-14 | Colony-mode injury sources (MVP has none) | Needs #13 | At #13 |
| QQ-15 | Tending as a 6th job type vs the "~5 job types" note | Job Assignment #10 | At #10 |
| QQ-16 | Beds/furniture for bed-rest recovery | Construction #16 | At #16 |
| QQ-17 | Colony manual saves still allow pre-raid reload — the honest carve-out | creative-director | Before Save/Load #6 |
| QQ-18 | `physics_jitter_fix` → 0 for a determinism-critical fixed-step sim? | technical-director | When `TimeAuthorityRoot` is built |
| QQ-19 | Steam Cloud: exclude the rolling checkpoint slot (150–300 writes/battle) | release setup | Release configuration |
| QQ-20 | Editor hot-reload can tear down a background write mid-flight | Save/Load #6 | Composition-root wiring |
| QQ-21 | Return-to-menu mid-battle must also join the checkpoint writer | Save/Load #6 | At #6 |
| QQ-27 | **Input has MEDIUM engine risk and no owner.** Godot 4.6's dual-focus system (mouse/touch focus separate from keyboard/gamepad focus) and 4.5's SDL3 gamepad driver are post-cutoff and unexercised, while all three UX specs are unwritten. #26 carries the project's highest accessibility load | ux-designer + technical-director | Before the first UX spec (#26) |
| QQ-22 | Capacity assumption unconfirmed — history reads part-time (15 commit-days / 32 calendar) | producer + user | Next `/sprint-plan` |

---

## 9. Risk Register

| Risk | Severity | Mitigation | Status |
|---|---|---|---|
| Compile-time writer-interface segregation asserted by design, **not yet built** | **HIGH** | ADR-0003 criterion 1's carried obligation — spikes used narrow methods, not the interface set. Runtime assertions only until the production composition root delivers it | **Open — first implementation obligation** |
| ADR-0004/0005 blocked behind an unbuilt async path | **HIGH** | QQ-02 — build as an ADR-exempt spike | Open |
| RNG re-roll contradiction | **MEDIUM** | QQ-01 amendment | Open |
| Combat set is half the game's identity and entirely unwritten (5 GDDs) | **MEDIUM** | Split into 5 entries; fun spike already validated the loop | By design |
| God-object growth in Terrain / Colonist | **MEDIUM** | Firewall tables name an owner for every adjacent concern; six-month review criteria | Controlled |
| Cell-struct / store field creep | **MEDIUM** | Forbidden-pattern list; "meaningless outside an encounter ⇒ side table" | Controlled |
| Composition-root grant drift | **MEDIUM** | One reviewable file; debug-console grant audit sweep | Controlled |
| Checkpoint buffer framing undefined (QQ-25) | **HIGH** | 7 owners share one buffer with no container format — blocks ADR-0004/0005 implementation entirely | **Open — LP-FEASIBILITY finding** |
| Composition root undefined (QQ-24) | **HIGH** | 30 references, 0 definitions; the one-writer guarantee depends on it | **Open — LP-FEASIBILITY finding** |
| Bus registration + C#-12 ref-struct trap (QQ-23) | **MEDIUM → addressed** | `TerrainChangeBatch` cannot be a generic type argument on net8.0/C# 12. **ADR-0006 (Proposed) resolves this** and corrects a further error: a .NET 9 upgrade would NOT lift it — `allows ref struct` is opt-in per declaration, and `List<T>` can never qualify by CLR rule | **Addressed, pending ADR-0006 acceptance** |
| Zero-allocation CI gate covers only spiked systems | **MEDIUM** | Evidence is real but spans the 5 Tier 0 spikes; the ~20 unwritten Feature-layer systems are where LINQ/boxing creeps in. Extend the gate as each system lands rather than assuming inheritance | Open |
| Writer-interface count without a growth trigger | **MEDIUM** | ~20–25 at MVP (practical); trends to 40–50+ once QQ-03's three systems land. Every other scaling risk in this document has an explicit trigger; this one does not | Open — see §11 |
| Revision-polling rescan cost | **LOW** | Measured 63.2 µs/dig at MVP caps; narrow change-list upgrade pre-planned at ~5× growth | Monitored |
| Whole-frame budget with entities/VFX/UI unmeasured | **LOW** | Terrain has ~8× headroom (p99 2.02–2.17 ms of 16.6 ms) | Monitored |

---

## 10. Measured Baselines

Everything below is measured, not estimated. Regressions against these are **bugs**.

| Measure | Value | Budget | Source |
|---|---|---|---|
| Frame-time p99 (terrain, 8 digs/frame) | **2.167 ms** Vulkan / **2.024 ms** D3D12 | 16.6 ms | Target hardware 2026-08-24 |
| GC collections, 1800 frames | **0** Gen0/1/2 | 0 | Target hardware |
| Allocation per frame | 32.7–36.1 B | ~0 | Target hardware |
| Draw calls, 3-layer cutaway | **32** | ≤150 terrain / ≤500 frame | Target hardware |
| Terrain render buffers | 16.23 MB | ≤20 MB | Target hardware |
| Cell data, MVP / full-vision | 2.00 MB / 16.00 MB | — | Terrain spike |
| Tick dispatch | 0.578 µs/sub-step | 16.6 ms frame | Mode-switch spike |
| Mode swap | 0.31 µs | — | Mode-switch spike |
| `PostEncounterReconcile` | 28.9 µs once/battle | — | Mode-switch spike |
| A\* long path (126 cells) | 118.9 µs | ≤200 µs | Pathfinding spike |
| Single-layer region rebuild | ~0.26 ms | ≤0.5 ms | Pathfinding spike |
| Full-map walkability sweep | 0.290 ms | ≤0.5 ms | Terrain spike |
| Save / gzip / write | 2.01 MB → 30 KB, 21.9 ms | — | Save/load spike |
| Style-variety draw calls | 1→32, 2→48, 4→80, 8→144, 16→272 | ≤150 | Terrain spike |

**Spike verdicts: 5/5 Tier 0 complete** — fun PROCEED · terrain ✅ · mode-switch 61/61 ·
pathfinding 44/44 · save/load 24/24.

---

## 11. Gate Review Record — 2026-08-25

Review mode **full**, so both Technical Setup gates ran. Recorded permanently because the
findings are conditions on the sign-off, not passing commentary.

### TD-ARCHITECTURE (technical-director self-review) — APPROVED WITH CONDITIONS

| Criterion | Verdict | Finding |
|---|---|---|
| 1 — Every baseline requirement covered by a decision | CONCERNS | 77/97 covered, 20 partial (expected under the tiered-doc plan), 0 gaps. **But TR-time-026/027 have *known-wrong* coverage**, not merely deferred: ADR-0005 guarantees identical replay; the save-scum ruling requires a re-roll. Tracked as QQ-01 |
| 2 — HIGH-risk engine domains addressed or flagged | CONCERNS | GridMap and the physics settings both closed by the 2026-08-25 verification (§1). **Input was rated MEDIUM with no owner and no open question** while all three UX specs are unwritten — now QQ-27 |
| 3 — API boundaries clean, minimal, implementable | CONCERNS | §5 is a sound *invariant checklist* but defers signatures. Real signatures exist for `TerrainWorld`, `IPathfinder`, `IReachabilityIndex`, `IMaterialCatalog`, `IPresentationGate`, `SeededRngStore`. **The writer interfaces exist nowhere** |
| 4 — Foundation-layer ADR gaps resolved before implementation | **FAIL** | Two Foundation ADRs (0004, 0005) are **Proposed**, and `docs/CLAUDE.md` auto-blocks stories referencing a Proposed ADR. ADR-0003 criterion 1 is an unbuilt obligation. The QQ-02 spike is a *recommendation*, not a resolved gap |

### LP-FEASIBILITY (lead-programmer, spawned) — CONCERNS

> *"Nothing in this document is unimplementable with Godot 4.7.1/C# .NET 8 … The concerns are
> real but all fixable before coding starts, mostly by writing down interfaces that are
> currently only implied by prose."*

Three findings the TD self-review missed, **all four load-bearing claims independently
verified against the repository before acceptance**:

1. **Checkpoint multi-owner buffer framing** (QQ-25) — 7 owners, one buffer, no container
   format. Blocks ADR-0004/0005 implementation outright.
2. **C# 12 ref-struct constraint** (QQ-23) — **verified: the csproj targets `net8.0`**, so
   `allows ref struct` (C# 13/.NET 9) is unavailable and `TerrainChangeBatch` **cannot be a
   generic type argument**. `Action<T>`, `EventHandler<T>`, `IObserver<T>` and
   `List<Action<T>>` are all illegal for it. The natural first idiom for bus registration
   will not compile.
3. **Checkpoint lock boundary + join timeout** (QQ-26) — "the sim never waits" is stated but
   not guaranteed; the obvious implementation holds the lock across the I/O.

Also verified: **"composition root" appears 30 times across 5 documents with zero type
definitions** (QQ-24); `IReservationOracle` is named once with no signature;
`terrain-rendering-cutaway.md:125` carries a literal `OnTerrainChanged(/* batch */)`
placeholder.

**Clean bills of health worth recording** (the LP checked and cleared these rather than
manufacturing concerns): the readonly-struct mutation window is sound on all exit paths;
the double-buffer coalesce-newest protocol is correct with exactly 2 buffers; the writer
thread correctly never touches a Godot API off the main thread; and the three deliberate
non-idiomatic patterns (`Revision`-polling over an event bus, pull-based view diffing over
signals, hand-rolled non-generic dispatch) are *documented, justified and reversible* —
"the opposite of technical debt."

The LP self-corrected mid-review, initially flagging `IPathfinder`/`IReachabilityIndex`/
`IPresentationGate` as missing before finding all three fully defined. Recorded because it
raises confidence in the findings that survived.

### One recommendation accepted as a note, not a change

The writer-interface set is ~20–25 at MVP (practical, not a "zoo") but trends toward 40–50+
once QQ-03's three new systems land. The LP proposes a capability-token pattern
(`WriteHealth(in WriterToken<Colonist.Health> token, …)`) — the same collapse ADR-0005
already uses for RNG streams. **No change now**: the current design is fine at MVP scale.
The actual gap is that this is the only scaling risk in the project without an explicit
adoption trigger, while pathfinding has its 5× rule and style variety has its 8-variant
ceiling. Logged in §9; a trigger should be set when QQ-03 is decided.
