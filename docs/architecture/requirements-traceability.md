# Architecture Traceability Index

> Last Updated: 2026-08-26 — full `/architecture-review` re-run in a fresh session (supersedes the 2026-08-24 hand-patch)
> Engine: Godot 4.7.1 / C# .NET 8
> Source: `/architecture-review` (full mode)
> TR IDs: `docs/architecture/tr-registry.yaml` (v2)

## Coverage Summary

- Total requirements: **97** (46 terrain, 51 time-authority)
- ✅ Covered: **80** (82%)
- ⚠️ Partial: **15** (16%)
- 🔴 Known-wrong: **2** (2%)
- ❌ Gap: **0**

> **Count corrected 2026-08-26.** The previous header read 77 / 20 / 1, which summed to 98
> against 97 requirements; the matrix body actually held 76 ✅ / 21 ⚠️; and the single ❌ was a
> stale duplicate of TR-time-025, closed by ADR-0005 on 2026-08-08 and already marked ✅ in the
> matrix. `architecture.md` §7.4 still repeats the old 77 / 20 / 0 and is owed the same fix.

**Status legend**
- ✅ Covered — an ADR explicitly addresses the requirement.
- ⚠️ Partial — an ADR provides the *foundation hook* but the consuming system is deferred to an as-yet-unwritten quick-spec, OR coverage depends on an open verification gate.
- 🔴 Known-wrong — an ADR addresses the requirement, but its coverage contradicts a later
  ruling or a downstream spec. Distinct from ⚠️: the answer is wrong, not merely absent.
- ❌ Gap — no ADR addresses the requirement.

## Full Matrix

### Terrain (`design/gdd/terrain-data-model.md` → ADR-0002, +0001/0003/0004)

