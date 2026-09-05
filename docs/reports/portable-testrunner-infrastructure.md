# Portable Test Orchestration Infrastructure

## The Doccer TestRunner design, filesystem discipline, and a cross-language rollout guide

**Status:** implementation report and portability blueprint

**Evidence basis:** Doccer `main` working tree inspected and verified on 2026-09-05

**Audience:** maintainers who want the same process isolation, bounded evidence, and path discipline
in repositories implemented in other languages

## Executive summary

Doccer's TestRunner is not a test framework. It is a repository-owned process orchestrator and
evidence system around independently executable test harnesses.

That distinction is the core of the design:

- the **harness** owns test meaning, assertions, case IDs, and case selection;
- an **adapter** translates one language ecosystem's build and discovery rules into a frozen,
  language-neutral work-item model;
- the **runner** owns direct process launch, bounded parallelism, exclusive barriers, cancellation,
  timeouts, output capture, artifact validation, complete accounting, retention, and the final
  machine receipt;
- a separate **runner contract suite** certifies the runner without asking it to certify itself.

The reusable concept is therefore not “copy this C# program.” It is “preserve this contract stack
and implement the ecosystem-specific adapter behind it.” Doccer's current
`executable_harness` adapter is deliberately .NET-specific: it builds a `.csproj`, evaluates its
`TargetPath`, discovers a strict JSON catalog, and compiles each case to an exact
`dotnet exec ... run --case ... --format json` process. The scheduler, executor, evidence model,
filesystem rules, and disclosure model do not depend on what those arguments mean.

The system's strongest properties are:

1. **Declared work, not filesystem inference.** A checked-in plan names test sources and command
   jobs. Harnesses expose explicit stable catalogs. The runner never crawls `tests/` and guesses.
2. **One frozen intermediate representation.** Every adapter produces the same work item: stable
   identity, executable, argument vector, working directory, environment additions, timeout,
   metadata, concurrency posture, and artifact assignment.
3. **Direct processes, never shell text.** Arguments remain an array all the way to the operating
   system. PowerShell, Bash, Nushell, `cmd.exe`, and CI YAML are callers, not execution semantics.
4. **Deterministic scheduling with explicit safety.** Parallel eligibility is case metadata.
   Exclusive work is a global barrier. Results are aggregated in frozen logical order even when
   completion order differs.
5. **Bounded but honest evidence.** Streams are always drained, but only a fixed head and tail are
   retained. Counts state exactly how many bytes were observed and retained. Clean passes are
   summary-only; failures disclose progressively.
6. **Repository-local filesystem ownership.** Disposable work, temporary files, logs, and child
   artifacts stay under ignored `build/`. Logical names never become long physical paths.
7. **Complete cancellation accounting.** Active work becomes `canceled`; planned work that never
   started becomes `not_started`. Cancellation cannot shrink the apparent run total.
8. **Conservative deletion.** Retention deletes only old directories it can positively recognize
   as finalized, inactive runs. Partial, malformed, redirected, active, and unknown entries survive.

These properties are more important than Doccer's exact numeric limits. The limits are policy
choices, but they must be explicit, published to children, validated by the parent, and changed only
as a versioned contract.

## 1. What was built

### 1.1 Repository topology and ownership

Doccer keeps production orchestration, the domain harness, the process fixture, and runner
verification separate:

```text
src/
└── Doccer.TestRunner/                 production process orchestrator
    ├── Contracts/                     frozen data and status contracts
    ├── Planning/                      CLI, plan parsing, adapters, work-item expansion
    ├── Execution/                     scheduler, process lifecycle, capture, finalization
    └── Infrastructure/                path guards, evidence writer, run claims, retention

tests/
├── Doccer.Tests/                      domain contract harness and stable case catalog
├── Doccer.TestRunner.FakeChild/       controllable test-only process fixture
├── Doccer.TestRunner.Tests/           independent runner contract suite
└── test-plan.json                     checked-in canonical engine plan

build/                                 all generated and disposable state; ignored
└── test-runs/                         finalized and partial TestRunner evidence
```

The source-placement discipline matters as much as the runtime layout:

- ordinary source is owned beneath its project directory;
- test source is not linked from sibling source trees;
- directory-wide build rules do not silently inject shared test code;
- test-only executable fixtures remain under `tests/` and are not packaged as production tools;
- new production SDK projects belong under `src/<Project>` with matching verification under
  `tests/<Project>.Tests`, not under a second competing root;
- checked-in fixtures are read-only inputs; any mutable copy belongs under `build/`.

In another ecosystem, the literal `src/` and `tests/` spelling may change. Preserve the ownership
separation: runner product, domain harness, runner conformance suite, fake child, checked-in plan,
and generated evidence should remain distinguishable.

### 1.2 Architectural layers

| Layer | Owns | Must not own |
|---|---|---|
| Domain harness | assertions, test semantics, stable case catalog, exact case execution | scheduling, run retention, parent evidence |
| Language adapter | build/prepare, strict discovery, conversion to work items | scheduling policy, result aggregation, artifact layout |
| Plan compiler | strict plan parsing, adapter dispatch, ID validation, frozen ordering | test discovery by convention |
| Scheduler | rolling parallel window and exclusive barriers | process details or test meaning |
| Single-process executor | one child lifecycle, timeout/cancel race, tree termination, stream draining | multi-item scheduling or summary policy |
| Finalizer | harness-result assimilation, artifact inspection, temp cleanup, result classification | plan mutation |
| Evidence writer | immutable plan, append-only events, selective case details, final summary | child-owned artifacts |
| Retention | recognition and safe pruning of old finalized runs | cleanup of unknown or partial data |
| Runner contract suite | certifies all layers through units and real child processes | domain engine correctness |

The control flow is a small compiler followed by an executor:

```mermaid
flowchart LR
    P[Checked-in plan] --> V[Strict parser and validator]
    V --> A[Language-specific adapters]
    H[Harness catalog] --> A
    A --> W[Frozen work-item IR]
    W --> S[Bounded scheduler]
    S --> E[One-process executor]
    E --> F[Result and artifact finalizer]
    F --> R[Plan, events, summary, selective details]
    R --> C[One bounded receipt]
```

