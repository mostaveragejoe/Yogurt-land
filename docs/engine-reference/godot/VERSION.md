# Godot Engine — Version Reference

| Field | Value |
|-------|-------|
| **Engine Version** | Godot 4.7.1 |
| **Release Date** | 4.7 base release ~mid-2026; 4.7.1 stability patch July 13, 2026 |
| **Project Pinned** | 2026-07-24 (Hollowdeep) |
| **Last Docs Verified** | 2026-07-24 |
| **LLM Knowledge Cutoff** | May 2025 |

## Knowledge Gap Warning

The LLM's training data likely covers Godot up to ~4.3. Versions 4.4, 4.5,
4.6, and 4.7 introduced significant changes that the model does NOT know about.
Always cross-reference this directory before suggesting Godot API calls.

## Why This Matters for Hollowdeep Specifically

- **`GridMap` + `MeshLibrary`** is Godot's native tool for cell-based 3D worlds — a strong starting candidate for the Gnomoria-style layered floor+wall tile grid (see `design/gdd/game-concept.md`). It is NOT a full solution for destructibility/dynamic updates on its own: community sources note that naive per-change mesh regeneration causes high draw-call and physics overhead at scale, and a proposed built-in `DestructibleArea3D` node exists only as an open proposal, not shipped functionality. The Tier 0 terrain spike should validate a chunking strategy on top of (or instead of) GridMap before committing to it as the final architecture.
- **`AreaLight3D`** (new in 4.7) — rectangular area lights with soft radiating glow. Directly useful for the "Warm Hearth, Cold Dark" visual anchor (warm claimed-territory lighting, torch/hearth glow).
- **HDR output** (new in 4.7, all platforms) — worth evaluating for the warm/cold color contrast central to the visual identity.
- **C# note**: the project uses C# as primary language (see `technical-preferences.md`) — verify GDExtension/C# API parity for any 4.7-specific feature before relying on it, as C# bindings sometimes lag GDScript-exposed features.

## Post-Cutoff Version Timeline

| Version | Release | Risk Level | Key Theme |
|---------|---------|------------|-----------|
| 4.4 | ~Mid 2025 | MEDIUM | Jolt physics option, FileAccess return types, shader texture type changes |
| 4.5 | ~Late 2025 | HIGH | Accessibility (AccessKit), variadic args, @abstract, shader baker, SMAA |
| 4.6 | Jan 2026 | HIGH | Jolt default, glow rework, D3D12 default on Windows, IK restored |
| 4.7 | ~Mid 2026 | HIGH | AreaLight3D, MeshLibrary editor improvements, HDR output, Android XR/Steam Frame support, Control offset transforms |
| 4.7.1 | Jul 13, 2026 | Stability patch only | Rendering and platform bug fixes |

## Breaking Changes Since 4.6 (4.7)

- BlendSpace point-handling changed — AnimationTree projects depending on specific BlendSpace internals need retesting.
- Audio spectrum analyzer API changed — audio visualizer code may need adjustment.
- Keyboard/mouse device ID numbering scheme changed — code hardcoding device IDs will break.
- Particle angular velocity corrections — rotating particles will look subtly different.
- Shader preprocessor restrictions tightened — some macro patterns valid in 4.6 no longer compile.

*(None of the above are currently relevant to Hollowdeep's Tier 0 scope, but flag them before writing animation, audio-visualizer, input-device, particle, or shader-preprocessor code.)*

## Verified Sources

- Official docs: https://docs.godotengine.org/en/stable/
- 4.5→4.6 migration: https://docs.godotengine.org/en/stable/tutorials/migrating/upgrading_to_godot_4.6.html
- 4.4→4.5 migration: https://docs.godotengine.org/en/stable/tutorials/migrating/upgrading_to_godot_4.5.html
- Changelog: https://github.com/godotengine/godot/blob/master/CHANGELOG.md
- Godot 4.7 release notes: https://godotengine.org/releases/4.7/
- GridMap class reference (stable): https://docs.godotengine.org/en/stable/classes/class_gridmap.html
- Using GridMaps tutorial: https://docs.godotengine.org/en/stable/tutorials/3d/using_gridmaps.html
- DestructibleArea3D proposal (not shipped — tracking only): https://github.com/godotengine/godot-proposals/issues/14021
