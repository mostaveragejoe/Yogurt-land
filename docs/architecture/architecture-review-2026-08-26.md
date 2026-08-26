# Architecture Review Report

- **Date:** 2026-08-26
- **Engine:** Godot 4.7.1 / C# (.NET 8)
- **Mode:** `/architecture-review` full
- **GDDs Reviewed:** 2 — `terrain-data-model.md`, `time-authority-mode-switch.md`
- **ADRs Reviewed:** 6 — ADR-0001 (Accepted), ADR-0002 (Accepted), ADR-0003 (Accepted), ADR-0004 (Proposed), ADR-0005 (Proposed), ADR-0006 (Proposed)
- **Also read:** 4 quick-specs, `architecture.md` v1.0, `cross-cutting-contracts.md`, engine reference (10 module docs), `tr-registry.yaml`
- **Supersedes:** `architecture-review-2026-08-08.md`
- **Not run:** the godot-specialist second-opinion consultation (Phase 5) — this session is configured not to spawn subagents unless asked. The automated engine audit below did run in full.

---

## Verdict: **CONCERNS**

**Not FAIL** — zero uncovered requirements, no dependency cycle, no data/state ownership
collision, and engine compatibility is clean across all six ADRs.

**Not PASS** — two requirements carry *known-wrong* coverage rather than merely deferred
coverage (one of them previously unrecorded), and five documentation-integrity defects have
the repo contradicting its own decisions in ways that will mislead an implementer.

**Carried blocker discharged:** CI is green. Four runs on `main`, all `success`, latest at
`d364992` (restore 7 s / build 6 s / test 4 s; both architecture grep gates pass). The
"workflow has never executed" warning carried from 2026-08-24 and 2026-08-25 is closed.

---

## Traceability Summary

| Status | Recorded | Actual (this review) |
|---|---|---|
| ✅ Covered | 77 | **80** (82%) |
| ⚠️ Partial | 20 | **15** (16%) |
| 🔴 Known-wrong | — | **2** (2%) |
| ❌ Gap | 1 | **0** |
| **Total** | 98 ✗ | **97** ✓ |

The recorded header was wrong three ways: it sums to 98 rather than 97; the matrix body
actually held 76 ✅ / 21 ⚠️, not 77/20; and the `❌ Gap: 1` entry is a stale duplicate of
TR-time-025, which the same file marks ✅ in the matrix and which ADR-0005 closed on
2026-08-08. `architecture.md` §7.4 repeats the incorrect 77/20/0.

### Rows moved ⚠️ → ✅ (evidence found this run)

| Row | Why it closes |
|---|---|
| **TR-time-011** engine step-clamp guard | `Engine.MaxPhysicsStepsPerFrame` name, default (8) and read-API confirmed against the `4.7.1-stable` tag (`modules/physics.md`, 2026-08-25). The architectural question is answered; building the guard is implementation, not an ADR gap |
| **TR-terrain-041** three discrete damage states | Breakpoints decided 2026-08-24 — damaged below 0.66, critical below 0.33 of `MaxWallHp`, load-validated as ordered (terrain GDD ownership table) |
| **TR-time-045** quit-dialog text + focus | Now covered by `design/ux/interaction-patterns.md:180-181` and `design/accessibility-requirements.md:151`. Both state the affirmative must not hold default focus and that `QuitConfirmText` is tested by equality |
| **TR-time-027** draws only in Tick; reload no re-roll | Re-scoped — see Conflict 2. This row governs mid-battle checkpoint resume, which the save-scum ruling does not touch. It was incorrectly marked overturned |

### Rows verified as still ⚠️ (not upgraded)

