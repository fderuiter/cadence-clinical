# ADR-2816: Safety Signal Detection Engine Architecture

- **Status:** Accepted
- **Date:** 2026-10-15
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical trial pharmacovigilance requires automated disproportionality signal detection engines to compute statistical metrics such as Proportional Reporting Ratio (PRR), Yates Chi-Square, Reporting Odds Ratio (ROR), and Empirical Bayes Geometric Mean (EBGM / MGPS) on adverse event data across investigational drugs and comparators (PRD-PVG-003, PRD-SYS-169). Furthermore, 21 CFR Part 11 electronic signature validation and review workflow rules must be strictly enforced when confirming or closing safety alerts (PRD-PVG-008).

## 2. Decision Drivers & Constraints

- **Driver 1:** Need for deterministic disproportionality metric calculations (PRR, Yates Chi-Square, ROR, EBGM) for safety signal identification (PRD-PVG-003).
- **Driver 2:** Strict 21 CFR Part 11 electronic signature requirements for alert review and disposition decisions (PRD-PVG-008).
- **Driver 3:** Enforcing Clinical Kernel ID boundaries (K-XS-1) and blinding air-gap filtering for blinded safety cases.

## 3. Options Considered

### Option 1: Native C# SignalDetectionEngine and Minimal API Route Mapping (Selected)

- **Overview:** Implement `SignalDetectionEngine` in `Cadence.Apps.Safety.Application` using C# 14 / .NET 10, strongly-typed Kernel `StudyId`, Minimal API endpoints in `SignalEndpoints.cs`, and Part 11 e-signature verification for signal reviews.
- **Pros:**
  - ✅ Satisfies requirements PRD-PVG-003, PRD-PVG-008, and PRD-SYS-169.
  - ✅ Adheres to Clinical Kernel architecture rules and strongly-typed `StudyId`.
  - ✅ Enforces blinding air-gap filtering for blinded trial cases.
- **Cons:**
  - ❌ Requires maintaining mathematical disproportionality metric logic in C#.

### Option 2: External Python Microservice Integration

- **Overview:** Call an external Python service via HTTP for signal detection metric calculation.
- **Pros:**
  - ✅ Reuses legacy Python libraries.
- **Cons:**
  - ❌ Violates repository policy against Python runtime services (ADR-2245 / AGENTS.md).
  - ❌ Introduces cross-network latency and non-deterministic behavior.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Implementing a native .NET 10 `SignalDetectionEngine` ensures fast, deterministic, in-process statistical computation, strict Part 11 compliance, and full adherence to Kernel architecture boundaries.

## 5. Consequences & Trade-offs

- **Positive Impact:** Fast vectorized metric calculations, robust Part 11 e-signature enforcement, and clean architecture boundaries.
- **Negative Impact / Technical Debt:** Requires comprehensive unit and architecture test coverage for signal detection scenarios.
- **Mitigation Strategy:** Covered by unit tests in `SignalDetectionEngineTests.cs` and automated RTM/GxP compliance tracing.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/safety` (`Cadence.Apps.Safety`).
- **Verification Plan:** Unit tests in `tests/Cadence.UnitTests/Safety/SignalDetectionEngineTests.cs` and architecture tests in `tests/Cadence.ArchitectureTests`.
