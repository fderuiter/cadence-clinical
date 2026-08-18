# ADR-2161: In-Process Clinical De-Identification and Gateway USDM Validation Router

- **Status:** Accepted
- **Date:** 2026-09-12
- **Authors:** @jules
- **Deciders:** @lead-architect

---

## Status

Accepted

## Context

Biostatistical export pipelines require high-throughput in-process clinical de-identification and USDM protocol specification import/export via API Gateway while enforcing strict cross-service import boundaries under PRD-SYS-001.

## Decision

Define Gateway-local Anti-Corruption Layer (ACL) for USDM validation (`apps/gateway/domain/acl/usdm_validation.py`) and optimize in-process de-identification date calculations.

## Alternatives Considered

Direct cross-service imports between Gateway and Execution services, which violates system import boundary rules.

## Trade-offs

Minor code duplication for USDM validation DTOs in Gateway ACL in exchange for strict service isolation and zero AST import violations.

---

## 1. Context & Problem Statement

Biostatistical export pipelines require high-throughput in-process clinical de-identification and USDM protocol specification import/export via API Gateway while enforcing strict cross-service import boundaries under requirement PRD-SYS-001.

## 2. Decision Drivers & Constraints

- **Driver 1:** Performant in-process date shifting, pseudonymization, and age capping for SDTM/ADaM clinical exports under strict performance SLAs (<100ms for 1,000+ records) (PRD-SYS-001).
- **Driver 2:** Enforce strict service import boundaries (no direct imports from Gateway into Execution domain).
- **Driver 3:** Unified USDM protocol import and export REST endpoints in Gateway router with schema validation.

## 3. Options Considered

### Option 1: Direct Cross-Service Import Between Gateway and Execution

- **Overview:** Import Execution ACL DTOs directly into Gateway presentation routers.
- **Pros:**
  - ✅ Quick implementation.
- **Cons:**
  - ❌ Violates system-wide hexagonal service boundary rules checked by AST import linter.

### Option 2: Gateway-Local Anti-Corruption Layer for USDM Validation (Selected)

- **Overview:** Define a Gateway-local Anti-Corruption Layer (ACL) module for USDM validation (`apps/gateway/domain/acl/usdm_validation.py`) and optimize in-process de-identification date calculations.
- **Pros:**
  - ✅ Preserves strict service boundaries with zero AST import lint failures.
  - ✅ Delivers high-throughput in-process date shifting and de-identification.
  - ✅ Fulfills PRD-SYS-001 requirements.
- **Cons:**
  - ❌ Minor code duplication for USDM validation DTOs across bounded contexts.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Guarantees strict isolation of Gateway and Execution services while providing high performance and satisfying requirement PRD-SYS-001.

## 5. Consequences & Trade-offs

- **Positive Impact:** Cleaner service boundaries, fast in-process de-identification performance.
- **Negative Impact / Technical Debt:** Maintenance of USDM validation logic in Gateway ACL.
- **Mitigation Strategy:** Validate contract parity using automated contract verification scripts in CI.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `apps/gateway/domain/acl/usdm_validation.py`
  - `apps/gateway/presentation/routers/usdm.py`
  - `apps/execution/biostat/deid.py`
- **Verification Plan:**
  - `uv run python scripts/validate_adrs.py`
  - `uv run python scripts/validate_imports.py`
  - `uv run cadence check --parallel`
