# ADR-085: Declarative Route Dependencies and Fallback Signature Verification

* **Status:** Accepted
* **Date:** 2026-08-11
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
To secure clinical execution APIs and comply with GxP standards, incoming requests must be authenticated and validated with clear caller identity and scope definitions (such as site_id, sponsor_id, and unblinded_access). Historically, route verification checks were performed inside endpoint bodies using legacy v2 signature structures, making it difficult to enforce authorization boundaries statically. We need to transition to standard, route-level declarative dependencies across execution routes while maintaining fallback backward compatibility for legacy clinical configurations and mock clinical verification.

This decision implements requirements under Trace-7.

## 2. Decision Drivers & Constraints
* **Driver 1:** Enforce route-level declarative authentication/authorization for secure clinical execution APIs.
* **Driver 2:** Maintain fallback compatibility to verify identity-only legacy Version 2 signatures without breaking existing mock setups.
* **Driver 3:** Preserve GxP audit fields (such as change justification) on sensitive write routes.
* **Driver 4:** Fully satisfy the platform's ADR validation quality gate checks.

## 3. Options Considered
### Option 1: Strictly Require Modern Propagation Payloads Only
* **Overview:** Reject any incoming request that does not include site-specific or sponsor-specific parameters in the signature verification payload.
* **Pros:** Highly secure, immediate standardization.
* **Cons:** Breaks backward compatibility with existing mock configurations and regression tests that use legacy identity-only signature payloads.

### Option 2: Fallback Dual-Verification Protocol and Route-Level Dependencies
* **Overview:** Standardize execution routes using FastAPI `Depends` for role validation and fallback to legacy identity-only payload checks if the modern verification fails.
* **Pros:** Secure, robust, backward-compatible, clean static boundary enforcement.
* **Cons:** Introduces a small fallback verification block in the verification utility.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Choosing Option 2 secures our FastAPI endpoints using route-level declarative dependencies while keeping full fallback compatibility for old mock configurations.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Clean, standardized, and audit-friendly execution endpoints.
  * Preserved backward compatibility across legacy tests.
* **Negative Impact / Technical Debt:**
  * Small footprint of legacy parsing logic is preserved inside `verify_gateway_signature`.

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  * `packages/security/rbac.py`
  * `packages/security/signing.py`
  * `packages/security/middleware.py`
  * `apps/execution/main.py`
* **Verification Plan:**
  * Automated testing in `tests/test_cli_etmf_archival.py`, `tests/test_medical_coding_impact.py`, and `tests/test_medical_coding_lifecycle.py`.
