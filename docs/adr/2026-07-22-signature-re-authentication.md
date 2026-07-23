# ADR 2026-07-22: Signature Re-Authentication

## Status
Accepted

## Context
Signatures on lock files must require explicit proof of identity re-authentication.

## Decision
We enforce user re-authentication upon performing e-signatures.

## Alternatives Considered
- Direct token reuse
- Long-lived session tokens

## Trade-offs
- Positive: Strict FDA 21 CFR Part 11 compliance.
- Negative: Additional prompt interaction for active users.
