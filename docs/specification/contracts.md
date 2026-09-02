# Implemented Contract Catalog

This document catalogs contracts, data structures, and algebraic verification baselines implemented by the current **Doccer** engine. It describes the present implementation rather than pre-approving future capabilities; source and the executable verification harness provide the final evidence when documentation drifts.

---

## 1. Core Text & Coordinate Substrate

### `TextMaster` & Code-Unit Fingerprints
- Represents an immutable coordinate space over UTF-16 code units.
- Lazily computes and caches a SHA-256 fingerprint of the raw code units, guaranteeing that coordinate systems distinguish even solitary surrogate differences.
- Byte views are in-process and host-endian.

### Unicode Scalar Tiling & Line Topology
- Computes a total scalar tiling over UTF-16 code units, capturing valid scalars and explicit malformed-surrogate atoms.
- Line topology tracks exact line starts, line endings, and newline geometries.
- **Derived Run Views**: Emitted on demand under an explicit caller break-key. Atoms carry facts only; coarser grouping is a per-call view where runs carry the key they broke on.

### Slice Lineage (`TextSlice`)
- Mints a deterministic fragment-local child master over a parent window (`{parent}#{start}-{end}`).
- **Child → Parent**: Total and bijective mapping for offsets, spans, sets, located relations, and batches.
- **Parent → Child**: Partial and loud; out-of-window geometry is rejected with an exception (never silently clamped).
- Collection commutes with rebase: collecting on a fragment and rebasing to the parent is equivalent to collecting on the parent scoped to the window.

---

## 2. Vectors & Unit Masks (D46)

### `BooleanVector` ($B$)
- Basisless, immutable finite bit sequence whose identity is defined by its bit length and logical bits (not private memory packing).
- Supports bitwise operations, forward inclusive prefix parity, inverse transitions, raw chunk carry, and typed material continuity.

### `Utf16UnitMask` ($U$)
- Binds a `BooleanVector` to a compatible `TextMaster` and an exact numeric code-unit window.
- In-range window edges or set bits are directly addressable; scalar safety is enforced during atom harvest.
- Transactional claim emission emits typed claims with boundary/classifier residue.

---

## 3. Claims, Batches & Occurrence Relations

### `SpanBatch`
- Columnar, frozen, overlap-preserving storage for claim occurrences.
- String columns are interned at freeze into per-row integer IDs with a distinct-value table.

### `ClaimSelection` ($C$)
- Immutable occurrence set over a single frozen batch's ordinal universe.
- Provides set operations: `Create`, `FromPredicate`, `Union`, `Intersect`, `Subtract`, `Complement`, and ascending-ordinal enumeration.
- `Records(ClaimOrder)` projects ordered records; `Coverage()` explicitly forgets occurrence identity and normalizes selected geometry into a `SpanSet`.

### `ClaimPairView`
- Immutable occurrence relation over an ordered pair of frozen batches.
- Derives Allen interval relation labels for every pair edge.
- `ComposePairs`: Exact shared-middle ordinal join.
- `GroupMiddleWitnesses`: Returns complete, ascending, basis-stamped witness evidence for each composition.

### Strict-Stack Pairing (`Pairing.Pair`)
- Evaluates open and close token selections under a caller compatibility policy.
- Valid pairs become `ClaimPairView` match edges.
- Unmatched opens, dangling closes, and mismatched pairs remain as complete residual selections on their exact bases (`PairingResult`).
- `PairedRegions()` normalizes full delimiter envelopes into geometry, explicitly forgetting token identities.

---

## 4. Interval Algebra & Located Relations

### `SpanSet`
- Normalized, disjoint Boolean interval set bound to an originating `TextMaster`.
- Supports union, intersection, subtraction, and complement operations.

### `LocatedRelation` ($L$)
- Immutable geometry-only relation over a compatible master and an exact window.
- Canonical duplicate-collapsing extents admit diagonal empties (`[k, k)`).
- **Operations**: `Empty`, `Identity`, `Union`, `Seq` (endpoint-equality composition), `Consuming`, and `Reachability`.
- **Verification Baseline**: Checked across all 64 values and 4,096 compositions on a complete 3-boundary carrier; all 262,144 triples satisfy associativity and both distributive laws against independent oracles.

### Allen Interval Relations (`AllenRelationSet` — $I$)
- The qualitative Boolean value over the 13 Allen interval relations.
- Provides singleton constructors, set operations, pointwise converse, and canonical `AllenCompose`.
- **Verification Baseline**: Validated against an independent endpoint-predicate oracle across all 3,375 triples of the 15 nonempty 6-boundary intervals (169-cell composition table, 409 atomic triads).

---

## 5. Candidate Graph Optimization & Structural Families (K4)

### `CandidateRegionGraph`
- Holds a `ClaimSelection` supplying parallel claim-ordinal edges wholly contained in an exact window.
- `ToLocatedRelation()` explicitly projects to identity-forgetting geometry.

