# ADR-058: FDA 21 CFR Part 11 Offline Sync Ledger & Digital Signatures

* **Status:** Accepted
* **Date:** 2026-07-25
* **Authors:** @google-labs-jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
In clinical trials, data integrity, traceability, and regulatory compliance are paramount. Previously, clinical discrepancy query updates were stored in volatile client-side memory, causing critical query changes to be lost on page reloads or network drops. This directly violates FDA 21 CFR Part 11 guidelines and compromises clinical data collection.
To meet GxP and FDA 21 CFR Part 11 requirements, we must implement an offline-capable local ledger that persists transaction blocks locally, gates high-security actions with active re-authenticated digital signatures, and synchronizes transactions asynchronously and reliably to the backend clinical sync gateway.

This decision implements requirements under Trace-1.

## 2. Decision Drivers & Constraints
* **Compliance:** Strict compliance with FDA 21 CFR Part 11 and EU Annex 11.
* **Resilience:** Safe offline operational state preservation during active clinical site trials with network instability.
* **Traceability:** Permanent, immutable audit trails of all user events and discrepancy updates.
* **Security:** Cryptographically verified digital signatures and protection against replay attacks.

## 3. Options Considered
### Option 1: Live-Only Sync
* **Overview:** Require direct real-time API calls for all query updates with no offline capabilities.
* **Pros:** Simpler architecture with zero local ledger state.
* **Cons:** Unstable network drops lead to data loss and system locking.

### Option 2: Persistent Offline Ledger & Async Sync Queue (Selected)
* **Overview:** Store local clinical queries and cryptographic ledger blocks to `localStorage`. Use an asynchronous background queue inside Pinia to monitor and safely sync ledger blocks to the backend REST endpoint, verifying every transition using JWT-backed single-use compliance tokens (`X-Sig-Token`).
* **Pros:** Zero data loss, robust offline capability, cryptographically secure.
* **Cons:** Slightly increased complexity in frontend sync queue tracking and backend HMAC-SHA256 signature verification.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 provides complete regulatory compliance with 21 CFR Part 11 by ensuring data integrity and audit trailing even during server connectivity drops. It meets security requirements through the combination of active re-authentication, HMAC-SHA256 validation, and single-use `X-Sig-Token` safeguards.

## 5. Consequences & Trade-offs
* **Positive Impact:** Robust offline-first clinical query handling, zero query loss on site reloads, and absolute compliance with GxP audit guidelines.
* **Negative Impact / Technical Debt:** Additional local state management overhead on the frontend client.
* **Mitigation Strategy:** Detailed unit testing covering offline scenarios, server errors, and replay attacks on the synchronization gateway.

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  * Frontend store: `apps/web/src/stores/clinical.js`
  * Frontend view: `apps/web/src/views/EcrfView.vue`
  * Shared security: `packages/security/middleware.py` and `packages/ui/signing.js`
  * Execution backend router: `apps/execution/main.py`
* **Verification Plan:**
  * Comprehensive Jest/Vitest unit tests covering frontend background queue and storage state.
  * Integration tests simulating role-based access control, network failures, single-use token expiration, and happy-path background synchronization.
  * Successfully merged with `main` and resolved all Version 1 / Version 2 signature compatibility intersections.
