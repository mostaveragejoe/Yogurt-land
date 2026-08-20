# [System Name] — Quick-Spec

> **What this tier is.** A quick-spec is the middle documentation tier defined by the
> PR-SCOPE routing policy in `design/gdd/systems-index.md`: 1–2 pages covering purpose,
> rules, public interface, and acceptance criteria, for systems where an ADR and/or a Tier 0
> spike already carries the heavy design load. It is **not** an 8-section GDD, and **not** a
> `/quick-design` change-spec (those are dated, change-oriented, and bypass `/design-review`).
>
> **Section 4 is mandatory and non-negotiable** — the routing policy requires a
> "Behavior under each time authority" section in *every* simulation-bearing spec at *any*
> tier, because the mode switch is a permanent integration tax.
>
> If more than two systems routed to this tier need promotion to full GDDs during
> implementation, the routing policy says to revisit the routing scheme itself.
>
> *(Delete this block when authoring.)*

| Field | Value |
|-------|-------|
| **Systems index** | #[n] (enumeration) / #[n] (design order) — [Category], [Priority] |
| **Doc tier** | Quick-spec — [why this tier: which ADR or spike carries the load] |
| **Status** | [Drafted / Reviewed / Approved] [YYYY-MM-DD] |
| **Governing ADRs** | [ADR-xxxx (what it contributes), …] |
| **Source evidence** | [Tier 0 spike + result + path, or "none — design-only"] |
| **Depends on** | [upstream systems, with index numbers and status] |

---

## 1. Purpose

[One short paragraph: what question(s) this system answers, for whom.]

**Explicitly out of scope** — named so they cannot drift in:

| Not owned here | Owner |
|---|---|
| [concern] | [system #n / "presentation — not simulation"] |

---

## 2. Core Rules

[Numbered, unambiguous rules — C1, C2, … A programmer implements from these without asking
questions. Where a spike measured something, cite the number inline. Where a rule looks
arbitrary, add a short blockquote explaining *why* this shape and what was rejected —
future readers will otherwise re-litigate it.]

### C1 — [Rule name]

[…]

> **Why this shape.** [Rejected alternatives and the cost that ruled them out.]

---

## 3. Public Interface

[Plain C#, `Hollowdeep.Core.[Namespace]`, zero Godot references per ADR-0001/0002/0003.
Show the actual signatures. Prefer caller-supplied `Span<T>` buffers to hold the
zero-steady-state-allocation standard. Note any deliberate distinction in the API that
exists to serve a specific caller.]

```csharp
[interface / struct / enum declarations]
```

---

## 4. Behavior Under Each Time Authority

*(Mandatory — routing policy. Do not delete, do not merge into another section.)*

[State first whether this system is **passive** (registers no `ITickable`, advances no
state — the ADR-0003 store pattern) or **ticking**. Then the differences.]

| | **RealTime** | **TurnBased** |
|---|---|---|
| [behavior axis] | [rule] | [rule] |
| Callers | [systems] | [systems] |

[If a governing ADR's text and a spike's verified behavior disagree, state the verified
rule here and **flag the ADR defect** — never silently override an Accepted ADR.]

---

## 5. Dependencies

**Upstream** — [system #n (what is consumed), …]

**Downstream** — [system #n (what it consumes from here), …]

---

## 6. Tuning Knobs

Values live in `assets/data/[file].json`, not hardcoded (coding standard).

| Knob | Default | Range | Category | Rationale |
|---|---|---|---|---|
| `[Name]` | [v] | [min–max] / **fixed** | [feel / curve / gate / perf / correctness] | [why this default] |

[If any value is deliberately NOT data-driven — e.g. a correctness pair that must change
together — call that out as an explicit, justified exception to the coding standard, and
say what asserts the invariant at runtime.]

---

## 7. Acceptance Criteria

[Typed per the Testing Standards table in `.claude/docs/coding-standards.md`. Keep the
gate levels honest: Logic is BLOCKING, Visual/UI is ADVISORY, and criteria that depend on
unbuilt siblings must NOT gate this system's own Done.]

### (a) Headless / automated — Logic, **BLOCKING**

- [ ] **AC-1** [testable statement, including how it is verified]

### (b) Performance — headless benchmark, **BLOCKING regression gate**

[Set bands *above* the spike's measured values, and cite the measurement, per the
Terrain / Time Authority precedent. Allocation gates stay tight.]

- [ ] **AC-n** [metric] ≤ **[band]** *(measured [value])*

### (c) Integration — **BLOCKED on siblings; does not gate this system's Done**

- [ ] **AC-n** [criterion] — blocked on #[n]

### (d) Advisory / playtest

- [ ] **AC-n** [qualitative criterion — what a playtest, not a test runner, validates]

---

## 8. Open Questions & Routed Items

[Every deferral gets a **named trigger** — a condition that, when met, reopens it. A
deferral without a trigger is an omission wearing a decision's clothes.]

| # | Item | Routed to | Note |
|---|---|---|---|
| 1 | [question] | [system #n / agent / **Deferred**] | [trigger, or why it belongs there] |
