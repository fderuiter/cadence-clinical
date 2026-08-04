# ADR-256: Enforce frozen lockfile alignment across dev and service containers

* **Status:** Accepted
* **Date:** 2026-08-04
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Previously, local development and service composition containers bypassed locked dependency definitions. Instead of strictly respecting local lockfiles, containers dynamically resolved third-party packages at build or startup time. This introduced silent runtime drifts, leading to situations where local development environments diverged from CI/CD states, causing hard-to-debug "works on my machine" failures. The objective is to ensure absolute parity across local development, testing, and production environments (PRD-SYS-001).

## 2. Decision Drivers & Constraints

* Ensure identical dependency resolution across dev, test, and production stages.
* Catch dependency mismatches early during local development and build time.
* Prevent external registry queries to improve build speed and predictability.
* Preserve Hot Module Replacement (HMR) and internal package symlinking for frontend development.

## 3. Options Considered

1. Option A (Selected): Enforcing frozen installations (`--frozen` & `--frozen-lockfile`)
   By explicitly copying `pyproject.toml`, `uv.lock`, `package.json`, and `pnpm-lock.yaml` prior to installation, we enforce strict dependency alignment with `--frozen` (for Python/`uv`) and `--frozen-lockfile` (for Node.js/`pnpm`). This guarantees that any mismatch between the project descriptors and lockfiles fails fast.
2. Option B (Alternative): Keep dynamic resolution
   Bypassing lockfile validation during build and resolving packages dynamically at container boot time. This risks silent version drift and unexpected runtime errors.

## 4. Decision Outcome

Chosen option: Option A because it guarantees environments remain strictly synchronized across all stages of development and deployment, preventing unexpected version drift and satisfying PRD-SYS-001.

## 5. Consequences & Trade-offs

* Positive: Complete environments alignment, offline-resilient builds, faster build pipelines, and immediate fail-fast error reporting on package definition mismatches.
* Negative: Lockfiles must be explicitly updated and aligned before container builds can succeed, adding a small local step during dependency upgrades.

## 6. Implementation & Verification

* Target Dockerfiles updated to copy lockfiles and run `uv sync --frozen` or `pnpm install --frozen-lockfile`.
* Local Compose setup configured to use frozen installations.
* Verification of container stability across all microservices.
