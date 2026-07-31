# Prototype Index

Complete history of what was tried and what was learned. One row per concept
prototype; spike notes live beside their prototypes.

## Concept Prototypes

| Concept | Date | Path | Verdict | Report |
|---------|------|------|---------|--------|
| Hollowdeep Fun Spike (tactics-in-your-own-base) | 2026-07-25 | HTML | **PROCEED** (CD-PLAYTEST: CONFIRM; notes CD-10–CD-18) | [REPORT.md](hollowdeep-fun-spike-concept/REPORT.md) |

## Technical Spikes

| Spike | Date | Question | Result | Note |
|---|---|---|---|---|
| Pathfinding spike | 2026-07-26 | Composite walkability per mode; can pathfinding stay correct + fast while the player digs mid-route? | **YES** — 36/36; A* 8–91 µs, 0.00 B/query, deterministic; mid-route digs correct. **Constraint**: full region flood fill = 3.30 ms (19.9% of frame), must not run per dig | [SPIKE-NOTE](pathfinding-spike/SPIKE-NOTE.md) |
| Mode-switch spike | 2026-07-26 | Does ADR-0001 hold — zero state conversion, deterministic, headless, zero orphans at the seam? | **YES** — 61/61; all 4 testable criteria pass; 0.578 µs/dispatch, 0.31 µs/swap, 0.00 B allocation; 3 corrections (struct mutation-window scope, normalization decides against the decision set, reap ALL raiders) | [SPIKE-NOTE](mode-switch-spike/SPIKE-NOTE.md) |
| Terrain spike | 2026-07-25 (addendum 07-26) | Does ADR-0002's chunked-AoS model hold, is chunk 32 right, and which render backend? | **YES** — 38/38 contract checks; AoS falsification test failed to falsify (AoS 21–46% *faster* than SoA); **two stacked GridMaps @ octant 32** beat MultiMesh on both axes and solve floor+wall-per-cell at 0 extra draw calls | [SPIKE-NOTE](terrain-spike/SPIKE-NOTE.md) |

## Pivot Chains

None.

## Tier 0 Spike Gate Status (systems-index)

| Spike | Status |
|-------|--------|
| Fun spike | ✅ PROCEED (2026-07-25) — CD-PLAYTEST: CONFIRM |
| Mode-switch spike | ✅ YES (2026-07-26) — ADR-0001 validated 61/61; recommended for Accepted. [SPIKE-NOTE](mode-switch-spike/SPIKE-NOTE.md) |
| Pathfinding spike | ✅ YES (2026-07-26) — ADR-0003 criterion 5 passes 36/36; region-rebuild trigger routed to the quick-spec. [SPIKE-NOTE](pathfinding-spike/SPIKE-NOTE.md) |
| Terrain spike | ✅ YES (2026-07-25) — ADR-0002 validated 5/6; frame-rate clause needs target hardware. [SPIKE-NOTE](terrain-spike/SPIKE-NOTE.md) |
| Mode-switch spike | Not started |
| Pathfinding spike | Not started |
| Save/load spike | Not started |
