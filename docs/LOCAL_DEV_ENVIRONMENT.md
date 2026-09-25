# Local Sandbox Development Environment Guide

Welcome to the Cadence Clinical Platform. This guide walks you through the step-by-step local configuration of your developer environment, maps our local containerized microservice cluster, and details our unified validation workflows using the Cadence CLI (`uv run cadence`).

---

## 1. Host System Runtime & Dependency Prerequisites

To prevent local verification and testing failures, a newly onboarded developer machine must meet the following system dependencies. These instructions apply to **macOS**, **Linux**, and **Windows WSL2** environments.

### Step 1: Install Git & Package Managers

Ensure Git and Node.js (v20+ LTS) are installed on your host system:

- **macOS:** `brew install git node pnpm`
- **Linux (Ubuntu/Debian):** `sudo apt update && sudo apt install -y git nodejs npm && npm install -g pnpm`
- **Windows (WSL2):** `sudo apt update && sudo apt install -y git nodejs npm && npm install -g pnpm`

### Step 2: Install Python 3.14 & `uv`

Cadence Clinical runs on **Python 3.14+** managed via `uv`:

```bash
# Install uv package manager
curl -LsSf https://astral.sh/uv/install.sh | sh

# Install project dependencies
uv sync --all-extras
```

### Step 3: Run Diagnostic Auto-Healing (`cadence doctor --auto-fix`)

Verify system prerequisites, port availability, and automatically provision all local SQLite databases:

```bash
uv run cadence doctor --auto-fix
```

_This command checks Python runtime, Docker daemon, node/pnpm, and auto-initializes all local schema files (`econsent.db`, `eisf.db`, `interop.db`, `notifications.db`, `safety.db`, `tickets.db`, `payments.db`, `fileshare.db`)._

---

## 2. Monorepo Service Catalog & Port Allocations (8000–8019)

The Cadence Clinical platform orchestrates **20+ specialized microservices**, supporting discrete clinical trial domains:

| Service Name | Port | Sub-directory | Primary Database | Domain & Operational Description |
| :--- | :--- | :--- | :--- | :--- |
| **`gateway`** | `8000` | `apps/gateway/` | Stateless / JWT | Central Reverse Proxy with Keycloak OIDC authentication & HMAC signature injection |
| **`designer`** | `8001` | `apps/designer/` | Neo4j (`7687`) | MDR/SDR Protocol Designer & CDISC USDM v3/v4 study graph modeling |
| **`execution`** | `8002` | `apps/execution/` | PostgreSQL (`5432`) | EDC Runtime Engine, dynamic subject eCRFs, real-time edit checks, & Dataset-JSON |
| **`etmf`** | `8004` | `apps/etmf/` | PostgreSQL / SQLite | DIA TMF Reference Model v3.2.0 document classification & completeness tracking |
| **`ctms`** | `8005` | `apps/ctms/` | PostgreSQL / SQLite | Clinical Trial Management, CRA monitoring visit reports, & 6-facet site greenlight |
| **`quality`** | `8006` | `apps/quality/` | PostgreSQL / SQLite | Quality Assurance, CAPA workflows, CSM Benford forensics, & pHash image checks |
| **`interop`** | `8007` | `apps/interop/` | PostgreSQL / SQLite | SMART on FHIR USCDI EHR ingestion & Open mHealth biometric streaming |
| **`org`** | `8008` | `apps/org/` | PostgreSQL / SQLite | Organization Directory, clinical trial sites, staff credentials, & PI DOA logs |
| **`safety`** | `8009` | `apps/safety/` | PostgreSQL / SQLite | ICH E2B(R3) ICSR safety XML compilation, CTCAE grading, & AS2 S/MIME EDI gateway |
| **`notifications`** | `8010` | `apps/notifications/` | PostgreSQL / SQLite | Multi-channel GxP alerts (SMTP Email, SMS, Webhooks, In-App inbox) |
| **`knowledge`** | `8011` | `apps/knowledge/` | PostgreSQL / SQLite | Clinical SOP hub, contextual help drawer routing, & versioned guidance |
| **`tickets`** | `8012` | `apps/tickets/` | PostgreSQL / SQLite | Clinical query escalation, data discrepancy tracking, & dual-target SLA timers |
| **`ai_gateway`** | `8013` | `apps/ai_gateway/` | Ephemeral / REST | Model tiering (Tier 1–3), PII/PHI de-id air-gap, & 21 CFR Part 11 AI draft ledgers |
| **`payments`** | `8014` | `apps/payments/` | PostgreSQL / SQLite | Investigator grant budgets, procedure fees, multi-payee splits, & participant wallet |
| **`imaging`** | `8015` | `apps/imaging/` | PostgreSQL / SQLite | Zero-footprint WebGL MPR DICOM viewer & blinded dual-reader RECIST 1.1 adjudication |
| **`eisf`** | `8016` | `apps/eisf/` | PostgreSQL / SQLite | Electronic Investigator Site Files (eISF) site binders & PI certification manifests |
| **`econsent`** | `8017` | `apps/econsent/` | PostgreSQL / SQLite | Digital Informed Consent, amendment re-consent gating, & cryptographic sealing |
| **`fileshare`** | `8018` | `apps/fileshare/` | MinIO (`9000`) + SQL | StoragePort S3 abstraction, presigned transfer URLs, & Part 11 document envelopes |
| **`compliance`** | `8019` | `apps/compliance/` | .NET 10 xUnit v3 + Audit Logs | Automated qualification testing, IQ/OQ/PQ assertions, & GxP sync engine |
| **`web`** | `5173` | `apps/web/` | Vite SPA Client | Vue 3 Unified Clinical Workspace (Multi-Persona Switcher) |
| **`subject-portal`** | `5174` | `apps/subject-portal/` | IndexedDB PWA | Offline-First Participant-Facing eCOA/ePRO Progressive Web App |