The adapter boundary is the portability seam. Everything after the frozen work-item IR should be
the same for .NET, Python, Rust, Go, Node, the JVM, or a mixed-language repository.

## 2. How the implementation was developed

The order in which the infrastructure landed is itself reusable.

### Stage 1: make the existing harness addressable

The original serial test executable was first converted from a handwritten call chain into an
immutable catalog. It retained its no-argument serial compatibility entry point, then added:

```text
<harness> list --format json
<harness> run --case <stable-id> --format json
```

This established stable identity and exact selection without changing test semantics.

### Stage 2: freeze contracts before orchestration

The plan schema, work-item shape, statuses, exit precedence, event lifecycle, artifact layout,
capture budgets, environment names, and receipt limit were encoded as types and contract tests.
A controllable fake child was added before the real coordinator.

### Stage 3: add orchestration behind those contracts

Only after the boundaries were testable did the implementation add build/discovery adaptation,
rolling scheduling, process-tree termination, bounded capture, result assimilation, atomic evidence,
and retention.

This sequence avoids a common trap: building a large concurrent runner around an unstable or
human-formatted test surface and then discovering that identity, cancellation, and artifact
ownership were never specified.

## 3. The contract stack

Doccer uses a protocol string plus `schemaVersion` on every machine document. Version 1 uses strict
camel-case JSON properties and snake-case enum values.

| Document | Protocol | Role |
|---|---|---|
| checked-in plan | `doccer-test-plan` | declares sources and commands |
| harness catalog/result | `doccer-test-harness` | language-facing discovery and exact-case result |
| frozen run snapshot | `doccer-test-run-plan` | records every concrete work item and assigned path |
| lifecycle event | `doccer-test-event` | append-only progress evidence |
| terminal item result | `doccer-test-result` | runner-owned normalized result |
| aggregate summary | `doccer-test-summary` | complete ordered finalized view |
| console receipt | `doccer-test-receipt` | bounded pointer to the summary |

Readers reject comments, trailing commas, duplicate properties, unknown properties, excessive JSON
depth, integer enum values, invalid UTF-8, and incorrect protocol/version pairs. Because unknown
fields are rejected, even an additive field requires deliberate versioning; it is not silently
ignored by an older runner.

### 3.1 Native harness catalog

An independently executable harness publishes a deterministic catalog like this:

```json
{
  "schemaVersion": 1,
  "protocol": "doccer-test-harness",
  "suiteId": "doccer.contracts",
  "cases": [
    {
      "ordinal": 1,
      "id": "MasterTopologyIsTotal",
      "displayName": "MasterTopologyIsTotal",
      "concurrency": "parallel"
    }
  ]
}
```

The catalog rules are strict:

- at least one case;
- at most 9,999 cases in the current policy;
- contiguous one-based ordinals matching list position;
- unique, trimmed, control-free case IDs;
- display names are presentation only;
- concurrency is exactly `parallel` or `exclusive`;
- one complete UTF-8 JSON object on stdout, no stderr, no logging noise, and no truncation.

Catalog order is validated, but the final multi-source execution order is the ordinal sort of the
expanded logical IDs. The plan array and child catalog array are not scheduling-order escape
hatches.

### 3.2 Exact-case harness result

The runner invokes exactly one selected case. A successful result looks like:

```json
{
  "schemaVersion": 1,
  "protocol": "doccer-test-harness",
  "suiteId": "doccer.contracts",
  "caseId": "MasterTopologyIsTotal",
  "status": "passed",
  "checkCount": 12,
  "elapsedMilliseconds": 3.417
}
```

The result must be one complete UTF-8 JSON object on stdout with empty stderr. Its `caseId` must
match the selected case, and its `passed`/`failed` status must agree with the process exit
classification. The check count is metadata, never a substitute for the number of planned cases.

When valid, the runner consumes this JSON into normalized status and assertion-count metadata and
discards the redundant stdout payload. Malformed JSON, stderr noise, a mismatched ID, a mismatched
status, or truncated output becomes `infrastructure_error`, with the bounded raw output retained as
diagnostic detail.

One version-1 limitation is worth preserving accurately: `suiteId` is validated as a well-formed
field but is not currently cross-checked against the suite ID discovered in the catalog. A shared
cross-language protocol should either bind it explicitly or document the same limitation.

### 3.3 Checked-in plan

The current schema supports two entry kinds:

- `command`: an explicit executable and argument vector; concurrency is declared by the plan;
- `executable_harness`: a .NET project whose arguments and concurrency are runner-owned or
  catalog-owned.

An illustrative current-schema plan is:

```json
{
  "schemaVersion": 1,
  "protocol": "doccer-test-plan",
  "entries": [
    {
      "id": "engine.contracts",
      "displayName": "Engine contract harness",
      "kind": "executable_harness",
      "project": "tests/Engine.Tests/Engine.Tests.csproj",
      "arguments": [],
      "workingDirectory": ".",
      "environment": {},
      "timeoutMilliseconds": 30000,
      "group": "contracts"
    },
    {
      "id": "repository.audit",
      "displayName": "Repository audit",
      "kind": "command",
      "executable": "dotnet",
      "arguments": ["run", "--project", "src/RepositoryAudit/RepositoryAudit.csproj"],
      "workingDirectory": ".",
      "environment": {},
      "timeoutMilliseconds": 60000,
      "group": "static",
      "concurrency": "exclusive"
    }
  ]
}
```

Plan paths are repository-relative and normalized before use. Working directories may be the
repository root; project files and path-bearing executables may not. Bare executable names may be
resolved through the inherited operating-system environment. Environment entries are additions to
the inherited environment, not a hermetic whitelist.

For `executable_harness`, the `.csproj` requirement and exact `dotnet` commands belong to the current
.NET adapter, not to the portable core.

### 3.4 Frozen work-item IR

Every input kind compiles to one shape:

```text
WorkItem
  id                  stable logical identity
  displayName         presentation only
  kind                command or harness case
  sourceId            declaring plan source
  sourceProject       optional adapter provenance
  executable          one program, never shell text
  arguments[]         exact ordered argv
  workingDirectory    repository-contained policy
  environment{}       explicit additions
  timeout             optional per-item bound
  group               descriptive metadata only in v1
  concurrency         parallel or exclusive
```

