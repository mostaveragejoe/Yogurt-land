# Unity Engine — Version Reference

| Field | Value |
|-------|-------|
| **Engine Version** | Unity 6.3 LTS |
| **Release Date** | December 2025 |
| **Project Pinned** | 2026-07-24 (Hollowdeep) |
| **Last Docs Verified** | 2026-07-24 |
| **LLM Knowledge Cutoff** | May 2025 |

## Knowledge Gap Warning

The LLM's training data likely covers Unity up to ~2022 LTS (2022.3). The entire
Unity 6 release series (formerly Unity 2023 Tech Stream) introduced significant
changes that the model does NOT know about. Always cross-reference this directory
before suggesting Unity API calls.

## Post-Cutoff Version Timeline

| Version | Release | Risk Level | Key Theme |
|---------|---------|------------|-----------|
| 6.0 | Oct 2024 | HIGH | Unity 6 rebrand, new rendering features, Entities 1.3, DOTS improvements |
| 6.1 | Nov 2024 | MEDIUM | Bug fixes, stability improvements |
| 6.2 | Dec 2024 | MEDIUM | Performance optimizations, new input system improvements |
| 6.3 LTS | Dec 2025 | HIGH | First LTS since 6.0, production-ready DOTS, enhanced graphics features |

## Major Changes from 2022 LTS to Unity 6.3 LTS

### Breaking Changes
- **Entities/DOTS**: Major API overhaul in Entities 1.0+, complete redesign of ECS patterns
- **Input System**: Legacy Input Manager deprecated, new Input System is default
- **Rendering**: URP/HDRP significant upgrades, SRP Batcher improvements
- **Addressables**: Asset management workflow changes
- **Scripting**: C# 9 support, new API patterns

### New Features (Post-Cutoff)
- **DOTS**: Production-ready Entity Component System (Entities 1.3+)
- **Graphics**: Enhanced URP/HDRP pipelines, GPU Resident Drawer
- **Multiplayer**: Netcode for GameObjects improvements
- **UI Toolkit**: Production-ready for runtime UI (replaces UGUI for new projects)
- **Async Asset Loading**: Improved Addressables performance
- **Web**: WebGPU support

### Deprecated Systems
- **Legacy Input Manager**: Use new Input System package
- **Legacy Particle System**: Use Visual Effect Graph
- **UGUI**: Still supported, but UI Toolkit recommended for new projects
- **Old ECS (GameObjectEntity)**: Replaced by modern DOTS/Entities

## Verified Sources

- Official docs: https://docs.unity3d.com/6000.0/Documentation/Manual/index.html
- Unity 6 release: https://unity.com/releases/unity-6
- Unity 6.3 LTS announcement: https://unity.com/blog/unity-6-3-lts-is-now-available
- Migration guide: https://docs.unity3d.com/6000.0/Documentation/Manual/upgrade-guides.html
- Unity 6 support: https://unity.com/releases/unity-6/support
- C# API reference: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/index.html
- Upgrade Guide (Unity 6.3 specific): https://docs.unity3d.com/6000.3/Documentation/Manual/UpgradeGuideUnity63.html
- What's New in Unity 6.3: https://docs.unity3d.com/6000.3/Documentation/Manual/WhatsNewUnity63.html
- Planned breaking changes (Unity Discussions, updated through 2026-03-27): https://discussions.unity.com/t/planned-breaking-changes-in-unity-6-5-updated-2026-03-27/1694205

## Re-Verification Note (2026-07-24, for Hollowdeep)

Live search confirms the existing Breaking Changes / Deprecated APIs / Best Practices docs below are accurate. Two additional items surfaced, noted here because they aren't in those files and are low-relevance to a single-player 3D project — recorded for completeness rather than added as full sections:

- **Multiplay Hosting** service is discontinued (shut down after March 31, 2026) — not relevant, Hollowdeep is single-player.
- **Box2D v3** integration improves the 2D physics stack — not relevant, Hollowdeep is a 3D voxel game with custom terrain collision, not Unity's 2D or 3D physics engine.
- **UI Toolkit** in 6.3 adds native SVG import and custom shader/post-processing support for UI — relevant to the blueprint/designation menu system (Tier 1 MVP); worth revisiting when `/ux-design` scopes that screen.
