# ADR-0005: Seeded RNG / Determinism

## Status
**Proposed**

## Date
2026-08-07

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 |
| **Domain** | Core (determinism, serialization) |
| **Knowledge Risk** | LOW — the entire generator and stream-derivation path is plain C# unsigned-integer arithmetic inside `Hollowdeep.Core`, zero Godot references. No Godot RNG API (`RandomNumberGenerator`, `randi()`) is used or extended |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md`, `deprecated-apis.md` (grepped for `rng`/`random`/`thread` — no matches; nothing post-cutoff applies to this domain) |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | None engine-specific — correctness is verified by this ADR's own Validation Criteria (headless, deterministic, no engine dependency) |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001 (Accepted) — this ADR's draw-site enforcement implements ADR-0001's forbidden pattern "RNG draws outside authority-driven execution"; reuses the same `MutationWindow` debug-assert primitive established by ADR-0002/0003 rather than inventing a second one |
| **Enables** | ADR-0004's checkpoint content-scope item 3 ("Combat RNG streams... format owned by the Seeded RNG ADR") and GDD AC-67 (deterministic resume vs. an unquit control run); Save/Load quick-spec (#6, jointly blocked with ADR-0004 until both exist); Colonist Entity & Attributes quick-spec (CD-4 appearance seed); Raid Trigger GDD (#18 — raider composition, reload-determinism per CD-GDD-ALIGN M1); Combat set GDDs (#19–#23) |
| **Blocks** | Save/Load quick-spec (#6) — explicitly named in ADR-0004 as jointly gated on this ADR |
| **Ordering Note** | This ADR and ADR-0004 together unblock Save/Load #6. ADR-0004's Validation Criterion 2 (resume reproduces an unquit control run bit-for-bit) requires both this ADR's stream format and ADR-0004's checkpoint mechanism — neither alone is sufficient. |

## Context

### Problem Statement
Combat resolution, raid generation, colonist appearance, and (later, Alpha-tier) map procedural generation all need randomness that is both **deterministic** (same seed + same draw sequence → same result, for bug reproduction and regression locking) and **checkpoint-resumable** (a battle checkpoint or colony save must capture exact RNG state and continue drawing identically after restore). No ADR yet defines the generator algorithm, the per-system stream architecture, or the serialization format — despite three existing ADRs already assuming it exists: ADR-0001's forbidden pattern ("RNG draws outside authority-driven execution"), ADR-0003's CD-4 (`AppearanceSeed` field, per-system streams for raider composition), and ADR-0004's checkpoint content scope (item 3, explicitly reserving this ADR's slot). Systems-index #4 tags this as Foundation-layer, MVP, ADR-only (no full GDD).