Harness case identity is the unambiguous two-segment form `<source>/<case>`. Literal `~` and `/`
inside either segment are escaped as `~0` and `~1`, following JSON Pointer segment escaping. All
expanded IDs must be unique and are sorted ordinally before scheduling. `group` does not affect
scheduling in version 1.

Physical case directories do not contain this logical ID. They use the frozen ordinal plus the
first six bytes of a SHA-256 digest, such as `0001-3080972a48d7`. The ordinal guarantees uniqueness
within a run; the digest provides a compact identity cue. Human display names remain in JSON.

## 4. End-to-end execution lifecycle

The current coordinator performs these phases:

1. Parse the CLI and establish the repository root, checked-in plan, configuration, and effective
   parallelism.
2. Read at most 1 MiB of strict UTF-8 plan JSON and validate every field before execution.
3. Expand command entries directly.
4. For each distinct .NET harness project, create one compact `build/hx-*` workspace, build once,
   evaluate the actual build-system `TargetPath`, and discover the strict catalog once.
5. Convert every discovered case to an exact direct-process work item; combine it with command
   items; sort by logical ID; reject duplicates or unsupported concurrency.
6. Remove the expansion workspace. At this point the catalog is frozen.
7. Reserve a unique direct child of `build/test-runs/` and keep an exclusive run claim open.
8. Atomically write the concrete `plan.json`, then create `events.jsonl`.
9. Append `run_started`, schedule work, and append item lifecycle events as work proceeds.
10. For each started item, create its case-local `tmp/`, launch one direct child, drain both streams,
    handle exit/timeout/cancellation, validate artifacts, remove temporary state, and selectively
    materialize detail.
11. Synthesize `not_started` results for every item never admitted after cancellation.
12. Append `run_finished`, derive the complete ordered summary and exit code, and atomically write
    `summary.json`.
13. Under a per-repository retention lock, prune only positively recognized inactive finalized runs
    older than the newest 16.
14. Emit one compact receipt on stdout. The run claim remains live until receipt writing completes.

Build and discovery happen before run reservation. Consequently, invalid plans, failed adapter
expansion, or cancellation during expansion do not create a partial run directory. Once reservation
has occurred, absence of `summary.json` is the durable signal that finalization did not complete.

## 5. Scheduling, cancellation, and outcome semantics

### 5.1 Frozen order and barriers

The scheduler walks the globally ID-sorted work list. A consecutive parallel segment uses a rolling
window: admit new children until the bound is full, then admit one replacement whenever one child
finishes. An exclusive item creates an empty-worker barrier:

```text
parallel segment -> drain all active work -> exclusive item alone -> later parallel segment
```

The implementation stores results by frozen index, so summary order is deterministic regardless of
completion order. `--max-parallel` accepts 1 through 256 and defaults to
`max(1, min(processor_count, 8))`.

Because order is ID-derived, changing an ID can move an exclusive barrier. If another implementation
wants plan order instead, that is a contract change, not an incidental scheduler detail.

### 5.2 Concurrency is a promise

A `parallel` case promises that process isolation plus its assigned artifact and temp directories
are sufficient. An `exclusive` case declares shared mutable state that requires the entire worker
set to be empty.

Concurrency must not be inferred from:

- folders or filenames;
- classes, modules, fully qualified names, or tags;
- assertion counts;
- current observed runtime;
- whether a test happened not to collide in one run.

Version 1 intentionally has no resource-key scheduler. Add it only when concrete pressure identifies
a real resource model; it should compile to the same frozen scheduler contract.

### 5.3 Cancellation and timeout

On caller cancellation, the runner:

1. stops admitting new work;
2. signals every active executor;
3. kills each complete child process tree;
4. waits up to the termination grace period and drains redirected output;
5. records active items as `canceled`;
6. records all remaining planned items as `not_started`;
7. writes one cancellation event and a complete summary when evidence infrastructure remains usable.

A per-item timeout kills that item tree and records `timed_out`, but does not automatically cancel
the remaining plan. Ordinary test failure also does not fail fast. There are no automatic retries:
a retry is a new run with new identity and evidence.

### 5.4 Terminal statuses and exit precedence

| Item status | Meaning |
|---|---|
| `passed` | process exit and any harness result both report success |
| `failed` | test process completed with a nonzero exit and valid failure contract |
| `canceled` | caller cancellation interrupted an active item |
| `timed_out` | the item's declared timeout elapsed |
| `not_started` | cancellation occurred before admission |
| `infrastructure_error` | launch, protocol, artifact, termination, or runner boundary failed |

| Process exit | Aggregate meaning |
|---:|---|
| 0 | every item passed |
| 1 | one or more ordinary test failures |
| 2 | invalid CLI, plan, or configuration before a valid run |
| 3 | cancellation, timeout, or not-started work |
| 4 | infrastructure failure |

Precedence is `infrastructure_error` > interruption > test failure > success. A completed run with an
item-level infrastructure error still has a JSON receipt and summary. A top-level evidence or
coordination failure may exit 4 with only bounded stderr and an incomplete run directory.

The item lifecycle allows:

```text
planned -> running -> passed | failed | canceled | timed_out | infrastructure_error
planned ----------> not_started | infrastructure_error
```

Every planned item must reach exactly one terminal result before a summary is valid.

## 6. The direct process boundary

### 6.1 No shell interpretation

Each work item retains a program and an ordered argument list. The executor uses direct process APIs
with shell execution disabled, separate stdout/stderr redirection, no visible window, and no mutation
of process-global current directory.

This prevents quoting rules from becoming part of the test contract and avoids shell injection from
concatenated command text. A plan may invoke a shell executable deliberately as its program, but the
shell and its script would then be explicit test input rather than hidden runner behavior.

### 6.2 One child lifecycle

For each item, the executor:

```text
validate cwd and path-bearing executable
create case-local temp directory
spawn child
start draining stdout and stderr concurrently
await first of exit, timeout, or cancellation
if needed, kill the entire process tree and await termination grace
finish draining both streams
return a process outcome
```

Starting stream drains immediately avoids pipe deadlock. The executor continues reading all bytes
even after the retained evidence budget is full; bounding storage must never stop drainage.

