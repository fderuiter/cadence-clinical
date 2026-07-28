# Local Sandbox Development Environment Guide

Welcome to the Cadence Clinical Platform. This guide walks you through the step-by-step local configuration of your developer environment, maps our local containerized microservice cluster, and details our unified validation workflows.

---

## 1. Host System Runtime & Dependency Prerequisites

To prevent local verification and testing failures, a newly onboarded developer machine must meet the following system dependencies. These instructions apply to **macOS**, **Linux**, and **Windows WSL2** environments.

### Step 1: Install Git & Pre-commit
Ensure Git is installed on your host system:
* **macOS:** `brew install git`
* **Linux (Ubuntu/Debian):** `sudo apt update && sudo apt install -y git`
* **Windows (WSL2):** `sudo apt update && sudo apt install -y git`

Once Git is installed, install `pre-commit` locally to handle our pre-commit hook triggers:
```bash
# We will initialize pre-commit in Step 4 once Python/uv is set up
```

### Step 2: Install Node.js (LTS) & pnpm
The frontend portals and monorepo workspace configurations depend on Node.js and `pnpm`.
1. Install Node.js LTS (v20+ recommended) using your favorite package manager (e.g., `nvm` or Homebrew).
2. Install `pnpm` globally:
   ```bash
   npm install -g pnpm
   ```
3. Install the workspace dependencies from the root directory to set up local workspace linkages (`apps/web` referencing `packages/ui`):
   ```bash
   pnpm install
   ```

### Step 3: Install Python 3.11+ & uv Package Manager
Our backend systems are written in Python and utilize `uv` for ultra-fast package and tool management.
1. Install Python 3.11 or higher on your host.
2. Install the `uv` Package Manager:
   * **macOS/Linux/WSL:**
     ```bash
     curl -LsSf https://astral.sh/uv/install.sh | sh
     ```
   * **Windows (PowerShell):**
     ```bash
     powershell -ExecutionPolicy Bypass -c "irm https://astral.sh/uv/install.ps1 | iex"
     ```
3. Sync and provision your local host's virtual environment with all Python development, security, testing, and formatting dependencies:
   ```bash
   uv sync --all-extras
   ```
   *This command installs critical dev tools (`ruff`, `pytest`, `pytest-asyncio`, `bandit`, `detect-secrets`, `playwright`) natively into your local virtual environment, ensuring the `pnpm check` checks execute flawlessly without import errors.*
4. Install browser binaries required by Playwright (used for layout and rendering verification tests):
   ```bash
   uv run playwright install
   ```

### Step 4: Install & Configure Git Pre-commit Hooks
Register the pre-commit configuration with Git to run automatic linting, formatting, and link validations before staging commits:
```bash
pre-commit install
```
You can run the checks manually on all files in the repository at any time:
```bash
uv run pre-commit run --all-files
```

