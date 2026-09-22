# Telemetry Package

## Overview

Shared OpenTelemetry telemetry module

---

## Architectural Boundaries

The `telemetry` package operates as a modular shared library within the Cadence Clinical ecosystem:
- **Code-Adjacent Domain:** Located strictly within `packages/telemetry/`.
- **Decoupled Architecture:** Provides explicit interface contracts and reusable domain models without imposing direct database or cross-service runtime bindings.
- **GxP Audit Compliance:** Maintained under strict versioning and change control standards.

---

## Public APIs / Exports

Key exported interfaces and components:
- JSONTraceLogFormatter
- configure_structured_logging
- extract_trace_context
- get_tracer
- inject_trace_context
- setup_telemetry
- JSONTraceLogFormatter
- configure_structured_logging

---

## Dependencies

This package relies on the following standard dependencies:
- `opentelemetry-api>=1.20.0`
- `opentelemetry-instrumentation-fastapi>=0.41b0`
- `opentelemetry-instrumentation-httpx>=0.41b0`
- `opentelemetry-sdk>=1.20.0`
- `packages-security`

---

## Local Development & Testing

Run unit and contract tests for this package:

```bash
uv run pytest packages/telemetry
```
