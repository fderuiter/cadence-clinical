# Cadence Clinical - API & Integration Specification

**Document Version:** 1.0.0-PROD
**Standards Compliance:** ISO 14155:2020, 21 CFR Part 11, ICH E6(R2), CDISC USDM v3.0/v4.0, CDISC ODM v2.0
**Target Audience:** Integration Engineers, Solution Architects, Regulatory Auditors, Security Officers

---

## 1. Executive Summary & Document Control

This specification serves as the absolute, contract-complete reference for all external and internal application programming interfaces (APIs) of the **Cadence Clinical Platform**. Cadence Clinical unifies Clinical Metadata Management (MDR) and downstream Electronic Data Capture (EDC) through an automated Digital Data Flow (DDF).

Every endpoint, data structure, authentication handshake, cryptographic payload signature, and synchronization mechanism described herein is designed to comply with **ISO 14155:2020** (Clinical investigation of medical devices for human subjects — Good clinical practice) and **21 CFR Part 11** (Electronic Records; Electronic Signatures). This document guarantees data integrity, auditability, trace-to-source traceability, and schema completeness across all external system borders and internal microservice bounds.

---

## 2. Architectural Paradigm & System Boundaries

The Cadence Clinical platform is structured as an API-first, service-oriented architecture. The system exposes its features via a central **API Gateway** acting as an reverse proxy, OAuth 2.0 / OIDC Policy Enforcement Point (PEP), rate limiter, and security boundary.

```
                         ┌─────────────────────────────────────────┐
                         │      External Consumer / UI Clients     │
                         └────────────────────┬────────────────────┘
                                              │
                                              ▼ (OAuth 2.0 / JWT)
                         ┌─────────────────────────────────────────┐
                         │            Central API Gateway          │
                         └────────────────────┬────────────────────┘
                                              │
                ┌─────────────────────────────┼─────────────────────────────┐
                │ (Internal HMAC + JWT Headers)                             │ (Internal HMAC + JWT Headers)
                ▼                                                           ▼
    ┌──────────────────────┐                                    ┌──────────────────────┐
    │     MDR Designer     │                                    │    EDC Execution     │
    │  (Neo4j Graph Core)  │                                    │  (PostgreSQL Store)  │
    └──────────────────────┘                                    └──────────────────────┘
```

The primary services are:

1. **MDR Designer Service (`apps/designer`)**: Operates on a Neo4j graph database. Manages CDISC USDM studies, activities, visits, Biomedical Concepts (BCs), standards governance, and concept mappings.
2. **EDC Execution Service (`apps/execution`)**: Operates on a PostgreSQL relational engine. Manages subject state transitions, eCRF data capture (ODM/CDASH structure), queries, translation workflows, and the GxP-compliant audit trail.

---

## 3. Core REST & GraphQL API Design

### 3.1 Authentication Standards & JWT Verification

Cadence Clinical enforces **OAuth 2.0** with **OpenID Connect (OIDC)** as the universal standard for authentication and access control. **Keycloak** acts as the central Identity Provider (IdP).

#### 3.1.1 Gateway Signature Handshake

All requests entering the platform must present a `Bearer` token in the `Authorization` header. The API Gateway intercepts this token, validates it against Keycloak's JSON Web Key Set (JWKS), extracts the user's identities and roles, and propagates them downstream using cryptographically signed headers.

The Gateway appends four crucial security headers to the downstream request:

- `X-User-Id`: The unique user identifier (`sub` claim).
- `X-User-Roles`: A comma-separated list of roles assigned to the user.
- `X-Gateway-Timestamp`: The exact UNIX timestamp of the request verification.
- `X-Gateway-Signature`: An **HMAC-SHA256** signature generated using a shared secret.

The downstream services compute the signature locally to verify that the request originated from the gateway:
$$\text{Signature} = \text{HMAC-SHA256}(\text{GATEWAY\_SECRET}, \text{X-User-Id} \parallel \text{":"} \parallel \text{X-User-Roles} \parallel \text{":"} \parallel \text{X-Gateway-Timestamp})$$

If the signature computed matches `X-Gateway-Signature`, and the timestamp is within $\pm 5$ seconds, the request is trusted as authenticated.

#### 3.1.2 JWT Token Structure

A valid JWT token contains the following standard and custom claims:

```json
{
  "iss": "https://auth.ccrsoft.com/realms/cadence",
  "sub": "usr_9921a88b2c410",
  "aud": "cadence-api-gateway",
  "exp": 1782035200,
  "nbf": 1782031600,
  "iat": 1782031600,
  "jti": "jwt_ef881029cbaef9901",
  "name": "Dr. Sarah Jenkins",
  "email": "s.jenkins@ccrsoft.com",
  "realm_access": {
    "roles": ["STUDY_DESIGNER", "TERMINOLOGY_MANAGER", "PRINCIPAL_INVESTIGATOR"]
  }
}
```

### 3.2 Rate Limiting Architecture

To prevent Denial of Service (DoS) attacks and ensure resource fairness, Cadence Clinical implements a distributed **Token Bucket / Sliding Window Rate Limiting** algorithm.

- **Default Limit**: 500 requests per sliding window of 60 seconds per IP address / authenticated user ID.
- **MDR Search & Bulk Endpoints**: 100 requests per sliding window of 60 seconds.
- **Dictionary Coding Endpoints**: 1,000 requests per sliding window of 60 seconds (optimized for parallel workflows).

#### 3.2.1 Rate Limiting Headers

Every response emitted by the gateway includes the following headers detailing rate-limit states:

- `X-RateLimit-Limit`: The maximum number of allowed requests in the current sliding window.
- `X-RateLimit-Remaining`: The remaining number of requests allowed for the current window.
- `X-RateLimit-Reset`: The UNIX epoch timestamp indicating when the current window resets.

#### 3.2.2 HTTP 429 Too Many Requests Payload

When limits are exceeded, the API Gateway immediately rejects the request with an HTTP status code `429` and the following payload structure:

```json
{
  "type": "https://api.ccrsoft.com/errors/too-many-requests",
  "title": "Rate Limit Exceeded",
  "status": 429,
  "detail": "The request limit of 500 requests per 60 seconds has been exceeded. Please retry after 14 seconds.",
  "instance": "/api/v1/mdr/concepts/search?q=heart",
  "retry_after_seconds": 14,
  "code": "RATE_LIMIT_EXCEEDED"
}
```

### 3.3 Standardized Error Handling (RFC 7807)

All errors returned by the Cadence Clinical API are modeled on **RFC 7807 (Problem Details for HTTP APIs)**, guaranteeing machine-readable semantic error structures across all services.

#### 3.3.1 Error Schema Properties

- `type` (string): A URI reference identifying the problem type.
- `title` (string): A short, human-readable summary of the problem type.
- `status` (integer): The HTTP status code.
- `detail` (string): A detailed explanation of this specific error instance.
- `instance` (string): A URI reference for the specific resource path.
- `code` (string): A stable, unique platform error code (e.g., `STUDY_LOCKED`, `INVALID_CONCEPT_CODE`).
- `invalid_params` (array, optional): A list of fields that failed validation (useful for `400 Bad Request`).

#### 3.3.2 Example Error Payload: Validation Failure (HTTP 400)

```json
{
  "type": "https://api.ccrsoft.com/errors/validation-failed",
  "title": "Request Validation Failed",
  "status": 400,
  "detail": "The request body fails to satisfy schema rules. Refer to 'invalid_params' for details.",
  "instance": "/api/v1/mdr/concepts",
  "code": "REQUEST_VALIDATION_ERROR",
  "invalid_params": [
    {
      "field": "concept_code",
      "reason": "The concept code must follow SNOMED-CT syntax: numeric identifier of 6 to 18 digits.",
      "value": "invalid_abc123"
    },
    {
      "field": "terminology",
      "reason": "The terminology must be one of: 'SNOMED-CT', 'LOINC', 'MedDRA', 'WHODrug', 'NCI', 'CDISC-CT'.",
      "value": "CUSTOM"
    }
  ]
}
```

### 3.4 Pagination Mechanics

Endpoints returning collections of records support unified pagination mechanisms. Two strategies are offered depending on the endpoint type:

#### 3.4.1 Cursor-Based Pagination (Recommended for Streaming & Real-time Integration)

Required for high-frequency or rapidly changing datasets (e.g., subject events, raw audit log feeds). It avoids the "duplicate-item" anomaly inherent in offset-based indexing during records insertion.

- `limit` (integer, query param): Number of items to return (default: 50, max: 250).
- `starting_after` (string, query param): The cursor (usually a cryptographically encoded ID) defining the starting position.

Response payload standard:

```json
{
  "object": "list",
  "data": [ ... ],
  "has_more": true,
  "next_cursor": "eyJpZCI6ICJjdXNfMDExOWIyIn0="
}
```

#### 3.4.2 Offset-Based Pagination (Fallback for Static Reference Data)

Commonly used for static terminology tables or lookup configurations.

- `offset` (integer, query param): Starting zero-based index.
- `limit` (integer, query param): Number of records.

Headers returned:
`Link: <https://api.ccrsoft.com/api/v1/mdr/concepts?offset=100&limit=50>; rel="next", <https://api.ccrsoft.com/api/v1/mdr/concepts?offset=0&limit=50>; rel="prev"`

### 3.5 Payload Compression & Wire Formats

To maximize throughput and comply with data-transmission efficiency goals, all endpoints support payload compression.

- **Accepted Compression Protocols**: `gzip`, `deflate`, `br` (Brotli).
- **Requirements**: Clients should specify `Accept-Encoding: br, gzip` in headers. Responses with payloads larger than 2 KB will automatically be compressed and issued with a corresponding `Content-Encoding: br` header.
- **Standard Content Type**: `application/json; charset=utf-8` or `application/fhir+json; charset=utf-8` for semantic data.

---

## 4. Metadata & MDR Endpoints

The Metadata Repository (MDR) serves as the source of truth for Clinical Biomedical Concepts (BCs), standards governance, study elements, and terminology alignments. The endpoints operate within the `apps/designer` context.

### 4.1 Biomedical Concepts (BCs) Contract

A **Biomedical Concept** is a formal, granular semantic building block representing a unit of clinical observation or collection (e.g., Systolic Blood Pressure, Patient Demographics).

#### 4.1.1 GET /api/v1/mdr/concepts

Fetches a paginated list of Biomedical Concepts.

**Query Parameters**:

- `terminology` (string): Filter by terminology system (e.g., `SNOMED-CT`, `LOINC`).
- `domain` (string): Filter by CDASH domain (e.g., `VS`, `LB`, `DM`).
- `limit` (int): Number of items (default 50).
- `starting_after` (string): Cursor identifier.

**Response (HTTP 200)**:

```json
{
  "object": "list",
  "data": [
    {
      "id": "bc_sys_bp_001",
      "concept_code": "271649006",
      "terminology": "SNOMED-CT",
      "display_name": "Systolic blood pressure",
      "definition": "The pressure exerted by circulating blood upon the walls of blood vessels when the heart ventricles contract.",
      "cdash_mapping": {
        "domain": "VS",
        "variable_name": "VSSBP",
        "data_type": "NUMERIC"
      },
      "allowable_units": [
        {
          "ucum_code": "mm[Hg]",
          "name": "millimeter of mercury"
        }
      ],
      "version": "1.0.0",
      "status": "APPROVED",
      "created_at": "2026-01-15T08:00:00Z",
      "created_by": "usr_9921a88b2c410"
    }
  ],
  "has_more": false,
  "next_cursor": null
}
```

#### 4.1.2 POST /api/v1/mdr/concepts

Creates a new Biomedical Concept. Requires the role `STUDY_DESIGNER` or `TERMINOLOGY_MANAGER`.

**Request Body**:

```json
{
  "concept_code": "364075005",
  "terminology": "SNOMED-CT",
  "display_name": "Heart rate",
  "definition": "The frequency of the heartbeat measured by the number of contractions of the ventricles per minute.",
  "cdash_mapping": {
    "domain": "VS",
    "variable_name": "VSHR",
    "data_type": "NUMERIC"
  },
  "allowable_units": [
    {
      "ucum_code": "/min",
      "name": "beats per minute"
    }
  ],
  "change_reason": "Required for Cardiovascular clinical study profile"
}
```

**Response (HTTP 211 Created)**:

```json
{
  "id": "bc_heart_rate_002",
  "concept_code": "364075005",
  "terminology": "SNOMED-CT",
  "display_name": "Heart rate",
  "definition": "The frequency of the heartbeat measured by the number of contractions of the ventricles per minute.",
  "cdash_mapping": {
    "domain": "VS",
    "variable_name": "VSHR",
    "data_type": "NUMERIC"
  },
  "allowable_units": [
    {
      "ucum_code": "/min",
      "name": "beats per minute"
    }
  ],
  "version": "1.0.0",
  "status": "DRAFT",
  "created_at": "2026-07-22T20:30:00Z",
  "created_by": "usr_9921a88b2c410"
}
```

#### 4.1.3 `PUT /api/v1/mdr/concepts/{id}`

Updates an existing Biomedical Concept, incrementing its version index. Requires standard 21 CFR Part 11 parameters (`reason_for_change`).

**Request Body**:

```json
{
  "display_name": "Heart rate (resting)",
  "definition": "The frequency of the heart rate at complete rest.",
  "cdash_mapping": {
    "domain": "VS",
    "variable_name": "VSRESTR",
    "data_type": "NUMERIC"
  },
  "allowable_units": [
    {
      "ucum_code": "/min",
      "name": "beats per minute"
    }
  ],
  "reason_for_change": "Refined domain scope to capture resting heart rate explicitly."
}
```

**Response (HTTP 200)**:

```json
{
  "id": "bc_heart_rate_002",
  "concept_code": "364075005",
  "terminology": "SNOMED-CT",
  "display_name": "Heart rate (resting)",
  "definition": "The frequency of the heart rate at complete rest.",
  "cdash_mapping": {
    "domain": "VS",
    "variable_name": "VSRESTR",
    "data_type": "NUMERIC"
  },
  "allowable_units": [
    {
      "ucum_code": "/min",
      "name": "beats per minute"
    }
  ],
  "version": "1.1.0",
  "status": "APPROVED",
  "created_at": "2026-07-22T20:30:00Z",
  "updated_at": "2026-07-22T20:35:00Z",
  "updated_by": "usr_9921a88b2c410",
  "reason_for_change": "Refined domain scope to capture resting heart rate explicitly."
}
```

### 4.2 Standards Governance & USDM Integration

The Cadence Clinical MDR enforces CDISC USDM (Unified Study Definitions Model) alignment. A study design graph constructed in `apps/designer` consists of studies, study elements, workflow steps, arms, epochs, visits, and activities.

#### 4.2.1 `GET /api/v1/mdr/studies/{study_id}/usdm`

Extracts the fully resolved, CDISC USDM JSON-compliant representation of a study.

**Response (HTTP 200)**:

```json
{
  "id": "std_cadence_001",
  "name": "An Open-Label Study Evaluating Cadence-01 Efficacy",
  "protocol": {
    "id": "prt_cadence_001",
    "version": "1.0",
    "status": "APPROVED",
    "document_url": "https://clinical.cadence.com/protocols/prt_cadence_001.pdf"
  },
  "study_arms": [
    {
      "id": "arm_active",
      "name": "Active Treatment Arm",
      "description": "Subjects receive Cadence-01 active compound.",
      "type": "TREATMENT"
    },
    {
      "id": "arm_placebo",
      "name": "Placebo Control Arm",
      "description": "Subjects receive matching placebo.",
      "type": "PLACEBO"
    }
  ],
  "study_epochs": [
    {
      "id": "ep_screening",
      "name": "Screening Epoch",
      "sequence_order": 1
    },
    {
      "id": "ep_treatment",
      "name": "Treatment Epoch",
      "sequence_order": 2
    }
  ],
  "study_elements": [
    {
      "id": "el_screening_v1",
      "name": "Informed Consent & Eligibility Check",
      "biomedical_concepts": ["bc_sys_bp_001", "bc_heart_rate_002"]
    }
  ]
}
```

### 4.3 Concept Search API (Query Engine)

The search endpoint query parser allows complex, multi-vocabulary lookups.

#### 4.3.1 GET /api/v1/mdr/search

Searches across loaded clinical metadata vocabularies.

**Query Parameters**:

- `q` (string, required): The search string (supports prefix matches and partial tokens).
- `terminology` (string): Restrict search to `MedDRA`, `SNOMED`, `LOINC`, `WHODrug`.
- `concept_class` (string): Restrict by term type (e.g., `LLT`, `PT`, `Observation`).

**Response (HTTP 200)**:

```json
{
  "query": "arterial pressure",
  "total_hits": 2,
  "results": [
    {
      "concept_code": "75367002",
      "terminology": "SNOMED-CT",
      "display_name": "Blood pressure monitoring, invasive",
      "match_score": 0.94,
      "attributes": {
        "concept_class": "Procedure",
        "semantic_type": "Diagnostic Procedure"
      }
    },
    {
      "concept_code": "10022714",
      "terminology": "MedDRA",
      "display_name": "Arterial blood pressure abnormal",
      "match_score": 0.89,
      "attributes": {
        "concept_class": "PT",
        "soc": "Investigations"
      }
    }
  ]
}
```

### 4.4 Rules Engine Authoring & Validation

The Rules Engine endpoints manage the creation, retrieval, updates, soft-deletion, and preview validation of version-controlled skip logic, constraint check, and cross-form clinical edit check rules.

#### 4.4.1 `GET /api/v1/studies/{study_id}/rules`

Fetches all non-soft-deleted active rules associated with a clinical study.

**Response (HTTP 200)**:

```json
[
  {
    "id": "rule_vssbp_skip",
    "study_id": "study_1",
    "type": "skip_logic",
    "condition": {
      "type": "comparison",
      "operator": "==",
      "operands": [
        {
          "type": "field_ref",
          "field_ref": {
            "field_id": "VSPERF"
          }
        },
        {
          "type": "constant",
          "value": "N"
        }
      ]
    },
    "action": "hide",
    "target_field": "VSSBP",
    "version_index": 1,
    "is_deleted": false
  }
]
```

#### 4.4.2 `POST /api/v1/studies/{study_id}/rules`

Creates a new rule under the specified clinical study.

**Request Body**:

```json
{
  "type": "constraint",
  "condition": {
    "type": "comparison",
    "operator": "<=",
    "operands": [
      {
        "type": "field_ref",
        "field_ref": {
          "field_id": "VSSBP"
        }
      },
      {
        "type": "constant",
        "value": 250
      }
    ]
  },
  "target_field": "VSSBP",
  "query_message": "Systolic Blood Pressure {value} is out of bounds (max 250 mmHg)."
}
```

**Response (HTTP 201 Created)**:

```json
{
  "id": "rule_vssbp_constraint",
  "study_id": "study_1",
  "type": "constraint",
  "condition": {
    "type": "comparison",
    "operator": "<=",
    "operands": [
      {
        "type": "field_ref",
        "field_ref": {
          "field_id": "VSSBP"
        }
      },
      {
        "type": "constant",
        "value": 250
      }
    ]
  },
  "target_field": "VSSBP",
  "query_message": "Systolic Blood Pressure {value} is out of bounds (max 250 mmHg).",
  "version_index": 1,
  "is_deleted": false
}
```

#### 4.4.3 `POST /api/v1/studies/{study_id}/rules/preview`

Compiles and validates a rule expression, returning the compiled XPath representation and identifying unknown fields or circular skip-logic dependencies.

**Request Body**:

```json
{
  "type": "skip_logic",
  "condition": {
    "type": "comparison",
    "operator": "==",
    "operands": [
      {
        "type": "field_ref",
        "field_ref": {
          "field_id": "VSPERF_INVALID"
        }
      },
      {
        "type": "constant",
        "value": "N"
      }
    ]
  },
  "action": "hide",
  "target_field": "VSSBP"
}
```

**Response (HTTP 200)**:

```json
{
  "xpath": "(/clinical_data/VSPERF_INVALID = 'N')",
  "failures": ["Unknown field reference: 'VSPERF_INVALID'"],
  "circular_cycles": []
}
```

#### 4.4.4 `POST /api/v1/studies/{study_id}/rules/validate`

Compiles and validates a rule expression, returning the compiled XPath representation and identifying unknown fields or circular skip-logic dependencies. This endpoint acts as the primary validation service for live feedback.

**Request Body**:

```json
{
  "type": "skip_logic",
  "condition": {
    "type": "comparison",
    "operator": "==",
    "operands": [
      {
        "type": "field_ref",
        "field_ref": {
          "field_id": "VSPERF_INVALID"
        }
      },
      {
        "type": "constant",
        "value": "N"
      }
    ]
  },
  "action": "hide",
  "target_field": "VSSBP"
}
```

**Response (HTTP 200)**:

```json
{
  "xpath": "(/clinical_data/VSPERF_INVALID = 'N')",
  "failures": ["Unknown field reference: 'VSPERF_INVALID'"],
  "circular_cycles": []
}
```

---

## 5. Medical Dictionary Connectors

To capture medical events and analyze drug occurrences reliably, Cadence Clinical supports native bindings and schema-validated synchronization mechanisms with standardized medical dictionaries.

```
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                               Medical Dictionaries Core                                │
├────────────────────────────────────────────────────────────────────────────────────────┤
│  ┌───────────────┐   ┌───────────────┐   ┌───────────────┐   ┌─────────────┐   ┌────────┐  │
│  │    MedDRA     │   │    WHODrug    │   │     LOINC     │   │  SNOMED CT  │   │  UCUM  │  │
│  │ (LLT-PT-HLT)  │   │   (ATC-Drug)  │   │ (Observation) │   │ (Ontology)  │   │ (Units)│  │
│  └───────┬───────┘   └───────┬───────┘   └───────┬───────┘   └──────┬──────┘   └───┬────┘  │
└──────────┼───────────────────┼───────────────────┼──────────────────┼──────────────┼───────┘
           ▼                   ▼                   ▼                  ▼              ▼
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                              Platform Loader & Coding APIs                             │
└────────────────────────────────────────────────────────────────────────────────────────┘
```

### 5.1 Dictionary Loading and Sync Pipelines

Dictionaries are loaded into the system via bulk-load multipart HTTP files. Standard formats include MedDRA ASC files, WHODrug B3 text files, and LOINC CSV releases.

#### 5.1.1 POST /api/v1/dictionaries/import

Imports raw dictionary files. Requires terminal operator role `SYSTEM_ADMIN` or `TERMINOLOGY_MANAGER`.

**Form Parameters**:

- `dictionary_type` (string, required): One of `MEDDRA`, `WHODRUG`, `LOINC`, `SNOMED`.
- `version` (string, required): The dictionary version identifier (e.g., `26.0`, `2024-03`).
- `files` (multipart binary arrays): The compressed raw dictionary package.

**Request Structure (Curl Example)**:

```bash
curl -X POST https://api.ccrsoft.com/api/v1/dictionaries/import \
  -H "Authorization: Bearer <JWT>" \
  -F "dictionary_type=MEDDRA" \
  -F "version=26.0" \
  -F "files=@meddra_26_0_english.zip" \
  -F "parse_multilingual=true"
```

**Response (HTTP 202 Accepted)**:

```json
{
  "job_id": "job_dict_import_889127b",
  "dictionary_type": "MEDDRA",
  "version": "26.0",
  "status": "PROCESSING",
  "started_at": "2026-07-22T20:45:00Z",
  "message": "Validating and parsing MedDRA 26.0 hierarchy. Progress can be monitored via the jobs endpoint.",
  "estimated_duration_seconds": 120
}
```

#### 5.1.2 `GET /api/v1/dictionaries/jobs/{job_id}`

Monitors the import progress.

**Response (HTTP 200)**:

```json
{
  "job_id": "job_dict_import_889127b",
  "status": "COMPLETED",
  "progress_percentage": 100,
  "completed_at": "2026-07-22T20:46:45Z",
  "records_imported": 245100,
  "errors_encountered": 0,
  "summary": "MedDRA 26.0 successfully parsed: 84,102 LLTs, 24,110 PTs, 1,720 HLTs, 341 HLGTs, 27 SOCs verified."
}
```

### 5.2 MedDRA Connector Specifications

MedDRA (Medical Dictionary for Regulatory Activities) is hierarchically organized. The connector must model and parse the 5-tiered structure:

1. Low Level Term (LLT)
2. Preferred Term (PT)
3. High Level Term (HLT)
4. High Level Group Term (HLGT)
5. System Organ Class (SOC)

#### 5.2.1 GET /api/v1/dictionaries/meddra/code

Performs precise coding or interactive auto-complete lookup on adverse events reported in eCRFs.

**Query Parameters**:

- `term` (string, required): Text string captured from trial (e.g., "headache").
- `version` (string): MedDRA version (defaults to active version, e.g. `26.0`).
- `target_level` (string): Terminology level (`LLT` or `PT`).

**Response (HTTP 200)**:

```json
{
  "matches": [
    {
      "llt_code": "10019211",
      "llt_name": "Headache",
      "pt_code": "10019211",
      "pt_name": "Headache",
      "hlt_code": "10019231",
      "hlt_name": "Headaches NEC",
      "hlgt_code": "10029214",
      "hlgt_name": "Headache and facial pain",
      "soc_code": "10029205",
      "soc_name": "Nervous system disorders",
      "primary_soc_flag": "Y",
      "score": 1.0
    },
    {
      "llt_code": "10019218",
      "llt_name": "Headache vascular",
      "pt_code": "10019211",
      "pt_name": "Headache",
      "hlt_code": "10019231",
      "hlt_name": "Headaches NEC",
      "hlgt_code": "10029214",
      "hlgt_name": "Headache and facial pain",
      "soc_code": "10029205",
      "soc_name": "Nervous system disorders",
      "primary_soc_flag": "Y",
      "score": 0.85
    }
  ]
}
```

### 5.3 WHODrug Connector Specifications

WHODrug is organized hierarchically for drug coding. It is parsed to capture Drug Codes, Preferred Names, and ATC (Anatomical Therapeutic Chemical) classifications.

#### 5.3.1 GET /api/v1/dictionaries/whodrug/code

Performs drug coding.

**Query Parameters**:

- `term` (string, required): Concomitant medication text (e.g., "Aspirin").
- `version` (string): WHODrug version identifier.

**Response (HTTP 200)**:
<!-- validation-skip -->

```json
{
  "matches": [
    {
      "drug_code": "00010101001",
      "preferred_name": "ASPIRIN",
      "atc_codes": [
        {
          "code": "N02BA01",
          "description": "acetylsalicylic acid"
        },
        {
          "code": "B01AC06",
          "description": "acetylsalicylic acid"
        }
      ],
      "manufacturer": "BAYER",
      "country": "UNITED STATES",
      "score": 1.0
    }
  ]
}
```

### 5.4 LOINC Connector Specifications

LOINC (Logical Observation Identifiers Names and Codes) provides standard names and codes for identifying laboratory and clinical observations.

#### 5.4.1 GET /api/v1/dictionaries/loinc/lookup

Retrieves a laboratory identifier.

**Query Parameters**:

- `code` (string, required): LOINC code (e.g., `2823-3`).

**Response (HTTP 200)**:
<!-- validation-skip -->

```json
{
  "loinc_num": "2823-3",
  "component": "Potassium",
  "property": "SCnc",
  "time_aspect": "Pt",
  "system": "Ser/Plas",
  "scale_type": "Qn",
  "method_type": "EChm",
  "long_common_name": "Potassium [Moles/volume] in Serum or Plasma",
  "class": "CHEM",
  "status": "ACTIVE"
}
```

### 5.5 SNOMED CT Connector Specifications

SNOMED CT is structured as a rich description-logic ontology representing clinical terms, relationships, and taxonomies.

#### 5.5.1 GET /api/v1/dictionaries/snomed/traverse

Traverses relationship trees in SNOMED CT.

**Query Parameters**:

- `concept_id` (string, required): Root concept ID (e.g., `50960005` - Heart valve structure).
- `relationship_type` (string): Relationship filter (e.g., `IsA`, `Part_Of`).

**Response (HTTP 200)**:

```json
{
  "concept_id": "50960005",
  "display_name": "Heart valve structure",
  "relationships": [
    {
      "type": "IsA",
      "target_concept_id": "312502000",
      "target_display_name": "Structure of cardiovascular system"
    },
    {
      "type": "Part_Of",
      "target_concept_id": "80891009",
      "target_display_name": "Heart structure"
    }
  ]
}
```

### 5.6 UCUM Unit Standardization

Units captured in the EDC must be normalized before clinical database ingestion or standard transformations (e.g., converting Fahrenheit to Celsius, or inches to centimeters). The system integrates the Unified Code for Units of Measure (UCUM).

#### 5.6.1 POST /api/v1/dictionaries/ucum/convert

Standardizes numeric values and verifies scale compatibility between source and target codes.

**Request Body**:
<!-- validation-skip -->

```json skip
{
  "value": 98.6,
  "source_unit": "[degF]",
  "target_unit": "Cel"
}
```

**Response (HTTP 200)**:

```json
{
  "source": {
    "value": 98.6,
    "unit": "[degF]"
  },
  "target": {
    "value": 37.0,
    "unit": "Cel"
  },
  "is_compatible": true,
  "scale_factor": 0.5555555555555556,
  "offset": -17.77777777777778
}
```

---

## 6. Data Exchange Schemas

To fulfill bulk clinical integrations and system synchronizations, Cadence Clinical enforces rigorous, schema-validated bulk structures.

### 6.1 Bulk Dataset Extraction

Extraction of capture data supports clinical formats (CDISC ODM JSON / XML) and standard row-structured JSON/CSV exports.

#### 6.1.1 `GET /api/v1/execution/studies/{study_id}/export`

Exports patient capturing datasets in bulk.

**Query Parameters**:

- `format` (string, required): One of `ODM-XML`, `ODM-JSON`, `CSV-ZIP`.
- `version` (string): Target CDISC ODM standard version (`1.3.2` or `2.0`).

**Response (HTTP 200 - application/xml for ODM-XML)**:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileType="Transactional" FileOID="ODM.CADENCE.001" CreationDateTime="2026-07-22T21:00:00Z" ODMVersion="1.3.2">
  <ClinicalData StudyOID="std_cadence_001" MetaDataVersionOID="MV.001">
    <SubjectData SubjectKey="SUB-101">
      <StudyEventData StudyEventOID="SE.SCREENING">
        <FormData FormOID="F.DEMO" FormVersion="1.0">
          <ItemGroupData ItemGroupOID="IG.DEMO_VALS">
            <ItemData ItemOID="I.AGE" Value="42"/>
            <ItemData ItemOID="I.SEX" Value="F"/>
          </ItemGroupData>
        </FormData>
      </StudyEventData>
    </SubjectData>
  </ClinicalData>
</ODM>
```

### 6.2 21 CFR Part 11 Audit Trail Exports

Every transactional write is logged in a cryptographically sealed relational model. These records can be exported in human-readable and machine-verifiable formats to fulfill regulatory inspection obligations.

#### 6.2.1 `GET /api/v1/execution/studies/{study_id}/audit-trail`

Retrieves the immutable audit trail log.

**Query Parameters**:

- `start_timestamp` (string): ISO 8601 lower bound.
- `end_timestamp` (string): ISO 8601 upper bound.
- `user_id` (string): Filter by acting entity.
- `table_name` (string): Filter by database boundary (e.g., `clinical_records`).

**Response (HTTP 200)**:

```json
{
  "study_id": "std_cadence_001",
  "audit_records": [
    {
      "audit_id": "aud_01b8a992",
      "timestamp": "2026-07-22T20:15:00Z",
      "user_id": "usr_9921a88b2c410",
      "action": "UPDATE",
      "table_name": "clinical_records",
      "record_id": "rec_009187a",
      "change_reason": "Correction of transcribing typographical error.",
      "version_index": 2,
      "old_values": {
        "heart_rate": 62,
        "vital_status": "NORMAL"
      },
      "new_values": {
        "heart_rate": 68,
        "vital_status": "NORMAL"
      },
      "signature_hash": "a1f8c8b21e8e29a8f4c2c1a89b023e42"
    }
  ],
  "verification": {
    "chain_intact": true,
    "last_validated_hash": "a1f8c8b21e8e29a8f4c2c1a89b023e42"
  }
}
```

### 6.3 Cross-System Sync Model (MDR to EDC)

When a study design is completed and finalized (transitioned to `APPROVED` or `PUBLISHED` status) in the MDR Designer, a dynamic migration pipeline transforms the graph definition into operational database tables and eCRF capture templates in the EDC Execution app.

```
MDR DESIGNER                                                   EDC EXECUTION
  [Study Published] ──► Event Triggered ──► Schema Migration ──► [Ready for Capture]
```

#### 6.3.1 Study Published Schema Transition Payload

The synchronization payload represents the structural definition of form structures mapped directly from study design nodes:

```json
{
  "event_id": "evt_sync_991827",
  "timestamp": "2026-07-22T21:10:00Z",
  "study_id": "std_cadence_001",
  "action": "STUDY_PUBLISHED",
  "metadata_version": "1.0",
  "forms": [
    {
      "form_oid": "F.VITAL_SIGNS",
      "name": "Vital Signs eCRF",
      "item_groups": [
        {
          "group_oid": "IG.VS_CORE",
          "name": "Core Vital Signs",
          "items": [
            {
              "item_oid": "I.VS_SBP",
              "name": "Systolic Blood Pressure",
              "data_type": "NUMERIC",
              "mandatory": true,
              "source_biomedical_concept": "bc_sys_bp_001",
              "validation_rule": {
                "min_value": 40,
                "max_value": 250,
                "error_message": "Blood pressure value is outside clinically plausible bounds (40-250 mmHg)."
              }
            }
          ]
        }
      ]
    }
  ]
}
```

This payload is consumed by `apps/execution/translator.py` which dynamically runs database migrations using SQLModel and triggers form rendering engine (OpenRosa/Enketo XForms) compilers.

---

## 7. Complete OpenAPI 3.0 Contract Specification

This section attaches the full OpenAPI 3.0 YAML Contract representing the core integration endpoints of the Cadence Clinical Gateway. It acts as the contract-complete reference for API compilation, client SDK generation, and mock test servers.

```yaml
openapi: 3.1.0
info:
  title: Cadence Clinical Unified Gateway API
  description:
    "Unified microservices API contract for Cadence Clinical Platform.

    Enforces OIDC/Keycloak authentication, RFC 7807 problem details, and ISO 14155:2020 regulatory compliance."
  version: 1.0.0-PROD
servers:
  - url: https://api.ccrsoft.com/api/v1
    description: Production API Gateway
  - url: http://localhost:8000/api/v1
    description: Local Dev Gateway Proxy
