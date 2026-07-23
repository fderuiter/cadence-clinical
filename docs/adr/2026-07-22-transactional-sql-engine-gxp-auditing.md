# ADR 2026-07-22: Transactional SQL Engine with Standard GxP Auditing

## Status
Accepted

## Context
GxP guidelines require standard, immutable and chronological database write audits.

## Decision
We enforce transactional safety with standard GxP auditing inside relational databases.

## Alternatives Considered
- Application-level logging only
- Non-transactional logging

## Trade-offs
- Positive: Immutable and legally compliant audit trails.
- Negative: Increased database write latency.
