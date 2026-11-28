# ADR-FRONTEND-20261128-centralize-shared-live-region-primitives-and-ast-gates: Centralize Shared Live-Region Primitives and AST Gates

- **Status:** Accepted
- **Date:** 2026-11-28
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Screen-reader users previously received silent or degraded status feedback across clinical staff hubs and trial participant workflows:

1. **Silent Filter and Async Transitions (WCAG 2.2 SC 4.1.3 / W3C Failure F103):** Staff hubs (`/knowledge`, `/tickets`, `/documents`, `/overview`, `/studies/:studyId?stage=data&section=queries`, and clinical consoles) and participant portal routes (`/inbox`, `/compliance`) updated visible result rows, active filter chips, or loading indicators without updating a programmatic live region, while `ClinicalDataTable` omitted `aria-busy` during loading and unmounted `.pagination-info` across single-page, external-pagination, `#pagination` slot, and empty states.
2. **Conditional `v-if` Status Mounts (W3C Technique ARIA22):** Mounting `role="status"` or `aria-live="polite"` containers simultaneously with their text via `v-if` (including `ClinicalDropzone` `.clinical-dropzone-selected`) causes screen readers on Windows/macOS/iOS to miss initial announcements because the live container was not registered in the accessibility tree prior to content insertion.
3. **Conflicting and Under-Escalated Error Semantics (W3C Technique ARIA19):** Six GxP outcome banners (`ExportsSection`, `LocksSection`, `ParticipantRecord`, `QueriesSection`, `ScopeLockControls`, `EcrfSdvToolbar`) paired `:role="outcome.error ? 'alert' : 'status'"` with static `aria-live="polite"`, downgrading GxP lock, export, and SDV failures to polite announcements. Twelve validation and operational error banners across `ClinicalFieldLayout`, `ReasonModal`, `StudyPulse`, `CtmsSectionScopeBar`, `SiteActivationPanel`, `ConsentAuthoringView`, `PinSetupModal`, `PinUnlockModal`, `ReconsentModal`, and `SignatureModal` used `role="status"` instead of `role="alert"`, or nested retry controls inside `aria-atomic="true"` spans.

## 2. Decision Drivers & Constraints

- **WCAG 2.2 AA & Section 508 Compliance:** Satisfy WCAG 2.2 SC 4.1.3 (Status Messages), W3C Technique ARIA22 (persistent `role="status"`), W3C Technique ARIA19 (`role="alert"` for validation/operational errors), and eliminate W3C Failure F103 without stealing keyboard focus from active filter inputs.
- **Preserve Existing Contracts:** Retain all table props, keyboard sorting, and focusable row controls from [ADR-2656](../2026-11-23-clinical-data-table-keyboard-and-aria-compliance.md), consumer reachability guards from [ADR-2677](../2026-11-24-retire-dead-ui-exports-and-enforce-consumer-guard.md), zero-emoji Lucide `1.75px` vector iconography ([ADR-2679](../2026-11-25-centralize-lucide-icons-and-cli-emoji-gate.md)), and `apps/web` cold-boot JavaScript budgets with zero new npm dependencies.
- **Participant Portal Guardrails:** Keep `apps/subject-portal` copy at a plain reading level with `>= 48px` touch targets (`--semantic-touch-target-min`) and zero exposed protocol codes.
- **Automated Regression Prevention:** Enforce live-region invariants statically in `eslint.config.mjs` via `vue-eslint-parser` template AST visitors.

## 3. Options Considered

### Option 1: Shared `ClinicalLiveRegion` + `useStatusAnnouncer` in `packages/ui` with Custom ESLint Vue AST Gates (Selected)

- **Overview:** Export `ClinicalLiveRegion` and `useStatusAnnouncer` from `packages/ui` (`ui`), upgrade `ClinicalDataTable`, `EmptyState`, `ClinicalDropzone`, and `ClinicalFieldLayout` to keep live containers mounted, migrate silent views, GxP outcome banners, and error banners across `apps/web` and `apps/subject-portal`, and enforce three custom Vue AST rules (`custom-vue/no-conflicting-live-region`, `custom-vue/no-status-role-on-errors`, `custom-vue/no-conditional-status-mount`) backed by a shrink-only count-ratcheted legacy allowlist.
- **Pros:**
  - Keeps both `[role="status"][aria-live="polite"]` and `[role="alert"]` nodes mounted in the DOM from initial render so ARIA22 and ARIA19 announcements fire reliably across NVDA, JAWS, and VoiceOver without dynamic role mutation.
  - Places `#pills` and `#actions` slots (`Reset All Filters`, `Retry`, `Reload`) as siblings outside `aria-atomic="true"` message spans so action labels are not concatenated into spoken status sentences.
  - Debounces rapid keystroke filters and clears consecutive identical messages across ticks (`""` -> `nextTick` -> `text`) so repeated filter resets or repeated PIN errors are spoken every time.
  - Prevents regressions at lint time with zero runtime third-party dependencies.