### K4a: Reachability & Segmentation
- `ReachabilityView`: Derives ordered forward/backward boundaries and exact dead-branch ordinals.
- `PartitionView`: Copies an ordered distinct candidate-ordinal path, validating shared endpoints, disjointness, and window coverage.
- `Segmentation.FirstOrdinalCompletePath`: Selects the lowest viable ordinal at each boundary, returning a `SegmentationResult` with either a `PartitionView` or a `SegmentationResidual` with gap and connectivity evidence.

### K4b: Additive Path Selection
- `AdditivePathPolicy`: Snapshots nonnegative `Int64` costs per candidate under named caller units with explicit minimum-additive and lexicographic stamps.
- `PathSelection.Select`: Uses a descending-boundary DAG recurrence to return the global minimum-cost complete source-graph partition or a `PathSelectionResidual`.
- **Verification Baseline**: Validated across all 16,384 admissibility-mask/binary-cost problems against complete-path enumeration.

### K4c: Structural Families & Hierarchy
- `PackingView`: Validates disjoint selections while exposing gaps.
- `CoverView`: Validates total window coverage while retaining overlap.
- `LaminarView`: Validates an exact no-proper-crossing selection.
- `Laminarizer.Admit`: Partitions candidates into accepted families and crossing residue under an `InclusionMaximal` priority policy.
- `HierarchyView`: Retains explicit acyclic evidence-labeled edges (admitting multiple parents).
- `LaminarHierarchy.NearestContainers`: Policy-gated immediate-container projection.
- `ResolutionView` & `ResolutionMap`: Multiresolution layer definitions with explicit incidence, functional aggregation, or material aggregation.

---

## 6. Canonical Facts & Ground Saturation (K5)

### K5a: Facts & Support
- `FactKey`: Immutable snapshot containing domain string, kind string, geometry extents ($L$), and canonical string-value tuples.
- `CanonicalFactTable`: Deduplicates and deterministically orders fact keys in a canonical `domain / kind / geometry / value` sequence.
- `SupportHypergraph`: Retains an exact fact table and occurrence batch with ordered rule/premise/parameter/occurrence support edges.

### K5b: Ground Saturation
- `GroundRule`: Snapshots one conclusion key plus ordered premise keys and evidence.
- `FactSaturation.Saturate`: Computes the finite positive least closure in key space over a `SaturationProblem`, remapping all enabled supports to final ordinals over the occurrence batch.
- **Verification Baseline**: Verified against powerset oracles over all 256 two-fact programs and 24 seed/support/rule permutations.

---

## 7. Origins & Lineage (K6)

- `OriginBasis`: Reference-identity ordered namespace of uniquely tagged `TextMaster` slots.
- `OriginRelation`: Canonical, finite, partial many-to-many relation over atom coordinates `(slot ordinal, atom ordinal)`.
- `ComposeOrigins`: Exact shared-middle relation composition requiring identical basis objects.
- `OriginProjection`: Retains the exact relation, selected output slot/span, and a disconnection-preserving `SpanSet` per source slot.
- **Verification Baseline**: Validated against a complete 16-relation / 256-pair / 4,096-triple Boolean matrix census.

---

## 8. Materialization (K7)

- `MaterializationTarget`: Retains output identity and slot tag.
- `OutputPiece`: Immutable positive declaration closed to:
  - Exact source copy (`PieceOrigin.Copy`).
  - Locally total origin-mapped literal (`PieceOrigin.OriginMapped`).
  - Explained synthetic literal (`PieceOrigin.Synthetic`).
- `RewritePlan`: Ordered output program validating source geometry, cumulative length, and scalar-safe boundaries.
- `RewriteMaterialization.Materialize`: Emits a new `TextMaster`, singleton output basis, gap-free reconstructing piece partition, direct K6 origin relation, and unused-source residue per input slot.
- **Verification Baseline**: Verified across a complete 156-plan / 430-piece census.

---

## 9. Query & Collection Mechanics

- **Suppression Queries**: Evaluates `Admitted` and `Excluded` queries over a selection and suppressor set. Suppression is a query property, never a baked claim flag.
- **Grouping & Projections**:
  - `Grouping.ByKey`: Groups claims deterministically by first appearance with ascending ordinals.
  - `Projection.Project`: Claim-major line ranges.
  - `Grouping.ByLine`: Line-major transpose total over line grain (`EveryLineTouched` vs `StartLineOnly`).
- **Gap Cadence**: Measures start-to-start gap statistics (count, median, mean, cv, span fraction) over a declared window basis.
- **Lookup Ordering**: `FindIntersecting` / `FindContaining` support `ClaimOrder.Geometry` (default) and `ClaimOrder.PriorityThenGeometry`.
- **Declarative Regex Collection**: `PatternRule` with load-time pattern validation, explicit whole-master or per-line execution scope, and mandatory `CultureInvariant` matching.
- **JSONL Inventory Loader**: Loads pattern rules with per-line provenance and source-generated JSON context.
