# Save/Load Spike (Tier 0)

**Hypothesis under test**: the whole world — terrain, entities, time authority, id source,
reservations — survives a save/load round-trip with derived state rebuilt and combat state
structurally absent. The concept doc flags this as a HIGH, "commonly underestimated" risk.

**Status**: **Concluded 2026-07-26 — YES, 24/24.** See [`SPIKE-NOTE.md`](SPIKE-NOTE.md).

## Headline findings

- `save → load → save` is **byte-identical**; a reloaded world then **evolves identically to one
  that never left memory** (the strongest available test — anything missing would diverge).
- Id non-reuse holds, including across an encounter boundary: raiders are never serialized, but
  the id counter they consumed still advances, which is correct.
- Derived state (occupancy, directory) is rebuilt on load and **contributes zero bytes** — proved
  by wiping it and re-saving.
- CD-9 is structural: raiders and the outcome inbox add **zero records**; saving inside a battle
  is refused; a TurnBased save is rejected as corrupt on load.
- Catalog evolution is safe — inserting a material before existing ones remaps ids instead of
  silently changing what every saved wall is made of.
- Corruption fails loudly: truncation, bad magic, future schema, unknown material.
- **Saves gzip to 2%** (2.01 MB → 30 KB). MVP autosave is 21.9 ms — about 1.2 frames, so no
  async save machinery is needed for MVP.

## How to run

```bash
cd prototypes/saveload-spike
dotnet run -c Release
```

Prints the 24-check contract suite followed by size/time costs at MVP and full-vision scale.

## Layout

| Path | What |
|---|---|
| `WorldSave.cs` | The serialization contract: schema version, stable keys, exclusion list |
| `GameWorld.cs` | Whole-world wiring + the derived state that must be REBUILT, never saved |
| `TerrainModel.cs`, `EntityLayer.cs`, `TimeAuthority.cs` | ADR models reused from earlier spikes |
| `Tests.cs` | The 24 contract checks |
| `Benchmarks.cs` | Save size, gzip ratio, write/read time, autosave budget |

## Rules

Throwaway. Production code must never reference this directory.
