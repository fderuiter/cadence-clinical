# ADR-135: Item-Level Source Data Verification (SDV) GxP Compliance and RBAC Isolation

* **Status:** Accepted
* **Date:** 2026-09-08
* **Authors:** @jules
* **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement
To enforce 21 CFR Part 11 and clinical data integrity, our clinical trial execution engine must support granular, item-level Source Data Verification (SDV). Previously, Source Data Verification was only performed at coarse levels (e.g., page, form, or visit level) without granular audit trail capture or role-based restriction on individual item verification.

We must implement complete item-level SDV flagging, resolution, and verification cascading with full GxP compliance. This requires updating shared security schemas in `packages/security/rbac.py`, creating dedicated endpoint models under `packages/core-models/execution/sdv_transport_models.py`, database changes, and logging detailed structured GxP events to the database audit ledger under `apps/execution/database/audit.py`.

This ADR specifically relates to requirements under **PRD-SYS-001**, **PRD-QRY-005**, and **PRD-QRY-006**.

## 2. Decision Drivers & Constraints
* **Driver 1:** Regulatory compliance (21 CFR Part 11 electronic records, audit logging, and data verification traceability).
* **Driver 2:** Security and RBAC separation, ensuring that only users with canonical CRA roles can perform SDV operations while other roles are restricted.
* **Driver 3:** Robust transaction-level audit trails recording verification status changes.
* **Constraint:** Enforcing study-level tenant isolation across all newly added endpoints.

## 3. Options Considered
### Option 1: Ad-hoc Flagging Logic without Structured GxP Log Integration
* **Overview:** Implement item flagging on clinical observations using raw JSON attributes on observation tables, without central integration with the GxP audit ledger or strict RBAC validations.
* **Pros:**
  * ✅ Fast implementation within execution routers.
* **Cons:**
  * ❌ Violates GxP compliance requirements for persistent, immutable audit history of changes.
  * ❌ No central permission gating in packages/security/rbac.py, increasing security vulnerability risks.

### Option 2: Centralized RBAC Isolation and GxP-Compliant Structured Audit Trail Cascade [Selected]
* **Overview:** Map fine-grained `sdv` flag permissions in `packages/security/rbac.py` and implement formal endpoint-level schemas in `sdv_transport_models.py`. Ensure that every flag or resolution action invokes structured ledger logging within `apps/execution/database/audit.py` with full cascading of parent page/visit verification drop rules.
* **Pros:**
  * ✅ Fully compliant with 21 CFR Part 11 and regulatory auditing expectations.
  * ✅ Highly modular and cleanly integrated with existing audit database tables.
  * ✅ Prevents un-verified parent structures from showing as verified when children are modified.
* **Cons:**
  * ❌ Requires updating shared code in `packages/security/rbac.py` and `apps/execution/database/audit.py`.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 provides complete assurance of data traceability, prevents compliance drift, and perfectly meets PRD-SYS-001 and SDV verification standards.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * ✅ High data integrity with automatic cascading parent verification resets.
  * ✅ Explicitly validated by automated test coverage for item-level SDV.
* **Negative Impact / Technical Debt:**
  * ❌ Minor database query overhead for updating verification states, mitigated by indexed column designs.

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  * `packages/security/rbac.py`
  * `apps/execution/database/audit.py`
  * `apps/execution/routers/sdv.py`
  * `packages/core-models/execution/sdv_transport_models.py`
* **Verification Plan:**
  * Execute specialized RBAC and functional suite: `uv run pytest tests/test_sdv_item_level_rbac.py tests/test_item_level_sdv_endpoints.py --no-cov`.
