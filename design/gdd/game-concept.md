# Game Concept: Hollowdeep

*Created: 2026-07-22*
*Status: Draft*

---

## Elevator Pitch

> Carve a colony into a procedurally generated cube-world mountain, layer by layer, in real time. When raiders breach, the game shifts to XCOM / FF Tactics-style turn-based squad combat fought inside your own architecture — every corridor, chokepoint, and hall you carved is the tactics map.

---

## Core Identity

| Aspect | Detail |
| ---- | ---- |
| **Genre** | Colony sim (real-time) + turn-based tactics hybrid |
| **Platform** | PC-first (Steam); other platforms deferred until post-MVP |
| **Target Audience** | Builder-strategists (see Player Profile below) |
| **Player Count** | Single-player |
| **Session Length** | 30–120 minutes |
| **Monetization** | Premium (single purchase); none finalized |
| **Estimated Scope** | Large (multi-year, solo) — see Scope Tiers |
| **Comparable Titles** | Gnomoria, RimWorld, XCOM, Final Fantasy Tactics |

---

## Core Fantasy

**"My home is my weapon."** The player expresses themselves through architecture — the layout, the people, the plans are their canvas — and that expression is not decoration: it gets tested. The colony you sculpted is also your battlefield. Nowhere else does defensive architecture function as a literal tactical skill: the walls you chose, the corridors you shaped, and the colonists you trained decide the battle.

---

## Unique Hook

**Like Gnomoria, AND ALSO your base layout IS the tactics map.**

When combat comes, there is no separate battle screen — the turn-based fight happens inside the architecture the player carved. Chokepoints, sightlines, fallback routes, and material strength are all player-authored. Defensive engineering is an expression system: the answer to destruction is better architecture, not less combat.

The hook is explainable in one sentence, affects gameplay directly, and no shipped game owns this lane.

---

## Player Experience Analysis (MDA Framework)

### Target Aesthetics (What the player FEELS)

| Aesthetic | Priority | How We Deliver It |
| ---- | ---- | ---- |
| **Expression** (self-expression, creativity) | 1 | Blueprint-driven architecture with visible tactical consequences; defensive engineering as a creative medium |
| **Challenge** (obstacle course, mastery) | 2 | Combat events judge colony readiness; in-battle tactics decide the cost |
| **Discovery** (exploration, secrets) | 3 | Descending strata reveal richer materials and worse threats |
| **Fantasy** (make-believe, role-playing) | 4 | The mountain-home commander; named colonists with histories |
| **Narrative** (drama, story arc) | 5 | Emergent stories from battles, scars, and veteran colonists — never authored |
| **Sensation** (sensory pleasure) | 6 | Warm Hearth, Cold Dark lighting; watching plans come alive |
| **Submission** (relaxation, comfort zone) | 7 | Peacetime building rhythm between combat events |
| **Fellowship** (social connection) | N/A | Single-player; community sharing of fortress designs is external |

### Key Dynamics (Emergent player behaviors)

- Players design architecture defensively — chokepoints, kill corridors, fallback halls — without being told to.
- Players scout material composition and upgrade weak walls after seeing where breaches succeed.
- Players form attachments to veteran colonists who survived battles in halls they built.
- Players screenshot and share fortress layouts; base design becomes a comparable, discussable skill.
- After each battle, players study the scars to find the engineering lesson, then rebuild stronger.

### Core Mechanics (Systems we build)

1. **Blueprint carving** — the player designs in menus and blueprints with precision; autonomous colonists execute the work in a diggable, layered tile-grid world (Gnomoria/Dwarf Fortress style: each Z-level is a 2D grid of cells, each cell holding a floor type and, separately, a wall type — not free-form/marching-cubes voxel terrain).
2. **Real-time colony simulation** — needs, jobs, and priorities run without puppeteering; the player directs through plans and orders.
3. **Breach-triggered mode switch** — one shared world with a swappable time authority: real-time colony life becomes turn-based squad tactics on the same grid.
4. **Material-tier destructibility** — architecture is fully destructible, but engineering (dirt → granite → reinforced) mitigates damage; defense scales with player skill and investment.
5. **Strata progression** — deeper layers hold richer materials and worse threats; descent is the frontier.

