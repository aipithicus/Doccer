# AGENTS.md — Working Guidance & Conventions

This document defines behavioral expectations, repository conventions, working culture, and context routing for AI agents operating in the **Doccer** repository.

---

## 1. Context Routing Map

Do not guess or assume architectural details—consult the relevant project source. Read each source according to its scope: contract documentation describes the current implementation, design rationale explains present choices, and the capability outlook maps possibilities without making release commitments.

| Need / Topic | Primary Reference |
| :--- | :--- |
| **Local Planning & Workflows** | [**`DocOps.md`**](./DocOps.md) (if present; private active briefs, scratchpads, ledgers) |
| **Agent Skills & Prompts** | [**`.agents/`**](./.agents/) (custom workspace skills and automation scripts) |
| **System Architecture & Design Rationale** | [**`docs/architecture/`**](docs/architecture/) (`overview.md`, `design-principles.md`) |
| **Type System & Carrier Algebra** | [**`docs/specification/carriers.md`**](docs/specification/carriers.md) (implemented sorts, identity rules, current naming conventions) |
| **Subsystem Contracts & Invariants** | [**`docs/specification/contracts.md`**](docs/specification/contracts.md) (current implementation and verification baselines) |
| **Verification & Testing Approach** | [**`docs/testing.md`**](docs/testing.md) (law checks, oracles, bounded census suites) |
| **Capability Outlook** | [**`docs/specification/capability-outlook.md`**](docs/specification/capability-outlook.md) (possible directions and open design questions, without release commitments) |
| **Build & Packaging Workflows** | [**`DEVELOPMENT.md`**](DEVELOPMENT.md) (.NET toolchain, project layout, compiler flags) |

---

## 2. Working Culture & Behavioral Guidance

- **Engine First**: Validate engine changes with unit tests, law checks, and property oracles before proposing integration wiring. Sequence engine depth ahead of consumer convenience.
- **Match the Sketched Shape**: When a design or pattern is proposed with examples, render a single unified, symmetrical shape. Do not invent ad-hoc asymmetries or unrequested abstractions.
- **Strict Core, No Compat Shims**: Pre-release evolution carries no backwards-compatibility shims or `[Obsolete]` aliases. Superseded surfaces are deleted cleanly.
- **Refactoring Discipline**: When refactoring, build new structures beside existing ones, switch call sites cleanly, and remove dead surfaces in a final cleanup sweep.
- **Preserve Residue**: Never silently clamp, drop, or auto-repair ambiguous or conflicting structures (e.g. crossing spans, unmatched delimiters, unmapped origins). Always return structured residual evidence.
- **Escalate Tool / Test Inaccuracies**: When project tooling, tests, or oracles produce false positives or confusing errors, verify the root cause in source and report it honestly—never silently work around it.

---

## 3. Repository Conventions & Instructions

### Solution Layout
- `src/Doccer/`: Domain-neutral engine library. Dependencies flow strictly downward across internal folders (`Core` → `Algebra`/`Vectors` → `Validation`/`Collector` → `Facts` → `Origins` → `Materialization`).
- `src/Doccer.TestRunner/`: Repository-owned process-orchestration executable. The current contract
  expands command entries and strict native executable-harness catalogs, building and resolving
  each distinct harness project once. It executes parallel items through a bounded rolling window
  and drains that window before running each exclusive item alone. Direct process launch,
  timeout/cancellation, complete queued-item accounting, bounded capture, evidence writing, native
  harness-result assimilation, finalized-run pruning, and one receipt are implemented.
- `tests/Doccer.Tests/`: Standalone, dependency-free contract harness and verification suite.
  Source-facing test files mirror the functional folders and type/operation names under
  `src/Doccer/`; cross-cutting checks use descriptive folders such as `Integration/` and
  `Workloads/`. Private docket or planning indices may be cited as provenance in prose, but must
  not name tracked source/test files, stable case IDs, commands, protocols, or documentation paths.
- `tests/Doccer.TestRunner.FakeChild/`: Test-only controllable child-process fixture.
- `tests/Doccer.TestRunner.Tests/`: Standalone verification for the TestRunner boundary.
- `tests/test-plan.json`: Checked-in canonical process-isolated engine verification plan.
- `docs/`: Markdown documentation and specifications.
- Additional production SDK projects belong under `src/<Project>` with matching verification under
  `tests/<Project>.Tests`; register both in `Doccer.slnx` rather than adding a parallel `projects/`
  root. Test-only executable fixtures stay under `tests/` with the owning project prefix and are
  never packaged as production tools.
