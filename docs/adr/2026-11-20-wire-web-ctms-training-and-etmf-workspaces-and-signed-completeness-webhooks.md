# ADR-2821: Wire Web CTMS, Study Training, and eTMF Workspaces With Signed Completeness Webhooks

- **Status:** Accepted
- **Date:** 2026-11-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SSU-001, PRD-SSU-002, PRD-SSU-003, PRD-CTMS-001, PRD-ETMF-001, UX-9

---

## 1. Context & Problem Statement

While the `.NET` microservices for CTMS (`apps/ctms`), Study Training & Delegations (`apps/knowledge`), and eTMF (`apps/etmf`) already serve OpenAPI-specified GxP endpoints and YARP gateway routes, operational web workspaces remained partially disconnected:

- `ServiceTopology.cs` marked `ctms`, `knowledge`, and `etmf` as `Core: false`, excluding them from default AppHost / Compose core boot profiles, and `generate-api-types.js` did not emit typed TypeScript OpenAPI clients for those three services.
- `SsuGreenlightConsole.vue` used raw `fetch()` calls, synthesized fallback greenlight matrices and `hmac_sha256_token_` strings on HTTP errors (violating `UX-9` and `docs/ux/CLINICAL_CANVAS.md` §6.1), and targeted an unmounted document upload path instead of eTMF QC staging.
- `EtmfIntelligenceEndpoints.cs` transitioned staged documents through clinical QC (`POST /api/v1/etmf/intelligence/qc-review/{doc_id}`), but did not forward signed site document completeness snapshots to Execution (`POST /api/v1/execution/webhooks/etmf`), leaving unseeded sites such as `SITE-102` blocked at site activation even after essential documents were approved.
- `CtmsView.vue` gated live milestones, monitoring visits, compiled visit letters, and DoA tabs behind `demoBuild`, and `StudyCanvasView.vue` hid Start (`documents`, `greenlight`, `training`) and Conduct (`milestones`, `monitoring`) operational sections.

## 2. Decision Drivers & Constraints

- **End-to-End GxP Integrity (`UX-9`):** Web views must never fabricate greenlight states, approval statuses, or cryptographic tokens in the browser; all mutations and evaluations must flow through authenticated OpenAPI clients with `X-Change-Reason` and Part 11 `X-Sig-Token` headers.
- **Zero-Trust Service-to-Service Identity ([ADR-2256](2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md)):** Completeness updates sent from eTMF to Execution must carry an HMAC-SHA256 forwarded-identity header signed as `etmf_service` using `IClock` timestamps.
- **Strict Role Separation (`PermissionMatrix`):** Site activation (`site_compliance:activate`), greenlight certification (`ctms_greenlight:certify`), clinical QC transitions (`etmf_document:transition_clinical_qc`), CRA visit completion (`monitoring_visit:update`), supervisory visit sign-off (`monitoring_visit:sign_off`), and PI delegation sign-off must enforce role splits in both backend RBAC and disabled-control UI explanations.
- **Hide Unbacked Surfaces:** Tabs and API calls without `.NET` backend routes (financials, CRA workload, recruitment, RTSM, training progress/quiz heartbeats) must stay uncalled and hidden outside demo builds.

## 3. Options Considered

### Option 1: Promote `ctms`, `knowledge`, and `etmf` to Core Services, Generate OpenAPI Clients, and Publish Signed eTMF Completeness Webhooks (Selected)

- **Overview:** Mark `ctms`, `knowledge`, and `etmf` as `Core: true` in `ServiceTopology.cs`; generate `ctms.ts`, `etmf.ts`, and `knowledge.ts` via `generate-api-types.js`; wire `ctmsApi.ts`, `etmf.ts`, and `training.ts` (`protocol_version` query parameter) into standalone routes and Clinical Canvas `Start`/`Conduct` sections; add `EtmfCompletenessWebhookPublisher` in `apps/etmf` to compute site activation completeness against required startup artifacts (`04.01.02`, `05.02.02`, `05.02.03`, `05.02.12`) and POST `X-Cadence-Identity`-signed payloads as `etmf_service` to Execution; and grant `sponsor_dm` `etmf_document:transition_clinical_qc` alongside `cra` and `admin` in `PermissionMatrix.cs` so sponsor startup leads can complete document QC through site activation.
- **Pros:**
  - ✅ Enables unseeded sites (`SITE-102`) to progress from document staging and QC approval through greenlight certification, site activation, enrollment, milestones, monitoring visit findings, compiled PDF letters, and supervisory sign-off against live `.NET` services.
  - ✅ Preserves zero-trust service authentication on `/api/v1/execution/webhooks/etmf` and compile-time OpenAPI type safety across all three web clients.
  - ✅ Eliminates browser-synthesized greenlight matrices and tokens (`UX-9`).
- **Cons:**
  - ❌ Increases default core topology container count by three services in full-stack local runs.

### Option 2: Poll eTMF Directly From Execution During Site Activation

- **Overview:** Have `Execution` make synchronous HTTP calls back to `eTMF` during `POST /api/v1/execution/sites/{siteId}/activate` instead of receiving signed completeness webhooks.
- **Pros:**
  - ✅ Avoids outbound webhook publishing in `apps/etmf`.
- **Cons:**
  - ❌ Introduces synchronous runtime coupling from Execution to eTMF and bypasses Execution's existing audited `SiteDocumentCompleteness` ledger and `/api/v1/execution/webhooks/etmf` contract.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Promoting the three operational services to core topology, generating typed OpenAPI clients, and publishing `etmf_service`-signed completeness webhooks reuses Execution's existing compliance ledger and zero-trust identity verification while eliminating all synthetic browser fallbacks.

## 5. Consequences & Trade-offs

- **Positive Impact:** Full startup-to-conduct clinical operations work end-to-end against live `.NET` services with strict role gating and zero unmounted API calls outside demo mode.
- **Negative Impact / Technical Debt:** Investigator grant financials, CRA workload allocations, recruitment projections, and RTSM inventory remain hidden outside demo builds until their `.NET` endpoints land (#4829).
- **Mitigation Strategy:** `docs/web/API_MIGRATION.md`, `availability.js`, and `lifecycle.ts` explicitly scope live vs demo-only surfaces and enforce contract parity in automated tests.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/topology`, `packages/security`, `apps/apphost`, `apps/etmf`, `apps/web`, `docker/docker-compose.yml`, `docs/web/API_MIGRATION.md`, `tests/Cadence.UnitTests`.
- **Verification Plan:** Verified via `EtmfCompletenessWebhookTests`, `AppHostModelTests`, `pnpm --filter web run generate:api -- --check`, Vitest suites across CTMS/SSU/Training/eTMF/Canvas, and `cadence check`.
