# ADR-2810: Native Web Push Notification Engine and eCOA Visit Window Scheduler

- **Status:** Accepted
- **Date:** 2026-10-06
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Subject portals and eCOA (electronic Clinical Outcome Assessment) applications require participant engagement and timely diary/questionnaire completion during protocol-defined visit windows. Participants need native browser push notifications without sensitive Personal Health Information (PHI) or ePHI in push payloads, while maintaining 21 CFR Part 11 audit trails and automatic cleanup of expired device subscriptions.
@req:PRD-SYS-001

## 2. Decision Drivers & Constraints

- **HIPAA Non-Sensitive Payload:** Push notification payloads delivered via third-party web push gateways must carry non-sensitive titles and messages ("Clinical Questionnaire Ready").
- **21 CFR Part 11 Compliance:** Subscription mutations and deactivations must record 21 CFR Part 11 audit fields (`CreatedAt`, `CreatedBy`, `UpdatedAt`, `ReasonForChange`, `VersionIndex`).
- **Standard VAPID Negotiation:** Browser enrollment must use Voluntary Application Server Identification (VAPID) keys and standard W3C Web Push API endpoints.
- **Automatic Subscription Maintenance:** HTTP 410 Gone and 404 Not Found responses from push service endpoints must automatically deactivate expired subscription records.

## 3. Options Considered

### Option 1: Native VAPID Web Push Service with EF Core Persistence (Selected)

- **Overview:** Implement a `PushSubscription` domain entity in `Cadence.Apps.Notifications` with EF Core Postgres persistence, a background `EcoaWindowSchedulerService`, and browser Service Worker (`sw.js`) handling in `subject-portal`.
- **Pros:**
  - ✅ Pure native implementation without third-party vendor lock-in.
  - ✅ Non-sensitive HIPAA-compliant push payloads.
  - ✅ Automatic HTTP 410/404 expired endpoint cleanup.
  - ✅ Full 21 CFR Part 11 audit field tracking.
- **Cons:**
  - ❌ Requires client VAPID subscription negotiation and service worker push event handling.

### Option 2: Proprietary Third-Party Mobile Push Gateway Service

- **Overview:** Integrate commercial third-party notification vendors.
- **Pros:**
  - ✅ Pre-packaged dashboard.
- **Cons:**
  - ❌ Vendors process clinical trial participant metadata, risking HIPAA/ePHI exposure and adding vendor lock-in.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Native VAPID Web Push ensures zero third-party ePHI transmission, full 21 CFR Part 11 compliance, and zero external service dependency.

## 5. Consequences & Trade-offs

- **Positive Impact:** Secure, privacy-preserving, native browser push alerts for trial participants.
- **Negative Impact / Technical Debt:** Requires maintenance of VAPID key pair configuration.
- **Mitigation Strategy:** Automated fallback generation for dev/test environments and comprehensive test coverage.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/notifications`, `apps/subject-portal`
- **Verification Plan:** Verified via `pnpm --filter subject-portal test`, `dotnet test`, and `pnpm cadence check`.
