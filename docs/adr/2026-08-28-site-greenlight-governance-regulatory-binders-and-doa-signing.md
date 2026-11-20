# ADR-2220: Site Greenlight Governance Regulatory Binders and DOA Signing

- **Status:** Accepted
- **Date:** 2026-08-28
- **Authors:** @fderuiter
- **Deciders:** Engineering Architecture Board, Clinical Operations Working Group

* **Requirements:** PRD-SSU-001, PRD-SSU-002, PRD-SYS-001

---

## 1. Context & Problem Statement

Clinical site startup and activation require rigorous verification across regulatory approvals, executed legal contracts, completed investigator binders, delegated duties, and investigational product release. Activating an unapproved site or enrolling subjects before regulatory prerequisites are met violates FDA 21 CFR 312, EU CTR 536/2014, and ICH GCP E6(R2)/(R3).

The platform requires:

1. Dynamic, jurisdiction-aware site activation rules (US FDA, EU CTR, JP PMDA, UK MHRA, Global).
2. A 6-facet site greenlight engine issuing cryptographic HMAC-SHA256 tokens and supporting 21 CFR Part 11 dual-signature sponsor overrides.
3. Sovereign execution gatekeeper middleware blocking subject randomization and eCRF writes on non-greenlight sites (`HTTP 423 Resource Locked`).
4. DIA Reference Model v3.2.0 (10 zones) in eTMF, Section 1–7 eISF binder taxonomy, and zero-downtime dual-read storage port adapters.
5. Principal Investigator electronic Delegation of Authority (DOA) log with 21 CFR Part 11 electronic signatures, cryptographic audit seals, and certified PDF export.

## 2. Decision Drivers & Constraints

- **ICH GCP E6(R2) §4.1.5 / §5.1.1**: The Principal Investigator must maintain a list of appropriately qualified persons to whom significant trial-related duties have been delegated.
- **FDA 21 CFR 312.60 / 312.62**: Investigational drug may only be administered to subjects under the personal supervision of the investigator after protocol prerequisites are satisfied.
- **Zero-Trust Microservice Isolation**: `apps/execution` must verify site greenlight status autonomously via HMAC signatures without direct sibling database dependencies.
- **ALCOA+ Audit Immutability**: All greenlight overrides, DOA task delegations, and eISF/eTMF sync actions must be immutably recorded in 21 CFR Part 11 audit trails.

## 3. Options Considered

### Option 1: **Option 1 (Selected): Dynamic Country Rules + Cryptographic Greenlight Tokens + Sovereign Execution Gate + Part 11 DOA Engine**:

- **Overview:** - Multi-factor 6-facet greenlight engine evaluates IRB, CTA, eISF/Docs, DOA, Training, and IP Release against jurisdiction presets.
  - Issues HMAC-SHA256 site activation tokens verified by `apps/execution` middleware.
  - Dual-signature sponsor override requiring two distinct executive roles (`sponsor_clinician` + `regulatory_lead`) with mandatory change justification.
  - Deterministic eISF/eTMF sync with correlation keys, CRA QC staging, and dual-read storage adapters.
  - Principal Investigator electronic DOA log with signature tokens (`execution.doa.signoff`), immutable audit ledger seals, and certified PDF export.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option 2: Static Monolithic Checklist**:

- **Overview:** - Fixed hardcoded checklist across all countries with manual CRA activation checkboxes and direct shared database access.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

### Option 3: **Option 3: Pure Paper/External Integration**:

- **Overview:** - Offload site startup and DOA logs entirely to third-party paper scans without execution gating.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

## 4. Decision Outcome

**- **Chosen Option:** ** Option 1 because it guarantees statutory regulatory compliance, enforces zero-trust execution boundaries, prevents premature subject enrollment with RFC 7807 `HTTP 423 Locked`, and provides end-to-end 21 CFR Part 11 audit traceability.

## 5. Consequences & Trade-offs

- **Positive**:
  - Eliminates regulatory risk of unauthorized site activation or rogue subject randomization.
  - Transparent 6-facet readiness tracking with real-time jurisdictional prerequisite evaluation.
  - Compliant electronic DOA delegation log fulfilling global inspection requirements.
  - Seamless eISF-to-eTMF document flow with ALCOA+ CRA QC verification.
- **Negative**:
  - Requires maintaining country prerequisite matrix presets across global regulatory changes.
  - Dual-signature override ceremony introduces structured workflow overhead for executive approvals.

## 6. Implementation & Verification

- **Core Target Modules**:
  - `apps/ctms/`: Country rules matrix, 6-facet greenlight evaluator, dual-signature override endpoints, DOA delegation manager (`apps/ctms/presentation/routers/doa.py`, `apps/ctms/application/doa_service.py`).
  - `apps/execution/`: `greenlight_gating.py` execution gatekeeper middleware, DOA verification routes.
  - `apps/eisf/` & `apps/etmf/`: DIA Reference Model v3.2.0 catalog, eISF 1–7 binder taxonomy, QC staging, dual-read storage port.
- **Verification Suites**:
  - `apps/ctms/tests/test_ssu_country_greenlight_activation.py`
  - `apps/execution/tests/test_greenlight_gating.py`
  - `apps/ctms/tests/test_doa_service.py`
  - `apps/ctms/tests/test_doa_router.py`
  - `apps/ctms/tests/test_doa_audit_suite.py`
  - `apps/eisf/tests/` (74 passed tests)
  - `apps/etmf/tests/` (192 passed tests)
