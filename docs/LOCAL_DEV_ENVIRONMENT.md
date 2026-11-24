# Local Development Environment

How to set up a workstation, run the platform locally and check your work. For the workflow and pull
requests see [CONTRIBUTING.md](../CONTRIBUTING.md); for every setting see
[ENVIRONMENT_VARIABLES.md](ENVIRONMENT_VARIABLES.md).

---

## 1. Prerequisites

| Tool                  | Version                                                                                                                                      | Notes                                                    |
| --------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------- |
| .NET SDK              | 10.0 (pinned in [`global.json`](../global.json))                                                                                             | `dotnet --version`                                       |
| Node.js + pnpm        | Node 22.13+ on the 22.x line or Node 24+ as specified in [`package.json`](../package.json). Enable Corepack to use the `packageManager` pin. | Frontend workspace and the `pnpm cadence` shortcut       |
| Docker                | Recent Docker Engine or Desktop                                                                                                              | Compose stack and Testcontainers                         |
| PowerShell (optional) | 7+                                                                                                                                           | `scripts/manage_migrations.ps1` (a `.sh` variant exists) |

If Corepack is missing from your Node.js installation, install it with `npm install --global corepack`,
then run `corepack enable`. Use the setup wizard to prepare the workspace:

```bash
make wizard                                  # interactive: bash scripts/setup_wizard.sh; Windows: scripts/setup_wizard.ps1
make setup                                   # non-interactive: bash scripts/setup_wizard.sh --dev --minimal
```

It installs or validates the SDK and HTTPS dev certificate, runs `pnpm install --frozen-lockfile`,
provisions `.cadence/venv` (`detect-secrets`) and `.cadence/bin` (`actionlint`, `hadolint`, `shellcheck`),
sets `git config core.hooksPath .githooks`, creates `.env` from `.env.example`, and finishes with
`cadence doctor`.

## 2. Build and test

```bash
dotnet restore Cadence.sln
dotnet build Cadence.sln                          # warnings are errors
dotnet test --project tests/Cadence.UnitTests/Cadence.UnitTests.csproj
dotnet test --project tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj
pnpm install --frozen-lockfile                    # apps resolve packages/ui directly from source
pnpm test                                         # workspace Vitest suites + VitePress navigation tests
make verify                                       # pnpm cadence check + pnpm test + pnpm cadence test
```

