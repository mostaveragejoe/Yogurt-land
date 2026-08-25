# Art Bible: Hollowdeep

*Created: 2026-07-24*
*Status: Sections 1–4 complete and gate-reviewed; 5–9 skeleton*

> **Art Director Sign-Off (AD-ART-BIBLE)**: REVISED 2026-07-24 — creative-director returned CONCERNS (5 substantive + cleanup); all recommended fixes applied same day. Scope reviewed: Sections 1–4 (visual identity core).

> Scope note: This is a constraint document. Every section narrows the visual
> solution space in exchange for coherence. Sections marked `[To be designed]`
> are placeholders awaiting a dedicated authoring session.

---

## 0. Asset Authorship Policy — BINDING

**All art assets for this project are made by a person. No AI-generated art ships in this game.**

Recorded as a standing project rule 2026-08-24 by user ruling. This is not a style preference and not a per-asset judgement call — it governs every texture, mesh, icon, UI element and animation in the game.

Practical consequences, so this is not rediscovered later:

- `/asset-spec` produces **written specifications and briefs for a human artist**, never generation prompts. Any template section that assumes AI generation is to be read as a brief, or skipped.
- Art production is a **real person's job and a real budget line**. Every scope decision touching asset count is also a decision about that person's time.
- The kit-of-parts discipline in Section 3 matters more under this policy, not less: fewer, more reusable pieces is how a human-authored pipeline stays feasible.
- The ~8-variants-per-tier draw-call ceiling and the asset-count implications of Sections 5-9 are **hiring and scheduling constraints**, not just technical ones.

---

## 1. Visual Identity Statement

### The Rule

> **Light sets the mood. Shape tells your story.**

Warmth is the game's aesthetic soul — the Warm Hearth palette is the visual mood of Hollowdeep, and it stays exactly that: mood. It is not a claim signal and not an authored code the player must learn: no mechanic queries it and no logic branches on it — no system, anywhere in the game, ever reads the state of a light. That is a strict engineering rule, not a suggestion. It does not mean the player can't *notice* the physical consequences of light and dark — a dark gap where a lamp used to stand before a breach is simply what destroying a lamp looks like, and the player is welcome to feel that. The rule only forbids meaning being *authored* into that perception: nothing in combat, threat state, or repair logic ever reads or reacts to it, and no design decision should ever start from "what should this color communicate about game state." Lighting exists to make the colony feel beautiful and lived-in, full stop. What actually carries meaning — who you are, what you value, how you think — is the architecture itself: the shapes, styles, and materials the player chooses to build, anywhere, at any depth, from day one.

### Supporting Principles

1. **The Palette Is the Player's Voice** *(Pillar 1 — The Blueprint Is the Player)*
   Every construction style, ornament set, and material finish in the building palette is available everywhere, immediately — there is no pre-existing architecture to discover and nothing to unlock by going deeper. The world generates as untouched natural landscape only; everything built is something the player chose to build.
   *Design test: when deciding whether a style, finish, or ornament should be restricted by depth, biome, or progression, the answer is no — restrictions may only ever be resource, time, or skill cost, never a stylistic lock.*

2. **Legibility Before Beauty** *(Pillar 1 — The Blueprint Is the Player)*
   Ornament and material flourish must never obscure the read of a corridor's width, a chokepoint, or a breach point. The floor plan is the primary content of every screen.
   *Design test: when a decorative element competes with the silhouette of a tactically meaningful space, the ornament loses — simplify or relocate it.*

3. **The Wild Deepens, the Built Doesn't** *(Pillar 5 — Depth Is the Frontier)*
   Depth needs to feel meaningful, but that meaning lives in the untouched world the player is carving into, not in what they're allowed to build once they get there.
   *Design test: when a location needs to communicate depth or danger, express it through natural terrain — rock strata, mineral veins, encroaching hazard density — never by gating or reskinning player architecture.*

### Resolution: 3D Pixel Art

