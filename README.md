# Cadence Clinical

> **The Metadata-Driven Clinical Execution Platform.**
> *Unifying Clinical Study Design (MDR/SDR) and Electronic Data Capture (EDC) into a single, automated digital data flow.*

[![CI](https://github.com/fderuiter/cadence-clinical/actions/workflows/ci.yml/badge.svg)](https://github.com/fderuiter/cadence-clinical/actions/workflows/ci.yml)

---

## ⚠️ Work in Progress / Active Development Status

**Active Development:** This platform is currently a work in progress and is undergoing rapid structural evolution. The platform scope, specifications, API endpoints, and integration interfaces are actively evolving. Various modules in the layout are either under active pre-production development or planned in the backlog. Please refer to the [Feature & Compatibility Matrix](docs/FEATURE_MATRIX.md) and the live issue tracker for current capability mappings and strategic roadmap milestones.

---

## 🚀 Overview

**Cadence Clinical** is a next-generation, open-source eClinical platform designed to eliminate manual study builds, expensive handoffs, and data silos in clinical research. By integrating the concepts of an upstream Clinical Metadata Repository (MDR/SDR) with a downstream Electronic Data Capture (EDC) engine, Cadence Clinical automates the digital data flow to turn static, narrative protocol documents into executable, machine-readable digital trials.

The core architecture synthesizes two complementary clinical trial paradigms:
1. **MDR/Designer (Neo4j)**: Upstream study design, CDISC Unified Study Definitions Model (USDM), and graph-based metadata modeling based on references like `openstudybuilder-ref`.
2. **EDC/Execution (PostgreSQL)**: Downstream EDC execution, subject enrollment state machines, eCRF form rendering (OpenRosa/Enketo XForms), clinical query workflows, and GxP-compliant audit trails based on references like `openclinica-ref`.

These core execution paths are bridged seamlessly via CDISC USDM data transfers and automated transform pipelines. The scope of Cadence Clinical has expanded to provide unified, compliant domain coverage across the entire clinical trial lifecycle, including:
- **Safety / Pharmacovigilance (PV)**: Processing E2B(R3) Individual Case Safety Reports (ICSR).
- **eConsent**: Secure, Part 11 compliant digital informed consent execution.
- **eISF (electronic Investigator Site File)**: Site-specific binder-scoped document filing and completeness tracking.
- **Notifications**: Automated, multi-channel patient compliance alerts (Email, SMS, Webhook, In-App).
- **Organization Directory**: Standardized directory mapping organizations, sites, personnel, and delegations of authority.
- **Quality & CAPA Management**: Rigid tracking of protocol deviations, root cause analyses, and corrective and preventive actions.

Every mutation across the entire platform enforces strict, GxP-compliant field-level validations, Part 11 electronic signatures, and immutable, chronologically-bound audit ledgers.

---

## 🏛️ System Architecture

Cadence Clinical is organized as a reverse-proxy fronted microservices topology utilizing a unified identity layer and a dual-persistence database layout.

```text
                               ┌────────────────────────────────────────────────┐
                               │             Subject Portal (PWA)               │
                               │                (port 5174)                     │
                               └──────────────────────┬─────────────────────────┘
                                                      │ (OIDC / OAuth 2.0 Auth)
                                                      ▼
 ┌────────────────────────┐    ┌────────────────────────────────────────────────┐
 │    Web Client SPA      │───►│            API Gateway & Auth Proxy            │
 │   (Vue 3 Sandbox)      │    │                (Keycloak OIDC)                 │
 └────────────────────────┘    └──────────────────────┬─────────────────────────┘
                                                      │ (Signature & Context Headers)
                                ┌─────────────────────┼─────────────────────────┐
                                │                     │                         │
                                ▼                     ▼                         ▼
                      ┌──────────────────┐  ┌──────────────────┐      ┌──────────────────┐
                      │  Designer App    │  │  Execution App   │      │  Domain Services │
                      │  (MDR / USDM)    │  │  (EDC & eCRFs)   │      │ (eTMF, CTMS, etc)│
                      └─────────┬────────┘  └─────────┬────────┘      └─────────┬────────┘
                                │                     │ (USDM ➔ ODM)            │
                                ▼                     ▼                         ▼
                      ┌──────────────────┐  ┌──────────────────┐      ┌──────────────────┐
                      │  Neo4j Graph DB  │  │  PostgreSQL DB   │      │ SQLite/Postgres  │
                      │  (Study Designs) │  │  (Trial Data)    │      │  (Domain Tables) │
                      └──────────────────┘  └──────────────────┘      └──────────────────┘
```

### Identity and Access Control (RBAC)
Authentication and Authorization are centralized at the API Gateway (`apps/gateway/`) using **Keycloak OpenID Connect (OIDC)**. Incoming JWT tokens are parsed to extract user roles, site scopes, and unblinded access attributes. The gateway strips incoming client-side claims headers and propagates securely signed gateway headers (`X-User-Id`, `X-User-Roles`, etc.) signed with a shared HMAC-SHA256 signature version 2 format to downstream services.

### Notifications Service Integration & Deployment Configurations
The multi-channel **Notifications Service** (`apps/notifications`) is fully integrated into the API Gateway and Docker Compose network.
Requests to `/notifications/` and `/api/v1/notifications/` are securely routed through the central gateway, which enforces identity verification, rate limiting, and HMAC-SHA256 signature verification.

For deployment, the Notifications Service depends on several environment variables that can be configured in your deployment settings:
- **`NOTIFICATIONS_DATABASE_URL`**: Relational database connection string (defaults to a local SQLite database `/app/notifications.db` or standard PostgreSQL URL).
- **`SMTP_HOST`**: Host address for the SMTP server (defaults to `smtp.mailhog.local` in development).
- **`SMTP_PORT`**: Port number for SMTP transmission (defaults to `1025`).
- **`SMTP_USERNAME`**: SMTP server username for authentication (non-secret development placeholder is `dev_user`).
- **`SMTP_PASSWORD`**: SMTP server password for authentication (non-secret development placeholder is `dev_password`).
- **`SMTP_USE_TLS`**: Enforce TLS protocol (defaults to `false` for development).
- **`SMTP_USE_SSL`**: Enforce SSL protocol (defaults to `false` for development).
- **`SMTP_SENDER`**: Originating email address (defaults to `no-reply@cadenceclinical.com`).
- **`WEBHOOK_URL`**: Target endpoint for outbound event webhooks (defaults to `http://webhooks.local/receiver`).
- **`WEBHOOK_SIGNING_SECRET`**: HMAC secret used to canonically sign outgoing webhook payloads to ensure integrity (defaults to `dev_webhook_secret_key_12345`).
- **`WEBHOOK_TIMEOUT`**: Timeout duration in seconds for dispatching a webhook request (defaults to `5.0`).
- **`NOTIFICATION_MAX_ATTEMPTS`**: Maximum retry attempts for failed email/webhook notification deliveries (defaults to `5`).

- **Role-Based Access Control (RBAC)** restricts operational paths. For example, Subjects are limited exclusively to ePRO submission endpoints, while only Quality Oversight roles can close or cancel CAPA workflows.
- **Gateway Step-Up Re-Authentication** is enforced on signature-gated mutations (e.g., PI batch sign-offs, subject randomization). A short-lived (60-second) `sig_token` must be requested with re-supplied password credentials and TOTP to satisfy 21 CFR Part 11 electronic signature mandates.

### Data Transformation Flow
The pipeline automates clinical workflows through three major translation stages:
1. **Design Formulation**: Protocol definitions, arms, epochs, visits, and procedures are authored in the Designer service and stored as version-chained Neo4j graph schemas mapped to the CDISC USDM standard.
2. **Delivery & Compilation**: Upon study publication, the USDM metadata is fetched by the Execution engine's transformation pipeline which compiles clinical concepts into CDISC ODM XML files and interactive OpenRosa/Enketo-compliant XForm structures.
3. **Runtime Execution**: Ingested layouts automatically configure electronic Case Report Forms (eCRFs) in PostgreSQL, defining the data entry rules, real-time edit-checks, and subject compliance matrices. Any structural anomalies or offline-sync reconciliation failures automatically trigger clinical queries to prevent silent data loss.

For further architectural specifications, cryptographic boundaries, and compliance traceability, refer to [ARCHITECTURE.md](ARCHITECTURE.md).

---

## 📁 Repo Layout & Component Index

This monorepo leverages `pnpm` workspace management for the frontend and `uv` workspace environments for the backend. The following table directories compile all services, packages, and operational directories.

### Platform Components & Status

| Path | Component Title / Standard Reference | Description / Purpose | Status |
| :--- | :--- | :--- | :--- |
| **`apps/designer`** | Cadence Clinical - Designer (MDR/SDR) | Study design, metadata authoring, and CDISC USDM graph modeling. Persisted in Neo4j. | **Supported** |
| **`apps/execution`** | Cadence Clinical - EDC Execution Engine | Downstream EDC runtime managing subjects, eCRF schedules, real-time edit checks, medical coding, lab ranges, and SDTM/ADaM exports. | **Supported** |
| **`apps/gateway`** | Cadence Clinical - API Gateway | Gateway-fronted microservices proxy. Handles OIDC token checks, step-up re-auth tokens, rate limiting, and OpenAPI docs aggregation. | **Supported** |
| **`apps/etmf`** | Cadence Clinical - Event-Driven eTMF Module | Ingests, classifies, and tracks clinical document archives against DIA TMF Reference Model v3.2.0. Enforces data-driven expected document lists. | **Supported** |
| **`apps/ctms`** | Cadence Clinical - CTMS | Tracks trial/site milestones, monitor visits, budget allocations, and CRA workloads. Fully audited append-only ledger. | **Supported** |
| **`apps/quality`** | Cadence Clinical - Quality & CAPA | Manages protocol deviations, root cause analysis (RCA) attachments, and corrective/preventive action (CAPA) workflow transitions. | **Supported** |
| **`apps/interop`** | Cadence Clinical - FHIR / eSource & eCOA Sync Gateway | Processes FHIR bundles and reconciles bulk offline ePRO submissions with durable conflict resolution and clinical query automation. | **Supported** |
| **`apps/notifications`** | Cadence Clinical - Notifications Service | Dynamic multi-channel reminder dispatcher supporting SMS, Email, Webhooks, and In-App notifications. | **Supported** |
| **`apps/safety`** | Cadence Clinical - Safety & Pharmacovigilance Gateway | Implements E2B(R3) Individual Case Safety Report (ICSR) XML compilation and rendering, with immutable audits. | **Supported** |
| **`apps/org`** | Cadence Clinical - Organization Directory | Directory for Organizations, Sites, Personnel, and Delegations of Authority. *No business logic routes are currently routed; exposes health check only.* | **In Progress** |
| **`apps/econsent`** | Cadence Clinical - eConsent | Template compilation, digital signatures, and consent audit ledgers. *Not yet routed through the central gateway proxy.* | **In Progress** |
| **`apps/eisf`** | Cadence Clinical - eISF Service | Site-scoped investigator site files and binder section browser. *Not yet routed through the central gateway proxy.* | **In Progress** |
| **`apps/web`** | Vue 3 SPA Sandbox & Legacy Engine | Primary frontend SPA with Keycloak authentication. *Features a legacy vanilla-JS layout parser (index.js) coexisting during migration to Vue 3 (src/App.vue) per ADR-052.* | **In Progress** |
| **`apps/subject-portal`** | Offline-First eCOA/ePRO PWA | Mobile-optimized Progressive Web App (PWA) running on port 5174. Includes IndexedDB offline queues and sync exception panels. | **Supported** |
| **`packages/security`** | Cryptography & Security Library | Shared libraries for HMAC signature generation, security context variables, and Keycloak auth validation helpers. | **Supported** |
| **`packages/ui`** | Shared UI & Signing Library | UI component framework and browser signing helpers. Exported via pnpm. | **Supported** |
| **`packages/core-models`** | Standardized Domain Models | Cross-service Pydantic payload models (TMF taxonomy, SDTM, Org, Part 11 GxP audit fields). | **Supported** |
| **`packages/database`** | Database Connection Manager | SQLAlchemy async relational session management. | **Supported** |
| **`packages/deid`** | PII/PHI Redaction Engine | High-performance de-identification and masking engine running inside eTMF and Interop. | **Supported** |
| **`docs/`** | System Documentation | Narrative specs, regulatory compliance guides, ADR registries, and feature mappings. | **Supported** |
| **`scripts/`** | Operational Scripts | Internal scripts for CI, link checks, ADR schema validations, and database migrations. | **Supported** |
| **`verification/`** | Verification & Integration Reports | Playwright automation, regression logs, and system validation reports. | **Supported** |

### Known Integration Gaps & Technical Debt
During the current active development phase, several known discrepancies exist within the repository:
1. **Gateway Proxy Exclusions:** The `org`, `econsent`, and `eisf` services operate independently on their local database backends but are not yet registered in the gateway `SERVICES` map inside `apps/gateway/main.py`. This means direct API requests to these routes bypass central authorization proxies.
2. **Organization Service Routing:** The Organization Directory service (`apps/org`) maintains a complete relational GxP database schema (with Organizations, Sites, Staff, DoA), but its `main.py` currently exposes only a standard `/health` check without operational CRUD endpoints.
3. **Web Frontend Migration:** Per **ADR-052**, `apps/web` is undergoing an active migration. Standard web client operations rely on a modern Vue 3 SPA architecture (`src/App.vue`), but legacy vanilla-JS layout and sign-off rendering engines (`index.js`) still coexist inside the workspace, occasionally leading to mixed validation patterns.

---

## 🛠️ Stack and Tooling

Cadence Clinical is built using high-performance, compliance-ready frameworks:

- **Backend Framework**: Python 3.11+ using FastAPI and Pydantic v2.
- **Relational Databases**: PostgreSQL (production EDC, auditing, security, and ledger systems) and SQLite (used for rapid test isolation across CTMS, eTMF, Quality, and Interop).
- **Graph Metadata Engine**: Neo4j (tracks version-chained protocol arms, epochs, visits, and biomedical concepts).
- **Frontend / Client Engines**: Vite, Vue 3, Pinia state stores, and vanilla JavaScript components.
- **Reporting & PDF Compilation**: WeasyPrint, Python-Docx, and Jinja2 templates.
- **Standards & Form Formats**: CDISC USDM (v3.0/v4.0), CDASH, CDISC ODM, ICH M11, OpenRosa/Enketo XForms, CDISC Dataset-JSON (SDTM / ADaM).
- **Identity & SSO Provider**: Keycloak OIDC.

---

## 🚀 Quickstart

### 1. Launch All Containerized Dependencies
The entire database and authentication ecosystem runs inside Docker containers. Start the full infrastructure sandbox (Neo4j, PostgreSQL, and Keycloak) in a single command:
```bash
docker compose -f docker/docker-compose.yml up -d --build
```
For credentials, port configurations, and detailed key mappings, refer to the [Local Development Environment Guide](docs/LOCAL_DEV_ENVIRONMENT.md).

### 2. Synchronize Python Backend Workspace
Initialize a unified Python environment and lock all python package dependencies across all monorepo apps and packages:
```bash
uv sync --all-extras
```

### 3. Install Frontend Dependencies
Configure frontend packages using workspace-aware Node scripts:
```bash
pnpm install
```

---

## 💻 Contributor Commands

Contributors must adhere to clean development patterns and verify formatting and testing locally prior to committing changes.

### Python Backend Workspace
```bash
# Run the entire backend test suite
uv run pytest --no-cov

# Automatically check and format python code styles
uv run ruff check . --fix
uv run ruff format .
```

### Frontend JS/TS Workspace
```bash
# Execute lint, formatting, and unit tests across all web apps and packages
pnpm -r lint
pnpm -r format
pnpm -r test

### Useful Developer Commands
```bash
# Export OpenAPI schemas for contract validation
uv run python scripts/validate_schemas.py --export-dir docs/openapi

# Regenerate and stage GxP RTM compliance docs
uv run python scripts/sync_gxp.py
```

---

## ⚙️ CI/CD Enforcement & Quality Gates

The repository implements strict continuous integration pipelines governed by `.github/workflows/ci.yml`. Every Pull Request triggers automated quality gates:

1. **Unit and Integration Testing**: Executes the `pytest` suite against a live Postgres test container.
2. **Linting and Style Standardization**: Checks and enforces PEP-compliant code via `ruff` checks and formatters.
3. **Static Application Security Testing (SAST)**: Scans python code for common security vulnerabilities using `bandit`.
4. **Credential Leak Prevention**: Runs `detect-secrets` against a strict pre-commit baseline.
5. **Frontend Quality Control**: Performs lints, formatting checks, and vitest assertions inside `pnpm`.
6. **Regulatory Document Assurance**: Validates that Markdown links are unbroken, and executes schema checks on all Architectural Decision Records (ADRs).

---

## 📚 Documentation

Detailed structural specifications, compliance designs, and operational instructions are archived directly in the source tree:

- **[ARCHITECTURE.md](ARCHITECTURE.md)**: Details the full service-by-service designs, signature parameters, and data flows.
- **[AGENTS.md](AGENTS.md)**: Defines the AI developer rules of engagement, code guidelines, and sandbox assumptions.
- **[SRS (Software Requirements Specification)](docs/SRS.md)**: The authoritative compliance document linking 21 CFR Part 11 and GxP requirements to implementation rules.
- **[Feature Matrix](docs/FEATURE_MATRIX.md)**: Lists active, in-progress, and planned clinical features mapped against sub-systems.
- **[Local Development Guide](docs/LOCAL_DEV_ENVIRONMENT.md)**: The reference book for keycloak setups, database schemas, and integration ports.
- **[Architectural Decision Records (ADRs)](docs/adr/index.md)**: Comprehensive registry of architectural choices and justifications.

---

## 🗺️ Roadmap & Status (TBD)

We track our live feature backlog and feature requests via the live [GitHub Issues](https://github.com/fderuiter/cadence-clinical/issues) and milestones pages.

Strategic backlog priorities and technical debt resolutions currently planned (TBD) include:
- [ ] Route the Organization Directory (`apps/org`), eConsent (`apps/econsent`), and eISF (`apps/eisf`) microservices through the API Gateway `SERVICES` mapping.
- [ ] Implement full operational CRUD routes for Site personnel and Delegations of Authority within `apps/org`.
- [ ] Complete the migration of legacy vanilla-JS layout scripts (`apps/web/src/lib/legacy_helpers.js`) into native Vue 3 components (`src/views/`).
- [ ] Establish automated E2B(R3) validator checks for suspect drug ingredient alignments using WHODrug hierarchies.
- [ ] Integrate automatic document QC status transitions within the eTMF indexing module.

---

## 📄 License

Cadence Clinical is licensed under the **ISC License**. See the [LICENSE](LICENSE) file for the full terms and conditions.
