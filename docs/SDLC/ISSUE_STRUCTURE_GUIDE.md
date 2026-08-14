# GitHub Issue Structure, Project Board & Parallel Work Stream Guide

This guide establishes the standardized issue structure, developer readiness badges, parallel work stream taxonomy, GitHub Project Board management, and Definition of Done (DoD) requirements across the **Cadence Clinical** monorepo.

---

## 1. Issue Header & Readiness Badges

Every open GitHub issue in Cadence Clinical is structured with a standardized header block providing instant visual context for developers:

```markdown
🟢 **READY FOR DEV** | **Work Stream**: `Stream 1: eTMF & Regulated Document Management` | **Milestone**: `Document Management & Archiving`

> 💡 **Developer Readiness**: Unblocked leaf task. Ready for immediate developer assignment.
> 🔒 **Requirements Traceability**: `PRD-TMF-001, Trace-5, ADR-049` | GxP 21 CFR Part 11 Regulated
> 📁 **Target Modules / Files**: `apps/etmf/main.py, apps/etmf/lifecycle.py`
```

### Readiness Status Standards

- 🟢 **`READY FOR DEV`**: Leaf task with no active blocking prerequisites. Developers can immediately pick up this issue for parallel work.
- 🔴 **`BLOCKED`**: Prerequisites (listed in header) must be merged into `main` before starting this issue.
- 🔵 **`PARENT EPIC`**: Top-level tracking epic containing the execution graph and dependency order for child issues.

---

## 2. Parallel Work Stream Taxonomy

To enable multiple developers to work concurrently without code collisions, issues are classified into 8 isolated work streams:

| Stream       | Functional Domain                          | Primary Microservices & Modules                                 |
| :----------- | :----------------------------------------- | :-------------------------------------------------------------- |
| **Stream 1** | **eTMF & Regulated Document Management**   | `apps/etmf/`, `packages/core-models/tmf_reference_model/*`      |
| **Stream 2** | **RTSM & IP Supply Chain**                 | `apps/execution/rtsm/*`, `apps/execution/ip_supply/*`           |
| **Stream 3** | **Study Designer, eCRF & SoA**             | `apps/designer/`, `packages/core-models/cdisc/*`                |
| **Stream 4** | **eCOA, ePRO & Subject Portal**            | `apps/subject-portal/*`, `apps/execution/ecoa/*`                |
| **Stream 5** | **Frontend Vue 3 SPA**                     | `apps/web/src/`, `packages/ui/*`                                |
| **Stream 6** | **Platform Security, RBAC & Audit Ledger** | `apps/gateway/`, `packages/security/`, `apps/execution/audit/*` |
| **Stream 7** | **Biostatistics & Dataset Exports**        | `apps/execution/exports/`, `packages/core-models/sdtm/*`        |
| **Stream 8** | **Clinical Operations, SDV & Lab Ranges**  | `apps/execution/sdv/*`, `apps/execution/labs/*`                 |

---

## 3. GitHub Project Board Management (`Cadence-Clinical` Project #17)

The project board is fully synchronized with repository issues and configured with automated status, priority, and size fields:

### Board Column Routing

- **`Ready`**: Contains all 🟢 **`READY FOR DEV`** unblocked leaf tasks. Developers pick work directly from this column.
- **`Backlog`**: Contains 🔴 **`BLOCKED`** tasks waiting on upstream prerequisites and 🔵 **`PARENT EPIC`** tracking issues.
- **`In progress`**: Tasks actively being worked on by developers.
- **`In review`**: Tasks with open Pull Requests undergoing Gate 1-3 verification.
- **`Done`**: Completed issues merged into `main`.

### Maintainability Automation Script

To maintain the project board as new issues are created or status changes:

```bash
uv run python scripts/sync_github_project.py
```

This script automatically:

1. Adds any newly opened GitHub issues to Project 17 (`Cadence-Clinical`).
2. Syncs board `Status` (`Ready`, `Backlog`, `Done`).
3. Classifies `Priority` (`P0`, `P1`, `P2`) based on clinical risk and label severity.
4. Estimates `Size` (`XS`, `S`, `M`, `L`, `XL`) based on targeted module scope.

---

## 4. Definition of Done (DoD) Checklist

All Pull Requests (PRs) must satisfy the 5-point Definition of Done before merging into `main`:

```markdown
## 📋 Definition of Done (DoD) Checklist

- [ ] Implementation complete across target file paths.
- [ ] Unit & integration tests added/updated in `tests/` (`uv run pytest`).
- [ ] Code formatted and typed cleanly (`uv run ruff check .`).
- [ ] GxP audit fields preserved/updated (`created_by`, `reason_for_change`, versioning) if models modified.
- [ ] Traceability docs or ADR updated if architectural/contract changes introduced.
```

---

## 5. 3-Tier Cascade Protocol Compliance

Every issue must cite its governing requirement identifiers:

1. **Requirements Level**: `PRD-SYS-xxx`, `PRD-EDC-xxx`, or `Trace-x`.
2. **Architecture Level**: `ADR-xxx` (created via `uv run python scripts/create_adr.py` for architectural changes).
3. **Traceability Level**: RTM entry in `docs/SDLC/Requirements_Traceability_Matrix.md`.
