# ADR-253: Align Workspace Dependencies and Eliminate Duplicate Declarations

* **Status:** Accepted
* **Date:** 2026-09-08
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
To ensure absolute package ecosystem stability, predictability, and compliance under **PRD-SYS-001**, the Cadence Clinical workspace dependencies must be kept aligned and free of upper dependency ceilings. Redundant upper limits on critical packages (such as `sqlmodel`) prevent the package manager from resolving optimal sub-dependencies across service boundaries and can lead to runtime package mismatch errors. Furthermore, duplicate package declarations across the workspace configurations must be unified to avoid compilation drift and package caching inconsistencies.

## 2. Decision Drivers & Constraints
* **Predictability & Long-Term Stability:** Eliminate restrictive upper version limits that lead to dependency conflicts during package resolution.
* **Service Boundary Safety:** Ensure that all workspace member projects resolve shared dependencies (like `sqlmodel`) to exact, synchronized versions.
* **Strict Code Quality Gating:** Keep project files aligned with ruff formatting and linter validations.

## 3. Options Considered
### Option 1: Maintain Rigid Upper Ceilings
* **Overview:** Keep upper dependency limits in individual `pyproject.toml` configurations.
* **Pros:**
  * ✅ Guards against breaking changes in future major version releases.
* **Cons:**
  * ❌ Restricts package managers from resolving valid, security-patched minor updates.
  * ❌ Leads to split package versions across workspace boundaries.

### Option 2: Remove Ceilings and Synchronize Declarations [Selected]
* **Overview:** Remove restrictive upper version caps (e.g. `<=0.0.39` or `<0.0.40`) and align all references to standard workspace-wide packages.
* **Pros:**
  * ✅ Seamless package resolution via `pnpm` and `uv` across all packages.
  * ✅ Solves runtime and static type checking version conflicts.
  * ✅ Resolves duplicate `sqlmodel` declaration issues cleanly.
* **Cons:**
  * ❌ Requires periodic validation of major package upgrades.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Choosing Option 2 establishes a uniform workspace environment, reducing package-resolution frictions and alignment issues, which directly supports the robust verification workflow required under **PRD-SYS-001**.

## 5. Consequences & Trade-offs
* **Positive Impact:** Cleaner workspaces, straightforward dependency management, and deterministic builds.
* **Negative Impact / Technical Debt:** Requires keeping major package upgrades documented and tested.
* **Mitigation Strategy:** Enforce locked lockfiles in CI builds and dry-run dependency checks before merging.

## 6. Implementation & Verification
* **Affected Repositories / Services:** All member `pyproject.toml` manifests under `apps/` and `packages/`.
* **Verification Plan:** Validated via `uv run ruff check .` and verifying successful workspace install with `pnpm install --frozen-lockfile`.
