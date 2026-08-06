# Mode-Switch Spike (Tier 0)

**Hypothesis under test**: ADR-0001's Time Authority architecture holds — one shared world with
a swappable time authority, zero state conversion at the boundary, deterministic, headlessly
testable — and the mode-switch seam leaves zero orphans on return to colony time.

**Status**: **Concluded 2026-07-26 — YES, 61/61 checks pass.** See
[`SPIKE-NOTE.md`](SPIKE-NOTE.md) for full findings and the three corrections it produced.

## Headline findings

- All four testable ADR-0001 validation criteria pass: headless full turn loop, TickSequence
  continuity + determinism, speed control touching zero `ITickable`s, and zero orphans after
  destroying terrain mid-encounter.
- Zero state conversion proven by identity: same store instance, same values, unchanged
  `Revision` across the swap.
- Cost is negligible: 0.578 µs per dispatch, 0.31 µs per swap, 28.9 µs for one reconcile,
  **0.00 B/sub-step allocation**.
- **Three corrections**: the mutation window's `IDisposable` scope allocated 24 B/dispatch (now
  a struct scope); pre-switch normalization must decide against the decision set, not live
  occupancy; and ADR-0003's "reap dead/withdrawn raiders" rule leaks live raiders — reap **all**
  of them, since raiders are encounter-scoped.

## How to run

```bash
cd prototypes/mode-switch-spike
dotnet run -c Release
```

Prints the 61-check contract suite followed by the cost table. Exit code is non-zero if any
check fails.

## Layout

| Path | What |
|---|---|
| `TimeAuthority.cs` | ADR-0001 implemented verbatim (manager, both authorities, switch, reconcile) |
| `EntityLayer.cs` | Minimal ADR-0003: stores, occupancy index, reservations, outcome inbox |
| `Systems.cs` | Real systems registered per ADR-0001's worked-example table |
| `TerrainModel.cs` | ADR-0002 model, copied from the terrain spike |
| `Tests.cs` | The 61 contract checks |
| `Benchmarks.cs` | Dispatch / swap / reconcile / allocation cost |

## Rules

Throwaway. Production code must never reference this directory; if ADR-0001 is promoted, the
production `TimeAuthorityManager` is written fresh in `src/core/` against the ADR.
