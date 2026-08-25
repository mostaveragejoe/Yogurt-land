# Session State

<!-- STATUS -->
Epic: Technical Setup
Feature: Master architecture document
Task: architecture.md v1.0 WRITTEN and gate-reviewed 2026-08-25 (TD APPROVED WITH CONDITIONS / LP CONCERNS ACCEPTED). Next: close QQ-23..QQ-26 (the four interface gaps), then /create-control-manifest.
<!-- /STATUS -->

## SESSION 2026-08-25 — /create-architecture (COMPLETE)

**Artifact written**: `docs/architecture/architecture.md` v1.0, 720 lines, 11 sections.
The last missing Technical Setup artifact. All 5 gate-check pre-artifacts now exist.

**Two engine-verification gates CLOSED** against the `4.7.1-stable` source tag (the docs
website is egress-blocked; the in-repo class XML is the same source it renders):
- **`docs/engine-reference/godot/modules/gridmap.md` AUTHORED** — the file the 2026-08-08
  architecture review named as blocking `/story-readiness` for render-backend stories.
  **GridMap downgraded HIGH -> LOW risk**: additive-only 4.3->4.7.1, 0 methods removed,
  0 signature changes, 7 new octant methods in 4.7. No per-instance data channel CONFIRMED
  by enumeration (validates the sparse damage overlay). `set_item_mesh_transform` per-item
  CONFIRMED (discharges review finding 4).
- **`modules/physics.md` amended** — `max_physics_steps_per_frame` (default 8) and
  `physics_ticks_per_second` (60) confirmed exactly as ADR-0001 assumed. **ADR-0001 OQ #9
  ANSWERED: read the `Engine` singleton, NOT `ProjectSettings`** — the ProjectSettings keys
  are only read at project start, so a guard reading them validates a value the engine may
  not be using. Binding on TR-time-011.

**Gate outcomes (review mode full, both gates ran)**:
- **TD-ARCHITECTURE: APPROVED WITH CONDITIONS.** Criterion 4 (Foundation gaps resolved) is a
  clear FAIL — ADR-0004/0005 still Proposed, ADR-0003 criterion 1 unbuilt.
- **LP-FEASIBILITY: CONCERNS** (lead-programmer spawned). "Nothing unimplementable... mostly
  fixable by writing down interfaces that are currently only implied by prose."

**Four NEW blocking questions from the gates — QQ-23..QQ-26, all verified against the repo:**
1. **QQ-23 Bus subscriber registration has no interface.** Plus the trap: csproj targets
   `net8.0` = C# 12, so `allows ref struct` (C# 13) is unavailable and `TerrainChangeBatch`
   CANNOT be a generic type argument. `Action<T>`/`EventHandler<T>`/`IObserver<T>` all
   illegal. The natural first idiom will not compile.
2. **QQ-24 The composition root has no type definition** — 30 references across 5 docs, zero
   definitions. The one-writer guarantee rests on it.
3. **QQ-25 Checkpoint multi-owner buffer framing unspecified** — 7 owners, one buffer, no
   ordering/length-prefixing/versioning. Blocks ADR-0004+0005 implementation.
4. **QQ-26 Checkpoint lock boundary + join timeout** — "sim never waits" stated but not
   guaranteed; no timeout on quiesce/quit join.
Plus **QQ-27**: input carries MEDIUM engine risk (4.6 dual-focus, SDL3) with no owner while
all three UX specs are unwritten.

**Session correction worth carrying**: an earlier pass in this session inflated a GridMap
lighting limitation into a "visual identity constraint" and turned the cutaway depth cue into
a blocker. Both were overcorrections and were reverted (commit cd40201). The depth cue is a
build-time tuning job — lower layers stay clearly visible, just de-emphasised — and lighting
is a feature to build, not a design restriction. **User directive: suggest features that
accomplish the vibe; do not manufacture design criteria from engine details.**


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
- Report: docs/architecture/architecture-review-2026-08-08.md · Index: docs/architecture/requirements-traceability.md · Registry: docs/architecture/tr-registry.yaml

