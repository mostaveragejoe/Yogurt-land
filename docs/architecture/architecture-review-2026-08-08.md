# Architecture Review Report

- **Date:** 2026-08-08
- **Engine:** Godot 4.7.1 / C# (.NET 8)
- **Mode:** `/architecture-review` full
- **GDDs Reviewed:** 2 — `terrain-data-model.md`, `time-authority-mode-switch.md`
- **ADRs Reviewed:** 4 — ADR-0001 (Accepted), ADR-0002 (Proposed), ADR-0003 (Accepted), ADR-0004 (Proposed)
- **Companion artifacts:** `requirements-traceability.md` (full 97-row matrix), `tr-registry.yaml` (97 stable TR-IDs)

---

## Verdict: **CONCERNS**

Not FAIL — no uncovered foundation-layer requirement, no blocking cross-ADR conflict, dependency graph acyclic.
Not PASS — one missing foundation ADR (Seeded RNG), two ADRs still Proposed behind a single open hardware gate, and two engine corrections to fold into ADR text before implementation.

---

## Traceability Summary

| | Count | % |
|---|---|---|
| Total requirements | 97 | 100% |
| ✅ Covered | 74 | 76% |
| ⚠️ Partial | 22 | 23% |
| ❌ Gap | 1 | 1% |

The four ADRs map cleanly onto the two foundation GDDs (ADR-0001↔time-authority, ADR-0002↔terrain, ADR-0003↔entity ownership feeding both, ADR-0004↔battle checkpoint). **Every technical requirement the two GDDs own at the foundation layer is covered.** Full row-by-row matrix in `requirements-traceability.md`.

### Coverage Gaps (no ADR)

