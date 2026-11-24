# ADR-2282: Declarative Ruleset and Automerge Integration

## Status

Accepted (Amended August 2026, November 2026)

## Context

Developers push code modifications directly to the main branch because the repository lacks active branch protection rules. This lets untested changes bypass critical validation and automated quality gates. We need a system to ensure all repository modifications pass automated validation checks before entering the main branch, while allowing safe, non-code updates (such as markdown documentation) to merge seamlessly and automatically.

Furthermore, a misalignment was identified in the declarative branch protection configuration. The `.github/rulesets/main.json` originally mandated a required status check named `"Test and Coverage"`. However, the actual continuous integration pipeline (configured in `.github/workflows/ci.yml`) executed and output different job check names, and a later decomposition into parallel validation jobs (`actionlint`, `lint-and-static`, `dotnet-quality`, `native-aot-build`, `docs-build`, `security`, `container-security`, `unit-tests`, `dotnet-tests`, and `compliance`) retired both `"Frontend & Backend Unit Tests"` and `"Style and Lint Verification"` while retiring Python operational scripts (`scripts/sync_ruleset.py`). That drift left `.github/rulesets/main.json` and `.github/rulesets/release_branches.json` requiring nonexistent checks with `strict_required_status_checks_policy: false`, deadlocking regular pull request merges and `gh pr merge --auto --squash`.

## Decision

Store declarative repository ruleset definitions in `.github/rulesets/` (`main.json`, `release_branches.json`, and `version_tags.json`), enforce parity continuously with a `rulesets` quality gate in `packages/cli`, reconcile rulesets via the Native AOT C# CLI command `pnpm cadence rulesets`, and gate branch merges and maintainer-attributed native GitHub auto-merges on one aggregated `CI Gate` status check plus deterministic `Check for Merge Conflicts`.

**Amended August 2026:**
Realigned the required status check definitions in `.github/rulesets/main.json` from `"Test and Coverage"` to `"Frontend & Backend Unit Tests"` and `"Style and Lint Verification"`.

**Amended November 2026:**

1. **Aggregated `CI Gate` (`ci-gate`):** Added an `if: always()` job named `CI Gate` in `.github/workflows/ci.yml` that depends on all pull-request validation jobs (`actionlint`, `lint-and-static`, `dotnet-quality`, `native-aot-build`, `docs-build`, `security`, `container-security`, `unit-tests`, `dotnet-tests`, `accessibility-e2e`, and `compliance`). `CI Gate` fails whenever any required validation job fails, cancels, or skips (including conditional downstream jobs such as `compliance`), while allowing `container-security` to skip on pull requests that touch no container or dependency inputs. Push-only jobs (`release-and-archive`, `deploy-docs`) and the pull-request classification job (`classify`) stay excluded from required branch status checks.
2. **Strict branch rulesets and deterministic conflict check:** Updated `.github/rulesets/main.json` (`refs/heads/main`, including a `SQUASH` / `ALLGREEN` `merge_queue` rule) and `.github/rulesets/release_branches.json` (`refs/heads/release/*`) to restrict repository-admin bypasses (`RepositoryRole` `5`) to `bypass_mode: "pull_request"`, set `strict_required_status_checks_policy: true`, and require only `CI Gate` and `Check for Merge Conflicts` (`.github/workflows/conflict-check.yml`), each pinned to `integration_id: 15368` (GitHub Actions), while preserving `required_approving_review_count: 1`, `require_code_owner_review: true`, and immutable `v*` tag protections in `.github/rulesets/version_tags.json`. `.github/workflows/conflict-check.yml` runs on `opened`, `synchronize`, `reopened`, and `edited` as well as `merge_group` with `contents: read` only, fetching the base ref and `refs/pull/<number>/head` from `$GITHUB_REPOSITORY` (or validating `HEAD` on `merge_group`) and failing closed on non-zero `git merge-tree --write-tree` exits without checking out forks or polling `gh pr view`.
3. **Fork-safe classification and native GitHub auto-merge:** Retired the `GITHUB_TOKEN`-backed `automerge` job (`Enable Automerge`) in `.github/workflows/ci.yml` because merges triggered by `GITHUB_TOKEN` suppress downstream `push` workflow runs (`deploy-docs` and `release-and-archive`). Instead, `classify` labels safe prose pull requests (`safe-change`) only when `github.event.pull_request.head.repo.full_name == github.repository` (avoiding fork `HTTP 403` errors), and maintainers enable native GitHub auto-merge under their own user account so merges to `main` reliably trigger `push` deployments once `CI Gate`, `Check for Merge Conflicts`, and review requirements pass.
4. **Native AOT C# sentinels and reconciliation (`cadence rulesets` and `workflow-security`):** Replaced the retired `scripts/sync_ruleset.py` script with `pnpm cadence check rulesets` (`QualityGates.cs`, verifying JSON schema, strict policy, `integration_id: 15368`, `CI Gate` job coverage, `edited` and `git merge-tree --write-tree` conflict-check parity, and the absence of `GITHUB_TOKEN` workflow merges) and `pnpm cadence rulesets` (`--dry-run` preview and fail-closed `gh api` reconciliation that exits non-zero on API or permission errors), paired with `pnpm cadence check workflow-security` to block fork checkouts, unguarded PR write scopes, and masked shell errors.

