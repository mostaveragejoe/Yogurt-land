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

This system owns no assets, but it owns the constraint governing the game's single most important presentation beat. Requirements transmitted to their owners:

- **The freeze is a decompression, not a bang** (creative-director adjudication, 2026-08-02): ambient colony bustle drops to silence; motion stops; no stinger, no screen shake, no adrenal spike. A jump-scare transition would inject reflex-game sensation into the one beat that must read as *judgment* — silence is the sound of an exam hall. **Owner: audio-director** (the silence drop and the ambient bed it is measured against), **art-director** (freeze-frame readability). *(AC-45)*
- **Freeze legibility within ~2 seconds**: the player must locate the breach and read their own preparedness off the frame — camera settles on the breach point; the colony's held motion stays visible (colonists caught mid-stride, not despawned). **Owner: art-director + Combat UI (#27)**.
- **The return beat**: the colony view restores at pause over the damage — no triumphant sting on victory, no failure sting on defeat; the survey (CD-1) carries the verdict. **Owner: audio-director + #27**.
- Speed-change feedback (dial clicks, time-lapse audio texture at 2×/3×) is cosmetic and owned by the UI/audio pass — no gameplay semantics (lighting/audio never signal threat state, per the art bible's aesthetic-only rule).

## UI Requirements

Consolidated from Core Rules for the three UX specs; this GDD owns the *rules*, the specs own the presentation:

- **Speed dial** (colony HUD, all three specs reference it): displays requested multiplier only (Rule 3); hidden during combat, replaced by combat UI (#27); reads 0 on every return from battle (Rule 8).
- **Order inertness in combat** (#26): all six order categories visibly disabled or absent — not merely unresponsive — with no queuing (Rule 5, AC-39). What the player sees when they try is #26's UX-spec decision (CD-10 requires it be *inert, not hidden* at minimum).
- **Quit-mid-battle confirmation** (#27 or shared shell UI): "Quitting now will lose this battle's progress" — blocking dialog, combat mode only (EC-8, AC-42).
- **After-action survey** (#27, CD-1): presented at the forced pause before the player regains the dial (Rule 8); camera fallback to breach site on total loss (EC-5).
- **Menu overlay** (shared shell): force-closes on switch-in (EC-4); never affects sim or turn state (Rule 11).
- **Notifications** (shared component): queued during combat, flushed on return (Rule 10) — presentation of the flush (burst vs. digest) is the component's spec decision.

## Acceptance Criteria

Every criterion below is independently verifiable without reading the rest of this GDD. Each is tagged with the Core Rule(s), Formula, or Edge Case(s) it covers, and its story-type gate level per `coding-standards.md`. Criteria reused verbatim from the Tier 0 spike (`prototypes/mode-switch-spike/`) are marked **[regression-locked]** — these already pass 61/61 and must be wired into the standing CI suite, not re-derived.

### (a) Headless / Automated — plain .NET, no Godot runtime (Logic, BLOCKING for this system's own Done)

1. **[Rule 1]** GIVEN TurnBased authority is active, WHEN `Advance()` runs for N physics frames, THEN zero `Tick()` calls are dispatched to any RealTime-registered system. **[regression-locked]**
2. **[Rule 1, Rule 13]** GIVEN RealTime authority is active at speed 0, WHEN `Advance()` runs for N frames, THEN `SubStepsDelivered == 0` every frame and RealTime systems are queried as "active, 0 sub-steps" — distinct from AC-1's "inactive, not ticked" so a bug in one cannot be mistaken for a bug in the other.
3. **[Rule 3, Formula D.1, spike criterion 3]** GIVEN speeds 0/1/2/3 in turn under unstalled frame timing, WHEN 60 physics frames run at each, THEN delivered sub-steps are 0/60/120/180 respectively and `SubStepDuration` never changes. **[regression-locked]**
4. **[Rule 3, Formula D.1]** GIVEN a stalled dispatch requesting more sub-steps than `SubStepCap` (e.g. 12 requested, cap 8), WHEN that frame is processed, THEN `SubStepsDelivered == 8`, the backlog is dropped (not queued for a future burst), and no exception is thrown. **[regression-locked — "sub-step cap slow-down"]**
5. **[Rule 4, EC-1]** GIVEN RealTime authority at speed 0, WHEN N frames elapse, THEN Raid Trigger's threat-accumulation call count is 0 and its threat value is provably unchanged — asserts EC-1's "impossible by construction" claim directly rather than by inspection.
6. **[Rule 5]** GIVEN TurnBased authority is active, WHEN a colony order-writer (blueprint/dig/build/zone/craft/haul) attempts a write outside the combat-mode writer set, THEN the mutation-window/writer-set guard rejects it — extends the spike's "writer-per-authority health arbitration refuses the wrong mode" pattern to order writers.
7. **[Rule 6, EC-2]** GIVEN an encounter is pending or active, WHEN a second `RequestSwitch(TurnBased, …)` is issued, THEN the result is `RejectedEncounterActive` and no second encounter state is created. **[regression-locked]**
8. **[Rule 6]** GIVEN one trigger event references multiple non-contiguous breach cells, WHEN `RequestSwitch` is called with all cells in one `SwitchTransitionData.BreachCells`, THEN exactly one `EncounterId` is created.
9. **[Rule 7]** GIVEN each of {victory, defeat, raider withdrawal}, WHEN Combat calls the switch-back for that outcome, THEN the identical reconcile→autosave→speed-0→survey sequence executes for all three — no outcome skips a step.
10. **[Rule 8]** GIVEN a battle resolves, WHEN the switch-back is accepted, THEN the observed order is: authority swap → `PostEncounterReconcile` → battle-end autosave → speed forced to 0 → survey-ready signal fired — asserted via ordered event/log sequence in the harness.
11. **[Rule 8, spike criterion 4]** GIVEN terrain destroyed mid-encounter (some reservations/jobs/paths touch destroyed cells, some don't), WHEN `PostEncounterReconcile` runs, THEN zero orphans remain targeting destroyed cells AND untouched entries survive unmodified. **[regression-locked]**
12. **[Rule 9]** GIVEN a switch into combat is accepted, WHEN the autosave fires, THEN the captured snapshot has `Mode == RealTime` and is sequenced strictly before the swap. **[regression-locked — "CD-9 refuses to snapshot inside a battle"]**
13. **[Rule 9]** GIVEN a battle ends, WHEN the battle-end autosave fires, THEN it is captured strictly after `PostEncounterReconcile` completes (no orphaned state in the snapshot).
14. **[Rule 9]** GIVEN either autosave moment, WHEN the save call fires, THEN no save-slot UI prompt/event is raised (silent) — verified against a stub UI sink; full serialization fidelity is AC-52.
15. **[Rule 10]** GIVEN a notification-worthy event fires during combat, WHEN `CurrentMode == TurnBased`, THEN zero notifications reach a stub presentation sink until mode returns to RealTime, at which point the queue flushes.
16. **[Rule 11]** GIVEN a menu-overlay open/close cycle at any speed or mid-turn, WHEN it closes, THEN `TickSequence` is unbroken across the window and the prior speed/turn state resumes exactly. Companion static check: grep gate confirms no `SceneTree.paused` reference exists in the simulation path.
17. **[Rule 12]** GIVEN a colony order submitted in the exact dispatch a switch is accepted, WHEN the swap lands, THEN the order produces zero world effect and zero exception.
18. **[Rule 13]** GIVEN scenario (a) speed-0-RealTime and (b) active-TurnBased, WHEN queried via `CurrentMode` + `SubStepsDelivered`, THEN the two are programmatically distinguishable, not merely visually identical.
19. **[Rule 14]** GIVEN a system registered RealTime-only, WHEN TurnBased is active, THEN it receives zero TurnBased ticks with no explicit opt-out required (freeze-by-default).
20. **[Formula D.1]** GIVEN `SubStepsRequested = 0`, WHEN evaluated, THEN `SubStepsDelivered = 0`.
21. **[Formula D.1]** GIVEN `SubStepsRequested` far exceeds `SubStepCap` (e.g. 1000), WHEN evaluated, THEN `SubStepsDelivered == SubStepCap` exactly (never exceeds it regardless of request size).
22. **[Formula D.2]** GIVEN defaults at 3× (`SubStepsDelivered=3, SubStepDuration=1/60, EngineFrameRate=60`), WHEN evaluated, THEN `GameSecondsPerRealSecond == 3.0` (± 1e-9) — matches the "8 real minutes per 24-game-hour day at 3×" worked example.
23. **[Formula D.2]** GIVEN `SubStepsDelivered = 0`, WHEN evaluated, THEN `GameSecondsPerRealSecond == 0` exactly.
24. **[EC-6]** GIVEN a manual-save call is issued in the same dispatch a switch-to-TurnBased is accepted (pending, not yet applied), WHEN the save executes, THEN it completes normally with `Mode == RealTime` (the swap has not yet applied). **[regression-locked target]**
25. **[EC-9]** GIVEN a sub-frame accumulator backlog (<1 sub-step) exists at the moment of freeze, WHEN the authority later swaps back, THEN the RealTime accumulator resumes from the exact preserved residual — never reset to zero. **[regression-locked target]**
26. **[EC-7]** GIVEN a speed-change input lands in the same dispatch as an accepted switch, WHEN that dispatch completes, THEN the new speed is honored only for the remainder of that dispatch, and the subsequent switch-back always forces speed to 0 regardless.
27. **[EC-11]** GIVEN speed remains at 0 after a battle-end forces it there, WHEN N frames elapse with no player input, THEN threat accumulation stays at its post-reconcile value — same mechanism/assertion as AC-5.
28. **[ADR dispatch determinism]** GIVEN two systems registered at identical `(TickPhase, priority)`, WHEN both attempt registration, THEN a debug assertion rejects it. **[regression-locked]**
29. **[ADR `DeltaSeconds=0` rule]** GIVEN a system registered under TurnBasedAuthority reads `TimeContext.DeltaSeconds` in `Tick()`, WHEN this occurs, THEN a debug assertion fires. **[regression-locked]**
30. **[ADR Validation Criterion 2]** GIVEN a fixed seed and input sequence spanning a RealTime→TurnBased→RealTime cycle plus a save/load round-trip, WHEN re-run, THEN `TickSequence`/`TurnIndex`/entity state are bit-identical and `TickSequence` is monotonic and gapless across the swap. **[regression-locked]**
31. **[ADR "zero state conversion, proven by identity"]** GIVEN a store instance before a mode swap, WHEN the swap completes, THEN the post-swap reference is the *same instance*, same values, unchanged `Revision`. **[regression-locked]**
32. **[ADR re-entrancy rule]** GIVEN `RequestSwitch` is called from inside an active `Tick()` or transition handler, WHEN it returns, THEN the result is `DeferredMidDispatch` and the swap applies only at the next between-dispatch boundary. **[regression-locked]**

### (d) Performance — headless benchmark harness (BLOCKING regression gate; tolerance bands above measured, allocation gate tight per Terrain precedent)

33. **[Dispatch cost, measured 0.578 µs/sub-step]** GIVEN the spike's reference load (7 systems × 10 colonists), WHEN benchmarked over ≥10,000 sub-steps, THEN mean cost per sub-step ≤ **0.70 µs** (measured + ~21% tolerance for harness/hardware variance). Re-baseline, don't silently raise, if the cause is legitimate scope growth.
34. **[Swap cost, measured 0.31 µs]** GIVEN a RealTime→TurnBased swap under reference load, WHEN benchmarked over ≥1,000 swaps, THEN mean cost ≤ **0.40 µs** (measured + ~29% — wider band, sub-microsecond timings are more timer-noise-sensitive).
35. **[Reconcile cost, measured 28.9 µs]** GIVEN the reference reconcile load (50 jobs, 50 paths, 20 reservations, 1 death, 3 raiders), WHEN benchmarked over ≥1,000 passes, THEN mean cost ≤ **35 µs** (measured + ~21%) — runs once per battle, so this gate exists to catch an accidental O(n²) as content scales, not because the absolute number matters.
36. **[Allocation, measured 0.00 B/sub-step, 0 Gen0/20k sub-steps]** GIVEN sustained dispatch + swap operation, WHEN measured over ≥20,000 sub-steps, THEN allocation ≤ **0.05 B/sub-step** (tight band for GC-timing measurement noise only — never a budget for new allocation) AND Gen0 count == **0**, hard binary. If tripped, fix the code (e.g. re-check for a boxed class scope per ADR-0001 Correction 1) — never loosen the gate. "Fix the harness" applies only if the harness is proven to be counting one-time JIT warm-up, nothing else.
37. **[ADR Correction 1 named-regression guard]** GIVEN `MutationWindow.Open()` is called across N dispatches, WHEN allocation is measured, THEN zero boxing occurs — names the specific historical regression (class-scope boxed 24 B/dispatch, ~4.3 KB/s at 3×) so a future contributor understands why AC-36 exists.

### (b) In-Engine / Manual — Godot build, screenshot/walkthrough evidence (UI/Visual-Feel, ADVISORY per Definition of Done)

38. **[Rule 3]** GIVEN the transition from colony play to combat, WHEN it completes, THEN the speed dial is not visible/interactable and combat UI occupies its place.
39. **[Rule 5, CD-10]** GIVEN combat is active, WHEN the player attempts each of the 6 order categories (blueprint, dig, build, zone, craft, haul) via its normal control, THEN each control is visibly disabled or absent — not merely unresponsive — and no order queues. Test each category individually.
40. **[Rule 8]** GIVEN a battle ends and reconcile completes, WHEN the colony view returns, THEN the dial reads 0/paused regardless of pre-battle speed, and the after-action survey displays before the player regains free camera/order control.
41. **[EC-4]** GIVEN a menu overlay is open, WHEN a switch to combat is accepted, THEN the overlay force-closes and the transition proceeds unblocked.
42. **[EC-8]** GIVEN a battle is in progress, WHEN the player initiates quit-to-desktop, THEN a confirmation dialog displays text substantially matching "Quitting now will lose this battle's progress" and quitting is blocked until confirmed. Verify the negative case too: no such dialog appears when quitting during colony play.
43. **[EC-8]** GIVEN the player confirms quit mid-battle and relaunches, WHEN the game loads, THEN the colony resumes at the moment combat began and the encounter restarts from its beginning. (Straddles manual/integration — save *content* fidelity is AC-52; this criterion checks only the observable in-game behavior.)
44. **[EC-5]** GIVEN a battle ends with zero surviving defenders, WHEN the after-action survey camera is placed, THEN it defaults to the breach site / colony overview.
45. **[Player Fantasy — freeze legibility]** GIVEN a raid triggers, WHEN the freeze/transition plays, THEN it reads as a decompression into silence (never a stinger) and completes within ~2 seconds of screen time. Lead sign-off + screenshot/recording evidence, against the constraints in Visual/Audio Requirements; scoreable once the freeze treatment exists (Open Question #4 — art/lighting pass).
46. **[Rule 2]** GIVEN the full input map and all colony-play UI screens, WHEN a tester audits available player actions, THEN no control calls `RequestSwitch(TurnBased, …)` directly. One-time audit, not a regression test — re-check whenever new UI (#26/#27/#28) ships.

### (c) Integration — BLOCKED on sibling systems (name the blocker; do not gate this system's own Done)

47. **[Rule 2 — BLOCKED: Raid Trigger #18]** GIVEN Raid Trigger's threshold is reached, WHEN it calls `RequestSwitch`, THEN the switch is accepted and the encounter begins with correct breach cells/participants end-to-end.
48. **[Rule 6/EC-2 — BLOCKED: Raid Trigger #18]** GIVEN a rejected switch (`RejectedEncounterActive`), WHEN #18 receives the result, THEN it resets/holds/re-arms the threat per its own (not-yet-written) design.
49. **[Rule 7 — BLOCKED: Combat set #19–23]** GIVEN each real outcome type from Combat's own state machine, WHEN it resolves, THEN it correctly triggers the identical switch-back path (AC-9 proves the manager side; this proves Combat actually calls it per-outcome).
50. **[EC-10 — BLOCKED: Squad Prep #24 + Combat set #19–23]** GIVEN Squad Prep drafts zero colonists for a triggered raid, WHEN the switch proceeds anyway, THEN Combat's turn loop correctly handles zero player-controlled units (raiders act, player observes, exit path unchanged).
51. **[ADR Correction 2 — BLOCKED: Squad Prep #24]** GIVEN multiple colonists co-located on one cell at the pending-switch moment, WHEN pre-switch placement normalization runs, THEN each unit decides against cells already claimed by earlier decisions in the same pass (not live occupancy), and only the correct subset moves. This is the specific implementation trap named in ADR-0001 Correction 2 — must be an explicit regression test once #24 exists.
52. **[Rule 9/EC-6/EC-8 — BLOCKED: Save/Load #6]** GIVEN the two autosave moments, WHEN Save/Load's real serialization runs, THEN the resulting files load correctly and reproduce the exact pre-swap / post-reconcile state.
53. **[Rule 10 — BLOCKED: Notifications]** GIVEN notifications queued during combat, WHEN the colony view returns, THEN all display with correct content/priority per Notifications' own rules.
54. **[Rule 3/5 — BLOCKED: Blueprint UI #26]** GIVEN production Blueprint UI, WHEN AC-38/AC-39 are re-run against it (not a stub), THEN they pass.
55. **[Rule 8 — BLOCKED: Combat UI #27]** GIVEN production Combat UI, WHEN AC-40/AC-44 are re-run against it, THEN they pass.
56. **[EC-3 — BLOCKED: Blueprint UI #26]** GIVEN an uncommitted authoring state (open drag-select, unconfirmed blueprint, open zone-menu selection) at the freeze moment, WHEN the freeze lands, THEN the state is discarded with no error and is not resumed after battle. (Committed jobs are already covered by AC-1/AC-2 — they simply freeze with their owner.)
57. **[Needs #13 — BLOCKED: Needs GDD + unresolved ADR-0001 Open Question]** GIVEN a decaying need at the moment combat began, WHEN the battle ends and reconcile completes, THEN Needs' resume behavior matches whatever the routed zero-elapsed-vs-catch-up question resolves to. **Flagged as a genuine coverage gap, not just a missing implementation** — the rule to test does not exist yet.

### (e) Advisory / Playtest-Tier

58. **[CD-9 battle-length budget]** GIVEN a full playtest battle, WHEN duration is measured from freeze to speed-regained, THEN it falls within 8–15 min (target), 20 min hard ceiling (escalation trigger). **Primary ownership: the Combat set's (#19–23) GDD**, since Combat owns achieving it via its own pacing levers (per this GDD's Tuning Knobs table). This GDD retains only the invariant it actually owns — no mid-battle save exists (AC-12) and no timeout/force-resolve mechanism is implied here (that is Combat's call under Rule 7). Do not duplicate the full battle-length protocol in two GDDs.
59. **[Player Fantasy falsifiable targets]** The four targets ("my base got tested" vs. "the game switched screens"; differentiated prepared/unprepared emotion at freeze; rising 3× usage as colony matures; nobody asks if damage "carries over") are **not** GIVEN-WHEN-THEN testable by scripted QA steps — they require a playtest protocol (recruitment, post-session questionnaire, qualitative coding). Track via a dedicated playtest protocol document once Visual/Audio and UI Requirements are written; do not force these into false-precision criteria.

**Verified elsewhere, deliberately not QA criteria**: Rule 2's "sole requester" invariant is a codebase-static property — CI grep / architecture-review gate on `RequestSwitch` call sites (AC-46 covers only the UI surface). Rule 14's "every spec carries a Behavior-under-each-time-authority section" belongs to `/design-review`/`/architecture-review`. Formula D.2's anti-duplication rule is a cross-document check belonging to `/review-all-gdds`.

## Open Questions

| # | Question | Owner | Resolve by |
|---|---|---|---|
| 1 | Post-battle time semantics: zero-elapsed vs. advance-by-battle-duration (as N catch-up sub-steps). Both stay architecturally open; AC-57 is unwritable until decided. | creative-director | **Before the Needs & Simulation GDD (#13)** — already routed via ADR-0001 |
| 2 | Zero-roster combat (EC-10, user decision (a)): Combat's turn loop must handle zero player units — pacing/skip UX of watching an unopposed raid. | Combat set (#19–23) | Combat GDD authoring |
| 3 | Fate of a rejected raid trigger (reset / hold / re-arm) — EC-2 guarantees only silent rejection. | Raid Trigger (#18) | #18 GDD authoring |
| 4 | The freeze treatment itself (audio bed, camera move, exact timing under the 2 s budget). | audio-director + art-director | Art/lighting pass; AC-45 unscoreable until then |
| 5 | UI-pause layer design (settings menu over combat; Rule 11's axis independence made concrete). | UX specs (#26–28 shared shell) | Before the first menu-flow spec |
| 6 | Does player-initiated retreat exist? (Rule 7 states only the invariant: if yes, identical exit path.) | Combat set (#19–23) | Combat GDD authoring |
| 7 | 4× speed: architecturally free (spike-proven), deliberately not in MVP's set. Revisit if playtests show late-game drag. | game-designer | Post-MVP playtests |
