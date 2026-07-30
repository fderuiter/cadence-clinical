# ADR-102: eConsent to eTMF Archival Delivery

* **Status:** Accepted
* **Date:** 2026-08-25
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
The eConsent service manages versioned clinical trial templates and patient/subject consent signatures. Upon signature, a durable signed Informed Consent Form (ICF) artifact must be archived into the electronic Trial Master File (eTMF) repository with full 21 CFR Part 11 auditing compliance, without losing delivery attempts or creating duplicate/ambiguous archival states.

This decision satisfies GxP and 21 CFR Part 11 requirements under PRD-SYS-001.

## 2. Decision Drivers & Constraints
* **Compliance:** 21 CFR Part 11 and GxP regulatory audit trails.
* **Reliability:** Idempotent, retry-safe service-to-service delivery.
* **Traceability:** Cross-system correlation IDs.

## 3. Options Considered
### Option 1: Direct Synchronous Network Ingestion
* **Overview:** Ingest the signed ICF into eTMF synchronously within the HTTP request lifecycle of the signature endpoint.
* **Pros:**
  * ✅ Simple implementation.
* **Cons:**
  * ❌ Failure of the eTMF service block the core clinical signature process.
  * ❌ Network hiccups lead to silent, unrecoverable archival loss.

### Option 2: Outbox Pattern with Retry-Safe Background Dispatcher (Selected)
* **Overview:** Persist a pending `EtmfArchivalDelivery` record in the database within the atomic transaction of the signature, and run an asynchronous background dispatcher loop with bounded exponential backoff to handle reliable delivery.
* **Pros:**
  * ✅ Extremely reliable and decouple-safe; eTMF downtime never blocks patient signing.
  * ✅ Highly traceable via persistent audit trails (QUEUED, ACCEPTED, FAILED).
  * ✅ Idempotency via deterministic correlation IDs prevents duplicate ingestion.
* **Cons:**
  * ❌ Introduces a background worker lifespan thread.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Guarantees GxP-compliant, non-blocking, and retry-safe delivery.

## 5. Consequences & Trade-offs
* **Positive Impact:** Failures are fully observable, auditable, and recoverable.
* **Negative Impact:** Adds slight infrastructure/background processing overhead.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/econsent`, `apps/etmf`
* **Verification Plan:** Verified via `tests/test_etmf.py` and `tests/test_econsent_archival.py`.
