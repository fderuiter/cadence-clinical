# 02. Bounded Context Decomposition & Coupling Analysis

**Document ID:** PHASE0-NET10-02
**Status:** Independently re-derived from source (see method note in `01_system_topology.md` §0). Every dependency edge below was found by grepping actual `*_URL`/`*_SERVICE_URL` consumption in each service's code, not inferred from domain knowledge or copied from `docs/migration/BOUNDED_CONTEXT_MIGRATION_MATRIX.md`. Where this document's evidence confirms that matrix, it is noted as confirmed; where it corrects it, the correction and its evidence are called out explicitly.

---

## 1. Context Catalog

20 Python-backed units map to exactly one bounded context each (18 FastAPI services + 2 Vue SPA edges); every `apps/*` and `packages/*` directory in the repository maps into this table's "Assigned Python Paths" column or into the Shared Kernel / Cross-Cutting section (§3). The full file-by-file accounting proving no `.py` file is left unclassified is `05_orphan_audit_gate.md`.

| Context | Assigned Paths | Owned Store | Inbound Callers (verified) | Outbound Deps (verified) |
|---|---|---|---|---|
| **Gateway** | `apps/gateway` (29 files) | Stateless (in-memory route table) | Web SPA, Subject-Portal SPA, external clients | *(reverse-proxies to all 17 backend services — not a business-domain edge)* |
| **Execution (EDC/RTSM)** | `apps/execution` (330 files) | PostgreSQL `cadence_edc` — **shared with Org**, see §4.1 | Quality, Ctms, Etmf, Safety, Designer, Gateway(proxy) | Designer, Etmf, Econsent, Notifications (via `packages/security`), AiGateway, Knowledge, Ctms |
| **Designer (USDM/MDR)** | `apps/designer` (169 files) | Neo4j (USDM v3/v4 graph) | Execution, Interop, Gateway(proxy) | Etmf, Execution, Tickets |
| **Org (Directory/DOA)** | `apps/org` (22 files) | PostgreSQL `cadence_edc` — **shared with Execution, no own migrations**, see §4.1 | Notifications, `packages/security`, Gateway(proxy) | Eisf, Interop |
| **CTMS (Site Ops)** | `apps/ctms` (65 files) | PostgreSQL `cadence_ctms` | Execution (greenlight check), Gateway(proxy) | Execution, Etmf, Quality, Safety |
| **eTMF** | `apps/etmf` (115 files) | PostgreSQL `cadence_etmf` + S3/MinIO | Eisf, Econsent, Designer, Execution, Ctms, Gateway(proxy) | Execution, Notifications |
| **eISF** | `apps/eisf` (43 files) | PostgreSQL/SQLite `cadence_eisf` | Org, Gateway(proxy) | Etmf |
| **Quality (CAPA/CSM)** | `apps/quality` (54 files) | PostgreSQL `cadence_quality` | Ctms, Gateway(proxy) | Execution *(synchronous request-time HTTP only — see §2)* |
| **Safety (PV)** | `apps/safety` (73 files) | PostgreSQL/SQLite `cadence_safety` | Ctms, Gateway(proxy) | Notifications, Execution, AiGateway |
| **Econsent** | `apps/econsent` (67 files) | PostgreSQL/SQLite `cadence_econsent` | Execution (async, via Redis — see `01_system_topology.md` §3.3), Gateway(proxy) | Etmf, Notifications, AiGateway |
| **Payments** | `apps/payments` (30 files) | SQLite `cadence_payments`, **DB URL undeclared in `.env.example`, no compose service** | Gateway(proxy) only | *(none found — true topological sink)* |
| **Tickets** | `apps/tickets` (47 files) | PostgreSQL/SQLite `cadence_tickets` | Designer, Gateway(proxy) | Knowledge |
| **Notifications** | `apps/notifications` (29 files) | PostgreSQL/SQLite `cadence_notifications` | Interop, Safety, Econsent, Etmf, Execution, `packages/security`, Gateway(proxy) | Org |
| **Interop (FHIR/Sensors/SCE)** | `apps/interop` (110 files) | PostgreSQL/SQLite `cadence_interop` | Org, Gateway(proxy) | Notifications, AiGateway, Designer |
| **AiGateway** | `apps/ai_gateway` (31 files, **no Dockerfile, no compose service**) | Ephemeral vector cache / REST | Interop, Safety, Econsent, Execution, Gateway(proxy) | *(none found — true topological sink)* |
| **Fileshare** | `apps/fileshare` (49 files) | MinIO/S3 + PostgreSQL/SQLite, **DB URL undeclared, no compose service listed under this name in `.env.example`** | Gateway(proxy) only | *(none found besides Keycloak, external — true sink)* |
| **Imaging** | `apps/imaging` (23 files, **no Dockerfile, no compose service**) | PostgreSQL/SQLite, DB URL undeclared | Gateway(proxy) only | *(none found besides MinIO/S3, external — true sink)* |
| **Knowledge** | `apps/knowledge` (39 files, **no compose service**) | PostgreSQL/SQLite, DB URL undeclared | Tickets, Execution (x2), Gateway(proxy) | *(none found — true sink)* |
| *(edge, non-Python)* Web | `apps/web` (5 files, Vue 3) | n/a | Browser | Gateway |
| *(edge, non-Python)* Subject-Portal | `apps/subject-portal` (Vue 3) | n/a | Browser | Gateway |

