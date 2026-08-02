# Terrain Data Model

> **Status**: In Design
> **Author**: user + game studio agents
> **Last Updated**: 2026-07-26
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

So the fantasy this system *protects* rather than delivers is: **the player's decisions persist, and they persist as the same object that later judges them.** The design consequence is a discipline, not a feature — terrain must remain legible, mutable through one path, and free of anything that is not architecture. Every rule in this document exists to keep that promise cheap to honour.

> *Authoring note*: `creative-director` was not consulted for this section (full review mode would normally require it) because the section was scoped as a downstream pointer rather than an authored fantasy. Pillar alignment is still checked by the CD-GDD-ALIGN gate over the finished document.

## Detailed Design

### Core Rules

**C1 — What a cell is.** Every in-bounds cell on every Z-level independently carries a floor and a wall. Four meaningful combinations: *solid rock* (floor + wall), *open* (floor, no wall — walkable), *stair* (a Z-linking floor, no wall), and *void* (neither — impassable). Terrain owns exactly these facts and nothing else.

**C2 — Diggability.** Any cell with a wall is a legal dig target for any colonist. **No skill gate, no tool gate, no per-colonist eligibility check.** Material tier changes how *long* a dig takes, never whether it is permitted. *Reasoning: gating legality would create a hard dependency on Skill & Veterancy (#30), which is Vertical Slice, not MVP — and a plan silently rejected by a hidden prerequisite violates Pillar 1.* There is no indestructible bedrock tier; the map edge is `WorldBounds`, not a fourth material.

**C3 — Work positions and reachability.** To dig or build at target `T`, a colonist must stand at a work position `W` that is one of the **8 cells surrounding `T` on the same Z-level**, is passable, and is reachable via composite walkability (terrain + doors + occupancy, per Pathfinding). **The corner-cutting ban governs how the colonist travels to `W`, not which neighbours may serve as `W`.** Working a cell is reaching sideways from walkable ground, not stepping through rock — these are deliberately two different rules, stated explicitly so neither is later "fixed" into the other.

**C4 — Dig progress is not terrain state.** A dig accumulates progress in a **sparse side table keyed by `CellCoord`, owned by Excavation & Construction** — never a cell field (ADR-0002's firewall: a cell describes architecture, not plans). The wall's `WallHp` is untouched while mining. When progress reaches the material's dig cost, Excavation issues `ClearWall` and the cell becomes open in one step. **Cancelling a dig discards the progress entry; the wall is left exactly as it was.**

**C5 — Mining and combat damage are independent.** Combat reduces `WallHp`; mining accumulates dig progress. In MVP a combat-damaged wall does **not** dig faster — the two are orthogonal. *(Flagged as a tuning opportunity: "breached walls are quicker to clear" is a plausible later rule, deliberately not taken now.)* If combat destroys a wall that is being mined, the `WallRemoved` event invalidates the dig job and its progress entry is dropped.

**C6 — Building.** A wall may be raised at any in-bounds cell with no wall, from a valid work position; floor presence at the target is irrelevant, so retaining walls on a chasm rim are legal. A floor may be laid at any void cell from a valid work position. **There is no hard support rule.** Because the work position must already be reachable, disconnected floating rooms are structurally impossible for free — while cell-by-cell cantilevered bridges over open space remain possible, which is a Pillar 1 expression feature. Unlimited unsupported spurs are accepted scope debt until Structural Collapse (#34) retrofits real support rules.

**C7 — Floors are authored, not created.** Map Authoring guarantees every excavatable cell carries a floor at load time. Digging never needs to add one, and the cell becomes walkable the instant the wall clears. Consequently **voids exist only where the map author placed them** (chasms, ravines, the open sky above the mountain) — nothing at runtime turns a floored cell into a void.

**C8 — Stairs.** *(Wording corrected 2026-07-26 after QA review — see the enforcement note at the end of this rule.)* The player designates **Dig Stairs Down** at `(x,y,z)`. Preconditions, checked at designation *and* re-checked at execution: the target is a diggable wall, reachable, unclaimed, and `(x,y,z+1)` is in-bounds and **solid**. On completion Excavation submits one atomic batch: `SetFloor(stair)` at `Z` plus `ClearWall` at `Z+1`. Because the cell below was solid immediately beforehand, **nothing can be standing in it** — opening a stair reveals only virgin rock, so there is no displacement case and no combat edge case. **Stairs are permanent in MVP** (no demolish verb) and **down-only**; every stairwell is therefore a permanent, known chokepoint. Combat cannot dig stairs — **enforced at runtime** by the writer-per-authority table's mode assertion, independent of CD-10. *Note: not yet enforced at compile time. ADR-0003 records the writer-interface segregation as a carried obligation that the production implementation must still deliver; until it does, this is a debug-assertion guarantee rather than an unrepresentable-state one.*

**C9 — Claims.** One terrain-modifying job may claim a cell at a time. **Latest designation wins**: a new designation cancels and replaces the pending one, with no rollback — a build that never completed left the cell untouched, and a cancelled dig simply discards progress (C4).

**C10 — Designation invalidation.** Each job type has exactly one precondition, and the existing change-event contract invalidates it — including mid-battle, since paused systems still receive events:

| Job | Precondition | Falsified by |
|---|---|---|
| Dig wall / dig stairs | wall present at target | `WallRemoved` |
| Build wall | no wall at target | `WallPlaced` |
| Build floor | no floor at target | `FloorPlaced` |
| Repair | wall present and below max HP | `WallRemoved` |

Reachability loss is **not** terrain's concern — `IsPassableTerrain` is a per-cell fact; "the path to this cell is gone" belongs to Job Assignment and Pathfinding.

### States and Transitions

| From | To | Cause | Notes |
|---|---|---|---|
| Solid rock | Damaged wall | `ApplyWallDamage` (Combat, TurnBased) | Floor unaffected |
| Damaged wall | Solid rock | `ApplyWallRepair` (Repair, RealTime) | CD-7; consumes hauled materials |
| Damaged wall | Open | `ApplyWallDamage` reaching 0 HP | Reports `WallRemoved`; `Previous` captures the failed tier for CD-1 |
| Solid rock | Open | `ClearWall` on dig completion (Excavation, RealTime) | Dig progress entry discarded |
| Solid rock | Stair | Atomic `SetFloor(stair)` @Z + `ClearWall` @Z+1 | One batch, one event |
| Open | Solid rock | `SetWall` (Construction, RealTime) | HP set to catalog max for the tier |
| Void | Open | `SetFloor` (Construction, RealTime) | The only path that removes a void |
| Stair | — | — | Terminal in MVP: stairs are permanent |

**Damage is visualised at three discrete levels** (intact / damaged / critical). The count is fixed here because it multiplies MeshLibrary size: GridMap holds one item id per cell and offers no per-instance shader channel, so the rendering spec should implement damage as a **third stacked overlay map** (~2 MB video memory, ~2 µs per threshold crossing) rather than by multiplying wall items across tier × style × damage.

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
| Outcome | — | enum {NoWall, Damaged, Destroyed} | set | NoWall if no wall before the call; Destroyed if H₁ = 0; Damaged otherwise |

**Output Range:** H₁ ∈ [0, H₀] — monotonically decreasing, floor-clamped at 0 regardless of how large D is. If no wall is present the call is a no-op and publishes nothing. At H₁ = 0 the wall is removed atomically in the same call and the batch reports `WallRemoved`, with `Previous` capturing the failed tier for CD-1.

**`AppliedAmount`, not `DamageAmount`, is the figure the after-action report should cite** — a 250-damage hit on a 180 HP wall did 180 damage, not 250.

**Example** (HP values illustrative):
- Granite wall H₀ = 180, D = 60 → H₁ = 120, Δ = 60, Damaged. Publishes `WallDamaged`.
- Granite wall H₀ = 180, D = 250 → H₁ = 0 (clamped, not −70), Δ = 180 (not 250), Destroyed. Publishes `WallRemoved`.
- Open cell, D = 60 → NoWall, Δ = 0, no state change, publishes nothing.

### Formula B — Wall Repair Application

`RemainingHp = min(WallHp + RepairAmount, MaxHp(tier))`
`AppliedAmount = RemainingHp − WallHp`

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Wall HP before | `WallHp` (H₀) | ushort | 0 to `MaxHp(tier)` | Current wall HP immediately before the call |
| Repair requested | `RepairAmount` (R) | int | ≥ 0 (negative is a caller error) | HP points requested; the rate per colonist-hour and its hauled-material cost are Repair & Rebuild's (#25, CD-7) to compute |
| Tier ceiling | `MaxHp(tier)` (H_max) | ushort | tier-specific, Material Catalog's (#5) | Looked up via the catalog; never stored per cell |
| Wall HP after | `RemainingHp` (H₁) | ushort | H₀ to H_max | Stored HP after the call |
| Applied amount | `AppliedAmount` (Δ) | int | 0 to (H_max − H₀) | HP actually restored after clamping |
| Outcome | — | enum {NoWall, AlreadyAtMax, Repaired} | set | NoWall if no wall; AlreadyAtMax if H₀ = H_max; Repaired otherwise |

**Output Range:** H₁ ∈ [H₀, H_max] — monotonically non-decreasing, ceiling-clamped at the tier's catalog max. Repair can never overheal or bank HP above the wall's own tier ceiling, and cannot manufacture a wall that is not there (that is Construction's `SetWall`, a distinct verb). If already at max the call is a no-op regardless of R.

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

`IsPassableTerrain(C) = Floor(C) ≠ 0 ∧ Wall(C) = 0` — equivalently, `CellState(C) ∈ {Open, Stair}`.

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Cell | `C` | CellCoord | in-bounds | The cell being classified |
| Floor field | `Floor(C)` | ushort | 0 (none) or a catalog floor id | Terrain's stored floor field |
| Wall field | `Wall(C)` | ushort | 0 (none) or a catalog wall id | Terrain's stored wall field |
| Stair-ness | `IsStairFloor` | bool (catalog-derived) | set | True iff the catalog entry for that floor declares Z-linkage; never stored per cell |
| Classification | `CellState` | enum {SolidRock, Open, Stair, Void} | 4 states | C1's four meaningful combinations |
| Passability | `IsPassableTerrain` | bool | {true, false} | Terrain's sole contribution to walkability; Pathfinding composes it with doors and occupancy |

**Output Range:** exactly the four states named in C1. One combination is **undefined by design and should not occur in normal play**: `Wall ≠ 0 ∧ Floor = 0` (a wall with no floor beneath it), which C7 prevents by guaranteeing every excavatable cell is authored with a floor. Should debug tooling produce it anyway, the formula degrades safely — `IsPassableTerrain` still evaluates false.

**Example:**
- Floor = granite, Wall = granite → SolidRock, passable = false.
- Floor = rock floor, Wall = 0 → Open, passable = true.
- Floor = stair, Wall = 0 → Stair, passable = true.
- Floor = 0, Wall = 0 → Void, passable = false.
- Floor = 0, Wall = granite (anomalous, debug only) → outside the taxonomy; passable = false.

### Formulas deliberately NOT written here

This table is part of the specification. Each row is a decision this document declines to make, so that the system which owns the balance makes it with the context to do so.

| Not written here | Owner | Why Terrain has no basis to write it |
|------------------|-------|--------------------------------------|
| Dig time per material tier | Excavation & Construction (#15/#16) | Terrain never sees dig progress (C4 — it lives in Excavation's side table) and stores no tier field |
| Wall max HP per tier | Material Catalog (#5) | Already assigned by this document's Interactions table; Terrain derives tier by lookup, never sets it |
| Combat damage amounts (the `D` fed into Formula A) | Destructibility (#17) / Combat: Targeting (#22) | Terrain consumes the number; it holds no weapon, armour or resolution data |
| Repair rate and material cost (the `R` fed into Formula B) | Repair & Rebuild (#25), per CD-7 | Terrain consumes the number; hauled-material accounting is Repair's |
| Tier ordering invariant (dirt < granite < reinforced) | Acceptance criterion in #5 (HP) and #15/#16 (dig time) | Terrain never compares tiers — it has no tier field to compare |
| Dig-completion threshold (`progress ≥ dig cost`) | Excavation & Construction (#15/#16) | Both operands live in Excavation's side table; Terrain's only guarantee is the atomic `ClearWall` on completion (C4) |
| Cell ↔ chunk coordinate mapping | ADR-0002 (data contract) | Pure implementation with zero gameplay content; restating it would create a second source of truth |
| Damage-visualisation HP breakpoints | Terrain Rendering & Cutaway (#7) | The *count* (3 levels) is fixed in Detailed Design; where the breakpoints fall is an art / tech-art call |

## Edge Cases

### Displacement rule (referenced below)

When a terrain change forces a colonist or an item stack out of a cell, the destination is the **nearest free cell**, found by scanning candidate offsets in fixed ascending (Z, Y, X) order at expanding radius; first match wins. Deterministic by construction, and deliberately **the same rule ADR-0003 fixes for pre-switch placement normalization** — the game has one displacement algorithm, not two that drift apart.

### Cases

- **If a wall completes in a cell a colonist occupies**: the wall is placed and the colonist is displaced per the rule above. *Blocking the job instead would let a single idling colonist stall construction indefinitely; the brief visual of being moved out of rock is the cheaper cost.*

- **If a wall completes in a cell holding an item stack**: the stack is displaced per the same rule. Nothing is destroyed and nothing is buried — the player never silently loses resources.

- **If displacement finds no free cell within the scan radius**: the build job is cancelled and the player is notified. *Failure is explicit rather than a silent no-op or an entity trapped inside rock.*

- **If the player designates a wall on a stair cell**: rejected at designation time. Stairs are permanent (C8), and permitting a seal would be a back-door partial undo of that decision. *Sealing a stairwell is a genuinely interesting defensive option and is deliberately deferred, not dismissed — reconsider it with the Combat set, where its tactical value can be judged.*

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
| Chunk size | **32** | **Locked** | Must equal GridMap `cell_octant_size`. At 64 one octant spans four chunks, so a single-chunk batch forces rebuilds of three untouched chunks. Never change alone — chunk size and octant size move together or not at all (ADR-0002). |
| Style variants per tier | **≤ 8** | 1–8 | Measured: 1 → 32 draw calls, 2 → 48, 4 → 80, 8 → 144, 16 → 272, against a ≤150 terrain budget. Draw calls scale with distinct material/style combos **co-occurring in an octant**, not with MeshLibrary size. Above 8, terrain alone breaks the budget before entities, VFX or UI are drawn. |
| Damage visual states | **3** (intact / damaged / critical) | 2–4 | A direct multiplier on the damage-overlay MeshLibrary. The HP breakpoints between states are Terrain Rendering's (#7) to set, not this document's. |
| Displacement scan radius | **8** | 4–16 | Below 4, displacement fails often in tight colonies and cancels builds that should have succeeded. Above 16, a failed search gets expensive and an entity can be flung implausibly far from where it stood. |
| MVP world bounds | **128 × 128 × 16** | up to 256 × 256 × 32 | 2 MB of cell data at MVP, 16 MB at the full-vision ceiling. Terrain is not the memory risk; the binding constraint at the ceiling is save-write time (~0.4–0.7 s), not capacity. |
| Cutaway depth | 3 layers | — | **Owned by Terrain Rendering (#7)**, listed here only because it interacts with the stair/void boundary edge case. |

Every value above **except displacement scan radius and damage-state count is measured, not estimated** — sourced from `prototypes/terrain-spike/SPIKE-NOTE.md` and the budgets in `.claude/docs/technical-preferences.md`. The scan radius is inherited from ADR-0003's placement-normalization rule rather than independently derived; reusing its number is deliberate, so the game has one displacement constant rather than two that drift.

## Visual/Audio Requirements

**This system produces no visuals or audio directly.** It is a plain-C# data layer with zero Godot dependency; everything the player sees of the terrain is produced by **Terrain Rendering & Cutaway (#7)** reading this model, against the direction in `design/art/art-bible.md` §1.

Three constraints this document imposes on that spec, recorded here so they are not rediscovered later:

- **Damage must be visible in three discrete states** (intact / damaged / critical) — the count is fixed by this document because it multiplies MeshLibrary size; the HP breakpoints between states are #7's to choose.
- **Damage cannot be a per-cell shader parameter.** GridMap holds one item id per cell and offers no per-instance data channel. The recommended implementation is a third stacked overlay map (~2 MB video memory, ~2 µs per threshold crossing), not a multiplication of wall items across tier × style × damage.
- **Style variety is capped at ~8 variants per tier** by the draw-call budget (measured). If the art direction wants more, that is a budget conversation involving chunk and octant size together, not a rendering-side tweak.

CD-1's after-action report needs to show which material tier failed; the data for that is already carried by `TerrainChange.Previous` (Formula A). Presenting it — including any transient highlight of the broken cell in the 3D view — belongs to the Combat UI spec (#27).

## UI Requirements

**This system has no UI of its own.** The player never interacts with the terrain model directly — they issue designations, and the owning systems execute them.

- Designation and blueprint interaction: **Blueprint / Designation UI (#26)**, which must expose *Dig Wall*, *Dig Stairs Down*, *Build Wall*, *Build Floor* and *Cancel*, and must reject a wall designation on a stair cell at designation time (Edge Cases).
- Notifications this document requires the shared Notifications component to surface: **a build cancelled because displacement found no free cell** — the one place a terrain rule fails visibly and the player must be told rather than left wondering why a job vanished.

## Acceptance Criteria

Story type for this system is **Logic** (packed-struct data model, pure formulas, deterministic state machine), with several criteria that are properly **Integration** because they span Terrain plus a consuming system. Per the project's test-evidence table: Logic → `tests/unit/terrain/`, BLOCKING; Integration → `tests/integration/terrain/`, BLOCKING; performance → `tests/performance/terrain/`, BLOCKING (these are gates in ADR-0002, not advisory figures).

### Core rules

- **GIVEN** any wall-bearing cell of any material tier, **WHEN** a dig is designated by any colonist regardless of skill state, **THEN** the designation is accepted — no eligibility check rejects it and rejection never depends on colonist identity. *(C2)*
- **GIVEN** a cell at the world edge, **WHEN** queried, **THEN** it behaves as a normal diggable wall, never as an indestructible bedrock tier. *(C2)*
- **GIVEN** target `T = (10,10,3)`, **WHEN** each of the 8 Moore-neighbourhood cells is tested, **THEN** all 8 return true, and `T` itself, any cell two or more away, and any cell at `Z ≠ 3` return false. *(C3 / Formula C)*
- **GIVEN** a diagonal work position whose two flanking orthogonal cells are both walls, **WHEN** adjacency is evaluated, **THEN** it still returns true — the geometric test is independent of corner-cutting legality. *(C3)*
- **GIVEN** a wall cell with a dig in progress, **WHEN** `GetCell` is queried mid-dig, **THEN** `WallHp` is unchanged and **no** `TerrainChange` has published. *(C4)*
- **GIVEN** a wall mid-dig, **WHEN** `ApplyWallDamage` is called against it, **THEN** HP decreases per Formula A and dig progress is unaffected — Terrain exposes no coupling field between them. *(C5)*
- **GIVEN** an in-bounds cell with no wall and **no floor**, **WHEN** `SetWall` is called from a valid work position, **THEN** the wall is placed — floor state at the target is irrelevant. *(C6)*
- **GIVEN** a freshly loaded map, **WHEN** every excavatable cell is queried, **THEN** each carries a non-zero floor. *(C7 — Integration, depends on Map Authoring)*
- **GIVEN** a solid-rock cell whose wall clears by dig completion, **WHEN** `ClearWall` executes, **THEN** the cell is walkable in the same call, with no separate floor-add step, and no cell ever becomes `Void` as a result of a dig. *(C7)*
- **GIVEN** a diggable, reachable, unclaimed wall at `(x,y,z)` with `(x,y,z+1)` in bounds and solid, **WHEN** Dig Stairs Down completes, **THEN** exactly **one** atomic batch publishes both `SetFloor(stair)@Z` and `ClearWall@Z+1`. *(C8)*
- **GIVEN** a stair designation whose precondition is falsified between designation and execution, **WHEN** execution is attempted, **THEN** it is rejected — verifying the precondition is checked **twice**, not only at designation. *(C8)*
- **GIVEN** a cell with a pending dig, **WHEN** a build is designated on the same cell, **THEN** the dig is cancelled with its progress discarded, the wall is untouched, and the claim transfers. *(C9 — Integration)*
- **GIVEN** each of the four job types with its precondition, **WHEN** the corresponding falsifying event publishes, **THEN** the pending job is invalidated — all four rows of C10's table as four discrete cases. *(C10 — Integration)*
- **GIVEN** the colony is paused during combat, **WHEN** Combat destroys a designated cell, **THEN** the invalidating event still fires and is still received — paused is not event-deaf. *(C10 — Integration)*

### Formulas

- **GIVEN** `WallHp = 180, D = 60`, **WHEN** damage applies, **THEN** `RemainingHp = 120`, `AppliedAmount = 60`, outcome `Damaged`, one `WallDamaged` batch.
- **GIVEN** `WallHp = 180, D = 250` (overkill), **WHEN** damage applies, **THEN** `RemainingHp = 0` (never negative), **`AppliedAmount = 180`, not 250**, outcome `Destroyed`, and `WallRemoved` publishes with `Previous` carrying the pre-destruction tier. *A test asserting only `RemainingHp == 0` does **not** cover Formula A — the `AppliedAmount` assertion is the load-bearing one (CD-1 cites this figure).*
- **GIVEN** an open cell, **WHEN** damage applies with any `D ≥ 0`, **THEN** outcome `NoWall`, `AppliedAmount = 0`, and **zero batches publish** — the subscriber receives no call at all, not an empty batch.
- **GIVEN** `WallHp = 280, H_max = 300, R = 100` (overheal), **WHEN** repair applies, **THEN** `RemainingHp = 300` and **`AppliedAmount = 20`, not 100** — the exact figure CD-7 bills materials against.
- **GIVEN** `WallHp = H_max`, **WHEN** repair applies with any `R`, **THEN** outcome `AlreadyAtMax`, `AppliedAmount = 0`, zero batches publish.
- **GIVEN** each of the four (floor, wall) combinations, **WHEN** classified, **THEN** state and passability match Formula D exactly, including the `IsStairFloor` branch separating `Open` from `Stair`.
- **GIVEN** the anomalous wall-without-floor combination, **WHEN** passability is evaluated, **THEN** it returns false **without throwing** — graceful degradation, per Formula D's undefined-by-design note.

### Edge cases

- **GIVEN** a colonist occupying a cell, **WHEN** a wall build completes there, **THEN** the colonist is displaced per the scan rule and the build proceeds — not blocked, not cancelled. *(Integration)*
- **GIVEN** an item stack occupying a cell, **WHEN** a wall build completes there, **THEN** the stack is displaced with count and identity unchanged — nothing destroyed, nothing buried. *(Integration)*
- **GIVEN** a build target with no free cell anywhere in the scan radius, **WHEN** completion is attempted, **THEN** the job is cancelled and a player-visible notification fires — explicitly, not as a silent no-op. *(Integration)*
- **GIVEN** a stair cell, **WHEN** a wall is designated on it, **THEN** the designation is rejected and never reaches a job queue.
- **GIVEN** a bulk `Apply` whose entry at index `k` is invalid, **WHEN** it executes, **THEN** `BulkResult` reports `k`, **zero** of the N mutations apply, and **zero** batches publish — verified by a post-call sweep showing nothing changed anywhere in the batch, not merely at `k`.
- **GIVEN** a displacement search where a geometrically nearer candidate is a void, **WHEN** it resolves, **THEN** the void is skipped in favour of the next passable candidate.

### Writer discipline and determinism

- **GIVEN** TurnBased is active, **WHEN** Excavation, Construction or Repair attempt any write, **THEN** the assertion fires and the write does not apply.
- **GIVEN** RealTime is active, **WHEN** Combat: Targeting attempts `ApplyWallDamage`, **THEN** the assertion fires and the write does not apply.
- **GIVEN** the mutation window is closed, **WHEN** a legal writer for the active mode calls a write method from a UI-callback context, **THEN** the mutation-window assertion fires — a guarantee distinct from the authority check above, and tested independently.
- **GIVEN** a fixed displacement scenario, **WHEN** it runs N times from fresh state with identical input, **THEN** the destination cell is identical every run. Include a case where a wrong tie-break axis would select a different candidate — **otherwise the test is a tautology rather than a falsification.**
- **GIVEN** identical mutation sequences applied to two freshly constructed worlds, **WHEN** both complete, **THEN** `Snapshot()` output is byte-identical **and** the published change streams match in content and order. *(ADR-0002 rule 8)*
- **GIVEN** a dig at partial progress, **WHEN** the world is snapshotted and restored, **THEN** the claim bit round-trips intact. *(Terrain's half is testable now; the full "resumes exactly where it was" claim is Integration, blocked on Excavation's serialization — see Dependencies.)*

### Performance

Thresholds are **tolerance-banded above the measured value** so CI catches genuine regressions without flaking on hardware and measurement noise. Draw calls are pinned exactly, because that figure is configuration-specific rather than noisy.

| Criterion | Measured | Gate |
|---|---|---|
| Full-map walkability sweep, MVP world | 0.290 ms | **≤ 0.5 ms** |
| Allocation over 60,000 mutations | 0.17 B/mutation, 0 Gen0 | **≤ 1 B/mutation and exactly 0 Gen0** |
| Terrain draw calls, 3-layer cutaway at one style per tier | 32 | **exactly 32** (not "≤150" — 150 is the whole-budget ceiling; 32 is this configuration's value) |
| Concentrated AoE, 75 cells in one octant | 21.5 µs | **≤ 30 µs** |

- **GIVEN** the terrain assembly, **WHEN** CI greps it for Godot references, **THEN** it finds none. *(ADR-0002 validation criterion 1)*

### Known gaps — not certifiable today

1. **60 fps on target hardware** — **ADVISORY, blocked**. The spike ran on software Vulkan (3–4 fps, no signal). No headless suite can verify this, and none should be allowed to imply it has. Closes only with a run on target hardware; it is also the last gate before ADR-0002 moves from Proposed to Accepted.
2. **Two test hooks may not exist yet**: forcing `TimeAuthorityManager` into a given mode and window state without a full tick, and the debug-console sweep for *claim bit ≡ Job Assignment's table*. Confirm both before marking the writer-discipline criteria ready.
3. **The spike's render-matches-model check** (15,763 cells verified against `TerrainWorld`) currently lives in prototype code. Promoting it to a standing regression test means reimplementing it in `tests/integration/terrain/` — prototype code is never migrated.
4. **Cross-system criteria** for C4, C5 and C9 certify only Terrain's half until Excavation & Construction carries its matching criteria.

## Open Questions

Each item names an owner and the point at which it should be resolved. Nothing here blocks implementation of this system.

| # | Question | Owner | Resolve by |
|---|---|---|---|
| 1 | **60 fps on target hardware.** The terrain spike ran on software Vulkan (3–4 fps — no signal). Draw calls, memory and CPU costs are hardware-independent and pass; the frame-rate clause does not yet exist. | technical-director | **This is the last gate before ADR-0002 moves from Proposed to Accepted.** Run `prototypes/terrain-spike/render/` on target hardware. |
| 2 | **May a wall seal a stairwell?** Currently rejected (Edge Cases), because permitting it would be a back-door partial undo of C8's permanence. Sealing a permanent chokepoint is a genuinely interesting defensive option and was deferred, not dismissed. | Combat set (#19–#23) | When the tactical value of chokepoint control can be judged against real combat rules. |
| 3 | **Should a combat-damaged wall be quicker to mine?** C5 keeps dig progress and wall HP independent in MVP. "Breached walls clear faster" is a plausible later rule with obvious player logic behind it. | Excavation & Construction (#15/#16) | A tuning pass once dig times exist. |
| 4 | **Cutaway boundary treatment** where a stair landing or void column falls past the visible window — read it as darkness, or extend the window by one layer at stair cells? | Terrain Rendering & Cutaway (#7) | That spec. Routed there by user decision. |
| 5 | **Should material tier ever gate *possibility* of digging, not just speed?** C2 says no for MVP, because gating would depend on Skill & Veterancy (#30), which is Vertical Slice. Revisit when that system lands — it changes what "reinforced" *means* to the player. | game-designer | Vertical Slice, with #30. |
| 6 | **Are up-stairs worth building?** Mechanically symmetric to Dig Stairs Down and cheap, but low value on a top-down, hand-authored, 3-strata mountain. Deferred, not rejected. | Excavation & Construction (#15/#16) | When a concrete use case appears (e.g. connecting a side cavern reached from below). |
| 7 | **Unlimited cantilevers.** C6 permits arbitrarily long unsupported floor spurs; reachability prevents disconnected rooms but nothing prevents implausible geometry. Accepted scope debt. | Structural Collapse (#34) | Alpha, when real support rules retrofit. |
| 8 | **Demolishable floors as a tactical mechanic** (drop-traps / murder holes). Deliberately out of MVP — reverting a floor to void opens a live drop into an occupied layer, which is a *new* mechanic rather than a cancel action. Ties naturally to CD-11's preference for player-activated pre-built objects. | creative-director | Vertical Slice / Alpha roadmap. |
| 9 | **Style-variety ceiling of ~8 per tier** may constrain the art direction. Raising it means revisiting chunk size and octant size **together**, not octant alone. | art-director + technical-director | The art/lighting pass, against the measured draw-call curve. |
