# ADR-2660: Enforce Configured Biometric Liveness HMAC Key and Single-Use Replay Protection

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter, Stitch Agent
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (21 CFR Part 11 electronic signature and step-up verification), SRS-SYS-005 (e-signature step-up, single use), SRS-EDC-003 (Part 11 signature manifest)
- **Extends:** [ADR-2260](2026-09-29-no-default-credentials-and-fail-closed-secret-resolution.md), [ADR-2494](2026-09-30-field-level-encryption-and-keyed-lookup-hashes-for-secrets-at-rest.md), [ADR-2515](2026-10-15-step-up-tokens-are-single-use-across-instances-and-restarts.md)

---

## 1. Context & Problem Statement

Remote electronic consent capture (`POST /api/v1/econsent/templates/{template_id}/versions/{version_index}/capture-consent` and `POST /api/v1/econsent/templates/{template_id}/capture-consent`) requires a short-lived biometric liveness token (`<base64url-payload>.<base64url-hmac>`) minted by `POST /api/v1/ai/biometrics/verify-liveness` in the Intelligence module. Three security gaps existed in how `BiometricLivenessTokenService` and the hosts handled these tokens:

1. **Embedded default HMAC secret:** `BiometricLivenessTokenService` contained an embedded `DefaultSecret`, and both `BiometricEndpoints` (minting) and `EconsentEndpoints` (verification) invoked `IssueToken` and `VerifyToken` without passing a configured secret. Any party with source visibility could forge arbitrary biometric liveness tokens with `IsLive = true` and `AntiSpoofScore = 0.99m`.
2. **Unbounded token lifetime and future `iat`:** `VerifyToken` checked only `currentTime > expires` and `!parsed.IsLive`, without bounding `ExpiresAtUnix - IssuedAtUnix` to the 15-minute contract or rejecting future `IssuedAtUnix` timestamps beyond clock skew.
3. **Replayable liveness tokens across consent versions:** `EconsentEndpoints` verified the token and inserted a `BiometricLivenessVerification` row, but never consumed the token's `Jti` in `ISigTokenUseStore` and mapped `BiometricLivenessVerification.LivenessTokenHash` with a non-unique index. A captured `liveness_token` remained reusable across re-consent versions throughout its validity window.

## 2. Decision Drivers & Constraints

- **No default credentials (ADR-2260, ADR-2490):** Cryptographic keys must come from configuration (`CADENCE_BIOMETRIC_LIVENESS_KEY`, base64 of 32 random bytes) and fail host startup when missing or malformed.
- **Bounded acceptance window (K-TIME-3, ADR-2515):** Liveness tokens must enforce a maximum 15-minute lifetime (`ExpiresAtUnix - IssuedAtUnix <= 900s`) and 5-second clock skew (`SigTokenVerifier.ClockSkew`) evaluated strictly against `IClock`.
- **Durable single-use consumption across instances and restarts (ADR-2515):** Consuming a liveness token during consent capture must atomically prevent replay across replicas and restarts, without burning a caller's token when pre-commit request validation or study/site authorization fails.

## 3. Options Considered

### Option 1: Reuse `GATEWAY_SECRET` for biometric liveness tokens

- **Rejected:** `GATEWAY_SECRET` authenticates gateway-to-service caller headers across all 18 services. Domain separation (as established by `ADR-2494` and `ADR-2515` for `FIELD_ENCRYPTION_KEY` and `CADENCE_SCE_SIGNING_KEY`) requires a dedicated 32-byte key shared only between the issuing host (`kernel`) and the verifying host (`econsent`).

### Option 2: Dedicated `CADENCE_BIOMETRIC_LIVENESS_KEY` + bounded lifetime + durable `ISigTokenUseStore` and unique `LivenessTokenHash` (selected)

- **Selected:** Introduce fail-closed `BiometricLivenessOptions` (`CADENCE_BIOMETRIC_LIVENESS_KEY`), remove `DefaultSecret`, enforce the 15-minute acceptance window with 5-second skew in `BiometricLivenessTokenService`, consume `"liveness:" + jti` through `ISigTokenUseStore.TryRecordFirstUseAsync` after request and scope checks pass, and add a unique database index on `BiometricLivenessVerification.LivenessTokenHash` as defense in depth.

### Option 3: Database lookup on `biometric_liveness_verifications` alone

