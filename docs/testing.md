# Verification & Testing Methodology

Doccer uses a high-assurance verification approach for mathematical interval algebras, graph algorithms, and rewrite materialization without runtime or framework dependencies.

---

## 1. Verification Philosophy

- **Zero-Dependency Harness**: The core test runner (`Doccer.Tests`) executes as a standalone program without third-party test framework overhead, enabling rapid, deterministic execution.
- **Algebraic Law Verification**: Where a capability has algebraic laws (associativity, distributivity, converse, identity, closure), the harness checks them through exhaustive finite censuses or independent reference oracles where tractable.
- **Oracle & Census Testing**: Complex optimization routines (such as DAG path selection or laminarization) are validated against independent, brute-force oracles and exhaustive state censuses over bounded inputs.
- **Preservation of Residue**: Tests explicitly assert that unresolved or crossing structures (e.g. crossing laminar spans, unclosed delimiters, unmapped origin slices) are captured as structured residual data rather than silently ignored.

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

---

## 4. Running the Suites

To execute the full verification suite:

```powershell
# Run standalone test harness and law suite
dotnet run --project tests/Doccer.Tests/Doccer.Tests.csproj
```
