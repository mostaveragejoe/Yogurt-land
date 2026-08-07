# Quick Design Spec: Colonist Entity & Attributes

**Type**: New Small System (Core / MVP; routed to Quick-spec per PR-SCOPE)
**Scope**: The per-colonist data record and its read/write surface. Architecture (typed store, write-ownership enforcement, `EntityId`, occupancy, snapshot) is owned by **ADR-0003**; this spec fixes the MVP player-facing attribute list, per-field ownership mapping, and acceptance criteria.
**Date**: 2026-08-07
**Governing ADRs**: ADR-0003 (Entity Data Ownership), ADR-0001 (time authority / mutation window), ADR-0005 (appearance seed stream)
**Estimated Implementation**: ~1–2 days for the MVP store + writer interfaces (the composition-root wiring is ADR-0003's carried obligation)

## Overview

A colonist is a record in the plain-C# `ColonistStore` (ADR-0003), keyed by `EntityId`, that every colony and combat system reads to know **who a colonist is, where they are, how hurt or hungry they are, what job they hold, and whether they are drafted**. The store owns no behavior and never ticks: systems act on colonists by reading this data and writing *their own field group* through a granted writer interface. This spec is the MVP field list and the contract for reading and writing it — the "what fields exist and who may touch them," on top of ADR-0003's "how ownership is enforced."

## Scope

**In scope (owned here):** the MVP field groups and their fields; the per-field writer mapping (deferring enforcement mechanism to ADR-0003); the CD-4 identity surface; the public read/write surface; behavior under each time authority; the serialization shape.

**Out of scope (owned elsewhere):**
- **Balance numbers** — max `Hp`, need caps/decay rates, damage magnitudes — owned by **Needs & Simulation (#13)**, the **Combat set (#19–23)**, and **Material Catalog (#5)**. This spec fixes field *shape*, never balance values (same discipline as the Terrain GDD: own the record, not the numbers).
- **Combat-transient state** (initiative, action points, target locks, overwatch) — encounter-scoped side tables owned by the combat systems (ADR-0003 firewall); never `ColonistStore` fields.
- **Skill/veterancy semantics** — Vertical Slice (#30/#31); MVP carries the fields dormant only.
- **Occupancy / pathfinding composition** — ADR-0003 + Pathfinding (#8).
- **Display** — Roster UI (#28) reads and shows; it never writes.

## Core Rules

**C1 — A colonist is passive data.** A record in `ColonistStore` keyed by `EntityId` (`long`, monotonic, never reused — ADR-0003). The store never registers with a time authority and has no `Tick()`.

**C2 — MVP field groups (the attribute list).** Fields and their writers map exactly onto ADR-0003's write-ownership table:

| Field group | MVP fields | Writer (RealTime) | Writer (TurnBased) | Primary readers |
|---|---|---|---|---|
| Lifecycle | spawn / despawn | Spawn: Map Authoring embark (load window). Despawn: `PostEncounterReconcile` only | — | — |
| Identity | `Name`, `AppearanceSeed`, `BattlesSurvived` | Name/seed set once at spawn; `BattlesSurvived` by Identity Bookkeeping (reconcile) | — (frozen) | Roster UI, Notifications, views |
| Position | `Cell`, movement progress/facing | Colonist Movement | Combat: Movement & Reachability | Pathfinding, Spatial Query, views |
| Health / body | `Hp`, `IsDead` | Needs & Simulation | Combat: Targeting & Resolution | Combat, Roster UI, Notifications |
| Needs | exactly 3 need values (food, sleep, work) | Needs & Simulation | — (frozen) | Job Assignment, Roster UI |
| Job state | `CurrentJobId`, claim backrefs | Job Assignment | — (frozen) | Job Assignment, Pathfinding |
| Squad / draft | `SquadRole`, drafted flag | Squad Preparation | — (frozen — roster locked at switch) | Combat set, Squad Prep, Roster UI |
| Skill / veterancy | fields present, **dormant in MVP** | — (init only) | — | — (VS: Veterancy sole writer) |

**C3 — Identity surface (CD-4).** MVP identity is exactly three fields: `Name` (persistent, set once at spawn), `AppearanceSeed` (a value drawn from the `ColonistAppearance` RNG stream at spawn per ADR-0005 — deterministic visuals; **views derive appearance from the seed, they never store rendered appearance in the model**), and `BattlesSurvived` (a counter incremented **only** by Identity Bookkeeping during `PostEncounterReconcile`; Roster UI *displays* it and never writes). Full Identity & Memory (#31) is Vertical Slice — MVP deliberately has no authored backstory or event history.

**C4 — Needs are exactly three in MVP.** `food`, `sleep`, `work` (the concept cap). The **count** is fixed here as the field shape; the **values, caps, and decay** are Needs & Simulation's (#13). Adding a fourth need is a design change to #13, not a store tweak.

**C5 — Health is writer-per-authority.** `Hp` plus an `IsDead` flag (wound/injury detail is GDD-era). Under RealTime the sole writer is Needs & Simulation (decay/recovery); under TurnBased it is Combat: Targeting & Resolution (damage) — one legal writer at any instant, mirroring ADR-0002 rule 4. `Hp`→0 sets `IsDead` in the **same** lethal write (no separate death writer), and the store internally removes the dead unit from the occupancy index (dead units do not block cells). CD-13's downed→stabilize model persists its recovery cost through the `EncounterOutcomeReport`/`PostEncounterReconcile` seam (routed to technical-director in the systems index) — MVP `IsDead` is the minimal surface.

**C6 — Position updates the occupancy index atomically.** `Cell` plus movement progress/facing. Both the RealTime writer (Colonist Movement, including executing Squad Prep's pre-switch placement orders) and the TurnBased writer (Combat: Movement & Reachability) go through the store's **single** position setter, which updates `UnitOccupancyIndex` synchronously and atomically (ADR-0003 — the index has no external writer).

**C7 — Job state.** `CurrentJobId` and claim backrefs; **Job Assignment (#10) is the sole writer**, pairing with its terrain-claim who/why table.

**C8 — Squad / draft.** `SquadRole` and a drafted flag; **Squad Preparation (#24) is the sole writer**; frozen under TurnBased (the roster is locked at the switch). The drafted roster feeds `SwitchTransitionData.ParticipantIds` (framing, not state).

**C9 — Skill / veterancy fields are dormant in MVP.** They exist in the store and the save format so no migration is needed at Vertical Slice, but have no MVP writer beyond initialization. At VS, **Veterancy is the sole writer**, consuming the `EncounterOutcomeReport`; **Combat never writes skills** (the Combat↔Veterancy cycle break, ADR-0003).

**C10 — Combat-transient state is never a colonist field.** Initiative, turn-order position, action points, selected targets, overwatch flags live in encounter-scoped side tables owned by the combat systems (ADR-0003 firewall). Any proposed `ColonistStore` field that is meaningless outside an encounter belongs in a side table.

**C11 — Read/write surface.** Read access for everyone by `EntityId`; **write access only through per-field-group writer interfaces** (e.g. `IColonistHealthWriter`, `IColonistNeedsWriter`, `IColonistMovementWriter`) granted at the composition root — a system physically lacks the setters it does not own. Every write debug-asserts the mutation window, the active `Mode` (where authority-split), and the id's kind (ADR-0003). Change notification is **`Revision` polling** — a monotonic `long`, +1 per mutating call and on `Restore`, never serialized; there is no entity event bus.

**C12 — Lifecycle.** Spawn at embark (Map Authoring, load window). **Despawn only at `PostEncounterReconcile`**, which reaps `IsDead` colonists after the outcome report is consumed — colonists are **never** despawned inside an encounter, so a corpse stays addressable for the outcome report and CD-4's death-cell notification. The `EntityIdSource` counter is serialized, so ids never collide or reuse across save/load (save/load spike).

## Behavior Under Each Time Authority

Mandatory section (cross-cutting contract #1). `ColonistStore` is a **passive store — it never ticks**; it registers with no authority and has no `Tick()`. Its behavior is *identical inert data in both modes; only the legal writer set changes*:

| Authority | Legal colonist writers |
|---|---|
| RealTime | Colonist Movement (Position); Needs & Simulation (Health, Needs); Job Assignment (Job state); Squad Prep (Squad/draft); Identity Bookkeeping (BattlesSurvived, at reconcile) |
| TurnBased | Combat: Movement & Reachability (Position); Combat: Targeting & Resolution (Health) — all other groups **frozen** |
| Outside both (load window) | Map Authoring (spawn); `Restore` |

Reads are always legal in both modes.

## Serialization

`ColonistStore` implements `Snapshot()` / `Restore(snapshot)` with a schema version (cross-cutting contract #2). A colony save contains all colonist field groups; because combat-transient state is never a store field, it is never in a colony save (ADR-0003 firewall). `AppearanceSeed` and `BattlesSurvived` persist (deterministic visuals and the CD-4 counter survive reload). Un-reaped `IsDead` colonists are a **legal serialized state in the battle checkpoint** (ADR-0003/0004) — the load path never reaps and the occupancy rebuild filters dead units. Occupancy and directory are derived, rebuilt on load, and contribute **0 bytes**.

## Tuning Knobs

This spec owns field **shape**, not balance — most values live with their owning systems.

| Knob | Value | Owner | Notes |
|---|---|---|---|
| Need count | **3** (food, sleep, work) | This spec (shape) / Needs #13 (values) | Fixed by the concept cap; a 4th need is a #13 design change |
| Max `Hp`, need caps, need decay rates | — | Needs & Simulation #13, Combat #22, Material Catalog #5 | **Deliberately not set here.** Same rule as the Terrain GDD: own the record, defer the numbers |
| `AppearanceSeed` → visual mapping | — | Art / views | The model stores only the seed; the mapping is presentation |

## Acceptance Criteria

- **GIVEN** any colonist, **WHEN** it is spawned and later despawned, **THEN** its `EntityId` is monotonic and never reused, including across save/load (serialized `EntityIdSource`). *(C1, C12)*
- **GIVEN** a system without a field group's writer interface, **WHEN** it attempts a write to that group, **THEN** it cannot (design-time interface segregation; runtime mutation-window + mode + kind assertion fires as the backstop). *(C11)*
- **GIVEN** RealTime is active, **WHEN** Combat: Targeting attempts an `Hp` write — or GIVEN TurnBased is active, **WHEN** Needs attempts an `Hp` write — **THEN** the mode assertion fires and the write does not apply. *(C5)*
- **GIVEN** a colonist at `Hp` > 0, **WHEN** a lethal write brings `Hp` to 0, **THEN** `IsDead` is set in the same call and the unit is removed from `UnitOccupancyIndex`. *(C5, C6)*
- **GIVEN** an encounter in progress, **WHEN** a colonist dies, **THEN** it is **not** despawned during the encounter and remains addressable by `EntityId` until `PostEncounterReconcile`. *(C12)*
- **GIVEN** a spawned colonist, **WHEN** any system other than Identity Bookkeeping (including Roster UI) attempts to write `Name`, `AppearanceSeed`, or `BattlesSurvived`, **THEN** the write is rejected; `BattlesSurvived` increments only via Identity Bookkeeping at reconcile. *(C3)*
- **GIVEN** the MVP `ColonistStore`, **WHEN** its need fields are enumerated, **THEN** there are exactly three (food, sleep, work). *(C4)*
- **GIVEN** a populated store, **WHEN** `Snapshot()` then `Restore()` runs, **THEN** all field groups round-trip byte-identically under the schema version; `AppearanceSeed` and `BattlesSurvived` survive; occupancy/directory are rebuilt and contribute 0 serialized bytes. *(Serialization)*
- **GIVEN** the MVP save format, **WHEN** skill/veterancy fields are inspected, **THEN** they are present but unwritten (dormant). *(C9)*
- **GIVEN** any mutating call or a `Restore`, **WHEN** it completes, **THEN** the store's `Revision` has incremented, and a consumer polling `Revision` observes the change. *(C11)*
- **GIVEN** any system, **WHEN** it needs colonist data, **THEN** it can read by `EntityId`, and **no** consumer writes the store directly. *(C11)*

## Systems Index

Tracked as **#9 Colonist Entity & Attributes** (Core / MVP / Quick-spec + ADR-003). This spec fills that row; status moves Not Started → Designed (quick-spec authored). No new index entry needed.

## Affected Systems / GDD Update

Conforms to ADR-0003; **no ADR change required**. No existing GDD is modified (there is no Colonist GDD; downstream systems — Needs #13, Job Assignment #10, Combat set, Squad Prep #24, Roster UI #28 — already reference the ADR-0003 ownership table). Only the systems-index status row updates.
