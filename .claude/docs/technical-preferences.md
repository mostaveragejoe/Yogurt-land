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
- [None configured yet — add as architectural decisions are made]

## Allowed Libraries / Addons

<!-- Add approved third-party dependencies here -->
- [None configured yet — add as dependencies are approved]

## Architecture Decisions Log

<!-- Quick reference linking to full ADRs in docs/architecture/ -->
- [No ADRs yet — use /architecture-decision to create one. First recommended ADR: mode-switch architecture ("one world, swappable time authority") per TD-FEASIBILITY guidance in `design/gdd/game-concept.md`. Second recommended ADR: terrain data model and rendering approach (GridMap+MeshLibrary vs. custom instanced rendering) for the layered floor+wall tile grid.]

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
