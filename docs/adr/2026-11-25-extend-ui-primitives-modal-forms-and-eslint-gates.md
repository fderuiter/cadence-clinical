# ADR-2679: Extend UI Primitives, Modal Forms, and ESLint Gates

- **Status:** Accepted
- **Date:** 2026-11-25
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015, PRD-SYS-001

## 1. Context & Problem Statement

Shared form primitives in `packages/ui` (`ClinicalFieldLayout`, `ClinicalInput`, `ClinicalFormField`, `ClinicalRadioGroup`, and `ClinicalLookupInput`), participant survey builders in `apps/subject-portal` (`clinical-fields.js`, `questionnaire.js`, and `field-errors.js`), and clinical data-capture surfaces in `apps/web` (`PlansView.vue`, `EcrfItemTable.vue`, `ObservationView.vue`, and `HeadlessFormRenderer.vue`) previously dropped input constraints (`required`, `min`, `max`, `step`), bound non-`data-*` attributes onto outer field wrappers, or omitted `aria-describedby` links for measurement units, physiological ranges, preview warnings, and `role="alert"` validation errors under WCAG 2.2 SC 1.3.1, SC 3.3.1, SC 3.3.2, and SC 4.1.2. (Draft references to `ADR-2656` were renumbered to `ADR-2679` because `ADR-2656` through `ADR-2659` and `ADR-2670` through `ADR-2678` were claimed on `main`.)

In parallel, 21 CFR Part 11 electronic signature and step-up dialogs (`ReasonModal.vue`, `ReAuthModal.vue`, `SignatureCaptureModal.vue`, and `TicketSignModal.vue`) omitted native `<form>` `Enter` submission, allowed `'Initial Entry'` on existing observation corrections, stole focus on mount in `ModalErrorAlert.vue`, or lacked `autocomplete` tokens across 22 credential and offline PIN inputs, while 22 demo files remained exempted from `custom-vue/no-vuejs-a11y-suppression`.

## 2. Decision Drivers & Constraints

- Enforce WCAG 2.2 Level A and AA compliance across all 40 web routes (including `/auditor` and `/icf-builder`) and participant survey widgets without adding duplicate component wrappers.
- Preserve slot-driven dynamic accessibility context ([ADR-252](./2026-08-11-slot-driven-dynamic-accessibility-context.md)), query-flag accessible naming ([ADR-2822](./2026-11-22-shared-a11y-primitives-and-label-in-name-ast-gates.md)), and `ClinicalModal` governance ([ADR-2653](./2026-11-23-adopt-clinical-modal-for-signatures-and-320px-workspace-reflow.md)).
- Preserve 21 CFR Part 11 password clearing, `X-Change-Reason` header encoding ([ADR-2495](./2026-09-30-reason-for-change-header-encoding.md)), single-use `X-Sig-Token` step-up tokens ([ADR-2259](./2026-09-29-electronic-signature-step-up-re-authentication.md)), and `ModalErrorAlert` programmatic focusability ([ADR-2808](./2026-10-02-modalerroralert-component-and-useasyncretry-composable-standard.md)).
- Keep auto-generated `packages/ui/src/types/cdash.ts` untouched per [ADR-328](./2026-09-11-preservation-of-cdash-type-definition-pristine-state.md) and stay within cold-boot incremental JS budgets.

## 3. Options Considered

1. **Patch individual forms and views ad hoc without updating `packages/ui` primitives or ESLint rules.** Rejected because attribute fallthrough and missing password `autocomplete` or `vuejs-accessibility` suppressions would regress in future views.
2. **Introduce separate accessible wrapper components alongside `ClinicalInput` and `ClinicalFormField`.** Rejected because parallel primitives fragment styling tokens and violate single-source UI governance.
3. **Extend existing `packages/ui` slot context, signing attribute presets, regulated `<form>` dialog patterns, and workspace-wide ESLint AST gates.** Selected because every consumer inherits constraint forwarding, `aria-describedby` merging, and compile-time lint enforcement.

