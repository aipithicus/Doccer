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
- `tests/Doccer.Tests/`: Standalone, dependency-free test runner and verification suite.
- `docs/`: Markdown documentation and specifications.

### Code & Dependency Standards
- **Zero External Dependencies**: `src/Doccer/Doccer.csproj` has zero runtime NuGet dependencies.
- **C# SDK Standards**: Pinned to .NET 10 (`global.json`), `<Nullable>enable</Nullable>`, `<ImplicitUsings>disable</ImplicitUsings>`.
- **Naming Conventions**: Current public engine APIs use sort-explicit operation names (e.g., `AllenCompose`, `Seq`, `ComposePairs`, `ComposeOrigins`, `PathSelection.Select`, `Laminarizer.Admit`, `FactSaturation.Saturate`, `RewriteMaterialization.Materialize`). Keep affected operation families coherent when introducing or revising names. Consult [`docs/specification/carriers.md`](docs/specification/carriers.md).

### Verification Mandate
Before concluding any implementation task or refactor, you must run and pass the full test suite:

```powershell
dotnet run --project tests/Doccer.Tests/Doccer.Tests.csproj
```
