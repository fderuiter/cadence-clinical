# ADR-2553: Schema-Linked Static API Reference, Enriched OpenAPI 3.1 Contracts, and Gateway Contract Export

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Amends:** [ADR-2226](2026-08-28-programmatic-vitepress-api-documentation-engine.md), [ADR-2510](2026-09-30-endpoint-permissions-from-the-permission-matrix.md)
- **Requirements:** PRD-SYS-001 (Regulated API boundary transparency, 21 CFR Part 11 signature/audit header contracts, and drift-gated developer documentation)

---

## 1. Context & Problem Statement

Following the .NET 10 platform migration and Clinical Kernel consolidation ([ADR-2244](2026-09-29-clinical-kernel-modular-monolith-architecture.md), [ADR-2601](2026-10-19-one-composition-host-for-the-kernel-and-its-modules.md)), Cadence's OpenAPI contracts (`docs/openapi/*.json`) and VitePress REST API reference (`docs/api/*.md`) exhibited six structural gaps:

1. **Missing Gateway & MCP contracts:** `apps/gateway` (including Part 11 step-up token issuance at `POST /api/v1/auth/signature-verification`) had no exported `gateway_openapi.json` because booting YARP in-memory exposed `{**catch-all}` proxy routes, and `IntelligenceMcp.cs` (`POST /api/v1/ai/mcp`) lacked OpenAPI metadata in `intelligence_openapi.json`.
2. **Flat, schema-less Markdown reference:** `ApiDocsGeneratorService` emitted only a 4-column method/path/operationId/summary table, discarding all `500+` `components.schemas` DTOs, path/query parameters, request bodies, RFC 7807 error responses, and `x-kernel-errors` codes.
3. **Undocumented security & GxP headers:** Exported specs lacked `BearerAuth` security schemes, `Permission:<resource:action>` / `SignedInCaller` metadata, rate-limit policy annotations, and regulated headers (`X-Sig-Token`, `Idempotency-Key`, `X-Change-Reason`).
4. **Operation metadata & response gaps:** Several Minimal API endpoints omitted `operationId`, `summary`, or typed `2xx` schemas, and `cdisc_openapi.json` appended `/api/v1` to `servers[].url` even though every path already starts with `/api/v1/...`.
5. **Indistinguishable maturity & stubs:** `ServiceTopology` did not record `Active` vs `Scaffold` maturity from `ARCHITECTURE.md`, and HTTP `501 Not Implemented` stubs (`ImagingSegmentationEndpoints`, `EsourceEndpoints`) looked identical to live endpoints.
6. **GitHub-redirected spec links:** `repoLinks.mjs` rewrote `docs/openapi/*.json` links to `github.com/.../blob/main/...` instead of serving downloadable JSON contracts from the static VitePress portal.

## 2. Decision Drivers & Constraints

- **Single source of truth & zero drift:** Every API reference page and TypeScript client (`apps/web/src/api/generated/*.ts`) must derive deterministically from live in-memory ASP.NET Core OpenAPI 3.1 documents verified by `pnpm cadence check openapi` and `pnpm cadence check api-clients`.
- **Static build-time Markdown without custom `.vue` files in `docs/`:** Repository path boundary rules (`PathPatternLinterService`) restrict `.vue` files to `apps/` and `packages/`, and static GitHub Pages hosting must not expose live `"Try It"` request execution against production or demo origins.
- **Explicit GxP & authority transparency:** Integrators and auditors must see exact `BearerAuth` requirements, `PermissionMatrix` capabilities, clinical study/site scopes, step-up `X-Sig-Token` enforcement, `Idempotency-Key` replay guards, and `X-Change-Reason` audit justifications on every operation.
- **Honest maturity & stub signaling:** Portal readers must immediately distinguish `Active` services/modules (`13` contracts) from `Scaffold` hosts (`7` contracts) and see explicit warnings on HTTP `501 Not Implemented` routes.

## 3. Options Considered

### Option 1: Enriched Shared OpenAPI Transformers + Native `<details>` Schema-Linked Markdown Generator + Static `/openapi/*.json` Portal Assets (Selected)

- **Overview:**
  - Add `ServiceMaturity` (`Active`, `Scaffold`) to `ServiceTopology.cs` (verified against `ARCHITECTURE.md` by `ServiceTopologyTests`).
  - Wire `CadenceOpenApiTransformers` via `CadenceTelemetryExtensions` and endpoint metadata helpers in `packages/security` (`EndpointHeaderMetadata`, `.WithChangeReasonHeader()`, `.WithIdempotencyKeyHeader()`) to emit `BearerAuth`, permissions, scopes, rate limits, and `X-Sig-Token` / `Idempotency-Key` / `X-Change-Reason` headers across all hosts, including `apps/gateway` (filtering out YARP `{**catch-all}` routes) and `POST /api/v1/ai/mcp`.
  - Complete missing `operationId`, `summary`, and typed `2xx` response metadata across Minimal API endpoints, fix `cdisc_openapi.json` root `servers` URLs and merged `securitySchemes`, and enforce non-empty `operationId` + `summary` in `OpenApiDriftValidatorService`.
  - Upgrade `ApiDocsGeneratorService` to emit static Markdown + native HTML `<details>` blocks with `#schema-...` anchor links, `Active`/`Scaffold` badges, `kernel.md` module grouping (`Kernel`, `Intelligence`, `Execution`), HTTP `501` stub callouts, and one deduplicated `## Schemas` catalog per page.
  - Copy top-level `docs/openapi/*.json` (excluding `legacy/`) to `docs/.vitepress/dist/openapi/` during `pnpm docs:build` without staging untracked files in `docs/`.