### Constraints
- **Explicit, fully serializable generator state** — the propagation session (2026-08-03) narrowed algorithm choice to "PCG/xoshiro class; rules out hidden/platform-dependent internal state." This rules out `System.Random`, whose internal state is not guaranteed stable across .NET versions and is not exposed for serialization.
- **Zero steady-state allocation** in the simulation path (measured standard, technical-preferences.md) — every draw call must be allocation-free.
- **RNG draws occur only inside `Tick()` or authority-driven resolution** (ADR-0001, forbidden-pattern list) — this ADR must give that rule a concrete, debug-assertable enforcement point, not just a policy statement.
- **Per-system seeded streams** (cross-cutting contract #2) — named explicitly as this ADR's deliverable; the stream layout "must round-trip like all other state."
- **Plain C#, zero Godot dependency**, headlessly testable — same posture as `TerrainWorld` and the entity stores.
- **Stream additions must not perturb existing streams** — inserting a new consumer (e.g., a Combat sub-stream added when the Combat GDDs are eventually written) must never change the derived seed of any stream that already exists, or every save made before that point silently desyncs on load.

### Requirements
- A generator whose complete internal state can be captured and restored exactly, at any point after an arbitrary number of prior draws (the "resumable at arbitrary draw counts" requirement — satisfied by exact state capture; no jump-to-offset computation is required for the checkpoint use case).
- A per-system stream architecture keyed by stable, human-readable names — never by registration order or allocation index.
- One root seed (`WorldSeed`) that deterministically reproduces every stream in the game from a single number, for save reproducibility, bug reports, and debug tooling.
- A debug-assertable draw-site enforcement point consistent with ADR-0001's rule.

## Decision

### 1. Algorithm — PCG32 (PCG XSH RR 64/32)

Each stream is a **PCG32** generator: 64 bits of state, a 64-bit odd increment, 32-bit output per draw, advanced by a linear congruential step and output through PCG's xorshift-rotate permutation. Total serialized state per stream is **16 bytes** (`ulong State`, `ulong Increment`).

PCG32 is chosen over xoshiro256\*\* specifically because **PCG's increment constant *is* its native stream mechanism** — the algorithm was designed so that any two odd increments produce statistically independent, non-overlapping sequences from the same seed. This maps directly onto the "per-system seeded streams" requirement with no additional splitting machinery (xoshiro would need its `jump()`/`long_jump()` functions layered on top to get the same property). PCG32's period (2^64) is enormously larger than any realistic per-stream draw count in a single playthrough.

### 2. Stream keying and seeding — root seed + stable-key derivation

```
WorldSeed (ulong, chosen at new-game, persisted once, never regenerated on load)
        │
        ▼
RngStreamKey { RngOwner Owner; string? SubKey }   — stable, append-only, never reused
        │
        ▼
KeyHash = Fnv1a64(Owner.ToString() + ":" + (SubKey ?? ""))      — stable 64-bit hash
        │
        ▼
(seed0, seed1) = SplitMix64(WorldSeed ^ KeyHash)                — two 64-bit outputs
        │
        ▼
PCG32 stream: State = seed0 ; Increment = seed1 | 1              — force odd (PCG requirement)
```

- **One `WorldSeed`** reproduces every stream in the world. It is the single number a bug report or a debug-console `seed` command needs.
- **Streams are keyed by a stable `(RngOwner, SubKey)` pair, never by registration order or an allocation counter.** Adding a new named stream can never change any existing stream's derived seed, because each stream's seed depends only on its own key and the shared `WorldSeed` — not on how many streams exist or what order they were touched in.
- `RngOwner` is a fixed enum, **append-only and never renumbered/reused** — the same convention already established for `EntityId` (ADR-0003) and the TR-ID registry.

### 3. Stream registry — fixed owners now, namespaced extensibility for Combat

| `RngOwner` | Tier | Consumer | Notes |
|---|---|---|---|
| `ColonistIdentity` | MVP | Colonist Entity & Attributes (#9) | Draws exactly one `ulong` per spawn to populate `AppearanceSeed` (ADR-0003, CD-4, frozen field). That seed then independently drives as many procedural-appearance sub-rolls as the view layer wants (hairstyle, palette, etc.) — those view-side rolls are **not** simulation RNG draws, carry no determinism obligation of their own beyond "same `AppearanceSeed` → same visual" (already ADR-0003's rule: "views derive, never store"), and do not touch this stream's draw count. |
| `RaidTrigger` | MVP | Raid Trigger (#18) | Raider composition, threat scaling rolls. |
| `Combat` | MVP | Combat set (#19–#23, not yet designed) | One encounter-wide stream for MVP. **Extensibility rule**: when the Combat GDDs are written, they may register additional named sub-streams via distinct `SubKey` values under `RngOwner.Combat` (e.g. `SubKey: "Targeting"`, `SubKey: "AIDecision"`) **without requiring a new ADR** — each sub-key derives an independent, non-overlapping stream by construction (§2), so splitting Combat's single stream later is purely additive and never perturbs the original. |
| `MapGeneration` | Alpha | World/Mountain Generation (#35) | Reserved now; unused until Alpha. Terrain itself draws no RNG (ADR-0002) — this stream belongs to the procgen *producer*, not `TerrainWorld`. |

New `SubKey`s under an existing owner are that owning system's namespace to manage; new `RngOwner` values require this ADR's registry table to be extended (a documentation change, not a re-architecture).

### 4. Draw-site enforcement

All draws go through `SeededRngRegistry.GetStream(RngStreamKey)`, returning an `IRngStream` (`NextUInt32()`, `NextFloat01()`, `NextRange(min, max)`). Every draw call **debug-asserts the mutation window is open** — reusing the exact `MutationWindow` primitive already established by ADR-0002 (`TerrainWorld`) and ADR-0003 (entity stores), rather than inventing a parallel enforcement mechanism. This gives ADR-0001's "RNG draws outside authority-driven execution" forbidden pattern a concrete, testable teeth in Debug builds.

### 5. Serialization — `Snapshot()` / `Restore()`

`SeededRngRegistry` implements cross-cutting contract #2's `Snapshot()`/`Restore()` pair with a schema version:

- **`Snapshot()`** captures `(RngOwner, SubKey, State, Increment)` for every stream that has been **touched at least once** since world creation — i.e., every stream that has ever drawn. Untouched streams are not serialized; they lazily re-derive from `WorldSeed` + key on first future access, which is safe precisely because a stream that has never drawn has no advanced state to lose.
- **`Restore()`** rehydrates each captured stream's exact `(State, Increment)` — continuing to draw from a restored stream is bit-identical to continuing an unbroken run that was never saved.
- `WorldSeed` itself is serialized once as top-level colony data (same category as the `EntityIdSource` counter, per the save/load spike) — chosen at new-game, never regenerated on load.
- This is the format ADR-0004's checkpoint content-scope item 3 consumes directly: "combat RNG streams... resumable at arbitrary draw counts" is satisfied by capturing whichever `Combat`-owned streams have drawn by the checkpoint beat.

### Key Interfaces

```
struct RngStreamKey { RngOwner Owner; string? SubKey; }   // stable, hashable, append-only Owner values

interface IRngStream {
    uint NextUInt32();
    float NextFloat01();
    int NextRange(int minInclusive, int maxExclusive);
}

class SeededRngRegistry {
    IRngStream GetStream(RngStreamKey key);   // lazy-derives on first access; debug-asserts mutation window on every draw
    RngSnapshot Snapshot();                   // touched streams only
    void Restore(RngSnapshot snapshot);       // exact state rehydration
}
```

## Alternatives Considered

### Alternative 1: xoshiro256\*\*
- **Description**: 256-bit-state generator, `jump()`/`long_jump()` functions used to carve non-overlapping subsequences for per-system streams.
- **Pros**: Larger period (2^256), excellent statistical quality, well-precedented in other game engines.
- **Cons**: No native per-stream concept — stream splitting requires implementing and testing the jump functions as a second piece of machinery on top of the base generator. 32-byte state per stream vs. PCG32's 16.
- **Rejection Reason**: PCG32's increment-based streams solve the exact "per-system seeded streams" requirement with no extra layer, and 2^64 is not a real constraint at any realistic in-game draw count. Worth revisiting only if a future need for cryptographic-strength unpredictability emerges (none is anticipated for a single-player colony sim).

### Alternative 2: .NET `System.Random`
- **Description**: Use the framework-provided generator directly.
- **Pros**: Zero implementation cost, already available.
- **Cons**: Microsoft does not guarantee the algorithm is stable across .NET versions; internal state is not exposed for serialization.
- **Rejection Reason**: Directly violates the "explicit-serializable-state" constraint from the 2026-08-03 propagation session — this generator cannot satisfy checkpoint resumability or save/load determinism at all, regardless of implementation effort.

### Alternative 3: Sequential stream indices assigned by registration order
- **Description**: Key streams by an incrementing integer assigned in the order each consumer first requests one, instead of a stable name.
- **Pros**: Marginally simpler — no hashing step.
- **Cons**: Inserting or removing a consumer anywhere in the registration sequence silently reseeds every stream that follows it. This is a non-local, silent desync bug class: a save made before the change would produce different combat/raid outcomes after a patch that merely added an unrelated new RNG consumer earlier in the list.
- **Rejection Reason**: Stable-key derivation (§2) makes every stream's seed depend only on its own name and the shared `WorldSeed`, never on sibling registration order — closing off this bug class by construction.

## Consequences

### Positive
- Closes the gap that has explicitly blocked Save/Load #6 and ADR-0004's AC-67 since the 2026-08-03 propagation.
- PCG's native stream mechanism means the not-yet-designed Combat set can add sub-streams later without touching this ADR's derivation scheme or any existing save.
- 16 bytes/stream is negligible against the checkpoint's buffer budget (ADR-0004's ~4 MB pooled buffers) — RNG state is not a meaningful contributor to checkpoint size or write time.
- One `WorldSeed` reproduces the entire world deterministically, useful for bug reports and future regression-lock tests (mirrors the mode-switch and save/load spikes' same-seed determinism tests).

### Negative
- PCG32's period (2^64) is smaller than xoshiro256\*\*'s (2^256) — a recorded trade-off, not a practical constraint at game-length draw counts.
- Every future feature that wants randomness must go through the registry facade instead of ad-hoc `System.Random` — a small discipline tax, consistent with the "God-object firewall table" style discipline already established for Terrain and entity stores.
- The mutation-window debug-assert only fires in Debug builds — a Release build does not runtime-enforce the draws-only-inside-`Tick()` rule. This is the same posture ADR-0001's other forbidden patterns already accept, not a new gap introduced here.

### Risks
- **`KeyHash` collision** between two distinct `RngStreamKey`s producing an identical derived increment — bounded: 64-bit hash space against a handful of named streams is astronomically unlikely (birthday bound); a startup assertion can verify all currently-registered streams have distinct increments.
- **Accidental key reuse** — a future consumer reusing an existing `(Owner, SubKey)` pair for a semantically different purpose would silently share a stream and desync draw order between two unrelated features. Mitigated the same way `EntityId`/TR-ID append-only registries are: this ADR's table (§3) is the source of truth for assigned keys, extended by documentation, not overwritten.
- **PCG32's statistical quality is weaker under adversarial scrutiny than a CSPRNG** — accepted: this is a single-player colony sim with no PvP wagering or anti-cheat-sensitive RNG surface. If that ever changes, this ADR's algorithm choice would need revisiting; explicitly out of scope for MVP.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| time-authority-mode-switch.md | Rule 9b / AC-67 — deterministic resume vs. an unquit control run, combat RNG stream restoration | §5 `Snapshot()`/`Restore()` gives ADR-0004's checkpoint the exact stream state it needs; §1's explicit-state generator makes bit-identical continuation possible |
| time-authority-mode-switch.md | AC-66/AC-68 (via ADR-0004) | This ADR supplies the stream *format* ADR-0004's checkpoint content-scope item 3 reserves; ADR-0004 owns cadence and writer provenance |
| entity-data-ownership.md (ADR-0003) | CD-4 — persistent name + deterministic appearance seed | §3 `ColonistIdentity` stream draws exactly one `ulong` per spawn into the frozen `AppearanceSeed` field; view-layer appearance rolls are explicitly decoupled from simulation draw count |
| systems-index.md #4 | Seeded RNG / Determinism — per-system seeded streams; without it, sim bugs are irreproducible and saves desync | This entire ADR |
| cross-cutting-contracts.md | Contract #2 — per-system seeded RNG streams named as this ADR's deliverable, must round-trip like all other state | §5 satisfies the round-trip obligation identically to `TerrainWorld`/entity-store `Snapshot()`/`Restore()` |

## Performance Implications
- **CPU**: a PCG32 draw is a handful of 64-bit multiply/xor/rotate operations (single-digit nanoseconds), zero allocation — negligible against the 16.6 ms frame budget and dwarfed by every other measured cost in this project (mode-switch dispatch 0.578 µs, terrain sweep 0.29 ms)
- **Memory**: 16 bytes/stream × ~4 MVP-tier streams ≈ 64 bytes total; `SplitMix64` derivation is transient, no allocation
- **Load Time**: negligible — a handful of streams deserialize in microseconds
- **Network**: n/a (no netcode, per technical-preferences.md)

## Migration Plan
Nothing ships yet — no RNG draw call sites exist anywhere in the current codebase (confirmed by grep; the project is still Foundation-layer). This ADR lands before its first real consumer (Colonist Entity, Raid Trigger, or Combat, whichever is designed first). Save/Load #6 implements the composition-root wiring for `SeededRngRegistry.Snapshot()`/`Restore()`; ADR-0004's checkpoint writer consumes this ADR's stream format directly via content-scope item 3.

## Validation Criteria
1. Deterministic derivation: the same `(WorldSeed, RngStreamKey)` always derives the identical `(State, Increment)` across separate process runs.
2. Draw-sequence determinism: the same seed + identical call sequence produces a bit-identical output sequence (regression-lockable, same pattern as the mode-switch/save-load spikes' same-seed tests).
3. Snapshot/Restore round-trip: capture mid-sequence state, restore into a fresh registry instance, continue drawing — output is bit-identical to an unbroken continuous run. This is the same test ADR-0004's Validation Criterion 2 and GDD AC-67 require.
4. Stream independence: registering a new named stream (simulating a future Combat sub-stream) does not change the derived `(State, Increment)` of any existing stream.
5. Debug-assert fires when a draw is attempted outside the mutation window; does not fire inside it.
6. Zero-allocation: N draws across all registered streams produce 0 bytes allocated and 0 Gen0 collections (same measured-standard pattern as the terrain and mode-switch spikes).
7. Startup collision assertion: all currently-registered streams have pairwise-distinct increments.

## Related Decisions
- ADR-0001 (Accepted) — the forbidden-pattern rule this ADR's draw-site enforcement implements
- ADR-0003 (Accepted) — CD-4 appearance seed, per-system streams for raider composition
- ADR-0004 (Proposed) — checkpoint content-scope item 3 reserves this ADR's slot; both share the target-hardware promotion gate for the frame-rate criterion (separately — this ADR's own promotion is not blocked on that measurement, since RNG draws are not part of the terrain/checkpoint frame-time profile)
- `docs/architecture/change-impact-2026-08-03-time-authority-mode-switch.md` — the propagation session that made this ADR blocking for Save/Load #6
- Cross-cutting contract #2 (serialization) — per-system seeded RNG streams named as this ADR's deliverable
