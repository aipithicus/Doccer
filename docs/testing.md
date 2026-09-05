# Verification & Testing Methodology

Doccer uses a high-assurance verification approach for mathematical interval algebras, graph algorithms, and rewrite materialization without runtime or framework dependencies.

---

## 1. Verification Philosophy

- **Zero-Dependency Harness**: The contract harness (`Doccer.Tests`) executes as a standalone program without third-party test framework overhead, enabling rapid, deterministic execution. Its versioned catalog exposes independently addressable cases. The separate `Doccer.TestRunner` has frozen versioned plan/run/event/artifact/receipt and fake-child contracts, expands the strict native catalog into exact case processes, and executes them with bounded parallelism and explicit exclusive barriers.
- **Algebraic Law Verification**: Where a capability has algebraic laws (associativity, distributivity, converse, identity, closure), the harness checks them through exhaustive finite censuses or independent reference oracles where tractable.
- **Oracle & Census Testing**: Complex optimization routines (such as DAG path selection or laminarization) are validated against independent, brute-force oracles and exhaustive state censuses over bounded inputs.
- **Preservation of Residue**: Tests explicitly assert that unresolved or crossing structures (e.g. crossing laminar spans, unclosed delimiters, unmapped origin slices) are captured as structured residual data rather than silently ignored.
- **Functional Placement and Naming**: Source-facing test files mirror the functional folders and
  type or operation names under `src/Doccer`. Cross-cutting checks use descriptive domains such as
  `Integration` and `Workloads`; private planning indices do not become filenames, stable case
  IDs, commands, protocols, or documentation paths.

---

## 2. Verification Categories

Verification suites in Doccer use five complementary categories:

| Category | Definition | Purpose & Invariants Verified | Examples in Doccer |
| :--- | :--- | :--- | :--- |
| **Law Tests** | Algebraic property checks. | Exercises mathematical laws across a declared finite carrier space. | • All 262,144 triples of `LocatedRelation` compositions satisfy associativity and distributivity.<br>• 169-cell `AllenRelationSet` composition table. |
| **Invariant Tests** | Structural integrity checks. | Checks that data types maintain their documented guarantees across exercised operations. | • UTF-16 surrogate pair and atom boundary safety.<br>• Master coordinate-space isolation (cross-master operations fail loudly).<br>• Bijective mapping in `TextSlice`. |
| **Oracle Tests** | Differential testing against independent mechanisms. | Compares efficient algorithms against separately implemented reference mechanisms. | • `PathSelection.Select` compared against full path enumeration on all 16,384 binary-cost graphs.<br>• `AllenCompose` compared against endpoint-predicate oracles. |
| **Exhaustive Census Tests** | Complete enumeration of an explicitly bounded finite domain. | Covers every case inside the declared model without claiming general coverage beyond that model. | • 156-plan / 430-piece materialization census.<br>• Powerset oracles over all 256 two-fact saturation programs.<br>• 1,024 structural masks and 4,096 directed graph problems. |
| **Behavioral Tests** | External contract validation. | Verifies black-box contract execution without coupling to internal representations. | • Strict-stack `Pairing.Pair` under various token streams.<br>• JSONL pattern rule inventory ingestion.<br>• Transactional regex sweep rollbacks on rule failure. |

---

## 3. Key Verification Baselines

The repository maintains specific baseline verification suites:

### A. Located Relations ($L$)
- Complete three-boundary carrier verification across all 64 values and 4,096 compositions.
- All 262,144 composition triples verified for associativity and distributivity.

### B. Allen Interval Relations ($I$)
- Validated against an independently encoded endpoint-predicate oracle across all 3,375 triples of the 15 nonempty 6-boundary intervals (169 cells, 409 atomic triads).

### C. Additive Path Selection ($K4b$)
- Validated across all 16,384 admissibility-mask and binary-cost DAG problems against complete path enumeration.

### D. Ground Saturation ($K5b$)
- Validated against powerset oracles over all 256 two-fact programs and 24 seed/support/rule permutations.

### E. Origins & Lineage ($K6$)
- Complete 16-relation / 256-pair / 4,096-triple Boolean matrix census.

### F. Materialization ($K7$)
- Complete 156-plan / 430-piece census covering reconstruction, origin/synthetic posture, exact-middle composition, and unused-source residue accounting.

### G. Cross-carrier integration and named engine workloads
- Five bounded cross-carrier witnesses cover multi-family pairing with residue, two-path ambiguity,
  adapter-budgeted chunks, fixed macro substitution with composed origins, and externally
  resource-bounded recursive expansion.
- One differential manifest fixes eleven named dense/sparse selection, validation, graph/path,
  fact/support, adjacency, vector, origin, and materialization workloads. Timing is a separate
  Release-only evidence command and never a test threshold.
