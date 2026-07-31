# ADR-114: eSignature-Backed Delegation of Authority (DOA) Log and Task Delegation Service

* **Status:** Accepted
* **Date:** 2026-08-26
* **Authors:** @jules
* **Deciders:** @fderuiter, @architect-lead

---

## 1. Context & Problem Statement
In clinical trials, ICH GCP E6(R2) Section 4.1.5 mandates that the Principal Investigator (PI) maintain a current, signed list of appropriately qualified persons to whom the PI has delegated significant trial-related duties. This list is commonly known as the Delegation of Authority (DOA) log.

The platform requires a robust, secure, and 21 CFR Part 11 compliant service to administer site personnel task delegation, validate training credentials (such as GCP certification), and capture electronic signatures from the PI during task delegation approvals.

This decision satisfies requirement PRD-SYS-001 under the eTMF & Regulated Document Management stream.

## 2. Decision Drivers & Constraints
* **Driver 1 (GxP Compliance & 21 CFR Part 11):** Any mutation of the Delegation of Authority records must preserve an append-only, chronologically ordered audit trail tracking who made the change, when, and the GxP-compliant reason for change.
* **Driver 2 (Electronic Signatures):** Sign-off actions by the PI must re-authenticate credentials (password and MFA/TOTP) and embed cryptographically verifiable metadata in the delegation record.
* **Driver 3 (Training Verification):** Site staff must possess valid GCP training before any trial-related task (e.g. Obtaining Informed Consent) can be delegated to them.

## 3. Options Considered

### Option 1: Inline Task Delegation in Organization Directory
Maintain delegation lists directly on the standard personnel records inside the `apps/org` service.
* **Pros:** Simpler domain hierarchy; personnel and role mapping are already present.
* **Cons:** Mixes basic directory lookup operations with highly regulated GxP workflow states (signatures, training gates) which require specialized execution audit logs.

### Option 2: Dedicated eSignature-Backed DOA Service (Selected)
Establish a dedicated database schema and service boundary inside `apps/ctms` with specialized GxP models (`DOADelegationRecord`, `SiteStaffMember`, `DOAAuditLog`) that handles task delegating, credentials re-authentication, signature verification hash generation, and end-dating.
* **Pros:** Strictly isolates regulated trial-specific delegation logs from general organizational profiles, enables strict credentials re-authentication and auditing, and supports granular end-dating and revocation lifecycles.
* **Cons:** Requires schema expansion on execution-related tables.

---

## 4. Decision Outcome

**Chosen Option:** Option 2

### Database Models
We expand the execution database models (`apps/execution/database/models.py`) with three distinct GxP entities:
* **SiteStaffMember:** Represents site personnel and captures clinical training status (e.g. `has_gcp_training`).
* **DOADelegationRecord:** Represents a delegated clinical task (e.g., obtaining informed consent `ICF_CONSENT`, entering clinical observation data `ECRF_ENTRY`, storing and dispensing investigational product `DRUG_DISPENSE`, filing serious adverse event notifications `SAE_REPORTING`) with transition statuses (`PENDING_PI_APPROVAL`, `ACTIVE`, `REVOKED`), eSignature metadata, and active/inactive scope flags.
* **DOAAuditLog:** An append-only audit trail dedicated to DOA modifications, capturing actions such as `DELEGATE_TASK`, `APPROVE_DELEGATION`, and `REVOKE_DELEGATION` with reasons and details.

### Workflow Services
We implement the core services under `apps/ctms/services/doa_service.py`:
* **Task Delegation (`delegate_task`):** Queries `SiteStaffMember` to verify completed GCP training certificates, and creates a `DOADelegationRecord` in `PENDING_PI_APPROVAL` status while writing to `DOAAuditLog`.
* **PI Sign-Off (`approve_delegation_with_esignature`):** Re-authenticates PI credentials (password and TOTP/MFA checks), computes a SHA-256 eSignature `verification_hash` based on the record details and signed timestamp, transitions status to `ACTIVE`, and writes to `DOAAuditLog`.
* **Delegation Revocation (`revoke_delegation`):** Marks the task end date, disables the record's active scope (`is_active = False`), transitions status to `REVOKED`, and writes to `DOAAuditLog`.

---

## 5. Consequences & Trade-offs
* **Positive Impact:**
  - Robust database-native enforcement of GxP workflows and Part 11 auditing.
  - Complete re-authentication check ensures PIs cannot approve delegations without providing valid password/TOTP credentials.
  - Strict training gate blocks delegation to unqualified or untrained personnel.
* **Negative Impact:**
  - Standard database migrations are required to backfill and deploy these GxP schemas.
* **Cross-Reference:**
  - This service works alongside central OIDC RBAC definitions located in [ADR-095](2026-08-08-centralized-rbac-toolkit.md).

---

## 6. Implementation & Verification

### Affected Files
* `apps/execution/database/models.py` (Added `SiteStaffMember`, `DOADelegationRecord`, and `DOAAuditLog` schemas)
* `apps/ctms/services/doa_service.py` (Implemented task delegation and Part 11 eSignature validation workflows)
* `tests/test_doa_service.py` (Created service-level unit test suite)

### Verification & Reproducible Commands
Execute the complete test suite and linter checks locally before committing:
```bash
uv run pytest tests/test_doa_service.py --no-cov
uv run ruff check . --fix
uv run ruff format .
python3 scripts/validate_adrs.py
```
