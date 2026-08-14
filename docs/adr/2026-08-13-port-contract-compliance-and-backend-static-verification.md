# ADR-[NUMBER]: Port Contract Compliance & Backend Static Verification

- **Status:** Accepted
- **Date:** 2026-08-13
- **Authors:** @jules
- **Deciders:** @jules

---

## 1. Context & Problem Statement

Developers previously faced runtime discrepancies and silent contract failures because the execution service repository adapters did not strictly conform to their defined domain port signatures. These inconsistencies bypassed verification due to redundant interface definitions and a lack of automated backend type checking.

To address this, we need to:

1. Eliminate duplicate, unused repository definitions from the domain layer.
2. Enforce that concrete database adapters and testing mock structures return fully populated domain models for signature saves.
3. Integrate an automated static verification gate in the deployment pipeline to block unmatched repository signatures.

This decision implements requirements under PRD-SYS-001.

## 2. Decision Drivers & Constraints

- **Driver 1:** Absolute type safety and contract enforcement at system boundaries to protect clinical data integrity and support GxP compliance standards.
- **Driver 2:** Zero-friction local development with fast-executing validations (under 2 minutes).
- **Driver 3:** Prevention of future contract drift and silent regressions.

## 3. Options Considered

### Option 1: Python Protocols checking only at runtime

- **Overview:** Rely on manual code reviews and Python Protocols at runtime.
- **Pros:**
  - ✅ No extra build steps.
- **Cons:**
  - ❌ Silent runtime failures are still possible.
  - ❌ No automated gate blocking unmatched signatures in the pipeline.

### Option 2: Automated Static Type Analysis via Mypy (Selected)

- **Overview:** Use a focused `mypy` execution targeted specifically at the repository contracts and their implementations, and run it as an automated gate in `pnpm check`, the `Makefile`, and pre-commit hooks.
- **Pros:**
  - ✅ Catch mismatching signatures instantly at development time.
  - ✅ High execution speed (sub-2 seconds) targeting the specific files.
  - ✅ Easy integration into the existing pre-push and PR pipelines.
- **Cons:**
  - ❌ Requires a small amount of configuration/setup overhead.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Implementing focused static analysis via `mypy` gives us 100% confidence in type correctness and boundary strictness, completing in under two seconds.

## 5. Consequences & Trade-offs

- **Positive Impact:** Automatic prevention of unmatched repository signatures during PR evaluations, zero interface drift, and rigid verifiable system boundaries.
- **Negative Impact / Technical Debt:** Additional type annotation overhead when introducing new repository methods.
- **Mitigation Strategy:** Provide a targeted verification script (`scripts/verify_contracts.py`) for developers to run fast local checks.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `apps/execution/domain/ports.py` (Source of truth repository interfaces)
  - `apps/execution/adapters/repositories.py` (Facade re-exports)
  - `apps/execution/infrastructure/repositories/execution_repositories.py` (Concrete database and in-memory mock repositories)
  - `scripts/verify_contracts.py` (Static contract type checking orchestrator)
- **Verification Plan:**
  - Run `uv run python scripts/verify_contracts.py` to confirm that all repository signatures fully comply with their ports.
  - Integrate contract type check into `.pre-commit-config.yaml`, `package.json` (`pnpm check`), and the `Makefile` (`make typecheck`).
