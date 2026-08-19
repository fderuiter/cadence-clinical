# 02: [Sponsor Designer] Protocol Authoring, SoA Matrix & USDM Export Polish

**What to build:**
A full-width, high-density Schedule of Activities (SoA) matrix editor (`SoAMatrixEditor.vue`) with responsive cell toggling, Biomedical Concept (BC) CDASH/SDTM variable mapping panel (`IECriteriaTable.vue` and `DesignerSchemaPanel.vue`), Inclusion/Exclusion criteria configuration, and one-click CDISC USDM v3.0/v4.0 JSON protocol export.

**Blocked by:** 01: [Core/Platform] Multi-Engine CADENCE-101 Hero Study Seeding & Dev Cockpit

**Status:** ready-for-agent

## Context & User Story
As a Sponsor Designer, I want to open `CADENCE-101` in the MDR authoring workspace, edit encounter-to-activity mappings on the high-density SoA matrix, inspect standardized Biomedical Concepts, and export the valid CDISC USDM JSON structure, so that the study design is finalized and compliant with regulatory clinical standards.

## Acceptance Criteria
- [ ] SoA Matrix editor occupies full viewport width with sticky encounter headers and activity row labels.
- [ ] Toggling an activity-encounter intersection updates the backend study graph via REST API with optimistic UI updates.
- [ ] Biomedical Concepts panel displays CDASH variable mappings (e.g. `VSORRES`, `SYSBP`, `DIABP`) with unit catalogs.
- [ ] Inclusion & Exclusion criteria editor provides real-time syntactic check and severity tagging.
- [ ] "Export USDM" button produces a schema-valid CDISC USDM v3.0/v4.0 JSON package matching `packages/usdm-schemas`.
- [ ] Tests in `apps/designer/tests/` and `apps/web/tests/` pass with zero regressions.
