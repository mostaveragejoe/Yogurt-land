# ADR-0004: Battle Checkpoint Architecture

## Status
**Proposed**

## Date
2026-08-03

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 |
| **Domain** | Core (serialization, threading) |
| **Knowledge Risk** | HIGH for the pinned engine overall — mitigated: the whole checkpoint path is plain C# in `Hollowdeep.Core` with zero Godot references |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md`, `deprecated-apis.md` |
| **Post-Cutoff APIs Used** | None. The write path uses .NET 8 `System.IO`, `System.IO.Compression.GZipStream`, and `System.Threading` — not Godot `FileAccess` (whose 4.4 return-type change therefore does not apply) |
| **Verification Required** | Checkpoint writes at combat cadence on target hardware (the re-scoped ADR-0002 criterion 5); atomic-replace mechanism — `File.Move(temp, slot, overwrite: true)` on non-Windows (a single `rename(2)`, the genuine atomic primitive) and `File.Replace` on Windows — must leave a valid file after a mid-write kill, **and the temp file must live on the same filesystem/volume as the slot** (rename atomicity silently breaks across volumes; `File.Replace` is not atomic on POSIX — godot-specialist findings 2026-08-03 & architecture-review 2026-08-08) |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001 (Accepted, amended 2026-08-03 — snapshot contract and resume path delegated here); ADR-0002 (Proposed, amended — `TerrainSnapshot` reuse, buffer strategy); ADR-0003 (Accepted, amended — side-table serialization, occupancy rebuild); **Seeded RNG ADR (pending)** for item 3 of the content scope — this ADR reserves the slot, that ADR defines the stream format |
| **Enables** | Save/Load quick-spec (#6); all battle-checkpoint stories; GDD AC-67's determinism test |
| **Blocks** | Save/Load quick-spec (#6) — it must not be specced before this ADR and the Seeded RNG ADR exist |
| **Ordering Note** | The non-RNG scope is implementable before the Seeded RNG ADR lands; AC-67 (deterministic resume) needs both. Do not promote ADR-0002 until checkpoint cadence is measured on target hardware. |

## Context

### Problem Statement
The Battle Persistence ruling (user, 2026-08-02) requires one rolling, non-selectable checkpoint written after every resolved actor activation, so a quit or crash mid-battle resumes at the next activation. The 2026-08-03 amendments to ADR-0001/0002/0003 retract the falsified CD-9 claims and delegate the checkpoint contract here. Two implement-it-wrong-by-default traps are on record: (1) a load path that routes through `RequestSwitch` re-fires the switch-in autosave and re-runs placement normalization; (2) a restore in `Mode == TurnBased` writes RealTime-only field groups and trips ADR-0003's mode assertions. This ADR owns the content scope, the cadence, the write mechanism, the resume path, and the invariants.

### Constraints
- **Zero steady-state allocation** in the simulation path (measured standard, technical-preferences) — a 2 MB LOH allocation per activation is a regression
- 16.6 ms frame budget; the ~21.9 ms write (spike-measured, ~1.2 frames) cannot sit on the sim thread — the **background write** overlaps the next activation's presentation; the snapshot itself (~0.6 ms) needs no animation cover
- Exactly one encounter at a time (ADR-0001); the checkpoint slot can therefore be singular
- Fail-loud posture for corrupt saves (save/load spike); no-rewind design intent (GDD EC-8)
- Side tables are serialized **only into the battle checkpoint by their owning systems**, never into colony saves (ADR-0003 amendment wording constraint)
- Terrain mutation replay is architecturally unavailable (ADR-0002 rule 9: the bus has no replay, terrain keeps no journal) — the checkpoint must carry terrain state
- Plain C# core, headlessly testable, zero Godot references

### Requirements
- Exactly one checkpoint write per resolved activation, post-resolution, silent, tagged `Mode == TurnBased` (GDD AC-66)
- Resume lands at the start of the activation after the last resolved one, deterministic against an unquit control run (GDD AC-67, with the Seeded RNG ADR)
- The checkpoint writer is the only legal author of a combat-mode save (GDD AC-68)
- The sim thread never blocks on disk; the player never sees a save-slot prompt

## Decision

### 1. Content scope — the checkpoint carries all of this, and nothing else

| # | Content | Serialized by | Notes |
|---|---------|--------------|-------|
| 1 | Encounter side tables, by owner: Turn Order (initiative/order), Action Economy (AP), Targeting (locks, overwatch) | Their owning combat systems | Into the checkpoint only — never colony saves (ADR-0003 amendment, owner-scoped wording mirrored per its binding constraint) |
| 2 | `TurnBasedAuthority` state | Time Authority | State-machine position and current/next actor, beyond `{Mode, TurnIndex, TickSequence}` — the ADR-0001 contract extension |
| 3 | Combat RNG streams | Seeded RNG owner | Resumable at arbitrary draw counts — format owned by the Seeded RNG ADR |
| 4 | Encounter framing | Time Authority | `EncounterId`, `BreachCells`, `ParticipantIds` — promoted from transient to serialized |
| 5 | `RaiderStore` in its entirety | Entity stores | "Adds zero records" is a colony-save-only property now |
| 6 | Un-reaped `IsDead` colonists and `IsBroken` doors | Entity stores | Legal serialized states; **the load path never reaps** — reaping stays reconcile's job |
| 7 | Terrain — full grid, same schema and material manifest as colony saves | TerrainWorld | Replay is unavailable. **Obligation on ADR-0002/0003**: the checkpoint path needs a snapshot-into-caller-buffer overload (`SnapshotInto(IBufferWriter<byte>)` or equivalent); the returning `Snapshot()` allocates and remains the colony-save path only — calling it per activation re-creates the 2 MB/activation LOH regression |
| 8 | — | — | **Derived state is never checkpointed**: `Restore` → `WorldReloaded` → full subscriber rebuild recovers occupancy, pathfinding caches, stale-job flags, render data |

The checkpoint is a **full self-contained save** — same schema family as a colony save, plus the combat scope above, tagged `Mode == TurnBased`. It validates on load by itself, with no reference to any other file.

### 2. Cadence and ordering invariants

- **The snapshot beat is pinned: the `AwaitingPresentation → NextActor` transition.** Not at the end of `ResolvingAction` — there the gate is busy and the state is arguably mid-activation (AC-66 forbids that). At the `NextActor` boundary the gate is provably idle, presentation state cannot exist in the snapshot, and the serialized state IS the resume point ("start of the activation following the last resolved one") with no reconstruction. The background write then overlaps the next activation freely.
- **"Activation 0": the first checkpoint is written immediately post-swap, before the first activation.** Without it, the newest save from switch-acceptance until the first resolved activation (squad placement, first input — tens of real seconds) is the RealTime switch-in autosave, and quitting there lands pre-battle — the exact re-roll window Battle Persistence closes. A combat-mode save therefore exists for the encounter's whole lifetime. *(Routed GDD correction: Rule 9(b) gains this sentence — see Related Decisions.)*
- **No checkpoint between battle-end declaration and the reconcile drain.** The `EncounterOutcomeInbox` is provably empty at every checkpoint and needs no serialization; a restored world with an undrained report and no live encounter is unrepresentable.
- **Slot retirement quiesces the writer FIRST.** At battle-end declaration: cancel any pending (not-yet-taken) buffer, join the in-flight write, and only then proceed to reconcile → battle-end autosave → clear the checkpoint slot. Without the quiesce, a write snapshotted before battle-end can land *after* the battle-end save commits and resurrect a retired slot as "newest."
- **Save ordering is decided by an in-file monotonic stamp (`TickSequence` + a save ordinal), never by filesystem mtime.** mtime is fragile against clock changes and Steam Cloud restores, and is what would let a resurrected checkpoint outrank the battle-end save.

### 3. Write mechanism — Option A (user decision 2026-08-03)

Snapshot on the sim thread; compress and write on a background thread.

```
sim thread (post-resolution beat)          background writer thread
─────────────────────────────────          ────────────────────────
snapshot world → free pooled buffer   ┐
mark buffer "newest pending"          ├──► take newest pending buffer
return to turn loop (~0.6 ms + KBs)   ┘    gzip (~30 KB at MVP)
                                           write temp file → fsync
                                           atomic replace checkpoint slot
                                           release buffer to pool
```

- **Double-buffered pooled buffers, allocated at the composition root** — not lazily at first battle (two 2 MB LOH arrays allocated mid-gameplay is an avoidable fragmentation event). Release policy after a battle is Save/Load #6's call. Zero per-checkpoint allocation on the sim thread — via the `SnapshotInto` obligation of §1 item 7; the 2 MB buffers are pooled, never per-activation LOH garbage.
- **Backpressure = coalesce-newest (user decision 2026-08-03).** A buffer is always in exactly one of three states: *free*, *pending* (newest snapshot, not yet taken), or *in-flight* (being written). If the writer is busy when an activation resolves, the sim snapshots into the free buffer and marks it pending; a newer snapshot overwrites a not-yet-taken pending buffer. The writer always takes the newest pending snapshot, and **releases its finished buffer before claiming the pending one** — otherwise there is an instant with no free buffer and the sim would block. The sim never waits; at most one write is in flight; intermediate checkpoints may never reach disk; the disk always converges to the newest resolved state.
- **Lag bound, stated honestly in activations**: a crash can lose every activation coalesced since the last completed write — bounded by write duration, not by count; under sustained coalescing it is unbounded in principle. This is why the quit path joins the writer (§Risks): crash lag is unavoidable, quit lag would be a design regression.
- **The writer thread reuses its compression and output buffers for the encounter's lifetime** — 150–300 gzip passes per battle must not produce process-wide GC pressure (a collection pauses the sim thread too).
- **Atomic replace**: gzip to a temp file in the same directory, flush, then atomically replace the slot. Use **`File.Move(temp, slot, overwrite: true)` as the cross-platform default** — on Unix it lowers to a single `rename(2)`, the true atomic primitive. **`File.Replace` is NOT atomic on POSIX** (its .NET Unix implementation performs a backup-file step, not a rename), so it is reserved for Windows where its backup semantics are wanted (godot-specialist finding, architecture-review 2026-08-08). A kill at any instant leaves either the old checkpoint or the new one — never a partial file.
- Buffer handoff is the only cross-thread state; it is guarded by a lock or interlocked exchange and is testable headlessly.
- **Save-directory resolution stays at the composition root**: `Hollowdeep.Core` cannot call `ProjectSettings.GlobalizePath("user://…")` (zero Godot references). The Godot-side composition root resolves the platform save path once and hands the core writer a plain string — the core never re-derives it via `Environment.GetFolderPath` (editor-vs-export paths would diverge).

### 4. Resume path — `RestoredFromCheckpoint`

- Loading a checkpoint **never routes through `RequestSwitch`**. `Restore` reconstructs `TurnBasedAuthority` directly and fires `ModeTransitioned` with reason `RestoredFromCheckpoint`. ADR-0001 Rule 2 ("only the game switches modes") and the `RequestSwitch` call-site grep gate survive intact; the switch-in autosave does not re-fire; ADR-0003's placement normalization does not re-run.
- **The load window carries an explicit exemption, scoped as a distinct sanctioned writer**: the restore path is store-internal `Restore`, invoked only inside the load window. A restore writes *every* field group across all stores — so it crosses both ADR-0003's mode assertions (a `Mode == TurnBased` restore writes RealTime-only groups like Needs and job state) and its writer-interface segregation. Naming it a sanctioned writer keeps the exemption narrow: the load window does not license bypassing writer interfaces generally.
- **Occupancy rebuild filters dead units** — otherwise the TurnBased exclusivity assertion fires on a corpse cell. The load path never reaps (invariant, content item 6).
- Resume position: the start of the activation after the last resolved one. The restored state machine begins at the activation boundary — no partial activation is ever re-entered.

### 5. Load semantics and failure

- **Newest-valid-save load** (distinct from §3's coalesce-newest backpressure — two mechanisms, two names): "Continue" always loads the newest valid save across all slots, ordered by the in-file monotonic stamp (§2) — during a battle that is the checkpoint; after it, the battle-end autosave.
- Validation on load: magic, schema version, and integrity check (CRC), per the save/load spike posture. Unknown material keys and future schemas fail loudly (ADR-0002).
- **Provenance is enforced, not assumed (AC-68's mechanism)**: the checkpoint lives in a dedicated non-selectable slot and carries a **writer-id field in the header**. Enforcement is primarily **write-side** — the save API refuses a `Mode == TurnBased` tag from any writer other than the battle-checkpoint writer — with the load-side header check as the backstop. Without this, a checkpoint is byte-indistinguishable from any other TurnBased-tagged save and AC-68/AC-54a are untestable.
- **Corrupt checkpoint → loud fallback (user decision 2026-08-03).** If the newest checkpoint fails validation, the game shows an explicit error and offers the next-newest valid save — normally the battle-start autosave; the battle restarts from its start. Genuine corruption never bricks the colony. **Obligation on Save/Load #6**: the autosave rotation must retain the switch-in autosave for the battle's whole duration, or this fallback silently degrades to "whatever colony autosave is left." **Threat-model note**: deliberate file deletion or editing can reach the pre-battle state; the anti-save-scum design closes UI paths, not disk tampering — same posture as every other file in the save directory. *(This carve-out joins the manual-save/pre-raid-reload hole as a second input to the GDD correction routed to creative-director — the GDD's "no longer reachable by any player action" absolutism needs one honest restatement, not two scattered footnotes.)*

## Alternatives Considered

### Alternative B: Delta checkpoint against the switch-in autosave
- **Description**: Store only what changed since the battle started; load = base + delta.
- **Pros**: Smallest writes; terrain mostly unchanged in many battles.
- **Cons**: Checkpoints are not self-contained; a lost or stale base orphans every delta; a second serialization path to maintain; validation requires two files to agree.
- **Rejection Reason**: Orphaned-from-base corruption modes and a second format, against ~30 KB gzipped full saves that are already cheap (user decision 2026-08-03).

### Alternative C: Hybrid — full entities, dirty-chunk terrain
- **Description**: Full entity/side-table state each checkpoint; terrain as dirty chunks since switch-in.
- **Pros**: Cuts the dominant (terrain) cost; single base file only for terrain.
- **Cons**: Still a second terrain path; still base-coupled; complexity lands in the least-testable place (load).
- **Rejection Reason**: Middle ground that keeps B's coupling problems (user decision 2026-08-03).

### Rejected backpressure options
- **Block the sim** until the write completes — a 20–30 ms stall inside the combat frame budget; violates the frame-time posture.
- **Skip when busy** — the disk checkpoint silently falls several activations behind; the resume guarantee erodes.

### Rejected failure-handling options
- **Refuse to load** on a corrupt checkpoint — protects no-rewind absolutely but makes the whole save unrecoverable; punishes disk failure as if it were cheating.
- **Silent fallback** — hides corruption; violates fail-loud.

## Consequences

### Positive
- Quit and crash both resume mid-battle; resolved outcomes cannot be avoided from inside the game (GDD Pillar and EC-8)
- One save format; the checkpoint self-validates; no cross-file coupling
- The sim thread's checkpoint cost is the ~0.6 ms snapshot, inside the post-resolution beat
- The switch-in and battle-end autosaves keep their existing one-shot-allocation path — nothing changes for colony saves

### Negative
- Async save machinery (writer thread, buffer pool, atomic replace) enters MVP scope for Save/Load #6
- ~4 MB of pooled buffer memory resident during battles (2 × 2 MB at MVP; grows with map size)
- ADR-0002's "no buffer-reuse machinery" stance is retired for the checkpoint path (already amended)

### Risks
- **Writer starvation under pathological activation rates** — mitigated by coalescing: at most one write in flight, disk converges to newest; verified by a burst-cadence test
- **Cross-thread buffer handoff bug** (torn snapshot) — mitigated: single handoff point, lock/interlocked discipline, headless stress test
- **Atomic-replace primitive differs per OS** — resolved (godot-specialist, architecture-review 2026-08-08): use `File.Move(temp, slot, overwrite: true)` as the cross-platform default (a direct `rename(2)` on same-volume Unix paths, genuinely atomic); reserve `File.Replace` for Windows. `File.Replace` is NOT atomic on POSIX (backup-file step), so it must not be the default. The temp file stays on the slot's volume; the fallback (write-temp + delete + rename) remains loud-fail safe
- **Corruption fallback reaches the pre-battle state** — accepted and bounded: loud, explicit, and only via disk failure or tampering (outside the design's threat model)
- **RNG streams not yet specified** — bounded: content slot reserved; Seeded RNG ADR is blocking for Save/Load #6
- **Editor-only: C# hot reload can tear down the assembly context mid-write** — a background write in flight when the editor reloads assemblies references a dying `AssemblyLoadContext`. Dev-iteration hazard only (exported builds have no hot reload); mitigation lands in Save/Load #6's composition-root wiring: quiesce/join the writer before reload, or recreate the pool cleanly on reload
- **Player-initiated quit MUST flush and join the writer** — atomic replace guarantees the slot is *valid*, not *current*. Quitting with an unjoined writer can lose the newest resolved activation: a small non-deterministic scum window on an outcome the player just watched, which is the exact regression Battle Persistence exists to close (GDD AC-45). The quit-confirmation dialog already provides the wall-clock cover for the join. "Atomic replace alone" is the guarantee for the crash/kill case only, where lag is unavoidable and accepted. The `NOTIFICATION_WM_CLOSE_REQUEST` hook that routes window-close through this join is therefore part of the requirement, not a nice-to-have
- **Steam Cloud sync churn** — 150–300 same-file replacements per battle generate that many change events for the cloud-sync watcher, and possibly conflict prompts under multi-machine play. The rolling checkpoint slot should be excluded from Steam Cloud (or debounced) — routed to whoever configures cloud-file inclusion at release setup
- **Forward-looking only**: an AOT-constrained Tier 3 platform (consoles, iOS) would need this threading/compression stack re-verified; PC mono exports (the lead SKU) run CoreCLR with none of those constraints

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| time-authority-mode-switch.md | Rule 9b — rolling per-activation checkpoint, sole legal combat-mode writer | Content scope §1, cadence §2, mechanism §3 |
| time-authority-mode-switch.md | EC-8 — quit suspends; relaunch resumes at next activation | Resume path §4, latest-wins §5 |
| time-authority-mode-switch.md | AC-66 — exactly one silent post-resolution write per resolved activation | Cadence §2, mechanism §3. **Routed GDD correction (do not bury this)**: under coalesce-newest, the observable is one *snapshot* per resolved activation with the on-disk slot converging to the newest resolved state — AC-66 must be re-authored to say so, since a BLOCKING category-(a) AC cannot be quietly reinterpreted by an ADR |
| time-authority-mode-switch.md | AC-67 — deterministic resume vs unquit control | §1 items 2–4, §4; RNG half blocked on the Seeded RNG ADR |
| time-authority-mode-switch.md | AC-68 — non-checkpoint TurnBased-tagged saves are corrupt | §5 validation; ADR-0001 amendment item 4 |
| time-authority-mode-switch.md | EC-6, AC-44/45 — manual saves disabled in combat; RealTime-tagged switch-in autosave | Unchanged behavior, restated as boundary conditions of §5 |

## Performance Implications
- **CPU (sim thread)**: ~0.6 ms snapshot per resolved activation (spike-measured terrain snapshot + kilobytes of entity data), inside the post-resolution beat; zero steady-state allocation
- **CPU (background)**: ~20–30 ms gzip + write per checkpoint at MVP; off the frame path entirely
- **Memory**: +~4 MB pooled buffers resident during battles; released or retained per composition-root policy (Save/Load #6 detail)
- **Load Time**: unchanged for colony saves; checkpoint load = colony load + combat scope (small)
- **Network**: n/a

## Migration Plan
Nothing ships yet — the save/load spike validated the colony path only. Save/Load #6 implements this ADR; the ADR-0002 promotion re-run on target hardware must measure checkpoint writes at combat cadence (re-scoped criterion 5). No existing data migrates.

## Validation Criteria
1. AC-66 harness test: burst of resolved activations → exactly one snapshot each, at the `AwaitingPresentation → NextActor` beat, disk converges to newest, no mid-activation write, no UI event
2. AC-67 harness test (with Seeded RNG ADR): resume from checkpoint reproduces an unquit control run bit-for-bit
3. AC-68 test: the save API refuses a `Mode == TurnBased` tag from any non-checkpoint writer; a forged TurnBased-tagged file without the checkpoint writer-id is rejected on load
4. `TickSequence` continuity across checkpoint restore (cross-cutting contract #2's blocking CI gate) — testable now, without the Seeded RNG ADR
5. Load-window exemption test: checkpoint restore writes all field groups via the sanctioned restore writer without tripping assertions — and the assertions still fire outside the load window
6. Occupancy-rebuild test: a checkpoint with `IsDead` units loads with corpses filtered from the index and nobody reaped
7. Ordering + quiesce test: no snapshot between battle-end declaration and reconcile drain; a write in flight at battle-end is joined before the battle-end autosave, and the retired slot never resurfaces as newest
8. Quit-join test: player-initiated quit flushes the newest resolved activation to disk before process exit
9. Kill test: process kill at arbitrary points during a write always leaves a loadable slot (old or new)
10. Corrupt-file test: a truncated/bit-flipped checkpoint produces the loud fallback offer, never a silent load
11. Frame profile: checkpoint cost never appears in the combat frame-time profile on target hardware — **this is the same measurement as ADR-0002's re-scoped criterion 5**; one shared target-hardware run gates both promotions, and ADR-0004 cannot reach Accepted before it

## Related Decisions
- ADR-0001/0002/0003 — Amendments 2026-08-03 (Battle Persistence); this ADR discharges the obligations they delegate
- `docs/architecture/change-impact-2026-08-03-time-authority-mode-switch.md` — decision record this ADR implements (§3, §4)
- Seeded RNG ADR (pending) — owns stream format; blocking for Save/Load #6
- Cross-cutting contract #2 (serialization) — mode-tagging rule as amended
- Open design hole routed to creative-director: colony manual saves still allow pre-raid reload; decide before Save/Load #6

### Routed follow-up corrections (file these; `/propagate-design-change` re-run verifies coverage)
1. **GDD AC-66 re-authoring** — "exactly one checkpoint *snapshot* per resolved activation; the on-disk slot converges to the newest resolved state" (TD finding B4)
2. **GDD Rule 9(b) addition** — the "activation 0" first checkpoint, written post-swap before the first activation (TD finding B7)
3. **GDD "no pre-battle rewind" restatement** — one honest carve-out covering both the corruption fallback and the manual-save/pre-raid-reload hole already routed to creative-director (TD finding A6)
4. **ADR-0002/0003 API obligation** — the `SnapshotInto` caller-buffer overload for the checkpoint path (TD finding B6)
5. **Save/Load #6 obligation** — autosave rotation retains the switch-in autosave for the battle's duration (TD finding A5)
