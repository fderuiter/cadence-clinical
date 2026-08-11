# ADR-133: Upgrade Environments to Python 3.12

- **Status:** Accepted
- **Date:** 2026-07-31
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Currently, developers build and test code locally using Python 3.11, while the production environment, operations pipelines, and deployment runtimes utilize Python 3.12. This environment divergence introduces several critical risks:

- **Silent runtime bugs** caused by minor syntax or dependency deviations.
- **Bypassed environmental verifications** during local testing.
- **Compliance drift** in regulatory validation reports (specifically GxP Installation Qualification).

By standardizing all workspaces, build pipelines, and container execution environments on a unified Python 3.12 baseline, we eliminate environment drift, ensure absolute functional parity across the lifecycle, and streamline compliance audits. This traces to requirement (PRD-SYS-001).

## 2. Decision Drivers & Constraints

- Eliminate operational environment drift between local dev and cloud production runtimes.
- Ensure absolute functional parity and compliance verification in GxP Installation Qualification (PRD-SYS-001).
- Maintain simple and deterministic build environments utilizing modern packaging standards (e.g., uv).

## 3. Options Considered

1. **Option A (Selected):** Standardize all development environments, dependency specifications, Docker bases, and GitHub Actions workflows on Python 3.12.
2. **Option B (Alternative):** Maintain the dual 3.11/3.12 support, exposing the project to environmental drift and manual GxP compliance overhead.

## 4. Decision Outcome

Chosen option: Option A because it completely eliminates environment drift, fulfills GxP integrity requirements (PRD-SYS-001), and aligns our developer environment with production runtimes.

## 5. Consequences & Trade-offs

- Positive: Full parity between development, testing, staging, and production runtimes.
- Positive: Direct dynamic compliance and verification for GxP IQ reports.
- Negative: Developers must have Python 3.12 installed locally.

## 6. Implementation & Verification

- Target files/packages modified: `pyproject.toml`, `package.json`, `Makefile`, `.python-version`, `.github/workflows/ci.yml`, `.github/workflows/project-automation.yml`, `docker/Dockerfile`.
- Verification tests added or updated under `apps/eisf/tests/test_eisf_compliance.py` to assert a minimum Python runtime version of `>= (3, 12)`.