**`imaging`, `knowledge`, `payments`, and `ai_gateway` are real, fully-formed FastAPI services with zero business-domain outbound coupling, but are not wired into `docker-compose.yml` and (for imaging/ai_gateway) have no Dockerfile at all.** They are complete bounded contexts, just an incomplete deployment topology — treat them as first-class migration targets, not as unfinished stubs.

---

## 2. Resolving the Cadence.Quality Tier 2/3 contradiction

`docs/migration/Migration_Phase0_Readiness_Audit.md` (AUD-NET10-002, Gap D1) found that `ADR-2239` §3.C.1/§4.3 states three times that `Cadence.Quality` is reclassified to **Tier 2**, while `docs/migration/BOUNDED_CONTEXT_MIGRATION_MATRIX.md` (line 184) and the audit it superseded both sequence it in **Tier 3**, with no document reconciling the two. This baseline resolves it as follows.

**Evidence.** `apps/quality/application/services/quality_surveillance_runner.py:44` imports `QualityExecutionClient` from `apps/quality/infrastructure/execution_client.py`, which resolves `EXECUTION_URL` and performs **synchronous, request-time** `httpx` calls to three Execution endpoints (`execution_client.py:88,145,175`: `/api/v1/execution/biostat/sdtm/{DOMAIN}`, `/api/v1/execution/audits`, `/api/v1/execution/attachments/images`). A full sweep of `apps/quality` for consumer/subscriber/outbox/MassTransit patterns found **zero** async ingestion path from Execution — the CSM surveillance runner (Benford's Law digit analysis, correlation-matrix anomaly detection, duplicate-record detection) depends entirely on live synchronous HTTP harvest calls to Execution. This confirms ADR-2239's factual premise: Quality was never a true Tier-1 leaf.

**Where the ADR's label is wrong.** The matrix's Tier 2 bucket (`Execution`, `Designer`, `Org`) is defined by its own mermaid graph as receiving edges only from Tier 1 *packages*, never from another *app* — that property is what lets those three services start migrating in parallel on day one. Quality does not meet that bar: it cannot begin until Execution's `/biostat/sdtm/*`, `/audits`, and `/attachments/images` contracts are frozen in `Cadence.Contracts.IExecutionApiClient`. That is a strictly sequential, one-wave-behind relationship — exactly what the existing Tier 3 bucket already encodes (`T2A --> T3D` in the matrix's own DAG), and the matrix's prose at line 184 already states the correct rationale. It just never updated the bucket *label* to Tier 2 to match the ADR, and the ADR never got amended to match the matrix. Moving Quality into a literal Tier-2 bucket would break that tier's defining invariant (dependency-free of other apps) for no operational benefit — the platform's own strangler-fig REST proxying decision (ADR-2239 Option B.1) already makes the callee's runtime irrelevant, so what actually matters for sequencing is only "Quality must not start before Execution's contract is frozen," which Tier-3-after-Tier-2 already guarantees.

**Verdict adopted by this baseline: Tier 3 is correct.** `ADR-2239` §3.C/§4.3 should be amended (not the matrix) to read "removal from Tier 1, sequenced in Tier 3 immediately after Execution" rather than "Tier 2 Realignment." This is carried forward as action item MIG-01 in `03_migration_plan.md`.

**Secondary scheduling note.** Within Tier 3, Quality has the narrowest footprint of any peer — one upstream edge to Execution, zero lateral Tier-3 peer dependencies — versus Ctms, which fans out to Execution + Etmf + Quality + Safety. Quality and Payments (which has zero inbound/outbound domain coupling anywhere) are the best-positioned Tier-3 slices to schedule first once Execution ships.

---

## 3. Shared Kernel & Cross-Cutting Concerns

