# Developer Guide

A practical guide for building, testing, and packaging the **Doccer** C# library.

---

## 1. Prerequisites & Toolchain

- **SDK**: [.NET 10.0 SDK](https://dotnet.microsoft.com/) (version pinned in [`global.json`](global.json))
- **Language**: C# 13+
- **IDE / Editor**: Visual Studio 2026, Visual Studio Code (with C# Dev Kit), JetBrains Rider, or CLI

---

## 2. Quick Workflow Commands

### Build the Solution
```powershell
dotnet build
```

### Run the Test & Verification Suite
Doccer uses a dependency-free, high-assurance executable contract harness. The checked-in plan is
the canonical process-isolated engine gate:

```powershell
# Execute all catalogued engine cases with bounded parallelism
dotnet run --project src/Doccer.TestRunner/Doccer.TestRunner.csproj -- run --plan tests/test-plan.json

# Verify the TestRunner contracts, catalog adapter, and fake-child boundary
dotnet run --project tests/Doccer.TestRunner.Tests/Doccer.TestRunner.Tests.csproj
```

`Doccer.Tests` is an executable harness rather than a test-SDK project. `dotnet test` does not
execute its checks. The plan and the TestRunner contract suite above are separate bootstrap gates:
the runner owns engine orchestration but does not use itself to decide whether its own contracts
pass. Both emit a bounded one-line receipt by default. The direct no-argument
`dotnet run --project tests/Doccer.Tests/Doccer.Tests.csproj` command remains the serial compatibility
and release check. After a planned engine failure, inspect the receipt's `summaryPath`, then rerun
only the named harness case with `--details`. The TestRunner contract suite accepts `-- --details`.
Use either detail surface only after its receipt rather than flooding the console with the whole
suite's diagnostics.

### Build a Release Package
```powershell
dotnet pack src/Doccer/Doccer.csproj -c Release -o release/
```

---

## 3. Project Structure

The repository follows a clean, modern C# SDK layout:

```text
Doccer/
├── global.json                  # Pins .NET SDK version and roll-forward policy
├── Directory.Build.props        # Centralized build properties and compiler settings
├── Doccer.slnx                  # Modern XML solution definition
├── src/
│   ├── Doccer/                  # Core domain-neutral C# library (zero external dependencies)
│   │   └── Doccer.csproj
│   └── Doccer.TestRunner/       # Repository-owned process orchestrator
│       └── Doccer.TestRunner.csproj
├── tests/
│   ├── Doccer.Tests/            # Catalogued contract harness and verification suite
│   │   └── Doccer.Tests.csproj
│   ├── Doccer.TestRunner.FakeChild/ # Test-only controllable process fixture
│   │   └── Doccer.TestRunner.FakeChild.csproj
│   ├── Doccer.TestRunner.Tests/ # TestRunner contract verification
│   │   └── Doccer.TestRunner.Tests.csproj
│   └── test-plan.json           # Canonical process-isolated engine verification plan
├── build/                       # Centralized build outputs (bin/ and obj/) — gitignored
└── release/                     # Packaged NuGet distributions — gitignored
```

Additional production or tool SDK projects belong at `src/<Project>`, with matching
verification projects at `tests/<Project>.Tests`. Register both in `Doccer.slnx`; do not introduce
a second `projects/` root. Test-only executable fixtures stay below `tests/` with their owning
project prefix and are not production packages. Keep a project's ordinary source beneath its own
project directory and let the SDK include it by default; avoid cross-tree `Compile Include` globs
and ambient build rules that silently inject shared test source.

`Doccer.TestRunner` is a direct .NET executable rather than a PowerShell-wrapped workflow. Its
current vertical slice loads a plan and expands both command entries and native executable-harness
catalogs. Each distinct harness project is built once, its `TargetPath` comes from evaluated MSBuild
state, and its bounded, strict JSON catalog becomes deterministic exact-case `dotnet exec` work
items. The build/evaluation/discovery processes share a compact repository-local `build/hx-*` temp
workspace which is removed after expansion. The runner freezes the expanded plan and uses a rolling
window of at most `--max-parallel` direct child processes. An exclusive item is a global barrier:
all preceding parallel work finishes, it runs alone, and later work waits for it. Each child retains
an explicit argument vector and environment; timeout or caller cancellation kills its process tree,
and cancellation records every unlaunched item as `not_started`. Both streams are drained into
bounded capture, child artifacts are validated, case-local temp workspaces are removed, progressive
repository-local evidence is written, and stdout receives one bounded receipt. A valid native
harness result is assimilated into summary status and assertion count rather than retained as a
redundant stdout log, so a clean, artifact-free harness run remains summary-only. The test-only fake
child can emit both streams (including a bounded flood), select an exit code, delay, spawn a
descendant, resist console cancellation, and write only beneath an assigned artifact directory.
After writing a valid summary, retention prunes recognized inactive runs outside the newest 16 and
reports the exact removal count in the receipt. Directories that are active, partial, malformed,
redirected, or otherwise unrecognized remain untouched; active finalized runs can temporarily keep
the root above the target until a later invocation. Parallel execution posture is explicit in the
catalog or command plan; directory placement and naming organize ownership but do not imply
concurrency safety. Invoking the runner from Nu, PowerShell, Bash, an IDE, or an agent does not
change its .NET process contract.

---

## 4. Build Configuration & Conventions

Build settings are standardized across projects in [`Directory.Build.props`](Directory.Build.props):

- **Centralized Output (`ArtifactsPath`)**: All compilation intermediates (`obj/`) and build outputs (`bin/`) are directed into the root `build/` directory.
- **Null Safety (`<Nullable>enable</Nullable>`)**: Strict nullable reference types are enforced.
- **Explicit Usings (`<ImplicitUsings>disable</ImplicitUsings>`)**: Implicit global usings are disabled to maintain explicit dependency hygiene.
- **Documentation (`<GenerateDocumentationFile>true</GenerateDocumentationFile>`)**: XML documentation files are generated on build to validate API doc comments.
- **Zero Runtime Dependencies**: The core `Doccer` library has no third-party package dependencies.
- **Internal Visibility**: `Doccer.csproj` grants `<InternalsVisibleTo Include="Doccer.Tests" />` solely to verify laziness without widening the public API.

---

## 5. Further Documentation

For deep technical details, refer to the documentation tree:

- [**AGENTS.md**](AGENTS.md) — Internal layer ordering and contributor invariants.
- [**Verification & Testing Methodology**](docs/testing.md) — Algebraic law testing, oracles, and verification baselines.
- [**Architecture Overview**](docs/architecture/overview.md) — Capability library vs. engine, coordinate spaces, and Unicode posture.
- [**Design Principles**](docs/architecture/design-principles.md) — Core tenets and feature admission standards.
- [**Carriers & Naming Canon**](docs/specification/carriers.md) — Many-sorted algebra ($P, L, I, C, F, O, B, U$) and naming rules.
- [**Contracts Catalog**](docs/specification/contracts.md) — Complete reference of implemented subsystems and data structures.
- [**Capability Outlook**](docs/specification/capability-outlook.md) — Possible extensions, non-goals, and admission questions.
