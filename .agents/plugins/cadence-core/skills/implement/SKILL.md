---
name: implement
description: "Implement a feature, bug fix, or refactor with strict adherence to Cadence coding standards, GxP traceability, and automated quality gates. Use when implementing approved plans, specs, or tickets."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Implementation Workflow

Executes code implementation tasks with strict adherence to repository standards, 21 CFR Part 11 requirements, and Antigravity artifact reporting.

---

## 1. Pre-Implementation Review
- Read the relevant spec, ticket, or plan.
- Check `CONTEXT.md` for canonical domain terminology.
- Identify the target directories:
  - `apps/designer/`: Study authoring / MDR / Neo4j logic.
  - `apps/execution/`: Data capture / eCRF / PostgreSQL logic.
  - `apps/gateway/`: OIDC Auth & API routers.
  - `apps/web/`: Frontend Vue 3 views (Vanilla CSS design tokens, no Tailwind).
  - `packages/`: Reusable cross-app packages (`packages.testing`, `packages.security`).

---

## 2. Implementation Execution
- Make surgical, cohesive edits using `replace_file_content` or `write_to_file`.
- Follow critical patterns:
  - **E712 Boolean Filters**: Always use `.is_(True)` / `.is_(False)` in SQLAlchemy queries.
  - **I001 Import Ordering**: Standard library → third-party → first-party, strictly sorted alphabetically.
  - **PEP 695 Generics**: Use native syntax (`class Repository[T]: ...`).
  - **Frontend Tokens**: Use semantic CSS variables (`var(--primary)`, `var(--surface)`).
  - **@req: Docstrings**: Add requirement tags (`@req:PRD-SYS-xxx`) to all new/modified test functions.

---

## 3. Mandatory Quality Gate Sequence

Before concluding any implementation turn, execute the four quality gates:

```bash
# 1. Auto-remediate formatting and import ordering
uv run cadence fix --all

# 2. Run fast unit and integration tests
uv run cadence test --fast

# 3. Run all 10 architecture and compliance sentinels
uv run cadence check --parallel

# 4. Sync GxP Traceability Matrix & Execution Report
uv run python scripts/sync_gxp.py
```

---

## 4. Produce Antigravity Walkthrough Artifact

Record changes in `<appDataDir>/brain/<conversation-id>/walkthrough.md`:
- Multi-component summary of files created/modified with clickable `[basename](file:///path/to/file)` links.
- Verification results table showing test outcomes and sentinel checks.
- Key GxP requirement IDs satisfied.
