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

- **Target Framerate**: 60 fps — **terrain measured on target hardware 2026-08-24** (RTX 3060 Ti, Godot 4.7.2 mono): frame-time **p99 2.167 ms (Vulkan) / 2.024 ms (D3D12)** against the 16.6 ms budget under 8 digs/frame sustained over 1800 frames, i.e. ~8× headroom. Evidence: `production/qa/evidence/terrain-target-hardware-2026-08-24/`. Whole-frame budget with entities, VFX and UI is still unmeasured.
- **Frame Budget**: 16.6 ms
- **Draw Calls**: **terrain budget ≤ 150 draw calls** for the visible 3-layer cutaway at MVP map size (measured 2026-07-25/26). Two stacked GridMaps at `cell_octant_size = 32` render it in **32** draw calls with one style per tier; octant 16 = 128, octant 8 = 343 (over budget). **`cell_octant_size` MUST equal `TerrainWorld.ChunkSize` (both 32) — a locked invariant**, so a dirtied chunk maps 1:1 to a dirtied octant; octant 64 would make one octant span four chunks and force rebuilds of untouched chunks. **Style-variety ceiling: ~8 variants per tier** — draw calls scale with distinct material/style combos co-occurring in an octant (1→32, 2→48, 4→80, 8→144, 16→272 measured); the 32-call figure is NOT headroom for unlimited visual variety. Leaves headroom for entities, VFX, and UI within a provisional **500 draw-call whole-frame ceiling**. Re-check the whole-frame number on target hardware.
- **Memory Ceiling**: terrain cell data is **2 MB at MVP** (128×128×16) and **16 MB at the full-vision ceiling** (256×256×32); terrain **render buffers 16.23 MB** for the 3-layer cutaway (target hardware 2026-08-24; the 2026-07-25 software-Vulkan figure of 14.25→16.42 MB was the same quantity and is confirmed). **Do not budget this as total video memory**: the process reported **43–50 MB** of video memory on real hardware, the difference being render targets and swapchain at real resolution — framebuffer overhead that scales with output resolution, not with terrain. The earlier figure was mislabelled "video memory"; it measures terrain buffers only. Chunks are 8 KB and stay off the Large Object Heap. **Whole-process ceiling still [TO BE CONFIGURED]** — set once target hardware is fixed; terrain is demonstrably not the memory risk.
- **Allocation**: **zero steady-state allocation** in the simulation path is a measured, enforceable standard, not an aspiration — the terrain spike recorded 0.17 B/mutation and 0 Gen0 collections across 60k mutations; **re-confirmed on target hardware 2026-08-24 in a live render loop**: 32.7–36.1 B/frame and **0 Gen0/Gen1/Gen2 collections** across 1800 frames at 8 digs/frame; the mode-switch spike recorded 0.00 B per dispatch across 20k sub-steps. Regressions here are bugs. **`MutationWindow.Open()` must return a `readonly struct` scope, never an `IDisposable` class** — a class scope boxes 24 B on every dispatch (measured 2026-07-26); `using` on a known struct type does not box.
- **Concentrated terrain change** (measured 2026-07-26): a combat AoE inside one octant is **sublinear** — 8 cells 14.3 µs, 27 cells 16.2 µs, 48 cells 17.2 µs, 75 cells 21.5 µs (0.29 µs/cell). The octant rebuild amortises, so batched destruction is cheaper per cell than scattered digging.
- **Mode-switch cost** (measured 2026-07-26): dispatch 0.578 µs/sub-step (0.003% of frame at 1x, 0.010% at 3x); RealTime→TurnBased swap 0.31 µs; `PostEncounterReconcile` 28.9 µs once per battle. The integration tax is discipline, not wall time.

## Testing

