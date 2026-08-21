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

## Not yet specified

- Admin UI design for article management in apps/web
- Frontend component architecture (dedicated route? floating panel? sidebar drawer?)
- Article content taxonomy and category tree structure
- Whether PostgreSQL full-text vs. dedicated search index is needed at MVP scale

## Out of scope

(nothing ruled out yet)

## Child tickets

Settled:
- #4237 Grilling: Article lifecycle and authoring workflow (CLOSED)
- #4238 Grilling: Support ticket routing, SLA, and resolution (CLOSED)
- #4239 Grilling: Notification and alert system (CLOSED)

Frontier (unblocked):
- #4235 Research: Cadence codebase architecture survey (wayfinder:research, AFK)
- #4236 Research: GxP and 21 CFR Part 11 obligations (wayfinder:research, AFK)

Blocked:
- #4240 Grilling: DB schema and data model (blocked by #4235, #4236)
- #4241 Grilling: REST API contract and gateway integration (blocked by #4235, #4236)
- #4242 Grilling: Contextual help mapping model (blocked by #4235)
- #4243 Grilling: Article search implementation (blocked by #4240)
- #4244 Task: Write the feature specification (blocked by #4237-#4243)
- #4245 Task: Create implementation backlog (blocked by #4244)

