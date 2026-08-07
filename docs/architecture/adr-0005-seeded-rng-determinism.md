# ADR-0005: Seeded RNG / Determinism

## Status
**Proposed** (2026-08-07)

*(Gates: godot-specialist — sound, no blocking issue; findings folded in (unchecked-arithmetic requirement, named-field serialization, reference test vectors, Alternative E, FMA note, stream pooling). TD-ADR (full mode) — CONCERNS: B1/B2/B4 and A1–A6 applied same day; B3 (reload seed policy) discharged at the ADR level — both policies one-knob cheap, identical-replay placeholder default, design pick routed to Raid Trigger #18's GDD (see §Reload seed policy). Companion edits + registry update applied 2026-08-07 with user approval.)*

## Date
2026-08-07

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 |
| **Domain** | Core |
| **Knowledge Risk** | HIGH for the pinned engine overall — mitigated: the entire RNG contract is plain C# with zero Godot dependency; no post-cutoff Godot API is load-bearing for this decision |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md`, `deprecated-apis.md`, `current-best-practices.md` — none carry RNG-specific entries; this decision is a .NET/C# language-level choice, not an engine-version-specific one |
| **Post-Cutoff APIs Used** | None. The generator is hand-rolled integer arithmetic (`System.UInt64`/`System.UInt32` only) — not `Godot.RandomNumberGenerator` (rejected below: Alternative E) and not `System.Random` (rejected below: Alternative C; not a post-cutoff API, but its determinism guarantee is explicitly out of scope of the BCL's compatibility contract) |
| **Verification Required** | (1) The core `State = State * Multiplier + Increment` step must run in an explicit `unchecked { }` block — unsigned overflow wraparound is the LCG's actual mechanism, not an accident, and C#'s `checked` context (a later hardening pass, an analyzer, or a Debug-build flag) throws `OverflowException` on it otherwise. This is the highest-probability failure mode in this ADR, not a hypothetical. (2) `PcgState` serialization must write the named `State`/`Increment` fields explicitly, never a raw struct memcpy (`MemoryMarshal.AsBytes`, `Marshal.StructureToPtr`) — C#'s default field layout is not contractually pinned across CLR/JIT/AOT toolchain versions, and a reordering would silently corrupt existing save/checkpoint files. (3) `NextUInt32` output should additionally match PCG32's published reference test vectors (O'Neill's paper/reference implementation) in CI — a stronger determinism proof than self-consistency across platforms alone (godot-specialist review, 2026-08-07: pure integer arithmetic is provably deterministic across all CLI-conformant runtimes/hardware once (1) and (2) are handled; there is no integer analogue of the old x87-extended-precision float problem) |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001 (Accepted) — draws only inside `Tick()` or authority-driven resolution; this ADR's load-window draws (colonist embark, future map generation) need a two-word companion amendment to that rule (§Migration Plan). ADR-0003 (Accepted) — entity spawn draws (`AppearanceSeed`); `EntityIdSource` is this ADR's direct precedent for `EncounterIdSource`. ADR-0004 (Proposed) — the `RestoredFromCheckpoint` resume path and load-window restore writer this ADR's combat streams ride, and the serializer of encounter framing (content item 4) that `EncounterIdSource` joins (TD-ADR B2/A5, 2026-08-07). |
| **Enables** | ADR-0004's AC-67 validation criterion (blocking — checkpoint RNG-stream resume needs this ADR's format); Save/Load quick-spec #6 (blocking, per systems-index dependency map); Raid Trigger GDD (#18, breach-point + composition draws); Colonist Entity quick-spec (#9, `AppearanceSeed` draw); Map Authoring / procgen (Alpha #35, post-MVP) |
| **Blocks** | Save/Load quick-spec #6; ADR-0004 promotion to Accepted (AC-67 needs this ADR's stream format) |
| **Ordering Note** | Fourth and last Foundation-layer ADR (systems-index #4). Written against ADR-0001/0003 as Accepted and ADR-0004 as Proposed; the ADR-0004 relationship is deliberately mutual — this ADR fills ADR-0004's reserved content slot while riding its restore machinery. If any of the three is revised, this ADR is re-checked at the same revision point. |

## Context

### Problem Statement
Systems-index #4 names the requirement directly: "per-system seeded streams; without it, sim bugs are irreproducible and saves desync." Five systems already need randomness or are named as future consumers — Raid Trigger (breach-point selection, raider composition — CD-2), Colonist Entity (deterministic appearance seed — CD-4), Combat: Targeting & Resolution (hit/damage rolls), Combat: Raider Decision-Making (AI decisions), and Map Authoring/procgen (Alpha #35, post-MVP) — and ADR-0004 has already reserved a content-scope slot for "combat RNG streams, resumable at arbitrary draw counts" without defining the format. This ADR fixes the algorithm, the per-system stream architecture, the draw-legality rule, and the serialization contract that every one of those systems codes against.

### Constraints
- Solo developer: the simplest structure that meets the requirement wins (house pattern, per ADR-0001/0002/0003)
- ADR-0001's mutation-window discipline: random draws only inside `Tick()` or authority-driven resolution — this ADR's own registered forbidden pattern (`rng_outside_authority_execution`, `docs/registry/architecture.yaml`), extended here to cover the load window
- Serialization contract (cross-cutting #2): plain data, `Snapshot()`/`Restore()`, headless CI round-trip — already names "per-system seeded RNG streams" as part of its scope
- Combat-transient firewall (ADR-0003): anything meaningless outside an encounter never enters a colony-mode save — encounter-scoped RNG streams follow the same rule, serialized only into the battle checkpoint (ADR-0004)
- Zero steady-state allocation (measured, project-wide standard) — draws must be integer arithmetic on existing fields, never allocation
- Plain C#, zero Godot dependency, headlessly testable

### Requirements
- Deterministic: same `MasterSeed` + same recorded inputs → byte-identical stream state and draw sequence, forever (save/load, replay, bug reproduction — ADR-0001's determinism requirement, restated for RNG)
- Per-system stream isolation: one system's draw count must never perturb another's
- Resumable at arbitrary draw counts, without replay (ADR-0004's AC-67, blocking)
- Combat streams checkpoint-serializable only; persistent streams colony-save-serializable — mirrors the existing combat-transient firewall
- No floating-point hardware variance, no locale-dependent conversion — a raw draw becomes a float only through a bit-construction method this ADR specifies

## Decision

Adopt a **hand-rolled PCG32 (XSH-RR variant)** generator as the sole RNG primitive, with **per-system named streams** derived from one world `MasterSeed` via a stream-selector mechanism native to the algorithm. Plain C#, zero Godot dependency.

### The generator — 16 bytes of state, integer-only

```csharp
// Namespace: Hollowdeep.Core.Random — plain C#, zero Godot dependency.
// PCG32, XSH-RR 64/32 variant (Melissa O'Neill's PCG family). Chosen for the
// smallest state that still gives native multi-stream support: Increment IS
// the stream selector — any odd 64-bit value yields a statistically
// independent stream from the same State. No floating point in the core step.

public struct PcgState                 // the ENTIRE resumable position — 16 bytes.
{                                       // "resumable at arbitrary draw counts"
    public ulong State;                // (ADR-0004 AC-67) reduces to: serialize
    public ulong Increment;            // these two fields. No replay, ever.
}                                       // Increment MUST be odd; only RngSeeder sets it.
                                        // Snapshot()/Restore() write State/Increment as two
                                        // NAMED fields, NEVER a raw struct memcpy — default
                                        // C# field layout is not pinned across CLR/JIT/AOT
                                        // versions (godot-specialist review, 2026-08-07).

public sealed class RngStream          // mutable class — matches the house pattern
{                                       // (ColonistStore etc. are classes, not structs);
                                        // zero allocation per draw
    public RngStream(PcgState seed);

    public uint NextUInt32();          // core operation: multiply, xor-shift, rotate.
                                        // The multiply-add step runs in an explicit
                                        // `unchecked { }` block — unsigned overflow
                                        // wraparound IS the LCG's mechanism, not a bug
                                        // (godot-specialist review, 2026-08-07: the
                                        // highest-probability failure mode here).
    public ulong NextUInt64();         // two NextUInt32 draws, documented composition
    public int NextInt(int minInclusive, int maxExclusive);  // Lemire's method — no modulo bias
    public float NextFloat01();        // [0,1) via a specified bit-shift construction —
                                        // NEVER via culture-aware parsing or FPU-mode-
                                        // dependent conversion (Risks)
    public bool NextBool(float probability = 0.5f);

    public PcgState Snapshot();        // the 16 bytes, by value
    public void Restore(PcgState state); // debug-asserts state.Increment is odd — PcgState's
                                        // public fields could otherwise install an even
                                        // increment and silently degrade the stream
                                        // (TD-ADR A4, 2026-08-07)

    // Every Next* method debug-asserts the ADR-0001 mutation window is open
    // (Tick() / authority-driven resolution / the load window — see the
    // companion amendment below). Same mechanism ADR-0002/0003 already use.
}
```

### Stream derivation — one MasterSeed, N independent streams

```csharp
public enum RngStreamId : byte
{
    RaidTrigger      = 0, // breach-point selection, raider composition (CD-2)
    ColonistIdentity = 1, // AppearanceSeed draw at embark (CD-4)
    MapGeneration    = 2, // reserved — Alpha #35, post-MVP
    CombatResolution = 3, // encounter-scoped — see below
    CombatRaiderAi   = 4, // encounter-scoped — see below
}
// Values are EXPLICIT and NEVER renumbered — the id participates in stream
// derivation, so renumbering silently re-seeds every stream and breaks the
// "replay one fight from (MasterSeed, EncounterId)" guarantee across versions
// (TD-ADR A2, 2026-08-07; same append-only discipline as ADR-0002's material
// manifest). New stream kinds later = a new appended enum value + one
// ownership-table row (below) — the same no-generic-grab-bag guard ADR-0003
// applies to entity stores, applied to RNG: no "misc RNG" stream exists.

public static class RngSeeder
{
    // Persistent streams: derived ONCE at world creation, then advance for the
    // whole playthrough. Mixing is SplitMix64 over (masterSeed, id) — a seeding
    // computation, not itself a gameplay "draw".
    public static PcgState DerivePersistent(ulong masterSeed, RngStreamId id);

    // Encounter-scoped streams: derived fresh at EVERY switch-in from
    // (masterSeed, id, encounterId). A single battle's RNG behavior is then
    // reproducible in isolation — replay one fight without replaying the
    // whole colony history.
    public static PcgState DeriveEncounterScoped(ulong masterSeed, RngStreamId id, long encounterId);
}
```

`MasterSeed` (`ulong`) is the **one external-entropy admission point in the whole game**: player-supplied or drawn from OS entropy exactly once, at new-colony creation, outside the mutation window (it precedes the world existing, so there is no window yet to be inside). Every draw for the rest of that playthrough is a pure deterministic function of `MasterSeed` plus recorded inputs. `MasterSeed` is serialized in the colony-mode save.

### The RNG Stream Ownership Table (the core deliverable)

| Stream | Purpose | Derivation | Drawn under | Serialized in | Sole drawer |
|---|---|---|---|---|---|
| `RaidTrigger` | Breach-point selection, raider composition | Persistent — `DerivePersistent` at world creation | RealTime (threat-accumulation `Tick`, per ADR-0001's worked-example row) | Colony-mode save | Raid Trigger (#18) |
| `ColonistIdentity` | `AppearanceSeed`, one draw per colonist at embark | Persistent — `DerivePersistent` at world creation | Load window (embark spawn path) | Colony-mode save | Map Authoring embark path (#14) — Colonist Entity (#9) is a passive store (ADR-0003) that never ticks and never draws; it merely *stores* the resulting `AppearanceSeed` |
| `MapGeneration` | World generation (reserved, not drawn from in MVP) | Persistent — `DerivePersistent` at world creation | Load window | Colony-mode save | Map Authoring / procgen (Alpha #35, post-MVP) |
| `CombatResolution` | Hit/damage rolls | Encounter-scoped — `DeriveEncounterScoped` at switch-in | TurnBased only | **Battle checkpoint only** — never a colony-mode save (ADR-0003 firewall) | Combat: Targeting & Resolution (#22) |
| `CombatRaiderAi` | Raider AI stochastic decisions | Encounter-scoped — `DeriveEncounterScoped` at switch-in | TurnBased only | **Battle checkpoint only** — never a colony-mode save | Combat: Raider Decision-Making (#23) |

The **Drawn under** column is load-bearing, not descriptive (TD-ADR B4, 2026-08-07): like ADR-0002 rule 4's writer table and ADR-0003's per-authority ownership columns, it makes "one drawer at any moment" expressible and assertable — a stream drawn from under the wrong authority fires the same mode assertion the entity stores use. Each system is granted read/draw access to **only its own named stream(s)** at the composition root — mirrors ADR-0003's per-(system × field group) writer-interface segregation, applied to RNG instead of store writes. A system holding a reference to a stream it doesn't own in this table is unrepresentable, the same discipline as the entity-store ownership table.

**Serialization completeness note** (TD-ADR A1): the battle checkpoint is a full self-contained save (ADR-0004 §1), so it carries `MasterSeed`, the persistent stream states, and `EncounterIdSource` via its colony-save schema half automatically — ADR-0004 content item 3's slot covers the two combat streams *specifically*, not the only RNG bytes in the file. Per ADR-0003's binding side-table wording ("serialized only into the battle checkpoint **by its owning systems**"), each combat system snapshots **its own** stream via the checkpoint writer's invocation — there is no central "RNG module" serializer, and ADR-0004 item 3's "Seeded RNG owner" phrasing is corrected to say so (companion edit, TD-ADR A6).

**Presentation randomness has a sanctioned home outside this table** (TD-ADR A3): a `PresentationRng` — OS-entropy-seeded, never serialized, never drawn inside `Tick()` — exists for views, ambient audio (#33), and idle/VFX variation. It is *outside* the determinism guarantee by design; the `rng_outside_authority_execution` forbidden pattern governs the simulation streams in this table, not presentation noise (registry description scoped accordingly — companion edit). Without a named legal generator, view code would inevitably violate the rule with the first idle animation jitter.

**Encounter-scoped derivation happens inside the authority-driven Reaction-phase seam at switch-in** — the same seam ADR-0001/0003 already use for pre-switch placement normalization — so the derivation step itself runs under the mutation window, and the derived streams are handed to Combat's systems before the first `Tick()` of the encounter. The two `RngStream` instances are allocated once at the composition root and `Restore()`d in place at each new encounter — zero allocation per battle (TD-ADR A4 correction: in-place restore allocates nothing; the once-ever object construction is the only allocation, consistent with ADR-0003's encounter-scoped side-table posture).

**Checkpoint restore never re-derives (TD-ADR B1 — the rule that keeps AC-67 true).** Derivation happens at *switch-in only*. On a `RestoredFromCheckpoint` resume (ADR-0004 §4) there is no `RequestSwitch`, no Reaction-phase seam, and **no derivation**: the combat streams are `Restore()`d from the checkpoint's serialized `PcgState`s inside the load window, via ADR-0004's sanctioned restore writer. Re-deriving from `(MasterSeed, EncounterId)` at restore would reset both streams to draw count zero and falsify AC-67's determinism-vs-unquit-control guarantee — the checkpoint's 16 bytes per stream ARE the mid-battle position; that is the entire point of carrying them.

### `EncounterId` gets a defined allocator (a gap this ADR closes; specification per TD-ADR B2, 2026-08-07)

`SwitchTransitionData.EncounterId` (ADR-0001) has been a bare `int` with no specified source since ADR-0001 was written. Encounter-scoped stream derivation needs `EncounterId` to be **unique and never reused** across a playthrough, or two different battles could derive the same combat RNG stream. Fix — `EncounterIdSource`, fully specified:

- **Type**: the field **widens `int` → `long`** (companion amendment to ADR-0001) — same rationale as `EntityId.Value` (ADR-0003): `long` crosses the Godot C# Variant boundary without conversion, and a monotonic counter never approaches `long.MaxValue`. `RngSeeder.DeriveEncounterScoped(..., long encounterId)` and the widened field now agree.
- **Owner**: **Time Authority** — NOT Raid Trigger. ADR-0004 content item 4 already makes Time Authority the serializer of encounter framing (`EncounterId`, `BreachCells`, `ParticipantIds`); a serialized Foundation-layer counter cannot be owned by a Feature-layer system (#18). The `TimeAuthorityManager` allocates the id at `RequestSwitch` acceptance and stamps it into the stored encounter framing; the requester (Raid Trigger, the sole switch-into-combat requester per ADR-0001's worked-example table and the registry's `mode_switch_request` contract) never supplies or sees the counter.
- **Serialized in**: **both the colony-mode save and the battle checkpoint**, riding Time Authority's snapshot alongside `{Mode, TurnIndex, TickSequence}` — "the serialized counter IS the guarantee" (ADR-0003's `EntityIdSource` mechanism) only holds if the counter survives every load path; a colony-save-only counter would reissue an id after a mid-battle relaunch and correlate two battles' rolls.
- **Never reused**, monotonic from 1 — mirrors `EntityIdSource` exactly.

### Reload seed policy (TD-ADR B3; discharges CD-GDD-ALIGN M1's routed obligation)

CD-GDD-ALIGN M1 (2026-08-02) routed one design question here jointly with Raid Trigger #18: after a colony-save reload, does the next raid roll identically or re-roll? This ADR's architectural answer is that **both policies are one-knob cheap, and neither requires revising this ADR**; the design pick is #18's GDD call, where CD-2 and CD-15 live.

- **Identical replay — the placeholder default, by construction.** The serialized `RaidTrigger` stream resumes at its saved position, so the next raid rolls identically: same timing, breach point, composition. Best for bug reproduction; grants exact retry foreknowledge (the CD-15 ceiling concern M1 flagged) — a concern accepted-for-now under the same MVP reload-freedom ruling of 2026-08-07, pending #18's explicit pick.
- **The knob: `ReseedOnLoad` (default off).** When on, a serialized monotonic **load ordinal** (bumped at every load; serialized like `EncounterIdSource`) mixes into the `RaidTrigger` stream derivation at load — post-reload raids differ while staying inside #18's threat band (the band is #18's threat model, never RNG's concern). Kills foreknowledge; opens reload-fishing. Turning it on later is a knob flip plus one already-specified counter, not a format migration.
- **Corrupt-checkpoint fallback interaction** (ADR-0004 §5): a battle restarted from the switch-in autosave re-derives from the same `(MasterSeed, EncounterId)` — the same initial dice tape, consistent with identical replay. Not worth counter-machinery: reaching that path requires disk failure or tampering, which ADR-0004 already places outside the design threat model.

### Draw-legality companion amendment to ADR-0001

ADR-0001's RNG rule currently reads: *"random draws occur only inside `Tick()` or authority-driven resolution — never in `_Process`, UI callbacks, or event handlers."* This does not cover the load window, where `ColonistIdentity` draws happen today (colonist embark) and `MapGeneration` will (Alpha #35). **Companion amendment**: extend the rule to *"…or the load window."* This mirrors ADR-0002 rule 5, which already carves out the load window for terrain mutations the same way — no new mechanism, a two-word extension of an existing one. See Migration Plan.

### Architecture Diagram

```
MasterSeed (ulong) — drawn from OS entropy or player-supplied, ONCE, at new-colony
creation. The one external-entropy admission point in the entire game.
        │
        ├─ RngSeeder.DerivePersistent(seed, RaidTrigger)       ─┐
        ├─ RngSeeder.DerivePersistent(seed, ColonistIdentity)   ├─► persistent RngStreams
        ├─ RngSeeder.DerivePersistent(seed, MapGeneration)      │   (colony-mode save)
        │  (all at world creation)                             ─┘
        │
        └─ at EVERY switch-in (Reaction phase, authority-driven):
              RngSeeder.DeriveEncounterScoped(seed, CombatResolution, encounterId)  ─┐
              RngSeeder.DeriveEncounterScoped(seed, CombatRaiderAi,   encounterId)  ─┤─► encounter-scoped
                                                                                      │   RngStreams
                                                                                      │   (battle checkpoint
                                                                                      │    only — discarded
                                                                                      │    at battle end)
                                                                                     ─┘
```

### Key Interfaces
`PcgState` (16-byte value struct — the entire resumable position; named-field serialization only) · `RngStream` (mutable class; `NextUInt32/NextUInt64/NextInt/NextFloat01/NextBool`; `Snapshot()/Restore()`, odd-increment asserted) · `RngStreamId` enum (ownership-table key; explicit never-renumbered values) · `RngSeeder.DerivePersistent/DeriveEncounterScoped` · `EncounterIdSource` (`long`, monotonic, never reused; owned and serialized by Time Authority in both save kinds — companion amendment to ADR-0001) · `PresentationRng` (non-sim, non-serialized, outside the determinism guarantee)

## Alternatives Considered

### Alternative B: xoshiro256** with jump-ahead tables
- **Description**: A 256-bit-state generator; independent streams via precomputed jump-ahead polynomial coefficients (skip 2¹²⁸ steps).
- **Pros**: Superior statistical quality at scale; used by several production engines.
- **Cons**: 32-byte state (2× PCG's); correct jump-ahead requires precomputed coefficients that are easy to get subtly wrong — a miscomputed jump silently correlates "independent" streams, a hard-to-detect bug class. MVP entity counts (~10 colonists, one raider type, one map) don't need the statistical headroom this buys.
- **Rejection Reason**: PCG's native increment-as-stream-selector achieves the isolation goal with a fraction of the implementation and verification risk; its statistical quality is more than sufficient for combat rolls, breach selection, and appearance seeds at MVP scale.

### Alternative C: .NET `System.Random` per stream
- **Description**: One seeded `System.Random` instance per system, held in a dictionary keyed by stream id.
- **Pros**: Zero custom code; reaches for the BCL.
- **Cons**: Microsoft's own documentation does not guarantee a seeded `Random` produces the same sequence across .NET/BCL versions — only within a given runtime's compatibility window. No public API exposes or restores its internal state; serialization would require reflection into a private field, unsupported and fragile across .NET version upgrades.
- **Rejection Reason**: Directly violates the save/checkpoint byte-identical round-trip requirement the moment the .NET runtime is upgraded — a game patched over months to years cannot bind its determinism guarantee to an unversioned BCL internal.

### Alternative D: One global stream, sequential draws across all systems
- **Description**: A single generator instance; every system draws from it in dispatch order.
- **Pros**: Simplest possible implementation — one object, no derivation or enum bookkeeping.
- **Cons**: This is exactly the failure mode systems-index #4 names outright: draw order couples to system registration/dispatch order; adding, removing, or reordering an unrelated system's draws shifts every subsequent system's stream, and one extra debug draw anywhere corrupts reproducibility project-wide.
- **Rejection Reason**: The index pre-decided against this shape; per-system isolation is the actual requirement, not an optimization on top of it.

### Alternative E: Godot's built-in `RandomNumberGenerator`
- **Description**: Use `Godot.RandomNumberGenerator` (engine-native; itself PCG32-family internally — `core/math/random_pcg.h`) per system.
- **Pros**: Zero custom implementation; the underlying algorithm choice is independently validated by this same convergence (godot-specialist review, 2026-08-07) — the engine's own designers reached for the same generator family.
- **Cons**: A `Godot.*`-namespace type, which violates the zero-Godot-dependency rule every prior Foundation ADR holds for `Hollowdeep.Core` — it would break headless testing and entangle the RNG contract with the engine. Independently of that rule: it exposes no increment/stream-selector primitive, so multiple instances would each need an independently supplied seed with no structural guarantee of stream independence — it would not satisfy the per-system-isolation requirement even if the dependency rule were waived.
- **Rejection Reason**: Fails the zero-Godot-dependency rule on its own; fails the stream-isolation requirement on a second, independent axis.

## Consequences

### Positive
- Every system that draws randomness gets an explicit, reviewable ownership-table row — the same anti-God-object discipline ADR-0002/0003 established for terrain cells and entity fields, applied to RNG.
- "Resumable at arbitrary draw counts" (ADR-0004 AC-67) is free: the entire resumable position is 16 bytes; resuming is a value copy, never a replay.
- Combat's per-encounter derivation makes a single battle deterministically reproducible from `(MasterSeed, EncounterId)` alone — a debug/regression harness can replay one fight without replaying the whole colony history.
- Zero Godot dependency, zero heap allocation per draw — headless unit tests run in plain .NET, consistent with every other Foundation ADR.
- The ADR-0001 companion amendment closes a real gap (load-window draws were previously unsanctioned) with a two-word addition, mirroring ADR-0002 rule 5's existing precedent — no new mechanism invented.

### Negative
- A hand-rolled PCG implementation is more code than reaching for `System.Random` — accepted as the minimum viable structure for a determinism guarantee that must survive years of .NET version upgrades.
- Five named streams at MVP (growing with GDD-era systems) is more bookkeeping than one shared generator — accepted: it is the direct fix for the bug class the systems index names.
- `EncounterIdSource` is new infrastructure this ADR introduces as a dependency of `SwitchTransitionData.EncounterId`, which previously had no defined allocator — a small, precedented addition (mirrors `EntityIdSource`), not scope creep, but a companion edit ADR-0001 did not originally carry.

### Risks
- **A `checked` arithmetic context throws on the core step's overflow instead of wrapping.** *Mitigation* (godot-specialist review, 2026-08-07 — assessed as the single highest-probability failure mode in this ADR, more likely than any hardware nondeterminism): the multiply-add step is wrapped in an explicit `unchecked { }` block in the implementation, never left to the project's default overflow setting; a CI test builds under both `checked` and `unchecked` project configurations and asserts identical output.
- **`PcgState` serialization reorders fields via a raw struct memcpy** (`MemoryMarshal.AsBytes`, `Marshal.StructureToPtr`) instead of writing `State`/`Increment` by name, and a future .NET/CLR upgrade silently changes default field layout, corrupting existing saves/checkpoints without a visible error. *Mitigation*: `Snapshot()`/`Restore()` serialize named fields only — stated directly in the contract (see the generator code block) — the same principle ADR-0002/0003 already apply to their own serialization contracts.
- **Stream-derivation bug produces correlated, not independent, streams.** *Mitigation*: PCG's odd-increment rule is the entire mechanism; a CI test seeds N streams from one master seed, runs a cross-correlation/collision check on the first K draws of each, and matches `NextUInt32` output against PCG32's published reference test vectors (O'Neill's paper/reference implementation) — a stronger determinism proof than self-consistency alone.
- **A system draws RNG outside the mutation window** (UI callback, `_Process`, a "just this once" debug hook). *Mitigation*: the same debug-assertion mechanism ADR-0001/0002/0003 already use; every `RngStream.Next*` asserts the window is open.
- **`EncounterId` reuse or non-monotonicity silently correlates two different battles' combat rolls.** *Mitigation*: `EncounterIdSource` is a serialized monotonic counter, never reused — the same non-reuse guarantee `EntityIdSource` already provides, and the save/load spike already validated that pattern.
- **A raw draw is converted to a float via a culture-aware or hardware-variant path** (e.g. a locale-formatted string round-trip, or FPU-rounding-mode-dependent conversion), and platform-dependent noise creeps into "deterministic" state. *Mitigation*: `NextFloat01`'s bit-construction method is specified in this ADR's contract, not left to each call site. **Downstream corollary** (godot-specialist review, 2026-08-07): the CLR has mandated strict IEEE-754 float semantics since .NET Core 3.0, so hardware rounding variance is not the live hazard — the remaining one is FMA (fused-multiply-add) instruction contraction on AVX2/FMA3-capable JIT paths, which can round a chained `roll * range + base` expression differently than separate operations. Irrelevant to the integer-only PCG core itself, but a call-site discipline note for any system (e.g. Combat) doing float math on a `NextFloat01()` result inline.
- **Cross-platform integer-arithmetic assumption unverified.** *Mitigation*: flagged in Engine Compatibility's Verification Required; C#'s unsigned integer overflow is well-defined (no undefined behavior) and pure integer ops have no analogue of the old x87-extended-precision float problem — the two risks above (`unchecked` context, struct layout) are the actual mechanism this covers, not generic hardware variance.
- **AOT export toolchains have historically made different struct-layout and FMA-usage choices than JIT.** *Mitigation*: low priority now — the PC lead SKU (Windows/Linux x86-64) publishes via standard CoreCLR, not NativeAOT. Revisit only if a future Tier 3 platform requires full AOT (godot-specialist review, 2026-08-07); the named-field-serialization fix above already neutralizes most of this risk in advance.

## GDD Requirements Addressed

| GDD Document | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| `design/gdd/systems-index.md` #4 | "Per-system seeded streams; without it, sim bugs are irreproducible and saves desync" | `RngStreamId` ownership table; PCG32 multi-stream derivation via the increment field |
| `design/gdd/time-authority-mode-switch.md` AC-67 / Rule 9b | Battle checkpoint carries combat RNG streams, resumable at arbitrary draw counts, deterministic vs. an unquit control run | Encounter-scoped stream derivation + 16-byte `Snapshot()`/`Restore()`; checkpoint-only serialization, never a colony save |
| `design/gdd/systems-index.md` CD-2 | Breach-point selection variety across battles | `RaidTrigger` persistent stream |
| `design/gdd/systems-index.md` CD-4 | Deterministic appearance seed | `ColonistIdentity` persistent stream, one draw at embark |

## Performance Implications
- **CPU**: `NextUInt32` is a handful of integer ops (multiply, xor-shift, rotate) — sub-nanosecond, immaterial against the 16.6 ms frame budget; consistent with the sub-microsecond dispatch costs already measured elsewhere in this project.
- **Memory**: 16 bytes per stream; five streams at MVP = 80 bytes. Zero per-draw allocation.
- **Load Time**: Negligible — derivation is a handful of SplitMix64 mixing steps at world creation.
- **Network**: N/A (single-player).

## Migration Plan
None — greenfield. **Companion edits at adoption** (same changeset as this ADR):
1. **ADR-0001** — two edits: (a) the RNG rule gains "…or the load window" (mirrors ADR-0002 rule 5's existing load-window carve-out for terrain mutations). (b) `SwitchTransitionData.EncounterId` **widens `int` → `long`** and gains its defined allocation source: `EncounterIdSource` — allocated by `TimeAuthorityManager` at `RequestSwitch` acceptance, monotonic, never reused, serialized by Time Authority in both colony saves and battle checkpoints (mirrors `EntityIdSource`, ADR-0003; ownership per ADR-0004 content item 4 — TD-ADR B2).
2. **ADR-0004** — content scope item 3 ("Combat RNG streams… format owned by the Seeded RNG ADR") is discharged; cross-reference this ADR. Item 3's "Serialized by: Seeded RNG owner" wording is corrected to "each stream's owning combat system" per ADR-0003's binding side-table wording (TD-ADR A6).
3. **`.claude/docs/technical-preferences.md`** — Architecture Decisions Log gains the ADR-0005 entry. Forbidden Patterns gains: an RNG stream reused across encounters or re-derived at checkpoint restore; a stream held by a system that doesn't own it in the ownership table; a raw draw converted to float via a culture-aware or platform-variant path.
4. **`design/gdd/systems-index.md`** — #4 status updates from "Not Started" to reflect this ADR's authoring.
5. **`docs/registry/architecture.yaml`** (TD-ADR A5) — the `rng_outside_authority_execution` forbidden-pattern entry gains the load-window extension and the presentation-scope clarification (`revised:` stamped, old value in a comment, per registry rules); new entries: `rng_streams` state ownership, the stream-grant interface contract, the PCG32 API decision, and the restore-never-rederives forbidden pattern.

## Validation Criteria
1. Same `MasterSeed` + same recorded inputs → byte-identical stream state and draw sequence, re-run twice in the same process and across a save/load round-trip (CI).
2. N persistent streams derived from one `MasterSeed` via `DerivePersistent` show no detectable cross-correlation over the first 10⁶ draws each (statistical smoke test — not a full randomness test-suite pass, MVP scope); `NextUInt32` output matches PCG32's published reference test vectors.
3. A combat encounter's `CombatResolution`/`CombatRaiderAi` streams, re-derived from `(MasterSeed, EncounterId)` in isolation, reproduce the exact draw sequence of that encounter as it ran embedded in a full playthrough (the "replay one fight" guarantee).
4. `Snapshot()`/`Restore()` round-trips every stream (persistent and encounter-scoped) byte-identically; a checkpoint-restored battle continues drawing identically to an unquit control run (AC-67's technical half, ADR-0004) — and the test explicitly asserts the restore path performed **zero derivations** (a re-derivation on restore is the B1 failure mode: draw counts reset to zero, determinism silently broken).
5. `RngStream.Next*` fires the mutation-window debug assertion when called outside `Tick()`/authority-driven resolution/the load window (post-companion-amendment); `Restore` fires its assertion on an even `Increment`.
6. A save → load → save cycle preserves `EncounterIdSource` such that a post-load battle receives a never-before-issued `EncounterId` (extends the save/load spike's `EntityIdSource` test to the new counter).
7. Six months in: no system holds a reference to a stream it doesn't own in the ownership table; no "misc RNG" grab-bag stream exists; no view or audio code draws from a simulation stream (`PresentationRng` exists precisely so this stays true).

## Related Decisions
- ADR-0001 Time Authority (Accepted) — companion amendment: load-window RNG legality, `EncounterId` allocation via `EncounterIdSource`
- ADR-0003 Entity Data Ownership (Accepted) — entity spawn draws (`AppearanceSeed`); `EntityIdSource` is this ADR's direct precedent for `EncounterIdSource`
- ADR-0004 Battle Checkpoint Architecture (Proposed) — content scope item 3, discharged by this ADR; AC-67 depends on it
- `design/gdd/systems-index.md` #4, CD-2, CD-4, CD-15; Gate Record CD-GDD-ALIGN M1 (reload seed policy — discharged in §Reload seed policy, design pick routed to #18)
- Save/Load quick-spec #6 — blocked on this ADR
