# ADR-[NUMBER]: Separate Monolithic Clinical Workflows into Modular Sub-routers

- **Status:** Accepted
- **Date:** 2026-08-12
- **Authors:** @jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The clinical workflows in the clinical trial execution service `apps/execution/main.py` grew over time into a massive monolithic structure. This monolithic design made maintenance extremely difficult, hindered developer velocity, and increased risk of merge conflicts during parallel feature development. To resolve this, we separated five major domain-specific workflows (Subject Randomization, Emergency Unblinding, Clinical Queries, Medical Dictionary Ingestion, and SDTM/ADaM exports) into isolated presentation sub-routers and router proxies under `apps/execution/presentation/routers/` and `apps/execution/routers/`.

This decision implements and traces requirements under Trace-1, Trace-2, PRD-QRY-001, and PRD-EDC-001.

## 2. Decision Drivers & Constraints

- **Driver 1:** Separation of Concerns (Hexagonal architecture guidelines require modular and domain-specific presentation routers).
- **Driver 2:** Maintainability and Developer Velocity (Preventing concurrent merge conflicts and keeping code organized).
- **Driver 3:** Compliance with GxP and Part 11 audit trails (Isolating safety-critical randomization and unblinding logic into clean boundaries).

## 3. Options Considered

### Option 1: Maintain the Monolithic Structure in main.py

- **Overview:** Keep all clinical workflows integrated inside `apps/execution/main.py`.
- **Pros:**
  - ✅ No structural architectural changes needed.
  - ✅ Avoids triggering ADR requirement gates for schema/sub-router additions.
- **Cons:**
  - ❌ Extremely high file size and cognitive load for developers.
  - ❌ Prone to severe git merge conflicts and regression risks.

### Option 2: Extract Modular Domain-Specific Sub-routers and Proxies

- **Overview:** Move workflows to dedicated sub-routers under `apps/execution/presentation/routers/` and expose them via proxies in `apps/execution/routers/` registered in alphabetical order in `main.py`.
- **Pros:**
  - ✅ Restores high code modularity and clean architectural boundaries.
  - ✅ Simplifies unit testing and localized static audit.
- **Cons:**
  - ❌ Requires creating several new router/schema files, necessitating an Architecture Decision Record.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Choosing Option 2 is the most robust long-term solution. It enforces a strict separation of concerns, ensures our clinical trial platform remains modular, and keeps individual source files small, clean, and safe from parallel merge conflicts.

## 5. Consequences & Trade-offs

- **Positive Impact:** Much cleaner repository, faster code reviews, isolated regression paths for unblinding and randomization workflows.
- **Negative Impact / Technical Debt:** Extra files added to the workspace, requiring maintenance of the router registry structure.
- **Mitigation Strategy:** Automated test suites verify endpoint routing and integration compatibility on every commit.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/execution`
- **Verification Plan:** Validated by running the extensive execution test suite `pytest apps/execution/tests/` and checking compliance via `uv run python scripts/validate_adrs.py`.
