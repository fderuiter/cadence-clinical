# ADR-2211: Deep Adjudication Workflow Seam

- **Status:** Accepted
- **Date:** 2026-08-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In the Medical Imaging microservice (`apps/imaging`), the presentation HTTP router (`apps/imaging/presentation/routers/imaging.py`) previously orchestrated multi-step blinded dual-reader state progression, concordance/discordance evaluation, 21 CFR Part 11 cryptographic sealing, and low-level database mutations directly within route handler functions across 1,937 lines.

This presentation-layer coupling violated the Single Responsibility Principle and made testing workflow state transitions impossible without bootstrapping full FastAPI HTTP request contexts and SQLAlchemy database sessions.

Requirements referenced: PRD-IMG-003, PRD-IMG-005, PRD-IMG-006, PRD-SYS-001.

## 2. Decision Drivers & Constraints

* **High Leverage & Low Surface Area:** The workflow interface must be deep (Ousterhout / Feathers principle), presenting minimal entry points that hide extensive discordance and Part 11 signing orchestration.
* **Hexagonal Architecture Decoupling:** Persistence and audit trail operations must be abstracted behind a typed driven port (`ImagingAdjudicationRepositoryPort`).
* **Sub-Second Testability:** Business logic, state transitions, and Part 11 hash-chain generation must be verifiable via pure in-memory test fakes without touching live PostgreSQL/SQLite databases.
* **GxP Compliance & 21 CFR Part 11 Integrity:** Cryptographic digest generation, step-up electronic signatures, and immutable audit logs must be guaranteed across all reader submissions and chair sign-offs.

## 3. Options Considered

### Option 1: **Option A (Selected): Deep `AdjudicationWorkflow` Module with `ImagingAdjudicationRepositoryPort`**

- **Overview:** Encapsulate reader progression, discordance triggers, 21 CFR Part 11 cryptographic sealing, and persistence mutations inside `AdjudicationWorkflow` presenting two primary methods: `submit_reader_assessment` and `sign_off_adjudication`. Inject an abstract `ImagingAdjudicationRepositoryPort` satisfying hexagonal architecture.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option B: Shallow Helper Functions with Direct `AsyncSession` Passing**

- **Overview:** Extract helper functions while continuing to pass raw SQLAlchemy `AsyncSession` objects across the presentation/service boundary.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 3: **Option C: Presentation-Layer Transaction Script**

- **Overview:** Retain inline database mutations and business logic within router endpoints.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead


## 4. Decision Outcome

- **Chosen Option:** **Option A** because it:
- **Justification:** Selected as primary architectural approach.
* Reduces the presentation router to concise, single-line delegations.
* Provides a clean seam at `ImagingAdjudicationRepositoryPort`, enabling lightweight in-memory fake testing.
* Strictly encapsulates Part 11 cryptographic sealing and EDC tumor assessment synchronization within the domain service layer.
* Translates domain errors (`AdjudicationSessionNotFoundError`, `AdjudicationSignOffRationaleRequiredError`) cleanly to standard HTTP error codes.

## 5. Consequences & Trade-offs

* **Positive:** Locality for maintainers: all adjudication business rules, discordance thresholds (20% SLD delta, new lesion mismatch), and Part 11 signatures reside in a single deep service.
* **Positive:** Testability: `AdjudicationWorkflow` can be tested in sub-second unit test suites with `InMemoryImagingAdjudicationRepository`.
* **Negative:** Requires maintaining repository port abstractions and database adapter mappings between domain models and SQLAlchemy ORM models.

## 6. Implementation & Verification

* `apps/imaging/ports/repository_port.py`: Defined abstract `ImagingAdjudicationRepositoryPort`.
* `apps/imaging/adapters/database.py`: Implemented `SqlImagingAdjudicationRepository` and FastAPI dependency providers.
* `apps/imaging/services/adjudication.py`: Implemented `AdjudicationWorkflow` with `submit_reader_assessment` and `sign_off_adjudication`.
* `apps/imaging/domain/exceptions.py`: Added explicit domain exceptions.
* `apps/imaging/presentation/routers/imaging.py`: Refactored adjudication endpoints to delegate directly to `AdjudicationWorkflow`.
* `apps/imaging/tests/test_adjudication.py`: Added unit tests verifying workflow state progression and error modes with an in-memory repository fake.
* `apps/imaging/tests/test_imaging_dual_reader_adjudication.py`: Verified end-to-end REST API integration tests continue to pass without regression.
