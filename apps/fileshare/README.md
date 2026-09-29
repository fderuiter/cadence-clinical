# Fileshare Microservice

## Overview

Cadence File Sharing, Secure Transfers & Media Management Microservice

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
- **Audit-Trail Retention:** `FileAuditLog`, `PlaybackAuditLog`, `ShareGrant` and `GuestLink` rows reference their `FileRecord` with `ON DELETE RESTRICT`, so deleting a parent that still has history fails instead of erasing it. Retire or soft-delete regulated records; never hard-delete them ([ADR-2258](../../docs/adr/2026-09-29-restrict-cascade-deletes-onto-audit-trails-and-regulated-children.md)).

---

## Public APIs / Exports

Service routes and contract interfaces:
- `HTTP` endpoints under `/api/v1/fileshare`

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
uv run pytest apps/fileshare/tests
```
