# ADR-121: Resolve Vitest Peer Dependency Conflict in Shared UI Package

* **Status:** Accepted
* **Date:** 2026-07-31
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The shared UI package (`packages/ui`) lacked explicit devDependencies for the Vitest test runner. When executing workspace-wide tests with `pnpm -r test`, the package manager fell back to global or unpinned versions, resolving `vitest` to version `4.1.10` alongside `vite` version `5.4.21`. Due to a subpath export discrepancy in Vite 5, this mismatch triggered a fatal startup error: `Error [ERR_PACKAGE_PATH_NOT_EXPORTED]: Package subpath for module-runner is not defined by "exports"`. This peer dependency conflict prevented execution of frontend unit tests in the CI pipeline. To resolve this, we must align and pin both `vite` and `vitest` versions across all workspace packages, in accordance with the system validation requirements (PRD-SYS-001).

## 2. Decision Drivers & Constraints

* Ensure consistent development and CI/CD environments across the entire monorepo.
* Comply with the performance budget (running tests in under 5 seconds) and standard SDLC testing procedures.
* Ensure GxP system verification traces are preserved (PRD-SYS-001).

## 3. Options Considered

1. **Option A (Selected):** Add explicit devDependencies for `vite` and `vitest` with matching pinned versions (`^8.1.5` and `^4.1.10` respectively) directly to `packages/ui/package.json`.
2. **Option B (Alternative):** Configure peer dependency overrides or hoisting rules in the root `package.json`. This was rejected because explicit dependency definition in local package configurations provides cleaner package isolation and avoids unexpected package hoisting behaviors.

## 4. Decision Outcome

Chosen option: Option A because it directly resolves the package runner's dependency path mismatch, ensuring `vitest` is invoked with the expected compatible `vite` version, satisfying PRD-SYS-001 and restoring local and CI test execution.

## 5. Consequences & Trade-offs

* Positive: Restores clean, error-free execution of the frontend unit test suite locally and in GHA.
* Positive: Avoids hoisting or peer dependency conflicts.
* Negative: Requires maintaining identical version specifications across multiple package configurations within the workspace.

## 6. Implementation & Verification

* Target files/packages modified: `packages/ui/package.json`.
* Verification: Ran `pnpm install` and executed workspace tests via `pnpm -r test` successfully with all tests passing.
