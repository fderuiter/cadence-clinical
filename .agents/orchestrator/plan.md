# Project Execution Plan: Eradicate `packages/core-models` & Implement ACLs

## Overview
This plan governs the architectural refactoring to eliminate `packages/core-models`, migrate domain models to their owning services (`src/domain/`), and establish Anti-Corruption Layers (ACLs) using local Pydantic DTOs for cross-service communication.

## Phases

### Phase 0: Discovery & Survey (Current)
- Dispatch 3 parallel Explorer subagents to:
  - **Explorer 1**: Analyze all files, models, and exports in `packages/core-models`. Determine canonical service ownership (e.g. `execution`, `designer`, `gateway`, `ctms`, etc.) for every model.
  - **Explorer 2**: Map all import sites of `packages/core-models` across all microservices and packages in the codebase.
  - **Explorer 3**: Audit existing cross-service model dependencies and database model sharing to design Anti-Corruption Layer (ACL) Pydantic DTO contracts.
- Consolidate findings into `PROJECT.md` at root.

### Phase 1: Milestone Decomposition & Track Setup
- Define independent milestones for moving models and refactoring import sites service by service.
- Setup Parallel Dual-Track:
  - **Implementation Track**: Migrate models, add ACL DTOs, update routers/services, clean up `packages/core-models`.
  - **E2E / Integration Verification Track**: Ensure full test suite passes (`uv run pytest -n auto`), ruff lint/format cleanly, and GxP documentation is synced (`scripts/sync_gxp.py`).

### Phase 2: Execution & Gated Verification
- For each milestone: Explorer → Worker → Reviewer → Challenger → Forensic Auditor (`teamwork_preview_auditor`).
- Enforce strict GxP sync and AGENTS.md rules.

### Phase 3: Final Verification & Completion Report
- Verify `packages/core-models` is completely deleted.
- Verify no direct cross-service database model imports remain.
- Run complete test suite and GxP compliance check.
- Report results to Sentinel.
