# Change Impact Report — Time Authority / Mode-Switch GDD (Battle Persistence)

**Date**: 2026-08-03 · **Skill**: `/propagate-design-change design/gdd/time-authority-mode-switch.md`
**GDD revision propagated**: commit `29074f5` (2026-08-02 re-review pass — the **Battle Persistence user ruling**)
**Gate**: TD-CHANGE-IMPACT — **CONCERNS → resolved** (all TD corrections adopted by user decision, 2026-08-03)
**User decisions of record (2026-08-03)**: (1) impact assessment revised per the TD's corrections; (2) propagation structured as **small dated Amendments in ADR-0001/0002/0003 + a new ADR-0004 (Battle Checkpoint Architecture)**; (3) checkpoint write mechanism = **Option A — full self-contained checkpoint, double-buffered snapshot on the sim thread, async gzip+write**.

---

## 1. Change Summary

The **Battle Persistence ruling** (user, 2026-08-02) replaces the two-autosave policy (switch-in + battle-end) with three moments: the battle now autosaves continuously — **one rolling, non-selectable checkpoint written after every resolved actor activation**, tagged `Mode == TurnBased`, the sole legal writer of a combat-mode save. Quitting mid-battle suspends the fight; relaunch resumes at the start of the activation following the last resolved one (GDD Rule 9b, EC-8, AC-66/67/68).

This **overturns CD-9's no-mid-battle-save half**. CD-9's battle-length half (8–15 min target, 20 min hard ceiling) **stands unchanged**. Dissolved with it: EC-8's quit-rewind, the reload seed question (old OQ #3a), and the suspend-to-exit post-MVP upgrade path (Battle Persistence *is* suspend-to-exit, generalized and shipped as foundation).

Unchanged sections: Core Rules 1–8 and 10–14, States & Transitions, Formulas D.1/D.2, Tuning Knobs (except the battle-budget rationale note), the freeze/return presentation beats.

## 2. Impact Analysis (TD-corrected classifications)

### ADR-0001 — Time Authority / Mode-Switch — ⚠️ Contract revision (Accepted status retained; amended)

Falsified passages: the Constraints CD-9 line; the Consequences "CD-9 banked" bullet (*"`TurnBasedAuthority` needs NO snapshot support in MVP … a save whose Mode is TurnBased is corrupt"*); the `Restore()` comment (*"Mode is invariantly RealTime in any valid save"*); the GDD-Requirements "CD-9 mode invariant" row; the Related Decisions CD-9 line; the spike regression note "CD-9 refuses to snapshot inside a battle" (now narrowed to non-checkpoint writers, per GDD AC-68).

**Load-bearing contract problem** (TD finding): `TimeAuthoritySnapshot {Mode, TurnIndex, TickSequence}` cannot round-trip a battle. Resume additionally requires the `TurnBasedAuthority` state-machine position, current/next actor, and encounter framing (`EncounterId`, `BreachCells`, `ParticipantIds` — today transient). Two implement-it-wrong-by-default traps recorded for ADR-0004:

- **Load-into-TurnBased is a third entry path into combat mode and must NOT go through `RequestSwitch`** — that would re-fire the switch-in autosave (clobbering the checkpoint with pre-battle state) and re-run ADR-0003's placement normalization (teleporting units mid-battle). Recommended: `Restore` into TurnBased fires `ModeTransitioned` with a distinct reason (`RestoredFromCheckpoint`); Rule 2's "sole requester" invariant and AC-48's grep gate on `RequestSwitch` call sites survive intact.
- **The load window will trip ADR-0003's mode assertions**: a restore in `Mode == TurnBased` writes RealTime-only field groups (Needs, job state). The load window needs an explicit exemption clause or the first checkpoint load fails in debug.

### ADR-0002 — Terrain Data Model — 🔴 Direct contradiction + measured budget breach (Proposed status retained; amended)

Falsified passages: Serialization section (*"Under CD-9, `Snapshot()` runs at the mode-switch autosave"* — it now also runs once per activation) and Spike Results decision 3 (*"one-shot allocation … at a non-gameplay moment; no buffer-reuse machinery is warranted"* — a checkpoint is a gameplay moment inside the turn loop).

**Costed breach** (save/load spike numbers): full MVP save = 2.01 MB, 0.61 ms snapshot, **21.9 ms (~1.2 frames) synchronous write**. Battle Persistence means ~150–300 checkpoints per battle, each landing immediately after an action resolves — under the presentation-gated animation — plus a 2 MB LOH allocation per activation against the zero-steady-state-allocation posture. Resolved by the Option A decision (below).

