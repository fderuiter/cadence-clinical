# ADR-2204: Cumulative Mean Kinetic Temperature Stability Budget Engine
* **Status:** Accepted
* **Date:** 2026-08-22
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Investigational Medicinal Products (IMPs), biologics, cell/gene therapies, and diagnostic reagents are highly temperature-sensitive. During multi-echelon transit (central depot to regional depot to clinical site), products encounter ambient thermal variations.
Traditional RTSM systems rely on rigid, instantaneous binary alarms (e.g. flagging any temperature $> 8^\circ\text{C}$ as ruined), causing millions of dollars in unnecessary drug destruction and study delays. Conversely, ignoring transient thermal spikes risks administering degraded, toxic, or ineffective drug products to trial subjects. We require a scientifically rigorous, automated Cumulative Mean Kinetic Temperature (MKT) and Stability Budget Engine conforming to USP <1079> and PDA Technical Report 39 (PRD-SCM-002).

## 2. Decision Drivers & Constraints

* **Patient Safety & Regulatory Compliance (USP <1079> / PDA TR 39)**: Thermal stability evaluations must use the Arrhenius equation to calculate non-linear thermal degradation over time rather than simple arithmetic averages.
* **Cumulative Multi-Echelon Tracking**: Drug lots transit through multiple air/ground couriers and depots; the stability ledger must track cumulative lifetime thermal exposure across all legs.
* **Automated Real-Time RTSM Quarantine**: If a cumulative stability budget is breached or a critical upper excursion threshold is violated, all impacted kits must be quarantined instantaneously across the entire supply chain.

## 3. Options Considered

1. **Cumulative Arrhenius MKT Kinetic Ledger & Automated RTSM Quarantine (Selected)**: The system models stability budgets in `apps/designer` (e.g. activation energy $\Delta H$, max cumulative degree-hours, excursion threshold curves). IoT logger data streams ingested at each transit hop update the kit's cumulative MKT and remaining stability budget. Breaches automatically transition kit states to `QUARANTINED_EXCURSION` and trigger depot replenishment orders in `apps/execution` (RTSM).
2. **Instantaneous Binary Threshold Alarms**: Any temperature reading outside 2°C–8°C triggers immediate automatic destruction.
3. **Manual Offline Temperature Logger Evaluation**: Sites download CSV logger files, email them to sponsor quality teams, and wait days for manual review.

## 4. Decision Outcome

Chosen option: **Cumulative Arrhenius MKT Kinetic Ledger & Automated RTSM Quarantine**.
- Scientifically distinguishes between safe transient thermal excursions and true product degradation.
- Prevents millions of dollars in needless drug wastage and supply shortages.
- Protects subject safety with instant, programmatic RTSM dispensing locks.

## 5. Consequences & Trade-offs

* **Positive**:
  - Dramatic reduction in wasted clinical supply batches.
  - Sub-second automated quarantine and depot resupply dispatch upon confirmed excursion.
  - Complete, verifiable temperature audit trail for regulatory inspection.
* **Negative**:
  - Requires sponsors to define thermal activation energy ($\Delta H$) and stability curves during study setup.
  - Requires IoT temperature logger format adapters in `apps/interop`.

## 6. Implementation & Verification

* **Requirements:** Specified in `docs/SDLC/01_Product_Requirements_Document_PRD.md` under `PRD-SCM-003`.
* **Domain Model:** Captured and defined in `CONTEXT.md`.
* **GxP Traceability:** Synchronized via `uv run python scripts/sync_gxp.py`.