| Package | Path | Files | Responsibility | Leaf-node status |
|---|---|---:|---|---|
| `Cadence.Core.Domain` | `packages/engine_core` | 59 | `AggregateRoot<T>`, `Entity<T>`, `ValueObject`, `Result<T,DomainError>`, `IGxPAuditable`, USDM v3/v4 primitives | **True leaf** — zero `apps.*` references, prod or test |
| `Cadence.Security` | `packages/security` | 36 | HMAC signer, canonical JSON, `sig_token` replay verifier, Keycloak JWT filters, cross-service HTTP clients | **NOT a true leaf** — see §3.1 |
| `Cadence.Database` | `packages/database` | 15 | EF Core base config equivalents, `GxPAuditInterceptor`, Postgres connection resiliency, Neo4j `graph.py` driver wrapper | True leaf in production code; test fixtures import `apps.*` |
| `Cadence.Contracts` | `packages/contracts` | 21 (`packages/ddf`) — see note | Inter-service DTOs, `IExecutionApiClient`, event contracts | **True leaf** |
| `Cadence.Testing` | `packages/testing` | 7 | Entity factories, in-memory repo fakes, mock security context | True leaf in production code; its own test suite imports `apps.*` |
| `Cadence.Cli` | `packages/cli` | 24 | `cadence` Typer CLI, MCP stdio server, developer tooling | **True leaf** |
| `Cadence.Deid` | `packages/deid` | 16 | HIPAA Safe Harbor / Expert Determination PHI redaction, DICOM anonymization | **True leaf** |
| `Cadence.Storage` | `packages/storage` | 15 | `StoragePort`, S3/MinIO/local adapters, Google Drive archival client | True leaf in production code; test fixtures import `apps.*` |
| `Cadence.Ddf` | `packages/ddf` | 21 | Digital Data Flow compiler, Dataset-JSON ETL | **True leaf** |
| `Cadence.Compliance` | `packages/compliance` | 14 | IQ/OQ/PQ runner, RTM sync, SQLAlchemy `Session` audit-listener mount (`audit.py:562-564`) | True leaf in production code; test fixtures import `apps.*` |
| `Cadence.Hexagonal` | `packages/hexagonal` | 5 | Ports/adapters scaffolding templates for `scripts/scaffold_service.py` | True leaf in production code; test fixtures import `apps.*` |
| `packages/tooling_core` | `packages/tooling_core` | 4 | Shared CLI-tooling command handlers | True leaf |
| `packages/sentinels` | `packages/sentinels` | 4 | Architecture-drift sentinel definitions consumed by `scripts/validate_architecture_drift.py` | True leaf |
| `packages/core-models` | `packages/core-models` | 1 | (single-file placeholder) | True leaf |
| `packages/gateway-rewrite` | `packages/gateway-rewrite` | 0 `.py` (TypeScript/NestJS) | Parallel, separate Gateway rewrite track | Out of scope — not Python, not part of this migration's file census |

### 3.1 `packages/security` is not a true leaf — carried forward as MIG-02

