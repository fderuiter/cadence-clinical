# ADR-2550: Event-Driven Participant Gamification, Compliance Streak Tracking, and Milestone Stipends

- **Status:** Accepted
- **Date:** 2026-10-15
- **Authors:** @fderuiter
- **Deciders:** Architecture Review Board

---

## 1. Context & Problem Statement

In decentralized clinical trials (DCT), participant retention and timely ePRO/eCOA completion are critical to maintaining protocol data integrity and statistical power. To incentivize participant compliance without introducing anti-coercion violations under IRB guidelines, Cadence Clinical requires a standardized gamification service. This service tracks daily completion streaks, awards milestone badges, triggers engagement notifications via MassTransit and Liquid templating, and handles compliant stipend disbursements ($25 IRB anti-coercion compliant payouts for major milestones). Satisfies requirement PRD-SYS-001.

## 2. Decision Drivers & Constraints

- **IRB Anti-Coercion Compliance:** Minor streak milestones (e.g., 3-day streak) must serve as non-monetary recognition milestones ($0.00), while major milestones (e.g., 7-day, 14-day, 30-day streak, 4-week completion) award compliant $25.00 stipend bonuses.
- **Transactional Outbox Event Publishing:** Gamification progress updates and event emissions (`ParticipantStreakMilestoneAchieved`, `EngagementNudgeTriggered`) must be atomically published using EF Core Transactional Outbox to prevent event loss or ghost notifications.
- **Idempotency & Duplicate Prevention:** MassTransit notification consumers (`ParticipantEngagementConsumer`) must process milestone events idempotently using composite keys (`{StudyId}:{SubjectId}:{MilestoneType}:{StreakCount}`) stored in `NotificationRecords.RelatedEntityId`.
- **YARP Gateway Alignment:** All gamification endpoints (`GET /api/v1/gamification/progress`, `POST /api/v1/gamification/process-submission`) must be mapped via YARP gateway routes to satisfy full gateway test coverage.

## 3. Options Considered

### Option 1: Direct Synchronous RPC between Execution and Notifications
- **Overview:** Execution service directly invokes Notifications API over synchronous HTTP upon submission processing.
- **Pros:**
  - Simple implementation.
- **Cons:**
  - High risk of tight coupling, cascaded failure during notification outages, and potential loss of stipend events.

### Option 2: Event-Driven Outbox Architecture with MassTransit & Liquid Templates (Selected)
- **Overview:** Execution service processes ePRO submissions, updates `ParticipantGamificationProgress`, and emits `ParticipantStreakMilestoneAchieved` / `EngagementNudgeTriggered` events via MassTransit transactional outbox. Notifications service consumes events idempotently and renders Liquid HTML templates.
- **Pros:**
  - ✅ Guaranteed atomic event publishing and eventual consistency.
  - ✅ Decoupled notification delivery with MassTransit consumer idempotency.
  - ✅ Full compliance with IRB anti-coercion stipend guidelines.
- **Cons:**
  - ❌ Requires database migration for `ParticipantGamificationProgress` and transactional outbox mapping.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Event-driven transactional outbox guarantees reliable milestone tracking and stipend auditing without coupling clinical trial execution to notification delivery systems.

## 5. Consequences & Trade-offs

- **Positive Impact:** Reliable streak tracking, IRB anti-coercion compliance, idempotent notification rendering, and reactive subject portal state updates.
- **Negative Impact / Technical Debt:** Requires maintenance of gamification schema migrations and MassTransit contract definitions.
- **Mitigation Strategy:** Automated unit tests (`GamificationServiceTests`, `ParticipantEngagementConsumerTests`), gateway route coverage tests (`GatewayRouteCoverageTests`), and vitest suite (`gamification.test.js`) enforce zero drift.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `apps/execution` (`ParticipantGamificationProgress`, `GamificationService`, `GamificationEndpoints`, EF Core Migration)
  - `apps/notifications` (`ParticipantEngagementConsumer`, `engagement_nudge.html.liquid`)
  - `apps/gateway` (`execution-gamification-route` YARP routing)
  - `packages/contracts` (`ParticipantStreakMilestoneAchieved`, `EngagementNudgeTriggered`)
  - `apps/subject-portal` (`App.vue`, `index.js`, `gamification.test.js`)
- **Verification Plan:**
  - Backend unit tests pass (`dotnet test tests/Cadence.UnitTests`).
  - Gateway route coverage tests pass (`GatewayRouteCoverageTests`).
  - Frontend unit tests pass (`pnpm --dir apps/subject-portal test`).
  - GxP sync and quality sentinels pass (`pnpm cadence check`).
