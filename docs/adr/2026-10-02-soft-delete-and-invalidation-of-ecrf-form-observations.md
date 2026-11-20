# ADR-2804: Soft Delete and Invalidation of eCRF Form Observations

- **Status:** Accepted
- **Date:** 2026-10-02
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

When updating eCRF form submission payloads in the Execution service, existing observations linked to the form submission must be invalidated/soft-deleted rather than hard-deleted or left active. This ensures 21 CFR Part 11 auditability and prevents stale observations from participating in active batch edit check evaluations.

## 2. Decision Drivers & Constraints

- **Driver 1:** Preservation of historical observation versions for 21 CFR Part 11 regulatory compliance (PRD-SYS-001, PRD-SYS-042).
- **Driver 2:** Active query filtering so default queries return only active observations (`IsActive == true`).

## 3. Options Considered

### Option 1: Soft deletion via IsActive flag and FormSubmissionId tracking (Selected)

- **Overview:** Extend `ClinicalObservation` entity with `FormSubmissionId` and `IsActive` properties, deactivate existing observations before persisting new extracted observations within the same transaction.
- **Pros:**
  - ✅ Satisfies requirements PRD-SYS-001, PRD-SYS-042, SRS-EDC-001
  - ✅ Preserves audit trail while ensuring default endpoints filter out inactive observations
- **Cons:**
  - ❌ Requires composite index on `(form_submission_id, is_active)` and query parameter filtering

### Option 2: Hard deletion of old observations

- **Overview:** Delete old observations when form payloads are updated.
- **Pros:**
  - ✅ Simpler schema
- **Cons:**
  - ❌ Violates 21 CFR Part 11 audit trail requirements

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Chosen because it preserves complete observation history for regulatory compliance while ensuring default queries return active observations.

## 5. Consequences & Trade-offs

- **Positive Impact:** Full audit trail and correct evaluation of active observations.
- **Negative Impact / Technical Debt:** Additional composite index and `include_inactive` parameter in query endpoints.
- **Mitigation Strategy:** Automated unit test coverage in `FormObservationSoftDeleteTests.cs`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/execution`
- **Verification Plan:** Unit tests in `FormObservationSoftDeleteTests.cs` and schema migration validation.
