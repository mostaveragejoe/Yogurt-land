# Session State

<!-- STATUS -->
Epic: Pre-Production
Feature: Foundation ADRs
Task: All 3 Foundation ADRs written (Proposed) — next: contracts annex + debug console
<!-- /STATUS -->

## Current Task
Foundation ADRs complete — ADR-0001, ADR-0002, ADR-0003 all written as Proposed. Next per the design order: cross-cutting contracts annex (one page), then debug console (Tier 0), then spikes. ADR-0003 changeset written but NOT committed (awaiting user instruction).

## Progress
- [x] Game concept (design/gdd/game-concept.md) — brainstorm complete, 3 gates passed
- [x] Engine configured: Godot 4.7.1 + C# (was Unity; changed after tile-grid clarification)
- [x] Art bible sections 1-4 (design/art/art-bible.md) — AD-ART-BIBLE gate REVISED/complete
- [x] Systems index (design/gdd/systems-index.md) — 35 entries, 25 MVP; TD-SYSTEM-BOUNDARY + PR-SCOPE gates addressed
- [x] CD-SYSTEMS gate — CONCERNS, 9 notes recorded in index; 4 user decisions made
- [x] ADR-0001 Time Authority (Proposed) — docs/architecture/adr-0001-time-authority-mode-switch.md; forbidden patterns + ADR log added to technical-preferences.md
- [x] ADR-0002 Terrain Data Model (Proposed) — docs/architecture/adr-0002-terrain-data-model.md; 5 forbidden patterns + ADR log entry added to technical-preferences.md; companion edits applied to ADR-0001 (shared-primitives correction, passive-store worked-example row)
- [x] ADR-0003 Entity Data Ownership (Proposed) — docs/architecture/adr-0003-entity-data-ownership.md; gates: godot-specialist PASS, TD-ADR two passes (21 findings closed per pre-approved resolutions). Companion edits applied: ADR-0001 (SwitchPending/PendingSwitchTarget property, reconcile duties, worked-example rows), ADR-0002 (EntityId in shared primitives), technical-preferences.md (5 forbidden patterns + log entry), systems-index.md (doors note, outcome-report pointer, Identity Bookkeeping note)
- [ ] Cross-cutting contracts annex (one page)
- [ ] Debug console (Tier 0)
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
- docs/architecture/adr-0003-entity-data-ownership.md (written, Proposed — NOT yet committed)
- docs/architecture/adr-0001-time-authority-mode-switch.md (companion edits applied)
- docs/architecture/adr-0002-terrain-data-model.md (companion edit applied)
- .claude/docs/technical-preferences.md (5 forbidden patterns + ADR-0003 log entry)
- design/gdd/systems-index.md (3 ADR-0003 companion notes)
- production/session-state/active.md (this file)

## Open Questions
- Post-battle time: resume as zero-elapsed vs. advance by battle duration — route to creative-director BEFORE Needs & Simulation GDD (both stay possible under ADR-0001's fixed-dt sub-stepping)
- Working-hours assumption (full-time vs evenings) — timeline bands double if part-time