Hollowdeep commits to 3D pixel art: low-poly, grid-aligned meshes built as a reusable `MeshLibrary` kit-of-parts, dressed with low-resolution, nearest-neighbor-filtered textures at a fixed, documented texel density (exact value set in Section 8 Asset Standards, alongside the terrain data-model ADR). This is the right call for a solo, multi-year project — chunky pixel textures on simple meshes are cheap to author, forgiving of imperfection, and far less costly than a PBR pipeline requiring sculpting and multi-map baking. The one real cost is texel-density discipline, enforced via import presets and an atlas template — a tooling cost, not a per-asset skill cost.

Godot 4.7.1 supports this cleanly: force Nearest filtering per-import or project-wide, limit mipmaps to avoid shimmer at distance, and lighting (including `AreaLight3D`'s glow and HDR output) computes per-fragment independent of texture resolution — the warm aesthetic renders correctly over chunky pixel textures with no special-casing.

**Deferred**: the camera question (fixed/quantized isometric à la Gnomoria vs. free orbit) is deliberately NOT locked here — it must also serve turn-based tactics readability. It gets a dedicated Camera & Composition decision later.

**Presentation-layer exemption**: multi-Z-level focus treatment — the cutaway/slice view that dims, desaturates, or ghosts non-focus Z-levels so the player can see the level they're working on — is explicitly exempt from the world-color rule above, in the same exemption class as UI chrome (Section 4.2). It is a rendering/presentation channel, not a world-material or lighting truth, and carries no more "meaning" than a UI panel border does. The actual treatment (how much dimming, whether levels above vs. below the focus level get different treatment, opacity vs. desaturation vs. both) is deferred to the Camera & Composition decision, since it's inseparable from the camera choice itself.

**Dependency chain to flag**: camera decision → texel density → UI base pixel unit → icon sizes. The camera decision (fixed/quantized vs. free orbit, and now also the Z-level cutaway treatment) constrains how large a colonist silhouette reads on-screen, which constrains the texel density Section 1 defers to Section 8, which constrains the UI's base pixel unit (Section 3.3) and therefore icon sizing (Section 3.3's 16–24px thumbnail test). **Sections 3.1 and 3.3 need re-validation once the camera decision lands** — their current numbers are reasonable placeholders, not load-bearing until camera is locked.

### Rendering Constant: Minimum Ambient Floor

To make lighting genuinely optional rather than secretly load-bearing, Hollowdeep defines a documented **minimum ambient fill** as a global rendering constant, applied in every state — including a fully unlit player base in tactics mode: no surface the camera can see ever crushes to true black. This is a rendering floor, not a mechanic — it exists so an all-torches-extinguished corridor stays legible (if bleak) geometry, never an unreadable void the player is punished for failing to light. The exact value (a minimum luminance/ambient-occlusion floor set on the `WorldEnvironment` resource) is fixed in Section 8 Asset Standards alongside texel density. Section 2.1's "nothing crushed to black, nothing blown out" is this same constant, not a peacetime-only choice.

### Bavarian + Roman Integration

Both references are **style vocabularies in the building palette, not stages of a progression** — a player can raise Roman-coded vaults at the surface or fachwerk cottages at the colony's deepest point on day one; nothing gates one by depth or material tier. Materials look like what they are and light does what light does: a Roman stone wall lit warmly looks warm, exactly like a lit Bavarian plaster wall — no hue constraint distinguishes them.

Practically, both vocabularies share one grid-based kit-of-parts (wall, doorway, ceiling, support modules) with swappable dressing — exposed beam framing and painted bauernmalerei-style folk trim for the Bavarian set; rounded arches, engaged pilasters, and vaulted ceiling silhouettes for the Roman set — keeping authoring cost sublinear rather than doubling the kit. This is a budget decision, not a lore one. The Roman set stays stylized and legible rather than literal (no full classical-order capital libraries — ornate sculptural detail reads as noise at pixel-art fidelity, in direct tension with Legibility Before Beauty). The Bavarian set is a coded construction-and-color influence rather than a literal costume simulation; character/culture specifics belong to Section 5.

---

## 2. Mood & Atmosphere

Every state shares one lighting truth per Section 1: warmth comes from where the player put hearths and lamps, coolness comes from unlit rock, and none of it means anything mechanically. Distinctness between states comes from four solo-dev-cheap levers: **light placement/count** (world lighting, not new logic), **color grading** (a `WorldEnvironment`/`Environment` resource swap in Godot — no new assets), **post-processing** (vignette, DOF, glow — engine-level toggles), and **camera/UI/pacing**. No state requires bespoke geometry or textures beyond what the destruction and building systems already produce.

