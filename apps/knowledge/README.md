# Knowledge Microservice

## Overview

Cadence Knowledge & Support Hub microservice

---

## Architectural Boundaries


Conforms strictly to Enterprise Hexagonal standards with clean decoupling:
- `ports/`: Port interfaces defining driving and driven contracts.
- `adapters/`: Database, S3/blob, and HTTP adapter implementations.
- `domain/`: Pure business entity domain models and core domain logic.
- `application/`: Business use case drivers coordinating ports and entities.
- `presentation/`: FastAPI / Minimal API router wrappers.

- **GxP Data Isolation:** Operates within isolated domain boundaries without direct cross-service database access.
- **Gateway Interception:** All client traffic is routed via the API Gateway for authentication, rate-limiting, and audit logging.

---

## Public APIs / Exports

Service routes and contract interfaces:
- `HTTP` endpoints under `/api/v1/knowledge`

---

## Dependencies

This microservice includes the following module dependencies:
- `aiosqlite`
- `asyncpg`
- `fastapi`
- `httpx`
- `pydantic>=2`
- `sqlalchemy[asyncio]`

---

## Local Development & Testing

Run microservice unit and integration tests:

```bash
uv run pytest apps/knowledge/tests
```
