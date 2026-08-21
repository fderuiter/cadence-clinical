# ADR-2187: Knowledge Hub Multi-Channel Notifications and Preferences Architecture

- **Status:** Accepted
- **Date:** 2026-08-21
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirement Reference:** PRD-KNB-002 | 21 CFR Part 11 | Trace-8

---

## 1. Context & Problem Statement

The Cadence Knowledge & Support Hub (`apps/knowledge/`) and Support Ticket workflows require a robust, multi-channel notification engine to alert trial personnel across clinical operations, medical monitoring, data management, and study design. Because Cadence operates in regulated clinical trial environments, notification delivery must balance rich communication channels with strict 21 CFR Part 11 audit trails, uncompromised blinding/PII protection, and regulatory non-opt-out mandates for safety-critical alerts.

This decision settles the notification delivery channels, frontend polling model, email templating, triggering event catalog, user preference matrix, webhook subscription security, and deduplication contracts.

## 2. Decision Drivers & Constraints

- **Multi-Channel Reach:** Support In-App notification center, SMTP Email delivery, and outbound Webhooks (e.g. Slack/Teams/PVI) across all lifecycle events.
- **GxP Compliance & 21 CFR Part 11:** Immutable audit trail of every notification creation, view, acknowledgment, resolution, and delivery attempt; tamper-evident append-only logs with `before_flush` session guards.
- **Blinding & Privacy (HIPAA/GDPR/DEID):** Outbound webhooks and external transmissions must transmit de-identified metadata and deep links without exposing blinded treatment codes, PHI, or sensitive clinical narratives.
- **Regulatory Non-Opt-Out Mandate:** Safety-critical events (`CRITICAL` severity tickets, SLA breaches, direct protocol task assignments) must strictly bypass user opt-out preferences.
- **Decoupled Asynchronous Reliability:** Transient SMTP or webhook outages must never block or roll back core knowledge or ticketing transactions.

## 3. Options Considered

1. **Option A (Decoupled Multi-Channel Engine with Preference Matrix)**: Centralized async dispatch in `apps/notifications/` supporting In-App, Email, and Webhook transports with GxP non-opt-out overrides.
2. **Option B (Direct Service SMTP Dispatch)**: Each microservice dispatches emails directly using independent SMTP configurations.
3. **Option C (Synchronous Webhook Calling)**: Invoke webhooks synchronously in the request path of mutating operations.

## 4. Decision Outcome

Chosen option: **Option A (Decoupled Multi-Channel Engine with Preference Matrix)** because it provides high availability, fault isolation, and centralized regulatory compliance tracking.

### Key Architectural Specifications:

### 1. Delivery Channels
The system supports three delivery transports:
- **In-App:** Delivered to the centralized in-app notification inbox.
- **Email:** Dispatched asynchronously via `aiosmtplib` using dedicated Jinja2 templates in `apps/notifications/templates/` with 21 CFR Part 11 header metadata.
- **Outbound Webhooks:** Dispatched to registered external endpoints with HMAC-SHA256 payload signatures (`X-Cadence-Signature`).

### 2. Frontend In-App Notification Model
- The web client (`apps/web`) leverages `useNotificationsStore` with **interval polling at 30 seconds** plus immediate revalidation on route navigation.
- Persistent top-bar notification bell displaying real-time unread count (red flashing indicator for `CRITICAL` / `SLA_BREACH`), quick-triage popover drawer, and link to full `/notifications` data view.

### 3. Triggering Event Catalog (10 Events)
1. `knowledge.article.submitted_for_review`: To `super_admin` reviewers (`IN_APP`).
2. `knowledge.article.approved` / `rejected`: To article author & last editor (`IN_APP`).
3. `knowledge.article.published`: To all personas matching `KnowledgeCategory.persona_visibility` (`IN_APP` + `EMAIL`).
4. `knowledge.article.archived`: To `super_admin` (`IN_APP`).
5. `ticket.created`: To default functional module queue (e.g. `sponsor_dm` for `DATA_CAPTURE_ECRF`) (`IN_APP` + `EMAIL`).
6. `ticket.assigned`: To specific assigned user (`IN_APP` + `EMAIL`).
7. `ticket.comment_added`: To reporter and assignee, excluding actor (`IN_APP`).
8. `ticket.status_changed`: To reporter and assignee (`IN_APP`).
9. `ticket.sla_warning` / `ticket.sla_breached`: To assignee and module queue supervisor (`IN_APP` + Urgent `EMAIL`).
10. `ticket.resolved` / `closed`: To ticket reporter with resolution code and change justification (`IN_APP` + `EMAIL`).

### 4. User Preference Matrix & Mandatory GxP Non-Opt-Out Policy
- Stored centrally in `apps/notifications` in table `user_notification_preferences`.
- Users can customize channels per category (e.g. disable email digests for general published articles).
- **Mandatory GxP Policy:** `CRITICAL` priority tickets, `SLA_BREACH` alerts, and direct task assignments (`ACTION_ITEMS`) are enforced as mandatory and non-opt-outable in the domain dispatch engine.

### 5. Webhook Subscription & Security
- Platform/tenant administrators manage webhook endpoints via table `notification_webhook_subscriptions` (`url`, `event_types`, `secret`, `is_active`, `created_by`, `reason_for_change`).
- Outbound requests include `X-Cadence-Signature` computed via HMAC-SHA256 over canonical JSON.
- Payloads adhere to the **De-identified Metadata + Deep Link** standard to prevent data leakage outside the GxP boundary.

### 6. Deduplication & Idempotency Key
- Composite idempotency token format: `related_entity_id = f"{entity_type}:{entity_id}:{event_type}:{version_index}"`.
- `apps/notifications` enforces a duplicate check on `(recipient_user_id, related_entity_id)` before queueing delivery tasks.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - High availability with asynchronous, fault-isolated dispatch across all three channels.
  - Zero risk of clinical trial staff missing safety-critical SLA escalations.
  - Strict compliance with GxP auditability, blinding integrity, and data privacy.
  - Consistent developer and user experience across all microservices.
- **Negative Impact / Technical Debt:**
  - Requires maintaining webhook subscription management endpoints and Jinja2 email templates in `apps/notifications`.
- **Mitigation Strategy:**
  - Comprehensive unit and integration test coverage across simulated SMTP servers and mock webhook receivers.

## 6. Implementation & Verification

- **Affected Services:** `apps/notifications`, `apps/knowledge`, `apps/tickets`, `apps/web`
- **Verification Plan:**
  - Unit and integration tests for multi-channel dispatch, webhook HMAC verification, and deduplication.
  - Verification of mandatory non-opt-out rules against user preferences.
  - Visual testing of top-bar notification drawer and `/notifications` view in `apps/web`.