## Alternatives Considered

### Option 1: Classic manual GitHub branch protection setup

- **Overview:** Manually configure branch protection rules in the GitHub repository UI.
- _**Pros:**_
  - Simple and does not require coding.
- _**Cons:**_
  - Cannot version control branch rules inside the codebase.
  - Changes to protections are not subject to pull request review.

### Option 2: Require every individual CI job directly in branch rulesets

- **Overview:** List every decomposed job name in `.github/rulesets/main.json` and `.github/rulesets/release_branches.json`.
- _**Pros:**_
  - Requires no aggregator job in `ci.yml`.
- _**Cons:**_
  - Splitting, renaming, or conditionally skipping a CI job (such as `container-security`) immediately deadlocks merges and requires repository-admin API updates.

### Option 3: Aggregated `CI Gate`, Declarative Rulesets, and Native AOT CLI Reconciliation

- **Overview:** Chosen option. Store rulesets in `.github/rulesets/*.json`, gate branches on `CI Gate` and `Check for Merge Conflicts` (`integration_id: 15368`, `strict_required_status_checks_policy: true`), enforce parity in `pnpm cadence check rulesets`, and reconcile via `pnpm cadence rulesets`.
- _**Pros:**_
  - Rules are versioned, documented, and verified against workflow definitions in pre-commit and CI.
  - Internal CI job refactors update `ci-gate.needs` without mutating GitHub Rulesets or deadlocking `gh pr merge --auto`.
  - Maintainers can preview (`--dry-run`) and reconcile rulesets locally while hosted GitHub Actions runners are paused, with zero Python tooling.
- _**Cons:**_
  - Adds a lightweight `ubuntu-latest` aggregator job (`CI Gate`) at the end of pull-request validation runs.

## Trade-offs

### Positive Impact

Restores active GxP merge gates and up-to-date branch enforcement across `main` and `release/*`, prevents spoofed status contexts by pinning `integration_id: 15368`, ensures user-attributed native GitHub auto-merges trigger `deploy-docs` and `release-and-archive` on `push`, unblocks fork contributors from label `HTTP 403` failures and GraphQL `UNKNOWN` false passes, and catches workflow-to-ruleset drift locally in `pnpm cadence check`.

### Negative Impact / Technical Debt

Applying ruleset updates with `pnpm cadence rulesets` requires a maintainer `gh` session with `Administration: write` permission on the repository; permission or API failures intentionally exit non-zero rather than silently skipping reconciliation.

This decision implements requirements under Trace-7.
