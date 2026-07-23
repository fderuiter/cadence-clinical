# Local Development Sandbox Guide

Welcome to the fully containerized local development sandbox for the Cadence Clinical Platform. This guide is intended to help developers navigate the local development environment and understand the core architectural constraints.

## Architecture & Decisions

Before making significant changes or starting a new feature, please review the historical design choices and the context behind them.

All past architectural decisions are documented in our **[Architectural Decisions Index](adr/index.md)**.

When introducing new architectural changes (e.g., adding a new dependency, modifying data models, or adding a new service), you are required to submit an Architecture Decision Record (ADR) following the mandatory format. A template is provided in the `docs/adr/` directory.

## Prerequisites
- Docker Engine installed (or Docker Desktop/OrbStack).
- Docker Compose v2 (usually included with Docker).
- Python 3.11+ installed (if you wish to install dependencies locally).

## 1. Running the Sandbox

To launch all backend applications (Gateway, Designer, Execution, eTMF), databases (PostgreSQL, Neo4j), and Keycloak identity server in isolated virtual networks, run a single command from the root of the repository:

```bash
docker compose -f docker/docker-compose.yml up -d --build
```

On container startup, the automatic database migration runner (`apps/execution/database/migrate.py`) runs inside the execution service container to set up the database schema and native GxP write-protection and mutation capture triggers automatically, eliminating any manual setup steps.

### Hot Reloading
The source directories (`apps/`, `packages/`, `tests/`, etc.) are mounted into the containers as volumes. The FastAPI services are running with `uvicorn --reload`. This means **any code changes you make on your local host will immediately synchronize and reload the running services**.

### Services & Ports
- **Gateway API**: [http://localhost:8000](http://localhost:8000)
- **Designer API**: [http://localhost:8001](http://localhost:8001)
- **Execution API**: [http://localhost:8002](http://localhost:8002)
- **PostgreSQL**: `localhost:5432` (User: `cadence` / Password: `cadence_password`) <!-- pragma: allowlist secret -->
- **Neo4j**: `localhost:7474` (UI) and `localhost:7687` (Bolt) (User: `neo4j` / Password: `cadence_password`) <!-- pragma: allowlist secret -->
- **eTMF API**: [http://localhost:8003](http://localhost:8003)
- **Keycloak Identity Server**: [http://localhost:8080](http://localhost:8080) (Admin User: `admin` / Password: `admin_password`) <!-- pragma: allowlist secret -->
  - A pre-configured Keycloak realm (`cadence`) is automatically imported on startup, featuring default roles: `Sponsor Admin`, `CRA`, `Data Manager`, `Site Investigator`, and `Auditor`.

## 2. Validation & Verification

Before submitting code, you must ensure all styling, linting, and tests pass successfully inside the containerized sandbox against the live containerized database. Review the API guidelines and Pydantic models in `packages/core-models/`.

We have provided a verification helper script:

```bash
./run-checks.sh
```

### What does `run-checks.sh` do?
1. **Formatting Checks**: Runs `ruff format --check` on the codebase.
2. **Linting Checks**: Runs `ruff check` on the codebase.
3. **Unit & Integration Tests**: Runs `pytest`. The test suite is configured to target the live containerized PostgreSQL database (`postgres:5432`), guaranteeing that tests assert correctness under a production-like network environment rather than relying on an in-memory SQLite database.

### Manual Executions
If you wish to run a specific command (e.g., auto-formatting code), you can execute it inside the `execution` container environment (where all `uv` dependencies are resolved):

```bash
# Auto-format your code using Ruff
docker compose -f docker/docker-compose.yml exec execution ruff format .
docker compose -f docker/docker-compose.yml exec execution ruff check --fix .

# Run a specific test file
docker compose -f docker/docker-compose.yml exec execution pytest tests/test_audit.py
```

## 3. Dependency Management

The project uses `uv` as the package manager and dependency installer in the sandbox. Dependencies are automatically installed into a virtual environment (`/opt/.venv`) within the containers during the build process via `uv sync --all-extras`. **You do not need to install Python or packages on your host.**

If you prefer to install locally outside the container, you can use `uv sync --all-extras`.

If you add new dependencies to `pyproject.toml`, you must rebuild the sandbox containers to pull in the new packages:

```bash
docker compose -f docker/docker-compose.yml up -d --build
```

For detailed submission guidelines, please refer to the main repository documentation.
