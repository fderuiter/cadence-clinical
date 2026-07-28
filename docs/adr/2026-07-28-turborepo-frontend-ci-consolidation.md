# ADR-104: Integrate Turborepo and Consolidate Frontend CI Verification

* Status: Accepted
* Date: 2026-07-28
* Authors: @jules
* Deciders: @fderuiter

---

## 1. Context & Problem Statement
Currently, our CI pipeline runs frontend checks sequentially and rebuilds unchanged packages from scratch on every execution. Furthermore, the pipeline lacks a compilation check, exposing us to potential build/compilation errors in production. 

To improve developer velocity and keep Pull Request feedback loops under three minutes, we have integrated **Turborepo** as our local and CI task orchestrator. By running tasks concurrently and caching results of unchanged packages, we can significantly speed up local and pipeline verification runs while catching compilation errors early in the process.

To keep the pipeline architecture simple, clean, and easy to maintain, we decided to avoid remote caching overhead and keep CI runs within a single sequential workflow step, consolidating all frontend checks into a unified command execution.

This decision implements requirements under Trace-1.

## 2. Decision Drivers & Constraints
* **Developer Velocity:** PR feedback loops must stay under three minutes.
* **Pipeline Simplicity:** Avoid remote caching overhead and keep CI runs within a single step.
* **Compilation Coverage:** Catch compilation errors in development and PR stages before they reach production.

## 3. Options Considered
### Option 1: Individual Sequential Package Runs
* **Overview:** Maintain the legacy setup of running `pnpm -r` commands sequentially for linting, formatting, and testing.
* **Pros:**
  * ✅ No additional tools or configurations required.
* **Cons:**
  * ❌ Inefficient, slow, and does not support task concurrency or local caching.
  * ❌ Lacks compilation/build verification across the entire workspace.

### Option 2: Integrated Turborepo Orchestration (Selected)
* **Overview:** Add a local/CI task orchestrator using Turborepo with concurrency and caching of results.
* **Pros:**
  * ✅ Dramatically reduces local and pipeline verification times.
  * ✅ Concurrently runs `lint`, `format`, `test`, and `build` tasks.
  * ✅ Simplifies CI config by consolidating checks into `pnpm turbo run lint format test build`.
* **Cons:**
  * ❌ Introduces a small dependency on `turbo`.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Turborepo enables concurrent package verification and local/CI caching. It optimizes developer velocity by skipping unchanged tasks and ensures that build/compilation checks cover all workspace packages.

## 5. Consequences & Trade-offs
* **Positive Impact:** Fast verification runs, consolidated and simplified CI jobs, robust compilation verification on every commit.
* **Negative Impact / Technical Debt:** We accept a new dependency on `turbo` and must configure `turbo.json`.
* **Mitigation Strategy:** Keep `turbo.json` minimal and easy to maintain, standardizing task patterns across all packages.

## 6. Implementation & Verification
* **Affected Repositories / Services:** Root package workspace, `.github/workflows/ci.yml`, and `packages/ui`.
* **Verification Plan:** Verify by running `pnpm turbo run lint format test build` locally and ensure it successfully completes.
