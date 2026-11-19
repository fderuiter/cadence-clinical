# Cadence Clinical Research Software

> An operating system for clinical research: one platform for the whole lifecycle of developing
> evidence about an intervention, from protocol design to submission.

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![C# 14](https://img.shields.io/badge/C%23-14-239120.svg)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![CDISC USDM](https://img.shields.io/badge/CDISC-USDM%20v3%20%7C%20v4-darkblue.svg)](https://www.cdisc.org/standards/data-exchange/usdm)
[![License: Proprietary](https://img.shields.io/badge/License-Proprietary-red.svg)](LICENSE)

> [!IMPORTANT]
> **Pre-release.** Cadence is in active initial development and is not validated for use on
> real clinical trials. Local containers, Keycloak realms and seed data are synthetic and for
> evaluation only. For pilots or commercial enquiries see [SUPPORT.md](SUPPORT.md).

---

## What Cadence is

Most eClinical stacks are a bundle of products (EDC, CTMS, eTMF, safety, a conformance validator),
each owning its own copy of the study, the site and the participant. Cadence inverts that: a single
**clinical kernel** holds the body of clinical truth, and every product is an application built on it.

- **Six engines** over one kernel: Knowledge, Execution, Evidence, Trust, Quality and Intelligence.
- **Founding invariant (K-TRUTH-1):** no product owns clinical truth, and no representation
  (SDTM, ADaM, FHIR, the protocol PDF) is clinical truth. They are projections.
- **Executable protocol:** the protocol is a versioned model (CDISC USDM); the PDF is a rendering.
- **Standards are native:** CDISC/FDA/PMDA conformance rules are versioned data evaluated
  continuously, from design time onward.
- **Authority is computed from evidence** (training, delegation, qualification), not from role strings.

The normative design is the
[Clinical Research OS kernel specification v0.2](docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md),
with its decisions recorded in ADR-2244 to ADR-2255 (see the [ADR index](docs/adr/index.md)).

## Where the code is today

The platform was ported from Python to **.NET 10 / C# 14** (September 2026) and is now being
re-founded on the kernel. Both of these are true at once:

| Today (running code)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                | Target (kernel spec §2, §12)                                                                                                                                                                                                    |
| --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 17 ASP.NET Core 10 product services and the kernel API host under `apps/`, each with its own schema, behind a YARP gateway                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          | One modular monolith: kernel + engines in a single PostgreSQL schema, products as application modules ([ADR-2244](docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md))                                        |
| `apps/execution` (EDC) is the most complete service                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 | Execution is the first product brought onto the kernel ([reference workflow](docs/PROJECT_PLAN.md#delivery-sequence))                                                                                           |
| `packages/kernel` holds the kernel projects with typed IDs, `IClock`, forced row-level security, bitemporal tables, the command pipeline with a per-tenant audit chain and outbox relay, the Concept Registry, the Standards Registry with its governed library, attested protocol versions holding a typed protocol design document whose elements are lineage nodes, with bitemporal effectivity, study-site activation and an expected schedule computed from the design, public `NodeRef`s with kernel-computed zoom paths, one `Explanation` type for every decision, declared command capabilities with dry-run authorization, tenant-scoped search, participants with a bitemporal enrollment lifecycle, rule packs evaluated through `IRuleEvaluator` with validation runs and findings, Study Pulse indicators, evidence artifacts, grants, roles and delegations as bitemporal authority with grant-backed visibility, qualifications that gate capabilities on current training, capability-keyed work queues, and an IdentityVault that seals direct identifiers behind audited reveals | Kernel phases 0–5 in [spec §15](docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md#15-roadmap), tracked in epic [#4719](https://github.com/fderuiter/cadence-clinical/issues/4719)                                                    |
| Neo4j is gone from the code, the CLI, the setup scripts and the dev container                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       | Neo4j retired; protocol versions stored in PostgreSQL ([ADR-2245](docs/adr/2026-09-29-native-postgresql-protocolversion-snapshots-and-neo4j-retirement.md), [#4790](https://github.com/fderuiter/cadence-clinical/issues/4790)) |
| Some `packages/` still contain Python reference sources for untested or missing ports                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               | Listed in [docs/PYTHON_REFERENCE_INVENTORY.md](docs/PYTHON_REFERENCE_INVENTORY.md) and deleted as each port is verified ([#4789](https://github.com/fderuiter/cadence-clinical/issues/4789))                                    |

[ARCHITECTURE.md](ARCHITECTURE.md) describes both views in detail.

## Repository layout

| Path                                | Contents                                                                                                              |
| ----------------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| `apps/<service>/`                   | ASP.NET Core 10 services (`src/Cadence.Apps.*.csproj`); each has a README                                             |
| `apps/gateway/`                     | YARP gateway: validates the OIDC token and forwards signed caller identity                                            |
| `apps/apphost/`                     | .NET Aspire AppHost for local orchestration                                                                           |
| `apps/web/`, `apps/subject-portal/` | Vue 3 web client and participant PWA                                                                                  |
| `packages/kernel/`                  | Clinical kernel projects                                                                                              |
| `packages/*`                        | Shared libraries: security, database, telemetry, templating, contracts, DDF compiler, de-identification, testing, CLI |
| `tests/`                            | `Cadence.UnitTests`, `Cadence.IntegrationTests`, `Cadence.ArchitectureTests`, `Cadence.E2ETests` (one folder each)    |
| `docs/`                             | Specifications, ADRs, SDLC/GxP documents ([index](docs/DOCUMENTATION_INDEX.md))                                       |
| `docker/`                           | Compose stack, Dockerfile, Keycloak realm                                                                             |

## Quickstart

**Prerequisites:** .NET 10 SDK (pinned in [`global.json`](global.json)), Node.js 22.13+ with corepack enabled (`corepack enable`; pnpm comes from the `packageManager` pin in `package.json`), Docker.
A new machine can run `make wizard` (or `bash scripts/setup_wizard.sh`) to install and check all of these.

```bash
git clone https://github.com/fderuiter/cadence-clinical.git
cd cadence-clinical

dotnet build Cadence.sln                        # warnings are errors
dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj      # unit tests
dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj
pnpm install                                    # frontend workspace
```

### Running the services

Every service refuses to start without `GATEWAY_SECRET` (at least 32 characters), and the gateway
refuses to start without `OIDC_AUTHORITY`
([ADR-2256](docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md)).
Every non-health request must come through the gateway with a Keycloak token, so the web client
has to sign in; its token-less demo mode can no longer reach the API.

The compose stack sets local values for both and boots PostgreSQL, Keycloak, the gateway and every
service unprofiled by default. Each service applies its own migrations on start:

```bash
docker compose -f docker/docker-compose.yml up -d --build
```

Optional storage services (MinIO and ClamAV) require `--profile storage`:

```bash
docker compose -f docker/docker-compose.yml --profile storage up -d --build
```

Each service exposes `/healthz/live` (process up) and `/healthz/ready` (database reachable).
Ports, tokens and other ways to run the platform are in
[docs/LOCAL_DEV_ENVIRONMENT.md](docs/LOCAL_DEV_ENVIRONMENT.md), and every setting in
[docs/ENVIRONMENT_VARIABLES.md](docs/ENVIRONMENT_VARIABLES.md).

## Developer CLI

The Cadence CLI lives in `packages/cli`. It is not installed as a `dotnet` tool, so run it through
pnpm or `dotnet run`:

```bash
pnpm cadence <command>
# same as: dotnet run --project packages/cli/src/Cadence.Cli.csproj -- <command>
```

| Task                                          | Command                                         |
| --------------------------------------------- | ----------------------------------------------- |
| All pre-commit quality gates                  | `pnpm cadence check`                            |
| One gate (e.g. `adrs`, `markdown`, `readmes`) | `pnpm cadence check <gate>`                     |
| Auto-fix formatting, schemas, ADR index       | `pnpm cadence fix`                              |
| Environment diagnostics                       | `pnpm cadence doctor --auto-fix`                |
| Validate requirement tags and test results    | `pnpm cadence gxp sync` (writes a preview only) |
| New ADR                                       | `pnpm cadence scaffold adr "<title>"`           |

The full, generated command reference is [docs/cli.md](docs/cli.md).

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) for the workflow, and [AGENTS.md](AGENTS.md) for coding
rules (they apply to humans and AI agents alike). Pull requests target `main`. Every PR that changes
behaviour updates the docs it affects in the same PR.

## Documentation

Start at the [documentation index](docs/DOCUMENTATION_INDEX.md). Key entry points:

- [Kernel specification v0.2](docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) and its [roadmap](docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md#15-roadmap)
- [Architecture](ARCHITECTURE.md) and [domain glossary](CONTEXT.md)
- [Architecture decision records](docs/adr/index.md)
- [SDLC and GxP validation documents](docs/SDLC/SDLC_Documentation_Suite.md)
- [Security policy](SECURITY.md) and [support](SUPPORT.md)

## Project direction

Cadence is developed and integrated in this repository as a modular monolith. The [project plan](docs/PROJECT_PLAN.md) covers module ownership, engineering practices, and the sequence from a synthetic study to a verified database lock. [Goals and priorities](docs/GOALS_AND_PRIORITIES.md) lists the outcomes to work toward next. [ADR-2603](docs/adr/2026-11-19-keep-cadence-in-one-repository.md) records the source layout decision.

- [Proposed commercial product catalog](docs/strategy/PRODUCT_CATALOG.md)
- [Market validation hypotheses](docs/strategy/MARKET_HYPOTHESES.md)

## License

Proprietary. All rights reserved. See [LICENSE](LICENSE).
