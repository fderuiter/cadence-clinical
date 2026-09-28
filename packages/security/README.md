# Security Package

## Overview

Security utilities package

---

## Architectural Boundaries

The `security` package operates as a modular shared library within the Cadence Clinical ecosystem:
- **Code-Adjacent Domain:** Located strictly within `packages/security/`.
- **Decoupled Architecture:** Provides explicit interface contracts and reusable domain models without imposing direct database or cross-service runtime bindings.
- **GxP Audit Compliance:** Maintained under strict versioning and change control standards.

---

## Public APIs / Exports

Key exported interfaces and components:
- ClinicalStaffRole
- TokenConsumptionCache
- token_consumption_cache
- verify_and_consume_sig_token
- TrialRole
- check_trial_role
- enforce_site_isolation
- GatewayBaseClient

---

## Dependencies

This package relies on the following standard dependencies:
- `cryptography>=4.5.0`
- `ecdsa>=0.19.2`
- `fastapi>=0.139.2,<0.140.0`
- `httpx>=0.27.0`
- `packages-telemetry`
- `pydantic>=2.6.0`
- `python-jose[cryptography]>=3.3.0`
- `redis>=5.0.0`

---

## Local Development & Testing

Run unit and contract tests for this package:

```bash
uv run pytest packages/security
```
