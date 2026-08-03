# ADR 2026-07-23: Declarative Ruleset and Automerge Integration

## Status
Accepted (Amended August 2026)

## Context
Developers push code modifications directly to the main branch because the repository lacks active branch protection rules. This lets untested changes bypass critical validation and automated quality gates. We need a system to ensure all repository modifications pass automated validation checks before entering the main branch, while allowing safe, non-code updates (such as markdown documentation) to merge seamlessly and automatically.

Furthermore, a misalignment was identified in the declarative branch protection configuration. The `.github/rulesets/main.json` originally mandated a required status check named `"Test and Coverage"`. However, the actual continuous integration pipeline (configured in `.github/workflows/ci.yml`) executes and outputs a status check named `"Frontend & Backend Unit Tests"`. This mismatch blocked developers from merging valid, passing code into the `main` branch without requiring an administrative bypass, causing friction and disrupting the standard continuous integration flow.

## Decision
Store branch protection definitions in a declarative JSON configuration file (`.github/rulesets/main.json`), synchronize them to GitHub via a python script (`scripts/sync_ruleset.py`) on push to `main` branch, and integrate with the existing CI/CD classifier for programmatic approvals and automerging.

**Amended August 2026:**
Realigned the required status check definitions in `.github/rulesets/main.json` to match the actual pipeline check names. The obsolete `"Test and Coverage"` reference was replaced with the active `"Frontend & Backend Unit Tests"` check. The mandatory `"Style and Lint Verification"` check was preserved to maintain code style quality gates.

## Alternatives Considered
### Option 1: Classic manual GitHub branch protection setup
* **Overview:** Manually configure branch protection rules in the GitHub repository UI.
* **Pros:**
  * Simple and does not require coding.
* **Cons:**
  * Cannot version control branch rules inside the codebase.
  * Changes to protections are not subject to pull request review.

### Option 2: Declarative Repository Rulesets with Sync Script and Automerge Integration
* **Overview:** Chosen option. Store in `.github/rulesets/main.json`, sync via GitHub API, and integrate with the existing CI/CD classifier.
* **Pros:**
  * Rules are versioned, documented, and reviewed in Git.
  * Automated merge handles safe non-code files by programmatically approving and triggering auto-merge after CI passes.
  * Direct pushes to `main` are safely blocked.
* **Cons:**
  * Requires maintaining a python sync script using the GitHub CLI (`gh api`).

## Trade-offs
### Positive Impact
Greater GxP compliance, zero bypass of quality gates, and automated merging of safe docs. Resolving the status check mismatch eliminates merge blocking while preserving robust code-quality gates.

### Negative Impact / Technical Debt
Requires a `GH_TOKEN` or `GITHUB_TOKEN` with write-access to repository rulesets in GitHub Actions. Future modifications or renaming of continuous integration jobs must be kept in close sync with `.github/rulesets/main.json`.

This decision implements requirements under Trace-7.

