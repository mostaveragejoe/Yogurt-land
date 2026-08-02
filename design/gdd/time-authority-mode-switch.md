# Time Authority / Mode-Switch

> **Status**: In Design
> **Author**: user + game studio agents
> **Last Updated**: 2026-08-02
> **Implements Pillar**: Pillar 2 (Preparation Gets Tested) · Pillar 4 (The Colony Lives Without You — combat as the bounded exception)
> **Architecture**: ADR-0001 (Accepted 2026-07-26, spike-validated 61/61) — this GDD specifies behavior and design rules; the ADR owns the implementation.

## Overview

The Time Authority / Mode-Switch system is the scheduling backbone of Hollowdeep: it decides, at every moment, which of two temporal models drives the one shared world — continuous real time (the colony simulation, with pause/1×/2×/3× speed control) or discrete turn-based time (tactics combat). Every simulation system registers with a single manager and is ticked by exactly one active authority; the inactive authority's systems receive nothing, which is why the colony fully pauses during a battle. Switching modes converts no state — the terrain and entities the tactics battle reads are the same live data the colony sim was using a moment earlier — so "your base is the tactics map" is literally true at the data level. The player never sees this system directly; they feel it as instant, trustworthy transitions (breach → battle, battle → colony), responsive speed control, and a world that never desyncs between modes. The architecture, contract, and measured costs are owned by ADR-0001 (Accepted 2026-07-26, spike-validated 61/61); this GDD specifies the design layer at the boundary: what is allowed to happen in each mode, the transition beats, autosave moments, speed-control behavior, and the battle-length budget (CD-9).

## Player Fantasy

This system is infrastructure the player never names — but it owns the two things they touch most often and feel hardest: the speed dial and the breach freeze.

**Who holds the clock.** In colony play the clock belongs to the player, and how they hold it is a readout of their confidence. Pause is the drafting table — nothing moves while they think. 1× is supervision. 3× is a statement: *my plan is good enough that I want to watch it run.* Pushing the colony to 3× is the ant-farm reward made into a button; dropping back to pause is the admission that something needs attention. This is Pillar 4 felt from the chair: the colony lives without you, and the dial is what that trust feels like.

**The test begins.** Then the mountain is breached, and the game takes the clock away. Everything holds still — haulers mid-stride, a mason one swing from finishing the wall — and the game stops asking the player to *plan* and starts asking them to *answer*. Time becomes a queue of turns, and in exchange the player receives the one thing colony play never grants: direct command of individual people. The trade is exact, and it is the whole shape of Pillar 4's bounded exception. The freeze is a **graded moment, never dread**: a prepared player reads it as *show me* — a plan about to be validated; an unprepared player reads exposure — the sick recognition that the west approach is still dirt. That split is Pillar 2 compressed into a single frame, and the presentation must make it legible within about two seconds (a decompression into silence, never a stinger — see Visual/Audio Requirements).

**A battle is a single sitting.** CD-9's no-mid-battle-save rule and the 8–15 minute battle budget (20-minute hard ceiling) are stated here as design, not constraint: the judgment cannot be save-scummed and cannot be walked away from. Like XCOM's Ironman, the limitation is the integrity of the test.

**The clock comes back.** Some minutes later the player gets the dial back — at pause, standing over a hole in a wall they were admiring (the world resumes exactly; the speed does not — the post-battle survey at pause is where scars teach). The wall that broke in the battle is broken in the colony; the colonist who was hauling stone still holds it. There was only ever one world.

**Falsifiable targets** (measured in playtests): a playtester describes the breach as "my base got tested," never "the game switched screens"; prepared and unprepared players report different emotions at the identical freeze frame; observed 3× usage rises as a colony matures; no playtester asks whether battle damage "carries over."

## Detailed Design

### Core Rules

