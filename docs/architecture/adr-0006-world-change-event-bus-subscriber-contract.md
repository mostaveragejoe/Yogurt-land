# ADR-0006: World Change Event Bus Subscriber Contract

## Status
**Proposed**

## Date
2026-08-25

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 |
| **Domain** | Core |
| **Knowledge Risk** | **LOW** — the bus lives entirely in `Hollowdeep.Core` with zero Godot references. `VERSION.md` rates 4.7 HIGH globally, but no Godot API is load-bearing in this decision. The binding constraint here is the **C# language version**, not the engine |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md`, `deprecated-apis.md`, `modules/gridmap.md` |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | CI-grep that no subscriber retains a batch; allocation benchmark proving dispatch is 0 B/publish; a determinism test proving dispatch order is stable across runs |

> ### ⚠️ The binding constraint: C# 12 forbids the obvious implementation
>
> **`Hollowdeep.Core.csproj` targets `net8.0`, which is C# 12** (verified 2026-08-25).
> `TerrainChangeBatch` is a `ref struct` (ADR-0002 rule 1 — retention must be a compile error).
>
> **A `ref struct` cannot be used as a generic type argument in C# 12.** The `allows ref struct`
> anti-constraint that permits it arrived in **C# 13 / .NET 9**. Therefore *all* of the
> following are **compile errors**, not merely discouraged:
>
> ```csharp
> event Action<TerrainChangeBatch> Changed;      // ILLEGAL
> event EventHandler<TerrainChangeBatch> Changed; // ILLEGAL
> IObserver<TerrainChangeBatch>                   // ILLEGAL
> List<Action<TerrainChangeBatch>> _handlers;     // ILLEGAL
> ```
>
> This is recorded at the top of the ADR because it is the single fact that makes this
> decision necessary. Without it written down, the first implementer reaches for
> `event Action<T>`, hits an error they do not understand, and improvises — and the likely
> improvisation (copy the changes into a `List<TerrainChange>` first) allocates on **every
> dig**, silently breaking the zero-steady-state-allocation standard that five Tier 0 spikes
> exist to protect.
>
> **If the project ever moves to .NET 9+, this constraint lifts.** The contract below stays
> valid regardless — it does not become wrong, only less mandatory. Do not migrate it on
> version-bump alone; see Consequences.

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | **ADR-0002** Terrain Data Model (**Accepted**) — owns `TerrainChangeBatch`, `ITerrainChangeSink`, the single-publisher rule and the batch-lifetime rule. **ADR-0001** Time Authority (**Accepted**) — provides the mutation window that handlers are asserted against, and the `(phase, priority, registration sequence)` ordering precedent this ADR reuses |
| **Enables** | Implementation of World Change Event Bus (systems-index #3) and every subscriber: Pathfinding #8, Terrain Rendering #7, Job Assignment #10, Spatial Query/LOS #12, Repair & Rebuild #25, Notifications |
| **Blocks** | Systems-index #3; the render-backend stories (#7) whose handler signature is currently a `/* batch */` placeholder |
| **Ordering Note** | Both dependencies are already Accepted, so this ADR is implementable immediately on acceptance. It deliberately does **not** depend on ADR-0004/0005 (both Proposed) — the bus has no checkpoint or RNG involvement |

## Context

### Problem Statement

ADR-0002 fully specifies the **publish** side of the World Change Event Bus:
`ITerrainChangeSink.Publish(in TerrainChangeBatch)` and `PublishWorldReloaded()`, one batch
per mutating call, valid only for the duration of the call.

It specifies **nothing about the subscribe side.** There is no defined way for Pathfinding,
Terrain Rendering, Job Assignment, Spatial Query, Repair, or Notifications to register to
receive batches, no defined dispatch order, and no defined unregistration path.

This is not a theoretical gap. `design/quick-specs/terrain-rendering-cutaway.md:125` — the
spec for the single highest-traffic subscriber — carries a literal placeholder:

```csharp
public void OnTerrainChanged(/* batch */);      // marks octants dirty
```

The parameter type was left as a comment because nobody had decided it. The LP-FEASIBILITY
gate (2026-08-25) flagged this as one of three findings blocking implementation, and the
architecture document tracks it as **QQ-23**.

### Constraints

- **C# 12 / .NET 8** — the ref-struct generic restriction above. Non-negotiable at the current target
- **Terrain is the bus's only publisher, permanently** (ADR-0002 rule 1, cross-cutting contract #3). This ADR must not create a second publisher or a path to one
- **Dumb synchronous dispatcher** — no queueing, no replay, no async, no priorities beyond declared handler order; it must never grow into a general message bus (cross-cutting contract #3)
- **Batch lifetime** — valid only during `Publish`; retention is a compile error and must stay one
- **Handlers are idempotent bookkeeping only** — invalidate, mark stale, dirty. They never advance simulation state and never write stores or terrain (ADR-0001; mutation-window assertion catches attempts)
- **Events are orthogonal to ticks** — paused systems still receive events, in both authorities
- **Zero steady-state allocation** — dispatch must allocate nothing, measured
- **Deterministic dispatch order** — identical mutation sequences must produce identical event streams (ADR-0002 rule 8), which requires a stable subscriber order

### Requirements

- A subscriber can register, be dispatched to in a declared deterministic order, and unregister
- The contract is expressible in C# 12 with a `ref struct` payload
- `WorldReloaded` reaches the same subscriber set through the same ordering
- A Godot-side subscriber (Terrain Rendering) can participate without the core gaining a Godot reference
- Registering the same subscriber twice, or unregistering during dispatch, has defined behaviour

## Decision

Adopt a **subscriber interface with explicit integer-priority registration**, dispatched
synchronously in `(priority, registration sequence)` order.

### The contract

```csharp
// Namespace: Hollowdeep.Core.Terrain — plain C#, zero Godot references.

/// A system that reacts to terrain change. Implementations are IDEMPOTENT BOOKKEEPING
/// ONLY: invalidate a cache, mark a region stale, dirty a chunk. A handler that advances
/// simulation state or writes a store/terrain is a bug — the ADR-0001 mutation-window
/// assertion fires on the attempt.
public interface ITerrainChangeSubscriber
{
    /// Called synchronously, once per mutating TerrainWorld call.
    /// The batch is valid ONLY for the duration of this call. Copy out the primitives
    /// you need (ChunkCoords to dirty, CellCoords to invalidate) and return.
    void OnTerrainChanged(in TerrainChangeBatch batch);

    /// Called when the world was populated non-incrementally (initial load, Restore).
    /// Respond with a FULL rebuild/rescan of your caches — never incremental handling.
    void OnWorldReloaded();
}

/// The bus. Sole publisher is TerrainWorld (ADR-0002) — enforced by construction:
/// the sink interface is what TerrainWorld holds; registration is a separate surface.
public sealed class WorldChangeEventBus : ITerrainChangeSink
{
    /// Lower priority dispatches first. Duplicate (subscriber, priority) pairs are a
    /// programming error; duplicate priorities across DIFFERENT subscribers are rejected
    /// by debug assertion, mirroring ADR-0001's phase/priority rule.
    public void Subscribe(ITerrainChangeSubscriber subscriber, int priority);
    public void Unsubscribe(ITerrainChangeSubscriber subscriber);

    // ITerrainChangeSink — called by TerrainWorld only.
    public void Publish(in TerrainChangeBatch batch);
    public void PublishWorldReloaded();
}
```

**Why an interface rather than a delegate.** A bespoke non-generic delegate
(`delegate void TerrainChangeHandler(in TerrainChangeBatch)`) is *also* legal in C# 12 —
delegates may take `ref struct` parameters; only generic *type arguments* are forbidden. It
was rejected on three counts: an interface gives both `OnTerrainChanged` and
`OnWorldReloaded` one identity to register and unregister (a delegate pair can be
half-registered), it makes the subscriber's obligations discoverable at the type level, and
`Unsubscribe` by object identity is unambiguous where delegate equality is subtle.

### Dispatch order — declared priorities

```
Priority   Subscriber                        Why here
────────────────────────────────────────────────────────────────────────────
  10       Pathfinding & Navigation (#8)     Invalidation others may read
  20       Spatial Query / LOS & Cover (#12) Same class of derived state
  30       Job Assignment & Priority (#10)   May consult reachability
  40       Repair & Rebuild (#25)            Gameplay bookkeeping
  50       Notifications                     Presentation-facing queueing
  60       Terrain Rendering & Cutaway (#7)  Pure view; last by construction
```

**The ordering is deliberately not load-bearing.** Every handler is idempotent bookkeeping,
so correctness must not depend on this order — the gaps of 10 exist for insertion, and the
numbers are a *determinism* device, not a dependency declaration. **If a subscriber ever
genuinely requires another to run first, that is a design smell to escalate, not a priority
to tune.** Recorded as a review question, not a tuning knob.

### Rules

1. **Single publisher, structurally.** `TerrainWorld` receives the bus as `ITerrainChangeSink`
   and can therefore only publish. `Subscribe`/`Unsubscribe` live on the concrete type held by
   the composition root. Terrain physically cannot register a subscriber, and a would-be
   second publisher cannot obtain a publish surface without a composition-root change.
2. **Synchronous, in-order, no queueing, no replay.** `Publish` walks the sorted list and
   returns. There is no buffering, no async, no scheduling, no filtering.
3. **Registration is composition-root-only**, alongside the writer-interface grants
   (ADR-0003) and the RNG draw handles (ADR-0005) — one place to review who listens to what.
4. **Duplicate registration is rejected** by debug assertion. Registering the same subscriber
   twice would double-dispatch and silently break the idempotence budget.
5. **Mutation during dispatch is forbidden.** `Subscribe`/`Unsubscribe` called while `Publish`
   is on the stack fails a debug assertion — the sorted list is not re-entrantly safe and
   ordering would depend on dispatch position. This is the same in-Publish flag ADR-0002
   rule 5 already requires for the write-path assertion; **it is one flag, reused, not a
   second mechanism.**
6. **The subscriber list is pre-sorted and never allocates at publish time.** Sorting happens
   at `Subscribe`; `Publish` walks an array. Zero allocation, measured.
7. **`WorldReloaded` uses the same subscriber set and the same order** — one registration
   covers both callbacks, so a subscriber cannot receive incremental changes while missing
   full-rebuild signals.
8. **Godot-side subscribers implement the plain-C# interface directly.** `TerrainRenderer`
   (a `Node3D`) implements `ITerrainChangeSubscriber`; the core holds it as the interface and
   never learns it is a Node. **Marshalling note (ADR-0002):** `TerrainChangeBatch` and its
   `ReadOnlySpan` can never cross a Godot Signal/Variant boundary — a Godot-side subscriber
   must consume the batch inside `OnTerrainChanged` and translate to plain data before any
   signal emission.
9. **Unsubscribe is mandatory for view-layer subscribers.** A freed Godot Node still
   registered is a dangling reference. `TerrainRenderer` unsubscribes in `_ExitTree`. The bus
   additionally logs a debug warning if a subscriber throws, rather than silently absorbing
   it — matching ADR-0001's tickable-purge precedent.

### Architecture Diagram

```
  TerrainWorld  ──holds──►  ITerrainChangeSink        (publish surface ONLY)
       │                          ▲
       │ one batch per            │ implemented by
       │ mutating call            │
       ▼                    WorldChangeEventBus  ◄──Subscribe(s, priority)── composition root
   Publish(in batch)              │                                          (the only registrar)
                                  │ walks pre-sorted array, synchronous
                                  │ in-Publish flag set (rule 5)
        ┌────────┬────────┬───────┴────┬──────────┬─────────────┐
        ▼        ▼        ▼            ▼          ▼             ▼
      10        20       30           40         50            60
   Pathfind   LOS     JobAssign    Repair   Notifications   TerrainRenderer
                                                             (Godot Node,
                                                              unsubscribes
                                                              in _ExitTree)

   Every handler: copy primitives out, mark stale, return. Never writes.
   Batch dies at the end of Publish — retention is a compile error.
```

### Key Interfaces

`ITerrainChangeSubscriber` (`OnTerrainChanged(in TerrainChangeBatch)`, `OnWorldReloaded()`) ·
`WorldChangeEventBus.Subscribe(subscriber, priority)` / `Unsubscribe(subscriber)` ·
`ITerrainChangeSink` (ADR-0002, unchanged — publish surface held by `TerrainWorld`)

## Alternatives Considered

### Alternative 1: C# `event` with `Action<TerrainChangeBatch>`

- **Description**: The idiomatic .NET approach — `event Action<TerrainChangeBatch> Changed;`, subscribers use `+=`.
- **Pros**: One line. Every C# developer knows it. No custom types.
- **Cons**: **It does not compile.** `TerrainChangeBatch` is a `ref struct`, and C# 12 forbids `ref struct` as a generic type argument (`allows ref struct` is C# 13/.NET 9).
- **Rejection Reason**: Illegal in the target language version. **Documented rather than omitted**, because it is the approach every implementer will try first — and the natural workaround (copy changes into a `List<TerrainChange>` before dispatch) allocates on every dig and breaks the zero-allocation standard.

### Alternative 2: Bespoke non-generic delegate

- **Description**: `delegate void TerrainChangeHandler(in TerrainChangeBatch batch);` plus a `TerrainChangeHandler[]` invocation list. Legal — delegates may take `ref struct` parameters.
- **Pros**: Lighter than an interface; no type to implement; subscribers can be lambdas or method groups.
- **Cons**: Needs a **second** parallel delegate for `WorldReloaded`, which can be half-registered. Lambda subscribers cannot be unsubscribed reliably (delegate equality on closures is a known trap) — fatal for view-layer subscribers that must detach in `_ExitTree`. Obligations are invisible at the type level.
- **Rejection Reason**: The two-callback lifetime is the deciding factor. One registration must cover both callbacks or a subscriber can silently miss `WorldReloaded` and run on stale caches after every load — precisely the bug ADR-0002 rule 6 exists to prevent.

### Alternative 3: Pull-based `Revision` polling (mirror the entity layer)

- **Description**: Drop push dispatch. Subscribers poll `TerrainWorld.Revision` at their own cadence and rescan, exactly as ADR-0003 does for entity stores.
- **Pros**: One notification idiom project-wide. No registration, no ordering, no lifetime management. Trivially deterministic.
- **Cons**: Polling knows only *that* the world changed, never *which cells* — so every consumer rescans everything on any change. The pathfinding spike measured this shape at **63.2 µs per dig with 10 cached paths**, and that is the *entity* case with dozens of entities; terrain has 262k cells and the renderer needs per-chunk dirty granularity to stay at 32 draw calls. Discards `TerrainChange.Previous`, which CD-1's after-action report depends on.
- **Rejection Reason**: The information the batch carries (which cells, and their prior state) is exactly what makes the renderer and CD-1 cheap. ADR-0003 chose polling for entities *because* dozens of entities make rescans free; terrain's scale inverts that reasoning. Recorded because the asymmetry is a fair question a reviewer will ask.

### Alternative 4: Fixed compile-time subscriber list

- **Description**: The bus holds a hardcoded enum of the six known subscribers, dispatched in enum order.
- **Pros**: Impossible to get ordering wrong; no registration API at all.
- **Cons**: Adding a seventh subscriber edits the bus itself; the bus gains compile-time knowledge of every consumer, inverting the dependency (Foundation depending on Feature).
- **Rejection Reason**: The inverted dependency is disqualifying — TD-SYSTEM-BOUNDARY explicitly checks for Foundation-layer systems depending on higher layers.

## Consequences

### Positive

- Six systems get a defined plug-in point; the `/* batch */` placeholder in the Terrain Rendering spec can be filled with a real signature.
- The C# 12 trap is documented at the top of the ADR, so the first implementer does not lose an afternoon to a compile error and then improvise an allocating workaround.
- Ordering is explicit and reviewable in one file, reusing ADR-0001's proven `(priority, registration)` idiom — one ordering concept in the project, not two.
- The single-publisher rule becomes **structural** rather than documentary: publish and subscribe are different surfaces on different types.
- Zero-allocation dispatch is preserved by construction (pre-sorted array, no per-publish work).

### Negative

- One more interface and one more registration site in the composition root — the same ceremony cost ADR-0003 already accepted for writer interfaces.
- Priority numbers are a small coordination surface. Mitigated by them being non-load-bearing by design (see Dispatch order).
- The bus is hand-rolled rather than idiomatic, so a C# reviewer unfamiliar with the constraint may flag it as reinventing events. The Engine Compatibility block exists to answer that on sight.

### Risks

- **A handler writes state.** *Mitigation*: ADR-0001's mutation-window assertion already fires on this; rule 5's in-Publish flag catches the bus-handler case specifically.
- **A subscriber retains the batch.** *Mitigation*: `ref struct` makes it a compile error — structurally impossible, not merely forbidden.
- **A freed Godot Node stays registered.** *Mitigation*: rule 9 (`_ExitTree` unsubscribe) plus a debug warning on subscriber throw, mirroring ADR-0001's tickable-purge precedent.
- **Ordering becomes load-bearing over time** — someone tunes a priority to fix a bug, making correctness depend on dispatch order. *Mitigation*: stated explicitly as a design smell to escalate; the debug-console sweep can dump the registered order for audit.
- **The bus grows into a general message bus.** *Mitigation*: cross-cutting contract #3's hard cap; `ITerrainChangeSink` is the only publish surface and it is terrain-shaped by its type.
- **.NET 9 migration invites a rewrite to `event Action<T>`.** *Mitigation*: recorded here — the constraint lifting does not make this contract wrong. A migration would trade a working, ordered, unsubscribable contract for idiom alone, and would lose declared ordering. **Do not migrate on version-bump alone.**

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| `design/gdd/systems-index.md` #3 World Change Event Bus | "One publisher (Terrain), many subscribers. A dumb synchronous dispatcher — no priorities, filters, replay, or ordering guarantees" | The subscriber contract itself; single-publisher enforced structurally; synchronous walk with no queueing or replay. *Declared* ordering is provided because ADR-0002 rule 8 requires deterministic event streams — this is the "declared handler order" cross-cutting contract #3 already permits, not the general priority system it forbids |
| `design/gdd/terrain-data-model.md` (TR-terrain-018) | Batched change events with previous-state capture (CD-1) | Subscribers receive the full `TerrainChangeBatch` including `Previous`, preserving the after-action report's data source |
| `design/quick-specs/terrain-rendering-cutaway.md` C8 / line 125 | "Change batches mark octants dirty; dirty octants rebuild once per frame" — signature was a `/* batch */` placeholder | Supplies the real signature: `OnTerrainChanged(in TerrainChangeBatch)`, priority 60 |
| `design/quick-specs/pathfinding-navigation.md` C3.3 / C7 | `MarkLayerDirty(z)` on terrain mutation; revalidation semantics | Pathfinding subscribes at priority 10 and marks its layer stale inside the handler |
| `design/gdd/time-authority-mode-switch.md` (TR-time-017) | Designation invalidation via events **including while paused** | Events are orthogonal to ticks; the bus dispatches identically under both authorities and regardless of pause |

## Performance Implications

- **CPU**: One array walk of ~6 entries per mutating call, plus each handler's own bookkeeping. Against the measured 0.338 µs mutation+publish cost, dispatch overhead is noise. Sorting is O(n log n) at `Subscribe` only — never at publish.
- **Memory**: One pre-sorted array of ~6 references. **Zero allocation at publish time** (validation criterion 3).
- **Load Time**: `PublishWorldReloaded` triggers six full cache rebuilds. This is intentional (ADR-0002 rule 6) and already the measured load path.
- **Network**: N/A.

## Migration Plan

Greenfield — the bus does not exist yet. Two companion edits at adoption:

1. **`design/quick-specs/terrain-rendering-cutaway.md:125`** — replace the
   `OnTerrainChanged(/* batch */)` placeholder with the real signature and note the
   priority-60 registration and the `_ExitTree` unsubscribe obligation.
2. **`.claude/docs/technical-preferences.md`** — Forbidden Patterns gains: *bus subscriber
   registration or unregistration during dispatch*; *`event`/`Action<T>`/`EventHandler<T>` over
   `TerrainChangeBatch`* (illegal in C# 12 — use `ITerrainChangeSubscriber`).

## Validation Criteria

1. Six subscribers register at declared priorities and are dispatched in `(priority, registration)` order; the order is identical across runs and across a save/load cycle.
2. A handler attempting to write terrain or a store fails the mutation-window assertion; `Subscribe`/`Unsubscribe` during `Publish` fails the in-Publish assertion.
3. **Zero allocation**: 10,000 publishes to 6 subscribers record 0 B and 0 Gen0 collections.
4. Duplicate subscriber registration is rejected; duplicate priorities across different subscribers are rejected.
5. `PublishWorldReloaded` reaches every registered subscriber, in the same order, and each responds with a full rebuild — verified by the existing no-stale-cache-after-load integration test (ADR-0002 criterion 3).
6. A Godot-side subscriber (`TerrainRenderer`) participates with the core assembly still passing the zero-Godot-references CI grep.
7. An unsubscribed subscriber receives nothing; a subscriber that throws is logged, not silently absorbed.
8. Six months in: the bus still has exactly one publisher, no queueing, no replay, and no subscriber's correctness depends on another's priority.

## Related Decisions

- **ADR-0002** Terrain Data Model — owns `TerrainChangeBatch`, `ITerrainChangeSink`, the single-publisher rule, batch lifetime, and rule 8's deterministic event streams
- **ADR-0001** Time Authority — mutation window; the `(phase, priority, registration sequence)` ordering precedent reused here; events-are-orthogonal-to-ticks
- **ADR-0003** Entity Data Ownership — the deliberate asymmetry: entities use `Revision` polling and have **no** event bus; Alternative 3 records why terrain differs
- `docs/architecture/cross-cutting-contracts.md` #3 — the one-page cap this ADR implements
- `docs/architecture/architecture.md` §8 **QQ-23** — the open question this ADR closes; §11 records the LP-FEASIBILITY finding that raised it
