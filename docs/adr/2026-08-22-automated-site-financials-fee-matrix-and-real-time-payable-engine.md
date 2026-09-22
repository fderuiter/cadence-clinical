# ADR-2200: Automated Site Financials Fee Matrix and Real-Time Payable Engine

- **Status:** Accepted
- **Date:** 2026-08-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Investigator site payments are traditionally managed via batch invoice processing, spreadsheets, and delayed reconciliations across disparate systems (EDC, CTMS, ERP). This latency creates friction with clinical trial sites, cash flow issues, and substantial accounting overhead.
We need an automated site financial management system that connects versioned investigator grant contracts, procedure-level fee matrices, and real-time EDC form submissions and CRA Source Data Verification (SDV) events into calculated, split-payee payable line items with automated self-billing invoice generation (PRD-PAY-001 through PRD-PAY-006).

## 2. Decision Drivers & Constraints

* **21 CFR Part 11 Financial Integrity**: Financial calculations, payment adjustments, and manual overrides must be append-only with cryptographic audit logging and change reason justifications.
* **Complex Split Payees & Withholdings**: Support multi-party disbursements (Site Institution, Principal Investigator, Clinical Research Coordinator, Central Lab) with configurable withholding retention rules (e.g. 10% held until study closeout).
* **Multi-Currency & VAT Compliance**: Support country-specific currency conversion and tax regulations across global clinical trial sites.
* **Event-Driven Real-Time Triggering**: Automatic generation of payable ledger entries upon verified eCRF completion or CRA SDV sign-off without manual intervention.

## 3. Options Considered

### Option 1: **Real-Time Trigger-to-Payable Engine with Self-Billing & Split-Payee Ledgers (Selected)**: Study teams configure versioned fee matrices in `apps/designer` and `apps/ctms`. The payments engine evaluates real-time clinical events (`VisitCompleted`, `SdvSignedOff`, `MilestoneAchieved`) to compute exact scheduled payables, apply withholding deductions, allocate split payee percentages, and automatically generate monthly self-billing invoices and remittance advices.

- **Overview:** **Real-Time Trigger-to-Payable Engine with Self-Billing & Split-Payee Ledgers (Selected)**: Study teams configure versioned fee matrices in `apps/designer` and `apps/ctms`. The payments engine evaluates real-time clinical events (`VisitCompleted`, `SdvSignedOff`, `MilestoneAchieved`) to compute exact scheduled payables, apply withholding deductions, allocate split payee percentages, and automatically generate monthly self-billing invoices and remittance advices.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Batch Manual Invoicing Workflow**: Sites submit manual PDF invoices for sponsor accountants to verify line-by-line against EDC exports.

- **Overview:** **Batch Manual Invoicing Workflow**: Sites submit manual PDF invoices for sponsor accountants to verify line-by-line against EDC exports.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 3: **Third-Party ERP Integration Only**: Delegate all financial calculations and tracking to external ERP software without native clinical trial context.

- **Overview:** **Third-Party ERP Integration Only**: Delegate all financial calculations and tracking to external ERP software without native clinical trial context.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead


## 4. Decision Outcome

- **Chosen Option:** **Real-Time Trigger-to-Payable Engine with Self-Billing & Split-Payee Ledgers**.
- **Justification:** Selected as primary architectural approach.
- Eliminates site invoicing delays and accelerates study enrollment by providing prompt, transparent site reimbursements.
- Natively synchronizes with EDC and CTMS monitoring visit reports.
- Full GxP compliance with immutable financial audit trails.

## 5. Consequences & Trade-offs

* **Positive**:
  - Dramatic reduction in clinical operations accounting overhead.
  - Transparent real-time visibility for sites into earned and pending payments.
  - Automated generation of compliant self-billing VAT invoices and remittance advice.
* **Negative**:
  - Requires maintaining currency exchange rate feeds and multi-tier tax calculation rules.
  - Amendments to investigator grant contracts require historical payable recalculation protections.

## 6. Implementation & Verification

* Financial domain models and fee calculation engine within `apps/ctms/`.
* Real-time trigger listeners integrated with `apps/execution/` event streams.
* Traceable to requirement `PRD-PAY-001` and verified by CTMS test suites.

