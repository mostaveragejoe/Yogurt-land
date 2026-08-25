# Interaction Pattern Library

> **Status**: In Design
> **Author**: user + ux-designer
> **Last Updated**: 2026-08-24
> **Template**: Interaction Pattern Library

---

## Overview

This library is the single source of truth for **how interactions behave** in Hollowdeep. A UX spec names a pattern; it does not redefine one. When two screens need the same behaviour they reference the same entry here, and when they genuinely need to differ, the difference is recorded as a new pattern rather than a quiet variation.

**Authored before any screen spec exists.** Normally a pattern library is extracted from screens already designed. Here it runs ahead of them, because ten patterns were already pinned by decisions in the Terrain and Time Authority GDDs, the CD notes, and `design/accessibility-requirements.md`. Writing them down now means the first UX spec inherits settled behaviour instead of inventing it.

Every pattern carries its accessibility clause inline. The committed tier is **WCAG-AA adapted for games**; a pattern that cannot meet it is not in this library.

**Input context**: Keyboard/Mouse primary. Gamepad **partial** — action names route cleanly from day one, controller UX is deferred until the game is proven fun. No touch. Patterns below specify mouse and keyboard; each notes what a gamepad binding would need when that work happens.

---

## Pattern Catalog

| # | Pattern | Category | Status |
|---|---|---|---|
| P1 | Drag-Select Designation | Input | Specified |
| P2 | Click-Click Rectangle | Input | Specified |
| P3 | Single-Cell Designation | Input | Specified |
| P4 | Disabled Control Treatment | Feedback | Specified — **one treatment, project-wide** |
| P5 | On-Attempt Transient Affordance | Feedback | Specified |
| P6 | Blocking Confirmation Dialog | Modal | Specified |
| P7 | Speed Dial | Data Display + Input | Specified |
| P8 | Notification Queue-and-Flush | Feedback | Specified |
| P9 | After-Action Survey | Modal | Specified |
| P10 | Persistent Cell Indicator | Data Display | Specified |
| P11 | Cell Inspect View | Data Display | Specified |

---

## Patterns

### P1 — Drag-Select Designation