The [PostgreSQL integration suite](TESTING.md#tests-that-need-postgresql) uses PostgreSQL 18 through
an existing server or Testcontainers:

```bash
# an existing server
CADENCE_TEST_POSTGRES="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=cadence_test" \
  pnpm cadence test --integration

# or a throwaway container
CADENCE_TEST_DOCKER=1 pnpm cadence test --integration
```

## 3. Run the platform

Two things are required before any service starts (ADR-2256):

- `GATEWAY_SECRET`: at least 32 characters, the same value for the gateway and every service.
- `OIDC_AUTHORITY` for the gateway: the local Keycloak realm, `http://localhost:8080/realms/cadence`.

Every API request goes through the gateway (port 8000) with a Keycloak access token. Services reject
direct calls without a valid gateway signature. Standard web builds must sign in through Keycloak; synthetic
demo mode (`--mode demo` / `VITE_DEMO_MODE=true`) runs offline in the browser via Mock Service Worker and
cannot reach the live API. If you only want to evaluate workflows without starting PostgreSQL, Keycloak, or
the .NET services, open the hosted [staff demo](https://fderuiter.github.io/cadence-clinical/demo/) and
[participant portal demo](https://fderuiter.github.io/cadence-clinical/subject-portal/) from the
[GitHub Pages portal](https://fderuiter.github.io/cadence-clinical/) ([ADR-2550](adr/2026-11-20-co-host-interactive-demos-on-github-pages-subpaths.md)).

### Option A: Docker Compose (full stack)

```bash
docker compose -f docker/docker-compose.yml up -d --build
docker compose -f docker/docker-compose.yml ps     # every .NET service should become "healthy"
```

This starts PostgreSQL (one `cadence` database, a schema per service), Keycloak with the `cadence` realm
imported from `docker/cadence-realm.json`, the gateway behind an nginx front proxy on port 8000, the `web`
(`http://localhost:3000/cadence-clinical/`) and `subject-portal` (`http://localhost:5174/subject-portal/`) Vite dev
servers with isolated container `node_modules` volumes, and every .NET microservice unprofiled by default. The compose
file sets its own local `GATEWAY_SECRET` and OIDC settings, so no `.env` is needed; those values are for local use
only.

- **Migrations:** each service applies its own migrations when it starts (`Database__MigrateOnStartup=true`)
  and reports healthy only once `/healthz/ready` answers, which requires the database
  ([database runbook](OPERATIONAL_DATABASE.md)).
- **Tokens:** every endpoint except the health probes needs a Keycloak token. Sign in through the web
  app, or request one from `http://localhost:8080/realms/cadence/protocol/openid-connect/token`.
- **Optional storage:** MinIO (`9000`, `9001`) and ClamAV (`3310`) run only when passing
  `--profile storage` (for example, `docker compose -f docker/docker-compose.yml --profile storage up -d`); no primary core service requires them to boot.
- **SDK container:** `dotnet-dev` runs only when passing `--profile dotnet` and mounts isolated NuGet and MSBuild
  intermediate volumes so container restores and builds do not overwrite host `obj/` files.
- **Pinned images:** `docker/Dockerfile.dotnet` and the compose file pin every image to a version and a
  `sha256` digest, so builds are reproducible. Dependabot (`.github/dependabot.yml`) opens weekly pull
  requests to update them, together with NuGet, npm and GitHub Actions dependencies. When updating the .NET
  images by hand, move the SDK and the ASP.NET runtime to the same release and replace both digests.
- **Reset:** `docker compose -f docker/docker-compose.yml down -v` removes the database volume. Do this once
  if your database predates the schema-per-service layout (ADR-2447).

### Option B: .NET Aspire AppHost

```bash
dotnet run --project apps/apphost/src/Cadence.AppHost.csproj
```

The AppHost starts PostgreSQL (database `cadence`, with pgAdmin), MinIO, Keycloak (with the `cadence` realm
imported), all 18 .NET hosts (the gateway on 8000 and the 17 services on 8001–8016 and 8018), the web app
(`http://localhost:3000/cadence-clinical/`), and the subject portal (`http://localhost:5174/subject-portal/`) on the
same host ports as docker compose. It sets `OTEL_EXPORTER_OTLP_ENDPOINT` so telemetry shows in the Aspire dashboard. Run
`pnpm install --frozen-lockfile` and set its secret parameters with `dotnet user-secrets`
first; [apps/apphost](../apps/apphost/README.md) lists them.

For kernel and Canvas work, the `core` profile runs PostgreSQL, Keycloak, the 6 core backend hosts (`gateway`,
`execution`, `etmf`, `ctms`, `knowledge`, `kernel`) and the `web` app:

```bash
dotnet run --project apps/apphost/src/Cadence.AppHost.csproj -- --AppHost:Profile=core
```

`ConnectionStrings:cadence` and `AppHost:OidcAuthority` replace the PostgreSQL and Keycloak containers with ones you
already run, so the core profile also works without Docker, for example in a cloud dev container.
[apps/apphost](../apps/apphost/README.md#run-locally) has the full command.

`scripts/local_stack_nodocker.sh up` does all of that in a container with no Docker: it starts a throwaway
PostgreSQL 18 cluster on port 5433, Keycloak from its release tarball with the `cadence` realm, and the core profile
in tmux session `apphost` (`status` and `down` check and stop it). The kernel host provisions the `cadence-local`
tenant the realm's users sign in to (ADR-2542) and seeds the demo study `CADENCE-101` with grants for them (ADR-2543):
`crc.site101` sees `SITE-101` and its participants, `pi.site101` signs that site's forms, and `SITE-102` stays masked. Keycloak imports the realm only when
it has none, so reset Keycloak's data once if it imported the realm before the users had fixed ids. Agents drive the running stack with
[.claude/skills/run-cadence](../.claude/skills/run-cadence/SKILL.md).

### Option C: one service

Every database-backed service requires an explicit PostgreSQL connection string (`ConnectionStrings__cadence` or
`POSTGRES_CONNECTION_STRING`), `GATEWAY_SECRET`, and `AUDIT_LOG_SECRET_KEY` — no environment has a built-in database
fallback (ADR-2260). For `Cadence.Apps.Execution`, also supply two distinct 32-byte Base64 encryption keys
(`DemographicsEncryption__KeyBase64` and `RANDOMIZATION_ALLOCATION_KEY`):

```bash
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=cadence_password" # pragma: allowlist secret
export Database__MigrateOnStartup=true
export GATEWAY_SECRET="local-dev-only-gateway-secret-not-for-production" # pragma: allowlist secret
export AUDIT_LOG_SECRET_KEY="dev-audit-secret-key-change-me" # pragma: allowlist secret
export DemographicsEncryption__KeyBase64="$(openssl rand -base64 32)"
export RANDOMIZATION_ALLOCATION_KEY="$(openssl rand -base64 32)"
dotnet run --project apps/execution/src/Cadence.Apps.Execution.csproj --urls http://localhost:8002
curl -fsS http://localhost:8002/healthz/ready
```

### Web clients

```bash
pnpm --filter web dev                           # http://localhost:3000/cadence-clinical/
pnpm --filter subject-portal dev                # http://localhost:5174/subject-portal/
```

Both dev servers call their own origin, and their `/api` proxies forward to the gateway (`CADENCE_GATEWAY_URL`,
default `http://localhost:8000`); the gateway sends no CORS headers, so do not point `VITE_API_BASE_URL` at it
(#5429, #5451). Set the `VITE_KEYCLOAK_*` variables for another Keycloak, or `VITE_ENABLE_MSW=true` to work against mocked
responses without a backend.

## 4. Services and ports

The host ports are the defaults the gateway routes to (`apps/gateway/src/Program.cs`), so the gateway reaches
services started with `dotnet run` and the compose stack alike. Inside compose, every .NET service listens on
8080 in its own container.

<!-- dev-service-ports:begin (generated from packages/topology/src/ServiceTopology.cs by `pnpm cadence generate-cli-docs`; do not edit) -->

| Service                       | Host port | Database schema      | Service                                | Host port | Database schema                       |
| ----------------------------- | --------- | -------------------- | -------------------------------------- | --------- | ------------------------------------- |
| front proxy (nginx) → gateway | 8000      | none                 | eisf                                   | 8010      | `eisf`                                |
| designer                      | 8001      | none                 | econsent                               | 8011      | `econsent`                            |
| execution                     | 8002      | `edc`                | org                                    | 8012      | `org`                                 |
| etmf                          | 8003      | `etmf`               | fileshare                              | 8013      | `fileshare`                           |
| interop                       | 8004      | `interop`            | imaging                                | 8014      | `imaging`                             |
| quality                       | 8005      | `quality`            | knowledge                              | 8015      | `knowledge`                           |
| notifications                 | 8006      | `notifications`      | payments                               | 8016      | `payments`                            |
| ctms                          | 8007      | `ctms`               | kernel (`apps/kernel-api`)             | 8018      | `kernel`, `intelligence`, `execution` |
| safety                        | 8008      | `safety`             | Keycloak                               | 8080      | (own store)                           |
| tickets                       | 8009      | `tickets`            | web (`apps/web`)                       | 3000      | none                                  |
| PostgreSQL                    | 5432      | `cadence` (database) | subject-portal (`apps/subject-portal`) | 5174      | none                                  |

<!-- dev-service-ports:end -->

What each service does and how mature it is:
[ARCHITECTURE.md §3](../ARCHITECTURE.md#3-running-services-today). Check for port clashes with
`pnpm cadence check-ports`.

Each service answers `/healthz/live` and `/healthz/ready` without a token.

The kernel host also serves `/api/v1/ai`, the Intelligence module (ADR-2630). Its command-palette intent router needs no
model. Agent tasks call one only when a provider is configured: `Intelligence__Anthropic__ApiKey` (or
`ANTHROPIC_API_KEY`) for Anthropic, or `Intelligence__FakeProvider=true` for deterministic answers in Development; see
[ENVIRONMENT_VARIABLES.md](ENVIRONMENT_VARIABLES.md#intelligence-module).

## 5. Database tasks

| Task                             | Command                                                |
| -------------------------------- | ------------------------------------------------------ |
| Add a migration                  | `pnpm cadence migrations add <module> <MigrationName>` |
| Idempotent SQL script for review | `pnpm cadence migrations script <module\|all>`         |
| Verify migrations & snapshots    | `pnpm cadence migrations verify`                       |
| Reset local databases            | `pnpm cadence db reset`                                |

## 6. Quality gates and GxP sync

```bash
dotnet format Cadence.sln --verify-no-changes
pnpm cadence check               # all pre-commit gates; `pnpm cadence check <gate>` for one
pnpm cadence fix                 # auto-fix formatting, schemas, ADR index
pnpm cadence gxp sync            # after changing requirement-tagged tests; validates, never commit docs/SDLC output
```

## 7. Documentation site and hosted demos

```bash
pnpm docs:dev                    # VitePress dev server over docs/
pnpm docs:build                  # full build: gates, generated API/schema docs, RTM draft, static site
pnpm docs:stage-demos            # build and stage staff (/demo/) and participant (/subject-portal/) demos into docs/.vitepress/dist
pnpm portal:build                # build shared UI, VitePress docs, and both co-hosted subpath demos
```

`docs/index.md` is the authored guided landing page for the portal; VitePress stages the root `README.md`,
`ARCHITECTURE.md`, `AGENTS.md`, and `LICENSE` as site pages in `docs/`. `deploy-docs.yml` runs `pnpm docs:stage-demos`
before computing `SHA256SUMS` and SLSA provenance so the documentation portal and both interactive demos ship as one
attested Pages artifact. The documentation index tracks the authored pages.
