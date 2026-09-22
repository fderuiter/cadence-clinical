# Ddf Package

## Overview

Digital Data Flow (DDF) Deep Compiler Pipeline

---

## Architectural Boundaries

The `ddf` package operates as a modular shared library within the Cadence Clinical ecosystem:
- **Code-Adjacent Domain:** Located strictly within `packages/ddf/`.
- **Decoupled Architecture:** Provides explicit interface contracts and reusable domain models without imposing direct database or cross-service runtime bindings.
- **GxP Audit Compliance:** Maintained under strict versioning and change control standards.

---

## Public APIs / Exports

Key exported interfaces and components:
- AccessibilityReport
- AccessibilityValidatorPort
- AstEditCheck
- CdiscMetadata
- ClinicalDataType
- CompileOptions
- CompilerWarning
- ConstraintDefinition

---

## Dependencies

This package relies on the following standard dependencies:
- `defusedxml>=0.7.1,<0.8.0`
- `jinja2>=3.1.6,<4.0.0`
- `pydantic>=2.6.0`

---

## Local Development & Testing

Run unit and contract tests for this package:

```bash
uv run pytest packages/ddf
```
