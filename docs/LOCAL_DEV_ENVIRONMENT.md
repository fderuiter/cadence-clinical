# Local Sandbox Development Environment Guide

Welcome to the Cadence Clinical Platform. This guide walks you through the step-by-step local configuration of your developer environment, maps our local containerized microservice cluster, and details our unified validation workflows using the native Cadence CLI (`dotnet cadence`).

---

## 1. Host System Runtime & Dependency Prerequisites

To prevent local verification and testing failures, a newly onboarded developer machine must meet the following system dependencies. These instructions apply to **macOS**, **Linux**, and **Windows WSL2** environments.

### Step 1: Install Git & Package Managers

Ensure Git and Node.js (v20+ LTS) are installed on your host system:

- **macOS:** `brew install git node pnpm`
- **Linux (Ubuntu/Debian):** `sudo apt update && sudo apt install -y git nodejs npm && npm install -g pnpm`
- **Windows (WSL2):** `sudo apt update && sudo apt install -y git nodejs npm && npm install -g pnpm`

### Step 2: Install .NET 10 SDK

Cadence Clinical runs natively on **.NET 10 (C# 14 / ASP.NET Core 10)**:

```bash
# Verify .NET 10 SDK
dotnet --version
# Output should indicate 10.0.xxx (pinned in global.json)

# Restore solution dependencies
dotnet restore Cadence.sln
```

### Step 3: Run Diagnostic Auto-Healing (`dotnet cadence doctor --auto-fix`)

Verify system prerequisites, port availability, and database connections:

```bash
dotnet cadence doctor --auto-fix
```

_This command checks the .NET runtime, Docker daemon and node/pnpm, and verifies connectivity to PostgreSQL (5432)._

---

## 2. Service Catalog & Ports

Each service's host port is the default the gateway routes to (`apps/gateway/src/Program.cs`), so the gateway
reaches services started with `dotnet run` on the host and the compose stack alike. Inside compose, every .NET
service listens on 8080 in its own container. Every service with a database uses the single `cadence` PostgreSQL
database, in the schema listed ([OPERATIONAL_DATABASE.md](OPERATIONAL_DATABASE.md)).

| Service | Host port | Directory | Storage | Domain |
| :--- | :--- | :--- | :--- | :--- |
| **front proxy** (nginx) | `8000` | `docker/nginx/` | none | Entry point for the web apps; forwards everything to the gateway |
| **`gateway`** | via `8000` | `apps/gateway/` | none (YARP) | Keycloak OIDC authentication and signed identity headers for downstream services |
| **`designer`** | `8001` | `apps/designer/` | none | Protocol designer and CDISC USDM authoring |
| **`execution`** | `8002` | `apps/execution/` | PostgreSQL, schema `edc` | EDC runtime: subjects, eCRFs, edit checks, queries |
| **`etmf`** | `8003` | `apps/etmf/` | PostgreSQL, `etmf` | TMF Reference Model document classification and completeness |
| **`interop`** | `8004` | `apps/interop/` | PostgreSQL, `interop` | FHIR and mHealth ingestion |
| **`quality`** | `8005` | `apps/quality/` | PostgreSQL, `quality` | CAPA workflows and data-integrity checks |
| **`notifications`** | `8006` | `apps/notifications/` | PostgreSQL, `notifications` | GxP alerts and in-app inbox |
| **`ctms`** | `8007` | `apps/ctms/` | PostgreSQL, `ctms` | Monitoring visits and site readiness |
| **`safety`** | `8008` | `apps/safety/` | PostgreSQL, `safety` | ICH E2B(R3) ICSRs and CTCAE grading |
| **`tickets`** | `8009` | `apps/tickets/` | PostgreSQL, `tickets` | Query escalation and SLA timers |
| **`eisf`** | `8010` | `apps/eisf/` | PostgreSQL, `eisf` | Investigator site files |
| **`econsent`** | `8011` | `apps/econsent/` | PostgreSQL, `econsent` | Electronic informed consent and re-consent |
| **`org`** | `8012` | `apps/org/` | PostgreSQL, `org` | Organizations, sites and staff |
| **`fileshare`** | `8013` | `apps/fileshare/` | PostgreSQL, `fileshare` | Document transfer envelopes |
| **`imaging`** | `8014` | `apps/imaging/` | PostgreSQL, `imaging` | DICOM viewing and blinded reads |
| **`knowledge`** | `8015` | `apps/knowledge/` | PostgreSQL, `knowledge` | SOP hub and contextual guidance |
| **`payments`** | `8016` | `apps/payments/` | PostgreSQL, `payments` | Investigator grants and participant payments |
| **`ai_gateway`** | `8017` | `apps/ai_gateway/` | none | Model tiering and PHI de-identification |
| **Keycloak** | `8080` | `docker/cadence-realm.json` | dev file store | OIDC provider; realm `cadence` is imported on start |
| **PostgreSQL** | `5432` | | volume `postgres_data` | Database `cadence` |
| **`web`** | `5173` | `apps/web/` | | Vue 3 clinical workspace (`pnpm --filter web dev`) |
| **`subject-portal`** | `5174` | `apps/subject-portal/` | IndexedDB | Participant PWA |

MinIO (`9000`, `9001`) and ClamAV (`3310`) are defined under the compose profile `storage`; no service reads them yet.

## 3. Running the Stack with Docker Compose

```bash
docker compose -f docker/docker-compose.yml up -d --build
docker compose -f docker/docker-compose.yml ps     # every .NET service should become "healthy"
```

- **Migrations:** each service applies its own migrations when it starts (`Database__MigrateOnStartup=true`) and
  reports healthy only once `/healthz/ready` answers, which requires the database.
- **Health probes:** `http://localhost:<port>/healthz/live` and `/healthz/ready` need no token. Every other endpoint
  requires one: sign in through the web app, or get a token from Keycloak at
  `http://localhost:8080/realms/cadence/protocol/openid-connect/token`.
- **Authentication settings:** the gateway validates tokens issued at `http://localhost:8080/realms/cadence`
  (`OIDC_AUTHORITY`) and fetches Keycloak's keys over the compose network (`OIDC_METADATA_ADDRESS`). All services
  share the local `GATEWAY_SECRET`. The compose file's secrets are for local use only.
- **Reset:** `docker compose -f docker/docker-compose.yml down -v` removes the database volume. Do this once if your
  database predates the schema-per-service layout (ADR-2447).

## 4. Primary CLI Workflows (`dotnet cadence`)

The repository features the native **Cadence CLI** (`dotnet cadence` or `dotnet run --project packages/cli/src/Cadence.Cli.csproj --`) designed to streamline developer orchestration:

### 1. Interactive Dev Cockpit (`dotnet cadence dev --tui`)

Runs and monitors all backend microservices in a terminal cockpit with live health monitoring and hotkey restarts:

```bash
dotnet cadence dev --tui
```

- `[r]`: Restart all services
- `[g]`: Hot-restart Gateway API (`8000`)
- `[e]`: Hot-restart Execution Engine (`8002`)
- `[d]`: Hot-restart Designer MDR (`8001`)
- `[c]`: Hot-restart CTMS (`8005`)
- `[q]`: Gracefully exit and terminate processes

### 2. Multi-Engine Scenario Seeding (`dotnet cadence db seed --tier full`)

Seeds realistic clinical test protocols, randomized subjects, eCRFs, deviations, and TMF documents across Neo4j, PostgreSQL, and MinIO:

```bash
dotnet cadence db seed --tier full
```

### 3. Smart Test Runner (`dotnet cadence test`)

Runs the full xUnit test suite across unit, architecture, and E2E suites:

```bash
dotnet cadence test
```

For ultra-fast iteration executing against in-memory repository fakes:

```bash
dotnet cadence test --fast
```

---

## 5. Quality Gates & Sentinels (`dotnet cadence check`)

Before committing changes or opening a pull request, run all 21 architecture sentinels:

```bash
dotnet cadence check
```

### Automated Remediation (`dotnet cadence fix --all`)

Auto-remediates C# formatting (`dotnet format`), aligns schemas, and indexes ADR records:

```bash
dotnet cadence fix --all
```

---

## 6. GxP Compliance Synchronization (`dotnet cadence gxp sync`)

Whenever test classes are added, modified, or requirement tags (`@req:PRD-SYS-XXX` / `[GxPRequirement]`) are updated, run:

```bash
dotnet cadence gxp sync
```

This executes the test suite, generates the updated Requirements Traceability Matrix (`docs/SDLC/Requirements_Traceability_Matrix.md`), and stages the compliance documents for Git commit.
