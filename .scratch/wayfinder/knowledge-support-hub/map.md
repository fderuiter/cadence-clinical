---
title: "Wayfinder: Unified GxP-Compliant Knowledge & Support Hub"
gh_issue: 4234
status: open
labels: [wayfinder:map, Parent, type: feature, architecture]
---

## Destination

A ready-to-execute GitHub backlog (spec written, architecture decisions locked, and implementation tickets created) for building the Cadence Knowledge & Support Hub: a new apps/knowledge/ microservice providing an in-app, GxP-compliant, role-aware knowledge base and support ticket system for the Cadence Clinical Research Software Platform.

## Notes

Skills every session should consult: grilling, domain-modeling, research

Standing decisions:
- New microservice: apps/knowledge/ (FastAPI + async SQLAlchemy + PostgreSQL)
- All personas; content is role-aware
- Articles: controlled documents (Draft to In Review to Approved to Published to Archived) + audit trail
- Support tickets: 21 CFR Part 11 fields + priority + module routing + SLA + resolution comment
- super_admin-only authoring for MVP
- Both searchable library and contextual in-page help
- Tracker: GitHub Issues (gh CLI)

## Decisions so far

- **#4237 Article Lifecycle:** 7-state machine (`DRAFT` → `IN_REVIEW` → `APPROVED` → `PUBLISHED` → `ARCHIVED`/`SUPERSEDED` + `REJECTED`), Four-eyes approval enforcement, monotonic `version_index` + `version_label`, mandatory `reason_for_change` on regulated transitions, SHA-256 audit logging across all lifecycle actions.
- **#4238 Support Ticket Routing & SLA:** 4-tier priority (`CRITICAL`, `HIGH`, `MEDIUM`, `LOW`), module queue routing (`PROTOCOL_DESIGNER`, `DATA_CAPTURE_ECRF`, `SUBJECT_MANAGEMENT_RTSM`, `REPORTING_ANALYTICS`, `PLATFORM_ADMIN_ACCESS`, `REGULATORY_COMPLIANCE`), dual-target SLA model (First Response & Resolution), auto-escalation on breach, 14-day controlled reopening window.
- **#4239 Multi-Channel Notifications & Preferences:** Multi-channel delivery across In-App inbox, asynchronous SMTP Email with domain-specific Jinja2 templates, and HMAC-SHA256 signed Outbound Webhooks (Slack/Teams/PVI). Frontend 30s interval polling with top-bar notification drawer. 10-event triggering catalog. Centralized user preference matrix in `apps/notifications` with mandatory GxP non-opt-out overrides for `CRITICAL` tickets, SLA breaches, and direct task assignments. Deterministic composite idempotency key (`{entity_type}:{entity_id}:{event_type}:{version_index}`).
- **#4240 DB Schema & Two-Tier Data Model:** Relational schema in `apps/knowledge/` separating operational entity head (`KnowledgeArticle`) from immutable snapshot versions (`KnowledgeArticleVersion`), self-referential categories (`KnowledgeCategory`), append-only audit ledger (`KnowledgeArticleAuditLog`), and route mappings (`ContextualHelpMapping`).
- **#4241 REST API Contract & Gateway:** Standardized REST endpoints under `/api/v1/knowledge/` protected by `GatewayAuthMiddleware` and HMAC signatures, strict Pydantic v2 schemas, and decoupled integration with `apps/tickets/`.
- **#4242 Contextual Help & Frontend Drawer:** Ingests route path and active persona; resolves mappings via Hierarchical Specificity Scoring (exact route > parameterized > wildcard; persona match > null; priority ASC; recency); renders in a 420px slide-in right drawer in `AppShell.vue` without compressing clinical workspaces; lazy fetching; interactive fallback with search and support ticket escalation.
- **#4243 Article Full-Text Search:** PostgreSQL `tsvector` indexed via GIN over title, tags, and body markdown, with conditional SQLAlchemy dialect guards for SQLite test harnesses.

## Child tickets

Settled & Closed:
- #4235 Research: Cadence codebase architecture survey (CLOSED)
- #4236 Research: GxP and 21 CFR Part 11 obligations (CLOSED)
- #4237 Grilling: Article lifecycle and authoring workflow (CLOSED)
- #4238 Grilling: Support ticket routing, SLA, and resolution (CLOSED)
- #4239 Grilling: Notification and alert system (CLOSED)
- #4240 Grilling: DB schema and data model (CLOSED)
- #4241 Grilling: REST API contract and gateway integration (CLOSED)
- #4242 Grilling: Contextual help mapping model (CLOSED)
- #4243 Grilling: Article search implementation (CLOSED)
- #4244 Task: Write the feature specification (CLOSED - `docs/SDLC/SPEC_Knowledge_Support_Hub.md`)
- #4245 Task: Create implementation backlog (CLOSED - #4324 to #4333)

## Implementation Backlog (Ready for Agent)

1. **#4324**: `feat(knowledge): 01 — Category Hierarchy & Persona Scoping` (Unblocked)
2. **#4325**: `feat(knowledge): 02 — Article Authoring & Working Draft Storage` (Blocked by #4324)
3. **#4326**: `feat(knowledge): 03 — Four-Eyes Review & Immutable Version Snapshotting` (Blocked by #4325)
4. **#4327**: `feat(knowledge): 04 — Article Publication, Auto-Supersede & Fast Lookups` (Blocked by #4326)
5. **#4328**: `feat(knowledge): 05 — In-Page Contextual Help Mapping & Dynamic Resolution` (Blocked by #4327)
6. **#4329**: `feat(knowledge): 06 — Full-Text Search Vector Indexing & Discovery` (Blocked by #4327)
7. **#4330**: `feat(knowledge): 07 — Auditor Read Logging & 21 CFR Part 11 Inspection Ledger` (Blocked by #4327)
8. **#4331**: `feat(knowledge): 08 — Dedicated Knowledge Hub Browser View in apps/web` (Blocked by #4327, #4329)
9. **#4332**: `feat(knowledge): 09 — Global Contextual Help Drawer & Support Ticket Escalation` (Blocked by #4328, #4331)
10. **#4333**: `feat(knowledge): 10 — End-to-End GxP Verification Suite & Automated RTM Sync` (Blocked by #4331, #4332)

