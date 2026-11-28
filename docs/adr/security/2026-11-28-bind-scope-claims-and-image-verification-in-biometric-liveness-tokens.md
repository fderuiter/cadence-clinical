# ADR-SECURITY-20261128-bind-scope-claims-and-image-verification-in-biometric-liveness-tokens: Bind Scope Claims and Image Verification in Biometric Liveness Tokens

- **Status:** Accepted
- **Date:** 2026-11-28
- **Authors:** @fderuiter, Stitch Agent
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (21 CFR Part 11 electronic signature and step-up verification), SRS-SYS-005 (e-signature step-up, single use), SRS-EDC-003 (Part 11 signature manifest)
- **Extends:** [ADR-2660](../2026-11-23-biometric-liveness-hmac-key-and-single-use-replay-protection.md), [ADR-2514](../2026-09-30-the-tenant-comes-from-sign-in-with-no-fallback.md), [ADR-2661](../2026-11-22-consolidate-standalone-caller-and-clinical-scope-guards.md)

---

## 1. Context & Problem Statement

Under [ADR-2660](../2026-11-23-biometric-liveness-hmac-key-and-single-use-replay-protection.md), `POST /api/v1/ai/biometrics/verify-liveness` (`Cadence.Modules.Intelligence`) and `POST /api/v1/econsent/templates/{template_id}/versions/{version_index}/capture-consent` (`Cadence.Apps.Econsent`) share a deployment-wide 32-byte HMAC secret (`CADENCE_BIOMETRIC_LIVENESS_KEY`) and consume `liveness:{jti}` once in `econsent.consumed_signature_tokens`. Three authorization and input-verification gaps remained:

1. **Unscoped HMAC token claims:** `BiometricLivenessTokenData` bound only `sub` (`SubjectPseudonym`), `score`, `is_live`, `mode`, `iat`, `exp`, and `jti`, omitting `tenant_id`, `issuer_id`, `study_id`, and `site_id`. A token minted in one tenant could verify under the shared `CADENCE_BIOMETRIC_LIVENESS_KEY` when submitted to `capture-consent` in another tenant.
2. **Missing caller RBAC and participant ownership checks at issuance:** `BiometricEndpoints.MapBiometricEndpoints` declared only `RequireSignedInCaller` without `RequireTenantScope()`, and `VerifyLivenessAsync` permitted any signed-in caller (including `subject` participants) to request `fallback_mode: true` (`coordinator_fallback` with zero frames) or mint camera tokens for arbitrary `subject_pseudonym` values. The participant portal (`apps/subject-portal`) also exposed a direct coordinator fallback button.
3. **Unverified camera frame payloads:** When `fallback_mode` was `false`, `VerifyLivenessAsync` checked only `VideoFrames.Count > 0` without base64 decoding, byte bounds, or PNG/JPEG/WebP image header and dimension validation.

## 2. Decision Drivers & Constraints

- **Driver 1 (Strict tenant and clinical scope isolation):** Cross-tenant and cross-study/site token replay must fail closed without adding synchronous cross-service RPC calls between `econsent` and `kernel-api`.
- **Driver 2 (21 CFR Part 11 role separation, PRD-SYS-001):** Coordinator fallback (`coordinator_fallback`) must be restricted to clinical staff holding `econsent_consent:capture` (`crc` and `investigator`) within their assigned `study_id` and `site_id`, while participants (`subject`) may only verify camera liveness for their own `principal.UserId == subject_pseudonym`.
- **Driver 3 (Non-destructive pre-consumption validation, ADR-2660):** Scope mismatches on `capture-consent` must fail before `ISigTokenUseStore.TryRecordFirstUseAsync` so rejected requests do not burn valid single-use `liveness:{jti}` tokens.
- **Driver 4 (Ephemeral biometric frame handling):** Camera frames must be decoded strictly in bounded temporary memory (`ArrayPool<byte>.Shared`, zeroed immediately after header inspection) and never persisted to disk or logs.

## 3. Options Considered

