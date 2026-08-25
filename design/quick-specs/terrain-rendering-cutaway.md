# Terrain Rendering & Cutaway — Quick-Spec

| Field | Value |
|-------|-------|
| **Systems index** | #7 (enumeration) / #11 (design order) — Core, MVP. **Absorbs Camera & Z-Level Visibility** |
| **Doc tier** | Quick-spec — ADR-0002 fixes the backend and the budgets; this spec fixes what the player sees and how the camera behaves |
| **Status** | Drafted 2026-08-24 |
| **Governing ADRs** | ADR-0002 (two stacked GridMaps at octant 32; GridMap is never authoritative; the style-variety draw-call curve) · ADR-0001 (`_Process` is presentation-only; no simulation in the render path) · ADR-0003 (views bind by `EntityId` and never write) |
| **Source evidence** | Terrain spike 2026-07-25/26 (backend selection, floor+wall resolution) · **target-hardware run 2026-08-24** (RTX 3060 Ti: 32 draw calls, p99 2.02–2.17 ms, 16.23 MB render buffers) — `production/qa/evidence/terrain-target-hardware-2026-08-24/` |
| **Depends on** | Terrain Data Model (#1), World Change Event Bus (#3) |

---

## 1. Purpose

This system turns `TerrainWorld` into the picture the player reads and controls the viewpoint they read it from. It owns the two stacked GridMaps, the damage overlay, the cutaway window, the treatment of non-focus Z-levels, and the camera.

It is **the only system in the MVP set whose code is Godot-side rather than plain C#** — everything here is a view over data it never owns.

**Explicitly out of scope** — named so they cannot drift in:

| Not owned here | Owner |
|---|---|
| Any authoritative terrain state | Terrain Data Model (#1) — GridMap is a write target, never truth |
| Palette, materials, lighting character, ambient floor | Art bible (`design/art/art-bible.md`) |
| Designation/blueprint overlay and the dormant-stair indicator | Blueprint UI (#26) |
| Colonist, raider, door and item visuals | Their own views (ADR-0003) |
| HUD, panels, notifications | Combat UI (#27) / Roster UI (#28) |
| Which cells are damaged, and by how much | Terrain (#1) `WallHp` + Material Catalog (#5) `MaxWallHp` — this system only *reads the ratio* |

---

## 2. Core Rules

### C1 — The render layer is a pure function of `TerrainWorld`

Every visual is derived by reading the model and applying change batches. The renderer never writes terrain, never caches authoritative state, and never runs simulation logic in `_Process` (ADR-0001).

> **Why restate an ADR rule.** ADR-0002 exists partly to stop GridMap becoming a second source of truth, and a render system is exactly where that erosion would start ("just store the last known wall type so we don't re-read"). Verified by AC-1: wiping and rebuilding the render layer from the model produces an identical picture.

### C2 — Two stacked GridMaps, octant 32, locked to `ChunkSize`

A **wall map** (full-cube items) and a **floor map** (thin slab items offset to the cell bottom), sharing coordinate system, cell size and octant. `cell_octant_size` **must** equal `TerrainWorld.ChunkSize` — both 32 — so a dirtied chunk maps 1:1 to a dirtied octant. This is a locked invariant from technical-preferences, not a tuning knob.

Measured on target hardware: **32 draw calls, 16.23 MB render buffers, 0.30 µs per dig.**

### C3 — The cutaway shows the focus layer plus two below it

Layers **above** the focus layer are not drawn at all — that is what makes it a cutaway. Layers **below** are drawn with progressive depth attenuation (C4). Default window depth is **3** (focus + 2).

### C4 — Non-focus layers are dimmed, not recoloured

Layers below the focus receive progressive darkening toward the ambient floor, with a light desaturation. They never receive a hue shift.

This is the art bible's **presentation-layer exemption** — a rendering channel like UI chrome, carrying no world meaning. The constraint that survives from Section 4: depth is never communicated by hue, so the attenuation must read as *further away*, never as *a different kind of rock*.

The ambient floor from the art bible's Rendering Constant applies to attenuated layers too — no visible surface crushes to true black, including two layers down.

### C5 — Below the cutaway floor, the world reads as darkness

Where a stair landing or void column falls past the visible window, it is **not** drawn and **not** window-extended. The window depth is uniform everywhere.

> **Why uniform.** Extending the window at stair cells gives a ragged silhouette and makes the draw-call count depend on map content rather than window depth — the one property that made the octant-32 budget predictable. **This is safe only because the cutaway is explicitly not the dormant-stair guarantee**: the terrain GDD's re-review recorded that a stair sealed below cutaway depth may be invisible in 3D, and routed that promise to the **#26 designation-layer indicator and the inspect view**, neither of which depends on cutaway depth. This treatment supplements them; it never substitutes for them. *(Closes terrain GDD Open Question #4.)*

### C6 — Camera: one fixed viewing angle, four 90° rotations, quantized zoom

*(Revised 2026-08-24 by user ruling — supersedes the earlier free-pitch proposal.)*

| Axis | Behaviour |
|---|---|
| **Projection** | Fixed. One parametric viewing angle, identical in every view |
| **Pitch** | **Not adjustable.** A single authored angle |
| **Rotation** | **4 steps of 90°.** The four cardinal views of the same fixed angle |
| **Zoom** | Quantized to discrete levels |

> **Why one angle beats a pitch range.** A single fixed angle means there is exactly one geometry for artists to author against — every texture, every silhouette, every ornament is drawn for the one view the player will ever see it in. A pitch *range* would have forced a choice of canonical authoring angle anyway, and every other angle would render art it was not drawn for. It also removes a whole class of camera-state complexity: the four views are the presets, so no reset control is needed.
>
> **Correction to the earlier rationale.** The previous version of this rule claimed texel density is driven by zoom and not pitch. That is true for the floor plane and false for walls — pitch changes vertical-surface foreshortening, so a wall's apparent texel density varies with it (art-director, gate review 2026-08-24). Fixing the angle removes the problem at its root rather than managing it.

**This closes the art bible's deferred camera decision**, and with it the dependency chain it flagged: camera → texel density → UI base pixel unit → icon sizes. Art bible Sections 3.1 and 3.3 are now unblocked for re-validation (§8 item 1).

### C7 — Damage is a sparse overlay that exists only where damage exists

Walls render in **three damage states** — intact, damaged, critical — read from `WallHp / MaxWallHp`. The count of three is fixed by the terrain GDD as a Pillar 3 legibility floor; the breakpoints are this spec's (§6).

Damage is drawn as a **separate sparse overlay** — one `MultiMeshInstance3D` per damage state, holding instances only for cells currently in that state. Intact cells contribute nothing.

**A destroyed wall leaves rubble** (user ruling 2026-08-24). When a wall is destroyed in combat the cell becomes open and walkable, and a rubble instance is placed on it. Rubble is **visual only** — it blocks nothing, costs nothing to clear, and carries no simulation state. It is a fourth instance kind in the same sparse overlay, so it inherits the overlay's cost properties: bounded by one mesh, instanced only where walls actually broke.

> **Why.** Without it a breach renders as an absence, identical to a cell the player deliberately dug. The after-action report can name where a wall failed; rubble is what makes that place findable when you walk the colony afterwards.

> **Why sparse rather than a third GridMap.** ADR-0002 assumed a third stacked map and flagged the problem: GridMap has no per-instance data channel, so each damage tier needs a distinct mesh item **per material/style combo**, multiplying against the measured ~8-variants-per-tier ceiling (1 variant → 32 draw calls, 2 → 48, 4 → 80, 8 → 144, against a ≤150 budget). Three damage states would treble the variant count — survivable at MVP's single ornament vocabulary, fatal at the art bible's two-vocabularies-plus-ornaments Vertical Slice target.
>
> **Mesh authoring: flat overlays for MVP** (user ruling 2026-08-24). Art-director flagged at the 2026-08-24 gate that flat alpha-cutout planes can read as stickers on a low-poly surface, and that volumetric meshes read as scars. Volumetric is the better end state; flat is what ships first, and playtest decides whether it needs upgrading. Recorded so the trade-off is a known choice rather than an oversight.
>
> A sparse overlay decouples the two axes entirely. Cost is bounded by **three meshes**, not by material × style × damage, and instance count scales with *how much is broken* — normally one breach site, not the map. It also matches the fiction: damage is a scar on a surface, not a different surface. **Still needs its own measurement (§7b), but the expected answer is a small constant rather than a multiplier.**

### C8 — Rebuilds are octant-aligned and batched per frame

Change batches mark octants dirty; dirty octants rebuild once per frame, never once per change. Concentrated destruction is sublinear (measured: 8 cells 11.9 µs, 75 cells 18.5 µs on target hardware), so a combat AoE costs little more than a single dig.

---

## 3. Public Interface

Godot-side, `Hollowdeep.Views.Terrain`. C# `partial` classes per the project naming convention.

```csharp
public partial class TerrainRenderer : Node3D
{
    public int  FocusLayer { get; private set; }
    public int  WindowDepth { get; private set; }   // default 3 (C3)

    public void SetFocusLayer(int z);               // clamped to world bounds
    public void OnTerrainChanged(/* batch */);      // marks octants dirty; ADR-0002 rule: copy out synchronously
    public void RebuildAll();                       // WorldReloaded / load path
}

public partial class TerrainCamera : Node3D
{
    public int RotationStep { get; private set; }   // 0..3, 90 degrees each
    public int ZoomLevel    { get; private set; }   // discrete

    public void RotateBy(int steps);                // wraps 0..3
    public void ZoomBy(int levels);
    // No pitch control by design (C6). No preset cycle - the four rotations are the presets.
}
```

`TerrainChangeBatch` is a `ref struct` and must not be retained beyond `Publish` (ADR-0002) — the handler copies the dirty octant coordinates out and returns.

---

## 4. Behavior Under Each Time Authority

*(Mandatory — routing policy.)*

Rendering is **presentation, not simulation**. It registers no `ITickable` and advances no state in either authority; it runs in `_Process` and reads. Camera input is player input, not simulation input, so it is live in both modes — including while the colony is paused.

| | **RealTime** | **TurnBased** |
|---|---|---|
| Focus layer | Player-driven | Player-driven, **and auto-focuses the active actor's layer** at turn start |
| Camera control | Full (C6) | Full — the player must be able to inspect the battlefield freely while deciding |
| Rebuild trigger | Change batches from colony work | Change batches from combat destruction |
| Rebuild cadence | Dirty octants, once per frame | Identical — no mode-specific path |

**A hitch is more visible in TurnBased**, where the camera is often still and the player is reading the board, which is why C8's batching applies uniformly rather than being relaxed during colony play.

---

## 5. Dependencies

**Upstream** — Terrain Data Model (#1, bulk chunk reads + change batches); World Change Event Bus (#3); Material Catalog (#5, `MaxWallHp` for the damage ratio, and material → mesh/material mapping).

**Downstream** — Blueprint UI (#26, draws designations over this view and owns the dormant-stair indicator); Combat UI (#27); Spatial Query (#12, shares the focus-layer notion for LOS presentation); Map Authoring (#14, the editor-facing view).

---

## 6. Tuning Knobs

Values live in `assets/data/rendering.json`, not hardcoded.

| Knob | Default | Range | Category | Rationale |
|---|---|---|---|---|
| `WindowDepth` | 3 | 2–5 | feel | Focus + 2 below. Deeper costs draw calls linearly and muddies the read |
| `DepthDimStep` | 0.35 | 0.15–0.6 | feel | Darkening applied per layer below focus, toward the ambient floor |
| `DepthDesaturateStep` | 0.20 | 0–0.5 | feel | Light desaturation per layer. Never a hue shift (C4) |
| `DamagedBreakpoint` | 0.66 | 0.5–0.8 | curve | `WallHp/MaxWallHp` below this reads as damaged |
| `CriticalBreakpoint` | 0.33 | 0.15–0.5 | curve | Below this reads as critical. Must stay below `DamagedBreakpoint` — load-validated |
| `RotationSteps` | **4** | fixed | gate | 90° steps — the four cardinal views. Not a knob (C6) |
| `ZoomLevels` | 5 | 3–8 | feel | Discrete stops; protects texel density (C6) |
| `ViewPitch` | one authored angle | fixed | gate | Not adjustable. The single angle all art is authored against (C6) |
| ~~`CameraPresets`~~ | — | — | — | Removed: the four rotations **are** the presets (C6) |

`cell_octant_size` is **not** a knob — it is locked to `ChunkSize` (C2).

---

## 7. Acceptance Criteria

### (a) Headless / automated where possible — Logic, **BLOCKING**

- [ ] **AC-1** `RebuildAll()` from the model produces a render state identical to the incrementally-updated one after an arbitrary mutation sequence. *(C1 — the anti-drift test)*
- [ ] **AC-2** The renderer performs zero writes to `TerrainWorld`; a CI grep finds no mutation call in the view assembly. *(C1)*
- [ ] **AC-3** `cell_octant_size == TerrainWorld.ChunkSize` is asserted at startup. *(C2)*
- [ ] **AC-4** Damage state is a pure function of `WallHp/MaxWallHp` against the two breakpoints, and a config with `CriticalBreakpoint >= DamagedBreakpoint` fails to load. *(C7, §6)*
- [ ] **AC-5** No input path changes pitch; rotation lands only on the 4 cardinal steps and wraps; zoom lands only on discrete levels. *(C6)*
- [ ] **AC-6** `RotateBy` from any step returns to the starting view after four rotations in the same direction. *(C6)*

### (b) Performance — target hardware, **BLOCKING regression gate**

Bands sit above the 2026-08-24 measurements.

- [ ] **AC-7** Wall + floor maps, 3-layer cutaway, one style per tier: **≤ 40 draw calls** *(measured 32)*
- [ ] **AC-8** Frame-time p99 **≤ 4 ms** under sustained digging *(measured 2.02–2.17 ms)*
- [ ] **AC-9** Terrain render buffers **≤ 20 MB** *(measured 16.23 MB)*
- [ ] **AC-10 — the damage-overlay spike, TR-terrain-044.** With every wall damaged across the visible window, total terrain draw calls stay **≤ 150**, and the overlay's own contribution is **independent of material and style count**. *(C7 — this is the ADR-0002 open item; it must be measured before the render backend is treated as settled)*
- [ ] **AC-11** Zero steady-state allocation in the rebuild path; 0 Gen0 collections over a sustained window. *(technical-preferences standard)*

### (c) Visual / feel — screenshot + lead sign-off, **ADVISORY**

- [ ] **AC-12** In a 3-layer cutaway, the focus layer is unambiguously the focus at a glance, and no visible surface crushes to black. *(C4, art bible ambient floor)*
- [ ] **AC-13** The three damage states are distinguishable in a single screenshot without a legend. *(C7 — the Pillar 3 legibility floor)*
- [ ] **AC-14** At the fixed angle, all four rotations read correctly: cutaway layering is legible, and wall height and cover are readable for tactics in every view. *(C6)*

### (d) Integration — **BLOCKED on siblings; does not gate this system's Done**

- [ ] **AC-15** Designations and the dormant-stair indicator read correctly over the cutaway, including for stairs below the window — blocked on #26 *(C5's carried obligation)*

---

## 8. Open Questions & Routed Items

| # | Item | Routed to | Trigger |
|---|---|---|---|
| 1 | **Art bible Sections 3.1 / 3.3 re-validation is now unblocked.** C6 closes the camera decision the art bible deferred, so texel density, UI base pixel unit and icon sizing can be fixed against real numbers instead of placeholders | art-director | Now — nothing else blocks it |
| 2 | **Damage-overlay draw-call measurement** (AC-10 / TR-terrain-044). The sparse design is expected to cost a small constant, but ADR-0002's caveat stands until measured | This spec's own spike | Before the render backend is declared settled; runs on the same harness as the 2026-08-24 run |
| 3 | **Many-local-lights evaluation and `AreaLight3D`** for "Warm Hearth, Cold Dark" claimed-territory glow. Still open from technical-preferences; the cutaway's dimming interacts with it | art-director + technical-artist | The art/lighting pass |
| 4 | **Style-variety ceiling at Vertical Slice.** The sparse overlay removes damage from the multiplier, but two ornament vocabularies plus ornament sets still push against the ≤8-per-tier curve. Raising it means revisiting chunk size and octant size **together**, never octant alone | art-director + technical-director | The art bible palette spec (already terrain GDD OQ#9) |
| 5 | **Auto-focus on the active actor** (§4) may fight a player who has deliberately framed elsewhere. Likely wants a "don't steal my camera" rule | Combat UI (#27) | At #27's authoring |
