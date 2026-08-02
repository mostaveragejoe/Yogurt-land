# Session State

<!-- STATUS -->
Epic: Pre-Production
Feature: GDD Authoring
Task: Designing time-authority-mode-switch GDD — Overview, Player Fantasy, Detailed Design written; Formulas in progress (systems-designer consulted)
<!-- /STATUS -->

## Current Task
Tier 0 FUN SPIKE: **COMPLETE — PROCEED, CD-PLAYTEST CONFIRM (2026-07-25)**.
- Hypothesis CONFIRMED (user debrief); report at prototypes/hollowdeep-fun-spike-concept/REPORT.md (incl. gate verdict + 7 caveats)
- CD notes **CD-10–CD-18** recorded in systems-index (binding on Combat set, Construction, Blueprint UI, Squad Prep, Raider Decision-Making, Raid Trigger, Colonist Entity, Repair & Rebuild, Job Assignment, Notifications)
- Headline decisions: combat changes world state, never authors it (CD-10); player-activated pre-built objects > autonomous traps (CD-11); deployables = prep expressed spatially, VS-era (CD-12); downed→stabilize, no free revives (CD-13); raider reactivity = MVP acceptance criterion in #23 (CD-14); threat-info floor/ceiling + cross-raid intel (CD-15); prep phase must present a real decision (CD-16); Discovery has an MVP-testable enemy-knowledge vector (CD-17, applied to concept doc + index); lesson-to-answer latency budget on #25 (CD-18)
**TERRAIN SPIKE: COMPLETE 2026-07-25 — YES** (`prototypes/terrain-spike/`, SPIKE-NOTE.md).
- 38/38 ADR-0002 contract checks pass. Memory 2 MB MVP / 16 MB full-vision (exactly as predicted); 0.17 B/mutation + 0 Gen0 over 60k mutations (zero-allocation CONFIRMED); full-map sweep 0.290 ms = 1.7% of frame; snapshot 0.61 ms one-shot.
- **AoS falsification test did NOT falsify AoS** — chunked AoS is 21–46% FASTER than flat SoA; ADR-0002's cache-density concession + hot/cold fallback are retired.
- **Chunk size 32 confirmed**; **snapshot = one-shot allocation** (no buffer-reuse machinery).
- **Render backend decided: GridMap + MeshLibrary @ `cell_octant_size = 32`** — 32 draw calls and ~1.85 µs/dig vs MultiMesh's 82 draw calls and ~452 µs/dig (~240×). TerrainWorld stayed authoritative; GridMap is a pure write target (ADR-permitted role).
- **Floor+wall-per-cell: RESOLVED 2026-07-26 — two stacked GridMaps** (wall map + floor map with slab items offset to cell bottom, shared coords/cell size/octant, both fed from TerrainWorld). Cost: **0 extra draw calls** (32 either way), +2.17 MB video mem (14.25→16.42), per-dig unchanged (~2 µs), 2 nodes. Verified in-engine: 15,763 cells carry BOTH floor and wall, matching TerrainWorld exactly (`render_matches_model=True`); cross-section screenshot confirms the floor band. Dig update got simpler (clear wall = one `SetCellItem(pos,-1)`). NOT a data-model defect — TerrainCell always had separate Floor/Wall fields; the limit was purely the render backend, which is what ADR-0002's "GridMap is at most a render backend" rule exists to contain. Generalizes: future per-cell visual layers = more stacked maps. Octant 32 chosen over 64 (8 draw calls) to keep frustum-culling granularity.
- Budgets filled in technical-preferences (draw calls, memory, allocation, renderer + backend).
- **OPEN**: ADR-0002 criterion 5's frame-rate clause — software Vulkan (lavapipe) ran 3–4 fps, meaningless. Re-run `prototypes/terrain-spike/render/` on target hardware to promote ADR-0002 to Accepted. Also unmeasured: GridMap collision cost, procgen sparse chunks.
- Environment note: Godot 4.7.1 mono + .NET 8 SDK + Xvfb/lavapipe all work in this container (Godot at scratchpad `Godot_v4.7.1-stable_mono_linux_x86_64/`).

