# Current Design Principles

Doccer's current architecture is organized around four design principles. They explain the shape of the implemented kernel and provide questions for evaluating additions; they are not a closed list of all future forms Doccer may take.

---

## 1. Four Current Principles

### I. Claims Carry Evidence
Every claim, relation, and inferred fact carries its exact provenance:
- An occurrence claim in a `SpanBatch` retains its exact row ordinal and source span.
- A pair relation in `ClaimPairView` carries the exact source and target batch identities and Allen relation labels.
- A deduced fact in `SupportHypergraph` carries the exact rule, premise keys, parameters, and occurrence evidence that justified its derivation.

### II. Policies Make Choices Explicit
Mechanisms with multiple valid outcomes expose named policies rather than hiding choices in unparameterized heuristics:
- Suppression, laminarization, and path selection execute caller-named, parameterized policies (e.g. `LaminarAdmissionPolicy.PriorityThenGeometry`, `AdditivePathPolicy`, `SegmentationPolicy.FirstOrdinalCompletePath`).
- Tie-breakers and priority resolutions are deterministic and explicit in the policy definition.

### III. Separate Generic Mechanism from Domain Meaning
The reusable engine implements deterministic resolution mechanisms without assigning format-specific meaning:
- The engine computes reachability, finds shortest complete paths, computes laminar families, and tracks lineage.
- Callers and adapters currently decide which query and policy to use, what the resulting structure means in their domain, and what workflow follows.

### IV. Preserve Unresolved Evidence
Current operations preserve ambiguities and structural conflicts as structured evidence:
- Unmatched open or close delimiters are retained in `PairingResult` as unclosed residue.
- Overlapping or crossing spans in laminarization are partitioned into accepted families and crossing residue (`Laminarizer.Admit`).
- Missing origin mappings are tracked as unused source spans in `RewritePlan` without inferring deletion.

---

## 2. Questions for Core Placement

When deciding whether a capability belongs in the reusable engine, the following questions help distinguish a stable generic mechanism from application-specific behavior:

1. **Determinism**: Can the stable operation produce repeatable results for identical inputs under an explicit policy?
2. **Reusable Mechanical Work**: Does it solve a recurring structural or algorithmic problem that would otherwise be reimplemented across consumers?
3. **Source and Evidence Preservation**: Can it retain exact coordinates, material, provenance, and unresolved residue appropriate to its contract?
4. **Domain Separation**: Can the reusable mechanism remain distinct from format-specific interpretation, or does the proposed behavior currently fit better in an adapter or caller?

These are placement questions, not permanent exclusion gates. An incomplete answer may justify further contract work, an exploratory implementation, or an adapter-level home. A capability can be reconsidered as evidence and use cases develop.
