# Accessibility Requirements: Hollowdeep

> **Status**: In Design
> **Author**: user + ux-designer
> **Last Updated**: 2026-08-24
> **Committed Tier**: WCAG-AA, adapted for games

---

## Accessibility Tier Definition

**Committed tier: WCAG-AA, adapted for games.** Committed 2026-08-24 during `/ux-design`.

Web criteria with no game analogue (page titles, link purpose, HTML landmarks) are out of scope. Everything about perceivability, operability and understandability is in.

**Why AA is the right floor here, and cheap.** Hollowdeep is menu-and-mouse driven with no reflex demands — the concept doc rules out timed inputs as an anti-pillar. Most of AA is therefore nearly free *if committed now* and expensive to retrofit. Three things make it unusually cheap:

- Combat is turn-based, so there are no reaction-time floors to accommodate
- Colony play is pausable at any moment (Time Authority Rule 3: speed 0 is a first-class state)
- Input already routes through Godot's `InputMap` with clean action names, so remapping is a settings screen rather than a refactor

**What this project commits to:**

| Area | Commitment |
|---|---|
| Contrast | 4.5:1 for body text, 3:1 for large text and meaningful UI boundaries |
| Color independence | No information conveyed by color alone, anywhere. Already a world-rule in the art bible — the world carries zero semantic color — this extends it to UI chrome |
| Keyboard | Every interactive element reachable and operable by keyboard, with a visible focus indicator |
| Remapping | All actions rebindable |
| Text | Scalable without clipping or overlap |
| Motion | A reduced-motion option covering camera and transition animation |
| Timing | No action requires a response within a fixed window |

**Three constraints inherited, not chosen.** The Time Authority GDD routed these here as binding inputs. This document owns the mitigations and **may not relax the rules themselves**:

1. **The mode-switch freeze can land mid-input with zero grace window.** A player mid-drag when a raid triggers loses that input with no warning.
2. **Disabled controls must be inert, not hidden** — and the disabled treatment must be legible without color.
3. **The after-action survey has no timeout and no escape.** Time Authority names this its single highest lockout risk. Its mitigation floor is fixed there: the survey must be clearable by one unambiguous confirm action.

**Beyond AA, chosen anyway:** a colorblind-safe palette as the default rather than a mode. The art bible already forbids semantic color, so there is no palette to swap — getting the base palette right costs nothing extra.

**Explicitly deferred:** screen-reader support. Godot 4.5's AccessKit integration exists, but a 3D cutaway colony sim has no meaningful non-visual representation, and committing now would be a promise with no plan. Recorded under Known Intentional Limitations rather than omitted.

---

## Visual Accessibility

### Contrast

| Element class | Minimum ratio | Notes |
|---|---|---|
| Body text, labels, tooltips | 4.5:1 | Against the panel behind it, not the world |
| Large text (>=24px), headings | 3:1 | |
| Meaningful UI boundaries — panel edges, focus rings, selection outlines | 3:1 | Decorative rules exempt |
| Designation overlays against terrain | 3:1 | **The hard one** — see below |

