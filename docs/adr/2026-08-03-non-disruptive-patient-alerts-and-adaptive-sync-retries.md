# ADR-255: Non-disruptive Patient Experience and Adaptive Sync Retry

* **Status:** Accepted
* **Date:** 2026-08-03
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In the patient-facing eCOA/ePRO Portal (under `apps/subject-portal/`), network connectivity issues can frequently interrupt data synchronization. Previously, standard alert popups could block the user interface, leading to a disruptive user experience. When data synchronization failed due to network offline states or intermittent gateway dropped connections, patients were presented with modal blocking dialogues or had to manually restart synchronization attempts.

To provide a resilient, compliant, and seamless patient experience, we require a mechanism to handle synchronization retries in the background adaptively while informing patients of connection status via non-disruptive in-app alerts (toasts and status bars) rather than blocking dialogs.

This is governed under our offline-resilient standards as described in **PRD-EDC-007**, **PRD-EDC-008**, and **Trace-9**.

## 2. Decision Drivers & Constraints

* **Driver 1:** Maintaining a seamless, high-fidelity patient experience under intermittent network offline states or backend API availability issues (**PRD-EDC-007**).
* **Driver 2:** Automated background retry loops leveraging progressive backoff delays to prevent excessive server-side rate limits/denials while guaranteeing final data delivery (**PRD-EDC-008**).
* **Driver 3:** Non-blocking in-app toasts and inline status indicators replacing traditional blocking alerts and dialog windows (**Trace-9**).

## 3. Options Considered

### Option 1: Synchronous Blocking Popups with Manual Retry Gating
* **Overview:** Upon sync failure, freeze the client application and present a blocking modal popup requiring manual acknowledgement and click-to-retry actions.
* **Pros:**
  * ✅ Extremely simple to implement.
* **Cons:**
  * ❌ Severe degradation of patient experience.
  * ❌ Violates non-disruptive feedback directives under clinical platform requirements.

### Option 2: Asynchronous Non-Disruptive Toasts with Progressively Backed-off Background Retries (Selected)
* **Overview:** Capture synchronization failures gracefully, displaying non-intrusive status toast alerts to notify the user of background actions without interrupting navigation or data entries. Schedule automated background retries with progress backoffs starting at 2000ms, doubling with each subsequent failure up to a maximum cap of 5 minutes (300,000ms).
* **Pros:**
  * ✅ High-fidelity, uninterrupted navigation and form data entry for subjects even during complete network disconnects.
  * ✅ Reduced server load during wide-scale network recoveries via randomized progressive backoffs.
  * ✅ 100% compliant with **PRD-EDC-008** and **Trace-9** verification criteria.
* **Cons:**
  * ❌ Requires stateful timer tracking and careful service worker status coordination to ensure double retries do not overlap.

## 4. Decision Outcome

Chosen option: **Option 2 (Asynchronous Non-Disruptive Toasts with Progressively Backed-off Background Retries)** because it perfectly satisfies the clinical usability standards and ensures continuous patient compliance, satisfying **PRD-EDC-007**, **PRD-EDC-008**, and **Trace-9**.

## 5. Consequences & Trade-offs

* **Positive Impact:** Patients can continue filling out ePRO diaries offline without experiencing blocking layout freezes. Background queues automatically sync upon internet reconnection.
* **Negative Impact / Technical Debt:** Requires mocking the standard browser `alert` system inside front-end test environments (e.g. `vitest`) to intercept and verify the new toast pipeline.
* **Mitigation Strategy:** Configured global window event hooks and unified `showToast` utility functions with vitest spy wrappers inside `apps/subject-portal/tests/patient-experience.test.js`.

## 6. Implementation & Verification

* **Affected Repositories / Services / Files:**
  * `apps/subject-portal/App.vue`: Introduced the non-disruptive inline status bar for background syncing states.
  * `apps/subject-portal/index.js`: Replaced element-blocking `alert()` calls with `showToast()` helper and integrated the `scheduleBackgroundRetry` progressive backoff controller.
  * `apps/subject-portal/style.css`: Added styles for non-blocking toast notifications.
  * `apps/subject-portal/tests/patient-experience.test.js`: Built complete automated integration specs simulating progressive backoffs and verifying non-blocking alert delivery.
* **Verification Plan:**
  * Run front-end unit and integration suites: `pnpm --filter subject-portal test`
  * Verify full monorepo GxP compliance and RTM alignments: `uv run python scripts/sync_gxp.py`
