# ADR-2227: Decoupled Persona Composable and Capability Pattern for eClinical Workspaces

- **Status:** Accepted
- **Date:** 2026-08-28
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

* **Requirement:** `PRD-SYS-050`, `PRD-CRF-009`, `PRD-SYS-001`

---

## 1. Context & Problem Statement

In `apps/web`, clinical workspaces such as `EcrfView.vue` encapsulate multiple clinical personas with distinct regulatory duties (Site Coordinator / CRC subject registration & data entry, CRA clinical monitor source data verification / SDV, and Principal Investigator batch sign-off under 21 CFR Part 11).

Previously, `EcrfView.vue` grew into a >1,200-line monolithic component managing data fetching, validation, modal dialogs, and role authorization through ad-hoc template expressions (`authStore.isCrc || activeUserRole === 'site_investigator'`). Furthermore, domain composables like `usePiSignoff.js` were written in untyped JavaScript with hardcoded mock arrays, creating test fragility and type drift.

## 2. Decision Drivers & Constraints

- **Persona Isolation & Modularity (`PRD-SYS-050`)**: Clinical views must allow smooth context-switching without shared reactive state leakage between CRC entry and CRA/PI sign-off flows.
- **Role-Based Authorization Gates (`PRD-CRF-009`)**: High-stakes operations (subject enrollment, batch electronic signatures, query closures) must be guarded by centralized, capability-based RBAC rather than scattered string checks.
- **GxP Audit Traceability (`PRD-SYS-001`)**: Every state-changing transaction (subject screening evaluation, consent registration, enrollment commit) must write a SHA-256 hash-chained audit block to the cryptographic ledger.
- **Strict Type Safety**: Monorepo standard requires strict TypeScript with typed Pinia store contracts.

## 3. Options Considered

### Option 1: **Option A (Selected) - Decoupled Persona Composables & Centralized Capability Matrix**:

- **Overview:** - Extract domain logic into dedicated, strongly-typed composables (`useSubjectEnrollment.ts`, `usePiSignoff.ts`, `useCrcWorkspace.ts`).
  - Introduce a centralized capability-based access control helper in `auth.js` (`authStore.can('SUBJECT_ENROLL')`).
  - Keep views thin as compositional layout orchestrators using vanilla CSS semantic tokens.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option B - Separate Standalone Vue Views per Persona**:

- **Overview:** - Create separate routes (`/crc-ecrf`, `/cra-monitoring`, `/pi-signoff`).
  - _Downside_: Violates single-pane-of-glass UX for multi-role users and increases navigation friction in clinical sites.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

### Option 3: **Option C - Status Quo Monolith with Inline Helpers**:

- **Overview:** - Keep all reactive state in `EcrfView.vue` script setup.
  - _Downside_: Excessive component complexity, high regression risk, and poor testability.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

## 4. Decision Outcome

**Chosen Option:** **Option A**.

We separate persona business logic into dedicated composables:

- `useSubjectEnrollment`: Manages subject registration form state, client-side validation, execution screening API interaction, and 21 CFR Part 11 ledger block emissions.
- `usePiSignoff`: Strongly typed composable deriving target subjects, visits, and form submissions dynamically from the Pinia clinical store, with two-factor re-authentication modal lifecycle.
- `authStore.can(capability)`: Maps normalized OIDC roles and demo presets to canonical clinical capabilities.

## 5. Consequences & Trade-offs

- **Positive**:
  - `EcrfView.vue` shrinks by >40%, acting purely as a presentational shell.
  - Zero hardcoded fallback arrays in PI sign-off targets; all entities derive reactively from store state.
  - Unit testing persona workflows is simplified via isolated composable harnesses without mounting entire view trees.
- **Negative**:
  - Requires maintaining dedicated composable unit tests alongside component specs.

## 6. Implementation & Verification

- **Modified Files**:
  - `apps/web/src/views/EcrfView.vue`
  - `apps/web/src/composables/useConsentGating.js`
  - `apps/web/src/composables/usePiSignoff.js`
  - `apps/web/src/stores/auth.js` & `apps/web/src/stores/clinical.ts`
- **Verification Suite**:
  - `apps/web/tests/components/SubjectEnrollmentForm.spec.ts`
  - `apps/web/tests/pi_signoff.test.js`
  - GxP requirement traceability synced via `uv run python scripts/sync_gxp.py` for `@req:PRD-SYS-050`.
