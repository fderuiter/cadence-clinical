# ADR-2178: Standardized Hexagonal Architecture and Unified Developer Experience Suite

- **Status:** Accepted
- **Date:** 2026-08-14
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

As the Cadence Clinical Research Software Platform scaled across 15+ microservices and diverse client tiers (web authoring workspace, subject mobile portal), architectural consistency and developer velocity faced key challenges:

1. Microservice entry points contained varying patterns for data access and route organization.
2. Local development required orchestrating numerous distributed background processes without unified observability or instant interactive controls.
3. Test execution and fixture provisioning required standardizing domain entity generators and fast in-memory repository fakes to achieve sub-second developer feedback loops.

System Requirement: **PRD-SYS-001** (Unified Clinical Architecture & System Maintainability).

---

## 2. Decision Drivers & Constraints

- **GxP 21 CFR Part 11 Compliance:** Microservice boundaries, signature verification, and immutable audit trailing must remain strictly preserved.
- **Developer Experience (DX):** Local developer workflows must provide instant feedback via a single CLI cockpit (`cadence dev --tui`, `cadence test --watch`, `cadence doctor --auto-fix`, `cadence fix --all`).
- **Zero Breaking Changes:** External HTTP API contracts and OpenAPI schemas must remain 100% backward compatible.
- **Static Verification & Sentinels:** Quality gates must statically verify hexagonal port contracts and prevent schema/import drift.

---

## 3. Options Considered

1. **Option A (Selected): Unified Interactive DX Suite & Centralized Test Infrastructure:**
   - Enhance the Cadence CLI with an interactive Rich TUI cockpit, live log filtering, and hot-restart capabilities.
   - Implement an intelligent file-system test watcher with sub-second feedback.
   - Establish `packages/testing` as the centralized testing toolkit providing domain entity factories, in-memory repository fakes, and mock security contexts.
   - Enforce progressive hexagonal decomposition across microservice entrypoints.

2. **Option B: Headless CLI & Ad-hoc Service Fixtures:**
   - Retain disparate subprocess commands and decentralized fixtures without a unified TUI or shared test package.

---

## 4. Decision Outcome

Chosen option: **Option A**.

This decision establishes:

1. **Interactive Multi-Service Cockpit:** `cadence dev --tui` monitors all 15 active microservices with real-time status indicators, live log streaming, and dedicated service restart hotkeys.
2. **Instant Test Watcher:** `cadence test --watch` automatically detects source code changes and triggers relevant test suites with sub-second latency.
3. **1-Click Auto-Healing:** `cadence doctor --auto-fix` automatically initializes missing SQLite databases and resolves environment drift.
4. **Centralized Test Infrastructure:** `packages/testing` provides standard domain factories (`SubjectFactory`, `ProtocolDefinitionFactory`, `ClinicalObservationFactory`, `AuditLogFactory`, `ConsentRecordFactory`, `QueryDiscrepancyFactory`), in-memory repository fakes (`InMemoryRepository`), and mock security context generators.

---

## 5. Consequences & Trade-offs

- **Positive:** Drastically accelerated developer feedback loops, unified multi-service orchestration, robust type safety, and zero regression risk across quality gates.
- **Positive:** Elimination of boilerplates in unit tests through centralized domain entity factories.
- **Trade-off:** Requires maintaining `packages/testing` in sync with domain models.

---

## 6. Implementation & Verification

- **Packages Modified/Added:**
  - `packages/cli/commands/dev.py` (TUI dashboard & process supervisor)
  - `packages/cli/commands/test.py` (File watcher & fast testing)
  - `packages/cli/commands/doctor.py` (Auto-healing diagnostics)
  - `packages/cli/commands/fix.py` (Comprehensive repository synchronization)
  - `packages/testing/` (Centralized test factories, fakes, and security helpers)
  - `scripts/validate_path_patterns.py` (Approved `.cadence/` subdirectory)
- **Verification:**
  - Validated with `uv run cadence check` (10/10 quality gates passing).
  - Validated with `uv run pytest packages/cli/tests/ packages/testing/tests/`.
