# Change Impact Report — Time Authority / Mode-Switch (CD-19 / CD-20)

| Field | Value |
|---|---|
| **GDD** | `design/gdd/time-authority-mode-switch.md` |
| **Date** | 2026-08-07 |
| **Trigger** | Two creative-director rulings (user-decided 2026-08-07): **CD-19** post-battle time = turn-derived with cap (overrides the CD recommendation of zero-elapsed); **CD-20** save-scum = raid-window save lock + reload re-roll + Commitment Mode opt-in (adopts the CD recommendation). Binding text: systems-index CD notes. |
| **Run** | `/propagate-design-change time-authority-mode-switch`, review mode full |
| **TD-CHANGE-IMPACT gate** | **CONCERNS** — draft classifications corrected per the TD (ADR-0001 and ADR-0004 were under-classified; ADR-0005 finding confirmed and strengthened; one unowned requirement surfaced; one GDD fidelity error caught and fixed same day). This document records the post-gate classifications. |

## 1. Change Summary (GDD revision landed 2026-08-07, commit `b695c5a` + fidelity fix)

- **Rule 8**: canonical return sequence gains the CD-19 colony-time catch-up step — strictly after `PostEncounterReconcile`, strictly before the battle-end autosave. Rate/cap and decay guardrails are Needs & Simulation #13's.
- **Rule 9**: manual-save lock widened from "in combat" to "raid warning through battle-end"; save-and-quit during the window writes a **non-selectable quit slot that Continue resumes** (CD-20 binding text — restored after the TD gate caught the initial edit paraphrasing it into "ordinary autosave rotation"); reload of any pre-raid save re-rolls the raid (breach + composition, within CD-15's ceiling); **Commitment Mode** introduced (per-colony, default off, immutable).
- **Player Fantasy**: "single sitting" paragraph replaced with the adopted honest restatement (no more "no pre-battle rewind by any player action" absolutism); "clock comes back" paragraph states colony time catches up.
- **EC-8** narrowed (quitting specifically); **EC-12** added (older-save reload is legal, re-rolled, costed); **AC-69–73** added; **AC-10/44/59** revised; Needs and Save/Load dependency rows updated; **OQ #1 closed**.

## 2. Impact Analysis (post-gate classifications)

### ✅ Not Affected — ADR-0002 (Terrain), ADR-0003 (Entities)
Neither references this GDD; CD-19's catch-up runs through the existing RealTime writer set (no new terrain mutation path, no new entity writer group); CD-20 lives in UI/Save-Load/Raid-Trigger territory. **TD note of record**: CD-19's clemency clause is *load-bearing, not tuning* — a starvation death during catch-up would land after reconcile with no second cleanup pass behind it; the clause is what makes "no new reconcile pass" safe.

### ⚠️ Needs Review — ADR-0001 (Time Authority / Mode-Switch) — reclassified from "Still Valid" by the TD
CD-19's "ordinary RealTime sub-steps, exactly like any other colony dispatch" is not quite achievable as written:
- **AC-70(d)** requires Job Assignment to re-plan once, not per sub-step — an ordinary dispatch re-plans per tick.
- **ADR-0001's worked-example table has Raid Trigger accumulating threat every RealTime `Tick()`** — ordinary catch-up sub-steps could therefore fire a *second raid* between reconcile and the battle-end autosave (Rule 6's single-encounter rejection no longer applies — the encounter has ended), with `RequestSwitch`'s `DeferredMidDispatch` landing at a point Rule 8 never reasoned about.
- **Conclusion**: catch-up is a **constrained dispatch** — a concept ADR-0001 does not yet have. The amendment must define it (minimum: threat accumulation suppressed or deferred during catch-up; single re-plan semantics; `RequestSwitch` deferral behavior across the catch-up window).
- Staleness: ADR-0001's Consequences line still calls the question "open" (its OQ section already carries the closure note); **GDD AC-27** asserts threat stays at its *post-reconcile* value after battle-end — stale or correct depending on the constrained-dispatch decision (if threat is suppressed during catch-up, AC-27 stands as written; if deferred-then-applied, it needs rewording). Resolve at amendment time.

