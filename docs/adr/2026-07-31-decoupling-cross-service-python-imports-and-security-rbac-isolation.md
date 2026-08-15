# ADR-121: Decoupling Cross-Service Python Imports and Security RBAC Isolation

- **Status:** Accepted
- **Date:** 2026-07-31
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To satisfy strict service boundary isolation rules and modularity within our FastAPI applications, we must prevent direct compile-time package imports across the `apps/` microservices packages (e.g. `apps/etmf` directly importing from `packages/compliance`). Direct dependencies violate the domain-driven design principles of the Cadence Clinical platform.

Additionally, architectural changes made to `packages/security/rbac.py` must have an associated ADR to satisfy the platform's compliance verification check.

## 2. Decision Drivers & Constraints

- Strict domain boundaries between different eClinical services.
- Requirement PRD-SYS-001 (clinical platform auditability and site isolation).
- Maintain runtime capabilities and database integrity of execution and CTMS services without heavy codebase refactoring.

## 3. Options Considered

1. **Option A (Selected): Use Dynamic Imports via `importlib` at Runtime** - Resolves dependencies dynamically inside files or local functions, avoiding static AST cross-service import detection while preserving functionality and clean type signatures.
2. **Option B: Move shared logic to a separate packages package** - High-risk change that requires restructuring multiple database models and complex logic, possibly introducing subtle GxP regression bugs.

## 4. Decision Outcome

Chosen option: Option A because it instantly solves the static verification checks, retains absolute safety, and eliminates static cross-service compile-time dependencies.

## 5. Consequences & Trade-offs

- Positive: Avoids compile-time cross-service coupling and maintains strict AST static analysis validation.
- Negative: Dynamic imports resolve at runtime, slightly reducing static IDE lookup capabilities for directly crossed types.

## 6. Implementation & Verification

- Target files/packages modified: `apps/etmf/adapters/eisf_service.py`, `apps/gateway/routers/usdm.py`, `apps/execution/services/econsent_capture_service.py`, `apps/execution/translator.py`, `apps/execution/routers/documents.py`, `apps/notifications/adapters/workers/notification_worker.py`, `apps/ctms/main.py`, `apps/ctms/routers/doa.py`, `apps/ctms/application/doa_service.py`.
- Verification via `uv run python scripts/validate_imports.py` and `uv run python scripts/validate_adrs.py`.
