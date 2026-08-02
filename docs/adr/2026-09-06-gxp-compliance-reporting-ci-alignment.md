# ADR-252: GxP Compliance Reporting CI Alignment and Local Dependency Configuration

* **Status:** Accepted
* **Date:** 2026-09-06
* **Authors:** @jules
* **Deciders:** @engineering_leads, @qa_lead

---

## 1. Context & Problem Statement

The platform's continuous integration (CI) pipeline enforces automated GxP compliance, verification tracing, and layout/accessibility audits on all pull requests. During local and CI execution, the GxP qualification suite requires a fully-functional local environment to run WCAG contrast validation and layout assertions. 

Prior to this decision, the local development and test execution environments lacked the necessary frontend node dependencies (specifically `axe-core`) required by the layout validator to perform accessibility audits. This caused silent layout test validation failures or layout verification suite exclusions. In addition, Python import sorting, unused variables, and type compatibility definitions within newly introduced eCOA/ePRO gateway routing logic had minor inconsistencies that triggered linting failures under Ruff and ESLint gating checks.

This decision addresses requirements under **PRD-SYS-001** and **PRD-SYS-003**.

## 2. Decision Drivers & Constraints

* **Driver 1:** 100% GxP validation coverage across both local and CI environments.
* **Driver 2:** Clean, compliant, and warning-free lint checks across all workspace projects.
* **Driver 3:** Developer ergonomics and deterministic build outcomes.
* **Constraint:** Must not introduce heavy runtime external dependencies or change core GxP requirements.

## 3. Options Considered

### Option 1: Ad-hoc manual configuration and partial lint suppression
* **Overview:** Manually download the `axe.min.js` file and suppress lint warnings in gateway routers using inline `# noqa` comments.
* **Pros:**
  * ✅ Minimizes local git changes.
* **Cons:**
  * ❌ Pragmatically fragile and prone to future breakage in clean environments.
  * ❌ Unused variables and raw imports remain as technical debt.

### Option 2: Full local dependency integration, lint resolution, and automated synchronization
* **Overview:** Formally include `axe-core` as part of the workspace devDependencies, completely resolve ESLint unused variable rules in `subject-portal` and Ruff formatting/import sorting rules in `apps/gateway/`, and run automated GxP SDLC report synchronization.
* **Pros:**
  * ✅ Clean, deterministic, and warning-free gate verification.
  * ✅ Automatic synchronization of SDP reports through sync scripting.
  * ✅ Eliminates silent failures in accessibility checking.
* **Cons:**
  * ❌ Requires checking in updated GxP trace documents.

## 4. Decision Outcome

* **Chosen Option:** Option 2
* **Justification:** Choosing Option 2 fully aligns with GxP verification standards by eliminating build-time warnings and ensuring the local layout engine has full access to the required accessibility library.

## 5. Consequences & Trade-offs

* **Positive Impact:** Builds are robust, repeatable, and 100% compliant with our automated linting gates and trace verification.
* **Negative Impact / Technical Debt:** Future gateway modifications will need to adhere strictly to Ruff import sorting rules.

## 6. Implementation & Verification

* **Affected Repositories / Services:**
  - `/app/apps/subject-portal/` (ESLint unused imports resolved)
  - `/app/apps/gateway/` (Ruff import sorting and formatting applied)
  - `/app/packages/core-models/` (StrEnum conversion for Python 3.12 compatibility)
  - `/app/docs/SDLC/` (Synchronized GxP Traceability Matrix and IQ/OQ/PQ Reports)
* **Verification Plan:**
  - Execute `pnpm check` locally and in CI/CD pipeline.
  - Run the full test suite to guarantee 100% operational pass rate.
