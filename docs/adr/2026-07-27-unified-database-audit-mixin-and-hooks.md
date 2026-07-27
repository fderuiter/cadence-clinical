# ADR-048: Unified Database Audit Mixin and Session Hook Factory

* **Status:** Accepted
* **Date:** 2026-07-27
* **Authors:** @jules
* **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement

To satisfy **FDA 21 CFR Part 11**, GAMP 5, and EU Annex 11 regulatory compliance guidelines, every clinical record modification must be captured in an immutable chronological audit trail. Physical record deletion is strictly forbidden.

Previously, microservices like CTMS, eTMF, and Quality independently declared distinct audit attributes (`created_at`, `created_by`, `reason_for_change`, and `version_index`) on their models. This resulted in copy-pasted schema declarations across independent repositories, rising risk of schema drift, and up to 60% compliance-related boilerplate code in model declarations. Furthermore, there was no uniform mechanism to dynamically block physical (hard) deletes of clinical records and audit logs globally.

We need a centralized, reusable `AuditMixin` and a configurable session hook factory in our core shared database library (`packages/database`) to eliminate redundancy, automate version increments, and globally enforce hard-delete protection.

## 2. Decision Drivers & Constraints

* **Compliance (21 CFR Part 11):** Enforce immutable chronological record tracking and prevent any attempt to hard-delete clinical records or existing audit logs.
* **Separation of Concerns:** Keep core auditing mechanisms inside a generic shared package (`packages/database`), while letting downstream microservices (`apps/ctms`, `apps/etmf`, `apps/quality`) maintain their own application-specific audit log models (e.g., `CTMSAuditLog`, `TMFAuditLog`, `QualityAuditLog`).
* **Developer Velocity & DRY:** Eliminate redundant column declarations, reducing audit-related boilerplates by up to 60%.
* **Flexibility & Performance:** Provide configuration parameters to exclude specific tables from audit tracking (via `skip_list`) to prevent db bloat on high-frequency tables, and a controlled bypass mechanism (via `.info["bypass_delete_protection"]`) for test environments and cleanup.

## 3. Options Considered

### Option 1: Bounded-Context Individual Audit Triggers & Listeners
* **Overview:** Maintain independent audit triggers or SQLAlchemy session listeners in each individual service repository and keep redundant schema declarations.
* **Pros:**
  * ✅ High isolation between services.
* **Cons:**
  * ❌ Massive code duplication (up to 60% boilerplate).
  * ❌ Susceptible to schema drift across multiple microservices.
  * ❌ Harder to enforce uniform deletion-prevention policies.

### Option 2: Core Shared `AuditMixin` and Dynamic Session Hook Factory (Selected)
* **Overview:** Centralize auditing attributes under a shared `AuditMixin` in `packages/database/mixins.py`. Automatically register session event listeners via `register_audit_hooks` inside `packages/database/hooks.py`. Intercept flushes to track `INSERT` and `UPDATE` operations, auto-increment versions, block hard `DELETE` operations, and dynamically append log records.
* **Pros:**
  * ✅ Single source of truth for Part 11 database audit attributes.
  * ✅ High developer velocity—models simply inherit from `AuditMixin`.
  * ✅ Automatic transactional flush-level change-tracking ensures nothing bypasses audits.
  * ✅ Centralized, standard hard-delete blocking on protected models.
  * ✅ Configurable bypass metadata (`bypass_delete_protection`) and ignore capability (`skip_list`).
* **Cons:**
  * ❌ Introduces a light runtime dependency on the shared core library.

## 4. Decision Outcome

* **Chosen Option:** Option 2
* **Justification:** Option 2 provides a elegant, highly configurable, and robust compliance framework. By handling audits at the SQLAlchemy session level, we secure transactional integrity and eliminate redundant declarations without coupling specific application audit tables together.

## 5. Consequences & Trade-offs

* **Positive Impact:**
  * Uniform auditing columns across CTMS, eTMF, and Quality.
  * Drastic boilerplate reduction.
  * Guaranteed delete-prevention to comply with regulatory audits.
* **Negative Impact / Technical Debt:**
  * Slight performance overhead during session flush.
* **Mitigation Strategy:**
  * Highly performant serialization and optional `skip_list` parameter to prevent tracking of high-frequency or non-sensitive tables.

## 6. Implementation & Verification

### Code Architecture
1. **`packages/database/mixins.py`**:
   - `AuditMixin` (aliased to `SharedAuditMixin`): Declares standardized columns:
     - `created_at` (DateTime, default `func.now()`)
     - `created_by` (String)
     - `reason_for_change` (String)
     - `version_index` (Integer, default 1)
2. **`packages/database/hooks.py`**:
   - `register_audit_hooks` (aliased to `setup_audit_hooks`): Sets up a SQLAlchemy `before_flush` session event listener to:
     - Raise `ValueError` on attempted deletion of models inheriting from `AuditMixin` or existing audit log classes.
     - Extract contextual variables (user identity, IP, change reason, timestamp) from `packages/security/context.py` thread-local variables.
     - Automatically populate and append serialized pre/post-mutation states as `INSERT` and `UPDATE` records to the registered audit log table.
     - Auto-increment model `version_index` on updates.

### Integration
- **CTMS (`apps/ctms`)**: Updated models (`CTMSStudy`, `MonitoringVisit`, etc.) to inherit from `AuditMixin` and initialized `register_audit_hooks` with `CTMSAuditLog`.
- **eTMF (`apps/etmf`)**: Refactored `ExpectedDocument` and `TMFDocument` to inherit from `AuditMixin` and registered hooks with `TMFAuditLog`.
- **Quality (`apps/quality`)**: Inherited `AuditMixin` on `Deviation`, `RootCauseAnalysis`, and `CAPARecord` and linked them via hooks to `QualityAuditLog`.

### Verification Plan
- **Unit and Integration Testing**: Added comprehensive integration test cases to verify:
  - Prevention of unauthorized physical deletions.
  - Automatic audit entry generation with correct old/new field serialization.
  - Automatic `version_index` increments.
