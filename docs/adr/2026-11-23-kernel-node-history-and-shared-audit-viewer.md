# ADR-2659: Kernel Node History, Audit Entry Resolution, and Shared AuditLogViewer

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001, PRD-SYS-004, PRD-SYS-192

---

## 1. Context & Problem Statement

Every write through `KernelCommandPipeline` (`ADR-2449`) atomically records a `kernel.provenance_activities` row, a hash-chained `kernel.audit_entries` link, and `kernel.lineage_nodes` provenance, while signed actions record `kernel.attestations` (`ADR-2452`) and observation corrections append `kernel.observation_revisions` (`ADR-2621`). However, several read-side and UI gaps prevented regulated users and participants from inspecting object history and tamper-evident audit context consistently:

1. **Unaddressable Audit Entries and Missing `PX-12` Object History:** `NodeKinds` did not register `audit_entry`, `KernelScopeGraph` and `GrantNodeVisibility` could not resolve or mask `audit_entry:<uuid>` references, `Cadence.Kernel.Api` lacked `GET /api/v1/kernel/nodes/{nodeRef}/history` and `GET /api/v1/kernel/audit-entries/{auditRef}`, and `CanvasInspector.vue` omitted the `History` verb (`PX-12`).
2. **Incomplete Activity Stream Context:** `ActivityProjection` (`ADR-2485`) omitted `reason_for_change`, `audit_entry_ref`, and `attestation_ref`, preventing `StudyActivity.vue` from linking activity rows to their underlying audit events and 21 CFR Part 11 attestations.
3. **Fragmented Audit Surfaces, Unbacked Endpoints, and Imperative Participant DOM:** `ObservationView.vue`, `KnowledgeView.vue`, `TicketDetailDrawer.vue`, and `StudyActivity.vue` rendered custom ad-hoc audit lists (including a hardcoded `"sha256:e3b0c442"` placeholder in `KnowledgeView.vue`); `AuditView.vue` called unbacked `/api/v1/etmf/audit-logs` and `/api/v1/execution/audit/integrity` routes in non-demo builds; and `apps/subject-portal` rendered `LedgerFooter.vue` via imperative `innerHTML` manipulation without reactive Vue links.

## 2. Decision Drivers & Constraints

- **Unified Clinical Canvas History & Entity Deep-Linking (`PRD-SYS-001`, `ADR-2481`, `PX-12`):** Selecting any `NodeRef` in `CanvasInspector` must expose a `History` tab backed by on-demand kernel reads, and audit actors, targets, and attestations must render as interactive `<EntityLink>` chips with progressive-disclosure `SupportDetails` for chain hashes and sequence metadata.
- **21 CFR Part 11 Audit Trail & Blinding Isolation (`PRD-SYS-004`, `PRD-SYS-192`, `K-AUTH-4`, `ADR-2457`, `ADR-2471`):** Object history and audit entry reads must enforce scope isolation and `enrollment.unblind` checks via `GrantNodeVisibility`, returning in-place masked placeholders (`NotPermitted` or `Blinded`) rather than silently omitting records or leaking treatment-arm values.
- **Zero Duplicate Audit Write Projections (`ADR-2449`, `ADR-2485`):** `GET /api/v1/kernel/nodes/{nodeRef}/history` and `GET /api/v1/kernel/audit-entries/{auditRef}` must read directly from `kernel.audit_entries`, `kernel.provenance_activities`, `kernel.attestations`, `kernel.lineage_nodes`, `kernel.observation_revisions`, `kernel.assertions`, and `kernel.validation_detections` without introducing a second audit write projection, while `ActivityProjection` enriches `kernel.activity_entries` on `ActivityId`.
- **Fail-Safe Non-Demo Routing & Accessible Participant Ledger:** Non-demo builds must never call unbacked `/api/v1/etmf/audit-logs` or `/api/v1/execution/audit/integrity` routes, and `LedgerFooter.vue` must use keyed reactive Vue templates preserving `<details>` disclosure and focus state while routing participants to `/consent`, `/compliance`, and `/wallet` in plain language.

## 3. Options Considered

### Option 1: Materialize a Separate Per-Object History Projection Table and Keep Per-Screen Audit Tables

- **Overview:** Project every domain event into a new `kernel.node_history_entries` table via the outbox and keep separate custom audit markup in `ObservationView`, `KnowledgeView`, `TicketDetailDrawer`, and `StudyActivity`.
- **Pros:**
  - Pre-joins history rows at write/outbox time.
- **Cons:**
  - Duplicates `kernel.audit_entries` and `kernel.provenance_activities`, introduces projection lag on the `History` inspector verb, leaves custom audit tables inconsistent across clinical views, and leaves imperative `innerHTML` in `apps/subject-portal`.

### Option 2: Direct Kernel History & Audit Entry Readers, Enriched Activity Projection, Shared `AuditLogViewer` + `EntityLink` + Peek Drawer, and Reactive Participant Ledger (Selected)

