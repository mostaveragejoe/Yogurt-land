# Art Bible: Hollowdeep

*Created: 2026-07-24*
*Status: In Progress — Sections 1–4 being authored; 5–9 skeleton*

> Scope note: This is a constraint document. Every section narrows the visual
> solution space in exchange for coherence. Sections marked `[To be designed]`
> are placeholders awaiting a dedicated authoring session.

---

## 1. Visual Identity Statement

### The Rule

> **Light sets the mood. Shape tells your story.**

Warmth is the game's aesthetic soul — the Warm Hearth palette is the visual mood of Hollowdeep, and it stays exactly that: mood. It is not a claim signal, not a readability channel, and nothing in combat, threat state, or repair logic ever reads or reacts to it. Lighting exists to make the colony feel beautiful and lived-in, full stop. What actually carries meaning — who you are, what you value, how you think — is the architecture itself: the shapes, styles, and materials the player chooses to build, anywhere, at any depth, from day one.

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

### Bavarian + Roman Integration

Both references are **style vocabularies in the building palette, not stages of a progression** — a player can raise Roman-coded vaults at the surface or fachwerk cottages at the colony's deepest point on day one; nothing gates one by depth or material tier. Materials look like what they are and light does what light does: a Roman stone wall lit warmly looks warm, exactly like a lit Bavarian plaster wall — no hue constraint distinguishes them.

Practically, both vocabularies share one grid-based kit-of-parts (wall, doorway, ceiling, support modules) with swappable dressing — exposed beam framing and painted bauernmalerei-style folk trim for the Bavarian set; rounded arches, engaged pilasters, and vaulted ceiling silhouettes for the Roman set — keeping authoring cost close to linear rather than doubling the kit. This is a budget decision, not a lore one. The Roman set stays stylized and legible rather than literal (no full classical-order capital libraries — ornate sculptural detail reads as noise at pixel-art fidelity, in direct tension with Legibility Before Beauty). The Bavarian set is a coded construction-and-color influence rather than a literal costume simulation; character/culture specifics belong to Section 5.

---

## 2. Mood & Atmosphere

[To be designed]

---

## 3. Shape Language

[To be designed]

---

## 4. Color System

[To be designed]

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
