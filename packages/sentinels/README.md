# Sentinels Package

## Overview

Cadence Clinical Architecture Sentinels

---

## Architectural Boundaries

The `sentinels` package operates as a modular shared library within the Cadence Clinical ecosystem:
- **Code-Adjacent Domain:** Located strictly within `packages/sentinels/`.
- **Decoupled Architecture:** Provides explicit interface contracts and reusable domain models without imposing direct database or cross-service runtime bindings.
- **GxP Audit Compliance:** Maintained under strict versioning and change control standards.

---

## Public APIs / Exports

Key exported interfaces and components:
- SentinelCheck
- SentinelEngine
- SentinelResult
- SentinelCheck
- SentinelEngine
- SentinelResult

---

## Dependencies

This package relies on the following standard dependencies:
- `packages-cli`
- `packages-tooling-core`
- `pydantic>=2.0.0`

---

## Local Development & Testing

Run unit and contract tests for this package:

```bash
uv run pytest packages/sentinels
```
