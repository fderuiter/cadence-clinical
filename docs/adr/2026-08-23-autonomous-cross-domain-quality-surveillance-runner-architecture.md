# ADR-2213: Autonomous Cross-Domain Quality Surveillance Runner Architecture

* **Status:** Accepted
* **Date:** 2026-08-23
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Prior to this architecture, Centralized Statistical Monitoring (CSM), statistical forensics (Benford's Law leading digit analysis, terminal digit rounding bias, Mahalanobis distance, variance shrinkage), and data integrity sentinels (cross-site duplicate patient matching, temporal burst clustering, perceptual image pHash matching) were implemented as shallow endpoint handlers in `apps/quality/presentation/routers/`. External callers and integration tests were forced to scrape, parse, normalize, and inject raw study observation arrays, subject feature vectors, and image attachments directly into HTTP request bodies.

This created several critical architectural and regulatory deficiencies:
1. **Shallow Module Anti-Pattern**: High-complexity statistical and computer vision algorithms were exposed without an autonomous data harvesting boundary, placing data-wrangling burden on upstream callers.
2. **Microservice Boundary Violations**: Ad-hoc observation assembly risked encouraging sibling database access across microservice lines (prohibited by platform guidelines).
3. **Absence of Sample-Gated Power Controls**: Early-stage sites with minimal data were exposed to statistical distribution checks without sample size gating ($N \ge 15$ subjects or $\ge 50$ observations), yielding elevated false-positive rates.
4. **Disjointed GxP Deviation Lifecycle**: Anomalies detected across distinct forensic sub-engines did not coalesce into holistic site-level risk profiles and lacked deterministic deduplication when logging 21 CFR Part 11 `Deviation` records.

System Requirements: `PRD-CSM-001`, `PRD-CSM-002`, `PRD-CSM-003`, `PRD-CSM-004`, and `PRD-SYS-001`.

---

## 2. Decision Drivers & Constraints

* **REST API-First & Microservice Decoupling**: Sibling database imports across `apps/quality` and `apps/execution` are strictly forbidden. All clinical data must be retrieved through HMAC-signed REST endpoints (`apps/execution/biostat/sdtm/*`).
* **ICH E6(R2)/(R3) Risk-Based Monitoring**: Sample-gated statistical power thresholds must be strictly enforced before evaluating leading digit or multivariate covariance distributions.
* **Deterministic 21 CFR Part 11 Auditability**: All surveillance triggers, anomaly detections, and resulting deviation records must be immutably recorded with deterministic idempotency keys to prevent duplicate deviation spam on recurring scheduled sweeps.
* **Dual-Mode Flexibility**: The surveillance engine must operate autonomously given only a `study_id`, while permitting in-memory `HarvestedSurveillanceBundle` injection for high-throughput simulations, calibration benchmarks, and offline unit tests.

---

## 3. Options Considered

1. **Option A: Deep Autonomous Orchestrator (`QualitySurveillanceRunner`) with Dual-Mode REST Harvesting (Selected)**:
   Encapsulate data harvesting, SDTM normalization, sample-size gating, statistical forensics, computer vision pHash scanning, composite risk scoring, and idempotent deviation logging behind a single deep class `QualitySurveillanceRunner`. Autonomous production runs use an HMAC-authenticated `ExecutionClient` to pull datasets, while testing suites can supply in-memory bundles.

2. **Option B: Push-Only REST Pipeline (Rejected)**:
   Keep data harvesting on external callers and only add a workflow orchestration wrapper. Rejected because it preserves shallow endpoint ergonomics and pushes data-wrangling complexity to frontend clients or external crons.

3. **Option C: Event-Sourced Materialized Read View (Rejected)**:
   Maintain a full replicated projection of all clinical observations and audit logs in `apps/quality` via background Kafka/Outbox event subscriptions. Rejected due to excessive storage duplication, eventual consistency latency during on-demand quality audits, and heightened operational complexity.

---

## 4. Decision Outcome

Chosen option: **Option A (Deep Autonomous Orchestrator with Dual-Mode REST Harvesting)**.

### Architectural Blueprint

```
+-------------------------------------------------------------------------------+
|                       QualitySurveillanceRunner                               |
|                                                                               |
|  1. Harvest / Ingest                                                          |
|     +--------------------+       +-----------------------------------------+  |
|     |   ExecutionClient  | --->  |       HarvestedSurveillanceBundle       |  |
|     |  (HMAC REST Client)|       | (DM, VS, LB, EG, Audits, Image Hashes) |  |
|     +--------------------+       +-----------------------------------------+  |
|                                                      |                        |
|  2. Sample Gating (ICH E6 R3)                        v                        |
|     +----------------------------------------------------------------------+  |
|     | Gating Sentinel: N >= 15 subjects OR >= 50 observations per site    |  |
|     +----------------------------------------------------------------------+  |
|               | (Sufficient Power)                     | (Unconditional)      |
|               v                                        v                      |
|  3. Statistical Forensics                 4. Data Integrity Sentinels         |
|     - Benford's Law (Leading Digit)          - Duplicate Patient Fingerprint  |
|     - Terminal Digit Rounding Bias           - Temporal Burst & Off-Hours     |
|     - Mahalanobis Distance / Covariance      - Identical Procedure Intervals  |
|     - Variance Shrinkage Anomaly             - Perceptual Image Hash (pHash)  |
|               \                                        /                      |
|                \                                      /                       |
|                 v                                    v                        |
|  5. Finding Coalescing & Multi-Flag Weighted Composite Risk Scoring           |
|                                      |                                        |
|                                      v                                        |
|  6. Idempotent GxP Action Dispatch ({study_id}:{site_id}:{anomaly}:{entity}) |
|     - 21 CFR Part 11 Deviation Record (source_system=QUALITY_SURVEILLANCE)    |
|     - KRI Breach Evaluation Event & Audit Log Emission                        |
+-------------------------------------------------------------------------------+
```

---

## 5. Consequences & Trade-offs

* **Positive**: 
  - Callers only need to invoke `POST /api/v1/quality/surveillance/run` with a `study_id`.
  - Zero cross-service database pollution or fragile SQL joins across service boundaries.
  - False positives on early-stage sites are suppressed via explicit ICH E6(R3) sample-size gating annotations.
  - Automated GxP deviations are deduplicated across recurring scheduled runs.
* **Negative**:
  - Requires maintaining the `ExecutionClient` HTTP adapter with appropriate timeouts and gateway signature tokens.
  - SDTM normalization requires mapping schemas for clinical observation variables.

---

## 6. Implementation & Verification

* **Core Engine**: `apps/quality/application/services/quality_surveillance_runner.py`
* **HTTP Client Adapter**: `apps/quality/infrastructure/execution_client.py`
* **Domain Models**: `apps/quality/domain/surveillance_models.py`
* **Presentation Layer**: `apps/quality/presentation/routers/surveillance.py`
* **DTO Contracts**: `apps/quality/presentation/dtos.py`
* **Verification Suite**: `apps/quality/tests/test_quality_surveillance_runner.py`

