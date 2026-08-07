# Handoff Report — M2 Domain Models Mapping (Notifications, Organization, Interop)

## 1. Observation
Directly observed file locations, defined symbols, and import statements in `packages/core-models/` and across the repository:

- **Notifications Domain**:
  - Files:
    - `packages/core-models/notifications/__init__.py`
    - `packages/core-models/notifications/event_models.py`
  - Defined Symbols:
    - `SystemDomainEvent` (`event_models.py:6-29`): Pydantic v2 `BaseModel` (`event_id`, `event_type`, `source_service`, `study_id`, `payload`, `timestamp_utc`).
    - `NotificationDispatchJob` (`event_models.py:31-48`): Pydantic v2 `BaseModel` (`job_id`, `recipient_user_ids`, `notification_payload`, `channels`).
  - Import Sites:
    - `apps/notifications/workers/notification_worker.py:9`: `from notifications.event_models import SystemDomainEvent`
    - `apps/notifications/tests/test_notification_worker.py:7`: `from notifications.event_models import SystemDomainEvent`
    - `packages/core-models/notifications/__init__.py:1`: `from notifications.event_models import NotificationDispatchJob, SystemDomainEvent`

- **Organization Domain**:
  - Files:
    - `packages/core-models/organization_domain/__init__.py`
    - `packages/core-models/organization_domain/models.py`
  - Defined Symbols:
    - `OrganizationType` (`models.py:15-25`): `StrEnum` (`SPONSOR`, `CRO`, `IRB_IEC`, `CENTRAL_LABORATORY`, `SITE`).
    - `ClinicalStaffRole` (`models.py:27-39`): `StrEnum` (`PRINCIPAL_INVESTIGATOR`, `SUB_INVESTIGATOR`, `CRC`, `CRA_MONITOR`, `EXTERNAL_MONITOR`).
    - `TrialDuty` (`models.py:41-56`): `StrEnum` (`INFORMED_CONSENT`, `ELIGIBILITY_ASSESSMENT`, `RANDOMIZATION`, `IP_MANAGEMENT`, `CRF_COMPLETION`, `QUERY_RESOLUTION`, `MEDICAL_ASSESSMENT`, `LAB_SAMPLE_MANAGEMENT`, `SAFETY_REPORTING`, `TRIAL_OVERSIGHT`).
  - Import Sites:
    - `apps/org/main.py:15`: `from organization_domain import ClinicalStaffRole, OrganizationType`
    - `apps/org/tests/test_organization_domain.py:8`: `from organization_domain import ClinicalStaffRole, OrganizationType, TrialDuty`
    - `apps/ctms/tests/test_delegation.py:8`: `from organization_domain import ClinicalStaffRole`
    - `packages/security/delegation.py:9`: `from organization_domain import ClinicalStaffRole`
    - `packages/core-models/organization_domain/__init__.py:5`: `from organization_domain.models import ClinicalStaffRole, OrganizationType, TrialDuty`

- **Interop Domain (`sync_engine`)**:
  - Files:
    - `packages/core-models/sync_engine.py`
  - Defined Symbols:
    - `SignatureValidationError` (`sync_engine.py:12-15`): Exception class (`ValueError`).
    - `SyncMetadata` (`sync_engine.py:18-35`): Pydantic v2 `BaseModel` (`timestamps`, `modified_by`, `signature`).
    - `SyncRecord` (`sync_engine.py:37-50`): Pydantic v2 `BaseModel` (`deduplication_key`, `data`, `metadata`).
    - `normalize_to_utc` (`sync_engine.py:52-56`): Utility function.
    - `get_signature_payload` (`sync_engine.py:59-78`): Utility function.
    - `verify_record_signature` (`sync_engine.py:81-88`): Utility function.
    - `reconcile_records` (`sync_engine.py:91-239`): Conflict resolution function (`CLIENT_WINS`, `SERVER_WINS`, `MERGE`).
  - Import Sites:
    - `apps/interop/main.py:42`: `from apps.interop.sync_engine import SignatureValidationError, SyncMetadata, SyncRecord, reconcile_records, verify_record_signature`
    - `apps/interop/sync_engine.py:1-23`: Dynamic wrapper module loading `packages/core-models/sync_engine.py`.
    - `apps/interop/tests/test_sync_engine.py:5`: `from apps.interop.sync_engine import SignatureValidationError, SyncMetadata, SyncRecord, get_signature_payload, reconcile_records, verify_record_signature`
    - `apps/interop/tests/test_interop_defeated.py:21`: `from apps.interop.sync_engine import SignatureValidationError, SyncMetadata, SyncRecord, get_signature_payload, reconcile_records, verify_record_signature`
    - `apps/ctms/main.py:2551`: `import sync_engine`

---

## 2. Logic Chain
1. **Target Mapping**:
   - `packages/core-models/notifications/` models map directly to owning microservice `apps/notifications/src/domain/`.
   - `packages/core-models/organization_domain/` models map directly to owning microservice `apps/org/src/domain/`.
   - `packages/core-models/sync_engine.py` maps directly to owning microservice `apps/interop/src/domain/sync_engine.py`.
2. **Import Site Adjustments**:
   - Notifications: Update `apps/notifications/workers/notification_worker.py` and `apps/notifications/tests/test_notification_worker.py` from `notifications.event_models` to `apps.notifications.src.domain.event_models`.
   - Organization: Update `apps/org/main.py`, `apps/org/tests/test_organization_domain.py`, `apps/ctms/tests/test_delegation.py`, and `packages/security/delegation.py` from `organization_domain` to `apps.org.src.domain.models`.
   - Interop: Relocate `sync_engine.py` to `apps/interop/src/domain/sync_engine.py`. Replace or update `apps/interop/sync_engine.py` shim so that `apps/interop/main.py`, `apps/interop/tests/test_sync_engine.py`, `apps/interop/tests/test_interop_defeated.py`, and `apps/ctms/main.py` import from `apps.interop.src.domain.sync_engine`.
3. **Risk Analysis**:
   - `apps/ctms/main.py:2551` has an in-line `import sync_engine` which currently resolves to `packages/core-models/sync_engine.py`. During M2, this must be updated to `from apps.interop.src.domain.sync_engine import ...`. In M4, this cross-service import will be replaced with an ACL DTO / REST HTTP client call.
   - `packages/security/delegation.py:9` imports `ClinicalStaffRole` from `organization_domain`. Shared packages under `packages/security/` should be updated to `from apps.org.src.domain.models import ClinicalStaffRole`.

---

## 3. Caveats
- No source code modifications were performed in this read-only exploration phase.
- OpenAPI schema re-export and test suite execution will be performed by worker/reviewer agents in subsequent execution steps.

---

## 4. Conclusion
- Source models, defined symbols, target paths, and all import sites across `apps/`, `packages/`, `scripts/`, and `tests/` for Notifications, Organization, and Interop are 100% cataloged and mapped.
- Detailed report saved to `/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_explorer_m2_3/analysis.md`.

---

## 5. Verification Method
1. Inspect mapped source files using `view_file`:
   - `packages/core-models/notifications/event_models.py`
   - `packages/core-models/organization_domain/models.py`
   - `packages/core-models/sync_engine.py`
2. Verify import sites using `grep_search`:
   - `grep_search` query `notifications.event_models`
   - `grep_search` query `organization_domain`
   - `grep_search` query `sync_engine`
3. Check analysis report:
   - `view_file` on `/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_explorer_m2_3/analysis.md`
