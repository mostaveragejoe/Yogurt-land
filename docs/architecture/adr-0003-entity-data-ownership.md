# ADR-0003: Entity Data Ownership

## Status
Proposed

*(Written per the systems-index sequencing: authored as Proposed before the Tier 0 spikes; promoted to Accepted when the pathfinding and save/load spikes validate the occupancy index under mid-route digs and the entity-store round-trip. This ADR fixes contracts and ownership; per-kind field details beyond the MVP core are GDD/quick-spec material.)*

## Date
2026-07-24

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 |
| **Domain** | Core |
| **Knowledge Risk** | HIGH (4.7 is post-cutoff) — mitigated: all entity stores are plain C# with zero Godot dependency; no post-cutoff API is load-bearing. The one engine-adjacent note inherited from ADR-0001: Jolt is the 3D physics default since 4.6 — irrelevant here because unit movement is cell-to-cell on the tile grid, never physics-body-driven |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md`, `deprecated-apis.md`, `current-best-practices.md`, `modules/physics.md` (Jolt cross-check routed here by ADR-0001) |
| **Post-Cutoff APIs Used** | None. Entity *views* (Godot nodes reading stores for presentation) touch whatever rendering/UI APIs their specs choose — decided in those specs, not here. Marshalling boundary rule: `EntityId.Value` is declared `long` (not `ulong`) so ids cross the Godot C# Variant boundary without conversion — same precedent as ADR-0002's span/ref-struct marshalling note |
| **Verification Required** | Pathfinding spike: occupancy index + door passability + Revision-polling invalidation correct under mid-route digs and door state changes. Save/load spike: entity-store snapshot round-trip with stable `EntityId`s; entity-layer Gen0 measured alongside ADR-0002's allocation gates. Mode-switch spike: pre-switch placement normalization and PostEncounterReconcile against real stores |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001 Time Authority (Proposed) — writes only inside authority-driven execution; per-entity sim state is plain data (Nodes are views); `SwitchTransitionData.ParticipantIds`; `PostEncounterReconcile`. ADR-0002 Terrain Data Model (Proposed) — `CellCoord`/`EntityId` primitives; the firewall table that pre-assigns entity-layer ownership; `IsPassableTerrain` as terrain's contribution to composite walkability |
| **Enables** | Colonist Entity & Attributes quick-spec; Job Assignment + Needs & Simulation GDD; Stockpile & Hauling quick-spec; Combat set GDDs (#19–#23); Squad Preparation quick-spec; Raid Trigger GDD; Pathfinding quick-spec (composite walkability inputs defined here); Seeded RNG ADR (per-system streams touch entity spawn) |
| **Blocks** | Tier 0 pathfinding spike (needs occupancy + door contracts), save/load spike (needs entity snapshot contract), mode-switch spike (needs participant/normalization/reconcile contracts); every story touching colonist, raider, item, or door state |
| **Ordering Note** | Third and last Foundation ADR before the contracts annex and the spikes. Written against ADR-0001/0002 as Proposed — if a spike falsifies either, this ADR is re-checked at the same revision point. The Colonist Entity quick-spec (#9) authors the *player-facing attribute list* later; this ADR fixes who may write which field group, not the full field list |

## Context

### Problem Statement
Colonist Entity is the second-highest-fan-out system in Hollowdeep (with Terrain, one of "the two highest-fan-out systems — their contracts gate everything downstream", systems-index Overview). Seven systems read or write colonist state; raiders, item stacks, and doors add three more entity kinds with their own writers. The genre's classic failure modes live exactly here: two systems writing health with different rules, hauling and jobs disagreeing about reservations, occupancy diverging from positions, combat state leaking into colony saves. The systems index mandates a **write-ownership table** — each field group has exactly one writer — and routes two named conflicts to this ADR: **health write-arbitration (Combat vs Needs)** and the **Combat ↔ Skill & Veterancy circular dependency**. ADR-0002's firewall table additionally defers here: the **door-vs-wall boundary**, the **mode-agnostic occupancy index**, and **item/stack reservation ownership**. This ADR fixes the entity data architecture and all four ownership boundaries — the contract the spikes implement and the GDDs elaborate.

### Constraints
- ADR-0001: per-entity simulation state is plain data; Godot Nodes are views only (the forbidden-pattern list points its "detail" here); state advances only inside authority-driven execution; bus handlers are idempotent bookkeeping; `SwitchTransitionData` carries encounter framing only (`ParticipantIds` — never entity state); `PostEncounterReconcile` must release dead colonists' reservations, cancel orphaned jobs, and leave zero orphans (integration-tested)
- ADR-0002: `TerrainCell` never describes occupants — occupancy, reservations, zones live with entity-layer owners; the terrain-job-claim bit's who/why table is Job Assignment's; `EntityId` lives in `Hollowdeep.Core.Primitives`; composite walkability (terrain + doors + occupancy) is composed by Pathfinding
- Serialization contract (cross-cutting #2): plain data, `Snapshot()`/`Restore()`, cross-object references by stable ID only, derived state never serialized, CI round-trip
- CD-9 (locked): no mid-battle saves — combat-transient state is never serialized
- CD-4 (locked): MVP identity surface = persistent name + deterministic appearance seed + battles-survived counter + named death notification with location
- MVP scope caps: ~10 colonists, ONE raider type, 3 needs, ~5 job types, fixed squad loadouts (equipment deferred to VS)
- Solo developer: the simplest structure that enforces ownership wins; no speculative generality

### Requirements
- Every entity field group has exactly one writer per time authority, recorded in one table (systems-index #9 mandate)
- Health arbitration and the Combat↔Veterancy cycle resolved structurally, not by convention alone
- A mode-agnostic occupancy answer to "which unit is standing on this cell?" — exclusive under TurnBased (tactics legality), tolerant under RealTime (colony traffic)
- Doors: block like walls, carry entity state, participate in composite walkability — without touching `TerrainCell`
- Item stacks: spawn (dig yields), consume (build/repair costs), move (hauling), reserve (hauling) — with reservation ownership fixed
- Stable `EntityId`s across save/load; deterministic iteration everywhere; zero Godot dependency; headless unit tests
- Downstream consumers (Pathfinding, views, job targeting) can cheaply detect entity-layer change — without an entity event bus
- Combat reads colonists and raiders uniformly as encounter participants without either store depending on combat

## Decision

Adopt **typed plain-C# stores per entity kind** — `ColonistStore`, `RaiderStore`, `ItemStore`, `DoorStore` — each a dense, deterministic collection of plain records keyed by `EntityId`, with **write access segregated by narrow writer interfaces** handed out at the composition root, one per (system × field group). A **write-ownership table** (below) is the single authority on who writes what under which time authority. Zero Godot dependency; Nodes are views binding by `EntityId`.

### Shared primitives

```csharp
// Namespace: Hollowdeep.Core.Primitives (established by ADR-0001/0002 companion correction)

public readonly record struct EntityId(long Value);
// 0 = None. Declared long (not ulong) so ids cross the Godot C# Variant boundary
// without conversion; a monotonic counter never approaches long.MaxValue.
// Allocated by EntityIdSource: monotonic per world starting at 1, NEVER reused;
// the counter itself is serialized, which is the entire non-reuse guarantee.
// A despawned id resolves to nothing forever after — dangling references
// (a notification naming a dead colonist) are legal and callers handle "gone".
// The stable cross-object reference mandated by the serialization contract.
// One id space across ALL entity kinds — a participant list, reservation table,
// or notification can reference any entity without kind-tagged unions.
```

An `EntityDirectory` (entity-layer module) maps `EntityId → EntityKind` for cross-kind lookups (e.g. resolving a mixed participant list) and backs the per-store kind assertion below. It is derived bookkeeping, rebuilt on load, never serialized.

**One id space is a trade, not a free win**: nothing at the type level stops a raider id reaching `IColonistHealthWriter` or a stack id reaching the occupancy index. Mitigation (part of the contract): **every store write debug-asserts the id resolves to its own kind** via `EntityDirectory` — wrong-kind writes fail fast in debug, and the headless suite covers it (validation criterion 1).

### Store shape — one pattern, four MVP instances

Each store is a plain-C# class owning:
- a dense list of per-entity records — **struct / record-struct elements by default** (godot-specialist note: Godot's embedded .NET GC runs on the main thread; heap-allocated per-entity records would make encounter-scoped raider spawn/despawn a per-battle garbage source — trivial at MVP scale, but the wrong precedent). Iterated **in ascending `EntityId` order** (determinism rule, matching ADR-0002 rule 8);
- **stable ordered removal**: `Despawn` compacts the list preserving ascending-id order (O(n) is irrelevant at the entity caps) — so deterministic iteration AND byte-stable snapshots hold by construction; no swap-remove, no free-list. Id→slot resolution is **binary search over the sorted dense list** — allocation-free; a `Dictionary<EntityId,int>` would rehash on despawn and quietly contradict the allocation policy;
- lifecycle: `Spawn(...) → EntityId` / `Despawn(EntityId)` — legal callers per the ownership table; a despawned id is never reused (serialized `EntityIdSource` counter);
- `Snapshot()` / `Restore(snapshot)` with a schema version (serialization contract);
- a monotonic **`Revision`** (see change notification, below);
- read access for everyone; **write access only through writer interfaces** (below).

**Stores are passive — they never tick** (ADR-0002 rule 9, applied verbatim to entities): no store, nor `UnitOccupancyIndex`, nor `EntityDirectory` registers with any time authority or has a `Tick()`. Their "Behavior under each time authority" is: *identical inert store; only the legal writer set changes* (the ownership table's authority columns). The debug invariant sweeps named in this ADR (occupancy ≡ positions; reservation consistency; composition-root grants) are owned and scheduled by the **Tier 0 debug console (#29)** — the same home as ADR-0002's reservation-bit sweep. Per-frame view diffing runs in presentation `_Process` and is not simulation.

New entity kinds (furniture, workshops, traps — GDD-era) follow the same recipe: typed store + ownership-table rows + snapshot + view binding. There is deliberately **no generic "misc entity" store** — a kind without a table row cannot exist, which is the anti-God-object guard for the entity layer.

### Write enforcement — interface segregation + the ADR-0001 window

Two mechanisms, compile-time and runtime:

1. **Writer interfaces**: each store's mutating surface is split per field group (e.g. `IColonistHealthWriter`, `IColonistNeedsWriter`, `IColonistJobStateWriter`, `IColonistMovementWriter`). The composition root hands each system exactly the interfaces the ownership table grants it — a system physically lacks a reference to setters it doesn't own. Ownership review happens in ONE file (the composition root), not by auditing call sites.
2. **Mutation-window assertion**: every store write debug-asserts ADR-0001's mutation window is open (authority dispatch or load window) — the same mechanism ADR-0002 rule 5 uses, catching UI-callback and bus-handler writes. Where the table splits one field group's writer by authority (health, position), the writer interface is authority-tagged and the store debug-asserts the active `Mode` matches. Every write additionally kind-asserts its id (above).

### Entity-layer change notification (no entity event bus — deliberate)

The World Change Event Bus keeps its ADR-0002 cap: **Terrain is its only publisher, ever**. The entity layer's replacement contract is deliberately cheaper:

- Every store and `UnitOccupancyIndex` exposes a monotonic **`Revision`** (`long`, +1 per mutating call, +1 on `Restore`; runtime-only, never serialized) — the same O(1) "did anything change?" primitive as `TerrainWorld.Revision`.
- **Consumers poll `Revision` at their own cadence** (Pathfinding per dispatch, views per frame) and rescan what they cache. There are no entity change events, no subscriber lists, no handler ordering questions.
- This is sufficient at MVP scale by construction: door count is dozens (full door rescan on `DoorStore.Revision` change is trivially in-budget), unit count is ~20, and view diffing is already a per-frame scan. **If a spike falsifies a rescan** (the pathfinding spike measures door-invalidation cost explicitly), the falsified store gains a narrow per-dispatch change list behind the same facade — an internal upgrade, not a contract break.

### The write-ownership table (the core deliverable)

**Colonist field groups** (`ColonistStore`):

| Field group | MVP fields | Writer under RealTime | Writer under TurnBased | Notes |
|---|---|---|---|---|
| Lifecycle | spawn; despawn | Spawn: Map Authoring embark path (load window). Despawn: PostEncounterReconcile only (reaps `IsDead` colonists after the outcome report is consumed) | — | Colonists are NEVER despawned inside an encounter — the corpse stays addressable for the outcome report and CD-4's death-cell notification. Colony-mode death (if the Needs GDD introduces it) reaps via the same RealTime reconcile-style pass, decided there |
| Identity | `Name`, `AppearanceSeed`, `BattlesSurvived` | Spawn sets name/seed once; `BattlesSurvived` incremented only by Identity Bookkeeping (a small entity-layer module registered to the reconcile pass — the named MVP writer; index #31's full Identity & Memory system remains VS) | — (frozen) | CD-4 surface. CD-4's "battles-survived counter (Roster UI)" means Roster UI *displays* it — UI never writes stores. `AppearanceSeed` is data → deterministic visuals; views derive, never store |
| Position | `Cell`, movement progress/facing | Colonist Movement (the job-execution mover) — including executing Squad Prep's pre-switch placement orders (see occupancy) | Combat: Movement & Reachability | BOTH paths write through the store's single position setter, which updates the occupancy index atomically (below) |
| Health / body | `Hp`, `IsDead` flag (wound detail is GDD-era) | Needs & Simulation (decay, rest, recovery) | Combat: Targeting & Resolution (damage) | **The decided arbitration**: writer-per-authority, mirroring ADR-0002 rule 4. One legal writer at any moment; authority-tagged interfaces + mode assertion enforce it. Neither system ever calls the other. `Hp`→0 sets `IsDead` via the same lethal write — no separate death writer; the store internally removes dead units from the occupancy index (dead units do not block cells) |
| Needs | 3 need values (food, sleep, work per concept cap) | Needs & Simulation | — (frozen, per ADR-0001 worked example) | |
| Job state | `CurrentJobId`, claim backrefs | Job Assignment | — (frozen) | Pairs with Job Assignment's terrain-claim who/why table (ADR-0002 Flags bit) |
| Squad / draft | `SquadRole`, drafted flag | Squad Preparation | — (frozen — roster locked at switch) | `SwitchTransitionData.ParticipantIds` is assembled jointly: Squad Prep's drafted roster + Raid Trigger's spawned raiders; still framing, not state |
| Skill / veterancy | Fields land in MVP save format, dormant (index #30/#31 note) | — (init only) | — | VS-era: **Veterancy is sole writer**, consuming the Encounter-Outcome Report — the Combat↔Veterancy cycle resolution. Combat NEVER writes skills |

**Combat-transient state is NOT colonist state.** Initiative, turn order position, action points, selected targets, overwatch flags — everything that exists only during an encounter — lives in **encounter-scoped side tables owned by the combat systems that rule them** (Turn Order owns initiative/order; Action Economy owns AP; Targeting owns target locks), keyed by `EntityId`, created at switch-in from store reads, discarded at battle end. They are never serialized — this is what makes CD-9's "no combat serialization" scope-shrink structural: the colonist store snapshot simply never contained battle state. The firewall mirrors ADR-0002's: **any proposed `ColonistStore` field that is meaningless outside an encounter belongs in a combat side table.**

**Raider field groups** (`RaiderStore`):

| Field group | Writer under RealTime | Writer under TurnBased | Notes |
|---|---|---|---|
| Lifecycle (spawn/despawn) | Spawn: Raid Trigger (spawns the raid at the breach, pre-switch, at exclusively-placed cells — its placement duty, asserted). Despawn: PostEncounterReconcile despawns **ALL** raiders — *corrected 2026-07-26 by the mode-switch spike; see Spike Results* (first RealTime dispatch after the swap, per ADR-0001 step 4) | — | Raiders are encounter-scoped in MVP (single encounter at a time, ADR-0001); corpse/loot representation routed to Raid Trigger GDD |
| Position | — (raiders do not act in colony time in MVP) | Combat: Movement & Reachability | Same single position setter + occupancy update |
| Health | — | Combat: Targeting & Resolution | Same authority-tagged writer as colonist health; `Hp`→0 sets `IsDead`, occupancy released |
| AI state (objective, satisfaction/withdraw meter per CD-3) | Raid Trigger (initial objective) | Combat: Raider Decision-Making | CD-3's withdraw condition is raider state, owned by its decision system |

**Item stacks** (`ItemStore`) — operation-based ownership, because one "quantity" field legitimately has multiple mutating *operations* with different owners:

| Operation | Legal caller | Authority | Notes |
|---|---|---|---|
| `SpawnStack(material, qty, cell)` | Excavation (dig yields), Construction (refunds on cancel); Map Authoring (initial stocks, load window) | RealTime / load window | |
| `ConsumeFromStack(id, qty)` | Construction, Repair & Rebuild (CD-7 costs) | RealTime | **Reservation-gated**: consume requires a live reservation held by the consuming job. This is the invariant that makes two legal callers safe (the index's "top genre bug class", answered structurally). A stack reaching quantity 0 is **despawned by the same operation that emptied it** (RealTime only; no encounter case in MVP) |
| `MoveStack(id, dest)` / `MergeStacks` / `SplitStack` | Stockpile & Hauling only | RealTime | |
| Combat-era item mutation | **None in MVP** (no ammo/consumables with fixed loadouts) | — | Adding one later adds a table row, not a redesign |

**Stack reservations are a sparse side table owned by Stockpile & Hauling** (`StackReservationTable`: stack id → reserving job, reserved qty) — exactly as ADR-0002's firewall pre-assigned. Not an `ItemStore` field, never terrain's Flags bit. **Layering guard (TD gate)**: the reservation gate does not make the Foundation-layer `ItemStore` depend on a gameplay system — `ItemStore` receives a narrow read-only `IReservationOracle` injected at the composition root, **debug builds only**; the same shape as ADR-0002's Job-Assignment claim-table invariant, which terrain checks without owning. `PostEncounterReconcile` releases reservations held by dead colonists by enumerating this table (its owner exposes `ReleaseAllHeldBy(EntityId)`). **Zone membership** (stockpile areas, home/no-go areas) likewise remains Stockpile & Hauling's sparse cell-set data per ADR-0002 — not an entity, not a store, out of scope here.

**Doors** (`DoorStore`) — the deferred boundary, settled:

> **A door is an entity that contributes blocking state to composite walkability. It is never a `TerrainCell` wall.**

| Aspect | Decision |
|---|---|
| MVP scope | Doors ARE in MVP — **user decision, 2026-07-24** (chokepoint play and breach tactics are the game's identity; the fun spike fights in player architecture). Minimal: `Cell`, `IsOpen`, `Hp`, `IsBroken`, material/style reference. Companion edit records the entity kind in the systems index (see Migration Plan) |
| Lifecycle | Spawn: Construction (RealTime — building a door). Despawn: Construction (RealTime — deconstruction), and `PostEncounterReconcile` reaps `IsBroken` doors. **No despawn inside an encounter — a universal entity-layer rule** (colonists, raiders, doors alike): combat marks (`IsDead`/`IsBroken`), RealTime reaps |
| State writers | `IsOpen` — RealTime: Colonist Movement (transit auto-open/close); TurnBased: Combat: Movement & Reachability (opening as part of movement; richer door-interaction actions are Combat-GDD-era). `Hp`/`IsBroken` — TurnBased: Combat: Targeting & Resolution (same authority-tagged damage writer pattern as unit health) |
| Passability | `DoorStore` exposes `BlocksMovement(CellCoord)` (closed, unbroken door on cell). **Pathfinding composes** per the mode-aware rule below. Terrain stays ignorant of doors; doors stay ignorant of pathfinding |
| Destructibility | **Provisional MVP answer (TD gate)**: doors carry `Hp` and are damageable by Combat: Targeting & Resolution *exactly like walls*. Without this, a fully-doored colony is unreachable to raiders and the fun/pathfinding spikes dead-end. `Hp`→0 sets `IsBroken`, which makes `BlocksMovement` and LOS-blocking return **false immediately** — combat gets its breach the same turn; the broken door despawns only at reconcile (parallel to `IsDead`). Tier/HP numbers and richer interactions (bashing vs. breaching) refined in the Combat set + Destructibility GDDs |
| LOS | Closed, unbroken doors block LOS like walls — Spatial Query reads `DoorStore` the same way Pathfinding does; no terrain involvement |

### Composite walkability — the mode-aware composition rule

Pathfinding composes (per ADR-0002's delegation), and the composition differs by authority — stated here because the pathfinding spike implements it:

- **TurnBased**: walkable = `IsPassableTerrain(c)` ∧ ¬`DoorStore.BlocksMovement(c)` [door-opening move rules are Combat-GDD-era] ∧ **cell not occupied by a living unit** (occupancy hard-blocks — tactics legality) [whether friendly occupancy blocks *traversal* or only *end-of-move* (XCOM permits move-through-ally) is a Combat: Movement & Reachability GDD decision — the index answers "occupied by whom" either way].
- **RealTime**: walkable = `IsPassableTerrain(c)` ∧ ¬`DoorStore.BlocksMovement(c)` [colonists auto-open doors in transit]. **Occupancy does not block colony pathing in MVP** — that is the advisory decision made concrete; a later cost-term (congestion avoidance) is a Pathfinding-spec option, never a blocker.

### The occupancy index (deferred by ADR-0002, settled here)

`UnitOccupancyIndex` — an entity-layer module **owned by the composition root and injected into the two unit stores only** (adding a third unit store means an ownership-table row, not a quiet third writer), keyed `CellCoord → living unit EntityId(s)`, covering **units only** (colonists + raiders; items and doors are located but never "occupy"):

- **Single write path**: only the unit stores' internal position/death handling updates it, synchronously and atomically with the store write. No external writer exists; the debug-console sweep asserts index ≡ living store positions.
- **Mode-dependent constraint** (the decided rule; rejected siblings recorded — always-exclusive: reintroduces the colony traffic-jam bug class; never-enforced: combat re-derives legality per check): under **TurnBased**, unit occupancy is **exclusive** — one living unit per cell, hard invariant, asserted on every combat position write. Under **RealTime**, the index is **advisory** — transient overlap during colony pathing is legal; the index still answers "who is here" for job targeting and raid triggering. Multi-occupant query results return in **ascending `EntityId` order** (determinism — nondeterministic selection would leak into job targeting).
- **Pre-switch normalization (decide/execute split — one small ADR-0001 surface addition, no new dispatch phase)**: when `RequestSwitch(TurnBased, …)` is accepted mid-dispatch (ADR-0001's `DeferredMidDispatch`), the swap happens at end-of-dispatch — and normalization runs inside that same dispatch, triggered by a **read-only `SwitchPending`/`PendingSwitchTarget` property on `TimeAuthorityManager`** (set when a switch is accepted-but-deferred, cleared at the swap; companion edit 1 — a property, not a pass, so the mutation window is the ordinary dispatch window). **Pinned `TickPhase.Reaction` ordering, ascending declared priorities**: (1) Raid Trigger places its spawned raiders exclusively; (2) **Squad Preparation decides colonist placements** (seeing the raiders) and submits placement orders; (3) **Colonist Movement executes them via its existing position writer** — Colonist Movement carries a Reaction-phase registration for this in addition to its Simulation-phase one; Squad Prep never writes positions, preserving the one-writer rule. Deterministic nudge rule (the exclusivity assertion depends on it): co-located units are processed in **ascending `EntityId` order, each move visible to the next**; the lowest-id unit keeps its cell; each other unit moves to the nearest RealTime-walkable, unoccupied cell, scanning candidate cells in fixed ascending (Z, Y, X)-offset order at expanding radius. The exclusivity assertion arms when `TurnBasedAuthority` activates; the mode-switch spike validates the whole seam.
- Derived bookkeeping: rebuilt from stores on `Restore`/load, never serialized.

### The Encounter-Outcome Report (the Combat↔Veterancy cycle, resolved)

Combat's only export is a **plain data record emitted exactly once per encounter at battle end**: `EncounterOutcomeReport { EncounterId, ParticipantIds, per-participant outcome (survived / died + death cell), raid result }`. Its life-cycle across the mode boundary is fully owned:

- **Custodian**: an `EncounterOutcomeInbox` — a one-slot entity-layer module. **Combat: Turn Order (#19), the encounter driver, writes it at battle end** (a legal TurnBased write; the inbox is transient plumbing, not store state — and its writer is table-traceable like everything else). `PostEncounterReconcile` drains it on the first RealTime dispatch and it is empty again — never serialized (an encounter cannot span a save under CD-9).
- **Consumer ordering**: consumers are ADR-0001 tickables registered to the reconcile Reaction pass — ordering comes from ADR-0001's existing `(TickPhase, priority, registration sequence)` scheme; **no parallel ordering registry exists**.
- **Guard-rail note**: the report is *derived summary* handed in the TurnBased→RealTime direction — the stores remain the single source of truth throughout, so ADR-0001's "no entity state in the switch envelope" rule (which governs `SwitchTransitionData`, the other direction) is not violated; a reviewer applying that rule here should read this note.
- MVP consumers: Identity Bookkeeping (`BattlesSurvived`++ for survivors — the identity-group writer above); Notifications (named death + death cell, CD-4). VS consumers (schema already multi-consumer, per the index's cycle resolution): Veterancy (sole writer of skill fields), Identity & Memory.

This is **not** the World Change Event Bus — the bus keeps its single publisher (Terrain, ADR-0002). Combat depends on entity stores read-only + its writer interfaces; Veterancy depends on the report schema; **neither depends on the other** — the cycle is broken exactly as the systems index prescribed.

### Allocation policy (the house pattern, applied to entities)

- **Zero steady-state allocation** on: all reads, position writes, occupancy updates, `Revision` polls, and per-frame view diffing (pre-sized presentation-side buffers).
- `UnitOccupancyIndex` multi-occupant representation: a small **fixed-capacity inline slot set per occupied cell with a rare overflow list** — occupancy churn (the hottest entity write) allocates nothing in steady state at ~20 units.
- Encounter-scoped side tables are pooled or freed wholesale at battle end — a bounded, non-gameplay moment.
- The save/load and pathfinding spikes measure **entity-layer Gen0 alongside ADR-0002's terrain gates** — one combined allocation verdict for the frame budget.
- `Snapshot()` allocation stance (symmetry with ADR-0002's spike-deferred buffer strategy): entity snapshots are kilobytes — a one-shot allocation at the CD-9 autosave moment is accepted outright; no buffer-reuse machinery is warranted at this scale.

### Views (ADR-0001's forbidden-pattern detail, delivered)

Godot Nodes (colonist visuals, door meshes, item piles) are **views**: they bind by `EntityId`, read stores in `_Process` for presentation (interpolation, animation state), and write **nothing**. View lifecycle follows store lifecycle via presentation-side diffing of the stores (spawn/despawn is low-frequency; a per-frame roster diff over ~dozens of entities is trivial — no sim-side view registry exists). `IsDead` + never-reused ids make "gone vs. ghost" unambiguous for teardown. Input/UI never writes stores — it submits designations/orders to owning systems (ADR-0001/0002 rule, restated for entities).

**Scoped exception to the signal-preference convention (explicit, per godot-specialist review)**: view updates are pull-based per-frame diffs, not Godot signals — a plain-C# store cannot emit Godot signals without violating zero-Godot-dependency, and an adapter layer is not worth it at this entity scale. Future code review should not flag the polling loop as an oversight; it is this ADR's decided pattern. How view Nodes *obtain* store read references (e.g. a single game-root autoload owning the composition root) and how colony-mode views behave during `TurnBasedAuthority` (suspension without the forbidden `SceneTree.paused`) are Views-spec decisions — routed in Open Questions.

### Architecture Diagram

```
                       writer interfaces (composition root grants per ownership table)
   ┌──────────────┬───────────────┬────────────────┬─────────────────┬──────────────┐
   │ Needs & Sim  │ Job Assignment│ Colonist Mvmt  │ Squad Prep      │ Raid Trigger │  [RealTime writers]
   │ health,needs │ job state     │ position (+pre-│ squad/draft;    │ raider spawn │
   │              │               │ switch orders) │ placement DECIDE│ (excl-placed)│
   └──────┬───────┴──────┬────────┴──────┬─────────┴──────┬──────────┴──────┬───────┘
          ▼              ▼               ▼                ▼                 ▼
   ┌─────────────────────────────────────────────────────────────────────────────────┐
   │  ColonistStore · RaiderStore · ItemStore · DoorStore   (plain C#, EntityId-keyed,│
   │  ascending-id order, stable removal, Snapshot/Restore, Revision, mutation-window │
   │  + mode + kind asserted; PASSIVE — never tick)                                   │
   │      └─ unit stores' position/death handling atomically updates ─►               │
   │         UnitOccupancyIndex (living units; exclusive under TurnBased, advisory    │
   │         under RealTime; ascending-id results; rebuilt on load)                   │
   └─────────────────────────────────────────────────────────────────────────────────┘
          ▲              ▲               ▲                    reads (everyone):
   ┌──────┴───────┬──────┴────────┬──────┴─────────┐          Pathfinding (mode-aware
   │ Combat: T&R  │ Combat: Mvmt  │ Combat: Raider │          composite walkability),
   │ health+door  │ position      │ AI state       │          Spatial Query/LOS, UI,
   │ damage       │               │                │          Godot views (read-only,
   └──────────────┴───────────────┴────────────────┘          poll Revision, diff/frame)
        [TurnBased writers]

   Combat side tables (initiative, AP, targets) — encounter-scoped, owned by combat
   systems, NEVER in stores, NEVER serialized (CD-9)
   Battle end ──► EncounterOutcomeReport ──► EncounterOutcomeInbox (one slot) ──►
   drained by PostEncounterReconcile (ADR-0001 phase/priority ordering)
   (MVP: Identity Bookkeeping, Notifications · VS: Veterancy, Memory)
   StackReservationTable — owned by Stockpile & Hauling; gates ConsumeFromStack
   Debug-console (#29) sweeps: occupancy ≡ positions · reservations ≡ claims
```

### Key Interfaces
`EntityId` (`long`, 0 = None) + serialized `EntityIdSource` (monotonic, never reused) · four typed stores with `Spawn/Despawn` (stable ordered removal), deterministic iteration, `Snapshot/Restore`, `Revision` · per-(system × field group) writer interfaces granted at the composition root (authority-tagged where split; kind-asserted) · `UnitOccupancyIndex` (living units; exclusive-in-TurnBased invariant; ascending-id results) · pre-switch placement: Squad Prep decides → Colonist Movement executes · mode-aware composite-walkability rule · `DoorStore.BlocksMovement` + door `Hp` (provisional MVP destructibility) · `ItemStore` operation API, reservation-gated consume, `StackReservationTable` (Stockpile & Hauling-owned) · `EncounterOutcomeReport` via one-slot `EncounterOutcomeInbox`, consumed in `PostEncounterReconcile` under ADR-0001 ordering · `EntityDirectory` (derived, rebuilt on load) · Revision-polling change notification (no entity event bus)

## Alternatives Considered

### Alternative B: Generic ECS (archetype/component storage)
- **Description**: A component-oriented store (custom or a C# ECS library): entities are ids, field groups become components, systems iterate component queries.
- **Pros**: Uniform machinery; field-group separation falls out naturally; scales to thousands of entities; combat side tables are "just components".
- **Cons**: MVP peaks at ~10 colonists + one raid + hundreds of stacks — the scale that justifies ECS never arrives (the concept caps sim depth explicitly). Write-ownership becomes *convention* (any system iterating a component can write it) unless wrapped in exactly the interface segregation proposed anyway. A framework dependency (or a hand-rolled archetype engine) is real solo-dev cost against "no speculative generality". Allowed-libraries list is empty by policy.
- **Rejection Reason**: The ownership table — the actual mandate — is enforced *better* by narrow interfaces over typed stores than by open component queries; ECS's wins are at a scale this game's caps forbid. Revisit only if a future tier lifts the entity-count caps (that would be a new ADR).

### Alternative C: Godot Nodes as entities
- **Description**: Each colonist/raider/door is a scene-tree Node; sim state lives on the node (C# script fields); systems traverse the tree.
- **Pros**: Engine-native lifecycle, editor inspection, view and state co-located.
- **Cons**: Violates ADR-0001's forbidden pattern outright (per-entity sim state as Nodes); no headless tests; save entangled with scenes; iteration order and lifecycle tied to the tree; combat/tools code inherits the 4.4–4.7 breaking-change surface.
- **Rejection Reason**: Rejected by ADR-0001 precedent — included to document that the "detail in ADR-0003" pointer resolves to: Nodes are views binding by `EntityId`, full stop.

### Alternative D: One health-owner module (for the arbitration specifically)
- **Description**: A dedicated Health subsystem is the permanent sole writer; Combat and Needs submit typed damage/heal requests it resolves.
- **Pros**: One future home for wound/injury/medical rules; writer never changes with mode.
- **Cons**: A third system and a request-queue layer for MVP's `Hp` integer; both callers already run under disjoint authorities, so the arbitration it provides is *already structural* in ADR-0001's mode exclusivity; request queues invite ordering questions ADR-0001 settled.
- **Rejection Reason**: Writer-per-authority reuses an existing, asserted pattern (ADR-0002 rule 4) at zero new machinery. If VS-era medical systems need richer arbitration, a Health module can be introduced *then* by claiming the health row of the table — a one-row migration, not a redesign.

### Alternative E: Assertion-only ownership enforcement (open setters, no writer interfaces)
- **Description**: Stores expose full mutating surfaces to everyone; the ownership table is review doctrine, enforced only by the runtime mutation-window/mode/kind assertions this ADR builds anyway.
- **Pros**: Materially cheaper for a solo developer — no interface zoo, no composition-root ceremony; the runtime assertions catch the window/mode violations regardless.
- **Cons**: Runtime-only enforcement fails **silently in code paths playtests do not exercise** — precisely where ownership bugs hide; the table decays into a document nothing checks; a wrong-system write in an exercised-but-unasserted combination (right window, right mode, wrong system) is *undetectable* at runtime without a per-call system-identity token, which is its own ceremony.
- **Rejection Reason**: Compile-time unrepresentability is what makes the ownership table still true six months in (validation criterion 6); the interface cost is ~a dozen small declarations paid once. This was the genuinely competitive alternative — rejected on failure mode, not on cost.

## Consequences

### Positive
- Every named conflict lands with a structural answer: health (writer-per-authority, mode-asserted), colonist death (lethal write sets `IsDead`; reconcile reaps), Combat↔Veterancy (outcome report via one-slot inbox, ADR-0001 ordering), reservations (Stockpile-owned table gating consume), doors (entity contributing to composed walkability, damageable like walls), occupancy (single-write-path index, exclusive only where tactics needs it).
- The ownership table is enforceable at two levels — compile-time (a system cannot call setters it wasn't handed) and runtime (mutation-window + mode + kind assertions) — so violations are build/debug failures, not playtest archaeology.
- CD-9's serialization scope-shrink is structural: combat-transient state never enters the stores, so "don't serialize battles" requires no code — there is nothing to skip.
- Spikes get real contracts: pathfinding composes three defined inputs under a mode-aware rule with a defined invalidation mechanism; save/load round-trips four stores + id source; mode-switch exercises normalization + reconcile against actual tables.
- The store recipe + no-misc-store rule gives GDD-era entity kinds (furniture, workshops, traps) a paved road that preserves the ownership discipline.

### Negative
- Writer-interface segregation is ceremony: ~a dozen small interfaces and a composition root that must be kept honest. Accepted: the alternative (Alternative E) fails silently exactly where ownership bugs live.
- Writer-per-authority health means future *cross-mode* health effects (poison ticking in both modes) would need a table revision. Accepted: MVP has none, and the revision is one row + one interface.
- Advisory RealTime occupancy means colony-mode queries ("who is on this cell") can return >1 unit and callers must handle it (in ascending-id order). Accepted as the price of no colony traffic deadlocks.
- One id space across kinds surrenders compile-time kind safety on ids; wrong-kind references are caught by debug kind-assertions, not the type system. Accepted for the union-free reference model; the assertion + headless tests are the floor.
- Encounter-scoped raiders + never-reused ids mean corpse/loot presentation needs its own answer later (routed to Raid Trigger GDD) — the store won't remember dead raiders for free.
- Four stores + index + side tables is more moving parts than one entity list. Accepted: each part has one owner and one job — that is the point.
- Entities are not Nodes, so they are invisible to Godot's Scene Tree dock and Remote Inspector at runtime — a real loss of built-in debugging affordance (godot-specialist note). Accepted; the mitigation is the Tier 0 debug console (index #29), which grows an entity-inspection surface reading the stores — the same tool path the sim already requires.

### Risks
- **Composition root drift** — writer interfaces handed to the wrong system over time. *Mitigation*: the root is ONE reviewable file; the ownership table in this ADR is its source of truth; code review checks new grants against the table; the debug-console sweep can dump grants for audit.
- **Occupancy divergence from positions** (the two-sources-of-truth risk, again). *Mitigation*: single write path by construction (store-internal, including death-release) + the debug-console sweep (index ≡ living positions), the same mechanism ADR-0002 uses for the reservation bit.
- **Combat-transient leakage into stores** ("just one flag on the colonist for overwatch…"). *Mitigation*: the firewall rule (meaningless-outside-encounter ⇒ side table) + validation criterion 6; CD-9 makes leakage visible as a serialization diff.
- **Pre-switch normalization edge cases** (no free adjacent cell within radius; participants on stairs). *Mitigation*: deterministic rule fixed here; the mode-switch spike validates before Accepted; the exclusivity assertion converts any surviving hole into a loud debug failure, not silent corruption.
- **Revision-polling misses a needed granularity** (a consumer genuinely needs per-cell entity change lists). *Mitigation*: the pathfinding spike measures the door-rescan path explicitly; the upgrade (narrow change list behind the same facade) is pre-planned and non-breaking.
- **`EncounterOutcomeReport` schema churn** as VS consumers arrive. *Mitigation*: multi-consumer schema from day one (the index's explicit instruction); consumers are additive tickables under existing ordering; the report is transient by construction (CD-9).
- **Store lifecycle vs. view lifecycle desync** (ghost visuals). *Mitigation*: presentation diffs stores per frame (cheap at this scale); `IsDead` + never-reused ids make "gone" unambiguous.

## GDD Requirements Addressed

| GDD Document | System | Requirement | How This ADR Satisfies It |
|-------------|--------|-------------|--------------------------|
| `design/gdd/systems-index.md` #9 | Colonist Entity & Attributes | "Data store with write-ownership table (ADR-003): each field group has exactly one writer. Health write-arbitration (Combat vs Needs) resolved in ADR-003" | The ownership table (incl. Lifecycle rows); writer-per-authority health with authority-tagged interfaces + mode assertion |
| `design/gdd/systems-index.md` Circular Dependencies | Combat ↔ Skill & Veterancy | "Colonist Entity owns skill/veterancy data with Veterancy as sole writer; Combat reads data, emits the combat-outcome event; multi-consumer schema" | `EncounterOutcomeReport` via one-slot inbox, consumed in `PostEncounterReconcile` under ADR-0001 ordering; skill fields land dormant in MVP saves |
| `design/gdd/systems-index.md` #10, #11 | Job Assignment; Stockpile & Hauling | Claiming/cancellation; "reservation logic is a first-class design problem" | Job-state group owned by Job Assignment; `StackReservationTable` owned by Stockpile & Hauling; reservation-gated `ConsumeFromStack`; `ReleaseAllHeldBy` for reconcile |
| `design/gdd/systems-index.md` #24 | Squad Preparation | Mode-switch seam ("hairiest moment", High-Risk table) | Squad/draft group ownership; pre-switch placement: Squad Prep decides, Colonist Movement executes, deterministic nudge rule fixed here; `ParticipantIds` sourced from it |
| CD-4 (systems index) | Identity minimum surface | Name + appearance seed + battles-survived + named death notification | Identity field group; Identity Bookkeeping as the named MVP writer (Roster UI displays, never writes); death cell preserved by no-despawn-in-encounter + the outcome report |
| CD-3 (systems index) | Raider withdraw condition | "Raiders leave when they get what they came for or the cost exceeds it" | Raider AI-state group (objective/satisfaction) owned by Raider Decision-Making, seeded by Raid Trigger |
| CD-9 (systems index) | No mid-battle saves | "Serialization scope for combat state shrinks accordingly" | Combat-transient side tables outside the stores — structurally unserialized; the outcome inbox is transient by construction |
| `design/gdd/game-concept.md` Core Mechanics 2–3, MVP caps | Colony sim + mode switch | ~10 colonists, 1 raider type, 3 needs, fixed loadouts; one shared world | Store scale sized to caps; zero-conversion switch (same stores, swapped writer set — ADR-0001's pattern completed for entities) |

## Performance Implications
- **CPU**: Entity counts are capped tiny (~10 colonists, one raid, hundreds of stacks) — all store iteration is trivially in-budget; occupancy queries are index lookups; per-frame view diffing and Revision polls are O(entities) over dozens.
- **Memory**: Kilobytes across all stores; the id source is one `long`; side tables are sparse; encounter-scoped tables are pooled/freed at battle end. Zero steady-state allocation per the policy above.
- **Load Time**: Four store restores + directory/index rebuilds — negligible; occupancy rebuild is one pass over living unit positions.
- **Network**: N/A.

## Migration Plan
None — greenfield. The Tier 0 pathfinding, save/load, and mode-switch spikes implement against these contracts; falsification (e.g. the normalization rule proves insufficient, or Revision-polling rescan costs surprise) revises this ADR before promotion to Accepted.

**Companion edits at adoption** (same changeset as this ADR — TD gate H4/A/C):
1. **ADR-0001** — three edits: (a) `TimeAuthorityManager` gains a read-only **`SwitchPending` / `PendingSwitchTarget`** property — set when a switch is accepted-but-deferred (`DeferredMidDispatch`), cleared at the swap; the trigger for pre-switch normalization (a property, not a new pass or phase). (b) `PostEncounterReconcile` duty list (§Decision step 4) gains: drain the `EncounterOutcomeInbox` and dispatch to registered consumers (existing phase/priority ordering); reap `IsDead` colonists, `IsBroken` doors, and dead/withdrawn raiders; `ReleaseAllHeldBy(deadId)` on the stack-reservation table. (c) Worked-example table: the Squad Preparation row's RealTime cell gains "on an accepted pending switch, decide pre-switch placements submitted to Colonist Movement (Reaction phase)"; new passive-store rows *Entity stores (ADR-0003) | neither — passive stores | — | — | writer set changes per the ADR-0003 ownership table* and *UnitOccupancyIndex / EntityDirectory | neither — derived bookkeeping | — | — | rebuilt on load, never serialized*.
2. **ADR-0002** — the shared-primitives block gains `EntityId`'s definition by reference ("defined in ADR-0003; `long`, monotonic, never reused"), since that block declares changing shared primitives an ADR-level event.
3. **`.claude/docs/technical-preferences.md`** — Forbidden Patterns gains: entity-store writes outside granted writer interfaces or the mutation window; combat-transient state in entity stores; any generic "misc entity" store; UI/views writing entity stores; occupancy-index updates outside store-internal position/death handling. Architecture Decisions Log gains the ADR-0003 entry.
4. **`design/gdd/systems-index.md`** — three notes: (a) doors as an MVP entity kind (user decision 2026-07-24): built/deconstructed by Construction (#16), damaged by Combat: Targeting & Resolution (#22), opened in combat movement (#21), composed into walkability by Pathfinding (#8), LOS-blocking read by Spatial Query (#12), state defined in ADR-0003 — a note across those entries, not a new numbered system. (b) The Circular Dependencies entry gains a pointer: the "combat-outcome event" is realized as ADR-0003's `EncounterOutcomeReport` (one-slot inbox, MVP consumers Identity Bookkeeping + Notifications, VS consumers Veterancy + Identity & Memory). (c) Entry #9 notes Identity Bookkeeping as a small entity-layer module defined in ADR-0003 (the MVP writer of `BattlesSurvived`), not a new system.

## Spike Results — mode-switch seam (2026-07-26)

The Tier 0 mode-switch spike (`prototypes/mode-switch-spike/`, SPIKE-NOTE.md) exercised this ADR's switch-facing contracts against real registered systems. **Validated**: occupancy advisory under RealTime / hard-asserted exclusive under TurnBased; the single position write path updating the index atomically; health writer-per-authority refusing each writer in the wrong mode; no-despawn-inside-an-encounter; the dead colonist's death cell surviving to the report (CD-4); `ReleaseAllHeldBy` leaving zero orphaned reservations; exactly one `EncounterOutcomeReport` drained from the one-slot inbox with `BattlesSurvived` credited to survivors only; the occupancy-vs-positions sweep clean after reconcile. Cost of a full reconcile (50 jobs, 50 paths, 20 reservations, 1 death, 3 raiders): **28.9 µs**, once per battle. The pathfinding and save/load spikes still gate the rest of this ADR.

**Correction — the raider reap rule leaked (found by the spike).** The Raider Lifecycle row said reconcile despawns *"dead/withdrawn"* raiders. But raiders are **encounter-scoped**: if a battle ends while a raider is alive and has not withdrawn — a debug/scripted end today, or any future objective-complete end condition — that raider survives into colony time as an undespawnable ghost, since despawn inside an encounter is forbidden and no later pass reaps it. **Corrected rule: `PostEncounterReconcile` reaps ALL raiders**, because none may outlive the encounter. The lifecycle row above now says so.

**Implementation trap for the pre-switch normalization spec (shared with ADR-0001):** the nudge rule must evaluate each unit against the cells already claimed by earlier decisions *in the same pass*, not against the live occupancy index — a decide-only pass still sees the pre-normalization world, so testing live occupancy makes every co-located unit move, including the lowest id that should keep its cell.

## Validation Criteria
1. Entity assemblies have zero Godot references (same CI grep as ADR-0002) and a headless suite covers: writer-interface segregation (a system holding only its granted interfaces compiles; forbidden writes are unrepresentable), mutation-window assertion firing on out-of-window writes, mode assertion firing on wrong-authority health/position writes, kind assertion firing on wrong-kind ids, reservation-gated consume rejecting unreserved calls.
2. Occupancy invariants: index ≡ living store positions after arbitrary mutation sequences (property test); exclusivity assertion fires on any TurnBased co-location; pre-switch normalization produces exclusive, deterministic placement from seeded co-located setups (same seed → same placement), including the no-free-adjacent-cell edge.
3. `Snapshot → Restore` round-trips all four stores byte-stably with `EntityId`s and the `EntityIdSource` counter preserved (non-reuse guaranteed across the round-trip); `EntityDirectory` and `UnitOccupancyIndex` rebuild correctly; **no combat side table and no outcome inbox contributes any bytes** (CD-9 structural check).
4. ADR-0001's reconcile integration test extended to entities: kill a reserving colonist mid-encounter → resume → `ReleaseAllHeldBy` leaves zero orphaned stack reservations, zero jobs claimed by the dead, `BattlesSurvived` incremented only for survivors, the dead colonist reaped with death cell captured in the consumed report, exactly one `EncounterOutcomeReport` drained under ADR-0001 ordering.
5. Composite walkability in the pathfinding spike, per mode: TurnBased paths treat occupied cells and closed doors as blocking; RealTime paths ignore occupancy and auto-open doors; a door state change bumps `DoorStore.Revision` and cached paths re-validate correctly (rescan cost measured — the pre-planned change-list upgrade triggers only if this fails budget).
6. Six months in: `ColonistStore` contains no encounter-scoped field; the composition root's grants still match this ADR's table; no "misc entity" store exists; the occupancy and reservation sweeps have never fired in CI.

## Open Questions (routed, not decided here)
- **Door destructibility detail** (HP values, material tiers, bash-vs-breach actions) → Combat set + Material-Tier Destructibility GDDs, against the provisional doors-damage-like-walls rule fixed here.
- **Furniture/workshop entity kinds** (beds, forge; ~15–20 room types at full vision) → Needs & Simulation / Construction GDDs, via the store recipe.
- **Raider corpse & loot representation** after despawn-at-reconcile → Raid Trigger GDD.
- **Colony-mode colonist death** (can starvation kill in MVP, and its reap path) → Needs & Simulation GDD, against the anti-permadeath rule (CD-3) and the Lifecycle row's RealTime reap pattern.
- **Stack merge/split rules, stockpile capacity, per-cell stack limits** → Stockpile & Hauling quick-spec (the operation API here is its substrate).
- **Colonist arrival/embark source** (who spawns the initial ~10) → Map Authoring quick-spec (owns the load-window spawn path).
- **The full colonist attribute list** (player-facing stats beyond these field groups) → Colonist Entity quick-spec (#9) — new attributes must join an existing field group or add a table row.
- **View wiring and mode-switch view lifecycle** (how view Nodes obtain store read references — likely one game-root autoload owning the composition root; whether colony views' `_Process` runs or is suspended during TurnBased, without `SceneTree.paused`) → Views/rendering specs + the mode-switch spike (godot-specialist notes 1 and 5).
- **Post-battle time semantics** (zero-elapsed vs. advance) — already routed to creative-director before the Needs GDD (open question in session state); does not change ownership, only Needs' decay math.

## Related Decisions
- ADR-0001 Time Authority (Proposed) — mutation window, mode exclusivity that makes writer-per-authority sound, `ParticipantIds`, `PostEncounterReconcile` as the outcome-processing site; companion edits listed in Migration Plan
- ADR-0002 Terrain Data Model (Proposed) — firewall table pre-assignments honored (occupancy, reservations, doors; zones stay Stockpile & Hauling's sparse cell-sets); `IsPassableTerrain` composed with door/occupancy inputs under the mode-aware rule; enforcement patterns reused (writer tables, debug sweeps, single write paths, `Revision`)
- Seeded RNG ADR (pending) — entity spawn (appearance seeds, raider composition) draws from per-system streams inside authority-driven execution
- `design/gdd/systems-index.md` — #9 mandate, circular-dependency resolutions, CD-3/CD-4/CD-9 notes, High-Risk seam entry
- `design/gdd/game-concept.md` — MVP caps that size this architecture; Pillar 4 (colony works autonomously — orders flow through owning systems, never direct writes)
