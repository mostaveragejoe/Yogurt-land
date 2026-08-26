# ADR-0005: Seeded RNG

## Status
Proposed · **Amended 2026-08-26** (encounter re-roll on colony-save reload — QQ-01 closed)

> ### Amendment 2026-08-26 — Encounter re-roll on colony-save reload (closes QQ-01)
>
> **User ruling, reaffirmed 2026-08-26: the encounter re-roll ships.** It is the mechanism that
> stops a player scouting a raid's breach and composition, reloading, and preparing against
> known information — the pre-reveal CD-15 forbids, and the erosion of Pillars 2 and 3 that
> follows from it.
>
> **This costs nothing in schedule and nothing in guarantees.** The apparent conflict with
> `TR-time-026` came from one sentence covering two different properties. Separated:
>
> | Property | Mechanism | Affected? |
> |---|---|---|
> | **Resume determinism** — restore a checkpoint, the battle continues identically (`TR-time-025`, AC-67, Battle Persistence) | Checkpoint restores the combat stream's `State` **directly**; never re-derives | **No — untouched** |
> | **Cross-save re-derivation** — reload a colony save, get the *same* encounter | `BeginEncounter` re-derives from the key material at **battle start only** | **Yes — and this is precisely the exploit being closed** |
>
> `BeginEncounter` is the sole re-derivation point and runs at battle start. Checkpoint restore
> restores `State` and never calls it. **Changing the derivation key therefore changes which
> battle you get and can never affect resuming the battle you are in.** Battle Persistence is a
> shipped player-facing feature, not a test convenience, and it is unaffected.
>
> **1. The Combat stream gains `EncounterAttempt` as derivation input.**
> `Inc,State = splitmix64(RootSeed, Combat-key, EncounterId, EncounterAttempt)`. Same
> forced-odd `Inc` rule, same `State`-only serialization; one more input to the mixer.
>
> **2. `EncounterAttempt` MUST NOT live in the colony save.** This is the whole point and the
> easy thing to get wrong: a counter persisted *inside* the save is restored *with* the save, so
> the reload reproduces the attempt number and **re-rolls nothing**. It lives in a
> **per-installation profile file under Godot's `user://`**, written by the Godot composition
> root and injected into the core as a plain `uint` — the identical pattern `RootSeed` already
> uses, since `Hollowdeep.Core` has no file access and no entropy source. It increments each
> time an encounter is **generated** for a given `EncounterId`.
>
> **3. `TR-time-026` is NOT weakened — its declared input set is corrected.** The requirement
> reads *"given fixed seed **+ input sequence** … bit-identical."* `EncounterAttempt` becomes a
> **declared determinism input** alongside `RootSeed`, rather than TR-time-026 taking a
> carve-out. Given the same `(RootSeed, EncounterAttempt, input sequence)` the run is still
> bit-identical and still fully testable. The guarantee keeps its strength; the input list
> becomes honest. *(This supersedes the "needs an explicit carve-out" framing recorded in
> `requirements-traceability.md` and `architecture-review-2026-08-26.md` — the sharper framing
> was found while drafting this amendment.)*
>
> **4. Threat model — stated, not implied.** A determined player can edit the profile file and
> reach a chosen attempt number. That is the **same posture ADR-0004 already records**: *"the
> anti-save-scum design closes UI paths, not disk tampering."* The ruling closes the
> reload-and-retry loop available through normal play; it does not claim tamper-proofing.
>
> **5. QA reproducibility — the one genuine cost, and its mitigation.** With the counter outside
> the save, "load save X, trigger the raid, observe bug Y" is no longer reproducible from the
> save file alone. **Obligation on Debug Console (#29)**: a command to read and pin
> `EncounterAttempt`, and bug reports record it beside `RootSeed`. Determinism for testing is
> fully preserved — it is pinned explicitly rather than implied by the save file.
>
> **6. GDD Open Question 3b is closed by this amendment**
> (`time-authority-mode-switch.md:296`), and `TR-time-027` was never in conflict: it governs
> mid-battle checkpoint resume, a different reload point (OQ 3a, closed 2026-08-02).

## Date
2026-08-08

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 |
| **Domain** | Core (determinism / serialization) |
| **Knowledge Risk** | LOW — the entire RNG lives in the plain-C# `Hollowdeep.Core` assembly with zero Godot dependency; no post-cutoff Godot API is touched |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md`, `current-best-practices.md` |
| **Post-Cutoff APIs Used** | None. The implementation uses only .NET 8 integer arithmetic and `System.Buffers.Binary`; no Godot `RandomNumberGenerator`, no `System.Random` |
| **Verification Required** | (1) Golden-vector unit test proving the ported PCG stream is bit-identical to the PCG reference across OS/CPU and .NET patch versions; (2) CI-grep proving no `System.Random`/`RandomNumberGenerator`/`Guid.NewGuid`/time-based seeding appears in `src/core` |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | **ADR-0001** Time Authority (Accepted) — draws occur only inside `Tick()`/authority-driven resolution; `TickSequence` is the determinism anchor. **ADR-0004** Battle Checkpoint (Proposed) — consumes its `SnapshotInto(IBufferWriter<byte>)` caller-buffer contract *and* its `AwaitingPresentation → NextActor` snapshot beat for the combat group (see B3/Serialization) |
| **Enables** | **ADR-0004** content item 3 — this ADR supplies the combat RNG **stream format** ADR-0004 reserves a slot for; Save/Load quick-spec #6; GDD AC-67 (deterministic resume) |
| **Blocks** | Save/Load quick-spec #6 — must not be specced before this ADR exists |
| **Ordering Note** | ADR-0004 and ADR-0005 are **co-dependent on the combat-group boundary** (0005 supplies the format; 0004 supplies where/when + the pooled buffer) and **promote together**. ADR-0005's own promotion has **no target-hardware gate** — its validation is headless logic (criteria 1–3, 5, 6); criterion 4 is a determinism harness gated only on ADR-0004's checkpoint existing, not a perf measurement |

## Context

### Problem Statement
The battle checkpoint (ADR-0004) must resume a suspended battle **bit-deterministically against an unquit control run** (GDD AC-67, EC-8), which requires every random draw to be reproducible after a save/load at an arbitrary point mid-battle. Full determinism (`TR-time-026`) extends this across a RealTime→TurnBased→RealTime cycle plus a save/load round-trip — including a **second battle after a colony save/load**, not only within one battle. No RNG architecture exists yet, and ADR-0004 explicitly reserves the RNG content slot for "the Seeded RNG ADR." ADR-0001 already constrains the solution: draws may occur only inside authority-driven execution, or `TickSequence` guarantees nothing.

### Constraints
- **Zero Godot dependency** — the RNG lives in `Hollowdeep.Core`; Godot's `RandomNumberGenerator` is out (CI-grep-enforced boundary).
- **No entropy source in core** — `System.Random`, `Guid.NewGuid`, and time-based seeding are all CI-grep-banned in `src/core`, so the *root seed itself cannot be generated inside the core*; it is injected as a plain value (see New-game seeding).
- **Deterministic across runtimes** — `System.Random`'s algorithm is not a stable contract across .NET versions and differs from .NET Framework; it cannot back a save format. The algorithm must be one we own and pin.
- **Resumable at arbitrary draw counts** — the persisted form must reconstruct the exact next value with no replay (`TR-time-025`).
- **Draws only inside `Tick()`/authority-driven resolution** (ADR-0001) — never in `_Process`, UI callbacks, or bus handlers.
- **Zero steady-state allocation** in the simulation path (technical-preferences) — draws must not allocate or box.

### Requirements
- Serialize a stream's live position to a few bytes and restore it to produce the identical subsequent sequence.
- Independent streams per purpose, so adding a draw in one system does not shift another system's sequence.
- The combat stream must produce identical sequences for battle *N* whether or not a colony save/load happened before it (cross-save determinism), while never being written into a colony save (ADR-0003/0004 firewall).
- Unbiased bounded-integer and float helpers, allocation-free and deterministic.

## Decision

Adopt **PCG (PCG-XSH-RR, 64-bit state → 32-bit output)** as the single sanctioned pseudo-random generator, exposed as a small mutable value type, with **named independent streams derived deterministically from one root seed**, owned by a `SeededRngStore` handed out at the composition root under the same draw-handle discipline the entity stores use for writes (ADR-0003).

### Core generator (PCG-XSH-RR 64/32)
A `PcgRng` struct carrying `ulong State` + `ulong Inc` (an **odd** increment that selects the stream sequence). One step, in an explicit `unchecked` block (correctness depends on mod-2⁶⁴ wraparound):
1. **Emit from the pre-advance state**: `output = XSH_RR(oldState)` — the permutation reads `State` *before* the LCG step (matches the reference `pcg32_random_r`; computing it from the post-advance state is off-by-one and fails golden vectors).
2. **Advance**: `State = oldState * 6364136223846793005 + Inc`.

`XSH_RR` = xorshift-high then random (state-selected) rotate to a 32-bit word. Built on top:
- `uint NextUInt32()`, `ulong NextUInt64()` (two 32-bit draws, high word first),
- `int NextInt(int maxExclusive)` — **Lemire's unbiased bounded method** (nearly divisionless, rare rejection; validates `maxExclusive > 0`, arithmetic in unsigned space; deterministic and allocation-free),
- `double NextDouble()` — top 53 bits scaled by 2⁻⁵³, in [0,1).

`PcgRng` is a plain mutable `struct` (16 bytes) — no boxing, no allocation. **Mutable-struct discipline (required, not optional):** live streams are held in a raw `PcgRng[]` indexed by the stream enum; draws mutate in place via the array indexer. The store must **never** hold streams in a `List<PcgRng>` (its indexer returns a copy — the draw is lost), never expose a stream via a struct-returning property getter, and never store a `PcgRng` in a `readonly` field. This is a real C# correctness contract, not style.

### Streams from one root seed
- The game holds a single `ulong RootSeed`, persisted in the colony save.
- Each purpose is a stable key in an append-only `RngStream` enum (`Combat`, `Worldgen`, `RaidSchedule`, `NeedsJitter`, `NameGen`, …, terminated by a `Count` sentinel). Keys are appended, never renumbered.
- Each stream's `(State, Inc)` is derived deterministically via **splitmix64** over the stream's key material. **The increment is forced odd**: `Inc = (splitmix64(...) << 1) | 1` — PCG's period and stream-separation guarantees require an odd increment; a raw splitmix64 output is odd only half the time.
- Because `Inc` is a pure function of the key material and never changes, **only `State` is ever serialized** (8 bytes/stream); `Inc` is re-derived on load. Serialization uses `BinaryPrimitives.WriteUInt64LittleEndian`/`ReadUInt64LittleEndian` — the wire format is explicit little-endian, decoupled from in-memory struct layout (no raw struct memcpy).

### New-game seeding & seed provenance (B1)
`Hollowdeep.Core` cannot generate entropy (grep-banned). At new game, the **Godot composition root** produces `RootSeed` — from OS entropy, or a player-supplied value for shared/seeded runs — and hands it to `new SeededRngStore(rootSeed)` as a plain `ulong`. This is the same injection pattern ADR-0004 uses for save-path resolution (core takes a plain value; Godot resolves it once). `RootSeed` round-trips in every colony save.

### Two serialization groups, and the combat re-derivation rule (B2)
Streams split into two groups, mirroring the **checkpoint-vs-colony-save firewall** (ADR-0003 amendment / ADR-0004) — this is distinct from the per-system draw-handle grant, which mirrors ADR-0003's *writer-ownership* pattern:
- **colony-persistent** streams (`Worldgen` [Tier 2, see below], `RaidSchedule`, `NeedsJitter`, …) — their `State` serializes into **colony saves**.
- **combat-transient** stream(s) (`Combat`, and any encounter-only stream) — **re-derived per encounter** at battle start from `splitmix64(RootSeed, Combat-key, EncounterId)`. `EncounterId` is monotonic and persisted (encounter framing, ADR-0001/0004), so battle *N*'s stream is identical whether or not a colony save/load preceded it — this is what makes "combat-transient" determinism-safe rather than merely tidy, and it closes the cross-save gap in `TR-time-026`. The combat stream is **never written to a colony save**; only its **mid-battle `State` position** is captured in the checkpoint.

### Combat capture beat (B3)
`SeededRngStore.SnapshotInto(combat group)` executes **inside ADR-0004's single synchronous `AwaitingPresentation → NextActor` snapshot, into the same coalesced pooled buffer, before the turn loop resumes and before any subsequent draw.** The beat is provably between draws (no resolution is in flight at `NextActor`, and a single draw — including Lemire's rejection loop — is atomic), so the RNG position is captured at the same logical instant as entities/terrain/side-tables. This is the RNG analogue of ADR-0004's "`EncounterOutcomeInbox` is provably empty at every checkpoint": no partial-draw state is ever checkpointed. A restored checkpoint restores **both** groups (the checkpoint is a full self-contained save — colony-persistent group via the base save plus the combat group; "combat only in the checkpoint" means checkpoint-*exclusive*, not checkpoint-*only-contents*).