- Each SDK project owns source beneath its project directory and uses default SDK compile inclusion
  unless an exceptional test asset is explicitly documented. Do not link ordinary source from a
  sibling tree or inject shared test source through ambient `Directory.Build.targets` rules.
- Parallel-test eligibility is explicit case metadata, never inferred from folders, filenames,
  classes, or assertion counts. Parallel cases must use their assigned artifact directory and must
  not write fixed shared paths; nonparallel work is declared exclusive. The frozen logical-ID order
  defines barrier position: preceding parallel work drains, the exclusive item runs alone, and only
  then may later parallel work start. `--max-parallel` accepts 1 through 256 and defaults to the
  smaller of 8 and the available processor count.
- Runner- and test-owned disposable files must remain beneath the repository's ignored `build/`
  tree. Do not use operating-system temp directories or user-profile paths for test artifacts.
  Checked-in fixtures are read-only inputs, not runtime workspaces. When the TestRunner launches a
  child, it must redirect `TMPDIR`, `TMP`, and `TEMP` to that case's repository-local work directory.
  Harness build/evaluation/discovery processes use one compact `build/hx-*` workspace and remove it
  after the catalog has been frozen.
- TestRunner run directories are direct children of `build/test-runs/` and use compact opaque
  physical identities; display names belong in JSON evidence, not paths. Runner-managed paths are
  capped at 220 characters. Child artifact paths are relative, have at most three components, cap
  each component at 64 characters and the relative path at 120 characters, and use portable names.
- Completed-run console output is one bounded JSON receipt on stdout. Child stdout and stderr are
  captured and never relayed by default. The receipt points to the repository-relative
  `summary.json`; inspect that summary before opening only the relevant case detail. Do not paste a
  whole event stream, catalog, or collection of child logs into agent context.
- Each child stream retains at most 256 KiB (64 KiB head and 192 KiB tail) while recording observed
  and retained byte counts plus explicit truncation. Per case, child artifacts are capped at 8
  files, 4 MiB per file, and 8 MiB total. Exceeding a budget is accounted evidence, never silent
  truncation or omission.
- Clean passing cases are represented in `summary.json` and do not get duplicate case directories.
  A case detail directory is retained only for a nonpassing result, nonempty captured stream, or
  child artifact. A valid native harness result is consumed into status/assertion metadata rather
  than retained as ordinary stdout. Per-case `tmp/` directories are execution workspaces and are
  removed at finalization.
- Retention keeps the newest 16 recognized finalized run directories beneath `build/test-runs/`,
  ordered by their completed-summary timestamp with a deterministic directory-name tie break. It
  considers only direct, non-reparse children with a valid identity-bound completed summary plus
  plan and event evidence. Active, partial, malformed, and otherwise unrecognized directories are
  never pruned; active finalized runs may temporarily keep the root above the target until a later
  invocation. The new run's receipt reports only directories actually removed.
- The runner is a direct .NET process/argument/environment contract. Nushell, PowerShell, Bash, and
  other shells may invoke it, but shell syntax and shell-specific pipeline behavior are not part of
  its plans, scheduling, evidence, or verification contracts.

### Code & Dependency Standards
- **Zero External Dependencies**: `src/Doccer/Doccer.csproj` has zero runtime NuGet dependencies.
- **C# SDK Standards**: Pinned to .NET 10 (`global.json`), `<Nullable>enable</Nullable>`, `<ImplicitUsings>disable</ImplicitUsings>`.
- **Naming Conventions**: Current public engine APIs use sort-explicit operation names (e.g., `AllenCompose`, `Seq`, `ComposePairs`, `ComposeOrigins`, `PathSelection.Select`, `Laminarizer.Admit`, `FactSaturation.Saturate`, `RewriteMaterialization.Materialize`). Keep affected operation families coherent when introducing or revising names. Consult [`docs/specification/carriers.md`](docs/specification/carriers.md).

### Verification Mandate
Before concluding any implementation task or refactor, you must run and pass the full test suite:

```powershell
dotnet run --project src/Doccer.TestRunner/Doccer.TestRunner.csproj -- run --plan tests/test-plan.json
dotnet run --project tests/Doccer.TestRunner.Tests/Doccer.TestRunner.Tests.csproj
```

Report these bounded receipts. The direct no-argument `Doccer.Tests` execution remains the serial
compatibility and release check when its catalog or entry point changes. If the TestRunner receipt
names a failing case, inspect its `summaryPath` before rerunning only that harness case with
`--details`; use the TestRunner contract suite's own `--details` switch when needed. Do not paste
unselected catalogs or complete log trees into context.
