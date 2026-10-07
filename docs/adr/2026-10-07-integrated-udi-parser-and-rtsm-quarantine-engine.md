# ADR-2812: Integrated GS1 and HIBCC UDI Parser with Automated RTSM Quarantine Engine

- **Status:** Accepted
- **Date:** 2026-10-07
- **Authors:** @stitch-agent
- **Deciders:** @architecture-lead, @clinical-operations

---

## 1. Context & Problem Statement

Medical device clinical trials traditionally suffer from manual serial entry errors, transcription mistakes, and delayed paper-based quarantine workflows. These manual processes introduce significant audit risks under 21 CFR Part 11 and ISO 14155:2020, and risk patient safety if defective or expired devices are dispensed to subjects.
@req:PRD-DEV-001

## 2. Decision Drivers & Constraints

- **Driver 1 (Compliance):** 21 CFR Part 11 compliance requiring immutable audit trails with reason-for-change attribution.
- **Driver 2 (Safety):** Instant real-time dispensation blocking (<500ms) for quarantined or expired device inventory items.
- **Driver 3 (Accuracy):** 100% decoding accuracy across GS1-128, GS1-DataMatrix, HIBCC-128, and HIBCC-DataMatrix barcode standards.
- **Driver 4 (Traceability):** Asynchronous transactional outbox event publishing via MassTransit for downstream RTSM and Safety module integration.

## 3. Options Considered

### Option 1: Client-Side Barcode Parsing and Synchronous Quarantine Webhooks

- **Overview:** Parse barcodes in client Vue components and invoke REST endpoints to update device states synchronously.
- **Pros:**
  - Fast client feedback.
- **Cons:**
  - Inconsistent barcode parser implementation across web and mobile platforms.
  - Vulnerable to network dropouts during quarantine state transitions.

### Option 2: Server-Side UDI Engine with EF Core Transactional Outbox Events (Selected)

- **Overview:** Implemented stateless `UdiParser` on the server in `Cadence.Apps.Execution` with GTIN-14 Modulo 10 and HIBCC Modulo 43 checksum validation, paired with transactional outbox events (`DeviceDeficiencyReportedEvent`, `DeviceQuarantinedEvent`) and programmatic RTSM dispensation gating in `DeviceApplicationService`.
- **Pros:**
  - ✅ Unified server-side parsing logic ensuring 100% compliance across all clients.
  - ✅ Real-time dispensation gating rejecting quarantined or expired items immediately.
  - ✅ Reliable at-least-once outbox delivery for downstream safety case escalation.
- **Cons:**
  - ❌ Requires server connectivity for real-time RTSM quarantine enforcement.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Centralizing UDI parsing and quarantine logic in `Cadence.Apps.Execution` guarantees strict regulatory compliance, instant dispensation gating, and robust outbox event propagation.

## 5. Consequences & Trade-offs

- **Positive Impact:** Programmatic dispensation gating eliminates manual transcription errors and prevents dispensing defective/expired devices.
- **Negative Impact / Technical Debt:** Requires network availability during site intake and dispensation workflows.
- **Mitigation Strategy:** Clear user feedback and error payloads (`DISPENSATION_BLOCKED`) if network or inventory exceptions occur.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `Cadence.Apps.Execution`
- **Verification Plan:** Verified via `UdiParserTests`, `DeviceApplicationServiceTests`, `DeviceEndpointsTests`, and CLI quality sentinels (`dotnet cadence check`).
