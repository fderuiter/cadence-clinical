# ADR-2218: ClinicalIssueHub Module-Based Routing and Dual-Target SLAs

- **Status:** Accepted
- **Date:** 2026-08-24
- **Authors:** @fderuiter
- **Deciders:** @fderuiter, Architecture Governance Board, Clinical Operations

---

## 1. Context & Problem Statement

Clinical trial operations under FDA 21 CFR Part 11, ICH GCP E6(R2)/(R3), and GxP guidelines require robust operational issue and discrepancy management. When issues, protocol deviations, data queries, and supply excursions arise across distributed sites and systems, they must be routed to specialized functional queues rapidly, tracked against granular response commitments, and governed by strict lifecycle re-opening boundaries.

Prior iterations of `apps/tickets` tracked a single, monolithic resolution SLA target and relied on ad-hoc role assignments without a cohesive, **local-substitutable, pure domain engine** capable of:

1. Routing inquiries deterministically to functional queues (`PROTOCOL_DESIGNER`, `DATA_CAPTURE_ECRF`, `SUBJECT_MANAGEMENT_RTSM`, `REPORTING_ANALYTICS`, `PLATFORM_ADMIN_ACCESS`, `REGULATORY_COMPLIANCE`).
2. Tracking decoupled dual-target SLAs (initial acknowledgment/triage velocity vs. final clinical resolution) with state-dependent clock freezing during external waiting states.
3. Enforcing a 30-day bounded ticket reopening window with 21 CFR Part 11 electronic justifications.
4. Executing in ephemeral memory with zero I/O or database dependencies for sub-millisecond evaluation in offline simulations, unit tests, and background workers.

Requirements: `PRD-TCK-001`, `PRD-TCK-002`, `PRD-TCK-003`, `PRD-SYS-051`.

---

## 2. Decision Drivers & Constraints

- **Pure Domain Substitutability:** The core engine (`ClinicalIssueHub`) must operate exclusively in ephemeral memory with zero database or network dependencies, taking immutable dataclass snapshots and returning deterministic evaluation verdicts.
- **Deterministic Multi-Tier Routing:** Routing must follow an explicit precedence: GxP Safety/Compliance overrides > Source Microservice & Entity Mappings > Category Matrix > Default Queue, assigning default clinical roles with transparent rationale.
- **Decoupled Dual-Target SLAs:** Independent acknowledgment/triage commitments (`ack_sla_target_at`) and resolution commitments (`sla_target_at`), with state-dependent pauses (`SLA_PAUSED_STATES`) applied strictly to resolution clocks.
- **21 CFR Part 11 Reopening Boundary:** Tickets resolved/closed for >30 days are permanently immutable; reopening within the window strictly mandates non-empty `reason_for_change` and bumps `version_index`.
- **Idempotent Background Escalation:** Two-step transaction commit design (`TICKET_ACK_ESCALATE` / `TICKET_RESOLUTION_ESCALATE` committed before notification dispatch) with pessimistic row-level locking (`.with_for_update()`).

---

## 3. Options Considered

### Option 1: **Option A (Selected): Pure Domain Engine (`ClinicalIssueHub`) with Layered Service Adapters and Dual-Phase Escalation.**

- **Overview:** - Domain layer encapsulates `ClinicalIssueHub`, `FunctionalQueue`, `IssueContextSnapshot`, `RoutingVerdict`, `DualSLAPolicy`, `DualSLAEvaluation`, and `ReopenVerdict`.
  - Infrastructure layer extends `Ticket` ORM model and repository methods.
  - Presentation layer exposes zero-I/O preview endpoints (`/api/v1/tickets/routing/evaluate`, `/api/v1/tickets/sla/evaluate`) and claim acknowledgment (`/api/v1/tickets/{id}/acknowledge`).
  - Escalation worker sweeps for both unacknowledged and unresolved SLA breaches.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option B: Database-Bound Monolithic Ticket Service.**

- **Overview:** - Engine executes queries internally on every routing or SLA evaluation.
  - _Rejected:_ Violates pure domain boundaries, prevents in-memory local substitution, and degrades performance.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

### Option 3: **Option C: Single-Clock SLA with Ad-Hoc Role Tagging.**

- **Overview:** - Retains single resolution deadline without tracking acknowledgment velocity or formal functional queues.
  - _Rejected:_ Does not provide operational visibility into initial response times or domain queue balancing.

---

- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

## 4. Decision Outcome

- **Chosen Option:** **Option A**. We implement `ClinicalIssueHub` as a pure, local-substitutable domain component under `apps/tickets/domain/services.py` backed by schema expansions in `apps/tickets/adapters/models.py`, API endpoints in `apps/tickets/presentation/routers/tickets.py`, and dual-phase escalation sweeps in `apps/tickets/escalation.py`.
- **Justification:** Selected as primary architectural approach.

### Key Specifications:

1. **Functional Queues:** `PROTOCOL_DESIGNER`, `DATA_CAPTURE_ECRF`, `SUBJECT_MANAGEMENT_RTSM`, `REPORTING_ANALYTICS`, `PLATFORM_ADMIN_ACCESS`, `REGULATORY_COMPLIANCE`.
2. **Dual SLA Baselines:**
   - Critical: Ack 15m / Res 4h
   - High: Ack 1h / Res 24h
   - Medium: Ack 4h / Res 72h
   - Low: Ack 8h / Res 168h
3. **Controlled Reopening:** 30 calendar day window from `resolved_at`/`closed_at`.

---

## 5. Consequences & Trade-offs

- **Positive:**
  - Sub-millisecond deterministic evaluation suitable for offline simulation, unit testing, and real-time form validation.
  - Granular operational telemetry across triage velocity and resolution compliance.
  - Clean separation of concerns adhering to Hexagonal Architecture and Domain-Driven Design.
- **Negative:**
  - Requires maintaining dual SLA timestamps and state fields on the `Ticket` schema.

---

## 6. Implementation & Verification

- **Domain Engine:** `apps/tickets/domain/services.py`, `apps/tickets/domain/models.py`
- **Data Models & Repositories:** `apps/tickets/adapters/models.py`, `apps/tickets/adapters/repositories.py`
- **Presentation Layer:** `apps/tickets/presentation/routers/tickets.py`, `apps/tickets/presentation/dtos.py`
- **Escalation Worker:** `apps/tickets/escalation.py`
- **Verification Suites:** Unit and integration tests under `apps/tickets/tests/` with `@req:PRD-TCK-001`, `@req:PRD-TCK-002`, `@req:PRD-TCK-003` tags.
