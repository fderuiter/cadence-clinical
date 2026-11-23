# ADR-2552: Share Step-Up Verifier, Unify Web HTTP Transport, and Gate TypeScript Duplication

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (Operational authentication resilience, unified gateway HTTP transport, and automated TypeScript duplication guardrails), PRD-CRF-015 (21 CFR Part 11 electronic signature step-up verification and modal decoupling)

---

## 1. Context & Problem Statement

Following [ADR-2551](2026-11-20-share-session-logout-and-signature-primitives.md), both `apps/web` and `apps/subject-portal` shared `buildStepUpVerificationPayload` from `packages/ui`, yet several structural duplications and coupling points remained:

1. **Duplicated step-up HTTP dispatch & token checks:** Seven step-up flows across `apps/web` (`etmf.ts`, `soaClient.ts`, `econsent.ts`, `CtmsView.vue`, `CecAdjudicationConsole.vue`) and `apps/subject-portal` (`reconsent.js`, `signature.js`) separately resolved fallback `semantic_action` strings, posted to `POST /api/v1/auth/signature-verification`, and checked whether `sig_token` was a non-empty string.
2. **Unrelated domain client coupling on signature surfaces:** `apps/web/src/components/SignatureCaptureModal.vue` statically imported and provided `etmfService` even when callers such as `EcrfFormPanel.vue` supplied `onSign`, while `ProtocolTrainingConsole.vue` imported `soaClient` solely to call `verifySignature` with `batchId`, and `signatureBinding.ts` re-implemented Web Crypto SHA-256 hex hashing already exported by `packages/ui`.
3. **Split web `ApiError`, base URL resolution, and 401 retry logic:** `apps/web/src/api/client.ts` and `apps/web/src/api/apiClient.js` each declared an independent `ApiError` class (While `apps/web/src/utils/apiErrors.js` threw plain `Error` instances from `ensureOk`), `client.ts` duplicated `resolveGatewayBaseUrl()` from `demoMode.js`, and `authorization.js`, `client.ts`, and `apiClient.js` maintained three separate single-flight `401` refresh-and-retry implementations.
4. **TypeScript blind spot in `DuplicationDetectorService`:** `packages/tooling_core/src/Services/DuplicationDetectorService.cs` scanned `.js`, `.vue`, `.css`, and `.cs` files and failed only on `.cs` duplicates, leaving hand-written `.ts` files unscanned and ungated.

## 2. Decision Drivers & Constraints

- **21 CFR Part 11 step-up token integrity ([ADR-2259](2026-09-29-electronic-signature-step-up-re-authentication.md)):** Every step-up caller must send a validated v3 payload (`password`, `action`, `semantic_action`, optional `totp` and `batch_id`), preserve `isStepUpAuthFailure` non-logout guards on `401`, and reject missing or blank `sig_token` responses before invoking downstream signing mutations.
- **Cold-boot & route-chunk hygiene:** Live eCRF signing (`EcrfFormPanel.vue` -> `SignatureCaptureModal.vue`) and training delegation sign-off (`ProtocolTrainingConsole.vue`) must not eagerly import `etmf.ts` or `soaClient.ts`, while keeping public method signatures on `etmfService.verifySignature`, `soaClient.verifySignature`, `econsentService.verifySignature`, `apiClient`, and `services/api.js` intact.
- **Deterministic `instanceof ApiError` semantics:** All `apps/web` error boundaries checking `err instanceof ApiError` must match regardless of whether `client.ts`, `apiClient.js`, or `ensureOk` raised the error.
- **Zero-false-positive `.ts` duplication gating:** `DuplicationDetectorService` must scan and gate hand-written `.ts` files while excluding `*.d.ts`, `src/api/generated/**`, `packages/ui/src/types/cdash.ts`, and bare object-literal/destructuring shorthand property lists.

## 3. Options Considered

### Option 1: Share Step-Up Verifier in `packages/ui`, Unify `apps/web` Transport Primitives, and Enable Blocking `.ts` Duplication Gate (Selected)

