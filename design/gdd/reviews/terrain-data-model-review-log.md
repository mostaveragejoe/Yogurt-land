# Review Log — Terrain Data Model

## Review — 2026-08-02 — Verdict: NEEDS REVISION
Scope signal: XL
Specialists: game-designer, systems-designer, performance-analyst, godot-specialist, qa-lead, creative-director (senior synthesis)
Blocking items: 7 | Recommended: 13
Summary: The strongest GDD in the project, precise about what it owns but imprecise at its boundaries: formulas had undefined behavior on hostile numeric input (a negative DamageAmount could silently destroy a wall via int overflow), the states table contradicted the Edge Cases (residual stair contradiction surviving CD-GDD-ALIGN), and the render backend rested on two unverified pre-4.4 GridMap claims in a declared HIGH knowledge-gap version range. The creative-director amended their own CD-GDD-ALIGN ruling on sealed stairwells (rule stands; dormant linkage must be player-visible). Verified against SPIKE-NOTE.md that the measured 32 draw calls exclude the mandated damage-overlay map — the "exactly 32" gate was replaced with the measured style-variety curve. All 7 blocking items and all 13 recommended revisions were applied the same day; one further residual contradiction (UI Requirements + one AC still required rejecting wall designations on stair cells) was found during revision and corrected.
Prior verdict resolved: First review (prior gate CD-GDD-ALIGN 2026-07-26 was a CD gate, not a /design-review)

**Re-review checklist** (verify each blocking fix held):
1. Formula A/B defensive contracts (RejectedInput, widened arithmetic, floor via H₀ ≥ H_max branch, catalog-rebalance never-lowers rule)
2. Formula C bounds-filtering order + edge-dig and passability-independence ACs
3. States table on Formula D's enum (ClearFloor row, stair seal/reopen rows, HP-as-attribute)
4. Dormant stair linkage visibility (C8, UI Requirements, 2 ACs)
5. Draw-call curve gate scoped to wall+floor maps + re-baseline procedure + overlay cost in Known Gaps
6. Godot 4.7.1 pre-render-backend verification gate (Visual/Audio Requirements) — external task, doc records it
7. C11 map-authoring connectivity rule + per-map AC (OQ#6a closed)

**Open external tasks recorded (not doc defects)**: 60 fps target-hardware run (ADR-0002 promotion gate); 4.7.1 GridMap API verification + gridmap.md authoring; overlay-map draw-call measurement; multi-octant aggregate rebuild measurement; EncounterOutcomeReport schema change for CD-1 ordering (technical-director).

**Adjudications of record**: Gen0 gate stays a hard binary (fix the harness, not the gate — CD overruled performance-analyst); OQ#9 (8-style ceiling vs Pillar 1) stays routed to the art-director + technical-director palette conversation, not game-concept.md (CD overruled game-designer on placement, agreed on severity).
