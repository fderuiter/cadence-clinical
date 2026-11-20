# ADR-2641: Contextual SOP Specificity Resolution and Audited Support Ticket Escalation

- **Status:** Accepted
- **Date:** 2026-11-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-KNB-001, PRD-KNB-002, PRD-TCK-001, PRD-TCK-002, PRD-SYS-001, PRD-SYS-003, PRD-SYS-192

---

## 1. Context & Problem Statement

Clinical trial staff operating across `/studies` (including `/studies/:studyCode`), `/ecrf`, `/ctms`, and `/ssu` rely on the global **Help & SOPs** slide-over (`ContextualHelpDrawer.vue`) to inspect controlled Standard Operating Procedures (SOPs) and escalate unresolved study or technical blockers into audited support tickets. Previously:

1. Neither `apps/knowledge` (`/api/v1/knowledge/**`) nor `apps/tickets` (`/api/v1/tickets/**`) exposed business endpoints beyond `GET /status`, leaving the web application to hit unmapped routes or refuse ticket creation behind `allowUnbackedAction` (#4827).
2. On initial application mount at `/ecrf`, `ContextualHelpDrawer.vue` skipped loading contextual guidance when `isOpen` started `false`, leaving the spotlight panel uninitialized until a subsequent route change.
3. Failed SOP lookups or article searches masked transport/server errors behind empty-state copy ("No Specific SOP Mapped for This Route" / "No SOPs matched") without an accessible alert or retry mechanism.
4. Legacy module-local audit tables (`KnowledgeArticleAuditLog` and `TicketAuditLog`) duplicated audit columns outside the shared SHA-256 Merkle-chained `AuditLogEntry` (`audit_log_entries`) ledger enforced by `GxPAuditInterceptor`.

## 2. Decision Drivers & Constraints

- **Topology Alignment (`ADR-2610`):** Serve `/api/v1/knowledge/**` from `apps/knowledge` and `/api/v1/tickets/**` from `apps/tickets` behind YARP gateway identity forwarding (`ADR-2256`).
- **Deterministic Specificity Scoring (`PRD-KNB-001`, `PRD-KNB-002`):** Resolve published, locked SOP snapshots deterministically across exact routes, parameterized segments (`/studies/:studyCode`), prefix wildcards (`/studies/*`), and universal fallbacks (`*`), broken by exact normalized persona match, priority weight, and creation recency.
- **21 CFR Part 11 Audit Integrity & Clock Discipline (`PRD-SYS-001`, `PRD-SYS-003`, `K-TIME-3`):** Derive all timestamps from `Cadence.Kernel.Time.IClock`, require non-empty `X-Change-Reason` headers on ticket writes, attribute mutations exclusively to verified gateway claims, and write tamper-evident records to the shared `audit_log_entries` Merkle chain via `GxPAuditInterceptor` and `AuditActions.Record`.
- **Participant De-Identification & Scope Isolation (`PRD-TCK-001`, `PRD-SYS-192`):** Reject direct participant PII in ticket titles, descriptions, subject fields, and change reasons while accepting pseudonymous subject codes (such as `SUBJ-8812`), and enforce `PermissionMatrix` plus study/site `CallerScope` checks.
- **Typed Frontend Contracts (`ADR-2808`):** Generate TypeScript OpenAPI clients (`knowledge.ts`, `tickets.ts`) via `apps/web/scripts/generate-api-types.js`, surface accessible `role="alert"` + retry controls on SOP lookup/search failures, and preserve isolated demo-mode session escalation (`SIM-...` reference IDs).

## 3. Options Considered

### Option 1: Client-Side SOP Matching and Module-Local Audit Tables

- **Overview:** Bundle static SOP markdown in `apps/web`, match routes in JavaScript, and log ticket writes to `TicketAuditLog` inside `apps/tickets`.
- **Pros:**
  - ✅ Avoids cross-service OpenAPI generation for `apps/knowledge`.
- **Cons:**
  - ❌ Violates single-source controlled SOP publication (`PRD-KNB-001`) and diverges from `AuditLogEntry` Merkle-chain verification (`PRD-SYS-003`).
  - ❌ Splits audit attribution rules across bespoke per-service tables.

### Option 2: Dedicated Knowledge & Tickets Minimal APIs Backed by Shared `AuditLogEntry` Ledger and Generated OpenAPI Clients (Selected)

- **Overview:** Implement read-only SOP resolution, article search/retrieval, and category endpoints in `apps/knowledge` using `ContextualHelpScorer` and idempotent `KnowledgeCatalogSeeder`; implement authenticated ticket creation and scoped listing in `apps/tickets` with `TicketRoutingAndSlaEngine`, `TicketPiiValidator`, `ChangeReasonHeaderFilter`, and `GxPAuditInterceptor`; regenerate OpenAPI specs and typed web clients; and update `ContextualHelpDrawer.vue`, `TicketCreateModal.vue`, and `TicketsView.vue`.
- **Pros:**
  - ✅ Preserves `ServiceTopology` boundaries (`ADR-2610`) and fail-closed RBAC (`PermissionMatrix.Knowledge.cs`, `PermissionMatrix.Tickets.cs`).
  - ✅ Routes every SOP baseline publication and ticket creation event through the shared SHA-256 Merkle-chained `audit_log_entries` ledger.
  - ✅ Eliminates `allowUnbackedAction` for ticket creation/escalation while retaining deterministic `SIM-...` session state in offline demo builds.
- **Cons:**
  - ❌ Adds two services (`knowledge`, `tickets`) to `GENERATED_SERVICES` in `generate-api-types.js`, requiring spec synchronization whenever DTO contracts evolve.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 satisfies regulatory traceability (`PRD-SYS-001`, `PRD-SYS-003`), route-level RBAC (`PRD-SYS-192`), deterministic 6-queue routing and Dual-Target SLAs (`PRD-TCK-001`, `PRD-TCK-002`), and controlled SOP delivery (`PRD-KNB-001`, `PRD-KNB-002`) without introducing bespoke audit mechanisms or unverified client-supplied identity fields.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Staff opening `/studies`, `/studies/:studyCode`, `/ecrf`, `/ctms`, or `/ssu` across all six workspace personas receive deterministic, locked SOP guidance immediately (including initial mount on `/ecrf`).
  - Failed SOP resolution or keyword search surfaces an explicit `role="alert"` banner with `#btn-contextual-help-retry` rather than misleading empty states.
  - Escalating an issue from `ContextualHelpDrawer.vue` or `TicketsView.vue` persists an audited ticket with Dual-Target SLA deadlines (`acknowledge_target_at`, `sla_target_at`), deterministic queue routing, PII rejection, and toast confirmation displaying the assigned ticket reference ID.
- **Negative Impact / Technical Debt:**
  - Legacy `KnowledgeArticleAuditLog` and `TicketAuditLog` entity classes remain in the EF schema snapshot until scaffold consolidation (#4787) removes unused scaffold tables, though no new code writes to them.
- **Mitigation Strategy:**
  - Unit and PostgreSQL integration tests explicitly assert zero rows in `KnowledgeArticleAuditLogs` and `TicketAuditLogs` alongside full `AuditLogEntry.ComputeBlockHash` predecessor chain verification.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `packages/security` (`PermissionMatrix.cs`, `PermissionMatrix.Knowledge.cs`, `PermissionMatrix.Tickets.cs`)
  - `apps/knowledge` (`ContextualHelpScorer.cs`, `KnowledgeCatalogSeeder.cs`, `KnowledgeEndpoints.cs`, `Program.cs`)
  - `apps/tickets` (`TicketRoutingAndSlaEngine.cs`, `TicketPiiValidator.cs`, `ChangeReasonHeaderFilter.cs`, `TicketEndpoints.cs`, `Program.cs`, `Migrations/`)
  - `apps/web` (`scripts/generate-api-types.js`, `src/api/generated/{knowledge,tickets}.ts`, `src/api/knowledge.ts`, `src/api/ticketsApi.ts`, `src/stores/knowledge.js`, `src/components/ContextualHelpDrawer.vue`, `src/components/tickets/TicketCreateModal.vue`, `src/views/TicketsView.vue`, `src/mocks/{demoStore,demoHandlers}.js`)
  - `docs/openapi/{knowledge,tickets}_openapi.json`
- **Verification Plan:**
  - `ContextualHelpEndpointsTests` and `TicketEndpointsTests` in `tests/Cadence.UnitTests` verify 4-tier route specificity, 6-persona SOP coverage, 6-queue routing, Dual-Target SLAs via `FakeClock`, PII rejection, `X-Change-Reason` enforcement, `CallerScope` isolation, and `AuditLogEntry` hash chains.
  - `TicketPersistenceAndAuditPostgresTests` in `tests/Cadence.IntegrationTests` verifies end-to-end PostgreSQL schema migrations, cross-context reload, and Merkle chain integrity.
  - `contextual_help_drawer.test.js`, `knowledge_failures_4523.spec.js`, `TicketsView.spec.js`, and `generated_types.test.js` in `apps/web/tests` verify initial `/ecrf` mount loading, `role="alert"` + retry recovery, live API ticket escalation toasts, and demo-mode `SIM-...` session retention.
