# Cli Package

## Overview

Cadence Clinical Developer Experience and Unified CLI

---

## Architectural Boundaries

The `cli` package operates as a modular shared library within the Cadence Clinical ecosystem:
- **Code-Adjacent Domain:** Located strictly within `packages/cli/`.
- **Decoupled Architecture:** Provides explicit interface contracts and reusable domain models without imposing direct database or cross-service runtime bindings.
- **GxP Audit Compliance:** Maintained under strict versioning and change control standards.

---

## Public APIs / Exports

Key exported interfaces and components:
- CadenceMcpServer

---

## Dependencies

This package relies on the following standard dependencies:
- `httpx>=0.27.0`
- `packages-storage`
- `packages-testing`
- `packages-tooling-core`
- `rich>=13.7.0`
- `typer>=0.12.0`

---

## Local Development & Testing

Run unit and contract tests for this package:

```bash
uv run pytest packages/cli
```
