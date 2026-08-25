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
| **Verification Required** | CI-grep that no subscriber retains a batch; allocation benchmark proving dispatch is 0 B/publish (with assertion-message allocation ruled out first); a determinism test proving dispatch order is stable across runs; **a `net9.0` compile check before anyone acts on the .NET 9 note below** |
| **Specialist Review** | **godot-csharp-specialist, 2026-08-25 — APPROVE WITH NOTES.** Confirmed: the `net8.0`→C# 12 mapping, the ref-struct generic restriction, the non-generic delegate being legal, `in` on an interface method, the Godot marshalling boundary, and zero-allocation dispatch (`readonly ref struct` means `in` inserts no defensive copy). **One correction folded**: the draft's ".NET 9 lifts this constraint" claim was wrong. **Three additions folded**: rules 10, 11, 13 |

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
> **A .NET 9 upgrade would NOT make the illegal forms above legal** — corrected 2026-08-25
> after godot-csharp-specialist review; the first draft of this ADR claimed otherwise and was
> wrong. `allows ref struct` is **opt-in per generic declaration**: a type accepts ref-struct
> arguments only if *its own* type parameter is annotated. Consequently:
>
> - **`List<Action<TerrainChangeBatch>>` can never be legal, in any C# version.** `List<T>`'s
>   backing store is `T[]`, and the CLR forbids arrays of ByRefLike element type. That is a
>   *runtime* rule, not a language rule — no future language version can lift it.
> - **`Action<T>`, `EventHandler<T>` and `IObserver<T>` remain illegal** for this payload
>   unless the BCL retrofits those specific type parameters with `allows ref struct`, which it
>   has not. *(Confidence note: the `List<T>`/CLR restriction is a hard, version-independent
>   rule. The "BCL delegates are unannotated" half is post-cutoff territory — verify with a
>   compile check against `net9.0` before anyone relies on it in a migration decision.)*
>
> **This strengthens the decision rather than weakening it**: the interface contract below is
> very likely the correct shape permanently, not a stopgap that a version bump dissolves.
> Do not rewrite it to `event Action<T>` on a version bump; see Consequences → Risks.

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | **ADR-0002** Terrain Data Model (**Accepted**) — owns `TerrainChangeBatch`, `ITerrainChangeSink`, the single-publisher rule and the batch-lifetime rule. **ADR-0001** Time Authority (**Accepted**) — provides the mutation window that handlers are asserted against, and the `(phase, priority, registration sequence)` ordering precedent this ADR reuses |
| **Enables** | Implementation of World Change Event Bus (systems-index #3) and every subscriber: Pathfinding #8, Terrain Rendering #7, Job Assignment #10, Spatial Query/LOS #12, Repair & Rebuild #25, Notifications |
| **Blocks** | Systems-index #3; the render-backend stories (#7) whose handler signature is currently a `/* batch */` placeholder |
| **Ordering Note** | Both dependencies are already Accepted. **Implementable once QQ-24 (the composition root) lands** — rule 3 makes registration composition-root-only, and that contract is defined nowhere yet (architecture.md §8, rated HIGH). Not a Proposed-ADR dependency, but a real sequencing constraint *(A6, TD-ADR)*. It deliberately does **not** depend on ADR-0004/0005 (both Proposed) — the bus has no checkpoint or RNG involvement |

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

**Implementation notes that cost an afternoon each if missed** *(godot-csharp-specialist,
2026-08-25)*:

- **The `in` modifier must be replicated exactly in every implementation.** `public void
  OnTerrainChanged(in TerrainChangeBatch batch)` — dropping `in` does not overload, it fails
  to implement the interface member and the compiler rejects the class.
- **`TerrainRenderer` must remain a `partial class`** like every Godot node script; adding
  this interface does not change that requirement.
- **Assertion messages must not allocate.** `Debug.Assert(cond, $"...")` **eagerly evaluates
  the interpolated string on every call, pass or fail**, in any build where assertions are
  compiled in. Rule 5's in-`Publish` assertion runs on every dispatch, so an interpolated
  message there is a per-publish allocation that would contaminate validation criterion 3's
  zero-allocation measurement. Use a non-allocating form, or confirm the benchmark strips
  `DEBUG`.

**Precedent for declared numeric ordering on an event** *(A3, TD-ADR)*: ADR-0001's
`ModeTransitioned` is already a non-tick, non-bus event whose *"handler order is explicitly
declared at subscription (same phase/priority scheme)"*. Declared numeric priority on an event
subscription is therefore an **already-Accepted pattern in this project**, not a novelty
introduced here — which is the strongest available support for the contract-#3 reconciliation.

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
  10       Pathfinding & Navigation (#8)     Foundation-layer derived state, earliest
  20       Spatial Query / LOS & Cover (#12) Same class of derived state
  30       Job Assignment & Priority (#10)   Gameplay bookkeeping
  40       Repair & Rebuild (#25)            Gameplay bookkeeping
  50       Notifications                     Presentation-facing queueing
  60       Terrain Rendering & Cutaway (#7)  Pure view; last by construction
```

**The ordering is deliberately not load-bearing, and rule 12 is what makes that true.**
Handlers only mark their own state stale and never read another subscriber's derived state
during dispatch, so no handler's result depends on whether an earlier one has run. The gaps
of 10 exist for insertion; the numbers are a *determinism* device, not a dependency
declaration. **If a subscriber ever genuinely requires another to run first, that is a design
smell to escalate, not a priority to tune.**

> *Corrected at TD-ADR 2026-08-25.* The first draft justified two placements as "invalidation
> others may read" and "may consult reachability" — i.e. it asserted a read-after-invalidate
> dependency in the same table where the prose denied one existed. The dependency was real
> (Job Assignment querying reachability Pathfinding had just marked stale), and rule 12 now
> forbids the query rather than the ordering absorbing it.

### Rules

1. **Single publisher, structurally.** `TerrainWorld` receives the bus as `ITerrainChangeSink`
   and can therefore only publish. `Subscribe`/`Unsubscribe` live on the concrete type held by
   the composition root; Terrain physically cannot register a subscriber.
   **`Publish` and `PublishWorldReloaded` are EXPLICIT interface implementations**
   (`void ITerrainChangeSink.Publish(...)`), so they do **not** appear on
   `WorldChangeEventBus`'s own surface. Without that keyword the composition root — which must
   hold the concrete type to call `Subscribe` — would also hold a fully usable publish surface,
   making single-publisher a matter of grant discipline rather than structure. *(A1, TD-ADR
   2026-08-25.)* The harder cap is the payload type itself: a general message bus cannot form
   around a terrain-shaped `ref struct`.
2. **Synchronous, in-order, no queueing, no replay.** `Publish` walks the sorted list and
   returns. There is no buffering, no async, no scheduling, no filtering.
3. **Registration is composition-root-only**, alongside the writer-interface grants
   (ADR-0003) and the RNG draw handles (ADR-0005) — one place to review who listens to what.
   **All subscribers must be registered BEFORE `TerrainWorld` is constructed**, and before any
   load-window publish. ADR-0002's constructor takes the sink, populates the world, and signals
   `WorldReloaded`; a subscriber registered after that misses the signal and begins life with
   empty-but-not-dirty caches — the stale-cache-after-load bug that rule 7 and ADR-0002 rule 6
   both exist to kill, arriving through composition-root ordering rather than through the
   subscriber set. **Late registration is a debug assertion.** *(Added at TD-ADR 2026-08-25;
   the obligation originates in Accepted ADR-0002, so it introduces no Proposed-ADR coupling.)*
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
   registered is a dangling reference. `TerrainRenderer` unsubscribes in `_ExitTree`.
10. **A missed unsubscribe must self-heal, not log forever.** `_ExitTree` can be skipped —
   a crash path, `QueueFree()` racing a `Publish` on the same frame, a leaked reference. The
   bus therefore applies **ADR-0001's tickable-purge precedent**: a subscriber that is a
   `GodotObject` failing `IsInstanceValid()` is **purged with one logged warning**, not
   skipped on every publish forever. Silent absorption is still forbidden; so is an unbounded
   warning stream.
   **The purge is DEFERRED, and this is not optional** — rule 5 forbids mutating the list
   during dispatch, but invalidity is only detectable *during* the walk. So: the walk **marks**
   the dead entry, **skips it for the remainder of that publish**, and emits the warning once
   at detection; **compaction happens after the walk returns**, outside the in-`Publish` flag.
   Removing an entry mid-walk would both trip rule 5's assertion and invalidate the array
   index rule 6 depends on. *(Added 2026-08-25, godot-csharp-specialist; deferral mechanic
   added at TD-ADR after it flagged rules 5 and 10 as contradictory — an implementer following
   both literally would have written the bug on the first attempt.)*
11. **Exception isolation is explicit: one subscriber's throw never starves the rest.** Each
   subscriber call is independently guarded; a throw is logged and dispatch **continues to
   every remaining subscriber in priority order**. Without this rule, a throw at priority 50
   (Notifications) would silently deny the batch to priority 60 (Terrain Rendering) — exactly
   the subscriber-desync bug class this ADR exists to prevent, reintroduced through the error
   path. **Build-dependent severity** (TD-ADR): **rethrow in DEBUG**, isolate-and-log in
   Release. A swallowed throw leaves that subscriber's cache silently stale, which reproduces
   as "flaky" — and loud-in-debug is the house posture everywhere else in this project.
   *(Added 2026-08-25, godot-csharp-specialist — the draft implied this but never said it,
   which is not good enough for a rule an implementer must not get wrong.)*
12. **A handler must not query another subscriber's derived state during dispatch.** Handlers
   copy primitives out of the batch and mark **their own** state stale. Cross-system reads
   (e.g. Job Assignment calling `IsReachable`) happen in `Tick()`, after dispatch completes.
   **This is what makes the "ordering is not load-bearing" claim true by construction** rather
   than by hope: handlers only mark, so there is no read whose answer depends on whether an
   earlier handler has run yet. Without this rule, Job Assignment consulting reachability that
   Pathfinding just marked stale is a genuine read-after-invalidate, and the idempotence rule
   does not cover it — *querying is not writing*. *(Added at TD-ADR 2026-08-25.)*
13. **The bus is single-threaded by design and does not lock.** Registration and `Publish`
   both occur exclusively on the sim thread. This is stated because ADR-0004 introduces a
   background writer thread elsewhere in the architecture, and a reader who knows that will
   reasonably ask. The bus is never touched from it.

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
- **Cons**: **It does not compile**, and **would not compile on .NET 9 either.** `TerrainChangeBatch` is a `ref struct`; C# 12 forbids `ref struct` as a generic type argument, and C# 13's `allows ref struct` is opt-in per declaration — the BCL has not annotated `Action<T>`/`EventHandler<T>`/`IObserver<T>`, and `List<T>` can never be annotated because the CLR forbids arrays of ByRefLike element type.
- **Rejection Reason**: Illegal in the target language version. **Documented rather than omitted**, because it is the approach every implementer will try first — and the natural workaround (copy changes into a `List<TerrainChange>` before dispatch) allocates on every dig and breaks the zero-allocation standard.

### Alternative 2: Bespoke non-generic delegate

- **Description**: `delegate void TerrainChangeHandler(in TerrainChangeBatch batch);` plus a `TerrainChangeHandler[]` invocation list. Legal — delegates may take `ref struct` parameters.
- **Pros**: Lighter than an interface; no type to implement; subscribers can be lambdas or method groups.
- **Cons**: Needs a **second** parallel delegate for `WorldReloaded`, which can be half-registered. Lambda subscribers cannot be unsubscribed reliably (delegate equality on closures is a known trap) — fatal for view-layer subscribers that must detach in `_ExitTree`. Obligations are invisible at the type level.
- **Rejection Reason**: The two-callback lifetime is the deciding factor. One registration must cover both callbacks or a subscriber can silently miss `WorldReloaded` and run on stale caches after every load — precisely the bug ADR-0002 rule 6 exists to prevent.

### Alternative 3: Pull-based `Revision` polling (mirror the entity layer)

- **Description**: Drop push dispatch. Subscribers poll `TerrainWorld.Revision` at their own cadence and rescan, exactly as ADR-0003 does for entity stores.
- **Pros**: One notification idiom project-wide. No registration, no ordering, no lifetime management. Trivially deterministic.
- **Cons**: Two that decide it. **(1) The renderer needs per-chunk dirty granularity** to hold 32 draw calls; `Revision` yields a boolean, so the renderer would rebuild every visible octant on every dig. **(2) Polling discards `TerrainChange.Previous`**, which CD-1's after-action report depends on and which exists nowhere else — ADR-0002's own comment calls it "the only place it survives."
- **Rejection Reason**: **Primarily that this alternative is not available to this ADR.** `ITerrainChangeSink` is fixed by **Accepted** ADR-0002 and by cross-cutting contract #3; choosing polling means reopening an Accepted ADR and the contract annex. It is recorded because a reviewer will ask about the ADR-0003 asymmetry, not because it was ever live. On the merits, the two cons above carry it.
- **Correction (TD-ADR 2026-08-25)**: an earlier draft cited "63.2 µs per dig with 10 cached paths" as evidence against polling and mislabelled it "the *entity* case." It is neither. That figure is **Pathfinding's `Revision`-driven rescan on a real terrain dig**, and the project's own conclusion was *"**Revision polling was not falsified** … 0.38% of a frame"* (ADR-0003 Spike Results; Pathfinding quick-spec §8 item 6; `architecture.md` §9 rates it LOW/Monitored). Citing a favourable polling measurement as an argument against polling was simply wrong, and an adversarial reader would have found it in five minutes.

### Alternative 4: Registration-order dispatch, no priority numbers

- **Description**: Identical to the Decision, minus the integers. `Subscribe(subscriber)` appends; dispatch walks in registration order. The composition root is the single ordering authority.
- **Pros**: Equally deterministic — ADR-0002 rule 8 requires *a* stable order, not numbers. Smaller coordination surface (no number to allocate or collide). Unambiguously inside every reading of the cap, including the systems-index's stricter wording.
- **Cons**: Ordering becomes an invisible property of line order in one file. A reviewer reordering two registration lines for tidiness silently changes dispatch order, with nothing at the call site indicating that order matters — the exact failure ADR-0001 designed its phase/priority scheme against.
- **Rejection Reason**: This is the closest alternative and the one a reviewer would actually propose *(added at TD-ADR 2026-08-25, which noted its absence)*. Rejected on **reviewability**, not capability: numbers make the order visible and intentional at each registration site. With rule 12 in place both options are equally correct; the numbers make the intent auditable.

### Alternative 5: Fixed compile-time subscriber list

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
- **.NET 9 migration invites a rewrite to `event Action<T>`.** *Mitigation*: **the premise is false and the ADR now says so at the top.** `allows ref struct` is opt-in per declaration; the BCL types in Alternative 1 stay illegal, and `List<T>` stays illegal permanently by CLR rule. A migration would therefore require hand-authoring an annotated generic delegate — i.e. custom infrastructure, which is what this ADR already provides — while losing declared ordering and reliable unsubscription. **Do not migrate on version-bump alone.**
- **A subscriber throw starves lower-priority subscribers.** *Mitigation*: rule 11 makes per-subscriber exception isolation an explicit numbered requirement, not an inference.
- **A leaked Godot subscriber logs a warning on every publish forever.** *Mitigation*: rule 10's `IsInstanceValid()` purge — remove once with one warning, matching ADR-0001.
- **Assertion messages silently break the zero-allocation measurement.** *Mitigation*: recorded as an implementation note above and as a condition on validation criterion 3.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| `design/gdd/systems-index.md` #3 World Change Event Bus | "One publisher (Terrain), many subscribers. Capped by ADR: a dumb synchronous dispatcher" | The subscriber contract itself; single-publisher enforced structurally; synchronous walk with no queueing, no replay, no filters. **On ordering**: the index previously paraphrased the cap as *"no priorities … or ordering guarantees"*, which was stricter than cross-cutting contract #3 (*"no priorities beyond declared handler order"*) and stricter than ADR-0002 rule 8, which **requires** a stable order. The index routes #3 as ADR-only and defers to "Capped by ADR", so this ADR is the authority — and **its paraphrase has been amended in the same changeset** rather than left to contradict. Precedent: ADR-0001's `ModeTransitioned` already declares handler order numerically at subscription |
| `design/gdd/terrain-data-model.md` (TR-terrain-018) | Batched change events with previous-state capture (CD-1) | Subscribers receive the full `TerrainChangeBatch` including `Previous`, preserving the after-action report's data source |
| `design/quick-specs/terrain-rendering-cutaway.md` C8 / line 125 | "Change batches mark octants dirty; dirty octants rebuild once per frame" — signature was a `/* batch */` placeholder | Supplies the real signature: `OnTerrainChanged(in TerrainChangeBatch)`, priority 60 |
| `design/quick-specs/pathfinding-navigation.md` C3.3 / C7 | `MarkLayerDirty(z)` on terrain mutation; revalidation semantics | Pathfinding subscribes at priority 10 and marks its layer stale inside the handler |
| `design/gdd/terrain-data-model.md` (**TR-terrain-017**) | Designation invalidation via events **including while paused** | Events are orthogonal to ticks; the bus dispatches identically under both authorities and regardless of pause. *(Citation corrected at TD-ADR 2026-08-25 — the draft cited TR-time-017, which is "PostEncounterReconcile duties".)* |

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
   `TerrainChangeBatch`* (illegal in C# 12 — use `ITerrainChangeSubscriber`); *a bus handler
   querying another subscriber's derived state during dispatch* (rule 12).
3. **`design/gdd/systems-index.md` line 83** — its paraphrase reads *"no priorities, filters,
   replay, **or ordering guarantees**"*, which is **stricter** than cross-cutting contract #3's
   *"no priorities beyond declared handler order"*. The index routes #3 as ADR-only and defers
   to "Capped by ADR", so this ADR is the authority — but it must then update the paraphrase.
   **Leaving two contradicting caps in the repo is worse than either one** *(A4, TD-ADR)*.
4. **`docs/architecture/architecture.md`** — close **QQ-23** in §8 and update its §9 risk row.
5. **`docs/architecture/requirements-traceability.md`** — add ADR-0006 to **TR-terrain-017**
   and **TR-terrain-018**.

## Validation Criteria

1. Six subscribers register at declared priorities and are dispatched in `(priority, registration)` order; the order is identical across runs and across a save/load cycle.
2. A handler attempting to write terrain or a store fails the mutation-window assertion; `Subscribe`/`Unsubscribe` during `Publish` fails the in-Publish assertion.
3. **Zero allocation**: 10,000 publishes to 6 subscribers record 0 B and 0 Gen0 collections. **The benchmark must state its build configuration** — if assertions are compiled in, their messages must be verified non-allocating first (see implementation notes), or the measurement is of the wrong build.
4. Duplicate subscriber registration is rejected; duplicate priorities across different subscribers are rejected **by debug assertion** — so in Release the `(priority, registration sequence)` tiebreak is the operative determinism guarantee, and registration sequence is deterministic *because* the composition root is a fixed sequence. That is what criterion 1's "identical across runs" actually rests on.
5. `PublishWorldReloaded` reaches every registered subscriber, in the same order, and each responds with a full rebuild — verified by the existing no-stale-cache-after-load integration test (ADR-0002 criterion 3).
6. A Godot-side subscriber (`TerrainRenderer`) participates with the core assembly still passing the zero-Godot-references CI grep.
7. An unsubscribed subscriber receives nothing; a subscriber that throws is logged, not silently absorbed — **and every remaining lower-priority subscriber still receives that batch** (rule 11).
8. A Godot subscriber freed without unsubscribing is purged from the list after one logged warning, and subsequent publishes are clean — no repeated warning, no repeated failure (rule 10).
9. **Review trigger, not a test** — owner: `/architecture-review`. Six months in: the bus still has exactly one publisher, no queueing, no replay, and no subscriber's correctness depends on another's priority (rule 12 still holds).

## Related Decisions

- **ADR-0002** Terrain Data Model — owns `TerrainChangeBatch`, `ITerrainChangeSink`, the single-publisher rule, batch lifetime, and rule 8's deterministic event streams
- **ADR-0001** Time Authority — mutation window; the `(phase, priority, registration sequence)` ordering precedent reused here; events-are-orthogonal-to-ticks
- **ADR-0003** Entity Data Ownership — the deliberate asymmetry: entities use `Revision` polling and have **no** event bus; Alternative 3 records why terrain differs
- `docs/architecture/cross-cutting-contracts.md` #3 — the one-page cap this ADR implements
- `docs/architecture/architecture.md` §8 **QQ-23** — the open question this ADR closes; §11 records the LP-FEASIBILITY finding that raised it
