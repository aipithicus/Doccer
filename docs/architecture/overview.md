# Architecture Overview

Doccer is a domain-neutral C# engine for exact interval algebra, relational structure extraction, and provenance-preserving text rewriting.

Domain-neutral does not mean free of mathematical or operational semantics. The engine may own
strong typed mechanisms—such as pairing, path optimization, closure, provenance, or future
realization verification—while adapters retain format vocabulary, interpretation, preferences,
and workflow consequences.

---

## 1. Capability Library vs. Declarative Engine

Doccer serves two distinct in-process roles:

1. **A Public Capability Library**: An à la carte suite of primitive carriers, algebras, and queries. Each stage can be invoked directly without instantiating higher-level engine machinery.
2. **A Declarative Composition Engine**: An orchestrator that composes these capabilities under caller-specified policies to perform parsing, laminarization, path selection, ground fact saturation, and materialization.

Construction cost scales strictly with what a workload touches. "Full Doccer" (sweep → collect → validate → laminarize → tiered acceptance) is one composition of these primitives, never the required entry price.

### The 18-Layer Rung Ladder

```text
TextSpan / Allen relations        pure, zero dependencies
SpanSet                           + master identity
BooleanVector / Utf16UnitMask     + basisless bits and explicit UTF-16 windows
Unit classification / harvest    + uncertainty, scalar residue, claim emission
SpanBatch + ClaimSelection        + typed occurrence queries
ClaimPairView                     + exact occurrence relations
PairingResult                     + policy-stamped structural evidence
LocatedRelation                   + compatible-window geometry reachability
CandidateRegionGraph + results    + exact ordinal partition evidence
PathSelection                     + exact objective execution and decision evidence
Packing / Cover / LaminarView     + exact structural-family validation
HierarchyView / ResolutionMap     + explicit parent and layer incidence evidence
FactKey / CanonicalFactTable      + master-relative semantic fact identity
SupportHypergraph                 + exact-basis supplied support evidence
FactSaturation                    + finite positive least closure and support
OriginBasis / OriginRelation      + exact-stage output-to-source material lineage
RewritePlan / Materialize         + exact ordered output realization and residue
Scoped collectors                 + declarative recognition
Interval joins                    + structure derivation
Validation tiers / inventories    + cross-examination
```

---

## 2. Coordinate Spaces & `TextMaster`

In Doccer, a document is not an ambient global string. A **`TextMaster`** represents an explicit, immutable coordinate space.

- **Fingerprint & Lazy Topology**: A `TextMaster` lazily computes a SHA-256 fingerprint of its raw UTF-16 code units upon first use. Line topology and scalar tiling are also computed on demand and cached.
- **Strict Identity Floor**: Spans (`TextSpan`) and span sets (`SpanSet`) are bound to a specific `TextMaster`. Operations between geometry bound to different masters fail loudly. Coordinate-space confusion is caught immediately rather than causing silent offset corruption.
- **Slice Lineage (`TextSlice`)**: Slices mint a deterministic fragment-local child master over a parent window (`{parent}#{start}-{end}`). Slicing is strictly opt-in: child-to-parent mapping is total and bijective; parent-to-child mapping is partial and loud (out-of-window geometry is rejected).

---

## 3. Unicode Stance

Doccer maintains a strict, literal posture toward text representation:

- **No Ambient Normalization**: Text is analyzed exactly as given. Identity is the default form.
- **Explicit Transforms**: Normalization forms (NFC, NFD, NFKC, NFKD) are treated as lossy transforms produced by explicit producer steps that emit a new `TextMaster`. A future F1 `OffsetMap` may provide a restricted coordinate-query view over such a performed transform; it is not part of the current contract.
- **Surrogate Pair Safety**: Malformed or unpaired surrogates are represented as explicit atomic tiles in topology rather than triggering silent replacements or exceptions.

---

## 4. Registers vs. Math Channels

To prevent semantic confusion, Doccer strictly distinguishes between registers and channels:

- **Unicode Register**: A named span of Unicode codepoint addresses (or a family of such spans). Membership produces Block, Script, and GeneralCategory Unicode classifications on character atoms.
- **Math Channel**: An unrelated canonical mathematical language channel. It is neither a Doccer fact ontology nor a dependency of canonical facts or saturation.

---

## 5. Domain Neutrality & Projections

- **Zero Format Bleed**: Markdown headings, LaTeX environments, PDF-specific boxes, and language-specific syntax nodes never exist as first-class engine primitives. They arrive through adapters and declarative inventories. This does not permanently exclude separately admitted domain-neutral mechanisms such as an evidence-bearing state graph, finite relation problem, or adjacent spatial basis; those mechanisms must not acquire the donor format's interpretation.
- **Process Boundary Projection**: The domain-agnostic in-process surface is designed to project across process boundaries (e.g., via a CLI or IPC tool) using JSON-serializable DTOs, without embedding domain semantics into verbs or flags.
