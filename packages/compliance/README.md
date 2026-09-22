# Compliance Package

## Overview

Compliance package

---

## Architectural Boundaries

The `compliance` package operates as a modular shared library within the Cadence Clinical ecosystem:
- **Code-Adjacent Domain:** Located strictly within `packages/compliance/`.
- **Decoupled Architecture:** Provides explicit interface contracts and reusable domain models without imposing direct database or cross-service runtime bindings.
- **GxP Audit Compliance:** Maintained under strict versioning and change control standards.

---

## Public APIs / Exports

Key exported interfaces and components:
- AIAssistedRecordMixin
- AIGenerationManifest
- AIReviewStatus
- AuditChainIntegrityError
- AuditFields
- AuditImmutabilityError
- AuditJustificationError
- ComplianceAuditFramework

---

## Dependencies

This package relies on the following standard dependencies:
- `cryptography>=4.5.0`
- `fastapi>=0.139.2,<0.140.0`
- `packages-database`
- `packages-deid`
- `packages-security`
- `pydantic>=2.6.0`

---

## Local Development & Testing

Run unit and contract tests for this package:

```bash
uv run pytest packages/compliance
```
