# Concept Prototype Report: Hollowdeep Fun Spike (tactics-in-your-own-base)

> **Date**: 2026-07-25
> **Prototype Path**: HTML
> **Concept File**: design/gdd/game-concept.md

---

## Hypothesis

If the player fights a turn-based raider defense inside a layout they authored, their architectural choices will visibly decide the battle — confirmed if (a) replaying the same raid against two different layouts produces clearly different outcomes/costs the player can trace to specific choices (chokepoint, door placement, wall tier), and (b) the player voluntarily redesigns and retries — "one more layout" — without being prompted.

---

## Riskiest Assumption Tested

**With ONE enemy type and zero colony sim, the architecture alone generates interesting tactical decisions.** This was the concept doc's named make-or-break creative bet ("Is tactics-in-your-own-base fun?"). It proved out: the player reported the strongest moments came from planning under uncertainty and folding battle lessons into the next layout — with a single raider type and a one-rule AI. The variety engine is the player's own architecture, exactly as the MVP hypothesis claims.

---

## Approach

A single self-contained `prototype.html` (canvas + vanilla JS, ~600 lines): build mode with a material budget (dirt/granite/reinforced walls + doors, 3-colonist squad placement) → raid: raiders Dijkstra-path to the gold hoard treating smashable structures as time-cost, so they emergently breach the weakest architecture → turn-based squad combat (2 AP: move/shoot/toggle door; Bresenham LOS blocked by walls and closed doors, for both sides; destructible everything) → after-action SCARS report naming exactly what broke and where → rebuild with persistent damage → escalating next raid. Built and headless-verified (Playwright: full raid played via UI, squad-wipe path, zero JS errors) in one session.

**Path chosen:** HTML
**Reason for path:** The combat is turn-based, so browser latency cannot corrupt the result; a double-clickable single file maximized replay convenience; the Godot project stays clean for the technical spikes that actually need the engine.

**Shortcuts taken (intentional):**
- All values hardcoded (HP, damage, costs, budget, raid sizes)
- Colored-rectangle greybox art; no sound; no menus; no saves
- No colony sim underneath — build mode stands in for the entire colony loop
- One raider archetype with a single behavior rule (cheapest path to gold; smash obstacles; hit adjacent defenders)
- Full-refund demolish; revived defenders between raids (loss signal carried by scars/loot/turns instead)

---

## Result

- **Hypothesis behaviors observed:** the player built new layouts voluntarily across raids and reported the layout-vs-outcome link directly. Verbatim: *"the best moments were the first moments where I had to figure out the best game plan without the enemies in the environment. I didn't know where they would come from, what their skills or plans were, and I had to react after encountering them and could use that info into the next round."* That is the expand → harden → get tested loop and Pillar 3 (Scars Teach) functioning in greybox form.
- **Weakest-architecture targeting worked as designed:** in verified runs, raiders breached dirt walls and ignored adjacent granite — the scar report traced the failure to the cheap material choice, which is the Pillar 1 payoff (layout has visible consequences).
- **Worst moment (verbatim):** *"The enemies were unreactive, just b-lined for the gold."* One-rule AI is fully predictable once seen; raider behavioral variety is what keeps the same base fresh. This confirms the concept doc's named "combat variety at home" risk is real and must be carried by raider design (CD-3 objectives/withdraw meter, faction variety).
- **Directional design decision surfaced (player-initiated):** the prototype's between-raid build screen is a compression artifact, not a target design. The player does **not** want free-form world construction during/around combat sequences — construction belongs to the normal colony loop (colonist labor over time). In-combat interactivity should come from **what the colonists bring**: per-skill/per-job deployables, revives, and interactions with pre-built environment objects — "an interactive environment that disallows constructing the world environment." (Structurally consistent with ADR-0001/0002: combat's only terrain writer is Combat: Targeting & Resolution — there is no build path in TurnBased.)
- No surprises reported; no bugs encountered during the user session.

---

## Metrics

| Metric | Value |
|--------|-------|
| Path used | HTML |
| Iterations to playable | 1 (one display bug found and fixed by the headless test before hand-off) |
| Prototype duration | ~1 session (well under the 1-day cap) |
| Playtesters | 1 internal (project owner) |
| Feel assessment | N/A by design (turn-based; HTML path chosen because feel/timing is not the hypothesis) |
| Hypothesis verdict | **CONFIRMED** |

---

## Recommendation: PROCEED

The hypothesis was confirmed by the exact behaviors it predicted: voluntary layout redesign across raids and traceable layout-to-outcome causality — achieved with one enemy type, greybox visuals, and no sim, which is the strongest available evidence that the hook itself (not surrounding content) carries the fun. Player verdict, verbatim: *"Proceed. There is a great potential in that style and I think it gives the player agency over the combat that other games don't offer, and in a unique style for the colony sim genre."* The two findings (raider reactivity; no construction during combat) are design-shaping, not verdict-threatening, and both have clear homes in the Combat GDD set.

---

## If Proceeding

- **Core tuning values discovered:** none binding (all values placeholder), but the *shape* worked: wall-tier HP ratios (3/8/16 vs. 2 dmg/turn) made material choice legible in one raid; escalating raid size (+1/raid) forced redesign under pressure; LOS-blocks-both-ways made doors a real tactical object (closed = safe but blind).
- **Assumptions confirmed:** the MVP core hypothesis (architecture generates the variety, one raider type suffices to test it); Pillar 1 (layout choices had visible consequences via emergent weakest-path breaching); Pillar 3 (the scars report was read and acted on); pre-battle planning under uncertainty is a first-class fun source.
- **Assumptions disproved:** none of the concept doc's assumptions failed. One implicit prototype affordance was explicitly rejected by the player: build-anywhere-between-fights as a combat-adjacent activity. Construction is colony-loop-only; combat gets interaction through colonists and pre-built objects.
- **Emergent mechanics:** raiders emergently besieging the weakest material (pure pathfinding cost, no scripted "find weak point" logic) — keep this exact mechanism in the real Raider Decision-Making design; enemy-intel-across-raids (learning spawn/behavior and countering next time) — candidate framing for Raid Trigger/threat design.

