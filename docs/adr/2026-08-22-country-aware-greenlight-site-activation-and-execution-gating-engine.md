# ADR-2202: Country-Aware Greenlight Site Activation and Execution Gating Engine

- **Status:** Accepted
- **Date:** 2026-08-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In global clinical trials across diverse regulatory jurisdictions (US FDA, EU CTR, Japan PMDA, UK MHRA), activating an investigator site requires satisfying a multifaceted set of legal, ethical, operational, and supply prerequisites.
In legacy systems (Veeva, Medidata), site activation is frequently tracked via static checklists or manual milestone toggles. This allows catastrophic human errors where sites begin randomizing subjects or receiving investigational drug supplies before obtaining necessary Institutional Review Board (IRB) approval, executed Clinical Trial Agreements (CTA), or fully certified Delegation of Authority (DOA) logs. We require an automated, un-bypassable Greenlight Site Activation engine (PRD-SYS-121).

## 2. Decision Drivers & Constraints

* **Regulatory Gating & Non-Bypassability**: The system must cryptographically block RTSM randomization, drug shipment releases, and eCRF write permissions at un-activated sites.
* **Country-Level Jurisdictional Flexibility**: Prerequisite checklists vary by country (e.g. EU Member State Part I/II authorizations vs US FDA 1572 forms).
* **Multi-App Orchestration**: Prerequisites span multiple domain modules: `apps/ctms/` (CTA contract execution), `apps/etmf/` / `apps/eisf/` (IRB approval and site binders), `apps/org/` (staff qualifications), `apps/knowledge/` (curriculum completion), and `apps/execution/` (DOA log execution).

## 3. Options Considered

### Option 1: **Dynamic Country-Aware Prerequisite Rules Matrix & Cryptographic Token Gating (Selected)**: Study authors configure country-specific prerequisite criteria in `apps/designer/`. The `apps/ctms/` Greenlight Engine dynamically evaluates real-time readiness across 6 domain facets:

- **Overview:** - Regulatory Authority & IRB/Ethics Committee Approval
   - Fully Executed Clinical Trial Agreement (CTA) & Budget
   - Complete Baseline eISF Essential Documents & Investigator CVs
   - Principal Investigator Executed Delegation of Authority (DOA) Log
   - Verified Protocol Amendment & SOP Training for PI and All Active Staff
   - Investigational Product (IP) Depot Greenlight Release
   Once all criteria are digitally certified with 21 CFR Part 11 electronic signatures, the Greenlight Engine issues an HMAC-signed activation certificate and emits `SiteGreenlightApproved` to un-gate RTSM and EDC.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Static Global Checklist**: A single hardcoded checklist for all sites with manual CRA sign-off.

- **Overview:** **Static Global Checklist**: A single hardcoded checklist for all sites with manual CRA sign-off.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 3: **Advisory Warning Only**: Display a non-blocking UI badge without programmatically gating RTSM or EDC.

- **Overview:** **Advisory Warning Only**: Display a non-blocking UI badge without programmatically gating RTSM or EDC.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead


## 4. Decision Outcome

- **Chosen Option:** **Dynamic Country-Aware Prerequisite Rules Matrix & Cryptographic Token Gating**.
- **Justification:** Selected as primary architectural approach.
- Enforces strict regulatory compliance with zero possibility of accidental non-compliant enrollment.
- Dynamically adapts to multi-national regulatory frameworks.
- Automates cross-app synchronization upon site activation.

## 5. Consequences & Trade-offs

* **Positive**:
  - Eliminates regulatory inspection findings related to unauthorized early site enrollment.
  - Full transparency for CRA and study startup teams on remaining activation blockers.
  - Automated cryptographic handshake between CTMS, RTSM, and EDC.
* **Negative**:
  - Requires all prerequisite upstream modules to expose certified completion status APIs.
  - Strict gating requires emergency override escalation protocols with executive Part 11 sign-off.

## 6. Implementation & Verification

* Greenlight prerequisite rules matrix and certification evaluator within `apps/ctms/`.
* Gating interceptors integrated with `apps/execution/` and `apps/gateway/`.
* Traceable to requirement `PRD-SYS-121` and verified by site activation test suites.

