# ADR 2023-01-01: Neo4j Graph Database for Clinical Metadata

## Status
Accepted

## Context
The Cadence Clinical platform requires a robust database solution for upstream Clinical Metadata Management (MDR). This data includes complex CDISC USDM graph algorithms, protocol versioning logic, and Schedule of Activities (SoA) definitions. Traditional relational schemas are not well-suited for heavily interconnected metadata due to the performance overhead of recursive queries and multi-table joins.

## Decision
We decided to use Neo4j as the primary database for the `apps/designer` service to manage clinical metadata. The service will connect to Neo4j using the Async Neo4j Python Driver.

## Alternatives Considered
- **Relational Databases (PostgreSQL/MySQL):** While robust for standard data structures, representing and querying deep graphical relations (such as USDM trees) requires complex foreign keys and recursive CTEs, scaling poorly for our specific schema needs.
- **Document Stores (MongoDB):** Flexible schemas are useful, but document stores lack native features for traversing multi-hop node connections efficiently, which is critical for compiling study protocols.

## Trade-offs
- **Positive:** Neo4j natively supports graph relationships, making it extremely fast and intuitive to query hierarchical clinical protocols and complex schedules.
- **Negative:** Introduces a non-relational query language (Cypher) and requires specialized knowledge for scaling, backup, and performance tuning compared to standard SQL databases.
