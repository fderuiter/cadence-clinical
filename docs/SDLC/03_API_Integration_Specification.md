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
  description: 'Unified microservices API contract for Cadence Clinical Platform.

    Enforces OIDC/Keycloak authentication, RFC 7807 problem details, and ISO 14155:2020 regulatory compliance.'
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
      description: 'Export authored clinical protocol synopsis into PDF, DOCX, or HTML format.


        Requirements: PRD-SYS-001'
      operationId: export_synopsis_document_api_v1_synopsis_export_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/SynopsisExportRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SynopsisExportResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/synopsis/render/{study_id}:
    get:
      tags:
      - Synopsis
      summary: Render Synopsis Download
      description: 'Direct file download endpoint for rendered protocol synopsis documents.


        Requirements: PRD-SYS-001'
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
          description: 'Export format: pdf, docx, html'
          default: pdf
          title: Format
        description: 'Export format: pdf, docx, html'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema: {}
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/designer/sentinel/evaluate:
    post:
      tags:
      - QualitySentinel
      summary: Evaluate Protocol Quality Endpoint
      description: 'Evaluate authored protocol specification payload against quality and burden rules.


        Requirements: PRD-SYS-001'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ProtocolQualityScore'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/designer/cascade/propagate:
    post:
      tags:
      - ArtifactCascade
      summary: Propagate Cascade Endpoint
      description: 'Cascade authored USDM protocol specification changes to downstream eCRFs and SoA matrices.


        Requirements: PRD-SYS-001'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CascadeSummaryReport'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/designer/export/m11/{study_id}:
    get:
      tags:
      - ProtocolExport
      summary: Export Protocol M11 Endpoint
      description: 'Download authored protocol specification in formatted ICH M11 Word (.docx) or USDM JSON format.


        Requirements: PRD-SYS-001'
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
          description: 'Target export format: docx or json'
          default: docx
          title: Format
        description: 'Target export format: docx or json'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema: {}
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/FormReviewCommentResponse'
                title: Response Get Form Comments Api V1 Designer Forms  Form Id  Comments Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/CommentCreatePayload'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/FormReviewCommentResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/FormReviewCommentResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/designer/digitization/extract:
    post:
      tags:
      - protocol_digitization
      summary: Extract Protocol Digitization
      description: 'Extracts structured CDISC USDM v4.0 parameters from an uploaded clinical protocol file.


        Accepts PDF, DOCX, or text files.'
      operationId: extract_protocol_digitization_api_v1_designer_digitization_extract_post
      requestBody:
        content:
          multipart/form-data:
            schema:
              $ref: '#/components/schemas/Body_extract_protocol_digitization_api_v1_designer_digitization_extract_post'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/USDMProtocolExtractionResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/designer/digitization/sample:
    get:
      tags:
      - protocol_digitization
      summary: Get Sample Protocol Extraction
      description: Returns a canonical pre-extracted USDM v4.0 study structure for instant review and sandboxing.
      operationId: get_sample_protocol_extraction_api_v1_designer_digitization_sample_get
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/USDMProtocolExtractionResponse'
  /api/v1/designer/studies/{study_id}/commit-usdm:
    post:
      tags:
      - protocol_digitization
      summary: Commit Usdm To Study
      description: 'Commits extracted USDM entities to the Neo4j graph and synthesizes CDASH eCRF forms.


        Guarantees 21 CFR Part 11 audit attribution and GxP change justification.'
      operationId: commit_usdm_to_study_api_v1_designer_studies__study_id__commit_usdm_post
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
              $ref: '#/components/schemas/CommitUSDMRequest'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CommitUSDMResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/designer/digitization/dag/jobs:
    post:
      tags:
      - protocol_digitization
      summary: Start Digitization Dag Job
      description: 'Starts an asynchronous multi-stage DAG protocol digitization job.


        Decomposes ingestion into checkpointed transformations (layout parsing, SoA extraction,

        biomedical concept mapping, eCRF synthesis, and USDM compilation) with schema validation gates.'
      operationId: start_digitization_dag_job_api_v1_designer_digitization_dag_jobs_post
      requestBody:
        content:
          multipart/form-data:
            schema:
              $ref: '#/components/schemas/Body_start_digitization_dag_job_api_v1_designer_digitization_dag_jobs_post'
        required: true
      responses:
        '202':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/StartDAGJobResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/designer/digitization/dag/jobs/{job_id}:
    get:
      tags:
      - protocol_digitization
      summary: Get Digitization Dag Job Status
      description: Retrieves real-time execution status, stage checkpoints, and progress for a DAG job.
      operationId: get_digitization_dag_job_status_api_v1_designer_digitization_dag_jobs__job_id__get
      parameters:
      - name: job_id
        in: path
        required: true
        schema:
          type: string
          title: Job Id
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/DAGJobStatusResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/designer/digitization/dag/jobs/{job_id}/resume:
    post:
      tags:
      - protocol_digitization
      summary: Resume Digitization Dag Job
      description: Resumes execution of a paused or failed DAG digitization job from a specified checkpoint.
      operationId: resume_digitization_dag_job_api_v1_designer_digitization_dag_jobs__job_id__resume_post
      parameters:
      - name: job_id
        in: path
        required: true
        schema:
          type: string
          title: Job Id
      requestBody:
        content:
          application/json:
            schema:
              anyOf:
              - $ref: '#/components/schemas/ResumeDAGJobRequest'
              - type: 'null'
              title: Payload
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/DAGJobStatusResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/designer/digitization/dag/jobs/{job_id}/compile-usdm:
    post:
      tags:
      - protocol_digitization
      summary: Compile Usdm From Dag Job
      description: Commits the finalized USDM model generated by a completed DAG job into Neo4j with Part 11 audit attribution.
      operationId: compile_usdm_from_dag_job_api_v1_designer_digitization_dag_jobs__job_id__compile_usdm_post
      parameters:
      - name: job_id
        in: path
        required: true
        schema:
          type: string
          title: Job Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CompileUSDMFromJobRequest'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CommitUSDMResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/designer/amendments/branch:
    post:
      tags:
      - DesignerAmendments
      summary: Branch Protocol Amendment Endpoint
      description: 'Create an isolated, immutable protocol amendment working branch from an approved baseline.


        Requirements: PRD-SYS-001, PRD-SUB-007'
      operationId: branch_protocol_amendment_endpoint_api_v1_designer_amendments_branch_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/BranchAmendmentRequest'
        required: true
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/BranchAmendmentResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/designer/amendments/diff:
    post:
      tags:
      - DesignerAmendments
      summary: Compute Semantic Diff Endpoint
      description: 'Compute multi-layer visual and semantic protocol diff across USDM Graph, SoA, Eligibility, and eCRF forms.


        Requirements: PRD-SYS-001, PRD-SUB-007'
      operationId: compute_semantic_diff_endpoint_api_v1_designer_amendments_diff_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/SemanticDiffRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SemanticDiffResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/designer/amendments/{study_id}/diff:
    get:
      tags:
      - DesignerAmendments
      summary: Get Study Semantic Diff Endpoint
      description: 'Get multi-layer semantic diff for a study between two version tags.


        Requirements: PRD-SYS-001, PRD-SUB-007'
      operationId: get_study_semantic_diff_endpoint_api_v1_designer_amendments__study_id__diff_get
      parameters:
      - name: study_id
        in: path
        required: true
        schema:
          type: string
          title: Study Id
      - name: base_version
        in: query
        required: false
        schema:
          type: string
          default: 1.0.0
          title: Base Version
      - name: amended_version
        in: query
        required: false
        schema:
          type: string
          default: 2.0.0
          title: Amended Version
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SemanticDiffResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/designer/amendments/impact:
    post:
      tags:
      - DesignerAmendments
      summary: Compute Amendment Impact Endpoint
      description: 'Compute quantitative Amendment Impact Summary (burden delta, affected visits, schema revisions).


        Requirements: PRD-SYS-001, PRD-SUB-007'
      operationId: compute_amendment_impact_endpoint_api_v1_designer_amendments_impact_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/SemanticDiffRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/AmendmentImpactSummary'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/designer/amendments/analyze-ripple:
    post:
      tags:
      - DesignerAmendments
      summary: Analyze Ripple Effects Endpoint
      description: 'Analyze ripple-effect deltas across USDM graph, SoA matrix, and narrative to generate ProtocolImpactAssessment.


        Requirements: PRD-SYS-001, PRD-SUB-007, PRD-SYS-051'
      operationId: analyze_ripple_effects_endpoint_api_v1_designer_amendments_analyze_ripple_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/RippleAnalysisRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ProtocolImpactAssessment'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/designer/amendments/dispatch-tickets:
    post:
      tags:
      - DesignerAmendments
      summary: Dispatch Operational Tickets Endpoint
      description: 'Generate and dispatch multi-domain operational tickets for a protocol amendment to apps/tickets.


        Requirements: PRD-SYS-001, PRD-SUB-007, PRD-SYS-051'
      operationId: dispatch_operational_tickets_endpoint_api_v1_designer_amendments_dispatch_tickets_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/TicketDispatchRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/TicketDispatchResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Get Legacy Study Api V1 Studies  Study Id  Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
          - type: 'null'
          description: 'Output format: json or yaml'
          title: Format
        description: 'Output format: json or yaml'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                title: Response Get Usdm Study Api V2 Studies  Study Id  Usdm Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
    post:
      summary: Import Usdm Study
      description: 'Ingests, validates, maps, and persists a USDM JSON/YAML payload for a specific study.


        Requirements: Phase 2 Ingestion'
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
          - type: 'null'
          description: Optional explicit version override ('v2' or 'v3')
          title: Override
        description: Optional explicit version override ('v2' or 'v3')
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema: {}
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/admin/cache/clear:
    post:
      summary: Clear Cache
      description: "Flushes the controlled terminology cache.\n\nReturns:\n    Dict[str, str]: A success message indicating the cache was cleared."
      operationId: clear_cache_api_admin_cache_clear_post
      responses:
        '200':
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
        '200':
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/StudyAlignmentReport'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/StudyTerminologyValidationReport'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/StudyTerminologyValidationReport'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ConceptValidationReport'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
          - type: 'null'
          title: From Record
      - name: page_size
        in: query
        required: false
        schema:
          anyOf:
          - type: integer
          - type: 'null'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/TerminologySearchResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/DifferenceResult'
                title: Response Study Differences Api V1 Studies  Study Id  Differences Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/SectionTransitionRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SectionReviewTransition'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema: {}
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/CommentThreadCreate'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CommentThread'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/CommentThread'
                title: Response Get Threads Endpoint Api V1 Studies  Study Id  Sections  Section Id  Threads Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/CommentCreate'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CommentThread'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CommentThread'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/SuggestionCreate'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Suggestion'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/Suggestion'
                title: Response Get Suggestions Endpoint Api V1 Studies  Study Id  Blocks  Block Id  Suggestions Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/SuggestionDecisionRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Suggestion'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/designer/ingestion/upload:
    post:
      summary: Upload Protocol Ingestion
      operationId: upload_protocol_ingestion_api_v1_designer_ingestion_upload_post
      requestBody:
        content:
          multipart/form-data:
            schema:
              $ref: '#/components/schemas/Body_upload_protocol_ingestion_api_v1_designer_ingestion_upload_post'
        required: true
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema: {}
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema: {}
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema: {}
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/TransitionItemRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema: {}
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/PromoteRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema: {}
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoAEntityCreatedResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoAEntityDetail'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/UpdateStudyArmRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoAEntityCreatedResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoAEntityCreatedResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoAEntityDetail'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/UpdateEpochRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoAEntityCreatedResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoAEntityCreatedResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoAEntityDetail'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/apps__designer__domain__protocol_authoring__soa__UpdateVisitRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoAEntityCreatedResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoAEntityCreatedResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoAEntityDetail'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/UpdateProcedureRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoAEntityCreatedResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoAEntityCreatedResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoAEntityDetail'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/UpdateTimingWindowRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoAEntityCreatedResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/LinkEpochVisitRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoALinkResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/LinkEpochVisitRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoALinkResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/LinkVisitProcedureRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoALinkResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/LinkVisitProcedureRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoALinkResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/LinkTimingRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoALinkResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/LinkTimingRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoALinkResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/LinkArmApplicabilityRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoALinkResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/LinkArmApplicabilityRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoALinkResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/studies/{study_id}/export:
    get:
      summary: Export Protocol
      description: 'Assembles a study version''s data, maps it to a canonical USDM content model,

        and renders the resulting clinical protocol document as a structurally valid

        PDF or DOCX document using shared layout templates.'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema: {}
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/studies/{study_id}/versions/{version_id}/sign-off:
    post:
      summary: Approve Study Version Endpoint
      description: 'Approve and cryptographically sign a Metadata Designer clinical protocol version, producing

        a 21 CFR Part 11 compliant persisted signature manifestation, recording immutable Action history,

        and locks the protocol version by transitioning its status to APPROVED.

        Locked statuses (APPROVED, SIGNED) block any subsequent edits via immutability checks across both Neo4j and mock databases.

        Successfully approved protocols are archived as PROTOCOL_SIGNOFF artifacts to the eTMF service asynchronously.'
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
              $ref: '#/components/schemas/ApproveProtocolRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Approve Study Version Endpoint Api V1 Studies  Study Id  Versions  Version Id  Sign Off Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/studies/{study_id}/versions/{version_id}/approve:
    post:
      summary: Approve Study Version Endpoint
      description: 'Approve and cryptographically sign a Metadata Designer clinical protocol version, producing

        a 21 CFR Part 11 compliant persisted signature manifestation, recording immutable Action history,

        and locks the protocol version by transitioning its status to APPROVED.

        Locked statuses (APPROVED, SIGNED) block any subsequent edits via immutability checks across both Neo4j and mock databases.

        Successfully approved protocols are archived as PROTOCOL_SIGNOFF artifacts to the eTMF service asynchronously.'
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
              $ref: '#/components/schemas/ApproveProtocolRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Approve Study Version Endpoint Api V1 Studies  Study Id  Versions  Version Id  Approve Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/studies/{study_id}/versions/{version_id}/publish:
    post:
      summary: Publish Study Version Endpoint
      description: "Two-Phase Study Version State Machine:\n1. transition target version status to 'PENDING_PUBLISH'\n2. synchronously call downstream execution setup / clinical migration\n3. if downstream succeeds, transition target version status to 'ACTIVE'\n4. if downstream fails or times out (15s window), rollback status to 'DRAFT',\n   record exact execution error to audit trail, release lock resources,\n   and return validation error to initiating researcher."
      operationId: publish_study_version_endpoint_api_v1_studies__study_id__versions__version_id__publish_post
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
              $ref: '#/components/schemas/PublishStudyVersionRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PublishStudyVersionResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/EligibilityCriterion'
                title: Response List Eligibility Criteria Api V1 Studies  Study Id  Eligibility Criteria Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/CreateEligibilityCriterionRequest'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/EligibilityCriterion'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/EligibilityCriterion'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/UpdateEligibilityCriterionRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/EligibilityCriterion'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/mappings/upload:
    post:
      summary: Upload Mapping Csv
      description: "Validates a CSV mapping configuration to ensure target names meet standard W3C XML naming specifications.\n\nRaises:\n    HTTPException: If the CSV format is invalid or if target XML names violate naming rules."
      operationId: upload_mapping_csv_api_v1_mappings_upload_post
      requestBody:
        content:
          multipart/form-data:
            schema:
              $ref: '#/components/schemas/Body_upload_mapping_csv_api_v1_mappings_upload_post'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema: {}
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/designer/usdm/validate:
    post:
      summary: Validate Usdm Endpoint
      description: 'Validates a USDM JSON or YAML payload, normalizes shape differences, and returns a detailed validation report.

        If the payload is invalid, raises a structured HTTP 422 ProblemDetails response.'
      operationId: validate_usdm_endpoint_api_v1_designer_usdm_validate_post
      parameters:
      - name: override
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Optional explicit version override ('v2' or 'v3')
          title: Override
        description: Optional explicit version override ('v2' or 'v3')
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema: {}
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema: {}
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
          - $ref: '#/components/schemas/TerminologyEnum'
          - type: 'null'
          title: Terminology
      - name: domain
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
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
          - type: 'null'
          title: Starting After
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ConceptListResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
    post:
      summary: Create Concept
      description: Creates a new Biomedical Concept inside the MDR graph repository.
      operationId: create_concept_api_v1_mdr_concepts_post
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreateConceptRequest'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ConceptDetail'
        '400':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ProblemDetails'
          description: Bad Request
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/UpdateConceptRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ConceptDetail'
        '400':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ProblemDetails'
          description: Bad Request
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Delete Concept Api V1 Mdr Concepts  Id  Delete
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/RenameConceptRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ConceptDetail'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              - $ref: '#/components/schemas/CreateFormRequest'
              - $ref: '#/components/schemas/CreateDataElementRequest'
              - $ref: '#/components/schemas/CreateArmRequest'
              - $ref: '#/components/schemas/apps__designer__library__CreateVisitRequest'
              discriminator:
                propertyName: object_type
                mapping:
                  FORM: '#/components/schemas/CreateFormRequest'
                  DATA_ELEMENT: '#/components/schemas/CreateDataElementRequest'
                  ARM: '#/components/schemas/CreateArmRequest'
                  VISIT: '#/components/schemas/apps__designer__library__CreateVisitRequest'
              title: Payload
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                oneOf:
                - $ref: '#/components/schemas/FormLibraryObjectDetail'
                - $ref: '#/components/schemas/DataElementLibraryObjectDetail'
                - $ref: '#/components/schemas/ArmLibraryObjectDetail'
                - $ref: '#/components/schemas/VisitLibraryObjectDetail'
                discriminator:
                  propertyName: object_type
                  mapping:
                    FORM: '#/components/schemas/FormLibraryObjectDetail'
                    DATA_ELEMENT: '#/components/schemas/DataElementLibraryObjectDetail'
                    ARM: '#/components/schemas/ArmLibraryObjectDetail'
                    VISIT: '#/components/schemas/VisitLibraryObjectDetail'
                title: Response Create Library Object Endpoint Api V1 Mdr Library Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
    get:
      summary: List Library Objects Endpoint
      description: 'Lists latest global library objects under the authenticated sponsor.

        Supports Stripe-style cursor-based pagination.'
      operationId: list_library_objects_endpoint_api_v1_mdr_library_get
      parameters:
      - name: object_type
        in: query
        required: false
        schema:
          anyOf:
          - $ref: '#/components/schemas/ObjectType'
          - type: 'null'
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
          - type: 'null'
          title: Starting After
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/LibraryObjectListResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
          - type: 'null'
          title: Version
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                oneOf:
                - $ref: '#/components/schemas/FormLibraryObjectDetail'
                - $ref: '#/components/schemas/DataElementLibraryObjectDetail'
                - $ref: '#/components/schemas/ArmLibraryObjectDetail'
                - $ref: '#/components/schemas/VisitLibraryObjectDetail'
                discriminator:
                  propertyName: object_type
                  mapping:
                    FORM: '#/components/schemas/FormLibraryObjectDetail'
                    DATA_ELEMENT: '#/components/schemas/DataElementLibraryObjectDetail'
                    ARM: '#/components/schemas/ArmLibraryObjectDetail'
                    VISIT: '#/components/schemas/VisitLibraryObjectDetail'
                title: Response Get Library Object Endpoint Api V1 Mdr Library  Id  Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              - $ref: '#/components/schemas/UpdateFormRequest'
              - $ref: '#/components/schemas/UpdateDataElementRequest'
              - $ref: '#/components/schemas/UpdateArmRequest'
              - $ref: '#/components/schemas/apps__designer__library__UpdateVisitRequest'
              discriminator:
                propertyName: object_type
                mapping:
                  FORM: '#/components/schemas/UpdateFormRequest'
                  DATA_ELEMENT: '#/components/schemas/UpdateDataElementRequest'
                  ARM: '#/components/schemas/UpdateArmRequest'
                  VISIT: '#/components/schemas/apps__designer__library__UpdateVisitRequest'
              title: Payload
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                oneOf:
                - $ref: '#/components/schemas/FormLibraryObjectDetail'
                - $ref: '#/components/schemas/DataElementLibraryObjectDetail'
                - $ref: '#/components/schemas/ArmLibraryObjectDetail'
                - $ref: '#/components/schemas/VisitLibraryObjectDetail'
                discriminator:
                  propertyName: object_type
                  mapping:
                    FORM: '#/components/schemas/FormLibraryObjectDetail'
                    DATA_ELEMENT: '#/components/schemas/DataElementLibraryObjectDetail'
                    ARM: '#/components/schemas/ArmLibraryObjectDetail'
                    VISIT: '#/components/schemas/VisitLibraryObjectDetail'
                title: Response Update Library Object Endpoint Api V1 Mdr Library  Id  Put
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/LibraryObjectAmendRequest'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                oneOf:
                - $ref: '#/components/schemas/FormLibraryObjectDetail'
                - $ref: '#/components/schemas/DataElementLibraryObjectDetail'
                - $ref: '#/components/schemas/ArmLibraryObjectDetail'
                - $ref: '#/components/schemas/VisitLibraryObjectDetail'
                discriminator:
                  propertyName: object_type
                  mapping:
                    FORM: '#/components/schemas/FormLibraryObjectDetail'
                    DATA_ELEMENT: '#/components/schemas/DataElementLibraryObjectDetail'
                    ARM: '#/components/schemas/ArmLibraryObjectDetail'
                    VISIT: '#/components/schemas/VisitLibraryObjectDetail'
                title: Response Amend Library Object Endpoint Api V1 Mdr Library  Id  Amend Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  oneOf:
                  - $ref: '#/components/schemas/FormLibraryObjectDetail'
                  - $ref: '#/components/schemas/DataElementLibraryObjectDetail'
                  - $ref: '#/components/schemas/ArmLibraryObjectDetail'
                  - $ref: '#/components/schemas/VisitLibraryObjectDetail'
                  discriminator:
                    propertyName: object_type
                    mapping:
                      FORM: '#/components/schemas/FormLibraryObjectDetail'
                      DATA_ELEMENT: '#/components/schemas/DataElementLibraryObjectDetail'
                      ARM: '#/components/schemas/ArmLibraryObjectDetail'
                      VISIT: '#/components/schemas/VisitLibraryObjectDetail'
                title: Response Get Library Object History Endpoint Api V1 Mdr Library  Id  History Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/mdr/library/{id}/transition:
    post:
      summary: Transition Library Object Endpoint
      description: 'Transitions the lifecycle status of a global library object.

        Enforces a strict role-gated ALLOWED_LIBRARY_TRANSITIONS map.'
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
              $ref: '#/components/schemas/LibraryObjectTransitionRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                oneOf:
                - $ref: '#/components/schemas/FormLibraryObjectDetail'
                - $ref: '#/components/schemas/DataElementLibraryObjectDetail'
                - $ref: '#/components/schemas/ArmLibraryObjectDetail'
                - $ref: '#/components/schemas/VisitLibraryObjectDetail'
                discriminator:
                  propertyName: object_type
                  mapping:
                    FORM: '#/components/schemas/FormLibraryObjectDetail'
                    DATA_ELEMENT: '#/components/schemas/DataElementLibraryObjectDetail'
                    ARM: '#/components/schemas/ArmLibraryObjectDetail'
                    VISIT: '#/components/schemas/VisitLibraryObjectDetail'
                title: Response Transition Library Object Endpoint Api V1 Mdr Library  Id  Transition Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/studies/{study_id}/versions:
    post:
      summary: Post Study Version
      description: 'Establishes a new StudyVersion node under a clinical study.

        Enforces that concurrent creation with duplicate index or tag fails with 409 Conflict.'
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
              $ref: '#/components/schemas/CreateStudyVersionRequest'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Post Study Version Api V1 Studies  Study Id  Versions Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  type: object
                  additionalProperties: true
                title: Response Get Study Rules Api V1 Studies  Study Id  Rules Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/CreateRuleRequest'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Create Study Rule Api V1 Studies  Study Id  Rules Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Get Study Rule By Id Api V1 Studies  Study Id  Rules  Rule Id  Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/CreateRuleRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Update Study Rule By Id Api V1 Studies  Study Id  Rules  Rule Id  Put
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Delete Study Rule By Id Api V1 Studies  Study Id  Rules  Rule Id  Delete
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/studies/{study_id}/rules/preview:
    post:
      summary: Compile Preview Rule
      description: 'Read-only compile and validation preview route.

        Detects unknown field references and circular skip-logic dependencies.'
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
              $ref: '#/components/schemas/CreateRuleRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/RulePreviewResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/studies/{study_id}/rules/validate:
    post:
      summary: Compile Validate Rule
      description: 'Read-only compile and validation preview route.

        Detects unknown field references and circular skip-logic dependencies.'
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
              $ref: '#/components/schemas/CreateRuleRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/RulePreviewResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/designer/protocols/{id}/amend:
    post:
      summary: Amend Protocol
      description: 'Exposes POST /api/designer/protocols/{id}/amend with 201, new_version, status, and parent_version.

        Creates a transaction-safe DRAFT successor with incremented version index.'
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
              $ref: '#/components/schemas/ProtocolAmendRequest'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Amend Protocol Api Designer Protocols  Id  Amend Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/CreateBlockRequest'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/BlockCreatedResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/BlockDetailResponse'
                title: Response List Blocks Endpoint Api V1 Studies  Study Id  Versions  Version Id  Blocks Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/UpdateBlockRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/BlockCreatedResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/BlockCreatedResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/BlockDetailResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/ReorderBlocksRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoALinkResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/CreateStudyArmRequest'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoAEntityCreatedResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/SoAEntityDetail'
                title: Response List Arms Endpoint Api V1 Studies  Study Id  Versions  Version Id  Arms Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/CreateEpochRequest'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoAEntityCreatedResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/SoAEntityDetail'
                title: Response List Epochs Endpoint Api V1 Studies  Study Id  Versions  Version Id  Epochs Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/apps__designer__domain__protocol_authoring__soa__CreateVisitRequest'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoAEntityCreatedResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/SoAEntityDetail'
                title: Response List Visits Endpoint Api V1 Studies  Study Id  Versions  Version Id  Visits Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/CreateProcedureRequest'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoAEntityCreatedResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/SoAEntityDetail'
                title: Response List Procedures Endpoint Api V1 Studies  Study Id  Versions  Version Id  Procedures Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/CreateTimingWindowRequest'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoAEntityCreatedResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/SoAEntityDetail'
                title: Response List Timing Windows Endpoint Api V1 Studies  Study Id  Versions  Version Id  Timing Windows Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SoAMatrixView'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/ArmReorderRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Reorder Arms Endpoint Api V1 Studies  Study Id  Versions  Version Id  Arms Reorder Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/EpochReorderRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Reorder Epochs Endpoint Api V1 Studies  Study Id  Versions  Version Id  Epochs Reorder Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/VisitReorderRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Reorder Visits Endpoint Api V1 Studies  Study Id  Versions  Version Id  Visits Reorder Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/ProcedureReorderRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Reorder Procedures Endpoint Api V1 Studies  Study Id  Versions  Version Id  Procedures Reorder Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/ActivityAssignmentRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Assign Activities To Visit Endpoint Api V1 Studies  Study Id  Versions  Version Id  Assignments Activities Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/VisitToArmAssignmentRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Assign Visits To Arm Endpoint Api V1 Studies  Study Id  Versions  Version Id  Assignments Arms Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/VisitToEpochAssignmentRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Assign Visits To Epoch Endpoint Api V1 Studies  Study Id  Versions  Version Id  Assignments Epochs Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/studies/{study_id}/versions/diff:
    get:
      summary: Get Versions Diff Endpoint
      description: 'Exposes graph-native, form-level version-diff API.

        Identifies additions, modifications, and deletions of forms.

        Returns HTTP 400 Bad Request if either version is nonexistent or unrelated.'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/VersionDiffResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/studies/{study_id}/library-instances:
    post:
      summary: Instantiate Library Object Endpoint
      description: 'Instantiates a specific version (or latest) of a Global Library object into a study-scoped instance.

        Enforces that the library object and study both belong to/are accessible by the authenticated sponsor.'
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
              $ref: '#/components/schemas/InstantiateLibraryObjectRequest'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/LibraryInstanceResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/studies/{study_id}/library-instances/{instance_id}:
    put:
      summary: Update Library Instance Endpoint
      description: 'Updates the payload of an instantiated library object inside a study.

        Verifies that target study belongs to or is accessible by the authenticated sponsor,

        leaving the global library source immutable.'
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
              $ref: '#/components/schemas/UpdateLibraryInstanceRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/LibraryInstanceResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/DifferenceResult'
                title: Response Get Library Instance Diff Endpoint Api V1 Studies  Study Id  Library Instances  Instance Id  Diff Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/designer/startup-matrix/presets:
    get:
      tags:
      - StartupPrerequisiteMatrix
      summary: Get Startup Presets Endpoint
      description: Retrieves standard statutory startup criteria presets across international jurisdictions.
      operationId: get_startup_presets_endpoint_api_v1_designer_startup_matrix_presets_get
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties:
                  items:
                    additionalProperties: true
                    type: object
                  type: array
                type: object
                title: Response Get Startup Presets Endpoint Api V1 Designer Startup Matrix Presets Get
  /api/v1/designer/startup-matrix/jurisdictions:
    get:
      tags:
      - StartupPrerequisiteMatrix
      summary: Get Supported Jurisdictions Endpoint
      description: Lists supported international jurisdictions with regulatory and ethics governance frameworks.
      operationId: get_supported_jurisdictions_endpoint_api_v1_designer_startup_matrix_jurisdictions_get
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                items:
                  $ref: '#/components/schemas/JurisdictionInfo'
                type: array
                title: Response Get Supported Jurisdictions Endpoint Api V1 Designer Startup Matrix Jurisdictions Get
  /api/v1/designer/startup-matrix/generate-template:
    post:
      tags:
      - StartupPrerequisiteMatrix
      summary: Generate Startup Template Endpoint
      description: Generates country-specific startup prerequisite criteria matrix for study protocol design.
      operationId: generate_startup_template_endpoint_api_v1_designer_startup_matrix_generate_template_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GenerateStartupTemplateRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/GenerateStartupTemplateResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /health:
    get:
      summary: Health Check
      description: "Service health check endpoint.\n\nReturns a basic JSON payload indicating the service is operational.\n\nReturns:\n    dict[str, str]: The health status payload."
      operationId: health_check_health_get
      responses:
        '200':
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
      description: 'Execute study, site, subject, visit, form, or field-level data lock or freeze operation.


        Requirements: PRD-SYS-001, PRD-SYS-002, Trace-1, Trace-3, Trace-17'
      operationId: lock_data_endpoint_api_v1_execution_locks_lock_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/DataLockRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/DataLockResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/locks/unlock:
    post:
      tags:
      - DataLock
      summary: Unlock Data Endpoint
      description: 'Execute GxP data unlock override operation enforcing >= 50 chars justification.


        Requirements: PRD-SYS-001, PRD-SYS-002, Trace-1, Trace-3, Trace-17'
      operationId: unlock_data_endpoint_api_v1_execution_locks_unlock_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/DataLockRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/DataLockResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/locks/status/{form_id}:
    get:
      tags:
      - DataLock
      summary: Get Form Lock Status Endpoint
      description: 'Retrieve active data locks for specified eCRF form submission.


        Requirements: PRD-SYS-001, Trace-1, Trace-17'
      operationId: get_form_lock_status_endpoint_api_v1_execution_locks_status__form_id__get
      parameters:
      - name: form_id
        in: path
        required: true
        schema:
          type: string
          title: Form Id
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/DataLockRecord'
                title: Response Get Form Lock Status Endpoint Api V1 Execution Locks Status  Form Id  Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/locks:
    get:
      summary: Get Lock Status
      description: Retrieve the current lock/freeze status of sites, visits, forms, subjects, and study-wide trial.
      operationId: get_lock_status_api_v1_execution_locks_get
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/LockStatusResponse'
  /api/v1/execution/locks/tree:
    get:
      tags:
      - DataLock
      summary: Get Lock Hierarchy Tree Endpoint
      description: Retrieve complete lock status tree across Study -> Sites -> Subjects -> Visits -> Forms hierarchy.
      operationId: get_lock_hierarchy_tree_endpoint_api_v1_execution_locks_tree_get
      parameters:
      - name: study_id
        in: query
        required: false
        schema:
          type: string
          description: Study ID to query
          default: STUDY-001
          title: Study Id
        description: Study ID to query
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Get Lock Hierarchy Tree Endpoint Api V1 Execution Locks Tree Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/signatures/sign:
    post:
      tags:
      - Signatures
      summary: Sign Payload Endpoint
      description: 'Signs a given string payload using the secure backend PKCS#7 signer.


        Requirements: PRD-SYS-001'
      operationId: sign_payload_endpoint_api_v1_execution_signatures_sign_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/SignRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SignResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/signatures/verify:
    post:
      tags:
      - Signatures
      summary: Verify Signature Endpoint
      description: 'Verifies a PKCS#7 signed document payload using the ESignatureVerifier and trust store.


        Requirements: PRD-SYS-001'
      operationId: verify_signature_endpoint_api_v1_execution_signatures_verify_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/VerifyRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/VerifyResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/signatures/batch-sign-off:
    post:
      tags:
      - Signatures
      summary: Batch Signature Sign Off Endpoint
      description: 'Execute 21 CFR Part 11 batch electronic signature casebook sign-off.


        Requirements: PRD-SYS-001'
      operationId: batch_signature_sign_off_endpoint_api_v1_execution_signatures_batch_sign_off_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/BatchSignatureRequest'
        required: true
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/BatchSignatureResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/amendments/publish:
    post:
      tags:
      - Amendments
      summary: Publish Amendment Endpoint
      description: 'Publish protocol amendment version and compute structural summary of changes.


        Requirements: PRD-SYS-001'
      operationId: publish_amendment_endpoint_api_v1_execution_amendments_publish_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/PublishAmendmentRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PublishAmendmentResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/amendments/summary/{study_id}/{version}:
    get:
      tags:
      - Amendments
      summary: Get Amendment Summary Endpoint
      description: 'Export Summary of Changes report for specified study version.


        Requirements: PRD-SYS-001'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Get Amendment Summary Endpoint Api V1 Execution Amendments Summary  Study Id   Version  Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/amendments/{study_id}/subject-impact:
    get:
      tags:
      - Amendments
      summary: Get Subject Impact Analysis
      description: 'Calculates subject migration impact for an active protocol amendment.


        Requirements: PRD-SYS-001, PRD-SUB-007'
      operationId: get_subject_impact_analysis_api_v1_execution_amendments__study_id__subject_impact_get
      parameters:
      - name: study_id
        in: path
        required: true
        schema:
          type: string
          title: Study Id
      - name: target_version
        in: query
        required: false
        schema:
          type: string
          default: 2.0.0
          title: Target Version
      - name: site_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Site Id
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Get Subject Impact Analysis Api V1 Execution Amendments  Study Id  Subject Impact Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/amendments/validate-gating:
    post:
      tags:
      - Amendments
      summary: Validate Gating Endpoint
      description: 'Validates subject re-consent gating before form data entry.


        Requirements: PRD-SUB-007'
      operationId: validate_gating_endpoint_api_v1_execution_amendments_validate_gating_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ValidateGatingRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties: true
                type: object
                title: Response Validate Gating Endpoint Api V1 Execution Amendments Validate Gating Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/amendments/reconsent:
    post:
      tags:
      - Amendments
      summary: Register Subject Reconsent
      description: 'Registers subject signed re-consent for an amended version, unlocking gating.


        Requirements: PRD-SUB-007, PRD-SYS-001'
      operationId: register_subject_reconsent_api_v1_execution_amendments_reconsent_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/SubjectReconsentRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties: true
                type: object
                title: Response Register Subject Reconsent Api V1 Execution Amendments Reconsent Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/amendments/bulk-reconsent:
    post:
      tags:
      - Amendments
      summary: Register Bulk Subject Reconsent
      description: 'Registers batch subject re-consent for an amended version, unlocking gating in bulk.


        Requirements: PRD-SUB-007, PRD-SYS-001'
      operationId: register_bulk_subject_reconsent_api_v1_execution_amendments_bulk_reconsent_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/BulkSubjectReconsentRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties: true
                type: object
                title: Response Register Bulk Subject Reconsent Api V1 Execution Amendments Bulk Reconsent Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/amendments/verify-training-gate:
    post:
      tags:
      - Amendments
      summary: Verify Training Gate Endpoint
      description: 'Programmatically verify training qualification for EDC write and signature permissions.


        Requirements: PRD-TRN-002'
      operationId: verify_training_gate_endpoint_api_v1_execution_amendments_verify_training_gate_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/VerifyTrainingGatingRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties: true
                type: object
                title: Response Verify Training Gate Endpoint Api V1 Execution Amendments Verify Training Gate Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/anomalies/evaluate:
    post:
      tags:
      - Anomalies
      summary: Evaluate Anomalies
      description: Evaluates cross-domain clinical data for a subject and stages detected candidate queries.
      operationId: evaluate_anomalies_api_v1_execution_anomalies_evaluate_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/AnomalyEvaluateRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/AnomalyEvaluateResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/anomalies/candidates:
    get:
      tags:
      - Anomalies
      summary: List Candidate Queries
      description: Lists staged CANDIDATE clinical queries flagged by the anomaly worker for review.
      operationId: list_candidate_queries_api_v1_execution_anomalies_candidates_get
      parameters:
      - name: study_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Filter by study ID
          title: Study Id
        description: Filter by study ID
      - name: subject_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Filter by subject ID
          title: Subject Id
        description: Filter by subject ID
      - name: domain
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Filter by primary domain
          title: Domain
        description: Filter by primary domain
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/AnomalyCandidateItem'
                title: Response List Candidate Queries Api V1 Execution Anomalies Candidates Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/anomalies/candidates/{query_id}/adjudicate:
    post:
      tags:
      - Anomalies
      summary: Adjudicate Candidate Query
      description: 'Adjudicates a staged CANDIDATE query: promotes to OPEN or dismisses to CANCELLED.'
      operationId: adjudicate_candidate_query_api_v1_execution_anomalies_candidates__query_id__adjudicate_post
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
              $ref: '#/components/schemas/AdjudicationRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/AdjudicationResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/auditor/token/generate:
    post:
      tags:
      - Auditor
      summary: Generate Auditor Token Endpoint
      description: 'Generate temporary time-bounded access token for regulatory auditors.


        Requirements: PRD-SYS-001'
      operationId: generate_auditor_token_endpoint_api_v1_execution_auditor_token_generate_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GenerateAuditorTokenRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties: true
                type: object
                title: Response Generate Auditor Token Endpoint Api V1 Execution Auditor Token Generate Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/auditor/inspect/audit-trail/{study_id}:
    get:
      tags:
      - Auditor
      summary: Inspect Study Audit Trail Endpoint
      description: 'Expose read-only 21 CFR Part 11 audit trail inspection endpoint for study.


        Requirements: PRD-SYS-001'
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
          - type: 'null'
          default: 100
          title: Limit
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Inspect Study Audit Trail Endpoint Api V1 Execution Auditor Inspect Audit Trail  Study Id  Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/cec/dossiers:
    post:
      tags:
      - CEC Adjudication
      summary: Create And Package Dossier
      description: 'Compiles and packages a de-identified clinical event dossier for 2+1 blinded adjudication.


        @req:PRD-CEC-001, @req:PRD-SYS-124, @req:ADR-2199'
      operationId: create_and_package_dossier_api_v1_execution_cec_dossiers_post
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/DossierPackageCreateDTO'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Create And Package Dossier Api V1 Execution Cec Dossiers Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
    get:
      tags:
      - CEC Adjudication
      summary: List Dossiers
      description: 'Lists clinical event dossiers with filtering options.


        @req:PRD-CEC-002'
      operationId: list_dossiers_api_v1_execution_cec_dossiers_get
      parameters:
      - name: study_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Filter by study ID
          title: Study Id
        description: Filter by study ID
      - name: status
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Filter by workflow status
          title: Status
        description: Filter by workflow status
      - name: category
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Filter by candidate event category
          title: Category
        description: Filter by candidate event category
      - name: reader_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Filter by assigned reader
          title: Reader Id
        description: Filter by assigned reader
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/CecDossierSummaryItem'
                title: Response List Dossiers Api V1 Execution Cec Dossiers Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/cec/dossiers/{dossier_id}:
    get:
      tags:
      - CEC Adjudication
      summary: Get Dossier Blinded View
      description: 'Retrieves a role-blinded projection of a dossier, enforcing reader isolation.


        @req:PRD-CEC-001, @req:PRD-CEC-002, @req:ADR-2199'
      operationId: get_dossier_blinded_view_api_v1_execution_cec_dossiers__dossier_id__get
      parameters:
      - name: dossier_id
        in: path
        required: true
        schema:
          type: string
          title: Dossier Id
      - name: x-reader-role
        in: header
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Reader Role override for testing/simulation
          title: X-Reader-Role
        description: Reader Role override for testing/simulation
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/DossierBlindedView'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/cec/dossiers/{dossier_id}/full:
    get:
      tags:
      - CEC Adjudication
      summary: Get Dossier Full
      description: 'Retrieves unblinded full dossier details for Committee Chair, Data Manager, or Auditor.


        @req:PRD-CEC-002, @req:PRD-SYS-001'
      operationId: get_dossier_full_api_v1_execution_cec_dossiers__dossier_id__full_get
      parameters:
      - name: dossier_id
        in: path
        required: true
        schema:
          type: string
          title: Dossier Id
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/DossierDetailResponseDTO'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/cec/dossiers/{dossier_id}/assessments:
    post:
      tags:
      - CEC Adjudication
      summary: Submit Reader Assessment
      description: 'Submits an expert reader assessment for Reader 1 or Reader 2 and auto-evaluates consensus.


        @req:PRD-CEC-002, @req:ADR-2199'
      operationId: submit_reader_assessment_api_v1_execution_cec_dossiers__dossier_id__assessments_post
      parameters:
      - name: dossier_id
        in: path
        required: true
        schema:
          type: string
          title: Dossier Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ReaderAssessmentSubmitDTO'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Submit Reader Assessment Api V1 Execution Cec Dossiers  Dossier Id  Assessments Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/cec/dossiers/{dossier_id}/chair-adjudicate:
    post:
      tags:
      - CEC Adjudication
      summary: Adjudicate Chair Tie Breaker
      description: 'Executes Adjudication Committee Chair tie-breaker ruling with 21 CFR Part 11 electronic signature.


        @req:PRD-CEC-002, @req:PRD-SYS-124, @req:ADR-2199'
      operationId: adjudicate_chair_tie_breaker_api_v1_execution_cec_dossiers__dossier_id__chair_adjudicate_post
      parameters:
      - name: dossier_id
        in: path
        required: true
        schema:
          type: string
          title: Dossier Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ChairAdjudicateDTO'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Adjudicate Chair Tie Breaker Api V1 Execution Cec Dossiers  Dossier Id  Chair Adjudicate Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/cec/dossiers/{dossier_id}/writeback:
    post:
      tags:
      - CEC Adjudication
      summary: Trigger Edc Writeback
      description: 'Manually or automatically triggers bi-directional EDC casebook writeback for an adjudicated event.


        @req:PRD-CEC-003'
      operationId: trigger_edc_writeback_api_v1_execution_cec_dossiers__dossier_id__writeback_post
      parameters:
      - name: dossier_id
        in: path
        required: true
        schema:
          type: string
          title: Dossier Id
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/EdcCasebookWritebackResult'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/cec/charters/{study_id}:
    get:
      tags:
      - CEC Adjudication
      summary: Get Study Charter
      description: 'Retrieves active CEC charter governance configuration.


        @req:PRD-SYS-124'
      operationId: get_study_charter_api_v1_execution_cec_charters__study_id__get
      parameters:
      - name: study_id
        in: path
        required: true
        schema:
          type: string
          title: Study Id
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CecCharterConfig'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/cec/charters:
    post:
      tags:
      - CEC Adjudication
      summary: Create Or Update Charter
      description: 'Configures or updates committee charter voting rules, quorum, and diagnostic criteria forms.


        @req:PRD-SYS-124'
      operationId: create_or_update_charter_api_v1_execution_cec_charters_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CharterCreateDTO'
        required: true
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CecCharterConfig'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/cec/meetings:
    post:
      tags:
      - CEC Adjudication
      summary: Record Meeting Quorum
      description: 'Records committee meeting attendance, evaluates quorum compliance, and captures minutes.


        @req:PRD-SYS-124'
      operationId: record_meeting_quorum_api_v1_execution_cec_meetings_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/MeetingQuorumRecordDTO'
        required: true
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties: true
                type: object
                title: Response Record Meeting Quorum Api V1 Execution Cec Meetings Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/cec/meetings/{study_id}:
    get:
      tags:
      - CEC Adjudication
      summary: List Study Meetings
      description: 'Lists all recorded committee meetings and quorum statuses for a study.


        @req:PRD-SYS-124'
      operationId: list_study_meetings_api_v1_execution_cec_meetings__study_id__get
      parameters:
      - name: study_id
        in: path
        required: true
        schema:
          type: string
          title: Study Id
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  type: object
                  additionalProperties: true
                title: Response List Study Meetings Api V1 Execution Cec Meetings  Study Id  Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/cec/sdtm-ce/compile:
    post:
      tags:
      - CEC Adjudication
      summary: Compile Sdtm Ce Dataset
      description: 'Compiles all finalized adjudicated events for a study directly into CDISC SDTM CE dataset.


        @req:PRD-CEC-003, @req:PRD-SYS-001'
      operationId: compile_sdtm_ce_dataset_api_v1_execution_cec_sdtm_ce_compile_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/SdtmCeCompileRequestDTO'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SdtmCeCompilationResult'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/safety/dispatch:
    post:
      tags:
      - Safety
      summary: Dispatch Safety Report Endpoint
      description: 'Dispatch ICH E2B(R3) safety report to external pharmacovigilance gateway.


        Requirements: PRD-SYS-001'
      operationId: dispatch_safety_report_endpoint_api_v1_execution_safety_dispatch_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/SafetyDispatchRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SafetyDispatchResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/safety/reconcile:
    post:
      tags:
      - Safety
      summary: Reconcile Sae Cases Endpoint
      description: 'Execute automated EDC AE to Safety ICSR case reconciliation.


        Requirements: PRD-SYS-001'
      operationId: reconcile_sae_cases_endpoint_api_v1_execution_safety_reconcile_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/SAEReconcileRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties: true
                type: object
                title: Response Reconcile Sae Cases Endpoint Api V1 Execution Safety Reconcile Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/eisf/upload:
    post:
      tags:
      - eISF
      summary: Upload Eisf Document Endpoint
      description: 'Upload eISF regulatory binder document and calculate SHA-256 integrity checksum.


        Requirements: PRD-SYS-001'
      operationId: upload_eisf_document_endpoint_api_v1_execution_eisf_upload_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/UploadEISFDocumentRequest'
        required: true
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/EISFDocumentRecord'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/eisf/binder/{study_id}/{site_id}:
    get:
      tags:
      - eISF
      summary: Get Site Regulatory Binder Endpoint
      description: 'Retrieve site-isolated regulatory binder documents for specified study and site.


        Requirements: PRD-SYS-001'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/EISFDocumentRecord'
                title: Response Get Site Regulatory Binder Endpoint Api V1 Execution Eisf Binder  Study Id   Site Id  Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/anonymization/scan-phi:
    post:
      tags:
      - Anonymization
      summary: Scan Phi Endpoint
      description: 'Scan text payload for Protected Health Information (PHI) identifiers.


        Requirements: PRD-SYS-001'
      operationId: scan_phi_endpoint_api_v1_execution_anonymization_scan_phi_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/PHIScanRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties: true
                type: object
                title: Response Scan Phi Endpoint Api V1 Execution Anonymization Scan Phi Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/anonymization/redact-pdf:
    post:
      tags:
      - Anonymization
      summary: Redact Pdf Endpoint
      description: 'Apply non-destructive PHI redaction overlays to PDF document.


        Requirements: PRD-SYS-001'
      operationId: redact_pdf_endpoint_api_v1_execution_anonymization_redact_pdf_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/RedactPDFRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties: true
                type: object
                title: Response Redact Pdf Endpoint Api V1 Execution Anonymization Redact Pdf Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/doa/assignment:
    post:
      tags:
      - DOA
      summary: Add Doa Assignment Endpoint
      description: 'Add site personnel task delegation entry to Delegation of Authority log.


        Requirements: PRD-SYS-001'
      operationId: add_doa_assignment_endpoint_api_v1_execution_doa_assignment_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/AddDOAAssignmentRequest'
        required: true
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/DOAAssignmentRecord'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/doa/sign-off:
    post:
      tags:
      - DOA
      summary: Sign Off Doa Assignment Endpoint
      description: 'Endorse Delegation of Authority task assignment with Principal Investigator eSignature.


        Requirements: PRD-SYS-001'
      operationId: sign_off_doa_assignment_endpoint_api_v1_execution_doa_sign_off_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/DOASignOffRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/DOAAssignmentRecord'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/doa/log/{study_id}/{site_id}:
    get:
      tags:
      - DOA
      summary: Get Site Doa Log Endpoint
      description: 'Retrieve site-isolated Delegation of Authority log entries.


        Requirements: PRD-SYS-001'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/DOAAssignmentRecord'
                title: Response Get Site Doa Log Endpoint Api V1 Execution Doa Log  Study Id   Site Id  Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/DelegateTaskRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/DOADelegationRecordResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/ApproveDelegationRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/DOADelegationRecordResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/ApproveTaskDelegationRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/DOADelegationRecordResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/RevokeDelegationRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/DOADelegationRecordResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/SiteStaffMemberRequest'
        required: true
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SiteStaffMemberResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/doa/audit-logs:
    get:
      tags:
      - DOA
      summary: Get Audit Logs Endpoint
      operationId: get_audit_logs_endpoint_api_v1_execution_doa_audit_logs_get
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                items:
                  $ref: '#/components/schemas/DOAAuditLogResponse'
                type: array
                title: Response Get Audit Logs Endpoint Api V1 Execution Doa Audit Logs Get
  /api/v1/execution/doa/delegations:
    get:
      tags:
      - DOA
      summary: Get Delegations Endpoint
      operationId: get_delegations_endpoint_api_v1_execution_doa_delegations_get
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                items:
                  $ref: '#/components/schemas/DOADelegationRecordResponse'
                type: array
                title: Response Get Delegations Endpoint Api V1 Execution Doa Delegations Get
  /api/v1/offline/sync-batch:
    post:
      tags:
      - Offline Sync
      summary: Sync Offline Batch
      description: 'Ingest batch of queued offline eCRF/ePRO deltas idempotently.


        Requirements: PRD-SYS-001'
      operationId: sync_offline_batch_api_v1_offline_sync_batch_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/OfflineBatchSyncRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/OfflineBatchSyncResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/offline/sync:
    post:
      tags:
      - Offline Sync
      summary: Offline Sync Endpoint
      description: 'Synchronize queued offline delta transactions.


        Requirements: PRD-SYS-001'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties: true
                type: object
                title: Response Offline Sync Endpoint Api V1 Execution Offline Sync Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/documents/upload:
    post:
      tags:
      - Documents
      summary: Upload Document
      description: 'Upload regulated document, compute SHA-256 hash, and record GxP audit trail.


        Requirements: PRD-SYS-001'
      operationId: upload_document_api_v1_documents_upload_post
      requestBody:
        content:
          multipart/form-data:
            schema:
              $ref: '#/components/schemas/Body_upload_document_api_v1_documents_upload_post'
        required: true
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/DocumentUploadResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/documents/{doc_id}:
    get:
      tags:
      - Documents
      summary: Download Document
      description: 'Stream file content with dynamic watermarking.


        Requirements: PRD-SYS-001'
      operationId: download_document_api_v1_documents__doc_id__get
      parameters:
      - name: doc_id
        in: path
        required: true
        schema:
          type: string
          title: Doc Id
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema: {}
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/documents/{doc_id}/versions:
    get:
      tags:
      - Documents
      summary: List Document Versions
      description: 'RETURN complete version history list.


        Requirements: PRD-SYS-001'
      operationId: list_document_versions_api_v1_documents__doc_id__versions_get
      parameters:
      - name: doc_id
        in: path
        required: true
        schema:
          type: string
          title: Doc Id
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/DocumentMetadataResponse'
                title: Response List Document Versions Api V1 Documents  Doc Id  Versions Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/tsdv/config:
    post:
      tags:
      - SDV/TSDV
      summary: Create Or Update Tsdv Config
      description: 'CREATE or UPDATE Targeted SDV (TSDV) configuration for a study.


        Restricts config writes to CRA/Data Manager roles WITH GxP change justifications.'
      operationId: create_or_update_tsdv_config_api_v1_execution_tsdv_config_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/TSDVConfigCreate'
        required: true
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/TSDVConfigResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/TSDVConfigResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/tsdv/required:
    get:
      tags:
      - SDV/TSDV
      summary: Evaluate Tsdv Rule
      description: 'Evaluate Targeted SDV (TSDV) requirement for a given context.


        Calculates deterministic sampling decisions and returns component results with an audit explanation.'
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
          - type: 'null'
          title: Domain
      - name: enrollment_index
        in: query
        required: false
        schema:
          anyOf:
          - type: integer
          - type: 'null'
          title: Enrollment Index
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/TSDVEvaluationResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/SDVSignOffRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SDVSignOffResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/BulkSdvSignOffRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/BulkSdvSignOffResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/BulkQueryGenerationRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/BulkQueryGenerationResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SubjectRandomizationResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/hierarchy:
    get:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Get Supply Hierarchy Endpoint
      description: Returns the complete multi-echelon supply hierarchy tree and stock rollup for a study.
      operationId: get_supply_hierarchy_endpoint_api_v1_execution_rtsm_hierarchy_get
      parameters:
      - name: study_id
        in: query
        required: true
        schema:
          type: string
          description: Clinical study identifier
          title: Study Id
        description: Clinical study identifier
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/MultiEchelonRollup'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/nodes:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Create Supply Node Endpoint
      description: Registers or updates a supply node in the clinical supply hierarchy.
      operationId: create_supply_node_endpoint_api_v1_execution_rtsm_nodes_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/SupplyNodeCreateRequest'
        required: true
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SupplyNodeResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/nodes/{node_id}/inventory:
    get:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Get Node Inventory Endpoint
      description: Lists inventory records and stock levels for a specific supply node.
      operationId: get_node_inventory_endpoint_api_v1_execution_rtsm_nodes__node_id__inventory_get
      parameters:
      - name: node_id
        in: path
        required: true
        schema:
          type: string
          title: Node Id
      - name: study_id
        in: query
        required: true
        schema:
          type: string
          description: Clinical study identifier
          title: Study Id
        description: Clinical study identifier
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/SupplyNodeInventoryResponse'
                title: Response Get Node Inventory Endpoint Api V1 Execution Rtsm Nodes  Node Id  Inventory Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/nodes/{node_id}/inventory/adjust:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Adjust Node Inventory Endpoint
      description: Adjusts or initializes stock levels for a supply node.
      operationId: adjust_node_inventory_endpoint_api_v1_execution_rtsm_nodes__node_id__inventory_adjust_post
      parameters:
      - name: node_id
        in: path
        required: true
        schema:
          type: string
          title: Node Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/SupplyNodeInventoryAdjustRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SupplyNodeInventoryResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/forecast/site:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Forecast Site Resupply Endpoint
      description: Executes a predictive demand and resupply forecast for a clinical site.
      operationId: forecast_site_resupply_endpoint_api_v1_execution_rtsm_forecast_site_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/PredictiveForecastRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PredictiveForecastResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/forecast/study:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Forecast Study Resupply Endpoint
      description: Generates concurrent predictive forecasts across all active clinical sites in a study.
      operationId: forecast_study_resupply_endpoint_api_v1_execution_rtsm_forecast_study_post
      parameters:
      - name: study_id
        in: query
        required: true
        schema:
          type: string
          description: Clinical study identifier
          title: Study Id
        description: Clinical study identifier
      - name: horizon_days
        in: query
        required: false
        schema:
          type: integer
          maximum: 365
          minimum: 7
          default: 60
          title: Horizon Days
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/PredictiveForecastResponse'
                title: Response Forecast Study Resupply Endpoint Api V1 Execution Rtsm Forecast Study Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/transfer-orders:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Create Transfer Order Endpoint
      description: Creates a stock transfer order between supply nodes.
      operationId: create_transfer_order_endpoint_api_v1_execution_rtsm_transfer_orders_post
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/TransferOrderCreateRequest'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/TransferOrderResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
    get:
      tags:
      - RTSM Multi-Echelon Supply
      summary: List Transfer Orders Endpoint
      description: Lists active transfer orders with optional filtering.
      operationId: list_transfer_orders_endpoint_api_v1_execution_rtsm_transfer_orders_get
      parameters:
      - name: study_id
        in: query
        required: true
        schema:
          type: string
          description: Clinical study identifier
          title: Study Id
        description: Clinical study identifier
      - name: source_node_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Source Node Id
      - name: destination_node_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Destination Node Id
      - name: status
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Status
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/TransferOrderResponse'
                title: Response List Transfer Orders Endpoint Api V1 Execution Rtsm Transfer Orders Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/transfer-orders/{order_id}/approve:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Approve Transfer Order Endpoint
      description: Approves a transfer order for warehouse packing.
      operationId: approve_transfer_order_endpoint_api_v1_execution_rtsm_transfer_orders__order_id__approve_post
      parameters:
      - name: order_id
        in: path
        required: true
        schema:
          type: string
          title: Order Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/TransferOrderApprovalRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/TransferOrderResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/transfer-orders/{order_id}/manifest:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Generate Packing Manifest Endpoint
      description: Generates a formal GxP packing manifest binding serialized kits to an approved transfer order.
      operationId: generate_packing_manifest_endpoint_api_v1_execution_rtsm_transfer_orders__order_id__manifest_post
      parameters:
      - name: order_id
        in: path
        required: true
        schema:
          type: string
          title: Order Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/PackingManifestCreateRequest'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PackingManifestResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/transfer-orders/{order_id}/ship:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Ship Transfer Order Endpoint
      description: Marks a transfer order shipped, moves inventory to in-transit, and dispatches depot notification.
      operationId: ship_transfer_order_endpoint_api_v1_execution_rtsm_transfer_orders__order_id__ship_post
      parameters:
      - name: order_id
        in: path
        required: true
        schema:
          type: string
          title: Order Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/TransferOrderShipRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/TransferOrderResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/transfer-orders/{order_id}/receive:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Receive Transfer Order Endpoint
      description: Marks a transfer order delivered/received at destination node, transferring in-transit stock to on-hand.
      operationId: receive_transfer_order_endpoint_api_v1_execution_rtsm_transfer_orders__order_id__receive_post
      parameters:
      - name: order_id
        in: path
        required: true
        schema:
          type: string
          title: Order Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/TransferOrderReceiveRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/TransferOrderResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/stability-budgets:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Create Stability Budget Endpoint
      description: Configures allowable thermal excursion limits and Arrhenius activation parameters for a kit type or lot.
      operationId: create_stability_budget_endpoint_api_v1_execution_rtsm_stability_budgets_post
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/StabilityBudgetCreateRequest'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/StabilityBudgetResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
    get:
      tags:
      - RTSM Multi-Echelon Supply
      summary: List Stability Budgets Endpoint
      description: Lists defined drug stability budgets for a clinical study.
      operationId: list_stability_budgets_endpoint_api_v1_execution_rtsm_stability_budgets_get
      parameters:
      - name: study_id
        in: query
        required: true
        schema:
          type: string
          description: Clinical study identifier
          title: Study Id
        description: Clinical study identifier
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/StabilityBudgetResponse'
                title: Response List Stability Budgets Endpoint Api V1 Execution Rtsm Stability Budgets Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/cold-chain/loggers:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Register Temperature Logger Endpoint
      description: Registers a temperature data logger device bound to a shipment or storage facility.
      operationId: register_temperature_logger_endpoint_api_v1_execution_rtsm_cold_chain_loggers_post
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/TemperatureLoggerRegisterRequest'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/TemperatureLoggerResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
    get:
      tags:
      - RTSM Multi-Echelon Supply
      summary: List Temperature Loggers Endpoint
      description: Lists registered temperature loggers for a study.
      operationId: list_temperature_loggers_endpoint_api_v1_execution_rtsm_cold_chain_loggers_get
      parameters:
      - name: study_id
        in: query
        required: true
        schema:
          type: string
          description: Clinical study identifier
          title: Study Id
        description: Clinical study identifier
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/TemperatureLoggerResponse'
                title: Response List Temperature Loggers Endpoint Api V1 Execution Rtsm Cold Chain Loggers Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/cold-chain/ingest:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Ingest Temperature Logger File Endpoint
      description: Ingests a digital logger file (CSV, XML, JSON), calculates Arrhenius MKT, and enforces automated kit quarantine.
      operationId: ingest_temperature_logger_file_endpoint_api_v1_execution_rtsm_cold_chain_ingest_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/LoggerIngestionRequest'
        required: true
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ExcursionEvaluationResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/cold-chain/evaluate-mkt:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Evaluate Mkt Direct Endpoint
      description: Direct Arrhenius Mean Kinetic Temperature (MKT) calculation per USP <1079> / PDA TR 39.
      operationId: evaluate_mkt_direct_endpoint_api_v1_execution_rtsm_cold_chain_evaluate_mkt_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/MktCalculationDirectRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/MktCalculationDirectResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/cold-chain/evaluations:
    get:
      tags:
      - RTSM Multi-Echelon Supply
      summary: List Excursion Evaluations Endpoint
      description: Lists cold chain excursion evaluation audit records.
      operationId: list_excursion_evaluations_endpoint_api_v1_execution_rtsm_cold_chain_evaluations_get
      parameters:
      - name: study_id
        in: query
        required: true
        schema:
          type: string
          description: Clinical study identifier
          title: Study Id
        description: Clinical study identifier
      - name: entity_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Optional filter by transfer order or node ID
          title: Entity Id
        description: Optional filter by transfer order or node ID
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/ExcursionEvaluationResponse'
                title: Response List Excursion Evaluations Endpoint Api V1 Execution Rtsm Cold Chain Evaluations Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/kits:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Create Kit Endpoint
      description: Registers an investigational product kit into the blinded RTSM inventory catalog.
      operationId: create_kit_endpoint_api_v1_execution_rtsm_kits_post
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/IPKitCreateRequest'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/IPKitResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
    get:
      tags:
      - RTSM Multi-Echelon Supply
      summary: List Kits Endpoint
      description: Lists investigational product kits.
      operationId: list_kits_endpoint_api_v1_execution_rtsm_kits_get
      parameters:
      - name: study_id
        in: query
        required: true
        schema:
          type: string
          description: Clinical study identifier
          title: Study Id
        description: Clinical study identifier
      - name: node_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Optional node/site ID filter
          title: Node Id
        description: Optional node/site ID filter
      - name: lot_number
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Optional lot number filter
          title: Lot Number
        description: Optional lot number filter
      - name: status
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Optional status filter (e.g. AVAILABLE, QUARANTINED)
          title: Status
        description: Optional status filter (e.g. AVAILABLE, QUARANTINED)
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/IPKitResponse'
                title: Response List Kits Endpoint Api V1 Execution Rtsm Kits Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/kits/quarantine:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Quarantine Kits Endpoint
      description: Manually places investigational product kits in QUARANTINED status with Part 11 electronic audit trail.
      operationId: quarantine_kits_endpoint_api_v1_execution_rtsm_kits_quarantine_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/KitQuarantineRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties:
                  anyOf:
                  - type: string
                  - type: integer
                type: object
                title: Response Quarantine Kits Endpoint Api V1 Execution Rtsm Kits Quarantine Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/kits/release-quarantine:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Release Quarantine Kits Endpoint
      description: Authorizes QA disposition release of quarantined kits back into available inventory.
      operationId: release_quarantine_kits_endpoint_api_v1_execution_rtsm_kits_release_quarantine_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/KitReleaseQuarantineRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties:
                  anyOf:
                  - type: string
                  - type: integer
                type: object
                title: Response Release Quarantine Kits Endpoint Api V1 Execution Rtsm Kits Release Quarantine Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/lots/extend-expiry:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Extend Lot Expiry Endpoint
      description: Executes automated lot expiration extension workflow, updating kits across all sites and depots.
      operationId: extend_lot_expiry_endpoint_api_v1_execution_rtsm_lots_extend_expiry_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/LotExpiryExtensionRequest'
        required: true
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/LotExpiryExtensionResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/lots/extensions:
    get:
      tags:
      - RTSM Multi-Echelon Supply
      summary: List Lot Expiry Extensions Endpoint
      description: Lists lot expiration extension audit records.
      operationId: list_lot_expiry_extensions_endpoint_api_v1_execution_rtsm_lots_extensions_get
      parameters:
      - name: study_id
        in: query
        required: true
        schema:
          type: string
          description: Clinical study identifier
          title: Study Id
        description: Clinical study identifier
      - name: lot_number
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Optional lot number filter
          title: Lot Number
        description: Optional lot number filter
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/LotExpiryExtensionResponse'
                title: Response List Lot Expiry Extensions Endpoint Api V1 Execution Rtsm Lots Extensions Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/relabeling-tasks:
    get:
      tags:
      - RTSM Multi-Echelon Supply
      summary: List Relabeling Tasks Endpoint
      description: Lists physical re-labeling action tasks across facilities.
      operationId: list_relabeling_tasks_endpoint_api_v1_execution_rtsm_relabeling_tasks_get
      parameters:
      - name: study_id
        in: query
        required: true
        schema:
          type: string
          description: Clinical study identifier
          title: Study Id
        description: Clinical study identifier
      - name: node_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Optional node/site ID filter
          title: Node Id
        description: Optional node/site ID filter
      - name: lot_number
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Optional lot number filter
          title: Lot Number
        description: Optional lot number filter
      - name: status
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Optional status filter
          title: Status
        description: Optional status filter
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/ReLabelingTaskResponse'
                title: Response List Relabeling Tasks Endpoint Api V1 Execution Rtsm Relabeling Tasks Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/relabeling-tasks/{task_id}/complete:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Complete Relabeling Task Endpoint
      description: Records completion of a physical kit re-labeling task with electronic signature justification.
      operationId: complete_relabeling_task_endpoint_api_v1_execution_rtsm_relabeling_tasks__task_id__complete_post
      parameters:
      - name: task_id
        in: path
        required: true
        schema:
          type: string
          title: Task Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ReLabelingTaskCompleteRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ReLabelingTaskResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/relabeling-tasks/{task_id}/verify:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Verify Relabeling Task Endpoint
      description: Dual QA verification of completed re-labeling task with barcode verification hash.
      operationId: verify_relabeling_task_endpoint_api_v1_execution_rtsm_relabeling_tasks__task_id__verify_post
      parameters:
      - name: task_id
        in: path
        required: true
        schema:
          type: string
          title: Task Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ReLabelingTaskVerifyRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ReLabelingTaskResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/cgt/apheresis:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Initialize Apheresis Endpoint
      description: Initializes Vein-to-Vein tracking upon initial apheresis cellular harvest.
      operationId: initialize_apheresis_endpoint_api_v1_execution_rtsm_cgt_apheresis_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CGTApheresisCreateRequest'
        required: true
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CGTChainOfCustodyResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/cgt/milestone:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Record Cgt Milestone Endpoint
      description: Records a cryogenic transit milestone with GPS coordinates and temperature logging.
      operationId: record_cgt_milestone_endpoint_api_v1_execution_rtsm_cgt_milestone_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CGTMilestoneCreateRequest'
        required: true
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CGTMilestoneResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/cgt/manufacturing-bind:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Bind Manufacturing Batch Endpoint
      description: Binds manufacturing batch record to original subject identity and regenerates barcode.
      operationId: bind_manufacturing_batch_endpoint_api_v1_execution_rtsm_cgt_manufacturing_bind_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CGTManufacturingBindRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CGTChainOfCustodyResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/cgt/bedside-verify:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Bedside Dual Verification Endpoint
      description: Executes mandatory two-person electronic bedside verification, unlocking the drug infusion eCRF.
      operationId: bedside_dual_verification_endpoint_api_v1_execution_rtsm_cgt_bedside_verify_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CGTBedsideVerificationRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CGTBedsideVerificationResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/cgt/infuse:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Execute Infusion Endpoint
      description: Records cellular drug infusion administration, gated by the bedside verification unlock token.
      operationId: execute_infusion_endpoint_api_v1_execution_rtsm_cgt_infuse_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CGTInfusionExecuteRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CGTChainOfCustodyResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/cgt/returns:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Log Vial Return Endpoint
      description: Logs returned kit/vial accountability and calculates patient adherence compliance.
      operationId: log_vial_return_endpoint_api_v1_execution_rtsm_cgt_returns_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CGTVialReturnReconciliationRequest'
        required: true
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CGTVialReturnReconciliationResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/cgt/destruction-certificate:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Generate Destruction Certificate Endpoint
      description: Generates a formal Certificate of Destruction with dual witness electronic signatures.
      operationId: generate_destruction_certificate_endpoint_api_v1_execution_rtsm_cgt_destruction_certificate_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CGTDestructionCertificateCreateRequest'
        required: true
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CGTDestructionCertificateResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/cgt/verify-barcode:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Verify Cgt Barcode Endpoint
      description: Verifies cryptographic authenticity and entity bindings for a scanned barcode.
      operationId: verify_cgt_barcode_endpoint_api_v1_execution_rtsm_cgt_verify_barcode_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CGTBarcodeVerifyRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CGTBarcodeVerifyResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/cgt/chain-of-custody/{study_id}/{subject_id}:
    get:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Get Subject Cgt Traceability Endpoint
      description: Retrieves full Vein-to-Vein traceability ledger for a subject.
      operationId: get_subject_cgt_traceability_endpoint_api_v1_execution_rtsm_cgt_chain_of_custody__study_id___subject_id__get
      parameters:
      - name: study_id
        in: path
        required: true
        schema:
          type: string
          title: Study Id
      - name: subject_id
        in: path
        required: true
        schema:
          type: string
          title: Subject Id
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CGTFullTraceabilityResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/cgt/certificates/{study_id}:
    get:
      tags:
      - RTSM Multi-Echelon Supply
      summary: List Destruction Certificates Endpoint
      description: Lists all Certificates of Destruction for a study.
      operationId: list_destruction_certificates_endpoint_api_v1_execution_rtsm_cgt_certificates__study_id__get
      parameters:
      - name: study_id
        in: path
        required: true
        schema:
          type: string
          title: Study Id
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/CGTDestructionCertificateResponse'
                title: Response List Destruction Certificates Endpoint Api V1 Execution Rtsm Cgt Certificates  Study Id  Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/cgt/reconciliations/{study_id}:
    get:
      tags:
      - RTSM Multi-Echelon Supply
      summary: List Vial Reconciliations Endpoint
      description: Lists all product return and accountability reconciliation records for a study.
      operationId: list_vial_reconciliations_endpoint_api_v1_execution_rtsm_cgt_reconciliations__study_id__get
      parameters:
      - name: study_id
        in: path
        required: true
        schema:
          type: string
          title: Study Id
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/CGTVialReturnReconciliationResponse'
                title: Response List Vial Reconciliations Endpoint Api V1 Execution Rtsm Cgt Reconciliations  Study Id  Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/adaptive/config:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Create Or Update Adaptive Config Endpoint
      description: Registers or updates adaptive oncology dose-escalation or RAR trial design parameters.
      operationId: create_or_update_adaptive_config_endpoint_api_v1_execution_rtsm_adaptive_config_post
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/AdaptiveTrialConfigDTO'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Create Or Update Adaptive Config Endpoint Api V1 Execution Rtsm Adaptive Config Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
    get:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Get Adaptive Config Endpoint
      description: Retrieves current adaptive dose-escalation trial configuration, active cohort state, and history.
      operationId: get_adaptive_config_endpoint_api_v1_execution_rtsm_adaptive_config_get
      parameters:
      - name: study_id
        in: query
        required: true
        schema:
          type: string
          description: Clinical study identifier
          title: Study Id
        description: Clinical study identifier
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Get Adaptive Config Endpoint Api V1 Execution Rtsm Adaptive Config Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/adaptive/cohorts/evaluate:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Evaluate Adaptive Cohort Endpoint
      description: Evaluates safety cohort DLTs and computes real-time recommendation flags (BOIN, CRM, 3+3).
      operationId: evaluate_adaptive_cohort_endpoint_api_v1_execution_rtsm_adaptive_cohorts_evaluate_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CohortEvaluationInput'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CohortEvaluationResult'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/adaptive/cohorts/transition:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Transition Adaptive Cohort Endpoint
      description: Advances or overrides a dose escalation cohort with 21 CFR Part 11 audit logging.
      operationId: transition_adaptive_cohort_endpoint_api_v1_execution_rtsm_adaptive_cohorts_transition_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CohortTransitionRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CohortTransitionLogResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/adaptive/cohorts/history:
    get:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Get Adaptive Cohorts History Endpoint
      description: Returns the immutable 21 CFR Part 11 audited transition log history for a study.
      operationId: get_adaptive_cohorts_history_endpoint_api_v1_execution_rtsm_adaptive_cohorts_history_get
      parameters:
      - name: study_id
        in: query
        required: true
        schema:
          type: string
          description: Clinical study identifier
          title: Study Id
        description: Clinical study identifier
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/CohortTransitionLogResponse'
                title: Response Get Adaptive Cohorts History Endpoint Api V1 Execution Rtsm Adaptive Cohorts History Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/adaptive/rar/evaluate:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Evaluate Rar Endpoint
      description: Calculates dynamic Response-Adaptive Randomization allocation weights across active arms.
      operationId: evaluate_rar_endpoint_api_v1_execution_rtsm_adaptive_rar_evaluate_post
      parameters:
      - name: study_id
        in: query
        required: true
        schema:
          type: string
          description: Clinical study identifier
          title: Study Id
        description: Clinical study identifier
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/RAREvaluationResult'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/adaptive/rar/arms:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Expand Or Terminate Rar Arm Endpoint
      description: Seamlessly adds or terminates a treatment arm in RAR with zero study downtime.
      operationId: expand_or_terminate_rar_arm_endpoint_api_v1_execution_rtsm_adaptive_rar_arms_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/RARExpandOrTerminateArmRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/RAREvaluationResult'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/adaptive/rar/state:
    get:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Get Rar State Endpoint
      description: Retrieves current Response-Adaptive Randomization arm allocation weights and posterior states.
      operationId: get_rar_state_endpoint_api_v1_execution_rtsm_adaptive_rar_state_get
      parameters:
      - name: study_id
        in: query
        required: true
        schema:
          type: string
          description: Clinical study identifier
          title: Study Id
        description: Clinical study identifier
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Get Rar State Endpoint Api V1 Execution Rtsm Adaptive Rar State Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/adaptive/boin/calculate-boundaries:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Calculate Boin Boundaries Endpoint
      description: Calculates theoretical BOIN escalation and de-escalation toxicity boundaries (lambda_e, lambda_d).
      operationId: calculate_boin_boundaries_endpoint_api_v1_execution_rtsm_adaptive_boin_calculate_boundaries_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/BoinBoundariesCalculationRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/BoinBoundariesCalculationResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/adaptive/crm/posterior:
    post:
      tags:
      - RTSM Multi-Echelon Supply
      summary: Calculate Crm Posterior Endpoint
      description: Calculates Bayesian posterior probabilities and recommended next dose under Continual Reassessment Method.
      operationId: calculate_crm_posterior_endpoint_api_v1_execution_rtsm_adaptive_crm_posterior_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CrmPosteriorCalculationRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CrmPosteriorCalculationResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/UnblindRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SubjectUnblindResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
          - type: 'null'
          title: Study Id
      - name: subject_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Subject Id
      - name: visit_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Visit Id
      - name: status
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Status
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/ClinicalQueryResponse'
                title: Response List Queries Api V1 Execution Queries Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/QueryCreate'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ClinicalQueryResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ClinicalQueryResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/QueryUpdate'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ClinicalQueryResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/QueryRespond'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ClinicalQueryResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ClinicalQueryResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              - $ref: '#/components/schemas/QueryReopen'
              - type: 'null'
              title: Payload
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ClinicalQueryResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/QueryCancel'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ClinicalQueryResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/SyncRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties: true
                type: object
                title: Response Sync Queries Api V1 Execution Queries Sync Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/Body_import_dictionary_api_v1_dictionaries_import_post'
        required: true
      responses:
        '202':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/JobStatusResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/JobStatusResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
          - type: 'null'
          default: '26.0'
          title: Version
      - name: target_level
        in: query
        required: false
        schema:
          anyOf:
          - $ref: '#/components/schemas/MedDRATargetLevelEnum'
          - type: 'null'
          default: LLT
          title: Target Level
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/MedDRACodeLookupResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/WHODrugCodeLookupResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/UCUMConvertRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/UCUMConvertResponse'
        '400':
          description: Bad Request
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ProblemDetails'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/ImpactAnalysisRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ImpactAnalysisResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
          - type: 'null'
          title: Observation Id
      - name: status
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Status
      - name: verbatim_text
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Verbatim Text
      - name: dictionary_type
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Dictionary Type
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/CodingAssignmentResponse'
                title: Response List Coding Assignments Api V1 Execution Coding Assignments Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CodingAssignmentResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/coding/assignments/{assignment_id}/suggest:
    post:
      tags:
      - Dictionaries
      summary: Post Suggest Coding
      description: Generates AI Tier 1 semantic coding suggestions for an assignment.
      operationId: post_suggest_coding_api_v1_execution_coding_assignments__assignment_id__suggest_post
      parameters:
      - name: assignment_id
        in: path
        required: true
        schema:
          type: string
          title: Assignment Id
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CodingAssignmentResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/CoderActionRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CodingAssignmentResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/coding/assignments/batch-assign:
    post:
      tags:
      - Dictionaries
      summary: Post Batch Assign
      description: Performs batch medical coding assignment across multiple assignments with GxP audit logging.
      operationId: post_batch_assign_api_v1_execution_coding_assignments_batch_assign_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/BatchAssignRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/BatchAssignResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/coding/assignments/{assignment_id}/raise-query:
    post:
      tags:
      - Dictionaries
      summary: Post Raise Coding Query
      description: Escalates a coding discrepancy into a ClinicalQuery on the associated observation/eCRF record.
      operationId: post_raise_coding_query_api_v1_execution_coding_assignments__assignment_id__raise_query_post
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
              $ref: '#/components/schemas/RaiseQueryRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/RaiseQueryResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/coding/queue:
    get:
      tags:
      - Dictionaries
      summary: Get Coding Queue
      description: Retrieves the active coding queue.
      operationId: get_coding_queue_api_v1_execution_coding_queue_get
      parameters:
      - name: observation_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Observation Id
      - name: status
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Status
      - name: verbatim_text
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Verbatim Text
      - name: dictionary_type
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Dictionary Type
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/CodingAssignmentResponse'
                title: Response Get Coding Queue Api V1 Execution Coding Queue Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/biostat/sdtm/{domain}:
    get:
      tags:
      - CDISC Exports
      summary: Export Sdtm Domain
      description: Exports SDTM domain data (DM, AE, VS, LB, MH, CM) in Dataset-JSON, SAS XPT, ODM-XML, or CSV format.
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
      - name: format
        in: query
        required: false
        schema:
          type: string
          description: 'Target export format: json, xpt, odm, csv'
          default: json
          title: Format
        description: 'Target export format: json, xpt, odm, csv'
      - name: version
        in: query
        required: false
        schema:
          type: string
          description: SAS XPT version if format is xpt (v5 or v8)
          default: v5
          title: Version
        description: SAS XPT version if format is xpt (v5 or v8)
      - name: privacy_profile
        in: query
        required: false
        schema:
          type: string
          description: 'Privacy policy: SAFE_HARBOR, LIMITED_DATA_SET, GDPR_PSEUDONYMIZED, UNRESTRICTED'
          default: SAFE_HARBOR
          title: Privacy Profile
        description: 'Privacy policy: SAFE_HARBOR, LIMITED_DATA_SET, GDPR_PSEUDONYMIZED, UNRESTRICTED'
      - name: salt
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: HMAC pseudonymization salt
          title: Salt
        description: HMAC pseudonymization salt
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                title: Response Export Sdtm Domain Api V1 Execution Biostat Sdtm  Domain  Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/biostat/adam/{dataset}:
    get:
      tags:
      - CDISC Exports
      summary: Export Adam Dataset
      description: Exports ADaM dataset data (ADSL, ADAE, ADVS) in Dataset-JSON, SAS XPT, ODM-XML, or CSV format.
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
      - name: format
        in: query
        required: false
        schema:
          type: string
          description: 'Target export format: json, xpt, odm, csv'
          default: json
          title: Format
        description: 'Target export format: json, xpt, odm, csv'
      - name: version
        in: query
        required: false
        schema:
          type: string
          description: SAS XPT version if format is xpt (v5 or v8)
          default: v5
          title: Version
        description: SAS XPT version if format is xpt (v5 or v8)
      - name: privacy_profile
        in: query
        required: false
        schema:
          type: string
          description: 'Privacy policy: SAFE_HARBOR, LIMITED_DATA_SET, GDPR_PSEUDONYMIZED, UNRESTRICTED'
          default: SAFE_HARBOR
          title: Privacy Profile
        description: 'Privacy policy: SAFE_HARBOR, LIMITED_DATA_SET, GDPR_PSEUDONYMIZED, UNRESTRICTED'
      - name: salt
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: HMAC pseudonymization salt
          title: Salt
        description: HMAC pseudonymization salt
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                title: Response Export Adam Dataset Api V1 Execution Biostat Adam  Dataset  Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/biostat/bundle:
    get:
      tags:
      - CDISC Exports
      summary: Export Biostat Bundle
      description: Exports all SDTM domains and ADaM datasets bundled in Dataset-JSON, CSV ZIP, or ODM-XML.
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
      - name: format
        in: query
        required: false
        schema:
          type: string
          description: 'Target export format: json, zip, csv_zip, odm'
          default: json
          title: Format
        description: 'Target export format: json, zip, csv_zip, odm'
      - name: privacy_profile
        in: query
        required: false
        schema:
          type: string
          description: 'Privacy policy: SAFE_HARBOR, LIMITED_DATA_SET, GDPR_PSEUDONYMIZED, UNRESTRICTED'
          default: SAFE_HARBOR
          title: Privacy Profile
        description: 'Privacy policy: SAFE_HARBOR, LIMITED_DATA_SET, GDPR_PSEUDONYMIZED, UNRESTRICTED'
      - name: salt
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: HMAC pseudonymization salt
          title: Salt
        description: HMAC pseudonymization salt
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                title: Response Export Biostat Bundle Api V1 Execution Biostat Bundle Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/exports/wizard:
    post:
      tags:
      - CDISC Exports
      summary: Execute Export Wizard
      description: Executes a parameterized export job from the Regulatory Export Wizard.
      operationId: execute_export_wizard_api_v1_execution_exports_wizard_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ExportBundleRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                title: Response Execute Export Wizard Api V1 Execution Exports Wizard Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/labs/ingest:
    post:
      tags:
      - Laboratory Governance & Ingestion
      summary: Ingest Lab Batch Endpoint
      description: Ingest central or local laboratory batch data (CSV, HL7 v2.x, FHIR JSON, CDISC LAB, or Local Manual).
      operationId: ingest_lab_batch_endpoint_api_v1_execution_labs_ingest_post
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/LabBatchIngestResult'
  /api/v1/execution/labs/batch-status:
    get:
      tags:
      - Laboratory Governance & Ingestion
      summary: Get Batch Status Endpoint
      description: Retrieve status and metrics for previous lab batch ingestion jobs.
      operationId: get_batch_status_endpoint_api_v1_execution_labs_batch_status_get
      parameters:
      - name: batch_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Optional batch ID to query
          title: Batch Id
        description: Optional batch ID to query
      - name: study_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Optional study ID filter
          title: Study Id
        description: Optional study ID filter
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                anyOf:
                - type: array
                  items:
                    $ref: '#/components/schemas/LabBatchIngestResult'
                - $ref: '#/components/schemas/LabBatchIngestResult'
                title: Response Get Batch Status Endpoint Api V1 Execution Labs Batch Status Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/labs/batch-status/{batch_id}:
    get:
      tags:
      - Laboratory Governance & Ingestion
      summary: Get Batch Status By Id Endpoint
      description: Query execution status and statistics of a specific laboratory batch by ID.
      operationId: get_batch_status_by_id_endpoint_api_v1_execution_labs_batch_status__batch_id__get
      parameters:
      - name: batch_id
        in: path
        required: true
        schema:
          type: string
          title: Batch Id
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/LabBatchIngestResult'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/labs/certifications:
    post:
      tags:
      - Laboratory Governance & Ingestion
      summary: Create Local Lab Certification
      description: Register a local laboratory certification and accreditation.
      operationId: create_local_lab_certification_api_v1_execution_labs_certifications_post
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/LocalLabCertificationCreate'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/LocalLabCertificationResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
    get:
      tags:
      - Laboratory Governance & Ingestion
      summary: List Local Lab Certifications
      description: List local laboratory certifications with optional filters.
      operationId: list_local_lab_certifications_api_v1_execution_labs_certifications_get
      parameters:
      - name: study_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Filter by study ID
          title: Study Id
        description: Filter by study ID
      - name: site_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Filter by site ID
          title: Site Id
        description: Filter by site ID
      - name: status
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Filter by certification status
          title: Status
        description: Filter by certification status
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/LocalLabCertificationResponse'
                title: Response List Local Lab Certifications Api V1 Execution Labs Certifications Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/labs/certifications/{cert_id}:
    put:
      tags:
      - Laboratory Governance & Ingestion
      summary: Update Local Lab Certification
      description: Update an existing local laboratory certification.
      operationId: update_local_lab_certification_api_v1_execution_labs_certifications__cert_id__put
      parameters:
      - name: cert_id
        in: path
        required: true
        schema:
          type: string
          title: Cert Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/LocalLabCertificationUpdate'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/LocalLabCertificationResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/labs/certifications/verify:
    post:
      tags:
      - Laboratory Governance & Ingestion
      summary: Verify Local Lab Certification Endpoint
      description: Verify if a site has active, unexpired local lab certification for an observation date.
      operationId: verify_local_lab_certification_endpoint_api_v1_execution_labs_certifications_verify_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/LocalLabCertificationVerifyRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/LocalLabCertificationVerifyResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/labs/toxicity/review-queue:
    get:
      tags:
      - Laboratory Governance & Ingestion
      summary: List Staged Ae Review Queue
      description: Retrieve laboratory Grade 3/4 toxicity triggers staged for Investigator review.
      operationId: list_staged_ae_review_queue_api_v1_execution_labs_toxicity_review_queue_get
      parameters:
      - name: study_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Filter by study ID
          title: Study Id
        description: Filter by study ID
      - name: site_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: Filter by site ID
          title: Site Id
        description: Filter by site ID
      - name: review_status
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          description: 'Filter by status (default: STAGED_FOR_INVESTIGATOR_REVIEW, ALL for any)'
          default: STAGED_FOR_INVESTIGATOR_REVIEW
          title: Review Status
        description: 'Filter by status (default: STAGED_FOR_INVESTIGATOR_REVIEW, ALL for any)'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/StagedAdverseEventReviewResponse'
                title: Response List Staged Ae Review Queue Api V1 Execution Labs Toxicity Review Queue Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/labs/toxicity/review/{review_id}/action:
    post:
      tags:
      - Laboratory Governance & Ingestion
      summary: Action Staged Ae Review
      description: Perform Investigator workflow action on a staged Grade 3/4 AE (Confirm, Dismiss, Escalate).
      operationId: action_staged_ae_review_api_v1_execution_labs_toxicity_review__review_id__action_post
      parameters:
      - name: review_id
        in: path
        required: true
        schema:
          type: string
          title: Review Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/StagedAdverseEventActionRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/StagedAdverseEventActionResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/labs/toxicity/evaluate:
    post:
      tags:
      - Laboratory Governance & Ingestion
      summary: Evaluate Toxicity Realtime
      description: Evaluate and simulate CTCAE v5.0 / DAIDS v2.1 toxicity grading in real time.
      operationId: evaluate_toxicity_realtime_api_v1_execution_labs_toxicity_evaluate_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/LabToxicityEvaluateRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/LabToxicityEvaluateResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/labs/loinc-catalog:
    get:
      tags:
      - Laboratory Governance & Ingestion
      summary: Get Loinc Catalog Endpoint
      description: Retrieve standardized LOINC catalog mappings.
      operationId: get_loinc_catalog_endpoint_api_v1_execution_labs_loinc_catalog_get
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/LoincCatalogResponse'
  /api/v1/execution/labs/convert-unit:
    post:
      tags:
      - Laboratory Governance & Ingestion
      summary: Convert Unit Endpoint
      description: Convert laboratory measurement between conventional and SI units.
      operationId: convert_unit_endpoint_api_v1_execution_labs_convert_unit_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/LoincConvertRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/LoincConvertResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /events/study-published:
    post:
      summary: Study Published
      description: "Ingest study publication events and trigger layout generation asynchronously.\n\nArgs:\n    event (StudyEvent): The incoming study event payload.\n    background_tasks (BackgroundTasks): FastAPI background task manager.\n\nReturns:\n    dict[str, str]: A status message confirming job acceptance."
      operationId: study_published_events_study_published_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/StudyEvent'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties:
                  type: string
                type: object
                title: Response Study Published Events Study Published Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/translation/jobs:
    get:
      summary: List Translation Jobs
      description: Retrieve a list of historical translation jobs.
      operationId: list_translation_jobs_api_v1_execution_translation_jobs_get
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                items:
                  $ref: '#/components/schemas/TranslationJobResponse'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/TranslationJobResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/subjects:
    post:
      summary: Create Subject
      description: Create a new clinical subject pseudonymously.
      operationId: create_subject_api_v1_execution_subjects_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/SubjectCreate'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SubjectResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/SubjectConsentRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SubjectConsentResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/SubjectConsentRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SubjectConsentResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/webhooks/etmf:
    post:
      summary: Etmf Completeness Webhook
      description: Receive and process completeness webhook events from eTMF to update local compliance cache.
      operationId: etmf_completeness_webhook_api_v1_execution_webhooks_etmf_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ETMFCompletenessWebhookPayload'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                additionalProperties: true
                type: object
                title: Response Etmf Completeness Webhook Api V1 Execution Webhooks Etmf Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/SiteActivationRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties: true
                title: Response Activate Site Endpoint Api V1 Execution Sites  Site Id  Activate Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ComplianceStatusResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              - $ref: '#/components/schemas/SubjectScreeningRequest'
              - type: 'null'
              title: Payload
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SubjectScreeningResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/SubjectStateUpdateRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SubjectResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/SubjectStateUpdateRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SubjectResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/SubjectDemographicsUpdateRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SubjectResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SubjectResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/SubjectDemographicsUpdateRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SubjectResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SubjectResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/visits:
    post:
      summary: Create Visit
      description: Create a new clinical visit.
      operationId: create_visit_api_v1_execution_visits_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/VisitCreate'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/VisitResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SubjectDetailResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/VisitDetailResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/observations:
    post:
      summary: Create Observation
      description: CREATE a new clinical observation, performing unit normalization and outlier checks.
      operationId: create_observation_api_v1_execution_observations_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ObservationCreate'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ObservationResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/UnitConversionRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/UnitConversionResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/UnitConversionResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/UnitConversionRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/UnitConversionResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/UnitConversionResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/outliers/recalculate:
    post:
      summary: Trigger Outlier Recalculation
      description: Trigger cohort-wide outlier recalculation on-demand.
      operationId: trigger_outlier_recalculation_api_v1_execution_outliers_recalculate_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/OutlierRecalculateRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/OutlierRecalculateResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/LabReferenceRangeCreate'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/LabReferenceRangeResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
          - type: 'null'
          title: Study Id
      - name: test_code
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Test Code
      - name: source
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Source
      - name: lab_source
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Lab Source
      - name: include_deleted
        in: query
        required: false
        schema:
          type: boolean
          default: false
          title: Include Deleted
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/LabReferenceRangeResponse'
                title: Response List Lab Ranges Api V1 Execution Lab Ranges Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/LabReferenceRangeResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/LabReferenceRangeUpdate'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/LabReferenceRangeResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/LabReferenceRangeResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/lab-ranges/recalculate:
    post:
      summary: Trigger Lab Range Recalculation
      description: Trigger cohort-wide reference range evaluation and recalculation on-demand.
      operationId: trigger_lab_range_recalculation_api_v1_execution_lab_ranges_recalculate_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/LabRangeRecalculateRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/LabRangeRecalculateResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema: {}
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema: {}
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/FormSubmissionCreate'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/FormSubmissionResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
          - type: 'null'
          title: Study Id
      - name: subject_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Subject Id
      - name: visit_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Visit Id
      - name: form_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Form Id
      - name: include_inactive
        in: query
        required: false
        schema:
          type: boolean
          default: false
          title: Include Inactive
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/FormSubmissionResponse'
                title: Response List Form Submissions Api V1 Execution Form Submissions Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/FormSubmissionResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/FormSubmissionApprove'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/FormSubmissionResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/batch-sign-off:
    post:
      summary: Post Batch Sign Off
      description: Perform a PI-only, atomic batch electronic-signature for form-, visit-, and subject-level sign-off.
      operationId: post_batch_sign_off_api_v1_execution_batch_sign_off_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/BatchSignOffRequest'
        required: true
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/BatchSignOffResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/FormSubmissionResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Lock Site Endpoint Api V1 Execution Locks Site  Site Id  Freeze Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Lock Site Endpoint Api V1 Execution Locks Site  Site Id  Lock Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Unlock Site Endpoint Api V1 Execution Locks Site  Site Id  Unfreeze Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Unlock Site Endpoint Api V1 Execution Locks Site  Site Id  Unlock Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Lock Visit Endpoint Api V1 Execution Locks Visit  Visit Id  Freeze Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Lock Visit Endpoint Api V1 Execution Locks Visit  Visit Id  Lock Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Unlock Visit Endpoint Api V1 Execution Locks Visit  Visit Id  Unfreeze Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Unlock Visit Endpoint Api V1 Execution Locks Visit  Visit Id  Unlock Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Lock Form Endpoint Api V1 Execution Locks Form  Form Id  Freeze Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Lock Form Endpoint Api V1 Execution Locks Form  Form Id  Lock Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Unlock Form Endpoint Api V1 Execution Locks Form  Form Id  Unfreeze Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Unlock Form Endpoint Api V1 Execution Locks Form  Form Id  Unlock Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Lock Subject Endpoint Api V1 Execution Locks Subject  Subject Id  Freeze Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Lock Subject Endpoint Api V1 Execution Locks Subject  Subject Id  Lock Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Unlock Subject Endpoint Api V1 Execution Locks Subject  Subject Id  Unfreeze Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: object
                additionalProperties:
                  type: string
                title: Response Unlock Subject Endpoint Api V1 Execution Locks Subject  Subject Id  Unlock Post
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/locks/trial/freeze:
    post:
      summary: Lock Trial Endpoint
      description: Locks or freezes the trial/study.
      operationId: lock_trial_endpoint_api_v1_execution_locks_trial_freeze_post
      responses:
        '200':
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
        '200':
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
        '200':
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
        '200':
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
      description: 'End-point to dispense investigational product (IP) kits against site inventory.


        Checks site locks early, calls dispense_kit_transaction, and handles commits atomically.

        Launches resupply alerts via fastapi background tasks post-commit if triggered.'
      operationId: dispense_kit_endpoint_api_v1_execution_rtsm_dispense_post
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/DispenseRequest'
        required: true
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/DispenseResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
              $ref: '#/components/schemas/MigrationRuleCreate'
      responses:
        '201':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/MigrationRuleResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/MigrationRuleResponse'
                title: Response List Migration Rules Api V1 Execution Migration Rules Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/audit/integrity:
    get:
      summary: Get Execution Audit Integrity
      description: 'Verify the GxP clinical execution ledger integrity via block-sealing validation.


        Ensures that chronological audit logs, block-level seals, and sequential chaining

        remain structurally unbroken.'
      operationId: get_execution_audit_integrity_api_v1_execution_audit_integrity_get
      responses:
        '200':
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
          - type: 'null'
          title: Status
      - name: event_type
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Event Type
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  type: object
                  additionalProperties: true
                title: Response Execution Admin Outbox Endpoint Api V1 Admin Outbox Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/resupply-events:
    get:
      summary: List Resupply Events Endpoint
      description: Lists all active resupply events, filtered by query params and user's site-level/study-level authorization.
      operationId: list_resupply_events_endpoint_api_v1_execution_rtsm_resupply_events_get
      parameters:
      - name: study_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Study Id
      - name: site_id
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Site Id
      - name: status
        in: query
        required: false
        schema:
          anyOf:
          - type: string
          - type: 'null'
          title: Status
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/ResupplyEventResponse'
                title: Response List Resupply Events Endpoint Api V1 Execution Rtsm Resupply Events Get
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/resupply-events/{event_id}/confirm:
    post:
      summary: Approve Resupply Event Endpoint
      description: Approve a resupply event to trigger shipment generation.
      operationId: approve_resupply_event_endpoint_api_v1_execution_rtsm_resupply_events__event_id__confirm_post
      parameters:
      - name: event_id
        in: path
        required: true
        schema:
          type: string
          title: Event Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ResupplyEventApprovalRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ResupplyEventResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
  /api/v1/execution/rtsm/resupply-events/{event_id}/reject:
    post:
      summary: Reject Resupply Event Endpoint
      description: Reject a resupply event.
      operationId: reject_resupply_event_endpoint_api_v1_execution_rtsm_resupply_events__event_id__reject_post
      parameters:
      - name: event_id
        in: path
        required: true
        schema:
          type: string
          title: Event Id
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ResupplyEventApprovalRequest'
      responses:
        '200':
          description: Successful Response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ResupplyEventResponse'
        '422':
          description: Validation Error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HTTPValidationError'
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
          - type: 'null'
          title: Epoch Id
        epoch_internal_id:
          type: integer
          title: Epoch Internal Id
        scheduled_event_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Scheduled Event Id
        scheduled_event_internal_id:
          type: integer
          title: Scheduled Event Internal Id
        activity_def_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Activity Def Id
        activity_def_internal_id:
          type: integer
          title: Activity Def Internal Id
        status:
          type: string
          title: Status
        unmapped_items:
          items:
            $ref: '#/components/schemas/ItemMappingStatus'
          type: array
          title: Unmapped Items
        mapped_items:
          items:
            $ref: '#/components/schemas/ItemMappingStatus'
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
          - type: 'null'
          title: Base Version
          description: Parent/base version tag
        amended_version:
          anyOf:
          - type: string
          - type: 'null'
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
    AmendmentImpactSummary:
      properties:
        base_version:
          type: string
          title: Base Version
          description: Baseline protocol version tag
        amended_version:
          type: string
          title: Amended Version
          description: Amended protocol version tag
        burden_delta:
          type: number
          title: Burden Delta
          description: Quantitative burden score delta (+/- burden index)
          default: 0.0
        affected_visits_count:
          type: integer
          title: Affected Visits Count
          description: Total affected visits count
          default: 0
        affected_visits:
          items:
            type: string
          type: array
          title: Affected Visits
          description: Names of affected visits/encounters
        affected_activities_count:
          type: integer
          title: Affected Activities Count
          description: Total affected procedures count
          default: 0
        affected_activities:
          items:
            type: string
          type: array
          title: Affected Activities
          description: Names of affected activities/procedures
        schema_revisions:
          $ref: '#/components/schemas/SchemaRevisionSummary'
          description: Detailed schema revision breakdown
        is_substantial:
          type: boolean
          title: Is Substantial
          description: Whether amendment is substantial vs administrative
          default: false
        requires_reconsent:
          type: boolean
          title: Requires Reconsent
          description: Whether mandatory subject re-consent is mandated (PRD-SUB-007)
          default: false
        estimated_cost_usd:
          type: number
          title: Estimated Cost Usd
          description: Estimated total cost impact in USD
          default: 0.0
        narrative_summary:
          type: string
          title: Narrative Summary
          description: Executive clinical narrative summary of changes
          default: ''
      type: object
      required:
      - base_version
      - amended_version
      title: AmendmentImpactSummary
      description: 'Calculated operational and clinical burden impact of a protocol amendment.


        Requirements: PRD-SYS-001, PRD-SUB-007'
    ApproveProtocolRequest:
      properties:
        signing_reason:
          $ref: '#/components/schemas/SigningReason'
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
          $ref: '#/components/schemas/LibraryStatus'
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
          - type: 'null'
          title: Updated At
          description: Audit timestamp of last update.
        updated_by:
          anyOf:
          - type: string
          - type: 'null'
          title: Updated By
          description: User ID of last updater.
        reason_for_change:
          anyOf:
          - type: string
          - type: 'null'
          title: Reason For Change
          description: Detailed explanation of changes applied.
        prior_status:
          anyOf:
          - type: string
          - type: 'null'
          title: Prior Status
          description: Previous status before transition.
        object_type:
          type: string
          const: ARM
          title: Object Type
          default: ARM
        payload:
          $ref: '#/components/schemas/ArmPayload'
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
          $ref: '#/components/schemas/ArmAttributes'
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
            $ref: '#/components/schemas/ArmReorderItem'
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
    Body_extract_protocol_digitization_api_v1_designer_digitization_extract_post:
      properties:
        file:
          type: string
          contentMediaType: application/octet-stream
          title: File
      type: object
      required:
      - file
      title: Body_extract_protocol_digitization_api_v1_designer_digitization_extract_post
    Body_start_digitization_dag_job_api_v1_designer_digitization_dag_jobs_post:
      properties:
        file:
          type: string
          contentMediaType: application/octet-stream
          title: File
        study_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Study Id
      type: object
      required:
      - file
      title: Body_start_digitization_dag_job_api_v1_designer_digitization_dag_jobs_post
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
    BranchAmendmentRequest:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Target study ID
        base_version_tag:
          type: string
          title: Base Version Tag
          description: Base approved version tag to branch from
          default: 1.0.0
        amendment_type:
          type: string
          title: Amendment Type
          description: 'Amendment classification: ''major'' or ''minor'''
          default: minor
        requires_reconsent:
          type: boolean
          title: Requires Reconsent
          description: Flag indicating if subject re-consent is mandated
          default: false
        change_reason:
          type: string
          title: Change Reason
          description: GxP 21 CFR Part 11 change justification reason
        branch_name:
          anyOf:
          - type: string
          - type: 'null'
          title: Branch Name
          description: Optional custom branch name (e.g. amendment-v2.0-draft)
      type: object
      required:
      - study_id
      - change_reason
      title: BranchAmendmentRequest
      description: 'Payload to create an immutable amendment branch.


        Requirements: PRD-SYS-001, PRD-SUB-007'
    BranchAmendmentResponse:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Target study ID
        branch_id:
          type: string
          title: Branch Id
          description: Unique branch identifier
        branch_name:
          type: string
          title: Branch Name
          description: Branch name
        base_version_tag:
          type: string
          title: Base Version Tag
          description: Base version tag
        new_version_tag:
          type: string
          title: New Version Tag
          description: New draft version tag
        version_id:
          type: string
          title: Version Id
          description: New version node identifier
        status:
          type: string
          title: Status
          description: Lifecycle status (e.g. DRAFT_AMENDMENT)
        requires_reconsent:
          type: boolean
          title: Requires Reconsent
          description: Re-consent requirement flag
        created_by:
          type: string
          title: Created By
          description: User ID who created branch
        created_at:
          type: string
          title: Created At
          description: UTC ISO creation timestamp
      type: object
      required:
      - study_id
      - branch_id
      - branch_name
      - base_version_tag
      - new_version_tag
      - version_id
      - status
      - requires_reconsent
      - created_by
      - created_at
      title: BranchAmendmentResponse
      description: 'Response after creating an immutable amendment branch.


        Requirements: PRD-SYS-001'
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
            $ref: '#/components/schemas/BurdenTraceItem'
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
            $ref: '#/components/schemas/CascadedFormTemplate'
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
      description: 'Summary report of downstream eCRF and SoA cascade propagation.


        Requirements: PRD-SYS-001'
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
      description: 'Auto-generated eCRF form template derived from USDM activities.


        Requirements: PRD-SYS-001'
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
          - type: 'null'
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
            $ref: '#/components/schemas/Comment'
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
    CommitUSDMRequest:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Unique study identifier
        data:
          $ref: '#/components/schemas/USDMProtocolExtractionResponse'
          description: USDM extraction data
        change_reason:
          type: string
          title: Change Reason
          description: GxP change justification reason
      type: object
      required:
      - study_id
      - data
      - change_reason
      title: CommitUSDMRequest
      description: Request payload for committing extracted USDM protocol data into Neo4j.
    CommitUSDMResponse:
      properties:
        study_id:
          type: string
          title: Study Id
        version_id:
          type: string
          title: Version Id
        status:
          type: string
          title: Status
        nodes_created:
          type: integer
          title: Nodes Created
        relationships_created:
          type: integer
          title: Relationships Created
        synthesized_forms:
          items:
            $ref: '#/components/schemas/SynthesizedECRFForm'
          type: array
          title: Synthesized Forms
        message:
          type: string
          title: Message
      type: object
      required:
      - study_id
      - version_id
      - status
      - nodes_created
      - relationships_created
      - message
      title: CommitUSDMResponse
      description: Response payload following USDM graph population and eCRF synthesis.
    ComparisonOperator:
      type: string
      enum:
      - ==
      - '!='
      - <
      - <=
      - '>'
      - '>='
      title: ComparisonOperator
      description: Allowed binary comparison operators for criteria evaluations.
    CompileUSDMFromJobRequest:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Target clinical study ID
        change_reason:
          type: string
          title: Change Reason
          description: GxP change justification reason
      type: object
      required:
      - study_id
      - change_reason
      title: CompileUSDMFromJobRequest
      description: Request to commit the USDM protocol graph generated by a completed DAG job into Neo4j.
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
          - $ref: '#/components/schemas/CDASHMapping'
          - type: 'null'
        allowable_units:
          anyOf:
          - items:
              $ref: '#/components/schemas/AllowableUnit'
            type: array
          - type: 'null'
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
          - type: 'null'
          title: Updated At
        updated_by:
          anyOf:
          - type: string
          - type: 'null'
          title: Updated By
        reason_for_change:
          anyOf:
          - type: string
          - type: 'null'
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
            $ref: '#/components/schemas/ConceptDetail'
          type: array
          title: Data
        has_more:
          type: boolean
          title: Has More
        next_cursor:
          anyOf:
          - type: string
          - type: 'null'
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
          $ref: '#/components/schemas/CodeValidationState'
        decode:
          anyOf:
          - type: string
          - type: 'null'
          title: Decode
        system:
          anyOf:
          - type: string
          - type: 'null'
          title: System
        error_message:
          anyOf:
          - type: string
          - type: 'null'
          title: Error Message
        references:
          items:
            $ref: '#/components/schemas/ConceptReference'
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
          $ref: '#/components/schemas/LibraryStatus'
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
          $ref: '#/components/schemas/ArmPayload'
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
          - type: 'null'
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
          - $ref: '#/components/schemas/CDASHMapping'
          - type: 'null'
        allowable_units:
          anyOf:
          - items:
              $ref: '#/components/schemas/AllowableUnit'
            type: array
          - type: 'null'
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
          $ref: '#/components/schemas/LibraryStatus'
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
          $ref: '#/components/schemas/DataElementPayload'
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
          $ref: '#/components/schemas/EpochProperties'
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
          $ref: '#/components/schemas/LibraryStatus'
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
          $ref: '#/components/schemas/FormPayload'
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
          $ref: '#/components/schemas/ProcedureProperties'
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
          $ref: '#/components/schemas/ExpressionNode-Input'
        action:
          anyOf:
          - type: string
            enum:
            - show
            - hide
          - type: 'null'
          title: Action
        target_field:
          anyOf:
          - type: string
          - type: 'null'
          title: Target Field
        target_form:
          anyOf:
          - type: string
          - type: 'null'
          title: Target Form
        target_group:
          anyOf:
          - type: string
          - type: 'null'
          title: Target Group
        query_message:
          anyOf:
          - type: string
          - type: 'null'
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
          $ref: '#/components/schemas/StudyArmProperties'
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
          $ref: '#/components/schemas/TimingWindowProperties'
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
    DAGJobStatusResponse:
      properties:
        job_id:
          type: string
          title: Job Id
          description: Unique DAG job identifier
        study_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Study Id
          description: Associated study ID
        status:
          $ref: '#/components/schemas/DigitizationJobStatus'
          description: Current job status
        current_stage:
          anyOf:
          - $ref: '#/components/schemas/DigitizationStage'
          - type: 'null'
          description: Current or last executed stage
        progress_pct:
          type: integer
          maximum: 100.0
          minimum: 0.0
          title: Progress Pct
          description: Overall completion progress percentage
        created_at:
          type: string
          format: date-time
          title: Created At
          description: Creation UTC timestamp
        updated_at:
          type: string
          format: date-time
          title: Updated At
          description: Last updated UTC timestamp
        checkpoints:
          additionalProperties:
            $ref: '#/components/schemas/StageCheckpoint'
          type: object
          title: Checkpoints
          description: Stage checkpoints record
        error_message:
          anyOf:
          - type: string
          - type: 'null'
          title: Error Message
          description: Error details if job failed
        is_terminal:
          type: boolean
          title: Is Terminal
          description: True if job has reached COMPLETED, FAILED, or CANCELLED state
        final_usdm_payload:
          anyOf:
          - $ref: '#/components/schemas/USDMProtocolExtractionResponse'
          - type: 'null'
          description: Final extracted USDM payload when completed
        synthesized_forms:
          items:
            $ref: '#/components/schemas/SynthesizedECRFForm'
          type: array
          title: Synthesized Forms
          description: Synthesized eCRFs when completed
      type: object
      required:
      - job_id
      - status
      - progress_pct
      - created_at
      - updated_at
      - is_terminal
      title: DAGJobStatusResponse
      description: Response describing the real-time execution progress of a digitization DAG job.
    DataCaptureEcrfImpact:
      properties:
        affected_forms_count:
          type: integer
          title: Affected Forms Count
          description: Total count of affected eCRF forms
          default: 0
        added_forms:
          items:
            type: string
          type: array
          title: Added Forms
          description: Names/keys of newly added forms
        modified_forms:
          items:
            type: string
          type: array
          title: Modified Forms
          description: Names/keys of modified forms
        removed_forms:
          items:
            type: string
          type: array
          title: Removed Forms
          description: Names/keys of deprecated/removed forms
        affected_visits:
          items:
            type: string
          type: array
          title: Affected Visits
          description: Names of visits with changed data capture requirements
        new_cdash_fields:
          items:
            type: string
          type: array
          title: New Cdash Fields
          description: New CDASH domain variables required
        rule_modifications_count:
          type: integer
          title: Rule Modifications Count
          description: Number of validation rules and edit checks affected
          default: 0
        estimated_build_hours:
          type: number
          title: Estimated Build Hours
          description: Estimated data management build hours
          default: 0.0
        action_items:
          items:
            type: string
          type: array
          title: Action Items
          description: Step-by-step action plan for Data Management
      type: object
      title: DataCaptureEcrfImpact
      description: Manifest of impact on electronic data capture, visit schedules, and eCRF forms.
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
          $ref: '#/components/schemas/LibraryStatus'
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
          - type: 'null'
          title: Updated At
          description: Audit timestamp of last update.
        updated_by:
          anyOf:
          - type: string
          - type: 'null'
          title: Updated By
          description: User ID of last updater.
        reason_for_change:
          anyOf:
          - type: string
          - type: 'null'
          title: Reason For Change
          description: Detailed explanation of changes applied.
        prior_status:
          anyOf:
          - type: string
          - type: 'null'
          title: Prior Status
          description: Previous status before transition.
        object_type:
          type: string
          const: DATA_ELEMENT
          title: Object Type
          default: DATA_ELEMENT
        payload:
          $ref: '#/components/schemas/DataElementPayload'
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
          - type: 'null'
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
    DigitizationJobStatus:
      type: string
      enum:
      - PENDING
      - RUNNING
      - COMPLETED
      - FAILED
      - CANCELLED
      title: DigitizationJobStatus
      description: Execution status of a protocol digitization DAG job.
    DigitizationStage:
      type: string
      enum:
      - LAYOUT_PARSING
      - SOA_EXTRACTION
      - BIOMEDICAL_CONCEPT_MAPPING
      - ECRF_SYNTHESIS
      - USDM_COMPILATION
      title: DigitizationStage
      description: Enumeration of sequential stages in the Protocol Digitization DAG.
    DispatchedTicketInfo:
      properties:
        ticket_id:
          type: string
          title: Ticket Id
          description: Created ticket ID
        reference:
          type: string
          title: Reference
          description: Sequential ticket reference (e.g. TKT-00101)
        domain_queue:
          $ref: '#/components/schemas/DomainQueue'
          description: Domain queue
        title:
          type: string
          title: Title
          description: Ticket title
        priority:
          type: string
          title: Priority
          description: Priority
        status:
          type: string
          title: Status
          description: Initial status (OPEN)
        assignee_role:
          type: string
          title: Assignee Role
          description: Assigned role
      type: object
      required:
      - ticket_id
      - reference
      - domain_queue
      - title
      - priority
      - status
      - assignee_role
      title: DispatchedTicketInfo
      description: Summary of a successfully dispatched operational ticket.
    DomainQueue:
      type: string
      enum:
      - DATA_CAPTURE_ECRF
      - SUBJECT_MANAGEMENT_RTSM
      - REGULATORY_COMPLIANCE
      - SITE_OPERATIONS
      - CLINICAL_OPERATIONS
      title: DomainQueue
      description: Target clinical and operational domain queues for ticket routing.
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
          default: ''
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
          default: ''
        human_readable_text:
          type: string
          title: Human Readable Text
          description: Human-readable text description of the criterion.
          default: ''
        dsl_expression_string:
          type: string
          title: Dsl Expression String
          description: The raw DSL statement source, e.g., 'eCRF.DM.AGE >= 18'.
          default: ''
        structured_expression_tree:
          $ref: '#/components/schemas/ExpressionNode-Output'
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
          default: ''
        description:
          type: string
          title: Description
          description: Backward compatible description field.
          default: ''
        dsl_source:
          type: string
          title: Dsl Source
          description: Backward compatible dsl_source field.
          default: ''
        condition:
          $ref: '#/components/schemas/ExpressionNode-Output'
          description: Backward compatible condition field.
      type: object
      required:
      - created_by
      - reason_for_change
      - criterion_type
      title: EligibilityCriterion
      description: Represents a single inclusion or exclusion criterion with full GxP audit metadata.
    EntityDiff:
      properties:
        entity_id:
          type: string
          title: Entity Id
          description: Unique entity identifier
        entity_type:
          type: string
          title: Entity Type
          description: 'Entity type: Arm, Epoch, Encounter, Activity, Criterion, Form, Rule'
        name:
          type: string
          title: Name
          description: Display name of the entity
        change_type:
          type: string
          title: Change Type
          description: 'Diff status: ADDED, REMOVED, MODIFIED, PRESERVED, UNCHANGED'
        spec:
          anyOf:
          - type: string
          - type: 'null'
          title: Spec
          description: Specification or subtitle details
        schedule:
          anyOf:
          - type: string
          - type: 'null'
          title: Schedule
          description: Schedule timing / epoch details
        delta_note:
          anyOf:
          - type: string
          - type: 'null'
          title: Delta Note
          description: Specific note describing the delta
        old_value:
          anyOf:
          - {}
          - type: 'null'
          title: Old Value
          description: Previous baseline value
        new_value:
          anyOf:
          - {}
          - type: 'null'
          title: New Value
          description: Amended new value
      type: object
      required:
      - entity_id
      - entity_type
      - name
      - change_type
      title: EntityDiff
      description: 'Entity-level visual diff with semantic tokens.


        Requirements: PRD-SYS-001'
    EpochProperties:
      properties:
        name:
          anyOf:
          - type: string
            minLength: 1
          - type: 'null'
          title: Name
          description: The name of the study epoch, e.g., 'Screening'.
        epoch_name:
          anyOf:
          - type: string
            minLength: 1
          - type: 'null'
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
            $ref: '#/components/schemas/EpochReorderItem'
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
          - type: 'null'
          title: Operator
        operands:
          anyOf:
          - items:
              $ref: '#/components/schemas/ExpressionNode-Input'
            type: array
          - type: 'null'
          title: Operands
        value:
          anyOf:
          - {}
          - type: 'null'
          title: Value
        field_ref:
          anyOf:
          - $ref: '#/components/schemas/FieldReference-Input'
          - type: 'null'
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
          - $ref: '#/components/schemas/ComparisonOperator'
          - $ref: '#/components/schemas/LogicalOperator'
          - type: string
          - type: 'null'
          title: Operator
          description: Operator for logical (and, or, not) or comparison (==, !=, <, <=, >, >=) nodes.
        operands:
          anyOf:
          - items:
              $ref: '#/components/schemas/ExpressionNode-Output'
            type: array
          - type: 'null'
          title: Operands
          description: Child operands of logical or comparison nodes.
        value:
          anyOf:
          - {}
          - type: 'null'
          title: Value
          description: Literal constant value of type constant.
        field_ref:
          anyOf:
          - $ref: '#/components/schemas/FieldReference-Output'
          - type: 'null'
          description: Field reference details of type field_ref.
      type: object
      required:
      - type
      title: ExpressionNode
      description: 'Recursive node inside a structured clinical expression tree (AST).

        Supported types are: logical, comparison, field_ref, constant.'
    ExtractedActivity:
      properties:
        activity_name:
          type: string
          title: Activity Name
          description: Procedure e.g., '12-Lead ECG', 'Vital Signs'
        cdash_domain:
          type: string
          title: Cdash Domain
          description: CDASH domain code e.g., 'EG', 'VS', 'LB'
        biomedical_concept_code:
          anyOf:
          - type: string
          - type: 'null'
          title: Biomedical Concept Code
          description: NCI Thesaurus or SNOMED CT concept code
        assigned_visit_names:
          items:
            type: string
          type: array
          title: Assigned Visit Names
          description: List of visit names where this procedure is scheduled
      type: object
      required:
      - activity_name
      - cdash_domain
      title: ExtractedActivity
      description: Represents a clinical procedure or assessment extracted from the protocol.
    ExtractedArm:
      properties:
        name:
          type: string
          title: Name
          description: Arm name e.g., 'Dose Escalation Cohort A'
        arm_type:
          type: string
          enum:
          - EXPERIMENTAL
          - ACTIVE_COMPARATOR
          - PLACEBO_COMPARATOR
          - SHAM_COMPARATOR
          - NO_INTERVENTION
          title: Arm Type
          description: Arm classification type
        description:
          anyOf:
          - type: string
          - type: 'null'
          title: Description
          description: Clinical description of the arm
        target_sample_size:
          anyOf:
          - type: integer
          - type: 'null'
          title: Target Sample Size
          description: Target planned sample size
      type: object
      required:
      - name
      - arm_type
      title: ExtractedArm
      description: Represents a clinical study arm extracted from protocol documentation.
    ExtractedCriterion:
      properties:
        criterion_type:
          type: string
          enum:
          - INCLUSION
          - EXCLUSION
          title: Criterion Type
          description: Criterion classification
        identifier:
          type: string
          title: Identifier
          description: e.g. 'INC-01', 'EXC-04'
        text_expression:
          type: string
          title: Text Expression
          description: Original protocol text requirement
        logical_expression:
          anyOf:
          - type: string
          - type: 'null'
          title: Logical Expression
          description: Compiled logical check e.g., 'DM.AGE >= 18'
      type: object
      required:
      - criterion_type
      - identifier
      - text_expression
      title: ExtractedCriterion
      description: Represents an eligibility criterion extracted from the protocol.
    ExtractedEpoch:
      properties:
        name:
          type: string
          title: Name
          description: Epoch name e.g., 'Screening', 'Treatment', 'Follow-up'
        epoch_type:
          type: string
          enum:
          - SCREENING
          - TREATMENT
          - WASHOUT
          - FOLLOW_UP
          - RUN_IN
          title: Epoch Type
          description: Epoch categorization type
        sequence_index:
          type: integer
          title: Sequence Index
          description: Chronological sequence order index
      type: object
      required:
      - name
      - epoch_type
      - sequence_index
      title: ExtractedEpoch
      description: Represents a trial epoch extracted from protocol documentation.
    ExtractedVisit:
      properties:
        visit_name:
          type: string
          title: Visit Name
          description: Visit name e.g., 'Visit 1 / Day 1 (Baseline)'
        epoch_name:
          type: string
          title: Epoch Name
          description: Associated parent epoch identifier name
        target_day:
          type: integer
          title: Target Day
          description: Target protocol timeline day
        window_lower_days:
          type: integer
          title: Window Lower Days
          description: Visit window lower margin in days
          default: 0
        window_upper_days:
          type: integer
          title: Window Upper Days
          description: Visit window upper margin in days
          default: 0
        is_mandatory:
          type: boolean
          title: Is Mandatory
          description: Whether visit is required for protocol compliance
          default: true
      type: object
      required:
      - visit_name
      - epoch_name
      - target_day
      title: ExtractedVisit
      description: Represents a study encounter or visit extracted from the protocol.
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
            $ref: '#/components/schemas/AttritionStep'
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
          - type: 'null'
          title: Form Id
        visit_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Visit Id
        visit_relative:
          anyOf:
          - type: string
          - type: 'null'
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
      description: 'Represents a structured field reference pointing to an eCRF domain variable.

        Format must strictly follow: eCRF.<DOMAIN>.<VARIABLE>'
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
          $ref: '#/components/schemas/LibraryStatus'
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
          - type: 'null'
          title: Updated At
          description: Audit timestamp of last update.
        updated_by:
          anyOf:
          - type: string
          - type: 'null'
          title: Updated By
          description: User ID of last updater.
        reason_for_change:
          anyOf:
          - type: string
          - type: 'null'
          title: Reason For Change
          description: Detailed explanation of changes applied.
        prior_status:
          anyOf:
          - type: string
          - type: 'null'
          title: Prior Status
          description: Previous status before transition.
        object_type:
          type: string
          const: FORM
          title: Object Type
          default: FORM
        payload:
          $ref: '#/components/schemas/FormPayload'
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
            $ref: '#/components/schemas/FormItem'
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
    GenerateStartupTemplateRequest:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Study Identifier
        country_codes:
          items:
            type: string
          type: array
          title: Country Codes
          description: List of target country codes (e.g. ['US', 'EU', 'JP', 'GB'])
      type: object
      required:
      - study_id
      - country_codes
      title: GenerateStartupTemplateRequest
    GenerateStartupTemplateResponse:
      properties:
        study_id:
          type: string
          title: Study Id
        generated_rules:
          items:
            $ref: '#/components/schemas/PrerequisiteCriterionTemplate'
          type: array
          title: Generated Rules
        summary_by_country:
          additionalProperties:
            type: integer
          type: object
          title: Summary By Country
      type: object
      required:
      - study_id
      - generated_rules
      - summary_by_country
      title: GenerateStartupTemplateResponse
    HTTPValidationError:
      properties:
        detail:
          items:
            $ref: '#/components/schemas/ValidationError'
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
          - type: 'null'
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
          - type: 'null'
          title: Field
        reason:
          anyOf:
          - type: string
          - type: 'null'
          title: Reason
        value:
          anyOf:
          - type: string
          - type: 'null'
          title: Value
      type: object
      title: InvalidParam
    ItemMappingStatus:
      properties:
        item_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Item Id
        internal_id:
          anyOf:
          - type: integer
          - type: 'null'
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
    JurisdictionInfo:
      properties:
        code:
          type: string
          title: Code
        name:
          type: string
          title: Name
        regulatory_framework:
          type: string
          title: Regulatory Framework
        ethics_framework:
          type: string
          title: Ethics Framework
        mandatory_criteria_count:
          type: integer
          title: Mandatory Criteria Count
      type: object
      required:
      - code
      - name
      - regulatory_framework
      - ethics_framework
      - mandatory_criteria_count
      title: JurisdictionInfo
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
          $ref: '#/components/schemas/InstantiatedFromDetail'
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
          - type: 'null'
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
            - $ref: '#/components/schemas/FormLibraryObjectDetail'
            - $ref: '#/components/schemas/DataElementLibraryObjectDetail'
            - $ref: '#/components/schemas/ArmLibraryObjectDetail'
            - $ref: '#/components/schemas/VisitLibraryObjectDetail'
            discriminator:
              propertyName: object_type
              mapping:
                ARM: '#/components/schemas/ArmLibraryObjectDetail'
                DATA_ELEMENT: '#/components/schemas/DataElementLibraryObjectDetail'
                FORM: '#/components/schemas/FormLibraryObjectDetail'
                VISIT: '#/components/schemas/VisitLibraryObjectDetail'
          type: array
          title: Data
        has_more:
          type: boolean
          title: Has More
        next_cursor:
          anyOf:
          - type: string
          - type: 'null'
          title: Next Cursor
      type: object
      required:
      - data
      - has_more
      title: LibraryObjectListResponse
      description: 'Paginated response envelope for Global Library Objects.

        Matches Stripe-style list response.'
    LibraryObjectTransitionRequest:
      properties:
        status:
          $ref: '#/components/schemas/LibraryStatus'
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
    MigrationDirective:
      properties:
        directive_id:
          type: string
          title: Directive Id
          description: Unique migration directive ID
        action:
          type: string
          title: Action
          description: 'Directive action: RECONSENT_GATE, SCHEMA_UPGRADE, PRESERVE_HISTORICAL'
        description:
          type: string
          title: Description
          description: Detailed directive description
        affected_cohort:
          type: string
          title: Affected Cohort
          description: 'Target subject cohort: ACTIVE, COMPLETED, ALL'
          default: ACTIVE
        target_version:
          type: string
          title: Target Version
          description: Target protocol version tag
      type: object
      required:
      - directive_id
      - action
      - description
      - target_version
      title: MigrationDirective
      description: 'Directive for in-flight subject migration and version transition.


        Requirements: PRD-SYS-001, PRD-SUB-007'
    NarrativeDelta:
      properties:
        section_id:
          type: string
          title: Section Id
          description: Protocol section key or identifier
        section_title:
          type: string
          title: Section Title
          description: Section title
        change_type:
          type: string
          title: Change Type
          description: 'Change type: ADDED, MODIFIED, REMOVED, UNCHANGED'
        old_text:
          anyOf:
          - type: string
          - type: 'null'
          title: Old Text
          description: Baseline section text
        new_text:
          anyOf:
          - type: string
          - type: 'null'
          title: New Text
          description: Amended section text
        delta_summary:
          type: string
          title: Delta Summary
          description: Human-readable summary of textual changes
        safety_risk_impact:
          type: boolean
          title: Safety Risk Impact
          description: Whether changes alter safety profile or adverse event risks
          default: false
      type: object
      required:
      - section_id
      - section_title
      - change_type
      - delta_summary
      title: NarrativeDelta
      description: Semantic comparison between protocol narrative sections.
    ObjectType:
      type: string
      enum:
      - FORM
      - DATA_ELEMENT
      - ARM
      - VISIT
      title: ObjectType
      description: Supported types of clinical design objects in the Global Library.
    OperationalTicketBlueprint:
      properties:
        domain_queue:
          $ref: '#/components/schemas/DomainQueue'
          description: Target domain queue for role-based triage
        title:
          type: string
          title: Title
          description: Action-oriented ticket title
        description:
          type: string
          title: Description
          description: Comprehensive problem & change context
        category:
          type: string
          title: Category
          description: 'Tickets category: CHANGE_REQUEST, CLINICAL, etc.'
          default: CHANGE_REQUEST
        priority:
          type: string
          title: Priority
          description: 'Priority level: LOW, MEDIUM, HIGH, CRITICAL'
          default: HIGH
        gxp_severity:
          type: string
          title: Gxp Severity
          description: 'GxP compliance severity: NOT_APPLICABLE, MINOR, MAJOR, CRITICAL'
          default: MAJOR
        assignee_role:
          type: string
          title: Assignee Role
          description: Default operational role responsible for ticket resolution
        action_plan:
          items:
            type: string
          type: array
          title: Action Plan
          description: Sequential pre-filled action items
        due_date_offset_days:
          anyOf:
          - type: integer
          - type: 'null'
          title: Due Date Offset Days
          description: Suggested completion SLA in days from amendment creation
        context_payload:
          additionalProperties: true
          type: object
          title: Context Payload
          description: Structured metadata payload for context linking
      type: object
      required:
      - domain_queue
      - title
      - description
      - assignee_role
      title: OperationalTicketBlueprint
      description: Structured actionable ticket blueprint ready for dispatch to apps/tickets.
    PrerequisiteCriterionTemplate:
      properties:
        country_code:
          type: string
          title: Country Code
        facet:
          type: string
          title: Facet
        criterion_code:
          type: string
          title: Criterion Code
        criterion_name:
          type: string
          title: Criterion Name
        is_mandatory:
          type: boolean
          title: Is Mandatory
        description:
          anyOf:
          - type: string
          - type: 'null'
          title: Description
      type: object
      required:
      - country_code
      - facet
      - criterion_code
      - criterion_name
      - is_mandatory
      title: PrerequisiteCriterionTemplate
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
              $ref: '#/components/schemas/InvalidParam'
            type: array
          - type: 'null'
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
          - type: 'null'
          title: Name
          description: The display name of the procedure.
        activity_name:
          anyOf:
          - type: string
            minLength: 1
          - type: 'null'
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
            $ref: '#/components/schemas/ProcedureReorderItem'
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
          - type: 'null'
          title: Amendment Type
          default: minor
        type:
          anyOf:
          - type: string
          - type: 'null'
          title: Type
      type: object
      title: ProtocolAmendRequest
      description: Payload for the Protocol/Designer Amendment endpoint.
    ProtocolImpactAssessment:
      properties:
        assessment_id:
          type: string
          title: Assessment Id
          description: Unique assessment identifier
        study_id:
          type: string
          title: Study Id
          description: Target study identifier
        base_version:
          type: string
          title: Base Version
          description: Baseline protocol version tag
        amended_version:
          type: string
          title: Amended Version
          description: Amended protocol version tag
        amendment_type:
          type: string
          title: Amendment Type
          description: 'Amendment classification: major or minor'
          default: minor
        is_substantial:
          type: boolean
          title: Is Substantial
          description: Whether amendment is substantial vs administrative
          default: false
        requires_reconsent:
          type: boolean
          title: Requires Reconsent
          description: Whether mandatory subject re-consent is mandated
          default: false
        patient_burden_delta:
          type: number
          title: Patient Burden Delta
          description: Calculated patient burden score change
          default: 0.0
        estimated_cost_usd:
          type: number
          title: Estimated Cost Usd
          description: Estimated total operational cost delta in USD
          default: 0.0
        executive_summary:
          type: string
          title: Executive Summary
          description: Executive narrative summary of the ripple effect analysis
        graph_deltas:
          items:
            $ref: '#/components/schemas/EntityDiff'
          type: array
          title: Graph Deltas
          description: USDM Graph entity diffs
        soa_deltas:
          items:
            $ref: '#/components/schemas/EntityDiff'
          type: array
          title: Soa Deltas
          description: Schedule of Activities matrix diffs
        narrative_deltas:
          items:
            $ref: '#/components/schemas/NarrativeDelta'
          type: array
          title: Narrative Deltas
          description: Narrative section textual diffs
        data_capture_ecrf:
          $ref: '#/components/schemas/DataCaptureEcrfImpact'
          description: eCRF & Visit data capture domain manifest
        subject_management_rtsm:
          $ref: '#/components/schemas/SubjectManagementRtsmImpact'
          description: RTSM & Dosing domain manifest
        regulatory_compliance:
          $ref: '#/components/schemas/RegulatoryComplianceImpact'
          description: Regulatory & Safety compliance domain manifest
        reconsent_gating_plan:
          $ref: '#/components/schemas/ReConsentGatingPlan'
          description: Subject re-consent gating enforcement plan
        operational_tickets:
          items:
            $ref: '#/components/schemas/OperationalTicketBlueprint'
          type: array
          title: Operational Tickets
          description: Domain-routed operational ticket blueprints
        migration_directives:
          items:
            $ref: '#/components/schemas/MigrationDirective'
          type: array
          title: Migration Directives
          description: In-flight migration directives
        created_at:
          type: string
          title: Created At
          description: UTC ISO timestamp of assessment
      type: object
      required:
      - assessment_id
      - study_id
      - base_version
      - amended_version
      - executive_summary
      - data_capture_ecrf
      - subject_management_rtsm
      - regulatory_compliance
      - reconsent_gating_plan
      - created_at
      title: ProtocolImpactAssessment
      description: Consolidated Protocol Amendment Ripple-Effect Impact Assessment.
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
            $ref: '#/components/schemas/QualityRuleFinding'
          type: array
          title: Findings
          description: Quality findings
        passed:
          type: boolean
          title: Passed
          description: True if no ERROR severity findings exist
        readability:
          anyOf:
          - $ref: '#/components/schemas/ReadabilityReport'
          - type: 'null'
          description: Readability metrics of narrative text blocks
        burden_details:
          anyOf:
          - $ref: '#/components/schemas/BurdenTraceReport'
          - type: 'null'
          description: Traceable operational burden details
        amendment_impact:
          anyOf:
          - $ref: '#/components/schemas/AmendmentImpactReport'
          - type: 'null'
          description: Amendment impact and cost estimation
        feasibility:
          anyOf:
          - $ref: '#/components/schemas/FeasibilityReport'
          - type: 'null'
          description: Pluggable patient population feasibility metrics
      type: object
      required:
      - study_id
      - quality_score
      - patient_burden_index
      - passed
      title: ProtocolQualityScore
      description: 'Protocol Quality Sentinel evaluation summary report.


        Requirements: PRD-SYS-001'
    PublishStudyVersionRequest:
      properties:
        description:
          anyOf:
          - type: string
          - type: 'null'
          title: Description
          description: Amendment description
        baseline_snapshot:
          anyOf:
          - additionalProperties: true
            type: object
          - type: 'null'
          title: Baseline Snapshot
          description: Optional baseline snapshot overrides
        amended_snapshot:
          anyOf:
          - additionalProperties: true
            type: object
          - type: 'null'
          title: Amended Snapshot
          description: Optional amended snapshot overrides
      type: object
      title: PublishStudyVersionRequest
    PublishStudyVersionResponse:
      properties:
        status:
          type: string
          title: Status
        version_id:
          type: string
          title: Version Id
        amendment_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Amendment Id
        summary_of_changes:
          anyOf:
          - type: string
          - type: 'null'
          title: Summary Of Changes
      type: object
      required:
      - status
      - version_id
      title: PublishStudyVersionResponse
    QualityRuleFinding:
      properties:
        rule_id:
          type: string
          title: Rule Id
          description: Unique quality rule ID (e.g. SENTINEL_REQ_01)
        severity:
          type: string
          title: Severity
          description: 'Severity level: ERROR, WARNING, INFO'
        category:
          type: string
          title: Category
          description: 'Category: Structural, Regulatory, Burden, Inconsistency'
        message:
          type: string
          title: Message
          description: Human-readable rule finding message
        target_node_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Target Node Id
          description: Target USDM graph node ID
      type: object
      required:
      - rule_id
      - severity
      - category
      - message
      title: QualityRuleFinding
      description: 'Specific protocol quality rule finding.


        Requirements: PRD-SYS-001'
    ReConsentGatingPlan:
      properties:
        gating_mandated:
          type: boolean
          title: Gating Mandated
          description: Whether in-flight visit progression must be gated
          default: false
        affected_cohort:
          type: string
          title: Affected Cohort
          description: 'Subject population: ACTIVE, SCREENING, ENROLLED, ALL'
          default: ACTIVE
        flagged_subject_count:
          type: integer
          title: Flagged Subject Count
          description: Total active subjects requiring re-consent before visit progression
          default: 0
        flagged_subject_ids:
          items:
            type: string
          type: array
          title: Flagged Subject Ids
          description: Explicit list of subject IDs requiring re-consent
        justification:
          type: string
          title: Justification
          description: Clinical rationale for re-consent requirement
          default: ''
      type: object
      title: ReConsentGatingPlan
      description: In-flight subject re-consent gating determination and cohort impact.
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
    RegulatoryComplianceImpact:
      properties:
        safety_risk_level:
          type: string
          title: Safety Risk Level
          description: 'Safety risk classification: LOW, MEDIUM, HIGH, CRITICAL'
          default: LOW
        requires_reconsent:
          type: boolean
          title: Requires Reconsent
          description: Whether subject re-consent is mandated (PRD-SUB-007)
          default: false
        is_substantial_amendment:
          type: boolean
          title: Is Substantial Amendment
          description: Whether amendment is substantial vs administrative
          default: false
        icf_version_upgrade:
          type: string
          title: Icf Version Upgrade
          description: Target Informed Consent Form (ICF) version tag
          default: ''
        irb_iec_submission_type:
          type: string
          title: Irb Iec Submission Type
          description: 'Ethics submission pathway: EXPEDITED, FULL_COMMITTEE, NOTIFICATION'
          default: NOTIFICATION
        affected_subject_cohorts:
          items:
            type: string
          type: array
          title: Affected Subject Cohorts
          description: Cohorts subject to regulatory gating
        flagged_active_subjects:
          items:
            type: string
          type: array
          title: Flagged Active Subjects
          description: IDs of in-flight active subjects requiring re-consent
        action_items:
          items:
            type: string
          type: array
          title: Action Items
          description: Step-by-step action plan for Regulatory & QA
      type: object
      title: RegulatoryComplianceImpact
      description: Manifest of impact on safety, ethics, and regulatory compliance.
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
          - type: 'null'
          title: Change Reason
      type: object
      required:
      - block_ids
      title: ReorderBlocksRequest
    ResumeDAGJobRequest:
      properties:
        from_stage:
          anyOf:
          - $ref: '#/components/schemas/DigitizationStage'
          - type: 'null'
          description: Optional stage to restart from. If omitted, resumes from the failed/next stage.
        change_reason:
          anyOf:
          - type: string
          - type: 'null'
          title: Change Reason
          description: Optional reason for resumption
      type: object
      title: ResumeDAGJobRequest
      description: Request payload to resume a paused or failed DAG job from a specific stage.
    RippleAnalysisRequest:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Target study ID
        base_version_tag:
          type: string
          title: Base Version Tag
          description: Base protocol version
          default: 1.0.0
        amended_version_tag:
          type: string
          title: Amended Version Tag
          description: Amended protocol version
          default: 2.0.0
        amendment_type:
          type: string
          title: Amendment Type
          description: '''major'' or ''minor'''
          default: minor
        requires_reconsent:
          anyOf:
          - type: boolean
          - type: 'null'
          title: Requires Reconsent
          description: Explicit override for re-consent requirement
        base_payload:
          anyOf:
          - additionalProperties: true
            type: object
          - type: 'null'
          title: Base Payload
          description: Explicit base protocol payload dictionary
        draft_payload:
          anyOf:
          - additionalProperties: true
            type: object
          - type: 'null'
          title: Draft Payload
          description: Explicit amended protocol payload dictionary
        active_subject_ids:
          anyOf:
          - items:
              type: string
            type: array
          - type: 'null'
          title: Active Subject Ids
          description: Optional explicit active cohort subject IDs to evaluate for gating
      type: object
      required:
      - study_id
      title: RippleAnalysisRequest
      description: Payload to request ripple effect impact analysis between protocol versions.
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
    SchemaRevisionSummary:
      properties:
        arms:
          additionalProperties:
            type: integer
          type: object
          title: Arms
        epochs:
          additionalProperties:
            type: integer
          type: object
          title: Epochs
        encounters:
          additionalProperties:
            type: integer
          type: object
          title: Encounters
        activities:
          additionalProperties:
            type: integer
          type: object
          title: Activities
        eligibility_criteria:
          additionalProperties:
            type: integer
          type: object
          title: Eligibility Criteria
        forms:
          additionalProperties:
            type: integer
          type: object
          title: Forms
      type: object
      title: SchemaRevisionSummary
      description: 'Breakdown of schema revisions across USDM domain layers.


        Requirements: PRD-SYS-001'
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
          $ref: '#/components/schemas/SectionReviewStatus'
          description: Source review status.
        to_status:
          $ref: '#/components/schemas/SectionReviewStatus'
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
          $ref: '#/components/schemas/SectionReviewStatus'
        reason_for_change:
          type: string
          title: Reason For Change
        username:
          anyOf:
          - type: string
          - type: 'null'
          title: Username
        password:
          anyOf:
          - type: string
          - type: 'null'
          title: Password
        signing_reason:
          anyOf:
          - $ref: '#/components/schemas/SigningReason'
          - type: 'null'
      type: object
      required:
      - to_status
      - reason_for_change
      title: SectionTransitionRequest
    SemanticDiffRequest:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Protocol study identifier
        base_version_tag:
          type: string
          title: Base Version Tag
          description: Base version tag
        amended_version_tag:
          type: string
          title: Amended Version Tag
          description: Amended target version tag
        base_payload:
          anyOf:
          - additionalProperties: true
            type: object
          - type: 'null'
          title: Base Payload
          description: Optional explicit base protocol payload
        draft_payload:
          anyOf:
          - additionalProperties: true
            type: object
          - type: 'null'
          title: Draft Payload
          description: Optional explicit amended protocol payload
      type: object
      required:
      - study_id
      - base_version_tag
      - amended_version_tag
      title: SemanticDiffRequest
      description: 'Request to compute semantic multi-layer amendment diff.


        Requirements: PRD-SYS-001'
    SemanticDiffResponse:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Protocol study identifier
        base_version_tag:
          type: string
          title: Base Version Tag
          description: Baseline version tag
        amended_version_tag:
          type: string
          title: Amended Version Tag
          description: Amended version tag
        usdm_graph_diffs:
          items:
            $ref: '#/components/schemas/EntityDiff'
          type: array
          title: Usdm Graph Diffs
          description: USDM Graph node diffs
        soa_matrix_diffs:
          items:
            $ref: '#/components/schemas/EntityDiff'
          type: array
          title: Soa Matrix Diffs
          description: Schedule of Activities matrix diffs
        eligibility_diffs:
          items:
            $ref: '#/components/schemas/EntityDiff'
          type: array
          title: Eligibility Diffs
          description: Eligibility criteria diffs
        ecrf_form_diffs:
          items:
            $ref: '#/components/schemas/EntityDiff'
          type: array
          title: Ecrf Form Diffs
          description: eCRF form and field diffs
        impact_summary:
          $ref: '#/components/schemas/AmendmentImpactSummary'
          description: Calculated amendment impact summary
        migration_directives:
          items:
            $ref: '#/components/schemas/MigrationDirective'
          type: array
          title: Migration Directives
          description: Automated in-flight migration directives
      type: object
      required:
      - study_id
      - base_version_tag
      - amended_version_tag
      - impact_summary
      title: SemanticDiffResponse
      description: 'Multi-layer visual and semantic protocol diff response.


        Requirements: PRD-SYS-001, PRD-SUB-007'
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
      - ADJUDICATION_SIGN_OFF
      - ENDPOINT_ADJUDICATION
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
          - type: 'null'
          title: Details
          description: Optional timing windows, constraints, or instruction notes.
        arm_id:
          anyOf:
          - type: string
          - type: 'null'
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
          - type: 'null'
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
          - type: 'null'
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
            $ref: '#/components/schemas/SoAHeaderEpoch'
          type: array
          title: Epochs
          description: Ordered list of Study Epoch columns.
        encounters:
          items:
            $ref: '#/components/schemas/SoAHeaderEncounter'
          type: array
          title: Encounters
          description: Ordered list of Encounter/Visit sub-columns.
        rows:
          items:
            $ref: '#/components/schemas/SoARowView'
          type: array
          title: Rows
          description: Ordered list of row-wise activity procedures.
        arms:
          items:
            $ref: '#/components/schemas/SoAHeaderArm'
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
            $ref: '#/components/schemas/SoACellView'
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
    StageCheckpoint:
      properties:
        stage:
          $ref: '#/components/schemas/DigitizationStage'
          description: DAG pipeline stage name
        status:
          $ref: '#/components/schemas/StageGateStatus'
          description: Stage execution outcome
        started_at:
          type: string
          format: date-time
          title: Started At
          description: Stage start UTC timestamp
        completed_at:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Completed At
          description: Stage completion UTC timestamp
        duration_ms:
          type: number
          title: Duration Ms
          description: Elapsed execution duration in milliseconds
          default: 0.0
        gate_status:
          $ref: '#/components/schemas/StageGateStatus'
          description: Schema validation gate status
          default: PASSED
        gate_errors:
          items:
            type: string
          type: array
          title: Gate Errors
          description: Diagnostic schema validation error messages
        confidence_score:
          type: number
          maximum: 1.0
          minimum: 0.0
          title: Confidence Score
          description: Calculated confidence score
          default: 1.0
        data:
          additionalProperties: true
          type: object
          title: Data
          description: Checkpoint data payload dictionary
      type: object
      required:
      - stage
      - status
      - started_at
      title: StageCheckpoint
      description: Immutable checkpoint record for a single stage within the DAG execution.
    StageGateStatus:
      type: string
      enum:
      - PASSED
      - FAILED
      - SKIPPED
      title: StageGateStatus
      description: Validation gate outcome at a stage transition boundary.
    StartDAGJobResponse:
      properties:
        job_id:
          type: string
          title: Job Id
          description: Assigned DAG job ID
        status:
          $ref: '#/components/schemas/DigitizationJobStatus'
          description: Initial job status
        current_stage:
          anyOf:
          - $ref: '#/components/schemas/DigitizationStage'
          - type: 'null'
          description: Initial stage
        message:
          type: string
          title: Message
          description: Human-readable initialization message
      type: object
      required:
      - job_id
      - status
      - message
      title: StartDAGJobResponse
      description: Response returned upon initiating an asynchronous protocol digitization DAG job.
    StudyAlignmentReport:
      properties:
        study_id:
          type: string
          title: Study Id
        complete_activities:
          items:
            $ref: '#/components/schemas/ActivityReport'
          type: array
          title: Complete Activities
        incomplete_activities:
          items:
            $ref: '#/components/schemas/ActivityReport'
          type: array
          title: Incomplete Activities
        unmapped_activities:
          items:
            $ref: '#/components/schemas/ActivityReport'
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
          - type: 'null'
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
            $ref: '#/components/schemas/ConceptValidationReport'
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
    SubjectManagementRtsmImpact:
      properties:
        cohort_adjustments_count:
          type: integer
          title: Cohort Adjustments Count
          description: Number of dosing cohorts or arms adjusted
          default: 0
        affected_arms:
          items:
            type: string
          type: array
          title: Affected Arms
          description: Names of affected study arms
        dosing_changes:
          items:
            additionalProperties: true
            type: object
          type: array
          title: Dosing Changes
          description: Specific dosing regimen modifications
        visit_window_adjustments:
          items:
            additionalProperties: true
            type: object
          type: array
          title: Visit Window Adjustments
          description: Visit calculation window (+/- days) changes
        requires_kit_reallocation:
          type: boolean
          title: Requires Kit Reallocation
          description: Whether investigational product kit packaging or supply must be reallocated
          default: false
        randomization_ratio_changed:
          type: boolean
          title: Randomization Ratio Changed
          description: Whether randomization stratifications or ratios changed
          default: false
        action_items:
          items:
            type: string
          type: array
          title: Action Items
          description: Step-by-step action plan for RTSM & Supply
      type: object
      title: SubjectManagementRtsmImpact
      description: Manifest of impact on randomization, trial supply management (RTSM), and cohort dosing.
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
          $ref: '#/components/schemas/SuggestionStatus'
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
          - type: 'null'
          title: Decision Reason
          description: Rationale for acceptance or rejection.
        decided_by:
          anyOf:
          - type: string
          - type: 'null'
          title: Decided By
          description: User ID of decider.
        decided_at:
          anyOf:
          - type: string
          - type: 'null'
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
          description: 'Target export format: ''pdf'', ''docx'', or ''html'''
          default: pdf
        creator:
          anyOf:
          - type: string
          - type: 'null'
          title: Creator
          description: Author or creator username
          default: Cadence Clinical DDF Engine
        change_reason:
          anyOf:
          - type: string
          - type: 'null'
          title: Change Reason
          description: GxP 21 CFR Part 11 change reason
          default: Initial Baseline
      type: object
      required:
      - study_id
      title: SynopsisExportRequest
      description: 'Request payload for exporting a clinical protocol synopsis.


        Requirements: PRD-SYS-001'
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
      description: 'Response payload containing base64 encoded document export stream.


        Requirements: PRD-SYS-001'
    SynthesizedECRFForm:
      properties:
        form_id:
          type: string
          title: Form Id
          description: Unique form identifier
        form_name:
          type: string
          title: Form Name
          description: Human-readable form title
        cdash_domain:
          type: string
          title: Cdash Domain
          description: CDASH domain code
        items:
          items:
            additionalProperties: true
            type: object
          type: array
          title: Items
          description: eCRF form item definitions
        rules:
          items:
            additionalProperties: true
            type: object
          type: array
          title: Rules
          description: Compiled validation and skip rules
      type: object
      required:
      - form_id
      - form_name
      - cdash_domain
      title: SynthesizedECRFForm
      description: Synthesized CDASH eCRF form definition compiled from extracted activities.
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
          $ref: '#/components/schemas/CodeValidationState'
        results:
          items:
            $ref: '#/components/schemas/TerminologyConcept'
          type: array
          title: Results
        total_results:
          type: integer
          title: Total Results
        error_message:
          anyOf:
          - type: string
          - type: 'null'
          title: Error Message
      type: object
      required:
      - query
      - state
      - results
      - total_results
      title: TerminologySearchResponse
      description: Response model for search and autocomplete queries.
    TicketDispatchRequest:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Target study ID
        impact_assessment:
          anyOf:
          - $ref: '#/components/schemas/ProtocolImpactAssessment'
          - type: 'null'
          description: Optional pre-calculated impact assessment
        base_version_tag:
          anyOf:
          - type: string
          - type: 'null'
          title: Base Version Tag
          description: Base protocol version
        amended_version_tag:
          anyOf:
          - type: string
          - type: 'null'
          title: Amended Version Tag
          description: Amended protocol version
        base_payload:
          anyOf:
          - additionalProperties: true
            type: object
          - type: 'null'
          title: Base Payload
          description: Base payload
        draft_payload:
          anyOf:
          - additionalProperties: true
            type: object
          - type: 'null'
          title: Draft Payload
          description: Amended payload
        selected_domain_queues:
          anyOf:
          - items:
              $ref: '#/components/schemas/DomainQueue'
            type: array
          - type: 'null'
          title: Selected Domain Queues
          description: Optional subset of domain queues to dispatch
      type: object
      required:
      - study_id
      title: TicketDispatchRequest
      description: Payload to dispatch operational tickets for an amendment.
    TicketDispatchResponse:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Target study ID
        assessment_id:
          type: string
          title: Assessment Id
          description: Associated assessment ID
        total_dispatched:
          type: integer
          title: Total Dispatched
          description: Total tickets created
          default: 0
        dispatched_tickets:
          items:
            $ref: '#/components/schemas/DispatchedTicketInfo'
          type: array
          title: Dispatched Tickets
          description: List of created ticket details
        message:
          type: string
          title: Message
          description: Confirmation message
      type: object
      required:
      - study_id
      - assessment_id
      - message
      title: TicketDispatchResponse
      description: Response after dispatching operational tickets to apps/tickets.
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
          - type: 'null'
          title: Anchor Reference
          description: Anchor reference, e.g. a visit name.
        target_day:
          anyOf:
          - type: integer
          - type: 'null'
          title: Target Day
          description: Target scheduled day.
        min_offset:
          anyOf:
          - type: integer
          - type: 'null'
          title: Min Offset
          description: Minimum day offset.
        max_offset:
          anyOf:
          - type: integer
          - type: 'null'
          title: Max Offset
          description: Maximum day offset.
        conditional:
          anyOf:
          - type: boolean
          - type: 'null'
          title: Conditional
          description: Flag indicating if the timing or applicability is conditional.
        reason:
          anyOf:
          - type: string
            minLength: 1
          - type: 'null'
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
          - type: 'null'
          title: Name
        label:
          anyOf:
          - type: string
          - type: 'null'
          title: Label
        value:
          anyOf:
          - type: string
          - type: 'null'
          title: Value
      type: object
      required:
      - status
      - reason
      title: TransitionItemRequest
    USDMProtocolExtractionResponse:
      properties:
        study_title:
          type: string
          title: Study Title
          description: Clinical study title
        protocol_id:
          type: string
          title: Protocol Id
          description: Protocol identification code
        phase:
          type: string
          enum:
          - PHASE_I
          - PHASE_I_II
          - PHASE_II
          - PHASE_III
          - PHASE_IV
          title: Phase
          description: Clinical development phase
        therapeutic_area:
          type: string
          title: Therapeutic Area
          description: Therapeutic area e.g., 'Oncology', 'Cardiology'
        arms:
          items:
            $ref: '#/components/schemas/ExtractedArm'
          type: array
          title: Arms
          description: Extracted study arms
        epochs:
          items:
            $ref: '#/components/schemas/ExtractedEpoch'
          type: array
          title: Epochs
          description: Extracted trial epochs
        visits:
          items:
            $ref: '#/components/schemas/ExtractedVisit'
          type: array
          title: Visits
          description: Extracted encounters / visits
        activities:
          items:
            $ref: '#/components/schemas/ExtractedActivity'
          type: array
          title: Activities
          description: Extracted Schedule of Activities procedures
        criteria:
          items:
            $ref: '#/components/schemas/ExtractedCriterion'
          type: array
          title: Criteria
          description: Extracted inclusion/exclusion criteria
        confidence_score:
          type: number
          maximum: 1.0
          minimum: 0.0
          title: Confidence Score
          description: Overall LLM extraction confidence
      type: object
      required:
      - study_title
      - protocol_id
      - phase
      - therapeutic_area
      - confidence_score
      title: USDMProtocolExtractionResponse
      description: Structured response representing complete USDM v4.0 protocol parameters.
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
          $ref: '#/components/schemas/ArmPayload'
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
          - type: 'null'
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
          - $ref: '#/components/schemas/CDASHMapping'
          - type: 'null'
        allowable_units:
          anyOf:
          - items:
              $ref: '#/components/schemas/AllowableUnit'
            type: array
          - type: 'null'
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
          $ref: '#/components/schemas/DataElementPayload'
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
          $ref: '#/components/schemas/EpochProperties'
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
          $ref: '#/components/schemas/FormPayload'
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
          $ref: '#/components/schemas/ProcedureProperties'
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
          $ref: '#/components/schemas/StudyArmProperties'
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
          $ref: '#/components/schemas/TimingWindowProperties'
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
            $ref: '#/components/schemas/DifferenceResult'
          type: array
          title: Added Nodes
        modified_nodes:
          items:
            $ref: '#/components/schemas/DifferenceResult'
          type: array
          title: Modified Nodes
        deleted_nodes:
          items:
            $ref: '#/components/schemas/DifferenceResult'
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
          $ref: '#/components/schemas/LibraryStatus'
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
          - type: 'null'
          title: Updated At
          description: Audit timestamp of last update.
        updated_by:
          anyOf:
          - type: string
          - type: 'null'
          title: Updated By
          description: User ID of last updater.
        reason_for_change:
          anyOf:
          - type: string
          - type: 'null'
          title: Reason For Change
          description: Detailed explanation of changes applied.
        prior_status:
          anyOf:
          - type: string
          - type: 'null'
          title: Prior Status
          description: Previous status before transition.
        object_type:
          type: string
          const: VISIT
          title: Object Type
          default: VISIT
        payload:
          $ref: '#/components/schemas/VisitPayload'
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
          $ref: '#/components/schemas/VisitAttributes'
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
          - type: 'null'
          title: Name
          description: The display name of the visit.
        encounter_name:
          anyOf:
          - type: string
            minLength: 1
          - type: 'null'
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
            $ref: '#/components/schemas/VisitReorderItem'
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
          $ref: '#/components/schemas/VisitProperties'
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
          $ref: '#/components/schemas/VisitProperties'
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
          $ref: '#/components/schemas/LibraryStatus'
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
          $ref: '#/components/schemas/VisitPayload'
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
          $ref: '#/components/schemas/VisitPayload'
      type: object
      required:
      - reason_for_change
      - payload
      title: UpdateVisitRequest
      description: Request model for updating a Visit library object.
    AdaptiveTrialConfigDTO:
      properties:
        study_id:
          type: string
          minLength: 1
          title: Study Id
        design_type:
          type: string
          minLength: 1
          title: Design Type
          description: 'Design type: ''THREE_PLUS_THREE'', ''BOIN'', ''CRM'', ''RAR'''
        dose_levels:
          items:
            $ref: '#/components/schemas/DoseLevelDef'
          type: array
          title: Dose Levels
        boin_config:
          anyOf:
          - $ref: '#/components/schemas/BoinConfig'
          - type: 'null'
        crm_config:
          anyOf:
          - $ref: '#/components/schemas/CrmConfig'
          - type: 'null'
        three_plus_three_config:
          anyOf:
          - $ref: '#/components/schemas/ThreePlusThreeConfig'
          - type: 'null'
        rar_config:
          anyOf:
          - $ref: '#/components/schemas/RarConfig'
          - type: 'null'
        cohort_size:
          type: integer
          minimum: 1.0
          title: Cohort Size
          default: 3
        max_sample_size:
          anyOf:
          - type: integer
            minimum: 1.0
          - type: 'null'
          title: Max Sample Size
        is_active:
          type: boolean
          title: Is Active
          default: true
      type: object
      required:
      - study_id
      - design_type
      title: AdaptiveTrialConfigDTO
      description: Comprehensive DTO for adaptive trial configuration.
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
          $ref: '#/components/schemas/DOATaskRoleEnum'
          description: Site role
        delegated_tasks:
          items:
            $ref: '#/components/schemas/DOATaskDelegationEnum'
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
      description: 'Request payload to add site personnel task delegation record.


        Requirements: PRD-SYS-001'
    AdjudicationActionEnum:
      type: string
      enum:
      - APPROVE
      - REJECT
      title: AdjudicationActionEnum
      description: Permitted adjudication actions.
    AdjudicationClassification:
      type: string
      enum:
      - CONFIRMED_MI
      - CONFIRMED_ISCHEMIC_STROKE
      - CONFIRMED_HEMORRHAGIC_STROKE
      - CONFIRMED_CV_DEATH
      - CONFIRMED_NON_CV_DEATH
      - CONFIRMED_HEART_FAILURE_HOSPITALIZATION
      - CONFIRMED_STENT_THROMBOSIS
      - CONFIRMED_VTE
      - CONFIRMED_DISEASE_PROGRESSION
      - NOT_AN_ENDPOINT
      - INDETERMINATE
      - INSUFFICIENT_DATA
      title: AdjudicationClassification
      description: Standardized adjudicated event classification determinations.
    AdjudicationRequest:
      properties:
        action:
          $ref: '#/components/schemas/AdjudicationActionEnum'
          description: 'Adjudication decision: APPROVE (promote to OPEN) or REJECT (dismiss/CANCELLED).'
        reason:
          type: string
          minLength: 5
          title: Reason
          description: Mandatory 21 CFR Part 11 justification or reason for change.
        updated_message:
          anyOf:
          - type: string
          - type: 'null'
          title: Updated Message
          description: Optional edited query text before opening to site staff.
      type: object
      required:
      - action
      - reason
      title: AdjudicationRequest
      description: Payload for Data Manager adjudication of a staged candidate query.
    AdjudicationResponse:
      properties:
        query_id:
          type: string
          title: Query Id
        new_status:
          type: string
          title: New Status
        action:
          type: string
          title: Action
        message:
          type: string
          title: Message
        adjudicated_by:
          type: string
          title: Adjudicated By
        adjudicated_at:
          type: string
          format: date-time
          title: Adjudicated At
      type: object
      required:
      - query_id
      - new_status
      - action
      - message
      - adjudicated_by
      - adjudicated_at
      title: AdjudicationResponse
      description: Outcome of Data Manager candidate query adjudication.
    AdjudicationWorkflowStatus:
      type: string
      enum:
      - PENDING_PRIMARY_REVIEWS
      - READER_1_COMPLETED
      - READER_2_COMPLETED
      - CONSENSUS_REACHED
      - DISCORDANCE_DETECTED
      - ESCALATED_TO_CHAIR
      - FINALIZED_CONCORDANT
      - FINALIZED_BY_CHAIR
      - FINALIZED_BY_COMMITTEE
      title: AdjudicationWorkflowStatus
      description: Lifecycle state machine for 2+1 blinded reader review.
    AnomalyCandidateItem:
      properties:
        query_id:
          type: string
          title: Query Id
        study_id:
          type: string
          title: Study Id
        subject_id:
          type: string
          title: Subject Id
        site_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Site Id
        visit_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Visit Id
        domain:
          anyOf:
          - type: string
          - type: 'null'
          title: Domain
        test_code:
          type: string
          title: Test Code
        observation_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Observation Id
        field_link:
          anyOf:
          - type: string
          - type: 'null'
          title: Field Link
        rule_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Rule Id
        message:
          anyOf:
          - type: string
          - type: 'null'
          title: Message
        explanation:
          anyOf:
          - type: string
          - type: 'null'
          title: Explanation
        origin:
          anyOf:
          - type: string
          - type: 'null'
          title: Origin
        priority:
          anyOf:
          - type: string
          - type: 'null'
          title: Priority
        status:
          type: string
          title: Status
          default: CANDIDATE
        created_at:
          type: string
          format: date-time
          title: Created At
        created_by:
          anyOf:
          - type: string
          - type: 'null'
          title: Created By
      type: object
      required:
      - query_id
      - study_id
      - subject_id
      - test_code
      - created_at
      title: AnomalyCandidateItem
      description: Summary of a staged CANDIDATE query generated by the anomaly detector.
    AnomalyEvaluateRequest:
      properties:
        subject_id:
          type: string
          title: Subject Id
          description: Subject identifier to evaluate.
        study_id:
          type: string
          title: Study Id
          description: Study identifier.
        enable_ai:
          type: boolean
          title: Enable Ai
          description: Whether to run AI Gateway semantic reasoning if available.
          default: true
        auto_stage_queries:
          type: boolean
          title: Auto Stage Queries
          description: Whether to automatically stage detected anomalies as CANDIDATE queries.
          default: true
      type: object
      required:
      - subject_id
      - study_id
      title: AnomalyEvaluateRequest
      description: Request payload to trigger on-demand anomaly evaluation.
    AnomalyEvaluateResponse:
      properties:
        subject_id:
          type: string
          title: Subject Id
        study_id:
          type: string
          title: Study Id
        anomalies:
          items:
            $ref: '#/components/schemas/CrossDomainAnomaly'
          type: array
          title: Anomalies
        evaluated_at:
          type: string
          format: date-time
          title: Evaluated At
        queries_staged_count:
          type: integer
          title: Queries Staged Count
      type: object
      required:
      - subject_id
      - study_id
      - anomalies
      - evaluated_at
      - queries_staged_count
      title: AnomalyEvaluateResponse
      description: Response payload returning evaluation results.
    AnomalySeverity:
      type: string
      enum:
      - HIGH
      - MEDIUM
      - LOW
      title: AnomalySeverity
      description: Priority and severity level of a detected cross-domain anomaly.
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
          - type: 'null'
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
    BatchAssignItem:
      properties:
        assignment_id:
          type: string
          title: Assignment Id
        action:
          type: string
          title: Action
          default: ACCEPT
        code:
          anyOf:
          - type: string
          - type: 'null'
          title: Code
        term:
          anyOf:
          - type: string
          - type: 'null'
          title: Term
        suggestion_index:
          anyOf:
          - type: integer
          - type: 'null'
          title: Suggestion Index
        reason_for_change:
          anyOf:
          - type: string
          - type: 'null'
          title: Reason For Change
      type: object
      required:
      - assignment_id
      title: BatchAssignItem
    BatchAssignRequest:
      properties:
        assignment_ids:
          anyOf:
          - items:
              type: string
            type: array
          - type: 'null'
          title: Assignment Ids
        items:
          anyOf:
          - items:
              $ref: '#/components/schemas/BatchAssignItem'
            type: array
          - type: 'null'
          title: Items
        code:
          anyOf:
          - type: string
          - type: 'null'
          title: Code
        term:
          anyOf:
          - type: string
          - type: 'null'
          title: Term
        dictionary_type:
          anyOf:
          - type: string
          - type: 'null'
          title: Dictionary Type
        dictionary_version:
          anyOf:
          - type: string
          - type: 'null'
          title: Dictionary Version
        reason:
          anyOf:
          - type: string
          - type: 'null'
          title: Reason
        reason_for_change:
          anyOf:
          - type: string
          - type: 'null'
          title: Reason For Change
        action:
          type: string
          title: Action
          default: ACCEPT
      type: object
      title: BatchAssignRequest
    BatchAssignResponse:
      properties:
        success_count:
          type: integer
          title: Success Count
        failed_count:
          type: integer
          title: Failed Count
        results:
          items:
            additionalProperties: true
            type: object
          type: array
          title: Results
          default: []
      type: object
      required:
      - success_count
      - failed_count
      title: BatchAssignResponse
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
          description: 'Target artifact type: FORM, CASEBOOK, DOC'
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
      description: 'Request payload for Principal Investigator batch eSignature casebook sign-off.


        Requirements: PRD-SYS-001'
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
      description: 'Response payload following successful batch eSignature execution.


        Requirements: PRD-SYS-001'
    BindingDeterminationSource:
      type: string
      enum:
      - CONCORDANT_CONSENSUS
      - CHAIR_UPHELD_READER_1
      - CHAIR_UPHELD_READER_2
      - CHAIR_CUSTOM_DETERMINATION
      - COMMITTEE_QUORUM_VOTE
      title: BindingDeterminationSource
      description: Source of the binding adjudication determination.
    Body_import_dictionary_api_v1_dictionaries_import_post:
      properties:
        dictionary_type:
          $ref: '#/components/schemas/DictTypeEnum'
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
    BoinBoundariesCalculationRequest:
      properties:
        target_toxicity_rate:
          type: number
          exclusiveMaximum: 1.0
          exclusiveMinimum: 0.0
          title: Target Toxicity Rate
          default: 0.25
        factor_e:
          type: number
          exclusiveMaximum: 1.0
          exclusiveMinimum: 0.0
          title: Factor E
          default: 0.6
        factor_d:
          type: number
          exclusiveMinimum: 1.0
          title: Factor D
          default: 1.4
      type: object
      title: BoinBoundariesCalculationRequest
      description: Direct calculation request for theoretical BOIN boundaries.
    BoinBoundariesCalculationResponse:
      properties:
        target_toxicity_rate:
          type: number
          title: Target Toxicity Rate
        phi_1:
          type: number
          title: Phi 1
        phi_2:
          type: number
          title: Phi 2
        lambda_e:
          type: number
          title: Lambda E
        lambda_d:
          type: number
          title: Lambda D
        description:
          type: string
          title: Description
      type: object
      required:
      - target_toxicity_rate
      - phi_1
      - phi_2
      - lambda_e
      - lambda_d
      - description
      title: BoinBoundariesCalculationResponse
      description: Response containing exact theoretical BOIN boundaries.
    BoinConfig:
      properties:
        target_toxicity_rate:
          type: number
          exclusiveMaximum: 1.0
          exclusiveMinimum: 0.0
          title: Target Toxicity Rate
          description: Target Dose-Limiting Toxicity (DLT) rate phi (e.g. 0.25 or 0.30).
          default: 0.25
        factor_e:
          type: number
          exclusiveMaximum: 1.0
          exclusiveMinimum: 0.0
          title: Factor E
          description: Escalation boundary factor c_e such that phi_1 = c_e * phi (default 0.6).
          default: 0.6
        factor_d:
          type: number
          exclusiveMinimum: 1.0
          title: Factor D
          description: De-escalation boundary factor c_d such that phi_2 = c_d * phi (default 1.4).
          default: 1.4
        safety_cutoff:
          type: number
          exclusiveMaximum: 1.0
          exclusiveMinimum: 0.5
          title: Safety Cutoff
          description: Bayesian overdose stopping probability cutoff P(p_j > phi | data) > cutoff.
          default: 0.95
        prior_alpha:
          type: number
          exclusiveMinimum: 0.0
          title: Prior Alpha
          description: Beta prior shape parameter alpha (default 1.0).
          default: 1.0
        prior_beta:
          type: number
          exclusiveMinimum: 0.0
          title: Prior Beta
          description: Beta prior shape parameter beta (default 1.0).
          default: 1.0
        min_subjects_for_elimination:
          type: integer
          minimum: 1.0
          title: Min Subjects For Elimination
          description: Minimum evaluated subjects at a dose level before overdose elimination can trigger.
          default: 3
      type: object
      title: BoinConfig
      description: Configuration parameters for Bayesian Optimal Interval (BOIN) design.
    BulkQueryGenerationRequest:
      properties:
        study_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Study Id
          description: Target protocol study ID
        site_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Site Id
          description: Optional target site identifier
        subject_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Subject Id
          description: Optional target subject identifier
        targets:
          items:
            $ref: '#/components/schemas/QueryTargetDescriptor'
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
      description: 'Request payload to execute bulk clinical query generation.


        Requirements: PRD-SYS-001'
    BulkQueryGenerationResponse:
      properties:
        batch_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Batch Id
          description: Unique bulk query batch identifier
        audit_tx:
          anyOf:
          - type: string
          - type: 'null'
          title: Audit Tx
          description: Immutable GxP audit ledger transaction ID
        generated_count:
          anyOf:
          - type: integer
          - type: 'null'
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
            $ref: '#/components/schemas/QueryTargetDescriptor'
          type: array
          title: Skipped Targets
          description: List of target descriptors that were skipped due to already having an active query
        timestamp_utc:
          anyOf:
          - type: string
          - type: 'null'
          title: Timestamp Utc
          description: UTC ISO timestamp of query generation execution
      type: object
      required:
      - generated_query_ids
      title: BulkQueryGenerationResponse
      description: 'Response payload following bulk query generation execution.


        Requirements: PRD-SYS-001'
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
          description: 'SDV scope boundary: FIELD, PAGE, or VISIT'
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
          - type: 'null'
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
      description: 'Request payload to execute bulk SDV sign-offs.


        Requirements: PRD-SYS-001'
    BulkSdvSignOffResponse:
      properties:
        bulk_id:
          anyOf:
          - type: string
          - type: 'null'
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
          - type: 'null'
          title: Verified Count
          description: Total number of successfully verified SDV items
        verified_target_ids:
          anyOf:
          - items:
              type: string
            type: array
          - type: 'null'
          title: Verified Target Ids
          description: List of target IDs that were successfully signed
        skipped_targets:
          anyOf:
          - items:
              additionalProperties: true
              type: object
            type: array
          - type: 'null'
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
      description: 'Response payload following bulk SDV sign-off execution.


        Requirements: PRD-SYS-001'
    BulkSubjectReconsentRequest:
      properties:
        subject_ids:
          items:
            type: string
          type: array
          title: Subject Ids
          description: List of subject identifiers
        study_id:
          type: string
          title: Study Id
          description: Study identifier
        protocol_version:
          type: string
          title: Protocol Version
          description: Amended protocol version tag
        version_index:
          type: integer
          title: Version Index
          description: Protocol version index
          default: 2
        icf_signed:
          type: boolean
          title: Icf Signed
          description: Whether ICF is signed
          default: true
        signature_type:
          type: string
          title: Signature Type
          description: 'Signature type: ''ECONSENT'' or ''PAPER_UPLOAD'''
          default: ECONSENT
        signer_name:
          type: string
          title: Signer Name
          description: Full name or user ID of batch signer
          default: site_coordinator_01
        reason_for_change:
          type: string
          title: Reason For Change
          description: GxP reason for change
          default: Bulk protocol amendment re-consent verification
      type: object
      required:
      - subject_ids
      - study_id
      - protocol_version
      title: BulkSubjectReconsentRequest
      description: Request payload to register batch subject re-consent for an amended version.
    BundledEcrfForm:
      properties:
        form_id:
          type: string
          title: Form Id
          description: Originating eCRF form submission ID
        form_name:
          type: string
          title: Form Name
          description: Form name (e.g. Adverse Events, Concomitant Medications, Vitals)
        visit_name:
          type: string
          title: Visit Name
          description: Study encounter / visit label
        submission_timestamp:
          type: string
          title: Submission Timestamp
          description: Shifted timestamp of form entry
        form_data:
          additionalProperties: true
          type: object
          title: Form Data
          description: Scrubbed eCRF field values
      type: object
      required:
      - form_id
      - form_name
      - visit_name
      - submission_timestamp
      title: BundledEcrfForm
      description: Pertinent eCRF form submission included in the event dossier.
    CGTApheresisCreateRequest:
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
        collector_id:
          type: string
          title: Collector Id
        collector_name:
          type: string
          title: Collector Name
        collector_role:
          type: string
          title: Collector Role
          default: Apheresis Nurse
        initial_temp_celsius:
          type: number
          title: Initial Temp Celsius
          default: -196.0
        din:
          anyOf:
          - type: string
          - type: 'null'
          title: Din
        dry_shipper_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Dry Shipper Id
        seal_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Seal Id
        notes:
          anyOf:
          - type: string
          - type: 'null'
          title: Notes
      type: object
      required:
      - study_id
      - site_id
      - subject_id
      - collector_id
      - collector_name
      title: CGTApheresisCreateRequest
      description: Payload to register initial apheresis collection and start Vein-to-Vein tracking.
    CGTBarcodeVerifyRequest:
      properties:
        barcode_payload:
          type: string
          title: Barcode Payload
        expected_subject_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Expected Subject Id
        expected_din:
          anyOf:
          - type: string
          - type: 'null'
          title: Expected Din
        expected_batch_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Expected Batch Id
      type: object
      required:
      - barcode_payload
      title: CGTBarcodeVerifyRequest
      description: Payload to verify cryptographic barcode authenticity.
    CGTBarcodeVerifyResponse:
      properties:
        is_valid:
          type: boolean
          title: Is Valid
        failure_reason:
          anyOf:
          - type: string
          - type: 'null'
          title: Failure Reason
        barcode_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Barcode Id
        study_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Study Id
        site_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Site Id
        subject_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Subject Id
        din:
          anyOf:
          - type: string
          - type: 'null'
          title: Din
        batch_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Batch Id
        crypto_hash:
          anyOf:
          - type: string
          - type: 'null'
          title: Crypto Hash
      type: object
      required:
      - is_valid
      title: CGTBarcodeVerifyResponse
      description: Response schema for barcode verification.
    CGTBedsideVerificationRequest:
      properties:
        coc_id:
          type: string
          title: Coc Id
        scanned_wristband_subject_id:
          type: string
          title: Scanned Wristband Subject Id
        scanned_bag_barcode:
          type: string
          title: Scanned Bag Barcode
        primary_verifier_id:
          type: string
          title: Primary Verifier Id
        primary_verifier_name:
          type: string
          title: Primary Verifier Name
        primary_verifier_role:
          type: string
          title: Primary Verifier Role
        primary_signature_token:
          type: string
          title: Primary Signature Token
        secondary_verifier_id:
          type: string
          title: Secondary Verifier Id
        secondary_verifier_name:
          type: string
          title: Secondary Verifier Name
        secondary_verifier_role:
          type: string
          title: Secondary Verifier Role
        secondary_signature_token:
          type: string
          title: Secondary Signature Token
        notes:
          anyOf:
          - type: string
          - type: 'null'
          title: Notes
      type: object
      required:
      - coc_id
      - scanned_wristband_subject_id
      - scanned_bag_barcode
      - primary_verifier_id
      - primary_verifier_name
      - primary_verifier_role
      - primary_signature_token
      - secondary_verifier_id
      - secondary_verifier_name
      - secondary_verifier_role
      - secondary_signature_token
      title: CGTBedsideVerificationRequest
      description: Payload for mandatory bedside two-person electronic verification gating infusion eCRF.
    CGTBedsideVerificationResponse:
      properties:
        id:
          type: string
          title: Id
        study_id:
          type: string
          title: Study Id
        site_id:
          type: string
          title: Site Id
        chain_of_custody_id:
          type: string
          title: Chain Of Custody Id
        subject_id:
          type: string
          title: Subject Id
        din:
          type: string
          title: Din
        batch_id:
          type: string
          title: Batch Id
        wristband_matched:
          type: boolean
          title: Wristband Matched
        bag_matched:
          type: boolean
          title: Bag Matched
        temperature_chain_verified:
          type: boolean
          title: Temperature Chain Verified
        primary_verifier_id:
          type: string
          title: Primary Verifier Id
        primary_verifier_name:
          type: string
          title: Primary Verifier Name
        primary_verifier_role:
          type: string
          title: Primary Verifier Role
        primary_signed_at:
          type: string
          format: date-time
          title: Primary Signed At
        secondary_verifier_id:
          type: string
          title: Secondary Verifier Id
        secondary_verifier_name:
          type: string
          title: Secondary Verifier Name
        secondary_verifier_role:
          type: string
          title: Secondary Verifier Role
        secondary_signed_at:
          type: string
          format: date-time
          title: Secondary Signed At
        status:
          type: string
          title: Status
        unlock_token:
          type: string
          title: Unlock Token
        verification_notes:
          anyOf:
          - type: string
          - type: 'null'
          title: Verification Notes
      type: object
      required:
      - id
      - study_id
      - site_id
      - chain_of_custody_id
      - subject_id
      - din
      - batch_id
      - wristband_matched
      - bag_matched
      - temperature_chain_verified
      - primary_verifier_id
      - primary_verifier_name
      - primary_verifier_role
      - primary_signed_at
      - secondary_verifier_id
      - secondary_verifier_name
      - secondary_verifier_role
      - secondary_signed_at
      - status
      - unlock_token
      title: CGTBedsideVerificationResponse
      description: Response schema for bedside two-person verification.
    CGTChainOfCustodyResponse:
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
        din:
          type: string
          title: Din
        batch_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Batch Id
        current_stage:
          type: string
          title: Current Stage
        status:
          type: string
          title: Status
        tamper_evident_barcode:
          anyOf:
          - type: string
          - type: 'null'
          title: Tamper Evident Barcode
        barcode_signature:
          anyOf:
          - type: string
          - type: 'null'
          title: Barcode Signature
        collection_site_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Collection Site Id
        collection_timestamp:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Collection Timestamp
        manufacturing_facility_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Manufacturing Facility Id
        cryo_temp_excursion_count:
          type: integer
          title: Cryo Temp Excursion Count
          default: 0
        lowest_temp_celsius:
          anyOf:
          - type: number
          - type: 'null'
          title: Lowest Temp Celsius
        highest_temp_celsius:
          anyOf:
          - type: number
          - type: 'null'
          title: Highest Temp Celsius
        infusion_unlocked:
          type: boolean
          title: Infusion Unlocked
          default: false
        infused_at:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Infused At
        reconciled_at:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Reconciled At
      type: object
      required:
      - id
      - study_id
      - subject_id
      - din
      - current_stage
      - status
      title: CGTChainOfCustodyResponse
      description: Response schema for Vein-to-Vein Chain of Custody & Identity record.
    CGTDestructionCertificateCreateRequest:
      properties:
        study_id:
          type: string
          title: Study Id
        site_id:
          type: string
          title: Site Id
        lot_number:
          type: string
          title: Lot Number
        kit_numbers:
          items:
            type: string
          type: array
          minItems: 1
          title: Kit Numbers
        vial_identifiers:
          items:
            type: string
          type: array
          minItems: 1
          title: Vial Identifiers
        destruction_reason:
          type: string
          title: Destruction Reason
        destruction_method:
          type: string
          title: Destruction Method
          default: HIGH_TEMPERATURE_INCINERATION
        destruction_facility:
          type: string
          title: Destruction Facility
        witness_1_id:
          type: string
          title: Witness 1 Id
        witness_1_name:
          type: string
          title: Witness 1 Name
        witness_1_role:
          type: string
          title: Witness 1 Role
        witness_1_signature_token:
          type: string
          title: Witness 1 Signature Token
        witness_2_id:
          type: string
          title: Witness 2 Id
        witness_2_name:
          type: string
          title: Witness 2 Name
        witness_2_role:
          type: string
          title: Witness 2 Role
        witness_2_signature_token:
          type: string
          title: Witness 2 Signature Token
        quantity_destroyed:
          type: integer
          title: Quantity Destroyed
          default: 1
        residual_volume_ml:
          anyOf:
          - type: number
          - type: 'null'
          title: Residual Volume Ml
        subject_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Subject Id
        din:
          anyOf:
          - type: string
          - type: 'null'
          title: Din
        coc_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Coc Id
        notes:
          anyOf:
          - type: string
          - type: 'null'
          title: Notes
      type: object
      required:
      - study_id
      - site_id
      - lot_number
      - kit_numbers
      - vial_identifiers
      - destruction_reason
      - destruction_facility
      - witness_1_id
      - witness_1_name
      - witness_1_role
      - witness_1_signature_token
      - witness_2_id
      - witness_2_name
      - witness_2_role
      - witness_2_signature_token
      title: CGTDestructionCertificateCreateRequest
      description: Payload to generate formal Certificate of Destruction with dual witness e-signatures.
    CGTDestructionCertificateResponse:
      properties:
        id:
          type: string
          title: Id
        study_id:
          type: string
          title: Study Id
        site_id:
          type: string
          title: Site Id
        certificate_number:
          type: string
          title: Certificate Number
        chain_of_custody_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Chain Of Custody Id
        subject_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Subject Id
        din:
          anyOf:
          - type: string
          - type: 'null'
          title: Din
        lot_number:
          type: string
          title: Lot Number
        kit_numbers:
          items:
            type: string
          type: array
          title: Kit Numbers
        vial_identifiers:
          items:
            type: string
          type: array
          title: Vial Identifiers
        quantity_destroyed:
          type: integer
          title: Quantity Destroyed
        unit_of_measure:
          type: string
          title: Unit Of Measure
        residual_volume_ml:
          anyOf:
          - type: number
          - type: 'null'
          title: Residual Volume Ml
        destruction_reason:
          type: string
          title: Destruction Reason
        destruction_method:
          type: string
          title: Destruction Method
        destruction_facility:
          type: string
          title: Destruction Facility
        destruction_timestamp:
          type: string
          format: date-time
          title: Destruction Timestamp
        witness_1_id:
          type: string
          title: Witness 1 Id
        witness_1_name:
          type: string
          title: Witness 1 Name
        witness_1_role:
          type: string
          title: Witness 1 Role
        witness_1_signed_at:
          type: string
          format: date-time
          title: Witness 1 Signed At
        witness_2_id:
          type: string
          title: Witness 2 Id
        witness_2_name:
          type: string
          title: Witness 2 Name
        witness_2_role:
          type: string
          title: Witness 2 Role
        witness_2_signed_at:
          type: string
          format: date-time
          title: Witness 2 Signed At
        cryptographic_seal:
          type: string
          title: Cryptographic Seal
        status:
          type: string
          title: Status
        reconciliation_notes:
          anyOf:
          - type: string
          - type: 'null'
          title: Reconciliation Notes
      type: object
      required:
      - id
      - study_id
      - site_id
      - certificate_number
      - lot_number
      - kit_numbers
      - vial_identifiers
      - quantity_destroyed
      - unit_of_measure
      - destruction_reason
      - destruction_method
      - destruction_facility
      - destruction_timestamp
      - witness_1_id
      - witness_1_name
      - witness_1_role
      - witness_1_signed_at
      - witness_2_id
      - witness_2_name
      - witness_2_role
      - witness_2_signed_at
      - cryptographic_seal
      - status
      title: CGTDestructionCertificateResponse
      description: Response schema for formal Certificate of Destruction.
    CGTFullTraceabilityResponse:
      properties:
        chain_of_custody:
          $ref: '#/components/schemas/CGTChainOfCustodyResponse'
        milestones:
          items:
            $ref: '#/components/schemas/CGTMilestoneResponse'
          type: array
          title: Milestones
        bedside_verification:
          anyOf:
          - $ref: '#/components/schemas/CGTBedsideVerificationResponse'
          - type: 'null'
        destruction_certificate:
          anyOf:
          - $ref: '#/components/schemas/CGTDestructionCertificateResponse'
          - type: 'null'
      type: object
      required:
      - chain_of_custody
      - milestones
      title: CGTFullTraceabilityResponse
      description: Comprehensive response schema for end-to-end Vein-to-Vein tracking.
    CGTInfusionExecuteRequest:
      properties:
        coc_id:
          type: string
          title: Coc Id
        unlock_token:
          type: string
          title: Unlock Token
        administrator_id:
          type: string
          title: Administrator Id
        administrator_name:
          type: string
          title: Administrator Name
        administrator_role:
          type: string
          title: Administrator Role
          default: Infusion Nurse
        volume_infused_ml:
          type: number
          title: Volume Infused Ml
          default: 50.0
        vital_signs_pre_temp_celsius:
          type: number
          title: Vital Signs Pre Temp Celsius
          default: 36.8
        vital_signs_post_temp_celsius:
          type: number
          title: Vital Signs Post Temp Celsius
          default: 37.1
        notes:
          anyOf:
          - type: string
          - type: 'null'
          title: Notes
      type: object
      required:
      - coc_id
      - unlock_token
      - administrator_id
      - administrator_name
      title: CGTInfusionExecuteRequest
      description: Payload to execute infusion administration with unlock token verification.
    CGTManufacturingBindRequest:
      properties:
        coc_id:
          type: string
          title: Coc Id
        batch_id:
          type: string
          title: Batch Id
        manufacturing_facility_id:
          type: string
          title: Manufacturing Facility Id
        operator_id:
          type: string
          title: Operator Id
        operator_name:
          type: string
          title: Operator Name
        operator_role:
          type: string
          title: Operator Role
          default: Cell Manufacturing Specialist
        temperature_celsius:
          type: number
          title: Temperature Celsius
          default: -196.0
        notes:
          anyOf:
          - type: string
          - type: 'null'
          title: Notes
      type: object
      required:
      - coc_id
      - batch_id
      - manufacturing_facility_id
      - operator_id
      - operator_name
      title: CGTManufacturingBindRequest
      description: Payload to bind a cell engineering manufacturing batch record to original subject identity.
    CGTMilestoneCreateRequest:
      properties:
        coc_id:
          type: string
          title: Coc Id
        stage:
          type: string
          title: Stage
        location_name:
          type: string
          title: Location Name
        temperature_celsius:
          type: number
          title: Temperature Celsius
        custodian_id:
          type: string
          title: Custodian Id
        custodian_name:
          type: string
          title: Custodian Name
        custodian_role:
          type: string
          title: Custodian Role
        facility_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Facility Id
        latitude:
          anyOf:
          - type: number
          - type: 'null'
          title: Latitude
        longitude:
          anyOf:
          - type: number
          - type: 'null'
          title: Longitude
        carrier:
          anyOf:
          - type: string
          - type: 'null'
          title: Carrier
        tracking_number:
          anyOf:
          - type: string
          - type: 'null'
          title: Tracking Number
        seal_intact:
          type: boolean
          title: Seal Intact
          default: true
        seal_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Seal Id
        dry_shipper_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Dry Shipper Id
        signature_token:
          anyOf:
          - type: string
          - type: 'null'
          title: Signature Token
        notes:
          anyOf:
          - type: string
          - type: 'null'
          title: Notes
      type: object
      required:
      - coc_id
      - stage
      - location_name
      - temperature_celsius
      - custodian_id
      - custodian_name
      - custodian_role
      title: CGTMilestoneCreateRequest
      description: Payload to record a cryogenic transit milestone with GPS and temperature.
    CGTMilestoneResponse:
      properties:
        id:
          type: string
          title: Id
        study_id:
          type: string
          title: Study Id
        chain_of_custody_id:
          type: string
          title: Chain Of Custody Id
        stage:
          type: string
          title: Stage
        milestone_type:
          type: string
          title: Milestone Type
        timestamp:
          type: string
          format: date-time
          title: Timestamp
        location_name:
          type: string
          title: Location Name
        facility_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Facility Id
        latitude:
          anyOf:
          - type: number
          - type: 'null'
          title: Latitude
        longitude:
          anyOf:
          - type: number
          - type: 'null'
          title: Longitude
        temperature_celsius:
          type: number
          title: Temperature Celsius
        temperature_status:
          type: string
          title: Temperature Status
        custodian_id:
          type: string
          title: Custodian Id
        custodian_name:
          type: string
          title: Custodian Name
        custodian_role:
          type: string
          title: Custodian Role
        carrier:
          anyOf:
          - type: string
          - type: 'null'
          title: Carrier
        tracking_number:
          anyOf:
          - type: string
          - type: 'null'
          title: Tracking Number
        seal_intact:
          type: boolean
          title: Seal Intact
        seal_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Seal Id
        dry_shipper_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Dry Shipper Id
        electronic_signature:
          anyOf:
          - additionalProperties: true
            type: object
          - type: 'null'
          title: Electronic Signature
        notes:
          anyOf:
          - type: string
          - type: 'null'
          title: Notes
      type: object
      required:
      - id
      - study_id
      - chain_of_custody_id
      - stage
      - milestone_type
      - timestamp
      - location_name
      - temperature_celsius
      - temperature_status
      - custodian_id
      - custodian_name
      - custodian_role
      - seal_intact
      title: CGTMilestoneResponse
      description: Response schema for a cryogenic transit milestone.
    CGTVialReturnReconciliationRequest:
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
        kit_number:
          type: string
          title: Kit Number
        vial_identifier:
          type: string
          title: Vial Identifier
        lot_number:
          type: string
          title: Lot Number
        expected_quantity:
          type: integer
          title: Expected Quantity
          default: 1
        returned_quantity:
          type: integer
          title: Returned Quantity
          default: 1
        intact_vials:
          type: integer
          title: Intact Vials
          default: 0
        used_vials:
          type: integer
          title: Used Vials
          default: 1
        residual_volume_ml:
          type: number
          title: Residual Volume Ml
          default: 0.0
        din:
          anyOf:
          - type: string
          - type: 'null'
          title: Din
        logged_by:
          type: string
          title: Logged By
        disposition:
          type: string
          title: Disposition
          default: RECONCILED_FOR_DESTRUCTION
      type: object
      required:
      - study_id
      - site_id
      - subject_id
      - kit_number
      - vial_identifier
      - lot_number
      - logged_by
      title: CGTVialReturnReconciliationRequest
      description: Payload to log returned kit/vial accountability and compute adherence percentage.
    CGTVialReturnReconciliationResponse:
      properties:
        id:
          type: string
          title: Id
        study_id:
          type: string
          title: Study Id
        site_id:
          type: string
          title: Site Id
        subject_id:
          type: string
          title: Subject Id
        kit_number:
          type: string
          title: Kit Number
        vial_identifier:
          type: string
          title: Vial Identifier
        lot_number:
          type: string
          title: Lot Number
        din:
          anyOf:
          - type: string
          - type: 'null'
          title: Din
        expected_quantity:
          type: integer
          title: Expected Quantity
        returned_quantity:
          type: integer
          title: Returned Quantity
        intact_vials:
          type: integer
          title: Intact Vials
        used_vials:
          type: integer
          title: Used Vials
        residual_volume_ml:
          type: number
          title: Residual Volume Ml
        adherence_percentage:
          type: number
          title: Adherence Percentage
        disposition:
          type: string
          title: Disposition
        logged_by:
          type: string
          title: Logged By
        logged_at:
          type: string
          format: date-time
          title: Logged At
        certificate_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Certificate Id
      type: object
      required:
      - id
      - study_id
      - site_id
      - subject_id
      - kit_number
      - vial_identifier
      - lot_number
      - expected_quantity
      - returned_quantity
      - intact_vials
      - used_vials
      - residual_volume_ml
      - adherence_percentage
      - disposition
      - logged_by
      - logged_at
      title: CGTVialReturnReconciliationResponse
      description: Response schema for vial return reconciliation.
    CecCharterConfig:
      properties:
        charter_id:
          type: string
          title: Charter Id
          description: Unique charter ID
        study_id:
          type: string
          title: Study Id
          description: Study ID
        committee_name:
          type: string
          title: Committee Name
          description: Name of Adjudication Committee
        version:
          type: string
          title: Version
          description: Charter version string
          default: '1.0'
        voting_rule:
          $ref: '#/components/schemas/VotingRule'
          description: Committee voting rule
          default: CHAIR_TIE_BREAKER
        minimum_quorum_percentage:
          type: number
          maximum: 100.0
          minimum: 0.0
          title: Minimum Quorum Percentage
          description: Minimum % quorum required
          default: 66.67
        minimum_members_count:
          type: integer
          minimum: 1.0
          title: Minimum Members Count
          description: Minimum member count for quorum
          default: 3
        eligible_members:
          items:
            additionalProperties:
              type: string
            type: object
          type: array
          title: Eligible Members
          description: 'Eligible members: [{member_id, member_name, role, specialty}]'
        endpoint_definitions:
          items:
            type: string
          type: array
          title: Endpoint Definitions
          description: List of endpoints governed by this charter
        diagnostic_criteria_templates:
          additionalProperties:
            items:
              type: string
            type: array
          type: object
          title: Diagnostic Criteria Templates
          description: Standard criteria checklists per endpoint
        is_active:
          type: boolean
          title: Is Active
          default: true
        created_at:
          type: string
          title: Created At
        created_by:
          type: string
          title: Created By
          default: system
        version_index:
          type: integer
          title: Version Index
          default: 1
      type: object
      required:
      - charter_id
      - study_id
      - committee_name
      title: CecCharterConfig
      description: Charter governance parameters defining voting rules, quorum, and criteria templates.
    CecDossierSummaryItem:
      properties:
        dossier_id:
          type: string
          title: Dossier Id
        event_id:
          type: string
          title: Event Id
        study_id:
          type: string
          title: Study Id
        masked_subject_id:
          type: string
          title: Masked Subject Id
        candidate_event_type:
          $ref: '#/components/schemas/EndpointCategory'
        reported_term:
          type: string
          title: Reported Term
        reported_onset_date:
          type: string
          title: Reported Onset Date
        workflow_status:
          $ref: '#/components/schemas/AdjudicationWorkflowStatus'
        is_discordant:
          type: boolean
          title: Is Discordant
        final_consensus_classification:
          anyOf:
          - $ref: '#/components/schemas/AdjudicationClassification'
          - type: 'null'
        reader_1_status:
          type: string
          title: Reader 1 Status
        reader_2_status:
          type: string
          title: Reader 2 Status
        created_at:
          type: string
          title: Created At
      type: object
      required:
      - dossier_id
      - event_id
      - study_id
      - masked_subject_id
      - candidate_event_type
      - reported_term
      - reported_onset_date
      - workflow_status
      - is_discordant
      - reader_1_status
      - reader_2_status
      - created_at
      title: CecDossierSummaryItem
      description: Summary item for CEC console list views.
    CertificationStatusEnum:
      type: string
      enum:
      - ACTIVE
      - EXPIRED
      - SUSPENDED
      - PENDING_RENEWAL
      title: CertificationStatusEnum
      description: Accreditation and certification lifecycle status of a local laboratory.
    ChairAdjudicateDTO:
      properties:
        final_classification:
          $ref: '#/components/schemas/AdjudicationClassification'
          description: Definitive classification
        final_subtype:
          anyOf:
          - type: string
          - type: 'null'
          title: Final Subtype
          description: Definitive subtype
        final_adjudicated_onset_date:
          type: string
          title: Final Adjudicated Onset Date
          description: Definitive onset date (ISO 8601)
        binding_reader:
          $ref: '#/components/schemas/BindingDeterminationSource'
          description: Binding determination source
          default: CHAIR_CUSTOM_DETERMINATION
        criteria_confirmed:
          items:
            type: string
          type: array
          title: Criteria Confirmed
          description: Criteria confirmed
        adjudication_rationale:
          type: string
          title: Adjudication Rationale
          description: Formal adjudication rationale
        signature_token:
          anyOf:
          - type: string
          - type: 'null'
          title: Signature Token
          description: Step-up HMAC signature token
        signer_name:
          type: string
          title: Signer Name
          description: Printed signer name
          default: Dr. Adjudication Chair
        password:
          type: string
          title: Password
          description: Re-authentication password
          default: SignOffPassword123!
        signature_meaning:
          type: string
          title: Signature Meaning
          description: 21 CFR Part 11 legal signature manifestation
          default: I confirm this clinical endpoint adjudication determination is final, complete, and accurate per charter guidelines
      type: object
      required:
      - final_classification
      - final_adjudicated_onset_date
      - adjudication_rationale
      title: ChairAdjudicateDTO
      description: Payload for Adjudication Committee Chair tie-breaker ruling with e-signature.
    ChairAdjudicationDetermination:
      properties:
        determination_id:
          type: string
          title: Determination Id
          description: Unique determination ID
        dossier_id:
          type: string
          title: Dossier Id
          description: Target dossier ID
        chair_id:
          type: string
          title: Chair Id
          description: Chair user ID
        chair_name:
          type: string
          title: Chair Name
          description: Chair full name
        final_classification:
          $ref: '#/components/schemas/AdjudicationClassification'
          description: Definitive classification
        final_subtype:
          anyOf:
          - type: string
          - type: 'null'
          title: Final Subtype
          description: Definitive event subtype
        final_adjudicated_onset_date:
          type: string
          title: Final Adjudicated Onset Date
          description: Definitive event onset date (ISO 8601)
        binding_reader:
          $ref: '#/components/schemas/BindingDeterminationSource'
          description: Source of binding decision
          default: CHAIR_CUSTOM_DETERMINATION
        criteria_confirmed:
          items:
            type: string
          type: array
          title: Criteria Confirmed
          description: Diagnostic criteria confirmed
        adjudication_rationale:
          type: string
          title: Adjudication Rationale
          description: Formal chair tie-breaker rationale
        electronic_signature:
          additionalProperties: true
          type: object
          title: Electronic Signature
          description: 21 CFR Part 11 electronic signature payload
        finalized_at:
          type: string
          title: Finalized At
      type: object
      required:
      - determination_id
      - dossier_id
      - chair_id
      - chair_name
      - final_classification
      - final_adjudicated_onset_date
      - adjudication_rationale
      - electronic_signature
      title: ChairAdjudicationDetermination
      description: Final tie-breaker determination issued by the Adjudication Committee Chair.
    CharterCreateDTO:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Target study ID
        committee_name:
          type: string
          title: Committee Name
          description: Committee name
        version:
          type: string
          title: Version
          default: '1.0'
        voting_rule:
          $ref: '#/components/schemas/VotingRule'
          default: CHAIR_TIE_BREAKER
        minimum_quorum_percentage:
          type: number
          maximum: 100.0
          minimum: 0.0
          title: Minimum Quorum Percentage
          default: 66.67
        minimum_members_count:
          type: integer
          minimum: 1.0
          title: Minimum Members Count
          default: 3
        eligible_members:
          items:
            additionalProperties:
              type: string
            type: object
          type: array
          title: Eligible Members
        endpoint_definitions:
          items:
            type: string
          type: array
          title: Endpoint Definitions
        diagnostic_criteria_templates:
          additionalProperties:
            items:
              type: string
            type: array
          type: object
          title: Diagnostic Criteria Templates
      type: object
      required:
      - study_id
      - committee_name
      title: CharterCreateDTO
      description: Payload to configure or update a committee charter.
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
          - type: 'null'
          title: Visit Id
        domain:
          anyOf:
          - type: string
          - type: 'null'
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
          - type: 'null'
          title: Explanation
        response:
          anyOf:
          - type: string
          - type: 'null'
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
            $ref: '#/components/schemas/QueryHistoryItem'
          type: array
          title: History
          default: []
        observation_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Observation Id
        field_link:
          anyOf:
          - type: string
          - type: 'null'
          title: Field Link
        message:
          anyOf:
          - type: string
          - type: 'null'
          title: Message
        origin:
          anyOf:
          - type: string
          - type: 'null'
          title: Origin
        priority:
          anyOf:
          - type: string
          - type: 'null'
          title: Priority
        rule_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Rule Id
        created_by:
          anyOf:
          - type: string
          - type: 'null'
          title: Created By
        responder:
          anyOf:
          - type: string
          - type: 'null'
          title: Responder
        resolver:
          anyOf:
          - type: string
          - type: 'null'
          title: Resolver
        resolved_at:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Resolved At
        cancellation_reason:
          anyOf:
          - type: string
          - type: 'null'
          title: Cancellation Reason
        escalated_at:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Escalated At
        form_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Form Id
        field_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Field Id
        query_type:
          anyOf:
          - type: string
          - type: 'null'
          title: Query Type
        action_required:
          anyOf:
          - type: string
          - type: 'null'
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
          - type: 'null'
          title: Code
        term:
          anyOf:
          - type: string
          - type: 'null'
          title: Term
        suggestion_index:
          anyOf:
          - type: integer
          - type: 'null'
          title: Suggestion Index
        reason_for_change:
          anyOf:
          - type: string
          - type: 'null'
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
          - type: 'null'
          title: Source Field
        observation_id:
          anyOf:
          - type: string
          - type: 'null'
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
          - type: 'null'
          title: Coded Code
        coded_term:
          anyOf:
          - type: string
          - type: 'null'
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
          - type: 'null'
          title: Assigned By
        assigned_at:
          type: string
          format: date-time
          title: Assigned At
        score:
          anyOf:
          - type: number
          - type: 'null'
          title: Score
        hierarchy:
          anyOf:
          - additionalProperties: true
            type: object
          - items: {}
            type: array
          - type: 'null'
          title: Hierarchy
        suggestions:
          anyOf:
          - items: {}
            type: array
          - additionalProperties: true
            type: object
          - type: 'null'
          title: Suggestions
        domain:
          anyOf:
          - type: string
          - type: 'null'
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
    CohortEvaluationInput:
      properties:
        study_id:
          type: string
          title: Study Id
        cohort_number:
          type: integer
          minimum: 1.0
          title: Cohort Number
        dose_level_index:
          type: integer
          minimum: 1.0
          title: Dose Level Index
        subjects_evaluated:
          type: integer
          minimum: 1.0
          title: Subjects Evaluated
        dlts_observed:
          type: integer
          minimum: 0.0
          title: Dlts Observed
        subject_ids:
          items:
            type: string
          type: array
          title: Subject Ids
        dlt_subject_ids:
          items:
            type: string
          type: array
          title: Dlt Subject Ids
      type: object
      required:
      - study_id
      - cohort_number
      - dose_level_index
      - subjects_evaluated
      - dlts_observed
      title: CohortEvaluationInput
      description: Input payload to evaluate an active cohort and compute adaptive dose recommendations.
    CohortEvaluationResult:
      properties:
        study_id:
          type: string
          title: Study Id
        cohort_number:
          type: integer
          title: Cohort Number
        evaluated_dose_level_index:
          type: integer
          title: Evaluated Dose Level Index
        evaluated_dose_name:
          type: string
          title: Evaluated Dose Name
        subjects_in_cohort:
          type: integer
          title: Subjects In Cohort
        dlts_in_cohort:
          type: integer
          title: Dlts In Cohort
        recommendation_action:
          type: string
          title: Recommendation Action
        recommended_dose_level_index:
          anyOf:
          - type: integer
          - type: 'null'
          title: Recommended Dose Level Index
        recommended_dose_name:
          anyOf:
          - type: string
          - type: 'null'
          title: Recommended Dose Name
        lambda_e:
          anyOf:
          - type: number
          - type: 'null'
          title: Lambda E
        lambda_d:
          anyOf:
          - type: number
          - type: 'null'
          title: Lambda D
        target_toxicity_rate:
          anyOf:
          - type: number
          - type: 'null'
          title: Target Toxicity Rate
        overdose_probability:
          anyOf:
          - type: number
          - type: 'null'
          title: Overdose Probability
        is_safety_stop_triggered:
          type: boolean
          title: Is Safety Stop Triggered
          default: false
        is_mtd_declared:
          type: boolean
          title: Is Mtd Declared
          default: false
        mtd_dose_level_index:
          anyOf:
          - type: integer
          - type: 'null'
          title: Mtd Dose Level Index
        mtd_dose_name:
          anyOf:
          - type: string
          - type: 'null'
          title: Mtd Dose Name
        dose_summaries:
          items:
            $ref: '#/components/schemas/DoseLevelEvaluationSummary'
          type: array
          title: Dose Summaries
        rationale:
          type: string
          title: Rationale
      type: object
      required:
      - study_id
      - cohort_number
      - evaluated_dose_level_index
      - evaluated_dose_name
      - subjects_in_cohort
      - dlts_in_cohort
      - recommendation_action
      - rationale
      title: CohortEvaluationResult
      description: Output recommendation and Bayesian metrics from a cohort evaluation.
    CohortRecommendationAction:
      type: string
      enum:
      - ESCALATE
      - DE_ESCALATE
      - STAY
      - EXPAND_COHORT
      - STOP_SAFETY
      - STOP_SUCCESS
      - MTD_FOUND
      title: CohortRecommendationAction
      description: Automated and committee-reviewed cohort actions.
    CohortTransitionLogResponse:
      properties:
        id:
          type: string
          title: Id
        study_id:
          type: string
          title: Study Id
        cohort_number:
          type: integer
          title: Cohort Number
        from_dose_level:
          type: integer
          title: From Dose Level
        to_dose_level:
          type: integer
          title: To Dose Level
        action_taken:
          type: string
          title: Action Taken
        algorithm_recommendation:
          type: string
          title: Algorithm Recommendation
        override_occurred:
          type: boolean
          title: Override Occurred
        override_reason:
          anyOf:
          - type: string
          - type: 'null'
          title: Override Reason
        evaluated_dlt_rate:
          anyOf:
          - type: number
          - type: 'null'
          title: Evaluated Dlt Rate
        posterior_toxicity_estimates:
          anyOf:
          - additionalProperties: true
            type: object
          - type: 'null'
          title: Posterior Toxicity Estimates
        authorized_by:
          type: string
          title: Authorized By
        transitioned_at:
          type: string
          format: date-time
          title: Transitioned At
        signature_token:
          anyOf:
          - type: string
          - type: 'null'
          title: Signature Token
        cryptographic_checksum:
          anyOf:
          - type: string
          - type: 'null'
          title: Cryptographic Checksum
      type: object
      required:
      - id
      - study_id
      - cohort_number
      - from_dose_level
      - to_dose_level
      - action_taken
      - algorithm_recommendation
      - override_occurred
      - authorized_by
      - transitioned_at
      title: CohortTransitionLogResponse
      description: 21 CFR Part 11 audited transition log record.
    CohortTransitionRequest:
      properties:
        study_id:
          type: string
          title: Study Id
        cohort_number:
          type: integer
          minimum: 1.0
          title: Cohort Number
        decision_action:
          type: string
          title: Decision Action
        target_dose_level_index:
          type: integer
          minimum: 1.0
          title: Target Dose Level Index
        override_reason:
          anyOf:
          - type: string
          - type: 'null'
          title: Override Reason
        electronic_signature:
          anyOf:
          - type: string
          - type: 'null'
          title: Electronic Signature
      type: object
      required:
      - study_id
      - cohort_number
      - decision_action
      - target_dose_level_index
      title: CohortTransitionRequest
      description: Payload to confirm or override an automated cohort recommendation.
    ComplianceStatusResponse:
      properties:
        study_id:
          type: string
          title: Study Id
        site_id:
          anyOf:
          - type: string
          - type: 'null'
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
    CrmConfig:
      properties:
        target_toxicity_rate:
          type: number
          exclusiveMaximum: 1.0
          exclusiveMinimum: 0.0
          title: Target Toxicity Rate
          description: Target DLT rate phi.
          default: 0.25
        skeleton_probs:
          items:
            type: number
          type: array
          minItems: 2
          title: Skeleton Probs
          description: Prior toxicity probability skeleton for each dose level (strictly increasing).
        model_type:
          type: string
          title: Model Type
          description: Dose-toxicity link model (e.g. 'EMPIRIC_POWER' where p_j(theta) = p_{0,j}^{exp(theta)}).
          default: EMPIRIC_POWER
        prior_std:
          type: number
          exclusiveMinimum: 0.0
          title: Prior Std
          description: Prior standard deviation for model parameter theta ~ Normal(0, sigma^2).
          default: 1.34
        safety_cutoff:
          type: number
          exclusiveMaximum: 1.0
          exclusiveMinimum: 0.5
          title: Safety Cutoff
          description: Safety stopping threshold on dose 1 toxicity P(p_1 > phi | D) > cutoff.
          default: 0.9
        allow_dose_skipping:
          type: boolean
          title: Allow Dose Skipping
          description: If False (modified CRM), forbids jumping over untried doses upon escalation.
          default: false
      type: object
      required:
      - skeleton_probs
      title: CrmConfig
      description: Configuration parameters for Continual Reassessment Method (CRM / mCRM).
    CrmPosteriorCalculationRequest:
      properties:
        skeleton_probs:
          items:
            type: number
          type: array
          title: Skeleton Probs
        target_toxicity_rate:
          type: number
          title: Target Toxicity Rate
          default: 0.25
        prior_std:
          type: number
          title: Prior Std
          default: 1.34
        dose_evaluations:
          items:
            additionalProperties:
              type: integer
            type: object
          type: array
          title: Dose Evaluations
          description: 'List of dicts per dose level: [{''dose_index'': 1, ''enrolled'': 3, ''dlts'': 0}, ...]'
        current_dose_index:
          type: integer
          title: Current Dose Index
          default: 1
      type: object
      required:
      - skeleton_probs
      - dose_evaluations
      title: CrmPosteriorCalculationRequest
      description: Direct calculation request for CRM posterior curve.
    CrmPosteriorCalculationResponse:
      properties:
        target_toxicity_rate:
          type: number
          title: Target Toxicity Rate
        current_dose_index:
          type: integer
          title: Current Dose Index
        recommended_dose_index:
          type: integer
          title: Recommended Dose Index
        recommendation_action:
          $ref: '#/components/schemas/CohortRecommendationAction'
        posterior_mean_probabilities:
          items:
            type: number
          type: array
          title: Posterior Mean Probabilities
        overdose_probability_dose_1:
          type: number
          title: Overdose Probability Dose 1
        is_safety_stop_triggered:
          type: boolean
          title: Is Safety Stop Triggered
        rationale:
          type: string
          title: Rationale
      type: object
      required:
      - target_toxicity_rate
      - current_dose_index
      - recommended_dose_index
      - recommendation_action
      - posterior_mean_probabilities
      - overdose_probability_dose_1
      - is_safety_stop_triggered
      - rationale
      title: CrmPosteriorCalculationResponse
      description: Response containing calculated CRM posterior probabilities and recommended next dose.
    CrossDomainAnomaly:
      properties:
        anomaly_id:
          type: string
          title: Anomaly Id
          description: Unique identifier for the detected anomaly instance.
        anomaly_type:
          $ref: '#/components/schemas/CrossDomainAnomalyType'
          description: Category of the cross-domain inconsistency.
        study_id:
          type: string
          title: Study Id
          description: Clinical study scope identifier.
        subject_id:
          type: string
          title: Subject Id
          description: Clinical subject identifier.
        site_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Site Id
          description: Clinical site identifier if known.
        visit_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Visit Id
          description: Clinical visit identifier associated with primary finding.
        primary_domain:
          type: string
          title: Primary Domain
          description: Primary CDISC domain (e.g. AE, CM, LB, VS, DS, EX).
        primary_test_code:
          type: string
          title: Primary Test Code
          description: Primary observation test code.
        correlated_domain:
          type: string
          title: Correlated Domain
          description: Correlated CDISC domain that exhibits inconsistency.
        correlated_test_code:
          anyOf:
          - type: string
          - type: 'null'
          title: Correlated Test Code
          description: Optional correlated test code in the related domain.
        severity:
          $ref: '#/components/schemas/AnomalySeverity'
          description: Clinical severity or query priority.
          default: MEDIUM
        message:
          type: string
          title: Message
          description: Short, human-readable summary of the discrepancy.
        explanation:
          type: string
          title: Explanation
          description: Detailed clinical explanation and rationale.
        confidence_score:
          type: number
          maximum: 1.0
          minimum: 0.0
          title: Confidence Score
          description: Confidence score (1.0 for deterministic rules, 0.0-1.0 for AI reasoners).
          default: 1.0
        model_identifier:
          anyOf:
          - type: string
          - type: 'null'
          title: Model Identifier
          description: AI model identifier if generated or assisted by AI Gateway.
        prompt_hash:
          anyOf:
          - type: string
          - type: 'null'
          title: Prompt Hash
          description: Cryptographic SHA-256 prompt hash for AI Gateway attribution.
        observation_ids:
          items:
            type: string
          type: array
          title: Observation Ids
          description: List of associated ClinicalObservation IDs involved in this finding.
        form_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Form Id
          description: Associated eCRF form or page ID.
        field_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Field Id
          description: Associated form field ID.
      type: object
      required:
      - anomaly_type
      - study_id
      - subject_id
      - primary_domain
      - primary_test_code
      - correlated_domain
      - message
      - explanation
      title: CrossDomainAnomaly
      description: Domain representation of an identified cross-domain inconsistency or anomaly.
    CrossDomainAnomalyType:
      type: string
      enum:
      - AE_WITHOUT_CONCOMITANT_MED
      - CONCOMITANT_MED_WITHOUT_AE
      - MARKED_LAB_ABNORMALITY_WITHOUT_AE
      - CRITICAL_VITALS_WITHOUT_AE
      - VITALS_INCONSISTENT_WITH_HISTORY
      - DRUG_DISCONTINUATION_WITHOUT_AE
      - TEMPORAL_SEQUENCE_MISMATCH
      - AI_CONTEXTUAL_INCONSISTENCY
      title: CrossDomainAnomalyType
      description: Enumeration of cross-domain clinical anomaly and discrepancy categories.
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
          $ref: '#/components/schemas/CustodianEnum'
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
          $ref: '#/components/schemas/DOATaskRoleEnum'
          description: Site personnel role
        delegated_tasks:
          items:
            $ref: '#/components/schemas/DOATaskDelegationEnum'
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
          - type: 'null'
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
      description: 'Delegation of Authority (DOA) site personnel assignment log record.


        Requirements: PRD-SYS-001'
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
          - type: 'null'
          title: Pi User Id
        status:
          type: string
          title: Status
        pi_signature_hash:
          anyOf:
          - type: string
          - type: 'null'
          title: Pi Signature Hash
        pi_approved_at:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Pi Approved At
        end_date:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: End Date
        reason_for_change:
          anyOf:
          - type: string
          - type: 'null'
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
      description: 'Request payload for PI eSignature endorsement of DOA record.


        Requirements: PRD-SYS-001'
    DOATaskDelegationEnum:
      type: string
      enum:
      - SUBJECT_INFORMED_CONSENT
      - PHYSICAL_EXAMINATION
      - AE_SAE_REPORTING
      - CRF_DATA_ENTRY
      - PI_CASEBOOK_SIGNOFF
      title: DOATaskDelegationEnum
      description: 'Specific clinical trial study tasks delegated to site personnel.


        Requirements: PRD-SYS-001'
    DOATaskRoleEnum:
      type: string
      enum:
      - PRINCIPAL_INVESTIGATOR
      - SUB_INVESTIGATOR
      - CLINICAL_RESEARCH_COORDINATOR
      - STUDY_NURSE
      - DATA_MANAGER
      title: DOATaskRoleEnum
      description: 'Site personnel roles on Delegation of Authority log.


        Requirements: PRD-SYS-001'
    DailyDemandProjection:
      properties:
        day_index:
          type: integer
          title: Day Index
          description: Day index from forecast start (1..N).
        date:
          type: string
          title: Date
          description: Projected calendar date (YYYY-MM-DD).
        scheduled_dispensations:
          type: integer
          title: Scheduled Dispensations
          description: Kits required for active subject visits on this day.
        projected_new_subject_dispensations:
          type: integer
          title: Projected New Subject Dispensations
          description: Kits required for newly randomized subjects entering on this day.
        total_daily_demand:
          type: integer
          title: Total Daily Demand
          description: Sum of scheduled and newly projected kit demands.
        cumulative_demand:
          type: integer
          title: Cumulative Demand
          description: Cumulative demand from day 1 to day t.
        projected_closing_balance:
          type: integer
          title: Projected Closing Balance
          description: Projected inventory at close of day.
      type: object
      required:
      - day_index
      - date
      - scheduled_dispensations
      - projected_new_subject_dispensations
      - total_daily_demand
      - cumulative_demand
      - projected_closing_balance
      title: DailyDemandProjection
      description: Daily projected kit requirement and stock trajectory.
    DataLockRecord:
      properties:
        lock_id:
          type: string
          title: Lock Id
          description: Unique data lock record identifier
        study_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Study Id
          description: Target protocol study ID
        site_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Site Id
          description: Target clinical site ID
        subject_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Subject Id
          description: Target clinical trial subject ID
        visit_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Visit Id
          description: Target clinical visit ID
        form_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Form Id
          description: Target eCRF form submission ID
        item_group_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Item Group Id
          description: Optional target item group code
        field_name:
          anyOf:
          - type: string
          - type: 'null'
          title: Field Name
          description: Optional target field variable name
        scope:
          anyOf:
          - $ref: '#/components/schemas/LockScopeEnum'
          - type: string
          title: Scope
          description: 'Lock scope: STUDY, SITE, SUBJECT, VISIT, FORM, ITEM_GROUP, FIELD'
          default: FORM
        scope_type:
          anyOf:
          - type: string
          - type: 'null'
          title: Scope Type
          description: Scope type identifier string
        scope_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Scope Id
          description: Target entity ID for specified scope
        status:
          anyOf:
          - $ref: '#/components/schemas/LockStatusEnum'
          - type: string
          title: Status
          description: 'Lock status: UNLOCKED, FROZEN, LOCKED, HARD_LOCK, SOFT_LOCK'
          default: LOCKED
        lock_type:
          anyOf:
          - type: string
          - type: 'null'
          title: Lock Type
          description: 'Lock type string: FROZEN, LOCKED, HARD_LOCK'
        is_active:
          type: boolean
          title: Is Active
          description: Indicates if the lock is actively enforced
          default: true
        locked_by:
          type: string
          title: Locked By
          description: User ID who executed data lock
        created_by:
          anyOf:
          - type: string
          - type: 'null'
          title: Created By
          description: Alias for locked_by user ID
        reason_for_change:
          type: string
          title: Reason For Change
          description: GxP 21 CFR Part 11 justification reason
        locked_at:
          type: string
          title: Locked At
          description: UTC ISO timestamp of lock execution
        created_at:
          anyOf:
          - type: string
          - type: 'null'
          title: Created At
          description: Alias for locked_at timestamp
        unlocked_by:
          anyOf:
          - type: string
          - type: 'null'
          title: Unlocked By
          description: User ID executing unlock override
        unlocked_at:
          anyOf:
          - type: string
          - type: 'null'
          title: Unlocked At
          description: UTC ISO timestamp of unlock override
        unlock_justification:
          anyOf:
          - type: string
          - type: 'null'
          title: Unlock Justification
          description: Mandatory >=50 character GxP unlock justification reason
        signature_token:
          anyOf:
          - type: string
          - type: 'null'
          title: Signature Token
          description: 21 CFR Part 11 step-up token digest
      type: object
      required:
      - lock_id
      - locked_by
      - reason_for_change
      - locked_at
      title: DataLockRecord
      description: Data lock state record representing frozen or locked clinical eCRF data.
    DataLockRequest:
      properties:
        study_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Study Id
          description: Target protocol study ID
        site_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Site Id
          description: Target site ID
        subject_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Subject Id
          description: Target subject ID
        visit_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Visit Id
          description: Target visit ID
        form_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Form Id
          description: Target eCRF form ID
        item_group_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Item Group Id
          description: Optional target item group code
        field_name:
          anyOf:
          - type: string
          - type: 'null'
          title: Field Name
          description: Optional target field variable name
        scope:
          anyOf:
          - $ref: '#/components/schemas/LockScopeEnum'
          - type: string
          - type: 'null'
          title: Scope
          description: 'Lock scope: STUDY, TRIAL, SITE, SUBJECT, VISIT, FORM, ITEM_GROUP, FIELD'
        scope_type:
          anyOf:
          - type: string
          - type: 'null'
          title: Scope Type
          description: 'Scope type name: STUDY, SITE, SUBJECT, VISIT, FORM, FIELD'
        scope_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Scope Id
          description: Target identifier for the specified scope
        action:
          type: string
          title: Action
          description: 'Action to perform: LOCK, FREEZE, HARD_LOCK, UNLOCK'
          default: LOCK
        lock_type:
          anyOf:
          - type: string
          - type: 'null'
          title: Lock Type
          description: 'Lock classification: FROZEN, LOCKED, HARD_LOCK, SOFT_LOCK'
        reason_for_change:
          anyOf:
          - type: string
          - type: 'null'
          title: Reason For Change
          description: Mandatory GxP 21 CFR Part 11 justification reason
        reason:
          anyOf:
          - type: string
          - type: 'null'
          title: Reason
          description: Convenience alias for reason_for_change
        justification:
          anyOf:
          - type: string
          - type: 'null'
          title: Justification
          description: Mandatory >=50 character justification for unlock operations
        lock_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Lock Id
          description: Specific lock ID targeted for unlock operation
      type: object
      title: DataLockRequest
      description: Request payload to execute form, item group, field, subject, visit, site, or study data locking.
    DataLockResponse:
      properties:
        lock_id:
          type: string
          title: Lock Id
          description: Lock record identifier
        status:
          type: string
          title: Status
          description: 'Resulting status: LOCKED, FROZEN, HARD_LOCK, UNLOCKED'
        message:
          type: string
          title: Message
          description: Operation result confirmation message
        record:
          anyOf:
          - $ref: '#/components/schemas/DataLockRecord'
          - additionalProperties: true
            type: object
          - type: 'null'
          title: Record
          description: Updated data lock record
        scope_type:
          anyOf:
          - type: string
          - type: 'null'
          title: Scope Type
          description: Scope type
        scope_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Scope Id
          description: Scope ID
        lock_type:
          anyOf:
          - type: string
          - type: 'null'
          title: Lock Type
          description: Lock type
        is_active:
          type: boolean
          title: Is Active
          description: Active status
          default: true
        locked_at:
          anyOf:
          - type: string
          - type: 'null'
          title: Locked At
          description: Lock timestamp
        unlocked_at:
          anyOf:
          - type: string
          - type: 'null'
          title: Unlocked At
          description: Unlock timestamp
      type: object
      required:
      - lock_id
      - status
      - message
      title: DataLockResponse
      description: Response payload for data lock/unlock operations.
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
          - type: 'null'
          title: Name
        birthdate:
          anyOf:
          - type: string
          - type: 'null'
          title: Birthdate
        gender:
          anyOf:
          - type: string
          - type: 'null'
          title: Gender
        race:
          anyOf:
          - type: string
          - type: 'null'
          title: Race
      type: object
      title: Demographics
      description: Pydantic schema representing demographic details.
    DiagnosticAttachment:
      properties:
        attachment_id:
          type: string
          title: Attachment Id
          description: Unique attachment ID
        document_type:
          type: string
          title: Document Type
          description: Type of diagnostic doc (e.g. Echocardiogram, Angiography, CT/MRI Brain)
        title:
          type: string
          title: Title
          description: Descriptive title
        summary:
          type: string
          title: Summary
          description: Clinical narrative summary
        key_findings:
          items:
            type: string
          type: array
          title: Key Findings
          description: Key bullet points / measurements
        file_url:
          anyOf:
          - type: string
          - type: 'null'
          title: File Url
          description: Secure presigned blob storage URI
        content_hash:
          anyOf:
          - type: string
          - type: 'null'
          title: Content Hash
          description: SHA-256 integrity hash of document blob
      type: object
      required:
      - attachment_id
      - document_type
      - title
      - summary
      title: DiagnosticAttachment
      description: De-identified medical imaging or clinical diagnostic attachment.
    DictTypeEnum:
      type: string
      enum:
      - MEDDRA
      - WHODRUG
      - LOINC
      - SNOMED
      title: DictTypeEnum
    DiscreteExcursionResponse:
      properties:
        start_time:
          type: string
          title: Start Time
        end_time:
          type: string
          title: End Time
        duration_hours:
          type: number
          title: Duration Hours
        excursion_type:
          type: string
          title: Excursion Type
        peak_or_nadir_temp_celsius:
          type: number
          title: Peak Or Nadir Temp Celsius
        threshold_celsius:
          type: number
          title: Threshold Celsius
      type: object
      required:
      - start_time
      - end_time
      - duration_hours
      - excursion_type
      - peak_or_nadir_temp_celsius
      - threshold_celsius
      title: DiscreteExcursionResponse
      description: Segment representation of a continuous excursion window.
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
      description: 'Schema representing complete document metadata.


        Requirements: PRD-SYS-001'
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
      description: 'Schema representing upload response.


        Requirements: PRD-SYS-001'
    DoseLevelDef:
      properties:
        index:
          type: integer
          minimum: 1.0
          title: Index
          description: 1-based dose level sequence index.
        name:
          type: string
          minLength: 1
          title: Name
          description: Dose identifier / label, e.g. '10 mg/kg'.
        dose_value:
          type: number
          exclusiveMinimum: 0.0
          title: Dose Value
          description: Numerical dose amount (e.g. 10.0).
      type: object
      required:
      - index
      - name
      - dose_value
      title: DoseLevelDef
      description: Definition of a discrete dose level.
    DoseLevelEvaluationSummary:
      properties:
        dose_level_index:
          type: integer
          title: Dose Level Index
        dose_name:
          type: string
          title: Dose Name
        dose_value:
          type: number
          title: Dose Value
        total_enrolled:
          type: integer
          title: Total Enrolled
        total_dlts:
          type: integer
          title: Total Dlts
        observed_dlt_rate:
          type: number
          title: Observed Dlt Rate
        posterior_dlt_rate:
          anyOf:
          - type: number
          - type: 'null'
          title: Posterior Dlt Rate
        is_eliminated_for_safety:
          type: boolean
          title: Is Eliminated For Safety
          default: false
        overdose_probability:
          anyOf:
          - type: number
          - type: 'null'
          title: Overdose Probability
      type: object
      required:
      - dose_level_index
      - dose_name
      - dose_value
      - total_enrolled
      - total_dlts
      - observed_dlt_rate
      title: DoseLevelEvaluationSummary
      description: Summary of subjects and DLTs evaluated at a specific dose level.
    DossierBlindedView:
      properties:
        dossier_id:
          type: string
          title: Dossier Id
        event_id:
          type: string
          title: Event Id
        study_id:
          type: string
          title: Study Id
        masked_site_id:
          type: string
          title: Masked Site Id
        masked_subject_id:
          type: string
          title: Masked Subject Id
        candidate_event_type:
          $ref: '#/components/schemas/EndpointCategory'
        reported_term:
          type: string
          title: Reported Term
        verbatim_description:
          type: string
          title: Verbatim Description
        reported_onset_date:
          type: string
          title: Reported Onset Date
        medical_narrative:
          type: string
          title: Medical Narrative
        is_deidentified:
          type: boolean
          title: Is Deidentified
        bundled_forms:
          items:
            $ref: '#/components/schemas/BundledEcrfForm'
          type: array
          title: Bundled Forms
        lab_panels:
          items:
            $ref: '#/components/schemas/LabPanelItem'
          type: array
          title: Lab Panels
        ecg_records:
          items:
            $ref: '#/components/schemas/ECGRecord'
          type: array
          title: Ecg Records
        diagnostic_attachments:
          items:
            $ref: '#/components/schemas/DiagnosticAttachment'
          type: array
          title: Diagnostic Attachments
        reader_role:
          $ref: '#/components/schemas/ReaderRole'
        workflow_status:
          $ref: '#/components/schemas/AdjudicationWorkflowStatus'
        my_assessment:
          anyOf:
          - $ref: '#/components/schemas/ReaderAssessment'
          - type: 'null'
        peer_assessment_completed:
          type: boolean
          title: Peer Assessment Completed
          default: false
        peer_assessment:
          anyOf:
          - $ref: '#/components/schemas/ReaderAssessment'
          - type: 'null'
        chair_determination:
          anyOf:
          - $ref: '#/components/schemas/ChairAdjudicationDetermination'
          - type: 'null'
        is_discordant:
          type: boolean
          title: Is Discordant
          default: false
        discordance_reasons:
          items:
            type: string
          type: array
          title: Discordance Reasons
        final_consensus_classification:
          anyOf:
          - $ref: '#/components/schemas/AdjudicationClassification'
          - type: 'null'
        final_adjudicated_onset_date:
          anyOf:
          - type: string
          - type: 'null'
          title: Final Adjudicated Onset Date
        dossier_digest:
          type: string
          title: Dossier Digest
          default: ''
      type: object
      required:
      - dossier_id
      - event_id
      - study_id
      - masked_site_id
      - masked_subject_id
      - candidate_event_type
      - reported_term
      - verbatim_description
      - reported_onset_date
      - medical_narrative
      - is_deidentified
      - bundled_forms
      - lab_panels
      - ecg_records
      - diagnostic_attachments
      - reader_role
      - workflow_status
      title: DossierBlindedView
      description: Blinded view of a patient event dossier presented to a specific reader role.
    DossierDetailResponseDTO:
      properties:
        dossier_id:
          type: string
          title: Dossier Id
        event_id:
          type: string
          title: Event Id
        study_id:
          type: string
          title: Study Id
        site_id:
          type: string
          title: Site Id
        masked_site_id:
          type: string
          title: Masked Site Id
        subject_id:
          type: string
          title: Subject Id
        masked_subject_id:
          type: string
          title: Masked Subject Id
        candidate_event_type:
          type: string
          title: Candidate Event Type
        reported_term:
          type: string
          title: Reported Term
        verbatim_description:
          type: string
          title: Verbatim Description
        reported_onset_date:
          type: string
          title: Reported Onset Date
        medical_narrative:
          type: string
          title: Medical Narrative
        is_deidentified:
          type: boolean
          title: Is Deidentified
        bundled_forms:
          items:
            additionalProperties: true
            type: object
          type: array
          title: Bundled Forms
        lab_panels:
          items:
            additionalProperties: true
            type: object
          type: array
          title: Lab Panels
        ecg_records:
          items:
            additionalProperties: true
            type: object
          type: array
          title: Ecg Records
        diagnostic_attachments:
          items:
            additionalProperties: true
            type: object
          type: array
          title: Diagnostic Attachments
        redaction_summary:
          additionalProperties:
            type: integer
          type: object
          title: Redaction Summary
        reader_1_id:
          type: string
          title: Reader 1 Id
        reader_1_name:
          type: string
          title: Reader 1 Name
        reader_1_status:
          type: string
          title: Reader 1 Status
        reader_1_assessment:
          anyOf:
          - additionalProperties: true
            type: object
          - type: 'null'
          title: Reader 1 Assessment
        reader_2_id:
          type: string
          title: Reader 2 Id
        reader_2_name:
          type: string
          title: Reader 2 Name
        reader_2_status:
          type: string
          title: Reader 2 Status
        reader_2_assessment:
          anyOf:
          - additionalProperties: true
            type: object
          - type: 'null'
          title: Reader 2 Assessment
        chair_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Chair Id
        chair_name:
          anyOf:
          - type: string
          - type: 'null'
          title: Chair Name
        chair_determination:
          anyOf:
          - additionalProperties: true
            type: object
          - type: 'null'
          title: Chair Determination
        workflow_status:
          type: string
          title: Workflow Status
        is_discordant:
          type: boolean
          title: Is Discordant
        discordance_reasons:
          items:
            type: string
          type: array
          title: Discordance Reasons
        final_consensus_classification:
          anyOf:
          - type: string
          - type: 'null'
          title: Final Consensus Classification
        final_consensus_subtype:
          anyOf:
          - type: string
          - type: 'null'
          title: Final Consensus Subtype
        final_adjudicated_onset_date:
          anyOf:
          - type: string
          - type: 'null'
          title: Final Adjudicated Onset Date
        binding_determination_source:
          anyOf:
          - type: string
          - type: 'null'
          title: Binding Determination Source
        adjudication_rationale:
          anyOf:
          - type: string
          - type: 'null'
          title: Adjudication Rationale
        electronic_signature:
          anyOf:
          - additionalProperties: true
            type: object
          - type: 'null'
          title: Electronic Signature
        edc_writeback_status:
          type: string
          title: Edc Writeback Status
        dossier_digest:
          type: string
          title: Dossier Digest
        created_at:
          type: string
          title: Created At
        version_index:
          type: integer
          title: Version Index
      type: object
      required:
      - dossier_id
      - event_id
      - study_id
      - site_id
      - masked_site_id
      - subject_id
      - masked_subject_id
      - candidate_event_type
      - reported_term
      - verbatim_description
      - reported_onset_date
      - medical_narrative
      - is_deidentified
      - bundled_forms
      - lab_panels
      - ecg_records
      - diagnostic_attachments
      - redaction_summary
      - reader_1_id
      - reader_1_name
      - reader_1_status
      - reader_1_assessment
      - reader_2_id
      - reader_2_name
      - reader_2_status
      - reader_2_assessment
      - chair_id
      - chair_name
      - chair_determination
      - workflow_status
      - is_discordant
      - discordance_reasons
      - final_consensus_classification
      - final_consensus_subtype
      - final_adjudicated_onset_date
      - binding_determination_source
      - adjudication_rationale
      - electronic_signature
      - edc_writeback_status
      - dossier_digest
      - created_at
      - version_index
      title: DossierDetailResponseDTO
      description: Complete dossier representation returned to authorized callers.
    DossierPackageCreateDTO:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Target protocol study ID
        site_id:
          type: string
          title: Site Id
          description: Investigational site ID
        subject_id:
          type: string
          title: Subject Id
          description: Subject UUID
        event_id:
          type: string
          title: Event Id
          description: Adverse event or clinical observation ID
        candidate_event_type:
          $ref: '#/components/schemas/EndpointCategory'
          description: Endpoint category (e.g. MACE, STROKE, MI)
        reported_term:
          type: string
          title: Reported Term
          description: Reported event term
        verbatim_description:
          type: string
          title: Verbatim Description
          description: Verbatim investigator clinical description
          default: ''
        reported_onset_date:
          type: string
          title: Reported Onset Date
          description: Reported onset date (ISO 8601 / DTC)
        medical_narrative:
          type: string
          title: Medical Narrative
          description: Detailed clinical narrative
          default: ''
        bundled_forms:
          items:
            $ref: '#/components/schemas/BundledEcrfForm'
          type: array
          title: Bundled Forms
        lab_panels:
          items:
            $ref: '#/components/schemas/LabPanelItem'
          type: array
          title: Lab Panels
        ecg_records:
          items:
            $ref: '#/components/schemas/ECGRecord'
          type: array
          title: Ecg Records
        diagnostic_attachments:
          items:
            $ref: '#/components/schemas/DiagnosticAttachment'
          type: array
          title: Diagnostic Attachments
        reader_1_id:
          type: string
          title: Reader 1 Id
          default: reader_01
        reader_1_name:
          type: string
          title: Reader 1 Name
          default: Dr. Primary Reader 1
        reader_2_id:
          type: string
          title: Reader 2 Id
          default: reader_02
        reader_2_name:
          type: string
          title: Reader 2 Name
          default: Dr. Primary Reader 2
        chair_id:
          type: string
          title: Chair Id
          default: chair_adjudicator
        chair_name:
          type: string
          title: Chair Name
          default: Dr. Adjudication Committee Chair
      type: object
      required:
      - study_id
      - site_id
      - subject_id
      - event_id
      - candidate_event_type
      - reported_term
      - reported_onset_date
      title: DossierPackageCreateDTO
      description: Payload to compile and package a new candidate clinical event dossier.
    ECGRecord:
      properties:
        ecg_id:
          type: string
          title: Ecg Id
          description: Unique ECG record identifier
        timestamp:
          type: string
          title: Timestamp
          description: Shifted timestamp of ECG acquisition
        rhythm:
          type: string
          title: Rhythm
          description: Cardiac rhythm classification
          default: Normal sinus rhythm
        st_elevation_leads:
          items:
            type: string
          type: array
          title: St Elevation Leads
          description: Leads exhibiting diagnostic ST elevation (e.g. V1-V4)
        st_depression_leads:
          items:
            type: string
          type: array
          title: St Depression Leads
          description: Leads exhibiting ST depression
        t_wave_inversion_leads:
          items:
            type: string
          type: array
          title: T Wave Inversion Leads
          description: Leads exhibiting T wave inversion
        q_wave_leads:
          items:
            type: string
          type: array
          title: Q Wave Leads
          description: Leads exhibiting pathological Q waves
        lbbb_present:
          type: boolean
          title: Lbbb Present
          description: New or presumed new Left Bundle Branch Block
          default: false
        qtcf_ms:
          anyOf:
          - type: number
          - type: 'null'
          title: Qtcf Ms
          description: Fridericia-corrected QT interval in milliseconds
        interpretation:
          anyOf:
          - type: string
          - type: 'null'
          title: Interpretation
          description: Summary cardiologist interpretation
      type: object
      required:
      - ecg_id
      - timestamp
      title: ECGRecord
      description: 12-Lead Electrocardiogram diagnostic findings.
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
          $ref: '#/components/schemas/EISFTaxonomyCategoryEnum'
          description: DIA taxonomy category
        title:
          type: string
          title: Title
          description: Human-readable document title
        version:
          type: string
          title: Version
          description: Document version string
          default: '1.0'
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
          - type: 'null'
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
      description: 'eISF regulatory binder document metadata record.


        Requirements: PRD-SYS-001'
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
      description: 'DIA eISF / Regulatory Binder document taxonomy categories.


        Requirements: PRD-SYS-001'
    ETMFCompletenessWebhookPayload:
      properties:
        study_id:
          type: string
          title: Study Id
        site_id:
          anyOf:
          - type: string
          - type: 'null'
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
    EdcCasebookWritebackResult:
      properties:
        dossier_id:
          type: string
          title: Dossier Id
        subject_id:
          type: string
          title: Subject Id
        study_id:
          type: string
          title: Study Id
        event_id:
          type: string
          title: Event Id
        observation_ids_updated:
          items:
            type: string
          type: array
          title: Observation Ids Updated
        adjudication_status:
          type: string
          title: Adjudication Status
          default: CONFIRMED
        adjudicated_term:
          type: string
          title: Adjudicated Term
        adjudication_date:
          type: string
          title: Adjudication Date
        signature_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Signature Id
        is_locked:
          type: boolean
          title: Is Locked
          default: true
        written_at:
          type: string
          title: Written At
        status:
          type: string
          title: Status
          default: SUCCESS
      type: object
      required:
      - dossier_id
      - subject_id
      - study_id
      - event_id
      - adjudicated_term
      - adjudication_date
      title: EdcCasebookWritebackResult
      description: Result of bi-directional synchronization back to EDC casebook.
    EndpointCategory:
      type: string
      enum:
      - MACE
      - MYOCARDIAL_INFARCTION
      - STROKE
      - CARDIOVASCULAR_MORTALITY
      - ALL_CAUSE_MORTALITY
      - HEART_FAILURE_HOSPITALIZATION
      - STENT_THROMBOSIS
      - VENOUS_THROMBOEMBOLISM
      - DISEASE_PROGRESSION
      - OTHER
      title: EndpointCategory
      description: Critical clinical endpoint categories subject to blinded adjudication.
    ExcursionEvaluationResponse:
      properties:
        id:
          anyOf:
          - type: string
          - type: 'null'
          title: Id
        study_id:
          type: string
          title: Study Id
        logger_id:
          type: string
          title: Logger Id
        entity_type:
          type: string
          title: Entity Type
        entity_id:
          type: string
          title: Entity Id
        evaluation_status:
          type: string
          title: Evaluation Status
        mean_kinetic_temperature_celsius:
          type: number
          title: Mean Kinetic Temperature Celsius
        arithmetic_mean_celsius:
          type: number
          title: Arithmetic Mean Celsius
        min_temperature_celsius:
          type: number
          title: Min Temperature Celsius
        max_temperature_celsius:
          type: number
          title: Max Temperature Celsius
        cumulative_hours_above_upper:
          type: number
          title: Cumulative Hours Above Upper
        cumulative_hours_below_lower:
          type: number
          title: Cumulative Hours Below Lower
        total_duration_hours:
          type: number
          title: Total Duration Hours
        readings_count:
          type: integer
          title: Readings Count
        hard_boundary_breached:
          type: boolean
          title: Hard Boundary Breached
        mkt_breached:
          type: boolean
          title: Mkt Breached
        cumulative_time_breached:
          type: boolean
          title: Cumulative Time Breached
        quarantine_triggered:
          type: boolean
          title: Quarantine Triggered
        quarantined_kit_count:
          type: integer
          title: Quarantined Kit Count
        excursions:
          items:
            $ref: '#/components/schemas/DiscreteExcursionResponse'
          type: array
          title: Excursions
        evaluated_at:
          type: string
          format: date-time
          title: Evaluated At
        evaluated_by:
          type: string
          title: Evaluated By
        summary_notes:
          anyOf:
          - type: string
          - type: 'null'
          title: Summary Notes
      type: object
      required:
      - study_id
      - logger_id
      - entity_type
      - entity_id
      - evaluation_status
      - mean_kinetic_temperature_celsius
      - arithmetic_mean_celsius
      - min_temperature_celsius
      - max_temperature_celsius
      - cumulative_hours_above_upper
      - cumulative_hours_below_lower
      - total_duration_hours
      - readings_count
      - hard_boundary_breached
      - mkt_breached
      - cumulative_time_breached
      - quarantine_triggered
      - quarantined_kit_count
      - evaluated_at
      - evaluated_by
      title: ExcursionEvaluationResponse
      description: Detailed response schema for an excursion evaluation and MKT analysis.
    ExportBundleRequest:
      properties:
        study_id:
          type: string
          title: Study Id
        format:
          type: string
          title: Format
          description: 'Target format: dataset_json, xpt_v5, xpt_v8, odm_xml, csv, csv_zip'
          default: dataset_json
        domains:
          items:
            type: string
          type: array
          title: Domains
          description: List of SDTM domains to include (e.g. DM, AE, VS, LB, MH, CM)
        datasets:
          items:
            type: string
          type: array
          title: Datasets
          description: List of ADaM datasets to include (e.g. ADSL, ADAE, ADVS)
        site_ids:
          items:
            type: string
          type: array
          title: Site Ids
          description: Optional list of site IDs to filter by
        cohorts:
          items:
            type: string
          type: array
          title: Cohorts
          description: Optional list of subject cohorts/arms to filter by
        privacy_profile:
          type: string
          title: Privacy Profile
          description: 'Privacy policy: SAFE_HARBOR, LIMITED_DATA_SET, GDPR_PSEUDONYMIZED, UNRESTRICTED'
          default: SAFE_HARBOR
        xpt_version:
          type: string
          title: Xpt Version
          description: 'SAS Transport version: v5 or v8'
          default: v5
        include_audit_trail:
          type: boolean
          title: Include Audit Trail
          description: Whether to include GxP audit records in ODM-XML / CSV
          default: true
        metadata_version_oid:
          type: string
          title: Metadata Version Oid
          description: ODM / Dataset-JSON metadata version OID
          default: MDV.001
        salt:
          anyOf:
          - type: string
          - type: 'null'
          title: Salt
          description: Optional HMAC salt override for deterministic de-identification
      type: object
      required:
      - study_id
      title: ExportBundleRequest
      description: Pydantic schema representing clinical dataset export request parameters.
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
          - type: 'null'
          title: Visit Id
        form_id:
          type: string
          title: Form Id
        protocol_version:
          anyOf:
          - type: string
          - type: 'null'
          title: Protocol Version
        payload:
          anyOf:
          - additionalProperties: true
            type: object
          - type: 'null'
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
          - type: 'null'
          title: Site Id
        subject_id:
          type: string
          title: Subject Id
        visit_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Visit Id
        form_id:
          type: string
          title: Form Id
        status:
          $ref: '#/components/schemas/FormSubmissionStatusEnum'
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
          - type: 'null'
          title: Signature Manifest
        protocol_version:
          anyOf:
          - type: string
          - type: 'null'
          title: Protocol Version
        payload:
          anyOf:
          - additionalProperties: true
            type: object
          - type: 'null'
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
          - type: 'null'
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
      description: 'Request payload to generate a temporary auditor access token.


        Requirements: PRD-SYS-001'
    GradingScaleEnum:
      type: string
      enum:
      - CTCAE_V5
      - DAIDS_V2_1
      title: GradingScaleEnum
      description: Toxicity grading system standard.
    IPKitCreateRequest:
      properties:
        study_id:
          type: string
          title: Study Id
        kit_number:
          type: string
          title: Kit Number
        kit_type:
          type: string
          title: Kit Type
        description:
          anyOf:
          - type: string
          - type: 'null'
          title: Description
        lot_number:
          anyOf:
          - type: string
          - type: 'null'
          title: Lot Number
        expiration_date:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Expiration Date
        node_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Node Id
      additionalProperties: false
      type: object
      required:
      - study_id
      - kit_number
      - kit_type
      title: IPKitCreateRequest
      description: Payload to catalog an investigational product kit.
    IPKitResponse:
      properties:
        id:
          type: string
          title: Id
        study_id:
          type: string
          title: Study Id
        kit_number:
          type: string
          title: Kit Number
        kit_type:
          type: string
          title: Kit Type
        description:
          anyOf:
          - type: string
          - type: 'null'
          title: Description
        status:
          type: string
          title: Status
        lot_number:
          anyOf:
          - type: string
          - type: 'null'
          title: Lot Number
        expiration_date:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Expiration Date
        node_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Node Id
        quarantine_reason:
          anyOf:
          - type: string
          - type: 'null'
          title: Quarantine Reason
        quarantined_at:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Quarantined At
        quarantined_by:
          anyOf:
          - type: string
          - type: 'null'
          title: Quarantined By
      type: object
      required:
      - id
      - study_id
      - kit_number
      - kit_type
      - status
      title: IPKitResponse
      description: Response schema for an investigational product kit.
    ImpactAnalysisRequest:
      properties:
        dictionary_type:
          $ref: '#/components/schemas/DictTypeEnum'
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
          $ref: '#/components/schemas/DictTypeEnum'
        new_version:
          type: string
          title: New Version
        metrics:
          $ref: '#/components/schemas/ImpactMetrics'
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
          - type: 'null'
          title: Verbatim Terms Affected
        coded_terms_affected:
          anyOf:
          - type: integer
          - type: 'null'
          title: Coded Terms Affected
        uncodable_terms:
          anyOf:
          - type: integer
          - type: 'null'
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
          $ref: '#/components/schemas/JobStatusEnum'
        started_at:
          type: string
          format: date-time
          title: Started At
        completed_at:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Completed At
        progress_percentage:
          anyOf:
          - type: integer
          - type: 'null'
          title: Progress Percentage
        records_imported:
          anyOf:
          - type: integer
          - type: 'null'
          title: Records Imported
        errors_encountered:
          anyOf:
          - type: integer
          - type: 'null'
          title: Errors Encountered
      type: object
      required:
      - job_id
      - dictionary_type
      - version
      - status
      - started_at
      title: JobStatusResponse
    KitQuarantineRequest:
      properties:
        study_id:
          type: string
          title: Study Id
        kit_numbers:
          items:
            type: string
          type: array
          title: Kit Numbers
        quarantine_reason:
          type: string
          title: Quarantine Reason
      type: object
      required:
      - study_id
      - kit_numbers
      - quarantine_reason
      title: KitQuarantineRequest
      description: Payload to manually quarantine kits with Part 11 justification.
    KitReleaseQuarantineRequest:
      properties:
        study_id:
          type: string
          title: Study Id
        kit_numbers:
          items:
            type: string
          type: array
          title: Kit Numbers
        qa_release_justification:
          type: string
          title: Qa Release Justification
      type: object
      required:
      - study_id
      - kit_numbers
      - qa_release_justification
      title: KitReleaseQuarantineRequest
      description: Payload for QA disposition release of quarantined kits.
    LabBatchIngestResult:
      properties:
        batch_id:
          type: string
          title: Batch Id
        study_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Study Id
        format:
          type: string
          title: Format
        status:
          type: string
          title: Status
        total_processed:
          type: integer
          title: Total Processed
          default: 0
        ingested_count:
          type: integer
          title: Ingested Count
          default: 0
        out_of_range_count:
          type: integer
          title: Out Of Range Count
          default: 0
        critical_alerts:
          type: integer
          title: Critical Alerts
          default: 0
        queries_raised:
          type: integer
          title: Queries Raised
          default: 0
        staged_aes_count:
          type: integer
          title: Staged Aes Count
          default: 0
        uncalibrated_safeguards:
          type: integer
          title: Uncalibrated Safeguards
          default: 0
        toxicity_grades:
          additionalProperties:
            type: integer
          type: object
          title: Toxicity Grades
        errors:
          items:
            additionalProperties: true
            type: object
          type: array
          title: Errors
        created_at:
          type: string
          format: date-time
          title: Created At
      type: object
      required:
      - batch_id
      - format
      - status
      title: LabBatchIngestResult
      description: Summary of batch ingestion processing.
    LabPanelItem:
      properties:
        test_name:
          type: string
          title: Test Name
          description: Laboratory analyte name (e.g. Troponin I, CK-MB)
        loinc_code:
          anyOf:
          - type: string
          - type: 'null'
          title: Loinc Code
          description: Standard LOINC code
        result_value:
          anyOf:
          - type: number
          - type: string
          title: Result Value
          description: Observed numeric or character result
        result_unit:
          type: string
          title: Result Unit
          description: Unit of measurement (e.g. ng/mL, U/L)
        reference_low:
          anyOf:
          - type: number
          - type: 'null'
          title: Reference Low
          description: Lower limit of normal
        reference_high:
          anyOf:
          - type: number
          - type: 'null'
          title: Reference High
          description: Upper limit of normal / 99th percentile URL
        is_out_of_range:
          type: boolean
          title: Is Out Of Range
          description: Flag indicating value exceeds reference limits
          default: false
        toxicity_grade:
          anyOf:
          - type: integer
          - type: 'null'
          title: Toxicity Grade
          description: Computed CTCAE / DAIDS toxicity grade (0-4)
        sample_timestamp:
          anyOf:
          - type: string
          - type: 'null'
          title: Sample Timestamp
          description: Relative shifted ISO timestamp of specimen collection
      type: object
      required:
      - test_name
      - result_value
      - result_unit
      title: LabPanelItem
      description: De-identified laboratory analyte finding pertinent to endpoint review.
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
          - type: 'null'
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
          - type: 'null'
          title: Age Low
        age_high:
          anyOf:
          - type: number
          - type: 'null'
          title: Age High
        low_bound:
          anyOf:
          - type: number
          - type: 'null'
          title: Low Bound
        high_bound:
          anyOf:
          - type: number
          - type: 'null'
          title: High Bound
        critical_low:
          anyOf:
          - type: number
          - type: 'null'
          title: Critical Low
        critical_high:
          anyOf:
          - type: number
          - type: 'null'
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
          - type: 'null'
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
          - type: 'null'
          title: Age Low
        age_high:
          anyOf:
          - type: number
          - type: 'null'
          title: Age High
        low_bound:
          anyOf:
          - type: number
          - type: 'null'
          title: Low Bound
        high_bound:
          anyOf:
          - type: number
          - type: 'null'
          title: High Bound
        critical_low:
          anyOf:
          - type: number
          - type: 'null'
          title: Critical Low
        critical_high:
          anyOf:
          - type: number
          - type: 'null'
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
          - type: 'null'
          title: Study Id
        test_code:
          anyOf:
          - type: string
          - type: 'null'
          title: Test Code
        test_name:
          anyOf:
          - type: string
          - type: 'null'
          title: Test Name
        source:
          anyOf:
          - type: string
          - type: 'null'
          title: Source
        site_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Site Id
        unit:
          anyOf:
          - type: string
          - type: 'null'
          title: Unit
        normalized_unit:
          anyOf:
          - type: string
          - type: 'null'
          title: Normalized Unit
        sex_applicability:
          anyOf:
          - type: string
          - type: 'null'
          title: Sex Applicability
        age_low:
          anyOf:
          - type: number
          - type: 'null'
          title: Age Low
        age_high:
          anyOf:
          - type: number
          - type: 'null'
          title: Age High
        low_bound:
          anyOf:
          - type: number
          - type: 'null'
          title: Low Bound
        high_bound:
          anyOf:
          - type: number
          - type: 'null'
          title: High Bound
        critical_low:
          anyOf:
          - type: number
          - type: 'null'
          title: Critical Low
        critical_high:
          anyOf:
          - type: number
          - type: 'null'
          title: Critical High
      type: object
      title: LabReferenceRangeUpdate
      description: Pydantic schema for updating a reference range.
    LabToxicityEvaluateRequest:
      properties:
        test_code:
          type: string
          title: Test Code
          description: Standardized laboratory test code (e.g. 'ALT', 'CREAT', 'ANC')
        test_name:
          anyOf:
          - type: string
          - type: 'null'
          title: Test Name
          description: Optional descriptive test name
        value:
          type: number
          title: Value
          description: Observed numeric test measurement
        unit:
          type: string
          title: Unit
          description: Reporting unit of measurement
        ref_low:
          anyOf:
          - type: number
          - type: 'null'
          title: Ref Low
          description: Lower limit of demographic normal range (LLN)
        ref_high:
          anyOf:
          - type: number
          - type: 'null'
          title: Ref High
          description: Upper limit of demographic normal range (ULN)
        baseline_value:
          anyOf:
          - type: number
          - type: 'null'
          title: Baseline Value
          description: Optional subject baseline value for acute delta calculation
        previous_value:
          anyOf:
          - type: number
          - type: 'null'
          title: Previous Value
          description: Optional previous observation value
        grading_scale:
          $ref: '#/components/schemas/GradingScaleEnum'
          description: 'Target grading scale: ''CTCAE_V5'' or ''DAIDS_V2_1'''
          default: CTCAE_V5
        alt_value:
          anyOf:
          - type: number
          - type: 'null'
          title: Alt Value
          description: Co-analyte ALT value for Hy's Law evaluation
        ast_value:
          anyOf:
          - type: number
          - type: 'null'
          title: Ast Value
          description: Co-analyte AST value for Hy's Law evaluation
        bili_value:
          anyOf:
          - type: number
          - type: 'null'
          title: Bili Value
          description: Co-analyte Total Bilirubin value for Hy's Law evaluation
        alkp_value:
          anyOf:
          - type: number
          - type: 'null'
          title: Alkp Value
          description: Co-analyte Alkaline Phosphatase value for Hy's Law evaluation
      type: object
      required:
      - test_code
      - value
      - unit
      title: LabToxicityEvaluateRequest
      description: Request payload to simulate/evaluate laboratory toxicity grading in real time.
    LabToxicityEvaluateResponse:
      properties:
        test_code:
          type: string
          title: Test Code
          description: Analyzed laboratory test code
        test_name:
          type: string
          title: Test Name
          description: Descriptive test name
        observed_value:
          type: number
          title: Observed Value
          description: Evaluated measurement value
        unit:
          type: string
          title: Unit
          description: Unit of measurement
        ctcae_grade:
          type: integer
          title: Ctcae Grade
          description: Computed severity grade integer (0 to 4)
        grading_scale:
          type: string
          title: Grading Scale
          description: Grading scale applied
        toxicity_description:
          type: string
          title: Toxicity Description
          description: Detailed clinical severity criterion description
        is_critical_ae_trigger:
          type: boolean
          title: Is Critical Ae Trigger
          description: True if Grade >= 3 triggering automated AE drafting
        is_hys_law_warning:
          type: boolean
          title: Is Hys Law Warning
          description: True if Hy's Law criteria for severe DILI are met
        is_delta_check_alert:
          type: boolean
          title: Is Delta Check Alert
          description: True if acute delta threshold exceeded
        delta_percentage:
          anyOf:
          - type: number
          - type: 'null'
          title: Delta Percentage
          description: Calculated percentage deviation from baseline
      type: object
      required:
      - test_code
      - test_name
      - observed_value
      - unit
      - ctcae_grade
      - grading_scale
      - toxicity_description
      - is_critical_ae_trigger
      - is_hys_law_warning
      - is_delta_check_alert
      title: LabToxicityEvaluateResponse
      description: Response payload with computed CTCAE / DAIDS toxicity grade and alert flags.
    LocalLabCertificationCreate:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Unique protocol study identifier
        site_id:
          type: string
          title: Site Id
          description: Investigator site identifier
        lab_name:
          type: string
          title: Lab Name
          description: Laboratory facility name
        lab_identifier:
          anyOf:
          - type: string
          - type: 'null'
          title: Lab Identifier
          description: Optional facility identifier
        certification_number:
          type: string
          title: Certification Number
          description: CLIA, CAP, or ISO certification number
        accreditation_body:
          type: string
          title: Accreditation Body
          description: Accrediting agency (e.g. CLIA, CAP, ISO 15189)
        effective_date:
          type: string
          format: date-time
          title: Effective Date
          description: Certification effective begin date
        expiration_date:
          type: string
          format: date-time
          title: Expiration Date
          description: Certification expiration end date
        director_name:
          anyOf:
          - type: string
          - type: 'null'
          title: Director Name
          description: Name of laboratory director
        is_accredited:
          type: boolean
          title: Is Accredited
          description: Accreditation status flag
          default: true
        notes:
          anyOf:
          - type: string
          - type: 'null'
          title: Notes
          description: Operational notes
        reason_for_change:
          type: string
          title: Reason For Change
          description: Mandatory GxP 21 CFR Part 11 justification reason
      type: object
      required:
      - study_id
      - site_id
      - lab_name
      - certification_number
      - accreditation_body
      - effective_date
      - expiration_date
      - reason_for_change
      title: LocalLabCertificationCreate
      description: 'Payload to register a new local laboratory accreditation or certification.


        Requirements: PRD-LAB-001'
    LocalLabCertificationResponse:
      properties:
        id:
          type: string
          title: Id
          description: Unique database identifier
        study_id:
          type: string
          title: Study Id
          description: Unique protocol study identifier
        site_id:
          type: string
          title: Site Id
          description: Investigator site identifier
        lab_name:
          type: string
          title: Lab Name
          description: Laboratory facility name
        lab_identifier:
          anyOf:
          - type: string
          - type: 'null'
          title: Lab Identifier
          description: Optional facility identifier
        certification_number:
          type: string
          title: Certification Number
          description: Certification number
        accreditation_body:
          type: string
          title: Accreditation Body
          description: Accreditation body
        effective_date:
          type: string
          format: date-time
          title: Effective Date
          description: Effective begin date
        expiration_date:
          type: string
          format: date-time
          title: Expiration Date
          description: Expiration end date
        status:
          $ref: '#/components/schemas/CertificationStatusEnum'
          description: Status
        director_name:
          anyOf:
          - type: string
          - type: 'null'
          title: Director Name
          description: Laboratory director name
        is_accredited:
          type: boolean
          title: Is Accredited
          description: Accredited status
        notes:
          anyOf:
          - type: string
          - type: 'null'
          title: Notes
          description: Notes
        version:
          type: integer
          title: Version
          description: Version index
        is_deleted:
          type: boolean
          title: Is Deleted
          description: Soft deletion flag
      type: object
      required:
      - id
      - study_id
      - site_id
      - lab_name
      - certification_number
      - accreditation_body
      - effective_date
      - expiration_date
      - status
      - is_accredited
      - version
      - is_deleted
      title: LocalLabCertificationResponse
      description: 'Response representing a local laboratory certification.


        Requirements: PRD-LAB-001'
    LocalLabCertificationUpdate:
      properties:
        lab_name:
          anyOf:
          - type: string
          - type: 'null'
          title: Lab Name
          description: Updated facility name
        lab_identifier:
          anyOf:
          - type: string
          - type: 'null'
          title: Lab Identifier
          description: Updated facility identifier
        certification_number:
          anyOf:
          - type: string
          - type: 'null'
          title: Certification Number
          description: Updated certification number
        accreditation_body:
          anyOf:
          - type: string
          - type: 'null'
          title: Accreditation Body
          description: Updated accrediting agency
        effective_date:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Effective Date
          description: Updated effective date
        expiration_date:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Expiration Date
          description: Updated expiration date
        status:
          anyOf:
          - $ref: '#/components/schemas/CertificationStatusEnum'
          - type: 'null'
          description: Updated operational status
        director_name:
          anyOf:
          - type: string
          - type: 'null'
          title: Director Name
          description: Updated laboratory director
        is_accredited:
          anyOf:
          - type: boolean
          - type: 'null'
          title: Is Accredited
          description: Updated accreditation flag
        notes:
          anyOf:
          - type: string
          - type: 'null'
          title: Notes
          description: Updated operational notes
        reason_for_change:
          type: string
          title: Reason For Change
          description: Mandatory GxP 21 CFR Part 11 justification reason
      type: object
      required:
      - reason_for_change
      title: LocalLabCertificationUpdate
      description: 'Payload to update an existing local laboratory certification.


        Requirements: PRD-LAB-001'
    LocalLabCertificationVerifyRequest:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Unique protocol study identifier
        site_id:
          type: string
          title: Site Id
          description: Target site identifier
        observation_date:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Observation Date
          description: Observation collection date (defaults to current time)
      type: object
      required:
      - study_id
      - site_id
      title: LocalLabCertificationVerifyRequest
      description: Request to verify local laboratory certification validity.
    LocalLabCertificationVerifyResponse:
      properties:
        is_valid:
          type: boolean
          title: Is Valid
          description: True if site has active, unexpired certification
        status:
          type: string
          title: Status
          description: Certification status code
        site_id:
          type: string
          title: Site Id
          description: Site identifier
        study_id:
          type: string
          title: Study Id
          description: Study identifier
        certification_number:
          anyOf:
          - type: string
          - type: 'null'
          title: Certification Number
          description: Certification number if found
        accreditation_body:
          anyOf:
          - type: string
          - type: 'null'
          title: Accreditation Body
          description: Accreditation body if found
        expiration_date:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Expiration Date
          description: Expiration date if found
        days_until_expiration:
          anyOf:
          - type: integer
          - type: 'null'
          title: Days Until Expiration
          description: Days remaining until expiration
        uncalibrated_safeguard_triggered:
          type: boolean
          title: Uncalibrated Safeguard Triggered
          description: True if safeguard fired due to expired or missing certification
          default: false
        message:
          type: string
          title: Message
          description: Human-readable status summary
      type: object
      required:
      - is_valid
      - status
      - site_id
      - study_id
      - message
      title: LocalLabCertificationVerifyResponse
      description: Response from local lab certification verification.
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
          $ref: '#/components/schemas/SyncBlockDetails'
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
      - STUDY
      - TRIAL
      - SITE
      - SUBJECT
      - VISIT
      - FORM
      - ITEM_GROUP
      - FIELD
      title: LockScopeEnum
      description: Granular lock scope boundaries.
    LockStatusEnum:
      type: string
      enum:
      - UNLOCKED
      - FROZEN
      - LOCKED
      - HARD_LOCK
      - SOFT_LOCK
      title: LockStatusEnum
      description: Data lock lifecycle status.
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
    LoggerIngestionRequest:
      properties:
        study_id:
          type: string
          title: Study Id
        logger_id:
          type: string
          title: Logger Id
        file_format:
          type: string
          title: File Format
          default: CSV
        raw_payload:
          type: string
          title: Raw Payload
        filename:
          type: string
          title: Filename
        entity_type:
          type: string
          title: Entity Type
          default: SHIPMENT
        entity_id:
          type: string
          title: Entity Id
        stability_budget_override:
          anyOf:
          - $ref: '#/components/schemas/StabilityBudgetCreateRequest'
          - type: 'null'
      type: object
      required:
      - study_id
      - logger_id
      - raw_payload
      - filename
      - entity_id
      title: LoggerIngestionRequest
      description: Payload to upload and ingest a raw temperature logger file.
    LoincCatalogResponse:
      properties:
        total_count:
          type: integer
          title: Total Count
          description: Total mapped LOINC test codes
        items:
          items:
            $ref: '#/components/schemas/LoincMappingItem'
          type: array
          title: Items
          description: List of mapped LOINC analyte items
      type: object
      required:
      - total_count
      - items
      title: LoincCatalogResponse
      description: Response payload containing standardized LOINC mappings.
    LoincConvertRequest:
      properties:
        test_code:
          type: string
          title: Test Code
          description: Laboratory analyte code
        from_unit:
          type: string
          title: From Unit
          description: Source unit
        to_unit:
          type: string
          title: To Unit
          description: Target unit
        value:
          type: number
          title: Value
          description: Numeric value to convert
      type: object
      required:
      - test_code
      - from_unit
      - to_unit
      - value
      title: LoincConvertRequest
      description: Request to convert laboratory value between conventional and SI units.
    LoincConvertResponse:
      properties:
        test_code:
          type: string
          title: Test Code
          description: Analyte test code
        from_unit:
          type: string
          title: From Unit
          description: Original source unit
        to_unit:
          type: string
          title: To Unit
          description: Target converted unit
        original_value:
          type: number
          title: Original Value
          description: Original input value
        converted_value:
          type: number
          title: Converted Value
          description: Computed converted value
        factor:
          type: number
          title: Factor
          description: Multiplicative conversion factor
        offset:
          anyOf:
          - type: number
          - type: 'null'
          title: Offset
          description: Additive offset if applicable
        loinc_code:
          anyOf:
          - type: string
          - type: 'null'
          title: Loinc Code
          description: Associated LOINC code if resolved
      type: object
      required:
      - test_code
      - from_unit
      - to_unit
      - original_value
      - converted_value
      - factor
      title: LoincConvertResponse
      description: Response from mathematical laboratory unit conversion.
    LoincMappingItem:
      properties:
        test_code:
          type: string
          title: Test Code
          description: Standardized internal analyte code
        test_name:
          type: string
          title: Test Name
          description: Analyte name
        loinc_code:
          type: string
          title: Loinc Code
          description: Standard LOINC code
        standard_unit:
          type: string
          title: Standard Unit
          description: Standardized SI/UCUM unit
        category:
          type: string
          title: Category
          description: Clinical laboratory domain category
        molecular_weight:
          anyOf:
          - type: number
          - type: 'null'
          title: Molecular Weight
          description: Molecular weight (g/mol) for molar/mass conversions
      type: object
      required:
      - test_code
      - test_name
      - loinc_code
      - standard_unit
      - category
      title: LoincMappingItem
      description: LOINC dictionary mapping metadata item.
    LotExpiryExtensionRequest:
      properties:
        study_id:
          type: string
          title: Study Id
        lot_number:
          type: string
          title: Lot Number
        kit_type:
          type: string
          title: Kit Type
        extended_expiry_date:
          type: string
          format: date-time
          title: Extended Expiry Date
        extension_reason:
          type: string
          title: Extension Reason
        stability_protocol_reference:
          type: string
          title: Stability Protocol Reference
      additionalProperties: false
      type: object
      required:
      - study_id
      - lot_number
      - kit_type
      - extended_expiry_date
      - extension_reason
      - stability_protocol_reference
      title: LotExpiryExtensionRequest
      description: Payload to execute automated lot expiration date extension.
    LotExpiryExtensionResponse:
      properties:
        id:
          type: string
          title: Id
        study_id:
          type: string
          title: Study Id
        lot_number:
          type: string
          title: Lot Number
        kit_type:
          type: string
          title: Kit Type
        initial_expiry_date:
          type: string
          format: date-time
          title: Initial Expiry Date
        extended_expiry_date:
          type: string
          format: date-time
          title: Extended Expiry Date
        extension_reason:
          type: string
          title: Extension Reason
        stability_protocol_reference:
          type: string
          title: Stability Protocol Reference
        authorized_by:
          type: string
          title: Authorized By
        approval_status:
          type: string
          title: Approval Status
        affected_kits_count:
          type: integer
          title: Affected Kits Count
        relabeling_tasks_generated:
          type: integer
          title: Relabeling Tasks Generated
        created_at:
          type: string
          format: date-time
          title: Created At
      type: object
      required:
      - id
      - study_id
      - lot_number
      - kit_type
      - initial_expiry_date
      - extended_expiry_date
      - extension_reason
      - stability_protocol_reference
      - authorized_by
      - approval_status
      - affected_kits_count
      - relabeling_tasks_generated
      - created_at
      title: LotExpiryExtensionResponse
      description: Response schema for a lot expiration extension record.
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
            $ref: '#/components/schemas/MedDRAMatch'
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
          - type: 'null'
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
    MeetingQuorumRecordDTO:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Study ID
        charter_id:
          type: string
          title: Charter Id
          description: Charter ID
        meeting_date:
          type: string
          title: Meeting Date
          description: Meeting date (ISO 8601)
        attendees:
          items:
            additionalProperties:
              type: string
            type: object
          type: array
          title: Attendees
          description: 'Attendees: [{member_id, member_name, role}]'
        total_eligible_members:
          type: integer
          minimum: 1.0
          title: Total Eligible Members
        dossiers_reviewed:
          items:
            type: string
          type: array
          title: Dossiers Reviewed
        voting_results:
          additionalProperties:
            additionalProperties: true
            type: object
          type: object
          title: Voting Results
        meeting_minutes:
          type: string
          title: Meeting Minutes
          default: ''
        chair_signature:
          anyOf:
          - additionalProperties: true
            type: object
          - type: 'null'
          title: Chair Signature
      type: object
      required:
      - study_id
      - charter_id
      - meeting_date
      - attendees
      - total_eligible_members
      title: MeetingQuorumRecordDTO
      description: Payload to record committee meeting quorum and minutes.
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
          - type: 'null'
          title: Source Field
        target_field:
          anyOf:
          - type: string
          - type: 'null'
          title: Target Field
        default_value_string:
          anyOf:
          - type: string
          - type: 'null'
          title: Default Value String
        default_value_float:
          anyOf:
          - type: number
          - type: 'null'
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
          - type: 'null'
          title: Source Field
        target_field:
          anyOf:
          - type: string
          - type: 'null'
          title: Target Field
        default_value_string:
          anyOf:
          - type: string
          - type: 'null'
          title: Default Value String
        default_value_float:
          anyOf:
          - type: number
          - type: 'null'
          title: Default Value Float
      type: object
      required:
      - id
      - study_id
      - source_version
      - target_version
      - rule_type
      title: MigrationRuleResponse
    MktCalculationDirectRequest:
      properties:
        temperatures_celsius:
          items:
            type: number
          type: array
          minItems: 1
          title: Temperatures Celsius
        activation_energy_over_r:
          type: number
          exclusiveMinimum: 0.0
          title: Activation Energy Over R
          default: 10000.0
      type: object
      required:
      - temperatures_celsius
      title: MktCalculationDirectRequest
      description: Direct standalone MKT Arrhenius mathematical calculation payload.
    MktCalculationDirectResponse:
      properties:
        mean_kinetic_temperature_celsius:
          type: number
          title: Mean Kinetic Temperature Celsius
        arithmetic_mean_celsius:
          type: number
          title: Arithmetic Mean Celsius
        min_temperature_celsius:
          type: number
          title: Min Temperature Celsius
        max_temperature_celsius:
          type: number
          title: Max Temperature Celsius
        readings_count:
          type: integer
          title: Readings Count
        formula_reference:
          type: string
          title: Formula Reference
          default: USP <1079> / PDA TR 39 Arrhenius Kinetic Model
      type: object
      required:
      - mean_kinetic_temperature_celsius
      - arithmetic_mean_celsius
      - min_temperature_celsius
      - max_temperature_celsius
      - readings_count
      title: MktCalculationDirectResponse
      description: Direct standalone MKT calculation result.
    MultiEchelonRollup:
      properties:
        study_id:
          type: string
          title: Study Id
        total_nodes_count:
          type: integer
          title: Total Nodes Count
        manufacturers_count:
          type: integer
          title: Manufacturers Count
        central_depots_count:
          type: integer
          title: Central Depots Count
        regional_depots_count:
          type: integer
          title: Regional Depots Count
        clinical_sites_count:
          type: integer
          title: Clinical Sites Count
        total_global_on_hand_stock:
          type: integer
          title: Total Global On Hand Stock
        total_global_in_transit_stock:
          type: integer
          title: Total Global In Transit Stock
        total_global_reserved_stock:
          type: integer
          title: Total Global Reserved Stock
        total_global_storage_capacity:
          type: integer
          title: Total Global Storage Capacity
        overall_capacity_utilization_pct:
          type: number
          title: Overall Capacity Utilization Pct
        hierarchy_tree:
          items:
            $ref: '#/components/schemas/SupplyHierarchyNode'
          type: array
          title: Hierarchy Tree
      type: object
      required:
      - study_id
      - total_nodes_count
      - manufacturers_count
      - central_depots_count
      - regional_depots_count
      - clinical_sites_count
      - total_global_on_hand_stock
      - total_global_in_transit_stock
      - total_global_reserved_stock
      - total_global_storage_capacity
      - overall_capacity_utilization_pct
      - hierarchy_tree
      title: MultiEchelonRollup
      description: Aggregated global and echelon-level supply metrics.
    ObservationCreate:
      properties:
        subject_id:
          type: string
          title: Subject Id
        study_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Study Id
        site_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Site Id
        visit_id:
          anyOf:
          - type: string
          - type: 'null'
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
          - type: 'null'
          title: Value
        value_string:
          anyOf:
          - type: string
          - type: 'null'
          title: Value String
        unit:
          anyOf:
          - type: string
          - type: 'null'
          title: Unit
        observation_date:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Observation Date
        lab_source:
          anyOf:
          - type: string
          - type: 'null'
          title: Lab Source
        lab_site_id:
          anyOf:
          - type: string
          - type: 'null'
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
          - type: 'null'
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
          - type: 'null'
          title: Value
        value_string:
          anyOf:
          - type: string
          - type: 'null'
          title: Value String
        unit:
          anyOf:
          - type: string
          - type: 'null'
          title: Unit
        normalized_value:
          anyOf:
          - type: number
          - type: 'null'
          title: Normalized Value
        normalized_unit:
          anyOf:
          - type: string
          - type: 'null'
          title: Normalized Unit
        is_outlier:
          type: boolean
          title: Is Outlier
        lab_source:
          anyOf:
          - type: string
          - type: 'null'
          title: Lab Source
        lab_site_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Lab Site Id
        lab_indicator:
          anyOf:
          - type: string
          - type: 'null'
          title: Lab Indicator
        lab_out_of_range:
          anyOf:
          - type: boolean
          - type: 'null'
          title: Lab Out Of Range
        matched_normal_bounds:
          anyOf:
          - type: string
          - type: 'null'
          title: Matched Normal Bounds
        range_indicator:
          anyOf:
          - type: string
          - type: 'null'
          title: Range Indicator
        is_out_of_range:
          anyOf:
          - type: boolean
          - type: 'null'
          title: Is Out Of Range
        reference_range_low:
          anyOf:
          - type: number
          - type: 'null'
          title: Reference Range Low
        reference_range_high:
          anyOf:
          - type: number
          - type: 'null'
          title: Reference Range High
        protocol_version_tag:
          anyOf:
          - type: string
          - type: 'null'
          title: Protocol Version Tag
        protocol_version_index:
          anyOf:
          - type: integer
          - type: 'null'
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
            $ref: '#/components/schemas/OfflineDeltaItem'
          type: array
          title: Deltas
          description: List of sync deltas to process
      type: object
      required:
      - client_batch_id
      - device_id
      - deltas
      title: OfflineBatchSyncRequest
      description: 'Request schema for offline batch delta sync.


        Requirements: PRD-SYS-001'
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
      description: 'Response schema for offline batch delta sync.


        Requirements: PRD-SYS-001'
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
      description: 'An individual sync delta item representing an entity mutation.


        Requirements: PRD-SYS-001'
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
      description: 'Request payload to scan text for HIPAA 18 PHI identifiers.


        Requirements: PRD-SYS-001'
    PackingManifestCreateRequest:
      properties:
        manifest_number:
          type: string
          title: Manifest Number
        serialized_kit_numbers:
          items:
            type: string
          type: array
          minItems: 1
          title: Serialized Kit Numbers
        lot_number:
          type: string
          title: Lot Number
        expiration_date:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Expiration Date
        container_serial_number:
          anyOf:
          - type: string
          - type: 'null'
          title: Container Serial Number
        data_logger_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Data Logger Id
        tamper_seal_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Tamper Seal Id
      type: object
      required:
      - manifest_number
      - serialized_kit_numbers
      - lot_number
      title: PackingManifestCreateRequest
      description: Payload to generate a packing manifest for an approved transfer order.
    PackingManifestResponse:
      properties:
        id:
          type: string
          title: Id
        study_id:
          type: string
          title: Study Id
        transfer_order_id:
          type: string
          title: Transfer Order Id
        manifest_number:
          type: string
          title: Manifest Number
        serialized_kit_numbers:
          items:
            type: string
          type: array
          title: Serialized Kit Numbers
        lot_number:
          type: string
          title: Lot Number
        expiration_date:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Expiration Date
        container_serial_number:
          anyOf:
          - type: string
          - type: 'null'
          title: Container Serial Number
        data_logger_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Data Logger Id
        tamper_seal_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Tamper Seal Id
        packed_by:
          type: string
          title: Packed By
        packed_at:
          type: string
          format: date-time
          title: Packed At
        verified_by:
          anyOf:
          - type: string
          - type: 'null'
          title: Verified By
        verified_at:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Verified At
      type: object
      required:
      - id
      - study_id
      - transfer_order_id
      - manifest_number
      - serialized_kit_numbers
      - lot_number
      - packed_by
      - packed_at
      title: PackingManifestResponse
      description: Response schema for packing manifests.
    PredictiveForecastRequest:
      properties:
        study_id:
          type: string
          title: Study Id
        site_id:
          type: string
          title: Site Id
        kit_type:
          type: string
          title: Kit Type
          default: DEFAULT
        horizon_days:
          type: integer
          maximum: 365.0
          minimum: 7.0
          title: Horizon Days
          default: 60
        enrollment_velocity_weekly:
          type: number
          minimum: 0.0
          title: Enrollment Velocity Weekly
          default: 0.5
        screening_pipeline_count:
          type: integer
          minimum: 0.0
          title: Screening Pipeline Count
          default: 0
        screening_conversion_rate:
          type: number
          maximum: 1.0
          minimum: 0.0
          title: Screening Conversion Rate
          default: 0.75
        lead_time_days:
          type: integer
          minimum: 1.0
          title: Lead Time Days
          default: 3
        buffer_days:
          type: integer
          minimum: 0.0
          title: Buffer Days
          default: 5
        safety_stock_threshold:
          type: integer
          minimum: 0.0
          title: Safety Stock Threshold
          default: 5
        storage_capacity:
          type: integer
          minimum: 1.0
          title: Storage Capacity
          default: 500
      type: object
      required:
      - study_id
      - site_id
      title: PredictiveForecastRequest
      description: Request payload to execute a predictive resupply forecast.
    PredictiveForecastResponse:
      properties:
        study_id:
          type: string
          title: Study Id
        site_id:
          type: string
          title: Site Id
        kit_type:
          type: string
          title: Kit Type
        forecast_generated_at:
          type: string
          format: date-time
          title: Forecast Generated At
        horizon_days:
          type: integer
          title: Horizon Days
        current_on_hand_qty:
          type: integer
          title: Current On Hand Qty
        current_reserved_qty:
          type: integer
          title: Current Reserved Qty
        current_in_transit_qty:
          type: integer
          title: Current In Transit Qty
        effective_available_stock:
          type: integer
          title: Effective Available Stock
        active_subjects_count:
          type: integer
          title: Active Subjects Count
        screening_pipeline_count:
          type: integer
          title: Screening Pipeline Count
        enrollment_velocity_weekly:
          type: number
          title: Enrollment Velocity Weekly
        total_projected_demand:
          type: integer
          title: Total Projected Demand
        average_daily_burn_rate:
          type: number
          title: Average Daily Burn Rate
        days_of_supply_remaining:
          type: number
          title: Days Of Supply Remaining
        projected_runout_date:
          anyOf:
          - type: string
          - type: 'null'
          title: Projected Runout Date
        stockout_risk_level:
          type: string
          title: Stockout Risk Level
        recommended_order_qty:
          type: integer
          title: Recommended Order Qty
        reorder_recommended:
          type: boolean
          title: Reorder Recommended
        reorder_trigger_day:
          anyOf:
          - type: integer
          - type: 'null'
          title: Reorder Trigger Day
        timeline:
          items:
            $ref: '#/components/schemas/DailyDemandProjection'
          type: array
          title: Timeline
      type: object
      required:
      - study_id
      - site_id
      - kit_type
      - forecast_generated_at
      - horizon_days
      - current_on_hand_qty
      - current_reserved_qty
      - current_in_transit_qty
      - effective_available_stock
      - active_subjects_count
      - screening_pipeline_count
      - enrollment_velocity_weekly
      - total_projected_demand
      - average_daily_burn_rate
      - days_of_supply_remaining
      - projected_runout_date
      - stockout_risk_level
      - recommended_order_qty
      - reorder_recommended
      - reorder_trigger_day
      - timeline
      title: PredictiveForecastResponse
      description: Detailed response schema for predictive demand forecast.
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
          $ref: '#/components/schemas/ProtocolVersionStatusEnum'
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
      description: 'Request payload to publish a new protocol amendment version.


        Requirements: PRD-SYS-001'
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
      description: 'Response payload following protocol amendment publishing.


        Requirements: PRD-SYS-001'
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
          - type: 'null'
          title: Visit Id
        domain:
          anyOf:
          - type: string
          - type: 'null'
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
          - type: 'null'
          title: Status
          default: OPEN
        observation_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Observation Id
        field_link:
          anyOf:
          - type: string
          - type: 'null'
          title: Field Link
        message:
          anyOf:
          - type: string
          - type: 'null'
          title: Message
        origin:
          anyOf:
          - type: string
          - type: 'null'
          title: Origin
        priority:
          anyOf:
          - type: string
          - type: 'null'
          title: Priority
        rule_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Rule Id
        created_by:
          anyOf:
          - type: string
          - type: 'null'
          title: Created By
        form_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Form Id
        field_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Field Id
        query_type:
          anyOf:
          - type: string
          - type: 'null'
          title: Query Type
        action_required:
          anyOf:
          - type: string
          - type: 'null'
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
          - type: 'null'
          title: User Id
        timestamp:
          type: string
          format: date-time
          title: Timestamp
        old_values:
          anyOf:
          - additionalProperties: true
            type: object
          - type: 'null'
          title: Old Values
        new_values:
          anyOf:
          - additionalProperties: true
            type: object
          - type: 'null'
          title: New Values
        change_reason:
          anyOf:
          - type: string
          - type: 'null'
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
          - type: 'null'
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
          - type: 'null'
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
          - type: 'null'
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
          - type: 'null'
          title: Observation Id
          description: Optional target unique clinical observation ID
        form_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Form Id
          description: Optional form identifier
        field_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Field Id
          description: Optional field identifier
        explanation:
          anyOf:
          - type: string
          - type: 'null'
          title: Explanation
          description: Contextual explanation/issue description triggering query generation
      type: object
      required:
      - subject_id
      - visit_id
      - domain
      - test_code
      title: QueryTargetDescriptor
      description: 'Coordinate fields representing the specific target of a query.


        Requirements: PRD-SYS-001'
    QueryUpdate:
      properties:
        status:
          type: string
          title: Status
        explanation:
          anyOf:
          - type: string
          - type: 'null'
          title: Explanation
        response:
          anyOf:
          - type: string
          - type: 'null'
          title: Response
        observation_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Observation Id
        field_link:
          anyOf:
          - type: string
          - type: 'null'
          title: Field Link
        message:
          anyOf:
          - type: string
          - type: 'null'
          title: Message
        origin:
          anyOf:
          - type: string
          - type: 'null'
          title: Origin
        priority:
          anyOf:
          - type: string
          - type: 'null'
          title: Priority
        rule_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Rule Id
        created_by:
          anyOf:
          - type: string
          - type: 'null'
          title: Created By
        responder:
          anyOf:
          - type: string
          - type: 'null'
          title: Responder
        resolver:
          anyOf:
          - type: string
          - type: 'null'
          title: Resolver
        resolved_at:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Resolved At
        cancellation_reason:
          anyOf:
          - type: string
          - type: 'null'
          title: Cancellation Reason
        escalated_at:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Escalated At
        form_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Form Id
        field_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Field Id
        query_type:
          anyOf:
          - type: string
          - type: 'null'
          title: Query Type
        action_required:
          anyOf:
          - type: string
          - type: 'null'
          title: Action Required
      type: object
      required:
      - status
      title: QueryUpdate
      description: Pydantic schema for general state transitions.
    RARArmEvaluationDTO:
      properties:
        arm_id:
          type: string
          title: Arm Id
        arm_name:
          type: string
          title: Arm Name
        is_control:
          type: boolean
          title: Is Control
        status:
          type: string
          title: Status
        enrolled_count:
          type: integer
          title: Enrolled Count
        response_count:
          type: integer
          title: Response Count
        posterior_alpha:
          type: number
          title: Posterior Alpha
        posterior_beta:
          type: number
          title: Posterior Beta
        posterior_mean:
          type: number
          title: Posterior Mean
        allocation_probability:
          type: number
          title: Allocation Probability
        superiority_vs_control_prob:
          anyOf:
          - type: number
          - type: 'null'
          title: Superiority Vs Control Prob
      type: object
      required:
      - arm_id
      - arm_name
      - is_control
      - status
      - enrolled_count
      - response_count
      - posterior_alpha
      - posterior_beta
      - posterior_mean
      - allocation_probability
      title: RARArmEvaluationDTO
      description: State and metrics for an individual arm under Response-Adaptive Randomization.
    RAREvaluationResult:
      properties:
        study_id:
          type: string
          title: Study Id
        total_enrolled:
          type: integer
          title: Total Enrolled
        arms:
          items:
            $ref: '#/components/schemas/RARArmEvaluationDTO'
          type: array
          title: Arms
        allocation_probabilities:
          additionalProperties:
            type: number
          type: object
          title: Allocation Probabilities
        dropped_arms:
          items:
            type: string
          type: array
          title: Dropped Arms
        graduated_arms:
          items:
            type: string
          type: array
          title: Graduated Arms
        rationale:
          type: string
          title: Rationale
        updated_at:
          type: string
          format: date-time
          title: Updated At
      type: object
      required:
      - study_id
      - total_enrolled
      - arms
      - allocation_probabilities
      - rationale
      - updated_at
      title: RAREvaluationResult
      description: Overall RAR probability adjustment output.
    RARExpandOrTerminateArmRequest:
      properties:
        study_id:
          type: string
          title: Study Id
        arm_id:
          type: string
          title: Arm Id
        arm_name:
          type: string
          title: Arm Name
        is_control:
          type: boolean
          title: Is Control
          default: false
        action:
          type: string
          title: Action
          description: '''ADD_ARM'', ''DROP_FUTILITY'', ''GRADUATE_SUCCESS'', ''TERMINATE'''
        prior_alpha:
          type: number
          title: Prior Alpha
          default: 1.0
        prior_beta:
          type: number
          title: Prior Beta
          default: 1.0
        reason:
          type: string
          title: Reason
      type: object
      required:
      - study_id
      - arm_id
      - arm_name
      - action
      - reason
      title: RARExpandOrTerminateArmRequest
      description: Request to seamlessly add or terminate a treatment arm with zero study downtime.
    RaiseQueryRequest:
      properties:
        query_text:
          anyOf:
          - type: string
          - type: 'null'
          title: Query Text
        message:
          anyOf:
          - type: string
          - type: 'null'
          title: Message
        explanation:
          anyOf:
          - type: string
          - type: 'null'
          title: Explanation
        reason:
          anyOf:
          - type: string
          - type: 'null'
          title: Reason
        reason_for_change:
          anyOf:
          - type: string
          - type: 'null'
          title: Reason For Change
      type: object
      title: RaiseQueryRequest
    RaiseQueryResponse:
      properties:
        query_id:
          type: string
          title: Query Id
        status:
          type: string
          title: Status
        assignment_id:
          type: string
          title: Assignment Id
        explanation:
          anyOf:
          - type: string
          - type: 'null'
          title: Explanation
      type: object
      required:
      - query_id
      - status
      - assignment_id
      title: RaiseQueryResponse
    RarConfig:
      properties:
        burn_in_sample_per_arm:
          type: integer
          minimum: 0.0
          title: Burn In Sample Per Arm
          description: Initial fixed allocation sample size per arm before adaptive re-weighting activates.
          default: 5
        power_tuning_param:
          type: number
          maximum: 2.0
          minimum: 0.0
          title: Power Tuning Param
          description: Tuning exponent c for allocation probability weighting w_k ~ (q_k / n_k)^c.
          default: 0.5
        min_allocation_prob:
          type: number
          exclusiveMaximum: 0.5
          minimum: 0.0
          title: Min Allocation Prob
          description: Floor boundary on active arm allocation probability to prevent arm starvation.
          default: 0.05
        max_allocation_prob:
          type: number
          maximum: 1.0
          exclusiveMinimum: 0.5
          title: Max Allocation Prob
          description: Ceiling boundary on active arm allocation probability.
          default: 0.85
        futility_threshold:
          type: number
          exclusiveMaximum: 0.5
          minimum: 0.0
          title: Futility Threshold
          description: Posterior probability threshold P(theta_k > theta_ctrl) < threshold to drop arm for futility.
          default: 0.05
        efficacy_threshold:
          type: number
          maximum: 1.0
          exclusiveMinimum: 0.5
          title: Efficacy Threshold
          description: Posterior probability threshold P(theta_k > theta_ctrl) > threshold to graduate arm for efficacy.
          default: 0.99
        prior_alpha:
          type: number
          exclusiveMinimum: 0.0
          title: Prior Alpha
          default: 1.0
        prior_beta:
          type: number
          exclusiveMinimum: 0.0
          title: Prior Beta
          default: 1.0
      type: object
      title: RarConfig
      description: Configuration parameters for Response-Adaptive Randomization (RAR).
    ReLabelingTaskCompleteRequest:
      properties:
        notes:
          anyOf:
          - type: string
          - type: 'null'
          title: Notes
      type: object
      title: ReLabelingTaskCompleteRequest
      description: Payload to record completion of a re-labeling task.
    ReLabelingTaskResponse:
      properties:
        id:
          type: string
          title: Id
        study_id:
          type: string
          title: Study Id
        task_number:
          type: string
          title: Task Number
        lot_number:
          type: string
          title: Lot Number
        node_id:
          type: string
          title: Node Id
        affected_kit_count:
          type: integer
          title: Affected Kit Count
        old_expiry_date:
          type: string
          format: date-time
          title: Old Expiry Date
        new_expiry_date:
          type: string
          format: date-time
          title: New Expiry Date
        status:
          type: string
          title: Status
        assigned_to:
          anyOf:
          - type: string
          - type: 'null'
          title: Assigned To
        completed_at:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Completed At
        completed_by:
          anyOf:
          - type: string
          - type: 'null'
          title: Completed By
        verified_at:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Verified At
        verified_by:
          anyOf:
          - type: string
          - type: 'null'
          title: Verified By
        label_barcode_hash:
          anyOf:
          - type: string
          - type: 'null'
          title: Label Barcode Hash
        relabeling_notes:
          anyOf:
          - type: string
          - type: 'null'
          title: Relabeling Notes
      type: object
      required:
      - id
      - study_id
      - task_number
      - lot_number
      - node_id
      - affected_kit_count
      - old_expiry_date
      - new_expiry_date
      - status
      title: ReLabelingTaskResponse
      description: Response schema for a physical kit re-labeling action task.
    ReLabelingTaskVerifyRequest:
      properties:
        label_barcode_hash:
          anyOf:
          - type: string
          - type: 'null'
          title: Label Barcode Hash
        notes:
          anyOf:
          - type: string
          - type: 'null'
          title: Notes
      type: object
      title: ReLabelingTaskVerifyRequest
      description: Payload for dual QA verification of a completed re-labeling task.
    ReaderAssessment:
      properties:
        assessment_id:
          type: string
          title: Assessment Id
          description: Unique assessment ID
        dossier_id:
          type: string
          title: Dossier Id
          description: Target dossier ID
        reader_id:
          type: string
          title: Reader Id
          description: Adjudicator user identifier
        reader_name:
          type: string
          title: Reader Name
          description: Adjudicator full name / credentials
        reader_role:
          $ref: '#/components/schemas/ReaderRole'
          description: 'Assigned role: READER_1 or READER_2'
        classification:
          $ref: '#/components/schemas/AdjudicationClassification'
          description: Determined classification
        subtype:
          anyOf:
          - type: string
          - type: 'null'
          title: Subtype
          description: Specific event subtype (e.g. Type 1 MI, Ischemic Stroke)
        adjudicated_onset_date:
          anyOf:
          - type: string
          - type: 'null'
          title: Adjudicated Onset Date
          description: Adjudicated date/time of event onset in ISO 8601
        criteria_met:
          items:
            type: string
          type: array
          title: Criteria Met
          description: Diagnostic criteria items satisfied
        confidence_score:
          type: integer
          maximum: 5.0
          minimum: 1.0
          title: Confidence Score
          description: Confidence level (1=Low, 5=Definitive)
          default: 5
        clinical_rationale:
          type: string
          title: Clinical Rationale
          description: Detailed clinical rationale
          default: ''
        submitted_at:
          anyOf:
          - type: string
          - type: 'null'
          title: Submitted At
          description: UTC submission timestamp
        assessment_digest:
          type: string
          title: Assessment Digest
          description: SHA-256 hash of reader assessment payload
          default: ''
      type: object
      required:
      - assessment_id
      - dossier_id
      - reader_id
      - reader_name
      - reader_role
      - classification
      title: ReaderAssessment
      description: Blinded expert reader assessment and diagnostic criteria evaluation.
    ReaderAssessmentSubmitDTO:
      properties:
        reader_role:
          $ref: '#/components/schemas/ReaderRole'
          description: 'Reader role: READER_1 or READER_2'
        classification:
          $ref: '#/components/schemas/AdjudicationClassification'
          description: Event classification
        subtype:
          anyOf:
          - type: string
          - type: 'null'
          title: Subtype
          description: Specific event subtype (e.g. Type 1 MI, Ischemic Stroke)
        adjudicated_onset_date:
          anyOf:
          - type: string
          - type: 'null'
          title: Adjudicated Onset Date
          description: Adjudicated onset date (ISO 8601)
        criteria_met:
          items:
            type: string
          type: array
          title: Criteria Met
          description: Diagnostic criteria satisfied
        confidence_score:
          type: integer
          maximum: 5.0
          minimum: 1.0
          title: Confidence Score
          description: Confidence score 1 to 5
          default: 5
        clinical_rationale:
          type: string
          title: Clinical Rationale
          description: Clinical rationale narrative
          default: ''
      type: object
      required:
      - reader_role
      - classification
      title: ReaderAssessmentSubmitDTO
      description: Payload for Reader 1 or Reader 2 independent blinded assessment submission.
    ReaderRole:
      type: string
      enum:
      - READER_1
      - READER_2
      - CHAIR
      - COMMITTEE_MEMBER
      - COORDINATOR
      title: ReaderRole
      description: Roles within the CEC charter hierarchy.
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
      description: 'Request payload to apply non-destructive redactions to PDF document.


        Requirements: PRD-SYS-001'
    ResupplyEventApprovalRequest:
      properties:
        change_justification:
          type: string
          title: Change Justification
      type: object
      required:
      - change_justification
      title: ResupplyEventApprovalRequest
    ResupplyEventResponse:
      properties:
        id:
          type: string
          title: Id
        study_id:
          type: string
          title: Study Id
        site_id:
          type: string
          title: Site Id
        kit_id:
          type: string
          title: Kit Id
        requested_qty:
          type: integer
          title: Requested Qty
        status:
          type: string
          title: Status
        triggered_at:
          type: string
          format: date-time
          title: Triggered At
      type: object
      required:
      - id
      - study_id
      - site_id
      - kit_id
      - requested_qty
      - status
      - triggered_at
      title: ResupplyEventResponse
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
          - type: 'null'
          title: Safety Cases Xml
          description: Optional raw E2B XML reports to parse
      type: object
      required:
      - study_id
      - edc_ae_events
      title: SAEReconcileRequest
      description: 'Request payload to reconcile EDC AE data against Safety ICSR cases.


        Requirements: PRD-SYS-001'
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
          $ref: '#/components/schemas/SDVScopeEnum'
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
          - type: 'null'
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
          - type: 'null'
          title: Site Id
        is_verified:
          type: boolean
          title: Is Verified
        verified_by:
          anyOf:
          - type: string
          - type: 'null'
          title: Verified By
        verified_at:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Verified At
        dropped_reason:
          anyOf:
          - type: string
          - type: 'null'
          title: Dropped Reason
        dropped_at:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
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
          description: 'Destination safety gateway: ARGUS, ARISG, EUDRAVIGILANCE'
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
      description: 'Request payload to dispatch ICH E2B(R3) safety report to external PV gateway.


        Requirements: PRD-SYS-001'
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
          description: 'Dispatch status: DISPATCHED, DELIVERED, ACKNOWLEDGED'
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
      description: 'Response payload following E2B safety report gateway dispatch.


        Requirements: PRD-SYS-001'
    SamplingModelEnum:
      type: string
      enum:
      - SUBJECT_BASED
      - FIELD_BASED
      - COMBINED
      title: SamplingModelEnum
    SdtmCeCompilationResult:
      properties:
        study_id:
          type: string
          title: Study Id
        ce_records:
          items:
            additionalProperties: true
            type: object
          type: array
          title: Ce Records
        suppce_records:
          items:
            additionalProperties: true
            type: object
          type: array
          title: Suppce Records
        record_count:
          type: integer
          title: Record Count
          default: 0
        compiled_at:
          type: string
          title: Compiled At
        dataset_digest:
          type: string
          title: Dataset Digest
          default: ''
      type: object
      required:
      - study_id
      title: SdtmCeCompilationResult
      description: Compiled CDISC SDTM CE and SUPPCE domain dataset.
    SdtmCeCompileRequestDTO:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Study ID
      type: object
      required:
      - study_id
      title: SdtmCeCompileRequestDTO
      description: Request payload to compile SDTM CE dataset for a study.
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
    StabilityBudgetCreateRequest:
      properties:
        study_id:
          type: string
          title: Study Id
        kit_type:
          type: string
          title: Kit Type
        lot_number:
          anyOf:
          - type: string
          - type: 'null'
          title: Lot Number
        storage_condition:
          type: string
          title: Storage Condition
          default: REFRIGERATED_2_TO_8
        min_temp_celsius:
          type: number
          title: Min Temp Celsius
          default: 2.0
        max_temp_celsius:
          type: number
          title: Max Temp Celsius
          default: 8.0
        max_mkt_celsius:
          type: number
          title: Max Mkt Celsius
          default: 8.0
        max_cumulative_hours_above_upper:
          type: number
          minimum: 0.0
          title: Max Cumulative Hours Above Upper
          default: 24.0
        max_cumulative_hours_below_lower:
          type: number
          minimum: 0.0
          title: Max Cumulative Hours Below Lower
          default: 0.0
        activation_energy_over_r:
          type: number
          exclusiveMinimum: 0.0
          title: Activation Energy Over R
          default: 10000.0
        hard_min_temp_celsius:
          anyOf:
          - type: number
          - type: 'null'
          title: Hard Min Temp Celsius
          default: 0.0
        hard_max_temp_celsius:
          anyOf:
          - type: number
          - type: 'null'
          title: Hard Max Temp Celsius
          default: 25.0
      type: object
      required:
      - study_id
      - kit_type
      title: StabilityBudgetCreateRequest
      description: Payload to create or configure a drug stability budget.
    StabilityBudgetResponse:
      properties:
        id:
          type: string
          title: Id
        study_id:
          type: string
          title: Study Id
        kit_type:
          type: string
          title: Kit Type
        lot_number:
          anyOf:
          - type: string
          - type: 'null'
          title: Lot Number
        storage_condition:
          type: string
          title: Storage Condition
        min_temp_celsius:
          type: number
          title: Min Temp Celsius
        max_temp_celsius:
          type: number
          title: Max Temp Celsius
        max_mkt_celsius:
          type: number
          title: Max Mkt Celsius
        max_cumulative_hours_above_upper:
          type: number
          title: Max Cumulative Hours Above Upper
        max_cumulative_hours_below_lower:
          type: number
          title: Max Cumulative Hours Below Lower
        activation_energy_over_r:
          type: number
          title: Activation Energy Over R
        hard_min_temp_celsius:
          anyOf:
          - type: number
          - type: 'null'
          title: Hard Min Temp Celsius
        hard_max_temp_celsius:
          anyOf:
          - type: number
          - type: 'null'
          title: Hard Max Temp Celsius
      type: object
      required:
      - id
      - study_id
      - kit_type
      - storage_condition
      - min_temp_celsius
      - max_temp_celsius
      - max_mkt_celsius
      - max_cumulative_hours_above_upper
      - max_cumulative_hours_below_lower
      - activation_energy_over_r
      title: StabilityBudgetResponse
      description: Response schema for a drug stability budget.
    StagedAdverseEventActionRequest:
      properties:
        action:
          type: string
          title: Action
          description: 'Action to take: ''CONFIRM'' (promote to formal AE), ''DISMISS'' (not clinically significant), or ''ESCALATE_SAE'''
        investigator_notes:
          anyOf:
          - type: string
          - type: 'null'
          title: Investigator Notes
          description: Clinical commentary and rationale
        electronic_signature_password:
          anyOf:
          - type: string
          - type: 'null'
          title: Electronic Signature Password
          description: 21 CFR Part 11 authentication credential
        reason_for_change:
          type: string
          title: Reason For Change
          description: Mandatory GxP 21 CFR Part 11 justification reason
      type: object
      required:
      - action
      - reason_for_change
      title: StagedAdverseEventActionRequest
      description: Payload to perform an Investigator action on a staged Grade 3/4 AE.
    StagedAdverseEventActionResponse:
      properties:
        id:
          type: string
          title: Id
          description: Review ID
        review_status:
          type: string
          title: Review Status
          description: Updated review status
        action_taken:
          type: string
          title: Action Taken
          description: Action that was executed
        form_submission_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Form Submission Id
          description: FormSubmission ID if confirmed
        query_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Query Id
          description: ClinicalQuery ID updated
        message:
          type: string
          title: Message
          description: Result message
        timestamp:
          type: string
          format: date-time
          title: Timestamp
          description: Action timestamp
      type: object
      required:
      - id
      - review_status
      - action_taken
      - message
      - timestamp
      title: StagedAdverseEventActionResponse
      description: Response from executing an Investigator action on a staged AE.
    StagedAdverseEventReviewResponse:
      properties:
        id:
          type: string
          title: Id
          description: Unique review record ID
        study_id:
          type: string
          title: Study Id
          description: Trial study identifier
        site_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Site Id
          description: Investigator site identifier
        subject_id:
          type: string
          title: Subject Id
          description: Subject identifier
        visit_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Visit Id
          description: Visit identifier
        observation_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Observation Id
          description: Linked ClinicalObservation ID
        form_submission_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Form Submission Id
          description: Drafted AE FormSubmission ID
        query_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Query Id
          description: Linked discrepancy ClinicalQuery ID
        test_code:
          type: string
          title: Test Code
          description: Laboratory test code
        test_name:
          type: string
          title: Test Name
          description: Descriptive test name
        lab_value:
          type: number
          title: Lab Value
          description: Observed lab measurement value
        unit:
          type: string
          title: Unit
          description: Unit of measurement
        ctcae_grade:
          type: integer
          title: Ctcae Grade
          description: Toxicity grade (3 or 4)
        grading_scale:
          type: string
          title: Grading Scale
          description: Grading scale standard
        toxicity_term:
          type: string
          title: Toxicity Term
          description: Standard adverse event term
        clinical_description:
          type: string
          title: Clinical Description
          description: Clinical severity description
        is_hys_law_warning:
          type: boolean
          title: Is Hys Law Warning
          description: Potential Hy's Law DILI alert
        is_delta_check_alert:
          type: boolean
          title: Is Delta Check Alert
          description: Acute delta check alert
        delta_percentage:
          anyOf:
          - type: number
          - type: 'null'
          title: Delta Percentage
          description: Delta change percentage from baseline
        review_status:
          type: string
          title: Review Status
          description: Current review status
        reviewed_by:
          anyOf:
          - type: string
          - type: 'null'
          title: Reviewed By
          description: User ID of reviewing Investigator
        reviewed_at:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Reviewed At
          description: Review timestamp
        investigator_notes:
          anyOf:
          - type: string
          - type: 'null'
          title: Investigator Notes
          description: Investigator clinical notes
        version:
          type: integer
          title: Version
          description: Version index
        is_deleted:
          type: boolean
          title: Is Deleted
          description: Soft deletion flag
        created_at:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Created At
          description: Creation timestamp
      type: object
      required:
      - id
      - study_id
      - subject_id
      - test_code
      - test_name
      - lab_value
      - unit
      - ctcae_grade
      - grading_scale
      - toxicity_term
      - clinical_description
      - is_hys_law_warning
      - is_delta_check_alert
      - review_status
      - version
      - is_deleted
      title: StagedAdverseEventReviewResponse
      description: Response payload for a staged Grade 3/4 AE laboratory toxicity review item.
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
          $ref: '#/components/schemas/ProtocolVersionRefDTO'
        icf_signed:
          type: boolean
          title: Icf Signed
        icf_signed_date:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Icf Signed Date
        requires_reconsent:
          type: boolean
          title: Requires Reconsent
          default: false
        is_paper_override:
          type: boolean
          title: Is Paper Override
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
          - type: 'null'
          title: Icf Signed Date
        requires_reconsent:
          type: boolean
          title: Requires Reconsent
        is_paper_override:
          type: boolean
          title: Is Paper Override
          default: false
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
        site_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Site Id
        arm_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Arm Id
        demographics:
          anyOf:
          - $ref: '#/components/schemas/Demographics'
          - type: 'null'
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
          - $ref: '#/components/schemas/Demographics'
          - type: 'null'
        strat_factors:
          anyOf:
          - additionalProperties: true
            type: object
          - type: 'null'
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
          - type: 'null'
          title: Site Id
        treatment_group:
          anyOf:
          - type: string
          - type: 'null'
          title: Treatment Group
        randomization_seed:
          anyOf:
          - type: string
          - type: 'null'
          title: Randomization Seed
        investigational_product_id:
          anyOf:
          - type: string
          - type: 'null'
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
          - type: 'null'
          title: Stratum Key
        randomized_at:
          type: string
          format: date-time
          title: Randomized At
        kit_reference:
          anyOf:
          - type: string
          - type: 'null'
          title: Kit Reference
        treatment_arm:
          anyOf:
          - type: string
          - type: 'null'
          title: Treatment Arm
      type: object
      required:
      - subject_id
      - status
      - randomized_at
      title: SubjectRandomizationResponse
      description: Pydantic schema for returning blinded subject randomization details.
    SubjectReconsentRequest:
      properties:
        subject_id:
          type: string
          title: Subject Id
          description: Subject identifier
        study_id:
          type: string
          title: Study Id
          description: Study identifier
        protocol_version:
          type: string
          title: Protocol Version
          description: Amended protocol version tag
        version_index:
          type: integer
          title: Version Index
          description: Protocol version index
          default: 2
        icf_signed:
          type: boolean
          title: Icf Signed
          description: Whether ICF is signed
          default: true
        signature_type:
          type: string
          title: Signature Type
          description: 'Signature type: ''ECONSENT'' or ''PAPER_UPLOAD'''
          default: ECONSENT
      type: object
      required:
      - subject_id
      - study_id
      - protocol_version
      title: SubjectReconsentRequest
      description: Request payload to register subject re-consent for an amended version.
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
        site_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Site Id
        treatment_group:
          anyOf:
          - type: string
          - type: 'null'
          title: Treatment Group
        status:
          anyOf:
          - type: string
          - type: 'null'
          title: Status
          default: SCREENING
        active_protocol_version:
          anyOf:
          - type: string
          - type: 'null'
          title: Active Protocol Version
          default: 1.0.0
        encrypted_demographics:
          anyOf:
          - type: string
          - type: 'null'
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
          - type: 'null'
          title: Study Id
      type: object
      title: SubjectScreeningRequest
      description: Pydantic schema for requesting subject eligibility screening.
    SubjectScreeningResponse:
      properties:
        eligible:
          anyOf:
          - type: boolean
          - type: 'null'
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
            $ref: '#/components/schemas/CriterionLevelResult'
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
          - type: 'null'
          title: Status
        state:
          anyOf:
          - type: string
          - type: 'null'
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
          - type: 'null'
          title: Treatment Arm
        drug_code:
          anyOf:
          - type: string
          - type: 'null'
          title: Drug Code
        unblinded_at:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Unblinded At
        unblinded_by:
          anyOf:
          - type: string
          - type: 'null'
          title: Unblinded By
        unblinded_reason:
          anyOf:
          - type: string
          - type: 'null'
          title: Unblinded Reason
      type: object
      required:
      - subject_id
      - status
      - is_unblinded
      title: SubjectUnblindResponse
      description: Pydantic schema for returning emergency unblind details.
    SupplyHierarchyNode:
      properties:
        node_id:
          type: string
          title: Node Id
        study_id:
          type: string
          title: Study Id
        node_type:
          $ref: '#/components/schemas/SupplyNodeType'
        name:
          type: string
          title: Name
        country:
          type: string
          title: Country
        address:
          anyOf:
          - type: string
          - type: 'null'
          title: Address
        parent_node_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Parent Node Id
        storage_capacity:
          type: integer
          title: Storage Capacity
        current_utilization:
          type: integer
          title: Current Utilization
        utilization_percentage:
          type: number
          title: Utilization Percentage
        lead_time_days:
          type: integer
          title: Lead Time Days
        temperature_conditions:
          items:
            type: string
          type: array
          title: Temperature Conditions
        is_active:
          type: boolean
          title: Is Active
        total_on_hand:
          type: integer
          title: Total On Hand
          default: 0
        total_reserved:
          type: integer
          title: Total Reserved
          default: 0
        total_in_transit:
          type: integer
          title: Total In Transit
          default: 0
        children:
          items:
            $ref: '#/components/schemas/SupplyHierarchyNode'
          type: array
          title: Children
      type: object
      required:
      - node_id
      - study_id
      - node_type
      - name
      - country
      - storage_capacity
      - current_utilization
      - utilization_percentage
      - lead_time_days
      - is_active
      title: SupplyHierarchyNode
      description: Node in the hierarchical supply chain tree representation.
    SupplyNodeCreateRequest:
      properties:
        study_id:
          type: string
          title: Study Id
        node_id:
          type: string
          title: Node Id
        node_type:
          type: string
          title: Node Type
          default: CLINICAL_SITE
        name:
          type: string
          title: Name
        country:
          type: string
          title: Country
        address:
          anyOf:
          - type: string
          - type: 'null'
          title: Address
        parent_node_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Parent Node Id
        storage_capacity:
          type: integer
          minimum: 1.0
          title: Storage Capacity
          default: 1000
        lead_time_days:
          type: integer
          minimum: 1.0
          title: Lead Time Days
          default: 3
        temperature_conditions:
          items:
            type: string
          type: array
          title: Temperature Conditions
      type: object
      required:
      - study_id
      - node_id
      - name
      - country
      title: SupplyNodeCreateRequest
      description: Payload to register or update a supply node in the clinical supply hierarchy.
    SupplyNodeInventoryAdjustRequest:
      properties:
        study_id:
          type: string
          title: Study Id
        node_id:
          type: string
          title: Node Id
        kit_type:
          type: string
          title: Kit Type
        lot_number:
          anyOf:
          - type: string
          - type: 'null'
          title: Lot Number
        on_hand_qty:
          type: integer
          minimum: 0.0
          title: On Hand Qty
        safety_stock_threshold:
          type: integer
          minimum: 0.0
          title: Safety Stock Threshold
          default: 10
        reorder_point:
          type: integer
          minimum: 0.0
          title: Reorder Point
          default: 20
        max_capacity:
          type: integer
          minimum: 1.0
          title: Max Capacity
          default: 500
      type: object
      required:
      - study_id
      - node_id
      - kit_type
      - on_hand_qty
      title: SupplyNodeInventoryAdjustRequest
      description: Payload to initialize or adjust inventory levels at a supply node.
    SupplyNodeInventoryResponse:
      properties:
        id:
          type: string
          title: Id
        study_id:
          type: string
          title: Study Id
        node_id:
          type: string
          title: Node Id
        kit_type:
          type: string
          title: Kit Type
        lot_number:
          anyOf:
          - type: string
          - type: 'null'
          title: Lot Number
        on_hand_qty:
          type: integer
          title: On Hand Qty
        reserved_qty:
          type: integer
          title: Reserved Qty
        in_transit_qty:
          type: integer
          title: In Transit Qty
        safety_stock_threshold:
          type: integer
          title: Safety Stock Threshold
        reorder_point:
          type: integer
          title: Reorder Point
        max_capacity:
          type: integer
          title: Max Capacity
        resupply_signal:
          type: boolean
          title: Resupply Signal
      type: object
      required:
      - id
      - study_id
      - node_id
      - kit_type
      - on_hand_qty
      - reserved_qty
      - in_transit_qty
      - safety_stock_threshold
      - reorder_point
      - max_capacity
      - resupply_signal
      title: SupplyNodeInventoryResponse
      description: Response schema for node inventory.
    SupplyNodeResponse:
      properties:
        id:
          type: string
          title: Id
        study_id:
          type: string
          title: Study Id
        node_id:
          type: string
          title: Node Id
        node_type:
          type: string
          title: Node Type
        name:
          type: string
          title: Name
        country:
          type: string
          title: Country
        address:
          anyOf:
          - type: string
          - type: 'null'
          title: Address
        parent_node_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Parent Node Id
        storage_capacity:
          type: integer
          title: Storage Capacity
        current_utilization:
          type: integer
          title: Current Utilization
        lead_time_days:
          type: integer
          title: Lead Time Days
        temperature_conditions:
          items:
            type: string
          type: array
          title: Temperature Conditions
        is_active:
          type: boolean
          title: Is Active
      type: object
      required:
      - id
      - study_id
      - node_id
      - node_type
      - name
      - country
      - storage_capacity
      - current_utilization
      - lead_time_days
      - is_active
      title: SupplyNodeResponse
      description: Response schema for a supply node.
    SupplyNodeType:
      type: string
      enum:
      - PRIMARY_MANUFACTURER
      - CENTRAL_DEPOT
      - REGIONAL_DEPOT
      - CLINICAL_SITE
      title: SupplyNodeType
      description: Hierarchy tiers for clinical trial drug supply chain.
    SyncBlockDetails:
      properties:
        fieldId:
          type: string
          title: Fieldid
        studyId:
          anyOf:
          - type: string
          - type: 'null'
          title: Studyid
        subjectId:
          anyOf:
          - type: string
          - type: 'null'
          title: Subjectid
        visitId:
          anyOf:
          - type: string
          - type: 'null'
          title: Visitid
        domain:
          anyOf:
          - type: string
          - type: 'null'
          title: Domain
        testCode:
          anyOf:
          - type: string
          - type: 'null'
          title: Testcode
        query:
          anyOf:
          - $ref: '#/components/schemas/SyncBlockQuery'
          - type: 'null'
        label:
          anyOf:
          - type: string
          - type: 'null'
          title: Label
        cdash:
          anyOf:
          - type: string
          - type: 'null'
          title: Cdash
        oldValue:
          anyOf:
          - type: string
          - type: 'null'
          title: Oldvalue
        newValue:
          anyOf:
          - type: string
          - type: 'null'
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
          - type: 'null'
          title: Message
        createdBy:
          anyOf:
          - type: string
          - type: 'null'
          title: Createdby
        createdAt:
          anyOf:
          - type: string
          - type: 'null'
          title: Createdat
        response:
          anyOf:
          - type: string
          - type: 'null'
          title: Response
        respondedBy:
          anyOf:
          - type: string
          - type: 'null'
          title: Respondedby
        respondedAt:
          anyOf:
          - type: string
          - type: 'null'
          title: Respondedat
        closedBy:
          anyOf:
          - type: string
          - type: 'null'
          title: Closedby
        closedAt:
          anyOf:
          - type: string
          - type: 'null'
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
            $ref: '#/components/schemas/LocalLedgerBlock'
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
          $ref: '#/components/schemas/SamplingModelEnum'
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
          - type: 'null'
          title: Full Sdv Domains
        safety_endpoints:
          anyOf:
          - items:
              type: string
            type: array
          - type: 'null'
          title: Safety Endpoints
        zero_sdv_domains:
          anyOf:
          - items:
              type: string
            type: array
          - type: 'null'
          title: Zero Sdv Domains
        trial_random_seed:
          anyOf:
          - type: integer
            minimum: 0.0
          - type: 'null'
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
          - type: 'null'
          title: Full Sdv Domains
        safety_endpoints:
          anyOf:
          - items:
              type: string
            type: array
          - type: 'null'
          title: Safety Endpoints
        zero_sdv_domains:
          anyOf:
          - items:
              type: string
            type: array
          - type: 'null'
          title: Zero Sdv Domains
        trial_random_seed:
          anyOf:
          - type: integer
          - type: 'null'
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
          - type: 'null'
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
    TemperatureLoggerRegisterRequest:
      properties:
        study_id:
          type: string
          title: Study Id
        logger_id:
          type: string
          title: Logger Id
        device_model:
          type: string
          title: Device Model
          default: Sensitech TempTale Ultra
        serial_number:
          anyOf:
          - type: string
          - type: 'null'
          title: Serial Number
        logger_type:
          type: string
          title: Logger Type
          default: SHIPMENT
        assigned_transfer_order_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Assigned Transfer Order Id
        assigned_node_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Assigned Node Id
        assigned_container_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Assigned Container Id
        calibration_expiry_date:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Calibration Expiry Date
      additionalProperties: false
      type: object
      required:
      - study_id
      - logger_id
      title: TemperatureLoggerRegisterRequest
      description: Payload to register a hardware temperature logger device.
    TemperatureLoggerResponse:
      properties:
        id:
          type: string
          title: Id
        study_id:
          type: string
          title: Study Id
        logger_id:
          type: string
          title: Logger Id
        device_model:
          type: string
          title: Device Model
        serial_number:
          anyOf:
          - type: string
          - type: 'null'
          title: Serial Number
        logger_type:
          type: string
          title: Logger Type
        assigned_transfer_order_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Assigned Transfer Order Id
        assigned_node_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Assigned Node Id
        assigned_container_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Assigned Container Id
        calibration_expiry_date:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Calibration Expiry Date
        status:
          type: string
          title: Status
      type: object
      required:
      - id
      - study_id
      - logger_id
      - device_model
      - logger_type
      - status
      title: TemperatureLoggerResponse
      description: Response schema for a temperature logger device.
    ThreePlusThreeConfig:
      properties:
        initial_cohort_size:
          type: integer
          minimum: 1.0
          title: Initial Cohort Size
          default: 3
        expansion_cohort_size:
          type: integer
          minimum: 1.0
          title: Expansion Cohort Size
          default: 3
        max_dlt_escalate:
          type: integer
          minimum: 0.0
          title: Max Dlt Escalate
          default: 0
        max_dlt_expand:
          type: integer
          minimum: 0.0
          title: Max Dlt Expand
          default: 1
      type: object
      title: ThreePlusThreeConfig
      description: Configuration parameters for classic 3+3 rule-based design.
    TransferOrderApprovalRequest:
      properties:
        change_justification:
          type: string
          title: Change Justification
      type: object
      required:
      - change_justification
      title: TransferOrderApprovalRequest
      description: Approval or rejection justification payload.
    TransferOrderCreateRequest:
      properties:
        study_id:
          type: string
          title: Study Id
        source_node_id:
          type: string
          title: Source Node Id
        destination_node_id:
          type: string
          title: Destination Node Id
        kit_type:
          type: string
          title: Kit Type
        quantity:
          type: integer
          minimum: 1.0
          title: Quantity
        priority:
          type: string
          title: Priority
          default: ROUTINE
        temperature_condition:
          type: string
          title: Temperature Condition
          default: CONTROLLED_ROOM_TEMP
        carrier:
          anyOf:
          - type: string
          - type: 'null'
          title: Carrier
        transfer_order_number:
          anyOf:
          - type: string
          - type: 'null'
          title: Transfer Order Number
      type: object
      required:
      - study_id
      - source_node_id
      - destination_node_id
      - kit_type
      - quantity
      title: TransferOrderCreateRequest
      description: Payload to create a multi-echelon stock transfer order.
    TransferOrderReceiveRequest:
      properties:
        change_justification:
          type: string
          title: Change Justification
      type: object
      required:
      - change_justification
      title: TransferOrderReceiveRequest
      description: Payload to mark a transfer order as delivered/received.
    TransferOrderResponse:
      properties:
        id:
          type: string
          title: Id
        study_id:
          type: string
          title: Study Id
        transfer_order_number:
          type: string
          title: Transfer Order Number
        source_node_id:
          type: string
          title: Source Node Id
        destination_node_id:
          type: string
          title: Destination Node Id
        kit_type:
          type: string
          title: Kit Type
        quantity:
          type: integer
          title: Quantity
        priority:
          type: string
          title: Priority
        status:
          type: string
          title: Status
        temperature_condition:
          type: string
          title: Temperature Condition
        carrier:
          anyOf:
          - type: string
          - type: 'null'
          title: Carrier
        tracking_number:
          anyOf:
          - type: string
          - type: 'null'
          title: Tracking Number
        estimated_delivery_date:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Estimated Delivery Date
        requested_at:
          type: string
          format: date-time
          title: Requested At
        requested_by:
          anyOf:
          - type: string
          - type: 'null'
          title: Requested By
        approved_at:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Approved At
        approved_by:
          anyOf:
          - type: string
          - type: 'null'
          title: Approved By
        shipped_at:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Shipped At
        shipped_by:
          anyOf:
          - type: string
          - type: 'null'
          title: Shipped By
        delivered_at:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Delivered At
        delivered_by:
          anyOf:
          - type: string
          - type: 'null'
          title: Delivered By
        rejection_reason:
          anyOf:
          - type: string
          - type: 'null'
          title: Rejection Reason
      type: object
      required:
      - id
      - study_id
      - transfer_order_number
      - source_node_id
      - destination_node_id
      - kit_type
      - quantity
      - priority
      - status
      - temperature_condition
      - requested_at
      title: TransferOrderResponse
      description: Response schema for transfer orders.
    TransferOrderShipRequest:
      properties:
        tracking_number:
          type: string
          title: Tracking Number
        carrier:
          anyOf:
          - type: string
          - type: 'null'
          title: Carrier
        estimated_delivery_date:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Estimated Delivery Date
        change_justification:
          type: string
          title: Change Justification
      type: object
      required:
      - tracking_number
      - change_justification
      title: TransferOrderShipRequest
      description: Payload to mark a transfer order as shipped.
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
          - type: 'null'
          title: Odm Payload
        openrosa_payload:
          anyOf:
          - type: string
          - type: 'null'
          title: Openrosa Payload
        error_message:
          anyOf:
          - type: string
          - type: 'null'
          title: Error Message
        warnings:
          anyOf:
          - items:
              additionalProperties: true
              type: object
            type: array
          - type: 'null'
          title: Warnings
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
          $ref: '#/components/schemas/UCUMUnitValue'
        target:
          $ref: '#/components/schemas/UCUMUnitValue'
        is_compatible:
          type: boolean
          title: Is Compatible
        scale_factor:
          type: number
          title: Scale Factor
        offset:
          anyOf:
          - type: number
          - type: 'null'
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
          $ref: '#/components/schemas/UnblindingReasonCode'
        justification:
          type: string
          minLength: 50
          title: Justification
        shares:
          items:
            $ref: '#/components/schemas/CustodianShare'
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
          $ref: '#/components/schemas/EISFTaxonomyCategoryEnum'
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
      description: 'Request payload to upload an eISF document.


        Requirements: PRD-SYS-001'
    ValidateGatingRequest:
      properties:
        subject_id:
          type: string
          title: Subject Id
          description: Subject identifier
        target_visit_id:
          type: string
          title: Target Visit Id
          description: Target visit identifier
        active_protocol_version:
          type: string
          title: Active Protocol Version
          description: Active protocol version tag (e.g., '2.0.0')
        requires_reconsent:
          type: boolean
          title: Requires Reconsent
          description: Whether amendment requires re-consent
          default: false
      type: object
      required:
      - subject_id
      - target_visit_id
      - active_protocol_version
      title: ValidateGatingRequest
      description: Request payload to validate subject re-consent gating.
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
          default: ''
      type: object
      required:
      - is_valid
      - status
      title: VerifyResponse
      description: Response model for PKCS#7 signature verification.
    VerifyTrainingGatingRequest:
      properties:
        study_id:
          type: string
          title: Study Id
          description: Clinical study ID
        protocol_version:
          type: string
          title: Protocol Version
          description: Active protocol version tag
        user_id:
          type: string
          title: User Id
          description: User ID to evaluate
        site_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Site Id
          description: Optional clinical site ID
        user_role:
          anyOf:
          - type: string
          - type: 'null'
          title: User Role
          description: Optional user clinical role
      type: object
      required:
      - study_id
      - protocol_version
      - user_id
      title: VerifyTrainingGatingRequest
      description: Payload to verify user qualification for EDC write permissions.
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
          - type: 'null'
          title: Visit Date
        planned_date:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Planned Date
        window_start:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Window Start
        window_end:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Window End
        window_status:
          anyOf:
          - type: string
          - type: 'null'
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
          - type: 'null'
          title: Treatment Group
        randomization_seed:
          anyOf:
          - type: string
          - type: 'null'
          title: Randomization Seed
        investigational_product_id:
          anyOf:
          - type: string
          - type: 'null'
          title: Investigational Product Id
        planned_date:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Planned Date
        window_start:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Window Start
        window_end:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Window End
        window_status:
          anyOf:
          - type: string
          - type: 'null'
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
          - type: 'null'
          title: Protocol Version Tag
        protocol_version_index:
          anyOf:
          - type: integer
          - type: 'null'
          title: Protocol Version Index
        planned_date:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Planned Date
        window_start:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Window Start
        window_end:
          anyOf:
          - type: string
            format: date-time
          - type: 'null'
          title: Window End
        window_status:
          anyOf:
          - type: string
          - type: 'null'
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
    VotingRule:
      type: string
      enum:
      - UNANIMOUS
      - MAJORITY_2_OF_3
      - CHAIR_TIE_BREAKER
      - SUPERMAJORITY
      title: VotingRule
      description: Committee charter decision and voting rules.
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
          - type: 'null'
          title: Code
        text:
          anyOf:
          - type: string
          - type: 'null'
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
            $ref: '#/components/schemas/WHODrugMatch'
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
          - type: 'null'
          title: Code
        name:
          anyOf:
          - type: string
          - type: 'null'
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
          - type: 'null'
          title: Drug Name
        score:
          type: number
          title: Score
        ingredients:
          items:
            $ref: '#/components/schemas/WHODrugIngredientItem'
          type: array
          title: Ingredients
          default: []
        atc_context:
          items:
            $ref: '#/components/schemas/WHODrugATCContext'
          type: array
          title: Atc Context
          default: []
        code:
          anyOf:
          - type: string
          - type: 'null'
          title: Code
        name:
          anyOf:
          - type: string
          - type: 'null'
          title: Name
        atc:
          items:
            $ref: '#/components/schemas/WHODrugATCContext'
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
