# Tier 0 Mode-Switch Spike — Spike Note

> **Date**: 2026-07-26 · **Validates**: ADR-0001 Time Authority / Mode-Switch (Proposed),
> plus the ADR-0003 contracts that meet it at the seam
> **Result**: **YES — the architecture holds. 61/61 checks pass. Three corrections found, one a real design gap.**

## Question

Does ADR-0001's Time Authority architecture hold — one shared world with a swappable time
authority, **zero state conversion** at the boundary, deterministic, headless — and does the
mode-switch seam (the concept doc's named highest-severity risk, "the hairiest moment")
actually leave zero orphans?

## How it was run

`dotnet run -c Release` in `prototypes/mode-switch-spike/`. Plain .NET 8, no engine.
Implements ADR-0001 verbatim (`TimeAuthorityManager`, `ITickable`, `TimeContext`,
`RealTimeAuthority` fixed-dt sub-stepping, `TurnBasedAuthority` state machine,
`IPresentationGate`, `SwitchTransitionData`, `PostEncounterReconcile`), the ADR-0002 terrain
model (reused from the terrain spike), and enough of ADR-0003 (stores, `UnitOccupancyIndex`,
`StackReservationTable`, `EncounterOutcomeInbox`) to exercise the seam with real systems
registered per ADR-0001's worked-example table.

## Result: YES — 61/61

**Criterion 1 — full turn loop, headless.** The encounter runs end-to-end with the
instant-completion stub gate and **zero Godot types in the assembly**. C2 answered: the
presentation gate did not become a Godot dependency magnet.

**Criterion 2 — determinism and continuity.** `TickSequence` is monotonic and gapless across a
full RealTime → TurnBased → RealTime cycle; same inputs produce identical `TickSequence`,
`TurnIndex`, and entity state; `Snapshot`/`Restore` preserves `{Mode, TurnIndex, TickSequence}`.

**Criterion 3 — speed control touches zero `ITickable`s.** Speeds 0/1/2/3 run 0/60/120/180
sub-steps per 60 frames with **dt constant**; simulated time scales linearly. No system
implementation changed. Speed 0 is the authority at zero sub-steps — never `SceneTree.paused`.
A catastrophic 1-second frame clamps at 8 sub-steps and **drops the backlog: the simulation
slows rather than death-spiralling**.

**Criterion 4 — destroy terrain mid-encounter, resume, zero orphans.** With 2 jobs, 2 cached
paths and 1 reservation live, combat destroyed a job's target wall and a wall a path crossed,
and killed the reservation holder. After the switch back: zero orphaned reservations, zero jobs
targeting destroyed cells or owned by the dead, zero paths crossing destroyed geometry — **and
the untouched job and path survived** (the reconcile is surgical, not a blanket flush). The
dead colonist was reaped in RealTime, `BattlesSurvived` incremented for the survivor only, and
exactly one `EncounterOutcomeReport` was drained from the one-slot inbox.

**Zero state conversion, proven by identity**: across the swap the store is the *same instance*
with the *same values* and an *unchanged `Revision`* — the switch performs no writes at all.

Also confirmed: the inactive authority's systems receive **zero** ticks (full colony pause —
needs froze); dispatch order follows `(phase, priority)` and not registration order; duplicate
phase+priority is rejected; `RequestSwitch` from inside a `Tick` returns `DeferredMidDispatch`
and applies atomically *between* dispatches; a second encounter is **rejected, never queued**;
TurnBased publishes `DeltaSeconds = 0` always while the authority itself still receives real
delta (deliberate, and now regression-locked); CD-9 refuses to snapshot inside a battle;
writer-per-authority health arbitration is structural (each writer is refused in the wrong
mode); occupancy is advisory under RealTime and a hard asserted invariant under TurnBased.

## Cost

| Measure | Result |
|---|---|
| RealTime dispatch (7 systems, 10 colonists) | **0.578 µs/sub-step** = 0.003% of a 16.6 ms frame |
| Same at 3× speed | 0.010% of frame budget |
| RealTime → TurnBased swap | **0.31 µs** (authority swap only) |
| `PostEncounterReconcile` (50 jobs, 50 paths, 20 reservations, 1 death, 3 raiders) | **28.9 µs**, once per battle |
| Dispatch-path allocation | **0.00 B/sub-step**, 0 Gen0 over 20,000 sub-steps |

The mode-switch "integration tax" costs **sub-microsecond wall time**. Its real price is
discipline, exactly as ADR-0001 predicted.

## Three corrections the spike produced

**1. `MutationWindow.Open()` allocated 24 B per dispatch — fixed.** The `IDisposable` scope was
a class, so every `using` boxed a fresh object: 24 B/sub-step, ~4.3 KB/s at 3× speed. Harmless
in isolation but a direct violation of the zero-steady-state-allocation standard that ADR-0001
and ADR-0002 both assert. Changed to a depth-counted **`readonly struct` scope** (`using` on a
known struct type does not box) → measured 0.00 B/sub-step. *Guidance for the production
implementation: the mutation window must be a struct scope, and the allocation assertion belongs
in CI.*

**2. Pre-switch normalization: decide against the DECISION SET, not live occupancy.** My first
implementation asked "is anyone else on my cell?" using the live occupancy index — but during a
decide-only pass every co-located unit still sees the others, so *every* unit concluded it had
to move, including the lowest id. The rule only works when each unit tests against the set of
cells already claimed **by decisions made earlier in the same pass** — that is what makes
ADR-0003's "each move visible to the next" true when Squad Prep decides and Colonist Movement
executes later. With that fix the lowest `EntityId` keeps its cell, the rest are placed
deterministically, and the cell is exclusive before `TurnBasedAuthority` arms its assertion.
*This is an implementation trap the ADR text does not warn about; the production spec should say
it explicitly.*

**3. ADR-0003's raider reap rule leaks — a real design gap.** The Raider Lifecycle row says
`PostEncounterReconcile` despawns **"dead/withdrawn"** raiders. But raiders are *encounter-scoped*:
if a battle ends while a raider is alive and has not withdrawn — a debug/scripted end, or any
future objective-complete end condition — that raider survives into colony time as an
undespawnable ghost (it cannot be despawned inside an encounter, and no later pass reaps it).
**Correct rule: reap ALL raiders at reconcile**, since none may outlive the encounter. Recorded
as an ADR-0003 correction.

## What this spike did NOT answer

- **Presentation gating against a real Godot view.** The stub gate proves the *contract* is
  engine-free; it does not prove a real animation-completion signal integrates cleanly. Re-test
  when the first combat view exists.
- **Battle length** (CD-9's 8–15 min target / 20 min ceiling) — a design measure, not
  architectural.
- **Post-battle time semantics** (zero-elapsed vs. advance-by-battle-duration) — **resolved
  2026-08-07 (user decision): advance-by-battle-duration**, see
  `design/gdd/time-authority-mode-switch.md` Tuning Knobs (`TurnDuration`). The spike's finding
  stands as the reason the decision was free to make either way: fixed-dt sub-stepping kept
  **both** options open, as ADR-0001 claimed.
- **Multi-encounter or save-inside-battle** — deliberately out of scope (CD-9, single-encounter
  invariant), and both are now asserted as refusals rather than untested assumptions.

## Status

Concluded. **ADR-0001 is validated on every criterion this spike can test (1–4; criterion 5 is a
six-month review item).** With correction 3 applied, the ADR-0003 seam contracts are validated
too. Recommend promoting **ADR-0001 to Accepted**; ADR-0003 still awaits the pathfinding and
save/load spikes for its other halves.
