# ADR-132: Randomization and Trial Supply Management

* **Status:** Accepted
* **Date:** 2026-08-31
* **Authors:** @jules
* **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement
Clinical trials require strict, GxP-compliant randomization of subjects to treatment arms, as well as deterministic tracking and dispensation of Investigational Product (IP) kits. In order to safeguard trial integrity and comply with FDA 21 CFR Part 11, EU Annex 11, and GDPR/HIPAA standards, we must establish a highly secure and auditable data foundation. This covers the ClinicalSubject state machine, randomization configurations, stratification factors, block sequences, and per-site IP kit inventories.
This ADR addresses the modeling, state gating, and compliance requirements defined in **PRD-SUB-002** (Partial Visit Query Capability on Withdrawn Subjects) and **PRD-SUB-003** (Stratified Block Randomization).

## 2. Decision Drivers & Constraints
* **GxP & 21 CFR Part 11 Compliance:** Immutable audit logs must capture all state transitions, inventory updates, and kit dispensations. Hard deletions must be blocked across all entities.
* **Blinding Integrity:** Blinding must be strictly maintained at the network and persistence layers. Sensitive treatment group and kit allocations must only be resolvable on authorized, unblinded layers.
* **Trial Isolation and Locks:** Global, site-level, and subject-level locks (e.g., TrialLockManager check integrations) must apply automatically to prevent mutations when a site or subject is locked.
* **Pre-decided Architecture Alignment:** The architecture must strictly align with prior decisions **ADR-014** (Compliance Tracing and Automated Trial Locks) and **ADR-16** (Core Service-Oriented Clinical Engine).

## 3. Options Considered
### Option 1: Inline/Decentralized Gating and Unencrypted Persistence
* **Overview:** Rely on localized validation in routers and store allocation arms in plaintext within the database.
* **Pros:**
  * Simple and fast to implement.
* **Cons:**
  * ❌ Violates database-level isolation and blinding security. Plaintext treatments are susceptible to leakage.
  * ❌ Lacks unified audit trail coverage.

### Option 2: Strong Encrypted Modeling with Pure-Python State Machine and Immutable Ledger (Selected)
* **Overview:** Use strongly-typed Pydantic schemas and AuditedModel-based SQLAlchemy entities with encrypted allocation and seed storage. Validate transitions via a pure-Python state guard.
* **Pros:**
  * ✅ Enforces strict, unidirectional subject state transitions.
  * ✅ Ensures perfect alignment with **ADR-014** by integrating with automated database triggers and site/visit locks.
  * ✅ Adheres to **ADR-16** by implementing all allocation logic in pure Python without raw SQL-native side-effects.
* **Cons:**
  * ❌ Small overhead from encryption/decryption routines.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Choosing Option 2 guarantees that subject randomized state transitions are fully locked and audit-trailed, fulfilling the regulatory objectives of **PRD-SUB-002** and **PRD-SUB-003**.

Specifically, this architecture enforces:
1. **Compliance with ADR-014:** Incorporates the threshold cryptography and key derivation metadata (via `AllocationKeyMetadata` and `AllocationKeyManager`) to avoid a single point of failure during unblinding, while using standard database write-protection triggers.
2. **Compliance with ADR-16:** Relies on pure-Python logic for permutation algorithms, stratification-factor validations, and inventory tracking. All templating is handled strictly within Jinja context.
3. **Immutability and Locks:** ClinicalSubject and RTSM models derive from `AuditedModel` to inherit automatic trigger-based shadow auditing. Fields like `site_id` and `visit_id` are consistently mapped so `TrialLockManager` rules apply automatically.

## 5. Consequences & Trade-offs
* **Positive Impact:** Secure, robust subject state flow and complete trace coverage of the randomization lifecycle.
* **Negative Impact / Technical Debt:** Requires cryptographic salt and secret material configuration for key management during local development and testing.
* **Mitigation Strategy:** Keys are managed via decentralized HSM/KMS setups or mocked locally via pre-configured environment secrets.

## 6. Implementation & Verification
* **Affected Repositories / Services:** Execution service (`apps/execution/`) and model definitions in `apps/execution/database/models.py`.
* **Verification Plan:** Unit and integration tests in `tests/test_subject_randomization_lifecycle.py` and `tests/test_rtsm_supply.py` are executed via pytest to verify correct trigger execution, locking enforcement, and state-transition validation.