### Step 5: Install Docker & Docker Compose v2
Orchestration of databases and service ports requires Docker:
1. Install [Docker Desktop](https://www.docker.com/products/docker-desktop/) or [OrbStack](https://orbstack.dev/).
2. Verify Docker Compose v2 is available on your path:
   ```bash
   docker compose version
   ```

---

## 2. Monorepo Service Catalog & Port Allocations

The local containerized cluster orchestrates **13 primary services** defined in `docker/docker-compose.yml`.

| Service Name | Port Mapping (Host:Container) | Sub-directory | Primary Database / Storage | Purpose & Operational Description |
| :--- | :--- | :--- | :--- | :--- |
| **`postgres`** | `5432:5432` | N/A (Docker volume `postgres_data`) | PostgreSQL | Global relational storage for active operational data, clinical subject records, and audit logs. |
| **`neo4j`** | `7474:7474` (HTTP)<br>`7687:7687` (Bolt) | N/A (Docker volume `neo4j_data`) | Neo4j Graph DB | Powering the Metadata Repository (MDR). Models CDISC USDM study design entities and path branching. |
| **`keycloak`** | `8080:8080` | `docker/` (config) | Relational (internal) | Identity and Access Management (IAM). Restores realm role definitions (e.g., Sponsor Admin, Auditor, CRA). |
| **`designer`** | `8001:8001` | `apps/designer/` | Connected to `neo4j` | Core Python service (FastAPI) responsible for clinical trial structure and CDISC schema definition. |
| **`execution`** | `8002:8002` | `apps/execution/` | Connected to `postgres` | Electronic Data Capture (EDC) engine overseeing trial workflows, subject progression, and database-level audits. |
| **`etmf`** | `8003:8003` | `apps/etmf/` | Shared workspace: `/app/tmf.db` (SQLite) | Electronic Trial Master File system managing GCP document structures, files, metadata taxonomy, and workflows. |
| **`ctms`** | `8007:8005` | `apps/ctms/` | Shared workspace: `/app/ctms.db` (SQLite) | Clinical Trial Management System tracking trial sites, CRA monitoring, and visit scheduling. |
| **`quality`** | `8005:8005` | `apps/quality/` | Shared workspace: `/app/quality.db` (SQLite) | Clinical quality, deviations, root-cause analyses, and CAPA logging. |
| **`interop`** | `8004:8004` | `apps/interop/` | Shared workspace: `/app/interop.db` (SQLite) | Interoperability gateway for integrations like external patient registries and mobile ePRO ingestion. |
| **`tickets`** | `8009:8009` | `apps/tickets/` | Shared workspace: `/app/tickets.db` (SQLite) | Communication and query tickets workflow between sites, monitors, and data managers. |
| **`notifications`** | `8006:8006` | `apps/notifications/` | Shared workspace: `/app/notifications.db` (SQLite) | Dispatches emails/alerts, maps notification templates, and provides webhook relays. |
| **`gateway`** | `8000:8000` | `apps/gateway/` | N/A | Central routing reverse-proxy exposing unified endpoint routing to individual backend APIs. |
| **`subject-portal`** | `5174:5174` | `apps/subject-portal/` | N/A | Patient-facing SPA (Vue/Node.js) for completing diaries, surveys, and reviewing profile metrics. |

---

## 3. Launching the Local Sandbox

To spin up the containerized database and backend stack, run the following command from the repository root:

```bash
docker compose -f docker/docker-compose.yml up -d --build
```

### DB Migrations & Database Initialization
On container startup, the automated database migration script (`apps/execution/database/migrate.py`) executes inside the execution service container to automatically build out relational structures, register GxP-protected write triggers, and seed Keycloak configurations without manual developer intervention.

### Hot Reloading
The workspace source files (`apps/`, `packages/`, `tests/`) are mounted directly into containers as volumes. All Python backends run under `uvicorn --reload`, meaning **any backend code adjustments made on your host will immediately hot-reload the sandbox containers**.

---

## 4. Running the Portals & Interfaces

Our environment features two web-based user interfaces:

### A. Patient-facing Subject Portal (`apps/subject-portal`)
* **Execution:** Executed automatically inside the containerized stack using Docker Compose.
* **Port:** Accessible on Port `5174` ([http://localhost:5174](http://localhost:5174)).
* **Purpose:** Provides a patient-centric UI optimized for diaries, surveys, and task queues.

### B. Administrative Web Client (`apps/web`)
* **Execution:** **NOT** containerized in Docker Compose. It must be launched natively on your host machine to allow developer-first styling, components caching, and active debugging.
* **Port:** Runs strictly on **Port 3000** (`strictPort: true` configured in `vite.config.js`).
* **Base Path:** `/cadence-clinical/`
* **Local Shared UI Component Resolution:** Imports elements from the local package `packages/ui` linked by the workspace. Ensure you run `pnpm install` in the root first.
* **Launch Commands:**
  From the repository root:
  ```bash
  pnpm --filter web dev
  ```
  Or change directories and launch directly:
  ```bash
  cd apps/web
  pnpm dev
  ```
* **Testing & Linting the Administrative UI:**
  ```bash
  pnpm --filter web test  # Launches Vitest unit/integration tests
  pnpm --filter web lint  # Validates UI code style with ESLint
  ```

---

## 5. Unified Local Quality Verification Command

Before creating a commit or opening a pull request, verify that all systems meet architectural drift, styling, and security boundaries.

Run our single parallelized workspace validation runner from the repository root:

```bash
pnpm check
```

This invokes `concurrently` to run **7 concurrent quality pipelines**:

1. **`pnpm format`**: Standardizes style across JavaScript, Vue templates (Prettier), and Python files (`uv run ruff format .`).
2. **`pnpm lint`**: Inspects frontend structures (ESLint) and Python syntaxes (`uv run ruff check .`).
3. **`pnpm test`**: Parallel execution of Vitest UI unit/component tests and Pytest Python backend checks (`uv run pytest`).
4. **`node scripts/check-links.js`**: Scans relative file/directory references in all markdown documentation files to ensure 100% valid relative references.
5. **`python3 scripts/validate_adrs.py`**: Verifies numbering consistency, structure, and detects architectural changes lacking required Architecture Decision Records.
6. **`python3 scripts/validate_markdown.py`**: A custom validation utility parsing markdown files to check CLI option patterns, Python AST function signature match validity, and Pydantic-based JSON blocks.
7. **`uv run bandit`**: Deep security scanner identifying common security hazards inside `apps/` and `packages/` utilizing configurations in `pyproject.toml`.

---

## 6. Manual & Granular Executions

If you wish to invoke tools individually on the host system:

```bash
# Apply automatic Python format and lint corrections
uv run ruff format .
uv run ruff check --fix .

# Execute python unit tests with coverage
uv run pytest tests/test_audit.py

# Run database schema or state resets
uv run python scripts/reset_db.py
```
