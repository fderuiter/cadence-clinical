# ADR-134: Upgrade Environments to Python 3.14

- Status: Accepted
- Date: 2026-08-03
- Authors: @google-labs-jules
- Deciders: @fderuiter

---

## 1. Context & Problem Statement

Currently, hardcoded Python references across configurations, local development tooling, deployment setups, and compliance documentation restrict our clinical execution ecosystem to older runtimes. This environment divergence introduces operational risks, compatibility overhead, and potential compliance drift in regulatory validation reports (specifically GxP Installation Qualification).

By standardizing all workspaces, build pipelines, and container execution environments on a unified, statically locked Python 3.14 baseline, we eliminate environment drift, ensure absolute functional parity across the lifecycle, and satisfy clinical compliance audits. This decision directly supports standard audit logging and GxP requirements traced in (PRD-SYS-001) and (Trace-1).

## 2. Decision Drivers & Constraints

- Eliminate operational environment drift between local dev, test verification pipelines, and containerized deployment runtimes.
- Ensure strict functional parity and compliance verification in GxP Installation Qualification (PRD-SYS-001).
- Streamline package management and linting rules using a statically locked runtime targeted to Python 3.14.

## 3. Options Considered

### Option 1: Maintain multi-version co-existence or older Python versions

- **Overview:** Keep development environments running on Python 3.12 while production runtimes or other pipelines use separate versions, or dynamic runtime discovery.
- **Pros:**
  - ✅ Less immediate upgrade effort.
- **Cons:**
  - ❌ High environment divergence, complex dependency matrix, and manual GxP compliance tracking.

### Option 2: Direct upgrade and standardization on Python 3.14 (Selected)

- **Overview:** Execute a direct, clean upgrade of all workspace definitions, CI/CD pipelines, Ruff/Black formatters, and Docker images to target and enforce Python 3.14.
- **Pros:**
  - ✅ Completely eliminates runtime parity issues.
  - ✅ Ensures robust compliance reporting (PRD-SYS-001).
  - ✅ Allows leveraging modern type hinting features and ruff-based optimizations.
- **Cons:**
  - ❌ Demands immediate update of dependencies, tooling files, and developer setups.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Choosing Option 2 directly enforces environment integrity, satisfies clinical GxP standards as defined in (PRD-SYS-001), and provides a statically locked, highly predictable runtime baseline across local and remote environments.

## 5. Consequences & Trade-offs

- **Positive Impact:** Full operational parity across local workspaces, CI/CD runners, and container environments. Improved type-safety verification and ruff formatting.
- **Negative Impact / Technical Debt:** Requires all developers and pipeline containers to support and install Python 3.14.
- **Mitigation Strategy:** Automated verification suites and CI/CD jobs will strictly validate the target Python version to prevent regression.

## 6. Implementation & Verification

- **Affected Repositories / Services:** Workspace configurations, container files (`docker/Dockerfile`), local developer tooling (`package.json`, `pyproject.toml`), and CI/CD pipelines (`.github/workflows/`).
- **Verification Plan:** Verified via the automated validation suite running environment integrity checks (`apps/eisf/tests/test_eisf_compliance.py`), which assert a minimum Python version of `>= (3, 14)`, and standard linting checks.
