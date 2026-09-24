# ADR-2207: GxP Telehealth Video Consultation, Direct-to-Patient Logistics, and Tiered Pediatric Assent

- **Status:** Accepted
- **Date:** 2026-08-23
- **Authors:** @fderuiter
- **Deciders:** Cadence Clinical Architecture Board

---

## 1. Context & Problem Statement

Decentralized and hybrid clinical trials (DCT) require robust, patient-centric capabilities that operate outside conventional site walls while maintaining strict GxP regulatory compliance, 21 CFR Part 11 electronic record integrity, and ICH GCP E6(R2)/(R3) participant safety guarantees. Specifically:
1. **Telehealth Video Visits (`PRD-DCT-005`, `ADR-2199`):** Investigators and subjects need secure, end-to-end encrypted WebRTC audio/video consultations with cryptographic room access authorization, session signaling, synchronized interactive co-viewing of informed consent materials, and optional GxP archived recordings protected by SHA-256 digital seals.
2. **Tiered Pediatric Assent & LAR Consent (`PRD-DCT-006`, `ADR-2199`):** Pediatric studies require age-tailored consent instruments (e.g. animated multimedia storyboards for ages 7–11, simplified text for ages 12–17) paired with simultaneous parent or Legally Authorized Representative (LAR) electronic signatures and investigator counter-signatures within a cryptographically sealed consent manifest.
3. **Direct-to-Patient (DTP) Logistics (`PRD-DCT-003`, `ADR-2199`):** Investigational Product (IP) shipments dispatched directly from central depots to patient homes require multi-zone cold chain monitoring (cryogenic, ultra-cold, frozen, refrigerated, ambient), automated Mean Kinetic Temperature (MKT) stability budget tracking, temperature excursion alarms, Chain of Custody (CoC) handoff verification, and delivery electronic signatures.
4. **Decentralized In-Home Nurse Visits (`PRD-DCT-004`, `ADR-2199`):** Mobile healthcare nurses conducting home visits require credential validation, protocol procedure checklists (e.g. PK draws, IP administration, ECGs), and mobile eCRF data entry backed by offline cache reconciliation and idempotent synchronization.

## 2. Decision Drivers & Constraints

* **21 CFR Part 11 & GxP Compliance:** All room authorizations, recording archival seals, assent/LAR signatures, custody handoffs, and offline eCRF sync transactions must produce immutable, attributable, and chronological audit trail entries.
* **Cryptographic Room Security & Privacy:** WebRTC sessions must enforce short-lived HMAC-SHA256 room authorization tokens with role-based capability flags (`can_present`, `can_annotate`, `can_sign`, `can_record`, `can_admit`) and DTLS-SRTP fingerprint validation.
* **Temperature Stability Invariants (USP <1079> / PDA TR 39):** Real-time temperature streams must calculate Mean Kinetic Temperature (MKT) using the Arrhenius equation and immediately flag excursions to prevent administering compromised IP.
* **Offline-First Synchronization:** In-home mobile eCRF entries collected in low-connectivity residential settings must support offline caching, cryptographic checksums, and conflict-free server reconciliation upon reconnect.
* **Vanilla CSS Design Standard:** Web client components must exclusively utilize design tokens from `apps/web/src/style.css` without third-party utility class frameworks.

## 3. Options Considered

### Option 1: **Option A (Selected): Unified DCT Engine in Interop Gateway with Domain-Driven Services**

- **Overview:** - Implement pure domain models and processors in `apps/interop/domain/dct_models.py` and `dct_processor.py`.
   - Implement relational persistence models and repositories in `apps/interop/infrastructure/dct_models.py` and `dct_repository.py`.
   - Expose standard REST endpoints under `/api/v1/interop/dct/*` with comprehensive Pydantic v2 DTOs.
   - Build a full-featured, accessible `TelehealthDtpConsole.vue` in `apps/web/src/components/clinical/`.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option B: Fragmented Per-Service Split**

- **Overview:** - Distribute telehealth to fileshare, DTP to RTSM/execution, in-home visits to CTMS, and pediatric assent to eConsent.
   - *Rejected:* Increases distributed transaction overhead, cross-service network latency, and complicates offline sync synchronization across disparate databases.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead


## 4. Decision Outcome

- **Chosen Option:** **Option A** because it provides a cohesive, high-performance, and auditable decentralized clinical trials architecture. The unified DCT subsystem bridges telehealth signaling, tiered pediatric assent, cold chain logistics, and mobile home health workflows with centralized GxP audit logging and end-to-end cryptographic sealing.
- **Justification:** Selected as primary architectural approach.

## 5. Consequences & Trade-offs

* **Positive:**
  - Standardized room token generation and WebRTC session signaling with zero external SaaS dependencies.
  - Provable recording authenticity via SHA-256 digital seals and 21 CFR Part 11 certification statements.
  - Multi-tier pediatric assent adhering to FDA and EMA pediatric clinical trial guidelines.
  - Automated MKT stability calculations and courier custody handoff verification.
  - Offline-first mobile eCRF data entry with optimistic locking and idempotent reconciliation.
* **Negative:**
  - Requires maintaining in-memory session signaling state for active WebRTC video sessions.

## 6. Implementation & Verification

* **Backend Services:**
  - `apps/interop/domain/dct_models.py` & `apps/interop/domain/dct_processor.py`
  - `apps/interop/infrastructure/dct_models.py` & `apps/interop/infrastructure/dct_repository.py`
  - `apps/interop/application/dct_service.py`
  - `apps/interop/presentation/routers/dct.py` & `apps/interop/presentation/dct_dtos.py`
* **Frontend Components:**
  - `apps/web/src/components/clinical/TelehealthDtpConsole.vue`
  - `apps/web/src/views/TelehealthDtpView.vue`
  - Route `/dct` added to `apps/web/src/router/index.js` and `apps/web/src/components/AppShell.vue`
* **Test Suite:**
  - `apps/interop/tests/test_dct_telehealth_dtp_logistics.py`
  - `tests/test_dct_telehealth_dtp_logistics.py`
