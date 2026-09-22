# Core Models Package

## Overview

Core clinical models package

---

## Architectural Boundaries

The `core-models` package operates as a modular shared library within the Cadence Clinical ecosystem:
- **Code-Adjacent Domain:** Located strictly within `packages/core-models/`.
- **Decoupled Architecture:** Provides explicit interface contracts and reusable domain models without imposing direct database or cross-service runtime bindings.
- **GxP Audit Compliance:** Maintained under strict versioning and change control standards.

---

## Public APIs / Exports

Key exported interfaces and components:
- CITATION_REGEX
- CONFIDENCE_THRESHOLD
- ParsedCitation
- ProtocolRAGContextChunk
- TIER_2_GROUNDED_RAG_SYSTEM_PROMPT
- evaluate_rag_faithfulness
- format_citation_marker
- format_grounded_rag_prompt

---

## Dependencies

This package relies on the following standard dependencies:
- `pydantic>=2.6.0`

---

## Local Development & Testing

Run unit and contract tests for this package:

```bash
uv run pytest packages/core-models
```
