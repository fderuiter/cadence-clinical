# ADR-2183: Comprehensive Clinical Issue and Operations Hub Architecture

* **Status:** Accepted
* **Date:** 2026-08-15
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical research systems require an integrated issue management and operational triage mechanism that bridges technical system incidents (access provisioning, software faults) and clinical operations events (protocol deviations, data queries, safety adverse events, cold-chain temperature excursions, and site monitoring findings). Traditional eClinical ecosystems maintain fragmented ticketing systems separate from EDC, CTMS, Safety, and eTMF platforms, leading to disconnected audit trails, uncoordinated SLA escalations, and regulatory inspection vulnerabilities under 21 CFR Part 11 and ICH GCP E6(R3).

This architecture implements a unified **Clinical Issue & Operations Hub** (`apps/tickets`) that serves as the central operations nerve center across all Cadence Clinical applications.

Reference: [PRD-TCK-001](file:///Users/fred/Code/cadence-clinical/docs/SDLC/01_Product_Requirements_Document_PRD.md).

## 2. Decision Drivers & Constraints

* **ICH GCP & GxP Issue Taxonomy:** Must support granular operational categories (Protocol Deviations, Data Discrepancies, Safety AE Events, Supply/Temperature Excursions, Site Operations, Monitoring Findings, Access Control) with GxP severity classifications (`MINOR`, `MAJOR`, `CRITICAL`) per PRD-TCK-001.
* **Cross-App Event Ingestion:** Sibling microservices (`apps/execution`, `apps/ctms`, `apps/safety`, `apps/quality`, `apps/etmf`) must ingest tickets programmatically via secure internal gateway-authenticated REST endpoints (`/api/v1/tickets/cross-app/events`) with typed entity bindings (Subject, CRF Form, SAE Case, CAPA Action, Document).
* **Multi-Tier SLA Engine:** Configurable clinical SLA resolution targets with pause mechanics for external dependencies (`WAITING_ON_SITE`, `WAITING_ON_SPONSOR`), amber warning thresholds (>75% elapsed), and automatic breach escalation to Lead CRAs and Quality Assurance.
* **21 CFR Part 11 & Root Cause Enforcement:** Mandate structured Root Cause Analysis (5-Whys) and resolution codes (`CORRECTIVE_ACTION_IMPLEMENTED`, `PROTOCOL_CLARIFICATION_ISSUED`) prior to resolving Major/Critical issues, with re-authenticated cryptographic electronic signature capture (`/api/v1/tickets/{id}/sign`).
* **Audited Evidence Attachments & Dual Visibility:** Support tamper-evident file attachments with SHA-256 integrity verification, DEID scrubbing, and dual-visibility comment streams (`PUBLIC` vs `INTERNAL_SPONSOR`).

## 3. Options Considered

1. **Option A: Hexagonal Multi-Tier Clinical Operations Hub with Part 11 eSignatures & Cross-App REST Gateway (Selected)**
   - Standalone microservice with domain-driven design (`domain`, `application`, `adapters`, `presentation`).
   - Cross-app REST ingestion with signature validation, eliminating direct cross-boundary database imports.
   - Comprehensive KRI/KPI analytics and Part 11 export endpoints (JSON/CSV).
   - Modern Vanilla CSS clinical workspace in `apps/web/src/views/TicketsView.vue` with Kanban/Table toggle, interactive countdown timers, and slide-over multi-tab drawers.

2. **Option B: Embedded EDC Query System with Basic Technical Ticket Queue**
   - Simple issue queue embedded directly inside `apps/execution`.
   - Lacks cross-service scope, ICH GCP severity escalation, dual comment streams, and standalone Part 11 audit trails.

## 4. Decision Outcome

Chosen option: **Option A** because it provides an enterprise-grade clinical helpdesk and issue management architecture meeting all 21 CFR Part 11 and ICH GCP E6(R3) regulatory requirements while maintaining microservice isolation.

## 5. Consequences & Trade-offs

* **Positive:**
  - Standardized cross-service REST ingestion pipeline with audit justification enforcement.
  - Complete visibility into clinical KRIs, MTTR, and SLA breach risks across all active study sites.
  - Immutably recorded 21 CFR Part 11 electronic signatures and tamper-evident attachments.
  - Granular persona scoping with dual-visibility comments protecting sponsor-internal deliberations from unblinded site exposure.
* **Trade-offs:**
  - Additional database models (`ticket_attachments`, `ticket_audit_logs`) and background escalation worker routines.

## 6. Implementation & Verification

* **Domain & Application Services:**
  - [`apps/tickets/domain/models.py`](file:///Users/fred/Code/cadence-clinical/apps/tickets/domain/models.py): Enums for `GxPSeverity`, `RootCauseCategory`, `ResolutionCode`, `CommentVisibility`, `TicketCategory`, and SLA pause states.
  - [`apps/tickets/domain/services.py`](file:///Users/fred/Code/cadence-clinical/apps/tickets/domain/services.py): SLA calculation, amber warning thresholds, and RCA resolution validators.
  - [`apps/tickets/adapters/models.py`](file:///Users/fred/Code/cadence-clinical/apps/tickets/adapters/models.py): SQLModel tables for `Ticket`, `TicketComment`, `TicketAttachment`, and `TicketAuditLog`.
  - [`apps/tickets/application/analytics.py`](file:///Users/fred/Code/cadence-clinical/apps/tickets/application/analytics.py): KPI calculation (MTTR, SLA compliance, site breakdown).
* **Presentation & REST Endpoints:**
  - [`apps/tickets/presentation/routers/tickets.py`](file:///Users/fred/Code/cadence-clinical/apps/tickets/presentation/routers/tickets.py): REST routes for tickets, comments, cross-app ingestion, file attachments, eSignatures, analytics, and audit exports.
* **Frontend Web Workspace:**
  - [`apps/web/src/views/TicketsView.vue`](file:///Users/fred/Code/cadence-clinical/apps/web/src/views/TicketsView.vue): Full clinical workspace with Kanban board, data table, and KPI cards.
  - [`apps/web/src/components/tickets/TicketDetailDrawer.vue`](file:///Users/fred/Code/cadence-clinical/apps/web/src/components/tickets/TicketDetailDrawer.vue): Multi-tab detail drawer.
  - [`apps/web/src/components/tickets/TicketCreateModal.vue`](file:///Users/fred/Code/cadence-clinical/apps/web/src/components/tickets/TicketCreateModal.vue): Issue creation modal.
  - [`apps/web/src/components/tickets/TicketSignModal.vue`](file:///Users/fred/Code/cadence-clinical/apps/web/src/components/tickets/TicketSignModal.vue): 21 CFR Part 11 eSignature modal.
* **Automated Test Suites:**
  - [`apps/tickets/tests/test_cross_app_ingestion.py`](file:///Users/fred/Code/cadence-clinical/apps/tickets/tests/test_cross_app_ingestion.py): Cross-service ingestion tests.
  - [`apps/tickets/tests/test_tickets_sla_advanced.py`](file:///Users/fred/Code/cadence-clinical/apps/tickets/tests/test_tickets_sla_advanced.py): SLA pause and amber warning tests.
  - [`apps/tickets/tests/test_part11_signatures_attachments.py`](file:///Users/fred/Code/cadence-clinical/apps/tickets/tests/test_part11_signatures_attachments.py): 21 CFR Part 11 signatures, attachments, and RCA tests.
  - [`apps/tickets/tests/test_tickets_analytics.py`](file:///Users/fred/Code/cadence-clinical/apps/tickets/tests/test_tickets_analytics.py): KPI analytics and export tests.
