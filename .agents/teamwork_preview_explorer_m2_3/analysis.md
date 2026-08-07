# Detailed Analysis: Notifications, Organization, and Interop Domain Models Migration (M2)

## 1. Executive Summary
This document maps all source files, model classes/functions, import sites, target destinations, and dependency risks for the Notifications (`notifications`), Organization (`organization_domain`), and Interop (`sync_engine`) domain models currently housed in `packages/core-models/`.

---

## 2. Domain Model Mapping

### 2.1 Notifications Domain Models
- **Source Files in `packages/core-models/`**:
  - `packages/core-models/notifications/__init__.py`
  - `packages/core-models/notifications/event_models.py`

- **Classes & Functions Defined**:
  1. `SystemDomainEvent` (`packages/core-models/notifications/event_models.py:6-29`)
     - Type: Pydantic v2 `BaseModel`
     - Description: Represents an asynchronous domain event emitted across clinical microservices.
     - Attributes:
       - `event_id: str`: Unique UUID or identifier for the domain event.
       - `event_type: str`: Type of clinical/system event (e.g., `EDC_QUERY_RAISED`).
       - `source_service: str`: Microservice emitting the event (e.g., `edc`, `etmf`).
       - `study_id: str`: Associated clinical study/trial ID.
       - `payload: dict[str, Any]`: Event-specific metadata or structured payload details.
       - `timestamp_utc: str`: ISO-8601 UTC timestamp of event generation.
  2. `NotificationDispatchJob` (`packages/core-models/notifications/event_models.py:31-48`)
     - Type: Pydantic v2 `BaseModel`
     - Description: Represents a resolved, ready-to-deliver notification dispatch job.
     - Attributes:
       - `job_id: str`: Unique UUID for tracking the notification delivery job.
       - `recipient_user_ids: list[str]`: Target Keycloak user IDs.
       - `notification_payload: dict[str, Any]`: Structured content for the notification.
       - `channels: list[Literal["WEBSOCKET", "EMAIL", "SMS"]]`: Target distribution channels.
  3. Package Exports (`packages/core-models/notifications/__init__.py:1-10`)
     - Re-exports `SystemDomainEvent` and `NotificationDispatchJob`.

- **Target Destination Paths**:
  - `apps/notifications/src/domain/__init__.py`
  - `apps/notifications/src/domain/event_models.py`

- **Import Sites & References**:
  1. `apps/notifications/workers/notification_worker.py:9`
     - `from notifications.event_models import SystemDomainEvent`
  2. `apps/notifications/tests/test_notification_worker.py:7`
     - `from notifications.event_models import SystemDomainEvent`
  3. `packages/core-models/notifications/__init__.py:1`
     - `from notifications.event_models import NotificationDispatchJob, SystemDomainEvent`

- **Import Conflicts & Dependency Risks**:
  - **Risk**: Low. Only the notifications service worker and its test suite import `SystemDomainEvent` directly.
  - **Cross-Service Triggers**: External microservices (e.g., `etmf`, `execution`, `tickets`) communicate with `notifications` via HTTP REST client endpoints (`POST /api/v1/notifications/send`), avoiding direct domain model imports.

---

### 2.2 Organization Domain Models
- **Source Files in `packages/core-models/`**:
  - `packages/core-models/organization_domain/__init__.py`
  - `packages/core-models/organization_domain/models.py`

