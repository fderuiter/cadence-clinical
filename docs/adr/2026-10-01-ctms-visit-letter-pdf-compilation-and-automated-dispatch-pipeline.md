# ADR-2802: CTMS Visit Letter PDF Compilation and Automated Dispatch Pipeline

- **Status:** Accepted
- **Date:** 2026-10-01
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical Trial Management Systems (CTMS) require Clinical Research Associates (CRAs) to compile, sign, and file site visit letters (e.g. pre-study, site initiation, interim monitoring, site close-out) in compliance with 21 CFR Part 11 and GxP standards. The visit letters must be compiled into tamper-evident PDF documents, signed electronically when finalized, filed into the eTMF repository, and dispatched via email notifications to site investigators.
@req:PRD-SYS-001

## 2. Decision Drivers & Constraints

- **21 CFR Part 11 Compliance:** Finalized visit letters require PKCS#7 electronic signatures and manifestation JSON.
- **Microservice Isolation:** CTMS must not directly import sibling service DbContexts (e.g. eTMF, Notifications) to preserve microservice boundaries.
- **Auditability:** Every generated letter and dispatch action must produce immutable GxP audit trail logs in CTMS.

## 3. Options Considered

### Option 1: Decoupled Dynamic Reflection and Service Resolution (Selected)

- **Overview:** CTMS compiles GxP PDFs and dispatches eTMF filing and email notifications via decoupled reflection-based service resolution rather than direct compile-time DbContext dependencies.
- **Pros:**
  - ✅ Preserves microservice isolation boundaries (passes Gate 12).
  - ✅ Enables 21 CFR Part 11 compliant PDF compilation and audit logging.
- **Cons:**
  - ❌ Uses reflection for cross-service entity dispatch.

### Option 2: Direct DbContext Project References

- **Overview:** CTMS directly references `Cadence.Apps.Etmf` and `Cadence.Apps.Notifications`.
- **Pros:**
  - ✅ Strongly-typed entity access at compile time.
- **Cons:**
  - ❌ Violates cross-service architectural import boundaries (fails Gate 12).

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Selected to satisfy GxP visit letter requirements while strictly maintaining microservice architecture boundaries.

## 5. Consequences & Trade-offs

- **Positive Impact:** Clear operational boundaries and GxP auditability for visit letter PDF generation and dispatch.
- **Negative Impact / Technical Debt:** Reflection-based cross-service helper execution.
- **Mitigation Strategy:** Comprehensive unit and integration test coverage.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/ctms`
- **Verification Plan:** Verified via `dotnet test` and `pnpm cadence check`.

## Amendment (2026-10-15): migration registered

`20261001180000_AddVisitLetterPdfAndDispatchFields` was committed without a `.Designer.cs` or a
`[Migration]` attribute, so EF never applied it and `ctms_generated_letters` lacked the PDF and dispatch
columns the model maps. The migration now carries `[DbContext]` and `[Migration]` attributes and applies
in order.