**Findings routed to:**
1. **Combat set GDDs (#19–#23) + Squad Prep (#24)**: in-combat agency = per-skill/job deployables, revives, door/trap/environment interactions — never world construction. (New requirement candidate for the Combat GDD set.)
2. **Raider Decision-Making (#23) / Raid Trigger (#18)**: raider reactivity is the freshness engine — CD-3's objective/withdraw meter is necessary, not optional; keep cost-based breach pathing.
3. **Material-Tier Destructibility (#15)**: tier-vs-breach-time legibility confirmed as the teaching mechanism; preserve "scars name the failed material" (CD-1 already mandates this).

**Next steps:**
1. Remaining Tier 0 spikes (technical): terrain → mode-switch → pathfinding → save/load (per systems-index spike gate; ADRs promote to Accepted on their results)
2. `/design-system terrain-data-model` and onward per the design order, folding the three routed findings above into the Combat/Raider/Destructibility docs when reached

---

## If Pivoting

N/A — verdict is PROCEED.

---

## If Killing

N/A — verdict is PROCEED.

---

## Lessons Learned

- **What assumptions were broken by actually building this?**
  None fatal. The build-screen-as-colony-loop compression revealed a boundary the concept doc never had to state: construction is a colony verb, full stop — combat interactivity must come from colonists and pre-built objects, not from editing the world.

- **What surprised us that didn't show up in the brainstorm?**
  How much fun load the *pre-battle uncertainty* carried — the concept doc emphasizes the battle and the scars, but the player's best moments were planning before contact and counter-planning after. Worth honoring in Raid Trigger design (imperfect threat information as a feature).

- **What would we test differently next time?**
  Give the AI even one reactive behavior (e.g., retarget toward exposed defenders) to measure how much raider reactivity amplifies replay pull; and test with an external playtester who hasn't read the concept doc.

---

## CD-PLAYTEST Gate (2026-07-25, full review mode)

**Verdict: CONFIRM PROCEED** — recommendation stands unchanged, including the three routed findings. Nine binding CD notes issued (**CD-10–CD-18**, recorded in `design/gdd/systems-index.md` → Creative Director Notes (CD-PLAYTEST)): the combat/construction boundary rule ("combat may change world state, never author it"), player-activated pre-built objects over autonomous traps, the four-part deployable legitimacy test (MVP guard: positioning/doors/destruction only), downed→stabilize instead of free revives, raider reactivity promoted to an MVP acceptance criterion in #23 (behavioral depth, not archetype count; preserve emergent cost-based breach pathing exactly), threat-information floor/ceiling + cross-raid intel accrual, the pre-contact moment as a designed moment (#24 watch flag), Discovery's second vector (enemy-knowledge — MVP-testable), and the lesson-to-answer latency budget on Repair & Rebuild #25.

**Pillar read**: Pillar 1 CONFIRMED on bias-immune mechanical evidence; Pillar 2 HALF confirmed — the in-battle-skill half is untested and is the same finding as "raiders unreactive"; Pillar 3 confirmed in its cheapest form (see caveat 3); Pillar 4 — the no-construction-in-combat decision is the correct reading of the pillar, endorsed as CD-10; Pillar 5 untested by design.

**Caveats recorded by the gate:**
1. n=1 and the playtester is the concept's author — confirmation-direction evidence is weak (falsification would have been strong). Read "CONFIRMED" as *not falsified by the cheapest available test*.
2. One evidence class is bias-immune and weighs more than the rest: emergent weakest-material breaching + the traceable scar report — the strongest single result in this spike; it substantially de-risks Pillar 1.
3. Hypothesis criterion (b) was partially bought by prototype shortcuts (full-refund demolish, instant construction, free revives). The real game removes all three — see CD-18; the sharpest limitation in this report.
4. The spike validated the hook, not the product: the colony-sim→tactics bridge (cost, latency, emotional investment of building through autonomous colonists) is entirely untested, and it is where the two-genre-hybrid market risk actually lives.
5. Novelty decay is untested and the one negative finding is a decay function — predictability compounds with exposure; "no surprises reported" is not evidence of durability.
6. Feel, pacing, and battle length carry zero signal (correctly — HTML path). CD-9's 8–15 min target / 20 min ceiling remains unvalidated.
7. Next fun-relevant checkpoint: the vertical slice with real construction latency, criterion *"does the player still voluntarily redesign when redesign costs real colony time?"* — plus the report's own suggestions (one reactive AI behavior; an external playtester who hasn't read the concept doc).

**Creative assessment (quoted):**
> The fun spike did the one job a fun spike exists to do: it proved the hook carries the game before we spend years building the game around it. The most valuable result is not the player's verdict but the mechanism behind it — raiders emergently besieging the cheapest material, and a scar report that named the choice that failed, with one enemy type and no simulation underneath. [...] The spike also did something better than confirm: it found the real boundary of the design. Construction is a colony verb; combat gets its agency from colonists and from objects the player pre-authored in peacetime. That boundary is not a constraint on the fantasy, it is the fantasy — the home you already built is the weapon, and architecture is a record of judgment rather than a real-time resource. [...] Proceed — and treat raider behavior, not raider variety, as the next thing that has to be proven.

---

> *Prototype code location: `prototypes/hollowdeep-fun-spike-concept/`*
> *This code is throwaway. Never refactor into production.*
