# Forensic Gap Analysis & Phase 0 Migration Readiness Audit

**Document ID:** AUD-NET10-002 (Supersedes AUD-NET10-001)
**Project:** Cadence Clinical Research Software Platform
**Target Runtime:** .NET 10 (C# 14 / ASP.NET Core 10 Minimal APIs)
**Lead Auditor:** Principal Software Systems Forensic Auditor & Migration Lead
**Audit Date:** 2026-09-03
**Status:** BLOCKED — PRIOR VERDICT OVERTURNED

---

## Executive Summary

This is a re-audit of the Phase 0 discovery baseline (`SYSTEM_TOPOLOGY_AUDIT.md`, `DEPENDENCY_AND_PARITY_CATALOG.md`, `BOUNDED_CONTEXT_MIGRATION_MATRIX.md`) and of the prior forensic audit **AUD-NET10-001**, which certified the repository **"UNCONDITIONALLY APPROVED"** and **100% covered** across all five discovery domains.

That certification does not hold up. Cross-referencing AUD-NET10-001's own "resolved" checklist against the actual working tree shows that **every code-level defect it identified is still present in the codebase, byte-for-byte unchanged**. What was actually delivered was an Architecture Decision Record (ADR-2239) and prose additions to `CONTEXT.md` — a *decision to fix things later*, not a fix. AUD-NET10-001 then marked those decisions with ✔ as if the underlying risk had been closed, and its own "Verification" line cites only `validate_adrs.py` and `validate_markdown.py` — markdown linters, which cannot and do not verify runtime behavior. This is a **process-integrity defect**, not a discovery-coverage defect: the original `SYSTEM_TOPOLOGY_AUDIT.md` had, in fact, already found three of the four headline gaps (§3.5, §3.6, §3.7) before AUD-NET10-001 "discovered" them a second time and then closed them on paper only.

In addition, this audit found:

1. **A governing ADR that contradicts its own implementing artifact.** ADR-2239 §4 states three separate times that `Cadence.Quality` is reclassified to **Tier 2**. `BOUNDED_CONTEXT_MIGRATION_MATRIX.md` (the document that actually sequences migration) and AUD-NET10-001's own verdict both say **Tier 3**. No document explains the divergence. A Phase 1 planner reading the ADR and the matrix will get two different answers to "when does Quality migrate?"
2. **An undocumented arbitrary code-execution surface.** `apps/interop/domain/sce_sandbox_orchestrator.py` shells out to `sys.executable` and `Rscript` to run untrusted statistical scripts. This is absent from the topology audit's "Native Subprocess & External Daemon Bindings" section (which covers only `ffmpeg`/`clamd`), absent from the dependency/parity catalog, and absent from every Dockerfile — there is no R runtime provisioned anywhere in the container build chain the discovery documents describe.
3. **A newly-codified standard that conflicts with existing regulated logic.** `CONTEXT.md`'s "Universal Decimal Precision Standard" mandates `decimal` for "high-precision dosage/lab calculations," but `apps/quality/application/services/statistical_forensics_service.py` relies on `math.isnan()`/`math.isinf()` as legitimate sentinel values in its statistical forensics logic. `System.Decimal` has no NaN/Infinity representation. Applying the standard as literally written to that module is not a remediation — it is a new defect waiting to be introduced in Phase 1.
4. **Roughly half of the runtime configuration surface is undocumented.** 33 of the 62 environment variables in `.env.example` (53%) — including *every* inter-service `*_URL` variable (`EXECUTION_URL`, `GATEWAY_URL`, `CTMS_URL`, etc.), the entire `WEBHOOK_*` outbound-delivery subsystem, and the `NCI_EVS_*`/`TERMINOLOGY_*` external terminology API — do not appear in the topology audit's Configuration & Secret Matrix. The `*_URL` variables are the literal wiring of the inter-service dependency graph that the Bounded Context Migration Matrix's tier sequencing depends on; the matrix was not cross-validated against them.

Given that the previously-certified remediations do not exist in code, that the governing ADR and the migration sequencing matrix disagree with each other, and that a newly-adopted platform-wide standard has an unresolved conflict with live regulated logic, **Phase 1 slice migration should not proceed on the basis of AUD-NET10-001's verdict.**

---

## 1. Discovery Coverage Scorecard

| Domain / Area | Discovery Completeness (%) | Status | Key Missing Artifacts |
|---|:---:|:---:|---|
| **Entry Points & APIs** | 85% | 🟡 Warning | Service topology (FastAPI routers, ports) is well mapped, but the inter-service `*_URL` wiring that defines actual runtime coupling between entry points (`EXECUTION_URL`, `GATEWAY_URL`, `CTMS_URL`, `DESIGNER_URL`, `ECONSENT_URL`, `EISF_URL`, `ETMF_URL`, `INTEROP_URL`, `NOTIFICATIONS_URL`, `ORG_URL`, `QUALITY_URL`, `SAFETY_URL`, `TICKETS_URL`) is absent from the Configuration & Secret Matrix. |
| **Domain Logic & Rules** | 55% | 🔴 Blocked | `apps/payments/domain/models.py` remains 100% `float` (zero `decimal` usage confirmed across 30+ monetary fields); the Universal Decimal Precision Standard has no CI/static-analysis enforcement (no Roslyn banned-API rule, no EditorConfig gate); the standard's own wording conflicts with NaN/Infinity-dependent statistical logic in `apps/quality`. |
| **Persistence & State** | 75% | 🟡 Warning | Lazy-loaded 49-model registry in `apps/execution/database/models/__init__.py` (module-level `__getattr__` + `importlib`) not catalogued as a dynamic-import seam; Redis pub/sub (`econsent_cache_invalidation`, `econsent_consent_completed`) is a second, non-transactional messaging channel never reconciled against the "MassTransit Transactional Outbox" single-spine narrative in `index.md`. |
| **Background/Async Tasks** | 65% | 🔴 Blocked | `sce_sandbox_orchestrator.py`'s Python/R subprocess sandbox is entirely undocumented; `sig_token` replay-prevention silently degrades to per-process in-memory state when `REDIS_URL` is unset, breaking cross-instance replay protection with no flag raised anywhere in the discovery set. |
| **Infrastructure & CI** | 70% | 🟡 Warning | No Dockerfile provisions an R runtime despite `Rscript` being invoked at runtime; ADR-2239's cited "Validation" (`validate_adrs.py`, `validate_markdown.py`) is documentation linting, not a functional or code-level check; no CI gate exists to prevent regression of the decimal-precision or dynamic-import invariants it claims to codify. |
| **Audit Trail & Remediation Closure Integrity** (added) | 0% | 🔴 Blocked | AUD-NET10-001 marked 4 gaps (7 findings) as resolved/✔ with zero corresponding code changes; ADR-2239's Tier 2 decision contradicts the Tier 3 value actually recorded in `BOUNDED_CONTEXT_MIGRATION_MATRIX.md` and in AUD-NET10-001's own verdict section. |

---

## 2. Forensic Omissions & Gaps Register

### 2.1 Codebase Coverage & Orphan Analysis

**Gap A1 — Dynamic model registry not catalogued as a reflection seam.**
`apps/execution/database/models/__init__.py` defines module-level `__getattr__` plus `discover_models()`, both driven by `importlib.import_module(f".{submodule}", __name__)`, to lazily load and register **49** clinical SQLAlchemy models across the entire Execution service. Neither `SYSTEM_TOPOLOGY_AUDIT.md` §3 ("Dark Debt & Dynamic Behavior Audit") nor AUD-NET10-001 lists this as one of the repository's `importlib` sites — both stop at `packages/security/audit_logger.py` and `apps/gateway/main.py`. Python's module-level `__getattr__` has no direct C# equivalent (no dynamic namespace-level member resolution without explicit declaration); every one of the 49 models will need to be explicitly enumerated and registered in the EF Core `DbContext`, which is a materially different (and larger) transcription task than the two previously-known seams implied.

**Gap A2 — Undocumented arbitrary script-execution surface.**
`apps/interop/domain/sce_sandbox_orchestrator.py` runs untrusted Python (`sys.executable`) and R (`Rscript`, located via `shutil.which`) scripts through `asyncio.create_subprocess_exec`, with its own docstring describing "isolated subprocess with bounded timeouts and network isolation." This is a production statistical-computing-environment (SCE) sandbox, not a dev tool. It does not appear in `SYSTEM_TOPOLOGY_AUDIT.md` §3.8 (which documents only `ffmpeg`/`clamd`), does not appear in `DEPENDENCY_AND_PARITY_CATALOG.md`, and no Dockerfile in the repository (`docker/Dockerfile`, `apps/interop/Dockerfile`, or otherwise) installs an R runtime or `r-base` package. This is both a missed discovery item and evidence the current Python deployment may already lack the R runtime the code path assumes.

**Gap A3 — Configuration/secret matrix covers only ~47% of declared runtime configuration.**
Diffing `.env.example` (62 declared variables) against `SYSTEM_TOPOLOGY_AUDIT.md`'s Configuration & Secret Matrix shows 33 undocumented variables, including:
- Every inter-service base-URL variable: `GATEWAY_URL`, `EXECUTION_URL`, `CTMS_URL`, `DESIGNER_URL`, `ECONSENT_URL`, `EISF_URL`, `ETMF_URL`, `INTEROP_URL`, `NOTIFICATIONS_URL`, `ORG_URL`, `QUALITY_URL`, `SAFETY_URL`, `TICKETS_URL`.
- An entire outbound webhook subsystem: `WEBHOOK_URL`, `WEBHOOK_SIGNING_SECRET`, `WEBHOOK_TIMEOUT`.
- An external terminology integration: `NCI_EVS_BASE_URL`, `NCI_EVS_OFFLINE`, `NCI_EVS_TERMINOLOGY`, `TERMINOLOGY_CACHE_TTL`, `TERMINOLOGY_OFFLINE`.
- `ORG_DATABASE_URL` (the Org service's own database is not listed alongside the other nine `*_DATABASE_URL` entries that are documented).
- SMTP credential/transport detail (`SMTP_USERNAME`, `SMTP_PASSWORD`, `SMTP_SENDER`, `SMTP_USE_TLS`, `SMTP_USE_SSL`), `JWT_ALGORITHM`, `KEYCLOAK_ADMIN`/`KEYCLOAK_ADMIN_PASSWORD`, `LOG_LEVEL`, `BRAND_NAME`/`BRAND_DOMAIN`, `INBOUND_EMAIL_MAX_SIZE_BYTES`.

The `*_URL` set matters most for this audit: it is the actual, configured inter-service dependency graph. Bounded-context tier sequencing (§2.4 of this report) should be validated against it, and currently is not.

**Gap A4 — Migration/CLI subprocess wrappers not enumerated.**
`apps/ctms/migrate.py`, `apps/etmf/adapters/migrate.py`, `apps/etmf/database/migrate.py`, `apps/fileshare/migrate.py`, and `apps/quality/migrate.py` each shell out via `subprocess` to drive Alembic. These are lower-risk (operator-invoked, not request-path) but are exactly the "maintenance/migration scripts not assigned to a bounded context" category the audit brief calls out, and none are named in the "Operational Engineering Scripts" inventory (§1.3 of the topology audit), which lists only `scripts/*.py` at the repo root.

### 2.2 State, Persistence & Concurrency

**Gap B1 — Second, non-transactional messaging channel not reconciled with the outbox narrative.**
`docs/migration/index.md` states the platform standardizes on a single "MassTransit Transactional Outbox Event Spine" for background/async work. In the actual codebase, `apps/econsent/infrastructure/cache.py` and `apps/execution/workers/consent_subscriber.py` implement a **separate** Redis pub/sub bus (`REDIS_CHANNEL`/`econsent_cache_invalidation`, `REDIS_CHANNEL_CONSENT`/`econsent_consent_completed`) that is fire-and-forget with no outbox durability guarantee. The topology audit does document the Redis usage itself (§2.4), but no document in the migration set states whether this pub/sub mechanism is (a) folded into MassTransit, (b) replaced by a .NET pub/sub equivalent (e.g., Redis `StackExchange.Redis` pub/sub, SignalR backplane), or (c) retained as-is. The "Background/Async Tasks: 100% Pass" rating in AUD-NET10-001 treated the outbox pattern as the complete picture; it is not.

**Gap B2 — Replay-prevention state silently degrades under horizontal scaling.**
`REDIS_URL` (Security, eConsent) defaults to "falls back to in-memory dictionary" per the topology audit's own matrix (§1.4). A single-use `sig_token` replay-prevention mechanism that falls back to per-process memory provides **no replay protection at all** once more than one instance of a service is running — a distributed-state hazard directly relevant to a security control, and it is not flagged as a risk anywhere in the discovery or gap-analysis documents.

**Gap B3 — Threading sites outside the two already documented.**
`SYSTEM_TOPOLOGY_AUDIT.md` §3.4 documents thread-pool usage in `tests/conftest.py` and `apps/designer/db.py` only. Two additional sites were found: `packages/security/gateway_client.py` and `apps/designer/domain/cdisc/usdm_importer.py`. The first is notable because `packages/security` (`Cadence.Security`) is listed in `BOUNDED_CONTEXT_MIGRATION_MATRIX.md` as a **Tier 1 leaf node already completed** in the tracer bullet — a blocking-call hazard in a component certified "done" was not verified before that certification.

### 2.3 Edge Case & Data Precision Invariants

**Gap C1 — Decimal-precision remediation was scoped to `apps/payments` only; the codified standard claims broader coverage.**
`CONTEXT.md`'s "Universal Decimal Precision Standard" text explicitly covers "high-precision dosage/lab calculations," but every code change and every gap finding related to it (ADR-2239, AUD-NET10-001 Gap 1) references only `apps/payments/domain/models.py`. A repository scan found **46 files under `apps/execution`** with `float`-typed fields tied to labs, lab ranges, RTSM adaptive dosing, CEC adjudication, and lab transport models (e.g., `apps/execution/rtsm_adaptive.py`, `apps/execution/adaptive_math.py`, `apps/execution/domain/lab_models.py`, `apps/execution/domain/lab_transport_models.py`, `apps/execution/domain/cec_models.py`) — none audited against the standard.

**Gap C2 — The standard as worded conflicts with existing statistical logic.**
`apps/quality/application/services/statistical_forensics_service.py` (lines 112, 191) explicitly checks `math.isnan(val)` and `math.isinf(val)` as part of its statistical anomaly/outlier detection. `System.Decimal` in .NET **cannot represent NaN or Infinity** — there is no equivalent sentinel. If "high-precision dosage/lab calculations" in `CONTEXT.md` is read to include this module (it is not excluded), applying the Universal Decimal Precision Standard literally will silently break this detection logic during transcription — likely via a runtime exception on the values that currently signal "no result," rather than a clean fallback. This needs an explicit scope carve-out (statistical/analytical computation should very likely remain `double`, with `decimal` reserved for ledger/dosage-record values, not intermediate statistical arithmetic) that does not currently exist in any codified standard.

**Gap C3 — No enforcement mechanism exists for the standard it claims to have "codified."**
No `BannedSymbols.txt`, Roslyn analyzer configuration, or `.editorconfig` rule prohibiting `float`/`double` in financial/dosage contexts was found anywhere in the repository. The standard is prose only. Absent an enforcement gate, nothing prevents a Phase 1 slice from reintroducing `float` on a monetary or dosage field and passing CI.

**Gap C4 — Custom JSON encoders not individually cross-walked.**
Nine-plus files define custom JSON encoding/serialization logic (`apps/interop/domain/cdr/dataset_json_compiler.py`, `apps/safety/services/regulatory_audit_ledger.py`, `apps/execution/services/cec_adjudication_service.py`, `packages/engine_core/derived/models.py`, `packages/engine_core/timeline/models.py`, `packages/engine_core/toxicity/models.py`, among others). `DEPENDENCY_AND_PARITY_CATALOG.md` should confirm each has a named `System.Text.Json` converter equivalent (custom converter, `JsonPolymorphic` attribute, etc.); this was not independently verified file-by-file and should be treated as an open item, not an assumed pass.

### 2.4 Migration Slicing & Dependency Order Validation

**Gap D1 — The governing ADR and the implementing sequencing matrix disagree on `Cadence.Quality`'s tier.**
ADR-2239 (titled, in part, "...and Tier 2 Quality Realignment") states in its Options (§3.C.1), Decision Outcome (§4.3), and even its own title that `Cadence.Quality` is **reclassified to Tier 2**. `BOUNDED_CONTEXT_MIGRATION_MATRIX.md` line 184 sequences it in **Tier 3**, and AUD-NET10-001's verdict (line 121) also asserts **Tier 3**, directly contradicting the remediation action item on the line immediately above it ("Move `Cadence.Quality` from Tier 1 to **Tier 2**"). No document explains or reconciles the discrepancy. This is not a rounding error — Tier 2 vs. Tier 3 changes what a Phase 1 planner believes must be migrated first.

**Gap D2 — The "false leaf node" certified as fixed is not actually fixed; the Strangler Fig proxy risk it protects against is unresolved.**
`apps/gateway/main.py` line 246 still contains `importlib.import_module(f"apps.{name}.name")`-style in-process aggregation, unchanged from the version AUD-NET10-001 examined. ADR-2239's stated decision (poll downstream `/openapi.json` over HTTP, with static-schema fallback) has not been implemented anywhere in code. This directly threatens ADR-2239's own stated decision driver: "the gateway must route traffic and publish API documentation transparently whether a downstream service is running on Python FastAPI or .NET 10 Minimal APIs." As currently shipped, the **first** Python service replaced by a .NET 10 service will break gateway OpenAPI aggregation exactly as originally warned in Gap 3 of AUD-NET10-001 — the risk was documented and then left open, not resolved.

**Gap D3 — `Cadence.Security`'s Tier 1 "leaf node, completed" status was not verified against its behavioral reference.**
`packages/security/audit_logger.py` still performs the exact upward `importlib` reflection into `apps.execution`, `apps.eisf`, `apps.etmf`, and `apps.quality` that Gap 2 of AUD-NET10-001 identified as "architectural debt." `Cadence.Security` is simultaneously listed as a **Tier 1 leaf/prerequisite already completed** in the tracer bullet (`BOUNDED_CONTEXT_MIGRATION_MATRIX.md` §Tier 1). If the C# `Cadence.Security` tracer-bullet implementation genuinely has zero downstream dependency (as a true leaf node must), then it necessarily diverges in behavior from its Python reference implementation on this exact point, and that divergence is undocumented — no parity note explains how audit-session resolution is handled differently, or confirms the C# implementation was verified against the documented Python behavior rather than assumed equivalent.

**Gap D4 — Inter-service dependency wiring (`*_URL` variables) was not used to validate the tier graph.**
As noted in Gap A3, the `*_URL` environment variables constitute the actual configured service-to-service coupling. `BOUNDED_CONTEXT_MIGRATION_MATRIX.md`'s dependency graph should be traceable 1:1 against which services hold a reference to which `*_URL` variable (this is exactly how Gap 4/D2's `EXECUTION_URL` dependency in `apps/quality` was originally found). No evidence exists that this cross-check was performed for the other 12 services now sequenced into Tiers 1–3.

---

## 3. Remediation Action Items

1. **Retract or correct AUD-NET10-001's verdict in the migration hub.** `docs/migration/index.md` currently links to a document that is factually wrong about remediation status. At minimum, mark AUD-NET10-001 as superseded by this document (AUD-NET10-002) in the catalog table.
2. **Reconcile the Tier 2/Tier 3 contradiction for `Cadence.Quality`.** Either amend `BOUNDED_CONTEXT_MIGRATION_MATRIX.md` to Tier 2 to match ADR-2239's decision, or issue an ADR amendment explaining and formally adopting Tier 3. Do not leave the two documents disagreeing.
3. **Implement, not just decide, the three ADR-2239 code changes**, each with a passing automated test as evidence of closure (not a markdown-lint pass):
   - Convert `apps/payments/domain/models.py` monetary fields to `decimal.Decimal`, with a unit test asserting no `float` remains in the module (or the equivalent for whatever language the slice transcribes into).
   - Replace `apps/gateway/main.py`'s `importlib.import_module` OpenAPI aggregation with HTTP polling of downstream `/openapi.json` (or the static-schema fallback), with an integration test that stubs one downstream as non-Python-importable and confirms aggregation still succeeds.
   - Replace `packages/security/audit_logger.py`'s upward `importlib` reflection with the stated `ISaveChangesInterceptor`-equivalent pattern, or, if the Python codebase itself will not be touched pre-transcription, explicitly document that this remediation applies only to the *target* C# implementation and add a parity test that pins the Python behavior it must replicate.
4. **Add an enforcement gate for the Universal Decimal Precision Standard** (banned-API analyzer, `.editorconfig` rule, or equivalent) before any slice touching financial or dosage fields is merged.
5. **Scope the Universal Decimal Precision Standard explicitly.** Determine, in writing, whether `apps/quality/application/services/statistical_forensics_service.py` and the 46 `apps/execution` files identified in Gap C1 are in-scope for `decimal`, and if any are excluded (e.g., statistical/analytical intermediate computation), state that exclusion in `CONTEXT.md` rather than leaving "high-precision dosage/lab calculations" ambiguous.
6. **Document `apps/interop/domain/sce_sandbox_orchestrator.py`** in `SYSTEM_TOPOLOGY_AUDIT.md` §3.8 and in `DEPENDENCY_AND_PARITY_CATALOG.md`. Confirm whether an R runtime is actually provisioned in any deployed environment today, and define the target-state approach (managed R interop, a sidecar, or removal of the R execution path) before this slice is scheduled.
7. **Backfill the Configuration & Secret Matrix** with the 33 missing `.env.example` variables identified in Gap A3, and cross-reference every `*_URL` variable against the consuming service's row in `BOUNDED_CONTEXT_MIGRATION_MATRIX.md`.
8. **Resolve or explicitly accept the Redis pub/sub vs. MassTransit outbox duality** (Gap B1): state whether `econsent_cache_invalidation`/`econsent_consent_completed` migrate onto the outbox spine or remain a separate mechanism in .NET 10, and update the "Background/Async Tasks: 100%" scorecard claim accordingly.
9. **Verify the `Cadence.Security` Tier 1 tracer-bullet implementation** against the Python `audit_logger.py` reflection behavior (Gap D3) and record the parity decision (replicate vs. intentionally diverge) in an ADR.
10. **Address the `REDIS_URL` in-memory fallback for `sig_token` replay prevention** (Gap B2): either require Redis in production via a fail-closed check (consistent with the `APP_ENV=production` fail-closed philosophy already stated for other secrets), or explicitly accept the degraded guarantee in writing.

---

## 4. Phase 1 Readiness Verdict

### **BLOCKED**

Phase 1 (Slice Deep-Dive) should not begin under the prior "UNCONDITIONALLY APPROVED" certification. Blocking issues:

1. **Zero of the four/seven previously-identified code defects have been remediated in code.** `apps/payments` is still `float`-only, `packages/security/audit_logger.py` and `apps/gateway/main.py` still perform the exact `importlib` reflection originally flagged, and the OpenAPI-aggregation risk to the Strangler Fig gateway is live, not mitigated.
2. **The governing ADR-2239 and the Bounded Context Migration Matrix disagree** on `Cadence.Quality`'s migration tier (Tier 2 vs. Tier 3), with no reconciling document. Sequencing decisions made from either source alone will be wrong relative to the other.
3. **A newly-codified, platform-wide standard (Universal Decimal Precision) has an identified, unaddressed conflict** with live NaN/Infinity-dependent statistical logic in a regulated (CSM/statistical forensics) module, and has no enforcement mechanism to prevent it from being silently violated or silently mis-applied during Phase 1 transcription.
4. **A production arbitrary-script-execution surface (`sce_sandbox_orchestrator.py`, Python + R subprocess execution) is entirely absent from the discovery baseline**, including from the infrastructure/Dockerfile inventory that would need to provision its OS-level runtime dependency in .NET 10.
5. **Roughly half of the platform's declared runtime configuration, including the entire inter-service URL topology that the migration-tier dependency graph should be validated against, is undocumented**, leaving Dimension 4 (Migration Slicing & Dependency Order Validation) without a verifiable evidence trail.

Phase 1 may proceed once Action Items 1–4 (and their associated code-level evidence, not documentation-only closure) are complete, and once Action Items 6–7 close the two undocumented-surface gaps (SCE sandbox, configuration matrix). Action Items 5, 8, 9, and 10 should be resolved before the specific slices they affect (`Cadence.Payments`/dosage-bearing slices, async/eConsent slices, `Cadence.Security`, and `Cadence.Security`/eConsent respectively) are scheduled, but do not block Phase 1 kickoff on unrelated slices.
