# ADR-2180: Comprehensive CTMS Modular Architecture and Ecosystem Synchronization

- **Status:** Accepted
- **Date:** 2026-08-15
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical Trial Management Systems (CTMS) coordinate mission-critical trial operations including site startup/regulatory greenlight, monitoring visit trip reports, protocol deviations and action items, Risk-Based Quality Management (RBQM/KRIs), investigator grant financials & payables, site IP accountability, and Delegation of Authority (DOA). Previously, CTMS operations were partially centralized in monolithic routers without dedicated sub-domain boundaries, automated Greenlight gating, dynamic KRI calculation engines, procedure-based auto-payables from EDC, or automated DIA TMF Reference Model eTMF synchronization.

Applicable System Requirements:

- `PRD-CTMS-001` (Operational Site & Milestone Tracking)
- `PRD-CTMS-002` (Monitoring Visit Reports Correspondence Lifecycle)
- `PRD-CTMS-003` (CRA Allocation & Workload Summaries)
- `PRD-CTMS-004` (Standard GxP Site Audit Trail)
- `PRD-CTMS-005` (Site Regulatory Greenlight & Essential Document Gatekeeper)
- `PRD-CTMS-006` (Protocol Deviation Lifecycle & Quality CAPA Escalation)
- `PRD-CTMS-007` (Risk-Based Quality Management & KRI Engine)
- `PRD-CTMS-008` (Procedure-Based Financials & EDC Auto-Payables)
- `PRD-CTMS-009` (Investigational Product Accountability & Temperature Excursions)
- `PRD-CTMS-010` (Automated DIA TMF Reference Model Synchronization)

## 2. Decision Drivers & Constraints

- Strict 21 CFR Part 11 and ICH GCP E6(R2)/(R3) auditability and electronic signature manifestations.
- Decentralized microservice boundaries: No sibling database imports across microservice paths; inter-service communication via internal HMAC-signed REST endpoints (`apps/etmf`, `apps/quality`, `apps/safety`, `apps/execution`).
- High performance (<100ms SLA) and asynchronous execution with SQLAlchemy / SQLModel.
- Clean separation of concerns using modular hexagonal sub-domains.

## 3. Options Considered

1. **Modular Hexagonal Sub-domain Architecture (Selected)**: Partition CTMS into 7 decoupled sub-domain packages (`site_startup`, `monitoring`, `deviations_issues`, `rbqm`, `financials`, `ip_accountability`, `doa`) with decoupled application services, repository ports, SQL persistence adapters, and dedicated REST routers.
2. **Monolithic Central Router Extension**: Keep all models and endpoints within legacy monolithic files rather than decoupled sub-packages.

## 4. Decision Outcome

Chosen option: **Modular Hexagonal Sub-domain Architecture** because it provides strict operational boundaries, enables unit and integration test isolation, enforces GxP traceability, and cleanly integrates with downstream eTMF, Quality/CAPA, Safety, and EDC execution services.

Key Sub-domains & Capabilities:

- **Site Startup & Regulatory Greenlight (`site_startup`)**: Manages Country/Site regulatory milestones and Essential Document Lists (EDL) with an automated Greenlight rule engine blocking unapproved site activation.
- **Monitoring Visits & Trip Reports (`monitoring`)**: Pre-visit letters, structured visit report findings/action items, follow-up letters, Part 11 sign-off, and automated eTMF push.
- **Protocol Deviations & Issues (`deviations_issues`)**: Minor/Major/Critical deviation lifecycle, 5-Why RCA, and automated CAPA escalation to `apps/quality`.
- **RBQM & Centralized Monitoring (`rbqm`)**: Dynamic calculation of Query Velocity, SAE Lag, Deviation Density, Form Entry Lag, SDV Backlog, and Site Risk Index scoring.
- **Investigator Financials & EDC Auto-Payables (`financials`)**: Procedure-based visit payment grids, holdback rules (e.g., 10% retention), passthrough expenses, and automated payable generation on EDC subject milestone triggers.
- **IP Accountability & Temperature Excursions (`ip_accountability`)**: Kit/lot receipt, temperature excursion logging with quarantine disposition, subject dispensation reconciliation, and Certificates of Destruction.
- **DOA & Site Personnel (`doa`)**: Principal Investigator oversight, staff onboarding, task delegation, GCP/CV credentialing, and Part 11 sign-offs.
- **eTMF Connector (`etmf_sync`)**: Automated packaging and push of finalized documents mapped to DIA TMF Reference Model zones (Zone 04, 05, 06, 08).

## 5. Consequences & Trade-offs

- Positive: Full market-leading feature parity with Veeva Vault CTMS and Medidata CTMS. Clean, testable, type-safe architecture.
- Positive: Complete 21 CFR Part 11 compliance with append-only `CTMSAuditLog` entries on all mutations.
- Positive: Automated cross-service Digital Data Flow (DDF) integration across eTMF, Quality, Safety, and EDC.
- Trade-off: Additional sub-domain router files and data transfer objects, cleanly managed through modular packaging.

## 6. Implementation & Verification

- Domain layer models & ports in `apps/ctms/domain/`.
- Application services in `apps/ctms/application/`.
- Adapters, models, repositories, and cross-service clients in `apps/ctms/adapters/`.
- Presentation DTOs and sub-domain routers in `apps/ctms/presentation/`.
- Verified by comprehensive pytest test suite under `apps/ctms/tests/` and synchronized via `scripts/sync_gxp.py`.