paths:
  /api/v1/synopsis/export:
    post:
      tags:
        - Synopsis
      summary: Export Synopsis Document
      description:
        "Export authored clinical protocol synopsis into PDF, DOCX, or HTML format.


        Requirements: PRD-SYS-001"
      operationId: export_synopsis_document_api_v1_synopsis_export_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/SynopsisExportRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SynopsisExportResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/synopsis/render/{study_id}:
    get:
      tags:
        - Synopsis
      summary: Render Synopsis Download
      description:
        "Direct file download endpoint for rendered protocol synopsis documents.


        Requirements: PRD-SYS-001"
      operationId: render_synopsis_download_api_v1_synopsis_render__study_id__get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: format
          in: query
          required: false
          schema:
            type: string
            description: "Export format: pdf, docx, html"
            default: pdf
            title: Format
          description: "Export format: pdf, docx, html"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema: {}
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/designer/sentinel/evaluate:
    post:
      tags:
        - QualitySentinel
      summary: Evaluate Protocol Quality Endpoint
      description:
        "Evaluate authored protocol specification payload against quality and burden rules.


        Requirements: PRD-SYS-001"
      operationId: evaluate_protocol_quality_endpoint_api_v1_designer_sentinel_evaluate_post
      requestBody:
        content:
          application/json:
            schema:
              additionalProperties: true
              type: object
              title: Payload
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/ProtocolQualityScore"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/designer/cascade/propagate:
    post:
      tags:
        - ArtifactCascade
      summary: Propagate Cascade Endpoint
      description:
        "Cascade authored USDM protocol specification changes to downstream eCRFs and SoA matrices.


        Requirements: PRD-SYS-001"
      operationId: propagate_cascade_endpoint_api_v1_designer_cascade_propagate_post
      parameters:
        - name: amendment_version
          in: query
          required: false
          schema:
            type: integer
            default: 1
            title: Amendment Version
      requestBody:
        required: true
        content:
          application/json:
            schema:
              type: object
              additionalProperties: true
              title: Payload
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/CascadeSummaryReport"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/designer/export/m11/{study_id}:
    get:
      tags:
        - ProtocolExport
      summary: Export Protocol M11 Endpoint
      description:
        "Download authored protocol specification in formatted ICH M11 Word (.docx) or USDM JSON format.


        Requirements: PRD-SYS-001"
      operationId: export_protocol_m11_endpoint_api_v1_designer_export_m11__study_id__get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: format
          in: query
          required: false
          schema:
            type: string
            description: "Target export format: docx or json"
            default: docx
            title: Format
          description: "Target export format: docx or json"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema: {}
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/designer/forms/{form_id}/comments:
    get:
      tags:
        - FormComments
      summary: Get Form Comments
      description: Fetch all review comments for a given form.
      operationId: get_form_comments_api_v1_designer_forms__form_id__comments_get
      parameters:
        - name: form_id
          in: path
          required: true
          schema:
            type: string
            title: Form Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: "#/components/schemas/FormReviewCommentResponse"
                title: Response Get Form Comments Api V1 Designer Forms  Form Id  Comments Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    post:
      tags:
        - FormComments
      summary: Post Form Comment
      description: Post a new review comment anchored to a field_id.
      operationId: post_form_comment_api_v1_designer_forms__form_id__comments_post
      parameters:
        - name: form_id
          in: path
          required: true
          schema:
            type: string
            title: Form Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/CommentCreatePayload"
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/FormReviewCommentResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/designer/comments/{comment_id}/resolve:
    patch:
      tags:
        - FormComments
      summary: Resolve Comment
      description: Mark a comment thread/item as resolved and log a GxP audit event.
      operationId: resolve_comment_api_v1_designer_comments__comment_id__resolve_patch
      parameters:
        - name: comment_id
          in: path
          required: true
          schema:
            type: string
            title: Comment Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/FormReviewCommentResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}:
    get:
      summary: Get Legacy Study
      description: "Returns the legacy internal projection with no USDM formatting.\n\nArgs:\n    study_id = get_study_projection(study_id)"
      operationId: get_legacy_study_api_v1_studies__study_id__get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Get Legacy Study Api V1 Studies  Study Id  Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v2/studies/{study_id}/usdm:
    get:
      summary: Get Usdm Study
      description: "Dynamically processes the internal projection and returns a compliant USDM structure.\n\nArgs:\n    study_id (str): The unique identifier of the study.\n    format (str, optional): Output format.\n\nReturns:\n    Any: Mapped USDM data (as dict or serialized Response).\n\nRaises:\n    HTTPException: If the study is not found or validation fails."
      operationId: get_usdm_study_api_v2_studies__study_id__usdm_get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: format
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            description: "Output format: json or yaml"
            title: Format
          description: "Output format: json or yaml"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                title: Response Get Usdm Study Api V2 Studies  Study Id  Usdm Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    post:
      summary: Import Usdm Study
      description:
        "Ingests, validates, maps, and persists a USDM JSON/YAML payload for a specific study.


        Requirements: Phase 2 Ingestion"
      operationId: import_usdm_study_api_v2_studies__study_id__usdm_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: override
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            description: Optional explicit version override ('v2' or 'v3')
            title: Override
          description: Optional explicit version override ('v2' or 'v3')
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema: {}
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/admin/cache/clear:
    post:
      summary: Clear Cache
      description: "Flushes the controlled terminology cache.\n\nReturns:\n    Dict[str, str]: A success message indicating the cache was cleared."
      operationId: clear_cache_api_admin_cache_clear_post
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties:
                  type: string
                type: object
                title: Response Clear Cache Api Admin Cache Clear Post
  /api/admin/cache/status:
    get:
      summary: Cache Status
      description: "Returns the current size and status of the terminology cache.\n\nReturns:\n    Dict[str, int]: The status dictionary containing size and max_size."
      operationId: cache_status_api_admin_cache_status_get
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties:
                  type: integer
                type: object
                title: Response Cache Status Api Admin Cache Status Get
  /api/v1/studies/{study_id}/alignment-validation:
    get:
      summary: Validate Study Alignment
      description: "Generate an alignment validation report for a specific clinical study.\n\nAnalyzes trace links dynamically to ensure the\nStudy Data Requirements (SDR) align with Metadata Requirements (MDR).\n\nArgs:\n    study_id (str): The unique identifier of the study to validate.\n\nReturns:\n    StudyAlignmentReport: The structured validation report."
      operationId: validate_study_alignment_api_v1_studies__study_id__alignment_validation_get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/StudyAlignmentReport"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/terminology-validation:
    get:
      summary: Validate Study Terminology Endpoint
      description: "Generate a terminology validation report for a specific clinical study.\n\nTraverses study concept references and aggregates validation outcomes\nsuch as identifying affected elements and references.\n\nArgs:\n    study_id (str): The unique identifier of the study to validate.\n\nReturns:\n    StudyTerminologyValidationReport: The structured validation report."
      operationId: validate_study_terminology_endpoint_api_v1_studies__study_id__terminology_validation_get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/StudyTerminologyValidationReport"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/ct-validation:
    get:
      summary: Validate Study Ct Endpoint
      description: "Generate a controlled terminology (CT) validation report for a specific clinical study.\n\nTraverses study concept references and aggregates validation outcomes\nsuch as identifying affected elements and references.\n\nArgs:\n    study_id (str): The unique identifier of the study to validate.\n\nReturns:\n    StudyTerminologyValidationReport: The structured validation report."
      operationId: validate_study_ct_endpoint_api_v1_studies__study_id__ct_validation_get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/StudyTerminologyValidationReport"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/terminology/validate/{code}:
    get:
      summary: Validate Single Code
      description: "Validates a single terminology concept code.\n\nArgs:\n    code (str): The concept code to validate.\n\nReturns:\n    ConceptValidationReport: Validation status and metadata."
      operationId: validate_single_code_api_v1_terminology_validate__code__get
      parameters:
        - name: code
          in: path
          required: true
          schema:
            type: string
            title: Code
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/ConceptValidationReport"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/terminology/search:
    get:
      summary: Search Terminology
      description: "Search or autocomplete terminology concepts by text query.\n\nArgs:\n    term (str): Search term.\n    from_record (int, optional): Record offset.\n    page_size (int, optional): Page size.\n    bypass_cache (bool, optional): Whether to bypass reading from cache. Defaults to False.\n    refresh (bool, optional): Whether to refresh the cache. Defaults to False.\n\nReturns:\n    TerminologySearchResponse: Search results and status."
      operationId: search_terminology_api_v1_terminology_search_get
      parameters:
        - name: term
          in: query
          required: true
          schema:
            type: string
            title: Term
        - name: from_record
          in: query
          required: false
          schema:
            anyOf:
              - type: integer
              - type: "null"
            title: From Record
        - name: page_size
          in: query
          required: false
          schema:
            anyOf:
              - type: integer
              - type: "null"
            title: Page Size
        - name: bypass_cache
          in: query
          required: false
          schema:
            type: boolean
            default: false
            title: Bypass Cache
        - name: refresh
          in: query
          required: false
          schema:
            type: boolean
            default: false
            title: Refresh
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/TerminologySearchResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/differences:
    get:
      summary: Study Differences
      description: "Get human-readable field-level differences between two version actions of a study.\n\nThis endpoint uses a decoupled, API-first in-memory diffing architecture. Instead of\nrelying on a direct database connection (which led to 503 errors and tight coupling),\nit fetches full study payloads from an external registry. The comparison logic runs\nentirely in-memory by flattening nested dictionary structures to dynamically identify\nadded, modified, and deleted fields. This ensures high availability and fast execution\nwithout maintaining direct database connections.\n\nArgs:\n    study_id (str): The unique identifier of the study.\n    action_id1 (str): The ID of the first action version.\n    action_id2 (str): The ID of the second action version.\n\nReturns:\n    List[DifferenceResult]: A list of field-level differences."
      operationId: study_differences_api_v1_studies__study_id__differences_get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: action_id1
          in: query
          required: true
          schema:
            type: string
            title: Action Id1
        - name: action_id2
          in: query
          required: true
          schema:
            type: string
            title: Action Id2
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: "#/components/schemas/DifferenceResult"
                title: Response Study Differences Api V1 Studies  Study Id  Differences Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/sections/{section_id}/transition:
    post:
      summary: Transition Section
      operationId: transition_section_api_v1_studies__study_id__sections__section_id__transition_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: section_id
          in: path
          required: true
          schema:
            type: string
            title: Section Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/SectionTransitionRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SectionReviewTransition"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/sections/{section_id}/status:
    get:
      summary: Get Section Review Status
      operationId: get_section_review_status_api_v1_studies__study_id__sections__section_id__status_get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: section_id
          in: path
          required: true
          schema:
            type: string
            title: Section Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema: {}
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/sections/{section_id}/threads:
    post:
      summary: Create Thread Endpoint
      operationId: create_thread_endpoint_api_v1_studies__study_id__sections__section_id__threads_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: section_id
          in: path
          required: true
          schema:
            type: string
            title: Section Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/CommentThreadCreate"
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/CommentThread"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    get:
      summary: Get Threads Endpoint
      operationId: get_threads_endpoint_api_v1_studies__study_id__sections__section_id__threads_get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: section_id
          in: path
          required: true
          schema:
            type: string
            title: Section Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: "#/components/schemas/CommentThread"
                title: Response Get Threads Endpoint Api V1 Studies  Study Id  Sections  Section Id  Threads Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/threads/{thread_id}/comments:
    post:
      summary: Add Comment Endpoint
      operationId: add_comment_endpoint_api_v1_studies__study_id__threads__thread_id__comments_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: thread_id
          in: path
          required: true
          schema:
            type: string
            title: Thread Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/CommentCreate"
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/CommentThread"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/threads/{thread_id}/resolve:
    post:
      summary: Resolve Thread Endpoint
      operationId: resolve_thread_endpoint_api_v1_studies__study_id__threads__thread_id__resolve_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: thread_id
          in: path
          required: true
          schema:
            type: string
            title: Thread Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/CommentThread"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/blocks/{block_id}/suggestions:
    post:
      summary: Create Suggestion Endpoint
      operationId: create_suggestion_endpoint_api_v1_studies__study_id__blocks__block_id__suggestions_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: block_id
          in: path
          required: true
          schema:
            type: string
            title: Block Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/SuggestionCreate"
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/Suggestion"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    get:
      summary: Get Suggestions Endpoint
      operationId: get_suggestions_endpoint_api_v1_studies__study_id__blocks__block_id__suggestions_get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: block_id
          in: path
          required: true
          schema:
            type: string
            title: Block Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: "#/components/schemas/Suggestion"
                title: Response Get Suggestions Endpoint Api V1 Studies  Study Id  Blocks  Block Id  Suggestions Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/suggestions/{suggestion_id}/decision:
    post:
      summary: Decide Suggestion Endpoint
      operationId: decide_suggestion_endpoint_api_v1_studies__study_id__suggestions__suggestion_id__decision_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: suggestion_id
          in: path
          required: true
          schema:
            type: string
            title: Suggestion Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/SuggestionDecisionRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/Suggestion"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/designer/ingestion/upload:
    post:
      summary: Upload Protocol Ingestion
      operationId: upload_protocol_ingestion_api_v1_designer_ingestion_upload_post
      requestBody:
        content:
          multipart/form-data:
            schema:
              $ref: "#/components/schemas/Body_upload_protocol_ingestion_api_v1_designer_ingestion_upload_post"
        required: true
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema: {}
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/designer/ingestion/jobs/{job_id}:
    get:
      summary: Get Ingestion Job Status
      operationId: get_ingestion_job_status_api_v1_designer_ingestion_jobs__job_id__get
      parameters:
        - name: job_id
          in: path
          required: true
          schema:
            type: string
            title: Job Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema: {}
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/designer/ingestion/candidates/{candidate_id}:
    get:
      summary: Get Ingestion Candidate
      operationId: get_ingestion_candidate_api_v1_designer_ingestion_candidates__candidate_id__get
      parameters:
        - name: candidate_id
          in: path
          required: true
          schema:
            type: string
            title: Candidate Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema: {}
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/designer/ingestion/candidates/{candidate_id}/items/{item_id}/transition:
    post:
      summary: Transition Ingestion Item
      operationId: transition_ingestion_item_api_v1_designer_ingestion_candidates__candidate_id__items__item_id__transition_post
      parameters:
        - name: candidate_id
          in: path
          required: true
          schema:
            type: string
            title: Candidate Id
        - name: item_id
          in: path
          required: true
          schema:
            type: string
            title: Item Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/TransitionItemRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema: {}
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/designer/ingestion/candidates/{candidate_id}/promote:
    post:
      summary: Promote Ingestion Candidate
      operationId: promote_ingestion_candidate_api_v1_designer_ingestion_candidates__candidate_id__promote_post
      parameters:
        - name: candidate_id
          in: path
          required: true
          schema:
            type: string
            title: Candidate Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/PromoteRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema: {}
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}:
    delete:
      summary: Retire Arm Endpoint
      operationId: retire_arm_endpoint_api_v1_studies__study_id__versions__version_id__arms__arm_id__delete
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
        - name: arm_id
          in: path
          required: true
          schema:
            type: string
            title: Arm Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoAEntityCreatedResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    get:
      summary: Get Arm Endpoint
      operationId: get_arm_endpoint_api_v1_studies__study_id__versions__version_id__arms__arm_id__get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
        - name: arm_id
          in: path
          required: true
          schema:
            type: string
            title: Arm Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoAEntityDetail"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    put:
      summary: Update Arm Endpoint
      operationId: update_arm_endpoint_api_v1_studies__study_id__versions__version_id__arms__arm_id__put
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
        - name: arm_id
          in: path
          required: true
          schema:
            type: string
            title: Arm Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/UpdateStudyArmRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoAEntityCreatedResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}:
    delete:
      summary: Retire Epoch Endpoint
      operationId: retire_epoch_endpoint_api_v1_studies__study_id__versions__version_id__epochs__epoch_id__delete
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
        - name: epoch_id
          in: path
          required: true
          schema:
            type: string
            title: Epoch Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoAEntityCreatedResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    get:
      summary: Get Epoch Endpoint
      operationId: get_epoch_endpoint_api_v1_studies__study_id__versions__version_id__epochs__epoch_id__get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
        - name: epoch_id
          in: path
          required: true
          schema:
            type: string
            title: Epoch Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoAEntityDetail"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    put:
      summary: Update Epoch Endpoint
      operationId: update_epoch_endpoint_api_v1_studies__study_id__versions__version_id__epochs__epoch_id__put
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
        - name: epoch_id
          in: path
          required: true
          schema:
            type: string
            title: Epoch Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/UpdateEpochRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoAEntityCreatedResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}:
    delete:
      summary: Retire Visit Endpoint
      operationId: retire_visit_endpoint_api_v1_studies__study_id__versions__version_id__visits__visit_id__delete
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
        - name: visit_id
          in: path
          required: true
          schema:
            type: string
            title: Visit Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoAEntityCreatedResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    get:
      summary: Get Visit Endpoint
      operationId: get_visit_endpoint_api_v1_studies__study_id__versions__version_id__visits__visit_id__get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
        - name: visit_id
          in: path
          required: true
          schema:
            type: string
            title: Visit Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoAEntityDetail"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    put:
      summary: Update Visit Endpoint
      operationId: update_visit_endpoint_api_v1_studies__study_id__versions__version_id__visits__visit_id__put
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
        - name: visit_id
          in: path
          required: true
          schema:
            type: string
            title: Visit Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/apps__designer__domain__protocol_authoring__soa__UpdateVisitRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoAEntityCreatedResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}:
    delete:
      summary: Retire Procedure Endpoint
      operationId: retire_procedure_endpoint_api_v1_studies__study_id__versions__version_id__procedures__procedure_id__delete
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
        - name: procedure_id
          in: path
          required: true
          schema:
            type: string
            title: Procedure Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoAEntityCreatedResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    get:
      summary: Get Procedure Endpoint
      operationId: get_procedure_endpoint_api_v1_studies__study_id__versions__version_id__procedures__procedure_id__get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
        - name: procedure_id
          in: path
          required: true
          schema:
            type: string
            title: Procedure Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoAEntityDetail"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    put:
      summary: Update Procedure Endpoint
      operationId: update_procedure_endpoint_api_v1_studies__study_id__versions__version_id__procedures__procedure_id__put
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
        - name: procedure_id
          in: path
          required: true
          schema:
            type: string
            title: Procedure Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/UpdateProcedureRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoAEntityCreatedResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/timing-windows/{timing_id}:
    delete:
      summary: Retire Timing Window Endpoint
      operationId: retire_timing_window_endpoint_api_v1_studies__study_id__versions__version_id__timing_windows__timing_id__delete
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
        - name: timing_id
          in: path
          required: true
          schema:
            type: string
            title: Timing Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoAEntityCreatedResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    get:
      summary: Get Timing Window Endpoint
      operationId: get_timing_window_endpoint_api_v1_studies__study_id__versions__version_id__timing_windows__timing_id__get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
        - name: timing_id
          in: path
          required: true
          schema:
            type: string
            title: Timing Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoAEntityDetail"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    put:
      summary: Update Timing Window Endpoint
      operationId: update_timing_window_endpoint_api_v1_studies__study_id__versions__version_id__timing_windows__timing_id__put
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
        - name: timing_id
          in: path
          required: true
          schema:
            type: string
            title: Timing Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/UpdateTimingWindowRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoAEntityCreatedResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/links/epoch-visit:
    delete:
      summary: Retire Epoch Visit Endpoint
      operationId: retire_epoch_visit_endpoint_api_v1_studies__study_id__versions__version_id__links_epoch_visit_delete
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/LinkEpochVisitRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoALinkResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    post:
      summary: Link Epoch Visit Endpoint
      operationId: link_epoch_visit_endpoint_api_v1_studies__study_id__versions__version_id__links_epoch_visit_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/LinkEpochVisitRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoALinkResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/links/visit-procedure:
    delete:
      summary: Retire Visit Procedure Endpoint
      operationId: retire_visit_procedure_endpoint_api_v1_studies__study_id__versions__version_id__links_visit_procedure_delete
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/LinkVisitProcedureRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoALinkResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    post:
      summary: Link Visit Procedure Endpoint
      operationId: link_visit_procedure_endpoint_api_v1_studies__study_id__versions__version_id__links_visit_procedure_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/LinkVisitProcedureRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoALinkResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/links/timing:
    delete:
      summary: Retire Timing Endpoint
      operationId: retire_timing_endpoint_api_v1_studies__study_id__versions__version_id__links_timing_delete
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/LinkTimingRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoALinkResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    post:
      summary: Link Timing Endpoint
      operationId: link_timing_endpoint_api_v1_studies__study_id__versions__version_id__links_timing_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/LinkTimingRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoALinkResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/links/arm-applicability:
    delete:
      summary: Retire Arm Applicability Endpoint
      operationId: retire_arm_applicability_endpoint_api_v1_studies__study_id__versions__version_id__links_arm_applicability_delete
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/LinkArmApplicabilityRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoALinkResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    post:
      summary: Link Arm Applicability Endpoint
      operationId: link_arm_applicability_endpoint_api_v1_studies__study_id__versions__version_id__links_arm_applicability_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/LinkArmApplicabilityRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoALinkResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/export:
    get:
      summary: Export Protocol
      description:
        "Assembles a study version's data, maps it to a canonical USDM content model,

        and renders the resulting clinical protocol document as a structurally valid

        PDF or DOCX document using shared layout templates."
      operationId: export_protocol_api_v1_studies__study_id__export_get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: format
          in: query
          required: false
          schema:
            type: string
            default: pdf
            title: Format
        - name: output
          in: query
          required: false
          schema:
            type: string
            default: combined
            title: Output
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema: {}
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/sign-off:
    post:
      summary: Approve Study Version Endpoint
      description:
        "Approve and cryptographically sign a Metadata Designer clinical protocol version, producing

        a 21 CFR Part 11 compliant persisted signature manifestation, recording immutable Action history,

        and locks the protocol version by transitioning its status to APPROVED.

        Locked statuses (APPROVED, SIGNED) block any subsequent edits via immutability checks across both Neo4j and mock databases.

        Successfully approved protocols are archived as PROTOCOL_SIGNOFF artifacts to the eTMF service asynchronously."
      operationId: approve_study_version_endpoint_api_v1_studies__study_id__versions__version_id__sign_off_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/ApproveProtocolRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Approve Study Version Endpoint Api V1 Studies  Study Id  Versions  Version Id  Sign Off Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/approve:
    post:
      summary: Approve Study Version Endpoint
      description:
        "Approve and cryptographically sign a Metadata Designer clinical protocol version, producing

        a 21 CFR Part 11 compliant persisted signature manifestation, recording immutable Action history,

        and locks the protocol version by transitioning its status to APPROVED.

        Locked statuses (APPROVED, SIGNED) block any subsequent edits via immutability checks across both Neo4j and mock databases.

        Successfully approved protocols are archived as PROTOCOL_SIGNOFF artifacts to the eTMF service asynchronously."
      operationId: approve_study_version_endpoint_api_v1_studies__study_id__versions__version_id__approve_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/ApproveProtocolRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Approve Study Version Endpoint Api V1 Studies  Study Id  Versions  Version Id  Approve Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/eligibility-criteria:
    get:
      summary: List Eligibility Criteria
      description: Retrieves all active eligibility criteria for a specific clinical study.
      operationId: list_eligibility_criteria_api_v1_studies__study_id__eligibility_criteria_get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: "#/components/schemas/EligibilityCriterion"
                title: Response List Eligibility Criteria Api V1 Studies  Study Id  Eligibility Criteria Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    post:
      summary: Create Eligibility Criterion Endpoint
      description: Creates a new eligibility criterion for a specific clinical study, parsing and validating the DSL.
      operationId: create_eligibility_criterion_endpoint_api_v1_studies__study_id__eligibility_criteria_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/CreateEligibilityCriterionRequest"
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/EligibilityCriterion"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/eligibility-criteria/{criterion_id}:
    get:
      summary: Get Eligibility Criterion Detail
      description: Retrieves details for a specific eligibility criterion by ID.
      operationId: get_eligibility_criterion_detail_api_v1_studies__study_id__eligibility_criteria__criterion_id__get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: criterion_id
          in: path
          required: true
          schema:
            type: string
            title: Criterion Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/EligibilityCriterion"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    put:
      summary: Update Eligibility Criterion Endpoint
      description: Updates an eligibility criterion for a specific clinical study, parsing and validating the DSL.
      operationId: update_eligibility_criterion_endpoint_api_v1_studies__study_id__eligibility_criteria__criterion_id__put
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: criterion_id
          in: path
          required: true
          schema:
            type: string
            title: Criterion Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/UpdateEligibilityCriterionRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/EligibilityCriterion"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/mappings/upload:
    post:
      summary: Upload Mapping Csv
      description: "Validates a CSV mapping configuration to ensure target names meet standard W3C XML naming specifications.\n\nRaises:\n    HTTPException: If the CSV format is invalid or if target XML names violate naming rules."
      operationId: upload_mapping_csv_api_v1_mappings_upload_post
      requestBody:
        content:
          multipart/form-data:
            schema:
              $ref: "#/components/schemas/Body_upload_mapping_csv_api_v1_mappings_upload_post"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema: {}
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/designer/usdm/validate:
    post:
      summary: Validate Usdm Endpoint
      description:
        "Validates a USDM JSON or YAML payload, normalizes shape differences, and returns a detailed validation report.

        If the payload is invalid, raises a structured HTTP 422 ProblemDetails response."
      operationId: validate_usdm_endpoint_api_v1_designer_usdm_validate_post
      parameters:
        - name: override
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            description: Optional explicit version override ('v2' or 'v3')
            title: Override
          description: Optional explicit version override ('v2' or 'v3')
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema: {}
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/designer/round-trip:
    post:
      summary: Run Round Trip Endpoint
      description: "Orchestrates USDM\u2192internal\u2192USDM and internal\u2192USDM\u2192internal round trips.\nReturns classification, fidelity details, source format, detected/resolved version, and mapping diagnostics."
      operationId: run_round_trip_endpoint_api_v1_designer_round_trip_post
      requestBody:
        content:
          application/json:
            schema:
              additionalProperties: true
              type: object
              title: Payload
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema: {}
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/mdr/concepts:
    get:
      summary: Get Concepts
      description: Fetches a paginated list of Biomedical Concepts.
      operationId: get_concepts_api_v1_mdr_concepts_get
      parameters:
        - name: terminology
          in: query
          required: false
          schema:
            anyOf:
              - $ref: "#/components/schemas/TerminologyEnum"
              - type: "null"
            title: Terminology
        - name: domain
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            title: Domain
        - name: limit
          in: query
          required: false
          schema:
            type: integer
            maximum: 250
            default: 50
            title: Limit
        - name: starting_after
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            title: Starting After
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/ConceptListResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    post:
      summary: Create Concept
      description: Creates a new Biomedical Concept inside the MDR graph repository.
      operationId: create_concept_api_v1_mdr_concepts_post
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/CreateConceptRequest"
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/ConceptDetail"
        "400":
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/ProblemDetails"
          description: Bad Request
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/mdr/concepts/{id}:
    put:
      summary: Update Concept
      description: Updates an existing concept, creating a new audit history and incrementing version index.
      operationId: update_concept_api_v1_mdr_concepts__id__put
      parameters:
        - name: id
          in: path
          required: true
          schema:
            type: string
            title: Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/UpdateConceptRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/ConceptDetail"
        "400":
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/ProblemDetails"
          description: Bad Request
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    delete:
      summary: Delete Concept
      description: Deletes an existing Biomedical Concept if it is not referenced by an Active-Recruiting study.
      operationId: delete_concept_api_v1_mdr_concepts__id__delete
      parameters:
        - name: id
          in: path
          required: true
          schema:
            type: string
            title: Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Delete Concept Api V1 Mdr Concepts  Id  Delete
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/mdr/concepts/{id}/rename:
    post:
      summary: Rename Concept
      description: Renames an existing Biomedical Concept if it is not referenced by an Active-Recruiting study.
      operationId: rename_concept_api_v1_mdr_concepts__id__rename_post
      parameters:
        - name: id
          in: path
          required: true
          schema:
            type: string
            title: Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/RenameConceptRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/ConceptDetail"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/mdr/library:
    post:
      summary: Create Library Object Endpoint
      description: Creates a new Global Library object under the authenticated sponsor's scope.
      operationId: create_library_object_endpoint_api_v1_mdr_library_post
      requestBody:
        required: true
        content:
          application/json:
            schema:
              oneOf:
                - $ref: "#/components/schemas/CreateFormRequest"
                - $ref: "#/components/schemas/CreateDataElementRequest"
                - $ref: "#/components/schemas/CreateArmRequest"
                - $ref: "#/components/schemas/apps__designer__library__CreateVisitRequest"
              discriminator:
                propertyName: object_type
                mapping:
                  FORM: "#/components/schemas/CreateFormRequest"
                  DATA_ELEMENT: "#/components/schemas/CreateDataElementRequest"
                  ARM: "#/components/schemas/CreateArmRequest"
                  VISIT: "#/components/schemas/apps__designer__library__CreateVisitRequest"
              title: Payload
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                oneOf:
                  - $ref: "#/components/schemas/FormLibraryObjectDetail"
                  - $ref: "#/components/schemas/DataElementLibraryObjectDetail"
                  - $ref: "#/components/schemas/ArmLibraryObjectDetail"
                  - $ref: "#/components/schemas/VisitLibraryObjectDetail"
                discriminator:
                  propertyName: object_type
                  mapping:
                    FORM: "#/components/schemas/FormLibraryObjectDetail"
                    DATA_ELEMENT: "#/components/schemas/DataElementLibraryObjectDetail"
                    ARM: "#/components/schemas/ArmLibraryObjectDetail"
                    VISIT: "#/components/schemas/VisitLibraryObjectDetail"
                title: Response Create Library Object Endpoint Api V1 Mdr Library Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    get:
      summary: List Library Objects Endpoint
      description:
        "Lists latest global library objects under the authenticated sponsor.

        Supports Stripe-style cursor-based pagination."
      operationId: list_library_objects_endpoint_api_v1_mdr_library_get
      parameters:
        - name: object_type
          in: query
          required: false
          schema:
            anyOf:
              - $ref: "#/components/schemas/ObjectType"
              - type: "null"
            title: Object Type
        - name: limit
          in: query
          required: false
          schema:
            type: integer
            maximum: 250
            default: 50
            title: Limit
        - name: starting_after
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            title: Starting After
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/LibraryObjectListResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/mdr/library/{id}:
    get:
      summary: Get Library Object Endpoint
      description: Retrieves the latest version or a specific version of a global library object.
      operationId: get_library_object_endpoint_api_v1_mdr_library__id__get
      parameters:
        - name: id
          in: path
          required: true
          schema:
            type: string
            title: Id
        - name: version
          in: query
          required: false
          schema:
            anyOf:
              - type: integer
              - type: "null"
            title: Version
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                oneOf:
                  - $ref: "#/components/schemas/FormLibraryObjectDetail"
                  - $ref: "#/components/schemas/DataElementLibraryObjectDetail"
                  - $ref: "#/components/schemas/ArmLibraryObjectDetail"
                  - $ref: "#/components/schemas/VisitLibraryObjectDetail"
                discriminator:
                  propertyName: object_type
                  mapping:
                    FORM: "#/components/schemas/FormLibraryObjectDetail"
                    DATA_ELEMENT: "#/components/schemas/DataElementLibraryObjectDetail"
                    ARM: "#/components/schemas/ArmLibraryObjectDetail"
                    VISIT: "#/components/schemas/VisitLibraryObjectDetail"
                title: Response Get Library Object Endpoint Api V1 Mdr Library  Id  Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    put:
      summary: Update Library Object Endpoint
      description: Updates a global library object by creating a new version.
      operationId: update_library_object_endpoint_api_v1_mdr_library__id__put
      parameters:
        - name: id
          in: path
          required: true
          schema:
            type: string
            title: Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              oneOf:
                - $ref: "#/components/schemas/UpdateFormRequest"
                - $ref: "#/components/schemas/UpdateDataElementRequest"
                - $ref: "#/components/schemas/UpdateArmRequest"
                - $ref: "#/components/schemas/apps__designer__library__UpdateVisitRequest"
              discriminator:
                propertyName: object_type
                mapping:
                  FORM: "#/components/schemas/UpdateFormRequest"
                  DATA_ELEMENT: "#/components/schemas/UpdateDataElementRequest"
                  ARM: "#/components/schemas/UpdateArmRequest"
                  VISIT: "#/components/schemas/apps__designer__library__UpdateVisitRequest"
              title: Payload
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                oneOf:
                  - $ref: "#/components/schemas/FormLibraryObjectDetail"
                  - $ref: "#/components/schemas/DataElementLibraryObjectDetail"
                  - $ref: "#/components/schemas/ArmLibraryObjectDetail"
                  - $ref: "#/components/schemas/VisitLibraryObjectDetail"
                discriminator:
                  propertyName: object_type
                  mapping:
                    FORM: "#/components/schemas/FormLibraryObjectDetail"
                    DATA_ELEMENT: "#/components/schemas/DataElementLibraryObjectDetail"
                    ARM: "#/components/schemas/ArmLibraryObjectDetail"
                    VISIT: "#/components/schemas/VisitLibraryObjectDetail"
                title: Response Update Library Object Endpoint Api V1 Mdr Library  Id  Put
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/mdr/library/{id}/amend:
    post:
      summary: Amend Library Object Endpoint
      description: Initiates an amendment on a library object that is in use by creating a successor draft version.
      operationId: amend_library_object_endpoint_api_v1_mdr_library__id__amend_post
      parameters:
        - name: id
          in: path
          required: true
          schema:
            type: string
            title: Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/LibraryObjectAmendRequest"
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                oneOf:
                  - $ref: "#/components/schemas/FormLibraryObjectDetail"
                  - $ref: "#/components/schemas/DataElementLibraryObjectDetail"
                  - $ref: "#/components/schemas/ArmLibraryObjectDetail"
                  - $ref: "#/components/schemas/VisitLibraryObjectDetail"
                discriminator:
                  propertyName: object_type
                  mapping:
                    FORM: "#/components/schemas/FormLibraryObjectDetail"
                    DATA_ELEMENT: "#/components/schemas/DataElementLibraryObjectDetail"
                    ARM: "#/components/schemas/ArmLibraryObjectDetail"
                    VISIT: "#/components/schemas/VisitLibraryObjectDetail"
                title: Response Amend Library Object Endpoint Api V1 Mdr Library  Id  Amend Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/mdr/library/{id}/history:
    get:
      summary: Get Library Object History Endpoint
      description: Retrieves the complete version history of a global library object.
      operationId: get_library_object_history_endpoint_api_v1_mdr_library__id__history_get
      parameters:
        - name: id
          in: path
          required: true
          schema:
            type: string
            title: Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  oneOf:
                    - $ref: "#/components/schemas/FormLibraryObjectDetail"
                    - $ref: "#/components/schemas/DataElementLibraryObjectDetail"
                    - $ref: "#/components/schemas/ArmLibraryObjectDetail"
                    - $ref: "#/components/schemas/VisitLibraryObjectDetail"
                  discriminator:
                    propertyName: object_type
                    mapping:
                      FORM: "#/components/schemas/FormLibraryObjectDetail"
                      DATA_ELEMENT: "#/components/schemas/DataElementLibraryObjectDetail"
                      ARM: "#/components/schemas/ArmLibraryObjectDetail"
                      VISIT: "#/components/schemas/VisitLibraryObjectDetail"
                title: Response Get Library Object History Endpoint Api V1 Mdr Library  Id  History Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/mdr/library/{id}/transition:
    post:
      summary: Transition Library Object Endpoint
      description: "Transitions the lifecycle status of a global library object.

        Enforces a strict role-gated ALLOWED_LIBRARY_TRANSITIONS map."
      operationId: transition_library_object_endpoint_api_v1_mdr_library__id__transition_post
      parameters:
        - name: id
          in: path
          required: true
          schema:
            type: string
            title: Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/LibraryObjectTransitionRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                oneOf:
                  - $ref: "#/components/schemas/FormLibraryObjectDetail"
                  - $ref: "#/components/schemas/DataElementLibraryObjectDetail"
                  - $ref: "#/components/schemas/ArmLibraryObjectDetail"
                  - $ref: "#/components/schemas/VisitLibraryObjectDetail"
                discriminator:
                  propertyName: object_type
                  mapping:
                    FORM: "#/components/schemas/FormLibraryObjectDetail"
                    DATA_ELEMENT: "#/components/schemas/DataElementLibraryObjectDetail"
                    ARM: "#/components/schemas/ArmLibraryObjectDetail"
                    VISIT: "#/components/schemas/VisitLibraryObjectDetail"
                title: Response Transition Library Object Endpoint Api V1 Mdr Library  Id  Transition Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions:
    post:
      summary: Post Study Version
      description: "Establishes a new StudyVersion node under a clinical study.

        Enforces that concurrent creation with duplicate index or tag fails with 409 Conflict."
      operationId: post_study_version_api_v1_studies__study_id__versions_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/CreateStudyVersionRequest"
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Post Study Version Api V1 Studies  Study Id  Versions Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/rules:
    get:
      summary: Get Study Rules
      description: Retrieves all non-soft-deleted active rules for a specific clinical study.
      operationId: get_study_rules_api_v1_studies__study_id__rules_get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  type: object
                  additionalProperties: true
                title: Response Get Study Rules Api V1 Studies  Study Id  Rules Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    post:
      summary: Create Study Rule
      description: Creates a new rule for a clinical study, enforcing auth and X-Change-Reason.
      operationId: create_study_rule_api_v1_studies__study_id__rules_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/CreateRuleRequest"
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Create Study Rule Api V1 Studies  Study Id  Rules Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/rules/{rule_id}:
    get:
      summary: Get Study Rule By Id
      description: Retrieves a specific rule by ID.
      operationId: get_study_rule_by_id_api_v1_studies__study_id__rules__rule_id__get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: rule_id
          in: path
          required: true
          schema:
            type: string
            title: Rule Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Get Study Rule By Id Api V1 Studies  Study Id  Rules  Rule Id  Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    put:
      summary: Update Study Rule By Id
      description: Updates a rule's parameters, incrementing version index.
      operationId: update_study_rule_by_id_api_v1_studies__study_id__rules__rule_id__put
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: rule_id
          in: path
          required: true
          schema:
            type: string
            title: Rule Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/CreateRuleRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Update Study Rule By Id Api V1 Studies  Study Id  Rules  Rule Id  Put
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    delete:
      summary: Delete Study Rule By Id
      description: Soft-deletes a rule, retaining its historical properties in audit.
      operationId: delete_study_rule_by_id_api_v1_studies__study_id__rules__rule_id__delete
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: rule_id
          in: path
          required: true
          schema:
            type: string
            title: Rule Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Delete Study Rule By Id Api V1 Studies  Study Id  Rules  Rule Id  Delete
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/rules/preview:
    post:
      summary: Compile Preview Rule
      description: "Read-only compile and validation preview route.

        Detects unknown field references and circular skip-logic dependencies."
      operationId: compile_preview_rule_api_v1_studies__study_id__rules_preview_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/CreateRuleRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/RulePreviewResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/rules/validate:
    post:
      summary: Compile Validate Rule
      description: "Read-only compile and validation preview route.

        Detects unknown field references and circular skip-logic dependencies."
      operationId: compile_validate_rule_api_v1_studies__study_id__rules_validate_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/CreateRuleRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/RulePreviewResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/designer/protocols/{id}/amend:
    post:
      summary: Amend Protocol
      description:
        "Exposes POST /api/designer/protocols/{id}/amend with 201, new_version, status, and parent_version.

        Creates a transaction-safe DRAFT successor with incremented version index."
      operationId: amend_protocol_api_designer_protocols__id__amend_post
      parameters:
        - name: id
          in: path
          required: true
          schema:
            type: string
            title: Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/ProtocolAmendRequest"
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Amend Protocol Api Designer Protocols  Id  Amend Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/blocks:
    post:
      summary: Create Block Endpoint
      operationId: create_block_endpoint_api_v1_studies__study_id__versions__version_id__blocks_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/CreateBlockRequest"
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/BlockCreatedResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    get:
      summary: List Blocks Endpoint
      operationId: list_blocks_endpoint_api_v1_studies__study_id__versions__version_id__blocks_get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: "#/components/schemas/BlockDetailResponse"
                title: Response List Blocks Endpoint Api V1 Studies  Study Id  Versions  Version Id  Blocks Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/blocks/{block_id}:
    put:
      summary: Update Block Endpoint
      operationId: update_block_endpoint_api_v1_studies__study_id__versions__version_id__blocks__block_id__put
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
        - name: block_id
          in: path
          required: true
          schema:
            type: string
            title: Block Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/UpdateBlockRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/BlockCreatedResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    delete:
      summary: Delete Block Endpoint
      operationId: delete_block_endpoint_api_v1_studies__study_id__versions__version_id__blocks__block_id__delete
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
        - name: block_id
          in: path
          required: true
          schema:
            type: string
            title: Block Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/BlockCreatedResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    get:
      summary: Get Block Endpoint
      operationId: get_block_endpoint_api_v1_studies__study_id__versions__version_id__blocks__block_id__get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
        - name: block_id
          in: path
          required: true
          schema:
            type: string
            title: Block Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/BlockDetailResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/blocks/reorder:
    post:
      summary: Reorder Blocks Endpoint
      operationId: reorder_blocks_endpoint_api_v1_studies__study_id__versions__version_id__blocks_reorder_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/ReorderBlocksRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoALinkResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/arms:
    post:
      summary: Create Arm Endpoint
      operationId: create_arm_endpoint_api_v1_studies__study_id__versions__version_id__arms_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/CreateStudyArmRequest"
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoAEntityCreatedResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    get:
      summary: List Arms Endpoint
      operationId: list_arms_endpoint_api_v1_studies__study_id__versions__version_id__arms_get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: "#/components/schemas/SoAEntityDetail"
                title: Response List Arms Endpoint Api V1 Studies  Study Id  Versions  Version Id  Arms Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/epochs:
    post:
      summary: Create Epoch Endpoint
      operationId: create_epoch_endpoint_api_v1_studies__study_id__versions__version_id__epochs_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/CreateEpochRequest"
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoAEntityCreatedResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    get:
      summary: List Epochs Endpoint
      operationId: list_epochs_endpoint_api_v1_studies__study_id__versions__version_id__epochs_get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: "#/components/schemas/SoAEntityDetail"
                title: Response List Epochs Endpoint Api V1 Studies  Study Id  Versions  Version Id  Epochs Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/visits:
    post:
      summary: Create Visit Endpoint
      operationId: create_visit_endpoint_api_v1_studies__study_id__versions__version_id__visits_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/apps__designer__domain__protocol_authoring__soa__CreateVisitRequest"
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoAEntityCreatedResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    get:
      summary: List Visits Endpoint
      operationId: list_visits_endpoint_api_v1_studies__study_id__versions__version_id__visits_get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: "#/components/schemas/SoAEntityDetail"
                title: Response List Visits Endpoint Api V1 Studies  Study Id  Versions  Version Id  Visits Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/procedures:
    post:
      summary: Create Procedure Endpoint
      operationId: create_procedure_endpoint_api_v1_studies__study_id__versions__version_id__procedures_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/CreateProcedureRequest"
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoAEntityCreatedResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    get:
      summary: List Procedures Endpoint
      operationId: list_procedures_endpoint_api_v1_studies__study_id__versions__version_id__procedures_get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: "#/components/schemas/SoAEntityDetail"
                title: Response List Procedures Endpoint Api V1 Studies  Study Id  Versions  Version Id  Procedures Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/timing-windows:
    post:
      summary: Create Timing Window Endpoint
      operationId: create_timing_window_endpoint_api_v1_studies__study_id__versions__version_id__timing_windows_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/CreateTimingWindowRequest"
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoAEntityCreatedResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    get:
      summary: List Timing Windows Endpoint
      operationId: list_timing_windows_endpoint_api_v1_studies__study_id__versions__version_id__timing_windows_get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: "#/components/schemas/SoAEntityDetail"
                title: Response List Timing Windows Endpoint Api V1 Studies  Study Id  Versions  Version Id  Timing Windows Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/soa-projection:
    get:
      summary: Get Soa Projection Endpoint
      operationId: get_soa_projection_endpoint_api_v1_studies__study_id__versions__version_id__soa_projection_get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SoAMatrixView"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/arms/reorder:
    post:
      summary: Reorder Arms Endpoint
      operationId: reorder_arms_endpoint_api_v1_studies__study_id__versions__version_id__arms_reorder_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/ArmReorderRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Reorder Arms Endpoint Api V1 Studies  Study Id  Versions  Version Id  Arms Reorder Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/epochs/reorder:
    post:
      summary: Reorder Epochs Endpoint
      operationId: reorder_epochs_endpoint_api_v1_studies__study_id__versions__version_id__epochs_reorder_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/EpochReorderRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Reorder Epochs Endpoint Api V1 Studies  Study Id  Versions  Version Id  Epochs Reorder Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/visits/reorder:
    post:
      summary: Reorder Visits Endpoint
      operationId: reorder_visits_endpoint_api_v1_studies__study_id__versions__version_id__visits_reorder_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/VisitReorderRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Reorder Visits Endpoint Api V1 Studies  Study Id  Versions  Version Id  Visits Reorder Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/procedures/reorder:
    post:
      summary: Reorder Procedures Endpoint
      operationId: reorder_procedures_endpoint_api_v1_studies__study_id__versions__version_id__procedures_reorder_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/ProcedureReorderRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Reorder Procedures Endpoint Api V1 Studies  Study Id  Versions  Version Id  Procedures Reorder Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/assignments/activities:
    post:
      summary: Assign Activities To Visit Endpoint
      operationId: assign_activities_to_visit_endpoint_api_v1_studies__study_id__versions__version_id__assignments_activities_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/ActivityAssignmentRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Assign Activities To Visit Endpoint Api V1 Studies  Study Id  Versions  Version Id  Assignments Activities Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/assignments/arms:
    post:
      summary: Assign Visits To Arm Endpoint
      operationId: assign_visits_to_arm_endpoint_api_v1_studies__study_id__versions__version_id__assignments_arms_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/VisitToArmAssignmentRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Assign Visits To Arm Endpoint Api V1 Studies  Study Id  Versions  Version Id  Assignments Arms Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/{version_id}/assignments/epochs:
    post:
      summary: Assign Visits To Epoch Endpoint
      operationId: assign_visits_to_epoch_endpoint_api_v1_studies__study_id__versions__version_id__assignments_epochs_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id
          in: path
          required: true
          schema:
            type: string
            title: Version Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/VisitToEpochAssignmentRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Assign Visits To Epoch Endpoint Api V1 Studies  Study Id  Versions  Version Id  Assignments Epochs Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/versions/diff:
    get:
      summary: Get Versions Diff Endpoint
      description: "Exposes graph-native, form-level version-diff API.

        Identifies additions, modifications, and deletions of forms.

        Returns HTTP 400 Bad Request if either version is nonexistent or unrelated."
      operationId: get_versions_diff_endpoint_api_v1_studies__study_id__versions_diff_get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version_id1
          in: query
          required: true
          schema:
            type: string
            description: The old version ID
            title: Version Id1
          description: The old version ID
        - name: version_id2
          in: query
          required: true
          schema:
            type: string
            description: The new version ID
            title: Version Id2
          description: The new version ID
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/VersionDiffResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/library-instances:
    post:
      summary: Instantiate Library Object Endpoint
      description:
        "Instantiates a specific version (or latest) of a Global Library object into a study-scoped instance.

        Enforces that the library object and study both belong to/are accessible by the authenticated sponsor."
      operationId: instantiate_library_object_endpoint_api_v1_studies__study_id__library_instances_post
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/InstantiateLibraryObjectRequest"
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/LibraryInstanceResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/library-instances/{instance_id}:
    put:
      summary: Update Library Instance Endpoint
      description:
        "Updates the payload of an instantiated library object inside a study.

        Verifies that target study belongs to or is accessible by the authenticated sponsor,

        leaving the global library source immutable."
      operationId: update_library_instance_endpoint_api_v1_studies__study_id__library_instances__instance_id__put
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: instance_id
          in: path
          required: true
          schema:
            type: string
            title: Instance Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/UpdateLibraryInstanceRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/LibraryInstanceResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/studies/{study_id}/library-instances/{instance_id}/diff:
    get:
      summary: Get Library Instance Diff Endpoint
      description: Returns field-level dot-notated differences between the library instance payload and its linked source version.
      operationId: get_library_instance_diff_endpoint_api_v1_studies__study_id__library_instances__instance_id__diff_get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: instance_id
          in: path
          required: true
          schema:
            type: string
            title: Instance Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: "#/components/schemas/DifferenceResult"
                title: Response Get Library Instance Diff Endpoint Api V1 Studies  Study Id  Library Instances  Instance Id  Diff Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /health:
    get:
      summary: Health Check
      description: "Service health check endpoint.\n\nReturns a basic JSON payload indicating the service is operational.\n\nReturns:\n    dict[str, str]: The health status payload."
      operationId: health_check_health_get
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties:
                  type: string
                type: object
                title: Response Health Check Health Get
  /api/v1/execution/locks/lock:
    post:
      tags:
        - DataLock
      summary: Lock Data Endpoint
      description:
        "Execute form, item-group, or field-level data lock or freeze operation.


        Requirements: PRD-SYS-001"
      operationId: lock_data_endpoint_api_v1_execution_locks_lock_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/DataLockRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/DataLockResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/locks/unlock:
    post:
      tags:
        - DataLock
      summary: Unlock Data Endpoint
      description: "Execute GxP data unlock override operation.


        Requirements: PRD-SYS-001"
      operationId: unlock_data_endpoint_api_v1_execution_locks_unlock_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/DataLockRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/DataLockResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/locks/status/{form_id}:
    get:
      tags:
        - DataLock
      summary: Get Form Lock Status Endpoint
      description:
        "Retrieve active data locks for specified eCRF form submission.


        Requirements: PRD-SYS-001"
      operationId: get_form_lock_status_endpoint_api_v1_execution_locks_status__form_id__get
      parameters:
        - name: form_id
          in: path
          required: true
          schema:
            type: string
            title: Form Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: "#/components/schemas/DataLockRecord"
                title: Response Get Form Lock Status Endpoint Api V1 Execution Locks Status  Form Id  Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/signatures/sign:
    post:
      tags:
        - Signatures
      summary: Sign Payload Endpoint
      description:
        "Signs a given string payload using the secure backend PKCS#7 signer.


        Requirements: PRD-SYS-001"
      operationId: sign_payload_endpoint_api_v1_execution_signatures_sign_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/SignRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SignResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/signatures/verify:
    post:
      tags:
        - Signatures
      summary: Verify Signature Endpoint
      description:
        "Verifies a PKCS#7 signed document payload using the ESignatureVerifier and trust store.


        Requirements: PRD-SYS-001"
      operationId: verify_signature_endpoint_api_v1_execution_signatures_verify_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/VerifyRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/VerifyResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/signatures/batch-sign-off:
    post:
      tags:
        - Signatures
      summary: Batch Signature Sign Off Endpoint
      description:
        "Execute 21 CFR Part 11 batch electronic signature casebook sign-off.


        Requirements: PRD-SYS-001"
      operationId: batch_signature_sign_off_endpoint_api_v1_execution_signatures_batch_sign_off_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/BatchSignatureRequest"
        required: true
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/BatchSignatureResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/amendments/publish:
    post:
      tags:
        - Amendments
      summary: Publish Amendment Endpoint
      description:
        "Publish protocol amendment version and compute structural summary of changes.


        Requirements: PRD-SYS-001"
      operationId: publish_amendment_endpoint_api_v1_execution_amendments_publish_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/PublishAmendmentRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/PublishAmendmentResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/amendments/summary/{study_id}/{version}:
    get:
      tags:
        - Amendments
      summary: Get Amendment Summary Endpoint
      description:
        "Export Summary of Changes report for specified study version.


        Requirements: PRD-SYS-001"
      operationId: get_amendment_summary_endpoint_api_v1_execution_amendments_summary__study_id___version__get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: version
          in: path
          required: true
          schema:
            type: string
            title: Version
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Get Amendment Summary Endpoint Api V1 Execution Amendments Summary  Study Id   Version  Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/auditor/token/generate:
    post:
      tags:
        - Auditor
      summary: Generate Auditor Token Endpoint
      description:
        "Generate temporary time-bounded access token for regulatory auditors.


        Requirements: PRD-SYS-001"
      operationId: generate_auditor_token_endpoint_api_v1_execution_auditor_token_generate_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/GenerateAuditorTokenRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties: true
                type: object
                title: Response Generate Auditor Token Endpoint Api V1 Execution Auditor Token Generate Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/auditor/inspect/audit-trail/{study_id}:
    get:
      tags:
        - Auditor
      summary: Inspect Study Audit Trail Endpoint
      description:
        "Expose read-only 21 CFR Part 11 audit trail inspection endpoint for study.


        Requirements: PRD-SYS-001"
      operationId: inspect_study_audit_trail_endpoint_api_v1_execution_auditor_inspect_audit_trail__study_id__get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: limit
          in: query
          required: false
          schema:
            anyOf:
              - type: integer
              - type: "null"
            default: 100
            title: Limit
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Inspect Study Audit Trail Endpoint Api V1 Execution Auditor Inspect Audit Trail  Study Id  Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/safety/dispatch:
    post:
      tags:
        - Safety
      summary: Dispatch Safety Report Endpoint
      description:
        "Dispatch ICH E2B(R3) safety report to external pharmacovigilance gateway.


        Requirements: PRD-SYS-001"
      operationId: dispatch_safety_report_endpoint_api_v1_execution_safety_dispatch_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/SafetyDispatchRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SafetyDispatchResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/safety/reconcile:
    post:
      tags:
        - Safety
      summary: Reconcile Sae Cases Endpoint
      description: "Execute automated EDC AE to Safety ICSR case reconciliation.


        Requirements: PRD-SYS-001"
      operationId: reconcile_sae_cases_endpoint_api_v1_execution_safety_reconcile_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/SAEReconcileRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties: true
                type: object
                title: Response Reconcile Sae Cases Endpoint Api V1 Execution Safety Reconcile Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/eisf/upload:
    post:
      tags:
        - eISF
      summary: Upload Eisf Document Endpoint
      description:
        "Upload eISF regulatory binder document and calculate SHA-256 integrity checksum.


        Requirements: PRD-SYS-001"
      operationId: upload_eisf_document_endpoint_api_v1_execution_eisf_upload_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/UploadEISFDocumentRequest"
        required: true
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/EISFDocumentRecord"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/eisf/binder/{study_id}/{site_id}:
    get:
      tags:
        - eISF
      summary: Get Site Regulatory Binder Endpoint
      description:
        "Retrieve site-isolated regulatory binder documents for specified study and site.


        Requirements: PRD-SYS-001"
      operationId: get_site_regulatory_binder_endpoint_api_v1_execution_eisf_binder__study_id___site_id__get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: site_id
          in: path
          required: true
          schema:
            type: string
            title: Site Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: "#/components/schemas/EISFDocumentRecord"
                title: Response Get Site Regulatory Binder Endpoint Api V1 Execution Eisf Binder  Study Id   Site Id  Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/anonymization/scan-phi:
    post:
      tags:
        - Anonymization
      summary: Scan Phi Endpoint
      description:
        "Scan text payload for Protected Health Information (PHI) identifiers.


        Requirements: PRD-SYS-001"
      operationId: scan_phi_endpoint_api_v1_execution_anonymization_scan_phi_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/PHIScanRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties: true
                type: object
                title: Response Scan Phi Endpoint Api V1 Execution Anonymization Scan Phi Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/anonymization/redact-pdf:
    post:
      tags:
        - Anonymization
      summary: Redact Pdf Endpoint
      description:
        "Apply non-destructive PHI redaction overlays to PDF document.


        Requirements: PRD-SYS-001"
      operationId: redact_pdf_endpoint_api_v1_execution_anonymization_redact_pdf_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/RedactPDFRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties: true
                type: object
                title: Response Redact Pdf Endpoint Api V1 Execution Anonymization Redact Pdf Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/doa/assignment:
    post:
      tags:
        - DOA
      summary: Add Doa Assignment Endpoint
      description:
        "Add site personnel task delegation entry to Delegation of Authority log.


        Requirements: PRD-SYS-001"
      operationId: add_doa_assignment_endpoint_api_v1_execution_doa_assignment_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/AddDOAAssignmentRequest"
        required: true
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/DOAAssignmentRecord"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/doa/sign-off:
    post:
      tags:
        - DOA
      summary: Sign Off Doa Assignment Endpoint
      description:
        "Endorse Delegation of Authority task assignment with Principal Investigator eSignature.


        Requirements: PRD-SYS-001"
      operationId: sign_off_doa_assignment_endpoint_api_v1_execution_doa_sign_off_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/DOASignOffRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/DOAAssignmentRecord"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/doa/log/{study_id}/{site_id}:
    get:
      tags:
        - DOA
      summary: Get Site Doa Log Endpoint
      description: "Retrieve site-isolated Delegation of Authority log entries.


        Requirements: PRD-SYS-001"
      operationId: get_site_doa_log_endpoint_api_v1_execution_doa_log__study_id___site_id__get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
        - name: site_id
          in: path
          required: true
          schema:
            type: string
            title: Site Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: "#/components/schemas/DOAAssignmentRecord"
                title: Response Get Site Doa Log Endpoint Api V1 Execution Doa Log  Study Id   Site Id  Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/doa/delegate:
    post:
      tags:
        - DOA
      summary: Delegate Task Endpoint
      operationId: delegate_task_endpoint_api_v1_execution_doa_delegate_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/DelegateTaskRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/DOADelegationRecordResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/doa/endorse:
    post:
      tags:
        - DOA
      summary: Approve Delegation Endpoint
      operationId: approve_delegation_endpoint_api_v1_execution_doa_endorse_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/ApproveDelegationRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/DOADelegationRecordResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/doa/endorse_task:
    post:
      tags:
        - DOA
      summary: Approve Task Endpoint
      operationId: approve_task_endpoint_api_v1_execution_doa_endorse_task_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/ApproveTaskDelegationRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/DOADelegationRecordResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/doa/revoke:
    post:
      tags:
        - DOA
      summary: Revoke Delegation Endpoint
      operationId: revoke_delegation_endpoint_api_v1_execution_doa_revoke_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/RevokeDelegationRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/DOADelegationRecordResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/doa/staff:
    post:
      tags:
        - DOA
      summary: Create Staff Endpoint
      operationId: create_staff_endpoint_api_v1_execution_doa_staff_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/SiteStaffMemberRequest"
        required: true
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SiteStaffMemberResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/doa/audit-logs:
    get:
      tags:
        - DOA
      summary: Get Audit Logs Endpoint
      operationId: get_audit_logs_endpoint_api_v1_execution_doa_audit_logs_get
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                items:
                  $ref: "#/components/schemas/DOAAuditLogResponse"
                type: array
                title: Response Get Audit Logs Endpoint Api V1 Execution Doa Audit Logs Get
  /api/v1/execution/doa/delegations:
    get:
      tags:
        - DOA
      summary: Get Delegations Endpoint
      operationId: get_delegations_endpoint_api_v1_execution_doa_delegations_get
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                items:
                  $ref: "#/components/schemas/DOADelegationRecordResponse"
                type: array
                title: Response Get Delegations Endpoint Api V1 Execution Doa Delegations Get
  /api/v1/offline/sync-batch:
    post:
      tags:
        - Offline Sync
      summary: Sync Offline Batch
      description:
        "Ingest batch of queued offline eCRF/ePRO deltas idempotently.


        Requirements: PRD-SYS-001"
      operationId: sync_offline_batch_api_v1_offline_sync_batch_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/OfflineBatchSyncRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/OfflineBatchSyncResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/offline/sync:
    post:
      tags:
        - Offline Sync
      summary: Offline Sync Endpoint
      description: "Synchronize queued offline delta transactions.


        Requirements: PRD-SYS-001"
      operationId: offline_sync_endpoint_api_v1_execution_offline_sync_post
      requestBody:
        content:
          application/json:
            schema:
              additionalProperties: true
              type: object
              title: Payload
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties: true
                type: object
                title: Response Offline Sync Endpoint Api V1 Execution Offline Sync Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/documents/upload:
    post:
      tags:
        - Documents
      summary: Upload Document
      description:
        "Upload regulated document, compute SHA-256 hash, and record GxP audit trail.


        Requirements: PRD-SYS-001"
      operationId: upload_document_api_v1_documents_upload_post
      requestBody:
        content:
          multipart/form-data:
            schema:
              $ref: "#/components/schemas/Body_upload_document_api_v1_documents_upload_post"
        required: true
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/DocumentUploadResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/documents/{doc_id}:
    get:
      tags:
        - Documents
      summary: Download Document
      description: "Stream file content with dynamic watermarking.


        Requirements: PRD-SYS-001"
      operationId: download_document_api_v1_documents__doc_id__get
      parameters:
        - name: doc_id
          in: path
          required: true
          schema:
            type: string
            title: Doc Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema: {}
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/documents/{doc_id}/versions:
    get:
      tags:
        - Documents
      summary: List Document Versions
      description: "RETURN complete version history list.


        Requirements: PRD-SYS-001"
      operationId: list_document_versions_api_v1_documents__doc_id__versions_get
      parameters:
        - name: doc_id
          in: path
          required: true
          schema:
            type: string
            title: Doc Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: "#/components/schemas/DocumentMetadataResponse"
                title: Response List Document Versions Api V1 Documents  Doc Id  Versions Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/tsdv/config:
    post:
      tags:
        - SDV/TSDV
      summary: Create Or Update Tsdv Config
      description:
        "CREATE or UPDATE Targeted SDV (TSDV) configuration for a study.


        Restricts config writes to CRA/Data Manager roles WITH GxP change justifications."
      operationId: create_or_update_tsdv_config_api_v1_execution_tsdv_config_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/TSDVConfigCreate"
        required: true
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/TSDVConfigResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/tsdv/config/{study_id}:
    get:
      tags:
        - SDV/TSDV
      summary: Get Tsdv Config
      description: Retrieve existing TSDV configuration for a study.
      operationId: get_tsdv_config_api_v1_execution_tsdv_config__study_id__get
      parameters:
        - name: study_id
          in: path
          required: true
          schema:
            type: string
            title: Study Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/TSDVConfigResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/tsdv/required:
    get:
      tags:
        - SDV/TSDV
      summary: Evaluate Tsdv Rule
      description:
        "Evaluate Targeted SDV (TSDV) requirement for a given context.


        Calculates deterministic sampling decisions and returns component results with an audit explanation."
      operationId: evaluate_tsdv_rule_api_v1_execution_tsdv_required_get
      parameters:
        - name: study_id
          in: query
          required: true
          schema:
            type: string
            title: Study Id
        - name: subject_id
          in: query
          required: true
          schema:
            type: string
            title: Subject Id
        - name: domain
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            title: Domain
        - name: enrollment_index
          in: query
          required: false
          schema:
            anyOf:
              - type: integer
              - type: "null"
            title: Enrollment Index
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/TSDVEvaluationResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/sdv/signoff:
    post:
      tags:
        - SDV/TSDV
      summary: Sdv Signoff
      description: CRA/monitor-gated SDV sign-off endpoint for Field, Page, or Visit scopes.
      operationId: sdv_signoff_api_v1_execution_sdv_signoff_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/SDVSignOffRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SDVSignOffResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/sdv/bulk-sign-off:
    post:
      tags:
        - SDV/TSDV
      summary: Bulk Sdv Signoff
      description: CRA/monitor-gated bulk SDV sign-off endpoint for Field, Page, or Visit scopes.
      operationId: bulk_sdv_signoff_api_v1_execution_sdv_bulk_sign_off_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/BulkSdvSignOffRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/BulkSdvSignOffResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/queries/generate:
    post:
      tags:
        - SDV/TSDV
      summary: Bulk Generate Queries
      description: CRA/monitor-gated bulk query generation endpoint.
      operationId: bulk_generate_queries_api_v1_execution_queries_generate_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/BulkQueryGenerationRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/BulkQueryGenerationResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/subjects/{subject_id}/randomize:
    post:
      tags:
        - Randomization
      summary: Randomize Subject Endpoint
      description: Execute GxP compliant subject randomization allocation and block-index advancement.
      operationId: randomize_subject_endpoint_api_v1_execution_subjects__subject_id__randomize_post
      parameters:
        - name: subject_id
          in: path
          required: true
          schema:
            type: string
            title: Subject Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SubjectRandomizationResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/subjects/{subject_id}/unblind:
    post:
      tags:
        - Unblinding
      summary: Unblind Subject
      description: "Execute an emergency treatment-allocation unblinding for a randomised subject.\n\nThis endpoint implements the GxP / 21 CFR Part 11 compliant emergency\nunblinding workflow: it validates step-up re-authentication, performs\nShamir dual-custody reconstruction of the encrypted allocation, builds a\ncryptographically signed evidence record, writes an immutable audit-log\nentry, and dispatches a critical-priority dashboard notification \u2014 all\nwithin a single atomic database transaction."
      operationId: unblind_subject_api_v1_execution_subjects__subject_id__unblind_post
      parameters:
        - name: subject_id
          in: path
          required: true
          schema:
            type: string
            title: Subject Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/UnblindRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SubjectUnblindResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/queries:
    get:
      tags:
        - Queries
      summary: List Queries
      description: Retrieve a list of clinical queries with optional filtering.
      operationId: list_queries_api_v1_execution_queries_get
      parameters:
        - name: study_id
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            title: Study Id
        - name: subject_id
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            title: Subject Id
        - name: visit_id
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            title: Visit Id
        - name: status
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            title: Status
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: "#/components/schemas/ClinicalQueryResponse"
                title: Response List Queries Api V1 Execution Queries Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    post:
      tags:
        - Queries
      summary: Open Query
      description: Raise a new clinical query on a specific field coordinate.
      operationId: open_query_api_v1_execution_queries_post
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/QueryCreate"
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/ClinicalQueryResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/queries/{query_id}:
    get:
      tags:
        - Queries
      summary: Get Query
      description: Query a single clinical query by ID, returning its full audit history.
      operationId: get_query_api_v1_execution_queries__query_id__get
      parameters:
        - name: query_id
          in: path
          required: true
          schema:
            type: string
            title: Query Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/ClinicalQueryResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    patch:
      tags:
        - Queries
      summary: Update Query State
      description: Transition a query through the designated state sequence and perform role checks.
      operationId: update_query_state_api_v1_execution_queries__query_id__patch
      parameters:
        - name: query_id
          in: path
          required: true
          schema:
            type: string
            title: Query Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/QueryUpdate"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/ClinicalQueryResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/queries/{query_id}/respond:
    post:
      tags:
        - Queries
      summary: Respond Query
      description: Submit an investigator response/answer to an open or reopened clinical query.
      operationId: respond_query_api_v1_execution_queries__query_id__respond_post
      parameters:
        - name: query_id
          in: path
          required: true
          schema:
            type: string
            title: Query Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/QueryRespond"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/ClinicalQueryResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/queries/{query_id}/close:
    post:
      tags:
        - Queries
      summary: Close Query
      description: Close an answered query (resolving the discrepancy loop).
      operationId: close_query_api_v1_execution_queries__query_id__close_post
      parameters:
        - name: query_id
          in: path
          required: true
          schema:
            type: string
            title: Query Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/ClinicalQueryResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/queries/{query_id}/reopen:
    post:
      tags:
        - Queries
      summary: Reopen Query
      description: Reopen an answered or closed clinical query for further clarification.
      operationId: reopen_query_api_v1_execution_queries__query_id__reopen_post
      parameters:
        - name: query_id
          in: path
          required: true
          schema:
            type: string
            title: Query Id
      requestBody:
        content:
          application/json:
            schema:
              anyOf:
                - $ref: "#/components/schemas/QueryReopen"
                - type: "null"
              title: Payload
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/ClinicalQueryResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/queries/{query_id}/cancel:
    post:
      tags:
        - Queries
      summary: Cancel Query
      description: Cancel a clinical query raised in error.
      operationId: cancel_query_api_v1_execution_queries__query_id__cancel_post
      parameters:
        - name: query_id
          in: path
          required: true
          schema:
            type: string
            title: Query Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/QueryCancel"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/ClinicalQueryResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/queries/sync:
    post:
      tags:
        - Queries
      summary: Sync Queries
      description: Synchronize clinical query local ledger blocks to the target database.
      operationId: sync_queries_api_v1_execution_queries_sync_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/SyncRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties: true
                type: object
                title: Response Sync Queries Api V1 Execution Queries Sync Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/dictionaries/import:
    post:
      tags:
        - Dictionaries
      summary: Import Dictionary
      description: Imports raw dictionary files and schedules a background parsing task.
      operationId: import_dictionary_api_v1_dictionaries_import_post
      requestBody:
        content:
          multipart/form-data:
            schema:
              $ref: "#/components/schemas/Body_import_dictionary_api_v1_dictionaries_import_post"
        required: true
      responses:
        "202":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/JobStatusResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/dictionaries/jobs/{job_id}:
    get:
      tags:
        - Dictionaries
      summary: Get Dictionary Import Job
      description: Query the execution status, progress, and import counts of a dictionary import job by ID.
      operationId: get_dictionary_import_job_api_v1_dictionaries_jobs__job_id__get
      parameters:
        - name: job_id
          in: path
          required: true
          schema:
            type: string
            title: Job Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/JobStatusResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/dictionaries/meddra/code:
    get:
      tags:
        - Dictionaries
      summary: Get Meddra Code
      description: Performs coding or interactive auto-complete lookup on adverse events using version-aware matcher.
      operationId: get_meddra_code_api_v1_dictionaries_meddra_code_get
      parameters:
        - name: term
          in: query
          required: true
          schema:
            type: string
            title: Term
        - name: version
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            default: "26.0"
            title: Version
        - name: target_level
          in: query
          required: false
          schema:
            anyOf:
              - $ref: "#/components/schemas/MedDRATargetLevelEnum"
              - type: "null"
            default: LLT
            title: Target Level
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/MedDRACodeLookupResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/dictionaries/whodrug/code:
    get:
      tags:
        - Dictionaries
      summary: Get Whodrug Code
      description: Performs coding or interactive lookup on WHODrug database using version-aware matcher.
      operationId: get_whodrug_code_api_v1_dictionaries_whodrug_code_get
      parameters:
        - name: term
          in: query
          required: true
          schema:
            type: string
            title: Term
        - name: version
          in: query
          required: true
          schema:
            type: string
            title: Version
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/WHODrugCodeLookupResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/dictionaries/ucum/convert:
    post:
      tags:
        - Dictionaries
      summary: Post Ucum Convert
      description: Standardizes numeric values and verifies scale compatibility between source and target codes.
      operationId: post_ucum_convert_api_v1_dictionaries_ucum_convert_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/UCUMConvertRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/UCUMConvertResponse"
        "400":
          description: Bad Request
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/ProblemDetails"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/coding/impact-analysis:
    post:
      tags:
        - Dictionaries
      summary: Post Impact Analysis
      description: Manually triggers up-versioning impact analysis on existing coded assignments.
      operationId: post_impact_analysis_api_v1_execution_coding_impact_analysis_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/ImpactAnalysisRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/ImpactAnalysisResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/coding/assignments:
    get:
      tags:
        - Dictionaries
      summary: List Coding Assignments
      description: Lists and filters medical coding assignments.
      operationId: list_coding_assignments_api_v1_execution_coding_assignments_get
      parameters:
        - name: observation_id
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            title: Observation Id
        - name: status
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            title: Status
        - name: verbatim_text
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            title: Verbatim Text
        - name: dictionary_type
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            title: Dictionary Type
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: "#/components/schemas/CodingAssignmentResponse"
                title: Response List Coding Assignments Api V1 Execution Coding Assignments Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/coding/assignments/{assignment_id}:
    get:
      tags:
        - Dictionaries
      summary: Get Coding Assignment
      description: Retrieves a single medical coding assignment by ID.
      operationId: get_coding_assignment_api_v1_execution_coding_assignments__assignment_id__get
      parameters:
        - name: assignment_id
          in: path
          required: true
          schema:
            type: string
            title: Assignment Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/CodingAssignmentResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/coding/assignments/{assignment_id}/action:
    post:
      tags:
        - Dictionaries
      summary: Process Coding Action
      description: Accepts a suggestion or submits a manual override, persisting results and updating the ledger.
      operationId: process_coding_action_api_v1_execution_coding_assignments__assignment_id__action_post
      parameters:
        - name: assignment_id
          in: path
          required: true
          schema:
            type: string
            title: Assignment Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/CoderActionRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/CodingAssignmentResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/biostat/sdtm/{domain}:
    get:
      tags:
        - CDISC Exports
      summary: Export Sdtm Domain
      description: Exports SDTM domain data (DM, AE, VS, LB, MH, CM) in CDISC Dataset-JSON format.
      operationId: export_sdtm_domain_api_v1_execution_biostat_sdtm__domain__get
      parameters:
        - name: domain
          in: path
          required: true
          schema:
            type: string
            title: Domain
        - name: study_id
          in: query
          required: true
          schema:
            type: string
            description: The unique study identifier
            title: Study Id
          description: The unique study identifier
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Export Sdtm Domain Api V1 Execution Biostat Sdtm  Domain  Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/biostat/adam/{dataset}:
    get:
      tags:
        - CDISC Exports
      summary: Export Adam Dataset
      description: Exports ADaM dataset data (ADSL, ADAE, ADVS) in CDISC Dataset-JSON format.
      operationId: export_adam_dataset_api_v1_execution_biostat_adam__dataset__get
      parameters:
        - name: dataset
          in: path
          required: true
          schema:
            type: string
            title: Dataset
        - name: study_id
          in: query
          required: true
          schema:
            type: string
            description: The unique study identifier
            title: Study Id
          description: The unique study identifier
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Export Adam Dataset Api V1 Execution Biostat Adam  Dataset  Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/biostat/bundle:
    get:
      tags:
        - CDISC Exports
      summary: Export Biostat Bundle
      description: Exports all SDTM domains and ADaM datasets bundled in a single CDISC Dataset-JSON document.
      operationId: export_biostat_bundle_api_v1_execution_biostat_bundle_get
      parameters:
        - name: study_id
          in: query
          required: true
          schema:
            type: string
            description: The unique study identifier
            title: Study Id
          description: The unique study identifier
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Export Biostat Bundle Api V1 Execution Biostat Bundle Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /events/study-published:
    post:
      summary: Study Published
      description: "Ingest study publication events and trigger layout generation asynchronously.\n\nArgs:\n    event (StudyEvent): The incoming study event payload.\n    background_tasks (BackgroundTasks): FastAPI background task manager.\n\nReturns:\n    dict[str, str]: A status message confirming job acceptance."
      operationId: study_published_events_study_published_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/StudyEvent"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties:
                  type: string
                type: object
                title: Response Study Published Events Study Published Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/translation/jobs:
    get:
      summary: List Translation Jobs
      description: Retrieve a list of historical translation jobs.
      operationId: list_translation_jobs_api_v1_execution_translation_jobs_get
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                items:
                  $ref: "#/components/schemas/TranslationJobResponse"
                type: array
                title: Response List Translation Jobs Api V1 Execution Translation Jobs Get
  /api/v1/execution/translation/jobs/{job_id}:
    get:
      summary: Get Translation Job
      description: Query the execution status, output metadata, and error messages of a single translation job by ID.
      operationId: get_translation_job_api_v1_execution_translation_jobs__job_id__get
      parameters:
        - name: job_id
          in: path
          required: true
          schema:
            type: string
            title: Job Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/TranslationJobResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/subjects:
    post:
      summary: Create Subject
      description: Create a new clinical subject pseudonymously.
      operationId: create_subject_api_v1_execution_subjects_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/SubjectCreate"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SubjectResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/subjects/{subject_id}/consents:
    post:
      summary: Record Subject Consent Endpoint
      description: Record/upload a signed informed consent form (ICF) for a subject, clearing any requires_reconsent gate.
      operationId: record_subject_consent_endpoint_api_v1_execution_subjects__subject_id__consents_post
      parameters:
        - name: subject_id
          in: path
          required: true
          schema:
            type: string
            title: Subject Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/SubjectConsentRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SubjectConsentResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/subjects/{subject_id}/consent:
    post:
      summary: Record Subject Consent Endpoint
      description: Record/upload a signed informed consent form (ICF) for a subject, clearing any requires_reconsent gate.
      operationId: record_subject_consent_endpoint_api_v1_execution_subjects__subject_id__consent_post
      parameters:
        - name: subject_id
          in: path
          required: true
          schema:
            type: string
            title: Subject Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/SubjectConsentRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SubjectConsentResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/webhooks/etmf:
    post:
      summary: Etmf Completeness Webhook
      description: Receive and process completeness webhook events from eTMF to update local compliance cache.
      operationId: etmf_completeness_webhook_api_v1_execution_webhooks_etmf_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/ETMFCompletenessWebhookPayload"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties: true
                type: object
                title: Response Etmf Completeness Webhook Api V1 Execution Webhooks Etmf Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/sites/{site_id}/activate:
    post:
      summary: Activate Site Endpoint
      description: Attempt site activation in the Execution Engine, validating compliance from the local cache.
      operationId: activate_site_endpoint_api_v1_execution_sites__site_id__activate_post
      parameters:
        - name: site_id
          in: path
          required: true
          schema:
            type: string
            title: Site Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/SiteActivationRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Activate Site Endpoint Api V1 Execution Sites  Site Id  Activate Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/sites/{site_id}/compliance-status:
    get:
      summary: Get Site Compliance Status
      description: Retrieve site compliance status from the local cache.
      operationId: get_site_compliance_status_api_v1_execution_sites__site_id__compliance_status_get
      parameters:
        - name: site_id
          in: path
          required: true
          schema:
            type: string
            title: Site Id
        - name: study_id
          in: query
          required: true
          schema:
            type: string
            description: The clinical study ID
            title: Study Id
          description: The clinical study ID
        - name: milestone
          in: query
          required: false
          schema:
            type: string
            description: The milestone to check
            default: SITE_ACTIVATION
            title: Milestone
          description: The milestone to check
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/ComplianceStatusResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/subjects/{subject_id}/screening:
    post:
      summary: Evaluate And Transition Screening
      description: Evaluate subject's eligibility criteria and execute the guarded screening lifecycle transition.
      operationId: evaluate_and_transition_screening_api_v1_execution_subjects__subject_id__screening_post
      parameters:
        - name: subject_id
          in: path
          required: true
          schema:
            type: string
            title: Subject Id
      requestBody:
        content:
          application/json:
            schema:
              anyOf:
                - $ref: "#/components/schemas/SubjectScreeningRequest"
                - type: "null"
              title: Payload
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SubjectScreeningResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /subjects/{id}/state:
    patch:
      summary: Update Subject State Endpoint
      operationId: update_subject_state_endpoint_subjects__id__state_patch
      parameters:
        - name: id
          in: path
          required: true
          schema:
            type: string
            title: Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/SubjectStateUpdateRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SubjectResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/subjects/{id}/state:
    patch:
      summary: Update Subject State Endpoint
      operationId: update_subject_state_endpoint_api_v1_execution_subjects__id__state_patch
      parameters:
        - name: id
          in: path
          required: true
          schema:
            type: string
            title: Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/SubjectStateUpdateRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SubjectResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /subjects/{id}/demographics:
    put:
      summary: Update Subject Demographics Endpoint
      operationId: update_subject_demographics_endpoint_subjects__id__demographics_put
      parameters:
        - name: id
          in: path
          required: true
          schema:
            type: string
            title: Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/SubjectDemographicsUpdateRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SubjectResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    delete:
      summary: Delete Subject Demographics Endpoint
      operationId: delete_subject_demographics_endpoint_subjects__id__demographics_delete
      parameters:
        - name: id
          in: path
          required: true
          schema:
            type: string
            title: Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SubjectResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/subjects/{id}/demographics:
    put:
      summary: Update Subject Demographics Endpoint
      operationId: update_subject_demographics_endpoint_api_v1_execution_subjects__id__demographics_put
      parameters:
        - name: id
          in: path
          required: true
          schema:
            type: string
            title: Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/SubjectDemographicsUpdateRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SubjectResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    delete:
      summary: Delete Subject Demographics Endpoint
      operationId: delete_subject_demographics_endpoint_api_v1_execution_subjects__id__demographics_delete
      parameters:
        - name: id
          in: path
          required: true
          schema:
            type: string
            title: Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SubjectResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/visits:
    post:
      summary: Create Visit
      description: Create a new clinical visit.
      operationId: create_visit_api_v1_execution_visits_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/VisitCreate"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/VisitResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/subjects/{subject_id}:
    get:
      summary: Get Subject Detail
      description: Retrieve detailed subject information, applying dynamic blinding redaction & site isolation.
      operationId: get_subject_detail_api_v1_execution_subjects__subject_id__get
      parameters:
        - name: subject_id
          in: path
          required: true
          schema:
            type: string
            title: Subject Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/SubjectDetailResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/visits/{visit_id}:
    get:
      summary: Get Visit Detail
      description: Retrieve detailed visit information, applying dynamic blinding redaction & site isolation.
      operationId: get_visit_detail_api_v1_execution_visits__visit_id__get
      parameters:
        - name: visit_id
          in: path
          required: true
          schema:
            type: string
            title: Visit Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/VisitDetailResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/observations:
    post:
      summary: Create Observation
      description: CREATE a new clinical observation, performing unit normalization and outlier checks.
      operationId: create_observation_api_v1_execution_observations_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/ObservationCreate"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/ObservationResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/unit-conversion:
    post:
      summary: Post Unit Conversion Execution
      description: Translate incoming values using UCUM mapping rules (Execution API).
      operationId: post_unit_conversion_execution_api_v1_execution_unit_conversion_post
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/UnitConversionRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/UnitConversionResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    get:
      summary: Get Unit Conversion Execution
      description: Translate incoming values using UCUM mapping rules via GET (Execution API).
      operationId: get_unit_conversion_execution_api_v1_execution_unit_conversion_get
      parameters:
        - name: value
          in: query
          required: true
          schema:
            type: number
            title: Value
        - name: from_unit
          in: query
          required: true
          schema:
            type: string
            title: From Unit
        - name: to_unit
          in: query
          required: true
          schema:
            type: string
            title: To Unit
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/UnitConversionResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /dictionary/unit-conversion:
    post:
      summary: Post Unit Conversion Dictionary
      description: Translate incoming values using UCUM mapping rules (Dictionary API).
      operationId: post_unit_conversion_dictionary_dictionary_unit_conversion_post
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/UnitConversionRequest"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/UnitConversionResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    get:
      summary: Get Unit Conversion Dictionary
      description: Translate incoming values using UCUM mapping rules via GET (Dictionary API).
      operationId: get_unit_conversion_dictionary_dictionary_unit_conversion_get
      parameters:
        - name: value
          in: query
          required: true
          schema:
            type: number
            title: Value
        - name: from_unit
          in: query
          required: true
          schema:
            type: string
            title: From Unit
        - name: to_unit
          in: query
          required: true
          schema:
            type: string
            title: To Unit
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/UnitConversionResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/outliers/recalculate:
    post:
      summary: Trigger Outlier Recalculation
      description: Trigger cohort-wide outlier recalculation on-demand.
      operationId: trigger_outlier_recalculation_api_v1_execution_outliers_recalculate_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/OutlierRecalculateRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/OutlierRecalculateResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/lab-ranges:
    post:
      summary: Create Lab Range
      description: CREATE a new lab reference range, validating all range invariants.
      operationId: create_lab_range_api_v1_execution_lab_ranges_post
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/LabReferenceRangeCreate"
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/LabReferenceRangeResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    get:
      summary: List Lab Ranges
      description: List and filter reference ranges.
      operationId: list_lab_ranges_api_v1_execution_lab_ranges_get
      parameters:
        - name: study_id
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            title: Study Id
        - name: test_code
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            title: Test Code
        - name: source
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            title: Source
        - name: lab_source
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            title: Lab Source
        - name: include_deleted
          in: query
          required: false
          schema:
            type: boolean
            default: false
            title: Include Deleted
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: "#/components/schemas/LabReferenceRangeResponse"
                title: Response List Lab Ranges Api V1 Execution Lab Ranges Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/lab-ranges/{range_id}:
    get:
      summary: Get Lab Range
      description: Retrieve a single lab reference range.
      operationId: get_lab_range_api_v1_execution_lab_ranges__range_id__get
      parameters:
        - name: range_id
          in: path
          required: true
          schema:
            type: string
            title: Range Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/LabReferenceRangeResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    put:
      summary: Update Lab Range
      description: UPDATE an existing lab reference range, validating all range invariants on the merged state.
      operationId: update_lab_range_api_v1_execution_lab_ranges__range_id__put
      parameters:
        - name: range_id
          in: path
          required: true
          schema:
            type: string
            title: Range Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/LabReferenceRangeUpdate"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/LabReferenceRangeResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    delete:
      summary: Delete Lab Range
      description: Soft-delete a lab reference range by setting is_deleted = True.
      operationId: delete_lab_range_api_v1_execution_lab_ranges__range_id__delete
      parameters:
        - name: range_id
          in: path
          required: true
          schema:
            type: string
            title: Range Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/LabReferenceRangeResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/lab-ranges/recalculate:
    post:
      summary: Trigger Lab Range Recalculation
      description: Trigger cohort-wide reference range evaluation and recalculation on-demand.
      operationId: trigger_lab_range_recalculation_api_v1_execution_lab_ranges_recalculate_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/LabRangeRecalculateRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/LabRangeRecalculateResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/export:
    get:
      summary: Get Cdisc Export Execution
      description: Export stored clinical subject observations in CDISC ODM XML format (Execution API).
      operationId: get_cdisc_export_execution_api_v1_execution_export_get
      parameters:
        - name: study_id
          in: query
          required: true
          schema:
            type: string
            title: Study Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema: {}
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /dictionary/export:
    get:
      summary: Get Cdisc Export Dictionary
      description: Export stored clinical subject observations in CDISC ODM XML format (Dictionary API).
      operationId: get_cdisc_export_dictionary_dictionary_export_get
      parameters:
        - name: study_id
          in: query
          required: true
          schema:
            type: string
            title: Study Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema: {}
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/form-submissions:
    post:
      summary: Create Form Submission
      description: Create a new FormSubmission in DRAFT status.
      operationId: create_form_submission_api_v1_execution_form_submissions_post
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/FormSubmissionCreate"
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/FormSubmissionResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    get:
      summary: List Form Submissions
      description: List form submissions with filters.
      operationId: list_form_submissions_api_v1_execution_form_submissions_get
      parameters:
        - name: study_id
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            title: Study Id
        - name: subject_id
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            title: Subject Id
        - name: visit_id
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            title: Visit Id
        - name: form_id
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            title: Form Id
        - name: include_inactive
          in: query
          required: false
          schema:
            type: boolean
            default: false
            title: Include Inactive
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: "#/components/schemas/FormSubmissionResponse"
                title: Response List Form Submissions Api V1 Execution Form Submissions Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/form-submissions/{submission_id}/complete:
    post:
      summary: Complete Form Submission
      description: Transition a FormSubmission from DRAFT to COMPLETED.
      operationId: complete_form_submission_api_v1_execution_form_submissions__submission_id__complete_post
      parameters:
        - name: submission_id
          in: path
          required: true
          schema:
            type: string
            title: Submission Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/FormSubmissionResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/form-submissions/{submission_id}/approve:
    post:
      summary: Approve Form Submission
      description: PI Approve/Sign-off a completed FormSubmission.
      operationId: approve_form_submission_api_v1_execution_form_submissions__submission_id__approve_post
      parameters:
        - name: submission_id
          in: path
          required: true
          schema:
            type: string
            title: Submission Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/FormSubmissionApprove"
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/FormSubmissionResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/batch-sign-off:
    post:
      summary: Post Batch Sign Off
      description: Perform a PI-only, atomic batch electronic-signature for form-, visit-, and subject-level sign-off.
      operationId: post_batch_sign_off_api_v1_execution_batch_sign_off_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/BatchSignOffRequest"
        required: true
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/BatchSignOffResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/form-submissions/{submission_id}:
    get:
      summary: Get Form Submission
      description: Retrieve a single form submission by ID.
      operationId: get_form_submission_api_v1_execution_form_submissions__submission_id__get
      parameters:
        - name: submission_id
          in: path
          required: true
          schema:
            type: string
            title: Submission Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/FormSubmissionResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/locks:
    get:
      summary: Get Lock Status
      description: Retrieve the current lock/freeze status of sites, visits, forms, subjects, and study-wide trial.
      operationId: get_lock_status_api_v1_execution_locks_get
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/LockStatusResponse"
  /api/v1/execution/locks/site/{site_id}/freeze:
    post:
      summary: Lock Site Endpoint
      description: Locks or freezes a specific site.
      operationId: lock_site_endpoint_api_v1_execution_locks_site__site_id__freeze_post
      parameters:
        - name: site_id
          in: path
          required: true
          schema:
            type: string
            title: Site Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Lock Site Endpoint Api V1 Execution Locks Site  Site Id  Freeze Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/locks/site/{site_id}/lock:
    post:
      summary: Lock Site Endpoint
      description: Locks or freezes a specific site.
      operationId: lock_site_endpoint_api_v1_execution_locks_site__site_id__lock_post
      parameters:
        - name: site_id
          in: path
          required: true
          schema:
            type: string
            title: Site Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Lock Site Endpoint Api V1 Execution Locks Site  Site Id  Lock Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/locks/site/{site_id}/unfreeze:
    post:
      summary: Unlock Site Endpoint
      description: Unlocks or unfreezes a specific site.
      operationId: unlock_site_endpoint_api_v1_execution_locks_site__site_id__unfreeze_post
      parameters:
        - name: site_id
          in: path
          required: true
          schema:
            type: string
            title: Site Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Unlock Site Endpoint Api V1 Execution Locks Site  Site Id  Unfreeze Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/locks/site/{site_id}/unlock:
    post:
      summary: Unlock Site Endpoint
      description: Unlocks or unfreezes a specific site.
      operationId: unlock_site_endpoint_api_v1_execution_locks_site__site_id__unlock_post
      parameters:
        - name: site_id
          in: path
          required: true
          schema:
            type: string
            title: Site Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Unlock Site Endpoint Api V1 Execution Locks Site  Site Id  Unlock Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/locks/visit/{visit_id}/freeze:
    post:
      summary: Lock Visit Endpoint
      description: Locks or freezes a specific visit.
      operationId: lock_visit_endpoint_api_v1_execution_locks_visit__visit_id__freeze_post
      parameters:
        - name: visit_id
          in: path
          required: true
          schema:
            type: string
            title: Visit Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Lock Visit Endpoint Api V1 Execution Locks Visit  Visit Id  Freeze Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/locks/visit/{visit_id}/lock:
    post:
      summary: Lock Visit Endpoint
      description: Locks or freezes a specific visit.
      operationId: lock_visit_endpoint_api_v1_execution_locks_visit__visit_id__lock_post
      parameters:
        - name: visit_id
          in: path
          required: true
          schema:
            type: string
            title: Visit Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Lock Visit Endpoint Api V1 Execution Locks Visit  Visit Id  Lock Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/locks/visit/{visit_id}/unfreeze:
    post:
      summary: Unlock Visit Endpoint
      description: Unlocks or unfreezes a specific visit.
      operationId: unlock_visit_endpoint_api_v1_execution_locks_visit__visit_id__unfreeze_post
      parameters:
        - name: visit_id
          in: path
          required: true
          schema:
            type: string
            title: Visit Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Unlock Visit Endpoint Api V1 Execution Locks Visit  Visit Id  Unfreeze Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/locks/visit/{visit_id}/unlock:
    post:
      summary: Unlock Visit Endpoint
      description: Unlocks or unfreezes a specific visit.
      operationId: unlock_visit_endpoint_api_v1_execution_locks_visit__visit_id__unlock_post
      parameters:
        - name: visit_id
          in: path
          required: true
          schema:
            type: string
            title: Visit Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Unlock Visit Endpoint Api V1 Execution Locks Visit  Visit Id  Unlock Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/locks/form/{form_id}/freeze:
    post:
      summary: Lock Form Endpoint
      description: Locks or freezes a specific form.
      operationId: lock_form_endpoint_api_v1_execution_locks_form__form_id__freeze_post
      parameters:
        - name: form_id
          in: path
          required: true
          schema:
            type: string
            title: Form Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Lock Form Endpoint Api V1 Execution Locks Form  Form Id  Freeze Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/locks/form/{form_id}/lock:
    post:
      summary: Lock Form Endpoint
      description: Locks or freezes a specific form.
      operationId: lock_form_endpoint_api_v1_execution_locks_form__form_id__lock_post
      parameters:
        - name: form_id
          in: path
          required: true
          schema:
            type: string
            title: Form Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Lock Form Endpoint Api V1 Execution Locks Form  Form Id  Lock Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/locks/form/{form_id}/unfreeze:
    post:
      summary: Unlock Form Endpoint
      description: Unlocks or unfreezes a specific form.
      operationId: unlock_form_endpoint_api_v1_execution_locks_form__form_id__unfreeze_post
      parameters:
        - name: form_id
          in: path
          required: true
          schema:
            type: string
            title: Form Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Unlock Form Endpoint Api V1 Execution Locks Form  Form Id  Unfreeze Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/locks/form/{form_id}/unlock:
    post:
      summary: Unlock Form Endpoint
      description: Unlocks or unfreezes a specific form.
      operationId: unlock_form_endpoint_api_v1_execution_locks_form__form_id__unlock_post
      parameters:
        - name: form_id
          in: path
          required: true
          schema:
            type: string
            title: Form Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Unlock Form Endpoint Api V1 Execution Locks Form  Form Id  Unlock Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/locks/subject/{subject_id}/freeze:
    post:
      summary: Lock Subject Endpoint
      description: Locks or freezes a specific subject.
      operationId: lock_subject_endpoint_api_v1_execution_locks_subject__subject_id__freeze_post
      parameters:
        - name: subject_id
          in: path
          required: true
          schema:
            type: string
            title: Subject Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Lock Subject Endpoint Api V1 Execution Locks Subject  Subject Id  Freeze Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/locks/subject/{subject_id}/lock:
    post:
      summary: Lock Subject Endpoint
      description: Locks or freezes a specific subject.
      operationId: lock_subject_endpoint_api_v1_execution_locks_subject__subject_id__lock_post
      parameters:
        - name: subject_id
          in: path
          required: true
          schema:
            type: string
            title: Subject Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Lock Subject Endpoint Api V1 Execution Locks Subject  Subject Id  Lock Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/locks/subject/{subject_id}/unfreeze:
    post:
      summary: Unlock Subject Endpoint
      description: Unlocks or unfreezes a specific subject.
      operationId: unlock_subject_endpoint_api_v1_execution_locks_subject__subject_id__unfreeze_post
      parameters:
        - name: subject_id
          in: path
          required: true
          schema:
            type: string
            title: Subject Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Unlock Subject Endpoint Api V1 Execution Locks Subject  Subject Id  Unfreeze Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/locks/subject/{subject_id}/unlock:
    post:
      summary: Unlock Subject Endpoint
      description: Unlocks or unfreezes a specific subject.
      operationId: unlock_subject_endpoint_api_v1_execution_locks_subject__subject_id__unlock_post
      parameters:
        - name: subject_id
          in: path
          required: true
          schema:
            type: string
            title: Subject Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Unlock Subject Endpoint Api V1 Execution Locks Subject  Subject Id  Unlock Post
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/locks/trial/freeze:
    post:
      summary: Lock Trial Endpoint
      description: Locks or freezes the trial/study.
      operationId: lock_trial_endpoint_api_v1_execution_locks_trial_freeze_post
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties:
                  type: string
                type: object
                title: Response Lock Trial Endpoint Api V1 Execution Locks Trial Freeze Post
  /api/v1/execution/locks/trial/lock:
    post:
      summary: Lock Trial Endpoint
      description: Locks or freezes the trial/study.
      operationId: lock_trial_endpoint_api_v1_execution_locks_trial_lock_post
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties:
                  type: string
                type: object
                title: Response Lock Trial Endpoint Api V1 Execution Locks Trial Lock Post
  /api/v1/execution/locks/trial/unfreeze:
    post:
      summary: Unlock Trial Endpoint
      description: Unlocks or unfreezes the trial/study.
      operationId: unlock_trial_endpoint_api_v1_execution_locks_trial_unfreeze_post
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties:
                  type: string
                type: object
                title: Response Unlock Trial Endpoint Api V1 Execution Locks Trial Unfreeze Post
  /api/v1/execution/locks/trial/unlock:
    post:
      summary: Unlock Trial Endpoint
      description: Unlocks or unfreezes the trial/study.
      operationId: unlock_trial_endpoint_api_v1_execution_locks_trial_unlock_post
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties:
                  type: string
                type: object
                title: Response Unlock Trial Endpoint Api V1 Execution Locks Trial Unlock Post
  /api/v1/execution/rtsm/dispense:
    post:
      summary: Dispense Kit Endpoint
      description:
        "End-point to dispense investigational product (IP) kits against site inventory.


        Checks site locks early, calls dispense_kit_transaction, and handles commits atomically.

        Launches resupply alerts via fastapi background tasks post-commit if triggered."
      operationId: dispense_kit_endpoint_api_v1_execution_rtsm_dispense_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/DispenseRequest"
        required: true
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/DispenseResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/migration-rules:
    post:
      summary: Create Migration Rule
      description: Create a new protocol version migration rule.
      operationId: create_migration_rule_api_v1_execution_migration_rules_post
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: "#/components/schemas/MigrationRuleCreate"
      responses:
        "201":
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/MigrationRuleResponse"
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
    get:
      summary: List Migration Rules
      description: List migration rules for a clinical study.
      operationId: list_migration_rules_api_v1_execution_migration_rules_get
      parameters:
        - name: study_id
          in: query
          required: true
          schema:
            type: string
            title: Study Id
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: "#/components/schemas/MigrationRuleResponse"
                title: Response List Migration Rules Api V1 Execution Migration Rules Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
  /api/v1/execution/audit/integrity:
    get:
      summary: Get Execution Audit Integrity
      description:
        "Verify the GxP clinical execution ledger integrity via block-sealing validation.


        Ensures that chronological audit logs, block-level seals, and sequential chaining

        remain structurally unbroken."
      operationId: get_execution_audit_integrity_api_v1_execution_audit_integrity_get
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties: true
                type: object
                title: Response Get Execution Audit Integrity Api V1 Execution Audit Integrity Get
  /api/v1/admin/outbox:
    get:
      summary: Execution Admin Outbox Endpoint
      operationId: execution_admin_outbox_endpoint_api_v1_admin_outbox_get
      parameters:
        - name: status
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            title: Status
        - name: event_type
          in: query
          required: false
          schema:
            anyOf:
              - type: string
              - type: "null"
            title: Event Type
      responses:
        "200":
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  type: object
                  additionalProperties: true
                title: Response Execution Admin Outbox Endpoint Api V1 Admin Outbox Get
        "422":
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/HTTPValidationError"
components:
  schemas:
    ActivityAssignmentRequest:
      properties:
        visit_id:
          type: string
          minLength: 1
          title: Visit Id
          description: The visit identifier.
        procedure_ids:
          items:
            type: string
          type: array
          title: Procedure Ids
          description: One or more procedure identifiers (non-empty).
        activity_ids:
          items:
            type: string
          type: array
          title: Activity Ids
          description: One or more activity/procedure identifiers (non-empty).
      type: object
      required:
        - visit_id
      title: ActivityAssignmentRequest
      description: Request contract carrying a visit id and one or more procedure/activity ids.
    ActivityReport:
      properties:
        epoch_id:
          anyOf:
            - type: string
            - type: "null"
          title: Epoch Id
        epoch_internal_id:
          type: integer
          title: Epoch Internal Id
        scheduled_event_id:
          anyOf:
            - type: string
            - type: "null"
          title: Scheduled Event Id
        scheduled_event_internal_id:
          type: integer
          title: Scheduled Event Internal Id
        activity_def_id:
          anyOf:
            - type: string
            - type: "null"
          title: Activity Def Id
        activity_def_internal_id:
          type: integer
          title: Activity Def Internal Id
        status:
          type: string
          title: Status
        unmapped_items:
          items:
            $ref: "#/components/schemas/ItemMappingStatus"
          type: array
          title: Unmapped Items
        mapped_items:
          items:
            $ref: "#/components/schemas/ItemMappingStatus"
          type: array
          title: Mapped Items
      type: object
      required:
        - epoch_id
        - epoch_internal_id
        - scheduled_event_id
        - scheduled_event_internal_id
        - activity_def_id
        - activity_def_internal_id
        - status
        - unmapped_items
        - mapped_items
      title: ActivityReport
      description: "Detailed report of an activity definition mapped within an epoch schedule.\n\nAttributes:\n    epoch_id: The public identifier for the study epoch.\n    epoch_internal_id: The internal database ID for the epoch.\n    scheduled_event_id: The public identifier for the scheduled event instance.\n    scheduled_event_internal_id: The internal database ID for the scheduled event instance.\n    activity_def_id: The public identifier for the activity definition.\n    activity_def_internal_id: The internal database ID for the activity definition.\n    status: Mapping status of this activity ('complete', 'incomplete', or 'unmapped').\n    unmapped_items: List of `ItemMappingStatus` for items lacking an operational mapping.\n    mapped_items: List of `ItemMappingStatus` for items successfully mapped to operational nodes."
    AllowableUnit:
      properties:
        ucum_code:
          type: string
          title: Ucum Code
        name:
          type: string
          title: Name
      type: object
      required:
        - ucum_code
        - name
      title: AllowableUnit
    AmendmentImpactReport:
      properties:
        base_version:
          anyOf:
            - type: string
            - type: "null"
          title: Base Version
          description: Parent/base version tag
        amended_version:
          anyOf:
            - type: string
            - type: "null"
          title: Amended Version
          description: Current amended version tag
        added_forms_count:
          type: integer
          title: Added Forms Count
          description: Count of added forms
          default: 0
        modified_forms_count:
          type: integer
          title: Modified Forms Count
          description: Count of modified forms
          default: 0
        deleted_forms_count:
          type: integer
          title: Deleted Forms Count
          description: Count of deleted forms
          default: 0
        estimated_cost_usd:
          type: number
          title: Estimated Cost Usd
          description: Estimated total cost in USD for this amendment
          default: 0.0
        burden_change:
          type: number
          title: Burden Change
          description: Burden index difference from base version
          default: 0.0
        explanation:
          type: string
          title: Explanation
          description: Detailed text explanation of impact and costs
      type: object
      required:
        - explanation
      title: AmendmentImpactReport
      description: Analysis of changes and cost estimates of a study amendment.
    ApproveProtocolRequest:
      properties:
        signing_reason:
          $ref: "#/components/schemas/SigningReason"
          description: Reason for signing
      type: object
      required:
        - signing_reason
      title: ApproveProtocolRequest
    ArmAttributes:
      properties:
        arm_type:
          type: string
          title: Arm Type
          description: The classification of arm (e.g., TREATMENT, PLACEBO).
        target_sample_size:
          type: integer
          title: Target Sample Size
          description: Target number of subjects planned for this arm.
        randomization_ratio:
          type: string
          title: Randomization Ratio
          description: Allocation ratio (e.g., '1:1', '2:1').
      type: object
      required:
        - arm_type
        - target_sample_size
        - randomization_ratio
      title: ArmAttributes
      description: Attributes defining a study arm.
    ArmLibraryObjectDetail:
      properties:
        id:
          type: string
          title: Id
          description: Stable, unique global library ID.
        version:
          type: string
          title: Version
          description: Semantic version of the library object.
        status:
          $ref: "#/components/schemas/LibraryStatus"
          description: Workflow review status of the object.
        sponsor_id:
          type: string
          title: Sponsor Id
          description: Sponsor identifier.
        tenant_id:
          type: string
          title: Tenant Id
          description: Tenant / Partition identifier.
        created_at:
          type: string
          format: date-time
          title: Created At
          description: Audit timestamp of creation.
        created_by:
          type: string
          title: Created By
          description: User ID who created this object.
        updated_at:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Updated At
          description: Audit timestamp of last update.
        updated_by:
          anyOf:
            - type: string
            - type: "null"
          title: Updated By
          description: User ID of last updater.
        reason_for_change:
          anyOf:
            - type: string
            - type: "null"
          title: Reason For Change
          description: Detailed explanation of changes applied.
        prior_status:
          anyOf:
            - type: string
            - type: "null"
          title: Prior Status
          description: Previous status before transition.
        object_type:
          type: string
          const: ARM
          title: Object Type
          default: ARM
        payload:
          $ref: "#/components/schemas/ArmPayload"
      type: object
      required:
        - id
        - version
        - status
        - sponsor_id
        - tenant_id
        - created_at
        - created_by
        - payload
      title: ArmLibraryObjectDetail
      description: Response model for an Arm library object.
    ArmPayload:
      properties:
        attributes:
          $ref: "#/components/schemas/ArmAttributes"
          description: Clinical arm configurations.
      type: object
      required:
        - attributes
      title: ArmPayload
      description: Arm-specific payload validation containing arm attributes.
    ArmReorderItem:
      properties:
        arm_id:
          type: string
          minLength: 1
          title: Arm Id
        sequence:
          type: integer
          minimum: 1.0
          title: Sequence
      type: object
      required:
        - arm_id
        - sequence
      title: ArmReorderItem
    ArmReorderRequest:
      properties:
        arms:
          items:
            $ref: "#/components/schemas/ArmReorderItem"
          type: array
          title: Arms
      type: object
      required:
        - arms
      title: ArmReorderRequest
    AttritionStep:
      properties:
        criterion_id:
          type: string
          title: Criterion Id
          description: The ID of the eligibility criterion
        type:
          type: string
          title: Type
          description: inclusion or exclusion
        description:
          type: string
          title: Description
          description: Description of the criterion
        passed_count:
          type: integer
          title: Passed Count
          description: Number of patients passing this criterion
        failed_count:
          type: integer
          title: Failed Count
          description: Number of patients failing this criterion
        remaining_count:
          type: integer
          title: Remaining Count
          description: Number of patients continuing to the next step
        attrition_rate:
          type: number
          title: Attrition Rate
          description: Percentage of current cohort lost at this step
      type: object
      required:
        - criterion_id
        - type
        - description
        - passed_count
        - failed_count
        - remaining_count
        - attrition_rate
      title: AttritionStep
      description: Step in the patient population attrition funnel.
    BlockCreatedResponse:
      properties:
        status:
          type: string
          title: Status
          default: success
        id:
          type: string
          title: Id
      type: object
      required:
        - id
      title: BlockCreatedResponse
    BlockDetailResponse:
      properties:
        id:
          type: string
          title: Id
        block_id:
          type: string
          title: Block Id
        block_type:
          type: string
          title: Block Type
        order:
          type: integer
          title: Order
        version_index:
          type: integer
          title: Version Index
        created_by:
          type: string
          title: Created By
        created_at:
          type: string
          title: Created At
      additionalProperties: true
      type: object
      required:
        - id
        - block_id
        - block_type
        - order
        - version_index
        - created_by
        - created_at
      title: BlockDetailResponse
    Body_upload_mapping_csv_api_v1_mappings_upload_post:
      properties:
        file:
          type: string
          contentMediaType: application/octet-stream
          title: File
      type: object
      required:
        - file
      title: Body_upload_mapping_csv_api_v1_mappings_upload_post
    Body_upload_protocol_ingestion_api_v1_designer_ingestion_upload_post:
      properties:
        file:
          type: string
          contentMediaType: application/octet-stream
          title: File
      type: object
      required:
        - file
      title: Body_upload_protocol_ingestion_api_v1_designer_ingestion_upload_post
    BurdenTraceItem:
      properties:
        component:
          type: string
          title: Component
          description: The name of the component, e.g. visits, procedures, forms
        count:
          type: integer
          title: Count
          description: Occurrences of the component
        weight:
          type: number
          title: Weight
          description: Weight multiplier per occurrence
        subtotal:
          type: number
          title: Subtotal
          description: Subtotal burden (count * weight)
        explanation:
          type: string
          title: Explanation
          description: Trace explanation of this component
      type: object
      required:
        - component
        - count
        - weight
        - subtotal
        - explanation
      title: BurdenTraceItem
      description: An itemized breakdown of clinical operational burden.
    BurdenTraceReport:
      properties:
        visit_burden:
          type: number
          title: Visit Burden
          description: Aggregated burden of patient visits
        procedure_burden:
          type: number
          title: Procedure Burden
          description: Aggregated burden of clinical procedures
        activity_burden:
          type: number
          title: Activity Burden
          description: Aggregated burden of CRFs and forms
        total_burden:
          type: number
          title: Total Burden
          description: Total clinical burden score
        trace:
          items:
            $ref: "#/components/schemas/BurdenTraceItem"
          type: array
          title: Trace
          description: Trace details explaining the sum
      type: object
      required:
        - visit_burden
        - procedure_burden
        - activity_burden
        - total_burden
      title: BurdenTraceReport
      description: Patient operational burden trace score.
    CDASHMapping:
      properties:
        domain:
          type: string
          title: Domain
        variable_name:
          type: string
          title: Variable Name
        data_type:
          type: string
          title: Data Type
      type: object
      required:
        - domain
        - variable_name
        - data_type
      title: CDASHMapping
    CascadeSummaryReport:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Target protocol study ID
        amendment_version:
          type: integer
          title: Amendment Version
          description: Protocol amendment version index
          default: 1
        forms_created:
          type: integer
          title: Forms Created
          description: Number of eCRF form templates generated
        visits_created:
          type: integer
          title: Visits Created
          description: Number of SoA visits synchronized
        rules_synced:
          type: integer
          title: Rules Synced
          description: Number of edit check rules generated
        forms:
          items:
            $ref: "#/components/schemas/CascadedFormTemplate"
          type: array
          title: Forms
          description: Cascaded form templates
      type: object
      required:
        - study_id
        - forms_created
        - visits_created
        - rules_synced
      title: CascadeSummaryReport
      description:
        "Summary report of downstream eCRF and SoA cascade propagation.


        Requirements: PRD-SYS-001"
    CascadedFormTemplate:
      properties:
        form_id:
          type: string
          title: Form Id
          description: Generated eCRF form template ID
        form_name:
          type: string
          title: Form Name
          description: eCRF form name
        domain:
          type: string
          title: Domain
          description: Target CDASH/SDTM domain code (e.g. VS, LB, AE)
        fields:
          items:
            additionalProperties: true
            type: object
          type: array
          title: Fields
          description: Form field definitions
        auto_generated:
          type: boolean
          title: Auto Generated
          description: True if cascaded from DDF protocol graph
          default: true
      type: object
      required:
        - form_id
        - form_name
        - domain
      title: CascadedFormTemplate
      description:
        "Auto-generated eCRF form template derived from USDM activities.


        Requirements: PRD-SYS-001"
    CodeValidationState:
      type: string
      enum:
        - VALID
        - INVALID
        - DEGRADED
      title: CodeValidationState
      description: Validation state of a controlled terminology concept code.
    Comment:
      properties:
        comment_id:
          type: string
          title: Comment Id
          description: Unique comment identifier.
        thread_id:
          type: string
          title: Thread Id
          description: Linked thread identifier.
        text:
          type: string
          title: Text
          description: Comment text body.
        created_by:
          type: string
          title: Created By
          description: Author user ID.
        created_at:
          type: string
          title: Created At
          description: Creation timestamp.
        updated_at:
          anyOf:
            - type: string
            - type: "null"
          title: Updated At
          description: Optional modification timestamp.
        version_index:
          type: integer
          title: Version Index
          description: Sequential version index for GxP auditing.
          default: 1
      type: object
      required:
        - comment_id
        - thread_id
        - text
        - created_by
      title: Comment
      description: Represent an individual block-anchored user review comment.
    CommentCreate:
      properties:
        text:
          type: string
          title: Text
      type: object
      required:
        - text
      title: CommentCreate
    CommentCreatePayload:
      properties:
        field_id:
          type: string
          title: Field Id
          description: The ID of the eCRF field this comment anchors to
        comment_text:
          type: string
          title: Comment Text
          description: The text content of the comment
      type: object
      required:
        - field_id
        - comment_text
      title: CommentCreatePayload
    CommentThread:
      properties:
        thread_id:
          type: string
          title: Thread Id
          description: Unique thread identifier.
        block_id:
          type: string
          title: Block Id
          description: Anchor block identifier.
        section_id:
          type: string
          title: Section Id
          description: Anchor section identifier.
        study_id:
          type: string
          title: Study Id
          description: Associated study identifier.
        status:
          type: string
          title: Status
          description: Thread resolution status (open, resolved).
          default: open
        created_by:
          type: string
          title: Created By
          description: Thread creator user ID.
        created_at:
          type: string
          title: Created At
          description: Creation timestamp.
        block_version_index:
          type: integer
          title: Block Version Index
          description: The block's version_index at the time of thread creation.
        comments:
          items:
            $ref: "#/components/schemas/Comment"
          type: array
          title: Comments
          description: Ordered list of comments.
      type: object
      required:
        - thread_id
        - block_id
        - section_id
        - study_id
        - created_by
        - block_version_index
      title: CommentThread
      description: Represents a collection of ordered review comments anchored to a specific block and section.
    CommentThreadCreate:
      properties:
        block_id:
          type: string
          title: Block Id
        text:
          type: string
          title: Text
      type: object
      required:
        - block_id
        - text
      title: CommentThreadCreate
    ComparisonOperator:
      type: string
      enum:
        - ==
        - "!="
        - <
        - <=
        - ">"
        - ">="
      title: ComparisonOperator
      description: Allowed binary comparison operators for criteria evaluations.
    ConceptDetail:
      properties:
        id:
          type: string
          title: Id
        concept_code:
          type: string
          title: Concept Code
        terminology:
          type: string
          title: Terminology
        display_name:
          type: string
          title: Display Name
        definition:
          type: string
          title: Definition
        cdash_mapping:
          anyOf:
            - $ref: "#/components/schemas/CDASHMapping"
            - type: "null"
        allowable_units:
          anyOf:
            - items:
                $ref: "#/components/schemas/AllowableUnit"
              type: array
            - type: "null"
          title: Allowable Units
        version:
          type: string
          title: Version
        status:
          type: string
          title: Status
        created_at:
          type: string
          format: date-time
          title: Created At
        created_by:
          type: string
          title: Created By
        updated_at:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Updated At
        updated_by:
          anyOf:
            - type: string
            - type: "null"
          title: Updated By
        reason_for_change:
          anyOf:
            - type: string
            - type: "null"
          title: Reason For Change
      type: object
      required:
        - id
        - concept_code
        - terminology
        - display_name
        - definition
        - version
        - status
        - created_at
        - created_by
      title: ConceptDetail
    ConceptListResponse:
      properties:
        object:
          type: string
          title: Object
        data:
          items:
            $ref: "#/components/schemas/ConceptDetail"
          type: array
          title: Data
        has_more:
          type: boolean
          title: Has More
        next_cursor:
          anyOf:
            - type: string
            - type: "null"
          title: Next Cursor
      type: object
      required:
        - object
        - data
        - has_more
      title: ConceptListResponse
    ConceptReference:
      properties:
        element_type:
          type: string
          title: Element Type
        element_id:
          type: string
          title: Element Id
        element_name:
          type: string
          title: Element Name
        attribute:
          type: string
          title: Attribute
      type: object
      required:
        - element_type
        - element_id
        - element_name
        - attribute
      title: ConceptReference
      description: Identifies a specific study element referencing a terminology concept.
    ConceptValidationReport:
      properties:
        concept_code:
          type: string
          title: Concept Code
        state:
          $ref: "#/components/schemas/CodeValidationState"
        decode:
          anyOf:
            - type: string
            - type: "null"
          title: Decode
        system:
          anyOf:
            - type: string
            - type: "null"
          title: System
        error_message:
          anyOf:
            - type: string
            - type: "null"
          title: Error Message
        references:
          items:
            $ref: "#/components/schemas/ConceptReference"
          type: array
          title: References
          default: []
      type: object
      required:
        - concept_code
        - state
      title: ConceptValidationReport
      description: Detailed validation status for a single terminology concept code.
    CreateArmRequest:
      properties:
        id:
          type: string
          title: Id
          description: Stable, unique global library ID.
        version:
          type: string
          title: Version
          description: Initial version code.
          default: 1.0.0
        status:
          $ref: "#/components/schemas/LibraryStatus"
          description: Initial library state.
          default: DRAFT
        sponsor_id:
          type: string
          title: Sponsor Id
          description: Sponsor / Tenant identifier.
        change_reason:
          type: string
          title: Change Reason
          description: Mandatory reason for change / audit trail justification.
        object_type:
          type: string
          const: ARM
          title: Object Type
          default: ARM
        payload:
          $ref: "#/components/schemas/ArmPayload"
      type: object
      required:
        - id
        - sponsor_id
        - change_reason
        - payload
      title: CreateArmRequest
      description: Request model for creating an Arm library object.
    CreateBlockRequest:
      properties:
        id:
          type: string
          title: Id
        block_type:
          type: string
          title: Block Type
        order:
          type: integer
          title: Order
        properties:
          additionalProperties: true
          type: object
          title: Properties
        change_reason:
          anyOf:
            - type: string
            - type: "null"
          title: Change Reason
      type: object
      required:
        - id
        - block_type
        - order
        - properties
      title: CreateBlockRequest
    CreateConceptRequest:
      properties:
        concept_code:
          type: string
          title: Concept Code
        terminology:
          type: string
          title: Terminology
        display_name:
          type: string
          title: Display Name
        definition:
          type: string
          title: Definition
        cdash_mapping:
          anyOf:
            - $ref: "#/components/schemas/CDASHMapping"
            - type: "null"
        allowable_units:
          anyOf:
            - items:
                $ref: "#/components/schemas/AllowableUnit"
              type: array
            - type: "null"
          title: Allowable Units
        change_reason:
          type: string
          title: Change Reason
      type: object
      required:
        - concept_code
        - terminology
        - display_name
        - definition
        - change_reason
      title: CreateConceptRequest
    CreateDataElementRequest:
      properties:
        id:
          type: string
          title: Id
          description: Stable, unique global library ID.
        version:
          type: string
          title: Version
          description: Initial version code.
          default: 1.0.0
        status:
          $ref: "#/components/schemas/LibraryStatus"
          description: Initial library state.
          default: DRAFT
        sponsor_id:
          type: string
          title: Sponsor Id
          description: Sponsor / Tenant identifier.
        change_reason:
          type: string
          title: Change Reason
          description: Mandatory reason for change / audit trail justification.
        object_type:
          type: string
          const: DATA_ELEMENT
          title: Object Type
          default: DATA_ELEMENT
        payload:
          $ref: "#/components/schemas/DataElementPayload"
      type: object
      required:
        - id
        - sponsor_id
        - change_reason
        - payload
      title: CreateDataElementRequest
      description: Request model for creating a Data Element library object.
    CreateEligibilityCriterionRequest:
      properties:
        criterion_id:
          type: string
          title: Criterion Id
          description: Unique identifier of this eligibility criterion, e.g., 'INC_01'.
        criterion_type:
          type: string
          enum:
            - inclusion
            - exclusion
          title: Criterion Type
          description: Whether this is an inclusion or exclusion criterion.
        description:
          type: string
          title: Description
          description: Human-readable text description of the criterion.
        dsl_source:
          type: string
          title: Dsl Source
          description: The raw DSL statement source, e.g., 'eCRF.DM.AGE >= 18'.
        expected_outcome:
          type: boolean
          title: Expected Outcome
          description: Expected Boolean outcome of evaluating the condition node.
          default: true
        change_reason:
          type: string
          title: Change Reason
          description: Reason for creating this criterion.
      type: object
      required:
        - criterion_id
        - criterion_type
        - description
        - dsl_source
        - change_reason
      title: CreateEligibilityCriterionRequest
    CreateEpochRequest:
      properties:
        id:
          type: string
          minLength: 1
          title: Id
          description: Unique identifier for the epoch.
        properties:
          $ref: "#/components/schemas/EpochProperties"
        change_reason:
          type: string
          title: Change Reason
          description: Change reason for audit trail
          default: Created epoch
      type: object
      required:
        - id
        - properties
      title: CreateEpochRequest
    CreateFormRequest:
      properties:
        id:
          type: string
          title: Id
          description: Stable, unique global library ID.
        version:
          type: string
          title: Version
          description: Initial version code.
          default: 1.0.0
        status:
          $ref: "#/components/schemas/LibraryStatus"
          description: Initial library state.
          default: DRAFT
        sponsor_id:
          type: string
          title: Sponsor Id
          description: Sponsor / Tenant identifier.
        change_reason:
          type: string
          title: Change Reason
          description: Mandatory reason for change / audit trail justification.
        object_type:
          type: string
          const: FORM
          title: Object Type
          default: FORM
        payload:
          $ref: "#/components/schemas/FormPayload"
      type: object
      required:
        - id
        - sponsor_id
        - change_reason
        - payload
      title: CreateFormRequest
      description: Request model for creating a Form library object.
    CreateProcedureRequest:
      properties:
        id:
          type: string
          minLength: 1
          title: Id
          description: Unique identifier for the procedure.
        properties:
          $ref: "#/components/schemas/ProcedureProperties"
        change_reason:
          type: string
          title: Change Reason
          description: Change reason for audit trail
          default: Created procedure
      type: object
      required:
        - id
        - properties
      title: CreateProcedureRequest
    CreateRuleRequest:
      properties:
        type:
          type: string
          enum:
            - skip_logic
            - constraint
            - cross_form_check
          title: Type
        condition:
          $ref: "#/components/schemas/ExpressionNode-Input"
        action:
          anyOf:
            - type: string
              enum:
                - show
                - hide
            - type: "null"
          title: Action
        target_field:
          anyOf:
            - type: string
            - type: "null"
          title: Target Field
        target_form:
          anyOf:
            - type: string
            - type: "null"
          title: Target Form
        target_group:
          anyOf:
            - type: string
            - type: "null"
          title: Target Group
        query_message:
          anyOf:
            - type: string
            - type: "null"
          title: Query Message
      type: object
      required:
        - type
        - condition
      title: CreateRuleRequest
      description: Request schema to create a rule.
    CreateStudyArmRequest:
      properties:
        id:
          type: string
          minLength: 1
          title: Id
          description: Unique identifier for the study arm.
        properties:
          $ref: "#/components/schemas/StudyArmProperties"
        change_reason:
          type: string
          title: Change Reason
          description: Change reason for audit trail
          default: Created study arm
      type: object
      required:
        - id
        - properties
      title: CreateStudyArmRequest
    CreateStudyVersionRequest:
      properties:
        id:
          type: string
          title: Id
        version_tag:
          type: string
          title: Version Tag
        status:
          type: string
          title: Status
        version_index:
          type: integer
          title: Version Index
      type: object
      required:
        - id
        - version_tag
        - status
        - version_index
      title: CreateStudyVersionRequest
      description: Request payload to establish a StudyVersion node.
    CreateTimingWindowRequest:
      properties:
        id:
          type: string
          minLength: 1
          title: Id
          description: Unique identifier for the timing window.
        properties:
          $ref: "#/components/schemas/TimingWindowProperties"
        change_reason:
          type: string
          title: Change Reason
          description: Change reason for audit trail
          default: Created timing window
      type: object
      required:
        - id
        - properties
      title: CreateTimingWindowRequest
    DataElementLibraryObjectDetail:
      properties:
        id:
          type: string
          title: Id
          description: Stable, unique global library ID.
        version:
          type: string
          title: Version
          description: Semantic version of the library object.
        status:
          $ref: "#/components/schemas/LibraryStatus"
          description: Workflow review status of the object.
        sponsor_id:
          type: string
          title: Sponsor Id
          description: Sponsor identifier.
        tenant_id:
          type: string
          title: Tenant Id
          description: Tenant / Partition identifier.
        created_at:
          type: string
          format: date-time
          title: Created At
          description: Audit timestamp of creation.
        created_by:
          type: string
          title: Created By
          description: User ID who created this object.
        updated_at:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Updated At
          description: Audit timestamp of last update.
        updated_by:
          anyOf:
            - type: string
            - type: "null"
          title: Updated By
          description: User ID of last updater.
        reason_for_change:
          anyOf:
            - type: string
            - type: "null"
          title: Reason For Change
          description: Detailed explanation of changes applied.
        prior_status:
          anyOf:
            - type: string
            - type: "null"
          title: Prior Status
          description: Previous status before transition.
        object_type:
          type: string
          const: DATA_ELEMENT
          title: Object Type
          default: DATA_ELEMENT
        payload:
          $ref: "#/components/schemas/DataElementPayload"
      type: object
      required:
        - id
        - version
        - status
        - sponsor_id
        - tenant_id
        - created_at
        - created_by
        - payload
      title: DataElementLibraryObjectDetail
      description: Response model for a Data Element library object.
    DataElementPayload:
      properties:
        data_type:
          type: string
          title: Data Type
          description: Expected value type (e.g., numeric, text).
        allowable_units:
          items:
            type: string
          type: array
          title: Allowable Units
          description: List of standard UCUM unit codes allowed.
        default_unit:
          anyOf:
            - type: string
            - type: "null"
          title: Default Unit
          description: Default unit code from the allowable list.
      type: object
      required:
        - data_type
        - allowable_units
      title: DataElementPayload
      description: Data-element specific payload validation containing units and format.
    DifferenceResult:
      properties:
        field:
          type: string
          title: Field
        old_value:
          title: Old Value
        new_value:
          title: New Value
      type: object
      required:
        - field
        - old_value
        - new_value
      title: DifferenceResult
      description: "Represents a field-level difference between two versions.\n\nAttributes:\n    field: The name of the field that changed.\n    old_value: The previous value of the field."
    EligibilityCriterion:
      properties:
        created_at:
          type: string
          title: Created At
          description: Chronological UTC timestamp when the record was created.
        created_by:
          type: string
          title: Created By
          description: Unique identifier of the user who created the record.
        reason_for_change:
          type: string
          title: Reason For Change
          description: Mandatory explanation or audit justification for creating or mutating this record.
        version_index:
          type: integer
          title: Version Index
          description: Row version counter or index.
          default: 1
        id:
          type: string
          title: Id
          description: Unique identifier of this eligibility criterion, e.g., 'INC_01'.
          default: ""
        criterion_type:
          type: string
          enum:
            - inclusion
            - exclusion
          title: Criterion Type
          description: Whether this is an inclusion or exclusion criterion.
        identifier:
          type: string
          title: Identifier
          description: Business identifier of this criterion, e.g., 'INC-001'.
          default: ""
        human_readable_text:
          type: string
          title: Human Readable Text
          description: Human-readable text description of the criterion.
          default: ""
        dsl_expression_string:
          type: string
          title: Dsl Expression String
          description: The raw DSL statement source, e.g., 'eCRF.DM.AGE >= 18'.
          default: ""
        structured_expression_tree:
          $ref: "#/components/schemas/ExpressionNode-Output"
          description: The parsed structured AST of this criterion.
        expected_outcome:
          type: boolean
          title: Expected Outcome
          description: Expected Boolean outcome of evaluating the condition node. Typically True for inclusions and False for exclusions.
          default: true
        criterion_id:
          type: string
          title: Criterion Id
          description: Backward compatible criterion_id field.
          default: ""
        description:
          type: string
          title: Description
          description: Backward compatible description field.
          default: ""
        dsl_source:
          type: string
          title: Dsl Source
          description: Backward compatible dsl_source field.
          default: ""
        condition:
          $ref: "#/components/schemas/ExpressionNode-Output"
          description: Backward compatible condition field.
      type: object
      required:
        - created_by
        - reason_for_change
        - criterion_type
      title: EligibilityCriterion
      description: Represents a single inclusion or exclusion criterion with full GxP audit metadata.
    EpochProperties:
      properties:
        name:
          anyOf:
            - type: string
              minLength: 1
            - type: "null"
          title: Name
          description: The name of the study epoch, e.g., 'Screening'.
        epoch_name:
          anyOf:
            - type: string
              minLength: 1
            - type: "null"
          title: Epoch Name
          description: Alternate/legacy field name for epoch name.
        sequence:
          type: integer
          minimum: 1.0
          title: Sequence
          description: Sequential ordering rank of the epoch.
      type: object
      required:
        - sequence
      title: EpochProperties
      description: Properties specific to a Study Epoch.
    EpochReorderItem:
      properties:
        epoch_id:
          type: string
          minLength: 1
          title: Epoch Id
        sequence:
          type: integer
          minimum: 1.0
          title: Sequence
      type: object
      required:
        - epoch_id
        - sequence
      title: EpochReorderItem
    EpochReorderRequest:
      properties:
        epochs:
          items:
            $ref: "#/components/schemas/EpochReorderItem"
          type: array
          title: Epochs
      type: object
      required:
        - epochs
      title: EpochReorderRequest
    ExpressionNode-Input:
      properties:
        type:
          type: string
          enum:
            - logical
            - comparison
            - function
            - field_ref
            - constant
          title: Type
        operator:
          anyOf:
            - type: string
            - type: "null"
          title: Operator
        operands:
          anyOf:
            - items:
                $ref: "#/components/schemas/ExpressionNode-Input"
              type: array
            - type: "null"
          title: Operands
        value:
          anyOf:
            - {}
            - type: "null"
          title: Value
        field_ref:
          anyOf:
            - $ref: "#/components/schemas/FieldReference-Input"
            - type: "null"
      type: object
      required:
        - type
      title: ExpressionNode
      description: A recursive node in a structured clinical expression tree.
    ExpressionNode-Output:
      properties:
        type:
          type: string
          enum:
            - logical
            - comparison
            - field_ref
            - constant
          title: Type
          description: Node type indicating the structure of the node.
        operator:
          anyOf:
            - $ref: "#/components/schemas/ComparisonOperator"
            - $ref: "#/components/schemas/LogicalOperator"
            - type: string
            - type: "null"
          title: Operator
          description: Operator for logical (and, or, not) or comparison (==, !=, <, <=, >, >=) nodes.
        operands:
          anyOf:
            - items:
                $ref: "#/components/schemas/ExpressionNode-Output"
              type: array
            - type: "null"
          title: Operands
          description: Child operands of logical or comparison nodes.
        value:
          anyOf:
            - {}
            - type: "null"
          title: Value
          description: Literal constant value of type constant.
        field_ref:
          anyOf:
            - $ref: "#/components/schemas/FieldReference-Output"
            - type: "null"
          description: Field reference details of type field_ref.
      type: object
      required:
        - type
      title: ExpressionNode
      description:
        "Recursive node inside a structured clinical expression tree (AST).

        Supported types are: logical, comparison, field_ref, constant."
    FeasibilityReport:
      properties:
        starting_cohort_size:
          type: integer
          title: Starting Cohort Size
          description: Initial patient pool size
        final_eligible_count:
          type: integer
          title: Final Eligible Count
          description: Number of fully eligible patients
        overall_eligibility_rate:
          type: number
          title: Overall Eligibility Rate
          description: Percentage of cohort that is eligible
        attrition_steps:
          items:
            $ref: "#/components/schemas/AttritionStep"
          type: array
          title: Attrition Steps
          description: Step-by-step funnel of attrition
      type: object
      required:
        - starting_cohort_size
        - final_eligible_count
        - overall_eligibility_rate
      title: FeasibilityReport
      description: Cohort-backed patient population feasibility and attrition rates.
    FieldReference-Input:
      properties:
        field_id:
          type: string
          title: Field Id
        form_id:
          anyOf:
            - type: string
            - type: "null"
          title: Form Id
        visit_id:
          anyOf:
            - type: string
            - type: "null"
          title: Visit Id
        visit_relative:
          anyOf:
            - type: string
            - type: "null"
          title: Visit Relative
      type: object
      required:
        - field_id
      title: FieldReference
      description: Represents a structured field reference within an expression tree.
    FieldReference-Output:
      properties:
        raw_reference:
          type: string
          title: Raw Reference
          description: Raw field reference string, e.g., 'eCRF.DM.AGE'.
        domain:
          type: string
          title: Domain
          description: The target eCRF domain, e.g., 'DM'.
        variable:
          type: string
          title: Variable
          description: The domain variable, e.g., 'AGE'.
      type: object
      required:
        - raw_reference
        - domain
        - variable
      title: FieldReference
      description:
        "Represents a structured field reference pointing to an eCRF domain variable.

        Format must strictly follow: eCRF.<DOMAIN>.<VARIABLE>"
    FormItem:
      properties:
        item_id:
          type: string
          title: Item Id
          description: Unique stable ID for the form item.
        name:
          type: string
          title: Name
          description: Identifier name conformant with CDASH/SDTM standards.
        question_text:
          type: string
          title: Question Text
          description: The user-facing prompt text.
        data_type:
          type: string
          title: Data Type
          description: Primitive data type (e.g., text, integer, choice, date).
        required:
          type: boolean
          title: Required
          description: Indicates if the form item must be filled.
          default: true
      type: object
      required:
        - item_id
        - name
        - question_text
        - data_type
      title: FormItem
      description: Represents an individual question/field item within a Form.
    FormLibraryObjectDetail:
      properties:
        id:
          type: string
          title: Id
          description: Stable, unique global library ID.
        version:
          type: string
          title: Version
          description: Semantic version of the library object.
        status:
          $ref: "#/components/schemas/LibraryStatus"
          description: Workflow review status of the object.
        sponsor_id:
          type: string
          title: Sponsor Id
          description: Sponsor identifier.
        tenant_id:
          type: string
          title: Tenant Id
          description: Tenant / Partition identifier.
        created_at:
          type: string
          format: date-time
          title: Created At
          description: Audit timestamp of creation.
        created_by:
          type: string
          title: Created By
          description: User ID who created this object.
        updated_at:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Updated At
          description: Audit timestamp of last update.
        updated_by:
          anyOf:
            - type: string
            - type: "null"
          title: Updated By
          description: User ID of last updater.
        reason_for_change:
          anyOf:
            - type: string
            - type: "null"
          title: Reason For Change
          description: Detailed explanation of changes applied.
        prior_status:
          anyOf:
            - type: string
            - type: "null"
          title: Prior Status
          description: Previous status before transition.
        object_type:
          type: string
          const: FORM
          title: Object Type
          default: FORM
        payload:
          $ref: "#/components/schemas/FormPayload"
      type: object
      required:
        - id
        - version
        - status
        - sponsor_id
        - tenant_id
        - created_at
        - created_by
        - payload
      title: FormLibraryObjectDetail
      description: Response model for a Form library object.
    FormPayload:
      properties:
        items:
          items:
            $ref: "#/components/schemas/FormItem"
          type: array
          title: Items
          description: List of form items/fields defined in this template.
      type: object
      required:
        - items
      title: FormPayload
      description: Form-specific payload validation containing items.
    FormReviewCommentResponse:
      properties:
        id:
          type: string
          title: Id
        form_id:
          type: string
          title: Form Id
        field_id:
          type: string
          title: Field Id
        author_id:
          type: string
          title: Author Id
        comment_text:
          type: string
          title: Comment Text
        status:
          type: string
          title: Status
        created_at:
          type: string
          title: Created At
        isResolved:
          type: boolean
          title: Isresolved
        authorName:
          type: string
          title: Authorname
        createdAt:
          type: string
          title: Createdat
        text:
          type: string
          title: Text
      type: object
      required:
        - id
        - form_id
        - field_id
        - author_id
        - comment_text
        - status
        - created_at
        - isResolved
        - authorName
        - createdAt
        - text
      title: FormReviewCommentResponse
    HTTPValidationError:
      properties:
        detail:
          items:
            $ref: "#/components/schemas/ValidationError"
          type: array
          title: Detail
      type: object
      title: HTTPValidationError
    InstantiateLibraryObjectRequest:
      properties:
        library_object_id:
          type: string
          title: Library Object Id
          description: Stable, unique global library ID to instantiate.
        version:
          anyOf:
            - type: integer
            - type: "null"
          title: Version
          description: The specific version of the library object to instantiate. Defaults to latest if not specified.
      type: object
      required:
        - library_object_id
      title: InstantiateLibraryObjectRequest
    InstantiatedFromDetail:
      properties:
        library_object_id:
          type: string
          title: Library Object Id
        version:
          type: integer
          title: Version
        sponsor_id:
          type: string
          title: Sponsor Id
      type: object
      required:
        - library_object_id
        - version
        - sponsor_id
      title: InstantiatedFromDetail
    InvalidParam:
      properties:
        field:
          anyOf:
            - type: string
            - type: "null"
          title: Field
        reason:
          anyOf:
            - type: string
            - type: "null"
          title: Reason
        value:
          anyOf:
            - type: string
            - type: "null"
          title: Value
      type: object
      title: InvalidParam
    ItemMappingStatus:
      properties:
        item_id:
          anyOf:
            - type: string
            - type: "null"
          title: Item Id
        internal_id:
          anyOf:
            - type: integer
            - type: "null"
          title: Internal Id
        is_mapped:
          type: boolean
          title: Is Mapped
      type: object
      required:
        - item_id
        - internal_id
        - is_mapped
      title: ItemMappingStatus
      description: "Represents the mapping status of an individual activity item.\n\nAttributes:\n    item_id: The public string identifier of the activity item.\n    internal_id: The internal graph database ID of the activity item.\n    is_mapped: Boolean indicating whether this item has a corresponding ODM/CRF node mapped to it."
    LibraryInstanceResponse:
      properties:
        id:
          type: string
          title: Id
        study_id:
          type: string
          title: Study Id
        object_type:
          type: string
          title: Object Type
        payload:
          additionalProperties: true
          type: object
          title: Payload
        created_at:
          type: string
          title: Created At
        created_by:
          type: string
          title: Created By
        instantiated_from:
          $ref: "#/components/schemas/InstantiatedFromDetail"
      type: object
      required:
        - id
        - study_id
        - object_type
        - payload
        - created_at
        - created_by
        - instantiated_from
      title: LibraryInstanceResponse
    LibraryObjectAmendRequest:
      properties:
        reason_for_change:
          type: string
          title: Reason For Change
          description: Mandatory reason for initiating the amendment.
        payload:
          anyOf:
            - additionalProperties: true
              type: object
            - type: "null"
          title: Payload
          description: Optional updated payload for the amended version. If not provided, the latest payload is cloned.
      type: object
      required:
        - reason_for_change
      title: LibraryObjectAmendRequest
      description: Payload for the Library Object Amendment endpoint.
    LibraryObjectListResponse:
      properties:
        object:
          type: string
          title: Object
          default: list
        data:
          items:
            oneOf:
              - $ref: "#/components/schemas/FormLibraryObjectDetail"
              - $ref: "#/components/schemas/DataElementLibraryObjectDetail"
              - $ref: "#/components/schemas/ArmLibraryObjectDetail"
              - $ref: "#/components/schemas/VisitLibraryObjectDetail"
            discriminator:
              propertyName: object_type
              mapping:
                ARM: "#/components/schemas/ArmLibraryObjectDetail"
                DATA_ELEMENT: "#/components/schemas/DataElementLibraryObjectDetail"
                FORM: "#/components/schemas/FormLibraryObjectDetail"
                VISIT: "#/components/schemas/VisitLibraryObjectDetail"
          type: array
          title: Data
        has_more:
          type: boolean
          title: Has More
        next_cursor:
          anyOf:
            - type: string
            - type: "null"
          title: Next Cursor
      type: object
      required:
        - data
        - has_more
      title: LibraryObjectListResponse
      description: "Paginated response envelope for Global Library Objects.

        Matches Stripe-style list response."
    LibraryObjectTransitionRequest:
      properties:
        status:
          $ref: "#/components/schemas/LibraryStatus"
          description: Target status level for transition.
        change_reason:
          type: string
          title: Change Reason
          description: Mandatory reason for change / audit trail justification.
      type: object
      required:
        - status
        - change_reason
      title: LibraryObjectTransitionRequest
      description: Request model for transitioning library object lifecycle status.
    LibraryStatus:
      type: string
      enum:
        - DRAFT
        - IN_REVIEW
        - APPROVED
        - PUBLISHED
        - ARCHIVED
        - REJECTED
      title: LibraryStatus
      description: Standard status levels for Global Library objects.
    LinkArmApplicabilityRequest:
      properties:
        arm_id:
          type: string
          minLength: 1
          title: Arm Id
        target_id:
          type: string
          minLength: 1
          title: Target Id
        target_type:
          type: string
          enum:
            - visit
            - procedure
            - epoch
          title: Target Type
          default: visit
      type: object
      required:
        - arm_id
        - target_id
      title: LinkArmApplicabilityRequest
    LinkEpochVisitRequest:
      properties:
        epoch_id:
          type: string
          minLength: 1
          title: Epoch Id
        visit_id:
          type: string
          minLength: 1
          title: Visit Id
      type: object
      required:
        - epoch_id
        - visit_id
      title: LinkEpochVisitRequest
    LinkTimingRequest:
      properties:
        source_id:
          type: string
          minLength: 1
          title: Source Id
        timing_id:
          type: string
          minLength: 1
          title: Timing Id
        source_type:
          type: string
          enum:
            - visit
            - procedure
          title: Source Type
          default: visit
      type: object
      required:
        - source_id
        - timing_id
      title: LinkTimingRequest
    LinkVisitProcedureRequest:
      properties:
        visit_id:
          type: string
          minLength: 1
          title: Visit Id
        procedure_id:
          type: string
          minLength: 1
          title: Procedure Id
      type: object
      required:
        - visit_id
        - procedure_id
      title: LinkVisitProcedureRequest
    LogicalOperator:
      type: string
      enum:
        - and
        - or
        - not
      title: LogicalOperator
      description: Allowed logical connectors for composite criteria expressions.
    ObjectType:
      type: string
      enum:
        - FORM
        - DATA_ELEMENT
        - ARM
        - VISIT
      title: ObjectType
      description: Supported types of clinical design objects in the Global Library.
    ProblemDetails:
      properties:
        type:
          type: string
          title: Type
        title:
          type: string
          title: Title
        status:
          type: integer
          title: Status
        detail:
          type: string
          title: Detail
        instance:
          type: string
          title: Instance
        code:
          type: string
          title: Code
        invalid_params:
          anyOf:
            - items:
                $ref: "#/components/schemas/InvalidParam"
              type: array
            - type: "null"
          title: Invalid Params
      type: object
      required:
        - type
        - title
        - status
        - detail
        - instance
        - code
      title: ProblemDetails
    ProcedureProperties:
      properties:
        name:
          anyOf:
            - type: string
              minLength: 1
            - type: "null"
          title: Name
          description: The display name of the procedure.
        activity_name:
          anyOf:
            - type: string
              minLength: 1
            - type: "null"
          title: Activity Name
          description: Alternate/legacy field name for the procedure.
      type: object
      title: ProcedureProperties
      description: Properties specific to a clinical Procedure / Activity.
    ProcedureReorderItem:
      properties:
        procedure_id:
          type: string
          minLength: 1
          title: Procedure Id
        sequence:
          type: integer
          minimum: 1.0
          title: Sequence
      type: object
      required:
        - procedure_id
        - sequence
      title: ProcedureReorderItem
    ProcedureReorderRequest:
      properties:
        procedures:
          items:
            $ref: "#/components/schemas/ProcedureReorderItem"
          type: array
          title: Procedures
      type: object
      required:
        - procedures
      title: ProcedureReorderRequest
    PromoteRequest:
      properties:
        change_reason:
          type: string
          title: Change Reason
      type: object
      required:
        - change_reason
      title: PromoteRequest
    ProtocolAmendRequest:
      properties:
        amendment_type:
          anyOf:
            - type: string
            - type: "null"
          title: Amendment Type
          default: minor
        type:
          anyOf:
            - type: string
            - type: "null"
          title: Type
      type: object
      title: ProtocolAmendRequest
      description: Payload for the Protocol/Designer Amendment endpoint.
    ProtocolQualityScore:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Target protocol study ID
        quality_score:
          type: number
          title: Quality Score
          description: Overall protocol quality score (0.0 to 100.0)
        patient_burden_index:
          type: number
          title: Patient Burden Index
          description: Calculated patient operational burden score
        findings:
          items:
            $ref: "#/components/schemas/QualityRuleFinding"
          type: array
          title: Findings
          description: Quality findings
        passed:
          type: boolean
          title: Passed
          description: True if no ERROR severity findings exist
        readability:
          anyOf:
            - $ref: "#/components/schemas/ReadabilityReport"
            - type: "null"
          description: Readability metrics of narrative text blocks
        burden_details:
          anyOf:
            - $ref: "#/components/schemas/BurdenTraceReport"
            - type: "null"
          description: Traceable operational burden details
        amendment_impact:
          anyOf:
            - $ref: "#/components/schemas/AmendmentImpactReport"
            - type: "null"
          description: Amendment impact and cost estimation
        feasibility:
          anyOf:
            - $ref: "#/components/schemas/FeasibilityReport"
            - type: "null"
          description: Pluggable patient population feasibility metrics
      type: object
      required:
        - study_id
        - quality_score
        - patient_burden_index
        - passed
      title: ProtocolQualityScore
      description: "Protocol Quality Sentinel evaluation summary report.


        Requirements: PRD-SYS-001"
    QualityRuleFinding:
      properties:
        rule_id:
          type: string
          title: Rule Id
          description: Unique quality rule ID (e.g. SENTINEL_REQ_01)
        severity:
          type: string
          title: Severity
          description: "Severity level: ERROR, WARNING, INFO"
        category:
          type: string
          title: Category
          description: "Category: Structural, Regulatory, Burden, Inconsistency"
        message:
          type: string
          title: Message
          description: Human-readable rule finding message
        target_node_id:
          anyOf:
            - type: string
            - type: "null"
          title: Target Node Id
          description: Target USDM graph node ID
      type: object
      required:
        - rule_id
        - severity
        - category
        - message
      title: QualityRuleFinding
      description: "Specific protocol quality rule finding.


        Requirements: PRD-SYS-001"
    ReadabilityReport:
      properties:
        flesch_reading_ease:
          type: number
          title: Flesch Reading Ease
          description: Flesch Reading Ease score
        flesch_kincaid_grade_level:
          type: number
          title: Flesch Kincaid Grade Level
          description: Flesch-Kincaid Grade Level
        word_count:
          type: integer
          title: Word Count
          description: Total words counted
        sentence_count:
          type: integer
          title: Sentence Count
          description: Total sentences counted
        syllable_count:
          type: integer
          title: Syllable Count
          description: Total syllables counted
        interpretation:
          type: string
          title: Interpretation
          description: Human-readable readability description
      type: object
      required:
        - flesch_reading_ease
        - flesch_kincaid_grade_level
        - word_count
        - sentence_count
        - syllable_count
        - interpretation
      title: ReadabilityReport
      description: Deterministic readability metrics of block texts.
    RenameConceptRequest:
      properties:
        display_name:
          type: string
          title: Display Name
        reason_for_change:
          type: string
          title: Reason For Change
      type: object
      required:
        - display_name
        - reason_for_change
      title: RenameConceptRequest
    ReorderBlocksRequest:
      properties:
        block_ids:
          items:
            type: string
          type: array
          title: Block Ids
        change_reason:
          anyOf:
            - type: string
            - type: "null"
          title: Change Reason
      type: object
      required:
        - block_ids
      title: ReorderBlocksRequest
    RulePreviewResponse:
      properties:
        xpath:
          type: string
          title: Xpath
        failures:
          items:
            type: string
          type: array
          title: Failures
        circular_cycles:
          items:
            type: string
          type: array
          title: Circular Cycles
      type: object
      required:
        - xpath
        - failures
        - circular_cycles
      title: RulePreviewResponse
      description: Response for rule preview/validation request.
    SectionReviewStatus:
      type: string
      enum:
        - DRAFT
        - IN_REVIEW
        - LOCKED
        - APPROVED
      title: SectionReviewStatus
      description: Standard review statuses representing the lifecycle of an ICH section.
    SectionReviewTransition:
      properties:
        transition_id:
          type: string
          title: Transition Id
          description: Unique transition tracking identifier.
        section_id:
          type: string
          title: Section Id
          description: Anchor section identifier.
        study_id:
          type: string
          title: Study Id
          description: Associated study identifier.
        from_status:
          $ref: "#/components/schemas/SectionReviewStatus"
          description: Source review status.
        to_status:
          $ref: "#/components/schemas/SectionReviewStatus"
          description: Destination review status.
        actor_id:
          type: string
          title: Actor Id
          description: User ID executing status transition.
        actor_role:
          type: string
          title: Actor Role
          description: Role string used to authorize status transition.
        reason_for_change:
          type: string
          title: Reason For Change
          description: Part 11 change reason justification.
        timestamp:
          type: string
          title: Timestamp
          description: Transition timestamp.
      type: object
      required:
        - transition_id
        - section_id
        - study_id
        - from_status
        - to_status
        - actor_id
        - actor_role
        - reason_for_change
      title: SectionReviewTransition
      description: Represents an immutable, audited, Part 11 compliant transition of a section review status.
    SectionTransitionRequest:
      properties:
        to_status:
          $ref: "#/components/schemas/SectionReviewStatus"
        reason_for_change:
          type: string
          title: Reason For Change
        username:
          anyOf:
            - type: string
            - type: "null"
          title: Username
        password:
          anyOf:
            - type: string
            - type: "null"
          title: Password
        signing_reason:
          anyOf:
            - $ref: "#/components/schemas/SigningReason"
            - type: "null"
      type: object
      required:
        - to_status
        - reason_for_change
      title: SectionTransitionRequest
    SigningReason:
      type: string
      enum:
        - AUTHOR
        - REVIEW
        - APPROVAL
        - SPONSOR_APPROVAL
        - INVESTIGATOR_SIGNATURE
        - TECHNICAL_QC
        - CLINICAL_QC
        - DATA_LOCK
        - SYSTEM_SEAL
        - PROTOCOL_APPROVAL
        - REGULATORY_FORM_SIGNATURE
        - TRAINING_ACKNOWLEDGEMENT
        - SITE_VISIT_SIGN_OFF
      title: SigningReason
      description: Controlled reasons for creating an electronic signature in compliance with 21 CFR Part 11.
    SoACellView:
      properties:
        activity_id:
          type: string
          title: Activity Id
          description: Target activity/procedure identifier.
        encounter_id:
          type: string
          title: Encounter Id
          description: Target encounter/visit identifier.
        epoch_id:
          type: string
          title: Epoch Id
          description: Associated study epoch identifier.
        is_applicable:
          type: boolean
          title: Is Applicable
          description: Whether the activity is planned to occur during this encounter.
        details:
          anyOf:
            - type: string
            - type: "null"
          title: Details
          description: Optional timing windows, constraints, or instruction notes.
        arm_id:
          anyOf:
            - type: string
            - type: "null"
          title: Arm Id
          description: Optional associated arm ID.
        derived_from_soa:
          type: boolean
          title: Derived From Soa
          description: Flag indicating selective lineage.
          default: false
      type: object
      required:
        - activity_id
        - encounter_id
        - epoch_id
        - is_applicable
      title: SoACellView
      description: An individual cell within the SoA matrix indicating applicability of an activity at an encounter.
    SoAEntityCreatedResponse:
      properties:
        status:
          type: string
          title: Status
          default: success
        id:
          type: string
          title: Id
      type: object
      required:
        - id
      title: SoAEntityCreatedResponse
      description: Standard successful creation response.
    SoAEntityDetail:
      properties:
        id:
          type: string
          title: Id
        version_index:
          type: integer
          title: Version Index
        created_by:
          type: string
          title: Created By
        created_at:
          type: string
          title: Created At
        is_retired:
          type: boolean
          title: Is Retired
          default: false
        is_deleted:
          type: boolean
          title: Is Deleted
          default: false
      additionalProperties: true
      type: object
      required:
        - id
        - version_index
        - created_by
        - created_at
      title: SoAEntityDetail
      description: Standard details of a versioned SoA entity.
    SoAHeaderArm:
      properties:
        arm_id:
          type: string
          title: Arm Id
          description: Unique arm identifier.
        arm_name:
          type: string
          title: Arm Name
          description: Name of the study arm (e.g., Active, Placebo).
        sequence:
          type: integer
          title: Sequence
          description: Sequence number of the arm.
      type: object
      required:
        - arm_id
        - arm_name
        - sequence
      title: SoAHeaderArm
      description: Presentation header representing a trial Study Arm.
    SoAHeaderEncounter:
      properties:
        encounter_id:
          type: string
          title: Encounter Id
          description: Unique encounter/visit identifier.
        encounter_name:
          type: string
          title: Encounter Name
          description: Name of the encounter/visit.
        epoch_id:
          type: string
          title: Epoch Id
          description: Associated study epoch identifier.
        sequence:
          type: integer
          title: Sequence
          description: Sequence number of the encounter/visit.
        arm_id:
          anyOf:
            - type: string
            - type: "null"
          title: Arm Id
          description: Optional associated arm ID.
      type: object
      required:
        - encounter_id
        - encounter_name
        - epoch_id
        - sequence
      title: SoAHeaderEncounter
      description: Presentation header representing a visit or Encounter within a Study Epoch.
    SoAHeaderEpoch:
      properties:
        epoch_id:
          type: string
          title: Epoch Id
          description: Unique epoch identifier.
        epoch_name:
          type: string
          title: Epoch Name
          description: Name of the study epoch (e.g., Treatment, Follow-up).
        sequence:
          type: integer
          title: Sequence
          description: Sequence number of the epoch.
        arm_id:
          anyOf:
            - type: string
            - type: "null"
          title: Arm Id
          description: Optional associated arm ID.
      type: object
      required:
        - epoch_id
        - epoch_name
        - sequence
      title: SoAHeaderEpoch
      description: Presentation header representing a trial Study Epoch.
    SoALinkResponse:
      properties:
        status:
          type: string
          title: Status
          default: success
        message:
          type: string
          title: Message
          default: Link established successfully
      type: object
      title: SoALinkResponse
    SoAMatrixView:
      properties:
        epochs:
          items:
            $ref: "#/components/schemas/SoAHeaderEpoch"
          type: array
          title: Epochs
          description: Ordered list of Study Epoch columns.
        encounters:
          items:
            $ref: "#/components/schemas/SoAHeaderEncounter"
          type: array
          title: Encounters
          description: Ordered list of Encounter/Visit sub-columns.
        rows:
          items:
            $ref: "#/components/schemas/SoARowView"
          type: array
          title: Rows
          description: Ordered list of row-wise activity procedures.
        arms:
          items:
            $ref: "#/components/schemas/SoAHeaderArm"
          type: array
          title: Arms
          description: Ordered list of Study Arm columns.
      type: object
      title: SoAMatrixView
      description: Presentation view of the Schedule of Activities (SoA) matrix table.
    SoARowView:
      properties:
        activity_id:
          type: string
          title: Activity Id
          description: Unique activity/procedure identifier.
        activity_name:
          type: string
          title: Activity Name
          description: Name or label of the activity/procedure.
        cells:
          items:
            $ref: "#/components/schemas/SoACellView"
          type: array
          title: Cells
          description: Applicability cell mapping for each encounter column.
        derived_from_soa:
          type: boolean
          title: Derived From Soa
          description: Flag indicating selective lineage.
          default: false
      type: object
      required:
        - activity_id
        - activity_name
      title: SoARowView
      description: A single row in the SoA matrix table representing a specific activity and its cell mappings.
    StudyAlignmentReport:
      properties:
        study_id:
          type: string
          title: Study Id
        complete_activities:
          items:
            $ref: "#/components/schemas/ActivityReport"
          type: array
          title: Complete Activities
        incomplete_activities:
          items:
            $ref: "#/components/schemas/ActivityReport"
          type: array
          title: Incomplete Activities
        unmapped_activities:
          items:
            $ref: "#/components/schemas/ActivityReport"
          type: array
          title: Unmapped Activities
        unmapped_odm_items:
          items:
            additionalProperties: true
            type: object
          type: array
          title: Unmapped Odm Items
        unmapped_crf_item_values:
          items:
            additionalProperties: true
            type: object
          type: array
          title: Unmapped Crf Item Values
      type: object
      required:
        - study_id
        - complete_activities
        - incomplete_activities
        - unmapped_activities
        - unmapped_odm_items
        - unmapped_crf_item_values
      title: StudyAlignmentReport
      description: "Comprehensive alignment report analyzing the mapping between study epochs and CRFs.\n\nAttributes:\n    study_id: The unique identifier of the study being evaluated.\n    complete_activities: Activities where all required items are mapped successfully.\n    incomplete_activities: Activities with partially mapped items.\n    unmapped_activities: Activities completely lacking any mapped items.\n    unmapped_odm_items: ODM nodes present but not associated with any active activity item.\n    unmapped_crf_item_values: CRF items/values present but not associated with any activity definition."
    StudyArmProperties:
      properties:
        name:
          type: string
          minLength: 1
          title: Name
          description: The name of the study arm, e.g., 'Active' or 'Placebo'.
        type:
          type: string
          minLength: 1
          title: Type
          description: The classification type of the arm.
        sequence:
          anyOf:
            - type: integer
              minimum: 1.0
            - type: "null"
          title: Sequence
          description: Sequential ordering rank.
      type: object
      required:
        - name
        - type
      title: StudyArmProperties
      description: Properties specific to a clinical trial Study Arm.
    StudyTerminologyValidationReport:
      properties:
        study_id:
          type: string
          title: Study Id
        is_valid:
          type: boolean
          title: Is Valid
        total_concepts:
          type: integer
          title: Total Concepts
        valid_count:
          type: integer
          title: Valid Count
        invalid_count:
          type: integer
          title: Invalid Count
        degraded_count:
          type: integer
          title: Degraded Count
        concepts:
          items:
            $ref: "#/components/schemas/ConceptValidationReport"
          type: array
          title: Concepts
      type: object
      required:
        - study_id
        - is_valid
        - total_concepts
        - valid_count
        - invalid_count
        - degraded_count
        - concepts
      title: StudyTerminologyValidationReport
      description: Aggregated terminology validation report for an entire study structure.
    Suggestion:
      properties:
        suggestion_id:
          type: string
          title: Suggestion Id
          description: Unique suggestion identifier.
        block_id:
          type: string
          title: Block Id
          description: Anchor block identifier.
        study_id:
          type: string
          title: Study Id
          description: Associated study identifier.
        suggested_text:
          type: string
          title: Suggested Text
          description: Proposed replacement text.
        original_text:
          type: string
          title: Original Text
          description: Original block text at proposed time.
        status:
          $ref: "#/components/schemas/SuggestionStatus"
          description: Current suggestion status.
          default: pending
        created_by:
          type: string
          title: Created By
          description: Proposer user ID.
        created_at:
          type: string
          title: Created At
          description: Creation timestamp.
        reason:
          type: string
          title: Reason
          description: Rationale for the suggestion.
        decision_reason:
          anyOf:
            - type: string
            - type: "null"
          title: Decision Reason
          description: Rationale for acceptance or rejection.
        decided_by:
          anyOf:
            - type: string
            - type: "null"
          title: Decided By
          description: User ID of decider.
        decided_at:
          anyOf:
            - type: string
            - type: "null"
          title: Decided At
          description: Timestamp of decision.
        block_version_index:
          type: integer
          title: Block Version Index
          description: The block's version_index at the time of proposing.
        version_index:
          type: integer
          title: Version Index
          description: Sequential version index.
          default: 1
      type: object
      required:
        - suggestion_id
        - block_id
        - study_id
        - suggested_text
        - original_text
        - created_by
        - reason
        - block_version_index
      title: Suggestion
      description: Represents a proposed collaborative content replacement suggestion for a block.
    SuggestionCreate:
      properties:
        suggested_text:
          type: string
          title: Suggested Text
        reason:
          type: string
          title: Reason
      type: object
      required:
        - suggested_text
        - reason
      title: SuggestionCreate
    SuggestionDecisionRequest:
      properties:
        decision:
          type: string
          enum:
            - accept
            - reject
          title: Decision
        decision_reason:
          type: string
          title: Decision Reason
      type: object
      required:
        - decision
        - decision_reason
      title: SuggestionDecisionRequest
    SuggestionStatus:
      type: string
      enum:
        - pending
        - accepted
        - rejected
      title: SuggestionStatus
      description: Statuses for suggestion workflows.
    SynopsisExportRequest:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Unique protocol study identifier
        format:
          type: string
          title: Format
          description: "Target export format: 'pdf', 'docx', or 'html'"
          default: pdf
        creator:
          anyOf:
            - type: string
            - type: "null"
          title: Creator
          description: Author or creator username
          default: Cadence Clinical DDF Engine
        change_reason:
          anyOf:
            - type: string
            - type: "null"
          title: Change Reason
          description: GxP 21 CFR Part 11 change reason
          default: Initial Baseline
      type: object
      required:
        - study_id
      title: SynopsisExportRequest
      description: "Request payload for exporting a clinical protocol synopsis.


        Requirements: PRD-SYS-001"
    SynopsisExportResponse:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Protocol study identifier
        format:
          type: string
          title: Format
          description: Export format
        content_base64:
          type: string
          title: Content Base64
          description: Base64 encoded binary document stream
        filename:
          type: string
          title: Filename
          description: Generated export filename
      type: object
      required:
        - study_id
        - format
        - content_base64
        - filename
      title: SynopsisExportResponse
      description:
        "Response payload containing base64 encoded document export stream.


        Requirements: PRD-SYS-001"
    TerminologyConcept:
      properties:
        code:
          type: string
          title: Code
        decode:
          type: string
          title: Decode
        system:
          type: string
          title: System
        valid:
          type: boolean
          title: Valid
      type: object
      required:
        - code
        - decode
        - system
        - valid
      title: TerminologyConcept
      description: Normalized terminology concept details.
    TerminologyEnum:
      type: string
      enum:
        - SNOMED-CT
        - LOINC
        - MedDRA
        - WHODrug
        - NCI
        - CDISC-CT
      title: TerminologyEnum
    TerminologySearchResponse:
      properties:
        query:
          type: string
          title: Query
        state:
          $ref: "#/components/schemas/CodeValidationState"
        results:
          items:
            $ref: "#/components/schemas/TerminologyConcept"
          type: array
          title: Results
        total_results:
          type: integer
          title: Total Results
        error_message:
          anyOf:
            - type: string
            - type: "null"
          title: Error Message
      type: object
      required:
        - query
        - state
        - results
        - total_results
      title: TerminologySearchResponse
      description: Response model for search and autocomplete queries.
    TimingWindowProperties:
      properties:
        name:
          type: string
          minLength: 1
          title: Name
          description: Label or duration specification of the timing window.
        anchor_reference:
          anyOf:
            - type: string
            - type: "null"
          title: Anchor Reference
          description: Anchor reference, e.g. a visit name.
        target_day:
          anyOf:
            - type: integer
            - type: "null"
          title: Target Day
          description: Target scheduled day.
        min_offset:
          anyOf:
            - type: integer
            - type: "null"
          title: Min Offset
          description: Minimum day offset.
        max_offset:
          anyOf:
            - type: integer
            - type: "null"
          title: Max Offset
          description: Maximum day offset.
        conditional:
          anyOf:
            - type: boolean
            - type: "null"
          title: Conditional
          description: Flag indicating if the timing or applicability is conditional.
        reason:
          anyOf:
            - type: string
              minLength: 1
            - type: "null"
          title: Reason
          description: Mandatory justification reason required if conditional is True.
      type: object
      required:
        - name
      title: TimingWindowProperties
      description: Properties specific to a Timing Window. Enforces cross-field conditional justification.
    TransitionItemRequest:
      properties:
        status:
          type: string
          title: Status
        reason:
          type: string
          title: Reason
        name:
          anyOf:
            - type: string
            - type: "null"
          title: Name
        label:
          anyOf:
            - type: string
            - type: "null"
          title: Label
        value:
          anyOf:
            - type: string
            - type: "null"
          title: Value
      type: object
      required:
        - status
        - reason
      title: TransitionItemRequest
    UpdateArmRequest:
      properties:
        reason_for_change:
          type: string
          title: Reason For Change
          description: Mandatory reason for change / audit trail justification.
        object_type:
          type: string
          const: ARM
          title: Object Type
          default: ARM
        payload:
          $ref: "#/components/schemas/ArmPayload"
      type: object
      required:
        - reason_for_change
        - payload
      title: UpdateArmRequest
      description: Request model for updating an Arm library object.
    UpdateBlockRequest:
      properties:
        properties:
          additionalProperties: true
          type: object
          title: Properties
        change_reason:
          anyOf:
            - type: string
            - type: "null"
          title: Change Reason
      type: object
      required:
        - properties
      title: UpdateBlockRequest
    UpdateConceptRequest:
      properties:
        display_name:
          type: string
          title: Display Name
        definition:
          type: string
          title: Definition
        cdash_mapping:
          anyOf:
            - $ref: "#/components/schemas/CDASHMapping"
            - type: "null"
        allowable_units:
          anyOf:
            - items:
                $ref: "#/components/schemas/AllowableUnit"
              type: array
            - type: "null"
          title: Allowable Units
        reason_for_change:
          type: string
          title: Reason For Change
      type: object
      required:
        - display_name
        - definition
        - reason_for_change
      title: UpdateConceptRequest
    UpdateDataElementRequest:
      properties:
        reason_for_change:
          type: string
          title: Reason For Change
          description: Mandatory reason for change / audit trail justification.
        object_type:
          type: string
          const: DATA_ELEMENT
          title: Object Type
          default: DATA_ELEMENT
        payload:
          $ref: "#/components/schemas/DataElementPayload"
      type: object
      required:
        - reason_for_change
        - payload
      title: UpdateDataElementRequest
      description: Request model for updating a Data Element library object.
    UpdateEligibilityCriterionRequest:
      properties:
        criterion_type:
          type: string
          enum:
            - inclusion
            - exclusion
          title: Criterion Type
          description: Whether this is an inclusion or exclusion criterion.
        description:
          type: string
          title: Description
          description: Human-readable text description of the criterion.
        dsl_source:
          type: string
          title: Dsl Source
          description: The raw DSL statement source, e.g., 'eCRF.DM.AGE >= 18'.
        expected_outcome:
          type: boolean
          title: Expected Outcome
          description: Expected Boolean outcome of evaluating the condition node.
          default: true
        change_reason:
          type: string
          title: Change Reason
          description: Reason for updating this criterion.
      type: object
      required:
        - criterion_type
        - description
        - dsl_source
        - change_reason
      title: UpdateEligibilityCriterionRequest
    UpdateEpochRequest:
      properties:
        properties:
          $ref: "#/components/schemas/EpochProperties"
        reason_for_change:
          type: string
          title: Reason For Change
          description: Reason for change for audit trail
          default: Updated epoch
      type: object
      required:
        - properties
      title: UpdateEpochRequest
    UpdateFormRequest:
      properties:
        reason_for_change:
          type: string
          title: Reason For Change
          description: Mandatory reason for change / audit trail justification.
        object_type:
          type: string
          const: FORM
          title: Object Type
          default: FORM
        payload:
          $ref: "#/components/schemas/FormPayload"
      type: object
      required:
        - reason_for_change
        - payload
      title: UpdateFormRequest
      description: Request model for updating a Form library object.
    UpdateLibraryInstanceRequest:
      properties:
        payload:
          additionalProperties: true
          type: object
          title: Payload
          description: The complete updated payload of the library instance.
      type: object
      required:
        - payload
      title: UpdateLibraryInstanceRequest
    UpdateProcedureRequest:
      properties:
        properties:
          $ref: "#/components/schemas/ProcedureProperties"
        reason_for_change:
          type: string
          title: Reason For Change
          description: Reason for change for audit trail
          default: Updated procedure
      type: object
      required:
        - properties
      title: UpdateProcedureRequest
    UpdateStudyArmRequest:
      properties:
        properties:
          $ref: "#/components/schemas/StudyArmProperties"
        reason_for_change:
          type: string
          title: Reason For Change
          description: Reason for change for audit trail
          default: Updated study arm
      type: object
      required:
        - properties
      title: UpdateStudyArmRequest
    UpdateTimingWindowRequest:
      properties:
        properties:
          $ref: "#/components/schemas/TimingWindowProperties"
        reason_for_change:
          type: string
          title: Reason For Change
          description: Reason for change for audit trail
          default: Updated timing window
      type: object
      required:
        - properties
      title: UpdateTimingWindowRequest
    ValidationError:
      properties:
        loc:
          items:
            anyOf:
              - type: string
              - type: integer
          type: array
          title: Location
        msg:
          type: string
          title: Message
        type:
          type: string
          title: Error Type
        input:
          title: Input
        ctx:
          type: object
          title: Context
      type: object
      required:
        - loc
        - msg
        - type
      title: ValidationError
    VersionDiffResponse:
      properties:
        added_nodes:
          items:
            $ref: "#/components/schemas/DifferenceResult"
          type: array
          title: Added Nodes
        modified_nodes:
          items:
            $ref: "#/components/schemas/DifferenceResult"
          type: array
          title: Modified Nodes
        deleted_nodes:
          items:
            $ref: "#/components/schemas/DifferenceResult"
          type: array
          title: Deleted Nodes
      type: object
      required:
        - added_nodes
        - modified_nodes
        - deleted_nodes
      title: VersionDiffResponse
    VisitAttributes:
      properties:
        visit_type:
          type: string
          title: Visit Type
          description: The scheduling type of visit (e.g., SCREENING, SCHEDULED, UNSCHEDULED).
        planned_day:
          type: integer
          title: Planned Day
          description: Target timeline day relative to randomization/enrollment.
        window_days:
          type: integer
          title: Window Days
          description: "Allowable margin of days around the planned day (e.g., \xB13 days)."
      type: object
      required:
        - visit_type
        - planned_day
        - window_days
      title: VisitAttributes
      description: Attributes defining a study visit.
    VisitLibraryObjectDetail:
      properties:
        id:
          type: string
          title: Id
          description: Stable, unique global library ID.
        version:
          type: string
          title: Version
          description: Semantic version of the library object.
        status:
          $ref: "#/components/schemas/LibraryStatus"
          description: Workflow review status of the object.
        sponsor_id:
          type: string
          title: Sponsor Id
          description: Sponsor identifier.
        tenant_id:
          type: string
          title: Tenant Id
          description: Tenant / Partition identifier.
        created_at:
          type: string
          format: date-time
          title: Created At
          description: Audit timestamp of creation.
        created_by:
          type: string
          title: Created By
          description: User ID who created this object.
        updated_at:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Updated At
          description: Audit timestamp of last update.
        updated_by:
          anyOf:
            - type: string
            - type: "null"
          title: Updated By
          description: User ID of last updater.
        reason_for_change:
          anyOf:
            - type: string
            - type: "null"
          title: Reason For Change
          description: Detailed explanation of changes applied.
        prior_status:
          anyOf:
            - type: string
            - type: "null"
          title: Prior Status
          description: Previous status before transition.
        object_type:
          type: string
          const: VISIT
          title: Object Type
          default: VISIT
        payload:
          $ref: "#/components/schemas/VisitPayload"
      type: object
      required:
        - id
        - version
        - status
        - sponsor_id
        - tenant_id
        - created_at
        - created_by
        - payload
      title: VisitLibraryObjectDetail
      description: Response model for a Visit library object.
    VisitPayload:
      properties:
        attributes:
          $ref: "#/components/schemas/VisitAttributes"
          description: Clinical visit configurations.
      type: object
      required:
        - attributes
      title: VisitPayload
      description: Visit-specific payload validation containing visit attributes.
    VisitProperties:
      properties:
        name:
          anyOf:
            - type: string
              minLength: 1
            - type: "null"
          title: Name
          description: The display name of the visit.
        encounter_name:
          anyOf:
            - type: string
              minLength: 1
            - type: "null"
          title: Encounter Name
          description: Alternate/legacy field name for encounter/visit.
        sequence:
          type: integer
          minimum: 1.0
          title: Sequence
          description: Sequential ordering rank of the visit.
      type: object
      required:
        - sequence
      title: VisitProperties
      description: Properties specific to a Visit / Encounter.
    VisitReorderItem:
      properties:
        visit_id:
          type: string
          minLength: 1
          title: Visit Id
          description: Unique identifier for the visit.
        sequence:
          type: integer
          minimum: 1.0
          title: Sequence
          description: New sequential order rank of the visit.
      type: object
      required:
        - visit_id
        - sequence
      title: VisitReorderItem
      description: Represents a visit id and its new sequence value.
    VisitReorderRequest:
      properties:
        visits:
          items:
            $ref: "#/components/schemas/VisitReorderItem"
          type: array
          title: Visits
          description: Ordered list of visit sequence updates.
      type: object
      required:
        - visits
      title: VisitReorderRequest
      description: Request contract carrying an ordered list of visit ID and sequence value pairs.
    VisitToArmAssignmentRequest:
      properties:
        arm_id:
          type: string
          minLength: 1
          title: Arm Id
        visit_ids:
          items:
            type: string
          type: array
          minItems: 1
          title: Visit Ids
      type: object
      required:
        - arm_id
        - visit_ids
      title: VisitToArmAssignmentRequest
    VisitToEpochAssignmentRequest:
      properties:
        epoch_id:
          type: string
          minLength: 1
          title: Epoch Id
        visit_ids:
          items:
            type: string
          type: array
          minItems: 1
          title: Visit Ids
      type: object
      required:
        - epoch_id
        - visit_ids
      title: VisitToEpochAssignmentRequest
    apps__designer__domain__protocol_authoring__soa__CreateVisitRequest:
      properties:
        id:
          type: string
          minLength: 1
          title: Id
          description: Unique identifier for the visit.
        properties:
          $ref: "#/components/schemas/VisitProperties"
        change_reason:
          type: string
          title: Change Reason
          description: Change reason for audit trail
          default: Created visit
      type: object
      required:
        - id
        - properties
      title: CreateVisitRequest
    apps__designer__domain__protocol_authoring__soa__UpdateVisitRequest:
      properties:
        properties:
          $ref: "#/components/schemas/VisitProperties"
        reason_for_change:
          type: string
          title: Reason For Change
          description: Reason for change for audit trail
          default: Updated visit
      type: object
      required:
        - properties
      title: UpdateVisitRequest
    apps__designer__library__CreateVisitRequest:
      properties:
        id:
          type: string
          title: Id
          description: Stable, unique global library ID.
        version:
          type: string
          title: Version
          description: Initial version code.
          default: 1.0.0
        status:
          $ref: "#/components/schemas/LibraryStatus"
          description: Initial library state.
          default: DRAFT
        sponsor_id:
          type: string
          title: Sponsor Id
          description: Sponsor / Tenant identifier.
        change_reason:
          type: string
          title: Change Reason
          description: Mandatory reason for change / audit trail justification.
        object_type:
          type: string
          const: VISIT
          title: Object Type
          default: VISIT
        payload:
          $ref: "#/components/schemas/VisitPayload"
      type: object
      required:
        - id
        - sponsor_id
        - change_reason
        - payload
      title: CreateVisitRequest
      description: Request model for creating a Visit library object.
    apps__designer__library__UpdateVisitRequest:
      properties:
        reason_for_change:
          type: string
          title: Reason For Change
          description: Mandatory reason for change / audit trail justification.
        object_type:
          type: string
          const: VISIT
          title: Object Type
          default: VISIT
        payload:
          $ref: "#/components/schemas/VisitPayload"
      type: object
      required:
        - reason_for_change
        - payload
      title: UpdateVisitRequest
      description: Request model for updating a Visit library object.
    AddDOAAssignmentRequest:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Target protocol study ID
        site_id:
          type: string
          title: Site Id
          description: Target investigator site ID
        personnel_name:
          type: string
          title: Personnel Name
          description: Full legal name
        personnel_email:
          type: string
          title: Personnel Email
          description: Email address
        role:
          $ref: "#/components/schemas/DOATaskRoleEnum"
          description: Site role
        delegated_tasks:
          items:
            $ref: "#/components/schemas/DOATaskDelegationEnum"
          type: array
          title: Delegated Tasks
          description: List of delegated tasks
        start_date:
          type: string
          title: Start Date
          description: Delegation start date (YYYY-MM-DD)
      type: object
      required:
        - study_id
        - site_id
        - personnel_name
        - personnel_email
        - role
        - delegated_tasks
        - start_date
      title: AddDOAAssignmentRequest
      description:
        "Request payload to add site personnel task delegation record.


        Requirements: PRD-SYS-001"
    ApproveDelegationRequest:
      properties:
        delegation_id:
          type: string
          title: Delegation Id
        pi_user_id:
          type: string
          title: Pi User Id
        password:
          type: string
          title: Password
        totp_code:
          anyOf:
            - type: string
            - type: "null"
          title: Totp Code
      type: object
      required:
        - delegation_id
        - pi_user_id
        - password
      title: ApproveDelegationRequest
    ApproveTaskDelegationRequest:
      properties:
        delegation_id:
          type: string
          title: Delegation Id
        pi_user_id:
          type: string
          title: Pi User Id
        signature_hash:
          type: string
          title: Signature Hash
        reason_for_change:
          type: string
          title: Reason For Change
      type: object
      required:
        - delegation_id
        - pi_user_id
        - signature_hash
        - reason_for_change
      title: ApproveTaskDelegationRequest
    BatchSignOffRequest:
      properties:
        study_id:
          type: string
          title: Study Id
        target_type:
          type: string
          title: Target Type
        target_ids:
          items:
            type: string
          type: array
          title: Target Ids
        signing_reason:
          type: string
          title: Signing Reason
      type: object
      required:
        - study_id
        - target_type
        - target_ids
        - signing_reason
      title: BatchSignOffRequest
    BatchSignOffResponse:
      properties:
        status:
          type: string
          title: Status
        approved_submission_ids:
          items:
            type: string
          type: array
          title: Approved Submission Ids
        skipped_submission_ids:
          items:
            type: string
          type: array
          title: Skipped Submission Ids
        skipped_targets:
          items:
            type: string
          type: array
          title: Skipped Targets
      type: object
      required:
        - status
        - approved_submission_ids
        - skipped_submission_ids
        - skipped_targets
      title: BatchSignOffResponse
    BatchSignatureRequest:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Target protocol study ID
        subject_id:
          type: string
          title: Subject Id
          description: Target subject ID
        target_type:
          type: string
          title: Target Type
          description: "Target artifact type: FORM, CASEBOOK, DOC"
          default: FORM
        target_ids:
          items:
            type: string
          type: array
          title: Target Ids
          description: List of target artifact IDs
        target_form_ids:
          items:
            type: string
          type: array
          title: Target Form Ids
          description: List of eCRF form IDs to sign
        signing_reason:
          type: string
          title: Signing Reason
          description: 21 CFR Part 11 signature purpose/meaning
        password:
          type: string
          title: Password
          description: Re-authentication password for identity confirmation
        printed_name:
          type: string
          title: Printed Name
          description: Printed full name of Principal Investigator
      type: object
      required:
        - study_id
        - subject_id
        - signing_reason
        - password
        - printed_name
      title: BatchSignatureRequest
      description:
        "Request payload for Principal Investigator batch eSignature casebook sign-off.


        Requirements: PRD-SYS-001"
    BatchSignatureResponse:
      properties:
        signature_id:
          type: string
          title: Signature Id
          description: Unique cryptographic signature record ID
        study_id:
          type: string
          title: Study Id
          description: Target study ID
        subject_id:
          type: string
          title: Subject Id
          description: Target subject ID
        signed_forms_count:
          type: integer
          title: Signed Forms Count
          description: Total number of signed eCRF forms
        content_digest:
          type: string
          title: Content Digest
          description: SHA-256 digest of signed casebook data
        timestamp_utc:
          type: string
          title: Timestamp Utc
          description: UTC ISO timestamp of signature execution
        audit_tx:
          type: string
          title: Audit Tx
          description: Immutable GxP audit ledger transaction ID
      type: object
      required:
        - signature_id
        - study_id
        - subject_id
        - signed_forms_count
        - content_digest
        - timestamp_utc
        - audit_tx
      title: BatchSignatureResponse
      description:
        "Response payload following successful batch eSignature execution.


        Requirements: PRD-SYS-001"
    Body_import_dictionary_api_v1_dictionaries_import_post:
      properties:
        dictionary_type:
          $ref: "#/components/schemas/DictTypeEnum"
        version:
          type: string
          title: Version
        files:
          type: string
          contentMediaType: application/octet-stream
          title: Files
        parse_multilingual:
          type: boolean
          title: Parse Multilingual
          default: true
      type: object
      required:
        - dictionary_type
        - version
        - files
      title: Body_import_dictionary_api_v1_dictionaries_import_post
    Body_upload_document_api_v1_documents_upload_post:
      properties:
        file:
          type: string
          contentMediaType: application/octet-stream
          title: File
        dia_tmf_code:
          type: string
          title: Dia Tmf Code
        reason_for_change:
          type: string
          title: Reason For Change
      type: object
      required:
        - file
        - dia_tmf_code
        - reason_for_change
      title: Body_upload_document_api_v1_documents_upload_post
    BulkQueryGenerationRequest:
      properties:
        study_id:
          anyOf:
            - type: string
            - type: "null"
          title: Study Id
          description: Target protocol study ID
        site_id:
          anyOf:
            - type: string
            - type: "null"
          title: Site Id
          description: Optional target site identifier
        subject_id:
          anyOf:
            - type: string
            - type: "null"
          title: Subject Id
          description: Optional target subject identifier
        targets:
          items:
            $ref: "#/components/schemas/QueryTargetDescriptor"
          type: array
          title: Targets
          description: List of query target coordinate fields and explanations
        reason_for_change:
          type: string
          title: Reason For Change
          description: Mandatory GxP 21 CFR Part 11 justification reason
      type: object
      required:
        - targets
        - reason_for_change
      title: BulkQueryGenerationRequest
      description: "Request payload to execute bulk clinical query generation.


        Requirements: PRD-SYS-001"
    BulkQueryGenerationResponse:
      properties:
        batch_id:
          anyOf:
            - type: string
            - type: "null"
          title: Batch Id
          description: Unique bulk query batch identifier
        audit_tx:
          anyOf:
            - type: string
            - type: "null"
          title: Audit Tx
          description: Immutable GxP audit ledger transaction ID
        generated_count:
          anyOf:
            - type: integer
            - type: "null"
          title: Generated Count
          description: Legacy generated query count
        generated_query_ids:
          items:
            type: string
          type: array
          title: Generated Query Ids
          description: List of generated unique query IDs
        skipped_targets:
          items:
            $ref: "#/components/schemas/QueryTargetDescriptor"
          type: array
          title: Skipped Targets
          description: List of target descriptors that were skipped due to already having an active query
        timestamp_utc:
          anyOf:
            - type: string
            - type: "null"
          title: Timestamp Utc
          description: UTC ISO timestamp of query generation execution
      type: object
      required:
        - generated_query_ids
      title: BulkQueryGenerationResponse
      description: "Response payload following bulk query generation execution.


        Requirements: PRD-SYS-001"
    BulkSdvSignOffRequest:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Target protocol study ID
        subject_id:
          type: string
          title: Subject Id
          description: Target subject ID
        scope:
          type: string
          title: Scope
          description: "SDV scope boundary: FIELD, PAGE, or VISIT"
        target_ids:
          items:
            type: string
          type: array
          title: Target Ids
          description: List of target database or artifact IDs corresponding to the scope
        reason_for_change:
          type: string
          title: Reason For Change
          description: Mandatory GxP 21 CFR Part 11 justification reason
        site_id:
          anyOf:
            - type: string
            - type: "null"
          title: Site Id
          description: Optional site identifier for the targets
        signing_reason:
          type: string
          title: Signing Reason
          description: GxP Part 11 signature meaning or reason
          default: CRA/monitor-gated bulk SDV sign-off
      type: object
      required:
        - study_id
        - subject_id
        - scope
        - target_ids
        - reason_for_change
      title: BulkSdvSignOffRequest
      description: "Request payload to execute bulk SDV sign-offs.


        Requirements: PRD-SYS-001"
    BulkSdvSignOffResponse:
      properties:
        bulk_id:
          anyOf:
            - type: string
            - type: "null"
          title: Bulk Id
          description: Unique bulk signature operation identifier
        content_digest:
          type: string
          title: Content Digest
          description: SHA-256 digest of bulk signed data
        timestamp_utc:
          type: string
          title: Timestamp Utc
          description: UTC ISO timestamp of signature execution
        audit_tx:
          type: string
          title: Audit Tx
          description: Immutable GxP audit ledger transaction ID
        verified_count:
          anyOf:
            - type: integer
            - type: "null"
          title: Verified Count
          description: Total number of successfully verified SDV items
        verified_target_ids:
          anyOf:
            - items:
                type: string
              type: array
            - type: "null"
          title: Verified Target Ids
          description: List of target IDs that were successfully signed
        skipped_targets:
          anyOf:
            - items:
                additionalProperties: true
                type: object
              type: array
            - type: "null"
          title: Skipped Targets
          description: List of skipped targets with details on skip reasons
        signed_count:
          type: integer
          title: Signed Count
          description: Total number of successfully signed SDV items
        signed_target_ids:
          items:
            type: string
          type: array
          title: Signed Target Ids
          description: List of target IDs that were successfully signed
        skipped_target_ids:
          items:
            type: string
          type: array
          title: Skipped Target Ids
          description: List of target IDs that were skipped or already signed
      type: object
      required:
        - content_digest
        - timestamp_utc
        - audit_tx
        - signed_count
        - signed_target_ids
        - skipped_target_ids
      title: BulkSdvSignOffResponse
      description: "Response payload following bulk SDV sign-off execution.


        Requirements: PRD-SYS-001"
    ClinicalQueryResponse:
      properties:
        id:
          type: string
          title: Id
        study_id:
          type: string
          title: Study Id
        subject_id:
          type: string
          title: Subject Id
        visit_id:
          anyOf:
            - type: string
            - type: "null"
          title: Visit Id
        domain:
          anyOf:
            - type: string
            - type: "null"
          title: Domain
        test_code:
          type: string
          title: Test Code
        status:
          type: string
          title: Status
        explanation:
          anyOf:
            - type: string
            - type: "null"
          title: Explanation
        response:
          anyOf:
            - type: string
            - type: "null"
          title: Response
        created_at:
          type: string
          format: date-time
          title: Created At
        updated_at:
          type: string
          format: date-time
          title: Updated At
        history:
          items:
            $ref: "#/components/schemas/QueryHistoryItem"
          type: array
          title: History
          default: []
        observation_id:
          anyOf:
            - type: string
            - type: "null"
          title: Observation Id
        field_link:
          anyOf:
            - type: string
            - type: "null"
          title: Field Link
        message:
          anyOf:
            - type: string
            - type: "null"
          title: Message
        origin:
          anyOf:
            - type: string
            - type: "null"
          title: Origin
        priority:
          anyOf:
            - type: string
            - type: "null"
          title: Priority
        rule_id:
          anyOf:
            - type: string
            - type: "null"
          title: Rule Id
        created_by:
          anyOf:
            - type: string
            - type: "null"
          title: Created By
        responder:
          anyOf:
            - type: string
            - type: "null"
          title: Responder
        resolver:
          anyOf:
            - type: string
            - type: "null"
          title: Resolver
        resolved_at:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Resolved At
        cancellation_reason:
          anyOf:
            - type: string
            - type: "null"
          title: Cancellation Reason
        escalated_at:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Escalated At
        form_id:
          anyOf:
            - type: string
            - type: "null"
          title: Form Id
        field_id:
          anyOf:
            - type: string
            - type: "null"
          title: Field Id
        query_type:
          anyOf:
            - type: string
            - type: "null"
          title: Query Type
        action_required:
          anyOf:
            - type: string
            - type: "null"
          title: Action Required
      type: object
      required:
        - id
        - study_id
        - subject_id
        - test_code
        - status
        - created_at
        - updated_at
      title: ClinicalQueryResponse
      description: Pydantic schema returning query details and full audit history.
    CoderActionRequest:
      properties:
        action:
          type: string
          title: Action
        code:
          anyOf:
            - type: string
            - type: "null"
          title: Code
        term:
          anyOf:
            - type: string
            - type: "null"
          title: Term
        suggestion_index:
          anyOf:
            - type: integer
            - type: "null"
          title: Suggestion Index
        reason_for_change:
          anyOf:
            - type: string
            - type: "null"
          title: Reason For Change
      type: object
      required:
        - action
      title: CoderActionRequest
    CodingAssignmentResponse:
      properties:
        id:
          type: string
          title: Id
        verbatim_text:
          type: string
          title: Verbatim Text
        source_field:
          anyOf:
            - type: string
            - type: "null"
          title: Source Field
        observation_id:
          anyOf:
            - type: string
            - type: "null"
          title: Observation Id
        dictionary_type:
          type: string
          title: Dictionary Type
        dictionary_version:
          type: string
          title: Dictionary Version
        coded_code:
          anyOf:
            - type: string
            - type: "null"
          title: Coded Code
        coded_term:
          anyOf:
            - type: string
            - type: "null"
          title: Coded Term
        status:
          type: string
          title: Status
        recoding_status:
          type: string
          title: Recoding Status
        assigned_by:
          anyOf:
            - type: string
            - type: "null"
          title: Assigned By
        assigned_at:
          type: string
          format: date-time
          title: Assigned At
        score:
          anyOf:
            - type: number
            - type: "null"
          title: Score
        hierarchy:
          anyOf:
            - additionalProperties: true
              type: object
            - items: {}
              type: array
            - type: "null"
          title: Hierarchy
        suggestions:
          anyOf:
            - items: {}
              type: array
            - additionalProperties: true
              type: object
            - type: "null"
          title: Suggestions
        domain:
          anyOf:
            - type: string
            - type: "null"
          title: Domain
        version:
          type: integer
          title: Version
        is_deleted:
          type: boolean
          title: Is Deleted
      type: object
      required:
        - id
        - verbatim_text
        - dictionary_type
        - dictionary_version
        - status
        - recoding_status
        - assigned_at
        - version
        - is_deleted
      title: CodingAssignmentResponse
    ComplianceStatusResponse:
      properties:
        study_id:
          type: string
          title: Study Id
        site_id:
          anyOf:
            - type: string
            - type: "null"
          title: Site Id
        milestone:
          type: string
          title: Milestone
        is_complete:
          type: boolean
          title: Is Complete
        missing_documents:
          items:
            type: string
          type: array
          title: Missing Documents
      type: object
      required:
        - study_id
        - site_id
        - milestone
        - is_complete
        - missing_documents
      title: ComplianceStatusResponse
    CriterionLevelResult:
      properties:
        criterion_id:
          type: string
          title: Criterion Id
        criterion_type:
          type: string
          title: Criterion Type
        description:
          type: string
          title: Description
        dsl_source:
          type: string
          title: Dsl Source
        is_met:
          type: boolean
          title: Is Met
        is_indeterminate:
          type: boolean
          title: Is Indeterminate
      type: object
      required:
        - criterion_id
        - criterion_type
        - description
        - dsl_source
        - is_met
        - is_indeterminate
      title: CriterionLevelResult
      description: Pydantic schema for individual criterion level evaluation result.
    CustodianEnum:
      type: string
      enum:
        - Lead Unblinded Statistician
        - IDMC
      title: CustodianEnum
      description: "Enumeration of the two permissible dual-custody key holders.\n\nThe Shamir secret-sharing scheme used for emergency unblinding mandates\nthat exactly one share comes from each of these two custodians.  Any\nother custodian identity is rejected with a 422 validation error before\nthe request reaches the cryptographic layer.\n\nAttributes:\n    LEAD_UNBLINDED_STATISTICIAN: The lead unblinded statistician who holds\n        one half of the Shamir key share.\n    IDMC: The Independent Data Monitoring Committee representative who holds\n        the second half of the Shamir key share."
    CustodianShare:
      properties:
        custodian:
          $ref: "#/components/schemas/CustodianEnum"
        version:
          type: integer
          title: Version
        x:
          type: integer
          exclusiveMinimum: 0.0
          title: X
          description: Shamir x-coordinate; must be > 0
        y:
          type: integer
          minimum: 0.0
          title: Y
          description: Shamir y-coordinate; must be >= 0
      type: object
      required:
        - custodian
        - version
        - x
        - y
      title: CustodianShare
      description: "A single custodian's Shamir secret share for dual-custody unblinding.\n\nBoth shares must be present in the request body before the encrypted\nallocation record can be reconstructed.  Field constraints are enforced\nat the schema boundary so malformed shares produce structured 422\nresponses rather than opaque crypto-layer failures.\n\nAttributes:\n    custodian: The identity of the key custodian; must be one of the two\n        approved dual-custody holders defined by ``CustodianEnum``.\n    version: The version of the key material associated with this share;\n        used to select the correct key generation from the database.\n    x: The x-coordinate of the Shamir share point; must be strictly\n        positive (> 0) as required by the polynomial reconstruction.\n    y: The y-coordinate of the Shamir share point; must be non-negative\n        (>= 0) and less than the prime modulus used by the crypto layer."
    DOAAssignmentRecord:
      properties:
        record_id:
          type: string
          title: Record Id
          description: Unique DOA record identifier
        study_id:
          type: string
          title: Study Id
          description: Target protocol study ID
        site_id:
          type: string
          title: Site Id
          description: Target investigator site ID
        personnel_name:
          type: string
          title: Personnel Name
          description: Full legal name of site personnel
        personnel_email:
          type: string
          title: Personnel Email
          description: Email address of site personnel
        role:
          $ref: "#/components/schemas/DOATaskRoleEnum"
          description: Site personnel role
        delegated_tasks:
          items:
            $ref: "#/components/schemas/DOATaskDelegationEnum"
          type: array
          title: Delegated Tasks
          description: List of delegated study tasks
        start_date:
          type: string
          title: Start Date
          description: Task delegation start date (YYYY-MM-DD)
        end_date:
          anyOf:
            - type: string
            - type: "null"
          title: End Date
          description: Optional task delegation end date
        is_active:
          type: boolean
          title: Is Active
          description: True if assignment is active
          default: true
        signed_off:
          type: boolean
          title: Signed Off
          description: True if eSignature endorsed by PI
          default: false
      type: object
      required:
        - record_id
        - study_id
        - site_id
        - personnel_name
        - personnel_email
        - role
        - delegated_tasks
        - start_date
      title: DOAAssignmentRecord
      description:
        "Delegation of Authority (DOA) site personnel assignment log record.


        Requirements: PRD-SYS-001"
    DOAAuditLogResponse:
      properties:
        id:
          type: string
          title: Id
        user_id:
          type: string
          title: User Id
        action:
          type: string
          title: Action
        details:
          type: string
          title: Details
        timestamp:
          type: string
          format: date-time
          title: Timestamp
      type: object
      required:
        - id
        - user_id
        - action
        - details
        - timestamp
      title: DOAAuditLogResponse
    DOADelegationRecordResponse:
      properties:
        id:
          type: string
          title: Id
        site_id:
          type: string
          title: Site Id
        staff_user_id:
          type: string
          title: Staff User Id
        task_code:
          type: string
          title: Task Code
        pi_user_id:
          anyOf:
            - type: string
            - type: "null"
          title: Pi User Id
        status:
          type: string
          title: Status
        pi_signature_hash:
          anyOf:
            - type: string
            - type: "null"
          title: Pi Signature Hash
        pi_approved_at:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Pi Approved At
        end_date:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: End Date
        reason_for_change:
          anyOf:
            - type: string
            - type: "null"
          title: Reason For Change
        is_active:
          type: boolean
          title: Is Active
      type: object
      required:
        - id
        - site_id
        - staff_user_id
        - task_code
        - status
        - is_active
      title: DOADelegationRecordResponse
    DOASignOffRequest:
      properties:
        record_id:
          type: string
          title: Record Id
          description: Target DOA record ID
        reason_for_change:
          type: string
          title: Reason For Change
          description: Mandatory GxP 21 CFR Part 11 justification
      type: object
      required:
        - record_id
        - reason_for_change
      title: DOASignOffRequest
      description: "Request payload for PI eSignature endorsement of DOA record.


        Requirements: PRD-SYS-001"
    DOATaskDelegationEnum:
      type: string
      enum:
        - SUBJECT_INFORMED_CONSENT
        - PHYSICAL_EXAMINATION
        - AE_SAE_REPORTING
        - CRF_DATA_ENTRY
        - PI_CASEBOOK_SIGNOFF
      title: DOATaskDelegationEnum
      description:
        "Specific clinical trial study tasks delegated to site personnel.


        Requirements: PRD-SYS-001"
    DOATaskRoleEnum:
      type: string
      enum:
        - PRINCIPAL_INVESTIGATOR
        - SUB_INVESTIGATOR
        - CLINICAL_RESEARCH_COORDINATOR
        - STUDY_NURSE
        - DATA_MANAGER
      title: DOATaskRoleEnum
      description: "Site personnel roles on Delegation of Authority log.


        Requirements: PRD-SYS-001"
    DataLockRecord:
      properties:
        lock_id:
          type: string
          title: Lock Id
          description: Unique data lock record identifier
        study_id:
          type: string
          title: Study Id
          description: Target protocol study ID
        subject_id:
          type: string
          title: Subject Id
          description: Target clinical trial subject ID
        form_id:
          type: string
          title: Form Id
          description: Target eCRF form submission ID
        item_group_id:
          anyOf:
            - type: string
            - type: "null"
          title: Item Group Id
          description: Optional target item group code
        field_name:
          anyOf:
            - type: string
            - type: "null"
          title: Field Name
          description: Optional target field variable name
        scope:
          $ref: "#/components/schemas/LockScopeEnum"
          description: "Lock scope: FORM, ITEM_GROUP, FIELD"
        status:
          $ref: "#/components/schemas/LockStatusEnum"
          description: "Lock status: UNLOCKED, FROZEN, LOCKED"
          default: LOCKED
        locked_by:
          type: string
          title: Locked By
          description: User ID who executed data lock
        reason_for_change:
          type: string
          title: Reason For Change
          description: GxP 21 CFR Part 11 justification reason
        locked_at:
          type: string
          title: Locked At
          description: UTC ISO timestamp of lock execution
      type: object
      required:
        - lock_id
        - study_id
        - subject_id
        - form_id
        - scope
        - locked_by
        - reason_for_change
        - locked_at
      title: DataLockRecord
      description:
        "Data lock state record representing frozen or locked clinical eCRF data.


        Requirements: PRD-SYS-001"
    DataLockRequest:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Target protocol study ID
        subject_id:
          type: string
          title: Subject Id
          description: Target subject ID
        form_id:
          type: string
          title: Form Id
          description: Target eCRF form ID
        item_group_id:
          anyOf:
            - type: string
            - type: "null"
          title: Item Group Id
          description: Optional target item group code
        field_name:
          anyOf:
            - type: string
            - type: "null"
          title: Field Name
          description: Optional target field variable name
        scope:
          $ref: "#/components/schemas/LockScopeEnum"
          description: "Lock scope: FORM, ITEM_GROUP, FIELD"
          default: FORM
        action:
          type: string
          title: Action
          description: "Action to perform: LOCK, FREEZE, UNLOCK"
          default: LOCK
        reason_for_change:
          type: string
          title: Reason For Change
          description: Mandatory GxP 21 CFR Part 11 justification reason
      type: object
      required:
        - study_id
        - subject_id
        - form_id
        - reason_for_change
      title: DataLockRequest
      description:
        "Request payload to execute form, item group, or field-level data locking.


        Requirements: PRD-SYS-001"
    DataLockResponse:
      properties:
        lock_id:
          type: string
          title: Lock Id
          description: Lock record identifier
        status:
          type: string
          title: Status
          description: "Resulting status: LOCKED, FROZEN, UNLOCKED"
        message:
          type: string
          title: Message
          description: Operation result confirmation message
        record:
          $ref: "#/components/schemas/DataLockRecord"
          description: Updated data lock record
      type: object
      required:
        - lock_id
        - status
        - message
        - record
      title: DataLockResponse
      description: "Response payload for data lock/unlock operations.


        Requirements: PRD-SYS-001"
    DelegateTaskRequest:
      properties:
        site_id:
          type: string
          title: Site Id
        staff_user_id:
          type: string
          title: Staff User Id
        task_code:
          type: string
          title: Task Code
        pi_user_id:
          type: string
          title: Pi User Id
        reason_for_change:
          type: string
          title: Reason For Change
      type: object
      required:
        - site_id
        - staff_user_id
        - task_code
        - pi_user_id
        - reason_for_change
      title: DelegateTaskRequest
    Demographics:
      properties:
        name:
          anyOf:
            - type: string
            - type: "null"
          title: Name
        birthdate:
          anyOf:
            - type: string
            - type: "null"
          title: Birthdate
        gender:
          anyOf:
            - type: string
            - type: "null"
          title: Gender
        race:
          anyOf:
            - type: string
            - type: "null"
          title: Race
      type: object
      title: Demographics
      description: Pydantic schema representing demographic details.
    DictTypeEnum:
      type: string
      enum:
        - MEDDRA
        - WHODRUG
        - LOINC
        - SNOMED
      title: DictTypeEnum
    DispenseRequest:
      properties:
        study_id:
          type: string
          title: Study Id
        site_id:
          type: string
          title: Site Id
        subject_id:
          type: string
          title: Subject Id
        visit_id:
          type: string
          title: Visit Id
        kit_id:
          type: string
          title: Kit Id
        quantity:
          type: integer
          minimum: 1.0
          title: Quantity
          default: 1
      type: object
      required:
        - study_id
        - site_id
        - subject_id
        - visit_id
        - kit_id
      title: DispenseRequest
    DispenseResponse:
      properties:
        status:
          type: string
          title: Status
        message:
          type: string
          title: Message
        resupply_triggered:
          type: boolean
          title: Resupply Triggered
      type: object
      required:
        - status
        - message
        - resupply_triggered
      title: DispenseResponse
    DocumentMetadataResponse:
      properties:
        document_id:
          type: string
          title: Document Id
        filename:
          type: string
          title: Filename
        version_index:
          type: string
          title: Version Index
        sha256_hash:
          type: string
          title: Sha256 Hash
        dia_tmf_code:
          type: string
          title: Dia Tmf Code
        status:
          type: string
          title: Status
        created_by:
          type: string
          title: Created By
        created_at:
          type: string
          format: date-time
          title: Created At
      type: object
      required:
        - document_id
        - filename
        - version_index
        - sha256_hash
        - dia_tmf_code
        - status
        - created_by
        - created_at
      title: DocumentMetadataResponse
      description: "Schema representing complete document metadata.


        Requirements: PRD-SYS-001"
    DocumentUploadResponse:
      properties:
        document_id:
          type: string
          title: Document Id
        filename:
          type: string
          title: Filename
        version_index:
          type: string
          title: Version Index
        sha256_hash:
          type: string
          title: Sha256 Hash
      type: object
      required:
        - document_id
        - filename
        - version_index
        - sha256_hash
      title: DocumentUploadResponse
      description: "Schema representing upload response.


        Requirements: PRD-SYS-001"
    EISFDocumentRecord:
      properties:
        document_id:
          type: string
          title: Document Id
          description: Unique eISF document identifier
        study_id:
          type: string
          title: Study Id
          description: Target protocol study ID
        site_id:
          type: string
          title: Site Id
          description: Target investigator site ID (for site-scoped isolation)
        category:
          $ref: "#/components/schemas/EISFTaxonomyCategoryEnum"
          description: DIA taxonomy category
        title:
          type: string
          title: Title
          description: Human-readable document title
        version:
          type: string
          title: Version
          description: Document version string
          default: "1.0"
        file_name:
          type: string
          title: File Name
          description: Original uploaded filename
        file_size_bytes:
          type: integer
          title: File Size Bytes
          description: File size in bytes
        sha256_hash:
          type: string
          title: Sha256 Hash
          description: SHA-256 integrity checksum hex string
        uploaded_by:
          type: string
          title: Uploaded By
          description: User ID of uploader
        uploaded_at:
          type: string
          title: Uploaded At
          description: UTC ISO timestamp of upload
        expiration_date:
          anyOf:
            - type: string
            - type: "null"
          title: Expiration Date
          description: Optional document expiration date (YYYY-MM-DD)
        is_redacted:
          type: boolean
          title: Is Redacted
          description: True if document contains non-destructive PHI redactions
          default: false
      type: object
      required:
        - document_id
        - study_id
        - site_id
        - category
        - title
        - file_name
        - file_size_bytes
        - sha256_hash
        - uploaded_by
        - uploaded_at
      title: EISFDocumentRecord
      description: "eISF regulatory binder document metadata record.


        Requirements: PRD-SYS-001"
    EISFTaxonomyCategoryEnum:
      type: string
      enum:
        - 1_INVESTIGATOR_CV
        - 2_MEDICAL_LICENSE
        - 3_PROTOCOL_APPROVAL
        - 4_IRB_IEC_APPROVAL
        - 5_INFORMED_CONSENT
        - 6_FINANCIAL_DISCLOSURE
        - 7_DELEGATION_OF_AUTHORITY
        - 8_SAFETY_REPORT
      title: EISFTaxonomyCategoryEnum
      description: "DIA eISF / Regulatory Binder document taxonomy categories.


        Requirements: PRD-SYS-001"
    ETMFCompletenessWebhookPayload:
      properties:
        study_id:
          type: string
          title: Study Id
        site_id:
          anyOf:
            - type: string
            - type: "null"
          title: Site Id
        milestone:
          type: string
          title: Milestone
        is_complete:
          type: boolean
          title: Is Complete
        missing_artifacts:
          items:
            type: string
          type: array
          title: Missing Artifacts
      type: object
      required:
        - study_id
        - milestone
        - is_complete
      title: ETMFCompletenessWebhookPayload
    FormSubmissionApprove:
      properties:
        signature_manifest:
          additionalProperties: true
          type: object
          title: Signature Manifest
        signing_reason:
          type: string
          title: Signing Reason
      type: object
      required:
        - signature_manifest
        - signing_reason
      title: FormSubmissionApprove
    FormSubmissionCreate:
      properties:
        study_id:
          type: string
          title: Study Id
        site_id:
          type: string
          title: Site Id
        subject_id:
          type: string
          title: Subject Id
        visit_id:
          anyOf:
            - type: string
            - type: "null"
          title: Visit Id
        form_id:
          type: string
          title: Form Id
        protocol_version:
          anyOf:
            - type: string
            - type: "null"
          title: Protocol Version
        payload:
          anyOf:
            - additionalProperties: true
              type: object
            - type: "null"
          title: Payload
      type: object
      required:
        - study_id
        - site_id
        - subject_id
        - form_id
      title: FormSubmissionCreate
    FormSubmissionResponse:
      properties:
        id:
          type: string
          title: Id
        study_id:
          type: string
          title: Study Id
        site_id:
          anyOf:
            - type: string
            - type: "null"
          title: Site Id
        subject_id:
          type: string
          title: Subject Id
        visit_id:
          anyOf:
            - type: string
            - type: "null"
          title: Visit Id
        form_id:
          type: string
          title: Form Id
        status:
          $ref: "#/components/schemas/FormSubmissionStatusEnum"
        version:
          type: integer
          title: Version
        is_deleted:
          type: boolean
          title: Is Deleted
        signature_manifest:
          anyOf:
            - additionalProperties: true
              type: object
            - type: "null"
          title: Signature Manifest
        protocol_version:
          anyOf:
            - type: string
            - type: "null"
          title: Protocol Version
        payload:
          anyOf:
            - additionalProperties: true
              type: object
            - type: "null"
          title: Payload
        is_active:
          type: boolean
          title: Is Active
        is_readonly:
          type: boolean
          title: Is Readonly
        cloned_from_id:
          anyOf:
            - type: string
            - type: "null"
          title: Cloned From Id
      type: object
      required:
        - id
        - study_id
        - site_id
        - subject_id
        - form_id
        - status
        - version
        - is_deleted
        - is_active
        - is_readonly
      title: FormSubmissionResponse
    FormSubmissionStatusEnum:
      type: string
      enum:
        - DRAFT
        - COMPLETED
        - APPROVED
      title: FormSubmissionStatusEnum
    GenerateAuditorTokenRequest:
      properties:
        auditor_email:
          type: string
          title: Auditor Email
          description: Target auditor email address
        study_id:
          type: string
          title: Study Id
          description: Target protocol study ID
        duration_hours:
          type: integer
          title: Duration Hours
          description: Token validity in hours
          default: 24
        reason_for_access:
          type: string
          title: Reason For Access
          description: GxP reason for provisioning auditor access
      type: object
      required:
        - auditor_email
        - study_id
        - reason_for_access
      title: GenerateAuditorTokenRequest
      description:
        "Request payload to generate a temporary auditor access token.


        Requirements: PRD-SYS-001"
    ImpactAnalysisRequest:
      properties:
        dictionary_type:
          $ref: "#/components/schemas/DictTypeEnum"
        new_version:
          type: string
          title: New Version
      type: object
      required:
        - dictionary_type
        - new_version
      title: ImpactAnalysisRequest
    ImpactAnalysisResponse:
      properties:
        status:
          type: string
          const: success
          title: Status
        dictionary_type:
          $ref: "#/components/schemas/DictTypeEnum"
        new_version:
          type: string
          title: New Version
        metrics:
          $ref: "#/components/schemas/ImpactMetrics"
      type: object
      required:
        - status
        - dictionary_type
        - new_version
        - metrics
      title: ImpactAnalysisResponse
    ImpactMetrics:
      properties:
        unchanged:
          type: integer
          title: Unchanged
          default: 0
        reclassified:
          type: integer
          title: Reclassified
          default: 0
        deprecated:
          type: integer
          title: Deprecated
          default: 0
        skipped:
          type: integer
          title: Skipped
          default: 0
        verbatim_terms_affected:
          anyOf:
            - type: integer
            - type: "null"
          title: Verbatim Terms Affected
        coded_terms_affected:
          anyOf:
            - type: integer
            - type: "null"
          title: Coded Terms Affected
        uncodable_terms:
          anyOf:
            - type: integer
            - type: "null"
          title: Uncodable Terms
      type: object
      title: ImpactMetrics
    JobStatusEnum:
      type: string
      enum:
        - PENDING
        - PROCESSING
        - COMPLETED
        - FAILED
      title: JobStatusEnum
    JobStatusResponse:
      properties:
        job_id:
          type: string
          title: Job Id
        dictionary_type:
          type: string
          title: Dictionary Type
        version:
          type: string
          title: Version
        status:
          $ref: "#/components/schemas/JobStatusEnum"
        started_at:
          type: string
          format: date-time
          title: Started At
        completed_at:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Completed At
        progress_percentage:
          anyOf:
            - type: integer
            - type: "null"
          title: Progress Percentage
        records_imported:
          anyOf:
            - type: integer
            - type: "null"
          title: Records Imported
        errors_encountered:
          anyOf:
            - type: integer
            - type: "null"
          title: Errors Encountered
      type: object
      required:
        - job_id
        - dictionary_type
        - version
        - status
        - started_at
      title: JobStatusResponse
    LabRangeRecalculateRequest:
      properties:
        study_id:
          type: string
          title: Study Id
        test_code:
          type: string
          title: Test Code
      type: object
      required:
        - study_id
        - test_code
      title: LabRangeRecalculateRequest
      description: Pydantic schema for triggering lab range recalculations.
    LabRangeRecalculateResponse:
      properties:
        status:
          type: string
          title: Status
        study_id:
          type: string
          title: Study Id
        test_code:
          type: string
          title: Test Code
        updated_count:
          type: integer
          title: Updated Count
      type: object
      required:
        - status
        - study_id
        - test_code
        - updated_count
      title: LabRangeRecalculateResponse
      description: Pydantic schema returning recalculation status.
    LabReferenceRangeCreate:
      properties:
        study_id:
          type: string
          title: Study Id
        test_code:
          type: string
          title: Test Code
        test_name:
          type: string
          title: Test Name
        source:
          type: string
          title: Source
        site_id:
          anyOf:
            - type: string
            - type: "null"
          title: Site Id
        unit:
          type: string
          title: Unit
        normalized_unit:
          type: string
          title: Normalized Unit
        sex_applicability:
          type: string
          title: Sex Applicability
        age_low:
          anyOf:
            - type: number
            - type: "null"
          title: Age Low
        age_high:
          anyOf:
            - type: number
            - type: "null"
          title: Age High
        low_bound:
          anyOf:
            - type: number
            - type: "null"
          title: Low Bound
        high_bound:
          anyOf:
            - type: number
            - type: "null"
          title: High Bound
        critical_low:
          anyOf:
            - type: number
            - type: "null"
          title: Critical Low
        critical_high:
          anyOf:
            - type: number
            - type: "null"
          title: Critical High
      type: object
      required:
        - study_id
        - test_code
        - test_name
        - source
        - unit
        - normalized_unit
        - sex_applicability
      title: LabReferenceRangeCreate
      description: Pydantic schema for creating a reference range.
    LabReferenceRangeResponse:
      properties:
        id:
          type: string
          title: Id
        study_id:
          type: string
          title: Study Id
        test_code:
          type: string
          title: Test Code
        test_name:
          type: string
          title: Test Name
        source:
          type: string
          title: Source
        site_id:
          anyOf:
            - type: string
            - type: "null"
          title: Site Id
        unit:
          type: string
          title: Unit
        normalized_unit:
          type: string
          title: Normalized Unit
        sex_applicability:
          type: string
          title: Sex Applicability
        age_low:
          anyOf:
            - type: number
            - type: "null"
          title: Age Low
        age_high:
          anyOf:
            - type: number
            - type: "null"
          title: Age High
        low_bound:
          anyOf:
            - type: number
            - type: "null"
          title: Low Bound
        high_bound:
          anyOf:
            - type: number
            - type: "null"
          title: High Bound
        critical_low:
          anyOf:
            - type: number
            - type: "null"
          title: Critical Low
        critical_high:
          anyOf:
            - type: number
            - type: "null"
          title: Critical High
        version:
          type: integer
          title: Version
        is_deleted:
          type: boolean
          title: Is Deleted
      type: object
      required:
        - id
        - study_id
        - test_code
        - test_name
        - source
        - unit
        - normalized_unit
        - sex_applicability
        - version
        - is_deleted
      title: LabReferenceRangeResponse
      description: Pydantic schema for returning reference range details.
    LabReferenceRangeUpdate:
      properties:
        study_id:
          anyOf:
            - type: string
            - type: "null"
          title: Study Id
        test_code:
          anyOf:
            - type: string
            - type: "null"
          title: Test Code
        test_name:
          anyOf:
            - type: string
            - type: "null"
          title: Test Name
        source:
          anyOf:
            - type: string
            - type: "null"
          title: Source
        site_id:
          anyOf:
            - type: string
            - type: "null"
          title: Site Id
        unit:
          anyOf:
            - type: string
            - type: "null"
          title: Unit
        normalized_unit:
          anyOf:
            - type: string
            - type: "null"
          title: Normalized Unit
        sex_applicability:
          anyOf:
            - type: string
            - type: "null"
          title: Sex Applicability
        age_low:
          anyOf:
            - type: number
            - type: "null"
          title: Age Low
        age_high:
          anyOf:
            - type: number
            - type: "null"
          title: Age High
        low_bound:
          anyOf:
            - type: number
            - type: "null"
          title: Low Bound
        high_bound:
          anyOf:
            - type: number
            - type: "null"
          title: High Bound
        critical_low:
          anyOf:
            - type: number
            - type: "null"
          title: Critical Low
        critical_high:
          anyOf:
            - type: number
            - type: "null"
          title: Critical High
      type: object
      title: LabReferenceRangeUpdate
      description: Pydantic schema for updating a reference range.
    LocalLedgerBlock:
      properties:
        index:
          type: integer
          title: Index
        timestamp:
          type: string
          format: date-time
          title: Timestamp
        action:
          type: string
          title: Action
        details:
          $ref: "#/components/schemas/SyncBlockDetails"
        reason:
          type: string
          title: Reason
        prevHash:
          type: string
          title: Prevhash
        hash:
          type: string
          title: Hash
      type: object
      required:
        - index
        - timestamp
        - action
        - details
        - reason
        - prevHash
        - hash
      title: LocalLedgerBlock
      description: Pydantic schema representing a cryptographically chained offline ledger block.
    LockScopeEnum:
      type: string
      enum:
        - FORM
        - ITEM_GROUP
        - FIELD
      title: LockScopeEnum
      description: "Granular lock scope boundaries.


        Requirements: PRD-SYS-001"
    LockStatusEnum:
      type: string
      enum:
        - UNLOCKED
        - FROZEN
        - LOCKED
      title: LockStatusEnum
      description: "Data lock lifecycle status.


        Requirements: PRD-SYS-001"
    LockStatusResponse:
      properties:
        locked_sites:
          items:
            type: string
          type: array
          title: Locked Sites
        locked_visits:
          items:
            type: string
          type: array
          title: Locked Visits
        locked_forms:
          items:
            type: string
          type: array
          title: Locked Forms
        locked_subjects:
          items:
            type: string
          type: array
          title: Locked Subjects
        trial_locked:
          type: boolean
          title: Trial Locked
      type: object
      required:
        - locked_sites
        - locked_visits
        - locked_forms
        - locked_subjects
        - trial_locked
      title: LockStatusResponse
      description: Pydantic model representing the active locking/freezing state of the system.
    MedDRACodeLookupResponse:
      properties:
        status:
          type: string
          enum:
            - AUTO-CODED
            - SUGGESTIONS
            - UNCODABLE
          title: Status
        matches:
          items:
            $ref: "#/components/schemas/MedDRAMatch"
          type: array
          title: Matches
      type: object
      required:
        - status
        - matches
      title: MedDRACodeLookupResponse
    MedDRAMatch:
      properties:
        llt_code:
          type: string
          title: Llt Code
        llt_name:
          type: string
          title: Llt Name
        pt_code:
          type: string
          title: Pt Code
        pt_name:
          type: string
          title: Pt Name
        hlt_code:
          type: string
          title: Hlt Code
        hlt_name:
          type: string
          title: Hlt Name
        hlgt_code:
          type: string
          title: Hlgt Code
        hlgt_name:
          type: string
          title: Hlgt Name
        soc_code:
          type: string
          title: Soc Code
        soc_name:
          type: string
          title: Soc Name
        primary_soc_flag:
          anyOf:
            - type: string
            - type: "null"
          title: Primary Soc Flag
        score:
          type: number
          title: Score
      type: object
      required:
        - llt_code
        - llt_name
        - pt_code
        - pt_name
        - hlt_code
        - hlt_name
        - hlgt_code
        - hlgt_name
        - soc_code
        - soc_name
        - score
      title: MedDRAMatch
    MedDRATargetLevelEnum:
      type: string
      enum:
        - LLT
        - PT
      title: MedDRATargetLevelEnum
    MigrationRuleCreate:
      properties:
        study_id:
          type: string
          title: Study Id
        source_version:
          type: string
          title: Source Version
        target_version:
          type: string
          title: Target Version
        rule_type:
          type: string
          title: Rule Type
        source_field:
          anyOf:
            - type: string
            - type: "null"
          title: Source Field
        target_field:
          anyOf:
            - type: string
            - type: "null"
          title: Target Field
        default_value_string:
          anyOf:
            - type: string
            - type: "null"
          title: Default Value String
        default_value_float:
          anyOf:
            - type: number
            - type: "null"
          title: Default Value Float
      type: object
      required:
        - study_id
        - source_version
        - target_version
        - rule_type
      title: MigrationRuleCreate
    MigrationRuleResponse:
      properties:
        id:
          type: string
          title: Id
        study_id:
          type: string
          title: Study Id
        source_version:
          type: string
          title: Source Version
        target_version:
          type: string
          title: Target Version
        rule_type:
          type: string
          title: Rule Type
        source_field:
          anyOf:
            - type: string
            - type: "null"
          title: Source Field
        target_field:
          anyOf:
            - type: string
            - type: "null"
          title: Target Field
        default_value_string:
          anyOf:
            - type: string
            - type: "null"
          title: Default Value String
        default_value_float:
          anyOf:
            - type: number
            - type: "null"
          title: Default Value Float
      type: object
      required:
        - id
        - study_id
        - source_version
        - target_version
        - rule_type
      title: MigrationRuleResponse
    ObservationCreate:
      properties:
        subject_id:
          type: string
          title: Subject Id
        study_id:
          anyOf:
            - type: string
            - type: "null"
          title: Study Id
        visit_id:
          anyOf:
            - type: string
            - type: "null"
          title: Visit Id
        domain:
          type: string
          title: Domain
        test_code:
          type: string
          title: Test Code
        test_name:
          type: string
          title: Test Name
        value:
          anyOf:
            - type: number
            - type: "null"
          title: Value
        value_string:
          anyOf:
            - type: string
            - type: "null"
          title: Value String
        unit:
          anyOf:
            - type: string
            - type: "null"
          title: Unit
        observation_date:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Observation Date
        lab_source:
          anyOf:
            - type: string
            - type: "null"
          title: Lab Source
        lab_site_id:
          anyOf:
            - type: string
            - type: "null"
          title: Lab Site Id
      type: object
      required:
        - subject_id
        - domain
        - test_code
        - test_name
      title: ObservationCreate
      description: Pydantic schema for creating a clinical observation.
    ObservationResponse:
      properties:
        id:
          type: string
          title: Id
        subject_id:
          type: string
          title: Subject Id
        study_id:
          type: string
          title: Study Id
        visit_id:
          anyOf:
            - type: string
            - type: "null"
          title: Visit Id
        domain:
          type: string
          title: Domain
        observation_date:
          type: string
          format: date-time
          title: Observation Date
        test_code:
          type: string
          title: Test Code
        test_name:
          type: string
          title: Test Name
        value:
          anyOf:
            - type: number
            - type: "null"
          title: Value
        value_string:
          anyOf:
            - type: string
            - type: "null"
          title: Value String
        unit:
          anyOf:
            - type: string
            - type: "null"
          title: Unit
        normalized_value:
          anyOf:
            - type: number
            - type: "null"
          title: Normalized Value
        normalized_unit:
          anyOf:
            - type: string
            - type: "null"
          title: Normalized Unit
        is_outlier:
          type: boolean
          title: Is Outlier
        lab_source:
          anyOf:
            - type: string
            - type: "null"
          title: Lab Source
        lab_site_id:
          anyOf:
            - type: string
            - type: "null"
          title: Lab Site Id
        lab_indicator:
          anyOf:
            - type: string
            - type: "null"
          title: Lab Indicator
        lab_out_of_range:
          anyOf:
            - type: boolean
            - type: "null"
          title: Lab Out Of Range
        matched_normal_bounds:
          anyOf:
            - type: string
            - type: "null"
          title: Matched Normal Bounds
        range_indicator:
          anyOf:
            - type: string
            - type: "null"
          title: Range Indicator
        is_out_of_range:
          anyOf:
            - type: boolean
            - type: "null"
          title: Is Out Of Range
        reference_range_low:
          anyOf:
            - type: number
            - type: "null"
          title: Reference Range Low
        reference_range_high:
          anyOf:
            - type: number
            - type: "null"
          title: Reference Range High
        protocol_version_tag:
          anyOf:
            - type: string
            - type: "null"
          title: Protocol Version Tag
        protocol_version_index:
          anyOf:
            - type: integer
            - type: "null"
          title: Protocol Version Index
      type: object
      required:
        - id
        - subject_id
        - study_id
        - domain
        - observation_date
        - test_code
        - test_name
        - is_outlier
      title: ObservationResponse
      description: Pydantic schema returning observation details.
    OfflineBatchSyncRequest:
      properties:
        client_batch_id:
          type: string
          title: Client Batch Id
          description: Unique client-supplied batch identifier for idempotency
        device_id:
          type: string
          title: Device Id
          description: Identifier of the device performing the sync
        deltas:
          items:
            $ref: "#/components/schemas/OfflineDeltaItem"
          type: array
          title: Deltas
          description: List of sync deltas to process
      type: object
      required:
        - client_batch_id
        - device_id
        - deltas
      title: OfflineBatchSyncRequest
      description: "Request schema for offline batch delta sync.


        Requirements: PRD-SYS-001"
    OfflineBatchSyncResponse:
      properties:
        client_batch_id:
          type: string
          title: Client Batch Id
          description: Unique client-supplied batch identifier
        status:
          type: string
          enum:
            - SUCCESS
            - PARTIAL_SUCCESS
            - ALREADY_PROCESSED
          title: Status
          description: Processing status of the batch
        processed_count:
          type: integer
          title: Processed Count
          description: Number of successfully processed deltas
        conflicts:
          items:
            additionalProperties: true
            type: object
          type: array
          title: Conflicts
          description: List of conflicts encountered during processing
      type: object
      required:
        - client_batch_id
        - status
        - processed_count
      title: OfflineBatchSyncResponse
      description: "Response schema for offline batch delta sync.


        Requirements: PRD-SYS-001"
    OfflineDeltaItem:
      properties:
        delta_id:
          type: string
          title: Delta Id
          description: Unique delta identifier
        entity_type:
          type: string
          title: Entity Type
          description: Type of mutated entity
        entity_id:
          type: string
          title: Entity Id
          description: Unique ID of mutated entity
        action:
          type: string
          enum:
            - CREATE
            - UPDATE
            - SUBMIT
          title: Action
          description: Mutation action type
        payload:
          additionalProperties: true
          type: object
          title: Payload
          description: Payload data for the entity
        client_timestamp_utc:
          type: string
          title: Client Timestamp Utc
          description: UTC timestamp of the mutation on client
        reason_for_change:
          type: string
          title: Reason For Change
          description: Reason for the mutation change
      type: object
      required:
        - delta_id
        - entity_type
        - entity_id
        - action
        - payload
        - client_timestamp_utc
        - reason_for_change
      title: OfflineDeltaItem
      description:
        "An individual sync delta item representing an entity mutation.


        Requirements: PRD-SYS-001"
    OutlierRecalculateRequest:
      properties:
        study_id:
          type: string
          title: Study Id
        test_code:
          type: string
          title: Test Code
      type: object
      required:
        - study_id
        - test_code
      title: OutlierRecalculateRequest
      description: Pydantic schema for triggering outlier calculations.
    OutlierRecalculateResponse:
      properties:
        status:
          type: string
          title: Status
        study_id:
          type: string
          title: Study Id
        test_code:
          type: string
          title: Test Code
        outliers_found:
          type: integer
          title: Outliers Found
      type: object
      required:
        - status
        - study_id
        - test_code
        - outliers_found
      title: OutlierRecalculateResponse
      description: Pydantic schema returning recalculation status.
    PHIScanRequest:
      properties:
        text:
          type: string
          title: Text
          description: Document text content to scan for PHI
      type: object
      required:
        - text
      title: PHIScanRequest
      description: "Request payload to scan text for HIPAA 18 PHI identifiers.


        Requirements: PRD-SYS-001"
    ProtocolVersionRefDTO:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Unique identifier of the clinical study (e.g. 'STUDY-101').
        version_tag:
          type: string
          title: Version Tag
          description: Semantic or alphanumeric version tag (e.g. '1.0', 'v2.1').
        version_index:
          type: integer
          title: Version Index
          description: Chronological, incrementing version index (must be >= 1).
        status:
          $ref: "#/components/schemas/ProtocolVersionStatusEnum"
          description: Current controlled status of protocol version.
      type: object
      required:
        - study_id
        - version_tag
        - version_index
        - status
      title: ProtocolVersionRefDTO
      description: Local Execution ACL DTO representing a reference to a specific protocol version.
    ProtocolVersionStatusEnum:
      type: string
      enum:
        - DRAFT
        - ACTIVE
        - LOCKED
        - PUBLISHED
        - ARCHIVED
        - FROZEN
      title: ProtocolVersionStatusEnum
      description: Controlled status vocabulary for protocol version references.
    PublishAmendmentRequest:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Target protocol study ID
        version_number:
          type: string
          title: Version Number
          description: Amended protocol version string (e.g. 2.0)
        description:
          type: string
          title: Description
          description: Amendment summary description
        baseline_snapshot:
          additionalProperties: true
          type: object
          title: Baseline Snapshot
          description: Previous USDM snapshot
        amended_snapshot:
          additionalProperties: true
          type: object
          title: Amended Snapshot
          description: New amended USDM snapshot
      type: object
      required:
        - study_id
        - version_number
        - description
        - baseline_snapshot
        - amended_snapshot
      title: PublishAmendmentRequest
      description: "Request payload to publish a new protocol amendment version.


        Requirements: PRD-SYS-001"
    PublishAmendmentResponse:
      properties:
        amendment_id:
          type: string
          title: Amendment Id
          description: Unique amendment publication ID
        study_id:
          type: string
          title: Study Id
          description: Target study ID
        version_number:
          type: string
          title: Version Number
          description: Published version string
        published_at:
          type: string
          title: Published At
          description: UTC ISO publication timestamp
        summary_of_changes:
          type: string
          title: Summary Of Changes
          description: Human-readable summary of changes
        added_activities_count:
          type: integer
          title: Added Activities Count
          description: Number of added activities
        removed_activities_count:
          type: integer
          title: Removed Activities Count
          description: Number of removed activities
      type: object
      required:
        - amendment_id
        - study_id
        - version_number
        - published_at
        - summary_of_changes
        - added_activities_count
        - removed_activities_count
      title: PublishAmendmentResponse
      description: "Response payload following protocol amendment publishing.


        Requirements: PRD-SYS-001"
    QueryCancel:
      properties:
        reason:
          type: string
          title: Reason
      type: object
      required:
        - reason
      title: QueryCancel
      description: Pydantic schema for cancelling a query with a reason.
    QueryCreate:
      properties:
        study_id:
          type: string
          title: Study Id
        subject_id:
          type: string
          title: Subject Id
        visit_id:
          anyOf:
            - type: string
            - type: "null"
          title: Visit Id
        domain:
          anyOf:
            - type: string
            - type: "null"
          title: Domain
        test_code:
          type: string
          title: Test Code
        explanation:
          type: string
          title: Explanation
        status:
          anyOf:
            - type: string
            - type: "null"
          title: Status
          default: OPEN
        observation_id:
          anyOf:
            - type: string
            - type: "null"
          title: Observation Id
        field_link:
          anyOf:
            - type: string
            - type: "null"
          title: Field Link
        message:
          anyOf:
            - type: string
            - type: "null"
          title: Message
        origin:
          anyOf:
            - type: string
            - type: "null"
          title: Origin
        priority:
          anyOf:
            - type: string
            - type: "null"
          title: Priority
        rule_id:
          anyOf:
            - type: string
            - type: "null"
          title: Rule Id
        created_by:
          anyOf:
            - type: string
            - type: "null"
          title: Created By
        form_id:
          anyOf:
            - type: string
            - type: "null"
          title: Form Id
        field_id:
          anyOf:
            - type: string
            - type: "null"
          title: Field Id
        query_type:
          anyOf:
            - type: string
            - type: "null"
          title: Query Type
        action_required:
          anyOf:
            - type: string
            - type: "null"
          title: Action Required
      type: object
      required:
        - study_id
        - subject_id
        - test_code
        - explanation
      title: QueryCreate
      description: Pydantic schema for raising a new query.
    QueryHistoryItem:
      properties:
        action:
          type: string
          title: Action
        user_id:
          anyOf:
            - type: string
            - type: "null"
          title: User Id
        timestamp:
          type: string
          format: date-time
          title: Timestamp
        old_values:
          anyOf:
            - additionalProperties: true
              type: object
            - type: "null"
          title: Old Values
        new_values:
          anyOf:
            - additionalProperties: true
              type: object
            - type: "null"
          title: New Values
        change_reason:
          anyOf:
            - type: string
            - type: "null"
          title: Change Reason
        version_index:
          type: integer
          title: Version Index
      type: object
      required:
        - action
        - timestamp
        - version_index
      title: QueryHistoryItem
      description: Pydantic schema representing a single audited event in query history.
    QueryReopen:
      properties:
        reason:
          anyOf:
            - type: string
            - type: "null"
          title: Reason
      type: object
      title: QueryReopen
      description: Pydantic schema for reopening a query with a reason.
    QueryRespond:
      properties:
        response:
          type: string
          title: Response
        responder:
          anyOf:
            - type: string
            - type: "null"
          title: Responder
      type: object
      required:
        - response
      title: QueryRespond
      description: Pydantic schema for responding to an open query.
    QueryTargetDescriptor:
      properties:
        study_id:
          anyOf:
            - type: string
            - type: "null"
          title: Study Id
          description: Target study trial identifier
        subject_id:
          type: string
          title: Subject Id
          description: Target clinical trial subject ID
        visit_id:
          type: string
          title: Visit Id
          description: Target visit identifier
        domain:
          type: string
          title: Domain
          description: Target SDTM domain code
        test_code:
          type: string
          title: Test Code
          description: Target clinical test code
        observation_id:
          anyOf:
            - type: string
            - type: "null"
          title: Observation Id
          description: Optional target unique clinical observation ID
        form_id:
          anyOf:
            - type: string
            - type: "null"
          title: Form Id
          description: Optional form identifier
        field_id:
          anyOf:
            - type: string
            - type: "null"
          title: Field Id
          description: Optional field identifier
        explanation:
          anyOf:
            - type: string
            - type: "null"
          title: Explanation
          description: Contextual explanation/issue description triggering query generation
      type: object
      required:
        - subject_id
        - visit_id
        - domain
        - test_code
      title: QueryTargetDescriptor
      description:
        "Coordinate fields representing the specific target of a query.


        Requirements: PRD-SYS-001"
    QueryUpdate:
      properties:
        status:
          type: string
          title: Status
        explanation:
          anyOf:
            - type: string
            - type: "null"
          title: Explanation
        response:
          anyOf:
            - type: string
            - type: "null"
          title: Response
        observation_id:
          anyOf:
            - type: string
            - type: "null"
          title: Observation Id
        field_link:
          anyOf:
            - type: string
            - type: "null"
          title: Field Link
        message:
          anyOf:
            - type: string
            - type: "null"
          title: Message
        origin:
          anyOf:
            - type: string
            - type: "null"
          title: Origin
        priority:
          anyOf:
            - type: string
            - type: "null"
          title: Priority
        rule_id:
          anyOf:
            - type: string
            - type: "null"
          title: Rule Id
        created_by:
          anyOf:
            - type: string
            - type: "null"
          title: Created By
        responder:
          anyOf:
            - type: string
            - type: "null"
          title: Responder
        resolver:
          anyOf:
            - type: string
            - type: "null"
          title: Resolver
        resolved_at:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Resolved At
        cancellation_reason:
          anyOf:
            - type: string
            - type: "null"
          title: Cancellation Reason
        escalated_at:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Escalated At
        form_id:
          anyOf:
            - type: string
            - type: "null"
          title: Form Id
        field_id:
          anyOf:
            - type: string
            - type: "null"
          title: Field Id
        query_type:
          anyOf:
            - type: string
            - type: "null"
          title: Query Type
        action_required:
          anyOf:
            - type: string
            - type: "null"
          title: Action Required
      type: object
      required:
        - status
      title: QueryUpdate
      description: Pydantic schema for general state transitions.
    RedactPDFRequest:
      properties:
        pdf_base64:
          type: string
          title: Pdf Base64
          description: Base64 encoded PDF document content
        target_snippets:
          items:
            type: string
          type: array
          title: Target Snippets
          description: Target PHI strings to redact
      type: object
      required:
        - pdf_base64
      title: RedactPDFRequest
      description:
        "Request payload to apply non-destructive redactions to PDF document.


        Requirements: PRD-SYS-001"
    RevokeDelegationRequest:
      properties:
        delegation_id:
          type: string
          title: Delegation Id
        end_date:
          type: string
          format: date-time
          title: End Date
        reason_for_change:
          type: string
          title: Reason For Change
      type: object
      required:
        - delegation_id
        - end_date
        - reason_for_change
      title: RevokeDelegationRequest
    SAEReconcileRequest:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Target study ID
        edc_ae_events:
          items:
            additionalProperties: true
            type: object
          type: array
          title: Edc Ae Events
          description: List of EDC AE form data dicts
        safety_cases_xml:
          anyOf:
            - items:
                type: string
              type: array
            - type: "null"
          title: Safety Cases Xml
          description: Optional raw E2B XML reports to parse
      type: object
      required:
        - study_id
        - edc_ae_events
      title: SAEReconcileRequest
      description:
        "Request payload to reconcile EDC AE data against Safety ICSR cases.


        Requirements: PRD-SYS-001"
    SDVScopeEnum:
      type: string
      enum:
        - FIELD
        - PAGE
        - VISIT
      title: SDVScopeEnum
    SDVSignOffRequest:
      properties:
        scope:
          $ref: "#/components/schemas/SDVScopeEnum"
        target_id:
          type: string
          title: Target Id
        subject_id:
          type: string
          title: Subject Id
        study_id:
          type: string
          title: Study Id
        site_id:
          anyOf:
            - type: string
            - type: "null"
          title: Site Id
      type: object
      required:
        - scope
        - target_id
        - subject_id
        - study_id
      title: SDVSignOffRequest
      description: Pydantic request schema for SDV sign-off.
    SDVSignOffResponse:
      properties:
        id:
          type: string
          title: Id
        scope:
          type: string
          title: Scope
        target_id:
          type: string
          title: Target Id
        subject_id:
          type: string
          title: Subject Id
        study_id:
          type: string
          title: Study Id
        site_id:
          anyOf:
            - type: string
            - type: "null"
          title: Site Id
        is_verified:
          type: boolean
          title: Is Verified
        verified_by:
          anyOf:
            - type: string
            - type: "null"
          title: Verified By
        verified_at:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Verified At
        dropped_reason:
          anyOf:
            - type: string
            - type: "null"
          title: Dropped Reason
        dropped_at:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Dropped At
      type: object
      required:
        - id
        - scope
        - target_id
        - subject_id
        - study_id
        - is_verified
      title: SDVSignOffResponse
      description: Pydantic response schema for SDV sign-off.
    SafetyDispatchRequest:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Target protocol study ID
        subject_id:
          type: string
          title: Subject Id
          description: Target subject ID
        safety_report_id:
          type: string
          title: Safety Report Id
          description: Unique E2B(R3) Safety Report ID
        destination_gateway:
          type: string
          title: Destination Gateway
          description: "Destination safety gateway: ARGUS, ARISG, EUDRAVIGILANCE"
          default: ARGUS
        expedited:
          type: boolean
          title: Expedited
          description: True for expedited 7/15-day reporting
          default: true
        reason_for_change:
          type: string
          title: Reason For Change
          description: Mandatory GxP 21 CFR Part 11 justification reason
      type: object
      required:
        - study_id
        - subject_id
        - safety_report_id
        - reason_for_change
      title: SafetyDispatchRequest
      description:
        "Request payload to dispatch ICH E2B(R3) safety report to external PV gateway.


        Requirements: PRD-SYS-001"
    SafetyDispatchResponse:
      properties:
        dispatch_id:
          type: string
          title: Dispatch Id
          description: Unique dispatch transaction ID
        safety_report_id:
          type: string
          title: Safety Report Id
          description: Target Safety Report ID
        status:
          type: string
          title: Status
          description: "Dispatch status: DISPATCHED, DELIVERED, ACKNOWLEDGED"
        dispatched_at:
          type: string
          title: Dispatched At
          description: UTC ISO dispatch timestamp
        ack_status:
          type: string
          title: Ack Status
          description: AS2 / SFTP gateway acknowledgment message
      type: object
      required:
        - dispatch_id
        - safety_report_id
        - status
        - dispatched_at
        - ack_status
      title: SafetyDispatchResponse
      description:
        "Response payload following E2B safety report gateway dispatch.


        Requirements: PRD-SYS-001"
    SamplingModelEnum:
      type: string
      enum:
        - SUBJECT_BASED
        - FIELD_BASED
        - COMBINED
      title: SamplingModelEnum
    SignRequest:
      properties:
        data:
          type: string
          title: Data
      type: object
      required:
        - data
      title: SignRequest
      description: Request model for signing payload data.
    SignResponse:
      properties:
        signed_data:
          type: string
          title: Signed Data
      type: object
      required:
        - signed_data
      title: SignResponse
      description: Response model with the PKCS#7 signed payload.
    SiteActivationRequest:
      properties:
        study_id:
          type: string
          title: Study Id
      type: object
      required:
        - study_id
      title: SiteActivationRequest
    SiteStaffMemberRequest:
      properties:
        site_id:
          type: string
          title: Site Id
        staff_user_id:
          type: string
          title: Staff User Id
        name:
          type: string
          title: Name
        email:
          type: string
          title: Email
        has_gcp_training:
          type: boolean
          title: Has Gcp Training
          default: true
      type: object
      required:
        - site_id
        - staff_user_id
        - name
        - email
      title: SiteStaffMemberRequest
    SiteStaffMemberResponse:
      properties:
        id:
          type: string
          title: Id
        site_id:
          type: string
          title: Site Id
        staff_user_id:
          type: string
          title: Staff User Id
        name:
          type: string
          title: Name
        email:
          type: string
          title: Email
        has_gcp_training:
          type: boolean
          title: Has Gcp Training
      type: object
      required:
        - id
        - site_id
        - staff_user_id
        - name
        - email
        - has_gcp_training
      title: SiteStaffMemberResponse
    StudyEvent:
      properties:
        study_id:
          type: string
          title: Study Id
        payload:
          additionalProperties: true
          type: object
          title: Payload
      type: object
      required:
        - study_id
        - payload
      title: StudyEvent
      description: "Pydantic model representing an incoming study publication event.\n\nAttributes:\n    study_id (str): The unique identifier of the study.\n    payload (dict[str, Any]): The raw USDM protocol payload."
    SubjectConsentRequest:
      properties:
        protocol_version:
          $ref: "#/components/schemas/ProtocolVersionRefDTO"
        icf_signed:
          type: boolean
          title: Icf Signed
        icf_signed_date:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Icf Signed Date
        requires_reconsent:
          type: boolean
          title: Requires Reconsent
          default: false
      type: object
      required:
        - protocol_version
        - icf_signed
      title: SubjectConsentRequest
      description: Pydantic schema for recording a subject's consent to a protocol version.
    SubjectConsentResponse:
      properties:
        id:
          type: string
          title: Id
        subject_id:
          type: string
          title: Subject Id
        study_id:
          type: string
          title: Study Id
        version_tag:
          type: string
          title: Version Tag
        version_index:
          type: integer
          title: Version Index
        icf_signed:
          type: boolean
          title: Icf Signed
        icf_signed_date:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Icf Signed Date
        requires_reconsent:
          type: boolean
          title: Requires Reconsent
        version:
          type: integer
          title: Version
      type: object
      required:
        - id
        - subject_id
        - study_id
        - version_tag
        - version_index
        - icf_signed
        - requires_reconsent
        - version
      title: SubjectConsentResponse
      description: Pydantic schema returning subject consent details.
    SubjectCreate:
      properties:
        subject_id:
          type: string
          title: Subject Id
        study_id:
          type: string
          title: Study Id
        demographics:
          anyOf:
            - $ref: "#/components/schemas/Demographics"
            - type: "null"
      type: object
      required:
        - subject_id
        - study_id
      title: SubjectCreate
      description: Pydantic schema for creating a clinical subject pseudonymously.
    SubjectDemographicsUpdateRequest:
      properties:
        demographics:
          anyOf:
            - $ref: "#/components/schemas/Demographics"
            - type: "null"
        strat_factors:
          anyOf:
            - additionalProperties: true
              type: object
            - type: "null"
          title: Strat Factors
      type: object
      title: SubjectDemographicsUpdateRequest
    SubjectDetailResponse:
      properties:
        subject_id:
          type: string
          title: Subject Id
        study_id:
          type: string
          title: Study Id
        status:
          type: string
          title: Status
        site_id:
          anyOf:
            - type: string
            - type: "null"
          title: Site Id
        treatment_group:
          anyOf:
            - type: string
            - type: "null"
          title: Treatment Group
        randomization_seed:
          anyOf:
            - type: string
            - type: "null"
          title: Randomization Seed
        investigational_product_id:
          anyOf:
            - type: string
            - type: "null"
          title: Investigational Product Id
      type: object
      required:
        - subject_id
        - study_id
        - status
      title: SubjectDetailResponse
    SubjectRandomizationResponse:
      properties:
        subject_id:
          type: string
          title: Subject Id
        status:
          type: string
          title: Status
        stratum_key:
          anyOf:
            - type: string
            - type: "null"
          title: Stratum Key
        randomized_at:
          type: string
          format: date-time
          title: Randomized At
        kit_reference:
          anyOf:
            - type: string
            - type: "null"
          title: Kit Reference
        treatment_arm:
          anyOf:
            - type: string
            - type: "null"
          title: Treatment Arm
      type: object
      required:
        - subject_id
        - status
        - randomized_at
      title: SubjectRandomizationResponse
      description: Pydantic schema for returning blinded subject randomization details.
    SubjectResponse:
      properties:
        id:
          type: string
          title: Id
        subject_id:
          type: string
          title: Subject Id
        study_id:
          type: string
          title: Study Id
        encrypted_demographics:
          anyOf:
            - type: string
            - type: "null"
          title: Encrypted Demographics
      type: object
      required:
        - id
        - subject_id
        - study_id
      title: SubjectResponse
      description: Pydantic schema returning subject details.
    SubjectScreeningRequest:
      properties:
        study_id:
          anyOf:
            - type: string
            - type: "null"
          title: Study Id
      type: object
      title: SubjectScreeningRequest
      description: Pydantic schema for requesting subject eligibility screening.
    SubjectScreeningResponse:
      properties:
        eligible:
          anyOf:
            - type: boolean
            - type: "null"
          title: Eligible
        failed_criteria:
          items:
            type: string
          type: array
          title: Failed Criteria
        indeterminate_criteria:
          items:
            type: string
          type: array
          title: Indeterminate Criteria
        criterion_evaluations:
          items:
            $ref: "#/components/schemas/CriterionLevelResult"
          type: array
          title: Criterion Evaluations
      type: object
      title: SubjectScreeningResponse
      description: Pydantic schema for subject screening evaluation outcome, excluding PHI.
    SubjectStateUpdateRequest:
      properties:
        status:
          anyOf:
            - type: string
            - type: "null"
          title: Status
        state:
          anyOf:
            - type: string
            - type: "null"
          title: State
      type: object
      title: SubjectStateUpdateRequest
    SubjectUnblindResponse:
      properties:
        subject_id:
          type: string
          title: Subject Id
        status:
          type: string
          title: Status
        is_unblinded:
          type: boolean
          title: Is Unblinded
        treatment_arm:
          anyOf:
            - type: string
            - type: "null"
          title: Treatment Arm
        drug_code:
          anyOf:
            - type: string
            - type: "null"
          title: Drug Code
        unblinded_at:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Unblinded At
        unblinded_by:
          anyOf:
            - type: string
            - type: "null"
          title: Unblinded By
        unblinded_reason:
          anyOf:
            - type: string
            - type: "null"
          title: Unblinded Reason
      type: object
      required:
        - subject_id
        - status
        - is_unblinded
      title: SubjectUnblindResponse
      description: Pydantic schema for returning emergency unblind details.
    SyncBlockDetails:
      properties:
        fieldId:
          type: string
          title: Fieldid
        studyId:
          anyOf:
            - type: string
            - type: "null"
          title: Studyid
        subjectId:
          anyOf:
            - type: string
            - type: "null"
          title: Subjectid
        visitId:
          anyOf:
            - type: string
            - type: "null"
          title: Visitid
        domain:
          anyOf:
            - type: string
            - type: "null"
          title: Domain
        testCode:
          anyOf:
            - type: string
            - type: "null"
          title: Testcode
        query:
          anyOf:
            - $ref: "#/components/schemas/SyncBlockQuery"
            - type: "null"
        label:
          anyOf:
            - type: string
            - type: "null"
          title: Label
        cdash:
          anyOf:
            - type: string
            - type: "null"
          title: Cdash
        oldValue:
          anyOf:
            - type: string
            - type: "null"
          title: Oldvalue
        newValue:
          anyOf:
            - type: string
            - type: "null"
          title: Newvalue
      type: object
      required:
        - fieldId
      title: SyncBlockDetails
      description: Pydantic schema representing block-specific metadata and clinical coordinates.
    SyncBlockQuery:
      properties:
        status:
          type: string
          title: Status
        message:
          anyOf:
            - type: string
            - type: "null"
          title: Message
        createdBy:
          anyOf:
            - type: string
            - type: "null"
          title: Createdby
        createdAt:
          anyOf:
            - type: string
            - type: "null"
          title: Createdat
        response:
          anyOf:
            - type: string
            - type: "null"
          title: Response
        respondedBy:
          anyOf:
            - type: string
            - type: "null"
          title: Respondedby
        respondedAt:
          anyOf:
            - type: string
            - type: "null"
          title: Respondedat
        closedBy:
          anyOf:
            - type: string
            - type: "null"
          title: Closedby
        closedAt:
          anyOf:
            - type: string
            - type: "null"
          title: Closedat
      type: object
      required:
        - status
      title: SyncBlockQuery
      description: Pydantic schema representing the query details in a local ledger block.
    SyncRequest:
      properties:
        blocks:
          items:
            $ref: "#/components/schemas/LocalLedgerBlock"
          type: array
          title: Blocks
      type: object
      required:
        - blocks
      title: SyncRequest
      description: Pydantic schema for bulk-synchronizing local client-side ledger updates.
    TSDVConfigCreate:
      properties:
        study_id:
          type: string
          title: Study Id
        sampling_model:
          $ref: "#/components/schemas/SamplingModelEnum"
        initial_full_sdv_subject_count:
          type: integer
          minimum: 0.0
          title: Initial Full Sdv Subject Count
          default: 0
        random_sample_percentage:
          type: number
          maximum: 100.0
          minimum: 0.0
          title: Random Sample Percentage
          default: 0.0
        full_sdv_domains:
          anyOf:
            - items:
                type: string
              type: array
            - type: "null"
          title: Full Sdv Domains
        safety_endpoints:
          anyOf:
            - items:
                type: string
              type: array
            - type: "null"
          title: Safety Endpoints
        zero_sdv_domains:
          anyOf:
            - items:
                type: string
              type: array
            - type: "null"
          title: Zero Sdv Domains
        trial_random_seed:
          anyOf:
            - type: integer
              minimum: 0.0
            - type: "null"
          title: Trial Random Seed
      type: object
      required:
        - study_id
        - sampling_model
      title: TSDVConfigCreate
    TSDVConfigResponse:
      properties:
        id:
          type: string
          title: Id
        study_id:
          type: string
          title: Study Id
        sampling_model:
          type: string
          title: Sampling Model
        initial_full_sdv_subject_count:
          type: integer
          title: Initial Full Sdv Subject Count
        random_sample_percentage:
          type: number
          title: Random Sample Percentage
        full_sdv_domains:
          anyOf:
            - items:
                type: string
              type: array
            - type: "null"
          title: Full Sdv Domains
        safety_endpoints:
          anyOf:
            - items:
                type: string
              type: array
            - type: "null"
          title: Safety Endpoints
        zero_sdv_domains:
          anyOf:
            - items:
                type: string
              type: array
            - type: "null"
          title: Zero Sdv Domains
        trial_random_seed:
          anyOf:
            - type: integer
            - type: "null"
          title: Trial Random Seed
        version:
          type: integer
          title: Version
      type: object
      required:
        - id
        - study_id
        - sampling_model
        - initial_full_sdv_subject_count
        - random_sample_percentage
        - version
      title: TSDVConfigResponse
    TSDVEvaluationResponse:
      properties:
        required:
          type: boolean
          title: Required
        subject_selected:
          type: boolean
          title: Subject Selected
        field_decision:
          anyOf:
            - type: boolean
            - type: "null"
          title: Field Decision
        sampling_model:
          type: string
          title: Sampling Model
        config_id:
          type: string
          title: Config Id
        enrollment_index:
          type: integer
          title: Enrollment Index
        explanation:
          type: string
          title: Explanation
      type: object
      required:
        - required
        - subject_selected
        - sampling_model
        - config_id
        - enrollment_index
        - explanation
      title: TSDVEvaluationResponse
    TranslationJobResponse:
      properties:
        id:
          type: string
          title: Id
        study_id:
          type: string
          title: Study Id
        status:
          type: string
          title: Status
        odm_payload:
          anyOf:
            - type: string
            - type: "null"
          title: Odm Payload
        openrosa_payload:
          anyOf:
            - type: string
            - type: "null"
          title: Openrosa Payload
        error_message:
          anyOf:
            - type: string
            - type: "null"
          title: Error Message
      type: object
      required:
        - id
        - study_id
        - status
      title: TranslationJobResponse
      description: Pydantic schema returning translation job status and metadata.
    UCUMConvertRequest:
      properties:
        value:
          type: number
          title: Value
        source_unit:
          type: string
          title: Source Unit
        target_unit:
          type: string
          title: Target Unit
      type: object
      required:
        - value
        - source_unit
        - target_unit
      title: UCUMConvertRequest
    UCUMConvertResponse:
      properties:
        source:
          $ref: "#/components/schemas/UCUMUnitValue"
        target:
          $ref: "#/components/schemas/UCUMUnitValue"
        is_compatible:
          type: boolean
          title: Is Compatible
        scale_factor:
          type: number
          title: Scale Factor
        offset:
          anyOf:
            - type: number
            - type: "null"
          title: Offset
      type: object
      required:
        - source
        - target
        - is_compatible
        - scale_factor
      title: UCUMConvertResponse
    UCUMUnitValue:
      properties:
        value:
          type: number
          title: Value
        unit:
          type: string
          title: Unit
      type: object
      required:
        - value
        - unit
      title: UCUMUnitValue
    UnblindRequest:
      properties:
        reason_code:
          $ref: "#/components/schemas/UnblindingReasonCode"
        justification:
          type: string
          minLength: 50
          title: Justification
        shares:
          items:
            $ref: "#/components/schemas/CustodianShare"
          type: array
          maxItems: 2
          minItems: 2
          title: Shares
          description: Exactly two custodian shares are required (dual-custody contract).
      type: object
      required:
        - reason_code
        - justification
        - shares
      title: UnblindRequest
      description: "Request body for an emergency treatment-allocation unblinding operation.\n\nThe dual-custody contract requires exactly two custodian shares \u2014 one from\neach approved custodian.  Requests with fewer or more shares, or with an\ninsufficiently detailed justification, are rejected at the schema layer.\n\nAttributes:\n    reason_code: One of the three regulatory-approved unblinding scenarios\n        from ``UnblindingReasonCode``.\n    justification: A free-text clinical justification of at least\n        ``MIN_JUSTIFICATION_LENGTH`` characters.  Stored only in the\n        immutable audit record; never broadcast in notifications.\n    shares: Exactly two ``CustodianShare`` objects \u2014 one per approved\n        custodian \u2014 supplying the Shamir secret shares needed to\n        reconstruct the blinded allocation key."
    UnblindingReasonCode:
      type: string
      enum:
        - SAE-Life-Threatening-Event
        - Accidental-Overdose
        - Required-by-Regulatory-Authority
      title: UnblindingReasonCode
      description: "Controlled vocabulary of approved reason codes for emergency unblinding.\n\nOnly these three regulatory-approved scenarios authorise an emergency\ntreatment-allocation disclosure outside of the standard end-of-study\nunblinding process.\n\nAttributes:\n    SAE_LIFE_THREATENING_EVENT: Serious Adverse Event that is immediately\n        life-threatening and requires knowledge of the treatment assignment.\n    ACCIDENTAL_OVERDOSE: Accidental administration of an overdose requiring\n        immediate clinical intervention with knowledge of the treatment arm.\n    REQUIRED_BY_REGULATORY_AUTHORITY: A competent regulatory authority has\n        formally requested disclosure of the blinded assignment."
    UnitConversionRequest:
      properties:
        value:
          type: number
          title: Value
        from_unit:
          type: string
          title: From Unit
        to_unit:
          type: string
          title: To Unit
      type: object
      required:
        - value
        - from_unit
        - to_unit
      title: UnitConversionRequest
      description: Pydantic schema for unit conversion requests.
    UnitConversionResponse:
      properties:
        value:
          type: number
          title: Value
        from_unit:
          type: string
          title: From Unit
        to_unit:
          type: string
          title: To Unit
        converted_value:
          type: number
          title: Converted Value
      type: object
      required:
        - value
        - from_unit
        - to_unit
        - converted_value
      title: UnitConversionResponse
      description: Pydantic schema returning converted values.
    UploadEISFDocumentRequest:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Target protocol study ID
        site_id:
          type: string
          title: Site Id
          description: Target investigator site ID
        category:
          $ref: "#/components/schemas/EISFTaxonomyCategoryEnum"
          description: DIA taxonomy category
        title:
          type: string
          title: Title
          description: Document title
        file_name:
          type: string
          title: File Name
          description: Original file name
        content_base64:
          type: string
          title: Content Base64
          description: Base64 encoded file content string
      type: object
      required:
        - study_id
        - site_id
        - category
        - title
        - file_name
        - content_base64
      title: UploadEISFDocumentRequest
      description: "Request payload to upload an eISF document.


        Requirements: PRD-SYS-001"
    VerifyRequest:
      properties:
        signed_data:
          type: string
          title: Signed Data
      type: object
      required:
        - signed_data
      title: VerifyRequest
      description: Request model for verifying a PKCS#7 signed payload.
    VerifyResponse:
      properties:
        is_valid:
          type: boolean
          title: Is Valid
        status:
          type: string
          title: Status
        failure_reason:
          type: string
          title: Failure Reason
          default: ""
      type: object
      required:
        - is_valid
        - status
      title: VerifyResponse
      description: Response model for PKCS#7 signature verification.
    VisitCreate:
      properties:
        subject_id:
          type: string
          title: Subject Id
        visit_name:
          type: string
          title: Visit Name
        study_id:
          type: string
          title: Study Id
        visit_date:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Visit Date
        planned_date:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Planned Date
        window_start:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Window Start
        window_end:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Window End
        window_status:
          anyOf:
            - type: string
            - type: "null"
          title: Window Status
      type: object
      required:
        - subject_id
        - visit_name
        - study_id
      title: VisitCreate
      description: Pydantic schema for creating a clinical visit.
    VisitDetailResponse:
      properties:
        id:
          type: string
          title: Id
        subject_id:
          type: string
          title: Subject Id
        visit_name:
          type: string
          title: Visit Name
        visit_date:
          type: string
          format: date-time
          title: Visit Date
        study_id:
          type: string
          title: Study Id
        treatment_group:
          anyOf:
            - type: string
            - type: "null"
          title: Treatment Group
        randomization_seed:
          anyOf:
            - type: string
            - type: "null"
          title: Randomization Seed
        investigational_product_id:
          anyOf:
            - type: string
            - type: "null"
          title: Investigational Product Id
        planned_date:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Planned Date
        window_start:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Window Start
        window_end:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Window End
        window_status:
          anyOf:
            - type: string
            - type: "null"
          title: Window Status
      type: object
      required:
        - id
        - subject_id
        - visit_name
        - visit_date
        - study_id
      title: VisitDetailResponse
    VisitResponse:
      properties:
        id:
          type: string
          title: Id
        subject_id:
          type: string
          title: Subject Id
        visit_name:
          type: string
          title: Visit Name
        visit_date:
          type: string
          format: date-time
          title: Visit Date
        study_id:
          type: string
          title: Study Id
        protocol_version_tag:
          anyOf:
            - type: string
            - type: "null"
          title: Protocol Version Tag
        protocol_version_index:
          anyOf:
            - type: integer
            - type: "null"
          title: Protocol Version Index
        planned_date:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Planned Date
        window_start:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Window Start
        window_end:
          anyOf:
            - type: string
              format: date-time
            - type: "null"
          title: Window End
        window_status:
          anyOf:
            - type: string
            - type: "null"
          title: Window Status
      type: object
      required:
        - id
        - subject_id
        - visit_name
        - visit_date
        - study_id
      title: VisitResponse
      description: Pydantic schema returning visit details.
    WHODrugATCContext:
      properties:
        atc_code:
          type: string
          title: Atc Code
        description:
          type: string
          title: Description
        code:
          anyOf:
            - type: string
            - type: "null"
          title: Code
        text:
          anyOf:
            - type: string
            - type: "null"
          title: Text
      type: object
      required:
        - atc_code
        - description
      title: WHODrugATCContext
    WHODrugCodeLookupResponse:
      properties:
        status:
          type: string
          enum:
            - AUTO-CODED
            - SUGGESTIONS
            - UNCODABLE
          title: Status
        matches:
          items:
            $ref: "#/components/schemas/WHODrugMatch"
          type: array
          title: Matches
      type: object
      required:
        - status
        - matches
      title: WHODrugCodeLookupResponse
    WHODrugIngredientItem:
      properties:
        ingredient_code:
          type: string
          title: Ingredient Code
        ingredient_name:
          type: string
          title: Ingredient Name
        code:
          anyOf:
            - type: string
            - type: "null"
          title: Code
        name:
          anyOf:
            - type: string
            - type: "null"
          title: Name
      type: object
      required:
        - ingredient_code
        - ingredient_name
      title: WHODrugIngredientItem
    WHODrugMatch:
      properties:
        drug_code:
          type: string
          title: Drug Code
        preferred_name:
          type: string
          title: Preferred Name
        drug_name:
          anyOf:
            - type: string
            - type: "null"
          title: Drug Name
        score:
          type: number
          title: Score
        ingredients:
          items:
            $ref: "#/components/schemas/WHODrugIngredientItem"
          type: array
          title: Ingredients
          default: []
        atc_context:
          items:
            $ref: "#/components/schemas/WHODrugATCContext"
          type: array
          title: Atc Context
          default: []
        code:
          anyOf:
            - type: string
            - type: "null"
          title: Code
        name:
          anyOf:
            - type: string
            - type: "null"
          title: Name
        atc:
          items:
            $ref: "#/components/schemas/WHODrugATCContext"
          type: array
          title: Atc
          default: []
      type: object
      required:
        - drug_code
        - preferred_name
        - score
      title: WHODrugMatch
