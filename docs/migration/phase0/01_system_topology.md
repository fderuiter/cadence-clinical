# 01. System Topology & Entry Point Audit

**Document ID:** PHASE0-NET10-01 (baseline dated 2026-09-03)
**Status:** Independently re-derived from source. Supersedes `docs/migration/SYSTEM_TOPOLOGY_AUDIT.md` for the items where the two disagree (see §7 "Reconciliation with prior discovery documents"); does not restate content that document already gets right.
**Method:** Every claim in this document was produced by direct `grep`/file-read evidence against the working tree on 2026-09-03, not by re-summarizing prior migration documents. File:line citations are given wherever a specific mechanism is described. Where a prior document (`docs/migration/Migration_Phase0_Readiness_Audit.md`, document AUD-NET10-002) had already found a gap, this document re-verifies the citation and states whether it is still accurate, rather than assuming it is.

---

## 0. Ground-truth repository census

```
find . -type f -name "*.py" -not -path "./.venv/*" -not -path "*/node_modules/*" -not -path "*/.git/*" -not -path "*/__pycache__/*" | wc -l
→ 1675
```

Every `.py` file in the repository resolves to exactly one of five top-level roots: `apps/` (22 subdirectories — 18 backend services + `web`, `subject-portal`, and 2 root files), `packages/` (18 subdirectories — shared kernel + tooling), `scripts/` (104 files, operational/CI tooling, not a bounded context), `tests/` (15 root-level integration/cross-cutting test files), `verification/` (1 file, a standalone HTML-verification utility, not part of any service). The full per-directory breakdown and the file-by-file accounting that proves 100% of these 1,675 files are classified is in [`05_orphan_audit_gate.md`](05_orphan_audit_gate.md) — this document only asserts the aggregate.

---

## 1. System Boundary Diagram

```mermaid
flowchart TB
    subgraph Edge
        Web[apps/web — Vue 3 SPA]
        SubjectPortal[apps/subject-portal — Vue 3 SPA]
        Gateway["apps/gateway :8000\nOIDC + HMAC reverse proxy"]
    end

    subgraph "Tier 2 — Core Entities"
        Execution["apps/execution :8002\nEDC / RTSM / Labs"]
        Designer["apps/designer :8001\nUSDM Protocol Authoring (Neo4j)"]
        Org["apps/org :8012\nDirectory / DOA"]
    end

    subgraph "Tier 3 — Orchestrators & Regulatory"
        Quality["apps/quality :8005"]
        Ctms["apps/ctms :8007"]
        Etmf["apps/etmf :8003"]
        Eisf["apps/eisf :8010"]
        Safety["apps/safety :8008"]
        Econsent["apps/econsent :8011"]
        Payments["apps/payments :8016"]
        Tickets["apps/tickets :8009"]
    end

    subgraph "Tier 4 — Edge / Sinks"
        Notifications["apps/notifications :8006"]
        Interop["apps/interop :8004"]
        AiGateway["apps/ai_gateway :8017"]
        Fileshare["apps/fileshare :8013"]
        Imaging["apps/imaging :8014"]
        Knowledge["apps/knowledge :8015"]
    end

    subgraph "External Systems"
        Keycloak[(Keycloak OIDC)]
        Neo4jDB[(Neo4j)]
        Postgres[(PostgreSQL x5 + shared cadence_edc)]
        SQLite[(SQLite x10, per-service files)]
        MinIO[(MinIO / S3)]
        ClamAV[(clamd daemon :3310)]
        NciEvs[(NCI EVS terminology API)]
        Rscript[("Rscript — NOT provisioned\nin any container, see §3.8")]

    end

    Web --> Gateway
    SubjectPortal --> Gateway
    Gateway -->|proxies all 17 backend services| Execution & Designer & Org & Quality & Ctms & Etmf & Eisf & Safety & Econsent & Payments & Tickets & Notifications & Interop & AiGateway & Fileshare & Imaging & Knowledge
    Gateway --> Keycloak

    Designer --> Neo4jDB
    Execution --> Postgres
    Org -->|shares cadence_edc with Execution — see §3.2.1| Postgres
    Etmf --> Postgres
    Ctms --> Postgres
    Quality --> Postgres
    Safety & Interop & Tickets & Notifications & Econsent & Eisf & Fileshare & Knowledge & Payments & Imaging --> SQLite

    Execution -->|EXECUTION_URL| Ctms
    Ctms -->|CTMS_SERVICE_URL non-canonical| Execution
    Execution -->|DESIGNER_URL| Designer
    Designer -->|EXECUTION_SERVICE_URL non-canonical| Execution
    Execution -->|ETMF_URL| Etmf
    Etmf -->|EXECUTION_URL| Execution
    Quality -->|EXECUTION_URL, sync HTTP| Execution
    Econsent -.->|Redis PUB/SUB, fire-and-forget| Execution

    Fileshare --> MinIO
    Etmf -.->|Minio if local/minio in URL, else S3| MinIO
    Fileshare --> ClamAV
    Designer --> NciEvs
    Interop -.->|sandboxed subprocess, R path is dead code| Rscript
```

---

## 2. Entry Point Inventory

### 2.1 HTTP entry points

All 17 Python backend services are **FastAPI** applications launched via `uvicorn apps.{service}.main:app`, dispatched centrally by `scripts/start.py:59-76`. **No gRPC, `Mount()`, `WSGIMiddleware`, or raw ASGI sub-app composition exists anywhere in `apps/`** (verified by repo-wide grep for `grpc|Mount\(|WSGIMiddleware|Starlette\(`). `apps/web` and `apps/subject-portal` are Vue 3 SPAs with no Python backend of their own.

Ports are centrally defined in `packages/cli/ports.py:20-84` and echoed in `docker/docker-compose.yml`.

