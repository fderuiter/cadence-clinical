# ADR 2026-07-23: Targeted Integration with Path Isolation

## Status
Accepted

## Context
USDM integration path design requires complete physical and network level path isolation.

## Decision
We implement path isolation for targeted integration steps.

## Alternatives Considered
- Monolithic integration
- Simple environment separation

## Trade-offs
- Positive: Complete isolation and safe execution.
- Negative: Duplicate configuration overhead.
