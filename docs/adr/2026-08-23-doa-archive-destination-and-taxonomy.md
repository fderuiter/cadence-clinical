# ADR-099: Delegation of Authority Log Archive Destination and Taxonomy Mapping Decisions

* **Status:** Accepted
* **Date:** 2026-08-23
* **Authors:** Jules
* **Deciders:** Jules, @fderuiter

---

## 1. Context & Problem Statement
When a digital Delegation of Authority (DOA) record is electronically signed off and finalized by a Principal Investigator, it must be automatically and durably archived in a GxP-compliant regulatory document management system.

We need to make two architectural decisions:
1. Choose and document the primary archive destination for finalized signed DOA records (eTMF, eISF, or a dual-filing workflow).
2. Reconcile the taxonomy mapping for Delegation of Authority Log (reconciling standard code `05.02.04` against historical proposal `05.02.18`).

This decision satisfies GxP and 21 CFR Part 11 requirements under Trace-7 and PRD-SYS-001.

## 2. Decision Drivers & Constraints
* **GxP Compliance & Site Responsibility:** Delegation of authority is legally a clinical investigator's site responsibility. The log must be retained in the Investigator Site File (ISF) to satisfy standard regulatory inspector expectations.
* **Standard-versus-Extension Policy:** We prioritize canonical DIA Reference Model codes mapped in the active complete catalog version (`v3.2.0-complete`) to prevent unneeded custom extensions and simplify taxonomy consistency.
* **Audit Trail Preservation:** The handoff from `apps/org` must be durable, secure, authenticated via gateway V2 signatures, and preserve all signed payloads, signatures, dates, and audit trail provenance.

## 3. Options Considered

### Option 1: Archive strictly to eTMF (using code 05.02.18)
* **Description:** Save finalized DOA logs in the Sponsor's eTMF using the historical draft code `05.02.18`.
* **Cons:**
  * ❌ Violates standard DIA Reference Model v3.2.0 taxonomy (which designates `05.02.04` for DOA logs).
  * ❌ Does not put the record in the eISF, failing standard site-level GCP investigator retention expectations.

### Option 2: Archive strictly to eISF (using code 05.02.04) (Selected)
* **Description:** Save finalized DOA logs in the Investigator Site File (eISF) using standard taxonomy code `05.02.04`.
* **Pros:**
  * ✅ High GCP/GxP compliance: site-level delegation of authority resides in the Investigator Site File (eISF), which is legally owned and maintained by the Principal Investigator.
  * ✅ Full taxonomy reconciliation: using the canonical code `05.02.04` aligns perfectly with standard DIA Reference Model v3.2.0-complete catalogs without creating a redundant, non-standard extension `05.02.18`.
  * ✅ Simple completeness integration: eISF already requires Delegation of Authority Log in `apps/eisf/main.py`.

### Option 3: Dual-Filing Workflow (Dual archival to both eTMF and eISF)
* **Description:** Automatically file the record in both systems.
* **Pros:** Highly compliant, but adds duplication.
* **Cons:** Increases complexity; Option 2 is sufficient and highly compliant since eISF-to-eTMF sync can handle cross-system propagation if configured.

## 4. Decision Outcome
* **Chosen Option:** Option 2 (Archive strictly to eISF using canonical code `05.02.04`).
* **Justification:** Choosing eISF as the primary archive destination aligns with legally defined GCP investigator-retention responsibilities. Reconciling the taxonomy to `05.02.04` keeps our taxonomy model strictly standard, avoiding the draft/historical proposal code `05.02.18`.

## 5. Consequences & Trade-offs
* **Handoff Mechanism:** Implemented a durable HTTP-based handoff in `apps/org/main.py` using standard Gateway V2 signatures to authenticate service-to-service requests.
* **Completeness Workflow:** Finalized records participate immediately in eISF completeness checks out-of-the-box.
* **Transport Resilience:** Downstream transport errors are captured and logged to ensure that a transient network glitch in eISF does not block PI operations.

## 6. Implementation & Verification
* New integration tests in `tests/test_org_integration_e2e.py` will verify:
  - Successful sign-off and subsequent handoff trigger to eISF.
  - Correct taxomomy mapping.
  - Participation in eISF completeness checks.
