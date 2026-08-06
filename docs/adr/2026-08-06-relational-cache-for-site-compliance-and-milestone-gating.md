# ADR-2160: Relational Cache for Site Compliance and Milestone Gating

- **Status:** Accepted
- **Date:** 2026-08-06
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To enforce strict GxP and regulatory compliance boundaries within the Cadence Clinical Platform, we need a reliable mechanism to gate study and site-level milestones (such as site activation and subject enrollment/screening transitions) based on document completeness statuses managed by downstream systems (like eTMF). Directly query-polling external services or files during high-throughput execution workflows would result in latency degradation and potential SLA violations (specifically our 100ms internal SLA target). Therefore, we require a persistent, local, relational caching mechanism within the execution engine. This decision is driven by requirement **PRD-TMF-004**.

## 2. Decision Drivers & Constraints

- **High Performance & Low Latency:** Inter-service communications must fit within the 100ms internal SLA. Polling on every gating action is prohibitive.
- **Auditability & Traceability:** All blocked milestones (such as blocked site activation or blocked subject enrollment) must generate comprehensive, tamper-proof audit trails to satisfy GxP guidelines (**PRD-SYS-001**).
- **Robust Synchronization:** Local cache must be kept in sync with the central eTMF completeness engine via reliable webhook notifications (**PRD-TMF-004**).
- **Fault Tolerance & Reliability:** Real-time fallbacks are required when cached entries do not exist (e.g., defaulting to compliant state initially).

## 3. Options Considered

### Option 1: Live API-First Integration (Synchronous Polling)

Query the eTMF/completeness API in real-time inside every gating route (subject creation, site activation, etc.).

- **Pros:**
  - ✅ Real-time data guarantee without cache synchronization or eventual consistency.
- **Cons:**
  - ❌ Severe latency overhead, risking violation of the 100ms internal SLA.
  - ❌ Strict runtime dependency on downstream services (single point of failure).

### Option 2: Local Relational Cache with Webhook Updates (Selected)

Maintain a local relational database model `SiteComplianceCache` updated asynchronously via inbound webhook endpoints.

- **Pros:**
  - ✅ Extremely fast, sub-millisecond local DB reads fit easily within 100ms SLA.
  - ✅ High availability; the execution engine can function and enforce gating even if downstream eTMF is temporarily offline.
  - ✅ Clean separation of concerns and clear microservice boundaries.
- **Cons:**
  - ❌ Eventual consistency gap between document upload in eTMF and webhook receipt (highly mitigated by immediate updates).

## 4. Decision Outcome

**Chosen Option:** Option 2 (Local Relational Cache with Webhook Updates).

### Justification

This option cleanly satisfies **PRD-TMF-004** by exposing an asynchronous webhook receiver to capture state from the eTMF completeness engine and update the local relational compliance cache. It enables efficient enforcement of compliance gating at critical integration points—such as site activation and subject enrollment—and supports retrieving the compliance readiness status badge, while logging all blocked activities in standard audit logs according to **PRD-SYS-001**.

## 5. Consequences & Trade-offs

- **Positive Impact:** Low latency, high uptime, and detailed audit trails on blocked actions.
- **Negative Impact / Technical Debt:** Eventual consistency must be handled. This is mitigated by ensuring the webhook payload includes comprehensive missing-document metadata.
- **Mitigation Strategy:** Any blocked action is explicitly logged as an `AuditLog` entry in the relational store with missing documents detail to enable easy troubleshooting by clinical coordinators.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `apps/execution/database/models.py`: Added `SiteComplianceCache` model.
  - `apps/execution/main.py`: Added webhook receiver, activation route, badge API, cache listing, and enrollment gating with 21 CFR Part 11 auditing.
- **Verification Plan:**
  - Automated tests added under `tests/test_site_compliance_cache.py` verifying webhook ingestion, badge retrieval, activation/enrollment gating, and audit logging.
