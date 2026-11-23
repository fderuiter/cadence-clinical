# ADR-2658: Share Step-Up Signature Dispatch, Web HTTP Core, and TypeScript Duplication Enforcement

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (21 CFR Part 11 electronic signature step-up re-authentication), PRD-SEC-001 (OIDC session refresh resilience)

---

## 1. Context & Problem Statement

Following the extraction of `buildStepUpVerificationPayload` and `SEMANTIC_ACTIONS` into `packages/ui/signing.js` ([ADR-2551](2026-11-20-share-session-logout-and-signature-primitives.md)), live step-up verification callers across `apps/web` and `apps/subject-portal` still duplicated `POST /api/v1/auth/signature-verification` dispatch, `semantic_action` fallback resolution, and `sig_token` response checks, while `apps/web/src/api/econsent.ts` retained an unused `verifySignature` method. In `apps/web`, `SignatureCaptureModal.vue` statically imported `etmfService` across 10 signing views, `ProtocolTrainingConsole.vue` imported `soaClient` solely for `batchId` support, and `signatureBinding.ts` duplicated `sha256`. At the HTTP transport layer, `apps/web` maintained two distinct `ApiError` classes (`utils/apiErrors.js` and `api/client.ts`), separate gateway base-URL helpers, three HTTP `401` single-flight retry implementations across `authorization.js`, `client.ts`, `apiClient.js`, `kernel/shared.ts` (`streamKernelDownload`), and `ingestionClient.ts` (`uploadProtocol`), plus a legacy `services/api.js` re-export shim. Meanwhile, `DuplicationDetectorService` scanned `.cs`, `.js`, `.vue`, and `.css` files while skipping hand-written `.ts` modules.

## 2. Decision Drivers & Constraints

- **Single Part 11 Step-Up Dispatch Contract (PRD-SYS-001, ADR-2259, ADR-2551):** Centralize `STEP_UP_VERIFICATION_PATH`, `resolveDefaultSemanticAction`, and `requestStepUpSignature` in `packages/ui/signing.js` while preserving caller-specific contracts — notably `ticketsApi.verifyTicketSignature` raw response pass-through (`{ requireSigToken: false }`) and `isStepUpAuthFailure` session protection in `apps/web/src/api/authorization.js` and `apps/subject-portal/src/api/client.js`.
- **Domain-Decoupled Signing Bundles:** Prevent non-eTMF and non-SoA views mounting `SignatureCaptureModal.vue` or `ProtocolTrainingConsole.vue` from pulling `etmf.ts` or `soaClient.ts` into their chunks, lazy-loading `etmfService.signDocument` only when legacy callers omit `onSign`.
- **Unified Web HTTP Error & 401 Retry Core (PRD-SEC-001):** Ensure `apiClient.js`, `client.ts`, and `streamKernelDownload` throw one `ApiError` class (`instanceof ApiError`), resolve gateway URLs via `resolveGatewayBaseUrl()`, and share one single-flight `401` refresh-and-retry helper (`fetchWithSingleFlightRetry`) across JSON, streaming, and `FormData` upload calls.
- **Zero-Noise TypeScript Duplication Gate:** Scan and enforce 15-line duplication checks across hand-written `.ts` files under `apps/` and `packages/` while excluding generated OpenAPI clients (`/generated/`), `.d.ts` declarations, `apps/web/src/api/types.ts`, and `packages/ui/src/types/cdash.ts` ([ADR-328](2026-09-11-preservation-of-cdash-type-definition-pristine-state.md)).

## 3. Options Considered

### Option 1: Centralize Step-Up Dispatch in `ui`, Unify Web HTTP Retry/`ApiError` Plumbing, and Enforce Hand-Written `.ts` Duplication Checks (Selected)

