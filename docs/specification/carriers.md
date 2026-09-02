# Many-Sorted Carrier Algebra & Naming Conventions

Doccer's mathematical architecture is a **many-sorted algebra** over finite ordered carriers. Carriers are not interchangeable views of a single universal span type: each represents a distinct mathematical sort with strict identity and composition laws.

---

## 1. The Public Sort Vocabulary

| Sort Symbol | Name | Description | Key Equality / Identity Rule |
| :---: | :--- | :--- | :--- |
| **`P`** | Master Boundaries | Valid scalar boundary coordinates within a `TextMaster`. | Validated integer offset in `[0, Master.Length]`. |
| **`L`** | Located Extents | Located character ranges, admitting diagonal empties (`[k, k)`). | Master compatibility + exact start and end offsets. |
| **`I`** | Allen Intervals | Nonempty intervals (`start < end`) over a linear continuum. | Geometric interval equivalence (`start == other.start && end == other.end`). |
| **`C`** | Claim Occurrences | Identity-bearing occurrence records in a frozen `SpanBatch`. | Exact frozen batch identity + row ordinal. |
| **`F`** | Semantic Facts | Canonical semantic assertions with geometry and value tuples. | Domain + Kind + Geometry tuple + Value tuple over a compatible master. |
| **`O`** | Origin Relations | Output-to-source atom correspondence relations. | Tagged-atom coordinate `(slot ordinal, atom ordinal)` over an `OriginBasis`. |
| **`B`** | Boolean Vectors | Basisless immutable finite bit sequences. | Bit length and logical bit sequence (not physical packing). |
| **`U`** | UTF-16 Unit Masks | Explicit UTF-16 code-unit windows over a `TextMaster`. | Compatible `TextMaster` + exact integer window + bit mask. |

### Sort Distinction Rules
- **Empty Extents**: Diagonal empty extents (`[k, k)`) belong strictly to located geometry ($L$), not to Allen intervals ($I$).
- **Geometric vs Occurrence Identity**: Allen `Equal` represents geometric equivalence on $I$, never occurrence identity on $C$. Equal rows across separate batches do not make claims interchangeable.
- **Origin Reference Identity**: An `OriginRelation` requires an exact shared reference-identity `OriginBasis` object at endpoints; matching slot tags alone cannot substitute basis identity.

---

## 2. Carrier Specifications

### Vectors & Masks (`B`, `U` — D46)
- **`BooleanVector` ($B$)**: An immutable, basisless finite bit sequence. Supports bitwise operations (AND, OR, XOR, NOT), forward inclusive prefix parity, inverse transitions, and chunk carry.
- **`Utf16UnitMask` ($U$)**: Binds a `BooleanVector` to a compatible `TextMaster` and an explicit code-unit window. Any in-range window edge or set unit is valid for direct use (including interior surrogate boundaries); scalar safety is enforced during atom harvest.

### Facts & Support Hypergraphs (`F` — D43, D44)
- **`FactKey`**: An immutable snapshot containing domain and kind strings, ordered geometry extents ($L$), and canonical string-value tuples. Zero-arity and empty extents are admitted.
- **`CanonicalFactTable`**: Deduplicates and deterministically orders fact keys in a fixed `domain / kind / geometry / value` sequence independent of proposal order.
- **`SupportHypergraph`**: Retains an exact fact table and occurrence batch with ordered rule/premise/occurrence evidence.
- **`FactSaturation` (D44)**: Computes the finite positive least closure in key space over a `SaturationProblem`, creating a new canonical fact table and remapping enabled supports to final ordinals.

### Origins & Lineage (`O` — D45)
- **`OriginBasis`**: An exact ordered namespace of uniquely tagged `TextMaster` slots.
- **`OriginRelation`**: A canonical, finite, partial, many-to-many relation over atom coordinates `(slot ordinal, atom ordinal)` on `TextTopology.Atoms`.
- **`OriginProjection`**: Retains the exact relation, selected output slot/span, and a disconnection-preserving normalized `SpanSet` per source slot.

### Materialization (`RewritePlan` — D47)
- **`RewritePlan`**: An ordered output program over an exact source basis whose positive pieces are closed to:
  1. Exact copy of source slices.
  2. Locally total origin-mapped literals.
  3. Explained synthetic literals.
- **`RewriteMaterialization.Materialize`**: Realizes the plan into a new `TextMaster` and singleton output basis, a gap-free reconstructing piece partition, a direct K6 origin relation, and an unused-source `SpanSet` per source slot.

---

## 3. Current Sort-Explicit Naming

Current public operations make their sort explicit in their names to reduce semantic ambiguity:

```text
Composition Operations:
  ├── AllenCompose       (Qualitative upper approximation on AllenRelationSet)
  ├── Seq                (Located geometric composition on LocatedRelation)
  ├── ComposePairs       (Occurrence-level relational join on ClaimPairView)
  └── ComposeOrigins     (Exact-basis relation composition on OriginRelation)

Selection & Optimization Operations:
  ├── PathSelection.Select       (Exact nonnegative-additive complete-path DAG optimization)
  ├── Laminarizer.Admit          (Maximal no-crossing structural family selection)
  ├── Pairing.Pair               (Strict-stack open/close delimiter pairing)
  ├── FactSaturation.Saturate    (Least positive closure on ground facts)
  └── RewriteMaterialization.Materialize (Ordered output realization and residue extraction)
```

There is **no unqualified `Compose` or `Select`** in the public API.

---

## 4. Verification Expectations

Current verification concerns are partitioned across mathematical lanes ($D42/D46$):
1. **Raw Vector Algebra**: Formal laws over basisless bit-vectors.
2. **UTF-16 Unit Window**: Refinement and code-unit boundary safety.
3. **Harvest Bridge**: Atom classification, scalar safety, and claim emission.
4. **Packed Backend Equivalence**: A future hardware-accelerated (SIMD/SWAR) backend would need convincing behavioral-equivalence evidence against the portable reference implementation before serving as an alternative engine backend.
