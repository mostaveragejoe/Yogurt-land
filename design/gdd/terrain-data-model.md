# Terrain Data Model

> **Status**: **Approved** — `/design-review` re-review 2026-08-02 verdict **APPROVED** (creative-director synthesis), conditional on 3 blocking line-edits + 6 recommended edits, all applied same day. Prior round: NEEDS REVISION (2026-08-02), all 7 blocking + 13 recommended items applied.
> **Re-review** (2026-08-02, full mode — same panel): Formulas A/C fixes confirmed held; two new arithmetic-class defects found and fixed (Formula B widened addition, Formula D explicit `Anomalous` return); 4.7.1 verification gate given owner/date/precondition; style-variant ceiling caveated pending overlay measurement; chunk-size lock conditioned on octant-granular rebake; writer-discipline/determinism ACs tagged against Known Gap #2; stair MeshLibrary item constraint added; dormant-stair guarantee decoupled from cutaway depth (OQ#4); stranded-colonist notification obligation routed to #10/#8; OQ#9 moved to an independent track. CD upheld prior adjudications on the Gen0 hard gate and OQ#9 placement.
> **Creative Director Review (CD-GDD-ALIGN)**: REVISED 2026-07-26 — CONCERNS raised, both must-fix items resolved (stair-rule self-contradiction removed; CD-1 breach-log ownership assigned to #22). Carried items applied.
> **Independent /design-review** (2026-08-02, full mode — game-designer, systems-designer, performance-analyst, godot-specialist, qa-lead, creative-director synthesis): NEEDS REVISION. Blocking: formula input hardening (A/B), Formula C bounds order, states-table rebuild, dormant-stair visibility (CD amended own CD-GDD-ALIGN ruling), draw-call gate rescoped to measured curve (overlay map confirmed absent from the 32 figure), Godot 4.7.1 GridMap verification gate recorded, OQ#6a promoted to C11. One residual contradiction found during revision (stair-designation rejection in UI Requirements + one AC vs. C8) and corrected.
> **Author**: user + game studio agents
> **Last Updated**: 2026-08-02
> **Implements Pillar**: Pillar 1 (The Blueprint Is the Player), Pillar 5 (Depth Is the Frontier)
> **Data contract**: `docs/architecture/adr-0002-terrain-data-model.md` (spike-validated 2026-07-25;
> Proposed pending the frame-rate clause on target hardware). That ADR owns the cell record,
> chunking, mutation API, event batching and serialization. **This GDD owns the gameplay rules.**
> Where the two overlap, the ADR wins.

## Overview

The Terrain Data Model is the authoritative record of the mountain's architecture: for every cell on every Z-level, what floor lies underfoot and — separately — what wall fills the space above it. It is the surface the player's expression is written onto. Every dig a colonist completes, every wall raised, every stair sunk toward the next stratum, and every breach a raider smashes resolves to a mutation here. Because tactics combat is fought on the same grid the colony was carved from, this store *is* the battle map: there is no second representation and no conversion at the mode switch, so the cover a colonist crouches behind during a raid is the same eight bytes the player designated three hours earlier.

Everything downstream reads it. Rendering slices it into the cutaway view, pathfinding composes it with doors and occupancy into walkability, spatial query derives line of sight and cover from it, and the after-action report names the material tier that failed by reading the cell's state as captured at the moment it broke. What the model deliberately does **not** hold is anything standing in a cell: occupants, designations, zones and combat state each have their own owner (ADR-0002's firewall table). A cell describes architecture and nothing else — that restraint is what keeps the highest-fan-out system in the project from becoming a god object.