### Draw discipline
Systems never hold their own `PcgRng`; they receive a narrow draw handle for exactly the stream(s) they own (Combat gets `Combat`; Raid Trigger gets `RaidSchedule`), granted at the composition root — the ADR-0003 writer-grant analogue. Handles are **mode-tagged**: a `Combat` handle asserts `Mode == TurnBased`, a `RaidSchedule`/`NeedsJitter` handle asserts `Mode == RealTime`, catching out-of-mode draws that a plain window assertion would miss. A draw is a write to `SeededRngStore`, so it runs under the same mutation-window / authority-driven-execution gate as any store write (ADR-0001/0002/0003) — the inherited assertion, not a new one.

### Architecture Diagram
```
RootSeed (ulong)  ── produced at the Godot composition root; persisted in colony save
   │
   ├─ colony-persistent streams:  Inc,State = derive(RootSeed, key)      → colony save (State only)
   │       RaidSchedule, NeedsJitter, NameGen, [Worldgen — Tier 2]
   │
   └─ combat-transient stream:    Inc,State = derive(RootSeed, Combat, EncounterId)   at battle start
           Combat                 → re-derived per encounter; mid-battle State only → checkpoint
                                     captured at ADR-0004's AwaitingPresentation→NextActor beat
   │
   ▼ granted, mode-tagged draw handles (composition root, per system)
Combat.Tick()  draw(Combat)   [asserts TurnBased] ;  RaidTrigger.Tick()  draw(RaidSchedule) [asserts RealTime]
   (draws only inside Tick()/authority-driven resolution — ADR-0001)
```