- **TR-terrain-043** dormant-stair indicator — the Terrain Rendering quick-spec closed the *cutaway* question (C5, uniform window depth) but explicitly routes the indicator to Blueprint UI #26; its own **AC-15 is marked "blocked on #26"**.
- **TR-terrain-042 / TR-terrain-044** — design answered (sparse `MultiMeshInstance3D` overlay per damage state), draw-call **measurement still owed** (QQ-05 / quick-spec AC-10).
- **TR-time-050** speed dial — P7 covers the *display* half. The GDD's "a delivered-vs-requested **divergence signal** MUST exist" half appears nowhere in `design/ux/` or ADR-0001.
- Excavation (6), Notifications (2), Map Authoring (2), Combat set (1) — foundation hook present, consuming quick-spec unwritten. Expected under the tiered-doc plan, not ADR defects.

---

## Cross-ADR / Cross-Document Conflicts

No **ADR-vs-ADR** conflict exists. Ownership remains cleanly partitioned (ADR-0001 time
authority and the mutation window; ADR-0002 all terrain cell state and the change-event
stream; ADR-0003 all entity state; ADR-0004 the checkpoint contract, owning no underlying
state; ADR-0005 the RNG stream format; ADR-0006 the bus subscribe side). ADR-0006's addition
this cycle introduces no ownership overlap — it consumes ADR-0002's `ITerrainChangeSink`
without claiming any state.

Both conflicts below are **GDD/ADR vs. downstream-spec or vs. user ruling.**

### 🔴 Conflict 1 — Pathfinding registration (NEW, previously unrecorded)

`architecture.md` §7.5 is titled "The one open conflict." There are two.

| Source | Claim |
|---|---|
| `time-authority-mode-switch.md:144` | Pathfinding (#8) "Registered under **both** authorities (colony paths / combat reachability) — the notable dual-registration case" |
| **ADR-0001:195** (**Accepted**) | Lists Pathfinding in the tickable registration table as RealTime + TurnBased, with per-authority Tick behaviour columns |
| `design/quick-specs/pathfinding-navigation.md` §4 (2026-08-24) | "Pathfinding is **passive**: it registers no `ITickable`, owns no `Tick()`, and advances no state" |

**Type:** Integration contract / registration model.

**Impact:** TR-time-039 is filed as "⚠️ ADR-0001 (mechanism); Pathfinding quick-spec
(deferred)" — but the quick-spec is written and it contradicts the ADR. The coverage is
known-wrong, not deferred. An implementer reading ADR-0001 registers a tickable that the
owning spec says must not exist.

**Governance note:** this is the same failure mode the 2026-08-24 gate-check named for the
sparse damage overlay — *"a downstream quick-spec had overturned a Foundation ADR's damage
backend with only an open-item note, which is a governance defect regardless of the decision
being right."* That instance was caught and recorded as a formal ADR-0002 amendment. This one
recurred and was not recorded. The pattern, not the individual case, is the finding.

**Resolution options:**
1. **Amend ADR-0001** (recommended) — remove Pathfinding from the tickable table; state that
   it is a passive query service whose cache invalidation runs in its ADR-0006 bus handler at
   priority 10, not in `Tick()`. Correct TR-time-039's wording to match. On the merits the
   quick-spec appears right: with ADR-0006 dispatching invalidation, a pure query service has
   no tick-time work, and ADR-0001's own row already describes its only duty as "invalidate
   cached paths/regions on terrain change."
2. **Amend the quick-spec** — reinstate dual registration. Requires a reason the bus handler
   is insufficient; none is currently stated.
3. Escalate to technical-director as a blocking open question before Pathfinding #8 work.

### 🔴 Conflict 2 — Combat RNG re-roll vs. identical replay (recorded as QQ-01, but mis-scoped)

**Type:** State/determinism contract vs. user ruling.

The repo records the 2026-08-24 save-scum ruling as overturning **both** TR-time-026 and
TR-time-027 (`requirements-traceability.md`, `architecture.md` §7.5). It does not:

- **TR-time-027** — *"reload resumes **the same battle** with nothing re-rolled"* governs the
  **mid-battle checkpoint resume** path, which restores `State` directly and is unaffected by
  the derivation key. Open Question 3a explicitly closed this case on 2026-08-02.
  **Not overturned — reclassified ✅.**
- **TR-time-026** — the **cross-save identical-replay** clause is the half genuinely in
  conflict, because ADR-0005 derives the Combat stream from
  `splitmix64(RootSeed, Combat, EncounterId)` precisely to make battle *N* reproduce across a
  colony save/load. **Stays 🔴.**

Scoping matters because it defines the amendment's blast radius.

**An unstated consequence.** The fix is described repo-wide as "derive from an encounter
attempt counter or equivalent." But an attempt counter persisted **in the colony save**
restores with the save and therefore re-rolls nothing — the exploit survives. To vary per
load-and-retry, the counter must deliberately survive **outside** the save file (profile- or
session-scoped). At that point "bit-identical replay from a fixed seed and input sequence" is
no longer true by construction, so **TR-time-026 needs an explicit carve-out, not merely a
different derivation key**, and ADR-0005's validation criterion 3 (independence /
re-derivation test) needs restating. This should be on the record before the amendment is
drafted.

