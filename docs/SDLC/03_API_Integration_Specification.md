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
* `X-User-Id`: The unique user identifier (`sub` claim).
* `X-User-Roles`: A comma-separated list of roles assigned to the user.
* `X-Gateway-Timestamp`: The exact UNIX timestamp of the request verification.
* `X-Gateway-Signature`: An **HMAC-SHA256** signature generated using a shared secret.

The downstream services compute the signature locally to verify that the request originated from the gateway:
$$\text{Signature} = \text{HMAC-SHA256}(\text{GATEWAY\_SECRET}, \text{X-User-Id} \parallel \text{":"} \parallel \text{X-User-Roles} \parallel \text{":"} \parallel \text{X-Gateway-Timestamp})$$

If the signature computed matches `X-Gateway-Signature`, and the timestamp is within $\pm 5$ seconds, the request is trusted as authenticated.

#### 3.1.2 JWT Token Structure
A valid JWT token contains the following standard and custom claims:

```json
{
  "iss": "https://auth.cadence-clinical.com/realms/cadence",
  "sub": "usr_9921a88b2c410",
  "aud": "cadence-api-gateway",
  "exp": 1782035200,
  "nbf": 1782031600,
  "iat": 1782031600,
  "jti": "jwt_ef881029cbaef9901",
  "name": "Dr. Sarah Jenkins",
  "email": "s.jenkins@cadence-clinical.com",
  "realm_access": {
    "roles": [
      "STUDY_DESIGNER",
      "TERMINOLOGY_MANAGER",
      "PRINCIPAL_INVESTIGATOR"
    ]
  }
}
```

### 3.2 Rate Limiting Architecture
To prevent Denial of Service (DoS) attacks and ensure resource fairness, Cadence Clinical implements a distributed **Token Bucket / Sliding Window Rate Limiting** algorithm.

* **Default Limit**: 500 requests per sliding window of 60 seconds per IP address / authenticated user ID.
* **MDR Search & Bulk Endpoints**: 100 requests per sliding window of 60 seconds.
* **Dictionary Coding Endpoints**: 1,000 requests per sliding window of 60 seconds (optimized for parallel workflows).

#### 3.2.1 Rate Limiting Headers
Every response emitted by the gateway includes the following headers detailing rate-limit states:
* `X-RateLimit-Limit`: The maximum number of allowed requests in the current sliding window.
* `X-RateLimit-Remaining`: The remaining number of requests allowed for the current window.
* `X-RateLimit-Reset`: The UNIX epoch timestamp indicating when the current window resets.

#### 3.2.2 HTTP 429 Too Many Requests Payload
When limits are exceeded, the API Gateway immediately rejects the request with an HTTP status code `429` and the following payload structure:

```json
{
  "type": "https://api.cadence-clinical.com/errors/too-many-requests",
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
* `type` (string): A URI reference identifying the problem type.
* `title` (string): A short, human-readable summary of the problem type.
* `status` (integer): The HTTP status code.
* `detail` (string): A detailed explanation of this specific error instance.
* `instance` (string): A URI reference for the specific resource path.
* `code` (string): A stable, unique platform error code (e.g., `STUDY_LOCKED`, `INVALID_CONCEPT_CODE`).
* `invalid_params` (array, optional): A list of fields that failed validation (useful for `400 Bad Request`).

#### 3.3.2 Example Error Payload: Validation Failure (HTTP 400)
```json
{
  "type": "https://api.cadence-clinical.com/errors/validation-failed",
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
      "reason": "The terminology must be one of: 'SNOMED-CT', 'LOINC', 'MedDRA', 'WHODrug'.",
      "value": "CUSTOM"
    }
  ]
}
```

### 3.4 Pagination Mechanics
Endpoints returning collections of records support unified pagination mechanisms. Two strategies are offered depending on the endpoint type:

#### 3.4.1 Cursor-Based Pagination (Recommended for Streaming & Real-time Integration)
Required for high-frequency or rapidly changing datasets (e.g., subject events, raw audit log feeds). It avoids the "duplicate-item" anomaly inherent in offset-based indexing during records insertion.
* `limit` (integer, query param): Number of items to return (default: 50, max: 250).
* `starting_after` (string, query param): The cursor (usually a cryptographically encoded ID) defining the starting position.

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
* `offset` (integer, query param): Starting zero-based index.
* `limit` (integer, query param): Number of records.

Headers returned:
`Link: <https://api.cadence-clinical.com/api/v1/mdr/concepts?offset=100&limit=50>; rel="next", <https://api.cadence-clinical.com/api/v1/mdr/concepts?offset=0&limit=50>; rel="prev"`

