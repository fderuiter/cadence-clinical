# ADR 2026-06-06: USDM Pydantic Models

## Status
Accepted

## Context
USDM models are represented as Pydantic v2 domain models for schema validation.

## Decision
USDM models are represented as Pydantic v2 domain models for schema validation.

## Alternatives Considered
- Direct dict manipulation
- Pydantic v1

## Trade-offs
- Positive: Fast, robust validation.
- Negative: Performance overhead.
