# Database Package

## Overview

Database utilities package

---

## Architectural Boundaries

The `database` package operates as a modular shared library within the Cadence Clinical ecosystem:
- **Code-Adjacent Domain:** Located strictly within `packages/database/`.
- **Decoupled Architecture:** Provides explicit interface contracts and reusable domain models without imposing direct database or cross-service runtime bindings.
- **GxP Audit Compliance:** Maintained under strict versioning and change control standards.

---

## Public APIs / Exports

Key exported interfaces and components:
- class `UTCDateTime`
- class `AIReviewStatus`
- class `AIAssistedRecordMixin`
- class `Part11AuditMixin`
- class `AIGenerationManifest`
- class `AuditFields`
- class `WrappedGraphTransaction`
- class `WrappedGraphSession`

---

## Dependencies

This package relies on the following standard dependencies:
- `alembic>=1.13.0`
- `asyncpg>=0.29.0`
- `fastapi>=0.139.2,<0.140.0`
- `packages-hexagonal`
- `packages-security`
- `pydantic>=2.6.0`
- `sqlalchemy>=2.0.28`

---

## Local Development & Testing

Run unit and contract tests for this package:

```bash
uv run pytest packages/database
```