1. **Two temporal models, one world.** Exactly one time authority is active at any moment: continuous real time (colony) or discrete turns (combat). The inactive side receives nothing — the colony fully pauses during a battle, and combat systems are dormant during colony play. There is no partial simulation, ever.
2. **Only the game switches modes.** Raid Trigger (#18) is the sole requester of the switch into combat; the Combat set (#19–23) is the sole requester of the switch back. The player has no manual "enter combat" verb in MVP. Any future feature wanting to trigger a switch (training, duels, scripted battles) is a new design decision revising this rule — never silent reuse.
3. **Speed control is a colony-only concept.** Pause/1×/2×/3×, where speed multiplies the simulation's fixed-step count and never changes step size. The speed UI shows the **requested** multiplier only; on a heavy colony the sim may silently deliver less than 3× — it slows gracefully, never hitches, and never catches up in a burst. During combat the dial is **hidden**, replaced by combat UI — turn pacing is animation-gated, not speed-driven. (A future "fast-forward animations" toggle would be a separate presentation knob, not this dial.)
4. **A raid cannot fire while paused — by construction.** Threat accumulates only inside real-time simulation steps; at speed 0 there are none. No special case exists or is needed.
5. **CD-10 at the boundary, as UI availability**: during combat the player may *view* everything but *author* nothing — blueprint, dig, build, zone, crafting, and hauling orders are unavailable and inert (not merely hidden; presentation owned by #26). Combat may change world state only through the existing writer surfaces under the combat-mode writer set (ADR-0002/0003) — no mutation category may exist that is exclusive to combat.
6. **One encounter at a time.** A second trigger while a battle is pending or active is rejected, never queued. Simultaneous multi-point breaches are one encounter with multiple breach cells, never two battles. What Raid Trigger does with a rejected trigger (reschedule, re-accumulate) is #18's decision — flagged as a required cross-reference.
7. **Battle end is Combat's call; the exit is ours.** Combat decides when the battle is over and who won. Every exit — victory, defeat, raider withdrawal, and any future player retreat (existence deferred to the Combat set) — funnels through the **identical** switch-back and reconcile path. No lighter exit exists.
8. **The canonical return sequence**, in order: battle resolves → authority swaps → `PostEncounterReconcile` runs (outcome report dispatched, dead colonists' reservations released, orphaned jobs/paths cancelled, dead/broken/withdrawn entities reaped) → battle-end autosave → **speed forced to 0** regardless of pre-battle speed → after-action survey presented (Combat UI, CD-1) → player regains the dial. World state resumes exactly; speed does not.
9. **Exactly two silent autosaves.** At switch-in: captures the **pre-swap colony state** (sequenced before the swap — a save taken in combat mode is corrupt by definition, CD-9). At battle end: **after** reconcile completes, so the save never contains orphaned state. No mid-battle saves; no player save-slot interaction at either moment.
10. **Notifications queue across the boundary.** Anything generated during combat queues silently and presents only after return to colony play. No pop-ups interrupt a battle.
11. **Menu pause is not a game mode.** A settings/menu overlay is presentation-only, on an independent axis from both the speed dial and the turn state. Closing it returns to exactly the prior speed or mid-turn state — nothing skipped, nothing double-applied. It never becomes a third temporal model and never touches the simulation path.
12. **Orders in flight at the freeze are dropped silently.** A colony order submitted in the same instant the switch lands is discarded with no world effect and no error — the window is sub-frame and imperceptible.
13. **Two kinds of "frozen," named for triage**: at speed 0 the colony systems are *active but stepping zero times*; during combat they are *inactive and receive nothing*. Identical on screen, structurally different — a bug in one is not a bug in the other.
14. **The default registration assumption**: colony systems freeze during battle unless their own spec says otherwise. Every simulation spec carries a "Behavior under each time authority" section written against ADR-0001's worked-example table; passive stores declare *inert; only the legal writer set changes*.

### States and Transitions

| From | To | Trigger | Player sees |
|---|---|---|---|
| Paused (0) | 1× / 2× / 3× | Player speed input | Colony resumes at chosen speed |
| 1× / 2× / 3× | Any other speed / Paused | Player speed input | Speed changes instantly |
| Any colony speed (0–3×) | Combat | Raid Trigger threshold → switch accepted | The freeze: colony halts mid-motion, camera reframes to the breach, combat UI replaces the dial |
| Combat | Paused (0) | Combat reports resolution → switch accepted → reconcile completes | Colony view returns **at pause**; after-action survey; hole in the wall is real |
| Any colony speed | Combat *(attempted)* | Second trigger while one is pending | **Rejected** — nothing visible; #18 owns the rejected threat's fate |
| Combat | Combat *(attempted)* | Duplicate switch request mid-encounter | **Rejected** — single-encounter invariant, silent |

Combat's interior turn structure (awaiting input → resolving → animating → next actor) is the Combat set's state machine, deliberately not enumerated here — this table treats Combat as one state.

### Interactions with Other Systems

| System | They own | This system owns |
|---|---|---|
| Raid Trigger (#18) | Threat accumulation, breach choice, populating the switch envelope, requesting the switch; behavior of a rejected trigger | Accept/reject, the atomic swap, guaranteeing threat freezes the instant combat begins |
| Combat set (#19–23) | Turn-by-turn resolution, win/loss/withdrawal, deciding battle end, outcome report contents | Turn dispatch mechanics, the swap back, the reconcile pass |
| Squad Prep (#24) | Deciding who is drafted and pre-switch placements (against the decision set, not live occupancy — spike Correction 2) | Exposing the pending-switch signal; executing placements before the swap. The window is sub-frame and invisible — no "deploying" UI beat |
| Needs (#13) | Decay rates, thresholds | Full freeze in battle — a starving colonist does not starve mid-fight. **Flag to the Needs GDD**: the routed post-battle-time question (zero-elapsed vs. catch-up) lands hardest here |
| Job Assignment (#10) | Queue integrity, actual cancellation logic | Guaranteeing reconcile runs exactly once, first, on return |
| Notifications | Content, priority, display | The queue-across-modes rule (Rule 10) |
| Save/Load | Save UX, format | The two autosave moments, their ordering (Rule 9), and the mode invariant (a combat-mode save is corrupt) |
| Blueprint UI (#26) / Combat UI (#27) | Presentation of order inertness (CD-10) and the after-action survey (CD-1) | The rules saying when orders are inert and when the survey appears (Rules 5, 8) |
| Every sim system | Its own "Behavior under each time authority" section | The contract that section is written against (Rule 14) |

## Formulas

Two formulas govern this system. Both restate ADR-0001's sub-stepping contract in design-consumable form; the shared values (`SubStepCap`, `SubStepDuration`, `EngineFrameRate`) are defined once in Tuning Knobs. Deliberately **not** formulas: the fixed dt (a pinned tuning knob with nothing to compute), the CD-9 battle-length budget (a threshold — see Acceptance Criteria), and the `TurnIndex`/`TickSequence` counters (fixed increment rules, ADR-0001's concern).

### Formula D.1 — Delivered Sub-Steps Per Physics Frame

`SubStepsDelivered = min(SubStepsRequested, SubStepCap)`

| Variable | Symbol | Type | Range | Description |
|---|---|---|---|---|
| Requested sub-steps | SubStepsRequested | int | 0–unbounded (practically small) | Sub-steps the active speed multiplier plus any real-frame catch-up backlog ask for this dispatch |
| Sub-step cap | SubStepCap | int | ≥1 (default 8) | Per-frame ceiling; the lower of the engine clamp and our own configured cap |
| Delivered sub-steps | SubStepsDelivered | int | 0–SubStepCap | Sub-steps that actually run — determines how much game time really advances |

**Output Range:** 0 (paused) to SubStepCap; never negative, never exceeds the cap regardless of request size — this is what makes "the sim slows down, never death-spirals" a guaranteed property rather than a hope.
**Example (normal frame):** speed 3×, on-schedule frame: `min(3, 8) = 3` — no throttling; MVP's max speed has headroom.
**Example (stalled frame):** the engine catches up 4 frames' worth at 3×: `min(12, 8) = 8` → the player experiences ~67% of the requested pace for that moment — a visible slowdown, not a skip or a hang. This is the mechanism behind Core Rule 3's "silently delivers less than 3×."

### Formula D.2 — Colony Time Elapsed Per Real-Time Second

`GameSecondsPerRealSecond = SubStepsDelivered × SubStepDuration × EngineFrameRate`

| Variable | Symbol | Type | Range | Description |
|---|---|---|---|---|
| Delivered sub-steps | SubStepsDelivered | int | 0–SubStepCap | Output of Formula D.1 |
| Sub-step duration | SubStepDuration | float (s) | fixed (default 1/60 s) | The fixed dt every sub-step advances by; never scaled |
| Engine physics frame rate | EngineFrameRate | float (Hz) | fixed (default 60 Hz) | Physics dispatch rate under unstalled conditions |
| Result | GameSecondsPerRealSecond | float | 0–8 at defaults | Simulated seconds per real second at the current speed |

**Output Range:** 0 at pause; ceiling 8 game-seconds/real-second at defaults — MVP speeds (max 3×) never hit it under normal frame timing.
**Example:** at 3×: `3 × (1/60) × 60 = 3` game-seconds per real second — a 24-game-hour day takes **8 real minutes at 3×**, 24 at 1×. This is the canonical throughput number Needs decay, day/night, and crafting durations must cite rather than re-derive (registered in the entity registry on approval of this GDD).

## Edge Cases

[To be designed]

## Dependencies

[To be designed]

## Tuning Knobs

[To be designed]

## Visual/Audio Requirements

[To be designed]

## UI Requirements

[To be designed]

## Acceptance Criteria

[To be designed]

## Open Questions

[To be designed]
