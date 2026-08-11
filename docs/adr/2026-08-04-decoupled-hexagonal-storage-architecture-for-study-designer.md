# ADR-2158: Decoupled Hexagonal Storage Architecture for Study Designer

- **Status:** Accepted
- **Date:** 2026-08-04
- **Authors:** @google-labs-jules[bot], @jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Previously, clinical study versioning, delta comparisons, and transaction-locking mechanisms within the Study Designer (`apps/designer/`) were directly coupled to Neo4j database drivers and inline Cypher query statements. This coupling created several key issues:

1. **Maintenance Overhead:** Core business logic was mixed with infrastructure-specific graph query code.
2. **Brittle Simulations:** We maintained duplicated production-code fallback paths to support offline, local simulation runs without an active database session.
3. **Validation & Regulatory Risk:** Coupling database drivers into core code risked runtime validation errors and complicated GxP compliance tracking.

To resolve this, we have refactored the Study Designer using a hexagonal architecture pattern. By decoupling domain logic from persistence mechanics, we can run clean, database-independent simulations and enforce strict architectural boundaries.

## 2. Decision Drivers & Constraints

- **Compliance:** Enforce strict 21 CFR Part 11 and GxP boundaries under **PRD-SYS-001** by isolating core study designer logic from persistence layers.
- **Decoupled System Boundaries:** Keep the core study designer domain and delta analysis logic independent of external database engines (specifically Neo4j).
- **Prevention of Architectural Regression:** Implement an automated verification check to programmatically block database driver imports inside the pure domain.
- **Fidelity & Compatibility:** Maintain 100% backward compatibility with existing tests and endpoints, preserving the shared in-memory mock states.

## 3. Options Considered

### Option 1: Inline Cypher & Direct Database Driver Coupling

- **Overview:** Maintain inline Cypher queries and direct Neo4j connections inside `apps/designer/delta.py`.
- **Pros:**
  - ✅ Less abstraction overhead.
- **Cons:**
  - ❌ Severe architectural regression risks.
  - ❌ Complex/brittle mocking required for unit testing and offline simulations.

### Option 2: Decoupled Hexagonal Storage with Dynamic Registry (Selected)

- **Overview:** Refactor the persistence mechanics into a separate adapter layer (`apps/designer/adapters/repositories.py`) and establish a dynamic registry where the concrete database adapter registers its implementation callbacks to the domain facade on startup.
- **Pros:**
  - ✅ Absolute isolation of the core domain from Neo4j.
  - ✅ Programmatic import protection via static analysis and architectural tests.
  - ✅ Simplified, robust offline/mock simulation states in memory.
- **Cons:**
  - ❌ Requires callback dynamic registration boilerplate on application startup.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Decoupling using hexagonal ports and adapters enforces a strict boundary between domain rules and persistence mechanisms. Establishing a dynamic registry allows us to preserve backward-compatible in-memory mock states for tests and gateway endpoints while ensuring that no database driver packages are ever directly imported inside `apps/designer/delta.py`.

## 5. Consequences & Trade-offs

- **Positive Impact:** Programmatic assurance of domain cleanliness, sub-millisecond offline test execution, and modular maintainability of graph queries inside dedicated repositories.
- **Negative Impact / Technical Debt:** Requires registering persistence callbacks to the domain facade at application startup.
- **Mitigation Strategy:** Callbacks are automatically registered inside the main application startup lifecycles (`apps/designer/main.py`) and verified by dedicated architecture tests.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `apps/designer/delta.py` (Domain Facade - zero neo4j imports)
  - `apps/designer/adapters/repositories.py` (Concrete persistence adapter and Cypher queries)
  - `apps/designer/main.py` (Startup registration of adapter callbacks)
- **Verification Plan:**
  - Enforced programmatically using `pytest-archon` inside `tests/test_hexagonal_architecture.py` (`test_designer_core_isolation`).
  - Run pytest: `uv run pytest -o addopts="" tests/test_hexagonal_architecture.py tests/test_hexagonal_domain.py` to confirm boundary verification passes successfully.
