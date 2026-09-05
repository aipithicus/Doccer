# Cross-carrier integration and engine workload baseline

This executable integration qualification crosses the implemented carrier families without adding
a public engine type or adapter-owned semantics to `src/Doccer`. Five test-only recipes retain the
exact carrier objects needed by each operation, name every identity-forgetting projection and
residual population, and make non-composable seams observable.

The executable cases live in
`tests/Doccer.Tests/Integration/CrossCarrierIntegrationTests.cs`. Their test-local seam packets are
asserted to contain input/result sorts, exact bases, projection loss, residue, policy/resource
stamps, scale posture, portability requirements, and failed-composition evidence. The packets are
not a shadow public carrier; this report is their durable human-readable rendering.

## Multi-family pairing with residue

- **Input sorts and bases:** one `TextMaster` for `)([])(`; one exact six-row `SpanBatch`; exact
  open and close `ClaimSelection` values over that same batch; one `PairingPolicy` named
  `k8-delimiter-family`.
- **Result sorts:** `PairingResult`, identity-bearing `ClaimPairView` match edges,
  `PairingFaults`, and a projected `SpanSet`.
- **Projection:** `PairingResult.PairedRegions()` turns accepted endpoint pairs into normalized
  envelopes. The nested round and square pairs collapse to `[1,5)`, so their ordinals cannot be
  reconstructed from the `SpanSet`.
- **Residue:** close ordinal `0` is dangling; open ordinal `5` is unclosed; mismatch residue is
  empty. Accepted endpoints plus the corresponding residue exactly partition both role inputs.
- **Policy/resources/scale:** exact family-key policy; six non-overlapping occurrences; one bounded
  strict-stack reference pass. This is not a throughput claim.
- **Portability needs:** source identity, ordered claim rows, role ordinals, and a portable
  definition/version for the family-key policy. This test-only seam supplies no durable
  cross-process identity.
- **Failed composition:** there is no `SpanSet -> CandidateRegionGraph` composition that could
  preserve pair endpoints. Recollecting projected geometry would mint a new occurrence basis and
  is therefore not represented as successful composition. `ClaimPairView.ComposePairs` also
  refuses a text-compatible recollected batch as its middle basis.

## Ambiguous two-path graph

- **Input sorts and bases:** one `TextMaster` for `abc`; one exact four-row `SpanBatch`; an all-row
  `ClaimSelection`; a `CandidateRegionGraph` over `[0,3)`; and a graph-stamped
  `AdditivePathPolicy`/`PathSelectionProblem`.
- **Result sorts:** `PathSelectionResult`, `PartitionView`, selected/rejected/excluded
  `ClaimSelection` values, and the optional `LocatedRelation` projection.
- **Projection:** `CandidateRegionGraph.ToLocatedRelation()` retains the window and geometry but
  explicitly forgets candidate ordinals. The chosen `PartitionView` retains the exact ordinal
  path.
- **Residue:** the minimum-penalty path is `{1,3}` at score 2; admissible alternative path `{0,2}`
  is rejected residue; no candidate is hard-excluded and no feasibility residual exists.
- **Policy/resources/scale:** `k8-two-path-minimum-penalty`, unit `penalty-points`, minimum additive
  cost, lexicographic ordinal tie, complete-path feasibility; four candidates and exactly two
  complete paths. Reference and production-DP results are bounded here; no cross-batch invariance
  is claimed.
- **Portability needs:** source identity, ordered batch rows, graph window/candidate ordinals,
  retained costs, and the objective/tie policy definition. Durable process identity is still
  required for replay.
- **Failed composition:** a text-compatible recollection is a different `SpanBatch`. Supplying its
  selection to the original graph problem is rejected as an exact-basis mismatch.

## Budgeted flat chunks

- **Input sorts and bases:** one exact seven-row chunk `SpanBatch`; an all-candidate
  `CandidateRegionGraph` over `[0,6)`; an adapter-measured admissible `ClaimSelection`; a
  graph-stamped `AdditivePathPolicy`; and a `PathSelectionProblem`.
