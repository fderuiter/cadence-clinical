# ADR-2205: Multi-Echelon RTSM Supply Hierarchy and Predictive Resupply

- **Status:** Accepted
- **Date:** 2026-08-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical trial drug supply chains operate across multi-tiered global logistics networks: Primary Manufacturer -> Central Depot -> Regional Depots -> Clinical Sites -> Subjects. Traditional threshold-based inventory systems only react when site stocks drop below arbitrary static boundaries, risking either trial disruptions from stockouts during rapid enrollment surges or massive drug waste due to lot expiration and overstocking.

Under **PRD-SCM-001** and **GMP Annex 13**, the Cadence RTSM engine (`apps/execution`) requires:

1. Multi-echelon hierarchy modeling across all supply tiers with storage capacity limits, temperature conditions, and shipping lead times.
2. Continuous predictive resupply forecasting driven by enrollment velocity, screening conversion pipelines, and protocol visit intervals (Schedule of Activities).
3. Automated GxP transfer orders, serialized blinded packing manifests with temperature data logger bindings, and depot notification webhooks.
4. Strict double-blinding preservation and 21 CFR Part 11 / GxP audit trail immutability.

## 2. Decision Drivers & Constraints

- **GxP & 21 CFR Part 11 Auditability:** Every supply chain transaction (order creation, approval, packing, shipment dispatch, delivery receipt, and inventory adjustment) must be captured in immutable trigger-backed shadow audit logs (`audit_logs`) with mandatory electronic justifications (`reason_for_change`).
- **Double-Blinding Integrity:** All serialized kit allocations, packing manifests, and transfer orders must use blinded kit numbers and types without exposing treatment group assignments to blinded study roles (CRC, CRA, Site Investigator).
- **Deterministic & Continuous Forecasting:** Forecast algorithms must simulate daily subject visit schedules, screening transitions, and enrollment burn rates without side-effects or uncontrolled race conditions.
- **Storage & Cold Chain Capacity Safety:** Reorder quantities must be bounded by temperature-controlled storage capacities at destination nodes to avoid physical storage breaches.
- **Trial Locking Conformance:** Supply mutations must respect global trial and site-level read-only locks managed by `TrialLockManager`.

## 3. Options Considered

### Option 1: **Option A: Integrated Audited Multi-Echelon Domain within Execution Service (Selected)**

- **Overview:** - Model `SupplyNode`, `SupplyNodeInventory`, `TransferOrder`, `PackingManifest`, and `PredictiveResupplyForecast` as `AuditedModel` subclasses inside `apps/execution/database/models/rtsm.py`.
  - Implement pure-Python forecasting and hierarchy resolution in `apps/execution/rtsm_supply.py`.
  - Expose REST API endpoints via a modular FastAPI router (`apps/execution/presentation/routers/rtsm.py`).
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option B: Standalone External RTSM Microservice**

- **Overview:** - Build a completely independent microservice for RTSM and supply chain.
  - Disadvantages: Increases deployment overhead, fragments the PostgreSQL GxP trigger ledger, and introduces distributed transaction complexities across clinical subjects and kit allocations.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

## 4. Decision Outcome

- **Chosen Option:** **Option A** because it natively inherits the validated GxP audit trigger foundation, enforces PostgreSQL transactional integrity, integrates seamlessly with subject visit scheduling and `TrialLockManager`, and ensures strict double-blinding.
- **Justification:** Selected as primary architectural approach.

Key architectural characteristics:

- **Hierarchy Representation:** Directed Acyclic Graph (DAG) with parent-child links from Manufacturer to Central Depot, Regional Depots, and Clinical Sites.
- **Predictive Forecast Formula:**
  $$\text{Days of Supply} = \frac{S_{\text{on\_hand}} + S_{\text{in\_transit}} - S_{\text{reserved}}}{\text{Daily Burn Rate}}$$
  $$Q_{\text{order}} = \min\left(C_{\text{max}} - S_{\text{on\_hand}}, \max\left(0, D(T_{\text{lead}} + T_{\text{buffer}}) + S_{\text{safety}} - (S_{\text{on\_hand}} + S_{\text{in\_transit}})\right)\right)$$
- **Manifest & Logger Binding:** Packing manifests explicitly bind serialized kit lists to temperature logger IDs, container serials, and tamper seals before shipment.
- **Depot Notification:** Async dispatch of depot notifications using the transactional outbox pattern and `NotificationRouter`.

## 5. Consequences & Trade-offs

- **Positive:**
  - Automated predictive inventory replenishments eliminate stockouts during enrollment spikes.
  - Full end-to-end multi-echelon traceability for GMP Annex 13 compliance.
  - Complete 21 CFR Part 11 electronic audit trail across the entire shipment lifecycle.
- **Negative / Overhead:**
  - Additional database tables and forecast calculation compute.
- **Mitigation:**
  - Optimized pure-Python algorithms and indexed database queries keep endpoint latency well below the 100ms SLA.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/execution/` (models, services, presentation routers), `apps/web/` (TypeScript schemas).
- **Verification Plan:**
  - Hierarchy DAG validation in `apps/execution/tests/test_rtsm_supply_hierarchy.py`.
  - Predictive forecasting algorithm unit tests in `apps/execution/tests/test_rtsm_predictive_forecasting.py`.
  - Full transfer order and packing manifest lifecycle tests in `apps/execution/tests/test_rtsm_transfer_orders.py`.
  - GxP compliance synchronization via `scripts/sync_gxp.py`.