### 3.5 Payload Compression & Wire Formats
To maximize throughput and comply with data-transmission efficiency goals, all endpoints support payload compression.
* **Accepted Compression Protocols**: `gzip`, `deflate`, `br` (Brotli).
* **Requirements**: Clients should specify `Accept-Encoding: br, gzip` in headers. Responses with payloads larger than 2 KB will automatically be compressed and issued with a corresponding `Content-Encoding: br` header.
* **Standard Content Type**: `application/json; charset=utf-8` or `application/fhir+json; charset=utf-8` for semantic data.

---

## 4. Metadata & MDR Endpoints

The Metadata Repository (MDR) serves as the source of truth for Clinical Biomedical Concepts (BCs), standards governance, study elements, and terminology alignments. The endpoints operate within the `apps/designer` context.

### 4.1 Biomedical Concepts (BCs) Contract

A **Biomedical Concept** is a formal, granular semantic building block representing a unit of clinical observation or collection (e.g., Systolic Blood Pressure, Patient Demographics).

#### 4.1.1 GET /api/v1/mdr/concepts
Fetches a paginated list of Biomedical Concepts.

**Query Parameters**:
* `terminology` (string): Filter by terminology system (e.g., `SNOMED-CT`, `LOINC`).
* `domain` (string): Filter by CDASH domain (e.g., `VS`, `LB`, `DM`).
* `limit` (int): Number of items (default 50).
* `starting_after` (string): Cursor identifier.

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

#### 4.1.3 PUT /api/v1/mdr/concepts/{id}
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

#### 4.2.1 GET /api/v1/mdr/studies/{study_id}/usdm
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
* `q` (string, required): The search string (supports prefix matches and partial tokens).
* `terminology` (string): Restrict search to `MedDRA`, `SNOMED`, `LOINC`, `WHODrug`.
* `concept_class` (string): Restrict by term type (e.g., `LLT`, `PT`, `Observation`).

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
* `dictionary_type` (string, required): One of `MEDDRA`, `WHODRUG`, `LOINC`, `SNOMED`.
* `version` (string, required): The dictionary version identifier (e.g., `26.0`, `2024-03`).
* `files` (multipart binary arrays): The compressed raw dictionary package.

**Request Structure (Curl Example)**:
```bash
curl -X POST https://api.cadence-clinical.com/api/v1/dictionaries/import \
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

#### 5.1.2 GET /api/v1/dictionaries/jobs/{job_id}
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
* `term` (string, required): Text string captured from trial (e.g., "headache").
* `version` (string): MedDRA version (defaults to active version, e.g. `26.0`).
* `target_level` (string): Terminology level (`LLT` or `PT`).

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
* `term` (string, required): Concomitant medication text (e.g., "Aspirin").
* `version` (string): WHODrug version identifier.

**Response (HTTP 200)**:
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
* `code` (string, required): LOINC code (e.g., `2823-3`).

**Response (HTTP 200)**:
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
* `concept_id` (string, required): Root concept ID (e.g., `50960005` - Heart valve structure).
* `relationship_type` (string): Relationship filter (e.g., `IsA`, `Part_Of`).

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
```json
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

#### 6.1.1 GET /api/v1/execution/studies/{study_id}/export
Exports patient capturing datasets in bulk.

**Query Parameters**:
* `format` (string, required): One of `ODM-XML`, `ODM-JSON`, `CSV-ZIP`.
* `version` (string): Target CDISC ODM standard version (`1.3.2` or `2.0`).

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

#### 6.2.1 GET /api/v1/execution/studies/{study_id}/audit-trail
Retrieves the immutable audit trail log.

