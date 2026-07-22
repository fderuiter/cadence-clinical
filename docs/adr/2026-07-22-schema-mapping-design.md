# ADR 2026-07-22: Schema Mapping Design

## Status
Accepted

## Context
Engineers cannot verify GxP-compliant data parity because the SDLC documentation suite lacks mapping specifications between the upstream graph database and downstream relational database. The ADR log does not record this schema synchronization design.

## Decision
We will define static mapping tables for all Unified Study Definition Model (USDM) clinical entities between Neo4j graph nodes/properties and PostgreSQL relational tables/columns. Dynamic background translation states will translate the clinical study payload.

## Alternatives Considered
- Implement custom automated testing frameworks or database synchronization engines. This was explicitly rejected as being out of scope.

## Trade-offs
- Positive: Ensures regulatory GxP compliance and enables engineers to verify database parity manually in under 30 minutes without custom testing tools.
- Negative: Documentation must be manually maintained and kept up-to-date with schema changes.