```

---

## 8. Complete GraphQL Schema Definition

In addition to REST, Cadence Clinical offers a high-performance **GraphQL endpoint** specifically designed for complex, deep, single-roundtrip traversals of the study design graph and concept taxonomy.

```graphql
"""
Core CDISC USDM/MDR Graph Schema for Cadence Clinical.
Provides contract-complete traversal capabilities for studies, epochs, arms, and biomedical concepts.
"""
scalar DateTime

enum TerminologySystem {
  SNOMED_CT
  LOINC
  MEDDRA
  WHODRUG
}

enum USDMStudyType {
  INTERVENTIONAL
  OBSERVATIONAL
  EXPANDED_ACCESS
}

type CDASHMapping {
  domain: String!
  variableName: String!
  dataType: String!
}

type AllowableUnit {
  ucumCode: String!
  name: String!
}

type BiomedicalConcept {
  id: ID!
  conceptCode: String!
  terminology: TerminologySystem!
  displayName: String!
  definition: String!
  cdashMapping: CDASHMapping
  allowableUnits: [AllowableUnit!]!
  version: String!
  status: String!
  createdAt: DateTime!
  createdBy: String!
  updatedAt: DateTime
  updatedBy: String
  reasonForChange: String
}

type StudyArm {
  id: ID!
  name: String!
  description: String
  type: String!
}

