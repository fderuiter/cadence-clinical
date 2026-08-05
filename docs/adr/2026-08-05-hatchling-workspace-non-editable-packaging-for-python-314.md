# ADR-2159: Non-Editable Workspace Sources for Python 3.14 Packaging Compatibility

- Status: Accepted
- Date: 2026-08-05
- Authors: @google-labs-jules
- Deciders: @fderuiter

---

## 1. Context & Problem Statement

Standardizing our local development and continuous integration environments on Python 3.14 exposes incompatibility issues with editable installations of workspace packages configured via Hatchling (such as standard Hatchling prefix paths with empty prefix `"" = "apps/<name>"`). Under Python 3.14, these editable installations raise module loading errors and fail to load package metadata correctly. This prevents successful building of the workspace packages, execution of our qualification suites, and validation of GxP compliance.

To maintain environment stability, standard package execution parity, and ensure that our automated validation runs properly under the standardized Python 3.14 runtime baseline, we must adapt our Hatchling workspace configuration.

## 2. Decision Drivers & Constraints

- Ensure reliable package builds and execution under Python 3.14 (PRD-SYS-001).
- Prevent runtime module loading failures in development and CI environments.
- Maintain workspace source separation without reverting to a monolithic non-package structure.

## 3. Options Considered

### Option 1: Revert to Python 3.12 or older Python runtime standard

- **Overview:** Avoid Python 3.14 compatibility limitations by remaining on an older version of Python.
- **Pros:**
  - ✅ Keeps editable installation support working without reconfiguration.
- **Cons:**
  - ❌ Breaks environment alignment across our standard platform requirements and limits modern performance optimizations.

### Option 2: Configure non-editable workspace sources under Hatchling (Selected)

- **Overview:** Explicitly set `editable = false` in our workspace sources configuration and configure packages directly in `pyproject.toml` to install them in non-editable mode.
- **Pros:**
  - ✅ Restores full packaging compatibility and successfully installs all packages in Python 3.14.
  - ✅ Ensures all qualification tests and security audits run flawlessly in CI.
- **Cons:**
  - ❌ Changes to packages require running `uv sync` to update the local installed copies (slightly more compilation overhead during package structure changes).

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Choosing Option 2 directly resolves the compatibility issues under Python 3.14, allows us to keep our multi-package architecture, and guarantees successful builds and clean test passes across the entire CI workflow (PRD-SYS-001).

## 5. Consequences & Trade-offs

- **Positive Impact:** All core workspace packages and applications install successfully under Python 3.14. Parity is achieved across local development environments, linting jobs, and CI.
- **Negative Impact / Technical Debt:** Requires running `uv sync` to pick up package layout changes, though this is already the standard developer workflow in this repository.
- **Mitigation Strategy:** Document the standard installation process in the index and the workspace readme.

## 6. Implementation & Verification

- **Affected Repositories / Services:** Workspace-level package config files (`pyproject.toml`, `apps/gateway/pyproject.toml`, `packages/security/pyproject.toml`).
- **Verification Plan:** Validated by running the full suite of style and lint verifications, path-pattern validators, and isolated GxP qualification tests in Python 3.14 container and runner environments (PRD-SYS-001).