## 4. Decision Outcome

We adopt Option 3 across `packages/ui`, `apps/subject-portal`, `apps/web`, and `eslint.config.mjs`:

- **Slot & Constraint Forwarding (`packages/ui` & `apps/subject-portal`):** `ClinicalFieldLayout` filters outer wrapper bindings to `data-*` attributes only, renders optional `unit` and `range` spans, marks validation errors with `role="alert"`, and exposes `{ id, error, errorId, hintId, unitId, rangeId, describedBy, hasError, required }` on its default slot. `ClinicalInput`, `ClinicalFormField`, `ClinicalRadioGroup`, and `ClinicalLookupInput` set `inheritAttrs: false` and forward `type`, `required`, `aria-required`, `min`, `max`, `step`, `aria-invalid`, and merged `aria-describedby` IDs to inner controls. `clinical-fields.js`, `questionnaire.js`, and `field-errors.js` split `containerAttrs` from `<input>` constraints, link VAS slider scale IDs and `aria-valuetext`, preserve base `aria-describedby` tokens across validation cycles, and pass `fieldLabel` into `createClinicalQueryFlag` and `createQueryPanel`.
- **Regulated `<form>` Dialogs & Non-Stealing Error Alerts:** `ReAuthModal.vue` migrates onto `ClinicalModal`, and `ReasonModal.vue`, `ReAuthModal.vue`, `SignatureCaptureModal.vue`, and `TicketSignModal.vue` wrap controls in `<form novalidate @submit.prevent>` paired with `type="submit"` `:form` footer buttons and explicit invalid-field retry focus. `ObservationView.vue` restricts modification reasons to correction-only choices defaulting to `'Typographical Error'`. `ModalErrorAlert.vue` retains `tabindex="-1"`, `role="alert"`, and `aria-live="assertive"` without stealing focus on mount or message updates.
- **Credential `autocomplete` Presets & ESLint Gates:** `packages/ui/signing.js` exports `SIGNATURE_USERNAME_INPUT_ATTRS`, `SIGNATURE_SIGNER_NAME_INPUT_ATTRS`, `SIGNATURE_TOTP_INPUT_ATTRS`, `OFFLINE_SECURITY_PIN_INPUT_ATTRS`, and `OFFLINE_SECURITY_NEW_PIN_INPUT_ATTRS` alongside `SIGNATURE_PASSWORD_INPUT_ATTRS` across all 22 credential and PIN inputs. `eslint.config.mjs` enforces `custom-vue/no-vuejs-a11y-suppression` across all of `apps/web/src/**/*.{js,ts,vue}` (0 suppressions remaining) and `custom-vue/require-password-autocomplete` across workspace Vue and JS sources.

## 5. Consequences & Trade-offs

- **Positive:** Screen readers announce units, physiological ranges, hints, preview warnings, and `role="alert"` errors consistently across staff eCRFs and participant ePRO widgets; password managers and keyboard `Enter` submission work uniformly across all regulated signing dialogs; ESLint blocks new label suppressions or bare password inputs at lint time.
- **Trade-offs:** Existing tests asserting `role="status"` on `ClinicalFieldLayout` validation errors update to `role="alert"`, and any new `<input type="password">` must explicitly declare `"current-password"`, `"new-password"`, or `"one-time-code"` (or spread the shared `signing.js` presets).

## 6. Implementation & Verification

- Rebuilt `packages/ui/dist` and updated `packages/ui/index.d.ts` while leaving `packages/ui/src/types/cdash.ts` untouched.
- Verified with `pnpm lint` (0 warnings, 0 `vuejs-accessibility` suppressions, password `autocomplete` AST gate active), `pnpm -r typecheck`, Vitest unit suites across `packages/ui`, `apps/subject-portal`, and `apps/web`, and Playwright `axe-core` WCAG 2.2 AA route scans including `/auditor` and `/icf-builder`.
