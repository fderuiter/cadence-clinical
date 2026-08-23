# ADR-2214: Deep Regulatory Transmission Gateway and Multi-Tier Acknowledgement Seam

* **Status:** Accepted
* **Date:** 2026-08-23
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Global clinical safety regulations (FDA 21 CFR Part 11 / 312.32, EMA EudraVigilance / ICH E2B(R3), PMDA, MHRA, and RFC 4130) mandate secure, authenticated, and non-repudiable electronic transmission of Individual Case Safety Reports (ICSRs) over Applicability Statement 2 (AS2). Health authorities respond asynchronously across three distinct acknowledgement tiers:
1. **ACK A (Perimeter Transport / MDN)**: Validates receipt over wire and computes SHA-256 Message Integrity Check (MIC) digests.
2. **ACK B (Syntactic Schema Validation)**: Confirms ICH E2B(R3) XML schema validity or returns structured syntactic error codes.
3. **ACK C (Business Acceptance)**: Confirms health authority safety database ingestion (FDA FAERS, EMA EV, PMDA).

Previously, this capability in `apps/safety` was fragmented across shallow helper modules and a massive FastAPI router that manually orchestrated cryptographic encryption, MIC hashing, HTTP transport, and XML parsing. This shallow structure created significant architectural friction, scattered error handling, and forced tests to assert on private regexes rather than observable outcomes across a clean seam.

A deep module is required to consolidate all AS2 transmission, S/MIME cryptography, MDN verification, multi-tier ACK state progression, and Part 11 audit logging behind a single cohesive seam under **PRD-PVG-002**.

## 2. Decision Drivers & Constraints

* **Maximum Depth & Locality:** Callers (the HTTP router and background workers) should interact with a compact 2-method interface (`transmit_icsr`, `process_incoming_receipt`). All S/MIME encryption, MIC calculation, MIME parsing, and ACK evaluation must remain internal implementation details.
* **Ports & Adapters Seam Discipline:** The domain interface `RegulatoryTransmissionPort` must be satisfied by authentic adapters: `HttpAS2GatewayAdapter` for production and `InMemoryAS2GatewayAdapter` for sub-millisecond, network-free unit tests.
* **Monotonic Lifecycle State Machine:** Enforce strict sequential status transitions (`SUBMITTED` → `ACK_A_RECEIVED` → `ACK_B_ACCEPTED/REJECTED` → `ACK_C_ACCEPTED/REJECTED`) with automatic back-filling for high-throughput synchronous gateways and idempotent deduplication.
* **Fail-Closed Cryptographic Verification:** Corrupted envelopes, invalid S/MIME signatures, or MIC mismatches must fail immediately (`TAMPERED` / `FAILED_INTEGRITY`), record a 21 CFR Part 11 audit failure, and reject with HTTP 422.
* **Transactional 21 CFR Part 11 Audit Logging:** All state transitions and transmission events must be sealed in `write_as2_audit_log` within the active transaction boundary.

## 3. Options Considered

1. **Option A (Selected): Single Deep `RegulatoryTransmissionGateway` with Ports & Adapters Architecture**:
   - Consolidate all transmission, cryptography, parsing, and ACK verification into `apps/safety/services/as2_gateway.py` and `apps/safety/services/as2_gateway_client.py`.
   - Provide `HttpAS2GatewayAdapter` and `InMemoryAS2GatewayAdapter` satisfying `RegulatoryTransmissionPort`.
   - Absorb and delete shallow helper files and dead re-export shims.
   - Refactor `apps/safety/presentation/routers/safety.py` into a thin transport adapter passing raw byte payloads.
2. **Option B: Split Outbound Transmission and Inbound Receipt Modules**: Separating outbound dispatch from inbound receipt parsing. Rejected because callers and tests would still have to manage intermediate data contracts across split modules.
3. **Option C: Retain Shallow Helper Files Internally**: Keeping separate files and having a gateway class import them. Rejected because it fails the deletion test and preserves navigational friction.

## 4. Decision Outcome

Chosen option: **Option A**.
Option A eliminates shallow files, shrinks the router's surface area, encapsulates cryptographic and parsing complexity, enforces monotonic GxP lifecycle progression, and establishes authentic in-memory testability.

## 5. Consequences & Trade-offs

* **Positive:** High leverage: Router and background jobs invoke a 2-method interface instead of orchestrating shallow modules.
* **Positive:** Locality: S/MIME, MIC, MDN regexes, and XML ACK parsers live in one deep module.
* **Positive:** Test surface: Unit tests execute against `InMemoryAS2GatewayAdapter` in sub-milliseconds without mock patching `httpx`.
* **Positive:** GxP Compliance: Monotonic state machine and fail-closed cryptographic verification prevent invalid regulatory submissions.
* **Negative:** Requires refactoring existing test suites to test at the new port interface rather than calling internal regex helpers directly.

## 6. Implementation & Verification

* Domain models: `apps/safety/domain/as2_models.py`.
* Gateway service: `apps/safety/services/as2_gateway.py`.
* Client transport adapter: `apps/safety/services/as2_gateway_client.py`.
* FastAPI transport router: `apps/safety/presentation/routers/safety.py`.
* Test suite: `apps/safety/tests/test_safety_as2_e2b_mdn.py` and `apps/safety/tests/test_as2_gateway.py` verifying full transmission, receipt verification, tampering detection, and audit trails under `@req:PRD-PVG-002`.
