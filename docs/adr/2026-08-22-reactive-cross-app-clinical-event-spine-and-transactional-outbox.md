# ADR-2201: Reactive Cross-App Clinical Event Spine and Transactional Outbox

- **Status:** Superseded by [ADR-2234](2026-09-02-core-service-architecture-cqrs-transactional-outbox-and-gxp-interceptor-standard.md)
- **Date:** 2026-08-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

> [!NOTE]
> **Superseded Notice (2026-09-02):** This record documented the legacy custom PostgreSQL polling worker outbox. It is formally superseded for .NET 10 by [ADR-2234](2026-09-02-core-service-architecture-cqrs-transactional-outbox-and-gxp-interceptor-standard.md) (MassTransit + EF Core PostgreSQL Transactional Outbox). Refer to `docs/MIGRATION_WORKFLOW.md` for current implementation standards.

---

## 1. Context & Problem Statement

Cadence Clinical comprises over 15 decoupled domain microservices (MDR Designer, EDC Execution, CTMS, Safety/PV, RTSM, Site Payments, Medical Imaging, eTMF, eISF, eConsent, Subject Portal, Quality/RBQM, SSU Greenlight). 
As clinical trials execute, cross-domain state transitions occur continuously (e.g. `SiteGreenlightApproved`, `EhrDataIngested`, `Grade3ToxicityDetected`, `EndpointAdjudicated`, `DeviceMalfunctionReported`, `VisitCompletedPayableTriggered`).

Direct synchronous HTTP REST invocations across services create cascading latency, coupling, distributed transaction failures, and unrecoverable partial state writes during network partitions. Conversely, direct sibling database queries violate microservice boundaries and GxP tenant isolation. We need a reliable, resilient, zero-data-loss communication spine that satisfies 21 CFR Part 11 audit requirements (PRD-SYS-120).

## 2. Decision Drivers & Constraints

* **21 CFR Part 11 Auditability**: All clinical state events must be strictly ordered, immutable, cryptographically verifiable, and non-repudiable.
* **Zero Data Loss Guarantee**: Events generated during database transactions (e.g., eCRF submission, SAE intake) must never be lost if the downstream broker or consumer is temporarily unavailable.
* **Microservice Decoupling**: Sibling database imports are strictly prohibited across microservice boundaries per AGENTS.md guidelines.
* **Deterministic Idempotency**: Network retries and redeliveries must not trigger duplicate payments, duplicate safety transmissions, or duplicate eCRF records.

## 3. Options Considered

### Option 1: **Transactional Outbox with Redis / Kafka Event Spine (Selected)**: Microservices persist clinical events to a local `transactional_outbox` table within the same ACID database transaction as the domain entity update. An asynchronous outbox relay polls or streams outbox records to a centralized high-speed event spine. Consuming services process events with deterministic idempotency keys (`{entity_type}:{entity_id}:{event_type}:{version_index}`).

- **Overview:** **Transactional Outbox with Redis / Kafka Event Spine (Selected)**: Microservices persist clinical events to a local `transactional_outbox` table within the same ACID database transaction as the domain entity update. An asynchronous outbox relay polls or streams outbox records to a centralized high-speed event spine. Consuming services process events with deterministic idempotency keys (`{entity_type}:{entity_id}:{event_type}:{version_index}`).
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Direct Synchronous REST APIs**: Services synchronously invoke target service endpoints on state changes.

- **Overview:** **Direct Synchronous REST APIs**: Services synchronously invoke target service endpoints on state changes.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 3: **Shared Sibling Database State**: Services write directly to shared database tables across service boundaries.

- **Overview:** **Shared Sibling Database State**: Services write directly to shared database tables across service boundaries.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead


## 4. Decision Outcome

- **Chosen Option:** **Transactional Outbox with Reactive Event Spine**.
- **Justification:** Selected as primary architectural approach.
- Guarantees at-least-once delivery without distributed two-phase commit overhead.
- Atomically binds event emission with domain state changes in PostgreSQL.
- Consumer idempotency keys guarantee exactly-once processing semantics for GxP operations.

## 5. Consequences & Trade-offs

* **Positive**: 
  - Complete decoupling of microservices with sub-100ms async propagation.
  - Resilience against downstream service outages with automated backoff and retry.
  - Full audit trail replay capability for disaster recovery and inspection readiness.
* **Negative**:
  - Eventual consistency requires UI workflows to handle asynchronous state transitions.
  - Requires maintaining outbox relay workers and consumer deduplication tables.

## 6. Implementation & Verification

* Standardized clinical event schemas defined under `packages/core-models/`.
* Outbox relay pattern implemented in `packages/database/`.
* Consuming workers deployed in `apps/ctms`, `apps/safety`, `apps/execution`, and `apps/quality`.
* Traceable to requirement `PRD-SYS-120` and verified by integration test suites.

