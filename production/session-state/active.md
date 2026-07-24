# Session State

<!-- STATUS -->
Epic: Pre-Production
Feature: Foundation ADRs
Task: ADR-0002 Terrain Data Model (ADR-0001 written as Proposed)
<!-- /STATUS -->

## Current Task
Foundation ADRs — ADR-0001 written (Proposed); next is ADR-0002 Terrain Data Model.

## Progress
- [x] Game concept (design/gdd/game-concept.md) — brainstorm complete, 3 gates passed
- [x] Engine configured: Godot 4.7.1 + C# (was Unity; changed after tile-grid clarification)
- [x] Art bible sections 1-4 (design/art/art-bible.md) — AD-ART-BIBLE gate REVISED/complete
- [x] Systems index (design/gdd/systems-index.md) — 35 entries, 25 MVP; TD-SYSTEM-BOUNDARY + PR-SCOPE gates addressed
- [x] CD-SYSTEMS gate — CONCERNS, 9 notes recorded in index; 4 user decisions made
- [x] ADR-0001 Time Authority (Proposed) — docs/architecture/adr-0001-time-authority-mode-switch.md; forbidden patterns + ADR log added to technical-preferences.md
- [ ] ADR-0002 Terrain Data Model, ADR-0003 Entity Data Ownership (as Proposed)
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

## Files Being Worked On
- design/gdd/systems-index.md (written, gates recorded)
- production/session-state/active.md (this file)

## Open Questions
- Post-battle time: resume as zero-elapsed vs. advance by battle duration — route to creative-director BEFORE Needs & Simulation GDD (both stay possible under ADR-0001's fixed-dt sub-stepping)
- Working-hours assumption (full-time vs evenings) — timeline bands double if part-time
