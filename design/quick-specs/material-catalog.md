# Material Catalog — Quick-Spec

| Field | Value |
|-------|-------|
| **Systems index** | #5 (enumeration) / #9 (design order) — Economy, MVP, Foundation layer |
| **Doc tier** | Quick-spec — ADR-0002 fixes the interface shape and the serialization contract; this spec fixes the content, the invariants and the ownership |
| **Status** | Drafted 2026-08-20 |
| **Governing ADRs** | ADR-0002 (`IMaterialCatalog` read-only dependency, runtime `ushort` ids, derived-not-stored tier, material manifest in `TerrainSnapshot`), ADR-0003 (`ItemStore` stacks carry stable material keys; `SpawnStack`/`ConsumeFromStack`), ADR-0005 (no RNG in the core outside `SeededRngStore` — the catalog draws nothing) |
| **Source evidence** | Save/load spike 24/24 (`prototypes/saveload-spike/`) — catalog evolution is safe via id remap; unknown material key fails restore loudly. No isolated catalog benchmark exists yet (see §7b) |
| **Depends on** | Nothing. Foundation layer, zero upstream systems |

---

## 1. Purpose

The Material Catalog is the single source of truth for **what materials exist and what numbers they carry**. It answers five questions and owns the answers: which materials exist, what tier each belongs to, what numbers each carries (wall HP, dig cost, build cost, dig yield), what stable key identifies it across saves and patches, and how materials are distributed across strata.

It is **content, not state**. Nothing in it changes while the game runs, and it is never serialized into a save — only the key→id manifest is, and that belongs to `TerrainSnapshot` (ADR-0002) and `ItemStore` (ADR-0003).

Its load-bearing job is an invariant, not a table. The terrain GDD hands this system the **tier-ordering invariant** explicitly, with a warning attached: *"granite held where dirt failed" only teaches if the ordering is reliable. It is currently split across two owners with nobody cross-checking direction, which is how invariants die.* This spec makes that cross-check mechanical (C3) rather than a thing a designer is trusted to remember.

**Explicitly out of scope** — named so they cannot drift in:

