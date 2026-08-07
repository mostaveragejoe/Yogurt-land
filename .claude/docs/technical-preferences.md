# Technical Preferences

<!-- Populated by /setup-engine. Updated as the user makes decisions throughout development. -->
<!-- All agents reference this file for project-specific standards and conventions. -->

## Engine & Language

- **Engine**: Godot 4.7.1
- **Language**: C# (.NET 8+, primary), C++ via GDExtension (native plugins only)
- **Rendering**: **Forward+** (terrain spike ran on it 2026-07-25 and met the draw-call budget); terrain render backend is **two stacked GridMaps at `cell_octant_size = 32`** — a wall map (full-cube items) and a floor map (thin slab items offset to the cell bottom), both reading from `TerrainWorld` and never authoritative (ADR-0002). One GridMap cannot express floor+wall in the same cell; two cost 0 extra draw calls and +2.17 MB video memory (measured 2026-07-26). Additional per-cell visual layers = additional stacked maps. Still open: the many-local-lights evaluation for "Warm Hearth, Cold Dark" and `AreaLight3D` (new in 4.7) for claimed-territory glow — decide in the art/lighting pass, not the data model.
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
- **Draw Calls**: **terrain budget ≤ 150 draw calls** for the visible 3-layer cutaway at MVP map size (measured 2026-07-25/26). Two stacked GridMaps at `cell_octant_size = 32` render it in **32** draw calls with one style per tier; octant 16 = 128, octant 8 = 343 (over budget). **`cell_octant_size` MUST equal `TerrainWorld.ChunkSize` (both 32) — a locked invariant**, so a dirtied chunk maps 1:1 to a dirtied octant; octant 64 would make one octant span four chunks and force rebuilds of untouched chunks. **Style-variety ceiling: ~8 variants per tier** — draw calls scale with distinct material/style combos co-occurring in an octant (1→32, 2→48, 4→80, 8→144, 16→272 measured); the 32-call figure is NOT headroom for unlimited visual variety. Leaves headroom for entities, VFX, and UI within a provisional **500 draw-call whole-frame ceiling**. Re-check the whole-frame number on target hardware.
- **Memory Ceiling**: terrain cell data is **2 MB at MVP** (128×128×16) and **16 MB at the full-vision ceiling** (256×256×32); terrain render/video memory **14.25 MB** for the 3-layer cutaway (measured 2026-07-25). Chunks are 8 KB and stay off the Large Object Heap. **Whole-process ceiling still [TO BE CONFIGURED]** — set once target hardware is fixed; terrain is demonstrably not the memory risk.
- **Allocation**: **zero steady-state allocation** in the simulation path is a measured, enforceable standard, not an aspiration — the terrain spike recorded 0.17 B/mutation and 0 Gen0 collections across 60k mutations; the mode-switch spike recorded 0.00 B per dispatch across 20k sub-steps. Regressions here are bugs. **`MutationWindow.Open()` must return a `readonly struct` scope, never an `IDisposable` class** — a class scope boxes 24 B on every dispatch (measured 2026-07-26); `using` on a known struct type does not box.
- **Concentrated terrain change** (measured 2026-07-26): a combat AoE inside one octant is **sublinear** — 8 cells 14.3 µs, 27 cells 16.2 µs, 48 cells 17.2 µs, 75 cells 21.5 µs (0.29 µs/cell). The octant rebuild amortises, so batched destruction is cheaper per cell than scattered digging.
- **Mode-switch cost** (measured 2026-07-26): dispatch 0.578 µs/sub-step (0.003% of frame at 1x, 0.010% at 3x); RealTime→TurnBased swap 0.31 µs; `PostEncounterReconcile` 28.9 µs once per battle. The integration tax is discipline, not wall time.

## Testing

