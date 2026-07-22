# Local Development Sandbox Guide

Welcome to the fully containerized local development sandbox for Cadence Clinical. This guide details how to launch, use, and validate the local environment without installing dependencies on your host machine.

## Prerequisites
- Docker Engine installed (or Docker Desktop/OrbStack).
- Docker Compose v2 (usually included with Docker).

## 1. Running the Sandbox

To launch all backend applications (Gateway, Designer, Execution) and databases (PostgreSQL, Neo4j) in isolated virtual networks, run a single command from the root of the repository:

```bash
docker compose -f docker/docker-compose.yml up -d --build
```

### Hot Reloading
The source directories (`apps/`, `packages/`, `tests/`, etc.) are mounted into the containers as volumes. The FastAPI services are running with `uvicorn --reload`. This means **any code changes you make on your local host will immediately synchronize and reload the running services**.

### Services & Ports
- **Gateway API**: [http://localhost:8000](http://localhost:8000)
- **Designer API**: [http://localhost:8001](http://localhost:8001)
- **Execution API**: [http://localhost:8002](http://localhost:8002)
- **PostgreSQL**: `localhost:5432` (User: `cadence` / Password: `cadence_password`)
- **Neo4j**: `localhost:7474` (UI) and `localhost:7687` (Bolt) (User: `neo4j` / Password: `cadence_password`)

## 2. Validation & Verification

Before submitting code, you must ensure all styling, linting, and tests pass successfully inside the containerized sandbox against the live containerized database.

We have provided a verification helper script:

```bash
./run-checks.sh
```

### What does `run-checks.sh` do?
1. **Formatting Checks**: Runs `black --check` on the codebase.
2. **Linting Checks**: Runs `ruff check` on the codebase.
3. **Unit & Integration Tests**: Runs `pytest`. The test suite is configured to target the live containerized PostgreSQL database (`postgres:5432`), guaranteeing that tests assert correctness under a production-like network environment rather than relying on an in-memory SQLite database.

### Manual Executions
If you wish to run a specific command (e.g., auto-formatting code), you can execute it inside the `execution` container environment (where all `uv` dependencies are resolved):

```bash
# Auto-format your code using Black and Ruff
docker compose -f docker/docker-compose.yml exec execution black .
docker compose -f docker/docker-compose.yml exec execution ruff check --fix .

# Run a specific test file
docker compose -f docker/docker-compose.yml exec execution pytest tests/test_audit.py
```

## 3. Dependency Management

The project uses `uv` as the package manager and dependency installer. Dependencies are automatically installed into a virtual environment (`/opt/.venv`) within the containers during the build process via `uv sync --all-extras`. **You do not need to install Python or packages on your host.**

If you add new dependencies to `pyproject.toml`, you must rebuild the sandbox containers to pull in the new packages:

```bash
docker compose -f docker/docker-compose.yml up -d --build
```