---

## 3. Primary CLI Workflows (`cadence`)

The repository features the unified **Cadence CLI** (`uv run cadence`) designed to streamline developer orchestration:

### 1. Interactive Dev TUI Cockpit (`cadence dev --tui`)

Runs and monitors all backend microservices in a Rich terminal cockpit with live health monitoring and hotkey restarts:

```bash
uv run cadence dev --tui
```

- `[r]`: Restart all services
- `[g]`: Hot-restart Gateway API (`8000`)
- `[e]`: Hot-restart Execution Engine (`8002`)
- `[d]`: Hot-restart Designer MDR (`8001`)
- `[c]`: Hot-restart CTMS (`8005`)
- `[q]`: Gracefully exit and terminate processes

### 2. Multi-Engine Scenario Seeding (`cadence db seed --tier full`)

Seeds realistic clinical test protocols, randomized subjects, eCRFs, deviations, and TMF documents across Neo4j, PostgreSQL, and SQLite:

```bash
uv run cadence db seed --tier full
```

### 3. Smart Test Watcher (`cadence test --watch`)

Monitors source files across `apps/`, `packages/`, and `tests/` and re-executes tests upon save:

```bash
uv run cadence test --watch
```

For ultra-fast iteration bypassing coverage calculations:

```bash
uv run cadence test --fast
```

---

## 4. Quality Gates & Sentinels (`cadence check --parallel`)

Before committing changes or opening a pull request, run all 10 architecture sentinels concurrently:

```bash
uv run cadence check --parallel
```

### Automated Remediation (`cadence fix --all`)

Auto-remediates linting issues, formats code, auto-indexes ADRs, and regenerates API documentation:

```bash
uv run cadence fix --all
```

---

## 5. GxP Compliance Synchronization (`cadence gxp sync`)

Whenever test functions are added, modified, or requirement tags (`@req:PRD-SYS-XXX`) are updated, run:

```bash
uv run cadence gxp sync
```

This executes the test suite, generates the updated Requirements Traceability Matrix (`docs/SDLC/Requirements_Traceability_Matrix.md`), and stages the compliance documents for Git commit.
