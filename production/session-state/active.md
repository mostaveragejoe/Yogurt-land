# Session State

<!-- STATUS -->
Epic: Pre-Production
Feature: GDD Authoring
Task: ADR-0004 Battle Checkpoint Architecture AUTHORED 2026-08-03 (/architecture-decision; Proposed; godot-specialist PASS WITH NOTES + TD-ADR CONCERNS all applied) — next: Seeded RNG ADR (blocking Save/Load #6), then #8 Colonist Entity quick-spec. OPEN: registry update skipped (needs user approval)
<!-- /STATUS -->

## BRANCH CONSOLIDATION 2026-08-06 — READ THIS BEFORE TRUSTING ANY OTHER BRANCH

- **`main` is now the single current design-pipeline state** (commit `316765f`: the l2ld1p tree at `743be86` restored on top of the `55229c4` merge-revert). Start new sessions from `main`.
- **The one-shot playable build lives ONLY on `claude/hollowdeep-one-shot-build-nmytzc`** (tip `be955c9`, includes `dist/` packages + Linux download README). It was never meant to merge into the design pipeline (user decision 2026-08-06, confirming the intent of revert `55229c4`). Do not merge it again.
- **Superseded branches** (fully contained in `main`, keep only for history): `claude/time-authority-mode-switch-l2ld1p` (`743be86`), `claude/terrain-data-model-review-nwhgkg` (`dcb090a`). `claude/terrain-data-model-review-85j1f4` mirrors `main`.
- A 2026-08-06 /design-review run against the stale `dcb090a` GDD copy was discarded as void — every finding was already covered by the two 2026-08-02 passes (see the review log). Lesson: **verify which branch holds the newest review log before re-reviewing.**

## ADR-0004 SESSION 2026-08-03 (/architecture-decision battle-checkpoint-architecture)

- **ADR-0004 written**: `docs/architecture/adr-0004-battle-checkpoint-architecture.md` — **Proposed**. Owns: 8-item checkpoint content scope (from the propagation session), snapshot beat pinned at `AwaitingPresentation → NextActor` + an "activation 0" checkpoint immediately post-swap, Option A mechanism (full self-contained checkpoint, double-buffered pooled snapshot via new `SnapshotInto` caller-buffer obligations on ADR-0002/0003 stores, coalesce-newest backpressure — sim never blocks on disk, async gzip + atomic same-volume temp-file replace), writer quiesce at slot retirement, in-file monotonic save-ordering counter (never mtime), `RestoredFromCheckpoint` resume path (no `RequestSwitch`; restore is a distinct sanctioned writer inside the load window; occupancy rebuild filters dead units), AC-68 provenance via writer-id header enforced at WRITE time, quit-path flush-and-join, loud-fallback corrupt-checkpoint recovery (fall back to switch-in autosave WITH an explicit dialog, never silently).
- Gates: **godot-specialist PASS WITH NOTES** (notes folded in — same-volume rename semantics, thread-pool vs dedicated writer thread, `OS.RequestPermissions` non-issue on PC); **TD-ADR CONCERNS → resolved** (B1–B7 blocking + A1–A10 advisory ALL applied; headline fixes: snapshot beat ambiguity, activation-0 gap, quiesce-on-retirement race, ordering-by-mtime ban, AC-68 moved write-side, gzip thread ownership, `SnapshotInto` named as a contract obligation not an aspiration).
- Promotion gate: shares ADR-0002's re-scoped criterion 5 — checkpoint writes at combat cadence must hold frame rate on target hardware; DO NOT promote ADR-0004 (or ADR-0002) before that run.
- Companion edits: GDD AC-66 re-authored (snapshot-capture wording + coalesce-newest convergence clause) and Rule 9(b) gains the activation-0 sentence; technical-preferences ADR log entry pending→Proposed.
- **OPEN — registry**: `design/registry/` update for ADR-0004 entities NOT done — BLOCKING-gated on explicit user approval; approve or decline next session.

## PROPAGATION SESSION 2026-08-03 (/propagate-design-change time-authority-mode-switch)

- Impact report: `docs/architecture/change-impact-2026-08-03-time-authority-mode-switch.md` — the record of all decisions below.
- TD-CHANGE-IMPACT gate: **CONCERNS → resolved** (user adopted all TD corrections). Key upgrades: ADR-0001 is a contract revision (`TimeAuthoritySnapshot` can't round-trip a battle; load-into-TurnBased must NOT use `RequestSwitch` → `RestoredFromCheckpoint` reason; load-window mode-assertion exemption); ADR-0002 is a direct contradiction + costed budget breach (~150–300 checkpoints/battle × 21.9 ms sync write; terrain replay architecturally unavailable → checkpoint carries full terrain; criterion 5 re-scoped — DO NOT promote ADR-0002 on the old gate); ADR-0003 is an amendment, not superseded (wording constraint: side tables serialized ONLY into the checkpoint by their owners, never colony saves).
- **User decisions 2026-08-03**: (1) revise assessment per TD; (2) structure = small dated Amendments in ADR-0001/0002/0003 (both Accepted ADRs KEEP status — no story auto-blocking) + new **ADR-0004 Battle Checkpoint Architecture**; (3) mechanism = **Option A** (full self-contained checkpoint, double-buffered snapshot ~0.6 ms, async gzip+write ~30 KB, atomic replace).
- Checkpoint content scope completed (GDD's 3-item list was incomplete; 8 items now in the GDD Save/Load dependency row + impact report §3): side tables, TB authority state incl. state machine/current actor, RNG streams, encounter framing, RaiderStore entire, un-reaped IsDead/IsBroken (load never reaps; occupancy rebuild filters dead), terrain full grid, derived-state-never-checkpointed. Plus 2 invariants for ADR-0004: no checkpoint between battle-end and reconcile drain (inbox provably empty); IPresentationGate always idle at checkpoint (post-resolution cadence).
- Cascades recorded: Seeded RNG ADR now BLOCKING for Save/Load #6 (streams resumable at arbitrary draw counts — PCG/xoshiro class); Save/Load #6 grows (rolling slot, atomic replace, latest-wins, async machinery); **design hole routed to creative-director**: colony manual saves still allow pre-raid reload (save-scum via colony save, not quit) — decide before Save/Load #6 spec.
- Files edited: ADR-0001/0002/0003 (Amendment sections + inline retractions), cross-cutting-contracts.md (Contract #2 Battle Persistence bullet), systems-index.md (CD-9 note + annex banner + checklist), technical-preferences.md (forbidden-pattern carve-out + ADR log + ADR-0004 entry), time-authority GDD (Save/Load rows — content list completed).

## RE-REVIEW SESSION 2026-08-02 (second /design-review pass)

- Verdict: NEEDS REVISION (light) → all revisions applied same session. 6 specialists + CD synthesis; all prior fixes (B1–B5, R1–R9) verified held. 5 new blocking (B6–B10) + 6 recommended sweeps applied; 5 nice-to-haves deliberately skipped (logged in review log).
- **BATTLE PERSISTENCE (user ruling, supersedes B6 and overturns CD-9's save half)**: battle autosaves continuously — one rolling non-selectable checkpoint per actor activation, written POST-resolution, tagged Mode==TurnBased, only legal combat-mode save writer. Load always resumes latest → mid-battle relaunch resumes mid-battle at next activation. No pre-battle rewind exists; EC-8 quit-rewind, reload seed question (old OQ #3a), and suspend-to-exit upgrade path all dissolved. Manual saves stay disabled in combat; switch-in + battle-end autosaves kept (3 total). New QuitConfirmText: "Quitting suspends the battle — it will resume exactly where you left off." New ACs 66/67/68; AC-43/45/54 rewritten. CD-9's battle-length half STANDS.
- **PROPAGATION PENDING (user decision: route via /propagate-design-change, NOT edited this session)**: ADR-0001 (TurnBased snapshot support now needed; "combat-mode save is corrupt" narrows to manual-writer saves; "CD-9 banked" consequence obsolete), ADR-0003 (combat-transient "never serialized" needs checkpoint carve-out), cross-cutting-contracts.md (CD-9), systems-index.md (CD-9 note), technical-preferences.md (CD-9 references). Seeded RNG ADR gains checkpoint RNG-stream serialization obligation. Save/Load #6 gains checkpoint scope.
- Other blockers fixed: B7 day-length div-by-zero (multiplicative form added, divisive form marked presentation-only); B8 AC-65 for config guard 3; B9 OQ #10 (N≥5 benchmark preconditions, owner TD, gate: before AC-34/35/36 in CI); B10 survey added to accessibility cross-ref + single-confirm clearability bound.
- Sweeps: Rule 3 honesty pass (throttle-signal obligation to HUD; "never hitches" narrowed); Tuning Knobs (SubStepCap range → …8; "one real lever" reword; speed-aware day-length note); AC hygiene (AC-30 provenance split; testable-now cores in 49/52/54/55/58; AC-60 → (d) pointer; AC-61 split telemetry vs qualitative; AC-36 raider-axis + allocation clauses); transmission gaps (drag-select exception, quit-dialog default focus, first-raid scoping, AC-16 grep scope, OQ #11 view-freeze companion note, OQ #9 widened w/ ADR-0001 "default 8" flag); Pillar 4 tableau sentence; "direct command" AC ownership named to Combat set.
- CD adjudications of record (in review log when appended): F1 pause-cycling ruling upheld but reasoning extended ("pause is cost-free when the alternative was progress; not automatically when the alternative was nothing"); onboarding-vs-advertisement line (U1 granted vs N2 denied); P1 tracking row consistent with OQ #8 ruling.


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

## Session Extract — /architecture-review 2026-08-08
- Verdict: CONCERNS
- Requirements: 97 total — 74 covered, 22 partial, 1 gap
- New TR-IDs registered: 97 (TR-terrain-001..046, TR-time-001..051) — tr-registry.yaml first real population (v2)
- GDD revision flags: None (GDDs already carry the engine-verification gates)
- Top ADR gap: Seeded RNG ADR (missing) — blocks TR-time-025, AC-67 determinism, Save/Load #6, ADR-0004 RNG content
- Cross-ADR conflicts: none open (2026-08-03 Battle Persistence amendment resolved the 3 former contradictions coherently; AC-66 coalesce-newest re-authoring verified consistent)
- Dependency flags: ADR-0003 (Accepted) depends on ADR-0002 (Proposed) — status inversion; ADR-0002 & ADR-0004 share one target-hardware criterion-5 promotion gate
- Engine (godot-specialist): 2 actionable challenges — (1) ADR-0004 use File.Move(overwrite:true) not File.Replace (POSIX atomicity); (2) ADR-0002 third damage-overlay GridMap is a style-variety draw-call multiplier, needs its own spike (not "free" like floor+wall). max_physics_steps_per_frame name/default still unverified in 4.7.1.
- Pre-gate checklist: all 5 items ❌ (tests/unit, tests/integration, tests.yml, accessibility-requirements, ux/interaction-patterns) — /gate-check not yet reachable; needs /test-setup + /ux-design
- Report: docs/architecture/architecture-review-2026-08-08.md · Index: docs/architecture/architecture-traceability.md · Registry: docs/architecture/tr-registry.yaml
