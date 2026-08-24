# Test Infrastructure

| Field | Value |
|---|---|
| **Engine** | Godot 4.7.1 (project pin) |
| **Language** | C# / .NET 8 |
| **Framework** | **xUnit** — plain .NET, no engine |
| **CI** | `.github/workflows/tests.yml` |
| **Setup date** | 2026-08-24 |

## Why xUnit and not GdUnit4

The simulation core (`src/core/Hollowdeep.Core.csproj`) has **zero Godot
references by contract** — ADR-0001, ADR-0002 and ADR-0003 all depend on it, and
ADR-0002 makes it an explicit validation criterion. Every Tier 0 spike ran
headless on that basis.

So the core's whole suite runs under a bare `dotnet test` with no engine
installed: fast, free on CI, and no display server or Godot download involved.

GdUnit4 is the right tool for **engine-facing** tests — views, GridMap
behaviour, scene wiring — and belongs in a separate project when those arrive.
It is deliberately not set up yet, because nothing engine-facing exists to test
and the project's own standards class Visual/Feel and UI evidence as ADVISORY.

## Layout

```
tests/
  Hollowdeep.Tests.csproj   xUnit project, references Hollowdeep.Core
  unit/                     isolated logic - formulas, state machines, primitives
  integration/              cross-system, save/load round-trips
  smoke/critical-paths.md   the /smoke-check gate list
  evidence/                 screenshots and manual sign-off records
```

## Running

```bash
dotnet test tests/Hollowdeep.Tests.csproj          # the suite
dotnet test tests/Hollowdeep.Tests.csproj -v n     # verbose
dotnet test --filter "FullyQualifiedName~CellCoord"  # one class
```

From the repo root, `dotnet test Hollowdeep.sln` also works but additionally
builds the Godot-facing project, which needs the Godot SDK restored.

## Naming

- **Files**: `[Thing]Tests.cs` — matches the .NET convention the analyzers expect
- **Methods**: `test_[scenario]_[expected]` — the project standard in
  `.claude/docs/coding-standards.md`
- **Namespace**: mirrors the folder, e.g. `Hollowdeep.Tests.Unit.Primitives`

The two conventions collide slightly: .NET tooling expects PascalCase methods,
the project standard specifies `test_snake_case`. The project standard wins —
it is what `/story-readiness` and `/test-evidence-review` look for.

## Determinism

Non-negotiable per `.claude/docs/coding-standards.md`:

- No wall-clock dependencies, no `DateTime.Now` in assertions
- No unseeded randomness — all RNG goes through `SeededRngStore` (ADR-0005)
- No file or network I/O in unit tests
- No inter-test ordering dependencies
- `InvariantGlobalization` is on in the csproj so locale cannot change results

## Story type → required evidence

| Story type | Evidence | Location | Gate |
|---|---|---|---|
| Logic | Automated unit test, must pass | `tests/unit/[system]/` | **BLOCKING** |
| Integration | Integration test or documented playtest | `tests/integration/[system]/` | **BLOCKING** |
| Visual/Feel | Screenshot + lead sign-off | `tests/evidence/` | Advisory |
| UI | Manual walkthrough or interaction test | `tests/evidence/` | Advisory |
| Config/Data | Smoke check pass | `production/qa/smoke-[date].md` | Advisory |

## CI jobs

`tests.yml` runs two jobs on every push to `main` and every PR:

1. **Core Unit + Integration Tests** — restore, build, `dotnet test`
2. **Architecture Grep Gates** — fails the build if the core gains a Godot
   reference (ADR-0001/0002/0003) or any stock/engine RNG (ADR-0005)

The grep gates turn two rules that previously lived only in prose into
build failures. Both skip comment lines on purpose: the core's doc comments
legitimately discuss the Godot boundary, and a naive match false-positives on
three of them today.
