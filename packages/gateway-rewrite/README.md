# Gateway Rewrite Package

## Overview

Core promise coalescing and key fetching module for NestJS Gateway rewrite

---

## Architectural Boundaries

The `gateway-rewrite` package operates as a modular shared library within the Cadence Clinical ecosystem:
- **Code-Adjacent Domain:** Located strictly within `packages/gateway-rewrite/`.
- **Decoupled Architecture:** Provides explicit interface contracts and reusable domain models without imposing direct database or cross-service runtime bindings.
- **GxP Audit Compliance:** Maintained under strict versioning and change control standards.

---

## Public APIs / Exports

Exports shared utilities, domain interfaces, and models for `gateway-rewrite`.

#### Exposed CLI Commands
- `test`: `vitest run`
- `build`: `tsc`

---

## Dependencies

This package relies on the following standard dependencies:
- `@nestjs/common`
- `@nestjs/core`
- `@types/node`
- `pino`
- `reflect-metadata`
- `rxjs`
- `typescript`
- `usdm-schemas`
- `vitest`

---

## Local Development & Testing

Run unit and contract tests for this package:

```bash
pnpm --filter gateway-rewrite test
```
