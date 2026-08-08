# ADR-064: GxP FMEA-Aligned Boot Recovery for Stuck Imports

- **Status:** Accepted
- **Date:** 2026-08-08
- **Authors:** @jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In clinical trial dictionary management, server crashes or reboots during dictionary uploads previously left imports permanently stuck in `PENDING` or `PROCESSING` states. This created a split-brain state where clinical data managers saw perpetual active tasks, blocking them from initiating clean re-uploads without manual database interventions.

Furthermore, under GxP regulatory guidelines, any automated state transition or mitigation must be fully auditable, traceable to a system/background identity, and evaluated under a Failure Mode and Effects Analysis (FMEA) framework to ensure the automated correction does not introduce new risks, in compliance with `PRD-SYS-001` and `PRD-SYS-003`.

This ADR defines the implementation of an automated, FMEA-aligned boot recovery sequence during application startup to resolve these stuck imports cleanly and in a fully auditable manner.

## 2. Decision Drivers & Constraints

- **GxP Traceability:** Every state transition must be recorded in the PostgreSQL audit ledger under a dedicated background service identity (`boot_recovery_service`), satisfying `PRD-SYS-001`.
- **FMEA Compliance:** Risk must be quantified using Severity (S), Occurrence (O), and Detectability (D) metrics to compute a Risk Priority Number (RPN) below the critical regulatory threshold of 20, satisfying `PRD-SYS-003`.
- **System Isolation:** The recovery routine must operate within strict, isolated database transactions and connection guardrails to prevent connection leakage before incoming API requests are processed.

## 3. Options Considered

### Option 1: Manual Administrative DB Interventions

- **Overview:** Rely on system administrators to manually identify stuck imports and run SQL update scripts to clear them.
- **Pros:**
  - ✅ Simple; requires zero startup logic in the application.
- **Cons:**
  - ❌ Slow and error-prone.
  - ❌ Leaves the system in a blocked state until manual intervention occurs.
  - ❌ Does not provide automated GxP audit trail logging of the transitions.

### Option 2: Automated sequential startup recovery inside FastAPI Lifespan (Selected)

- **Overview:** Integrate a boot recovery runner sequential task directly into the FastAPI `@asynccontextmanager` lifespan handler, executing it on startup before yielding control to public traffic. Stuck imports are transitioned to `FAILED` under the background identity.
- **Pros:**
  - ✅ Eliminates split-brain/stuck states automatically and transparently on server boot.
  - ✅ Fully compliant with GxP auditing requirements and risk reporting (RPN calculation).
  - ✅ Sequenced startup ensures zero race conditions with active API requests.
- **Cons:**
  - ❌ Adds a brief sequential step during the application startup process.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 is chosen because it fully automates stuck import resolution without manual intervention, maintains high reliability, and aligns perfectly with GxP compliance constraints under `PRD-SYS-001` and `PRD-SYS-003`.

## 5. Consequences & Trade-offs

- **Positive Impact:** Clinicians are never blocked by stuck import states after system reboots. All automated transitions are mapped to a secure background service identity, maintaining audit log integrity.
- **Negative Impact / Technical Debt:** Application startup duration increases slightly to run the recovery transaction.
- **Mitigation Strategy:** Database queries are scoped to single short-lived transactions to minimize connection hold-time.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/execution/`
- **Verification Plan:** Unit and integration tests verify the automatic transition of `PENDING`/`PROCESSING` jobs to `FAILED` and assert database connection releases, verified via `pytest apps/execution/tests/test_boot_recovery.py`.
