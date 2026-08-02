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

- **If Raid Trigger's threshold is reached while speed is 0**: impossible by construction — threat accumulates only inside real-time steps, and speed 0 delivers none. No "check while paused" path exists. *(EC-1)*
- **If a second breach condition is met while an encounter is active or pending**: the switch request is rejected silently — no visible transition, no queued second battle. Raid Trigger (#18) owns what becomes of the rejected threshold (reset / hold / re-arm). *(EC-2)*
- **If a UI action is mid-authoring at the freeze** (uncommitted drag-select, unconfirmed blueprint, open zone-menu selection): dropped silently, never resumed after battle — the referenced state may no longer exist by then. **Scope**: this covers *uncommitted player input* only; a committed job (a colonist mid-haul) simply freezes with its owner and resumes correctly. *(EC-3)*
- **If a menu overlay is open when the switch is accepted**: the switch proceeds regardless (axis independence, Rule 11); the menu is force-closed on entering combat, since CD-10-restricted colony UI has no combat equivalent. *(EC-4)*
- **If the battle ends with zero surviving defenders**: the return path is identical — reconcile reaps all the dead with no "all vs. some" branch, autosave fires, speed forces to 0, the survey displays. The survey camera, lacking a survivor to frame, defaults to the **breach site / colony overview**. Whether total loss triggers any further failure state is the Combat set's / game concept's domain — this system's contract is satisfied identically. *(EC-5)*
- **If the player issues a manual save in the same frame the switch is accepted** (pending, not yet applied): the save is allowed and completes normally — the mode is still real-time for the entire remainder of that dispatch; the swap is atomic between dispatches, so CD-9's invariant is never at risk. *(EC-6, regression-test candidate)*
- **If the player changes speed in the same frame the switch is accepted**: honored for the remainder of that dispatch (harmless — the view cuts before the next rendered frame), then irrelevant: return always forces speed 0. *(EC-7)*
- **If the player quits to desktop mid-battle**: on relaunch they load the switch-in autosave — the colony resumes at the moment combat began and the encounter restarts from its beginning. Forced by CD-9 (no mid-battle save exists to return to). A **confirmation dialog** warns before quitting: "Quitting now will lose this battle's progress." *(EC-8)*
- **If a sub-frame time backlog (<1 sub-step) exists at the freeze**: preserved, not discarded — the inactive authority's accumulator sits inert and resumes exactly where it left off. No observable effect; stated so nobody "fixes" it by resetting the accumulator on swap. *(EC-9)*
- **If Squad Prep drafts zero colonists when a raid fires** (all dead, incapacitated, or unreachable): **the switch proceeds anyway** — an empty-roster encounter is legal, and Combat resolves the unopposed raid inside turn-based mode (user decision 2026-08-02, chosen over a Raid-Trigger pre-check). **Obligation transmitted to the Combat set (#19–23)**: its turn loop must handle zero player-controlled units — raiders act, the player observes; the identical exit path still applies. *(EC-10)*
- **If the player leaves speed at 0 after a battle and never resumes**: no new raid can ever trigger (same mechanism as EC-1). Stated as a deliberate design property, not an accident: resume-at-pause doubles as a safety buffer — the player is never ambushed again until they choose to restart time. *(EC-11)*

**Evaluated and excluded** (tuning-knob constraints, not runtime edges): `SubStepCap` below the max speed multiplier (safe-range note in Tuning Knobs); physics tick rate changes (build-time concern; ADR-0001 requires revisiting the ADR itself).

## Dependencies

**Upstream: none.** This is a Foundation system. The primitive types it references (`CellCoord`, `EntityId`) live in the shared foundation-primitives namespace defined jointly with ADR-0002 — a namespace, not a system dependency.

**Downstream — every simulation-bearing system.** The systems index states it flatly: *under-declaring this dependency is forbidden; every system that advances state over time depends on it.* Named interfaces, all **hard** unless marked:

| Dependent | Interface (what crosses the boundary) |
|---|---|
| Raid Trigger (#18) | The switch-request call and its accept/reject result; the guarantee threat freezes on entry (Rules 2, 4, 6; EC-2) |
| Combat set (#19–23) | Turn dispatch, presentation-gated advancement, the switch-back call, the reconcile pass; the EC-10 zero-roster obligation; the CD-9 battle budget |
| Squad Prep (#24) | The pending-switch signal and the participants/breach-cells envelope (framing only — never entity state) |
| Needs (#13), Job Assignment (#10), Excavation/Construction (#15/#16), Stockpile & Hauling (#11) | Real-time registration, full freeze in battle, reconcile guarantees on return (Rules 1, 8, 14) |
| Pathfinding (#8) | Registered under **both** authorities (colony paths / combat reachability) — the notable dual-registration case |
| Save/Load (#6) | The two autosave moments and their ordering; the mode invariant — a combat-mode save is corrupt (Rule 9, EC-6, EC-8) |
| Notifications (shared component) | The queue-across-modes rule (Rule 10) |
| Blueprint UI (#26), Combat UI (#27), Roster UI (#28) | Dial visibility/hiding, order inertness (CD-10), survey timing, the quit-confirmation dialog (Rules 3, 5, 8; EC-8) |
| Terrain (#1) + entity stores (ADR-0003) | *Soft/passive*: never ticked — only their legal writer set changes per authority |

**Bidirectional-consistency obligation**: every dependent spec must (a) list Time Authority / Mode-Switch in its Depends On, and (b) carry the mandatory "Behavior under each time authority" section (Rule 14). The systems index currently declares this dependency explicitly only for #18, #19, and #24 — correct per its own cross-cutting-contract note (the dependency is universal by contract), but any future index revision that enumerates per-row dependencies must not read those three as the complete set.

## Tuning Knobs

| Knob | Default | Safe range | What it affects / what breaks at extremes |
|---|---|---|---|
| Speed multiplier set | {0, 1, 2, 3} | any integers with max ≤ `SubStepCap` | The colony speed options. Adding 4× touches zero simulation systems (spike-proven) but must stay ≤ `SubStepCap` or the new speed silently never delivers its multiplier (permanent degradation, not load-dependent); each step up multiplies per-frame sim cost linearly |
| `SubStepCap` | 8 | max speed multiplier … ~12 | Per-frame sub-step ceiling (Formula D.1). Too low: high speeds permanently degraded even on light colonies. Too high: a slow frame does more catch-up work, worsening the very frame pacing that caused it — the cap exists to make the sim slow down gracefully |
| `SubStepDuration` (fixed dt) | 1/60 s | **do not treat as a lever** | Advances per sub-step; every rate constant in every system is calibrated against it. Changing it changes simulation-speed semantics project-wide and **requires revisiting ADR-0001** — a build-time decision, never a balance pass |
| `EngineFrameRate` (physics tick) | 60 Hz | **do not treat as a lever** | Pinned in project settings; same warning as dt. The pair (dt, tick rate) defines Formula D.2's real-time↔game-time mapping |
| Battle length budget | 8–15 min target, 20 min hard ceiling | ceiling 15–25 min | CD-9's single-sitting integrity. Owned here as the *boundary budget*; the Combat set owns achieving it (its pacing levers live there). Raising the ceiling weakens the no-mid-battle-save justification; lowering the target below ~8 min makes battles too small to express Pillar 2 |

**Knob interactions**: Formula D.2's throughput is the product of three knobs — retune any one and every time-dependent system's *felt* pacing shifts together. Max speed and `SubStepCap` form a hard constraint pair (see safe ranges). **Anti-duplication rule**: downstream systems must not create their own real-time↔game-time conversion knobs — Needs decay, day/night, and crafting durations cite Formula D.2's `GameSecondsPerRealSecond` as the single source of truth.

## Visual/Audio Requirements

[To be designed]

## UI Requirements

[To be designed]

## Acceptance Criteria

[To be designed]

## Open Questions

[To be designed]
