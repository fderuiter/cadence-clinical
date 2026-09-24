# Subject Portal Microservice

## Overview

Patient/Subject-facing eCOA Portal

---

## Architectural Boundaries

Implements clinical domain services and application workflows within `apps/subject-portal/`.
- **GxP Data Isolation:** Operates within isolated domain boundaries without direct cross-service database access.
- **Gateway Interception:** All client traffic is routed via the API Gateway for authentication, rate-limiting, and audit logging.

---

## Public APIs / Exports

Service routes and contract interfaces:
- `HTTP` endpoints under `/api/v1/subject-portal`

---

## Dependencies

This microservice includes the following module dependencies:
- `@axe-core/playwright`
- `@playwright/test`
- `@vitejs/plugin-vue`
- `@vitest/coverage-v8`
- `fake-indexeddb`
- `jsdom`
- `keycloak-js`
- `msw`
- `ui`
- `usdm-schemas`
- `vite`
- `vitest`
- `vue`

---

## Local Development & Testing

Run microservice unit and integration tests:

```bash
pnpm --filter subject-portal test
```