| Not owned here | Owner |
|---|---|
| The rules that consume the numbers — dig-progress accumulation | Excavation & Construction (#15/#16) |
| Wall HP clamping, destroy-at-zero, change events | Terrain Data Model (#1), Formulas A/B |
| Damage amount fed into `ApplyWallDamage` | Material-Tier Destructibility (#17) |
| Billing hauled materials against `AppliedAmount` (CD-7) | Repair & Rebuild (#25) |
| Item stack storage, merge/split, capacity, reservations | `ItemStore` (ADR-0003) + Stockpile & Hauling (#11) |
| Style / ornament vocabulary, and the ≤8-variants-per-tier draw-call ceiling | Art bible (CD-5) + Terrain Rendering & Cutaway (#7). The cell carries `StyleId` **separately** from its material ids — style is not a catalog axis |
| Threat scaling per stratum (the other half of "strata are data") | Raid Trigger (#18), per CD-6 |
| Which material actually sits in which cell | Map Authoring (#14) in MVP; World Gen (#35) later |
| Meshes, textures, and material assets | Terrain Rendering & Cutaway (#7) |

---

## 2. Core Rules

### C1 — The catalog is immutable after load

It is constructed once, inside the load window, and never mutated afterwards. **No writer interface exists** — not a restricted one, not a debug one. This is the property that makes §4 trivial and makes the catalog safe to read from any system, any authority, any thread, without a mutation-window assertion.

> **Why this shape.** Every other Foundation system in the project needed an ownership table because it had writers to arbitrate. This one has none, so the cheapest correct design is to make mutation unrepresentable rather than governed. A live-reload-the-catalog dev feature would break this; if it is ever wanted, it must go through a full world reload (C7), never an in-place swap.

### C2 — Stable keys are identity; runtime ids are indices

Each material has a canonical **stable string key** (`"granite"`) that is its identity forever. The runtime `ushort` id is an index into a flat array, assigned at load, and is **not stable across sessions**. Per ADR-0002, **id `0` is reserved** and means *no material* — no wall (open cell) or no floor (void).

Runtime ids may appear in cells and in memory. They may **never** be persisted except through the manifest that translates them back to stable keys.

### C3 — Tier ordering is a validated invariant, not a convention

`MaterialTier` is an ordered enum: `Dirt(0) < Granite(1) < Reinforced(2)`.

At load, the catalog **validates and fails loudly** if any tier-ordered number is non-monotonic in tier:

- `MaxWallHp` — strictly increasing by tier
- `DigCost` — strictly increasing by tier
- `BuildCost` — non-decreasing by tier
- `Value` — strictly increasing by tier

Failure is a hard load error naming the offending material and field. It is never a warning, never a clamp, never a silent reorder.

> **Why this shape.** This is a Pillar 3 (*Scars Teach*) requirement wearing a data-validation costume. If a rebalance ever makes dirt tougher than granite, the game does not become slightly mistuned — it starts teaching the player something false, and the after-action report (CD-1) becomes an active lie. A designer editing a JSON file at 1am is exactly who this catches. The terrain GDD assigned #5 the cross-check; this rule is that cross-check.

### C4 — Every material carries a complete row

No nullable numbers, no partial definitions, no "granite has no build cost" special cases. A material missing any field fails load with the same loudness as C3.

Floors are the one structural exception, and it is a **type** distinction rather than a missing-field one: floors carry no HP and no yield because MVP floors cannot be destroyed (terrain GDD MVP scope), so `FloorDef` is a genuinely smaller record than `WallDef` (§3) rather than a `WallDef` with holes in it.

### C5 — Distribution is per-stratum weights with monotonic expected value

Materials are distributed by an authored **weight table** over (stratum, material). The catalog validates two things at load:

1. **Monotonic expected value.** For naturally-occurring materials, `EV(stratum)` is **strictly increasing** with depth, where
   `EV(s) = Σ_m ( weight(s,m) / Σ_m weight(s,m) ) × Value(m)`
2. **Materials do not vanish downward.** Once a material has non-zero weight at stratum `s`, its weight stays non-zero for every stratum below `s`. Deeper strata may introduce new materials; they never delete old ones.

Rule 2 is what keeps the mountain geological rather than a layer cake — dirt still shows up deep, and the player who wants granite still has to look for it rather than being handed it by depth alone.

> **Why this shape.** CD-6 puts Pillar 5's "descent creates escalating material reward" on this system and explicitly warns it must not decay into untuned tables. A distributional rule is testable as a pure function of the weight table with no game running, which is what makes it a real acceptance criterion (AC-6) instead of a hope. Strict tier gating by stratum was rejected: it reads as gamey and collides with the art bible's *"The Wild Deepens, the Built Doesn't"* — depth should feel like geology, not like a level-select.

### C6 — Not every material is naturally occurring

A material may have **zero weight in every stratum**, meaning it is never mined and must be obtained another way (crafting, refining). Such a material is excluded from C5's EV calculation — it has no depth to be monotonic in.

This exists because the concept doc is genuinely ambiguous about whether **reinforced** is mined at depth or manufactured (`game-concept.md`: *"Construction tech scales from dirt to reinforced stone to engineered defenses"* alongside *"deeper strata hold richer materials"*). The catalog supports both readings so the question can be answered by Construction (#16) without a schema change. See §8 item 1 — this is the one open question that could move real weight around.

### C7 — Catalog evolution is safe by construction

Between patches: **adding** materials, **reordering** them, and **changing numbers** are all safe. Saves carry the key→id manifest and `Restore` remaps ids through it (ADR-0002, validated by the save/load spike).

- **Removing** a key that an existing save references fails restore loudly. Deprecate by leaving the row in place, never by deleting it.
- **Lowering `MaxWallHp`** below a saved wall's current HP is a legal rebalance. The over-max wall is *not* migrated: repair reports `AlreadyAtMax` and the anomaly decays naturally through damage (terrain GDD Formula B, decided at `/design-review` 2026-08-02).

### C8 — The catalog draws no randomness

It exposes the weight table; it never samples it. Any actual draw is made by the consuming system through its own `SeededRngStore` stream (ADR-0005) — Map Authoring at world build, World Gen (#35) later. A catalog that sampled internally would need an RNG stream of its own and would put a determinism-critical draw inside Foundation infrastructure that nothing ticks.

---

## 3. Public Interface

Plain C#, `Hollowdeep.Core.Materials`, zero Godot references.

```csharp
public enum MaterialTier : byte { Dirt = 0, Granite = 1, Reinforced = 2 }

/// Shared identity + ordering facts, valid for any material.
public readonly struct MaterialDef
{
    public readonly ushort       Id;      // runtime index; 0 = none (ADR-0002)
    public readonly MaterialTier Tier;
    public readonly int          Value;   // scalar ordering worth; drives C5's EV
}

/// Wall-role numbers. Walls have HP, are dug, and yield items.
public readonly struct WallDef
{
    public readonly MaterialDef Material;
    public readonly ushort      MaxWallHp;   // Formula B's ceiling (terrain GDD)
    public readonly int         DigCost;     // work units to clear one wall
    public readonly int         BuildCost;   // item qty consumed to build one wall
    public readonly byte        YieldQty;    // stacks spawned per wall dug (ADR-0003)
}

/// Floor-role numbers. MVP floors are indestructible: no HP, no yield.
public readonly struct FloorDef
{
    public readonly MaterialDef Material;
    public readonly int         BuildCost;
}

public interface IMaterialCatalog
{
    int Count { get; }
    int StratumCount { get; }

    WallDef  Wall(ushort id);    // ADR-0002's call site: Catalog.Wall(WallTypeId).Tier
    FloorDef Floor(ushort id);

    bool                 TryResolve(ReadOnlySpan<char> stableKey, out ushort id);
    ReadOnlySpan<char>   StableKey(ushort id);

    /// Weight table for one stratum, written into caller-supplied buffers.
    /// The catalog never samples — the caller draws with its own RNG stream (C8).
    int GetStratumWeights(int stratum, Span<ushort> ids, Span<int> weights);
}
```

Two shapes are deliberate. `Wall(id)` / `Floor(id)` keep ADR-0002's existing call site verbatim while letting the two roles carry different records (C4). `GetStratumWeights` fills caller buffers rather than returning a collection, holding the zero-steady-state-allocation standard even though it is only called at world build.

---

## 4. Behavior Under Each Time Authority

*(Mandatory — routing policy.)*

The catalog is **passive, immutable infrastructure**. It registers no `ITickable`, advances no state, holds no `Revision`, and has no writer interface. It is the simplest possible case of the ADR-0003 passive-store pattern: read-only in both authorities, identical in both.

| | **RealTime** | **TurnBased** |
|---|---|---|
| Writes | None — no write path exists (C1) | None — no write path exists (C1) |
| Reads | Legal, unrestricted, no mutation-window assertion | Legal, unrestricted, no mutation-window assertion |
| Callers | Terrain, Excavation & Construction, Repair & Rebuild, Stockpile & Hauling, Terrain Rendering, Map Authoring | Combat: Targeting & Resolution (via Destructibility's `MaxWallHp` lookups), Terrain Rendering |

**Load window** is the only phase in which the catalog is constructed (C1), and it is constructed *before* `TerrainWorld.Restore`, because `Restore` remaps the save's manifest against the current catalog and needs it already standing.

**Serialization**: the catalog is never written to a colony save or a battle checkpoint. Only the manifest is, and it is owned by `TerrainSnapshot` (ADR-0002) and by `ItemStore`'s stable keys (ADR-0003). A checkpoint written mid-battle and resumed after a patch that rebalanced the catalog resumes against the **new** numbers — correct, and the same rule colony saves already follow (C7).

---

## 5. Dependencies

**Upstream** — none. Foundation layer, zero dependencies. It is loadable and fully testable headlessly with nothing else in the project standing.

**Downstream** — Terrain Data Model (#1, hard: tier + `MaxWallHp` + stable keys); Terrain Rendering & Cutaway (#7, material→mesh mapping); Stockpile & Hauling (#11, stacks keyed by material); Map Authoring (#14, reads the weight table at world build); Excavation & Construction (#15/#16, `DigCost` / `BuildCost` / `YieldQty`); Material-Tier Destructibility (#17, tier semantics); Raid Trigger (#18, reads `StratumCount` only — threat scaling is its own); Repair & Rebuild (#25, CD-7 billing); Save/Load (#6, the manifest contract).

---

## 6. Tuning Knobs

The catalog **is** a tuning knob — its entire content is data. Values live in `assets/data/materials.json` and `assets/data/strata-distribution.json`, never hardcoded.

| Knob | Default | Range | Category | Rationale |
|---|---|---|---|---|
| `MaxWallHp` (dirt / granite / reinforced) | 100 / 300 / 800 | strictly increasing by tier (C3) | curve | Ratios matter, absolutes do not. ~3× and ~8× over dirt make a granite wall meaningfully a decision and a reinforced wall an investment |
| `DigCost` (dirt / granite / reinforced) | 40 / 120 / 320 | strictly increasing by tier (C3) | curve | Tracks HP loosely but deliberately flatter — tougher material should not be punitively slower to mine, or players stop descending |
| `BuildCost` (dirt / granite / reinforced) | 1 / 2 / 4 | non-decreasing by tier (C3) | gate | The hauling cost of a strong wall is what makes Pillar 3's rebuild loop bite (CD-7) |
| `YieldQty` | 1 / 1 / 1 | 0–4 | curve | Flat in MVP. A tier-varying yield is a second economy lever and should not be pulled before the first is tuned |
| `Value` (dirt / granite / reinforced) | 1 / 3 / 8 | strictly increasing by tier (C3) | curve | The scalar C5's EV check orders by. Mirrors the HP ratio in MVP because HP *is* what material is worth here |
| Stratum weights | authored per (stratum, material) | ≥ 0, subject to C5 | curve | The Pillar 5 lever. Constrained by C5's two validations, not free-form |
| `StratumCount` | 3 | 3 (MVP) — 6–8 at Tier 3 | gate | Concept doc MVP cap; Raid Trigger reads it for threat scaling |

**All defaults above are first-pass placeholders.** They are stated rather than omitted because CD-6's failure mode is untuned tables, and a table with no numbers cannot be checked, played, or argued with. `/balance-check` owns validating them once the loop is playable.

---

## 7. Acceptance Criteria

### (a) Headless / automated — Logic, **BLOCKING**

- [ ] **AC-1** A catalog whose `MaxWallHp`, `DigCost`, `BuildCost` or `Value` is non-monotonic in tier **fails to load**, with an error naming the offending material and field. Verified by four negative-case fixtures, one per field. *(C3)*
- [ ] **AC-2** A material row missing any required field fails to load with the same loudness. *(C4)*
- [ ] **AC-3** `TryResolve(StableKey(id)) == id` for every id in the catalog, and `TryResolve` on an unknown key returns false without throwing. *(C2)*
- [ ] **AC-4** Id `0` resolves to *no material* for both `Wall` and `Floor`, and is never assigned to an authored material. *(C2, ADR-0002)*
- [ ] **AC-5** No write path exists: `IMaterialCatalog` exposes no mutating member, and a CI grep finds no setter, no `Add`, no `Reload` on the concrete type. *(C1)*
- [ ] **AC-6** For an authored weight table, `EV(s+1) > EV(s)` strictly for every adjacent stratum pair, computed over naturally-occurring materials only; a fixture that inverts two strata fails load. *(C5, C6 — this is CD-6's mechanical form)*
- [ ] **AC-7** A material with non-zero weight at stratum `s` has non-zero weight at every stratum below `s`; a fixture that drops one to zero fails load. *(C5 rule 2)*
- [ ] **AC-8** A material with zero weight in every stratum loads successfully and is excluded from AC-6's calculation. *(C6 — the reinforced-is-crafted reading must not fail load)*
- [ ] **AC-9** `GetStratumWeights` allocates zero bytes and never draws: two calls with the same arguments return identical buffers. *(C8)*

### (b) Performance — headless benchmark, **BLOCKING regression gate**

**No isolated catalog measurement exists yet.** The terrain spike's numbers (0.17 B/mutation, 0.290 ms full-map sweep) include catalog lookups but never isolated them, so the bands below are stated as **design budgets to be replaced by measured bands at first benchmark**, not as bands above a known figure. Flagged rather than fabricated.

- [ ] **AC-10** `Wall(id)` / `Floor(id)` lookup ≤ **50 ns** and **0 B** allocated *(budget, unmeasured — ADR-0002 predicts a flat array index)*
- [ ] **AC-11** Full catalog load + all C3/C5 validations ≤ **5 ms** for the MVP table *(budget, unmeasured; one-shot load-window cost)*

### (c) Integration — **BLOCKED on siblings; does not gate this system's Done**

- [ ] **AC-12** Excavation accumulates dig progress against `DigCost` and issues `ClearWall` at exactly the threshold — blocked on #15/#16
- [ ] **AC-13** Repair bills hauled material against `AppliedAmount`, not the requested amount (CD-7) — blocked on #25
- [ ] **AC-14** A save written before a catalog rebalance restores correctly against the new catalog, and a save referencing a removed key fails loudly — **partly evidenced already** by the save/load spike's id-remap and unknown-key cases; re-verify against the production catalog when #6 lands

### (d) Advisory / playtest

- [ ] **AC-15** A player descending through the three strata reports noticing that deeper digging yields better material, **without having been told the rule**. If this needs explaining, C5's weights are too subtle regardless of what AC-6 proves. *(CD-6's qualitative half)*

---

## 8. Open Questions & Routed Items

| # | Item | Routed to | Trigger |
|---|---|---|---|
| 1 | **Is reinforced mined or manufactured?** The concept doc supports both readings (Pillar 5's "deeper strata hold richer materials" vs. "construction tech scales from dirt to reinforced stone"). C6 makes the catalog indifferent, but the answer changes whether reinforced has stratum weights at all, and whether `BuildCost` needs an input recipe rather than a scalar | Construction (#16) + creative-director | **Before Construction #16 is authored.** If manufactured, `BuildCost` becomes a recipe and this spec gains a small amendment |
| 2 | **Dig time's home.** The terrain GDD's Tuning Knobs table routes "dig time per material tier" to Excavation & Construction; this spec puts `DigCost` in the catalog (user decision 2026-08-20, so every tier-ordered number is checkable in one place per C3) while Excavation keeps the accumulation *rule*. Consistent in substance, but the terrain GDD's table now reads as if it owns the number | Terrain Data Model (#1) — one-line clarification to its Tuning Knobs table | Next `/design-review` pass on the terrain GDD, **or** before #15/#16 is authored — whichever comes first |
| 3 | **`Value` as an authored field.** MVP mirrors the HP ratio, so `Value` carries no independent information yet. It earns its place only when worth stops tracking toughness | economy-designer | When a second value axis appears — a material that is valuable but weak (ore, gems), i.e. at Tier 3 strata or the first crafting economy |
| 4 | **Style axis vs. material axis.** Style is not a catalog field (§1), but the ≤8-variants-per-tier draw-call ceiling is measured on *material × style combos co-occurring in an octant*, so the two axes multiply against one budget | Art bible palette spec + Terrain Rendering (#7) — already terrain GDD OQ#9 | The art bible's palette specification. Adding a fourth material multiplies against the same ceiling |
| 5 | **Registry entries.** `design/registry/entities.yaml` has empty `items:` and `entities:` sections. The three materials and the tier-ordering invariant are exactly the cross-boundary facts it exists to hold, and this spec is their owner | User approve-or-decline, matching the ADR-0004 registry gate | Proposed alongside this spec; not written without approval |