**Owner:** technical-director with Raid Trigger (#18). **Blocks:** ADR-0005 promotion.

---

## Documentation-Integrity Defects

These are not architectural disagreements. They are places where the repo states something
its own decisions have already superseded, in locations an implementer will act on.

| # | Defect | Consequence |
|---|---|---|
| 1 | **ADR-0002 contradicts its own status header.** Line 4 declares **Accepted** (2026-08-24). Line 6 says "promotion to Accepted now awaits only the checkpoint clause"; line 416 says "this ADR **remains Proposed**, exactly as the amendment requires." The 2026-08-24 amendment retired that coupling; two body passages were never updated | `docs/CLAUDE.md` auto-blocks stories referencing a Proposed ADR, and ADR-0002 gates 11 of 35 systems. A reader landing on line 416 concludes all terrain work is blocked |
| 2 | **`.claude/docs/technical-preferences.md:97`** records ADR-0002 as "(Proposed, 2026-07-24…)"; line 101's "**Next**: promote ADR-0002 + ADR-0004…" is stale | The project-standards doc most likely to be read first disagrees with the ADR |
| 3 | **`design/gdd/systems-index.md:123`** records "ADR-002 Terrain Data Model \| ADR (Proposed; spike-validated 2026-07-25 — … target-hardware criterion-5 run is the only gate left)" | Third location recording an Accepted ADR as Proposed. The named gate has since run and passed |
| 4 | **ADR-0006 is absent from the Architecture Decisions Log** in `technical-preferences.md` (the log ends at ADR-0005 + "Next") — while three of its rules are already cited as Forbidden Patterns at lines 72–74 | The log is the canonical ADR index; a decision enforced as a forbidden pattern but not listed is invisible to anyone reading the log to learn what governs the bus |
| 5 | **ADR-0004's dependency field is stale twice** — calls ADR-0002 "(Proposed, amended)" and names the "**Seeded RNG ADR (pending)**" rather than ADR-0005, which has existed since 2026-08-08 | Dependency ordering derived from ADR-0004's own header is wrong |
| 6 | **ADR-0006's migration plan is 4-of-5 applied.** Item 1 is not done: `design/quick-specs/terrain-rendering-cutaway.md:125` still reads `public void OnTerrainChanged(/* batch */);` | That placeholder is the exact defect ADR-0006 was written to close, and the ADR cites it by line number as its motivating evidence |

**Undercount corrected 2026-08-26, after fixing.** A repo-wide sweep run while applying these
fixes found **nine further live-document instances of the same class**, which the review's
first pass missed because it only searched for stale *ADR-0002* and *ADR-0006* status claims —
it never checked whether ADR-0001's status was recorded correctly by its dependents:

| # | Defect | Note |
|---|---|---|
| 7 | **ADR-0003:41 records its dependency ADR-0001 as "(Proposed)"** | ADR-0001 has been **Accepted since 2026-07-26** — stale for two months, and the most consequential of the set, since ADR-0003 is itself Accepted and Foundation-layer |
| 8 | ADR-0003:41 and :404 both record ADR-0002 as "(Proposed)" | Two more instances in the same file |
| 9 | `cross-cutting-contracts.md:4` — ADR-0002 "Proposed; spike-validated, awaiting the frame-rate clause on target hardware" | The contracts annex is a primary reference doc; the clause it names has passed |
| 10 | ADR-0004:29 Ordering Note — "Do not promote ADR-0002 until checkpoint cadence is measured on target hardware" | Inverted by the 2026-08-24 split: that clause now gates **ADR-0004**, not ADR-0002 |
| 11 | ADR-0004:196 Related Decisions — "Seeded RNG ADR (pending)" | Second instance of defect 5 in the same file |
| 12 | `terrain-data-model.md:455` — "the frame-rate clause does not yet exist… the last gate before ADR-0002 moves from Proposed to Accepted" | The run happened 2026-08-24 and passed |
| 13 | **ADR-0002:60 `Depends On` records ADR-0001 as "(Proposed)"** | Same two-month-stale ADR-0001 status, in the other Foundation ADR |
| 14 | ADR-0002:436 Related Decisions — ADR-0001 "(Proposed)" | Second instance in the same file |
| 15 | ADR-0003:403 Related Decisions — ADR-0001 "(Proposed)" | Third file-internal duplicate |

**Total: 15, all fixed.** **Five of the fifteen misrecorded ADR-0001** — Accepted 2026-07-26 —
as Proposed, across both other Foundation ADRs. Since `/story-readiness` auto-blocks stories on
a Proposed governing ADR, the repo was carrying a false block on the two most-depended-on
decisions in the project. Correctly-historical occurrences were left untouched — session logs,
the dated spike note, the dated QA-evidence README, the 2026-08-03 change-impact doc, the
2026-08-08 review, and ADR-0006's own quotations of the `/* batch */` placeholder as its
motivating evidence.

**Method note worth carrying**: the first pass enumerated defects by reading the documents a
reviewer would naturally reach for. It took a mechanical `grep` for the *pattern* — every
co-occurrence of an ADR id with "Proposed" — to find the rest. Six of twelve were invisible to
reading and obvious to grep. Future runs of this skill should grep the pattern, not just read
the documents.

---

## Registry Scope Gap — quick-specs carry no TR-IDs

The TR registry covers only the two foundation GDDs (46 terrain + 51 time = 97). Four
quick-specs have since been authored and now carry, between them, **71 acceptance criteria
and 1 TR-ID reference**:

| Quick-spec | ACs | TR-ID refs |
|---|---|---|
| `colonist-entity-attributes.md` | 19 | 0 |
| `material-catalog.md` | 15 | 0 |
| `pathfinding-navigation.md` | 22 | 0 |
| `terrain-rendering-cutaway.md` | 15 | 1 |

`/create-stories` embeds a TR-ID in each story's Context section and `/story-readiness`
validates that the ID exists and is active. A story sourced from a quick-spec has nothing to
cite. **Decide before story authoring begins** whether quick-spec ACs are registered as
TR-IDs (e.g. `TR-pathfinding-001`), or whether quick-spec-sourced stories are exempt from the
TR-ID requirement. Registering them is the smaller change and preserves the traceability
chain; this review did **not** register them, because doing so unilaterally would create ~71
IDs the user has not reviewed.

---

## ADR Dependency Order

```
Foundation:   ADR-0001 Time Authority          [Accepted]
              ADR-0002 Terrain Data Model      [Accepted]   (← 0001)
              ADR-0003 Entity Data Ownership   [Accepted]   (← 0001, 0002)
              ADR-0006 Event Bus Subscriber    [Proposed]   (← 0001, 0002 — both Accepted)
Persistence:  ADR-0004 Battle Checkpoint       [Proposed] ◄──► ADR-0005 Seeded RNG [Proposed]
```

**No cycles.** The Combat↔Veterancy cycle stays broken by `EncounterOutcomeReport`.

- ✅ **The ADR-0003 status inversion is resolved** — ADR-0002 reached Accepted 2026-08-24.
- ⚠️ **ADR-0004 ◄──► ADR-0005 is a declared co-promotion pair**, not a cycle: 0005 supplies
  the combat stream format, 0004 supplies the snapshot beat and pooled buffer. Both ADRs state
  this and state they promote together. Blocked jointly on **QQ-02** (build the async
  checkpoint path to measure it) and, for 0005 alone, on **QQ-01**.
- ⚠️ **ADR-0006 has no Proposed-ADR dependency** but cannot be implemented until **QQ-24**
  lands — the composition root is referenced 30 times across 5 documents and defined nowhere,
  and ADR-0006 rule 3 makes registration composition-root-only.

---

## GDD Revision Flags (Architecture → Design Feedback)

**One flag.** No GDD assumption is contradicted by verified *engine* behaviour; this flag is
a GDD that has fallen behind an Accepted ADR.

| GDD | Assumption | Reality (from ADR) | Action |
|-----|-----------|--------------------|--------|
| `time-authority-mode-switch.md` Rule 8 (line 41) | The canonical return sequence reaps "dead/broken/withdrawn entities"; the document contains **zero** occurrences of "downed" or "bleed" | ADR-0003 **Amendment 2026-08-24** (Downed state + persisted injury, CD-13): `PostEncounterReconcile` additionally **applies injuries** to those stabilized in the battle and **increments `BattlesSurvived`**, and **must not resolve downed colonists** — they walk out still downed, clock intact | Revise Rule 8 and TR-time-017 |

An implementer building reconcile from TR-time-017 alone would omit both added duties and
could reap downed colonists as "broken."

**Systems-index consequence — proposed, NOT applied.** Under the skill this warrants marking
the GDD `Needs Revision`. The proposed change to `design/gdd/systems-index.md:25` is:

- **Current:** `| 2 | Time Authority / Mode-Switch (inferred, elaborated) | Core | MVP | **Designed** (…) | …`
- **Proposed:** `| 2 | Time Authority / Mode-Switch (inferred, elaborated) | Core | MVP | **Needs Revision** (…existing text…; Rule 8 reconcile duties stale vs ADR-0003 Amendment 2026-08-24 — downed colonists, injury application, BattlesSurvived) | …`

This was left unapplied pending explicit approval, because `Needs Revision` is matched as an
exact string by other skills and changing a GDD's status has downstream gate effects.

---

## Engine Compatibility — clean

| Check | Result |
|---|---|
| ADRs with an Engine Compatibility section | **6 / 6** |
| Version agreement | All six pin **Godot 4.7.1**. No disagreement |
| Post-Cutoff APIs Used | All six declare **None**. Core is plain C# by contract; checkpoint IO uses .NET `System.IO`/`GZipStream`, RNG uses `System.Buffers.Binary` |
| Deprecated-API references | **0.** All 22 entries in `deprecated-apis.md` were cross-checked against all six ADRs. The `_process()` entry concerns a GDScript `$NodePath` idiom and does not apply — the ADRs' `_Process` usage is presentation-only, which is the sanctioned pattern |
| Stale version references | None |
| Post-cutoff API conflicts between ADRs | None — there are no post-cutoff APIs to conflict over |

**Engine gates closed since 2026-08-08:**
- `docs/engine-reference/godot/modules/gridmap.md` **authored** — GridMap downgraded HIGH → LOW
  risk (additive-only 4.3→4.7.1; 0 methods removed, 0 signature changes). This was the gate
  that returned BLOCKED from `/story-readiness` for render-backend stories.
- `modules/physics.md` **amended** — `max_physics_steps_per_frame` (default 8) and
  `physics_ticks_per_second` (60) confirmed exactly as ADR-0001 assumed, and ADR-0001 OQ #9
  answered: read the **`Engine` singleton**, not `ProjectSettings` (whose keys are read only at
  project start, so a guard reading them validates a stale value).
- **Target-hardware run 2026-08-24** — RTX 3060 Ti, p99 2.167 ms Vulkan / 2.024 ms D3D12
  against a 16.6 ms budget, 0 GC collections across 1800 frames at 8 digs/frame.

**Engine gates still open:** damage-overlay draw-call measurement (QQ-05 / quick-spec AC-10);
checkpoint snapshot+write at combat cadence (QQ-02).

**Not performed:** the godot-specialist second-opinion consultation the skill's Phase 5 calls
for. This session is configured not to spawn subagents unless explicitly asked. The 2026-08-08
specialist pass produced five findings; of those, the `File.Move(overwrite:true)` correction
(finding 1) and the `SetItemMeshTransform` text-precision note (finding 4) should be
re-confirmed as folded when that consultation is next run.

---

## Architecture Document Coverage

`docs/architecture/architecture.md` v1.0 (720 lines, 11 sections) now exists and was read in
full. `control-manifest.md` does not exist (`/create-control-manifest` has not run).

- Every system in `systems-index.md` appears in the architecture layer tables. **No orphaned
  architecture** — no system in the document lacks an index entry.
- The Open Questions Register (§8) is the document's strongest output: 26 tracked questions
  with owners and blocking triggers.
- **Two corrections owed to this document**, both consequences of findings above: §7.4's
  traceability numbers (77/20/0) do not match the matrix, and §7.5's "The one open conflict"
  is now two. Neither was edited by this review — `architecture.md` is outside the skill's
  write scope.

---

## Blocking Issues (must resolve before PASS)

1. **Resolve Conflict 1 (Pathfinding registration).** Amend ADR-0001 or the quick-spec. Until
   then TR-time-039's coverage is known-wrong.
2. **Resolve QQ-01 / Conflict 2 (RNG re-roll)**, including the persistence-scope consequence
   above. Blocks ADR-0005 promotion.
3. **Fix the six documentation-integrity defects**, especially the three stale
   ADR-0002-is-Proposed claims — they falsely block terrain implementation.
4. **QQ-24 — define the composition root.** 30 references, 0 definitions. Blocks the first
   store implementation and ADR-0006 implementation.
5. **QQ-02** — build ADR-0004's async checkpoint path so it can be measured; jointly gates
   ADR-0004 and ADR-0005 promotion.

## Required ADRs

**None net-new for the current foundation scope.** Every requirement has ADR coverage; the
remaining work is amendments (0001, 0005), promotions (0004, 0005, 0006), and the composition-root
definition — which QQ-24 already nominates as the next ADR.

---

## Handoff

**Immediate actions**
1. Route **Conflict 1** to technical-director — it is new, it contradicts an Accepted ADR, and
   it is cheap to settle.
2. Amend **ADR-0005** for the re-roll ruling, with the out-of-save persistence consequence
   stated (QQ-01).
3. Apply the six documentation-integrity corrections — mechanical, and they unblock
   terrain story authoring.

**Pre-gate checklist — all ✅**

- ✅ `tests/unit/`
- ✅ `tests/integration/`
- ✅ `.github/workflows/tests.yml` (green on `main`, 4/4 runs)
- ✅ `design/accessibility-requirements.md`
- ✅ `design/ux/interaction-patterns.md`

`/gate-check pre-production` is reachable. Note that its criterion 4 (Foundation-layer ADR
gaps resolved) will still register the three Proposed ADRs.

**Rerun trigger:** re-run `/architecture-review` after ADR-0001 and ADR-0005 are amended, to
confirm both known-wrong rows close.