- **Result sorts:** `PathSelectionResult`, `PartitionView`, selected/rejected/excluded selections,
  and `PathSelectionResidual` for infeasible admission.
- **Projection:** the located-geometry projection is available but loses chunk occurrence
  identity. The result partitions stay on the exact source batch.
- **Residue:** with maximum UTF-16 span length 3, chunks `{2,6}` are hard-excluded, `{0,3,5}` are
  admitted but rejected, and `{1,4}` is selected at score 2. With maximum length 1, no complete
  path exists and the operation returns coverage residue rather than repairing the input.
- **Policy/resources/scale:** adapter measure `k8-utf16-span-length`; maximum 3; objective
  `k8-adapter-chunk-cost`; unit `penalty-points`; seven candidates. Measure and cost meanings stay
  outside the kernel. The result is exact for this bounded problem only.
- **Portability needs:** source/batch identity, measure algorithm/version, threshold, retained
  cost table, graph window, and tie policy. The test-only seam does not serialize them.
- **Failed composition:** the maximum-1 admission is explicitly a stamped failed complete-path
  result. It is not silently widened to satisfy the graph. A policy stamped by a text-compatible
  recollected graph is separately refused by the original exact-basis problem.

## Fixed macro substitution with composed origins

- **Input sorts and bases:** root `TextMaster` `say: @!`; `TextSlice` `[5,7)`; exact singleton root
  and child `OriginBasis` values; a two-piece `RewritePlan` (`OriginMapped("hi")`, then `Copy("!")`)
  over the exact child basis.
- **Result sorts:** `MaterializationResult`, new output `TextMaster` `hi!`, stage `OriginRelation`,
  composed root `OriginRelation`, unused-source `SpanSet` values, and an origin projection.
- **Projection:** projecting the composed relation over the whole output yields root region
  `[5,7)`. This normalizes atom-edge multiplicity into source regions.
- **Residue:** the plan uses every child atom, so `MaterializationResult.UnusedSources[0]` is empty.
  Root material `[0,5)` remains explicit slice-scope residue; it is not described as deletion.
- **Policy/resources/scale:** fixed table `@ -> hi`, one ordered plan, two positive pieces, one
  relational composition, and an exact `MaterializationTarget`. This is a bounded recipe rather
  than a general macro language.
- **Portability needs:** root identity, slice window, basis tags, macro table, ordered pieces,
  target identity, and origin edges. Process-stable identities must be defined before replay.
- **Failed composition:** even a value-identical clone of the child `OriginBasis` is refused as the
  shared middle of `ComposeOrigins`; the exact basis object must be reused.

## Resource-bounded recursive expansion

- **Input sorts and bases:** root `TextMaster` `${A}`; document-supplied definitions
  `A -> x${B}`, `B -> y${C}`, `C -> z`; a test-local leftmost-occurrence policy; and an exact
  per-stage `RewritePlan`/`OriginBasis` chain.
- **Result sorts:** ordered `MaterializationResult` stages, final `TextMaster`, composed root
  `OriginRelation`, and an exact final-output `ClaimSelection` of unresolved macro occurrences.
- **Projection:** exact stage relations are composed without projection. A later source-region
  projection may forget atom-edge multiplicity, but neither stage identity nor unresolved
  occurrence identity is discarded by the recipe.
- **Residue:** `MaxDepth=2` stops at `xy${C}` with one exact `${C}` occurrence. A separate
  `MaxOutputUtf16Units=4` run stops before the first expansion with `${A}` intact. Raising depth to
  3 completes as `xyz` with empty residue.
- **Policy/resources/scale:** policy `k8-leftmost-document-macro`, leftmost occurrence order,
  `MaxDepth=2`, `MaxOutputUtf16Units=32`; two performed stages in the bounded run. Recursion,
  definition lookup, and stop policy are test/adapter orchestration, not kernel semantics.
