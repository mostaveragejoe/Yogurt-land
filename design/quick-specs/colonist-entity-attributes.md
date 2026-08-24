# Colonist Entity & Attributes — Quick-Spec

| Field | Value |
|-------|-------|
| **Systems index** | #9 (enumeration) / #8 (design order) — Core, MVP |
| **Doc tier** | Quick-spec — ADR-0003 already fixes the store, the write-ownership table and the writer interfaces; this spec fixes the **fields and their design meaning** |
| **Status** | Drafted 2026-08-24 |
| **Governing ADRs** | ADR-0003 (`ColonistStore`, `EntityId`, per-field-group writers, health writer-per-authority, `EncounterOutcomeReport`, occupancy) · ADR-0001 (writes only in authority-driven execution; reconcile ordering) · ADR-0005 (`AppearanceSeed` from a seeded stream, never `Guid`/`System.Random`) |
| **Source evidence** | Mode-switch spike 61/61 (zero-conversion swap over the same store instance); save/load spike 24/24 (`EntityIdSource` serialized, derived state contributes 0 bytes) |
| **Depends on** | Terrain Data Model (#1) for `CellCoord`. Nothing else structural — ADR-0003 is Accepted |
| **CD notes bound here** | CD-4 (identity minimum surface), **CD-13 (downed → stabilize, no free revives)** |

---

## 1. Purpose

A colonist is the persistent record of a person in the colony: who they are, where they are, how hurt they are, and what they carry forward from the last fight. This spec fixes **what fields exist and what they mean to the player**. It deliberately does not restate ownership — ADR-0003 is Accepted and its write-ownership table is authoritative.

The load-bearing content here is **CD-13**. The fun spike concluded that revives must cost something and that a felled colonist must carry consequence into the colony loop, because free revives would dissolve CD-4's death weight. CD-13 named Colonist Entity as the owner of *the persisted injury half*, via the ADR-0003 reconcile seam. That is what §2's C4–C7 specify.

**Explicitly out of scope** — named so they cannot drift in:

| Not owned here | Owner |
|---|---|
| Need values and their decay/satisfaction curves | Needs & Simulation (#13) |
| Job queue, claiming, priorities | Job Assignment (#10) |
| Initiative, AP, target locks | Combat side tables (#19–#23) — ADR-0003 firewall |
| The stabilize action itself (cost, range, who may perform it) | Combat set (#19–#23) |
| Whether raiders choose to shoot a downed colonist | Combat: Raider Decision-Making (#23) |
| Draft roster, `SquadRole` assignment, muster points | Squad Preparation (#24) |
| Hospital beds, medical furniture | Construction (#16) — **no furniture concept exists yet, see §8** |
| Prosthetics as items, and the surgery that fits them | Material Catalog (#5) + Construction (#16) — **no crafting chain exists yet, see §8** |
| Skill values and veterancy growth | Skill & Veterancy (#30), Vertical Slice |
| Displaying any of this | Roster UI (#28) — UI never writes stores |

---

## 2. Core Rules

### C1 — This spec adds fields; ADR-0003 assigns writers

Every field below lands in an existing ADR-0003 field group with its existing writer. Where a field needs a writer the table does not yet name, this spec says so explicitly and routes it (§8 item 1) rather than inventing one.

> **Why this shape.** ADR-0003 is **Accepted**. A quick-spec that silently re-specified ownership would create a second source of truth for the project's most-cited contract — the exact failure the RealTime-door-blocking defect demonstrated two days ago.

### C2 — Identity is three fields and they are set once (CD-4)

- `Name` — persistent, human-readable, assigned at spawn.
- `AppearanceSeed` — a `uint` drawn at spawn from the **`Colonist` RNG stream** (ADR-0005; never `Guid.NewGuid`, never `System.Random`). Views derive the whole visual from it; **no visual detail is ever stored**.
- `BattlesSurvived` — incremented only by Identity Bookkeeping at reconcile, for survivors.

Frozen under TurnBased. Roster UI displays; it never writes.

> **Why a seed and not a sprite.** CD-4's minimum identity surface has to survive a save/load round-trip and cost near-zero bytes. A seed does both, and ADR-0005 makes it bit-reproducible so the same colonist looks the same on every machine and every reload.

### C3 — Health has three living states, not two

MVP colonists are `Healthy → Downed → Dead`, plus an orthogonal **injured** condition that persists after recovery from downed.

`Hp` reaching 0 no longer means death. It means **downed** (C4). Death is a distinct, later transition.

> **Why this changes ADR-0003.** ADR-0003's health row reads *"`Hp`→0 sets `IsDead` via the same lethal write"*. CD-13 requires a state between the two, so that row needs an amendment — recorded as §8 item 1, not applied unilaterally here.

### C4 — Downed: one bleed-out clock, denominated in sim-ticks, that survives the battle

When `Hp` reaches 0, the colonist becomes **Downed** and `BleedOutRemaining` is set to `BleedOutDuration`.

**The clock is stored in sim-ticks, not turns.** That is what lets it cross the mode boundary without a conversion step:

- **TurnBased** — decrements by `TicksPerTurn` at the start of that unit's turn.
- **RealTime** — decrements by the elapsed tick each dispatch.

Same field, same unit, both authorities. A colonist downed with most of their clock left **walks out of the battle still downed and still rescuable**; the colony has whatever time remains to reach them.

1. **They are out of the fight.** No actions, no reactions, no overwatch. They still **occupy their cell** and remain a **legal target** — further hits subtract from `BleedOutRemaining` rather than restoring `Hp`.
2. **Stabilizing stops the clock permanently.** In battle a `Medic` spends actions on an adjacent downed unit; in the colony it is a tend job. Either way `BleedOutRemaining` freezes and the colonist becomes injured (C5).
3. **`BleedOutRemaining` reaching 0 is death**, in whichever mode it runs out. Nobody reached them in time.
4. **The battle ending changes nothing.** The clock keeps running into colony time; the rescue window simply continues.

Because the clock persists past the encounter, `BleedOutRemaining` and `IsDowned` are **colonist state in `ColonistStore`**, not a combat side table — they remain meaningful outside an encounter, so ADR-0003's firewall rule points them at the store. They serialize into colony saves like any other health field.

> **Why targetable, and why the clock carries over.** Targetability is what gives CD-13's tradeoff teeth — if downed colonists were safe, stabilizing could always wait until the last raider fell. But killing them at the horn would be arbitrary: a colonist with most of their clock left has not run out of time just because the shooting stopped. Carrying the clock over keeps the pressure and moves the rescue into the colony loop, where CD-13 wants the cost paid anyway. **Combined with targetability this still needs a floor — see §8 item 2.**

### C5 — Injury: severity decides the recovery path

A colonist who survives being downed is **injured**. Severity is assigned when the injury is created and decides which recovery path applies:

| Severity | Immediate effect | Recovery path | End state |
|---|---|---|---|
| **Bandaged** | Works normally at reduced movement | Time only — no bed, no carer | Full recovery to normal speed |
| **Bed-rest** | Cannot work; must occupy a bed | Bed occupancy **plus** another colonist's tending time | Released to work at reduced movement, then full recovery |
| **Lost limb** | Cannot work; must occupy a bed | Surgery **if a prosthetic is available**; otherwise none | With prosthetic: reduced movement, then full recovery. **Without: permanently very slow — this does not heal** |

Severity is set once at injury creation and never worsens on its own.

> **Why three tiers and not a wound list.** Each tier maps to a distinct *player decision*: none, a bed plus somebody's labour, or a scarce manufactured part. A per-body-part wound model multiplies content without adding decisions at MVP scale (~10 colonists, one raider type).

### C6 — `MobilityFactor` is the single mechanical expression of injury

All injury effects on capability reduce to **one** multiplier on movement speed, `MobilityFactor` (1.0 = unimpaired). Nothing else about a colonist is modified by injury in MVP.

> **Why one number.** It keeps injury out of the pathfinder's *legality* rules — a slow colonist still paths identically, just takes longer, so ADR-0003's composite-walkability contract is untouched. It also avoids scattering modifiers across systems that would each need their own tuning pass, and it is the only per-colonist mechanical variance MVP has (C8).

### C7 — Injury arrives at reconcile and lives in the colony save

Injury outcomes are carried in the **`EncounterOutcomeReport`** and applied during `PostEncounterReconcile` (ADR-0001 ordering, ADR-0003 seam — exactly the route CD-13 named). Battle-side state resolves at that moment: still-downed becomes dead, stabilized becomes injured.

`InjurySeverity`, `MobilityFactor`, `RecoveryRemaining` and `HasProsthetic` are **persistent colonist state** and serialize into colony saves. The downed counter never does.

### C8 — MVP colonists are otherwise mechanically identical

No per-colonist stat variance beyond injury. Skill values exist as dormant fields in the save format (ADR-0003) and are written by nobody until Skill & Veterancy (#30) lands at Vertical Slice.

---

## 3. Public Interface

Plain C#, `Hollowdeep.Core.Entities`, zero Godot references. Fields added to existing ADR-0003 field groups.

```csharp
public enum InjurySeverity : byte { None = 0, Bandaged = 1, BedRest = 2, LostLimb = 3 }

public enum RecoveryStage : byte { None = 0, Bedridden = 1, Impaired = 2 }

/// Health / body group (ADR-0003). RealTime writer: Needs & Simulation.
/// TurnBased writer: Combat: Targeting & Resolution.
public struct ColonistHealth
{
    public ushort         Hp;
    public bool           IsDead;
    public bool           IsDowned;        // set on Hp->0; persists across modes while the clock runs
    public int            BleedOutRemaining; // sim-ticks; 0 => dead. Decrements in BOTH authorities
    public InjurySeverity Injury;
    public RecoveryStage  Stage;
    public float          MobilityFactor;  // 1.0 = unimpaired; the ONLY injury effect (C6)
    public int            RecoveryRemaining; // sim-ticks left in the current stage; 0 = stage complete
    public bool           HasProsthetic;   // LostLimb only; false = permanent impairment
}

/// Identity group (ADR-0003). Written at spawn; BattlesSurvived by Identity Bookkeeping.
public struct ColonistIdentity
{
    public string Name;
    public uint   AppearanceSeed;   // ADR-0005 Colonist stream, drawn once at spawn
    public int    BattlesSurvived;
}
```

---

## 4. Behavior Under Each Time Authority

*(Mandatory — routing policy.)*

`ColonistStore` is a **passive store**: it registers no `ITickable` and advances nothing itself (ADR-0001 worked example). What differs by authority is *who may write* and *which clocks run*.

| | **RealTime** | **TurnBased** |
|---|---|---|
| Health writer | Needs & Simulation | Combat: Targeting & Resolution |
| `Hp` → 0 means | *(no colony-mode damage source in MVP)* | **Downed**, not dead (C4) |
| `BleedOutRemaining` | **Decrements per elapsed tick** — the rescue window continues after the battle | Decrements by `TicksPerTurn` at the downed unit's turn start |
| Stabilize | Tend job reaches the downed colonist | Medic action, adjacent |
| Recovery clock (`RecoveryRemaining`) | **Ticks** — bed rest and impairment burn colony time | **Frozen** — colony is fully paused (ADR-0001) |
| Identity | `BattlesSurvived` at reconcile only | Frozen |
| Occupancy | Advisory | Exclusive; **a downed unit still occupies its cell** — only *dead* units are removed |

**At the switch back (`PostEncounterReconcile`, ADR-0001 ordering):** drain the `EncounterOutcomeReport`; apply severity, `MobilityFactor` and `RecoveryRemaining` to colonists stabilized during the battle; increment `BattlesSurvived` for survivors; reap the dead. **Colonists still downed are left downed** with their clock intact — reconcile does not resolve them, because the rescue window is still open.

**Recovery is colony-time work, and that is the point.** Because the recovery clock runs only in RealTime and the colony is fully paused during battle, an injury's cost is paid entirely in the loop the player is trying to rebuild — which is precisely the price CD-13 asks for.

---

## 5. Dependencies

**Upstream** — Terrain Data Model (#1) for `CellCoord`. ADR-0003 for the store and writer contracts.

**Downstream** — Needs & Simulation (#13, recovery ticking + health writer); Job Assignment (#10, injured colonists' job eligibility); Colonist Movement (`MobilityFactor` applied to traversal); Combat set (#19–#23, downed/stabilize/targeting); Squad Preparation (#24, `SquadRole` incl. `Medic`); Notifications (CD-4 named death, and named survival per CD-13); Roster UI (#28, display); Save/Load (#6, persistent fields); Skill & Veterancy (#30, VS).

---

## 6. Tuning Knobs

Values live in `assets/data/colonists.json`, not hardcoded.

| Knob | Default | Range | Category | Rationale |
|---|---|---|---|---|
| `BleedOutDuration` | 6 turns' worth of ticks | 2–20 turns' worth | feel | Long enough that a medic across the room has a real chance and that the colony can finish a rescue after the battle; short enough that ignoring it is a decision. Authored in turns, stored in ticks |
| `MobilityFactor` — Bandaged | 0.75 | 0.5–1.0 | curve | Noticeably slower, still useful |
| `MobilityFactor` — post-bed-rest impaired | 0.6 | 0.4–0.9 | curve | The lingering tail after getting up |
| `MobilityFactor` — limbless, no prosthetic | 0.35 | 0.2–0.6 | gate | **Permanent.** Harsh by design: this is the consequence that makes prosthetics worth building |
| `MobilityFactor` — limbless, with prosthetic | 0.6 | 0.4–0.9 | curve | Recovers to 1.0 after the impaired period |
| `RecoveryDays` — Bandaged | 2 | 1–6 | curve | Days of reduced speed |
| `RecoveryDays` — BedRest (bedridden) | 3 | 1–8 | curve | Days occupying a bed and consuming tending |
| `RecoveryDays` — impaired tail | 2 | 0–6 | curve | Applies after bed rest and after surgery |
| `TendingHoursPerDay` | 1 | 0–4 | gate | Another colonist's labour per bedridden day — CD-13's "not-shooting tradeoff" in colony form |

All defaults are first-pass placeholders for `/balance-check`.

---

## 7. Acceptance Criteria

### (a) Headless / automated — Logic, **BLOCKING**

- [ ] **AC-1** `Hp` reaching 0 in TurnBased sets `IsDowned`, **not** `IsDead`. *(C3, C4)*
- [ ] **AC-2** A downed colonist takes no turns and remains in `UnitOccupancyIndex`; a dead one is removed. *(C4.2)*
- [ ] **AC-3** A downed colonist is a legal target; a hit while downed reduces `BleedOutRemaining` and never raises `Hp`. *(C4.1)*
- [ ] **AC-4** Stabilizing freezes the clock permanently, in either authority. *(C4.2)*
- [ ] **AC-5** `BleedOutRemaining` reaching 0 kills the colonist in **either** authority. *(C4.3)*
- [ ] **AC-5b** A colonist downed with clock remaining is **still downed and still alive** after `PostEncounterReconcile`, and their clock continues decrementing in RealTime. *(C4.4)*
- [ ] **AC-5c** Total elapsed sim-ticks to bleed out is identical whether the clock ran entirely in TurnBased, entirely in RealTime, or across a switch. *(C4 — the conversion is the unit, so there is nothing to drift)*
- [ ] **AC-6** A stabilized colonist resolves to alive with a non-`None` severity, and `BattlesSurvived` increments for them. *(C7)*
- [ ] **AC-7** `LostLimb` with no prosthetic available never reaches `MobilityFactor == 1.0`, at any elapsed time. *(C5)*
- [ ] **AC-8** `AppearanceSeed` is byte-identical across a save→load→save round-trip, and two runs from the same `RootSeed` produce identical seeds. *(C2, ADR-0005)*
- [ ] **AC-9** `IsDowned` and `BleedOutRemaining` round-trip in a colony save and in a mid-battle checkpoint. *(C4)*
- [ ] **AC-10** `RecoveryRemaining` does not change across a full TurnBased encounter. *(§4 — colony clock frozen)*
- [ ] **AC-11** A store write from the wrong authority's writer fails the debug assertion. *(ADR-0003, restated as regression cover)*

### (b) Integration — **BLOCKED on siblings; does not gate this system's Done**

- [ ] **AC-12** A bedridden colonist occupies a bed and consumes tending labour — blocked on #16 (furniture) and #10 (tend job)
- [ ] **AC-13** Surgery consumes a prosthetic from a stack — blocked on #5/#16
- [ ] **AC-14** `MobilityFactor` scales traversal time without altering path legality — blocked on Colonist Movement

### (c) Advisory / playtest

- [ ] **AC-15** A player who loses a colonist to bleed-out reports it as *their* failure to reach them, not as an arbitrary death. If it reads as arbitrary, `BleedOutTurns` is too short. *(CD-13)*
- [ ] **AC-16** A returning-to-colony player feels the labour shortage from injuries without feeling the run is over. *(CD-3 / anti-pillar)*

---

## 8. Open Questions & Routed Items

| # | Item | Routed to | Trigger |
|---|---|---|---|
| 1 | **ADR-0003 amendment.** Its health row says `Hp`→0 sets `IsDead` directly; C3/C4 insert a Downed state between them, and the health group gains seven persistent fields. Reconcile-time writer proposed as Needs & Simulation, already the RealTime health writer — no new writer needed. Applied alongside this spec | technical-director | Done with this spec |
| 2 | **Survivability floor.** Targetable downed colonists plus ~10 colonists means a bad fight can trend toward a roster wipe, which CD-3 and the anti-pillar rule out. The carry-over clock softens this a lot — most downed colonists now leave the battle alive and rescuable. The remaining gap is a raider withdraw condition, which CD-3 already assigns elsewhere | Raid Trigger (#18) + Raider Decision-Making (#23) | When the Combat set is authored |
| 3 | **Beds are furniture, and there is no furniture entry in the systems index.** Construction (#16) currently builds walls and doors | Construction (#16) | At #16's authoring |
| 4 | **Prosthetics need a production chain.** No crafting/workshop entry exists; Material Catalog's three tiers are construction materials | Material Catalog (#5) + Construction (#16) | At #16's authoring. If none ships in MVP, `LostLimb` is permanent-only — still playable |
| 5 | **Tending is a 6th job type** against the concept doc's "~5 job types" note | Job Assignment (#10) | At #10's authoring |
| 6 | **`Medic` as a `SquadRole` value** — C4.3 assumes it exists. Squad Prep owns the role set, and MVP uses fixed loadouts | Squad Preparation (#24) | Before #24. If roles stay uniform, any colonist can stabilize and CD-13's cost stays intact — a smaller change than it looks |
| 7 | **Colony-mode injury sources.** MVP has no way to be hurt outside combat, so the RealTime health writer only ever heals. If Needs & Simulation adds starvation damage, the downed state needs a RealTime meaning | Needs & Simulation (#13) | At #13's authoring |
