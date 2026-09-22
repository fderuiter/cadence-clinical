# ADR-2215: Decompose Payments Domain Modules

- **Status:** Accepted
- **Date:** 2026-08-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The Site Payments microservice contained a monolithic 3,116-line god-module (`PaymentsService`), an 808-line repository adapter (`SQLAlchemyPaymentsRepository`), and a 1,917-line router (`payments.py`). This architecture mixed three completely distinct clinical financial domains:
1. Investigator grant budgeting, fee schedules, multi-tier overhead/withholding, and real-time eCRF/SDV/milestone trigger processing.
2. Self-billing invoice compilation cycles, cryptographic SHA-256 PDF remittance advice generation/signing, and enterprise ERP integration (SAP/NetSuite).
3. Participant financial wallets, SoA stipend rules, visit verification triggers, travel expense reimbursement with OCR, digital payouts, and EU CTR 536/2014 lay summaries.

Furthermore, the 40-method repository adapter introduced pass-through boilerplate without isolation value, as SQLite/PostgreSQL `AsyncSession` ORM models are already isolated within the payments microservice boundary.

Requirements: `PRD-PAY-002`, `PRD-PAY-003`, `PRD-PAY-004`, `PRD-PAY-006`.

## 2. Decision Drivers & Constraints

* **Deep Domain Module Architecture:** Encapsulate complex domain workflows behind compact interfaces with direct database query internalization (`AsyncSession`).
* **Microservice Boundary Discipline:** Queries across shared schema models within the microservice (e.g. `BillingAndErpGateway` querying `PayableModel` during invoice compilation) are legal and efficient.
* **100% Backward Compatibility:** HTTP REST contract parity under `/api/v1/payments/*` must be preserved through modular sub-routers aggregated under an entry router.
* **21 CFR Part 11 Audit Trail:** Centralized audit trail helper `record_payment_audit_log` ensures consistent tamper-resistant audit logging across all domains.

## 3. Options Considered

### Option 1: **Option A (Decompose into Three Deep Domain Modules with Direct Session Queries):**

- **Overview:** - Split `PaymentsService` into `SitePaymentLedgerEngine`, `BillingAndErpGateway`, and `ParticipantCompensationService`.
   - Delete `IPaymentsRepository` and `SQLAlchemyPaymentsRepository`, internalizing queries directly into the services.
   - Decompose the router into `site_ledger.py`, `billing_erp.py`, and `participant.py`, aggregated via `payments.py`.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option B (Maintain Monolithic Service with Refactored Repository Interfaces):**

- **Overview:** - Keep `PaymentsService` as a single facade while splitting repository interfaces into 3 sub-repositories.
   - Rejected: Leaves the 3,000+ line god-class intact and adds more intermediate pass-through layers.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead


## 4. Decision Outcome

- **Chosen Option:** **Option A**.
- **Justification:** Selected as primary architectural approach.

1. `SitePaymentLedgerEngine` (`apps/payments/services/site_payment_ledger_engine.py`) owns contracts, fee schedules, split payee configs, trigger processing, payable approvals, and holdbacks.
2. `BillingAndErpGateway` (`apps/payments/services/billing_and_erp_gateway.py`) owns self-billing cycles, PDF remittance generation/signing, ERP export, webhooks, and bank reconciliation.
3. `ParticipantCompensationService` (`apps/payments/services/participant_compensation_service.py`) owns wallets, stipend rules, visit triggers, OCR receipt expenses, disbursements, transactions, and lay summaries.
4. `record_payment_audit_log` (`apps/payments/services/audit.py`) centralizes Part 11 audit logging.
5. Modular sub-routers (`apps/payments/presentation/routers/site_ledger.py`, `apps/payments/presentation/routers/billing_erp.py`, `apps/payments/presentation/routers/participant.py`) aggregated into `apps/payments/presentation/routers/payments.py`.

## 5. Consequences & Trade-offs

* **Positive:**
  - High cohesion and clear bounded contexts across site budgeting, self-billing/ERP, and participant compensation.
  - Eliminated 808 lines of pass-through repository boilerplate; reduced cyclomatic complexity.
  - Test suites run sub-second with direct session injection.
  - 100% backward compatible REST API contract preserved.
* **Negative:**
  - Direct database queries reside inside the domain services (standard pattern for deep services within microservice boundaries).

## 6. Implementation & Verification

* **Created:**
  - `apps/payments/services/audit.py`
  - `apps/payments/services/site_payment_ledger_engine.py`
  - `apps/payments/services/billing_and_erp_gateway.py`
  - `apps/payments/services/participant_compensation_service.py`
  - `apps/payments/presentation/routers/site_ledger.py`
  - `apps/payments/presentation/routers/billing_erp.py`
  - `apps/payments/presentation/routers/participant.py`
  - `apps/payments/tests/test_deep_domain_services.py`
* **Decommissioned & Deleted:**
  - Legacy `payments_service.py` (3,116 lines god-module)
  - Legacy `repositories.py` (808 lines pass-through repository adapter)
  - Legacy `ports.py` (257 lines port definitions)
* **Verified:**
  - All 26 automated unit and integration tests passing (`apps/payments/tests`).
  - GxP compliance and RTM traceability intact.
