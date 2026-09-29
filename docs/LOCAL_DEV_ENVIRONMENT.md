# Local Development Environment

How to set up a workstation, run the platform locally and check your work. For the workflow and pull
requests see [CONTRIBUTING.md](../CONTRIBUTING.md); for every setting see
[ENVIRONMENT_VARIABLES.md](ENVIRONMENT_VARIABLES.md).

---

## 1. Prerequisites

| Tool | Version | Notes |
| --- | --- | --- |
| .NET SDK | 10.0 (pinned in [`global.json`](../global.json)) | `dotnet --version` |
| Node.js + pnpm | Node 20+, pnpm from `package.json` | Frontend workspace and the `pnpm cadence` shortcut |
| Docker | Recent Docker Engine or Desktop | Compose stack and Testcontainers |
| PowerShell (optional) | 7+ | `scripts/manage_migrations.ps1` (a `.sh` variant exists) |

The fastest way to install and check all of these is the setup wizard:

```bash
make wizard                                  # bash scripts/setup_wizard.sh; Windows: scripts/setup_wizard.ps1
bash scripts/setup_wizard.sh --dev --minimal # no prompts, no Playwright browser download
```

It installs or validates the SDK and HTTPS dev certificate, runs `pnpm install`, sets
`git config core.hooksPath .githooks`, creates `.env` from `.env.example`, and finishes with
`cadence doctor`.

## 2. Build and test

```bash
dotnet restore Cadence.sln
dotnet build Cadence.sln                          # warnings are errors
dotnet test tests/Cadence.UnitTests.csproj
dotnet test tests/Cadence.ArchitectureTests.csproj
pnpm install
```

PostgreSQL integration tests are skipped unless you provide a server:

```bash
# an existing server
CADENCE_TEST_POSTGRES="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=cadence_test" \
  dotnet test tests/Cadence.UnitTests.csproj --filter "Category=Integration"

# or a throwaway container
CADENCE_TEST_DOCKER=1 dotnet test tests/Cadence.UnitTests.csproj --filter "Category=Integration"
```

## 3. Run the platform

Two things are required before any service starts (ADR-2256):

- `GATEWAY_SECRET`: at least 32 characters, the same value for the gateway and every service.
- `OIDC_AUTHORITY` for the gateway: the local Keycloak realm, `http://localhost:8080/realms/cadence`.

Every API request goes through the gateway (port 8000) with a Keycloak access token. Services reject
direct calls without a valid gateway signature. The web client must sign in; its token-less demo mode
cannot reach the API.

### Option A: Docker Compose (full stack)

```bash
cp .env.example .env            # set GATEWAY_SECRET
docker compose -f docker/docker-compose.yml up --build
```

This starts PostgreSQL, Keycloak (realm from `docker/cadence-realm.json`), MinIO, ClamAV, the gateway,
the services and the subject portal. Neo4j is still defined but no .NET code uses it (#4790).

### Option B: .NET Aspire AppHost

```bash
dotnet run --project apps/apphost/src/Cadence.AppHost.csproj
```

The AppHost starts PostgreSQL (database `cadence`, with pgAdmin), MinIO, Keycloak and the gateway,
execution, designer, ctms, etmf, quality and fileshare services, and sets
`OTEL_EXPORTER_OTLP_ENDPOINT` so telemetry shows in the Aspire dashboard. Supply `GATEWAY_SECRET` and
`OIDC_AUTHORITY` in your environment first.

### Option C: one service

```bash
export GATEWAY_SECRET="<32+ characters>"
export ASPNETCORE_ENVIRONMENT=Development      # allows the local database fallback
dotnet run --project apps/execution/src/Cadence.Apps.Execution.csproj
curl http://localhost:<port>/healthz/ready
```

### Web clients

```bash
pnpm --filter web dev                           # http://localhost:5173
pnpm --filter subject-portal dev                # http://localhost:5174
```

Set `VITE_API_BASE_URL` and the `VITE_KEYCLOAK_*` variables, or `VITE_ENABLE_MSW=true` to work against
mocked responses without a backend.

## 4. Services and ports

| Service | Port | Service | Port |
| --- | --- | --- | --- |
| gateway | 8000 | eisf | 8010 |
| designer | 8001 | econsent | 8011 |
| execution | 8002 | org | 8012 |
| etmf | 8003 | fileshare | 8013 |
| interop | 8004 | imaging | 8014 |
| quality | 8005 | knowledge | 8015 |
| notifications | 8006 | payments | 8016 |
| ctms | 8007 | ai_gateway | 8017 |
| safety | 8008 | Keycloak | 8080 |
| tickets | 8009 | web / subject portal | 5173 / 5174 |

What each service does and how mature it is: [ARCHITECTURE.md §3](../ARCHITECTURE.md#3-running-services-today).
Check for port clashes with `pnpm cadence check-ports`.

Each service answers `/healthz/live` and `/healthz/ready`.

## 5. Database tasks

| Task | Command |
| --- | --- |
| Add a migration | `pwsh scripts/manage_migrations.ps1 add <service> <MigrationName>` |
| Idempotent SQL script for review | `pwsh scripts/manage_migrations.ps1 script <service>` |
| Reset local databases | `pnpm cadence db reset` |
| Seed local databases | `pnpm cadence db seed` |
| Database status | `pnpm cadence db status` |

## 6. Quality gates and GxP sync

```bash
dotnet format Cadence.sln --verify-no-changes
pnpm cadence check               # all pre-commit gates; `pnpm cadence check <gate>` for one
pnpm cadence fix                 # auto-fix formatting, schemas, ADR index
pnpm cadence gxp sync            # after changing requirement-tagged tests
```

## 7. Documentation site

```bash
pnpm docs:dev                    # VitePress dev server over docs/
pnpm docs:build                  # full build: gates, generated API/schema docs, RTM draft, static site
```

The build copies the root `README.md`, `ARCHITECTURE.md` and `AGENTS.md` into `docs/` temporarily;
those copies are not committed.
