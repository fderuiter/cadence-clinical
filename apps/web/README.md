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

## 🧪 Testing

Run frontend unit and integration tests using Vitest:

```bash
pnpm --filter web run test
```
