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

[To be designed]

---

## Motor Accessibility

[To be designed]

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
