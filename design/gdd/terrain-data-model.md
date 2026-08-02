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

**C8 — Stairs.** The player designates **Dig Stairs Down** at `(x,y,z)`. Preconditions, checked at designation *and* re-checked at execution: the target is a diggable wall, reachable, unclaimed, and `(x,y,z+1)` is in-bounds and **solid**. On completion Excavation submits one atomic batch: `SetFloor(stair)` at `Z` plus `ClearWall` at `Z+1`. Because the cell below was solid immediately beforehand, **nothing can be standing in it** — opening a stair reveals only virgin rock, so there is no displacement case and no combat edge case. **Stairs are permanent in MVP** (no demolish verb) and **down-only**; every stairwell is therefore a permanent, known chokepoint. Combat cannot dig stairs — structurally guaranteed by the writer-per-authority table, independent of CD-10.

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

[To be designed]

## Tuning Knobs

[To be designed]

## Visual/Audio Requirements

[To be designed]

## UI Requirements

[To be designed]

## Acceptance Criteria

[To be designed]

## Open Questions

[To be designed]
