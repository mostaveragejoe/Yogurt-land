# BUILD-REPORT — Hollowdeep one-shot build

Date: 2026-08-05 · Godot **4.7.1-stable mono** (exact spec version, confirmed available)
· .NET 8 · Map **64×64×8** (allowed by §8 for one-shot feasibility, DECISIONS #2).

## Verification results (§7, in order)

1. **Core test suite — PASS.** 41/41 tests green on `Hollowdeep.Core` (plain .NET, no
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
2. **Headless smoke — PASS.** Scripted arc (dig orders → fortify → raid → warning →
   breach switch → auto-resolved battle → reconcile → scar-driven repairs) runs with
   no exceptions/assertion failures; final state hash stable across two fresh runs.
   Implemented pure-core (§7.2 allows either); also invocable in-game via F12 `smoke`.
3. **Launch check — PASS.** Headless import clean; 180-frame headless run exits 0 with
   no errors; real-rendered screenshots captured under xvfb (Forward+, llvmpipe):
   `docs/colony.png` (cutaway mountain, hearth glow, 10 named colonists, stockpile +
   farm overlays, tools) and `docs/combat.png` (turn-based battle: move-range flood,
   raiders, initiative/AP panel, colonist roster).
4. This report.

Purity gate: `tools/check-core-purity.sh` green at every commit — zero Godot references
under `src/core`.

## Done and verified (by tests and/or headless run)

- Time authority swap with zero state conversion; fixed-dt sub-stepping (speeds 0–3
  multiply substep count); pause is RealTime-at-zero-substeps, never engine pause
- Terrain: packed 8-byte cells, 32×32 chunks, single write facade, mutation-window
  enforcement, pooled batched change events with previous-state capture
- Entity stores + per-(system×field-group) writers asserting window/mode/id-kind;
  health writer-per-authority; occupancy advisory-RT/exclusive-TB, synchronous with
  position writes
- A* per §4d (octile 10/14, index-tiebroken heap, diagonal rule, stairs both ways,
  +10 door surcharge), breach-cost variant, amortized region index (never per-dig)
- Jobs/needs/hauling: dig, stair-down, build (wall/floor/stair/door), haul with
  reservation-gated stacks, repair, eat, sleep, rally; needs decay + interrupts;
  mushroom farm regrowth; wealth tracking
- Raids: timed escalating waves sized by wealth, edge spawns, wall/door battering with
  scar logging, breach-radius switch trigger, 30 s warning window
- Turn-based combat: initiative colonists-then-raiders by id, 2 AP, move budgets,
  cover −25%, Bresenham LOS, melee + ranged raider variant, raider AI (close, attack,
  batter structures), rout at 60%, downed/struck-while-downed rules
- Reconcile: drain one-slot inbox, stabilize downed, reap ALL raiders, clear side
  tables; downed colonists recover at 50% HP over one day in RT
- Saves/checkpoints per ADR-0004, including refusal of colony saves in TB and the
  loud corrupt-save fallback path
- Godot boot, world render, and both game modes reaching the screen

## Built but unverified (no human has played it)

- Feel/usability of mouse tools, camera, and combat clicking; UI layout on other
  resolutions and themes
- Long-session pacing (raid 2+ difficulty curve, needs tuning over multiple days)
- Steady-state allocation freedom and frame budgets (§4f): the sim is written
  allocation-conscious (pooled batches, reused path buffers, struct scopes) but was
  not profiled in this environment; draw calls not counted against the ≤150/≤500
  budgets (64×64×8 with per-layer GridMaps should sit far under them)
- Door open/close animation states (doors render static; broken state renders)
- Windows/macOS builds (Linux only here); editor-driven export templates untested

## Cut (per §9 cut order, logged in DECISIONS.md)

- **Placeable hearth/torch props** (first in the sanctioned cut list) — the worldgen
  hearth with its one warm light exists; a placeable furniture item does not
- **TB squad-placement phase** — squad prep is RT rally orders during the warning
  (DECISIONS #18); the activation-0 checkpoint semantics are unchanged
- **Multiple quicksave slots** — one quicksave + autosaves + checkpoint (allowed cut)
- Idle wander animations, audio cue stubs, colonist portraits — never started

**Not cut** (the five §9 never-cut items, all present and tested): the mode switch,
battle-on-your-own-map, destructibility, hauling visibility, checkpoint resume, and
the repair loop.

## Known rough edges

- Raider RT movement recomputes its approach path more eagerly than colonists do;
  harmless at this map size but worth a revisit at 128×128×16
- `FarmSystem` scans the item list per plot per tick (bounded, but O(plots×items))
- The debug console's `give_material`/`heal_all` open the mutation gate directly —
  sanctioned and asserted, but they bypass the writer-interface pattern (DECISIONS #25)
- UI is functional greybox: no tooltips beyond hints, no rebindable keys
- Job priorities exist throughout the system (personal jobs pre-empt, haul ranks
  below dig, deterministic priority/distance/id scoring) but the UI paints all
  designations at the default priority — a per-designation priority adjuster did
  not make the cut (§5.10 partially met)

## How to reproduce verification

```sh
dotnet test                                   # suite incl. smoke + checkpoint tests
tools/check-core-purity.sh                    # Godot-free core
godot --headless --import --path .            # import
godot --path . -- --shot-colony=/tmp/c.png    # boots, screenshots, quits
```
