# ADR-2156: Implement Hexagonal Domain Repository Separation

- **Status:** Accepted
- **Date:** 2026-08-04
- **Authors:** @google-labs-jules[bot], @jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Our clinical state validation and GxP compliance rules (such as 21 CFR Part 11 consent protections and safety audit trails) were previously coupled tightly to SQLAlchemy database models. This dependency forced unit tests to initialize database engines or rely on fragile emulation wrappers, severely slowing down test feedback loops.

Additionally, because compliance workflows were obscured by database-level triggers, listeners, and custom session-flush hooks, it was difficult for auditors to quickly verify the integrity of clinical workflows.

By decoupling the core domain rules from database-specific frameworks using a **Hexagonal Architecture (Ports and Adapters)**, we seek to isolate core domain validation logic from physical database engines and details, in order to comply with standard audit logging requirements under **PRD-SYS-001**.

## 2. Decision Drivers & Constraints

- **Compliance:** Align with standard audit logging under **PRD-SYS-001** (21 CFR Part 11 § 11.10(e)) by isolating and making audit/consent rules extremely transparent.
- **Developer Velocity:** Sub-millisecond developer test feedback loops using pure Python unit tests, completely database-free.
- **Maintainability & Modularity:** Clean operational boundaries between pure business entities and database-specific framework schemas (SQLAlchemy/SQLModel).

## 3. Options Considered

### Option 1: Framework-coupled Database Models

- **Overview:** Keep state machine validations, immutability constraints, and GxP rules directly embedded in SQLAlchemy model classes via validators and active event hooks.
- **Pros:**
  - ✅ Simpler codebase in the short term (fewer files/abstractions).
  - ✅ Automatic synchronization of DB status and local memory representations.
- **Cons:**
  - ❌ Severe coupling of business rules with third-party libraries (SQLAlchemy).
  - ❌ Testing requires spinning up active databases or extensive mocking of ORM behaviors.

### Option 2: Hexagonal Domain-Repository Separation

- **Overview:** Separate the execution core into pure Python domain models (`ClinicalSubjectDomain`, `ConsentSignatureDomain`, `ConsentFormRecordDomain`, `AuditLogDomain`) and Protocol-based repository ports. Implement concrete Adapters for both SQLAlchemy and In-Memory storage.
- **Pros:**
  - ✅ Standardized business layer that has zero dependencies on databases or frameworks.
  - ✅ Instantaneous unit testing capability using In-Memory repositories.
  - ✅ Clearer, more readable code for clinical regulatory auditors.
- **Cons:**
  - ❌ Additional mapping layer/boilerplate needed to convert between domain models and database records.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Decoupling using Ports and Adapters allows us to cleanly isolate GxP compliance constraints from database-specific frameworks. It fulfills **PRD-SYS-001** audit trail guarantees by securing data transitions and immutability rules inside pure Python classes, while enabling thread-safe, ultra-fast test execution using `InMemorySubjectRepository`, `InMemoryConsentRepository`, and `InMemoryAuditRepository`.

## 5. Consequences & Trade-offs

- **Positive Impact:** Clear operational boundaries, zero leakage of database adapters to other components, and sub-millisecond test suite execution.
- **Negative Impact / Technical Debt:** Requires a translation/mapping layer in the repository adapter to map between domain entities and database models.
- **Mitigation Strategy:** Keep mapping logic highly standardized and verify correctness with comprehensive integration tests running against actual databases (as implemented in `test_hexagonal_domain.py`).

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `apps/execution/domain/models.py` (Pure Python entities)
  - `apps/execution/domain/ports.py` (Port Protocols & concrete Adapters)
  - `apps/execution/domain/__init__.py` (Clean public package interface)
- **Verification Plan:**
  - Run `pytest tests/test_hexagonal_domain.py` to assert both pure domain transition logic, in-memory workflow tests, and SQLAlchemy repository integration tests.
