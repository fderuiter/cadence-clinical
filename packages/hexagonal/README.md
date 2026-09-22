# Hexagonal Package

## Overview

System-wide Hexagonal Architecture Base and Port/Adapter Utilities

---

## Architectural Boundaries

The `hexagonal` package operates as a modular shared library within the Cadence Clinical ecosystem:
- **Code-Adjacent Domain:** Located strictly within `packages/hexagonal/`.
- **Decoupled Architecture:** Provides explicit interface contracts and reusable domain models without imposing direct database or cross-service runtime bindings.
- **GxP Audit Compliance:** Maintained under strict versioning and change control standards.

---

## Public APIs / Exports

Exports shared utilities, domain interfaces, and models for `hexagonal`.

---

## Dependencies

This package is maintained as a standard shared library within `packages/hexagonal`.

---

## Local Development & Testing

Run unit and contract tests for this package:

```bash
uv run pytest packages/hexagonal
```
