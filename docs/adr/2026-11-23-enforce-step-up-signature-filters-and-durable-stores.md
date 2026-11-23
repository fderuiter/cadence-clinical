# ADR-2660: Enforce Step-Up Signature Filters and Durable Token Stores Across Regulated Standalone Routes

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-003, PRD-SUB-005, SRS-SYS-005

---

## 1. Context & Problem Statement

Under FDA 21 CFR Part 11 (§ 11.50 and § 11.200) and [ADR-2515](2026-10-15-step-up-tokens-are-single-use-across-instances-and-restarts.md), clinical and financial sign-off actions require a fresh, single-use step-up authorization token (`X-Sig-Token`, contract v3, 60-second lifetime) issued by `/api/v1/auth/signature-verification` and recorded in a durable PostgreSQL `consumed_signature_tokens` table.

Across five standalone services (`execution`, `ctms`, `payments`, `imaging`, and `interop`), twelve regulated endpoints either executed regulated sign-offs without verifying an `X-Sig-Token`, relied on process-local replay caches instead of durable PostgreSQL token-use stores, or accepted caller-supplied `SignerName` fields from the request body:

- **Execution (`apps/execution`):** `PATCH /api/v1/execution/subjects/{subjectId}/state` checked `SubjectAuthority.CanUnblind` and the 50-character clinical necessity reason when transitioning to `UNBLINDED`, but did not require a step-up `X-Sig-Token`. Non-unblinding transitions (`ENROLLED`, `WITHDRAWN`, `COMPLETED`) share the same route and must remain callable without step-up re-authentication.
- **CTMS (`apps/ctms`):** Delegation of authority sign-off (`POST /api/v1/ctms/doa/sign-off`), monitoring visit report sign-off (`POST /api/v1/ctms/monitoring-visits/{visit_id}/sign-off`), finalized visit letter compilation (`POST /api/v1/ctms/letters/compile` when `Finalize: true`), site greenlight certification (`POST /api/v1/ctms/startup/sites/{siteId}/greenlight/certify`), and country matrix batch certification (`POST /api/v1/ctms/startup/matrix/batch-certify`) lacked `RequireElectronicSignature` enforcement and a durable `ctms.consumed_signature_tokens` store. Letter compilation also fell back to `request.SignerName`.
- **Payments (`apps/payments`):** Remittance advice signing (`POST /api/v1/payments/invoices/{id}/remittance-advice/sign`) enforced `finance_officer` separation of duties but did not require a step-up `X-Sig-Token` or register `payments.consumed_signature_tokens`.
- **Imaging (`apps/imaging`):** Committee chair tie-breaker adjudication (`POST /api/v1/imaging/adjudications/{sessionId}/chair-adjudicate`) lacked `RequireElectronicSignature`, took `SignerName` from the request body when present, and had no `imaging.consumed_signature_tokens` store.
- **Interop (`apps/interop`):** Single and batch EHR eSource reconciliation acceptance and rejection (`POST /api/v1/interop/esource/reconcile/accept`, `batch-accept`, `reject`, and `batch-reject`) lacked `RequireElectronicSignature`, deterministic `batch_id` payload binding, and an `interop.consumed_signature_tokens` store.

## 2. Decision Drivers & Constraints

- **Driver 1 (21 CFR Part 11 Step-Up Enforcement):** Regulated sign-offs, certifications, adjudications, eSource reconciliations, and emergency unblinding transitions must fail closed with `401 REAUTHENTICATION_REQUIRED` before any database mutation when `X-Sig-Token` is absent, expired, replayed, or bound to a different user, route, semantic action, or batch payload.
- **Driver 2 (Mixed-Mode Route Compatibility):** Routes that serve both routine operational updates and regulated sign-offs (`PATCH /subjects/{subjectId}/state` and `POST /letters/compile`) must require step-up verification only when the request invokes the regulated transition (`UNBLINDED` or `Finalize: true`).
- **Driver 3 (Durable Multi-Instance Replay Prevention):** Every service mapping `ElectronicSignatureRequired` metadata must register `AddCadenceDurableSigTokenStore<TDbContext>()` over a schema-local `consumed_signature_tokens` table so `SigTokenUseStoreStartupCheck` succeeds outside `Development` and single-use `jti` consumption holds across instances and restarts.
- **Driver 4 (Verified Signer Attribution):** Persisted signature manifestations and audit logs must derive signer identity (`sub`) and username (`username`) strictly from the verified `X-Sig-Token` or HMAC-signed gateway principal, never from request body fields.