### 2.1 Colony Mode — Settled / Peacetime

- **Emotional target**: The specific pride of watching a home you built keep working without you standing in it — unhurried domestic contentment, quietly proud rather than merely "cozy," with an undertone of watchfulness since you know it won't stay quiet forever.
- **Lighting character**: Warm-dominant. Many small local warm sources (hearths, lamps, candles) placed by the player, each with soft falloff, pooling light room-to-room rather than one flat wash. Ambient fill stays low and cool-neutral so the warm pools read as the eye's destination. Low-to-moderate contrast overall — nothing crushed to black, nothing blown out (the global minimum-ambient-floor rendering constant defined in Section 1, applied here, not a peacetime-only choice).
- **Atmospheric descriptors**: lived-in, industrious, unhurried, intimate, steady.
- **Energy level**: Low-moderate, continuous — colonists moving on their own idle loops, no player-driven urgency.
- **Visual element that carries the mood**: A hearth or oven with a slow steam/smoke curl, colonists' small idle-task animations visible in the pixel-art silhouette nearby (stirring, hauling, sitting) — motion that reads as a home functioning, not a stage set.

### 2.2 Colony Mode — Wilds / Frontier

- **Emotional target**: The held breath before a decision — vast and indifferent rather than hostile, the specific thrill of standing at a cave mouth with one lantern, more expectant than foreboding.
- **Lighting character**: Cool-dominant, blue-grey ambient with no local warm sources (nothing built there yet). Higher contrast right at the frontier edge, where the last built room's warm spill meets raw dark — that boundary is the single most beautiful frame in this state. Deep in unexcavated rock, light is directionless and ambient-occlusion-heavy; optional cool bioluminescent mineral-vein accents add color variety without carrying any meaning.
- **Atmospheric descriptors**: vast, hushed, mineral, indifferent, expectant.
- **Energy level**: Low, slow — minimal motion beyond dust/particle drift and the player's own excavation cursor.
- **Visual element that carries the mood**: The literal seam where a cut stone wall meets unexcavated rock — pixel-textured strata bands visible in cross-section, the clearest "this is the edge of what you've made" image in the game.

### 2.3 Tactics Mode — Combat Event

- **Emotional target**: Coiled clarity — the sharpened, controlled adrenaline of aiming down a hallway you built yourself, because the hard thinking already happened in prep; tense focus, not horror or panic.
- **Lighting character**: **Unchanged from whatever the physical space already is.** This state does not relight the scene or recolor materials — same lights, same rooms, same warm/cool balance as colony mode. All mood shift is carried by:
  - **Camera**: tighter framing, pulled closer into the corridor/room geometry the player carved, locked or constrained angles per engagement rather than free colony-cam roam.
  - **UI**: turn-order ribbon, action-point ghosting, high-contrast selection/reachable-tile outlines rendered as pixel-art HUD overlays on top of the unchanged scene.
  - **Pacing**: turn-based stillness punctuated by decisive action beats — long holds, sharp payoffs.
  - **Post-processing**: subtle vignette tightening during aiming/targeting, brief desaturation or time-dilation pulse on a resolving hit, restored to normal grading between turns.
- **Atmospheric descriptors**: coiled, deliberate, high-stakes, procedural, focused.
- **Energy level**: High but controlled — sharp spikes on action resolution, stillness between turns.
- **Visual element that carries the mood**: The reachable-tile/line-of-sight overlay itself — amber-outlined tiles for clarity (not threat-coded color), drawn directly over the same lit corridor the player designed, making the tactical read entirely a function of the architecture, not the lighting.

### 2.4 Post-Battle Aftermath / Rebuild