- **Classes & Functions Defined**:
  1. `OrganizationType` (`packages/core-models/organization_domain/models.py:15-25`)
     - Type: `enum.StrEnum`
     - Values: `SPONSOR = "sponsor"`, `CRO = "CRO"`, `IRB_IEC = "IRB/IEC"`, `CENTRAL_LABORATORY = "central laboratory"`, `SITE = "site"`.
  2. `ClinicalStaffRole` (`packages/core-models/organization_domain/models.py:27-39`)
     - Type: `enum.StrEnum`
     - Values: `PRINCIPAL_INVESTIGATOR = "Principal Investigator"`, `SUB_INVESTIGATOR = "Sub-Investigator"`, `CRC = "CRC"`, `CRA_MONITOR = "CRA/Monitor"`, `EXTERNAL_MONITOR = "External Monitor"`.
  3. `TrialDuty` (`packages/core-models/organization_domain/models.py:41-56`)
     - Type: `enum.StrEnum`
     - Values: `INFORMED_CONSENT`, `ELIGIBILITY_ASSESSMENT`, `RANDOMIZATION`, `IP_MANAGEMENT`, `CRF_COMPLETION`, `QUERY_RESOLUTION`, `MEDICAL_ASSESSMENT`, `LAB_SAMPLE_MANAGEMENT`, `SAFETY_REPORTING`, `TRIAL_OVERSIGHT`.
  4. Package Exports (`packages/core-models/organization_domain/__init__.py:1-18`)
     - Re-exports `ClinicalStaffRole`, `OrganizationType`, `TrialDuty`, and `AuditFields` (from `packages.database.audit`).

- **Target Destination Paths**:
  - `apps/org/src/domain/__init__.py`
  - `apps/org/src/domain/models.py`

- **Import Sites & References**:
  1. `apps/org/main.py:15`
     - `from organization_domain import ClinicalStaffRole, OrganizationType`
  2. `apps/org/tests/test_organization_domain.py:8`
     - `from organization_domain import ClinicalStaffRole, OrganizationType, TrialDuty`
  3. `apps/ctms/tests/test_delegation.py:8`
     - `from organization_domain import ClinicalStaffRole`
  4. `packages/security/delegation.py:9`
     - `from organization_domain import ClinicalStaffRole`
  5. `packages/core-models/organization_domain/__init__.py:5`
     - `from organization_domain.models import ClinicalStaffRole, OrganizationType, TrialDuty`

- **Import Conflicts & Dependency Risks**:
  - **Shared Security Package Coupling**: `packages/security/delegation.py:9` imports `ClinicalStaffRole` from `organization_domain`. Shared packages under `packages/` should ideally not depend on microservice-owned models (`apps/org/src/domain/models.py`). When migrating, `packages/security/delegation.py` must update its import to `from apps.org.src.domain.models import ClinicalStaffRole` (or duplicate the enum if strict package independence is required).
  - **Cross-Service Test Import**: `apps/ctms/tests/test_delegation.py:8` imports `ClinicalStaffRole` from `organization_domain`. Must be updated to `from apps.org.src.domain.models import ClinicalStaffRole`.

---

### 2.3 Interop Domain Models (`sync_engine`)
- **Source Files in `packages/core-models/`**:
  - `packages/core-models/sync_engine.py`

- **Classes & Functions Defined**:
  1. `SignatureValidationError` (`packages/core-models/sync_engine.py:12-15`)
     - Type: Exception class (`ValueError`)
     - Description: Raised when signature validation fails or is missing when required.
  2. `SyncMetadata` (`packages/core-models/sync_engine.py:18-35`)
     - Type: Pydantic v2 `BaseModel`
     - Attributes: `timestamps: dict[str, datetime]`, `modified_by: str`, `signature: str | None`.
  3. `SyncRecord` (`packages/core-models/sync_engine.py:37-50`)
     - Type: Pydantic v2 `BaseModel`
     - Attributes: `deduplication_key: str`, `data: dict[str, Any]`, `metadata: SyncMetadata`.
  4. `normalize_to_utc(dt: datetime) -> datetime` (`packages/core-models/sync_engine.py:52-56`)
     - Function: Normalizes datetime objects to timezone-aware UTC.
  5. `get_signature_payload(record: SyncRecord) -> dict[str, Any]` (`packages/core-models/sync_engine.py:59-78`)
     - Function: Serializes record data and timestamps to canonical ISO-8601 payload for signature checks.
  6. `verify_record_signature(record: SyncRecord, secret: bytes) -> bool` (`packages/core-models/sync_engine.py:81-88`)
     - Function: Validates HMAC-SHA256 signature against canonical payload using `packages.security.signing.verify_canonical_signature`.
  7. `reconcile_records(...) -> dict[str, Any]` (`packages/core-models/sync_engine.py:91-239`)
     - Function: Executes conflict resolution algorithm (`CLIENT_WINS`, `SERVER_WINS`, `MERGE` with LWW and lexicographical tiebreaking).