**Category**: Input
**Used In**: Blueprint UI (#26). Expected in Squad Prep (#24) for muster-point areas.

**Description**
The primary designation gesture. Press on an origin cell, drag to an opposite corner, release to commit a rectangle of cells on the current focus layer. The canonical way a player expresses intent over an area.

**Specification**
- Press, move, release. The rectangle previews continuously and commits on release
- Constrained to the **focus layer only** — a drag never spans Z
- No minimum drag distance: a one-cell drag is valid
- No minimum drag speed: a slow drag is a drag
- Releasing outside the play area commits the rectangle clamped to world bounds
- **Escape during the drag cancels it**, committing nothing
- **An interrupted drag is discarded, never partially committed.** The mode-switch freeze can land mid-gesture with no grace window (Time Authority); the player loses the gesture but never receives a designation they did not intend

**Accessibility (AA)**
- P2 is a mandatory first-class equivalent, not a fallback
- The preview rectangle meets the 3:1 boundary contrast floor against player-lit terrain, using its own backing treatment rather than fill color
- Validity is never signalled by color alone (P4's rule applies to the preview)
- Gamepad, when built: needs a cursor model plus a hold-to-anchor binding

**When to use**: any designation over a contiguous area.
**When NOT to use**: single-cell precision work — P3 is faster and more reliable. Non-rectangular selections; no pattern supports those and none is planned.

---

### P2 — Click-Click Rectangle

**Category**: Input
**Used In**: Blueprint UI (#26), everywhere P1 is offered.

**Description**
Click an origin cell, then click an opposite corner. Same result as P1 with no held button. Specified as a **first-class path, not an accessibility fallback** — it is also the more reliable route for precise corners, which is what keeps it exercised rather than left to rot.

**Specification**
- First click anchors and shows a persistent anchor marker
- The rectangle previews from anchor to cursor, identically to P1's preview
- Second click commits
- **Escape, or clicking the anchor again, cancels** with nothing committed
- No timeout between clicks. The anchor persists indefinitely, including across a mode switch — where the anchor survives but is inert until colony mode resumes
- Mixing is allowed: anchor by click, then drag from the anchor, and release commits

**Accessibility (AA)**
- Satisfies the no-simultaneous-input and no-timing-pressure rules directly
- The anchor marker is shape-based, not color-only
- Gamepad, when built: maps to two confirm presses, no hold required

**When to use**: precise corners, large areas where a drag is unwieldy, or any player who prefers discrete input.
**When NOT to use**: nothing — it is always available alongside P1.

---

### P3 — Single-Cell Designation

**Category**: Input
**Used In**: Blueprint UI (#26).

**Description**
One click on one cell applies the active designation to it.

**Specification**
- Click applies immediately; there is no preview step
- Click on an already-designated cell with the same designation **removes it** (toggle)
- Click with a different active designation **replaces** it
- No confirmation. Designation is a plan, not an execution — cancellation is free and instant (Terrain C4)

**Accessibility (AA)**
- Minimum target is the rendered cell; discrete zoom (Terrain Rendering C6) is what makes cell size a reliable, player-controlled quantity
- Toggle-not-error behaviour supports exploratory clicking without punishment

**When to use**: single cells, and correcting one cell inside a previously designated area.
**When NOT to use**: areas — P1 or P2.

---

### P4 — Disabled Control Treatment

**Category**: Feedback
**Used In**: All six order categories, the save control, the speed dial in combat — Blueprint UI (#26), Combat UI (#27), shared shell.

**Description**
The single visual language for "this control exists and is currently unavailable." Time Authority mandates that there is **exactly one** such treatment project-wide; this library owns what it is.

**Specification**
- **Inert, not hidden.** A disabled control keeps its position, label and icon. Removing it is forbidden — the player must be able to see that the capability exists and is currently unavailable
- Treatment is **dimming plus a hatched overlay** — two channels, so neither carries the meaning alone
- Hover or focus on a disabled control shows a **reason string**, never a generic "unavailable"
- No queuing. Activating a disabled control does nothing and enqueues nothing (Time Authority Rule 5, AC-40)
- Disabled controls remain **keyboard-focusable**, so a keyboard user can discover the reason string the same way a mouse user can

**Accessibility (AA)**
- Legible without color — the hatch is the color-independent channel and is the load-bearing half
- The reason string is the cognitive mitigation: "disabled" without "why" is a dead end
- Dimming must not drop the control below the 3:1 boundary contrast floor. **Dim toward the ambient floor, not toward transparent**

**When to use**: any control unavailable due to game state.
**When NOT to use**: controls unavailable due to *player progression* — those are a different problem and want a locked/unlockable treatment, which does not exist yet (see Gaps).

---

### P5 — On-Attempt Transient Affordance

**Category**: Feedback
**Used In**: Blueprint UI (#26), for gesture-type controls during combat.

**Description**
P4's counterpart for controls with no at-rest surface. A drag gesture has nothing to dim — and "no rectangle appears" is exactly the merely-unresponsive outcome Time Authority bans. So the affordance appears **on attempt** rather than persisting.

**Specification**
- Triggered when the player begins a gesture that is currently unavailable
- Presents a struck ghost-rectangle at the attempted location, or a cursor badge, and the same reason string as P4
- **Transient**: dismisses on release or after a short interval. Never requires dismissal
- Shares P4's visual language — the hatch — so disabled reads the same whether the control is a button or a gesture

**Accessibility (AA)**
- No timing pressure: the affordance is informational and dismisses itself; nothing is missed by not reading it in time
- Must not flash or strobe — reduced-motion safe by construction

**When to use**: gesture controls that are unavailable.
**When NOT to use**: anything with a persistent at-rest surface — that is P4.

---

### P6 — Blocking Confirmation Dialog

**Category**: Modal
**Used In**: Shared shell (quit mid-battle). Expected wherever an irreversible action needs a checkpoint.

**Description**
A modal that halts interaction until the player answers. Reserved for irreversible or state-destroying actions; overuse turns it into a reflex click-through.

**Specification**
- Renders a title, a **behaviour-stating body** — what will happen, not "are you sure?" — and two clearly-labelled options
- Options are labelled with **verbs describing the outcome** ("Quit and suspend the battle" / "Keep playing"), never "OK" / "Cancel"
- **The exit is rendered before the modal is interactive.** A modal whose dismiss control appears conditionally is a defect
- Escape maps to the non-destructive option
- **Default focus is decided by the spec that owns the dialog.** The quit-mid-battle dialog is already fixed by Time Authority: the affirmative must NOT hold default focus, so a stressed player cannot key-repeat through it. Whether that generalises is each owning spec's call
- Canonical strings live as constants. `QuitConfirmText` is rendered verbatim and tested by equality, not resemblance (Time Authority AC-43)

**Accessibility (AA)**
- No timeout, ever
- Visible focus indicator on whichever option holds focus
- The full dialog is keyboard-operable

**When to use**: irreversible actions, and actions whose consequence the player cannot see from where they stand.
**When NOT to use**: anything undoable. Designation is undoable, so it never confirms.

---

### P7 — Speed Dial

**Category**: Data Display + Input
**Used In**: Colony HUD; referenced by all three UX specs.

**Description**
The player's control over colony time, and the readout of what they asked for.

**Specification**
- Displays the **requested** multiplier, not the achieved one (Time Authority Rule 3). If the sim cannot keep up, that is surfaced by a separate throttle signal, never by silently changing the number the player set
- Discrete steps: 0, 1x, 2x, 3x. **0 is a first-class state**, not a special pause mode
- **Hidden during combat**, replaced by combat UI (#27) — this is the one sanctioned exception to P4's inert-not-hidden rule, because the control is not merely unavailable, its whole domain is suspended
- Reads **0 on every return from battle** (Time Authority Rule 8)

**Accessibility (AA)**
- Current speed is conveyed by position and numeral, not by color
- Keyboard-bindable per step, and rebindable like everything else
- No timing pressure: changing speed is never urgent, because 0 is always available

**When to use**: colony mode only.
**When NOT to use**: combat. There is no turn-speed control and no turn timer.

---

### P8 — Notification Queue-and-Flush

**Category**: Feedback
**Used In**: Shared Notifications component, consumed by every system.

**Description**
How the game tells the player something happened, including things that happened while they were somewhere else.

**Specification**
- In colony mode, notifications present as they occur
- **During combat they queue**, and flush on return (Time Authority Rule 10)
- **Digest is the default presentation for a flush** — a burst of thirty individual messages is a cognitive dump
- **The component may depart from digest** where a message type genuinely needs immediacy or individual acknowledgement. A named colonist death is not a line item in a summary. This exception is the component's to exercise; this library records that it exists rather than forbidding it
- Notifications naming a location are **click-to-focus**, moving the camera to the cell
- A notification never blocks. Anything that must block is P6 or P9

**Accessibility (AA)**
- Every notification has a visual form; audio is never the sole carrier (accessibility doc, Auditory)
- No notification auto-dismisses faster than it can be read, and a history is reachable so a missed one is recoverable
- Severity is conveyed by icon and placement, not color alone

**When to use**: anything the player should know but need not act on immediately.
**When NOT to use**: anything requiring a decision now — that is P6.

---

### P9 — After-Action Survey

**Category**: Modal
**Used In**: Combat UI (#27).

**Description**
The post-battle beat that delivers CD-1's teaching promise — what broke, where, which material tier failed, what breached first — at the forced pause before the player regains the dial.

**Specification**
- Presented at the forced pause after battle end (Time Authority Rule 8), before the speed dial returns
- **No timeout and no escape.** It persists until answered
- **Clearable by one unambiguous confirm action.** Any further detail sits behind that confirm, never in front of it. Time Authority names this the mitigation floor, and it is what makes the no-escape wall acceptable
- Focus loss (alt-tab, minimize) is safe by construction — the sim is already at forced speed 0, so the survey simply persists
- On total loss, the camera falls back to the breach site (EC-5)

**Accessibility (AA)**
- **This is the project's single highest lockout risk**, named as such by Time Authority. The single-confirm clearability bound is not a nicety, it is the thing that makes a no-escape modal defensible
- The confirm control is visible from the moment the survey renders, never revealed by scrolling
- No reading deadline; the sim is paused

**When to use**: exactly once, after a battle.
**When NOT to use**: anywhere else. A no-escape modal is a last resort and this is the only sanctioned instance.

---

### P10 — Persistent Cell Indicator

**Category**: Data Display
**Used In**: Blueprint UI (#26), for dormant stair linkage. Extensible to other always-true cell facts.

**Description**
A marker on a cell whose state the player must be able to find without inspecting cell by cell.

**Specification**
- Renders on any cell carrying the tracked fact — for stairs, any floor with `IsStairFloor`, whether or not a wall is present
- **Independent of cutaway depth.** Terrain Rendering C5 reads below the cutaway floor as darkness, so a stair sealed below the visible window is invisible in 3D. This indicator plus P11 carry that promise; the cutaway supplements them and never substitutes
- Renders in the designation layer, above terrain, below modals

**Accessibility (AA)**
- Shape-based, not color-based
- Meets the 3:1 boundary contrast floor with its own backing treatment, since it sits over player-lit terrain

**When to use**: a persistent cell fact the player needs to locate spatially.
**When NOT to use**: transient state, or anything better answered by inspecting one cell — that is P11.

---

### P11 — Cell Inspect View

**Category**: Data Display
**Used In**: Blueprint UI (#26).

**Description**
The authoritative readout for one cell: what it is, what it is made of, what is planned for it, and what is true of it that the 3D view cannot show.

**Specification**
- Invoked on a single cell; presents floor, wall, material, damage state, active designation, and dormant Z-linkage where present
- **Names dormant stair linkage explicitly**, in words. This is half of the guarantee that survives cutaway depth (with P10)
- Read-only. Designation happens through P1/P2/P3, never from the inspect panel — one action, one path
- Non-blocking; the colony continues at whatever speed the dial holds

**Accessibility (AA)**
- Text-based, so it is the most accessible surface in the game and deliberately carries the facts hardest to read visually
- Damage state named in words, not only shown as a mesh
- Keyboard-invocable on the focused cell

**When to use**: answering "what is actually here?"
**When NOT to use**: as a designation surface, or as a substitute for making the 3D view legible.

---

## Gaps & Patterns Needed

| Gap | Needed by | Note |
|---|---|---|
| **Locked / unlockable control treatment** | Onboarding (#32), VS | P4 covers state-unavailable, not progression-locked. Conflating them would teach the player that dimmed means "later" when it means "not now" |
| **List selection and multi-select** | Roster UI (#28) | Colonist lists need a selection model. Nothing here covers non-spatial selection |
| **Drag-and-drop assignment** | Squad Prep (#24) | If drafting is drag-based it needs a pattern, and P1's click-click equivalence rule would apply to it |
| **Tooltip / hover-detail** | Everywhere | Referenced implicitly by P4's reason string. Needs its own entry once a second consumer exists |
| **Tab / panel switching** | Roster UI (#28), Combat UI (#27) | No pattern for moving between panel sections |
| **Numeric entry or slider** | Settings, and any tuning surface | Volume sliders are already required by the accessibility doc |

Gaps are listed rather than pre-solved. A pattern invented without a real consumer is a guess.

---

## Open Questions

| # | Question | Owner | Trigger |
|---|---|---|---|
| 1 | **P4's hatch pattern is specified as a concept, not as art.** The actual hatch needs to survive the pixel-art texel grid at every zoom step | art-director | With the art bible 3.1/3.3 re-validation |
| 2 | **P7's throttle signal has no pattern.** Time Authority requires the HUD to surface that the sim is not keeping up, but the presentation is unspecified and belongs with the HUD design | ux-designer, at `/ux-design hud` | Before the colony HUD spec |
| 3 | **P2's anchor persisting across a mode switch** is specified here as inert-but-surviving. That is a guess at what feels right; it may be better to discard it like P1's drag | Blueprint UI (#26) | At #26's UX spec, or first playtest |
| 4 | **No player journey map** exists, so patterns are specified against inferred rather than mapped player context | ux-designer | Before the first screen UX spec |
