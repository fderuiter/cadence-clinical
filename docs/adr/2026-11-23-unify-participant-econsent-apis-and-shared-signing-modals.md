# ADR-2671: Unify Participant eConsent APIs and Shared Signing Modals

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (Participant boundary isolation and caller-scoped authorization), PRD-SYS-053 (Signed gateway and service-to-service identity), PRD-CRF-015 (21 CFR Part 11 electronic signature verification and credential hygiene)

---

## 1. Context & Problem Statement

Authenticated participants using `apps/subject-portal` outside synthetic demo builds could not complete electronic informed consent or protocol amendment re-consent end-to-end due to five cross-service contract and UI divergences:

1. **Availability & startup re-consent gating:** `AUTHENTICATED_SCREENS` in `apps/subject-portal/src/router/availability.js` only included `wallet`, redirecting authenticated participants visiting `/consent` to `/unavailable` and skipping `checkPendingReconsent()` on startup outside demo builds.
2. **OIDC `preferred_username` vs. `sub` conflation:** `apps/subject-portal/src/features/auth.js` stored only the OIDC `sub` UUID on `state.session.userId`. Step-up password verification (`POST /api/v1/auth/signature-verification` in `apps/gateway`) authenticates against the human-readable Keycloak `preferred_username` (for example `subject.site101`), whereas encrypted IndexedDB draft scoping (`transitionParticipantScope`) and `subject_pseudonym` persistence in `apps/econsent` require the immutable `sub` claim.
3. **Participant RBAC & caller-bound eConsent endpoints:** `ROLE_SUBJECT` needed least-privilege self-service permissions (`econsent_self_consent:read|verify|sign`, `econsent_self_reconsent:read|sign`) with zero staff authoring or staff consent capture grants, and `apps/econsent` needed caller-bound comprehension grading, initial consent capture, pending amendment lookup, and re-consent completion enforcing `subject_pseudonym == CallerScope.RequiredUserId(httpContext)`.
4. **Dual-request single-use token replay & missing Execution sync:** `apps/subject-portal/src/features/reconsent.js` previously requested one single-use `X-Sig-Token`, posted to an unmounted `/api/v1/execution/amendments/reconsent` path, and replayed the same consumed token against `/api/v1/econsent/reconsent/complete/{requirement_id}`, while `apps/econsent` had no signed service-to-service webhook to synchronize signed consents into `apps/execution`.
5. **Custom portal signing modal shells:** `SignatureModal.vue` and `ReconsentModal.vue` maintained custom dialog and alert markup instead of composing `ClinicalModal` and `ModalErrorAlert` from `packages/ui`.

## 2. Decision Drivers & Constraints

- **Strict participant self-service isolation (K027, ADR-2457):** `ROLE_SUBJECT` must never hold `econsent_template:publish` or `econsent_consent:capture`, and cross-participant `subject_pseudonym` access in `apps/econsent` must fail closed with HTTP `403`. Site staff (`ROLE_CRC`, `ROLE_INVESTIGATOR`) must retain staff consent capture capabilities.
- **Single-use Part 11 step-up tokens (ADR-2515, ADR-2551):** Each electronic signature operation must consume exactly one `X-Sig-Token` bound to the target HTTP route and canonical `semantic_action` (`econsent.consent.capture` or `econsent.reconsent.sign`), clear passwords before network submission and on dismissal, preserve custom signing reasons and comprehension answers on failure, and return focus deterministically to the password field.
- **Signed service-to-service synchronization:** `apps/econsent` must synchronize signed initial and amended consents into `apps/execution` (`POST /api/v1/execution/webhooks/econsent`) using HMAC-signed gateway headers (`svc:econsent`, role `econsent_service`, permission `subject_enrollment:econsent_sync`) so subject enrollment, `SubjectConsent`, and `RequiresReconsent` stay consistent without granting participant tokens direct write access to `apps/execution`.
- **Design system & WCAG 2.2 AA compliance:** Both portal signing modals must use `ClinicalModal` and `ModalErrorAlert` from `packages/ui` with zero emojis, assertive `role="alert"` banners, and deterministic focus management.

## 3. Options Considered

### Option 1: Caller-Bound `apps/econsent` Self-Service Endpoints, Signed Execution Webhook Sync, Separated OIDC `username`/`userId` State, and Shared `ClinicalModal` + `ModalErrorAlert` Modals (Selected)

