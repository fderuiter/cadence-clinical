# ADR-2657: Share UI Primitives and Synchronize Participant Portal Route Queries

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (21 CFR Part 11 electronic signature gating and audit provenance), PRD-CRF-015 (WCAG 2.2 Level AA orientation breadcrumbs, disabled-control guidance, and 48px touch targets), PRD-DCT-001 (BYOD participant portal deep-link restoration and mobile ergonomics), PRD-PAT-002 (plain-language receipt review and W-9 payout hold guidance), PRD-SYS-179 (protocol amendment re-consent scope isolation), PRD-SYS-182 (participant wallet ledger inspection)

---

## 1. Context & Problem Statement

`packages/ui` shipped clinician-facing overlays and controls (`ClinicalDrawer`, `ClinicalModal`, `AuditLogViewer`), yet lacked shared breadcrumb navigation, deep-link entity pills, stroke-only status iconography, a `90dvh` mobile bottom-sheet mode on `ClinicalDrawer`, and framework-neutral OIDC `studyId`/`siteId` claim extraction. Consequently, `apps/web/src/utils/callerScope.ts` kept its own claim parser while `apps/subject-portal` fell back to hardcoded `"STUDY-01"` defaults in amendment re-consent checks, mounted the Vue router before restoring cached sessions or encrypted IndexedDB questionnaire drafts (misrouting cold reloads on `/wallet` and `/questionnaire?assignmentId=...`), only validated `/questionnaire` on `START_LOCATION`, omitted URL query synchronization across portal views, exposed internal study codes and raw `0.90` OCR confidence decimals in participant copy, and lacked `aria-describedby` recovery links on `$600` W-9 holds and locked consent signature triggers.

## 2. Decision Drivers & Constraints

- **Shared Clinical & Participant Primitives (PRD-CRF-015, PRD-DCT-001):** Breadcrumbs (`<nav aria-label="Breadcrumb">`), keyboard-accessible entity buttons/links, stroke-only status icons, `90dvh` safe-area mobile bottom sheets, and OIDC claim scope helpers must live once in `packages/ui` and serve both `apps/web` and `apps/subject-portal`.
- **Deterministic Cold-Reload & History Guarding (PRD-SYS-001, PRD-DCT-001):** Initial navigation guards must run after session restoration and task/draft hydration complete, `/questionnaire` transitions must validate `assignmentId` on every history pop or push, and signature submission must fail closed when `activeQuestionnaire` is absent while preserving `#4829` `/unavailable` gating outside demo builds and ADR-2526 caller-isolated wallet idempotency.
- **6th-Grade Participant Copy & Actionable Hold Recovery (PRD-CRF-015, PRD-PAT-002, PRD-SYS-179, PRD-SYS-182):** Participant-facing headers, breadcrumbs, consent summaries, peek sheets, and OCR review notes must avoid internal protocol identifiers and raw confidence thresholds, and every compliance/tax/consent gate must expose programmatic `aria-describedby` guidance plus a direct keyboard-focusable resolution control.

## 3. Options Considered

### Option 1: Extend `packages/ui` Primitives, Hydrate Before Router Mount, and Layer Query-Synced Peek Sheets Over Existing Portal Renderers (Selected)

- **Overview:** Extend `ClinicalDrawer.vue` and `packages/ui/responsive.css` with `position="bottom"` and `mobileBottomSheet` (`max-height: 90dvh`, safe-area bottom inset, `48px` close target); add `ClinicalBreadcrumbs.vue`, `EntityLink.vue`, `ClinicalIcon.vue`, and `callerScope.js` to `packages/ui` (re-exported by `apps/web/src/utils/callerScope.ts`); reorder `apps/subject-portal/src/bootstrap/initialize.js` so session/task/draft hydration runs before `mountPortal`; synchronize `studyId`, `siteId`, and active entity query keys (`assignmentId`, `version`, `lang`, `notifId`, `claimId`, `txId`, `ledgerIndex`, `complianceId`, `syncSeq`) bidirectionally in `apps/subject-portal/src/router/index.js` while validating `/questionnaire` on every route transition; wire inbox, compliance, wallet, audit ledger, and sync queue rows to `PortalPeekSheet.vue` (`90dvh` `ClinicalDrawer`) with focus restoration; replace internal study codes and `0.90` OCR threshold notes in `consent.js`, `mocks/handlers.js`, and `PaymentEndpoints.cs` with plain-language copy; and bind `aria-describedby` explanations and direct jump links on W-9 holds, disabled `#btn-trigger-consent-sign` triggers, and `ReconsentBanner.vue`.
- **Pros:**
  - ✅ Shares UI components, responsive bottom-sheet tokens, and OIDC claim helpers cleanly across `packages/ui`, `apps/web`, and `apps/subject-portal` without duplicating logic or adding third-party dependencies.
  - ✅ Preserves deep-linked `/questionnaire` and `/wallet` cold reloads, browser Back/Forward restoration, `#4829` `/unavailable` gating, and DOM IDs relied on by imperative portal modules.
- **Cons:**
  - ❌ Keeps imperative DOM renderers (`inbox.js`, `compliance.js`, `consent.js`, `audit.js`) alongside Vue shell components (`App.vue`, `PortalPeekSheet.vue`, `WalletView.vue`) rather than rewriting every portal tab into nested SFC routes in one pass.

### Option 2: Rewrite Every Portal Feature Module into Nested Vue SFC Child Routes

- **Overview:** Replace `inbox.js`, `compliance.js`, `consent.js`, `questionnaire.js`, and `audit.js` with nested `<RouterView>` SFC hierarchies before introducing query-synced deep links or peek sheets.
- **Pros:**
  - ✅ Consolidates template rendering entirely inside Vue SFCs.
- **Cons:**
  - ❌ Violates scope boundaries, churns validated offline sync and PIN-encrypted draft flows, and breaks DOM-targeted integration contracts unnecessarily.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Extending `packages/ui` with shared navigation, entity-link, icon, bottom-sheet, and OIDC claim primitives while sequencing portal bootstrap hydration ahead of router evaluation delivers bookmarkable query-synced routes, `90dvh` peek inspection, plain-language copy, and accessible gate recovery with minimal blast radius.

## 5. Consequences & Trade-offs

- **Positive Impact:** Both apps consume a single `callerScope` implementation and shared WCAG 2.2 AA clinical UI primitives; participant deep links survive cold reloads and Back/Forward transitions; row clicks open a `90dvh` peek sheet that restores trigger focus on close; and receipt/consent/tax holds explain recovery steps in 6th-grade language.
- **Negative Impact / Technical Debt:** Portal feature modules that render into static container IDs coordinate peek selection through `src/features/peek.js` and `src/router/index.js` query state until future view-by-view SFC migrations occur.
- **Mitigation Strategy:** Unit and E2E suites in `packages/ui`, `apps/subject-portal`, `apps/web`, and `Cadence.UnitTests` enforce bottom-sheet sizing, zero-emoji compliance, OIDC claim precedence, cold-reload query restoration, focus return, and plain-language OCR notes.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `apps/payments`, `tests/Cadence.UnitTests`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test`, `pnpm --filter ui lint`, `pnpm --filter subject-portal test`, `pnpm --filter subject-portal lint`, `pnpm --filter subject-portal test:e2e`, `pnpm --filter web typecheck`, `pnpm --filter web lint`, `dotnet test --filter ParticipantSelfServiceAndOcrTests`, and `cadence check` quality gates.
