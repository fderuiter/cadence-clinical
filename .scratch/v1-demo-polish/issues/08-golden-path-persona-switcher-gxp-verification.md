# 08: [Platform / Demo] Top-Bar Persona Switching, Golden Path Walkthrough & GxP Verification

**What to build:**
A polished top-bar Persona Switcher (`AppShell.vue`) enabling seamless role transitions (`sponsor_designer`, `site_crc`, `cra_monitor`, `data_manager`, `auditor`) during live presentations, an end-to-end integration test exercising the full Golden Path walkthrough, and automatic synchronization of the GxP Requirements Traceability Matrix (`uv run cadence gxp sync`).

**Blocked by:**
- 02: [Sponsor Designer] Protocol Authoring, SoA Matrix & USDM Export Polish
- 03: [Sponsor Designer] Semantic Protocol Amendment Diffing & USDM Branching
- 04: [Patient & Site CRC] eConsent, ICF Builder & 21 CFR Part 11 Signature Capture
- 05: [Site CRC] Dynamic Subject Enrollment & eCRF Visit Execution with Edit Checks
- 06: [CRA Monitor] CTMS Monitoring Console, SDV Toggles & Query Discrepancy Lifecycle
- 07: [Data Manager & Auditor] 21 CFR Part 11 Audit Trail, Medical Coding & Dataset Exports

**Status:** ready-for-agent

## Context & User Story
As a Presenter or Reviewer, I want to switch between clinical personas with a single click in the top navigation bar without logging out or losing context, walk through the complete closed-loop lifecycle from study design to eCRF entry to CTMS monitoring to audit inspection, and verify that all GxP compliance artifacts and traceability matrices are 100% in sync.

## Acceptance Criteria
- [ ] Top-bar Persona Switcher in `AppShell.vue` dynamically updates `authStore` roles and redirects to the canonical landing view for each persona.
- [ ] Switching personas preserves the active study context (`CADENCE-101`) across all views.
- [ ] Full end-to-end smoke test passes: Author SoA $\rightarrow$ Sign eConsent $\rightarrow$ Enroll Subject $\rightarrow$ Enter eCRF $\rightarrow$ Verify SDV & Query in CTMS $\rightarrow$ Inspect Audit Trail.
- [ ] `uv run cadence gxp sync` regenerates `docs/SDLC/Requirements_Traceability_Matrix.md` and `docs/SDLC/IQ_OQ_PQ_Execution_Report.md` with 100% test coverage alignment.
- [ ] `uv run cadence check --parallel` passes all quality gates without warnings or failures.
