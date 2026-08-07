# ADR-0005: Seeded RNG

## Status
**Accepted** (2026-08-07, user decision)

*Gates: godot-csharp-specialist — no blocking issues (11 minor notes folded in). TD-ADR — CONCERNS → all five findings applied, including the blocking seed-collision fix (domain separation) and the segregated per-stream draw surface. Promoted with the registry update the same day.*

## Date
2026-08-07

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 |
| **Domain** | Core (determinism, serialization) |
| **Knowledge Risk** | LOW — the whole generator is plain C# in `Hollowdeep.Core` with zero Godot references. It uses only .NET 8 integer math. No post-cutoff API is load-bearing. |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`; ADR-0001, ADR-0003, ADR-0004; `cross-cutting-contracts.md` |
| **Post-Cutoff APIs Used** | None. The draw path uses `ulong`/`long` integer operations only — not Godot `RandomNumberGenerator`, not `System.Random`. |
| **Verification Required** | xoshiro256** and SplitMix64 output must match the published reference vectors, plus dedicated vectors for the pinned stream-seed and entity-value derivations. Domain separation must hold (no stream seed equals any entity value). Integer-only sim draws must produce identical results on x64 and ARM. Generator arithmetic must run `unchecked`. The snapshot round-trip must be byte-stable and little-endian. The draw path must assert the mutation window. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001 (Accepted) — the draws-only-inside-`Tick()` rule; `TimeContext`; the mutation-window assertion. ADR-0003 (Accepted) — `EntityId` for per-entity seed derivation. ADR-0004 (Accepted) — the `SnapshotInto` caller-buffer obligation and content item 3 (combat RNG streams). |
| **Enables** | Save/Load quick-spec (#6) — this ADR unblocks it. GDD AC-67's deterministic-resume test (the RNG half). |
| **Blocks** | Save/Load quick-spec (#6) must not be specced before this ADR exists. The Combat GDD set (#19–#23) references the draw-site rule defined here. |
| **Ordering Note** | This is the last open foundation-ADR obligation before Save/Load #6. The non-RNG half of ADR-0004 is implementable without it; AC-67's bit-for-bit resume test needs both. |

## Context

### Problem Statement
The simulation must be deterministic: the same seed and the same inputs must give the same result. ADR-0001 fixed the draw-site rule but not the generator. ADR-0004 needs to serialize combat RNG streams, resumable at arbitrary draw counts. Save/Load #6 cannot be specced until the stream format exists. This ADR fixes the algorithm, the stream model, and the serialization contract.

### Constraints
- **Draw-site rule (ADR-0001, locked)**: random draws occur only inside `Tick()` or authority-driven resolution. Never in `_Process`, UI callbacks, or bus handlers.
- **Zero Godot dependency (ADR-0001/0002/0003, locked)**: the generator lives in `Hollowdeep.Core`. The CI grep gate forbids a Godot reference there.
- **Resumable at arbitrary draw counts (ADR-0004, locked)**: the generator state must serialize in full. The algorithm must keep no hidden or platform-dependent internal state.
- **Round-trip like all other state (cross-cutting contract #2)**: `Snapshot()`/`Restore()` with a schema version; byte-stable; same-seed re-runs identical.
- **Zero steady-state allocation (technical-preferences, enforced)**: a draw must allocate nothing.
- **Deterministic iteration (house pattern)**: stream order is fixed, never hash-order.

### Requirements
- The generator must produce identical output on every platform for gameplay draws.
- Each system must draw from an independent stream, so a new draw in one system does not shift another system's results.
- The full RNG state must serialize into both a colony save and a battle checkpoint.
- A restore must reproduce the exact draw sequence that an unquit run produced.

## Decision

Adopt **xoshiro256\*\*** as the generator for every long-lived stream, and **SplitMix64** as the seed mixer only. Own all streams in one plain-C# `RngService`. Key streams by a fixed `RngStreamId` enum. Derive one-shot per-entity values as a pure function of the master seed and the `EntityId`.

### The generator
- **Algorithm**: xoshiro256\*\*. State is four `ulong` words (`S0..S3`), 32 bytes. The state is the position, so a resume at any draw count needs no separate counter.
- **Seed mixer**: SplitMix64. It fills the four state words from a 64-bit seed. It is never the main generator — its statistical quality under heavy draw volume is not vetted to the standard that xoshiro256\*\* is.
- **Non-zero guard**: the all-zero xoshiro state is illegal. The seeding path guarantees a non-zero state and asserts it.

### The stream model
- **World seed**: one 64-bit master seed per colony. The colony creation step chooses it. The default is a random value; the player can supply a value for bug reproduction. The seed is immutable for the colony's life. The save header stores it.
- **`RngService`** owns a fixed array of generators, one per `RngStreamId`. It seeds each stream from the master seed and a **stream domain tag** (see Seeding formula). Central ownership gives one snapshot with one schema version.
- **`RngStreamId`** is a compile-time enum. The MVP members: `Spawn`, `Combat`, `RaiderAI`, `MapGen` (reserved, Alpha). New members append; they never renumber.
- **Domain separation (rule)**: stream seeds and per-entity seeds draw from different domain tags, so a stream seed can never equal an entity seed. Every derivation site must pass a domain tag.
- **Per-entity values** (for example the CD-4 appearance seed) use `DeriveEntitySeed(entityId, purpose)` — a pure function of the master seed, the `entityId`, and a `purpose` tag. It draws from the entity domain, not the stream domain. It is pure and its inputs already serialize, so it needs no serialized generator. The `purpose` tag lets one entity hold more than one independent value.
- **The real rule is "recomputable from serialized inputs", not "one-shot vs sequence"**: a value may skip a serialized generator only if it is a pure function of already-serialized inputs (master + entityId + purpose). If a system needs a running sequence whose position must persist, it must use a registry stream.
- **Appearance-seed storage**: the colonist appearance seed is recomputed at view-bind from `DeriveEntitySeed(entityId, Appearance)`. It is not a new `ColonistStore` field, so it adds no store schema.

### Seeding formula (pinned)
The implementation must match these exactly. Each has a reference test vector.
- **SplitMix64 (canonical mixer)**: `z = (state += 0x9E3779B97F4A7C15); z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9; z = (z ^ (z >> 27)) * 0x94D049BB133111EB; return z ^ (z >> 31);`
- **Domain tags**: `DOMAIN_STREAM = 0x5F3759DF5F3759DFUL`, `DOMAIN_ENTITY = 0xD1B54A32D192ED03UL` — two distinct 64-bit constants.
- **Stream seed**: set a SplitMix64 state to `master ^ DOMAIN_STREAM ^ (ulong)id`. Step it four times to fill `S0..S3`. If all four words are zero, set `S0 = 1` (the non-zero guard).
- **Entity value**: `DeriveEntitySeed(entityId, purpose)` returns one SplitMix64 mix of `master ^ DOMAIN_ENTITY ^ (ulong)entityId ^ PurposeTag(purpose)`.
- **xoshiro256\*\* constants (pinned)**: rotations 7, 9, 11, 45, and the `S1 << 17` step. Do not refactor these into a generic `rotl(x, k)` — a `k` of 0 or 64 hits C#'s shift-count mask and breaks the generator.

### Implementation notes
- The generator array is a raw `Xoshiro256SS[]` indexed by `(int)id` with `ref` access. It is never a `List<T>` (its indexer copies the struct by value) or a `Dictionary<enum,_>` (enum-key boxing).
- All generator arithmetic runs `unchecked`. `Hollowdeep.Core.csproj` must not set `CheckForOverflowUnderflow`. Overflow checking would throw on the first draw.
- `SnapshotInto`/`Restore` read and write each state word with `BinaryPrimitives` little-endian, not a raw span reinterpret. The save format is endian-explicit by construction.
- `NextInt` uses `Math.BigMul` for the 64×64→128 multiply that the unbiased bounded method needs.
- A static pure `DeriveEntitySeed(master, entityId, purpose)` exists for isolated unit tests. The instance method, which reads the stored master, is the primary call path.

### The draw API
- **Segregated draw surface (house pattern)**: `RngService` owns state and serialization, but it hands out no draw-any-stream method. The composition root grants each system a narrow per-stream handle (for example `ISpawnRng`, `ICombatRng`). A system can draw only its granted stream. This mirrors ADR-0003's per-(system × field group) writer interfaces. It stops one system from shifting another system's determinism.
- **Authority assertion**: a `Combat`-stream draw asserts `Mode == TurnBased`, the same split as ADR-0003's health writer-per-authority rule.
- Integer draws only in the sim path: `NextULong()`, and `NextInt(minInclusive, maxExclusive)` with an unbiased bounded method (Lemire, via `Math.BigMul`). Modulo bias is forbidden.
- **No floating-point draw in the sim path.** Floats are not portable across platforms and would break determinism. A float helper, if the team ever needs one, lives in the presentation assembly — outside `Hollowdeep.Core`. "Zero float draw in core" is then a structural invariant that a grep gate enforces, not a naming guess.
- Every draw asserts the mutation window and the draw-site rule, the same house pattern as the entity stores. World generation (`MapGen`, Alpha) runs before the tick loop. It draws inside a sanctioned **world-gen window**, the same exemption model as ADR-0004's load window. This ADR reserves that window now.

### Serialization
- `RngService` serializes centrally as one self-describing block: a schema version, the master seed, a stream count, then each stream's four state words in `RngStreamId` order. `Restore` reads the version and the count from the block, so it detects an older, shorter buffer by content, not by a fixed length.
- It follows ADR-0004's caller-buffer obligation: `SnapshotInto(buffer)` writes the fixed ~200-byte block into the aggregate checkpoint buffer that ADR-0004 §3 owns. The colony save uses the same writer. The footprint is tiny, so no separate allocating `Snapshot()` path exists.
- **The `Combat` stream is not combat-transient side-table state.** It is a persistent stream owned by `RngService`. It rides both colony saves and checkpoints, so carry-forward survives a between-battle colony save. Only initiative, AP, and target-lock side tables are checkpoint-only (ADR-0003/0004). Do not move the `Combat` stream into a side table.
- Combat streams carry forward across a battle boundary, like the `EntityId` counter. No per-battle reseed happens by default. A reseed is a design knob, not a default.
- On load of an older save that lacks a newer stream, `Restore` seeds that stream fresh from the master seed and the stream domain tag. This is deterministic. The Migration Plan records it.

### Architecture Diagram
```
master seed (64-bit, in save header)
      │
      │  SplitMix64(master, id)                 SplitMix64(master, entityId)
      ▼                                                   ▼
┌─────────────────────────────┐              one-shot per-entity value
│ RngService                  │              (appearance seed, CD-4)
│  Xoshiro256SS[] by StreamId │              pure function — not serialized
│   ├ Spawn                   │
│   ├ Combat   ── TurnBased resolution draws here
│   ├ RaiderAI                │
│   └ MapGen (reserved)       │
│  SnapshotInto / Restore     │  ── master + version + state words, StreamId order
└─────────────────────────────┘
      ▲
      │ draws only inside Tick()/authority-driven resolution (asserted)
```

### Key Interfaces
```csharp
public enum RngStreamId { Spawn = 0, Combat = 1, RaiderAI = 2, MapGen = 3 }
public enum RngPurpose  { Appearance = 0 /* append; never renumber */ }

// Narrow per-stream draw handle, granted at the composition root.
public interface IRngStream
{
    ulong NextULong();                              // advances this stream
    int   NextInt(int minInclusive, int maxExclusive); // unbiased (Lemire + BigMul)
}

public sealed class RngService
{
    IRngStream GetStream(RngStreamId id);           // composition-root grant only
    ulong DeriveEntitySeed(long entityId, RngPurpose purpose); // pure; reads stored master

    void  SnapshotInto(Span<byte> buffer);          // ADR-0004 caller-buffer contract
    void  Restore(ReadOnlySpan<byte> buffer);       // load / world-gen window only
    int   SnapshotByteLength { get; }               // current-schema length; Restore reads the embedded version + count
}
```

## Alternatives Considered

### Alternative 1: .NET `System.Random`
- **Description**: Use the framework generator.
- **Pros**: No code to write.
- **Cons**: The algorithm is not guaranteed stable across .NET versions (it changed in .NET Core). It is not seed-portable. Its internal state does not serialize.
- **Rejection Reason**: It fails the resumability and cross-version determinism requirements outright.

### Alternative 2: Godot `RandomNumberGenerator`
- **Description**: Use the engine generator. It wraps a PCG32-family generator (`RandomPCG`), not xoshiro.
- **Pros**: Built in.
- **Cons**: It couples the core to Godot, which the zero-Godot rule forbids. Its state exposure and portability across the C# binding are not guaranteed.
- **Rejection Reason**: The zero-Godot core rule (ADR-0001/0002/0003) bans it. Re-implementing xoshiro costs little.

### Alternative 3: One single global stream
- **Description**: One generator for the whole game.
- **Pros**: Simplest to serialize.
- **Cons**: The draw order couples unrelated systems. A new combat draw shifts every later spawn and AI result. This makes the game fragile under any change.
- **Rejection Reason**: It breaks determinism under normal development. Independent streams are cheap.

### Alternative 4: PCG64
- **Description**: A 128-bit generator with a per-stream sequence constant.
- **Pros**: Excellent quality; easy independent streams.
- **Cons**: A little more code than xoshiro; no strong reason to prefer it here.
- **Rejection Reason**: Not chosen, but acceptable. It is the fallback if xoshiro shows a problem. Both meet every requirement.

### Alternative 5: A separate draw counter
- **Description**: Store a draw count next to each stream.
- **Pros**: Explicit.
- **Cons**: Redundant. The xoshiro state already is the position.
- **Rejection Reason**: Unnecessary. Full-state serialization gives arbitrary-draw-count resume for free.

## Consequences

### Positive
- The simulation is deterministic. Saves, replays, and bug reproduction all work.
- The RNG save footprint is tiny: 8 bytes for the master seed plus 32 bytes per stream.
- A draw allocates nothing.
- Independent streams decouple systems. A change in one system does not shift another.
- Integer-only sim draws give the same result on every platform.

### Negative
- The team must hand-implement the generator and its reference-vector tests. This is small, well-known code.
- Mutable generator state needs discipline. The generators live only inside `RngService`; no generator struct is handed out by value.
- The sim path cannot use floats for gameplay draws. It must use the integer API. Any float helper lives outside core.
- `RngStreamId` is a compile-time coupling point. A new stream is a schema addition.
- Every derivation site must pass a domain tag. This is a small discipline, backed by the domain-separation rule and by a helper that requires the tag.
- The composition root must grant per-stream handles. This adds wiring, the same tax as ADR-0003's writer interfaces.

### Risks
- **A float draw leaks into the sim path** → non-determinism. *Mitigation*: the sim-facing RNG surface has no float method; a grep gate finds any float draw in the core.
- **A draw runs outside the mutation window** → `TickSequence` guarantees nothing. *Mitigation*: every draw asserts the window, the same as the stores.
- **An all-zero xoshiro state** → the generator produces only zero. *Mitigation*: the seeding path guarantees a non-zero state and asserts it.
- **A generator struct is copied by value** → two call sites diverge or repeat. *Mitigation*: `RngService` owns the array; it never returns a generator by value.
- **A new derivation site omits its domain tag** → a stream seed and an entity seed collide. *Mitigation*: the domain-separation rule; the derivation helper requires a domain tag argument.
- **A system draws a stream it does not own** → it silently shifts another system's determinism. *Mitigation*: the segregated per-stream handles; no draw-any-stream method exists.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| time-authority-mode-switch.md | AC-67 — a resume reproduces an unquit control run bit-for-bit | Full-state serialization of every stream; the restore continues the exact draw sequence |
| time-authority-mode-switch.md | Determinism criterion — same seed + same inputs → same state | One master seed; deterministic seeding; integer-only draws |
| Combat GDD set (#19–#23) | Hit, damage, and crit rolls in combat | The `Combat` stream, drawn only inside TurnBased resolution |
| Colonist Entity quick-spec / CD-4 | Deterministic appearance seed per colonist | `DeriveEntitySeed(master, entityId)` — a pure one-shot value, not serialized |
| Raid Trigger / Raider Decision-Making | Raider composition and AI choices | The `Spawn` and `RaiderAI` streams |
| Save/Load quick-spec (#6) | RNG streams round-trip with all other state | Central `SnapshotInto`/`Restore`; this ADR is blocking for #6 |

## Performance Implications
- **CPU**: A xoshiro256\*\* draw is a few integer operations. It is far below any frame concern.
- **Memory**: 8 bytes (master) + 32 bytes per stream. The MVP set is under 200 bytes.
- **Load Time**: Restore reads a fixed, small block. No measurable impact.
- **Network**: Not applicable (single-player).

## Migration Plan
No RNG code exists yet, so nothing migrates. The save/load spike used no RNG; Save/Load #6 implements this ADR. When a later version adds an `RngStreamId` member, `Restore` seeds the missing stream from the master seed and the stream domain tag. The self-describing block (schema version + stream count up front) makes an older, shorter buffer detectable by content. The `MapGen` stream and its world-gen window are reserved now; the Alpha world-gen story implements the window rather than bypassing the draw-site assertion.

## Validation Criteria
1. xoshiro256\*\* and SplitMix64 output match the published reference vectors.
2. The pinned derivations match dedicated reference vectors: the stream seed (`master ^ DOMAIN_STREAM ^ id`, stepped four times) and the entity value (`DeriveEntitySeed`). Canonical SplitMix64 alone does not cover these.
3. Domain separation holds: no stream seed equals any entity value across the MVP `id` and `entityId` ranges.
4. The same master seed and the same tick/input sequence give an identical draw sequence and identical world state.
5. A serialize → restore → continue produces the identical next draws. The snapshot is byte-stable.
6. With ADR-0004: a resume from a checkpoint reproduces an unquit control run bit-for-bit (AC-67).
7. Integer-only sim draws give identical results on x64 and ARM.
8. `NextInt(min, max)` is unbiased (a distribution test) and deterministic.
9. A draw allocates zero bytes and causes zero Gen0 collections across a large draw count.
10. A draw does not throw with the project build settings (the `unchecked` requirement holds; overflow checking is off in core).
11. `Restore` reads a shorter, older-schema buffer and seeds the missing streams from the master seed. The snapshot bytes are little-endian.

## Related Decisions
- ADR-0001 Time Authority — the draws-only-inside-`Tick()` rule; determinism criterion.
- ADR-0003 Entity Data Ownership — `EntityId` for per-entity derivation.
- ADR-0004 Battle Checkpoint Architecture — content item 3; the `SnapshotInto` caller-buffer contract.
- cross-cutting-contracts.md — contract #2 (serialization) and the per-system-stream rule.
- Save/Load quick-spec (#6) — unblocked by this ADR.
