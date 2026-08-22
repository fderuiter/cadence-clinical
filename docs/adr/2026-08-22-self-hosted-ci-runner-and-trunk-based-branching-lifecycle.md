# ADR-2198: Self-Hosted CI Runner and Trunk-Based Branching Lifecycle

- Status: Accepted
- Date: 2026-08-22
- Authors: @fderuiter
- Deciders: @fderuiter

---

## 1. Context & Problem Statement

As Cadence Clinical progresses through its pre-production engineering lifecycle, maintaining dual integration branches (`dev` and `main`) introduced synchronization drift, duplicate CI execution overhead, and ambiguous pull request targets. Furthermore, running comprehensive multi-engine integration test suites (PostgreSQL, Neo4j, Redis, ClamAV) on standard public cloud runners incurred significant startup latency and consumable compute costs.

Transitioning the repository to private visibility enables the safe deployment of a dedicated, high-performance local macOS CI/CD runner (Apple Silicon Mac Mini), while establishing a future-proof, linear Trunk-Based Development workflow.

This decision maps to system reliability and operational requirements under `PRD-SYS-001`.

## 2. Decision Drivers & Constraints

- **Security & Sandboxing:** GitHub strictly discourages self-hosted runners on public repositories due to untrusted pull request execution risks. Private repository isolation is mandatory before deploying a local machine as a runner.
- **Trunk Cleanliness & Linear Auditability:** GxP clinical software traceability (21 CFR Part 11) requires a clean, bisectable, and immutable commit history on the primary branch.
- **Resource Optimization & Runner Throughput:** As a single physical host, the Mac Mini runner must maximize test throughput, eliminate container boot latency, and avoid pipeline queue congestion caused by rapid iterative commits.
- **Process Resilience & Hands-Off Maintenance:** The runner must operate headless as a system daemon that survives macOS reboots without manual terminal management.

## 3. Options Considered

### Option 1: Dual-Trunk GitFlow (`dev` + `main`) with Ephemeral Cloud Runners

- **Overview:** Maintain `dev` as the daily integration branch and `main` as the release mirror, running builds on cloud-hosted runners (`ubuntu-latest`).
- **Pros:**
  - Standard GitHub-managed cloud infrastructure.
- **Cons:**
  - ❌ Pre-production branch drift between `dev` and `main`.
  - ❌ 30-60s cold-start overhead per job provisioning dependencies.
  - ❌ Higher cloud resource consumption and rate limits.

### Option 2: Private Repository + Trunk-Based Development (`main`) + Dedicated macOS `launchd` Self-Hosted Runner (Selected)

- **Overview:** Standardize on Trunk-Based Development with `main` as the sole protected trunk. Deploy a dedicated Mac Mini runner using `launchd` background services, host-native toolpaths (`uv`, `pnpm`), persistent test service daemons, and PR-scoped `cancel-in-progress` triggers.
- **Pros:**
  - ✅ **Zero Branch Drift:** All feature branches (`feat/*`, `fix/*`) branch from and squash-merge into `main`.
  - ✅ **Instant CI Startup:** Host-native `uv` and `pnpm` execution eliminates runner setup overhead.
  - ✅ **High-Throughput Isolation:** Cadence's existing worker-suffix database isolation (`_{run_uid}_{worker_id}`) runs against persistent local daemons without container restarts.
  - ✅ **Queue Protection:** `cancel-in-progress` on pull requests terminates obsolete runs immediately upon new pushes.
- **Cons:**
  - ❌ Requires initial local runner installation and Homebrew/Docker daemon management.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 provides sub-second CI startup velocity, eliminates branch management friction in pre-production, guarantees complete security via private repository boundaries, and produces an immaculate, linear commit history on `main`.

## 5. Consequences & Trade-offs

- **Positive:**
  - Immediate feedback cycles on pull requests (<2 minutes full verification).
  - Eliminates duplicate merge overhead between `dev` and `main`.
  - Secure execution environment gated behind private GitHub repository permissions.
  - Clean, bisectable linear git history on `main`.
- **Negative:**
  - Requires maintaining the Mac Mini host environment (e.g. keeping Homebrew packages, Docker daemons, and Node/Python runtimes up to date).
- **Mitigation:**
  - Use `launchd` service management for automated daemon recovery.
  - Implement workspace artifact sweeps (`git clean -ffdx -e .venv -e node_modules`) before each job run.

## 6. Implementation & Verification

- **Branch Topology:**
  - Retire `dev` branch after merging active features into `main`.
  - Set `main` as default branch on GitHub.
  - Enforce branch protection: require PR, require CI status checks, require squash-and-merge with linear history.
- **Runner Provisioning:**
  - Configure GitHub Runner on macOS under `launchd` background service (`svc.sh install` and `svc.sh start`).
  - Label runner: `self-hosted, macOS`.
- **Workflow Pipeline:**
  - Update `.github/workflows/ci.yml` jobs to specify `runs-on: [self-hosted, macOS]`.
  - Configure PR concurrency: `concurrency.cancel-in-progress: true`.
- **Verification Plan:**
  - Validate ADR compliance via `uv run python scripts/validate_adrs.py`.
  - Test runner connectivity by triggering a test workflow on a feature branch PR.