- ❌ **TR-time-025 — checkpoint RNG stream state.** The checkpoint must carry combat RNG streams resumable at arbitrary draw counts (GDD EC-8 / AC-67). ADR-0004 *reserves the content slot* but delegates the stream format to a **Seeded RNG ADR that does not yet exist**.
  - **Suggested ADR:** `/architecture-decision seeded-rng` — a PCG/xoshiro-class stream serializable and resumable at arbitrary draw counts.
  - **Domain:** Determinism / Persistence. **Engine Risk:** LOW (plain-C#, no Godot API).
  - **Blocks:** GDD AC-67 (deterministic resume vs. unquit control), Save/Load quick-spec #6, and the RNG-dependent halves of TR-time-026 / TR-time-027.

### Partial Coverage (foundation hook present; consuming spec deferred)

22 requirements are ⚠️ Partial because the ADR provides the architectural hook but the **consuming system's quick-spec is not yet authored** — expected under the project's tiered-doc plan, not an ADR defect. Grouped by owner: Excavation, Notifications, Map Authoring, Terrain Rendering & Cutaway, Combat set, Pathfinding, UX specs, plus the open engine-verification gates. See the traceability index for the itemized list.

---

## Cross-ADR Conflicts

**None open. No data/state ownership collisions.**

| ADR | Owns |
|-----|------|
| ADR-0001 | Time authority contract; `{Mode, TurnIndex, TickSequence}`; tick dispatch/ordering; mode-switch mechanism; `PostEncounterReconcile`; the mutation-window mechanism |
| ADR-0002 | All terrain cell state; the sole `TerrainWorld` mutation path; the terrain change-event stream; `ChunkOf`/`ChunkSize` |
| ADR-0003 | All entity state (four typed stores); `EntityId`/`EntityIdSource`; `UnitOccupancyIndex`; the write-ownership table; `EncounterOutcomeReport` machinery |
| ADR-0004 | The battle-checkpoint contract (scope/cadence/mechanism/resume/provenance) — orchestrates serialization but owns **no underlying state** (delegates each item to its owner) |

The three points that were once contradictions were resolved coherently, in-place, by the coordinated **2026-08-03 Battle Persistence amendment** applied across ADR-0001/0002/0003 with ADR-0004 as the discharge point:

1. ADR-0002's one-shot-allocation stance vs. per-activation checkpoint cadence → retracted for the checkpoint path; double-buffered async write (colony autosaves keep the one-shot stance).
2. ADR-0003's "combat-transient state is never serialized" vs. the checkpoint carrying side tables → narrowed to "never in a *colony* save; checkpoint-only, by the owning systems."
3. ADR-0001's "`TimeAuthoritySnapshot` round-trips a battle" → retracted; combat-mode resume additionally carries the `TurnBasedAuthority` state machine + current/next actor + encounter framing, and loads via `RestoredFromCheckpoint` (never `RequestSwitch`).

**Consistency verified, not a conflict:** GDD **AC-66** was correctly re-authored (2026-08-03) to match ADR-0004's coalesce-newest mechanism — "exactly one *snapshot* per resolved activation … the on-disk slot converges to the newest resolved state." The GDD and ADR agree.

---

## ADR Dependency Order

Topological order (no cycles — the Combat↔Veterancy cycle is broken by `EncounterOutcomeReport`):

```
Foundation:      ADR-0001 Time Authority            [Accepted]
Depends 0001:    ADR-0002 Terrain Data Model        [Proposed]
Depends 0001+02: ADR-0003 Entity Data Ownership     [Accepted]
Depends 01+02+03+RNG: ADR-0004 Battle Checkpoint    [Proposed]
                 Seeded RNG ADR                     [MISSING]
```

**Two status flags (not cycles):**

- ⚠️ **Status inversion — ADR-0003 (Accepted) depends on ADR-0002 (Proposed).** Per `docs/CLAUDE.md`, stories referencing a Proposed ADR are auto-blocked, so ADR-0003 cannot be fully implemented until ADR-0002 reaches Accepted.
- ⚠️ **ADR-0002 and ADR-0004 share one promotion gate.** Both are blocked on the re-scoped criterion 5: frame rate on target hardware *plus* checkpoint snapshot+write at per-activation combat cadence. One measurement run unblocks both.
- ⚠️ **ADR-0004 depends on the missing Seeded RNG ADR** for content item 3. Its non-RNG scope is implementable first (the ADR says so explicitly), but AC-67 determinism cannot be completed without it.

---

## GDD Revision Flags (Architecture → Design feedback)

**None.** No GDD assumption is *contradicted* by verified engine behaviour. The GDDs already carry explicit verification gates for every engine-uncertain item (the damage-overlay draw-call gate at TR-terrain-044; the `max_physics_steps_per_frame` unverified flag in the config-guard section). Nothing requires a systems-index status change.

---

## Engine Compatibility

Automated audit: **4/4 ADRs have Engine Compatibility sections**, all pin Godot 4.7.1, all declare **"Post-Cutoff APIs Used: None"** in the core assembly (deliberately using .NET `System.IO`, not Godot `FileAccess`). No deprecated-API references. No version disagreement across ADRs.

### Engine Specialist Findings (godot-specialist)

**Positive confirmations:** core-assembly isolation (zero Godot refs) is real and enforced; all GridMap/RenderingServer writes stay on the main thread inside `Tick()` (the checkpoint background thread touches no engine state); `EntityId.Value : long` is Variant-boundary-safe; `ref struct TerrainChangeBatch` correctly stays off the Variant/Signal boundary; the two spike-measured claims (GridMap octant behaviour, allocation numbers) were validated directly against Godot 4.7.1 mono rather than assumed from training data.

**Actionable challenges:**

1. 🔶 **ADR-0004 — `File.Replace` is not atomic on POSIX.** Its .NET Unix implementation uses a backup-file step, not `rename(2)`, which undermines the "a kill at any instant leaves old-or-new, never partial" guarantee the checkpoint relies on. **Recommendation:** use `File.Move(source, dest, overwrite: true)` as the cross-platform default (a direct `rename(2)` on same-filesystem Unix paths); reserve `File.Replace` for Windows if its backup semantics are specifically wanted. Escalate from "verify later" (current ADR Risks entry) to a decision now. .NET BCL fact, not version-dependent.
2. 🔶 **ADR-0002 — the third damage-overlay GridMap is not "free" like floor+wall was.** Floor+wall stayed at 32 draw calls because it added two flat, system-wide layers. Damage has no per-instance channel (confirmed), so it must be discretized into N damage-tier *mesh items per material/style combo* — a **style-variety multiplier** against the measured ~8-variants-per-tier draw-call ceiling, not a flat +1 layer. It needs its own draw-call spike before being treated as settled; the existing 2-GridMap spike numbers do not cover it.
3. ⚪ **ADR-0001 — `max_physics_steps_per_frame`** setting name/default and the runtime-read API surface (`ProjectSettings.GetSetting` vs. an `Engine` singleton property) are genuinely **unverified in 4.7.1** (already self-flagged by the ADR; reference docs are silent). The fixed-timestep math `SubStepDuration == 1/physics_ticks_per_second` is confirmed sound.
4. ⚪ **Minor — ADR-0002 text precision:** clarify that `SetItemMeshTransform` is a `MeshLibrary` (per-item) call, not per-cell, so a future reader cannot misread it as a per-instance runtime override (which would contradict the no-per-instance-channel finding).
5. ⚪ **Minor — teardown paths:** confirm any "return to main menu mid-battle without quitting the process" flow (scene reload / SceneTree root swap) also joins the checkpoint writer, or a background write can outlive the scene that spawned it. Route to Save/Load #6.

### Confirmed engine assumptions

| Assumption | Verdict |
|-----------|---------|
| GridMap has no per-instance data/shader channel → damage needs a stacked overlay | CONFIRM (ADR-0002) |
| `cell_octant_size == ChunkSize == 32`; octant rebake still octant-granular in 4.7 | CONFIRM (spike-measured on 4.7.1) |
| 60 fps for stacked GridMaps at MVP size | UNVERIFIED — target-hardware run outstanding (ADR-0002 criterion 5) |
| `_Process` presentation-only vs `Tick()` sim; no `SceneTree.paused` in sim | CONFIRM (version-agnostic) |
| Checkpoint IO via .NET `System.IO`/`GZipStream`, not Godot `FileAccess` | CONFIRM the direction; CHALLENGE `File.Replace` (see finding 1) |

### Open engine verification gates (documented, not blind spots)

- Author `docs/engine-reference/godot/modules/gridmap.md` (version-pinned) before render-backend work; `/story-readiness` returns BLOCKED for render-backend stories until it exists.
- Target-hardware 60 fps + checkpoint-at-cadence run (ADR-0002 criterion 5 / ADR-0004 shared gate).
- Confirm `max_physics_steps_per_frame` name/default/read-API in 4.7.1 (technical-director, OQ #9).
- Confirm cross-platform atomic-replace choice (finding 1).

---

## Architecture Document Coverage

`docs/architecture/architecture.md` and `docs/architecture/control-manifest.md` do **not** exist yet — expected, since `/create-architecture` and `/create-control-manifest` have not been run. No orphaned architecture. These become relevant at the Pre-Production → Production transition.

---

## Blocking Issues for PASS

1. Author the **Seeded RNG ADR** (closes TR-time-025 and the RNG halves of 026/027; unblocks Save/Load #6 and AC-67).
2. Run the **target-hardware criterion-5** measurement → promote **ADR-0002** and **ADR-0004** to Accepted (also clears the ADR-0003 status inversion).
3. Apply the **`File.Move(overwrite:true)`** correction to ADR-0004's write mechanism.
4. Spike the **damage-overlay draw calls** against the style-variety ceiling before ADR-0002's render backend is treated as settled.

---

## Required ADRs (priority order)

1. **Seeded RNG ADR** — Foundation; blocks Save/Load #6 and battle-checkpoint determinism.
2. *(no other net-new ADR required for the current foundation scope — remaining coverage is downstream quick-specs, not ADRs.)*

---

## Handoff

**Immediate actions**
1. `/architecture-decision seeded-rng` (top gap, Foundation).
2. Schedule the target-hardware criterion-5 run → promote ADR-0002 & ADR-0004.
3. Fold the two engine corrections (finding 1 `File.Move`; finding 2 damage-overlay spike) into ADR-0004 / ADR-0002.

**Pre-gate checklist** (all ❌ — `/gate-check` is not yet reachable):
- ❌ `tests/unit/` — run `/test-setup`
- ❌ `tests/integration/` — run `/test-setup`
- ❌ `.github/workflows/tests.yml` — run `/test-setup`
- ❌ `design/accessibility-requirements.md` — run `/ux-design`
- ❌ `design/ux/interaction-patterns.md` — run `/ux-design`

**Rerun trigger:** re-run `/architecture-review` after the Seeded RNG ADR is written to confirm coverage closes to 0 gaps.