**Query Parameters**:
* `start_timestamp` (string): ISO 8601 lower bound.
* `end_timestamp` (string): ISO 8601 upper bound.
* `user_id` (string): Filter by acting entity.
* `table_name` (string): Filter by database boundary (e.g., `clinical_records`).

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
openapi: 3.0.3
info:
  title: Cadence Clinical Unified Gateway API
  description: |
    Unified microservices API contract for Cadence Clinical Platform.
    Enforces OIDC/Keycloak authentication, RFC 7807 problem details, and ISO 14155:2020 regulatory compliance.
  version: 1.0.0-PROD
servers:
  - url: https://api.cadence-clinical.com/api/v1
    description: Production API Gateway
  - url: http://localhost:8000/api/v1
    description: Local Dev Gateway Proxy
paths:
  /mdr/concepts:
    get:
      summary: Paginated Biomedical Concepts List
      description: Returns all configured biomedical concepts matching query parameters.
      parameters:
        - name: terminology
          in: query
          required: false
          schema:
            type: string
            enum: [SNOMED-CT, LOINC, MedDRA, WHODrug]
        - name: domain
          in: query
          required: false
          schema:
            type: string
        - name: limit
          in: query
          required: false
          schema:
            type: integer
            default: 50
            maximum: 250
        - name: starting_after
          in: query
          required: false
          schema:
            type: string
      responses:
        '200':
          description: A list of Biomedical Concepts
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ConceptListResponse'
        '401':
          $ref: '#/components/responses/UnauthorizedError'
        '429':
          $ref: '#/components/responses/TooManyRequestsError'
        '500':
          $ref: '#/components/responses/InternalServerError'
    post:
      summary: Create Biomedical Concept
      description: Defines a new Biomedical Concept inside the MDR graph repository.
      security:
        - OAuth2Bearer: [STUDY_DESIGNER, TERMINOLOGY_MANAGER]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreateConceptRequest'
      responses:
        '201':
          description: Concept successfully registered
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ConceptDetail'
        '400':
          $ref: '#/components/responses/ValidationError'
        '401':
          $ref: '#/components/responses/UnauthorizedError'
        '403':
          $ref: '#/components/responses/ForbiddenError'

  /mdr/concepts/{id}:
    put:
      summary: Update Biomedical Concept
      description: Updates an existing concept, creating a new audit history and incrementing version index.
      parameters:
        - name: id
          in: path
          required: true
          schema:
            type: string
      security:
        - OAuth2Bearer: [STUDY_DESIGNER, TERMINOLOGY_MANAGER]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/UpdateConceptRequest'
      responses:
        '200':
          description: Concept updated successfully
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ConceptDetail'
        '400':
          $ref: '#/components/responses/ValidationError'
        '401':
          $ref: '#/components/responses/UnauthorizedError'
        '404':
          $ref: '#/components/responses/NotFoundError'

  /dictionaries/import:
    post:
      summary: Bulk Import Dictionary Package
      description: Accepts zip/tar archives containing raw medical taxonomy catalogs and executes background parse.
      security:
        - OAuth2Bearer: [SYSTEM_ADMIN, TERMINOLOGY_MANAGER]
      requestBody:
        required: true
        content:
          multipart/form-data:
            schema:
              $ref: '#/components/schemas/DictionaryImportPayload'
      responses:
        '202':
          description: Archive accepted, job schedule initialized.
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/JobStatusResponse'
        '401':
          $ref: '#/components/responses/UnauthorizedError'
        '403':
          $ref: '#/components/responses/ForbiddenError'

  /dictionaries/meddra/code:
    get:
      summary: Coding Engine Adverse Event Lookup
      description: Returns semantic matches ordered by search scores based on clinical input terms.
      parameters:
        - name: term
          in: query
          required: true
          schema:
            type: string
        - name: version
          in: query
          required: false
          schema:
            type: string
            default: '26.0'
        - name: target_level
          in: query
          required: false
          schema:
            type: string
            enum: [LLT, PT]
            default: LLT
      responses:
        '200':
          description: List of coded matches
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/MedDRACodingResult'
        '401':
          $ref: '#/components/responses/UnauthorizedError'

  /dictionaries/ucum/convert:
    post:
      summary: Standardize and Convert Units
      description: Evaluates source and target UCUM strings, parses scale ratios, and returns standard float values.
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/UCUMConvertRequest'
      responses:
        '200':
          description: Conversion calculation output
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/UCUMConvertResponse'
        '400':
          $ref: '#/components/responses/ValidationError'

