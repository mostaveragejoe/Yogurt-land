# Technical Preferences

<!-- Populated by /setup-engine. Updated as the user makes decisions throughout development. -->
<!-- All agents reference this file for project-specific standards and conventions. -->

## Engine & Language

- **Engine**: Godot 4.7.1
- **Language**: C# (.NET 8+, primary), C++ via GDExtension (native plugins only)
- **Rendering**: [TO BE CONFIGURED] — evaluate Godot's Forward+ renderer against the "Warm Hearth, Cold Dark" many-local-lights visual direction; `AreaLight3D` (new in 4.7) is a strong candidate for the warm claimed-territory glow. Decide during `/art-bible` or the Tier 0 terrain spike.
- **Physics**: [TO BE CONFIGURED] — likely minimal use; the layered tile-grid terrain (floor+wall per cell) is not physics-engine-driven, closer to instanced prefab placement than a physics simulation. Revisit if ragdoll/projectile physics are needed for tactics combat.

## Input & Platform

<!-- Written by /setup-engine. Read by /ux-design, /ux-review, /test-setup, /team-ui, and /dev-story -->
<!-- to scope interaction specs, test helpers, and implementation to the correct input methods. -->

- **Target Platforms**: PC (Steam / Epic) — lead SKU. Other platforms explicitly deferred to Tier 3 (see `design/gdd/game-concept.md`)
- **Input Methods**: Keyboard/Mouse (primary)
- **Primary Input**: Keyboard/Mouse
- **Gamepad Support**: Partial — route input through Godot's `InputMap`/`InputEvent` system with clean action names from day one as cheap insurance for future ports, but do not spend implementation time on controller UX until the game is proven fun (TD-FEASIBILITY guidance)
- **Touch Support**: None
- **Platform Notes**: Menu-heavy, blueprint-driven UI is the primary interaction model — built for mouse precision (drag-select, click-to-designate). Controller and touch UX are explicitly out of scope until post-vertical-slice. Godot console export requires third-party publishers or significant extra work if Tier 3 platforms are ever pursued.

## Naming Conventions

- **Classes**: PascalCase (`PlayerController`) — must also be `partial`
- **Public properties/fields**: PascalCase (`MoveSpeed`, `JumpVelocity`)
- **Private fields**: `_camelCase` (`_currentHealth`, `_isGrounded`)
- **Methods**: PascalCase (`TakeDamage()`, `GetCurrentHealth()`)
- **Signal delegates**: PascalCase + `EventHandler` suffix (`HealthChangedEventHandler`)
- **Files**: PascalCase matching class (`PlayerController.cs`)
- **Scenes**: PascalCase matching root node (`PlayerController.tscn`)
- **Constants**: PascalCase (`MaxHealth`, `DefaultMoveSpeed`)

## Performance Budgets

