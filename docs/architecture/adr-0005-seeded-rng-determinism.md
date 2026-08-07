# ADR-0005: Seeded RNG / Determinism

## Status
**Proposed**

## Date
2026-08-07

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 |
| **Domain** | Core (RNG, determinism, serialization) |
| **Knowledge Risk** | **LOW for this ADR** — the generator lives in `Hollowdeep.Core` with zero Godot references, using only .NET 8 integer primitives; no post-cutoff Godot API is load-bearing. (The pinned engine is HIGH-risk overall — that risk does not reach this decision.) |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md`, `deprecated-apis.md` |
| **Post-Cutoff APIs Used** | **None.** Deliberately **not** Godot `RandomNumberGenerator` and **not** .NET `System.Random` (see Alternative 1). Pure `ulong` arithmetic — behaviour is fixed by the C# language spec, identical across platforms and runtimes. |
| **Verification Required** | (1) Cross-platform golden vectors: a fixed seed produces the checked-in reference draw sequence on Windows/Linux/macOS exports. (2) `Snapshot()`→`Restore()` byte-identical at arbitrary draw counts. (3) Resume-from-checkpoint reproduces an unquit control run (AC-67 technical half — shared with ADR-0004). |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001 (Accepted) — random draws occur only inside `Tick()`/authority-driven resolution; `TickSequence` is the determinism anchor. This ADR does not relax that rule; it defines the streams the rule governs. |
| **Enables** | Save/Load quick-spec (#6); ADR-0004's AC-67 determinism (the RNG half of the resume test); World / Mountain Generation (#35, Alpha) |
| **Blocks** | Save/Load quick-spec (#6) — it must not be specced or implemented before this ADR is Accepted (change-impact 2026-08-03) |
| **Ordering Note** | Independent of ADR-0002/0003 promotion. ADR-0004's non-RNG scope is implementable before this lands; AC-67's deterministic-resume test needs **both** this ADR and ADR-0004. |

## Context

### Problem Statement
Every determinism guarantee the project has already committed to — "same seed + same inputs → same state" (ADR-0001), byte-stable save/load round-trips (cross-cutting contract #2), mid-battle checkpoint resume (ADR-0004), and reproducible bug reports — rests on a random-number generator the project fully controls. ADR-0001 pins **where** draws may happen; nothing yet pins **what** generator is used, **how** per-system streams are seeded, or **how** their state is serialized. ADR-0004 reserves a checkpoint content slot for "combat RNG streams, resumable at arbitrary draw counts" and explicitly delegates the format to this ADR. Save/Load #6 is blocked until it exists.

### Constraints
- **Draws occur only in sanctioned deterministic contexts**: `Tick()`/authority-driven resolution (ADR-0001) **and** the bounded world-creation/load bootstrap window — the same deterministic window ADR-0002 grants Map Authoring and ADR-0004 grants restore — never `_Process`, UI callbacks, or event handlers. `MapGen` and the initial `ColonistAppearance` draws happen in that bootstrap window; **`Restore` itself performs no draws** — loading a saved world re-installs serialized stream state, it never re-runs `MapGen`. This ADR pins the draw contexts and does not weaken ADR-0001's forbidden-context list.
- **Resumable at arbitrary draw counts** (ADR-0004 item 3) → the generator must have **explicit, fully-serializable state** with no hidden or platform-dependent internal state. This rules out `System.Random`, Godot `RandomNumberGenerator`, and any generator carrying floating-point state.
- **Per-system seeded streams**, round-tripping via `Snapshot()`/`Restore()` with a schema version (cross-cutting contract #2).
- **Zero steady-state allocation** in the simulation path (technical-preferences) — a draw must allocate nothing; stream state is a value type.
- **Plain C# core, headlessly testable, zero Godot references.**
- **The firewall is unidirectional** (ADR-0003/0004): encounter RNG streams are serialized only into the battle checkpoint, **never** into colony saves. Colony streams, by contrast, appear in **both** colony saves and the checkpoint — because the checkpoint is a *full self-contained save* (ADR-0004 §1), not a combat-only delta.
- **Cross-platform determinism**: integer-only arithmetic; fixed-width, little-endian, culture-invariant serialization.

### Requirements
- **Deterministic**: same master seed + identical authority-driven input sequence → byte-identical state and identical draw sequences, across platforms and repeated runs.
- **Independent streams**: adding a draw site to one system never shifts another system's sequence.
- **Resumable**: a stream restores to its exact position from serialized state, at any draw count, in O(1), with no replay.
- **Two save scopes**: Colony streams in colony saves; Encounter streams in the battle checkpoint only.
- Provide the RNG that ADR-0004 item 3 and GDD AC-67 depend on.

## Decision

The project adopts a single, project-owned, **PCG-family generator** in `Hollowdeep.Core`, organised as **named per-system streams** derived from one master world seed, whose **full state is serialized** (never seed-plus-count), and **partitioned into Colony and Encounter scopes** for the two save types.

### 1. Algorithm — PCG (pinned default)

Pin the **PCG** family: **pcg32** (32-bit output; 64-bit `state` + 64-bit odd `inc`) for the common case, with **pcg64** available where 64-bit draws are needed. The generator's entire state is the pair `(ulong state, ulong inc)` — a 16-byte POD with no hidden fields. A draw performs the LCG step on `state` and returns the PCG output permutation of the *previous* state; all arithmetic is `ulong`, so the sequence is fixed by the C# language spec and identical on every platform and runtime.

Rationale: fully-serializable POD state (the resumability requirement is satisfied by construction), excellent statistical quality (well beyond a colony sim's needs), tiny, integer-only, and cheap independent streams via the `inc` constant. Implementation shape (mutable struct advanced by `ref`, vs. a small handle class) is an implementation detail for Save/Load #6 — the *state* is the two `ulong`s either way.

**Bounded draws use Lemire's unbiased bounded-integer method** (pinned), so range draws introduce neither modulo bias nor platform variance. Any floating-point value is *derived deterministically from an integer draw at the call site* — floating-point never enters the generator's stored state.

### 2. Stream identity — named registry, constant-derived

A central **`RngStreamId` registry** (enumerated, in `Hollowdeep.Core`) names every stream — e.g. `Needs`, `RaiderComposition`, `RaiderAI`, `CombatResolution`, `ColonistAppearance`, `MapGen`, `AmbientColony`. Each stream is seeded deterministically from the master seed:

```
seed64  = SplitMix64(masterSeed XOR StreamConstant(id))
state   = SplitMix64(seed64)
inc     = SplitMix64(seed64 + 0x9E3779B97F4A7C15) | 1     // forced odd
```

where `StreamConstant(id)` is a fixed per-id 64-bit value (a checked-in constant, not a runtime string hash, so ids can be renamed without shifting seeds). This yields provably independent streams with no manual coordination: **adding a new stream id never perturbs any existing stream.** The **master seed is a single `ulong`** chosen at world creation and stored in the colony-save header.

**Anti-duplication rule** (mirrors the `game_time_throughput` discipline): a system draws **only** from its registered stream(s) via an injected handle granted at the composition root. No system creates an ad-hoc generator, and no system reads another system's stream. One stream per `(system × purpose)`; new draw sites append to that system's own usage and never touch another stream.

### 3. Serialization — full state, never seed+count

`Snapshot` writes, per active stream, a fixed record `{ streamId, state, inc }`; `Restore` reinstalls that state directly. **There is no replay.** Records are fixed-width, little-endian, culture-invariant, and schema-versioned (every Godot 4.7 export target is little-endian, so this holds across the full platform matrix); the block round-trips under cross-cutting contract #2's blocking CI gate (byte-stable state, identical same-seed re-runs). Because every draw goes through a registered stream, there is no transient/derived generator state to reconstruct — restore is O(number of streams), not O(draws).

### 4. Save scope — per-stream Colony vs Encounter tag

Every `RngStreamId` carries a **scope tag**, and the firewall between the two save types is **unidirectional**:

- **Colony-scoped** streams (`Needs`, `MapGen`, `ColonistAppearance`, `RaiderComposition`, `AmbientColony`) serialize into colony saves **and** into the battle checkpoint, and persist for the world's life. They appear in the checkpoint because ADR-0004 §1 pins it as a *full self-contained save* — "same schema family as a colony save, plus the combat scope," valid on its own with no reference to any other file. During combat the colony is fully paused (ADR-0001), so a checkpoint's colony streams are simply the switch-in state carried forward unchanged. `RaiderComposition` is Colony-scoped so the composition stream's position **survives across the mode switch** and raid makeup stays reproducible (the exact reload behavior of an in-progress pre-switch raid is Raid Trigger's / ADR-0003's call, not this ADR's). **Rule of thumb: streams drawn under RealTime or in the world-creation/load window are Colony-scoped.**
- **Encounter-scoped** streams (`CombatResolution`, `RaiderAI`, any TurnBased-only draw) are **combat-transient**: created at battle start, seeded deterministically from `(masterSeed, EncounterId)`, serialized **only into the battle checkpoint** by ADR-0004's writer, and discarded at `PostEncounterReconcile`. They **never** appear in a colony save (ADR-0003/0004 firewall). **Rule of thumb: streams drawn only inside TurnBased resolution are Encounter-scoped.**

So the checkpoint's RNG payload is `SnapshotColony` (colony streams + `masterSeed`) **plus** `SnapshotEncounter` (encounter streams); a colony save is `SnapshotColony` only. This is what lets a restored battle reproduce an unquit control run **through** `PostEncounterReconcile` and into RealTime — the first post-battle colony draw (Needs, ambient) resumes from the same colony-stream position, not a stale or re-seeded one.

**Scope/authority invariant**: a stream is drawn only under the authority (or bootstrap window) matching its scope tag — a Colony-scoped stream is never advanced under TurnBased, and an Encounter-scoped stream is never advanced under RealTime. A purpose that genuinely spans both modes must be split into two streams, one per scope, preserving the one-stream-per-`(system × purpose)` rule. This is debug-asserted at the draw site (see Validation Criteria) — the draw-side complement to the byte-side firewall.

Encounter-stream determinism holds both ways: a battle started fresh re-seeds from `(masterSeed, EncounterId)` and is reproducible; a battle resumed from a checkpoint restores each encounter stream's exact mid-stream state — which is precisely why AC-67 (resume == unquit control) passes. *(This relies on `EncounterId` being deterministic and serialized, which ADR-0004 item 4 guarantees; should `EncounterId` ever be RNG-derived, that draw must come from a Colony-scoped stream pre-switch.)*

### Architecture Diagram

```
composition root
  masterSeed (ulong, from colony-save header)
        │
        ▼
  RngService(masterSeed)
        │  GetStream(RngStreamId)  ── injected handle per (system × purpose)
        ├──────────────► Needs / MapGen / ColonistAppearance / RaiderComposition   [Colony scope]
        └── BeginEncounter(id) ─► CombatResolution / RaiderAI                        [Encounter scope]
                                     (discarded at PostEncounterReconcile)

  draws happen ONLY inside Tick()/authority-driven resolution, or the
  bounded world-creation/load window (MapGen, initial appearance)      (ADR-0001)

  colony save  ◄── SnapshotColony                        (Colony streams + masterSeed)
  checkpoint   ◄── SnapshotColony + SnapshotEncounter    (full self-contained save:
                                                          Colony streams + masterSeed
                                                          + Encounter streams; ADR-0004 writer only)
