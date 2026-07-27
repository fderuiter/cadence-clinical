# ADR-85: Standardizing Workspace Boundaries and Cryptographic Verification

* **Status:** Accepted
* **Date:** 2026-08-11
* **Authors:** @jules
* **Deciders:** @engineering-lead, @security-lead

---

## 1. Context & Problem Statement
The lack of automated architectural boundary checks allowed potential direct dependencies between isolated applications and domain logic packages. Additionally, duplicate cryptographic signature verification logic was written across different packages, which increases compliance risk and makes updates to security protocols difficult to roll out consistently.

## 2. Decision Drivers & Constraints
* **Driver 1:** Enforce strict architectural boundary checks in local development and continuous integration environments.
* **Driver 2:** Centralize cryptographic electronic signature verification under a single canonical utility path to eliminate code drift.
* **Driver 3:** Maintain low runtime and build validation overhead (< 5 seconds guardrail during commits).

## 3. Options Considered
### Option 1: Manual review of boundaries and separate package-specific signature verification logic
* **Overview:** Rely on manual code reviews to block forbidden imports and maintain separate X.509 signature validation logic per module.
* **Pros:**
  * ✅ No initial configuration complexity.
* **Cons:**
  * ❌ High probability of regression and compliance gaps due to manual slips.

### Option 2: Centralized verification and automated boundary checks (Selected)
* **Overview:**
  1. Route all electronic X.509 signature verification methods to `asymmetric_verify` in `packages.security.signing`.
  2. Implement strict ESLint `no-restricted-imports` configuration for JavaScript and Vue.
  3. Enforce Ruff `TID251` (flake8-tidy-imports) banned API check specifically on Python domain libraries under `packages/` to restrict imports of application-level files.
  4. Automatically run checks during the pre-commit flow.
* **Pros:**
  * ✅ Eliminates code drift and guarantees a single secure pipeline for GxP operations.
  * ✅ Automated boundary enforcement prevents any cross-boundary package-to-app imports.
  * ✅ Extremely fast (< 1.5s) local verification checks.
* **Cons:**
  * ❌ None.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 strictly satisfies all requirements and acceptance criteria under our platform Mission, guaranteeing 21 CFR Part 11 auditing and GxP compliance boundaries.

## 5. Consequences & Trade-offs
* **Positive Impact:** Automatic detection of boundary violations at compile/commit-time; robust verification consistency across eTMF, execution, and portal services.
* **Negative Impact / Technical Debt:** Requires keeping import patterns updated in case of workspace expansions.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/etmf`, `packages/security`, root config files, and pre-commit workflow.
* **Verification Plan:** Verified via `pnpm lint`, `uv run ruff check .`, `uv run pytest`, and automated pre-commit runs.