- **Portability needs:** root identity, definition bytes and lookup/parser version, policy and
  limits, stage targets, basis tags, and every origin relation. The test-only seam deliberately
  stops before durable cross-process replay identity.
- **Failed composition:** depth/output exhaustion is returned with exact unresolved-occurrence
  residue. A cloned stage basis is separately refused by exact origin composition.

## Named engine workload baseline

`tests/Doccer.Tests/Workloads/EngineWorkloadTests.cs` defines eleven fixed workloads. A normal
catalog case executes each once against its independent checksum. The Release-only
`measure-workloads` command performs three warmups and nine measured repetitions, records every
elapsed-time/allocation sample, uses the median statistic, and requires every warmup and repetition
to match its independent reference. No threshold is asserted.

The baseline below was recorded at `2026-09-04T21:47:54Z` with .NET 10.0.5, `win-x64`, Windows
10.0.26220, process architecture X64, `Intel64 Family 6 Model 170 Stepping 4, GenuineIntel`, and 18
logical processors. Allocations are per-thread deltas from
`GC.GetAllocatedBytesForCurrentThread`. Times and allocations characterize only these named
fixtures on this run.

| Workload | Posture | Median elapsed (ns) | Median allocated (bytes) | Differential |
| --- | --- | ---: | ---: | --- |
| `selection-enumeration-dense` | dense selection, 4,096/4,096 rows | 51,300 | 40 | pass |
| `selection-enumeration-sparse` | sparse selection, 16/4,096 rows | 47,500 | 40 | pass |
| `relation-validation-dense-all-pairs` | 64 x 64 relation candidates | 314,600 | 23,144 | pass |
| `graph-path-dense-alternatives` | 256-unit window, 764 admitted edges | 1,239,600 | 194,944 | pass |
| `graph-path-sparse-unit-chain` | same graph, 256 admitted edges | 765,200 | 207,712 | pass |
| `fact-saturation-sparse-chain` | 1 seed, 127 rules, 128 reached facts | 3,847,300 | 612,960 | pass |
| `support-enumeration-dense-alternatives` | 256 supports for one fact | 17,700 | 4,328 | pass |
| `hierarchy-adjacency-sparse-tree` | 512 nodes, 511 edges, 1,024 queries | 2,140,400 | 118,704 | pass |
| `boolean-vector-dense-prefix-parity` | 8,192 logical bits | 177,900 | 1,160 | pass |
| `origin-composition-dense-many-to-many` | 128 outputs, 512 composed edges | 410,400 | 141,792 | pass |
| `materialization-copy-heavy-reordered` | 512 pieces, 8,192 output units | 8,551,900 | 2,079,160 | pass |

New raw run artifacts are intentionally generated beneath ignored `build/workload-baselines/`;
the checked-in recipe explains how to regenerate them. These numbers justify no statement that one
carrier, algorithm, runtime, or machine is generally faster than another. Any accelerated,
indexed, packed, incremental, or otherwise alternative backend still needs its own named
comparison and differential gate.

## Verification at close

- Release engine-workload receipt: 11 workloads, 3 warmups, 9 repetitions, every differential
  check passed; the raw artifact was retained beneath the ignored build tree at capture time.
- Canonical process-isolated engine receipt: 114 passed, 0 failed/cancelled/timed-out/not-started/
  infrastructure-error; 2,816 assertions; no retained case details; summary
  `build/test-runs/20260904T215345Z-cbef24a1936d44ad/summary.json`.
- Direct serial compatibility receipt: 114 cases, 2,816 checks, passed.
- Independent `Doccer.TestRunner.Tests` receipt: 810 checks, passed.
- Debug build and the verified runs emitted zero compiler warnings. `src/Doccer` is unchanged; the
  closure consists of test-local orchestration, workload instrumentation, recipes, and this report.

## Closure

The five seams are representable without widening the public carrier algebra. The only rejected
paths are deliberate contract boundaries: identity-forgetting projections, exact-basis mismatch,
and explicit policy/resource exhaustion. Cross-process equivalence and durable identities remain
explicit cross-process replay and durable-identity work.
