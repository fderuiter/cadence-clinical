# Etmf Microservice

## Overview

eTMF microservice

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
- `HTTP` endpoints under `/api/v1/etmf`

---

## Dependencies

This microservice includes the following module dependencies:
- `PyMuPDF>=1.24.0`
- `asyncpg>=0.29.0`
- `cryptography>=4.5.0`
- `fastapi>=0.139.2,<0.140.0`
- `pydantic>=2.6.0`
- `sqlalchemy>=2.0.28`
- `sqlmodel>=0.0.39`

---

## Local Development & Testing

Run microservice unit and integration tests:

```bash
uv run pytest apps/etmf/tests
```
