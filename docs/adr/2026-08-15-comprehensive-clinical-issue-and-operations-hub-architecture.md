# ADR-2183: Comprehensive Clinical Issue and Operations Hub Architecture

- **Status:** Accepted
- **Date:** 2026-08-15
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical research systems require an integrated issue management and operational triage mechanism that bridges technical system incidents (access provisioning, software faults) and clinical operations events (protocol deviations, data queries, safety adverse events, cold-chain temperature excursions, and site monitoring findings). Traditional eClinical ecosystems maintain fragmented ticketing systems separate from EDC, CTMS, Safety, and eTMF platforms, leading to disconnected audit trails, uncoordinated SLA escalations, and regulatory inspection vulnerabilities under 21 CFR Part 11 and ICH GCP E6(R3).

This architecture implements a unified **Clinical Issue & Operations Hub** (`apps/tickets`) that serves as the central operations nerve center across all Cadence Clinical applications.

Reference: `PRD-TCK-001` (see `docs/SDLC/01_Product_Requirements_Document_PRD.md`).

## 2. Decision Drivers & Constraints

- **ICH GCP & GxP Issue Taxonomy:** Must support granular operational categories (Protocol Deviations, Data Discrepancies, Safety AE Events, Supply/Temperature Excursions, Site Operations, Monitoring Findings, Access Control) with GxP severity classifications (`MINOR`, `MAJOR`, `CRITICAL`) per `PRD-TCK-001`.
- **Cross-App Event Ingestion:** Sibling microservices (`apps/execution`, `apps/ctms`, `apps/safety`, `apps/quality`, `apps/etmf`) must ingest tickets programmatically via secure internal gateway-authenticated REST endpoints (`/api/v1/tickets/cross-app/events`) with typed entity bindings (Subject, CRF Form, SAE Case, CAPA Action, Document).
- **Multi-Tier SLA Engine:** Configurable clinical SLA resolution targets with pause mechanics for external dependencies (`WAITING_ON_SITE`, `WAITING_ON_SPONSOR`), amber warning thresholds (>75% elapsed), and automatic breach escalation to Lead CRAs and Quality Assurance.
- **21 CFR Part 11 & Root Cause Enforcement:** Mandate structured Root Cause Analysis (5-Whys) and resolution codes (`CORRECTIVE_ACTION_IMPLEMENTED`, `PROTOCOL_CLARIFICATION_ISSUED`) prior to resolving Major/Critical issues, with re-authenticated cryptographic electronic signature capture (`/api/v1/tickets/{id}/sign`).
- **Audited Evidence Attachments & Dual Visibility:** Support tamper-evident file attachments with SHA-256 integrity verification, DEID scrubbing, and dual-visibility comment streams (`PUBLIC` vs `INTERNAL_SPONSOR`).

## 3. Options Considered

1. **Option 1: Decentralized In-App Ticketing (Status Quo)**
   - Maintain separate discrepancy/issue queues inside EDC, CTMS, Safety, and eTMF.
   - *Rejected:* Prevents unified operational oversight, breaks cross-study reporting, duplicates SLA logic, and creates audit fragmentation during regulatory inspections.

2. **Option 2: Third-Party External Ticketing Integration (Jira/ServiceNow)**
   - Proxy all clinical operations tickets to Jira or ServiceNow APIs.
   - *Rejected:* Third-party SaaS tools lack native CDISC entity models, GxP Part 11 electronic signature workflows, protocol-amendment locks, and sub-100ms internal microservice SLA guarantees.

3. **Option 3 (Selected): Unified Clinical Issue & Operations Hub Microservice**
   - Standalone `apps/tickets` service with REST API-first contracts, hexagonal architecture, and strict boundary isolation.
   - Event-driven cross-app ingestion via internal Gateway HMAC authentication.
   - Integrated SLA engine, 5-Whys RCA, Part 11 eSignatures, and multi-channel notification dispatch.

## 4. Decision Outcome

Chosen option: **Option 3 (Unified Clinical Issue & Operations Hub Microservice)**. It provides a standardized, Part 11-compliant operations nerve center across all clinical modules while adhering to our REST API-first and microservice decoupling standards.

## 5. Consequences & Trade-offs

- **Positive:**
  - Single pane of glass for clinical issues, deviations, monitoring findings, and system requests across all studies and sites.
  - Granular GxP audit trail and re-authenticated 21 CFR Part 11 electronic signature captures.
  - Decoupled SLA calculation with pause support for external operational delays.
  - Secure evidence file attachments with automated SHA-256 verification and DEID compliance.
- **Negative / Neutral:**
  - Additional database models (`ticket_attachments`, `ticket_audit_logs`) and background escalation worker routines.

## 6. Implementation & Verification

- **Domain & Application Services:**
  - `apps/tickets/domain/models.py`: Enums for `GxPSeverity`, `RootCauseCategory`, `ResolutionCode`, `CommentVisibility`, `TicketCategory`, and SLA pause states.
  - `apps/tickets/domain/services.py`: SLA calculation, amber warning thresholds, and RCA resolution validators.
  - `apps/tickets/adapters/models.py`: SQLModel tables for `Ticket`, `TicketComment`, `TicketAttachment`, and `TicketAuditLog`.
  - `apps/tickets/adapters/analytics.py`: KPI calculation (MTTR, SLA compliance, site breakdown).
- **Presentation & REST Endpoints:**
  - `apps/tickets/presentation/routers/tickets.py`: REST routes for tickets, comments, cross-app ingestion, file attachments, eSignatures, analytics, and audit exports.
- **Frontend Web Workspace:**
  - `apps/web/src/views/TicketsView.vue`: Full clinical workspace with Kanban board, data table, and KPI cards.
  - `apps/web/src/components/tickets/TicketDetailDrawer.vue`: Multi-tab detail drawer.
  - `apps/web/src/components/tickets/TicketCreateModal.vue`: Issue creation modal.
  - `apps/web/src/components/tickets/TicketSignModal.vue`: 21 CFR Part 11 eSignature modal.
- **Automated Test Suites:**
  - `apps/tickets/tests/test_cross_app_ingestion.py`: Cross-service ingestion tests.
  - `apps/tickets/tests/test_tickets_sla_advanced.py`: SLA pause and amber warning tests.
  - `apps/tickets/tests/test_part11_signatures_attachments.py`: 21 CFR Part 11 signatures, attachments, and RCA tests.
  - `apps/tickets/tests/test_tickets_analytics.py`: KPI analytics and export tests.
