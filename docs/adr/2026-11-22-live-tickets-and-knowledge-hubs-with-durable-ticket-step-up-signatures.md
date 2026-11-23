# ADR-2645: Live Tickets and Knowledge Hubs with Durable Ticket Step-Up Signatures

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-TCK-001, PRD-TCK-002, PRD-KNB-001, PRD-KNB-002, PRD-SYS-001, PRD-SYS-003, PRD-SYS-192, SRS-SYS-005

---

## 1. Context & Problem Statement

ADR-2641 established contextual SOP resolution (`GET /api/v1/knowledge/contextual-help`) and support ticket creation (`POST /api/v1/tickets`), yet both `/tickets` and `/knowledge` remained excluded from `AVAILABLE_SCREENS` in live builds (`VITE_DEMO_MODE=false`):

1. Users submitting an audited support ticket from `ContextualHelpDrawer.vue` received a reference ID toast (`TKT-YYYY-NNNN`) with no live destination to track responses, document root-cause analysis (RCA), or apply 21 CFR Part 11 closure sign-off, because `apps/tickets` lacked comment, workflow transition, electronic signature, and audit-trail endpoints, and `TicketsDbContext` had no durable `consumed_signature_tokens` replay store (`ADR-2515`).
2. `TicketsView.vue`, `TicketDetailDrawer.vue`, and `KnowledgeView.vue` mixed live API state with unbacked demo controls (static KPI fallbacks, synthetic comments/attachments, client-side batch edits, and seven-state SOP Markdown authoring).
3. Six live routes (`/overview`, `/plans`, `/onboarding/trial`, `/etmf`, `/training`, and `/observations/:observationRef`) lacked seeded SOP mappings in `KnowledgeCatalogSeeder`, and two personas (`safety_officer` / `safety_reviewer` and `site_investigator`) fell back to unmapped visibility in `ContextualHelpScorer`, `RoleNormalization`, and `KnowledgeEndpoints`.

## 2. Decision Drivers & Constraints

- **Single-Use Step-Up Signatures (`ADR-2515`, `SRS-SYS-005`):** Ticket closure signatures (`POST /api/v1/tickets/{ticket_id}/sign`) must verify gateway-issued `X-Sig-Token` headers (`tickets.ticket.sign`) against a durable PostgreSQL `tickets.consumed_signature_tokens` table via `AddCadenceDurableSigTokenStore<TicketsDbContext>()`.
- **Fail-Closed RBAC & Sponsor Comment Isolation (`ADR-2516`, `PRD-SYS-192`):** Ticket mutations require explicit `tickets:comment`, `tickets:transition`, and `tickets:sign` permissions (`PermissionMatrix.Tickets.cs`), `safety_reviewer` normalizes to `sponsor_mm` (`RoleNormalization.cs`), and site-bound roles (`crc`, `investigator`, `principal_investigator`) must neither read nor post `INTERNAL_SPONSOR` comments.
- **Participant De-Identification & Shared Merkle Auditing (`PRD-TCK-001`, `PRD-SYS-001`, `PRD-SYS-003`, `K-TIME-3`):** Every ticket comment, transition, and signature must enforce `X-Change-Reason`, reject direct participant PII via `TicketPiiValidator`, derive timestamps from `IClock`, and append to `audit_log_entries` via `GxPAuditInterceptor` and `AuditActions.Record`.
- **Live Build Honesty (`#4523`, `#4827`, `#4829`):** Enabling `tickets` and `knowledge` in `AVAILABLE_SCREENS` requires hiding unbacked attachment uploads, bulk ticket edits/exports, and seven-state SOP authoring outside demo builds while wiring real version history and Merkle audit trails.

## 3. Options Considered

### Option 1: Unlock Live Read/Triage/Closure Tickets Hub and Published SOP Knowledge Hub (Selected)

