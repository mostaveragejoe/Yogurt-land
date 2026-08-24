# Smoke Test: Critical Paths

**Purpose**: run these checks in under 15 minutes before any QA hand-off.
**Run via**: `/smoke-check` (which reads this file).
**Update**: add an entry when a new core system lands; delete nothing.

Items marked *(not yet implementable)* are placeholders for systems that do not
exist yet. They are listed now so the gate grows with the game rather than being
retrofitted.

## Automated (must pass before anything below is worth doing)

1. `dotnet test tests/Hollowdeep.Tests.csproj` passes with zero failures
2. Architecture grep gates pass — core is Godot-free, core uses no stock RNG

## Core Stability

3. Godot project opens without script or import errors
4. Debug console overlay toggles on F12 and accepts a command *(autoload exists; not yet runtime-verified in the editor)*
5. Game launches to a running scene without crash *(not yet implementable — no main scene)*

## Core Mechanic

6. Terrain renders as a 3-layer cutaway and a dig updates it *(spike-proven; not yet in the game project)*
7. Colonists path to a designated dig and complete it *(not yet implementable)*
8. A raid triggers the switch to TurnBased and back *(not yet implementable)*

## Data Integrity

9. Save completes without error *(not yet implementable)*
10. Load restores identical state — the save/load spike's decisive test, promoted here once the real save path exists *(not yet implementable)*

## Performance

11. Frame time holds under sustained play on target hardware — terrain measured 2026-08-24 at p99 2.02–2.17 ms against a 16.6 ms budget; re-check once entities, VFX and UI are drawing
12. No memory growth over 5 minutes of play *(not yet implementable)*