### ⚠️ Needs Review — ADR-0004 (Battle Checkpoint) — reclassified from "doc-sync" to substantive by the TD
1. §2 prose ("reconcile → battle-end autosave") is one step stale against Rule 8 — genuine doc-sync, invariant unbroken (the checkpoint is quiesced and retired before reconcile begins).
2. **Substantive**: CD-20's non-selectable RealTime quit slot is a **third slot class ADR-0004's taxonomy cannot express** — §5 keys legality/selectability off `Mode == TurnBased` + the checkpoint writer-id; a RealTime-tagged, non-selectable, Continue-resumed slot fits neither the checkpoint class nor the ordinary selectable class. The amendment must add it (and Save/Load #6 inherits the implementation).
3. Its "Related Decisions" open hole ("colony manual saves still allow pre-raid reload — decide before Save/Load #6") and routed correction #3 (the honest restatement) are **resolved by CD-20** — mark them closed.
4. **Open sub-question for the amendment** (TD recommends *no*, pending user ruling): does the reload re-roll apply to the corrupt-checkpoint fallback? That raid never *resolved*; disk failure is not save-scumming.

### ⚠️ Needs Review — ADR-0005 (Seeded RNG) — confirmed by the TD, and sharper than drafted
**The direct conflict**: ADR-0005's exact-state-capture determinism means a restored `RaidTrigger` stream draws the *identical* next values — a reloaded pre-raid save would reproduce the **same** raid, which is precisely what CD-20 forbids. Worse, ADR-0005 §5 explicitly pins `WorldSeed` as "never regenerated on load," and its Validation Criterion 8 makes *post-restore raid-roll identity with the control run* a correctness requirement — the naive fixes are all explicitly ruled out by the ADR itself.
**TD corrections to the draft finding**:
- The ADR-0001 objection was overstated: ADR-0005 §4 already sanctions load-window draws by name (`ColonistIdentity` embark, `MapGeneration`) — the enforcement primitive permits a load-window draw; what's missing is only a named mechanism and rule, a cheaper fix than drafted.
- **Scope discipline is the hard requirement**: divergence must apply to the **colony-save-reload path only** — never to battle-checkpoint restore (AC-67) and never to the same-slot Continue path — or VC-8/AC-67 break and deterministic resume dies with them.
**Standing warning until amended**: Raid Trigger #18 must NOT implement its reload behavior against ADR-0005 as currently written.

### 🔴 Unowned requirement (TD finding — no current ADR owns this)
CD-20 needs **per-colony state that survives outside any single save file**:
- a **reload/divergence epoch that advances per load event** — without it, reloading the same slot twice re-rolls to the *same* "different" raid, recreating the re-roll-slot-machine foreknowledge exploit EC-8 claims closed;
- **"has raid #N resolved?"** — unknowable from inside a pre-raid save after a relaunch.
Entropy must remain reproducible from `(WorldSeed, epoch)` or seed-based bug reproduction dies. Ownership is one of the pending decisions below.

### Route back to design (TD finding #6)
AC-72's "never replaying the exact resolved raid" is **unachievable by independent re-draw** over a small breach/composition space — a coincidental repeat is always possible without remembering the prior raid. The wording must match whichever re-roll semantic is chosen below (independent re-derivation → "never replays *by construction of a fresh draw*, coincidental similarity possible"; guaranteed-distinct → keep the absolute wording, pay the metadata cost).

## 3. Resolution Decisions — PENDING USER (asked 2026-08-07, not yet answered)

| # | Decision | Options (TD/CD-recommended first) |
|---|---|---|
| 1 | Adopt the TD-corrected classifications and amend ADR-0001, ADR-0004, ADR-0005 in place (dated amendments, per project pattern)? | **Revise per TD, amend all 3** / accept original draft (ADR-0004 doc-sync + ADR-0005 only) / discuss |
| 2 | Reload re-roll semantics | **Independent re-derivation** from `(WorldSeed, reload epoch)` — honest AC-72 rewording, no prior-raid memory / guaranteed-distinct — remembers the resolved raid, reject-samples until different |
| 3 | Does the re-roll apply to the corrupt-checkpoint fallback? | **No — the same unresolved raid resumes** (disk failure ≠ scumming) / yes — single rule everywhere |
| 4 | Who owns the out-of-save per-colony metadata (reload epoch, raid ledger, Commitment flag storage)? | **Save/Load #6 owns the file; the ADR-0005 amendment pins the derivation rule** / new dedicated ADR-0006 |

## 4. Applied This Run

- GDD revision (commit `b695c5a`): all §1 changes.
- GDD Rule 9 **fidelity fix** (this commit): non-selectable quit slot clause restored per CD-20's binding text; architecture note pointing here.
- ADR-0001 Open Questions closure note (commit `619be57`, pre-gate — stands; the Consequences-line staleness and the constrained-dispatch amendment await decision #1).
- Systems-index CD-19/CD-20 notes, technical-preferences Next line, session state (commit `619be57`).

## 5. Follow-Ups (blocked on §3 decisions)

1. **ADR-0001 amendment** — the constrained-dispatch concept for CD-19 catch-up (threat suppression/deferral, single re-plan, `RequestSwitch` deferral across catch-up); fix the stale Consequences line; resolve AC-27's wording.
2. **ADR-0004 amendment** — §2 sequence prose; the third slot class (RealTime-tagged, non-selectable, Continue-resumed); close the routed hole + correction #3 as resolved-by-CD-20; record the fallback re-roll ruling (decision #3).
3. **ADR-0005 amendment** — the reload-divergence mechanism (epoch mixed into `RaidTrigger` stream derivation, load-window-sanctioned, reproducible from `(WorldSeed, epoch)`), scoped to the colony-save-reload path only; VC-8 gains the scope clause; the bare-`WorldSeed`-never-regenerated rule gains the epoch carve-out.
4. **GDD AC-72 rewording** to match decision #2; AC-27 resolution per decision #1's amendment.
5. **Save/Load #6** inherits: slot taxonomy incl. the quit slot, lock window, labelling, Commitment flag, and (per decision #4) the per-colony metadata file.
6. **Raid Trigger #18** standing warning: do not implement reload behavior against ADR-0005 as written; wait for amendment #3.
7. After amendments land: re-run `/propagate-design-change time-authority-mode-switch` to verify coverage, and run `/architecture-review` in a fresh session.

## Related Documents
- `docs/architecture/change-impact-2026-08-03-time-authority-mode-switch.md` — the prior propagation (Battle Persistence); this run is its successor addressing the two questions it left open.
- systems-index CD-19/CD-20 (binding ruling text) · ADR-0001/0004/0005 · `production/session-state/active.md` (CD Rulings section, adopted restatement paragraph)
