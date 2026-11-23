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

Exports shared utilities, domain interfaces, and models for `usdm-schemas`:

- `usdm-schemas` (`./src/index.ts`): Full CDISC USDM v3/v4 Zod study schemas and standalone AST/cycle graph validator (`./src/graph-validator.ts`).
- `usdm-schemas/epro` (`./src/epro.ts`): Lightweight participant ePRO payload and submission validators (`EPROAnswersSchema`, `EPROSubmissionSchema`, `validateEproPayload`, `validateEproSubmission`) isolated from the full USDM study graph for cold-boot bundle budgets.

#### Exposed CLI Commands

- No build step: the Zod schemas in `src/` are committed. The old Python generator was removed and there is no C# USDM export command yet.
- `test`: `vitest run`

---

## Dependencies

This package relies on the following standard dependencies:

- `vitest`
- `zod`

---

## Local Development & Testing

Run unit and contract tests for this package:

```bash
pnpm --filter usdm-schemas test
```
