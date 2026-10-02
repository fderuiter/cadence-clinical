# ADR-2801: Automated Stipend Disbursement Threshold Rules for Participant Wallet

- **Status:** Accepted
- **Date:** 2026-10-01
- **Authors:** @cadence-agent
- **Deciders:** @cadence-architecture-board

---

## 1. Context & Problem Statement

To achieve feature parity with Greenphire ClinCard, the Cadence Clinical Platform requires automated threshold-based stipend disbursements triggered by eCRF visit verification events. The platform must auto-disburse payments within threshold limits while enforcing IRS Form 1099 compliance ($600 YTD threshold without verified Form W-9), IRB anti-coercion caps, idempotency on duplicate events, and 21 CFR Part 11 audit trails.
@req:PRD-SYS-182

## 2. Decision Drivers & Constraints

- **IRS Form 1099 Compliance:** Payments reaching or exceeding $600 YTD without a verified W-9 must be halted and flagged for administrator review.
- **IRB Anti-Coercion Protection:** Stipend amounts exceeding protocol/wallet anti-coercion caps must be routed to manual review.
- **Auto-Disbursement Safety Thresholds:** Configurable threshold limits per study, site, and visit type must guard against automated over-disbursements.
- **Idempotency & Auditability:** Events with duplicate `event_id` or `IdempotencyKey` must be cleanly deduplicated without double crediting, and all decisions recorded in a 21 CFR Part 11 audit trail.

## 3. Options Considered

### Option 1: Synchronous In-Process Rule Engine & Ledger Persistence (Selected)

- **Overview:** Evaluate stipend compensation rules synchronously in the Payments microservice (`apps/payments`), update the participant wallet micro-ledger, enforce tax and coercion thresholds, and record 21 CFR Part 11 compliant audit logs in EF Core PostgreSQL.
- **Pros:**
  - ✅ Immediate deterministic feedback for EDC visit triggers and web UI.
  - ✅ Atomic EF Core transactions ensure wallet balance, ledger transactions, and audit logs stay strictly in sync.
- **Cons:**
  - ❌ Requires database schema extension for participant stipend rules, transactions, expense claims, and disbursements.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Guarantees strict compliance and idempotency while providing responsive feedback to EDC webhooks and coordinator review queues.

## 5. Consequences & Trade-offs

- **Positive Impact:** Full Greenphire ClinCard parity with automated threshold disbursements, IRS 1099 compliance holds, IRB anti-coercion cap enforcement, and Part 11 audit logs.
- **Negative Impact / Technical Debt:** Requires running EF Core migrations to update the payments database schema.
- **Mitigation Strategy:** Managed EF Core migrations (`20261001161305_AddParticipantCompensationTables.cs`) with idempotent SQL support and automated unit/integration tests.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/payments`, `apps/web`
- **Verification Plan:** Verified with xUnit test suite (`ParticipantStipendDisbursementTests.cs`) and `pnpm cadence check`.