- **Rejected:** Relying on a post-query `SaveChangesAsync` unique constraint alone surfaces concurrent replays as unhandled `DbUpdateException` errors rather than a clean `400 Bad Request` validation problem, whereas `ISigTokenUseStore.TryRecordFirstUseAsync` provides deterministic atomic single-use rejection backed by `econsent.consumed_signature_tokens` with the unique `LivenessTokenHash` index as a secondary persistence invariant.

## 4. Decision Outcome

1. **Keyed-only `BiometricLivenessTokenService`:**
   - Remove `DefaultSecret` and require an explicit 32-byte `secret` parameter on both `IssueToken` and `VerifyToken` (`ArgumentException` on null or length != 32).
   - Require a non-whitespace `Jti` (`<= 119` chars so `"liveness:" + jti` fits within `consumed_signature_tokens.token_id`'s 128-character limit) and enforce `WithinAcceptanceWindow`: `ExpiresAtUnix > IssuedAtUnix`, `ExpiresAtUnix - IssuedAtUnix <= 15 minutes`, `now >= IssuedAtUnix - 5s`, and `now < ExpiresAtUnix + 5s`.
2. **Startup-validated `BiometricLivenessOptions`:**
   - Add `BiometricLivenessOptions` (`ConfigurationKey = "CADENCE_BIOMETRIC_LIVENESS_KEY"`) and `AddCadenceBiometricLiveness()` in `Cadence.Security.Authentication`, validated on startup via `FieldProtectionKey.IsValid` + `ValidateOnStart()`.
   - Register `AddCadenceBiometricLiveness()` in `apps/kernel-api/src/Program.cs` and `apps/econsent/src/Program.cs`, wire `biometric-liveness-key` in `apps/apphost/src/Program.cs` (both `core` and `full` profiles for `kernel`, `full` for `econsent`), and document `CADENCE_BIOMETRIC_LIVENESS_KEY` in `.env.example`, `docker/docker-compose.yml`, and `docs/ENVIRONMENT_VARIABLES.md`.
3. **Single-use consumption and persistence uniqueness:**
   - In `EconsentEndpoints.ProcessCaptureConsentAsync`, verify the token with the decoded key, run all request field checks and `ClinicalScopeGuard.RequireStudyAndSite`, and only then record `"liveness:" + livenessData.Jti` via `ISigTokenUseStore.TryRecordFirstUseAsync` with retention `ExpiresAtUnix + 5s`, returning `400 Bad Request` (`ValidationProblem` on `liveness_token`) on replay.
   - Configure `BiometricLivenessVerification.LivenessTokenHash` with `.IsUnique()` in `EconsentConfigurations` and ship migration `EnforceUniqueBiometricLivenessTokenHash`.

## 5. Consequences & Trade-offs

- **Fail-closed startup:** Both `kernel` and `econsent` refuse to start when `CADENCE_BIOMETRIC_LIVENESS_KEY` is unset or not base64 of 32 bytes. Rotating the key invalidates in-flight liveness tokens (at most 15 minutes).
- **Non-destructive validation failures:** Malformed capture payloads or unauthorized study/site scopes return `400`/`403` before `TryRecordFirstUseAsync` executes, allowing legitimate callers to correct the request without re-running facial liveness capture.
- **Defense in depth:** `econsent.consumed_signature_tokens` (`liveness:{jti}`) rejects replays deterministically across replicas and restarts, while `IX_biometric_liveness_verifications_LivenessTokenHash` (`UNIQUE`) prevents duplicate token hashes at the relational layer.

## 6. Implementation & Verification

- `packages/security/src/Authentication/BiometricLivenessOptions.cs` and `BiometricLivenessTokenService.cs`.
- `apps/intelligence/src/Endpoints/BiometricEndpoints.cs`, `apps/econsent/src/Endpoints/EconsentEndpoints.cs`, `apps/econsent/src/Program.cs`, `apps/kernel-api/src/Program.cs`, and `apps/apphost/src/Program.cs`.
- `apps/econsent/src/Infrastructure/Persistence/Configurations/EconsentConfigurations.cs`, migration `20261123161128_EnforceUniqueBiometricLivenessTokenHash`, and `docs/schema/econsent.md`.
- Tests: `BiometricLivenessTests`, `EconsentEndpointsTests`, `FieldProtectionHostWiringTests`, and `AppHostModelTests` in `tests/Cadence.UnitTests`.
