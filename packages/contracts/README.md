# Contracts Package

## Overview

Shared domain and infrastructure component `contracts` within the Cadence Clinical research software platform.

---

## Architectural Boundaries

The `contracts` package operates as a modular shared library within the Cadence Clinical ecosystem:
- **Code-Adjacent Domain:** Located strictly within `packages/contracts/`.
- **Decoupled Architecture:** Provides explicit interface contracts and reusable domain models without imposing direct database or cross-service runtime bindings.
- **GxP Audit Compliance:** Maintained under strict versioning and change control standards.

---

## Public APIs / Exports

Exports shared utilities, domain interfaces, and models for `contracts`.

---

## Dependencies

This package is maintained as a standard shared library within `packages/contracts`.

---

## Local Development & Testing

Run unit and contract tests for this package:

```bash
uv run pytest packages/contracts
```
