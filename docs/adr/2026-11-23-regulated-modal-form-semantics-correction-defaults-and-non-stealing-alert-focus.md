# ADR-2823: Regulated Modal Form Semantics, Correction Defaults, and Non-Stealing Alert Focus

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (21 CFR Part 11 electronic signature re-authentication and GxP audit change-reason accuracy), PRD-CRF-015 (WCAG 2.1 SC 3.3.1 Error Identification, SC 3.3.2 Labels or Instructions, and WCAG 2.2 AA dialog keyboard submission and focus retention)

---

## 1. Context & Problem Statement

Following the migration of electronic signature and GxP change-reason dialogs onto `ClinicalModal` (ADR-2653) and the standardization of `ModalErrorAlert` across modal overlays (ADR-2808), three workflow and accessibility gaps remained across `ReasonModal.vue` (`ReasonForChangeModal.vue`), `ReAuthModal.vue`, `SignatureCaptureModal.vue`, and `ModalErrorAlert.vue`:

1. **Missing `<form>` Semantics and Premature Submit Availability:** `ReasonModal.vue`, `ReAuthModal.vue`, and `SignatureCaptureModal.vue` wrapped their fields in `<div>` containers with `type="button"` actions instead of semantic `<form @submit.prevent>` elements, preventing standard keyboard submission from `<select>` controls (`Enter`) and `<textarea>` controls (`Ctrl+Enter` / `Cmd+Enter`), while `#btn-confirm-sig` in `SignatureCaptureModal.vue` remained enabled even when username, password, or signing reason was blank.
2. **Correction Reason Defaults and `Other` Required Instructions:** Data-correction callers (`ReasonForChangeModal.vue`, `ObservationView.vue`, and `EcrfDemoWorkspace.vue`) included `"Initial Entry"` and defaulted to `"Initial Entry"` when modifying an existing clinical value, risking inaccurate 21 CFR Part 11 / ICH E6(R2) audit trail entries (`X-Reason-For-Change`, ADR-2495), while selecting `"Other"` did not surface a required label or `required` / `aria-required="true"` attributes before validation failed.
3. **Unconditional `ModalErrorAlert` Focus Stealing and Retry Focus Recovery:** `ModalErrorAlert.vue` unconditionally called `alertRef.value.focus()` inside `onMounted` and its `hasError` watcher even though the root container already announces errors via `role="alert"` and `aria-live="assertive"`. When `ReAuthModal` or `SignatureCaptureModal` re-enabled credentials after an async `401` / `REAUTHENTICATION_REQUIRED` failure (`busy = false`), `ModalErrorAlert` stole keyboard focus away from `#reauth-credential` or `#sig-password`, and `ReAuthModal` lacked `clear-error` synchronization with parent `errorMessage` props (`MediaPreviewModal.vue`).

## 2. Decision Drivers & Constraints

- **21 CFR Part 11 & ICH E6(R2) Audit Trail Fidelity (PRD-SYS-001):** Correction dialogs modifying existing clinical values must exclude `"Initial Entry"` and default to `"Typographical Error"`, while authoring dialogs creating or publishing drafts (`ConsentAuthoringView.vue`, `RulesView.vue`, `MdrView.vue`) retain `"Initial Entry"` in `ReasonModal.vue`'s base defaults.
- **WCAG 2.1 SC 3.3.1 / SC 3.3.2 & Keyboard Ergonomics (PRD-CRF-015):** Regulated dialogs must use native `<form novalidate @submit.prevent>` submission semantics, associate inline validation alerts with invalid inputs via `aria-invalid` and `aria-describedby`, mark custom `"Other"` explanations with a visible required label and `aria-required="true"`, and keep focus on actionable credential/reason controls during error recovery.
- **Backward Compatibility Across Shared UI Consumers:** `ModalErrorAlert.vue` must preserve `role="alert"`, `aria-live="assertive"`, and `tabindex="-1"` while making programmatic focus opt-in (`autoFocus` prop defaulting to `false`), and `ClinicalModal` form associations (`form="..."` + single-emit click fallback) must work in both attached browser DOMs and detached Vitest mounts.

## 3. Options Considered

### Option 1: Opt-In `ModalErrorAlert` Auto-Focus, Native `<form>` Semantics, Correction-Specific Defaults, and Credential Focus Recovery (Selected)