- **Target Destination Path**:
  - `apps/interop/src/domain/sync_engine.py`

- **Import Sites & References**:
  1. `apps/interop/main.py:42`
     - `from apps.interop.sync_engine import SignatureValidationError, SyncMetadata, SyncRecord, reconcile_records, verify_record_signature`
  2. `apps/interop/sync_engine.py:1-23`
     - Dynamic loader shim that imports from `packages/core-models/sync_engine.py`.
  3. `apps/interop/tests/test_sync_engine.py:5`
     - `from apps.interop.sync_engine import SignatureValidationError, SyncMetadata, SyncRecord, get_signature_payload, reconcile_records, verify_record_signature`
  4. `apps/interop/tests/test_interop_defeated.py:21`
     - `from apps.interop.sync_engine import SignatureValidationError, SyncMetadata, SyncRecord, get_signature_payload, reconcile_records, verify_record_signature`
  5. `apps/ctms/main.py:2551`
     - `import sync_engine` inside monitoring visit reconciliation handler.

- **Import Conflicts & Dependency Risks**:
  - **Runtime In-Process Cross-Service Import in CTMS**: `apps/ctms/main.py:2551` imports `sync_engine` in-process during monitoring visit sync reconciliation. Relocating `sync_engine.py` to `apps/interop/src/domain/sync_engine.py` requires updating CTMS to `from apps.interop.src.domain.sync_engine import SignatureValidationError, SyncMetadata, SyncRecord, reconcile_records, verify_record_signature` in M2, until M4 replaces this direct import with a local `SyncEngineDTO` and REST client call.
  - **Dynamic File Loader Breakdown**: `apps/interop/sync_engine.py` uses `importlib.util.spec_from_file_location` pointing to `../../packages/core-models/sync_engine.py`. Once `packages/core-models/sync_engine.py` is removed, this file loader will fail unless `apps/interop/sync_engine.py` is replaced with the relocated domain module or converted to re-export `apps.interop.src.domain.sync_engine`.

---

## 3. Summary Mapping Table

| Domain | Source File in `packages/core-models/` | Classes & Functions Defined | Target Destination Path | Key Import Sites |
|---|---|---|---|---|
| Notifications | `notifications/__init__.py`<br>`notifications/event_models.py` | `SystemDomainEvent`<br>`NotificationDispatchJob` | `apps/notifications/src/domain/__init__.py`<br>`apps/notifications/src/domain/event_models.py` | `apps/notifications/workers/notification_worker.py`<br>`apps/notifications/tests/test_notification_worker.py` |
| Organization | `organization_domain/__init__.py`<br>`organization_domain/models.py` | `OrganizationType`<br>`ClinicalStaffRole`<br>`TrialDuty` | `apps/org/src/domain/__init__.py`<br>`apps/org/src/domain/models.py` | `apps/org/main.py`<br>`apps/org/tests/test_organization_domain.py`<br>`apps/ctms/tests/test_delegation.py`<br>`packages/security/delegation.py` |
| Interop | `sync_engine.py` | `SignatureValidationError`<br>`SyncMetadata`<br>`SyncRecord`<br>`normalize_to_utc`<br>`get_signature_payload`<br>`verify_record_signature`<br>`reconcile_records` | `apps/interop/src/domain/sync_engine.py` | `apps/interop/main.py`<br>`apps/interop/tests/test_sync_engine.py`<br>`apps/interop/tests/test_interop_defeated.py`<br>`apps/ctms/main.py` |