| Service | `main.py` | Port | Router modules mounted |
|---|---|---:|---|
| gateway | `apps/gateway/main.py:108` | 8000 | cdisc, usdm, ecoa (`:118-120`) + a ~1,900-line reverse-proxy body forwarding to all 17 downstream services |
| designer | `apps/designer/main.py:189` | 8001 | synopsis, sentinel, cascade, export, comments, digitization, amendments, designer, startup_matrix, cohort_feasibility, tripartite (`:201-211`) |
| execution | `apps/execution/main.py:280` | 8002 | 24 routers (`:285-310`) — amendments, anomalies, anonymization, auditor, cec, devices, dictionaries, doa, documents, eisf, exports, forms, labs, locks, observations, offline, queries, randomization, rtsm, rules, safety, sdv, signatures, subjects, unblinding, visits |
| etmf | `apps/etmf/main.py:173` | 8003 | archive, inspection, intelligence, taxonomy, etmf (`:188-192`) |
| interop | `apps/interop/main.py:56` | 8004 | interop, lakehouse, cdr_pipeline, sce, sensors, dct, esource, devices (`:78-85`) |
| quality | `apps/quality/main.py:72` | 8005 | quality_router (`:223`) |
| notifications | `apps/notifications/main.py:41` | 8006 | notifications_router (`:64`) |
| ctms | `apps/ctms/main.py:30` | 8007 | doa, site_startup, deviations, rbqm, financials, ip_accountability, etmf_sync, ctms (`:45-52`) |
| safety | `apps/safety/main.py:61` | 8008 | safety_router (`:82`) |
| tickets | `apps/tickets/main.py:52` | 8009 | tickets_router (`:70`) |
| eisf | `apps/eisf/main.py:63` | 8010 | eisf_router (`:91`) |
| econsent | `apps/econsent/main.py:107` | 8011 | econsent, reconsent, withdrawal, export, granular, audit, readability (`:129-135`) |
| org | `apps/org/main.py:32` | 8012 | org_router (`:48`) |
| fileshare | `apps/fileshare/main.py:38` | 8013 | files, files_alias, audit, audit_alias, guest (`:56-60`) |
| imaging | `apps/imaging/main.py:26` | 8014 | imaging_router (`:42`) |
| knowledge | `apps/knowledge/main.py:37` | 8015 | articles, protocols, training (`:53-55`) |
| payments | `apps/payments/main.py:26` | 8016 | payments_router (`:42`) |
| ai_gateway | `apps/ai_gateway/main.py:36` | 8017 | inference, rag, reasoning (`:48-50`) |

