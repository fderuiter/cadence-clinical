# ADR-253: Upgrade Environments to Python 3.14

- **Status:** Accepted
- **Date:** 2026-08-03
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Standardizing our runtime baseline ensures absolute environmental consistency and compliance correctness across all deployment layers. Historically, developers and central execution workloads ran on older environments (such as Python 3.12).

Standardizing all development workspaces, GitHub Actions automation, and Docker deployment targets on a modern unified **Python 3.14** baseline ensures that we eliminate architectural overhead, dynamic runtime drift, and compilation/linting variations across testing and production environments.

This traces to requirement **PRD-SYS-001** (GxP Environment Consistency and System Integrity).

## 2. Decision Drivers & Constraints

- **Zero Environment Drift:** Ensure perfect, 100% functional alignment between local dev boxes, CI pipelines, and live containers.
- **GxP Compliance & Automated Tracing:** Ensure that regulatory reports (such as IQ/OQ/PQ Qualification Report) capture a statically locked runtime signature (`Python 3.14`).
- **Future-Proof Standard Library & Type Tooling:** Gain access to modern Python 3.14 syntax capabilities, modern type-checking annotations, and optimized runtime execution features.

## 3. Options Considered

1. **Option A (Selected):** Direct, statically locked upgrade to Python 3.14 across all workspaces, workflows, tooling manifests, local dev environment specifications, and Dockerfile layers.
2. **Option B (Alternative):** Maintain legacy Python 3.12 runtime and tooling boundaries, creating potential drift across active workflows and sacrificing modern syntax checks.

## 4. Decision Outcome

Chosen option: **Option A** because it enforces strict, predictable GxP environment compliance (PRD-SYS-001) without dynamic configuration complexity. Furthermore, standardizing on Python 3.14 unlocks highly optimized interpreter execution paths and strict modern Ruff/type-checking configurations.

## 5. Consequences & Trade-offs

- **Positive:** Absolute consistency across testing and production targets.
- **Positive:** Strict style configurations (e.g. automatic UP037 and UP043 type-annotation upgrades) automatically enforced by Ruff under the Python 3.14 runtime target.
- **Positive:** IQ/OQ/PQ validation logs statically assert and qualify on Python 3.14.5.
- **Negative:** Workspace dependencies must be locked and compatible with the Python 3.14 baseline.

## 6. Implementation & Verification

- **Target files/packages modified:**
  - Docker deployment configuration: `docker/Dockerfile`
  - Workflow actions: `.github/workflows/ci.yml`, `.github/workflows/production-pipeline.yml`, `.github/workflows/project-automation.yml`
  - Packaging and workspace manifests: `pyproject.toml`, `package.json`, `uv.lock`
  - Runtime specification: `.python-version`, `Makefile`
  - Developer and system guidelines: `README.md`, `CONTRIBUTING.md`, `AGENTS.md`, `docs/LOCAL_DEV_ENVIRONMENT.md`, `docs/SDLC/07_Operations_Deployment_Guide.md`
- **Verification tests:**
  - System-level environment asserts under `apps/eisf/tests/test_eisf_compliance.py` updated to verify a minimum Python runtime version of `>= (3, 14)`.
  - Style and lint validation: All codebase files formatted and lint-checked cleanly using target runtime syntax.
