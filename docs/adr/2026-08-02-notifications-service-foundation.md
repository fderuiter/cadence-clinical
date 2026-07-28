# ADR-053: Standalone Notifications Service Foundation

* **Status:** Accepted
* **Date:** 2026-08-02
* **Authors:** @fpderutier
* **Deciders:** @fpderutier

---

## 1. Context & Problem Statement
The Cadence Clinical platform requires a decoupled notifications service that is responsible for notification, alert, and action-item persistence and dashboard-facing APIs.
To comply with FDA 21 CFR Part 11 and full GxP auditing standards, all notifications, status changes, and views must be thoroughly audited. Additionally, access to notifications must be role-restricted and recipient-targeted.

This decision implements requirements under Trace-10.

## 2. Decision Drivers & Constraints
* **Driver 1:** Decoupled architectural boundaries to ensure high scalability and easy maintenance of notifications, alerts, and action items.
* **Driver 2:** Compliance with FDA 21 CFR Part 11 regarding electronic records, immutability of audit trails, and strict target-based visibility to avoid information leaks.

## 3. Options Considered
### Option 1: Shared Execution Database for Notifications
* **Overview:** Store notifications as tables in the core clinical execution database.
* **Pros:**
  * ✅ Less architectural overhead.
* **Cons:**
  * ❌ Violates microservice isolation principles.
  * ❌ Might impact core clinical execution performance during peak notification loads.

### Option 2: Standalone Notifications Service
* **Overview:** Build a standalone Notifications service with its own isolated database manager and schema.
* **Pros:**
  * ✅ Absolute database isolation and strict microservice encapsulation.
  * ✅ Dedicated audit logging table to record view and transition events.
* **Cons:**
  * ❌ Additional service to scaffold and run.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Implementing a standalone notifications microservice aligns with the system's microservice architectural style and ensures full GxP isolation.

## 5. Consequences & Trade-offs
* **Positive Impact:** Independent deployment, scalability, and focused audit trailing for notifications.
* **Negative Impact / Technical Debt:** Additional database connection string `NOTIFICATIONS_DATABASE_URL` needs to be managed in deployment.
* **Mitigation Strategy:** Provide sensible sqlite fallbacks and leverage docker-compose to handle runtime environments seamlessly.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/notifications`, `apps/gateway`, `apps/execution/database/audit.py`
* **Verification Plan:** Verified using standard unit and integration tests under `tests/test_notifications.py` using sqlite in-memory db execution.
