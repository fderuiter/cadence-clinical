# ADR-251: Enforce Ruff Style and Formatting on API Gateway Routers

- **Status:** Accepted
- **Date:** 2026-08-02
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To maintain code quality and prevent technical debt across the platform services, all services must adhere to strict linting, style, and formatting rules. The API gateway, OIDC authentication modules, and endpoints located in `apps/gateway/` must meet these style rules (Ruff check, Ruff formatting) to ensure long-term stability and reliability under PRD-SYS-001.

## 2. Decision Drivers & Constraints

- Ensure all imports inside the API gateway routers are correctly ordered and formatted.
- Ensure unused imports are removed from critical gateway routes to minimize bundle size and security footprint.
- Adhere to PRD-SYS-001 requirements for clean, maintainable systems.

## 3. Options Considered

1. Automatically format and fix all import and style violations across the API gateway using Ruff.
2. Manually suppress lint warnings using inline comments.

## 4. Decision Outcome

Chosen option: Option 1, because automatically formatting and fixing imports using the central Ruff configuration ensures codebase consistency, ease of maintainability, and alignment with general repo-wide checks without adding unnecessary bypass configurations.

## 5. Consequences & Trade-offs

- Positive: The code is consistently styled and easy to read. Unused variables and imports are kept clean.
- Negative: Import ordering is strictly enforced by the Ruff utility, which requires format runs to maintain compliance.

## 6. Implementation & Verification

- Corrected import styles and unused imports inside `apps/gateway/main.py` and `apps/gateway/routers/ecoa.py`.
- Validated that `pnpm check` and `pnpm lint` pass successfully locally.
