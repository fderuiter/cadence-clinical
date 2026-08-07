# ADR-2163: Decentralize Test Directory Structure Across Workspaces

* **Status:** Accepted
* **Date:** 2026-08-07
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Previously, all unit and integration test files for the Cadence Clinical platform resided in a single centralized `/tests` root directory. As microservices (`apps/`) and shared packages (`packages/`) expanded, housing all tests centrally created module coupling, obscured service-level ownership, and complicated isolated test execution per workspace.

To align with modern monorepo best practices and preserve microservice decoupling, test suites needed to be decentralized into their respective workspace directories (`apps/<service>/tests/`, `packages/<package>/tests/`, and `scripts/tests/`).

## 2. Decision Drivers & Constraints

* **Microservice Ownership & Boundary Isolation (PRD-SYS-001)**: Service-specific and package-specific unit tests must reside within their respective workspace boundaries.
* **Dynamic Pytest Discovery**: Pytest configuration (`pyproject.toml`) must dynamically discover and execute tests across all workspace paths (`apps`, `packages`, `scripts`, `tests`).
* **GxP Compliance & RTM Traceability**: Requirement annotations (`@req:`) across decentralized tests must continue to be scanned and traced by `scripts/generate_rtm.py` and `scripts/sync_gxp.py`.

## 3. Options Considered

1. **Decentralized Workspace Tests (Selected)**: Move test modules into `apps/<service>/tests/`, `packages/<package>/tests/`, and `scripts/tests/`, while preserving shared test fixtures and qualification suites in `/tests/`.
2. **Centralized Root Monolith Tests (Status Quo)**: Retain all `test_*.py` files in root `/tests/`.

## 4. Decision Outcome

Chosen option: **Option 1 (Decentralized Workspace Tests)**.
All microservice tests, package tests, and tooling tests have been migrated into their respective workspace directories. `pyproject.toml` updated `testpaths = ["apps", "packages", "scripts", "tests"]`, and root `conftest.py` provides workspace-wide fixture initialization.

## 5. Consequences & Trade-offs

* **Positive**: Clear service boundary isolation, improved developer experience, modular test execution per workspace.
* **Positive**: Fully automated GxP RTM traceability preserved across all workspace test paths.
* **Trade-off**: Requires updated relative path calculations in scripts that reference test paths.

## 6. Implementation & Verification

* **Migrated Directories**: `apps/<name>/tests/`, `packages/<name>/tests/`, `scripts/tests/`.
* **Configuration Updated**: `pyproject.toml`, `conftest.py`, `scripts/generate_rtm.py`, `scripts/sync_gxp.py`, `.github/workflows/ci.yml`, `AGENTS.md`.
* **Verification**: `uv run python scripts/sync_gxp.py` passes 100% of test suites and generates updated GxP compliance artifacts.