- **Target Framerate**: 60 fps
- **Frame Budget**: 16.6 ms
- **Draw Calls**: [TO BE CONFIGURED — engine changed from Unity's SRP Batcher assumption; set a real ceiling from the Tier 0 terrain spike once a GridMap-based or custom instanced-rendering approach is prototyped]
- **Memory Ceiling**: [TO BE CONFIGURED — set once target hardware and world-size ceiling are known]

## Testing

- **Framework**: [TO BE CONFIGURED — decide during `/test-setup`; GUT (Godot Unit Test) is the common choice for GDScript, but verify C#-compatible options such as GoDotTest or standard .NET test runners]
- **Minimum Coverage**: [TO BE CONFIGURED]
- **Required Tests**: Balance formulas, gameplay systems, networking (if applicable)

## Forbidden Patterns

<!-- Add patterns that should never appear in this project's codebase -->
- **`SceneTree.paused` in the simulation path** — gameplay pause is RealTimeAuthority at zero sub-steps; engine pause is reserved for a future UI-pause layer only (ADR-0001)
- **Simulation logic in `_Process`/`_PhysicsProcess`** — `ITickable.Tick()` is the only sanctioned simulation-update path; `_Process` is presentation-only (ADR-0001)
- **Delta-scaling for game speed** — speed multiplies the fixed-dt sub-step count, never scales dt; delta-scaling breaks determinism and closes off post-battle time catch-up (ADR-0001)
- **RNG draws outside authority-driven execution** — random draws only inside `Tick()` or authority-driven resolution; never in `_Process`, UI callbacks, or event handlers (ADR-0001)
- **State-advancing World Change Event Bus handlers** — bus handlers are idempotent bookkeeping only (invalidate, mark stale, dirty); ticking is the only channel that advances simulation state (ADR-0001)
- **Entity state in `SwitchTransitionData`** — the mode-switch envelope carries encounter framing only; any field duplicating Terrain or Colonist Entity state is state conversion creeping back in (ADR-0001)
- **Per-entity simulation state as Nodes** — entity sim state is plain data; Nodes are views that read it for presentation (ADR-0001; detail in ADR-0003)
- **Terrain writes outside the mutation window** — `TerrainWorld` mutations happen only inside authority-driven execution or the load window; never from UI callbacks or bus handlers (debug-asserted) (ADR-0002)
- **Cell fields describing occupants, plans, zones, or combat state** — `TerrainCell` describes the architecture only; adjacent concerns live with their firewall-table owners (ADR-0002)
- **Retaining a `TerrainChangeBatch` beyond `Publish`** — batches are pooled and valid only for the duration of the call; handlers copy primitives out synchronously (ADR-0002)
- **GridMap (or any Node) as authoritative terrain state** — the model is plain C#; GridMap is at most a render backend reading from it (ADR-0002)
- **Caller-side chunk math** — `TerrainWorld.ChunkOf()`/`ChunkSize` are the only sanctioned CellCoord→chunk mapping; hardcoded shift/mask constants outside the facade will break when the spike tunes chunk size (ADR-0002)
- **Entity-store writes outside granted writer interfaces or the mutation window** — mutating store surfaces exist only as per-(system × field group) writer interfaces handed out at the composition root; every write debug-asserts the mutation window, the active mode (where the writer is authority-split), and the id's kind (ADR-0003)
- **Combat-transient state in entity stores** — anything meaningless outside an encounter (initiative, AP, target locks, overwatch) lives in encounter-scoped side tables owned by the combat systems, never in stores, never serialized (ADR-0003; CD-9)
- **A generic "misc entity" store** — every entity kind gets a typed store plus ownership-table rows; a kind without a table row cannot exist (ADR-0003)
- **UI or views writing entity stores** — views bind by `EntityId` and read-only poll `Revision`; input/UI submits designations and orders to owning systems, never direct writes (ADR-0003)
- **Occupancy-index updates outside store-internal position/death handling** — `UnitOccupancyIndex` has a single write path, synchronous and atomic with the store write; no external writer exists (ADR-0003)

## Allowed Libraries / Addons

<!-- Add approved third-party dependencies here -->
- [None configured yet — add as dependencies are approved]

## Project Layout & Autoloads

- **Godot project root**: repository root (`project.godot` + `Hollowdeep.csproj`). Simulation code lives in the separate plain-C# assembly `src/core/Hollowdeep.Core.csproj` (zero Godot references — CI-grep gate per ADR-0001/0002/0003); the root project holds Godot-facing views/tools and excludes `src/core`, `tests`, `tools`, `prototypes` from its compile glob.
- **Autoloads** (register new ones here, per ADR-0001):
  - `DebugConsole` → `src/tools/DebugConsole/DebugConsoleRoot.cs` — Tier 0 debug console overlay (systems index #29). Toggle: `debug_console_toggle` action (default F12, runtime-registered only if the project defines no binding). Core API: `GetNode<DebugConsoleRoot>("/root/DebugConsole").Core` for command/sweep registration until the composition root owns wiring.
  - `TimeAuthorityRoot` — NOT yet created (arrives with the mode-switch spike, ADR-0001).

## Architecture Decisions Log

<!-- Quick reference linking to full ADRs in docs/architecture/ -->
- **ADR-0001** (Proposed, 2026-07-24): Time Authority / Mode-Switch Architecture — strategy pattern over one shared world; plain-C# core (`TimeAuthorityManager`, `ITickable`, `TimeContext`); fixed-dt sub-stepping for speed control; full colony pause in combat; authority-swap with zero state conversion; `PostEncounterReconcile` on return to real time. Owns `{Mode, TurnIndex, TickSequence}`. See `docs/architecture/adr-0001-time-authority-mode-switch.md`. Validated by the Tier 0 mode-switch spike before promotion to Accepted.
- **ADR-0002** (Proposed, 2026-07-24): Terrain Data Model — chunked dense grid of packed 8-byte `TerrainCell` structs (AoS, per-layer 32×32 chunks) behind a single `TerrainWorld` write facade; batched change events with previous-state capture (CD-1); `ApplyWallRepair` (CD-7); material manifest + schema version for stable-ID saves; writer set per time authority; mutation-window assertion; God-object firewall table. Plain C#, zero Godot dependency; GridMap is a candidate render backend only. Chunk size/layout numbers gated on the terrain spike before promotion to Accepted. See `docs/architecture/adr-0002-terrain-data-model.md`.
- **ADR-0003** (Proposed, 2026-07-24): Entity Data Ownership — typed plain-C# stores per entity kind (`ColonistStore`, `RaiderStore`, `ItemStore`, `DoorStore`) keyed by `EntityId` (long, monotonic, never reused); write-ownership table enforced by per-(system × field group) writer interfaces granted at the composition root plus mutation-window/mode/kind assertions; health writer-per-authority (Needs in RealTime, Combat in TurnBased); combat-transient state in encounter-scoped side tables (CD-9 made structural); `UnitOccupancyIndex` exclusive under TurnBased, advisory under RealTime, with deterministic pre-switch placement normalization (Squad Prep decides, Colonist Movement executes); doors as damageable MVP entities composed into mode-aware walkability; reservation-gated `ConsumeFromStack` with the `StackReservationTable` owned by Stockpile & Hauling; Combat↔Veterancy cycle broken by the `EncounterOutcomeReport` (one-slot inbox drained in `PostEncounterReconcile`); Revision-polling change notification (no entity event bus). See `docs/architecture/adr-0003-entity-data-ownership.md`. Validated by the pathfinding, save/load, and mode-switch spikes before promotion to Accepted.
- **Next**: Seeded RNG ADR (constrained by ADR-0001's draws-only-inside-Tick rule); cross-cutting contracts annex.

## Engine Specialists

<!-- Written by /setup-engine when engine is configured. -->
<!-- Read by /code-review, /architecture-decision, /architecture-review, and team skills -->
<!-- to know which specialist to spawn for engine-specific validation. -->

- **Primary**: godot-specialist
- **Language/Code Specialist**: godot-csharp-specialist (all .cs files)
- **Shader Specialist**: godot-shader-specialist (.gdshader files, VisualShader resources)
- **UI Specialist**: godot-specialist (no dedicated UI specialist — primary covers all UI)
- **Additional Specialists**: godot-gdextension-specialist (GDExtension / native C++ bindings only)
- **Routing Notes**: Invoke primary for architecture decisions, ADR validation, and cross-cutting code review — including the terrain data-model decision (GridMap vs. custom) and the mode-switch architecture. Invoke C# specialist for code quality, `[Signal]` delegate patterns, `[Export]` attributes, `.csproj` management, and C#-specific Godot idioms. Invoke shader specialist for the Warm Hearth, Cold Dark lighting/material work. Invoke GDExtension specialist only if native C++ plugins become necessary (e.g., for performance-critical terrain or pathfinding code).

### File Extension Routing

<!-- Skills use this table to select the right specialist per file type. -->
<!-- If a row says [TO BE CONFIGURED], fall back to Primary for that file type. -->

| File Extension / Type | Specialist to Spawn |
|-----------------------|---------------------|
| Game code (.cs files) | godot-csharp-specialist |
| Shader / material files (.gdshader, VisualShader) | godot-shader-specialist |
| UI / screen files (Control nodes, CanvasLayer) | godot-specialist |
| Scene / prefab / level files (.tscn, .tres) | godot-specialist |
| Project config (.csproj, NuGet) | godot-csharp-specialist |
| Native extension / plugin files (.gdextension, C++) | godot-gdextension-specialist |
| General architecture review | godot-specialist |