### Key Interfaces
```csharp
public struct PcgRng {              // 16 bytes; plain mutable struct, no alloc
    public ulong State;
    public ulong Inc;               // odd; selects the stream sequence
    public uint   NextUInt32();     // output from pre-advance state, then advance (unchecked)
    public ulong  NextUInt64();
    public int    NextInt(int maxExclusive);   // Lemire, unbiased, maxExclusive > 0
    public double NextDouble();                 // [0,1), top 53 bits
}

public enum RngStream : int { Combat = 0, Worldgen, RaidSchedule, NeedsJitter, NameGen, Count /* append before Count */ }
public enum RngStreamGroup { ColonyPersistent, CombatTransient }

public sealed class SeededRngStore {
    public SeededRngStore(ulong rootSeed);                    // derives colony-persistent streams (odd Inc)
    //   EncounterAttempt is injected per-encounter from the composition root (user:// profile
    //   file, NEVER the colony save) — Amendment 2026-08-26
    public void BeginEncounter(long encounterId, uint encounterAttempt);  // (re)derives Combat from
                                                            // RootSeed+key+id+attempt (Amendment 2026-08-26)
    // draws (granted narrowly + mode-tagged per system at the composition root):
    public int    NextInt(RngStream s, int maxExclusive);
    public double NextDouble(RngStream s);
    // serialization, State only, little-endian, by group (owner = this store):
    public void SnapshotInto(IBufferWriter<byte> dst, RngStreamGroup group);   // ADR-0004 caller-buffer contract
    public void Restore(ReadOnlySpan<byte> src, RngStreamGroup group);         // re-derives Inc, restores State
}
```

