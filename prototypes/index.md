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
| Terrain spike | 2026-07-25 | Does ADR-0002's chunked-AoS model hold, is chunk 32 right, and which render backend? | **YES** — 38/38 contract checks; AoS falsification test failed to falsify (AoS 21–46% *faster* than SoA); GridMap @ octant 32 beats MultiMesh on both draw calls and per-dig cost | [SPIKE-NOTE](terrain-spike/SPIKE-NOTE.md) |

## Pivot Chains

None.

## Tier 0 Spike Gate Status (systems-index)

| Spike | Status |
|-------|--------|
| Fun spike | ✅ PROCEED (2026-07-25) — CD-PLAYTEST: CONFIRM |
| Terrain spike | ✅ YES (2026-07-25) — ADR-0002 validated 5/6; frame-rate clause needs target hardware. [SPIKE-NOTE](terrain-spike/SPIKE-NOTE.md) |
| Mode-switch spike | Not started |
| Pathfinding spike | Not started |
| Save/load spike | Not started |
