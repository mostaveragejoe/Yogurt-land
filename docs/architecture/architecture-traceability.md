# Architecture Traceability Index

> Last Updated: 2026-08-08
> Engine: Godot 4.7.1 / C# .NET 8
> Source: `/architecture-review` (full mode)
> TR IDs: `docs/architecture/tr-registry.yaml` (v2)

## Coverage Summary

- Total requirements: **97** (46 terrain, 51 time-authority)
- ✅ Covered: **74** (76%)
- ⚠️ Partial: **22** (23%)
- ❌ Gap: **1** (1%)

**Status legend**
- ✅ Covered — an ADR explicitly addresses the requirement.
- ⚠️ Partial — an ADR provides the *foundation hook* but the consuming system is deferred to an as-yet-unwritten quick-spec, OR coverage depends on an open verification gate.
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
| TR-terrain-017 | Designation invalidation via events incl. while paused | ADR-0001, ADR-0002 | ✅ |
| TR-terrain-018 | Batched change events w/ previous-state (CD-1) | ADR-0002 | ✅ |
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
| TR-terrain-041 | 3 discrete damage states (breakpoints #7) | ADR-0002; Material-Tier Destructibility #7 (deferred) | ⚠️ |
| TR-terrain-042 | Damage = third overlay GridMap (no per-instance channel) | Terrain Rendering quick-spec (deferred, engine-gated) | ⚠️ |
| TR-terrain-043 | Floor MeshLibrary stair item + dormant indicator | Terrain Rendering quick-spec (deferred) | ⚠️ |
| TR-terrain-044 | Pre-render-backend Godot 4.7.1 verification gate | Engine gate (open — `gridmap.md` not authored) | ⚠️ |
| TR-terrain-045 | 60 fps on target hardware (unverified) | ADR-0002 criterion 5 (open gate) | ⚠️ |
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
| TR-time-011 | Startup guard: engine step-clamp ≥ SubStepCap | ADR-0001 (engine setting unverified in 4.7.1) | ⚠️ |
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
| TR-time-025 | Checkpoint carries resumable RNG stream state | **Seeded RNG ADR (missing)** — ADR-0004 reserves slot only | ❌ |
| TR-time-026 | Full determinism across cycle + save/load | ADR-0001, ADR-0004 (state); Seeded RNG (RNG half, pending) | ⚠️ |
| TR-time-027 | RNG draws only inside Tick; reload no re-roll | ADR-0001 (draws rule); Seeded RNG (resume half, pending) | ⚠️ |
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
| TR-time-039 | Pathfinding dual-registration (both authorities) | ADR-0001 (mechanism); Pathfinding quick-spec (deferred) | ⚠️ |
| TR-time-040 | Pre-switch placement normalization (decision-order) | ADR-0001, ADR-0003 | ✅ |
| TR-time-041 | View-freeze contract on ModeTransitioned | ADR-0001 | ✅ |
| TR-time-042 | Sim only via Tick(); _Process presentation-only | ADR-0001 | ✅ |
| TR-time-043 | Perf budgets: dispatch/swap/reconcile | ADR-0001 | ✅ |
| TR-time-044 | Zero allocation; readonly-struct MutationWindow | ADR-0001 | ✅ |
| TR-time-045 | Quit dialog: verbatim text, quit not default focus | ADR-0004 (flush); UX spec (dialog focus) | ⚠️ |
| TR-time-046 | Quit path flush-and-joins the checkpoint writer | ADR-0004 | ✅ |
| TR-time-047 | Corrupt/missing checkpoint → loud fallback | ADR-0004 | ✅ |
| TR-time-048 | Shared foundation-primitives namespace | ADR-0001, ADR-0002 | ✅ |
| TR-time-049 | Stores passive; health writer split | ADR-0001, ADR-0003 | ✅ |
| TR-time-050 | Speed dial displays requested; divergence signal exists | ADR-0001 (signal); UX spec (display) | ⚠️ |
| TR-time-051 | Battle-length budget; SubStepCap default 8 | ADR-0001 (Combat owns length) | ✅ |

## Known Gaps

| Req ID | Requirement | Suggested action | Domain | Engine risk |
|--------|-------------|------------------|--------|-------------|
| TR-time-025 | Checkpoint must carry RNG stream state resumable at arbitrary draw counts | `/architecture-decision seeded-rng` (PCG/xoshiro class; streams resumable at arbitrary draw counts) | Determinism / Persistence | LOW |

Partial items TR-time-026 and TR-time-027 are the downstream consequences of the same missing ADR — their RNG-dependent halves close once the Seeded RNG ADR exists.

## Partial Coverage — deferred to unwritten downstream specs

These are **not ADR defects**: the foundation ADR provides the hook; the consuming
system's quick-spec is simply not authored yet (expected under the tiered-doc plan).

- **Excavation**: TR-terrain-008, 009, 010, 011, 015, 032 (dig-progress serialization half)
- **Notifications**: TR-terrain-029, TR-time-033
- **Map Authoring**: TR-terrain-019, 020
- **Terrain Rendering & Cutaway**: TR-terrain-041, 042, 043, 044 (render backend; engine-gated)
- **Combat set (#19–23)**: TR-terrain-046
- **Pathfinding**: TR-time-039
- **UX specs**: TR-time-045 (dialog focus), TR-time-050 (dial display)
- **Engine verification gate**: TR-terrain-044, 045; TR-time-011

## Superseded Requirements

None — this is the first population of the registry.

## History

| Date | Total | Covered | Partial | Gap | Notes |
|------|-------|---------|---------|-----|-------|
| 2026-08-08 | 97 | 74 | 22 | 1 | Initial matrix — 4 ADRs, 2 foundation GDDs |