**MODE-SWITCH SPIKE: COMPLETE 2026-07-26 — YES, 61/61** (`prototypes/mode-switch-spike/`, SPIKE-NOTE.md).
- All 4 testable ADR-0001 criteria pass: headless full turn loop (stub gate, zero Godot — C2 answered); TickSequence gapless across RT→TB→RT + same-seed determinism; speed 0/1/2/3 → 0/60/120/180 sub-steps with dt constant (zero ITickable changes); destroy-terrain-mid-encounter → zero orphaned reservations/jobs/paths AND untouched job+path survive.
- **Zero state conversion proven by identity**: same store instance, same values, unchanged Revision across the swap.
- Cost: dispatch 0.578 µs (0.003% frame), swap 0.31 µs, reconcile 28.9 µs once per battle, **0.00 B/sub-step allocation**.
- **3 corrections**: (1) MutationWindow.Open() must return a `readonly struct` scope — the IDisposable class version boxed 24 B/dispatch, violating the zero-allocation standard (now in technical-preferences); (2) pre-switch normalization must decide against the DECISION SET, not live occupancy — otherwise every co-located unit moves incl. the lowest id that should keep its cell (recorded in ADR-0001 + ADR-0003); (3) **ADR-0003 raider reap leaked** — "dead/withdrawn" leaves live raiders as undespawnable ghosts; corrected to reap ALL raiders at reconcile (ADR-0003 lifecycle row updated).
- **ADR-0001 recommended for promotion to Accepted** — awaiting user decision. ADR-0003 still gated on pathfinding + save/load spikes.
- NOT answered: presentation gating against a real Godot view (stub only); battle length (design, not architecture); post-battle time semantics (still the open CD question — spike confirms fixed-dt keeps both options open).

