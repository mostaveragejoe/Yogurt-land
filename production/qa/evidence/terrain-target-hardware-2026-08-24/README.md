# Terrain target-hardware run — 2026-08-24

Evidence for **ADR-0002 validation criterion 5**, frame-rate and Gen0 clauses.

| Field | Value |
|---|---|
| GPU | NVIDIA GeForce RTX 3060 Ti |
| Godot | 4.7.2 stable **mono** (`ed1daf0bf`) — note: project pins 4.7.1; 4.7.2 is a patch release |
| Build config | **Debug** (Godot loads Debug when running a project from its folder) |
| Backend | `gridmap_two`, `octant=32`, `styles=1` — the adopted configuration |
| Window | 1800 measured frames after 300 warmup, 8 digs/frame, vsync off |
| Drivers run | Vulkan and D3D12 (D3D12 is the Godot 4.6+ Windows default) |

`software_rasterizer=False` on both runs — timings are admissible.

## Verdict

Both clauses **PASS**, with roughly 8x headroom.

| | Vulkan | D3D12 | Budget |
|---|---|---|---|
| Frame p99 | 2.167 ms | 2.024 ms | 16.6 ms |
| Frame mean | 1.538 ms | 1.754 ms | — |
| Worst frame | 50.310 ms | 45.000 ms | see caveat |
| Gen0 / Gen1 / Gen2 | 0 / 0 / 0 | 0 / 0 / 0 | 0 |
| Bytes per frame | 36.1 | 32.7 | ~0 |
| Draw calls | 32 | 32 | <=150 |
| Dig rebuild | 0.30 us | 0.30 us | — |

## Caveats recorded with the result

1. **One 50 ms frame per run**, far beyond p99 (1 frame in 1800). Reads as
   environmental — driver, OS scheduling, or compositor — because a systematic
   cost would have raised p99, which sits at ~2 ms. Not treated as a blocker.
   Worth one confirming re-run before Accepted.
2. **Video memory 43.32 / 49.61 MB** against a recorded 16.42 MB. Not a
   regression: `buffer_mem_mb` reads **16.23 MB** on both runs, matching the
   recorded figure almost exactly. The recorded number measures terrain
   *buffers*; the larger number is total video memory including render targets
   and swapchain at real resolution, which is framebuffer overhead rather than
   terrain's budget line. The label was wrong, not the measurement.
3. **Godot 4.7.2, not the pinned 4.7.1.** Patch release; no reason to expect a
   difference, recorded for provenance.
4. **Debug build, not Release.** Immaterial at 2 ms against a 16.6 ms budget.
5. `cells_with_BOTH_floor_and_wall=15043` differs from July's 15763 by design —
   the count is now taken after 300 frames of dig churn rather than on a
   pristine build. `render_matches_model=True` is the invariant, and it held.

## Still NOT measured

The checkpoint clause added by ADR-0002's 2026-08-03 Battle Persistence
amendment — checkpoint snapshot+write at per-activation combat cadence on
ADR-0004's double-buffered async path. **No implementation exists**, so
criterion 5 is not fully discharged and ADR-0002 stays **Proposed**.

## Files

- `terrain-vulkan.txt` — full Vulkan run output
- `terrain-d3d12.txt` — full D3D12 run output

Local Windows paths in the logs are redacted to `<user>`.
