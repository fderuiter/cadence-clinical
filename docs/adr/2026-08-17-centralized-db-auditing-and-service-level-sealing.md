# ADR-097: Centralized Database Auditing and Service-Level Sealing

* Status: Accepted
* Date: 2026-08-17
* Authors: @jules
* Deciders: @fderuiter

---

## 1. Context & Problem Statement
Clinical trial systems must enforce strict compliance with regulatory frameworks such as FDA 21 CFR Part 11 and GxP standards. Crucially, all database modifications must be transparently audited, and attempts to bypass audits or hard-delete records must be prevented. Previously, auditing logic was scattered across individual service layers or endpoint handlers (e.g., CTMS, eTMF, Quality modules), which is error-prone, violates DRY principles, and introduces risks of omissions. Additionally, verifying audit data integrity requires cryptographic sealing, but a single global database ledger violates physical boundaries and architectural isolation.

## 2. Decision Drivers & Constraints
* **Compliance & Auditability:** Ensure 100% automated coverage of GxP audit fields and database modification tracking.
* **Integrity Enforcement:** Guard against physical database tampering, hard deletions, and unsigned modifications.
* **Architectural Boundaries:** Respect database isolation constraints per service (e.g., SQLite files remain service-local) without cross-schema dependencies.
* **Maintainability & DRY:** Centralize audit interception, cryptographic signing, and verification utilities to prevent duplicate logic.

## 3. Options Considered
### Option 1: Manual Route-Level / Service-Level Auditing
* **Overview:** Each microservice implements its own endpoints, models, and custom hooks to capture audit context and sign records on modification.
* **Pros:**
  * ✅ High flexibility for service-specific schemas.
* **Cons:**
  * ❌ Violation of DRY, leading to code duplication (e.g., duplicate sealing code).
  * ❌ High risk of omission (e.g., developers forgetting to add audit hooks to new endpoints).

### Option 2: Unified Database Event Listeners and Service-Local Ledger Sealing
* **Overview:** Implement a shared database listener (`before_flush` in `packages/database`) to intercept modifications and block hard-deletions globally. Extract user and change metadata dynamically from context variables. Each service runs its own background ledger sealing loop using central helper functions.
* **Pros:**
  * ✅ 100% audit coverage automatically without developer manual action.
  * ✅ Absolute prevention of unauthorized hard-deletions at the database level.
  * ✅ Complete reuse of cryptographic sealing/verification functions via `packages/security/signing.py`.
* **Cons:**
  * ❌ Requires careful handling of context variable propagation in background threads or tasks.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Choosing Option 2 guarantees that every clinical trial record is audited correctly and that ledger integrity is cryptographically validated without duplicating signing and hashing code across CTMS and Quality services.

## 5. Consequences & Trade-offs
* **Positive Impact:** Fully automated GxP auditing across all models, centralized cryptographic signing helpers, and automated trial locking on tamper detection.
* **Negative Impact / Technical Debt:** Requires careful propagation of request context to background tasks via explicit keyword arguments when calling `audit_context`.
* **Mitigation Strategy:** Added strict validation to ensure correct context parameter usage.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/database/`, `packages/security/`, `apps/ctms/`, `apps/quality/`
* **Verification Plan:** Validated via automated tests in `tests/test_centralized_audit_sealer.py` which verify context capture, cryptographic signing, tamper detection, and lockout propagation.