## Session Extract — /architecture-decision seeded-rng 2026-08-08
- ADR-0005 Seeded RNG written (Proposed): docs/architecture/adr-0005-seeded-rng.md
- Decisions: PCG-XSH-RR 64/32; named independent streams from one RootSeed (splitmix64-derived, forced-odd Inc); SeededRngStore owner with mode-tagged per-system draw handles; RootSeed from Godot composition root (no entropy in core); combat stream re-derived per encounter from (RootSeed, Combat, EncounterId) for cross-save determinism; combat mid-battle State captured at ADR-0004's AwaitingPresentation→NextActor beat; State-only little-endian serialization (Inc re-derived); two groups mirror the checkpoint-vs-colony-save firewall.
- Reviews: godot-csharp-specialist — 2 BLOCKING fixes folded (force odd Inc; output reads pre-advance state) + refinements (little-endian, State-only, mutable-struct guardrails, unchecked). TD-ADR CONCERNS → all B1–B5 + A1–A6 applied (B2 combat re-derivation and B3 capture-beat were the determinism-critical ones).
- Closes the architecture-review gap TR-time-025 (+ RNG halves of TR-time-026/027); unblocks Save/Load #6. Re-run /architecture-review in a FRESH session to refresh coverage (do not run it in this authoring session).
- Registry: docs/registry/architecture.yaml populated (api_decisions, forbidden_patterns, state_ownership, interfaces). technical-preferences.md: ADR-0005 log entry + forbidden pattern added; Next line updated.
- Promotion: no target-hardware gate on ADR-0005 itself; co-dependent with ADR-0004 on the combat-group boundary, promote together.

## Session Extract — ADR engine corrections 2026-08-08
- Applied the 2 godot-specialist challenges from the architecture review to the ADRs:
- ADR-0004: atomic replace now specifies `File.Move(temp, slot, overwrite: true)` as the cross-platform default (single rename(2) on Unix); `File.Replace` reserved for Windows only (it is NOT atomic on POSIX — backup-file step). Updated in 3 places: Verification Required row, the mechanism bullet, the Risks entry.
- ADR-0002: (1) clarified `SetItemMeshTransform` is a `MeshLibrary` per-item call, not per-cell (GridMap has no per-instance override); (2) added the damage-overlay caveat — a third damage-state overlay GridMap is a style-variety draw-call multiplier vs the ~8-variants-per-tier ceiling, NOT a free flat layer like floor+wall, and needs its own draw-call spike; added as an explicit render-quick-spec open item folded into the TR-terrain-044 verification gate.
- Remaining to PASS: target-hardware criterion-5 run (needs real hardware) to promote ADR-0002/0004/0005. Then re-run /architecture-review in a fresh session.


## Session Extract — quick-spec batch + bookkeeping 2026-08-24

- **Pathfinding & Navigation quick-spec DRAFTED** (`design/quick-specs/pathfinding-navigation.md`, commit `282e8a9`). First document at the quick-spec tier. Resolved the three items the pathfinding spike routed to it:
  - **Region index = per-layer regions, terrain-only, stair cells as portals.** Doors and occupancy deliberately excluded from the index so a door toggle never dirties it. A dig marks only its own layer stale. Rebuild is lazy (first query reading a stale layer triggers it), capped at one layer per dispatch, never inside the mutation window. Full-world flood fill was 4.16 ms / 25.1% of frame; per-layer ~0.26 ms.
  - **Degradation rule (the load-bearing one):** a stale layer with the rebuild budget already spent degrades to authoritative A*, so the index is exact regardless of freshness. Lazy rebuild therefore cannot influence a simulation decision or desync a replay — determinism does not depend on rebuild timing.
  - **Paths are caller-held derived state, never serialized** → Pathfinding needs no row in ADR-0003's write-ownership table. Consistent with the save/load spike (derived state contributed 0 bytes).
  - RealTime congestion cost term deferred **with a named adoption trigger**.
- **ADR-0003 DEFECT FOUND, not yet fixed**: its RealTime composite-walkability formula makes a closed door block, contradicting its own bracketed note *and* the spike's verified behavior (RT auto-opens doors; that is what prevents traffic deadlock). The quick-spec states the verified rule and flags the ADR (§4 flag, §8 item 4) rather than silently overriding an **Accepted** ADR. **Amendment still owed to ADR-0003.**
- **Quick-spec tier template created**: `.claude/docs/templates/quick-spec.md`. Encodes the tier's rules so the remaining three specs do not re-derive them — mandatory "Behavior under each time authority" section, gate-level honesty (Logic BLOCKING / Visual-UI ADVISORY / sibling-blocked criteria must not gate a system's own Done), performance bands set above cited measurements, and a named trigger required for every deferral. Named without a `-template` suffix to match the dominant convention in that directory.
- **Tooling gap recorded**: `/quick-design` targets the same `design/quick-specs/` directory but is a *different document type* — dated, change-oriented, and it explicitly bypasses `/design-review`. Its own redirect rules send anything that "adds a new system that belongs in the systems index" to `/design-system`. So it is the wrong instrument for these four MVP systems, and no skill produces the tier the routing policy defines. The template is the stopgap; a `/quick-spec` skill is the real fix if this recurs.
- **Bookkeeping corrected in `design/gdd/systems-index.md`**:
  - Enumeration rows: #4 Seeded RNG → Specified (ADR-0005); #6 Save/Load → Partial (checkpoint half in ADR-0004; colony-save format still to author); #8 Pathfinding → Drafted + link; #2 Time Authority now records that the `/propagate-design-change` gate blocking its third `/design-review` pass **cleared 2026-08-03**.
  - Progress Tracker was materially wrong: "Tier 0 spikes complete 0/6" → **5/5 GATE COMPLETE**; "ADRs written 3/3" → **5**; design docs 2 → **3**; MVP systems designed → 3/25 documented, plus 2 ADR-specified (ADR-only is a complete tier per the routing policy, not a gap).
  - Design order: ADR-0004 and ADR-0005 added as **unnumbered** rows placed *below* the spike-gate row (unnumbered so existing "design order #N" references stay valid; below the gate so position does not imply they preceded it). Items 6/7/10 statuses set; item 2's stale "final numbers await terrain spike" corrected.
  - Next Steps: spike checkbox ticked; four live items now listed — quick-spec batch, ADR-0003 amendment, target-hardware run, `/test-setup` + `/ux-design`.
