# ADR 2026-07-22: Database Shadow Triggers

## Status
Accepted

## Context
All relational mutations must be captured with complete field histories automatically.

## Decision
We utilize database shadow triggers to transparently audit mutations.

## Alternatives Considered
- Application middleware audit logs
- Polling changes via cron

## Trade-offs
- Positive: Guaranteed capture of all SQL mutations.
- Negative: DB level trigger maintenance overhead.
