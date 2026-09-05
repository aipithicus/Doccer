# Cross-carrier integration and engine workload recipes

These recipes exercise the cross-carrier integration witnesses and produce a mechanics-grade
engine workload artifact. They use the repository-owned executable harness; `dotnet test` does
not run it.

## Run the five cross-carrier integration witnesses

From the repository root:

```powershell
dotnet run --project tests/Doccer.Tests/Doccer.Tests.csproj -- run --case CrossCarrierMultiFamilyPairingRetainsResidueAndReportsSeam --format json
dotnet run --project tests/Doccer.Tests/Doccer.Tests.csproj -- run --case CrossCarrierAmbiguousTwoPathGraphRetainsPoliciesAndReportsSeam --format json
dotnet run --project tests/Doccer.Tests/Doccer.Tests.csproj -- run --case CrossCarrierBudgetedChunksRetainAdapterMeasureCostAndReportsSeam --format json
dotnet run --project tests/Doccer.Tests/Doccer.Tests.csproj -- run --case CrossCarrierFixedMacroSubstitutionComposesOriginsAndReportsSeam --format json
dotnet run --project tests/Doccer.Tests/Doccer.Tests.csproj -- run --case CrossCarrierRecursiveExpansionStopsAtResourceBoundaryAndReportsSeam --format json
```

Each case verifies actual carrier stamps and outcomes as well as a test-local seam packet. The
packet fields are rendered in the
[cross-carrier integration and engine workload report](../reports/cross-carrier-integration-and-engine-workload-baseline.md).

## Verify the engine workload manifest

```powershell
dotnet run --project tests/Doccer.Tests/Doccer.Tests.csproj -- run --case EngineWorkloadManifestIsBoundedAndDifferential --format json
```

This is a correctness check, not a timed assertion. It freezes the eleven workload IDs and their
parameters, covers dense and sparse postures, and compares every operation with an independent
checksum.

## Record an engine workload baseline

Use a Release build and keep generated evidence beneath the ignored repository `build/` tree:

```powershell
dotnet run --configuration Release --project tests/Doccer.Tests/Doccer.Tests.csproj -- measure-workloads --output build/workload-baselines/engine-workload-baseline.json
```

The command refuses Debug builds and output paths outside `build/`. Its versioned JSON contains:

- the UTC recording time, Release configuration, .NET/runtime/OS/CPU stamps, logical processor
  count, and stopwatch frequency;
- the fixed three-warmup/nine-repetition policy;
- every elapsed-nanosecond and allocated-byte sample plus their medians;
- exact workload parameters and scale posture; and
- independent expected/observed checksums with a differential pass flag.

There are no performance thresholds and no comparison backend. A result describes only the named
workload, machine, runtime, and run captured in that file.

## Run the repository gates

```powershell
dotnet run --project src/Doccer.TestRunner/Doccer.TestRunner.csproj -- run --plan tests/test-plan.json
dotnet run --project tests/Doccer.TestRunner.Tests/Doccer.TestRunner.Tests.csproj
```

The first command expands and runs every catalogued engine case. The second independently verifies
the runner, native catalog adapter, scheduling, evidence, and fake-child boundary.
