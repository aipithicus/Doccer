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
Doccer uses a dependency-free, high-assurance test runner:
```powershell
# Direct execution of the test suite
dotnet run --project tests/Doccer.Tests/Doccer.Tests.csproj
```

Or via standard `dotnet test`:
```powershell
dotnet test
```

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
│   └── Doccer/                  # Core domain-neutral C# library (zero external dependencies)
│       └── Doccer.csproj
├── tests/
│   └── Doccer.Tests/            # Standalone test runner and verification suite
│       └── Doccer.Tests.csproj
├── build/                       # Centralized build outputs (bin/ and obj/) — gitignored
└── release/                     # Packaged NuGet distributions — gitignored
```

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
- [**Governing Doctrine**](docs/architecture/doctrine.md) — Core tenets and feature admission standards.
- [**Carriers & Naming Canon**](docs/specification/carriers.md) — Many-sorted algebra ($P, L, I, C, F, O, B, U$) and naming rules.
- [**Contracts Catalog**](docs/specification/contracts.md) — Complete reference of implemented subsystems and data structures.
- [**Scope & Non-Goals**](docs/specification/non-goals.md) — Deliberately absent features and admission gate criteria.