**"Terrain via mutation replay" is architecturally unavailable**: ADR-0002 rule 9 — the bus has no replay, terrain keeps no journal — and no player-input log exists or is planned. **The checkpoint must carry terrain state.** Under Option A it carries the full grid via the standard snapshot, double-buffered, written async.

**Promotion gate re-scoped**: validation criterion 5's "snapshot buffer strategy at the CD-9 autosave" deliverable is mis-scoped; the target-hardware re-run must measure checkpoint writes at combat cadence. ADR-0002 must not be promoted on the old criterion 5.

### ADR-0003 — Entity Data Ownership — ⚠️ Amendment to an Accepted ADR (not superseded)

The write-ownership table, occupancy index, reservation gating, doors, and `EncounterOutcomeReport` machinery are untouched and remain authoritative. Falsified passages: the Constraints CD-9 line; the Decision-section side-table paragraph (*"never serialized … structural"*); the Allocation Policy line (*"one-shot allocation at the CD-9 autosave moment"*); the Consequences bullet (*"'don't serialize battles' requires no code"*); the save/load spike claims (*"raiders + outcome inbox add ZERO records"*, *"TurnBased save rejected as corrupt on load"* — both now hold only for colony-mode saves); the CD-9 GDD-Requirements row; validation criterion 3's "no combat side table contributes any bytes" clause (colony-save-scoped now).

**Wording constraint (binding on all edits)**: the corrected rule is *"combat-transient state is never in the stores, and is serialized **only into the battle checkpoint by its owning systems** — never into a colony-mode save."* A loose deletion of "never serialized" would read as licence to move combat state into `ColonistStore` and destroy the firewall CD-9 bought.

**Process rule honored**: neither Accepted ADR is demoted to Proposed (stories referencing Proposed ADRs auto-block, per `docs/CLAUDE.md`). Both carry dated Amendment sections with obligations, following ADR-0003's own criterion-1 precedent.

### Non-ADR documents