- **Emotional target**: Grief-tinged competence — the particular quiet of clearing rubble at dawn; the work of putting a home back together, not despair.
- **Lighting character**: Warm/cool balance shifts only because the physical world changed, not because a mood-system decided it should — light sources destroyed in the fight are simply out (literal consequence of destruction, not a coded signal), leaving larger dim/cool patches next to small moving warm pools from lanterns carried by repair crews. Contrast is higher than peacetime for exactly this reason: small warm work-light against bigger dark gaps.
- **Atmospheric descriptors**: subdued, dusty, resilient, hushed, procedural.
- **Energy level**: Low, slow, deliberate — a cleanup-montage pace, not urgent.
- **Visual element that carries the mood**: Dust motes drifting through a breached wall, cool ambient light from the unexcavated dark spilling into a room that used to be fully warm-lit — the same frontier-edge visual language from 2.2, now happening inside a room that used to be finished.

### 2.5 Menus / Blueprint Overlays

- **Emotional target**: The calm authorship of leaning over a drafting table — unhurried, tactile problem-solving pleasure, not sterile software efficiency.
- **Lighting character**: This is UI space, not world space — it doesn't use scene lighting at all. Base tone is a warm parchment/vellum neutral, evenly lit as if by a single soft desk lamp, low internal contrast so icons and text stay legible at pixel-art resolution.
- **Atmospheric descriptors**: crafted, tactile, focused, warm-utilitarian, unhurried.
- **Energy level**: Low, entirely player-paced — no time pressure beyond whatever alert badges the player has chosen to leave visible.
- **Visual element that carries the mood**: The designation/blueprint overlay itself rendered as amber linework on graph-paper texture ghosted over the pixel-art world beneath it — literally a hand-drafted technical drawing, with panel-frame flourishes borrowed from the Bavarian folk-trim vocabulary (Section 1) as a UI skin choice, not a semantic color cue.

### Feasibility note

All five states are achievable with `WorldEnvironment`/`Environment` resource swaps for grading and glow, camera rig changes, a UI CanvasLayer overlay pass, and standard post-processing toggles — no bespoke per-state art. **Implementation non-goal to hand to `technical-artist`/`godot-specialist`**: no "combat lighting" pass may sneak in for drama — tactics mode inherits the scene lighting exactly as-is, per Section 1's lighting-is-aesthetic-only rule.

---

## 3. Shape Language

Every principle below is a direct extension of the Rule — *Light sets the mood, shape tells your story* — applied to geometry. Sections 1 and 2 took color and light off the table as meaning-carriers, so shape is the one channel left to do that work. Nothing here restricts what the player can build (Bavarian and Roman dressing both remain available anywhere, per Section 1): these are authoring rules for the game's own characters, kit-of-parts, and UI — not new constraints on player expression.

### 3.1 Character Silhouette Philosophy

*Anchors: Pillar 2 — Preparation Gets Tested; Pillar 4 — The Colony Lives Without You*

Colonists and raiders read at tile-grid, pixel-art scale — small moving silhouettes glimpsed mid-task or mid-engagement. Archetype must be legible from **silhouette alone**: proportion, gear-shape, and pose, not palette.

- **Proportion direction**: chunky over elongated — stout, toylike mass rather than realistic elongation. Reads better at small scale, matches the rounded warmth of bauernmalerei folk-art figures, and forgives low-poly/low-texel construction. The head is kept slightly oversized to carry hat/hair/role silhouette without extra texel budget on a face.
- **Archetype-by-silhouette, not color**: worker, soldier, and raider must be tellable apart in grayscale at thumbnail size. Workers read soft and rounded (tool-in-hand, apron/satchel). Soldiers read squared and weapon-forward (shoulder mass, blocky plates, upright posture). Raiders read broken and asymmetric (mismatched gear, ragged profile, off-axis posture) — deliberately the least "orderly" shape family in the game, in contrast to everything the colony builds.
- **"Know the class before the person"** (the FFT reference made literal): squad composition must read from silhouettes at a glance, before any individual identity resolves — preparation depends on reading a threat *before* the engagement (Pillar 2).
- **State, not just archetype**: idle, working, and alert/combat states read through pose bias (relaxed vs. tense stance, tool-out vs. weapon-out) so the player can tell what the colony is doing without opening a UI panel (Pillar 4 — silhouette is the primary feedback channel for autonomous colonists).

**Design test**: when two archetypes or states could be told apart by a color swap alone, reject that solution and find a silhouette solution — proportion, gear shape, or pose — instead.