- **Overview:**
  - Add `"consent"` to `AUTHENTICATED_SCREENS` in `apps/subject-portal/src/router/availability.js` and run `checkPendingReconsent()` on authenticated startup in `apps/subject-portal/src/bootstrap/initialize.js`.
  - Hydrate OIDC `preferred_username` onto `state.session.username` (bound to `state.session.userId`) in `apps/subject-portal/src/features/auth.js` so `SignatureModal` and `ReconsentModal` prefill and validate `preferred_username` for `/api/v1/auth/signature-verification` while preserving `state.session.userId` (`sub`) for `transitionParticipantScope` and `subject_pseudonym`.
  - Grant `ROLE_SUBJECT` strictly `econsent_self_consent:read|verify|sign` and `econsent_self_reconsent:read|sign` in `PermissionMatrix.Econsent.cs`, enforce caller-bound `subject_pseudonym` checks and comprehension pass gating in `EconsentEndpoints.cs`, and complete amendment re-consent atomically through a single `POST /api/v1/econsent/reconsent/complete/{requirement_id}` call.
  - Publish signed `POST /api/v1/execution/webhooks/econsent` events (`HttpEconsentExecutionWebhookPublisher` -> `EconsentWebhookEndpoints` / `EconsentWebhookApplicationService`) under `econsent_service` (`subject_enrollment:econsent_sync`) to auto-enroll missing subjects, upsert `SubjectConsent`, and clear prior `RequiresReconsent` flags.
  - Add `keepMounted` to `ClinicalModal.vue` and `autoFocus` + `ClinicalIcon` to `ModalErrorAlert.vue` in `packages/ui`, and migrate `SignatureModal.vue` and `ReconsentModal.vue` onto both shared primitives.
- **Pros:**
  - Satisfies single-use `X-Sig-Token` semantics, least-privilege RBAC, and cross-participant isolation end-to-end.
  - Eliminates duplicated modal/alert markup in `apps/subject-portal` while preserving imperative DOM compatibility and deterministic password-input refocusing.
- **Cons:**
  - Introduces an HTTP webhook hop from `apps/econsent` to `apps/execution` requiring `EXECUTION_SERVICE_URL` and shared `GATEWAY_SECRET` configuration in AppHost and Docker Compose.

### Option 2: Expose Direct Participant Write Endpoints on `apps/execution` and Keep Separate Portal Modal Markup

- **Overview:** Allow `ROLE_SUBJECT` to call both `apps/execution` and `apps/econsent` directly with separate step-up tokens and keep bespoke modal shells in `apps/subject-portal`.
- **Pros:**
  - Avoids service-to-service webhook wiring between `apps/econsent` and `apps/execution`.
- **Cons:**
  - Forces participants to perform two password re-authentications for one re-consent signature, risks partial writes across services, widens `ROLE_SUBJECT` attack surface onto `apps/execution`, and leaves duplicated modal code in `apps/subject-portal`.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Completing initial consent and amendment re-consent through caller-bound `apps/econsent` endpoints with a single bound `X-Sig-Token` and synchronizing `apps/execution` via a signed `econsent_service` webhook preserves atomic participant UX, strict K027/RBAC boundaries, and shared `packages/ui` modal accessibility.

## 5. Consequences & Trade-offs

- **Positive Impact:** Authenticated participants can review approved consent templates, pass graded comprehension checks, sign initial consents, and complete protocol amendment re-consents outside demo mode using their OIDC `preferred_username` and password while IndexedDB encryption and `subject_pseudonym` records remain keyed to OIDC `sub`; `apps/execution` automatically reflects signed consent versions and clears `RequiresReconsent`; and both portal signing modals share `ClinicalModal` and `ModalErrorAlert`.
- **Negative Impact / Technical Debt:** Synchronous HTTP webhook delivery logs a warning if `apps/execution` is transiently unreachable rather than queuing via a durable broker outbox.
- **Mitigation Strategy:** `EconsentWebhookApplicationService` is idempotent across repeated deliveries for the same `(subject_code, study_id, version_index)`, allowing straightforward migration to a durable broker transport when cross-service messaging is enabled.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/security`, `packages/ui`, `apps/econsent`, `apps/execution`, `apps/subject-portal`, `apps/apphost`, `docker/docker-compose.yml`, `tests/Cadence.UnitTests`.
- **Verification Plan:** xUnit tests (`ParticipantEconsentAndWebhookTests`, `SignatureTokenEndpointTests`, `RouteAuthorizationTests`, `Cadence.ArchitectureTests`), Vitest suites (`packages/ui`, `apps/subject-portal/tests/participant-econsent-unification.test.js` and full portal coverage suite), Playwright visual verification of `SignatureModal` and `ReconsentModal`, OpenAPI/doc index synchronization, and `cadence check` gates.
