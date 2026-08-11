# ADR-2156: System-Wide Hexagonal Architecture and Pytest-Archon Enforcement

- **Status:** Accepted
- **Date:** 2026-08-04
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Historically, our core microservices (specifically the Clinical Trial Metadata System [CTMS] and Execution engines) suffered from tight coupling where database ORM entities and database operations leaked into API routers and domain logic controllers. This leak of infrastructure details into pure clinical logic made the design fragile, raised regression risks, and increased friction when writing isolated unit tests. To satisfy system baseline requirements (such as standard compliance audit checks and security, traced under PRD-SYS-001) and promote clean separation of GxP boundaries, we need a standard decoupled architecture.

## 2. Decision Drivers & Constraints

- **Decoupled System Boundaries:** Domain business logic must remain independent of external technologies (e.g., SQLAlchemy/SQLModel or FastAPI).
- **Prevention of Architectural Regression:** We require an automated, build-time mechanism to prevent accidental import of framework or adapter libraries into core domain/application packages.
- **Standardized Exception Translation:** Database-specific exceptions must be caught and cleanly mapped to standard domain errors before crossing boundaries, satisfying PRD-SYS-001.

## 3. Options Considered

1. **Option A (Selected):** Implement Hexagonal (Ports and Adapters) Architecture and enforce import boundary rules via build-time `pytest-archon` testing.
2. **Option B:** Maintain legacy layered structures without automated import checking, relying purely on manual code reviews.

## 4. Decision Outcome

Chosen option: **Option A** because it formally separates the clinical engine's pure logic (**Domain**) from orchestration workflow logic (**Application**) and infrastructure technologies (**Adapter**), which complies with the operational verification rules of PRD-SYS-001.

Key implementations:

- Establishment of `packages/hexagonal` containing foundational types (e.g., `RepositoryPort`, `UseCasePort`), domain exceptions, and a `@map_database_exceptions` translator.
- Separation of `apps/ctms/` and `apps/execution/` into explicit `domain/`, `application/`, and `adapters/` packages.
- Enforcement of import rules using `pytest-archon` to programmatically assert boundary logic during unit tests, ensuring violations fail the build automatically.

## 5. Consequences & Trade-offs

- **Positive:** Clear operational boundaries, simplified testing with isolated unit mocks, and complete protection against framework lock-in or future regression under PRD-SYS-001.
- **Negative:** Introduces additional interface/abstraction layers (ports and adapters) which slightly increases the number of files and initial boilerplate code.

## 6. Implementation & Verification

- Target packages and files created and modified: `packages/hexagonal`, `apps/ctms/`, `apps/execution/`.
- Validation and Verification: `tests/test_hexagonal_architecture.py` enforces isolation constraints. GxP synchronization executed and confirmed via `uv run python scripts/sync_gxp.py` showing all tests successfully passing.
