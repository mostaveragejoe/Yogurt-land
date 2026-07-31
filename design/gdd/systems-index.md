# Systems Index: Hollowdeep

> **Status**: Approved (user + TD-SYSTEM-BOUNDARY + PR-SCOPE gates)
> **Created**: 2026-07-24
> **Last Updated**: 2026-07-24
> **Source Concept**: design/gdd/game-concept.md

---

## Overview

Hollowdeep is a real-time colony sim on a layered floor+wall tile grid that switches into turn-based tactics combat fought inside the player's own architecture. Mechanically this decomposes into: a foundation of shared data and contracts (terrain grid, time authority, events, serialization); a core of simulation infrastructure (pathfinding, colonist data, job dispatch, rendering/cutaway); the gameplay loop itself (excavate → construct → simulate needs → raid trigger → tactics battle → repair); and a menu-driven presentation layer. The dominant architectural facts: the **mode switch is a permanent integration tax** (every simulation system must define behavior under both time authorities), and the **Terrain Data Model and Colonist Entity are the two highest-fan-out systems** — their contracts (ADR-002, ADR-003) gate everything downstream. Design order follows dependency layers, gated by the Tier 0 spikes.

**Documentation routing policy** (recorded decision, per PR-SCOPE gate 2026-07-24): not every system gets a full 8-section GDD. Systems route to one of: **Full GDD** (rule-heavy, expensive-to-unwind), **Quick-spec** (`design/quick-specs/`, 1–2 pages: purpose, rules, public interface, acceptance criteria), **ADR-only** (pure plumbing — the ADR plus code is the spec), **UX spec** (`design/ux/`), or **Audio brief**. Every simulation-bearing spec of any tier MUST include a **"Behavior under each time authority"** section. A future `/review-all-gdds` should treat quick-specs/ADR-only routing as compliant per this policy, not as missing GDDs. If more than two quick-spec systems need promotion to full GDDs during implementation, revisit the routing.

**Sequencing policy** (per PR-SCOPE): documents are authored **just-in-time, one dependency layer ahead of implementation** — not all up front. Order: 3 ADRs (as *Proposed*) + cross-cutting contracts + debug console → **Tier 0 spikes (fun spike first)** → GDD authoring against measured numbers. The Terrain Data Model GDD explicitly waits for terrain-spike draw-call/memory numbers.

---

## Systems Enumeration

