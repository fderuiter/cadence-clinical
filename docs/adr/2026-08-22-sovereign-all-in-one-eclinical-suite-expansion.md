# ADR-2199: Sovereign All-in-One eClinical Suite Expansion

- **Status:** Accepted
- **Date:** 2026-08-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

* **Requirement:** PRD-SYS-060

---

## 1. Context & Problem Statement

Legacy eClinical ecosystems (e.g., Medidata Clinical Cloud, Veeva Vault Clinical Suite, Oracle Clinical One) are fundamentally fragmented by decades of corporate acquisitions and point-to-point batch synchronizations. Clinical data is siloed across separate EDC, CTMS, eTMF, RTSM, Safety, and Site Payment platforms. This causes multi-month study startup delays, complex reconciliation jobs, manual duplicate entry, and brittle integrations.

Cadence Clinical possesses a distinct architectural foundation: an automated **Digital Data Flow (DDF)** and a **CDISC USDM Neo4j Graph Spine** that compiles upstream digitized protocols into downstream runtime infrastructure. To establish global market leadership and eliminate every capability gap present against Veeva, Medidata, and Oracle, Cadence is establishing a unified sovereign suite architecture spanning all 12 enterprise eClinical pillars.

## 2. Decision Drivers & Constraints

- **Total Feature Parity & Superiority:** Match and exceed every feature in Veeva Vault (EDC, CTMS, eTMF, RTSM, Payments, Safety, SiteVault, Study Training), Medidata (Rave EDC, Balance, Patient Cloud, Rave Imaging, Detect/RBQM, Edge Payments, Safety Gateway, Coder), and Oracle (Clinical One, Argus Safety, DMW).
- **Single Source of Truth (Zero ETL):** All modules must bind directly to the CDISC USDM study graph and relational execution runtime, eliminating asynchronous batch replication and brittle third-party connectors.
- **GxP & 21 CFR Part 11 Compliance:** Unified audit ledger (`PRD-SYS-001`), immutable cryptographic chaining (`PRD-SYS-003`), role-based access control, dual-attribution AI review (`PRD-SYS-051`), and electronic signatures.
- **REST API-First & Microservice Decoupling:** Microservices communicate exclusively via gateway-authenticated REST APIs with HMAC signatures, maintaining strict GxP isolation boundaries and sub-100ms SLA connection pooling.

## 3. Options Considered

### Option 1: **Option A (Selected): Sovereign All-in-One eClinical Suite**

- **Overview:** Build native microservices and deep functional engines across all 12 clinical domains sharing the unified USDM/DDF core:
  - **MDR / Protocol Design:** CDISC USDM v3/v4 graph, Biomedical Concepts, ICH M11 sync, automated PDF digitization.
  - **EDC / CDMS:** Dynamic subject schema projection, cross-domain anomaly detection, multi-tier SDV/SDR, database locks.
  - **Site Payments & Financials:** Automated trigger-to-payable engine from EDC/CTMS milestones, split payees, holdbacks, remittance advice.
  - **Medical Imaging & Adjudication:** Zero-footprint DICOM viewer, in-flight pixel PHI scrubbing, dual-blinded IRC charters with tie-breaker adjudication.
  - **Enterprise Pharmacovigilance (PV):** Global regulatory reporting rules engine (FDA, EMA, PMDA, MHRA), AS2 electronic B2B gateway, disproportionality signal detection (PRR/ROR/EBGM), aggregate reports (DSUR/PBRER).
  - **Advanced RTSM & Supply Chain:** Multi-echelon inventory, MKT temperature excursion engine, adaptive cohort dosing (BOIN/CRM), cell/gene therapy vein-to-vein COC/COI.
  - **Centralized Statistical Monitoring (CSM):** TransCelerate RACT matrix, digit preference / variance anomaly detectors, data fabrication sentinels.
  - **Clinical Data Repository (CDR) & Validated SCE:** Multi-source data lakehouse, automated SDTM/ADaM pipelines, containerized reproducible R/Python analysis workspaces.
  - **Connected Study Training:** Automated protocol amendment curricula, role qualification, and EDC write gating.
  - **Decentralized Clinical Trials (DCT):** Connected biometric sensors, wearables (CGM, actigraphy, spirometry), and telehealth integration.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option B: Core Foundation with External Vendor Integrations**

- **Overview:** Maintain only core MDR, EDC, and basic CTMS, while delegating Enterprise PV to Oracle Argus, Imaging to BioTelemetry, and RTSM to 4G Clinical.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

## 4. Decision Outcome

**- **Chosen Option:** Option A (Sovereign All-in-One eClinical Suite)** because it leverages Cadence's existing DDF/USDM compilation engine to deliver an unparalleled, zero-reconciliation experience that eliminates the multi-million-dollar integration overhead characteristic of legacy competitors.

## 5. Consequences & Trade-offs

- **Positive:** Complete market dominance; zero-delay study build; immediate cross-module event reactivity (e.g. EDC visit -> instant site payment -> instant supply resupply -> instant TMF milestone check -> instant KRI computation).
- **Positive:** Single unified user experience across all sponsor, site, monitor, investigator, and patient personas.
- **Trade-off:** Expanded domain breadth requires rigorous ISO 29148 requirements specifications and complete GxP traceability matrices across all new functional areas.

## 6. Implementation & Verification

- **Requirements:** Specified in `docs/SDLC/01_Product_Requirements_Document_PRD.md` under `PRD-PAY-*`, `PRD-IMG-*`, `PRD-PVG-*`, `PRD-SCM-*`, `PRD-CSM-*`, `PRD-CDR-*`, `PRD-TRN-*`, `PRD-DCT-*`.
- **Domain Model:** Captured and defined in `CONTEXT.md`.
- **Blueprint:** Documented in `docs/COMPETITIVE_ANALYSIS_AND_MARKET_SUPERIORITY.md`.
- **GxP Traceability:** Synchronized via `uv run python scripts/sync_gxp.py`.
