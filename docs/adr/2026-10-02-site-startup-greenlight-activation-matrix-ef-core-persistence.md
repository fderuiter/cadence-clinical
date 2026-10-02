# ADR-2805: Site Startup Greenlight Activation Matrix EF Core Persistence

- **Status:** Accepted
- **Date:** 2026-10-02
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Site Startup (SSU) and Country-Level Multi-Site Greenlight Activation Matrix require persistent state tracking for six facet readiness checks (Regulatory IRB, CTA Budget, eISF Baseline, DOA Log, Staff Training, and IP Release) along with individual greenlight verification tokens. (PRD-SSU-001)

## 2. Decision Drivers & Constraints

- **Driver 1:** 21 CFR Part 11 and GxP readiness require explicit recording of greenlight certification tokens and override justifications.
- **Driver 2:** Business/GxP requirement (PRD-SSU-001)

## 3. Options Considered

### Option 1: Proposed Architecture / Primary Approach (Selected)

- **Overview:** Map CtmsSiteGreenlightGate in CtmsDbContextModelSnapshot.cs and CtmsConfigurations.cs.
- **Pros:**
  - ✅ Satisfies requirement PRD-SSU-001
  - ✅ Maintains operational boundaries and compliance tracing
- **Cons:**
  - ❌ Requires schema snapshot maintenance in CTMS

### Option 2: Alternative Architecture / Status Quo

- **Overview:** Transient in-memory evaluation.
- **Pros:**
  - ✅ Requires minimal initial scaffolding
- **Cons:**
  - ❌ Fails to fully address requirements or long-term maintainability

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Chosen because it satisfies PRD-SSU-001 while ensuring system maintainability, auditability, and clear operational boundaries.

## 5. Consequences & Trade-offs

- **Positive Impact:** Clear operational boundaries and compliance tracing for requirement PRD-SSU-001.
- **Negative Impact / Technical Debt:** Maintenance overhead of additional structured abstractions.
- **Mitigation Strategy:** Automated test coverage and CI lint validation.

## 6. Implementation & Verification

- **Affected Repositories / Services:** apps/ctms
- **Verification Plan:** Automated unit and architecture tests added under tests/.
