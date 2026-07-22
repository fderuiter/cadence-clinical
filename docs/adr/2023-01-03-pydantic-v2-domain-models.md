# ADR 2023-01-03: Pydantic v2 for Domain Models

## Status
Accepted

## Context
The platform manages highly sensitive clinical metadata that transitions across multiple system boundaries, specifically translating upstream CDISC USDM schemas to downstream eCRF structures. We need a reliable mechanism to strictly type, parse, and validate data structures within the Python runtime to prevent silent failures and ensure domain model integrity.

## Decision
We decided to mandate the use of Pydantic v2 for all domain models, CDISC schemas, and data validation layers (especially within `packages/core-models/`). This enforces strict type-checking and automated runtime validation across the entire Cadence Clinical monorepo.

## Alternatives Considered
- **Standard Library Dataclasses:** Native to Python but lack powerful built-in validation, recursive type-checking, and out-of-the-box JSON serialization tailored for API endpoints.
- **Marshmallow:** An excellent validation library, but Pydantic provides significantly tighter integration with modern type hints, FastAPI, and IDE tooling.

## Trade-offs
- **Positive:** Pydantic v2 offers exceptional parsing performance (via Rust core), strict schema enforcement, and seamless integration with the FastAPI framework. It ensures that any malformed data is caught immediately at the service boundary.
- **Negative:** Strict validation can reject loosely formatted payloads from legacy systems. Refactoring models and handling complex nested validation logic can occasionally introduce developer friction.