The current process-tree termination grace is 10 seconds. Equivalent implementations must test the
real operating-system behavior—Windows job/process-tree semantics, POSIX process groups, or container
cgroups—rather than assuming that killing only the direct PID is sufficient.

### 6.3 Current .NET adapter

For each distinct project path, the adapter:

1. rejects missing files and symlinks/reparse points in the repository path;
2. runs `dotnet build <project> --configuration <configuration>` once;
3. runs evaluated MSBuild property discovery for `TargetPath` rather than guessing `bin/` layout;
4. requires the resolved target to be a repository-contained existing `.dll`;
5. invokes `dotnet exec <target> list --format json`;
6. validates the complete catalog;
7. creates `dotnet exec <target> run --case <id> --format json` work items.

Build, target evaluation, and discovery use one `build/hx-*` temp workspace through redirected
`TMPDIR`, `TMP`, and `TEMP`. Their current timeouts are five minutes, one minute, and one minute.
Output is bounded and captured, never relayed. Target evaluation must produce one trimmed line;
catalog discovery must produce one complete strict JSON document with empty stderr.

Source-specific environment additions apply to case execution, not to adapter build/discovery. The
toolchain otherwise inherits the caller's environment, including normal SDK/package caches.

## 7. Filesystem conventions and path discipline

### 7.1 Canonical generated tree

```text
build/test-runs/20260905T013812Z-f36ac39e7aa14b8f/
├── plan.json
├── events.jsonl
├── summary.json
└── c/                              absent when every case is a clean pass
    └── 0001-3080972a48d7/          present only when this case has detail
        ├── result.json
        ├── out.log                 only when stdout is retained
        ├── err.log                 only when stderr is retained
        ├── a/                      only when child artifacts exist
        └── tmp/                    execution-only; always removed at finalization
```

Run directories are direct children of `build/test-runs`. The name combines a sortable UTC request
stamp with the first 16 lower-hex characters of a random 32-character run ID. An exclusive,
dot-prefixed claim file in the generated root prevents a live run from being pruned.

The shallow layout is deliberate. It bounds Windows path exposure, makes retention enumerate only
one directory level, and prevents display names or nested suite names from becoming filesystem
structure.

### 7.2 Ownership matrix

| Owner | May write | May not own |
|---|---|---|
| Runner | run directory, `plan.json`, `events.jsonl`, `summary.json`, case `result.json` and logs | domain artifacts' meaning |
| Child | assigned `a/` artifacts and assigned `tmp/` disposable work | plan, event log, normalized result, summary, sibling cases |
| Build/discovery adapter | one temporary `build/hx-*` workspace | run evidence tree |
| Human/CI caller | checked-in plan and invocation options | caller-selected run output directory |

If a child creates entries directly under its case directory outside `a/` and `tmp/`, the finalizer
removes them and classifies the item as an artifact-contract infrastructure error.

### 7.3 Containment rules

The implementation applies both lexical and physical checks:

- repository root must be absolute and normalized;
- plan, project, working-directory, and path-bearing executable inputs must resolve beneath it;
- run directories must be direct children of the generated root;
- relative paths reject rooted inputs and traversal that escapes containment;
- path equality is case-insensitive on Windows and ordinal on other systems;
- existing repository paths are checked component by component for symlinks, junctions, or other
  reparse points;
- runner-created directory components are also checked as they are created;
- recursive artifact inspection rejects reparse points and re-resolves every file canonically;
- deletion accepts an explicit containment root, refuses to delete the root itself, and does not
  recurse through a reparse-point directory;
- retention revalidates a deletion candidate immediately before removal.

String normalization alone is not a hostile-filesystem sandbox. These measures are designed for
cooperating test processes and accidental path mistakes, not an adversary racing filesystem entries.

### 7.4 Portable artifact names

A child artifact path must be relative to its assigned `a/` directory and satisfy all of these:

- at most 3 components, so at most 2 directory levels before the filename;
- at most 64 characters per component;
- at most 120 characters for the complete relative path;
- no empty, `.` or `..` component;
- no leading/trailing whitespace or trailing dot;
- no control character or `< > : " | ? *`;
- no Windows device stem such as `CON`, `PRN`, `AUX`, `NUL`, `COM1`-`COM9`, or `LPT1`-`LPT9`.

Using the portable subset even on POSIX keeps CI artifacts movable across operating systems.

### 7.5 Current numeric budgets

| Budget | Current value | Purpose |
|---|---:|---|
| checked-in plan | 1 MiB | bound pre-execution input |
| expanded work items | 9,999 | align with four-digit physical ordinals |
| runner-managed absolute path | 220 characters | fail before platform path surprises |
| run directory name | 64 characters | preserve layout headroom |
| artifact components | 3 | keep layout shallow |
| artifact component | 64 characters | portable names |
| artifact relative path | 120 characters | bound child-controlled suffix |
| artifacts per case | 8 files | bounded evidence fan-out |
| one artifact file | 4 MiB | avoid one-file explosions |
| all artifacts for one case | 8 MiB | bound aggregate storage |
| retained stdout per case | 256 KiB | 64 KiB head + 192 KiB tail |
| retained stderr per case | 256 KiB | 64 KiB head + 192 KiB tail |
| finalized runs retained | 16 | bound routine local history |
| normalized result diagnostic | 2,048 characters | bound summary/detail messages |
| adapter diagnostic | 1,024 characters | bound pre-run build/discovery errors |
| receipt or top-level error | 768 characters | bounded console surface |
| retention summary parse | 64 MiB | refuse oversized deletion candidates |

The 220-character absolute limit means checkout depth consumes the budget. Fail early and visibly;
do not silently fall back to an OS temp directory.

The stream limit is a hard retention bound because the parent controls capture. Artifact limits are
post-execution cooperation and validation rules, not filesystem quotas: a violating child may write
too much before inspection. The runner records an infrastructure error rather than pretending the
collection was accepted; hostile or quota-enforced execution needs an OS-level boundary.

### 7.6 Child environment contract

The runner reserves and supplies:

