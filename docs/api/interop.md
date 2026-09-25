# FHIR & eSource Gateway

> **Functional Domain:** `Patient Engagement & Interoperability`  
> **Service Host Port:** `localhost:8007`  
> **Gateway Proxy Prefix:** `/api/v1/interop`  
> **Persistence Backend:** `PostgreSQL / SQLite`  

## Overview

SMART on FHIR EHR integration, eSource reconciliation drawers, and Open mHealth continuous biometric sensor streaming adapters.

---

## Endpoints Summary

| Method | Path | Summary | GxP Auth & Signatures |
| :--- | :--- | :--- | :--- |
| `POST` | [/api/v1/interop/assignments](#post-api-v1-interop-assignments) | Create Subject Assignment | 🔒 Step-Up Sig Token |
| `GET` | [/api/v1/interop/assignments/subject/{subject_id}](#get-api-v1-interop-assignments-subject-subject_id) | Get Subject Assignments | 👀 Read-Only |
| `POST` | [/api/v1/interop/cdr/export/dataset-json](#post-api-v1-interop-cdr-export-dataset-json) | Export clinical datasets in official CDISC Dataset-JSON 1.1 / 2.0 | ✍️ Audited Write |
| `POST` | [/api/v1/interop/cdr/export/define-xml](#post-api-v1-interop-cdr-export-define-xml) | Auto-compile and export CDISC Define-XML v2.1 metadata specification | ✍️ Audited Write |
| `GET` | [/api/v1/interop/cdr/metadata/catalog](#get-api-v1-interop-cdr-metadata-catalog) | Retrieve CDISC domain and variable metadata catalog | 👀 Read-Only |
| `POST` | [/api/v1/interop/cdr/pipeline/adam](#post-api-v1-interop-cdr-pipeline-adam) | Execute CDISC ADaM 4-dataset derivation engine | ✍️ Audited Write |
| `POST` | [/api/v1/interop/cdr/pipeline/execute](#post-api-v1-interop-cdr-pipeline-execute) | Execute end-to-end continuous CDISC SDTM/ADaM pipeline and compiler | ✍️ Audited Write |
| `GET` | [/api/v1/interop/cdr/pipeline/results/{execution_id}](#get-api-v1-interop-cdr-pipeline-results-execution_id) | Retrieve execution manifest for a pipeline run | 👀 Read-Only |
| `POST` | [/api/v1/interop/cdr/pipeline/sdtm](#post-api-v1-interop-cdr-pipeline-sdtm) | Execute CDISC SDTM 10-domain transformation engine | ✍️ Audited Write |
| `POST` | [/api/v1/interop/cdr/validate/dataset-json](#post-api-v1-interop-cdr-validate-dataset-json) | Validate a Dataset-JSON 1.1 / 2.0 payload | ✍️ Audited Write |
| `POST` | [/api/v1/interop/cdr/validate/define-xml](#post-api-v1-interop-cdr-validate-define-xml) | Validate a CDISC Define-XML v2.1 document | ✍️ Audited Write |
| `POST` | [/api/v1/interop/dct/dtp/shipments](#post-api-v1-interop-dct-dtp-shipments) | Create Direct-to-Patient (DTP) courier dispatch order | ✍️ Audited Write |
| `GET` | [/api/v1/interop/dct/dtp/shipments](#get-api-v1-interop-dct-dtp-shipments) | List Direct-to-Patient drug shipments | 👀 Read-Only |
| `GET` | [/api/v1/interop/dct/dtp/shipments/{tracking_number}](#get-api-v1-interop-dct-dtp-shipments-tracking_number) | Retrieve single DTP shipment details by tracking number | 👀 Read-Only |
| `POST` | [/api/v1/interop/dct/dtp/shipments/{tracking_number}/delivery](#post-api-v1-interop-dct-dtp-shipments-tracking_number-delivery) | Confirm final delivery with recipient signature | ✍️ Audited Write |
| `POST` | [/api/v1/interop/dct/dtp/shipments/{tracking_number}/handoff](#post-api-v1-interop-dct-dtp-shipments-tracking_number-handoff) | Record Chain of Custody (CoC) handoff event | ✍️ Audited Write |
| `POST` | [/api/v1/interop/dct/dtp/shipments/{tracking_number}/temperature](#post-api-v1-interop-dct-dtp-shipments-tracking_number-temperature) | Ingest cold-chain temperature telemetry points & calculate MKT | ✍️ Audited Write |
| `POST` | [/api/v1/interop/dct/econsent/pediatric-assent](#post-api-v1-interop-dct-econsent-pediatric-assent) | Submit tiered pediatric assent & LAR dual-signature manifest | ✍️ Audited Write |
| `GET` | [/api/v1/interop/dct/econsent/pediatric-assent](#get-api-v1-interop-dct-econsent-pediatric-assent) | List executed pediatric assents | 👀 Read-Only |
| `GET` | [/api/v1/interop/dct/econsent/pediatric-assent/{assent_id}/verify](#get-api-v1-interop-dct-econsent-pediatric-assent-assent_id-verify) | Verify cryptographic bundle seal of pediatric assent | 👀 Read-Only |
| `POST` | [/api/v1/interop/dct/home-health/visits](#post-api-v1-interop-dct-home-health-visits) | Schedule decentralized in-home nursing visit | ✍️ Audited Write |
| `GET` | [/api/v1/interop/dct/home-health/visits](#get-api-v1-interop-dct-home-health-visits) | List in-home visiting clinician appointments | 👀 Read-Only |
| `GET` | [/api/v1/interop/dct/home-health/visits/{visit_id}](#get-api-v1-interop-dct-home-health-visits-visit_id) | Retrieve single in-home visit details | 👀 Read-Only |
| `POST` | [/api/v1/interop/dct/home-health/visits/{visit_id}/status](#post-api-v1-interop-dct-home-health-visits-visit_id-status) | Update visiting nurse visit progress and completed procedures | ✍️ Audited Write |
| `POST` | [/api/v1/interop/dct/home-health/visits/{visit_id}/sync-ecrf](#post-api-v1-interop-dct-home-health-visits-visit_id-sync-ecrf) | Reconcile offline-cached mobile eCRF clinical data entry | ✍️ Audited Write |
| `GET` | [/api/v1/interop/dct/metrics/{study_id}](#get-api-v1-interop-dct-metrics-study_id) | Retrieve aggregated DCT operational metrics | 👀 Read-Only |
| `POST` | [/api/v1/interop/dct/telehealth/sessions](#post-api-v1-interop-dct-telehealth-sessions) | Schedule WebRTC telehealth video visit | ✍️ Audited Write |
| `GET` | [/api/v1/interop/dct/telehealth/sessions](#get-api-v1-interop-dct-telehealth-sessions) | List telehealth video consultation sessions | 👀 Read-Only |
| `GET` | [/api/v1/interop/dct/telehealth/sessions/{session_id}](#get-api-v1-interop-dct-telehealth-sessions-session_id) | Retrieve single telehealth session details | 👀 Read-Only |
| `POST` | [/api/v1/interop/dct/telehealth/sessions/{session_id}/archive-recording](#post-api-v1-interop-dct-telehealth-sessions-session_id-archive-recording) | Archive GxP recording with SHA-256 digital seal (21 CFR Part 11) | ✍️ Audited Write |
| `POST` | [/api/v1/interop/dct/telehealth/sessions/{session_id}/co-view](#post-api-v1-interop-dct-telehealth-sessions-session_id-co-view) | Synchronize eConsent interactive co-viewing state | ✍️ Audited Write |
| `POST` | [/api/v1/interop/dct/telehealth/sessions/{session_id}/signaling](#post-api-v1-interop-dct-telehealth-sessions-session_id-signaling) | Exchange WebRTC session signaling message | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/interop/dct/telehealth/sessions/{session_id}/token](#post-api-v1-interop-dct-telehealth-sessions-session_id-token) | Generate HMAC-SHA256 signed room authorization token | ✍️ Audited Write |
| `POST` | [/api/v1/interop/devices/export-package](#post-api-v1-interop-devices-export-package) | Generate Export Package Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/interop/devices/mir-report](#post-api-v1-interop-devices-mir-report) | Generate Mdr Incident Report Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/interop/devices/sdtm/dd](#post-api-v1-interop-devices-sdtm-dd) | Generate Sdtm Dd Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/interop/devices/sdtm/do](#post-api-v1-interop-devices-sdtm-do) | Generate Sdtm Do Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/interop/epro/quarantine](#get-api-v1-interop-epro-quarantine) | List Quarantined Submissions | 👀 Read-Only |
| `GET` | [/api/v1/interop/epro/quarantine/{id}](#get-api-v1-interop-epro-quarantine-id) | Get Quarantined Submission | 👀 Read-Only |
| `POST` | [/api/v1/interop/epro/quarantine/{id}/edit](#post-api-v1-interop-epro-quarantine-id-edit) | Edit Quarantined Submission | ✍️ Audited Write |
| `POST` | [/api/v1/interop/epro/quarantine/{id}/replay](#post-api-v1-interop-epro-quarantine-id-replay) | Replay Quarantined Submission | ✍️ Audited Write |
| `POST` | [/api/v1/interop/epro/submit](#post-api-v1-interop-epro-submit) | Epro Submit | ✍️ Audited Write |
| `POST` | [/api/v1/interop/epro/sync](#post-api-v1-interop-epro-sync) | Epro Sync | ✍️ Audited Write |
| `GET` | [/api/v1/interop/esource/candidates](#get-api-v1-interop-esource-candidates) | List Candidates | 👀 Read-Only |
| `GET` | [/api/v1/interop/esource/candidates/{candidate_id}](#get-api-v1-interop-esource-candidates-candidate_id) | Get Candidate | 👀 Read-Only |
| `POST` | [/api/v1/interop/esource/endpoints](#post-api-v1-interop-esource-endpoints) | Register Ehr Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/interop/esource/endpoints](#get-api-v1-interop-esource-endpoints) | List Ehr Endpoints | 👀 Read-Only |
| `POST` | [/api/v1/interop/esource/ingest](#post-api-v1-interop-esource-ingest) | Ingest Fhir Bundle | ✍️ Audited Write |
| `POST` | [/api/v1/interop/esource/poll](#post-api-v1-interop-esource-poll) | Poll Hospital Ehr | ✍️ Audited Write |
| `GET` | [/api/v1/interop/esource/provenance/{candidate_id}](#get-api-v1-interop-esource-provenance-candidate_id) | Get Provenance Audit | 👀 Read-Only |
| `POST` | [/api/v1/interop/esource/reconcile/accept](#post-api-v1-interop-esource-reconcile-accept) | Accept Candidate | ✍️ Audited Write |
| `POST` | [/api/v1/interop/esource/reconcile/query](#post-api-v1-interop-esource-reconcile-query) | Raise Clinical Query | ✍️ Audited Write |
| `POST` | [/api/v1/interop/esource/reconcile/reject](#post-api-v1-interop-esource-reconcile-reject) | Reject Candidate | ✍️ Audited Write |
| `POST` | [/api/v1/interop/esource/smart/assertion](#post-api-v1-interop-esource-smart-assertion) | Generate Client Assertion | ✍️ Audited Write |
| `POST` | [/api/v1/interop/esource/smart/verify](#post-api-v1-interop-esource-smart-verify) | Verify Client Assertion | ✍️ Audited Write |
| `GET` | [/api/v1/interop/fhir/concept-maps](#get-api-v1-interop-fhir-concept-maps) | List Concept Maps | 👀 Read-Only |
| `POST` | [/api/v1/interop/fhir/pre-screen](#post-api-v1-interop-fhir-pre-screen) | Fhir Pre Screen | ✍️ Audited Write |
| `POST` | [/api/v1/interop/fhir/prefill](#post-api-v1-interop-fhir-prefill) | Fhir Prefill | ✍️ Audited Write |
| `POST` | [/api/v1/interop/fhir/semantic-map](#post-api-v1-interop-fhir-semantic-map) | Fhir Semantic Map | ✍️ Audited Write |
| `POST` | [/api/v1/interop/instruments](#post-api-v1-interop-instruments) | Create Instrument | ✍️ Audited Write |
| `GET` | [/api/v1/interop/instruments/{id}](#get-api-v1-interop-instruments-id) | Get Instrument | 👀 Read-Only |
| `POST` | [/api/v1/interop/lakehouse/ingest](#post-api-v1-interop-lakehouse-ingest) | Ingest heterogeneous clinical stream data | ✍️ Audited Write |
| `GET` | [/api/v1/interop/lakehouse/metrics/{study_id}](#get-api-v1-interop-lakehouse-metrics-study_id) | Retrieve real-time Lakehouse stream and snapshot metrics | 👀 Read-Only |
| `POST` | [/api/v1/interop/lakehouse/snapshots](#post-api-v1-interop-lakehouse-snapshots) | Create an immutable, version-pinned clinical data snapshot | ✍️ Audited Write |
| `GET` | [/api/v1/interop/lakehouse/snapshots](#get-api-v1-interop-lakehouse-snapshots) | List version-pinned snapshots for a study | 👀 Read-Only |
| `GET` | [/api/v1/interop/lakehouse/snapshots/{snapshot_id}](#get-api-v1-interop-lakehouse-snapshots-snapshot_id) | Retrieve snapshot metadata and seal info | 👀 Read-Only |
| `GET` | [/api/v1/interop/lakehouse/snapshots/{snapshot_id}/audit-trail](#get-api-v1-interop-lakehouse-snapshots-snapshot_id-audit-trail) | Retrieve 21 CFR Part 11 audit log for a snapshot | 👀 Read-Only |
| `GET` | [/api/v1/interop/lakehouse/snapshots/{snapshot_id}/data](#get-api-v1-interop-lakehouse-snapshots-snapshot_id-data) | Fetch snapshot pool data records via GET | 👀 Read-Only |
| `POST` | [/api/v1/interop/lakehouse/snapshots/{snapshot_id}/query](#post-api-v1-interop-lakehouse-snapshots-snapshot_id-query) | Query pooled records inside a version-pinned snapshot | ✍️ Audited Write |
| `POST` | [/api/v1/interop/lakehouse/snapshots/{snapshot_id}/verify](#post-api-v1-interop-lakehouse-snapshots-snapshot_id-verify) | Verify cryptographic SHA-256 integrity of sealed snapshot | ✍️ Audited Write |
| `POST` | [/api/v1/interop/notifications/{notification_id}/acknowledge](#post-api-v1-interop-notifications-notification_id-acknowledge) | Acknowledge Notification | ✍️ Audited Write |
| `POST` | [/api/v1/interop/reminders/compute](#post-api-v1-interop-reminders-compute) | Compute Reminders | ✍️ Audited Write |
| `POST` | [/api/v1/interop/sce/execute](#post-api-v1-interop-sce-execute) | Execute statistical program in isolated containerized sandbox | ✍️ Audited Write |
| `POST` | [/api/v1/interop/sce/lock-tokens](#post-api-v1-interop-sce-lock-tokens) | Issue database lock authorization token for unblinded analysis | ✍️ Audited Write |
| `GET` | [/api/v1/interop/sce/lock-tokens](#get-api-v1-interop-sce-lock-tokens) | List active database lock authorization tokens | 👀 Read-Only |
| `POST` | [/api/v1/interop/sce/lock-tokens/{token_id}/revoke](#post-api-v1-interop-sce-lock-tokens-token_id-revoke) | Revoke an active database lock authorization token | ✍️ Audited Write |
| `POST` | [/api/v1/interop/sce/programs](#post-api-v1-interop-sce-programs) | Create or register a statistical analysis program | ✍️ Audited Write |
| `GET` | [/api/v1/interop/sce/programs](#get-api-v1-interop-sce-programs) | List statistical programs for a study | 👀 Read-Only |
| `GET` | [/api/v1/interop/sce/programs/{program_id}](#get-api-v1-interop-sce-programs-program_id) | Retrieve statistical program by ID | 👀 Read-Only |
| `GET` | [/api/v1/interop/sce/runs](#get-api-v1-interop-sce-runs) | List historical execution runs for a study | 👀 Read-Only |
| `GET` | [/api/v1/interop/sce/runs/{run_id}](#get-api-v1-interop-sce-runs-run_id) | Retrieve execution run manifest and details | 👀 Read-Only |
| `GET` | [/api/v1/interop/sce/runs/{run_id}/artifacts/{artifact_id}](#get-api-v1-interop-sce-runs-run_id-artifacts-artifact_id) | Retrieve generated artifact details and preview | 👀 Read-Only |
| `POST` | [/api/v1/interop/sce/runs/{run_id}/verify](#post-api-v1-interop-sce-runs-run_id-verify) | Cryptographically verify execution run manifest seal | ✍️ Audited Write |
| `POST` | [/api/v1/interop/sce/validate-lockfile](#post-api-v1-interop-sce-validate-lockfile) | Validate package lockfile integrity and GxP qualification | ✍️ Audited Write |
| `GET` | [/api/v1/interop/sensors/daily-summaries](#get-api-v1-interop-sensors-daily-summaries) | Query calculated daily summary endpoints | 👀 Read-Only |
| `POST` | [/api/v1/interop/sensors/devices](#post-api-v1-interop-sensors-devices) | Register & map approved medical IoT sensor device | ✍️ Audited Write |
| `GET` | [/api/v1/interop/sensors/devices](#get-api-v1-interop-sensors-devices) | List registered medical IoT devices | 👀 Read-Only |
| `POST` | [/api/v1/interop/sensors/ingest](#post-api-v1-interop-sensors-ingest) | Ingest continuous biometric IoT sensor telemetry | ✍️ Audited Write |
| `GET` | [/api/v1/interop/sensors/metrics/{study_id}](#get-api-v1-interop-sensors-metrics-study_id) | Get connected IoT sensor dashboard metrics | 👀 Read-Only |
| `GET` | [/api/v1/interop/sensors/safety-alerts](#get-api-v1-interop-sensors-safety-alerts) | Query automated safety threshold alerts | 👀 Read-Only |
| `POST` | [/api/v1/interop/sensors/safety-alerts/{alert_id}/acknowledge](#post-api-v1-interop-sensors-safety-alerts-alert_id-acknowledge) | Acknowledge safety alert with electronic signature | ✍️ Audited Write |
| `GET` | [/api/v1/interop/subjects/{subject_id}/compliance](#get-api-v1-interop-subjects-subject_id-compliance) | Get Subject Compliance | 👀 Read-Only |
| `GET` | [/api/v1/interop/subjects/{subject_id}/instruments](#get-api-v1-interop-subjects-subject_id-instruments) | Get Subject Assigned Instruments | 👀 Read-Only |
| `GET` | [/api/v1/interop/subjects/{subject_id}/notifications](#get-api-v1-interop-subjects-subject_id-notifications) | Get Subject Notifications | 👀 Read-Only |
| `GET` | [/health](#get-health) | Health Check | 👀 Read-Only |

---

## Granular Endpoint Specifications

### `POST` `/api/v1/interop/assignments`

**Summary:** Create Subject Assignment  
**Operation ID:** `create_subject_assignment_api_v1_interop_assignments_post`  

**Clinical & GxP Rationale:**  
Assign an eCOA instrument to a subject with due/recurrence window data.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `SubjectAssignmentCreateDTO`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Unique identifier of the clinical study |
| `subject_id` | `string` | ✔ Yes | Unique subject identifier |
| `instrument_id` | `string` | ✔ Yes | ID of the Instrument to assign |
| `start_date` | `string` | ✔ Yes | Start of the due/recurrence window |
| `end_date` | `string` | ✔ Yes | End of the due/recurrence window |
| `recurrence_pattern` | `string` | `null` | No | E.g., DAILY, WEEKLY |
| `due_at` | `string` | `null` | No | Optional specific due date/time |
| `reason_for_change` | `string` | ✔ Yes | 21 CFR Part 11 compliant reason for change |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `SubjectAssignmentResponseDTO` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/assignments', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/assignments',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/assignments' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/assignments/subject/{subject_id}`

**Summary:** Get Subject Assignments  
**Operation ID:** `get_subject_assignments_api_v1_interop_assignments_subject__subject_id__get`  

**Clinical & GxP Rationale:**  
Retrieve all assignments for a given subject.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `subject_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<SubjectAssignmentResponseDTO>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/assignments/subject/{subject_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/assignments/subject/{subject_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/assignments/subject/{subject_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/cdr/export/dataset-json`

**Summary:** Export clinical datasets in official CDISC Dataset-JSON 1.1 / 2.0  
**Operation ID:** `export_dataset_json_api_v1_interop_cdr_export_dataset_json_post`  

**Clinical & GxP Rationale:**  
Serialize and export SDTM and ADaM datasets in CDISC Dataset-JSON 1.1 / 2.0 format.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ExportDatasetJSONRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study Identifier |
| `snapshot_id` | `string` | `null` | No | Optional snapshot ID |
| `datasets` | `Array<string>` | `null` | No | List of domains/datasets to include (e.g. ['DM', 'AE', 'ADSL', 'ADAE']) |
| `dataset_json_version` | `string` | No | Dataset-JSON specification version ('1.0.0', '1.1.0', '2.0.0') |
| `reason_for_change` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DatasetJSONDocument` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/cdr/export/dataset-json', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/cdr/export/dataset-json',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/cdr/export/dataset-json' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/cdr/export/define-xml`

**Summary:** Auto-compile and export CDISC Define-XML v2.1 metadata specification  
**Operation ID:** `export_define_xml_api_v1_interop_cdr_export_define_xml_post`  

**Clinical & GxP Rationale:**  
Auto-compile CDISC Define-XML v2.1 metadata document encapsulating dataset definitions, VLM, WhereClauses, and Origins.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ExportDefineXMLRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study Identifier |
| `study_name` | `string` | `null` | No | Study Name |
| `protocol_name` | `string` | `null` | No | Protocol Name |
| `metadata_version_oid` | `string` | No | Metadata Version OID |
| `standard_name` | `string` | No | Standard Name |
| `standard_version` | `string` | No | Standard Version |
| `domains` | `Array<string>` | No | SDTM domains to document |
| `adam_datasets` | `Array<string>` | No | ADaM datasets to document |
| `include_vlm` | `boolean` | No | Include Value Level Metadata |
| `include_where_clauses` | `boolean` | No | Include WhereClauses |
| `include_origins` | `boolean` | No | Include Origin annotations |
| `reason_for_change` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/cdr/export/define-xml', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/cdr/export/define-xml',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/cdr/export/define-xml' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/cdr/metadata/catalog`

**Summary:** Retrieve CDISC domain and variable metadata catalog  
**Operation ID:** `get_metadata_catalog_api_v1_interop_cdr_metadata_catalog_get`  

**Clinical & GxP Rationale:**  
Retrieve catalog of supported SDTM domains, ADaM datasets, variable definitions, and VLM.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DomainCatalogResponse` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/cdr/metadata/catalog', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/cdr/metadata/catalog',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/cdr/metadata/catalog' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/cdr/pipeline/adam`

**Summary:** Execute CDISC ADaM 4-dataset derivation engine  
**Operation ID:** `execute_adam_pipeline_api_v1_interop_cdr_pipeline_adam_post`  

**Clinical & GxP Rationale:**  
Execute statistical derivation rules generating ADSL, ADAE, ADTTE, BDS datasets.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `RunADaMPipelineRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study Identifier |
| `snapshot_id` | `string` | `null` | No | Optional snapshot ID |
| `datasets` | `Array<string>` | `null` | No | Optional list of specific ADaM datasets (ADSL, ADAE, ADTTE, BDS) |
| `reason_for_change` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ADaMPipelineResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/cdr/pipeline/adam', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/cdr/pipeline/adam',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/cdr/pipeline/adam' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/cdr/pipeline/execute`

**Summary:** Execute end-to-end continuous CDISC SDTM/ADaM pipeline and compiler  
**Operation ID:** `execute_continuous_pipeline_api_v1_interop_cdr_pipeline_execute_post`  

**Clinical & GxP Rationale:**  
Execute automated continuous transformation mapping raw clinical observations to 10 SDTM domains, 4 ADaM datasets, Dataset-JSON, and Define-XML.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `RunContinuousPipelineRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Unique Study Identifier (e.g. STUDY-001) |
| `snapshot_id` | `string` | `null` | No | Optional version-pinned snapshot ID to execute over |
| `dataset_json_version` | `string` | No | Target Dataset-JSON version ('1.0.0', '1.1.0', '2.0.0') |
| `reason_for_change` | `string` | No | 21 CFR Part 11 required reason for change |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `PipelineExecutionResult` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/cdr/pipeline/execute', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/cdr/pipeline/execute',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/cdr/pipeline/execute' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/cdr/pipeline/results/{execution_id}`

**Summary:** Retrieve execution manifest for a pipeline run  
**Operation ID:** `get_pipeline_execution_result_api_v1_interop_cdr_pipeline_results__execution_id__get`  

**Clinical & GxP Rationale:**  
Retrieve details and metadata summary for a completed pipeline run.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `execution_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `PipelineExecutionResult` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/cdr/pipeline/results/{execution_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/cdr/pipeline/results/{execution_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/cdr/pipeline/results/{execution_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/cdr/pipeline/sdtm`

**Summary:** Execute CDISC SDTM 10-domain transformation engine  
**Operation ID:** `execute_sdtm_pipeline_api_v1_interop_cdr_pipeline_sdtm_post`  

**Clinical & GxP Rationale:**  
Transform canonical observations into 10 standard CDISC SDTM domains.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `RunSDTMPipelineRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study Identifier |
| `snapshot_id` | `string` | `null` | No | Optional snapshot ID |
| `domains` | `Array<string>` | `null` | No | Optional list of specific SDTM domains (DM, AE, CM, VS, LB, EX, DS, TU, TR, RS) |
| `reason_for_change` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SDTMPipelineResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/cdr/pipeline/sdtm', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/cdr/pipeline/sdtm',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/cdr/pipeline/sdtm' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/cdr/validate/dataset-json`

**Summary:** Validate a Dataset-JSON 1.1 / 2.0 payload  
**Operation ID:** `validate_dataset_json_endpoint_api_v1_interop_cdr_validate_dataset_json_post`  

**Clinical & GxP Rationale:**  
Perform strict structural and schema conformance validation for CDISC Dataset-JSON.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ValidateDatasetJSONRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `payload` | `object` | `string` | ✔ Yes | Dataset-JSON document or dictionary |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ValidateDatasetJSONResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/cdr/validate/dataset-json', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/cdr/validate/dataset-json',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/cdr/validate/dataset-json' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/cdr/validate/define-xml`

**Summary:** Validate a CDISC Define-XML v2.1 document  
**Operation ID:** `validate_define_xml_endpoint_api_v1_interop_cdr_validate_define_xml_post`  

**Clinical & GxP Rationale:**  
Validate CDISC Define-XML v2.1 document syntax, ODM root, and element presence.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ValidateDefineXMLRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `xml_content` | `string` | ✔ Yes | Define-XML document string |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DefineXMLValidationResult` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/cdr/validate/define-xml', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/cdr/validate/define-xml',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/cdr/validate/define-xml' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/dct/dtp/shipments`

**Summary:** Create Direct-to-Patient (DTP) courier dispatch order  
**Operation ID:** `create_dtp_shipment_api_v1_interop_dct_dtp_shipments_post`  

**Clinical & GxP Rationale:**  
Create a new Direct-to-Patient courier drug shipment with temperature-controlled profile.

@req:PRD-DCT-003
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DTPShipmentCreateRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `site_id` | `string` | ✔ Yes | — |
| `subject_pseudonym_id` | `string` | ✔ Yes | — |
| `destination_address_pseudonym` | `string` | ✔ Yes | — |
| `courier_name` | `string` | No | — |
| `temperature_zone` | `TemperatureZone` | No | — |
| `kit_serials` | `Array<string>` | ✔ Yes | — |
| `tamper_seal_id` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `DTPShipmentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/dct/dtp/shipments', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/dct/dtp/shipments',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/dct/dtp/shipments' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/dct/dtp/shipments`

**Summary:** List Direct-to-Patient drug shipments  
**Operation ID:** `list_dtp_shipments_api_v1_interop_dct_dtp_shipments_get`  

**Clinical & GxP Rationale:**  
Query DTP shipments with cold-chain indicators and tracking states.

@req:PRD-DCT-003
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |
| `site_id` | `query` | `string` | `null` | No | — |
| `subject_pseudonym_id` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DTPShipmentListResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/dct/dtp/shipments', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/dct/dtp/shipments',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/dct/dtp/shipments' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/dct/dtp/shipments/{tracking_number}`

**Summary:** Retrieve single DTP shipment details by tracking number  
**Operation ID:** `get_dtp_shipment_api_v1_interop_dct_dtp_shipments__tracking_number__get`  

**Clinical & GxP Rationale:**  
Retrieve details, temperature telemetry, and Chain of Custody for a DTP shipment.

@req:PRD-DCT-003
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `tracking_number` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DTPShipmentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/dct/dtp/shipments/{tracking_number}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/dct/dtp/shipments/{tracking_number}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/dct/dtp/shipments/{tracking_number}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/dct/dtp/shipments/{tracking_number}/delivery`

**Summary:** Confirm final delivery with recipient signature  
**Operation ID:** `confirm_dtp_delivery_api_v1_interop_dct_dtp_shipments__tracking_number__delivery_post`  

**Clinical & GxP Rationale:**  
Confirm delivery at subject residence with recipient signature and greenlight checks.

@req:PRD-DCT-003
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `tracking_number` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `DeliveryConfirmRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `recipient_name` | `string` | ✔ Yes | — |
| `signature_manifestation` | `string` | ✔ Yes | — |
| `tamper_seal_verified` | `boolean` | No | — |
| `logger_greenlight` | `boolean` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DTPShipmentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/dct/dtp/shipments/{tracking_number}/delivery', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/dct/dtp/shipments/{tracking_number}/delivery',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/dct/dtp/shipments/{tracking_number}/delivery' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/dct/dtp/shipments/{tracking_number}/handoff`

**Summary:** Record Chain of Custody (CoC) handoff event  
**Operation ID:** `record_dtp_handoff_api_v1_interop_dct_dtp_shipments__tracking_number__handoff_post`  

**Clinical & GxP Rationale:**  
Record signed Chain of Custody handoff across depot, courier hubs, and final mile.

@req:PRD-DCT-003
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `tracking_number` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CustodyHandoffRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `stage` | `string` | ✔ Yes | e.g. DEPOT_PICKUP, TRANSIT_HUB, FINAL_MILE |
| `released_by` | `string` | ✔ Yes | — |
| `received_by` | `string` | ✔ Yes | — |
| `tamper_seal_intact` | `boolean` | No | — |
| `current_temperature_celsius` | `number` | ✔ Yes | Visual thermometer reading in °C |
| `notes` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DTPShipmentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/dct/dtp/shipments/{tracking_number}/handoff', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/dct/dtp/shipments/{tracking_number}/handoff',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/dct/dtp/shipments/{tracking_number}/handoff' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/dct/dtp/shipments/{tracking_number}/temperature`

**Summary:** Ingest cold-chain temperature telemetry points & calculate MKT  
**Operation ID:** `ingest_dtp_temperature_api_v1_interop_dct_dtp_shipments__tracking_number__temperature_post`  

**Clinical & GxP Rationale:**  
Ingest cold chain logger data points, compute Arrhenius MKT, and flag excursions.

@req:PRD-DCT-003
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `tracking_number` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `TemperatureIngestRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `readings` | `Array<TemperatureReadingDTO>` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DTPShipmentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/dct/dtp/shipments/{tracking_number}/temperature', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/dct/dtp/shipments/{tracking_number}/temperature',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/dct/dtp/shipments/{tracking_number}/temperature' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/dct/econsent/pediatric-assent`

**Summary:** Submit tiered pediatric assent & LAR dual-signature manifest  
**Operation ID:** `submit_pediatric_assent_api_v1_interop_dct_econsent_pediatric_assent_post`  

**Clinical & GxP Rationale:**  
Submit age-appropriate pediatric assent and parent/LAR electronic signature bundle.

@req:PRD-DCT-006
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `PediatricAssentSubmitRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `subject_id` | `string` | ✔ Yes | — |
| `minor_age` | `integer` | ✔ Yes | Age of minor at consent execution |
| `answers` | `Array<ComprehensionAnswerDTO>` | No | Quiz answers |
| `assent_signature` | `string` | `null` | No | Minor assent signature string or drawing token |
| `lar_name` | `string` | `null` | No | Name of Parent or Legally Authorized Representative |
| `lar_relationship` | `LARRelationship` | `null` | No | Legal relationship to minor |
| `lar_legal_basis` | `string` | `null` | No | Legal authority basis (e.g. Birth Certificate, Court Order) |
| `lar_signature` | `string` | `null` | No | Electronic signature of LAR |
| `investigator_name` | `string` | `null` | No | Investigator counter-signatory name |
| `investigator_signature` | `string` | `null` | No | Investigator electronic signature |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `PediatricAssentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/dct/econsent/pediatric-assent', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/dct/econsent/pediatric-assent',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/dct/econsent/pediatric-assent' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/dct/econsent/pediatric-assent`

**Summary:** List executed pediatric assents  
**Operation ID:** `list_pediatric_assents_api_v1_interop_dct_econsent_pediatric_assent_get`  

**Clinical & GxP Rationale:**  
Query executed pediatric assent and LAR consent records.

@req:PRD-DCT-006
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |
| `subject_id` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `PediatricAssentListResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/dct/econsent/pediatric-assent', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/dct/econsent/pediatric-assent',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/dct/econsent/pediatric-assent' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/dct/econsent/pediatric-assent/{assent_id}/verify`

**Summary:** Verify cryptographic bundle seal of pediatric assent  
**Operation ID:** `verify_pediatric_assent_api_v1_interop_dct_econsent_pediatric_assent__assent_id__verify_get`  

**Clinical & GxP Rationale:**  
Validate cryptographic SHA-256 seal integrity for pediatric assent bundle.

@req:PRD-DCT-006
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `assent_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `VerifyAssentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/dct/econsent/pediatric-assent/{assent_id}/verify', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/dct/econsent/pediatric-assent/{assent_id}/verify',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/dct/econsent/pediatric-assent/{assent_id}/verify' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/dct/home-health/visits`

**Summary:** Schedule decentralized in-home nursing visit  
**Operation ID:** `schedule_in_home_visit_api_v1_interop_dct_home_health_visits_post`  

**Clinical & GxP Rationale:**  
Schedule visiting healthcare nurse appointment with protocol procedure tasks.

@req:PRD-DCT-004
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `InHomeVisitCreateRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `site_id` | `string` | ✔ Yes | — |
| `subject_id` | `string` | ✔ Yes | — |
| `visit_number` | `string` | ✔ Yes | — |
| `visit_name` | `string` | ✔ Yes | — |
| `nurse_id` | `string` | ✔ Yes | — |
| `nurse_name` | `string` | ✔ Yes | — |
| `nurse_license_number` | `string` | ✔ Yes | — |
| `scheduled_start_utc` | `string` | ✔ Yes | — |
| `procedures` | `Array<ProtocolProcedureDTO>` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `InHomeVisitResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/dct/home-health/visits', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/dct/home-health/visits',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/dct/home-health/visits' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/dct/home-health/visits`

**Summary:** List in-home visiting clinician appointments  
**Operation ID:** `list_in_home_visits_api_v1_interop_dct_home_health_visits_get`  

**Clinical & GxP Rationale:**  
Query in-home nursing visits by study, site, nurse, or subject.

@req:PRD-DCT-004
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |
| `site_id` | `query` | `string` | `null` | No | — |
| `nurse_id` | `query` | `string` | `null` | No | — |
| `subject_id` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `InHomeVisitListResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/dct/home-health/visits', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/dct/home-health/visits',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/dct/home-health/visits' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/dct/home-health/visits/{visit_id}`

**Summary:** Retrieve single in-home visit details  
**Operation ID:** `get_in_home_visit_api_v1_interop_dct_home_health_visits__visit_id__get`  

**Clinical & GxP Rationale:**  
Retrieve in-home nursing visit details, procedure progress, and mobile eCRF data.

@req:PRD-DCT-004
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `visit_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `InHomeVisitResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/dct/home-health/visits/{visit_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/dct/home-health/visits/{visit_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/dct/home-health/visits/{visit_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/dct/home-health/visits/{visit_id}/status`

**Summary:** Update visiting nurse visit progress and completed procedures  
**Operation ID:** `update_in_home_visit_status_api_v1_interop_dct_home_health_visits__visit_id__status_post`  

**Clinical & GxP Rationale:**  
Update in-home visit status (EN_ROUTE, ARRIVED_ON_SITE, PROCEDURES_IN_PROGRESS, etc.).

@req:PRD-DCT-004
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `visit_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `InHomeVisitStatusUpdateRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `status` | `InHomeVisitStatus` | ✔ Yes | — |
| `procedures` | `Array<ProtocolProcedureDTO>` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `InHomeVisitResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/dct/home-health/visits/{visit_id}/status', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/dct/home-health/visits/{visit_id}/status',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/dct/home-health/visits/{visit_id}/status' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/dct/home-health/visits/{visit_id}/sync-ecrf`

**Summary:** Reconcile offline-cached mobile eCRF clinical data entry  
**Operation ID:** `reconcile_mobile_ecrf_sync_api_v1_interop_dct_home_health_visits__visit_id__sync_ecrf_post`  

**Clinical & GxP Rationale:**  
Reconcile mobile eCRF clinical data entered offline by visiting nurse.

@req:PRD-DCT-004
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `visit_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `MobileECRFSyncRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `client_sync_id` | `string` | ✔ Yes | — |
| `offline_collected_at_utc` | `string` | ✔ Yes | — |
| `data` | `object` | ✔ Yes | Key-value mapping of eCRF item answers |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `InHomeVisitResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/dct/home-health/visits/{visit_id}/sync-ecrf', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/dct/home-health/visits/{visit_id}/sync-ecrf',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/dct/home-health/visits/{visit_id}/sync-ecrf' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/dct/metrics/{study_id}`

**Summary:** Retrieve aggregated DCT operational metrics  
**Operation ID:** `get_dct_metrics_api_v1_interop_dct_metrics__study_id__get`  

**Clinical & GxP Rationale:**  
Retrieve real-time DCT dashboard KPI metrics for study.

@req:PRD-DCT-003
@req:PRD-DCT-004
@req:PRD-DCT-005
@req:PRD-DCT-006
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DCTMetricsResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/dct/metrics/{study_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/dct/metrics/{study_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/dct/metrics/{study_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/dct/telehealth/sessions`

**Summary:** Schedule WebRTC telehealth video visit  
**Operation ID:** `schedule_telehealth_session_api_v1_interop_dct_telehealth_sessions_post`  

**Clinical & GxP Rationale:**  
Schedule a new WebRTC telehealth video consultation session.

@req:PRD-DCT-005
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `TelehealthSessionCreateRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study protocol identifier |
| `subject_id` | `string` | ✔ Yes | Study subject identifier |
| `title` | `string` | No | Descriptive title of consultation |
| `scheduled_start_utc` | `string` | ✔ Yes | Scheduled visit start timestamp in UTC |
| `site_id` | `string` | `null` | No | Clinical trial site identifier |
| `scheduled_end_utc` | `string` | `null` | No | Optional scheduled end timestamp |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `TelehealthSessionResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/dct/telehealth/sessions', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/dct/telehealth/sessions',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/dct/telehealth/sessions' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/dct/telehealth/sessions`

**Summary:** List telehealth video consultation sessions  
**Operation ID:** `list_telehealth_sessions_api_v1_interop_dct_telehealth_sessions_get`  

**Clinical & GxP Rationale:**  
Query scheduled and active telehealth consultation sessions.

@req:PRD-DCT-005
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |
| `subject_id` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `TelehealthSessionListResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/dct/telehealth/sessions', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/dct/telehealth/sessions',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/dct/telehealth/sessions' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/dct/telehealth/sessions/{session_id}`

**Summary:** Retrieve single telehealth session details  
**Operation ID:** `get_telehealth_session_api_v1_interop_dct_telehealth_sessions__session_id__get`  

**Clinical & GxP Rationale:**  
Retrieve details of a single telehealth video session by ID or room ID.

@req:PRD-DCT-005
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `session_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `TelehealthSessionResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/dct/telehealth/sessions/{session_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/dct/telehealth/sessions/{session_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/dct/telehealth/sessions/{session_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/dct/telehealth/sessions/{session_id}/archive-recording`

**Summary:** Archive GxP recording with SHA-256 digital seal (21 CFR Part 11)  
**Operation ID:** `archive_telehealth_recording_api_v1_interop_dct_telehealth_sessions__session_id__archive_recording_post`  

**Clinical & GxP Rationale:**  
Archive telehealth video consultation recording with cryptographic SHA-256 digital seal.

@req:PRD-DCT-005
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `session_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ArchiveRecordingRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `media_payload_or_hash` | `string` | ✔ Yes | Raw recording media binary string or computed SHA-256 hash |
| `duration_seconds` | `integer` | ✔ Yes | Total recording duration in seconds |
| `resolution` | `string` | No | Video recording resolution (e.g. 720p, 1080p, 4K) |
| `recorded_by` | `string` | ✔ Yes | User ID of recording operator |
| `participant_manifest` | `Array<object>` | No | List of participant identities |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ArchiveRecordingResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/dct/telehealth/sessions/{session_id}/archive-recording', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/dct/telehealth/sessions/{session_id}/archive-recording',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/dct/telehealth/sessions/{session_id}/archive-recording' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/dct/telehealth/sessions/{session_id}/co-view`

**Summary:** Synchronize eConsent interactive co-viewing state  
**Operation ID:** `sync_co_view_state_api_v1_interop_dct_telehealth_sessions__session_id__co_view_post`  

**Clinical & GxP Rationale:**  
Synchronize eConsent page position, active highlights, and scroll percentage.

@req:PRD-DCT-005
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `session_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CoViewStateRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `document_id` | `string` | ✔ Yes | Informed consent document identifier |
| `page_index` | `integer` | No | Current active page index |
| `clause_id` | `string` | `null` | No | Active highlighted clause ID |
| `presenter_id` | `string` | ✔ Yes | Presenter user ID |
| `presenter_role` | `ParticipantRole` | No | Role of presenter |
| `scroll_percent` | `number` | No | Vertical scroll percentage 0.0 to 100.0 |
| `active_highlights` | `Array<object>` | No | Array of highlight coordinates |
| `lock_presenter_control` | `boolean` | No | Whether presenter has exclusive navigation lock |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `TelehealthSessionResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/dct/telehealth/sessions/{session_id}/co-view', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/dct/telehealth/sessions/{session_id}/co-view',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/dct/telehealth/sessions/{session_id}/co-view' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/dct/telehealth/sessions/{session_id}/signaling`

**Summary:** Exchange WebRTC session signaling message  
**Operation ID:** `update_session_signaling_api_v1_interop_dct_telehealth_sessions__session_id__signaling_post`  

**Clinical & GxP Rationale:**  
Append WebRTC session signaling envelope (SDP offer/answer, ICE candidates).

@req:PRD-DCT-005
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `session_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `WebRTCSignalingRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `sender_id` | `string` | ✔ Yes | Participant sender identifier |
| `sender_role` | `ParticipantRole` | ✔ Yes | Role of the sender |
| `message_type` | `SignalingMessageType` | ✔ Yes | Type of signaling message |
| `sdp` | `string` | `null` | No | Session Description Protocol (SDP) string |
| `candidate` | `string` | `null` | No | ICE candidate string |
| `sdp_mid` | `string` | `null` | No | SDP media stream identifier |
| `sdp_m_line_index` | `integer` | `null` | No | SDP m-line index |
| `payload` | `object` | No | Arbitrary payload for chat/annotations |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `TelehealthSessionResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/dct/telehealth/sessions/{session_id}/signaling', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/dct/telehealth/sessions/{session_id}/signaling',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/dct/telehealth/sessions/{session_id}/signaling' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/dct/telehealth/sessions/{session_id}/token`

**Summary:** Generate HMAC-SHA256 signed room authorization token  
**Operation ID:** `generate_room_token_api_v1_interop_dct_telehealth_sessions__session_id__token_post`  

**Clinical & GxP Rationale:**  
Issue a role-scoped, cryptographic room authorization token for WebRTC video entry.

@req:PRD-DCT-005
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `session_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `RoomTokenRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `participant_id` | `string` | ✔ Yes | Unique participant identifier |
| `participant_role` | `ParticipantRole` | ✔ Yes | Clinical role of participant |
| `display_name` | `string` | ✔ Yes | Friendly participant display name |
| `duration_minutes` | `integer` | No | Token validity window in minutes |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `RoomTokenResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/dct/telehealth/sessions/{session_id}/token', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/dct/telehealth/sessions/{session_id}/token',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/dct/telehealth/sessions/{session_id}/token' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/devices/export-package`

**Summary:** Generate Export Package Endpoint  
**Operation ID:** `generate_export_package_endpoint_api_v1_interop_devices_export_package_post`  

**Clinical & GxP Rationale:**  
Compiles a complete regulatory investigation package (SDTM DD, DO, and MIR forms).

Requirements: PRD-SYS-126, PRD-DEV-004, PRD-ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DeviceInvestigationExportPackageRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `devices` | `Array<object>` | No | — |
| `deficiencies` | `Array<object>` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DeviceInvestigationExportPackageResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/devices/export-package', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/devices/export-package',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/devices/export-package' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/devices/mir-report`

**Summary:** Generate Mdr Incident Report Endpoint  
**Operation ID:** `generate_mdr_incident_report_endpoint_api_v1_interop_devices_mir_report_post`  

**Clinical & GxP Rationale:**  
Compiles an EU MDR 2017/745 / EU IVDR 2017/746 Manufacturer Incident Report (MIR v7.2).

Requirements: PRD-SYS-126, PRD-DEV-004, EU MDR 2017/745, EU IVDR 2017/746, PRD-ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `MDRIncidentReportRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `site_id` | `string` | ✔ Yes | — |
| `deficiency_id` | `string` | ✔ Yes | — |
| `brand_name` | `string` | ✔ Yes | — |
| `udi_di` | `string` | ✔ Yes | — |
| `lot_number` | `string` | ✔ Yes | — |
| `serial_number` | `string` | `null` | No | — |
| `incident_date` | `string` | ✔ Yes | — |
| `awareness_date` | `string` | `null` | No | — |
| `deficiency_type` | `string` | No | — |
| `deficiency_description` | `string` | ✔ Yes | — |
| `safety_classification` | `string` | No | — |
| `causality` | `string` | No | — |
| `patient_involved` | `boolean` | No | — |
| `health_impact` | `string` | No | — |
| `root_cause_analysis` | `string` | `null` | No | — |
| `action_taken` | `string` | No | — |
| `capa_required` | `boolean` | No | — |
| `fsca_required` | `boolean` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `MDRIncidentReportResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/devices/mir-report', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/devices/mir-report',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/devices/mir-report' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/devices/sdtm/dd`

**Summary:** Generate Sdtm Dd Endpoint  
**Operation ID:** `generate_sdtm_dd_endpoint_api_v1_interop_devices_sdtm_dd_post`  

**Clinical & GxP Rationale:**  
Compiles CDISC SDTM DD (Device Deficiencies) dataset from reported device malfunctions.

Requirements: PRD-DEV-004, ISO 14155:2020, PRD-ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SDTMDDGenerateRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `deficiencies` | `Array<object>` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SDTMDDResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/devices/sdtm/dd', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/devices/sdtm/dd',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/devices/sdtm/dd' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/devices/sdtm/do`

**Summary:** Generate Sdtm Do Endpoint  
**Operation ID:** `generate_sdtm_do_endpoint_api_v1_interop_devices_sdtm_do_post`  

**Clinical & GxP Rationale:**  
Compiles CDISC SDTM DO (Device Tracking and Operations) dataset from serialized inventory.

Requirements: PRD-DEV-004, ISO 14155:2020, PRD-ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SDTMDOGenerateRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `devices` | `Array<object>` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SDTMDOResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/devices/sdtm/do', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/devices/sdtm/do',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/devices/sdtm/do' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/epro/quarantine`

**Summary:** List Quarantined Submissions  
**Operation ID:** `list_quarantined_submissions_api_v1_interop_epro_quarantine_get`  

**Clinical & GxP Rationale:**  
Retrieve all quarantined ePRO submissions for clinical trial managers.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `status` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<QuarantinedSubmissionResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/epro/quarantine', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/epro/quarantine',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/epro/quarantine' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/epro/quarantine/{id}`

**Summary:** Get Quarantined Submission  
**Operation ID:** `get_quarantined_submission_api_v1_interop_epro_quarantine__id__get`  

**Clinical & GxP Rationale:**  
Retrieve a single quarantined ePRO submission by ID.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `QuarantinedSubmissionResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/epro/quarantine/{id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/epro/quarantine/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/epro/quarantine/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/epro/quarantine/{id}/edit`

**Summary:** Edit Quarantined Submission  
**Operation ID:** `edit_quarantined_submission_api_v1_interop_epro_quarantine__id__edit_post`  

**Clinical & GxP Rationale:**  
Edit a quarantined ePRO submission payload.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `EditQuarantinedSubmissionRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `answers` | `object` | ✔ Yes | The edited ePRO/eCOA answers |
| `password` | `string` | ✔ Yes | The password for 21 CFR Part 11 compliant digital signature verification |
| `change_reason` | `string` | ✔ Yes | Standard 21 CFR Part 11 compliant reason for the edit |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `QuarantinedSubmissionResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/epro/quarantine/{id}/edit', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/epro/quarantine/{id}/edit',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/epro/quarantine/{id}/edit' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/epro/quarantine/{id}/replay`

**Summary:** Replay Quarantined Submission  
**Operation ID:** `replay_quarantined_submission_api_v1_interop_epro_quarantine__id__replay_post`  

**Clinical & GxP Rationale:**  
Replay a corrected quarantined ePRO submission back into the active database.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ReplayQuarantinedSubmissionRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `password` | `string` | ✔ Yes | The password for 21 CFR Part 11 compliant digital signature verification |
| `change_reason` | `string` | ✔ Yes | Standard 21 CFR Part 11 compliant reason for the replay |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/epro/quarantine/{id}/replay', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/epro/quarantine/{id}/replay',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/epro/quarantine/{id}/replay' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/epro/submit`

**Summary:** Epro Submit  
**Operation ID:** `epro_submit_api_v1_interop_epro_submit_post`  

**Clinical & GxP Rationale:**  
Submit a single participant diary/survey entry.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `EPROSubmissionPayload`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `subject_id` | `string` | ✔ Yes | Pseudonymized identifier of the subject |
| `diary_id` | `string` | ✔ Yes | Unique identifier for the diary or survey |
| `version_index` | `integer` | No | The version index of the instrument used for compilation |
| `device_timestamp` | `string` | ✔ Yes | ISO 8601 timestamp when the entry was created on device |
| `answers` | `object` | ✔ Yes | The questionnaire response key-values |
| `offline_sync_markers` | `OfflineSyncMarkers` | ✔ Yes | The offline sync queue conflict tracking parameters |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/epro/submit', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/epro/submit',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/epro/submit' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/epro/sync`

**Summary:** Epro Sync  
**Operation ID:** `epro_sync_api_v1_interop_epro_sync_post`  

**Clinical & GxP Rationale:**  
Bulk sync endpoint for offline queues.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `BulkSyncPayload`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `submissions` | `Array<EPROSubmissionPayload>` | ✔ Yes | A list of queued ePRO submissions |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/epro/sync', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/epro/sync',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/epro/sync' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/esource/candidates`

**Summary:** List Candidates  
**Operation ID:** `list_candidates_api_v1_interop_esource_candidates_get`  

**Clinical & GxP Rationale:**  
Retrieve candidate reconciliation items with filtering for the split-screen drawer.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |
| `subject_id` | `query` | `string` | `null` | No | — |
| `site_id` | `query` | `string` | `null` | No | — |
| `domain` | `query` | `string` | `null` | No | — |
| `status` | `query` | `string` | `null` | No | — |
| `skip` | `query` | `integer` | No | — |
| `limit` | `query` | `integer` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `EhrCandidateListResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/esource/candidates', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/esource/candidates',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/esource/candidates' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/esource/candidates/{candidate_id}`

**Summary:** Get Candidate  
**Operation ID:** `get_candidate_api_v1_interop_esource_candidates__candidate_id__get`  

**Clinical & GxP Rationale:**  
Get details of a specific candidate reconciliation item.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `candidate_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `EhrCandidateItemDTO` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/esource/candidates/{candidate_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/esource/candidates/{candidate_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/esource/candidates/{candidate_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/esource/endpoints`

**Summary:** Register Ehr Endpoint  
**Operation ID:** `register_ehr_endpoint_api_v1_interop_esource_endpoints_post`  

**Clinical & GxP Rationale:**  
Register a new hospital EHR FHIR endpoint with SMART Backend Services credentials.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `EhrEndpointCreateRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `name` | `string` | ✔ Yes | Hospital or health system network name |
| `system_type` | `EhrSystemType` | No | EHR system vendor |
| `fhir_base_url` | `string` | ✔ Yes | FHIR R4 API base URL |
| `token_url` | `string` | ✔ Yes | SMART Backend Services OAuth2 token endpoint |
| `client_id` | `string` | ✔ Yes | OAuth2 client_id registered with hospital EHR |
| `public_key_jwks_url` | `string` | `null` | No | Optional public JWKS URL |
| `scopes` | `Array<string>` | No | FHIR scopes requested |
| `polling_interval_sec` | `integer` | No | Automated polling interval in seconds |
| `is_active` | `boolean` | No | Whether endpoint polling is active |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `EhrEndpointResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/esource/endpoints', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/esource/endpoints',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/esource/endpoints' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/esource/endpoints`

**Summary:** List Ehr Endpoints  
**Operation ID:** `list_ehr_endpoints_api_v1_interop_esource_endpoints_get`  

**Clinical & GxP Rationale:**  
List configured hospital EHR endpoints.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `active_only` | `query` | `boolean` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<EhrEndpointResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/esource/endpoints', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/esource/endpoints',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/esource/endpoints' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/esource/ingest`

**Summary:** Ingest Fhir Bundle  
**Operation ID:** `ingest_fhir_bundle_api_v1_interop_esource_ingest_post`  

**Clinical & GxP Rationale:**  
Ingest a standard FHIR Bundle, normalize to CDASH domain models, and populate candidate reconciliation queue.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `EhrIngestBundleRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Clinical study protocol identifier |
| `site_id` | `string` | No | Clinical trial site identifier |
| `ehr_system_id` | `string` | No | Source EHR system name |
| `bundle` | `object` | ✔ Yes | Raw HL7 FHIR Bundle JSON payload |
| `current_ecrf_context` | `object` | `null` | No | Current eCRF field values for side-by-side comparison |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `EhrIngestBundleResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/esource/ingest', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/esource/ingest',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/esource/ingest' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/esource/poll`

**Summary:** Poll Hospital Ehr  
**Operation ID:** `poll_hospital_ehr_api_v1_interop_esource_poll_post`  

**Clinical & GxP Rationale:**  
Trigger automated SMART on FHIR polling against a registered hospital EHR endpoint.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `EhrPollRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `endpoint_id` | `string` | ✔ Yes | ID of registered EHR endpoint |
| `study_id` | `string` | ✔ Yes | Clinical study protocol identifier |
| `site_id` | `string` | No | Clinical trial site identifier |
| `subject_id` | `string` | ✔ Yes | Subject identifier/MRN to poll from hospital EHR |
| `private_key_pem` | `string` | ✔ Yes | RSA private key for SMART Backend Services auth |
| `current_ecrf_context` | `object` | `null` | No | Optional current eCRF context dictionary for live comparison |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `EhrPollResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/esource/poll', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/esource/poll',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/esource/poll' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/esource/provenance/{candidate_id}`

**Summary:** Get Provenance Audit  
**Operation ID:** `get_provenance_audit_api_v1_interop_esource_provenance__candidate_id__get`  

**Clinical & GxP Rationale:**  
Retrieve complete immutable FHIR source provenance and Part 11 audit ledger for a record.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `candidate_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `EhrProvenanceResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/esource/provenance/{candidate_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/esource/provenance/{candidate_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/esource/provenance/{candidate_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/esource/reconcile/accept`

**Summary:** Accept Candidate  
**Operation ID:** `accept_candidate_api_v1_interop_esource_reconcile_accept_post`  

**Clinical & GxP Rationale:**  
1-Click 'Accept & Commit' candidate EHR data into eCRF with Part 11 audit attribution.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `EhrReconcileAcceptRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `candidate_id` | `string` | ✔ Yes | UUID of candidate record to accept |
| `signature_token` | `string` | `null` | No | Optional Part 11 electronic signature token |
| `commit_reason` | `string` | `null` | No | Optional commit justification |
| `change_reason` | `string` | No | Part 11 audit change reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `EhrReconcileActionResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/esource/reconcile/accept', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/esource/reconcile/accept',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/esource/reconcile/accept' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/esource/reconcile/query`

**Summary:** Raise Clinical Query  
**Operation ID:** `raise_clinical_query_api_v1_interop_esource_reconcile_query_post`  

**Clinical & GxP Rationale:**  
1-Click 'Raise Clinical Query' to open a formal GxP discrepancy query for an EHR-to-eCRF mismatch.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `EhrReconcileQueryRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `candidate_id` | `string` | ✔ Yes | UUID of candidate record with discrepancy |
| `query_explanation` | `string` | ✔ Yes | Clinical query discrepancy explanation |
| `discrepancy_type` | `string` | No | Type of discrepancy |
| `change_reason` | `string` | No | Part 11 audit change reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `EhrReconcileActionResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/esource/reconcile/query', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/esource/reconcile/query',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/esource/reconcile/query' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/esource/reconcile/reject`

**Summary:** Reject Candidate  
**Operation ID:** `reject_candidate_api_v1_interop_esource_reconcile_reject_post`  

**Clinical & GxP Rationale:**  
1-Click 'Reject with Reason' to discard candidate EHR data with mandatory justification.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `EhrReconcileRejectRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `candidate_id` | `string` | ✔ Yes | UUID of candidate record to reject |
| `rejection_reason` | `string` | ✔ Yes | Mandatory clinical rationale for rejection |
| `change_reason` | `string` | No | Part 11 audit change reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `EhrReconcileActionResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/esource/reconcile/reject', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/esource/reconcile/reject',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/esource/reconcile/reject' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/esource/smart/assertion`

**Summary:** Generate Client Assertion  
**Operation ID:** `generate_client_assertion_api_v1_interop_esource_smart_assertion_post`  

**Clinical & GxP Rationale:**  
Generate an asymmetric signed client assertion JWT for SMART on FHIR Backend Services OAuth2.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SmartClientAssertionRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `client_id` | `string` | ✔ Yes | OAuth2 client identifier |
| `token_endpoint` | `string` | ✔ Yes | Target EHR OAuth2 token URL (aud claim) |
| `private_key_pem` | `string` | ✔ Yes | RSA private key in PEM format |
| `algorithm` | `string` | No | Asymmetric signing algorithm (RS384 or RS256) |
| `key_id` | `string` | `null` | No | Optional kid header |
| `expiration_seconds` | `integer` | No | Assertion validity lifetime in seconds |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SmartClientAssertionResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/esource/smart/assertion', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/esource/smart/assertion',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/esource/smart/assertion' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/esource/smart/verify`

**Summary:** Verify Client Assertion  
**Operation ID:** `verify_client_assertion_api_v1_interop_esource_smart_verify_post`  

**Clinical & GxP Rationale:**  
Verify an asymmetric client assertion JWT per SMART Backend Services rules.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SmartAssertionVerifyRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `assertion_jwt` | `string` | ✔ Yes | Raw client assertion JWT |
| `public_key_pem` | `string` | ✔ Yes | RSA public key in PEM format |
| `expected_audience` | `string` | `null` | No | Expected token endpoint URL |
| `expected_client_id` | `string` | `null` | No | Expected client_id claim |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SmartAssertionVerifyResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/esource/smart/verify', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/esource/smart/verify',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/esource/smart/verify' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/fhir/concept-maps`

**Summary:** List Concept Maps  
**Operation ID:** `list_concept_maps_api_v1_interop_fhir_concept_maps_get`  

**Clinical & GxP Rationale:**  
Retrieve the catalog of supported FHIR-to-CDISC ConceptMaps.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ConceptMapListResponse` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/fhir/concept-maps', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/fhir/concept-maps',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/fhir/concept-maps' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/fhir/pre-screen`

**Summary:** Fhir Pre Screen  
**Operation ID:** `fhir_pre_screen_api_v1_interop_fhir_pre_screen_post`  

**Clinical & GxP Rationale:**  
Advisory FHIR eligibility pre-screening endpoint.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `FHIRPreScreenRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Unique identifier of the clinical study |
| `bundle` | `object` | ✔ Yes | The standard FHIR Bundle JSON payload |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FHIRPreScreenResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/fhir/pre-screen', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/fhir/pre-screen',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/fhir/pre-screen' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/fhir/prefill`

**Summary:** Fhir Prefill  
**Operation ID:** `fhir_prefill_api_v1_interop_fhir_prefill_post`  

**Clinical & GxP Rationale:**  
Ingest a standard FHIR Bundle payload and return mapped CDASH eCRF fields.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `FHIRPrefillRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Unique identifier of the clinical study |
| `bundle` | `object` | ✔ Yes | The standard FHIR Bundle JSON payload |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/fhir/prefill', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/fhir/prefill',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/fhir/prefill' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/fhir/semantic-map`

**Summary:** Fhir Semantic Map  
**Operation ID:** `fhir_semantic_map_api_v1_interop_fhir_semantic_map_post`  

**Clinical & GxP Rationale:**  
Execute hybrid FHIR-to-CDISC semantic interoperability mapping.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `FHIRSemanticMapRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Unique identifier of the clinical study |
| `bundle` | `object` | ✔ Yes | The standard FHIR Bundle JSON payload |
| `enable_deterministic` | `boolean` | No | Whether to execute Tier 1 deterministic ConceptMaps |
| `enable_embedding` | `boolean` | No | Whether to execute Tier 2 embedding cosine similarity matching |
| `enable_llm_fallback` | `boolean` | No | Whether to execute Tier 3 LLM semantic reasoning for narrative text |
| `embedding_confidence_threshold` | `number` | No | Minimum cosine similarity threshold for embedding tier |
| `llm_confidence_threshold` | `number` | No | Minimum confidence threshold for LLM extraction tier |
| `human_review_confidence_threshold` | `number` | No | Threshold below which mappings are flagged for human Data Manager review |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FHIRSemanticMapResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/fhir/semantic-map', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/fhir/semantic-map',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/fhir/semantic-map' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/instruments`

**Summary:** Create Instrument  
**Operation ID:** `create_instrument_api_v1_interop_instruments_post`  

**Clinical & GxP Rationale:**  
Author a new eCOA questionnaire/diary definition.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `InstrumentCreateDTO`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Unique identifier of the clinical study |
| `name` | `string` | ✔ Yes | The name of the questionnaire/diary |
| `description` | `string` | `null` | No | Optional description |
| `items` | `object` | ✔ Yes | Items/questions |
| `response_types` | `object` | ✔ Yes | Response types and options |
| `scoring_metadata` | `object` | ✔ Yes | Scoring metadata |
| `reason_for_change` | `string` | ✔ Yes | 21 CFR Part 11 compliant reason for change |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `InstrumentResponseDTO` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/instruments', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/instruments',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/instruments' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/instruments/{id}`

**Summary:** Get Instrument  
**Operation ID:** `get_instrument_api_v1_interop_instruments__id__get`  

**Clinical & GxP Rationale:**  
Retrieve an Instrument definition by ID.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `InstrumentResponseDTO` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/instruments/{id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/instruments/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/instruments/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/lakehouse/ingest`

**Summary:** Ingest heterogeneous clinical stream data  
**Operation ID:** `ingest_stream_data_api_v1_interop_lakehouse_ingest_post`  

**Clinical & GxP Rationale:**  
Ingest and normalize heterogeneous trial streams (EDC, ePRO, Labs, Wearables, FHIR, Imaging).

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `MultiStreamIngestPayload`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Clinical trial protocol ID (e.g. STUDY-901) |
| `stream_type` | `DataStreamType` | ✔ Yes | Stream type being ingested |
| `edc_items` | `Array<EDCEcrfIngestItem>` | `null` | No | EDC eCRF items when stream_type=EDC_ECRF |
| `epro_items` | `Array<EPROEcoaIngestItem>` | `null` | No | ePRO items when stream_type=EPRO_ECOA |
| `central_lab_items` | `Array<CentralLabIngestItem>` | `null` | No | Central lab items when stream_type=CENTRAL_LAB |
| `local_lab_items` | `Array<LocalLabIngestItem>` | `null` | No | Local lab items when stream_type=LOCAL_LAB |
| `wearable_items` | `Array<IOTWearableIngestItem>` | `null` | No | IoT wearable items when stream_type=IOT_WEARABLE |
| `fhir_items` | `Array<EHRFHIRIngestItem>` | `null` | No | FHIR items when stream_type=EHR_FHIR |
| `imaging_items` | `Array<MedicalImagingIngestItem>` | `null` | No | Imaging items when stream_type=MEDICAL_IMAGING |
| `reason_for_ingest` | `string` | No | GxP justification for ingestion |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `IngestResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/lakehouse/ingest', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/lakehouse/ingest',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/lakehouse/ingest' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/lakehouse/metrics/{study_id}`

**Summary:** Retrieve real-time Lakehouse stream and snapshot metrics  
**Operation ID:** `get_metrics_api_v1_interop_lakehouse_metrics__study_id__get`  

**Clinical & GxP Rationale:**  
Aggregate real-time data stream, subject, site, and snapshot metrics for a study.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `LakehouseMetricsResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/lakehouse/metrics/{study_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/lakehouse/metrics/{study_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/lakehouse/metrics/{study_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/lakehouse/snapshots`

**Summary:** Create an immutable, version-pinned clinical data snapshot  
**Operation ID:** `create_snapshot_api_v1_interop_lakehouse_snapshots_post`  

**Clinical & GxP Rationale:**  
Pool lakehouse observations into an immutable, version-pinned snapshot sealed with SHA-256.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CreateSnapshotRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Clinical study identifier (e.g. STUDY-901) |
| `version_tag` | `string` | ✔ Yes | Unique version tag (e.g. v1.0.0-INTERIM-1) |
| `milestone` | `LockMilestoneType` | ✔ Yes | Target database lock milestone |
| `lock_id` | `string` | `null` | No | Optional associated DataLock ID |
| `reason_for_snapshot` | `string` | ✔ Yes | Formal GxP justification for snapshot creation |
| `filter_criteria` | `SnapshotFilterCriteria` | `null` | No | Optional stream, domain, subject, and time cutoff filtering criteria |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `SnapshotResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/lakehouse/snapshots', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/lakehouse/snapshots',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/lakehouse/snapshots' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/lakehouse/snapshots`

**Summary:** List version-pinned snapshots for a study  
**Operation ID:** `list_snapshots_api_v1_interop_lakehouse_snapshots_get`  

**Clinical & GxP Rationale:**  
Retrieve all sealed, version-pinned snapshots for the specified study.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | Study identifier |
| `milestone` | `query` | `string` | `null` | No | Optional milestone filter |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SnapshotListResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/lakehouse/snapshots', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/lakehouse/snapshots',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/lakehouse/snapshots' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/lakehouse/snapshots/{snapshot_id}`

**Summary:** Retrieve snapshot metadata and seal info  
**Operation ID:** `get_snapshot_details_api_v1_interop_lakehouse_snapshots__snapshot_id__get`  

**Clinical & GxP Rationale:**  
Fetch metadata and cryptographic digest for a specific snapshot.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `snapshot_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SnapshotResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/lakehouse/snapshots/{snapshot_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/lakehouse/snapshots/{snapshot_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/lakehouse/snapshots/{snapshot_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/lakehouse/snapshots/{snapshot_id}/audit-trail`

**Summary:** Retrieve 21 CFR Part 11 audit log for a snapshot  
**Operation ID:** `get_snapshot_audit_trail_api_v1_interop_lakehouse_snapshots__snapshot_id__audit_trail_get`  

**Clinical & GxP Rationale:**  
Retrieve all chronological 21 CFR Part 11 audit trail entries for a snapshot.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `snapshot_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SnapshotAuditTrailResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/lakehouse/snapshots/{snapshot_id}/audit-trail', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/lakehouse/snapshots/{snapshot_id}/audit-trail',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/lakehouse/snapshots/{snapshot_id}/audit-trail' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/lakehouse/snapshots/{snapshot_id}/data`

**Summary:** Fetch snapshot pool data records via GET  
**Operation ID:** `get_snapshot_data_api_v1_interop_lakehouse_snapshots__snapshot_id__data_get`  

**Clinical & GxP Rationale:**  
Convenience endpoint to retrieve observations in a snapshot pool.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `snapshot_id` | `path` | `string` | ✔ Yes | — |
| `subject_id` | `query` | `string` | `null` | No | Optional subject filter |
| `domain` | `query` | `string` | `null` | No | Optional domain filter |
| `stream_type` | `query` | `DataStreamType` | `null` | No | Optional stream filter |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SnapshotDataQueryResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/lakehouse/snapshots/{snapshot_id}/data', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/lakehouse/snapshots/{snapshot_id}/data',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/lakehouse/snapshots/{snapshot_id}/data' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/lakehouse/snapshots/{snapshot_id}/query`

**Summary:** Query pooled records inside a version-pinned snapshot  
**Operation ID:** `query_snapshot_data_api_v1_interop_lakehouse_snapshots__snapshot_id__query_post`  

**Clinical & GxP Rationale:**  
Query observations pooled inside a snapshot, enforcing role-based unblinded masking.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `snapshot_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SnapshotDataQueryRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `filter_criteria` | `SnapshotFilterCriteria` | `null` | No | Optional slice and filter criteria |
| `request_unblinded` | `boolean` | No | Whether caller requests unblinded access (requires role) |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SnapshotDataQueryResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/lakehouse/snapshots/{snapshot_id}/query', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/lakehouse/snapshots/{snapshot_id}/query',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/lakehouse/snapshots/{snapshot_id}/query' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/lakehouse/snapshots/{snapshot_id}/verify`

**Summary:** Verify cryptographic SHA-256 integrity of sealed snapshot  
**Operation ID:** `verify_snapshot_api_v1_interop_lakehouse_snapshots__snapshot_id__verify_post`  

**Clinical & GxP Rationale:**  
Recompute and verify the SHA-256 seal against all pooled records in the snapshot.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `snapshot_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `VerifySnapshotResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/lakehouse/snapshots/{snapshot_id}/verify', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/lakehouse/snapshots/{snapshot_id}/verify',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/lakehouse/snapshots/{snapshot_id}/verify' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/notifications/{notification_id}/acknowledge`

**Summary:** Acknowledge Notification  
**Operation ID:** `acknowledge_notification_api_v1_interop_notifications__notification_id__acknowledge_post`  

**Clinical & GxP Rationale:**  
Acknowledge/read a notification.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `notification_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `AcknowledgeNotificationRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `reason_for_change` | `string` | ✔ Yes | 21 CFR Part 11 compliant reason for change |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SubjectNotificationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/notifications/{notification_id}/acknowledge', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/notifications/{notification_id}/acknowledge',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/notifications/{notification_id}/acknowledge' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/reminders/compute`

**Summary:** Compute Reminders  
**Operation ID:** `compute_reminders_api_v1_interop_reminders_compute_post`  

**Clinical & GxP Rationale:**  
Compute due reminders from assignment schedules on demand.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `subject_id` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/reminders/compute', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/reminders/compute',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/reminders/compute' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/sce/execute`

**Summary:** Execute statistical program in isolated containerized sandbox  
**Operation ID:** `execute_program_api_v1_interop_sce_execute_post`  

**Clinical & GxP Rationale:**  
Execute validated R or Python script against version-pinned data snapshot with signed run manifest.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ExecuteProgramRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Clinical study ID |
| `program_id` | `string` | ✔ Yes | Statistical program ID to execute |
| `snapshot_id` | `string` | ✔ Yes | Version-pinned data snapshot ID |
| `is_unblinded` | `boolean` | No | Request unblinded treatment arm access |
| `database_lock_token_id` | `string` | `null` | No | Required formal database lock authorization token if is_unblinded is True |
| `sandbox_config` | `ExecutionSandboxConfig` | `null` | No | Optional resource bounds and sandbox settings |
| `reason_for_execution` | `string` | No | GxP execution rationale |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ExecuteProgramResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/sce/execute', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/sce/execute',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/sce/execute' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/sce/lock-tokens`

**Summary:** Issue database lock authorization token for unblinded analysis  
**Operation ID:** `issue_lock_token_api_v1_interop_sce_lock_tokens_post`  

**Clinical & GxP Rationale:**  
Issue a cryptographically signed database lock token granting unblinded statistical analysis access.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `IssueLockTokenRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Clinical study ID |
| `lock_id` | `string` | ✔ Yes | Database lock reference ID |
| `milestone` | `string` | ✔ Yes | Lock milestone tier (e.g. DATABASE_LOCK) |
| `issued_to` | `string` | ✔ Yes | Authorized user ID |
| `reason` | `string` | ✔ Yes | Regulatory justification for unblinded access |
| `expires_in_hours` | `integer` | No | Validity duration in hours (default 24h) |
| `authorized_roles` | `Array<string>` | `null` | No | Optional list of permitted unblinded roles |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `LockTokenResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/sce/lock-tokens', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/sce/lock-tokens',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/sce/lock-tokens' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/sce/lock-tokens`

**Summary:** List active database lock authorization tokens  
**Operation ID:** `list_lock_tokens_api_v1_interop_sce_lock_tokens_get`  

**Clinical & GxP Rationale:**  
List active authorization tokens for unblinded analysis.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | Clinical study ID |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `LockTokenListResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/sce/lock-tokens', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/sce/lock-tokens',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/sce/lock-tokens' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/sce/lock-tokens/{token_id}/revoke`

**Summary:** Revoke an active database lock authorization token  
**Operation ID:** `revoke_lock_token_api_v1_interop_sce_lock_tokens__token_id__revoke_post`  

**Clinical & GxP Rationale:**  
Revoke an active database lock token.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `token_id` | `path` | `string` | ✔ Yes | — |
| `reason` | `query` | `string` | No | Revocation reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/sce/lock-tokens/{token_id}/revoke', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/sce/lock-tokens/{token_id}/revoke',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/sce/lock-tokens/{token_id}/revoke' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/sce/programs`

**Summary:** Create or register a statistical analysis program  
**Operation ID:** `create_program_api_v1_interop_sce_programs_post`  

**Clinical & GxP Rationale:**  
Register a version-controlled R or Python statistical program with package lockfile.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CreateProgramRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Associated clinical study ID |
| `name` | `string` | ✔ Yes | Program script filename (e.g. t_14_1_demog.py) |
| `title` | `string` | ✔ Yes | Human-readable program title |
| `description` | `string` | No | Detailed statistical methodology description |
| `language` | `ExecutionLanguage` | ✔ Yes | Runtime language: PYTHON or R |
| `version` | `string` | No | Semantic version string |
| `source_code` | `string` | ✔ Yes | Script source code |
| `lockfile_content` | `string` | `null` | No | Raw text of uv.lock or renv.lock |
| `lock_type` | `PackageLockType` | No | Lockfile format |
| `target_domain` | `string` | `null` | No | Target CDISC output domain (e.g. ADSL, TLF-14.1) |
| `reason_for_change` | `string` | No | 21 CFR Part 11 change rationale |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `ProgramResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/sce/programs', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/sce/programs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/sce/programs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/sce/programs`

**Summary:** List statistical programs for a study  
**Operation ID:** `list_programs_api_v1_interop_sce_programs_get`  

**Clinical & GxP Rationale:**  
List all version-controlled statistical programs for a study.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | Clinical study ID |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ProgramListResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/sce/programs', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/sce/programs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/sce/programs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/sce/programs/{program_id}`

**Summary:** Retrieve statistical program by ID  
**Operation ID:** `get_program_api_v1_interop_sce_programs__program_id__get`  

**Clinical & GxP Rationale:**  
Retrieve statistical program definition.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `program_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ProgramResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/sce/programs/{program_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/sce/programs/{program_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/sce/programs/{program_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/sce/runs`

**Summary:** List historical execution runs for a study  
**Operation ID:** `list_execution_runs_api_v1_interop_sce_runs_get`  

**Clinical & GxP Rationale:**  
List all historical analytical execution runs.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | Clinical study ID |
| `program_id` | `query` | `string` | `null` | No | Optional program ID filter |
| `snapshot_id` | `query` | `string` | `null` | No | Optional snapshot ID filter |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ExecutionRunListResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/sce/runs', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/sce/runs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/sce/runs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/sce/runs/{run_id}`

**Summary:** Retrieve execution run manifest and details  
**Operation ID:** `get_execution_run_api_v1_interop_sce_runs__run_id__get`  

**Clinical & GxP Rationale:**  
Retrieve details and signed manifest of an execution run.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `run_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ExecutionRunDetailResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/sce/runs/{run_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/sce/runs/{run_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/sce/runs/{run_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/sce/runs/{run_id}/artifacts/{artifact_id}`

**Summary:** Retrieve generated artifact details and preview  
**Operation ID:** `get_run_artifact_api_v1_interop_sce_runs__run_id__artifacts__artifact_id__get`  

**Clinical & GxP Rationale:**  
Retrieve output artifact metadata and content preview.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `run_id` | `path` | `string` | ✔ Yes | — |
| `artifact_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/sce/runs/{run_id}/artifacts/{artifact_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/sce/runs/{run_id}/artifacts/{artifact_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/sce/runs/{run_id}/artifacts/{artifact_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/sce/runs/{run_id}/verify`

**Summary:** Cryptographically verify execution run manifest seal  
**Operation ID:** `verify_run_manifest_api_v1_interop_sce_runs__run_id__verify_post`  

**Clinical & GxP Rationale:**  
Assert bit-for-bit SHA-256 seal integrity and 21 CFR Part 11 electronic signature.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `run_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `VerifyManifestResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/sce/runs/{run_id}/verify', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/sce/runs/{run_id}/verify',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/sce/runs/{run_id}/verify' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/sce/validate-lockfile`

**Summary:** Validate package lockfile integrity and GxP qualification  
**Operation ID:** `validate_lockfile_api_v1_interop_sce_validate_lockfile_post`  

**Clinical & GxP Rationale:**  
Parse and verify package lockfile (uv.lock / renv.lock) integrity and qualification.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ValidateLockfileRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `lock_type` | `PackageLockType` | ✔ Yes | Format: UV_LOCK, RENV_LOCK, or REQUIREMENTS_TXT |
| `content` | `string` | ✔ Yes | Raw text content of lockfile |
| `expected_digest` | `string` | `null` | No | Optional SHA-256 digest to assert immutability |
| `strict_qualification` | `boolean` | No | Enforce strict package whitelist validation |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ValidateLockfileResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/sce/validate-lockfile', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/sce/validate-lockfile',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/sce/validate-lockfile' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/sensors/daily-summaries`

**Summary:** Query calculated daily summary endpoints  
**Operation ID:** `list_daily_summaries_api_v1_interop_sensors_daily_summaries_get`  

**Clinical & GxP Rationale:**  
Retrieve aggregate daily summary endpoints (TIR, glucose, steps, cadence, RHR, FEV1).

@req:PRD-DCT-002
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | Study ID |
| `subject_id` | `query` | `string` | `null` | No | Optional pseudonymized subject ID |
| `device_type` | `query` | `SensorDeviceType` | `null` | No | Device type filter |
| `start_date` | `query` | `string` | `null` | No | Start date filter |
| `end_date` | `query` | `string` | `null` | No | End date filter |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DailySummariesResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/sensors/daily-summaries', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/sensors/daily-summaries',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/sensors/daily-summaries' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/sensors/devices`

**Summary:** Register & map approved medical IoT sensor device  
**Operation ID:** `register_device_api_v1_interop_sensors_devices_post`  

**Clinical & GxP Rationale:**  
Register and map an approved medical IoT sensor device (CGM, Actigraphy, Spirometry, BP).

@req:PRD-DCT-002
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DeviceRegistrationRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study protocol ID |
| `site_id` | `string` | `null` | No | Site ID |
| `subject_id` | `string` | ✔ Yes | Assigned subject ID |
| `device_type` | `SensorDeviceType` | ✔ Yes | Device category |
| `vendor` | `string` | ✔ Yes | Device manufacturer |
| `model_name` | `string` | ✔ Yes | Device model |
| `device_udi` | `string` | `null` | No | FDA Unique Device Identifier (UDI) |
| `mac_address` | `string` | `null` | No | Bluetooth MAC address |
| `serial_number` | `string` | `null` | No | Device serial number |
| `firmware_version` | `string` | `null` | No | Firmware version |
| `calibration_status` | `CalibrationState` | No | Initial calibration state |
| `calibration_date` | `string` | `null` | No | Date of last calibration |
| `reason_for_change` | `string` | No | GxP justification |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `DeviceRegistrationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/sensors/devices', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/sensors/devices',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/sensors/devices' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/sensors/devices`

**Summary:** List registered medical IoT devices  
**Operation ID:** `list_devices_api_v1_interop_sensors_devices_get`  

**Clinical & GxP Rationale:**  
List registered medical IoT sensor devices for a clinical study.

@req:PRD-DCT-002
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | Study protocol ID |
| `site_id` | `query` | `string` | `null` | No | Optional site ID filter |
| `subject_id` | `query` | `string` | `null` | No | Optional subject ID filter |
| `device_type` | `query` | `SensorDeviceType` | `null` | No | Optional device type filter |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DeviceListResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/sensors/devices', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/sensors/devices',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/sensors/devices' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/sensors/ingest`

**Summary:** Ingest continuous biometric IoT sensor telemetry  
**Operation ID:** `ingest_telemetry_stream_api_v1_interop_sensors_ingest_post`  

**Clinical & GxP Rationale:**  
Ingest continuous time-series telemetry streams from approved medical IoT devices.

Applies:
1. Automated HMAC-SHA256 pseudonymization & device MAC/UDI mapping
2. Clock drift correction between device RTC and gateway UTC
3. Normalization into Open mHealth (OMH) & HL7 FHIR Observation schemas
4. Aggregate daily summary endpoints calculation (Time-in-Range TIR, mean glucose, steps, FEV1)
5. Automated safety threshold alert evaluation
6. Automated feeding into the Clinical Data Repository (CDR) Lakehouse

@req:PRD-DCT-002
@req:PRD-DCT-001
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SensorIngestStreamPayload`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Clinical trial protocol ID (e.g. STUDY-901) |
| `site_id` | `string` | `null` | No | Clinical site identifier |
| `subject_id` | `string` | ✔ Yes | Participant identifier (raw or pseudonymized) |
| `device_type` | `SensorDeviceType` | ✔ Yes | Medical IoT device category (CGM, ACTIGRAPHY, SPIROMETRY, BLOOD_PRESSURE) |
| `vendor` | `string` | ✔ Yes | Device manufacturer/vendor (e.g. Dexcom, Abbott, ActiGraph, MIR, Omron) |
| `model_name` | `string` | ✔ Yes | Device model name (e.g. G7, FreeStyle Libre 3, wGT3X-BT, Spirobank Smart) |
| `device_udi` | `string` | `null` | No | FDA Unique Device Identifier (UDI) barcode/string |
| `mac_address` | `string` | `null` | No | Bluetooth Low Energy (BLE) MAC address (e.g. 00:1A:7D:DA:71:13) |
| `serial_number` | `string` | `null` | No | Device hardware serial number |
| `firmware_version` | `string` | `null` | No | Device firmware build version |
| `device_sync_timestamp` | `string` | ✔ Yes | Timestamp reported by device RTC at the synchronization moment |
| `gateway_receive_timestamp` | `string` | ✔ Yes | Authoritative UTC timestamp when gateway received the payload |
| `readings` | `Array<RawSensorReading>` | ✔ Yes | Batch of high-frequency sensor readings |
| `feed_to_lakehouse` | `boolean` | No | Whether to automatically feed normalized records to CDR Lakehouse |
| `reason_for_change` | `string` | No | GxP audit trail justification |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `SensorIngestResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/sensors/ingest', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/sensors/ingest',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/sensors/ingest' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/sensors/metrics/{study_id}`

**Summary:** Get connected IoT sensor dashboard metrics  
**Operation ID:** `get_sensor_dashboard_metrics_api_v1_interop_sensors_metrics__study_id__get`  

**Clinical & GxP Rationale:**  
Retrieve real-time IoT sensor telemetry metrics across a clinical study.

@req:PRD-DCT-002
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SensorMetricsResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/sensors/metrics/{study_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/sensors/metrics/{study_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/sensors/metrics/{study_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/sensors/safety-alerts`

**Summary:** Query automated safety threshold alerts  
**Operation ID:** `list_safety_alerts_api_v1_interop_sensors_safety_alerts_get`  

**Clinical & GxP Rationale:**  
Retrieve triggered biometric safety threshold alerts for clinical investigation.

@req:PRD-DCT-002
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | Study protocol ID |
| `subject_id` | `query` | `string` | `null` | No | Optional pseudonymized subject filter |
| `severity` | `query` | `string` | `null` | No | Severity filter (CRITICAL, WARNING) |
| `alert_type` | `query` | `string` | `null` | No | Alert type filter |
| `is_acknowledged` | `query` | `boolean` | `null` | No | Acknowledgment filter |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SafetyAlertsResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/sensors/safety-alerts', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/sensors/safety-alerts',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/sensors/safety-alerts' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/interop/sensors/safety-alerts/{alert_id}/acknowledge`

**Summary:** Acknowledge safety alert with electronic signature  
**Operation ID:** `acknowledge_safety_alert_api_v1_interop_sensors_safety_alerts__alert_id__acknowledge_post`  

**Clinical & GxP Rationale:**  
Acknowledge a safety threshold alert with 21 CFR Part 11 electronic sign-off and clinical notes.

@req:PRD-DCT-002
@req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `alert_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `AcknowledgeAlertRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `notes` | `string` | ✔ Yes | Clinical evaluation and justification notes |
| `audit_signature` | `string` | `null` | No | 21 CFR Part 11 electronic signature token |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `AcknowledgeAlertResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/sensors/safety-alerts/{alert_id}/acknowledge', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/interop/api/v1/interop/sensors/safety-alerts/{alert_id}/acknowledge',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/interop/api/v1/interop/sensors/safety-alerts/{alert_id}/acknowledge' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/subjects/{subject_id}/compliance`

**Summary:** Get Subject Compliance  
**Operation ID:** `get_subject_compliance_api_v1_interop_subjects__subject_id__compliance_get`  

**Clinical & GxP Rationale:**  
Retrieve/compute eCOA compliance status metrics for a subject.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `subject_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SubjectComplianceResponseDTO` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/subjects/{subject_id}/compliance', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/subjects/{subject_id}/compliance',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/subjects/{subject_id}/compliance' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/subjects/{subject_id}/instruments`

**Summary:** Get Subject Assigned Instruments  
**Operation ID:** `get_subject_assigned_instruments_api_v1_interop_subjects__subject_id__instruments_get`  

**Clinical & GxP Rationale:**  
Retrieve all unique assigned instruments for a given subject.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `subject_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<InstrumentResponseDTO>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/subjects/{subject_id}/instruments', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/subjects/{subject_id}/instruments',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/subjects/{subject_id}/instruments' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/interop/subjects/{subject_id}/notifications`

**Summary:** Get Subject Notifications  
**Operation ID:** `get_subject_notifications_api_v1_interop_subjects__subject_id__notifications_get`  

**Clinical & GxP Rationale:**  
Retrieve all notifications for a given subject.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `subject_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<SubjectNotificationResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/api/v1/interop/subjects/{subject_id}/notifications', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/api/v1/interop/subjects/{subject_id}/notifications',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/api/v1/interop/subjects/{subject_id}/notifications' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/health`

**Summary:** Health Check  
**Operation ID:** `health_check_health_get`  

**Clinical & GxP Rationale:**  
Service health check endpoint.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/interop/health', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/interop/health',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/interop/health' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---