## Alternatives Considered

### Alternative 1: Seed + replay draw count
- **Description**: Persist the seed and a per-stream draw counter; on load, re-draw N times to fast-forward.
- **Pros**: Tiny persisted state; trivially correct.
- **Cons**: O(draws) load cost growing with battle length; fragile if any draw path changes; conflates "how many draws" with "which values."
- **Rejection Reason**: `TR-time-025` demands resume at *arbitrary* draw counts with no replay; a state-based generator resumes in O(1).

### Alternative 2: `System.Random` or Godot `RandomNumberGenerator`
- **Description**: Use a stock RNG.
- **Pros**: Zero implementation cost.
- **Cons**: `System.Random`'s algorithm is not a stable cross-version contract (Microsoft documents this; it changed between .NET Framework and Core and within Core's history), so a saved stream can decode differently after a runtime update — fatal for a save format. Godot's `RandomNumberGenerator` is a Godot dependency, banned in `Hollowdeep.Core`.
- **Rejection Reason**: Neither can back a portable, version-stable save; both violate a hard project constraint.

### Alternative 3: xoshiro256** / splitmix64-only
- **Description**: Other own-able PRNGs.
- **Pros**: xoshiro is fast with `jump()` for stream separation; splitmix64 is minimal.
- **Cons**: xoshiro has 256-bit state (32 bytes/stream vs PCG's 16, i.e. **2×** per-stream footprint) and separates streams by jumps rather than a native sequence selector; splitmix64-only is weaker for many independent streams.
- **Rejection Reason**: PCG's native per-stream sequence selector maps directly onto the "named independent streams" model. splitmix64 is retained but only as the *seeding/derivation* mixer — its ideal role.

## Consequences

### Positive
- Exact O(1) resume at any draw count → satisfies AC-67 / `TR-time-025` cleanly.
- Per-encounter combat re-derivation from `EncounterId` gives cross-save determinism (`TR-time-026`) *and* per-battle variety while honouring the "never in colony saves" firewall.
- Independent streams make determinism robust to ordinary code changes (a new draw in one system can't desync another).
- Plain-struct, allocation-free draws keep the zero-steady-state-allocation standard.
- Owned, pinned algorithm + explicit little-endian wire format → save format stable across .NET versions and platforms; only 8 bytes/stream persisted.
- The serialization split and draw-handle grant fall out of the existing ADR-0003/0004 ownership models — no new save-authority concept.

### Negative
- We must port and maintain a PRNG with golden test vectors (small, one-time).
- Draws must be routed through granted, mode-tagged stream handles; ad-hoc `new Random()` is forbidden and policed.
- The `RngStream` key set must be curated (append-only, like `EntityId` kinds).

### Risks
- **Bit-stability across platforms/runtimes** → golden-vector unit tests in CI on every target; explicit little-endian serialization; `unchecked` block so a project-wide overflow-check flag cannot silently break the LCG step.
- **Accidental `System.Random`/`Guid`/time-seeding in core** → CI-grep gate.
- **Even increment / wrong output-order** (the two easy-to-miss PCG bugs) → pinned in the Decision and asserted by the golden-vector test.
- **Mutable-struct copy bug** (`List<PcgRng>`, readonly field, struct-returning getter) → banned explicitly in the Decision; a copy bug is caught by the round-trip/independence tests.
- **An unkeyed or out-of-mode draw** → mode-tagged handles + ADR-0001's draws-inside-Tick assertion; code review on new draws.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| time-authority-mode-switch.md | `TR-time-025` — checkpoint carries RNG stream state resumable at arbitrary draw counts (AC-67, EC-8) | State-based PCG; the combat stream's mid-battle `State` is serialized into the checkpoint by `SeededRngStore` at ADR-0004's snapshot beat, resumed in O(1) |
| time-authority-mode-switch.md | `TR-time-026` — full determinism across a mode cycle + save/load round-trip, including a second battle | Streams from one persisted `RootSeed`; combat stream re-derived per encounter from `EncounterId`, so battle *N* is identical with or without a prior colony save/load; positions serialized by group |
| time-authority-mode-switch.md | `TR-time-027` — draws only inside authority-driven execution; reload re-rolls nothing | Mode-tagged draw handles usable only inside `Tick()`; inherits ADR-0001's assertion; resume replays nothing |
| terrain-data-model.md | `TR-terrain-033` — snapshot/restore determinism; procgen reproducibility | **MVP has no procgen** (hand-authored mountain, #14); terrain reproducibility is satisfied by the persisted `TerrainWorld` snapshot, not RNG. The `Worldgen` stream is a **reserved Tier-2 key** (#35); its position is not serialized in MVP. When #35 lands: one-shot generation ⇒ terrain-as-data still suffices; incremental generation ⇒ the Worldgen position must then persist (decided in #35's GDD) |

## Performance Implications
- **CPU**: Negligible — one 64-bit multiply-add + a few shifts per 32-bit draw; bounded draws add one multiply and a rare rejection.
- **Memory**: 16 bytes per live stream; a handful of streams → well under 1 KB; serialized combat group is 8 bytes/stream.
- **Load Time**: O(1) resume (no replay).
- **Network**: N/A (single-player MVP).

## Migration Plan
Greenfield — no existing RNG to migrate. This ADR establishes the only sanctioned RNG; any future stock-RNG use is a defect caught by the CI-grep gate.

## Validation Criteria
1. Golden-vector unit test: `PcgRng` reproduces the reference PCG-XSH-RR output for known `(State, Inc)` seeds, identical across OS/CPU/.NET patch (pins the pre-advance-output and odd-`Inc` details).
2. Round-trip test: snapshot a stream's `State` after K draws, restore, and confirm the next M draws match the never-serialized control.
3. Independence test: interleaving extra draws on stream A does not change stream B's sequence; and combat re-derivation from the same `(RootSeed, EncounterId)` reproduces the sequence with/without a prior colony save/load.
4. AC-67 harness (once ADR-0004's checkpoint exists): resume-from-checkpoint reproduces an unquit control run bit-for-bit.
5. CI-grep: no `System.Random`/`RandomNumberGenerator`/`Guid.NewGuid`/time-based seeding in `src/core`.
6. Zero-allocation test: a draw loop records 0 Gen0 collections.
7. **Re-roll test (Amendment 2026-08-26)**: the same `(RootSeed, EncounterId)` with *different*
   `EncounterAttempt` values produces **different** encounter rolls — the save-scum loop is
   actually closed, not merely intended.
8. **Re-roll determinism test**: the same `(RootSeed, EncounterId, EncounterAttempt)` produces
   an **identical** roll across processes and machines — `TR-time-026` holds against its
   corrected input set.
9. **Counter-location test**: a colony save/load round-trip does **not** restore
   `EncounterAttempt` — proving the counter lives outside the save and the reload actually
   re-rolls. This is the test that catches the easy implementation error.
10. **Resume-unaffected test**: checkpoint restore reproduces an unquit control run bit-for-bit
    **regardless of `EncounterAttempt`**, confirming re-derivation and resume are independent
    paths (guards the separation this amendment rests on).

## Related Decisions
- ADR-0001 Time Authority (draws-inside-Tick rule; `TickSequence` anchor)
- ADR-0004 Battle Checkpoint (reserves the RNG content slot; `SnapshotInto` caller-buffer contract; snapshot beat; combat-group serialization) — co-dependent, promotes together
- ADR-0003 Entity Data Ownership (checkpoint-vs-colony-save firewall; composition-root writer/draw grants)
- Save/Load quick-spec #6 (colony-group serialization; consumes this format)
