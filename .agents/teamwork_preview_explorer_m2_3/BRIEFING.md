# BRIEFING — 2026-08-07T14:50:00Z

## Mission
Investigate and map source files, model classes/functions, import sites, target destinations, and dependency risks for Notifications (`notifications`), Organization (`organization_domain`), and Interop (`sync_engine`) models in `packages/core-models/`.

## 🔒 My Identity
- Archetype: explorer
- Roles: Teamwork explorer
- Working directory: /Users/fred/Code/cadence-clinical/.agents/teamwork_preview_explorer_m2_3/
- Original parent: 34f7436c-be3f-4037-9a01-5d758d8a7573
- Milestone: m2 (Decompose core-models package into domain apps)

## 🔒 Key Constraints
- Read-only investigation — do NOT modify any source code files.
- Deliver analysis report (`analysis.md`) and handoff report (`handoff.md`) to working directory.
- Send completion message to parent (`34f7436c-be3f-4037-9a01-5d758d8a7573`).

## Current Parent
- Conversation ID: 34f7436c-be3f-4037-9a01-5d758d8a7573
- Updated: 2026-08-07T14:50:00Z

## Investigation State
- **Explored paths**:
  - `packages/core-models/notifications/` (`__init__.py`, `event_models.py`)
  - `packages/core-models/organization_domain/` (`__init__.py`, `models.py`)
  - `packages/core-models/sync_engine.py`
  - Import sites across `apps/`, `packages/`, `scripts/`, `tests/`
- **Key findings**:
  - Notifications models (`SystemDomainEvent`, `NotificationDispatchJob`) -> target `apps/notifications/src/domain/`
  - Organization models (`OrganizationType`, `ClinicalStaffRole`, `TrialDuty`) -> target `apps/org/src/domain/`
  - Interop models (`SignatureValidationError`, `SyncMetadata`, `SyncRecord`, functions) -> target `apps/interop/src/domain/`
  - Identified cross-service runtime import in `apps/ctms/main.py:2551` (`import sync_engine`) and cross-package import in `packages/security/delegation.py:9` (`from organization_domain import ClinicalStaffRole`).
- **Unexplored areas**: None (Scope complete).

## Key Decisions Made
- Written detailed `analysis.md` and standard 5-component `handoff.md`.

## Artifact Index
- /Users/fred/Code/cadence-clinical/.agents/teamwork_preview_explorer_m2_3/DISPATCH.md — Task dispatch
- /Users/fred/Code/cadence-clinical/.agents/teamwork_preview_explorer_m2_3/BRIEFING.md — Working state briefing
- /Users/fred/Code/cadence-clinical/.agents/teamwork_preview_explorer_m2_3/progress.md — Execution heartbeat
- /Users/fred/Code/cadence-clinical/.agents/teamwork_preview_explorer_m2_3/analysis.md — Comprehensive mapping & risk analysis
- /Users/fred/Code/cadence-clinical/.agents/teamwork_preview_explorer_m2_3/handoff.md — Handoff report
