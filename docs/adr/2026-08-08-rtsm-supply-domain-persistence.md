# ADR-061: RTSM Supply Domain Persistence Models

* **Status:** Accepted
* **Date:** 2026-08-08
* **Authors:** @jules
* **Deciders:** @lead_architect, @gxp_compliance_officer

---

## 1. Context & Problem Statement
The platform requires minimal audited supply-domain persistence to handle blinded investigational product (IP) kits, site inventory tracking, dispensation, and threshold-triggered automatic resupply. In clinical trials, tracking supply chain actions is a GxP requirement. These actions must be secure, transparently versioned, and fully auditable for regulatory compliance (21 CFR Part 11).

Crucially, to preserve the study's double-blind status, treatment allocation or drug-code details must not be stored in ordinary blinded supply records. Instead, they must reference blinded kit identifiers, leaving drug-code resolution confined to authorized unblinded layers.

This decision implements requirements under Trace-1.

## 2. Decision Drivers & Constraints
* **Compliance (21 CFR Part 11 & GxP):** Every supply transaction (adding kits, updating inventory levels, dispensing kits, requesting resupply) must generate automated audit trail entries. Hard deletes must be strictly prevented.
* **Double-Blinding Integrity:** The schema must store only blinded kit identifiers (e.g., kit number or kit type) without exposing treatment groups.
* **Durable Dispensation Mapping:** Kit dispensations must map uniquely and durably to subject, kit, site, visit, quantity, and timestamp fields.
* **Durable Resupply Trigger:** Site inventory records must track on-hand quantities and reorder thresholds, raising durable resupply signals when stock falls below specified limits.
* **Conformity with Existing Locks:** Supply mutations must respect global, site, visit, and subject read-only locks natively.

## 3. Options Considered
### Option 1: Standalone Supply Microservice
* **Overview:** Build a dedicated, isolated supply management service.
* **Pros:**
  * ✅ High separation of concerns.
* **Cons:**
  * ❌ Increases operational and network overhead.
  * ❌ Does not natively share the clinical execution database's transaction-bound audit shadow triggers, cryptographic sealing, and locking states.

### Option 2: Unified Audited Models inside Clinical Execution Service
* **Overview:** Add `IPKit`, `SiteInventory`, `KitDispensation`, and `ResupplyEvent` models as subclasses of `AuditedModel` in the existing clinical execution schema.
* **Pros:**
  * ✅ Native participation in the database-trigger-based GxP audit ledger (`audit_logs`) and soft-delete safeguards.
  * ✅ Instant conformity to `TrialLockManager`'s read-only locks based on existing `site_id`, `visit_id`, and `subject_id` fields.
  * ✅ Minimal technical complexity and robust transactional safety.
* **Cons:**
  * ❌ Schema addition within the execution microservice (mitigated by logical separation into separate dedicated tables).

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 directly utilizes the existing validated GxP auditing and locking foundation, ensuring regulatory compliance and strong operational consistency.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * All supply-related insertions and edits are automatically logged in `audit_logs` without needing direct writes to the audit ledger.
  * Native protection against hard deletes.
  * Easy integration with site and subject locking.
* **Negative Impact / Technical Debt:**
  * Requires additional database tables inside the execution domain.
* **Mitigation Strategy:** Keep tables cleanly isolated with clear docstrings and clear boundaries between supply tables and observation tables.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/execution/` (models).
* **Verification Plan:**
  * Deploy database schemas and verify model structure in unit tests under `tests/test_rtsm_supply.py`.
  * Ensure automatic audit log tracking, unique constraints, soft deletes, and trial locks function flawlessly.
