# DECISIONS

Decisions made during the one-shot build that the spec left open, with the
pillar(s) used as tiebreaker. Spec-mandated choices are not repeated here.

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | Godot **4.7.1-stable mono** confirmed available and used, as specified. Godot.NET.Sdk/4.7.1 from NuGet. | Spec §3. |
| 2 | Map is **64×64×8** (2×2 chunks of 32×32 per layer, 8 layers). | §8 allows it for one-shot feasibility; keeps flood/region rebuilds trivially inside budget. |
| 3 | "Hand-authored" map is a deterministic generator with fixed seed `0xH0110D33` (`0x48011053`): surface layer, dirt band (z 5–6), granite band (z 1–4), reinforced veins, one cave pocket, flat spawn clearing + starting stockpile + 10 colonists. | §8 explicitly allows. |
| 4 | `EntityId` packs the entity kind in the top byte of the long; low 56 bits are the monotonic counter. Writers assert kind on every write. | Makes the ADR-0003 "id kind" assert cheap and impossible to forget. |
| 5 | Diagonal legality implemented exactly per §4d: a diagonal step is legal iff **at least one** of its two orthogonal neighbors is walkable; both blocked ⇒ sealed. | Spec text is explicit; the corner-cut-ban test encodes it. |
| 6 | Colonists are melee-only (miners 25 dmg, others 20). The cheap ranged variant went to **raiders** (ranged raider: 15 dmg, range 8, LOS required). | §5.12 wording places the ranged variant among raider types; Pillar 1 — ranged raiders make walls/sightlines matter more. |
| 7 | Move budget per AP = 50 octile units (5 orthogonal steps; diagonals cost 14), door traversal +10. | Direct translation of "5 cells/AP" into the integer octile cost model. |
| 8 | Breach trigger radius N = 10 cells (Chebyshev, same or adjacent Z) from any colonist or the colony core (hearth). | Close enough that prep matters, far enough that the player sees the raiders coming. |
| 9 | Needs decay: Food 100→0 in 4 min at 1x (eat ~2×/day at 8 min days), Sleep 100→0 in 8 min, eat threshold 35, sleep threshold 25. Eating restores to 100 (consumes 1 food item), sleeping restores over 45 s. Work/morale decays only while idle and is cosmetic in MVP. | §8 day-length tuning. |
| 10 | Wealth = cellsDug + 2×wallsBuilt + stockpile item count; raid threshold 150. | §5.11 formula shape, tuned so raid 2 is bigger than raid 1 after normal play. |
| 11 | Colony "core" = the hearth cell placed in the spawn clearing at map generation. Raiders target nearest colonist, else the hearth. | Gives raiders a stable objective even if colonists hide. |
| 12 | RNG is xoshiro256** with SplitMix64 seeding; one named stream per system (worldgen, needs, jobs, raids, combat, misc). Full 256-bit state serialized, so streams resume at arbitrary draw counts after load. | ADR-0001 requirement. |
| 13 | The battle checkpoint and colony saves share one binary codec; checkpoint additionally serializes combat side tables + TB authority state, and is written by the checkpoint writer only (writer-id header enforced). | ADR-0004. |
| 14 | Smoke harness is a pure-core scripted scenario (also compiled into the test suite) rather than a headless-Godot script. | §7.2 allows either; pure-core keeps it deterministic and CI-friendly. |
| 15 | In-battle "activation 0" checkpoint is written synchronously with the swap into TB; subsequent checkpoints at each `AwaitingPresentation → NextActor` beat via the coalesce-newest async writer. | ADR-0004. |
| 16 | Ranged raiders appear from raid 2 onward (1 in raid 2, then 25% of each raid), so the first battle teaches melee basics first. | Pillar 3 — lessons arrive one at a time. |
| 17 | Doors auto-open for colonists only in RT; raiders treat any door as a wall to break (RT) / blocked unless broken (TB). | Pillar 1 — doors are player-authored defenses, not raider conveniences. |
