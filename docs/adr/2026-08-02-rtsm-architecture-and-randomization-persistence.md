# ADR-053: RTSM Architecture and Randomization Persistence

* **Status:** Accepted
* **Date:** 2026-08-02
* **Authors:** @jules
* **Deciders:** @lead_architect, @gxp_compliance_officer

---

## 1. Context & Problem Statement
The platform requires a Randomization and Trial Supply Management (RTSM) module to handle subject treatment allocation and clinical supply chain integration. In clinical trials, the process of assigning subjects to treatment arms (randomization) is a critical GxP (Good Clinical Practice) process that must be highly secure, reproducible, deterministic under controlled conditions (e.g., using stratified permuted block randomization or minimization algorithms), and fully auditable for regulatory compliance (21 CFR Part 11).

To maintain blinding, the treatment assignments and allocation sequences must be strictly confidential. Thus, sensitive allocation and sequence values must be encrypted-at-rest. Concurrently, the randomization setup, state tracking per stratum, and subject assignments must be protected from tampering and hard-deletion, participating in the platform's unified audit trail and read-only trial-locking architecture.

This decision record establishes the governing RTSM architecture and the audited relational database schemas for randomization configuration, per-stratum state, and subject assignments.

This decision implements requirements under Trace-1.

## 2. Decision Drivers & Constraints
* **Compliance (21 CFR Part 11 & GxP):** All changes to randomization setup or assignments must produce immutable, versioned audit trail records. Hard deletes must be strictly prohibited.
* **Blinding & Security:** Treatment sequences, block designs, and subject allocations must be cryptographically protected at rest to prevent unblinding by unauthorized personnel or direct database queries.
* **Trial Locking & Immutability:** Randomization entities must conform to trial-level read-only locks, ensuring no new assignments can occur if a trial lock is active.
* **Reliability & Determinism:** Randomization states must be tracked independently per stratum to allow deterministic, concurrent treatment allocations without block sequence overlap.
* **Pure Python implementation:** All components must be written in standard Python, leveraging the shared core models, cryptographic utilities, and relational trigger-based auditing.

## 3. Options Considered
### Option 1: Separate RTSM Microservice with Independent Storage
* **Overview:** Build a standalone RTSM microservice with its own database, security boundaries, and custom audit logging.
* **Pros:**
  * ✅ High isolation; simplifies service boundaries.
* **Cons:**
  * ❌ Increases operational complexity and network overhead.
  * ❌ Bypasses or duplicates the existing `AuditedModel` triggers, Merkle-sealing, and trial-locking mechanics in the clinical execution database.

### Option 2: Unified Audited Persistence Models inside Clinical Execution Service
* **Overview:** Model RTSM persistence directly inside the clinical execution database using `AuditedModel` base models. Utilize the existing schema management, automatic audit shadow triggers, and trial-locking boundaries. Apply encryption-at-rest using shared cryptographic helpers (such as AES/Fernet via `AllocationKeyManager`) for sensitive sequence and allocation data.
* **Pros:**
  * ✅ Leverages existing, validated database-trigger-based GxP audit tracking and soft-delete enforcement automatically.
  * ✅ Full compatibility with `TrialLockManager` and Merkle-root sealing.
  * ✅ Simple, pure-Python execution utilizing existing database managers.
* **Cons:**
  * ❌ Couples randomization schema with the clinical observations/visits schema (mitigated by clean logical separation into dedicated tables).

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 ensures immediate and full compliance with 21 CFR Part 11 out of the box, as all database operations automatically write to the immutable `audit_logs` table via Postgres/SQLite triggers. It natively adheres to trial, site, and visit read-only locking. Cryptographic blinding is maintained by storing sensitive allocation names and sequence maps in encrypted-at-rest fields.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Unified GxP audit trail across all clinical execution data (subjects, observations, queries, and randomization).
  * Zero risk of hard deletion of critical randomization configurations or subject assignments.
  * Strong blinding security through field-level encryption.
* **Negative Impact / Technical Debt:**
  * Developer must ensure that keys used for allocation encryption/decryption are managed securely (mitigated by using `AllocationKeyManager` or threshold-based key reconstruction).
* **Mitigation Strategy:** Key management protocols and roles (e.g., restricted to "Grants Manager" or "Sponsor Admin" / unblinded statistical roles) will restrict access to unblinding keys.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/execution/` (models and migrations).
* **Verification Plan:**
  * Add Pydantic or SQLAlchemy models in `apps/execution/database/models.py`.
  * Verify model triggers and table migration execution via database test runs.
  * Ensure that all write operations trigger `INSERT` or `UPDATE` audit log actions automatically.
  * Assert that trial locking blocks any mutations to randomization states or subject assignments.