- **Cons:**
  - Requires maintaining a shrink-only count-ratcheted allowlist (`scripts/eslint/allowlists/live-regions.mjs`) for untouched legacy `v-if` status lines until they are migrated opportunistically.

### Option 2: Global Singleton DOM Announcer Only Without Inline Status Strips or AST Rules

- **Overview:** Inject a single visually hidden `#global-live-announcer` at `document.body` and leave existing inline banners and `eslint.config.mjs` unchanged.
- **Pros:**
  - Minimal template changes across feature views.
- **Cons:**
  - Fails to fix downgraded `:role="... ? 'alert' : 'status'" aria-live="polite"` GxP banners or `role="status"` error banners in the accessibility tree, provides no visible filter match strip for sighted/low-vision coordinators, and allows new `v-if` status regressions to land unchecked.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Justification:** Centralizing `ClinicalLiveRegion` and `useStatusAnnouncer` in `packages/ui` fixes the structural DOM mounting lifecycle once for both applications, delivers cohesive visible status strips with tabular numerals (`tabular-nums`) and sibling filter-reset/retry controls, and locks in ARIA19/ARIA22 compliance through deterministic ESLint AST rules.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Filtering `/documents`, `/knowledge`, `/tickets`, `/studies/:studyId?stage=data&section=queries`, and clinical consoles updates pre-mounted `role="status"` regions politely while focus stays inside the active search/filter input.
  - All 17 `ClinicalDataTable` instances keep `.clinical-table-live-summary[role="status"]` mounted across client, external, `#pagination` slot, loading (`aria-busy="true"`), and zero-row states.
  - All 6 GxP outcome banners and all 12 error banners expose `live: "assertive"` (`role="alert"`) with sibling action buttons outside atomic text spans.
- **Negative Impact / Technical Debt:**
  - Legacy untouched `v-if` `role="status"` nodes remain capped at their exact per-file counts in `scripts/eslint/allowlists/live-regions.mjs`.
- **Mitigation Strategy:**
  - `custom-vue/no-conditional-status-mount` blocks any unallowlisted `v-if` status mount and any count increase in legacy files, ensuring monotonic burn-down.

## 6. Implementation & Verification

- **Affected Packages & Applications:**
  - `packages/ui`: `src/components/clinical/ClinicalLiveRegion.vue`, `src/composables/useStatusAnnouncer.js`, `ClinicalDataTable.vue`, `EmptyState.vue`, `ClinicalDropzone.vue`, `ClinicalFieldLayout.vue`, `src/index.js`, `index.d.ts`.
  - `apps/web`: `DocumentsHubView.vue`, `KnowledgeView.vue`, `TicketsView.vue`, `OverviewView.vue`, `NotificationsView.vue`, `AuditView.vue`, `AuditTrailViewer.vue`, `QualityCockpitView.vue`, `ImagingView.vue`, `LaySummaryPortal.vue`, `RulesView.vue`, `MdrView.vue`, `ProtocolDigitizationView.vue`, clinical consoles, canvas GxP outcome banners, and error banners.
  - `apps/subject-portal`: `InboxView.vue`, `ComplianceView.vue`, `PinSetupModal.vue`, `PinUnlockModal.vue`, `ReconsentModal.vue`, `SignatureModal.vue`, `features/signature.js`.
  - `scripts/eslint/`: `rules/live-region-semantics.mjs`, `allowlists/live-regions.mjs`, `eslint.config.mjs`.
- **Verification Plan:**
  - Unit and AST lint tests in `packages/ui/tests/live_region_primitives.test.js`, `design_system_harmony.test.js`, and `ui_consumer_reachability.test.js`.
  - `pnpm exec eslint --max-warnings 0 .`, `pnpm typecheck`, `pnpm --filter ui test`, `pnpm --filter web test`, `pnpm --filter subject-portal test`, and Playwright accessibility + cold-boot bundle suites.
