# ADR-2809: CGT Secondary Verifier Signature Tokens and Quarantine Safety Guard

- **Status:** Accepted
- **Date:** 2026-10-06
- **Authors:** @fderuiter
- **Deciders:** @fderuiter, @cadence-architecture-board

---

## 1. Context & Problem Statement

Cell and Gene Therapy (CGT) bedside drug administration requires strict 21 CFR Part 11 electronic signatures and dual-person independent verification before unlocking infusion hardware or authorizing bedside administration. Furthermore, products subject to quality alerts or chain-of-custody holds must be quarantined and barred from bedside verification until QA release.
@req:PRD-SYS-001

## 2. Decision Drivers & Constraints

- **21 CFR Part 11 Compliance:** Both primary and secondary verifiers must supply non-empty electronic signature tokens (`PrimarySignatureToken` and `SecondarySignatureToken`).
- **Dual-Verifier Safety Standard:** Mandatory two-person independent verification (`PrimaryVerifierId != SecondaryVerifierId`) with both verifier IDs required.
- **Quarantine Safety Guard:** Immediate rejection of bedside verification if product status is `QUARANTINED`, preventing unlock token generation.
- **GxP Audit Logging:** Explicit audit records logged for verification failures (`FAILED_BEDSIDE_VERIFICATION_MISSING_VERIFIER`, `FAILED_BEDSIDE_VERIFICATION_DUPLICATE_VERIFIERS`, `FAILED_BEDSIDE_VERIFICATION_MISSING_SIGNATURE`, `FAILED_BEDSIDE_VERIFICATION_QUARANTINED`).

## 3. Options Considered

### Option 1: Single Verifier with Deferred Electronic Signature Validation
- **Overview:** Relying on single-verifier authorization and delegating electronic signature validation to downstream UI components.
- **Pros:** Fewer API parameters.
- **Cons:** Violates FDA 21 CFR Part 11 requirements for dual independent verification and permits unsafe administration.

### Option 2: Enforced Dual Verifiers, Signature Tokens, Quarantine Safety Guard, and Action Audit Logging (Selected)
- **Overview:** Validate primary and secondary verifier IDs and signature tokens in `CgtApplicationService`, enforce quarantine state checks before generating unlock tokens, and write action-only GxP audit entries via `IAuditLogWriter`.
- **Pros:**
  - ✅ Guarantees two distinct, authenticated verifiers.
  - ✅ Blocks quarantined CGT lots from generating unlock tokens.
  - ✅ Records fail-closed GxP audit entries for all verification rejection paths.
- **Cons:**
  - ❌ Requires client applications to pass both signature tokens during bedside verification requests.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Satisfies 21 CFR Part 11 and GxP safety standards by ensuring dual independent verification, quarantine enforcement, and complete audit trail logging.

## 5. Consequences & Trade-offs

- **Positive Impact:** Guaranteed patient safety during CGT administration, robust 21 CFR Part 11 electronic signature compliance, and full auditability.
- **Negative Impact / Technical Debt:** Requires front-end workflows to collect signature tokens for both verifiers.
- **Mitigation Strategy:** Clear HTTP 400 and 422 error responses mapped to domain exceptions in Minimal API endpoints.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/execution`
- **Verification Plan:** Unit tests in `CgtApplicationServiceTests.cs` and `FormEndpoints` integration tests.
