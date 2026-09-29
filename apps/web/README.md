# Cadence Clinical - Web Frontend Application

## Overview

This directory contains the primary frontend web application for Cadence Clinical, built using **Vue 3**, **Pinia**, and **Vite**.

---

## Architectural Boundaries

### 🔒 Gateway Integration & Local API Client

To maintain GxP compliance and enforce Part 11 requirements (e.g. rate-limiting, step-up re-authentication, and signature verification), the frontend SPA does not call backend microservices directly. It communicates **exclusively** with the API Gateway.

#### API Client Wrapper (`apps/web/src/api/apiClient.js`)

The application features a secure, centralized `apiClient` wrapper around the browser `fetch` API:

- **Base URL Configuration:** Automatically reads from `import.meta.env.VITE_API_BASE_URL` at runtime, defaulting to the local API Gateway at `http://localhost:8000`.
- **Zero-Knowledge Browser Context:** Never stores, manages, or uses signing keys/secrets or generates cryptographic signatures in the browser code.
- **OIDC Authentication Mapping:** Dynamically resolves the active **Keycloak bearer token** from the Pinia `useAuthStore` and attaches it to all requests as `Authorization: Bearer <token>`.
- **Change Reason Propagation:** Automatically attaches the custom `X-Change-Reason` header on mutations (`POST`, `PUT`, `DELETE`, `PATCH`) if supplied by the caller, supporting regulatory audit trail requirements.

### 🔑 Keycloak Authentication, Ports, & Role Normalization

To enforce robust Identity and Access Management (IAM), the frontend integrates seamlessly with Keycloak using **Authorization Code Flow + PKCE**.

#### Dev Port & Redirect URI Alignment

The Keycloak client config and local development server ports must be perfectly aligned:

- **Local Dev Server:** Runs on port **`3000`** (strictly locked in `vite.config.js`).
- **Keycloak Configuration:** The `cadence-realm.json` file (under the `cadence` realm, client `cadence-web`) is configured with matching redirect URIs and web origins:
  - `redirectUris`: `http://localhost:3000/*`
  - `webOrigins`: `http://localhost:3000`
- **Keycloak Base URL:** By default, Keycloak is expected to run at `http://localhost:8080/`.

#### Clinical Role to Keycloak Realm Role Mapping

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

## Screen availability

Most screens were written against the Python services and call endpoints no .NET service serves. Only the eCRF and CTMS screens have a backend behind them (Execution). Every other screen is hidden from the sidebar and command palette, and its route shows a "not available yet" page, until its module serves the endpoints it needs. `AVAILABLE_SCREENS` in `src/router/availability.js` lists the visible screens. [`docs/web/API_MIGRATION.md`](../../docs/web/API_MIGRATION.md) has the screen-by-screen inventory and the steps for moving a screen over, and epic #4833 has the plan.

The clients in `src/api/` are still typed against the Python-era specs in `docs/openapi/legacy/`. They are replaced as each screen moves over.

---

## Public APIs / Exports

### 🛠️ Microservice Service Modules

Service-specific endpoint contracts are isolated into standalone service files to interact seamlessly with the API Gateway:

- **Designer (`api/designer.js`):** Integrates with study configuration, CDISC USDM models, and metadata rules.
- **Execution (`api/execution.js`):** Integrates with clinical EDC data entry, patient consent recording, query lifecycles, and form submissions.
- **eTMF (`api/etmf.js`):** Integrates with document ingestion, indexing, and expected document list (EDL) completeness tracking.
- **Interop (`api/interop.js`):** Integrates with FHIR bundle prefills, offline ePRO questionnaire synchronization, and assigned instruments lists.

---

## Dependencies

- **Framework & Build:** Vue 3, Pinia, Vite, Vue Router
- **HTTP Client:** Native `fetch` with `apiClient.js` wrapper
- **UI Components:** Tailwind CSS / Lucide icons
- **Package Manager:** pnpm

---

## Local Development & Testing

### ⚙️ Local Development & API Proxy Configuration

During local development, you may configure the local Vite dev server to proxy requests to the API gateway.

#### 1. Environment Variable Override

You can override the target API Gateway URL by creating a local environment file `.env.local`:

```bash
VITE_API_BASE_URL=http://localhost:8000
```

#### 2. Vite Proxy Server

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

### Demo mode and API failures

Demo mode is opt-in at build time: `pnpm --filter web dev --mode demo`, `pnpm build:demo`, or
`VITE_DEMO_MODE=true` (`src/utils/demoMode.js`). Only a demo build signs in with the offline demo identity,
starts the MSW mock API and loads sample data. Every other build talks to the gateway: when Keycloak is
unreachable the app opens the login page and sign-in reports the problem, and a failed API call shows an error
alert (`useToastStore().apiError(title, err)` from `src/stores/toasts.js`) and leaves the screen as it was. Never
substitute sample data or report success when a call fails. Tests opt into a demo build with
`vi.stubEnv("VITE_DEMO_MODE", "true")`.

### 🧪 Testing

Run frontend unit and integration tests using Vitest:

```bash
pnpm --filter web run test
```
