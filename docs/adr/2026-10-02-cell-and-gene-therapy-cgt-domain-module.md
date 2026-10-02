# ADR-2803: Cell and Gene Therapy (CGT) Domain Module Integration

- **Status:** Accepted
- **Date:** 2026-10-02
- **Authors:** @cadence-agent
- **Deciders:** @fderuiter, @cadence-architecture-board

---

## 1. Context & Problem Statement

Cell and Gene Therapies (CGT), such as CAR-T cell treatments and autologous gene therapies, require strict chain-of-custody tracking, temperature zone monitoring (cryogenic, ultra-cold, frozen, refrigerated, controlled ambient), ISBT 128 donation identification verification, bedside two-person verification gating, vial return reconciliation, and 21 CFR Part 11 compliant certificates of destruction.
@req:PRD-SYS-001

## 2. Decision Drivers & Constraints

- **21 CFR Part 11 / GxP Compliance:** Audit trails and cryptographic seals for certificates of destruction.
- **ISBT 128 Standards:** ISO 7064 Mod 37-2 check character verification for Donation Identification Numbers (DIN).
- **Bedside Safety Gating:** Mandatory two-person verification (`primary_verifier_id != secondary_verifier_id`) to unlock drug infusion.
- **Excursion Tracking:** Continuous temperature monitoring across cryogenic (<= -70°C), ultra-cold (-70°C to -20°C), frozen (-20°C to 2°C), refrigerated (2°C to 8°C), and controlled ambient (> 8°C) zones.

## 3. Options Considered

### Option 1: Legacy RTSM/IRT Extension
- **Overview:** Extending generic RTSM kit allocation tables with unstructured JSON metadata for CGT tracking.
- **Pros:** Minimal new database tables.
- **Cons:** Lack of strong typing, schema constraints, or auditability for dual verification and cryptographic seals.

### Option 2: Dedicated CGT Domain Module in Execution Engine (Selected)
- **Overview:** Implement core algorithms (`Isbt128Engine`, `TemperatureZoneClassifier`, `CgtBarcodeEngine`) in `packages/engine_core` and dedicated persistence models (`CgtChainOfCustody`, `CgtMilestoneEvent`, `CgtBedsideVerification`, `CgtDestructionCertificate`, `CgtVialReturnReconciliation`) in `apps/execution`.
- **Pros:**
  - ✅ Strong GxP audit trail support via EF Core interceptor.
  - ✅ Explicit REST API endpoints mapped under `/api/v1/execution/rtsm/cgt`.
  - ✅ Type-safe dual-verifier and dual-witness validation.
- **Cons:**
  - ❌ Requires database schema migration.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Provides full compliance with clinical safety standards, GxP regulations, and ISBT 128 standards with end-to-end traceability.

## 5. Consequences & Trade-offs

- **Positive Impact:** Full CGT chain-of-custody, bedside verification safety gating, and automated certificate of destruction seal generation.
- **Negative Impact / Technical Debt:** Additional database tables and migration overhead in `ExecutionDbContext`.
- **Mitigation Strategy:** Managed via EF Core migrations and covered by extensive unit test suites.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/engine_core`, `apps/execution`
- **Verification Plan:** Unit tests in `Cadence.UnitTests` covering ISBT 128 DIN validation, temperature zone classification, HMAC-SHA256 barcode verification, bedside dual verification gating, and certificate of destruction seal computation.
