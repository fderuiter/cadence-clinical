# ADR-146: Global Library Governance & Release Management

- **Status:** Accepted
- **Date:** 2026-09-04
- **Authors:** @jules
- **Deciders:** @engineering-lead, @quality-officer

---

## 1. Context & Problem Statement

To ensure GxP and 21 CFR Part 11 compliance (PRD-SYS-001), clinical library objects (Forms, Data Elements, Arms, and Visits) in the Global Library must follow a strict, role-gated state transition machine. In-use library objects referenced by active recruiting studies must be protected against direct silent edits (modifications) to guarantee historical trial reproducibility. Furthermore, published library objects must be immutable and optionally sealed with a canonical cryptographic signature to ensure complete data and schema integrity.

## 2. Decision Drivers & Constraints

- **Driver 1:** 21 CFR Part 11 and GxP compliance requirements (`PRD-SYS-001`) mandate strict audit trails, electronic signatures, and role gating.
- **Driver 2:** Non-repudiation and clinical data integrity: active trials must not have their forms modified retroactively without formal protocol amendments.
- **Driver 3:** DRY (Don't Repeat Yourself) design and robust authorization using centralized RBAC.

## 3. Options Considered

### Option 1: Overwriting objects in-place without state machine controls

- **Overview:** Keep library object fields completely mutable at any time, with standard user roles.
- **Pros:**
  - Simple CRUD operations.
- **Cons:**
  - ❌ Severe violation of GxP compliance. Retrospective changes invalidate historical trial definitions.
  - ❌ Lacks transition pedigree or workflow tracing.

### Option 2: Active-study locking, cryptographic sealing, and role-gated state transitions

- **Overview:** Implement a strict transition state machine (DRAFT -> IN_REVIEW -> APPROVED -> PUBLISHED -> ARCHIVED), with a reference-scan lock check rejecting modifications on in-use library objects. Extend RBAC with `"library_object"` resources for actions `"approve"`, `"publish"`, and `"release"`, while sealing published versions with a canonical cryptographic signature.
- **Pros:**
  - ✅ Full GxP and 21 CFR Part 11 compliance.
  - ✅ High isolation and safety for active trials.
  - ✅ Author self-approval block (four-eyes principle) ensures quality oversight.
- **Cons:**
  - ❌ Increases API layer complexity with state verification and cryptographic signing.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Implementing a formal governance lifecycle and reference checking secures the global library against unauthorized changes, while enabling formal protocol amendments to safely introduce new template versions without breaking existing trials.

## 5. Consequences & Trade-offs

- **Positive Impact:** Secure, traceable, and immutable global library template assets with complete multi-tenant boundaries.
- **Negative Impact / Technical Debt:** Requires callers to use the explicit `/amend` endpoint to edit in-use templates, which is the expected workflow for clinical protocol amendments.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/designer/`, `packages/security/`
- **Verification Plan:** Verified through end-to-end integration tests under `tests/test_library_locks.py` verifying state transition logic, author self-approval rejection, role permission restrictions, and active recruiting lockouts.