```text
DOCCER_TEST_RUN_ID
DOCCER_TEST_RUN_DIRECTORY
DOCCER_TEST_CASE_ID
DOCCER_TEST_CASE_ARTIFACT_DIRECTORY
DOCCER_TEST_CASE_TEMP_DIRECTORY
DOCCER_TEST_RUN_PLAN

DOCCER_TEST_ARTIFACT_MAX_COMPONENTS
DOCCER_TEST_ARTIFACT_MAX_COMPONENT_CHARS
DOCCER_TEST_ARTIFACT_MAX_RELATIVE_CHARS
DOCCER_TEST_ARTIFACT_MAX_FILES
DOCCER_TEST_ARTIFACT_MAX_FILE_BYTES
DOCCER_TEST_ARTIFACT_MAX_TOTAL_BYTES
DOCCER_TEST_STREAM_RETAINED_HEAD_BYTES
DOCCER_TEST_STREAM_RETAINED_TAIL_BYTES
```

It also redirects child-only `TMPDIR`, `TMP`, and `TEMP` to the assigned `tmp/`. Plan entries cannot
override these names or any `DOCCER_TEST_*` variable, including case-only differences on Windows.

Publishing limits lets a cooperative child reject oversized work before writing it; the parent still
inspects physical output afterward. The parent remains the authority.

This is not full hermetic execution. The child inherits the rest of the runner environment, may use
shared compiler/package caches, and can intentionally write elsewhere if its test code chooses to.
Untrusted code requires an additional OS sandbox, container, restricted token, namespace, job
object, or equivalent security boundary.

## 8. Bounded evidence and progressive disclosure

### 8.1 Stream capture

Each stdout and stderr stream is drained separately as bytes. For a stream larger than 256 KiB, the
runner retains the first 64 KiB and the last 192 KiB and inserts a visible truncation marker between
them. Metadata records:

```text
observedBytes
retainedBytes
truncated
```

`retainedBytes` counts child bytes, not the inserted marker. Empty streams have no capture record.
This is materially more honest than a log file whose shortened contents look complete.

Machine protocol output is stricter than diagnostic output. A catalog or harness result must fit
entirely within the capture budget, decode as strict UTF-8, and contain no unrelated stdout/stderr.
A diagnostic command may be truncated and still have a correctly classified result.

### 8.2 Evidence layers

| Layer | Write behavior | Question it answers |
|---|---|---|
| `plan.json` | atomic, before execution | What exactly was intended and assigned? |
| `events.jsonl` | append-only, sequenced, flushed per event | How far did the run get? |
| `summary.json` | atomic, after complete accounting | What was the final ordered outcome? |
| case detail | atomic and selective | What evidence matters for this one case? |
| console receipt | one compact line | Where is the bounded final evidence? |

The event stream begins at sequence 1. A normal clean run has `run_started`, one start and one finish
per case, then `run_finished`. A cancellation adds one run-level `cancellation_requested`; a
`not_started` case has a finish event without a start event, as allowed by the lifecycle.

The summary refuses missing, duplicate, foreign-run, unplanned, malformed, or out-of-run-interval
results. Counts and aggregate exit code are derived from the result collection rather than trusted
as independent inputs. Detail references are repository-relative and must correspond exactly to
results that require materialized detail.

### 8.3 Clean-pass compaction

A passing harness case whose structured stdout was successfully assimilated, with no stderr and no
artifacts, is represented entirely in `summary.json`. Its physical case directory is removed.

A case directory survives only when at least one condition holds:

- status is not `passed`;
- stdout or stderr remains diagnostically relevant;
- child artifacts exist.

This makes success cheap without erasing failure evidence.

### 8.4 Intended disclosure sequence

Automation, agents, and humans should disclose evidence in this order:

1. parse the one-line receipt;
2. open `summary.json` only when counts or failing identities are needed;
3. open one selected `result.json`, `out.log`, `err.log`, or artifact tree;
4. inspect `events.jsonl` only for lifecycle reconstruction.

Do not paste an entire catalog, event stream, or directory of logs into CI output or an agent
conversation. The point of retaining durable evidence is to avoid making the console carry it all.

### 8.5 What “provenance” does and does not mean here

The frozen plan records run ID, UTC request time, repository and run roots, configuration, effective
parallelism, concrete commands, paths, and item metadata. It does **not** currently capture Git
commit, dirty state, OS, architecture, SDK/runtime versions, or machine identity.

The plan also contains local absolute paths, whereas receipt and detail pointers are repository-
relative. The run tree is local execution evidence, not a relocatable or redacted interchange
package. If another project needs archival provenance or cross-machine export, add a versioned
environment/export contract; do not imply those properties from the current schema.

Evidence is crash-informative but not cryptographically tamper-evident. For regulated or hostile
environments, add content hashes, event hash chaining, signatures, immutable object storage, or
another explicit archival layer.

## 9. Conservative finalized-run retention

Retention runs only after a valid current summary exists and under a named per-repository lock. The
current implementation waits up to five seconds for that lock and skips pruning, reporting zero
removals, rather than delaying or failing the completed test run.

An old direct child is recognized as deletable only when:

- its directory name has the exact timestamp-plus-lower-hex identity shape;
- it is a real directory, not a reparse point;
- nonempty regular `plan.json`, `events.jsonl`, and `summary.json` files exist;
- `summary.json` is bounded, strict JSON with the supported protocol;
- the full run ID begins with the short ID in the directory name;
- the summary request timestamp agrees with the directory stamp;
- timestamps are UTC and monotonic;
- results are unique, internally valid, belong to the run, and lie inside its interval;
- recomputed counts and exit precedence match;
- every required detail reference exists under the run's `c/` tree, is unique, and names a
  nonempty `result.json` without reparse points.

The newest 16 recognized candidates are retained by summary completion time, then directory name as
a deterministic tie-break. Older candidates are re-read immediately before deletion. A live claim
prevents deletion. Only successful removals increment `prunedRunCount`.

The algorithm intentionally preserves:

- active runs;
- partial runs without summaries;
- malformed or oversized summaries;
- symlinked/redirected entries;
- arbitrary unknown directories and files;
- a finalized run whose claim is still active.

Consequently, the root may contain more than 16 directories. The number 16 is a target for
recognized inactive finalized runs, not a reason to delete unfamiliar data. Partial-run cleanup and
long-term archival are separate policies.

Retention currently parses and revalidates the summary but only checks that plan and event evidence
are regular, nonempty files; it does not replay the events or re-parse the frozen plan. Stronger
cross-file integrity would require a new contract, ideally with digests recorded in the summary.

