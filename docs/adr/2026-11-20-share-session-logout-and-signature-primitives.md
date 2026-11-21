# ADR-2551: Share Session Resilience, Draft-Safe Sign-Out, and Part 11 Signature Primitives

- **Status:** Accepted
- **Date:** 2026-11-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (Operational authentication resilience and participant boundary isolation), PRD-CRF-015 (21 CFR Part 11 electronic signature re-authentication and credential hygiene)

---

## 1. Context & Problem Statement

Both the staff clinical workspace (`apps/web`) and the participant portal (`apps/subject-portal`) authenticate via Keycloak OIDC (`cadence-web`), retain unsigned clinical or questionnaire drafts locally while a user works, and verify electronic signatures against the gateway step-up endpoint (`POST /api/v1/auth/signature-verification`).

Previously, session lifecycle, sign-out confirmation, and signature verification logic diverged across the two clients:

1. **Fragmented OIDC refresh & 401 handling:** Concurrent API calls and background expiry callbacks (`onTokenExpired`) could trigger duplicate `keycloak.updateToken()` requests, `subject-portal` did not register `onTokenExpired` / `onAuthRefreshSuccess` or pass an explicit `redirectUri` in `login()`, and involuntary `401` expiry did not consistently attempt a single-flight refresh, preserve return URLs, or transition `ParticipantCard.vue` out of `Authenticated` to `Session Expired`.
2. **Unconfirmed voluntary sign-out with unsaved drafts:** Clicking **Sign out** immediately purged user-scoped eCRF storage (`purgeUserScopedStorage(null)`) or reset active questionnaire progress without flushing debounced drafts or asking the user to confirm discarding unsaved entries.
3. **Duplicated step-up payloads & signing UX gaps:** Seven callers constructed `/api/v1/auth/signature-verification` bodies separately, `CtmsView.vue` omitted `semantic_action: "ctms.doa.sign_off"`, password inputs lacked shared alphanumeric attributes (`inputmode="text"`), `"Other"` custom reason validation was duplicated, `SignatureCaptureModal.vue` wiped passwords on failed submission without an inline re-entry cue or claim-derived signer role default, and `docker/cadence-realm.json` lacked a deterministic `Subject` account for local E2E testing.

## 2. Decision Drivers & Constraints

- **Draft preservation vs. account isolation (#5369, ADR-2360):** Involuntary token expiry (`onTokenExpired` or unrecoverable `401`) must clear stale bearer tokens in memory without purging user-scoped eCRF drafts or PIN-encrypted IndexedDB questionnaire drafts, whereas confirmed voluntary sign-out must continue purging `purgeUserScopedStorage(null)` and clearing `activeQuestionnaire`.
- **Zero raw token storage (ADR-2493, ADR-2360):** Raw OIDC access tokens and unwrapped session encryption keys must never be persisted to `localStorage` or `sessionStorage`.
- **21 CFR Part 11 cryptographic contract (`docs/SDLC/Signature_Token_Cryptographic_Contract.md`):** Step-up requests must carry canonical lowercase `semantic_action` strings matching `^[a-z0-9_]+(\.[a-z0-9_]+)+$`, enforce custom text when reason `"Other"` is selected, and wipe password/TOTP fields on submit, cancel, and `Escape`.
- **Participant kernel isolation (K027, ADR-2457):** A seeded Keycloak `Subject` user must carry `Subject` realm role and scoped `tenant_id`, `study_ids`, and `site_id` token attributes while holding zero staff kernel grants.

## 3. Options Considered

### Option 1: Centralize Session, Sign-Out Guard, and v3 Signing Primitives in `packages/ui` + Seed Deterministic `Subject` Realm User (Selected)

- **Overview:**
  - Export `createOidcSessionCoordinator` (`packages/ui/session.js`) to register `onTokenExpired` and `onAuthRefreshSuccess`, coalesce concurrent `updateToken` refreshes into one single-flight promise, retry `401` responses once with a refreshed bearer token, and preserve return URLs on involuntary expiry without purging local drafts.
  - Export `SignOutConfirmModal` and `useSignOutGuard` (`packages/ui`) and wire debounced draft `flush()` hooks in `useEcrfForm.ts`, `useEcrfDrafts.js`, and `questionnaire.js` so voluntary sign-out flushes pending drafts and requires explicit confirmation before purging drafts when unsaved work exists.
  - Export `SEMANTIC_ACTIONS`, `isValidSemanticAction`, `buildStepUpVerificationPayload`, `SIGNATURE_PASSWORD_INPUT_ATTRS`, `requiresCustomSignatureReason`, `resolveSignatureReason`, and `deriveSignerRoleFromClaims` (`packages/ui/signing.js`), standardize all seven `/api/v1/auth/signature-verification` callers (including `ctms.doa.sign_off` in `CtmsView.vue`), and surface an inline password re-entry cue in `GxpCredentialsInput.vue` / `SignatureCaptureModal.vue`.
  - Seed deterministic `subject.site101` in `docker/cadence-realm.json` with `realmRoles: ["Subject"]` and `tenant_id: ["cadence-local"]`, `study_ids: ["CADENCE-101"]`, `site_id: ["SITE-101"]`.
- **Pros:**
  - Eliminates duplicated refresh, sign-out confirmation, and Part 11 step-up payload code across `apps/web` and `apps/subject-portal`.
  - Guarantees consistent WCAG 2.1 AA focus-trapped confirmation dialogs, credential wiping, and v3 step-up token binding.
- **Cons:**
  - Requires keeping `packages/ui` built (`pnpm --filter ui build`) before downstream workspace typechecks and tests.

### Option 2: Patch Individual App Stores and Views Independently

- **Overview:** Implement separate refresh locks, custom browser `window.confirm()` dialogs, and inline step-up request bodies in each app.
- **Pros:**
  - Avoids touching `packages/ui` exports.
- **Cons:**
  - Fails shared-code governance, breaks WCAG 2.1 AA focus containment (`window.confirm`), and invites future drift across step-up signature endpoints.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Sharing session coordination, accessible sign-out confirmation, and Part 11 v3 signature helpers in `packages/ui` enforces one tested contract across staff and participant applications while preserving strict memory-only token security and K027 participant isolation.

## 5. Consequences & Trade-offs

- **Positive Impact:** Single-flight token refresh prevents refresh stampedes; involuntary expiry preserves clinical and participant drafts with clear `Session Expired` recovery; voluntary sign-out prevents accidental draft loss; all seven step-up callers emit contract-valid v3 payloads; and `subject.site101` enables deterministic participant OIDC testing.
- **Negative Impact / Technical Debt:** Browser-side eCRF drafts remain user-scoped in `localStorage` until server-side unsigned draft persistence lands.
- **Mitigation Strategy:** `useSignOutGuard` flushes debounced writes before evaluating draft presence and warns users that continuing sign-out discards unsaved entries on the shared workstation.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `docker/cadence-realm.json`, `tests/Cadence.UnitTests`, `tests/Cadence.IntegrationTests`.
- **Verification Plan:** Vitest suites (`packages/ui/tests/session_logout_signing.test.js`, `apps/subject-portal/tests/session-logout-signature-resilience.test.js`, `apps/web/tests/session_logout_signature_primitives.spec.ts`), xUnit realm and K027 isolation tests (`KeycloakRealmTenantTests`, `KernelDevelopmentTenantPostgresTests`), axe-core accessibility assertions, and full `cadence check --pre-commit` quality gates.