components:
  securitySchemes:
    OAuth2Bearer:
      type: oauth2
      flows:
        authorizationCode:
          authorizationUrl: https://auth.cadence-clinical.com/realms/cadence/protocol/openid-connect/auth
          tokenUrl: https://auth.cadence-clinical.com/realms/cadence/protocol/openid-connect/token
          scopes:
            STUDY_DESIGNER: Administer study schemas
            TERMINOLOGY_MANAGER: Manage medical dictionaries
            SYSTEM_ADMIN: Perform low level bulk transfers

  schemas:
    ConceptListResponse:
      type: object
      required: [object, data, has_more]
      properties:
        object:
          type: string
          example: list
        data:
          type: array
          items:
            $ref: '#/components/schemas/ConceptDetail'
        has_more:
          type: boolean
          example: false
        next_cursor:
          type: string
          nullable: true
          example: null

    ConceptDetail:
      type: object
      required: [id, concept_code, terminology, display_name, definition, version, status, created_at, created_by]
      properties:
        id:
          type: string
          example: bc_sys_bp_001
        concept_code:
          type: string
          example: '271649006'
        terminology:
          type: string
          example: SNOMED-CT
        display_name:
          type: string
          example: Systolic blood pressure
        definition:
          type: string
          example: Systolic pressure of blood in arteries.
        cdash_mapping:
          type: object
          properties:
            domain:
              type: string
              example: VS
            variable_name:
              type: string
              example: VSSBP
            data_type:
              type: string
              example: NUMERIC
        allowable_units:
          type: array
          items:
            type: object
            properties:
              ucum_code:
                type: string
                example: mm[Hg]
              name:
                type: string
                example: millimeter of mercury
        version:
          type: string
          example: 1.0.0
        status:
          type: string
          example: APPROVED
        created_at:
          type: string
          format: date-time
          example: '2026-01-15T08:00:00Z'
        created_by:
          type: string
          example: usr_9921a88b2c410
        updated_at:
          type: string
          format: date-time
          nullable: true
        updated_by:
          type: string
          nullable: true
        reason_for_change:
          type: string
          nullable: true

    CreateConceptRequest:
      type: object
      required: [concept_code, terminology, display_name, definition, change_reason]
      properties:
        concept_code:
          type: string
        terminology:
          type: string
        display_name:
          type: string
        definition:
          type: string
        cdash_mapping:
          type: object
        allowable_units:
          type: array
          items:
            type: object
        change_reason:
          type: string

    UpdateConceptRequest:
      type: object
      required: [display_name, definition, reason_for_change]
      properties:
        display_name:
          type: string
        definition:
          type: string
        cdash_mapping:
          type: object
        allowable_units:
          type: array
          items:
            type: object
        reason_for_change:
          type: string

    DictionaryImportPayload:
      type: object
      required: [dictionary_type, version, files]
      properties:
        dictionary_type:
          type: string
          enum: [MEDDRA, WHODRUG, LOINC, SNOMED]
        version:
          type: string
        files:
          type: string
          format: binary
        parse_multilingual:
          type: boolean
          default: true

    JobStatusResponse:
      type: object
      required: [job_id, dictionary_type, version, status, started_at]
      properties:
        job_id:
          type: string
        dictionary_type:
          type: string
        version:
          type: string
        status:
          type: string
          enum: [PENDING, PROCESSING, COMPLETED, FAILED]
        started_at:
          type: string
          format: date-time
        completed_at:
          type: string
          format: date-time
          nullable: true
        progress_percentage:
          type: integer
          example: 45
        records_imported:
          type: integer
          example: 10245
        errors_encountered:
          type: integer
          example: 0

    MedDRACodingResult:
      type: object
      required: [matches]
      properties:
        matches:
          type: array
          items:
            type: object
            required: [llt_code, llt_name, pt_code, pt_name, hlt_code, hlt_name, hlgt_code, hlgt_name, soc_code, soc_name, score]
            properties:
              llt_code:
                type: string
              llt_name:
                type: string
              pt_code:
                type: string
              pt_name:
                type: string
              hlt_code:
                type: string
              hlt_name:
                type: string
              hlgt_code:
                type: string
              hlgt_name:
                type: string
              soc_code:
                type: string
              soc_name:
                type: string
              primary_soc_flag:
                type: string
                enum: [Y, N]
              score:
                type: number
                format: float

    UCUMConvertRequest:
      type: object
      required: [value, source_unit, target_unit]
      properties:
        value:
          type: number
        source_unit:
          type: string
        target_unit:
          type: string

    UCUMConvertResponse:
      type: object
      required: [source, target, is_compatible, scale_factor]
      properties:
        source:
          type: object
          properties:
            value:
              type: number
            unit:
              type: string
        target:
          type: object
          properties:
            value:
              type: number
            unit:
              type: string
        is_compatible:
          type: boolean
        scale_factor:
          type: number
        offset:
          type: number

    ProblemDetails:
      type: object
      required: [type, title, status, detail, instance, code]
      properties:
        type:
          type: string
        title:
          type: string
        status:
          type: integer
        detail:
          type: string
        instance:
          type: string
        code:
          type: string
        invalid_params:
          type: array
          items:
            type: object
            properties:
              field:
                type: string
              reason:
                type: string
              value:
                type: string

  responses:
    ValidationError:
      description: The provided parameters or payload failed validation rules.
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/ProblemDetails'
    UnauthorizedError:
      description: Authorization header is missing, corrupt, or expired.
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/ProblemDetails'
    ForbiddenError:
      description: User is authenticated but lacks required administrative roles.
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/ProblemDetails'
    NotFoundError:
      description: The requested resource path or entity ID does not exist.
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/ProblemDetails'
    TooManyRequestsError:
      description: Sliding rate limit window threshold has been exceeded.
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/ProblemDetails'
    InternalServerError:
      description: An unhandled exception occurred within the gateway context.
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/ProblemDetails'
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