## 10. Certifying the runner

### 10.1 The bootstrap rule

The runner must not be its own only judge. Doccer uses two ordered gates:

```powershell
# Certify runner contracts, adapters, scheduler, executor, and filesystem behavior.
dotnet run --project tests/Doccer.TestRunner.Tests/Doccer.TestRunner.Tests.csproj

# Use the certified runner to execute the canonical domain plan.
dotnet run --project src/Doccer.TestRunner/Doccer.TestRunner.csproj -- run --plan tests/test-plan.json
```

The no-argument domain harness remains a serial compatibility/release check when its catalog or
entry point changes. It is not a third duplicate on every CI change.

### 10.2 Controllable fake child

The test-only process fixture can:

- emit chosen stdout and stderr;
- flood stdout past the retention budget;
- delay for a chosen duration;
- exit with a chosen code;
- write one artifact through the assigned environment contract;
- spawn a descendant process;
- resist console cancellation so tree termination can be proved.

This fixture makes process behavior deterministic enough to test without mocking away the operating
system boundary.

### 10.3 Required contract coverage

The independent suite verifies, among other things:

- strict plan/catalog/result schemas and ambiguity rejection;
- deterministic expansion and one build per distinct harness project;
- evaluated target resolution and exact case argument vectors;
- worker bounds, exclusive non-overlap, and ordered aggregation;
- admission stopping and complete `not_started` accounting;
- pass, failure, timeout, cancellation, launch failure, and descendant termination;
- separate stream capture and head/tail truncation;
- path containment, collision-safe physical identities, environment reservations, and budgets;
- artifact traversal, depth, count, and byte rejection;
- event sequences and lifecycle transitions;
- complete summary accounting and exit precedence;
- atomic/selective evidence and bounded receipts;
- concurrent retention, active claims, malformed directories, and exact prune counts.

### 10.4 Fresh checkout evidence

On 2026-09-05, the live checkout produced:

- runner contract suite: **passed, 810 checks**;
- canonical engine plan: **passed, 114 of 114 cases**;
- effective parallelism: **8**;
- run events: **230** (`2 + 2 × 114`);
- case detail count: **0**;
- physical clean-run files: only `plan.json`, `events.jsonl`, and `summary.json`;
- runner-reported elapsed execution: **2,892.977 ms**.

These figures are an observation of that checkout, not part of the portable contract and not a
performance threshold. The live working tree contained pre-existing in-progress documentation and
later cross-carrier integration and engine-workload changes, so this is not an attestation of one
clean commit.

## 11. Cross-language portability blueprint

### 11.1 Preserve the core; replace the adapter

A portable adapter can be modeled as three operations:

```text
prepare(source, repository, expansionWorkspace) -> PreparedHarness
discover(preparedHarness)                       -> strict Catalog
compileCase(source, preparedHarness, case)      -> WorkItem
```

The adapter may know about Cargo, `pytest`, Go packages, Node reporters, Gradle, or a bespoke binary.
It must not know how to schedule, write summaries, prune runs, or choose case directories.

Every adapter should prove:

- preparation happens once per distinct source;
- tool output is bounded and captured;
- discovery is a stable machine protocol, not parsed console prose;
- exact selection executes one and only one catalog case;
- the produced command is a direct executable plus argv;
- build outputs and working directories satisfy repository policy;
- source metadata compiles to the common IR without changing runner behavior.

### 11.2 Ecosystem options

| Ecosystem | Viable adapter shape | Principal warning |
|---|---|---|
| .NET | explicit executable harness; build once; evaluate `TargetPath`; `dotnet exec` exact case | do not assume `bin/<config>` or rely on `dotnet test` for a non-SDK harness |
| Python | repository harness module or pytest plugin that emits strict JSON catalog/results and accepts exact node IDs | do not parse ordinary `--collect-only` prose; decide whether the environment is inherited or pinned |
| Rust | dedicated harness binary or stable adapter-generated manifest; build once and invoke exact IDs | standard libtest listing/output is a toolchain-facing surface, not automatically a frozen protocol |
| Go | explicit catalog wrapper or generated manifest; build package test binaries once; exact anchored selection | package/test regex selection must be escaped exactly and still needs explicit concurrency metadata |
| Node/TypeScript | repository harness with a strict JSON reporter and exact case selector; build once where applicable | TAP/human reporters may contain noise or multiple records; avoid shell-script command strings |
| JVM | small launcher using the JUnit Platform API or a repository harness that emits unique IDs and strict JSON | console discovery output and display names are not stable identities |
| Mixed language | one plan with several explicit adapter kinds plus ordinary command entries | all adapters must compile to identical result, path, and cancellation semantics |

Where the standard test platform lacks a stable machine discovery channel, an explicit repository
harness or generated catalog is preferable to fragile parsing. “The tool prints something we can
regex today” is not a protocol.

### 11.3 Recommended shared deployment model

For multiple repositories, use a hybrid model:

1. Share a **language-neutral protocol specification**, JSON schemas, status/exit tables, path test
   vectors, and a fake-child behavior corpus.
2. Keep each repository's **canonical plan and test meaning local**.
3. Implement thin **ecosystem adapters** that compile into the shared work-item IR.
4. Share orchestration libraries or a runner binary only after at least two consumers demonstrate
   that the contract is genuinely common.

This avoids two opposite failures: copying divergent runners indefinitely, and prematurely building
a universal runner that encodes the first repository's naming, toolchain, and directory assumptions.

Use a neutral namespace for genuinely shared protocols and environment variables. Do not make
`DOCCER_TEST_*`, `.csproj`, `dotnet`, or Doccer's suite vocabulary part of a universal contract.

### 11.4 Portable repository shape

One neutral layout is:

```text
tools/test-runner/                    repository-owned runner or thin entry point
tests/harness/                        domain catalog and exact-case execution
tests/test-runner-contract/           independent runner verification
tests/test-runner-fake-child/         controllable process fixture
tests/test-plan.json                  checked-in canonical plan
build/test-runs/                      ignored run evidence
build/hx-*/                           ignored, short-lived adapter workspaces
```

The spelling may follow the host language. The ownership and data flow should not.

## 12. Invariants versus policy knobs

### Keep invariant across implementations

