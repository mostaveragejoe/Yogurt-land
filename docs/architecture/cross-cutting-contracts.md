# Cross-Cutting Contracts (Annex)

**Status**: Active (binds all MVP specs and stories) · **Date**: 2026-07-25 · **Owner**: technical-director
**Sources**: ADR-0001 (Time Authority — **Accepted**), ADR-0002 (Terrain Data Model — **Accepted**), ADR-0003 (Entity Data Ownership — **Accepted**). All three spike-validated 2026-07-25/26.
**Hard cap** (systems-index mandate): one page, these three contracts, **no fourth**. A concern that doesn't fit one of these three gets its own ADR — it does not grow this page.

Every simulation-bearing GDD, quick-spec, and story is written against — and reviewed against — these three contracts.

## 1. Time Authority Tick Contract (ADR-0001)

- Every simulation system implements `ITickable.Tick(TimeContext)` and registers with `TimeAuthorityManager` under an explicit `(authority, TickPhase, priority)`. Dispatch order is deterministic; scene-tree order never controls simulation order.
- **Every sim spec includes a "Behavior under each time authority" section**, written against ADR-0001's worked-example table. Passive stores (Terrain, entity stores, occupancy/directory) declare exactly: *inert store; only the legal writer set changes*.
- State advances **only** inside authority-driven execution (the mutation window). `_Process` is presentation-only. RNG draws occur only inside `Tick()`/authority-driven resolution.
- `DeltaSeconds` is fixed-dt in RealTime and **always 0 in TurnBased** — a system that integrates `rate × DeltaSeconds` must not register TurnBased (debug-asserted).
- Game speed multiplies sub-step count, never scales dt. Gameplay pause is RealTimeAuthority at zero sub-steps — never `SceneTree.paused`.
- **Named seam obligations**: Squad Prep decides pre-switch placements on `SwitchPending` and Colonist Movement executes them (Reaction phase, ADR-0003 nudge rule); `PostEncounterReconcile` (first RealTime dispatch after battle) drains the outcome inbox, releases dead colonists' reservations, cancels orphaned jobs/paths, and reaps `IsDead`/`IsBroken`/withdrawn entities. Notifications queue across modes as bookkeeping and present in RealTime.

## 2. Serialization Contract (ADR-0001/0002/0003)

- Authoritative state is **plain C# data, zero Godot dependency**, separable from nodes and headlessly testable (CI greps sim assemblies for Godot references).
- Every state-holding system exposes `Snapshot()` / `Restore(snapshot)` with a **schema version**. The **round-trip test is a blocking CI gate from the first state-holding system**: byte-stable state, `TickSequence` continuity, identical same-seed re-runs.
- Cross-object references are **stable IDs only**: `EntityId` (`long`, monotonic via the serialized `EntityIdSource`, never reused; 0 = None) for entities; stable string material keys remapped through the material manifest for terrain type ids. Runtime indices, spans, and object references never enter a snapshot.
- **Derived/cached state is reconstructible, never serialized**: `Revision` counters, `UnitOccupancyIndex`, `EntityDirectory`, pathfinding caches, render data — all rebuilt on load. Non-incremental population signals `WorldReloaded` (full rebuild), never an event storm.
- **Battle Persistence (user ruling 2026-08-02; overturns CD-9's save half — its battle-length half stands)**: three silent autosave moments — switch-into-tactics, **one rolling checkpoint per resolved actor activation** (tagged `Mode == TurnBased`; the battle-checkpoint system is the *only* legal writer of a combat-mode save — from any other writer a TurnBased-tagged save remains corrupt), and battle end. Manual saves stay disabled in combat. Combat-transient state lives in encounter-scoped side tables — never in stores, never in a colony-mode save — and is serialized **only into the battle checkpoint by its owning systems** (content scope, cadence, Option A async write mechanism, and resume path: ADR-0004, pending; ADR-0001/0002/0003 Amendments 2026-08-03).
- RNG is **per-system seeded streams**, draw-sites constrained by contract #1; stream layout is ADR-0005 Seeded RNG's deliverable (Accepted 2026-08-07 — xoshiro256\*\*, `RngService` central ownership, per-stream `IRngStream` grants) and must round-trip like all other state.

## 3. World Change Events (ADR-0002)

- **Terrain (`TerrainWorld`) is the bus's only publisher — permanently.** The bus is a dumb synchronous dispatcher: no queueing, no replay, no priorities beyond declared handler order, and it never grows into a general message bus.
- Subscribers (Pathfinding, Job Assignment, Terrain Rendering, Combat LOS/Spatial Query, Repair, Notifications component) receive one `TerrainChangeBatch` per mutating call, **valid only for the duration of `Publish`** (pooled `ref struct` — retention is a compile error); handlers copy out primitives synchronously.
- **All handlers, paused or not, are idempotent bookkeeping only** — invalidate, mark stale, dirty. Ticking is the only channel that advances state; bus handlers never write stores or terrain (mutation-window assertion catches attempts).
- Events are orthogonal to ticks: paused systems still receive events. `ModeTransitioned` is a direct manager event, **not** a bus event.
- **The entity layer deliberately has no event bus**: consumers poll per-store `Revision` counters at their own cadence and rescan their caches. If a spike falsifies a rescan cost, that store gains a narrow change list behind the same facade — the bus still gains no second publisher.

---

*Enforcement, everywhere the same house pattern: one write facade per domain · writer sets per time authority granted at the composition root · mutation-window + mode + kind debug assertions · deterministic iteration order · pooled buffers, zero steady-state allocation · debug-console (#29) invariant sweeps (reservation bit ≡ claim table; occupancy ≡ living positions; grant audit).*
