# Engine Core Package

## Overview

Universal Rules, Expression, and FSM Lifecycle Core Engines

---

## Architectural Boundaries

The `engine_core` package operates as a modular shared library within the Cadence Clinical ecosystem:
- **Code-Adjacent Domain:** Located strictly within `packages/engine_core/`.
- **Decoupled Architecture:** Provides explicit interface contracts and reusable domain models without imposing direct database or cross-service runtime bindings.
- **GxP Audit Compliance:** Maintained under strict versioning and change control standards.

---

## Public APIs / Exports

Key exported interfaces and components:
- AUTHORIZED_UNBLINDING_ROLES
- AdverseEventDraftPayload
- AlertLevel
- AnchorEventType
- BOINConfig
- BOINDoseEscalationEngine
- BOINDoseRecommendation
- BusinessHoursSchedule

---

## Dependencies

This package relies on the following standard dependencies:
- `packages-ddf`
- `pydantic>=2.6.0`

---

## Local Development & Testing

Run unit and contract tests for this package:

```bash
uv run pytest packages/engine_core
```
