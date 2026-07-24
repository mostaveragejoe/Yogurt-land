# ADR-0001: Time Authority / Mode-Switch Architecture

## Status
Proposed

*(Written per the systems-index sequencing: ADRs authored as Proposed before the Tier 0 spikes; promoted to Accepted when the mode-switch spike validates the architecture.)*

## Date
2026-07-24

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 |
| **Domain** | Core |
| **Knowledge Risk** | HIGH (4.7 is post-LLM-cutoff) — mitigated: the core contract is engine-agnostic by design and touches no post-cutoff APIs |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md`, `deprecated-apis.md`, `current-best-practices.md` |
| **Post-Cutoff APIs Used** | None. The plain-C# core references no Godot API. The thin wrapper uses `Node._PhysicsProcess`, `ProcessMode`, Autoload — all stable pre-4.3 behavior, unflagged in breaking-changes.md |
| **Verification Required** | Fixed-step behavior under render-rate ≠ physics-rate; `max_physics_steps_per_frame` clamp behavior on slow frames (sim must slow down, not spiral); headless .NET test run with zero Godot runtime |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | None (first ADR, Foundation layer). Primitive types this ADR references (`CellCoord`, `EntityId`) live in the shared foundation-primitives namespace (`Hollowdeep.Core.Primitives`, defined jointly with ADR-0002) — this ADR does not depend on the terrain assembly. *(Corrected 2026-07-24 with ADR-0002 to remove a circular type-ownership implication.)* |
| **Enables** | ADR-0002 (Terrain Data Model), ADR-0003 (Entity Data Ownership), the Seeded RNG ADR (constrained by this ADR: draws only inside authority-driven execution), Save/Load contract (mode invariant defined here) |
| **Blocks** | All simulation-bearing GDDs/quick-specs (their mandatory "Behavior under each time authority" sections are written against this contract); the Tier 0 mode-switch spike implements this ADR |
| **Ordering Note** | Written as Proposed BEFORE the Tier 0 spikes by design; the spike validates rather than precedes it. Physics-body specifics (Jolt default since 4.6, `HingeJoint3D.damp` GodotPhysics-only) are NOT relevant to this ADR but become relevant in ADR-0002/0003 territory — re-check `breaking-changes.md` there. |

## Context

### Problem Statement
Hollowdeep is one shared world — terrain grid, colonists, resources — that must run under two temporal models: continuous real-time (colony simulation) and discrete turn-based (tactics combat on the same grid, in the player's own architecture). The concept doc names this the project's permanent integration tax and its highest-severity architectural risk: build it wrong and you get "two games that must reconcile" — duplicated state, desync, save-format complexity. This decision must be made first because 25 MVP system specs each require a "Behavior under each time authority" section written against this contract.

### Constraints
- Solo first-time developer: minimum viable structure, no speculative framework growth
- 16.6 ms frame budget (60 fps target) in colony mode
- Serialization contract (systems index, cross-cutting contract #2): authoritative state is plain data, separable from Godot nodes, headlessly testable
- CD-9 (locked): no mid-battle saves — autosave at mode-switch into tactics and at battle end only
- World Change Event Bus (cross-cutting contract #3) is a dumb synchronous dispatcher — one publisher (Terrain), no queueing/replay; it must not grow into a general message bus

### Requirements
- One world, swappable time authority — zero state conversion at the mode boundary
- Deterministic: same seed + same inputs → same state (save/load, replay, bug reproduction)
- Headlessly testable core (plain .NET runner, no Godot runtime)
- Support RimWorld-standard game speed control (pause/1x/2x/3x) in colony mode
- Support turn-based scheduling with presentation-gated turn advancement (an XCOM-style move resolves logically in zero time but animates over ~2s)

## Decision

Adopt a **Time Authority strategy pattern** over a single shared world, implemented as plain C# with zero Godot dependency in the core contract.

### Locked design decisions (user-approved 2026-07-24)
1. **Full colony pause during combat** — no split-clock partial simulation.
2. **Exactly one active tactics encounter at a time** — mode is a single global binary, never a spatial query.
3. **Push-based tick dispatch** — systems are passive; they never know which authority drives them.
4. **Authority-swap only** — the mode switch converts/copies NO state. Terrain Data Model and Colonist Entity remain the single source of truth throughout (detail in ADR-0002/0003).

### Core contract (plain C#, no Godot namespace)

```csharp
public enum TimeAuthorityMode { RealTime, TurnBased }