- **Overview:**
  - Export `resolveStepUpSemanticAction` and `verifyStepUpSignature` from `packages/ui` (`signing.js`, `index.js`, `index.d.ts`) to resolve fallback `semantic_action`, build v3 payloads, invoke caller transport, and validate non-empty `sig_token` strings.
  - Add `apps/web/src/api/signatureApi.ts` over `verifyStepUpSignature`, `openApiClient`, and `unwrapResponse`, routing `etmfService.verifySignature`, `soaClient.verifySignature`, `econsentService.verifySignature`, `CtmsView.vue`, `CecAdjudicationConsole.vue`, `SignatureCaptureModal.vue`, and `ProtocolTrainingConsole.vue` through it (lazy-loading `etmf.ts` in `SignatureCaptureModal.vue` only when `onSign` is omitted) and reusing `sha256` from `ui` in `signatureBinding.ts`.
  - Add `verifyPortalSignature` in `apps/subject-portal/src/api/client.js` over `verifyStepUpSignature` and `dispatchApi` for `reconsent.js` and `signature.js`.
  - Define one `ApiError` class in `apps/web/src/utils/apiErrors.js` (re-exported by `client.ts` and `apiClient.js`), delegate `client.ts` `getBaseUrl()` to `resolveGatewayBaseUrl()`, and share `rebuildRequest` + `fetchWithUnauthorizedRetry` in `apps/web/src/api/authorization.js` across `installAuthorizedFetch`, `client.ts`, and `apiClient.js`.
  - Share demo USDM SoA fixtures (`demoUsdmFixtures.js`) and workspace role constants (`availability.js`), strip bare shorthand property lines in `DuplicationDetectorService.NormalizeLine`, and scan + gate hand-written `.ts` files in `DuplicationDetectorService.cs`.
- **Pros:**
  - Eliminates duplicated step-up verification, HTTP `401` retry, and `ApiError` definitions without changing wire contracts or migrating out-of-scope Python-era screens.
  - Prevents future TypeScript copy-paste drift via pre-commit and CI duplication checks.
- **Cons:**
  - Requires rebuilding `packages/ui/dist` whenever shared step-up helpers change.

### Option 2: Keep App-Local Step-Up Wrappers and Gate Only C# Duplication

- **Overview:** Leave step-up HTTP dispatch, `ApiError`, and `401` retry logic split across `apps/web` and `apps/subject-portal`, and restrict `DuplicationDetectorService` to `.cs` files.
- **Pros:**
  - Smaller immediate diff.
- **Cons:**
  - Preserves cross-domain module coupling (`SignatureCaptureModal` -> `etmf`, `ProtocolTrainingConsole` -> `soaClient`), `instanceof ApiError` mismatches between `client.ts` and `apiClient.js`, and unchecked `.ts` duplication.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing step-up execution and `sig_token` validation in `packages/ui`, unifying `ApiError` and single-flight `401` retry transport in `apps/web`, and gating hand-written `.ts` files in `DuplicationDetectorService` removes client drift while preserving every existing public API signature and runtime guardrail.

## 5. Consequences & Trade-offs

- **Positive Impact:** One tested verifier in `packages/ui` backs all seven step-up flows plus modal/console callers; `EcrfFormPanel.vue` and `ProtocolTrainingConsole.vue` no longer pull in `etmf.ts` or `soaClient.ts`; `err instanceof ApiError` holds across `client.ts`, `apiClient.js`, and `ensureOk`; and `cadence check duplication` blocks `.ts` regressions alongside `.cs`.
- **Negative Impact / Technical Debt:** Legacy screens that still call `apiClient.js` directly remain on `apiClient.js` until their backend endpoints migrate per `docs/web/API_MIGRATION.md`.
- **Mitigation Strategy:** `apiClient.js`, `client.ts`, and `installAuthorizedFetch` now share the exact same `ApiError` class, `resolveGatewayBaseUrl()` base resolution, and `fetchWithUnauthorizedRetry` refresh helper.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `packages/tooling_core`, `tests/Cadence.UnitTests`, `docs/adr/`.
- **Verification Plan:** Vitest suites with coverage gates in `packages/ui` (80% gate on `signing.js`), `apps/web` (ADR-2530 coverage floors), and `apps/subject-portal`, xUnit `DomainEngineUnitTests` verifying `.ts` duplication gating and generated/shorthand exclusions, `Cadence.ArchitectureTests`, and `pnpm cadence check --pre-commit`.
