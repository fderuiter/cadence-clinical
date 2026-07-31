# ADR 2026-08-28: SAE Reconciliation & Safety Gateway (E2B)

## Status
Accepted

## Context
Cadence Clinical requires a highly secure and regulatory-compliant safety and pharmacovigilance gateway microservice (`apps/safety/`) to:
1. Orchestrate the automated reconciliation of serious adverse events (SAEs) between the Electronic Data Capture (EDC) system (`apps/execution`) and external pharmacovigilance safety databases.
2. Formulate and structurally validate standard E2B(R3) Individual Case Safety Reports (ICSR) in XML format for expedited regulatory submissions.
3. Secure outbound PV communication and internal microservice requests in a GxP and FDA 21 CFR Part 11 compliant manner, ensuring zero patient PII leakage inside local audit logs while preserving full regulatory traceability.

## Decision
We decided to:
- Establish the `apps/safety/` microservice as an independent integration-boundary service within the platform's monorepo architecture.
- Enforce strict GxP and FDA 21 CFR Part 11 database-native audit logging (`SafetyAuditLog`) with triggers preventing any manual modification, deletion, or tampering of transaction history.
- Implement a clear and secure data flow architecture:
  - **EDC Data Sourcing:** Fetch clean CDISC Dataset-JSON v1.0 standard Adverse Event (AE) tables via authenticated REST endpoints.
  - **Normalization:** Map EDC records to standard Pydantic v2 `SeriousAdverseEvent` objects and generate stable event keys to align EDC events with external safety records.
  - **Reconciliation:** Perform deterministic, bi-directional comparisons of fields (onset dates, MedDRA coding, severity, seriousness, outcomes, causality) to flag discordant elements, versioning reconciliation results securely.
  - **PII Pseudonymization:** Apply deterministic HMAC-SHA256 pseudonymization on Subject/Patient IDs (utilizing a secure, gateway-managed salt) and strip direct patient identifiers (such as birth dates) prior to external transmission or audit storage.
  - **E2B XML Export:** Render clean, well-formed ICH E2B(R3) XML payloads using Jinja2 templates, validate structural specifications defensively, and transmit to configured safety gateways via signed clients.
- Enforce centralized OIDC token validation at the API Gateway and verify Gateway V2 HMAC-signatures downstream via `GatewayAuthMiddleware` to protect read/write boundaries and reject invalid, expired, or tampered requests.
- Integrate immediate, fail-open notification dispatches via a gateway-signed client to alert the Sponsor Medical Monitor of material discrepancies during background runs.

## Alternatives Considered
- **Direct EDC-PV Integration:** Running reconciliation and XML rendering directly in the EDC core microservice. While simpler, this compromises boundary isolation, couples the core trial transaction engine with external safety database schemas, and increases regulatory impact during upgrades.
- **Relational database without async support:** We chose SQLAlchemy `AsyncSession` with SQLite (`sqlite+aiosqlite:///:memory:`) to remain highly consistent with existing microservices (`apps/etmf`, `apps/execution`) and enable unified, clean asynchronous testing in the local sandbox.

## Trade-offs
- **Positive:**
  - Robust segregation of integrations and safety reporting, reducing core validation overhead.
  - Bulletproof privacy via HMAC de-identification and strict regex-based text scrubbing.
  - Full regulatory accountability through immutable database-driven auditing.
- **Negative:**
  - Introduces a new independent microservice, increasing deployment topology complexity and requiring gateway route configuration.

This decision implements requirements under Trace-14 and Trace-15.
