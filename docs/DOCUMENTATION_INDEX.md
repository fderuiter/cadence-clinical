# Cadence Clinical Master Documentation Hub

Welcome to the **Cadence Clinical Platform** master documentation index. This portal provides a structured sitemap, architectural taxonomy, and maintenance guide for developers, system architects, and compliance auditors working with the platform.

---

## Documentation Architecture & Taxonomy

Cadence Clinical documentation is organized into a 6-layer hierarchy to maintain traceability across clinical requirements, architectural design decisions, API contracts, and GxP compliance ledgers:

```mermaid
graph TD
    Layer1["1. System Requirements & Scope<br/>(SRS.md, PRD.md)"]
    Layer2["2. Technical Architecture & Specs<br/>(ARCHITECTURE.md, TDD, Blueprint)"]
    Layer3["3. REST API Contract Reference<br/>(docs/api/* Microservice Catalogs)"]
    Layer4["4. Decision Records<br/>(docs/adr/ Domain Categories)"]
    Layer5["5. Developer & Operational Guides<br/>(AGENTS.md, CONTRIBUTING.md, CLI Guide)"]
    Layer6["6. Validation & GxP Traceability<br/>(RTM.md, Audit Specs, QA Plan)"]

    Layer1 --> Layer2
    Layer2 --> Layer3
    Layer2 --> Layer4
    Layer4 --> Layer6
    Layer5 --> Layer2
```

---

## 1. System Requirements & Scope

High-level clinical system contracts, product scope, and functional specifications.

- **[System Requirements Specification (SRS)](SRS.md)** (`SRS.md`): Upstream digital data flow (DDF), clinical metadata management, eCRF data model requirements, and system traceability specifications (`Trace-1` through `Trace-N`).
- **[Product Requirements Document (PRD)](SDLC/01_Product_Requirements_Document_PRD.md)** (`01_Product_Requirements_Document_PRD.md`): Functional capabilities, user roles, eClinical workflow definitions, and PRD requirement identifiers (`PRD-SYS-xxx`, `PRD-EDC-xxx`, `PRD-CTMS-xxx`, etc.).

---

## 2. Technical Architecture & Integration Specifications

Deep technical design specifications, database models, and service boundary definitions.

- **[System Architecture Guide](ARCHITECTURE.md)** (`ARCHITECTURE.md`): High-level system architecture, service communication topology, and database split (Neo4j for MDR, PostgreSQL for EDC).
- **[.NET 10 Platform Modernization Hub](migration/index.md)** (`docs/migration/index.md`): Master Phase 0 modernization baseline, 18-context decomposition, and .NET 10 transition roadmap.
  - **[Surface & Data Topology Audit](migration/SYSTEM_TOPOLOGY_AUDIT.md)** (`docs/migration/SYSTEM_TOPOLOGY_AUDIT.md`): Complete entry point catalog, persistence models, and dynamic behavior audit.
  - **[Dependency & Parity Catalog](migration/DEPENDENCY_AND_PARITY_CATALOG.md)** (`docs/migration/DEPENDENCY_AND_PARITY_CATALOG.md`): Python-to-.NET 10 library cross-walk, test fixtures, and golden datasets.
  - **[Bounded Context Migration Matrix](migration/BOUNDED_CONTEXT_MIGRATION_MATRIX.md)** (`docs/migration/BOUNDED_CONTEXT_MIGRATION_MATRIX.md`): 4-tier dependency sequence and Clean Architecture sentinels.
  - **[Solution Scaffold Guide](migration/SCAFFOLD_GUIDE.md)** (`docs/migration/SCAFFOLD_GUIDE.md`): Executable solution scaffold guide and verification steps.
  - **[Migration Workflow Playbook](MIGRATION_WORKFLOW.md)** (`docs/MIGRATION_WORKFLOW.md`): Authoritative 6-step service transcription SOP and Python decommissioning recipe.
- **[Technical Design Document (TDD)](SDLC/02_Technical_Design_Document_TDD.md)** (`02_Technical_Design_Document_TDD.md`): Internal micro-architecture, database schemas, cryptographic signature specs, and service isolation boundaries.
- **[API Integration Specification](SDLC/03_API_Integration_Specification.md)** (`03_API_Integration_Specification.md`): Gateway routes, request/response payload contracts, RFC 7807 error formats, and authentication propagation headers.
- **[Data Standards & Interoperability Blueprint](SDLC/04_Data_Standards_Interoperability_Blueprint.md)** (`04_Data_Standards_Interoperability_Blueprint.md`): CDISC USDM v3.0/v4.0, CDISC ODM, SDTM/ADaM exports, FHIR/eSource, and MedDRA/WHODrug medical coding integration.

