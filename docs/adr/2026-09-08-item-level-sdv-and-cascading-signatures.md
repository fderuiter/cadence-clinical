# ADR-255: Centralized RBAC and Cascading Signature Invalidation for Item-Level SDV

* **Status:** Accepted
* **Date:** 2026-09-08
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
Item-level Source Data Verification (SDV) requires fine-grained role-based access control (RBAC) to ensure that only authorized roles (such as Clinical Research Associates - CRAs) can perform verification flag actions. Additionally, 21 CFR Part 11 and GxP guidelines require that any modification or resolution of a flagged item must cascade and invalidate existing transactional signatures on that record to preserve clinical data integrity and provenance.

Requirements: PRD-SYS-001

## 2. Decision Drivers & Constraints
* **PRD-SYS-001 (GxP and 21 CFR Part 11 Audit Trail and Role-Based Restrictions):** All item-level verification flags and resolution operations must be authorized via centralized security policies, and any subsequent data modifications must invalidate any signature locks currently held.
* **Audit Integrity and Data Provenance:** Changes to flagged items must cascade and cleanly remove obsolete signature associations.

## 3. Options Considered
### Option 1: Inline Signature Invalidation in API Controllers
* **Overview:** Check for existing signatures and delete them manually within the route handler of the item-level SDV router.
* **Pros:**
  * ✅ Simple and direct implementation for single endpoints.
* **Cons:**
  * ❌ Bypasses centralized audit triggers and cascading rules, making it prone to omissions if multiple endpoints modify the same data.

### Option 2: Centralized Database-Cascaded Signature Invalidation
* **Overview:** Implement cascading signature deletion and invalidation directly in the GxP audit ledger and transaction manager. Centralize the `"sdv"` action map in the main `rbac.py` file to systematically authorize `"flag"` operations for designated roles.
* **Pros:**
  * ✅ Clean separation of concerns.
  * ✅ Guarantees that any modification automatically invalidates associated signatures across the system.
  * ✅ Centralizes role verification within `packages/security/rbac.py`.
* **Cons:**
  * ❌ Requires careful database mapping and dependency structure.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Centralizing the permission model in `packages/security/rbac.py` and implementing cascading signature invalidation ensures complete alignment with PRD-SYS-001.

## 5. Consequences & Trade-offs
* **Positive Impact:** Robust and tamper-proof invalidation of e-signatures, perfect compliance with 21 CFR Part 11.
* **Negative Impact / Technical Debt:** Requires keeping the centralized permission map updated with all newly introduced resource actions.
* **Mitigation Strategy:** Automated API contract validation and RBAC suite runs in CI to flag any inconsistencies.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/security/rbac.py`, `apps/execution/`
* **Verification Plan:** Verified via `test_sdv_item_level_rbac.py` and full pytest regression suite.
