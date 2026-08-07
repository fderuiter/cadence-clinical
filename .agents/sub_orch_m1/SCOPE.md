# Scope: Milestone M1 — Foundational Core Utilities Migration

## Architecture
Relocating infrastructure and GxP utility modules out of `packages/core-models` into dedicated package domains (`packages/database`, `packages/security`, `packages/storage`).

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | Move audit.py | Relocate `Part11AuditMixin`, `AuditFields` to `packages/database/audit.py` | M1 | PROJECT.md |
| 2 | Move datetime_helpers.py | Relocate `datetime_helpers.py` to `packages/database/datetime_helpers.py` or `packages/security/datetime_helpers.py` | M1 | PROJECT.md |
| 3 | Move signature.py | Relocate `SigningReason`, `ApprovalStatus`, `SignatureManifestation` to `packages/security/signature.py` | M1 | PROJECT.md |
| 4 | Move storage/ | Relocate `storage/` directory to `packages/storage/` | M1 | PROJECT.md |
| 5 | Update Imports | Update all import statements across `apps/` and `packages/` referencing moved files | M1 | PROJECT.md |
| 6 | Verification | Ensure `uv run ruff check .`, `uv run ruff format .`, and pytest pass cleanly | M1 | PROJECT.md |

## Milestones Status
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | Core Utilities Migration | Relocate core utils out of `packages/core-models` & update imports | None | IN_PROGRESS |

## Interface Contracts
### `packages/database/audit.py`
- Exports `Part11AuditMixin`, `AuditFields`

### `packages/security/signature.py`
- Exports `SigningReason`, `ApprovalStatus`, `SignatureManifestation`

### `packages/storage/`
- Storage service abstractions and providers relocated from `packages/core-models/storage/`

## Code Layout
- Target package locations:
  - `packages/database/audit.py`
  - `packages/database/datetime_helpers.py` (or `packages/security/datetime_helpers.py`)
  - `packages/security/signature.py`
  - `packages/storage/`
- Update `packages/*/pyproject.toml` or `__init__.py` as needed for proper package exports.
