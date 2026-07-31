# ADR-117: Add SQLModel Dependency for eISF Structured Section Taxonomy and Versioning

* **Status:** Accepted
* **Date:** 2026-08-27
* **Authors:** @fpderutier
* **Deciders:** @fpderutier, @fderuiter

---

## 1. Context & Problem Statement
The Electronic Investigator Site File (eISF) service needs to support a structured document section taxonomy aligned with the TMF Reference Model v3.2 and ICH GCP E6(R2) Section 8 ("Essential Documents for the Conduct of a Clinical Trial"). To achieve this, we need to map regulatory binder sections and enforce document lifecycle states (DRAFT, UNDER_REVIEW, APPROVED, EXPIRED, SUPERSEDED). The implementation requires defining both SQLAlchemy/SQLModel ORM models and clean Pydantic v2 transport schemas for API serialization.

Without a unified framework like SQLModel, we would need to duplicate model definitions between SQLAlchemy ORM classes and Pydantic serialization schemas. This would lead to higher maintenance overhead and potential schema desynchronization.

This decision implements requirements under **PRD-SYS-001**.

## 2. Decision Drivers & Constraints
* **Driver 1:** Code reuse and reduction of boilerplate by combining ORM mapping and Pydantic validation into a single class hierarchy.
* **Driver 2:** Strict compliance with GxP 21 CFR Part 11 requirements (retaining audit fields such as `created_at`, `created_by`, `reason_for_change`, and `version_index`).
* **Driver 3:** Clean, type-safe API response serialization in compliance with Pydantic v2 standards.

## 3. Options Considered
### Option 1: Separate SQLAlchemy Declarative Models and Pydantic Schemas
* **Overview:** Declare database schemas using SQLAlchemy DeclarativeBase and map them manually to separate Pydantic v2 schemas for API serialization.
* **Pros:**
  * ✅ No new package dependencies.
* **Cons:**
  * ❌ Severe code duplication (fields are declared once for database and once for Pydantic).
  * ❌ High risk of schema drift or serialization mismatches as fields evolve.

### Option 2: Introduce SQLModel Dependency
* **Overview:** Introduce SQLModel as a core dependency to allow defining unified models that serve as both SQLAlchemy tables and Pydantic schemas.
* **Pros:**
  * ✅ Zero duplication: fields are defined once and inherited for database mapping and serialization.
  * ✅ Native support for both Pydantic v2 validation and SQLAlchemy ORM operations.
  * ✅ Perfect alignment with FastAPI's native response model serialization.
* **Cons:**
  * ❌ Adds one extra lightweight library dependency (`sqlmodel`).

## 4. Decision Outcome
* **Chosen Option:** Option 2 (SQLModel)
* **Justification:** SQLModel perfectly addresses our dual requirement of SQLAlchemy-compatible ORM models and Pydantic-compliant transport schemas without any field duplication. This guarantees type safety, accelerates developer velocity, and ensures robust compliance tracing.

## 5. Consequences & Trade-offs
* **Positive Impact:** eISF models like `EISFSectionTaxonomy` and `EISFDocumentRecord` are declared in a clean, unified structure. Automatic default valuation (e.g. `version_index = 1` and `status = "DRAFT"`) is handled seamlessly.
* **Negative Impact / Technical Debt:** Added dependency on `sqlmodel`.
* **Mitigation Strategy:** Pin `sqlmodel>=0.0.22` in `pyproject.toml` to ensure stability and compatibility.

## 6. Implementation & Verification
* **Affected Repositories / Services:** eISF Service (`apps/eisf/`), Core Models (`packages/core-models/etmf/`).
* **Verification Plan:**
  - Verify that the standard eISF sections are properly seeded inside migrations (`apps/eisf/database/migrate.py`).
  - Implement unit and integration tests in `tests/test_eisf_taxonomy.py` verifying model defaults and database querying of all 8 mandatory sections.
