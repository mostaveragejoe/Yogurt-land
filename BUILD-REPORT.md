# BUILD-REPORT — Hollowdeep one-shot build

Date: 2026-08-05 · Godot **4.7.1-stable mono** (exact spec version, confirmed available)
· .NET 8 · Map **64×64×8** (allowed by §8 for one-shot feasibility, DECISIONS #2).

## Verification results (§7, in order)

1. **Core test suite — PASS.** 47/47 tests green on `Hollowdeep.Core` (plain .NET, no
   Godot). Covers every §7.1 bullet:
   - determinism: same seed + same scripted input → identical world hash, twice
   - TickSequence gapless across RT→TB→RT (exact substep/action accounting)
   - save→load→save byte-identity (full file, fixed counter)
   - reloaded world evolves identically to one that never left memory
   - checkpoint resume mid-battle (hash-identical at the activation beat, identical
     battle completion after resume; activation-0 + rolling checkpoint files verified
     on disk with writer-id headers; battle-end autosave supersedes; corrupt-fallback
     ordering by in-file counter)
   - pathfinding law: corner-cut ban + one-orthogonal legalization, sealed goal
     returns empty (not stale), door/broken-door walkability in both modes, unit
     never blocks itself, region index agrees with A* connectivity
   - occupancy normalization: deterministic, lowest id keeps cell, unique TB occupancy
   - mutation-window and mode assertions fire on illegal writes
   - raider reap completeness (live raiders included; inbox drained exactly once)
   - door repair via designation (damaged AND broken), torch construction,
     wounded-colonist regen
2. **Headless smoke — PASS.** Scripted arc (dig orders → fortify → raid → warning →
   breach switch → auto-resolved battle → reconcile → scar-driven repairs) with no
   exceptions; digging progress asserted (≥10 cells); final hash stable across two
   fresh runs. Pure-core (§7.2 allows either); also invocable in-game via F12 `smoke`.
3. **Launch check — PASS.** Headless import clean; 240-frame headless run exits 0;
   real-rendered screenshots under xvfb (Forward+, llvmpipe): `docs/colony.png` and
   `docs/combat.png`.
4. This report.

Purity gate: `tools/check-core-purity.sh` green at every commit — zero Godot references
under `src/core`.

## Measured performance (§4f)

- **Steady-state simulation allocation: 0 B per sub-step**, measured over 600 sub-steps
  of live colony play after warm-up (`SteadyStateSimAllocationIsNearZero`, exact 0 in
  the recorded run; the test's ceiling is 200 B/sub-step to catch regressions).
- **Draw calls: 382 (colony) / 370 (combat) per frame** at 1600×900, against the ≤500
  frame budget. Terrain's share is bounded by construction: 7 visible cutaway layers ×
  2 GridMaps × 4 octants = ≤56, far under the ≤150 terrain budget.
- Whole 47-test suite (including a 2-sim-day endurance run) completes in seconds;
  72,000 sub-steps of 10-colonist sim run well inside real time.

## Endurance run (automated 2-sim-day playtest)

`TwoRaidsOverTwoSimDaysColonySurvivesAndSecondRaidIsBigger` scripts a player's opening
(corridor + room digs, then door/walls/torch once the mouth is open), then lets two
full days run, auto-resolving battles and painting repairs from scars:

- 2 raids fought, sizes escalating 4 → 5, both ended in rout
- 10/10 colonists alive at the end; food economy stable (farm + hauling)
- 55 cells dug, fortifications built, raiders fully reaped after each battle

This run also caught (and its fix is now regression-locked) a real design bug: under
the corner-cut ban, diagonal mining could open a sealed, unenterable pocket and stall
the job queue — mining reach is now orthogonal-only (DECISIONS #26).

## Done and verified

Everything in §5, specifically including the items that were previously listed as
gaps — all now implemented and tested or screenshot-verified:

- per-designation **priority** painting (High/Normal/Low) with priority-tinted overlays
- **placeable torch props** + the hearth as a prop (one warm point light each,
  aesthetic only), built by colonists from a blueprint for 1 material
- **door repair** via the repair tool for both damaged and broken doors
- door **auto-open animation** in RT (presentation-only slide)
- **combat event log** in the battle panel (moves, hits/misses, structure damage,
  downs, rout) alongside the initiative/AP readout
- **pause menu** (Esc): three save slots, three load slots, new-colony (archives old
  saves, never deletes), quit-with-confirm mid-battle
- colonist roster buttons **center the camera** on the colonist
- **stockpile painting validates walkability**; farm plots render with their own overlay
- wounded colonists **heal passively** while fed; downed recovery unchanged
- debug console gained `perf` (draw calls, primitives, fps, memory)

Plus the previously verified core: time-authority swap, terrain facade, writers,
occupancy, A*/regions, jobs/needs/hauling, raids, TB combat, reconcile, saves and the
battle checkpoint — see the test list above.

## Remaining honest limits

- **No human has played it.** Everything a machine can verify is verified; feel,
  difficulty curve, and camera comfort still need a human hour.
- **Playable exports shipped.** `dist/Hollowdeep-windows-x86_64.zip` is a release
  export (Godot 4.7.1 mono templates, embedded pck, bundled .NET runtime, player
  README). The export pipeline was verified end-to-end by exporting the Linux build
  from the same presets and launch-checking that binary under xvfb (boots, renders
  382 draw calls, screenshots, exits clean). The Windows .exe itself is cross-built
  and could not be executed in this Linux environment — the engine layer is
  identical, but a Windows double-click smoke test remains for a human. The build
  is unsigned (SmartScreen will warn).
- Sanctioned §9 simplifications that are design decisions, not gaps: squad prep is
  RT rally during the warning (DECISIONS #18), rout ends the battle at the threshold
  (#19), colonists sleep in place (#20).

**Never-cut items (§9), all present and tested**: the mode switch,
battle-on-your-own-map, destructibility, hauling visibility, checkpoint resume, and
the repair loop.

## How to reproduce verification

```sh
dotnet test                                   # 47 tests incl. endurance + allocation
tools/check-core-purity.sh                    # Godot-free core
godot --headless --import --path .            # import
godot --path . -- --shot-colony=/tmp/c.png    # boots, prints draw calls, screenshots
```
