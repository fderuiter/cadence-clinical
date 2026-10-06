# ADR-2809: ACH Auto-Reimbursement and Expense Claim Disbursement Reference

- **Status:** Accepted
- **Date:** 2026-10-06
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical trial participant travel expense reimbursement requires automated OCR parsing, policy-backed auto-approval, and immediate direct ACH disbursement. To track direct ACH dispatches against individual participant expense claims, the `ParticipantExpenseClaim` domain model and underlying database schema in `apps/payments` must store a unique disbursement reference (`DisbursementReference`).
@req:PRD-SYS-182

## 2. Decision Drivers & Constraints

- **Regulatory & Audit Tracing:** Every auto-approved reimbursement dispatched via direct ACH must have an explicit tracking reference (`DISB-ACH-{timestamp}`) linked to the claim and recorded in 21 CFR Part 11 compliant audit logs.
- **Automated Direct Reimbursement:** Auto-approve claims meeting policy criteria (< $100.00 gross amount, OCR confidence >= 0.90, meal charges <= $75.00 per-diem cap) and immediately dispatch direct ACH funds.
- **Review Hold Routing:** Flag and route claims exceeding thresholds or per-diem caps to `PENDING_REVIEW` status for manual sponsor review.

## 3. Options Considered

### Option 1: Extend ParticipantExpenseClaim Model with DisbursementReference Property (Selected)

- **Overview:** Add a string column `DisbursementReference` to `ParticipantExpenseClaim` via EF Core migration `20261006193258_AddDisbursementReferenceToExpenseClaims`.
- **Pros:**
  - ✅ Direct relational persistence linking claims to ACH transaction identifiers.
  - ✅ Simple queryability and status display in subject-portal and payment APIs.
- **Cons:**
  - ❌ Requires database migration for `payments_participant_expense_claims` table.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Directly satisfies auditability and financial tracking requirements while seamlessly integrating with EF Core and PostgreSQL persistence.

## 5. Consequences & Trade-offs

- **Positive Impact:** Clear tracking of automated ACH disbursements across API responses, subject wallet UI, and audit logs.
- **Negative Impact / Technical Debt:** Database schema migration added to payments context.
- **Mitigation Strategy:** EF Core migration generated and applied cleanly with snapshot alignment and unit test coverage.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/payments`, `apps/subject-portal`
- **Verification Plan:** Verified with `dotnet test --filter ExpenseClaimAutoApprovalTests`, `pnpm --filter subject-portal test`, and `pnpm cadence check`.