- **Framework**: [TO BE CONFIGURED — decide during `/test-setup`; GUT (Godot Unit Test) is the common choice for GDScript, but verify C#-compatible options such as GoDotTest or standard .NET test runners]
- **Minimum Coverage**: [TO BE CONFIGURED]
- **Required Tests**: Balance formulas, gameplay systems, networking (if applicable)

## Forbidden Patterns

<!-- Add patterns that should never appear in this project's codebase -->
- **`SceneTree.paused` in the simulation path** — gameplay pause is RealTimeAuthority at zero sub-steps; engine pause is reserved for a future UI-pause layer only (ADR-0001)
- **Simulation logic in `_Process`/`_PhysicsProcess`** — `ITickable.Tick()` is the only sanctioned simulation-update path; `_Process` is presentation-only (ADR-0001)
- **Delta-scaling for game speed** — speed multiplies the fixed-dt sub-step count, never scales dt; delta-scaling breaks determinism and closes off post-battle time catch-up (ADR-0001)
- **RNG draws outside authority-driven execution** — random draws only inside `Tick()`, authority-driven resolution, or the load window; never in `_Process`, UI callbacks, or event handlers (ADR-0001, load-window extension amended 2026-08-07 per ADR-0005; governs simulation streams — `PresentationRng` is the sanctioned non-sim generator)
- **State-advancing World Change Event Bus handlers** — bus handlers are idempotent bookkeeping only (invalidate, mark stale, dirty); ticking is the only channel that advances simulation state (ADR-0001)
- **Entity state in `SwitchTransitionData`** — the mode-switch envelope carries encounter framing only; any field duplicating Terrain or Colonist Entity state is state conversion creeping back in (ADR-0001)
- **Per-entity simulation state as Nodes** — entity sim state is plain data; Nodes are views that read it for presentation (ADR-0001; detail in ADR-0003)
- **Terrain writes outside the mutation window** — `TerrainWorld` mutations happen only inside authority-driven execution or the load window; never from UI callbacks or bus handlers (debug-asserted) (ADR-0002)
- **Cell fields describing occupants, plans, zones, or combat state** — `TerrainCell` describes the architecture only; adjacent concerns live with their firewall-table owners (ADR-0002)
- **Retaining a `TerrainChangeBatch` beyond `Publish`** — batches are pooled and valid only for the duration of the call; handlers copy primitives out synchronously (ADR-0002)
- **GridMap (or any Node) as authoritative terrain state** — the model is plain C#; GridMap is at most a render backend reading from it (ADR-0002)
- **Caller-side chunk math** — `TerrainWorld.ChunkOf()`/`ChunkSize` are the only sanctioned CellCoord→chunk mapping; hardcoded shift/mask constants outside the facade will break when the spike tunes chunk size (ADR-0002)
- **Entity-store writes outside granted writer interfaces or the mutation window** — mutating store surfaces exist only as per-(system × field group) writer interfaces handed out at the composition root; every write debug-asserts the mutation window, the active mode (where the writer is authority-split), and the id's kind (ADR-0003)
- **Combat-transient state in entity stores** — anything meaningless outside an encounter (initiative, AP, target locks, overwatch) lives in encounter-scoped side tables owned by the combat systems, never in stores, never in a colony-mode save; it is serialized **only into the battle checkpoint by its owning systems** (ADR-0003 Amendment 2026-08-03; Battle Persistence overturned CD-9's save half — this carve-out is NOT licence to move encounter fields into stores)
- **A generic "misc entity" store** — every entity kind gets a typed store plus ownership-table rows; a kind without a table row cannot exist (ADR-0003)
- **UI or views writing entity stores** — views bind by `EntityId` and read-only poll `Revision`; input/UI submits designations and orders to owning systems, never direct writes (ADR-0003)
- **Occupancy-index updates outside store-internal position/death handling** — `UnitOccupancyIndex` has a single write path, synchronous and atomic with the store write; no external writer exists (ADR-0003)
- **An RNG stream reused across encounters or re-derived at checkpoint restore** — encounter-scoped streams derive fresh at every switch-in from `(MasterSeed, RngStreamId, EncounterId)`; the `RestoredFromCheckpoint` path only `Restore()`s serialized `PcgState`, never re-derives — re-derivation resets draw counts to zero and silently breaks AC-67 (ADR-0005)
- **A system holding or drawing from an RNG stream it doesn't own in the ownership table** — per-stream grants at the composition root, same segregation as entity-store writer interfaces; no "misc RNG" grab-bag stream exists; view/audio code uses `PresentationRng`, never a simulation stream (ADR-0005)
- **A raw draw converted to float via a culture-aware or platform-variant path** — `NextFloat01`'s specified bit construction only; never locale-formatted string round-trips or FPU-mode-dependent conversion (ADR-0005)

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
- **ADR-0001** (**Accepted** 2026-07-26; proposed 2026-07-24; **amended 2026-08-03 — Battle Persistence**): Time Authority / Mode-Switch Architecture — strategy pattern over one shared world; plain-C# core (`TimeAuthorityManager`, `ITickable`, `TimeContext`); fixed-dt sub-stepping for speed control; full colony pause in combat; authority-swap with zero state conversion; `PostEncounterReconcile` on return to real time. Owns `{Mode, TurnIndex, TickSequence}`. See `docs/architecture/adr-0001-time-authority-mode-switch.md`. **Validated by the Tier 0 mode-switch spike (61/61) and promoted to Accepted 2026-07-26.** Corrections at promotion: struct mutation-window scope; normalization decides against the decision set. Amendment 2026-08-03: CD-9's save half overturned — `TurnBasedAuthority` needs checkpoint-grade snapshot support; a TurnBased save is valid iff written by the battle-checkpoint writer; load-into-TurnBased must not route through `RequestSwitch` (details in ADR-0004, pending).
- **ADR-0002** (Proposed, 2026-07-24; spike-validated 2026-07-25 — **frame-rate clause on target hardware is the only gate left before Accepted, re-scoped 2026-08-03 to include checkpoint writes at combat cadence (Battle Persistence amendment)**): Terrain Data Model — chunked dense grid of packed 8-byte `TerrainCell` structs (AoS, per-layer 32×32 chunks) behind a single `TerrainWorld` write facade; batched change events with previous-state capture (CD-1); `ApplyWallRepair` (CD-7); material manifest + schema version for stable-ID saves; writer set per time authority; mutation-window assertion; God-object firewall table. Plain C#, zero Godot dependency; GridMap is a candidate render backend only. Chunk size **32 confirmed**, AoS concession retired (AoS measured faster than SoA), snapshot = one-shot allocation, render backend = **two stacked GridMaps at octant 32**. See `docs/architecture/adr-0002-terrain-data-model.md`.
- **ADR-0003** (**Accepted** 2026-07-26; proposed 2026-07-24): Entity Data Ownership — typed plain-C# stores per entity kind (`ColonistStore`, `RaiderStore`, `ItemStore`, `DoorStore`) keyed by `EntityId` (long, monotonic, never reused); write-ownership table enforced by per-(system × field group) writer interfaces granted at the composition root plus mutation-window/mode/kind assertions; health writer-per-authority (Needs in RealTime, Combat in TurnBased); combat-transient state in encounter-scoped side tables (CD-9 made structural); `UnitOccupancyIndex` exclusive under TurnBased, advisory under RealTime, with deterministic pre-switch placement normalization (Squad Prep decides, Colonist Movement executes); doors as damageable MVP entities composed into mode-aware walkability; reservation-gated `ConsumeFromStack` with the `StackReservationTable` owned by Stockpile & Hauling; Combat↔Veterancy cycle broken by the `EncounterOutcomeReport` (one-slot inbox drained in `PostEncounterReconcile`); Revision-polling change notification (no entity event bus). See `docs/architecture/adr-0003-entity-data-ownership.md`. **Validated by the mode-switch, pathfinding and save/load spikes (criteria 2–5) and promoted to Accepted 2026-07-26.** Carried obligation: criterion 1's compile-time writer-interface segregation is asserted by design, not yet built — runtime assertions only until the production implementation delivers the interfaces + composition root. Correction at promotion: reconcile reaps ALL raiders, not just dead/withdrawn. **Amended 2026-08-03 (Battle Persistence)**: combat-transient side tables gain checkpoint-grade serialization via their owners (checkpoint only, never colony saves); `RaiderStore` and un-reaped `IsDead`/`IsBroken` entities are legal checkpoint states; load path never reaps; occupancy rebuild filters dead units.
- **ADR-0004** (**Proposed** 2026-08-03; authored via `/architecture-decision`, review mode full): Battle Checkpoint Architecture — checkpoint content scope (8 items), snapshot beat pinned at `AwaitingPresentation → NextActor` plus an "activation 0" checkpoint post-swap, the Option A write mechanism (full self-contained checkpoint, double-buffered pooled snapshot via a `SnapshotInto` caller-buffer obligation on ADR-0002/0003, coalesce-newest backpressure, async gzip + atomic same-volume replace), writer quiesce at slot retirement + in-file monotonic save ordering (never mtime), the `RestoredFromCheckpoint` resume path (no `RequestSwitch`; restore = distinct sanctioned writer in the load window; occupancy rebuild filters dead), write-side AC-68 provenance enforcement (writer-id header), quit-path flush-and-join requirement, and loud-fallback corrupt-checkpoint recovery. Gates: godot-specialist PASS WITH NOTES (folded); TD-ADR CONCERNS → all B1–B7 + A1–A10 applied. Promotion shares ADR-0002's re-scoped criterion-5 target-hardware run. See `docs/architecture/adr-0004-battle-checkpoint-architecture.md`.
- **ADR-0005** (**Proposed** 2026-08-07; authored via `/architecture-decision`, review mode full): Seeded RNG / Determinism — hand-rolled PCG32 (XSH-RR 64/32) as the sole simulation RNG primitive; 16-byte `PcgState {State, Increment}` with named-field serialization only and the core step in an explicit `unchecked` block; per-system named streams from one `MasterSeed` (SplitMix64 over (seed, id)) — `RaidTrigger`/`ColonistIdentity`/`MapGeneration` persistent (colony save), `CombatResolution`/`CombatRaiderAi` encounter-scoped from `(MasterSeed, RngStreamId, EncounterId)` (battle checkpoint only, snapshotted by their owning combat systems); RNG Stream Ownership Table with a per-authority "Drawn under" column and per-stream grants at the composition root; **checkpoint restore never re-derives** (B1); `EncounterIdSource` (`long`, Time Authority-owned, serialized in both save kinds, allocated at `RequestSwitch` acceptance) closes ADR-0001's undefined-allocator gap (B2); `PresentationRng` is the sanctioned non-sim generator; reload seed policy deferred to Raid Trigger #18's GDD (identical-replay placeholder default, `ReseedOnLoad` knob off). Companion amendments applied to ADR-0001 and ADR-0004 (content item 3 discharged). Gates: godot-specialist PASS (folded); TD-ADR CONCERNS → B1/B2/B4 + A1–A6 applied. Promotion gated on the ADR's validation criteria (reference test vectors, cross-correlation smoke, restore-performs-zero-derivations, `EncounterIdSource` round-trip). See `docs/architecture/adr-0005-seeded-rng-determinism.md`.
- **Next**: Colonist Entity quick-spec (#9); Save/Load quick-spec #6 (spec-level unblocked — ADR-0004 + ADR-0005 both exist as Proposed; their promotion gates still apply); cross-cutting contracts annex.

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