| Req ID | Requirement (short) | ADR Coverage | Status |
|--------|---------------------|--------------|--------|
| TR-terrain-001 | 8-byte cell, floor+wall per cell/Z | ADR-0002 | ✅ |
| TR-terrain-002 | Single shared representation, no conversion at switch | ADR-0001, ADR-0002 | ✅ |
| TR-terrain-003 | Cell holds architecture only (firewall table) | ADR-0002 | ✅ |
| TR-terrain-004 | Single write facade (Set/Clear/Damage/Repair) | ADR-0002 | ✅ |
| TR-terrain-005 | Walls have HP/destructible; floors no HP | ADR-0002 | ✅ |
| TR-terrain-006 | CellState enum incl. debug-asserted Anomalous | ADR-0002 | ✅ |
| TR-terrain-007 | IsPassableTerrain derived; composed by Pathfinding | ADR-0002, ADR-0003 | ✅ |
| TR-terrain-008 | No skill/tool diggability gate | Excavation quick-spec (deferred) | ⚠️ |
| TR-terrain-009 | Dig progress in Excavation-owned side table | ADR-0002 (firewall names owner); Excavation spec | ⚠️ |
| TR-terrain-010 | Mining vs combat independent; WallRemoved invalidates dig | ADR-0002 (event); Excavation (job side) | ⚠️ |
| TR-terrain-011 | Work-position = Moore 8-neighbourhood | Excavation quick-spec (deferred) | ⚠️ |
| TR-terrain-012 | OOB filter before read; reads throw on OOB | ADR-0002 (OOB-throw contract) | ✅ |
| TR-terrain-013 | Dig Stairs Down = one atomic batch | ADR-0002 | ✅ |
| TR-terrain-014 | IsStairFloor retained under a built wall | ADR-0002 | ✅ |
| TR-terrain-015 | One claim per cell, latest-wins, no rollback | ADR-0002 (bit storage); Job Assignment (semantics) | ⚠️ |
| TR-terrain-016 | claim bit == table key invariant | ADR-0002 | ✅ |
| TR-terrain-017 | Designation invalidation via events incl. while paused | ADR-0001, ADR-0002, **ADR-0006** | ✅ |
| TR-terrain-018 | Batched change events w/ previous-state (CD-1) | ADR-0002, **ADR-0006** | ✅ |
| TR-terrain-019 | Map authoring guarantees floor at load | Map Authoring quick-spec (deferred) | ⚠️ |
| TR-terrain-020 | Connectivity: no region reachable only from below | Map Authoring quick-spec (deferred) | ⚠️ |
| TR-terrain-021 | Terrain passive, never ticks | ADR-0001, ADR-0002 | ✅ |
| TR-terrain-022 | Authority-scoped legal writer set | ADR-0002 | ✅ |
| TR-terrain-023 | Every write asserts mutation window | ADR-0001, ADR-0002 | ✅ |
| TR-terrain-024 | Widened arithmetic, reject negatives | ADR-0002 | ✅ |
| TR-terrain-025 | Repair never lowers HP (AlreadyAtMax) | ADR-0002 | ✅ |
| TR-terrain-026 | Zero-delta ops are no-ops, publish nothing | ADR-0002 | ✅ |
| TR-terrain-027 | Max HP/tier from Material Catalog, not per cell | ADR-0002 | ✅ |
| TR-terrain-028 | Deterministic nearest-free displacement | ADR-0002, ADR-0003 | ✅ |
| TR-terrain-029 | Displacement skip void, cancel + notify | ADR-0002 (event); Notifications (deferred) | ⚠️ |
| TR-terrain-030 | Bulk batch validate-all-then-apply | ADR-0002 | ✅ |
| TR-terrain-031 | TerrainChangeBatch ref struct, retention = compile error | ADR-0002 | ✅ |
| TR-terrain-032 | Save/load terrain + claim + dig progress | ADR-0002, ADR-0004; Excavation (dig-progress serialize) | ⚠️ |
| TR-terrain-033 | Snapshot/Restore byte-identical determinism | ADR-0002, ADR-0004 | ✅ |
| TR-terrain-034 | Memory budget 2 MB MVP / 16 MB full-vision | ADR-0002 | ✅ |
| TR-terrain-035 | Chunk 32 == cell_octant_size; facade owns chunk math | ADR-0002 | ✅ |
| TR-terrain-036 | Draw calls ≤150/≤500; ~8 style variants/tier | ADR-0002 | ✅ |
| TR-terrain-037 | Walkability sweep ≤0.5 ms | ADR-0002 | ✅ |
| TR-terrain-038 | Zero steady-state allocation | ADR-0002 | ✅ |
| TR-terrain-039 | Concentrated AoE 75 cells ≤30 µs | ADR-0002 | ✅ |
| TR-terrain-040 | Plain C#, zero Godot, CI-grep | ADR-0002 | ✅ |
| TR-terrain-041 | 3 discrete damage states (breakpoints #7) | ADR-0002. **Breakpoints decided 2026-08-24**: damaged below 0.66, critical below 0.33 of `MaxWallHp`, load-validated as ordered (terrain GDD ownership table) | ✅ |
| TR-terrain-042 | Damage rendering backend | **SUPERSEDED 2026-08-24**: no longer a third overlay GridMap. `design/quick-specs/terrain-rendering-cutaway.md` C7 specifies a **sparse overlay** — one `MultiMeshInstance3D` per damage state, instanced only on damaged cells, plus a rubble kind for destroyed walls. Decouples damage from the material × style multiplier. Draw-call measurement (AC-10) still owed | ⚠️ |
| TR-terrain-043 | Floor MeshLibrary stair item + dormant indicator | Terrain Rendering quick-spec (deferred) | ⚠️ |
| TR-terrain-044 | Pre-render-backend Godot 4.7.1 verification gate | Engine gate (open — `gridmap.md` not authored) | ⚠️ |
| TR-terrain-045 | 60 fps on target hardware | **VERIFIED 2026-08-24** — RTX 3060 Ti, p99 2.167 ms Vulkan / 2.024 ms D3D12 vs 16.6 ms budget, 0 GC. Evidence: `production/qa/evidence/terrain-target-hardware-2026-08-24/` | ✅ |
| TR-terrain-046 | Breach log owned by Combat #22 via EncounterOutcomeReport | ADR-0003; Combat set (deferred) | ⚠️ |

### Time Authority / Mode Switch (`design/gdd/time-authority-mode-switch.md` → ADR-0001, +0003/0004)

| Req ID | Requirement (short) | ADR Coverage | Status |
|--------|---------------------|--------------|--------|
| TR-time-001 | One authority active; full colony pause | ADR-0001 | ✅ |
| TR-time-002 | Systems register authority-scoped with manager | ADR-0001 | ✅ |
| TR-time-003 | Zero state conversion (same instance) | ADR-0001 | ✅ |
| TR-time-004 | Speed multiplies sub-step count, not dt | ADR-0001 | ✅ |
| TR-time-005 | Fixed-dt sub-stepping w/ SubStepCap clamp | ADR-0001 | ✅ |
| TR-time-006 | No catch-up; residual persists in accumulator | ADR-0001 | ✅ |
| TR-time-007 | One accumulator in RealTimeAuthority | ADR-0001 | ✅ |
| TR-time-008 | Multiplicative real↔game time conversion | ADR-0001 | ✅ |
| TR-time-009 | Startup assert SubStepDuration×FrameRate=1 | ADR-0001 | ✅ |
| TR-time-010 | Startup assert SubStepCap ≥ 1 and ≥ max speed | ADR-0001 | ✅ |
| TR-time-011 | Startup guard: engine step-clamp ≥ SubStepCap | ADR-0001. **Verified 2026-08-25** against the `4.7.1-stable` tag: `Engine.MaxPhysicsStepsPerFrame` (default 8), `physics_ticks_per_second` 60. **Read the `Engine` singleton, NOT `ProjectSettings`** — its keys are read only at project start, so a guard reading them validates a stale value (ADR-0001 OQ #9, answered). Building the guard is implementation | ✅ |
| TR-time-012 | Only Raid Trigger in / Combat out; CI gate | ADR-0001 | ✅ |
| TR-time-013 | RequestSwitch atomic, single-encounter invariant | ADR-0001 | ✅ |
| TR-time-014 | Mid-dispatch RequestSwitch → DeferredMidDispatch | ADR-0001 | ✅ |
| TR-time-015 | Multi-breach = one EncounterId | ADR-0001 | ✅ |
| TR-time-016 | Fixed canonical return sequence | ADR-0001 | ✅ |
| TR-time-017 | PostEncounterReconcile duties (once, first) | ADR-0001, ADR-0003 | ✅ |
| TR-time-018 | Survey carrier = drained EncounterOutcomeReport | ADR-0001, ADR-0003 | ✅ |
| TR-time-019 | Three ordered autosave moments | ADR-0001, ADR-0004 | ✅ |
| TR-time-020 | Rolling checkpoint, activation-0, snapshot beat | ADR-0004, ADR-0001 | ✅ |
| TR-time-021 | Load resumes latest; no pre-battle rewind | ADR-0004, ADR-0001 | ✅ |
| TR-time-022 | Non-checkpoint TurnBased save corrupt; manual inert in combat | ADR-0004 (AC-68), ADR-0001 | ✅ |
| TR-time-023 | Checkpoint 8-item content scope | ADR-0004 | ✅ |
| TR-time-024 | Non-blocking async checkpoint write mechanism | ADR-0004 | ✅ |
| TR-time-025 | Checkpoint carries resumable RNG stream state | **ADR-0005 Seeded RNG** (PCG-XSH-RR; `State`-only serialization; combat stream captured at ADR-0004's `AwaitingPresentation → NextActor` beat) | ✅ |
| TR-time-026 | Full determinism across cycle + save/load | ADR-0001, ADR-0004 (state); **ADR-0005** (RNG half). **🔴 Known-wrong**: ADR-0005 derives the Combat stream from `splitmix64(RootSeed, Combat, EncounterId)` specifically to make battle *N* reproduce across a colony save/load. The 2026-08-24 save-scum ruling requires the encounter to **re-roll** on reload. The **cross-save identical-replay clause** is the half in conflict. See the ruling note below | 🔴 |
| TR-time-027 | RNG draws only inside Tick; reload no re-roll | ADR-0001 (draws rule); **ADR-0005** (resume half). **Re-scoped 2026-08-26 — NOT overturned.** This row's "reload resumes **the same battle** with nothing re-rolled" governs the **mid-battle checkpoint resume** path, which restores `State` directly and is unaffected by the derivation key (Open Question 3a, closed 2026-08-02). The save-scum ruling concerns colony-save reload *before* a raid, a different reload point | ✅ |
| TR-time-028 | Pause vs freeze programmatically distinguishable | ADR-0001 | ✅ |
| TR-time-029 | No raid while paused (threat only in real steps) | ADR-0001 | ✅ |
| TR-time-030 | SceneTree.paused forbidden in sim path; grep gate | ADR-0001 | ✅ |
| TR-time-031 | Menu overlay = presentation-only third axis | ADR-0001 | ✅ |
| TR-time-032 | Combat order-writers rejected by writer-set guard | ADR-0001, ADR-0002, ADR-0003 | ✅ |
| TR-time-033 | Notifications queue in combat, flush on return | ADR-0001 (contract); Notifications quick-spec (deferred) | ⚠️ |
| TR-time-034 | In-flight colony orders dropped silently at freeze | ADR-0001 | ✅ |
| TR-time-035 | Zero-elapsed preconditions honored in-dispatch | ADR-0001 | ✅ |
| TR-time-036 | Zero-agency edge cases safe by construction | ADR-0001 (Combat handling deferred) | ✅ |
| TR-time-037 | Manager owns Mode/TurnIndex/TickSequence; dup reject | ADR-0001 | ✅ |
| TR-time-038 | TurnBased systems must not read DeltaSeconds | ADR-0001 | ✅ |
| TR-time-039 | Pathfinding dual-registration (both authorities) | ADR-0001:195 (**Accepted**) lists Pathfinding in the tickable table under both authorities. **🔴 Conflict opened 2026-08-26**: `design/quick-specs/pathfinding-navigation.md` §4 (2026-08-24) states Pathfinding *"registers no `ITickable`, owns no `Tick()`, and advances no state"*. A downstream quick-spec has overturned an Accepted Foundation ADR with no amendment. See the conflict note below | 🔴 |
| TR-time-040 | Pre-switch placement normalization (decision-order) | ADR-0001, ADR-0003 | ✅ |
| TR-time-041 | View-freeze contract on ModeTransitioned | ADR-0001 | ✅ |
| TR-time-042 | Sim only via Tick(); _Process presentation-only | ADR-0001 | ✅ |
| TR-time-043 | Perf budgets: dispatch/swap/reconcile | ADR-0001 | ✅ |
| TR-time-044 | Zero allocation; readonly-struct MutationWindow | ADR-0001 | ✅ |
| TR-time-045 | Quit dialog: verbatim text, quit not default focus | ADR-0004 (flush-and-join); **`design/ux/interaction-patterns.md:180-181`** (affirmative must not hold default focus; `QuitConfirmText` rendered verbatim and tested by equality) and **`design/accessibility-requirements.md:151`** | ✅ |
| TR-time-046 | Quit path flush-and-joins the checkpoint writer | ADR-0004 | ✅ |
| TR-time-047 | Corrupt/missing checkpoint → loud fallback | ADR-0004 | ✅ |
| TR-time-048 | Shared foundation-primitives namespace | ADR-0001, ADR-0002 | ✅ |
| TR-time-049 | Stores passive; health writer split | ADR-0001, ADR-0003 | ✅ |
| TR-time-050 | Speed dial displays requested; divergence signal exists | ADR-0001 (signal); UX spec (display) | ⚠️ |
| TR-time-051 | Battle-length budget; SubStepCap default 8 | ADR-0001 (Combat owns length) | ✅ |

## Known Gaps

**None.** Every one of the 97 requirements has ADR coverage. The last gap (TR-time-025 —
checkpoint RNG stream state) closed on 2026-08-08 with ADR-0005.

*(The previous version of this section still listed TR-time-025 as an open gap, duplicating a
row the matrix above already marked ✅. Removed 2026-08-26.)*

## Known-Wrong Coverage — 2 rows

Distinct from Partial: an ADR **does** address these, and its answer is contradicted by a later
ruling or a downstream spec. These are the review's blocking findings.

### 🔴 TR-time-039 — Pathfinding registration (opened 2026-08-26)

| Source | Claim |
|---|---|
| `time-authority-mode-switch.md:144` | Pathfinding (#8) "Registered under **both** authorities … the notable dual-registration case" |
| **ADR-0001:195** (**Accepted**) | Lists Pathfinding in the tickable registration table, RealTime + TurnBased, with per-authority Tick behaviour columns |
| `design/quick-specs/pathfinding-navigation.md` §4 (2026-08-24) | "Pathfinding is **passive**: it registers no `ITickable`, owns no `Tick()`, and advances no state" |

**Governance note**: this is the same failure mode the 2026-08-24 gate-check named for the
sparse damage overlay — *"a downstream quick-spec had overturned a Foundation ADR's damage
backend with only an open-item note, which is a governance defect regardless of the decision
being right."* That instance became a formal ADR-0002 amendment. This one recurred unrecorded.

**Recommended resolution**: amend **ADR-0001** — remove Pathfinding from the tickable table and
state that it is a passive query service whose cache invalidation runs in its ADR-0006 bus
handler at priority 10, not in `Tick()`. On the merits the quick-spec appears right: ADR-0001's
own row already describes Pathfinding's only duty as "invalidate cached paths/regions on
terrain change," which ADR-0006 now dispatches. Owner: technical-director.

### 🔴 TR-time-026 — combat RNG re-roll vs identical replay (QQ-01)

The 2026-08-24 user ruling on the save-scum hole: reloading a colony save before a raid lets
the player scout the exact breach and composition, then reload and prepare against known
information — the pre-reveal CD-15 forbids. **The encounter re-rolls within its threat band on
reload.**

This contradicts ADR-0005's `splitmix64(RootSeed, Combat, EncounterId)` derivation, which
exists precisely to guarantee cross-save identical replay.

**Scope correction (2026-08-26)**: the previous note claimed this ruling also overturns
TR-time-027. It does not — that row governs mid-battle checkpoint resume, a different reload
point, closed by Open Question 3a on 2026-08-02. Only TR-time-026's cross-save clause is
affected.

**Unstated consequence, recorded here 2026-08-26.** The fix is described repo-wide as "derive
from an encounter attempt counter or equivalent." But an attempt counter persisted **in the
colony save** restores with the save and re-rolls nothing — the exploit survives. To vary per
load-and-retry the counter must deliberately survive **outside** the save file (profile- or
session-scoped). At that point "bit-identical replay from a fixed seed and input sequence" is
no longer true by construction, so **TR-time-026 needs an explicit carve-out, not merely a
different derivation key**, and ADR-0005's validation criterion 3 needs restating.

Owner: technical-director with Raid Trigger (#18). Blocks ADR-0005 promotion.

## Partial Coverage — deferred to unwritten downstream specs

These are **not ADR defects**: the foundation ADR provides the hook; the consuming
system's quick-spec is simply not authored yet (expected under the tiered-doc plan).

- **Excavation**: TR-terrain-008, 009, 010, 011, 015, 032 (dig-progress serialization half)
- **Notifications**: TR-terrain-029, TR-time-033
- **Map Authoring**: TR-terrain-019, 020
- **Terrain Rendering & Cutaway**: TR-terrain-042, 043, 044 — design answered (sparse
  `MultiMeshInstance3D` overlay per damage state), **draw-call measurement still owed**
  (QQ-05 / quick-spec AC-10). TR-terrain-043's dormant-stair indicator is routed to Blueprint
  UI #26; the quick-spec's own AC-15 is marked "blocked on #26"
- **Combat set (#19–23)**: TR-terrain-046
- **UX specs**: TR-time-050 — P7 covers the *display* half; the GDD's "delivered-vs-requested
  **divergence signal** MUST exist" half appears nowhere in `design/ux/` or ADR-0001

*Closed since 2026-08-24*: TR-terrain-041 (breakpoints decided), TR-terrain-045 (target
hardware), TR-time-011 (engine setting verified), TR-time-039 (no longer deferred — now a
conflict), TR-time-045 (UX specs written).

## Superseded Requirements

None. No requirement has been retired; TR-terrain-042's *implementation* was superseded
(third stacked GridMap → sparse overlay) but the requirement itself stands.

## History

| Date | Total | Covered | Partial | Known-wrong | Gap | Notes |
|------|-------|---------|---------|-------------|-----|-------|
| 2026-08-08 | 97 | 74 | 22 | — | 1 | Initial matrix — 4 ADRs, 2 foundation GDDs |
| 2026-08-24 | 97 | 76 | 21 | — | 0 | Hand-patch at gate check (header recorded 77/20/1 in error) |
| 2026-08-26 | 97 | 80 | 15 | 2 | 0 | Full re-run, 6 ADRs. Count corrected; 4 rows closed; TR-time-039 conflict opened; TR-time-026/027 re-scoped |