---

## 3. Programmatic REST API Contract Reference

Comprehensive, statically extracted OpenAPI 3.1.0 specifications across all 20+ microservices with GxP role gates, step-up tokens, and code snippets.

- **[Master REST API Reference](api/index.md)** (`docs/api/index.md`): Master catalog, gateway proxy topology, Keycloak OIDC headers, and RFC 7807 problem detail models.
- **[Study Designer & USDM API](api/designer.md)** (`docs/api/designer.md`): CDISC USDM protocol authoring, SoA matrix, and Biomedical Concept endpoints.
- **[EDC Execution API](api/execution.md)** (`docs/api/execution.md`): Dynamic eCRF schema projection, subject progression, and Dataset-JSON exports.
- **[CTMS Operations API](api/ctms.md)** (`docs/api/ctms.md`): Monitoring visit reports, site milestones, and 6-facet greenlight activation.
- **[Safety & Pharmacovigilance API](api/safety.md)** (`docs/api/safety.md`): ICH E2B(R3) ICSR XML, CTCAE grading, and AS2 S/MIME EDI gateway.
- **[Quality & CAPA API](api/quality.md)** (`docs/api/quality.md`): Protocol deviations, root-cause analyses, Benford CSM forensics, and pHash image duplicate checks.
- **[Site Payments & Grants API](api/payments.md)** (`docs/api/payments.md`): Procedure fee schedules, multi-payee splits, and participant expense claims.
- **[Blinded Imaging Adjudication API](api/imaging.md)** (`docs/api/imaging.md`): Blinded dual-reader RECIST 1.1 evaluations and 20% discordance triggers.

---

## 4. Architectural Decision Records (ADRs)

Immutable chronological records of key architectural and technical decisions made throughout system evolution. All ADRs are categorized into 7 functional domains:

- **[Architectural Decision Records Index](adr/index.md)** (`docs/adr/index.md`): Categorized ADR repository including:
  1. **Core Platform & Engine**: Graph DB, PostgreSQL, Pydantic v2, rules engine, dynamic diffing.
  2. **API Gateway & Security**: Keycloak identity, RBAC, Gateway aggregation, canonical JSON signatures.
  3. **Clinical Data Interoperability & Standards**: USDM v3/v4 mapping, ODM, SDTM/ADaM, FHIR, medical coding.
  4. **Clinical Operations & Business Modules**: CTMS, eTMF, RTSM, eCOA/ePRO, eConsent, SDV/TSDV, Quality/CAPA, eISF.
  5. **Compliance, Audit & Governance**: 21 CFR Part 11 Audit Trail, Merkle root sealing, shadow triggers.
  6. **Frontend & Design System**: Vue 3 SPA, pnpm workspace, shared UI primitives, grid layouts.
  7. **DevOps, Tooling & CI/CD**: Parallel testing, DB CLI tool, markdown/ADR validation scripts.

---

## 5. Developer Experience & Operations

Guidelines for setting up, building, running, and contributing code to Cadence Clinical.

- **[Local Development Environment](LOCAL_DEV_ENVIRONMENT.md)** (`LOCAL_DEV_ENVIRONMENT.md`): Setup instructions, environment variables, Docker services, and database migrations.
- **[Issue Structure, Work Streams & Project Board Guide](SDLC/ISSUE_STRUCTURE_GUIDE.md)** (`SDLC/ISSUE_STRUCTURE_GUIDE.md`): Standardized issue formatting, 8 parallel work streams, readiness badges, GitHub Project 17 automation, and DoD requirements.
- **[AI Agent & Development Instructions](AGENTS.md)** (`AGENTS.md`): Architectural guardrails, coding standards, directory targeting rules, and PR verification gates.
- **[Contributing Guidelines](../CONTRIBUTING.md)** (`CONTRIBUTING.md`): Workspace development standards, code style, formatting tools, and the Issue-to-Doc Sync Workflow.
- **[Operations & Deployment Guide](SDLC/07_Operations_Deployment_Guide.md)** (`07_Operations_Deployment_Guide.md`): Production deployment topology, Docker orchestration, monitoring, and database backup protocols.
- **[Lab Reference Ranges Guide](OPERATIONAL_LAB_RANGES.md)** (`OPERATIONAL_LAB_RANGES.md`): Outlines the technical and regulatory mechanisms used to manage, validate, and update laboratory reference ranges, including multi-dimensional scoring and deterministic tie-breaking.
- **[Inbound Email Ingestion Guide](OPERATIONAL_INBOUND_EMAIL.md)** (`OPERATIONAL_INBOUND_EMAIL.md`): Details security protocols and configurations for the eTMF inbound-email webhook, including HMAC signature validation and attachment processing.

