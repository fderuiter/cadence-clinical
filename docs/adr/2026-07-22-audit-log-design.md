# ADR 2026-07-22: Audit Log Design

## Status
Accepted

## Context
Tracing study designers' and site investigators' actions requires a coherent central log design.

## Decision
We design a unified audit log specification mapping both gateway and database mutations.

## Alternatives Considered
- Separate application and database log streams
- Third-party log collectors only

## Trade-offs
- Positive: Single source of truth for all forensic tracing.
- Negative: Increased complexity in audit log serialization.
