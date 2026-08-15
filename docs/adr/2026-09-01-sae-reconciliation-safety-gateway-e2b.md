# ADR 2026-09-01: SAE Reconciliation & Safety Gateway (E2B)

## Status

Accepted

## Context

In clinical trials, patient safety data is collected in two parallel, isolated systems: the Electronic Data Capture (EDC) system (which tracks Adverse Events reported at clinical sites) and the Safety/Pharmacovigilance database (which maintains authoritative Serious Adverse Event (SAE) cases for regulatory reporting). To ensure data integrity, GCP compliance, and participant safety, these two sources must be reconciled periodically.

The platform requires a robust, automated, and secure gateway to align clinical EDC observations with authoritative safety cases, identify discrepancies, persist results with full version history, and alert the Sponsor Medical Monitor of any material differences.

This decision implements requirements under PRD-SYS-001 and Trace-14.

## Decision

We decided to establish a dedicated, isolated safety gateway microservice (`apps/safety`) as the integration boundary. This service is completely responsible for managing safety case records and exports, as well as running the reconciliation logic.

### 1. Service Boundary & Isolation

Following our architectural patterns, the safety subsystem operates on its own dedicated relational database and audit ledger (`apps/safety/models.py`), keeping third-party pharmacovigilance schema completely separate from the core clinical execution (EDC) transaction engine.

### 2. Data Flow Architecture

The automatic reconciliation and transmission pipeline implements a strict sequential flow:

1. **EDC Sourcing**: The safety subsystem queries the clinical trial execution engine's SDTM AE dataset via `ExecutionClient.fetch_ae_data`, retrieving de-identified Adverse Events as Dataset-JSON.
2. **MedDRA Normalization**: The verbatim terms are resolved to standardized MedDRA codes using `resolve_meddra_code`.
3. **Reconcile**: The comparison engine `compare_sae_records` aligns standard variables (`AESER`, `AESTDTC`, `AEENDTC`, `AESEV`, `AEREL`, `AEOUT`) and MedDRA hierarchy codes, identifying discrepancies using deterministic, PII-free case event keys.
4. **E2B Export**: The validated safety cases are compiled into HL7/E2B XML payloads (`generate_e2b_xml`) complying with the `urn:hl7-org:v3` namespace.
5. **Outbound Safety DB Transmission**: Payloads are transmitted to the external safety database using `SafetyDatabaseAdapter`.

### 3. PII Pseudonymization & Privacy

The safety gateway strictly enforces a zero-PII/PHI leakage boundary:

- Irreversible HMAC-SHA256 pseudonymization (using a secure salt) is applied to all Subject/Patient IDs.
- Direct identifiers (such as name, telecom, address) and direct dates of birth (DOB) are stripped prior to local persistence or external transmission.
- All errors persisted in jobs are PII-sanitized (truncating error messages to exception class names) to guarantee that no database or server paths are leaked.

### 4. Gateway Integration & Security

The safety microservice endpoints are exposed securely through the API Gateway, enforcing:

- `GatewayAuthMiddleware` to verify double-authentication signatures.
- Gateway V2 HMAC signatures with mandatory `X-Change-Reason` justifications on all mutating actions to guarantee 21 CFR Part 11 compliant traceability.

## Alternatives Considered

- **Direct Database-to-Database Sync**: Exposing database views from the execution/EDC service directly to the safety subsystem and performing SQL-level joins. This was rejected because it violates service boundary isolation, couples the EDC transaction schemas directly to PV transmission structures, and increases the risk of raw PII/PHI leakage.
- **Consolidating under `apps/interop`**: Incorporating safety reconciliation and E2B logic under the existing EHR integration gateway. This was rejected because safety reconciliation and pharmacovigilance represent a distinct clinical domain with different data models, validation constraints, and regulatory recipients. Separating it into `apps/safety` isolates the GxP validation scope.

## Trade-offs

- **Positive**:
  - Perfect service and database separation of concerns.
  - Complete elimination of PII/PHI storage or transmission risk.
  - Bulletproof GxP traceability using a chronological `version_index` and a separate, unalterable GxP audit log.
- **Negative**:
  - Maintenance overhead of an additional microservice database and running MedDRA API lookups on-the-fly.
