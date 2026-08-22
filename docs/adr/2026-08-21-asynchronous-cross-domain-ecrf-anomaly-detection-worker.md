# ADR-2195: Asynchronous Cross-Domain eCRF Anomaly Detection Worker

* **Status:** Accepted
* **Date:** 2026-08-21
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical trial data capture involves complex interdependencies across distinct eCRF domains such as Adverse Events (`AE`), Concomitant Medications (`CM`), Laboratory Diagnostics (`LB`), Vital Signs (`VS`), Disposition (`DS`), and Exposure (`EX`). Traditional field-level and single-form edit checks fail to catch holistic clinical inconsistencies, such as severe adverse events without concomitant rescue medications, critical laboratory abnormalities or vital sign spikes without documented adverse events, study drug discontinuation due to unrecorded events, or inverted cross-domain temporal spans. Furthermore, directly raising official queries from unreviewed algorithmic detections risks overburdening site coordinators and investigators with false positives. An asynchronous, decoupled cross-domain evaluation engine and worker is required to detect clinical anomalies and stage them as candidate queries for human Data Manager adjudication under **PRD-QRY-008**.

## 2. Decision Drivers & Constraints

* **Asynchronous Non-Blocking Execution (100ms SLA):** Cross-domain evaluations involving multi-form data aggregation and optional AI semantic reasoning must never block synchronous form submission HTTP request lifecycles.
* **Human-in-the-Loop (HITL) Adjudication:** Discrepancies detected by algorithms or AI must be staged in a `CANDIDATE` state without directly opening active queries on site personnel until approved by a Data Manager (FDA 21 CFR Part 11).
* **Multi-Domain Correlation Rules:** Must reliably evaluate `AE-CM`, `AE-LB`, `AE-VS`, and `DS/EX-AE` temporal and semantic rules.
* **AI Gateway Integration & Resilient Fallback:** Must integrate with `apps/ai_gateway` Tier 2 models for semantic reasoning with Pydantic v2 structured schemas and dual attribution (`AIAssistedRecordMixin`), with zero failure impact when AI Gateway is unconfigured or unreachable.
* **Concurrency Safety & Distributed Coordination:** Background worker execution across multiple service replicas must be coordinated via PostgreSQL advisory locks (`pg_try_advisory_xact_lock`) and database-gated test harness isolation.

## 3. Options Considered

1. **Option A (Selected): Dedicated Asynchronous Cross-Domain Evaluation Worker & Candidate Query Staging Engine in `apps/execution`**:
   - The worker runs periodically with distributed advisory locking and can also be scheduled on post-submission hooks.
   - Combines deterministic domain correlation engines with optional HMAC-authenticated AI Gateway semantic reasoners.
   - Generates staged `CANDIDATE` queries in `clinical_queries`, exposed via REST endpoints (`/api/v1/execution/anomalies/*`) for Data Manager adjudication (`APPROVE` -> `OPEN`, `REJECT` -> `CANCELLED`).
2. **Option B: Synchronous In-Line Validation on Every Submission**: Evaluating all cross-domain rules synchronously during form submission. Rejected due to latency spikes and database pool exhaustion.
3. **Option C: Direct Auto-Creation of Active `OPEN` Queries**: Bypassing candidate staging and immediately creating active queries for site staff. Rejected due to GxP compliance risks and noise from potential false positives.

## 4. Decision Outcome

Chosen option: **Option A**.
Option A preserves system responsiveness, strictly complies with 21 CFR Part 11 human-in-the-loop oversight, isolates cross-domain evaluation logic, and provides seamless Data Manager review and candidate adjudication.

## 5. Consequences & Trade-offs

* **Positive:** Complete cross-domain coverage across `AE`, `CM`, `LB`, `VS`, `DS`, and `EX` without slowing down data capture.
* **Positive:** Robust candidate staging ensures only verified clinical queries reach site investigators.
* **Positive:** AI Gateway integration adds semantic discrepancy reasoning with full `packages/deid` air-gapping and dual-attribution audit logs.
* **Positive:** Graceful deterministic fallback ensures high availability if external AI models are unavailable.
* **Negative:** Staged candidate queries require Data Manager triage workflows and dedicated UI panels.

## 6. Implementation & Verification

* Domain models: `apps/execution/domain/anomaly.py`.
* AI client adapter: `apps/execution/adapters/ai_anomaly_client.py`.
* Anomaly evaluation service: `apps/execution/services/cross_domain_anomaly_service.py`.
* Background lifecycle worker: `apps/execution/workers/anomaly_worker.py`.
* Presentation router & schemas: `apps/execution/presentation/routers/anomalies.py` & `anomalies_schemas.py`.
* Web UI integration: `apps/web/src/views/RulesView.vue` and `apps/web/src/api/execution.ts`.
* Unit, integration, and lifecycle test suite: `apps/execution/tests/test_cross_domain_anomaly_worker.py` tracing requirement `@req:PRD-QRY-008`.
