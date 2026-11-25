# ADR-2509: Retire Remaining Python Reference Sources, Phantom Baselines, and Scanner Residue

- **Status:** Accepted
- **Date:** 2026-11-24
- **Authors:** @cadence-arch
- **Deciders:** Architecture & Security Review Board
- **Requirements:** PRD-SYS-053

---

## 1. Context & Problem Statement

Under [ADR-2492](2026-09-30-retire-python-reference-sources-once-ports-are-tested.md), Python reference files remained in `packages/` only while their C# equivalents were unported, untested, or awaiting an explicit architectural disposition (#4789). Following the kernel schedule slices ([ADR-2487](2026-09-30-expected-schedule-as-a-pure-function.md), [ADR-2507](2026-09-30-visit-timing-anchored-visits-and-cycles-on-the-expected-schedule.md)), security and step-up signature hardening ([ADR-2259](2026-09-29-electronic-signature-step-up-re-authentication.md), [ADR-2490](2026-09-30-fail-fast-startup-validation-of-required-settings.md), [ADR-2510](2026-09-30-endpoint-permissions-from-the-permission-matrix.md), [ADR-2515](2026-10-15-step-up-tokens-are-single-use-across-instances-and-restarts.md)), and scaffold disposition rules ([ADR-2610](2026-10-15-scaffold-services-become-modules-merge-or-are-deleted.md)), seven dead Python files (`3,469` lines) still remained on disk:

- `packages/engine_core/timeline/engine.py` and `packages/engine_core/timeline/models.py`
- `packages/security/cert_store.py`, `packages/security/crypto_verifier.py`, `packages/security/fail_fast.py`, `packages/security/rbac.py`, and `packages/security/signing.py`

Alongside those dead files, static tooling and ratchet baselines still carried migration-era residue:

- `baselines/secrets/apps/ctms.secrets.baseline.json` and `baselines/secrets/apps/econsent.secrets.baseline.json` retained eight phantom `test_*.py` entries for deleted files, and `baselines/secrets/apps/web.secrets.baseline.json` retained a deleted `BatchSignatureModal.vue` entry because `SecretsBaselineCleanerService` did not prune or reject missing repository paths.
- `baselines/deid/packages/engine_core.baseline.txt` retained a stale `packages/engine_core/timeline/engine.py` finding.
- `SecretScanner` and `scripts/build-docs.js` still referenced `INBOUND_EMAIL_HMAC_SECRET` from the retired eTMF inbound email webhook (`signing.py`).
- `EnvVarExtractorService`, `DeidComplianceService`, and `DuplicationDetectorService` retained `.py` scanner branches or porting comments, while `PathPatternValidatorService` did not block reintroducing `.py` files under `apps/` or `packages/`.

## 2. Decision Drivers & Constraints

- **Single authoritative security and compliance surface (`PRD-SYS-053`):** Contributors auditing 21 CFR Part 11 signatures, RBAC permissions, hash-chained audit trails, or visit-window schedules must encounter only live `.NET 10` implementations and accepted ADRs—never dead Python code with divergent behavior.
- **Fail-closed ratchet hygiene:** Sharded baselines under `baselines/secrets/` and `baselines/deid/` must track only files that exist in the repository and shrink automatically when files are retired.
- **Zero runtime drift:** Retiring dead reference files and tightening CLI gates must preserve exact runtime behavior across all C# services, Vue clients, and local Keycloak realm definitions (`docker/cadence-realm.json`).
- **Link stability:** [PYTHON_REFERENCE_INVENTORY.md](../PYTHON_REFERENCE_INVENTORY.md) is linked from historical ADRs and engineering blog posts, so the document must remain on disk in archived `Historical` state rather than breaking portal links.

## 3. Options Considered

### Option 1: Delete all seven Python files, record explicit dispositions, prune phantom baselines, and remove `.py` scanner residue (Selected)