**MVP scope**: a hand-authored mountain of three strata; three material tiers (dirt → granite → reinforced); destruction is **wall HP only** — floors carry no HP and cannot be destroyed, because floor loss drops units between layers, which is collapse-adjacent and deferred to Structural Collapse (#34). Procedural generation (#35) becomes a second producer of this data later without changing the contract.

**Scope boundary**: [`docs/architecture/adr-0002-terrain-data-model.md`](../../docs/architecture/adr-0002-terrain-data-model.md) owns the *data contract* — the packed cell record, chunking, the single-write-path mutation API, batched change events, and the serialization format — and it has been spike-validated against measured budgets. This document owns the *gameplay rules*: what is diggable, what building requires, how tiers behave under damage, how stairs work as a player verb, and the formulas behind all of it. Where the two overlap, the ADR wins.

## Player Fantasy

This system has no player fantasy of its own, and that is deliberate. Nobody experiences "the terrain data model." What players experience is carving a room and watching colonists move into it, choosing granite over dirt for a wall they suspect will be tested, sinking a stair toward a stratum they have not seen, and reading a scar afterwards to learn which choice failed. Those feelings are owned by Excavation & Construction (#15/#16), Material-Tier Destructibility (#17), Repair & Rebuild (#25), and the Combat set (#19–#23).

What terrain owes those systems is the single property that makes their fantasies possible: **the thing the player shapes and the thing the player fights in are the same thing.** Pillar 1 — *The Blueprint Is the Player* — only holds if architecture has consequences the player can trace, and that traceability is a data property before it is a design one. A model that stored the colony one way and the battlefield another would make every downstream fantasy a translation problem, and the seams would show exactly where the player is most invested.

So the fantasy this system *protects* rather than delivers is: **the player's decisions persist, and they persist as the same object that later judges them.** The design consequence is a discipline, not a feature — terrain must remain legible, mutable through one path, and free of anything that is not architecture.

Three promises in this document are terrain's own content in Pillars 1 and 3, and are named here rather than left in Detailed Design so a future reviewer cannot trade them away as implementation detail:

1. **No hidden gate.** The plan the player draws is never silently refused for a reason they cannot see. *(That sentence is the durable pillar promise. The stronger MVP rule — every wall diggable by any colonist, no skill gate at all — is C2's, and is recorded there as named scope debt pending Skill & Veterancy (#30), not as a pillar commitment; see the review note in C2.)*
2. **Nothing is silently lost.** Displacement never buries a colonist or destroys an item stack, and when displacement fails the player is told rather than left wondering why a job vanished (Edge Cases).
3. **The failed choice survives its own failure.** `TerrainChange.Previous` captures the material tier at the moment of the breach — the literal data floor under Pillar 3 (*Scars Teach*).

> *Authoring note*: this section was drafted without the `creative-director` consult that full review mode normally requires, because it was scoped as a downstream pointer rather than an authored fantasy. **The CD-GDD-ALIGN gate (2026-07-26) reviewed it and upheld the framing** — a data-substrate GDD that invented a fantasy would create a second source of truth competing with #15/#16/#17/#25 — while directing that the three promises above be named explicitly rather than left implicit in Detailed Design.

## Detailed Design

### Core Rules

**C1 — What a cell is.** Every in-bounds cell on every Z-level independently carries a floor and a wall. Four meaningful combinations: *solid rock* (floor + wall), *open* (floor, no wall — walkable), *stair* (a Z-linking floor, no wall), and *void* (neither — impassable). Terrain owns exactly these facts and nothing else.

**C2 — Diggability.** Any cell with a wall is a legal dig target for any colonist. **No skill gate, no tool gate, no per-colonist eligibility check.** Material tier changes how *long* a dig takes, never whether it is permitted. *Reasoning: gating legality would create a hard dependency on Skill & Veterancy (#30), which is Vertical Slice, not MVP — and a plan silently rejected by a hidden prerequisite violates Pillar 1.* There is no indestructible bedrock tier; the map edge is `WorldBounds`, not a fourth material.

> **Named scope debt (SD-C2, per /design-review 2026-08-02):** "no skill gate at all" is an MVP scope decision, not a pillar requirement. The durable Pillar 1 promise is only that a refusal is never *invisible* — #30 may later gate speed or even eligibility, provided any gate is legible to the player at designation time (OQ#5). This split protects #30's design space from being pre-committed by a document with no authority over it.

**C3 — Work positions and reachability.** To dig or build at target `T`, a colonist must stand at a work position `W` that is one of the **8 cells surrounding `T` on the same Z-level**, is passable, and is reachable via composite walkability (terrain + doors + occupancy, per Pathfinding). **The corner-cutting ban governs how the colonist travels to `W`, not which neighbours may serve as `W`.** Working a cell is reaching sideways from walkable ground, not stepping through rock — these are deliberately two different rules, stated explicitly so neither is later "fixed" into the other.

**C4 — Dig progress is not terrain state.** A dig accumulates progress in a **sparse side table keyed by `CellCoord`, owned by Excavation & Construction** — never a cell field (ADR-0002's firewall: a cell describes architecture, not plans). The wall's `WallHp` is untouched while mining. When progress reaches the material's dig cost, Excavation issues `ClearWall` and the cell becomes open in one step. **Cancelling a dig discards the progress entry; the wall is left exactly as it was.**

**C5 — Mining and combat damage are independent.** Combat reduces `WallHp`; mining accumulates dig progress. In MVP a combat-damaged wall does **not** dig faster — the two are orthogonal. *(Flagged as a tuning opportunity: "breached walls are quicker to clear" is a plausible later rule, deliberately not taken now.)* If combat destroys a wall that is being mined, the `WallRemoved` event invalidates the dig job and its progress entry is dropped.

**C6 — Building.** A wall may be raised at any in-bounds cell with no wall, from a valid work position; floor presence at the target is irrelevant, so retaining walls on a chasm rim are legal. A floor may be laid at any void cell from a valid work position. **There is no hard support rule.** Because the work position must already be reachable, disconnected floating rooms are structurally impossible for free — while cell-by-cell cantilevered bridges over open space remain possible. **Unlimited unsupported spurs are accepted scope debt, not a feature** *(reframed per /design-review 2026-08-02: consequence-free geometry is the absence of a rule, not a Pillar 1 expression — Pillars 2 and 3 both promise construction choices are tested and failures teach, and free cantilevers train a mental model Structural Collapse (#34) will later contradict)*. The player-trust consequence across the #34 patch boundary is tracked in OQ#7.

**C7 — Floors are authored, not created.** Map Authoring guarantees every excavatable cell carries a floor at load time. Digging never needs to add one, and the cell becomes walkable the instant the wall clears. Consequently **voids exist only where the map author placed them** (chasms, ravines, the open sky above the mountain) — nothing at runtime turns a floored cell into a void.

**C8 — Stairs.** *(Wording corrected 2026-07-26 after QA review — see the enforcement note at the end of this rule.)* The player designates **Dig Stairs Down** at `(x,y,z)`. Preconditions, checked at designation *and* re-checked at execution: the target is a diggable wall, reachable, unclaimed, and `(x,y,z+1)` is in-bounds and **solid**. On completion Excavation submits one atomic batch: `SetFloor(stair)` at `Z` plus `ClearWall` at `Z+1`. Because the cell below was solid immediately beforehand, **nothing can be standing in it** — opening a stair reveals only virgin rock, so there is no displacement case and no combat edge case. **The stair floor is permanent; the passage through it is not.** No verb reverts a stair back to solid rock — the Z-linkage, once dug, is a permanent record of that decision. But a stair cell is otherwise an ordinary cell with no wall, so **a wall may be built on it or on its landing below, and dug out again later** (C2, C6). Sealing a stairwell is therefore a legitimate defensive option *and* the player's correction for a stairwell sunk in the wrong place; the linkage survives dormant underneath. **A dormant linkage must never be hidden state** *(added per /design-review 2026-08-02, amending CD-GDD-ALIGN)*: a wall over a stair floor is distinguishable from solid rock in the data (`IsStairFloor` is retained) and must be distinguishable to the player — Blueprint / Designation UI (#26) renders a persistent indicator on any cell whose floor carries `IsStairFloor` regardless of wall presence, and the cell's inspect view names the dormant linkage (see UI Requirements). Without this, the player who sealed a mis-sited stair — the exact correction path this rule sanctions — is left with an invisible landmine under their floor plan, breaching promise #1 and Pillar 3. Stairs are **down-only** in MVP. Combat cannot dig stairs — **enforced at runtime** by the writer-per-authority table's mode assertion, independent of CD-10. *Note: not yet enforced at compile time. ADR-0003 records the writer-interface segregation as a carried obligation that the production implementation must still deliver; until it does, this is a debug-assertion guarantee rather than an unrepresentable-state one.*

**C9 — Claims.** One terrain-modifying job may claim a cell at a time. **Latest designation wins**: a new designation cancels and replaces the pending one, with no rollback — a build that never completed left the cell untouched, and a cancelled dig simply discards progress (C4).

**C10 — Designation invalidation.** Each job type has exactly one precondition, and the existing change-event contract invalidates it — including mid-battle, since paused systems still receive events:

| Job | Precondition | Falsified by |
|---|---|---|
| Dig wall / dig stairs | wall present at target | `WallRemoved` |
| Build wall | no wall at target | `WallPlaced` |
| Build floor | no floor at target | `FloorPlaced` |
| Repair | wall present and below max HP | `WallRemoved` |

Reachability loss is **not** terrain's concern — `IsPassableTerrain` is a per-cell fact; "the path to this cell is gone" belongs to Job Assignment and Pathfinding.

**C11 — Map-authoring connectivity constraint** *(promoted from OQ#6a per /design-review 2026-08-02 — its own text said "not deferrable" while sitting in a table headed "nothing here blocks implementation")*. With stairs down-only (C8), **no authored region may be reachable only from below** — such a region would be permanently unconnectable at runtime. This is a hard validity constraint on every map Map Authoring / Content Load (#14) produces, checked before the first map is authored and testable per map (see Acceptance Criteria). It binds World / Mountain Generation (#35) identically when it becomes the second producer.

### States and Transitions

*(Table rebuilt per /design-review 2026-08-02: states are Formula D's `CellState` enum exactly — `WallHp` is an **attribute within `SolidRock`**, not a state, so "damaged" rows are attribute changes; the `ClearFloor` row was missing; and the old `Stair | terminal` row contradicted the Edge Case permitting wall-on-stair.)*

| From | To | Cause | Notes |
|---|---|---|---|
| SolidRock | SolidRock (`WallHp` reduced) | `ApplyWallDamage` (Combat, TurnBased) | Attribute change, not a state change; floor unaffected |
| SolidRock (`WallHp` < max) | SolidRock (`WallHp` raised) | `ApplyWallRepair` (Repair, RealTime) | CD-7; consumes hauled materials; **never lowers HP** (Formula B) |
| SolidRock | Open | `ApplyWallDamage` reaching 0 HP | Reports `WallRemoved`; `Previous` captures the failed tier for CD-1 |
| SolidRock | Open | `ClearWall` on dig completion (Excavation, RealTime) | Dig progress entry discarded |
| SolidRock | Stair | Atomic `SetFloor(stair)` @Z + `ClearWall` @Z+1 | One batch, one event (C8) |
| Open | SolidRock | `SetWall` (Construction, RealTime) | HP set to catalog max for the tier |
| Stair | SolidRock | `SetWall` (Construction, RealTime) | Sealing a stairwell (C8, Edge Cases); the `IsStairFloor` floor is retained beneath — linkage dormant and **player-visible** (C8) |
| SolidRock (stair floor) | Stair | `ClearWall` (dig, Excavation) or `ApplyWallDamage` to 0 | Re-opening a sealed stairwell |
| Void | Open | `SetFloor` (Construction, RealTime) | The only path that removes a void |
| Open / Stair | Void | `ClearFloor` — **debug/authoring tooling only** | In ADR-0002's API surface; **no MVP gameplay verb invokes it** (C7 guarantees digging never voids a cell; OQ#8 closed floor-drops out of terrain). Listed so the implementation surface and this table agree |

**What "stairs are permanent" now means precisely**: no verb converts a *stair floor* back to a plain floor or removes it (outside debug `ClearFloor`) — the Z-linkage attribute is terminal even though the cell's *state* is not: a stair cell can become SolidRock (sealed) and back again, with the linkage surviving throughout.

**Damage is visualised at three discrete levels** (intact / damaged / critical). The count is fixed here because it multiplies MeshLibrary size: GridMap holds one item id per cell and offers no per-instance shader channel, so the rendering spec should implement damage as a **third stacked overlay map** (~2 MB video memory, ~2 µs per threshold crossing) rather than by multiplying wall items across tier × style × damage. *(Disambiguation: the three visual damage states and Formula A's three-ish-valued `Outcome` enum are unrelated taxonomies that coincidentally share a cardinality — one is HP-threshold banding for rendering, the other is a per-call event outcome. Do not conflate them in implementation.)*

### Interactions with Other Systems

| System | Reads | Writes | Interface owner |
|---|---|---|---|
| Terrain Rendering & Cutaway (#7) | chunk cells, change batches | — | Rendering |
| Pathfinding & Navigation (#8) | `IsPassableTerrain`, change batches | — | Pathfinding composes |
| Spatial Query / LOS & Cover (#12) | wall presence per cell | — | Spatial Query |
| Map Authoring / Content Load (#14) | — | full population inside the load window | Map Authoring |
| Excavation & Construction (#15/#16) | cell state, claim bit | `ClearWall`, `SetWall`, `SetFloor`, claim bit | Excavation |
| Material-Tier Destructibility (#17) | tier via catalog | `ApplyWallDamage` | Destructibility |
| Repair & Rebuild (#25) | `WallHp` vs catalog max | `ApplyWallRepair` | Repair |
| Combat: Targeting & Resolution (#22) | wall presence, tier | `ApplyWallDamage` (TurnBased only) | Combat |
| Job Assignment & Priority (#10) | change batches (never cell state) | — | Job Assignment owns the claim who/why table; bound by the **structural invariant** *claim bit set ≡ key present in its table* (ADR-0002 debug invariant) — added per /design-review 2026-08-02: a mutual-consistency coupling tighter than event subscription must be visible where writers are declared, not only in Dependencies |
| Material Catalog (#5) | — | — | Catalog owns tier, max HP and stable keys |

**Behavior under each time authority** (mandatory, cross-cutting contract #1): terrain is a **passive store — it never ticks**. It registers with no time authority and has no `Tick()`. Its behaviour is identical in both modes; only the legal writer set changes.

| Authority | Legal terrain writers |
|---|---|
| RealTime | Excavation, Construction, Repair & Rebuild |
| TurnBased | Combat: Targeting & Resolution **only** |
| Outside both (load window) | Map Authoring, `Restore` |

## Formulas

**Ownership principle: Terrain owns the function, never the arguments.** `ApplyWallDamage(cell, amount)` — Terrain owns what happens to `amount` once it arrives (the clamp, the destroy-at-zero transition, the event), never where `amount` comes from. Consequently **this section declares no balance numbers**. Doing so would also contradict this document's own Interactions table, which assigns tier and max HP to the Material Catalog (#5). All HP figures in the worked examples below are illustrative placeholders.

### Formula A — Wall Damage Application

`RemainingHp = max(WallHp − DamageAmount, 0)`
`AppliedAmount = WallHp − RemainingHp`

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Wall HP before | `WallHp` (H₀) | ushort | 0 to `MaxHp(tier)` — the max is Material Catalog's (#5) value | Current wall HP immediately before the call |
| Damage requested | `DamageAmount` (D) | int | ≥ 0 (negative is a caller error) | Damage points requested; the *magnitude* is Destructibility's (#17) / Combat's (#22) to compute |
| Wall HP after | `RemainingHp` (H₁) | ushort | 0 to H₀ | Stored HP after the call; 0 means the wall is removed |
| Applied amount | `AppliedAmount` (Δ) | int | 0 to H₀ | HP actually removed after clamping |
| Outcome | — | enum {NoWall, Damaged, Destroyed, RejectedInput} | set | NoWall if no wall before the call; Destroyed if H₁ = 0; RejectedInput if D < 0; Damaged otherwise |

**Defensive contract** *(added per /design-review 2026-08-02)*: `D < 0` is **rejected, not clamped** — debug builds fire an assertion (the same pattern as the mutation-window and writer-discipline asserts); production performs **no state change, publishes nothing**, and returns outcome `RejectedInput` with Δ = 0. The rejection happens *before* any arithmetic, and the subtraction is specified in widened (long) arithmetic, so no overflow path exists: without this, a large-magnitude negative `D` overflows `int`, wraps negative, clamps to 0 — and a caller bug *silently destroys the wall*, the worst available failure in the store that produces CD-1's forensic evidence.

**Output Range:** H₁ ∈ [0, H₀] — monotonically decreasing, floor-clamped at 0 regardless of how large D is. If no wall is present the call is a no-op and publishes nothing. `D = 0` is legal, not rejected: outcome `Damaged` with Δ = 0, and **publishes nothing** — no state changed, and a zero-delta batch would be noise every subscriber must special-case *(specified at re-review 2026-08-02; previously silent)*. At H₁ = 0 the wall is removed atomically in the same call and the batch reports `WallRemoved`, with `Previous` capturing the failed tier for CD-1.

**`AppliedAmount`, not `DamageAmount`, is the figure the after-action report should cite** — a 250-damage hit on a 180 HP wall did 180 damage, not 250.

**Example** (HP values illustrative):
- Granite wall H₀ = 180, D = 60 → H₁ = 120, Δ = 60, Damaged. Publishes `WallDamaged`.
- Granite wall H₀ = 180, D = 250 → H₁ = 0 (clamped, not −70), Δ = 180 (not 250), Destroyed. Publishes `WallRemoved`.
- Open cell, D = 60 → NoWall, Δ = 0, no state change, publishes nothing.

### Formula B — Wall Repair Application

If `WallHp ≥ MaxHp(tier)`: `RemainingHp = WallHp` (outcome `AlreadyAtMax` — see the rebalance note below)
Otherwise: `RemainingHp = min(WallHp + RepairAmount, MaxHp(tier))`
`AppliedAmount = RemainingHp − WallHp`

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Wall HP before | `WallHp` (H₀) | ushort | 0 to `MaxHp(tier)` — **may exceed it after a catalog rebalance** (see the rebalance note; the range row must admit the case the same formula handles) | Current wall HP immediately before the call |
| Repair requested | `RepairAmount` (R) | int | ≥ 0 (negative is a caller error) | HP points requested; the rate per colonist-hour and its hauled-material cost are Repair & Rebuild's (#25, CD-7) to compute |
| Tier ceiling | `MaxHp(tier)` (H_max) | ushort | tier-specific, Material Catalog's (#5) | Looked up via the catalog; never stored per cell |
| Wall HP after | `RemainingHp` (H₁) | ushort | H₀ to H_max | Stored HP after the call |
| Applied amount | `AppliedAmount` (Δ) | int | 0 to max(H_max − H₀, 0) | HP actually restored after clamping |
| Outcome | — | enum {NoWall, AlreadyAtMax, Repaired, RejectedInput} | set | NoWall if no wall; AlreadyAtMax if H₀ ≥ H_max; RejectedInput if R < 0; Repaired otherwise |

**Defensive contract** *(added per /design-review 2026-08-02, symmetric with Formula A; completed at re-review 2026-08-02)*: `R < 0` is **rejected, not clamped** — debug assertion; production performs no state change, publishes nothing, returns `RejectedInput` with Δ = 0. Rejection happens before any arithmetic or cast, so the previously possible failure — a negative R wrapping through the `ushort` store into massive overheal — is unreachable. **The addition `H₀ + R` is specified in widened (long) arithmetic** — the same word-for-word requirement as Formula A's subtraction *(re-review finding: the first revision added only the sign guard, leaving `65535 + int.MaxValue` free to wrap through `int` into a negative sum that `min` would then select, breaking the monotonicity guarantee below)* — so no overflow path exists for any legal `R ≥ 0`. The stated output range below is a *guarantee*, not an assumption about callers.

**Catalog-rebalance note**: if a Material Catalog rebalance lowers `MaxHp(tier)` below a saved wall's H₀, then `H₀ > H_max` at call time. **Repair never lowers HP**: the call is `AlreadyAtMax`, HP untouched (decided at /design-review 2026-08-02 over load-window normalization — no data migration, no silently weakened player walls after a patch; over-max walls decay naturally through damage and the anomaly self-heals).

**Output Range:** H₁ ∈ [H₀, max(H₀, H_max)] — monotonically non-decreasing **unconditionally**, ceiling-clamped at the tier's catalog max whenever H₀ ≤ H_max. Repair can never overheal or bank HP above the wall's own tier ceiling, and cannot manufacture a wall that is not there (that is Construction's `SetWall`, a distinct verb). If already at max the call is a no-op regardless of R.

**`AppliedAmount` is the figure CD-7's material consumption should bill for** — repairing the last 20 points of a 100-point request costs 20 points' worth of material, not 100.

**Example** (HP values illustrative):
- Granite wall H₀ = 120, H_max = 300, R = 100 → H₁ = 220, Δ = 100, Repaired.
- Granite wall H₀ = 280, R = 100 → H₁ = 300 (clamped), Δ = 20, Repaired.
- Granite wall H₀ = 300, R = 50 → Δ = 0, AlreadyAtMax, publishes nothing.

### Formula C — Work-Position Adjacency (formalises C3)

`IsAdjacentSameLayer(W, T) = (max(|Wx − Tx|, |Wy − Ty|) = 1) ∧ (Wz = Tz)`

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Target cell | `T` | CellCoord | in-bounds | The dig / build / repair target |
| Candidate work position | `W` | CellCoord | in-bounds | Cell being tested as a legal place to stand while working `T` |
| Horizontal offsets | Δx, Δy | int | unbounded as inputs | \|Wx − Tx\|, \|Wy − Ty\| |
| Result | — | bool | {true, false} | True iff `W` is one of the 8 cells surrounding `T` on the same layer |

**Output Range:** Boolean. Exactly 8 cells satisfy it for any `T` — the full Moore neighbourhood minus the centre, all on `T`'s own Z-level.

This test is **diagonal-inclusive** and purely geometric. It deliberately does **not** encode the corner-cutting ban (which governs travel *to* `W` and is Pathfinding's), and does **not** encode passability or reachability — Terrain contributes `IsPassableTerrain(W)` as a separate fact, ANDed in afterwards. Formalised because "the 8 cells surrounding T" in prose invites Excavation, Construction and Repair to reimplement "surrounding" three different ways.

**Bounds filtering order** *(added per /design-review 2026-08-02)*: candidate work positions are enumerated as the 8 offsets around `T`, and **any candidate outside `WorldBounds` is discarded before any cell read** — `IsPassableTerrain` is never called on an out-of-bounds coordinate, because ADR-0002 specifies that single-cell reads throw on OOB. This matters because edge digs are *routine*, not exceptional: C2's own acceptance criterion guarantees world-edge cells are ordinary diggable walls, so a target at `x = 0` simply has ≤ 5 legal work positions, never a throw path. The evaluation order is: geometric adjacency (this formula) → bounds filter → `IsPassableTerrain` → composite reachability (Pathfinding's).

**Example:** T = (10, 10, 3).
- W = (9, 9, 3) → true (diagonal; legal even if both flanking orthogonal cells are walls, since this tests only W's relation to T).
- W = (10, 9, 3) → true.
- W = (10, 10, 3) → false (the target itself).
- W = (12, 10, 3) → false (two cells away).
- W = (10, 10, 4) → false (different layer, matching X/Y).

### Formula D — Cell State Classification and Passability (formalises C1)

`CellState(C)` =
- **SolidRock** if `Floor ≠ 0 ∧ Wall ≠ 0`
- **Stair** if `Floor ≠ 0 ∧ Wall = 0 ∧ IsStairFloor(Floor)`
- **Open** if `Floor ≠ 0 ∧ Wall = 0 ∧ ¬IsStairFloor(Floor)`
- **Void** if `Floor = 0 ∧ Wall = 0`
- **Anomalous** if `Floor = 0 ∧ Wall ≠ 0` — *(added at re-review 2026-08-02)* an explicit fifth return, never reachable in normal play (C7), debug-asserted when produced

`IsPassableTerrain(C) = Floor(C) ≠ 0 ∧ Wall(C) = 0` — equivalently, `CellState(C) ∈ {Open, Stair}`.

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Cell | `C` | CellCoord | in-bounds | The cell being classified |
| Floor field | `Floor(C)` | ushort | 0 (none) or a catalog floor id | Terrain's stored floor field |
| Wall field | `Wall(C)` | ushort | 0 (none) or a catalog wall id | Terrain's stored wall field |
| Stair-ness | `IsStairFloor` | bool (catalog-derived) | set | True iff the catalog entry for that floor declares Z-linkage; never stored per cell |
| Classification | `CellState` | enum {SolidRock, Open, Stair, Void, Anomalous} | 5 values, 4 reachable in normal play | C1's four meaningful combinations plus the explicit anomaly value |
| Passability | `IsPassableTerrain` | bool | {true, false} | Terrain's sole contribution to walkability; Pathfinding composes it with doors and occupancy |

**Output Range:** the four states named in C1, plus `Anomalous` for the one combination that **should not occur in normal play**: `Wall ≠ 0 ∧ Floor = 0` (a wall with no floor beneath it), which C7 prevents by guaranteeing every excavatable cell is authored with a floor. *(Hardened at re-review 2026-08-02: the previous text claimed the formula "degrades safely" but defined no return for this branch — an implementation falling through to `default(CellState)` would have silently reported `SolidRock`, misclassifying the anomaly as the most normal-looking state for every consumer that reads `CellState` directly — inspect UI, save/load, rendering — rather than `IsPassableTerrain`. The same failure class as the round-one negative-damage bug: hostile input becoming a plausible state.)* Should debug tooling produce the combination anyway, `CellState` returns `Anomalous` and fires a debug assertion; `IsPassableTerrain` still evaluates false — a guarantee that holds independently of the classification, since its own formula (`Floor ≠ 0 ∧ Wall = 0`) fails on either conjunct.

**Example:**
- Floor = granite, Wall = granite → SolidRock, passable = false.
- Floor = rock floor, Wall = 0 → Open, passable = true.
- Floor = stair, Wall = 0 → Stair, passable = true.
- Floor = 0, Wall = 0 → Void, passable = false.
- Floor = 0, Wall = granite (debug only) → `Anomalous` (explicit, asserted), passable = false.

### Formulas deliberately NOT written here

This table is part of the specification. Each row is a decision this document declines to make, so that the system which owns the balance makes it with the context to do so.

| Not written here | Owner | Why Terrain has no basis to write it |
|------------------|-------|--------------------------------------|
| Dig time per material tier | **Number**: Material Catalog (#5) as `DigCost` *(corrected 2026-08-24 — user decision; this row previously assigned the number to Excavation)*. **Rule**: Excavation & Construction (#15/#16) owns the progress accumulation that consumes it | Terrain never sees dig progress (C4 — it lives in Excavation's side table) and stores no tier field. The number moved to the catalog so that **every tier-ordered value sits in one load-validated table** — the same split this table already applies to wall max HP (catalog owns the number, the consuming system owns the rule) |
| Wall max HP per tier | Material Catalog (#5) | Already assigned by this document's Interactions table; Terrain derives tier by lookup, never sets it |
| Combat damage amounts (the `D` fed into Formula A) | Destructibility (#17) / Combat: Targeting (#22) | Terrain consumes the number; it holds no weapon, armour or resolution data |
| Repair rate and material cost (the `R` fed into Formula B) | Repair & Rebuild (#25), per CD-7 | Terrain consumes the number; hauled-material accounting is Repair's |
| Tier ordering invariant (dirt < granite < reinforced) | **Material Catalog (#5) owns it as a pillar-level constraint** — **discharged 2026-08-24** | Terrain never compares tiers — it has no tier field. **The invariant is a Pillar 3 requirement**: "granite held where dirt failed" only teaches if the ordering is reliable. **This row's warning — "split across two owners with nobody cross-checking direction, which is how invariants die" — is now resolved.** Both halves live in the catalog (HP *and* dig cost, per the corrected row above), and the Material Catalog quick-spec C3 makes the cross-check a **hard load failure** naming the offending material and field, with AC-1 as its BLOCKING test. Registered as `material_tier_ordering` in `design/registry/entities.yaml`, so `/consistency-check` can verify it |
| Dig-completion threshold (`progress ≥ dig cost`) | Excavation & Construction (#15/#16) | *(Corrected 2026-08-24)* The operands now live in two places — `progress` in Excavation's side table, `dig cost` in the Material Catalog (#5) — but the **comparison and the completion decision stay Excavation's**. Terrain's only guarantee is unchanged: the atomic `ClearWall` on completion (C4) |
| Cell ↔ chunk coordinate mapping | ADR-0002 (data contract) | Pure implementation with zero gameplay content; restating it would create a second source of truth |
| Damage-visualisation HP breakpoints | Terrain Rendering & Cutaway (#7) — **decided 2026-08-24**: damaged below 0.66, critical below 0.33 of `MaxWallHp`, load-validated as ordered | The *count* (3 levels) is fixed in Detailed Design; where the breakpoints fall is an art / tech-art call |

## Edge Cases

### Displacement rule (referenced below)

When a terrain change forces a colonist or an item stack out of a cell, the destination is the **nearest free cell**, found by scanning candidate offsets in fixed ascending (Z, Y, X) order at expanding radius; first match wins. Deterministic by construction, and deliberately **the same rule ADR-0003 fixes for pre-switch placement normalization** — the game has one displacement algorithm, not two that drift apart.

### Cases

- **If a wall completes in a cell a colonist occupies**: the wall is placed and the colonist is displaced per the rule above. *Blocking the job instead would let a single idling colonist stall construction indefinitely; the brief visual of being moved out of rock is the cheaper cost.* **Presentation requirement transmitted to #7** *(routing made explicit per /design-review 2026-08-02 — previously an unfunded mandate)*: per Pillar 4's Design Test B (visible over abstracted), displacement should read as a shove or step-out, not a teleport — a colonist popping out of rock is an ant-farm legibility loss. **Terrain Rendering & Cutaway (#7) owns the acceptance criterion for this**; its spec must carry one, and `/consistency-check` should flag it if absent.

- **If a repair is called on a wall whose HP exceeds the tier's current catalog max** (possible after a catalog rebalance lowered `MaxHp(tier)` under a saved wall): the call is `AlreadyAtMax` and HP is untouched — **repair never lowers HP** (Formula B's catalog-rebalance note). No load-time normalization occurs; the over-max wall decays naturally through damage.

- **If a wall completes in a cell holding an item stack**: the stack is displaced per the same rule. Nothing is destroyed and nothing is buried — the player never silently loses resources.

- **If displacement finds no free cell within the scan radius**: the build job is cancelled and the player is notified. *Failure is explicit rather than a silent no-op or an entity trapped inside rock.*

- **If a wall is built on a stair cell or on the landing below it**: permitted, like any other cell with no wall. The cell becomes impassable and the Z-linkage lies dormant until the wall is dug out. *No special case exists here deliberately — an earlier draft forbade it to protect C8's "permanence", which was circular reasoning that also contradicted C6, since the landing below a stair was already an ordinary buildable cell.*

- **If sealing a stairwell strands colonists below it**: that is the player's business and it is recoverable — any colonist can dig any wall (C2) from an adjacent cell on the same layer (C3), and a stranded colonist is by definition adjacent to the seal. *This is ordinary colony-sim consequence, not an anti-pillar concern: the "no unrecoverable full-colony permadeath" anti-pillar governs loss the **game** imposes with no way back, not a mistake the player makes and can undo.* **But "recoverable in principle" is not the same as "signalled"** *(added at re-review 2026-08-02)*: detection that a colonist has become unreachable is a **reachability** fact — C10 already establishes that reachability is Job Assignment's and Pathfinding's concern, not terrain's — so **Job Assignment / Pathfinding (#10/#8) carry a named obligation to surface a colonist-unreachable notification through the shared Notifications component**, symmetric with the displacement-failure notification below. Without it, a colonist stranded and starving behind a seal the player forgot is a silent loss, breaching promise #2. `/consistency-check` should flag #10/#8 specs that omit it.

- **If a save is loaded while digs are in flight**: designations, terrain-job claims, and **in-flight dig progress all persist** — a half-dug wall resumes exactly where it was. Dig progress is authoritative work state, not derived state, so it is serialized by Excavation & Construction (#15/#16) alongside its designation table and must round-trip under the serialization contract's CI gate. *This keeps the claim bit and its progress entry consistent across a save by construction, so ADR-0002's claim invariant (bit set ≡ key present in Job Assignment's table) needs no special load-time handling.*

- **If combat destroys a cell that is designated or claimed**: the `WallRemoved` event invalidates the job immediately, **including mid-battle while the colony is paused** (C10, and cross-cutting contract #1 — paused systems still receive events). No post-encounter rescan of designations is required.

- **If a bulk batch contains one invalid entry**: the entire batch is rejected — nothing is applied, nothing is published, and the first offending index is reported (ADR-0002 rule 3). Validate-all-then-apply means there is never a partial application to roll back.

- **If a stair is designated on the bottom layer**: rejected at designation time, since C8 requires `(x, y, z+1)` to be in bounds.

- **If two designations target the same cell**: the latest wins, replacing the pending one (C9). A cancelled dig discards its progress entry; a build that never completed leaves the cell exactly as it was.

- **If repair is applied to a wall already at full HP, or damage to a cell with no wall**: both are no-ops that publish nothing (Formulas A and B). Neither is an error condition.

- **If a cell is a void**: it is impassable, pathfinding never routes through it, and no MVP mechanic displaces an entity into one. Voids exist only where the map author placed them (C7).

- **If the cutaway window's lower boundary cuts through a stair landing or a void column**: the treatment is owned by Terrain Rendering & Cutaway (#7). Recorded here as a known boundary condition, deliberately not decided in this document.

## Dependencies

**Definitions used here** — the labels are chosen so they change a decision rather than describe a vibe:

- **Hard**: the system *consumes terrain data* — it reads cell state or writes through the facade. Remove Terrain and it computes nothing. It cannot be meaningfully built or unit-tested before Terrain exists.
- **Soft**: the system does **not** read cell state. It couples through change events and shared coordinates only, and can be built and unit-tested against a stub.

### Upstream

Terrain is a Foundation system with **no upstream simulation dependencies** — it is the substrate everything else reads. It has exactly one:

| Depends on | Nature | Interface |
|---|---|---|
| Material Catalog (#5) | **Hard**, read-only | Terrain stores runtime `ushort` type ids and queries the catalog for tier, max HP and stable string keys. Terrain never writes the catalog and never stores tier per cell. Without it, Formula B has no ceiling to clamp against. |

### Downstream

| System | Nature | Interface |
|---|---|---|
| Terrain Rendering & Cutaway (#7) | **Hard** | Bulk-reads chunk cells; subscribes to change batches; owns all render data and the damage-overlay map |
| Pathfinding & Navigation (#8) | **Hard** | Reads `IsPassableTerrain` (Formula D) and composes it with doors and occupancy; invalidates caches on change batches |
| Spatial Query / LOS & Cover (#12) | **Hard** | Reads wall presence per cell to derive sightlines and cover |
| Map Authoring / Content Load (#14) | **Hard** — *producer* | The only writer in the load window; guarantees C7 (every excavatable cell carries a floor) |
| Excavation & Construction (#15/#16) | **Hard** | Writes `ClearWall`, `SetWall`, `SetFloor` and the claim bit; owns the dig-progress side table (C4) and its serialization |
| Material-Tier Destructibility (#17) | **Hard** | Supplies the `DamageAmount` consumed by Formula A |
| Repair & Rebuild (#25) | **Hard** | Supplies the `RepairAmount` consumed by Formula B; bills hauled materials against `AppliedAmount` per CD-7 |
| Combat: Targeting & Resolution (#22) | **Hard** | The **only** legal TurnBased terrain writer |
| Job Assignment & Priority (#10) | **Soft** | Never reads cell state. Subscribes to change batches to invalidate orphaned jobs (C10), and owns the claim who/why table. **One structural coupling worth naming**: ADR-0002's debug invariant requires *claim bit set ≡ key present in Job Assignment's table* — a mutual consistency requirement tighter than pure event subscription, even though neither system stops functioning without the other |
| Stockpile & Hauling (#11) | **Soft** | Never reads cell state. Zone membership is its own sparse cell-set data and stack reservations are `EntityId`-keyed — the ADR's firewall keeps both out of Terrain. Reacts to the stack-displacement edge case as an event consumer |
| World / Mountain Generation (#35, Alpha) | **Hard** — *second producer* | Replaces hand-authored maps behind the same contract, with no change to it |

**Bidirectional obligation**: every system above must name Terrain Data Model in its own Dependencies section. `/consistency-check` should flag any that does not.

**Cross-document obligation from QA review**: C4, C5 and C9 make claims about Excavation's dig-progress side table that this document cannot certify on its own. Excavation & Construction (#15/#16) must carry the matching acceptance criteria — *on `WallRemoved`, discard the progress entry for that cell* and *dig progress round-trips through save/load* — before those rules can be considered fully covered by tests.

## Tuning Knobs

| Knob | Value | Safe range | What breaks outside it |
|---|---|---|---|
| Chunk size | **32** | **Locked**¹ | Must equal GridMap `cell_octant_size`. At 64 one octant spans four chunks, so a single-chunk batch forces rebuilds of three untouched chunks. Never change alone — chunk size and octant size move together or not at all (ADR-0002). ¹*Locked **given octant-granular rebake** — the premise the 4.7.1 verification gate (Visual/Audio Requirements, item 2) still has to confirm; if 4.6/4.7 introduced partial-octant rebake, the lock's rationale is re-derived, not assumed (re-review 2026-08-02).* |
| Style variants per tier | **≤ 8, pending overlay-map measurement (Known Gap #7)** | 1–8 | Measured: 1 → 32 draw calls, 2 → 48, 4 → 80, 8 → 144, 16 → 272, against a ≤150 terrain budget. Draw calls scale with distinct material/style combos **co-occurring in an octant**, not with MeshLibrary size. Above 8, terrain alone breaks the budget before entities, VFX or UI are drawn. **Caveat (re-review 2026-08-02): 144 at 8 variants leaves 6 calls of headroom, and the mandatory damage-overlay map's draw-call cost is unmeasured — the ceiling may drop below 8 once the overlay is measured.** |
| Damage visual states | **3** (intact / damaged / critical) | 3–4 | A direct multiplier on the damage-overlay MeshLibrary — but **the floor of 3 is a Pillar 3 legibility requirement, not a memory one**: at 2 states, "damaged" and "critical" collapse into one and the player can no longer read how close a wall is to failing, which is the lesson *Scars Teach* depends on. The HP breakpoints between states are Terrain Rendering's (#7) to set. |
| Displacement scan radius | **8** | 4–16 | Below 4, displacement fails often in tight colonies and cancels builds that should have succeeded. Above 16, a failed search gets expensive and an entity can be flung implausibly far from where it stood. |
| MVP world bounds | **128 × 128 × 16** | up to 256 × 256 × 32 | 2 MB of cell data at MVP, 16 MB at the full-vision ceiling. Terrain is not the memory risk; the binding constraint at the ceiling is save-write time (~0.4–0.7 s), not capacity. **Interaction with style variants** *(stated explicitly per /design-review 2026-08-02)*: the two knobs are independent for draw calls **because draw calls scale with style combos co-occurring per octant, and only visible-cutaway octants render** — growing the world adds octants, not styles-per-octant, so bounds × variants does not compound the draw-call budget. What growing the world *does* scale is **video memory for octant mesh data**, which the 14.25–16.42 MB figures measured only at MVP bounds — re-measure VRAM before adopting the 256 × 256 × 32 ceiling. |
| Cutaway depth | 3 layers | — | **Owned by Terrain Rendering (#7)**, listed here only because it interacts with the stair/void boundary edge case. |

Every value above **except displacement scan radius and damage-state count is measured, not estimated** — sourced from `prototypes/terrain-spike/SPIKE-NOTE.md` and the budgets in `.claude/docs/technical-preferences.md`. The scan radius is inherited from ADR-0003's placement-normalization rule rather than independently derived; reusing its number is deliberate, so the game has one displacement constant rather than two that drift.

## Visual/Audio Requirements

**This system produces no visuals or audio directly.** It is a plain-C# data layer with zero Godot dependency; everything the player sees of the terrain is produced by **Terrain Rendering & Cutaway (#7)** reading this model, against the direction in `design/art/art-bible.md` §1.

Four constraints this document imposes on that spec, recorded here so they are not rediscovered later:

- **Damage must be visible in three discrete states** (intact / damaged / critical) — the count is fixed by this document because it multiplies MeshLibrary size; the HP breakpoints between states are #7's to choose.
- **Damage cannot be a per-cell shader parameter.** GridMap holds one item id per cell and offers no per-instance data channel. The recommended implementation is a third stacked overlay map (~2 MB video memory, ~2 µs per threshold crossing), not a multiplication of wall items across tier × style × damage.
- **Style variety is capped at ~8 variants per tier** by the draw-call budget (measured). If the art direction wants more, that is a budget conversation involving chunk and octant size together, not a rendering-side tweak.
- **The floor MeshLibrary needs a distinguishable stair item** *(added at re-review 2026-08-02)*: `IsStairFloor` is a real, catalog-derived, player-visible attribute (C8, Formula D), so a visible stair floor must render as its own item, distinct from every plain-floor style — and dormant stairs additionally need the #26/#7 indicator treatment (UI Requirements). **Budget accounting: the stair item is a distinct item and therefore contributes to per-octant style-combo scaling like any other** — it is functional, not exempt. It consumes draw-call budget wherever a stair co-occurs with other combos in an octant; #7's spec must count it when re-running the style-variety curve, not discover it at measurement.

**Pre-render-backend verification gate (Godot 4.7.1)** *(added per /design-review 2026-08-02, godot-specialist finding)*: two load-bearing claims in this section rest on pre-4.4 GridMap knowledge, in a version range (4.4–4.7) the project's own `docs/engine-reference/godot/VERSION.md` flags as a HIGH knowledge-gap area. Before any render-backend implementation begins: **(1)** verify against the 4.7.1 `GridMap` class reference that GridMap still offers no per-instance data channel — this claim is the *sole* justification for the third-overlay-map damage design, and if 4.7 added per-instance custom data the overlay may not be the cheapest option; **(2)** verify that octant rebake is still octant-granular — the locked `cell_octant_size == ChunkSize` invariant in `.claude/docs/technical-preferences.md` degrades from invariant to heuristic if 4.6/4.7 introduced partial-octant rebake; **(3)** author the missing `docs/engine-reference/godot/modules/gridmap.md` so these claims have a version-pinned source of truth; **(4)** measure the overlay map's own *draw-call* cost — the ~2 µs figure quoted here is CPU mutation cost, not draw calls, and the overlay inherits the same per-octant-per-style-combo scaling as the wall/floor maps. **This gate blocks the render backend only** — the plain-C# terrain core has zero Godot dependency (CI-enforced) and proceeds in parallel. **Enforcement** *(added at re-review 2026-08-02 — a gate with no owner, date, or checkable precondition is advisory, not blocking)*: the gate is owned by **technical-director**, resolved alongside OQ#1's target-hardware run, and its checkable precondition is that **every render-backend story carries an acceptance criterion referencing `docs/engine-reference/godot/modules/gridmap.md` and the verification date recorded in it** — `/story-readiness` must return BLOCKED for any render-backend story while that file does not exist.

**CD-1's after-action report — what terrain does and does not guarantee.** CD-1 requires four facts: what broke, where, which material tier failed, **and what breached first**. Terrain guarantees only that `TerrainChange.Previous` carries the tier *at the instant of publish* (Formula A). It guarantees nothing afterwards: per ADR-0002 rule 9 the bus has no replay and terrain keeps no journal, and `TerrainChangeBatch` is a `ref struct` whose retention is a compile error. **Terrain therefore cannot satisfy CD-1's ordering requirement, and no subscriber named in the Interactions table records a breach log.**

**The encounter-era breach log is owned by Combat: Targeting & Resolution (#22)** — the sole TurnBased terrain writer, which already holds both the `DamageResult` return values (the source of `AppliedAmount`, which lives on the return value and *not* in `TerrainChange`) and the cell coordinates. It should route that ordered log to Combat UI (#27) via ADR-0003's `EncounterOutcomeReport`, the one-slot inbox already drained by `PostEncounterReconcile`. **Flag for technical-director**: that report currently carries participant outcomes only; extending it to carry an ordered terrain breach list is a schema change to an Accepted ADR's contract.

Presenting the report — including any transient highlight of the broken cell in the 3D view — belongs to Combat UI (#27).

## UI Requirements

**This system has no UI of its own.** The player never interacts with the terrain model directly — they issue designations, and the owning systems execute them.

- Designation and blueprint interaction: **Blueprint / Designation UI (#26)**, which must expose *Dig Wall*, *Dig Stairs Down*, *Build Wall*, *Build Floor* and *Cancel*. *(Correction 2026-08-02: an earlier draft of this bullet said #26 "must reject a wall designation on a stair cell" — that contradicted C8/Edge Cases, which explicitly permit walling a stair cell. Removed.)*
- **Dormant stair linkage visibility (#26 + #7)** *(added per /design-review 2026-08-02, C8)*: #26 renders a persistent indicator on any cell whose floor carries `IsStairFloor`, wall present or not, and the cell inspect view names the dormant Z-linkage; #7 carries the matching treatment in the 3D view. The data already exists — only the surfacing is required.
- Notifications this document requires the shared Notifications component to surface: **a build cancelled because displacement found no free cell** — the one place a terrain rule fails visibly and the player must be told rather than left wondering why a job vanished. *(Testability note: the acceptance criterion asserts the published Notifications **event and payload**, not "player-visible" — visibility is the component's job.)* A second notification — **colonist unreachable** (e.g. stranded behind a sealed stairwell) — is required by the sealing edge case but is **not terrain's to publish**: reachability is Job Assignment's / Pathfinding's (#10/#8), and the obligation is recorded there (Edge Cases; added at re-review 2026-08-02).

## Acceptance Criteria

Story type for this system is **Logic** (packed-struct data model, pure formulas, deterministic state machine), with several criteria that are properly **Integration** because they span Terrain plus a consuming system. Per the project's test-evidence table: Logic → `tests/unit/terrain/`, BLOCKING; Integration → `tests/integration/terrain/`, BLOCKING; performance → `tests/performance/terrain/`, BLOCKING (these are gates in ADR-0002, not advisory figures).

### Core rules

- **GIVEN** any wall-bearing cell of any material tier, **WHEN** a dig is designated by any colonist regardless of skill state, **THEN** the designation is accepted — no eligibility check rejects it and rejection never depends on colonist identity. *(C2)*
- **GIVEN** a cell at the world edge, **WHEN** queried, **THEN** it behaves as a normal diggable wall, never as an indestructible bedrock tier. *(C2)*
- **GIVEN** target `T = (10,10,3)`, **WHEN** each of the 8 Moore-neighbourhood cells is tested, **THEN** all 8 return true, and `T` itself, any cell two or more away, and any cell at `Z ≠ 3` return false. *(C3 / Formula C)*
- **GIVEN** a dig target at `x = 0` (or any world edge/corner), **WHEN** work positions are enumerated, **THEN** out-of-bounds candidates are discarded before any cell read — no OOB read throws, and the target has fewer than 8 but more than zero candidate positions. *(C3 / Formula C bounds-filtering order)*
- **GIVEN** a candidate `W` that is a void or otherwise impassable, **WHEN** `IsAdjacentSameLayer(W, T)` is evaluated, **THEN** it returns true — the geometric test is independent of passability, which is ANDed in afterwards as a separate fact. *(Formula C's negative space, locked so a future dev cannot "fix" the two rules into one)*
- **GIVEN** a diagonal work position whose two flanking orthogonal cells are both walls, **WHEN** adjacency is evaluated, **THEN** it still returns true — the geometric test is independent of corner-cutting legality. *(C3)*
- **GIVEN** a wall cell with a dig in progress, **WHEN** `GetCell` is queried mid-dig, **THEN** `WallHp` is unchanged and **no** `TerrainChange` has published. *(C4)*
- **GIVEN** a wall mid-dig, **WHEN** `ApplyWallDamage` is called against it, **THEN** HP decreases per Formula A and dig progress is unaffected — Terrain exposes no coupling field between them. *(C5)*
- **GIVEN** an in-bounds cell with no wall and **no floor**, **WHEN** `SetWall` is called from a valid work position, **THEN** the wall is placed — floor state at the target is irrelevant. *(C6)*
- **GIVEN** a freshly loaded map, **WHEN** every excavatable cell is queried, **THEN** each carries a non-zero floor. *(C7 — Integration, **blocked on Map Authoring #14**)*
- **GIVEN** any authored map, **WHEN** region connectivity is analysed with stairs down-only, **THEN** no region is reachable only from below. *(C11 — Integration, **blocked on Map Authoring #14**; a per-map validity check, not a one-time test)*
- **GIVEN** a solid-rock cell whose wall clears by dig completion, **WHEN** `ClearWall` executes, **THEN** the cell is walkable in the same call, with no separate floor-add step, and no cell ever becomes `Void` as a result of a dig. *(C7)*
- **GIVEN** a diggable, reachable, unclaimed wall at `(x,y,z)` with `(x,y,z+1)` in bounds and solid, **WHEN** Dig Stairs Down completes, **THEN** exactly **one** atomic batch publishes both `SetFloor(stair)@Z` and `ClearWall@Z+1`. *(C8)*
- **GIVEN** a stair designation whose precondition is falsified between designation and execution, **WHEN** execution is attempted, **THEN** it is rejected — verifying the precondition is checked **twice**, not only at designation. *(C8)*
- **GIVEN** a cell with a pending dig, **WHEN** a build is designated on the same cell, **THEN** the dig is cancelled with its progress discarded, the wall is untouched, and the claim transfers. *(C9 — Integration, **blocked on Excavation & Construction #15/#16**)*
- **GIVEN** a cell with a pending build, **WHEN** a dig is designated on the same cell, **THEN** the build is cancelled with the cell left exactly as it was — no rollback needed because nothing was applied — and the claim transfers. *(C9's symmetric case, added per /design-review 2026-08-02)*
- **GIVEN** each of the four job types with its precondition, **WHEN** the corresponding falsifying event publishes, **THEN** the pending job is invalidated — all four rows of C10's table as four discrete cases. *(C10 — Integration, **blocked on Job Assignment #10**)*
- **GIVEN** the colony is paused during combat, **WHEN** Combat destroys a designated cell, **THEN** the invalidating event still fires and is still received — paused is not event-deaf. *(C10 — Integration, **blocked on Job Assignment #10 + Combat #22**)*

### Formulas

- **GIVEN** `WallHp = 180, D = 60`, **WHEN** damage applies, **THEN** `RemainingHp = 120`, `AppliedAmount = 60`, outcome `Damaged`, one `WallDamaged` batch.
- **GIVEN** `WallHp = 180, D = 250` (overkill), **WHEN** damage applies, **THEN** `RemainingHp = 0` (never negative), **`AppliedAmount = 180`, not 250**, outcome `Destroyed`, and `WallRemoved` publishes with `Previous` carrying the pre-destruction tier. *A test asserting only `RemainingHp == 0` does **not** cover Formula A — the `AppliedAmount` assertion is the load-bearing one (CD-1 cites this figure).*
- **GIVEN** an open cell, **WHEN** damage applies with any `D ≥ 0`, **THEN** outcome `NoWall`, `AppliedAmount = 0`, and **zero batches publish** — the subscriber receives no call at all, not an empty batch.
- **GIVEN** `WallHp = 280, H_max = 300, R = 100` (overheal), **WHEN** repair applies, **THEN** `RemainingHp = 300` and **`AppliedAmount = 20`, not 100** — the exact figure CD-7 bills materials against.
- **GIVEN** `WallHp = H_max`, **WHEN** repair applies with any `R`, **THEN** outcome `AlreadyAtMax`, `AppliedAmount = 0`, zero batches publish.
- **GIVEN** any wall and `D < 0` (including `int.MinValue`, the overflow-magnitude case), **WHEN** damage is called, **THEN** outcome `RejectedInput`, HP unchanged, zero batches publish, and the debug assertion fires in debug builds — **the wall is never destroyed or healed by a negative input**. *(Formula A defensive contract)*
- **GIVEN** any wall and `R < 0` (including `int.MinValue`), **WHEN** repair is called, **THEN** outcome `RejectedInput`, HP unchanged, zero batches publish — no wraparound overheal is reachable. *(Formula B defensive contract)*
- **GIVEN** a damaged wall with `WallHp` near `ushort.MaxValue` and `R = int.MaxValue` (the additive-overflow-magnitude case), **WHEN** repair is called, **THEN** `RemainingHp = MaxHp(tier)` exactly — the widened addition never wraps negative and `min` never selects a wrapped sum. *(Formula B widened arithmetic; added at re-review 2026-08-02)*
- **GIVEN** a wall with `WallHp > MaxHp(tier)` after a catalog rebalance lowered the tier max, **WHEN** repair applies with any `R ≥ 0`, **THEN** outcome `AlreadyAtMax` and HP is **unchanged — never lowered**. *(Formula B catalog-rebalance note)*
- **GIVEN** a damaged wall (`0 < WallHp < H_max`), **WHEN** repair applies with `R ≥ H_max − WallHp`, **THEN** `RemainingHp = H_max` and the cell remains SolidRock throughout — the full Damaged→restored transition, not only the clamp arithmetic. *(States table row 2)*
- **GIVEN** each of the four (floor, wall) combinations, **WHEN** classified, **THEN** state and passability match Formula D exactly, including the `IsStairFloor` branch separating `Open` from `Stair`.
- **GIVEN** the anomalous wall-without-floor combination, **WHEN** passability is evaluated, **THEN** it returns false **without throwing**; **and WHEN** `CellState` is evaluated, **THEN** it returns **`Anomalous` — never `SolidRock` or any other normal state** — and the debug assertion fires in debug builds. *(Formula D; hardened at re-review 2026-08-02 — a test asserting only passability does not cover the classification path that inspect UI, save/load and rendering read.)*

### Edge cases

- **GIVEN** a colonist occupying a cell, **WHEN** a wall build completes there, **THEN** the colonist is displaced per the scan rule and the build proceeds — not blocked, not cancelled. *(Integration)*
- **GIVEN** an item stack occupying a cell, **WHEN** a wall build completes there, **THEN** the stack is displaced with count and identity unchanged — nothing destroyed, nothing buried. *(Integration)*
- **GIVEN** a build target with no free cell anywhere in the scan radius, **WHEN** completion is attempted, **THEN** the job is cancelled and a **Notifications event of the build-cancelled-displacement-failed type is published carrying the cancelled cell's coordinate** — asserted as a concrete published event with payload, not as "player-visible" (visibility is the Notifications component's job). *(Integration, **blocked on Excavation & Construction #15/#16 + Notifications**; rewritten per /design-review 2026-08-02)*
- **GIVEN** a stair cell, **WHEN** a wall is designated on it, **THEN** the designation is **accepted** (C6, Edge Cases — sealing a stairwell is legal), and on completion the cell is SolidRock with its `IsStairFloor` floor retained and queryable. *(Corrected 2026-08-02: the previous version of this criterion required rejection, contradicting the CD-GDD-ALIGN stair decision this document itself records.)*
- **GIVEN** a wall built on a stair cell, **WHEN** the designation layer is queried, **THEN** the cell reports its dormant stair linkage — the flag #26's indicator and inspect view bind to. *(C8 visibility requirement)*
- **GIVEN** a bulk `Apply` whose entry at index `k` is invalid, **WHEN** it executes, **THEN** `BulkResult` reports `k`, **zero** of the N mutations apply, and **zero** batches publish — verified by a post-call sweep showing nothing changed anywhere in the batch, not merely at `k`.
- **GIVEN** a displacement search where a geometrically nearer candidate is a void, **WHEN** it resolves, **THEN** the void is skipped in favour of the next passable candidate.

### Writer discipline and determinism

- **GIVEN** TurnBased is active, **WHEN** Excavation, Construction or Repair attempt any write, **THEN** the assertion fires and the write does not apply. *(Blocked on Known Gap #2's mode-forcing test hook.)*
- **GIVEN** RealTime is active, **WHEN** Combat: Targeting attempts `ApplyWallDamage`, **THEN** the assertion fires and the write does not apply. *(Blocked on Known Gap #2's mode-forcing test hook.)*
- **GIVEN** the mutation window is closed, **WHEN** a legal writer for the active mode calls a write method from a UI-callback context, **THEN** the mutation-window assertion fires — a guarantee distinct from the authority check above, and tested independently. *(Blocked on Known Gap #2's window-state test hook.)*
- **GIVEN** a fixed displacement scenario, **WHEN** it runs N times from fresh state with identical input, **THEN** the destination cell is identical every run. Include a case where a wrong tie-break axis would select a different candidate — **otherwise the test is a tautology rather than a falsification.**
- **GIVEN** identical mutation sequences applied to two freshly constructed worlds — **the sequence must include at least one displacement, one destroy-at-zero, and one repair-to-max**, so the test exercises every order-sensitive path rather than passing trivially on sequences that never diverge — **WHEN** both complete, **THEN** `Snapshot()` output is byte-identical **and** the published change streams match in content and order. *(ADR-0002 rule 8; de-tautologised per /design-review 2026-08-02, mirroring the wrong-tie-break-axis requirement on the displacement test above. **Blocked on Known Gap #2**: driving the mutation sequence requires the mode-forcing and window-state hooks — zero Godot dependency does not make this runnable until they exist. Tagged at re-review for consistency with the doc's own blocked-AC convention.)*
- **GIVEN** test code that retains a `TerrainChangeBatch` beyond the `Publish` call, **WHEN** it is compiled, **THEN** compilation **fails** — the `ref struct` retention guarantee is verified by a compile-fail test, not a runtime assertion. *(ADR-0002 rule 9; AC added per /design-review 2026-08-02 — previously asserted in prose with no criterion)*
- **GIVEN** a dig at partial progress, **WHEN** the world is snapshotted and restored, **THEN** the claim bit round-trips intact. *(Terrain's half is testable now; the full "resumes exactly where it was" claim is Integration, blocked on Excavation's serialization — see Dependencies.)*

### Performance

Thresholds are **tolerance-banded above the measured value** so CI catches genuine regressions without flaking on hardware and measurement noise. *(Gates revised per /design-review 2026-08-02.)*

| Criterion | Measured | Gate |
|---|---|---|
| Full-map walkability sweep, MVP world | 0.290 ms | **≤ 0.5 ms** |
| Allocation over 60,000 mutations | 0.17 B/mutation, 0 Gen0 | **≤ 0.5 B/mutation and exactly 0 Gen0** (tightened from ≤1 B — 6× headroom let a 5× regression pass silently, contradicting the "regressions are bugs" standard. "Exactly 0 Gen0" is deliberately a hard binary: a Gen0 collection in this path is a genuine regression or a mis-measuring harness — **fix the harness** (force a collection before the measured window; assert on allocated bytes if collection count proves noisy), never loosen the gate) |
| Terrain draw calls, 3-layer cutaway, **wall + floor maps only** | curve below | **Gate is the measured style-variety curve, not a single pin**: 1 style/tier → 32, 2 → 48, 4 → 80, 8 → 144; ceiling ≤ 150 at every point. The old "exactly 32" pin measured a configuration the shipping game won't run (one style per tier) and would fail on legitimate style increases inside the knob's own safe range. **Re-baseline procedure**: any style-count change re-runs the curve measurement and updates this table *and* re-verifies the whole-frame 500-call ceiling — a baseline change is a reviewed edit to this document, never a silent constant bump. **Scope caveat**: the measured curve covers the wall+floor maps only — the mandated damage-overlay third map was *not* present during measurement (verified against `SPIKE-NOTE.md` 2026-08-02); its cost is a Known Gap below |
| Concentrated AoE, 75 cells in one octant | 21.5 µs | **≤ 30 µs** |
| Multi-octant aggregate rebuild (multi-front raid: simultaneous breaches across ≥ 4 octants in one frame) | **unmeasured** | **Gate TBD at measurement** — added per /design-review 2026-08-02: the 75-cell single-octant figure is not a validated worst case; multi-front raids are a *designed* Pillar 2 scenario. Measure before the render backend is accepted; until then this row is an obligation, not a pass |

- **GIVEN** the terrain assembly, **WHEN** CI greps it for Godot references, **THEN** it finds none. *(ADR-0002 validation criterion 1)*

### Known gaps — not certifiable today

1. **60 fps on target hardware** — **split status** *(clarified per /design-review 2026-08-02)*: **non-blocking for the plain-C# terrain core** (zero Godot dependency, fully unit-testable with no GPU), but **blocking for ADR-0002's promotion to Accepted and for any claim that the render backend is validated**. The spike ran on software Vulkan (3–4 fps, no signal); every measured number in this document is CPU-side — nothing has exercised GPU fill-rate on three stacked GridMaps. No headless suite can verify this, and none should be allowed to imply it has.
2. **Two test hooks may not exist yet**: forcing `TimeAuthorityManager` into a given mode and window state without a full tick, and the debug-console sweep for *claim bit ≡ Job Assignment's table*. Confirm both before marking the writer-discipline criteria ready.
3. **The spike's render-matches-model check** (15,763 cells verified against `TerrainWorld`) currently lives in prototype code. Promoting it to a standing regression test means reimplementing it in `tests/integration/terrain/` — prototype code is never migrated.
4. **Cross-system criteria** for C4, C5 and C9 certify only Terrain's half until Excavation & Construction carries its matching criteria.
5. **CD-1's breach-*ordering* requirement has no certified owner-side implementation** *(added per /design-review 2026-08-02)*: this document states plainly that Terrain cannot satisfy it, and the fix — extending ADR-0003's `EncounterOutcomeReport` to carry an ordered terrain breach list — is an unresolved schema change to an Accepted ADR, still flagged to technical-director. Until that lands, CD-1 is only three-quarters covered.
6. **C8's combat-cannot-dig-stairs guarantee is runtime-only**: ADR-0003's compile-time writer-interface segregation is a carried obligation not yet built; until the production implementation delivers it, this is a debug-assertion guarantee, not an unrepresentable-state one.
7. **The damage-overlay map's draw-call and render cost is unmeasured** (the ~2 µs figure is CPU mutation cost) — and the two GridMap claims underpinning the overlay design need verification against Godot 4.7.1 before render-backend work begins (see the pre-render-backend verification gate in Visual/Audio Requirements).

## Open Questions

Each item names an owner and the point at which it should be resolved. Nothing here blocks implementation of this system.

| # | Question | Owner | Resolve by |
|---|---|---|---|
| 1 | ~~**60 fps on target hardware.**~~ **CLOSED 2026-08-24.** Measured on an RTX 3060 Ti (Godot 4.7.2 mono, 1800-frame window at 8 digs/frame): frame-time **p99 2.167 ms Vulkan / 2.024 ms D3D12** against the 16.6 ms budget — ~8× headroom — with **0 Gen0/Gen1/Gen2 collections** and draw calls at exactly the predicted 32. Evidence: `production/qa/evidence/terrain-target-hardware-2026-08-24/`. The original spike ran on software Vulkan (3–4 fps — no signal), which is why this row existed. **ADR-0002 was promoted to Accepted on this run.** | technical-director | Done. Remaining render-backend gate is the sparse damage-overlay draw-call measurement (quick-spec AC-10 / TR-terrain-044), not frame rate. |
| 2 | ~~May a wall seal a stairwell?~~ **CLOSED 2026-07-26 (user decision, CD-GDD-ALIGN).** Yes. The stair floor is permanent; the passage is not. The prohibition was circular reasoning and contradicted C6 anyway. See C8 and Edge Cases. | — | Closed. |
| 3 | **Should a combat-damaged wall be quicker to mine?** C5 keeps dig progress and wall HP independent in MVP. "Breached walls clear faster" is a plausible later rule with obvious player logic behind it. | Excavation & Construction (#15/#16) | A tuning pass once dig times exist. |
| 4 | ~~**Cutaway boundary treatment**~~ — **CLOSED 2026-08-24**: read as darkness; the window depth stays uniform and is never extended at stair cells (a ragged silhouette would make draw-call count depend on map content). Safe precisely because of the constraint below — see `design/quick-specs/terrain-rendering-cutaway.md` C5. Original question: read it as darkness, or extend the window by one layer at stair cells? **Constraint recorded at re-review 2026-08-02: whatever #7 decides, the cutaway cannot be the *sole* dormant-stair-visibility guarantee** — a stair sealed below the cutaway depth may be invisible in the 3D view, so C8's promise is carried by the #26 designation-layer indicator and the inspect view, which do not depend on cutaway depth. #7's treatment supplements them; it never substitutes for them. | Terrain Rendering & Cutaway (#7) | That spec. Routed there by user decision. |
| 5 | **Should material tier ever gate *possibility* of digging, not just speed?** C2 says no for MVP, because gating would depend on Skill & Veterancy (#30), which is Vertical Slice. Revisit when that system lands — it changes what "reinforced" *means* to the player. | game-designer | Vertical Slice, with #30. |
| 6a | ~~Map-authoring constraint~~ **CLOSED 2026-08-02 (/design-review): promoted to rule C11** — a "not deferrable" hard constraint had no business in a table headed "nothing here blocks implementation". Now stated in Detailed Design with a per-map acceptance criterion. | — | Closed — see C11. |
| 6b | **Are up-stairs worth building as a mechanic?** Symmetric to Dig Stairs Down and cheap, but low value on a top-down, hand-authored, 3-strata mountain. Deferred, not rejected. | Excavation & Construction (#15/#16) | When a concrete use case appears. |
| 7 | **Unlimited cantilevers → player trust across the #34 patch boundary** *(merged with former OQ#10 per /design-review 2026-08-02)*. C6 permits arbitrarily long unsupported spurs — accepted scope debt, **no longer framed as a Pillar 1 feature**. The headline risk is trust, not data: every hour a player internalises "geometry is free" is mental-model debt Structural Collapse will contradict, and the correction will read as the game changing its mind, not as a lesson (Pillar 3 inverted). Save-compat (grandfathering old geometry vs. applying rules only to new cells) is one *consequence* of this, not the framing. #34's design brief must open with the trust problem. | Structural Collapse (#34) | Alpha, at #34's design — and #34's brief must address migration of player expectations, not only saves. |
| 8 | ~~Demolishable floors as a tactical mechanic~~ **CLOSED 2026-07-26 by the creative-director at CD-GDD-ALIGN.** Under CD-11, a floor that reverts to void is an *autonomous* trap unless a colonist spends an action — and CD-11 prefers player-activated pre-built objects. So the shape is already decided: a floor-drop is a **pre-built drop-gate activated by a colonist action**, making it a door-like entity in ADR-0003's domain, **not a terrain mutation**. Routed to Construction (#16) + Combat: Action Economy (#20) as a CD-11 instance. *Consequence for this document: terrain stays permanently free of gameplay floor removal, hardening C7.* | #16 + #20 | Closed here. |
| 9 | **Style-variety ceiling of ~8 per tier is a Pillar 1 constraint, not a rendering tweak.** The measured limit is on distinct style combos co-occurring in one 32×32 octant — roughly one room. CD-5 keeps MVP safe at one vocabulary, but the art bible commits to two vocabularies plus ornament sets at Vertical Slice, all available everywhere. *A player building a show-off great hall that mixes Roman stonework and Bavarian trim across three materials is exactly the Pillar 1 behaviour the game wants — and is the case that breaks the budget.* Raising the ceiling means revisiting chunk size and octant size **together**, never octant alone. | art-director + technical-director | **The art bible's palette specification, before any kit-of-parts authoring** — by the art/lighting pass the variant count is sunk cost. **Independent track, not sequenced behind OQ#1** *(re-ordered at re-review 2026-08-02: the palette spec plausibly lands before target hardware exists, and OQ#1 is non-blocking for the core)* — resolve by the art bible's palette spec, whenever that arrives. |
| 10 | ~~Retroactive invalidation by #34~~ **MERGED into OQ#7 (2026-08-02, /design-review)** — same underlying issue; keeping two entries invited resolving the save-compat half while leaving the trust half unowned. | — | See OQ#7. |
