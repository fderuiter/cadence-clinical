# ADR-097: API-driven Lock Sync and AST Import Validator for eTMF

* **Status:** Accepted
* **Date:** 2026-07-27
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
Direct in-memory code imports between the document management (`eTMF`) service and the execution service resulted in isolated, desynchronized trial lock states. This gap allowed unauthorized document writes during active clinical or legal freezes. To resolve this, we must replace direct imports with secure synchronous API-driven communication and implement a build-time Abstract Syntax Tree (AST) validator to permanently prevent direct cross-service imports.

## 2. Decision Drivers & Constraints
* **GxP Integrity / Zero Caching:** Any latency or caching of the trial lock state can result in illegal document modifications during active legal or clinical freezes, failing GxP compliance.
* **Separation of Concerns:** Keep service boundaries decoupled and maintain clean independent deployment contexts.
* **Proactive Security:** Inter-service communications must be authenticated and cryptographically signed.
* **Preventing Regression:** Build-time automated checks to ensure developers do not accidentally reintroduce boundary-crossing direct imports.

## 3. Options Considered
### Option 1: Shared Database or In-Memory State Sync
* **Overview:** Rely on shared SQL database or in-memory state sync across service boundaries.
* **Pros:**
  * ✅ Avoids extra HTTP API network hops.
* **Cons:**
  * ❌ Wrecks service isolation and decouples database architectures incorrectly.
  * ❌ Vulnerable to synchronization delay and desynchronization.

### Option 2: Secure Synchronous API Lock Propagation & Build-Time AST Validator
* **Overview:** Decouple services by introducing synchronous API-driven lookups over HTTP with cryptographically signed authorization headers, and add a custom AST parser to scan Python imports during build/test phase to assert boundaries.
* **Pros:**
  * ✅ Real-time authoritative verification of lock state, fulfilling strict GxP requirements.
* **Cons:**
  * ❌ Adds minor latency overhead for inter-service communication.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Implementing secure, synchronous HTTP lookups prevents desynchronized trial lock states. Combining this with a build-time AST validation mechanism enforces code-level service boundaries, preventing developers from reintroducing direct package imports.

## 5. Consequences & Trade-offs
* **Positive Impact:** Secure, decoupled trial lock state validation. Code base isolation is statically enforced by the CI pipeline.
* **Negative Impact / Technical Debt:** Requires maintenance of the custom AST parser script and test suite.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/etmf/`, `scripts/validate_imports.py`, `package.json`.
* **Verification Plan:** Validated via automated unit tests under `tests/test_validate_imports.py` and integration tests under `tests/test_etmf_lock_integration.py`.
