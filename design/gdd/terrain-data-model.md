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

[To be designed]

### States and Transitions

[To be designed]

### Interactions with Other Systems

[To be designed]

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
