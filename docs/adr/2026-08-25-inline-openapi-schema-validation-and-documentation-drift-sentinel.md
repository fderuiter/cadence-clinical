# ADR-2217: Inline OpenAPI Schema Validation and Documentation Drift Sentinel

* **Status:** Accepted
* **Date:** 2026-08-25
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

As backend microservices evolve and FastAPI routes or Pydantic schemas are modified, the exported OpenAPI specifications (`docs/openapi/`) and the unified API integration document (`docs/SDLC/03_API_Integration_Specification.md`) can quietly drift out of sync if documentation re-generation is not enforced.

Prior to this decision, API schema export and synchronization were manual or deferred steps, creating risk of uncommitted documentation drift in continuous integration and release pipelines. System requirement PRD-SYS-001 requires strict, continuous alignment between implementation and SDLC compliance artifacts.

## 2. Decision Drivers & Constraints

* **Strict GxP Compliance (PRD-SYS-001):** API documentation published in `docs/SDLC/03_API_Integration_Specification.md` must be 100% faithful to live FastAPI route definitions and Pydantic schemas.
* **Non-Destructive CI Operations:** Quality gates executing in PR CI environments must detect drift without leaving uncommitted file modifications in the runner workspace.
* **Unified Local & Remote Developer Workflows:** Local CLI tooling (`cadence check` / `cadence fix`) and GitHub Actions CI pipelines must execute identical sentinel logic.

## 3. Options Considered

1. **Option A (Selected - Inline Sentinel Script & Cadence Quality Gate):** Implement `scripts/validate_openapi_drift.py` to validate microservice schemas, re-export JSON specs to `docs/openapi/`, sync Section 7 of `docs/SDLC/03_API_Integration_Specification.md`, and assert zero uncommitted git status changes. If drift is detected, report actionable local remediation (`uv run cadence fix --all`) and execute non-destructive cleanup (`git checkout` and `git clean`). Integrate into `.github/workflows/ci.yml`, `cadence check -g openapi-drift`, and `cadence fix --all`.
2. **Option B (Separate Standalone CI Job):** Create a separate standalone GitHub Actions workflow job that runs schema validation. This incurs extra workflow orchestration overhead and slows PR feedback loops.
3. **Option C (Manual Developer Verification):** Rely on PR reviewers to notice schema divergence manually. Highly error-prone and non-compliant with GxP traceability standards.

## 4. Decision Outcome

Chosen option: **Option A**. Implementing an inline sentinel (`scripts/validate_openapi_drift.py`) integrated directly into local CLI commands (`cadence check`, `cadence fix`) and CI pipeline style verification satisfies PRD-SYS-001 with sub-minute execution times, non-destructive test execution, and automated remediation support.

## 5. Consequences & Trade-offs

* **Positive:**
  * Eliminates API contract and documentation drift across all 15 microservices.
  * Provides instant, actionable error messages and one-command local auto-remediation (`uv run cadence fix --all`).
  * Leaves CI runner workspace pristine via automatic non-destructive git checkout/clean steps upon detecting drift.
* **Negative:**
  * Developer PRs modifying API routes must include updated `docs/openapi/*.json` and `docs/SDLC/03_API_Integration_Specification.md` files in their commits.

## 6. Implementation & Verification

* **Sentinel Script:** `scripts/validate_openapi_drift.py`
* **Unit Tests:** `scripts/tests/test_validate_openapi_drift.py`
* **Local CLI Commands:** `packages/cli/commands/check.py` (`openapi-drift` gate) and `packages/cli/commands/fix.py` (`OpenAPI Schemas Export` & `OpenAPI Specification Sync` steps)
* **CI Workflow:** `.github/workflows/ci.yml` (`Run OpenAPI Documentation Drift Check` step)
* **Verification:** `uv run python scripts/validate_openapi_drift.py` and `uv run cadence check -g openapi-drift`.

