# Doccer

Doccer is a domain-neutral C# engine for exact interval algebra, relational structure extraction, and provenance-preserving text rewriting.

It is intentionally decoupled from document formats (Markdown, LaTeX, PDF) and runtime transports (MCP, CLI, workflows). Those systems emit claims or consume views, but none owns the interval substrate.

---

## The Two Roles

Doccer operates in two primary modes:

1. **A Public Capability Library**: Every stage and carrier is admitted as an independent primitive that can be called directly without paying for higher layers.
2. **A Declarative Composition Engine**: A policy-driven harness that composes these capabilities into recognition, laminarization, saturation, and rewrite pipelines.

```text
TextSpan / Allen relations        pure, zero dependencies
SpanSet                           + master identity
BooleanVector / Utf16UnitMask     + basisless bits and explicit UTF-16 windows
Unit classification / harvest    + uncertainty, scalar residue, claim emission
SpanBatch + ClaimSelection        + typed occurrence queries
ClaimPairView                     + exact occurrence relations
PairingResult                     + policy-stamped structural evidence
LocatedRelation                   + compatible-window geometry reachability
CandidateRegionGraph + results    + exact ordinal partition evidence
PathSelection                     + exact objective execution and decision evidence
Packing / Cover / LaminarView     + exact structural-family validation
HierarchyView / ResolutionMap     + explicit parent and layer incidence evidence
FactKey / CanonicalFactTable      + master-relative semantic fact identity
SupportHypergraph                 + exact-basis supplied support evidence
FactSaturation                    + finite positive least closure and support
OriginBasis / OriginRelation      + exact-stage output-to-source material lineage
RewritePlan / Materialize         + exact ordered output realization and residue
Scoped collectors                 + declarative recognition
Interval joins                    + structure derivation
Validation tiers / inventories    + cross-examination
```

---

## Current Design Principles

- **Claims carry evidence**: Claims, relations, and facts carry their exact basis and provenance.
- **Queries execute named policies**: Operations (suppression, admission, path selection) execute deterministic, caller-specified policies rather than hardcoded heuristics.
- **Orchestration decides meaning**: The engine provides resolution mechanisms, but orchestration determines which query to run, which policy to apply, and what the result means.
- **The engine never pre-resolves**: Ambiguity, crossing residue, and unclosed delimiters are preserved as structured residual evidence rather than silently dropped or repaired.

---

## Quick Start

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/)

### Running the Test Suite

Doccer uses a standalone, dependency-free verification harness with bounded law checks and
independent-oracle coverage. The checked-in plan is the canonical process-isolated engine gate:

```powershell
# Run all catalogued engine cases with bounded parallelism
dotnet run --project src/Doccer.TestRunner/Doccer.TestRunner.csproj -- run --plan tests/test-plan.json

# Verify the TestRunner contracts, catalog adapter, and fake-child boundary
dotnet run --project tests/Doccer.TestRunner.Tests/Doccer.TestRunner.Tests.csproj
```

The checked-in plan names the current 108-case `Doccer.Tests` catalog explicitly. The harness still
supports its no-argument serial compatibility entry point and exact single-case execution. The
separate `Doccer.TestRunner` builds and resolves each distinct executable harness project once,
validates its bounded JSON catalog, and expands every case into an exact
`dotnet exec ... run --case ...` work item. Parallel items enter a bounded rolling window; each
exclusive item drains that window, runs alone, and blocks later admission until it completes. Valid
harness result JSON is folded into ordered summary status and assertion counts, so clean passes do
not retain redundant stdout logs. Cancellation accounts for active and queued cases, and the final
console record remains one receipt pointing to repository-local evidence. After finalization, the
runner keeps the newest 16 recognized completed runs while preserving active, partial, or
unrecognized directories. The execution contract is ordinary .NET process orchestration and does
not depend on Nushell, PowerShell, or another command shell.

---

## Documentation

| Document | Description |
| :--- | :--- |
| [**DEVELOPMENT.md**](DEVELOPMENT.md) | Practical guide for building, testing, and packaging. |
| [**AGENTS.md**](AGENTS.md) | Agent orientation, layer order invariants, and working rules. |
| [**Verification & Testing**](docs/testing.md) | Verification approach, bounded law checks, and oracle and census baselines. |
| [**Architecture Overview**](docs/architecture/overview.md) | Capability library vs. engine, coordinate spaces (`TextMaster`), and Unicode posture. |
| [**Current Design Principles**](docs/architecture/design-principles.md) | Rationale for the shape of the implemented kernel and questions for evaluating additions. |
| [**Carriers & Naming Conventions**](docs/specification/carriers.md) | Many-sorted algebra ($P, L, I, C, F, O, B, U$), identity rules, and current naming conventions. |
| [**Implemented Contract Catalog**](docs/specification/contracts.md) | Current subsystem contracts, data structures, and verification baselines. |
| [**Capability Outlook**](docs/specification/capability-outlook.md) | Plausible directions and open design questions, without release commitments. |