- See the
  [executable recipes](recipes/cross-carrier-integration-and-engine-workloads.md) and
  [typed seam report](reports/cross-carrier-integration-and-engine-workload-baseline.md).

---

## 4. Running the Suites

To execute the catalogued engine suite through the canonical checked-in plan:

```powershell
dotnet run --project src/Doccer.TestRunner/Doccer.TestRunner.csproj -- run --plan tests/test-plan.json
```

The plan names the `Doccer.Tests` executable harness explicitly and expands its 114 catalogued cases
without crawling the `tests/` tree. This direct .NET command is the contributor and CI engine entry
point: future CI wiring must invoke it unchanged. A shell wrapper is not part of the contract. The
runner writes one bounded receipt to stdout.

The direct no-argument harness remains the serial compatibility and release check. It also emits
one bounded receipt and exposes machine-readable discovery and exact case selection:

```powershell
dotnet run --project tests/Doccer.Tests/Doccer.Tests.csproj
dotnet run --project tests/Doccer.Tests/Doccer.Tests.csproj -- list --format json
dotnet run --project tests/Doccer.Tests/Doccer.Tests.csproj -- run --case MasterTopologyIsTotal --format json
```

On failure, use progressive disclosure instead of rerunning the whole suite with verbose output:

```powershell
dotnet run --project tests/Doccer.Tests/Doccer.Tests.csproj -- run --case <stable-id> --details
```

Cross-carrier integration cases are ordinary catalog entries. Engine workload measurement is
deliberately outside the test pass/fail path; it requires Release configuration, writes versioned
JSON beneath ignored `build/workload-baselines/`, and checks every sample against an independent
result checksum:

```powershell
dotnet run --configuration Release --project tests/Doccer.Tests/Doccer.Tests.csproj -- measure-workloads --output build/workload-baselines/engine-workload-baseline.json
```

The artifact records runtime/CPU identity, warmups, repetitions, raw elapsed/allocation samples,
medians, exact workload parameters, and the no-broad-claim qualification. It contains no threshold
or accelerated-backend comparison.

To verify the TestRunner contracts, native catalog adapter, and fake-child boundary:

```powershell
dotnet run --project tests/Doccer.TestRunner.Tests/Doccer.TestRunner.Tests.csproj
# Add -- --details only after a failing receipt requires its exception stack.
```

The contract suite loads the checked-in plan and covers strict plan parsing; native catalog schema,
ordinal, identity, and concurrency validation; one-build-per-project expansion through evaluated
`TargetPath`; exact single-case argument vectors; a live canary over all 114 Doccer cases; explicit
parallel/exclusive posture; compact artifact containment and path budgets; reserved child
environment; UTC/result invariants; complete summary accounting; append-only event/lifecycle
rules; exit precedence; a bounded one-line receipt; selective detail materialization; and the
controllable fake child. It also exercises the direct-process execution boundary across a clean pass,
captured streams and artifacts, bounded output flooding, nonzero exit, timeout with descendant
termination, cancellation, and launch failure. Multi-process contracts verify the rolling worker
bound, drain-before-exclusive and resume-after-exclusive barriers, ordered aggregation, and complete
`not_started` accounting after cancellation. Native result contracts verify that clean structured
harness stdout becomes assertion metadata rather than retained detail.

To select a nondefault worker bound:

```powershell
dotnet run --project src/Doccer.TestRunner/Doccer.TestRunner.csproj -- run --plan tests/test-plan.json --max-parallel 8
```

The plan may contain `command` and `executable_harness` entries. A harness project is built once,
resolved from evaluated SDK state, and queried with `list --format json`; every catalog case becomes
an exact `dotnet exec <target> run --case <id> --format json` work item whose concurrency comes from
the catalog. Its logical identity is the unambiguous two-segment form `<source>/<case>`, with `~`
and `/` escaped as JSON Pointer segments; display names never become filesystem names. Commands
retain their plan-declared concurrency, and all expanded identities are sorted ordinally. The
runner admits parallel items through a rolling window bounded by `--max-parallel`. The option
accepts 1 through 256 and defaults to the smaller of 8 and the available processor count. An
exclusive item divides the frozen order into phases: all preceding parallel work drains, the item
runs alone, and later work is not admitted until it finishes. Results are aggregated in frozen plan
order regardless of completion order.

## 5. Test Layout and Parallel-Execution Discipline

- A test project and its ordinary source are co-located under `tests/<Project>.Tests` and included
  by the SDK's default compile rules.
- Test source is not linked from sibling domain trees, and shared helpers are not silently injected
  into every test project by directory-wide build targets.
