# Testing Package

## Overview

Cadence Clinical Centralized Test Infrastructure & Domain Factories

---

## Architectural Boundaries

The `testing` package operates as a modular shared library within the Cadence Clinical ecosystem:
- **Code-Adjacent Domain:** Located strictly within `packages/testing/`.
- **Decoupled Architecture:** Provides explicit interface contracts and reusable domain models without imposing direct database or cross-service runtime bindings.
- **GxP Audit Compliance:** Maintained under strict versioning and change control standards.

---

## Public APIs / Exports

Key exported interfaces and components:
- AuditLogFactory
- ClinicalObservationFactory
- ConsentRecordFactory
- DocumentMetadataFactory
- InMemoryNotificationDispatcher
- InMemoryRepository
- InMemoryStoragePort
- ProtocolDefinitionFactory

---

## Dependencies

This package relies on the following standard dependencies:
- `packages-ddf`
- `packages-hexagonal`
- `packages-security`
- `packages-storage`
- `pydantic>=2.6.0`

---

## Local Development & Testing

Run unit and contract tests for this package:

```bash
uv run pytest packages/testing
```