- **`production/stage.txt`: `Concept` → `Pre-Production`.** Its git history shows it was written at engine-config time (`4bdc320`), never by `/gate-check`, so this was stale bookkeeping rather than a gate verdict being overwritten. `/project-stage-detect` treats the file as an explicit override, so the stale value was suppressing correct auto-detection for every stage-aware skill. The heuristic (engine configured, 7 `.cs` files < 10) gives Pre-Production. Written with no trailing newline to match `/gate-check`'s `echo -n` convention.
- **Unchanged / still open**: the target-hardware criterion-5 run (needs real hardware — lavapipe gave 3-4 fps, no signal); all 5 `/gate-check` pre-gate artifacts still missing so the phase gate remains unreachable; `design/registry/` update for ADR-0004 entities still awaiting the user's approve-or-decline; post-battle time semantics still routed to creative-director and still needed before the Needs & Simulation GDD; colony-save save-scum hole still needs a creative-director ruling before the Save/Load spec.
- **Minor, untouched**: a skill references `.claude/docs/templates/patch-notes-template.md`, which does not exist in that directory.


## Session Extract — Material Catalog quick-spec 2026-08-24

- **Material Catalog quick-spec DRAFTED** (`design/quick-specs/material-catalog.md`). Second document at the quick-spec tier; used `.claude/docs/templates/quick-spec.md`.
- **Two user decisions taken this session:**
  1. **Catalog owns the numbers, systems own the rules.** `MaxWallHp`, `DigCost`, `BuildCost`, `YieldQty` and `Value` all live in the catalog; Excavation keeps the dig *accumulation rule*, Terrain keeps the HP clamp, Repair keeps CD-7 billing. Rationale: every tier-ordered number in ONE table so the tier-ordering invariant is checkable in one place.
  2. **CD-6 / Pillar 5 = distributional with monotonic expected value.** Deeper strata re-weight toward higher tiers without eliminating lower ones; NOT strict tier gating by stratum (rejected as gamey and colliding with the art bible's "The Wild Deepens, the Built Doesn't").
- **Load-bearing rules:**
  - **C1 — immutable after load, NO writer interface exists at all.** Mutation made unrepresentable rather than governed; this is why §4 is a two-row table and why no mutation-window assertion is needed.
  - **C3 — tier ordering is a hard LOAD FAILURE**, not a convention. `MaxWallHp`/`DigCost`/`Value` strictly increasing, `BuildCost` non-decreasing, or the catalog refuses to load naming the offending material and field. Framed as Pillar 3: an inverted table does not mistune the game, it makes CD-1's after-action report an active lie. This is the cross-check the terrain GDD explicitly handed to #5 with a "this is how invariants die" warning.
  - **C5 — CD-6 made mechanical**: EV(s+1) > EV(s) strictly, PLUS materials never vanish downward (once weighted at stratum s, still weighted below s). Both are pure functions of the weight table, so both are load-validated and unit-testable with no game running (AC-6, AC-7).
  - **C6 — a material may be naturally-occurring or not.** Zero weight in every stratum is legal and excluded from the EV check.
  - **C8 — the catalog draws no randomness.** It exposes the weight table; consumers sample with their own `SeededRngStore` stream (ADR-0005).
- **OPEN QUESTION FOUND — is reinforced mined or manufactured?** The concept doc supports both readings ("deeper strata hold richer materials" vs "construction tech scales from dirt to reinforced stone to engineered defenses"). C6 makes the catalog indifferent so no schema change is needed either way, but the answer decides whether `BuildCost` is a scalar or a recipe. **Routed to Construction (#16) + creative-director; trigger = before #16 is authored.** Recorded in the registry entry for `reinforced` as `naturally_occurring: "UNRESOLVED"`.
- **Deliberate honesty calls:** §6 states first-pass placeholder numbers (HP 100/300/800, Value 1/3/8) rather than omitting them — CD-6's named failure mode is untuned tables, and a table with no numbers cannot be checked or argued with; handed to `/balance-check`. §7b states AC-10/AC-11 as **design budgets, explicitly unmeasured** — the terrain spike's figures include catalog lookups but never isolated them, so there was no measurement to set a band above.
- **REGISTRY POPULATED** (user approved) — `design/registry/entities.yaml`, previously empty `items:`/`entities:`:
  - `items:` dirt / granite / reinforced with full number rows.
  - `formulas:` `stratum_expected_material_value` (CD-6's EV expression).
  - `constants:` `material_tier_ordering` (the Pillar 3 invariant).
  - Also **corrected a stale ownership note** in the formulas section that assigned dig cost to Excavation & Construction — third place that claim lived.
- **DIG-TIME OWNERSHIP — still owed to the terrain GDD.** `design/gdd/terrain-data-model.md`'s Tuning Knobs table still reads as though Excavation owns the dig-time *number*. Substance agrees with the new decision, wording does not. Registry corrected; **the Approved GDD is not**. Trigger: its next `/design-review`, or before #15/#16 — whichever is first.
- **Quick-spec batch status: 2 of 4.** Remaining: Colonist Entity & Attributes (#8, highest fan-out), Terrain Rendering & Cutaway (#11, folds in the ADR-0002 damage-overlay draw-call open item).

## Session Extract — governed-doc debt paydown 2026-08-24

Both debts carried by the two quick-specs are now **CLOSED at source**. Neither was a design change — both were documents disagreeing with decisions already made and already validated.

- **ADR-0003 (Accepted) — RealTime composite-walkability formula CORRECTED.** Its RealTime line read `walkable = IsPassableTerrain(c) ∧ ¬DoorStore.BlocksMovement(c)`, making a closed door block in RealTime. That contradicted **three** things already inside the same ADR: its own bracketed note ("colonists auto-open doors in transit"), its spike-results section ("under RealTime colonists path through closed doors"), and validation criterion 5 — which requires the auto-open behaviour and **passed 44/44**. So the formula was the defect; the behaviour was always right.
  - Corrected in place to `walkable = IsPassableTerrain(c)`, with doors contributing a `DoorTransitSurcharge` traversal cost instead of blocking.
  - Added a `Correction 2026-08-24` block explaining **why it mattered**: doors blocking in RealTime is the traffic-deadlock case. An implementer following the formula literally ships a colony that stalls on its first door, then debugs it as a pathfinding bug.
  - Status line now reads `Amended 2026-08-03 (Battle Persistence) · Corrected 2026-08-24 (RealTime composite-walkability formula)`.
  - **Scope deliberately narrow**: wording correction, NOT a design change — no re-validation, no `/propagate-design-change`, no status change. TurnBased untouched (closed doors still block; opening is an action).
  - Live caveat preserved: if a **door policy that forbids opening** ever ships (CD-16 lists door policy among peacetime standing decisions), RealTime gains a blocking case again and this needs revisiting. Tracked as Pathfinding quick-spec §8 item 3.
- **Terrain Data Model GDD (Approved) — three Tuning Knobs rows corrected**:
  1. *Dig time per material tier* now splits the **number** (Material Catalog `DigCost`) from the **rule** (Excavation's progress accumulation) — the same split the table already applied to wall max HP.
  2. *Dig-completion threshold* notes its operands now live in two places (`progress` in Excavation's side table, `dig cost` in the catalog) while the comparison and completion decision stay Excavation's. Terrain's atomic `ClearWall` guarantee unchanged.
  3. *Tier ordering invariant* row — its warning **"split across two owners with nobody cross-checking direction, which is how invariants die"** is now marked **DISCHARGED**. Both halves (HP and dig cost) live in the catalog, and Material Catalog C3 makes the cross-check a hard load failure with AC-1 as its BLOCKING test. Registered as `material_tier_ordering` so `/consistency-check` can verify it.
- **Debt trackers cleared** so nothing still claims these are owed: Pathfinding §4 flag + §8 item 4, Material Catalog §8 item 2, and both index Next Steps checkboxes.
- **Note on method**: the ADR correction was written as a dated `Correction` block rather than an edit-in-silence, matching the `Amendment 2026-08-03` precedent. An Accepted ADR that quietly changes meaning is worse than one that carries its own errata.

## Session Extract — target-hardware terrain run 2026-08-24

**ADR-0002 criterion 5: frame-rate and Gen0 clauses CLOSED. The ADR stays Proposed.**

- Run by the user on their own PC (this container has no GPU). **RTX 3060 Ti, `software_rasterizer=False`** — timings admissible for the first time.
- **p99 2.167 ms (Vulkan) / 2.024 ms (D3D12)** against the 16.6 ms budget — **~8× headroom**. Mean 650 / 570 fps. **0 Gen0/Gen1/Gen2 collections**, 32.7–36.1 B/frame. Draw calls **32**, exactly as predicted in July. `render_matches_model=True` after 30 s of continuous digging. Dig rebuild 0.30 µs (was 1.85 µs on lavapipe).
- Evidence committed: `production/qa/evidence/terrain-target-hardware-2026-08-24/` (README + both raw logs; Windows username redacted to `<user>`).

**Two findings recorded rather than waved through:**
1. **One ~50 ms frame per run** — 1 in 1800, far beyond p99. Reads as environmental (driver / OS / compositor) since a systematic cost would have lifted p99 off ~2 ms. Not a blocker; **one confirming re-run wanted before Accepted**.
2. **Video memory 43–50 MB vs a recorded 16.42 MB — the label was wrong, not the measurement.** `buffer_mem_mb` reads **16.23 MB**, matching the July figure almost exactly. The July number measures terrain *buffers*; the larger figure is total video memory including render targets and swapchain at real resolution — framebuffer overhead scaling with output resolution, not with terrain. technical-preferences corrected to say "render buffers" and to warn against budgeting it as total video memory.

**Harness work that made the run meaningful** (commit `c6aa68a`): the original bench read `TimeFps` once at frame 60, quit at 62, with vsync on. Replaced with warmup-under-load → untimed draw-call sample → sustained 1800-frame window reporting mean/p50/p95/p99/worst plus GC counts, vsync forced off in two places, adapter name printed with a software-rasterizer VOID warning. The legacy single-read line printed **59.0 fps on a run whose true mean was 650** — it has now been deleted, since sitting next to good numbers it was actively misleading.

**Environment deviations recorded for provenance:** Godot **4.7.2** (project pins 4.7.1 — patch release); **Debug** build, not Release (Godot loads Debug when running a project from its folder). Neither is material at 2 ms against a 16.6 ms budget.

**STILL OPEN — the last gate.** Criterion 5's **checkpoint clause** (added by the 2026-08-03 Battle Persistence amendment): checkpoint snapshot+write at per-activation combat cadence on ADR-0004's double-buffered async path, confirming no frame-time impact during combat. **No implementation exists** — this is a spike in its own right, not a flag on the render bench. ADR-0002/0004/0005 promote together once it lands, then `/architecture-review` in a fresh session.

**Housekeeping:** all of this session's docs were dated 2026-08-20 in error; corrected to **2026-08-24** across 9 files (128 occurrences). Security sweep run at user request — no credentials, keys, `.env` files, IPs or personal identifiers in the repo; the only personal datum was a Windows username in the raw logs, redacted.

## Session Extract — Colonist Entity & Attributes quick-spec 2026-08-24

- **Drafted**: `design/quick-specs/colonist-entity-attributes.md`. Third quick-spec; batch now 3 of 4.
- **User decisions (CD-13 model):**
  1. **Injury is tiered by severity**, each tier with its own recovery path — *Bandaged* (works, reduced movement, heals fully) · *BedRest* (bed + another colonist's tending, then a slowed tail, then full) · *LostLimb* (surgery if a prosthetic exists; **without one, permanently very slow, never heals**).
  2. **Downed uses a bleed-out clock the player can beat**, medics can stabilize in battle, and **downed colonists remain targetable** (this is what gives CD-13's not-shooting tradeoff teeth).
  3. **Death at battle end was rejected by the user as arbitrarily harsh.** A colonist with clock remaining survives the horn.
- **Resulting design — the clock is denominated in SIM-TICKS, not turns.** TurnBased subtracts `TicksPerTurn` at the downed unit's turn start; RealTime subtracts per elapsed tick. Same field, same unit, so there is no conversion step to drift and **the rescue window simply continues into colony time**. A colonist downed late in a fight walks out still downed and still rescuable. AC-5c tests that total bleed-out duration is identical whether the clock ran in one authority or across a switch.
- **Consequence for the firewall**: because the clock outlives the encounter, `IsDowned`/`BleedOutRemaining` are **store state, not a combat side table** — they stay meaningful outside an encounter, so ADR-0003's own rule points them at the store. Genuinely encounter-scoped state (initiative, AP, target locks) is untouched.
- **`MobilityFactor` is the single mechanical expression of injury** — one multiplier on movement speed, nothing else. Keeps injury out of pathfinding *legality*, so ADR-0003's walkability contract is untouched, and it is MVP's only per-colonist variance (skills stay dormant until #30 at VS).
- **ADR-0003 AMENDED same day** (`Amendment 2026-08-24`): `Hp`→0 now sets `IsDowned` not `IsDead` (original clause retracted); health group gains 7 fields; reconcile does NOT resolve downed colonists; downed units still occupy cells and are targetable, only dead are removed from occupancy; **no new writer** — Needs & Simulation already owns RealTime health and takes bleed-out tick, recovery, and reconcile-time injury application.
- **Three systems the model needs that the 35-entry index does not have** — recorded plainly as §8 items 3–5, not treated as blockers: **furniture/beds** (Construction #16), **prosthetics production chain** (no crafting entry anywhere), **tend job** (6th type vs the concept doc's "~5"). All routed to their owning systems' authoring time.
- **Survivability floor** (§8 item 2): targetable downed colonists on a ~10-colonist roster still needs a raider withdraw condition, which CD-3 already assigns to Raid Trigger #18 / Raider Decision-Making #23. The carry-over clock materially softens this — most downed colonists now leave the battle alive.
- **Process note from the user, applied**: stop dramatizing ordinary gaps in an early concept. Missing systems get a line in §8 and the work continues.

## Session Extract — Terrain Rendering & Cutaway quick-spec 2026-08-24 — BATCH COMPLETE

- **Drafted**: `design/quick-specs/terrain-rendering-cutaway.md`. **The quick-spec batch (design order #8–#11) is now 4 of 4.**
- **User decisions:**
  1. **Camera** — quantized rotation (8 compass steps) and quantized zoom (discrete levels), **pitch free within 10°–80°**, plus a button cycling **four canonical (rotation, pitch) presets** as a reset. **This closes the camera question the art bible explicitly deferred**, and with it the dependency chain it flagged: camera → texel density → UI base pixel unit → icon sizes. Art bible Sections 3.1/3.3 re-validation is now unblocked (routed to art-director).
     - Rationale recorded in C6: texel-to-pixel ratio is driven by **zoom distance, not pitch**, so quantizing zoom is what protects the pixel-art crispness and free pitch does not threaten it. The 10° floor keeps layering readable; the 80° ceiling preserves wall height and cover for tactics.
  2. **Damage visuals = sparse overlay, not a third GridMap.** One `MultiMeshInstance3D` per damage state, instanced only on cells currently in that state.
- **Why the damage decision matters architecturally**: ADR-0002 assumed a third stacked map and flagged that GridMap's lack of a per-instance channel makes each damage tier a distinct mesh item **per material/style combo** — trebling the variant count against the measured curve (1 variant → 32 draw calls, 2 → 48, 4 → 80, 8 → 144, ceiling 150). Survivable at MVP's one ornament vocabulary, fatal at the art bible's VS target of two vocabularies plus ornament sets. The sparse overlay **decouples the two axes**: cost bounded by three meshes, instance count scaling with how much is broken rather than world size. ADR-0002's open item updated to record the design answer while keeping the measurement owed (AC-10 / TR-terrain-044).
- **Terrain GDD Open Question #4 CLOSED** (cutaway boundary at stair/void): **read as darkness, window depth stays uniform**, never extended at stair cells — a ragged silhouette would make draw-call count depend on map content, losing the one property that made the octant-32 budget predictable. Safe **only because** the terrain GDD's re-review already routed the dormant-stair-visibility promise to the #26 designation indicator and the inspect view, neither of which depends on cutaway depth. C5 records that this supplements them and never substitutes.
- **Damage breakpoints decided** (the terrain GDD left them to #7): damaged below 0.66, critical below 0.33 of `MaxWallHp`, load-validated as ordered. Terrain GDD tuning row updated.
- Performance ACs set as bands above the 2026-08-24 target-hardware measurements (≤40 draw calls vs 32 measured; p99 ≤4 ms vs 2.02–2.17; ≤20 MB buffers vs 16.23).
- Noted in §1: this is **the only MVP-set spec whose code is Godot-side rather than plain C#** — everything in it is a view over data it never owns.

## Session Extract — /test-setup 2026-08-24

**Test infrastructure scaffolded. 3 of the 5 `/gate-check` pre-gate artifacts now exist.**

- **Framework decision: xUnit, not GdUnit4.** The skill's Godot path defaults to GdUnit4 with a GDScript runner; that is the wrong default here. `src/core/Hollowdeep.Core.csproj` is **Godot-free by contract** (ADR-0001/0002/0003; ADR-0002 makes it a validation criterion), so the core suite runs under a bare `dotnet test` with **no engine installed** — fast, free on CI, no display server. GdUnit4 gets its own project when engine-facing view tests exist; deliberately not set up now.
- **Created**: `tests/Hollowdeep.Tests.csproj` (xUnit + net8.0, `InvariantGlobalization` on for determinism) · `Hollowdeep.sln` (3 projects) · `tests/unit/Primitives/CellCoordTests.cs` (6 real tests, not a stub — value semantics, axis non-interchangeability, dictionary-key usage, and the **Z-down convention pinned** so a reversal fails a test rather than inverting the design language) · `tests/README.md` · `tests/smoke/critical-paths.md` · `tests/integration/` + `tests/evidence/` · `.github/workflows/tests.yml`.
- **Two ADR-mandated CI grep gates added** — these turn rules that lived only in prose into build failures:
  1. **Core is Godot-free** (ADR-0001/0002/0003; ADR-0002 validation criterion 1)
  2. **Core uses no stock/engine RNG** (ADR-0005's forbidden-pattern entry explicitly says "CI-grep gate")
  - **Both greps skip comment lines deliberately, and this was verified against the real tree**: a naive `grep Godot src/core` false-positives on **three doc comments** (`DebugConsole.cs` ×2, `CellCoord.cs` ×1) that legitimately discuss the Godot boundary. The comment-stripped patterns return clean. Tested here before wiring in.
- **technical-preferences slots filled**: Framework = xUnit. **Minimum Coverage = no numeric target, by decision** — coverage is gated by story type via the Testing Standards table (Logic/Integration BLOCKING, Visual/UI advisory); a percentage measures lines executed rather than behaviour pinned and reliably produces tests written for the metric. Revisit only if a story ships Done with evidence a percentage would have caught.
- **`.gitignore`** gained `artifacts/`, `TestResults/`, `*.trx`. Root `Hollowdeep.csproj` already had `<Compile Remove="tests\**\*.cs" />` — it was written expecting this.
- **NOT VERIFIED LOCALLY — no .NET SDK in this container.** The workflow YAML parses and both grep gates were run against the real `src/core`, but `dotnet test` has never executed. First CI run on push is the real check; budget one round for a package-version or namespace fix.
- **Remaining for `/gate-check`**: `/ux-design` must produce `accessibility-requirements` and `ux/interaction-patterns`. Those are the last 2 of the 5.

## Session Extract — /ux-design 2026-08-24 — ALL 5 GATE ARTIFACTS NOW EXIST

Two documents written. **`/gate-check` is reachable for the first time.**

### `design/accessibility-requirements.md` — 11 sections, complete

- **Tier committed: WCAG-AA adapted for games.** Cheap here for three reasons already true: turn-based combat has no reaction floors, colony play pauses at will (speed 0 is first-class), and input already routes through Godot's `InputMap` so remapping is a settings screen not a refactor.
- **Path conflict resolved**: the gate, the skill and the template all use `design/accessibility-requirements.md`; the Time Authority GDD pointed at `design/ux/accessibility-requirements.md`. Canonical file written at the majority path and **the GDD's single reference corrected** (closes the doc's own open question 5).
- **Three constraints inherited from Time Authority, not chosen** — mid-input freeze with no grace window, inert-not-hidden disabled controls, and the after-action survey's no-timeout/no-escape persistence (that GDD's self-named highest lockout risk). This doc owns mitigations and may not relax the rules.
- **Mitigations decided**: interrupted drag is **discarded, never partially committed** (losing the gesture is fine; an unintended designation is not); every modal renders its exit before becoming interactive; click-click rectangle is a **first-class path, not a fallback** — it is also faster for precise corners, which is what stops it rotting.
- **User corrections applied mid-session**: (1) the quit-dialog default-focus rule was **not** generalised to all confirmations — scoped back to where Time Authority ruled it, with generalisation left to each owning spec; (2) digest-over-burst for the notification flush is a **default with exceptions allowed**, not a mandate, since a named colonist death is not a line item in a summary. **Process note: pose extensions as options, do not apply them unilaterally.**
- Screen readers, gamepad navigation, OS high-contrast inheritance, audio downmix and drag-select-as-primary are all in **Known Intentional Limitations** — each with a reason and a revisit trigger. Not-inheriting OS high-contrast is explicitly recorded as a judgement call, since "we meet AA on our own" is an argument games often make badly.
- **Blueprint UI (#26) carries the highest accessibility load in the project** per the feature matrix.

### `design/ux/interaction-patterns.md` — 11 patterns, P1–P11

Authored **ahead of any screen spec** — unusual, but ten patterns were already pinned by the Terrain and Time Authority GDDs, the CD notes, and the accessibility doc. The first UX spec now inherits settled behaviour instead of inventing it. Every pattern carries its accessibility clause inline.

- **Four genuine inventions, flagged as such**: P4's disabled treatment is **dimming + hatch + a reason string on hover/focus**, with disabled controls staying keyboard-focusable so the reason is reachable without a mouse; P7's speed dial gets an **explicit carve-out from the inert-not-hidden rule** (hidden in combat because its whole domain is suspended, not merely unavailable); P3 makes single-cell designation a **toggle**; P2's anchor **survives a mode switch inert** (flagged as open question 3 — may feel wrong in play).
- **P10 + P11 land the dormant-stair handoff** that Terrain Rendering C5 created when it decided below-cutaway reads as darkness. That promise is no longer dangling.
- **Six gaps listed, not pre-solved**: locked-vs-disabled treatment, list/multi-select, drag-drop assignment, tooltips, tab switching, numeric entry. A pattern with no real consumer is a guess.
- P4's hatch is specified as a concept, not as art — it must survive the pixel-art texel grid at every zoom step (art-director, with the art bible 3.1/3.3 re-validation).

### Gate status

All five pre-gate artifacts exist: `tests/unit`, `tests/integration`, `.github/workflows/tests.yml`, `design/accessibility-requirements.md`, `design/ux/interaction-patterns.md`. **Expect `/gate-check` to return CONCERNS rather than PASS** — no screen UX specs exist and `/ux-review` has never run against the new tier.

## Session Extract — gate check + user rulings 2026-08-24

**`/gate-check` ran both gates with all four directors (full mode). Unanimous: Gate 1 (Tech Setup → Pre-Production) CONCERNS; Gate 2 (Pre-Production → Production) FAIL. `stage.txt` NOT advanced.**

### User rulings — binding, do not re-litigate

1. **Prosthetics are an unlockable technology.** Not a scope question, not a cut candidate. `LostLimb` stays in MVP. The gate's cut recommendation is **withdrawn**. Requires two systems the index does not have: **Research/Technology** and a **production chain**.
2. **Camera: one fixed viewing angle, 4 rotations of 90°, quantized zoom.** No pitch adjustment, no preset-cycle control (the four rotations are the presets). Supersedes the earlier free-pitch-10°–80° proposal and dissolves the canonical-authoring-pitch problem — there is exactly one angle to author against.
3. **`BleedOutRemaining` is exempt from post-battle time advance.** A downed colonist must always get a real, playable chance at rescue; the clock must never be consumed by a time-skip. AC-5d added.
4. **Destroyed walls leave rubble** — visual only, blocks nothing, costs nothing to clear. A fourth instance kind in the sparse overlay.
5. **No job-type cap.** The concept doc's "~5 job types" line is removed. Many more are expected.
6. **All art assets are made by a person. No AI-generated art ships.** Standing project rule; art bible **Section 0** + technical-preferences. `/asset-spec` writes briefs for a human artist, never generation prompts.
7. **Flat damage overlays for MVP** — volumetric recorded as the better end state, playtest decides.
8. **Encounter re-rolls within threat band on colony-save reload** (save-scum fix). Time Authority OQ **3b**.
9. **Better ore spawns deeper. Full stop.** Risk comes from distance, hauling, exposure and logistics — emergent from other systems, not modelled in the catalog.
10. **Default to the development plan** for scheduling disruptions like the ADR circularity.
11. No decision yet on mid-battle rebuild or final slice scope — **playtesting decides**.

### Fixes applied this pass

- **ADR-0002 PROMOTED TO ACCEPTED.** Criterion 5 **split**: terrain clauses all measured and passed 2026-08-24; the **checkpoint clause moved to ADR-0004**, where the risk lives. This ends the circular block (ADR-0002 Proposed → needed a checkpoint measurement → needed ADR-0004's async path → building it is a story → auto-blocked by ADR-0002 being Proposed). ADR-0002 gates 11 of 35 systems, so a battle-save clause was freezing the terrain contract. ADR-0004 + ADR-0005 remain Proposed behind the inherited clause.
- **ADR-0002 sparse-overlay supersession recorded as an amendment** — a downstream quick-spec had overturned a Foundation ADR's damage backend with only an open-item note.
- **Traceability renamed** `architecture-traceability.md` → `requirements-traceability.md` (the name the gate globs), 7 inbound refs updated; **4 stale rows patched** (TR-time-025 ❌→✅ via ADR-0005; TR-terrain-045 ⚠️→✅ via the hardware run; TR-terrain-042 marked superseded; TR-time-026/027 now carry the re-roll conflict).
- **CI grep gates PROVEN, not assumed.** Planted violations (`using Godot;`, `new Random(`, `Guid.NewGuid()`) were **all three caught**, and a real core file mentioning Godot in a doc comment was **not** flagged. A gate that has only ever passed is not a gate.
- `coding-standards.md` test command corrected from the nonexistent gdunit4 runner to `dotnet test`.

### Still open

- ~~**`/create-architecture`**~~ — **DONE 2026-08-25**, gate-reviewed. Conditions QQ-23..QQ-26 open.
- **ADR-0005 amendment** for the re-roll ruling (conflicts with TR-time-026/027 identical replay).
- **New systems-index entries**: Research/Technology, production chain, furniture/workstations.
- **Vertical slice** — scoped deliberately below MVP (1 stratum, ~3 colonists, dig+build, one raid), `prototypes/`, ADR-exempt, answering CD-18 only.
- **Capacity assumption** still unconfirmed. Commit history reads part-time (15 commit-days / 32 calendar, one 12-day gap).
- CI still never executed — pushing to `main` this pass to close it.
