# ADR-141: Modernize Python 3.10 Type Hint Syntax and Lock Ruff Rule Enforcements

* **Status:** Accepted
* **Date:** 2026-07-31
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Legacy Python typing constructs (`typing.List`, `typing.Dict`, `typing.Optional`, `typing.Union`) introduce verbose boilerplate and diverge from modern Python 3.10+ typing standards (PEP 585 built-in generic collections and PEP 604 union syntax `A | B`). Modernizing type hints across `packages/security/` and `pyproject.toml` ensures static analysis precision and strict Ruff rule enforcement.

## 2. Decision Drivers & Constraints

* Enforce PEP 585 (`list[...]`, `dict[...]`) and PEP 604 (`T | None`) across `packages/security/` and service modules.
* Lock UP (pyupgrade) rules in `pyproject.toml` to prevent typing syntax regressions.
* System requirement compliance: PRD-SYS-001.

## 3. Options Considered

1. **Modern Python 3.10+ PEP 585/604 Syntax Lock (Selected)**: Refactor legacy imports to native syntax and enable Ruff UP linter rules globally.
2. Maintain legacy `typing.Optional` and `typing.List` syntax.

## 4. Decision Outcome

Chosen option 1 because built-in generic collections improve readability, reduce import overhead, and integrate cleanly with Pydantic v2 strict typing rules.

## 5. Consequences & Trade-offs

* **Positive**: Reduced typing import overhead and consistent code style across all `packages/security/` modules.
* **Positive**: Enforced via `uv run ruff check .` in CI pipelines.
* **Negative**: Requires modern Python 3.10+ runtime support.

## 6. Implementation & Verification

* Refactored `packages/security/`, `apps/gateway/`, and `apps/execution/`.
* Updated `pyproject.toml` and verified using `uv run ruff check .` and `uv run pytest`.