**`imaging`, `knowledge`, `payments`, and `ai_gateway` are not present in `docker/docker-compose.yml`** — they exist as fully-formed FastAPI apps (referenced in `apps/gateway/main.py`'s `SERVICES` registry, `:481-486`), but are absent from the deployed local/dev stack. Treat them as real bounded contexts with an incomplete deployment topology, not as out-of-scope.

### 2.2 Asynchronous / background workers

**No Celery, Dramatiq, RQ, Kafka, RabbitMQ, or SQS usage exists anywhere in application code.** All background processing is custom in-process `asyncio` tasks (or one Redis-subscriber thread) started from each service's FastAPI `lifespan`.

| Service | Module | Mechanism | Purpose |
|---|---|---|---|
| execution | `workers/outbox_worker.py:218,227` | asyncio loop | Polls `IntegrationOutbox`, dispatches signed HTTP callbacks |
| execution | `workers/anomaly_worker.py:88-142` | asyncio task | Cross-domain eCRF anomaly detection |
| execution | `workers/consent_subscriber.py:128-187` | **background thread**, Redis Pub/Sub | Subscribes `econsent_consent_completed`, auto-provisions `ClinicalSubject` on consent completion — see §3.3 for the durability gap |
| execution | `database/sealer.py:318` | asyncio task | GxP record-sealing (uses `pg_try_advisory_xact_lock(42001)`) |
| execution | `queries_escalation.py:173` | asyncio task | Query-escalation SLA checker (`pg_try_advisory_xact_lock(42002)`) |
| etmf | `workers/outbox_worker.py:75,82` | asyncio task | Polls `IntegrationOutbox` for eTMF |
| etmf | `workers/document_intelligence_worker.py:147` | asyncio daemon | Document layout parsing / DIA classification queue |
| etmf | `sealer.py:295` (`adapters/sealer.py`) | asyncio task | TMF-document sealing |
| etmf | `expiration_scanner.py` | asyncio task | Document/certification expiration scan |
| fileshare | `workers/transcode_worker.py:336` | asyncio daemon | FFmpeg HLS/DASH transcode queue |
| fileshare | `workers/conversion_worker.py:234` | asyncio daemon | LibreOffice document→PDF conversion queue |
| notifications | `workers/notification_worker.py:365,381` | asyncio task | Dispatches `Notification`/`NotificationDelivery` rows |
| econsent | `workers/archival_worker.py:136,145` | asyncio task | Pushes signed consent records to eTMF |
| tickets | `escalation.py:274-296` | asyncio task | SLA escalation, poll interval `TICKETS_ESCALATION_POLL_INTERVAL_SECONDS` (default 60s) |
| designer | `workers/cohort_feasibility_worker.py:115` | request-triggered, Postgres advisory lock | Not a background loop — invoked from the cohort-feasibility router |

All lifespans respect a `RUN_BACKGROUND_WORKERS` flag (`apps/execution/main.py:203`) to disable workers under test.

### 2.3 Schedulers

**No APScheduler, `croniter`, or `schedule` usage anywhere in application code.** The only calendar-based scheduling in the repository is at the CI level: `.github/workflows/project-automation.yml:8-9`, `cron: "0 0 * * *"` (daily project-board/issue-format sync). All in-app "periodic" behavior is the polling loops in §2.2, not calendar-scheduled jobs.

### 2.4 CLI / management commands

The platform CLI is `cadence` (Typer), entry point `packages.cli.main:main` (`pyproject.toml:199-200`), with subcommand groups `doctor`, `dev`, `test`, `check`, `fix`, `db`, `scaffold`, `setup`, `record`, `gxp`, `cdisc`, `mcp` (a stdio JSON-RPC MCP server for AI agents, `packages/cli/main.py:39-44`).

Per-service DB migration entry points (standalone scripts, discovered dynamically by `scripts/start.py:15-25` before uvicorn boot): `apps/ctms/migrate.py`, `apps/fileshare/migrate.py`, `apps/execution/database/migrate.py`, `apps/etmf/database/migrate.py` **and** `apps/etmf/adapters/migrate.py` (two independent, near-duplicate Alembic-stamp/upgrade implementations for the same eTMF legacy path — see §3.8), `apps/eisf/database/migrate.py`, `apps/quality/migrate.py`. `apps/org` has **no** migration script (see §3.2.1).

`packages/deid/cli.py` is a separate de-identification CLI tool.

### 2.5 Operational scripts (`scripts/`, 104 files, not a bounded context)

Excluding `scripts/tests/` (53 pytest files covering the tooling below) and `scripts/hooks/` (4 Antigravity agent lifecycle hooks: `post_write_check.py`, `pre_invocation_reminder.py`, `pre_tool_guardrail.py`, `stop_sentinel.py`), the remaining 63 files fall into six categories:

- **Validation/compliance linters** (CI-gating): `validate_adrs.py`, `validate_architecture_drift.py`, `validate_codeowners.py`, `validate_dependencies.py`, `validate_imports.py`, `validate_markdown.py`, `validate_path_patterns.py`, `validate_schemas.py`, `validate_vulnerabilities.py`, `verify_contracts.py`, `verify_gxp_signatures.py`, `audit_security.py`, `compliance_utility.py`, `detect_duplication.py`, `check_ports.py`, `check_duplicate_identifiers.js`.
- **Sync scripts**: `sync_gxp.py`, `sync_github_project.py`, `sync_openapi_spec.py`, `sync_ruleset.py`.
- **Generators**: `generate_api_docs.py`, `generate_rtm.py`, `generate_schema_documentation.py`, `generate_schemas.py`, `regenerate_templates.py`, `create_adr.py`.
- **Migration/data scripts**: `migrate_etmf_blobs_to_s3.py`, `migrate_service_wizard.{sh,ps1}`, `decommission_service_wizard.{sh,ps1}`, `manage_migrations.{sh,ps1}`, `reset_db.py`, `clean_test_dbs.py`.
- **.NET migration tooling**: `scaffold_dotnet_solution.sh`, `setup_dotnet_env.{sh,ps1}`, `setup_dotnet_wizard.{sh,ps1}`.
- **Dev/orchestration, git tooling, CI automation**: `dev_orchestrator.py`, `start.py`, `scaffold_service.py`, `setup_credentials.sh`, `setup_gdrive_wizard.sh`, `ast_merge_driver.py`, `git_merge_driver.py`, `pre_commit.py`, `pre_commit_openapi.py`, `pre_commit_query_linter.py`, `clean_secrets_baseline.py`, `run_gating_pipeline.py`, `self_heal.py`, `post_pr_comment.py`, `remove_jules_labels.py`, `merge_junit.py`, `fix_query_style.py`, `runtime_guard.py`, `introspect_pg_schema.py`, `test_m4_challenger2_stress.py`, `build-docs.js`, `parse_frontend_ast.js`, `parse_js_blocks.js`, `conftest.py`.

None of these are transcribed to .NET — they are Python/CI developer tooling that is retired alongside the Python codebase it operates on, except where a direct .NET-side equivalent is needed (CI compliance gates, RTM generation) — see `03_migration_plan.md` §5.

### 2.6 Build & CI infrastructure

**Dockerfiles (19 total):** one per backend service under `apps/{service}/Dockerfile` (16 files — **`ai_gateway` and `imaging` have no Dockerfile**), plus `docker/Dockerfile` (shared hot-reload dev image), `docker/Dockerfile.dotnet` (.NET 10 SDK dev/build container, exposes 8000-8002), `docker/fileshare-transcoder/Dockerfile` (FFmpeg + LibreOffice-headless).

**`docker/docker-compose.yml` services:** `postgres` (5432, backs `cadence_edc` — shared by execution+org), `postgres-etmf` (5433), `postgres-ctms` (5434), `postgres-quality` (5435), `neo4j` (7474/7687), `keycloak` (8080), designer/execution/org/eisf/etmf/ctms/quality/interop/tickets/safety/notifications/econsent/fileshare (their documented ports), `clamav` (3310), `fileshare-transcoder`, `subject-portal` (5174), `front-proxy` (nginx), `gateway-rewrite` (Node/TS, port 3000 internal — a parallel, separate Gateway rewrite track, out of scope for this Python→.NET migration), `gateway` (8000), `minio` (9000/9001), `dotnet-dev` (.NET profile). **No Redis service is defined anywhere in compose** — see §3.3, this is not a cosmetic omission.

**GitHub Actions workflows:** `ci.yml` (Python lint/test, push/PR to `main`/`release/*`), `dotnet-ci.yml` (.NET 10 build/test/GxP-compliance gate, push/PR to `main`/`master`/`dotnet-migration`), `conflict-check.yml` (merge-conflict detection + auto-heal), `project-automation.yml` (daily cron + issue/PR sync), `deploy-docs.yml` (VitePress→GitHub Pages), `production-pipeline.yml` (release/archive), `sanitize-bot-comment.yml` (sanitizes bot PR comments).

**No Helm charts exist in the repository.**

**Root `Makefile` targets:** `setup`, `doctor`, `dev`, `fix`, `lint-paths`, `typecheck`, `lint`, `format`, `check`, `verify`, `test`, `sync-gxp`, `rtm`, `adr`, `docs`, `db-reset`, `db-reset-offline`, `db-seed`, `db-status`, `regenerate-templates`, `ports`, `help`.

---

## 3. Data, State & Configuration Topology

### 3.1 Persistence engines by service

| Storage engine | Services |
|---|---|
| Neo4j (exclusive) | designer |
| PostgreSQL (dedicated container) | execution, org (**shares `cadence_edc` with execution**, see §3.2.1), etmf, ctms, quality |
| SQLite (no Postgres container in compose) | safety, interop, tickets, notifications, econsent, eisf |
| SQLite, config present but **not declared in `.env.example`, no compose service** | fileshare, knowledge, payments, imaging |
| None (stateless) | gateway, ai_gateway |

Every Postgres/SQLite-fallback service uses the identical one-line pattern `os.getenv("<APP>_DATABASE_URL", "sqlite+aiosqlite:///:memory:")`, delegated to a shared lifecycle helper `packages/database/__init__.py:441-463` (`get_relational_db_lifespan`). SQLite mode self-migrates via `create_all` at boot; Postgres mode requires the explicit `alembic/` or `migrate.py` path per service. eTMF-specific SQLite immutability triggers are injected ad hoc inside this same shared helper (`packages/database/__init__.py:472-498`) and must be reproduced as real Postgres constraints/triggers in the .NET target, not silently dropped.

#### 3.2.1 Confirmed shared-database anti-pattern: `apps/execution` ↔ `apps/org`

`apps/execution/main.py:112` (`DATABASE_URL`) and `apps/org/main.py:24` (`ORG_DATABASE_URL`) both resolve, in both `.env.example` (lines 70 and 74) and `docker-compose.yml` (lines 187 and 214), to the **identical** connection string: `postgresql+asyncpg://cadence:cadence_password@postgres:5432/cadence_edc`. There is exactly one `postgres` container backing both. `apps/org` has **no `alembic/` directory and no `migrate.py`** — it relies entirely on Execution's already-existing schema in `cadence_edc` and only auto-creates tables when running on SQLite. On Postgres, Org's tables (`organizations`, `sites`, `personnel`, `personnel_assignments`, `delegations_of_authority`, `org_training_logs`, `org_audit_logs`) live in the same physical database as Execution's, with **no ORM-enforced cross-service ownership boundary**. This must be resolved (physical DB split, or an explicit EF Core schema separation with zero cross-`DbContext` navigation) before Org can be a genuinely independent Tier-2 slice — see `03_migration_plan.md`.

### 3.3 Ephemeral state — Redis, and why it currently does nothing in the reference stack

**No Redis service exists anywhere in `docker/docker-compose.yml`.** Every Redis-dependent code path below therefore runs in its no-Redis fallback branch in the reference dev/CI environment at all times — this is not a hypothetical failure mode, it is the default. Three inconsistent Redis-configuration schemes coexist in the codebase:

1. **`REDIS_URL`** (`packages/security/sig_token_verifier.py:16`, sole reader) — single-use e-signature replay-prevention cache (`TokenConsumptionCache`, `sig_token_verifier.py:37-69`). Uses atomic `SET NX EX` when Redis is present. **When `REDIS_URL` is unset (the default), falls back to a process-local `threading.Lock()`-guarded in-memory `dict`** (`:62-69`). Consequence: with more than one worker/replica, a signature token consumed against worker A can be replayed against worker B — a live 21 CFR Part 11 gap in the current architecture, not a hypothetical one.
2. **`REDIS_HOST`/`REDIS_PORT`/`REDIS_PASSWORD`/`REDIS_CHANNEL`** (`apps/econsent/infrastructure/cache.py`, none of these four vars declared in `.env.example`) — `econsent_cache_invalidation` pub/sub, a self-subscribing cross-instance cache-coherence channel for the eConsent translation cache. Fire-and-forget; publish is a silent no-op if `REDIS_HOST` is unset (`cache.py:44-59`); by design it degrades to per-process TTL expiry (default 3600s) when Redis is absent — an accepted, not accidental, fallback.
3. **`REDIS_HOST`/`REDIS_PORT`/`REDIS_PASSWORD`/`REDIS_CHANNEL_CONSENT`** — `econsent_consent_completed`, published by `apps/econsent/presentation/routers/econsent.py:89-137` (two call sites, `:669`, `:1091`) on consent completion, subscribed by `apps/execution/workers/consent_subscriber.py`. The subscriber **auto-creates a `ClinicalSubject` and cached `SubjectConsent` row in Execution's system-of-record database** on receipt. `start_consent_subscriber()` (`consent_subscriber.py:189-208`) explicitly no-ops if `REDIS_HOST` is unset — **meaning in the default reference stack, this subscriber never starts, and Execution never auto-provisions subjects from eConsent completions.** This is a load-bearing cross-service side effect that is silently dormant in every environment that matches the shipped `docker-compose.yml`. It is fire-and-forget PUB/SUB with no persistence — a message published while the subscriber is down is lost forever. Given it feeds system-of-record subject creation, this is a genuine GxP data-integrity gap in the *current* Python architecture, and the .NET target must not reproduce it as-is: replace with a durable mechanism (the platform's own MassTransit transactional outbox, per `docs/adr/2026-09-02-core-service-architecture-cqrs-transactional-outbox-and-gxp-interceptor-standard.md`) rather than Redis PUB/SUB.

