# Tier 0 Save/Load Spike — Spike Note

> **Date**: 2026-07-26 · **Validates**: ADR-0003 validation criterion 3, ADR-0001 criterion 2,
> and cross-cutting contract #2 (serialization) end to end
> **Result**: **YES — 24/24. The whole world round-trips byte-identically, and a reloaded world
> evolves identically to one that never left memory.**

## Question

The concept doc lists save/load of a large mutable grid plus a live simulation as a HIGH
technical risk and notes it is "commonly underestimated". Does the whole world — terrain,
entities, time authority, id source, reservations — actually survive a round-trip, with derived
state rebuilt and combat state structurally absent?

## How it was run

`dotnet run -c Release` in `prototypes/saveload-spike/`. Plain .NET 8. Reuses the ADR-0002
terrain model, ADR-0001 time authority, and ADR-0003 entity layer from the earlier spikes, and
adds a binary `WorldSave` implementing the serialization contract: magic number, schema version,
stable string material keys, ascending-id record order, and a deliberate exclusion list.

## Result: 24/24

**Round-trip integrity.** `save → load → save` is **byte-identical**. Every terrain cell survives
including HP, style and the job-claim flag; colonists keep id, cell, hp, death flag,
`BattlesSurvived` and name; doors keep open/broken state; item stacks round-trip **by stable
material key, not runtime id**; stack reservations survive; `TickSequence` and `TurnIndex` are
preserved (ADR-0001 criterion 2).

**Id non-reuse — the guarantee, verified.** The `EntityIdSource` counter is serialized, so an
entity spawned after a load gets a fresh id that cannot collide with any pre-save entity, and an
unknown/dangling id resolves to nothing rather than to a stranger. **Subtle case worth recording**:
running an encounter advances the counter even though raiders are never serialized — and that is
*correct*, because non-reuse must hold across the encounter boundary too. The test asserts the
save gains no records while the counter legitimately advances.

**Derived state is rebuilt, and contributes zero bytes — proved, not assumed.** The occupancy
index and entity directory are rebuilt on load and verified against positions. The decisive test:
**wiping all derived state changes not a single byte of the save.**

**CD-9 is structural.** Spawning raiders and depositing an `EncounterOutcomeReport` adds **zero
records** to the save — "don't serialize battles" requires no code because there is nothing to
skip. Saving inside a battle is refused outright, and a save whose Mode is TurnBased is rejected
on load as corrupt.

**Catalog evolution.** A later build that inserts a material *before* the existing ones still
loads correctly: ids remap through the manifest, so saved walls stay the same rock instead of
silently re-materialising. This is the three-month-retrofit class ADR-0002 bought for bytes.

**Corruption fails loudly, never silently**: truncated file, foreign/garbage file (magic number),
future schema version, and a save naming a material this build does not know — all four throw
rather than misread.

**The strongest test — determinism after load.** A world that was saved and reloaded, then
advanced 200 mutations against a fixed input sequence, produces a **byte-identical result to a
control world that never left memory**. If anything load-bearing were missing from the save, the
two would diverge. They do not.

## Cost

| Scale | Save size | Gzipped | Write | Read |
|---|---|---|---|---|
| **MVP 128×128×16** | 2.01 MB | **0.03 MB (2%)** | 21.9 ms | 8.1 ms |
| Full-vision 256×256×32 | 16.02 MB | 0.24 MB (1%) | ~0.4–0.7 s | ~0.3 s |

**Two findings from these numbers:**

1. **Saves should be gzipped — the ratio is extraordinary.** 2.01 MB → **30 KB (2%)**, because a
   mountain is overwhelmingly repeated identical cells. Effectively free (fastest compression
   level), and it turns a 16 MB full-vision save into 240 KB. Recommend compressing from the
   first save format rather than retrofitting.
2. **MVP autosave is imperceptible: 21.9 ms, about 1.2 frames.** CD-9 fires autosave at the
   mode-switch into tactics and at battle end — both non-gameplay moments — so **no async or
   background-save machinery is warranted for MVP**. Full-vision write (~0.4–0.7 s) *would* be
   felt; revisit async saving at Tier 2 when world size grows, not before.

## What this spike did NOT answer

- **Seeded RNG stream serialization.** The pending Seeded RNG ADR owns stream layout; the save
  format has an obvious slot for it and the round-trip harness extends to cover it.
- **Save file versioning/migration across schema versions.** The spike *rejects* a mismatched
  schema loudly, which is the right MVP behaviour, but a real migration path (v1 → v2 upgraders)
  is a later concern.
- **Disk I/O, atomic replace, and corrupt-on-power-loss.** Everything here is in-memory byte
  arrays. Writing to disk safely (temp file + atomic rename, keep-last-N) is a Tier 1 task.
- **Full writer-interface segregation.** The spikes model ADR-0003's ownership with mode/window
  assertions and narrow methods rather than the complete interface set; criterion 1's
  *compile-time* half is asserted by design, not yet exercised by a build.

## Status

Concluded. **ADR-0003 criterion 3 passes**, ADR-0001 criterion 2 passes, and cross-cutting
contract #2 is validated end to end. Combined with the mode-switch and pathfinding spikes,
**ADR-0003 is now validated on criteria 2–5** (1 partially, 6 is a six-month review item) —
recommend promoting it to Accepted alongside ADR-0001.
