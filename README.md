# Cadence Clinical

> **The Metadata-Driven Clinical Execution Platform.** > *Unifying Clinical Study Design (MDR/SDR) and Electronic Data Capture (EDC) into a single, automated digital data flow.*

---

## 🚀 Overview

**Cadence Clinical** is a next-generation, open eClinical platform built to eliminate manual study builds, handoffs, and data silos in clinical research. By integrating the concepts of upstream Clinical Metadata Repositories (MDR) with downstream Electronic Data Capture (EDC) engines, Cadence Clinical turns static protocols into executable, machine-readable digital data workflows.

Cadence Clinical synthesizes domain paradigms extracted from two open-source reference implementations:
1. **`openstudybuilder-ref`**: Upstream study design, CDISC Unified Study Definitions Model (USDM), and graph-based metadata modeling.
2. **`openclinica-ref`**: Downstream EDC execution, subject enrollment state machines, eCRF form rendering (OpenRosa/Enketo XForms), and audit trails.

---

## 🏛️ System Architecture

Cadence Clinical operates as a modular, API-first monorepo designed around clean bounded contexts:

```text
               ┌─────────────────────────────────────────┐
               │          Unified Web Frontend           │
               └────────────────────┬────────────────────┘
                                    │
                                    ▼
               ┌─────────────────────────────────────────┐
               │        API Gateway & Auth (OIDC)        │
               └─────────┬─────────────────────┬─────────┘
                         │                     │
          ┌──────────────┴─────────┐         ┌─┴──────────────────────┐
          │                        │         │                        │
          ▼                        ▼         ▼                        ▼
┌──────────────────┐                         ┌──────────────────┐
│  Designer App    │────────────────────────►│  Execution App   │
│  (MDR / USDM)    │  (DDF Study Published)  │  (EDC & eCRFs)   │
└─────────┬────────┘                         └─────────┬────────┘
          │                                            │
          ▼                                            ▼
┌──────────────────┐                         ┌──────────────────┐
│ Neo4j Graph DB   │                         │ PostgreSQL DB    │
│ (Study Metadata) │                         │ (Trial Data)     │
└──────────────────┘                         └──────────────────┘

```
### Repo Layout
```text
cadence-clinical/
├── apps/
│   ├── designer/         # Study Design & Metadata Authoring Service (FastAPI / Neo4j, CDISC USDM standard representation)
│   ├── execution/        # EDC Execution Service (eCRF, Subjects, Queries, PostgreSQL, USDM ↔ ODM transformation engine)
│   └── gateway/          # Central API Gateway & JWT Auth Middleware
├── packages/
│   ├── security/         # Cryptographic context listeners, auth token validation layers
│   └── ui/               # Standard clinical UI elements & design library
├── docker/               # Orchestration blueprints (PostgreSQL, Neo4j, Keycloak)
├── tests/                # Cross-service integration & transformation suites
├── AGENTS.md             # AI Agent guidelines & workspace constraints
├── ARCHITECTURE.md       # In-depth architectural specification
├── pyproject.toml        # Workspace dependencies (Python 3.11+)
└── README.md
```
### Stack and Tooling

Backend Framework: Python 3.11+ using FastAPI and Pydantic v2.
Primary Databases: * Neo4j (Graph database for protocol nodes, visits, arms, and biomedical concepts).
PostgreSQL (Relational database for clinical subject records, form submissions, and GxP audit logs).
Identity & SSO: Keycloak using OpenID Connect (OIDC) / OAuth 2.0.
Standards Compliance: CDISC USDM (v3.0/v4.0), CDASH, CDISC ODM, ICH M11, 21 CFR Part 11 / Annex 11.

### Quickstart (Local Development)
#### Prerequisites
Docker & Docker Compose
Python 3.11+
uv package manager

#### Launch Local Dependencies
Start Neo4j, PostgreSQL, and Keycloak:
```bash
docker compose -f docker/docker-compose.yml up -d
```
#### Install Workspace Dependencies
```bash
uv sync --all-extras
```
#### Run Services
```bash
# Run API Gateway
uv run uvicorn apps.gateway.main:app --reload --port 8000

# Run Designer Service
uv run uvicorn apps.designer.main:app --reload --port 8001

# Run EDC Execution Service
uv run uvicorn apps.execution.main:app --reload --port 8002
```