## 3. Options Considered

### Option 1: Split mixed-mode endpoints into separate URLs

- **Overview:** Introduce dedicated `/unblind` and `/letters/finalize` routes so every signed route uses an unconditional filter.
- **Pros:**
  - Avoids inspecting the request body inside an endpoint filter.
- **Cons:**
  - Breaks existing REST contracts, OpenAPI clients, and audit/history conventions where subject lifecycle transitions and letter compilation share canonical resource endpoints.

### Option 2: Conditional step-up filter extension, canonical `SigTokenBatchId`, and per-service durable PostgreSQL stores (Selected)

- **Overview:** Extend `ElectronicSignatureEndpointFilterExtensions` with `RequireElectronicSignatureWhen(...)` (emitting `ElectronicSignatureRequired` metadata for startup verification while resolving `SigTokenBinding?` per request), centralize batch target digest computation in `SigTokenBatchId.FromIds(...)` and `packages/ui/signing.js`, register `AddCadenceDurableSigTokenStore<TDbContext>()` with EF Core migrations in `ctms`, `payments`, `imaging`, and `interop`, and wire web API clients and modals to send `X-Sig-Token`.
- **Pros:**
  - Preserves existing endpoint URLs and non-regulated transitions (`ENROLLED`, `WITHDRAWN`, draft letter compilation) without extra round-trips.
  - Keeps startup fail-closed guarantees (`SigTokenUseStoreStartupCheck`) uniform across unconditional and conditional step-up routes.
  - Shares one deterministic ordinal-sorted comma-joined SHA-256 `batch_id` derivation across C# (`SigTokenBatchId.FromIds`) and JavaScript/TypeScript (`computeBatchTargetHash`).
- **Cons:**
  - Adds a `consumed_signature_tokens` table to four standalone service schemas until standalone modules consolidate into the kernel host.

## 4. Decision Outcome

- **Chosen Option:** Option 2.
- **Justification:** Extending `packages/security` with `RequireElectronicSignatureWhen` and `SigTokenBatchId.FromIds` closes every unauthenticated signing gap across all twelve routes while preserving mixed-mode endpoint semantics, enforcing fail-closed startup checks in non-Development environments, and guaranteeing atomic single-use `jti` consumption on PostgreSQL.

### Gated Routes and Canonical Bindings

| Service     | Endpoint                                                          | Condition             | `semantic_action`                       | `batch_id`                                      |
| :---------- | :---------------------------------------------------------------- | :-------------------- | :-------------------------------------- | :---------------------------------------------- |
| `execution` | `PATCH /api/v1/execution/subjects/{subjectId}/state`              | `target == UNBLINDED` | `execution.subject.unblind`             | None                                            |
| `ctms`      | `POST /api/v1/ctms/doa/sign-off`                                  | Always                | `ctms.doa.sign_off`                     | None                                            |
| `ctms`      | `POST /api/v1/ctms/monitoring-visits/{visit_id}/sign-off`         | Always                | `ctms.monitoring_visit.sign_off`        | None                                            |
| `ctms`      | `POST /api/v1/ctms/letters/compile`                               | `Finalize == true`    | `ctms.letter.compile`                   | None                                            |
| `ctms`      | `POST /api/v1/ctms/startup/sites/{siteId}/greenlight/certify`     | Always                | `ctms.greenlight.certify`               | None                                            |
| `ctms`      | `POST /api/v1/ctms/startup/matrix/batch-certify`                  | Always                | `ctms.greenlight.batch_certify`         | `SigTokenBatchId.FromIds(request.SiteIds)`      |
| `payments`  | `POST /api/v1/payments/invoices/{id}/remittance-advice/sign`      | Always                | `payments.remittance.sign`              | None                                            |
| `imaging`   | `POST /api/v1/imaging/adjudications/{sessionId}/chair-adjudicate` | Always                | `imaging.adjudication.chair_adjudicate` | None                                            |
| `interop`   | `POST /api/v1/interop/esource/reconcile/accept`                   | Always                | `interop.esource.accept`                | None                                            |
| `interop`   | `POST /api/v1/interop/esource/reconcile/batch-accept`             | Always                | `interop.esource.batch_accept`          | `SigTokenBatchId.FromIds(request.CandidateIds)` |
| `interop`   | `POST /api/v1/interop/esource/reconcile/reject`                   | Always                | `interop.esource.reject`                | None                                            |
| `interop`   | `POST /api/v1/interop/esource/reconcile/batch-reject`             | Always                | `interop.esource.batch_reject`          | `SigTokenBatchId.FromIds(request.CandidateIds)` |