| # | System Name | Category | Priority | Status | Doc Type | Design Doc | Depends On |
|---|-------------|----------|----------|--------|----------|------------|------------|
| 1 | Terrain Data Model | Core | MVP | Not Started | Full GDD (after terrain spike) + ADR-002 | — | (none — Foundation) |
| 2 | Time Authority / Mode-Switch (inferred, elaborated) | Core | MVP | Not Started | Full GDD + ADR-001 | — | (none — Foundation) |
| 3 | World Change Event Bus (inferred, TD) | Core | MVP | Not Started | ADR-only | — | (none — Foundation) |
| 4 | Seeded RNG / Determinism (inferred, TD) | Core | MVP | Not Started | ADR-only | — | (none — Foundation) |
| 5 | Material Catalog (inferred) | Economy | MVP | Not Started | Quick-spec | — | (none — Foundation) |
| 6 | Save/Load & World Serialization (inferred) | Persistence | MVP | Not Started | ADR-only (contract now, format later) | — | Cross-cutting: shape of all state-holding systems |
| 7 | Terrain Rendering & Cutaway (inferred, TD; absorbs Camera & Z-Level Visibility) | Core | MVP | Not Started | Quick-spec | — | Terrain Data Model, World Change Event Bus |
| 8 | Pathfinding & Navigation (inferred) | Core | MVP | Not Started | Quick-spec (spike + ADR carry it) | — | Terrain Data Model, World Change Event Bus |
| 9 | Colonist Entity & Attributes (inferred) | Core | MVP | Not Started | Quick-spec + ADR-003 | — | Terrain Data Model |
| 10 | Job Assignment & Priority (inferred) | Gameplay | MVP | Not Started | Full GDD (coupled with #13) | — | Colonist Entity, World Change Event Bus |
| 11 | Stockpile & Hauling (inferred, TD split) | Economy | MVP **(user-mandated, non-negotiable)** | Not Started | Quick-spec | — | Terrain Data Model, Material Catalog, Job Assignment |
| 12 | Spatial Query / LOS & Cover (inferred, TD) | Core | MVP | Not Started | Quick-spec | — | Terrain Data Model |
| 13 | Colonist Needs & Simulation | Gameplay | MVP | Not Started | Full GDD (coupled with #10) | — | Colonist Entity, Job Assignment, Material Catalog |
| 14 | Map Authoring / Content Load (inferred, TD) | Meta | MVP | Not Started | Quick-spec | — | Terrain Data Model |
| 15 | Excavation System | Gameplay | MVP | Not Started | Full GDD (combined with #16) | — | Terrain Data Model, Pathfinding, Colonist Entity, Job Assignment, Material Catalog, Stockpile & Hauling |
| 16 | Construction System | Gameplay | MVP | Not Started | Full GDD (combined with #15) | — | Same as #15 |
| 17 | Material-Tier Destructibility & Damage | Gameplay | MVP | Not Started | Full GDD | — | Terrain Data Model, Material Catalog, World Change Event Bus |
| 18 | Raid / Threat Trigger (inferred) | Gameplay | MVP | Not Started | Full GDD | — | Time Authority, Colonist Entity, strata data (Material Catalog + own scaling tables) |
| 19 | Combat: Turn Order & Initiative (split per PR-SCOPE) | Gameplay | MVP | Not Started | Full GDD (combat set) | — | Time Authority, Colonist Entity |
| 20 | Combat: Action Economy (split) | Gameplay | MVP | Not Started | Full GDD (combat set) | — | #19 |
| 21 | Combat: Movement & Reachability (split) | Gameplay | MVP | Not Started | Full GDD (combat set) | — | #19, Pathfinding, Spatial Query/LOS |
| 22 | Combat: Targeting & Resolution (split) | Gameplay | MVP | Not Started | Full GDD (combat set) | — | #19, #20, Spatial Query/LOS, Material-Tier Destructibility, Material Catalog (debris/consumption) |
| 23 | Combat: Raider Decision-Making (absorbs Raider AI) | Gameplay | MVP | Not Started | Full GDD (combat set) | — | #19–#22, Spatial Query/LOS, Pathfinding |
| 24 | Squad Preparation (fixed loadouts in MVP; equipment half deferred to VS) | Gameplay | MVP | Not Started | Quick-spec | — | Colonist Entity, Time Authority (mode-switch seam) |
| 25 | Repair & Rebuild — **PROTECTED: loop closer, do not cut** | Gameplay | MVP | Not Started | Quick-spec | — | Construction, Material-Tier Destructibility, Job Assignment, Pathfinding, Material Catalog, Stockpile & Hauling (CD-7: repair consumes hauled materials) |
| 26 | Blueprint / Designation UI | UI | MVP | Not Started | UX spec | — | Excavation, Construction |
| 27 | Combat UI | UI | MVP | Not Started | UX spec | — | Combat set (#19–#23), Terrain Rendering & Cutaway |
| 28 | Colonist / Roster UI | UI | MVP | Not Started | UX spec | — | Colonist Entity, Needs, Squad Prep |
| 29 | Dev Tools / Debug Console (inferred, TD) — **PROTECTED: build during Tier 0** | Meta | Tier 0 | Not Started | ADR-only / lightweight | — | grows with each system it pokes |
| 30 | Colonist Skill & Veterancy (inferred) | Progression | Vertical Slice | Not Started | Quick-spec | — | Colonist Entity (data store), combat-outcome event |
| 31 | Colonist Identity & Memory | Narrative | Vertical Slice (data fields land in MVP) | Not Started | Quick-spec | — | Colonist Entity, combat-outcome event |
| 32 | Onboarding / First Session Flow (inferred) | Meta | Vertical Slice | Not Started | UX spec | — | Blueprint UI, Excavation, Construction |
| 33 | Ambient & Combat Audio (inferred) | Audio | Vertical Slice | Not Started | Audio brief | — | Needs & Sim, Combat set, Raid Trigger |
| 34 | Structural Collapse (separate future system per PR-SCOPE — MVP Destructibility is designed as if collapse will never exist) | Gameplay | Alpha | Not Started | Full GDD (future) | — | Material-Tier Destructibility, Terrain Data Model |
| 35 | World / Mountain Generation (procgen) | Meta | Alpha | Not Started | Full GDD (future) | — | Terrain Data Model, Map Authoring |

**Demotions/reorganizations recorded** (PR-SCOPE, user-approved): Strata/Depth Progression is **data, not a system** (material distribution in Material Catalog + threat scaling in Raid Trigger). Notifications & Alerts is a **shared UI component** specified once across the three UX specs. Squad equipment defers to Vertical Slice (MVP uses fixed role loadouts). Stockpile & Hauling **stays MVP by explicit user decision** overriding the producer's recommendation.

**Doors are an MVP entity kind** (user decision 2026-07-24, recorded in ADR-0003 — chokepoint play and breach tactics are the game's identity): not a new numbered system but a note across existing entries — built/deconstructed by Construction (#16), damaged by Combat: Targeting & Resolution (#22) exactly like walls (provisional destructibility rule), opened as part of combat movement (#21), composed into walkability by Pathfinding (#8), LOS-blocking read by Spatial Query (#12). Entity state and write ownership defined in ADR-0003 (`DoorStore`).

---

## Priority Tiers

| Tier | Definition | Count |
|------|------------|-------|
| **Tier 0** | Built during spikes (Dev Tools) | 1 |
| **MVP** | Required for the core loop fun test | 25 systems (28 entries incl. combat split) |
| **Vertical Slice** | Growth, identity, audio, onboarding | 4 |
| **Alpha** | Procgen + structural collapse | 2 |
| **Full Vision** | Content scaling only — no new standalone systems | — |

---

## Dependency Map

### Foundation Layer (no dependencies)
1. **Terrain Data Model** — pure data: chunked cell arrays, mutation API, change events. Plain C#, ZERO Godot dependency, headlessly unit-testable. The authoritative model is never a GridMap node (GridMap is a candidate *rendering backend* only). Cell record carries: floor type, wall type, material tier, damage/HP, style dressing, reservation tags — memory layout (struct-of-arrays vs per-chunk AoS) decided in ADR-002.
2. **Time Authority / Mode-Switch** — the tick contract every sim system implements (ADR-001). Under-declaring this dependency is forbidden: every system that advances state over time depends on it (see Cross-Cutting Contracts).
3. **World Change Event Bus** — one publisher (Terrain), many subscribers. Capped by ADR: a dumb synchronous dispatcher — no priorities, filters, replay, or ordering guarantees.
4. **Seeded RNG / Determinism** — per-system seeded streams; without it, sim bugs are irreproducible and saves desync.
5. **Material Catalog** — item/material definitions incl. per-stratum distribution data. Zero deps.
6. **Save/Load serialization contract** — see Cross-Cutting Contracts (format designed late; contract enforced from the first state-holding system; round-trip test in CI).

### Core Layer
7. Terrain Rendering & Cutaway — deps: Terrain Data Model, Event Bus. Owns meshing/instancing, Z-level slicing, chunk rebuild, draw-call budget, cutaway focus treatment (presentation-layer channel per art bible §1).
8. Pathfinding & Navigation — deps: Terrain Data Model, Event Bus (path invalidation on mid-route dig). Includes region/connectivity flood-fill, reachability caching.
9. Colonist Entity & Attributes — deps: Terrain Data Model. Data store with **write-ownership table** (ADR-003): each field group has exactly one writer. Health write-arbitration (Combat vs Needs) resolved in ADR-003. Identity Bookkeeping — the MVP writer of `BattlesSurvived` (CD-4) — is a small entity-layer module defined in ADR-0003, not a new system.
10. Job Assignment & Priority — deps: Colonist Entity, Event Bus. Owns the task queue, arbitration, claiming, cancellation, and **job invalidation on world mutation**. Starts concrete (dig/build/haul); generalizes to the task-producer interface only when the third producer exists.
11. Stockpile & Hauling — deps: Terrain, Material Catalog, Job Assignment. Reservation logic is a first-class design problem (top genre bug class).
12. Spatial Query / LOS & Cover — deps: Terrain Data Model. Serves combat LOS/cover and raider AI without exposing combat internals.
13. Map Authoring / Content Load — deps: Terrain Data Model. The MVP producer of terrain data (hand-authored mountain); procgen (#35) is a later second producer.

### Feature Layer (the loop)
14. Excavation · 15. Construction · 16. Needs & Simulation (coupled pair with Job Assignment: Needs emits scored task candidates into the queue Job Assignment owns) · 17. Material-Tier Destructibility · 18. Raid Trigger · 19–23. Combat set · 24. Squad Preparation · 25. Repair & Rebuild (scheduled immediately after Construction, NOT at the end).

### Presentation Layer
26. Blueprint/Designation UI · 27. Combat UI · 28. Roster UI (+ shared Notifications component across all three).

### Polish Layer
32. Onboarding · 33. Audio.

---

## Cross-Cutting Contracts (annex — hard cap: one page, these three, no fourth)

> **Authored 2026-07-25**: the binding one-page annex now lives at `docs/architecture/cross-cutting-contracts.md`, written against ADR-0001/0002/0003. The mid-battle-save question below is resolved: **NO** (CD-9). The summaries below are the original index-level sketch; the annex page is authoritative.

1. **Time Authority tick contract (ADR-001)**: every simulation system implements the tick interface; every sim spec includes a "Behavior under each time authority" section. Named seam risks: Squad Prep at the mode-switch boundary (who is drafted, where they stand at transition); Notifications queueing across modes.
2. **Serialization contract (with ADR set)**: authoritative state is plain data separable from Godot nodes; cross-object references by stable ID only; derived/cached state reconstructible, never serialized; every state-holding system exposes `Snapshot()`/`Restore()`; per-system seeded RNG streams. Round-trip test is a CI gate from the first state-holding system. **Open design question routed to creative-director before the Combat GDD: can the player save mid-battle?** ("No" is dramatically cheaper.)
3. **World Change Events**: Terrain publishes; Pathfinding, Job Assignment, Rendering, Combat LOS, Repair, and the Notifications component subscribe. Dumb synchronous dispatcher by ADR.

---

## Recommended Design Order

| Order | Item | Type | Priority | Layer | Agent(s) | Est. |
|-------|------|------|----------|-------|----------|------|
| 1 | ADR-001 Time Authority | ADR (Proposed) | MVP | Foundation | technical-director, godot-specialist | S |
| 2 | ADR-002 Terrain Data Model | ADR (Proposed; final numbers await terrain spike) | MVP | Foundation | technical-director, godot-specialist | S |
| 3 | ADR-003 Entity Data Ownership | ADR (Proposed) | MVP | Foundation | technical-director, lead-programmer | S |
| 4 | Cross-cutting contracts annex + serialization contract | Contract page | MVP | Foundation | technical-director | S |
| 5 | Dev Tools / Debug Console | Build (Tier 0) | Tier 0 | — | gameplay-programmer | S |
| — | **Tier 0 SPIKE GATE: fun spike → terrain spike → mode-switch spike → pathfinding spike → save/load spike** | `/prototype` | — | — | prototyper | — |
| 6 | Terrain Data Model | Full GDD | MVP | Foundation | systems-designer, godot-specialist | M |
| 7 | Time Authority / Mode-Switch | Full GDD | MVP | Foundation | systems-designer, technical-director | M |
| 8 | Colonist Entity & Attributes | Quick-spec | MVP | Core | game-designer | S |
| 9 | Material Catalog | Quick-spec | MVP | Foundation | economy-designer | S |
| 10 | Pathfinding & Navigation | Quick-spec | MVP | Core | godot-specialist | S |
| 11 | Terrain Rendering & Cutaway | Quick-spec | MVP | Core | godot-specialist, technical-artist | S |
| 12 | Job Assignment + Needs & Simulation | Full GDD (coupled pair, one session set) | MVP | Core/Feature | game-designer, systems-designer | L |
| 13 | Stockpile & Hauling | Quick-spec | MVP | Core | systems-designer | S |
| 14 | Excavation + Construction | Full GDD (combined) | MVP | Feature | game-designer | M |
| 15 | Material-Tier Destructibility | Full GDD | MVP | Feature | systems-designer | M |
| 16 | Repair & Rebuild | Quick-spec | MVP | Feature | game-designer | S |
| 17 | Spatial Query / LOS & Cover | Quick-spec | MVP | Core | godot-specialist | S |
| 18 | Combat set (#19–23) | Full GDD set | MVP | Feature | game-designer, systems-designer | L |
| 19 | Raid Trigger | Full GDD | MVP | Feature | systems-designer | M |
| 20 | Squad Preparation | Quick-spec | MVP | Feature | game-designer | S |
| 21 | Blueprint UI · Combat UI · Roster UI | UX specs | MVP | Presentation | ux-designer | M |
| 22 | Map Authoring / Content Load | Quick-spec | MVP | Core | tools-programmer | S |
| 23 | Save/Load format finalization | ADR update | MVP | Cross-cutting | technical-director | S |

*(Vertical Slice docs — Skill & Veterancy, Identity & Memory, Onboarding, Audio brief — authored just-in-time when their layer approaches.)*

---

## Circular Dependencies

- **Combat ↔ Skill & Veterancy** — resolved via ADR-003: Colonist Entity owns skill/veterancy data with Veterancy as sole writer; Combat reads data, emits the combat-outcome event; Veterancy and Identity & Memory both consume that event (multi-consumer schema, not ad-hoc callback). *Realized in ADR-0003 as the `EncounterOutcomeReport` (one-slot `EncounterOutcomeInbox`, drained by `PostEncounterReconcile` under ADR-0001 ordering): MVP consumers Identity Bookkeeping + Notifications; VS consumers Veterancy + Identity & Memory.*
- **Job Assignment ↔ Needs** — resolved: Needs is a *task producer* submitting scored candidates into the queue Job Assignment owns; designed as a coupled pair in one session set. Generalization to more producers happens at the third concrete producer, not before.

## High-Risk Systems

| System | Risk Type | Description | Mitigation |
|--------|-----------|-------------|------------|
| Terrain Data Model + Rendering (XL) | Technical | Highest fan-out; God-object risk; GC/memory layout on 16.6ms budget | Data/render split (done); ADR-002; terrain spike numbers before GDD; headless unit tests |
| Pathfinding (XL) | Technical | Dynamic 3D grid invalidation — genre's #1 correctness/perf sink | Dedicated spike; region/connectivity caching design |
| Combat set (XL) | Design + Technical | Half the game's identity; was under-decomposed | Split into 5 entries; fun spike FIRST, before any combat GDD |
| Job Assignment + Needs (XL) | Design | Coupled pair; invalidation + reservation bugs | Coupled design session; invalidation as first-class problem |
| Save/Load | Technical | HIGH (TD-FEASIBILITY); retrofit is a 3-month loss | Contract in Foundation now; CI round-trip gate; mid-battle-save question to CD early |
| Stockpile & Hauling | Technical | Reservation bug class; kept in MVP by user mandate | Reservation/invalidation designed with Job Assignment, not bolted on |
| Squad Prep mode-switch seam | Technical | "Hairiest moment in the architecture" (TD) | Explicit seam section in its quick-spec + ADR-001 |

## Progress Tracker

| Metric | Count |
|--------|-------|
| Total index entries | 35 |
| Design docs started | 0 |
| ADRs written | 3/3 (all Proposed, spike-gated) + contracts annex |
| Tier 0 spikes complete | 0/6 |
| MVP systems designed | 0/25 |
| Vertical Slice systems designed | 0/4 |

## Gate Record

- **TD-SYSTEM-BOUNDARY** (2026-07-24): CONCERNS — all addressed: terrain data/render split, Resource split into Material Catalog + Stockpile & Hauling, write-ownership table (ADR-003), serialization contract hoisted, Time Authority cross-cutting annex, 6 systems added, edges fixed.
- **PR-SCOPE #2** (2026-07-24): OPTIMISTIC — adjustments applied: tiered documentation policy, just-in-time authoring, spike gate before GDDs, combat split, 3 demotions accepted; Stockpile & Hauling kept in MVP by explicit user decision. Working-hours assumption behind the 12–24mo Tier 1 band: **[TO CONFIRM: full-time vs evenings/weekends — double the bands if part-time]**.
- **CD-SYSTEMS** (2026-07-24): CONCERNS — 9 notes recorded below; mid-battle-save resolved (NO); Pillar 4 reframed in concept doc; Identity minimum surface landed in MVP; one style vocabulary in MVP with picker in VS.
- **CD-PLAYTEST** (2026-07-25): **CONFIRM PROCEED** on the fun spike (hypothesis CONFIRMED; report: `prototypes/hollowdeep-fun-spike-concept/REPORT.md`). Pillar 1 confirmed by bias-immune mechanical evidence (emergent weakest-material breaching + traceable scars); Pillar 2 HALF confirmed (prep sets the ceiling — the in-battle-skill half is untested and is the same finding as "raiders unreactive"); Pillar 3 confirmed in its cheapest form (free/instant rebuild — see CD-18). 9 binding notes CD-10–CD-18 recorded below; caveats appended to the report. Quote for the record: *"Proceed — and treat raider behavior, not raider variety, as the next thing that has to be proven."*

## Creative Director Notes (CD-SYSTEMS, 2026-07-24 — fold into specs as they are authored)

- **CD-1 (Pillar 3 "teach" half)**: The Combat UI UX spec MUST include a post-battle after-action section — what broke, where, which material tier failed, what breached first. A playtester should be able to point at a wall and say which choice failed.
- **CD-2 (breach-point selection)**: Owned by **Raid Trigger**. Acceptance criterion: three battles in the same colony feel different because raiders came in from different places. With one raider type and fixed loadouts, this is the MVP's primary variety lever.
- **CD-3 (anti-permadeath rule)**: Raid Trigger defines a survivability floor; Combat: Raider Decision-Making defines a raider objective and withdraw/satisfaction condition (raiders leave when they get what they came for or the cost exceeds it). No exterminate-all default.
- **CD-4 (Identity minimum surface in MVP — decided YES)**: persistent name + deterministic appearance seed (Colonist Entity), battles-survived counter (Roster UI), named death notification with location (Notifications component). Fun-spike interpretation protocol: *"deaths feel weightless" is not a combat-design verdict in MVP* — full Identity & Memory is deliberately absent.
- **CD-5 (style palette — decided)**: MVP ships ONE ornament vocabulary; the style-picker mechanism + second vocabulary land in Vertical Slice. Recorded so Pillar 1's expression half is a deliberate partial in MVP, not an omission.
- **CD-6 (Pillar 5 owners)**: Material Catalog owns *descent creates escalating material reward*; Raid Trigger owns *descent creates escalating threat*. Both need acceptance criteria in their specs — Pillar 5 must not decay into untuned tables.
- **CD-7 (repair costs materials)**: Repair & Rebuild consumes materials that must be hauled (deps updated in the table). Logistics under pressure is what gives the Pillar 3 loop a price.
- **CD-8 (Pillar 4 reframe — applied)**: concept doc Pillar 4 now includes "colony work is legible and satisfying to watch" + Design Test B (choose visible over abstracted). Protects hauling/job visibility from future optimization-to-spreadsheet.
- **CD-9 (mid-battle save — decided NO)**: autosave at mode-switch into tactics and at battle end; saving disabled inside battles. Combat GDD set carries a battle-length acceptance criterion: **8–15 min target, 20 min hard ceiling**. Post-MVP option if playtests demand: suspend-to-exit as a single non-reloadable state. Serialization scope for combat state shrinks accordingly (contract note).
- **Anti-pillar watch items**: Combat UI must not introduce timed inputs; Identity & Memory generates history from events only (no authored backstory pools); World Gen stays one vertical mountain; Structural Collapse (Alpha) requires a fresh anti-permadeath check at adoption.
- **MVP validation limitation (recorded; amended per CD-17, 2026-07-25)**: Discovery has two vectors. *Geographic/strata* Discovery (the "next stratum" retention hook) cannot be validated on a hand-authored MVP mountain — do not read weak long-term-pull signals in MVP playtests as design failures. *Enemy-knowledge* Discovery (learning raider behavior across raids) IS MVP-testable with one raider type and should be measured in MVP playtests.

## Creative Director Notes (CD-PLAYTEST, 2026-07-25 — binding on the named owners; full text in the fun-spike gate review)

- **CD-10 (combat/construction boundary rule)**: Combat may **change** world state, never **author** it. Destruction, debris, door state, trap triggering, and colonist-carried deployables are legal (they mutate/express what exists); placing floors/walls/structures, issuing blueprints, or queuing excavation is illegal inside TurnBased. Design test: *if a proposed combat action would leave a new structure on the grid that no colonist built over time, it is out.* Owners: **Combat set #19–#23** (state the rule + test in Detailed Rules), **Construction #16** (orders unavailable under TurnBased), **Blueprint/Designation UI #26** (inert — not merely hidden — in combat; UX spec says what the player sees when they try).
- **CD-11 (pre-built interactive objects — prefer player-activated over autonomous)**: the escape valve for in-combat cleverness is pre-authored reactivity — objects built in peacetime whose value is realized by a combat decision. Doors are the proven archetype (LOS-blocks-both-ways: safe but blind). Prefer objects a colonist spends an action to activate (doors, levers, drop-gates, collapse charges) over autonomous traps, which hollow out Pillar 2's in-battle half and drift toward tower defense. Owners: **Construction #16** (palette must include combat-activatable objects; doors at MVP minimum), **Combat: Action Economy #20** (activation cost), **#21** (door opening, already owned).
- **CD-12 (deployables are prep expressed spatially)**: legitimate only if ALL of: selected pre-contact in Squad Prep in finite quantity; carried/placed by the holding colonist at/adjacent to their position; costs action economy; never becomes architecture (temporary/removable/destructible, never a terrain-model wall equivalent). **MVP scope guard**: equipment is VS-deferred, so MVP in-combat agency = positioning/cover, doors, destruction only — deployables are designed-for, not built. Owners: **Squad Preparation #24**, **Combat: Action Economy #20**.
- **CD-13 (downed/stabilize, not free revive)**: model "revives" as downed → stabilized: a felled colonist is out of the fight; stabilizing costs another colonist's actions (not-shooting tradeoff = Pillar 2's price half); the stabilized colonist carries a persistent injury/recovery cost into the colony loop (CD-4 death-weight preserved — free revives would violate CD-4). Owners: **Combat set #19–#23** (in-encounter states), **Squad Prep #24** (if loadout-gated), **Colonist Entity #9** (persisted injury via the ADR-0003 `EncounterOutcomeReport`/`PostEncounterReconcile` seam — flag to technical-director), **Notifications** (named survival/death message).
- **CD-14 (raider reactivity is MVP-critical, not content)**: the spike observed the "combat variety at home" risk. Two variety axes: *between-raid* (Raid Trigger breach-point selection, CD-2) and *within-raid* (**Raider Decision-Making #23**). CD-3's objective + withdraw/satisfaction condition is **promoted to a binding MVP acceptance criterion in #23**, plus at least one within-encounter reactive behavior (retarget exposed defenders, decline a proven kill corridor, opportunistic reprioritization). Hard constraints: preserve the emergent cost-based breach pathing exactly (never a scripted "find weak point" routine); solve freshness with behavioral depth, NOT archetype count (one raider type stays — adding types would falsify the MVP hypothesis and blow scope).
- **CD-15 (imperfect threat information is a designed feature, with floor and ceiling)**: never pre-reveal exact breach point or composition (ceiling — full info collapses planning into solving); always reveal approximate timing, rough scale, and general direction (floor — below it, prep degrades into a lottery and Competence goes unserved). Intel accrues across raids and is visible/actionable — the cheapest progression axis in the game and the mechanical form of the playtest's best moment. Owners: **Raid Trigger #18** (information-completeness as an explicit Tuning Knob), **Notifications** (display), **#23** (source data).
- **CD-16 (the pre-contact moment is a designed moment)**: **Squad Preparation #24** stays a quick-spec (no scope inflation) but must carry an acceptance criterion that the prep phase presents at least one meaningful, non-obvious decision under uncertainty (who is drafted, where they stand, door states at entry). **Producer flag**: #24 is the single likeliest quick-spec-to-GDD promotion candidate — watch it, do not pre-promote.
- **CD-17 (Discovery has two vectors)**: applied — see the amended MVP validation limitation above and the game-concept MDA Discovery row. Owner for the mechanical half: **Raid Trigger #18**.
- **CD-18 (lesson-to-answer latency budget)**: the spike's retry pull was purchased with instant/free rebuilding; the real game charges colonist-hours + hauled materials (CD-7). *The likeliest failure mode is not that the hook is unfun — it is that the rebuild loop closes too slowly, decaying Pillar 3 into "you got punished."* **Repair & Rebuild #25 (PROTECTED)** carries a testable acceptance criterion: after a battle, the player can identify the failed choice and have the correction under construction within one session, completing before the next raid at default pacing. Supporting owners: **Construction #16**, **Job Assignment #10** (repair prioritizable above routine labor); raid cadence + rebuild time are ONE tuning problem, routed to **Raid Trigger #18**.

## Next Steps

- [x] CD-SYSTEMS gate (creative-director review of the system set vs pillars) — CONCERNS, 9 notes recorded above, 2026-07-24
- [x] Write ADR-001, ADR-002, ADR-003 as Proposed (`/architecture-decision`) — done 2026-07-24
- [x] Write the cross-cutting contracts annex (one page) — `docs/architecture/cross-cutting-contracts.md`, 2026-07-25
- [x] Build the debug console (Tier 0) — `src/core/Diagnostics/` (plain-C# core) + `src/tools/DebugConsole/` (Godot overlay), 2026-07-25
- [ ] Run `/prototype` — Tier 0 spikes. **Fun spike: DONE 2026-07-25 — PROCEED, CD-PLAYTEST CONFIRM (notes CD-10–CD-18 above)**. **Terrain spike: DONE 2026-07-25 — YES; ADR-0002 validated 5/6 criteria, chunk 32 confirmed, AoS concession retired, GridMap @ octant 32 adopted as render backend; frame-rate clause needs target hardware.** **Mode-switch spike: DONE 2026-07-26 — YES; ADR-0001 validated 61/61 on all 4 testable criteria, recommended for promotion to Accepted; 3 corrections recorded (struct mutation-window scope, normalization decides against the decision set, ADR-0003 reaps ALL raiders).** **Pathfinding spike: DONE 2026-07-26 — YES; ADR-0003 criterion 5 passes 36/36 (mode-aware doors/occupancy, stair Z-linkage, mid-route digs correct, allocation-free A*). Constraint routed to the Pathfinding quick-spec (#8): full region flood fill = 3.30 ms (19.9% of frame) — needs an incremental/deferred/per-layer rebuild trigger, must NOT run per dig. Also routed: diagonal movement (currently 4-connected).** Remaining: save/load
- [ ] After spikes report: begin GDD authoring per the design order (`/design-system terrain-data-model`)
- [x] Route the mid-battle-save question to creative-director before the Combat GDD set — resolved NO (CD-9, 2026-07-24)