- **Overview:** Delete all seven remaining `.py` files, record the C# supersessions and intentionally dropped behaviors in this ADR, archive `docs/PYTHON_REFERENCE_INVENTORY.md` as `Historical` with zero active rows, enforce repository-file existence in `SecretsBaselineCleanerService`, remove `INBOUND_EMAIL_HMAC_SECRET` and `.py` branches from CLI tooling, and reject `.py` paths under `apps/` and `packages/` in `PathPatternValidatorService`.
- **Pros:**
  - ✅ Brings unretired Python files across the repository to zero while preserving full recoverability in git history ([ADR-2492](2026-09-30-retire-python-reference-sources-once-ports-are-tested.md)).
  - ✅ Permanently prevents phantom secrets baseline entries when source or test files are deleted.
  - ✅ Aligns `SecretScanner`, `EnvVarExtractorService`, `DeidComplianceService`, `DuplicationDetectorService`, and `PathPatternValidatorService` with the pure `.NET 10` / TypeScript / Vue codebase.
- **Cons:**
  - ❌ Synthetic unit tests for `SecretsBaselineCleanerService` must materialize dummy target files in temporary repositories so valid entries are distinguished from phantom paths.

### Option 2: Keep the five `packages/security/*.py` files until deferred scaffold modules land

- **Overview:** Retire only `packages/engine_core/timeline/` now and keep `packages/security/*.py` on disk until the deferred `quality` scaffold module is built out.
- **Pros:**
  - ✅ Smaller immediate diff.
- **Cons:**
  - ❌ Keeps `3,051` lines of dead security Python code (including obsolete client-supplied PEM/XML signature fallbacks, placeholder string lists, and retired `INBOUND_EMAIL_HMAC_SECRET` logic) in `packages/security/`, obscuring authoritative `PRD-SYS-053` controls.
  - ❌ Leaves `.py` branches and phantom baseline shards active across CLI quality gates.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Justification:** Every surviving capability across the seven Python files is either implemented and tested in `.NET 10`, superseded by an accepted kernel or security ADR, or explicitly deferred to an existing tracked module roadmap. Recording each disposition here satisfies [ADR-2492](2026-09-30-retire-python-reference-sources-once-ports-are-tested.md) and `PRD-SYS-053` without carrying dead reference files in active packages.

### Recorded Dispositions

1. **Visit-window schedule and models (`packages/engine_core/timeline/engine.py`, `timeline/models.py`):**
   - Superseded by `ExpectedSchedule` (`packages/kernel/src/Cadence.Kernel.Abstractions/Protocol/Design/ExpectedSchedule.cs`, [ADR-2487](2026-09-30-expected-schedule-as-a-pure-function.md)) and `ExpectedSchedule.Assess` ([ADR-2507](2026-09-30-visit-timing-anchored-visits-and-cycles-on-the-expected-schedule.md)).
   - The Python reference's fixed 7-day major/minor deviation threshold and silent anchor fallback are not carried forward: deviation severity belongs to governed Quality rules in the one rule language, and remaining anchored-visit / repeating-cycle extensions are specified in [ADR-2507](2026-09-30-visit-timing-anchored-visits-and-cycles-on-the-expected-schedule.md) (#5045).

2. **X.509 certificate store and electronic signature verifier (`packages/security/cert_store.py`, `crypto_verifier.py`):**
   - 21 CFR Part 11 electronic signatures (`PRD-SYS-053`) are enforced through gateway-issued single-use HS256 step-up tokens verified and consumed by `SigTokenVerifier.VerifyAndConsumeAsync` (`packages/security/src/Authentication/SigTokenVerifier.cs`, [ADR-2259](2026-09-29-electronic-signature-step-up-re-authentication.md), [ADR-2515](2026-10-15-step-up-tokens-are-single-use-across-instances-and-restarts.md)) paired with RFC 8785 canonical payload hashing in `CanonicalSigner` ([ADR-2513](2026-09-30-form-approval-hashes-the-rfc-8785-canonical-payload.md)).
   - Client-submitted PEM/XML-DSig certificate chains, in-memory certificate registration/CRL checks, and asymmetric-to-HMAC signature fallback orchestration from `cert_store.py` and `crypto_verifier.py` are not carried forward. Server-side RSA/X.509 signing for de-identification certificates and redaction manifests lives in `AsymmetricSigner`, compliance certificate verification tests remain pinned in `Cadence.Compliance` (`ESignatureVerifier`), and SMART client assertions use configured server-side keys ([ADR-2517](2026-10-19-smart-client-assertions-are-signed-only-with-server-side-endpoint-keys.md)).

