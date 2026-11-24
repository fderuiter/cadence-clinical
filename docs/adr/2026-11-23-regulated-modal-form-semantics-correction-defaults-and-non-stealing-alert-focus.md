# ADR-2670: Regulated Modal Form Semantics, Post-Entry Correction Defaults, and Non-Stealing Alert Focus

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (21 CFR Part 11 electronic signature re-authentication and attributable reason-for-change audit trails), PRD-CRF-015 (WCAG 2.2 Level AA keyboard operability, form instructions, error identification, and status messages)

---

## 1. Context & Problem Statement

Auditing regulated modal workflows across `apps/web` and `packages/ui` surfaced four interrelated compliance and accessibility defects:

1. **Inaccurate Post-Entry Audit Defaults (21 CFR Part 11 §11.10(e)):** `ReasonForChangeModal.vue` inherited `ReasonModal.vue`'s generic defaults (`"Initial Entry"` first) without overriding `options` or `defaultOption`, and `EcrfDemoWorkspace.vue` explicitly set `default-option="Initial Entry"` when editing already-populated eCRF fields or toggling SDV. Submitting a correction without changing the dropdown recorded `"Initial Entry"` in the audit trail of a post-entry modification.
2. **Focus Stealing on Inline Modal Alerts (WCAG 2.2 SC 3.3.1, SC 4.1.3):** `ModalErrorAlert.vue` unconditionally called `alertRef.value.focus()` in `onMounted` and `watch(hasError)`, yanking keyboard focus away from the credential or signing-reason input that needed correction whenever an error banner appeared.
3. **Missing `<form>` Semantics and Conditional Field Cues (WCAG 2.2 SC 2.1.1, SC 3.3.2):** `ReasonModal.vue`, `ReAuthModal.vue`, and `SignatureCaptureModal.vue` rendered inputs inside plain `<div>` containers with `type="button"` footer triggers, uneven `Enter` key handling, static `"Custom Explanation (Optional)"` labels even when `"Other"` made explanation mandatory, and enabled `#btn-confirm-sig` before required fields were populated.
4. **Stale Parent Error Dismissal in `ReAuthModal.vue`:** Clicking `Dismiss` in `ReAuthModal.vue` cleared only `localError` while leaving `props.errorMessage` visible and `effectiveError` prioritized `props.errorMessage` ahead of fresher client-side validation messages.

## 2. Decision Drivers & Constraints

- **21 CFR Part 11 Audit Accuracy (PRD-SYS-001, ADR-2620):** Post-entry correction dialogs must only present genuine modification reasons (`Typographical Error`, `Transcription Error`, `Source Data Clarification`, `Other`) and default to `"Typographical Error"`, while preserving `"Initial Entry"` on `ReasonModal.vue` solely for true initial-creation callers.
- **WCAG 2.2 AA Error Identification, Labels & Non-Disruptive Live Regions (PRD-CRF-015, ADR-2655, ADR-2808):** `role="alert"` banners (`aria-live="assertive"`) announce automatically without moving focus; keyboard focus belongs on the invalid or cleared credential/reason control (`aria-invalid="true"`, `aria-describedby`), with opt-in `autoFocus` reserved for non-field overlays.
- **Consistent Keyboard Form Submission:** Regulated modals using `ClinicalModal` split-slot footers (`#footer`) must bind `type="submit"` buttons via `form="<id>"` to `<form novalidate @submit.prevent>` bodies and support deterministic `Enter` (`Ctrl`/`Cmd+Enter` on multi-line `<textarea>`) submission.

## 3. Options Considered

### Option 1: Make `ModalErrorAlert` Non-Stealing by Default, Standardize Regulated `<form>` Semantics, and Separate Post-Entry Correction Defaults (Selected)

- **Overview:** Add `autoFocus: { type: Boolean, default: false }` to `ModalErrorAlert.vue` (and `packages/ui/index.d.ts`); wrap `ReasonModal.vue`, `ReAuthModal.vue`, and `SignatureCaptureModal.vue` in `<form novalidate @submit.prevent>` with `type="submit"` footer triggers and `Enter` / `Ctrl+Enter` handlers; dynamically switch `ReasonModal.vue`'s textarea label and `required` / `aria-required` attributes when `"Other"` is selected; configure `ReasonForChangeModal.vue`, `ObservationView.vue`, and `EcrfDemoWorkspace.vue` to exclude `"Initial Entry"` and default to `"Typographical Error"`; gate `#btn-confirm-sig` on required fields (`username`, `password`, `signingReason`); and route validation, `Retry`, `Dismiss` (`clear-error`), and post-401/step-up recovery focus directly to the actionable input with targeted `aria-invalid` and `aria-describedby` bindings.
- **Pros:**
  - ✅ Eliminates accidental `"Initial Entry"` reasons on post-entry clinical data corrections while keeping `ReasonModal.vue` backward-compatible for initial-creation flows.
  - ✅ Satisfies WCAG 2.2 SC 3.3.1, SC 3.3.2, and SC 4.1.3 without breaking existing `ModalErrorAlert` consumers or `useFocusTrap` containment.
- **Cons:**
  - ❌ Callers that want programmatic focus on `ModalErrorAlert` itself (when no invalid form control exists) must pass `:auto-focus="true"`.

### Option 2: Keep `ModalErrorAlert` Auto-Focus Enabled and Override Focus With Delayed Timers in Each Modal

- **Overview:** Leave `ModalErrorAlert` focusing itself on every error and schedule `setTimeout` focus overrides in parent modals.
- **Pros:**
  - ❌ None; causes double screen-reader announcements and timing races with Vue DOM updates.
- **Cons:**
  - ❌ Violates WCAG 2.2 SC 4.1.3 intent and introduces brittle timer coordination.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Treating `ModalErrorAlert` as a non-stealing live status/alert banner by default while pairing native `<form>` semantics, targeted `aria-invalid` / `aria-describedby` associations, and post-entry correction defaults directly addresses both Part 11 audit fidelity and WCAG 2.2 AA keyboard ergonomics cleanly.

## 5. Consequences & Trade-offs

- **Positive Impact:** Clinicians correcting observations or eCRF fields default to `"Typographical Error"` without `"Initial Entry"` pollution; screen readers announce modal errors via `role="alert"` while keyboard focus lands directly on `#sig-password`, `#reauth-credential`, `#sig-reason`, or `#<prefix>change-reason-text`; and `Dismiss` reliably clears both local and parent-supplied `ReAuthModal` errors.
- **Negative Impact / Technical Debt:** `ReAuthModal.vue` remains on the legacy raw-overlay allowlist (`ADR-2821`) until migrated onto `ClinicalModal` in a dedicated structural cleanup.
- **Mitigation Strategy:** Vitest suites in `packages/ui` and `apps/web` plus Playwright WCAG 2.2 AA E2E assertions cover `ModalErrorAlert` focus behavior, `ReasonForChangeModal` defaults, `ReAuthModal` `clear-error` and focus recovery, and `SignatureCaptureModal` disabled-submit and step-up retry flows.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `docs/adr/`, `docs/components/modal.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test`, `pnpm -r typecheck`, `pnpm lint`, `pnpm --filter web test`, Playwright `web-accessibility.spec.ts`, and `pnpm cadence check --base origin/main`.