- **Overview:** Add `STEP_UP_VERIFICATION_PATH`, `resolveDefaultSemanticAction`, and `requestStepUpSignature` to `packages/ui/signing.js` (re-exported from `packages/ui/index.js` and `index.d.ts`); add thin app adapters in `apps/web/src/api/signatureVerification.ts` and `apps/subject-portal/src/api/signatureVerification.js` and route all 8 live callers through them; delete unused `econsentService.verifySignature` and `apps/web/src/services/api.js`; decouple `SignatureCaptureModal.vue` and `ProtocolTrainingConsole.vue` from `etmfService` and `soaClient` and reuse `sha256` from `"ui"` in `signatureBinding.ts`; unify `ApiError` in `apps/web/src/utils/apiErrors.js` and `fetchWithSingleFlightRetry` in `apps/web/src/api/authorization.js`; deduplicate existing TypeScript clones in `clinicalUsdm.ts`/`demoHandlers.js` and `palette.ts`/`onboarding.js`; and extend `DuplicationDetectorService` and `QualityGates` to scan and enforce hand-written `.ts` files.
- **Pros:**
  - ✅ Routes every clinician and participant step-up verification call through one shared primitive without changing public domain client signatures or ticket pass-through behavior.
  - ✅ Consolidates `ApiError` (`instanceof` parity), gateway base-URL resolution, and single-flight `401` token refresh across OpenAPI, legacy JSON, streaming download, and `FormData` upload paths.
  - ✅ Eliminates static `etmf.ts`/`soaClient.ts` coupling from shared signing UI and enforces 15-line duplication checks on hand-written TypeScript with zero generated-file false positives.
- **Cons:**
  - ❌ Requires rebuilding `packages/ui` (`pnpm --filter ui build`) and regenerating CLI and documentation index artifacts.

### Option 2: Keep App-Local Step-Up Wrappers and Add TypeScript Scanning Without Transport Consolidation

- **Overview:** Enable `.ts` scanning in `DuplicationDetectorService` and deduplicate only blocks exceeding 15 normalized lines while leaving per-client step-up wrappers, dual `ApiError` classes, and separate `401` retry loops intact.
- **Pros:**
  - ✅ Smaller diff across `apps/web/src/api/`.
- **Cons:**
  - ❌ Leaves 8 step-up callers, dual `ApiError` classes (`instanceof` mismatches), three `401` retry paths, static `etmfService`/`soaClient` bundle coupling, and the dead `services/api.js` shim in place.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Sharing `requestStepUpSignature` in `packages/ui`, unifying `ApiError` and single-flight `401` retries in `apps/web`, removing dead shims and domain-client bundle coupling, and enforcing hand-written `.ts` duplication checks in `DuplicationDetectorService` eliminate client drift at both runtime and CI gate layers while preserving all existing API and GxP contracts.

## 5. Consequences & Trade-offs

- **Positive Impact:** Clinician and participant step-up flows share one payload builder, semantic-action resolver, endpoint constant, and `sig_token` validator; `SignatureCaptureModal.vue` and `ProtocolTrainingConsole.vue` avoid unrelated domain client imports; all web gateway wrappers throw a single `ApiError` class and retry expired bearer tokens once via `fetchWithSingleFlightRetry`; and `cadence check duplication` enforces hand-written `.ts` files alongside `.cs` and `.js`.
- **Negative Impact / Technical Debt:** Legacy screens that still call `apps/web/src/api/apiClient.js` remain on that wrapper until their domain endpoints migrate to generated OpenAPI slices ([docs/web/API_MIGRATION.md](../web/API_MIGRATION.md)).
- **Mitigation Strategy:** Guarded by unit tests in `packages/ui`, `apps/subject-portal`, `apps/web` (including cold-boot bundle and chunk-split specs), `DomainEngineUnitTests`, and `cadence check duplication`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `packages/tooling_core`, `packages/cli`, `tests/Cadence.UnitTests`, `docs/cli.md`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test`, `pnpm --filter subject-portal test`, `pnpm --filter web test`, `pnpm --filter web typecheck`, `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj`, `pnpm cadence generate-cli-docs --check`, and `pnpm cadence check duplication`.
