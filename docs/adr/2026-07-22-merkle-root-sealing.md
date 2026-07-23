# ADR 2026-07-22: Merkle Root Sealing

## Status
Accepted

## Context
E-signatures and block logs must be protected from local tamper modifications.

## Decision
We seal block logs with a Merkle root verification pattern.

## Alternatives Considered
- Simple hash chaining
- Centralized timestamps

## Trade-offs
- Positive: High security, easy audit validation.
- Negative: Additional computation cost per transaction.