- **Overview:** Register `AuditEntryId` and `NodeKinds.AuditEntry` (`audit_entry`) across `KernelScopeGraph`, `KernelAuthorityStore`, `GrantNodeVisibility`, `ProductFamilyGuardMiddleware`, and `apps/web/src/canvas/`; expose `GET /api/v1/kernel/nodes/{nodeRef}/history` (with optional `valid_at` and `known_at` filters) and `GET /api/v1/kernel/audit-entries/{auditRef}` via `KernelHistoryReader` with in-place `K-AUTH-4` masking; enrich `ActivityProjection` and `GET /api/v1/kernel/activity` with `reason_for_change`, `audit_entry_ref`, and `attestation_ref` (masked when hidden); ship `EntityLink.vue`, `SupportDetails.vue`, `auditAdapters.js`, and an upgraded `AuditLogViewer.vue` with a `ClinicalDrawer` `Audit Event` peek drawer in `packages/ui`; adopt them across `CanvasInspector.vue` (`#verb-history`), `StudyActivity.vue`, `ObservationView.vue`, `KnowledgeView.vue`, `TicketDetailDrawer.vue`, and `AuditView.vue` (rewiring non-demo `auditor.ts` calls to `GET /api/v1/kernel/activity`); and refactor `LedgerFooter.vue` and `features/audit.js` in `apps/subject-portal` to keyed reactive Vue rendering with `/consent`, `/compliance`, and `/wallet` deep links.
- **Pros:**
  - Immediate read-after-write consistency for object history and audit entry inspection with zero new audit write tables.
  - Preserves `K-AUTH-4` blinding and scope isolation with explicit in-place masked placeholders across node resolution, history timelines, audit entry detail, and activity streams.
  - Unifies GxP audit presentation, `<EntityLink>` navigation, and progressive `SupportDetails` disclosure across `packages/ui` and `apps/web` while eliminating `innerHTML` writes in `apps/subject-portal`.
- **Cons:**
  - Requires normalizing heterogeneous domain audit shapes (`Observation`, `Knowledge`, `Tickets`, `Kernel`) via client adapters in `packages/ui` and adding three nullable columns to `kernel.activity_entries`.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Reading object history and audit entry details directly from the kernel's authoritative `audit_entries`, `provenance_activities`, `attestations`, `observation_revisions`, `assertions`, and `lineage_nodes` tables guarantees zero-lag Part 11 traceability and uniform `GrantNodeVisibility` masking, while shared `AuditLogViewer`, `EntityLink`, and `SupportDetails` primitives eliminate bespoke audit markup, fake hashes, unbacked API calls, and imperative DOM mutations.

## 5. Consequences & Trade-offs

- **Positive Impact:** Every selectable `NodeRef` in Clinical Canvas now exposes a `History` (`PX-12`) timeline with `valid_at`/`known_at` bitemporal filtering; `audit_entry:<id>` is addressable and scope/blinding-aware across the kernel and web canvas; `StudyActivity`, `ObservationView`, `KnowledgeView`, `TicketDetailDrawer`, and `AuditView` share one accessible `AuditLogViewer` and `Audit Event` peek drawer; and `apps/subject-portal` renders participant activity reactively with plain-language route links.
- **Negative Impact / Technical Debt:** `KernelHistoryReader` joins across `audit_entries`, `provenance_activities`, `attestations`, and domain revision/assertion tables on demand, bounded by `MaxHistoryLimit` (`500`).
- **Mitigation Strategy:** Queries use indexed tenant/aggregate/activity lookups (`AsNoTracking()`) bounded by `limit`, covered by PostgreSQL integration tests across all kernel domain entity kinds, `K-AUTH-4` blinding/scope tests, and Vitest component suites.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel` (`Abstractions`, `Application`, `Persistence`, `Api`), `packages/security` (`ProductFamilyGuardMiddleware`), `packages/ui` (`AuditLogViewer.vue`, `EntityLink.vue`, `SupportDetails.vue`, `auditAdapters.js`), `apps/web` (`canvas`, `api/kernel`, `api/auditor.ts`, `CanvasInspector.vue`, `StudyActivity.vue`, `ObservationView.vue`, `KnowledgeView.vue`, `TicketDetailDrawer.vue`, `AuditView.vue`, `mocks/demoHandlers.js`), `apps/subject-portal` (`LedgerFooter.vue`, `features/audit.js`), `docs/openapi/kernel_openapi.json`, `docs/api/kernel.md`, `docs/schema/`.
- **Verification Plan:** Verified via `Cadence.IntegrationTests` (`K-AUTH-4` blinding/scope masking, `valid_at`/`known_at` bitemporal filtering, 12+ domain entity history coverage, `ActivityProjection` enrichment), `Cadence.UnitTests`, `Cadence.ArchitectureTests`, `packages/ui`, `apps/web`, and `apps/subject-portal` Vitest suites, and `pnpm cadence check`.
