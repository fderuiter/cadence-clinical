# ADR-2209: Medical Device and IVD Investigation Suite with UDI and Malfunction Quarantine

- **Status:** Accepted
- **Date:** 2026-08-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical trials evaluating investigational medical devices and in-vitro diagnostics (IVDs) must comply with international standards and regulatory mandates including ISO 14155:2020 (Clinical investigation of medical devices for human subjects — Good clinical practice), EU Medical Device Regulation (MDR 2017/745), EU In-Vitro Diagnostic Regulation (IVDR 2017/746), and FDA 21 CFR Part 812 & Part 11.

Traditional eClinical platforms lack native support for:
1. Standardized Unique Device Identifier (UDI) barcode ingestion and parsing (combining Device Identifier DI and Production Identifier PI: lot number, serial number, expiration date, manufacturing date, and SaMD software versions).
2. Automated cross-service safety and supply chain triggers upon reporting a device deficiency or malfunction.
3. Programmatic dispensation blocking and quarantine state enforcement across serialized inventory in RTSM.
4. Compliant vigilance incident reporting exports (EU MDR/IVDR Manufacturer Incident Report XML/JSON formats) and CDISC SDTM DD (Device Deficiencies) and DO (Device Tracking and Operations) domain generation.

This ADR defines the unified architecture satisfying **PRD-SYS-126**, **PRD-DEV-001**, **PRD-DEV-002**, **PRD-DEV-003**, and **PRD-DEV-004**.

---

## 2. Decision Drivers & Constraints

* **ISO 14155:2020 Compliance:** Explicit distinction between Adverse Device Effects (ADE), Serious Adverse Device Effects (SADE), Unanticipated Serious Adverse Device Effects (USADE), and Device Deficiencies.
* **EU MDR (2017/745) & EU IVDR (2017/746):** Rapid vigilance reporting timeframes (2 calendar days for serious public health threats, 10 days for death/serious deterioration, 15 days for reportable incidents) and Manufacturer Incident Report (MIR v7.2) structure.
* **FDA UDI Rule & 21 CFR Part 11:** Parsing GS1-128, GS1-DataMatrix, and HIBCC barcodes; electronic signatures on deficiency submissions and quarantine releases with immutable audit logs.
* **Decoupled Monorepo Architecture:** Zero direct sibling database imports between `apps/execution`, `apps/safety`, and `apps/interop`. Event communication via the transactional outbox pattern and typed REST endpoints.
* **Vanilla CSS UI Standard:** 100% design-token-driven responsive frontend in `apps/web` with 0 Tailwind dependencies.

---

## 3. Options Considered

### Option 1: **Option A (Selected): Unified Hexagonal Microservice Suite with Reactive Outbox Triggers:**

- **Overview:** - `apps/execution`: UDI parsing engine (GS1 / HIBCC), serialized device inventory in RTSM, deficiency eCRF logging, and programmatic dispensation gating for quarantined devices.
   - `apps/safety`: Adverse Device Effect (ADE / SADE / USADE) investigation lifecycle, vigilance countdown timers, and Safety Physician electronic signature adjudication.
   - `apps/interop`: CDISC SDTM DD & DO dataset compiler and EU MDR/IVDR Manufacturer Incident Report (MIR v7.2 XML/JSON) export engine.
   - `apps/web`: High-density `DeviceInvestigationConsole.vue` providing barcode scanning simulation, deficiency logging, quarantine management, and regulatory exports.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option B: Monolithic EDC Database Coupling:**

- **Overview:** - Centralizing safety cases and SDTM exports directly in `apps/execution` database models.
   - Rejected because it violates Cadence microservice boundaries and 21 CFR Part 11 audit separation.

---
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead


## 4. Decision Outcome

- **Chosen Option:** **Option A** because it delivers full regulatory compliance under ISO 14155 and EU MDR/IVDR while upholding strict architectural decoupling and GxP auditability.
- **Justification:** Selected as primary architectural approach.

### Architecture Highlights:
- **UDI Barcode Engine:** Stateless regex-based GS1 Application Identifier (AI 01, 10, 11, 17, 21, 8012) and HIBCC primary/secondary parser validating check digits (GTIN Mod 10, HIBCC Mod 43).
- **RTSM Quarantine Enforcement:** Any device in `QUARANTINED` status or matching an active lot quarantine is immediately blocked from dispensation (`DeviceQuarantinedError`), preventing compromised devices from reaching participants.
- **Safety Investigation Auto-Drafting:** Reporting a deficiency automatically creates an Adverse Device Effect investigation case in `apps/safety` with vigilance deadlines (2d, 10d, 15d).
- **SDTM & Incident Form Synthesis:** CDISC SDTM DD and DO domain generation with full audit attribution and MIR v7.2 JSON/XML output.

---

## 5. Consequences & Trade-offs

* **Positive:** Complete traceability from physical barcode scan through inventory allocation, clinical deficiency capture, safety escalation, and regulatory authority submission.
* **Positive:** Safe fail-closed dispensation blocking prevents accidental allocation of quarantined investigational devices.
* **Trade-off:** Requires maintaining synchronized domain models and test fixtures across microservices.

---

## 6. Implementation & Verification

* **Core Engine:**
  - `apps/execution/database/models/device.py`
  - `apps/execution/domain/device_models.py`
  - `apps/execution/device_investigation.py`
  - `apps/execution/presentation/routers/devices.py`
  - `apps/execution/domain/sdtm/models.py` (DD & DO domains)
* **Safety Microservice:**
  - `apps/safety/infrastructure/models.py`
  - `apps/safety/presentation/routers/safety.py`
  - `apps/safety/presentation/dtos.py`
* **Interop Microservice:**
  - `apps/interop/presentation/routers/devices.py`
  - `apps/interop/domain/`
* **Frontend:**
  - `apps/web/src/components/clinical/DeviceInvestigationConsole.vue`
  - `apps/web/src/views/DeviceInvestigationView.vue`
* **Automated Tests:**
  - `apps/execution/tests/test_device_investigation_udi_quarantine.py`
  - `apps/safety/tests/test_device_safety_investigation.py`
  - `apps/interop/tests/test_device_sdtm_regulatory_exports.py`
