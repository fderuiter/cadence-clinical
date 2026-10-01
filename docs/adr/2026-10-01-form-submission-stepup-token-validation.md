# ADR-2601: Form Submission Application Service Step-Up Token Validation

- **Status:** Accepted
- **Date:** 2026-10-01
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

21 CFR Part 11 electronic signature compliance requires step-up re-authentication verification at the application service boundary for form submission approvals (`PRD-SYS-053`). Previously, eCRF form submission approvals validated signer roles and payload hashes, but step-up token checks were handled only via Minimal API endpoint filters or omitted in direct service invocations.

## 2. Decision Drivers & Constraints

- **21 CFR Part 11 Compliance (PRD-SYS-053):** E-signature approvals must validate 60-second step-up tokens at the service boundary (`FormSubmissionApplicationService`).
- **Fail Closed:** Re-authentication failures (missing, expired, replayed, or mismatched tokens) must immediately halt form approval prior to modifying database aggregate state.
- **Single-Use Consumption:** Step-up signature tokens must be consumed upon verification to prevent replay attacks.

## 3. Options Considered

### Option 1: Validate step-up tokens inside FormSubmissionApplicationService (Selected)

Inject `SigTokenVerifier` into `FormSubmissionApplicationService` constructor and validate/consume `request.SigToken` inside `ApproveAsync`.

### Option 2: Rely solely on HTTP Endpoint Filters

Filter-level validation leaves internal or non-HTTP service invocations unprotected against unauthenticated approvals.

## 4. Decision Outcome

Chosen Option 1. `FormSubmissionApproveRequest` accepts an optional `SigToken` property. `FormSubmissionApplicationService` receives `SigTokenVerifier` via dependency injection and verifies tokens against expected signer ID and action binding before aggregate state mutation.

## 5. Consequences & Trade-offs

- **Positive Impact:** Fail-closed step-up re-authentication is enforced at the application service boundary across all execution paths.
- **Mitigation:** Token validation runs prior to repository save changes, keeping aggregate state pristine on verification failure.

## 6. Implementation & Verification

Verified via unit tests in `FormSubmissionStateTests.cs` and integration test suites.