---

## Player Motivation Profile

### Primary Psychological Needs Served

| Need | How This Game Satisfies It | Strength |
| ---- | ---- | ---- |
| **Autonomy** (freedom, meaningful choice) | The player chooses where, what, and how to build; the mountain is a canvas with no prescribed layout | Core |
| **Competence** (mastery, skill growth) | Battle outcomes measure engineering skill; scars teach legible lessons; preparation visibly pays off | Core |
| **Relatedness** (connection, belonging) | Named colonists with combat histories fight and die in halls they built; deaths must land as stories, not stat losses — this needs deliberate design | Supporting |

### Player Type Appeal (Bartle Taxonomy)

- [x] **Achievers** (goal completion, collection, progression) — How: strata milestones, veteran rosters, engineering tech tiers
- [x] **Explorers** (discovery, understanding systems, finding secrets) — How: descent into generated strata; mastering the interaction of materials, layout, and enemy behavior
- [ ] **Socializers** — Not served in-game; community design-sharing is external
- [ ] **Killers/Competitors** — Not served; no PvP (see Anti-Pillars)

### Flow State Design

- **Onboarding curve**: The first 10 minutes are one small dig order, one room blueprint, and colonists visibly executing it — intent becomes structure immediately.
- **Difficulty scaling**: Threat scales with depth, and depth is player-paced; the player chooses when to descend and when to harden.
- **Feedback clarity**: Battle scars show exactly which engineering choice failed; completed rooms and surviving veterans show what worked.
- **Recovery from failure**: Losses are survivable by design (no full-colony wipe); the rebuild loop turns failure into the next building project.

---

## Core Loop

### Moment-to-Moment (30 seconds)

Review a need → draft or adjust a blueprint → colonists execute → watch the work complete. The drafting table and the ant farm: planning is the verb, execution is the reward. Satisfaction comes from watching intent become structure.

### Short-Term (5-15 minutes)

Complete a room or system — a barracks, a forge hall, a trap corridor. Each completion unlocks the next want. "One more room" is the pull, Civ-style.

### Session-Level (30-120 minutes)

A full cycle of *expand → equip → harden → get tested*. A combat event (or the visible threat of one) punctuates the session. After a battle: assess the scars, rebuild stronger, redesign the weak point. The natural stopping point is the post-battle rebuild — which is also the reason to come back.

### Long-Term Progression

Deeper strata hold richer materials and worse threats. Colonists grow from diggers into veterans with combat history. Construction tech scales from dirt to reinforced stone to engineered defenses. The long-term goal is to reach and survive the deepest stratum — a mastery milestone, not a win-state game-over. The game has no hard end.

### Retention Hooks

- **Curiosity**: What is in the next stratum down? What does the next raider faction bring?
- **Investment**: A colony carved over dozens of hours; veterans whose survival the player engineered.
- **Social**: Fortress layouts as shareable, comparable artifacts (external community).
- **Mastery**: Defensive engineering as a skill with visible report cards — every battle grades the architecture.

---

## Game Pillars

### Pillar 1: The Blueprint Is the Player

The player's architecture is their self-expression, and every system must give design choices visible consequences.

*Design test*: When we debate two features, we choose the one that makes the player's layout matter more.

### Pillar 2: Preparation Gets Tested

Combat exists to judge the colony's readiness — preparation determines whether you can win; in-battle skill determines what it costs you (casualties, structural damage, resources).

*Design test*: When we debate a combat feature, we choose the one where preparation sets the outcome ceiling and in-battle decisions set the price.

### Pillar 3: Scars Teach

Destruction is real, but every loss must leave a legible lesson and an engineering answer — the player must be able to trace which choice failed.

