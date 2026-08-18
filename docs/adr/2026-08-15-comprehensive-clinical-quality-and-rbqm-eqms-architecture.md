# ADR-2181: Comprehensive Clinical Quality and RBQM eQMS Architecture

- **Status:** Accepted
- **Date:** 2026-08-15
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Modern clinical research operations require a unified, GxP-compliant electronic Quality Management System (eQMS) and Risk-Based Quality Management (RBQM) platform conforming to ICH E6(R2/R3), ICH E8(R1), 21 CFR Part 11, EU Annex 11, and TransCelerate guidelines.
Historically, quality activities (protocol deviations, root cause analyses, CAPAs, clinical audits, and KRI monitoring) were siloed across disparate spreadsheets or disjointed third-party point solutions. In Cadence Clinical, quality operations must be unified under `apps/quality` while maintaining strict microservice decoupling, immutable Part 11 audit trails, REST API contracts, and high-performance hexagonal domain ports.

This architecture directly addresses requirements `PRD-QLT-001` through `PRD-QLT-009`.

## 2. Decision Drivers & Constraints

- **GxP & 21 CFR Part 11 Compliance (`PRD-QLT-003`, `PRD-QLT-008`, `PRD-QLT-009`):** All records (deviations, RCAs, CAPAs, action items, audits, and breaches) must carry mandatory audit attributes (`created_at`, `created_by`, `version_index`, `reason_for_change`) with an append-only `QualityAuditLog` and cryptographic step-up re-authentication signatures for approvals and closures.
- **TransCelerate & ICH E6(R3) RBQM Engine (`PRD-QLT-004`, `PRD-QLT-005`):** Standardize Critical to Quality (CtQ) factors, automated statistical anomaly scoring (Z-scores) across site KRIs, composite weighted Site Risk Index (SRI), and study-level Quality Tolerance Limit (QTL) breach detection with automated Clinical Study Report (CSR Section 9.6) text generation.
- **Multi-Methodology RCA & 6-Stage Gate CAPA (`PRD-QLT-001`, `PRD-QLT-002`, `PRD-QLT-003`):** Provide hierarchical 5-Whys causal trees and 6M Ishikawa/Fishbone visual structures, plus strict stage gating with scheduled effectiveness check intervals (30/60/90 days) and automated recurrence flagging.
- **Clinical Audits & Serious Breach Reporting (`PRD-QLT-006`, `PRD-QLT-007`):** Multi-type audit planning with graded findings, 1-click CAPA promotion, regulatory 7-day clock countdowns, and 1-click Inspection Readiness Dossier packaging.
- **Microservice Decoupling & Hexagonal Architecture:** Clean separation of domain entities, ports, SQL adapters, and modular FastAPI routers without sibling database imports.

## 3. Options Considered

1. **Monolithic Extension of CTMS/Execution:** Embed quality operations inside `apps/ctms` or `apps/execution`.
   - _Cons:_ Relational leakage, complex schema invalidation, cross-app failure propagation.
2. **Dedicated Modular eQMS Microservice in `apps/quality` with Hexagonal Architecture (Selected):**
   - _Pros:_ Complete domain isolation, independent database schema and Alembic migrations, unified REST ingestion endpoint (`/api/v1/quality/ingest/event`), dedicated Vue 3 Quality Cockpit, and strict audit isolation.

## 4. Decision Outcome

Chosen option: **Option 2 (Dedicated Modular eQMS Microservice in `apps/quality`)**.
`apps/quality` acts as the single source of truth for clinical trial quality events, CAPAs, RBQM statistical scoring, clinical audits, and inspection readiness dossiers.

## 5. Consequences & Trade-offs

- **Positive:** Complete regulatory alignment with ICH E6(R3) and 21 CFR Part 11; rich interactive visualizations (Fishbone, 5-Why, RBQM Heatmaps, CAPA Kanban); automated ingestion from EDC, CTMS, and eTMF.
- **Negative / Operational Mitigation:** Requires managing dedicated relational database sessions (`QUALITY_DATABASE_URL`), which is standardized using `packages.database.get_relational_db_lifespan`.

## 6. Implementation & Verification

- **Domain & Adapters:** `apps/quality/domain/models.py`, `apps/quality/domain/ports.py`, `apps/quality/adapters/models.py`, `apps/quality/adapters/repositories.py`.
- **Application Services:** `quality_service.py`, `rbqm_service.py`, `audit_service.py`, `serious_breach_service.py`.
- **Presentation Routers:** `deviations.py`, `rca.py`, `capas.py`, `rbqm.py`, `audits.py`, `serious_breaches.py`, `audit_logs.py`.
- **Frontend:** `AuditView.vue` and Quality Cockpit UI modules under `apps/web/src/views/AuditView.vue`.
- **Verification:** Unit and integration tests under `apps/quality/tests/`, schema validation via `scripts/validate_schemas.py`, and GxP synchronization via `scripts/sync_gxp.py`.