**PATHFINDING SPIKE: COMPLETE 2026-07-26 — YES, 44/44** (`prototypes/pathfinding-spike/`, SPIKE-NOTE.md).
- ADR-0003 criterion 5 PASSES. Mode-aware composite walkability verified both directions: RT = auto-open doors + occupancy advisory (no traffic deadlock); TB = closed doors and occupied cells block (tactics legality); self never blocks; **broken door unblocks movement AND LOS immediately** (breach lands same turn).
- ADR-0002 stair Z-linkage validated (layers disconnected without stairs; stair links Z↔Z+1 both ways).
- Mid-route digs (the concept doc's open question) answered YES: off-route dig = 1 revalidation/0 recomputes; build on remaining route = exactly 1 recompute routing around; changes behind the cursor don't invalidate; sealed goal = empty path, not stale.
- A* allocation-free (0.00 B/query), deterministic (index-tiebroken heap).
- Cost: A* 10.6–126.3 µs; 10 colonists all repathing in one frame 0.500 ms (3.0%); dig+poll/revalidate 23.8 µs (0.14%).
- **CONSTRAINT FOUND (biggest result)**: full region flood fill = **4.16 ms = 25.1% of a frame** (worse with diagonals) — must NEVER run per dig. Options for Pathfinding quick-spec (#8): incremental updates / deferred-amortized rebuild / per-layer regions (~0.2 ms). Index is already Revision-staleness-aware; only the TRIGGER needs design. Not on the A* path — pathfinding alone is comfortably in budget.
- **Revision polling NOT falsified** (63.2 µs/dig) but cost is O(paths × remaining length) per mutation; ADR-0003's change-list upgrade trigger stated: ~5× growth in colonists/path length/dig rate.
- **MOVEMENT MODEL DECIDED (user, 2026-07-26)**: 8-connected, corner-cutting BANNED, integer octile costs (10 orthogonal / 14 diagonal), octile heuristic (Manhattan is INADMISSIBLE with diagonals — silently loses A* optimality). Implemented in BOTH pathfinder and region index (connectivity must match or reachability lies). Verified: 1-cell-thick diagonal wall SEALS, corner-touching rooms stay disconnected, clearing one orthogonal neighbour legalises the step. Measured cost: A* 1.6x (118.9 vs 75.4 µs), regions 1.4x. 44/44 tests pass.
- **Still routed to quick-spec**: door step cost placeholder (+10 surcharge); TB occupancy blocks traversal vs end-of-move (Combat GDD).

**SAVE/LOAD SPIKE: COMPLETE 2026-07-26 — YES, 24/24** (`prototypes/saveload-spike/`, SPIKE-NOTE.md).
- ADR-0003 criterion 3 + ADR-0001 criterion 2 + cross-cutting contract #2 all validated.
- save→load→save **byte-identical**; **a reloaded world evolves IDENTICALLY to one that never left memory** (200 mutations, fixed input sequence — the decisive test).
- EntityIdSource counter serialized → post-load spawns never collide; dangling ids resolve to nothing. Subtlety recorded: the counter advances across an encounter even though raiders are never serialized — correct (non-reuse holds across the boundary).
- Derived state (occupancy, directory) rebuilt on load and **contributes 0 bytes** — proved by wiping it and re-saving byte-identically.
- **CD-9 structural**: raiders + outcome inbox add ZERO records; save refused in TurnBased; TurnBased save rejected as corrupt on load.
- Catalog evolution safe (id remap); corruption fails loudly (truncation, bad magic, future schema, unknown material).
- Cost: MVP 2.01 MB → **30 KB gzipped (2%)**, write 21.9 ms (~1.2 frames), read 8.1 ms → **no async save machinery needed for MVP**. Full-vision 16 MB / 240 KB gz, ~0.4–0.7 s write — revisit async at Tier 2. **Recommend gzipping the save format from the start.**
- NOT covered: seeded-RNG stream serialization (pending ADR), schema migration path, disk I/O + atomic replace, full compile-time writer-interface segregation (criterion 1's compile-time half asserted by design, not built).

## TIER 0 SPIKE GATE: COMPLETE (5/5)
fun ✅ PROCEED · terrain ✅ · mode-switch ✅ · pathfinding ✅ · save/load ✅

**AWAITING USER DECISION**: promote ADR-0001 and ADR-0003 to Accepted (both recommended). ADR-0002 still needs the frame-rate clause re-run on target hardware (software Vulkan gave no fps signal).

**Next per the design order**: begin GDD authoring — `/design-system terrain-data-model` first (it was explicitly waiting on terrain-spike numbers, which now exist), then Time Authority GDD, then the quick-specs. Fold in: CD-10–CD-18, the pathfinding region-rebuild trigger + movement model, and the measured budgets.

Foundation phase complete beforehand: 3 ADRs (Proposed) + contracts annex + debug console — committed and pushed.

## Progress
- [x] Game concept (design/gdd/game-concept.md) — brainstorm complete, 3 gates passed
- [x] Engine configured: Godot 4.7.1 + C# (was Unity; changed after tile-grid clarification)
- [x] Art bible sections 1-4 (design/art/art-bible.md) — AD-ART-BIBLE gate REVISED/complete
- [x] Systems index (design/gdd/systems-index.md) — 35 entries, 25 MVP; TD-SYSTEM-BOUNDARY + PR-SCOPE gates addressed
- [x] CD-SYSTEMS gate — CONCERNS, 9 notes recorded in index; 4 user decisions made
- [x] ADR-0001 Time Authority (Proposed) — docs/architecture/adr-0001-time-authority-mode-switch.md; forbidden patterns + ADR log added to technical-preferences.md
- [x] ADR-0002 Terrain Data Model (Proposed) — docs/architecture/adr-0002-terrain-data-model.md; 5 forbidden patterns + ADR log entry added to technical-preferences.md; companion edits applied to ADR-0001 (shared-primitives correction, passive-store worked-example row)
- [x] ADR-0003 Entity Data Ownership (Proposed) — docs/architecture/adr-0003-entity-data-ownership.md; gates: godot-specialist PASS, TD-ADR two passes (21 findings closed per pre-approved resolutions). Companion edits applied: ADR-0001 (SwitchPending/PendingSwitchTarget property, reconcile duties, worked-example rows), ADR-0002 (EntityId in shared primitives), technical-preferences.md (5 forbidden patterns + log entry), systems-index.md (doors note, outcome-report pointer, Identity Bookkeeping note)
- [x] Cross-cutting contracts annex — docs/architecture/cross-cutting-contracts.md (one page, three contracts, no fourth; CD-9 recorded as resolved)
- [x] Debug console (Tier 0) — Godot 4.7.1 C# project scaffolded at repo root (project.godot + Hollowdeep.csproj); plain-C# core assembly src/core/Hollowdeep.Core.csproj (Primitives: CellCoord/ChunkCoord/EntityId per ADRs; Diagnostics: DebugConsole core with command + sweep registries, Revision polling); Godot overlay src/tools/DebugConsole/DebugConsoleRoot.cs (autoload "DebugConsole", F12 toggle, history, presentation-only). `dotnet build` green (0 warn/0 err, Godot.NET.Sdk 4.7.1 from NuGet); core zero-Godot grep clean. NOT yet runtime-verified in the Godot editor (no engine binary in this environment) — first editor open should confirm autoload + toggle. .gitignore fixed (Unity leftovers were ignoring *.csproj/*.sln)
- [ ] Tier 0 spikes: fun spike FIRST, then terrain, mode-switch, pathfinding, save/load

## Key Decisions This Session
- Stockpile & Hauling stays MVP (user-mandated, overrides producer recommendation)
- Equipment deferred to VS (MVP = fixed loadouts); strata = data; notifications = shared UI component
- Tiered docs: 8 full GDDs / ~13 quick-specs / 4 ADR-only / 4 UX specs / 1 audio brief (recorded in index)
- Spike-gated sequencing: ADRs + contracts + debug console -> spikes -> GDDs
- Combat split into 5 index entries
- Lighting is aesthetic-only (no gameplay semantics) — art bible Section 1 is authoritative
- ADR-0001 locked: full colony pause in combat; single encounter at a time; push-based ticks; authority-swap only (zero state conversion); fixed-dt sub-steps for speed (delta-scaling banned); CD-9 means no combat serialization in MVP
- ADR-0002 locked: chunked dense grid, packed 8-byte TerrainCell (AoS), per-layer 32×32 chunks (spike-tunable); single TerrainWorld write facade; batched change events with previous-state capture (CD-1); ApplyWallRepair (CD-7); material manifest + schema version for stable-ID saves; mutation-window debug assertion; God-object firewall table; plain C# with zero Godot deps — GridMap is render-backend candidate only
- ADR-0003 locked (3 user decisions): health writer-per-authority (Needs in RealTime, Combat in TurnBased); occupancy exclusive-in-combat-only (advisory in RealTime); doors ARE MVP with the entity boundary contract + provisional destructibility (doors carry Hp, damaged like walls, IsBroken unblocks immediately, reaped at reconcile). Typed stores + writer interfaces at composition root; EntityId (long, monotonic, never reused); combat-transient state in side tables (CD-9 structural); EncounterOutcomeReport via one-slot inbox breaks Combat↔Veterancy; Revision polling, no entity event bus; pre-switch normalization: Squad Prep decides, Colonist Movement executes, deterministic nudge rule

## Files Being Worked On
- docs/architecture/cross-cutting-contracts.md (new — the annex)
- project.godot, Hollowdeep.csproj, src/core/** , src/tools/DebugConsole/** (new — project scaffold + debug console)
- .claude/docs/technical-preferences.md (Project Layout & Autoloads section added)
- design/gdd/systems-index.md (annex pointer; tracker 3/3; next-steps checkboxes)
- .gitignore (removed Unity-era *.csproj/*.sln ignores)
- production/session-state/active.md (this file)

## Open Questions
- Post-battle time: resume as zero-elapsed vs. advance by battle duration — route to creative-director BEFORE Needs & Simulation GDD (both stay possible under ADR-0001's fixed-dt sub-stepping)
- Working-hours assumption (full-time vs evenings) — timeline bands double if part-time
