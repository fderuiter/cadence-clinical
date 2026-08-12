# ADR-147: Task-Driven CRA Workspace Routing & Multi-Select Batch SDV

- **Status:** Accepted
- **Date:** 2026-08-03
- **Authors:** @google-labs-jules[bot]
- **Deciders:** @lead-architect, @fderuiter, @quality-officer
- **Requirements:** PRD-SYS-001

---

## 1. Context & Problem Statement

Clinical Research Associates (CRAs) are tasked with reviewing and verifying large volumes of clinical data across isolated views (eCRF, CTMS, eTMF) to ensure data accuracy and compliance with study protocols. Previously, this process suffered from:

1. **Inefficient Navigation & Context Loss:** CRAs had to manually navigate and select subjects/visits across separate views, leading to cognitive fatigue and frequent loss of selection context.
2. **Repetitive Single-Field Clicks:** Verified fields could only be signed off individually, resulting in excessive manual effort.
3. **Inadequate Verification Integrity Enforcement:** When a verified data point was edited post-verification, the system did not automatically drop the verification status or notify previous verifiers.

To support efficient monitoring workflows and maintain strict compliance with regulatory data integrity guidelines (FDA 21 CFR Part 11 and GxP standards), a centralized, task-driven workspace with multi-select batch Source Data Verification (SDV) capabilities is required.

## 2. Decision Drivers & Constraints

- **Driver 1 (User Experience & Efficiency):** Reduce cognitive load and context-switching by aggregating actionable notifications into a unified workspace and supporting bulk-signing capabilities.
- **Driver 2 (Regulatory Compliance - FDA 21 CFR Part 11):** Enforce strict electronic signature re-authentication, dynamic token-bound transactions, and a strict time-bound window of 60 seconds to prevent signature reuse or session hijacking.
- **Driver 3 (Data Integrity - Auto-Clear & Audit Trail):** Guarantee that post-verification modifications instantly clear the verified state, log an audited `SDV_CLEAR` block to the ledger, and dispatch a high-priority workspace alert.

## 3. Options Considered

### Option 1: Incremental Individual Verification & Manual Page Navigation

- **Overview:** Keep eCRF, CTMS, and eTMF views fully decoupled. Continue to require CRAs to navigate manually and sign off on field verification one-by-one.
- **Pros:**
  - ✅ Minimizes frontend complexity (no need for batch signature modals or route-based state syncing).
- **Cons:**
  - ❌ Severe operational inefficiency when monitoring high-throughput clinical sites.
  - ❌ High probability of missing post-verification field modifications due to lack of automated clear mechanisms and notifications.

### Option 2: Centralized Task Workspace Routing & Multi-Select Batch SDV (Selected)

- **Overview:**
  1. Display direct deep links with `studyId`, `siteId`, `subjectId`, and `visitId` query parameters inside a centralized notification inbox (`NotificationsView.vue`).
  2. Synchronize store states upon land across `EcrfView.vue`, `CtmsView.vue`, and `DocumentManagerView.vue`.
  3. Integrate GxP-compliant checkbox components adjacent to clinical fields, prompting a unified dual-factor electronic re-authentication modal for bulk signing.
  4. Enforce client/server-side 60-second signature expiration limit.
  5. Automatically drop verification status on field modifications, log `SDV_CLEAR` to the ledger, and push a high-priority `ALERTS` notification back to the unified inbox.
- **Pros:**
  - ✅ High usability: CRAs can transition from notification straight into the correct subject, visit, and field contexts.
  - ✅ High efficiency: Enables single-action multi-select bulk sign-offs.
  - ✅ Full Regulatory Compliance: Adheres to FDA 21 CFR Part 11 re-authentication, audit-trail ledger preservation, and 60-second lockout window constraints.
- **Cons:**
  - ❌ Moderate frontend architectural overhead for Pinia state sync and modal management.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 directly resolves the operational pain points of clinical monitoring while elevating compliance to the highest rigorous standards of FDA 21 CFR Part 11. Deep-linking, batch signatures, automatic verification drops, and immediate notification loops protect data integrity across all clinical observation fields.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - ✅ Enhanced CRA monitoring throughput and reduced cognitive fatigue.
  - ✅ Reliable and automatic data integrity protection via `SDV_CLEAR` triggers.
  - ✅ Secure, step-up dual-factor re-authentication for GxP events.
- **Negative Impact / Technical Debt:**
  - ❌ CRAs must complete the bulk signature re-authentication within the strict 60-second window, or face compliance lockout errors.
  - ❌ Maintenance overhead of Vue component and Pinia store-level synchronization logic.

## 6. Implementation & Verification

### Affected Components & Repositories

- **Deep-linking & Routing:**
  - `apps/web/src/router/index.js` — Defined roles permissions mapping.
  - `apps/web/src/views/NotificationsView.vue` — Computed dynamic deep-links based on notification properties and routed with state query parameters.
- **Context Synchronization:**
  - `apps/web/src/views/EcrfView.vue` / `CtmsView.vue` / `DocumentManagerView.vue` — Synthesized query parameters on mount to synchronize with active store states.
- **Batch SDV, Compliance Lockout, and Verification Drop:**
  - `apps/web/src/views/EcrfView.vue` — Implemented batch checkbox checkboxes, `BULK_SDV` re-authentication workflow, 60-second lockout checks, `SDV_CLEAR` automatic triggers on field value changes inside `commitChange`, and high-priority alert dispatcher.
  - `apps/web/src/stores/clinical.ts` — Tracked eCRF field definitions, ledger entries, and core EDC state.

### Verification Plan

- **Automated Integration Tests:**
  - Developed `apps/web/tests/batch_verification_gxp.test.js` to assert:
    1. Query parameters parsing and store state synchronization.
    2. Checkboxes rendering gated by roles permission mapping.
    3. Dual-factor electronic sign-off and token verification inside the 60-second window.
    4. Compliance lockout rejection if the authentication token exceeds 60 seconds.
    5. Automatic clear of verification states and dispatch of high-priority workspace notifications on verified field edits.
  - Validated that the test suite compiles and runs successfully under Vitest.
