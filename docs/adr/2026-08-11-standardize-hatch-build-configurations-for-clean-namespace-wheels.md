# ADR-2167: Standardize Hatch build configurations for clean namespace wheels

- **Status:** Accepted
- **Date:** 2026-08-11
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To establish consistent, reproducible, and standardized packaging and wheel builds across all 19 Python sub-projects (`apps/*` and `packages/*`) in the Cadence Clinical platform, we need to enforce uniform build configurations. Prior configurations had inconsistencies in namespace packaging, build targets, and metadata definition, which led to incorrect layout structure in generated wheel artifacts and potential import clashes in unified deployment environments. This decision addresses this issue to ensure proper workspace isolation under `PRD-SYS-001`.

## 2. Decision Drivers & Constraints

- **Developer Velocity & Maintainability:** Build artifacts must be consistent and intuitive to construct across all services.
- **System Integrity and Compliance (PRD-SYS-001):** Packages must build cleanly without pollution of global namespace or cross-project contamination.
- **Namespace Packaging:** Shared packages and services must conform to standard workspace/namespace layouts in wheels.

## 3. Options Considered

### Option 1: Legacy Build Configurations

- **Overview:** Use ad-hoc or standard implicit backend builds (setuptools/poetry/hatch) without strict packaging boundaries.
- **Pros:**
  - ✅ No additional migration effort.
- **Cons:**
  - ❌ Inconsistent wheel outputs and packaging targets.
  - ❌ Potential cross-package pollution and namespace import collisions.

### Option 2: Standardized Hatch Configurations (Selected)

- **Overview:** Standardize on Hatch and configure pyproject.toml explicitly with build targets (wheel, sdist), namespace tracking paths, and strict metadata targets.
- **Pros:**
  - ✅ Uniform build artifacts across all 19 python projects.
  - ✅ Explicitly defined search/namespace paths prevent import collisions.
  - ✅ Clean sdist and wheel configurations conformant to GxP audit standards.
- **Cons:**
  - ❌ Requires explicit build blocks across all pyproject.toml files.

## 4. Decision Outcome

**Chosen Option:** Option 2. Standardizing on Hatch configurations across all sub-projects ensures clean and reliable namespace wheels. This directly satisfies `PRD-SYS-001` and establishes reproducible packaging across the workspaces.

## 5. Consequences & Trade-offs

- **Positive Impact:** Clear packaging boundaries, reliable builds, and compliant tracing for audits.
- **Negative Impact / Technical Debt:** Small overhead of maintaining synchronized configuration blocks across `pyproject.toml` files.
- **Mitigation Strategy:** Use schema validation and automated linters to ensure build definitions remain intact.

## 6. Implementation & Verification

- **Affected Repositories / Services:** All 19 Python sub-projects in `apps/*` and `packages/*`.
- **Verification Plan:** Verified using the python tools and running `pnpm docs:build` locally to ensure documentation and packaging gates compile cleanly.
