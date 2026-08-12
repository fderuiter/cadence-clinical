# ADR-[NUMBER]: Python-Native Isolated Architecture (Strict Compliance)

- **Status:** Accepted
- **Date:** 2026-08-12
- **Authors:** @fderuiter
- **Deciders:** @engineering-lead, @security-architect

---

## 1. Context & Problem Statement

Centralizing database schemas via a single Prisma schema violates GxP isolation guidelines and introduces a language stack mismatch into our Python backend. This compromise breaks compliance with ADR-2159 and risks violating FDA 21 CFR Part 11 regulations. We must maintain regulatory and compliance integrity by keeping independent databases and Python-native database tools.

Preserving our isolated, service-level Python database design will eliminate cross-language technical friction and guarantee seamless GxP compliance audits (PRD-SYS-001). Under regulatory compliance and GxP standards, clinical records must be protected against unauthorized data deletion (Trace-1).

## 2. Decision Drivers & Constraints

- **Compliance (GxP / 21 CFR Part 11):** Independent databases and strict data isolation for eTMF, CTMS, and Quality.
- **Node.js/Prisma Exclusions:** No Prisma schemas, client engines, or Node.js runtime packages in the database layer.
- **Deployments:** Separate schema changes without cross-database coordination dependencies.
- **Immutability:** Immutability triggers to block deletions of eTMF documents at the container boundary (Trace-1).

## 3. Options Considered

### Option 1: Centralized Node.js/Prisma DB Layer
Consolidating all schemas into a single Prisma schema file and using Node/Prisma runtime packages.
- **Pros:** Single tool for multi-database schemas.
- **Cons:** Violates GxP data isolation, introduces stack mismatch, conflicts with ADR-2159.

### Option 2: Python-Native Isolated Architecture (Selected)
Maintaining isolated SQLAlchemy and SQLModel ORM configurations per service, with localized migration toolchains, and enforcing deletion prevention via database-level triggers.
- **Pros:**
  - ✅ Guarantees 100% GxP data isolation.
  - ✅ Aligns with ADR-2159 and PRD-SYS-001.
  - ✅ Zero Node.js or Prisma dependencies in the database layer.
  - ✅ Localized migrations execute independently.
  - ✅ Restricts deletion on both database and ORM layers.
- **Cons:** None.

## 4. Decision Outcome

**Chosen Option:** Option 2 (Python-Native Isolated Architecture) because it complies with GxP data isolation, keeps the stack pure Python, and implements database-level triggers to prevent deletion of eTMF documents (Trace-1).

## 5. Consequences & Trade-offs

- **Positive Impact:** Decoupled schemas, fully isolated PostgreSQL containers for CTMS, eTMF, and Quality, and database-enforced immutability (PRD-SYS-001).
- **Negative Impact / Technical Debt:** None.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/etmf`, `apps/ctms`, `apps/quality`, and `packages/database`.
- **Verification Plan:** Verified via an automated test suite. Run `uv run pytest apps/etmf/tests/test_etmf_qc_invariants.py` to assert that document deletion is blocked.
