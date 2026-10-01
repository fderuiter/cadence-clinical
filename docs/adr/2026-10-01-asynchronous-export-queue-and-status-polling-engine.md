# ADR-2593: Asynchronous Export Queue and Status Polling Engine

- **Status:** Accepted
- **Date:** 2026-10-01
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Synchronous biostatistical dataset export generation (CDISC Dataset-JSON 1.0.0, ODM XML 1.3.2, SAS Transport .XPT, CSV) blocks the HTTP request thread and frontend UI when compiling large clinical trial datasets. To match Veeva Vault CDMS parity and unblock UI navigation, exports must be offloaded to an asynchronous background processing queue with non-blocking status polling and automated toast notifications (PRD-SYS-001).

## 2. Decision Drivers & Constraints

- **Driver 1:** Non-blocking UI execution: export submission must return HTTP 202 Accepted within < 200 ms.
- **Driver 2:** Status polling with exponential backoff (2s -> 4s -> 8s -> max 10s) without persistent WebSockets or Server-Sent Events (SSE).
- **Driver 3:** Part 11 audit compliance: audit events (`SUBMIT_EXPORT_JOB`, `COMPLETE_EXPORT_JOB`, `FAIL_EXPORT_JOB`) recorded via `IAuditLogWriter`.
- **Driver 4:** Artifact expiration and security: 24-hour retention window for compiled export artifacts.

## 3. Options Considered

### Option 1: Bounded Channel Queue with Hosted Background Worker & Pinia Polling Store (Selected)

- **Overview:** Use `System.Threading.Channels.Channel<ExportJob>` and `BackgroundService` in execution service with HTTP polling in Vue `exportJobs.js` Pinia store.
- **Pros:**
  - ✅ Non-blocking HTTP 202 Accepted response < 200 ms.
  - ✅ Non-persistent HTTP polling with exponential backoff.
  - ✅ Part 11 audit logging and 24-hour artifact retention.
- **Cons:**
  - ❌ In-memory job state requires persistent DB store for multi-replica scaling.

### Option 2: Synchronous Request Processing

- **Overview:** Generate export payload directly in the HTTP request handler.
- **Pros:**
  - ✅ Simple single-request architecture.
  - ❌ Times out on large clinical datasets, blocking user navigation.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Chosen because it provides non-blocking dataset compilation, Veeva Vault CDMS parity, and full 21 CFR Part 11 audit logging.

## 5. Consequences & Trade-offs

- **Positive Impact:** Smooth UI navigation during biostatistical dataset generation with automatic toast notifications upon completion.
- **Negative Impact / Technical Debt:** In-memory queue state is ephemeral across service restarts.
- **Mitigation Strategy:** Ephemeral state backed by audit trail ledger.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/execution`, `apps/web`.
- **Verification Plan:** Verified via `dotnet test`, `pnpm check`, and `exportJobs.spec.js`.