---

## 6. Validation & GxP Compliance Ledger

GxP audit readiness, requirement traceability, and automated verification reports.

- **[Requirements Traceability Matrix (RTM)](SDLC/Requirements_Traceability_Matrix.md)** (`Requirements_Traceability_Matrix.md`): Automated bidirectional mapping between PRD/SRS requirements, test suite execution results, and ADRs.
- **[Security, Compliance & Audit Spec](SDLC/05_Security_Compliance_Audit_Spec.md)** (`05_Security_Compliance_Audit_Spec.md`): 21 CFR Part 11 compliance matrix, encryption standards, and security vulnerability exclusion ledger.
- **[QA Validation Plan](SDLC/06_QA_Validation_Plan.md)** (`06_QA_Validation_Plan.md`): GAMP 5 validation strategy, testing tiers, and qualification execution standards.
- **[IQ/OQ/PQ Qualification Execution Report](SDLC/IQ_OQ_PQ_Execution_Report.md)** (`IQ_OQ_PQ_Execution_Report.md`): Automated qualification build outputs and release state signatures.
- **[Feature and Environment Matrix](FEATURE_MATRIX.md)** (`FEATURE_MATRIX.md`): Maps compliance features to designer/execution services and inventories persistence engines and auditing mechanisms.
- **[Data Lifecycle Guide](DATA_LIFECYCLE.md)** (`DATA_LIFECYCLE.md`): Specifies the electronic Trial Master File (eTMF) Quality Control review lifecycle and data transitions from draft to archive.

---

## Developer Guide: Issue-to-Doc Synchronization Workflow

When working on a new GitHub issue or modifying system functionality, follow this 3-tier cascade to ensure documentation remains in sync:

1. **Requirement Level (`PRD` / `SRS`)**:
   - Check if the change introduces new user capabilities or modifies existing specifications.
   - If so, update `docs/SDLC/01_Product_Requirements_Document_PRD.md` or `docs/SRS.md` with a unique requirement ID (`PRD-SYS-xxx` or `Trace-x`).
2. **Architecture & Decision Level (`ADR`)**:
   - If the change introduces new dependencies, database model changes, shared package utilities, or gateway routes, create a new ADR using the CLI tool:
     ```bash
     uv run python scripts/create_adr.py --title "Your Architectural Decision Title" --domain "core-platform" --req "PRD-SYS-001"
     ```
3. **Traceability Level (`RTM`)**:
   - Write automated pytest or vitest unit/integration tests referencing the requirement ID or feature.
   - Run the unified synchronization script `uv run python scripts/sync_gxp.py` to refresh the Requirements Traceability Matrix.

#### Strict Fail-Fast GxP Validation & Draft Mode

To guarantee regulatory data integrity (21 CFR Part 11) and prevent the silent or accidental generation of unverified "PASSED" results, the platform enforces strict validation rules during compliance document generation:

- **Strict Default Fail-Fast**: If the required JUnit test execution report (`report.xml`) is missing or empty, `generate_rtm.py` terminates immediately with exit code 1. No files are updated or committed under this state.
- **Visual Draft Bypass**: For development, fast PR checking, or dry-run validation layouts where running full test suites is skipped, pass the `--draft` CLI flag or set the `RTM_DRAFT=true` environment variable.
- **Draft Document Manifestation**: Under draft mode, document generation completes successfully but prepends a prominent visual warning banner (`> ⚠️ **DRAFT ONLY — UNVERIFIED GxP COMPLIANCE DOCUMENT** ⚠️`) to the absolute top of `Requirements_Traceability_Matrix.md` and `IQ_OQ_PQ_Execution_Report.md`. Additionally, unverified requirements display as `⚠️ **Unverified**` with missing test statuses explicitly set to `⚪ (UNVERIFIED)` and durations strictly mapped to `N/A`.
