# Architecture Review Report

**Date**: 2026-08-07
**Mode**: `/architecture-review` (full)
**Engine**: Godot 4.7.1 (C# / .NET 8+), pinned 2026-07-24
**GDDs reviewed**: 4 (`game-concept.md`, `systems-index.md`, `terrain-data-model.md`, `time-authority-mode-switch.md`) + 2 review logs + `design/art/art-bible.md`
**ADRs reviewed**: 4 (ADR-0001 … ADR-0004) + `cross-cutting-contracts.md` + `change-impact-2026-08-03-time-authority-mode-switch.md`
**Registries checked**: `docs/registry/architecture.yaml`, `docs/architecture/tr-registry.yaml`, `design/registry/entities.yaml`
**Specialist consulted**: godot-specialist (Phase 5 second opinion — findings folded into §6)

---

## Verdict: **FAIL**

The four ADRs that exist are unusually strong documents — internally rigorous, spike-validated, honest about their own gaps, and structurally complete (all four carry every section `docs/CLAUDE.md` requires, including Engine Compatibility). **The failure is not in their content.** Nothing found in this review challenges a decision made in ADR-0001–0004.

The failure is that **the premise of this review is false in three checkable ways**, one of which is a genuine Foundation-layer blocker:

1. **ADR-0005 (Seeded RNG / Determinism) does not exist.** Not in the working tree, not in any commit, not on any branch. Four ADRs, the contracts annex, the systems index and `technical-preferences.md` all reference it as *pending*. It is a Foundation-layer MVP system (`systems-index` #4, status **Not Started**).
2. **`docs/registry/architecture.yaml` is an untouched template.** Every section is `[]`; `last_updated: ""`. No stance from any of the four ADRs has ever been registered.
3. **`docs/architecture/tr-registry.yaml` is also an untouched template** (`requirements: []`). No TR-ID has ever been issued — this is the project's first traceability run, not a re-verification.

The review is also being run **one ADR earlier than the project's own trigger**: `change-impact-2026-08-03-…md` §7 follow-up 5 reads *"Run `/architecture-review` after ADR-0004 **and the Seeded RNG ADR** land."* Only ADR-0004 landed.

FAIL is correct per this skill's rubric (*"critical gaps — Foundation/Core layer requirements uncovered"*), but read it as **"one missing Foundation ADR plus accumulated bookkeeping debt"**, not as a judgement on the architecture.

*(Note: `game-concept.md`'s Technical Risks preamble carries a standing instruction addressed to this review — the TD feasibility assessment was run against a free-form-voxel assumption before the world model was clarified as a Gnomoria-style layered tile grid, and says *"Revisit formally at `/architecture-review`."* Discharged: ADR-0002's layered floor+wall cell struct is built on the corrected assumption, and the voxel-era risk framing no longer applies.)*

---

## 1. Status of the claimed inputs

| Claimed in the request | Actual repository state |
|---|---|
| ADR-0001 Accepted, **amended 2026-08-03 and 2026-08-07** | Accepted 2026-07-26, amended **2026-08-03 only**. No 2026-08-07 amendment exists. |
| ADR-0002 Proposed, spike-validated; target-hardware clause the only gate left | Proposed, spike-validated ✅ — but **four further gates are tracked outside the ADR** (§4.6). |
| ADR-0003 Accepted, amended 2026-08-03 | ✅ Accurate. |
| ADR-0004 Proposed 2026-08-03 | ✅ Accurate. |
| ADR-0005 Proposed 2026-08-07, **companion edits + registry update applied same day** | ❌ **Does not exist.** No companion edits. No registry update. |
| ADR-0005's reload-seed pick deferred to Raid Trigger #18's GDD | Partly — and #18 carries **no RNG requirement at all** today (§4.7). |
| ADR-0002/0004 share a target-hardware promotion gate | ✅ The two ADRs agree. The **Terrain GDD does not** (§4.6). |

---

## 2. Traceability

### 2.1 Registry state — the blocking bookkeeping finding

`docs/registry/architecture.yaml` is read by `/architecture-decision` (conflict baseline before authoring), `/create-stories` (to embed constraints into stories) and `/dev-story` (to check implementation against accepted stances). It is **completely empty**, while the four ADRs have produced exactly the material it exists to hold:

| Registry section | Entries | Should hold |
|---|---|---|
| `state_ownership` | **0** | ADR-0003's entire write-ownership table (colonist health/position/needs/job/squad/identity; raider; item; door field groups); ADR-0002's terrain writer-set-per-authority table; `{Mode, TurnIndex, TickSequence}` |
| `interfaces` | **0** | `ITickable.Tick`; `ITerrainChangeSink.Publish`/`PublishWorldReloaded`; `EncounterOutcomeReport` via one-slot inbox; Revision-polling (explicitly *instead of* an event bus) |
| `performance_budgets` | **0** | 60 fps / 16.6 ms; terrain ≤150 draw calls; provisional 500-call whole-frame ceiling; every measured per-system cost |
| `api_decisions` | **0** | Two stacked GridMaps @ `cell_octant_size = 32` (and *not* authoritative); Forward+; .NET `System.IO`/`GZipStream` **not** Godot `FileAccess` |
| `forbidden_patterns` | **0** | The 15+ patterns already enumerated in `technical-preferences.md` |

The budgets and forbidden patterns *do* exist — in `.claude/docs/technical-preferences.md`. But that is prose for humans; the registry is the machine-checkable baseline downstream skills consult. Today `/create-stories` and `/dev-story` would find nothing.

### 2.2 TR registry — 203 requirements extracted, zero IDs issued

`tr-registry.yaml` contains only its worked examples (which reference `design/gdd/combat-system.md`, a file that does not exist). Extraction across the four design documents yields **203 technical requirements**:

| Source | Count | Proposed slug |
|---|---|---|
| `game-concept.md` | 21 | `TR-concept-NNN` |
| `systems-index.md` (cross-cutting contracts, CD notes, index-level facts) | 44 | `TR-xcut-NNN` |
| `terrain-data-model.md` | 68 | `TR-terrain-NNN` |
| `time-authority-mode-switch.md` | 70 | `TR-timeauth-NNN` |

**A decision is needed before these are written.** The slug scheme above is per *document*, not per *system*, because three of the four sources are not single-system GDDs and most of the systems they constrain have no GDD yet. The alternative — per-system slugs (`TR-rng-001`, `TR-saveload-001`) — would pre-claim slugs for systems whose GDDs do not exist. **TR-IDs are permanent by rule and are embedded in every future story file, so this review has deliberately not written them.** Ratify the scheme, then run `/architecture-review` again (or write the registry directly) to issue them. The `TR-xcut` block is the one to split if per-system slugs are preferred.

A full RTM (requirement → ADR → story → test) is not producible: `production/epics/` does not exist and no story files exist.

### 2.3 Coverage by system

**Only 3 of the 35 index entries name an ADR** — #1 (ADR-002), #2 (ADR-001), #9 (ADR-003). Systems #3, #4, #6, #8 and #29 are routed "ADR-only" or "spike + ADR" **without naming one**, and **ADR-0004 is referenced from no enumeration-table row at all** — including #6 Save/Load, the system it most obviously governs.

| # | System | Layer | Governing ADR | Status |
|---|---|---|---|---|
| 1 | Terrain Data Model | Foundation | ADR-0002 | ✅ Covered (GDD Approved) |
| 2 | Time Authority / Mode-Switch | Foundation | ADR-0001 | ✅ Covered (GDD Designed, pending re-review) |
| 3 | World Change Event Bus | Foundation | ADR-0002 rule 1 + annex contract #3 | ⚠️ Covered in substance; index still lists it as an un-started standalone "ADR-only" system |
| 4 | **Seeded RNG / Determinism** | **Foundation** | **— none** | ❌ **GAP — blocking** |
| 6 | Save/Load & World Serialization | Cross-cutting | ADR-0004 (partial) | ⚠️ Blocked on **both** ADR-0004 (Proposed) and the missing Seeded RNG ADR |
| 8, 12 | Pathfinding; Spatial Query / LOS | Core | ADR-0002 + ADR-0003 | ✅ Covered (composite-walkability rule fixed, spike-validated) |
| 9 | Colonist Entity & Attributes | Core | ADR-0003 | ✅ Covered |
| 7 | Terrain Rendering & Cutaway | Core | ADR-0002 | ⚠️ Partial — the third-GridMap conflict (§4.1) and four unmet engine gates (§6) |
| 18 | Raid / Threat Trigger | Feature | ADR-0001 (switch only) | ⚠️ Partial — **no RNG requirement anywhere** despite CD-2 requiring breach variety (§4.7) |
| 19–23 | Combat set | Feature | ADR-0001 + 0003 + 0004 | ⚠️ Partial — side-table checkpoint serialization specified; RNG streams not |
| 24 | Squad Preparation | Feature | ADR-0001 + ADR-0003 | ✅ Covered (decide/execute split + nudge rule fixed) |
| 29 | Dev Tools / Debug Console | Tier 0 | ADR-only / lightweight | ⚠️ **Built** (`src/tools/DebugConsole/`) but index says "Not Started" |
| remainder | 5, 10, 11, 13–17, 25–28, 30–35 | — | Downstream | Not yet designed — expected at this stage, not gaps |

**Scope caveat on the ADR-coverage count.** `.claude/docs/coding-standards.md` says *"Every system must have a corresponding architecture decision record."* The systems index's own routing policy sanctions quick-spec and UX-spec tiers that name no ADR and instructs reviewers to treat them as compliant. These are not literally contradictory (ADR-only satisfies both), but they give different answers to *"is a quick-spec system without an ADR a gap?"* This review treats the index's routing policy as authoritative — but the two documents should be reconciled, because the answer changes the gap count by roughly twenty systems.

**Blast radius of the single Foundation gap (#4):** ADR-0004 content-scope item 3 is a reserved-but-unfilled slot; ADR-0004 validation criterion 2 cannot run; Time Authority GDD **AC-67** is tagged `BLOCKED: Save/Load #6 + Seeded RNG ADR`; AC-54b sits on the same chain; Save/Load #6 cannot be specced. Of the Time Authority GDD's 68 acceptance criteria, 16 are tagged BLOCKED, and the deepest terminate here.

**Also worth recording**: the hardest requirement the missing ADR must satisfy — *"streams must resume at arbitrary draw counts"*, which constrains the algorithm to explicit-serializable-state generators (PCG/xoshiro class) and rules out hidden or platform-dependent internal state — appears **only in `CLAUDE.md` and the change-impact report, in no GDD**. `game-concept.md` contains zero mentions of RNG, seeds, determinism or reproducibility.

---

## 3. ADR dependency order

```
Foundation (no dependencies)
  1. ADR-0001  Time Authority / Mode-Switch          [Accepted]

Depends on Foundation
  2. ADR-0002  Terrain Data Model                    [Proposed]  requires ADR-0001
  3. ADR-0003  Entity Data Ownership                 [Accepted]  requires ADR-0001, ADR-0002
  4. ADR-0005  Seeded RNG / Determinism              [DOES NOT EXIST]  constrained by ADR-0001

Feature layer
  5. ADR-0004  Battle Checkpoint Architecture        [Proposed]  requires ADR-0001, 0002, 0003, Seeded RNG
```

No dependency cycle exists. Three ordering flags:

- 🔴 **ADR-0004 (Proposed) depends on an ADR that does not exist.** Its Ordering Note concedes the non-RNG scope is implementable first, but AC-67 needs both. This is the blocking edge.
- 🔴 **ADR-0003 is Accepted while ADR-0002, which it depends on, is still Proposed.** Deliberate — both were promoted at the same spike gate and ADR-0002's remaining gate is a measurement, not a contract question — but per `docs/CLAUDE.md` stories may reference ADR-0003 today while its substrate is not Accepted. Worth an explicit note when stories are cut.
- ⚠️ **ADR-0002 and ADR-0004 are jointly gated on one target-hardware run** (ADR-0002 criterion 5 as re-scoped 2026-08-03 ≡ ADR-0004 criterion 11). Both ADRs state this identically and correctly.

---

## 4. Cross-ADR conflicts and inconsistencies

### 4.1 🔴 Render backend: two GridMaps or three?

**Type**: Integration contract / performance budget

- **ADR-0002** (Spike Results, 2026-07-26) and **`technical-preferences.md`** both record the backend as **two** stacked GridMaps (wall + floor) at octant 32, **32 draw calls**, video memory 14.25 → 16.42 MB.
- **`terrain-data-model.md`** (status **Approved**) mandates a **third** map — a damage overlay — repeatedly: Known Gap #1 refers to *"three stacked GridMaps"*; the performance table states the measured draw-call curve *"covers the wall+floor maps only — the mandated damage-overlay third map was **not** present during measurement"*; Known Gap #7 calls its draw-call cost unmeasured. The terrain review log calls it *"the single largest unquantified draw-call risk."*

The GDD's reasoning is sound: GridMap holds one item id per cell and offers no per-instance shader channel, so per-cell damage tint needs its own map rather than a tier × style × damage item explosion — and damage is visualised at exactly three discrete levels, a Pillar 3 legibility floor.

**Impact**: The ≤150 terrain draw-call budget was measured at **144 calls for 8 style variants on two maps** — six calls of headroom. A third map inheriting the same per-octant-per-style-combo scaling could breach it outright, and the GDD already warns the style ceiling *"may drop below 8 once the overlay is measured."* ADR-0002 — the governing contract — does not know the third map exists.

**Resolution**: amend ADR-0002's Spike Results to record the damage overlay as mandated-but-unmeasured, and add its draw-call measurement to criterion 5.

### 4.2 🔴 `SnapshotInto` — ADR-0004 requires an API its dependencies do not expose

**Type**: Integration contract

ADR-0004 §1 item 7 places an explicit **obligation on ADR-0002/0003**: the checkpoint path needs a snapshot-into-caller-buffer overload (`SnapshotInto(IBufferWriter<byte>)` or equivalent), because the returning `Snapshot()` allocates and *"calling it per activation re-creates the 2 MB/activation LOH regression."* ADR-0004 §3 then rests on it: *"Zero per-checkpoint allocation on the sim thread — via the `SnapshotInto` obligation of §1 item 7."*

ADR-0004's own routed-corrections list names this as follow-up 4. **It was never filed.** `SnapshotInto` appears only in ADR-0004, in `technical-preferences.md`'s summary of ADR-0004, and in session state. ADR-0002's `TerrainWorld` facade still exposes only `TerrainSnapshot Snapshot();`, and ADR-0003's stores likewise.

**Impact**: ADR-0004's zero-allocation guarantee currently rests on an API its dependency contracts do not provide. An implementer reading ADR-0002 as the terrain contract would build the allocating path — and the checkpoint carries the **full terrain grid**, 2 MB per activation at MVP bounds.

### 4.3 🔴 ADR-0003's diagram and body contradict its own amendment

**Type**: State management / serialization

ADR-0003 is meticulous about marking retracted claims inline (`*[amended 2026-08-03]*` appears six times). Three places were missed:

| Line | Text | Problem |
|---|---|---|
| 242 (architecture diagram) | `Combat side tables … NEVER in stores, NEVER serialized (CD-9)` | "NEVER serialized" is now **false** — side tables are checkpoint-serialized by their owners. The diagram is the most-copied part of an ADR. |
| 194 | inbox *"never serialized (an encounter cannot span a save under CD-9)"* | Conclusion holds; the **stated reason is falsified** — an encounter can now span a save. The real reason is ADR-0004's ordering invariant. |
| 303 | *"the report is transient by construction (CD-9)"* | Same class. |

### 4.4 🔴 The `cell_octant_size == ChunkSize` "locked invariant" is true only in X/Y

**Type**: Architecture pattern / engine behaviour — *raised by the godot-specialist; this is the sharpest technical finding in the review*

Three documents assert a 1:1 chunk↔octant mapping. `technical-preferences.md`: *"`cell_octant_size` MUST equal `TerrainWorld.ChunkSize` (both 32) — a locked invariant, so a dirtied chunk maps 1:1 to a dirtied octant."* `SPIKE-NOTE.md` states it even more strongly (*"a LOCKED INVARIANT — not a preference"*). The Terrain GDD repeats it and **cites ADR-0002 as the source** — but **ADR-0002 contains no such rule**; it still calls chunk size *"a queryable tunable… never a caller-side constant"* and says `ChunkSize`/`ChunkOf` remain the sanctioned mapping *"regardless."*

Worse, the invariant is **geometrically false on the Z axis**. `GridMap.cell_octant_size` is a single scalar applied **cubically** — octants group uniformly across X, Y *and* Z. `TerrainWorld`'s chunking is explicitly **anisotropic**: per-layer tiles of **32×32×1**, so every `ChunkCoord.Z` is a separate chunk one layer deep. With MVP depth 16 and full-vision depth 32, and octant size 32, **every Z-layer in a given X/Y footprint falls inside the same single GridMap octant**. The real relationship is **N chunks : 1 octant**, where N is the number of populated Z-layers sharing that footprint. Dirtying chunk `(x, y, 3)` and chunk `(x, y, 12)` dirties the *identical* octant.

**Whether this costs anything depends on a question the spike explicitly did not answer.** The measured 1.85–2 µs per-dig figures were taken against a **3-layer cutaway**, and `SPIKE-NOTE.md` lists *"cutaway Z-level transitions beyond the full-layer extraction cost"* under what it did not measure. If the (unwritten) Terrain Rendering & Cutaway spec only ever populates the visible 3-layer window into GridMap, the numbers hold because the octant stays sparse. If it populates all layers and hides the rest via clipping, one dig forces a rebuild spanning up to 16–32 layers of geometry in a single octant — materially different from what was measured, and a hard constraint on how cutaway scrolling must be implemented.

**Severity: MEDIUM.** Not a data-model defect — ADR-0002's authoritative contract is untouched either way — and GridMap-over-MultiMesh is still very likely correct given the margins (32 vs 82 draw calls; ~2 µs vs ~450 µs per dig). But the "1:1" wording overstates a guarantee GridMap's API does not provide, and it must be corrected before a Terrain Rendering spec author takes it literally.

**Resolution**: (a) correct the wording to scope the invariant to X/Y; (b) move it into ADR-0002, where the GDD already claims it lives; (c) measure per-dig octant-rebuild cost with all 16 (MVP) and 32 (full-vision) Z-layers populated, alongside the target-hardware run.

### 4.5 🔴 `File.Replace` will throw on the first checkpoint write of every encounter

**Type**: Correctness — *raised by the godot-specialist*

ADR-0004 §3 specifies *"gzip to a temp file in the same directory, flush, then rename/`File.Replace` over the slot."* `System.IO.File.Replace(source, destination, backup)` **throws `FileNotFoundException` when `destination` does not exist.**

ADR-0004's own "activation 0" rule guarantees that condition on every encounter: *"the first checkpoint is written immediately post-swap, before the first activation"* — by construction the first-ever write to that slot, with no pre-existing file to replace. Implemented literally as written, **it fails on the very first checkpoint write of every battle.**

There is a second problem in the same call: `File.Replace` is not the same primitive across platforms. On Windows it maps to `ReplaceFileW` with backup-file semantics; on Unix those semantics do not map cleanly. Atomic same-directory replacement on .NET 6+ is better expressed as `File.Move(source, destination, overwrite: true)`, which is backed by `rename()` and atomic on the same filesystem.

**Resolution**: standardize on `File.Move(temp, dest, overwrite: true)`. This fixes the missing-destination case and the platform-semantic mismatch in one change, and it is the concrete answer ADR-0004's own Verification Required item (*"atomic-replace semantics on Windows (`File.Replace`) vs POSIX rename"*) should land on. The same-volume requirement ADR-0004 already records still applies.

### 4.6 ⚠️ ADR-0002's promotion gate is understated — and the Terrain GDD is stale on it

**Type**: Process / promotion gating — *the second thread the request asked about*

ADR-0002 and ADR-0004 agree with each other perfectly on the shared gate. The problem is on either side of that agreement.

**ADR-0002 lists one open gate. Its own review log tracks five:**

| Open external task (terrain review log, 2026-08-02) | In ADR-0002? |
|---|---|
| 60 fps target-hardware run | ✅ criterion 5 |
| 4.7.1 GridMap API verification + author `modules/gridmap.md` | ❌ |
| Overlay-map draw-call measurement ("largest unquantified draw-call risk") | ❌ |
| Multi-octant aggregate rebuild measurement (multi-front raid, ≥4 octants in one frame) | ❌ |
| `EncounterOutcomeReport` schema change for CD-1 ordering | ❌ |

**And the Terrain GDD never received the 2026-08-03 Battle Persistence propagation at all.** It contains **zero** occurrences of "checkpoint" or "Battle Persistence". Its OQ#1 — which the GDD itself calls *"the last gate before ADR-0002 moves from Proposed to Accepted"* — still describes the pre-amendment gate, with no mention of the per-activation checkpoint cadence measurement the amendment added to criterion 5.

**Root cause is identifiable**: the change-impact report's §2 "Non-ADR documents" table lists four documents to edit — contracts annex, systems index, technical-preferences, Time Authority GDD. **The Terrain GDD is absent from that table.** The omission originated in the impact assessment, not its execution. It matters because ADR-0002's `Snapshot()` contract is directly changed by Battle Persistence, and the Terrain GDD is what will be read when terrain stories are cut.

### 4.7 ⚠️ The reload-seed thread is not cleanly deferred — *the first thread the request asked about*

The request describes this as *"deliberately deferred to Raid Trigger #18's GDD."* Four records disagree:

1. **`systems-index.md` line 185** (CD-GDD-ALIGN M1, 2026-08-02): the reload seed policy — *identical replay vs. re-roll within threat band* — is *"explicitly routed to **Raid Trigger #18 + the Seeded RNG ADR**."* ← closest match to the request.
2. **`time-authority-mode-switch.md` OQ 3a**: the same question is struck through and marked ***"Dissolved by Battle Persistence… nothing is re-rolled… Residual obligation: checkpoint RNG-stream serialization, routed to the Seeded RNG ADR + Save/Load (#6). **Closed 2026-08-02**."*** ← routed to **#6, not #18**, and marked closed.
3. **Time Authority review log** (2026-08-02 adjudications): *"re-roll-within-threat-band **elevated to binding CD default** — identical replay now requires an explicit CD ruling."* ← a decision of record already exists.
4. **ADR-0004 §5**: the corrupt-checkpoint loud fallback offers the next-newest save and *"the battle restarts from its start"* — **materially re-opening the pre-battle-reload scenario** record 2 assumed away. ADR-0004 notices the wording problem and routes correction 3 (*"one honest restatement"* of the GDD's *"no longer reachable by any player action"*). **That correction was never filed.**

**And the deferral target is empty.** System #18 Raid / Threat Trigger — which must decide *when* and *where* raiders arrive, and which CD-2 charges with *"three battles in the same colony feel different because raiders came in from different places"*, calling it *"the MVP's primary variety lever"* — carries **no RNG, seeding, or determinism requirement anywhere** in the index or either GDD. CD-2 plainly implies stochastic breach selection; ADR-0001 forbids draws outside `Tick()`; nothing states how #18 complies.

So: a question marked *closed* in one document is still routed *open* in another, already has a *binding default*, has been *re-opened* by ADR-0004's fallback path, and its named owner has no RNG requirement to attach it to.

### 4.8 ⚠️ ADR-0004's routed corrections: 2 of 5 filed

| # | Routed correction | Status |
|---|---|---|
| 1 | GDD AC-66 re-authored for coalesce-newest | ✅ **Filed** — now reads *"exactly one checkpoint **snapshot** … the on-disk slot converges to the newest resolved state"* |
| 2 | GDD Rule 9(b) gains the "activation 0" checkpoint | ✅ **Filed** — carried verbatim |
| 3 | GDD "no pre-battle rewind" honest restatement | ❌ **Not filed** (§4.7) |
| 4 | ADR-0002/0003 `SnapshotInto` API obligation | ❌ **Not filed** (§4.2) |
| 5 | Save/Load #6 retains the switch-in autosave for the battle's duration | ⚠️ Cannot be filed — #6 does not exist. Correctly deferred. |

### 4.9 ⚠️ The systems index's cell-record sketch contradicts ADR-0002's firewall

**Type**: Data structure

`systems-index.md` Foundation item 1 states the cell record carries *"floor type, wall type, **material tier**, damage/HP, style dressing, **reservation tags**"* — and that *"memory layout (struct-of-arrays vs per-chunk AoS) [is] decided in ADR-002."* All three italicised claims are now wrong:

- **Material tier is not stored.** ADR-0002 derives it (`Catalog.Wall(WallTypeId).Tier`) precisely to avoid a second writer for the same fact; the Terrain GDD says *"Terrain derives tier by lookup, never sets it"* and *"stores no tier field."*
- **Reservation tags are not a cell field.** `technical-preferences.md` forbids *"cell fields describing occupants, plans, zones, or combat state."* Stack reservations live in the `EntityId`-keyed `StackReservationTable` owned by Stockpile & Hauling (ADR-0003). The only survivor is `Flags` bit 0 — a single terrain-job claim mutex, explicitly *"not item/stack reservations, ever."*
- **Memory layout is decided** — AoS confirmed, and the index's own Next Steps says so (*"AoS concession retired"*).

This is a pre-ADR sketch that was never updated. It matters because it is the sentence a requirements extraction reads first, and it would seed two wrong requirements.

### 4.10 ⚠️ Dependency-status drift across all four ADRs

Ten cross-references describe ADRs at statuses they left weeks ago — all of one kind: written before the referenced ADR was promoted or authored, never back-updated.

| Location | Says | Actual |
|---|---|---|
| ADR-0001 Related Decisions | ADR-0002 "(pending)", ADR-0003 "(pending)", ADR-0004 "(pending)" | Proposed / **Accepted** / Proposed |
| ADR-0002 Depends On + Related | ADR-0001 "(Proposed)" ×2, ADR-0003 "(pending)" | **Accepted** / **Accepted** |
| ADR-0002 Amendment | ADR-0004 "(pending)" | Proposed (exists) |
| ADR-0003 Depends On + Related | ADR-0001 "(Proposed)" ×2 | **Accepted** |
| ADR-0003 Amendment | ADR-0004 "(pending)" | Proposed (exists) |
| `cross-cutting-contracts.md` | ADR-0004 "(pending)" | Proposed (exists) |

`cross-cutting-contracts.md` additionally carries a stale header: **Date 2026-07-25** and a Sources line naming only ADR-0001/0002/0003, while its body carries the 2026-08-03 rewrite and references ADR-0004. It also describes ADR-0002's gate without the 2026-08-03 re-scope.

### 4.11 ⚠️ `technical-preferences.md` internal contradiction on render memory

Line 41 (Memory Ceiling): *"terrain render/video memory **14.25 MB** … (measured 2026-07-25)"* — the **single-GridMap** figure.
Line 10 (Rendering): two stacked GridMaps cost *"+2.17 MB video memory (measured 2026-07-26)"*.
ADR-0002: *"video memory 14.25 → **16.42 MB**."*
Terrain GDD correctly carries the range *"14.25–16.42 MB"* and adds it was measured at MVP bounds only, so VRAM must be re-measured before adopting the 256×256×32 ceiling.

Line 41 was not updated when the two-map decision landed.

### 4.12 ⚠️ No frame-budget ledger exists

The skill's performance-budget conflict check cannot be performed: **no ADR claims a frame-time allocation in summable form**, `registry/architecture.yaml.performance_budgets` is empty, and `technical-preferences.md` records the whole-process memory ceiling as `[TO BE CONFIGURED]` and the 500-draw-call whole-frame ceiling as "provisional."

Already known — the Time Authority review log records *"frame-budget ledger ruled a process gap… routed to technical-director (OQ #8), before the third sim-bearing system's Done."* Recorded here so it is not lost. The measured per-system costs are all comfortably small (dispatch 0.578 µs/sub-step, terrain mutation 0.338 µs, walkability sweep 0.290 ms, revision polling 63.2 µs/dig), with one outlier already routed: full region flood fill at **4.16 ms = 25.1% of a frame**, which must never run per dig.

### 4.13 ⚠️ ADR-0001's view-freeze companion note was never filed

The Time Authority GDD's 2026-08-02 review recorded blocking item B3 (*"colonists caught mid-stride" was unachievable while `_Process` keeps running*), resolved by naming a view-freeze contract with `ModeTransitioned` as the signal plus AC-64, and routed *"ADR-0001 presentation-layer companion note for the view-freeze contract"* to technical-director.

ADR-0001 contains no such note — "view-freeze" appears nowhere in `docs/architecture/`. ADR-0001 still says only that presentation smoothing *"lives in nodes' own `_Process`"*, with no statement of what `_Process` must do during TurnBased. ADR-0003 flags the same hole as an open question. Two ADRs point at each other; neither owns it. The GDD additionally requires **one shared freeze/resume mechanism** so AnimationPlayer / Tween / interpolation-alpha implementations do not diverge three ways.

### 4.14 ⚠️ Document bookkeeping contradictions

**`systems-index.md`**, four contradictions inside one file:

| Claim | Contradicted by |
|---|---|
| Progress Tracker: *"Tier 0 spikes complete — **0/6**"* | Next Steps: *"**TIER 0 SPIKE GATE COMPLETE (5/5)**"* — the denominator differs too |
| Progress Tracker: *"ADRs written — **3/3** + contracts annex"* | ADR-0004 exists; the Recommended Design Order table has no ADR-0004 row |
| #29 Debug Console: *"Not Started"* | Next Steps `[x] Build the debug console`; `src/tools/DebugConsole/DebugConsoleRoot.cs` exists as a documented autoload |
| Next Steps: `[ ] After spikes report: begin GDD authoring` | Two GDDs written (Terrain **Approved**, Time Authority **Designed**) |

Neither ADR-0004 nor the Seeded RNG ADR appears in the Next Steps checklist, despite being follow-up actions 1 and 2 of the change-impact report.

**Dependency Map numbering diverges from the enumeration table** for four systems: Map Authoring is listed as "13" but is #14; Excavation "14" is #15; Construction "15" is #16; Needs & Simulation "16" is #13. Anyone building a coverage matrix from the Dependency Map rather than the enumeration table will mis-map all four. **Use the enumeration table.**

**Status drift on #2**: `time-authority-mode-switch.md`'s own header says `Status: In Design`; the index says **Designed**, pending re-review.

**Stale engine residue in `game-concept.md`**: the Scope Risks section says *"route input through **Unity's** Input System from day one"*, and Next Steps says *"pin **Unity** version, **URP** Forward+."* The engine is Godot 4.7.1; `technical-preferences.md` already carries the corrected version (Godot `InputMap`/`InputEvent`). The requirement's intent survives; the text names the wrong engine. The same section's *"no performance budget or Unity version is pinned yet"* is also obsolete.

---

## 5. GDD revision flags (architecture → design feedback)

| GDD | Assumption | Reality | Action |
|---|---|---|---|
| `design/art/art-bible.md` §1 | *"Every construction style, ornament set, and material finish… available everywhere, immediately"*; commits to two vocabularies (Bavarian + Roman) plus ornament sets, all available anywhere | Measured GridMap draw-call scaling caps distinct style combos **co-occurring in one 32×32 octant** at **~8 per tier** (1→32, 2→48, 4→80, 8→144 against a ≤150 budget), and the ceiling *"may drop below 8"* once the damage-overlay map is measured. The floor MeshLibrary's mandatory distinguishable stair item counts toward the same scaling. The art bible contains **no** draw-call or variant budget anywhere. | **Revise** — the palette specification (art bible §4/§8, currently `[To be designed]`) must carry the ceiling **before kit-of-parts authoring**. Owner exists: Terrain GDD OQ#9, art-director + technical-director |
| `design/gdd/time-authority-mode-switch.md` | *"the pre-battle moment is no longer reachable by any player action"* | False twice: ADR-0004 §5's corrupt-checkpoint fallback restarts the battle from its start, and ordinary colony manual saves still allow a pre-raid reload (change-impact §5, routed to creative-director) | **Revise** — ADR-0004 routed correction 3; one honest restatement, not two scattered footnotes |
| `design/gdd/terrain-data-model.md` | Snapshot/serialization sections written against the pre-Battle-Persistence model; OQ#1 describes the pre-amendment promotion gate | ADR-0002 Amendment 2026-08-03 changed when `Snapshot()` runs and re-scoped criterion 5 | **Revise** — the document was omitted from the propagation entirely (§4.6) |
| `design/gdd/game-concept.md` | Input routing and version-pinning advice names Unity / URP | Engine is Godot 4.7.1 | **Revise** — text-level correction only; intent already carried correctly in `technical-preferences.md` |

The art-bible flag is the one with a real deadline: by the art/lighting pass, the variant count is sunk cost.

---

## 6. Engine compatibility

### 6.1 Audit results

**Engine**: Godot 4.7.1 · **ADRs with an Engine Compatibility section: 4 / 4** ✅ — and all four carry every other section `docs/CLAUDE.md` requires, plus Validation Criteria and Alternatives Considered.

**Version consistency**: ✅ All four name Godot 4.7.1 verbatim. No ADR is written against an older version.

**Post-Cutoff APIs Used**: all four declare **None**, and the declaration is credible — the load-bearing decision in every case is plain C# behind a zero-Godot-reference boundary. ADR-0004 is explicitly and correctly reasoned that using .NET `System.IO`/`GZipStream` rather than Godot `FileAccess` sidesteps the 4.4 `FileAccess` return-type change.

**Deprecated API references**: **zero hits** across all four ADRs, checked against `deprecated-apis.md` and `breaking-changes.md`. Every named surface — `Node._PhysicsProcess`, `ProcessMode`, Autoload, `GridMap`, `MeshLibrary`, `SetItemMeshTransform`, `SetCellItem`, `System.IO`, `GZipStream`, `File.Replace`, `ProjectSettings.GlobalizePath`, `NOTIFICATION_WM_CLOSE_REQUEST` — is clean. (See the caveat in §6.2 on how much that result is worth.)

**Post-cutoff API conflicts between ADRs**: none. GridMap/MeshLibrary is owned exclusively by ADR-0002 and mentioned by no other. Jolt (4.6 default) is consistently resolved as inapplicable across ADR-0001, ADR-0003 and `technical-preferences.md` — unit movement is cell-to-cell on the tile grid, never physics-body-driven. `AreaLight3D`/HDR are deferred to the art/lighting pass by `technical-preferences.md` and claimed by no ADR.

### 6.2 Engine specialist findings

**🔴 The engine reference library is a version behind the engine it is supposed to pin — and this bounds the deprecated-API result above.**

`VERSION.md` is current (last verified 2026-07-24). Everything else is not: `breaking-changes.md`, `deprecated-apis.md`, `current-best-practices.md` and all eight module docs are stamped **"Last verified: 2026-02-12 | Engine: Godot 4.6"** — five months and one full minor version behind the pinned 4.7.1, and dated *before* 4.7's release per `VERSION.md`'s own timeline. `breaking-changes.md` has sections for 4.2→4.3 through 4.5→4.6 and **no 4.6 → 4.7 section at all**. The only place the 4.7 breaking-change list exists is `VERSION.md`'s summary table.

All four ADRs list `breaking-changes.md` under "References Consulted." An author consulting it alone would not see the 4.7 changes. Nothing in that list (BlendSpace, audio spectrum analyzer, device IDs, particle angular velocity, shader preprocessor) intersects anything any ADR does today — so **no ADR is currently exposed** — but the clean result in §6.1 is only as strong as a source that does not cover the pinned version.

**🔴 There is no `modules/gridmap.md`, and `rendering.md` never mentions GridMap or MeshLibrary at all.** GridMap is the locked terrain render backend. `VERSION.md` carries a single unelaborated bullet (*"`MeshLibrary` editor improvements"*). The Terrain GDD's pre-render-backend verification gate already makes authoring this file a precondition and specifies that `/story-readiness` must return **BLOCKED** for any render-backend story until it exists. Two load-bearing claims rest on pre-4.4 knowledge: that GridMap offers no per-instance data channel (the sole justification for the damage-overlay design), and that octant rebake is octant-granular (the sole justification for the octant/chunk lock — which *"degrades from invariant to heuristic"* if 4.6/4.7 introduced partial-octant rebake).

**⚠️ `project.godot` leaves the renderer `[TO BE CONFIGURED]`** while `technical-preferences.md` and ADR-0002 both treat Forward+ as decided and the spike ran on it explicitly. This matters directly for the shared promotion gate: whoever runs the target-hardware measurement needs the renderer pinned in the project file, or the run may silently default to Mobile or Compatibility and produce numbers that do not correspond to the decision. Related: no ADR specifies which *rendering device backend* that run uses. D3D12 became the Windows default in 4.6, and the spike ran on Linux with lavapipe software Vulkan — so the target-hardware run will be the project's first exercise of a real Windows/D3D12 path for GridMap octant rebuilds. Pin the backend for that run rather than letting the host OS choose.

**⚠️ ADR-0002's Engine Compatibility table is stale relative to its own body.** The table says the render backend is *"NOT decided here — decided in the Terrain Rendering & Cutaway quick-spec after the terrain spike."* ADR-0002's own Spike Results section, added to the same document, **does decide it**, and `technical-preferences.md` upgrades that to a locked invariant. The section whose entire job is *"what post-cutoff API does this decision depend on"* now understates the dependency. Sync it to name GridMap/MeshLibrary directly.

**⚠️ GridMap collision must be disabled at the MeshLibrary, not the GridMap.** ADR-0002 and the spike note record that collision shapes were disabled because the grid is not physics-driven. The nuance for whoever writes the rendering spec: GridMap generates per-octant collision bodies from shapes baked into each `MeshLibraryItem`, not from `GridMap.collision_layer`. Zeroing `collision_layer` changes what the body collides *with*; it does not skip constructing the `PhysicsServer3D` body. "Disabled" must mean the MeshLibrary items carry no shapes, or octant-rebuild cost silently includes physics-shape construction the spike numbers would not reflect.

**⚠️ A naive pause menu will not pause the game.** `TimeAuthorityRoot` runs `ProcessMode = Always` specifically so dispatch is never frozen by an unrelated pause toggle — correct for its purpose. The consequence is that a future pause menu doing the textbook Godot thing (`GetTree().Paused = true`) will **not stop the simulation**, because `Always`-mode nodes ignore `SceneTree.paused`. ADR-0001's intent is right (*"a separate UI-pause concern layered on top — two competing pause mechanisms are forbidden"*), but the practical warning — the pause menu **must** route through `TimeAuthorityManager`'s speed-0 path — is not stated anywhere, and `SceneTree.paused` is the first thing most Godot developers reach for. Worth one line in ADR-0001 or the eventual Pause Menu spec. (The Time Authority GDD already specifies a CI grep gate for `SceneTree.paused` outside `src/core`, with an explicit allowlist entry for the sanctioned UI-pause layer — that gate is the enforcement; this is the missing explanation.)

**⚠️ ADR-0003's GC rationale is imprecise in a way that now matters more.** ADR-0003 justifies struct-by-default entity records with *"Godot's embedded .NET GC runs on the main thread."* The conclusion (avoid heap-allocated per-entity records) is right; the mechanism is not. CoreCLR's default workstation-concurrent mode does background collection off the main thread. What actually matters is that **blocking/Gen2 collections are stop-the-world across every managed thread**, including the sim thread, regardless of which thread's allocations triggered them. That distinction is newly load-bearing because ADR-0004 introduces a background writer thread performing 150–300 gzip passes per battle — ADR-0004 already requires that thread to reuse its buffers for exactly this reason, but ADR-0003's wording could mislead a reader into thinking background-thread allocation is exempt.

**✅ Positive — the zero-Godot-reference guarantee is stronger than the ADRs claim.** The ADRs describe *"CI greps the terrain/entity assembly for Godot references"* as the enforcement mechanism. `src/core/Hollowdeep.Core.csproj` uses `Sdk="Microsoft.NET.Sdk"` (net8.0, `TreatWarningsAsErrors=true`) with **no reference to GodotSharp at all**, while the root `Hollowdeep.csproj` uses `Sdk="Godot.NET.Sdk/4.7.1"` and pulls Core in by `ProjectReference`, with an explicit `<Compile Remove>` block excluding `src/core`, `tests`, `tools` and `prototypes` from its own glob. Referencing a Godot type in the core assembly is therefore a **build failure, not a lint finding**. Recommend the CI gate assert the manifest (no Godot package/project reference) as the authoritative test, keeping the source grep as a secondary lint. No export-template pitfall found; one cheap verification item before Save/Load #6 ships: confirm export publish settings do not enable aggressive IL trimming that could affect `System.IO.Compression`'s native zlib dependency.

**✅ Confirmed sound**: background-thread I/O never touches Node/SceneTree/RenderingServer, so Godot's main-thread API restrictions do not apply, and ADR-0004 correctly keeps its one Godot call (`ProjectSettings.GlobalizePath`) on the main thread at the composition root. `EntityId.Value : long` is the right choice — Godot's Variant integer is signed 64-bit, with no unsigned-64 Variant type. Views re-resolving `EntityId` through binary search each frame rather than caching slot indices is correct given `Despawn` compacts in ascending order (worth one line in the Views spec, since caching the index is an easy "optimization" for a future contributor). `_PhysicsProcess` (sim) versus `_Process` (view read) carries no torn-read hazard on Godot's single-threaded main loop. ADR-0001's `readonly struct` mutation-window scope genuinely does avoid boxing — with the caveat that boxing reappears if `MutationWindow` (or the `ref struct` `TerrainChangeBatch`) is ever widened to an interface-typed parameter, which is exactly why the CI allocation gate ADR-0001 already commits to is the durable enforcement.

---

## 7. Architecture document coverage

`docs/architecture/architecture.md` **does not exist**, so Phase 6 coverage cannot be checked against a master architecture document. Consistent with the project's stated sequencing (ADRs and the contracts annex first; `/create-architecture` has not been run). Not a defect at this stage, but it means the four ADRs plus the one-page annex are the only architecture-level synthesis.

`docs/architecture/architecture-traceability.md` also does not exist — noted as expected in the change-impact report.

---

## 8. Blocking issues (must resolve before PASS)

1. **Author ADR-0005 Seeded RNG / Determinism.** Its constraints are already fixed by four documents and can be lifted nearly verbatim: draws only inside `Tick()`/authority-driven resolution (ADR-0001); per-system seeded streams that round-trip like all other state (annex contract #2); **resumable at arbitrary draw counts**, constraining the algorithm to explicit-serializable-state generators (PCG/xoshiro class) and ruling out hidden or platform-dependent internal state; entity-spawn draws — appearance seeds, raider composition (ADR-0003). It must additionally (a) reconcile the four reload-seed records in §4.7 and inherit the binding CD default of re-roll-within-threat-band, and (b) give system #18 a breach-point-selection stream, which it currently lacks entirely.
2. **Resolve the two-vs-three GridMap conflict** (§4.1) and fold the damage-overlay draw-call measurement into ADR-0002's criterion 5.
3. **File ADR-0004's routed corrections 3 and 4** (§4.2, §4.7). Correction 4 in particular leaves ADR-0004's zero-allocation guarantee resting on an API that does not exist in its dependencies' contracts.
4. **Correct the `File.Replace` write path** (§4.5) — this one fails at runtime on the first checkpoint of every battle if implemented as written.

## 9. Non-blocking corrections recommended

All mechanical; none requires a design decision. **No file outside this report has been modified** — every item below touches an approved design document or an Accepted ADR and is proposed, not applied.

| Priority | Fix | Files |
|---|---|---|
| High | Populate `docs/registry/architecture.yaml` from the four ADRs (§2.1) | `docs/registry/architecture.yaml` |
| High | Propagate Battle Persistence into the Terrain GDD (§4.6) | `terrain-data-model.md` |
| High | Add the four untracked promotion-gate items to ADR-0002 (§4.6) | ADR-0002 |
| High | Refresh the engine reference library to 4.7.1 and author `modules/gridmap.md` (§6.2) | `docs/engine-reference/godot/**` |
| High | Pin the renderer in `project.godot` before the target-hardware run (§6.2) | `project.godot` |
| Medium | Correct the octant/chunk invariant to X/Y-only and move it into ADR-0002 (§4.4) | ADR-0002, `technical-preferences.md`, `terrain-data-model.md` |
| Medium | Fix ADR-0003 diagram line 242 + the two stale CD-9 rationales (§4.3) | ADR-0003 |
| Medium | Correct ADR-0002's CD-1 coverage claim to three-quarters; record the `EncounterOutcomeReport` breach-list schema change against ADR-0003 (Terrain Known Gap #5) | ADR-0002, ADR-0003 |
| Medium | File ADR-0001's view-freeze companion note + the one-shared-mechanism requirement (§4.13) | ADR-0001 |
| Medium | Fix the cell-record sketch in the systems index (§4.9) | `systems-index.md` |
| Medium | Sync ADR-0002's Engine Compatibility table to name GridMap/MeshLibrary (§6.2) | ADR-0002 |
| Low | Add the `ProcessMode.Always` pause-menu warning (§6.2) | ADR-0001 |
| Low | Correct ADR-0003's GC-thread wording (§6.2) | ADR-0003 |
| Low | Refresh the ten stale dependency statuses + the annex header (§4.10) | all four ADRs, annex |
| Low | Fix render memory to 14.25–16.42 MB (§4.11) | `technical-preferences.md` |
| Low | Reconcile spike counts, ADR count, #29 status, checklist state, Dependency Map numbering (§4.14) | `systems-index.md` |
| Low | Remove Unity/URP residue (§4.14) | `game-concept.md` |
| Low | Reconcile #2's status (`In Design` vs `Designed`) (§4.14) | `time-authority-mode-switch.md` |
| Low | Ratify the TR slug scheme, then issue the 203 TR-IDs (§2.2) | `tr-registry.yaml` |

## 10. Required ADRs, most foundational first

1. **ADR-0005 — Seeded RNG / Determinism** (#4, Foundation, MVP) — blocking, above.
2. **Save/Load & World Serialization** (#6) — the save-file *container* format (magic, schema version, CRC, writer-id header, monotonic save ordinal) is depended on by ADR-0002 (material manifest), ADR-0003 (store schemas + `EntityIdSource`) and ADR-0004 (provenance + ordering), but owned by no document. Correctly sequenced after ADR-0004 and ADR-0005.
3. **World Change Event Bus** (#3) — substantively covered by ADR-0002 rule 1 plus annex contract #3. Recommend closing the index entry by reference rather than writing a fourth contract, since the annex is capped at three by mandate.

---

## 11. Pre-gate checklist

| Artifact | State | Required action |
|---|---|---|
| `tests/unit/`, `tests/integration/` | ❌ Missing | Run `/test-setup` |
| `.github/workflows/tests.yml` | ❌ Missing | Run `/test-setup` |
| `design/accessibility-requirements.md` | ❌ Missing | Run `/ux-design` |
| `design/ux/interaction-patterns.md` | ❌ Missing | Run `/ux-design` |
| `docs/architecture/control-manifest.md` | ❌ Missing | Run `/create-control-manifest` after ADR-0005 |
| `docs/engine-reference/godot/modules/gridmap.md` | ❌ Missing | Blocks all render-backend stories by the Terrain GDD's own enforcement rule |

`/gate-check pre-production` should not be run until at least the test infrastructure and ADR-0005 exist.

Three dependencies will bite as soon as `/test-setup` runs. The Terrain GDD's Known Gap #2 names **two missing test hooks** — forcing `TimeAuthorityManager` into a given mode and window state without a full tick, and the debug-console sweep for *claim bit ≡ Job Assignment's table* — which block four of its acceptance criteria including the determinism gate. Known Gap #3 requires the spike's render-matches-model check (15,763 cells verified against `TerrainWorld`) to be reimplemented in `tests/integration/terrain/`, because prototype code is never migrated. And the Time Authority GDD's OQ #10 forbids wiring AC-34/35/36 into CI before N≥5 mean±stddev re-measurement with warm-up passes.

The missing accessibility document is more load-bearing than a checklist row suggests: the Time Authority GDD names it the binding owner of four transmitted mitigations, including the after-action survey's no-timeout/no-escape lockout, which that GDD calls its *"single highest lockout risk."*

---

## 12. What is genuinely healthy

Recorded deliberately, because a FAIL on a bookkeeping premise would otherwise misrepresent this architecture:

- **The Battle Persistence propagation was, on the ADR side, done properly.** Amendments are dated, in place, and mark retracted claims inline rather than deleting them. The binding wording constraint (*"serialized only into the battle checkpoint by its owning systems"*) is reproduced verbatim in ADR-0003, the annex and `technical-preferences.md` — the firewall it protects held.
- **ADR-0001 and ADR-0004 interlock exactly.** ADR-0004's snapshot beat (`AwaitingPresentation → NextActor`) is provably gate-idle under ADR-0001's state machine, and its claim that the background write overlaps the next activation's presentation follows from ADR-0001's presentation gating. The two were written months apart and agree without hand-waving.
- **The three "implement-it-wrong-by-default" traps are all closed** — load-into-TurnBased bypassing `RequestSwitch`, the load-window mode-assertion exemption scoped as a distinct sanctioned writer, and the occupancy rebuild filtering dead units.
- **Every measurable claim is backed by a spike number**, and where a spike falsified an ADR the ADR was corrected in the honest direction — the AoS concession was retired because AoS measured *faster*, not slower.
- **Determinism where it does not depend on RNG is genuinely solid**: one displacement algorithm shared between terrain and pre-switch normalization rather than two that drift; fixed ascending (Z, Y, X) scan order; dispatch ordering by `(TickPhase, priority)` with duplicate registration rejected; decision-set-not-live-occupancy normalization. Only the *stream* half is ungoverned.
- **The scaffolded code matches the contracts.** `src/core/Primitives/EntityId.cs`, `CellCoord.cs` and `ChunkCoord.cs` implement ADR-0002/0003 exactly — `EntityId` is `long` with `None = 0`, Z increases downward, and the doc comments carry the rationale. No implementation has run ahead of its ADR.

---

*Generated by `/architecture-review` (full mode), with a godot-specialist Phase 5 consultation.*