3. **Startup secret and branding validation (`packages/security/fail_fast.py` — Inventory Note S1):**
   - Superseded by fail-closed startup validation in each `.NET 10` host ([ADR-2260](2026-09-29-no-default-credentials-and-fail-closed-secret-resolution.md), [ADR-2490](2026-09-30-fail-fast-startup-validation-of-required-settings.md)): `GATEWAY_SECRET` (minimum 32 characters), `DemographicsEncryptionOptions`, `RANDOMIZATION_ALLOCATION_KEY`, `AddCadenceFieldEncryption()` / `AddCadenceLookupHashing()` ([ADR-2494](2026-09-30-field-level-encryption-and-keyed-lookup-hashes-for-secrets-at-rest.md)), `AddCadenceBiometricLiveness()` ([ADR-2660](2026-11-23-biometric-liveness-hmac-key-and-single-use-replay-protection.md)), `SigTokenUseStoreStartupCheck` ([ADR-2515](2026-10-15-step-up-tokens-are-single-use-across-instances-and-restarts.md)), and gateway cluster URL checks.
   - Notification branding validation lives in `NotificationBrandingOptions` (`apps/notifications/src/Infrastructure/NotificationBrandingOptions.cs`), registered with `ValidateOnStart()` to require non-empty `BrandName` and an HTTPS (or loopback HTTP) `PortalUrl`. Python's generic placeholder substring denylist and `BRAND_DOMAIN` boot variable are dropped.

