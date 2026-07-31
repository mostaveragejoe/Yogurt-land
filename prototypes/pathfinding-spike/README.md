# Pathfinding Spike (Tier 0)

**Hypothesis under test**: ADR-0003's mode-aware composite walkability (terrain ∧ doors ∧
occupancy) holds, ADR-0002's stair Z-linkage works, and pathfinding stays correct *and* in
budget while the player digs mid-route — the concept doc's named open question.

**Status**: **Concluded 2026-07-26 — YES on correctness, 36/36.** One real performance
constraint found. See [`SPIKE-NOTE.md`](SPIKE-NOTE.md).

## Headline findings

- Mode-aware walkability verified in both directions: RealTime colonists auto-open doors and
  ignore occupancy; TurnBased treats closed doors and occupied cells as blocking; a **broken
  door unblocks immediately**, so combat gets its breach the same turn.
- Stair floors Z-link a cell to the layer below and carry movement both ways.
- Mid-route digs handled correctly: off-route digs cost one revalidation and zero recomputes;
  a build on the remaining route forces exactly one recompute that routes around it; changes
  behind the colonist do not invalidate; sealing the goal yields an empty path, not a stale one.
- A* is **allocation-free** (0.00 B/query) and deterministic.
- **The constraint**: a full region flood fill costs **3.30 ms — 19.9% of a frame**. It must
  never run per dig; the rebuild trigger needs a policy (incremental, deferred, or per-layer).
- Revision polling is affordable (63.2 µs/dig) but scales as O(paths × path length) per
  mutation — the ADR-0003 change-list upgrade trigger is stated explicitly in the note.
- **Movement is 4-connected**: diagonals are a routed gameplay decision, not an oversight.

## How to run

```bash
cd prototypes/pathfinding-spike
dotnet run -c Release
```

Prints the 36-check contract suite followed by the cost table at MVP scale (128×128×16).
Exit code is non-zero if any check fails.

## Layout

| Path | What |
|---|---|
| `Walkability.cs` | `DoorStore`, `UnitOccupancyIndex`, mode-aware `CompositeWalkability` |
| `Pathfinder.cs` | Pooled A* with stair Z-linkage, `RegionIndex`, Revision-polling `PathCache` |
| `TerrainModel.cs` | ADR-0002 model (copied from the mode-switch spike — struct `MutationWindow`) |
| `Tests.cs` | The 36 contract checks |
| `Benchmarks.cs` | A*, colony frame, region rebuild, invalidation-under-digging, allocation |

## Rules

Throwaway. Production code must never reference this directory.