| Requirement | Cadence Clinical Implementation Paradigm | API Endpoint / Schema Reference |
| :--- | :--- | :--- |
| **Data Traceability (Clause 7.8.2)** | Every record creation, update, or soft deletion captures acting user ID and timestamp. Direct database hard-deletions are prevented at the database driver layer. | `GET /api/v1/execution/studies/{study_id}/audit-trail` |
| **System Access Controls** | Role-Based Access Control (RBAC) configured via Keycloak OIDC. Only users with designated claims can perform mutations. | Sections 3.1 & 7.1 (OAuth2 Bearer Scope mapping) |
| **Change Reason Enforcement** | MDR and EDC interfaces enforce `reason_for_change` parameters on updates. Requests omitting this block fail with HTTP 400. | `PUT /api/v1/mdr/concepts/{id}` (Section 4.1.3) |
| **Data Synchronization Safety** | Sync pipelines use strict validation. Translation of schema state is atomic; failure triggers rolling back. | `POST /api/v1/execution/studies/sync` (Section 6.3.1) |
| **Unit Verification Standards** | Dynamic lookup of numeric metrics against standardized UCUM scale parameters. Prevents anomalous scale discrepancies. | `POST /api/v1/dictionaries/ucum/convert` (Section 5.6.1) |

---

## 10. Multi-Lingual Support Framework

Medical taxonomies (such as MedDRA and SNOMED CT) require native multi-lingual support to allow international clinical trials. Cadence Clinical supports localized concept representations.

### 10.1 Language Negotiation Headers
All search and lookup APIs accept the `Accept-Language` standard HTTP header:
* `Accept-Language: en` (Default: English)
* `Accept-Language: ja` (Japanese)
* `Accept-Language: es` (Spanish)
* `Accept-Language: zh` (Chinese)

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
**End of Specification.**