- The stable case catalog, not physical placement or a parsed display name, identifies work for the
  runner and its scheduler.
- Parallel eligibility is explicit. A parallel case must not depend on fixed shared output paths;
  each child receives its own artifact directory. Work requiring repository-global or other shared
  mutable state is declared exclusive.
- Runner- and test-owned disposable output is confined beneath the repository's ignored `build/`
  tree; operating-system temp directories and user-profile paths are not test workspaces. The
  contract harness creates no runtime files and reads checked-in fixtures copied into the
  repository-local build output. The TestRunner contract suite deliberately exercises one fake
  child and complete run evidence beneath unique build-output directories and removes its test
  fixtures afterward. The runner redirects each child's `TMPDIR`, `TMP`, and `TEMP` variables to its
  isolated case directory. Harness build, target evaluation, and catalog discovery redirect the
  same variables to one compact `build/hx-*` workspace and remove that workspace after expansion.
- The contributor and CI entry point is one direct `dotnet` command. PowerShell glue is not part of
  the TestRunner contract.

## 6. Evidence Disclosure and Console Hygiene

The writer uses a deliberately shallow physical layout:

```text
build/test-runs/20260903T120000Z-75f0f79dd895441c/
├── plan.json
├── events.jsonl
├── summary.json
└── c/                              # absent when every case is a clean pass
    └── 0001-0123456789ab/          # only when this case has details
        ├── result.json
        ├── out.log                 # only when nonempty
        ├── err.log                 # only when nonempty
        ├── a/                      # only when artifacts exist
        └── tmp/                    # execution-only; removed at finalization
```

Run directories must be direct children of `build/test-runs/`; human display names are retained in
the plan and summary rather than repeated in physical names. Runner-managed paths have a
220-character ceiling. A child artifact path is relative to its assigned `a/` directory, contains
at most three components, limits each component to 64 characters and the relative path to 120
characters, and rejects traversal, empty components, nonportable characters, and Windows device
names. A case may retain at most 8 artifact files, 4 MiB per file, and 8 MiB total. These are
cooperation and validation rules, not an operating-system sandbox around an arbitrary executable.

A clean passing case is fully represented by its ordered result in `summary.json`, so it does not
receive a duplicate `result.json` or case directory. Nonpassing cases, nonempty captured streams,
and child artifacts materialize selective case detail. The execution workspace under `tmp/` is
always transient.

Each redirected child stream is drained without relaying it to the console. A stream retains at
most 256 KiB: for a larger stream, 64 KiB from the head and 192 KiB from the tail. Its result records
the observed byte count, retained byte count, and explicit truncation flag, so capture limits never
masquerade as complete output. `summary.json` includes direct repository-relative references for
the cases that materialized details. Build and catalog output is likewise captured rather than
relayed. A catalog must fit completely within the 256 KiB capture budget and decode as one strict
UTF-8 JSON document; truncation, stderr, noise, or an empty catalog is an expansion failure whose
console diagnostic is separately bounded. A selected native harness case must return one strict
versioned JSON result agreeing with its case ID and process exit classification. Valid result stdout
is consumed into summary status and assertion-count metadata; malformed protocol output is retained
as bounded detail and classified as an infrastructure error.

The retention pass keeps the newest 16 recognized finalized runs, ordered by the
`completedAtUtc` value in their valid summaries and then by directory name for a deterministic tie
break. Recognition requires an exact generated directory identity, matching run ID and requested
timestamp, strict summary schema and accounting, non-reparse plan/event/summary evidence, and valid
in-run detail references. Only direct inactive children outside that newest set are removed.
Active, partial, malformed, redirected, and otherwise unrecognized directories are preserved; an
active finalized run may therefore keep the root temporarily above 16 until a later invocation.
`prunedRunCount` reports only directories actually removed. Evidence that needs longer-lived
preservation must be exported deliberately before it ages out of the ignored `build/` tree.

Completed execution writes exactly one compact `doccer-test-receipt` JSON record to stdout. It is
capped at 768 characters and reports the aggregate outcome, exit code, counts, elapsed time, and a
repository-relative path to `summary.json`, plus the finalized-run prune count. Child output is
captured to case logs and is never streamed to the parent console by default. The intended agent
disclosure sequence is:

1. Read the receipt.
2. Open `summary.json` only when aggregate counts or failing case identities are needed.
3. Open `result.json`, `out.log`, `err.log`, or `a/` only for a selected case whose details matter.

Catalog listings, event streams, and collections of case logs are detail surfaces and should not be
copied wholesale into an agent conversation. The contracts and implementation use only .NET
process, argument-list, environment, path, and JSON APIs; shell choice is outside the runner.