- **Overview:** Gate programmatic `.focus()` in `packages/ui/src/components/ModalErrorAlert.vue` (and `packages/ui/index.d.ts`) behind an opt-in `autoFocus` prop (`default: false`, `flush: "post"`). Wrap `ReasonModal.vue`, `ReAuthModal.vue`, and `SignatureCaptureModal.vue` fields in `<form novalidate @submit.prevent>`, wire `type="submit"` buttons and keyboard shortcuts (`Enter` on `<select>`, `Ctrl/Meta+Enter` on `#change-reason-text`), surface required labels and ARIA bindings (`aria-required`, `aria-invalid`, `aria-describedby`), exclude `"Initial Entry"` with `defaultOption: "Typographical Error"` in correction callers (`ReasonForChangeModal.vue`, `ObservationView.vue`, `EcrfDemoWorkspace.vue`), synchronize `clear-error` between `ReAuthModal.vue` and `MediaPreviewModal.vue`, disable `#btn-confirm-sig` until required signature fields are populated, and restore focus to `#reauth-credential`, `#sig-password`, or `#sig-reason` across validation, `Retry`, `Dismiss`, and post-`busy` (`await nextTick()`) credential failures.
- **Pros:**
  - ✅ Preserves assertive screen-reader announcements in `ModalErrorAlert` without stealing focus from password or reason inputs during step-up retry flows (ADR-2259, ADR-2515).
  - ✅ Prevents accidental `"Initial Entry"` audit reasons on post-entry clinical corrections while keeping authoring workflows unchanged.
  - ✅ Satisfies WCAG 2.1 SC 3.3.1, SC 3.3.2, and native keyboard form submission across both attached browsers and detached unit test mounts.
- **Cons:**
  - ❌ Callers that explicitly want `ModalErrorAlert` to take focus on mount must pass `:auto-focus="true"`.

### Option 2: Override `ModalErrorAlert` Focus with Delayed `setTimeout` Calls Inside Individual Modals

- **Overview:** Keep unconditional `onMounted` focus in `ModalErrorAlert.vue` and add `setTimeout` focus overrides in `ReAuthModal.vue` and `SignatureCaptureModal.vue`, leaving `<div>` containers and `"Initial Entry"` defaults unchanged.
- **Pros:**
  - ✅ Avoids changing `packages/ui`.
- **Cons:**
  - ❌ Introduces timing races and double focus announcements for assistive technologies, leaves `"Initial Entry"` as the default on clinical data corrections, and omits `<form>` semantics and `SC 3.3.2` required indicators.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Making `ModalErrorAlert` programmatic focus opt-in (`autoFocus: false` by default) while pairing assertive ARIA live announcements with explicit input focus recovery (`aria-invalid` + `aria-describedby`), native `<form>` submission semantics, and `"Typographical Error"` correction defaults eliminates focus races and audit-reason papercuts cleanly across `packages/ui` and `apps/web`.

## 5. Consequences & Trade-offs

- **Positive Impact:** Clinicians can submit regulated reason, re-authentication, and signature dialogs with standard keyboard gestures, see clear required-field guidance before submitting `"Other"` explanations, avoid mislabeling data corrections as `"Initial Entry"`, and immediately re-type credentials after `401` / `REAUTHENTICATION_REQUIRED` step-up failures without losing focus to the alert banner.
- **Negative Impact / Technical Debt:** `packages/ui` must be rebuilt (`pnpm --filter ui build`) before running `apps/web` tests or builds so `apps/web` consumes the compiled `ModalErrorAlert` changes.
- **Mitigation Strategy:** Covered by regression tests in `packages/ui/tests/ModalErrorAlert.test.js`, `apps/web/tests/reason_modal.test.js`, `apps/web/tests/components/ReasonForChangeModal.spec.js`, `apps/web/tests/views/ObservationView.spec.ts`, `apps/web/tests/components/ReAuthModal.spec.ts`, `apps/web/tests/components/ModalErrorAlertRetry.spec.ts`, and Playwright E2E WCAG 2.2 AA modal checks.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `docs/components/modal.md`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm test`, `pnpm build`, `pnpm lint`, Playwright WCAG 2.2 AA accessibility specs, and Cadence CLI quality sentinels.
