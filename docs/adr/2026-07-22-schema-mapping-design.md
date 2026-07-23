# ADR 2026-07-22: Schema Mapping Design

## Status
Accepted

## Context
USDM structures require reliable transformation mappings into OpenRosa clinical representation.

## Decision
We enforce a bidirectional unified schema mapping protocol for clinical data conversion.

## Alternatives Considered
- Single-direction transformers
- Manual JSON path extraction

## Trade-offs
- Positive: Guaranteed lossless round-trips.
- Negative: High initial configuration complexity.