**Production budget** (MVP-era; revisit only by deliberate decision, never by organic accumulation): a fixed **6 silhouette families** — 3 colonist work-archetypes, 1 soldier, 2 raider families — and a fixed **3 pose-bias states** per family (idle / working / alert). Gear and role variation within a family is expressed as a small set of swappable silhouette props (tools, weapons, headgear) mounted on **one shared base rig**, never as per-class meshes. This is what keeps the chunky-proportion, silhouette-first system in budget for a solo dev; expanding family or pose-state count is a scope call for `producer`, not something that should creep in per-asset.

### 3.2 Environment Geometry

*Anchors: Pillar 5 — Depth Is the Frontier (wild); Pillar 1 — The Blueprint Is the Player (built)*

The tile grid gives rectilinear geometry for free, and that default split is the story shape tells: **wild terrain reads as irregular and force-shaped; built architecture reads as rectilinear and tool-shaped.** This is an emergent visual fact, not a mechanical signal — nothing reads it, nothing reacts to it; it's just what carving looks like next to what nature looks like.

- **Wild terrain vocabulary**: faceted, jagged, irregular masses — randomized per-cell corner chamfers, broken edges, and rubble-variant meshes layered onto the same grid, not true sculpted organic geometry (the kit budget can't afford it). The wild should never accidentally read as "designed."
- **Built architecture vocabulary**: rectilinear-dominant — flat planes, even coursing, deliberate right angles and beveled edges — the visible signature of tool-and-intent, underneath both the Bavarian and Roman dressing families.
- **Where curves fit**: Roman arches, vaults, and columns are the one deliberate exception, budgeted as a **bounded module family** — a small fixed set of arch/vault/capital meshes authored once at standard grid-cell spans (1-cell / 2-cell arch, fixed vault height) that drop into the same slots as rectilinear pieces. One standardized curve radius means one texture treatment works everywhere (curved surfaces stretch pixel-art UVs unpredictably otherwise). Curves stay rare enough that encountering one reads as "this took real skill."
- The full vocabulary is available to the player anywhere, at any depth, per Section 1 — the rectilinear/curve split is production discipline for the kit, not a placement rule for the player.

**Design test**: when unsure whether a geometric element belongs to the wild or built vocabulary, ask whether it reads as tool-precise (rectilinear, even, deliberate) or force-of-nature (irregular, faceted, chamfered) — never split the difference into "stylized in-between" geometry; ambiguity here undercuts the carving fantasy.

### 3.3 UI Shape Grammar

*Anchor: Pillar 1 — The Blueprint Is the Player*

Extends Section 2.5's drafting-table/parchment/amber-linework direction: the UI is an extension of the same hand-drafted, tool-shaped identity as the built environment, not a separate "modern app" layer.

- **Corner treatment**: chamfered (straight diagonal cut), not rounded — a chamfer is a crisp diagonal that renders clean at pixel resolution and matches drafting-paper and carved-wood-panel corners; rounded corners anti-alias poorly and read mushy.
- **Panel framing**: bordered like a technical drawing's title block — a consistent ruled edge, with Bavarian folk-trim corner flourishes reserved for hero panels only (main menu, major overlays) so they stay a flourish rather than noise. Legibility Before Beauty applied to UI: ornament concentrates only where it can't compete with information.
- **Icon silhouette rules**: single- or double-weight amber linework pictograms, silhouette-first — every icon must read correctly as a solid shape at 16–24px before any internal line detail is added (the same thumbnail test as 3.1).
- **Grid alignment**: UI panels and icons snap to the same base pixel unit as the world's texel density (Section 1) — no non-integer scaling blur.

**Design test**: when a UI shape choice could go soft-modern (rounded pills, drop shadows, blur) or hand-drafted (chamfered corners, etched linework, ruled borders), choose hand-drafted — the world's shape language shouldn't disappear the moment the player opens a menu.

### 3.4 Hero Shapes vs. Supporting Shapes

*Anchors: Pillar 3 — Scars Teach (breach points); Pillar 1 — The Blueprint Is the Player (everything else)*

Not everything can draw the eye, or nothing does — deliberate figure/ground hierarchy that also keeps the kit budget sane.

- **Hero shapes** (bespoke silhouette, scale break from the grid rhythm, ornament concentration): hearths and lamps, doorways/thresholds, the wild/built frontier seam, colonists, and breach/damage points — exactly the "visual element that carries the mood" entries from Section 2. Breach points earn hero-shape treatment to serve Pillar 3: a wound in the architecture must be instantly recognizable as a wound, not blend into the wall run around it.
- **Supporting shapes** (repeated, deliberately plain, low ornament): wall runs, floor tiles, filler corridor lengths, generic stone/plaster facing. Variation handled at the texture level (grime, wear, tone noise) rather than geometry — cheap, repeatable, and receding.

**Design test**: when deciding whether an element deserves hero-shape treatment, ask whether the player needs to recognize it instantly across a busy screen (hearths, doorways, breach points, colonists) or whether it's structural filler (wall runs, floor tiles) — keep hero-tier elements to roughly **3–5 simultaneously prominent per typical screen composition**; past that, nothing reads as important.

**Forward declaration for Section 6**: breach/damage points being a hero shape (above) only solves *that* damage is legible — it doesn't yet solve *what kind* of damage is legible. Pillar 3 (Scars Teach) needs collapse, burn, and battering damage to be visually distinct from each other, not just distinct from undamaged geometry, so a player can read the engineering lesson (a burned granary reads differently from a battered gate) at a glance. That taxonomy is Environment & Level Art's job — Section 6 must author it before any damage-state assets are built.

---

## 4. Color System

Two disciplines govern everything below. First, per Sections 1–3: **materials look like what they are and light does what light does** — the world's color is never a code to be read, only a beauty to be seen. Second, pixel-art fidelity means every individual texture draws from a tight, quantized sub-palette rather than free gradients — cheap to author, coherent by construction. The tool for both is a single **master palette resource** (a shared `.tres`/palette asset in Godot) that world materials, UI, and per-texture sub-palettes all draw their values from — "Hearth Amber" is one token used everywhere it appears, not several values drifting apart across assets.

*(Hex values are indicative starting points for the master palette, not final color-managed output — to be refined once real pixel-art textures and Godot's HDR pipeline are in hand.)*

### 4.1 Primary Palette

| Color | Indicative Hex | Role — where it lives in this world |
|---|---|---|
| **Hearth Amber** | `#E8A23D` | The warmth of habitation. The color of every hearth, lamp, and candle the player has ever lit — the game's aesthetic soul, per Section 1. |
| **Parchment Cream** | `#E4D3B4` | Warmth without saturation. Whitewashed plaster, vellum, bare timber-infill — the neutral surface that lets amber light and folk accents do the talking. |
| **Timber Umber** | `#6B4226` | The hand of the builder. Raw and finished wood — fachwerk beams, furniture, doorframes — the material color of craft itself. |
| **Deep Stone Grey** | `#5E6670` | The mountain, honestly rendered. One true material color for raw and worked stone alike — it reads cool in the unlit wilds and warm under hearth light for the same reason a real rock does: because of what's actually lighting it, never because of a coded state. |
| **Mineral Vein Accent** | `#4F8A72` (verdigris) / `#C9C2B0` (quartz) | The beauty found in depth. Rare, decorative color variation in unexcavated strata — a visual reward for descending, never a danger-tier signal (see 4.3). |
| **Bauernmalerei Red** | `#B23A2E` | The celebratory human hand. A saturated folk-painted trim color, available to the player anywhere, on anything, from day one. |
| **Alpine Folk Blue-Green** | `#3A6B6E` | The cooler counterpart to Folk Red — the other half of the traditional bauernmalerei accent pair, equally a free player choice. |

### 4.2 Semantic Color Usage (UI-Layer Only, Minimal)

The world carries zero semantic color. The *only* place color means something fixed is a small set of UI-chrome conventions — three assignments, each reusing a primary-palette token where possible:

| Semantic role | Color | Rationale |
|---|---|---|
| **Active / valid / selected** | Hearth Amber (reused) | Sections 2.3 and 3.3 already established amber linework as the game's "clarity" overlay (reachable tiles, blueprint drafting). Reusing it means the player learns one visual convention, not two. |
| **Invalid / blocked / threat notification** | **Alert Crimson** `#C43B2E` (UI-exclusive) | The near-universal "stop, look here" convention — lowest learning cost. Deliberately kept out of the world palette (never on a material or a light) so it stays unambiguous, and kept visually distinct from Bauernmalerei Red (which is warmer, dustier) so a red-trimmed door and a "blocked" cursor never read as the same thing. |
| **Disabled / unavailable** | Deep Stone Grey, desaturated (reused, dimmed) | Reuses an existing token; low saturation is the standard "inert" convention. |

That is the entire semantic set. Everything else — every material, strata band, and painted trim choice — stays purely aesthetic.

**Design test**: when a proposed color assignment would let a player infer a mechanical state from hue — anywhere, world or UI — reject it and move that information to shape, icon, or motion instead.

### 4.3 Per-Area Color Temperature (Natural Strata)

Depth communicates through terrain irregularity and hazard density (Section 3), never through hue — no single Z-level's color ever correlates with hazard tier, and no gradient is fixed or predictable region-to-region. Strata coloration is instead drawn from **depth-weighted probability pools with heavy overlap between adjacent bands**, so a cool/dark trend is only ever felt across hundreds of meters of descent — never diagnosable at any one dig site:

- **Near-surface pool** (dominant near the surface, tapering with depth, never absent): warmer-neutral grey-browns, faint iron-oxide staining bleeding down from soil above.
- **Mid-depth pool** (dominant mid-range, present in traces both near-surface and deep): cooler blue-grey slate and schist tones — Deep Stone Grey's natural range.
- **Deep pool** (dominant far down, present in rare traces even near-surface): darker charcoal-blue basalt and granite, punctuated by rare Mineral Vein Accents (verdigris copper, white quartz, ochre streaks).

These three descriptions are **pool centers, not fixed assignments** — every generated region samples across all three pools with depth-weighted probability, and the overlap between adjacent pools must be wide enough that two players digging at the same Z-level in different regions get visibly different strata color. That overlap is the mechanism, not a caveat on top of it: it's what keeps the trend a felt, geological beauty rather than a readable difficulty ramp.

### 4.4 UI Palette

The parchment/vellum + amber linework system from 2.5 and 3.3, built almost entirely from tokens already defined in 4.1 so world and UI stay of-a-piece:

| Role | Color | Source |
|---|---|---|
| Panel base | Parchment Cream `#E4D3B4` | Reused from primary palette |
| Ink / body text / ruled lines | Warm Charcoal `#3B2E23` | New — warm near-black, never pure black: hand-drafted ink, not digital text |
| Linework / overlay accents | Amber Linework `#D98F2B` | Tuned variant of Hearth Amber for legibility against the cream base |
| Hero-panel corner flourishes | Bauernmalerei Red / Alpine Folk Blue-Green | Reused, reserved for hero panels only per Section 3.3 |
| Semantic layer | Alert Crimson / desaturated Deep Stone Grey | As defined in 4.2 |

### 4.5 Colorblind Safety

Because the world carries no color semantics, the entire exposure is the three-color UI set in 4.2 — small enough to fully audit:

- **Amber (active) vs. Alert Crimson (invalid/threat)**: the at-risk pair — both warm hues, and red-based color-vision deficiencies compress exactly this distinction. Required backups: distinct icon glyphs (outline/checkmark language for amber; X/exclamation for crimson, per Section 3.3's silhouette-first icon rule), a deliberate value/saturation gap (crimson darker and more saturated), and behavioral reinforcement (crimson states pair with a blocked-cursor icon or reject animation — never color alone).
- **Alert Crimson vs. Bauernmalerei Red**: different visual layers (flat UI chrome vs. 3D pixel-art world) make confusion unlikely; the two are kept at different value/saturation as a safety margin regardless.
- **Disabled grey vs. panel neutrals**: desaturation alone is insufficient — disabled elements also drop opacity and/or gain a diagonal hatch pattern.
- **Standing rule**: every semantic color assignment in 4.2 must ship with a non-color backup — icon shape, pattern, or motion — before it's considered complete.

---

## 5. Character Art Direction

[To be designed]

---

## 6. Environment & Level Art

[To be designed]

---

## 7. UI Visual Language

[To be designed]

---

## 8. Asset Standards

[To be designed]

---

## 9. Style Prohibitions & References

[To be designed]
