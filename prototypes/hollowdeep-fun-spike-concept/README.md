# Hollowdeep Fun Spike (concept prototype)

**Hypothesis under test**: if the player fights a turn-based raider defense inside a layout
they authored, their architectural choices will visibly decide the battle — confirmed if
(a) different layouts against the same raid produce traceably different outcomes and (b) the
player voluntarily redesigns and retries unprompted.

**Riskiest assumption**: with ONE enemy type and zero colony sim, the architecture alone
generates interesting tactical decisions.

**Status**: **Concluded 2026-07-25 — hypothesis CONFIRMED, verdict PROCEED**
(CD-PLAYTEST gate: CONFIRM, notes CD-10–CD-18). Full findings, caveats, and the creative
assessment are in [`REPORT.md`](REPORT.md).

## How to run

Open `prototype.html` in any browser — single self-contained file, no server, no install.

Build mode: pick a tool, paint walls (dirt / granite / reinforced) and doors on a material
budget, place your three colonists, then sound the alarm. Battle: 2 AP per colonist — move
(≤4 cells), shoot (range 7, needs line of sight), or toggle an adjacent door. Raiders path to
the gold hoard, smashing whatever is weakest in the way. After the raid, read the scars,
rebuild, and face a larger raid.

## Headline findings

- Raiders emergently besiege the *cheapest material* — a consequence of cost-based pathing,
  never a scripted "find the weak point" routine — and the scar report names the choice that
  failed. This was the strongest, bias-immune evidence for Pillar 1.
- Strongest reported fun came from planning under uncertainty *before* contact, then folding
  what was learned into the next layout.
- Only negative finding: one-rule raider AI is fully predictable once seen — which promoted
  raider reactivity to an MVP acceptance criterion (CD-14).
- Player-initiated design boundary: no world construction during combat; in-combat agency
  comes from colonists and pre-built objects (CD-10 – CD-13).

## Rules

Throwaway. Hardcoded values, greybox visuals, no colony sim. Production code must never
reference this directory; the real game is written fresh from the GDDs this report informs.
