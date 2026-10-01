# ADR-2593: Direct-To-Patient Cold-Chain Telemetry and Resupply Persistence

- **Status:** Accepted
- **Date:** 2026-10-01
- **Authors:** @cadence-dev
- **Deciders:** @cadence-arch-team

---

## 1. Context & Problem Statement

Direct-to-patient (DTP) shipments in decentralized clinical trials require real-time cold-chain IoT temperature logging and automated re-supply triggers to comply with USP <1079> and PDA TR 39 Mean Kinetic Temperature (MKT) Arrhenius calculation standards (PRD-DCT-003, PRD-SYS-181). Unmonitored thermal excursions risk participant safety, unblinded dosing disruptions, and wasted clinical trial material without real-time alerts.

## 2. Decision Drivers & Constraints

- **USP <1079> / PDA TR 39 Compliance:** Mandatory Arrhenius Mean Kinetic Temperature (MKT) evaluation using $E_a = 83.144 \text{ kJ/mol}$ (PRD-DCT-003, PRD-SYS-181).
- **21 CFR Part 11 Audit Trail:** Complete audit immutability for temperature telemetry and chain-of-custody handoffs (PRD-SYS-181).
- **Double-Blind Protection:** Allocation of replacement kits must strictly maintain double-blind protocols during automated resupply (PRD-SCM-002).
- **Real-Time Responsiveness:** Automatic transition to `TEMP_EXCURSION_HOLD` state and immediate generation of replacement dispatch orders upon thermal breach detection (PRD-DCT-003).

## 3. Options Considered

### Option 1: Transient In-Memory Processing Without Persistence
- **Overview:** Process IoT telemetry points entirely in-memory at gateway time without persisting shipment history or database migrations.
- **Pros:**
  - Fast execution time.
- **Cons:**
  - ❌ Fails 21 CFR Part 11 audit trail requirement.
  - ❌ Historical telemetry points lost on service restart.

### Option 2: Dedicated Interop EF Core Persistence with Audit Interceptors (Selected)
- **Overview:** Define `DtpShipmentRecord` entity and DbContext configuration in `apps/interop` mapped to table `dtp_shipments`, integrated with EF Core migrations and GxP audit logging.
- **Pros:**
  - ✅ Fully persistent telemetry and chain of custody tracking.
  - ✅ Immutable audit entries via `AuditLogEntry`.
  - ✅ Enforces database schema integrity and indexing on tracking numbers and shipment IDs.
- **Cons:**
  - ❌ Requires database migration maintenance.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Persisting DTP shipments and cold-chain telemetry in the Interop database with GxP audit trails provides full regulatory compliance with USP <1079> and 21 CFR Part 11.

## 5. Consequences & Trade-offs

- **Positive Impact:** Sub-second detection of thermal excursion breaches and automated replacement kit dispatch within 5 minutes.
- **Negative Impact / Technical Debt:** Requires periodic database index optimization as telemetry points scale.
- **Mitigation Strategy:** Store telemetry readings as JSONB arrays with indexed query metadata.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/interop`, `apps/execution`, `tests/Cadence.UnitTests`
- **Verification Plan:** Unit tests in `DtpColdChainTelemetryTests.cs`, OpenAPI schema synchronization, and `pnpm cadence check` quality gates.
