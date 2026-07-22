# ADR 2026-07-22: API-First Validation and USDM Integration

## Status
Accepted

## Context
Cadence Clinical's study design ingestion and validation previously relied on a tightly coupled, database-driven process using a legacy placeholder model. This legacy approach silently truncated rich clinical study setup data and required direct database (Neo4j) coupling to query and validate data, which caused brittle integrations, maintenance overhead, and downtime.

## Decision
We are transitioning to a decoupled, API-first validation approach and integrating the official CDISC USDM standard package.

1. **Official USDM Adoption (`usdm==0.65.0`)**: Instead of maintaining custom, error-prone parser models, we have integrated the official CDISC USDM Python library. This guarantees high-fidelity parsing of complex nested structures (including Study, StudyVersion, Epochs, Arms, and Timelines) without dropping critical clinical fields.
2. **Removing Direct Database (Neo4j) Coupling**: We removed the Neo4j driver initialization process on startup in the designer application. Moving validation to the API layer decouples Cadence from the underlying data store of the external registry, significantly improving system boundaries and reliability. The differences endpoint has also been transitioned away from direct database coupling.
3. **API-Driven On-Demand Validation**: The alignment validation engine now dynamically retrieves nested study definitions via the `/usdm/v4` HTTP endpoints, utilizing `httpx.AsyncClient` with strict timeouts.

## Alternatives Considered
- **Maintaining custom parser models**: This was rejected because maintaining parity with the evolving CDISC USDM standard would require significant ongoing manual effort and risk data truncation.
- **Keeping direct Neo4j database coupling**: This was rejected because it violates architectural boundaries and makes the system brittle and heavily dependent on the external registry's internal data store rather than its API.

## Trade-offs
- **Positive**: 100% schema validation without direct database queries, elimination of data truncation errors, cleaner architecture, and improved reliability.
- **Negative**: Adds a network dependency on the external registry's HTTP endpoints for validation, meaning validation is subject to potential latency or network errors.
