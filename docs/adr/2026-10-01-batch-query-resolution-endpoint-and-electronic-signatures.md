# ADR-2593: Batch Query Resolution Endpoint and Electronic Signatures

- **Status:** Accepted
- **Date:** 2026-10-01
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical monitors, data managers, and auditors require bulk processing of clinical queries and deviations (resolving, escalating, pausing SLA timers) across large clinical studies. Performing these operations individually causes operational friction during database locks and freeze milestones. Additionally, bulk resolutions require 21 CFR Part 11 compliant electronic signatures and individual Merkle-chained audit logging per modified query (PRD-SYS-001).

## 2. Decision Drivers & Constraints

- **Driver 1:** 21 CFR Part 11 compliance requiring electronic signature re-authentication, rationale, and individual audit trail entries per modified ticket (PRD-SYS-001).
- **Driver 2:** Batch operations capped at 100 items per request, validating state transitions individually without failing valid items when an item is invalid.
- **Driver 3:** RBAC security gating enforcing the `ticket.batch_resolve` capability across canonical roles.

## 3. Options Considered

### Option 1: Dedicated Minimal API Batch Endpoint with Single Electronic Signature Modal (Selected)

- **Overview:** Implement `POST /api/v1/tickets/batch` in `apps/tickets` accepting `BatchUpdateTicketsCommand`, validating individual items, writing individual `TicketAuditLog` records, and attaching Part 11 signature tokens. Frontend provides multi-select table checkboxes, a floating `<BatchTicketActionBar>`, and a single `<BatchTicketSignModal>`.
- **Pros:**
  - ✅ Satisfies 21 CFR Part 11 & PRD-SYS-001 with single signature attestation dialog
  - ✅ Caps batch size at 100 and isolates invalid item failures
  - ✅ Fully integrated with `GxPAuditInterceptor` and Merkle-chained audit logging
- **Cons:**
  - ❌ Requires state transition handling logic for multiple batch action types

### Option 2: Sequential Single-Item API Calls

- **Overview:** Loop over selected items in the frontend, sending N individual HTTP requests.
- **Pros:**
  - ✅ Reuses existing single-item endpoints
- **Cons:**
  - ❌ Poor network performance, risk of partial batch completion on disconnection, and repetitive e-signature prompts

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Option 1 provides atomic batch handling, single Part 11 signature attestation, isolated per-item state validation, and Merkle-chained audit entries while enforcing RBAC capabilities.

## 5. Consequences & Trade-offs

- **Positive Impact:** Fast bulk query resolution, seamless Part 11 compliance, and clear feedback on failed vs successful batch items.
- **Negative Impact / Technical Debt:** Additional DTO and endpoint logic in `apps/tickets`.
- **Mitigation Strategy:** Comprehensive unit tests in `BatchUpdateTicketsCommandTests` and frontend Vitest component tests.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/tickets`, `packages/security`, `apps/web`.
- **Verification Plan:** Verified via `dotnet test` (2721 unit tests), architecture tests, Vitest suite, and `gxp sync`.