- stable logical IDs distinct from display names and paths;
- strict versioned machine protocols;
- exact single-case execution;
- direct executable plus argv, without implicit shell interpretation;
- one frozen ordered work-item collection before scheduling;
- explicit parallel/exclusive posture;
- bounded rolling admission and true exclusive barriers;
- complete terminal accounting after cancellation;
- process-tree termination and stream drainage;
- repository-contained generated state;
- parent-assigned artifact/temp ownership;
- explicit stream and artifact truncation/budget evidence;
- immutable plan, append-only events, atomic complete summary;
- summary-first progressive disclosure;
- recognition-before-deletion retention;
- runner certification independent of the runner itself;
- no retries or timing thresholds hidden inside correctness execution.

### Tune per repository, but freeze in a contract

- maximum parallelism and default worker count;
- per-stream head/tail split;
- artifact file/count/byte budgets;
- path and component lengths;
- plan and summary size limits;
- child and adapter timeouts;
- retention count;
- protocol/environment namespace;
- whether ordinary commands may resolve executables from `PATH`;
- whether the inherited environment is allowed or replaced with a whitelist;
- which provenance fields are required;
- whether artifacts need archival export, hashing, or signatures.

Do not expose every number as an ad hoc CLI option. A runtime override can make evidence incomparable
unless the effective value is captured in the frozen plan.

## 13. Recommended rollout sequence for another repository

### Phase 0: inventory reality

- enumerate actual test entry points, not just framework declarations;
- identify which cases can be selected independently;
- inventory fixed output paths, shared mutable state, global temp usage, and subprocesses;
- distinguish repository fixtures from runtime workspaces;
- measure current serial behavior without making acceleration a functional requirement.

**Gate:** a written boundary between test semantics, adapter mechanics, and orchestration.

### Phase 1: catalog and exact selector

- assign stable IDs;
- add explicit display names and concurrency posture;
- implement strict JSON `list`;
- implement exact `run --case` with structured result;
- keep the existing serial compatibility entry point.

**Gate:** duplicate/invalid IDs fail; every listed ID runs exactly one case; the full serial catalog
still produces the same correctness result.

### Phase 2: freeze data and filesystem contracts

- define plan, IR, status, event, result, summary, and receipt schemas;
- define exit precedence and lifecycle transitions;
- define run/case tree, environment names, path rules, byte budgets, and retention posture;
- add golden valid/invalid JSON and path vectors.

**Gate:** contracts can be tested without launching the final runner.

### Phase 3: build the fake child and single-process executor

- exercise stdout, stderr, flood, delay, nonzero exit, artifacts, descendants, and cancellation;
- drain streams concurrently;
- implement timeout and process-tree termination;
- normalize one child outcome without multi-item scheduling.

**Gate:** every terminal status and capture boundary is proven against a real process.

### Phase 4: add evidence writing and finalization

- reserve unique run/case identities;
- write the plan atomically before execution;
- append flushed lifecycle events;
- validate artifacts and remove temp state;
- materialize only relevant details;
- require complete summary accounting and one bounded receipt.

**Gate:** once evidence writing begins, crashes leave bounded partial state that is preserved rather
than mistaken for a finalized run; clean passes leave no case directories.

### Phase 5: add the bounded scheduler

- schedule the frozen list by explicit posture;
- prove worker maximums and exclusive non-overlap;
- stop admission on cancellation;
- synthesize every `not_started` result;
- preserve frozen aggregation order.

**Gate:** mixed parallel/exclusive/cancel scenarios have deterministic complete summaries.

### Phase 6: add ecosystem adapters

- build or prepare each distinct source once;
- use the build system's evaluated output rather than guessing paths;
- require strict discovery output;
- compile exact commands into the common IR;
- keep adapter scratch under a compact repository-local workspace.

**Gate:** a live canary expands and runs the repository's complete catalog without filesystem crawl
or human-output parsing.

### Phase 7: add conservative retention

- lock per generated root;
- recognize only identity-bound finalized runs;
- preserve active, partial, malformed, redirected, and unknown entries;
- revalidate immediately before deletion;
- report exact removals.

**Gate:** concurrent pruning and adversarial directory fixtures cannot delete unrecognized data.

### Phase 8: install the canonical CI contract

- check in one plan;
- run the independent runner suite first;
- invoke the same runner command locally and in CI;
- upload selected failure evidence without flooding logs;
- keep benchmarking, coverage, fuzzing, and provider wiring as separate concerns.

**Gate:** CI does not add shell-specific discovery or mutate the checked-in plan.

## 14. Common mistakes this design prevents

- parsing human test output and calling it a discovery protocol;
- deriving case identity from a display name;
- using folder placement as a parallel-safety declaration;
- passing one interpolated command string to a shell;
- allowing children to choose arbitrary output roots;
- using OS temp directories and then losing evidence or portability;
- retaining only tail output without stating what was discarded;
- stopping capture when the storage cap is reached and deadlocking the child;
- reporting only started children after cancellation;
- rewriting an aggregate manifest as the only progress record;
- deleting every old-looking directory without proving it is a finalized run;
- asking the runner to be the sole certifier of its own scheduler and executor;
- silently retrying failures and erasing flakiness evidence;
- turning elapsed time into a correctness threshold;
- assuming process-per-case parallelism must be faster.

The last point is important. Doccer proved parallel capability and isolation, but its initial warm-run
performance qualification did not show the desired acceleration for the then-current catalog.
Process startup can dominate small cases. Any batching or granularity change should be measure-first
and preserve the same identity, result, cancellation, and evidence contracts.

## 15. Explicit non-goals and security boundary

The TestRunner does not:

- define the domain assertion API;
- convert domain facts into framework-specific test facts;
- parallelize test bodies inside one process;
- make shared in-process assertion state concurrent;
- provide distributed execution;
- retry flaky tests;
- perform coverage, benchmarking, fuzzing, or mutation testing;
- provide cryptographic evidence integrity;
- sandbox malicious child code;
- replace repository-audit or packaging tools;
- depend on a particular shell or CI provider;
- guarantee that parallel execution is faster than the serial harness.

Those capabilities may be adjacent consumers or plan entries. They should not blur the runner's
core responsibility: compile declared work into direct isolated processes and leave bounded,
complete, inspectable evidence.

