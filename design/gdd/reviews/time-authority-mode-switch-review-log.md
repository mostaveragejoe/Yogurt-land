# Review Log — Time Authority / Mode-Switch

## Review — 2026-08-02 — Verdict: NEEDS REVISION (revised same day; pending re-review)
Scope signal: XL
Specialists: game-designer, systems-designer, qa-lead, performance-analyst, godot-specialist, ux-designer, creative-director (senior synthesis)
Blocking items: 5 | Recommended: 9 | Nice-to-have: 3 (deliberately not applied — user scope decision)
Summary: The design is sound and the architecture beneath it is Accepted and spike-validated; nothing challenged a pillar, rule, or prior CD ruling. Every blocking item was narrow and locally fixable. The headline defect (B1): Formula D.2's worked example was off by 60× — "8 real minutes per 24-game-hour day at 3×" only holds if a game-day is 1,440 game-seconds, a compression constant that existed nowhere in the doc despite the number being named the canonical citation for Needs/day-night/crafting. Resolved by user decision: `GameSecondsPerGameDay = 1440` declared as a Tuning Knob; the canonical citation is now the rate+day-length pair. Other blockers: accumulator drop-vs-preserve semantics formalized (B2 — D.1/Rule 3/AC-4/EC-9 were reconcilable but unstated); view-freeze contract named, `ModeTransitioned` as the signal, AC-64 (B3 — "colonists caught mid-stride" was unachievable while `_Process` keeps running); EC-10 fast-resolve MAY → MUST-bound (B4); AC-36 given a second 4× load point with a ≤6× near-linear ratio gate (B5 — one benchmark point fits any monotonic curve). CD verdict: "This is a half-day revision, not a redesign."
Prior verdict resolved: First /design-review (prior gate CD-GDD-ALIGN 2026-08-02 was a CD gate, not a /design-review)

**Adjudications of record**: 20-minute-ceiling runtime enforcement ruled correctly-routed Combat-set scope, NOT a defect of this doc (CD overruled game-designer). EC-8 quit-rewind: M1's Option A routing stands, but re-roll-within-threat-band elevated to binding CD default — identical replay now requires an explicit CD ruling (CD partially conceded to game-designer). Quit-rewind onboarding disclosure OVERRULED AND INVERTED (CD vs ux-designer): the rewind is deliberately NOT surfaced pre-emptively — advertising the save-scum path is the fastest way to falsify Pillar 3; recorded as an anti-requirement candidate (nice-to-have N2, not yet applied). Pause-cycling metagame downgraded: paying in stalled colony progress is pillar-consonant player choice. Frame-budget ledger ruled a process gap, not this doc's defect — routed to technical-director (OQ #8). qa-lead's suspected CD-9 tag collision on AC-44/AC-60 investigated and dismissed: CD-9 genuinely covers both save-disable and battle-length.

**Re-review checklist** (verify each fix held):
1. B1 — `GameSecondsPerGameDay = 1440` knob + D.2 derivation + AC-22; canonical *pair* citation rule
2. B2 — Backlog-semantics paragraph (whole-step overflow dropped, sub-step residual preserved, one mechanism)
3. B3 — View-freeze contract (Visual/Audio Requirements bullet, Dependencies row, AC-64)
4. B4 — EC-10 MUST-bound non-interactive resolution + AC-52 rewrite
5. B5 — AC-36 two-point ratio gate (≤6× at 4× load)
6. R2 — AC-49/55 concrete referents; AC-45 re-tag (BLOCKED #6); AC-53 re-tag (testable-now Logic); AC-43 `QuitConfirmText`
7. R3/R4 — Config-load guards paragraph + AC-62/63
8. R5/R6/R7 — EC-3 freeze-beat obligation; disabled-state consistency + accessibility cross-reference; survey focus-loss rule
9. R8 — Section (b) methodology preconditions (N≥5 runs, warm-up)

**Open external tasks (routed, not doc defects)**: frame-budget ledger (technical-director, OQ #8 — before the third sim-bearing system's Done); engine-reference 4.7 refresh incl. `max_physics_steps_per_frame` 4.7.1 verification (technical-director, OQ #9 — before this system's implementation stories); ADR-0001 presentation-layer companion note for the view-freeze contract (technical-director); benchmark harness N≥5 + warm-up rework before AC-34/35/36 lock as CI gates.

**Not applied (nice-to-haves, on record)**: N1 scope falsifiable target #2 to non-empty-roster encounters; N2 record "quit-rewind deliberately not surfaced pre-emptively" as an anti-requirement; N3 pause-cycling intent sentence, AC-47 "≤2.5 s", 0.05 B band rationale sentence, Jolt-irrelevance note, AC-48 automation.