No other distributed-lock mechanism exists; all other serialization uses Postgres-native `pg_try_advisory_xact_lock` (`apps/execution/database/sealer.py:50`, lock id 42001; `apps/execution/queries_escalation.py:34`, lock id 42002). Because ADR-2238 already commits the .NET target to a **single PostgreSQL backend for all 18 services**, these advisory locks and the `set_config()`-based session-GUC audit-context pattern (`apps/execution/presentation/routers/subjects.py:425-433`, `apps/execution/database/audit.py:52,62`, `apps/execution/presentation/routers/sdv.py:157,375,554`) are directly portable via Npgsql raw SQL / an EF Core `SaveChanges` interceptor — they are a transcription task, not a re-architecture, and should not be treated as a "no direct .NET equivalent" risk the way a SQL-Server target would face.

### 3.4 Filesystem & blob storage

`packages/storage` provides a `StoragePort`-style abstraction over `LocalStorageProvider`, `S3StorageProvider`/`S3StorageAdapter`, `MinioStorageAdapter` (subclasses `S3StorageAdapter`), and a separate Google Drive v3 client (`gdrive.py`, GxP UI-recording archival — distinct from object storage). Every provider does SHA-256 checksum verification on put/get.

Only `apps/fileshare` (+ its `fileshare-transcoder` sidecar) is wired to MinIO in `docker-compose.yml` (`STORAGE_ENDPOINT_URL`, `STORAGE_BUCKET=cadence-fileshare`, `STORAGE_ACCESS_KEY`/`STORAGE_SECRET_KEY`). `apps/etmf/storage.py:16-32` auto-selects Minio vs. raw S3 by inspecting whether the endpoint URL contains `"localhost"`/`"minio"`; `apps/imaging/adapters/storage.py:16-38` does similar detection reading `IMAGING_STORAGE_BUCKET`. **`STORAGE_BACKEND` and `STORAGE_LOCAL_DIR` (`.env.example:128-129`) are dead configuration — zero code reads them anywhere**; backend selection is actually driven by the endpoint-URL/bucket variables below, none of which are declared in `.env.example` (see §4).

`clamav` (ClamAV daemon, `:3310`) is bound to `apps/fileshare/adapters/clamav_adapter.py:41-42` over a raw TCP socket (`asyncio.open_connection`, zINSTREAM protocol) — this is a **network daemon binding, not a spawned subprocess**, a distinct migration shape from the subprocess sites in §3.8 (a .NET port needs a `TcpClient`-based zINSTREAM client, or an `nClam`-equivalent library, not a process launcher).

### 3.5 Complete environment-variable dictionary

`.env.example` declares **62 active (uncommented) variables** plus 4 commented-out optional examples (`REDIS_URL`, `KEYCLOAK_JWKS_URL`, `LLM_API_KEY`, `LLM_API_ENDPOINT`). All 62 are catalogued below by section, with every reading site found in the codebase and a criticality rating. This supersedes the prior audit's finding that "33 of 62 are undocumented" — that gap is now closed for the *declared* set — but a **new, larger gap was found**: at least **37 additional variables are live in code and/or `docker-compose.yml` and are not declared in `.env.example` at all** (§3.5.6). The true configuration surface is closer to 99 variables than 62.

#### 3.5.1 Global platform (`.env.example` §1-2)

| Var | Default | Consumer(s) | Criticality |
|---|---|---|---|
| `APP_ENV` | `development` | `apps/gateway/main.py:33`; `packages/security/fail_fast.py:24,65` (gates fail-closed secret checks in prod) | Critical |
| `CADENCE_TEST_MODE` | `false` | `apps/designer/.../digitization_service.py:591` | Medium |
| `DEBUG` | `true` | **No code reads this — dead config** | Low (dead) |
| `LOG_LEVEL` | `INFO` | **No code reads this — dead config** | Low (dead) |
| `BRAND_NAME` | Cadence Clinical Research Software | 8 services' FastAPI metadata + `packages/security/fail_fast.py:70-72` prod validation | Low, but boot-gated in prod |
| `BRAND_DOMAIN` | ccrsoft.com | Gateway, Notifications (SMTP sender domain), Designer, `fail_fast.py:74-80` | Low/Medium |

#### 3.5.2 Cryptographic secrets (§3)

