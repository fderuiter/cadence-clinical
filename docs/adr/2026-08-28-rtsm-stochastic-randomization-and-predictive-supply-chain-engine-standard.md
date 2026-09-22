# ADR-2230: RTSM Stochastic Randomization and Predictive Supply Chain Engine Standard

- **Status:** Accepted
- **Date:** 2026-08-28
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
* **Requirements:** `PRD-SYS-001`, `PRD-RTSM-001`, `PRD-RTSM-002`, `PRD-SCM-001`, `PRD-SCM-002`, `PRD-SYS-050`, `GMP Annex 13`

---

## 1. Context & Problem Statement

Randomization and Trial Supply Management (RTSM / IRT) represents one of the most critical, high-risk domains in clinical trial execution. Allocation errors (e.g. predictable block sequence unblinding, severe covariate imbalance, improper dose escalation in Phase I oncology trials) or stockouts of investigational product directly jeopardize patient safety, data integrity, and regulatory approval under **FDA 21 CFR Part 11**, **ICH GCP E6(R2)**, and **GMP Annex 13**.

Historically, randomization algorithms and inventory threshold evaluations were coupled directly with database ORM models and asynchronous task queues in `apps/execution/`. To achieve extreme performance, complete mathematical testability, and adherence to our **Metadata-Driven Engine Architecture (ADR-2228)**, the RTSM domain is decoupled into pure, zero-I/O functional engine runtimes in `packages/engine_core/randomization/` and `packages/engine_core/supply/`.

## 2. Decision Drivers & Constraints

* **Algorithmic Reproducibility & Cryptographic Sealing (21 CFR Part 11 § 11.10(e))**: All stochastic allocation procedures must support deterministic seed reproduction. Randomization list generation must produce immutable SHA-256 configuration checksums.
* **Modern Phase I/II Adaptive Dose Escalation**: In addition to classical fixed blocks and dynamic minimization, oncology trials require Bayesian Optimal Interval (BOIN) design support with mathematical dose-limiting toxicity (DLT) escalation/de-escalation boundaries ($\lambda_e, \lambda_d$) and safety stopping rules ($\Pr(p_j > \phi | \text{data}) > 0.95$).
* **Multi-Echelon Predictive Supply Forecasting**: Inventory replenishment must not rely on simplistic static thresholds ($on\_hand \le threshold$). The engine must simulate subject accrual via Poisson arrival rates, track expiration curves, factor in multi-echelon lead times (Primary $\to$ Central $\to$ Regional $\to$ Site), and support Direct-to-Patient (DTP) temperature-gated logistics.
* **Zero-I/O Functional Engine Boundary**: Runtimes in `packages/engine_core/` must ingest pure Pydantic v2 domain schemas and return deterministic allocation decisions or supply projections without database or network side effects.

## 3. Options Considered

### Option 1: **Option A (Selected) - Pure Functional Engines in `packages/engine_core/` with Clean Hexagonal Adapters**:

- **Overview:** - Establish `packages/engine_core/randomization/` (Stratified Block, Pocock-Simon Minimization, BOIN, 3+3) and `packages/engine_core/supply/` (Multi-echelon hierarchy, Poisson demand simulator, DTP logistics, emergency unblinding).
   - Integrate with `apps/execution/` via thin repository adapters and REST endpoints.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option B - Status Quo In-Service Algorithm Embedding in `apps/execution/`**:

- **Overview:** - Retain procedural logic inside `apps/execution/randomization.py` and `apps/execution/rtsm_supply.py`.
   - *Rejected*: Violates zero-I/O separation, impairs isolated benchmarking, and prevents cross-service engine reuse.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead


## 4. Decision Outcome

**Chosen Option:** **Option A**.

The RTSM & Supply Chain Engines family delivers:
1. **Stochastic Randomization Engine** (`packages/engine_core/randomization/`):
   - **Stratified Permuted Block Randomization**: Variable block size selection, stratum key generation, and target arm ratio enforcement.
   - **Pocock-Simon Covariate Adaptive Minimization**: Range, Variance, and Standard Deviation marginal distance metrics with factor weighting and biased coin allocation ($p_{preferred} \in [0.5, 1.0]$).
   - **Phase I/II Dose Escalation**: BOIN decision boundary evaluator ($\lambda_e = \ln(\frac{1-\phi_1}{1-\phi}) / \ln(\frac{\phi(1-\phi_1)}{\phi_1(1-\phi)}}$, $\lambda_d = \ln(\frac{1-\phi}{1-\phi_2}) / \ln(\frac{\phi_2(1-\phi)}{\phi(1-\phi_2)})$) and 3+3 rule engine.
2. **Predictive Supply & DTP Logistics Engine** (`packages/engine_core/supply/`):
   - **Multi-Echelon Hierarchy**: Tree validation, cycle detection, capacity utilization, and stock rollups across manufacturer, central depots, regional depots, and sites.
   - **Predictive Demand & Resupply Simulator**: Poisson enrollment rate modeling, scheduled visit dispensation projections, lead-time safety buffers, and expiration run-out curves.
   - **DTP & Cold-Chain Logistics**: Temperature excursion gating, quarantine verification, and tamper-evident manifest generation.
   - **Emergency Unblinding**: 21 CFR Part 11 authenticated sign-off with mandatory reason-for-change and instant safety notifications.

## 5. Consequences & Trade-offs

* **Positive**:
  - Sub-millisecond allocation and simulation performance with zero database contention during calculation.
  - Complete mathematical test coverage and property-based seed verification.
  - Full compliance with FDA 21 CFR Part 11, ICH GCP E6(R2), and GMP Annex 13.
* **Negative**:
  - Requires maintaining dedicated adapters to bridge SQLModel database persistence with engine payloads in `apps/execution/`.

## 6. Implementation & Verification

* **Core Packages**:
  - `packages/engine_core/randomization/` (`models.py`, `engine.py`, `boin.py`, `minimization.py`, `blocks.py`)
  - `packages/engine_core/supply/` (`models.py`, `engine.py`, `forecasting.py`, `hierarchy.py`, `dtp.py`)
* **Verification**:
  - Unit tests in `packages/engine_core/tests/test_randomization_engine.py` and `packages/engine_core/tests/test_supply_engine.py`.
  - Full monorepo qualification via `uv run cadence check --parallel` and `uv run python scripts/sync_gxp.py`.
