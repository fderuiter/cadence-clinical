# ADR-253: Lightweight Path Pattern Boundary Linter

- **Status:** Accepted
- **Date:** 2026-08-03
- **Authors:** @google-labs-jules[bot], @fderuiter
- **Deciders:** @fderuiter, @engineering_leads, @qa_lead

---

## 1. Context & Problem Statement

As the Cadence Clinical repository and engineering organization scale, maintaining clear boundaries between modules is essential to enforce correct system layout and GxP compliance boundaries. Developers and automated agents can occasionally place code, configurations, or schemas in incorrect folders. This causes layout drift and structural anomalies that are typically caught late in remote pipelines, hurting developer velocity and increasing verification friction.

To prevent these layout violations from ever reaching the shared repository, we need a fast, local path-validation check that acts as both a git pre-commit hook and a continuous integration guardrail.

This decision addresses structural and layout verification requirements under **PRD-SYS-001**.

## 2. Decision Drivers & Constraints

- **Driver 1:** Enforce strict operational directory boundaries without delaying developer actions.
- **Driver 2:** Prevent layout drift across apps, packages, scripts, tests, and documentation.
- **Driver 3:** Zero external dependencies and high performance, executing in under 150 milliseconds.
- **Constraint:** Must run cleanly as a local pre-commit hook and in the remote CI pipeline.

## 3. Options Considered

### Option 1: AST-Based and Language-Specific Content Linting

- **Overview:** Write complex parsers to analyze code structure, import statements, and AST patterns across JS/TS/Python.
- **Pros:**
  - ✅ High precision of semantic usage of components/imports.
- **Cons:**
  - ❌ Heavy execution overhead, far exceeding the 150ms target.
  - ❌ Fragile when encountering unparseable or intermediate file states during staging.

### Option 2: Static Path Pattern and Layout Linter

- **Overview:** Build a lightweight, static glob and pattern matcher utilizing Python's standard library (`pathlib`, `fnmatch`).
- **Pros:**
  - ✅ Zero external runtime dependencies.
  - ✅ Fast execution (< 100ms) with minimal footprint.
  - ✅ Highly actionable failure messages specifying correct target folders.
  - ✅ Easily integrable into git pre-commit hooks and make pipelines.
- **Cons:**
  - ❌ Does not parse code contents to verify inline import boundaries (which are already handled by other linters/tests).

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Choosing Option 2 satisfies our performance and simplicity goals under **PRD-SYS-001** and prevents repository structural degradation without adding runtime complexity.

## 5. Consequences & Trade-offs

- **Positive Impact:** Fast, reliable, local pre-commit and CI verification of structural correctness.
- **Negative Impact / Technical Debt:** Requires updating the allowed path patterns list if new directories or files are added at the root level of the workspace.

## 6. Implementation & Verification

- **Affected Repositories / Services / Files:**
  - `scripts/validate_path_patterns.py` (Core validation logic)
  - `.pre-commit-config.yaml` (Hook definition)
  - `Makefile` (New `lint-paths` target)
  - `package.json` (Updated `lint` script command)
  - `tests/validation/test_path_boundary_linter.py` (Unit and negative scenario integration tests)
- **Verification Plan:**
  - Execute `uv run python scripts/validate_path_patterns.py --all` to verify standard layout alignment.
  - Run `uv run pytest tests/validation/test_path_boundary_linter.py` to ensure all linter test scenarios pass cleanly.
