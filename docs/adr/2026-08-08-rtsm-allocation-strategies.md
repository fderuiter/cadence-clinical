# ADR-066: RTSM Pure-Python Block, Stratified-Block, and Minimization Allocation Strategies

* **Status:** Accepted
* **Date:** 2026-08-08
* **Authors:** @jules
* **Deciders:** @lead_architect, @gxp_compliance_officer

---

## 1. Context & Problem Statement
The clinical trial platform requires a Randomization and Trial Supply Management (RTSM) module to assign clinical trial subjects to treatment arms. To satisfy Good Clinical Practice (GCP) and regulatory compliance guidelines, these allocation methods must support multiple industry-standard algorithms: Permuted Block randomization, Stratified Permuted Block randomization, and Pocock-Simon dynamic minimization. These algorithms must be deterministic, reproducible, secure, and entirely self-contained without dependencies on heavy scientific computing environments.

## 2. Decision Drivers & Constraints
* **GCP & GxP Compliance:** Randomization allocations must be completely auditable, with stable, predictable stratum keys and rigorous parameter validation.
* **No Heavy Scientific Dependencies:** The RTSM core engine must avoid external scientific libraries such as NumPy, SciPy, or Pandas, running natively on pure Python standard primitives.
* **Cryptographic & Seed-based Randomness:** Probabilistic assignments must leverage cryptographically secure sources (`random.SystemRandom`) for production safety, whilst maintaining support for deterministic seed-based PRNGs for simulation and testing.

## 3. Options Considered
### Option 1: Integrating with Scientific Python Libraries (NumPy/Pandas)
* **Overview:** Build the matrix math and list shuffles utilizing NumPy and Pandas structures.
* **Pros:**
  * ✅ High-performance matrix manipulation for large datasets.
* **Cons:**
  * ❌ Increases dependency weight, container size, and security vulnerabilities of the runtime platform.

### Option 2: Pure-Python Allocation Strategy Engine
* **Overview:** Implement a pure-Python hierarchy of strategy classes implementing a unified `AllocationStrategy` interface with standard-library arithmetic.
* **Pros:**
  * ✅ Zero external runtime dependency overhead.
  * ✅ High isolation, enabling robust unit testing and predictable simulation.
  * ✅ Native support for stable, alphabetically-sorted canonical stratum keys.
* **Cons:**
  * ❌ Basic Python performance is slower for massive cohorts, which is negligible for standard clinical trials.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Choosing a pure-Python execution model satisfies the project's strict architectural guidelines on eliminating heavy computing dependencies, ensures exact portability across target sites, and guarantees straightforward predictability and auditability under 21 CFR Part 11 requirements.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Pure-Python, self-contained `rtsm.py` implementation.
  * Robust stable stratum keys using alphabetical sorting.
  * Precise mathematical scaling for unequal ratios in dynamic minimization.
* **Negative Impact / Technical Debt:**
  * Requires custom sorting and aggregation logic instead of leveraging pandas dataframes.
* **Mitigation Strategy:** Covered by exhaustive unit tests in `tests/test_rtsm_algorithms.py`.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/execution/`
* **Verification Plan:**
  * Run focused unit tests under `tests/test_rtsm_algorithms.py` covering valid allocations, configuration errors, and seed-based reproducibility boundaries.