- **Framework**: **xUnit** (`tests/Hollowdeep.Tests.csproj`, .NET 8) — decided at `/test-setup` 2026-08-24. The simulation core is Godot-free by contract, so its whole suite runs under a bare `dotnet test` with no engine installed. GdUnit4 is the right tool for engine-facing view tests and gets its own project when those exist; it is deliberately not set up yet.
- **Minimum Coverage**: **no numeric target, by decision** (2026-08-24). Coverage is gated by *story type* via the Testing Standards table — Logic and Integration stories are BLOCKING on real evidence, Visual/UI are advisory. A percentage target measures lines executed, not behaviour pinned, and reliably produces tests written for the metric. Revisit only if a story ships Done with evidence that a percentage would have caught.
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
- **Combat-transient state in entity stores** — anything meaningless outside an encounter (initiative, AP, target locks, overwatch) lives in encounter-scoped side tables owned by the combat systems, never in stores, never in a colony-mode save; it is serialized **only into the battle checkpoint by its owning systems** (ADR-0003 Amendment 2026-08-03; Battle Persistence overturned CD-9's save half — this carve-out is NOT licence to move encounter fields into stores)
- **A generic "misc entity" store** — every entity kind gets a typed store plus ownership-table rows; a kind without a table row cannot exist (ADR-0003)
- **UI or views writing entity stores** — views bind by `EntityId` and read-only poll `Revision`; input/UI submits designations and orders to owning systems, never direct writes (ADR-0003)
- **Occupancy-index updates outside store-internal position/death handling** — `UnitOccupancyIndex` has a single write path, synchronous and atomic with the store write; no external writer exists (ADR-0003)
- **`event`/`Action<T>`/`EventHandler<T>`/`IObserver<T>`/`List<Action<T>>` over `TerrainChangeBatch`** — illegal in C# 12 (a `ref struct` cannot be a generic type argument) and **still illegal on .NET 9** (`allows ref struct` is opt-in per declaration; `List<T>` can never qualify, by CLR rule). Use `ITerrainChangeSubscriber` (ADR-0006)
- **Bus subscriber registration or unregistration during dispatch** — the sorted list is not re-entrantly safe; the `IsInstanceValid` purge is deferred until after the walk returns (ADR-0006 rules 5, 10)
- **A bus handler querying another subscriber's derived state during dispatch** — handlers mark their own state only; cross-system reads happen in `Tick()`. This is what keeps dispatch order non-load-bearing (ADR-0006 rule 12)
- **`EncounterAttempt` persisted in a colony save** — the anti-save-scum re-roll counter must survive *outside* the save file (`user://` profile, injected from the composition root). A counter written into the save is restored with it, reproduces the attempt number, and **re-rolls nothing** — the exploit survives while looking fixed (ADR-0005 Amendment 2026-08-26; validation criterion 9 tests exactly this)
- **Stock or engine RNG in the core** — no `System.Random`, Godot `RandomNumberGenerator`, `Guid.NewGuid`, or time-based seeding anywhere in `src/core`; all randomness goes through `SeededRngStore` (PCG-XSH-RR), with `RootSeed` injected from the composition root. `System.Random` is not version-stable (breaks save formats), engine RNG is a Godot dependency, and entropy sources break bit-reproducible resume (CI-grep gate) (ADR-0005)

## Asset Authorship

- **All art assets are made by a person. No AI-generated art ships in this game.** Standing project rule, user ruling 2026-08-24. Full statement and consequences: `design/art/art-bible.md` Section 0. `/asset-spec` writes briefs for a human artist, never generation prompts.

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
- **ADR-0002** (**Accepted** 2026-08-24; proposed 2026-07-24; spike-validated 2026-07-25; **frame-rate + Gen0 clauses measured on target hardware 2026-08-24** — p99 2.02–2.17 ms vs the 16.6 ms budget on an RTX 3060 Ti, 0 GC collections. The 2026-08-24 amendment **split criterion 5** and moved the checkpoint clause to ADR-0004, ending the circular block; every terrain clause passed): Terrain Data Model — chunked dense grid of packed 8-byte `TerrainCell` structs (AoS, per-layer 32×32 chunks) behind a single `TerrainWorld` write facade; batched change events with previous-state capture (CD-1); `ApplyWallRepair` (CD-7); material manifest + schema version for stable-ID saves; writer set per time authority; mutation-window assertion; God-object firewall table. Plain C#, zero Godot dependency; GridMap is a candidate render backend only. Chunk size **32 confirmed**, AoS concession retired (AoS measured faster than SoA), snapshot = one-shot allocation, render backend = **two stacked GridMaps at octant 32**. See `docs/architecture/adr-0002-terrain-data-model.md`.
- **ADR-0003** (**Accepted** 2026-07-26; proposed 2026-07-24): Entity Data Ownership — typed plain-C# stores per entity kind (`ColonistStore`, `RaiderStore`, `ItemStore`, `DoorStore`) keyed by `EntityId` (long, monotonic, never reused); write-ownership table enforced by per-(system × field group) writer interfaces granted at the composition root plus mutation-window/mode/kind assertions; health writer-per-authority (Needs in RealTime, Combat in TurnBased); combat-transient state in encounter-scoped side tables (CD-9 made structural); `UnitOccupancyIndex` exclusive under TurnBased, advisory under RealTime, with deterministic pre-switch placement normalization (Squad Prep decides, Colonist Movement executes); doors as damageable MVP entities composed into mode-aware walkability; reservation-gated `ConsumeFromStack` with the `StackReservationTable` owned by Stockpile & Hauling; Combat↔Veterancy cycle broken by the `EncounterOutcomeReport` (one-slot inbox drained in `PostEncounterReconcile`); Revision-polling change notification (no entity event bus). See `docs/architecture/adr-0003-entity-data-ownership.md`. **Validated by the mode-switch, pathfinding and save/load spikes (criteria 2–5) and promoted to Accepted 2026-07-26.** Carried obligation: criterion 1's compile-time writer-interface segregation is asserted by design, not yet built — runtime assertions only until the production implementation delivers the interfaces + composition root. Correction at promotion: reconcile reaps ALL raiders, not just dead/withdrawn. **Amended 2026-08-03 (Battle Persistence)**: combat-transient side tables gain checkpoint-grade serialization via their owners (checkpoint only, never colony saves); `RaiderStore` and un-reaped `IsDead`/`IsBroken` entities are legal checkpoint states; load path never reaps; occupancy rebuild filters dead units.
- **ADR-0004** (**Proposed** 2026-08-03; authored via `/architecture-decision`, review mode full): Battle Checkpoint Architecture — checkpoint content scope (8 items), snapshot beat pinned at `AwaitingPresentation → NextActor` plus an "activation 0" checkpoint post-swap, the Option A write mechanism (full self-contained checkpoint, double-buffered pooled snapshot via a `SnapshotInto` caller-buffer obligation on ADR-0002/0003, coalesce-newest backpressure, async gzip + atomic same-volume replace), writer quiesce at slot retirement + in-file monotonic save ordering (never mtime), the `RestoredFromCheckpoint` resume path (no `RequestSwitch`; restore = distinct sanctioned writer in the load window; occupancy rebuild filters dead), write-side AC-68 provenance enforcement (writer-id header), quit-path flush-and-join requirement, and loud-fallback corrupt-checkpoint recovery. Gates: godot-specialist PASS WITH NOTES (folded); TD-ADR CONCERNS → all B1–B7 + A1–A10 applied. Promotion shares ADR-0002's re-scoped criterion-5 target-hardware run. See `docs/architecture/adr-0004-battle-checkpoint-architecture.md`.
- **ADR-0005** (**Proposed** 2026-08-08; authored via `/architecture-decision`, review mode full): Seeded RNG — **PCG-XSH-RR 64/32** as the sole sanctioned generator (plain-C# `PcgRng` struct, output from pre-advance state, forced-odd `Inc`), **named independent streams derived from one `RootSeed`** via splitmix64, owned by `SeededRngStore` with per-system mode-tagged draw handles (ADR-0003 grant analogue). `RootSeed` injected from the Godot composition root (core has no entropy source). Two serialization groups mirroring the checkpoint-vs-colony-save firewall: colony-persistent streams' `State` in colony saves; the **Combat stream re-derived per encounter from `splitmix64(RootSeed, Combat, EncounterId, EncounterAttempt)`** (**Amended 2026-08-26** — the attempt counter ships the anti-save-scum re-roll and lives in a `user://` profile file, **never the colony save**; `TR-time-026` keeps full strength with `EncounterAttempt` as a declared determinism input, and resume determinism was never affected since checkpoint restore restores `State` directly), its mid-battle `State` captured only in the checkpoint at ADR-0004's `AwaitingPresentation → NextActor` beat. `State`-only, little-endian (`BinaryPrimitives`) serialization; `Inc` re-derived. Gates: godot-csharp-specialist (2 blocking fixes folded: odd-`Inc`, pre-advance output); TD-ADR CONCERNS → all B1–B5 + A1–A6 applied. **No target-hardware gate on its own promotion** (headless logic); co-dependent with ADR-0004 on the combat-group boundary — promote together. See `docs/architecture/adr-0005-seeded-rng.md`.
- **ADR-0006** (**Proposed** 2026-08-25; authored via `/architecture-decision`, review mode full): World Change Event Bus Subscriber Contract — closes the subscribe side ADR-0002 left undefined. `ITerrainChangeSubscriber` (`OnTerrainChanged(in TerrainChangeBatch)` + `OnWorldReloaded()`), registered at the composition root with an explicit integer priority and dispatched synchronously in `(priority, registration sequence)` order — Pathfinding 10, LOS 20, Job Assignment 30, Repair 40, Notifications 50, Terrain Rendering 60. **Ordering is deliberately not load-bearing**: rule 12 forbids a handler querying another subscriber's derived state during dispatch, so no handler's result depends on whether an earlier one ran. 13 rules total, incl. single-publisher-by-construction (explicit interface implementation of `ITerrainChangeSink`), all-subscribers-registered-before-`TerrainWorld`, deferred `IsInstanceValid` purge (rules 5+10 reconciled), and per-subscriber exception isolation (rethrow in DEBUG). **Records the binding C#-12 trap**: `net8.0` means a `ref struct` cannot be a generic type argument, so `event Action<T>`/`EventHandler<T>`/`IObserver<T>`/`List<Action<T>>` over `TerrainChangeBatch` are compile errors — and **.NET 9 does not lift this** (`allows ref struct` is opt-in per declaration; `List<T>` never qualifies, by CLR rule). Gates: godot-csharp-specialist APPROVE WITH NOTES (1 correction + 3 rules folded); TD-ADR CONCERNS → B1–B5 + A1–A7 applied. See `docs/architecture/adr-0006-world-change-event-bus-subscriber-contract.md`.
- **Next** *(refreshed 2026-08-26 at `/architecture-review`)*: **amend ADR-0001** for the Pathfinding registration conflict (its tickable table contradicts the Pathfinding quick-spec, which registers no `ITickable`); **define the composition root** (QQ-24 — 30 references, 0 definitions, blocks the first store implementation and ADR-0006); build ADR-0004's async checkpoint path so it can be measured (QQ-02), which jointly gates ADR-0004 + ADR-0005 promotion; Save/Load quick-spec #6.

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
