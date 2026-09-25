# Clinical AI Gateway

> **Functional Domain:** `Protocol & Study Design`  
> **Service Host Port:** `localhost:8013`  
> **Gateway Proxy Prefix:** `/api/v1/ai`  
> **Persistence Backend:** `Ephemeral / REST`  

## Overview

Model tiering (Tier 1–3), PII/PHI de-identification air-gap proxy, medical coding embeddings, and 21 CFR Part 11 dual-attribution AI ledgers.

---

## Endpoints Summary

| Method | Path | Summary | GxP Auth & Signatures |
| :--- | :--- | :--- | :--- |
| `POST` | [/api/v1/ai/embed](#post-api-v1-ai-embed) | Generate dense vector embeddings for input texts | ✍️ Audited Write |
| `POST` | [/api/v1/ai/generate](#post-api-v1-ai-generate) | Execute AI text generation or structured extraction | ✍️ Audited Write |
| `POST` | [/api/v1/ai/rag/protocol-triage](#post-api-v1-ai-rag-protocol-triage) | Execute grounded Tier 2 protocol RAG triage with citation markers | ✍️ Audited Write |
| `POST` | [/api/v1/ai/reasoning/multi-step](#post-api-v1-ai-reasoning-multi-step) | Execute multi-step clinical reasoning planning loop with grounded citations | ✍️ Audited Write |
| `POST` | [/api/v1/ai/reasoning/plan](#post-api-v1-ai-reasoning-plan) | Initialize or advance a caller-managed multi-step clinical reasoning plan | ✍️ Audited Write |
| `GET` | [/api/v1/ai/tiers](#get-api-v1-ai-tiers) | Retrieve active configuration and model mapping across execution tiers | 👀 Read-Only |
| `GET` | [/health](#get-health) | Health Check | 👀 Read-Only |
| `GET` | [/healthz](#get-healthz) | Health Check | 👀 Read-Only |

---

## Granular Endpoint Specifications

### `POST` `/api/v1/ai/embed`

**Summary:** Generate dense vector embeddings for input texts  
**Operation ID:** `generate_embeddings_api_v1_ai_embed_post`  

**Clinical & GxP Rationale:**  
Generate float vector embeddings for input text strings.

Args:
    payload: EmbeddingRequestDTO containing batch of text strings and model tier.
    engine: Injected AIEnginePort adapter.

Returns:
    EmbeddingResponseDTO containing generated float vectors and usage metrics.

Raises:
    HTTPException: If embedding generation encounters an upstream error.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `EmbeddingRequestDTO`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `input_texts` | `Array<string>` | ✔ Yes | Batch of text strings to embed. |
| `tier` | `ModelTier` | No | Embedding model tier (defaults to Tier 1 local). |
| `model_override` | `string` | `null` | No | Optional model override. |
| `tenant_id` | `string` | `null` | No | Tenant identifier. |
| `custom_terms` | `Array<string>` | `null` | No | Optional custom literal terms or patient names to redact. |
| `enable_deid` | `boolean` | No | Whether to apply in-flight de-identification air-gap. |
| `compliance_profile` | `string` | No | Compliance profile for PHI detection (HIPAA, GDPR, EU_CTR). |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `EmbeddingResponseDTO` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ai/api/v1/ai/embed', {
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
        '/api/v1/ai/api/v1/ai/embed',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ai/api/v1/ai/embed' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ai/generate`

**Summary:** Execute AI text generation or structured extraction  
**Operation ID:** `generate_completion_api_v1_ai_generate_post`  

**Clinical & GxP Rationale:**  
Generate completion text or structured JSON data conforming to a requested schema.

Args:
    payload: GenerationRequestDTO containing prompt/messages, tier, and optional schema.
    engine: Injected AIEnginePort adapter.

Returns:
    GenerationResponseDTO with content, structured data, token usage, and telemetry.

Raises:
    HTTPException: If payload is missing prompt/messages or inference fails.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `GenerationRequestDTO`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `prompt` | `string` | `null` | No | Single-turn prompt string. |
| `messages` | `Array<ChatMessageDTO>` | `null` | No | Multi-turn conversation history. |
| `tools` | `Array<ToolDefinitionDTO>` | `null` | No | List of tool definitions available for model tool selection. |
| `tool_choice` | `string` | `object` | `null` | No | Tool choice configuration (e.g. 'auto', 'required', or explicit selection). |
| `tool_responses` | `Array<ToolResponseDTO>` | `null` | No | List of execution responses for previously requested tool calls. |
| `tier` | `ModelTier` | No | Execution tier (tier_1_local, tier_2_fast, tier_3_frontier). |
| `model_override` | `string` | `null` | No | Explicit model name override. |
| `temperature` | `number` | No | Sampling temperature. |
| `max_tokens` | `integer` | `null` | No | Maximum tokens to generate. |
| `response_schema` | `object` | `null` | No | JSON Schema for Pydantic v2 structured outputs. |
| `tenant_id` | `string` | `null` | No | Tenant identifier for isolation and auditing. |
| `study_id` | `string` | `null` | No | Study protocol scope identifier. |
| `custom_terms` | `Array<string>` | `null` | No | Optional custom literal terms or patient names to redact. |
| `enable_deid` | `boolean` | No | Whether to apply in-flight de-identification air-gap. |
| `compliance_profile` | `string` | No | Compliance profile for PHI detection (HIPAA, GDPR, EU_CTR). |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `GenerationResponseDTO` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ai/api/v1/ai/generate', {
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
        '/api/v1/ai/api/v1/ai/generate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ai/api/v1/ai/generate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ai/rag/protocol-triage`

**Summary:** Execute grounded Tier 2 protocol RAG triage with citation markers  
**Operation ID:** `triage_protocol_query_api_v1_ai_rag_protocol_triage_post`  

**Clinical & GxP Rationale:**  
Executes grounded clinical RAG answering support inquiries with verbatim citations.

Enforces:
- Verbatim citation markers: `[Protocol v2.1, Section 7.3, Page 42]`
- Mathematical faithfulness calculation (85% confidence gate)
- Fail-closed behavior on missing or unapproved protocol context

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ProtocolRAGRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `query` | `string` | ✔ Yes | Clinical inquiry text from user/ticket. |
| `study_id` | `string` | ✔ Yes | Target study identifier. |
| `protocol_version` | `string` | `null` | No | Optional target protocol version. |
| `context_chunks` | `Array<ProtocolRAGContextChunk>` | No | Retrieved protocol chunks from Knowledge Hub. |
| `top_k` | `integer` | No | — |
| `tenant_id` | `string` | No | Tenant identifier. |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ProtocolRAGResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ai/api/v1/ai/rag/protocol-triage', {
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
        '/api/v1/ai/api/v1/ai/rag/protocol-triage',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ai/api/v1/ai/rag/protocol-triage' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ai/reasoning/multi-step`

**Summary:** Execute multi-step clinical reasoning planning loop with grounded citations  
**Operation ID:** `execute_multi_step_reasoning_api_v1_ai_reasoning_multi_step_post`  

**Clinical & GxP Rationale:**  
Executes a multi-step clinical reasoning workflow for protocol synthesis and patient data.

Enforces:
- Multi-step planning loops breaking down trial queries into discrete steps
- In-flight PHI masking using temporary surrogate tokens for intermediate steps
- Evaluation of protocol citations and 85% confidence threshold gate (fail-closed)
- Stateless persistence with caller-managed execution state context
- Staging generated clinical records into draft state requiring human e-signature
- Mandatory tenant and audit context validation

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ClinicalReasoningRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `query` | `string` | ✔ Yes | Complex clinical trial reasoning query. |
| `tenant_id` | `string` | ✔ Yes | Mandatory tenant isolation identifier. |
| `study_id` | `string` | `null` | No | Target study identifier. |
| `protocol_chunks` | `Array<ProtocolRAGContextChunk>` | No | Clinical trial protocol chunks for citation verification. |
| `patient_records` | `Array<object>` | `object` | `string` | `null` | No | Patient records or clinical context containing sensitive identifiers. |
| `execution_state` | `ExecutionStateContext` | `null` | No | Caller-managed execution state context for stateless continuation. |
| `min_citation_confidence` | `number` | No | Minimum required citation confidence score (default 0.85). |
| `compliance_profile` | `string` | No | Compliance profile for PHI detection (HIPAA, GDPR, EU_CTR). |
| `esignature_context` | `object` | `null` | No | Electronic signature & audit context passed across execution steps. |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ClinicalReasoningResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ai/api/v1/ai/reasoning/multi-step', {
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
        '/api/v1/ai/api/v1/ai/reasoning/multi-step',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ai/api/v1/ai/reasoning/multi-step' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ai/reasoning/plan`

**Summary:** Initialize or advance a caller-managed multi-step clinical reasoning plan  
**Operation ID:** `plan_clinical_reasoning_api_v1_ai_reasoning_plan_post`  

**Clinical & GxP Rationale:**  
Initialize or advance a multi-step clinical reasoning loop statelessly.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ClinicalReasoningRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `query` | `string` | ✔ Yes | Complex clinical trial reasoning query. |
| `tenant_id` | `string` | ✔ Yes | Mandatory tenant isolation identifier. |
| `study_id` | `string` | `null` | No | Target study identifier. |
| `protocol_chunks` | `Array<ProtocolRAGContextChunk>` | No | Clinical trial protocol chunks for citation verification. |
| `patient_records` | `Array<object>` | `object` | `string` | `null` | No | Patient records or clinical context containing sensitive identifiers. |
| `execution_state` | `ExecutionStateContext` | `null` | No | Caller-managed execution state context for stateless continuation. |
| `min_citation_confidence` | `number` | No | Minimum required citation confidence score (default 0.85). |
| `compliance_profile` | `string` | No | Compliance profile for PHI detection (HIPAA, GDPR, EU_CTR). |
| `esignature_context` | `object` | `null` | No | Electronic signature & audit context passed across execution steps. |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ClinicalReasoningResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ai/api/v1/ai/reasoning/plan', {
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
        '/api/v1/ai/api/v1/ai/reasoning/plan',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ai/api/v1/ai/reasoning/plan' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ai/tiers`

**Summary:** Retrieve active configuration and model mapping across execution tiers  
**Operation ID:** `list_tiers_api_v1_ai_tiers_get`  

**Clinical & GxP Rationale:**  
List all available execution tiers and active model configurations.

Args:
    engine: Injected AIEnginePort adapter.

Returns:
    List of TierInfoDTO describing active tier mappings and capabilities.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<TierInfoDTO>` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ai/api/v1/ai/tiers', {
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
        '/api/v1/ai/api/v1/ai/tiers',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ai/api/v1/ai/tiers' \
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
const response = await fetch('http://localhost:8000/api/v1/ai/health', {
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
        '/api/v1/ai/health',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ai/health' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/healthz`

**Summary:** Health Check  
**Operation ID:** `health_check_healthz_get`  

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
const response = await fetch('http://localhost:8000/api/v1/ai/healthz', {
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
        '/api/v1/ai/healthz',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ai/healthz' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---
