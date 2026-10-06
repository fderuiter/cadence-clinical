# ADR-2812: Predictive Enrollment Velocity Forecasting and Interactive Site Risk Heatmaps

- **Status:** Accepted
- **Date:** 2026-10-06
- **Authors:** @stitch
- **Deciders:** Clinical Platform Architecture Team

---

## 1. Context & Problem Statement

Clinical trial operations require predictive modeling for enrollment velocity and centralized risk-based quality management (RBQM) site risk heatmaps (PRD-SYS-001). Previously, Study Pulse only presented raw subject status totals without forward-looking trajectory projections, confidence intervals, or multi-category site risk scoring.

## 2. Decision Drivers & Constraints

- **Driver 1:** Provide predictive Monte Carlo/Poisson weekly enrollment projections with 95% confidence bounds (PRD-SYS-001).
- **Driver 2:** Calculate composite site risk scores across 5 core clinical categories (Safety, Consent, Data Quality, Enrollment, Supply).
- **Driver 3:** Maintain K-TIME-3 compliance solution-wide using `IClock` abstractions.
- **Driver 4:** Interactive multi-tab Study Pulse canvas view with WCAG 2.1/2.2 AA compliant UI controls and site drilldown modal.

## 3. Options Considered

### Option 1: Client-side mathematical simulation only

- **Overview:** Perform velocity projections and risk scoring entirely inside Vue store.
- **Pros:** Fast local UI state updates.
- **Cons:** ❌ Inconsistent results across personas and lack of GxP auditability on backend.

### Option 2: C# Engine Services with Minimal API Endpoints and Vue Store Integration (Selected)

- **Overview:** Implement `PredictiveEnrollmentVelocityEngine` in `packages/engine_core` and `SiteRiskHeatmapService` in `apps/quality`, exposing Minimal APIs in `apps/execution` and `apps/quality`.
- **Pros:**
  - ✅ Centralized, deterministic GxP calculations.
  - ✅ Consistent Minimal API contracts and Pinia store state management.
  - ✅ Full testability across xUnit C# unit tests and Vue/Vitest frontend specs.
- **Cons:**
  - ❌ Requires synchronization across backend DTO records and frontend TypeScript interfaces.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Enforces clinical truth and GxP compliance while providing interactive UI performance.

## 5. Consequences & Trade-offs

- **Positive Impact:** Enables interactive scenario simulation and real-time site risk monitoring.
- **Negative Impact:** Expands Minimal API surface area in `apps/execution` and `apps/quality`.
- **Mitigation Strategy:** Automated OpenAPI schema generation and Vitest/xUnit test coverage.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/engine_core`, `apps/execution`, `apps/quality`, `apps/web`.
- **Verification Plan:** Validated via `dotnet test` and `pnpm --filter web exec vitest run tests/components/StudyPulse.spec.ts`.