*Design test*: When we debate a destructive event, we keep it only if the player can rebuild stronger against it.

### Pillar 4: The Colony Lives Without You

During colony operation, the player directs through plans and orders, never micromanagement. Combat is the bounded exception: a mode where direct command is earned — you command the defenders the colony trained, in the architecture you carved, but you did not script who survived to fight.

*Design test*: When we debate control schemes, we choose autonomy plus orders in colony mode; direct command exists only inside the combat mode.

### Pillar 5: Depth Is the Frontier

Progression means going deeper — richer materials and worse threats scale downward, not outward.

*Design test*: When we debate new content, we choose content that enriches the descent.

### Anti-Pillars (What This Game Is NOT)

- **NOT real-time or reflex-based combat**: it would compromise *Preparation Gets Tested* — battles judge the colony, not the player's reflexes.
- **NOT an authored, linear story campaign**: it would compromise *The Blueprint Is the Player* — stories must emerge from play.
- **NOT unrecoverable full-colony permadeath**: it would compromise *Scars Teach* — a lesson needs a survivor to learn it.
- **NOT surface empire-building or map conquest**: it would compromise *Depth Is the Frontier*.

---

## Visual Identity Anchor

**Direction: Warm Hearth, Cold Dark**

*(Revised 2026-07-24 during `/art-bible`: the original brainstorm framing of "light is claimed territory" gave lighting gameplay-signal weight the design does not want. Lighting is aesthetic, not mechanic. The authoritative, expanded version of this section lives in `design/art/art-bible.md`, Section 1.)*

**One-line visual rule**: *Light sets the mood. Shape tells your story.*

**Supporting principles**:

1. **The Palette Is the Player's Voice.** Every construction style, ornament set, and finish is available everywhere from day one — the world generates as untouched natural landscape only, and everything built is a player choice. Style is never gated by depth, biome, or progression; only resource, time, or skill cost may restrict it.
2. **Legibility Before Beauty.** Ornament must never obscure the read of a corridor, chokepoint, or breach point — the floor plan is the primary content of every screen.
3. **The Wild Deepens, the Built Doesn't.** Depth and danger are communicated through natural terrain (rock strata, mineral veins, hazard density), never by gating or reskinning player architecture.

**Aesthetic mood**: The Warm Hearth, Cold Dark palette survives as the game's *mood* — warm, lived-in hearth-glow in the colony against the cool dark of unexcavated wilds — but it carries no gameplay semantics: combat, threat state, and repair logic never read or react to lighting, and the player's lighting choices are purely expressive/aesthetic.

