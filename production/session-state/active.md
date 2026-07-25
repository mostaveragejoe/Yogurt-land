# Session State

<!-- STATUS -->
Epic: Pre-Production
Feature: Foundation
Task: ADRs + contracts annex + debug console DONE — next: Tier 0 spike gate (fun spike first)
<!-- /STATUS -->

## Current Task
Tier 0 FUN SPIKE: **COMPLETE — PROCEED, CD-PLAYTEST CONFIRM (2026-07-25)**.
- Hypothesis CONFIRMED (user debrief); report at prototypes/hollowdeep-fun-spike-concept/REPORT.md (incl. gate verdict + 7 caveats)
- CD notes **CD-10–CD-18** recorded in systems-index (binding on Combat set, Construction, Blueprint UI, Squad Prep, Raider Decision-Making, Raid Trigger, Colonist Entity, Repair & Rebuild, Job Assignment, Notifications)
- Headline decisions: combat changes world state, never authors it (CD-10); player-activated pre-built objects > autonomous traps (CD-11); deployables = prep expressed spatially, VS-era (CD-12); downed→stabilize, no free revives (CD-13); raider reactivity = MVP acceptance criterion in #23 (CD-14); threat-info floor/ceiling + cross-raid intel (CD-15); prep phase must present a real decision (CD-16); Discovery has an MVP-testable enemy-knowledge vector (CD-17, applied to concept doc + index); lesson-to-answer latency budget on #25 (CD-18)
- **Next: remaining Tier 0 spikes in order — terrain → mode-switch → pathfinding → save/load** (technical spikes; use /prototype spike mode or direct engine builds against the ADR contracts; results promote ADR-0001/0002/0003 to Accepted)

Foundation phase complete beforehand: 3 ADRs (Proposed) + contracts annex + debug console — committed and pushed.

## Progress
- [x] Game concept (design/gdd/game-concept.md) — brainstorm complete, 3 gates passed
- [x] Engine configured: Godot 4.7.1 + C# (was Unity; changed after tile-grid clarification)
- [x] Art bible sections 1-4 (design/art/art-bible.md) — AD-ART-BIBLE gate REVISED/complete
- [x] Systems index (design/gdd/systems-index.md) — 35 entries, 25 MVP; TD-SYSTEM-BOUNDARY + PR-SCOPE gates addressed
- [x] CD-SYSTEMS gate — CONCERNS, 9 notes recorded in index; 4 user decisions made
- [x] ADR-0001 Time Authority (Proposed) — docs/architecture/adr-0001-time-authority-mode-switch.md; forbidden patterns + ADR log added to technical-preferences.md
- [x] ADR-0002 Terrain Data Model (Proposed) — docs/architecture/adr-0002-terrain-data-model.md; 5 forbidden patterns + ADR log entry added to technical-preferences.md; companion edits applied to ADR-0001 (shared-primitives correction, passive-store worked-example row)
- [x] ADR-0003 Entity Data Ownership (Proposed) — docs/architecture/adr-0003-entity-data-ownership.md; gates: godot-specialist PASS, TD-ADR two passes (21 findings closed per pre-approved resolutions). Companion edits applied: ADR-0001 (SwitchPending/PendingSwitchTarget property, reconcile duties, worked-example rows), ADR-0002 (EntityId in shared primitives), technical-preferences.md (5 forbidden patterns + log entry), systems-index.md (doors note, outcome-report pointer, Identity Bookkeeping note)
- [x] Cross-cutting contracts annex — docs/architecture/cross-cutting-contracts.md (one page, three contracts, no fourth; CD-9 recorded as resolved)
- [x] Debug console (Tier 0) — Godot 4.7.1 C# project scaffolded at repo root (project.godot + Hollowdeep.csproj); plain-C# core assembly src/core/Hollowdeep.Core.csproj (Primitives: CellCoord/ChunkCoord/EntityId per ADRs; Diagnostics: DebugConsole core with command + sweep registries, Revision polling); Godot overlay src/tools/DebugConsole/DebugConsoleRoot.cs (autoload "DebugConsole", F12 toggle, history, presentation-only). `dotnet build` green (0 warn/0 err, Godot.NET.Sdk 4.7.1 from NuGet); core zero-Godot grep clean. NOT yet runtime-verified in the Godot editor (no engine binary in this environment) — first editor open should confirm autoload + toggle. .gitignore fixed (Unity leftovers were ignoring *.csproj/*.sln)
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
- docs/architecture/cross-cutting-contracts.md (new — the annex)
- project.godot, Hollowdeep.csproj, src/core/** , src/tools/DebugConsole/** (new — project scaffold + debug console)
- .claude/docs/technical-preferences.md (Project Layout & Autoloads section added)
- design/gdd/systems-index.md (annex pointer; tracker 3/3; next-steps checkboxes)
- .gitignore (removed Unity-era *.csproj/*.sln ignores)
- production/session-state/active.md (this file)

## Open Questions
- Post-battle time: resume as zero-elapsed vs. advance by battle duration — route to creative-director BEFORE Needs & Simulation GDD (both stay possible under ADR-0001's fixed-dt sub-stepping)
- Working-hours assumption (full-time vs evenings) — timeline bands double if part-time