## 16. Portable acceptance checklist

A new implementation is ready only when all of the following are true:

- [ ] Stable catalog IDs are duplicate-free and exact-selectable.
- [ ] Display names never determine identity, paths, or concurrency.
- [ ] Discovery and case results are strict, versioned, single-document machine output.
- [ ] Unknown JSON fields and ambiguous duplicate properties fail loudly.
- [ ] Every adapter compiles to one common work-item IR.
- [ ] Expanded work is globally deterministic and duplicate-free.
- [ ] Commands reach the OS as executable plus argv, without implicit shell parsing.
- [ ] Working directories and path-bearing executables are repository-contained.
- [ ] Parallel worker bounds and exclusive barriers are proved with real overlap tests.
- [ ] Cancellation kills descendants, stops admission, and accounts for queued work.
- [ ] Timeout, cancellation, failure, and infrastructure error remain distinct.
- [ ] Both process streams are always drained and have honest observed/retained counts.
- [ ] Child artifacts are path-, count-, and byte-validated after execution.
- [ ] All disposable state and redirected temp paths remain under ignored repository output.
- [ ] Clean passes produce no per-case directory.
- [ ] Plan, events, summary, result, and receipt each have a clear ownership and durability rule.
- [ ] A missing summary unambiguously means incomplete finalization.
- [ ] Aggregate counts and exit code are recomputed from exactly one result per planned item.
- [ ] Retention preserves anything it cannot positively recognize and revalidates before deletion.
- [ ] The runner's own contract suite runs independently of the runner.
- [ ] CI invokes the same checked-in plan and direct entry point contributors use locally.
- [ ] Performance claims come from separate measurement, not scheduler correctness tests.
- [ ] The documentation states whether execution is cooperative, hermetic, sandboxed, archival, or
      tamper-evident; it does not imply stronger properties than are implemented.

## 17. Doccer implementation map

The principal source anchors are:

- [`Contracts/TestPlan.cs`](../../src/Doccer.TestRunner/Contracts/TestPlan.cs) — plan entries and
  frozen work-item shape;
- [`Contracts/TestRun.cs`](../../src/Doccer.TestRunner/Contracts/TestRun.cs) — statuses, normalized
  results, complete summaries, and exit precedence;
- [`Contracts/TestRunEvent.cs`](../../src/Doccer.TestRunner/Contracts/TestRunEvent.cs) — append-only
  lifecycle events and transitions;
- [`Contracts/TestArtifacts.cs`](../../src/Doccer.TestRunner/Contracts/TestArtifacts.cs) — physical
  run/case identities and managed-path limit;
- [`Planning/TestPlanParser.cs`](../../src/Doccer.TestRunner/Planning/TestPlanParser.cs) — strict plan
  parsing and repository-relative validation;
- [`Planning/ExecutableHarnessCatalog.cs`](../../src/Doccer.TestRunner/Planning/ExecutableHarnessCatalog.cs)
  — strict native catalog;
- [`Planning/ExecutableHarnessAdapter.cs`](../../src/Doccer.TestRunner/Planning/ExecutableHarnessAdapter.cs)
  — .NET-specific build, evaluated target discovery, and work-item compilation;
- [`Execution/BoundedTestScheduler.cs`](../../src/Doccer.TestRunner/Execution/BoundedTestScheduler.cs)
  — rolling parallel segments and exclusive barriers;
- [`Execution/SingleProcessExecutor.cs`](../../src/Doccer.TestRunner/Execution/SingleProcessExecutor.cs)
  — one child lifecycle, cancellation, timeout, and tree termination;
- [`Execution/BoundedProcessStreamCapture.cs`](../../src/Doccer.TestRunner/Execution/BoundedProcessStreamCapture.cs)
  — complete drainage with retained head/tail evidence;
- [`Execution/ArtifactPathContract.cs`](../../src/Doccer.TestRunner/Execution/ArtifactPathContract.cs)
  and [`ArtifactCollectionInspector.cs`](../../src/Doccer.TestRunner/Execution/ArtifactCollectionInspector.cs)
  — portable child paths and physical artifact budgets;
- [`Execution/WorkItemFinalizer.cs`](../../src/Doccer.TestRunner/Execution/WorkItemFinalizer.cs) —
  structured result assimilation, artifact errors, and temp cleanup;
- [`Infrastructure/RunEvidenceWriter.cs`](../../src/Doccer.TestRunner/Infrastructure/RunEvidenceWriter.cs)
  — atomic and selective evidence;
- [`Infrastructure/TestRunReservation.cs`](../../src/Doccer.TestRunner/Infrastructure/TestRunReservation.cs)
  — unique run identity and active claim;
- [`Infrastructure/FinalizedRunPruner.cs`](../../src/Doccer.TestRunner/Infrastructure/FinalizedRunPruner.cs)
  — conservative recognition and retention;
- [`tests/Doccer.Tests/TestCatalog.cs`](../../tests/Doccer.Tests/TestCatalog.cs) — harness catalog,
  exact selection, and structured result surface;
- [`tests/Doccer.TestRunner.FakeChild/Program.cs`](../../tests/Doccer.TestRunner.FakeChild/Program.cs)
  — controllable process fixture;
- [`tests/Doccer.TestRunner.Tests/Program.cs`](../../tests/Doccer.TestRunner.Tests/Program.cs) —
  independent contract-suite inventory;
- [`tests/test-plan.json`](../../tests/test-plan.json) — canonical checked-in plan;
- [`docs/testing.md`](../testing.md) and [`DEVELOPMENT.md`](../../DEVELOPMENT.md) — contributor-facing
  execution, disclosure, and repository conventions.

## Conclusion

The Doccer TestRunner succeeds as infrastructure because it does less than a universal test
platform and specifies more than a shell script. Test semantics remain local; adapters absorb
toolchain differences; a small frozen IR feeds deterministic scheduling; and every process, byte,
path, status, and deletion decision has an explicit owner and bound.

For another project, begin with the harness catalog and exact selector, not with concurrency. Freeze
the evidence and filesystem contracts next. Prove the process boundary with a fake child. Only then
add scheduling and ecosystem discovery. That order produces a runner whose behavior can be trusted
across languages without importing Doccer's names or .NET assumptions.