**The designation-overlay case needs stating plainly.** Blueprint overlays sit on top of a warm, low-light 3D world whose brightness the player controls by where they put torches. A fixed overlay color cannot guarantee contrast against a surface that might be lit or unlit. **Mitigation: designation overlays carry their own backing treatment — outline, scrim, or stipple — rather than relying on fill color against terrain.** The specific treatment is owned by Blueprint UI (#26); this document sets the floor and the obligation.

The art bible's **minimum ambient fill** is load-bearing here: because no visible surface crushes to black, overlays always have a floor to contrast against. That constant is an accessibility dependency, not only an art choice, and must not be lowered without re-checking this section.

### Color independence

The art bible already establishes that the world carries zero semantic color. This document extends that to UI chrome, where three assignments do carry meaning:

| Meaning | Must also be conveyed by |
|---|---|
| Valid / invalid designation | Shape or icon, not green/red alone |
| Disabled control | The single shared disabled treatment (Time Authority: exactly one treatment, legible without color) — hatching or dimming plus an icon |
| Damage state (intact / damaged / critical) | Distinct mesh silhouette per state — already required by Terrain Rendering C7's three-state legibility floor |

Damage states show the rule paying for itself: they were already specified as distinct **meshes** rather than tints, so they satisfy color independence at no extra cost.

### Text scaling

- UI text scalable to **150%** without clipping, overlap, or loss of function
- No text baked into textures, except decorative signage that conveys nothing
- Panels reflow or scroll; they never truncate an actionable label

**Flagged tension.** The art bible ties the UI base pixel unit to texel density, which the camera decision (quantized zoom, Terrain Rendering C6) has now fixed. A freely scalable UI and a fixed-pixel-unit UI pull against each other. **Proposed resolution: UI scales in integer steps (100%, 125%, 150%) so the pixel grid survives.** Routed to art-director alongside the Sections 3.1/3.3 re-validation — this document proposes, art-director confirms.

### Readability

- Minimum body size **16px at 100% scale** at the default zoom level
- No italic body text; italics for emphasis only, never for a whole message
- The cutaway's depth attenuation (Terrain Rendering C4) applies to **world geometry only** — UI never dims with depth

---

## Motor Accessibility

### Remapping

- **Every action rebindable**, including mouse buttons and modifier combinations. No reserved bindings except OS-level ones
- Conflict detection on bind: the UI names what the key currently does and requires confirmation
- A restore-defaults control, always reachable
- Bindings persist per profile and survive a patch that adds new actions — **a new action arrives unbound rather than stealing an existing binding**

Cheap because input already routes through Godot's `InputMap` with clean action names (technical-preferences). Skipping remapping would mean *removing* a capability the architecture already has.

### No timing pressure

- **No action anywhere requires a response within a fixed window.** A whole-project rule, not a UI one — the concept doc lists timed inputs as an anti-pillar
- No double-click required to reach any function; a double-click may exist as an accelerator only, never as the sole path
- No press-and-hold required to confirm; hold may accelerate repeat, never gate an action
- Turn-based combat has **no turn timer**, in any mode or difficulty

### Drag-select, and the one real problem

Drag-select is the primary designation gesture and the least accessible thing in the game. Two obligations on Blueprint UI (#26):

1. **Every drag-select operation has a non-drag equivalent** — click the first cell, click the last cell, same rectangle designated. This is a **first-class path, not a lesser fallback**: it is also the faster route for precise single-cell work, which is what keeps it exercised rather than rotting unused.
2. **No minimum drag speed or distance.** A slow drag is a drag. A one-cell drag is valid.

**The mid-drag freeze.** Time Authority's zero-agency interruption means a raid can trigger mid-drag and discard that input with no warning. For a player who takes ten seconds to complete a drag this is materially worse than for one who takes one second. **Mitigation: an interrupted drag is discarded, never partially committed.** The player loses the gesture but never receives a designation they did not intend — silent partial commitment is the harmful outcome, not the loss.

### Click targets and precision

- Minimum interactive target **24x24px at 100% scale** for UI chrome. World-space cells are exempt — a cell is as big as zoom makes it, which is part of why zoom is a discrete, reliable control (Terrain Rendering C6)
- No interaction requires pixel-accurate positioning; adjacent targets carry at least 2px separation or a shared boundary that cannot be ambiguously hit
- **No path through the game requires simultaneous multi-key input.** Modifier-plus-click may exist as an accelerator with a non-modifier equivalent

### Gamepad

Partial support per technical-preferences: action names route cleanly from day one, controller UX is not built until the game is proven fun. **This document does not commit to gamepad navigation.** When it is built it inherits every rule in this section. Recorded so the deferral stays visible rather than becoming an unnoticed gap.

---

## Cognitive Accessibility

[To be designed]

---

## Auditory Accessibility

[To be designed]

---

## Platform Accessibility API Integration

[To be designed]

---

## Per-Feature Accessibility Matrix

[To be designed]

---

## Accessibility Test Plan

[To be designed]

---

## Known Intentional Limitations

[To be designed]

---

## Audit History

[To be designed]

---

## Open Questions

[To be designed]
