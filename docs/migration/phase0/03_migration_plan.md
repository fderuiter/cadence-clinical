# 03. Migration Sequencing Plan & Strangler Fig Design

**Document ID:** PHASE0-NET10-03
**Status:** Builds directly on `01_system_topology.md` and `02_bounded_contexts.md`. Tier assignments below match `docs/migration/BOUNDED_CONTEXT_MIGRATION_MATRIX.md` where that document is confirmed correct by fresh evidence, and depart from it only where §2 of `02_bounded_contexts.md` documents a specific correction (Cadence.Quality's *label*, not its tier; the Execution↔Designer same-tier cycle; the Org/Execution shared-database blocker; the `packages/security` leaf-node caveat).

---

## 1. Migration Dependency DAG

```mermaid
graph TD
    subgraph "Tier 0 — Shared Kernel (leaf packages)"
        T0A[Cadence.Core.Domain]
        T0B[Cadence.Contracts]
        T0C[Cadence.Database]
        T0D[Cadence.Cli]
        T0E[Cadence.Testing]
        T0F[Cadence.Deid]
        T0G[Cadence.Storage]
        T0H[Cadence.Ddf]
        T0I["Cadence.Security *(not a true leaf — MIG-02)*"]
    end

    subgraph "Tier 2 — Core Entities (parallel start, one internal cycle)"
        T2A[Execution]
        T2B[Designer]
        T2C["Org *(blocked on DB split — MIG-05)*"]
    end

    subgraph "Tier 3 — Orchestrators & Regulatory (sequential-after-Execution)"
        T3A[Quality]
        T3B[Ctms]
        T3C[Etmf]
        T3D[Eisf]
        T3E[Safety]
        T3F[Econsent]
        T3G[Payments]
        T3H[Tickets]
    end

    subgraph "Tier 4 — Edge, Fan-in Hubs & Sinks"
        T4A[Gateway]
        T4B[Notifications]
        T4C[Interop]
        T4D[AiGateway]
        T4E[Fileshare]
        T4F[Imaging]
        T4G[Knowledge]
    end

    T0A --> T2A
    T0A --> T2B
    T0B --> T2A
    T0C --> T2A
    T0I -.->|reflection into apps.execution.database.context| T2A
    T0I -.->|reflection into apps.{eisf,etmf,quality}.*.database| T3D
    T0I -.->|reflection| T3C
    T0I -.->|reflection| T3A

    T2A <-->|same-tier cycle: DESIGNER_URL / EXECUTION_SERVICE_URL — MIG-03| T2B
    T2A <-.->|CTMS_SERVICE_URL / EXECUTION_URL, cross-tier| T3B
    T2A <-.->|ETMF_URL / EXECUTION_URL, cross-tier| T3C

    T2A --> T3A
    T2A --> T3B
    T2A --> T3C
    T2A --> T3E
    T2A -.->|Redis pub/sub, fire-and-forget — MIG-06| T3F
    T2B --> T3H
    T3C --> T3D
    T3C --> T3F
    T3B --> T3A
    T3B --> T3C
    T3B --> T3E

    T3C --> T4B
    T3E --> T4B
    T3F --> T4B
    T3F --> T4D
    T3E --> T4D
    T2A --> T4D
    T2A --> T4G
    T3H --> T4G
    T4C --> T4B
    T4C --> T4D
    T4C --> T2B

    T3B --> T4A
    T4A -.->|proxies all 17, ADR-2239 HTTP-discovery not yet implemented — MIG-07| T2A
    T4A -.-> T2B
    T4A -.-> T2C
    T4A -.-> T3A
    T4A -.-> T3B
    T4A -.-> T3C
    T4A -.-> T3D
    T4A -.-> T3E
    T4A -.-> T3F
    T4A -.-> T3G
    T4A -.-> T3H
    T4A -.-> T4B
    T4A -.-> T4C
    T4A -.-> T4D
    T4A -.-> T4E
    T4A -.-> T4F
    T4A -.-> T4G
```

Solid arrows are hard sequencing dependencies (callee's contract must exist first). Dotted arrows are either the Gateway's routing-table fan-out (not a business dependency) or a flagged risk edge (reflection, same-tier cycle, non-durable async coupling) carried as an action item below rather than a blocking dependency.

---

## 2. Sequenced Migration Table

Complexity is 1 (trivial) to 5 (highest); Blast Radius is the operational/regulatory consequence of a defect in this slice, independent of complexity.

| Phase | Slice ID | Domain | Files | Complexity | Blast Radius | Blockers |
|---|---|---|---:|:---:|:---:|---|
| 0 | SLICE-00a | Cadence.Core.Domain | 59 | 2 | Critical (everything depends on it) | None — already scaffolded (`packages/engine_core/src`) |
| 0 | SLICE-00b | Cadence.Security | 36 | 4 | Critical | MIG-02: reflection into 4 app DBs must become an explicit interface before downstream Tier 3 slices trust its "leaf" status |
| 0 | SLICE-00c | Cadence.Contracts | 21 | 2 | Critical | None — already scaffolded |
| 0 | SLICE-00d | Cadence.Database | 15 | 3 | Critical | Must carry the Postgres advisory-lock and `set_config` GUC patterns forward (§3.6 of `01_system_topology.md`) |
| 0 | SLICE-00e | Cadence.Testing | 7 | 1 | Low | None — already scaffolded |
| 0 | SLICE-00f | Cadence.Cli | 24 | 2 | Low | None — already scaffolded |
| 0 | SLICE-00g | Cadence.Deid | 16 | 3 | High (PHI redaction correctness) | None |
| 0 | SLICE-00h | Cadence.Storage | 15 | 2 | Medium | None |
| 0 | SLICE-00i | Cadence.Ddf | 21 | 3 | Medium | None |
| 1 | SLICE-01 | **Execution (EDC/RTSM)** | 330 | 5 | **Critical** | MIG-05 (Org DB split must land before/alongside), MIG-03 (Designer same-tier cycle), MIG-04 (decimal scoping for 46 files), 49-model EF Core enumeration (§4.1 of `01_system_topology.md`) |
| 1 | SLICE-02 | **Designer (USDM/MDR)** | 169 | 5 | High | MIG-03 (Execution cycle); Neo4j driver monkeypatch (§4.2 `01_system_topology.md`) needs an explicit `IDriver`-decorator redesign before the `SafeDriver`/`SafeSession`/`SafeTransaction` behavior can be trusted as ported |
| 1 | SLICE-03 | **Gateway** | 29 | 3 | **Critical** (single ingress) | MIG-07: `importlib`-based OpenAPI aggregation must become the ADR-2239-decided HTTP-polling + static-fallback mechanism *before* the first non-Python service ships, or gateway doc aggregation breaks on day one of Phase 1 |
| 2 | SLICE-04 | **Org (Directory/DOA)** | 22 | 3 | Medium | **MIG-05 — hard blocker**: no migrations of its own, physically shares `cadence_edc` with Execution (§4.1 `02_bounded_contexts.md`). Must resolve schema/DB separation before this slice is independently deployable. |
| 3 | SLICE-05 | **Quality (CAPA/CSM)** | 54 | 3 | High | Execution's `/biostat/sdtm/*`, `/audits`, `/attachments/images` contracts frozen in `IExecutionApiClient`; MIG-04 double-carve-out for `statistical_forensics_service.py` codified in `CONTEXT.md` before transcription starts |
| 3 | SLICE-06 | **Payments** | 30 | 3 | Medium (isolated — zero coupling) | MIG-04: 60 float→decimal conversions, banker's-rounding parity tests against Python reference values |
| 3 | SLICE-07 | **Ctms (Site Ops)** | 65 | 3 | High | Execution contract frozen; non-canonical `*_SERVICE_URL` fallback-port bugs (§4.3 `02_bounded_contexts.md`) must not be carried into `appsettings.json` defaults |
| 3 | SLICE-08 | **Etmf** | 115 | 4 | High | Execution contract frozen; the two duplicate `migrate.py` implementations (§4.3 `01_system_topology.md`) must be reconciled into one Alembic-equivalent EF Core migration history, not ported twice |
| 3 | SLICE-09 | **Safety (PV)** | 73 | 4 | **Critical** (patient-safety reporting) | Execution + Notifications contracts frozen; ICH E2B(R3) XML generation and blinding-salt logic need parity-tested transcription, not best-effort |
| 3 | SLICE-10 | **Econsent** | 67 | 3 | **Critical** (informed-consent integrity) | MIG-06 — hard blocker: the `econsent_consent_completed` Redis pub/sub → Execution subject-auto-provisioning path is fire-and-forget with no durability and is *currently dormant* in the reference stack (§3.3 `01_system_topology.md`). Must be redesigned onto the platform's MassTransit transactional outbox before this slice ships, not reproduced as-is. |
| 3 | SLICE-11 | **Eisf** | 43 | 2 | Medium | Etmf contract frozen |
| 3 | SLICE-12 | **Tickets** | 47 | 2 | Medium | Knowledge contract frozen (Tier 4, but zero-coupling so can be built in parallel and stubbed) |
| 4 | SLICE-13 | **Notifications** | 29 | 3 | Medium-High (fan-in from 5 domains) | Org contract frozen |
| 4 | SLICE-14 | **AiGateway** | 31 | 3 | High (PHI/PII air-gap correctness) | None (true sink) — no Dockerfile/compose service exists yet, must be created as part of this slice |
| 4 | SLICE-15 | **Fileshare** | 49 | 3 | Medium | None (true sink) — ffmpeg/libreoffice subprocess ports, ClamAV zINSTREAM TCP client |
| 4 | SLICE-16 | **Imaging** | 23 | 4 | High (blinding/unblinding risk in oncology adjudication) | None (true sink) — no Dockerfile/compose service exists yet |
| 4 | SLICE-17 | **Knowledge** | 39 | 2 | Low | None (true sink) |
| 5 | SLICE-18 | **Interop (FHIR/Sensors/SCE)** | 110 | 5 | High (SCE sandbox is a code-execution surface) | MIG-08 — hard blocker: the R-runtime target-state decision (§4.4 `01_system_topology.md`) must be made explicitly (managed R interop / sidecar / remove) before this slice ships; the current fallback-masking behavior must not be transcribed as-is |

**Phase 4 note:** `AiGateway`, `Fileshare`, `Imaging`, `Knowledge` show **zero verified outbound domain coupling** (§1 of `02_bounded_contexts.md`) — they can be staffed as a parallel workstream alongside Phase 3 rather than strictly serially after it, once Tier 0's `Deid`/`Storage`/`Ddf` packages exist. `Payments` shares this same zero-coupling property and could move earlier in Phase 3 for the same reason.

---

## 3. Action Item Register (carried from `01_system_topology.md` / `02_bounded_contexts.md`)

| ID | Action | Blocks |
|---|---|---|
| MIG-01 | Amend `ADR-2239` §3.C/§4.3 wording from "Tier 2 Realignment" to "Tier 1 removal, Tier 3 confirmation" for Cadence.Quality | Documentation-only, no code blocker, but must land before Phase 1 planning is handed to a second team who would otherwise trust the ADR's literal wording |
| MIG-02 | Replace `packages/security/audit_logger.py`'s upward `importlib` reflection with an explicit `ICurrentSessionAccessor` interface | SLICE-05, 08, 09, 11 (all reflect-into-by services) trusting Security's Tier 1 "complete" status |
| MIG-03 | Decide Execution↔Designer same-tier sequencing (parallel with contract-freeze, or explicit sub-order) | SLICE-01, SLICE-02 |
| MIG-04 | Codify the Universal Decimal Precision Standard's explicit carve-out (`statistical_forensics_service.py` stays `double`; `apps/payments` and persisted `apps/execution` lab/dosage fields become `decimal`) in `CONTEXT.md`, plus a Roslyn banned-API/analyzer enforcement rule | SLICE-01, SLICE-05, SLICE-06 |
| MIG-05 | Resolve the Execution/Org shared `cadence_edc` database (schema split + dedicated `DbContext`, or physical DB split with data migration) | SLICE-04 |
| MIG-06 | Replace the `econsent_consent_completed` Redis pub/sub subject-auto-provisioning path with the platform's MassTransit transactional outbox | SLICE-10 |
| MIG-07 | Implement ADR-2239's decided Gateway OpenAPI HTTP-polling + static-fallback mechanism, replacing the `importlib`-based in-process aggregation | SLICE-03 — and functionally blocks *every* other slice's Gateway-visible API docs the moment it's the first non-Python service |
| MIG-08 | Decide the SCE sandbox's target-state R execution strategy (managed interop / sidecar / remove) and stop the silent fallback-masking behavior | SLICE-18 |
| MIG-09 | Require a distributed (Redis-backed) store for `sig_token` replay prevention in any Production `APP_ENV`, fail-closed if unconfigured — do not port the in-memory fallback as an acceptable Production behavior | SLICE-00b (Cadence.Security), cross-cutting for every service performing e-signature re-authentication |
| MIG-10 | Backfill `.env.example` / the target `appsettings.json` baseline with the 37 undocumented-but-live variables and 8 non-canonical `*_SERVICE_URL` variables cataloged in `01_system_topology.md` §3.5.6 and §4.3 of `02_bounded_contexts.md`, correcting the wrong fallback ports rather than carrying them forward | All Tier 2-4 slices' configuration baseline |

---

## 4. Strangler Fig Routing Design

`apps/gateway` is already the platform's single ingress (Tier 4, but architecturally first in sequencing terms because every other slice's traffic passes through it from day one of Phase 1). The strangler-fig cutover replaces it in place with an ASP.NET Core **YARP** (`Yarp.ReverseProxy`, already pinned in `Directory.Packages.props`) reverse proxy that can route a given path prefix to either the legacy Python FastAPI service or its new .NET 10 replacement, toggled per-route without a client-visible change.

### 4.1 Routing table shape

```jsonc
// appsettings.yarp.json — illustrative, one entry per bounded context.
// "Destination" toggles per-context as each Phase in §2 ships; both
// destinations exist simultaneously during a slice's own cutover window.
{
  "ReverseProxy": {
    "Routes": {
      "execution-route": {
        "ClusterId": "execution-cluster",
        "Match": { "Path": "/api/v1/execution/{**catch-all}" }
      },
      "designer-route": {
        "ClusterId": "designer-cluster",
        "Match": { "Path": "/api/v1/{cdisc,usdm}/{**catch-all}" }
      }
      // ... one route block per bounded context in §2's table, same
      // path prefixes the current apps/gateway/main.py SERVICES registry
      // already uses (apps/gateway/main.py:481-486).
    },
    "Clusters": {
      "execution-cluster": {
        "Destinations": {
          "legacy": { "Address": "http://execution-python:8002/" },
          "dotnet": { "Address": "http://execution-dotnet:8002/" }
        }
      }
    }
  }
}
```

During a service's own migration window, `ClusterId` for its route points at whichever destination is currently authoritative; both containers can run side-by-side (shadow traffic or blue/green) using YARP's weighted-destination support before the legacy destination is retired and its Dockerfile deleted.

### 4.2 OpenAPI aggregation (replaces MIG-07's target mechanism)

Per ADR-2239 Option B.1 (decided, not yet implemented — MIG-07): the .NET Gateway polls each downstream's `GET /openapi.json` asynchronously (works identically whether the downstream is still FastAPI or already ASP.NET Core, since both frameworks serve OpenAPI 3.x at a conventional path) and merges the results into the public aggregate schema. If a downstream is unreachable at poll time, the Gateway falls back to a build-time-generated static OpenAPI artifact checked into `docs/openapi/` (these already exist per-service, confirmed via `docs/openapi/interop_openapi.json`, `docs/openapi/cdisc_openapi.json` referenced in `01_system_topology.md` §4.4) rather than crashing gateway startup, which is what the current `importlib.import_module(f"apps.{name}.main")` mechanism (`apps/gateway/main.py:246`) would do the moment any service is no longer Python-importable in-process.

### 4.3 Auth/HMAC boundary during cutover

Both legacy Python destinations and new .NET destinations behind a given YARP route must accept the same `GATEWAY_SECRET`-signed HMAC header and the same Keycloak-issued JWT (`JWT_ALGORITHM=RS256`) — this is already guaranteed by `Cadence.Security`'s `HmacSigner` being a direct port of `packages/security/signing.py`'s canonical RFC 8785 JSON signing scheme, so no dual-auth-scheme bridge is required. The one auth-adjacent item requiring attention before cutover of any e-signature-bearing slice (Execution, Econsent, Designer) is MIG-09 (`sig_token` distributed-cache requirement) — a .NET destination must not silently accept the in-memory-fallback replay-prevention behavior in a multi-replica Production deployment.

### 4.4 Rollback

Because both destinations remain wired in the same `Clusters` block for the duration of a slice's migration window, rollback is a config-only change (repoint `ClusterId`'s active destination back to `legacy`) with no code deployment required — this is the operational justification for keeping the legacy Dockerfile and container image available until the .NET destination has run in Production for an agreed bake-in period per slice, not deleting it the moment the .NET build passes CI.