`packages/security/audit_logger.py:163-175` performs `importlib.import_module()` reflection into `apps.execution.database.context`, `apps.eisf.infrastructure.database`, `apps.etmf.infrastructure.database`, and `apps.quality.infrastructure.database` (module path built via string concatenation, which is why a naive `grep "import apps\."` scan misses it — this is exactly how it evaded the discovery baseline that first certified `Cadence.Security` "Tier 1, complete"). In addition, `packages/security/org_client.py:66,126` and `packages/security/notifications.py:177` make outbound runtime HTTP calls to `apps/org` and `apps/notifications` respectively. **The "leaf" package is itself a runtime dependency edge onto three-to-five app-tier services.** Before any Tier 1 "complete" certification for `Cadence.Security` is trusted for Phase 1 planning, this reflection must be replaced with an explicit interface (e.g. an `ICurrentSessionAccessor` resolved via DI, populated by each service's own composition root) and the Org/Notifications HTTP clients must be acknowledged as genuine outbound coupling in the DAG, not "shared kernel, therefore dependency-free."

---

## 4. Coupling Anomalies

### 4.1 Confirmed shared-database anti-pattern: Execution ↔ Org

`apps/execution/main.py:112` (`DATABASE_URL`) and `apps/org/main.py:24` (`ORG_DATABASE_URL`) resolve to the identical Postgres connection string in both `.env.example` and `docker-compose.yml` (`.../cadence_edc`), backed by one `postgres` container. `apps/org` has no `alembic/` directory and no `migrate.py` of its own — its tables (`organizations`, `sites`, `personnel`, `personnel_assignments`, `delegations_of_authority`, `org_training_logs`, `org_audit_logs`) physically live inside Execution's schema with no ORM-enforced boundary. **This must be resolved before Org is a genuinely independent Tier-2 slice**: either split into a physically separate database with a one-time data migration, or, at minimum, isolate Org's tables into their own Postgres schema with a dedicated `DbContext` and zero cross-context navigation properties, enforced by the platform's own `ArchUnitNET` "distinct `DbContext` per microservice" rule (already codified in `BOUNDED_CONTEXT_MIGRATION_MATRIX.md` §2).

### 4.2 Circular dependencies (confirmed via code, not diagram inference)

Three bidirectional service pairs were found; two are low-risk because the pair spans tiers (the later tier can be built against a frozen, earlier contract), one is a genuine same-tier cycle requiring an explicit sequencing decision:

1. **Execution ↔ CTMS** — Execution → CTMS via `CTMS_SERVICE_URL` (`apps/execution/greenlight_gating.py:77`, checks site greenlight status before allowing enrollment/eCRF writes/RTSM randomization); CTMS → Execution via `EXECUTION_URL` (`apps/ctms/application/doa_service.py:15,72,105,136,214`, `apps/ctms/presentation/routers/ctms.py:2375,2423,2539`). Different tiers (Execution=2, Ctms=3) — lower risk, but Execution's reverse call means it is not a *pure* Tier-2 leaf either; this bidirectional edge should be documented explicitly in the DAG rather than left as a one-directional arrow.
2. **Execution ↔ Etmf** — Execution → Etmf via `ETMF_URL` (`apps/execution/workers/outbox_worker.py:132`); Etmf → Execution via `EXECUTION_URL` (`apps/etmf/{infrastructure,adapters}/lock_client.py:60,128`, record-lock coordination, duplicated in two files). Different tiers, same lower-risk shape as #1.
3. **Execution ↔ Designer — same-tier cycle, requires an explicit decision.** Execution → Designer via `DESIGNER_URL` (`apps/execution/designer_client.py:31`); Designer → Execution via `EXECUTION_SERVICE_URL` (`apps/designer/presentation/routers/designer_routes.py:2618`, protocol-amendment publish flow). **Both services are Tier 2** — neither can be treated as strictly "ready first." This requires either simultaneous/parallel migration with contract-first coordination (freeze both directions' HTTP contracts in `Cadence.Contracts` before either team starts), or an explicit sub-sequencing decision inside Tier 2. Carried forward as action item MIG-03.

### 4.3 Config-drift landmines: non-canonical variable names and wrong fallback defaults

Eight inter-service coupling edges use variable names that don't match the canonical `*_URL` convention documented anywhere (`EXECUTION_SERVICE_URL`, `TICKETS_SERVICE_URL`, `CTMS_SERVICE_URL`, `KNOWLEDGE_SERVICE_URL`, `ETMF_SERVICE_URL`, `QUALITY_SERVICE_URL`, `SAFETY_SERVICE_URL`, `ORG_SERVICE_URL`) — see `01_system_topology.md` §3.5.3 for the full list. Several also carry **wrong fallback ports** relative to the real port assignments (e.g. `apps/execution/greenlight_gating.py:77` defaults CTMS to `:8005`, which is actually Quality's port; `apps/ctms/adapters/clients.py:21,73,120` default Etmf/Quality/Safety to `:8006`/`:8008`/`:8009` instead of `:8003`/`:8005`/`:8008`; `apps/notifications/workers/notification_worker.py:29` and `packages/security/org_client.py:126` both default Org to `:8010`, EISF's port). These only work today because every real deployment sets the variables explicitly. **Do not carry these fallback defaults into .NET `appsettings.json`** — resolve each service's correct port from `01_system_topology.md` §2.1, not from the buggy Python defaults.

---

## 5. Regulated-Logic Scoping Note: the Universal Decimal Precision Standard

`CONTEXT.md`'s "Universal Decimal Precision Standard" covers "high-precision dosage/lab calculations" without qualification, which creates a live conflict with `apps/quality/application/services/statistical_forensics_service.py` (NaN/Infinity sentinel usage — `System.Decimal` cannot represent either). This is a cross-cutting scoping decision that affects the **Quality**, **Execution**, and **Payments** contexts simultaneously and is not resolved by picking a tier — it must be resolved once, in `CONTEXT.md` itself, before any of those three contexts' slices are scheduled:

- **In scope, unconditionally:** `apps/payments/domain/models.py` (60 float fields, all monetary — see `01_system_topology.md` §4.6).
- **In scope, per-field:** `apps/execution`'s 46 files / 390 float fields covering labs, RTSM adaptive dosing, and CEC adjudication — these are persisted dosage/lab-*result* values and belong in `decimal`.
- **Explicit carve-out, must stay `double`:** `apps/quality/application/services/statistical_forensics_service.py` — intermediate statistical/analytical computation (Benford's Law, terminal-digit rounding) where NaN/Infinity are meaningful sentinels, not data-entry errors.

This is action item MIG-04 in `03_migration_plan.md`.