| Var | Consumer(s) | Criticality |
|---|---|---|
| `GATEWAY_SECRET` | Gateway (HMAC verify), Interop (SCE sandbox tokens), Quality, Org, Execution, `sig_token_verifier.py:121-125` default HMAC | **Critical** — shared inter-service HMAC secret |
| `AUDIT_LOG_SECRET_KEY` | `packages/compliance/audit.py:79`, `packages/security/crypto_verifier.py:377`, `audit_logger.py:33` | **Critical** — Part 11 audit-trail hash key |
| `INBOUND_EMAIL_HMAC_SECRET` | `packages/security/signing.py:413` | **Critical** — inbound eTMF webhook auth |
| `SAFETY_SALT` | `apps/safety/.../safety.py:724`, `apps/safety/main.py:53` | **Critical** — SUSAR/SAE blinding hash salt |
| `SIGNING_SECRET` | Interop SCE tokens, Designer document/protocol digital seals | **Critical** |
| `REDIS_URL` (commented) | `packages/security/sig_token_verifier.py:16` (sole reader) | **Critical when >1 process** — see §3.3 |

#### 3.5.3 Inter-service `*_URL` (§4) — the real runtime dependency graph

| Var | Default port | Every reading file found |
|---|---:|---|
| `GATEWAY_URL` | 8000 | **No app reads it** — compose-only, set as an env var for other containers but never consumed via `os.getenv` |
| `DESIGNER_URL` | 8001 | `apps/interop/{infrastructure,adapters}/designer_client.py`, `apps/gateway/main.py:468`, `apps/execution/designer_client.py:31` |
| `EXECUTION_URL` | 8002 | `apps/quality/infrastructure/execution_client.py:42`, `apps/gateway/main.py:469`, `apps/ctms/.../ctms.py:2375,2423,2539`, `apps/ctms/.../doa_service.py:15`, `apps/safety/infrastructure/execution_client.py:29`, `apps/etmf/{infrastructure,adapters}/lock_client.py:60,128` |
| `ETMF_URL` | 8003 | `apps/gateway/main.py:470`, `apps/eisf/.../eisf.py:1134,1539,1643`, `apps/econsent/adapters/etmf_client.py:27`, `apps/designer/.../designer_routes.py:1804`, `apps/execution/workers/outbox_worker.py:132` |
| `INTEROP_URL` | 8004 | `apps/gateway/{main.py:471, presentation/routers/ecoa.py:40}`, `apps/org/.../org.py:390,1534` (fallback) |
| `QUALITY_URL` | 8005 | `apps/gateway/main.py:474` (only reader) |
| `NOTIFICATIONS_URL` | 8006 | `apps/gateway/main.py:473`, `apps/safety/{presentation/routers/safety.py:174, processor.py:116, adapters/processor.py:116}`, `apps/etmf/infrastructure/notifications_client.py:29`, `packages/security/notifications.py:177` |
| `CTMS_URL` | 8007 | `apps/gateway/main.py:472` (only reader) |
| `SAFETY_URL` | 8008 | `apps/gateway/main.py:475-477` (only reader) |
| `TICKETS_URL` | 8009 | `apps/gateway/main.py:478` (only reader) |
| `EISF_URL` | 8010 | `apps/gateway/main.py:480`, `apps/org/.../org.py:390,1534` (primary) |
| `ECONSENT_URL` | 8011 | `apps/gateway/main.py:481`, `apps/execution/econsent_client.py:46` |
| `ORG_URL` | 8012 | `apps/gateway/main.py:479`, `apps/notifications/workers/notification_worker.py:29`, `packages/security/org_client.py:126` — **both of the latter two default their fallback to port `:8010` (EISF's port) instead of `:8012` — a copy-paste bug in the Python source, must not be replicated in .NET config defaults.** |

**Five more inter-service URL couplings are used in code but never declared in `.env.example`:** `FILESHARE_URL` (:8013), `IMAGING_URL` (:8014), `KNOWLEDGE_URL` (:8015), `PAYMENTS_URL` (:8016), `AI_GATEWAY_URL` (:8017, consumed by Interop, Safety, Econsent, Execution).

**Eight non-canonical `*_SERVICE_URL` variables** (found by the coupling-DAG research pass, not by name-matching the canonical list) carry real inter-service edges and are undocumented anywhere: `EXECUTION_SERVICE_URL` (designer, wrong default port `:8001`), `TICKETS_SERVICE_URL` (designer), `CTMS_SERVICE_URL` (execution, wrong default port `:8005`), `KNOWLEDGE_SERVICE_URL` (execution, tickets — tickets' default is wrong, `:8006` instead of `:8015`), `ETMF_SERVICE_URL`/`QUALITY_SERVICE_URL`/`SAFETY_SERVICE_URL` (ctms, wrong default ports `:8006`/`:8008`/`:8009` vs. real `:8003`/`:8005`/`:8008`), `ORG_SERVICE_URL` (`packages/security/org_client.py:66`). These only work today because every real deployment sets the vars explicitly — the wrong fallback defaults are landmines if a Phase 1 .NET config carries the same defaults forward uncritically.

All 13 canonical + 5 missing + 8 non-canonical = **26 distinct inter-service URL variables** define the platform's actual coupling graph. See `02_bounded_contexts.md` for the resulting dependency table.

#### 3.5.4 Database URLs (§5)

| Var | Default | Criticality |
|---|---|---|
| `NEO4J_URI` / `NEO4J_USER` / `NEO4J_PASSWORD` | `bolt://localhost:7687` / `neo4j` / `cadence_password` | Critical |
| `DATABASE_URL` | `...cadence_edc` | Critical — shared with `ORG_DATABASE_URL`, §3.2.1 |
| `ETMF_DATABASE_URL` / `CTMS_DATABASE_URL` / `QUALITY_DATABASE_URL` | dedicated Postgres per service | Critical |
| `ORG_DATABASE_URL` | **identical value to `DATABASE_URL`** | Critical — shared-DB risk |
| `SAFETY_DATABASE_URL` / `INTEROP_DATABASE_URL` / `TICKETS_DATABASE_URL` / `NOTIFICATIONS_DATABASE_URL` / `ECONSENT_DATABASE_URL` / `EISF_DATABASE_URL` | SQLite file paths | Critical |

Missing from `.env.example` despite being live in code/compose: `FILESHARE_DATABASE_URL`, `KNOWLEDGE_DATABASE_URL`, `PAYMENTS_DATABASE_URL`, `IMAGING_DATABASE_URL`.

#### 3.5.5 Keycloak/OIDC, SMTP/webhooks, terminology, storage (§6-10)

| Var | Consumer | Criticality | Note |
|---|---|---|---|
| `KEYCLOAK_URL` | `apps/fileshare/adapters/credential_verifier.py:18` only | High | Gateway (the primary JWT verifier) does **not** use this var — see below |
| `KEYCLOAK_REALM` / `KEYCLOAK_CLIENT_ID` | Gateway, `fail_fast.py` | Critical | |
| `KEYCLOAK_ADMIN` / `KEYCLOAK_ADMIN_PASSWORD` | Keycloak container bootstrap only, no Python reader | Critical secret, correctly infra-scoped | |
| `JWT_ALGORITHM` | `apps/gateway/main.py:462` | Critical | |
| `KEYCLOAK_JWKS_URL` (commented) | **Never read under this name** — the code reads `JWKS_URL` (`apps/gateway/main.py:66`, no `KEYCLOAK_` prefix) | Critical | **Documentation bug**: the example file names a variable the code doesn't consume |
| `SMTP_HOST/PORT/USERNAME/PASSWORD/USE_TLS/USE_SSL/SENDER` | `apps/notifications/application/delivery.py:46-53` | `SMTP_PASSWORD` critical, rest medium/low | |
| `WEBHOOK_URL` / `WEBHOOK_SIGNING_SECRET` / `WEBHOOK_TIMEOUT` | Notifications | Signing secret critical | |
| `TERMINOLOGY_OFFLINE` / `NCI_EVS_OFFLINE` / `NCI_EVS_BASE_URL` / `NCI_EVS_TERMINOLOGY` / `TERMINOLOGY_CACHE_TTL` | `apps/designer/{db.py,evs_client.py}`, `apps/safety/reconciliation.py:40` (via undocumented `CACHE_TTL` fallback) | Medium | |
| `STORAGE_BACKEND` / `STORAGE_LOCAL_DIR` | **Dead — no code reads either** | Low (dead) | Real backend selection uses undocumented vars, §3.5.6 |
| `INBOUND_EMAIL_MAX_SIZE_BYTES` | `apps/etmf/.../etmf.py:2173` | Medium | |
| `LLM_API_KEY` (commented) | `apps/designer/.../digitization_service.py:585` | Critical when enabled | |
| `LLM_API_ENDPOINT` (commented) | **Dead — no code reads it** | Low (dead) | |

#### 3.5.6 Live variables entirely absent from `.env.example` (37 confirmed)

`REDIS_HOST`, `REDIS_PORT`, `REDIS_PASSWORD`, `REDIS_CHANNEL`, `REDIS_CHANNEL_CONSENT`, `ECONSENT_CACHE_TTL`, `CACHE_TTL`, `FILESHARE_DATABASE_URL`, `KNOWLEDGE_DATABASE_URL`, `PAYMENTS_DATABASE_URL`, `IMAGING_DATABASE_URL`, `FILESHARE_URL`, `IMAGING_URL`, `KNOWLEDGE_URL`, `PAYMENTS_URL`, `AI_GATEWAY_URL`, `JWKS_URL`, `SKIP_JWKS_FETCH`, `JWT_TEST_SECRET`, `ALLOW_UNVERIFIED_JWT_FOR_TEST`, `STORAGE_BUCKET`, `S3_BUCKET_NAME`, `STORAGE_ACCESS_KEY`, `AWS_ACCESS_KEY_ID`, `STORAGE_SECRET_KEY`, `AWS_SECRET_ACCESS_KEY`, `STORAGE_ENDPOINT_URL`, `AWS_ENDPOINT_URL`, `MINIO_ENDPOINT_URL`, `MINIO_BUCKET`, `MINIO_ROOT_USER`, `MINIO_ROOT_PASSWORD`, `IMAGING_STORAGE_BUCKET`, `CLAMAV_HOST`, `CLAMAV_PORT`, `CLAMAV_ENABLED`, `BIOSTAT_EXPORT_SALT`, `TESTING`, plus the 8 non-canonical `*_SERVICE_URL` vars in §3.5.3. **Action item:** backfill all of these into `.env.example` before Phase 1 config transcription begins, so the .NET `appsettings`/secret-manager baseline has one authoritative source instead of being reverse-engineered from source a second time.

### 3.6 Raw SQL / Postgres-native features requiring explicit .NET (Npgsql) equivalents

- `SELECT set_config('cadence.current_user_id', :user_id, true)` and siblings (`apps/execution/presentation/routers/subjects.py:425-433`, `database/audit.py:52,62`, `presentation/routers/sdv.py:157,375,554`) — session-scoped GUCs smuggling audit-actor context into DB triggers/RLS. Reproduce via an EF Core `SaveChanges`/`DbConnection` interceptor issuing the equivalent `SET LOCAL` inside the same transaction.
- `pg_try_advisory_xact_lock(42001)` (`database/sealer.py:50`) and `pg_try_advisory_xact_lock(42002)` (`queries_escalation.py:34`) — transaction-scoped advisory locks. Directly portable via Npgsql raw SQL since the target stays PostgreSQL (ADR-2238).
- eTMF SQLite immutability DDL triggers (`packages/database/__init__.py:472-498`) — must become real Postgres `CREATE TRIGGER ... RAISE EXCEPTION` constraints, not silently dropped when the SQLite-only services move to the platform's unified Postgres standard.

---

## 4. Dark Debt & Dynamic Behavior Audit

This section re-verifies every item the prior audit (AUD-NET10-002 §2.1) found, and adds items an additional forensic sweep turned up that neither the original topology audit nor AUD-NET10-002 had catalogued.

### 4.1 Dynamic imports & reflection

| Site | Status | Description |
|---|---|---|
| `apps/execution/database/models/__init__.py:1,3-144` | **Re-verified, unchanged** | Module-level `__getattr__` (`:132`) + `discover_models()` (`:126`), both driven by `importlib.import_module(f".{submodule}", __name__)` (`:129,138`). Lazily registers **49** named symbols across 17 submodules. A `__dir__()` (`:143`) exposes the same dynamic surface. Every one of the 49 models must be explicitly enumerated in the EF Core `DbContext` — there is no C# equivalent of module-level `__getattr__`. |
| `packages/security/audit_logger.py:161-183` | **Re-verified, unchanged** | `resolve_current_session()` performs upward `importlib.import_module()` against a hardcoded list of `apps.execution.database.context`, `apps.eisf.infrastructure.database`, `apps.etmf.infrastructure.database`, `apps.quality.infrastructure.database` (module names built via string concatenation, deliberately evading a naive `grep "import apps\."` scan), then `getattr(mod, var_name)` to pull each service's `current_session` ContextVar. **This means `packages/security` — certified Tier 1 complete — is not a true dependency-free leaf.** See `02_bounded_contexts.md` §2 for the sequencing consequence. |
| `apps/gateway/main.py:224-252` | **Re-verified, unchanged** | `importlib.import_module(f"apps.{name}.main")` (`:246`) walks `apps/*/main.py` on disk and pulls `getattr(module, "app", None)` for in-process OpenAPI aggregation. ADR-2239's decided replacement (HTTP polling of downstream `/openapi.json` with static-schema fallback) is **not yet implemented in code** — this is still the live mechanism. |
| `scripts/validate_schemas.py:88-89`, `scripts/generate_api_docs.py:205-206` | **New finding** | Two more, independent copies of the exact same `importlib.import_module(f"apps.{name}.main")` + `getattr(module, "app", None)` pattern, for CI-invoked tooling. Not production runtime risk, but confirms the pattern is relied on in three places, not one. |
| `packages/database/graph.py:149-150,222-223` | **New finding** | `WrappedGraphTransaction.__getattr__` / `WrappedGraphSession.__getattr__` forward all unknown attribute access to a wrapped Neo4j driver object. |
| `apps/designer/adapters/safety_gateway.py:112-113,151-152,174-175` | **New finding** | Three more `__getattr__` proxies (`SafeTransaction`, `SafeSession`, `SafeDriver`) — an independent second wrapping of the Neo4j driver in Designer. |
| `apps/designer/validator.py:432` | **New finding** | Another `__getattr__` dynamic-proxy definition, same forwarding idiom. |
| `scripts/validate_markdown.py:668-689` | **New finding** | `import_class_by_name_from_map()` uses `importlib.util.spec_from_file_location` + `module_from_spec` + `sys.modules[...]` + `spec.loader.exec_module(module)` to dynamically load and **execute** an arbitrary `.py` file by path, then `getattr()` a named class out of it, to validate doc-vs-code field consistency. Tooling-only, but is a materially different risk shape (arbitrary module execution, not just import) from a plain `import_module` call — worth noting for anyone auditing this script's trust boundary. |

### 4.2 Runtime reflection / eval / exec / metaclasses / monkeypatching

**No `eval()`/`exec()` in production code, and no metaclasses anywhere in the repository** (verified repo-wide). Two genuine monkeypatch/class-mutation sites were found, neither previously catalogued:

- **`apps/designer/adapters/safety_gateway.py:178-188`** — At **import time**, this module captures `_original_driver_factory = AsyncGraphDatabase.driver`, defines `safe_driver_factory()`, and executes `AsyncGraphDatabase.driver = safe_driver_factory` as a bare module-level statement — permanently replacing the third-party `neo4j.AsyncGraphDatabase.driver` classmethod for the entire process the moment this module is imported. This has no direct .NET equivalent (Neo4j's .NET driver has no comparable factory-swap seam) and needs an explicit interception strategy — e.g. a wrapping `IDriver` decorator registered via DI — rather than a line-for-line transcription. Whatever safety/query-scoping behavior `SafeDriver`/`SafeSession`/`SafeTransaction` (§4.1) implement must be identified and re-implemented as an explicit decorator, since the Python mechanism (silent global patching) cannot be ported as-is.
- **`packages/compliance/audit.py:562-564`** — `mount_compliance_audit_framework()` attaches a `_compliance_listener_attached` guard flag directly onto SQLAlchemy's `Session` class via `setattr(Session, ...)` to make repeated `event.listen(Session, "before_flush", ...)` registration idempotent across multiple services' `mount()` calls.

Two production `functools.wraps`-based transaction/audit-context decorator factories were found to be **independently duplicated**, not shared: `packages/database/__init__.py:11-57` (`transactional`, used in production at `apps/execution/randomization_service.py`) and `apps/execution/database/decorators.py:19` (a second, execution-local `transactional` decorator, structurally identical). `packages/security/context.py:105-138+` builds a similar audit-context decorator using runtime argument introspection to synthesize audit metadata from a decorated function's own parameters.

### 4.3 Native binaries, FFI, and subprocess

**No `ctypes`, `cffi`, or `.pyx`/`.c`/`.cpp` source exists anywhere in the repository.**

| Subprocess/daemon site | Binary/mechanism | Service |
|---|---|---|
| `apps/interop/domain/sce_sandbox_orchestrator.py:195-202` | `sys.executable` via `asyncio.create_subprocess_exec` | interop, SCE sandbox |
| `apps/interop/domain/sce_sandbox_orchestrator.py:257-264` | `Rscript` (via `shutil.which`) via `asyncio.create_subprocess_exec` | interop, SCE sandbox — **see §4.4, this path is dead code in the shipped container** |
| `apps/fileshare/workers/transcode_worker.py:70,89-99` | `ffmpeg` (via `shutil.which`) | fileshare |
| `apps/fileshare/adapters/conversion_adapter.py:122-140` | `libreoffice`/`soffice --convert-to pdf` (via `shutil.which`) | fileshare — **not in the prior audit's known list** |
| `apps/fileshare/adapters/clamav_adapter.py:41-42` | ClamAV daemon over raw TCP (zINSTREAM), **not a subprocess** | fileshare |
| `apps/{ctms,fileshare,quality}/migrate.py` | `sys.executable -m alembic -c apps/<svc>/alembic.ini upgrade head` | ctms, fileshare, quality |
| `apps/etmf/adapters/migrate.py:886-894,910-918` **and** `apps/etmf/database/migrate.py:945-953,974-982` | `sys.executable -m alembic ... stamp head` / `upgrade head` | etmf — **two independent duplicate implementations, doubling the eTMF migration-parity surface**, not one file with two paths as prior shorthand suggested |
| `packages/deid/cli.py:127`, `packages/cli/commands/record.py:509-515`, `packages/cli/commands/*.py` | `subprocess.Popen`/`subprocess.run` (ffmpeg, uv, pytest, ruff, alembic, dev servers) | developer CLI, not a runtime service |
| `scripts/*.py` (16 files) | `git`, `uv`, alembic, `ruff`, `tsc`, pre-commit binaries | CI/repo tooling, operator-invoked |

### 4.4 The SCE sandbox: confirmed undocumented, and confirmed non-functional as designed

`apps/interop/domain/sce_sandbox_orchestrator.py` runs untrusted Python and R scripts through `asyncio.create_subprocess_exec` with a scrubbed offline environment (`UV_OFFLINE=1`, `PIP_NO_INDEX=1`, `NO_PROXY=*`) and a bounded timeout. This matches AUD-NET10-002 Gap A2 exactly. Two facts sharpen the risk beyond what that audit found:

1. **No Dockerfile in the repository installs an R runtime.** All 19 Dockerfiles were checked; `apps/interop/Dockerfile` (the service that owns this file) installs no OS packages beyond the base `python:3.14-slim-bookworm` image. The "real Rscript" code path (`sce_sandbox_orchestrator.py:234-294`) is therefore **dead code in the actual deployed container today.**
2. **When `Rscript` is not found, the code does not error — it silently falls back to `_execute_r_fallback_engine()` (`:298-364`), a hand-coded Python function that fabricates plausible R-style stdout** (`"[R 4.4.2 SCE Sandbox Engine]..."`) and a synthetic `t_summary_statistics.csv`. A caller inspecting only the output artifacts cannot distinguish real R execution from this simulated fallback. `apps/interop/domain/sce_models.py:199` even hardcodes `r_version: str = Field("4.4.2", ...)`, and generated OpenAPI docs describe a "pinned R runtime version" the container cannot deliver. **This fallback-masking behavior is a correctness risk independent of the provisioning gap** and was not previously documented anywhere. Before this slice is scheduled, the target-state decision (managed R interop, a sidecar container, or removing the R execution path entirely) must be made explicitly, and if any R path is retained, it must fail loudly rather than fabricate output when the runtime is absent.

### 4.5 Custom JSON encoders / canonicalization (audit-chain integrity risk)

No `class X(json.JSONEncoder)` subclasses exist; all custom serialization uses `json.dumps(..., default=...)` at call sites. **9 files / 11 distinct call sites** confirmed:

- **Integrity-bearing (canonicalize-then-hash, `sort_keys=True, default=str`):** `apps/safety/services/regulatory_audit_ledger.py:192,244,259,397` (4 sites), `apps/execution/services/cec_adjudication_service.py:149`, `packages/engine_core/derived/models.py:97`, `packages/engine_core/timeline/models.py:131`, `packages/engine_core/toxicity/models.py:98`. **These require byte-identical canonical JSON output in the .NET target** (matching key ordering and Python's `str()` fallback formatting for non-primitive types) or historical audit-chain hashes will not re-verify against records sealed by the Python system. This needs a dedicated `System.Text.Json` `JsonConverter`/comparer designed and tested against real Python-produced hashes before any Tier 3 slice touching `regulatory_audit_ledger.py` or `cec_adjudication_service.py` is scheduled — treat as its own action item, not a routine port.
- **Presentation-only (not integrity-bearing):** `apps/interop/domain/cdr/dataset_json_compiler.py:913`, `apps/safety/presentation/routers/safety.py:2355`, `packages/cli/formatting.py:29,148`, `packages/cli/mcp/server.py:389`.

### 4.6 Float vs. Decimal — confirmed scope, and a required carve-out

- `apps/payments/domain/models.py` — **60** `float`-typed fields (not the ~30 previously estimated), zero `decimal.Decimal` usage. Every monetary field (`gross_amount`, `net_amount`, `withholding_amount`, `patient_stipend_amount`, `total_budget`, `current_balance`, etc.) is in scope for the Universal Decimal Precision Standard without qualification.
- `apps/execution` — **46 files** contain `float`-typed fields (42 non-test), **390 total `float` field occurrences**, concentrated in labs/RTSM/adaptive-randomization/CEC-adjudication code (`lab_transport_models.py` alone has 41). These are in scope for `decimal` **where they represent persisted dosage/lab-result values**, per `CONTEXT.md`'s standard.
- `apps/quality/application/services/statistical_forensics_service.py:112,191` — confirmed `math.isnan`/`math.isinf` sentinel checks in Benford's Law and terminal-digit-rounding forensic engines, where `NaN`/`Infinity` are legitimate, expected values signaling "excluded from sample" (e.g. from upstream division-by-zero). **`System.Decimal` cannot represent `NaN`/`Infinity`** and throws instead of propagating a sentinel. **This file is an explicit, required carve-out from the Universal Decimal Precision Standard** — it must remain `double` in the .NET target. `CONTEXT.md`'s current wording ("high-precision dosage/lab calculations") does not state this exclusion; it must be added before any Quality-tier slice is scheduled (see `02_bounded_contexts.md` and the action item in `03_migration_plan.md`).

---

## 5. External network dependencies

| System | Consumer | Protocol |
|---|---|---|
| Keycloak (OIDC/JWKS) | Gateway (JWT verify), all services indirectly via Gateway-issued tokens | HTTPS |
| NCI EVS terminology API | Designer | HTTPS (`NCI_EVS_BASE_URL`, offline-mode fallback via `NCI_EVS_OFFLINE`) |
| Google Drive API v3 | `packages/storage/gdrive.py` — GxP UI-recording/report archival | HTTPS/OAuth2 service account |
| AWS S3 / MinIO | Fileshare, Etmf (conditional), Imaging | S3 API |
| SMTP relay | Notifications | SMTP(S) |
| Outbound webhooks | Notifications → arbitrary configured `WEBHOOK_URL` | HTTPS, HMAC-signed |
| LLM API (Gemini/OpenAI-compatible) | Designer digitization (optional, gated by `CADENCE_TEST_MODE`) | HTTPS |
| ClamAV daemon | Fileshare | TCP (zINSTREAM), in-cluster only |

---

## 6. What could not be verified in this environment

In the interest of not repeating AUD-NET10-002's central finding — that a prior pass certified things it had not actually checked — this section states discovery limits plainly:

- **No `dotnet` SDK could be installed in this sandbox** (`apt-get install dotnet-sdk-10.0` returned 404 for every package on both configured Ubuntu mirrors; `dot.net`, the official install-script host, is not on the environment's outbound-proxy allowlist). This document and `04_scaffold_solution.sh` were produced without ever running `dotnet build`, `dotnet restore`, or `dotnet new` in this session. See `05_orphan_audit_gate.md` §4 for what this means for Step 5 of this task.
- The 1,675-file Python census (§0) is exhaustive and mechanically verifiable (a `find` command anyone can re-run), but the *content*-level findings in §2-4 come from four parallel research passes using `grep`/`Read` over the highest-signal locations, not a line-by-line read of all 1,675 files. Where a finding says "confirmed," it was independently checked against source at least once in this session. Anything not explicitly called out here should be assumed unverified rather than assumed complete.

---

## 7. Reconciliation with prior discovery documents

This baseline **agrees with and closes** AUD-NET10-002's action items 6 (SCE sandbox), 7 (config matrix backfill — see §3.5.6, though the true gap is larger than that audit found), and adds fresh evidence for item 9 (`packages/security` leaf-node status, §4.1). It **resolves** action item 2 (Quality Tier 2/3 contradiction) — see `02_bounded_contexts.md` §2 for the full reasoning; the short answer is that Tier 3 (the matrix) is correct and ADR-2239's wording is the artifact that needs a follow-up amendment, not the other way around. It **does not yet close** action items 3 (payments decimal conversion — not yet implemented in code), 4 (decimal enforcement gate), 8 (Redis pub/sub vs. outbox decision — recorded as a target-state recommendation in §3.3, not yet implemented), or 10 (`sig_token` fail-closed requirement — recorded as a requirement in §3.3, not yet implemented). Those remain open engineering work for the slices that touch them, tracked in `03_migration_plan.md`.