**Style vocabularies**: 3D pixel art (low-poly kit-of-parts meshes, chunky nearest-filtered textures at fixed texel density — Gnomoria's pixel-art heritage carried into 3D). Two ornament vocabularies in the building palette, both player-selectable anywhere: Bavarian/German folk construction (fachwerk timber framing, painted folk trim) and stylized classical Roman stonework (arches, pilasters, vaults). Materials look like what they are; warmly lit Roman stone reads warm, exactly like a lit Bavarian plaster wall.

**Feasibility note**: One shared grid-based kit-of-parts with swappable style dressing keeps authoring cost near-linear; pixel textures on simple meshes avoid a PBR sculpting/baking pipeline entirely. Cheapest viable direction for a solo developer.

---

## Inspiration and References

| Reference | What We Take From It | What We Do Differently | Why It Matters |
| ---- | ---- | ---- | ---- |
| Gnomoria | Layered floor-and-wall tile-grid mountain (per-Z-level, not free-form voxel), colony management depth, digging-as-core-verb | Full 3D presentation with a lighting-driven identity; combat becomes a distinct turn-based mode | Validates the fortress fantasy and the diggable-world loop, and gives a proven, simpler-than-voxel data model to build on |
| RimWorld | Real-time autonomous colonists, emergent stories from disaster and recovery | Combat is turn-based tactics on the base itself, not real-time skirmish | Proves the market for colony sims with emergent narrative (multi-million sales) |
| XCOM | Turn-based squad tactics, preparation feeding into missions | The battlefield is the player's own base, not generated mission maps | Proves turn-based tactics sells; supplies the combat grammar |
| Final Fantasy Tactics | Named characters whose growth and loss carry emotional weight; role/class legibility | Characters are simulation-grown colonists, not authored cast | Supplies the model for making unit loss land as story |
| Civilization V | "One more turn" cadence and completion-pull | Applied to room completion in real time, not turns | Supplies the session-pull psychology |

**Non-game inspirations**: Geological cross-section diagrams (the cutaway mountain view); the hearth as the archetype of claimed, safe space against the dark; castle and mine engineering — architecture as recorded decision-making.

---

## Target Player Profile

| Attribute | Detail |
| ---- | ---- |
| **Age range** | 20–45 |
| **Gaming experience** | Mid-core to hardcore; comfortable with systemic depth and light UI complexity |
| **Time availability** | 1–2 hour evening sessions; longer weekend sessions |
| **Platform preference** | PC, mouse and keyboard |
| **Current games they play** | RimWorld, Dwarf Fortress (Steam), XCOM 2, Battle Brothers |
| **What they're looking for** | A colony sim where base design has *provable* consequences — architecture that gets tested, not just admired |
| **What would turn them away** | Reflex-dependent combat; an authored campaign railroad; punishing unrecoverable losses; shallow "builder-lite" simulation |

**Primary player type**: Builder-strategists — the Mastery + Creativity cluster (Quantic Foundry), Achiever-Explorer blend (Bartle). **Secondary appeal**: tactics fans who want battles with stakes beyond the mission. **Who this is NOT for**: action players, authored-story players, and competitive PvP players — all excluded by anti-pillars.

---

## Technical Considerations

| Consideration | Assessment |
| ---- | ---- |
| **Recommended Engine** | Godot 4 (revised via `/setup-engine`; originally scoped for Unity, changed after clarifying the world model — see note below). Pin exact version via `/setup-engine` |
| **Key Technical Challenges** | (1) Layered tile-grid terrain with destructibility — simpler than free-form voxel meshing (Gnomoria/DF-style: floor + wall per cell, per Z-level, no marching cubes or continuous mesh deformation — closer to instanced cube/plane rendering than chunked greedy meshing); (2) pathfinding + job AI in a dynamically diggable 3D grid — needs custom grid pathfinding, engine-agnostic; (3) real-time ↔ turn-based switch over one shared world state — resolved architecturally as "one world, swappable time authority" (first ADR); (4) save/load of a mutable grid + sim world — prove early; (5) layered cutaway/cross-section rendering (Z-level visibility); (6) blueprint/designation UI as a substantial system |
| **Art Style** | 3D stylized low-poly; lighting-driven (Warm Hearth, Cold Dark); neutral modular block kit |
| **Art Pipeline Complexity** | Low-Medium — one-time shader/lighting setup dominates; minimal ongoing asset production |
| **Audio Needs** | Moderate — ambient colony soundscape, combat feedback, warm/cold audio identity to mirror the light rule |
| **Networking** | None (single-player) |
| **Content Volume** | Full vision: one generated mountain per playthrough, 6–8 strata, ~15–20 room/workshop types, 4–6 raider factions; sandbox with no gameplay-hour cap |
| **Procedural Systems** | Procedural mountain/strata generation — deferred to Tier 2; MVP uses a hand-authored mountain |

---

## Risks and Open Questions

### Design Risks

- **The fun hypothesis is unproven**: "Is a turn-based battle inside a base you built actually enjoyable?" is the make-or-break creative bet — addressed by a dedicated greybox fun spike *before* the full sim is built.
- **Expression vs. destruction tension**: players who mainly want to build may resent battle damage to their canvas — mitigated by material-tier engineering (the answer to destruction is better architecture) and *Scars Teach*.
- **Combat variety at home**: if every fight happens in the same base, encounters must stay fresh without new terrain — raider variety and breach-point unpredictability carry this load.
- **Colonist deaths must land as stories, not stat losses** — Relatedness needs deliberate design (names, histories, visible veterancy).

### Technical Risks

*(Full assessment: TD-FEASIBILITY gate, verdict CONCERNS — resolved by re-sequencing. Note: the TD assessment was run against a free-form-voxel assumption; the world model was later clarified as a Gnomoria-style layered tile grid, which reduces several of these risks — see notes below. Revisit formally at `/architecture-review`.)*

- Layered tile-grid terrain with destructibility and cutaway rendering — originally assessed as HIGH assuming free-form voxel meshing; now MEDIUM, since a floor+wall-per-cell grid (no marching cubes, no continuous mesh deformation) is closer to instanced prefab rendering than chunked greedy meshing.
- Colonist AI + pathfinding in a diggable 3D grid; DF-depth simulation is explicitly NOT an MVP target (HIGH).
- Real-time ↔ turn-based mode switch — HIGH, drops to MEDIUM once the "one world, swappable time authority" spike validates the architecture.
- Save/load serialization of a large mutable grid + sim world (HIGH, commonly underestimated).
- Structural collapse simulation — deferred entirely to Tier 2; MVP uses wall-HP by material tier only (MEDIUM as scoped).
- Lighting-heavy direction across many cells (MEDIUM) — start with modest real dynamic lights; flood-fill light values deferred.
- Engine change from Unity to Godot 4 — LOW risk to re-litigate: Godot's gentler learning curve and free-forever licensing suit a first-time solo dev; the earlier Unity Jobs/Burst advantage was weighted for a harder problem (continuous voxel meshing) than this project actually has.

### Market Risks

- The colony-sim audience is proven (RimWorld, DF Steam) but expects depth — a shallow first release could burn the reputation with the exact audience the game serves.
- Two-genre hybrid risks disappointing both audiences if either half feels vestigial — the revised Pillar 2 (prep gates the win, skill gates the cost) exists to keep tactics fans on board.

### Scope Risks

- **First-time solo developer + multi-year DF-class hybrid is the dominant risk** — mitigated by spike sequencing, tier discipline, and honest timeline bands.
- The mode-switch is a **permanent integration tax**: every feature must work under both time authorities. Not removable — budget for it explicitly.
- Voxel engines, sim AI depth, and tactics depth are each bottomless — the MVP caps (one raider type, three needs, wall-HP only) are hard lines.
- Multi-platform ambitions are a scheduling trap — deferred entirely past the vertical slice; route input through Unity's Input System from day one as cheap insurance.
- No performance budget or Unity version is pinned yet — set frame budget and target world size before writing systems (`/setup-engine`, `technical-preferences.md`).

### Open Questions

- Is tactics-in-your-own-base fun? → Answered by the Tier 0 greybox fun spike (turn-based skirmish in a hand-placed destructible room, no sim underneath).
- Does the "one world, swappable time authority" architecture hold? → Answered by the mode-switch spike; written as the first ADR.
- Can an instanced tile-grid renderer (floor + wall prefabs per cell) hit acceptable framerate on a small map with a cutaway/Z-level view? → Answered by the terrain core spike; this is expected to be materially easier than the free-form voxel meshing originally assumed.
- Can pathfinding stay correct and performant while the player digs mid-route? → Answered by the diggable-grid pathfinding spike.
- Does the voxel + agent data model survive a save/load round-trip? → Answered by the serialization spike.
- What exact Unity version, frame budget, and world-size ceiling? → Answered by `/setup-engine` and `technical-preferences.md` before systems are written.

---

## MVP Definition

The MVP answers ONE question: **"Is the core loop fun?"**

**Core hypothesis**: The loop *dig → build → prepare → breach → turn-based battle on my own layout → rebuild stronger* is engaging with even ONE enemy type — because the player's architecture, not content volume, generates the variety.

**Required for MVP** (Tier 1):

1. Hand-authored small mountain, 3 strata; blueprint carving with autonomous colonists (~10) and a food/sleep/work simulation.
2. Three material tiers (dirt → granite → reinforced) with wall-HP destruction — no collapse cascade.
3. One raider type; breach-triggered switch into turn-based tactics fought on the player's layout; rebuild loop afterward.
4. PC / mouse-and-keyboard only; simple dynamic lights.

**Explicitly NOT in MVP** (defer to later):

- Structural collapse/cascade simulation (Tier 2)
- Procedural mountain generation — MVP mountain is hand-authored (Tier 2)
- Flood-fill/propagated lighting (aesthetic ambiance at scale) — simple dynamic lights suffice (Tier 2)
- Multiple raider factions, veteran progression systems (Tier 2)
- Controller support, ports, additional platforms (Tier 3)
- Dwarf-Fortress-depth simulation — three needs and ~5 job types is the MVP cap

### Scope Tiers (if budget/time shrinks)

| Tier | Content | Features | Timeline |
| ---- | ---- | ---- | ---- |
| **Tier 0 — Foundation spikes** | Throwaway/foundation greyboxes | (1) Layered tile-grid dig + cutaway/Z-level rendering with instanced floor+wall prefabs — game question first, optimize only when measured; (2) mode-switch architecture (one world, swappable time authority); (3) pathfinding on a diggable grid; (4) job AI with ~10 colonists; (5) save/load round-trip; (6) **fun spike** — greybox turn-based skirmish in a hand-placed destructible room, no sim underneath | 6–12 months (honest band for a first-time dev) |
| **Tier 1 — MVP** (*ships-if-time-runs-out floor*) | Hand-made small mountain, 3 strata, ~10 colonists, 1 raider type | Full core loop: blueprint carving, needs sim, material-tier wall HP, breach-triggered mode switch, rebuild loop | 12–24 months after Tier 0 |
| **Tier 2 — Vertical Slice / Early Access shape** | Procgen mountains, multiple raider factions | Veteran colonist progression, flood-fill/propagated lighting for rich ambiance at scale (aesthetic only), structural collapse simulation | 12+ months after Tier 1 |
| **Tier 3 — Full Vision** | 6–8 strata with unique materials/threats, 4–6 factions, ~15–20 room types | Deep simulation, additional platforms/ports | Open-ended |

*Gate record: CD-PILLARS — CONCERNS, resolved (Pillars 2 and 4 reworded). TD-FEASIBILITY — CONCERNS, resolved (spike-first re-sequencing, trimmed MVP). PR-SCOPE — OPTIMISTIC, adjustments folded in (fun spike added, voxel spike split, honest timeline bands).*

---

## Next Steps

- [x] Get concept approval from creative-director (CD-PILLARS gate — resolved)
- [ ] Fill in CLAUDE.md technology stack based on engine choice (`/setup-engine` — pin Unity version, URP Forward+, frame budget, world-size ceiling)
- [ ] Create the art bible from the Visual Identity Anchor (`/art-bible`)
- [ ] Validate this document (`/design-review design/gdd/game-concept.md`)
- [ ] **Prototype core idea** (`/prototype`) — run the Tier 0 spikes, starting with the fun spike and the mode-switch spike, before writing GDDs
- [ ] Write the mode-switch ADR: one world, swappable time authority (`/architecture-decision`)
- [ ] If prototypes PROCEED: Decompose concept into systems (`/map-systems`)
- [ ] Design each system (`/design-system [system-name]`) — use spike learnings in Tuning Knobs and Formulas sections
- [ ] Build vertical slice in Pre-Production (`/vertical-slice`) — validate full game loop before committing to Production
- [ ] Validate core loop with playtest (`/playtest-report`)
- [ ] Plan first milestone (`/sprint-plan new`)
