# Scope: Milestone M3 — Execution Service Domain Migration

## Architecture
- Relocate all domain models from `packages/core-models/execution/` (including offline models, ePRO, safety, SDTM, trial lock) into `apps/execution/src/domain/`.
- Update all import paths across `apps/`, `packages/`, `scripts/`, and `tests/` to import execution domain models from `apps.execution.src.domain...` instead of `packages.core_models.execution...`.
- Ensure no dangling imports or sys.path hacks remain.

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 8 | Execution Domain Models Migration | Move `execution/` offline models, ePRO, safety, SDTM, trial lock to `apps/execution/src/domain/` | M3 | survey_1 |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M3 | Execution Service Domain Migration | Relocate domain models for `execution` to `apps/execution/src/domain/` | M1, M2 | IN_PROGRESS |

## Interface Contracts
- Domain models owned by Execution Service are housed in `apps/execution/src/domain/`.
- Imports within execution service and test suite must reference `apps.execution.src.domain...`.
