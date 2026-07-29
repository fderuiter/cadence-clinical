# Cadence Clinical Platform Contribution Guidelines

Welcome to the Cadence Clinical Platform contributor repository! This document outlines the workflows, coding standards, and quality verification gates required of all human developers wishing to contribute.

To ensure the GxP-compliance and high-integrity nature of this eClinical monorepo, please adhere strictly to these guidelines. Refer to the **[Master Documentation Index](docs/DOCUMENTATION_INDEX.md)** for a complete sitemap of system specifications, architecture blueprints, ADRs, and validation ledgers.

---

## 1. Branching & Workflow Strategy

All development is structured around a trunk-based/main branch development model with short-lived feature/bugfix branches.

### Branch Naming Conventions
When starting a new issue or feature, create a branch from the up-to-date `main` branch. Use the following naming schemes:
* **Feature Branches:** `feature/<ticket-number>-<brief-hyphenated-description>` (e.g., `feature/cad-104-enrollment-validation`)
* **Bug Fixes:** `bugfix/<ticket-number>-<brief-hyphenated-description>` (e.g., `bugfix/cad-215-signature-overflow`)
* **Documentation/Chore:** `docs/<brief-hyphenated-description>` or `chore/<brief-hyphenated-description>`

### Workflow Lifecycle
1. **Sync with Upstream:** Before creating your branch or writing code, pull the latest changes from `main`:
   ```bash
   git checkout main
   git pull origin main
   ```
2. **Create Feature Branch:** Create and checkout your feature branch:
   ```bash
   git checkout -b feature/cad-XXX-my-feature
   ```
3. **Local Setup & Development:** Develop locally on your host environment or inside the containerized sandbox (see [Local Development Environment Guide](docs/LOCAL_DEV_ENVIRONMENT.md)).
4. **Local Verification:** Run all formatting, linting, secret audits, link checks, and ADR validations before making a pull request:
   ```bash
   pnpm check      # Fast pre-flight quality gates (formatting, linting, secrets, ADRs, links)
   pnpm verify     # Full verification (pnpm check + unit & integration test suites)
   ```
5. **Git Commit & Push:** Commit your staged changes with a descriptive commit message and push to GitHub:
   ```bash
   git add .
   git commit -m "feat(execution): implement GxP write-protection trigger for enrollment"
   git push origin feature/cad-XXX-my-feature
   ```
6. **Open a Pull Request:** Open a PR against the `main` branch. Ensure the PR description details what changed and references any related issue or architectural decision record.

---

## 2. Issue Lifecycle, Parallel Work Streams & GitHub Project Automation

To maintain parallel developer efficiency across microservices and eClinical modules, all issues follow the standardized framework detailed in **[Issue Structure, Work Streams & Project Board Guide](docs/SDLC/ISSUE_STRUCTURE_GUIDE.md)**:

### Filing a New Issue
1. **Use Issue Templates**: Choose from `.github/ISSUE_TEMPLATE/actionable_leaf_task.yml` or `epic_parent.yml`.
2. **Assign Work Stream**: Select one of the 8 parallel work streams (`Stream 1: eTMF`, `Stream 2: RTSM`, `Stream 3: Study Designer/eCRF`, `Stream 4: eCOA/ePRO`, `Stream 5: Frontend Vue 3 SPA`, `Stream 6: Security & RBAC`, `Stream 7: Biostatistics`, `Stream 8: Clinical Ops`).
3. **Specify Target Files**: List exact module paths (`apps/...`, `packages/...`).
4. **Automated Workflow**: Upon filing, `.github/workflows/project-automation.yml` will automatically:
   * Format the issue header with readiness badges (🟢 `READY FOR DEV`, 🔴 `BLOCKED`, 🔵 `PARENT EPIC`).
   * Import the issue to **GitHub Project Board 17 (`Cadence-Clinical`)**.
   * Calculate module `Size` (`XS` to `XL`) and set `Priority` (`P0`, `P1`, `P2`).
   * Route the issue to `Ready` or `Backlog`.

---

## 3. Issue-to-Documentation Synchronization Workflow

When addressing open GitHub issues or modifying system functionality, follow the 3-tier documentation cascade to keep specifications and tests in sync:

1. **Requirement Level (`PRD` / `SRS`)**:
   - Verify if your issue changes user capabilities or platform specs. Update `docs/SDLC/01_Product_Requirements_Document_PRD.md` or `docs/SRS.md` and reference a Requirement ID (`PRD-SYS-xxx` or `Trace-x`).
