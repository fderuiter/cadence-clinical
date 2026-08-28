---
name: cadence-adr
description: "Scaffold, author, and record Architecture Decision Records (ADRs) aligned with Cadence domain standards and auto-indexed in docs/adr/index.md. Use when introducing dependencies, changing data models, modifying service boundaries, or making architectural trade-offs."
license: MIT
metadata:
  author: Cadence Team
  version: "1.0"
---

# Cadence Architecture Decision Records (ADR)

Records significant, hard-to-reverse architectural decisions with clear rationale, context, and consequences.

## When an ADR is Required

Create an ADR when:
1. Adding a new third-party dependency or database engine.
2. Modifying inter-service data contracts or introducing new API gateways.
3. Changing data storage models (Neo4j graph nodes or PostgreSQL schema migrations).
4. Making architectural decisions that satisfy all three criteria:
   - **Hard to reverse**: High cost to change later.
   - **Surprising without context**: Future maintainers would ask "why was this done this way?".
   - **Real trade-off**: Deliberate choice between viable alternatives.

## Scaffolding an ADR

Use the automated ADR generator:

```bash
uv run python scripts/create_adr.py --title "Descriptive Title" --domain "core-platform" --req "PRD-SYS-xxx"
```
*(Or via Cadence CLI: `uv run cadence scaffold adr "Descriptive Title"`)*

This creates a dated file under `docs/adr/YYYY-MM-DD-slug.md` and automatically updates the index table in `docs/adr/index.md`.

## ADR Structure

1. **Title**: Short noun phrase describing the decision.
2. **Status**: `PROPOSED`, `ACCEPTED`, `SUPERSEDED`, or `DEPRECATED`.
3. **Context & Problem Statement**: The forces, requirements, and constraints at play.
4. **Considered Options**: Concrete alternatives evaluated with pros/cons.
5. **Decision Outcome**: The chosen solution and explicit justification.
6. **Consequences**: Positive effects, negative trade-offs, and compliance/GxP impacts.
7. **Traceability**: Linked requirement IDs (`PRD-SYS-xxx`).