4. **RBAC role normalization and permission matrix (`packages/security/rbac.py` — Inventory Note S2):**
   - Active role normalization, permission policies, blinding, and scope enforcement live in `RoleNormalization.cs`, `PermissionMatrix.cs` (`PermissionMatrix.<Module>.cs`), `SiteAccessEvaluator.cs`, `StudyAccessEvaluator.cs`, `ClinicalScopeGuard.cs`, `FieldMasker.cs`, and `AuditorWriteGuardMiddleware.cs` (`PRD-SYS-053`, [ADR-2510](2026-09-30-endpoint-permissions-from-the-permission-matrix.md), [ADR-2661](2026-11-22-consolidate-standalone-caller-and-clinical-scope-guards.md)).
   - `system` and `anonymous` roles remain dropped per K-TX-5 ([ADR-2257](2026-09-29-attributable-audit-actors-and-named-service-accounts.md)).
   - For the five roles noted in Inventory Note S2:
     - `grants_manager` normalizes to `RoleNormalization.ROLE_FINANCE_OFFICER` (`finance_officer` in `packages/security/src/Rbac/RoleNormalization.cs`), which holds the Payments contract, payable, and invoice permissions (`PermissionMatrix.Payments.cs`, #5361).
     - `sponsor_clinical` is not carried forward; sponsor clinical data and medical oversight capabilities are partitioned across `sponsor_dm` and `sponsor_mm` in `packages/security/src/Rbac/PermissionMatrix.cs`.
     - `quality_manager`, `qa_lead`, and `quality_oversight` remain defined in `docker/cadence-realm.json` for local realm compatibility, while their permission bundles are deferred until the `quality` scaffold becomes a module of the composition host under [ADR-2610](2026-10-15-scaffold-services-become-modules-merge-or-are-deleted.md) (#5376).

5. **Signing helpers, greenlight tokens, inbound email HMAC, and audit hashing (`packages/security/signing.py` — Inventory Note S3):**
   - Gateway identity signing, canonical JSON signing, asymmetric signing, and audit digest calculation live in `HmacSigner.cs`, `CanonicalSigner.cs`, `AsymmetricSigner.cs`, and `AuditDigestEngine.cs`.
   - `verify_inbound_email_signature` and `INBOUND_EMAIL_HMAC_SECRET` are dropped because the eTMF inbound email webhook is not carried forward (#5049); `INBOUND_EMAIL_HMAC_SECRET` is simultaneously removed from `SecretScanner.cs` and `scripts/build-docs.js`.
   - `generate_greenlight_token` / `verify_greenlight_token` are superseded in CTMS by `GreenlightTokenGenerator.GenerateToken` (`apps/ctms/src/Endpoints/StartupGreenlightEndpoints.cs`, [ADR-2805](2026-10-02-ctms-site-startup-greenlight-matrix.md)) and in the clinical kernel by bitemporal study-site activation ([ADR-2456](2026-09-30-effectivity-and-study-site-activation.md)).
   - `compute_merkle_root` / `compute_block_hash` are superseded by the single-writer hash-chained GxP audit interceptor `GxPAuditInterceptor` (`packages/database/src/GxPAuditInterceptor.cs`, [ADR-2446](2026-09-29-single-audit-writer-and-one-chain-per-aggregate.md)) verified by `AuditLogChainVerifier` and `AuditDigestEngine`, and by per-tenant kernel audit chains ([ADR-2449](2026-09-29-kernel-command-pipeline-transactional-write-set-and-per-tenant-audit-chain.md)).

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Zero `.py` files remain anywhere in the repository, and `PathPatternValidatorService` blocks adding `.py` files under `apps/` or `packages/`.
  - `SecretsBaselineCleanerService` automatically prunes entries for deleted files (and removes empty shards such as `ctms.secrets.baseline.json` and `econsent.secrets.baseline.json`) while failing closed in `--check` and `--pre-commit` modes when a baseline references a missing path.
  - Documentation and static scanners reflect only active `.NET 10`, TypeScript, and Vue surfaces.
- **Negative Impact / Technical Debt:**
  - Historical rationale for deleted Python files must be inspected via `git log` / `git show` rather than working-tree files.
- **Mitigation Strategy:**
  - Keep [PYTHON_REFERENCE_INVENTORY.md](../PYTHON_REFERENCE_INVENTORY.md) on disk as an archived `Historical` record linking to [ADR-2492](2026-09-30-retire-python-reference-sources-once-ports-are-tested.md) and this ADR.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - Deleted Python sources: `packages/engine_core/timeline/{engine,models}.py`, `packages/security/{cert_store,crypto_verifier,fail_fast,rbac,signing}.py`.
  - Tooling & build scripts: `packages/tooling_core/src/Services/SecretsBaselineCleanerService.cs`, `EnvVarExtractorService.cs`, `DeidComplianceService.cs`, `DuplicationDetectorService.cs`, `PathPatternValidatorService.cs`, `DocIndexSyncService.cs`, `packages/tooling_core/src/Security/SecretScanner.cs`, and `scripts/build-docs.js`.
  - Baselines: `baselines/secrets/apps/{ctms,econsent,web}.secrets.baseline.json` and `baselines/deid/packages/engine_core.baseline.txt`.
  - Documentation: `docs/PYTHON_REFERENCE_INVENTORY.md`, `docs/DOCUMENTATION_INDEX.md`, `docs/adr/index.md`, `packages/engine_core/README.md`, `packages/security/README.md`, `README.md`, `ARCHITECTURE.md`, `AGENTS.md`, and `CONTRIBUTING.md`.
- **Verification Plan:**
  - Run `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj` and `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`.
  - Run `pnpm cadence check` across `adrs`, `doc-index`, `clean-secrets`, `detect-secrets`, `security`, `deid`, `duplication`, `env-vars`, `path-patterns`, `baseline-ratchet`, `markdown`, `readmes`, and `gxp-snapshot`, plus `pnpm docs:build`.
