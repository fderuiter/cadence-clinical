# ADR-096: Parallel Continuous Integration Workflows and Local Concurrent Verification

* Status: Accepted
* Date: 2026-08-16
* Authors: @jules
* Deciders: @fderuiter

---

## 1. Context & Problem Statement
Our continuous integration (CI) and local validation workflows previously ran in a monolithic, sequential pipeline. This structure meant that fast, lightweight checks—like formatting, code style linting, security audits, and link validation—were blocked by high-overhead steps such as downloading and installing browser engines, database provisioning, and running end-to-end integration tests. Sequential execution increased developer feedback loops, wasted GitHub Actions runner minutes, and delayed pull request merges.

This decision implements requirements under PRD-QRY-001.

## 2. Decision Drivers & Constraints
* **Developer Feedback Loop:** Reduce local and remote validation cycles by parallelizing independent checks.
* **Resource Optimization:** Maximize CPU usage on local environments and CI runners by executing non-dependent steps concurrently.
* **Separation of Concerns:** Isolate lightweight linter and static analysis runs from heavy end-to-end and integration tests.

## 3. Options Considered
### Option 1: Retain Sequential Monolithic Execution
* **Overview:** Keep executing all checks in a single sequence of steps in one job.
* **Pros:**
  * ✅ Simplest design with no orchestration tool required.
* **Cons:**
  * ❌ Long wait times for linting results when integration steps fail first.
  * ❌ High GHA runner minute usage.

### Option 2: Full Job Parallelization in GHA and Concurrently Orchestration Locally
* **Overview:** Split the monolithic GHA job into distinct concurrent jobs (`style`, `security`, `unit-tests`, `integration-tests`, `compliance`) and configure local validation scripts with `concurrently` to run parallel checks in the terminal.
* **Pros:**
  * ✅ Dramatically reduced execution times (by over 50%).
  * ✅ Parallel checks are isolated, preventing single-domain failures from masking other issues.
  * ✅ Allows developers to get fast feedback locally with a single terminal command.
* **Cons:**
  * ❌ Requires installing `concurrently` as a devDependency in the root workspace.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Choosing Option 2 significantly improves feedback velocity for both local development and remote PR validation. Executing independent linters and tests concurrently utilizes system resources efficiently.

## 5. Consequences & Trade-offs
* **Positive Impact:** Developers receive linting and security feedback in under two minutes, while heavy test jobs run asynchronously in isolated runner environments.
* **Negative Impact / Technical Debt:** Added `concurrently` as a package dependency, increasing node modules footprint slightly.
* **Mitigation Strategy:** Pin `concurrently` version in `package.json` to ensure predictable behavior and run regular vulnerability checks.

## 6. Implementation & Verification
* **Affected Repositories / Services:** Root workspace files (`package.json`, `pnpm-lock.yaml`, `.github/workflows/ci.yml`).
* **Verification Plan:** Verify the performance improvement by running `pnpm run check` locally and monitoring GitHub Actions workflow run outputs.
