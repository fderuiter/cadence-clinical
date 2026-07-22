# ADR 2026-07-22: Audit Log Design

## Status
Accepted

## Context
During regulatory reviews, engineers need to verify historical audit trails for GxP compliance. The ADR log lacks documentation on how database triggers and graph actions record immutability.

## Decision
We will document audit log designs to record schema mapping transitions and system states. This enables compliance auditing for GxP verification and supports verification of consistent state changes in both Neo4j and PostgreSQL databases using database triggers and graph actions.

## Alternatives Considered
- Relying solely on application-level logging without specialized documentation. Rejected due to the rigorous requirements of GxP compliance and historical audit verifiability.

## Trade-offs
- Positive: Facilitates compliance auditing and manual verification of database parity.
- Negative: Requires careful maintenance of standard queries and verification protocols.
