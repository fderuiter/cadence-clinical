# ADR-2177: Standardized Hexagonal Architecture and Unified Developer CLI

- **Status:** Accepted
- **Date:** 2026-08-14
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirement:** PRD-SYS-001

---

## 1. Context & Problem Statement

As the Cadence Clinical Research Platform scaled across 16 interconnected microservices and 7 shared platform packages, architectural drift, leaky boundaries (e.g. cross-app database imports in test suites), inconsistent local development scripts, and manual database lifecycle workflows emerged.

To achieve enterprise-grade maintainability, zero-downtime microservice decoupling, and the best-in-class developer and AI agent experience (DX), a unified architectural paradigm and developer toolset was required under **PRD-SYS-001**.

## 2. Decision Drivers & Constraints

- **Strict Microservice Isolation (21 CFR Part 11 / GxP)**: Zero direct database or ORM imports across service boundaries. Cross-service operations must route through typed REST clients or HMAC-authenticated driven ports.
- **Developer & Agent Experience (DX)**: Provide a unified, high-performance CLI (`cadence`) with human-centric terminal formatting (Rich tables, progress panels) and machine-readable (`--json`) output for autonomous AI agents.
- **Multi-Engine Database Lifecycle**: Automated management of SQLite, PostgreSQL, and Neo4j database initialization, schema migration, multi-tier clinical scenario seeding (`study_oncology_phase3`), and compressed snapshot/restore workflows.
- **Architectural Sentinels**: Continuous verification of port/adapter structural contracts, cross-service AST imports, Mermaid architecture diagram parity, and automated self-healing.
- **Frontend Design System Standardization**: Shared, vanilla CSS-based enterprise components in `@cadence/ui` (`ClinicalDataTable`, `ClinicalModal`, `PersonaSwitcher`, `AuditLogViewer`) with a searchable VitePress component catalog.

## 3. Options Considered

1. **Option A (Selected): Unified Typer Developer CLI + Hexagonal Kernel Primitives + Dynamic Sentinels**
   - Implements `packages/cli` exposing `cadence` command with subcommand suite (`doctor`, `dev`, `test`, `check`, `fix`, `db`, `scaffold`, `gxp`).
   - Enhances `packages/hexagonal` with domain primitives (`BaseEntity`, `AggregateRoot`, `DomainEvent`, `RepositoryPort`, `UseCasePort`, `ExternalServiceClientPort`).
   - Upgrades `scripts/verify_contracts.py` to dynamically discover and validate all port and adapter contracts with MyPy.
   - Decouples cross-app test imports and eliminates legacy import exemption lists.
2. **Option B: Fragmented Shell Scripts and Ad-Hoc Makefiles**
   - Keeps fragmented Python/Bash scripts in `scripts/`. Lacks unified CLI interface, structured JSON output for AI agents, and centralized database snapshot tooling.

## 4. Decision Outcome

**Chosen option: Option A.**

### Key Architectural Standards Established:

1. **Developer CLI (`cadence`)**: Installed globally and in workspace root, wrapping service supervision, concurrent quality checks, test filtering, and multi-tier database seeding.
2. **Hexagonal Platform Kernel (`packages/hexagonal`)**: Provides base contracts and standard domain exceptions (`DomainError`, `EntityNotFoundError`, `EntityAlreadyExistsError`, `ValidationError`, `ConflictError`).
3. **Dynamic Contract Sentinel (`scripts/verify_contracts.py`)**: Automatically discovers all port interfaces and adapter implementations across `apps/` and `packages/`, ensuring 100% type safety and structural conformance.
4. **Zero Cross-App Database Imports**: All inter-service communications in tests and production use driven ports, HMAC-authenticated HTTP clients, or test mock adapters.
5. **Shared Enterprise UI**: Centralized Vanilla CSS design tokens with accessible components and full-width clinical workspace layouts.

## 5. Consequences & Trade-offs

- **Positive**:
  - Dramatic reduction in onboarding friction and development cognitive load via `cadence doctor`, `cadence dev`, and `cadence fix`.
  - Zero-drift guarantee across microservice boundaries validated on every pre-commit and CI run.
  - Safe, deterministic multi-engine clinical scenario test seeding (`cadence db seed --tier full`).
  - Strict compliance with GxP and 21 CFR Part 11 electronic records separation.
- **Negative**:
  - Requires developers to define driven ports and adapters for newly added cross-service dependencies instead of direct imports.

## 6. Implementation & Verification

- **Packages Created / Updated**:
  - [`packages/cli/`](file:///Users/fred/Code/cadence-clinical/packages/cli/) (`cadence` CLI tool)
  - [`packages/hexagonal/`](file:///Users/fred/Code/cadence-clinical/packages/hexagonal/) (Hexagonal primitives & ports)
  - [`packages/ui/`](file:///Users/fred/Code/cadence-clinical/packages/ui/) (Shared Enterprise UI components)
- **Scripts & Sentinels**:
  - [`scripts/verify_contracts.py`](file:///Users/fred/Code/cadence-clinical/scripts/verify_contracts.py) (Dynamic contract sentinel)
  - [`scripts/validate_imports.py`](file:///Users/fred/Code/cadence-clinical/scripts/validate_imports.py) (Cross-service boundary validator)
- **Documentation & Catalog**:
  - [`docs/cli.md`](file:///Users/fred/Code/cadence-clinical/docs/cli.md) (Developer CLI manual)
  - [`docs/components/`](file:///Users/fred/Code/cadence-clinical/docs/components/) (UI component catalog)
- **Verification Tests**:
  - [`packages/cli/tests/test_cadence_cli.py`](file:///Users/fred/Code/cadence-clinical/packages/cli/tests/test_cadence_cli.py)
  - [`packages/hexagonal/tests/test_hexagonal_kernel.py`](file:///Users/fred/Code/cadence-clinical/packages/hexagonal/tests/test_hexagonal_kernel.py)
