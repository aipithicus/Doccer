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
Doccer uses a dependency-free, high-assurance executable contract harness:
```powershell
# Execute the complete contract and law suite
dotnet run --project tests/Doccer.Tests/Doccer.Tests.csproj

# Verify the current TestRunner scaffold boundary
dotnet run --project tests/Doccer.TestRunner.Tests/Doccer.TestRunner.Tests.csproj
```

`Doccer.Tests` is an executable harness rather than a test-SDK project. `dotnet test` does not
execute its checks. Until `Doccer.TestRunner` scheduling lands, run both executable verification
projects above as the canonical repository gate.

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
│   └── Doccer.TestRunner/       # Buildable process-orchestrator scaffold
│       └── Doccer.TestRunner.csproj
├── tests/
│   ├── Doccer.Tests/            # Catalogued contract harness and verification suite
│   │   └── Doccer.Tests.csproj
│   └── Doccer.TestRunner.Tests/ # TestRunner scaffold verification
│       └── Doccer.TestRunner.Tests.csproj
├── build/                       # Centralized build outputs (bin/ and obj/) — gitignored
└── release/                     # Packaged NuGet distributions — gitignored
```

Additional production or executable SDK projects belong at `src/<Project>`, with matching
verification projects at `tests/<Project>.Tests`. Register both in `Doccer.slnx`; do not introduce
a second `projects/` root. Keep a project's ordinary source beneath its own project directory and
let the SDK include it by default; avoid cross-tree `Compile Include` globs and ambient build rules
that silently inject shared test source.

The scaffolded `Doccer.TestRunner` is a direct .NET executable rather than a PowerShell-wrapped
workflow. It currently exposes only honest help/version behavior; catalog expansion, process
scheduling, and run artifacts remain unimplemented. Test cases admitted to bounded parallel
execution declare that posture explicitly; directory placement and naming organize ownership but
do not imply concurrency safety.

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
