# ADR-054: Notification Delivery Channels and Retry Dispatcher

* **Status:** Accepted
* **Date:** 2026-08-04
* **Authors:** @fpderutier
* **Deciders:** @fpderutier

---

## 1. Context & Problem Statement
The Cadence Clinical platform requires reliable asynchronous delivery of notifications across multiple independent channels: in-app, email, and webhooks. Storing notifications without processing delivery state blocks integration and visibility. Additionally, failed deliveries should be retried following a bounded exponential backoff policy without blocking other notifications, while maintaining full GxP-compliant audit trails and diagnostic tracking.

This decision implements requirements under Trace-8.

## 2. Decision Drivers & Constraints
* **Driver 1:** Reliability and fault isolation across different delivery channels (in-app, email, and webhooks).
* **Driver 2:** Bounded exponential backoff and diagnostics tracking for transient failure recovery.
* **Driver 3:** Clean integration with the standalone Notifications microservice lifecycle.
* **Driver 4:** Compliance with §2.2 FDA 21 CFR Part 11 auditing and role-based visibility.

## 3. Options Considered
### Option 1: Synchronous Delivery on Creation
* **Overview:** Send email, webhook, and in-app updates directly during the HTTP request thread in POST `/api/v1/notifications`.
* **Pros:**
  * ✅ Simplest implementation, no database updates or dispatcher worker needed.
* **Cons:**
  * ❌ Webhook timeouts or SMTP latency directly block or fail user creation requests.
  * ❌ No automatic retries or error persistence.

### Option 2: Asynchronous Dispatcher with Channel-Level Persistence
* **Overview:** Store individual delivery attempts/tasks in a separate relational table `notification_deliveries`. A background async worker handles independent dispatching, retries, and backoff scheduling.
* **Pros:**
  * ✅ High reliability and absolute separation of concerns.
  * ✅ Failed webhooks or slow SMTP servers do not impact core performance.
  * ✅ Precise retry scheduling and error/diagnostic logging.
* **Cons:**
  * ❌ Slight increase in architectural/schema complexity.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** An independent, asynchronous delivery mechanism is essential for enterprise-grade clinical environments. It allows fine-grained audit control, decoupled retry attempts, and prevents external service outages from affecting core workflows.

## 5. Consequences & Trade-offs
* **Positive Impact:** Fine-grained monitoring, robust fault tolerance, and independent retry schedules per channel.
* **Negative Impact / Technical Debt:** Additional table and a background loop task running on the server lifespan.
* **Mitigation Strategy:** Leverage `aiosmtplib` and `httpx` async clients inside structured tasks, and automatically create the tables via SQLAlchemy upon startup.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/notifications`
* **Verification Plan:** Unit tests with mocked external SMTP and webhook HTTP connections to verify success, failures, retries, and bounded backoff.
