# ADR-2641: Participant ePRO Interop Endpoints and Keyed-HMAC BYOD Activation

- **Status:** Accepted
- **Date:** 2026-11-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Participants using the Companion Portal (`apps/subject-portal`) require live bring-your-own-device (BYOD) onboarding, scheduled instrument assignments, offline diary submission and batch sync reconciliation, compliance summaries, reminder notification acknowledgements, and offline Service Worker shell caching (PRD-SYS-001). Previously, `apps/interop` defined persistence entities (`Instrument`, `SubjectAssignment`, `SubjectNotification`, `EproSubmission`, `EproSubmissionDefeated`, `EproSubmissionQuarantine`, `InteropAuditLog`) without mounting the eight participant `/api/v1/interop/*` routes, `apps/execution` lacked pseudonymous BYOD activation code issuance and redemption, the Gateway did not sign or forward pseudonymous `subject_code` claims, and `public/sw.js` referenced dev-only `/src/*` paths that returned `404` in production builds.

## 2. Decision Drivers & Constraints

- **Single Body of Clinical Truth (K-TRUTH-1):** Interop reconciles offline ePRO submissions and forwards resolved diary items as typed `ObservationValue` payloads into canonical Kernel observations (`POST /api/v1/kernel/observations`) rather than treating ePRO storage as an isolated silo.
- **Zero PII Outside IdentityVault (K-IDN-1):** BYOD onboarding must bind an OIDC `Subject` session to a pseudonymous `SubjectCode` using 30-day activation codes stored exclusively as keyed HMAC-SHA256 digests (`ILookupHasher`), with zero participant contact details or raw activation secrets persisted at rest.
- **Fail-Closed Participant Isolation & Role Gating:** Activation code issuance must be restricted to investigators and coordinators (`SubjectAuthority.AuthorizeParticipantChange`), and every participant endpoint must verify `Subject` caller ownership of `subjectId` before any database read or write executes.
- **21 CFR Part 11 Data Integrity & Deterministic Time (K-TIME-3):** All mutations enforce `X-Change-Reason`, record audit entries, stamp timestamps strictly via `Cadence.Kernel.Time.IClock`, preserve defeated conflict payloads (`EproSubmissionDefeated`), and quarantine stale instrument versions (`EproSubmissionQuarantine`).

## 3. Options Considered

### Option 1: Mount Interop ePRO Reconciliation with Canonical Observation Forwarding, Signed `X-Subject-Code` Gateway Claims, and Keyed-HMAC Activation Codes (Selected)

- **Overview:** Mount `MapEproEndpoints()` in `apps/interop`, forward resolved diary items via `ICanonicalObservationForwarder` (`K-TRUTH-1`), issue and redeem 30-day HMAC-digested activation codes in `apps/execution` (`SubjectActivationToken`, hashed via `AddCadenceLookupHashing()` under `FIELD_LOOKUP_HASH_KEY` with fail-closed startup validation per ADR-2494), extend `GatewayIdentity` and `HmacSigner` to sign `X-Subject-Code`, enforce pre-query `Subject` ownership guards, and inject Vite Rollup bundle asset paths into `dist/sw.js` at build time using Vite's resolved `base` (`VITE_BASE_PATH` / `/subject-portal/`, preserving `SW_BASE` subpath co-hosting per ADR-2550).
- **Pros:**
  - ✅ Satisfies PRD-SYS-001, `K-TRUTH-1`, `K-IDN-1`, `K-TIME-3`, and `K-XS-1` with zero raw secrets, fallback keys, or PII outside `IdentityVault`.
  - ✅ Enforces `HTTP 403` cross-participant isolation before touching `InteropDbContext` and preserves defeated/quarantined offline sync payloads.
  - ✅ Eliminates production Service Worker `404` precache errors across both `/subject-portal/` and GitHub Pages subpaths by injecting hashed `${base}assets/*` chunks during `vite build`.
- **Cons:**
  - ❌ Adds `subject_code` header canonicalization to `HmacSigner` and an EF Core migration (`AddSubjectActivationTokens`) in `apps/execution`.

### Option 2: Store Activation Codes in Plaintext and Keep ePRO Submissions Local to Interop

- **Overview:** Persist raw one-time onboarding codes on `ClinicalSubject` and query `EproSubmission` directly without forwarding canonical observations or signing `subject_code` at the Gateway.
- **Pros:**
  - ✅ Fewer moving parts across `execution`, `gateway`, and `interop`.
- **Cons:**
  - ❌ Violates `K-TRUTH-1` by trapping participant clinical observations inside `interop` and weakens secret-at-rest hygiene (`K-IDN-1`).

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Combining HMAC-digested 30-day BYOD activation tokens, Gateway-signed `X-Subject-Code` claims, pre-database `Subject` ownership checks, durable Interop sync/quarantine reconciliation, and canonical Kernel observation forwarding delivers offline-capable participant ePRO capture without compromising `K-TRUTH-1`, `K-IDN-1`, or 21 CFR Part 11 auditability.

## 5. Consequences & Trade-offs

- **Positive Impact:** Investigators and coordinators can issue 30-day BYOD activation codes from the Study Canvas inspector; participants can sign in (`SUBJ-101-001` or redeemed OIDC binding), cache the portal shell offline with zero `404`s, submit/sync diaries idempotently, inspect compliance, and acknowledge reminders.
- **Negative Impact / Technical Debt:** Rotating `FIELD_LOOKUP_HASH_KEY` invalidates unredeemed activation-code digests unless paired with key-ring verification; canonical observation forwarding operates over signed HTTP until `interop` is consolidated into the modular monolith host (ADR-2244).
- **Mitigation Strategy:** Documented `FIELD_LOOKUP_HASH_KEY` rotation behavior in `docs/ENVIRONMENT_VARIABLES.md` and covered all endpoints, HMAC signing vectors, ownership guards, and Service Worker bundle injection with automated tests.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/interop`, `apps/execution`, `apps/gateway`, `packages/security`, `apps/web`, `apps/subject-portal`, and `docker/cadence-realm.json`.
- **Verification Plan:** Verified via `EproEndpointsTests`, `SubjectActivationCodeTests`, `RouteAuthorizationTests`, `Cadence.ArchitectureTests`, `participantActivationCode.spec.ts`, `epro-interop-and-sw.test.js`, and `pnpm cadence check`.
