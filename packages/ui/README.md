# Ui Package

## Overview

Shared domain and infrastructure component `ui` within the Cadence Clinical research software platform.

---

## Architectural Boundaries

The `ui` package operates as a modular shared library within the Cadence Clinical ecosystem:
- **Code-Adjacent Domain:** Located strictly within `packages/ui/`.
- **Decoupled Architecture:** Provides explicit interface contracts and reusable domain models without imposing direct database or cross-service runtime bindings.
- **GxP Audit Compliance:** Maintained under strict versioning and change control standards.

---

## Public APIs / Exports

Exports shared utilities, domain interfaces, and models for `ui`.

#### Exposed CLI Commands
- `build`: `node apps/web/src/types/generate-types.js && vite build`
- `test`: `vitest run --testTimeout=30000`
- `lint`: `eslint .`
- `format`: `prettier --write .`

---

## Dependencies

This package relies on the following standard dependencies:
- `@vitejs/plugin-vue`
- `axe-core`
- `pinia`
- `typescript`
- `vite`
- `vitest`
- `vue`

---

## Local Development & Testing

Run unit and contract tests for this package:

```bash
pnpm --filter ui test
```
