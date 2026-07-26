# Terrain Spike (Tier 0)

**Hypothesis under test**: ADR-0002's terrain data model — a chunked array-of-structures of
8-byte `TerrainCell` records behind a single `TerrainWorld` write facade — meets the 16.6 ms
frame budget at MVP map size with zero steady-state allocation, and 32×32 is the right chunk
size. Secondary: resolve the ADR's routed open question on the render backend (GridMap vs
MultiMesh).

**Status**: **Concluded 2026-07-25 — YES.** See [`SPIKE-NOTE.md`](SPIKE-NOTE.md) for the full
findings, tables, and the three ADR corrections it produced.

## Headline findings

- 38/38 ADR-0002 contract correctness checks pass.
- 2.00 MB cell data at MVP (128×128×16), 16.00 MB at full-vision — exactly as predicted.
- Zero steady-state allocation confirmed: 0.17 B/mutation, 0 Gen0 collections over 60k mutations.
- The AoS falsification test did **not** falsify AoS — chunked AoS is 21–46% *faster* than
  flat SoA; the ADR's "we accept worse sweep cache density" concession was unnecessary.
- Chunk size **32** confirmed.
- **GridMap at `cell_octant_size = 32` wins the backend question**: 32 draw calls and ~1.9 µs
  per dig, versus MultiMesh's 82 draw calls and ~452 µs per dig.
- **Not answered**: 60 fps on real hardware (software Vulkan only here).

## How to run

**Data-model half** (no engine needed):

```bash
cd prototypes/terrain-spike
dotnet run -c Release
```

Prints the correctness suite followed by every benchmark table in the spike note.

**Render half** (needs Godot 4.7.1 mono on `PATH` or edit `$GODOT`):

```bash
cd prototypes/terrain-spike/render
dotnet build
godot --path . -- backend=gridmap octant=32      # also: multimesh | multimesh_buffer | multimesh_pooled
```

Prints `RESULT key=value` lines (draw calls, video memory, build ms, per-dig update µs) and
saves a screenshot to the Godot user data directory. On a headless box, wrap with
`xvfb-run -a --server-args="-screen 0 1280x720x24"` and set
`VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.x86_64.json` for software Vulkan — but note
that frame rate from software rendering is meaningless; only the counters are valid.

## Layout

| Path | What |
|---|---|
| `TerrainModel.cs` | ADR-0002 implemented verbatim (facade, events, snapshot, bulk apply) |
| `Correctness.cs` | The 38 contract checks (ADR-0002 validation criteria 1–4) |
| `Benchmarks.cs` | Memory, sweep (AoS vs SoA), allocation, bulk, extraction, snapshot |
| `Program.cs` | Runner |
| `render/` | Godot project: MultiMesh vs GridMap backend comparison |

## Rules

Throwaway. Production code must never reference this directory; if ADR-0002 is promoted, the
production `TerrainWorld` is written fresh in `src/core/` against the ADR — this code is
reference only, never migrated.