### Option 1: Per-tenant HMAC keys or synchronous callback from `econsent` to `kernel-api`

- **Overview:** Provision distinct HMAC secrets per tenant or query an internal `kernel-api` token introspection store during `capture-consent`.
- **Pros:**
  - Isolates cryptographic material or centralizes state in one service.
- **Cons:**
  - Breaks self-service trial tenant onboarding without dynamic secret distribution and introduces runtime service-to-service coupling across standalone boundaries.

### Option 2: Signed scope claims in `BiometricLivenessTokenService` + issuance RBAC and temporary-memory image header validation (Selected)

- **Overview:** Bind `tenant_id`, `issuer_id`, and optional `study_id` / `site_id` (mandatory on `coordinator_fallback`) inside the HMAC-signed token payload, enforce `RequireTenantScope()`, `PermissionMatrix`, subject ownership, and bounded PNG/JPEG/WebP header checks in `BiometricEndpoints`, remove direct coordinator fallback from `apps/subject-portal`, and verify tenant/study/site claims in `EconsentApplicationService.ProcessCaptureConsentAsync` prior to `TryRecordFirstUseAsync`.
- **Pros:**
  - Enforces stateless cryptographic tenant, study, and site binding across services with zero cross-service calls.
  - Preserves ADR-2660 non-destructive validation and single-use `liveness:{jti}` replay protection.
  - Rejects non-image and zero-dimension payloads in bounded pooled memory without retaining biometric data.
- **Cons:**
  - Existing unsigned or tenant-less tokens are rejected fail-closed upon deployment (bounded to at most the 15-minute token TTL).

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Signed scope claims inside `BiometricLivenessTokenData` eliminate cross-tenant and cross-study/site replay under the shared `CADENCE_BIOMETRIC_LIVENESS_KEY` while keeping `econsent` verification stateless and non-destructive before single-use token consumption. Combining `RequireTenantScope()`, `econsent_consent:capture` RBAC, `subject` ownership enforcement, and temporary-memory PNG/JPEG/WebP header decoding closes unprivileged issuance bypasses end to end.

## 5. Consequences & Trade-offs

- **Positive Impact:** `POST /api/v1/ai/biometrics/verify-liveness` denies `fallback_mode: true` from non-clinical callers (`403`), denies cross-subject participant requests (`403`), and rejects malformed, non-image, zero-dimension, or oversized `video_frames` (`400`, capped at 10 frames, 2 MiB per frame, 8 MiB total in zeroed `ArrayPool<byte>` buffers). `capture-consent` fails closed (`400 ValidationProblem` on `liveness_token`) whenever `tenant_id` is blank or mismatched or bound `study_id`/`site_id` claims differ from the capture request, without consuming `liveness:{jti}`.
- **Negative Impact / Technical Debt:** Callers invoking `coordinator_fallback` must supply `study_id` and `site_id` matching their `ClinicalScopeGuard` assignments, and participants needing fallback assistance must have an authorized coordinator initiate fallback on their behalf.
- **Mitigation Strategy:** `apps/subject-portal` removes the participant-facing fallback button, provides plain-language coordinator assistance guidance, and sends valid PNG camera check frames with `fallback_mode: false`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/security` (`BiometricLivenessTokenService`), `apps/intelligence` (`BiometricEndpoints`, `IntelligenceModule`), `apps/econsent` (`EconsentApplicationService`), `apps/subject-portal` (`ConsentView.vue`, `consent.js`, `controls.js`, `handlers.js`), and OpenAPI artifacts (`docs/openapi/intelligence_openapi.json`, `apps/web/src/api/generated/intelligence.ts`).
- **Verification Plan:** Verified by `BiometricLivenessTests` (unit + `TestServer` HTTP wire suite), `EconsentEndpointsTests`, `StandaloneServiceScopeAuthorizationTests`, `Cadence.ArchitectureTests` (`K-XS-1`, route authorization sentinels), `apps/subject-portal/tests/portal_econsent.test.js`, and `cadence check` quality gates.
