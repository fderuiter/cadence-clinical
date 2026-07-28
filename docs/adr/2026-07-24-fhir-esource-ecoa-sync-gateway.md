# ADR 2026-07-24: FHIR / eSource & eCOA Sync Gateway

## Status
Accepted

## Context
Cadence Clinical requires a secure and robust interoperability microservice (`apps/interop/`) to:
1. Ingest Electronic Health Records (EHR) in HL7 FHIR format, map resources (`Patient`, `Observation`, `Condition`, `MedicationStatement`) directly into CDASH eCRF target fields to pre-fill them, and ensure all incoming data strips direct identifiers (PII) before storage.
2. Provide endpoints for mobile ePRO/eCOA participant diary and survey submissions with support for offline queue reconciliation and conflict resolution.

## Decision
We decided to:
- Scaffold the `apps/interop/` microservice as a separate FastAPI application in the monorepo.
- Implement an irreversible HMAC-SHA256 pseudonymization utility to replace Patient IDs and other direct PII (name, telecom, address) prior to storing or transmitting records.
- Implement a comprehensive FHIR Adapter capable of parsing and translating standard FHIR Bundle entries into CDASH target variables (e.g. `DM.BRTHDTC`, `DM.SEX`, `VS.SYSBP`, etc.).
- Create an SQLite database schema for storing ePRO submissions with client-side device timestamps and offline sync markers.
- Implement clear conflict resolution strategies (`CLIENT_WINS`, `SERVER_WINS`, `MERGE`) to reconcile offline queues deterministically.
- Integrate the interop microservice routes into the API Gateway (`apps/gateway/main.py`) for secure proxying and centralized OpenAPI documentation aggregation.

## Alternatives Considered
- **Direct ingestion into EDC app:** While simpler initially, separating external integration boundaries from the transaction engine keeps the EDC microservice focused and prevents scaling/maintenance issues related to third-party integration schemas.
- **Relational database without async support:** We chose SQLAlchemy AsyncSession with SQLite (`sqlite+aiosqlite:///:memory:`) to remain highly consistent with existing microservices (`apps/etmf`, `apps/execution`) and enable unified, clean asynchronous testing in the local sandbox.

## Trade-offs
- **Positive:**
  - Excellent separation of concerns between EHR integration/mobile ingestion and the core EDC database.
  - Bulletproof GxP data integrity through HMAC-SHA256 de-identification and explicit audit trails.
  - Clear and testable offline synchronization conflict resolution behavior.
- **Negative:**
  - Introduces a new microservice in the Monorepo stack requiring gateway routing updates.

This decision implements requirements under Trace-8.