```

### Key Interfaces

- **`IRngStream`** (handle): `uint NextUInt()`, `uint NextUInt(uint maxExclusive)` (Lemire-unbiased), `int NextInt(int minInclusive, int maxExclusive)`, `bool NextChance(uint numerator, uint denominator)`, `double NextUnitInterval()`. All advance integer state only; all allocation-free. `NextUnitInterval()` is the only surface touching floating point and its derivation is **pinned**: from the top 53 bits of a 64-bit draw, `(draw >> 11) * (1.0 / (1UL << 53))`. It is computed from an integer draw, so no float ever enters stored state; .NET 8 uses strict IEEE-754 doubles on every Godot 4.7 export target (no x87 extended-precision path), so the derivation is itself bit-identical cross-platform.
- **`RngService`**: `IRngStream GetStream(RngStreamId id)`; `void BeginEncounter(ulong encounterId)`; `void EndEncounter()`; `void SnapshotColony(IBufferWriter<byte> w)` / `void RestoreColony(ReadOnlySpan<byte> r)`; `void SnapshotEncounter(IBufferWriter<byte> w)` / `void RestoreEncounter(ReadOnlySpan<byte> r)`. The Snapshot/Restore signatures follow ADR-0004's caller-buffer (`SnapshotInto`) obligation so the checkpoint path allocates nothing on the sim thread.
- **Thread handoff**: `Snapshot*` methods perform a synchronous struct copy on the calling (sim) thread and hand **only already-serialized bytes** to ADR-0004's async writer — no live `RngService` or stream reference ever crosses the thread boundary, so the async checkpoint write cannot observe a torn generator state.

## Alternatives Considered

### Alternative 1: `System.Random` / Godot `RandomNumberGenerator`
- **Description**: Use a stock generator from .NET or Godot.
- **Pros**: No implementation to write.
- **Cons**: `System.Random`'s algorithm and internal state are undocumented and **changed between .NET Framework and .NET Core** — not seed-portable, not serialization-stable. Godot's `RandomNumberGenerator` is a Godot dependency (forbidden in the core) and its state exposure carries no cross-version save-format contract. Neither guarantees byte-identical reproduction across platforms and versions.
- **Rejection Reason**: Fails the two non-negotiables — explicit serializable state and cross-platform/-version reproducibility.

### Alternative 2: xoshiro256** + SplitMix64 seeding + jump-based streams
- **Description**: A xoshiro256** core, seeded via SplitMix64, with `jump()` used to carve non-overlapping per-system sub-streams.
- **Pros**: Very fast; excellent quality; explicit serializable state (meets the hard requirements).
- **Cons**: 256-bit state per stream (larger checkpoints); stream separation needs jump machinery rather than PCG's free per-stream `inc`; more moving parts for no benefit at our draw volumes.
- **Rejection Reason**: A viable fallback, but PCG gives cheaper independent streams, smaller state, and a simpler mental model. Recorded as the fallback if PCG quality ever proved insufficient (it will not at this scale).

### Alternative 3: Serialize seed + draw count, replay on load
- **Description**: Store each stream's seed and how many draws occurred; replay them on restore.
- **Pros**: Marginally smaller on disk.
- **Cons**: O(draws) restore; fragile; re-derives state the generator already holds; directly defeats the "resumable at arbitrary draw counts" requirement.
- **Rejection Reason**: Contradicts the resumability requirement for a negligible disk saving.

### Alternative 4: A single global RNG
- **Description**: One generator shared by all systems.
- **Pros**: Trivial.
- **Cons**: Draw-order coupling — adding a draw in one system shifts every subsequent result everywhere; makes independent reproducibility impossible and makes the Colony/Encounter scope split unrepresentable.
- **Rejection Reason**: Breaks per-system reproducibility and the save-scope firewall.

## Consequences

### Positive
- The project's determinism guarantees become concrete and testable; save/load and checkpoint resume reproduce bit-for-bit.
- Per-system streams isolate change: a new draw site perturbs only its own stream.
- Draws are allocation-free; stream state is a value type.
- Integer-only arithmetic + fixed serialization → cross-platform stability.
- Unblocks Save/Load #6 and completes ADR-0004's AC-67.

### Negative
- A small hand-maintained `RngStreamId` registry with per-id constants must be kept current.
- Discipline required (no ad-hoc RNG, correct scope tag) — enforced by assertion + CI grep, not left to convention.
- One more thing to get right: the Colony/Encounter scope tag on each stream.

### Risks
- **A system draws outside its stream or outside `Tick()`** → nondeterminism. *Mitigation*: injected handles only; a CI grep gate bans `System.Random` / `new Random(` / Godot `RandomNumberGenerator` in `Hollowdeep.Core` (same pattern as the zero-Godot grep gate), plus a debug-console invariant note.
- **Bounded-draw bias or platform variance**. *Mitigation*: Lemire unbiased bounded is pinned; distribution + cross-platform vector tests.
- **Serialization drift (endianness/culture)**. *Mitigation*: fixed little-endian widths, integer-only, culture-invariant; golden test vectors checked into `tests/`.
- **Master seed not captured** → unreproducible world. *Mitigation*: master seed lives in the colony-save header, schema-versioned.
- **Stream-constant collision**. *Mitigation*: the registry is enumerated and CI-checked for duplicate per-id constants.
- **Downstream floating-point *math* determinism** (beyond `NextUnitInterval`'s pinned derivation) remains the **caller's** concern — there is no project-wide float-determinism policy yet. State-affecting decisions in sim code should prefer integer draws/comparisons; a float derived from a draw is fine for presentation or for decisions immediately reduced to an integer/boolean, but accumulating floats into stored state is out of this ADR's guarantee.
- **`ulong` across the Godot `Variant` boundary** (forward-looking). Stream state stays raw `ulong` because it moves only as bytes inside plain C# (`Snapshot`/`Restore`), never through Godot's `Variant` — which has no unsigned-64-bit type (the reason ADR-0003 declares `EntityId` as `long`). *Mitigation*: if the DebugConsole overlay (#29, Godot-referencing) ever displays or binds a `masterSeed`/stream value, format it as a hex string or cast through `long` explicitly — do not pass raw `ulong` across the `Variant` boundary, repeating the mistake ADR-0003 already fixed for `EntityId`.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| time-authority-mode-switch.md | AC-67 — deterministic resume vs an unquit control run | Full per-stream state serialization (§3) + encounter-stream restore (§4); the checkpoint carries exact mid-stream state |
| time-authority-mode-switch.md | Determinism / `TickSequence` as the save/replay anchor | Draws-inside-`Tick` honoured (§ Constraints); the seeded streams are the RNG the `TickSequence` determinism claim quantifies over |
| Save/Load & World Serialization (#6) | A serialization format for RNG state | §3 record layout + §4 scope partitioning; master seed in the colony-save header |
| (via ADR-0003) Colonist Entity / Raid Trigger | Deterministic appearance seed (CD-4); reproducible raider composition (CD-3) | `ColonistAppearance` and `RaiderComposition` Colony-scoped streams (§2, §4) |
| (via ADR-0004) Battle Checkpoint | Content item 3 — combat RNG streams resumable at arbitrary draw counts | §1 explicit state + §3 full-state serialization + §4 Encounter scope |

## Performance Implications
- **CPU**: a PCG draw is a few `ulong` operations (~1–2 ns); negligible; zero allocation.
- **Memory**: 16 bytes of state per stream × a handful of streams; encounter streams released at `PostEncounterReconcile`.
- **Load Time**: restore is O(streams), not O(draws) — negligible.
- **Network**: n/a.

## Migration Plan
Nothing ships yet — the save/load spike validated the colony path without RNG state. Save/Load #6 implements this ADR: the colony-save header gains a `masterSeed` field and a schema-versioned RNG-stream block; the battle checkpoint gains its encounter-stream block (ADR-0004 writer). No existing data migrates.

## Validation Criteria
1. **Same-seed reproduction**: identical master seed + identical authority-driven input sequence → identical draw sequences and byte-identical `Snapshot`, run twice (CI, alongside ADR-0001/0002 determinism gates).
2. **Cross-platform golden vectors**: pcg32 with a fixed seed produces the checked-in reference sequence on Windows/Linux/macOS exports — including at least one `NextUnitInterval()` vector, the only API surface touching floating point.
3. **Stream independence**: adding a draw to stream A does not change stream B's output (property test).
4. **State round-trip**: `Snapshot`→`Restore`→continue is byte-identical to an uninterrupted continue, at arbitrary draw counts.
5. **Scope firewall (byte-side)**: a colony save contains zero encounter-stream bytes; the checkpoint contains them.
6. **Checkpoint self-containment**: a checkpoint round-trips the **Colony stream block + `masterSeed`** as well as the Encounter streams; restoring a checkpoint and continuing **through `PostEncounterReconcile` into RealTime** reproduces the unquit control run bit-for-bit — including the first post-battle *colony* draws (Needs/ambient), not only combat draws (AC-67 technical half — the shared ADR-0004 measurement).
7. **Scope/authority binding (draw-side)**: a Colony-scoped stream drawn under TurnBased, or an Encounter-scoped stream drawn under RealTime, trips a debug assertion — the draw-side complement to criterion 5.
8. **Bounded draws** are unbiased (chi-square within tolerance) and platform-stable.
9. **CI grep gate**: no `System.Random`, `new Random(`, or Godot `RandomNumberGenerator` anywhere in `Hollowdeep.Core`.

## Related Decisions
- ADR-0001 — draws only inside authority-driven execution; `TickSequence` determinism anchor
- ADR-0003 — per-system streams touch entity spawn (appearance seeds, raider composition)
- ADR-0004 — checkpoint content item 3 and AC-67; encounter-stream serialization writer
- Cross-cutting contract #2 (serialization) — RNG streams round-trip like all other state
- Save/Load quick-spec (#6) — the consumer; blocked until this ADR is Accepted
