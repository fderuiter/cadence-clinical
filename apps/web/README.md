# Cadence Clinical - Web Frontend Application

This directory contains the primary frontend web application for Cadence Clinical, built using **Vue 3**, **Pinia**, and **Vite**.

---

## 🔒 Gateway Integration & Local API Client

To maintain GxP compliance and enforce Part 11 requirements (e.g. rate-limiting, step-up re-authentication, and signature verification), the frontend SPA does not call backend microservices directly. It communicates **exclusively** with the API Gateway.

### API Client Wrapper (`apps/web/src/api/apiClient.js`)

The application features a secure, centralized `apiClient` wrapper around the browser `fetch` API:

- **Base URL Configuration:** Automatically reads from `import.meta.env.VITE_API_BASE_URL` at runtime, defaulting to the local API Gateway at `http://localhost:8000`.
- **Zero-Knowledge Browser Context:** Never stores, manages, or uses signing keys/secrets or generates cryptographic signatures in the browser code.
- **OIDC Authentication Mapping:** Dynamically resolves the active **Keycloak bearer token** from the Pinia `useAuthStore` and attaches it to all requests as `Authorization: Bearer <token>`.
- **Change Reason Propagation:** Automatically attaches the custom `X-Change-Reason` header on mutations (`POST`, `PUT`, `DELETE`, `PATCH`) if supplied by the caller, supporting regulatory audit trail requirements.

---

## 🛠️ Microservice Service Modules

Service-specific endpoint contracts are isolated into standalone service files to interact seamlessly with the API Gateway:

- **Designer (`api/designer.js`):** Integrates with study configuration, CDISC USDM models, and metadata rules.
- **Execution (`api/execution.js`):** Integrates with clinical EDC data entry, patient consent recording, query lifecycles, and form submissions.
- **eTMF (`api/etmf.js`):** Integrates with document ingestion, indexing, and expected document list (EDL) completeness tracking.
- **Interop (`api/interop.js`):** Integrates with FHIR bundle prefills, offline ePRO questionnaire synchronization, and assigned instruments lists.

---

## ⚙️ Local Development & API Proxy Configuration

During local development, you may configure the local Vite dev server to proxy requests to the API gateway.

### 1. Environment Variable Override

You can override the target API Gateway URL by creating a local environment file `.env.local`:

```bash
VITE_API_BASE_URL=http://localhost:8000
```

### 2. Vite Proxy Server

Vite is pre-configured (`vite.config.js`) to automatically route any local `/api` traffic to the Gateway:

```javascript
server: {
  port: 3000,
  strictPort: true,
  proxy: {
    "/api": {
      target: "http://localhost:8000",
      changeOrigin: true,
      secure: false,
    },
  },
}
```

This ensures that calling `/api/v1/...` relative paths in local components routes cleanly through the gateway proxy in local browser environments, resolving potential CORS issues or local development routing gaps.

---

## 🔑 Keycloak Authentication, Ports, & Role Normalization

To enforce robust Identity and Access Management (IAM), the frontend integrates seamlessly with Keycloak using **Authorization Code Flow + PKCE**.

### Dev Port & Redirect URI Alignment

The Keycloak client config and local development server ports must be perfectly aligned:

- **Local Dev Server:** Runs on port **`3000`** (strictly locked in `vite.config.js`).
- **Keycloak Configuration:** The `cadence-realm.json` file (under the `cadence` realm, client `cadence-web`) is configured with matching redirect URIs and web origins:
  - `redirectUris`: `http://localhost:3000/*`
  - `webOrigins`: `http://localhost:3000`
- **Keycloak Base URL:** By default, Keycloak is expected to run at `http://localhost:8080/`.

### Clinical Role to Keycloak Realm Role Mapping

The application utilizes a Pinia auth store (`apps/web/src/stores/auth.js`) to normalize incoming Keycloak roles into standardized UI-scoped roles. The normalization mapping details are as follows:

| Clinical / Business Role   | Seeded Keycloak Realm Role            | Normalized UI Role(s)      | Mapping & Normalization Logic                                                                                                  |
| -------------------------- | ------------------------------------- | -------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| **Site Coordinator / CRC** | `Site Investigator`                   | `site_investigator`, `crc` | Lowercased, spaces/dashes converted to underscores, mapped via `ROLE_ALIASES`.                                                 |
| **CRA / Monitor**          | `CRA`                                 | `cra`, `monitor`           | Normalizes to `cra` and mapped to `monitor` alias.                                                                             |
| **Data Manager**           | `Data Manager`                        | `data_manager`             | Lowercased and converted to snake_case.                                                                                        |
| **TMF Auditor**            | `Auditor`                             | `auditor`, `tmf_auditor`   | Maps Keycloak `Auditor` role to both `auditor` and `tmf_auditor` normalized aliases.                                           |
| **Study Designer**         | `Sponsor Designer` / `study_designer` | `sponsor_designer`         | Maps Keycloak roles `Sponsor Designer`, `study_designer`, and `designer` into the single canonical UI role `sponsor_designer`. |
| **Sponsor Admin**          | `Sponsor Admin`                       | `sponsor_admin`            | Lowercased and converted to snake_case.                                                                                        |

This central normalization layer decouples Keycloak-level role naming from UI-level routing and capability checks, providing a robust, GxP-compliant RBAC layer.

---

## 🧪 Testing

Run frontend unit and integration tests using Vitest:

```bash
pnpm --filter web run test
```
