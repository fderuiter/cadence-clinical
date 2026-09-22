# Cadence Clinical REST API Reference

> **Unified Microservice Architecture & GxP Endpoint Contract.**  
> _All endpoints exposed through the central API Gateway (`localhost:8000/api/v1/*`) with Keycloak OIDC authentication and HMAC signature verification._

---

## 🏛️ Architectural Overview & Gateway Topology

The Cadence Clinical platform routes all client requests through a centralized reverse proxy (`apps/gateway`). Incoming requests undergo:

1. **Keycloak OIDC JWT Token Verification**: Validates user credentials, roles (`sponsor_designer`, `site_crc`, `cra_monitor`, etc.), and tenant/site scoping.
2. **Gateway Signature Injection**: Automatically generates and appends canonical `X-Gateway-Signature` (HMAC-SHA256) headers before dispatching to downstream services.
3. **21 CFR Part 11 Electronic Signature Step-Up**: Mutating operations (e.g. batch approvals, subject randomization, unblinding) require a short-lived (60-second) `sig_token` obtained via TOTP + password re-authentication.
4. **Standardized RFC 7807 Error Responses**: All errors emit RFC 7807 `application/problem+json` envelopes with canonical type URIs, status codes, and detail strings.

---

## 📁 Layered Microservice API Catalog

### Platform & Gateway Infrastructure

| Microservice | Base Route | Port | Endpoints | Persistence | Documentation |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Central API Gateway & Auth Proxy** | `/api/v1` | `8000` | `1036` | `Stateless / Keycloak OIDC` | [View Reference](./gateway.md) |
| **Compliance & Qualification Engine** | `/api/v1/compliance` | `8019` | `1` | `Pytest + Audit Ledgers` | [View Reference](./compliance.md) |

### Protocol & Study Design

| Microservice | Base Route | Port | Endpoints | Persistence | Documentation |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Study Designer & MDR (USDM)** | `/api/v1/designer` | `8001` | `143` | `Neo4j Graph Database` | [View Reference](./designer.md) |
| **Knowledge & SOP Engine** | `/api/v1/knowledge` | `8011` | `47` | `PostgreSQL / SQLite` | [View Reference](./knowledge.md) |
| **Clinical AI Gateway** | `/api/v1/ai` | `8013` | `8` | `Ephemeral / REST` | [View Reference](./ai_gateway.md) |

### Clinical Operations & Execution

| Microservice | Base Route | Port | Endpoints | Persistence | Documentation |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **EDC Execution Engine** | `/api/v1/execution` | `8002` | `237` | `PostgreSQL Relational DB` | [View Reference](./execution.md) |
| **Clinical Trial Management System (CTMS)** | `/api/v1/ctms` | `8005` | `93` | `PostgreSQL / SQLite` | [View Reference](./ctms.md) |
| **Organization Directory** | `/api/v1/org` | `8008` | `34` | `PostgreSQL / SQLite` | [View Reference](./org.md) |
| **Clinical Issue Hub & Query Escalation** | `/api/v1/tickets` | `8012` | `19` | `PostgreSQL / SQLite` | [View Reference](./tickets.md) |
| **Site Payments & Participant Wallet** | `/api/v1/payments` | `8014` | `57` | `PostgreSQL / SQLite` | [View Reference](./payments.md) |

### Safety, Quality & Governance

| Microservice | Base Route | Port | Endpoints | Persistence | Documentation |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Electronic Trial Master File (eTMF)** | `/api/v1/etmf` | `8004` | `50` | `PostgreSQL / SQLite` | [View Reference](./etmf.md) |
| **Quality & CAPA Management** | `/api/v1/quality` | `8006` | `67` | `PostgreSQL / SQLite` | [View Reference](./quality.md) |
| **Safety & Pharmacovigilance Gateway** | `/api/v1/safety` | `8009` | `54` | `PostgreSQL / SQLite` | [View Reference](./safety.md) |
| **Blinded DICOM Adjudication** | `/api/v1/imaging` | `8015` | `24` | `PostgreSQL / SQLite` | [View Reference](./imaging.md) |
| **Electronic Investigator Site Files (eISF)** | `/api/v1/eisf` | `8016` | `18` | `PostgreSQL / SQLite` | [View Reference](./eisf.md) |
| **Object Storage & FileShare Gateway** | `/api/v1/fileshare` | `8018` | `58` | `MinIO / S3 & SQL Envelope` | [View Reference](./fileshare.md) |

### Patient Engagement & Interoperability

| Microservice | Base Route | Port | Endpoints | Persistence | Documentation |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **FHIR & eSource Gateway** | `/api/v1/interop` | `8007` | `95` | `PostgreSQL / SQLite` | [View Reference](./interop.md) |
| **Multi-Channel Notifications Dispatcher** | `/api/v1/notifications` | `8010` | `6` | `PostgreSQL / SQLite` | [View Reference](./notifications.md) |
| **Electronic Consent (eConsent)** | `/api/v1/econsent` | `8017` | `44` | `PostgreSQL / SQLite` | [View Reference](./econsent.md) |

---

## 🔒 Authentication & Standard Gateway Headers

All API requests dispatched to the Gateway must include standard Bearer authorization headers:

```http
GET /api/v1/execution/subjects HTTP/1.1
Host: localhost:8000
Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...
X-Change-Reason: Routine monitoring inspection
Content-Type: application/json
```

### Injected Downstream Service Headers

| Header | Source | Description |
| :--- | :--- | :--- |
| `X-User-Id` | Gateway Proxy | Authenticated Keycloak Subject Identifier (UUID) |
| `X-User-Roles` | Gateway Proxy | Comma-separated list of assigned GxP user roles |
| `X-Gateway-Signature` | Gateway Proxy | Canonical HMAC-SHA256 signature verifying internal traffic origin |
| `X-Change-Reason` | Client Caller | Mandatory 21 CFR Part 11 justification string for all mutating writes |
| `X-Signature-Token` | Client Caller | 60-second step-up TOTP re-authentication token for signature gates |

---

## ⚠️ Standard RFC 7807 Problem Detail Format

In the event of an API error, Cadence returns standardized RFC 7807 error envelopes:

```json
{
  "type": "https://cadence.clinical/errors/resource-locked",
  "title": "Resource Locked",
  "status": 423,
  "detail": "Site SITE-101 has not achieved 6-facet greenlight activation. Randomization blocked.",
  "instance": "/api/v1/execution/subjects/SUBJ-001/randomize"
}
```