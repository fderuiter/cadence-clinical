# ADR-2164: Eradicate Core Models and Implement Anti-Corruption Layers

- **Status:** Accepted
- **Date:** 2026-08-07
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The system previously utilized a shared package, `packages/core-models`, containing shared domain models and database schemas across multiple services. While this shared-model architecture simplified early-stage development, it introduced tight coupling across microservice boundaries. Changes to a shared model in one service (e.g., Designer) caused unexpected runtime or compile-time failures and schema mismatches in consuming services (e.g., Execution, eTMF, or Interop). This direct coupling violated microservice autonomy, slowed down deployment pipelines, and created software configuration drift, which poses severe regulatory compliance risks under FDA 21 CFR Part 11 and EU Annex 11. To ensure regulatory submissions match the actual software architecture, we must eradicate shared package models and decouple the services to align with system requirement PRD-SYS-001.

## 2. Decision Drivers & Constraints

- **Strict Decoupling (PRD-SYS-001):** Microservices must strictly own their domain models and must not share database models or domain schemas directly. Sibling imports are prohibited.
- **Maintainability & Developer Velocity:** Changes in one microservice should not break or require immediate updates in unrelated sibling services.
- **Data Integrity & Traceability:** Sibling services must interact via clear, explicit data mapping layers.
- **GxP Compliance:** Software configuration boundaries must be precise, enabling deterministic compliance audits of each service's verification suites.

## 3. Options Considered

### Option A: Decentralized Anti-Corruption Layers (ACLs) - Selected

Move all domain models to their respective service domains (e.g., `apps/<service>/domain/`). All inter-service communication occurs via authenticated REST HTTP endpoints using HMAC-SHA256 V2 gateway signatures. Consumer services deserialize incoming JSON payloads directly into local, consumer-owned Pydantic DTOs defined under `apps/<service>/domain/acl/`.

- **Pros:**
  - ✅ Complete microservice decoupling and autonomy.
  - ✅ Individual database schemas can evolve independently.
  - ✅ Satisfies PRD-SYS-001 and strict GxP regulatory requirements.
  - ✅ Cascading build and deployment bottlenecks are eliminated.
- **Cons:**
  - ❌ Some duplicate schema definition/boilerplate code across consumer ACL DTOs.

### Option B: Centralized Shared API Contracts Package

Maintain a shared schemas or contracts package containing centralized JSON Schema or Pydantic DTO definitions.

- **Pros:**
  - ✅ Less boilerplate code duplication across consuming services.
- **Cons:**
  - ❌ Retains a centralized dependency that couples the release cycles of all services.
  - ❌ Changes to centralized contracts can still cause cascading build failures.

## 4. Decision Outcome

- **Chosen Option:** Option A
- **Justification:** Option A satisfies PRD-SYS-001 while ensuring maximum system maintainability, strict boundaries, and GxP compliance. By eradicating `packages/core-models` and implementing localized Anti-Corruption Layers (ACLs), each microservice can evolve its internal data models and database schemas independently.

## 5. Consequences & Trade-offs

- **Positive Impact:** Clear operational boundaries, complete service autonomy, isolated databases, robust GxP compliance, and localized verification suites.
- **Negative Impact / Technical Debt:** Subefficient duplicate model declarations across different service ACLs (e.g., `ProtocolVersionRefDTO`).
- **Mitigation Strategy:** Enforce AST-based static import validation to block any cross-service or shared import violations.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/execution/`, `apps/etmf/`, `apps/interop/`, `apps/ctms/`, and complete eradication of the `packages/core-models` directory.
- **Verification Plan:** Verify through the full backend test suite (`pytest`) and AST-based static import validation (`validate_imports.py`) to confirm no cross-boundary imports occur.
