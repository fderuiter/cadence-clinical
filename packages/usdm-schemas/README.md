# Usdm Schemas Package

## Overview

Shared domain and infrastructure component `usdm-schemas` within the Cadence Clinical research software platform.

---

## Architectural Boundaries

The `usdm-schemas` package operates as a modular shared library within the Cadence Clinical ecosystem:

- **Code-Adjacent Domain:** Located strictly within `packages/usdm-schemas/`.
- **Decoupled Architecture:** Provides explicit interface contracts and reusable domain models without imposing direct database or cross-service runtime bindings.
- **GxP Audit Compliance:** Maintained under strict versioning and change control standards.

---

## Public APIs / Exports

Exports shared utilities, domain interfaces, and Zod validation models for `usdm-schemas` (`.` for full CDISC USDM + graph validation schemas, including `WrapperSchema` / `Wrapper` for USDM v4 envelopes [`usdmVersion`, `systemName`, `systemVersion`, `study.versions[].studyDesigns[]`] and `USDMStudySchema` accepting both USDM v4 `Wrapper` and legacy USDM v3 payloads; and `./epro` for zero-dependency participant ePRO payload validation).

#### Exposed CLI Commands

- No build step: the Zod schemas in `src/` are committed. The old Python generator was removed and there is no C# USDM export command yet.
- `test`: `vitest run`
- `typecheck`: `tsc --noEmit`

---

## Dependencies

This package relies on the following standard dependencies:

- `typescript`
- `vitest`
- `zod`

---

## Local Development & Testing

Run unit and contract tests for this package:

```bash
pnpm --filter usdm-schemas test
```
