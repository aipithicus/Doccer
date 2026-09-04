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

# Verify the current TestRunner contracts and fake-child boundary
dotnet run --project tests/Doccer.TestRunner.Tests/Doccer.TestRunner.Tests.csproj
```

`Doccer.Tests` is an executable harness rather than a test-SDK project. `dotnet test` does not
execute its checks. Until `Doccer.TestRunner` scheduling lands, run both executable verification
projects above as the canonical repository gate. Both emit a bounded one-line receipt by default;
after a `Doccer.Tests` failure, rerun only the named case with `--details`. The TestRunner contract
suite accepts `-- --details`. Use either detail surface only after its receipt rather than flooding
the console with the whole suite's diagnostics.

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
│   └── Doccer.TestRunner/       # Process-orchestrator contract scaffold
│       └── Doccer.TestRunner.csproj
├── tests/
│   ├── Doccer.Tests/            # Catalogued contract harness and verification suite
│   │   └── Doccer.Tests.csproj
│   ├── Doccer.TestRunner.FakeChild/ # Test-only controllable process fixture
│   │   └── Doccer.TestRunner.FakeChild.csproj
│   └── Doccer.TestRunner.Tests/ # TestRunner contract verification
│       └── Doccer.TestRunner.Tests.csproj
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
current contract scaffold freezes strict versioned plan parsing, command expansion with explicit
deferred harness-source residue, immutable run/event/result models, lifecycle and aggregate exit
precedence, compact repository-contained artifact paths, bounded one-line receipts, selective case
detail retention, bounded stream/artifact capture with explicit truncation residue, and child
environment isolation. Finalized working evidence is capped at 16 run roots, with cleanup reported
in the next receipt and active or unrecognized directories excluded from pruning. The test-only
fake child can emit both streams, select an exit code, delay,
spawn a descendant, resist console cancellation, and write only beneath an assigned artifact
directory. CLI plan loading, the process executor, harness-catalog expansion, scheduling, and run
evidence writing remain unimplemented. Test cases admitted to bounded parallel execution declare
that posture explicitly; directory placement and naming organize ownership but do not imply
concurrency safety. The runner starts executables with explicit argument lists and environment
entries through .NET APIs; invoking the runner from Nu, PowerShell, Bash, an IDE, or an agent does
not change its contract.

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