| Document | Status | Edit |
|---|---|---|
| `docs/architecture/cross-cutting-contracts.md` (Contract #2) | 🔴 | CD-9 bullet rewritten: three autosave moments; TurnBased-tagged saves legal from the checkpoint writer only; side tables checkpoint-serialized but still outside stores |
| `design/gdd/systems-index.md` | ⚠️ | CD-9 note corrected (line ~198); annex banner "resolved: NO" (line ~110); checklist item (line ~222) |
| `.claude/docs/technical-preferences.md` | ⚠️ | Forbidden-pattern carve-out (with the wording constraint above); ADR log entries amended |
| `design/gdd/time-authority-mode-switch.md` | ⚠️ | Save/Load dependency row's checkpoint-content list completed (see §3) — routed back as a GDD correction |

`docs/architecture/requirements-traceability.md` does not exist — no traceability index update.

## 3. Checkpoint content scope (corrected — the GDD's list was incomplete)

The GDD's Save/Load row listed "encounter side tables, TurnBased authority state, combat RNG streams." The complete list, for ADR-0004 to own:

1. **Encounter side tables** (initiative, AP, target locks — serialized by their owning combat systems, into the checkpoint only).
2. **`TurnBasedAuthority` state** — including state-machine position and current/next actor, beyond `{Mode, TurnIndex, TickSequence}` (ADR-0001 contract revision).
3. **Combat RNG streams** — resumable at arbitrary draw counts (Seeded RNG ADR obligation).
4. **Encounter framing** — `EncounterId`, `BreachCells`, `ParticipantIds` (transient today; required for resume).
5. **`RaiderStore` in its entirety** — the "adds zero records" property is colony-save-only now.
6. **Un-reaped `IsDead` colonists and `IsBroken` doors** — legal serialized states for the first time; the `UnitOccupancyIndex` rebuild on load must filter dead units or the TurnBased exclusivity assertion fires on a corpse cell; the load path must NOT reap — reaping remains reconcile's job.
7. **Terrain** — full grid via the standard snapshot (Option A; replay is unavailable, see §2/ADR-0002).
8. **Derived state is still never checkpointed** — `Restore` → `WorldReloaded` → full subscriber rebuild recovers pathfinding caches, stale-job flags, render data. Stated explicitly so ADR-0002 rule 9's "live subscribers' bookkeeping is the only record" is not misread as a reason to serialize caches.

**Ordering invariant (sibling to AC-66, for ADR-0004)**: **no checkpoint is written between battle-end declaration and the reconcile drain** — the `EncounterOutcomeInbox` is therefore provably empty at every checkpoint and needs no serialization; restoring a world holding an undrained report with no live encounter, or double-draining, becomes unrepresentable.
**Presentation invariant (for ADR-0004)**: checkpoints are written post-resolution, so `IPresentationGate` is always idle at checkpoint time — presentation state is never serialized.

## 4. Mechanism decision — Option A (user decision, 2026-08-03)

**Full self-contained checkpoint + async write.** Same schema as any save, plus the combat scope above. Snapshot on the sim thread into a double-buffered pooled buffer (~0.6 ms terrain + kilobytes of entities); gzip (~30 KB at MVP) and write on a background thread with atomic replace. One format, no cross-file coupling, self-validating on load.

Rejected: **B** (delta vs. switch-in autosave — cheapest writes but non-self-contained checkpoints, orphaned-from-base corruption modes, a second serialization path) and **C** (hybrid dirty-chunk terrain — middle ground, still a second terrain path). Accepted cost of A: async save machinery enters MVP scope for Save/Load #6, and ADR-0002's "no buffer-reuse machinery" stance is retired in favour of double-buffering. (Full-vision writes at 0.4–0.7 s forced async before Tier 2 regardless.)

## 5. Cascading effects

- **Seeded RNG ADR** — scope grows: streams must be resumable at arbitrary draw counts (constrains algorithm choice to explicit-serializable-state generators, PCG/xoshiro class; rules out hidden/platform-dependent internal state). Gains a validation criterion (resume-from-checkpoint reproduces an unquit control run — AC-67's technical half) and becomes **blocking for Save/Load #6**.
- **Save/Load (#6) quick-spec** — materially larger: non-selectable rolling slot with atomic replace; checkpoint-vs-manual-slot distinction in format and UX; latest-wins load semantics; async write machinery.
- **Doors / destructibility contract** — no contract change; `IsBroken`/`IsDead` intermediates become serializable and the load path must not clean them up.
- **ADR-0002 promotion gate** — criterion 5 re-scoped (see §2).
- **Design hole routed to creative-director (NOT decided here)**: the GDD's Player Fantasy claims "the pre-battle moment is no longer reachable by any player action" — false while ordinary colony manual saves exist (a player can load a save from five minutes before the raid). The checkpoint closes *quitting*, not *reloading a colony save*. If the answer is single-slot/ironman manual saves, Save/Load #6's scope changes again.

## 6. Resolution decisions

| Document | Resolution |
|---|---|
| ADR-0001 | **Update in place** — dated Amendment (2026-08-03); status stays Accepted; contract-revision items (snapshot shape, resume path, load-window exemption) delegated to ADR-0004 |
| ADR-0002 | **Update in place** — dated Amendment (2026-08-03); status stays Proposed; criterion 5 re-scoped; checkpoint mechanics delegated to ADR-0004 |
| ADR-0003 | **Update in place** — dated Amendment (2026-08-03); status stays Accepted; serialization language corrected under the wording constraint |
| **ADR-0004 — Battle Checkpoint Architecture** | **To be authored** via `/architecture-decision` — owns content scope (§3), cadence, Option A write mechanism (§4), the resume path (`RestoredFromCheckpoint`, no `RequestSwitch`, load-window assertion exemption, occupancy-rebuild corpse filter), and the ordering/presentation invariants |
| cross-cutting-contracts.md / systems-index.md / technical-preferences.md / the GDD itself | **Corrected in the same changeset** as this report |

## 7. Follow-up actions

1. **Author ADR-0004** — `/architecture-decision Battle Checkpoint Architecture` (inputs: §3, §4, ADR-0001's two resume-path traps, the two invariants). Then re-run `/propagate-design-change design/gdd/time-authority-mode-switch.md` to verify coverage.
2. **Author the Seeded RNG ADR** with the resumability requirement — now blocking for Save/Load #6.
3. **Route the manual-save/pre-raid-reload design hole to creative-director** (§5) before Save/Load #6 is specced.
4. **Do not promote ADR-0002** until the re-scoped criterion 5 (checkpoint writes at combat cadence, on target hardware) is measured.
5. **Run `/architecture-review`** after ADR-0004 and the Seeded RNG ADR land, to re-verify the traceability matrix.

**Validation (how we know this was right)**: AC-67's resumed-battle determinism test passes against an unquit control run; checkpoint write cost never appears in the combat frame-time profile; ADR-0003 criterion 6 (no encounter-scoped field in `ColonistStore`) survives the amendment six months on.
