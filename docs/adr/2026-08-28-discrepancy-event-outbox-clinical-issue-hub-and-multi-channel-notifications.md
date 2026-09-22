# ADR-2221: Discrepancy Event Outbox Clinical Issue Hub and Multi Channel Notifications

- **Status:** Accepted
- **Date:** 2026-08-28
- **Authors:** @fderuiter
- **Deciders:** Engineering Architecture Board, Clinical Operations Working Group
* **Requirements:** PRD-SYS-001, PRD-QRY-001, PRD-NOT-001

---

## 1. Context & Problem Statement

Clinical data discrepancies and safety queries originate across disparate microservice boundaries—including eCRF edit checks (`apps/execution`), out-of-range lab analyzers, medical coding anomalies, protocol violation sentinels, and cross-domain anomaly background workers. Inconsistent query lifecycle management and fragmented notification dispatch risk critical pharmacovigilance omissions and delayed query resolution.

The platform requires:
1. A transactional outbox event emitter dispatches discrepancy events to the Clinical Issue Hub (`apps/tickets`) with structured composite idempotency keys (`{study_id}:{entity_type}:{entity_id}:{version_index}`).
2. Dual-Target SLA tracking (Initial Triage vs. Final Resolution) with dynamic clock pausing during external hold states (`WAITING_FOR_SITE_RESPONSE`, etc.).
3. Mandatory GxP notification policy bypassing user mute preferences for critical safety alerts, SLA warnings, and re-consent tasks across In-App, SMTP Email (Jinja2), and Webhooks.
4. Bounded 14-day ticket reopening window requiring 21 CFR Part 11 electronic signature step-up tokens and explicit reason for change.

## 2. Decision Drivers & Constraints

* **ICH GCP E6(R2) §5.5.3 / §4.9**: Data queries and audit discrepancy resolutions must be documented, tracked, and verifiable with clear audit trails.
* **FDA 21 CFR 312.32 / ICH E2A**: Expedited reporting of serious and unexpected adverse events requires uncompromised notification delivery.
* **Idempotency Invariant**: Repeated form saves or re-evaluations must not spawn duplicate tickets.
* **Bounded Operational Lifecycles**: Resolved queries cannot remain open indefinitely without formal electronic re-authorization.

## 3. Options Considered

### Option 1: **Option 1 (Selected): Transactional Outbox + Dual-Target SLA Engine + Mandatory GxP Multi-Channel Dispatch + 14-Day Reopening Window**:

- **Overview:** - Outbox pattern ensures exactly-once delivery with composite key deduplication.
   - Dual-milestone SLA timers (triage vs. resolution) with state-dependent clock pauses on external holds.
   - Safety/SLA alerts bypass mute preferences across In-App, responsive HTML Email, and HMAC-signed Webhooks.
   - Closed tickets are locked against reopening after 14 days, mandating Part 11 electronic signatures (`generic.close`/`generic.reopen`) during the active window.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option 2: Direct Synchronous HTTP Dispatches**:

- **Overview:** - Tightly couples execution and anomaly workers to tickets API with risk of dropped events during service outages.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 3: **Option 3: Manual Query Management & Static Deadlines**:

- **Overview:** - No automated outbox, single static SLA deadlines without clock pausing, and optional manual email alerts.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead


## 4. Decision Outcome

**- **Chosen Option:** ** Option 1 because it guarantees transactional resilience, non-repudiable Part 11 audit trails, accurate monitor SLA tracking, and reliable safety notification delivery.

## 5. Consequences & Trade-offs

* **Positive**:
  - Eliminates duplicate query noise and prevents lost discrepancy events.
  - Fair and accurate SLA metrics reflecting real monitor/investigator response times.
  - Fail-safe safety and compliance notification delivery.
  - Clear Part 11 closure immutability with strict 14-day reopening boundaries.
* **Negative**:
  - Requires maintaining background delivery workers with retry backoff queues.
  - State-machine complexity for calculating elapsed pause durations across timezones.

## 6. Implementation & Verification

* **Core Target Modules**:
  - `apps/tickets/`: Clinical Issue Hub domain service, SLA calculator, dual-target milestones, 14-day reopening rules, and Part 11 audit ledger.
  - `apps/notifications/`: Multi-channel notification worker, Jinja2 template renderer, mandatory GxP policy bypass, SMTP and webhook dispatchers.
  - `apps/execution/`: Discrepancy outbox table and event emitters for eCRF validations and lab ranges.
* **Verification Suites**:
  - `apps/tickets/tests/test_tickets_service.py` (29 passed tests)
  - `apps/notifications/tests/test_notifications.py`
  - `apps/notifications/tests/test_reconsent_notifications.py`
  - Full end-to-end suite `tests/e2e/test_phase1_e2e_suite.py` (Tier 1 Feature 05 query escalation & resolution)