public readonly record struct TimeContext(
    TimeAuthorityMode Mode,
    double DeltaSeconds,   // fixed sub-step dt in RealTime; ALWAYS 0 in TurnBased (see rule below)
    int TurnIndex,         // increments per ACTOR ACTIVATION in TurnBased; -1 in RealTime
    ulong TickSequence);   // monotonic across both modes; determinism/save/replay anchor
// Passed BY VALUE (four fields; `in` would force defensive copies on non-readonly structs
// and is banned until a profiler demands it).

public interface ITickable
{
    void Tick(TimeContext context);
}

public enum TickPhase { Input, Simulation, Reaction, Presentation }  // dispatch order

public interface ITimeAuthority
{
    void Advance(double engineDeltaSeconds);  // receives REAL delta even in TurnBased
    bool IsActive { get; }
}

public enum SwitchResult { Accepted, RejectedAlreadyInMode, RejectedEncounterActive, DeferredMidDispatch }

public sealed class TimeAuthorityManager
{
    public TimeAuthorityMode CurrentMode { get; }
    public void Register(ITickable system, TimeAuthorityMode authority, TickPhase phase, int priority = 0);
    public void Unregister(ITickable system, TimeAuthorityMode authority);
    public SwitchResult RequestSwitch(TimeAuthorityMode target, SwitchTransitionData transition);
    public event Action<ModeTransition> ModeTransitioned;   // direct manager event — NOT on the World Change Event Bus
    public void Advance(double engineDeltaSeconds);          // called once per engine physics frame
    public TimeAuthoritySnapshot Snapshot();                 // {Mode, TurnIndex, TickSequence}
    public void Restore(TimeAuthoritySnapshot snapshot);     // Mode is invariantly RealTime in any valid save
}

public readonly record struct SwitchTransitionData(
    int EncounterId,
    SwitchTriggerReason TriggerReason,
    IReadOnlyList<CellCoord> BreachCells,
    IReadOnlyList<EntityId> ParticipantIds);
// GUARD RAIL: carries encounter FRAMING only — never entity state. A field duplicating
// Terrain Data Model or Colonist Entity data is a bug: state conversion creeping back in.
// (Everything beyond this minimum shape is deferred; participant selection is Squad Prep's half.)

