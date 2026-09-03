# ADR-2239: Universal Decimal Financial Precision Dynamic OpenAPI Gateway Discovery and Tier 2 Quality Realignment

- **Status:** Accepted
- **Date:** 2026-09-03
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

During the Phase 0 forensic gap analysis (AUD-NET10-001) of the Cadence Clinical repository, three critical architectural hazards were uncovered that jeopardized migration to .NET 10:

1. **Floating-Point Financial Non-Compliance:** The legacy Python implementation in `apps/payments` modeled monetary currency amounts, fee schedules, split payee shares, withholding deductions, and invoice totals using binary floating-point (`float`). Under 21 CFR Part 11 and IRS 1099 compliance regulations, binary floating-point arithmetic introduces IEEE-754 precision drift, risking audit disqualification and ledger reconciliation errors.
2. **In-Process Gateway Coupling:** `apps/gateway/main.py` aggregated downstream OpenAPI schemas by dynamically importing `apps.{name}.main` in-process. Transcribing Python services to .NET 10 breaks this reflection mechanism, causing gateway startup crashes or missing route documentation.
3. **False Leaf Node Sequencing:** `apps/quality` was designated as a Tier 1 leaf node, yet its surveillance runner (`QualityExecutionClient`) relies on HTTP calls to `apps/execution` (port 8002).

---

## 2. Decision Drivers & Constraints

- **21 CFR Part 11 & IRS 1099 Integrity:** Absolute financial ledger precision is non-negotiable; rounding errors on participant stipends and investigator payouts violate GxP traceability (`PRD-SYS-053`, `PRD-PAY-002`).
- **Strangler Fig Proxy Independence:** The gateway must route traffic and publish API documentation transparently whether a downstream service is running on Python FastAPI or .NET 10 Minimal APIs.
- **Hermetic Integration Testing:** Services in higher migration tiers must be testable without circular dependencies or unmocked upstream network failures.

---

## 3. Options Considered

### A. Financial Precision

1. **(Selected) Universal C# 14 Decimal Standard:** Enforce strict C# 14 `decimal` (`System.Decimal`) mapped to PostgreSQL `numeric(18, 4)` with `MidpointRounding.ToEven` (banker's rounding) across all financial DTOs, domain models, and calculation engines.
2. **Floating-Point with API Boundary Coercion:** Retain `double` internally and coerce to decimal at the HTTP serialization boundary. _Rejected: Internal domain calculation accumulation errors would remain._

### B. OpenAPI Gateway Discovery

1. **(Selected) Dynamic HTTP Discovery & Precompiled Schema Fallback:** Gateway polls downstream `/openapi.json` endpoints asynchronously and merges them into the public schema, falling back to build-time generated OpenAPI artifacts in `docs/openapi/`.
2. **Retain In-Process Module Reflection:** Keep `importlib` and stub out .NET services with Python placeholder files. _Rejected: Violates clean-break migration and introduces synthetic technical debt._

### C. Quality Service Sequencing

1. **(Selected) Tier 2 Realignment with Typed Contracts:** Demote `Cadence.Quality` to Tier 2 (migrating after `Cadence.Execution`), standardizing cross-service interactions on `IExecutionApiClient` in `Cadence.Contracts`.
2. **Retain Tier 1 with Ad-hoc Mocks:** Keep `Cadence.Quality` in Tier 1 using loose HTTP string mocks. _Rejected: Fragile contracts fail to detect schema drift._

---

## 4. Decision Outcome

1. **Adopt Universal Decimal Precision Standard:** All currency, fees, withholding rates, overheads, and stipend records in `Cadence.Payments` must use `decimal` with explicit banker's rounding.
2. **Decouple Gateway Schema Discovery:** API Gateway discovers OpenAPI endpoints over standard HTTP (`GET /openapi.json`) or from static schema files, eliminating runtime Python module imports.
3. **Reclassify Cadence.Quality as Tier 2:** `Cadence.Quality` is scheduled in Tier 2 following `Cadence.Execution`. Cross-service calls use typed `IExecutionApiClient` contracts.

---

## 5. Consequences & Trade-offs

- **Positive:** Eliminates floating-point calculation drift under financial audit; enables clean Strangler Fig proxying without Python in-process dependencies; establishes robust contract-based integration testing.
- **Negative:** Requires updating `BOUNDED_CONTEXT_MIGRATION_MATRIX.md` dependency graphs; requires implementing typed contracts in `Cadence.Contracts` prior to Tier 2 service migration.

---

## 6. Implementation & Verification

- **Glossary Updated:** Added `Universal Decimal Precision Standard` and `Canonical Timestamp Normalization Invariant` to `CONTEXT.md`.
- **Migration Hub Documented:** Updated `docs/migration/Migration_Phase0_Readiness_Audit.md` and indexed in `docs/migration/index.md`.
- **Validation:** Verified using `python scripts/validate_adrs.py` and `python scripts/validate_markdown.py`.
