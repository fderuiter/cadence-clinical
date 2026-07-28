# Cadence Clinical Platform Contribution Guidelines

Welcome to the Cadence Clinical Platform contributor repository! This document outlines the workflows, coding standards, and quality verification gates required of all human developers wishing to contribute.

To ensure the GxP-compliance and high-integrity nature of this eClinical monorepo, please adhere strictly to these guidelines.

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
4. **Local Verification:** Run all formatting, linting, tests, link checks, and ADR validations before making a pull request:
   ```bash
   pnpm check
   ```
5. **Git Commit & Push:** Commit your staged changes with a descriptive commit message and push to GitHub:
   ```bash
   git add .
   git commit -m "feat(execution): implement GxP write-protection trigger for enrollment"
   git push origin feature/cad-XXX-my-feature
   ```
6. **Open a Pull Request:** Open a PR against the `main` branch. Ensure the PR description details what changed and references any related issue or architectural decision record.

---

## 2. Human vs. AI Agent Boundary & Rules

This repository is designed to be co-authored by human developers and autonomous AI agents. To prevent operational confusion, the guidelines and automated checks distinguish between human contribution pipelines and AI execution parameters:

* **Human Developers:** Follow the interactive, cross-platform instructions outlined in this document (`CONTRIBUTING.md`) and the [Local Development Environment Guide](docs/LOCAL_DEV_ENVIRONMENT.md). Humans should leverage their interactive shell environments, run manual package resolution commands natively on the host (using Git, Node, pnpm, Python, and uv), and run local checks in parallel.
* **Autonomous AI Agents:** Must strictly comply with `/app/AGENTS.md`. Agents have specific directory target constraints, automated file structure validation requirements, strict Pydantic v2 validation constraints, and must format code only with ruff / black to maintain syntactic uniformity.

---

## 3. Mandatory Quality Gates & Verification Pipelines

All contributions must pass through three rigorous quality verification gates before merging into the `main` branch.

### Gate 1: Comprehensive Documentation & Docstrings
Every module, class, function, and public API endpoint must be thoroughly documented:
* **Python Codebases (`apps/`, `packages/`):** All functions and classes must include clear docstrings following Google or NumPy style guidelines. Any complex business logic must be accompanied by explanatory comments.
* **Workspace Documentation (`docs/`):** Update SRS, architecture diagrams, or lab range configurations if a PR modifies active data flows or introduces new service boundaries.

### Gate 2: Architecture Decision Records (ADRs)
We enforce a strict **"Code + Context"** policy. If your PR introduces significant architectural drift, you must document it with an Architecture Decision Record (ADR):
* **When required:** Introducing a new library/database, modifying inter-service REST contracts, adding a new service, or altering global database schemas.
* **Format:** Create a new markdown document inside `docs/adr/` using the format `YYYY-MM-DD-short-title.md` (e.g., `docs/adr/2026-07-27-dynamic-ecrf-templates.md`).
* ADRs must follow the template structure provided in `docs/adr/` and are validated on pre-commit and push gates.

### Gate 3: Mandatory Test Coverage & Verification Passes
No code is merged untested.
* All unit and integration tests must reside inside the `tests/` directory.
* Backend tests must run successfully under `pytest` with `pytest-asyncio`.
* Total Python coverage must reach a minimum of **80%** (enforced by `pytest-cov` in `pnpm check`).
* Frontend and packages must run successfully under `vitest`.

---

## 4. Local Git Hook Configurations (`pre-commit`)

We use `pre-commit` to catch code formatting, security hazards, and relative link errors before they are committed to Git.

### Setting Up Git Hooks Natively
Ensure you have the Python host environment properly configured, and run:
```bash
pre-commit install
```
This binds git hooks to run formatting check triggers (`prettier`, `ruff`), security scanners (`bandit`, `detect-secrets`), and internal documentation checks on `git commit`.

If you need to run pre-commit checks manually on all files without committing, run:
```bash
uv run pre-commit run --all-files
```

For a comprehensive system setup, port allocation map, and service directory, refer to the [Local Development Environment Guide](docs/LOCAL_DEV_ENVIRONMENT.md).