2. **Architecture & Decision Level (`ADR`)**:
   - If introducing architectural changes, scaffold a domain-indexed ADR using the developer CLI tool:
     ```bash
     python3 scripts/create_adr.py --title "Your Feature Title" --domain "core-platform" --req "PRD-SYS-001"
     ```
   - This automatically creates the formatted ADR file in `docs/adr/` and inserts it under the chosen domain in `docs/adr/index.md`.
3. **Traceability Level (`RTM`)**:
   - Reference requirement IDs in unit/integration test docstrings or test names.
   - Run `node scripts/build-docs.js` locally to verify links, rebuild VitePress, and update the Requirements Traceability Matrix.

---

## 3. Human vs. AI Agent Boundary & Rules

This repository is designed to be co-authored by human developers and autonomous AI agents. To prevent operational confusion, the guidelines and automated checks distinguish between human contribution pipelines and AI execution parameters:

* **Human Developers:** Follow the interactive, cross-platform instructions outlined in this document (`CONTRIBUTING.md`) and the [Local Development Environment Guide](docs/LOCAL_DEV_ENVIRONMENT.md). Humans should leverage their interactive shell environments, run manual package resolution commands natively on the host (using Git, Node, pnpm, Python, and uv), and run local checks in parallel.
* **Autonomous AI Agents:** Must strictly comply with `AGENTS.md`. Agents have specific directory target constraints, automated file structure validation requirements, strict Pydantic v2 validation constraints, and must format code only with ruff / black to maintain syntactic uniformity.

---

## 4. Mandatory Quality Gates & Verification Pipelines

All contributions must pass through three rigorous quality verification gates before merging into the `main` branch.

### Gate 1: Comprehensive Documentation & Docstrings
Every module, class, function, and public API endpoint must be thoroughly documented:
* **Python Codebases (`apps/`, `packages/`):** All functions and classes must include clear docstrings following Google or NumPy style guidelines. Any complex business logic must be accompanied by explanatory comments.
* **Workspace Documentation (`docs/`):** Update SRS, architecture diagrams, or lab range configurations if a PR modifies active data flows or introduces new service boundaries.

### Gate 2: Architecture Decision Records (ADRs)
We enforce a strict **"Code + Context"** policy. If your PR introduces significant architectural drift, you must document it with an Architecture Decision Record (ADR):
* **When required:** Introducing a new library/database, modifying inter-service REST contracts, adding a new service, or altering global database schemas.
* **Scaffolding Tool:** `python3 scripts/create_adr.py --title "..." --domain "..." --req "PRD-..."`
* ADRs are automatically indexed into `docs/adr/index.md` by functional domain and validated on pre-commit and push gates.

### Gate 3: Mandatory Test Coverage & Verification Passes
No code is merged untested.
* All unit and integration tests must reside inside the `tests/` directory.
* Backend tests must run successfully under `pytest` with `pytest-asyncio`.
* Total Python coverage must reach a minimum of **80%** (enforced by `pytest-cov` in `pnpm check`).
* Frontend and packages must run successfully under `vitest`.

---

## 5. Local Git Hook Configurations (`pre-commit`)

We use `pre-commit` to catch code formatting, security hazards, and relative link errors before they are committed to Git.

### Setting Up Git Hooks Natively
Ensure you have the Python host environment properly configured, and run:
```bash
pre-commit install
```
This binds git hooks to run formatting check triggers (`prettier`, `ruff`), security scanners (`bandit`, `detect-secrets`), and internal documentation checks on `git commit` and `git push`.

If you need to run pre-commit checks manually on all files without committing, run:
```bash
uv run pre-commit run --all-files
```
*Note: All hooks are registered across `pre-commit`, `pre-push`, and `manual` stages so manual runs succeed cleanly without missing-stage errors.*

### Synchronizing GxP Compliance Reports
When adding new requirements or updating tests, regenerate the GxP Requirements Traceability Matrix locally before submitting a PR:
```bash
pnpm rtm
# Or directly: python3 scripts/generate_rtm.py --validate
```

### Developer & AI Agent Pain Point Prevention
To maintain developer velocity and prevent recurring workspace errors:
- **Binary File Hygiene (`.docx`)**: Never commit binary `.docx` files to git. Use `python3 scripts/regenerate_templates.py` to rebuild protocol templates dynamically.
- **Script Backward Compatibility**: Maintain parameter defaults when modifying script signatures (e.g. `scripts/post_pr_comment.py`) so existing unit tests do not break.
- **CI Permission Drift**: GitHub API scripts output non-blocking warnings on HTTP 403 unless `FAIL_ON_RULESET_SYNC_ERROR="true"` is explicitly set.

For a comprehensive system setup, port allocation map, and service directory, refer to the [Local Development Environment Guide](docs/LOCAL_DEV_ENVIRONMENT.md).
