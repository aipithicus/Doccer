# Capability Outlook

Doccer has substantial design space beyond its current implementation. This document maps plausible directions, their present foundations, and unresolved questions.

It is not a release plan, priority order, compatibility promise, or permanent exclusion list. The list is neither exhaustive nor scheduled; items may change, move, or disappear as contracts, implementation evidence, and use cases develop.

---

## 1. Current Approach to Kernel Admission

For the current domain-neutral kernel, contract clarity and executable verification are the primary evidence for admitting a stable capability.
- Consumers provide design pressure, priorities, and realistic validation without solely determining the general engine contract.
- A clear reusable mechanism may be developed ahead of a consumer when its contract is sufficiently understood.
- A "first consumer" is a useful prioritization heuristic for unresolved shape questions, not a permanent admission rule.
- Exploratory implementations may remain outside the stable kernel while their reusable shape is investigated.

---

## 2. Candidate Directions

### `OffsetMap`
- **Current foundation**: A possible contract shape has been explored (sum-type results: `Exact | Range | Unmapped`, segment-list storage, span projection under named policies with explicit residuals).
- **Open questions**: Bidirectional projection and segment compaction need realistic pressure from a normalization or edit-plan producer.

### Byte Coordinates and Portable Typed Identity ($F2$, $F3$)
- **Current foundation**: `TextMaster` supplies exact UTF-16 coordinate identity in process; current carriers deliberately distinguish compatible-master value from exact occurrence or stage-basis identity.
- **Open questions**: F3 would map source bytes to UTF-16 under an explicit encoding and decode-error contract, independently of F1 transform coordinates. F2 would serialize separate typed basis, occurrence/entity, observation/state, policy, result, and residual stamps rather than one universal node identifier. NuShell and TeX source evidence justify bounded contract investigation; a declared wire/decoding fixture must still shape implementation.

### Hardware-Accelerated Vector Backends ($V2$)
- **Current foundation**: The portable $V1$ implementation (`BooleanVector`, `Utf16UnitMask`) provides the behavioral reference.
- **Open questions**: Word-level, SWAR, SIMD, and parallel backends would need convincing equivalence evidence against that reference, alongside a demonstrated performance need.

### HPC & Micro-Optimization Repertoire ($D41$)
- **Current foundation**: Standard memory and allocation hygiene is maintained.
- **Open questions**: Specialized allocations (SoA columnar layouts, custom worker scratchpads, bounded heaps, online reductions) are best evaluated per capability under measured profiling evidence rather than assumed as an ambient framework.

### Suppression Bitmaps
- **Current foundation**: Suppression queries (`Admitted`/`Excluded`) execute over `ClaimSelection` and `SpanSet`.
- **Open questions**: A bitmap backend would need bit-exact equivalence to reference `SpanSet` complements and a clear ownership model that avoids becoming a second semantic source of truth.

### Unicode Block & Script Classifications as Break Keys
- **Current foundation**: Atomic scalar classifications exist.
- **Open questions**: Versioned Unicode Character Database (UCD) properties need an explicit data-provenance and versioning strategy before becoming a stable kernel capability.

### Further Path & Structural Objectives
- **Current foundation**: Minimum-cost complete additive path selection is implemented ($K4b$).
- **Open questions**: Alternative objectives (partial paths, signed scores, maximum-weight paths, fewest-edge paths) would need explicit, separately named contracts rather than implicit widening of existing policies.

### Distance, Correspondence & Hashing Lanes ($F7, F8, F9$)
These lanes describe possible extensions rather than scheduled work:
- **$F7$ (Correspondence vs Origins)**: Post-hoc geometric alignment could provide correspondence evidence distinct from material origin relations unless an explicit transform establishes a stronger relation.
- **$F8$ (Similarity & Sketches)**: Direct comparison hashes ($F8a$), rolling hashes ($F8b$), similarity signatures ($F8c$), and streaming sketches ($F8d$) would need explicit error and merge contracts to become stable capabilities.
- **$F9$ (Online Views, Features & Ranking)**: Counted views ($F9a$), immutable feature artifacts ($F9b$), and ranked queries ($F9c$, such as BM25/PMI) raise open questions about vocabulary bases and tie-breaking policies.

### Observation, Boundary, and State Evidence (D48)
- **Observation evidence**: A possible instrument-stamped plane would retain producer/version/configuration, subject basis, typed result reference, diagnostics, support, and residue while allowing observations to disagree. Promotion into facts or verdicts would be explicit.
- **Boundary evidence**: Missing-token and insertion-site evidence may need a sort distinct from consuming `SpanBatch` claims and nonempty Allen intervals.
- **State-labelled transitions**: NuShell and TeX both motivate a graph whose continuation depends on explicit state as well as source position. Such a graph would remain distinct from `CandidateRegionGraph`, ASTs, and grammar DSLs and would project reachability or relation evidence only through named operations.
- **Open questions**: The first contract work must establish identity, basis, support, residual, and projection laws before any shared public carrier is assumed.

### Order Realization ($R0$-$R3$)
- **R0 problem contract**: A finite opaque entity basis, support-bearing required/forbidden pairs, asserted relation versus declared closure, optional fixed factors, represented family descriptors, resource posture, and structured residue.
- **R1 supplied verification**: Validate supplied factor representations and family membership, compare their intersection with the declared closure, and return a checkable certificate or cycle/missing/extraneous/invalid-factor evidence. A bounded exact census should precede construction.
- **R2 construction**: Admit one finite extension, fixed-factor, or partial-representation algorithm at a time; distinguish infeasible from `UnknownWithinBudget` and claim minimal obstruction only when proved.
- **R3 dimension and optimization**: Exact bounded dimension, bounds, heterogeneous factorization, embeddings, and search planning would follow verified constructive foundations. Smaller theoretical dimension is not automatically a runtime improvement.
- **Boundary**: This is a sibling of Allen algebra and qualitative constraint networks, not their replacement. Allen, observation, transition, spatial, fact, or origin evidence enters only through named projections.

### Adjacent Spatial Basis
- **Current pressure**: TeX/PDF analysis needs page, line, box, and glyph geometry joined to source evidence through explicit correspondence such as SyncTeX observations.
- **Open questions**: A domain-neutral point/rectangle basis and correspondence seam may be investigated from one bounded fixture. PDF vocabulary remains adapter-owned, and spatial geometry is never encoded as fictional one-dimensional text spans.

---

## 3. Sources of Design Evidence

Doccer draws algorithmic inspiration and lessons from prior systems and literature (including
NuShell, TeXDig, `ThermoMapper`, and interval/order-representation research):
- **Design evidence**: Donor systems provide examples of transferable mathematical patterns without determining Doccer's design or sequencing.
- **Defect learning**: Shortcomings or coupling issues in donor implementations suggest test cases, design questions, and acceptance concerns rather than immutable constraints.
- **Discovery is not dependency**: An analogy such as ThermoMapper's persistent backbone may explain how an opportunity became visible without becoming the target abstraction, implementation home, or gating policy.
