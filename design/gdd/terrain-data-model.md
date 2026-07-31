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

[To be designed]

## Edge Cases

[To be designed]

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