## 5. Consequences & Trade-offs

- **Positive Impact:** All twelve regulated mutations reject missing, wrong-action, wrong-batch, wrong-subject, expired, or replayed `X-Sig-Token` headers with `401 REAUTHENTICATION_REQUIRED` prior to executing handler logic. Signer attribution on finalized CTMS visit letters and Imaging chair adjudications is cryptographically bound to the verified token/caller claims.
- **Negative Impact / Technical Debt:** `ctms`, `payments`, `imaging`, and `interop` each maintain a `consumed_signature_tokens` table in their respective PostgreSQL schemas alongside `execution`, `econsent`, `knowledge`, `safety`, and `tickets`.
- **Mitigation Strategy:** All stores reuse `ConsumedSignatureTokenConfiguration` and `PostgresSigTokenUseStore<TDbContext>` from `packages/database`, verified continuously by `pnpm cadence migrations verify` and `SigTokenUseStoreStartupCheck`.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `packages/security`: `ConditionalElectronicSignatureEndpointFilter`, `RequireElectronicSignatureWhen`, and `SigTokenBatchId.FromIds`.
  - `apps/execution`, `apps/ctms`, `apps/payments`, `apps/imaging`, `apps/interop`: endpoint step-up filters, verified signer attribution, `AddCadenceDurableSigTokenStore<TDbContext>()` registrations, and EF Core `AddConsumedSignatureTokens` migrations.
  - `packages/ui` and `apps/web`: `SEMANTIC_ACTIONS`, `computeBatchTargetHash`, `SignatureCaptureModal.vue`, typed API clients (`rtsm.ts`, `etmf.ts`, `interop.ts`, `paymentsApi.ts`, `imagingApi.ts`, `soaClient.ts`), clinical workflow components, and MSW demo handlers.
- **Verification Plan:**
  - `Cadence.UnitTests`: `RegulatedEndpointsStepUpTests` (missing token, wrong semantic action, tampered `batch_id`, valid token, second-use `jti` replay, mixed-mode bypasses, and spoofed `SignerName` attribution across all 12 routes) and `SigTokenUseStoreWiringTests` (Production startup fail-closed and durable store wiring checks).
  - `Cadence.IntegrationTests`: `DurableSigTokenStoresPostgresTests` (concurrent `Task.WhenAll` single-use `jti` replay rejection and expiry window enforcement across `CtmsDbContext`, `PaymentsDbContext`, `ImagingDbContext`, and `InteropDbContext` on PostgreSQL 16).
  - Frontend & Guardrails: `packages/ui` and `apps/web` Vitest suites (`session_logout_signing.test.js`, `regulatedStepUpClients.spec.ts`, `EhrReconciliationDrawer.spec.ts`, `SsuGreenlightConsoleMatrix.spec.ts`, `CtmsOperationsAndLetters.spec.ts`, `StudyCanvasRtsmWorkflows.spec.js`), `pnpm cadence migrations verify`, `pnpm cadence generate-api-docs`, and `pnpm cadence check`.
