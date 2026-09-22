# ADR-2222: Real Time Payable Ledger Holdbacks and Patient Compensation Wallet

- **Status:** Accepted
- **Date:** 2026-08-28
- **Authors:** @fderuiter
- **Deciders:** Engineering Architecture Board, Clinical Operations Working Group, Finance Working Group
* **Requirements:** PRD-SYS-001, PRD-FIN-001, PRD-FIN-002, PRD-PAT-001

---

## 1. Context & Problem Statement

Clinical trial financial administration spans investigator grant management, multi-party payee splits, contractual holdback escrows, participant stipend disbursements, travel expense reconciliations, and regulatory lay summary reporting. Fragmented or delayed financial tracking risks site dissatisfaction, tax non-compliance (IRS Form 1099-MISC), overpayments prior to data verification, and breaches of EU CTR 536/2014 Article 37 transparency mandates.

The platform requires:
1. Real-time outbox event processing from eCRF visit completions and CRA Source Data Verification (SDV) certifications into automated investigator payable line items.
2. Multi-party payee split calculations (e.g., 70% Institution / 30% PI) and statutory 10% closeout holdback retention escrow.
3. Dual 21 CFR Part 11 electronic signature ceremony for holdback release upon site closeout audit verification.
4. Participant Financial Wallet supporting automated SoA visit stipends, 1-click CRC receipt approval, and $600 IRS 1099 threshold monitoring with automated W-9 validation requests.
5. EU CTR 536/2014 Article 37 Plain Language Lay Summary distribution with cryptographically signed SHA-256 confirmation read-receipt tokens.

## 2. Decision Drivers & Constraints

* **ICH GCP E6(R2) §5.9 / §4.9**: Financial agreements between sponsor and investigator/institution must be documented in writing, with verifiable payment records aligned to verified trial activities.
* **IRS 1099-MISC Reporting Rules**: Participant stipends exceeding $600 in a calendar year require taxpayer identification collection and annual reporting.
* **EU CTR 536/2014 Article 37**: Sponsors must provide lay summaries of clinical trial results to trial participants within 12 months of trial completion.
* **Dual-Sign-Off Holdback Release**: Release of escrowed funds strictly requires independent authorization from Sponsor Finance and Lead CRA with 21 CFR Part 11 electronic signatures.

## 3. Options Considered

### Option 1: **Option 1 (Selected): Real-Time Payable Ledger + SDV Gating + Payee Splits + 10% Escrow Holdback + Patient Financial Wallet + EU CTR Lay Summary Portal**:

- **Overview:** - Real-time EDC trigger-to-payable outbox processor checks procedure payment grids and holds unverified items in `PENDING_SDV` until CRA certification.
   - Payee split rates allocate funds dynamically while reserving 10% in `HOLDBACK_RETAINED` escrow.
   - Dual Part 11 electronic signatures release holdbacks upon site closeout.
   - Patient Financial Wallet provides mobile receipt OCR, 1-click CRC approvals, and real-time $600 IRS 1099 threshold accumulator.
   - Secure patient lay summary portal records SHA-256 confirmed read-receipts in the GxP audit ledger.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option 2: Offline Monthly Spreadsheet Billing**:

- **Overview:** - Monthly manual invoices submitted by sites with manual finance approval and untracked cash disbursements to patients.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 3: **Option 3: Immediate Unverified Payouts**:

- **Overview:** - Instant payment upon visit entry with no SDV verification gating or holdbacks.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead


## 4. Decision Outcome

**- **Chosen Option:** ** Option 1 because it guarantees contractual compliance, prevents pre-verification financial leakages, enforces tax compliance, protects participant privacy, and fulfills EU CTR transparency mandates.

## 5. Consequences & Trade-offs

* **Positive**:
  - Eliminates payment delays and reduces site administrative burden via automated self-billing.
  - Guarantees data verification before payment release for complex or high-cost clinical procedures.
  - Ensures seamless IRS 1099-MISC tax compliance without manual accounting overhead.
  - Provides verifiable participant lay summary delivery receipts for regulatory inspections.
* **Negative**:
  - Requires maintaining real-time transaction event handlers and billing compilation cycles.
  - Dual-signature holdback release requires coordination across distinct sponsor and monitoring personas.

## 6. Implementation & Verification

* **Core Target Modules**:
  - `apps/payments/`: `SitePaymentLedgerEngine`, `ParticipantCompensationService`, `BillingAndERPGateway`, payee split calculators, and self-billing invoice aggregators.
  - `apps/ctms/`: Procedure payment grids, investigator grant contract models, site budget templates, and milestone financial triggers.
  - `apps/web/src/components/payments/`: Site financial ledger visualizers, invoice approval drawers, and participant wallet UI.
* **Verification Suites**:
  - `apps/payments/tests/test_deep_domain_services.py`
  - `apps/payments/tests/test_patient_wallet_lay_summary_portal.py`
  - `apps/payments/tests/test_payments_trigger_split.py`
  - `apps/payments/tests/test_payments_self_billing_invoicing.py`
  - `apps/ctms/tests/test_financials_auto_payables.py`
