# Tooling Core Package

## Overview

Cadence Clinical Unified Tooling Core Package

---

## Architectural Boundaries

The `tooling_core` package operates as a modular shared library within the Cadence Clinical ecosystem:
- **Code-Adjacent Domain:** Located strictly within `packages/tooling_core/`.
- **Decoupled Architecture:** Provides explicit interface contracts and reusable domain models without imposing direct database or cross-service runtime bindings.
- **GxP Audit Compliance:** Maintained under strict versioning and change control standards.

---

## Public APIs / Exports

Key exported interfaces and components:
- CommandEnvelope
- DoctorDiagnoseRequest
- DoctorDiagnoseResponse
- FastTestRequest
- FastTestResponse
- SeedScenarioRequest
- SeedScenarioResponse
- SentinelRunRequest

---

## Dependencies

This package relies on the following standard dependencies:
- `pydantic>=2.0.0`

---

## Local Development & Testing

Run unit and contract tests for this package:

```bash
uv run pytest packages/tooling_core
```
