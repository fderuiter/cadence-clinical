# Blinded DICOM Adjudication

> **Functional Domain:** `Safety, Quality & Governance`  
> **Service Host Port:** `localhost:8015`  
> **Gateway Proxy Prefix:** `/api/v1/imaging`  
> **Persistence Backend:** `PostgreSQL / SQLite`  

## Overview

Zero-footprint WebGL Multi-Planar Reconstruction (MPR), blinded dual-reader RECIST 1.1 oncology evaluation, and 20% discordance triggers.

---

## Endpoints Summary

| Method | Path | Summary | GxP Auth & Signatures |
| :--- | :--- | :--- | :--- |
| `POST` | [/api/v1/imaging/adjudication/sessions](#post-api-v1-imaging-adjudication-sessions) | Create Adjudication Session | ✍️ Audited Write |
| `GET` | [/api/v1/imaging/adjudication/sessions](#get-api-v1-imaging-adjudication-sessions) | List Adjudication Sessions | 👀 Read-Only |
| `GET` | [/api/v1/imaging/adjudication/sessions/{session_id}](#get-api-v1-imaging-adjudication-sessions-session_id) | Get Adjudication Session | 👀 Read-Only |
| `POST` | [/api/v1/imaging/adjudication/sessions/{session_id}/assessments](#post-api-v1-imaging-adjudication-sessions-session_id-assessments) | Submit Reader Assessment | ✍️ Audited Write |
| `GET` | [/api/v1/imaging/adjudication/sessions/{session_id}/edc-binding](#get-api-v1-imaging-adjudication-sessions-session_id-edc-binding) | Get Edc Ecrf Tumor Binding | 👀 Read-Only |
| `GET` | [/api/v1/imaging/adjudication/sessions/{session_id}/export-sdtm](#get-api-v1-imaging-adjudication-sessions-session_id-export-sdtm) | Export Adjudication Sdtm Datasets | 👀 Read-Only |
| `GET` | [/api/v1/imaging/adjudication/sessions/{session_id}/sdtm](#get-api-v1-imaging-adjudication-sessions-session_id-sdtm) | Export Adjudication Sdtm Datasets | 👀 Read-Only |
| `POST` | [/api/v1/imaging/adjudication/sessions/{session_id}/sign-off](#post-api-v1-imaging-adjudication-sessions-session_id-sign-off) | Sign Off Adjudication | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/imaging/annotations](#post-api-v1-imaging-annotations) | Create Caliper Annotation | ✍️ Audited Write |
| `GET` | [/api/v1/imaging/annotations](#get-api-v1-imaging-annotations) | List Caliper Annotations | 👀 Read-Only |
| `DELETE` | [/api/v1/imaging/annotations/{annotation_id}](#delete-api-v1-imaging-annotations-annotation_id) | Delete Caliper Annotation | ✍️ Audited Write |
| `GET` | [/api/v1/imaging/certificates/{certificate_id}/verify](#get-api-v1-imaging-certificates-certificate_id-verify) | Verify Certificate | 👀 Read-Only |
| `POST` | [/api/v1/imaging/deidentify](#post-api-v1-imaging-deidentify) | Deidentify In Flight | ✍️ Audited Write |
| `GET` | [/api/v1/imaging/instances/{sop_instance_uid}/file](#get-api-v1-imaging-instances-sop_instance_uid-file) | Download Instance File | 👀 Read-Only |
| `GET` | [/api/v1/imaging/instances/{sop_instance_uid}/presigned-url](#get-api-v1-imaging-instances-sop_instance_uid-presigned-url) | Get Presigned Download Url | 👀 Read-Only |
| `POST` | [/api/v1/imaging/response-evaluations/lugano](#post-api-v1-imaging-response-evaluations-lugano) | Evaluate Lugano Response | ✍️ Audited Write |
| `POST` | [/api/v1/imaging/response-evaluations/recist](#post-api-v1-imaging-response-evaluations-recist) | Evaluate Recist Response | ✍️ Audited Write |
| `GET` | [/api/v1/imaging/series/{series_instance_uid}/mpr-metadata](#get-api-v1-imaging-series-series_instance_uid-mpr-metadata) | Get Series Mpr Metadata | 👀 Read-Only |
| `GET` | [/api/v1/imaging/studies](#get-api-v1-imaging-studies) | List Studies | 👀 Read-Only |
| `GET` | [/api/v1/imaging/studies/{study_uid}](#get-api-v1-imaging-studies-study_uid) | Get Study Detail | 👀 Read-Only |
| `GET` | [/api/v1/imaging/subjects/{subject_id}/timepoints](#get-api-v1-imaging-subjects-subject_id-timepoints) | Get Subject Timepoints Trajectory | 👀 Read-Only |
| `POST` | [/api/v1/imaging/subjects/{subject_id}/timepoints/evaluate](#post-api-v1-imaging-subjects-subject_id-timepoints-evaluate) | Evaluate And Lock Timepoint | ✍️ Audited Write |
| `POST` | [/api/v1/imaging/upload](#post-api-v1-imaging-upload) | Upload Dicom | ✍️ Audited Write |
| `GET` | [/health](#get-health) | Health Check | 👀 Read-Only |

---

## Granular Endpoint Specifications

### `POST` `/api/v1/imaging/adjudication/sessions`

**Summary:** Create Adjudication Session  
**Operation ID:** `create_adjudication_session_api_v1_imaging_adjudication_sessions_post`  

**Clinical & GxP Rationale:**  
Instantiates and routes a new Blinded Dual-Reader Independent Review Charter session.

@req:PRD-IMG-003
@req:PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CreateAdjudicationSessionDto`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Trial Protocol Identifier |
| `site_id` | `string` | ✔ Yes | Clinical Trial Site ID |
| `subject_id` | `string` | ✔ Yes | Trial Subject UUID |
| `timepoint_name` | `string` | ✔ Yes | e.g. BASELINE, CYCLE_2_DAY_1 |
| `study_envelope_id` | `string` | `null` | No | Optional study envelope ID |
| `assessment_criteria` | `string` | No | RECIST_1_1, LUGANO_2014, RANO |
| `reader_1_id` | `string` | ✔ Yes | Certified Radiologist 1 User ID |
| `reader_1_name` | `string` | No | Reader 1 Full Name |
| `reader_2_id` | `string` | ✔ Yes | Certified Radiologist 2 User ID |
| `reader_2_name` | `string` | No | Reader 2 Full Name |
| `adjudicator_id` | `string` | `null` | No | Adjudication Committee Chair User ID |
| `adjudicator_name` | `string` | `null` | No | Adjudicator Full Name |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `AdjudicationSessionResponseDto` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/imaging/api/v1/imaging/adjudication/sessions', {
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
        '/api/v1/imaging/api/v1/imaging/adjudication/sessions',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/imaging/api/v1/imaging/adjudication/sessions' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/imaging/adjudication/sessions`

**Summary:** List Adjudication Sessions  
**Operation ID:** `list_adjudication_sessions_api_v1_imaging_adjudication_sessions_get`  

**Clinical & GxP Rationale:**  
Lists adjudication sessions with dynamic blinding enforcement based on requesting role.

@req:PRD-IMG-003

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |
| `subject_id` | `query` | `string` | `null` | No | — |
| `timepoint_name` | `query` | `string` | `null` | No | — |
| `workflow_status` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<AdjudicationSessionResponseDto>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/imaging/api/v1/imaging/adjudication/sessions', {
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
        '/api/v1/imaging/api/v1/imaging/adjudication/sessions',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/imaging/api/v1/imaging/adjudication/sessions' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/imaging/adjudication/sessions/{session_id}`

**Summary:** Get Adjudication Session  
**Operation ID:** `get_adjudication_session_api_v1_imaging_adjudication_sessions__session_id__get`  

**Clinical & GxP Rationale:**  
Retrieves an adjudication session with strict blinding isolation between readers.

@req:PRD-IMG-003

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
| `200` | Successful Response | `AdjudicationSessionResponseDto` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/imaging/api/v1/imaging/adjudication/sessions/{session_id}', {
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
        '/api/v1/imaging/api/v1/imaging/adjudication/sessions/{session_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/imaging/api/v1/imaging/adjudication/sessions/{session_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/imaging/adjudication/sessions/{session_id}/assessments`

**Summary:** Submit Reader Assessment  
**Operation ID:** `submit_reader_assessment_api_v1_imaging_adjudication_sessions__session_id__assessments_post`  

**Clinical & GxP Rationale:**  
Submits a blinded reader assessment and automatically detects response discordance.

@req:PRD-IMG-003
@req:PRD-IMG-005

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `session_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SubmitReaderAssessmentDto`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `reader_id` | `string` | ✔ Yes | Radiologist User ID |
| `reader_role` | `string` | ✔ Yes | READER_1, READER_2, or ADJUDICATOR |
| `reader_name` | `string` | No | Radiologist Display Name |
| `target_lesions` | `Array<LesionMeasurementDto>` | No | — |
| `non_target_status` | `string` | No | CR, NON_CR_NON_PD, PD, NE |
| `new_lesions_present` | `boolean` | No | — |
| `new_lesions_count` | `integer` | No | — |
| `sum_longest_diameters_mm` | `number` | No | — |
| `baseline_sld_mm` | `number` | `null` | No | — |
| `nadir_sld_mm` | `number` | `null` | No | — |
| `overall_response` | `string` | ✔ Yes | CR, PR, SD, PD, NE |
| `assessment_notes` | `string` | `null` | No | — |
| `digital_signature` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `AdjudicationSessionResponseDto` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/imaging/api/v1/imaging/adjudication/sessions/{session_id}/assessments', {
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
        '/api/v1/imaging/api/v1/imaging/adjudication/sessions/{session_id}/assessments',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/imaging/api/v1/imaging/adjudication/sessions/{session_id}/assessments' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/imaging/adjudication/sessions/{session_id}/edc-binding`

**Summary:** Get Edc Ecrf Tumor Binding  
**Operation ID:** `get_edc_ecrf_tumor_binding_api_v1_imaging_adjudication_sessions__session_id__edc_binding_get`  

**Clinical & GxP Rationale:**  
Extracts direct EDC eCRF tumor assessment form bindings from verified adjudication measurements.

@req:PRD-IMG-006

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
| `200` | Successful Response | `EDCTumorBindingResponseDto` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/imaging/api/v1/imaging/adjudication/sessions/{session_id}/edc-binding', {
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
        '/api/v1/imaging/api/v1/imaging/adjudication/sessions/{session_id}/edc-binding',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/imaging/api/v1/imaging/adjudication/sessions/{session_id}/edc-binding' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/imaging/adjudication/sessions/{session_id}/export-sdtm`

**Summary:** Export Adjudication Sdtm Datasets  
**Operation ID:** `export_adjudication_sdtm_datasets_api_v1_imaging_adjudication_sessions__session_id__export_sdtm_get`  

**Clinical & GxP Rationale:**  
Exports CDISC SDTM TU (Tumor Identification), TR (Tumor Results), and RS (Disease Response) datasets.

@req:PRD-IMG-006

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
| `200` | Successful Response | `SDTMDatasetExportResponseDto` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/imaging/api/v1/imaging/adjudication/sessions/{session_id}/export-sdtm', {
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
        '/api/v1/imaging/api/v1/imaging/adjudication/sessions/{session_id}/export-sdtm',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/imaging/api/v1/imaging/adjudication/sessions/{session_id}/export-sdtm' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/imaging/adjudication/sessions/{session_id}/sdtm`

**Summary:** Export Adjudication Sdtm Datasets  
**Operation ID:** `export_adjudication_sdtm_datasets_api_v1_imaging_adjudication_sessions__session_id__sdtm_get`  

**Clinical & GxP Rationale:**  
Exports CDISC SDTM TU (Tumor Identification), TR (Tumor Results), and RS (Disease Response) datasets.

@req:PRD-IMG-006

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
| `200` | Successful Response | `SDTMDatasetExportResponseDto` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/imaging/api/v1/imaging/adjudication/sessions/{session_id}/sdtm', {
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
        '/api/v1/imaging/api/v1/imaging/adjudication/sessions/{session_id}/sdtm',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/imaging/api/v1/imaging/adjudication/sessions/{session_id}/sdtm' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/imaging/adjudication/sessions/{session_id}/sign-off`

**Summary:** Sign Off Adjudication  
**Operation ID:** `sign_off_adjudication_api_v1_imaging_adjudication_sessions__session_id__sign_off_post`  

**Clinical & GxP Rationale:**  
Executes 21 CFR Part 11 step-up electronic signature sign-off for binding tie-breaker determination.

@req:PRD-IMG-005
@req:PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `session_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `StepUpAdjudicationSignOffDto`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `signer_id` | `string` | ✔ Yes | Adjudicator User ID |
| `signer_name` | `string` | ✔ Yes | Full Legal Name of Adjudicator |
| `password` | `string` | `null` | No | Step-up credential / authentication secret |
| `step_up_token` | `string` | `null` | No | Cryptographic step-up token |
| `signing_reason` | `string` | No | 21 CFR Part 11 signature reason (e.g. ADJUDICATION_SIGN_OFF, APPROVAL) |
| `binding_reader` | `string` | No | Source of binding measurements: READER_1, READER_2, or ADJUDICATOR |
| `rationale` | `string` | ✔ Yes | Clinical justification for tie-breaker determination |
| `adjudicator_assessment` | `SubmitReaderAssessmentDto` | `null` | No | Optional independent adjudicator measurement set |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `AdjudicationSessionResponseDto` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/imaging/api/v1/imaging/adjudication/sessions/{session_id}/sign-off', {
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
        '/api/v1/imaging/api/v1/imaging/adjudication/sessions/{session_id}/sign-off',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/imaging/api/v1/imaging/adjudication/sessions/{session_id}/sign-off' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/imaging/annotations`

**Summary:** Create Caliper Annotation  
**Operation ID:** `create_caliper_annotation_api_v1_imaging_annotations_post`  

**Clinical & GxP Rationale:**  
Creates a new oncologic caliper annotation with 21 CFR Part 11 audit logging.

@req:PRD-IMG-004
@req:PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CaliperAnnotationCreateDto`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_envelope_id` | `string` | `null` | No | — |
| `series_envelope_id` | `string` | `null` | No | — |
| `sop_instance_uid` | `string` | `null` | No | — |
| `study_id` | `string` | ✔ Yes | — |
| `site_id` | `string` | ✔ Yes | — |
| `subject_id` | `string` | ✔ Yes | — |
| `timepoint_name` | `string` | ✔ Yes | — |
| `lesion_id` | `string` | ✔ Yes | — |
| `lesion_type` | `string` | No | — |
| `organ_site` | `string` | ✔ Yes | — |
| `measurement_criteria` | `string` | No | — |
| `measurement_type` | `string` | No | — |
| `longest_diameter_mm` | `number` | ✔ Yes | — |
| `perpendicular_diameter_mm` | `number` | `null` | No | — |
| `volume_mm3` | `number` | `null` | No | — |
| `slice_index` | `integer` | `null` | No | — |
| `slice_location` | `number` | `null` | No | — |
| `deauville_score` | `integer` | `null` | No | — |
| `points` | `Array<PointCoordinateDto>` | No | — |
| `hounsfield_stats` | `object` | `null` | No | — |
| `notes` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `CaliperAnnotationResponseDto` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/imaging/api/v1/imaging/annotations', {
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
        '/api/v1/imaging/api/v1/imaging/annotations',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/imaging/api/v1/imaging/annotations' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/imaging/annotations`

**Summary:** List Caliper Annotations  
**Operation ID:** `list_caliper_annotations_api_v1_imaging_annotations_get`  

**Clinical & GxP Rationale:**  
Lists caliper annotations matching criteria.

@req:PRD-IMG-004

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_envelope_id` | `query` | `string` | `null` | No | — |
| `series_envelope_id` | `query` | `string` | `null` | No | — |
| `sop_instance_uid` | `query` | `string` | `null` | No | — |
| `subject_id` | `query` | `string` | `null` | No | — |
| `timepoint_name` | `query` | `string` | `null` | No | — |
| `reader_id` | `query` | `string` | `null` | No | — |
| `skip` | `query` | `integer` | No | — |
| `limit` | `query` | `integer` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<CaliperAnnotationResponseDto>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/imaging/api/v1/imaging/annotations', {
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
        '/api/v1/imaging/api/v1/imaging/annotations',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/imaging/api/v1/imaging/annotations' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/imaging/annotations/{annotation_id}`

**Summary:** Delete Caliper Annotation  
**Operation ID:** `delete_caliper_annotation_api_v1_imaging_annotations__annotation_id__delete`  

**Clinical & GxP Rationale:**  
Deletes a caliper annotation with 21 CFR Part 11 reason for change.

@req:PRD-IMG-004
@req:PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `annotation_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/imaging/api/v1/imaging/annotations/{annotation_id}', {
  method: 'DELETE',
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
    response = await client.delete(
        '/api/v1/imaging/api/v1/imaging/annotations/{annotation_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/imaging/api/v1/imaging/annotations/{annotation_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/imaging/certificates/{certificate_id}/verify`

**Summary:** Verify Certificate  
**Operation ID:** `verify_certificate_api_v1_imaging_certificates__certificate_id__verify_get`  

**Clinical & GxP Rationale:**  
Verifies the cryptographic HMAC-SHA256 signature of a de-identification certificate.

@req:PRD-IMG-002

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `certificate_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `CertificateVerificationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/imaging/api/v1/imaging/certificates/{certificate_id}/verify', {
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
        '/api/v1/imaging/api/v1/imaging/certificates/{certificate_id}/verify',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/imaging/api/v1/imaging/certificates/{certificate_id}/verify' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/imaging/deidentify`

**Summary:** Deidentify In Flight  
**Operation ID:** `deidentify_in_flight_api_v1_imaging_deidentify_post`  

**Clinical & GxP Rationale:**  
Performs in-memory de-identification on an uploaded DICOM file without persistent storage.

Returns the clean, de-identified DICOM bytes with certificate metadata headers.

@req:PRD-IMG-002

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/imaging/api/v1/imaging/deidentify', {
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
        '/api/v1/imaging/api/v1/imaging/deidentify',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/imaging/api/v1/imaging/deidentify' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/imaging/instances/{sop_instance_uid}/file`

**Summary:** Download Instance File  
**Operation ID:** `download_instance_file_api_v1_imaging_instances__sop_instance_uid__file_get`  

**Clinical & GxP Rationale:**  
Streams the stored de-identified DICOM binary file.

@req:PRD-IMG-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `sop_instance_uid` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/imaging/api/v1/imaging/instances/{sop_instance_uid}/file', {
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
        '/api/v1/imaging/api/v1/imaging/instances/{sop_instance_uid}/file',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/imaging/api/v1/imaging/instances/{sop_instance_uid}/file' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/imaging/instances/{sop_instance_uid}/presigned-url`

**Summary:** Get Presigned Download Url  
**Operation ID:** `get_presigned_download_url_api_v1_imaging_instances__sop_instance_uid__presigned_url_get`  

**Clinical & GxP Rationale:**  
Generates a secure, short-lived presigned URL for direct object storage download.

@req:PRD-IMG-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `sop_instance_uid` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `PresignedDownloadUrlResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/imaging/api/v1/imaging/instances/{sop_instance_uid}/presigned-url', {
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
        '/api/v1/imaging/api/v1/imaging/instances/{sop_instance_uid}/presigned-url',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/imaging/api/v1/imaging/instances/{sop_instance_uid}/presigned-url' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/imaging/response-evaluations/lugano`

**Summary:** Evaluate Lugano Response  
**Operation ID:** `evaluate_lugano_response_api_v1_imaging_response_evaluations_lugano_post`  

**Clinical & GxP Rationale:**  
Computes automated Lugano 2014 & Cheson derivation for lymphoma.

@req:PRD-IMG-004

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `LuganoEvaluationRequestDto`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `current_lesions` | `Array<LuganoLesionInputDto>` | ✔ Yes | — |
| `baseline_spd_mm2` | `number` | No | — |
| `prior_timepoints_spd` | `Array<number>` | No | — |
| `new_lesions_present` | `boolean` | No | — |
| `single_lesion_progression` | `boolean` | No | — |
| `non_measured_progression` | `boolean` | No | — |
| `is_baseline` | `boolean` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `LuganoEvaluationResponseDto` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/imaging/api/v1/imaging/response-evaluations/lugano', {
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
        '/api/v1/imaging/api/v1/imaging/response-evaluations/lugano',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/imaging/api/v1/imaging/response-evaluations/lugano' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/imaging/response-evaluations/recist`

**Summary:** Evaluate Recist Response  
**Operation ID:** `evaluate_recist_response_api_v1_imaging_response_evaluations_recist_post`  

**Clinical & GxP Rationale:**  
Computes automated RECIST 1.1 derivation for target and non-target lesions.

@req:PRD-IMG-004

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `RECISTEvaluationRequestDto`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `current_lesions` | `Array<RECISTLesionInputDto>` | ✔ Yes | — |
| `baseline_sld_mm` | `number` | No | — |
| `prior_timepoints_sld` | `Array<number>` | No | — |
| `non_target_status` | `string` | No | — |
| `new_lesions_present` | `boolean` | No | — |
| `new_lesions_count` | `integer` | No | — |
| `is_baseline` | `boolean` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `RECISTEvaluationResponseDto` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/imaging/api/v1/imaging/response-evaluations/recist', {
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
        '/api/v1/imaging/api/v1/imaging/response-evaluations/recist',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/imaging/api/v1/imaging/response-evaluations/recist' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/imaging/series/{series_instance_uid}/mpr-metadata`

**Summary:** Get Series Mpr Metadata  
**Operation ID:** `get_series_mpr_metadata_api_v1_imaging_series__series_instance_uid__mpr_metadata_get`  

**Clinical & GxP Rationale:**  
Retrieves 3D volume geometry, voxel spacing, and MPR orientation metadata.

@req:PRD-IMG-001
@req:PRD-IMG-004

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `series_instance_uid` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `MprMetadataResponseDto` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/imaging/api/v1/imaging/series/{series_instance_uid}/mpr-metadata', {
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
        '/api/v1/imaging/api/v1/imaging/series/{series_instance_uid}/mpr-metadata',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/imaging/api/v1/imaging/series/{series_instance_uid}/mpr-metadata' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/imaging/studies`

**Summary:** List Studies  
**Operation ID:** `list_studies_api_v1_imaging_studies_get`  

**Clinical & GxP Rationale:**  
Lists de-identified study metadata envelopes matching query filters.

@req:PRD-IMG-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | Filter by Protocol ID |
| `site_id` | `query` | `string` | `null` | No | Filter by Site ID |
| `subject_id` | `query` | `string` | `null` | No | Filter by Subject UUID |
| `modality` | `query` | `string` | `null` | No | Filter by Modality |
| `status` | `query` | `string` | `null` | No | — |
| `skip` | `query` | `integer` | No | — |
| `limit` | `query` | `integer` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<StudyEnvelopeSummaryDto>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/imaging/api/v1/imaging/studies', {
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
        '/api/v1/imaging/api/v1/imaging/studies',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/imaging/api/v1/imaging/studies' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/imaging/studies/{study_uid}`

**Summary:** Get Study Detail  
**Operation ID:** `get_study_detail_api_v1_imaging_studies__study_uid__get`  

**Clinical & GxP Rationale:**  
Retrieves full study envelope hierarchy including series and instance envelopes.

@req:PRD-IMG-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_uid` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `StudyEnvelopeDetailDto` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/imaging/api/v1/imaging/studies/{study_uid}', {
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
        '/api/v1/imaging/api/v1/imaging/studies/{study_uid}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/imaging/api/v1/imaging/studies/{study_uid}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/imaging/subjects/{subject_id}/timepoints`

**Summary:** Get Subject Timepoints Trajectory  
**Operation ID:** `get_subject_timepoints_trajectory_api_v1_imaging_subjects__subject_id__timepoints_get`  

**Clinical & GxP Rationale:**  
Retrieves full chronological trajectory of locked oncologic evaluations for a subject.

@req:PRD-IMG-004

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
| `200` | Successful Response | `SubjectTimepointsTrajectoryResponseDto` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/imaging/api/v1/imaging/subjects/{subject_id}/timepoints', {
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
        '/api/v1/imaging/api/v1/imaging/subjects/{subject_id}/timepoints',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/imaging/api/v1/imaging/subjects/{subject_id}/timepoints' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/imaging/subjects/{subject_id}/timepoints/evaluate`

**Summary:** Evaluate And Lock Timepoint  
**Operation ID:** `evaluate_and_lock_timepoint_api_v1_imaging_subjects__subject_id__timepoints_evaluate_post`  

**Clinical & GxP Rationale:**  
Evaluates, cryptographically signs, and locks an oncologic timepoint assessment.

@req:PRD-IMG-004
@req:PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `subject_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SubjectTimepointEvaluateRequestDto`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `site_id` | `string` | ✔ Yes | — |
| `timepoint_name` | `string` | ✔ Yes | — |
| `assessment_criteria` | `string` | No | — |
| `non_target_status` | `string` | No | — |
| `new_lesions_present` | `boolean` | No | — |
| `new_lesions_count` | `integer` | No | — |
| `is_baseline` | `boolean` | No | — |
| `digital_signature` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `TimepointTrajectorySummaryDto` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/imaging/api/v1/imaging/subjects/{subject_id}/timepoints/evaluate', {
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
        '/api/v1/imaging/api/v1/imaging/subjects/{subject_id}/timepoints/evaluate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/imaging/api/v1/imaging/subjects/{subject_id}/timepoints/evaluate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/imaging/upload`

**Summary:** Upload Dicom  
**Operation ID:** `upload_dicom_api_v1_imaging_upload_post`  

**Clinical & GxP Rationale:**  
Uploads and de-identifies DICOM file(s) or ZIP archives in-flight before persisting to S3.

@req:PRD-IMG-001
@req:PRD-IMG-002

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `DicomUploadResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/imaging/api/v1/imaging/upload', {
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
        '/api/v1/imaging/api/v1/imaging/upload',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/imaging/api/v1/imaging/upload' \
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
const response = await fetch('http://localhost:8000/api/v1/imaging/health', {
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
        '/api/v1/imaging/health',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/imaging/health' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---
