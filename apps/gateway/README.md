# Gateway Microservice

## Overview

Clinical application microservice for `gateway` within the Cadence Clinical platform.

---

## Architectural Boundaries

Implements clinical domain services and application workflows within `apps/gateway/`.
- **GxP Data Isolation:** Operates within isolated domain boundaries without direct cross-service database access.
- **Gateway Interception:** All client traffic is routed via the API Gateway for authentication, rate-limiting, and audit logging.

---

## Public APIs / Exports

Service routes and contract interfaces:
- `HTTP` endpoints under `/api/v1/gateway`

---

## Dependencies

Interacts with core platform capabilities and shared libraries in `packages/`.

---

## Local Development & Testing

Run microservice unit and integration tests:

```bash
uv run pytest apps/gateway/tests
```