- **Overview:** Mount ticket comment, workflow transition, Part 11 signature (`tickets.consumed_signature_tokens`), and audit-trail endpoints in `apps/tickets`; mount article `/versions` and `/audit-log` read endpoints plus complete live-route and 8-persona SOP seeding in `apps/knowledge`; grant `tickets:comment`, `tickets:transition`, and `tickets:sign` in `PermissionMatrix.Tickets.cs`; enable `tickets` and `knowledge` in `AVAILABLE_SCREENS` with deep links from toasts, `ContextualHelpDrawer`, `AppShell`, and Command Palette; and hide unbacked attachment/batch/authoring controls outside demo mode.
- **Pros:**
  - ✅ Completes end-to-end GxP issue escalation, triage, SLA pause/resume, RCA resolution, and single-use Part 11 closure signing over existing `TicketsDbContext` entities.
  - ✅ Gives every live workspace route and clinical persona (`safety_officer`, `site_investigator`, etc.) deterministic published SOP coverage, immutable version inspection, and SHA-256 Merkle audit verification.
  - ✅ Keeps unbacked binary attachment storage and seven-state SOP authoring strictly gated out of live builds.
- **Cons:**
  - ❌ Adds an EF Core migration (`20261122164714_AddConsumedSignatureTokens`) to `apps/tickets` prior to scaffold consolidation (#4787).

### Option 2: Bundle Binary Attachment Storage and Seven-State SOP Authoring in the Same Slice

- **Overview:** Also implement object-store attachment scanning/uploads in `apps/tickets` and full four-eyes Markdown SOP authoring in `apps/knowledge`.
- **Pros:**
  - ✅ Lights up every demo control in `TicketsView.vue` and `KnowledgeView.vue` simultaneously.
- **Cons:**
  - ❌ Expands blast radius across binary malware/PHI scanning pipelines and multi-approver SOP publication state machines that are orthogonal to operational issue triage and controlled SOP consumption.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Option 1 closes the broken escalation dead-end and contextual SOP gaps across all live workspace routes and personas using existing domain tables, shared `Cadence.Security` durable step-up token primitives, and fail-closed RBAC without exposing unbacked UI actions.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Escalation toasts (`ToastHost.vue`), `ContextualHelpDrawer.vue`, `AppShell.vue`, and `palette.ts` navigate directly to `/tickets` (including `/tickets?ref=TKT-...` auto-open) and `/knowledge`.
  - Ticket closure signatures are replay-protected across service restarts and replicas via `tickets.consumed_signature_tokens`, and signed tickets reject subsequent comments or transitions with `409 Conflict`.
  - All 15 live/core workspace routes resolve published, locked SOPs across all 8 normalized staff personas.
- **Negative Impact / Technical Debt:**
  - Evidence attachment uploads and SOP draft/review authoring remain demo-only until dedicated storage and controlled-document authoring slices land.
- **Mitigation Strategy:**
  - Unit tests (`TicketsView.spec.js`, `knowledge_view.test.js`, `screen_availability.test.js`), C# endpoint/security suites (`TicketEndpointsTests`, `ContextualHelpEndpointsTests`, `SigTokenUseStoreWiringTests`, `RouteAuthorizationTests`), and PostgreSQL 18 integration tests (`SignatureTokenSingleUsePostgresTests`, `TicketPersistenceAndAuditPostgresTests`) enforce every boundary.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `packages/security` (`RoleNormalization.cs`, `PermissionMatrix.Tickets.cs`)
  - `apps/tickets` (`Program.cs`, `TicketsDbContext.cs`, `Migrations/20261122164714_AddConsumedSignatureTokens.cs`, `TicketPiiValidator.cs`, `TicketEndpoints.cs`)
  - `apps/knowledge` (`ContextualHelpScorer.cs`, `KnowledgeCatalogSeeder.cs`, `KnowledgeEndpoints.cs`)
  - `apps/web` (`availability.js`, `toasts.js`, `ToastHost.vue`, `ContextualHelpDrawer.vue`, `AppShell.vue`, `palette.ts`, `ticketsApi.ts`, `TicketDetailDrawer.vue`, `TicketSignModal.vue`, `TicketsView.vue`, `knowledge.ts`, `KnowledgeView.vue`)
  - `docs/openapi/{tickets,knowledge}_openapi.json`, `docs/api/{tickets,knowledge}.md`, `docs/schema/tickets.md`
- **Verification Plan:**
  - `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`
  - `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj`
  - `dotnet test tests/Cadence.IntegrationTests/Cadence.IntegrationTests.csproj` (`SignatureTokenSingleUsePostgresTests`, `TicketPersistenceAndAuditPostgresTests`)
  - `pnpm -r test`, `pnpm -r lint`, `pnpm build`, and `pnpm cadence check`
