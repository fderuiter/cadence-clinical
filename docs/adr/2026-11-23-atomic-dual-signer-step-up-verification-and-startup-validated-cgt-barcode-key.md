# ADR-2661: Atomic Dual-Signer Step-Up Verification and Startup-Validated CGT Barcode Key

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (21 CFR Part 11 electronic signature integrity), SRS-SYS-005 (Single-use step-up signature token verification)

---

## 1. Context & Problem Statement

Cell and Gene Therapy (CGT) chain-of-custody workflows in `Cadence.Apps.Execution` (`ADR-2803`) had three security and 21 CFR Part 11 compliance gaps:

1. **Hardcoded Fallback Barcode Key:** `CgtBarcodeEngine` defined a source-resident `DefaultSecretKey` fallback and compared lowercase hex strings rather than constant-time binary HMAC-SHA256 digests, while `CgtApplicationService` omitted a deployment key at runtime.
2. **Unverified Dual Signers & Caller Spoofing:** `POST /api/v1/execution/rtsm/cgt/bedside-verify` and `POST /api/v1/execution/rtsm/cgt/destruction-certificate` accepted caller-supplied `primary_verifier_id` / `witness_1_id` and unverified signature token strings without checking caller identity (`ADR-2516`) or verifying `v3` step-up JWTs via `SigTokenVerifier` (`ADR-2515`), while `CellGeneTherapyCocConsole.vue` initialized mock signature token strings in browser state.
3. **Partial Token Burn Risk on Dual-Signer Mutations:** Calling single-token `VerifyAndConsumeAsync` sequentially for two co-signers would consume the primary signer's `jti` before checking the secondary signer's token, burning a valid primary step-up token whenever the secondary token is expired, replayed, bound to the wrong target record, or held by a read-only auditor role.

## 2. Decision Drivers & Constraints

- **Fail-Closed Startup Keying (`PRD-SYS-001`):** Every service key must come from deployment configuration validated at host boot via `FieldProtectionKey.IsValid` and `.ValidateOnStart()`, with zero source-code fallback secrets and constant-time digest comparison (`CryptographicOperations.FixedTimeEquals`).
- **Authenticated Caller Attribution (`ADR-2516`):** The primary bedside verifier (`primary_verifier_id`), infusion executor (`primary_verifier_id`), and primary destruction witness (`witness_1_id`) must equal the gateway-signed caller (`User.ToAuthenticatedCaller()`), returning HTTP `403 Forbidden` (`SIGNER_MISMATCH`) on mismatch.
- **All-or-Nothing Dual `jti` Consumption (`SRS-SYS-005`, `ADR-2515`):** Both `v3` step-up tokens must pass cryptographic signature, acceptance-window, non-auditor role, distinct-signer, action, semantic-action, and target-record (`batch_id`) validation before either `jti` is recorded as consumed, and multi-token persistence must succeed or roll back atomically.

## 3. Options Considered

### Option 1: Startup-Validated `CGT_BARCODE_HMAC_KEY`, Caller-Bound Dual Step-Up Verification, and Atomic `TryRecordFirstUseBatchAsync` (Selected)

- **Overview:** Remove `DefaultSecretKey` from `CgtBarcodeEngine`, require a 32-byte `byte[] secretKey`, and compare raw SHA-256 digests with `CryptographicOperations.FixedTimeEquals`. Bind and validate `CGT_BARCODE_HMAC_KEY` (`CgtBarcodeProtectionOptions`) at startup in `apps/execution/src/Program.cs`. Extend `ISigTokenUseStore` (`ReplayCache` and `PostgresSigTokenUseStore<TDbContext>`) with `TryRecordFirstUseBatchAsync` and add a dual-signer `SigTokenVerifier.VerifyAndConsumeAsync` overload that validates both tokens (`ValidateBound`) and distinct `jti` values before atomically consuming both tokens. Enforce caller matching and dual step-up verification in `CgtEndpoints` / `CgtApplicationService`, and wire shared `packages/ui/signing.js` step-up helpers (`CGT_BEDSIDE_VERIFY`, `CGT_DESTRUCTION_CERTIFY`, `formatCgtBedsideBindingId`, `formatCgtDestructionBindingId`) in `CellGeneTherapyCocConsole.vue`.
- **Pros:**
  - ✅ Eliminates hardcoded cryptographic material and fails closed at host startup when `CGT_BARCODE_HMAC_KEY` is missing, malformed, or not 32 bytes.
  - ✅ Guarantees neither co-signer's `jti` is burned when the other co-signer's token fails validation or replay checks across both in-memory and PostgreSQL/EF Core stores.
  - ✅ Enforces `ADR-2516` caller attribution (`403 SIGNER_MISMATCH`) and `SRS-SYS-005` re-authentication (`401 REAUTHENTICATION_REQUIRED`) end to end.
- **Cons:**
  - ❌ Requires configuring `CGT_BARCODE_HMAC_KEY` across local development templates, Docker Compose, Aspire AppHost, OpenAPI exporter, and Execution test hosts.

### Option 2: Sequential Single-Token `VerifyAndConsumeAsync` Calls Without Store Batch Atomicity

- **Overview:** Reuse the existing single-token `VerifyAndConsumeAsync` twice in sequence inside `CgtApplicationService`.
- **Pros:**
  - ✅ Avoids adding `TryRecordFirstUseBatchAsync` to `ISigTokenUseStore`.
- **Cons:**
  - ❌ Burns the primary verifier's single-use `jti` whenever the secondary verifier's token is invalid, mistyped, expired, or replayed, forcing the primary clinician to re-enter credentials unnecessarily.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Validating both `v3` step-up tokens up front and recording their `jti` values atomically via `TryRecordFirstUseBatchAsync` satisfies `SRS-SYS-005` single-use replay prevention without penalizing the primary clinician when secondary verification fails, while startup-validated `CGT_BARCODE_HMAC_KEY` removes all fallback cryptographic keys from source code.

## 5. Consequences & Trade-offs

- **Positive Impact:** CGT bag barcodes are verified solely with deployment-configured 32-byte HMAC-SHA256 keys in constant time; bedside infusion unlocking and destruction certification require verified dual 21 CFR Part 11 step-up signatures bound to the exact study/subject/DIN/batch or study/site/lot/CoC record; and mock tokens are removed from the web console.
- **Negative Impact / Technical Debt:** Every environment and test host booting `Cadence.Apps.Execution` must supply a valid Base64 32-byte `CGT_BARCODE_HMAC_KEY`.
- **Mitigation Strategy:** Provide `TestSecurityContext.CgtBarcodeKeyBase64` / `UseTestCgtBarcodeKey()` for test hosts, wire `cgt-barcode-hmac-key` in Aspire AppHost and `docker-compose.yml`, and document `CGT_BARCODE_HMAC_KEY` in `.env.example`, `docs/ENVIRONMENT_VARIABLES.md`, `docs/LOCAL_DEV_ENVIRONMENT.md`, and `docs/OPERATIONAL_DATABASE.md`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/engine_core`, `packages/security`, `packages/database`, `packages/testing`, `packages/ui`, `packages/tooling_core`, `apps/execution`, `apps/apphost`, `apps/web`, `docker/docker-compose.yml`, `.env.example`, `docs/`.
- **Verification Plan:** Verified via `CgtEngineTests`, `SignatureTokenTests`, `CgtApplicationServiceTests`, `CgtEndpointsSecurityTests`, `DemographicsKeyStartupValidationTests`, `CellGeneTherapyCocConsole.spec.ts`, `session_logout_signing.test.js`, `Cadence.ArchitectureTests`, and `pnpm cadence check`.
