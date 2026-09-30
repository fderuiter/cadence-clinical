# ADR-2591: Synchronous In-Process DSUR Compilation and Cryptographic Signing Engine

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Safety teams spend hundreds of hours manually compiling Development Safety Update Reports (DSURs) and cross-tabulating MedDRA terms. The platform requires an automated, in-process DSUR compiler engine in `apps/safety` to aggregate safety cases, calculate cumulative patient-years exposure from subject enrollment data, cross-tabulate MedDRA terms by SOC and PT against Reference Safety Information (RSI), store immutable JSON report snapshots, and apply 21 CFR Part 11 cryptographic signatures via `GxPSigner` to lock records (PRD-PVG-004, PRD-PVG-008).

## 2. Decision Drivers & Constraints

- **Driver 1:** DSUR compilation and MedDRA cross-tabulation must complete within 5 seconds for safety datasets (PRD-PVG-004).
- **Driver 2:** Cumulative patient-years exposure must be accurately derived from subject enrollment dates and discontinuation dates (PRD-PVG-004).
- **Driver 3:** Locked DSUR records with active signature manifests must remain strictly immutable and reject edit requests with HTTP 409 Conflict (PRD-PVG-004).
- **Driver 4:** Electronic signatures must use SHA-256 with RSA-PSS padding via `GxPSigner` and pass validation with `ESignatureVerifier` to satisfy 21 CFR Part 11 rules (PRD-PVG-008).

## 3. Options Considered

### Option 1: Synchronous In-Process DSUR Compiler Engine in `apps/safety` (Selected)

- **Overview:** Implement an in-process compiler engine (`DsurCompilerEngine`) that executes exposure calculations, de-identifies patient demographics, performs MedDRA SOC/PT cross-tabulations, persists immutable JSON snapshots in `PeriodicSafetyReportRecord.ReportPayloadJson` within a read-committed database transaction, and signs reports with `GxPSigner`.
- **Pros:**
  - ✅ Satisfies requirements PRD-PVG-004 and PRD-PVG-008.
  - ✅ High performance (< 5 seconds execution) without asynchronous queue latency.
  - ✅ Enforces record immutability and Part 11 cryptographic signature verification via `ESignatureVerifier`.
- **Cons:**
  - ❌ Holds database connection during exposure and cross-tabulation calculations within the read-committed transaction scope.

### Option 2: Asynchronous Background Job Processing via MassTransit

- **Overview:** Offload DSUR report generation to an asynchronous background worker queue.
- **Pros:**
  - ✅ Prevents long-running web HTTP requests.
- **Cons:**
  - ❌ Adds asynchronous orchestration complexity for synchronous UI feedback and fails the low-latency requirement for safety officers drafting reports.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Chosen because synchronous in-process compilation completes in under 5 seconds, simplifies read-committed transactional integrity, and seamlessly integrates with `GxPSigner` and `ESignatureVerifier` for 21 CFR Part 11 compliance (PRD-PVG-004, PRD-PVG-008).

## 5. Consequences & Trade-offs

- **Positive Impact:** Reduces DSUR preparation time from weeks to seconds while satisfying 21 CFR Part 11 electronic signature rules and maintaining patient de-identification.
- **Negative Impact / Technical Debt:** Memory usage scales with the size of the safety dataset passed during compilation.
- **Mitigation Strategy:** Optimized C# LINQ grouping and indexed database queries for large clinical trial datasets.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/safety` (`Cadence.Apps.Safety`), `packages/compliance` (`Cadence.Compliance`).
- **Verification Plan:** Unit tests in `tests/Cadence.UnitTests/Safety/DsurCompilerEngineTests.cs` and CLI quality sentinel gates.