public interface IPresentationGate      // plain C#; view layer satisfies it, tests stub it
{
    bool IsComplete { get; }
}
```

### The two authorities

**RealTimeAuthority** — advances the colony simulation on **fixed-dt sub-steps**:
- `Advance(engineDelta)` accumulates time and runs N sub-steps of fixed `dt` per physics frame. Game speed (pause/1x/2x/3x) multiplies N, **never scales dt** — delta-scaling is banned because it destroys fixed-step determinism and closes off post-battle time catch-up (see Consequences).
- Speed 0 (gameplay pause) is this authority at zero sub-steps — **never `SceneTree.paused`**.
- Slow frames: the simulation **slows down** rather than spiral-catching-up (Godot clamps at `max_physics_steps_per_frame`, default 8; we additionally cap our own sub-steps per frame). Physics tick rate is pinned in project settings; changing it changes simulation-speed semantics and requires revisiting this ADR.

**TurnBasedAuthority** — an explicit **state machine**: `AwaitingInput → ResolvingAction → AwaitingPresentation → NextActor`.
- Driven by `Advance(engineDelta)` every physics frame — it receives real delta for its own timeouts and gating even though it **publishes `DeltaSeconds = 0`** to tickables. (This looks contradictory; it is deliberate. Do not "fix" it.)
- Discrete ticks fire on scheduler events (action submitted, action resolved, activation ends), not per frame. `TurnIndex` increments per **actor activation**.
- Turn advancement is gated on `IPresentationGate` completion — the view layer (Godot side) reports when the 2-second move animation finishes; headless tests use an instant-completion stub. **No Godot type ever enters the authority.**

### Dispatch rules
- The active authority dispatches `Tick()` to its registered systems in **deterministic order**: sorted by `(TickPhase, priority, registration sequence)`; a debug assertion rejects two systems sharing an exact phase+priority. Scene-tree `_Ready` order must never control simulation order.
- The **inactive** authority's systems receive **zero Tick calls** — true pause.
- **Events are orthogonal to ticks**: paused systems still receive World Change Event Bus events, but ALL bus handlers (paused or not) are restricted to **idempotent bookkeeping** — invalidate a cached path, mark a reservation stale, dirty a chunk. Handlers never advance simulation state; ticking is the only channel that advances state.
- **Re-entrancy**: `RequestSwitch` from inside any Tick or transition handler is deferred to end-of-dispatch (`DeferredMidDispatch`). The manager enforces the single-encounter invariant itself — it rejects, never queues.
- **RNG rule (constraint on the Seeded RNG ADR)**: random draws occur only inside `Tick()` or authority-driven resolution — never in `_Process`, UI callbacks, or event handlers. Otherwise `TickSequence` guarantees nothing.
- **DeltaSeconds=0 rule**: systems that integrate `rate * DeltaSeconds` must not register with the TurnBased authority — a debug assertion flags any TurnBased-registered system reading DeltaSeconds. Silent no-op integration is a bug class that presents as "the game is subtly wrong."

### The mode switch (atomic, between dispatches)
1. Current dispatch completes (mid-dispatch requests deferred).
2. Manager fires `ModeTransitioned` — a **direct manager event, not a bus event** (different publisher, ordering requirements). Handler order is explicitly declared at subscription (same phase/priority scheme); Combat UI receives encounter context before the camera reframes.
3. `ActiveAuthority` swaps. No state is converted, copied, or rebuilt.
4. **Switching back to RealTime runs a named `PostEncounterReconcile` step** (its own TickPhase.Reaction pass, first real-time dispatch): release reservations held by dead colonists, cancel jobs targeting destroyed cells, invalidate paths crossing destroyed geometry, re-run reachability. "No state conversion" does NOT mean the switch is free — no *representation* changes, but reconciliation duties are real, named, and integration-tested.

### Godot integration (the only engine-touching layer)
- A thin **Autoload** node `TimeAuthorityRoot : Node` (`ProcessMode = Always`) owns the plain-C# `TimeAuthorityManager` and calls `Manager.Advance(delta)` from `_PhysicsProcess` (fixed-step; pairs with `TickSequence` for determinism). Register it in CLAUDE.md's autoload documentation when created.
- **`SceneTree.paused` is never used in the simulation path.** If a future settings/pause-menu needs true engine pause, that is a separate UI-pause concern layered on top — two competing pause mechanisms are forbidden.
- **`SystemTickableNode`** (deliberately NOT named `TickableNode`): a base class for the rare *system-level singleton* that genuinely needs scene-tree lifetime. It disables its own `ProcessMode` in `_EnterTree`, exposes only `Tick()`, auto-unregisters in `_ExitTree`. **Rule: per-entity simulation state is never a Node.** Colonists' sim state is plain data (ADR-0003); their Nodes are *views* that read that data in their own `_Process` for presentation interpolation. If ten colonist `SystemTickableNode`s exist, the architecture has drifted and the save format is already compromised.
- The manager's dispatch loop defensively skips any registered tickable that is a `GodotObject` failing `IsInstanceValid()` — but this purge **logs a debug warning** (surfaced in the Tier 0 debug console); silent absorption of a missing Unregister is itself a bug.
- Presentation-layer smoothing (camera follow, tweens, visual interpolation) is **out of scope for `Tick()`** and lives in nodes' own `_Process`. `ITickable` is the only sanctioned simulation-update path; `_Process` is the only sanctioned presentation-update path.

### Architecture Diagram

```
Godot engine (physics frame, fixed step)
  └── TimeAuthorityRoot (Autoload, ProcessMode=Always)   [only Godot-aware layer]
        └── TimeAuthorityManager.Advance(delta)          [plain C# from here down]
              ├── ActiveAuthority == RealTimeAuthority
              │     └── N fixed-dt sub-steps (N = speed multiplier; 0 = paused)
              │           └── Tick(ctx) → [Input | Simulation | Reaction | Presentation]
              │                            phase/priority-sorted registry (colony systems)
              ├── ActiveAuthority == TurnBasedAuthority
              │     └── state machine: AwaitingInput → ResolvingAction
              │            → AwaitingPresentation (IPresentationGate) → NextActor
              │           └── discrete Tick(ctx, Δ=0, TurnIndex++) → combat systems
              └── RequestSwitch(target, SwitchTransitionData)
                    → atomic between dispatches
                    → ModeTransitioned (direct event, ordered handlers)
                    → [TurnBased→RealTime only] PostEncounterReconcile pass

World Change Event Bus (Terrain → subscribers): ORTHOGONAL to ticking.
Paused systems still receive events; ALL handlers are idempotent bookkeeping only.
```

### Worked example table (reference for every "Behavior under each time authority" spec section)

| System | Registers with | RealTime `Tick` does | TurnBased `Tick` does | Bus handlers (any mode) |
|---|---|---|---|---|
| Colonist Needs & Sim | RealTime only | Integrate need decay over `DeltaSeconds`; emit task candidates | — (needs freeze; see Open Questions) | none |
| Job Assignment | RealTime only | Arbitrate queue, assign/cancel jobs | — | Mark reservations stale, flag jobs whose target cells changed (no state advance) |
| Pathfinding | RealTime + TurnBased | Service colony path requests | Service combat reachability queries (Δ irrelevant — query-driven) | Invalidate cached paths/regions on terrain change |
| Raid Trigger | RealTime only | Accumulate threat over `DeltaSeconds`; on trigger: `RequestSwitch(TurnBased, …)` and gate on the returned `SwitchResult` | — | none |
| Combat: Turn Order | TurnBased only | — | Advance activation order on each discrete tick | none |
| Terrain Data Model | **neither** — passive store (ADR-0002) | — | — | None — it is the bus's sole publisher; only its legal writer set changes per authority (ADR-0002 writer table) |
| Terrain Rendering & Cutaway | **neither** — it is a view | — | — | Dirty chunks on terrain change; rebuilds happen in its own `_Process` |
| Squad Preparation | RealTime only (its output rides `SwitchTransitionData.ParticipantIds`) | Maintain roster/draft assignments | — | none |

### Key Interfaces
`ITickable.Tick(TimeContext)` · `TimeAuthorityManager.Register/Unregister(system, authority, phase, priority)` · `RequestSwitch(mode, SwitchTransitionData) → SwitchResult` · `ModeTransitioned` (direct, ordered) · `Snapshot()/Restore()` · `IPresentationGate` · `PostEncounterReconcile` (named reconciliation pass)

## Alternatives Considered

### Alternative B: Godot SceneTree pause + per-node `process_mode`
- **Description**: Use `SceneTree.paused` with per-node `ProcessMode` overrides to gate which nodes run in each mode.
- **Pros**: Zero custom infrastructure; engine-native; familiar to Godot tutorials.
- **Cons**: A global boolean designed for pause menus. Cannot express turn scheduling, `TurnIndex`, `TickSequence`, per-authority registries, or discrete event-driven ticks. Every future node must remember the correct `ProcessMode` or silently misbehaves. Simulation logic lives in Node callbacks — not headlessly testable, entangles save state with the scene tree.
- **Rejection Reason**: It is a pause primitive, not a scheduler. Hollowdeep needs a mode swap between two live simulations with different tick semantics.

### Alternative C: Separate object graphs with state handoff
- **Description**: Convert colony state into a combat-specific representation at breach, run combat, convert results back.
- **Pros**: Each mode's code is simple in isolation; combat could use a bespoke optimized representation.
- **Cons**: State duplication; two converters that must stay in sync with every schema change; desync bugs; save format must handle mid-conversion states; "two games that must reconcile."
- **Rejection Reason**: Explicitly identified at TD-FEASIBILITY as the project-killing failure mode. The entire value of "your base IS the tactics map" is that it is literally the same data.

## Consequences

### Positive
- The mode-switch integration tax becomes one visible contract instead of a per-feature surprise; 25 spec sections get written against one table.
- Plain-C# core + serialization contract reinforce each other: headless unit tests with a standard .NET runner, no Godot runtime, no GoDotTest dependency — partially answers the open Testing question in technical-preferences.md.
- Engine-version insulation by construction: the core references no Godot API, so all of 4.4–4.7's breaking changes (Jolt, glow reorder, dual-focus, Quaternion init) are irrelevant to it.
- **CD-9 banked**: saves occur only in RealTime mode. `TurnBasedAuthority` needs NO snapshot support in MVP. A save file whose Mode is `TurnBased` is corrupt, not a supported state. Do not build combat-state serialization "just in case."
- Fixed-dt sub-stepping gives speed control AND keeps post-battle time catch-up possible (N normal sub-steps, never one giant delta) — the CD's pending zero-elapsed-vs-battle-duration question stays open architecturally.
- Adding 3x speed later requires zero changes to any `ITickable`.

### Negative
- ~300–500 lines of custom infrastructure a `SceneTree.paused` approach would not need — accepted as the minimum viable structure for a permanent dual-mode game.
- Every simulation system must declare authority registration, phase, and bus-handler discipline — ongoing spec/review overhead (this is the integration tax made visible, which is the point).
- Discipline burden: the `_Process`-for-simulation ban and the events-are-bookkeeping-only rule are conventions backed by base classes and assertions, but a determined shortcut can still violate them; code review must watch for both.

### Risks
- **Presentation gate becomes a Godot dependency magnet** — someone awaits a Godot signal inside the authority. *Mitigation*: `IPresentationGate` is plain C#; the spike's headless full-turn-loop test is the canary — if it can't run without Godot, C2 was answered wrong.
- **`SwitchTransitionData` field creep rebuilds Alternative C one convenience field at a time.** *Mitigation*: the framing-only guard rail is stated in the contract; review checks new fields against it.
- **Dispatch-order drift breaks determinism as "flaky tests."** *Mitigation*: phase/priority sorting + debug assertion; TickSequence continuity asserted in CI across save/load and mode switches.
- **Reconcile step under-scoped** — orphaned reservations/jobs/paths surface as playtest bugs. *Mitigation*: `PostEncounterReconcile` is a named, integration-tested step (test destroys terrain mid-encounter, asserts zero orphans on resume).
- **Sub-step count explosion at 3x on heavy colonies** blows the frame budget. *Mitigation*: per-frame sub-step cap (sim slows down); terrain spike provides the per-tick cost numbers.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| systems-index.md — Cross-Cutting Contract #1 | Every sim system defines behavior under each time authority | Provides the tick contract, registration model, and the worked example table those sections are written against |
| systems-index.md #2 Time Authority | "One world, swappable time authority" | The decision itself |
| systems-index.md #18 Raid Trigger | Threat timers spanning modes; triggering the switch | `DeltaSeconds` accumulation in RealTime; `RequestSwitch` result + `CurrentMode` gate; timers freeze during battle (full pause) |
| systems-index.md #19–23 Combat set | Turn scheduling, presentation-gated resolution | `TurnBasedAuthority` state machine + `IPresentationGate`; `TurnIndex` per actor activation |
| systems-index.md #24 Squad Prep | The mode-switch seam ("hairiest moment") | `SwitchTransitionData.ParticipantIds` is Squad Prep's envelope; framing-only guard rail bounds it |
| Cross-Cutting Contract #2 (serialization) | Snapshot/Restore, determinism | Manager implements `Snapshot()/Restore()`; TickSequence + seeded-RNG rule + CD-9 mode invariant |

## Performance Implications
- **CPU**: Dispatch overhead is one sorted-list walk per sub-step — negligible next to the ticked work itself. Sub-stepping multiplies simulation cost linearly with game speed (capped per frame; sim slows rather than hitches).
- **Memory**: Registry lists + snapshots — trivial. Zero per-entity allocation in the dispatch path (TimeContext is a stack struct passed by value).
- **Load Time**: None.
- **Network**: N/A (single-player).

## Migration Plan
None — greenfield. The Tier 0 mode-switch spike is the first implementation; if the spike falsifies a property (e.g., presentation gating can't stay Godot-free), this ADR is revised BEFORE Accepted status, and downstream spec sections are re-checked against the updated worked-example table.

## Validation Criteria
1. Mode-switch spike runs the full turn loop **headlessly** — no Godot types in the core assembly, stub presentation gate.
2. Save/load round-trip AND a RealTime→TurnBased→RealTime cycle both preserve `TickSequence` continuity; same-seed re-run produces identical state (CI).
3. Adding 3x speed post-spike touches **zero** `ITickable` implementations.
4. Integration test: destroy terrain mid-encounter → resume → zero orphaned reservations, zero jobs targeting dead cells, zero paths through destroyed geometry.
5. Six months in: `SwitchTransitionData` has gained no field duplicating Terrain or Colonist Entity state.

## Open Questions (routed, not decided here)
- **To creative-director, before the Needs & Simulation GDD**: when a battle ends, does the colony resume as if zero time passed, or does colony time advance by the battle's represented duration (as N catch-up sub-steps)? Both remain architecturally possible under fixed-dt sub-stepping; delta-scaling would have closed the second option, which is (part of) why it is banned.
- **To the Squad Preparation quick-spec**: participant selection and placement at transition — fills the `ParticipantIds`/`BreachCells` envelope defined here.
- **To ADR-0002/0003**: physics-body specifics (Jolt default since 4.6) if colonist movement ever touches physics bodies; not relevant to this ADR's core.

## Related Decisions
- ADR-0002 Terrain Data Model (pending — single source of truth across the switch)
- ADR-0003 Entity Data Ownership (pending — per-entity sim state as plain data; write-ownership table)
- Seeded RNG ADR (pending — constrained by the draws-only-inside-Tick rule)
- `design/gdd/systems-index.md` — Cross-Cutting Contracts annex; CD-9 (no mid-battle save)
- `design/gdd/game-concept.md` — mode-switch named as permanent integration tax