type StudyEpoch {
  id: ID!
  name: String!
  sequenceOrder: Int!
}

type StudyElement {
  id: ID!
  name: String!
  biomedicalConcepts: [BiomedicalConcept!]!
}

type Protocol {
  id: ID!
  version: String!
  status: String!
  documentUrl: String
}

type USDMStudy {
  id: ID!
  name: String!
  studyType: USDMStudyType!
  protocol: Protocol!
  studyArms: [StudyArm!]!
  studyEpochs: [StudyEpoch!]!
  studyElements: [StudyElement!]!
}

type ConceptSearchResult {
  conceptCode: String!
  terminology: TerminologySystem!
  displayName: String!
  matchScore: Float!
}

type Query {
  """
  Retrieve a study by its unique system ID, returning a fully resolved USDM graph.
  """
  study(id: ID!): USDMStudy

  """
  Fetch a single Biomedical Concept from the registry database.
  """
  biomedicalConcept(id: ID!): BiomedicalConcept

  """
  Perform deep taxonomy and concept search with score-based ranking.
  """
  searchConcepts(
    query: String!
    terminology: TerminologySystem
    limit: Int = 20
  ): [ConceptSearchResult!]!
}

input CDASHMappingInput {
  domain: String!
  variableName: String!
  dataType: String!
}