- **Pros:**
  - Zero runtime JS/Vue bundle overhead in `docs/`, full compatibility with `PathPatternLinterService` and strict portal CSP (`ADR-2497`), and 100% deterministic drift checking in pre-commit and CI.
  - Preserves `openapi-typescript` compatibility across all `20` `apps/web/src/api/generated/*.ts` contracts.
- **Cons:**
  - Increases generated Markdown size in `docs/api/*.md`, mitigated by collapsible `<details>` disclosure blocks and a single deduplicated schema appendix per service page.

### Option 2: Embed Swagger UI or Scalar Vue Island Components Inside `docs/`

- **Overview:** Mount an interactive OpenAPI viewer component inside VitePress pages that fetches `*_openapi.json` at runtime.
- **Pros:** Familiar interactive explorer UI.
- **Cons:** Violates repository `.vue` placement boundaries outside `apps/` and `packages/`, bloats static portal JS bundles, introduces live `"Try It"` execution surfaces that conflict with static CSP and GxP posture, and prevents VitePress local search and Markdown anchor validation from indexing schemas and operations.

### Option 3: Hand-Authored API Reference Supplements Alongside Summary Tables

- **Overview:** Keep the 4-column generator output and document headers, errors, and DTOs manually in prose pages.
- **Pros:** Minimal generator code changes.
- **Cons:** Guaranteed to drift from C# DTOs, `PermissionMatrix`, and `KernelErrors` as endpoints evolve.

## 4. Decision Outcome

- **Chosen Option:** Option 1 (Enriched Shared OpenAPI Transformers + Native `<details>` Schema-Linked Markdown Generator + Static `/openapi/*.json` Portal Assets).
- **Justification:** Enriching the ASP.NET Core OpenAPI 3.1 pipeline at the source benefits every downstream consumer—generated TypeScript clients, external integrators downloading `/openapi/*_openapi.json`, and portal readers browsing `docs/api/*.md`—while keeping all portal rendering static, searchable, CSP-safe, and drift-gated.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - All `20` service/module contracts (`318` per-contract paths / `301` unique paths in `cdisc_openapi.json`, `347` per-contract operations / `330` unique operations, `542` schemas) have `0` missing `operationId`s, `0` blank summaries, `0` empty `200` schemas, and complete `BearerAuth` + GxP header documentation.
  - `gateway_openapi.json` and `docs/api/gateway.md` document `POST /api/v1/auth/signature-verification` without leaking YARP proxy catch-alls, and `intelligence_openapi.json` / `docs/api/intelligence.md` document `POST /api/v1/ai/mcp`.
  - Both `docs/api/*.md` headers and the VitePress `REST API Reference` sidebar surface `Active` and `Scaffold` badges directly from `ServiceTopology`, and HTTP `501` stubs are prominently flagged.
- **Negative Impact / Technical Debt:**
  - New Minimal API endpoints must declare both `.WithName(...)` (`operationId`) and `.WithSummary(...)`; omitting either now fails `pnpm cadence check openapi`.
- **Mitigation Strategy:**
  - `OpenApiDriftValidatorService.FindOperationMetadataGaps` reports the exact spec file, HTTP verb, and route path whenever an endpoint lacks `operationId` or `summary`.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `packages/topology/src/ServiceTopology.cs`
  - `packages/telemetry/src/CadenceOpenApiTransformers.cs`, `packages/telemetry/src/CadenceTelemetryExtensions.cs`
  - `packages/security/src/Rbac/EndpointAuthorizationMetadata.cs`, `packages/security/src/DependencyInjection/CadenceSecurityServiceCollectionExtensions.cs`
  - `apps/gateway/src/Program.cs`, `apps/gateway/src/Signatures/SignatureTokenEndpoints.cs`, `apps/intelligence/src/Mcp/IntelligenceMcp.cs`, and Minimal API endpoint summaries/typed responses across `apps/*`
  - `packages/tooling_core/src/Cadence.OpenApiExporter/Program.cs`, `packages/tooling_core/src/Services/ApiDocsGeneratorService.cs`, `packages/tooling_core/src/Services/OpenApiDriftValidatorService.cs`
  - `docs/.vitepress/config.mjs`, `docs/.vitepress/sidebar.mjs`, `docs/.vitepress/repoLinks.mjs`, `docs/.vitepress/theme/custom.css`, `scripts/build-docs.js`
  - `docs/openapi/*.json`, `docs/api/*.md`, `docs/openapi/README.md`, `docs/web/API_MIGRATION.md`, `docs/DOCUMENTATION_INDEX.md`, `apps/web/src/api/generated/*.ts`
- **Verification Plan:**
  - `ServiceTopologyTests` verifies `Active` and `Scaffold` service classification parity with `ARCHITECTURE.md`.
  - `OpenApiDriftTests` verifies completeness gating (`operationId` and `summary`), `<details>` and `#schema-...` rendering, `100%` `components.schemas` coverage, `kernel.md` module grouping, `Active`/`Scaffold` badges, and `501` stub callouts.
  - `BuildDocsScriptTests` and `docs/.vitepress/navigation.test.mjs` verify `/api/gateway` indexing, `ServiceTopology` sidebar badges, and `/openapi/*.json` static portal asset publishing without leftover staged files.
  - `pnpm cadence check openapi`, `api-clients`, `adrs`, `doc-index`, `markdown`, `deid`, and `pnpm docs:build` pass with zero errors.
