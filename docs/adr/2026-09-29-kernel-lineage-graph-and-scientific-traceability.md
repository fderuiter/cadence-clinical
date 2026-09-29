# ADR-2255: Kernel Lineage Graph and End-to-End Scientific Traceability (K009)

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Regulators, reviewers and sponsors need to trace any reported number back to its source, and any protocol
requirement forward to the evidence and results it produced: an ADaM value to the SDTM record, the canonical
observation, the CRF version, the protocol version effective for that participant at that site, the source and
the audit trail. ICH E6(R3) and 21 CFR Part 11 (PRD-SYS-053) require this reconstruction; today it is done by
people reading specifications and logs. The Neo4j graph was retired for protocol storage (ADR-2245), so the
lineage graph needs a home.

## 2. Decision Drivers & Constraints

- **Driver 1 (completeness):** every derived object has recorded inputs; lineage cannot drift from data.
- **Driver 2 (both directions):** backward (value → source) and forward (objective → results) queries.
- **Driver 3 (time):** lineage is interpreted `as_of(valid_at, known_at)` like all kernel truth.
- **Driver 4 (inspection):** one query assembles the evidence dossier for any participant, endpoint or finding.

## 3. Options Considered

### Option 1: Lineage nodes and edges in the kernel schema, written in the command transaction (Selected)

- **Overview:** `LineageEdge(from_node, to_node, kind, activity_id, tenant)` insert-only, where nodes are versioned
  kernel objects and edge kinds are `requiredBy`, `definedBy`, `collectedBy`, `derivedFrom`, `mappedBy`,
  `transformedBy`, `validatedBy`, `attestedBy`, `governedBy`, `supersedes`. Edges are written by the same
  ProvenanceActivity that writes the object (K-LIN-2). Traversal uses recursive CTEs with depth limits; a graph
  projection MAY be built for analytics.
- **Pros:**
  - ✅ Transactional completeness: an object and its lineage commit together.
  - ✅ RLS and bitemporal rules apply unchanged.
  - ✅ No additional datastore.
- **Cons:**
  - ❌ Deep traversals over very large studies need careful indexing or a projection.

### Option 2: Separate graph database fed by events

- **Pros:**
  - ✅ Rich graph query languages.
- **Cons:**
  - ❌ Eventually consistent; lineage can lag or diverge from truth; a second system to validate (ADR-2245 rationale).

### Option 3: Derive lineage on demand from provenance and specifications

- **Cons:**
  - ❌ Every query re-derives relationships; specification changes silently alter historical lineage.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Justification:** Lineage is evidence; it must obey the same transactional, tenancy and temporal guarantees as
  the facts it describes. A graph projection can be added for exploration without becoming the source of truth.

## 5. Consequences & Trade-offs

- **Positive Impact:** Trace in both directions, inspection dossiers, amendment impact analysis (K-PROT-2) and
  lineage-grounded AI answers all read one structure.
- **Negative Impact / Technical Debt:** Every writer must emit edges; forgetting is a defect.
- **Mitigation Strategy:** The command pipeline requires declared inputs for derived writes, and K-LIN-1 (no
  orphan derived objects) runs as a scheduled validation rule.

## 6. Implementation & Verification

- **Affected Assemblies:** `Cadence.Kernel.Domain` (node/edge model), `Cadence.Kernel.Application` (pipeline),
  `Cadence.Kernel.Persistence` (tables, traversal queries).
- **Tracked by:** #4779 (pipeline writes edges), roadmap phase 2 (lineage store and trace API).
- **Verification Plan:**
  - Spec §14 test 9: backward trace from an ADaM value and forward trace from an Objective.
  - Property test: derived writes without inputs are rejected.
  - Byte-reproducibility of projections for fixed `(valid_at, known_at)` (K-LIN-3).