input AllowableUnitInput {
  ucumCode: String!
  name: String!
}

input CreateBiomedicalConceptInput {
  conceptCode: String!
  terminology: TerminologySystem!
  displayName: String!
  definition: String!
  cdashMapping: CDASHMappingInput
  allowableUnits: [AllowableUnitInput!]!
  changeReason: String!
}

type Mutation {
  """
  Register a new Biomedical Concept inside the MDR catalog, logging proper audit changes.
  """
  createBiomedicalConcept(
    input: CreateBiomedicalConceptInput!
  ): BiomedicalConcept!
}
```

---

## 9. ISO 14155:2020 Data Integrity Matrix

**ISO 14155:2020** mandates stringent criteria for electronic clinical systems, focusing on data integrity, traceability, prevention of unauthorized changes, and robust system validation.

| Requirement                          | Cadence Clinical Implementation Paradigm                                                                                                                          | API Endpoint / Schema Reference                          |
| :----------------------------------- | :---------------------------------------------------------------------------------------------------------------------------------------------------------------- | :------------------------------------------------------- |
| **Data Traceability (Clause 7.8.2)** | Every record creation, update, or soft deletion captures acting user ID and timestamp. Direct database hard-deletions are prevented at the database driver layer. | `GET /api/v1/execution/studies/{study_id}/audit-trail`   |
| **System Access Controls**           | Role-Based Access Control (RBAC) configured via Keycloak OIDC. Only users with designated claims can perform mutations.                                           | Sections 3.1 & 7.1 (OAuth2 Bearer Scope mapping)         |
| **Change Reason Enforcement**        | MDR and EDC interfaces enforce `reason_for_change` parameters on updates. Requests omitting this block fail with HTTP 400.                                        | `PUT /api/v1/mdr/concepts/{id}` (Section 4.1.3)          |
| **Data Synchronization Safety**      | Sync pipelines use strict validation. Translation of schema state is atomic; failure triggers rolling back.                                                       | `POST /api/v1/execution/studies/sync` (Section 6.3.1)    |
| **Unit Verification Standards**      | Dynamic lookup of numeric metrics against standardized UCUM scale parameters. Prevents anomalous scale discrepancies.                                             | `POST /api/v1/dictionaries/ucum/convert` (Section 5.6.1) |

---

## 10. Multi-Lingual Support Framework

Medical taxonomies (such as MedDRA and SNOMED CT) require native multi-lingual support to allow international clinical trials. Cadence Clinical supports localized concept representations.

### 10.1 Language Negotiation Headers

All search and lookup APIs accept the `Accept-Language` standard HTTP header:

- `Accept-Language: en` (Default: English)
- `Accept-Language: ja` (Japanese)
- `Accept-Language: es` (Spanish)
- `Accept-Language: zh` (Chinese)

### 10.2 localized Response Payload Structure

When a localized request is issued, the dictionary connector maps the base concept code to its localized descriptions while retaining the exact parent-child structural codes:

```json
{
  "concept_code": "10019211",
  "terminology": "MedDRA",
  "requested_language": "ja",
  "hierarchical_level": "LLT",
  "display_name": "頭痛",
  "english_equivalent": "Headache",
  "parent_pt": {
    "pt_code": "10019211",
    "display_name": "頭痛"
  },
  "hierarchy": {
    "soc_code": "10029205",
    "soc_name": "神経系障害"
  }
}
```

This ensures that regardless of the site language executing data capture, the underlying clinical metrics are bound to the identical numerical identifier, enforcing universal semantic consistency.

---

## 11. Tickets & Query Escalation Endpoints

The Tickets microservice endpoints manage in-application support tickets, comments, and audit trails. These endpoints comply fully with standard Section 3 protocols (such as gateway signature handshake, RFC 7807 problem details, and standard limit/offset pagination).

### 11.1 General Architecture & Semantics

#### 11.1.1 Status Transition State Machine

Support tickets follow a rigid status transition lifecycle:

- **Allowed Paths:**
  $$\text{OPEN} \longrightarrow \text{IN\_PROGRESS} \longrightarrow \text{RESOLVED} \longrightarrow \text{CLOSED}$$
  $$\text{CLOSED} \longrightarrow \text{REOPENED} \longrightarrow \text{IN\_PROGRESS}$$
- **Terminal States:** `CLOSED` and `CANCELLED`. Direct updates to tickets in terminal states are rejected with HTTP 400, except for status transition requests to `REOPENED` or `OPEN`.

#### 11.1.2 Optimistic Locking Semantic (`version_index`)

To prevent concurrent overwrite hazards (race conditions), all ticket updates (`PUT` and `/assign`, `/transition` mutations) require the current `version_index`. The gateway/service searches for the expected version in:

1. **Request Body:** payload attribute `version_index`.
2. **Query Parameter:** `version_index` or `expected_version`.
3. **HTTP Header:** `If-Match` or `X-Expected-Version`.

If the expected version index is missing or mismatched with the current database `version_index`, the request is immediately rejected with **HTTP 409 Conflict** (`version_index` mismatch).

#### 11.1.3 Site & Study Scoped Isolation Rules

- **Global Access:** Sponsor administrator and global admin roles preserve view and mutation access across all tickets.
- **Site Scoped Gating:** Clinical research coordinators (CRC) and investigator roles are restricted to tickets matching their assigned site IDs. Direct requests referencing out-of-scope site IDs are blocked with **HTTP 403 Forbidden**.
- **Auditor Gating:** Auditor and inspector roles are permitted read-only (`GET`) queries, but are blocked from any write/mutation pathways via `verify_not_auditor` checks, returning **HTTP 403 Forbidden**.

#### 11.1.4 Part 11 Audit Trail & Read-Audit Policy

Every ticket mutation requires a non-empty change reason (propagated via `X-Change-Reason` / `X-Change-Justification` or header). To satisfy 21 CFR Part 11 non-repudiation, **read actions also generate append-only logs** in the immutable audit ledger. Specially:

- `GET /api/v1/tickets/{id}` emits a `TICKET_VIEW` log.
- `GET /api/v1/tickets` emits a `TICKET_LIST` log.
- `GET /api/v1/tickets/{id}/comments` emits a `TICKET_COMMENTS_VIEW` log.
- `GET /api/v1/tickets/audit-logs` emits a `TICKET_AUDIT_LOG_LIST` log.

---

### 11.2 Endpoint Specifications

#### 11.2.1 POST /api/v1/tickets

Creates a new support ticket.

- **Status Codes:**
  - `211 Created`: Ticket created successfully (Note: returns HTTP 201).
  - `403 Forbidden`: Missing change justification reason, or role-scope mismatch.
  - `422 Unprocessable Entity`: Invalid fields or categories.

- **Request Body Summary:**

<!-- validation-skip -->

```json skip
{
  "title": "System connection failure on study 102",
  "description": "Database connection timed out during subject randomization.",
  "category": "TECHNICAL",
  "priority": "HIGH",
  "assignee_user": "bob_developer",
  "assignee_role": "developer",
  "site_id": "SITE-BOSTON-01",
  "study_id": "STUDY-ONC-01"
}
```

- **Response Body Summary:**

```json
{
  "id": "tkt_81a8b992f03",
  "reference": "TKT-00001",
  "title": "System connection failure on study 102",
  "description": "Database connection timed out during subject randomization.",
  "category": "TECHNICAL",
  "priority": "HIGH",
  "status": "OPEN",
  "reporter": "crc_user_1",
  "assignee_user": "bob_developer",
  "assignee_role": "developer",
  "site_id": "SITE-BOSTON-01",
  "study_id": "STUDY-ONC-01",
  "is_deleted": false,
  "created_at": "2026-10-24T14:32:01.009Z",
  "created_by": "crc_user_1",
  "reason_for_change": "Initial ticket logging",
  "version_index": 1
}
```

#### 11.2.2 GET /api/v1/tickets

Retrieves a filtered list of tickets scoped to the user's site permissions. Generates a `TICKET_LIST` audit log.

- **Query Parameters:**
  - `status`, `category`, `priority`, `reporter`, `assignee`, `site_id`, `study_id`.
  - `limit` (default: 20, max: 100), `offset` (default: 0).
  - `include_deleted` (default: false).

- **Status Codes:**
  - `200 OK`: Returns a list of matching `TicketResponse` structures.
  - `403 Forbidden`: Querying out-of-scope site IDs.

#### 11.2.3 `GET /api/v1/tickets/{id}`

Retrieves details of a specific ticket by its ID or sequential reference (e.g. `TKT-00001`). Generates a `TICKET_VIEW` audit log.

- **Status Codes:**
  - `200 OK`: Returns the ticket details.
  - `403 Forbidden`: Ticket belongs to a site outside user's assigned scope.
  - `404 Not Found`: Ticket reference/ID does not exist.

#### 11.2.4 `PUT /api/v1/tickets/{id}`

Updates general fields of a ticket. Checks status transitions and optimistic locking version.

- **Status Codes:**
  - `200 OK`: Ticket updated.
  - `400 Bad Request`: Ticket in terminal state and request is not a reopen transition, or invalid status transition path.
  - `409 Conflict`: Missing or stale `version_index`.

#### 11.2.5 `POST /api/v1/tickets/{id}/transition`

Transitions a ticket's status explicitly. Emits transition notifications asynchronously.

- **Request Body Summary:**

<!-- validation-skip -->

```json
{
  "status": "RESOLVED",
  "version_index": 1
}
```

- **Status Codes:**
  - `200 OK`: Transition successful.
  - `400 Bad Request`: Invalid transition path from current state.
  - `409 Conflict`: Stale version index.

#### 11.2.6 `POST /api/v1/tickets/{id}/assign`

Assigns a ticket to a user and/or role-based target explicitly.

- **Request Body Summary:**

<!-- validation-skip -->

```json
{
  "assignee_user": "bob_developer",
  "assignee_role": "developer",
  "version_index": 1
}
```

#### 11.2.7 `POST /api/v1/tickets/{id}/comments`

Appends an auditable comment to a ticket. Enqueues a notification to other stakeholders.

- **Request Body Summary:**

<!-- validation-skip -->

```json skip
{
  "body": "This issue is resolved following a database pool resize."
}
```

- **Status Codes:**
  - `211 Created`: Comment appended (Note: returns HTTP 201).
  - `403 Forbidden`: Missing change justification or out of scope.

#### 11.2.8 `GET /api/v1/tickets/{id}/comments`

Lists comments for a ticket in ascending chronological order. Generates a `TICKET_COMMENTS_VIEW` audit log.

#### 11.2.9 GET /api/v1/tickets/audit-logs

Retrieves the paginated audit trail ledger of ticket events in descending chronological order. Generates a `TICKET_AUDIT_LOG_LIST` log.

- **Query Parameters:**
  - `ticket_id` (optional, filter logs for a specific ticket).
  - `limit` (default: 50, max: 250), `offset` (default: 0).
  - `start_time`, `end_time` (ISO 8601 date filters).

- **Status Codes:**
  - `200 OK`: Returns paginated audit items.
  - `422 Unprocessable Entity`: Boundary validation failures (e.g. limit > 250, offset < 0).

## 11. In-Application Tickets Endpoints

The Tickets microservice (`apps/tickets`) manages GxP and Part 11 compliant support tickets, comments, and audit trails. All endpoints are protected by the central API Gateway.

### 11.1 Reference to Common Gateway Standards

Consistent with Section 3, all requests routed to Tickets endpoints must undergo the API Gateway signature handshake (Version 2) using `GATEWAY_SECRET` to verify identity and scopes. Standardized error reporting utilizes RFC 7807 Problem Details formats, and paginated listings support standard offset and limit parameters.

### 11.2 Status Transition Map & Lifecycle States

The ticket system follows a deterministic state-transition map. Jumps to arbitrary undeclared statuses are rejected with HTTP 400 Bad Request.

- **Terminal States:** `CLOSED`, `CANCELLED`.
- **Reopenable States:** A terminal ticket is strictly immutable and blocks all modifications unless explicitly transitioned using `REOPENED` status.
- **Transition Rules:**
  - `OPEN` $\longrightarrow$ `IN_PROGRESS` or `RESOLVED`
  - `IN_PROGRESS` $\longrightarrow$ `RESOLVED` or `CANCELLED`
  - `RESOLVED` $\longrightarrow$ `CLOSED` or `REOPENED`
  - `CLOSED` $\longrightarrow$ `REOPENED`
  - `CANCELLED` $\longrightarrow$ `REOPENED`
  - `REOPENED` $\longrightarrow$ `IN_PROGRESS` or `RESOLVED`

### 11.3 Optimistic Locking & Header Resolution

All mutation endpoints (POST, PUT, transition, assign) enforce strict optimistic locking checks using `version_index` to prevent overwrites:

1. **Resolution Order:** The expected version index is extracted first from the request body payload's `version_index` field. If absent, the query parameters (`version_index` or `expected_version`) are checked. Finally, headers (`If-Match` or `X-Expected-Version`) are evaluated.
2. **Conflict Checking:** If the expected version is missing, or is mismatched against the active database record's `version_index`, the request is rejected with HTTP 409 Conflict.

### 11.4 Scope and Access Isolation Rules

Visibility and mutation access are strictly scoped based on the OIDC principal extracted from the API Gateway signature:

- **Auditor Blocking:** Auditors or inspectors are strictly read-only and blocked from all mutations with HTTP 403 Forbidden (verified via `verify_not_auditor`).
- **Site and Study Isolation:** Site-scoped roles (e.g. Investigators, CRCs, CRAs) can only view or mutate tickets scoped to their assigned sites or studies. Out-of-scope operations or comments query requests are rejected with HTTP 403 Forbidden.

### 11.5 Read-Audit Logging Policy

GET actions on the tickets service are auditable and trigger explicit log append writes to the `TicketAuditLog` ledger:

- `GET /api/v1/tickets` $\longrightarrow$ Appends a `TICKET_LIST` audit log.
- `GET /api/v1/tickets/{id}` $\longrightarrow$ Appends a `TICKET_VIEW` audit log.
- `GET /api/v1/tickets/{id}/comments` $\longrightarrow$ Appends a `TICKET_COMMENTS_VIEW` audit log.
- `GET /api/v1/tickets/audit-logs` $\longrightarrow$ Appends a `TICKET_AUDIT_LOG_LIST` self-audit log.

### 11.6 Route Specification Contracts

#### POST /api/v1/tickets

Creates and persists a new Ticket. Status is initialized as `OPEN` and version_index is set to 1.

- **Request Body:** `TicketCreate` (title, description, category, priority, assignee_user, assignee_role, org_id, site_id, study_id, related_entity_type, related_entity_id, due_date).
- **Expected Response:** `201 Created` with a `TicketResponse` object.
- **Error Codes:**
  - `403 Forbidden`: Missing `X-Change-Reason` justification or Auditor role.
  - `422 Unprocessable Entity`: Invalid category or priority enum.

#### GET /api/v1/tickets

Lists and filters tickets with pagination and scope isolation.

- **Query Parameters:** `status`, `category`, `priority`, `reporter`, `assignee`, `org_id`, `site_id`, `study_id`, `include_deleted`, `limit`, `offset`.
- **Expected Response:** `200 OK` with an array of `TicketResponse` objects.
- **Read-Audit Log:** Writes `TICKET_LIST` entry.

#### `GET /api/v1/tickets/{id}`

Retrieves a specific ticket by ID or unique human-readable reference (e.g., `TKT-00001`).

- **Expected Response:** `200 OK` with `TicketResponse` object.
- **Error Codes:**
  - `404 Not Found`: Ticket does not exist.
  - `403 Forbidden`: Insufficient site or study scope.
- **Read-Audit Log:** Writes `TICKET_VIEW` entry.

#### `PUT /api/v1/tickets/{id}`

Updates general fields of a ticket, applying optimistic locking and transition checks.

- **Request Body:** `TicketUpdate` (title, description, category, priority, status, assignee_user, assignee_role, org_id, site_id, study_id, related_entity_type, related_entity_id, due_date, is_deleted, version_index).
- **Expected Response:** `200 OK` with updated `TicketResponse`.
- **Error Codes:**
  - `400 Bad Request`: Ticket is terminal and not reopening, or invalid transition path.
  - `409 Conflict`: Missing or stale expected version.
  - `403 Forbidden`: Missing change justification.

#### `POST /api/v1/tickets/{id}/transition`

Explicitly transitions a ticket status.

- **Request Body:** `TicketTransitionPayload` (status, version_index).
- **Expected Response:** `200 OK` with updated `TicketResponse`.
- **Error Codes:** `400 Bad Request` (invalid transition), `409 Conflict` (locking violation).

#### `POST /api/v1/tickets/{id}/assign`

Explicitly updates assignee user and/or role on a non-terminal ticket.

- **Request Body:** `TicketAssignPayload` (assignee_user, assignee_role, version_index).
- **Expected Response:** `200 OK` with updated `TicketResponse`.

#### `POST /api/v1/tickets/{id}/comments`

Appends an auditable comment to a specific ticket.

- **Request Body:** `CommentCreate` (body).
- **Expected Response:** `201 Created` with a `CommentResponse` object.
- **Error Codes:** `404 Not Found` (parent ticket missing), `403 Forbidden` (scope boundary violation).

#### `GET /api/v1/tickets/{id}/comments`

Lists all comments for a ticket in ascending chronological order.

- **Expected Response:** `200 OK` with an array of `CommentResponse` objects.
- **Read-Audit Log:** Writes `TICKET_COMMENTS_VIEW` entry.

#### GET /api/v1/tickets/audit-logs

Retrieves ticket audit logs in descending chronological order.

- **Query Parameters:** `ticket_id`, `limit`, `offset`, `start_time`, `end_time`.
- **Expected Response:** `200 OK` with a `PaginatedTicketAuditLogResponse` envelope.
- **Error Codes:** `422 Unprocessable Entity` (limit < 1 or > 250, offset < 0).
- **Read-Audit Log:** Writes `TICKET_AUDIT_LOG_LIST` self-auditing entry.

---

**End of Specification.**
